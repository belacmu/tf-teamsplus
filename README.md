# Teams+ — PC (FortRise 5)

A third team (**Yellow**) for TowerFall's team play, plus **lone wolves**: the
neutral column of the team-select screen is a real choice, and a solo archer
fights every team at once. Registered as its own versus mode, so vanilla Team
Deathmatch is untouched.

This is the desktop sibling of the Switch mod in `../exlaunch-overlay`
(same design, same gold, same wolves). The port assessment that led here is
[`docs/THREE-TEAM-PC-PORT.md`](../../docs/THREE-TEAM-PC-PORT.md).

## What it does

| | |
|---|---|
| **Mode** | "TEAMS+" in the GAME MODE carousel (needs 3+ archers), with its own icon |
| **Select screen** | four columns — **SOLO / BLUE / YELLOW / RED**, neutral first |
| **Start gate** | any two groups may start (2v1, 1v1v1, 2v1v1, team + wolves…). No per-team cap; you just can't all be on one team |
| **Yellow archers** | real gold sprite sheets — body, bow and head — plus gold aimer, dust, dash, hyper-jump, feather and **laser-arrow** colours |
| **Custom archers** | sheets are recoloured from the atlas at spawn time, so archer mods get gold and white versions too — nothing is pre-baked |
| **Lone wolves** | neutral = its own one-archer group: **white sprite sheets** plus white aimer/shield/dust/lasers, so no archer palette can be mistaken for a team. Hostile to everyone (including other wolves), own score row, nobody revives them |
| **Spawning** | two groups get the normal left/right split; a third group is anchored at the marker farthest from everyone else, teammates clustered around it, nobody sharing a marker |
| **Team-designed levels** | levels that ship only team markers and no neutral spawns are still in the pool: the map is split left-to-right into one block per group instead (Blue leftmost, Red rightmost) |
| **Round / match end** | round is over when one group is left standing; final-kill spotlight, crowns, goal, victory jingle all follow the group that won |
| **8 players** | works with [WiderSet](https://github.com/FortRise/ExampleFortRiseMod/tree/main/WiderSet) — 2v2v2 + 2 wolves verified, with a wide-set select screen. Optional: absent WiderSet everything runs at the vanilla 4 |
| **Results board** | one row per group, no blank rows |
| **Team Revive** | the variant works in this mode: any team, yellow included, revives its own. A lone wolf never gets a reviver — its allegiance is its own, so nothing could ever reach it |

## Install (players)

Drop `release/TeamsPlus.zip` into FortRise's `Mods` folder — no unzipping.
Version history is in [CHANGELOG.md](CHANGELOG.md).
`release/gamebanana/` holds a ready-to-paste submission kit (description,
screenshots, readme) if this goes public.

## Build

Requires the .NET 10 SDK and a FortRise 5.3+ install (the build references
`TowerFall.Patch.dll` from it).

```bash
./build.sh                 # debug build, auto-deployed into <FortRise>/Mods/TeamsPlus
./build.sh Release         # also writes release/TeamsPlus.zip for sharing
```

The FortRise folder is guessed from a macOS Steam install; point elsewhere with:

```bash
dotnet build -c Release -p:ModLoaderPath="/path/to/FortRise"
```

## Archer sheets, and custom archers

The gold and white archer sheets are **not** shipped. They are produced at spawn
time by `src/Recolor.cs`, which reads the archer's sheet out of the atlas,
recolours the pixels and caches the result for the session. That is what makes
archer mods work: a modded archer's art lives in its own mod, so no art we could
ship in advance would ever cover it.

**Which pixels belong to the team is the game's decision, not a guess.** Every
archer ships a blue *and* a red sheet, hand-painted; the pixels where those two
differ are the team's, and the pixels they agree on are the archer's own. So the
pair is diffed and only the differing pixels are recoloured — the same division
vanilla makes when it swaps blue for red.

Source preference per sprite:

1. `BlueTexture` + `RedTexture` — the pair, so the mask is exact
2. whichever single team sheet exists, with the team's pixels judged by hue
   instead (150–310°, measured off the shipped sheets: team colour ~190°, outfit
   shadow 280–300°, archer identity outside it)
3. **wolves only:** the archer's own sprite, drained of colour

There is no red-from-blue formula in the game to copy — the correspondence is not
even a bijection (one blue shade maps to three different reds in `body2`, and
`body3` carries a lone `#000000 → #FFFFFF` pixel) — which is why the mask, rather
than a palette rule, is what "equivalent to red/blue" actually means here.

So a custom archer that ships team textures (FortRise's `SpriteConfiguration`
has `BlueTexture`/`RedTexture`) behaves exactly like a vanilla one. One that
ships none still becomes a proper white wolf, but stays its own colours on the
yellow team — there is no sane way to force an unknown palette to gold, and such
an archer already cannot play blue or red in *any* team mode, because vanilla's
Player constructor reads `BlueTexture` unconditionally and throws without it.

The two shipped images — the gold select-screen banner and the carousel icon —
are generated from the game's own menu atlas:

```bash
python3 tools/gen_assets.py                    # macOS Steam default
python3 tools/gen_assets.py --game "/path/to/TowerFall"
```

## How it works

`src/Teams3.cs` is the spine: Yellow is `(Allegiance)2`, a lone wolf is a joined
player left on `Allegiance.Neutral`, and every group (each non-empty team, plus
one per wolf) is packed into a **score slot** at match start. Everything else
follows from those three ideas.

| File | Job |
|---|---|
| `ThreeTeamGameMode.cs` | the registered mode. `IsTeamMode = true` is what turns the whole team pipeline on |
| `ThreeTeamRoundLogic.cs` | N-group round-over check, group spawning, group final-kill |
| `Hooks/DataHooks.cs` | `ArcherData.Teams` -> Blue, Red, Yellow + one white entry per possible wolf (sized off `TFGame.Players`), which is where a wolf's white accents come from; `Get(Allegiance)`; team particle arrays sized to `Teams.Length` |
| `Hooks/RulesHooks.cs` | start gate, max/player team size, friendly-fire bookkeeping, score slots, wolf-vs-wolf kill typing |
| `Recolor.cs` | runtime sheet recolouring (gold / white) from the archer's team art, cached per source sheet |
| `Hooks/VisualHooks.cs` | picks the source sheet and swaps it in — body/bow/head, and again when a hat changes |
| `Wide.cs` | optional WiderSet interop: is the wide set on, how wide is the play-field |
| `SelfTest.cs` | dev-only scripted playtest, `./run.sh --selftest [8]` or `./run.sh --revive` |
| `Hooks/MenuHooks.cs` + `UI/` | the four-column select screen |
| `Hooks/LevelHooks.cs` | which levels the mode may pick: any level whose markers can seat everyone |
| `Hooks/ResultsHooks.cs` | the round-results board's `new int[2]` P-label array |
| `Hooks/SafetyHooks.cs` | the two places the engine indexes team data with a raw allegiance and would read `Teams[-1]` for a wolf |

Every hook is inert unless the mode is active, so other mods and vanilla modes
behave exactly as before.

## 8-player mods

Vanilla desktop TowerFall is four players, which would cap three teams at 1v1v1
or 2v1v1. With **WiderSet** installed (the maintained FortRise 8-player mod) the
mode goes up to 2v2v2 + 2 lone wolves, and the select screen switches to the
wide layout. Nothing is required: with no 8-player mod present, every path falls
back to the vanilla four.

How the two mods stay out of each other's way:

| Shared method | WiderSet | Us |
|---|---|---|
| `MatchTeams` indexer, `GetPlayersOfAllegiance`, `GetAmountOfPlayersOfAllegiance` | adds slots 4–7 (finalizers / postfixes) | always read teams *through* the indexer, and never touch a slot for a player who hasn't joined |
| `MatchSettings.CanStartWithTeams`, `GetMaxTeamSize`, `GetPlayerTeamSize` | **transpilers** (widen the vanilla bodies) | prefixes that answer for our mode only, and defer otherwise — so their transpiled bodies still serve every other mode |
| `MainMenu.CreateTeamSelect` | transpiler that re-lays out the vanilla screen | we replace the screen, so our layout does the wide geometry itself |
| `RoundLogic.SpawnPlayersTeams` | transpiler | never called: our RoundLogic spawns its own groups |

Player counts are read from `TFGame.Players`, and team data / particle arrays are
sized from it, so any mod that widens the player array gets the same treatment —
WiderSet just additionally tells us (through its API) when the wide set is on, so
the select screen and spawn distances match the wider play-field.

## Level selection

Vanilla picks a match's levels two ways and chooses by a literal mode
comparison: Team Deathmatch takes levels with enough `TeamSpawn` pairs,
everything else takes levels with enough `PlayerSpawn`s. A custom mode is
neither, so this mode would silently inherit the free-for-all rule — which
excludes the ~25 team-designed levels that carry no `PlayerSpawn` markers at all,
and would flip the day that `if` changes upstream.

So the mode filters for itself: a level is eligible when
`PlayerSpawns + 2 × TeamSpawns >= players`, i.e. when the markers the spawner
actually uses can seat everyone. Of the 192 shipped versus levels, that keeps
the 40 with both marker sets, the 95 free-for-all ones, and the 25 team-only
ones — with the spawner switching to left-to-right map splitting on the last
group, since a two-base level has no third base to anchor in.

## Known limits

* `HasEvenTeams` is still Blue-vs-Red, so the "even teams" treasure bonuses stay
  off in Teams+ matches.
* Corpses are not recoloured (vanilla only ships blue/red corpse sheets), so a
  gold or white archer leaves a normal corpse.
* The versus intro still shows only the blue and red banners.
