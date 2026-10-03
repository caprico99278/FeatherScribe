using FeatherScribe.Core;

namespace FeatherScribe.App;

/// <summary>
/// Thrown by <see cref="PasteTargetGuardTextOutput"/> when no external paste target could be restored.
/// The text has already been copied to the clipboard (ClipboardOnly) before this is thrown, so the
/// user-facing text may say the result is on the clipboard. The message is the event log ErrorType
/// ("paste target unavailable") and must stay the same for log metadata compatibility.
/// </summary>
internal sealed class PasteTargetUnavailableException : InvalidOperationException
{
    public const string ErrorText = "paste target unavailable";

    public PasteTargetUnavailableException()
        : base(ErrorText)
    {
    }

    /// <summary>True when the dictation output failed only because no paste target was available.</summary>
    public static bool IsCauseOf(PipelineResult result)
        => !result.OutputSucceeded
            && string.Equals(result.OutputErrorMessage, ErrorText, StringComparison.Ordinal);
}
