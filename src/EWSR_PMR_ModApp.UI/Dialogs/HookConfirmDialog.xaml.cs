using System.Windows;

namespace EWSR_PMR_ModApp.UI.Dialogs;

/// <summary>
/// Presents a mod lifecycle hook script to the user and asks whether to run it.
/// Supports both post-install and post-uninstall hooks.
/// Returns <c>true</c> (Run) or <c>false</c> (Skip) via <see cref="Window.DialogResult"/>.
/// </summary>
public partial class HookConfirmDialog : Window
{
    public HookConfirmDialog(
        string  modName,
        string  scriptName,
        string? description,
        bool    requiresElevation,
        bool    isPostInstall)
    {
        InitializeComponent();

        string hookPhase  = isPostInstall ? "Post-Install" : "Post-Uninstall";
        string actionWord = isPostInstall ? "after installation" : "after uninstall";

        Title             = $"{hookPhase} Script — {modName}";
        TitleText.Text    = $"{hookPhase} Script";
        SubtitleText.Text = $"\"{modName}\" includes a script that can run {actionWord}.";
        ScriptNameText.Text = scriptName;
        RunButton.Content = isPostInstall ? "Run Script →" : "Run Cleanup →";

        if (!string.IsNullOrWhiteSpace(description))
            DescriptionText.Text = description;
        else
            DescriptionText.Visibility = Visibility.Collapsed;

        if (requiresElevation)
            ElevationBorder.Visibility = Visibility.Visible;
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
