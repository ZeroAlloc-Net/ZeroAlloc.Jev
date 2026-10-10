using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Minos.AotSmoke.Tests;

/// <summary>
/// Creating a smoke type reaches its protected overrides of members declared outside the smoke app, since only that
/// code can call them, as <c>DelegatingDecisionClient.Dispose()</c> calls a stage's protected <c>Dispose(bool)</c>,
/// which no check can call itself. A public override is not reached by construction: a check calls it itself. These
/// tests run the coverage resolver over a small compilation whose base type, <see cref="TextWriter"/>, comes from
/// metadata, as the packages' types do for the smoke app.
/// </summary>
public sealed class OverrideReachTests
{
    private const string Source = """
        using System.IO;
        using System.Text;

        public sealed class Writer : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;

            protected override void Dispose(bool disposing)
            {
                Helpers.Released();
                base.Dispose(disposing);
            }

            public override void Flush() => Helpers.Flushed();
        }

        public class Base : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;

            public virtual void Own() => Helpers.OwnBase();
        }

        public sealed class Derived : Base
        {
            public override void Own() => Helpers.OwnDerived();
        }

        public static class Helpers
        {
            public static void Released()
            {
            }

            public static void Flushed()
            {
            }

            public static void OwnBase()
            {
            }

            public static void OwnDerived()
            {
            }
        }

        public static class Checks
        {
            public static void Creates()
            {
                using var writer = new Writer();
            }

            public static void CreatesDerived()
            {
                using var derived = new Derived();
            }

            public static void Names(Writer writer) => writer.Write('x');
        }
        """;

    private static readonly Compilation Compilation = CSharpCompilation.Create(
        "OverrideReach",
        [CSharpSyntaxTree.ParseText(Source)],
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Fact]
    public void The_sample_compiles() => Assert.Empty(Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

    [Fact]
    public void Creating_a_type_reaches_its_override_of_a_metadata_member_and_the_base_call_inside_it()
    {
        var calls = Calls("Creates");

        Assert.Contains(Member("Helpers", "Released"), calls, SymbolEqualityComparer.Default);
        Assert.Contains(
            calls,
            method => string.Equals(method.Name, "Dispose", StringComparison.Ordinal) && method.Parameters.Length == 1 && string.Equals(method.ContainingType.Name, "TextWriter", StringComparison.Ordinal));
    }

    [Fact]
    public void Creating_a_type_does_not_reach_its_public_override_of_a_metadata_member()
        => Assert.DoesNotContain(Member("Helpers", "Flushed"), Calls("Creates"), SymbolEqualityComparer.Default);

    [Fact]
    public void Creating_a_type_does_not_reach_an_override_of_a_member_declared_in_source()
        => Assert.DoesNotContain(Member("Helpers", "OwnDerived"), Calls("CreatesDerived"), SymbolEqualityComparer.Default);

    [Fact]
    public void Using_a_type_without_creating_it_does_not_reach_its_overrides()
        => Assert.DoesNotContain(Member("Helpers", "Released"), Calls("Names"), SymbolEqualityComparer.Default);

    private static HashSet<IMethodSymbol> Calls(string check)
        => SmokeChecks.Reach(Compilation, Member("Checks", check), transitive: true);

    private static IMethodSymbol Member(string type, string name)
        => Compilation.GetTypeByMetadataName(type)!.GetMembers(name).OfType<IMethodSymbol>().First();
}
