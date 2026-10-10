using System.Text;
using System.Text.Json;
using Minos.Protocols;
using ZeroAlloc.TestHelpers;

namespace Minos.Tests;

/// <summary>The allocation budget for the protocol reading a built set's answers into its <see cref="Answers"/>.</summary>
public sealed class BuiltSetAllocationTests
{
    private const string AnswersJson = """{"is_urgent":{"type":"noul","noul":0.95},"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12},"confidence":0.81},"effort":{"type":"score","score":1.2,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.1,"1":0.6,"2":0.3},"confidence":0.7}}""";

    [Fact]
    public void ParsingABuiltSetsAnswers_StaysWithinItsBudget()
    {
        var built = QuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _)
            .Choice<Department>("department", "Which team?", out _)
            .Score("effort", "How much effort?", out _, l => l.Level("Minutes").Level("Hours").Level("Days"))
            .Build();
        Assert.True(built.IsSuccess);
        var set = built.Value;
        var answers = Encoding.UTF8.GetBytes(AnswersJson);
        var factory = Factory(set);

        // Measured 216 B/call under the JIT on win-x64: the Answers object, its double[7] probability buffer and its
        // AnswerSlot[3]. Budget: only fixed-layout objects, so the measurement rounded up to the next multiple of 64, 256 B.
        AllocationGate.AssertBudget(
            256,
            1000,
            () =>
            {
                var reader = new Utf8JsonReader(answers);
                reader.Read();
                _ = SystemOneProtocol.Instance.ReadAnswers(ref reader, set.Definition, factory);
            },
            "ParseBuiltSet");
    }

    [Fact]
    public void ParsingAnOversizedBuiltSet_ReadsEveryAnswer_AndAllocatesOneSlotArray()
    {
        const int count = 70;
        var builder = QuestionSet.CreateBuilder();
        var handles = new NoulHandle[count];
        for (var i = 0; i < count; i++)
        {
            builder.Noul("q" + i, "Question?", out handles[i]);
        }

        var built = builder.Build();
        Assert.True(built.IsSuccess);
        var set = built.Value;
        var json = "{" + string.Join(",", Enumerable.Range(0, count).Select(i => "\"q" + i + "\":{\"type\":\"noul\",\"noul\":0." + (i % 10) + "}")) + "}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var factory = Factory(set);

        Answers Parse()
        {
            var reader = new Utf8JsonReader(bytes);
            reader.Read();
            return SystemOneProtocol.Instance.ReadAnswers(ref reader, set.Definition, factory);
        }

        var answers = Parse();
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(i % 10 / 10.0, answers.Get(handles[i]).Probability, 6);
        }

        // The Answers object, its empty probability buffer and one AnswerSlot[70] (16 B header plus 70 slots of at most 32 B).
        // A second slot array would add over 2 KB, so the bound catches it.
        Parse();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Parse();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 1, 3000);
    }

    // What the built-set path builds from the slots the protocol read: the Answers over them, created once per set.
    private static AnswerFactory<Answers> Factory(QuestionSet set)
        => answers => new Answers(set, answers.Probabilities, answers.HeapSlots ?? answers.Slots.ToArray());
}
