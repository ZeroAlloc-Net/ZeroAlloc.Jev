using System.Buffers;
using ZeroAlloc.Rest;

namespace Minos.Transport;

/// <summary>
/// The serializer for <see cref="IDecisionApi.SendAsync"/>: it passes <see cref="RawJson"/> bodies through as bytes,
/// without parsing them or building an object model.
/// </summary>
internal sealed class DecisionRawSerializer : IRestSerializer
{
    // Large enough for a typical /v1/systemone response, small enough to stay out of the large object heap.
    private const int InitialResponseCapacity = 4096;

    // The least free space each read is offered, so reads near the end of the buffer do not shrink to a few bytes.
    private const int MinimumReadSize = 1024;

    private readonly ArrayPool<byte> _pool;

    /// <summary>Initializes a new instance of the <see cref="DecisionRawSerializer"/> class over the shared pool.</summary>
    /// <remarks>Public and parameterless because ZeroAlloc.Rest's generated DI registration constructs it.</remarks>
    public DecisionRawSerializer()
        : this(ArrayPool<byte>.Shared)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DecisionRawSerializer"/> class.</summary>
    /// <param name="pool">The pool response buffers are rented from.</param>
    internal DecisionRawSerializer(ArrayPool<byte> pool) => _pool = pool;

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <summary>Reads the whole stream into a pooled <see cref="RawJson"/>, which the caller owns and must dispose.</summary>
    /// <typeparam name="T">Must be <see cref="RawJson"/>.</typeparam>
    /// <param name="stream">The response body.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The body.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not <see cref="RawJson"/>.</exception>
    public async ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (typeof(T) != typeof(RawJson))
        {
            throw new NotSupportedException("DecisionRawSerializer reads only RawJson, not " + typeof(T).Name + ".");
        }

        var raw = RawJson.Create(_pool, InitialCapacity(stream));
        try
        {
            int read;
            while ((read = await stream.ReadAsync(raw.GetMemory(MinimumReadSize), ct).ConfigureAwait(false)) > 0)
            {
                raw.Advance(read);
            }

            return (T)(object)raw;
        }
        catch
        {
            raw.Dispose();
            throw;
        }
    }

    // A seekable stream, such as a buffered response, knows its remaining length: renting that plus room for the final
    // zero-byte read means the body is read without regrowth. Otherwise the default capacity is a guess.
    private static int InitialCapacity(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return InitialResponseCapacity;
        }

        var remaining = stream.Length - stream.Position;
        return remaining <= 0 ? MinimumReadSize : (int)Math.Min(remaining + MinimumReadSize, Array.MaxLength);
    }

    /// <summary>Writes a <see cref="RawJson"/> body's bytes to the stream. The body is not disposed.</summary>
    /// <typeparam name="T">Must be <see cref="RawJson"/>.</typeparam>
    /// <param name="stream">The request body stream.</param>
    /// <param name="value">The body.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when the bytes are written.</returns>
    /// <exception cref="NotSupportedException"><paramref name="value"/> is not a <see cref="RawJson"/>.</exception>
    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (value is not RawJson raw)
        {
            throw new NotSupportedException("DecisionRawSerializer writes only RawJson, not " + typeof(T).Name + ".");
        }

        return stream.WriteAsync(raw.Memory, ct);
    }
}
