using System.Globalization;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// The page cites gates and budgets from the AOT smoke application. These tests keep every one of them honest.
public sealed partial class NativeAotTests
{
    private const string Page = "native-aot.md";

    private static string Source(params string[] path) => File.ReadAllText(Path.Combine([PublishedPages.Root, .. path]));

    private static string AllocationChecks() => Source("samples", "Minos.NET.AotSmoke", "AllocationChecks.cs");

    // The literal budgets inside one gate's method: "budgetBytes: 192" and "const long BudgetBytes = 5056".
    private static int[] Budgets(string checks, string gate)
    {
        var method = Regex.Match(
            checks,
            $@"public static (?:void|async Task) {gate}\(\)(?<body>.*?)(?=\n    public static |\n    private static |\z)",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
        Assert.True(method.Success, $"AllocationChecks has a gate named {gate}.");
        var budgets = new List<int>();
        foreach (Match budget in Budget().Matches(method.Groups["body"].Value))
        {
            budgets.Add(int.Parse(budget.Groups["bytes"].Value, CultureInfo.InvariantCulture));
        }

        return [.. budgets];
    }

    [Fact]
    public void TheGateTableOnThePage_IsTheSmokeAppsBudgets()
    {
        var checks = AllocationChecks();
        var rows = PageTables.Rows(Page, "The allocation budgets");

        Assert.Equal(28, rows.Length);
        Assert.Equal(rows.Length, new HashSet<string>(rows.Select(row => PageTables.Code(row[0])), StringComparer.Ordinal).Count);
        Assert.All(
            rows,
            row =>
            {
                var gate = PageTables.Code(row[0]);
                if (!int.TryParse(row[2], CultureInfo.InvariantCulture, out var budget))
                {
                    return;
                }

                Assert.True(Array.IndexOf(Budgets(checks, gate), budget) >= 0, $"{gate} has the budget {budget}.");
            });
    }

    // The page lists every gate the smoke app runs, and no other: a gate missing from the table is a budget nobody can see.
    [Fact]
    public void TheGatesOnThePage_AreExactlyTheGatesProgramRuns()
    {
        var program = Source("samples", "Minos.NET.AotSmoke", "Program.cs");
        var called = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match call in GateCall().Matches(program))
        {
            called.Add(call.Groups["gate"].Value);
        }

        var listed = new SortedSet<string>(
            PageTables.Rows(Page, "The allocation budgets").Select(row => PageTables.Code(row[0])),
            StringComparer.Ordinal);

        Assert.Equal(28, called.Count);
        Assert.Equal(called, listed);
    }

    [Fact]
    public void ZeroBudgetGates_ReallyHaveABudgetOfZero()
    {
        var checks = AllocationChecks();

        Assert.All(["AnswersGet", "PatternHelpers", "NoulEquals", "AnswerSlotAccessors", "PassThroughStage"], gate => Assert.Contains(0, Budgets(checks, gate)));
    }

    [Fact]
    public void TheAotSmokeApp_IsPublishedAsAotWithEveryWarningAnError_AndRunInCi()
    {
        var project = Source("samples", "Minos.NET.AotSmoke", "Minos.NET.AotSmoke.csproj");
        var workflow = Source(".github", "workflows", "ci.yml");

        Assert.Contains("<PublishAot>true</PublishAot>", project, StringComparison.Ordinal);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", project, StringComparison.Ordinal);
        Assert.Contains("aot-smoke:", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet publish samples/Minos.NET.AotSmoke/Minos.NET.AotSmoke.csproj -r linux-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("./aot-out/Minos.NET.AotSmoke", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void BothPackages_AreAotCompatible_AndTheDependencyInjectionPackageBindsWithAGenerator()
    {
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", Source("src", "Minos.NET", "Minos.NET.csproj"), StringComparison.Ordinal);

        var injection = Source("src", "Minos.NET.DependencyInjection", "Minos.NET.DependencyInjection.csproj");
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", injection, StringComparison.Ordinal);
        Assert.Contains("<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>", injection, StringComparison.Ordinal);
    }

    // The one reflection: the enum option set reads public fields, and says so to the trimmer.
    [Fact]
    public void TheOneReflection_IsTheEnumOptionSetReadingPublicFields_DeclaredToTheTrimmer()
    {
        var optionSet = Source("src", "Minos.NET", "EnumOptionSet.cs");

        Assert.Contains("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)", optionSet, StringComparison.Ordinal);
        Assert.Contains("GetFields", optionSet, StringComparison.Ordinal);
    }

    // The page's own claim about the answer types: they are structs, which is why reading one allocates nothing.
    [Fact]
    public void TheAnswerTypes_AreStructs()
    {
        Assert.True(typeof(Noul).IsValueType);
        Assert.True(typeof(Choice<Department>).IsValueType);
        Assert.True(typeof(Score<Mood>).IsValueType);
        Assert.True(typeof(ProbabilityMap<Department>).IsValueType);
    }

    // The budgets the page quotes in prose are performance.md's.
    [Fact]
    public void TheProseFigures_ArePerformanceMds()
    {
        var performance = Source("docs", "performance.md");
        Assert.Contains("measures 2648 B against an unchanged budget of 7296 B", performance, StringComparison.Ordinal);
        Assert.Contains("2984 B", performance, StringComparison.Ordinal);
        Assert.Contains("211 B", performance, StringComparison.Ordinal);
        Assert.Contains("4544 B", performance, StringComparison.Ordinal);
        Assert.Contains("roughly 480 B", performance, StringComparison.Ordinal);
        Assert.Contains("Phase 3.3 — DI package", performance, StringComparison.Ordinal);
        Assert.Contains("Phase 3.1 — Logging", performance, StringComparison.Ordinal);
        Assert.Contains("Phase 3.2 — Telemetry", performance, StringComparison.Ordinal);
    }

    // The same figures, on the pages that quote them: each page says what performance.md says.
    [Fact]
    public void TheProseFigures_AreOnTheQuotingPagesToo()
    {
        var aot = Source("docs", "native-aot.md");
        Assert.Contains("2984 B under Native AOT", aot, StringComparison.Ordinal);
        Assert.Contains("It measures 2648 B", aot, StringComparison.Ordinal);
        Assert.Contains("211 B, measured under the JIT", aot, StringComparison.Ordinal);

        var observability = Source("docs", "observability.md");
        Assert.Contains("about 480 B", observability, StringComparison.Ordinal);
        Assert.Contains("211 B, measured under the JIT", observability, StringComparison.Ordinal);
        Assert.Contains("a typed call pays 1560 B, which is 4544 B listening", observability, StringComparison.Ordinal);
        Assert.Contains("against 2984 B with nothing listening", observability, StringComparison.Ordinal);

        // The 1560 B is the difference of the two measured figures.
        Assert.Equal(1560, 4544 - 2984);
    }

    [GeneratedRegex(@"AllocationChecks\.(?<gate>\w+)\(\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GateCall();

    [GeneratedRegex(@"(?:budgetBytes: |BudgetBytes = )(?<bytes>\d+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Budget();
}
