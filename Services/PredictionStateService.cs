using WorldCupPredict.Models;

namespace WorldCupPredict.Services;

public sealed class PredictionStateService(LocalStorageService localStorage, BracketGenerator bracketGenerator)
{
    private const string StorageKey = "world-cup-predictor-state";
    private const int CurrentStateVersion = 1;
    private readonly object initializationLock = new();
    private Task? initializationTask;

    private PredictionState state = CreateDefaultState();

    public bool IsLoaded { get; private set; }
    public event Action? Changed;

    public Task InitializeAsync()
    {
        if (IsLoaded)
        {
            return Task.CompletedTask;
        }

        lock (initializationLock)
        {
            initializationTask ??= InitializeCoreAsync();
            return initializationTask;
        }
    }

    public IReadOnlyList<Team> GetGroupRanking(string groupId)
    {
        if (!state.GroupRankings.TryGetValue(groupId, out var ranking))
        {
            return [];
        }

        return ranking.Select(TournamentData.FindTeam).OfType<Team>().ToList();
    }

    public async Task ReorderGroupAsync(string groupId, int fromIndex, int toIndex)
    {
        if (!state.GroupRankings.TryGetValue(groupId, out var ranking) ||
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
        state.KnockoutWinners.Clear();
        await SaveAndNotifyAsync();
    }

    public async Task ResetGroupAsync(string groupId)
    {
        var group = TournamentData.Groups.First(group => group.Id == groupId);
        state.GroupRankings[groupId] = group.Teams.Select(team => team.Id).ToList();
        state.KnockoutWinners.Clear();
        await SaveAndNotifyAsync();
    }

    public async Task ResetAllAsync()
    {
        state = CreateDefaultState();
        EnsureBestThirdSelectionExists();
        await SaveAndNotifyAsync();
    }

    public bool AreGroupsComplete() =>
        TournamentData.Groups.All(group =>
            state.GroupRankings.TryGetValue(group.Id, out var ranking) &&
            ranking.Count == group.Teams.Count &&
            ranking.Distinct().Count() == group.Teams.Count);

    public bool AreBestThirdSelectionsComplete() =>
        state.BestThirdGroupIds.Count == BracketGenerator.RequiredBestThirdCount;

    public List<KnockoutRound> GetBracket() => bracketGenerator.Generate(state);

    public IReadOnlySet<string> GetQualifiedTeamIds() => bracketGenerator.GetRoundOf32TeamIds(state);

    public IReadOnlyDictionary<string, QualificationStatus> GetQualificationStatuses() =>
        bracketGenerator.GetRoundOf32QualificationStatuses(state);

    public bool IsBestThirdGroupSelected(string groupId) => state.BestThirdGroupIds.Contains(groupId);

    public bool IsBestThirdSelectionFull => state.BestThirdGroupIds.Count >= BracketGenerator.RequiredBestThirdCount;

    public int BestThirdSelectionCount => state.BestThirdGroupIds.Count;

    public int RequiredBestThirdSelectionCount => BracketGenerator.RequiredBestThirdCount;

    public async Task ToggleBestThirdGroupAsync(string groupId)
    {
        if (state.BestThirdGroupIds.Remove(groupId))
        {
            state.KnockoutWinners.Clear();
            await SaveAndNotifyAsync();
            return;
        }

        if (state.BestThirdGroupIds.Count >= BracketGenerator.RequiredBestThirdCount)
        {
            state.BestThirdGroupIds.RemoveAt(0);
        }

        state.BestThirdGroupIds.Add(groupId);
        state.KnockoutWinners.Clear();
        await SaveAndNotifyAsync();
    }

    public Team? GetChampion() => bracketGenerator.GetChampion(state);

    public async Task SelectWinnerAsync(Matchup matchup, Team team)
    {
        if (!matchup.IsComplete)
        {
            return;
        }

        state.KnockoutWinners[matchup.Id] = team.Id;
        ClearAffectedLaterRounds(matchup);
        await SaveAndNotifyAsync();
    }

    public async Task StartAgainAsync()
    {
        await localStorage.RemoveAsync(StorageKey);
        state = CreateDefaultState();
        EnsureBestThirdSelectionExists();
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
            state.KnockoutWinners.Remove(nextMatchupId);
            nextIndex /= 2;
            nextRoundId = BracketGenerator.NextRoundId(nextRoundId);
        }
    }

    private async Task SaveAndNotifyAsync()
    {
        await localStorage.SetAsync(StorageKey, state);
        Changed?.Invoke();
    }

    private static PredictionState CreateDefaultState() =>
        new()
        {
            StateVersion = CurrentStateVersion,
            GroupRankings = TournamentData.Groups.ToDictionary(
                group => group.Id,
                group => group.Teams.Select(team => team.Id).ToList()),
            KnockoutWinners = new Dictionary<string, string>(),
            BestThirdSelectionInitialized = false
        };

    private async Task InitializeCoreAsync()
    {
        try
        {
            var loadedState = await localStorage.GetAsync<PredictionState>(StorageKey);
            state = IsSupportedStateVersion(loadedState) ? loadedState! : CreateDefaultState();
            state.StateVersion = CurrentStateVersion;

            EnsureStorageCollectionsExist();
            EnsureAllGroupsExist();
            EnsureBestThirdSelectionExists();
            EnsureKnockoutWinnersAreValid();
        }
        catch
        {
            state = CreateDefaultState();
            EnsureBestThirdSelectionExists();
        }

        IsLoaded = true;
        await localStorage.SetAsync(StorageKey, state);
        Changed?.Invoke();
    }

    private static bool IsSupportedStateVersion(PredictionState? loadedState) =>
        loadedState is not null && loadedState.StateVersion is 0 or CurrentStateVersion;

    private void EnsureStorageCollectionsExist()
    {
        state.GroupRankings ??= [];
        state.KnockoutWinners ??= [];
        state.BestThirdGroupIds ??= [];
    }

    private void EnsureAllGroupsExist()
    {
        var validGroupIds = TournamentData.Groups.Select(group => group.Id).ToHashSet();
        foreach (var groupId in state.GroupRankings.Keys.Where(groupId => !validGroupIds.Contains(groupId)).ToList())
        {
            state.GroupRankings.Remove(groupId);
        }

        foreach (var group in TournamentData.Groups)
        {
            var currentTeamIds = group.Teams.Select(team => team.Id).ToHashSet();

            if (!state.GroupRankings.TryGetValue(group.Id, out var ranking) ||
                ranking is null ||
                ranking.Count != group.Teams.Count ||
                !currentTeamIds.SetEquals(ranking))
            {
                state.GroupRankings[group.Id] = group.Teams.Select(team => team.Id).ToList();
                state.BestThirdSelectionInitialized = false;
            }
        }
    }

    private void EnsureBestThirdSelectionExists()
    {
        var validGroupIds = TournamentData.Groups.Select(group => group.Id).ToHashSet();
        state.BestThirdGroupIds = state.BestThirdGroupIds
            .Where(validGroupIds.Contains)
            .Distinct()
            .Take(BracketGenerator.RequiredBestThirdCount)
            .ToList();

        if (state.BestThirdSelectionInitialized)
        {
            return;
        }

        state.BestThirdGroupIds = bracketGenerator.CreateDefaultBestThirdGroupSelection(state).ToList();
        state.BestThirdSelectionInitialized = true;
    }

    private void EnsureKnockoutWinnersAreValid()
    {
        var validTeamIds = TournamentData.Groups.SelectMany(group => group.Teams).Select(team => team.Id).ToHashSet();
        var generatedBracket = bracketGenerator.Generate(state);
        var validWinnerIdsByMatchup = generatedBracket
            .SelectMany(round => round.Matchups)
            .ToDictionary(
                matchup => matchup.Id,
                matchup => new[] { matchup.TeamA?.Id, matchup.TeamB?.Id }
                    .OfType<string>()
                    .ToHashSet());

        foreach (var (matchupId, teamId) in state.KnockoutWinners.ToList())
        {
            if (!validTeamIds.Contains(teamId) ||
                !validWinnerIdsByMatchup.TryGetValue(matchupId, out var validWinnerIds) ||
                !validWinnerIds.Contains(teamId))
            {
                state.KnockoutWinners.Remove(matchupId);
            }
        }
    }
}
