# Changelog — Teams+ (PC / FortRise 5)

Player-facing changes. The engineering log, with the reasoning and the
verification notes, is in [STATUS.md](STATUS.md).

## 0.1.2 — 2026-11-04

**Updates in game:** FortRise now checks this mod's GitHub releases and offers new versions.

**Renamed: the mode is now called Teams+** ("TEAMS+" in the GAME MODE
carousel; it was "THREE TEAM"). The mod's id, folder and zip are now `TeamsPlus` too (they were
`ThreeTeamMode`): delete the old ThreeTeamMode folder or zip from Mods when updating.

**Fixed: TEAMS+ could not be picked with TF.EX installed.** FortRise loads mods by
their priority, and TF.EX (priority Low) loaded first; its GAME MODE patch stops
the scroll at its own Netplay mode, so every mode registered after it was out of
reach. Teams+ now has priority Lowest and loads before TF.EX. With WiderSet
installed it still loaded after TF.EX (it waited for WiderSet, an optional
dependency): WiderSet is no longer a dependency, Teams+ finds it when first needed.

## 0.1.1 — 2026-08-22

**Fixed: the TEAM REVIVE variant did nothing.** The variant screen offers the
toggle in every versus mode, but the engine only spawns a reviver when the mode
is literally Team Deathmatch — and this is its own mode, so nothing happened.
Teams now revive their own, yellow included, with the revive prompt in the
team's colour.

- Lone wolves deliberately get no reviver. A wolf answers to nobody, so no
  archer could ever satisfy the revive; a prompt over the corpse would only be a
  promise the mode cannot keep.
- Fixed a round that could hang if a revive failed at the last moment (the
  corpse ended up inside a wall, or was squished). The winner is re-checked, as
  it is in Team Deathmatch.
- Known limit: a yellow revive plays the blue team's revive sound. The game
  ships no third one.

## 0.1.0 — 2026-08-21

First release.

- A third team, **YELLOW**, with real gold archers — body, bow and head — plus a
  gold aimer, dust, dash and laser arrows.
- **Lone wolves**: the neutral column of the team screen is a real choice. A
  white archer, hostile to every team and to the other wolves, scoring on its
  own row.
- Four-column team select — SOLO | BLUE | YELLOW | RED.
- Start with any two groups: 1v1v1, 2v1v1, one team against two wolves. The only
  line-up refused is everyone on one team.
- Custom archers are handled: gold and white sheets are recoloured at runtime
  from whatever art an archer ships, so nothing is pre-baked and archer mods work
  like vanilla ones.
- Team-designed levels stay in the pool — the map is split into one block per
  group when a level has no neutral spawns.
- Third groups spawn at the point farthest from everyone already placed.
- One results row per group, and crowns, goals, final-kill spotlights and
  victory music all follow whichever group won.
- 8 players with [WiderSet](https://github.com/FortRise/ExampleFortRiseMod):
  2v2v2 plus two lone wolves, on a select screen laid out for the wide set.
  Optional — without it everything runs at the usual four archers.
