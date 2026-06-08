namespace WorldCupPredict.Models;

public sealed class Group
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required List<Team> Teams { get; init; }
}
