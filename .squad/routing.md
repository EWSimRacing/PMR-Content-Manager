# Routing

| Domain / Signal | Agent |
|-----------------|-------|
| Architecture, scope, stack decisions, code review, triage | Furiosa (Lead) |
| File sync engine, zip extraction, game path detection, update-revert recovery, install logic | Nux (Core Dev) |
| UI, drop-zone, mod list, install status, settings screen | Slit (UI Dev) |
| Tests, edge cases (bad zips, missing game dir, file conflicts, permissions) | Wez (Tester) |
| Memory, decisions, session logs | Scribe |
| Work queue, backlog, keep-alive | Ralph |

## Notes
- Reviewer role: Furiosa and Wez may approve/reject work.
- When in doubt on core file-handling logic → Nux. On anything the user sees → Slit.
