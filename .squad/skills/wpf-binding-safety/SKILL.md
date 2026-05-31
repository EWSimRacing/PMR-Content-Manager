# WPF Binding Safety

## The Problem: TwoWay-by-Default Bindings Against Read-Only VM Properties

Several WPF dependency properties bind **TwoWay by default**. If the bound ViewModel property is read-only (get-only or `private set`), WPF will throw a runtime `XamlParseException → InvalidOperationException` **at startup** — even though `dotnet build` succeeds.

### Controls / properties that default to TwoWay

| Control / Property | Default Mode |
|---|---|
| `ProgressBar.Value` (`RangeBase.Value`) | TwoWay |
| `Slider.Value` | TwoWay |
| `ComboBox.SelectedItem` | TwoWay |
| `ComboBox.SelectedValue` | TwoWay |
| `ListBox.SelectedItem` | TwoWay |
| `Selector.SelectedItem` | TwoWay |
| `TextBox.Text` | TwoWay |
| `CheckBox.IsChecked` | TwoWay |
| `ToggleButton.IsChecked` | TwoWay |
| `DatePicker.SelectedDate` | TwoWay |

`TextBlock.Text`, `Button.IsEnabled`, `Visibility`, `ItemsSource`, and most read-only display properties default to OneWay and are safe.

## The Fix

If the binding is **display-only** (value flows VM → UI only), add `Mode=OneWay`:

```xml
<!-- WRONG — crashes at startup if ProgressValue has private/no setter -->
<ProgressBar Value="{Binding ProgressValue}"/>

<!-- CORRECT -->
<ProgressBar Value="{Binding ProgressValue, Mode=OneWay}"/>
```

If the binding **should** be two-way (e.g. a TextBox the user edits), make the VM property public settable with change notification:

```csharp
// Correct two-way target: public setter + PropertyChanged
public string? ConfiguredPath
{
    get => _configuredPath;
    set => SetField(ref _configuredPath, value);  // raises PropertyChanged
}
```

## Critical: Build Success ≠ App Runs

XAML binding errors are **runtime-only**. The solution compiles clean and the crash only appears on first window load. Always **launch-test** after UI changes:

```powershell
# PowerShell launch test — process must stay alive (need to be killed) = good
$p = Start-Process -FilePath dotnet -ArgumentList 'run','--project','src/EWSR_PMR_ModApp.UI','--no-build' `
     -PassThru -RedirectStandardError err.log -RedirectStandardOutput out.log
Start-Sleep 25
if (!$p.HasExited) { $p.Kill(); "STILL RUNNING = GOOD" }
else               { "EXITED EARLY — check err.log"; Get-Content err.log }
Remove-Item err.log, out.log -ErrorAction SilentlyContinue
```

A process that stays alive (had to be killed) means the window opened successfully. A process that self-exited with exception text means it still crashes.

## Audit Checklist When Adding/Reviewing XAML

1. Identify all bindings to TwoWay-default properties (table above).
2. For each, check the VM property: does it have a **public** setter?
3. If display-only → add `Mode=OneWay`.
4. If two-way intended → ensure public setter + `SetField` / `OnPropertyChanged`.
5. Launch-test before calling it done.

## Real Example (EWSR_PMR_ModApp, 2026-05-31)

`MainWindow.xaml` status bar:

```xml
<!-- Before (crashes) -->
<ProgressBar Value="{Binding ProgressValue}" .../>

<!-- After (fixed) -->
<ProgressBar Value="{Binding ProgressValue, Mode=OneWay}" .../>
```

`MainViewModel.ProgressValue` had `private set` — display-only, so `Mode=OneWay` is correct and clean. `PropertyChanged` is already raised via `SetField`, so the bar still animates during install operations.
