# Changelog

## [0.7.0](https://github.com/MarcelRoozekrans/Minos.NET/compare/v0.6.0...v0.7.0) (2026-10-10)


### ⚠ BREAKING CHANGES

* **core:** replace the systemone literal and parser with the neutral definition
* **core:** IQuestionSet drops QuestionsUtf8 and Parse for a static Definition and a static Create over AnswerSlots, QuestionSet drops QuestionsUtf8 for Definition, AnswerReader is removed, DecisionOptionSet drops IndexOfKey, MIN102 reserves the property names Definition and Create instead of Parse and QuestionsUtf8, and an empty Key on a question or a Criteria attribute is now the build error MIN106 instead of an empty wire key.

### Features

* **core:** add a provider-neutral question set definition that generated and built sets expose ([b919ba3](https://github.com/MarcelRoozekrans/Minos.NET/commit/b919ba3c061df451ee24aec511e4c953bf0fc7f7))
* **core:** let a criterion be read back from a definition ([b919ba3](https://github.com/MarcelRoozekrans/Minos.NET/commit/b919ba3c061df451ee24aec511e4c953bf0fc7f7))


### Bug Fixes

* **deps:** move pydantic_core with pydantic so the benchmark requirements install ([23af2d6](https://github.com/MarcelRoozekrans/Minos.NET/commit/23af2d6cbb2f4f97ac21cb89ff1ae61176717e25))


### Code Refactoring

* **core:** replace the systemone literal and parser with the neutral definition ([b919ba3](https://github.com/MarcelRoozekrans/Minos.NET/commit/b919ba3c061df451ee24aec511e4c953bf0fc7f7))

## [0.6.0](https://github.com/MarcelRoozekrans/Minos.NET/compare/v0.5.3...v0.6.0) (2026-10-10)


### ⚠ BREAKING CHANGES

* **core:** the package ids are now Minos.NET and Minos.NET.DependencyInjection, the namespace is Minos, and the public types follow the Minos naming rule: Questions, QuestionSet, Answer, Criterion, IDecisionClient, DecisionClient, DecisionError, AddDecisionClient. Analyzer rule ids JEV0xx become MIN0xx, the runtime builder failure is MIN108, and the analyzer category is Minos. The telemetry source and meter are Minos with minos.* attribute keys, the logger category is Minos.DecisionClient with log messages prefixed Minos, and the HttpClient name and User-Agent are Minos.NET. The DecisionContent exception texts, the generated hint names *.Questions.g.cs and the __minos_ field prefix, and the project and docs URLs change with it.

### Code Refactoring

* **core:** rename ZeroAlloc.Jev to Minos ([d74c0d6](https://github.com/MarcelRoozekrans/Minos.NET/commit/d74c0d61a1443ae28e6b7d68f23f0a07e2562ba4))

## [0.5.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.5.2...v0.5.3) (2026-10-09)


### Tests

* **tests:** pin that OpenRouter rejects jev-preview, and document it ([6f97863](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/6f97863477b7f05f4108536b53d701c49eb34cf4))

## [0.5.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.5.1...v0.5.2) (2026-10-09)


### Documentation

* **docs:** add the phase 5.4 design and implementation plan from a first live run against TypeSafe ([38b92b8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/38b92b850151a38526a3d15bea055761c3c082ac))
* **docs:** record that TypeSafe documents no Retry-After and that versioned model ids are accepted ([38b92b8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/38b92b850151a38526a3d15bea055761c3c082ac))
* **docs:** show the 422 body TypeSafe really sends and how to read its problems ([38b92b8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/38b92b850151a38526a3d15bea055761c3c082ac))


### Tests

* **tests:** assert the live 422 shape instead of throwing when it changes ([38b92b8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/38b92b850151a38526a3d15bea055761c3c082ac))
* **tests:** check the jev-latest and jev-preview aliases live on both providers ([38b92b8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/38b92b850151a38526a3d15bea055761c3c082ac))
* **tests:** pin the 422 and 401 bodies TypeSafe really sends ([38b92b8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/38b92b850151a38526a3d15bea055761c3c082ac))

## [0.5.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.5.0...v0.5.1) (2026-10-07)


### Bug Fixes

* **benchmarks:** restore a pydantic_core that pydantic accepts, and stop Renovate updating it on its own ([5e72229](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5e72229d5edb854e7f7b3aba4e743b3c4401dff7))
* **samples:** measure the allocation gates without the pool refill their forced collection caused ([5e72229](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5e72229d5edb854e7f7b3aba4e743b3c4401dff7))


### Documentation

* **docs:** describe the whole-assembly AOT check and the entry-point coverage rule ([5e72229](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5e72229d5edb854e7f7b3aba4e743b3c4401dff7))
* **docs:** trace the allocation creep since Phase 3 to the measuring loop, not the library ([5e72229](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5e72229d5edb854e7f7b3aba4e743b3c4401dff7))
* **roadmap:** split live and alias verification into Phase 5.4 and move the 1.0 release to Phase 5.5 ([5e72229](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5e72229d5edb854e7f7b3aba4e743b3c4401dff7))


### Tests

* **samples:** require the AOT smoke app to exercise every public entry point, default interface methods included ([5e72229](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5e72229d5edb854e7f7b3aba4e743b3c4401dff7))
* **tests:** measure the zero-allocation unit tests through AllocationGate ([5e72229](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5e72229d5edb854e7f7b3aba4e743b3c4401dff7))

## [0.5.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.4.0...v0.5.0) (2026-10-04)


### Features

* **benchmarks:** add reusable comparison runners, a physical-core split and a merge script that turns every harness's results into Markdown tables ([c3382a5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c3382a582c1558dccf33f0cfdd1902c90edbe6e5))
* **benchmarks:** compare ZeroAlloc.Jev with a hand-written HttpClient client, JevSharp, TypeSafe.AI.Sdk, Jev.Net and TypeSafe's official JS and Python SDKs against one local Kestrel mock ([c3382a5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c3382a582c1558dccf33f0cfdd1902c90edbe6e5))


### Bug Fixes

* **core:** read the package version from the release manifest, mark builds that are not releases as -local, and fail the build when the manifest has no semantic version ([c3382a5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c3382a582c1558dccf33f0cfdd1902c90edbe6e5))


### Documentation

* **docs:** publish the client comparison from three CI runs in the performance guide ([c3382a5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c3382a582c1558dccf33f0cfdd1902c90edbe6e5))


### Dependencies

* **deps:** adopt ZeroAlloc.Rest 3.2.1, which drops about 384 B of per-call transport allocations, and tighten every Native AOT allocation budget to match ([c3382a5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c3382a582c1558dccf33f0cfdd1902c90edbe6e5))

## [0.4.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.3.3...v0.4.0) (2026-10-04)


### ⚠ BREAKING CHANGES

* **generator:** drop the analyzer rules JEV108 and JEV109; the run-time builder still reports JEV108
* **core:** construct JevError with a kind and a message, and set StatusCode, RetryAfter, Detail and Exception as init properties
* **core:** number JevErrorKind from 1 so default is no kind, and add JevErrorKind.Disposed
* **core:** remove the builder's keyed Choice, keyed Score and enum Score overloads that have no configurator
* **core:** rename NoulAttribute.True and False to WhenTrue and WhenFalse
* **core:** rename the client's ct parameters to cancellationToken
* **core:** remove Json = true from the question-set attributes; build structured JSON sets at run time with JevContent and JevCriterion.Json

### Features

* **core:** construct JevError with a kind and a message, and set StatusCode, RetryAfter, Detail and Exception as init properties ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))
* **core:** give Noul value equality and hide JevOptionSet from IntelliSense ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))
* **core:** number JevErrorKind from 1 so default is no kind, and add JevErrorKind.Disposed ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))
* **core:** remove Json = true from the question-set attributes; build structured JSON sets at run time with JevContent and JevCriterion.Json ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))
* **core:** remove the builder's keyed Choice, keyed Score and enum Score overloads that have no configurator ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))
* **core:** rename NoulAttribute.True and False to WhenTrue and WhenFalse ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))
* **core:** rename the client's ct parameters to cancellationToken ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))
* **core:** set error.type to the exception's full type name on a thrown call's span, without its message, with ZeroAlloc.Telemetry 1.11.0 ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))
* **generator:** drop the analyzer rules JEV108 and JEV109; the run-time builder still reports JEV108 ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))


### Bug Fixes

* **core:** return Disposed for a call in flight when its client is disposed, and never retry once disposed ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))


### Documentation

* **docs:** document disposal, the error kinds and JevError construction, and the public API review for 1.0 ([f622ff5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f622ff57565ac7c91f60327336acd67f4f705b52))

## [0.3.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.3.2...v0.3.3) (2026-10-03)


### Documentation

* **docs:** put the guide on jev.zeroalloc.net (Phase 4.4) ([#88](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/issues/88)) ([a4676d6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a4676d6b572518fa015e6d9cd047edaeaec46a0c))

## [0.3.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.3.1...v0.3.2) (2026-10-02)


### Documentation

* **docs:** add the user guide: getting started, question types, typed evaluation, run-time question sets, client and errors, dependency injection, observability, Native AOT, diagnostics, testing your code, patterns, samples and performance ([5326126](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5326126ec5ac25de3ad0c682c96c31d5242a1cbb))


### Tests

* **docs:** check the guide's front matter, internal links and anchors, next steps, and the tables and figures it copies from the code ([5326126](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5326126ec5ac25de3ad0c682c96c31d5242a1cbb))

## [0.3.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.3.0...v0.3.1) (2026-10-02)


### Documentation

* **samples:** add guardrails, intent-routing and re-ranking cookbook samples that run live, record from OpenRouter, or replay checked-in answers offline ([a163615](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a163615be4f76aeab75490ed795fa9f3a460c608))


### Tests

* **samples:** replay every cookbook sample in CI, pin its recorded decisions, snapshot its report and scan the recordings for keys ([a163615](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a163615be4f76aeab75490ed795fa9f3a460c608))

## [0.3.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.2.0...v0.3.0) (2026-10-02)


### Features

* **core:** add ConfidenceThresholds and ConfidenceTier, which place an answer's confidence in a Low, Medium or High tier without allocating ([55a85db](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/55a85db040b636501be297ed84ee6c8b1cb17309))
* **core:** add Normalized to Score and KeyedScore, putting the expected level on a 0 to 1 scale ([55a85db](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/55a85db040b636501be297ed84ee6c8b1cb17309))


### Documentation

* **docs:** add guides for speculative fan-out, confidence routing, composite scoring and intent routing, with C# snippets that compile and run in CI ([55a85db](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/55a85db040b636501be297ed84ee6c8b1cb17309))


### Tests

* **samples:** gate ConfidenceThresholds.Classify and Normalized at 0 B under Native AOT ([55a85db](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/55a85db040b636501be297ed84ee6c8b1cb17309))

## [0.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/compare/v0.1.0...v0.2.0) (2026-10-01)


### Features

* **core:** add JevClient constructors that take an ILoggerFactory; a null factory logs nothing, and a constructor call with two null literals no longer compiles because it is ambiguous ([b1709a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b1709a2fe2819231b2c70aa9f6f8d30d36fd609a))
* **core:** add JevClientOptions.Validate ([12ef375](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/12ef375a1ea9a26f4c53f85295d11ffee8ff117a))
* **core:** add JevCriterion for structured option and level descriptions ([78874a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/78874a23103562b4bd38dbd8a6931c2ec26ee066))
* **core:** add the instrumented operations interface ([b118b5c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b118b5c83158d9aa3c6d83a44811b9d2d8e29c3a))
* **core:** add the telemetry names and the deferred response reads ([b118b5c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b118b5c83158d9aa3c6d83a44811b9d2d8e29c3a))
* **core:** build question sets at run time with JevQuestionSet.CreateBuilder ([78874a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/78874a23103562b4bd38dbd8a6931c2ec26ee066))
* **core:** check built question sets against the analyzers' rules and fail with the new JevErrorKind.InvalidQuestions ([78874a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/78874a23103562b4bd38dbd8a6931c2ec26ee066))
* **core:** configure a factory-created httpclient as the client configures its own ([d1c0f53](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d1c0f53eed2dd6b7d23bcfda3e0703f77a592fed))
* **core:** evaluate built question sets through IJevClient and JevClient ([78874a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/78874a23103562b4bd38dbd8a6931c2ec26ee066))
* **core:** log each evaluation, model listing, retried attempt and unexpected exception through ILogger, with source-generated LoggerMessage events 1001 to 1006 that never carry the state, questions, answers, API key, header values or error body ([b1709a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b1709a2fe2819231b2c70aa9f6f8d30d36fd609a))
* **core:** read a built set's answers through typed handles with JevAnswers ([78874a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/78874a23103562b4bd38dbd8a6931c2ec26ee066))
* **core:** trace and measure every client operation ([b118b5c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b118b5c83158d9aa3c6d83a44811b9d2d8e29c3a))
* **di:** bind AddJevClient options from IConfiguration ([12ef375](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/12ef375a1ea9a26f4c53f85295d11ffee8ff117a))
* **di:** register keyed clients with their own options and httpclient ([d1c0f53](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d1c0f53eed2dd6b7d23bcfda3e0703f77a592fed))
* **di:** register the default client over ihttpclientfactory ([d1c0f53](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d1c0f53eed2dd6b7d23bcfda3e0703f77a592fed))
* **di:** validate AddJevClient options at startup ([12ef375](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/12ef375a1ea9a26f4c53f85295d11ffee8ff117a))


### Bug Fixes

* **core:** reject a timeout httpclient cannot hold before configuring anything ([d1c0f53](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d1c0f53eed2dd6b7d23bcfda3e0703f77a592fed))
* **core:** require the legend on score answers, as the TypeSafe API does, so every evaluation path rejects a Score answer without it ([3a43c88](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/3a43c88567c805343a3862bda6caedbc0651bdce))


### Documentation

* **docs:** document configuration binding and startup validation ([12ef375](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/12ef375a1ea9a26f4c53f85295d11ffee8ff117a))
* **docs:** document dependency injection ([d1c0f53](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d1c0f53eed2dd6b7d23bcfda3e0703f77a592fed))
* **docs:** document the client's telemetry ([b118b5c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b118b5c83158d9aa3c6d83a44811b9d2d8e29c3a))
* document question sets built at run time ([78874a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/78874a23103562b4bd38dbd8a6931c2ec26ee066))
* document the client's log events, levels, privacy and cost ([b1709a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b1709a2fe2819231b2c70aa9f6f8d30d36fd609a))


### Dependencies

* add Microsoft.Extensions.Logging.Abstractions 10.0.0 as a runtime dependency ([b1709a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b1709a2fe2819231b2c70aa9f6f8d30d36fd609a))
* add ZeroAlloc.Validation 2.0.3 as a runtime dependency ([78874a2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/78874a23103562b4bd38dbd8a6931c2ec26ee066))

## 0.1.0 (2026-09-28)


### Features

* **core:** add JevAnswerReader for typed noul, choice and score answers ([70be00d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/70be00d5f4dcc3eb3caf8df8162b97bd1dffa868))
* **core:** add JevClient for typesafe and openrouter over zeroalloc.rest ([981f2e0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/981f2e0c8c6ac313678684d6fea77a00d2bacd92))
* **core:** add JevClientOptions and resolve settings from the environment ([163d64f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/163d64fcc6bfbb384df6d61673a2724c5b087e8d))
* **core:** add jevcontent factories for typed values and utf-8 json ([19948f6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/19948f61bd9b1561316745aa5b9641ebd4cedbec))
* **core:** add JevContent for text or structured JSON values ([3272a0e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/3272a0e31a60c7cee6b289a750f0d8f6c0760b1d))
* **core:** add JevError and map zeroalloc.rest failures to it ([da3dfa5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/da3dfa5e8591133972d88061fe0c9f736c0bbe21))
* **core:** add openrouter response fields and provider defaults ([9104e80](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/9104e805ed4e2518452fa215e7ca32437a80e449))
* **core:** add question generator project with snake_case and json text helpers ([ebc3067](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/ebc30674a50012391023c615128c748b57cd834a))
* **core:** add request wire model and json context ([c67ee13](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c67ee13f6b77982a94124a19c5467441a76e6ebe))
* **core:** add response and models wire model ([21c7d39](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/21c7d39da603c9b9512868234b634f85a9665b84))
* **core:** add retry options with the official sdk's defaults ([a18eeb7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a18eeb7577eca067f8999a10a3513a6801299a1b))
* **core:** add typed answer types, option sets and question attributes ([1103f29](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/1103f295bee972a792bff718cebe7a5da6c32c37))
* **core:** add typed EvaluateAsync overloads to IJevClient ([a19e50b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a19e50bf5a7e4b3e10b3d2f790945bd9ae6095ca))
* **core:** evaluate typed question sets over a raw zero-allocation path ([2b9b9a8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/2b9b9a8c88779778fcc8dd9df7cb61dec1288858))
* **core:** generate question json and typed answer parsers for jevquestions types ([97148ba](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/97148bae0fbe74ddd12bfd95db425cc4a3773202))
* **core:** retry transient failures with backoff and retry-after ([6f3a2ce](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/6f3a2ceb5c219c677d8b870a3638834f60e9995a))
* **core:** retry transient failures with backoff and retry-after (phase 1.6) ([7d3dce6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/7d3dce6b1d46c5a1a4fe75de36e57b78609e7500))
* **core:** scaffold Jev.Net library and test project ([0b888af](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/0b888af43614d3c0cef177cc3d46d85c831df6a2))
* **core:** send X-TypeSafe-Retry-Count on retries ([df84932](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/df84932a7f319ddca1305f229f914f30eb6dd628))
* **generator:** add a strict json minifier for attribute text ([6be6780](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/6be67803e1cdc471a3812ddb49f2d3e3d8c6e2d1))
* **generator:** add code fixes that describe choice options and score levels ([a886c8f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a886c8f1ea820c365c3c1ac937980486d5c60514))
* **generator:** check json text and example entries for empty text and state names ([59c3774](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/59c3774d96beefe158b52ea90f6fef5eaac89d5a))
* **generator:** enforce the jev api rules at compile time ([be56b65](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/be56b656934cc518a0b15ce0a90633e110f31b23))
* **generator:** link question sets to a typed state with State ([deeb63d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/deeb63d37900406d3ecae680be663de6591a285f))
* **generator:** send examples and not-for as a criterion object ([226e2ed](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/226e2ed95eedc013589c719d8f81c67ad8d9d460))
* **generator:** send json instructions and descriptions checked at compile time ([5c66c95](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5c66c954d62e38b3b6646bf3f362f5184d89c75c))
* **generator:** stub the question properties of an invalid set ([c3c77ac](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c3c77ac6c790433b97978f5e4d3a8574d9d9f67b))


### Bug Fixes

* **core:** accept padded typed states and size raw reads sensibly ([06e0c15](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/06e0c159ecc0e01e299a304e8b066966fa5b9ef3))
* **core:** compare probability maps by value and stop boxing on the error path ([b99ca77](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b99ca77b7faf3162d24d5ea1366fdaa10ad3536c))
* **core:** compile generated code for empty enums, keyword names and separator keys ([8124da9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/8124da982d89001b24041d6e1b2b2c9693ce88ac))
* **core:** document retry behaviour on options and error kinds ([4ad2503](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/4ad250346ce21fcd0c8ca02ee0ec96b9b860461e))
* **core:** drop the per-thread answers buffer and document mocking of typed overloads ([184165e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/184165e1d03b9d36c793e2e52da6344cce750f3d))
* **core:** name the option parameter and stop boxing in Choice/Score equality ([e3343e4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/e3343e43098bc422222a290c1b2d9604ebd33f1d))
* **core:** pack the generator from its real build output ([452f08b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/452f08bfac9f8a7e3ef02915b7a17f531960ec7c))
* **core:** parse every retry-after form and the retry-after-ms header ([def22d6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/def22d66bb63c847077cbb93ca27169ee44003c2))
* **core:** reject base addresses with a query or fragment ([8a4c151](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/8a4c1518944cbbd18f8976d1dea6f7df8b9e4beb))
* **core:** reject non-finite retry-after-ms and fix delta-seconds and RFC 850 year edge cases ([6358e11](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/6358e110e7230bedf7a61be0e425371d5692b568))
* **core:** reject nulls and non-content json in the wire model ([c6df3ec](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c6df3ecadd4c91f95a30271954cc07cdafac07bb))
* **core:** share the declined-exception unwrap and make the retry tests deterministic ([fe27e52](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/fe27e52ba4e8f5d61287e4ea3a19ffff1b812ee2))
* **core:** skip a leading UTF-8 BOM and check arguments before disposal ([fef35fb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/fef35fb1a5af0943be705efd013a6e81e6946258))
* **core:** use constructor overloads without optional parameters and track hlq001 upstream ([2a9f4db](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/2a9f4db9c528731b74c252b7b3e71a5f543ec244))
* **core:** validate borrowed and configured base addresses, scope TYPESAFE_BASE_URL, and harden keys ([7d2504d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/7d2504df0e02a6c00ae6a7956a8ab6fd26791d86))
* **generator:** accept array state types in [JevQuestions(State = ...)] ([a6a7d89](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a6a7d89fb623e1cdb9f54fe58882fd598bf0a8cc))
* **generator:** cap json text nesting at 60 levels to fit the request ([3420b19](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/3420b1943f2901b8465ebca9f2a8473d3a547b80))
* **generator:** catch colliding, modified, file-local and required-member question sets ([1a79b67](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/1a79b675975d990a724e3420a624ce7263469e10))
* **generator:** escape non-ASCII characters in CSharpLiteral ([d0c6cae](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d0c6cae3a04bf53615c720f8fcc69b49b7923679))
* **generator:** find required members inherited from a base type ([8924a7f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/8924a7fb2a05eea311a4abf0ecec3486301d0e3d))
* **generator:** give jev003 advice that fits the empty text ([9d4d538](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/9d4d5384b7f5d0d5f665228b8982314eb815456c))
* **generator:** keep the description code fix compiling and in the file's style ([8ab6781](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/8ab6781dfbb90c7feab428ba89be286b8b9a8c19))
* **generator:** name every jev101 shape rule in its message ([05229ac](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/05229ac7171410345b77d1f6852459fd479f7ed4))
* **generator:** reject static State types and round out State test coverage ([876f122](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/876f122c4720f3c56e0facd4e19fc03393164dfb))
* **generator:** report enum rules from the enum so the ide shows them ([2fda78d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/2fda78d4c1716d6c9c3cd49d12ff6ca867eefa43))
* **generator:** send a lone surrogate as the replacement character ([a6943b4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a6943b4fce260bf941d2cd4f4c68376b07bd81d0))
* **generator:** state the reproduced importadder defects and split the jev003 message test ([861b359](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/861b3590a39baf28765aa927b891b77f66d873de))
* **generator:** stub every partial question property of an invalid set ([8cca886](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/8cca886ff8508194d32779106db201d00cb77f74))
* **generator:** stub jev101 sets a partial part can complete and report in generated code ([f1191d1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f1191d1bef184868adf8a1c9125d62bc8482d2cb))


### Code Refactoring

* **core:** keep the single-value json check inside jevcontent ([476a358](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/476a358325d1e9ef0ab93924a1d4f8b2a82795e1))
* **core:** make wire model types classes and close the hierarchies ([7aed6d8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/7aed6d8428e2da4aa16d5589ae08d6fd5a6b72fb))
* **core:** rename jev.net to zeroalloc.jev ([79b0f13](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/79b0f138fe2661beb443e1e9c977e7f330a0fb28))
* **core:** rethrow declined exceptions through RethrowDeclined ([cc7e904](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/cc7e9040b5c61c2242ef75dac0ae7cd5809a228d))
* **generator:** keep rule descriptors out of the generator ([156e6a8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/156e6a83f7760a52447872ef687408de9b2e02f8))
* **generator:** report question-set diagnostics from an analyzer ([8aaf43e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/8aaf43e516b95c5b63d8a3ab8b7cb224305818cb))


### Documentation

* add upstream zeroalloc gap analysis with filed issues ([60a0e3f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/60a0e3f8e97efb1978abf5ad658acb6773dc7098))
* bring planning docs up to date for phase 1.5 ([e0837a9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/e0837a9ff6616e61e4ce1915e5c3dba1543ba1d6))
* correct the readme diagnostics section ([2875c5d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/2875c5d93c11606a72028c263ddd4b54c3396913))
* describe zeroalloc.jev in the readme and planning docs ([cf41625](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/cf416259712af920aeb062215d458fd8a557f8d8))
* document structured criteria, json text and the content factories ([5aed924](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5aed9245f3fa5c04f83df48e1ac3e9ee1061ae2c))
* document the minimum toolchain and trigger the org website on docs changes ([c6195f3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c6195f3a974bc89aff98d73e427b4a6199fe09e8))
* document typed evaluation in the README ([96d0d4a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/96d0d4a9b6d6e2bc0391a753c3610837150858d4))
* explain the JEV_LIVE=1 opt-in for live tests ([2ac15d7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/2ac15d72a84bcff786285c767137514a50c544a7))
* fix the typed-evaluation README snippets and wording ([0f6adf1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/0f6adf1249443bc541847d3577b7f99f47eba216))
* **generator:** link the importadder workarounds to their tracking issue ([434b23c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/434b23c581465ef883378fc599a960b9dca0ba1b))
* link phase 1.3 and 1.4 follow-ups to their issues ([e5cd956](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/e5cd956d5c771e6531585aa20e40783b07c091db))
* place per-request data in state and qualify the content numbers ([dbebf0b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/dbebf0b2a79d1d30f931d9cc10a7655fde81ad0c))
* **roadmap:** add jevquestions source generator roadmap change ([528ab13](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/528ab13779a29104fd8d15d10c388e36412c962e))
* **roadmap:** add openrouter as a first-class provider for phases 1.3 and 1.6 ([39df0f6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/39df0f650110dc3108df426659b8fedeb7dcf6fa))
* **roadmap:** add org ruleset and pull request workflow to phase 1.5 design ([64f04e8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/64f04e888e15c994301d978afaf0e432c971d415))
* **roadmap:** add phase 1.1 implementation plan ([6efd06b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/6efd06b5963ed5938bcf4097e2c91c87d84a11bb))
* **roadmap:** add phase 1.1 repo scaffolding design ([a7ab63b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a7ab63b4382ac8c1e71231bd0991fabde0d884aa))
* **roadmap:** add phase 1.2 implementation plan ([7ee8035](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/7ee8035b9b70e6d20902d738ff89d37b5bd73a6e))
* **roadmap:** add phase 1.2 wire model design ([df8d8d1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/df8d8d1992a773a1522ca47964945d96b224cfcd))
* **roadmap:** add phase 1.3 question generator core design ([14decd3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/14decd3b853f8a9432d85aa2c441878153813247))
* **roadmap:** add phase 1.3 question generator core plan ([13d8a91](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/13d8a916d37c90ce3d0baf23ad3dd4d48424f4be))
* **roadmap:** add phase 1.4 transport and error model design ([55d12ab](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/55d12ab9610dcfe53aed96876c16cf40cf10addd))
* **roadmap:** add phase 1.4 transport and error model plan ([d267f6c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d267f6c3849a01fecbe8874bf88b4f4a63b85daa))
* **roadmap:** add phase 1.5 rename impact analysis ([42e10e3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/42e10e3e6745ad3f120141861d0e1bf1b63577b0))
* **roadmap:** add phase 1.5 rename to zeroalloc.jev design ([60e7ea0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/60e7ea0117f5ee0954001493f51483ecee3b02b6))
* **roadmap:** add phase 1.5 rename to zeroalloc.jev plan ([e9c65db](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/e9c65db802b45c302690bd5df2829b1a8e3dd8a9))
* **roadmap:** add phase 1.6 resilience design ([ff424e2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/ff424e24ecf23811f1e65ca6b17be0d73706f673))
* **roadmap:** add phase 1.6 resilience plan ([354209d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/354209d685d62e7d59b00882e86c25723ebf71d9))
* **roadmap:** add phase 1.7 test harness design ([e48f584](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/e48f584ea82ab7556f1b6d73779c1be9e2805f73))
* **roadmap:** add phase 1.7 test harness plan ([34e3eb8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/34e3eb895ee06eb53d470746cf91004d9a03f042))
* **roadmap:** add phase 1.8 ci and release pipeline design ([5b1a589](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/5b1a589adce9a6dc4e09d87e6488fd4e1f489c82))
* **roadmap:** add phase 1.8 ci and release pipeline plan ([cf896a5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/cf896a5f12ac63f98e68f9b93f27187925048a28))
* **roadmap:** add phase 2.1 typed evaluation design ([cc33f2f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/cc33f2ffb52e0a41f1e4bfaf7361b73deb1b7db7))
* **roadmap:** add phase 2.1 typed evaluation plan ([3ea38af](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/3ea38af7f5c1c3856ec7f16895b460c3ddf89d5c))
* **roadmap:** add phase 2.2 analyzers and code fixes design ([f1ec27e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f1ec27efefb4fad7f92870b44c554b587cf52f1d))
* **roadmap:** add phase 2.2 analyzers and code fixes plan ([824dc8e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/824dc8e48c5366a64498f66cb1013b1dcbcc9e7c))
* **roadmap:** add roadmap design spec ([6c44e98](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/6c44e98d61232dca68a1dc7de5e06ef5c314f0dc))
* **roadmap:** check off phase 1.1 plan tasks ([4bb44ee](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/4bb44ee2e920dda3fcdde498a7e3d1fa2922749a))
* **roadmap:** check off phase 1.2 plan and record follow-ups ([3d76652](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/3d76652e5e47849df29b15c2c09ea6c47ad3520c))
* **roadmap:** check off phase 1.3 plan and record follow-ups ([19a0442](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/19a0442dad9835643f81b6b354587c5ec7fd9874))
* **roadmap:** check off phase 1.4 plan and record follow-ups ([4cb335b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/4cb335b84642c22c0728ab0ca46f086da1ae5d3c))
* **roadmap:** check off the phase 2.3 plan ([338eea2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/338eea2d8d4973c618730d0427dc7bded51ca51e))
* **roadmap:** design phase 2.3 structured instructions and criteria ([a3af406](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a3af406a9695d8bd5bf3b8bfe15e2c616fd9a250))
* **roadmap:** link upstream ZeroAlloc.Rest accessibility issue ([d048ec1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d048ec1e61b70e6419c819e4835ba83801da86b7))
* **roadmap:** move JEV107 with the rest into Phase 2.2 ([990df8b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/990df8bd1f7f0fa8419c2010096eeb8482823fc2))
* **roadmap:** name jev001-006 and both description code fixes for phase 2.2 ([a917057](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/a917057b20abada688287eeb19d9867f26880db7))
* **roadmap:** plan phase 2.3 structured instructions and criteria ([ae56465](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/ae5646542b727fbcbb7a6be33189cc576a041da7))
* **roadmap:** record phase 1.1 follow-ups for later phases ([3b5e5d4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/3b5e5d446236b4e976b1c14367266cf7b3b1f786))
* **roadmap:** require an explicit opt-in for the live smoke tests ([bc78cb0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/bc78cb0785669601398c047387534cbfa7d7e751))
* **roadmap:** stop promising alpha NuGet publishing this phase dropped ([13b2460](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/13b2460063facff3206157db0d3b5ee9804adfa0))
* **roadmap:** sync phase 1.2 spec with review rulings ([361629c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/361629c05d70605b3b1c9fe71ea35bb936bd84dd))
* **roadmap:** update phase 1.5 design for the public repository ([dfc9c9f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/dfc9c9f52d7b733b493fe454eb4675aa3b0cc77b))
* split the SDK requirement for using vs. building the package ([2960590](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/2960590a89b85f69f7a8d73e894651559a838879))
* **state:** record the retry-count follow-up and point to milestone 2 ([3f8431f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/3f8431fbcca4afc673f7e6daf3113876c8d47c6c))


### Tests

* **benchmarks:** add a Noul-only typed benchmark alongside the triage one ([be6f492](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/be6f4929b31e59160ead7c8fe0e9e1bc18c36571))
* **benchmarks:** add a typed EvaluateAsync benchmark next to the untyped one ([8d3a012](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/8d3a012a366901e6c8708841284162fcafacae0a))
* **benchmarks:** add parse and client benchmarks with the org smoke gate ([ced7fdc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/ced7fdcaeecb2f215c1cf55cfb0d2cc405229094))
* **benchmarks:** measure the default resilience path, not a bypassed one ([db76b28](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/db76b28f8866ec4c1f480a493846726e745b8319))
* **core:** cover key casing, partial criteria and type info resolution ([4eca29e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/4eca29e87bfa30a10d0e7ec98cf1f880321e75e8))
* **generator:** run every lone surrogate case through appendjsonstring ([60c4d6d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/60c4d6dacefb42327867132d8d3702aa14c5c46f))
* **samples:** add a native aot smoke app for the client and generator ([429f871](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/429f8714b1b213f04131c2f3987520283c3842e8))
* **samples:** check retries in the native aot smoke app ([1ff71a0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/1ff71a08e4d44b850fa3b664c436fe1c1b6788bb))
* **samples:** correct allocation-gate and comment wording in the AOT smoke app ([c9b8961](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c9b89619fd9cf91272b0230b3674e3a9ab4d4ad0))
* **samples:** exercise typed evaluation and its DIM fallback under Native AOT ([5728151](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/572815118d2e84cd84176e86fc3208118ec02172))
* **samples:** gate allocations and every warning in the native aot smoke app ([0d8f439](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/0d8f439a92ddbb9f559dc001c5f6ee67bc426d08))
* **samples:** gate structured questions and the content factories under native aot ([1c1035f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/1c1035f7172e0fd722ea4059de2e7a394f382fa5))
* **samples:** widen the EvaluateRoundTrip allocation budget for linux-x64 ([212715f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/212715fd60d468a5f9e6522301b16b81dbe7cb32))
* **tests:** add live smoke tests that skip without api keys ([e646de0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/e646de070ca58045b1cdb672d43d689e0d316935))
* **tests:** add wiremock integration tests for the wire format and providers ([f3c817f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f3c817fe91a82da8c24d5781462488356220c012))
* **tests:** assert the non-ascii literal parses without diagnostics ([191506f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/191506f2145197963fd3fd3c60b18cc81c9ceafe))
* **tests:** build the integration request from public types ([cb486f0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/cb486f07fe9cd5ef018563acbb2b2f6f44810ee2))
* **tests:** cover base address and api key hardening, dispose borrowed http clients ([911349f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/911349ff86e4aef14b42389d6ad5bfcc523767de))
* **tests:** cover empty 200 bodies as invalid responses ([98772a6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/98772a6ea287f34980aa3281cb23b4214f81707c))
* **tests:** cover empty enums, escape edge cases and diagnostic locations ([52c9ec2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/52c9ec22d7111250e36e7bcc8054019cc4bca9c8))
* **tests:** cover errors, retries, time-outs and network failures over real http ([470b6f9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/470b6f9a09f5dff8d7800a4a59d56cdc4b49599a))
* **tests:** cover generator caching for question sets with diagnostics ([4e44fca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/4e44fca21c7304db91398efea0a00f544a788c72))
* **tests:** cover question generator diagnostics jev101-jev106 ([6a2d8d6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/6a2d8d6ede293db6315f0280ccaa1c02b3cd6683))
* **tests:** cover the edge cases tracked in [#17](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/issues/17) ([b795ac5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b795ac59a290e405c66bf63036fe089c4770a47b))
* **tests:** expect the platform new line when a document has none to follow ([9911f49](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/9911f49d316070bdc7fa98600058bdc3bd3b7c4e))
* **tests:** expect the retry-count header absent, not empty, on the first attempt ([ca37a4c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/ca37a4c63af607a469a6162644b538f89c625f92))
* **tests:** gate typed evaluation over WireMock and the live TypeSafe API ([b844b4b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/b844b4b62a91040614865f255a5bcf23cf6ae331))
* **tests:** hold a bound socket for the refused-connection test ([f2f8171](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/f2f8171542384893d47dffc40f7f6a27604a67c4))
* **tests:** honour JEV_LIVE_MODEL overrides in the typed live client ([627a2ed](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/627a2ede0d35e3cae162ac7c19d254e4ae48145e))
* **tests:** isolate the WireMock log and reuse the unit test fixture ([e8b7204](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/e8b72047936029bf31f4d0913ef2c254e0e902f9))
* **tests:** log live errors before asserting and cover generated probabilities ([9400e3f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/9400e3f5426fd73ef686da016ef449b7dc5c24fb))
* **tests:** make the wiremock integration tests deterministic under load ([9e7e886](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/9e7e886f8e99bdc5ff8c1dfe7826b74ea25b1831))
* **tests:** pack the library and inspect the nupkg ([c4729b7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/c4729b73de8eeb587e36f72460744cee058b76c5))
* **tests:** require JEV_LIVE=1 to run the live smoke tests ([d1ed75c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/d1ed75c2ea65c2dbccf40858e325082a6f6ed3be))
* **tests:** restore escaped option key in JevAnswerReader test ([05a6a01](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/05a6a01d1417056fb484fed3b81919f48a6bcffc))
* **tests:** send json instructions through the client over real sockets ([318082d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/318082d2e9c90ee96c0976f5d12bbd4327d570b2))
* **tests:** send structured criteria through the client over real sockets ([633d118](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/633d118eeb8d8aa8145dc31005f875d926575204))
* **tests:** verify generated question sets against the wire fixtures ([e7aacaa](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/commit/e7aacaa55cfcd33c2b3f5147730f1c63e1807aba))

## Changelog
