namespace FeatherScribe.App;

/// <summary>
/// Which result actions can run now. MainWindow buttons and the tray menu both use this,
/// so an action is never offered when it would only fail.
/// </summary>
internal readonly record struct ActionAvailability(bool CanCopy, bool CanRepaste, bool CanReformat, bool CanAdopt)
{
    public static ActionAvailability From(DictationController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var hasLatestResult = controller.HasLatestResult;
        return new ActionAvailability(
            CanCopy: hasLatestResult,
            CanRepaste: hasLatestResult,
            CanReformat: controller.CanReformat,
            CanAdopt: controller.HasRejectedCandidate);
    }
}
