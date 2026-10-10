namespace Minos.Tests;

public sealed class DecisionResponseTests
{
    private static readonly QuestionSetDefinition Triage = QuestionSets.TriageDefinition();

    private static DecisionResponse Sample()
        => new(
            Triage,
            [
                QuestionAnswer.Noul(0.9),
                QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]),
                QuestionAnswer.Score(2, 1.7, 0.6, [0.1, 0.1, 0.8]),
            ],
            model: "jev-1",
            usage: new DecisionUsage { InputTokens = 10, OutputTokens = 3 });

    [Fact]
    public void Answers_carry_keys_kinds_and_values_in_definition_order()
    {
        var answers = Sample().Answers;

        Assert.Equal(3, answers.Count);
        Assert.Equal("is_urgent", answers[0].Key);
        Assert.Equal(QuestionKind.Noul, answers[0].Kind);
        Assert.Equal(0.9, answers[0].Value);
        Assert.Equal(-1, answers[0].ChosenIndex);
        Assert.True(answers[0].Probabilities.IsEmpty);
        Assert.Equal("department", answers[1].Key);
        Assert.Equal(1, answers[1].ChosenIndex);
        Assert.Equal(0.8, answers[1].Confidence);
        Assert.Equal([0.1, 0.8, 0.1], answers[1].Probabilities.ToArray());
        Assert.Equal("severity", answers[2].Key);
        Assert.Equal(2, answers[2].ChosenIndex);
        Assert.Equal(1.7, answers[2].Value);
    }

    [Fact]
    public void Enumerator_visits_every_answer_in_order()
    {
        var keys = new List<string>();
        foreach (var answer in Sample().Answers)
        {
            keys.Add(answer.Key);
        }

        Assert.Equal(["is_urgent", "department", "severity"], keys);
    }

    [Fact]
    public void Envelope_fields_round_trip()
    {
        var response = Sample();

        Assert.Equal("jev-1", response.Model);
        Assert.Equal(10, response.Usage!.InputTokens);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(3, response.OutputTokens);
        Assert.Null(response.Cost);
        Assert.Null(response.Id);
        Assert.Null(response.Provider);
        Assert.Same(Triage, response.Definition);
    }

    [Fact]
    public void Confidences_skip_noul_answers()
    {
        var values = new List<double>();
        foreach (var confidence in Sample().Confidences)
        {
            values.Add(confidence);
        }

        Assert.Equal([0.8, 0.6], values);
    }

    [Fact]
    public void Wrong_answer_count_throws()
        => Assert.Throws<ArgumentException>("answers", () => new DecisionResponse(Triage, [QuestionAnswer.Noul(0.5)]));

    [Fact]
    public void Wrong_kind_throws()
        => Assert.Throws<ArgumentException>("answers", () => new DecisionResponse(
            Triage, [QuestionAnswer.Noul(0.5), QuestionAnswer.Noul(0.5), QuestionAnswer.Noul(0.5)]));

    [Fact]
    public void Probability_count_must_match_the_options()
        => Assert.Throws<ArgumentException>("answers", () => new DecisionResponse(
            Triage, [QuestionAnswer.Noul(0.5), QuestionAnswer.Choice(0, 0.5, [1.0]), QuestionAnswer.Score(0, 0, 0.5, [1, 0, 0])]));

    [Fact]
    public void Chosen_index_must_be_an_option()
        => Assert.Throws<ArgumentException>("answers", () => new DecisionResponse(
            Triage, [QuestionAnswer.Noul(0.5), QuestionAnswer.Choice(3, 0.5, [0.2, 0.3, 0.5]), QuestionAnswer.Score(0, 0, 0.5, [1, 0, 0])]));

    [Fact]
    public void Factories_reject_out_of_range_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => QuestionAnswer.Noul(1.5));
        Assert.Throws<ArgumentOutOfRangeException>("confidence", () => QuestionAnswer.Choice(0, -0.1, [1.0]));
        Assert.Throws<ArgumentNullException>("probabilities", () => QuestionAnswer.Score(0, 0, 0.5, null!));
    }

    [Fact]
    public void Answer_slots_feed_a_built_answer_reader()
    {
        var slots = Sample().ToAnswerSlots();

        Assert.Equal(3, slots.Count);
        Assert.Equal(0.9, slots.Noul(0).Probability);
    }
}
