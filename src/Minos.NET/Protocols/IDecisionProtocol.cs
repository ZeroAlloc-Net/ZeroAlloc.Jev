using System.Buffers;
using System.Text.Json;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.Protocols;

/// <summary>
/// A provider wire format: how a question set's request is written and how its answers are read. The client owns
/// transport, retries, telemetry and logging; a protocol owns only bytes in and answers out.
/// </summary>
internal interface IDecisionProtocol
{
    /// <summary>Rents and writes a request body for <paramref name="definition"/> and a state.</summary>
    RawJson WriteRequest<TArg>(QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct;

    /// <summary>Reads the answers to <paramref name="definition"/> and builds the result with <paramref name="create"/>.</summary>
    /// <exception cref="JsonException">An answer is missing, has the wrong type, names an unknown option or level, or lacks a required field.</exception>
    TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create);

    /// <summary>Gets the path, relative to the client's base address, that requests are POSTed to.</summary>
    string EndpointPath { get; }

    /// <summary>Reads a successful response body into a <see cref="DecisionResponse"/> for <paramref name="definition"/>.</summary>
    /// <returns>The response, or an <see cref="DecisionErrorKind.InvalidResponse"/> error with status 200.</returns>
    Result<DecisionResponse, DecisionError> ReadResponse(ReadOnlySpan<byte> body, QuestionSetDefinition definition);

    /// <summary>Maps an error status and its body to a <see cref="DecisionError"/>.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="body">The error body, up to the transport's limit.</param>
    /// <param name="bodyTruncated">Whether the transport cut the body at its limit.</param>
    /// <param name="contentType">The body's media type, if any.</param>
    /// <param name="retryAfter">The wait the response asked for, already parsed from its headers by the transport.</param>
    DecisionError MapError(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter);
}
