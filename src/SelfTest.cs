using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FortRise;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using TowerFall;

namespace ThreeTeamMode;

/// <summary>
/// A scripted smoke test, for development only: it drives the mode end to end
/// with no controller attached, so a change can be verified (and screenshotted)
/// without four humans in the room.
///
/// Off unless the environment variable THREETEAM_SELFTEST=1 is set:
///
///     THREETEAM_SELFTEST=1 ./FortRise
///
/// It joins archers, opens OUR team-select screen, spreads them over the four
/// columns, starts a match, then kills everyone who is not on the yellow team so
/// the round ends on a third team and the results board draws one row per group.
/// Each step is announced in the log, which is what the screenshot tooling waits
/// on.
///
/// THREETEAM_SELFTEST_PLAYERS=8 asks for the 8-player run instead: it flips
/// WiderSet into its wide set, joins eight, and lines up 2v2v2 + two lone
/// wolves — the widest shape the mode supports.
///
/// THREETEAM_SELFTEST_REVIVE=1 runs the Team Revive scenario instead: 2v1 plus a
/// lone wolf with the variant on, one blue archer killed and its teammate walked
/// onto the corpse, and the wolf killed as the control. It checks four things and
/// logs SELFTEST REVIVE PASS/FAIL for each:
///
///   * a reviver appears for the blue corpse,
///   * none appears for the wolf's,
///   * the revive completes and the archer is back in the level,
///   * the round did not end while the revive was running.
/// </summary>
internal static class SelfTest
{
    public static bool Enabled { get; set; }

    private const int MenuSettleFrames = 150;   // let the menu finish booting
    private const int LookFrames = 260;         // ~4s to look at / capture a step
    private const int MatchSettleFrames = 320;  // round start + unfreeze

    private static int phase;
    private static int frames;
    private static bool forceConfirm;
    private static int playerCount = 4;
    private static int forceTower = -1;
    private static int forceLevel = -1;
    private static bool reviveTest;

    private static bool Confirm_Prefix(ref bool __result)
    {
        if (!Enabled || !forceConfirm)
        {
            return true;
        }
        __result = true;
        return false;
    }

    /// <summary>Where to drop screenshots (THREETEAM_SHOTDIR).</summary>
    private static string? shotDir;
    private static string? pendingShot;

    public static void Load(IHarmony harmony)
    {
        if (!Enabled)
        {
            // A normal launch installs none of this: no patches on Engine.Draw,
            // Level.Update, MainMenu.Update or MenuInput.
            return;
        }
        shotDir = Environment.GetEnvironmentVariable("THREETEAM_SHOTDIR");
        if (int.TryParse(Environment.GetEnvironmentVariable("THREETEAM_SELFTEST_PLAYERS"),
                         out int wanted))
        {
            playerCount = Math.Clamp(wanted, 3, 8);
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("THREETEAM_SELFTEST_TOWER"),
                         out int tower))
        {
            forceTower = tower;
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("THREETEAM_SELFTEST_LEVEL"),
                         out int level))
        {
            forceLevel = level;
        }
        reviveTest = Environment.GetEnvironmentVariable("THREETEAM_SELFTEST_REVIVE") == "1";
        if (reviveTest)
        {
            playerCount = 4;
        }

        harmony.Patch(AccessTools.DeclaredMethod(typeof(MainMenu), "Update"),
                      postfix: new HarmonyMethod(typeof(SelfTest), nameof(MenuUpdate_Postfix)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Level), "Update"),
                      postfix: new HarmonyMethod(typeof(SelfTest), nameof(LevelUpdate_Postfix)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Engine), "Draw"),
                      postfix: new HarmonyMethod(typeof(SelfTest), nameof(Draw_Postfix)));
        // The versus intro waits on `while (!MenuInput.Confirm)`, so a hands-off
        // run has to answer it.
        harmony.Patch(AccessTools.DeclaredPropertyGetter(typeof(MenuInput), nameof(MenuInput.Confirm)),
                      prefix: new HarmonyMethod(typeof(SelfTest), nameof(Confirm_Prefix)));
    }

    /// <summary>
    /// Ask for a screenshot of the next rendered frame. Grabbing the game's own
    /// back buffer beats driving the OS screenshot tool: it is the exact frame,
    /// it needs no window focus, and it works while the game is in fullscreen.
    /// </summary>
    public static void Shoot(string name)
    {
        if (shotDir != null)
        {
            pendingShot = name;
        }
    }

    private static int tailFrames;

    /// <summary>
    /// Once the round is over the game leaves the Level scene (round results,
    /// then match results), so the tail of the run is paced off draw frames and
    /// shoots whatever is on screen.
    /// </summary>
    private static void TailUpdate()
    {
        if (!Enabled || phase != 3)
        {
            return;
        }
        // the final kill triggers an instant replay before the results board,
        // so the tail keeps shooting for a while
        tailFrames++;
        switch (tailFrames)
        {
            case 200:
                Shoot("4-final-kill");
                break;
            case 700:
                Shoot("5-replay");
                break;
            case 1100:
                Shoot("6-round-results");
                break;
            case 1500:
                Shoot("7-later");
                break;
            case 1900:
                Shoot("8-match-results");
                break;
            case 2300:
                Shoot("9-final");
                break;
            case 2400:
                ThreeTeamModule.Log("SELFTEST done");
                Enabled = false;
                break;
        }
    }

    private static void Draw_Postfix()
    {
        TailUpdate();
        if (pendingShot == null || shotDir == null)
        {
            return;
        }
        string name = pendingShot;
        pendingShot = null;
        try
        {
            GraphicsDevice device = Engine.Instance.GraphicsDevice;
            int w = device.PresentationParameters.BackBufferWidth;
            int h = device.PresentationParameters.BackBufferHeight;
            var pixels = new Color[w * h];
            device.GetBackBufferData(pixels);
            using var texture = new Texture2D(device, w, h);
            texture.SetData(pixels);
            Directory.CreateDirectory(shotDir);
            string path = Path.Combine(shotDir, name + ".png");
            using FileStream file = File.Create(path);
            texture.SaveAsPng(file, w, h);
            ThreeTeamModule.Log($"SELFTEST shot: {path} ({w}x{h})");
        }
        catch (Exception e)
        {
            ThreeTeamModule.Log($"SELFTEST shot failed: {e.Message}");
        }
    }

    private static void MenuUpdate_Postfix(MainMenu __instance)
    {
        if (!Enabled)
        {
            return;
        }
        frames++;
        try
        {
            switch (phase)
            {
                case 0 when frames > MenuSettleFrames:
                    OpenTeamSelect(__instance);
                    Advance(1);
                    break;
                case 1 when frames == LookFrames - 30:
                    Shoot("1-team-select");
                    break;
                case 1 when frames > LookFrames:
                    StartMatch();
                    Advance(2);
                    break;
            }
        }
        catch (Exception e)
        {
            ThreeTeamModule.Log($"SELFTEST FAILED in phase {phase}: {e}");
            Enabled = false;
        }
    }

    private static void LevelUpdate_Postfix(Level __instance)
    {
        if (!Enabled)
        {
            return;
        }
        if (phase == 3)
        {
            frames++;
            if (frames == 90)
            {
                Shoot("3-round-over");
            }
            return;
        }
        if (phase != 2)
        {
            return;
        }
        // The intro sequence (VersusStart) freezes everyone and only then calls
        // StartRound; killing during the freeze would set Level.Ending with the
        // round logic still idle, which no real match can do.
        if (__instance.Session.RoundLogic == null || !__instance.Session.RoundLogic.RoundStarted)
        {
            return;
        }
        forceConfirm = false;
        frames++;
        if (frames == 60)
        {
            Shoot("2-match");
        }
        if (reviveTest)
        {
            ReviveUpdate(__instance);
            return;
        }
        if (frames <= 120)
        {
            return;
        }
        try
        {
            // leave the yellow team standing: the round should end on slot 2
            // and the results board should draw one row per group.
            foreach (Player player in __instance.Players)
            {
                if (Teams3.SlotOf(player.PlayerIndex) != 2)
                {
                    player.Die(DeathCause.Curse, -1);
                }
            }
            ThreeTeamModule.Log("SELFTEST phase 3: killed every non-yellow archer "
                                + "(expect round over, yellow scores)");
            Advance(3);
        }
        catch (Exception e)
        {
            ThreeTeamModule.Log($"SELFTEST FAILED in phase 2: {e}");
            Enabled = false;
        }
    }

    private static int revivePasses;
    private static int reviveFails;

    /// <summary>
    /// The Team Revive scenario, driven off the round's frame counter:
    ///
    ///   120  kill blue P2 and the wolf P4
    ///   150  check who got a reviver — P2 must, the wolf must not
    ///   180+ pin the surviving blue P1 on top of P2's corpse (the revive needs
    ///        60 frames of unbroken contact, and its own 60-frame arming delay
    ///        has to pass first)
    ///   420  check that P2 is alive again, and that the round never ended
    ///   450  kill red P3, so the round ends on blue and the run finishes as usual
    /// </summary>
    private static void ReviveUpdate(Level level)
    {
        try
        {
            switch (frames)
            {
                case 120:
                    KillPlayer(level, 1, "blue P2");
                    KillPlayer(level, 3, "wolf P4");
                    break;

                case 150:
                    Check("a reviver for blue P2's corpse", ReviverFor(level, 1) != null);
                    Check("no reviver for the wolf's corpse", ReviverFor(level, 3) == null);
                    Shoot("3-revivers");
                    break;

                case >= 180 and < 420:
                    // stand the survivor on the corpse; Player.Update keeps
                    // applying gravity, so it has to be re-pinned every frame
                    PinOnCorpse(level, reviver: 0, corpseOf: 1);
                    if (frames == 300)
                    {
                        Shoot("4-reviving");
                    }
                    break;

                case 420:
                    Check("blue P2 is alive again", level.GetPlayer(1) != null);
                    Check("the round did not end mid-revive", !level.Ending);
                    Check("the wolf stayed dead", level.GetPlayer(3) == null);
                    Shoot("5-revived");
                    ThreeTeamModule.Log($"SELFTEST REVIVE: {revivePasses} passed, "
                                        + $"{reviveFails} failed");
                    break;

                case 450:
                    KillPlayer(level, 2, "red P3");
                    ThreeTeamModule.Log("SELFTEST phase 3: red is out (expect round over, "
                                        + "blue scores)");
                    Advance(3);
                    break;
            }
        }
        catch (Exception e)
        {
            ThreeTeamModule.Log($"SELFTEST FAILED in the revive run: {e}");
            Enabled = false;
        }
    }

    private static void Check(string what, bool ok)
    {
        if (ok)
        {
            revivePasses++;
        }
        else
        {
            reviveFails++;
        }
        ThreeTeamModule.Log($"SELFTEST REVIVE {(ok ? "PASS" : "FAIL")}: {what}");
    }

    private static void KillPlayer(Level level, int playerIndex, string who)
    {
        Player? player = level.GetPlayer(playerIndex);
        if (player == null)
        {
            ThreeTeamModule.Log($"SELFTEST REVIVE: {who} was already gone");
            return;
        }
        player.Die(DeathCause.Curse, -1);
        ThreeTeamModule.Log($"SELFTEST REVIVE: killed {who}");
    }

    private static TeamReviver? ReviverFor(Level level, int playerIndex)
    {
        foreach (TeamReviver reviver in level[GameTags.TeamReviver])
        {
            if (reviver.Corpse != null && reviver.Corpse.PlayerIndex == playerIndex)
            {
                return reviver;
            }
        }
        return null;
    }

    private static void PinOnCorpse(Level level, int reviver, int corpseOf)
    {
        Player? player = level.GetPlayer(reviver);
        TeamReviver? found = ReviverFor(level, corpseOf);
        if (player == null || found?.Corpse == null)
        {
            return;
        }
        player.Position = found.Corpse.Position;
        player.Speed = Vector2.Zero;
    }

    /// <summary>
    /// In wide mode the levels live in WiderSet's own tower set (its wide towers
    /// carry eight spawn points); otherwise use the first vanilla tower.
    /// </summary>
    private static VersusTowerData PickTower()
    {
        if (forceTower >= 0 && forceTower < GameData.VersusTowers.Count)
        {
            VersusTowerData forced = GameData.VersusTowers[forceTower];
            string what = (forceLevel >= 0 && forceLevel < forced.Levels.Count)
                ? $"{forced.Levels[forceLevel].Path} "
                  + $"(PlayerSpawns={forced.Levels[forceLevel].PlayerSpawns}, "
                  + $"TeamSpawns={forced.Levels[forceLevel].TeamSpawns})"
                : "random level";
            ThreeTeamModule.Log($"SELFTEST forced tower {forceTower} -> {what}");
            return forced;
        }
        if (Wide.IsWide)
        {
            VersusTowerData? wide = GameData.VersusTowers
                .FirstOrDefault(t => t.TowerSet != null && t.TowerSet.Contains("WiderSet"));
            if (wide != null)
            {
                ThreeTeamModule.Log($"SELFTEST tower set: {wide.TowerSet}");
                return wide;
            }
            ThreeTeamModule.Log("SELFTEST could not find a WiderSet tower — using a vanilla one");
        }
        return GameData.VersusTowers[0];
    }

    private static void Advance(int next)
    {
        phase = next;
        frames = 0;
    }

    private static void OpenTeamSelect(MainMenu menu)
    {
        if (playerCount > 4 && !Wide.TrySetWide(true))
        {
            ThreeTeamModule.Log("SELFTEST wanted 8 players but WiderSet is not installed — "
                                + "falling back to 4");
            playerCount = 4;
        }

        for (int i = 0; i < playerCount && i < TFGame.Players.Length; i++)
        {
            TFGame.Players[i] = true;
            TFGame.Characters[i] = i;      // four different archers
            TFGame.AltSelect[i] = ArcherData.ArcherTypes.Normal;
            // the game reads PlayerInputs[i] all over the place for a joined
            // player; hand the unattached slots an idle keyboard input.
            TFGame.PlayerInputs[i] ??= new KeyboardInput();
        }

        MatchSettings settings = MainMenu.VersusMatchSettings;
        IVersusGameModeEntry entry = ThreeTeamModule.ModeEntry;
        settings.IsCustom = true;
        settings.Mode = entry.Modes;
        // CustomVersusModeName is FortRise-internal (the mode carousel sets it),
        // so a scripted start has to go through the setter by reflection.
        AccessTools.PropertySetter(typeof(MatchSettings), "CustomVersusModeName")
                   .Invoke(settings, new object[] { entry.Name });

        menu.State = MainMenu.MenuState.TeamSelect;
        ThreeTeamModule.Log($"SELFTEST phase 1: {playerCount} archers joined, mode set, "
                            + $"team select opened (wide={Wide.IsWide}, "
                            + "expect SOLO | BLUE | YELLOW | RED)");
    }

    private static void StartMatch()
    {
        MatchSettings settings = MainMenu.VersusMatchSettings;
        if (reviveTest)
        {
            // BLUE needs two archers for one to revive the other; the wolf is the
            // control (nothing may ever revive it).
            settings.Teams![0] = Allegiance.Blue;
            settings.Teams![1] = Allegiance.Blue;
            settings.Teams![2] = Allegiance.Red;
            settings.Teams![3] = Allegiance.Neutral;
            settings.Variants!.TeamRevive.Value = true;   // what the menu toggle does
            ThreeTeamModule.Log("SELFTEST revive run: 2v1 + wolf, TeamRevive on "
                                + $"(variant reads {(bool)settings.Variants.TeamRevive})");
        }
        // 4 players: one per column. 8 players: 2v2v2 plus two lone wolves.
        Allegiance[] lineup = playerCount > 4
            ? new[]
            {
                Allegiance.Blue, Allegiance.Blue, Allegiance.Red, Allegiance.Red,
                Teams3.Yellow, Teams3.Yellow, Allegiance.Neutral, Allegiance.Neutral
            }
            : new[] { Allegiance.Blue, Allegiance.Red, Teams3.Yellow, Allegiance.Neutral };
        if (!reviveTest)
        {
            for (int i = 0; i < playerCount; i++)
            {
                settings.Teams![i] = lineup[i];
            }
        }

        settings.LevelSystem = PickTower().GetLevelSystem();
        if (settings.LevelSystem is VersusLevelSystem versus)
        {
            versus.StartOnLevel(forceLevel);
        }
        // Instant match length -> a single round win ends the match, so one run
        // also exercises the match-results screen and the winner's music.
        settings.MatchLength = MatchSettings.MatchLengths.Instant;
        ThreeTeamModule.Log($"SELFTEST phase 2: starting {playerCount} archers "
                            + $"(wide={Wide.IsWide}, gate says "
                            + $"CanStartWithTeams={settings.CanStartWithTeams}, "
                            + $"goal={settings.GoalScore})");
        forceConfirm = true;
        new Session(settings).StartGame();
    }
}
