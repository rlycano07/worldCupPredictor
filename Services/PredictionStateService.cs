using WorldCupPredict.Models;

namespace WorldCupPredict.Services;

public sealed class PredictionStateService(LocalStorageService localStorage, BracketGenerator bracketGenerator)
{
    private const string StorageKey = "world-cup-predictor-state";

    public PredictionState State { get; private set; } = new();
    public bool IsLoaded { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        State = await localStorage.GetAsync<PredictionState>(StorageKey) ?? CreateDefaultState();
        EnsureAllGroupsExist();
        IsLoaded = true;
        Changed?.Invoke();
    }

    public IReadOnlyList<Team> GetGroupRanking(string groupId)
    {
        if (!State.GroupRankings.TryGetValue(groupId, out var ranking))
        {
            return [];
        }

        return ranking.Select(TournamentData.FindTeam).OfType<Team>().ToList();
    }

    public async Task ReorderGroupAsync(string groupId, int fromIndex, int toIndex)
    {
        if (!State.GroupRankings.TryGetValue(groupId, out var ranking) ||
            fromIndex == toIndex ||
            fromIndex < 0 ||
            toIndex < 0 ||
            fromIndex >= ranking.Count ||
            toIndex >= ranking.Count)
        {
            return;
        }

        var movedTeam = ranking[fromIndex];
        ranking.RemoveAt(fromIndex);
        ranking.Insert(toIndex, movedTeam);
        State.KnockoutWinners.Clear();
        await SaveAndNotifyAsync();
    }

    public async Task ResetGroupAsync(string groupId)
    {
        var group = TournamentData.Groups.First(group => group.Id == groupId);
        State.GroupRankings[groupId] = group.Teams.Select(team => team.Id).ToList();
        State.KnockoutWinners.Clear();
        await SaveAndNotifyAsync();
    }

    public async Task ResetAllAsync()
    {
        State = CreateDefaultState();
        await SaveAndNotifyAsync();
    }

    public bool AreGroupsComplete() =>
        TournamentData.Groups.All(group =>
            State.GroupRankings.TryGetValue(group.Id, out var ranking) &&
            ranking.Count == group.Teams.Count &&
            ranking.Distinct().Count() == group.Teams.Count);

    public List<KnockoutRound> GetBracket() => bracketGenerator.Generate(State);

    public Team? GetChampion() => bracketGenerator.GetChampion(State);

    public async Task SelectWinnerAsync(Matchup matchup, Team team)
    {
        if (!matchup.IsComplete)
        {
            return;
        }

        State.KnockoutWinners[matchup.Id] = team.Id;
        ClearAffectedLaterRounds(matchup);
        await SaveAndNotifyAsync();
    }

    public async Task StartAgainAsync()
    {
        await localStorage.RemoveAsync(StorageKey);
        State = CreateDefaultState();
        await SaveAndNotifyAsync();
    }

    public string CreateShareText()
    {
        var champion = GetChampion();
        var championText = champion is null ? "I am building my World Cup prediction." : $"My World Cup champion is {champion.Name}.";
        return $"{championText} Make your own bracket in World Cup Predictor.";
    }

    private void ClearAffectedLaterRounds(Matchup matchup)
    {
        var nextRoundId = BracketGenerator.NextRoundId(matchup.RoundId);
        if (nextRoundId is null)
        {
            return;
        }

        var nextIndex = matchup.Index / 2;

        while (nextRoundId is not null)
        {
            var nextMatchupId = $"{nextRoundId}-{nextIndex + 1}";
            State.KnockoutWinners.Remove(nextMatchupId);
            nextIndex /= 2;
            nextRoundId = BracketGenerator.NextRoundId(nextRoundId);
        }
    }

    private async Task SaveAndNotifyAsync()
    {
        await localStorage.SetAsync(StorageKey, State);
        Changed?.Invoke();
    }

    private static PredictionState CreateDefaultState() =>
        new()
        {
            GroupRankings = TournamentData.Groups.ToDictionary(
                group => group.Id,
                group => group.Teams.Select(team => team.Id).ToList()),
            KnockoutWinners = new Dictionary<string, string>()
        };

    private void EnsureAllGroupsExist()
    {
        foreach (var group in TournamentData.Groups)
        {
            var currentTeamIds = group.Teams.Select(team => team.Id).ToHashSet();

            if (!State.GroupRankings.TryGetValue(group.Id, out var ranking) ||
                ranking.Count != group.Teams.Count ||
                !currentTeamIds.SetEquals(ranking))
            {
                State.GroupRankings[group.Id] = group.Teams.Select(team => team.Id).ToList();
            }
        }
    }
}
