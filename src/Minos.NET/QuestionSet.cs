namespace Minos;

/// <summary>
/// A question set built at run time with <see cref="QuestionSetBuilder"/>: the counterpart of a <c>[Questions]</c>
/// type, for questions, options or keys known only at run time. Evaluate it with
/// <see cref="DecisionClientExtensions.EvaluateAsync(IDecisionClient, QuestionSet, DecisionContent)"/>.
/// </summary>
/// <remarks>Immutable and safe to share across threads: build it once and reuse it.</remarks>
public sealed class QuestionSet
{
    internal QuestionSet(
        object identity, QuestionSetDefinition definition, QuestionFailure[] warnings, object?[] optionSets)
    {
        Identity = identity;
        Definition = definition;
        Warnings = warnings.Length == 0 ? [] : new System.Collections.ObjectModel.ReadOnlyCollection<QuestionFailure>(warnings);
        OptionSets = optionSets;
    }

    /// <summary>Gets the set's questions, independent of any provider's wire format.</summary>
    public QuestionSetDefinition Definition { get; }

    /// <summary>Gets the advice the set's questions break: MIN003 and MIN005, which do not stop the build.</summary>
    public IReadOnlyList<QuestionFailure> Warnings { get; }

    /// <summary>Gets the identity token of the builder that created this set; every set that builder builds shares it.</summary>
    internal object Identity { get; }

    /// <summary>
    /// Gets each question's option set, in wire order, for the typed handles: an <see cref="EnumOptionSet{T}"/> or a
    /// <see cref="KeyedOptionSet"/>, or <see langword="null"/> for a Noul. Everything else comes from <see cref="Definition"/>.
    /// </summary>
    internal object?[] OptionSets { get; }

    /// <summary>Starts a new set.</summary>
    /// <returns>An empty builder.</returns>
    public static QuestionSetBuilder CreateBuilder() => new();
}
