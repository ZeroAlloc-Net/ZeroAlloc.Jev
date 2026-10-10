---
id: diagnostics
title: Diagnostics
sidebar_position: 10
description: Every MIN analyzer rule with its severity, the two code fixes, and how to suppress a rule that does not apply.
---

# Diagnostics

A question set is code that Minos turns into a request, so mistakes in it are best found while you type, not when a call
fails. `Minos.NET` ships a set of Roslyn analyzers for this. They live inside the package, next to the
`[Questions]` generator, so there is nothing else to install. The editor shows their findings as you type, and the
build reports them too.

Every rule has an id that starts with `MIN`, a severity and a short title. The severity says how much the finding
matters:

- **Error** stops the build, because the question set cannot work.
- **Warning** is something that is probably a mistake, such as an empty description. The set still works.
- **Info** is a suggestion. It does not fail the build, and the editor shows it as a hint.

## Two groups of rules

The rules come in two groups.

- **MIN001 to MIN006 check the set against the Jev API's own rules.** MIN001 and MIN002 follow the schema of TypeSafe's
  official SDK, which needs at least one option or level. The others are advice and never block the generator. The
  limits behind MIN005 are guidance from the API sketch, not a limit of the schema.
- **MIN101 to MIN107 check what the generator can turn into code.** They are all errors, because a declaration the
  generator cannot read cannot be turned into a request.

A set with any error is invalid. The generator writes no `Definition`, no `Create` and no `IQuestionSet` for it,
so `EvaluateAsync<T>` does not compile for that type. The generator still gives each unimplemented question property a
stub that throws. That way a command-line build reports the MIN error, and not CS9248, "partial property must have an
implementation part", which would hide it.

One thing can still hide the analyzers. An error in your own declaration, such as CS0238 for `sealed` on a property that
overrides nothing, stops a command-line build before any analyzer runs. The editor runs the analyzers live and still
shows them.

## The rules

| Id | Severity | Title | It fires when |
| --- | --- | --- | --- |
| MIN001 | Error | Choice enum has no members | The enum of a `Choice<T>` has no members. The API needs at least one option. |
| MIN002 | Error | Score enum has no members | The enum of a `Score<T>` has no members. The API needs at least one level. |
| MIN003 | Warning | Empty instructions or description | Instructions, a description, or an `Examples` or `NotFor` entry is empty or only white space. A `null` text is fine, because the API accepts none. |
| MIN004 | Warning | Instructions refer to an unknown state member | A name in backticks in the instructions matches no public property or field of the `State` type, as [typed evaluation](typed-evaluation.md#referring-to-the-state-in-a-question) describes. For an array state the element type is checked. |
| MIN005 | Warning | Option or level count outside the API guidance | A Score enum has fewer than 2 or more than 10 levels, or a Choice enum has more than 255 options. |
| MIN006 | Info | Choice option has no description | A member of a Choice enum has no `[Criteria]`. It is still an option, sent with no description, and a description usually helps. |
| MIN101 | Error | Unsupported question set type | The type with `[Questions]` is not a non-generic, non-abstract, non-static, top-level partial class or record that is not file-local. |
| MIN102 | Error | Unsupported question property | A question property is not a partial, get-only instance property, or it is `virtual`, `new`, `sealed` or `override`, or it is named `Definition` or `Create`, which the generator reserves. |
| MIN103 | Error | Question attribute does not match the property type | A question property has more than one question attribute, or one that does not fit its type: `[Noul]` needs a `Noul`, `[Choice]` a `Choice<T>` and `[Score]` a `Score<T>`. |
| MIN104 | Error | Score level has no description | A member of a Score enum has no `[Level]`. The API does not accept a level without a description. |
| MIN105 | Error | Question set has no parameterless constructor | The set has no constructor that can be called without arguments, or it has `required` members and that constructor lacks `[SetsRequiredMembers]`. A constructor whose parameters all have defaults counts. |
| MIN106 | Error | Duplicate wire key | Two questions in the set use the same wire key, or two options of one Choice do, or a `Key` on a question attribute or a `[Criteria]` is empty. |
| MIN107 | Error | Invalid state type | The `State` type is not a class, struct, record or array type. |

A [set built at run time](question-sets-at-run-time.md#checking-the-set) is checked against the same limits, with the
same rule ids. That page says which rules apply there, and which of them fail `Build()`.

### Reported by the run-time builder

One rule has no analyzer, because only a [set built at run time](question-sets-at-run-time.md#checking-the-set) can
carry JSON. A declared set's instructions and descriptions are always text.

| Id | Outcome | It fires when |
| --- | --- | --- |
| MIN108 | `Build()` fails | JSON instructions, a JSON description, or a JSON yes/no meaning nests more than 60 levels deep. |

### Where a rule is reported

A rule about an enum, such as MIN001, MIN005, MIN006 and MIN104, is reported on the enum and its members, and not on
each set that uses it. The other rules are reported on the set type or on the property they are about. An enum from
another assembly has no declaration in your project, so its findings are reported on the question property that uses it.

## Code fixes

Two rules come with a code fix. Both write the missing description for you, from the member's own name.

| Rule | Fix title, for a member named `NeedsAttention` | What it adds |
| --- | --- | --- |
| MIN006 | `Add [Criteria("Needs attention")]` | A `[Criteria]` on the Choice member. |
| MIN104 | `Add [Level("Needs attention")]` | A `[Level]` on the Score member. |

The name is split into words and written as a sentence, so `NeedsAttention` becomes `"Needs attention"`, `HTTPError`
becomes `"HTTP error"` and `NEEDS_ATTENTION` becomes `"Needs attention"`. That is a starting point. Read it and write
something that tells the model what the option means, because the model reads the description.

Both fixes can fix every occurrence in a document, a project or a solution at once. The fix adds a `using` for
`Minos.NET` only when the attribute is not already in scope. There is no fix when the enum is declared in another
assembly, because there is no member in your code to put the attribute on.

## Suppressing a rule

Suppress a warning or an Info rule that does not apply to your case. Do it narrowly, and say why. A `#pragma` around
the one declaration is the narrowest. This enum has 11 levels on purpose, a 0 to 10 scale, so the MIN005 warning is
turned off for that enum and nothing else. The example is compiled with warnings treated as errors, so it only builds
because the suppression works.

<!-- snippet: Diagnostics_Suppress -->
```cs
using Minos;

// The Jev API's guidance is 2 to 10 levels for a Score, so MIN005 warns about this enum. A 0 to 10 scale has 11
// levels by definition, so the warning is suppressed here, for this enum only, and the reason is written next to it.
#pragma warning disable MIN005 // A 0 to 10 recommendation scale has 11 levels on purpose.
public enum Recommendation
{
    [Level("0: Not at all likely")] Zero,
    [Level("1")] One,
    [Level("2")] Two,
    [Level("3")] Three,
    [Level("4")] Four,
    [Level("5: Neutral")] Five,
    [Level("6")] Six,
    [Level("7")] Seven,
    [Level("8")] Eight,
    [Level("9")] Nine,
    [Level("10: Extremely likely")] Ten,
}
#pragma warning restore MIN005

[Questions]
public partial record SurveyReply
{
    [Score("How likely is this customer to recommend us to a friend?")]
    public partial Score<Recommendation> Recommend { get; }
}
```
<!-- endSnippet -->

To change a rule for a whole project, or for the files an `.editorconfig` section covers, set its severity there:

```ini
[*.cs]
dotnet_diagnostic.MIN005.severity = none
```

The same setting can go the other way. If you want every Choice option to have a description, raise MIN006 from Info to
a warning, and the build will then fail until each option has one:

```ini
[*.cs]
dotnet_diagnostic.MIN006.severity = warning
```

A project-wide `<NoWarn>MIN005</NoWarn>` in the project file works too.

An error does not go away that way. Suppressing MIN001, MIN002 or one of MIN101 to MIN107 hides the message, but the set
stays invalid. The generator does not read the diagnostics. It applies the same rules itself, so a suppressed set still
gets no `Definition`, no `Create` and no `IQuestionSet`. `EvaluateAsync<T>` does not compile for it, and the stub
properties throw. Fix the declaration instead.

## Next

- [Testing your code](testing-your-code.md): test code that uses Minos with fakes and canned replies.
- [Typed evaluation](typed-evaluation.md): declaring question sets, which these rules check.
- [Question sets at run time](question-sets-at-run-time.md): the same limits and rule ids, checked when you call
  `Build()`.
- [Performance](performance.md): what a call costs, measured.
