namespace Minos.Tests;

[Questions]
public partial record UrgencyCheck
{
    [Noul("Does this convey urgency?", WhenTrue = "Explicitly time-sensitive", WhenFalse = "No urgency expressed")]
    public partial Noul IsUrgent { get; }
}

[Questions]
public partial record MinimalUrgencyCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

public enum Department
{
    [Criteria("Payments, invoicing, refunds")]
    Billing,

    [Criteria("Bugs, outages, integrations")]
    Technical,

    [Criteria("Pricing, upgrades, new accounts")]
    Sales,

    Other,
}

[Questions]
public partial record DepartmentRouting
{
    [Choice("Which team should handle this?")]
    public partial Choice<Department> Department { get; }
}

public enum StructuredDepartment
{
    [Criteria("Payments, invoicing, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing,

    [Criteria("Bugs, outages, integrations", Examples = new[] { "The API returns 500" })]
    Technical,

    [Criteria("Pricing, upgrades, new accounts", Examples = [], NotFor = [])]
    Sales,

    Other,
}

public enum StructuredSeverity
{
    [Level("Cosmetic", NotFor = ["Data loss"])]
    Low,

    [Level("Blocks work")]
    High,
}

[Questions]
public partial record StructuredRouting
{
    [Choice("Which team should handle this?")]
    public partial Choice<StructuredDepartment> Department { get; }

    [Score("How severe is this?")]
    public partial Score<StructuredSeverity> Severity { get; }
}

public enum Frustration
{
    [Level("Calm")]
    Calm,

    [Level("Frustrated")]
    Frustrated,

    [Level("Very angry")]
    VeryAngry,
}

[Questions]
public partial record FrustrationCheck
{
    [Score("How frustrated is the customer?")]
    public partial Score<Frustration> Frustration { get; }
}

[Questions]
public partial class TicketTriage
{
    [Noul("Does `message` ask for a credential?")]
    public partial Noul RequestsCredentials { get; }

    [Choice("Which team should handle `message`?")]
    public partial Choice<Department> Team { get; }

    [Score("How frustrated is the customer?")]
    public partial Score<Frustration> Mood { get; }
}

public enum Priority
{
    [Criteria("Can wait")]
    Low = 10,

    [Criteria("Needs attention", Key = "urgent")]
    High = 20,

    // Deliberate alias: test data for the generator's alias handling
    // (global-constraints.md: "enum members that repeat an earlier member's value (aliases) are not
    // separate options"), not an accidental duplicate.
#pragma warning disable CA1069
    Legacy = 10,
#pragma warning restore CA1069
}

[Questions]
public partial record EdgeCases
{
    public const string TrickyInstructions = "Quote \" backslash \\ newline \n control \u0001 accent é emoji 😀 backtick `message`";

    [Noul(TrickyInstructions, Key = "tricky")]
    public partial Noul Escaping { get; }

    [Choice("Priority?")]
    public partial Choice<Priority> Priority { get; }
}

// JSON at the run-time builder's depth limit of 60. Embedded in a request, a criterion description reaches the 64
// levels System.Text.Json reads by default, and the instructions one level less.
public static class DeepJson
{
    public const string Text = "[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[\"Is it deep?\"]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]";
}

public enum DeepOption
{
    Shallow,
    Deep,
}

/// <summary>Built sets the telemetry tests share.</summary>
internal static class BuiltSets
{
    /// <summary>One Noul, <c>is_urgent</c>, which <c>response-noul.json</c> answers.</summary>
    public static QuestionSet UrgencyOnly()
        => QuestionSet.CreateBuilder().Noul("is_urgent", "Does this convey urgency?", out _).Build().Value;
}

/// <summary>Run-time definitions the neutral request and response tests share.</summary>
internal static class QuestionSets
{
    /// <summary>One Noul, <c>is_urgent</c>.</summary>
    public static QuestionSetDefinition UrgencyDefinition()
        => new(QuestionDefinition.Noul("is_urgent", DecisionContent.FromString("Is this urgent?")));

    /// <summary>A Noul <c>is_urgent</c>, a Choice <c>department</c> of three options and a Score <c>severity</c> of three levels.</summary>
    public static QuestionSetDefinition TriageDefinition()
        => new(
            QuestionDefinition.Noul("is_urgent", DecisionContent.FromString("Is this urgent?")),
            QuestionDefinition.Choice(
                "department",
                DecisionContent.FromString("Which team should handle this?"),
                new OptionDefinition("billing", Criterion.Text("Payments")),
                new OptionDefinition("tech", Criterion.Text("Bugs")),
                new OptionDefinition("other", null)),
            QuestionDefinition.Score(
                "severity",
                DecisionContent.FromString("How severe is this?"),
                "Cosmetic",
                "Disruptive",
                "Blocking"));
}
