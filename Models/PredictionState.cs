namespace WorldCupPredict.Models;

public sealed class PredictionState
{
    public int StateVersion { get; set; }
    public Dictionary<string, List<string>> GroupRankings { get; set; } = new();
    public Dictionary<string, string> KnockoutWinners { get; set; } = new();
    public List<string> BestThirdGroupIds { get; set; } = [];
    public bool BestThirdSelectionInitialized { get; set; }
}
