namespace EWSR_PMR_ModApp.Core.ZipHandling;

public enum SkipCategory
{
    Install,             // File will be installed to the game
    DisplayOnly,         // Show in UI (README, preview), do not install
    NoPathMatch,         // No mapping to any game data path
    MetaFile,            // modinfo.json itself — parsed, not installed
    HashMatch,           // Already up-to-date on disk (identical hash)
    UserExcluded,        // User blocked this file
    AmbiguousPending,    // Multiple possible targets — needs user confirmation
    Collision,           // Two zip entries target same destination
    UnsafeFile           // Executable or system file — never auto-install
}
