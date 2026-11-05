using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using FortRise;
using HarmonyLib;
using TowerFall;

namespace TeamsPlus.Hooks;

/// <summary>
/// The "missed one = crash" list.
///
/// Most of the engine handles a neutral player gracefully because FFA runs on
/// Allegiance.Neutral (feather arrows, laser arrows, shields and dust all check
/// for it and fall back to archer colours). Two places do not, and both are
/// reachable in a three-team match:
///
///   * SensorBlock colours its gems with ArcherData.Teams[(int)Teams[squisher]]
///     — a direct array index, so a lone wolf squisher reads Teams[-1].
///   * anything calling ArcherData.Get(Allegiance) with Neutral, handled in
///     DataHooks by returning the white wolf data.
///
/// Also keeps Teams3.Live in step with the running session.
/// </summary>
internal static class SafetyHooks
{
    public static void Load(IHarmony harmony)
    {
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(Session), nameof(Session.StartGame)),
            postfix: new HarmonyMethod(typeof(SafetyHooks), nameof(StartGame_Postfix)));

        int patched = 0;
        foreach (Type nested in typeof(SensorBlock).GetNestedTypes(
                     BindingFlags.Public | BindingFlags.NonPublic))
        {
            MethodInfo? moveNext = AccessTools.DeclaredMethod(nested, "MoveNext");
            if (moveNext == null)
            {
                continue;
            }
            harmony.Patch(moveNext,
                transpiler: new HarmonyMethod(typeof(SafetyHooks), nameof(TeamLookup_Transpiler)));
            patched++;
        }
        ThreeTeamModule.Log($"safety: sensor-block team lookup patched in {patched} coroutine body(s)");
    }

    private static void StartGame_Postfix(Session __instance)
    {
        Teams3.Live = Teams3.IsOurs(__instance.MatchSettings);
        if (Teams3.Live)
        {
            ThreeTeamModule.Log("three-team match live");
        }
    }

    /// <summary>
    /// Redirects MatchTeams' indexer to Teams3.TeamDataAllegiance inside the
    /// patched method only, so a wolf resolves to its own white TeamData entry
    /// instead of index -1.
    /// </summary>
    private static IEnumerable<CodeInstruction> TeamLookup_Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo original = AccessTools.DeclaredMethod(typeof(MatchTeams), "get_Item");
        MethodInfo replacement = AccessTools.DeclaredMethod(typeof(Teams3),
                                                           nameof(Teams3.TeamDataAllegiance));
        foreach (CodeInstruction instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                && ReferenceEquals(instruction.operand, original))
            {
                yield return new CodeInstruction(OpCodes.Call, replacement);
                continue;
            }
            yield return instruction;
        }
    }
}
