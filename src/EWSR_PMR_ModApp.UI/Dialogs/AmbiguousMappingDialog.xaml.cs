using System.Windows;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.UI.Dialogs;

/// <summary>
/// Modal dialog that lets the user resolve ambiguous file-to-game-path mappings
/// before an install proceeds. Each row shows a zip entry with a ComboBox to pick
/// the correct target path (or skip).
/// </summary>
public partial class AmbiguousMappingDialog : Window
{
    private readonly List<AmbiguousMappingItemViewModel> _items;

    public AmbiguousMappingDialog(IReadOnlyList<AmbiguousMapping> ambiguous)
    {
        InitializeComponent();

        _items = ambiguous
            .Select(a => new AmbiguousMappingItemViewModel(a))
            .ToList();

        ItemsHost.ItemsSource = _items;
    }

    /// <summary>Returns the user's resolutions after the dialog closes.</summary>
    public IReadOnlyList<ResolvedMapping> GetResolutions() =>
        _items.Select(i => i.ToResolved()).ToList();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SkipAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.SelectedOption = item.Options[0]; // "(Skip this file)"

        DialogResult = true;
        Close();
    }
}
