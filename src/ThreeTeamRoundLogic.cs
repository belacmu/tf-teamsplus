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
///     loops, so there is nowhere for a third group to stand.
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
    /// Group-aware spawn placement, ported from the Switch build's
    /// SpawnPlayersTeams replacement.
    ///
    /// * With two groups, whoever is present takes the normal left/right split
    ///   (Blue keeps left and Red keeps right when they are in; a third team or
    ///   a wolf takes the vacant side), so a 2-group match feels like vanilla.
    /// * With three or more, the side groups keep their markers and every other
    ///   group is ANCHORED at the free marker farthest from everyone already
    ///   placed, with teammates clustered around it.
    /// * A used-position registry stops two archers sharing a marker, which maps
    ///   invite by reusing platforms across their marker sets.
    /// </summary>
    private void SpawnGroups()
    {
        MatchSettings settings = Session.MatchSettings;
        if (Teams3.GroupCount <= 0)
        {
            Teams3.BuildGroups(settings);
        }

        var groups = new List<Group>();
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
                groups.Add(group);
            }
        }
        for (int i = 0; i < Teams3.PlayerCap; i++)
        {
            if (Session.ShouldSpawn(i) && Teams3.IsWolf(settings, i))
            {
                var wolf = new Group(Allegiance.Neutral);
                wolf.Members.Add(i);
                groups.Add(wolf);
            }
        }
        if (groups.Count == 0)
        {
            return;
        }

        // Left/right owners: Blue and Red keep their vanilla sides; with only two
        // groups on the board the vacant side is handed to whoever is left.
        int left = groups.FindIndex(g => g.Team == Allegiance.Blue);
        int right = groups.FindIndex(g => g.Team == Allegiance.Red);
        if (groups.Count <= 2)
        {
            for (int g = 0; g < groups.Count; g++)
            {
                if (g == left || g == right)
                {
                    continue;
                }
                if (left < 0)
                {
                    left = g;
                }
                else if (right < 0)
                {
                    right = g;
                }
            }
        }

        List<Vector2> markersA = Session.CurrentLevel.GetXMLPositions("TeamSpawnA");
        List<Vector2> markersB = Session.CurrentLevel.GetXMLPositions("TeamSpawnB");
        List<Vector2> neutral = Session.CurrentLevel.GetXMLPositions("PlayerSpawn");
        foreach (Vector2 shared in Session.CurrentLevel.GetXMLPositions("TeamSpawn"))
        {
            (shared.X <= Teams3.LevelWidth * 0.5f ? markersA : markersB).Add(shared);
        }

        // Everything the spawner can stand an archer on.
        var pool = new List<Vector2>();
        pool.AddRange(neutral);
        pool.AddRange(markersA);
        pool.AddRange(markersB);

        // How many archers would have to find neutral ground (everyone not on
        // the two side groups)? On a team-designed level — team markers, no
        // PlayerSpawn at all — that number cannot be met, and anchoring would
        // drop the third group into somebody's base. Carve the level up instead.
        int anchoring = 0;
        for (int g = 0; g < groups.Count; g++)
        {
            if (g != left && g != right)
            {
                anchoring += groups[g].Members.Count;
            }
        }
        if (anchoring > 0 && neutral.Count < anchoring)
        {
            ThreeTeamModule.Log($"level has {neutral.Count} neutral spawn(s) for {anchoring} "
                                + $"archer(s) with no side — splitting all {pool.Count} marker(s) "
                                + $"between {groups.Count} groups instead");
            SpawnByPartition(groups, pool, left, right);
            return;
        }

        markersA.Sort(SortLeft);
        markersB.Sort(SortRight);
        int seed = new System.Random().Next();
        markersA.Shuffle(new System.Random(seed));
        markersB.Shuffle(new System.Random(seed));

        var used = new List<Vector2>();

        SpawnSide(left, groups, markersA, used, "left");
        SpawnSide(right, groups, markersB, used, "right");

        for (int g = 0; g < groups.Count; g++)
        {
            if (g == left || g == right)
            {
                continue;
            }
            SpawnAnchored(groups[g], pool, used);
        }
    }

    /// <summary>
    /// Divide the level between the groups left-to-right and give each one a
    /// contiguous block of markers, sized by how many archers it has.
    ///
    /// This is the strategy for levels that only carry team markers: three
    /// groups on a two-base level need a third base, and the honest way to make
    /// one is to treat every marker as available and split the map, rather than
    /// wedge the odd group in among an existing team. Blue keeps the leftmost
    /// block and Red the rightmost, so a match on a familiar team level still
    /// opens roughly where players expect.
    /// </summary>
    private void SpawnByPartition(List<Group> groups, List<Vector2> pool, int left, int right)
    {
        if (pool.Count == 0)
        {
            ThreeTeamModule.Log("no markers at all on this level — nobody spawned");
            return;
        }
        pool.Sort((a, b) => a.X.CompareTo(b.X));

        // order: Blue's block first, Red's last, everyone else in between
        var order = new List<int>();
        if (left >= 0)
        {
            order.Add(left);
        }
        for (int g = 0; g < groups.Count; g++)
        {
            if (g != left && g != right)
            {
                order.Add(g);
            }
        }
        if (right >= 0 && right != left)
        {
            order.Add(right);
        }

        int totalPlayers = 0;
        foreach (Group group in groups)
        {
            totalPlayers += group.Members.Count;
        }

        int cursor = 0;
        for (int i = 0; i < order.Count; i++)
        {
            Group group = groups[order[i]];
            int groupsLeft = order.Count - i - 1;
            int markersLeft = pool.Count - cursor;
            // proportional share, but never take so much that a later group
            // cannot get at least one marker
            int share = (totalPlayers > 0)
                ? (int)System.Math.Round((double)pool.Count * group.Members.Count / totalPlayers)
                : 1;
            share = System.Math.Clamp(share, 1, System.Math.Max(1, markersLeft - groupsLeft));

            for (int k = 0; k < group.Members.Count; k++)
            {
                // spread the group's archers across its own block
                int index = (share <= 1)
                    ? cursor
                    : cursor + (int)((double)k * (share - 1) / System.Math.Max(1, group.Members.Count - 1));
                index = System.Math.Clamp(index, 0, pool.Count - 1);
                SpawnOne(group.Members[k], pool[index]);
            }
            ThreeTeamModule.Log($"{Describe(group)} takes markers {cursor}..{cursor + share - 1} "
                                + $"of {pool.Count} (left to right)");
            cursor += share;
            totalPlayers -= group.Members.Count;
        }
    }

    private static string Describe(Group group) =>
        group.Team == Allegiance.Neutral
            ? $"wolf P{group.Members[0] + 1}"
            : (group.Team == Allegiance.Blue ? "BLUE" : group.Team == Allegiance.Red ? "RED" : "YELLOW");

    private void SpawnSide(int groupIndex, List<Group> groups, List<Vector2> markers,
                           List<Vector2> used, string label)
    {
        if (groupIndex < 0)
        {
            return;
        }
        Group group = groups[groupIndex];
        if (markers.Count == 0)
        {
            ThreeTeamModule.Log($"no {label}-side markers on this level — "
                                + $"{group.Team} falls back to the anchor pool");
            SpawnAnchored(group, Session.CurrentLevel.GetXMLPositions("PlayerSpawn"), used);
            return;
        }
        for (int k = 0; k < group.Members.Count; k++)
        {
            Vector2 position = markers[k % markers.Count];
            if (PickFree(markers, used, out Vector2 free))
            {
                position = free;
            }
            SpawnOne(group.Members[k], position);
            used.Add(position);
        }
    }

    private void SpawnAnchored(Group group, List<Vector2> pool, List<Vector2> used)
    {
        if (pool.Count == 0)
        {
            ThreeTeamModule.Log($"no markers at all for {group.Team} — not spawned");
            return;
        }

        // anchor = the free marker whose nearest already-used position is the
        // farthest away; with nothing placed yet, prefer the centre of the map.
        int anchor = -1;
        float best = float.MinValue;
        for (int pass = 0; pass < MinDistances.Length && anchor < 0; pass++)
        {
            for (int k = 0; k < pool.Count; k++)
            {
                if (!IsFree(pool[k], used, MinDistances[pass]))
                {
                    continue;
                }
                float score;
                if (used.Count == 0)
                {
                    float dx = pool[k].X - Teams3.LevelWidth * 0.5f;
                    score = -(dx * dx);
                }
                else
                {
                    score = float.MaxValue;
                    foreach (Vector2 u in used)
                    {
                        score = System.Math.Min(score, Distance2(pool[k], u));
                    }
                }
                if (anchor < 0 || score > best)
                {
                    anchor = k;
                    best = score;
                }
            }
        }
        if (anchor < 0)
        {
            anchor = 0;
        }

        Vector2 anchorPosition = pool[anchor];
        for (int k = 0; k < group.Members.Count; k++)
        {
            Vector2 position = anchorPosition;
            if (k > 0)
            {
                // teammates cluster: nearest free marker to the anchor
                int nearest = -1;
                float nearestDistance = float.MaxValue;
                for (int pass = 0; pass < MinDistances.Length && nearest < 0; pass++)
                {
                    for (int m = 0; m < pool.Count; m++)
                    {
                        if (!IsFree(pool[m], used, MinDistances[pass]))
                        {
                            continue;
                        }
                        float d = Distance2(pool[m], anchorPosition);
                        if (nearest < 0 || d < nearestDistance)
                        {
                            nearest = m;
                            nearestDistance = d;
                        }
                    }
                }
                if (nearest >= 0)
                {
                    position = pool[nearest];
                }
            }
            SpawnOne(group.Members[k], position);
            used.Add(position);
        }
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

    /// <summary>Three relaxing passes: a comfortable gap, a small gap, exact overlap only.</summary>
    private static readonly float[] MinDistances = { 400f, 64f, 4f };

    private static bool PickFree(List<Vector2> markers, List<Vector2> used, out Vector2 position)
    {
        for (int pass = 0; pass < MinDistances.Length; pass++)
        {
            foreach (Vector2 candidate in markers)
            {
                if (IsFree(candidate, used, MinDistances[pass]))
                {
                    position = candidate;
                    return true;
                }
            }
        }
        position = default;
        return false;
    }

    private static bool IsFree(Vector2 position, List<Vector2> used, float minDistance2)
    {
        foreach (Vector2 u in used)
        {
            if (Distance2(position, u) < minDistance2)
            {
                return false;
            }
        }
        return true;
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

    private static int SortLeft(Vector2 a, Vector2 b)
    {
        if (a.Y != b.Y)
        {
            return (int)((a.Y - b.Y) * 10f);
        }
        return (int)((b.X - a.X) * 10f);
    }

    private static int SortRight(Vector2 a, Vector2 b)
    {
        if (a.Y != b.Y)
        {
            return (int)((a.Y - b.Y) * 10f);
        }
        return (int)((a.X - b.X) * 10f);
    }
}
