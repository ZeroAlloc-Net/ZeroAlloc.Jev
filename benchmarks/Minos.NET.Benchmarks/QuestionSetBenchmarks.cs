using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Minos.Protocols;
using ZeroAlloc.Results;

namespace Minos.Benchmarks;

/// <summary>Twenty Noul questions, the generated counterpart of <see cref="QuestionSetBenchmarks"/>' built twenty-question set.</summary>
[Questions]
public partial record BenchTwenty
{
    [Noul("Question 1?")] public partial Noul Q01 { get; }
    [Noul("Question 2?")] public partial Noul Q02 { get; }
    [Noul("Question 3?")] public partial Noul Q03 { get; }
    [Noul("Question 4?")] public partial Noul Q04 { get; }
    [Noul("Question 5?")] public partial Noul Q05 { get; }
    [Noul("Question 6?")] public partial Noul Q06 { get; }
    [Noul("Question 7?")] public partial Noul Q07 { get; }
    [Noul("Question 8?")] public partial Noul Q08 { get; }
    [Noul("Question 9?")] public partial Noul Q09 { get; }
    [Noul("Question 10?")] public partial Noul Q10 { get; }
    [Noul("Question 11?")] public partial Noul Q11 { get; }
    [Noul("Question 12?")] public partial Noul Q12 { get; }
    [Noul("Question 13?")] public partial Noul Q13 { get; }
    [Noul("Question 14?")] public partial Noul Q14 { get; }
    [Noul("Question 15?")] public partial Noul Q15 { get; }
    [Noul("Question 16?")] public partial Noul Q16 { get; }
    [Noul("Question 17?")] public partial Noul Q17 { get; }
    [Noul("Question 18?")] public partial Noul Q18 { get; }
    [Noul("Question 19?")] public partial Noul Q19 { get; }
    [Noul("Question 20?")] public partial Noul Q20 { get; }
}

/// <summary>
/// Question sets built at run time: building one, writing a new definition's questions once, evaluating one, and parsing
/// twenty answers against the generated parser.
/// </summary>
[MemoryDiagnoser]
public class QuestionSetBenchmarks
{
    private static readonly AnswerFactory<BenchTwenty> TwentyFactory = static answers => BenchTwenty.Create(answers);

    private const string State = "Help! My payouts have been failing for 3 days.";

    private QuestionSetBuilder _builder = null!;
    private QuestionSet _triage = null!;
    private QuestionDefinition[] _triageQuestions = [];
    private QuestionSet _twenty = null!;
    private AnswerFactory<Answers> _twentyFactory = null!;
    private byte[] _twentyAnswers = [];
    private HttpClient _http = null!;
    private DecisionClient _client = null!;

    /// <summary>The Noul, enum Choice and enum Score of <see cref="ClientBenchmarks.TypedEvaluateAsync"/>, as a builder.</summary>
    internal static QuestionSetBuilder TriageBuilder()
        => QuestionSet.CreateBuilder()
            .Noul("requests_credentials", "Does `message` ask for a credential?", out _)
            .Choice<Team>("team", "Which team should handle `message`?", out _, o => o
                .Describe(Team.Billing, "Charges, invoices, refunds")
                .Describe(Team.Account, "Login, profile, permissions"))
            .Score<Urgency>("urgency", "How urgent is `message`?", out _, l => l
                .Level(Urgency.Low, "Can wait").Level(Urgency.Medium, "This week").Level(Urgency.High, "Today"));

    [GlobalSetup]
    public void Setup()
    {
        _builder = TriageBuilder();
        _triage = _builder.Build().Value;
        _triageQuestions = [.. _triage.Definition.Questions];

        var twenty = QuestionSet.CreateBuilder();
        var answers = new StringBuilder("{");
        for (var i = 1; i <= 20; i++)
        {
            var key = "q" + i.ToString("00", CultureInfo.InvariantCulture);
            twenty.Noul(key, "Question " + i.ToString(CultureInfo.InvariantCulture) + "?", out _);
            answers.Append(i > 1 ? "," : string.Empty).Append('"').Append(key).Append("\":{\"type\":\"noul\",\"noul\":0.5}");
        }

        _twenty = twenty.Build().Value;
        _twentyFactory = answers => new Answers(_twenty, answers.Probabilities, answers.HeapSlots ?? answers.Slots.ToArray());
        _twentyAnswers = Encoding.UTF8.GetBytes(answers.Append('}').ToString());

        (_http, _client) = ClientBenchmarks.CreateClient(ClientBenchmarks.TriageResponseJson);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _http.Dispose();
    }

    /// <summary><see cref="QuestionSetBuilder.Build"/> of a Noul, an enum Choice and an enum Score.</summary>
    [Benchmark]
    public Result<QuestionSet, DecisionError> Build() => _builder.Build();

    /// <summary>A new definition over the triage set's three questions, the baseline for <see cref="NewDefinitionAndQuestionsJson"/>.</summary>
    [Benchmark]
    public QuestionSetDefinition NewDefinition() => new(_triageQuestions);

    /// <summary>
    /// A new definition over the same questions, then its first <c>questions</c> object: the one-time cost per set, outside
    /// any per-call budget. The difference from <see cref="NewDefinition"/> is the first serialization alone.
    /// </summary>
    [Benchmark]
    public int NewDefinitionAndQuestionsJson() => SystemOneProtocol.QuestionsJson(new QuestionSetDefinition(_triageQuestions)).Length;

    /// <summary>The same three questions as <see cref="ClientBenchmarks.TypedEvaluateAsync"/>, evaluated as a built set.</summary>
    [Benchmark]
    public ValueTask<Result<Answers, DecisionError>> EvaluateBuiltSet() => _client.EvaluateAsync(_triage, State);

    /// <summary>Parses twenty answers through the protocol into the built set's <see cref="Answers"/>.</summary>
    [Benchmark]
    public Answers ParseBuiltTwenty()
    {
        var reader = new Utf8JsonReader(_twentyAnswers);
        reader.Read();
        return SystemOneProtocol.Instance.ReadAnswers(ref reader, _twenty.Definition, _twentyFactory);
    }

    /// <summary>Parses the same twenty answers through the protocol into the generated type, as the baseline for the built set.</summary>
    [Benchmark]
    public BenchTwenty ParseGeneratedTwenty()
    {
        var reader = new Utf8JsonReader(_twentyAnswers);
        reader.Read();
        return SystemOneProtocol.Instance.ReadAnswers(ref reader, BenchTwenty.Definition, TwentyFactory);
    }
}
