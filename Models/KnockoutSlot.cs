namespace WorldCupPredict.Models;

public sealed record KnockoutSlot(string GroupId, int Position)
{
    private const string BestThirdPrefix = "Best 3rd ";

    public bool IsBestThirdSlot => GroupId.StartsWith(BestThirdPrefix, StringComparison.Ordinal);

    public IReadOnlyList<string> EligibleThirdPlaceGroups =>
        IsBestThirdSlot
            ? GroupId[BestThirdPrefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}
