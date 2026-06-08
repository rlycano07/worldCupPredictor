using WorldCupPredict.Models;

namespace WorldCupPredict.Services;

public sealed class BracketGenerator
{
    public const int RequiredBestThirdCount = 8;

    private static readonly (string Id, string Name, int MatchupCount)[] RoundDefinitions =
    [
        ("r32", "Round of 32", 16),
        ("r16", "Round of 16", 8),
        ("qf", "Quarter-finals", 4),
        ("sf", "Semi-finals", 2),
        ("final", "Final", 1)
    ];

    private static readonly IReadOnlyDictionary<string, int[]> MatchNumbersByRound = new Dictionary<string, int[]>
    {
        ["r16"] = [89, 90, 91, 92, 93, 94, 95, 96],
        ["qf"] = [97, 98, 99, 100],
        ["sf"] = [101, 102],
        ["final"] = [104]
    };

    // FIFA World Cup 2026 Regulations, Annex C: official knockout winner routes by match number.
    private static readonly IReadOnlyDictionary<string, AdvancementRoute> AdvancementRoutes = new Dictionary<string, AdvancementRoute>
    {
        ["r32-74"] = new("r16-89", true),
        ["r32-77"] = new("r16-89", false),
        ["r32-73"] = new("r16-90", true),
        ["r32-75"] = new("r16-90", false),
        ["r32-76"] = new("r16-91", true),
        ["r32-78"] = new("r16-91", false),
        ["r32-79"] = new("r16-92", true),
        ["r32-80"] = new("r16-92", false),
        ["r32-83"] = new("r16-93", true),
        ["r32-84"] = new("r16-93", false),
        ["r32-81"] = new("r16-94", true),
        ["r32-82"] = new("r16-94", false),
        ["r32-86"] = new("r16-95", true),
        ["r32-88"] = new("r16-95", false),
        ["r32-85"] = new("r16-96", true),
        ["r32-87"] = new("r16-96", false),
        ["r16-89"] = new("qf-97", true),
        ["r16-90"] = new("qf-97", false),
        ["r16-93"] = new("qf-98", true),
        ["r16-94"] = new("qf-98", false),
        ["r16-91"] = new("qf-99", true),
        ["r16-92"] = new("qf-99", false),
        ["r16-95"] = new("qf-100", true),
        ["r16-96"] = new("qf-100", false),
        ["qf-97"] = new("sf-101", true),
        ["qf-98"] = new("sf-101", false),
        ["qf-99"] = new("sf-102", true),
        ["qf-100"] = new("sf-102", false),
        ["sf-101"] = new("final-104", true),
        ["sf-102"] = new("final-104", false)
    };

    public List<KnockoutRound> Generate(PredictionState state)
    {
        var rounds = RoundDefinitions
            .Select((definition, roundIndex) => new KnockoutRound
            {
                Id = definition.Id,
                Name = definition.Name,
                Matchups = roundIndex == 0
                    ? CreateRoundOf32Matchups()
                    : CreateOfficialNumberedMatchups(definition.Id)
            })
            .ToList();

        ApplyRoundOf32(rounds[0], state);
        ApplyWinners(rounds, state);

        return rounds;
    }

    public Team? GetChampion(PredictionState state)
    {
        if (!state.KnockoutWinners.TryGetValue("final-104", out var winnerId))
        {
            return null;
        }

        return TournamentData.FindTeam(winnerId);
    }

    public IReadOnlySet<string> GetRoundOf32TeamIds(PredictionState state) =>
        GetRoundOf32QualificationStatuses(state).Keys.ToHashSet();

    public IReadOnlyDictionary<string, QualificationStatus> GetRoundOf32QualificationStatuses(PredictionState state)
    {
        var statuses = new Dictionary<string, QualificationStatus>();
        var bestThirdAssignments = AssignBestThirdPlaceSlots(state);

        foreach (var mapping in TournamentData.RoundOf32Mapping)
        {
            AddQualificationStatus(state, mapping.SlotA, bestThirdAssignments, statuses);
            AddQualificationStatus(state, mapping.SlotB, bestThirdAssignments, statuses);
        }

        return statuses;
    }

    public static int RoundIndex(string roundId) =>
        Array.FindIndex(RoundDefinitions, definition => definition.Id == roundId);

    public static string? NextRoundId(string roundId)
    {
        var index = RoundIndex(roundId);
        return index >= 0 && index < RoundDefinitions.Length - 1 ? RoundDefinitions[index + 1].Id : null;
    }

    public static IReadOnlyList<string> GetAffectedLaterMatchupIds(Matchup matchup)
    {
        var affectedMatchupIds = new List<string>();
        var nextMatchupId = matchup.Id;

        while (AdvancementRoutes.TryGetValue(nextMatchupId, out var route))
        {
            affectedMatchupIds.Add(route.TargetMatchupId);
            nextMatchupId = route.TargetMatchupId;
        }

        return affectedMatchupIds;
    }

    private static List<Matchup> CreateRoundOf32Matchups() =>
        TournamentData.RoundOf32Mapping
            .Select((mapping, index) => new Matchup
            {
                Id = mapping.MatchupId,
                RoundId = "r32",
                Index = index
            })
            .ToList();

    private static List<Matchup> CreateOfficialNumberedMatchups(string roundId) =>
        MatchNumbersByRound[roundId]
            .Select((matchNumber, index) => new Matchup
            {
                Id = $"{roundId}-{matchNumber}",
                RoundId = roundId,
                Index = index
            })
            .ToList();

    private static void ApplyRoundOf32(KnockoutRound round, PredictionState state)
    {
        var bestThirdAssignments = AssignBestThirdPlaceSlots(state);

        foreach (var (mapping, index) in TournamentData.RoundOf32Mapping.Select((mapping, index) => (mapping, index)))
        {
            round.Matchups[index].TeamA = ResolveSlot(state, mapping.SlotA, bestThirdAssignments);
            round.Matchups[index].TeamB = ResolveSlot(state, mapping.SlotB, bestThirdAssignments);
        }
    }

    private static void ApplyWinners(List<KnockoutRound> rounds, PredictionState state)
    {
        var matchupsById = rounds
            .SelectMany(round => round.Matchups)
            .ToDictionary(matchup => matchup.Id);

        foreach (var round in rounds)
        {
            foreach (var matchup in round.Matchups)
            {
                if (!state.KnockoutWinners.TryGetValue(matchup.Id, out var winnerId) || !ContainsTeam(matchup, winnerId))
                {
                    continue;
                }

                matchup.WinnerTeamId = winnerId;

                if (!AdvancementRoutes.TryGetValue(matchup.Id, out var route) ||
                    !matchupsById.TryGetValue(route.TargetMatchupId, out var nextMatchup))
                {
                    continue;
                }

                var winner = TournamentData.FindTeam(winnerId);

                if (route.IsTargetSlotA)
                {
                    nextMatchup.TeamA = winner;
                }
                else
                {
                    nextMatchup.TeamB = winner;
                }
            }
        }
    }

    private static Team? ResolveSlot(
        PredictionState state,
        KnockoutSlot slot,
        IReadOnlyDictionary<string, Team>? bestThirdAssignments = null)
    {
        if (slot.IsBestThirdSlot)
        {
            return bestThirdAssignments?.GetValueOrDefault(slot.GroupId);
        }

        return ResolveRankedTeam(state, slot.GroupId, slot.Position);
    }

    private static Dictionary<string, Team> AssignBestThirdPlaceSlots(PredictionState state)
    {
        var bestThirdSlots = TournamentData.RoundOf32Mapping
            .SelectMany(mapping => new[] { mapping.SlotA, mapping.SlotB })
            .Where(slot => slot.IsBestThirdSlot);
        var selectedGroupPriority = state.BestThirdSelectionInitialized
            ? state.BestThirdGroupIds
            : TournamentData.BestThirdPlaceGroupPriority;

        var assignments = new Dictionary<string, string>();
        AssignBestThirdGroups(bestThirdSlots.ToList(), selectedGroupPriority, assignments, state);

        return assignments
            .Select(assignment => new { assignment.Key, Team = ResolveRankedTeam(state, assignment.Value, 3) })
            .Where(assignment => assignment.Team is not null)
            .ToDictionary(assignment => assignment.Key, assignment => assignment.Team!);
    }

    private static bool AssignBestThirdGroups(
        IReadOnlyList<KnockoutSlot> slots,
        IReadOnlyList<string> selectedGroupPriority,
        Dictionary<string, string> assignments,
        PredictionState state,
        int slotIndex = 0)
    {
        if (slotIndex == slots.Count)
        {
            return HasNoPotentialSameGroupRoundOf16Matchup(assignments);
        }

        var slot = slots[slotIndex];
        foreach (var groupId in selectedGroupPriority)
        {
            if (assignments.ContainsValue(groupId) ||
                !slot.EligibleThirdPlaceGroups.Contains(groupId) ||
                ResolveRankedTeam(state, groupId, 3) is null)
            {
                continue;
            }

            assignments[slot.GroupId] = groupId;
            if (AssignBestThirdGroups(slots, selectedGroupPriority, assignments, state, slotIndex + 1))
            {
                return true;
            }

            assignments.Remove(slot.GroupId);
        }

        return false;
    }

    public IReadOnlyList<string> CreateDefaultBestThirdGroupSelection(PredictionState state) =>
        AssignBestThirdPlaceSlots(new PredictionState
            {
                GroupRankings = state.GroupRankings,
                KnockoutWinners = state.KnockoutWinners,
                BestThirdGroupIds = [],
                BestThirdSelectionInitialized = true
            })
            .Values
            .Select(team => TournamentData.Groups.First(group => group.Teams.Any(candidate => candidate.Id == team.Id)).Id)
            .Distinct()
            .Take(RequiredBestThirdCount)
            .ToList();

    private static void AddQualificationStatus(
        PredictionState state,
        KnockoutSlot slot,
        IReadOnlyDictionary<string, Team> bestThirdAssignments,
        Dictionary<string, QualificationStatus> statuses)
    {
        if (slot.IsBestThirdSlot)
        {
            if (bestThirdAssignments.TryGetValue(slot.GroupId, out var bestThirdTeam))
            {
                statuses[bestThirdTeam.Id] = new QualificationStatus("Best 3rd", "best-third");
            }

            return;
        }

        var team = ResolveRankedTeam(state, slot.GroupId, slot.Position);
        if (team is null)
        {
            return;
        }

        var status = slot.Position switch
        {
            1 => new QualificationStatus("1st", "first-place"),
            2 => new QualificationStatus("2nd", "second-place"),
            _ => new QualificationStatus($"{slot.Position}th", "qualified")
        };

        statuses.TryAdd(team.Id, status);
    }

    private static Team? ResolveRankedTeam(PredictionState state, string groupId, int position)
    {
        if (!state.GroupRankings.TryGetValue(groupId, out var ranking) || ranking.Count < position)
        {
            return null;
        }

        return TournamentData.FindTeam(ranking[position - 1]);
    }

    private static bool ContainsTeam(Matchup matchup, string teamId) =>
        matchup.TeamA?.Id == teamId || matchup.TeamB?.Id == teamId;

    private static bool HasNoPotentialSameGroupRoundOf16Matchup(IReadOnlyDictionary<string, string> bestThirdAssignments)
    {
        var roundOf16Pairings = AdvancementRoutes
            .Where(route => route.Key.StartsWith("r32-", StringComparison.Ordinal))
            .GroupBy(route => route.Value.TargetMatchupId)
            .Select(group => group.Select(route => route.Key).ToList());

        foreach (var pairing in roundOf16Pairings)
        {
            if (pairing.Count != 2)
            {
                continue;
            }

            var firstGroups = GetPossibleWinnerGroupIds(pairing[0], bestThirdAssignments);
            var secondGroups = GetPossibleWinnerGroupIds(pairing[1], bestThirdAssignments);

            if (firstGroups.Overlaps(secondGroups))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> GetPossibleWinnerGroupIds(
        string matchupId,
        IReadOnlyDictionary<string, string> bestThirdAssignments)
    {
        var mapping = TournamentData.RoundOf32Mapping.First(mapping => mapping.MatchupId == matchupId);
        var groupIds = new HashSet<string>();

        AddPossibleWinnerGroupId(mapping.SlotA, bestThirdAssignments, groupIds);
        AddPossibleWinnerGroupId(mapping.SlotB, bestThirdAssignments, groupIds);

        return groupIds;
    }

    private static void AddPossibleWinnerGroupId(
        KnockoutSlot slot,
        IReadOnlyDictionary<string, string> bestThirdAssignments,
        HashSet<string> groupIds)
    {
        if (slot.IsBestThirdSlot)
        {
            if (bestThirdAssignments.TryGetValue(slot.GroupId, out var assignedGroupId))
            {
                groupIds.Add(assignedGroupId);
            }

            return;
        }

        groupIds.Add(slot.GroupId);
    }

    private sealed record AdvancementRoute(string TargetMatchupId, bool IsTargetSlotA);
}
