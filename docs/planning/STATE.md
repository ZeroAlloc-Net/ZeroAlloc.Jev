# Session State — 2026-10-11 (Phase 6.3 complete: the IDecisionClient pipeline)

**Date:** 2026-10-11

## Current Position
- **Roadmap:** re-planned on 2026-10-09 (`docs/superpowers/specs/2026-10-09-roadmap-design.md`).
  - The library becomes a provider-neutral decision client before 1.0.
  - **Milestone 5 (1.0 hardening):** complete on 2026-10-09. The audit passed, with one warning: no pre-push-review reports (`docs/plans/2026-10-09-milestone-5-audit.md`). There is no tag; release-please owns releases.
  - **New milestones:**
    - 6, Provider-neutral core: the name, neutral model, `IDecisionClient`, both adapters, conformance and docs;
    - 7, 1.0 release;
    - 8, Choosing a provider and a threshold;
    - 9, Hosted to local;
    - 10, Escalation.
  - Issues #120 (the name, blocking) and #115–#119 track the work.
- **Milestone 6, Phase 6.1 (rename to Minos):** complete on 2026-10-10.
  - **Merged:** #127, the rename, in the 0.6.0 release PR #128 as a breaking change. Pre-push review PASS: `docs/plans/2026-10-09-phase-6.1-pre-push-review.md`.
  - **The repository is `MarcelRoozekrans/Minos.NET`**, transferred on 2026-10-09; GitHub redirects the old URLs.
  - **After the move:**
    - #131: release-please runs on the built-in token, with no `RELEASE_PLEASE_TOKEN`. Its `check-release-pr` job dispatches `ci.yml` and the benchmark smoke run on the release branch, so the five required checks report on the release PR without a stored credential. GitHub also creates the PR's own runs for a bot-opened PR but holds them as "action_required"; they can be ignored or approved.
    - #134: a dispatched `docs.yml` run on `main` now deploys. The site is live at https://marcelroozekrans.github.io/Minos.NET/.
    - The `Main` ruleset requires `build`, `aot-smoke`, `aot-surface`, `smoke / benchmarks` and `release-tracking`, with a pull-request bypass for the admin role, as in Thalos.NET.
    - The `live-api` environment came along with both keys; Live smoke on `main` passed 13/13 (run 37988954091).
    - `tools/rename/` is deleted.
  - **Done after the move:** ZeroAlloc-Net/.website#87 merged and the `za-docs-jev` worker retired, on 2026-10-10. #128 merged: v0.6.0 is a GitHub release, and nothing was published to NuGet. Renovate runs on Minos.NET.
  - **Still open, maintainer:** remove the `sonarqubecloud` app from the repository if SonarCloud is not used on the personal account.
- **Milestone 6, Phase 6.2 (neutral question model and adapter boundary):** complete on 2026-10-10.
  - **Merged:** #139, in the 0.7.0 release PR #141 as a breaking change. Pre-push review PASS: `docs/plans/2026-10-10-phase-6.2-pre-push-review.md`. Live smoke on `main` passed 13/13 (run 38043667198).
  - **What shipped:**
    - the public `QuestionSetDefinition`, `QuestionDefinition`, `OptionDefinition` and `QuestionKind`, exposed by generated and built sets, plus the read-only `Criterion` getters;
    - generated `Create(AnswerSlots)` in place of the JSON parser;
    - the internal `IDecisionProtocol` with `SystemOneProtocol`, which writes the questions JSON once per set and caches it, writes the request envelope, and reads answers into stack-allocated slots.
  - **Removed:** `QuestionsUtf8`, `Parse`, `AnswerReader` and `IndexOfKey`. MIN102 reserves `Definition` and `Create`; an empty `Key` is MIN106.
  - **Deferred to Phase 6.3**, in its ROADMAP goal: the endpoint path, the response envelope and telemetry reading, `DecisionErrorMapper`, and the default `IDecisionClient` typed path.
  - #140 fixed the benchmark requirements: pydantic_core moves with pydantic, and pydantic updates wait for dashboard approval.
  - #141 (0.7.0) merged: v0.7.0 is a GitHub release.
- **Milestone 6, Phase 6.3 (IDecisionClient abstraction and pipeline):** complete on 2026-10-11.
  - **Merged:** #146, in the 0.8.0 release PR #148 as a breaking change. Pre-push review PASS: `docs/plans/2026-10-10-phase-6.3-pre-push-review.md`. The live smoke has not been run on `main` yet.
  - **What shipped:**
    - `IDecisionClient` is one neutral `EvaluateAsync(DecisionRequest)` returning `Result<DecisionResponse, DecisionError>`, plus `GetService` and `IDisposable`. The typed and built-set calls are `DecisionClientExtensions`.
    - The pipeline: `DelegatingDecisionClient`, `DecisionClientBuilder` with `AsBuilder`, and the retry, logging and OpenTelemetry stages. `DecisionClient` runs them in that order over an internal one-attempt `DecisionTransport`; `UseStandardPipeline = false` leaves the bare transport, and then the raw calls do not retry either.
    - The protocol owns the endpoint path, the response envelope and the error body. `AddDecisionClient` returns `DecisionClientServiceBuilder`, with `.HttpClient` and `.Name`.
    - Six new AOT allocation gates; the typed call measures 3248 B. The UTF-8 and typed-state overloads send exactly the bytes main sent; the UTF-8 state is copied and the `JsonElement` state is cloned, so a caller may reuse or dispose either as soon as the call returns.
    - Observability: neutral calls report `evaluate-set`, event 1001 logs the provider's metadata name, and typed spans gain `gen_ai.response.id` and `minos.usage.cost`.
  - **Upstream:** ZeroAlloc.Rest#422 added the `{**path}` route token (3.3.0) and #425 made it cost 0 B (3.3.1). Minos uses Rest 3.3.1 and Results 1.4.0.
  - **Filed:** #145, the PackTests hang under MSBuild node reuse; set `MSBUILDDISABLENODEREUSE=1` until it is fixed.
  - **Release-please:** a multi-commit override needs every commit after the first in `BEGIN_NESTED_COMMIT` … `END_NESTED_COMMIT`, or the breaking-change notes are mis-assigned. #146's body was fixed that way and #148 is correct.
  - **Leave #148 (0.8.0) open** until a release is wanted. #119 stays open: its escalation, fallback chain and sample are Milestone 10.
- **Old Phase 5.5 (1.0 release):** removed from Milestone 5 and folded into Phase 7.4.
  - Its reviewed pipeline work sits on the local branch `phase/5.5-release`, not pushed:
    - inspection script;
    - publish job with a 0.x guard;
    - rescue workflow;
    - api-compat;
    - guide version line.
  - Task 3 there was implemented but not reviewed.
  - The SDD ledger is `.superpowers/sdd/2026-10-09-phase-5.5-1.0-release/progress.md`, local and git-ignored.
  - At Phase 7.4, rebase or replay that branch onto the renamed code and update the package ids.

## Open Decisions
- **Name: decided 2026-10-09: Minos.** The repository moves to `MarcelRoozekrans/Minos.NET` and leaves the ZeroAlloc org. Packages are `Minos.NET[.X]`, the namespace is `Minos`, analyzer IDs are `MIN`. See the Phase 6.1 spec. The repositioning as a provider-neutral decision client follows OpenAI's Decisions API and Cloudflare's Clef launching. The proposal is recorded verbatim in `docs/plans/2026-10-09-provider-neutral-direction.md`, items 1 to 6.
  - **Item 1, blocking:** rename packages, namespaces, `[JevQuestions]`, `IJevClient`, `JevError`, the JEV analyzer IDs and the docs site before the first NuGet publish.
  - Nothing is published under `ZeroAlloc.Jev` yet; both ids were still free on nuget.org on 2026-10-09.
- **Issues:** items 1–6 are tracked in #120 (new) and #115–#119, updated on 2026-10-09 to the revised text; #29 and #28 re-pointed to Phase 7.4.

## Blockers
- **Publishing:** blocked until Milestone 7. Do not push `phase/5.5-release` as a PR, and do not merge anything carrying `Release-As: 1.0.0`.

## Recommended Next Step
1. Milestone 6, Provider-neutral core, is active (design `docs/superpowers/specs/2026-10-09-milestone-6-design.md`).
2. Next: Phase 6.4, the `/v1/systemone` adapter (#115). It needs a brainstorm. It builds on Phase 6.3's protocol seam and `DecisionClientMetadata`: presets for TypeSafe, OpenRouter, Clef, vLLM-SR and local servers, capability flags, optional auth and keyed DI clients.
   - Before it, optionally run the live smoke on `main` to confirm Phase 6.3 against the real APIs.
3. Run `pre-push-review` on each feature branch before its PR, and keep the report in `docs/plans/`: the docs tests treat every file at the top of `docs/` as a site page.
4. Allocation gates run only in the published AOT smoke executable, not in `dotnet test`: publish and run it for any change under `src/`.

## Operational notes
- **Live keys:** `TYPESAFE_API_KEY` and `OPENROUTER_API_KEY` are in the `live-api` environment, which deploys only from `main`. Run the suite with `gh workflow run live-smoke.yml --ref main`. About 13 small billed calls per run.
- **Website updates:**
  - .website's bot "update submodules" PRs hold their `build` run (`action_required`). Approve it with `gh api -X POST repos/ZeroAlloc-Net/.website/actions/runs/<id>/approve`, then merge with `--admin`, after asking the maintainer.
  - jev.zeroalloc.net only updates when that PR merges.
- **Org-wide publishing:** ZeroAlloc-Net/.github#49 tracks one shared publish workflow for the org. It no longer applies here: Minos publishes the way Thalos.NET does.
- **CI on a branch without a PR:** `gh workflow run ci.yml --ref <branch>`. Add `-f aot-smoke-runs=20` to repeat the AOT smoke run.

## What Phase 5.4 shipped
- **Live evidence:** Live smoke on `main`.
  - Probe [run 37913354273](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37913354273): 8/8, the first run with a real TypeSafe key.
  - Post-merge [run 37918889031](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37918889031): 12/13.
  - [Run 37925900075](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37925900075): 13/13.
- **Aliases:**
  - TypeSafe answers `jev-latest` and `jev-preview` with `jev-1.13.0`, and accepts `jev-1.13.0` as a model. Its `/v1/models` lists only the two aliases.
  - OpenRouter answers `jev-latest` as `typesafe/jev-1.13-20260917`. It has no `jev-preview`: HTTP 400, "Model typesafe/jev-preview does not exist", because OpenRouter prefixes the alias itself.
  - `OpenRouter_Preview_IsRejectedAsAnUnknownModel` pins that rejection, and fails when OpenRouter starts offering the preview (#111).
- **Error bodies:**
  - TypeSafe's 422 is `{"detail":[{type, loc, msg, input, ctx}]}` and its 401 is `{"detail":{error_type, message}}`. OpenRouter's errors are `{"error":{message, code}}`.
  - Pinned in `JevErrorMapperTests`, and asserted live in `InvalidRequest_IsValidation`.
- **Retry-After:** not seen live; no 429 or 529 was met. TypeSafe's API docs say only to back off, and its Python SDK reads both headers. This is recorded in the guide.
- **Guide (`client-and-errors.md`):**
  - the real 422 example, and the `ValidationProblems.List` snippet, which skips malformed entries;
  - OpenRouter's error shape;
  - the Retry-After record;
  - model ids: aliases, versioned ids, and `jev-preview` as TypeSafe-only.
- **Website:** jev.zeroalloc.net had been stale since Phase 4.4, because .website#84 was never merged and so `Json = true` was still shown. It was merged on 2026-10-09 and the site now serves the current guide.

## What Phase 5.3 shipped
- `samples/ZeroAlloc.Jev.AotSurface` + `aot-surface` CI job: roots both packages (`TrimmerRootAssembly`, `TrimMode=full`) and instantiates every public open generic over an enum (rooting cannot instantiate `where T : struct` generics), so every public member is trim and AOT analysed; 0 warnings on win-x64 and linux-x64. A test fails if a new public generic is not instantiated.
- Entry-point coverage: `tests/ZeroAlloc.Jev.AotSmoke.Tests` rebuilds the smoke app's real compilation (same features, editorconfig options and generators) and requires a `[Covers("<PublicAPI line>")]` on a check that really calls each of the 137 public entry points; default interface methods count only when called on a type that does not override them; record members are excluded by `[CompilerGenerated]`.
- Measurement: flaky allocation gates were caused by the measuring loop (warm-up, then forced gen2 GC, then ArrayPool's trim drops the warmed arrays under high machine memory load, so the measurement pays to refill them). Fixed in Jev's helpers and upstream in ZeroAlloc.TestHelpers 1.5.1 (#62), now pinned; relative gates use `AssertNoMoreThanValueTask` (#73, #79 closed). #74's unit tests use `AllocationGate`. #68's creep was the same artefact: true costs unchanged across phases except -384 B from ZeroAlloc.Rest 3.2.1. The yielding disabled-logger check keeps a 16 B tolerance sized from 800 runs; Jev's async helpers wait for TestHelpers#65 (tracked in #104).
- Proof: the AOT smoke app passed 20 consecutive runs on win-x64 and 20 on linux-x64 CI.

## What Phase 5.2 shipped
- Client comparison benchmark in `benchmarks/compare/`: ZeroAlloc.Jev against a hand-written HttpClient + System.Text.Json client, JevSharp 0.2.0, TypeSafe.AI.Sdk 0.3.0, Jev.Net 0.4.0 (all MIT, checks in `docs/plans/2026-10-04-phase-5.2-library-checks.md`), TypeSafe's JS SDK 0.6.0 and Python SDK 0.7.2.
  - Mock: our own minimal Kestrel endpoint (WireMock dropped: its request log halved throughput), serving one recorded Jev answer, counting requests at `/count`.
  - Harnesses: .NET (`ZeroAlloc.Jev.Benchmarks.Compare`, BenchmarkDotNet for bytes), Node, Python; same per-call latency method, warm-up of the measured instance, 16-worker throughput, verified request counts. .NET latency runs in 20 interleaved rotating rounds; throughput order rotates per run. All .NET clients share one SocketsHttpHandler setup.
  - Runners `run.ps1`/`run.sh` + `cores.py` (mock and clients on separate physical cores, by core type on hybrid CPUs) + `merge.py`; reusable for the org-wide sweep via `--project`, `--results`, `--bench-root`.
  - CI: `benchmarks-compare.yml` smoke on PRs, full run on the `benchmarks:compare` label or dispatch.
  - Published in `docs/performance.md` from three CI runs on fcc4cee (`benchmarks/compare/results/ci-run-1..3.json`): ZeroAlloc.Jev had the lowest mean latency, the highest throughput (small lead over raw) and the fewest bytes per call in all three; every claim is tested against the JSON. Local idle-machine table tracked in #97.
- Investigation: the first-run 2x latency gap was a cold-JIT artefact of measurement order, not Jev; Jev's pipeline costs about 5.9 us CPU per call against 5.3 for raw.
- Upstream: ZeroAlloc.Rest#406 (per-call Accept header, unguarded metric tags, params array, Host read) fixed in 3.2.1 by the org session; adopted here (#95), every AOT budget tightened by about 384 B, Linux CI confirms.
- Versioning: builds read the release manifest; non-release builds are `<version>-local` (prereleases sort below their release); `-p:JevRelease=true` applies to build and pack (Phase 5.4 must pass it to both). A missing or malformed manifest fails the build clearly.
- Docs tests read the analyzer release history as one sequence, so cutting a release can't break them.

## What Phase 5.1 shipped
- API review: `docs/plans/2026-10-04-phase-5.1-api-review.md`, 59 types and 445 members, findings R1–R14 each linked to its fix; impact analysis `docs/plans/2026-10-04-phase-5.1-public-api-review-impact-analysis.md`.
- Breaking (all in `PublicAPI.Unshipped.txt`, 43 removed and 40 added lines):
  - `Json = true` removed from `[Noul]`, `[Choice]`, `[Score]`, `[Criteria]`, `[Level]` (#67); the generator's `JsonMinifier` and the JSON branches of JEV003 and JEV004 removed; JEV109 and the JEV108 analyzer descriptor removed (listed under Removed Rules); JEV108 stays as a run-time builder failure, documented in its own `diagnostics.md` section.
  - `JevError(kind, message)` with init properties `StatusCode`, `RetryAfter`, `Detail` (`JsonElement?`) and `Exception` (#24).
  - `JevErrorKind` numbered 1–12 with no zero member, plus `Disposed` (#23). CA1008 is not in the `latest-recommended` set, so no suppression.
  - Builder overloads that could never build removed (keyed Choice, keyed Score, enum Score without a configurator); `NoulAttribute.True`/`False` renamed `WhenTrue`/`WhenFalse`; public `ct` parameters renamed `cancellationToken`, and the guide and samples follow.
- Disposal (#25): a call in flight when its client is disposed returns `Disposed`; a disposed client never retries, owned or borrowed `HttpClient` (maintainer-approved ruling); a borrowed client's attempt already in flight keeps its own result. `DisposalGuardJevApi` sits inside the retry proxy and inspects only faulted attempts, so the success path allocates nothing.
- Telemetry (#85): ZeroAlloc.Telemetry 1.11.0, `ExceptionDescription = false` on every `[Trace]`; a thrown call's span carries `error.type` = the exception's `FullName` and no status description.
- Additive: `Noul` value equality (0 B AOT gate); `JevOptionSet<T>` hidden from IntelliSense; XML docs completed (R11–R14).
- Measurement notes: AOT absolute gates pass; figures crept since Phase 3 (non-yielding +~20 B, a yielding call 5254 → 5363 B), noted on #68 for Phase 5.3; the relative gates still flake (#79). #21 closed.

## What Phase 4.4 shipped
- ZeroAlloc-Net/.website#81 (open): `repos/jev` submodule, `apps/docs-jev` copied from docs-rest with `onBrokenLinks: 'throw'` and `planning`/`superpowers` excluded, a zeroalloc.net home-page entry (`available: true`, maintainer decision), README row and lockfile. Its `build` workflow passed.
- Logo: `assets/icon.svg` and `icon.png` are byte-identical copies of ZeroAlloc.Rest's shared icon (maintainer decision); pack tests pin both hashes and check the packed icon.
- README cut from 531 to about 95 lines: logo as a Markdown image (nuget.org renders no raw HTML), disclaimer and Status line verbatim, install, a compiled and tested example, one absolute jev.zeroalloc.net link per guide page, samples, building, testing and license. Tests check every site link against the site's routing (folder plus front-matter `id`, index pages, `slug: /`), anchors at h2 to h6, sidebar order, titles, absolute links and no raw HTML. The docs tests' heading ids matched a real Docusaurus build for all 17 pages.
- CI: `docs-site.yml` checks out .website, checks this repository out into `website/repos/jev`, and builds `@zeroalloc/docs-jev` on changes to `docs/**` or `assets/**`. Not a required check. `trigger-website.yml` now also dispatches on `assets/**`.
- Guide: `client-and-errors.md` names both ambiguous bare-null constructor calls, CS0121.
- Maintainer to-do: create the Cloudflare Workers project `za-docs-jev` and the `jev.zeroalloc.net` custom domain; until then the README's site links do not load. #29 has a note to reword the README Status line when publishing starts.

## What Phase 4.3 shipped
- The user guide in `docs/`, in the org's Docusaurus layout: getting-started (`slug: /`), question-types, typed-evaluation, question-sets-at-run-time, client-and-errors, dependency-injection, observability, native-aot, diagnostics, testing-your-code, patterns/ (with `_category_.json`), samples and performance. Maintainer decision: the guide is the single source of user docs; Phase 4.4 cuts the README to an overview, and every README reference fact now has a guide home.
- Every C# block is a compiled MarkdownSnippets region in `tests/ZeroAlloc.Jev.Docs.Tests`; each page's main example runs as a test. 318 docs tests, including front matter (ids, positions, the root slug), every internal link and anchor under the github-slugger rule (links that wrap across lines included), a `## Next` on every page, and tests tying copied tables and quoted figures to the code (JEV rules, AOT gates, `JevDefaults`, telemetry names, sample commands).
- Published scope excludes `docs/planning`, `docs/plans` and `docs/superpowers`, matching Phase 4.4's site exclusions.
- The docs tests reference `OpenTelemetry.Extensions.Hosting` 1.19.1 (test-only) so the observability page's wiring compiles; the packages still take no OpenTelemetry dependency, which a test checks.
- README fixes on the way: removing a defaults handler from Jev's client, and the telemetry notes (ZeroAlloc.Telemetry#184 is fixed in 1.11.0; adoption tracked in #85).
- Not reproduced: one docs test-host crash seen once during the final fix round, clean in six reruns.

## What Phase 4.2 shipped
- `samples/ZeroAlloc.Jev.Samples.Shared`: `SampleMode` (replay default, live, record), `ReplayHandler` (primary handler, answers by SHA-256 of the request body, fails with the re-record command), `RecordingHandler` (successful response bodies only, never headers), `RecordingSession` (throws on mixed models), `RecordingsFile` (bodies stored as raw JSON, LF-only), `SampleHost` (wires a mode onto `AddJevClient`; replay supplies a placeholder key). Live runs need no checkout; replay and record read and write the recordings in the clone.
- Three original samples recorded on OpenRouter (`typesafe/jev-1.13-20260917`, 2026-10-02), each crediting and linking its TypeSafe cookbook:
  - Guardrails: 15 authored messages, Strict and Lenient policies. Ruling: advice requests are review-only (`BlockAt` null); severity still blocks. g15 (indirect instruction probe, 0.61) is where the policies part on recorded answers: Strict blocks, Lenient reviews — a 0.01 margin, documented.
  - Intent routing: 12 travel requests; 8 of 12 need no language model. "asdf" came back as high-confidence Other, so it reaches a person by intent, not by the low-confidence rule (documented).
  - Re-ranking: 25 authored help-centre articles, keyword shortlist of 8, one fan-out request per query with a keyed Noul per candidate built at run time; hit@1 1/5 to 5/5, hit@3 2/5 to 5/5 (a small corpus and a deliberately weak baseline, documented).
- `tests/ZeroAlloc.Jev.Samples.Tests` (139 tests): every rule pinned on canned answers, every recorded decision pinned, report snapshots via ZeroAlloc.TestHelpers 1.5.0 `TextSnapshot.VerifyText`, and a recordings safety scan (key shapes, auth headers case-insensitive, exact entry shape).
- CI (maintainer request): the `build` job runs every discovered sample's real entry point in replay mode with an empty key and `timeout 120s`. Other ZeroAlloc repos only run their AotSmoke apps.
- Upstream: ZeroAlloc.TestHelpers #59 (`TextSnapshot`, BCL-only) filed and shipped in 1.5.0 by the org session.

## What Phase 4.1 shipped
- Core: `ConfidenceTier { Low, Medium, High }` and `readonly struct ConfidenceThresholds`. Its `default` means 0.5 and 0.9, the cut points TypeSafe's confidence guide uses in an example, so an unset value never classifies every answer as High; a value on a threshold goes to the higher tier and NaN is Low. `Normalized` on `Score<T>` and `KeyedScore`: Expected / (levels − 1), clamped, 0 below two levels.
- AOT: the `PatternHelpers` gate holds `Classify` and both `Normalized` properties to 0 B per call; the check tells a strict instance from the default.
- Docs: guides for fan-out, confidence routing, composite scoring and intent routing in `docs/patterns/`, with an index and a `## Patterns` README section. Every C# block comes from a `#region` in `tests/ZeroAlloc.Jev.Docs.Tests`, written in by MarkdownSnippets 28.5.0 (`mdsnippets.json`, local tool); each example runs against canned answers, a test fails when a snippet block has more than one source, and CI runs `dotnet mdsnippets` then `git diff --exit-code`.
- Maintainer ruling at the final review: the guides use our own example data (app-store reviews, shop refunds, pull-request rubrics, an IT helpdesk); TypeSafe's example data has no known reuse terms. Each guide links TypeSafe's pattern page.
- Lessons: MarkdownSnippets excludes directories only by bare name, and reads snippet regions from any untracked text file, including `.superpowers` review diffs, so `ExcludeSnippetDirectories` lists `.superpowers`.
- Issue filed: #79 (the relative AOT allocation gates compare two noisy measurements with zero headroom; one flaked once during verification).

## What Phase 3.4 shipped
- Core: public `JevClientOptions.Validate()` runs `JevClientSettings.Resolve`, the check every constructor runs, so it throws what the constructor would throw. The invalid-option cases live in one shared test source, `InvalidOptionsCases`.
- DI:
  - Every `AddJevClient` registration adds an internal `JevClientOptionsValidator` for its options name, plus `ValidateOnStart()`. A generic host fails at `StartAsync`; without a host the first resolve throws `OptionsValidationException` with the core's message.
  - New overloads `AddJevClient(IConfiguration)` and `AddJevClient(string name, IConfiguration)`. Binding is source-generated through `EnableConfigurationBindingGenerator`. New dependency `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.0.
- Plan-probe finding: `TryAddEnumerable` keeps one validator for every options name, because it compares implementation types. The validator is added on the first registration of a name instead.
- Behaviour changes, documented in the README:
  - Options are still validated when the app registers its own `IJevClient`; a test host needs a placeholder key.
  - Creating Jev's named `HttpClient` from the factory directly needs valid options, an API key included.
  - A configuration value the binder cannot convert fails at the same moment with the binder's `InvalidOperationException`.
- Cost: a client bound from configuration measures 4376 B per call under Native AOT, equal to a hand-built client, checked by a relative gate. Existing budgets are unchanged.
- ZeroAlloc.Validation.Options was dropped from the milestone; startup validation reuses the core's own rules.
- Maintainer decision still open: config errors carry the core's `(Parameter 'options')` suffix, which the spec keeps.

## What Phase 3.3 shipped
- New package `ZeroAlloc.Jev.DependencyInjection` with four overloads, all returning `IHttpClientBuilder`:
  - `AddJevClient()` and `AddJevClient(Action<JevClientOptions>)` register the default `IJevClient` singleton.
  - `AddJevClient(name)` and `AddJevClient(name, configure)` register keyed singletons, injected with `[FromKeyedServices(name)]`.
- How each registration is wired:
  - Named options: the default client uses `Options.DefaultName`, a keyed client uses its key.
  - A named `HttpClient`, `ZeroAlloc.Jev` or `ZeroAlloc.Jev:{name}`, with a pooled `SocketsHttpHandler` (2 min) and an infinite handler lifetime, configured through `JevClient.ConfigureHttpClient`.
  - The `HttpClient` is configured on the first registration of each name only, guarded by a private marker service. The client is registered with TryAdd, so an `IJevClient` the app registered itself wins.
- Maintainer decision: `AddJevClient` removes the factory's request loggers, which cost 344 B per call (4720 vs 4376 B). `.AddDefaultLogger()` on the builder restores them.
- Core: public `JevClient.ConfigureHttpClient(HttpClient, JevClientOptions?)` applies the timeout, the base address (only when none is set) and the User-Agent, and needs no API key. `EnsureValidTimeout` rejects a time-out above `int.MaxValue` ms up front, which fixed a half-configured client and a leaked owned client.
- Cost: a DI-resolved call measures 4376 B under Native AOT, equal to a hand-built client. A relative gate and an absolute 4864 B gate check this. Existing budgets are unchanged.
- The pack fixture no longer packs with `--no-build`, which used to let a stale build pass.
- The milestone's ZeroAlloc.Inject and ZeroAlloc.Rest.DependencyInjection dependencies were dropped as unneeded.
- Issues filed: #73 (adopt ZeroAlloc.TestHelpers#56's measuring API, implemented in TestHelpers PR #57 but not released yet) and #74 (hand-rolled zero-allocation tests can flake under load).
- Lesson: the ZeroAlloc org session is `zeroalloc-0f` this time; check ListAgents for its current name before messaging it.

## What Phase 3.2 shipped
- Internal `[Instrument("ZeroAlloc.Jev")] IJevOperations`, with four methods, implemented by `JevOperations` over the retry proxy. `JevClient` calls the generated `JevOperationsInstrumented`. Source and meter are both `ZeroAlloc.Jev`. No constructor changes.
- One GenAI CLIENT span per operation, `evaluate {model}` or `list_models`, with start tags and success/failure end tags. Failures get `error.type` = the `JevErrorKind` name and Error status with no description. The span nests each retried attempt's `ZeroAlloc.Rest` span.
- Metrics: `gen_ai.client.operation.duration` in seconds, the GenAI token histograms and counters (`gen_ai.token.modality=text`), and `jev.answer.confidence`, one point per Choice or Score answer. Each has explicit buckets.
- `Evaluated<T>` defers the model, usage and confidence reads from the pooled response until the proxy asks, which it does only while something listens. `GeneratedQuestionCount<T>` is now read on every typed call and is 0 for a malformed hand-written set.
- Cost with nothing listening:
  - 0 B on the raw path, list-models and synchronously completing typed and built-set calls.
  - About 211 B on an asynchronously completing typed or built-set call, for the unwrap. The limit is 344 B.
- Cost while listening: about 1.0–1.8 KB per call. New AOT budgets: 6272, 5440 and 5760 B while listening, and 5056 B for an async typed call with telemetry off. Existing budgets are unchanged.
- The AOT smoke app's yielding measurements now take the median of five runs, because the least of three made a logging check flaky.
- New runtime dependency ZeroAlloc.Telemetry 1.10.0. Its generator reaches consumers transitively and stays inert, which the pack tests assert.
- Upstream: ZeroAlloc-Net/ZeroAlloc.Telemetry#184 is filed by the ZeroAlloc org session. On the exception path the span gets the exception message as its description and no `error.type`. The README documents this. #68 has a comment on the third loose budget, `EvaluateBuiltSetRoundTrip`: 4736 B against 3656 B measured.
- Lesson: the plan quoted a stale 4288 B for the built-set call. `main` measured 3656 B. Re-measure baselines on `main` before a plan asserts them.

## What Phase 3.1 shipped
- `JevClient(JevClientOptions?, ILoggerFactory?)` and `JevClient(HttpClient, JevClientOptions?, ILoggerFactory?)`; category `ZeroAlloc.Jev.JevClient`; the four older constructors log nothing. `new JevClient(null, null)` is now CS0121 (accepted; it always threw).
- Six source-generated `[LoggerMessage]` events in internal `JevLog`, ids 1001–1006, none with more than six fields (a larger one generates a struct HLQ006 rejects). Success at Debug, failures and retries at Warning, unexpected exceptions at Error.
- Retried attempts are logged by the internal `LoggingJevApi` decorator between the ZeroAlloc.Resilience proxy and the transport, using the proxy's own `RetryPolicy`.
- Privacy: no state, instructions, criteria, answers, API key, headers or `JevError.Detail` in logs. `Network` and `InvalidResponse` messages are logged as a fixed text, because they can quote the request or response; the privacy tests found and closed a real leak of exception text on the network path.
- Cost: nothing with no logger or every level disabled, proven by an async discriminating check in the AOT smoke app (5254 B per call either way). An enabled logger adds 0 B on a synchronous call and about 480 B on a truly asynchronous one.
- New runtime dependency `Microsoft.Extensions.Logging.Abstractions` 10.0.0; its `[LoggerMessage]` generator reaches consumers transitively (NuGet/Home#6720) and stays inert.
- Issues filed: #67 (remove `Json = true` from attributes, deferred to Phase 5.1), #68 (tighten two AOT budgets, needs a linux-x64 re-measurement).

## What Phase 2.4 shipped
- `JevQuestionSet.CreateBuilder()`: Noul, enum and keyed Choice, enum and keyed Score questions with configurators (closed after their callback) and handles. `Build()` returns `Result<JevQuestionSet, JevError>` after checking the analyzers' rules with ZeroAlloc.Validation 2.0.3; failures come back as the new `JevErrorKind.InvalidQuestions` with `JevError.Failures`, warnings on `JevQuestionSet.Warnings`. `QuestionsUtf8` is byte-identical to the generator's for the same set, which a differential test pins.
- `JevAnswers.Get(handle)` reads `Noul`, `Choice<T>`, `Score<T>`, `KeyedChoice` and `KeyedScore` without allocating; a handle from another builder, a `default` handle or one added after the build throws `ArgumentException`. `IJevClient.EvaluateAsync(JevQuestionSet questionSet, JevContent state)` with its CancellationToken overload are default interface methods; `JevClient` overrides them on its pooled path.
- Shared single sources: `JevLimits` (generator, analyzers, library), `Utf8Keys`, the generator's linked `SnakeCase.cs` and `DiagnosticIds.cs`, and `SystemOneJsonEncoder`, which escapes as the generator does.
- Budgets: Build 7296 B, evaluating a built set 4736 B, parsing a built set 256 B, `JevAnswers.Get` 0 B. A 20-question built parse runs at 1.26× the generated one, so no key map.
- Maintainer decisions: enum options are read from the enum's public fields in declaration order under `DynamicallyAccessedMembers` (exception to the no-reflection goal, since `Enum.GetName` names aliases by the alias in larger enums; verified under Native AOT). NuGet flows ZeroAlloc.Validation's analyzers to consumers transitively (NuGet/Home#6720); they stay inert. Score answers require `legend`, as the TypeSafe API does (#61, fixed in #64).
- Upstream: ZeroAlloc-Net/ZeroAlloc.Validation#282 (InclusiveBetween with When ignored its upper bound) was fixed in 2.0.3 by the zeroalloc-ae session, which owns upstream ZeroAlloc work.
- Lesson: phase PRs are squash-merged, so a plain PR title makes release-please drop the whole phase. Put a `BEGIN_COMMIT_OVERRIDE` block in the PR body; after merging, confirm the entries appear in the release PR.

## What Phase 2.3 shipped
- `Examples` / `NotFor` in attributes, sent as a criterion object; object and array instructions and criteria; `state` helpers and the `JevContent` factories, with budgets and `ContentBenchmarks`. Merged as PR #58.

## What Phase 2.2 shipped
- `ZeroAlloc.Jev.Analyzers` reports every Jev diagnostic, and the generator reports none. New rules: JEV001–002 (Error: empty Choice or Score enum, from the SDK schema), JEV003 (Warning: blank text), JEV004 (Warning: a backticked name that matches no `State` member), JEV005 (Warning: the sketch's level and option ranges), JEV006 (Info: a Choice member without `[Criteria]`). JEV101–107 moved out of the generator, with #5–#11 fixed. Enum rules are local, so the IDE shows them. The analyzer also runs on generated code.
- Invalid sets get throwing stub properties, so command-line builds show the JEV error rather than CS9248.
- `ZeroAlloc.Jev.CodeFixes` inserts `[Criteria]` and `[Level]`. Its Roslyn `ImportAdder` workarounds are tracked in #51; upstream, dotnet/roslyn#85804 and a comment on #77119.
- The package ships the generator, analyzers and code fixes. `Microsoft.CodeAnalysis.CSharp.Workspaces` is pinned at `[5.0.0]`.

## What Phase 2.1 shipped
- `IJevClient.EvaluateAsync<T>` (string, `JsonElement`), `EvaluateUtf8Async<T>`, `EvaluateAsync<T, TState>` as default interface methods; `JevClient` overrides with a raw pooled-buffer path through `IJevApi.EvaluateRawAsync` and `JevRawSerializer`. Overload pairs without / with required `ct` (RS0026).
- `[JevQuestions(State = typeof(...))]` → `IJevQuestionSet<TSelf, TState>`; JEV107 for invalid State types (moves to the analyzers in 2.2).
- `JevClientOptions.Model`; value equality for `ProbabilityMap<T>`, `Choice<T>`, `Score<T>`.
- Allocation gates: typed round trip 3784 B / budget 4224 B (identical on linux-x64). Upstream: ZeroAlloc.Rest#362.

## What Phase 1.8 shipped
- The AOT smoke app enforces `AllocationGate` budgets (Parse 192 B, readers 0 B, `EvaluateAsync` 5120 B) and treats every warning as an error.
- `benchmarks/ZeroAlloc.Jev.Benchmarks` plus `benchmarks.yml` (org smoke gate; `smoke / benchmarks` not required); record the first full run in `docs/performance.md`.
- The generator is packed from `GetTargetPath`; `tests/ZeroAlloc.Jev.PackTests` checks the nupkg layout and dependencies.
- `trigger-website.yml` (user docs only); Phase 4.3 now targets the org website. Org issue ZeroAlloc-Net/.github#43.

## What Phase 1.7 shipped
- `tests/ZeroAlloc.Jev.Integration.Tests`: WireMock.Net 2.18.0 over real sockets, 15 tests, public API only, runs in the CI `build` job.
- `tests/ZeroAlloc.Jev.Live.Tests`: 6 live tests that run only with `JEV_LIVE=1` and the provider's key; `.github/workflows/live-smoke.yml` runs them on manual dispatch from the `live-api` environment.
- OpenRouter live evaluation passes with the default model `jev-latest` (run locally 2026-09-27).
- Maintainer to-do: create the `live-api` environment (deployment branches `main`), add `TYPESAFE_API_KEY` and `OPENROUTER_API_KEY`, dispatch **Live smoke**, then record below whether TypeSafe sends `Retry-After` and what the 422 body looks like.

## What Phase 1.6 shipped
- `JevClient` retries 429, 503/529, other 5xx, 408, network failures and client time-outs with exponential backoff through ZeroAlloc.Resilience 3.2.0's `[Retry]` on the internal `IJevApi`, honouring `retry-after-ms` and `Retry-After` capped by `MaxRetryDelay`; exhausted retries return the last `JevError`.
- Public options `MaxRetries` (2), `InitialBackoff` (500 ms), `MaxRetryDelay` (30 s), `Jitter` (true); `Timeout` is per attempt.
- `RetryAfterHeader` reads all RFC 9110 date forms, pivots RFC 850 years on the current date, clamps overflow and rejects non-finite `retry-after-ms`.
- Tracked: ZeroAlloc-Net/ZeroAlloc.Resilience#195 (the `JevClient.ThrowDeclined` unwrap is a workaround until it ships), ZeroAlloc-Net/ZeroAlloc.Resilience#197 (unused package dependencies), #38 (HLQ005 pragmas), #40 (`X-TypeSafe-Retry-Count` parity).
- Lesson (superseded 2026-09-30): this assumed merge commits. Phase PRs are squash-merged, and a plain title then drops the phase from the changelog; see Phase 2.4's lesson.

## What Phase 1.5 shipped
- Repository https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev, public; `main` protected by ruleset "Main" (required checks `build`, `aot-smoke`; pull requests with 1 approval; admin bypass for merges, since the only maintainer cannot approve their own pull request).
- Release PR #33 (`chore(main): release 0.1.0`) is open and stays open: releases and NuGet publishing wait until the maintainer declares the package mature. There is no publish job; the org's `NUGET_API_KEY` is visible to this repository, so any future publish workflow must be a deliberate step.
- Renovate is on the org configuration. It merged two dependency PRs into `main` before the ruleset existed; the deliberate `Microsoft.CodeAnalysis.CSharp` 5.0.0 pin is now guarded with the exact NuGet range `[5.0.0]` (a bare `5.0.0` means 5.0.0 or higher, which is why #34 appeared).

## What Phase 1.4 shipped
- Public `IJevClient` / `JevClient` (four constructors without optional parameters), `JevClientOptions` (`set` accessors), `JevProvider`, `JevError` / `JevErrorKind`; internal `IJevApi` over ZeroAlloc.Rest 2.1.0 with `[ErrorMapper(typeof(JevErrorMapper))]`, `JevClientSettings`, `RetryAfterHeader`.
- OpenRouter: `SystemOneResponse.Id` / `Provider`, `JevUsage.Cost`; `ListModelsAsync` returns `Unsupported` without a request; `TYPESAFE_BASE_URL` applies to TypeSafe only.
- Upstream issues filed: ZeroAlloc-Net/ZeroAlloc.Rest#335 (HLQ001 in generated code, which `ZeroAlloc.Jev.csproj` suppresses project-wide until it ships) and #336 (transitive dependency footprint).
- Follow-ups for later phases are listed at the end of the phase 1.4 plan.

## What Phase 1.3 shipped
- Runtime types in `ZeroAlloc.Jev`: `Noul`, `Choice<T>`, `Score<T>`, `ProbabilityMap<T>` (internal constructor), `JevOptionSet<T>`, `IJevQuestionSet<TSelf>`, the six question attributes, and `JevAnswerReader`, a public helper hidden from IntelliSense that the generated code calls.
- Generator `src/ZeroAlloc.Jev.Generator` (netstandard2.0, Roslyn 5.0.0): `QuestionsUtf8` as a pure-ASCII u8 literal, a `Parse` that dispatches to `JevAnswerReader`, and one private option set per Choice/Score question. It reports JEV101–JEV106.
- Decisions taken without the user while they were away. They are listed with what each costs if wrong in the session's final message, and recorded in the plan's "Follow-ups from the final review":
  - Work went directly on `main`, as the trunk conventions allow.
  - `LocationInfo` holds the `SyntaxTree`, so diagnostics are reported at in-source locations.
  - Subagent commits carry their own `Co-Authored-By: Claude Sonnet 5` or `Claude Haiku 4.5` trailers.
  - The `ProbabilityMap<T>` constructor is internal.

## Upstream check (2026-09-26)
- ZeroAlloc.Rest 2.1.0 fixed #298 (error body) and #300 (custom error type); 2.0.0 fixed #299 and #301 (breaking; see `docs/migrating-to-v2.md`).
- ZeroAlloc.Resilience 3.2.0 fixed #142 (`RetryWhen`) and #143 (`DelayHint`).
- ZeroAlloc.Validation 2.0.0 is released (breaking; the generator is now bundled).
- Still open: Telemetry #142, needed only by Phase 3.3.
- Milestone 1 has no upstream blockers.

## Open Decisions
- Decided 2026-09-27: the repository is public at https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev (`origin`, `main` unprotected, no PRs required). NuGet publishing, including alpha pushes, waits until the maintainer declares the package mature — no NuGet secret is configured.
- Decided 2026-09-27: the NuGet id `Jev.Net` is taken by another publisher, so the project becomes `ZeroAlloc.Jev` (package and root namespace), published from the ZeroAlloc.NET NuGet account with the GitHub repo in the ZeroAlloc-Net org. The rename is Phase 1.5, right after 1.4; Resilience moved to 1.6, Test harness 1.7, CI and release 1.8.
- Decided 2026-09-27: former phases 1.4 Transport and 1.5 Error model merged into 1.4 "Transport and error model"; Resilience is now 1.5, Test harness 1.6, CI and release 1.7. OpenRouter model listing fails fast with `JevErrorKind.Unsupported`; configuration is a `JevProvider` enum plus `JevClientOptions`; `JevError` is one sealed type with a `Kind` enum.
- Decided 2026-09-27 (phase 1.6): retry 429, 503/529, other 5xx, 408, network failures and time-outs; defaults mirror the official TypeSafe Python SDK.
- Phase 1.3 and 1.4 follow-ups are tracked as issues labelled `follow-up` on ZeroAlloc-Net/ZeroAlloc.Jev.

## Blockers
- None for Milestone 3's start.
- Still unknown until a TypeSafe live run (needs `TYPESAFE_API_KEY` and the `live-api` environment): whether TypeSafe sends `Retry-After`, the 422 body schema, and whether Phase 2.4's `BuiltQuestionSet_ParsesAKeyedChoice` passes.

## Recommended Next Step
Open and merge the Phase 5.3 PR, check its release-please entries, then Phase 5.4 (needs a TypeSafe key) or Phase 5.5's planning.




Open maintainer items:
- the `live-api` environment and the TypeSafe live run;
- record the performance baseline from a full Benchmarks run;
- decide #68, which now covers three gates;
- answer ZeroAlloc.Telemetry#184's two API questions;
- decide whether to report to dotnet/runtime that IHttpClientFactory's request loggers allocate 344 B per call even when no logger is enabled;
- Renovate PRs #70 and #71;
