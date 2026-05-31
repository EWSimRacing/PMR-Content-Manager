namespace EWSR_PMR_ModApp.Core.GameDetection;

/// <summary>Indicates how the game data root was located.</summary>
public enum LocationSource
{
    /// <summary>Path was explicitly provided by the user in app settings.</summary>
    UserConfigured,

    /// <summary>Path was found at the hard-coded default install location.</summary>
    DefaultPath,

    /// <summary>Path was located by parsing Steam's libraryfolders.vdf or registry.</summary>
    SteamDetected,

    /// <summary>Path could not be determined; the user must select it manually.</summary>
    NotFound
}
