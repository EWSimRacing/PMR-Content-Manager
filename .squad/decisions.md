# Decisions

Canonical decision ledger for EWSR_PMR_ModApp. Append-only. Scribe merges entries from `decisions/inbox/`.

---

### 2026-05-31T15:41:33-04:00: Stack Choice — .NET 10 + WPF

**By:** Furiosa

**What:** The project will use C# / .NET 10 with WPF for the UI and a separate Core class library for all engine logic.

**Why:**
- Windows-only target → WPF gives native drag-and-drop, file dialogs, and system tray support out of the box.
- .NET's `System.IO.Compression` and async file I/O are excellent for the zip/sync workload.
- Single-file publish produces a clean installer story without bundling a browser (vs Electron).
- C# is strongly typed and well-tooled for a solo/small dev — refactoring is safe, NuGet ecosystem is rich.
- Inspired by AMS2 Content Manager which is also .NET/WPF — proven pattern for this domain.
