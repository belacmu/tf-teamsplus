using System;
using System.Xml;
using FortRise;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TeamsPlus.Hooks;

/// <summary>
/// The data spine: give the game a third TeamData (and one per possible lone
/// wolf) and make everything that reads team data survive an index above 1.
///
/// Vanilla sizes ArcherData.Teams to exactly 2 and ArcherData.Get(Allegiance)
/// throws for anything that is not Blue or Red. Those two are the choke point
/// for the whole cosmetic layer — aimers, particles, chalice glow, reviver
/// glow, feather arrows, victory music — so patching them lights all of it up
/// at once.
/// </summary>
internal static class DataHooks
{
    public static void Load(IHarmony harmony)
    {
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(ArcherData), nameof(ArcherData.Initialize)),
            postfix: new HarmonyMethod(typeof(DataHooks), nameof(Initialize_Postfix)));

        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(ArcherData), nameof(ArcherData.Get),
                                       new[] { typeof(Allegiance) }),
            prefix: new HarmonyMethod(typeof(DataHooks), nameof(GetTeamData_Prefix)));

        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(ArcherData), nameof(ArcherData.GetTeamParticleTypes)),
            prefix: new HarmonyMethod(typeof(DataHooks), nameof(GetTeamParticleTypes_Prefix)));
    }

    /// <summary>
    /// Teams becomes [Blue, Red, Yellow, wolf0..wolfN]. The wolf entries are
    /// white "no affiliation" data: they are only ever reached by score-slot
    /// lookups (the results board plays ArcherData.Teams[winnerSlot]'s victory
    /// music), never by a player's allegiance, since a wolf stays Neutral.
    /// </summary>
    private static void Initialize_Postfix()
    {
        // One entry per team plus one per possible lone wolf. Sized off
        // TFGame.Players so an 8-player mod (WiderSet) — or any other mod that
        // widens the player array — is covered without a recompile.
        int wolves = Math.Max(TFGame.Players.Length, Teams3.MaxPlayers);
        var teams = new TeamData[Teams3.TeamCount + wolves];
        teams[0] = ArcherData.Teams[0];
        teams[1] = ArcherData.Teams[1];
        teams[2] = MakeTeamData("YellowTeam", Teams3.YellowColorA, Teams3.YellowColorB,
                                "aimers/yellow");
        for (int i = Teams3.TeamCount; i < teams.Length; i++)
        {
            teams[i] = MakeTeamData("WolfTeam", Color.White, Color.White, "aimers/white");
        }
        // Teams has a private setter; assign through it rather than poking the
        // backing field so a future FortRise change to the property still works.
        AccessTools.PropertySetter(typeof(ArcherData), nameof(ArcherData.Teams))
                   .Invoke(null, new object[] { teams });
        ThreeTeamModule.Log($"team data: {teams.Length} entries "
                            + "(Blue, Red, Yellow + one per lone wolf)");
    }

    /// <summary>
    /// TeamData's only constructor parses an XmlElement, so synthesise one
    /// rather than reflecting over the fields — this keeps working if TeamData
    /// grows a field.
    /// </summary>
    private static TeamData MakeTeamData(string name, Color colorA, Color colorB, string aimer)
    {
        var document = new XmlDocument();
        XmlElement root = document.CreateElement(name);
        document.AppendChild(root);
        AddChild(document, root, "ColorA", Hex(colorA));
        AddChild(document, root, "ColorB", Hex(colorB));
        // aimers/yellow and aimers/white already ship in the game atlas.
        AddChild(document, root, "Aimer", aimer);
        // "Team" -> Music.PlayImmediate("VictoryTeam"), the same jingle the
        // blue and red teams use.
        AddChild(document, root, "VictoryMusic", "Team");
        return new TeamData(root);
    }

    private static void AddChild(XmlDocument document, XmlElement parent, string name, string text)
    {
        XmlElement child = document.CreateElement(name);
        child.InnerText = text;
        parent.AppendChild(child);
    }

    private static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// Vanilla: Blue -> Teams[0], Red -> Teams[1], anything else throws. Any
    /// non-negative allegiance now maps straight to its Teams entry, so Yellow
    /// gets colours and an aimer everywhere the game asks for team data.
    /// </summary>
    private static bool GetTeamData_Prefix(Allegiance team, ref TeamData __result)
    {
        int index = (int)team;
        if (ArcherData.Teams == null)
        {
            return true;
        }
        if (index < 0)
        {
            // Neutral. Vanilla throws, and several team-mode call sites reach
            // here for a lone wolf (chalice fill, chalice pad, chalice ghost),
            // so hand back the white "no affiliation" data while our match is
            // running and let vanilla throw everywhere else.
            if (!Teams3.Live || ArcherData.Teams.Length <= Teams3.TeamCount)
            {
                return true;
            }
            __result = ArcherData.Teams[Teams3.TeamCount];
            return false;
        }
        if (index >= ArcherData.Teams.Length)
        {
            return true;
        }
        __result = ArcherData.Teams[index];
        return false;
    }

    /// <summary>
    /// Vanilla allocates `new ParticleType[2]` here, which is what makes a
    /// third team an out-of-bounds in SIX different particle statics
    /// (TeamDust / TeamDash / TeamHyperJump / TeamFeathers /
    /// TeamLaserArrowGlow / TeamLaserArrowTrail). Sizing this to Teams.Length
    /// fixes all of them in one place — including the laser-arrow colours that
    /// were never solved on the Switch build.
    /// </summary>
    private static bool GetTeamParticleTypes_Prefix(ParticleType prototype, bool firstLight,
                                                    float firstColorMult, bool secondLight,
                                                    float secondColorMult,
                                                    ref ParticleType[] __result)
    {
        TeamData[] teams = ArcherData.Teams;
        if (teams == null || teams.Length <= 2)
        {
            return true;
        }
        var array = new ParticleType[teams.Length];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = new ParticleType(prototype);
            array[i].Color = (firstLight ? teams[i].ColorB : teams[i].ColorA) * firstColorMult;
            array[i].Color2 = (secondLight ? teams[i].ColorA : teams[i].ColorB) * secondColorMult;
        }
        __result = array;
        return false;
    }
}
