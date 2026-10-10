using System.Runtime.InteropServices;

namespace Minos;

/// <summary>A response's answers, one per question in definition order. A view: reading it allocates nothing.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct QuestionAnswerList
{
    private readonly DecisionResponse? _response;

    internal QuestionAnswerList(DecisionResponse response) => _response = response;

    /// <summary>Gets the number of answers.</summary>
    public int Count => _response?.Slots.Length ?? 0;

    /// <summary>Gets the answer to the question at <paramref name="index"/>.</summary>
    /// <param name="index">The question's position in the definition.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public QuestionAnswer this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _response!.AnswerAt(index);
        }
    }

    /// <summary>Returns an enumerator over the answers.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Enumerates a <see cref="QuestionAnswerList"/>.</summary>
    [StructLayout(LayoutKind.Auto)]
    public struct Enumerator
    {
        private readonly QuestionAnswerList _list;
        private int _index;

        internal Enumerator(QuestionAnswerList list)
        {
            _list = list;
            _index = -1;
        }

        /// <summary>Gets the current answer.</summary>
        public readonly QuestionAnswer Current => _list[_index];

        /// <summary>Advances to the next answer.</summary>
        /// <returns><see langword="true"/> while there is an answer.</returns>
        public bool MoveNext() => ++_index < _list.Count;
    }
}
