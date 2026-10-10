using Minos.Telemetry;

namespace Minos;

/// <summary>A provider-neutral evaluation result: the answers in definition order, and what the provider reported about the call.</summary>
public sealed class DecisionResponse
{
    /// <summary>Initializes a new instance of the <see cref="DecisionResponse"/> class from answers, such as for a test fake.</summary>
    /// <param name="definition">The questions the answers belong to.</param>
    /// <param name="answers">One answer per question, in definition order, made with the <see cref="QuestionAnswer"/> factories.</param>
    /// <param name="model">The model that answered, if known.</param>
    /// <param name="usage">The token usage, if known.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> or <paramref name="answers"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Checked: a non-null element for every question, and per answer its kind, its probability count against the question's options, and a chosen position below the option count. The factories check the ranges of the confidence and the probabilities, the range of a Noul's <see cref="QuestionAnswer.Value"/> and that a Score's <see cref="QuestionAnswer.Value"/> is finite. Not checked: a Score's <see cref="QuestionAnswer.Value"/> against its levels, and that the probabilities sum to 1 or that the chosen position is the most probable.
    /// </remarks>
    /// <exception cref="ArgumentException">An answer is a default value, or the answers do not match the definition's questions in number, kind, option count or chosen position.</exception>
    public DecisionResponse(QuestionSetDefinition definition, IReadOnlyList<QuestionAnswer> answers, string? model = null, DecisionUsage? usage = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(answers);
        var questions = definition.QuestionArray;
        if (answers.Count != questions.Length)
        {
            throw new ArgumentException($"The definition has {questions.Length} questions, not {answers.Count} answers.", nameof(answers));
        }

        var probabilities = definition.ProbabilityCount == 0 ? [] : new double[definition.ProbabilityCount];
        var slots = new AnswerSlot[questions.Length];
        for (var i = 0; i < questions.Length; i++)
        {
            var answer = answers[i];
            if (answer.IsDefault)
            {
                throw new ArgumentException($"Answer {i} is a default value; make answers with the QuestionAnswer factories.", nameof(answers));
            }

            var question = questions[i];
            if (answer.Kind != question.Kind)
            {
                throw new ArgumentException($"Question {i} ({question.Key}) is a {question.Kind}, not a {answer.Kind}.", nameof(answers));
            }

            if (question.Kind == QuestionKind.Noul)
            {
                slots[i] = new AnswerSlot(0, answer.Value, 0, 0);
                continue;
            }

            var options = question.OptionArray.Length;
            if (answer.Probabilities.Length != options)
            {
                throw new ArgumentException($"Question {i} ({question.Key}) has {options} options, not {answer.Probabilities.Length} probabilities.", nameof(answers));
            }

            if (answer.ChosenIndex >= options)
            {
                throw new ArgumentException($"Question {i} ({question.Key}) has {options} options; {answer.ChosenIndex} is not one of them.", nameof(answers));
            }

            var offset = definition.Offsets[i];
            answer.Probabilities.CopyTo(probabilities.AsSpan(offset));
            slots[i] = new AnswerSlot(answer.ChosenIndex, answer.Value, answer.Confidence, offset);
        }

        Definition = definition;
        Slots = slots;
        Probabilities = probabilities;
        Model = model;
        Usage = usage;
        InputTokens = usage?.InputTokens;
        OutputTokens = usage?.OutputTokens;
        Cost = usage?.Cost;
    }

    internal DecisionResponse(
        QuestionSetDefinition definition,
        AnswerSlot[] slots,
        double[] probabilities,
        string? model,
        int? inputTokens,
        int? outputTokens,
        double? cost,
        string? id,
        string? provider)
    {
        Definition = definition;
        Slots = slots;
        Probabilities = probabilities;
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Cost = cost;
        Id = id;
        Provider = provider;
        Usage = inputTokens is { } input && outputTokens is { } output
            ? new DecisionUsage { InputTokens = input, OutputTokens = output, Cost = cost }
            : null;
    }

    /// <summary>Gets the questions the answers belong to.</summary>
    public QuestionSetDefinition Definition { get; }

    /// <summary>Gets the answers, one per question in definition order.</summary>
    public QuestionAnswerList Answers => new(this);

    /// <summary>Gets the model that answered, or <see langword="null"/> when the provider did not say.</summary>
    public string? Model { get; }

    /// <summary>Gets the token usage, or <see langword="null"/> when the provider did not report both token counts.</summary>
    public DecisionUsage? Usage { get; }

    /// <summary>Gets the provider's response id, if any.</summary>
    public string? Id { get; }

    /// <summary>Gets the upstream provider the response names, if any, such as OpenRouter's.</summary>
    public string? Provider { get; }

    internal AnswerSlot[] Slots { get; }

    internal double[] Probabilities { get; }

    // Read field by field, as the provider sent them, for telemetry: one count can be present without the other.
    internal int? InputTokens { get; }

    internal int? OutputTokens { get; }

    internal double? Cost { get; }

    internal SlotConfidences Confidences => new(this);

    internal AnswerSlots ToAnswerSlots() => new(Slots, Probabilities, Definition, Slots);

    internal QuestionAnswer AnswerAt(int index)
    {
        var question = Definition.QuestionArray[index];
        var slot = Slots[index];
        return question.Kind == QuestionKind.Noul
            ? new QuestionAnswer(question.Key, QuestionKind.Noul, -1, slot.Value, 0, null, 0, 0)
            : new QuestionAnswer(question.Key, question.Kind, slot.ValueIndex, question.Kind == QuestionKind.Choice ? 0 : slot.Value, slot.Confidence, Probabilities, slot.Offset, question.OptionArray.Length);
    }
}
