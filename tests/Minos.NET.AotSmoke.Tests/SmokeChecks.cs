using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Minos.AotSmoke.Tests;

/// <summary>A smoke check: a method with <c>[Covers]</c> attributes, the entry points it declares, and the ones it calls.</summary>
internal sealed record SmokeCheck(IMethodSymbol Method, List<string> Declared, HashSet<string> Called)
{
    public string Name => Method.ContainingType.Name + "." + Method.Name;
}

/// <summary>
/// Reads the smoke checks out of <see cref="SampleCompilation.Smoke"/>, and resolves what each one calls through the
/// semantic model, so a declaration is honest only if the code really binds to that entry point.
/// </summary>
/// <remarks>
/// A check calls every method and constructor its body binds to: invocations, object creations, method groups, the
/// <c>GetEnumerator</c>, <c>MoveNext</c> and <c>Dispose</c> a <c>foreach</c> runs, and, transitively, whatever the
/// smoke app's own methods, constructors, accessors and lambdas it reaches call in turn, generated code included. A
/// constructor of a smoke type also calls its base constructor, and reaches the type's protected overrides of members
/// declared outside the smoke app, since only the code that declares them can call them, as a
/// <c>DelegatingDecisionClient</c>'s <c>Dispose()</c> calls a stage's <c>Dispose(bool)</c>. A call is matched by the signature of the member it
/// binds to, so a call on a <c>DecisionClient</c> covers <c>DecisionClient</c>'s member, and a call through an
/// <c>IDecisionClient</c> covers the interface's. A default interface method counts only when its own body runs: the
/// receiver's static type, or the type its local was created as, must not override it, so a default called on a
/// <c>DecisionClient</c> or on a client resolved from a container does not count, nor does one on a local that is assigned
/// again after its declaration. A call inside <c>nameof</c> runs nothing and does not count.
/// </remarks>
internal static class SmokeChecks
{
    private const string CoversAttribute = "CoversAttribute";

    /// <summary>Every check in the smoke app.</summary>
    public static List<SmokeCheck> All { get; } = Find();

    /// <summary>The methods <c>Program.Main</c> calls directly: the checks that run.</summary>
    public static HashSet<IMethodSymbol> RunByMain { get; } = FindRunByMain();

    private static List<SmokeCheck> Find()
    {
        var compilation = SampleCompilation.Smoke.Compilation;
        var checks = new List<SmokeCheck>();
        foreach (var method in SourceMethods(compilation.Assembly.GlobalNamespace))
        {
            var declared = Declared(method);
            if (declared.Count > 0)
            {
                checks.Add(new SmokeCheck(method, declared, Called(compilation, method)));
            }
        }

        return checks;
    }

    /// <summary>For each method <c>Main</c> runs, the entry points it calls, so a failure can say where an
    /// undeclared entry point is already called.</summary>
    public static Dictionary<string, HashSet<string>> CalledByMainsCallees { get; } = FindCalledByMainsCallees();

    private static Dictionary<string, HashSet<string>> FindCalledByMainsCallees()
    {
        var compilation = SampleCompilation.Smoke.Compilation;
        return RunByMain
            .Where(IsSource)
            .ToDictionary(method => method.ContainingType.Name + "." + method.Name, method => Called(compilation, method), StringComparer.Ordinal);
    }

    private static List<string> Declared(IMethodSymbol method)
        => [.. method.GetAttributes()
            .Where(attribute => string.Equals(attribute.AttributeClass?.Name, CoversAttribute, StringComparison.Ordinal))
            .Select(attribute => (string)attribute.ConstructorArguments[0].Value!)];

    private static HashSet<string> Called(Compilation compilation, IMethodSymbol check)
        => Reach(compilation, check, transitive: true).Select(ApiSignature.Of).ToHashSet(StringComparer.Ordinal);

    private static HashSet<IMethodSymbol> FindRunByMain()
    {
        var compilation = SampleCompilation.Smoke.Compilation;
        var main = compilation.GetEntryPoint(CancellationToken.None)
            ?? throw new InvalidOperationException("The smoke app has no entry point.");
        return Reach(compilation, main, transitive: false);
    }

    private static IEnumerable<IMethodSymbol> SourceMethods(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                foreach (var method in SourceMethods(nested))
                {
                    yield return method;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (var method in SourceMethods(type))
                {
                    yield return method;
                }
            }
        }
    }

    private static IEnumerable<IMethodSymbol> SourceMethods(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                yield return method;
            }
        }

        foreach (var nestedType in type.GetTypeMembers())
        {
            foreach (var nested in SourceMethods(nestedType))
            {
                yield return nested;
            }
        }
    }

    /// <summary>The methods and constructors <paramref name="root"/> calls; with <paramref name="transitive"/>, also
    /// the ones the smoke app's own code it calls calls in turn.</summary>
    internal static HashSet<IMethodSymbol> Reach(Compilation compilation, IMethodSymbol root, bool transitive)
    {
        var called = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { root };
        var pending = new Queue<IMethodSymbol>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var method = pending.Dequeue();
            foreach (var target in DirectCalls(compilation, method))
            {
                var definition = target.PartialImplementationPart ?? target;
                called.Add(definition);
                if (transitive && IsSource(definition) && visited.Add(definition))
                {
                    pending.Enqueue(definition);
                }
            }
        }

        return called;
    }

    private static bool IsSource(IMethodSymbol method)
        => method.OriginalDefinition.Locations.Any(location => location.IsInSource)
            || (method.MethodKind == MethodKind.Constructor && method.ContainingType.Locations.Any(location => location.IsInSource));

    private static IEnumerable<IMethodSymbol> DirectCalls(Compilation compilation, IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        var references = method.DeclaringSyntaxReferences;
        if (method.MethodKind == MethodKind.Constructor && !HasExplicitInitializer(references))
        {
            // An implicit or initializer-less constructor runs its base type's parameterless constructor.
            var baseConstructor = method.ContainingType.BaseType?.InstanceConstructors
                .FirstOrDefault(constructor => constructor.Parameters.Length == 0);
            if (baseConstructor is not null)
            {
                yield return baseConstructor;
            }
        }

        if (method.MethodKind == MethodKind.Constructor)
        {
            // Creating a smoke type reaches its protected overrides of members declared outside the smoke app, since
            // only that code can call them, as DelegatingDecisionClient.Dispose() runs a stage's Dispose(bool). A public
            // override is not reached this way: a check can call it, or the virtual it overrides, itself. Whether the
            // outside code calls a protected override in a given check is left to the check's runtime assertions.
            foreach (var member in method.ContainingType.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.IsOverride
                    && member.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal
                    && member.OverriddenMethod is { } overridden
                    && !IsSource(overridden))
                {
                    yield return member;
                }
            }
        }

        foreach (var reference in references)
        {
            var syntax = reference.GetSyntax();
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var node in NodesOf(syntax))
            {
                if (IsInsideNameof(node))
                {
                    continue;
                }

                if (node is CommonForEachStatementSyntax forEach)
                {
                    var info = model.GetForEachStatementInfo(forEach);
                    foreach (var loopMethod in new[] { info.GetEnumeratorMethod, info.MoveNextMethod, info.DisposeMethod })
                    {
                        if (loopMethod is not null)
                        {
                            yield return loopMethod;
                        }
                    }

                    continue;
                }

                switch (model.GetSymbolInfo(node).Symbol)
                {
                    case IMethodSymbol target when IsDefaultInterfaceMethod(target):
                        // Only the invocation itself is judged, by the receiver it dispatches on; the member access
                        // inside it, and a method group, never count for a default interface method.
                        if (node is InvocationExpressionSyntax invocation && RunsDefaultBody(model, invocation, target))
                        {
                            yield return target;
                        }

                        break;
                    case IMethodSymbol target:
                        yield return target;
                        break;
                    case IPropertySymbol property:
                        // A smoke type's accessors are followed like its methods; they are not entry points themselves.
                        foreach (var accessor in new[] { property.GetMethod, property.SetMethod })
                        {
                            if (accessor is not null && IsSource(accessor))
                            {
                                yield return accessor;
                            }
                        }

                        break;
                }
            }
        }
    }

    // A default interface method: an interface instance method with a body. Abstract members have no body to run, and
    // a static one has no receiver to dispatch on.
    private static bool IsDefaultInterfaceMethod(IMethodSymbol method)
        => method.ContainingType.TypeKind == TypeKind.Interface
            && method.MethodKind == MethodKind.Ordinary
            && !method.IsAbstract
            && !method.IsStatic;

    // Whether calling target on this invocation's receiver runs target's own body: the receiver's runtime type, as far
    // as the code shows it, must not override the default. An unknown runtime type, such as a client resolved from a
    // container, does not count, since it may well override it.
    private static bool RunsDefaultBody(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol target)
    {
        if (model.GetOperation(invocation) is not IInvocationOperation { Instance: { } receiver }
            || RuntimeType(model, receiver, depth: 0) is not { } runtimeType)
        {
            return false;
        }

        var definition = target.OriginalDefinition;
        var implementation = runtimeType.FindImplementationForInterfaceMember(definition);
        return SymbolEqualityComparer.Default.Equals(implementation?.OriginalDefinition, definition);
    }

    // The runtime type of a receiver as far as the code shows it: its static type when that is a class or struct,
    // else the type its local was created as, provided nothing assigns the local again. Null when the code does not
    // show it.
    private static ITypeSymbol? RuntimeType(SemanticModel model, IOperation operation, int depth)
    {
        while (operation is IConversionOperation or IParenthesizedOperation)
        {
            operation = operation is IConversionOperation conversion ? conversion.Operand : ((IParenthesizedOperation)operation).Operand;
        }

        if (operation.Type is { TypeKind: not TypeKind.Interface and not TypeKind.TypeParameter } known)
        {
            return known;
        }

        if (depth < 8
            && operation is ILocalReferenceOperation local
            && local.Local.DeclaringSyntaxReferences is [var declaration]
            && declaration.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
            && !IsAssignedAgain(model, local.Local, declarator)
            && model.GetOperation(value) is { } initializer)
        {
            return RuntimeType(model, initializer, depth + 1);
        }

        return null;
    }

    // Whether anything besides its declarator's initializer writes the local: an assignment, an out or ref argument,
    // a deconstruction or an increment, in a nested block or a lambda included. A local is in scope only in the
    // statements that follow its declaration in the same list, so those, and the initializers of its own declaration,
    // are all a write can be in. A declaration outside a statement list, such as a for loop's, counts as written.
    private static bool IsAssignedAgain(SemanticModel model, ILocalSymbol local, VariableDeclaratorSyntax declarator)
    {
        if (declarator.Parent?.Parent is not LocalDeclarationStatementSyntax statement
            || Siblings(statement) is not { } siblings)
        {
            return true;
        }

        var regions = siblings
            .Where(sibling => sibling != statement)
            .Select(sibling => model.AnalyzeDataFlow(sibling))
            .Concat(statement.Declaration.Variables
                .Select(variable => variable.Initializer?.Value)
                .OfType<ExpressionSyntax>()
                .Select(value => model.AnalyzeDataFlow(value)));
        return regions.Any(region => region is null
            || !region.Succeeded
            || region.WrittenInside.Contains(local, SymbolEqualityComparer.Default));
    }

    private static IEnumerable<StatementSyntax>? Siblings(LocalDeclarationStatementSyntax statement)
        => statement.Parent switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            GlobalStatementSyntax { Parent: CompilationUnitSyntax unit } => unit.Members.OfType<GlobalStatementSyntax>().Select(global => global.Statement),
            _ => null,
        };

    private static bool HasExplicitInitializer(IEnumerable<SyntaxReference> references)
        => references.Select(reference => reference.GetSyntax()).Any(syntax => syntax switch
        {
            ConstructorDeclarationSyntax constructor => constructor.Initializer is not null,
            TypeDeclarationSyntax type => type.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().Any() == true,
            _ => false,
        });

    private static IEnumerable<SyntaxNode> NodesOf(SyntaxNode syntax)
        => BodyOf(syntax).SelectMany(body => body.DescendantNodesAndSelf());

    // The code a method runs: its body and initializer, not its attributes or, for a primary constructor, the rest
    // of its type.
    private static IEnumerable<SyntaxNode> BodyOf(SyntaxNode syntax)
        => syntax switch
        {
            ConstructorDeclarationSyntax constructor => new SyntaxNode?[] { constructor.Initializer, constructor.Body, constructor.ExpressionBody }.OfType<SyntaxNode>(),
            BaseMethodDeclarationSyntax method => new SyntaxNode?[] { method.Body, method.ExpressionBody }.OfType<SyntaxNode>(),
            LocalFunctionStatementSyntax local => new SyntaxNode?[] { local.Body, local.ExpressionBody }.OfType<SyntaxNode>(),
            AccessorDeclarationSyntax accessor => new SyntaxNode?[] { accessor.Body, accessor.ExpressionBody }.OfType<SyntaxNode>(),
            ArrowExpressionClauseSyntax arrow => [arrow],
            TypeDeclarationSyntax type => type.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>() ?? Enumerable.Empty<SyntaxNode>(),
            _ => [],
        };

    private static bool IsInsideNameof(SyntaxNode node)
        => node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().Any(invocation =>
            invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
}
