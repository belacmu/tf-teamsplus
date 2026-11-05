using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using FortRise;
using HarmonyLib;
using TowerFall;

namespace TeamsPlus.Hooks;

/// <summary>
/// The rule layer: who may start a match, how big a "team" is, which score row
/// a player owns, and the one place the engine mistakes two lone wolves for
/// team-mates.
/// </summary>
internal static class RulesHooks
{
    public static void Load(IHarmony harmony)
    {
        harmony.Patch(
            AccessTools.DeclaredPropertyGetter(typeof(MatchTeams), nameof(MatchTeams.ProperlyAssigned)),
            prefix: new HarmonyMethod(typeof(RulesHooks), nameof(ProperlyAssigned_Prefix)));

        // CanStartWithTeams does NOT call ProperlyAssigned — it inlines the
        // same two-team check (the Switch build hit this too), so the start
        // gate needs both patched.
        harmony.Patch(
            AccessTools.DeclaredPropertyGetter(typeof(MatchSettings),
                                               nameof(MatchSettings.CanStartWithTeams)),
            prefix: new HarmonyMethod(typeof(RulesHooks), nameof(CanStartWithTeams_Prefix)));

        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(MatchSettings), nameof(MatchSettings.GetMaxTeamSize)),
            prefix: new HarmonyMethod(typeof(RulesHooks), nameof(GetMaxTeamSize_Prefix)));

        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(MatchSettings), nameof(MatchSettings.GetPlayerTeamSize)),
            prefix: new HarmonyMethod(typeof(RulesHooks), nameof(GetPlayerTeamSize_Prefix)));

        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(MatchSettings), nameof(MatchSettings.IsFriendly)),
            prefix: new HarmonyMethod(typeof(RulesHooks), nameof(IsFriendly_Prefix)));

        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(Session), nameof(Session.GetScoreIndex)),
            prefix: new HarmonyMethod(typeof(RulesHooks), nameof(GetScoreIndex_Prefix)));

        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(RoundLogic), nameof(RoundLogic.OnPlayerDeath)),
            transpiler: new HarmonyMethod(typeof(RulesHooks), nameof(OnPlayerDeath_Transpiler)));
    }

    /// <summary>
    /// Start gate. Vanilla demands "nobody neutral, and both Blue and Red
    /// present". Ours: at least two groups, where a group is a non-empty team
    /// or a single lone wolf. No per-team cap (the user's rule for the Switch
    /// build was "no team cap, just don't let everyone be on one team"), and
    /// sitting in the neutral column is now a choice rather than an omission.
    /// </summary>
    private static bool ProperlyAssigned_Prefix(MatchTeams __instance, ref bool __result)
    {
        if (!Teams3.MenuActive)
        {
            return true;
        }
        __result = CountGroups(__instance) >= 2;
        return false;
    }

    /// <summary>
    /// Same rule as ProperlyAssigned, for the copy of the check that lives in
    /// MatchSettings. This is the one the select screen actually asks.
    /// </summary>
    private static bool CanStartWithTeams_Prefix(MatchSettings __instance, ref bool __result)
    {
        if (!Teams3.IsOurs(__instance))
        {
            return true;
        }
        __result = CountGroups(__instance.Teams!) >= 2;
        return false;
    }

    private static int CountGroups(MatchTeams teams)
    {
        var teamSeen = new bool[Teams3.TeamCount];
        int wolves = 0;
        for (int i = 0; i < Teams3.PlayerCap; i++)
        {
            if (!Teams3.Joined(i))
            {
                continue;
            }
            Allegiance team = teams[i];
            if (team == Allegiance.Neutral)
            {
                wolves++;
            }
            else if ((int)team >= 0 && (int)team < Teams3.TeamCount)
            {
                teamSeen[(int)team] = true;
            }
        }
        int groups = wolves;
        foreach (bool seen in teamSeen)
        {
            if (seen)
            {
                groups++;
            }
        }
        return groups;
    }

    /// <summary>
    /// Vanilla builds `new int[2]` and indexes it by allegiance — the classic
    /// third-team out-of-bounds. Drives the results board layout (how many
    /// P-labels fit beside a score row) and the treasure handicap.
    /// </summary>
    private static bool GetMaxTeamSize_Prefix(MatchSettings __instance, ref int __result)
    {
        if (!Teams3.AnyActive(__instance) || !__instance.TeamMode)
        {
            return true;
        }
        int max = 0;
        for (int t = 0; t < Teams3.TeamCount; t++)
        {
            max = Math.Max(max, Teams3.CountOn(__instance, (Allegiance)t));
        }
        for (int i = 0; i < Teams3.PlayerCap; i++)
        {
            if (Teams3.IsWolf(__instance, i))
            {
                max = Math.Max(max, 1);
                break;
            }
        }
        __result = Math.Max(max, 1);
        return false;
    }

    /// <summary>
    /// Team size drives the arrow handicap. Vanilla counts everyone sharing
    /// your allegiance, which would make every lone wolf a member of one big
    /// "neutral team".
    /// </summary>
    private static bool GetPlayerTeamSize_Prefix(MatchSettings __instance, int playerIndex,
                                                 ref int __result)
    {
        if (!Teams3.AnyActive(__instance) || !__instance.TeamMode)
        {
            return true;
        }
        if (Teams3.IsWolf(__instance, playerIndex))
        {
            __result = 1;
            return false;
        }
        return true;   // real teams: vanilla's count is already correct
    }

    /// <summary>
    /// Friendly-fire bookkeeping (bomb/explosion kill credit). Two neutrals are
    /// NOT friends in this mode. Note the combat layer needs no help: the engine
    /// already treats Neutral as hostile to everything, which is what makes
    /// lone wolves work at all.
    /// </summary>
    private static bool IsFriendly_Prefix(MatchSettings __instance, int playerA, int playerB,
                                          ref bool __result)
    {
        if (!Teams3.AnyActive(__instance) || !__instance.TeamMode)
        {
            return true;
        }
        if (playerA == -1 || playerB == -1)
        {
            __result = false;
            return false;
        }
        if (Teams3.IsWolf(__instance, playerA) || Teams3.IsWolf(__instance, playerB))
        {
            __result = playerA == playerB;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Which score row a player owns. Vanilla returns the raw allegiance in
    /// team mode, which is -1 for a wolf and leaves gaps when a team is empty;
    /// ours returns the packed group slot.
    /// </summary>
    private static bool GetScoreIndex_Prefix(Session __instance, int playerIndex, ref int __result)
    {
        if (!Teams3.SessionActive(__instance))
        {
            return true;
        }
        if (playerIndex == -1)
        {
            __result = -1;
            return false;
        }
        if (Teams3.GroupCount <= 0)
        {
            Teams3.BuildGroups(__instance.MatchSettings);
        }
        int slot = Teams3.SlotOf(playerIndex);
        __result = slot >= 0 ? slot : 0;
        return false;
    }

    /// <summary>
    /// RoundLogic.OnPlayerDeath decides DeathType.Team by comparing the two
    /// players' allegiances, so wolf-kills-wolf would be filed as a team kill
    /// (wrong stats, wrong award text). Redirect just those two calls at a
    /// helper that gives every wolf a private allegiance value.
    /// </summary>
    private static IEnumerable<CodeInstruction> OnPlayerDeath_Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo original = AccessTools.DeclaredMethod(
            typeof(MatchSettings), nameof(MatchSettings.GetPlayerAllegiance));
        MethodInfo replacement = AccessTools.DeclaredMethod(
            typeof(RulesHooks), nameof(AllegianceForKillType));

        int swapped = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                && ReferenceEquals(instruction.operand, original))
            {
                yield return new CodeInstruction(OpCodes.Call, replacement);
                swapped++;
                continue;
            }
            yield return instruction;
        }
        ThreeTeamModule.Log($"OnPlayerDeath: redirected {swapped} allegiance lookup(s) "
                            + "for wolf-vs-wolf kill typing");
    }

    /// <summary>
    /// Same shape as MatchSettings.GetPlayerAllegiance (the instance becomes the
    /// first argument), but every lone wolf answers with a value of its own so
    /// no two wolves ever compare equal.
    /// </summary>
    public static Allegiance AllegianceForKillType(MatchSettings settings, int playerIndex)
    {
        if (Teams3.IsOurs(settings) && Teams3.IsWolf(settings, playerIndex))
        {
            return (Allegiance)(100 + playerIndex);
        }
        return settings.GetPlayerAllegiance(playerIndex);
    }
}
