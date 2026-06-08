using WorldCupPredict.Models;

namespace WorldCupPredict.Services;

public sealed class PredictionStateService(LocalStorageService localStorage, BracketGenerator bracketGenerator)
{
    private const string StorageKey = "world-cup-predictor-state";
    private const int CurrentStateVersion = 1;
    private const string ShareStatePrefix = "1.";
    private const string CompactAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly IReadOnlyList<string> ShareMatchupIds =
    [
        "r32-73", "r32-74", "r32-75", "r32-76", "r32-77", "r32-78", "r32-79", "r32-80",
        "r32-81", "r32-82", "r32-83", "r32-84", "r32-85", "r32-86", "r32-87", "r32-88",
        "r16-89", "r16-90", "r16-91", "r16-92", "r16-93", "r16-94", "r16-95", "r16-96",
        "qf-97", "qf-98", "qf-99", "qf-100", "sf-101", "sf-102", "final-104"
    ];
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

    public string CreateShareUrl(string baseUri)
    {
        var appBaseUri = baseUri.TrimEnd('/');
        var encodedState = Uri.EscapeDataString(EncodeShareState());
        return $"{appBaseUri}/knockout?p={encodedState}";
    }

    public async Task<bool> TryApplyShareStateAsync(string encodedState)
    {
        if (!TryDecodeShareState(encodedState, out var sharedState))
        {
            return false;
        }

        state = sharedState;
        EnsureStorageCollectionsExist();
        EnsureAllGroupsExist();
        EnsureBestThirdSelectionExists();
        EnsureKnockoutWinnersAreValid();
        await SaveAndNotifyAsync();
        return true;
    }

    private void ClearAffectedLaterRounds(Matchup matchup)
    {
        foreach (var matchupId in BracketGenerator.GetAffectedLaterMatchupIds(matchup))
        {
            state.KnockoutWinners.Remove(matchupId);
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

    private string EncodeShareState()
    {
        var groupRankings = string.Concat(TournamentData.Groups.Select(EncodeGroupRanking));
        var bestThirdGroups = ToBase36((ulong)EncodeBestThirdGroupMask());
        var knockoutWinners = ToBase36(EncodeKnockoutWinners());
        return $"{ShareStatePrefix}{groupRankings}.{bestThirdGroups}.{knockoutWinners}";
    }

    private bool TryDecodeShareState(string encodedState, out PredictionState sharedState)
    {
        sharedState = CreateDefaultState();

        if (string.IsNullOrWhiteSpace(encodedState) ||
            !encodedState.StartsWith(ShareStatePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = encodedState[ShareStatePrefix.Length..].Split('.');
        if (parts.Length != 3 ||
            parts[0].Length != TournamentData.Groups.Count ||
            !TryFromBase36(parts[1], out var bestThirdMask) ||
            !TryFromBase36(parts[2], out var knockoutWinnerSelections))
        {
            return false;
        }

        var decodedState = CreateDefaultState();

        for (var groupIndex = 0; groupIndex < TournamentData.Groups.Count; groupIndex++)
        {
            var group = TournamentData.Groups[groupIndex];
            var rank = CompactAlphabet.IndexOf(parts[0][groupIndex]);
            if (rank is < 0 or >= 24)
            {
                return false;
            }

            decodedState.GroupRankings[group.Id] = DecodeGroupRanking(group, rank);
        }

        decodedState.BestThirdGroupIds = DecodeBestThirdGroupMask(bestThirdMask);
        if (decodedState.BestThirdGroupIds.Count != BracketGenerator.RequiredBestThirdCount)
        {
            return false;
        }

        decodedState.BestThirdSelectionInitialized = true;
        ApplyDecodedKnockoutWinners(decodedState, knockoutWinnerSelections);
        sharedState = decodedState;
        return true;
    }

    private char EncodeGroupRanking(Group group)
    {
        if (!state.GroupRankings.TryGetValue(group.Id, out var ranking) ||
            ranking.Count != group.Teams.Count)
        {
            return '0';
        }

        var teamIndexes = ranking
            .Select(teamId => group.Teams.FindIndex(team => team.Id == teamId))
            .ToList();

        return teamIndexes.Count == group.Teams.Count && teamIndexes.All(index => index >= 0)
            ? CompactAlphabet[RankPermutation(teamIndexes)]
            : '0';
    }

    private static List<string> DecodeGroupRanking(Group group, int rank) =>
        UnrankPermutation(group.Teams.Count, rank)
            .Select(index => group.Teams[index].Id)
            .ToList();

    private int EncodeBestThirdGroupMask()
    {
        var mask = 0;
        for (var index = 0; index < TournamentData.Groups.Count; index++)
        {
            if (state.BestThirdGroupIds.Contains(TournamentData.Groups[index].Id))
            {
                mask |= 1 << index;
            }
        }

        return mask;
    }

    private static List<string> DecodeBestThirdGroupMask(ulong mask) =>
        TournamentData.Groups
            .Where((_, index) => (mask & (1UL << index)) != 0)
            .Select(group => group.Id)
            .Take(BracketGenerator.RequiredBestThirdCount)
            .ToList();

    private ulong EncodeKnockoutWinners()
    {
        var generatedBracket = bracketGenerator.Generate(state);
        var matchupsById = generatedBracket
            .SelectMany(round => round.Matchups)
            .ToDictionary(matchup => matchup.Id);

        ulong encodedWinners = 0;
        ulong placeValue = 1;

        foreach (var matchupId in ShareMatchupIds)
        {
            var selection = 2UL;
            if (matchupsById.TryGetValue(matchupId, out var matchup) &&
                state.KnockoutWinners.TryGetValue(matchupId, out var winnerId))
            {
                selection = winnerId == matchup.TeamA?.Id ? 0UL : winnerId == matchup.TeamB?.Id ? 1UL : 2UL;
            }

            encodedWinners += selection * placeValue;
            placeValue *= 3;
        }

        return encodedWinners;
    }

    private void ApplyDecodedKnockoutWinners(PredictionState decodedState, ulong encodedWinners)
    {
        foreach (var matchupId in ShareMatchupIds)
        {
            var selection = encodedWinners % 3;
            encodedWinners /= 3;

            if (selection > 1)
            {
                continue;
            }

            var generatedBracket = bracketGenerator.Generate(decodedState);
            var matchup = generatedBracket
                .SelectMany(round => round.Matchups)
                .FirstOrDefault(matchup => matchup.Id == matchupId);
            var winner = selection == 0 ? matchup?.TeamA : matchup?.TeamB;

            if (winner is not null)
            {
                decodedState.KnockoutWinners[matchupId] = winner.Id;
            }
        }
    }

    private static int RankPermutation(IReadOnlyList<int> permutation)
    {
        var rank = 0;
        var available = Enumerable.Range(0, permutation.Count).ToList();

        for (var index = 0; index < permutation.Count; index++)
        {
            var selectedIndex = available.IndexOf(permutation[index]);
            rank += selectedIndex * Factorial(permutation.Count - index - 1);
            available.RemoveAt(selectedIndex);
        }

        return rank;
    }

    private static List<int> UnrankPermutation(int itemCount, int rank)
    {
        var available = Enumerable.Range(0, itemCount).ToList();
        var permutation = new List<int>();

        for (var index = itemCount - 1; index >= 0; index--)
        {
            var factor = Factorial(index);
            var selectedIndex = rank / factor;
            rank %= factor;
            permutation.Add(available[selectedIndex]);
            available.RemoveAt(selectedIndex);
        }

        return permutation;
    }

    private static int Factorial(int value)
    {
        var result = 1;
        for (var factor = 2; factor <= value; factor++)
        {
            result *= factor;
        }

        return result;
    }

    private static string ToBase36(ulong value)
    {
        if (value == 0)
        {
            return "0";
        }

        var result = new Stack<char>();
        while (value > 0)
        {
            result.Push(CompactAlphabet[(int)(value % 36)]);
            value /= 36;
        }

        return new string(result.ToArray());
    }

    private static bool TryFromBase36(string value, out ulong result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var character in value.ToUpperInvariant())
        {
            var digit = CompactAlphabet.IndexOf(character);
            if (digit is < 0 or >= 36)
            {
                return false;
            }

            result = result * 36 + (ulong)digit;
        }

        return true;
    }

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
