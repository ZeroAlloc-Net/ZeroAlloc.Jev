using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Minos.Samples.Tests;

public sealed class SampleHostTests : IDisposable
{
    private const string Sample = "Minos.NET.Samples.Example";
    private const string Body = """{"model":"jev-1.13.0","answers":{"urgent":{"type":"noul","noul":0.9}},"usage":{"input_tokens":1,"output_tokens":1}}""";

    private readonly string _directory = Directory.CreateTempSubdirectory("minos-sample-host-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task ReplayProvider_AnswersARecordedRequest_WithoutAKey_AndFailsAnUnrecordedOne()
    {
        System.IO.File.WriteAllText(
            Path.Combine(_directory, "appsettings.json"),
            """{ "Minos": { "Provider": "OpenRouter", "MaxRetries": 0 } }""");

        // The hash of the exact body the client sends, taken from the request itself and never written by hand.
        var hash = await CaptureRequestHashAsync();
        var recordings = Path.Combine(_directory, "recordings.json");
        new RecordingsFile
        {
            Provider = "OpenRouter",
            Model = "jev-1.13.0",
            Recorded = "2026-10-02",
            Entries = [new RecordedResponse(hash, JsonElement.Parse(Body))],
        }.Save(recordings);

        // Whatever the process environment holds, the placeholder key passes startup validation and the replay handler
        // is the primary handler, so no request can leave the process.
        await using var provider = SampleHost.BuildReplayProvider(_directory, Sample);
        var client = provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!;

        var answered = await client.EvaluateAsync(Request("Is this urgent?"));
        Assert.True(answered.IsSuccess);
        Assert.Equal(0.9, Assert.IsType<NoulAnswer>(answered.Value.Answers["urgent"]).Noul);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.EvaluateAsync(Request("Something never recorded.")));
        Assert.Contains("dotnet run --project samples/" + Sample + " -- --record", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingsPath_IsNull_ForLive_SoLiveNeedsNoCheckout()
        => Assert.Null(SampleHost.RecordingsPath(SampleMode.Live, "Minos.NET.Samples.Guardrails"));

    [Theory]
    [InlineData(SampleMode.Replay)]
    [InlineData(SampleMode.Record)]
    public void RecordingsPath_IsTheSourceFolderFile_ForReplayAndRecord(SampleMode mode)
        => Assert.Equal(
            Path.Combine(Repository.Root, "samples", "Minos.NET.Samples.Guardrails", "recordings.json"),
            SampleHost.RecordingsPath(mode, "Minos.NET.Samples.Guardrails"));

    [Fact]
    public void Replay_NeedsARecordingsPath()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.ThrowsAny<ArgumentException>(
            () => new ServiceCollection().AddSampleDecisionClient(configuration.GetSection("Minos"), SampleMode.Replay, null, Sample, new RecordingSession()));
    }

    [Fact]
    public void Live_RegistersWithoutARecordingsPath()
    {
        var configuration = new ConfigurationBuilder().Build();

        var builder = new ServiceCollection().AddSampleDecisionClient(configuration.GetSection("Minos"), SampleMode.Live, null, Sample, new RecordingSession());

        Assert.NotNull(builder);
    }

    private async Task<string> CaptureRequestHashAsync()
    {
        // The replay wiring still loads the recordings; an empty file is enough, as the capture handler answers instead.
        new RecordingsFile { Provider = "OpenRouter", Model = "jev-1.13.0", Recorded = "2026-10-02", Entries = [] }
            .Save(Path.Combine(_directory, "recordings.json"));
        var capture = new CaptureHandler();
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(_directory, "appsettings.json")).Build();
        var services = new ServiceCollection();
        services
            .AddSampleDecisionClient(
                configuration.GetSection("Minos"),
                SampleMode.Replay,
                Path.Combine(_directory, "recordings.json"),
                Sample,
                new RecordingSession())
            .ConfigurePrimaryHttpMessageHandler(() => capture);
        await using var provider = services.BuildServiceProvider();

        _ = await provider.GetRequiredService<IDecisionClient>().GetService<DecisionClient>()!.EvaluateAsync(Request("Is this urgent?"));

        return capture.Hash ?? throw new InvalidOperationException("The client sent nothing.");
    }

    private static SystemOneRequest Request(string question)
        => new()
        {
            State = "A fixed state.",
            Questions = new Dictionary<string, Question> { ["urgent"] = new NoulQuestion { Instructions = question } },
        };

    // Stands in for the primary handler: it records the hash of the body and answers with a canned response.
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Hash { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hash = RequestHash.Of(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
