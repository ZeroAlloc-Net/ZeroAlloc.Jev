using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minos.Samples.Reranking;
using ZeroAlloc.TestHelpers;

namespace Minos.Samples.Tests;

public sealed class RerankingSampleTests
{
    private const string Sample = "Minos.NET.Samples.Reranking";

    [Fact]
    public void Corpus_HasTwentyFiveArticlesAndFiveQueries()
    {
        Assert.Equal(25, Articles.All.Count);
        Assert.Equal(Enumerable.Range(1, 25).Select(n => "a" + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture)), Articles.All.Select(a => a.Id));
        Assert.Equal(["a04", "a09", "a08", "a21", "a16"], Queries.All.Select(q => q.BestId));
    }

    [Fact]
    public void Shortlist_ContainsTheBestArticle_ForEveryQuery()
    {
        foreach (var q in Queries.All)
        {
            Assert.Contains(q.BestId, ShortlistIds(q.Text));
        }
    }

    [Fact]
    public void Shortlist_IsNotAlreadyPerfect()
    {
        var hits = Queries.All.Count(q => string.Equals(ShortlistIds(q.Text)[0], q.BestId, StringComparison.Ordinal));

        Assert.True(hits < 5, "keyword hit@1 is " + hits);
    }

    [Fact]
    public void Shortlist_PinsTheKeywordShortlistOfEveryQuery()
    {
        string[][] expected =
        [
            ["a01", "a02", "a12", "a04", "a06", "a13", "a15", "a16"],
            ["a01", "a02", "a05", "a09", "a10", "a11", "a12", "a21"],
            ["a07", "a08", "a04", "a16", "a01", "a02", "a03", "a05"],
            ["a22", "a01", "a02", "a05", "a17", "a18", "a21", "a03"],
            ["a16", "a17", "a01", "a02", "a03", "a04", "a05", "a06"],
        ];

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], ShortlistIds(Queries.All[i].Text));
        }
    }

    [Fact]
    public void Tokens_AreLowerCasedAndSplitOnNonAlphanumerics()
    {
        var tokens = KeywordShortlist.Tokens("Bike-LOCK, Wheel2/Brake!");

        Assert.Equal(["bike", "brake", "lock", "wheel2"], Ordered(tokens));
    }

    [Fact]
    public void Tokens_DropWordsUnderThreeCharacters()
    {
        var tokens = KeywordShortlist.Tokens("an ox is big");

        Assert.Equal(["big"], Ordered(tokens));
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("for")]
    [InlineData("with")]
    [InlineData("that")]
    [InlineData("this")]
    [InlineData("you")]
    [InlineData("your")]
    [InlineData("are")]
    [InlineData("was")]
    [InlineData("were")]
    [InlineData("but")]
    [InlineData("not")]
    [InlineData("can")]
    [InlineData("has")]
    [InlineData("had")]
    [InlineData("have")]
    [InlineData("from")]
    [InlineData("they")]
    [InlineData("them")]
    [InlineData("what")]
    [InlineData("when")]
    [InlineData("how")]
    [InlineData("its")]
    [InlineData("our")]
    [InlineData("any")]
    [InlineData("all")]
    public void Tokens_DropEachStopWord(string stopWord)
    {
        Assert.Empty(KeywordShortlist.Tokens(stopWord));
        Assert.Empty(KeywordShortlist.Tokens(stopWord.ToUpperInvariant()));
    }

    [Fact]
    public void Tokens_KeepWordsThatOnlyLookLikeStopWords()
    {
        Assert.Equal(["android", "theme"], Ordered(KeywordShortlist.Tokens("theme android")));
    }

    [Fact]
    public void Tokens_AreDistinct()
    {
        Assert.Equal(["bike"], Ordered(KeywordShortlist.Tokens("bike Bike BIKE bike")));
    }

    [Fact]
    public void Score_CountsDistinctQueryTokensOnce_InTitleAndBody()
    {
        var article = new Article("x1", "Wheel", "The brake and the chain. Brake again.");

        Assert.Equal(1, KeywordShortlist.Score("wheel wheel wheel", article));
        Assert.Equal(2, KeywordShortlist.Score("brake chain", article));
        Assert.Equal(3, KeywordShortlist.Score("wheel brake chain saddle", article));
        Assert.Equal(0, KeywordShortlist.Score("saddle", article));
    }

    [Fact]
    public void Top_OrdersByScoreDescending_ThenById()
    {
        Article[] articles =
        [
            new("b", "one", "zzz"),
            new("c", "one two", "zzz"),
            new("a", "one", "zzz"),
            new("d", "one two three", "zzz"),
            new("e", "nothing", "zzz"),
        ];

        var top = KeywordShortlist.Top("one two three", articles, 5);

        Assert.Equal(["d", "c", "a", "b", "e"], top.Select(a => a.Id));
    }

    [Fact]
    public void Top_TakesTheRequestedCount_OrFewerWhenTheCorpusIsSmaller()
    {
        Article[] articles = [new("a", "one", "x"), new("b", "one", "x"), new("c", "one", "x")];

        Assert.Equal(["a", "b"], KeywordShortlist.Top("one", articles, 2).Select(a => a.Id));
        Assert.Empty(KeywordShortlist.Top("one", articles, 0));
        Assert.Equal(3, KeywordShortlist.Top("one", articles, 10).Count);
    }

    [Fact]
    public void Rank_OrdersByProbabilityDescending()
    {
        Article[] candidates = [new("a", "t", "b"), new("b", "t", "b"), new("c", "t", "b")];

        Assert.Equal(["c", "a", "b"], RerankingSample.Rank(candidates, [0.2, 0.1, 0.9]));
    }

    [Fact]
    public void Rank_BreaksTiesByKeywordOrder_NotById()
    {
        // Keyword order is z, y, x; the ids are deliberately not alphabetical.
        Article[] candidates = [new("z", "t", "b"), new("y", "t", "b"), new("x", "t", "b"), new("w", "t", "b")];

        Assert.Equal(["w", "z", "y", "x"], RerankingSample.Rank(candidates, [0.5, 0.5, 0.5, 0.9]));
        Assert.Equal(["z", "y", "x", "w"], RerankingSample.Rank(candidates, [0.5, 0.5, 0.5, 0.5]));
        Assert.Equal(["z", "x", "y", "w"], RerankingSample.Rank(candidates, [0.9, 0.1, 0.5, 0.0]));
    }

    [Fact]
    public void Rank_NeedsOneProbabilityPerCandidate()
    {
        Assert.Throws<ArgumentException>(() => RerankingSample.Rank([new("a", "t", "b")], [0.1, 0.2]));
    }

    [Fact]
    public void Report_CountsHitsAt1AndAt3_ForBothOrderings()
    {
        var report = new RerankingReport(
        [
            new RankedQuery("q1", "b", ["a", "b", "c", "d"], ["b", "a", "c", "d"]),
            new RankedQuery("q2", "c", ["a", "b", "c", "d"], ["a", "b", "d", "c"]),
            new RankedQuery("q3", "d", ["a", "b", "c", "d"], ["a", "c", "b", "d"]),
            new RankedQuery("q4", "a", ["a", "b", "c", "d"], ["d", "c", "a", "b"]),
        ]);

        Assert.Equal(1, report.KeywordHitsAt1);
        Assert.Equal(3, report.KeywordHitsAt3);
        Assert.Equal(1, report.DecisionHitsAt1);
        Assert.Equal(2, report.DecisionHitsAt3);
    }

    [Fact]
    public void Render_PrintsTotalsAndMarkers()
    {
        var report = new RerankingReport(
        [
            new RankedQuery("first", "b", ["a", "b", "c", "d"], ["b", "a", "c", "d"]),
            new RankedQuery("second", "d", ["a", "b", "c", "d"], ["a", "b", "c", "d"]),
        ]);

        Assert.Equal(
            "first\n  keyword  a b c  hit@3\n  model    b a c  hit@1\n\n"
            + "second\n  keyword  a b c  miss\n  model    a b c  miss\n\n"
            + "hit@1 keyword 0/2 -> model 1/2\nhit@3 keyword 1/2 -> model 1/2\n",
            report.Render());
    }

    [Fact]
    public async Task Reranking_DoesNotLoseHits()
    {
        var report = await Run();

        Assert.True(report.DecisionHitsAt1 >= report.KeywordHitsAt1);
        Assert.True(report.DecisionHitsAt3 >= report.KeywordHitsAt3);
    }

    [Fact]
    public async Task Reranking_ImprovesHitAt1()
    {
        var report = await Run();

        Assert.True(report.DecisionHitsAt1 > report.KeywordHitsAt1);
    }

    [Fact]
    public async Task Reranking_PinsTheRecordedHitCounts()
    {
        var report = await Run();

        Assert.Equal(1, report.KeywordHitsAt1);
        Assert.Equal(2, report.KeywordHitsAt3);
        Assert.Equal(5, report.DecisionHitsAt1);
        Assert.Equal(5, report.DecisionHitsAt3);
        Assert.EndsWith("hit@1 keyword 1/5 -> model 5/5\nhit@3 keyword 2/5 -> model 5/5\n", report.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reranking_PinsEachQuerysTopThree()
    {
        var report = await Run();

        string[][] keyword = [["a01", "a02", "a12"], ["a01", "a02", "a05"], ["a07", "a08", "a04"], ["a22", "a01", "a02"], ["a16", "a17", "a01"]];
        string[][] decision = [["a04", "a02", "a12"], ["a09", "a10", "a05"], ["a08", "a07", "a04"], ["a21", "a18", "a05"], ["a16", "a02", "a04"]];
        Assert.Equal(5, report.Queries.Count);
        for (var i = 0; i < decision.Length; i++)
        {
            Assert.Equal(Queries.All[i].Text, report.Queries[i].Query);
            Assert.Equal(keyword[i], FirstThree(report.Queries[i].KeywordOrder));
            Assert.Equal(decision[i], FirstThree(report.Queries[i].DecisionOrder));
        }
    }

    [Fact]
    public async Task Reranking_OnlyReordersTheShortlist()
    {
        var report = await Run();

        foreach (var q in report.Queries)
        {
            Assert.Equal(8, q.DecisionOrder.Count);
            Assert.Equal(Sorted(q.KeywordOrder), Sorted(q.DecisionOrder));
        }
    }

    [Fact]
    public async Task EachQuery_IsOneRequest()
    {
        var directory = SampleHost.SampleDirectory(Sample);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(directory, "appsettings.json")).Build();
        var counter = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSampleDecisionClient(
            configuration.GetSection("Minos"), SampleMode.Replay, Path.Combine(directory, "recordings.json"), Sample, new RecordingSession())
            .HttpClient.AddHttpMessageHandler(() => counter);
        using var provider = services.BuildServiceProvider();

        var report = await RerankingSample.RunAsync(provider.GetRequiredService<IDecisionClient>(), CancellationToken.None);

        Assert.Equal(5, report.Queries.Count);
        Assert.Equal(5, counter.Requests);
    }

    [Fact]
    public void Report_CountsFollowTheQueries_AfterAWith()
    {
        var report = new RerankingReport([new RankedQuery("q", "b", ["a", "b"], ["a", "b"])]);
        var changed = report with { Queries = [new RankedQuery("q", "b", ["a", "b"], ["b", "a"])] };

        Assert.Equal(0, report.DecisionHitsAt1);
        Assert.Equal(1, changed.DecisionHitsAt1);
    }

    [Fact]
    public async Task Report_MatchesTheSnapshot() => TextSnapshot.VerifyText((await Run()).Render());

    private static async Task<RerankingReport> Run()
    {
        using var provider = SampleHost.BuildReplayProvider(SampleHost.SampleDirectory(Sample), Sample);
        return await RerankingSample.RunAsync(provider.GetRequiredService<IDecisionClient>(), CancellationToken.None);
    }

    private static string[] FirstThree(IReadOnlyList<string> order) => [.. order.Take(3)];

    private static string[] Sorted(IReadOnlyList<string> ids) => [.. ids.Order(StringComparer.Ordinal)];

    private static string[] ShortlistIds(string query) =>
        [.. KeywordShortlist.Top(query, Articles.All, 8).Select(a => a.Id)];

    private static string[] Ordered(IReadOnlySet<string> tokens) => [.. tokens.Order(StringComparer.Ordinal)];

    private sealed class CountingHandler : DelegatingHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
