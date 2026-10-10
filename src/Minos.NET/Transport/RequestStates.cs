using System.Buffers;
using System.Text.Json;
using Minos.Protocols;

namespace Minos.Transport;

/// <summary>
/// Checks a caller's state and hands it, with a writer for its JSON value, to a protocol's
/// <see cref="IDecisionProtocol.WriteRequest{TArg}"/>, which writes the request body around it. The state is written
/// straight into the protocol's pooled body; no <see cref="SystemOneRequest"/> is built.
/// </summary>
internal static class RequestStates
{
    // A guess for states whose size is unknown until they are written.
    private const int UnknownStateSize = 1024;

    /// <summary>Writes a request with a text state.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The text.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson WriteRequest(this IDecisionProtocol protocol, QuestionSetDefinition definition, string state, string model, ArrayPool<byte> pool)
        => protocol.WriteRequest<string>(definition, state, state.Length, static (writer, _, text) => writer.WriteStringValue(text), model, pool);

    /// <summary>Writes a request with a JSON state, which must be a string, object or array.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The state.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    public static RawJson WriteRequest(this IDecisionProtocol protocol, QuestionSetDefinition definition, JsonElement state, string model, ArrayPool<byte> pool)
    {
        TypedEvaluation.EnsureStateKind(state.ValueKind, nameof(state));
        return protocol.WriteRequest<JsonElement>(definition, state, UnknownStateSize, static (writer, _, element) => element.WriteTo(writer), model, pool);
    }

    /// <summary>Writes a request with a <see cref="DecisionContent"/> state: text as a string, JSON as its value.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The state, which must be initialized.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson WriteRequest(this IDecisionProtocol protocol, QuestionSetDefinition definition, DecisionContent state, string model, ArrayPool<byte> pool)
    {
        if (state.TryGetString(out var text))
        {
            return protocol.WriteRequest(definition, text, model, pool);
        }

        state.TryGetJson(out var json);
        return protocol.WriteRequest(definition, json, model, pool);
    }
}
