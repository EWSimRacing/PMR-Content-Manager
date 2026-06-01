# History: Slit — UI Dev (Summary)

## Overview
Slit is the UI developer for EWSR_PMR_ModApp. Domain: drop-zone UI, installed mods list, install status, settings screen. Full history available in archived version.

## Key Milestones (2026-05-31)

### WPF UI Implementation & Setup
- Full WPF shell built: MainWindow, drop zone, mod list, settings, toolbar
- DI container setup (App.xaml.cs)
- Infrastructure: ViewModelBase, RelayCommand, AsyncRelayCommand, BoolToVisibilityConverter
- Catppuccin Mocha palette applied

### ProgressBar Binding Bug Fix
- TwoWay binding on ProgressBar.Value caused XamlParseException at startup
- Fixed by setting Mode=OneWay; audited all XAML bindings
- Lesson: XAML binding errors are runtime-only; always launch-test after UI changes

### Drag-and-Drop Resolution (3 rounds)
1. **Round 1:** Missing ChangeWindowMessageFilterEx for elevated mode → added P/Invoke calls
2. **Round 2:** Handlers not wired to DropZoneBorder (only on Window) → attached handlers to drop target
3. **Round 3:** Decorative TextBlocks were hit-testable → added IsHitTestVisible="False" to center content
   - **Root cause:** Decorative TextBlocks captured hit-test before Border could receive drag events
   - **Result:** Drag-drop now works both non-elevated and elevated
   - Diagnostic logging (gated #if DEBUG) captured the issue
   - ChangeWindowMessageFilterEx calls retained for completeness

### Elevation Broker Wiring (Phase 2)
- Integrated non-elevated-UI + elevated-helper architecture
- All install/uninstall/reapply operations flow through Prepare → WritePlanRequest → IElevatedWriter
- Manifest orchestration complete

### Home Button & App Logo (Latest)
- HomeCommand: RelayCommand that sets IsSettingsVisible = false
- Home button in toolbar (visible only in Settings, disabled while busy)
- App logo: "Checkered Mod" 2×2 racing-flag vector design
  - Assets/Logo.xaml (DrawingImage resource)
  - Assets/logo.svg (SVG source)
  - Replaces 🎮 emoji in toolbar header
  - Window.Icon deferred to packaging (taskbar .ico)

### PMR Content Manager Logo Redesign (2026-05-31)
- Elliott rejected the flat checkered-grid logo; requested AMS2 CM-inspired design
- Researched Assetto Corsa Content Manager visual language:
  - Circular badge emblem (professional utility feel)
  - Dashboard/gauge motifs (motorsport dashboard)
  - Bold monograms and clean typography
  - Speed lines, chevrons, motion indicators
- **New design — "PMR Gauge Badge":**
  - Circular badge with gradient ring border (#45475a → #313244)
  - Dark disc background (#1e1e2e → #11111b gradient)
  - Speedometer arc (180° gauge at top) in AccentBrush (#89B4FA)
  - Gauge needle pointing "high speed" (top-right)
  - Tick marks around gauge arc (semi-transparent)
  - Triple racing chevrons at bottom (> > >) for motion feel
  - ViewBox: 64×64 (scalable down to 16px)
- Files: `Assets/logo.svg` (SVG source), `Assets/Logo.xaml` (WPF DrawingImage)
- Resource key `LogoDrawingImage` preserved for compatibility
- Toolbar Image bumped to 30×30 for new design

## Learnings
- AMS2/AC Content Manager design cues: circular badge, gauge/dashboard motifs, bold monogram, speed lines
- SVG text elements don't translate to WPF DrawingImage; use PathGeometry for typography or geometric icons
- Semi-transparent colors in WPF Pen: use #AARRGGBB format (e.g., #7089B4FA for 50% opacity)
- Keep icons geometric and simple for legibility at small sizes (16–32px)
- **Logo is now Elliott's provided PMR/CM wordmark SVG** (PMR stacked over CM; brass gold #B99A5D / white / asphalt #050505; transparent background). All previous gauge-badge code removed.
- **SVG → WPF DrawingImage translation**: Each SVG `<g transform="translate(tx,ty) scale(1,-1)">` becomes a `DrawingGroup` with `<MatrixTransform Matrix="1,0,0,-1,tx,ty"/>`. `scale(1,-1)` flips the path's y-axis so upright SVG paths (converted from text) render correctly in WPF's top-down coordinate system. Stroke-only layers use `GeometryDrawing Brush="{x:Null}"` with a `Pen`; fill layers use `Brush` + `Pen` together. `FillRule="Nonzero"` matches SVG's default. Root `DrawingGroup` uses `ClipGeometry` (RectangleGeometry 1200×800) to lock aspect ratio.
- **WPF RenderTargetBitmap → multi-res .ico**: No ImageMagick/Inkscape on the machine, so generated via a throwaway `tools/IconGen` console project (net10.0-windows, UseWPF=true). Builds DrawingGroup in C# code, renders to `RenderTargetBitmap` (PixelFormats.Pbgra32, transparent) at 6 sizes on an STA thread ([STAThread] or `Thread.SetApartmentState(STA)`), encodes each as PNG via `PngBitmapEncoder`, then writes ICONDIR + ICONDIRENTRY headers manually and concatenates PNG payloads. PNG-compressed ICO is valid on Windows Vista+; 256 px entry must be PNG per shell requirement. Resulting `app.ico` is 45,083 bytes with 6 entries: 16, 32, 48, 64, 128, 256 px.
- **Icon wiring**: `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` in the csproj embeds the icon in the .exe for desktop/Explorer display. `<Resource Include="Assets\app.ico"/>` in the csproj makes it available as a WPF pack-URI resource at runtime. `Icon="Assets/app.ico"` on the `<Window>` in MainWindow.xaml sets the title-bar and taskbar icon.
- **Image aspect ratio**: Wordmark is 3:2 (1200×800). Toolbar `<Image>` now uses `Height="28" Stretch="Uniform"` (no Width constraint) so the image auto-widens to ~42 px and the wordmark is never squished.


## Technical Lessons
- TwoWay-default bindings: ProgressBar.Value, Slider.Value, ComboBox.SelectedItem, CheckBox.IsChecked, TextBox.Text
- WPF TextBlocks are hit-testable even with no Background; use IsHitTestVisible="False" for decorative overlays
- Drag-drop handlers must be wired to the AllowDrop element itself, not distant ancestors
- Elevated WPF OLE drag-drop from non-elevated Explorer: use non-elevated UI + elevated helper pattern (not ChangeWindowMessageFilterEx fixes)

### 2026-06-01: User PMR/CM logo + black/white/gold retheme
- Elliott supplied a raster PNG (PMR with gold "V" wedge over "CM" on black). Used it for the app/desktop/taskbar icon AND the toolbar mark, and rethemed the app to match.
- **Icon generation (raster source):** Pillow auto-crops the non-black bounding box (threshold 40 to include the dark-grey letters but skip the vignette). Toolbar = tight crop kept on its black bg in a rounded `Border` badge; icon = centered square crop with ~30% black padding.
- **CRITICAL — Pillow's default `.ico` is NOT WPF-decodable.** `Window.Icon` threw `XamlParseException → FileFormatException ("The image is unrecognized")` at startup. Fix: build the ICO by hand as PNG-payload entries (ICONDIR + 16-byte ICONDIRENTRY per size + concatenated PNG bytes; height byte 0 ⇒ 256). This is the same PNG-compressed ICO format that already worked for the app (Vista+). Sizes 16/32/48/64/128/256.
- **Don't key out the black for transparency** — the PMR letters are dark grey on black; removing black erases them. Keep the black and frame it as a rounded badge on the near-black toolbar.
- **Palette (replaces Catppuccin Mocha) in App.xaml:** Base `#0A0A0A`, Mantle `#050505`, Surface0 `#161616`, Surface1 `#262626`, new `BorderBrush #E8E4D8` (white), Accent `#C2A35A` (brass gold; logo avg `#B49862`), Text `#EFEADD` (cream), Subtext `#A89F8C`. Headings/section labels switched to AccentBrush (gold); AccentButton stays gold-on-black.
- **White borders:** repointed Button/TextBox/ComboBox/ListBox/toolbar/status-bar/drop-zone `BorderBrush` from the old Surface brushes to `BorderBrush`. Dialogs (Warnings, AmbiguousMapping) use hardcoded hex (separate Windows) — reskinned each manually.
- **Black OS title bar:** added `DwmSetWindowAttribute` P/Invoke in `MainWindow.OnSourceInitialized` — `DWMWA_USE_IMMERSIVE_DARK_MODE(20)`, `DWMWA_CAPTION_COLOR(35)`=black, `DWMWA_BORDER_COLOR(34)`=white, `DWMWA_TEXT_COLOR(36)`=gold. COLORREF is `0x00BBGGRR`. Try/catch so it no-ops on pre-Win11.
- **Verification:** build green; 164/164 tests; launched exe and screen-captured the window (via process `MainWindowHandle` + `Graphics.CopyFromScreen`) to confirm the look. Screenshots can't find the window by title (em-dash) — enumerate by process handle instead.
