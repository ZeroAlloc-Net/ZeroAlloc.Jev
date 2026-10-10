using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Minos.Samples;

/// <summary>Wires a sample's Minos client for its mode.</summary>
public static class SampleHost
{
    /// <summary>
    /// Registers the default <see cref="IDecisionClient"/> bound from <paramref name="clientSection"/>, then applies
    /// <paramref name="mode"/>. Replay needs no key, so it supplies a placeholder that is never sent anywhere.
    /// <paramref name="recordingsPath"/> is read in replay only, so a live run may pass <see langword="null"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Replay is asked for without a recordings path.</exception>
    public static DecisionClientServiceBuilder AddSampleDecisionClient(
        this IServiceCollection services,
        IConfiguration clientSection,
        SampleMode mode,
        string? recordingsPath,
        string sampleName,
        RecordingSession session)
    {
        var builder = services.AddDecisionClient(clientSection);
        switch (mode)
        {
            case SampleMode.Replay:
                ArgumentException.ThrowIfNullOrEmpty(recordingsPath);
                services.AddDecisionClient(options => options.ApiKey ??= "replay-no-key");
                builder.HttpClient.ConfigurePrimaryHttpMessageHandler(() => new ReplayHandler(RecordingsFile.Load(recordingsPath), sampleName));
                break;
            case SampleMode.Record:
                builder.HttpClient.AddHttpMessageHandler(() => new RecordingHandler(session));
                break;
        }

        return builder;
    }

    /// <summary>Builds a replaying provider from the sample's own <c>appsettings.json</c>, so tests send exactly the requests the sample recorded.</summary>
    public static ServiceProvider BuildReplayProvider(string sampleDirectory, string sampleName)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(sampleDirectory, "appsettings.json")).Build();
        var services = new ServiceCollection();
        services.AddSampleDecisionClient(
            configuration.GetSection("Minos"), SampleMode.Replay, Path.Combine(sampleDirectory, "recordings.json"), sampleName, new RecordingSession());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The sample's <c>recordings.json</c> in its source folder, which replay reads and record rewrites, so a record run
    /// is replayed at once with no rebuild. Live mode reads no recordings and gets <see langword="null"/>, so it runs
    /// from anywhere, not only from a clone of the repository.
    /// </summary>
    public static string? RecordingsPath(SampleMode mode, string sampleName)
        => mode == SampleMode.Live ? null : Path.Combine(SampleDirectory(sampleName), "recordings.json");

    /// <summary>Finds <c>samples/<paramref name="sampleName"/></c> from the running assembly.</summary>
    public static string SampleDirectory(string sampleName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Minos.NET.slnx")))
            {
                return Path.Combine(dir.FullName, "samples", sampleName);
            }
        }

        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory + ".");
    }
}
