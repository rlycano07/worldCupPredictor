namespace WorldCupPredict.Models;

public sealed class KnockoutRound
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required List<Matchup> Matchups { get; init; }
}
