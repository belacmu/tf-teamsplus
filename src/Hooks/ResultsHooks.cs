using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using FortRise;
using HarmonyLib;
using TowerFall;

namespace TeamsPlus.Hooks;

/// <summary>
/// The results board.
///
/// Score rows, coin rows and crowns are all driven by session.Scores.Length, so
/// they generalise for free once the score array is group-sized. The one
/// hard-coded two left in the round-results screen is a `new int[2]` that
/// counts how many "P#" labels have been stacked next to each score row (and is
/// then re-read to place the crowns) — an out-of-bounds the moment a third
/// group exists. It lives inside the screen's coroutine, so the patch targets
/// the compiler-generated MoveNext.
/// </summary>
internal static class ResultsHooks
{
    public static void Load(IHarmony harmony)
    {
        int patched = 0;
        foreach (Type nested in typeof(VersusRoundResults).GetNestedTypes(
                     BindingFlags.Public | BindingFlags.NonPublic))
        {
            MethodInfo? moveNext = AccessTools.DeclaredMethod(nested, "MoveNext");
            if (moveNext == null)
            {
                continue;
            }
            harmony.Patch(moveNext,
                transpiler: new HarmonyMethod(typeof(ResultsHooks), nameof(LabelArray_Transpiler)));
            patched++;
        }
        ThreeTeamModule.Log($"results board: patched {patched} coroutine body(s) "
                            + "for group-sized P-label rows");
    }

    /// <summary>
    /// Rewrites `new int[2]` into `new int[LabelRowCount()]`. Only touches the
    /// exact `ldc.i4.2; newarr System.Int32` pair, so any other constant in the
    /// method is left alone.
    /// </summary>
    private static IEnumerable<CodeInstruction> LabelArray_Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo counter = AccessTools.DeclaredMethod(typeof(ResultsHooks), nameof(LabelRowCount));
        var list = new List<CodeInstruction>(instructions);
        for (int i = 0; i < list.Count; i++)
        {
            bool isTwo = list[i].opcode == OpCodes.Ldc_I4_2
                         || (list[i].opcode == OpCodes.Ldc_I4 && (list[i].operand as int?) == 2)
                         || (list[i].opcode == OpCodes.Ldc_I4_S
                             && Convert.ToInt32(list[i].operand) == 2);
            bool nextIsIntArray = i + 1 < list.Count
                                  && list[i + 1].opcode == OpCodes.Newarr
                                  && list[i + 1].operand as Type == typeof(int);
            if (isTwo && nextIsIntArray)
            {
                var call = new CodeInstruction(OpCodes.Call, counter);
                call.labels.AddRange(list[i].labels);
                call.blocks.AddRange(list[i].blocks);
                yield return call;
                continue;
            }
            yield return list[i];
        }
    }

    /// <summary>
    /// How many score rows the board may have to label. Never smaller than
    /// vanilla's 2, so the patch is inert in Team Deathmatch and harmless (a
    /// slightly longer scratch array) in the other modes.
    /// </summary>
    public static int LabelRowCount() => Math.Max(2, Teams3.GroupCount);
}
