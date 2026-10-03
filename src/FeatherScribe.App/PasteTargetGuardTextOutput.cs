using FeatherScribe.Core;

namespace FeatherScribe.App;

/// <summary>
/// Pipeline output decorator that never sends Ctrl+V into FeatherScribe's own window.
/// When FeatherScribe is in the foreground at paste time (e.g. MainWindow was opened while
/// recording), the previous input target is restored first. If that is not possible the text
/// is only copied to the clipboard and the output is reported as failed, so the pipeline
/// returns OutputSucceeded=false and the user is told the paste did not happen.
/// </summary>
internal sealed class PasteTargetGuardTextOutput : ITextOutput
{
    private readonly ITextOutput _inner;
    private readonly Func<bool> _isOwnProcessForeground;
    private readonly Func<bool> _tryRestoreExternalTarget;

    public PasteTargetGuardTextOutput(
        ITextOutput inner,
        Func<bool> isOwnProcessForeground,
        Func<bool> tryRestoreExternalTarget)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(isOwnProcessForeground);
        ArgumentNullException.ThrowIfNull(tryRestoreExternalTarget);
        _inner = inner;
        _isOwnProcessForeground = isOwnProcessForeground;
        _tryRestoreExternalTarget = tryRestoreExternalTarget;
    }

    public async Task OutputAsync(string text, OutputMode mode, CancellationToken cancellationToken)
    {
        if (mode != OutputMode.ClipboardAndPaste ||
            !_isOwnProcessForeground() ||
            _tryRestoreExternalTarget())
        {
            await _inner.OutputAsync(text, mode, cancellationToken).ConfigureAwait(false);
            return;
        }

        // No external target: keep the text on the clipboard, but never paste into our own window.
        await _inner.OutputAsync(text, OutputMode.ClipboardOnly, cancellationToken).ConfigureAwait(false);
        throw new PasteTargetUnavailableException();
    }
}
