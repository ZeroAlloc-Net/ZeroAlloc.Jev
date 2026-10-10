using System.Runtime.CompilerServices;
using Minos.Protocols;

namespace Minos.Tests;

public sealed class ProtocolBoundaryTests
{
    // The /v1/systemone request envelope, question and answer field names. Only the protocol, and the files Phase 6.3
    // moves behind it, may name them.
    // Only the u8 literal form is matched, on purpose: plain strings would flag XML doc param names. A plain string or a
    // JsonPropertyName use of these names is not caught.
    private static readonly string[] WireNames =
    [
        "\"state\"u8", "\"model\"u8", "\"questions\"u8",
        "\"probabilities\"u8", "\"legend\"u8", "\"noul\"u8", "\"criteria\"u8", "\"instructions\"u8",
        "\"description\"u8", "\"examples\"u8", "\"not_for\"u8",
    ];

    private static readonly string[] Allowed =
    [
        "Protocols/",

        // Phase 6.3: response envelope.
        "Telemetry/ResponseFields.cs",
    ];

    [Fact]
    public void OnlyTheProtocolNamesTheSystemOneWireFields()
    {
        var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "src", "Minos.NET"));
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(f => !Allowed.Any(a => f.Path.StartsWith(a, StringComparison.Ordinal)))
            .Where(f => WireNames.Any(n => f.Text.Contains(n, StringComparison.Ordinal)))
            .Select(f => f.Path)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheSystemOneProtocolOwnsTheEndpointPath()
        => Assert.Equal("v1/systemone", SystemOneProtocol.Instance.EndpointPath);

    // Transport/IDecisionApi.cs keeps the literal for its Post attribute: the raw call stays, and Phase 6.3 adds the neutral one.
    [Fact]
    public void OnlyTheProtocolAndTheApiInterfaceNameTheEndpointPath()
    {
        var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "src", "Minos.NET"));
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(f => !f.Path.StartsWith("Protocols/", StringComparison.Ordinal) && !string.Equals(f.Path, "Transport/IDecisionApi.cs", StringComparison.Ordinal))
            .Where(f => f.Text.Contains("\"v1/systemone\"", StringComparison.Ordinal))
            .Select(f => f.Path)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
