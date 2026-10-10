using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Minos.Serialization;

namespace Minos;

/// <summary>
/// A value the Jev API accepts as either plain text or structured JSON (an object or array),
/// such as a request <c>state</c>, question <c>instructions</c>, or a criteria description.
/// </summary>
[JsonConverter(typeof(DecisionContentConverter))]
public readonly struct DecisionContent : IEquatable<DecisionContent>
{
    // Text as a string, or a caller's UTF-8 JSON state held without a copy: a whole array as itself, any other memory
    // in a Utf8Memory. Null for structured JSON, which is in _json, and for uninitialized content. Sharing one field
    // keeps the struct, which every DecisionRequest carries, at its size.
    private readonly object? _value;
    private readonly JsonElement _json;

    private DecisionContent(string text)
    {
        _value = text;
        _json = default;
    }

    private DecisionContent(JsonElement json)
    {
        _value = null;
        _json = json;
    }

    private DecisionContent(object utf8State)
    {
        _value = utf8State;
        _json = default;
    }

    /// <summary>Gets a value indicating whether this content is plain text.</summary>
    public bool IsString => _value is string || (TryGetUtf8State(out var utf8) && IsJsonString(utf8));

    /// <summary>Creates text content.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static DecisionContent FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new DecisionContent(text);
    }

    /// <summary>
    /// Creates content from JSON. A JSON string becomes text; any other value is stored as a detached copy.
    /// </summary>
    /// <param name="json">The JSON value.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="json"/> is not a string, object or array.
    /// </exception>
    public static DecisionContent FromJson(JsonElement json)
    {
        return json.ValueKind switch
        {
            JsonValueKind.String => new DecisionContent(json.GetString()!),
            JsonValueKind.Object or JsonValueKind.Array => new DecisionContent(json.Clone()),
            _ => throw new ArgumentException(
                "Decision content must be a string, object or array.", nameof(json)),
        };
    }

    /// <summary>Creates content by serializing a value through its source-generated metadata; no reflection.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value: it must serialize to a JSON string, object or array.</param>
    /// <param name="typeInfo">The metadata to serialize <paramref name="value"/> with.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> does not serialize to a string, object or array.</exception>
    public static DecisionContent FromValue<T>(T value, JsonTypeInfo<T> typeInfo) => FromValue(value, typeInfo, nameof(value));

    /// <summary>Creates content from UTF-8 JSON: exactly one JSON string, object or array.</summary>
    /// <param name="utf8Json">The UTF-8 JSON. The content does not reference it.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="utf8Json"/> is empty, malformed, holds more than one value, or is not a string, object or array.
    /// </exception>
    public static DecisionContent FromUtf8Json(ReadOnlySpan<byte> utf8Json) => FromUtf8Json(utf8Json, nameof(utf8Json));

    internal static DecisionContent FromValue<T>(T value, JsonTypeInfo<T> typeInfo, string paramName)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return FromDetached(JsonSerializer.SerializeToElement(value, typeInfo), paramName);
    }

    internal static DecisionContent FromUtf8Json(ReadOnlySpan<byte> utf8Json, string paramName)
    {
        EnsureSingleJsonValue(utf8Json, paramName);
        var reader = new Utf8JsonReader(utf8Json);
        return FromDetached(JsonElement.ParseValue(ref reader), paramName);
    }

    /// <summary>
    /// Wraps a caller's JSON state without the detached copy <see cref="FromJson"/> makes, so a call does not pay for one.
    /// The caller has already checked its kind with <see cref="TypedEvaluation.EnsureStateKind"/>. An object or array
    /// stays in the caller's document, which must not be disposed until the call the content is sent with completes.
    /// </summary>
    /// <param name="json">A JSON string, object or array. A string becomes text, as with <see cref="FromJson"/>.</param>
    /// <returns>The content.</returns>
    internal static DecisionContent FromCheckedJsonState(JsonElement json)
        => json.ValueKind == JsonValueKind.String ? new DecisionContent(json.GetString()!) : new DecisionContent(json);

    /// <summary>
    /// Wraps a caller's UTF-8 JSON state without copying it, so the request writer sends its bytes unchanged. The
    /// caller has already checked it with <see cref="TypedEvaluation.EnsureStateJson"/>. The content reads the memory,
    /// so it must not change until the call the content is sent with completes.
    /// </summary>
    /// <param name="utf8Json">One JSON string, object or array.</param>
    /// <returns>The content.</returns>
    internal static DecisionContent FromCheckedUtf8State(ReadOnlyMemory<byte> utf8Json)
        => MemoryMarshal.TryGetArray(utf8Json, out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? new DecisionContent((object)segment.Array)
            : new DecisionContent(new Utf8Memory(utf8Json));

    /// <summary>Gets the caller's UTF-8 JSON, when this content is a state from <see cref="FromCheckedUtf8State"/>.</summary>
    /// <param name="utf8Json">The bytes as the caller wrote them, or empty.</param>
    /// <returns><see langword="true"/> when this content holds a caller's UTF-8 JSON.</returns>
    internal bool TryGetUtf8State(out ReadOnlySpan<byte> utf8Json)
    {
        switch (_value)
        {
            case byte[] array:
                utf8Json = array;
                return true;
            case Utf8Memory memory:
                utf8Json = memory.Memory.Span;
                return true;
            default:
                utf8Json = default;
                return false;
        }
    }

    /// <summary>Wraps an element that already owns its document, with no second copy.</summary>
    private static DecisionContent FromDetached(JsonElement json, string paramName) => json.ValueKind switch
    {
        JsonValueKind.String => new DecisionContent(json.GetString()!),
        JsonValueKind.Object or JsonValueKind.Array => new DecisionContent(json),
        _ => throw new ArgumentException("Decision content must be a JSON string, object or array.", paramName),
    };

    /// <summary>Checks that <paramref name="utf8Json"/> is exactly one complete JSON value. Does not allocate.</summary>
    /// <param name="utf8Json">The UTF-8 JSON to check.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException">The input is empty, malformed, truncated or holds more than one value.</exception>
    internal static void EnsureSingleJsonValue(ReadOnlySpan<byte> utf8Json, string paramName)
    {
        var reader = new Utf8JsonReader(utf8Json);
        try
        {
            if (!reader.Read())
            {
                throw new ArgumentException("The JSON is empty.", paramName);
            }

            reader.Skip();
            if (reader.Read())
            {
                throw new ArgumentException("The input must be a single JSON value.", paramName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The input is not a single, well-formed JSON value.", paramName, exception);
        }
    }

    /// <summary>Converts text to content.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static implicit operator DecisionContent(string text) => FromString(text);

    /// <summary>Compares two content values for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when both hold equal text or deeply equal JSON.</returns>
    public static bool operator ==(DecisionContent left, DecisionContent right) => left.Equals(right);

    /// <summary>Compares two content values for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(DecisionContent left, DecisionContent right) => !left.Equals(right);

    /// <summary>Throws unless <paramref name="content"/> holds text or JSON.</summary>
    /// <param name="content">The content to check.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException"><paramref name="content"/> is <see langword="default"/>.</exception>
    internal static void EnsureInitialized(DecisionContent content, string paramName)
    {
        if (content._value is null && content._json.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException(
                "The content is uninitialized: create it with FromString, FromJson, FromValue or FromUtf8Json.", paramName);
        }
    }

    /// <summary>Gets the text, when this content is plain text.</summary>
    /// <param name="text">The text, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when this content is plain text.</returns>
    public bool TryGetString([NotNullWhen(true)] out string? text)
    {
        text = Resolve()._value as string;
        return text is not null;
    }

    /// <summary>Gets the JSON value, when this content is structured JSON.</summary>
    /// <param name="json">The JSON value, or <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when this content is structured JSON.</returns>
    public bool TryGetJson(out JsonElement json)
    {
        var content = Resolve();
        json = content._json;
        return content._value is null && json.ValueKind != JsonValueKind.Undefined;
    }

    /// <inheritdoc />
    public bool Equals(DecisionContent other)
    {
        var content = Resolve();
        var otherContent = other.Resolve();
        if (content._value is not null || otherContent._value is not null)
        {
            return string.Equals(content._value as string, otherContent._value as string, StringComparison.Ordinal);
        }

        var json = content._json;
        var otherJson = otherContent._json;
        if (json.ValueKind == JsonValueKind.Undefined || otherJson.ValueKind == JsonValueKind.Undefined)
        {
            return json.ValueKind == otherJson.ValueKind;
        }

        return JsonElement.DeepEquals(json, otherJson);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DecisionContent other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var content = Resolve();
        if (content._value is string text)
        {
            return StringComparer.Ordinal.GetHashCode(text);
        }

        var json = content._json;
        return json.ValueKind.GetHashCode();
    }

    /// <summary>Returns the text, or the raw JSON for structured content.</summary>
    /// <returns>The text or raw JSON; empty for uninitialized content.</returns>
    public override string ToString()
    {
        var content = Resolve();
        if (content._value is string text)
        {
            return text;
        }

        var json = content._json;
        return json.ValueKind == JsonValueKind.Undefined ? string.Empty : json.GetRawText();
    }

    private static bool IsJsonString(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        return reader.Read() && reader.TokenType == JsonTokenType.String;
    }

    // A caller's UTF-8 state as the text or JSON FromUtf8Json would have made of it, parsed each time a public member
    // needs it; any other content as itself.
    private DecisionContent Resolve()
    {
        if (!TryGetUtf8State(out var utf8))
        {
            return this;
        }

        var reader = new Utf8JsonReader(utf8);
        return FromDetached(JsonElement.ParseValue(ref reader), nameof(utf8));
    }

    /// <summary>A caller's UTF-8 state that is not a whole array, boxed so it fits the content's one reference field.</summary>
    /// <param name="memory">The state.</param>
    private sealed class Utf8Memory(ReadOnlyMemory<byte> memory)
    {
        public ReadOnlyMemory<byte> Memory { get; } = memory;
    }
}
