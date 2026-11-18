---
name: Something is not working
about: A feature did nothing, something appeared in the wrong place, or the game misbehaved
title: ""
labels: bug
assignees: ""
---

**Before anything else:** run `diagnose.cmd` (one double-click) and attach
`SeaPowerAdvanced-diagnose.txt`. It collects the version, the game folder, the mod's own log and
every report file the mod writes, and without it a report like "the ECM does not work" cannot be
told apart from "the ECM is not installed".

## What happened

<!-- The menu you clicked, the ship, the contact, and what you expected instead. -->

## What the log says

<!-- The relevant lines from seapoweradvanced.log, or the report file the feature writes. -->

## Your install

| | |
|---|---|
| Mod version (`version.txt` in the mod folder) | |
| Sea Power version | |
| Installed with | `build.cmd` / `update.cmd` / Anchor Chain (Workshop) / other |
| Multiplayer mod in use | no / yes - host / yes - client |

## Files attached

- [ ] `SeaPowerAdvanced-diagnose.txt`
- [ ] `seapoweradvanced.log`
- [ ] the report file for the feature (`ecm-report.txt`, `offensive-ecm.txt`, `ecm-dive.txt`, `ecm-cone.txt`, `menu-report.txt`, `capabilities.txt`, `multiplayer.txt`)

<details>
<summary>What each report file answers</summary>

| File | The question it answers |
|---|---|
| `version.txt` | Which build is actually installed |
| `seapoweradvanced.log` | What the mod did, action by action, and whether it loaded |
| `capabilities.txt` | Which game members this install exposes |
| `menu-report.txt` | Which menu section each entry went into |
| `ecm-report.txt` | Which ECM interface this install has |
| `offensive-ecm.txt` | What the last jamming orders read and did |
| `ecm-dive.txt` | Which missiles were put in the water, and which were left alone |
| `ecm-cone.txt` | Whether the cones drew, and from which positions |
| `multiplayer.txt` | Whether this machine is host or client, and what changed because of it |

</details>
