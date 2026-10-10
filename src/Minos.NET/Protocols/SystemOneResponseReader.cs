using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.Protocols;

/// <summary>Reads a <c>/v1/systemone</c> body into a <see cref="DecisionResponse"/> in one pass.</summary>
/// <remarks>
/// Fails exactly as the typed path did: the same messages, status 200, and <see cref="DecisionErrorKind.InvalidResponse"/>.
/// <c>model</c>, <c>usage</c>, <c>id</c> and <c>provider</c> are optional and read leniently: a field of the wrong type is
/// treated as absent, never as an error, as telemetry has always read them. The first occurrence of a field decides it, even when that occurrence has the wrong type; a repeat is skipped, and so is a second <c>usage</c> object.
/// </remarks>
internal static class SystemOneResponseReader
{
    private static readonly AnswerFactory<(AnswerSlot[] Slots, double[] Probabilities)> Capture
        = static slots => (slots.HeapSlots ?? slots.Slots.ToArray(), slots.Probabilities);

    // The last model and provider read, shared by the whole process: an allocation cut that keeps the typed call within
    // its AOT gate. A client meets few distinct values, so a value that matches the last one is returned as that same
    // string instead of a new one per response. A race only replaces the cached value; any string returned equals the
    // bytes read.
    private static string? lastModel;
    private static string? lastProvider;

    // Utf8JsonReader treats a leading UTF-8 BOM as an invalid start of a value, while the raw path's stream-based
    // deserializer skips one; a body read here must match that tolerance.
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public static Result<DecisionResponse, DecisionError> Read(SystemOneProtocol protocol, ReadOnlySpan<byte> body, QuestionSetDefinition definition)
    {
        var reader = new Utf8JsonReader(SkipUtf8Bom(body));
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return Invalid("The response body is not a JSON object.");
            }

            (AnswerSlot[] Slots, double[] Probabilities)? answers = null;
            string? model = null, id = null, provider = null;
            bool modelSeen = false, idSeen = false, providerSeen = false, usageSeen = false;
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
                    if (modelSeen)
                    {
                        reader.Skip();
                    }
                    else
                    {
                        modelSeen = true;
                        model = CachedStringOrSkip(ref reader, ref lastModel);
                    }
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    if (idSeen)
                    {
                        reader.Skip();
                    }
                    else
                    {
                        idSeen = true;
                        id = StringOrSkip(ref reader);
                    }
                }
                else if (reader.ValueTextEquals("provider"u8))
                {
                    reader.Read();
                    if (providerSeen)
                    {
                        reader.Skip();
                    }
                    else
                    {
                        providerSeen = true;
                        provider = CachedStringOrSkip(ref reader, ref lastProvider);
                    }
                }
                else if (reader.ValueTextEquals("usage"u8))
                {
                    reader.Read();
                    if (usageSeen)
                    {
                        reader.Skip();
                    }
                    else
                    {
                        usageSeen = true;
                        ReadUsage(ref reader, ref inputTokens, ref outputTokens, ref cost);
                    }
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

    internal static string? CachedStringOrSkip(ref Utf8JsonReader reader, ref string? cache)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        var cached = Volatile.Read(ref cache);
        if (cached is not null && reader.ValueTextEquals(cached))
        {
            return cached;
        }

        var value = reader.GetString();
        Volatile.Write(ref cache, value);
        return value;
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

        bool inputSeen = false, outputSeen = false, costSeen = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var field = reader.ValueTextEquals("input_tokens"u8) ? 1 : reader.ValueTextEquals("output_tokens"u8) ? 2 : reader.ValueTextEquals("cost"u8) ? 3 : 0;
            reader.Read();
            switch (field)
            {
                case 1 when !inputSeen:
                    inputSeen = true;
                    inputTokens = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var input) ? input : null;
                    break;
                case 2 when !outputSeen:
                    outputSeen = true;
                    outputTokens = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var output) ? output : null;
                    break;
                case 3 when !costSeen:
                    costSeen = true;
                    cost = reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var value) ? value : null;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private static Result<DecisionResponse, DecisionError> Invalid(string message)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.InvalidResponse, message) { StatusCode = 200 });

    private static ReadOnlySpan<byte> SkipUtf8Bom(ReadOnlySpan<byte> json)
        => json.StartsWith(Utf8Bom) ? json[Utf8Bom.Length..] : json;
}
