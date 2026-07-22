# Unslop Unity Asset Bridge

## Goals

- [x] Full MVP of `com.unslop.unity-bridge` per [plan](file:///c%3A/Users/devel/.cursor/plans/unity_bridge_phase_a_7d961cf3.plan.md)

### Milestones
- [x] Phase A — Foundations (`ff07765`, `bfab0df`)
- [x] Phase B — Initial install (`2f3a6c3`)
- [x] Phase C — Staged updates (`ba97c80`)
- [x] Phase D — Materials (`105453b`; ownership service in `2f3a6c3`)
- [x] Phase E — Scale (`71e3c41`)
- [x] Phase F — Hardening (`9061600`)

## Active work
- None — MVP implementation committed locally (`main` ahead of `origin/main` by 7)

## Agents
- [Phase A implementer](10eb6673-3903-4164-a113-c196e0dc5ac8)
- [B-F implementer](2d53e1b4-ca50-4a4f-b6b6-2186e80aa415)
- [C-F implementer](5ec77860-a9ed-43aa-ae5f-e72b0103c921)

## Plan
- [Unity Bridge Full MVP](file:///c%3A/Users/devel/.cursor/plans/unity_bridge_phase_a_7d961cf3.plan.md)

## Notes
- Headless tests: `python scripts/validate_package.py`, `test_manifest_fixtures.py`, `test_domain_rules.py`, `smoke_api.py` (needs `UNSLOP_API_KEY`)
- Unity EditMode tests live under `Tests/Editor` (require Unity Test Runner)
