using System.Text;
using System.Text.Json;
using Minos.Protocols;
using ZeroAlloc.Results;
using ZeroAlloc.TestHelpers;

namespace Minos.Tests;

/// <summary>A built set's answers: the same typed values as a generated set's, read by handle without allocating.</summary>
public sealed class AnswersTests
{
    private const string KeyedResponse = """
        {"answers":{
          "product":{"type":"choice","choice":"team-plan","probabilities":{"pro-plan":0.25,"team-plan":0.7},"confidence":0.66},
          "extra":{"type":"noul","noul":1},
          "effort":{"type":"score","score":0.8,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.4,"1":0.4,"2":0.2},"confidence":0.5}
        }}
        """;

    [Fact]
    public void Noul_EqualsTheGeneratedSetsAnswer()
    {
        var set = Built(QuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out var urgent));

        var answer = Parse(set, Fixture.Text("response-noul.json")).Get(urgent);

        Assert.Equal(ResponseAnswers.Parse<UrgencyCheck>(Fixture.Text("response-noul.json")).IsUrgent.Probability, answer.Probability);
    }

    [Fact]
    public void EnumChoice_EqualsTheGeneratedSetsAnswer()
    {
        var set = Built(QuestionSet.CreateBuilder().Choice<Department>("department", "Which team?", out var department));

        var answer = Parse(set, Fixture.Text("response-choice.json")).Get(department);

        Assert.Equal(ResponseAnswers.Parse<DepartmentRouting>(Fixture.Text("response-choice.json")).Department, answer);
    }

    [Fact]
    public void EnumScore_EqualsTheGeneratedSetsAnswer()
    {
        var set = Built(QuestionSet.CreateBuilder().Score<Frustration>("frustration", "How frustrated?", out var frustration, l => l
            .Level(Frustration.Calm, "Calm").Level(Frustration.Frustrated, "Frustrated").Level(Frustration.VeryAngry, "Very angry")));

        var answer = Parse(set, Fixture.Text("response-score.json")).Get(frustration);

        Assert.Equal(ResponseAnswers.Parse<FrustrationCheck>(Fixture.Text("response-score.json")).Frustration, answer);
    }

    [Fact]
    public void EnumScore_LevelIndexes_MapToTheMembersInCallOrder()
    {
        // OutOfOrderLevel declares High = 2, Low = 0, Medium = 1; given in declaration order, level 0 is High.
        var set = Built(QuestionSet.CreateBuilder().Score<OutOfOrderLevel>("level", "How?", out var level, l => l
            .Level(OutOfOrderLevel.High, "High").Level(OutOfOrderLevel.Low, "Low").Level(OutOfOrderLevel.Medium, "Medium")));

        var answer = Parse(set, """
            {"answers":{"level":{"type":"score","score":0.25,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.6,"1":0.3,"2":0.1},"confidence":0.4}}}
            """).Get(level);

        Assert.Equal(OutOfOrderLevel.High, answer.Value);
        Assert.Equal(0.6, answer.Probabilities[OutOfOrderLevel.High]);
        Assert.Equal(0.1, answer.Probabilities[OutOfOrderLevel.Medium]);
    }

    [Fact]
    public void Mixed_UsesSeparateBufferSlices_AndSkipsUnknownAnswers()
    {
        const string response = """
            {"answers":{
              "mood":{"type":"score","score":0.2,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.8,"1":0.2,"2":0.0},"confidence":0.7},
              "extra_question":{"type":"noul","noul":1},
              "requests_credentials":{"noul":0.1,"type":"noul"},
              "team":{"type":"choice","choice":"technical","probabilities":{"billing":0.1,"technical":0.9},"confidence":0.85}
            }}
            """;
        var set = Built(QuestionSet.CreateBuilder()
            .Noul("requests_credentials", "Does `message` ask for a credential?", out var credentials)
            .Choice<Department>("team", "Which team should handle `message`?", out var team)
            .Score<Frustration>("mood", "How frustrated is the customer?", out var mood, l => l
                .Level(Frustration.Calm, "Calm").Level(Frustration.Frustrated, "Frustrated").Level(Frustration.VeryAngry, "Very angry")));
        var generated = ResponseAnswers.Parse<TicketTriage>(response);

        var answers = Parse(set, response);

        Assert.Equal(generated.RequestsCredentials.Probability, answers.Get(credentials).Probability);
        Assert.Equal(generated.Team, answers.Get(team));
        Assert.Equal(generated.Mood, answers.Get(mood));
    }

    [Fact]
    public void KeyedChoice_ReadsTheKeyConfidenceAndProbabilities()
    {
        var (set, product, _) = KeyedSet();

        var answer = Parse(set, KeyedResponse).Get(product);

        Assert.Equal("team-plan", answer.Value);
        Assert.Equal(0.66, answer.Confidence);
        Assert.Equal(3, answer.Probabilities.Count);
        Assert.Equal(0.25, answer.Probabilities["pro-plan"]);
        Assert.Equal(0.0, answer.Probabilities["other"]);
        Assert.Equal(0.7, answer.Probabilities[1]);
        Assert.Equal(
            [("pro-plan", 0.25), ("team-plan", 0.7), ("other", 0.0)],
            Enumerate(answer.Probabilities));
    }

    [Fact]
    public void KeyedScore_ArgmaxTie_IsTheLowerLevel()
    {
        var (set, _, effort) = KeyedSet();

        var answer = Parse(set, KeyedResponse).Get(effort);

        Assert.Equal(0, answer.Level);
        Assert.Equal(0.8, answer.Expected);
        Assert.Equal(0.5, answer.Confidence);
        Assert.Equal(0.2, answer.Probabilities[2]);
        Assert.Equal(0.4, answer.Probabilities["1"]);
    }

    [Fact]
    public void KeyedAnswers_HaveValueEquality()
    {
        var (set, product, effort) = KeyedSet();
        var first = Parse(set, KeyedResponse);
        var second = Parse(set, KeyedResponse);

        Assert.Equal(first.Get(product), second.Get(product));
        Assert.Equal(first.Get(product).GetHashCode(), second.Get(product).GetHashCode());
        Assert.Equal(first.Get(effort), second.Get(effort));
        Assert.True(first.Get(product) == second.Get(product));
        Assert.NotEqual(default, first.Get(product));
    }

    [Fact]
    public void KeyedProbabilityMap_RejectsUnknownKeys()
    {
        var (set, product, _) = KeyedSet();
        var probabilities = Parse(set, KeyedResponse).Get(product).Probabilities;

        Assert.Equal("key", Assert.Throws<ArgumentOutOfRangeException>(() => probabilities["legacy-plan"]).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(() => probabilities[null!]).ParamName);
        Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => probabilities[3]).ParamName);
    }

    [Fact]
    public void MissingAnswer_IsInvalidResponse()
    {
        var (set, _, _) = KeyedSet();

        var result = Read(set, Encoding.UTF8.GetBytes("""{"answers":{"extra":{"type":"noul","noul":1}}}"""));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.IsType<JsonException>(result.Error.Exception);
        Assert.Contains("'product'", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_AllocatesNothing_ForEveryAnswerKind()
    {
        var set = Built(QuestionSet.CreateBuilder()
            .Noul("n", "Urgent?", out var noul)
            .Choice<Department>("d", "Which team?", out var department)
            .Score<Frustration>("f", "How frustrated?", out var frustration, l => l
                .Level(Frustration.Calm, "Calm").Level(Frustration.Frustrated, "Frustrated").Level(Frustration.VeryAngry, "Very angry"))
            .Choice("product", "Which product?", out var product, o => o.Option("pro-plan", "Pro").Option("team-plan", "Team").Option("other"))
            .Score("effort", "How much effort?", out var effort, l => l.Level("Minutes").Level("Hours").Level("Days")));
        var answers = Parse(set, """
            {"answers":{
              "n":{"type":"noul","noul":0.95},
              "d":{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12,"sales":0.0},"confidence":0.81},
              "f":{"type":"score","score":1.05,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.95,"2":0.05},"confidence":0.92},
              "product":{"type":"choice","choice":"team-plan","probabilities":{"pro-plan":0.25,"team-plan":0.7},"confidence":0.66},
              "effort":{"type":"score","score":0.8,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.4,"1":0.4,"2":0.2},"confidence":0.5}
            }}
            """);

        // Warm up the JIT before measuring.
        _ = answers.Get(noul);
        _ = answers.Get(department);
        _ = answers.Get(frustration);
        _ = answers.Get(product);
        _ = answers.Get(effort);

        AllocationGate.AssertBudget(
            0,
            1000,
            () =>
            {
                _ = answers.Get(noul);
                _ = answers.Get(department);
                _ = answers.Get(frustration);
                _ = answers.Get(product);
                _ = answers.Get(effort);
            },
            "AnswersGet");
    }

    [Fact]
    public void AHandleFromAnotherSet_Throws_ForEveryHandleKind()
    {
        var (set, _, _) = KeyedSet();
        var (_, otherProduct, otherEffort) = KeyedSet();
        _ = QuestionSet.CreateBuilder()
            .Noul("n", "Urgent?", out var otherNoul)
            .Choice<Department>("d", "Which team?", out var otherDepartment)
            .Score<Frustration>("f", "How?", out var otherFrustration, l => l.Level(Frustration.Calm, "Calm"));
        var answers = Parse(set, KeyedResponse);

        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(otherProduct)).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(otherEffort)).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(otherNoul)).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(otherDepartment)).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(otherFrustration)).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(default(KeyedChoiceHandle))).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(default(NoulHandle))).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(default(ChoiceHandle<Department>))).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(default(ScoreHandle<Frustration>))).ParamName);
        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(default(KeyedScoreHandle))).ParamName);
    }

    [Fact]
    public void AHandleAddedAfterTheBuild_Throws()
    {
        var builder = QuestionSet.CreateBuilder().Noul("is_urgent", "Urgent?", out _);
        var set = Built(builder);
        builder.Noul("later", "Later?", out var later);
        var answers = Parse(set, Fixture.Text("response-noul.json"));

        Assert.Equal("question", Assert.Throws<ArgumentException>(() => answers.Get(later)).ParamName);
    }

    [Fact]
    public void DuplicateAnswerKeys_LastWins_AsInAGeneratedSet()
    {
        const string response = """
            {"answers":{
              "is_urgent":{"type":"noul","noul":0.1},
              "department":{"type":"choice","choice":"billing","probabilities":{"billing":0.9,"technical":0.1,"sales":0.0},"confidence":0.8},
              "is_urgent":{"type":"noul","noul":0.7},
              "department":{"type":"choice","choice":"technical","probabilities":{"billing":0.2,"technical":0.8,"sales":0.0},"confidence":0.6}
            }}
            """;
        var set = Built(QuestionSet.CreateBuilder()
            .Noul("is_urgent", "Urgent?", out var urgent)
            .Choice<Department>("department", "Which team?", out var department));

        var answers = Parse(set, response);

        Assert.Equal(0.7, answers.Get(urgent).Probability);
        Assert.Equal(Department.Technical, answers.Get(department).Value);
        Assert.Equal(0.8, answers.Get(department).Probabilities[Department.Technical]);
        Assert.Equal(0.6, answers.Get(department).Confidence);
    }

    [Fact]
    public void MoreThan256Questions_ParseAndReadTheLastAnswer()
    {
        var (set, handles) = ManyNoulSet(257);
        var response = "{\"answers\":{" + string.Join(",", Enumerable.Range(0, 257).Select(i => $"\"q{i}\":{{\"type\":\"noul\",\"noul\":{(i == 256 ? "0.75" : "0.5")}}}")) + "}}";

        var answers = Parse(set, response);

        Assert.Equal(0.75, answers.Get(handles[256]).Probability);
        Assert.Equal(0.5, answers.Get(handles[0]).Probability);
    }

    [Fact]
    public void MoreThan256Questions_MissingAnswer_IsInvalidResponse()
    {
        var (set, _) = ManyNoulSet(257);
        var response = "{\"answers\":{" + string.Join(",", Enumerable.Range(0, 256).Select(i => $"\"q{i}\":{{\"type\":\"noul\",\"noul\":0.5}}")) + "}}";

        var result = Read(set, Encoding.UTF8.GetBytes(response));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Contains("'q256'", result.Error.Message, StringComparison.Ordinal);
    }

    private static (QuestionSet Set, List<NoulHandle> Handles) ManyNoulSet(int count)
    {
        var builder = QuestionSet.CreateBuilder();
        var handles = new List<NoulHandle>();
        for (var i = 0; i < count; i++)
        {
            builder.Noul($"q{i}", "Q?", out var handle);
            handles.Add(handle);
        }

        return (Built(builder), handles);
    }

    private static (QuestionSet Set, KeyedChoiceHandle Product, KeyedScoreHandle Effort) KeyedSet()
    {
        var set = Built(QuestionSet.CreateBuilder()
            .Choice("product", "Which product?", out var product, o => o.Option("pro-plan", "Pro").Option("team-plan", "Team").Option("other"))
            .Score("effort", "How much effort?", out var effort, l => l.Level("Minutes").Level("Hours").Level("Days")));
        return (set, product, effort);
    }

    private static List<(string, double)> Enumerate(KeyedProbabilityMap map)
    {
        var items = new List<(string, double)>();
        foreach (var (key, probability) in map)
        {
            items.Add((key, probability));
        }

        return items;
    }

    private static Answers Parse(QuestionSet set, string response)
    {
        var result = Read(set, Encoding.UTF8.GetBytes(response));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    // Reads the whole body through the protocol, as the built-set extension does, and builds the answers from its slots.
    private static Result<Answers, DecisionError> Read(QuestionSet set, byte[] response)
    {
        var read = SystemOneProtocol.Instance.ReadResponse(response, set.Definition);
        return read.IsSuccess
            ? Result<Answers, DecisionError>.Success(new Answers(set, read.Value.Probabilities, read.Value.Slots))
            : Result<Answers, DecisionError>.Failure(read.Error);
    }

    private static QuestionSet Built(QuestionSetBuilder builder)
    {
        var built = builder.Build();
        Assert.True(built.IsSuccess, built.IsFailure ? built.Error.ToString() : null);
        return built.Value;
    }
}
