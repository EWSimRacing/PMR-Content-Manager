# EWSR_PMR_ModApp

A Windows desktop mod manager for **Project Motor Racing**. Drop a mod `.zip` and the app installs files into the correct game directories — and re-applies them when game updates revert your mods.

Inspired by [AMS2 Content Manager](https://www.overtake.gg/).

## Stack

- **Language:** C# / .NET 10
- **UI:** WPF (Windows Presentation Foundation)
- **Architecture:** Core library (engine) + UI shell (thin WPF app)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)
- Windows 10/11

## Build & Run

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run the app
dotnet run --project src/EWSR_PMR_ModApp.UI
```

## Project Structure

```
EWSR_PMR_ModApp.sln
src/
  EWSR_PMR_ModApp.Core/       # Engine library (no UI dependency)
    SyncEngine/                # Mod install/uninstall/re-apply orchestration
    ZipHandling/               # Zip validation & extraction
    GameDetection/             # Locate game install path (Steam/registry)
    Manifest/                  # Track installed mods & file hashes
    Backup/                    # Backup/restore original game files
  EWSR_PMR_ModApp.UI/         # WPF desktop application
docs/
  ARCHITECTURE.md             # Module breakdown and design notes
```

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full module breakdown and planned work.

## License

TBD
