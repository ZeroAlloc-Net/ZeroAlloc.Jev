using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.Protocols;

/// <summary>Reads a <c>/v1/systemone</c> body into a <see cref="DecisionResponse"/> in one pass.</summary>
/// <remarks>
/// Fails exactly as the typed path did: the same messages, status 200, and <see cref="DecisionErrorKind.InvalidResponse"/>.
/// <c>model</c>, <c>usage</c>, <c>id</c> and <c>provider</c> are optional and read leniently: a field of the wrong type is
/// treated as absent, never as an error, as telemetry has always read them. A repeated field keeps its first value.
/// </remarks>
internal static class SystemOneResponseReader
{
    private static readonly AnswerFactory<(AnswerSlot[] Slots, double[] Probabilities)> Capture
        = static slots => (slots.HeapSlots ?? slots.Slots.ToArray(), slots.Probabilities);

    public static Result<DecisionResponse, DecisionError> Read(SystemOneProtocol protocol, ReadOnlySpan<byte> body, QuestionSetDefinition definition)
    {
        var reader = new Utf8JsonReader(TypedEvaluation.SkipUtf8Bom(body));
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return Invalid("The response body is not a JSON object.");
            }

            (AnswerSlot[] Slots, double[] Probabilities)? answers = null;
            string? model = null, id = null, provider = null;
            int? inputTokens = null, outputTokens = null;
            double? cost = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("answers"u8))
                {
                    reader.Read();
                    if (answers is not null)
                    {
                        return Invalid("The response has more than one answers property.");
                    }

                    if (reader.TokenType == JsonTokenType.Null)
                    {
                        return Invalid("The response's answers are null.");
                    }

                    answers = protocol.ReadAnswers(ref reader, definition, Capture);
                }
                else if (reader.ValueTextEquals("model"u8))
                {
                    reader.Read();
                    model ??= StringOrSkip(ref reader);
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    id ??= StringOrSkip(ref reader);
                }
                else if (reader.ValueTextEquals("provider"u8))
                {
                    reader.Read();
                    provider ??= StringOrSkip(ref reader);
                }
                else if (reader.ValueTextEquals("usage"u8))
                {
                    reader.Read();
                    ReadUsage(ref reader, ref inputTokens, ref outputTokens, ref cost);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            // Reads past the root object, so trailing content after it is reported.
            reader.Read();

            return answers is { } read
                ? Result<DecisionResponse, DecisionError>.Success(
                    new DecisionResponse(definition, read.Slots, read.Probabilities, model, inputTokens, outputTokens, cost, id, provider))
                : Invalid("The response has no answers.");
        }
        catch (JsonException exception)
        {
            return Result<DecisionResponse, DecisionError>.Failure(new DecisionError(
                DecisionErrorKind.InvalidResponse,
                "The response could not be read as the question set's answers: " + exception.Message)
            {
                StatusCode = 200,
                Exception = exception,
            });
        }
    }

    private static string? StringOrSkip(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip();
        return null;
    }

    private static void ReadUsage(ref Utf8JsonReader reader, ref int? inputTokens, ref int? outputTokens, ref double? cost)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var field = reader.ValueTextEquals("input_tokens"u8) ? 1 : reader.ValueTextEquals("output_tokens"u8) ? 2 : reader.ValueTextEquals("cost"u8) ? 3 : 0;
            reader.Read();
            switch (field)
            {
                case 1 when reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var input) && inputTokens is null:
                    inputTokens = input;
                    break;
                case 2 when reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var output) && outputTokens is null:
                    outputTokens = output;
                    break;
                case 3 when reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var value) && cost is null:
                    cost = value;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private static Result<DecisionResponse, DecisionError> Invalid(string message)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.InvalidResponse, message) { StatusCode = 200 });
}
