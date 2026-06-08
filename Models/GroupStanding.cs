namespace WorldCupPredict.Models;

public sealed class GroupStanding
{
    public required string GroupId { get; init; }
    public required string TeamId { get; init; }
    public int Position { get; set; }
}
