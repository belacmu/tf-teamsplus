using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TeamsPlus;

/// <summary>
/// Team Deathmatch's round logic, generalised from "two teams" to "N groups".
///
/// Vanilla's two team-shaped methods are binary in a way no flag can fix, so
/// they are reimplemented here rather than hooked:
///   * TeamCheckForRoundOver builds a bool[2] and decides the winner with
///     array[0] == array[1];
///   * SpawnPlayersTeams reads TeamSpawnA/TeamSpawnB and runs two hard-coded
///     loops, so there is nowhere for a third group to stand (SpawnGroups
///     assigns the level's spawn points itself).
/// Everything else (crown events, scoring, final kills, treasure) is inherited.
/// </summary>
public class ThreeTeamRoundLogic : RoundLogic
{
    private readonly RoundEndCounter roundEndCounter;
    private bool done;
    private bool wasFinalKill;
    private bool winCheckRequested;

    public ThreeTeamRoundLogic(Session session)
        : base(session, canHaveMiasma: true)
    {
        roundEndCounter = new RoundEndCounter(session);
    }

    public override void OnLevelLoadFinish()
    {
        base.OnLevelLoadFinish();
        Session.CurrentLevel.Add(new VersusStart(Session));
        SpawnGroups();
        Players = TFGame.PlayerAmount;
    }

    public override void OnRoundStart()
    {
        base.OnRoundStart();
        SpawnTreasureChestsVersus();
    }

    public override void OnUpdate()
    {
        SessionStats.TimePlayed += Engine.DeltaTicks;
        base.OnUpdate();
        if (winCheckRequested)
        {
            winCheckRequested = false;
            CheckForWin();
        }
        if (!RoundStarted || done || !Session.CurrentLevel.Ending || !Session.CurrentLevel.CanEnd)
        {
            return;
        }
        if (!roundEndCounter.Finished)
        {
            roundEndCounter.Update();
            return;
        }
        done = true;
        if (Session.CurrentLevel.Players.Count > 0
            && Session.CurrentLevel.Players[0] is Player survivor)
        {
            int slot = Teams3.SlotOf(survivor.PlayerIndex);
            if (slot >= 0)
            {
                AddScore(slot, 1);
            }
        }
        InsertCrownEvent();
        Session.EndRound();
    }

    public override void OnPlayerDeath(Player player, PlayerCorpse corpse, int playerIndex,
                                       DeathCause deathType, Vector2 position, int killerIndex)
    {
        base.OnPlayerDeath(player, corpse, playerIndex, deathType, position, killerIndex);
        if (wasFinalKill && Session.CurrentLevel.LivingPlayers == 0)
        {
            wasFinalKill = false;
            CancelFinalKill();
            return;
        }
        if (!GroupCheckForRoundOver(out int survivingSlot))
        {
            return;
        }
        Session.CurrentLevel.Ending = true;
        if (survivingSlot >= 0 && survivingSlot < Session.Scores.Length
            && Session.Scores[survivingSlot] >= Session.MatchSettings.GoalScore - 1)
        {
            wasFinalKill = true;
            FinalKillGroup(corpse, survivingSlot);
        }
    }

    /// <summary>
    /// Ask for a round-over check on the next update. Used when a team revive
    /// fails: vanilla re-checks inline, but the corpse is still flagged as
    /// reviving at that moment, so the answer is only correct a frame later.
    /// </summary>
    public void RequestWinCheck()
    {
        winCheckRequested = true;
    }

    public void CheckForWin()
    {
        if (GroupCheckForRoundOver(out int _))
        {
            Session.CurrentLevel.Ending = true;
        }
    }

    /// <summary>
    /// N-group replacement for RoundLogic.TeamCheckForRoundOver: the round is
    /// over once at most one group still has someone alive or revivable.
    /// </summary>
    public bool GroupCheckForRoundOver(out int survivingSlot)
    {
        var alive = new bool[Teams3.GroupCount];
        foreach (Player player in Session.CurrentLevel.Players)
        {
            if (!player.Dead)
            {
                Mark(alive, player.PlayerIndex);
            }
        }
        foreach (PlayerCorpse corpse in Session.CurrentLevel[GameTags.Corpse])
        {
            if (corpse.Revived)
            {
                Mark(alive, corpse.PlayerIndex);
            }
        }

        int standing = 0;
        survivingSlot = -1;
        for (int slot = 0; slot < alive.Length; slot++)
        {
            if (alive[slot])
            {
                standing++;
                survivingSlot = slot;
            }
        }
        if (standing != 1)
        {
            survivingSlot = -1;
        }
        return standing <= 1;
    }

    private static void Mark(bool[] alive, int playerIndex)
    {
        int slot = Teams3.SlotOf(playerIndex);
        if (slot >= 0 && slot < alive.Length)
        {
            alive[slot] = true;
        }
    }

    /// <summary>
    /// FinalKillTeams by group slot (vanilla's version compares allegiances,
    /// which cannot tell two lone wolves apart).
    /// </summary>
    private void FinalKillGroup(PlayerCorpse corpse, int slot)
    {
        var spotlight = new List<LevelEntity>();
        for (int i = 0; i < Teams3.PlayerCap; i++)
        {
            if (!Teams3.Joined(i) || Teams3.SlotOf(i) != slot)
            {
                continue;
            }
            Session.MatchStats[i].GotWin = true;
            LevelEntity entity = Session.CurrentLevel.GetPlayerOrCorpse(i);
            if (entity != null && entity != corpse)
            {
                spotlight.Add(entity);
            }
        }
        Session.CurrentLevel.LightingLayer.SetSpotlight(spotlight.ToArray());
        FinalKillNoSpotlight();
    }

    // ---- spawning -----------------------------------------------------------

    private readonly struct Group
    {
        public readonly Allegiance Team;
        public readonly List<int> Members;

        public Group(Allegiance team)
        {
            Team = team;
            Members = new List<int>();
        }
    }

    /// <summary>
    /// Every archer on one of the level's own spawn points, assigned by us.
    ///
    /// The level's PlayerSpawn, TeamSpawnA and TeamSpawnB markers (and a plain
    /// TeamSpawn) are one set of points; which marker type a point was does not
    /// matter. Blue's home is the leftmost point and Red's the rightmost (with
    /// one of them missing, a third team takes its side); each of those teams
    /// takes the free points nearest its home. A third team in between takes
    /// the group of the points left that is close together and away from the
    /// others (the middle, when the level has points there); lone wolves last, each on the free point farthest from
    /// everyone. When the points do not fit the teams, the team that fills last
    /// is the one split; only when they run out do archers share one.
    ///
    /// No randomness: the same level and teams give the same spawns on every
    /// machine (online play needs both sides to agree).
    /// </summary>
    private void SpawnGroups()
    {
        MatchSettings settings = Session.MatchSettings;
        if (Teams3.GroupCount <= 0)
        {
            Teams3.BuildGroups(settings);
        }

        var teams = new List<Group>();
        for (int t = 0; t < Teams3.TeamCount; t++)
        {
            var group = new Group((Allegiance)t);
            for (int i = 0; i < Teams3.PlayerCap; i++)
            {
                if (Session.ShouldSpawn(i) && Teams3.Joined(i) && settings.Teams![i] == (Allegiance)t)
                {
                    group.Members.Add(i);
                }
            }
            if (group.Members.Count > 0)
            {
                teams.Add(group);
            }
        }
        var wolves = new List<int>();
        for (int i = 0; i < Teams3.PlayerCap; i++)
        {
            if (Session.ShouldSpawn(i) && Teams3.IsWolf(settings, i))
            {
                wolves.Add(i);
            }
        }
        if (teams.Count == 0 && wolves.Count == 0)
        {
            return;
        }

        List<Vector2> spots = Spots();
        if (spots.Count == 0)
        {
            ThreeTeamModule.Log("no spawn markers on this level — nobody spawned");
            return;
        }
        var taken = new bool[spots.Count];
        int shared = 0;

        // Blue's home is the leftmost point and Red's the rightmost (a third team
        // takes a free side); those teams fill first, each its nearest points
        int leftmost = 0, rightmost = 0;
        for (int k = 1; k < spots.Count; k++)
        {
            if (spots[k].X < spots[leftmost].X)
            {
                leftmost = k;
            }
            if (spots[k].X > spots[rightmost].X)
            {
                rightmost = k;
            }
        }
        var homes = new int[teams.Count];
        bool leftUsed = false, rightUsed = false;
        for (int t = 0; t < teams.Count; t++)
        {
            homes[t] = -1;
            if (teams[t].Team == Allegiance.Blue)
            {
                homes[t] = leftmost;
                leftUsed = true;
            }
            else if (teams[t].Team == Allegiance.Red)
            {
                homes[t] = rightmost;
                rightUsed = true;
            }
        }
        for (int t = 0; t < teams.Count; t++)
        {
            if (homes[t] >= 0)
            {
                continue;
            }
            if (!leftUsed)
            {
                homes[t] = leftmost;
                leftUsed = true;
            }
            else if (!rightUsed)
            {
                homes[t] = rightmost;
                rightUsed = true;
            }
        }
        for (int t = 0; t < teams.Count; t++)
        {
            if (homes[t] < 0)
            {
                continue;
            }
            foreach (int member in teams[t].Members)
            {
                int spot = NearestFree(spots, taken, spots[homes[t]]);
                if (spot < 0)
                {
                    spot = homes[t];
                    shared++;
                }
                taken[spot] = true;
                SpawnOne(member, spots[spot]);
            }
        }

        // a third team: the tightest group of free points (the middle, when the
        // level has one); split only when the free points are
        for (int t = 0; t < teams.Count; t++)
        {
            if (homes[t] >= 0)
            {
                continue;
            }
            int anchor = TightestFree(spots, taken, teams[t].Members.Count);
            foreach (int member in teams[t].Members)
            {
                int spot = anchor < 0 ? -1 : NearestFree(spots, taken, spots[anchor]);
                if (spot < 0)
                {
                    spot = FarthestFree(spots, taken, false);
                    shared++;
                }
                taken[spot] = true;
                SpawnOne(member, spots[spot]);
            }
        }

        // lone wolves: each the free point farthest from everyone placed
        for (int w = 0; w < wolves.Count; w++)
        {
            int spot = FarthestFree(spots, taken, true);
            if (spot < 0)
            {
                spot = FarthestFree(spots, taken, false);
                shared++;
            }
            taken[spot] = true;
            SpawnOne(wolves[w], spots[spot]);
        }

        if (++spawnLogs <= 8)
        {
            ThreeTeamModule.Log($"spawns: {spots.Count} point(s) for {teams.Count} team(s) and "
                                + $"{wolves.Count} lone wolf/wolves"
                                + (shared > 0 ? $", {shared} archer(s) sharing a point (too few points)" : ""));
        }
    }

    private static int spawnLogs;

    /// <summary>The level's spawn points: every PlayerSpawn and team marker, a point once.</summary>
    private List<Vector2> Spots()
    {
        var spots = new List<Vector2>();
        string[] kinds = { "PlayerSpawn", "TeamSpawnA", "TeamSpawnB", "TeamSpawn" };
        foreach (string kind in kinds)
        {
            foreach (Vector2 position in Session.CurrentLevel.GetXMLPositions(kind))
            {
                bool known = false;
                foreach (Vector2 s in spots)
                {
                    if (Vector2.DistanceSquared(s, position) < 64f)
                    {
                        known = true;
                        break;
                    }
                }
                if (!known)
                {
                    spots.Add(position);
                }
            }
        }
        return spots;
    }

    /// <summary>
    /// The anchor of the best free group of count points for a team in between:
    /// its (count - 1) nearest free points close to it, and it far from the
    /// archers already placed (squared distances, straight across the screen:
    /// spread minus distance to the nearest placed). -1 if no point is free.
    /// </summary>
    private static int TightestFree(List<Vector2> spots, bool[] taken, int count)
    {
        int best = -1;
        float bestCost = float.MaxValue;
        var picked = new bool[spots.Count];
        for (int k = 0; k < spots.Count; k++)
        {
            if (taken[k])
            {
                continue;
            }
            for (int m = 0; m < spots.Count; m++)
            {
                picked[m] = taken[m] || m == k;
            }
            float spread = 0f;
            for (int n = 1; n < count; n++)
            {
                int nearest = NearestFree(spots, picked, spots[k]);
                if (nearest < 0)
                {
                    break;
                }
                picked[nearest] = true;
                spread += Vector2.DistanceSquared(spots[k], spots[nearest]);
            }
            float away = float.MaxValue;
            for (int m = 0; m < spots.Count; m++)
            {
                if (taken[m])
                {
                    away = System.Math.Min(away, Vector2.DistanceSquared(spots[k], spots[m]));
                }
            }
            float cost = spread - (away == float.MaxValue ? 0f : away);
            if (cost < bestCost)
            {
                best = k;
                bestCost = cost;
            }
        }
        return best;
    }

    /// <summary>The free point nearest home, straight across the screen (a team keeps to its area); -1 if none.</summary>
    private static int NearestFree(List<Vector2> spots, bool[] taken, Vector2 home)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int k = 0; k < spots.Count; k++)
        {
            float d = Vector2.DistanceSquared(spots[k], home);
            if (!taken[k] && d < bestDistance)
            {
                best = k;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>
    /// The point (a free one if freeOnly) farthest from every taken point, with
    /// wrap-around (the distance an enemy really is); with nothing taken, the
    /// point nearest the middle. -1 if freeOnly and none is free.
    /// </summary>
    private static int FarthestFree(List<Vector2> spots, bool[] taken, bool freeOnly)
    {
        int best = -1;
        float bestScore = float.MinValue;
        for (int k = 0; k < spots.Count; k++)
        {
            if (freeOnly && taken[k])
            {
                continue;
            }
            float score = float.MaxValue;
            bool anyTaken = false;
            for (int u = 0; u < spots.Count; u++)
            {
                if (taken[u] && u != k)
                {
                    anyTaken = true;
                    score = System.Math.Min(score, Distance2(spots[k], spots[u]));
                }
            }
            if (!anyTaken)
            {
                float dx = spots[k].X - Teams3.LevelWidth * 0.5f;
                score = -(dx * dx);
            }
            if (score > bestScore)
            {
                best = k;
                bestScore = score;
            }
        }
        return best;
    }

    private void SpawnOne(int playerIndex, Vector2 position)
    {
        // Teams spawn under their own allegiance; a lone wolf spawns under its
        // private one (Teams3.WolfAllegiance), which is what makes it hostile to
        // every other wolf and paints it white.
        Allegiance allegiance = Teams3.SpawnAllegiance(Session.MatchSettings, playerIndex);
        var player = new Player(playerIndex, position + Vector2.UnitY * 2f,
                                allegiance, allegiance,
                                Session.GetPlayerInventory(playerIndex),
                                Session.GetSpawnHatState(playerIndex),
                                frozen: true, flash: true, indicator: true);
        Session.CurrentLevel.Add(player);
    }

    /// <summary>Squared distance with horizontal wrap-around (towers wrap).</summary>
    private static float Distance2(Vector2 a, Vector2 b)
    {
        float dx = System.Math.Abs(a.X - b.X);
        if (Teams3.LevelWidth - dx < dx)
        {
            dx = Teams3.LevelWidth - dx;
        }
        float dy = System.Math.Abs(a.Y - b.Y);
        return dx * dx + dy * dy;
    }
}
