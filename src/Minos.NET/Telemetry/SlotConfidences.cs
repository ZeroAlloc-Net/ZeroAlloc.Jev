using System.Runtime.InteropServices;

namespace Minos.Telemetry;

/// <summary>
/// The confidence of each Choice and Score answer in a <see cref="DecisionResponse"/>, none for a Noul. A struct enumerable,
/// so the telemetry proxy's per-element histogram iterates it without allocating.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct SlotConfidences(DecisionResponse response)
{
    public Enumerator GetEnumerator() => new(response);

    /// <summary>A plain struct, not a <c>ref struct</c>, which ZeroAlloc.Telemetry's per-element histogram needs.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct Enumerator(DecisionResponse response)
    {
        private int _index = -1;

        public readonly double Current => response.Slots[_index].Confidence;

        public bool MoveNext()
        {
            var questions = response.Definition.QuestionArray;
            while (++_index < questions.Length)
            {
                if (questions[_index].Kind != QuestionKind.Noul)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
