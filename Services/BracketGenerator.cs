using WorldCupPredict.Models;

namespace WorldCupPredict.Services;

public sealed class BracketGenerator
{
    private static readonly (string Id, string Name, int MatchupCount)[] RoundDefinitions =
    [
        ("r32", "Round of 32", 16),
        ("r16", "Round of 16", 8),
        ("qf", "Quarter-finals", 4),
        ("sf", "Semi-finals", 2),
        ("final", "Final", 1)
    ];

    public List<KnockoutRound> Generate(PredictionState state)
    {
        var rounds = RoundDefinitions
            .Select((definition, roundIndex) => new KnockoutRound
            {
                Id = definition.Id,
                Name = definition.Name,
                Matchups = roundIndex == 0
                    ? CreateRoundOf32Matchups()
                    : CreateNumberedMatchups(definition.Id, definition.MatchupCount)
            })
            .ToList();

        ApplyRoundOf32(rounds[0], state);
        ApplyWinners(rounds, state);

        return rounds;
    }

    public Team? GetChampion(PredictionState state)
    {
        if (!state.KnockoutWinners.TryGetValue("final-1", out var winnerId))
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

    private static List<Matchup> CreateRoundOf32Matchups() =>
        TournamentData.RoundOf32Mapping
            .Select((mapping, index) => new Matchup
            {
                Id = mapping.MatchupId,
                RoundId = "r32",
                Index = index
            })
            .ToList();

    private static List<Matchup> CreateNumberedMatchups(string roundId, int matchupCount) =>
        Enumerable.Range(0, matchupCount)
            .Select(index => new Matchup
            {
                Id = $"{roundId}-{index + 1}",
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
        for (var roundIndex = 0; roundIndex < rounds.Count; roundIndex++)
        {
            var round = rounds[roundIndex];

            foreach (var matchup in round.Matchups)
            {
                if (!state.KnockoutWinners.TryGetValue(matchup.Id, out var winnerId) || !ContainsTeam(matchup, winnerId))
                {
                    continue;
                }

                matchup.WinnerTeamId = winnerId;

                if (roundIndex == rounds.Count - 1)
                {
                    continue;
                }

                var nextMatchup = rounds[roundIndex + 1].Matchups[matchup.Index / 2];
                var winner = TournamentData.FindTeam(winnerId);

                if (matchup.Index % 2 == 0)
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
        var assignments = new Dictionary<string, Team>();
        var usedGroups = new HashSet<string>();
        var bestThirdSlots = TournamentData.RoundOf32Mapping
            .SelectMany(mapping => new[] { mapping.SlotA, mapping.SlotB })
            .Where(slot => slot.IsBestThirdSlot);

        foreach (var slot in bestThirdSlots)
        {
            var selectedGroup = TournamentData.BestThirdPlaceGroupPriority
                .Where(groupId => slot.EligibleThirdPlaceGroups.Contains(groupId))
                .FirstOrDefault(groupId => !usedGroups.Contains(groupId) && ResolveRankedTeam(state, groupId, 3) is not null);

            if (selectedGroup is null)
            {
                continue;
            }

            var team = ResolveRankedTeam(state, selectedGroup, 3);
            if (team is null)
            {
                continue;
            }

            assignments[slot.GroupId] = team;
            usedGroups.Add(selectedGroup);
        }

        return assignments;
    }

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
}
