# Milestone 6: Provider-neutral core

**Status:** active
**Started:** 2026-10-09
**Design:** `docs/superpowers/specs/2026-10-09-milestone-6-design.md`

## Goal
One typed question set runs against TypeSafe, OpenRouter, OpenAI and a local `/v1/systemone` server by configuration alone, under a vendor-neutral name and behind an `IDecisionClient`. The library stops being a TypeSafe Jev client and becomes a provider-neutral decision client. Its public API reaches the shape that Milestone 7 reviews and freezes. Nothing is published in this milestone.

## Definition of Done
- [ ] All planned phases complete.
- [ ] All tests pass: unit, generator, analyzer, DI, docs, integration, pack, samples in replay mode, and the AOT smoke and surface checks.
- [ ] The name decision is recorded in `docs/planning/`.
  - No public type, namespace, package id or analyzer ID carries the vendor name "Jev". The only exception is a deliberate `ZeroAlloc.Jev` convenience or redirect package, if Phase 6.1 decides on one.
  - The repository and docs site have moved to the new name, or the maintainer's remaining actions are listed in STATE.md.
- [ ] The generator's output contains no `/v1/systemone` wire JSON; serialization lives only in protocol adapters. A test serializes one question set for both protocols.
- [ ] `IDecisionClient` is the client abstraction, with retries, telemetry and logging as pipeline stages. Every allocation budget on the `/v1/systemone` path is unchanged or tightened.
- [ ] Both adapters have recorded-fixture tests. An unsupported question kind, or image state on a provider without image input, fails with a typed error naming the provider and the question, never as a raw HTTP failure.
- [ ] Live runs pass against TypeSafe, OpenRouter and OpenAI, and against one local `/v1/systemone` server, either in CI or documented as run.
- [ ] The conformance suite runs green against recorded TypeSafe and OpenAI fixtures.
- [ ] A Providers docs page shows the presets and the capability matrix. A sample switches providers by configuration only.

## Phases
1. Phase 6.1 — Rename to Minos and move out of the ZeroAlloc org [complete]
2. Phase 6.2 — Neutral question model and adapter boundary [complete]
3. Phase 6.3 — IDecisionClient abstraction and pipeline [complete]
4. Phase 6.4 — /v1/systemone adapter [pending]
5. Phase 6.5 — OpenAI /v1/decisions adapter and image state [pending]
6. Phase 6.6 — Conformance suite [pending]
7. Phase 6.7 — Providers docs and samples [pending]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
