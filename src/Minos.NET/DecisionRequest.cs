namespace Minos;

/// <summary>A provider-neutral evaluation: the questions, the state they are asked about and, optionally, the model.</summary>
/// <remarks>
/// A pipeline stage that sends the request again, such as the retry stage, sends a copy made with <c>with</c>, so the
/// instance a caller passed is never changed.
/// </remarks>
public sealed record DecisionRequest
{
    private readonly int _retryAttempt;

    /// <summary>Initializes a new instance of the <see cref="DecisionRequest"/> record.</summary>
    /// <param name="Definition">The questions to ask.</param>
    /// <param name="State">The state the questions are about.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="State"/> is the default, uninitialized value.</exception>
    public DecisionRequest(QuestionSetDefinition Definition, DecisionContent State)
    {
        ArgumentNullException.ThrowIfNull(Definition);
        DecisionContent.EnsureInitialized(State, nameof(State));
        this.Definition = Definition;
        this.State = State;
    }

    /// <summary>Gets the questions to ask.</summary>
    public QuestionSetDefinition Definition { get; }

    /// <summary>Gets the state the questions are about.</summary>
    public DecisionContent State { get; }

    /// <summary>Gets the model to ask, or <see langword="null"/> for the transport's configured model.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the attempt number: 0 for the first attempt, 1 for the first retry, and so on.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int RetryAttempt
    {
        get => _retryAttempt;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _retryAttempt = value;
        }
    }
}
