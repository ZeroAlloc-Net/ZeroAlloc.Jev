using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>
/// Shared plumbing for typed evaluation: checking a caller's state, and reading typed answers from a response body,
/// mapping a rejected response to <see cref="DecisionErrorKind.InvalidResponse"/>.
/// </summary>
internal static class TypedEvaluation
{
    /// <summary>Checks that a state's JSON kind is one Jev accepts: a string, object or array.</summary>
    /// <param name="kind">The state's kind.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not a string, object or array.</exception>
    public static void EnsureStateKind(JsonValueKind kind, string paramName)
    {
        if (kind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
        {
            throw InvalidStateKind(paramName);
        }
    }

    /// <summary>
    /// Checks that <paramref name="utf8Json"/> is exactly one complete JSON string, object or array. Does not allocate.
    /// </summary>
    /// <param name="utf8Json">The UTF-8 JSON state.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException">
    /// The input is empty, malformed, truncated, holds more than one value, or is not a string, object or array.
    /// </exception>
    public static void EnsureStateJson(ReadOnlySpan<byte> utf8Json, string paramName)
    {
        DecisionContent.EnsureSingleJsonValue(utf8Json, paramName);

        var reader = new Utf8JsonReader(utf8Json);
        reader.Read();
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.StartObject or JsonTokenType.StartArray))
        {
            throw InvalidStateKind(paramName);
        }
    }

    /// <summary>The exception for a state that is not a JSON string, object or array.</summary>
    /// <param name="paramName">The caller's parameter name.</param>
    /// <returns>The exception.</returns>
    public static ArgumentException InvalidStateKind(string paramName)
        => new("The state must be a JSON string, object or array.", paramName);

    /// <summary>
    /// Parses the typed answers from a complete <c>/v1/systemone</c> response body: finds the top-level
    /// <c>answers</c> property and runs <typeparamref name="T"/>'s parser on it. The rest of the body is read to its
    /// end, so malformed or truncated JSON anywhere is reported. Does not allocate on success.
    /// </summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="responseJson">The UTF-8 response body.</param>
    /// <param name="statusCode">The HTTP status code to report on a failure, when one is known.</param>
    /// <returns>
    /// The typed answers, or an <see cref="DecisionErrorKind.InvalidResponse"/> error when the body is not an object, has no
    /// or several <c>answers</c> properties, or the answers are rejected; a <see cref="JsonException"/> is kept as
    /// <see cref="DecisionError.Exception"/>.
    /// </returns>
    public static Result<T, DecisionError> ParseResponse<T>(ReadOnlySpan<byte> responseJson, int? statusCode = null)
        where T : IQuestionSet<T>
        => ParseResponse(responseJson, GeneratedAnswerParser<T>.Instance, statusCode);

    /// <summary>
    /// Parses the typed answers from a complete <c>/v1/systemone</c> response body: finds the top-level
    /// <c>answers</c> property and runs <paramref name="parse"/> on it. The rest of the body is read to its
    /// end, so malformed or truncated JSON anywhere is reported. Does not allocate on success.
    /// </summary>
    /// <typeparam name="TResult">The typed answers.</typeparam>
    /// <param name="responseJson">The UTF-8 response body.</param>
    /// <param name="parse">The question set's parser.</param>
    /// <param name="statusCode">The HTTP status code to report on a failure, when one is known.</param>
    /// <returns>
    /// The typed answers, or an <see cref="DecisionErrorKind.InvalidResponse"/> error when the body is not an object, has no
    /// or several <c>answers</c> properties, or the answers are rejected; a <see cref="JsonException"/> is kept as
    /// <see cref="DecisionError.Exception"/>.
    /// </returns>
    public static Result<TResult, DecisionError> ParseResponse<TResult>(
        ReadOnlySpan<byte> responseJson, AnswerParser<TResult> parse, int? statusCode = null)
    {
        var reader = new Utf8JsonReader(SkipUtf8Bom(responseJson));
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return Result<TResult, DecisionError>.Failure(Invalid("The response body is not a JSON object.", statusCode));
            }

            var found = false;
            TResult? answers = default;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isAnswers = reader.ValueTextEquals("answers"u8);
                reader.Read();
                if (!isAnswers)
                {
                    reader.Skip();
                    continue;
                }

                if (found)
                {
                    return Result<TResult, DecisionError>.Failure(Invalid("The response has more than one answers property.", statusCode));
                }

                if (reader.TokenType == JsonTokenType.Null)
                {
                    return Result<TResult, DecisionError>.Failure(Invalid("The response's answers are null.", statusCode));
                }

                answers = parse(ref reader);
                found = true;
            }

            // Reads past the root object, so trailing content after it is reported.
            reader.Read();

            return found
                ? Result<TResult, DecisionError>.Success(answers!)
                : Result<TResult, DecisionError>.Failure(Invalid("The response has no answers.", statusCode));
        }
        catch (JsonException exception)
        {
            return Result<TResult, DecisionError>.Failure(Rejected(exception, statusCode));
        }
    }

    // Utf8JsonReader treats a leading UTF-8 BOM as an invalid start of a value, while the untyped path's
    // stream-based deserializer (System.Text.Json's Deserialize(Stream)/DeserializeAsync(Stream)) skips one. A
    // response body read straight into these reader-based paths must match that tolerance.
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    internal static ReadOnlySpan<byte> SkipUtf8Bom(ReadOnlySpan<byte> json)
        => json.StartsWith(Utf8Bom) ? json[Utf8Bom.Length..] : json;

    private static DecisionError Rejected(JsonException exception, int? statusCode)
        => new(DecisionErrorKind.InvalidResponse, "The response could not be read as the question set's answers: " + exception.Message) { StatusCode = statusCode, Exception = exception };

    private static DecisionError Invalid(string message, int? statusCode) => new(DecisionErrorKind.InvalidResponse, message) { StatusCode = statusCode };
}
