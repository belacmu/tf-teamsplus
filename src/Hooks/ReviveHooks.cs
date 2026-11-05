using FortRise;
using HarmonyLib;
using Monocle;
using TowerFall;

namespace TeamsPlus.Hooks;

/// <summary>
/// Team Revive.
///
/// The variant is offered in every versus mode's variant screen, but the thing
/// it turns on is spawned by a literal mode comparison in PlayerCorpse.Added:
///
///     else if (MatchSettings.Mode == Modes.TeamDeathmatch &amp;&amp; Variants.TeamRevive)
///         Level.Add(new TeamReviver(this, TeamReviver.Modes.TeamDeathmatch));
///
/// Our mode is Modes.Custom, so the reviver was never created and the variant
/// did nothing — the same class of bug as the level filter in LevelHooks. A
/// postfix adds the reviver ourselves; vanilla's branch cannot have fired, so
/// there is nothing to suppress.
///
/// Everything downstream of that already works unchanged, because TeamReviver
/// matches revivers to corpses by ALLEGIANCE rather than by a two-team table:
///
///   * a yellow archer revives a yellow corpse (both are Allegiance 2), and the
///     revive arrow is tinted from ArcherData.Get(TeamColor) — gold, via
///     DataHooks;
///   * the shock circle spares the reviver's own team (Teams[reviver] compared
///     against each player's allegiance, no array indexed by allegiance);
///   * GroupCheckForRoundOver already counts a corpse that is mid-revive as its
///     group still standing, exactly as RoundLogic.TeamCheckForRoundOver does.
///
/// LONE WOLVES ARE DELIBERATELY EXCLUDED. Each wolf has its own private
/// allegiance, so no one could ever reach the revive — the reviver would only
/// float a revive prompt over the corpse that nothing can satisfy. A wolf that
/// dies stays dead, which is the rule the mode is built on.
/// </summary>
internal static class ReviveHooks
{
    public static void Load(IHarmony harmony)
    {
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(PlayerCorpse), "Added"),
            postfix: new HarmonyMethod(typeof(ReviveHooks), nameof(Added_Postfix)));

        // A revive can still fail at the last moment (the corpse ended up inside
        // a wall, or got squished). Vanilla re-checks for a winner there, again
        // behind a mode comparison we do not match: without it a round whose last
        // living group was a failed revive never ends.
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(TeamReviver), "FinishReviving"),
            postfix: new HarmonyMethod(typeof(ReviveHooks), nameof(FinishReviving_Postfix)));
    }

    private static void Added_Postfix(PlayerCorpse __instance)
    {
        Level? level = __instance.Level;
        MatchSettings? settings = level?.Session?.MatchSettings;
        MatchVariants? variants = settings?.Variants;
        if (__instance.PlayerIndex == -1 || level == null || variants == null
            || !Teams3.IsOurs(settings) || !variants.TeamRevive)
        {
            return;
        }

        // teams only: a wolf's allegiance is its own, so nobody can revive it
        int team = (int)__instance.Allegiance;
        if (team < 0 || team >= Teams3.TeamCount)
        {
            return;
        }

        // another mod patching the same method could have added one already
        foreach (TeamReviver existing in level[GameTags.TeamReviver])
        {
            if (existing.Corpse == __instance)
            {
                return;
            }
        }

        level.Add(new TeamReviver(__instance, TeamReviver.Modes.TeamDeathmatch));
        ThreeTeamModule.Log($"team revive: reviver spawned for P{__instance.PlayerIndex + 1} "
                            + $"(allegiance {team})");
    }

    private static void FinishReviving_Postfix(TeamReviver __instance, Player? __result)
    {
        Session? session = __instance.Level?.Session;
        if (__result != null || session == null || !Teams3.SessionActive(session))
        {
            return;
        }
        // The caller clears Corpse.Revived immediately after we return, so the
        // check has to wait a frame — asking now would still see the corpse as
        // its group's last hope. RoundLogic.OnUpdate runs before the revivers do.
        (session.RoundLogic as ThreeTeamRoundLogic)?.RequestWinCheck();
    }
}
