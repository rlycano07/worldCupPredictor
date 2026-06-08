namespace WorldCupPredict.Models;

public sealed class Matchup
{
    public required string Id { get; init; }
    public required string RoundId { get; init; }
    public int Index { get; init; }
    public Team? TeamA { get; set; }
    public Team? TeamB { get; set; }
    public string? WinnerTeamId { get; set; }

    public bool IsComplete => TeamA is not null && TeamB is not null;
}
