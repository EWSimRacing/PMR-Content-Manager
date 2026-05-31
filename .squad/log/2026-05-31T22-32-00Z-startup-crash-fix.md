# Startup Crash Fix — Session Log

**Session:** 2026-05-31T22:32:00Z  
**Scribe:** Recording Slit's WPF binding fix  

## Issue
ProgressBar.Value binding was TwoWay (WPF default), attempting write-back to read-only MainViewModel.ProgressValue property. XamlParseException at startup, app crash before window render.

## Resolution
Added `Mode=OneWay` to binding in MainWindow.xaml. Progress is display-only (VM → UI), no write-back needed.

## Verification
✓ Builds clean  
✓ App launches and stays alive  
✓ 56/56 tests pass  

## Decisions Updated
Appended new entry to .squad/decisions.md documenting the fix rationale and OneWay binding pattern decision.
