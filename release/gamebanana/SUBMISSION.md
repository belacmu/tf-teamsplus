# GameBanana submission kit — Three Team Mode

Everything needed to put the mod up. Nothing here has been uploaded anywhere.

* `ThreeTeamMode.zip` — the file to attach (this is the mod; installers drop it
  straight into FortRise's `Mods` folder, no unzipping)
* `description.txt` — paste into the submission's description box
* `screenshots/` — 7 in-game frames, in the order they're worth showing
* `INSTALL.txt` — the same readme that ships inside the zip

## Filling in the form

| Field | Suggested value |
|---|---|
| Name | Three Team Mode |
| Category | TowerFall → Mods → Game Modes (or Other, if that category doesn't exist) |
| Requirements | FortRise 5.3.0+; optional: WiderSet for 8 players |
| Tags | game mode, versus, teams, FortRise |
| Version | 0.1.1 |
| Credit | crab |

Screenshot order — the first is the thumbnail, so lead with the select screen:

1. `1-team-select.png` — the four columns, which is the whole pitch
2. `2-match-three-teams-and-a-wolf.png` — 1v1v1 + a wolf in play
3. `3-gold-archer-closeup.png` — the yellow team's gold sheet, 5x
4. `4-white-wolf-closeup.png` — a lone wolf's white sheet, 5x
5. `5-final-kill.png` — final-kill spotlight on the winning team
6. `6-results-one-row-per-group.png` — a score row per group
7. `7-team-select-8p.png` — the wide select screen with 8 archers
8. `8-results-8p.png` — five rows, two P-labels per team

Every frame is from the shipped build except one gap: there is **no 8-player
in-match frame**, because the only one I had was captured before lone wolves went
white and would misrepresent the mod. Worth capturing before submitting —
`./run.sh --selftest 8` and take `shots/2-match.png`.

## Versions

`../../CHANGELOG.md` is the authoritative history; `description.txt` carries a
short version of it for the page. GameBanana keeps its own per-version Updates
section — post the 0.1.1 entry there rather than editing the description, so
subscribers get notified.

## After the page exists

Grab the submission's numeric id from its URL and add it to `meta.json` so
FortRise can offer in-app updates, then rebuild the zip:

```json
"update": { "gamebanana": { "id": <that number> } }
```

It is deliberately absent right now — a wrong id would point users' update checks
at somebody else's mod.
