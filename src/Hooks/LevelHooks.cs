using System.Collections.Generic;
using FortRise;
using HarmonyLib;
using TowerFall;

namespace ThreeTeamMode.Hooks;

/// <summary>
/// Which levels this mode may pick.
///
/// Vanilla filters two ways and picks the branch by a literal mode comparison:
/// Team Deathmatch takes levels with `TeamSpawns >= maxTeamSize`, everything
/// else takes levels with `PlayerSpawns >= PlayerAmount`. A custom mode is
/// neither, so we silently inherited the free-for-all rule — which happens to
/// exclude the ~25 team-designed levels that ship no PlayerSpawn markers at all.
///
/// Relying on that is fragile (it is one upstream `if` away from flipping, and
/// custom tower packs ship any mix of markers), and it also throws away good
/// levels. So this mode filters by the question that actually matters: can the
/// level seat everyone with the markers our spawner uses — PlayerSpawn plus both
/// team sides? The spawner then adapts to whatever mix it finds.
/// </summary>
internal static class LevelHooks
{
    public static void Load(IHarmony harmony)
    {
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(VersusTowerData), nameof(VersusTowerData.GetLevels)),
            prefix: new HarmonyMethod(typeof(LevelHooks), nameof(GetLevels_Prefix)));
    }

    private static bool GetLevels_Prefix(VersusTowerData __instance, MatchSettings matchSettings,
                                        ref List<string> __result)
    {
        if (!Teams3.AnyActive(matchSettings))
        {
            return true;
        }

        var levels = new List<string>();
        int players = TFGame.PlayerAmount;
        foreach (VersusLevelData level in __instance.Levels)
        {
            // TeamSpawns is min(A, B), so the pair contributes twice.
            if (level.PlayerSpawns + level.TeamSpawns * 2 >= players)
            {
                levels.Add(level.Path);
            }
        }

        if (levels.Count == 0)
        {
            return true;   // nothing qualifies: let vanilla decide rather than crash
        }
        __result = levels;
        return false;
    }
}
