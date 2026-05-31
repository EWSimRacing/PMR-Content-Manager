namespace EWSR_PMR_ModApp.Core.Manifest;

/// <summary>Indicates how a mod file's target path under the game data root was determined.</summary>
public enum MappingMethod
{
    /// <summary>The zip preserved folder structure that was overlaid onto the data root.</summary>
    PathOverlay,

    /// <summary>Target resolved by matching the filename against an index of existing data files.</summary>
    FilenameMatch,

    /// <summary>Target explicitly declared in a <c>modinfo.json</c> inside the zip.</summary>
    ModInfo
}
