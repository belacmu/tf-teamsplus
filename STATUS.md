# Three Team Mode (PC / FortRise 5) — status

Built 2026-08-20 in one session, from the port assessment in
[`docs/THREE-TEAM-PC-PORT.md`](../../docs/THREE-TEAM-PC-PORT.md).

## Environment (set up this session)

* FortRise **5.3.5** (osx-x64) extracted into
  `<Steam>/steamapps/common/TowerFall/TowerFall.app/Contents/FortRise.app`.
  It needs `launch_override.json` next to the `FortRise` binary (this layout is
  not one it auto-detects):
  `{"GamePath": ".../TowerFall.app/Contents/Resources/TowerFall.exe"}`
* **Steam must be running** — FortRise inits the Steam API and otherwise bounces
  the launch through `steam://run/251470` and exits.
* Run the modded game with `cd <FortRise>/Contents/MacOS && ./FortRise`.
* .NET 10 SDK present; `./build.sh` builds and deploys into `<FortRise>/Mods/`.
* The self-test needs the game window **focused** to run to completion — FNA
  throttles updates when the window is in the background, and a backgrounded run
  will sit in the round intro forever. Leave the window in front while it drives.
* Don't launch two instances: the second fails to write
  `Mods/_RelinkerCache/ThreeTeamMode.ThreeTeamMode.dll` and runs stale code.
  Use `./run.sh` — it closes any running instance first. Note the process
  command line is just `./FortRise`, so `pkill -f MacOS/FortRise` never matches
  it (that is how three instances piled up here); `pkill -x FortRise` works.

## Verified by the scripted self-test (`THREETEAM_SELFTEST=1`)

Screenshots are taken from the game's own back buffer, so these are real frames:

| What | Evidence |
|---|---|
| Mod loads, 11 TeamData entries, sheets recoloured on demand | log |
| Gold and white sheets built at runtime from the atlas: `gold (yellow team): P3 recoloured 3/3`, `white (lone wolf): P4 recoloured 3/3` | log + frames |
| Custom-archer fallback (`THREETEAM_FORCE_ARCHER_SOURCE=1`, i.e. no archer ships team textures): wolf still goes white `3/3` from its own sprite, yellow team keeps its palette `0/3` | log + frame |
| Four-column select screen: SOLO / BLUE / **gold banner** / RED, P1–P4 in archer colours | `1-team-select.png` |
| Start gate accepts 1v1v1 + wolf (`CanStartWithTeams=True`) | log |
| Group → score slot packing: `0=BLUE(1) 1=RED(1) 2=YELLOW(1) 3=WOLF P4` | log |
| Spawning: four groups at four separated markers, no stacking | `2-match.png` |
| **Yellow archer is really gold** (body, bow, head all swapped: `3/3`) | `2-match.png` + log |
| Lone wolf keeps its own archer colours | `2-match.png` |
| Round-over on a third team + final-kill spotlight on the yellow player | `4-final-kill.png` |
| Round-results board: **one row per group**, P-labels in archer colours, the yellow row's coin lit | `6-round-results.png` |
| Match end: crown on the winning group's row, CONTINUE prompt, no exception | `8-match-results.png` |

## Verified with WiderSet (8-player) installed

Built WiderSet 0.9.0 from FortRise's example-mods repo into the same Mods folder
and re-ran the harness with `THREETEAM_SELFTEST_PLAYERS=8`:

| What | Evidence |
|---|---|
| Both mods load; our interop resolves (`WiderSet detected`) | log |
| Wide set forced on, wide tower set picked (`Teuria.WiderSet/<Teuria.WiderSet>/TowerFall`), 1290x720 frames | log |
| Start gate accepts the 8-player line-up | `CanStartWithTeams=True` |
| **2v2v2 + 2 lone wolves**: `0=BLUE(2) 1=RED(2) 2=YELLOW(2) 3=WOLF P7 4=WOLF P8` | log |
| Wide select screen: 8 rows, three banners, title and FIGHT prompt clear | `1-team-select.png` |
| In-match: 2 red, 2 blue, 2 **gold** and 2 wolves in their own archer colours, spread across the wide level | `2-match.png` |
| Results board: five rows, teams stacking two P-labels each, crown on the yellow row | `6-round-results.png` |

## 0.1.1 — Team Revive fixed (2026-08-21)

**Bug report: team revive did nothing.** Confirmed at source level against the
FortRise-patched assembly. `PlayerCorpse.Added` spawns the reviver behind a
literal mode comparison:

```csharp
else if (MatchSettings.Mode == Modes.TeamDeathmatch && Variants.TeamRevive)
    Level.Add(new TeamReviver(this, TeamReviver.Modes.TeamDeathmatch));
```

Our mode is `Modes.Custom`, so no reviver was ever created — while the variant
screen happily offered the toggle, because `MatchVariants` builds its list from
field attributes and never consults the mode. Same class of bug as the level
filter. `src/Hooks/ReviveHooks.cs` adds the reviver in a postfix.

Nothing downstream needed changing, and that is worth knowing: `TeamReviver`
matches revivers to corpses by **allegiance**, not by a two-team table, so a
yellow archer reviving a yellow corpse works untouched, the revive arrow is
already tinted from `ArcherData.Get(TeamColor)`, and `ShockCircle` spares the
reviver's own team by comparing `Teams[reviver]` against each player's
allegiance — no array indexed by an allegiance anywhere on the path.
`GroupCheckForRoundOver` already counted a mid-revive corpse as its group still
standing, matching `RoundLogic.TeamCheckForRoundOver`.

Two deliberate decisions:

* **Wolves get no reviver at all.** Each wolf's allegiance is its own, so no
  player could ever satisfy the revive; spawning one would only float a revive
  prompt over the corpse forever. A wolf that dies stays dead.
* **Single-member teams still get one**, dangling prompt and all — that is
  exactly what vanilla does in a 1v1 Team Deathmatch with the variant on, and
  1v1v1 here is the same shape. Not our call to improve.

Also fixed on the same path: a revive can fail at the last moment (corpse inside
a wall, or squished), and vanilla re-checks for a winner there behind the same
mode comparison. Without it, a round whose last standing group was a failed
revive would never end. `ReviveHooks` defers that check by one frame via
`ThreeTeamRoundLogic.RequestWinCheck` — the caller clears `Corpse.Revived`
immediately after `FinishReviving` returns, so asking any earlier gets the old
answer.

`./run.sh --revive` is a scripted 2v1 + wolf run with the variant on: it kills a
blue archer and the wolf, pins the surviving blue on the corpse, and logs
`SELFTEST REVIVE PASS/FAIL` for four checks — reviver present for blue, absent
for the wolf, archer alive again, round did not end mid-revive.

**Not yet run in game** — launching is still on hold at the user's request. The
patch targets (`PlayerCorpse.Added`, `TeamReviver.FinishReviving`) were both
verified present in the decompiled assembly the mod compiles against, and the
build is clean, but the scenario has not executed.

## Release 0.1.0 — ready (2026-08-21)

`release/ThreeTeamMode.zip`, 48 KB, 6 files: `meta.json`, `INSTALL.txt`, the dll
(+pdb for readable user stack traces) and the two shipped images. Pre-flight
passes: meta at root, declared dll present, FortRise required / WiderSet
optional, no stale sheet folders, no `update.gamebanana.id` yet. The dll in the
zip checksum-matches a clean Release build.

`release/gamebanana/` is the submission kit — paste-ready `description.txt`,
eight screenshots in presentation order, `INSTALL.txt`, an identical copy of the
zip, and `SUBMISSION.md` with the form fields.

**Two things still open before it goes public:**

1. The Release dll itself has not been launched — launching is on hold at the
   user's request. `./run.sh --revive` then `./run.sh --selftest 4` confirms both
   the revive fix and the mask change.
2. No 8-player in-match screenshot in the kit: the one I had predates white
   wolves, so it was dropped rather than ship something misleading.
   `./run.sh --selftest 8`, then take `shots/2-match.png`.

## Shipping

`release/ThreeTeamMode.zip` (~83 KB) is the distributable: `meta.json`,
`INSTALL.txt`, the dll (+pdb, so user stack traces have line numbers) and
`Content/`. **Verified by installing the zip itself** — deleted the dev folder
deploy, dropped the zip into `Mods/`, and FortRise loaded
`ThreeTeamMode 0.1.0` from it and played a full 8-player match.

`release/gamebanana/` is a submission kit: paste-ready `description.txt`, seven
in-game screenshots in presentation order, `INSTALL.txt`, the zip, and
`SUBMISSION.md` with the form fields. Nothing has been uploaded. `meta.json`
deliberately has **no** `update.gamebanana.id` — that gets filled in from the
submission's URL after the page exists, otherwise users' update checks point at
somebody else's mod.

Pre-ship hardening:
* `SelfTest` installs no patches at all unless `THREETEAM_SELFTEST=1` — a normal
  launch has nothing on `Engine.Draw`, `Level.Update`, `MainMenu.Update` or
  `MenuInput`.
* `meta.json` lists `Teuria.WiderSet` under `optionalDependencies`, so FortRise
  loads it first; `Wide` also retries the API lookup rather than pinning itself
  to 4 players if the lookup ever comes up empty.

## Not verified yet

* **Real controllers / real players.** Everything above was driven by the mod
  itself with idle keyboard inputs.
* A team with **two** players (2v1v1): teammate clustering in the spawner.
* Multi-round matches (goal > 1), overtime, sudden death.
* Wolf-vs-wolf kill typing (the `OnPlayerDeath` transpiler), wolf victory music.
* Variants, especially **Team Revive** (a wolf has no team to revive it) and the
  chalice / sensor-block colour paths that `SafetyHooks` covers.
* How the three spawn strategies feel in play. All three are exercised and
  verified by the harness — side split (levels with both marker sets), anchor
  (free-for-all levels), and left-to-right partition (team-only levels, verified
  on `01 - Twilight Spire/07.oel`: BLUE|YELLOW|wolf|RED across the map) — but
  only play will say whether the openings are fair.

## Level selection (changed 2026-08-21)

`VersusTowerData.GetLevels` branches on `Mode == Modes.TeamDeathmatch`, so a
custom mode inherits the free-for-all filter — which dropped the 25 team-designed
levels (no `PlayerSpawn` markers) and left us one upstream `if` away from
suddenly being handed them. The mode now filters for itself
(`PlayerSpawns + 2 * TeamSpawns >= players`, `Hooks/LevelHooks.cs`) and the
spawner grew a third strategy for levels that cannot seat the odd group on
neutral ground: split the marker pool left-to-right, one block per group, Blue
leftmost and Red rightmost.

## Bugs found and fixed while testing

1. `MatchSettings.CanStartWithTeams` **inlines** the two-team check instead of
   calling `MatchTeams.ProperlyAssigned` — patching only the latter left the
   start gate refusing to begin (`CanStartWithTeams=False`). Same trap as the
   Switch build. Both are patched now.
2. `TeamSelector` (and ours) null-derefs `TFGame.PlayerInputs[i]` for a joined
   player with no device attached. Ours is now null-safe.
3. `ArcherData.Get(Allegiance.Neutral)` throws, and several team-mode call sites
   (chalice fill, chalice pad, chalice ghost) reach it for a lone wolf → returns
   the white wolf TeamData while our match is live.
4. `SensorBlock` indexes `ArcherData.Teams[(int)Teams[squisher]]` directly, which
   is `Teams[-1]` for a wolf → redirected through `Teams3.TeamDataAllegiance`.

## Marking a group: settled

Groups are marked by **recoloured sheets derived from the archer's own team
art** — gold for the yellow team, white for lone wolves — plus the white/gold
accents (aimer, shield, dust, dash, feathers, lasers).

Team outlines (the ring the console builds draw) were prototyped and dropped by
request. Findings, in case it ever comes back up: no ring exists anywhere in the
PC codebase — the 8-player SKU's `GameTags` stops at 38 entries while the Switch
build tags players 38/39 by allegiance — but Monocle's
`GraphicsComponent.DrawOutline(Color, offset)` plus a prefix on
`LevelEntity.DoWrapRender` draws one in about 40 lines, behind the archer and
including screen-wrapped copies.

**Team pixels come from the blue/red diff**, not from a hue rule: the two sheets
are hand-painted and where they differ is, by the game's own authorship, the
team's part of the sprite. Measured against the alternative, per sheet (gold
gate):

| sheet | team px (mask) | hue-window px | archer px the window would wreck |
|---|---|---|---|
| body0 | 868 | 868 | 0 |
| body1 | 1207 | 753 | 0 |
| body2 | 499 | 489 | 0 |
| body3 | 1413 | 1766 | **353** |
| body4 | 195 | 239 | 44 |
| body7 | 718 | 882 | 164 |

So the window both missed team art (body1: 454 pixels) and ate the archer's own
(body3: 353). The hue window survives only as the fallback for an archer that
ships a single team sheet; 150–310° there, and note the Switch generator's
150–280° left purple trousers on the pink archer — don't sync it back.

## Deliberate differences from the Switch build

* Wolves get **white sprite sheets** as well as the white accents (aimer,
  shield, dust, dash, feathers, lasers) — the sheets were removed once and then
  asked for again; they are in.
* A wolf's allegiance is its own slot past the three teams (`Teams3.WolfAllegiance`),
  exactly like the Switch build's split-slot design. That is what gives it the
  white accents, makes two wolves enemies, stops Team Revive from reviving them,
  and stops arrow trading.
* No gold ring / ring suppression: that whole subsystem was a Switch-only
  workaround for the missing sprite swap.

## Next

1. Real playtest with controllers (2v1v1, 1v1v1, all-wolves, multi-round, and
   an 8-player wide match picked through the menus rather than scripted).
2. The versus intro shows only the blue and red banners — add the gold one.
3. Optional: `HasEvenTeams` for three teams (treasure bonuses currently stay off).
4. Optional: WiderSet interop for 4v4v4 (see README "Known limits").
