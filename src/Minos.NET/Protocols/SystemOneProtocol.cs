using System.Buffers;
using System.Text.Json;
using Minos.Serialization;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.Protocols;

/// <summary>The <c>/v1/systemone</c> wire format: TypeSafe, OpenRouter, Clef and compatible servers.</summary>
internal sealed class SystemOneProtocol : IDecisionProtocol
{
    private const int CacheSlot = 0;

    // Slots for this many questions live on the stack; larger sets rent nothing and allocate, as before.
    private const int MaxStackSlots = 64;

    // Found flags are one byte each, so they stay on the stack for more questions than slots do.
    private const int MaxStackFlags = 256;

    public static SystemOneProtocol Instance { get; } = new();

    private SystemOneProtocol()
    {
    }

    /// <summary>Gets the <c>questions</c> object for <paramref name="definition"/>, written once and cached on it.</summary>
    public static ReadOnlySpan<byte> QuestionsJson(QuestionSetDefinition definition)
        => definition.GetOrAddProtocolData(CacheSlot, static d => SystemOneQuestionsWriter.Write(d));

    public RawJson WriteRequest<TArg>(QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct
        => SystemOneRequestWriter.Write(QuestionsJson(definition), state, stateSizeHint, writeState, model, pool);

    public string EndpointPath => "v1/systemone";

    public Result<DecisionResponse, DecisionError> ReadResponse(ReadOnlySpan<byte> body, QuestionSetDefinition definition)
        => SystemOneResponseReader.Read(this, body, definition);

    public DecisionError MapError(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter)
        => SystemOneErrors.Map(statusCode, body, bodyTruncated, contentType, retryAfter);

    public TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create)
    {
        SystemOneAnswers.EnsureStartObject(ref answers);
        var questions = definition.QuestionArray;
        var count = questions.Length;
        var probabilities = definition.ProbabilityCount == 0 ? [] : new double[definition.ProbabilityCount];
        var heapSlots = count <= MaxStackSlots ? null : new AnswerSlot[count];
        Span<AnswerSlot> slots = heapSlots ?? stackalloc AnswerSlot[count];
        Span<bool> found = count <= MaxStackFlags ? stackalloc bool[count] : new bool[count];

        while (SystemOneAnswers.NextProperty(ref answers))
        {
            var index = Utf8Keys.IndexOf(ref answers, definition.KeysUtf8);
            if (index < 0)
            {
                answers.Skip();
                continue;
            }

            answers.Read();
            var offset = definition.Offsets[index];
            var keys = definition.OptionKeysUtf8[index];
            switch (questions[index].Kind)
            {
                case QuestionKind.Noul:
                    slots[index] = new AnswerSlot(0, SystemOneAnswers.ReadNoul(ref answers), 0, 0);
                    break;
                case QuestionKind.Choice:
                    var (choice, confidence) = SystemOneAnswers.ReadChoice(ref answers, keys, probabilities, offset);
                    slots[index] = new AnswerSlot(choice, 0, confidence, offset);
                    break;
                default:
                    var (level, expected, scoreConfidence) = SystemOneAnswers.ReadScore(ref answers, keys, probabilities, offset);
                    slots[index] = new AnswerSlot(level, expected, scoreConfidence, offset);
                    break;
            }

            found[index] = true;
        }

        for (var i = 0; i < count; i++)
        {
            if (!found[i])
            {
                throw SystemOneAnswers.MissingAnswer(questions[i].Key);
            }
        }

        return create(new AnswerSlots(slots, probabilities, definition, heapSlots));
    }
}
