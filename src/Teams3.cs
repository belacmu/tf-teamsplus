using System;
using Microsoft.Xna.Framework;
using TowerFall;

namespace TeamPlus;

/// <summary>
/// The spine of the mod: what "Yellow" is, who is a lone wolf, and the
/// group -> score-slot mapping every rule and every UI row hangs off.
///
/// Design (carried over from the Switch build, three-team-mod/DESIGN.md):
///   * teams are Blue / Red / Yellow, Yellow being (Allegiance)2 — the enum is
///     int-backed so widening it costs nothing; what costs is every array of
///     length 2 indexed by an allegiance (see Hooks/DataHooks + RulesHooks).
///   * the NEUTRAL column of the select screen is a real choice, not a parking
///     spot: a neutral player is a LONE WOLF, its own one-archer group. The
///     engine already treats Allegiance.Neutral as hostile-to-everything
///     (Player.cs "Allegiance == Neutral || entity.Allegiance != Allegiance"),
///     so wolves need bookkeeping, not combat work.
///   * scores are per GROUP, and groups are packed into slots 0..N-1 at match
///     start, so the results board has no empty rows.
/// </summary>
public static class Teams3
{
    public const Allegiance Yellow = (Allegiance)2;

    /// <summary>Blue, Red, Yellow.</summary>
    public const int TeamCount = 3;

    /// <summary>
    /// Upper bound for per-slot tables. Vanilla is 4 players; WiderSet lifts it
    /// to 8, and ArcherData.Teams is sized for the worst case so that a wolf
    /// score slot is always a valid index (VersusRoundResults plays the winner's
    /// victory music via ArcherData.Teams[winnerSlot]).
    /// </summary>
    public const int MaxPlayers = 8;

    public static readonly Color YellowColorA = HexColor(0xFCE83C);
    public static readonly Color YellowColorB = HexColor(0xFCF4A4);

    /// <summary>Play-field width, for wrap-around spawn distances (420 in wide).</summary>
    public static float LevelWidth => Wide.LevelWidth;

    private static readonly int[] slotOf = new int[MaxPlayers];
    private static readonly int[] firstPlayerOfSlot = new int[MaxPlayers + TeamCount];

    public static int GroupCount { get; private set; }

    /// <summary>
    /// True while a three-team MATCH is running. Some engine code reaches for
    /// team data with no MatchSettings in hand (a chalice pad's owner colour, a
    /// sensor block's squisher colour), so those hooks need a plain flag.
    /// Maintained by a postfix on Session.StartGame.
    /// </summary>
    public static bool Live { get; set; }

    public static Color HexColor(int rgb) =>
        new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    // ---- mode detection -----------------------------------------------------

    /// <summary>True when these settings are running OUR versus mode.</summary>
    public static bool IsOurs(MatchSettings? settings) =>
        settings != null && settings.IsCustom
        && settings.CustomVersusGameMode is ThreeTeamGameMode;

    /// <summary>True while the player is setting up our mode in the menus.</summary>
    public static bool MenuActive => IsOurs(MainMenu.VersusMatchSettings);

    public static bool SessionActive(Session? session) =>
        session != null && IsOurs(session.MatchSettings);

    /// <summary>
    /// Menu-or-match: the rule hooks fire in both contexts (the select screen
    /// asks CanStartWithTeams, the match asks GetMaxTeamSize), and only one of
    /// the two settings objects is the live one at any time.
    /// </summary>
    public static bool AnyActive(MatchSettings? settings) =>
        IsOurs(settings) || MenuActive;

    // ---- players and groups -------------------------------------------------

    public static int PlayerCap => Math.Min(TFGame.Players.Length, MaxPlayers);

    public static bool Joined(int index) =>
        index >= 0 && index < TFGame.Players.Length && TFGame.Players[index];

    public static Allegiance TeamOf(MatchSettings settings, int index) =>
        settings.Teams![index];

    public static bool IsWolf(MatchSettings settings, int index) =>
        Joined(index) && settings.Teams![index] == Allegiance.Neutral;

    /// <summary>Number of joined players sitting on the given team column.</summary>
    public static int CountOn(MatchSettings settings, Allegiance team)
    {
        int n = 0;
        for (int i = 0; i < PlayerCap; i++)
        {
            if (Joined(i) && settings.Teams![i] == team)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// Distinct groups in the current line-up: every non-empty team counts once,
    /// every lone wolf counts as its own. This is the number the start gate and
    /// the score array are built from.
    /// </summary>
    public static int CountGroups(MatchSettings settings)
    {
        int groups = 0;
        for (int t = 0; t < TeamCount; t++)
        {
            if (CountOn(settings, (Allegiance)t) > 0)
            {
                groups++;
            }
        }
        for (int i = 0; i < PlayerCap; i++)
        {
            if (IsWolf(settings, i))
            {
                groups++;
            }
        }
        return groups;
    }

    /// <summary>
    /// Assign score slots: live teams first (Blue, Red, Yellow — skipping empty
    /// ones so no blank row is drawn), then one slot per lone wolf. Stable for
    /// the whole match; players cannot join mid-match.
    /// </summary>
    public static void BuildGroups(MatchSettings settings)
    {
        for (int i = 0; i < slotOf.Length; i++)
        {
            slotOf[i] = -1;
        }
        for (int i = 0; i < firstPlayerOfSlot.Length; i++)
        {
            firstPlayerOfSlot[i] = -1;
        }

        int slot = 0;
        for (int t = 0; t < TeamCount; t++)
        {
            var team = (Allegiance)t;
            if (CountOn(settings, team) == 0)
            {
                continue;
            }
            for (int i = 0; i < PlayerCap; i++)
            {
                if (Joined(i) && settings.Teams![i] == team)
                {
                    slotOf[i] = slot;
                    if (firstPlayerOfSlot[slot] < 0)
                    {
                        firstPlayerOfSlot[slot] = i;
                    }
                }
            }
            slot++;
        }
        for (int i = 0; i < PlayerCap; i++)
        {
            if (IsWolf(settings, i))
            {
                slotOf[i] = slot;
                firstPlayerOfSlot[slot] = i;
                slot++;
            }
        }
        GroupCount = Math.Max(slot, 1);
    }

    public static int SlotOf(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= slotOf.Length)
        {
            return -1;
        }
        return slotOf[playerIndex];
    }

    /// <summary>Lowest-index player in a slot — used to colour a results row.</summary>
    public static int FirstPlayerOfSlot(int slot) =>
        (slot >= 0 && slot < firstPlayerOfSlot.Length) ? firstPlayerOfSlot[slot] : -1;

    /// <summary>
    /// The allegiance a lone wolf FIGHTS and LOOKS as: its own slot past the
    /// three teams, which is both unique (so two wolves are enemies, neither
    /// revives the other under Team Revive, and neither trades arrows) and
    /// backed by a white TeamData entry.
    ///
    /// The white is on the ACCENTS — aimer, shield, dust, dash, feathers,
    /// lasers — not the sprite: a wolf keeps its own archer. This is the Switch
    /// build's white-shield rule, and it exists because an archer's own palette
    /// can otherwise read as a team's (the blue archer standing next to the blue
    /// team).
    /// </summary>
    public static Allegiance WolfAllegiance(int playerIndex) =>
        (Allegiance)(TeamCount + Math.Clamp(playerIndex, 0, MaxPlayers - 1));

    /// <summary>What to hand the Player constructor for allegiance AND team colour.</summary>
    public static Allegiance SpawnAllegiance(MatchSettings settings, int playerIndex) =>
        IsWolf(settings, playerIndex)
            ? WolfAllegiance(playerIndex)
            : settings.GetPlayerAllegiance(playerIndex);

    /// <summary>True for a team colour that means "lone wolf".</summary>
    public static bool IsWolfColor(Allegiance teamColor) => (int)teamColor >= TeamCount;

    /// <summary>One-line group summary for the log, e.g. "0=BLUE(2) 1=RED(1) 2=WOLF P4".</summary>
    public static string DescribeGroups(MatchSettings settings)
    {
        var parts = new System.Text.StringBuilder();
        for (int slot = 0; slot < GroupCount; slot++)
        {
            int first = FirstPlayerOfSlot(slot);
            if (first < 0)
            {
                continue;
            }
            Allegiance team = settings.Teams![first];
            if (parts.Length > 0)
            {
                parts.Append(' ');
            }
            if (team == Allegiance.Neutral)
            {
                parts.Append($"{slot}=WOLF P{first + 1}");
            }
            else
            {
                string name = team == Allegiance.Blue ? "BLUE"
                            : team == Allegiance.Red ? "RED" : "YELLOW";
                parts.Append($"{slot}={name}({CountOn(settings, team)})");
            }
        }
        return parts.ToString();
    }

    /// <summary>
    /// The allegiance to READ TEAM DATA WITH for a player: teams answer with
    /// themselves, a lone wolf answers with its own white entry in
    /// ArcherData.Teams. Used where the engine indexes ArcherData.Teams
    /// directly (SensorBlock) and would otherwise read Teams[-1].
    /// </summary>
    public static Allegiance TeamDataAllegiance(MatchTeams teams, int playerIndex)
    {
        Allegiance team = teams[playerIndex];
        if (Live && team == Allegiance.Neutral && playerIndex >= 0)
        {
            return (Allegiance)(TeamCount + Math.Min(playerIndex, MaxPlayers - 1));
        }
        return team;
    }

    /// <summary>
    /// TeamData index for a group slot: teams keep their own palette, a wolf
    /// slot gets one of the white "no affiliation" entries appended by
    /// DataHooks. Only ever called with a slot that BuildGroups produced.
    /// </summary>
    public static int TeamDataIndexOfSlot(MatchSettings settings, int slot)
    {
        int player = FirstPlayerOfSlot(slot);
        if (player < 0)
        {
            return 0;
        }
        var team = settings.Teams![player];
        if (team == Allegiance.Neutral)
        {
            return TeamCount + Math.Min(player, MaxPlayers - 1);
        }
        return (int)team;
    }
}
