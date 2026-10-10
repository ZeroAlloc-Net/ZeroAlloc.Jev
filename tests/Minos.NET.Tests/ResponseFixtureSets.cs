namespace Minos.Tests;

/// <summary>Pairs each response fixture with the run-time definition it answers, plus bodies that must fail the same way on both paths.</summary>
internal static class ResponseFixtureSets
{
    public static IEnumerable<(QuestionSetDefinition Definition, byte[] Response)> Responses()
    {
        var urgency = QuestionSets.UrgencyDefinition();
        yield return (urgency, Bytes("response-noul.json"));
        yield return (urgency, Bytes("response-missing-usage.json"));
        yield return (urgency, Bytes("response-openrouter.json"));
        yield return (urgency, Bytes("response-type-last.json"));

        yield return (
            new QuestionSetDefinition(QuestionDefinition.Choice(
                "department",
                DecisionContent.FromString("Which team should handle this?"),
                new OptionDefinition("billing", Criterion.Text("Payments")),
                new OptionDefinition("technical", Criterion.Text("Bugs")),
                new OptionDefinition("sales", null))),
            Bytes("response-choice.json"));

        yield return (
            new QuestionSetDefinition(QuestionDefinition.Score(
                "frustration",
                DecisionContent.FromString("How frustrated is the customer?"),
                "Calm",
                "Frustrated",
                "Very angry")),
            Bytes("response-score.json"));

        yield return (new QuestionSetDefinition(QuestionDefinition.Noul("q", DecisionContent.FromString("Q?"))), Bytes("response-unknown-type.json"));

        // Bodies that fail: a missing question, truncated JSON, and content after the root object.
        yield return (urgency, Bytes("response-choice.json"));
        yield return (urgency, "{\"answers\":{\"is_urgent\":{\"type\":\"noul\",\"noul\":0.5}}"u8.ToArray());
        yield return (urgency, "{\"answers\":{\"is_urgent\":{\"type\":\"noul\",\"noul\":0.5}}} x"u8.ToArray());
    }

    private static byte[] Bytes(string name) => System.Text.Encoding.UTF8.GetBytes(Fixture.Text(name));
}
