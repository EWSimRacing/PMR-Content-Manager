using System.Windows;

namespace EWSR_PMR_ModApp.UI.Dialogs;

/// <summary>
/// Displays install warnings (collisions, skipped files, etc.) as a simple list.
/// Collisions are NOT auto-installed; this dialog makes that visible.
/// </summary>
public partial class WarningsDialog : Window
{
    public WarningsDialog(string operationName, IReadOnlyList<string> warnings)
    {
        InitializeComponent();

        TitleText.Text        = $"Warnings — {operationName}";
        WarningsList.ItemsSource = warnings;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
