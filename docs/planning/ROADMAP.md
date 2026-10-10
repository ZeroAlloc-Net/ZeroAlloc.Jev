---
compress_memory: disabled
---

# Project Roadmap

> Source spec: `docs/superpowers/specs/2026-09-24-roadmap-design.md`

## Milestone 1: Foundation & core client [status: complete]
**Goal:** A working, AOT-clean `JevClient` covering both endpoints, with CI gates (tests, AOT smoke, benchmarks); NuGet publishing is deferred until the maintainer declares the package mature (#29).
**Started:** 2026-09-24
**Completed:** 2026-09-27
**Definition of Done:**
- [x] `JevClient` calls `POST /v1/systemone` (Noul, Choice, Score) and `GET /v1/models`, returning `Result<T, JevError>`
- [x] `[JevQuestions]` generator emits question JSON and parses answers, verified against the wire fixtures
- [x] 401/422/429/529, network failures and timeouts map to `JevError`; 429/529 retried honoring `retry-after`
- [x] WireMock.Net component tests and key-gated live smoke suite pass
- [x] AOT smoke app publishes with zero IL2xxx/IL3xxx warnings in CI
- [x] BenchmarkDotNet smoke gate and `AllocationGate` budgets in CI
- [x] release-please wired; NuGet publishing deferred (#29)

### Phase 1.1: Repo scaffolding [status: complete]
**Goal:** Solution skeleton following AdoNet.Async standards: `.slnx`, `Directory.Build.props`, analyzers, `.editorconfig`, commitlint, Renovate, logo placeholder.
**Surface:** Infra
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-24-phase-1.1-repo-scaffolding.md`
**Completed:** 2026-09-24

### Phase 1.2: Wire model [status: complete]
**Goal:** Request/response types mirroring the HTTP API, serialised via System.Text.Json source generation / ZeroAlloc.Serialisation.
**Surface:** Backend
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-24-phase-1.2-wire-model.md`
**Completed:** 2026-09-24

### Phase 1.3: Question generator core [status: complete]
**Goal:** Incremental `[JevQuestions]` source generator with its attribute and runtime types (`[Noul]`, `[Choice]`, `[Score]`, `[Criteria]`, `[Level]`, `IJevQuestionSet<TSelf>`, typed `Noul` / `Choice<T>` / `Score<T>`), emitting `QuestionsUtf8` and a `Utf8JsonReader` answer parser, verified against the Phase 1.2 wire model and fixtures. No transport, `EvaluateAsync` or analyzers. Change spec: `docs/superpowers/specs/2026-09-25-question-generator-roadmap-design.md`.
**Surface:** Backend
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-26-phase-1.3-question-generator-core.md`
**Completed:** 2026-09-27

### Phase 1.4: Transport and error model [status: complete]
**Goal:** Public `JevClient` (`IJevClient`) calling `/v1/systemone` and `/v1/models` on TypeSafe or OpenRouter through an internal ZeroAlloc.Rest 2.1.0 interface, returning `Result<T, JevError>` for every outcome via `[ErrorMapper]` — 401/422/429/529, other statuses, network failures, time-outs and unreadable responses. `JevClientOptions` with `JevProvider`, API key and base address from options or `TYPESAFE_API_KEY` / `OPENROUTER_API_KEY` / `TYPESAFE_BASE_URL`. Merges the former 1.4 Transport and 1.5 Error model (2026-09-27).
**Surface:** Backend
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.4-transport-and-error-model.md`
**Completed:** 2026-09-27

### Phase 1.5: Rename to ZeroAlloc.Jev [status: complete]
**Goal:** Rename the package, root namespace, projects and generator references from `Jev.Net` to `ZeroAlloc.Jev` — the NuGet id `Jev.Net` is owned by another publisher (JohnCampionJr, since 2026-09-20). Covers namespaces, project and folder names, the solution, generator metadata names and emitted `global::` references, snapshots, PublicAPI files, package metadata and docs; the package is published from the ZeroAlloc.NET NuGet account with the repo in the ZeroAlloc-Net GitHub org. The result must fit the ZeroAlloc-Net org: conventions surveyed from the sibling repos (ZeroAlloc.Rest, .Results, .Resilience) for naming, layout, build props, package metadata, docs and CI shape; and Native AOT: a smoke app publishes with `PublishAot` and zero IL2xxx/IL3xxx warnings, with the `aot-smoke` CI job landing in this phase and becoming a required check.
**Surface:** Refactor
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.5-rename-to-zeroalloc-jev.md`
**Completed:** 2026-09-27

### Phase 1.6: Resilience [status: complete]
**Goal:** ZeroAlloc.Resilience retry with exponential backoff on 429, 503/529, other 5xx, 408, network failures and time-outs, honouring `Retry-After` and `retry-after-ms`; `MaxRetries`, `InitialBackoff`, `MaxRetryDelay` and `Jitter` on `JevClientOptions`, defaulting to the official TypeSafe SDK's 2 retries, 500 ms, 30 s and jitter. Completes `RetryAfterHeader` (#18).
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-1.6-resilience-design.md`
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.6-resilience.md`
**Completed:** 2026-09-27

### Phase 1.7: Test harness [status: complete]
**Goal:** Unit tests, WireMock.Net component tests, and a live smoke suite skipped when no API key is present — covering both TypeSafe direct and OpenRouter.
**Surface:** Infra
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-1.7-test-harness-design.md`
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.7-test-harness.md`
**Completed:** 2026-09-27

### Phase 1.8: CI and release pipeline [status: complete]
**Goal:** Build/test, AOT smoke with `AllocationGate`, a BenchmarkDotNet smoke gate, release-please, a pack test verifying the packed nupkg carries the generator under `analyzers/dotnet/cs`, every trim and AOT warning promoted to an error, and a website trigger workflow that notifies the org site on `docs/` changes. Publishing to nuget.org and api-compat stay switched off until the maintainer declares the package mature; the repository itself is already public. Tracked: #28 (api-compat once a released baseline exists), #29 (NuGet publishing when mature); versioning is by release-please.
**Surface:** Infra
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-1.8-ci-and-release-pipeline-design.md`
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.8-ci-and-release-pipeline.md`
**Completed:** 2026-09-27

## Milestone 2: Typed .NET API [status: complete]
**Goal:** Questions and answers become strongly typed, idiomatic C# with no reflection. They are declared as `[JevQuestions]` types or built fluently at runtime, evaluated through `EvaluateAsync<T>`, AOT-clean and within CI-enforced allocation budgets.
**Started:** 2026-09-27
**Completed:** 2026-09-30
**Design:** `docs/superpowers/specs/2026-09-27-milestone-2-design.md`
**Definition of Done:**
- [x] `[JevQuestions]` types evaluate end-to-end through `EvaluateAsync<T>` → `Result<T, JevError>`, with no reflection, plus raw `JsonElement` / string / UTF-8 overloads
- [x] All Jev diagnostics come from `ZeroAlloc.Jev.Analyzers` (JEV001–006, and JEV101–107 moved out of the generator); code fixes adding a missing `[Criteria]` or `[Level]` in `ZeroAlloc.Jev.CodeFixes`; #4–#11 closed
- [x] Structured instructions and criteria work in attributes and builders
- [x] Fluent builders cover all three question types, with runtime API-limit validation via ZeroAlloc.Validation
- [x] Every phase adds `AllocationGate` budgets and benchmarks for what it ships; the AOT smoke app stays clean; #12, #13 and #22 closed

### Phase 2.1: Typed evaluation [status: complete]
**Goal:** `EvaluateAsync<T>` returning `Result<T, JevError>`, with:
- typed state via `[JevQuestions(State = typeof(...))]` and a caller-supplied `JsonTypeInfo`;
- raw `JsonElement` / string / UTF-8 overloads;
- a non-breaking `IJevClient` shape (#22);
- the record-equality fix (#12) and the ProbabilityMap boxing fix (#13), with budgets and benchmarks.

**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-2.1-typed-evaluation-design.md`
**Plan:** `docs/superpowers/plans/2026-09-27-phase-2.1-typed-evaluation.md`
**Completed:** 2026-09-27

### Phase 2.2: Analyzers and code fixes [status: complete]
**Goal:** `ZeroAlloc.Jev.Analyzers` hosts JEV001–006 (empty enums, empty text, state-member references, option and level guidance, missing criteria) and the JEV101–107 checks, which move out of the generator (#4; #5–#11 fixed along the way). `ZeroAlloc.Jev.CodeFixes` adds code fixes for a missing `[Criteria]` and a missing `[Level]`. Both are packed under `analyzers/dotnet/cs` and asserted by the pack tests.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-2.2-analyzers-and-code-fixes-design.md`
**Plan:** `docs/superpowers/plans/2026-09-28-phase-2.2-analyzers-and-code-fixes.md`
**Completed:** 2026-09-28

### Phase 2.3: Structured instructions and criteria [status: complete]
**Goal:** `Examples` / `NotFor` in attributes, sent as a criterion object, which is a Jev.Net convention and not an API field; object and array instructions and criteria; and `state` helpers. This spans the attributes, the generator and the analyzers, with budgets for the new paths.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-28-phase-2.3-structured-instructions-and-criteria-design.md`
**Plan:** `docs/superpowers/plans/2026-09-28-phase-2.3-structured-instructions-and-criteria.md`
**Completed:** 2026-09-28

### Phase 2.4: Fluent question builders [status: complete]
**Goal:** Builders for runtime-defined Noul, Choice and Score questions that share the typed answer types, with:
- runtime API-limit validation via ZeroAlloc.Validation, using limits shared with the analyzers;
- allocation budgets and benchmarks for the builders.

**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-28-phase-2.4-fluent-question-builders-design.md`
**Plan:** `docs/superpowers/plans/2026-09-28-phase-2.4-fluent-question-builders.md`
**Completed:** 2026-09-30

## Milestone 3: .NET integration [status: complete]
**Goal:** Jev feels native in a .NET generic-host app: one-call registration, including keyed clients per provider; options bound from configuration and failing fast, carrying the retry and timeout settings; and structured logs, spans and metrics for tokens, latency and confidence. Logging and telemetry live in the core package, and the DI package wires them up. AOT-clean and within CI-enforced allocation budgets.
**Started:** 2026-09-30
**Completed:** 2026-10-01
**Design:** `docs/superpowers/specs/2026-09-30-milestone-3-design.md`
**Definition of Done:**
- [x] `ZeroAlloc.Jev.DependencyInjection` registers the client in one call, and keyed clients with their own options and `HttpClient`, over `IHttpClientFactory`
- [x] `JevClientOptions` bind from `IConfiguration` and fail fast on invalid values, validated at startup; retry and timeout settings are configurable this way
- [x] Source-generated `[LoggerMessage]` logging through `ILogger`, with no payload or key in logs and no cost without a logger
- [x] Spans and metrics for tokens, latency and confidence via ZeroAlloc.Telemetry, named per the GenAI conventions plus `jev.*`, nesting the `ZeroAlloc.Rest` span
- [x] Every phase adds `AllocationGate` budgets and benchmarks for what it ships, and existing budgets hold; the AOT smoke app exercises logging, telemetry and DI

### Phase 3.1: Logging [status: complete]
**Goal:** Source-generated `[LoggerMessage]` logging across `JevClient`, with constructor overloads that accept an `ILoggerFactory`.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-30-phase-3.1-logging-design.md`
**Plan:** `docs/superpowers/plans/2026-09-30-phase-3.1-logging.md`
**Completed:** 2026-09-30

### Phase 3.2: Telemetry [status: complete]
**Goal:** ZeroAlloc.Telemetry spans and metrics in the core package for tokens, latency and confidence, named per the GenAI conventions plus `jev.*`.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-01-phase-3.2-telemetry-design.md`
**Plan:** `docs/superpowers/plans/2026-10-01-phase-3.2-telemetry.md`
**Completed:** 2026-10-01

### Phase 3.3: DI package [status: complete]
**Goal:** `ZeroAlloc.Jev.DependencyInjection` with `AddJevClient(...)` and keyed clients over `IHttpClientFactory`, wiring logging and telemetry.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-01-phase-3.3-di-package-design.md`
**Plan:** `docs/superpowers/plans/2026-10-01-phase-3.3-di-package.md`
**Completed:** 2026-10-01

### Phase 3.4: Options and configuration [status: complete]
**Goal:** `IConfiguration` binding validated at startup with the core's own rules, with retry and timeout settings through configuration. Absorbs the roadmap's former Phase 3.5, configurable resilience (2026-09-30).
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-01-phase-3.4-options-configuration-design.md`
**Plan:** `docs/superpowers/plans/2026-10-01-phase-3.4-options-configuration.md`
**Completed:** 2026-10-01

## Milestone 4: Patterns & docs [status: complete]
**Goal:** A C# developer can learn Jev from its own docs site and apply TypeSafe's documented patterns in idiomatic C#, with thin allocation-free helpers, original runnable samples kept honest by replayed recordings, and a user guide served at jev.zeroalloc.net.
**Started:** 2026-10-02
**Completed:** 2026-10-03
**Design:** `docs/superpowers/specs/2026-10-02-milestone-4-design.md`
**Definition of Done:**
- [x] Pattern helpers ship in the core: a normalized Score value and a confidence-tier gate, allocation-free and exercised under Native AOT
- [x] Guides for fan-out, confidence routing, composite scoring and intent routing, with C# snippets that compile in CI
- [x] Original guardrails, intent-routing and re-ranking samples run as C# projects, live or from replayed recordings, and CI checks their decisions in replay mode
- [x] The user guide in `docs/` is served at jev.zeroalloc.net through ZeroAlloc-Net/.website
- [x] README carries the unofficial-client disclaimer and the logo, and links the site

### Phase 4.1: Pattern helpers and guides [status: complete]
**Goal:** Two allocation-free helpers, a normalized Score value and a confidence-tier gate with overridable defaults, plus guides for the four documented patterns with C# snippets that compile in CI.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-02-phase-4.1-pattern-helpers-design.md`
**Plan:** `docs/superpowers/plans/2026-10-02-phase-4.1-pattern-helpers.md`
**Completed:** 2026-10-02

### Phase 4.2: Cookbook samples [status: complete]
**Goal:** Original guardrails, intent-routing and re-ranking samples that run live or in replay mode from recorded OpenRouter answers, with CI checking their decisions in replay mode.
**Surface:** Docs
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-02-phase-4.2-cookbook-samples-design.md`
**Plan:** `docs/superpowers/plans/2026-10-02-phase-4.2-cookbook-samples.md`
**Completed:** 2026-10-02

### Phase 4.3: User guide [status: complete]
**Goal:** The user guide in `docs/`, in the org layout: getting started, every question type, typed evaluation and builders, DI and configuration, logging and telemetry, Native AOT, patterns and samples. Closes #16.
**Surface:** Docs
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-02-phase-4.3-user-guide-design.md`
**Plan:** `docs/superpowers/plans/2026-10-02-phase-4.3-user-guide.md`
**Completed:** 2026-10-02

### Phase 4.4: Docs site, logo and README [status: complete]
**Goal:** Register the repository in ZeroAlloc-Net/.website (`repos/jev` submodule and `apps/docs-jev` app) so `trigger-website.yml` publishes the guide to jev.zeroalloc.net; add a logo and slim the README to point at the site, keeping the disclaimer.
**Surface:** Docs
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-03-phase-4.4-docs-site-design.md`
**Plan:** `docs/superpowers/plans/2026-10-03-phase-4.4-docs-site.md`
**Completed:** 2026-10-03

## Milestone 5: 1.0 hardening [status: complete]
**Goal:** Harden the client before 1.0: a reviewed public API, benchmarks against hand-written .NET and TypeSafe's official JS and Python SDKs, a passing TypeSafe live run, and full-surface Native AOT verification. Re-scoped 2026-10-09: the 1.0 publish moved to Milestone 7, after the provider-neutral rework in Milestone 6 (`docs/superpowers/specs/2026-10-09-roadmap-design.md`).
**Started:** 2026-10-04
**Completed:** 2026-10-09
**Design:** `docs/superpowers/specs/2026-10-04-milestone-5-design.md`
**Definition of Done:**
- [x] Public API reviewed: sealing, naming, nullability and XML docs; #67, #23, #24 and #25 resolved; Telemetry 1.11.0 adopted (#85); #21 closed
- [x] Published benchmarks against a raw HttpClient + STJ client and the official JS and Python SDKs, on one local mock server
- [x] TypeSafe live suite passed once with a real key; `jev-latest` and `jev-preview` checked live
- [x] Full-surface AOT/trim verification green; #68, #73, #74 and #79 closed

### Phase 5.1: Public API review [status: complete]
**Goal:** Review and freeze the public surface (sealing, naming, nullability, XML docs), make the breaking changes #67, #23, #24 and #25, adopt Telemetry 1.11.0 (#85) and close #21.
**Surface:** Refactor
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-04-phase-5.1-public-api-review-design.md`
**Plan:** `docs/superpowers/plans/2026-10-04-phase-5.1-public-api-review.md`
**Completed:** 2026-10-04

### Phase 5.2: Benchmark suite [status: complete]
**Goal:** A raw .NET baseline in BenchmarkDotNet plus Node and Python harnesses running TypeSafe's official SDKs, all against one local mock server serving recorded Jev responses; results published.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-04-phase-5.2-benchmark-suite-design.md`
**Plan:** `docs/superpowers/plans/2026-10-04-phase-5.2-benchmark-suite.md`
**Completed:** 2026-10-04

### Phase 5.3: AOT, trim and measurement verification [status: complete]
**Goal:** Root both packages in a full-trim Native AOT publish with no IL2xxx or IL3xxx warnings, require the AOT smoke app to exercise every public entry point, fix the flaky allocation gates at their cause, and trace the allocation creep; closes #68, #73, #74 and #79.
**Surface:** Infra
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-04-phase-5.3-aot-and-measurement-verification-design.md`
**Plan:** `docs/superpowers/plans/2026-10-04-phase-5.3-aot-and-measurement-verification.md`
**Completed:** 2026-10-07

### Phase 5.4: Live and alias verification [status: complete]
**Goal:** Pass the TypeSafe live suite with a real key, record whether TypeSafe sends Retry-After and what its 422 body looks like, and check the `jev-latest` and `jev-preview` aliases live on both providers. Starts when the maintainer has a TypeSafe key.
**Surface:** Infra
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-09-phase-5.4-live-and-alias-verification-design.md`
**Plan:** `docs/superpowers/plans/2026-10-09-phase-5.4-live-and-alias-verification.md`
**Completed:** 2026-10-09
**Evidence:** Live smoke on main: probe [run 37913354273](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37913354273) (8/8), post-merge [run 37918889031](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37918889031) (12/13, OpenRouter rejects jev-preview, fixed in #111) and [run 37925900075](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/37925900075) (13/13). TypeSafe answers jev-latest and jev-preview with jev-1.13.0 and accepts jev-1.13.0 as a model; OpenRouter answers jev-latest as typesafe/jev-1.13-20260917 and has no jev-preview.

## Milestone 6: Provider-neutral core [status: active]
**Goal:** Rename to a vendor-neutral name and reshape the core around a neutral question model, an `IDecisionClient` and two protocol adapters, `/v1/systemone` and OpenAI's `/v1/decisions`, validated by a conformance suite, before any 1.0 API freeze.
**Started:** 2026-10-09
**Design:** `docs/superpowers/specs/2026-10-09-milestone-6-design.md`
**Definition of Done:**
- [ ] A vendor-neutral name is decided and applied to packages, namespaces, attributes, client and error types, analyzer IDs, the repository and the docs site; the fate of `ZeroAlloc.Jev` is decided
- [ ] The generator emits a provider-neutral question set; serialization lives in protocol adapters
- [ ] `IDecisionClient` with a builder pipeline carries retries, telemetry and logging
- [ ] The same question type runs against TypeSafe, OpenAI and a local `/v1/systemone` server by configuration only, with capability flags and typed errors for unsupported combinations
- [ ] A conformance suite passes against recorded TypeSafe and OpenAI fixtures
- [ ] A Providers docs page documents the presets and the capability matrix

### Phase 6.1: Rename to Minos and move out of the ZeroAlloc org [status: complete]
**Goal:** Rename to Minos (`Minos.NET` packages, `Minos` namespace, `MIN` analyzer IDs, the approved type-naming rule) and move the repository to `MarcelRoozekrans/Minos.NET`. Follow the maintainer's personal-library conventions: an in-repo Docusaurus site on GitHub Pages, an own logo, no ZeroAlloc-org workflows. The README explains the name. No behaviour change (#120).
**Surface:** Refactor
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-09-phase-6.1-rename-design.md`
**Impact analysis:** `docs/plans/2026-10-09-phase-6.1-rename-impact-analysis.md`
**Plan:** `docs/superpowers/plans/2026-10-09-phase-6.1-rename-to-minos.md`
**Completed:** 2026-10-10

### Phase 6.2: Neutral question model and adapter boundary [status: complete]
**Goal:** The generator emits a provider-neutral question-set description; `/v1/systemone` serialization moves behind a protocol-adapter boundary, with no behaviour change (#115).
**Surface:** Refactor
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-10-phase-6.2-neutral-question-model-design.md`
**Impact analysis:** `docs/plans/2026-10-10-phase-6.2-neutral-question-model-impact-analysis.md`
**Plan:** `docs/superpowers/plans/2026-10-10-phase-6.2-neutral-question-model.md`
**Completed:** 2026-10-10

### Phase 6.3: IDecisionClient abstraction and pipeline [status: complete]
**Goal:** An `IDecisionClient` following Microsoft.Extensions.AI conventions, with a builder pipeline in which retries, telemetry and logging become stages (core of #119). The client chooses its protocol, and the parts Phase 6.2 left outside the seam move behind it: the endpoint path in `IDecisionApi`, finding `answers` in the response envelope, telemetry's `ResponseFields` and `ConfidenceValues`, `DecisionErrorMapper`, and the typed path of the default `IDecisionClient` interface methods, which still builds a raw `SystemOneRequest` for clients other than `DecisionClient`.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-10-phase-6.3-decision-client-pipeline-design.md`
**Plan:** `docs/superpowers/plans/2026-10-10-phase-6.3-decision-client-pipeline.md`
**Completed:** 2026-10-11

### Phase 6.4: /v1/systemone adapter [status: pending]
**Goal:** Configurable base URL and model with presets for TypeSafe, OpenRouter, Cloudflare Clef, vLLM-SR and local servers; capability flags with typed errors naming provider and question; optional auth; keyed DI clients (#115).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 6.5: OpenAI /v1/decisions adapter and image state [status: pending]
**Goal:** An adapter for OpenAI's Decisions API, its schema and question-type mapping verified against OpenAI's docs, and typed state that can carry images where a provider supports it (#115).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 6.6: Conformance suite [status: pending]
**Goal:** Checks any endpoint's request and response shapes, multi-question requests, distributions, error shapes and model reporting, green against recorded TypeSafe and OpenAI fixtures (#116, conformance part).
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_

### Phase 6.7: Providers docs and samples [status: pending]
**Goal:** A Providers page with presets and the capability matrix, and a sample running one question type against three providers by configuration only.
**Surface:** Docs
**HelpWanted:** no
**Plan:** _to be written_

## Milestone 7: 1.0 release [status: pending]
**Goal:** Review, verify and publish the provider-neutral library as 1.0.0 on NuGet, with api-compat guarding every later change.
**Design:** `docs/superpowers/specs/2026-10-09-roadmap-design.md`
**Definition of Done:**
- [ ] The renamed public API is reviewed and `PublicAPI.Shipped.txt` describes 1.0.0
- [ ] Live runs pass against TypeSafe, OpenRouter, OpenAI and a local `/v1/systemone` server
- [ ] Native AOT and trim verification covers the whole public API with no warnings; every allocation budget is unchanged or tightened
- [ ] 1.0.0 is on NuGet through release-please and the publish job (#29), the guide states its version, and api-compat guards later changes (#28)

### Phase 7.1: Public API review of the renamed surface [status: pending]
**Goal:** Review sealing, naming, nullability and XML docs of the provider-neutral surface before it freezes.
**Surface:** Refactor
**HelpWanted:** no
**Plan:** _to be written_

### Phase 7.2: Live verification across providers [status: pending]
**Goal:** Pass live suites against TypeSafe, OpenRouter, OpenAI and a local `/v1/systemone` server.
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_

### Phase 7.3: AOT, trim and benchmark re-verification [status: pending]
**Goal:** Re-run the whole-surface AOT/trim check and the benchmarks on the new API; no allocation budget loosened.
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_

### Phase 7.4: Publish 1.0 [status: pending]
**Goal:** Publish 1.0.0 the way Thalos.NET, Rag.NET and AdoNet.Async do: GitVersion, release-please, and NuGet Trusted Publishing from `ci.yml`'s `publish-nuget` job, rehearsed against a local feed. Reuse what still applies from the paused Phase 5.5 branch, such as the package inspection. Then turn on api-compat (#29, #28), and add the NuGet links to the docs site's navbar and footer once the packages exist.
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_

## Milestone 8: Choosing a provider and a threshold [status: pending]
**Goal:** A dotnet tool that measures accuracy, calibration, selective-prediction coverage, latency and cost per provider on a labeled dataset, and docs that use it to choose providers and thresholds.
**Design:** `docs/superpowers/specs/2026-10-09-roadmap-design.md`
**Definition of Done:**
- [ ] A documented JSONL dataset format of state, questions and ground truth
- [ ] Reports with accuracy, ECE and reliability per question kind, a coverage/accuracy threshold sweep, latency and cost
- [ ] A calibration report for at least two providers on a public sample dataset
- [ ] A "Choosing a provider and a threshold" guide backed by the tool

### Phase 8.1: Labeled dataset format and loader [status: pending]
**Goal:** The JSONL dataset format, shared later by the decision export, and its loader (#116).
**Surface:** Data
**HelpWanted:** no
**Plan:** _to be written_

### Phase 8.2: Calibration metrics and report [status: pending]
**Goal:** Accuracy, ECE, reliability diagrams, the threshold sweep, latency and cost, as markdown and JSON reports (#116).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 8.3: Dotnet tool packaging [status: pending]
**Goal:** One dotnet tool for conformance and calibration against any configured provider (#116).
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_

### Phase 8.4: Choosing a provider and a threshold guide [status: pending]
**Goal:** A guide backed by the tool's reports on a public sample dataset.
**Surface:** Docs
**HelpWanted:** no
**Plan:** _to be written_

## Milestone 9: Hosted to local [status: pending]
**Goal:** Record decisions and outcomes, export them in an open format, shadow-compare a candidate provider, and serve decisions in-process from .NET if the inference spike says go.
**Design:** `docs/superpowers/specs/2026-10-09-roadmap-design.md`
**Definition of Done:**
- [ ] An opt-in decision recorder with outcome labels and redaction hooks, and a JSONL export with a documented schema
- [ ] A shadow-mode report between two providers: agreement, calibration and threshold sweep
- [ ] A go/no-go write-up for in-process inference with numbers and licence notes; on a go, a local provider that answers Noul, Choice and Score in-process
- [ ] A "Moving decisions to a cheaper or local model" guide

### Phase 9.1: Decision recorder and outcome labels [status: pending]
**Goal:** An opt-in recorder decorating `IDecisionClient`, storing state, questions, answers, provider, model version and request id, with an API to attach outcomes later (#118).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 9.2: JSONL export and redaction hooks [status: pending]
**Goal:** Export in the state/questions record shape open models train on, matching Milestone 8's dataset format, with PII redaction hooks (#118).
**Surface:** Data
**HelpWanted:** no
**Plan:** _to be written_

### Phase 9.3: Shadow mode and its report [status: pending]
**Goal:** Run a candidate provider alongside the primary without acting on it, reporting agreement, calibration and a threshold sweep with Milestone 8's report code (#118).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 9.4: Spike: in-process decision-model inference [status: pending]
**Goal:** ONNX Runtime from .NET with a decision head and calibration in C#, on a small permissive model, measured for latency, allocations, AOT size and CPU vs GPU; go/no-go write-up. Has no API dependency and may run earlier (#117).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 9.5: Local provider package [status: pending]
**Goal:** On a go from 9.4, an in-process provider so existing typed question sets run unchanged, measurable by the Milestone 8 tool (#117).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 9.6: Moving decisions to a cheaper or local model guide [status: pending]
**Goal:** The record, export, shadow and switch path as one guide (#118).
**Surface:** Docs
**HelpWanted:** no
**Plan:** _to be written_

## Milestone 10: Escalation [status: pending]
**Goal:** Fast decisions first, escalating on low confidence to another provider or an `IChatClient`, with a provider fallback chain, as `IDecisionClient` middleware.
**Design:** `docs/superpowers/specs/2026-10-09-roadmap-design.md`
**Definition of Done:**
- [ ] Escalation middleware with per-question thresholds that records which path answered
- [ ] A provider fallback chain composing with DI and keyed clients
- [ ] A ticket-triage sample with local-first decisions, escalation on low confidence and metrics showing the split
- [ ] A guide turning confidence routing into a framework feature, and a proposal discussion opened on dotnet/extensions

### Phase 10.1: Escalation middleware [status: pending]
**Goal:** Per-question thresholds sized to the cost of being wrong; fall back to another decision provider or an `IChatClient` with structured output; record the path (#119).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 10.2: Provider fallback chain [status: pending]
**Goal:** For example local, then Clef, then OpenAI, composing with DI and keyed clients (#119).
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 10.3: Ticket-triage sample [status: pending]
**Goal:** Local-first decisions with escalation on low confidence and metrics showing the split (#119).
**Surface:** Mixed
**HelpWanted:** no
**Plan:** _to be written_

### Phase 10.4: Confidence routing as a framework feature [status: pending]
**Goal:** Turn the confidence-routing pattern guide into a framework feature, and open a discussion on dotnet/extensions proposing the abstraction (#119).
**Surface:** Docs
**HelpWanted:** no
**Plan:** _to be written_
