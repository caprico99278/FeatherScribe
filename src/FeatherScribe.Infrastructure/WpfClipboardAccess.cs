using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FeatherScribe.Infrastructure;

/// <summary>
/// Real clipboard access via System.Windows.Clipboard on dedicated STA threads, plus the Win32 clipboard
/// sequence number used to detect changes made by the user or other apps.
/// The clipboard may be locked by another process, so writes are retried.
/// </summary>
public sealed class WpfClipboardAccess : IClipboardAccess
{
    private const int MaxAttempts = 10;
    private const int RetryDelayMilliseconds = 50;

    // Formats captured for restoreClipboard, in the order they are written back.
    private static readonly string[] SupportedFormats =
    [
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.Rtf,
        DataFormats.Html,
        DataFormats.CommaSeparatedValue,
        DataFormats.FileDrop,
        DataFormats.Bitmap,
    ];

    public Task<ClipboardSnapshot> CaptureSnapshotAsync() => RunOnStaThreadAsync(CaptureOnStaThread);

    public Task SetTextAsync(string text) => RunOnStaThreadAsync(() => SetClipboardTextWithRetry(text));

    public Task<uint> SetTextAndGetSequenceAsync(string text) => RunOnStaThreadAsync(() =>
    {
        SetClipboardTextWithRetry(text);
        return GetClipboardSequenceNumber();
    });

    public uint GetSequenceNumber() => GetClipboardSequenceNumber();

    public Task<bool> RestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence) =>
        RunOnStaThreadAsync(() => RestoreOnStaThread(snapshot, expectedSequence));

    public Task<CapturedSelection> ReadSelectionAsync() => RunOnStaThreadAsync(ReadSelectionOnStaThread);

    // One STA call: the text and the VSCode empty-selection marker come from the same data object.
    // Clipboard errors (after the open retries) propagate; the caller maps them to its abort flow.
    private static CapturedSelection ReadSelectionOnStaThread()
    {
        var data = GetDataObjectWithRetry();
        if (data is null)
        {
            return new CapturedSelection(null, IsFromEmptySelection: false);
        }

        var text = data.GetDataPresent(DataFormats.UnicodeText, autoConvert: true)
            ? data.GetData(DataFormats.UnicodeText, autoConvert: true) as string
            : null;
        var formats = data.GetFormats(autoConvert: false) ?? [];
        var fromEmptySelection = VsCodeClipboardMarker.IsFromEmptySelection(
            formats,
            format => data.GetData(format, autoConvert: false));

        return new CapturedSelection(text, fromEmptySelection);
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (COMException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }

    private static ClipboardSnapshot CaptureOnStaThread()
    {
        try
        {
            var source = GetDataObjectWithRetry();
            var presentFormats = source?.GetFormats(autoConvert: false) ?? [];
            if (source is null || presentFormats.Length == 0)
            {
                return ClipboardSnapshot.Empty;
            }

            var copy = new DataObject();
            var formatCount = 0;
            long textChars = 0;
            var bitmapWidth = 0;
            var bitmapHeight = 0;

            foreach (var format in SupportedFormats)
            {
                if (!presentFormats.Contains(format, StringComparer.Ordinal))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = source.GetData(format, autoConvert: false);
                }
                catch (Exception)
                {
                    // Skip a format whose data cannot be read; the others may still be restorable.
                    continue;
                }

                switch (value)
                {
                    case string text:
                        textChars += text.Length;
                        break;
                    case string[] paths:
                        textChars += paths.Sum(p => (long)(p?.Length ?? 0));
                        break;
                    case MemoryStream stream:
                        textChars += stream.Length;
                        break;
                    case BitmapSource bitmap:
                        bitmapWidth = bitmap.PixelWidth;
                        bitmapHeight = bitmap.PixelHeight;
                        break;
                    default:
                        continue;
                }

                // Checked before copying so an oversized clipboard is never duplicated in memory.
                if (!ClipboardRestorePolicy.IsWithinSizeLimit(textChars, bitmapWidth, bitmapHeight))
                {
                    return ClipboardSnapshot.Unsupported(ClipboardSnapshot.ReasonTooLarge);
                }

                object captured = value switch
                {
                    string[] paths => paths.ToArray(),
                    MemoryStream stream => new MemoryStream(stream.ToArray(), writable: false),
                    BitmapSource bitmap => CopyFrozen(bitmap),
                    _ => value,
                };

                copy.SetData(format, captured, autoConvert: false);
                formatCount++;
            }

            return formatCount == 0
                ? ClipboardSnapshot.Unsupported(ClipboardSnapshot.ReasonUnsupportedFormats)
                : ClipboardSnapshot.Captured(copy, formatCount);
        }
        catch (Exception)
        {
            // Locked clipboard, broken source app, out of memory...: never fail the paste over the snapshot.
            return ClipboardSnapshot.Unsupported(ClipboardSnapshot.ReasonCaptureFailed);
        }
    }

    private static IDataObject? GetDataObjectWithRetry()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Clipboard.GetDataObject();
            }
            catch (COMException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }

    // The source bitmap belongs to the capture thread; the restore runs on another STA thread,
    // so keep a frozen, thread-independent pixel copy.
    private static BitmapSource CopyFrozen(BitmapSource source)
    {
        var stride = ((source.PixelWidth * source.Format.BitsPerPixel) + 7) / 8;
        var pixels = new byte[(long)stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX,
            source.DpiY,
            source.Format,
            source.Palette,
            pixels,
            stride);
        copy.Freeze();
        return copy;
    }

    private static bool RestoreOnStaThread(ClipboardSnapshot snapshot, uint expectedSequence)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Re-check right before every write: a newer clipboard change always wins.
            if (expectedSequence == 0 || GetClipboardSequenceNumber() != expectedSequence)
            {
                return false;
            }

            try
            {
                switch (snapshot.Kind)
                {
                    case SnapshotKind.Empty:
                        Clipboard.Clear();
                        return true;
                    case SnapshotKind.Captured when snapshot.Payload is IDataObject data:
                        Clipboard.SetDataObject(data, copy: true);
                        return true;
                    default:
                        return false;
                }
            }
            catch (COMException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }

    private static Task RunOnStaThreadAsync(Action action) =>
        RunOnStaThreadAsync(() =>
        {
            action();
            return true;
        });

    private static Task<T> RunOnStaThreadAsync<T>(Func<T> function)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(function());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
