namespace Minos.Tests;

/// <summary>Answer literals for the telemetry tests.</summary>
internal static class TelemetryBodies
{
    /// <summary>A Choice answer whose <c>confidence</c> is 0.81.</summary>
    public const string ChoiceJson = """{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12},"confidence":0.81}""";

    /// <summary>A Score answer whose <c>confidence</c> is 0.92.</summary>
    public const string ScoreJson = """{"type":"score","score":1.05,"legend":{"0":"Calm","1":"Frustrated"},"probabilities":{"0":0.05,"1":0.95},"confidence":0.92}""";

    /// <summary>A Noul answer, which has no <c>confidence</c>.</summary>
    public const string NoulJson = """{"type":"noul","noul":0.95}""";
}
