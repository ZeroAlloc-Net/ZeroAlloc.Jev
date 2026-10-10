using System.Buffers;
using System.Text;
using System.Text.Json.Nodes;
using Minos.Protocols;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.Tests;

/// <summary>What the <see cref="DecisionClient"/> test classes share: a client over a stub handler and a fake client.</summary>
internal static class ClientTestKit
{
    public const string TestModel = "jev-test-model";

    // The exception must come from the call itself, before any task exists, as for the default interface methods.
    public static TException ThrowsSynchronously<TException>(Func<Task> call)
        where TException : Exception
    {
        Task? task = null;
        var exception = Record.Exception(() => { task = call(); });
        Assert.Null(task);
        return Assert.IsType<TException>(exception);
    }

    public static StubHandler.Captured OnlyRequest(StubHandler handler)
    {
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        return Assert.Single(handler.Requests);
#pragma warning restore HLQ005
    }

    /// <summary>The body <see cref="DecisionClient"/> sends for <paramref name="request"/> with <see cref="TestModel"/>, as JSON.</summary>
    public static JsonNode ExpectedBody(DecisionRequest request)
    {
        using var body = SystemOneProtocol.Instance.WriteRequest(request.Definition, request.State, TestModel, ArrayPool<byte>.Shared);
        return JsonNode.Parse(Encoding.UTF8.GetString(body.Span))!;
    }

    /// <summary>Creates a client over <paramref name="handler"/>; the caller disposes <paramref name="httpClients"/>.</summary>
    public static DecisionClient Client(
        List<HttpClient> httpClients, StubHandler handler, CountingPool? pool = null, DecisionProvider provider = DecisionProvider.TypeSafe)
    {
        var http = new HttpClient(handler);
        httpClients.Add(http);
        var settings = DecisionClientSettings.Resolve(
            new DecisionClientOptions { ApiKey = "test-key", Provider = provider, Model = TestModel, MaxRetries = 0 },
            _ => null);
        return new DecisionClient(settings, http, ownedHandler: null, TimeProvider.System, pool ?? new CountingPool());
    }

    /// <summary>
    /// A fake <see cref="IDecisionClient"/> that records each <see cref="DecisionRequest"/>. It answers with
    /// <paramref name="failure"/> when given; otherwise with <paramref name="responseJson"/> read through the System One
    /// protocol when given; otherwise with a canned response for <paramref name="answersFor"/>, or the request's own
    /// definition: every Noul at 0.95, and every Choice and Score at index 0 with confidence 0.9 and probability 1.
    /// </summary>
    public sealed class CapturingClient(QuestionSetDefinition? answersFor = null, DecisionError? failure = null, string? responseJson = null)
        : IDecisionClient
    {
        public List<DecisionRequest> Requests { get; } = [];

        public CancellationToken LastToken { get; private set; }

        public DecisionRequest OnlyRequest()
        {
            // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
            return Assert.Single(Requests);
#pragma warning restore HLQ005
        }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            LastToken = cancellationToken;
            if (failure is not null)
            {
                return ValueTask.FromResult(Result<DecisionResponse, DecisionError>.Failure(failure));
            }

            var definition = answersFor ?? request.Definition;
            return ValueTask.FromResult(responseJson is null
                ? Result<DecisionResponse, DecisionError>.Success(Canned(definition))
                : SystemOneProtocol.Instance.ReadResponse(Encoding.UTF8.GetBytes(responseJson), definition));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }

        public static DecisionResponse Canned(QuestionSetDefinition definition)
        {
            var answers = new QuestionAnswer[definition.Questions.Count];
            for (var i = 0; i < answers.Length; i++)
            {
                var question = definition.Questions[i];
                if (question.Kind == QuestionKind.Noul)
                {
                    answers[i] = QuestionAnswer.Noul(0.95);
                    continue;
                }

                var probabilities = new double[question.Options.Count];
                probabilities[0] = 1;
                answers[i] = question.Kind == QuestionKind.Choice
                    ? QuestionAnswer.Choice(0, 0.9, probabilities)
                    : QuestionAnswer.Score(0, 0, 0.9, probabilities);
            }

            return new DecisionResponse(definition, answers, "minos-fake");
        }
    }
}
