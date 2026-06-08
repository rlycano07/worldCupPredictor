namespace WorldCupPredict.Models;

public sealed class PredictionState
{
    public Dictionary<string, List<string>> GroupRankings { get; set; } = new();
    public Dictionary<string, string> KnockoutWinners { get; set; } = new();
}
