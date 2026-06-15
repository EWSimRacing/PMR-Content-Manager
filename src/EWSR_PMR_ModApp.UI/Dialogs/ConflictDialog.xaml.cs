using System.Windows;

namespace EWSR_PMR_ModApp.UI.Dialogs;

/// <summary>
/// Calm, informational notice shown when a freshly installed mod shares one or
/// more files with an already-installed mod. Conflicts are <b>not</b> errors:
/// the file-level overlay means the most recently installed mod wins, so this
/// dialog explains the situation in plain language instead of alarming the user.
/// </summary>
public partial class ConflictDialog : Window
{
    /// <summary>A single "this mod vs. that mod" conflict grouping for display.</summary>
    public sealed record ConflictGroup(
        string NewModName,
        string OtherModName,
        IReadOnlyList<string> Files);

    public ConflictDialog(string newModName, IReadOnlyList<ConflictGroup> conflicts)
    {
        InitializeComponent();

        int fileCount = conflicts.Sum(c => c.Files.Count);
        int modCount  = conflicts.Count;

        SubtitleText.Text =
            $"'{newModName}' shares {fileCount} file(s) with " +
            $"{modCount} other installed mod{(modCount == 1 ? "" : "s")}.";

        ConflictList.ItemsSource = conflicts;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
