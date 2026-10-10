using System.Runtime.InteropServices;

namespace Minos;

/// <summary>One question's answer in provider-neutral form.</summary>
/// <remarks>
/// <see cref="Value"/> is the Noul value or the Score's expected level, and 0 for a Choice. <see cref="ChosenIndex"/> is the
/// chosen option's or level's position, and -1 for a Noul. <see cref="Probabilities"/> has one entry per option or level, in
/// definition order, and is empty for a Noul.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct QuestionAnswer
{
    private readonly string? _key;
    private readonly double[]? _probabilities;
    private readonly int _offset;
    private readonly int _count;

    internal QuestionAnswer(string key, QuestionKind kind, int chosenIndex, double value, double confidence, double[]? probabilities, int offset, int count)
    {
        _key = key;
        Kind = kind;
        ChosenIndex = chosenIndex;
        Value = value;
        Confidence = confidence;
        _probabilities = probabilities;
        _offset = offset;
        _count = count;
    }

    /// <summary>Gets the question's key; empty for an answer made by a factory and not yet part of a response.</summary>
    public string Key => _key ?? string.Empty;

    // A default answer has no key; the factories and responses always set one, empty at most.
    internal bool IsDefault => _key is null;

    /// <summary>Gets the question's kind.</summary>
    public QuestionKind Kind { get; }

    /// <summary>Gets the chosen option's or level's position, or -1 for a Noul.</summary>
    public int ChosenIndex { get; }

    /// <summary>Gets the Noul value or the Score's expected level; 0 for a Choice.</summary>
    public double Value { get; }

    /// <summary>Gets the confidence of a Choice or Score; 0 for a Noul.</summary>
    public double Confidence { get; }

    /// <summary>Gets one probability per option or level; empty for a Noul.</summary>
    public ReadOnlySpan<double> Probabilities => _probabilities is null ? default : _probabilities.AsSpan(_offset, _count);

    /// <summary>Creates a Noul answer.</summary>
    /// <param name="value">The answer, from 0 to 1.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is outside 0 to 1, or not a number.</exception>
    public static QuestionAnswer Noul(double value)
    {
        EnsureUnit(value, nameof(value));
        return new(string.Empty, QuestionKind.Noul, -1, value, 0, null, 0, 0);
    }

    /// <summary>Creates a Choice answer.</summary>
    /// <param name="chosenIndex">The chosen option's position.</param>
    /// <param name="confidence">The confidence, from 0 to 1.</param>
    /// <param name="probabilities">One probability per option, in definition order; copied.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="probabilities"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chosenIndex"/> is negative, or a value is outside 0 to 1.</exception>
    public static QuestionAnswer Choice(int chosenIndex, double confidence, double[] probabilities)
        => Ranked(QuestionKind.Choice, chosenIndex, 0, confidence, probabilities);

    /// <summary>Creates a Score answer.</summary>
    /// <param name="level">The chosen level's position.</param>
    /// <param name="value">The expected level.</param>
    /// <param name="confidence">The confidence, from 0 to 1.</param>
    /// <param name="probabilities">One probability per level, in definition order; copied.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="probabilities"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is negative, <paramref name="value"/> is not finite, or a value is outside 0 to 1.</exception>
    public static QuestionAnswer Score(int level, double value, double confidence, double[] probabilities)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The expected level must be a finite number.");
        }

        return Ranked(QuestionKind.Score, level, value, confidence, probabilities);
    }

    private static QuestionAnswer Ranked(QuestionKind kind, int index, double value, double confidence, double[] probabilities)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index, kind == QuestionKind.Score ? "level" : "chosenIndex");
        EnsureUnit(confidence, nameof(confidence));
        ArgumentNullException.ThrowIfNull(probabilities);
        foreach (var probability in probabilities)
        {
            EnsureUnit(probability, nameof(probabilities));
        }

        return new(string.Empty, kind, index, value, confidence, [.. probabilities], 0, probabilities.Length);
    }

    private static void EnsureUnit(double value, string paramName)
    {
        if (!(value is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be from 0 to 1.");
        }
    }
}
