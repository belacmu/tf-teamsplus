using FortRise;
using Microsoft.Xna.Framework;
using TowerFall;

namespace TeamsPlus;

/// <summary>
/// The versus mode itself. Registering this is what gets us an entry in the
/// GAME MODE carousel, an icon, and — because IsTeamMode is true — the whole
/// team pipeline: FortRise replaces MatchSettings.TeamMode with this flag, so
/// team select, team spawning, friendly fire, team scoring and the team results
/// board all switch on without a single extra hook. On the Switch build this
/// was one hand-written hook on a getter with 59 call sites.
/// </summary>
public sealed class ThreeTeamGameMode : IVersusGameMode
{
    public string Name => "Teams+";

    public Color NameColor => Teams3.YellowColorA;

    public ISubtextureEntry Icon => Assets3.ModeIcon;

    public bool IsTeamMode => true;

    /// <summary>Three groups need three archers; two teams + a wolf also works.</summary>
    public int GetMinimumTeamPlayers(MatchSettings matchSettings) => 3;

    public void OnStartGame(Session session)
    {
        // Pack groups into score slots and size the score arrays to match, so
        // the results board draws exactly as many rows as there are groups.
        // Session's constructor allocated int[2] (TeamMode ? 2 : 4).
        Teams3.BuildGroups(session.MatchSettings);
        session.Scores = new int[Teams3.GroupCount];
        session.OldScores = new int[Teams3.GroupCount];
        ThreeTeamModule.Log($"match start: {Teams3.GroupCount} group(s) -> score slots "
                            + Teams3.DescribeGroups(session.MatchSettings));
    }

    public RoundLogic OnCreateRoundLogic(Session session) => new ThreeTeamRoundLogic(session);
}
