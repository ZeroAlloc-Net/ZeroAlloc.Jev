using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.Docs.Tests;

public sealed class QuestionSetsAtRunTimeTests
{
    // Priority: 0 x 0.1 + 1 x 0.2 + 2 x 0.7 = 1.6.
    private const string RouteResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "urgent": { "type": "noul", "noul": 0.88 },
            "team": { "type": "choice", "choice": "payments", "probabilities": { "payments": 0.75, "platform": 0.25 }, "confidence": 0.7 },
            "priority": { "type": "score", "score": 1.6, "legend": { "0": "Can wait", "1": "This week", "2": "Today" }, "probabilities": { "0": 0.1, "1": 0.2, "2": 0.7 }, "confidence": 0.8 }
          },
          "usage": { "input_tokens": 80, "output_tokens": 9 }
        }
        """;

    private static readonly (string Key, string Summary)[] Teams = [("payments", "Billing and refunds"), ("platform", "Outages and APIs")];

    private enum Route
    {
        SendToBilling,
        Billing = SendToBilling,
        NeedsHuman,
    }

    [Fact]
    public async Task RouteAsync_BuildsOnce_EvaluatesAndReadsThroughHandles()
    {
        var router = new TenantRouter(Teams);
        var (http, client, requests) = CannedDecision.Client(RouteResponse);
        using (http)
        using (client)
        {
            var routed = await router.RouteAsync(client, "Our checkout is down.", CancellationToken.None);

            Assert.Equal((true, "payments", Priority.High), routed);
            Assert.Equal((true, "payments", Priority.High), await router.RouteAsync(client, "Again.", CancellationToken.None));
        }

        Assert.Equal(2, requests.Count);
        using var request = JsonDocument.Parse(requests[0]);
        Assert.Equal("Our checkout is down.", request.RootElement.GetProperty("state").GetString());
        var questions = request.RootElement.GetProperty("questions");
        Assert.Equal("The sender needs an answer today", questions.GetProperty("urgent").GetProperty("criteria").GetProperty("true").GetString());
        Assert.Equal("Billing and refunds", questions.GetProperty("team").GetProperty("criteria").GetProperty("payments").GetString());
        Assert.Equal("This week", questions.GetProperty("priority").GetProperty("criteria")[1].GetString());
    }

    [Fact]
    public async Task RouteAsync_ReportsAFailureAsNull()
    {
        var router = new TenantRouter(Teams);
        var (http, client, _) = CannedDecision.Client("this is not json");
        using (http)
        using (client)
        {
            Assert.Null(await router.RouteAsync(client, "Anything.", CancellationToken.None));
        }
    }

    [Fact]
    public void TenantRouter_ReportsEveryBrokenRuleOfATenantsTeams()
    {
        var empty = Assert.Throws<InvalidOperationException>(() => new TenantRouter([]));
        Assert.Contains("MIN001", empty.Message, StringComparison.Ordinal);

        var duplicate = Assert.Throws<InvalidOperationException>(() => new TenantRouter([("a", "One"), ("a", "Two"), ("", "None")]));
        Assert.Contains("MIN106", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Criteria_AreSentAsTextOrCriterionObjectsOrJson_AndReadWithTheEnum()
    {
        var (set, team) = CriteriaExample.Create();

        using var actual = await CannedDecision.QuestionsSentAsync(set, "{}");
        using var expected = JsonDocument.Parse("""
            {
              "team": {
                "type": "choice",
                "instructions": "Which team should handle this?",
                "criteria": {
                  "billing": {
                    "description": "Payments, invoices and refunds",
                    "examples": ["I was charged twice"],
                    "not_for": ["How much is the Pro plan?"]
                  },
                  "technical": { "scope": "bugs", "also": ["outages", "integrations"] },
                  "sales": "Pricing and upgrades"
                }
              }
            }
            """);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));

        var (http, client, _) = CannedDecision.Client("""
            { "model": "m", "answers": { "team": { "type": "choice", "choice": "technical", "probabilities": { "billing": 0.1, "technical": 0.8, "sales": 0.1 }, "confidence": 0.9 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(set, "Help", CancellationToken.None);

            Assert.True(result.IsSuccess);
            var answer = result.Value.Get(team);
            Assert.Equal(ServiceTeam.Technical, answer.Value);
            Assert.Equal(0.8, answer.Probabilities[ServiceTeam.Technical]);
        }
    }

    [Fact]
    public void Build_ListsEveryBrokenRule_UnderTheInvalidQuestionsKind()
    {
        Assert.Equal(["MIN001 team", "MIN106 team"], RuleChecks.BrokenRules());

        var built = QuestionSet.CreateBuilder().Noul("a", "A?", out NoulHandle _).Noul("a", "B?", out NoulHandle _).Build();
        Assert.True(built.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidQuestions, built.Error.Kind);
        Assert.Collection(built.Error.Failures, failure => Assert.Equal(("MIN106", "a"), (failure.Rule, failure.QuestionKey)));
    }

    [Fact]
    public void Build_AWarningStillBuildsTheSet()
    {
        Assert.Equal(["MIN003 urgent", "MIN005 mood"], RuleChecks.Advice());

        var clean = QuestionSet.CreateBuilder().Noul("a", "A?", out NoulHandle _).Build();
        Assert.True(clean.IsSuccess);
        Assert.Empty(clean.Value.Warnings);
    }

    [Fact]
    public void Build_ChecksTheRulesInTheTable()
    {
        // MIN104: a member of an enum Score that is not given a level, and MIN106: one given twice.
        var missing = QuestionSet.CreateBuilder()
            .Score("p", "How soon?", out ScoreHandle<Priority> _, levels => levels.Level(Priority.Low, "x").Level(Priority.Medium, "y"))
            .Build();
        Assert.Equal(["MIN104"], Rules(missing.Error));

        var twice = QuestionSet.CreateBuilder()
            .Score("p", "How soon?", out ScoreHandle<Priority> _, levels => levels
                .Level(Priority.Low, "x").Level(Priority.Low, "x").Level(Priority.Medium, "y").Level(Priority.High, "z"))
            .Build();
        Assert.Equal(["MIN106"], Rules(twice.Error));

        // MIN001 and MIN002: a keyed Score with no levels. An enum Choice with no options cannot happen, as every member is one.
        var noLevels = QuestionSet.CreateBuilder().Score("s", "How?", out KeyedScoreHandle _, levels => { }).Build();
        Assert.Equal(["MIN002"], Rules(noLevels.Error));

        // An empty question key, an empty option key and a repeated option key are MIN106.
        var keys = QuestionSet.CreateBuilder()
            .Noul(string.Empty, "A?", out NoulHandle _)
            .Choice("c", "Which?", out KeyedChoiceHandle _, o => o.Option("x").Option("x").Option(string.Empty))
            .Build();
        Assert.Equal(["MIN106", "MIN106", "MIN106"], Rules(keys.Error));

        // MIN108: JSON nested deeper than 60 levels, and 60 levels are fine.
        var deep = DecisionContent.FromUtf8Json(System.Text.Encoding.UTF8.GetBytes(new string('[', 61) + new string(']', 61)));
        var deepest = DecisionContent.FromUtf8Json(System.Text.Encoding.UTF8.GetBytes(new string('[', 60) + new string(']', 60)));
        Assert.Equal(["MIN108"], Rules(QuestionSet.CreateBuilder().Noul("n", deep, out NoulHandle _).Build().Error));
        Assert.True(QuestionSet.CreateBuilder().Noul("n", deepest, out NoulHandle _).Build().IsSuccess);

        // MIN003: a blank description or example, and JSON that is exactly {} or [].
        var blank = QuestionSet.CreateBuilder()
            .Choice("c", "Which?", out KeyedChoiceHandle _, o => o.Option("x", Criterion.Text("ok").WithExamples(" ")))
            .Noul("n", DecisionContent.FromUtf8Json("{}"u8), out NoulHandle _)
            .Build();
        Assert.True(blank.IsSuccess);
        Assert.Equal(["MIN003", "MIN003"], WarningRules(blank.Value));

        // MIN005: a Score of 11 levels, and a Choice of 256 options.
        var many = QuestionSet.CreateBuilder()
            .Score("s", "How?", out KeyedScoreHandle _, levels =>
            {
                for (var i = 0; i < 11; i++)
                {
                    levels.Level("level");
                }
            })
            .Choice("c", "Which?", out KeyedChoiceHandle _, options =>
            {
                for (var i = 0; i < 256; i++)
                {
                    options.Option("option" + i);
                }
            })
            .Build();
        Assert.True(many.IsSuccess);
        Assert.Equal(["MIN005", "MIN005"], WarningRules(many.Value));
    }

    [Fact]
    public async Task Handles_ReadOnlyTheAnswersOfASetFromTheirOwnBuilder()
    {
        var builder = QuestionSet.CreateBuilder().Noul("first", "First?", out var first);
        var early = builder.Build().Value;
        builder.Noul("second", "Second?", out var second);
        var late = builder.Build().Value;
        var other = QuestionSet.CreateBuilder().Noul("first", "First?", out var foreign).Build().Value;

        var (http, client, _) = CannedDecision.Client("""
            { "model": "m", "answers": { "first": { "type": "noul", "noul": 0.2 }, "second": { "type": "noul", "noul": 0.7 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (client)
        {
            var earlyAnswers = (await client.EvaluateAsync(early, "x", CancellationToken.None)).Value;
            var lateAnswers = (await client.EvaluateAsync(late, "x", CancellationToken.None)).Value;
            var otherAnswers = (await client.EvaluateAsync(other, "x", CancellationToken.None)).Value;

            // A handle works with every set its builder built, if the question existed when the set was built.
            Assert.Equal(0.2, earlyAnswers.Get(first).Probability);
            Assert.Equal(0.2, lateAnswers.Get(first).Probability);
            Assert.Equal(0.7, lateAnswers.Get(second).Probability);

            // It does not work with a set built before its question was added, another builder's set, or when it is default.
            Assert.Throws<ArgumentException>(() => earlyAnswers.Get(second));
            Assert.Throws<ArgumentException>(() => otherAnswers.Get(first));
            Assert.Throws<ArgumentException>(() => earlyAnswers.Get(foreign));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(NoulHandle)));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(KeyedChoiceHandle)));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(KeyedScoreHandle)));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(ChoiceHandle<ServiceTeam>)));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(ScoreHandle<Priority>)));
        }
    }

    [Fact]
    public void Definition_ListsTheSameQuestionsForAGeneratedAndABuiltSet()
    {
        #region QuestionSetsAtRunTime_Definition
        // TicketCheck is the generated set from getting started; the builder makes the same two questions.
        var built = QuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out NoulHandle _)
            .Choice("team", "Which team should handle this?", out ChoiceHandle<SupportTeam> _)
            .Build();

        var shapes = new List<string>();
        foreach (var definition in new[] { TicketCheck.Definition, built.Value.Definition })
        {
            var lines = new List<string>();
            foreach (var question in definition.Questions)
            {
                // Options holds a Choice's options or a Score's levels, and is empty for a Noul.
                var keys = new List<string>();
                foreach (var option in question.Options)
                {
                    keys.Add(option.Key);
                }

                lines.Add($"{question.Key} {question.Kind} [{string.Join(", ", keys)}]");
            }

            shapes.Add(string.Join("; ", lines));
        }

        // Both print: is_urgent Noul []; team Choice [billing, technical, sales]
        Assert.Equal(shapes[0], shapes[1]);
        #endregion
        Assert.Equal("is_urgent Noul []; team Choice [billing, technical, sales]", shapes[0]);
    }

    [Fact]
    public async Task AMissingAnswer_FailsTheCallAsInvalidResponse()
    {
        var set = QuestionSet.CreateBuilder().Noul("a", "A?", out NoulHandle _).Noul("b", "B?", out NoulHandle _).Build().Value;
        var (http, client, _) = CannedDecision.Client("""
            { "model": "m", "answers": { "a": { "type": "noul", "noul": 0.2 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(set, "x", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        }
    }

    [Fact]
    public void AConfigurator_WorksOnlyInsideItsCallback()
    {
        KeyedChoiceOptionsBuilder? stored = null;
        NoulCriteriaBuilder? storedNoul = null;
        QuestionSet.CreateBuilder()
            .Choice("c", "Which?", out KeyedChoiceHandle _, options =>
            {
                stored = options;
                options.Option("x");
            })
            .Noul("n", "A?", out NoulHandle _, criteria => storedNoul = criteria);

        Assert.Throws<InvalidOperationException>(() => stored!.Option("y"));
        Assert.Throws<InvalidOperationException>(() => storedNoul!.WhenTrue("yes"));
    }

    [Fact]
    public void Arguments_AreCheckedWhenAQuestionIsAdded()
    {
        var builder = QuestionSet.CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.Noul(null!, "A?", out NoulHandle _));
        Assert.Throws<ArgumentException>(() => builder.Noul("a", default, out NoulHandle _));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Choice(
            "c", "Which?", out ChoiceHandle<ServiceTeam> _, options => options.Describe((ServiceTeam)99, "x")));
    }

    [Fact]
    public async Task EnumQuestions_KeyTheMembersInSnakeCase_AndSkipAliases()
    {
        var set = QuestionSet.CreateBuilder().Choice("route", "Where to?", out ChoiceHandle<Route> _).Build().Value;

        using var questions = await CannedDecision.QuestionsSentAsync(set, "{}");
        var criteria = questions.RootElement.GetProperty("route").GetProperty("criteria");
        Assert.Equal(["send_to_billing", "needs_human"], criteria.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task EnumAttributes_AreNotReadByTheBuilder()
    {
        // Department carries [Criteria] on its members; the builder sends no description for them.
        var set = QuestionSet.CreateBuilder().Choice("d", "Which?", out ChoiceHandle<Department> department).Build().Value;

        using var questions = await CannedDecision.QuestionsSentAsync(set, "{}");
        var criteria = questions.RootElement.GetProperty("d").GetProperty("criteria");
        Assert.Equal(JsonValueKind.Null, criteria.GetProperty("billing").ValueKind);

        var (http, client, _) = CannedDecision.Client("""
            { "model": "m", "answers": { "d": { "type": "choice", "choice": "sales", "probabilities": { "billing": 0.1, "technical": 0.2, "sales": 0.7 }, "confidence": 0.5 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(set, "x", CancellationToken.None);

            Assert.Equal(Department.Sales, result.Value.Get(department).Value);
        }
    }

    [Fact]
    public async Task AnswersToUnknownKeys_AreIgnored()
    {
        var set = QuestionSet.CreateBuilder().Noul("a", "A?", out var a).Build().Value;
        var (http, client, _) = CannedDecision.Client("""
            { "model": "m", "answers": { "extra": { "type": "noul", "noul": 0.9 }, "a": { "type": "noul", "noul": 0.2 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (client)
        {
            var result = await client.EvaluateAsync(set, "x", CancellationToken.None);

            Assert.Equal(0.2, result.Value.Get(a).Probability);
        }
    }

    [Fact]
    public async Task AFakeClient_EvaluatesABuiltSet_ThroughTheExtension()
    {
        var set = QuestionSet.CreateBuilder().Noul("a", "A?", out var a).Build().Value;

        var result = await ((IDecisionClient)new FakeClient()).EvaluateAsync(set, "x", CancellationToken.None);

        Assert.Equal(0.4, result.Value.Get(a).Probability);
    }

    [Fact]
    public void ExamplesOnAJsonCriterion_Throw()
    {
        var json = Criterion.Json(DecisionContent.FromUtf8Json("{}"u8));

        Assert.Throws<InvalidOperationException>(() => json.WithExamples("x"));
        Assert.Throws<InvalidOperationException>(() => json.WithNotFor("x"));
        Assert.Throws<ArgumentException>(() => Criterion.Json(DecisionContent.FromString("text")));
    }

    private sealed class FakeClient : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Result<DecisionResponse, DecisionError>.Success(
                new DecisionResponse(request.Definition, [QuestionAnswer.Noul(0.4)], model: "fake")));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private enum Empty
    {
    }

    [Fact]
    public void AnEnumWithNoMembers_FailsAsMin001AndMin002()
    {
        var choice = QuestionSet.CreateBuilder().Choice("c", "Which?", out ChoiceHandle<Empty> _).Build();
        var score = QuestionSet.CreateBuilder().Score("s", "How?", out ScoreHandle<Empty> _, levels => { }).Build();
        var keyedChoice = QuestionSet.CreateBuilder().Choice("c", "Which?", out KeyedChoiceHandle _, options => { }).Build();
        var enumScore = QuestionSet.CreateBuilder().Score("s", "How?", out ScoreHandle<Priority> _, levels => { }).Build();

        Assert.Equal(["MIN001"], Rules(choice.Error));
        Assert.Equal(["MIN002"], Rules(score.Error));
        Assert.Equal(["MIN001"], Rules(keyedChoice.Error));
        Assert.Equal(["MIN104", "MIN104", "MIN104"], Rules(enumScore.Error));
    }

    [Fact]
    public void ANoulAndAnEnumChoice_BuildWithoutAConfigurator()
    {
        Assert.True(QuestionSet.CreateBuilder().Noul("n", "A?", out NoulHandle _).Build().IsSuccess);
        Assert.True(QuestionSet.CreateBuilder().Choice("c", "Which?", out ChoiceHandle<ServiceTeam> _).Build().IsSuccess);
    }

    private static string[] Rules(DecisionError error) => [.. error.Failures.Select(f => f.Rule)];

    private static string[] WarningRules(QuestionSet set) => [.. set.Warnings.Select(f => f.Rule)];
}
