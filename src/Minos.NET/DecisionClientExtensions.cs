using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>Typed and built-set evaluation over any <see cref="IDecisionClient"/>, and a typed <see cref="IDecisionClient.GetService"/>.</summary>
/// <remarks>
/// Each call checks its arguments synchronously, builds a <see cref="DecisionRequest"/> from the question set's definition
/// with no model, so the client's configured model applies, calls <see cref="IDecisionClient.EvaluateAsync"/>, and
/// creates the answers from the <see cref="DecisionResponse"/>. When the client's call completes synchronously, so does
/// the extension's.
/// </remarks>
public static class DecisionClientExtensions
{
    /// <summary>Returns the <typeparamref name="TService"/> this client or a client it wraps provides.</summary>
    /// <typeparam name="TService">The type to look for.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="serviceKey">An optional key.</param>
    /// <returns>The object, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public static TService? GetService<TService>(this IDecisionClient client, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.GetService(typeof(TService), serviceKey) is TService service ? service : default;
    }

    /// <summary>Asks <typeparamref name="T"/>'s questions about a text state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[Questions]</c> question set.</typeparam>
    /// <param name="client">The client to ask.</param>
    /// <param name="state">The text to evaluate.</param>
    /// <returns>The typed answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync{T}(IDecisionClient, string, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="state"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <typeparamref name="T"/>'s.</exception>
    public static ValueTask<Result<T, DecisionError>> EvaluateAsync<T>(this IDecisionClient client, string state)
        where T : IQuestionSet<T>
        => EvaluateAsync<T>(client, state, CancellationToken.None);

    /// <summary>Asks <typeparamref name="T"/>'s questions about a text state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[Questions]</c> question set.</typeparam>
    /// <param name="client">The client to ask.</param>
    /// <param name="state">The text to evaluate.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The typed answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="state"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <typeparamref name="T"/>'s.</exception>
    public static ValueTask<Result<T, DecisionError>> EvaluateAsync<T>(this IDecisionClient client, string state, CancellationToken cancellationToken)
        where T : IQuestionSet<T>
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(state);
        return Typed<T>(client, DecisionContent.FromString(state), cancellationToken);
    }

    /// <summary>Asks <typeparamref name="T"/>'s questions about a JSON state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[Questions]</c> question set.</typeparam>
    /// <param name="client">The client to ask.</param>
    /// <param name="state">The state: a JSON string is sent as text; an object or array as structured content.</param>
    /// <returns>The typed answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync{T}(IDecisionClient, JsonElement, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <typeparamref name="T"/>'s.</exception>
    public static ValueTask<Result<T, DecisionError>> EvaluateAsync<T>(this IDecisionClient client, JsonElement state)
        where T : IQuestionSet<T>
        => EvaluateAsync<T>(client, state, CancellationToken.None);

    /// <summary>Asks <typeparamref name="T"/>'s questions about a JSON state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[Questions]</c> question set.</typeparam>
    /// <param name="client">The client to ask.</param>
    /// <param name="state">The state: a JSON string is sent as text; an object or array, such as records or a chat log, as structured content.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The typed answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <typeparamref name="T"/>'s.</exception>
    public static ValueTask<Result<T, DecisionError>> EvaluateAsync<T>(this IDecisionClient client, JsonElement state, CancellationToken cancellationToken)
        where T : IQuestionSet<T>
    {
        ArgumentNullException.ThrowIfNull(client);
        TypedEvaluation.EnsureStateKind(state.ValueKind, nameof(state));
        return Typed<T>(client, DecisionContent.FromJson(state), cancellationToken);
    }

    /// <summary>Asks <typeparamref name="T"/>'s questions about a UTF-8 JSON state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[Questions]</c> question set.</typeparam>
    /// <param name="client">The client to ask.</param>
    /// <param name="utf8JsonState">
    /// The state as one UTF-8 JSON value: a string is sent as text; an object or array as structured content. It is
    /// not referenced after the call returns.
    /// </param>
    /// <returns>The typed answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateUtf8Async{T}(IDecisionClient, ReadOnlyMemory{byte}, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="utf8JsonState"/> is not exactly one well-formed JSON string, object or array.
    /// </exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <typeparamref name="T"/>'s.</exception>
    public static ValueTask<Result<T, DecisionError>> EvaluateUtf8Async<T>(this IDecisionClient client, ReadOnlyMemory<byte> utf8JsonState)
        where T : IQuestionSet<T>
        => EvaluateUtf8Async<T>(client, utf8JsonState, CancellationToken.None);

    /// <summary>Asks <typeparamref name="T"/>'s questions about a UTF-8 JSON state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[Questions]</c> question set.</typeparam>
    /// <param name="client">The client to ask.</param>
    /// <param name="utf8JsonState">
    /// The state as one UTF-8 JSON value: a string is sent as text; an object or array as structured content. It is
    /// not referenced after the call returns.
    /// </param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The typed answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="utf8JsonState"/> is not exactly one well-formed JSON string, object or array.
    /// </exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <typeparamref name="T"/>'s.</exception>
    public static ValueTask<Result<T, DecisionError>> EvaluateUtf8Async<T>(
        this IDecisionClient client, ReadOnlyMemory<byte> utf8JsonState, CancellationToken cancellationToken)
        where T : IQuestionSet<T>
    {
        ArgumentNullException.ThrowIfNull(client);
        TypedEvaluation.EnsureStateJson(utf8JsonState.Span, nameof(utf8JsonState));
        return Typed<T>(client, DecisionContent.FromUtf8Json(utf8JsonState.Span, nameof(utf8JsonState)), cancellationToken);
    }

    /// <summary>
    /// Asks <typeparamref name="T"/>'s questions about a typed state, the one its
    /// <c>[Questions(State = typeof(TState))]</c> names, and returns its typed answers.
    /// </summary>
    /// <typeparam name="T">A <c>[Questions]</c> question set linked to <typeparamref name="TState"/>.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="client">The client to ask.</param>
    /// <param name="state">The state; it must serialize to a JSON string, object or array.</param>
    /// <param name="stateTypeInfo">The source-generated metadata <paramref name="state"/> is serialized with.</param>
    /// <returns>The typed answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>
    /// Calls <see cref="EvaluateAsync{T, TState}(IDecisionClient, TState, JsonTypeInfo{TState}, CancellationToken)"/> without cancellation.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="client"/>, <paramref name="state"/> or <paramref name="stateTypeInfo"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> does not serialize to a string, object or array.</exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <typeparamref name="T"/>'s.</exception>
    public static ValueTask<Result<T, DecisionError>> EvaluateAsync<T, TState>(this IDecisionClient client, TState state, JsonTypeInfo<TState> stateTypeInfo)
        where T : IQuestionSet<T, TState>
        => EvaluateAsync<T, TState>(client, state, stateTypeInfo, CancellationToken.None);

    /// <summary>
    /// Asks <typeparamref name="T"/>'s questions about a typed state, the one its
    /// <c>[Questions(State = typeof(TState))]</c> names, and returns its typed answers.
    /// </summary>
    /// <typeparam name="T">A <c>[Questions]</c> question set linked to <typeparamref name="TState"/>.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="client">The client to ask.</param>
    /// <param name="state">The state; it must serialize to a JSON string, object or array.</param>
    /// <param name="stateTypeInfo">
    /// The source-generated metadata <paramref name="state"/> is serialized with, from your <c>JsonSerializerContext</c>.
    /// </param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The typed answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="client"/>, <paramref name="state"/> or <paramref name="stateTypeInfo"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> does not serialize to a string, object or array.</exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <typeparamref name="T"/>'s.</exception>
    public static ValueTask<Result<T, DecisionError>> EvaluateAsync<T, TState>(
        this IDecisionClient client, TState state, JsonTypeInfo<TState> stateTypeInfo, CancellationToken cancellationToken)
        where T : IQuestionSet<T, TState>
    {
        ArgumentNullException.ThrowIfNull(client);
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        ArgumentNullException.ThrowIfNull(stateTypeInfo);
        return Typed<T>(client, DecisionContent.FromValue(state, stateTypeInfo, nameof(state)), cancellationToken);
    }

    /// <summary>Asks a built question set's questions about a state and returns its answers.</summary>
    /// <param name="client">The client to ask.</param>
    /// <param name="questionSet">The set, from <see cref="QuestionSetBuilder.Build"/>.</param>
    /// <param name="state">The state: text, or a JSON object or array.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync(IDecisionClient, QuestionSet, DecisionContent, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="questionSet"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> is uninitialized.</exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <paramref name="questionSet"/>.</exception>
    public static ValueTask<Result<Answers, DecisionError>> EvaluateAsync(this IDecisionClient client, QuestionSet questionSet, DecisionContent state)
        => EvaluateAsync(client, questionSet, state, CancellationToken.None);

    /// <summary>Asks a built question set's questions about a state and returns its answers.</summary>
    /// <param name="client">The client to ask.</param>
    /// <param name="questionSet">The set, from <see cref="QuestionSetBuilder.Build"/>.</param>
    /// <param name="state">The state: text, or a JSON object or array.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="questionSet"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> is uninitialized.</exception>
    /// <exception cref="InvalidOperationException">The client answered a different question set than <paramref name="questionSet"/>.</exception>
    public static ValueTask<Result<Answers, DecisionError>> EvaluateAsync(
        this IDecisionClient client, QuestionSet questionSet, DecisionContent state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(questionSet);
        DecisionContent.EnsureInitialized(state, nameof(state));
        var call = client.EvaluateAsync(new DecisionRequest(questionSet.Definition, state), cancellationToken);
        return call.IsCompletedSuccessfully
            ? new(BuiltSet(questionSet, call.Result))
            : BuiltSetAsync(questionSet, call);
    }

    private static ValueTask<Result<T, DecisionError>> Typed<T>(IDecisionClient client, DecisionContent state, CancellationToken ct)
        where T : IQuestionSet<T>
    {
        var call = client.EvaluateAsync(new DecisionRequest(T.Definition, state), ct);
        return call.IsCompletedSuccessfully ? new(Create<T>(call.Result)) : TypedAsync<T>(call);
    }

    private static async ValueTask<Result<T, DecisionError>> TypedAsync<T>(ValueTask<Result<DecisionResponse, DecisionError>> call)
        where T : IQuestionSet<T>
        => Create<T>(await call.ConfigureAwait(false));

    private static Result<T, DecisionError> Create<T>(Result<DecisionResponse, DecisionError> result)
        where T : IQuestionSet<T>
    {
        if (result.IsFailure)
        {
            return Result<T, DecisionError>.Failure(result.Error);
        }

        var response = result.Value;
        EnsureDefinition(response, T.Definition);
        return Result<T, DecisionError>.Success(T.Create(response.ToAnswerSlots()));
    }

    private static async ValueTask<Result<Answers, DecisionError>> BuiltSetAsync(QuestionSet questionSet, ValueTask<Result<DecisionResponse, DecisionError>> call)
        => BuiltSet(questionSet, await call.ConfigureAwait(false));

    private static Result<Answers, DecisionError> BuiltSet(QuestionSet questionSet, Result<DecisionResponse, DecisionError> result)
    {
        if (result.IsFailure)
        {
            return Result<Answers, DecisionError>.Failure(result.Error);
        }

        var response = result.Value;
        EnsureDefinition(response, questionSet.Definition);
        return Result<Answers, DecisionError>.Success(new Answers(questionSet, response.Probabilities, response.Slots));
    }

    // A pipeline returns the response for the request's own definition instance; a fake that answers another set is a
    // bug in the fake. QuestionSetDefinition has reference identity, so this is a reference check.
    private static void EnsureDefinition(DecisionResponse response, QuestionSetDefinition expected)
    {
        if (!ReferenceEquals(response.Definition, expected))
        {
            throw new InvalidOperationException("The client returned answers for a different question set than the one it was asked.");
        }
    }
}
