using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.UI.Infrastructure;

namespace EWSR_PMR_ModApp.UI.Dialogs;

/// <summary>
/// Represents one ambiguous mapping item the user must resolve:
/// pick a target path from the candidates or skip the file.
/// </summary>
public sealed class AmbiguousMappingItemViewModel : ViewModelBase
{
    private string _selectedOption;

    public AmbiguousMappingItemViewModel(AmbiguousMapping mapping)
    {
        ZipFileName = mapping.ZipEntry.FileName;
        ZipPath     = mapping.ZipEntry.FullNameInZip;
        Reason      = mapping.Reason;
        ZipEntry    = mapping.ZipEntry;

        // First option is always "skip".
        Options = new List<string> { "(Skip this file)" };
        Options.AddRange(mapping.CandidatePaths);
        _selectedOption = Options[0];
    }

    public string       ZipFileName    { get; }
    public string       ZipPath        { get; }
    public string       Reason         { get; }
    public List<string> Options        { get; }

    internal Core.ZipHandling.ZipEntryInfo ZipEntry { get; }

    public string SelectedOption
    {
        get => _selectedOption;
        set => SetField(ref _selectedOption, value);
    }

    public ResolvedMapping ToResolved()
    {
        if (_selectedOption == "(Skip this file)")
            return new ResolvedMapping { ZipEntry = ZipEntry, Skip = true };

        return new ResolvedMapping
        {
            ZipEntry                = ZipEntry,
            ChosenRelativeTargetPath = _selectedOption
        };
    }
}
