using System.Text.Json;

namespace Minos;

/// <summary>Shared plumbing for typed evaluation: checking a caller's state.</summary>
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
}
