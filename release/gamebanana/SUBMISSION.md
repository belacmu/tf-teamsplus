# GameBanana submission kit — Teams+

Everything needed to put the mod up. Nothing here has been uploaded anywhere.

* `TeamsPlus.zip` — the file to attach (this is the mod; installers drop it
  straight into FortRise's `Mods` folder, no unzipping)
* `description.txt` — paste into the submission's description box
* `screenshots/` — 7 in-game frames, in the order they're worth showing
* `INSTALL.txt` — the same readme that ships inside the zip

## Filling in the form

| Field | Suggested value |
|---|---|
| Name | Teams+ |
| Category | TowerFall → Mods → Game Modes (or Other, if that category doesn't exist) |
| Requirements | FortRise 5.3.0+; optional: WiderSet for 8 players |
| Tags | game mode, versus, teams, FortRise |
| Version | 0.1.2 |
| Credit | crab |

Screenshot order (the first is the thumbnail). Taken 2026-11-06 from 0.1.2's release zip in the browser port of TowerFall
(the PC game, FortRise 5.5.0-beta.4, WiderSet 0.9.0): the 4-player frames from Teams+'s own selftest, the 8-player ones played
through the menus.

1. `1-team-select-8-players.png`: the four columns with 8 players picked (2 BLUE, 2 YELLOW, 2 RED, 2 SOLO)
2. `2-match-three-teams-and-a-wolf.png`: 1v1v1 plus a lone wolf in play
3. `3-game-mode.png`: TEAMS+ in the GAME MODE carousel
4. `4-gold-archer-closeup.png`: the yellow team's gold sheet, 4x
5. `5-white-wolf-closeup.png`: a lone wolf's white sheet, 4x
6. `6-match-8-players.png`: an 8-player match's opening (TowerForge, every archer in its group's colour)
7. `7-round-results.png`: the round results

No 8-player frame from mid-round: the browser port doesn't get past an 8-player round's intro (vanilla modes too), so it can't
show one. Worth adding from the PC game (`./run.sh --selftest 8`, `shots/2-match.png`).

## Versions

`../../CHANGELOG.md` (the repo's CHANGELOG.md) is the authoritative history; `description.txt` carries a
short version of it for the page. GameBanana keeps its own per-version Updates
section — post each version's entry there (0.1.2 now) rather than editing the description, so
subscribers get notified.

## After the page exists

Grab the submission's numeric id from its URL and add it to `meta.json` so
FortRise can offer in-app updates, then rebuild the zip:

```json
"update": { "gamebanana": { "id": <that number> } }
```

It is deliberately absent right now — a wrong id would point users' update checks
at somebody else's mod.
