using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Minos.Transport;

namespace Minos;

/// <summary>Shared plumbing for typed evaluation: checking a caller's state, and serializing a typed one.</summary>
internal static class TypedEvaluation
{
    // Room for a typical state, so most need no regrowth.
    private const int TypedStateSize = 256;

    /// <summary>
    /// Serializes a typed state through its source-generated metadata into content that keeps the serializer's bytes,
    /// so the request writer sends them unchanged, as a converter that writes raw JSON wrote them.
    /// </summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="stateTypeInfo">The metadata <paramref name="state"/> is serialized with.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <returns>The content.</returns>
    /// <remarks>
    /// The writer has default options, as the request writer's own does, so the state is escaped as it would be if it
    /// were serialized straight into the body; the options of <paramref name="stateTypeInfo"/> do not change that.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="state"/> does not serialize to a string, object or array.</exception>
    public static DecisionContent SerializeState<TState>(TState state, JsonTypeInfo<TState> stateTypeInfo, string paramName)
    {
        using var buffer = RawJson.Create(ArrayPool<byte>.Shared, TypedStateSize);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, state, stateTypeInfo);
        }

        EnsureStateJson(buffer.Span, paramName);
        return DecisionContent.FromCheckedUtf8State(buffer.Span);
    }

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
