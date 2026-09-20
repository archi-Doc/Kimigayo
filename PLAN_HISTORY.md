# Kimigayo Plan History

**These are historical records, not current execution instructions.** Current implementation state, unresolved work and exact next actions are maintained only in [PLAN.md §2](PLAN.md#2-execution-state); product coverage is in [STATUS.md](STATUS.md). Recorded deadlines, stop-at-program restrictions, imperative commands, intermediate states and statements such as “current” apply only to their original execution. Do not execute them from this archive.

Records preserve original commands, paths, identifiers, hashes, quoted diagnostics, verification counts, failures, fixes and decisions. A historical PASS is tied to its recorded source/artifact state. IMPLEMENTED_UNVERIFIED checkpoints are retained; later final results do not rewrite the intermediate record. Corrections learned during migration are explicitly separated below. Missing evidence is not reconstructed from a textual reference.

## Record index

- [Origin surrounding-code review (2026-09-20)](#origin-review-20260920)
- [Origin syntax and elision integration (2026-09-20)](#origin-syntax-elision-20260920)
- [Documentation Markdown parser review (2026-09-20)](#documentation-markdown-review-20260920)
- [Documentation Markdown formal specification integration (2026-09-20)](#documentation-markdown-specification-20260920)

- [Documentation Markdown product switch (2026-09-20)](#documentation-markdown-product-switch-20260920)
- [Documentation Markdown second tuning round (2026-09-20)](#documentation-markdown-tuning-20260920)
- [Documentation Markdown benchmarks and improvements (2026-09-20)](#documentation-markdown-benchmarks-20260920)
- [Compiler continuation: native requirements and LibraryImport validation (2026-09-19 13:29 UTC)](#compiler-continuation-20260919-132943)
- [Compiler continuation: source modules and Library inspection (2026-09-19)](#compiler-continuation-20260919-113419)

- [Bounded loop/match/default continuation (2026-09-18)](#bounded-continuation-20260918)

- [Program Milestone 12 completion (2026-09-18)](#program12-completion)

- [Current timed continuation — partial bodies and caught transfers (2026-09-18)](#execution-1)
- [Previous timed continuation — partial terminal joins (2026-09-18)](#execution-2)
- [Previous timed continuation — ownership scope joins (2026-09-17)](#execution-3)
- [Previous timed continuation — broader ownership plan (2026-09-17)](#execution-4)
- [Program Milestone 11 completed (2026-09-17)](#execution-5)
- [Program Milestone 9 completed (2026-09-17)](#execution-6)
- [Historical execution: program Milestone 9 resumed after program 10 (2026-09-17, incomplete at that checkpoint)](#execution-7)
- [Program Milestone 10 integration (2026-09-17, complete)](#execution-8)
- [Program Milestone 9 integration (2026-09-17, in progress)](#execution-9)
- [Program Milestone 8 integration (2026-09-17)](#execution-10)
- [Program Milestone 7 integration (2026-09-17)](#execution-11)
- [Earlier execution-state snapshots](#old-execution-state)
- [Planning architecture audit](#old-architecture)
- [I2 Appendix A baseline map](#appendix-a-baseline)
- [G1–G9 / D1–D5 original decisions](#old-decisions)
- [Earlier I checklist](#old-checklist)
- [Initial baseline and units 1–32](#units-1-32)
- [Original roadmap](#old-roadmap)
- [Plan changes, reverted approaches and out-of-scope findings](#plan-changes)
- [STATUS dated records](#status-record-index)
- [STATUS area snapshot and 2026-09-14/15 verification](#status-area-snapshot)

<a id="documentation-markdown-review-20260920"></a>

## Documentation Markdown parser review — 2026-09-20

DM6 reviewed `Kimi/Compiler/Documentation` against the formal profile, including block/inline boundaries, source slices/mappings, candidate classification, immutable publication, resource limits and URL output. Existing documentation integration edits and the user's AGENTS.md change were preserved; no draft files were changed.

| Finding | Correction and regression |
| --- | --- |
| Emphasis used remaining delimiter counts for the rule of three, changing matches after inner emphasis consumed markers. | Retain the original length modulo three. Two byte fields replace the old character field, keeping delimiter scratch at 20 bytes. Explicit three/four-marker cases and generated official-output checks cover the change. |
| NUL stopped bare destinations/autolinks and had the wrong punctuation classification beside emphasis. | Treat it as U+FFFD during recognition, preserve original text/ranges, share the replaced autolink label/destination, and replace it in disabled-autolink output. Numeric-reference and backslash handling stay separate. |
| An empty link was counted one tree level too deep. | Compute wrapper height from actual children, allowing `[](u)` at depth 2 while preserving nonempty/nested limits. |
| Generated record formatting recursed through parent/child relationships until stack overflow, including during failed assertion formatting. | Provide a bounded node `ToString`, also safe for the default value and item formatting. |
| URL encoding allocated two temporary strings for each encoded scalar. | Write UTF-8 bytes and percent escapes through four stack bytes into the existing builder. Existing URL escapes and components are preserved. |

Initial failing tests reproduced NUL recognition and empty-link depth; failed assertion formatting also exposed the stack overflow. A 10,000-input fixed-seed comparison exposed partial-emphasis errors. Markdig also differed from official CommonMark behavior: an attempted change to escaped backtick handling was reverted after checking the official implementation. Expected results use [CommonMark 0.31.2](https://spec.commonmark.org/0.31.2/) and the pinned official commonmark.js output, not Markdig as an oracle. All 10,000 final structural HTML outputs matched; their input/output digests and the reference distribution hash are documented in the [verification data](xUnitTest/TestData/DocumentationMarkdown/README.md#2026-09-20-parser-review). Focused regressions also cover exact ranges, NUL normalization equivalence, deep formatting and Unicode URL output.

Validation: `dotnet build Kimigayo.slnx --no-restore -c Release -v:q` and the corresponding Debug build both finished with zero warnings/errors. Each configuration's `xUnitTest.dll -parallelMode none -failSkips` passed all 10,589 tests, with zero failures/skips (Release 46.618 s; Debug 55.941 s). This includes existing conformance, Markdig comparison, Kimigayo integration, Unicode, source-position, cancellation and resource tests. Logs are under `bin/DocumentationReview/`. NativeAOT and separate native fixtures were not run.

The local measurement probe initially failed because loading two compiler versions with shared Tinyhand registration state caused type-identifier collisions. It was corrected to isolate each assembly and its dependencies; only successful final samples are reported. A standalone probe restore was blocked from reading the user NuGet configuration; reusing the existing project assets allowed the probe to build without restore. [Final samples and limitations](Benchmark/DocumentationMarkdown.md#dm6-parser-review-2026-09-20) record the successful paired run. These checks do not establish exhaustive conformance or compiler-wide speedups.

<a id="documentation-markdown-specification-20260920"></a>

## Documentation Markdown formal specification integration — 2026-09-20

DM1 follow-up: the profile had been adopted through a normative reference to the [2026-09-19 design](draft/Design/2026-09-19%20Documentation%20Markdown.md), but its detailed rules still lived outside the formal specification. Integrated those rules into concise English specification sections with examples, preserving the finalized behavior and leaving all draft files unchanged.

| Design content | Formal owner |
| --- | --- |
| §§2–5: syntax and boundary rules | [Documentation Markdown profile §2](spec/documentation-markdown.md#2-syntax) |
| §6: summaries, items, classification and writing diagnostics | [Profile §3](spec/documentation-markdown.md#3-summary-and-documentation-items), [§2.3.5–6](spec/02-source-and-lexical-structure.md#235-writing-and-extracting-items) |
| §7: HTML, reference bases, logical paths and URL output | [Profile §4](spec/documentation-markdown.md#4-html-and-links) |
| §8: immutable API, source positions, publication, reuse and resources | [Profile §5](spec/documentation-markdown.md#5-syntax-api-and-processing-guarantees) |
| §9: conformance, differential, integration and resource verification | [Appendix A.21](spec/appendices/A-compiler-requirements.md#a21-documentation-comments) |
| §§10–11: integration and optional algorithms | Formal entry points now own the rules; product types remain independent of Markdig and concrete algorithms remain optional. Existing DM2–DM5 evidence is unchanged. |

Removed duplicate detailed rules from §2.3.4–6 and corrected two stale STATUS rows that still described the pre-DM5 product path. Test-data authority now points to the formal profile. This was a documentation-only change; no new implementation or runtime verification is claimed.

Verification: manually matched the design's adopted rules and acceptance criteria to the owners above; checked local Markdown targets, heading anchors and fenced blocks across the eight edited current documents. All documentation targets passed; 15 existing links in STATUS point to historical `bin/` reports absent from this checkout and were left unchanged. The formal SPEC/spec files have no remaining proposal references. No compiler tests were rerun for this documentation-only integration.

The updated AGENTS.md requires formal specifications to be independent of proposals. The former SPEC design-record index is preserved below as historical provenance, along with the former §21.3 reference to the [Generic Sharing and Specialization design](draft/Design/2026-09-13%20Generic%20Sharing%20and%20Specialization.md). These records and their old precedence notices no longer provide normative authority; the owning formal sections do. No unrelated language behavior was changed.

### Former SPEC design-record index (historical)

Design documents, decision records and change records are stored in `draft/` (formerly `doc/`); the rename does not change the precedence notices below. Each record listed here is integrated into the named sections. Where a record states that its changes take precedence, they override earlier restrictions. Features of a record that are not integrated are not adopted.

| Record | Integrated into | Notes |
| --- | --- | --- |
| [Documentation Markdown](draft/Design/2026-09-19%20Documentation%20Markdown.md) | [Limited Markdown profile, links and items](spec/02-source-and-lexical-structure.md#234-markdown-and-links), [Appendix A.21](spec/appendices/A-compiler-requirements.md#a21-documentation-comments) | Adopted limited profile; its explicit changes take precedence over the earlier Documentation Comments record. Unicode 15.0.0, lowercase standard items, declaration-dependent classification, immutable syntax and structured URL rules are requirements, independently of implementation status. |
| [Documentation Comments](draft/Design/2026-09-17%20Documentation%20Comments.md) | [Documentation syntax and association](spec/02-source-and-lexical-structure.md#231-documentation-text), [processing by use](spec/02-source-and-lexical-structure.md#236-tooling-and-diagnostics), [Appendix A.21](spec/appendices/A-compiler-requirements.md#a21-documentation-comments) | The finalized design takes precedence. Collection is disabled for ordinary compilation and enabled for documentation tooling; source-reading Mods retain raw-source dependencies. |
| [Whole-value replacement](draft/Changes/2026-09-17%20Whole%20Value%20Replacement.md) | [Sealed](spec/08-generics-constraints-and-contracts.md#847-intrinsic-contracts-and-guarantees), [payload projection](spec/13-operators-and-assignment.md#1355-explicit-borrow-and-reborrow), [receiver compatibility](spec/12-expressions.md#1244-object-receiver-compatibility), [whole-value updates](spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates), and the corresponding destruction, refinement, artifact and code-generation rules | Its Section 6 changes take precedence. |
| [Kimi library and named aliases](draft/Changes/2026-09-17%20Kimi%20Library%20and%20Named%20Aliases.md) | [Source aliases and effective defaults](spec/18-modules-and-dependencies.md#181-external-references-and-aliases), [name lookup](spec/09-names-signatures-and-access.md#941-named-aliases-collisions-and-warnings), [the Kimi library](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) | Core remains the name of the Type component. |
| [Declaration Container nesting](draft/Changes/2026-09-17%20Declaration%20Container%20Nesting.md) | [Container placement](spec/06-declarations-and-containers.md#611-root-and-nested-containers), [bound Contract references](spec/08-generics-constraints-and-contracts.md#849-bound-contracts-collisions-and-proof-paths), [qualified lookup](spec/09-names-signatures-and-access.md#961-bound-container-paths), [static storage](spec/22-core-execution-and-foreign-functions.md#2224-static-storage-in-inherited-environments) | Its changed rules take precedence. Runtime Contract Views and user-declared Contract parameters are not introduced. |
| [Dependencies and artifacts](draft/Design/2026-09-13%20Dependencies%20and%20Artifacts.md) | [Chapter 18](spec/18-modules-and-dependencies.md), [native build and commands](spec/20-compilation-configuration.md#208-llvm-output-native-build-and-execution), [product/test generation](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), [Appendix A.16](spec/appendices/A-compiler-requirements.md#a16-dependencies-artifacts-and-bounded-reuse) | Source-first distribution, exact-version resolution, locks, pack/publish, content stores, semantic reuse and native input validation. |
| [Testing](draft/Design/2026-09-13%20Testing.md) | [Test declarations](spec/06-declarations-and-containers.md#651-test-definitions), [verification](spec/17-failure-handling.md#175-test-verification-operations), [inputs](spec/18-modules-and-dependencies.md#188-product-and-test-inputs), [discovery and CLI](spec/20-compilation-configuration.md#209-test-command-and-discovery), [generation](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), [execution and reporting](spec/22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting), [Appendix A.17](spec/appendices/A-compiler-requirements.md#a17-test-verification-and-runner-requirements) | §17.5 supersedes the draft's earlier `$require` return behavior. The adopted [test profile](spec/testing-profile.md) defines public execution interfaces; future extensions remain in [Appendix D.4](spec/appendices/D-deferred-features.md#d4-testing-extensions). |

<a id="documentation-markdown-product-switch-20260920"></a>

<a id="compiler-continuation-20260920-import-symbols"></a>

## Compiler continuation: import symbol agreement — 2026-09-20

Entry baseline `7d2a8cb` with a clean tree; the plan's recorded `d871d45` baseline
was the DM5 predecessor commit. The request authorized unfinished implementation
Milestones and Checklist items, excluding Milestone Programs, draft edits and
NativeAOT. I26 was selected from the recorded next action. No specification or
draft file was changed.

**T26f — runtime declaration collisions (SPEC 21.5.2, 22.5.6).** The generated
module emits seven `declare dllimport` kernel32 APIs, and the test runtime adds
`GetEnvironmentVariableA` and `SetHandleInformation`. A `#LibraryImport` naming one
of those nine now shares that declaration only through the reserved `kernel32`
supply and with the declaration's exact physical signature; any other provider or
signature is `ConflictingRuntimeSymbol_Kd`. The two test-runtime declarations are
included because one source is compiled in both the product and the test build, so
accepting a disagreeing declaration would only move the collision to `kimi test`. `ExitProcess` is recorded as
unshareable because its declaration is `noreturn`, an ABI attribute no import can
express. The other generated definitions (user bodies `__kimi_f<n>`, runtime
helpers, `__kimi_start`, `_fltused` and the backend-provided symbols) are already
unnameable through the existing reserved-name rejection, so no further collision
class remained at this stage. `WindowsProfile.RuntimeDeclarations` records the
table, and `Kernel32ImportsTest.RuntimeDeclarationTableMatchesTheEmittedDeclarations`
re-derives every code from `WindowsRuntime.ll.in` and `Kimi/Testing/TestRuntime.ll`
so the table cannot drift from either template.

**T26g — one supply kind per external symbol (SPEC 20.8.2.1, 21.5.2).** Requirement
resolution moved ahead of the symbol-table update, so each declaration carries the
`Kind` of its defining module's requirement after self-targeted supply expansion,
with `kernel32` fixed to `import` and `kimi_backend` to `static`. Declarations of
one external symbol that agree on the physical signature but disagree on `Kind`
cannot share one `dllimport` setting and are `ConflictingImportSupply_Kd`. An
unresolved kind reports only the existing `MissingNativeRequirement_Kd`; the
previous acceptance of a record without a `Kind`, which project validation already
rejects, was preserved rather than converted into a second diagnostic. The final
symbol table is one `Dictionary<string, (string Signature, string? Kind)>` created
only when an import with a complete signature exists.

**T26h — one import declaration per function (SPEC 22.3.1).** Two `#LibraryImport`
attributes on one function previously left both attributes unresolved, reporting only
`UnresolvedBinding_Kd`, which is indistinguishable from the unimplemented-import
boundary. A repeated import is now an invalid declaration
(`InvalidLibraryImport_Kd`), because one declaration selects one external symbol; the
reproducer was added first and failed for that reason before the fix. Repetition of
the identical pair is rejected as well, following the existing Layout precedent.

Actual provider identity was deliberately left open: §20.8.2.3 does not merge
different modules' logical names by spelling, and two names may still designate one
supply, so the decision needs resolved supplies and belongs with direct-call
lowering and linking, not Binding.

Verification, all against the built sources above:

| Check | Configuration | Result |
| --- | --- | --- |
| `dotnet build Kimigayo.slnx -c Release` | Release | PASS, 0 warnings / 0 errors |
| `dotnet build Kimi/Kimi.csproj`, `xUnitTest/xUnitTest.csproj` | Debug | PASS, 0 warnings / 0 errors |
| `dotnet test xUnitTest/xUnitTest.csproj --no-build` | Debug | PASS, 10,602 / 10,602 |
| `dotnet test xUnitTest/xUnitTest.csproj --no-build` | Release | PASS, 10,602 / 10,602 |
| Focused `LibraryImportTargetBindingTest`, `Kernel32ImportsTest` | Debug | PASS, 94 / 94 |
| Focused `UnsafeFunctionValueBindingTest` | Debug | PASS, 12 / 12 |
| `kimi.exe check` of `examples/Counter` and `examples/Hello` | Release | PASS, exit 0, no diagnostics |

The suite grew from the recorded 10,561 by the 41 added cases (13 runtime-symbol
rows, the declaration-table check, 4 kind rows, the reserved-kind case, the
cross-module kind case, 2 repeated-declaration rows, 5 reserved-catalog rows, 2
reserved-name rows and 12 unsafe-value rows). Every new negative case names a diagnostic introduced by
this change, so it could not have passed before it. Fixture generation, LLVM
verification, native execution and performance measurement were NOT_RUN: no
generation or runtime path changed, and imports still cannot complete Binding.
NativeAOT was NOT_RUN.

**Out-of-scope finding (G10): unchecked named function values.** While checking
whether §22.3.1's "unsafe functions cannot be acquired as values" was enforced, a
temporary probe bound these sources through `MinimalEmissionTest.Analyze` and
printed `Binding.Result.IsComplete` with the issue codes. The probe was removed
after the investigation; the recorded results were:

| Source | Result |
| --- | --- |
| `func safe() -> i32 => 1` / `let g: () -> i32 = safe` | complete, no issue |
| `func safe() -> i32 => 1` / `let g: (i32) -> i32 = safe` | complete, no issue (wrong arity) |
| `unsafe func raw() -> i32 => 1` / `let g: () -> i32 = raw` / `let v = g()` | complete, no issue |
| `unsafe func raw() -> i32 => 1` / `let g = raw` | incomplete, `UnresolvedBinding_Kd` |
| `let g: () -> i32 = doesNotExist` | incomplete, `UnresolvedBinding_Kd` |

`BindReference` resolves a function group with a null Type for call selection, and
`BindVariable` runs `CheckTypeUse` only when the initializer Type is not null, so a
declared Function Type accepts any function group, including an unsafe one whose
value can then be called without an unsafe context. That is an acceptance hole in
I18's unimplemented function-item acquisition, not in the import work of this
execution, and a guard at the single initializer site would leave assignment and
argument positions unchanged. It is recorded as G10 in PLAN.md §5 with I18 as the
owning item.

**T26i — a reserved supply provides only its catalog (SPEC 20.8.2.4, 21.5.7).**
A `kernel32` import naming a symbol outside the embedded, hash-verified
`backend/windows-x64/kernel32.def` was accepted and would have failed later at link
time. It is now `UnavailableReservedImport_Kd`, which states that extending the
reserved supply needs a reviewed definition and profile update. The same rule applies
to `kimi_backend` against `WindowsProfile.ProvidedSymbols`; because every entry of that
catalog is already a reserved external name, no valid `kimi_backend` import remains.
Nothing is read from disk: both catalogs are embedded. The
existing reserved-name rejection now reads `WindowsProfile.FloatMarker` and
`WindowsProfile.ProvidedSymbols` instead of repeating their spellings, with two added
rows covering `__chkstk` and `_fltused`; the accepted and rejected sets are unchanged.

**T18a — unsafe functions are not values (SPEC 7.7).** The unambiguous part of that
finding was fixed here: `BindReference` now rejects a reference to an unsafe named
function in a value position — a declaration initializer, an assignment source, a
call argument, a transferred `return`/`exit` result, an expression body or an
array/tuple literal element, including through a qualified member reference — with
the new `UnsafeFunctionValue_Kd`. Direct calls, including qualified calls inside an `unsafe`
block, keep their existing binding, and safe function groups are untouched. The
signature-checking half of G10 was deliberately not attempted: it is the I18
acquisition path, and guessing it here would fix acceptance in one syntactic place
while leaving the same hole elsewhere. Twelve cases in
`xUnitTest/Tests/UnsafeFunctionValueBindingTest.cs` cover both directions.

Documentation: [STATUS.md](STATUS.md) records the new boundary, and the README
native-requirements section states the agreement rules and the unchanged limits.

## Documentation Markdown DM5 — 2026-09-20

The user authorized switching the compiler to the independent parser and removing
Markdig from the compiler, retaining it only for test purposes. Entry baseline
`d871d45` was clean. No draft or normative specification was changed.

`DocumentationMarkdown` now delegates parsing to `DocumentationMarkdownDocument`,
exposes independent node/item types, queries current Binding names and receiver
roles without rebinding, and reports optional source-mapped item diagnostics.
Unbound list candidates remain unclassified. Classification is not cached against
mutable declaration facts. Existing Binding publication, specialization, access,
fragment ordering and generated-source association remain in use.

The independent renderer uses iterative traversal, LF output, relative heading
levels (ARIA headings beyond h6), tight/loose lists, escaped code/text/attributes,
and cancellation with no partial publication. Structured source targets preserve
project/module identity, logical path, query and fragment. Source segments decode
UTF-8 once; relative separators/controls, invalid encoding and project-root escape
are rejected. Output mapping and final rewriting are checked independently. Empty
paths use the display page and preserve query/fragment absence versus emptiness,
including with a separate HTML base. Rejected ordinary links keep decorated text;
rejected autolinks keep their original escaped spelling. The default output
placement is documented root-relative identity mapping; custom layouts use the
structured mapper. [API guide](Kimi/Compiler/Documentation/README.md).

Markdig 1.3.2 was removed from `Kimi.csproj` and added as `PrivateAssets="all"`
only to `xUnitTest` and `Benchmark`. The compiler assembly-reference test passes;
restored compiler assets, compiler/Playground dependency manifests and output
directories contain no Markdig. A fresh managed (explicitly non-AOT) publish in
`bin/documentation-markdown/dm5-publish` contains no Markdig DLL or manifest entry;
`dotnet .../Kimi.dll --help` exits 0. Restore initially could not read the sandboxed
user NuGet.Config; an approved restore with a local source-free config used the
existing package cache and completed. No package version was upgraded.

Acceptance gates recorded before finalization were warning-free Debug/Release
builds, full managed regressions, official non-link HTML plus explicit URL and
facade tests, dependency-free compiler publish, equal-output seven-sample
render/parse+render comparisons, and <6× rendering time per approximately 4× input.
All pass. Legacy product expectations were migrated for lowercase standard items,
unrecognized named entities and unsupported reference definitions; the first
focused run exposed the obsolete `&copy;` expectation, which was corrected to the
adopted limited profile. No upstream CommonMark fixture was changed.

Output measurements led to bounded per-thread StringBuilder reuse (capacity at
most 65,536), vectorized escaping and removal of redundant URL scans. Same-output
six-case geometric means are 0.476 Markdig parse+render time / 0.394 allocations.
Render-only allocation equals the result string in these cases, but five cases
are slower than Markdig rendering alone. Oversized stress documents allocate
more because the retained scratch size is capped. These limits and raw samples
are in the [output report](Benchmark/DocumentationMarkdown.md#dm5-product-output-and-switch-2026-09-20).
Final Release product SHA-256:
`E1973176A1A2B05E405F9B29242C62B01BD8DEE69D02549C53B7725F6321C29E`.

Final verification commands:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -result-xml bin/documentation-markdown/dm5-full-release.xml
dotnet build Kimigayo.slnx -c Debug --no-restore
dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -result-xml bin/documentation-markdown/dm5-full-debug.xml
dotnet publish Kimi/Kimi.csproj -c Release --no-restore -p:PublishAot=false --self-contained false -o bin/documentation-markdown/dm5-publish
```

Both builds: zero warnings/errors. Both suites: **10,561 passed, zero errors,
failures, skips or unexecuted tests**, including **1,351 documentation cases**.
There are 320 additional exact official non-link product-output cases and 54
explicit output/URL/facade tests. Managed compiler integration renders and queries
documentation before confirming unchanged collected/uncollected IR. Logs/XML are
local ignored `bin/documentation-markdown/dm5-*` artifacts; raw benchmark JSON is
versioned under `Benchmark/Results/DocumentationMarkdown/2026-09-20-product-output.json`.
Native execution was not repeated; NativeAOT was not run. Optional additional
writing diagnostics and cross-comment caching remain absent. Current execution
state and next actions are owned only by [PLAN.md §2](PLAN.md#2-execution-state).

<a id="documentation-markdown-tuning-20260920"></a>

## Documentation Markdown second tuning round — 2026-09-20

The user requested a review of `Kimi/Compiler/Documentation` against the
2026-09-19 draft for bugs, then allocation and speed reductions measured with
`DocumentationMarkdownMeasurements`. Entry baseline was commit `cde175c` (DM4
complete). The draft, specification, Markdig product path and public API shapes
were not changed.

Findings and changes:

- Bug: bare link destinations stopped at any `<` (`[x](a<b)` became text).
  CommonMark 0.31.2, cmark, commonmark.js and Markdig accept `<` after the first
  character. Fixed in `TryLink` and the parenthesis index; three parser cases and
  three Markdig comparison destinations were added. No retained conformance,
  difference or interaction result changed.
- Allocation: retained nodes lost `Previous`/`Last` (44 → 36 bytes; parser-only
  links and depth/height live in a parallel buffer); the parser became a
  `ref struct` on caller-provided `stackalloc` scratch (no parser object, no pool
  traffic for typical comments); adjacent plain text runs merge; destinations and
  titles without escapes are lazily materialized source slices and title-less links
  add no table entry; the fence language, decoded text, entities and titles avoid
  intermediate strings; `ClassifyItems` scans small parameter sets without a
  dictionary; `GetText`/`Format` build exact arrays and strings once.
- Speed (from a SuspendThread+ClrMD sampler over a parse loop): a NUL in the
  inline `SearchValues` needle selected the slower
  `Ssse3AndWasmHandleZeroInNeedle` searcher and per-node NUL scans were wasted;
  fixed by a NUL-free needle plus a document-level NUL flag. Multi-line paragraphs
  with contiguous lines parse in place, joined paragraphs map positions per line;
  the exact depth is tracked during parsing instead of a final tree walk;
  parenthesis indexing jumps between relevant characters with a lazily cached stop;
  fence lines, whitespace consumption and list-marker reading avoid repeated scans.
  A plain-paragraph fast path now handles multi-line plain text; its fill loop
  records line bounds first because vectorized searches after the array store
  measured several times slower on this host.
- Measured pair (same process, 15 alternating samples, `cde175c` vs tuned):
  Summary 127.6 → 133.8 ns / 216 → 192 B; Rich-summary 1,253.0 → 872.7 / 920 → 536;
  Parameters 3,851.8 → 1,927.9 / 2,608 → 1,952; Code-example 7,227.1 → 4,245.3 /
  608 → 264; Nested 1,732.6 → 1,129.9 / 1,336 → 912; Links-entities 14,991.8 →
  9,603.9 / 15,664 → 11,568. Geometric means 0.669 time / 0.663 bytes. Full-run
  tuned/Markdig geometric means 0.395 / 0.170. Backtick-runs-256 allocation fell
  from 39,000 to 192 B; failed links/titles from 45,464 to 18,584 B at size 256.
  Scaling families remain below the 6× gate (2.76×–4.05× for ~4× input).

Verification: warning-free Release and Debug solution builds; the focused
documentation run passes 925 cases (`-class '*DocumentationMarkdown*'`, 977 with
`'*Documentation*'`); the full Release and Debug suites each pass 10,187 tests with
zero failures or skips. Raw JSON:
`Benchmark/Results/DocumentationMarkdown/2026-09-20-tuned*.json`; report section:
[Benchmark/DocumentationMarkdown.md](Benchmark/DocumentationMarkdown.md#second-tuning-round-2026-09-20-after-dm4).
Limits: the Markdig product path, renderer, URL resolution and publication adapter
are unchanged (DM5); timing noise on the host is about ±10% between runs; NativeAOT
and native execution were not run.

<a id="documentation-markdown-benchmarks-20260920"></a>

## Documentation Markdown DM4 — 2026-09-20

The user requested step 4: compare time/allocations with Markdig and make necessary
improvements. Entry baseline was clean commit `4c53331`. The product Markdig path,
dependencies, specification and draft were preserved. No NativeAOT or native
execution was performed in this step.

The pre-tuning gates were: retain DM3 regression coverage; no allocation regression
in six representative parse workloads; at least 10% geometric-mean time or
allocation improvement over DM3 to retain changes; investigate repeated timing
regressions above 10%; largest approximately 4× input growth below 6× time unless
explained; no unexplained quadratic trends; zero allocated bytes for cached
summary/candidates/source mapping. Parsing, fixture-adapted item processing,
retained results, three isolated first calls per input/engine and concurrency/
interruption were measured separately. These gates are historical acceptance
criteria for this execution, not universal runtime guarantees.

The [report](Benchmark/DocumentationMarkdown.md) owns detailed methods, results,
environment, reproduction commands, hashes and links to raw baseline/final/paired
and diagnostic JSON. Measurements use Release .NET 10.0.12, disabled tiering,
process CPU affinity, seven alternating warm samples (15 in the same-process
DM3/final pair), and identical LF/HTML-disabled/precise-location inputs. Recorded
Markdig assembly version 1.3.0.0 belongs to NuGet package 1.3.2.

Changes and evidence:

- Plain single-line paragraphs construct immutable nodes without parser scratch;
  summary allocation falls from 456 to 216 B.
- Contiguous physical fenced-code slices coalesce while preserving removed
  prefixes, NUL replacement and synthetic final LF; the 256-line example falls
  from 23,088 to 608 B. Eighteen regression cases exercise fast-path tree/range
  equivalence and code-slice boundaries.
- Dedicated simultaneous-worker measurements exposed duplicate first extraction.
  A monitor on private node storage serializes only unpublished extraction;
  completed reads remain lock-free. At 2,048 items/16 callers, median allocations
  fall from 1,038,824 to 147,728 B including coordination. Failed/cancelled attempts
  publish nothing. A waiting caller observes cancellation after acquiring the
  monitor; waiting itself is not cancellable.
- Same-process final/DM3 geometric-mean ratios are 0.640 time / 0.482 allocation.
  Final/Markdig ratios are 0.566 / 0.256 for the six representative parse inputs.
  All pre-tuning gates above pass. Classification has a small time/allocation
  disadvantage against the narrower fixture adapter; adversarial unequal
  backticks also allocate more than Markdig. Both are disclosed, without weakening
  node metadata or cancellation contracts.

Failures and measurement boundaries:

- Initial broad runs stopped at Markdig internal depth exceptions for
  `Delimiters-16384` and `Unmatched-brackets-16384`. The harness now records these
  rejected comparisons explicitly and measures the independent cases; it never
  reports an exception as a zero-time parse. Final baseline/final runs complete.
- The first same-process loader shared Tinyhand and failed duplicate registration.
  Isolating baseline DLL dependencies in its load context corrected the harness.
- Preliminary pinned-CPU concurrency observations included cold scheduling and
  were superseded by dedicated seven-sample diagnostics with prepared workers.
  Cancellation probes include timer scheduling and may cancel before parsing;
  their elapsed time is not polling latency. Retained heap deltas are estimates;
  whole-process peak memory includes the harness and does not prove a parser peak
  reduction. No rendering/URL or whole-product speedup was measured.

Final verification: warning-free `dotnet build Kimigayo.slnx -c Debug --no-restore`
and the corresponding Release build; `dotnet xUnitTest/bin/<configuration>/net10.0/xUnitTest.dll
-xml bin/documentation-markdown/dm4-full-<configuration>.xml` passes **10,175 tests,
zero failures/errors/skips** per configuration (965 documentation cases included).
The runner deprecates `-xml` in favor of `-result-xml`; it still writes the results.
The focused Release documentation run also passes all 965. Local build/test logs
and XML live under `bin/documentation-markdown/dm4-*`; raw benchmark JSON is kept
under `Benchmark/Results/DocumentationMarkdown/2026-09-20-*`. Current next actions
remain exclusively in [PLAN.md §2](PLAN.md#2-execution-state).

<a id="migration-audit"></a>

## 2026-09-18 documentation migration audit (later findings)

This section records the documentation migration, not compiler implementation. The worktree was clean at entry, at HEAD `a9843eced89fc82b1ff79921e9a205fa9a0f0506`. Both original working-tree files were copied byte-for-byte before editing, so uncommitted content would also have been retained. The temporary migration backup is `C:\Users\bwff1\AppData\Local\Temp\Kimigayo-doc-migration-20260918-013858`; it is not a fourth maintained document.

- Original `PLAN.md`: 333,988 bytes; SHA-256 `356191f13996a48dc76392485f8518cae6818805cf460ee823323ca97c6332d3`.
- Original `STATUS.md`: 176,553 bytes; SHA-256 `a32672dc1e83140b042c7bb6f53274a54bf3fdcf720748f35bedb1367091e853`.

Only PLAN.md, STATUS.md and PLAN_HISTORY.md are migration outputs. No implementation, specification, prompt, draft, build, test, native execution, NativeAOT, benchmark or autoframe work was performed. Read-only repository/file/hash/report checks and Markdown validation are distinct from conformance tests.

### Evidence identity and availability

The latest `bin/plan-execution/20260918-003029/verification.json` describes the execution based on HEAD `51defc81407658dff8736e80c72a5fd9419be092`, with later uncommitted changes. Its frozen manifests match the migration checkout: **606/606 source**, **4/4 compiler/test artifacts**, **4,305/4,305 fixture inputs**. All **22** referenced program reports exist; their PASS status, check counts, compiler configuration/hash and original source hash match the summary, totaling **984** checks. This read-only correspondence explains why the saved evidence can describe those matching inputs despite a different HEAD; it is not a fresh PASS and does not cover unmanifested inputs or every feature.

Latest artifact SHA-256 values, preserved from the manifest:

| Artifact | SHA-256 |
| --- | --- |
| `Kimi/bin/Debug/net10.0/Kimi.dll` | `83CBC8B5932E4970461C4DEA1B699FAEB8D536DCD40578F0D0668AB9EC4FFB7B` |
| `Kimi/bin/Release/net10.0/Kimi.dll` | `669D8AC70C708F35FA467F1954BABB058CC856A065AF02C3C8C444F0F236E91E` |
| `xUnitTest/bin/Debug/net10.0/xUnitTest.dll` | `F57A4FA7EB09AD394AFD41C75BA1E667BACC9E8E56D7A52090D0B0DBFC5BB459` |
| `xUnitTest/bin/Release/net10.0/xUnitTest.dll` | `B565ED98F358B3D2271807B3DB10A5C76944DA12A97D1D9CEF566D57A4B66959` |

| Evidence root checked during migration | Availability |
| --- | --- |
| `bin/plan-baseline/m1-8c49edb/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-014222/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-024236/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-034310/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-044326/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-054333/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-083038/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-092418/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-144405/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-112322/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-122546/` | MISSING; original records retained below, files not inspected |
| `bin/plan-execution/20260917-232851/` | PRESENT; existence alone does not validate every contained record |
| `bin/plan-execution/20260918-003029/` | PRESENT; existence alone does not validate every contained record |
| `bin/milestone7-work/` | MISSING; original records retained below, files not inspected |
| `bin/milestone8-work/` | MISSING; original records retained below, files not inspected |
| `bin/milestone9-work/` | MISSING; original records retained below, files not inspected |
| `bin/milestone9-resume-20260917/` | MISSING; original records retained below, files not inspected |
| `bin/milestone10-work-20260917/` | MISSING; original records retained below, files not inspected |
| `bin/milestone10-resume-20260917-083205/` | MISSING; original records retained below, files not inspected |
| `bin/milestone9-final-20260917/` | MISSING; original records retained below, files not inspected |
| `bin/milestone9-complete-20260917/` | MISSING; original records retained below, files not inspected |
| `bin/milestone11-work-20260917/` | MISSING; original records retained below, files not inspected |

`bin/kimi-alias-verification.json` is also MISSING. Container-nesting (8,305 managed/configuration, 68 cases, 10 new native executions) and whole-value replacement (8,237/configuration, 30 native executions) adoption entries supplied no precise per-run evidence path; their exact historical validation text is preserved below, but those adoption-specific reports could not be located in the named top-level evidence locations. The latest WholeValue regression is a distinct available record.

Earlier `PLAN-work-unit-history.md` references were relative to the named ignored run directories, not a repository-root fourth management file. The program 9/11 directories containing them are missing. Historical absolute paths under `C:\Users\bwff1\repos\Kimigayo` remain exactly as written and are not assumed to map to this checkout. Pattern/placeholder references such as `*-*.log`, `<configuration>/<run-id>` or abbreviated hashes are not individually verified evidence paths. Exact missing references discovered in the source documents are listed below.

<details>
<summary>Missing exact evidence references (literal source spellings)</summary>

- `bin/milestone1/Debug/64187af0f669403f80df9d887ec5926d/verification.json`
- `bin/milestone1/Debug/684f853e8e3945e6b5fdd842d32a9632/verification.json`
- `bin/milestone1/Debug/81cd458228a34cf6b733bcc5ab58d0b0/verification.json`
- `bin/milestone1/Debug/f8276af0d6fc48c9aacc864c6b053987/verification.json`
- `bin/milestone1/Release/599310bce5fd4088846e314e000fa532/verification.json`
- `bin/milestone1/Release/5ff41039b4dc486c8209c1767c56802f/verification.json`
- `bin/milestone1/Release/994fe9bb29794b7e87bc974d83757ef0/verification.json`
- `bin/milestone1/Release/b9a4a24c520c4742acbf45c024c6f122/verification.json`
- `bin/milestone10-resume-20260917-083205/`
- `bin/milestone10-work-20260917/baseline/`
- `bin/milestone10-work-20260917/verification.json`
- `bin/milestone11-work-20260917/`
- `bin/milestone11-work-20260917/verification.json`
- `bin/milestone6/Debug/98a3947647c4494cb4666f78a2c01896/verification.json`
- `bin/milestone6/Release/d945c54bb6b340d798c484200e300889/verification.json`
- `bin/milestone7-work/`
- `bin/milestone7/Debug/dabe834f49cf46f1aea7b8b66c29c4c9/verification.json`
- `bin/milestone7/Release/602c33a1a4694ec0897c46e9377e418d/verification.json`
- `bin/milestone8-work/`
- `bin/milestone8-work/verification.json`
- `bin/milestone9-complete-20260917/`
- `bin/milestone9-complete-20260917/verification.json`
- `bin/milestone9-final-20260917/`
- `bin/milestone9-final-20260917/baseline/`
- `bin/milestone9-final-20260917/verification.json`
- `bin/milestone9-resume-20260917/`
- `bin/milestone9-resume-20260917/baseline/`
- `bin/milestone9-resume-20260917/next-indirect-call.kimi`
- `bin/milestone9-resume-20260917/verification.json`
- `bin/milestone9-work/`
- `bin/milestone9-work/verification.json`
- `bin/plan-baseline/`
- `bin/plan-baseline/m1-8c49edb/`
- `bin/plan-baseline/m1-8c49edb/full-debug.xml`
- `bin/plan-baseline/m1-8c49edb/full-release.xml`
- `bin/plan-execution/20260917-014222/`
- `bin/plan-execution/20260917-014222/unavailable.kimi`
- `bin/plan-execution/20260917-024236/`
- `bin/plan-execution/20260917-034310/`
- `bin/plan-execution/20260917-034310/final-manifest.txt`
- `bin/plan-execution/20260917-044326/next-scope-continuation.kimi`
- `bin/plan-execution/20260917-054333/`
- `bin/plan-execution/20260917-054333/next-partial-scope.kimi`
- `bin/plan-execution/20260917-083038/`
- `bin/plan-execution/20260917-083038/next-partial-scope.kimi`
- `bin/plan-execution/20260917-092418/`
- `bin/plan-execution/20260917-112322/`
- `bin/plan-execution/20260917-112322/next-short-circuit.kimi`
- `bin/plan-execution/20260917-122546/`
- `bin/plan-execution/20260917-122546/next-partial-terminal-branch.kimi`
- `bin/plan-execution/20260917-122546/verification.json`
- `bin/plan-execution/20260917-144405/`
- `bin/plan-execution/20260917-144405/next-mixed-effect.kimi`

</details>

The three historical autoframe links (`autoframe/SPEC.md`, `autoframe/README.md`, `autoframe/VERIFICATION.md`) are also MISSING. Their original Markdown links remain unchanged in the archived STATUS snapshot for traceability; current STATUS records the absence instead of claiming retrievable support documentation. No replacement location was found among tracked files.

### Corrections and dispositions (not edits to the historical facts)

| Issue | Reconciled interpretation / disposition |
| --- | --- |
| “Current” sections disagreed about unit 33, unit 48 and unit 52 | Final matching evidence closes units 48–51 / T4n-ai–al. Unit 52 / T4n-am remains TODO; broader M2/M3 stay IN_PROGRESS. The earlier instructions/deadlines are archived. |
| Older code baseline and uncommitted claims | Original statements describe their executions. Migration began with a clean worktree at the newer HEAD; manifest correspondence, not timestamps, links the latest evidence. |
| Exchange spelling excluded in old plan §1 | SPEC §15.7 now defines Kimi.replace/exchange/swap and Sealed. The exclusion is superseded, its original wording retained below, and R/I/T families retain the adopted obligations. |
| ObjectCallCompatible effect implementation described as ordinary next work in older notes | SPEC.md explicitly defers inference/publication and release checking pending further instructions. Required semantics remain; no implementation was authorized by this migration. |
| Container nesting called unadopted in the earlier alias record | SPEC.md and owning chapters adopt it. Bound associated/refinement paths, static lifetime and A.20 invalidation remain incomplete. |
| Six-of-eighteen catalog and CoreIntrinsics/CoreDeclaration identifiers | Inspected KimiLibrary/KimiDeclaration and CoreCatalogTest use 10 validated of 22 entries. Twelve slots and object operations remain incomplete. The stale object-spelling comment is a remaining source/document mismatch, not specification authority. |
| Blanket “no defaults/generics/closures/structs/enums/modules” summaries | Replaced by the executed subsets and remaining boundaries. `LlvmEmitter.SupportedContainers`, ScalarDefaults and GenericStoragePlan admit supported forms despite broad old diagnostic wording. No full-feature completion inferred. |
| M4/M5/M7 TODO versus already implemented subsets | Current milestone labels are IN_PROGRESS; I9/I11–I15/I17–I20 reflect their bounded work. No parent was promoted to DONE. Earlier labels are retained. |
| P9-C and later P9-C1/C2/G1–G6 | Original umbrella split into concrete call/capture/generation slices; target completion records close that scope. Earlier incomplete target states remain historical and do not negate the later matching target regression. |
| References to plan M1–M17 | The actual stable milestone definitions are M1–M15. Program numbers 1–14 are separate. No M16/M17 definitions were found; these mentions are numbering inconsistencies, not omitted acceptance criteria. |
| Proposed GenericGenerationPlan/GenericContextSchema/GenericFramePlan | Existing GenericStoragePlan implements bounded shared generation; the earlier proposed file split is superseded as an implementation approach, while key/schema/frame requirements remain. Other proposed file names remain design suggestions, not mandatory duplicate implementations. |
| Appendix A coverage map | I2 mapped A.1–A.17 at its baseline. It does not close adopted A.18–A.20. Requirements remain under existing R/I families, with clause-level completion under I33. |
| CI described uniformly as lacking configuration/target/nonzero checks | test.yml explicitly selects Release/project/nonzero minimum/serial tests; publish.yml has a different, less explicit test step. Neither workflow was run here. |
| Failed/superseded verification | Preserve T4n-q's reverted attempt/spurious diagnostic; late warning corrections; unsafe old element self-read allowance; pre-generation empty native attempts; ah→ah2 corrections; and all shared-output collisions. The final isolated runs supersede overlapped program results even if those initially said PASS. |
| Historical allocation claims | Keep workload/configuration bounds, 72-byte require-expression Binding baseline, shared-module allocation exclusions, and measured front-end regression/no-speedup finding. Zero warm allocation is not universal. |
| G8 stale product documentation | Reconciled within these three documents. Full I34 support/usage audit remains unfinished; G1/G2 interface gaps, G3/G6/G7 investigations and G4 implementation mismatch remain. No normative SPEC_CONFLICT was established. |

### Preservation and reference map

| Original information | Destination |
| --- | --- |
| Objective/scope/authority and exclusions | PLAN §1; superseded scope wording below |
| Current M/I states, completed T/P slices, unfinished work and next reproducer | PLAN §2 only; previous values remain in dated records |
| R1–R28 requirements, M1–M15 design/dependencies/acceptance, I1–I34 outcomes, T1–T30/suffixes, V1–V12 procedures | PLAN §§2/4/6/8/9/11; original changed wording retained below |
| G1–G9 and D1–D5 | Current decisions in PLAN §5; full original reasoning in this history |
| Timed runs, units, intermediate IMPLEMENTED_UNVERIFIED, results, errors, fixes, rejected/reverted approaches, source/artifact identities | This history's execution sections and original change ledger |
| Product feature support and limitations | STATUS §§1–6; original status text retained as dated records and area snapshot |
| Verification counts/commands and target code state | STATUS §7 summary; full original evidence records here |

Original headings and explicit legacy C.* anchors are retained in the relevant destination or provided as compatibility redirects. Existing repository links into STATUS, including SPEC's c12 and the §4.8/§7.4 references, remain usable. Historical local paths are retained as evidence references even when unavailable. External/bookmarked links outside the accessible repositories cannot be enumerated; compatibility anchors cover the old headings.

References outside these three documents were left unchanged. `prompts/implementation-planning.md` and `prompts/implementation-execution.md` still describe the plan as an execution record and may need alignment with AGENTS.md's three-document ownership. Older example guides (including Functions, Matches, StringResults, StringFunctions, Conversions and Integers) contain increment-specific limits that should not be read as today's whole-product limits. `backend/windows-x64/ADOPTION.md` points to the legacy C.51 anchor for counts; that anchor now reaches current generation scope and this history through STATUS §7. No prompt, example, source diagnostic or draft was modified to repair those references.

### Final documentation validation

- All **28 original requirement rows** remain unchanged in current PLAN. All **72 milestone objective/dependency/invariant/completion/risk lines** remain there unchanged. I1–I34 retain their outcomes and unfinished scope, with supported slices and superseded proposed files identified separately.
- Comparing both preserved originals with the three outputs found **no missing non-heading content line, stable ID, or fenced code block**. Changed wording and original tables are archived with their disposition; headings were reorganized with compatibility anchors.
- All original PLAN/STATUS heading targets and explicit legacy anchors remain reachable in those files; no duplicate anchors remain. All **370 local link occurrences** in the three documents were checked. The only unresolved paths are the **three pre-existing autoframe links retained in the historical snapshot**, explicitly recorded as missing above. Current PLAN/STATUS links and tracked incoming Markdown links pass. No incoming reference was found in tracked Markdown in the sibling Tinyhand checkout.
- The **22 program reports** have `status: "passed"`, the recorded configurations/source/compiler hashes, and **984 total checks**. The source/artifact/fixture identity comparison has no mismatch. These are read-only checks of prior evidence, not executed conformance tests.
- Whitespace and workspace checks cover all three documents, including the new untracked history file. Only these three repository files changed; no compiler/test execution or draft edit was used for validation. Temporary comparison scripts and audit output remain beside the temporary backup, outside the maintained document set.

## STATUS record index

- [Partial-terminal ownership continuations (2026-09-18)](#status-record-2)
- [Declaration Container nesting adoption (2026-09-18)](#status-record-3)
- [Whole-value replacement adoption (2026-09-17)](#status-record-4)
- [Kimi library and named aliases (2026-09-17)](#status-record-5)
- [Earlier whole-value replacement proposal review (2026-09-17), preceding the](#status-record-6)
- [Mixed-target ownership continuations (2026-09-17)](#status-record-7)
- [ObjectCallCompatible implementation plan (2026-09-17)](#status-record-8)
- [Object compatibility terminology (2026-09-17)](#status-record-9)
- [Milestone 11 integration (2026-09-17, complete)](#status-record-10)
- [Milestone 9 integration (2026-09-17, complete)](#status-record-11)
- [Previous Milestone 9 continuation (2026-09-17, incomplete target)](#status-record-12)
- [Milestone 11 static-member example (2026-09-17)](#status-record-13)
- [Milestone 10 integration (2026-09-17, complete)](#status-record-14)
- [Milestone 9 continuation (2026-09-17)](#status-record-15)
- [Program Milestone 9 integration (2026-09-17, incomplete)](#status-record-16)
- [Program Milestone 8 integration (2026-09-17, complete)](#status-record-17)
- [Specification programs 10–14 (2026-09-17)](#status-record-18)
- [Ownership checking continuations (2026-09-17)](#status-record-19)
- [Scalar operand arrival (2026-09-17)](#status-record-20)
- [Omitted-default completion (2026-09-17)](#status-record-21)
- [Incomplete call acquisitions (2026-09-17)](#status-record-22)
- [Default initialization checking (2026-09-17)](#status-record-23)
- [State-neutral loop continuations (2026-09-17)](#status-record-24)
- [Nonreturning-call continuations (2026-09-17)](#status-record-25)
- [Final continuation verification (2026-09-17)](#status-record-26)
- [Prepared scalar subplace defaults (2026-09-17)](#status-record-27)
- [Mutable scalar locals in defaults (2026-09-17)](#status-record-28)
- [Unit defaults (2026-09-17)](#status-record-29)
- [Immutable scalar locals in defaults (2026-09-17)](#status-record-30)
- [Contained scalar default transfers (2026-09-17)](#status-record-31)
- [Default Move diagnostics (2026-09-17)](#status-record-32)
- [Scalar do defaults and final verification (2026-09-17)](#status-record-33)
- [Scalar selections in defaults (2026-09-17)](#status-record-34)
- [Scalar conversions in defaults (2026-09-17)](#status-record-35)
- [Scalar default execution (2026-09-17)](#status-record-36)
- [Default-expression control flow (2026-09-17)](#status-record-37)
- [Omitted-default call plans (2026-09-17)](#status-record-38)
- [Projected-call diagnostic boundary (2026-09-17)](#status-record-39)
- [Source validity and diagnostic lifetime (2026-09-17)](#status-record-40)
- [Source-change invalidation (2026-09-17)](#status-record-41)
- [Unresolved-conformance diagnostics (2026-09-17)](#status-record-42)
- [Unavailable declaration modifiers (2026-09-17)](#status-record-43)
- [Merged-container diagnostic locations (2026-09-17)](#status-record-44)
- [Program Milestone 7 complete (2026-09-17)](#status-record-45)
- [Program Milestone 6 complete (2026-09-17)](#status-record-46)
- [Program Milestone 5 complete (2026-09-17)](#status-record-47)
- [Specification programs 6–9 (2026-09-16)](#status-record-48)
- [Program Milestone 4 complete (2026-09-16)](#status-record-49)
- [Program Milestone 3 complete (2026-09-16)](#status-record-50)
- [Program Milestone 2 complete (2026-09-16)](#status-record-51)
- [Program Milestone 1 complete (2026-09-16)](#status-record-52)
- [Dependency API renames (2026-09-15)](#status-record-53)
- [Document structure (2026-09-14)](#status-record-54)
- [Test verification failure behavior (2026-09-15)](#status-record-55)
- [Design document alignment (2026-09-15)](#status-record-56)
- [Dependency and artifact specification integration (2026-09-15)](#status-record-57)
- [Receiver shorthand (2026-09-15)](#status-record-58)

<a id="execution-1"></a>

## Recorded timed continuation — partial bodies and caught transfers (2026-09-18)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Execution stopped **2026-09-18 01:37:44 UTC** (**67m15s elapsed**) after completing the current unit and its final documentation. No further unit started.

**Final checkpoint: units 48–51 / I6/I8 T4n-ai–al DONE.** Final combined
verification PASS at **01:36:58 UTC** (**66m29s elapsed**). Clean Debug/Release
builds and full managed suites PASS **8,507 tests/configuration** (**94 net
added**). All **861 native fixtures / 1,722 O0/O2 executions PASS**, including
**122 new fixtures**. Programs **1–11 PASS all 984 integration checks** across
both configurations; all 22 reports match compiler and original source hashes.
The final audit confirms **606 source**, **4 compiler/test artifact** and
**4,305 fixture input** identities unchanged; all **3,695 previous fixture
inputs** are retained unchanged. Reload/warmed analysis and IR writing allocate
zero bytes in the measured workloads. Final diff review and whitespace check PASS.
Evidence: `bin/plan-execution/20260918-003029/verification.json` and adjacent
logs/manifests. No implemented unit has missing required verification.

M2/M3 and the full compiler remain **IN_PROGRESS**. Next is **unit 52 / T4n-am**,
the completing-while reproducer recorded below; it has not started. General
iteration replay, unequal Loan joins, deferred cleanup and effectful divergence
remain incomplete. No new out-of-scope findings or pending decisions. The shared
program-output collision was resolved by isolated reruns and serialized checks.
Changes remain uncommitted; prior user changes are preserved. NativeAOT is
NOT_RUN. Draft, specification and milestone inputs are unchanged; SPEC §14.10.3
already requires this behavior. The soft-limit overrun only finished the existing
bounded native verification, evidence audit and documentation; no new work unit
or scope expansion started after 60 minutes.

Started **2026-09-18 00:30:29 UTC** (09:30:29 JST); soft deadline
**01:30:29 UTC**. Inspection, implementation and verification count toward the
60-minute limit. Fresh HEAD is `51defc8`; all previous uncommitted changes are
preserved. Re-read repository rules, execution instructions, the latest plan,
specification and ownership paths. All **605 source hashes** from the previous
checkpoint match, so its baseline remains applicable before new edits.
Evidence: `bin/plan-execution/20260918-003029/`.

**Unit 48 / I6/I8 T4n-ai DONE:** retain partial-terminal branch bodies
after noncompleting conditions. Confirm the recorded failing reproducer, preserve
already-recorded terminal histories and normal tails with their target/cleanup
extents, and verify both branch orders, initialization/Move/let/Loan diagnostics,
reload/warm reuse, full Debug/Release suites and LLVM/native O0/O2 execution.
SPEC §14.10.3 already defines the required behavior. M2/M3 and the full compiler
remain IN_PROGRESS; ObjectCallCompatible, draft edits and NativeAOT stay excluded.

At **00:34:06 UTC** (**3m37s elapsed**), the recorded 13:13 ownership failure
is reproduced and fixed. Nested branches already retain terminal histories;
`RecordTerminalSeed` now admits their proven normal tail after local cleanup.
Clean Debug build and **53 focused tests PASS**, including 17 new cases for
branch order, missing else, later conditions, cleanup, original transfer targets,
initialization/Move/let/Loan effects and reload/warm allocation. Unit 48 is
IMPLEMENTED_UNVERIFIED pending full Debug/Release and new native O0/O2 checks.

The first full Debug run exposed one obsolete guard expectation: the original
partial-condition test now correctly reports `PossiblyMovedUse`. Keep that exact
source, require the ordinary Move diagnostic and failed emission, and add two
ordinary Abort fixtures. Corrected verification uses `ai2-*`. The next scoped
reproducer (`next-partial-scope.kimi`) still reports UnsupportedOwnership_Kd at
9:13 and 12:13 after a completing `do` wraps a partial branch; it is not yet started.

Completed **00:38:35 UTC** (**8m06s elapsed**): both builds have zero warnings/
errors, full suites PASS **8,432/configuration** (19 added), and **20 new fixtures /
40 native O0/O2 executions PASS**. Reload/warm tests allocate zero bytes.
Evidence: `ai2-*`; source and native input identities are archived. Diff check PASS.

**Unit 49 / I6/I8 T4n-aj DONE, started 00:38:35 UTC:** preserve a
completing scope's normal checking tail when nested branches terminate. Reuse
normal-tail seeds after scope cleanup; keep escaping terminal paths pending for
their original targets. A proof must exclude transfers caught by the scope so
they cannot be silently omitted. Verify nested/scalar-result scopes, both branch
orders, local cleanup, initialization/Move/let/Loans, ordinary runtime behavior,
reload/warm reuse, full suites and native O0/O2 execution.

At **00:42:40 UTC** (**12m11s elapsed**), unit 49 is IMPLEMENTED_UNVERIFIED:
the scoped reproducer passes, Debug builds cleanly and **25 focused tests PASS**.
The initial scalar test used a discarded block body; it now uses the specified
expression-body form (`do => ...`) to exercise scalar result delivery. The
scope-caught exit reproducer retains its explicit Unsupported diagnostic.
Full Debug/Release and native verification uses `aj2-*`.

Completed **00:45:57 UTC** (**15m28s elapsed**): clean Debug/Release builds,
full suites PASS **8,457/configuration** (25 added), **30 new fixtures / 60 native
O0/O2 executions PASS**, and zero measured warmed allocations. Source and native
input identities are archived; diff check PASS.

**Unit 50 / I6/I8 T4n-ak DONE, started 00:45:57 UTC:**
`next-caught-scope.kimi` still reports UnsupportedOwnership_Kd at 9:13 and 12:13.
Record scope-caught normal arrivals after transfer cleanup, and distinguish their
pending source continuations from genuinely escaping terminal histories. Preserve
each inherited path's original target; no runtime edges are added. Reuse buffers
and extend only internal analysis records (no artifact schema change). Verify
both branch orders, caught and escaping mixtures, nested scopes, missing normal
tails, dead source after caught transfers, scalar results, initialization/Move/
let/Loan state, reload/warm reuse, full suites and O0/O2 native execution.

At **00:52 UTC** (about 22 minutes elapsed), the first **78 focused tests PASS**,
but an additional missing-condition audit reproduced a false UninitializedPlace_Kd
at 10:21 (`ak-dead-arrival.kimi`). Unit 50 remains IN_PROGRESS. Retain the
control-flow analysis's already-computed target-arrival membership and consult
it before collecting a normal caught arrival; an unreachable transfer still
contributes result Types and source checking, but cannot create a normal arrival.
Add initialization/Move/let regressions and stored-Loan cases before full checks.

At **00:53:43 UTC** (**23m14s elapsed**), both caught-scope reproducers pass;
the corrected Debug build is clean and **83 focused tests PASS**. Unit 50 is
IMPLEMENTED_UNVERIFIED pending full Debug/Release and O0/O2 checks (`ak2-*`).
The retained arrival set is cleared on control-flow reanalysis; checking buffers
are reused per function. Warmed reload/analysis/IR tests still allocate zero bytes.

The first full run passes all new cases but exposes an obsolete scoped-logical
guard. Preserve its exact source as a positive native fixture and add completing/
noncompleting left operands with both `and` and `or`; deferred and divergent
operands remain guarded. Corrected full/native verification uses `ak3-*`.
`next-caught-selection.kimi` still reports UnsupportedOwnership_Kd at 11:13 for
a selection-local `yield`; that separate arrival join has not started.

At **01:00:03 UTC** (**29m34s elapsed**), both full suites PASS
**8,484/configuration** (27 added) and **40 new fixtures / 80 native executions
PASS**. All 3,695 previous fixture inputs remain identical. Two test-only
formatting warnings are corrected without changing the test source strings or
compiler implementation; clean rebuilds and that test class are being checked
before closing the unit. The full/native results remain `ak3-*`.

Completed **01:02:10 UTC** (**31m41s elapsed**): both post-formatting builds have
zero warnings/errors and all **52 focused tests/configuration PASS**. Full suites
PASS 8,484/configuration and all 80 new native executions PASS; formatting did
not change compiler inputs or fixture source strings. No verification is pending
for this unit. Scope-caught arrivals and scoped logical operands are now supported
under the retained proof; broader cleanup/divergence boundaries remain guarded.

**Unit 51 / I6/I8 T4n-al DONE, started 01:02:10 UTC:** extend the caught
arrival mechanism to selection-local yields. Join structurally arriving transfers
after cleanup with normal branch tails; discard only their pending caught source
histories. Preserve non-arriving transfers after missing conditions, original
targets and escaping paths. Verify nested scope/selection interactions, scalar
results, both branch orders, dead-source effects, initialization/Move/let/Loans,
reload/warm reuse, full Debug/Release and native checks. Then freeze compiler
inputs for all Never/Default/WholeValue native fixtures and programs 1–11.
Final integration is required before this unit is DONE; no subsequent scope
expansion is planned during that verification window.

At **01:06:15 UTC** (**35m46s elapsed**), unit 51 is IMPLEMENTED_UNVERIFIED:
Debug builds without warnings/errors and **161 focused tests PASS**, including
23 new cases. Selection frames now collect only structurally arriving caught
transfers; the former exclusion proof is removed because these paths have
explicit normal seeds. Compiler/test inputs are frozen in
`final-source-hashes.json` (**606 identities**). Configuration builds use separate
output roots and source generation-to-file is disabled; full tests remain
sequential because fixture output is shared. Full/native/program verification
is pending, and the parent milestones remain IN_PROGRESS.

At **01:08:01 UTC** (**37m32s elapsed**), both builds are clean and the full
Debug suite PASS is **8,507 tests** (23 added; 94 since this execution's baseline).
Release full tests are running. Program configuration work initially collided
on `milestones/bin/.../Milestone1.O2.ll`: although report/work roots are separate,
the original-source CLI build uses a shared native output. Preserve this failure
in `initial-program-collision-Debug.log`; let Release finish, then rerun Debug
sequentially. This is a verification orchestration issue, not a compiler failure.
The earlier assumption that separate report roots isolated all program outputs
is superseded. Managed fixture writes are also serialized before native reads.

At **01:09:52 UTC** (**39m23s elapsed**), both full suites PASS
**8,507/configuration**, with zero failures/skips and clean builds. All source,
compiler/test artifact and generated fixture inputs are frozen. Final ordinary
native verification covers every `Default*.ll`, `WholeValue*.ll` and `Never*.ll`
fixture at O0/O2, including this execution's new cases. Release program checks
continue; Debug follows after Release releases the shared milestone outputs.
Unit 51 remains IMPLEMENTED_UNVERIFIED until these checks and the identity audit
finish. No new implementation unit has started.

**Next action — T4n-am / unit 52 TODO:**
`next-terminal-while-body.kimi` reports UnsupportedOwnership_Kd at 11:13 under
the final Debug binary. Start with a completing `while` whose body always
terminates: retain the skipped normal path and each terminal body history under
the original mixed targets. Do not discard the zero-iteration path, invent a
runtime result, or generalize to body backedges without their fixed-point proof.
Add and verify initialization/Move/let/Loan state, nested targets, both original
runtime paths, reload/warm reuse, full suites and LLVM/native O0/O2 evidence.
No implementation for unit 52 has started. Reproducer:

```kimi
func stop() -> Never => $abort("stop")
func f(c: bool)
    var x = 1
    do
        loop
            if c => return else => exit
            while c => return
            x = 4
        x = 2
        stop()
    let y = x
f(true)
```

Release program 1's initial overlapping run is also superseded despite its PASS;
rerun it in isolation after the Release sequence before starting Debug. Keep
all final program reports tied to non-overlapping native-output ownership.

At **01:15:50 UTC** (**45m21s elapsed**), all **182 Default fixtures / 364 O0/O2
executions PASS**. Release programs 1–11 have finished; program 1 is being rerun
in isolation to replace its overlapping result, then Debug will run sequentially.
All 606 frozen source hashes still match. WholeValue and Never native families
remain in progress; final source/fixture/artifact audit remains required.

At **01:17:24 UTC** (**46m55s elapsed**), Default and WholeValue verification
PASS **197 fixtures / 394 executions**. Release programs 1–11 PASS all **492
checks**, with program 1 replaced by its isolated rerun. Debug program work now
owns the shared milestone outputs. The final Never run covers **664 fixtures /
1,328 executions** and is still running. Its existing bounded operation is part
of unit 51's required verification; do not start another unit at the deadline.
Final totals, if all checks pass, will be **861 fixtures / 1,722 executions**,
including **122 new fixtures** from units 48–51. Counts remain provisional until
the final identity/report audit passes.

At **01:20:22 UTC** (**49m53s elapsed**), priority programs 1/8/9/11 PASS
**370 checks** across both configurations, with remaining Debug programs still
running. The frozen-input audit verifies all **606 source**, **4 compiler/test
artifact** and **4,305 fixture input** identities unchanged. Final diff review
PASS. Native Never verification remains active; no further implementation or
scope expansion has started.

At **01:25:52 UTC** (**55m23s elapsed**), programs **1–11 PASS all 984 checks**
across Debug/Release. The independent program audit validates all 22 reports
against the frozen compiler hashes and original source hashes (`final-program-audit.json`).
Default/WholeValue native checks pass; only the existing bounded Never run and
its final combined evidence audit remain. Unit 51 is not yet DONE.

**Soft-limit boundary, 01:30:42 UTC (60m13s elapsed):** no new unit or scope
expansion has started. The existing bounded Never verification is still active;
1,040 of 1,328 native output files have been produced (progress only, not a PASS
count), with about four minutes remaining at its observed rate. No additional
implementation is required. Finish this already-running check and the final
identity/documentation checkpoint, then stop. If it fails and needs substantive
new work, preserve IMPLEMENTED_UNVERIFIED and record the exact failure instead.
All 450 native inputs from completed units 48–50 still match their verified
versions (`completed-unit-input-audit.json`).

Completed **01:36:58 UTC (66m29s elapsed):** the bounded Never run finished at
01:35:06 UTC with **1,328 native executions PASS**. The final combined audit
passes every required unit 51 and integration check, including its **23 added
managed tests** and **32 new native fixtures**. The final checkpoint above
supersedes the provisional verification states in this execution history.

<a id="execution-2"></a>

## Recorded timed continuation — partial terminal joins (2026-09-18)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Execution stopped **2026-09-18 00:30:08 UTC** (**61m17s elapsed**) after final
verification review and documentation. The brief soft-limit overrun completed
the existing unit; unit 48 was not started.


**Final checkpoint: units 43–47 / T4n-ad–ah and the catalog prerequisite are
DONE.** The final evidence audit passed at **00:28:51 UTC**, **60m00s** after
execution started. No new implementation unit started after the soft deadline;
only the current unit's evidence review and documentation were finalized.
Debug/Release builds have zero warnings/errors, and both full managed suites
PASS **8,413 tests** (**108 net added** from the fresh baseline). All **112 new
fixtures / 224 native O0/O2 executions** and **143 existing fixtures / 286
native executions** PASS: **255 unique fixtures / 510 executions**. The 24
unit 45/46 fixtures were refreshed against the corrected final binaries.
Programs **1–11 PASS all 984 integration checks** across both configurations;
all 22 reports match their compiler and original program hashes. The audit
validated **605 source identities** and **3,695 fixture input identities**;
3,475 retained earlier inputs are unchanged. Reload/warmed analysis and IR
writing allocate zero bytes in the measured workloads. Final diff review PASS.
Evidence is consolidated in `bin/plan-execution/20260917-232851/verification.json`
and its adjacent logs/manifests. No implemented unit has missing verification.
Changes remain uncommitted. NativeAOT is NOT_RUN; draft, specification and
milestone inputs are unchanged. SPEC §14.10.3 already requires the behavior.
M2/M3 and the full compiler plan remain IN_PROGRESS. No new out-of-scope finding
or pending user decision was introduced; earlier recorded findings remain open.

**Next action — T4n-ai / unit 48 TODO:** reproduce
`bin/plan-execution/20260917-232851/next-partial-body-terminal-condition.kimi`
(final Release still reports `UnsupportedOwnership_Kd` at 13:13), then extend
`RecordTerminalSeed` to retain both the already-recorded terminal histories and
the remaining normal tail of a partial-terminal branch after a noncompleting
condition. The current `ScopedCheckingProof.Check(block, false)` restriction
must not be removed without preserving all paths and their target/cleanup
extents. Add both branch orders, initialization/Move/let/Loan, reload/warmed
reuse, full Debug/Release and LLVM/native O0/O2 verification. This unit has not
started. Recreate the ignored reproducer if evidence is unavailable:

```kimi
func stop() -> Never => $abort("stop")
func truth(x: i32) -> bool => true
func f(c: bool)
    var x = 1
    do
        loop
            if c => return else => exit
            if truth(stop())
                if c => return else => x = 3
            else => x = 4
        x = 2
        stop()
    let y = x
f(true)
```

General scope/caught-transfer replay, scoped logical operands with caught
transfers, unequal Loan joins, deferred cleanup and general effectful divergence
remain incomplete. ObjectCallCompatible implementation remains deferred.

Started **2026-09-17 23:28:51 UTC** (08:28:51 JST); soft deadline
**2026-09-18 00:28:51 UTC**. Inspection, implementation and verification count
toward the 60-minute limit. Fresh HEAD is `51defc8`; the worktree was clean.
Re-read the execution instructions, current plan, repository rules, specification
and ownership paths. Newer committed container-nesting changes are preserved.
The previous ignored evidence directory is absent, so historical results are not
treated as current verification. New evidence: `bin/plan-execution/20260917-232851/`.

**Unit 43 / I6/I8 T4n-ad DONE:** recreate the latest partial-terminal
mixed-branch reproducer and establish a fresh build/baseline. Extend branch-prefix
tracking with a separate normal-tail join, preserving pending terminal histories,
original transfer targets, pre-cleanup states and runtime edges. Require both
branch orders, missing else, later effects, initialization/Move/let/Loan checks,
reload/warm reuse, Debug/Release managed and LLVM/native O0/O2 evidence.
SPEC §14.10.3 already requires this behavior. Deferred cleanup, unequal Loan
joins, general effectful divergence and partial logical operands remain separate.
ObjectCallCompatible implementation, draft edits and NativeAOT stay excluded.
M2/M3 and the full compiler plan remain IN_PROGRESS.

At **23:34 UTC** (about 6 minutes elapsed), the fresh Debug build passes with
zero warnings/errors (`-m:1`; default parallel MSBuild failed without diagnostics).
The full managed baseline PASS is **8,305 tests**. The recreated CLI reproducer
reports UnsupportedOwnership_Kd at 13:13 as expected, plus pre-existing
EmptyExecutableBlock_Kd diagnostics for Kimi's three compiler-owned update
signatures. **T4n-ad prerequisite IN_PROGRESS:** parse those trusted signatures
through the existing signature parser, without requesting nonexistent source
bodies. Preserve ordinary user-body validation; add catalog diagnostic coverage
and verify CLI/integration behavior. This prerequisite is necessary for clean
native CLI verification and does not change the language's body requirements.

At **23:41 UTC** (about 13 minutes elapsed), the prerequisite's 78 focused
catalog/body-syntax tests PASS and the CLI has only the expected ownership error.
The initial partial-branch implementation passes its focused cases and reproducer.
The broad run identified three obsolete guard expectations (moved to positive
coverage) and three loop regressions caused by splitting fully normal branch
graphs. Restrict splitting to selections containing terminal paths; preserve
closed normal CFG replay. A targeted review also exposed missing selection-local
yield/exit arrivals; retain an explicit guard and tests for that separate slice.
Unit 43 remains IN_PROGRESS until these corrections and full/native checks pass.

At **23:45 UTC** (16 minutes elapsed), unit 43 is IMPLEMENTED_UNVERIFIED:
final Debug build has zero warnings/errors and all **8,336 tests PASS** (31 net
added from the fresh baseline). The recreated partial-branch CLI input passes.
Existing caught-yield handling still diagnoses the missing initialization; the
new fork explicitly leaves these arrivals to that existing path rather than
discarding them. Release and native checks are running. Source identities are
recorded in `ad-source-hashes.json`. Next dependency-ready reproducer:
`next-partial-logical.kimi` still reports UnsupportedOwnership_Kd at 10:13 for
`c and (if c => return else => true)` after a mixed-target join; it needs separate
evaluated-normal and skipped normal tails, with terminal RHS histories pending.

Completed **23:48:28 UTC** (**19m37s elapsed**): clean Debug/Release builds,
both full suites PASS **8,336/configuration**, and **40 new fixtures / 80 native
O0/O2 executions PASS**. The catalog prerequisite is DONE. Reload/warmed analysis
and IR writing allocate zero bytes in the new workload. Source identities and
native inputs are archived; diff check PASS. No specification change is needed.

**Unit 44 / I6/I8 T4n-ae DONE, started 23:48:28 UTC:** the recorded
partial-logical reproducer fails at 10:13. Fork a proven prefix for a completing
left operand and partial-terminal RHS, then join only evaluated-normal/skipped
tails. Retain RHS terminal histories for enclosing joins. Verify and/or, both
branch orders, nested RHS, subsequent effects, initialization/Move/let/Loans,
reload/warm reuse, full Debug/Release suites and new native O0/O2 fixtures.
Noncompleting left operands with partial RHS remain a separate slice.

At **23:52 UTC** (about 23 minutes elapsed), the reproducer passes and **110
focused tests PASS**, including 27 new cases. Debug builds cleanly; final full
Debug/Release and new native checks are running with unchanged sources. The
next reproducer, `next-partial-logical-left.kimi`, still fails at 10:13: its left
operand is `truth(stop())`. Retain its lack of runtime result while joining
every evaluated/skipped checking history; do not invent a normal successor.

Completed **23:55:08 UTC** (**26m17s elapsed**): clean Debug/Release builds,
both full suites PASS **8,363/configuration** (27 added), **28 new fixtures / 56
native O0/O2 executions PASS**. All 3,335 earlier fixture input hashes match.
Warm reload/reanalysis/emission checks allocate zero bytes. Evidence: `ae-*`.

**Unit 45 / I6/I8 T4n-af DONE, started 23:55:08 UTC:** preserve the
evaluated/skipped checking join for a noncompleting left operand with a partial
RHS. The next reproducer still fails UnsupportedOwnership_Kd at 10:13. The
RHS's terminal paths are now explicitly recorded, so the proof may include them
only while their continuations and normal tails are retained. Verify common
guarantees, skipped paths, initialization/Move/let/Loans, nested operands,
reload/warm reuse and full/native regressions. No runtime result is introduced.

At **23:58 UTC** (about 30 minutes elapsed), clean Debug build and **116 focused
tests PASS**, including 14 added cases. The reproducer passes; existing deferred,
divergent and completing-scope guard cases still pass. Full Debug/Release and
the new left-operand native fixtures are running. Next candidate is
`next-terminal-condition.kimi`; its branch-prefix proof still excludes a
noncompleting condition, even when the whole selection cannot complete.

Completed **00:01:46 UTC** (**32m55s elapsed**): both builds have zero warnings/
errors; full suites PASS **8,377/configuration** (14 added); **10 new fixtures /
20 native O0/O2 executions PASS**. All 3,475 prior input hashes match. Warmed
reload/analysis/emission remains allocation-free in the measured tests. Evidence:
`af-*`. Scope/cleanup/divergence guards retain their existing diagnostics.

**Unit 46 / I6/I8 T4n-ag DONE, started 00:01:46 UTC:** the next terminal
condition reproducer reports UnsupportedOwnership_Kd at 11:13. Permit branch
prefixes after noncompleting conditions only when the selection itself cannot
complete; join all checking branch histories without producing a runtime result.
Completing selections with a later noncompleting condition remain separate.
Verify missing else, else-if, transfers, common initialization/Move/let/Loan state,
reload/warm reuse, full suites and native O0/O2 fixtures.

At **00:04 UTC** (about 36 minutes elapsed), clean Debug build and **70 focused
tests PASS**, including 18 new cases. The original condition reproducer passes.
Final full suites/native checks are running. `next-later-terminal-condition.kimi`
retains the completing-selection boundary and fails at 11:13; its earlier
normal branch must be kept separate from later checking-only condition paths.

Completed **00:07:41 UTC** (**38m50s elapsed**): clean Debug/Release builds;
full suites PASS **8,395/configuration** (18 added); **14 new fixtures / 28 native
O0/O2 executions PASS**. Reload/warmed analysis/emission checks pass. Evidence:
`ag-*`. The completing-selection boundary remains explicitly tested.

**Unit 47 / I6/I8 T4n-ah DONE, started 00:07:41 UTC:** retain earlier
normal branch arrivals when a later condition terminates. Track whether the
condition chain can still complete; later branch tails are terminal checking
histories, never normal arrivals. Verify normal/terminal state separation,
missing initialization, Move/let/Loan histories, else-if chains, warmed reuse,
full Debug/Release and new native checks. Then freeze compiler inputs for final
native and program integration regressions, prioritizing programs 1/8/9/11.
Check elapsed time between operations and stop starting work at the soft deadline.

At **00:10 UTC** (about 42 minutes elapsed), the later-condition reproducer
passes; Debug builds cleanly and **84 focused tests PASS** (14 net added).
Implementation is frozen. Full Debug/Release suites, new native fixtures and
final regressions are running; source identities are in `final-source-hashes.json`.
No further implementation unit has started. The next bounded candidate is a
partial-terminal branch body after a noncompleting condition; its normal tail
and already-recorded terminal histories must both participate in the checking
join. Do not remove the current completing-body proof until those paths are
verified together.

At **00:12 UTC** (about 44 minutes elapsed), an additional ordinary-path audit
reproduced a Debug assertion in `OwnershipBody.PartitionCheckingBlocks` for
`ordinary-later-condition.kimi`: the normal join inherited the later dead
condition's region. Unit 47 is reopened IN_PROGRESS. Restore the join's original
region when no mixed-prefix join was created, and add reachable true/Abort,
single-target dead-source and missing-initialization regressions. The in-flight
checks are superseded; final integration is paused until the corrected sources
build and pass. Do not mark this unit DONE from the earlier focused results.

At **00:15 UTC** (about 47 minutes elapsed), the ordinary-condition reproducer
passes after restoring the original normal join region. **133 focused tests
PASS**, including four new crash/runtime/diagnostic cases. The corrected build
is clean. Final verification uses `ah2-*`; the earlier `ah-*` results are
historical and do not certify this correction. Updated frozen source identities
are in `final-source-hashes.json`. Independent native/program work may run in
parallel only with separate output roots and matching corrected binaries.

At **00:18 UTC** (about 49 minutes elapsed), corrected Debug/Release builds
have zero warnings/errors, both full suites PASS **8,413/configuration** (108
net added from the fresh baseline), and unit 47's **20 fixtures / 40 native
O0/O2 executions PASS**. All implementation-specific checks pass. Final native
regressions and program integrations are still running against frozen binaries;
Debug and Release program work uses separate configuration/GUID directories,
and scalar native work exclusively owns `bin/scalar-native`. The six completed
implementation slices add 112 fixtures / 224 native executions in total.

At **00:24 UTC** (about 55 minutes elapsed), the six final native regression
families PASS **143 fixtures / 286 O0/O2 executions**. All 3,475 retained earlier
input hashes match. Programs 1/8/9/11 PASS **370 checks** across both corrected
configurations; remaining program regressions are running. Refresh the 24 unit
45/46 fixtures against final binaries because their intermediate native inputs
were not separately archived. This closes that evidence gap rather than
assuming identical output. No further implementation unit has started.

<a id="execution-3"></a>

## Recorded timed continuation — ownership scope joins (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

**Final checkpoint: units 36–42 / T4n-w–ac are DONE.** Final integration completed
at **13:24:11 UTC**, **58m25s** after the execution start. Both builds have zero
warnings/errors; both full managed suites PASS **8,107 tests** (146 net added).
All **136 new fixtures / 272 native O0/O2 executions PASS**. All **524 source
hashes** and **3,065 input hashes for 613 native fixtures** match final artifacts;
477 of those fixtures retain their previous native evidence through identical inputs.
Programs **1–11 PASS all 984 integration checks** across both configurations;
all 22 reports match the final compiler and program identities. Warmed analysis/
emission reuse allocates zero bytes in the retained workloads. Final diff review
PASS; no implemented unit has missing verification. Changes remain uncommitted.
The full compiler plan and M2/M3 remain IN_PROGRESS. The next action is the
partial-terminal mixed-branch reproducer recorded below. Evidence is consolidated
in `bin/plan-execution/20260917-122546/verification.json` and adjacent manifests.

Final documentation/workspace review finished **13:26:10 UTC**, **60m24s elapsed**. No further work unit was started.

Started **12:25:46 UTC**, soft deadline **13:25:46 UTC**, including repository
inspection, implementation and verification. Fresh HEAD is `7869ce7`; preserved
the existing modified and untracked unit 33–35 files. Re-read AGENTS.md, execution
instructions, this plan, owning specification rules and current implementation.
All 519 source identities from the previous checkpoint still match. Baseline
worktree/patch, plan and evidence are under `bin/plan-execution/20260917-122546/`.
The broader compiler scope remains authorized; program 11 is already complete.
ObjectCallCompatible implementation, draft edits and NativeAOT remain excluded.

**Unit 36 / I6/I8 T4n-w DONE:** reproduce the current short-circuit failure
and extend `ScopedCheckingProof` to logical operands whose entire evaluation
completes. Keep transfer/divergence operands guarded. Reuse existing short-circuit
CFGs, per-target replay and Loan checking; preserve skipped paths, evaluation
order, initialization/Move histories and runtime reachability. Required evidence:
positive/negative and/or/nested/scoped effects, execution/skip behavior, stored
Loans, reload/warm reuse, clean Debug/Release builds/full suites and new O0/O2
native fixtures. No semantic change to SPEC §13.4 or §14.10.3 is proposed.

Completed **12:33:14 UTC** (7m28s). The baseline reproducer failed with
UnsupportedOwnership_Kd and now passes. Completing logical operands reuse the
existing evaluated/skipped CFG join; transfers and noncompleting operands remain
guarded. Clean Debug/Release builds and full suites PASS **7,988/configuration**
(27 added cases); **26 new fixtures / 52 native O0/O2 executions PASS**. All
2,385 previous native input hashes still match. Reload/warm reuse allocates zero
bytes in the tested workload. Evidence: `w-*` logs and source/fixture manifests.
Initial test-only do expressions discarded their final bool under the common Body
rules; corrected them to effectful Unit-argument calls so the negative tests reach
ownership analysis. No specification change, draft edits or NativeAOT.

**Unit 37 / I6/I8 T4n-x DONE, started 12:35 UTC:** the fresh `next-require.kimi`
reproducer fails UnsupportedOwnership_Kd at 10:13. Require's failure body currently
discards its terminal continuation. Retain it before implicit cleanup, alongside
nested terminal seeds, for enclosing scope/selection joins; preserve require's
ordinary success-only successor and transparent target lookup. Admit require in
the bounded scope proof only through the already-supported condition/body checks.
Verify direct/block failures, partial nested paths, targets, initialization/Move/
let/Loans, default checking, guards, reload/warm reuse, full Debug/Release suites
and ordinary native O0/O2 execution. General region-changing replay stays guarded.
The default checks found a prerequisite in `ScalarDefaults`: require was rejected
before declaration/caller ownership checking. Reuse the existing scalar condition,
body and contained-transfer validation, then verify both supplied declaration
checking and omitted default execution, results and caller histories in this unit.
Completed **12:42 UTC** (about 16 minutes elapsed). Clean Debug/Release builds
and full suites PASS **8,017/configuration** (29 added); **26 fixtures / 52 native
O0/O2 executions PASS**. All 2,515 prior native input hashes match regeneration.
The reproducer now passes; supplied-default errors, target filtering, ordinary
success-only state, stored Loans and zero-allocation reload/warm checks pass.
Evidence: `x-*` logs/manifests. General compiler M2/M3 remain incomplete.

**Unit 38 / I6/I8 T4n-y DONE, started 12:42 UTC:** record short-circuit terminal
operand continuations and join evaluated/skipped checking paths after a
noncompleting left operand. `next-logical-terminal.kimi` reproduces the missing
outer state at 9:13. Preserve no-runtime-successor/result behavior, RHS skip
semantics, source diagnostics and terminal target extents. Remove the unit 36
completing-only proof restriction only after these paths have explicit provenance;
mixed region-changing replays, deferred and effectful divergent operands remain
guarded. Require targeted state/Loan/default/reload tests, full Debug/Release and
new native O0/O2 evidence before DONE.
Completed **12:48 UTC** (about 22 minutes elapsed). The reproducer now passes;
clean Debug/Release builds/full suites PASS **8,043/configuration** (26 added).
**28 fixtures / 56 O0/O2 executions PASS**, including Abort, skipped and executed
returns and bounded default divergence. Initialization/Move/let/Loan, nested
operand and zero-allocation reload/warm checks pass. All 2,645 prior native input
hashes match. Evidence: `y-*` logs/manifests. The existing mixed-target terminal
operand guard cases still pass; their missing per-branch replay remains separate.

**Unit 39 / I6/I8 T4n-z DONE, started 12:51 UTC:** the fresh
`next-mixed-terminal-branch.kimi` still fails at 13:13. Preserve per-target state
at a proven common branch prefix, then seed separate checking regions for the
terminal branches. Limit the first slice to closed terminal if/else selections;
keep the ordinary runtime edges, original targets and pre-cleanup state unchanged.
Do not weaken the existing closed-graph replay proof. Verify branch-specific
effects, nested/else-if paths, common guarantees, Loan liveness, guarded partial
and divergent paths, reload/warm allocation and full/native regressions.
The unit 38 compiler integrations are running against unchanged binaries; any
later compiler build will require those affected integration checks again.
At **12:55 UTC**, the reproducer and branch-state cases pass. Loan/local-cleanup
tests exposed a deferred-capture bug: a continuation referenced a region whose
Entry was later assigned by implicit cleanup, producing an invalid replay range.
Capture the immutable constituent seed range before cleanup instead. The tests
retain this crash regression, and the newly supported closed-terminal guard case
moves to positive coverage while a partial-terminal branch preserves its old guard.
Completed **13:00:30 UTC** (34m44s elapsed): clean Debug/Release builds and full
suites PASS **8,066/configuration** (23 added); **20 fixtures / 40 native O0/O2
executions PASS**. All 2,785 prior native input hashes match. The crash regression,
Loan liveness, owned locals, neutral divergence and zero-allocation warmed reload
pass. Indexed branch traversal removes a measured 320 B/8-iteration iterator cost.
Evidence: `z-*`. Partial-terminal branches and mixed require remain guarded.

**Unit 40 / I6/I8 T4n-aa DONE, started 13:00:30 UTC:** reuse the proven branch
prefix for require's failure and success checking regions. The existing fresh
`next-mixed-require.kimi` fails at 13:13. Retain failure histories separately while
the success path continues, without a normal failure successor. Verify target
filtering, conditional/common effects, nested failures, Move/let/Loan state,
cleanup/divergence guards and warmed reuse, full Debug/Release and new native
O0/O2 fixtures. No new replay-proof relaxation is proposed.
Completed **13:05 UTC** (about 39 minutes elapsed): clean Debug/Release builds
and full suites PASS **8,085/configuration** (19 added); **14 fixtures / 28 native
O0/O2 executions PASS**. All 2,885 prior native input hashes match. The reproducer,
target filtering, failure/success/condition effects, nested failures, stored Loans
and zero-allocation warmed reload pass. Evidence: `aa-*`.

**Unit 41 / I6/I8 T4n-ab DONE, started 13:05 UTC:** reuse branch prefixes for
mixed-target logical expressions with a completing left and terminal right operand.
`next-mixed-logical-right.kimi` still fails at 13:13. Keep the skipped path as the
only ordinary logical successor; retain terminal RHS histories for enclosing joins.
Do not broaden noncompleting-left or partial-RHS support in this slice. Verify
and/or, transfers/divergence guards, Move/Loan histories, target filtering and
warmed reload, full Debug/Release and new ordinary O0/O2 fixtures.
Completed **13:09 UTC** (about 43 minutes elapsed): clean Debug/Release builds
and full suites PASS **8,096/configuration** (11 net added); **12 fixtures / 24
native O0/O2 executions PASS**. All 2,955 prior native inputs match. Supported
guard cases moved to positive coverage; partial RHS, deferred and effectful
divergence guards remain. Warmed reload allocates zero bytes. Evidence: `ab-*`.

**Unit 42 / I6/I8 T4n-ac DONE, started 13:09 UTC:** extend the same prefix split
to noncompleting left operands, using the existing explicit evaluated/skipped
checking join (and no runtime result). `next-mixed-logical-left.kimi` reproduces
UnsupportedOwnership_Kd at 13:13. Preserve common guarantees across RHS execution
and skipping, original target extents, Loans and zero-allocation reuse. Partial
RHS and general effectful divergence remain guarded. Verify full Debug/Release,
new native O0/O2 and final completed-program regressions against frozen binaries.
At **13:12:13 UTC** (46m27s), implementation and focused/native verification PASS:
clean Debug/Release builds and full suites **8,107/configuration** (11 added),
**10 fixtures / 20 native O0/O2 executions**, common-guarantee/Move/let/Loan and
zero-allocation warmed reload checks. Final program 1–11 regressions are running
against frozen binaries, prioritizing 1/8/9/11. Check the soft deadline between
program runs; do not start another implementation unit while these checks run.
Final program regressions remained pending at that checkpoint; all subsequently
passed at 13:24:11 UTC, completing this unit without further source changes.
At **13:18 UTC** (about 52 minutes), final programs **1/8/9/11 PASS 370 checks**
across both configurations; reports match the frozen compiler hashes. Remaining
completed programs 2–7/10 are running, with the same deadline check between runs.
All seven new native groups returned exit 0. No further implementation unit has
started; final source/fixture identities and the resumption reproducer are recorded.

Final artifact review: **524 source identities**, **613 native fixtures / 3,065
IR and expectation identities**, comprising 136 new fixtures / 272 new executions
and 477 previously verified fixtures with unchanged inputs. All 146 net added
managed cases pass per configuration. Baseline-only ownership body files and the
existing mixed-target test remain byte-identical to the starting worktree.
No specification, draft or milestone source edits; NativeAOT NOT_RUN. The review
found no remaining diagnostic scaffolding or unrelated source changes.

**Exact next implementation action after this execution:** reproduce
`dotnet Kimi/bin/Debug/net10.0/Kimi.dll check bin/plan-execution/20260917-122546/next-partial-terminal-branch.kimi`.
Both configurations currently report UnsupportedOwnership_Kd at **13:13** for
`if c => return else => x = 3` following a mixed-target join. Extend branch-prefix
tracking to partial-terminal selections only with an explicit normal-tail join;
keep terminal histories pending for enclosing extents, and never let them replace
the ordinary successor's state. Add missing-else, both branch orders, Move/let/
Loan and normal-tail effect tests before broadening the current proof. Partial
logical operands, unequal Loan joins, deferred cleanup and general effectful
divergence remain separate obligations. M2/M3 and the full compiler are incomplete.

<a id="execution-4"></a>

## Recorded timed continuation — broader ownership plan (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

**Final checkpoint: units 33–35 are DONE.** Verification finished at
**12:22:56 UTC**, **59m34s** after execution start. No further unit is started.
Both builds have zero warnings/errors and both full suites pass **7,961 tests**
(80 net added). LLVM/native regression covers **477 unique fixtures / 954 unique
O0/O2 executions**; all **2,385 fixture/expectation hashes** and **519 source
hashes** match final inputs. Programs **1/8/9/11 pass all 370 integration checks**
across Debug/Release; all eight reports match the final compiler identities.
Reload/warmed analysis and emission allocate zero bytes in the retained workloads.
Final documentation/workspace review finished at **12:25:16 UTC**, **61m54s** elapsed.
Workspace/diff review PASS. SPEC.md, specification chapters, draft and milestone
sources were not changed. NativeAOT was not run. Changes are uncommitted.

M2/M3 and the full compiler plan remain IN_PROGRESS. No implemented unit has
missing verification. **Exact next action:** reproduce
`dotnet Kimi/bin/Debug/net10.0/Kimi.dll check bin/plan-execution/20260917-112322/next-short-circuit.kimi`
(UnsupportedOwnership_Kd at 13:13), then extend `ScopedCheckingProof` only for
proven completing short-circuit operands. Preserve skipped-path joins, Move and
Loan histories; operands with transfers/divergence require separate proof.
That next unit has not been implemented. Region-changing terminal branches,
unequal Loan joins, deferred cleanup and general effectful divergence remain.

Started **11:23:22 UTC**, soft deadline **12:23:22 UTC** (60 elapsed minutes,
including inspection and verification). Fresh HEAD is `7869ce7`; the worktree was
clean. Re-read the execution instructions, current plan, repository rules,
specification and ownership implementation. The user selected continuation of
the broader compiler plan; program 11 remains complete and programs 12–14 are
not this execution's target. ObjectCallCompatible implementation remains deferred
by the latest SPEC.md update.

**Unit 33 / I6/I8 T4n-t DONE:** reproduced the unit 32 mixed-effect
failure, then preserved per-target checking state through straight-line effects
after a mixed join. Retain original transfer targets and pre-cleanup seeds;
never add runtime predecessors or use one merged state after target filtering.
Branching continuations, unequal Loan joins, deferred cleanup and general
effectful divergence remain separate obligations. Required evidence: focused
initialization/Move/let/Loan/cleanup and transfer-chain cases, reload/warm reuse,
Debug/Release builds and managed regression, LLVM/native O0/O2 fixtures.

At **11:34 UTC** (about 11 minutes), both builds have zero warnings/errors and
both full managed suites PASS **7,908** tests (27 net additional cases). The
original reproducer now passes. Native regression is running. Replays are retained
value records with a reusable stack; warmed reanalysis/emission allocates zero
bytes in the regression workload. No overall performance improvement is claimed.
Additional Loan inspection at **11:39 UTC** found that stored-borrow liveness
did not follow checking seed/replay links. A counter mutation followed by a later
use of its stored shared borrow was wrongly accepted. The unit now also propagates
liveness through separate pre-cleanup checking links; 346 focused ownership/Borrow
tests pass, including required conflict rejection and last-use positive controls.
Full regression must be rerun for this correction. Existing native fixtures will
be hash-compared after regeneration; new Loan fixtures require native verification.

Completed at **11:44 UTC** (about 21 minutes). Final clean Debug/Release builds
and full suites PASS **7,914/configuration** (33 net new tests). LLVM/native PASS
**492 Never + 414 Default + 8 new stored-Loan O0/O2 executions** (overlapping
families). All 2,145 earlier fixture/expectation hashes match regeneration after
the liveness fix. Stored-borrow reload/warmed analysis and emission also allocate
zero bytes. Diff review/check PASS. SPEC §14.10.3 and §15.6 already require this
behavior; no specification change is needed. NativeAOT is NOT_RUN.

**Unit 34 / I6/I8 T4n-u DONE**, started **11:44 UTC**: extend replay to
closed acyclic checking CFGs whose paths all reach the retained endpoint. The
fresh `next-mixed-branch.kimi` reproducer replaces the assignment with a completing
if/else and still fails UnsupportedOwnership_Kd at 13:13. Preserve separate
constituent states, normal join rules, and stored Loan liveness; reject cycles,
escaping paths and region-changing terminal branches until separately represented.
Required checks: positive/negative branch initialization and Move histories,
missing else, nested conditionals, target filtering, Loans, reload/warm reuse,
full managed suites and new native O0/O2 fixtures. At **11:50 UTC** (26m38s),
both clean builds and full suites PASS **7,943/configuration** (29 new cases).
All 2,165 prior fixture/expectation hashes match. Completed **11:51 UTC** with
**52 new LLVM/native O0/O2 executions PASS**; stored-Loan reload/warm reuse passes
without allocations. Cyclic continuation remains rejected by a fresh reproducer.

**Unit 35 / I6/I8 T4n-v DONE**, started **11:51 UTC** (about 28 minutes):
support closed same-region cyclic checking CFGs when every retained node can
reach the endpoint. Use the existing finite-state fixed-point solver independently
for each constituent. A forward closure and reverse reachability proof must reject
region escapes and terminal components that cannot reach the endpoint. Preserve
zero-iteration paths, Move/let histories and stored Loans; keep divergent scopes,
deferred cleanup and terminal branches crossing checking regions guarded. The
fresh `next-mixed-loop.kimi` still fails UnsupportedOwnership_Kd at 13:13.
Required verification: zero/multiple-iteration abstract histories, nested loops,
branch interaction, Loan/cleanup boundaries, reload/warm reuse, both full suites,
native O0/O2 fixtures and completed-program regression.

At **11:55 UTC** (about 32 minutes), both builds are clean and full suites PASS
**7,961/configuration** (18 new cases). All 2,295 earlier native fixture/expectation
hashes match; new loop fixtures and programs 1/8/9/11 are being verified. Closed
cyclic replay preserves zero-iteration and fixed-point histories. Proof storage,
replay states and worklists are reused; stored-Loan warm/reload tests pass.
Completed at **12:22:56 UTC**: all 36 new native runs and 370 completed-program
checks pass. All eight program reports match current compiler and source hashes.

Reproduction commands (configurations/native scripts run serially):
`dotnet build Kimigayo.slnx -c Debug --no-restore` and the Release counterpart;
`dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll` and the Release counterpart;
`./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Never*.ll'` and
`'*Default*.ll'`, plus recorded new Loan/branch/while fixture groups;
`./backend/windows-x64/test-milestone<N>.ps1 -Configuration <Debug|Release>`
for N = 1, 8, 9, 11. Native tools required an approved sandbox escalation after
the first opt.exe launch was denied; no verification remains environment-blocked.
See `verification.json`, `t-*`, `u-*`, `v-*`, and linked integration reports in
the evidence root. Native family counts overlap; the final unique count above
deduplicates their manifests. No new out-of-scope finding was introduced.

Evidence root: `bin/plan-execution/20260917-112322/`, with `t-*` records for unit
33 and `u-*` records for unit 34. Earlier execution deadlines and stop-at-program
instructions below are historical. Next action is the unimplemented short-circuit
reproducer above. The soft deadline elapsed during final documentation; only the
current unit's evidence and handoff records were completed after that point.

<a id="execution-5"></a>

## Recorded Program Milestone 11 completed (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Started **10:37:44 UTC**, soft deadline **11:37:44 UTC**. Latest target is program
11, independently of this plan's implementation-stage numbering. Re-read the
current instructions, plan, worktree and all fourteen program files. Preserved
all pre-existing Milestone 9 changes, untracked files and binary diff under
`bin/milestone11-work-20260917/`; HEAD is `1e13acc`.

**Target program 11 is DONE.** All required verification finished at
**11:13:44 UTC**, **36.02 minutes** after execution start. No later-program work
was started. Final documentation/workspace review finished at **11:14:50 UTC**
(**37.10 minutes** elapsed), within the 60-minute soft limit.

The initial unchanged-target reproduction failed Binding: explicit specialization at 7:21
was unsupported and the forwarding call at 10:52 had an unproven constraint.
Downstream gaps were initially predictions and were subsequently reproduced below.
Programs 1–10 supply existing scalar, ownership, array, enum and shared generic
foundations. Program 11 adds closed implementation selection through generic
forwarding, static immutable state and indexed shared borrowing. Programs 12–14
are design context only; mutable captures and Iterator work remain out of scope.

| Unit | State | Work and required verification |
| --- | --- | --- |
| P11-S | DONE | Closed Type specialization contracts: fifteen positive/negative binding/ownership tests, ambiguity and rebind invalidation; ordinary definitions remain universally checked. |
| P11-G | DONE | Selected implementations, shared direct-call adapters, checked i32 operations and indexed shared borrows; normal, boundary, recursion and corrupt-plan checks pass through native O0/O2. |
| P11-P | DONE | Verified pure immutable group scalar literal reads execute; mutable/effectful unsupported state and invalid writes/initializers reject emission. |
| P11-F | DONE | Both clean builds, 7,881 managed tests/configuration, 506 native fixtures and all completed-program/target integrations pass with current source/compiler identities. |

Expected target stdout is `Generic weights are 6, 3, 2.\n`, empty stderr and exit
0. **Remaining target work: None. Next action: None; stop at Milestone 11.**
NativeAOT is NOT_RUN as instructed. No required check was blocked by the environment.
Reproduction: `dotnet Kimi/bin/Debug/net10.0/Kimi.dll build milestones/Milestone11.kimi`.

**Final verification: PASS**

- Debug and Release solution builds: zero warnings/errors.
- Both full managed suites: **7,881 passed**; **37 new tests**.
- Related LLVM/native regression: **506 fixtures / 1,012 unique O0/O2 runs**;
  all **2,530 fixture/expectation hashes** match.
- Programs 1–10 plus target 11: **984 integration checks**. All **22 reports**
  match current compiler and program hashes. The target contributes 110 checks;
  the prior completed programs contribute 874.
- Target exact stdout: `Generic weights are 6, 3, 2.\n`; stderr empty; exit 0.
- Shared-body identity and selected-callee inspection: four unchanged-source
  Debug/Release O0/O2 IR artifacts pass; retained in `shared-generation.json`.
- Checked specialization contract/ambiguity/duplicate/invalidation failures,
  universal ordinary-body checking, ordinary overload selection, recursive
  specialization calls, length forwarding, non-Copy element borrowing, empty
  arrays, bounds Abort, i32 overflow and corrupted ownership/value plans.
- `git diff --check`: PASS. All Milestone sources and draft are unmodified by
  this execution. Concurrent documentation edits are preserved as noted below.

Evidence: `bin/milestone11-work-20260917/verification.json`, named build/test/native
logs, source and fixture manifests, linked integration reports and
`shared-generation.json`. Earlier unit states are archived in
`PLAN-work-unit-history.md`. Reproduce the full target integration:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone11.ps1 -Configuration Release
```

**Retained boundaries:** no effectful/mutable static initialization, static address
identity, length/receiver/constrained/defaulted specialization headers, explicit
specialization Origin binders or dependent owned-result forwarding was added.
Those unsupported paths still reject generation/verification rather than weakening
the language. Concrete call-context expansion is bounded at depth 128. Shared
execution allocates fixed stack scratch and no per-element heap objects; no new
allocation-performance claim is made. Programs 12–14 remain out of scope.

### Implementation checkpoints (historical within this execution)

At 10:49 UTC (about 12 minutes), the unchanged target passes Binding. Confirmed
ownership blockers: immutable static read, concrete scalar reference and indexed
shared element borrowing. Added concrete scalar reference representation to the
existing reference family; explicit `@ref/i32` tests now pass. Scalar `@ref`
shorthand remains outside this unit. Next: represent static constant reads and
indexed references with retained checked ownership operations, then shared calls.

At **10:56:43 UTC** (18.98 minutes), the unchanged target builds, LLVM-verifies and
runs natively at O2 with exact expected stdout and exit 0. Five focused fixtures
pass LLVM verification and **10 O0/O2 native executions**. The static scalar path
only folds verified immutable integer/bool literal initializers with no observable
initialization effect; effectful initializers, mutable static storage and static
address identity remain unsupported rather than being eagerly executed or omitted.
Forwarding metadata chooses concrete entry adapters while retaining one checked
shared body per ordinary generic definition. Indexed borrows retain evaluated
receiver/index snapshots and runtime bounds checks without requiring Copy on T.
Next: semantic/negative/corruption boundary coverage and full completion checks.

At **11:08 UTC** (about 31 minutes), both Debug/Release builds have zero warnings
or errors and both full managed suites pass **7,881 tests** (37 new). Target
integration passes **55 checks/configuration**, including byte-identical O0/O2
copies, native and CLI execution, changed default/specialized weights, another
specialization key, empty/singleton arrays, renamed identifiers and ten invalid
inputs rejected before artifact emission. Four unchanged-source Debug/Release
O0/O2 IR artifacts contain exactly three shared generic bodies, nine concrete
entries, six forwarding adapters and one selected specialization call; identities
and hashes are retained in `shared-generation.json`.

Broader forwarding with a dependent owned result still lacks retained result
storage and explicitly rejects generation; a negative test records this boundary.
New reference support is limited to the checked input/projection Origin family;
existing unsupported static-Origin scalar payload tests remain unchanged and pass.
Native diagnostic expected columns were corrected to the actual expressions
(indexed borrow 2:79; i32 addition 1:41). Program/spec expectations are unchanged.

During final verification (11:10 UTC), an independent workspace edit changed
ObjectCompatible/RuntimeUsable terminology in SPEC.md and related chapters to
ObjectCallCompatible/ObjectViewCompatible. Inspected and preserved those edits;
they do not change this target's specialization, storage or static-read semantics.
The observed diff is recorded in `concurrent-specification.patch`. This execution
has not edited SPEC.md, specification chapters or draft. Existing Milestone 9
files remain preserved; overlap is limited to documented shared lowering and
progress/README updates.

<a id="execution-6"></a>

## Recorded Program Milestone 9 completed (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Execution started **09:56:24 UTC**, soft deadline **10:56:24 UTC**. All required
verification completed at **10:34:17 UTC** (37.88 minutes). Documentation and final
workspace review finished at **10:36:22 UTC** (**39.97 minutes** elapsed). Target program 9 is **DONE**;
no later-program implementation was started.

At startup, re-read AGENTS.md, `prompts/implementation-execution.md`, current HEAD
`1e13acc`, this plan, implementation and verification environment. The worktree was
clean; baseline/status and the previous plan are preserved under
`bin/milestone9-complete-20260917/`. Read all fourteen current program files (the
repository has fourteen, not five). Programs 1–8 provide ownership, borrowing,
arrays and generic storage; completed program 10 provides enum layouts/patterns.
Programs 11–14 remained design context. These program numbers are distinct from
this plan's broader implementation stages.

The initial current-compiler reproduction failed ownership on `Hit<T>` at 20:21
and its cases at 25:28/26:20. Subsequent confirmed blockers were shared generic
destruction, range storage/iteration and common-function calls. Each was addressed
through checked ownership plans, shared lowering, LLVM verification and native
execution; no filename, constant, output or source-program special case was added.

| Item | State | Completed target scope |
| --- | --- | --- |
| P9-C1/C2 | DONE | Retained checked common-function calls and inline scalar/Unit capture conversion. |
| P9-G1/G2/G3 | DONE | Retained borrowed field Origins and Copy element reads through shared offset/size/length policies. |
| P9-G4 | DONE | Finite symbolic enum ownership, selected tag/payload construction, results and active-case destruction. |
| P9-G5 | DONE | Shared range iteration, scalar SSA snapshots, checked isize addition, receiver Loans and concrete callback ABI adapters. |
| P9-G6 | DONE | One checked generic destructor body when receiver fields are unobserved; instantiated field cleanup runs afterwards. |
| P9-G | DONE | All shared-generation work required by the unchanged target. |
| P9-F | DONE | Current-source Debug/Release build, full managed regression, LLVM/O0/O2 native execution, invalid inputs and completed programs. |

The unchanged target has exact stdout:

```text
Found 6 at index 3.
Missing value handled.
Search finished.
Batch destroyed.
```

Stderr is empty; exit code is 0. The messages verify the successful search, empty
exhaustion and defer-before-destructor order. Target integration passes **59 checks
per configuration**, covering byte-identical O0/O2 copies, native and both CLI run
forms, first/last/singleton/absent results, nonempty exhaustion, i64 instantiation,
Abort without cleanup and eight invalid programs rejected before artifact emission.

**Final verification: PASS**

- Debug and Release solution builds: zero warnings/errors.
- Both complete managed suites: **7,844 passed** (32 new tests).
- Related LLVM/native regression: **492 fixtures / 984 unique O0/O2 executions**;
  all **2,460 IR/expectation hashes** match the generated fixtures.
- Completed programs 1–8 and 10 plus target 9: **874 integration checks**. All
  twenty reports match the current compiler and program hashes; target accounts
  for 118 checks and prior completed programs for 756.
- Coverage includes owned active enum cleanup, nested destructor ordering,
  empty/single/multiple iterations, retained scalar snapshots, overflow diagnostics,
  different scalar callback ABIs, repeated/multiple/zero arguments and Unit results,
  wrong Types/arity/lengths, missing Copy, moved values and corrupt retained plans.
- `git diff --check`: PASS. NativeAOT: NOT_RUN as instructed. SPEC.md, draft and
  every Milestone program are unchanged.

Evidence: `bin/milestone9-complete-20260917/verification.json`, native manifest and
named logs; integration reports are linked from that JSON. Intermediate work-unit
states are archived in `PLAN-work-unit-history.md` there. A malformed callback test
was corrected to valid syntax. An overflow fixture's expected source column was
corrected to the start of `n + 1` (47). Neither correction changes the target or
its expected behavior.

**Remaining target work: None. Next action: None; stop at Milestone 9.** Reproduce:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone9.ps1 -Configuration Release
```

**Retained boundaries:** shared callback adapters accept direct scalar ABI
parameters and bool/isize/Unit results. Generic destructors that observe fields,
heap/owned/borrowed capture environments, arbitrary Callable witnesses and later
program features remain explicitly unsupported where previously unsupported.
ResolvedRange support added here is internal shared storage, not new ordinary
function-signature support. No allocation-performance improvement is claimed.
No additional unrelated specification issue was taken into scope.

<a id="execution-7"></a>

## Recorded Historical execution: program Milestone 9 resumed after program 10 (2026-09-17, incomplete at that checkpoint)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Execution started at **08:55:11 UTC**, with a **09:55:11 UTC** soft deadline.
Execution closed at **09:54:45 UTC** (59.57 minutes elapsed), after finishing the current implementation, verification and documentation units. No additional enum implementation unit was started in the remaining sub-minute window. The target remains incomplete.

The latest explicit target is program 9. Re-read current HEAD (`cc2cb04`), worktree,
AGENTS.md, execution instructions, this plan and the implementation before work.
Preserved pre-existing changed/untracked files and binary diff under
`bin/milestone9-final-20260917/baseline/` and `baseline.patch`. All fourteen
milestone programs were read (the repository contains fourteen, not five).
Programs 1–8 provide ownership, borrowing, arrays and shared generic storage;
completed program 10 provides enum layouts and nested patterns. Programs 11–14
remain design context. Plan stage IDs and program numbers are distinct.

At execution start, `dotnet Kimi/bin/Debug/net10.0/Kimi.dll build milestones/Milestone9.kimi`
first failed final Binding at 24:20 (`NotCallable_Kd`), with additional anonymous
function/capture failures. See `initial-target.log`. The original program was not
changed. Its required stdout remains:

```text
Found 6 at index 3.
Missing value handled.
Search finished.
Batch destroyed.
```

Required stderr is empty and exit code is 0. **The target has not executed.**

| Item | Current state | Implemented and verified scope |
| --- | --- | --- |
| P9-C1 | DONE | Checked common-function invocation Binding; positional signature, adaptation and retained acquisition plans. |
| P9-C2 | DONE (target closure subset) | Scalar/Unit snapshots, inline environment word/operations table, checked indirect calls, moves, returns, cleanup and call-wide shared Loans. |
| P9-G1 | DONE | Concrete borrowed struct field addresses and declared result Origins. |
| P9-G2 | DONE | Symbolic `ref/T` field addresses through universally checked shared bodies and instantiated offset metadata. |
| P9-G3 | DONE | Borrowed fixed-array Copy element reads through shared size/length policies, including bounds checks for zero-sized elements. |
| P9-G | IN_PROGRESS | Generic enum result/construction, shared iteration/callback adaptation and generic destructor generation remain. |
| P9-F | IN_PROGRESS | Current foundation regression complete; unchanged target LLVM/native verification remains blocked by implementation. |

**Current target stage:** final Binding passes in Debug and Release. `Batch.view()`,
the generic element read and common-function call pass ownership. First remaining
failure is ownership of `Hit<T>` at 20:21, followed by `.Found`/`.Missing`
construction at 25:28 and 26:20. `g3-target.log` and `target-release.log` retain the
current diagnostics. Target LLVM generation, expected output, exit and cleanup
side effects remain **UNVERIFIED**; Milestone 9 is not DONE.

**Verification checkpoint (09:52 UTC, about 57 minutes elapsed):**

- Debug/Release solution builds: zero warnings and errors.
- Both complete managed suites: **7,812 passed**, including 68 new tests. Final
  strengthening of projected-offset fixtures also passes all ten focused tests
  in both configurations; compiler implementation and test count are unchanged.
- Related LLVM verification/linking/native checks: **471 fixtures / 942 unique
  O0/O2 executions**, with **2,355 matching IR/expectation hashes**. Two projected
  fixtures were additionally rerun after strengthening their nonzero-offset case.
- Completed programs **1–8 and 10**: **756 Debug/Release integration checks**;
  all eighteen reports match current compiler and program hashes.
- Positives, invalid captures/types/arity, move/Loan conflicts, invalid lifetime,
  unproved Copy, bounds failures, corrupt plans, reload and reanalysis are covered.
  Final callback tests check receiver/argument/body order, nonexecution of a Never
  body during creation, and explicit rejection of oversized environments.
- Warm closure Binding/ownership are allocation-free in the focused measurement.
  The test excludes the previously recorded 72-byte require-expression Binding
  baseline. Shared module construction is not claimed allocation-free.
- `git diff --check` passes. NativeAOT was not run. SPEC.md and draft are unchanged.

Evidence is in `bin/milestone9-final-20260917/verification.json`, its linked
integration reports and named logs. Intermediate work-unit checkpoints are retained
in `PLAN-work-unit-history.md` there. The independently changed Milestone11 example,
its README entry and STATUS entry were preserved; this execution implemented no
later-program functionality.

**Boundaries:** common-call generation currently accepts scalar parameters and
scalar/Unit/Never results. Closure conversion requires a fixed expected common
signature with explicit parameter Types and scalar/Unit captures fitting the
8-byte inline environment. Owning/borrowed captures, heap environments, general
concrete closure values, inferred parameters and public borrowed callable
signatures remain explicitly unsupported.

**Next exact action:** reproduce with
`dotnet Kimi/bin/Debug/net10.0/Kimi.dll build milestones/Milestone9.kimi`, then
implement finite symbolic enum payload ownership in `OwnershipAnalysis.Enums.cs`
and shared enum tag/payload/result policies in `GenericStoragePlan.cs` and its
writer. Preserve Copy/Move, active-case cleanup and universal definition checks.
Then implement shared range iteration, common-call ABI adaptation and generic
`Batch<T>` destruction, rerunning the unchanged target after each coherent unit.

Focused independent reproducers are saved as `remaining-enum.kimi`,
`remaining-iteration.kimi`, `remaining-callback.kimi` and `remaining-destructor.kimi`
with corresponding logs in the evidence directory. The enum reproducer fails
ownership; the other three pass ownership and reproduce separate shared-generation
boundaries. Those are **isolated reproducer failures**, not evidence that the
Milestone 9 target has reached generation. Do not advance to another program.

<a id="execution-8"></a>

## Recorded Program Milestone 10 integration (2026-09-17, complete)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Resume audit: **DONE**, started 2026-09-17 08:32:05 UTC with a 60-minute soft
deadline of 09:32:05 UTC; closed at approximately 08:33 UTC (one minute elapsed).
Re-read current HEAD/worktree, AGENTS.md, the execution prompt, latest plan and
status. All P10 items remain DONE; no executable work remains within the explicit
program-10 scope. Preserved current changed/untracked files and binary diff under
`bin/milestone10-resume-20260917-083205/`. Read-only evidence checks PASS: all
eighteen integration reports match current compiler/source hashes, and all 2,220
fixture/expectation hashes match. `git diff --check` passes. No implementation
change or new failure requires repeating successful builds/tests; none were rerun.
Only this plan checkpoint was added. No remaining target action; stop without
starting program 9 or programs 11–14. Product support and STATUS.md are unchanged.
Repeated resume check at 08:33:15 UTC (deadline 09:33:15 UTC), closed at
08:33:40 UTC, about 25 seconds elapsed: instructions and current state re-read;
all 64 changed/untracked files other than this plan match the saved checkpoint.
The previous plan was preserved as `PLAN-before-083315.md` in the audit directory.
Diff check PASS. No newly executable in-scope item or changed implementation was
found; completion and the stop-at-program-10 scope remain unchanged. No tests rerun.

Execution started at 07:47:36 UTC. The preceding execution's 60-minute soft limit
is retained for this continuation (08:47:36 UTC); after it, finish only the current
coherent unit and its required verification/documentation. Program numbers are
independent of this document's compiler stages. Current HEAD is `cc2cb04`.
All pre-existing changed/untracked files and the binary diff were preserved in
`bin/milestone10-work-20260917/baseline/` and `baseline.patch` before changes.
AGENTS.md, the execution prompt, current PLAN and implementation were re-read.
There are fourteen milestone sources rather than five; all fourteen were read.
Programs 1–8 supply scalar/control-flow, ownership, borrowing, arrays and generic
storage. Program 9 remains incomplete, but its callback functionality is not a
dependency of program 10. Program 10 requires concrete generic enum layout,
nested tuple/payload patterns, guard candidate reads, Copy array iteration and
existing cross-selection/loop transfers. Programs 11–14 are design context only.

At execution start, the Debug compiler first failed final Binding on the target's match and
its nested-payload guard at 17:31 / 18:45 (`UnsupportedBinding_Kd`). This is an
observed failure. Ownership currently gates tuple patterns; emission gates enum
layouts and composite match plans. These are inspected implementation boundaries,
not evidence that the whole target has reached those phases. Expected stdout is
`Accepted total is 12.\nPattern run finished.\n`, empty stderr and exit 0.

- **P10-1 DONE (Binding/ownership subset):** bind nested guarded patterns and retain checked candidate
  projections and tuple decomposition through ownership, with positive/negative tests.
- **P10-2 DONE (target subset):** concrete enum storage/construction and composite match generation,
  using existing layout/ownership plans and checking LLVM/native O0/O2 behavior.
- **P10-3 DONE:** integrate Copy aggregate array iteration and target transfers;
  verify unchanged target and focused runtime/invalid-input variants.
- **P10-V DONE:** final Debug/Release regression, completed programs 1–8, documentation.

No program 9 completion or later-program-specific implementation is claimed.

At 07:55 UTC, 249 focused Binding/ownership/guard tests pass. Recursive candidate
indexing preserves separate guard/body identities; scalar candidate projections
do not acquire payload ownership. Owned tuple decomposition reuses the existing
selected-case ownership path. Two prior unsupported tuple tests are now positive;
the exhaustive guarded enum Binding test now expects successful Binding. The
unchanged target passes Binding and first fails ownership at aggregate array
iteration (16:9). No target generation or execution has passed yet.

At 08:07 UTC (20 elapsed minutes), the unchanged target passes ownership, LLVM
verification, native linking and ordinary Debug/O2 CLI execution: exact expected
lines and exit 0. Enum layout/construction passes five O0/O2 fixtures (10 runs);
composite scalar candidate patterns pass four O0/O2 fixtures (8 runs). Composite
match generation currently requires storage without destruction; whole enum
construction/transfer/destruction supports active Non-Copy payloads independently.
Pattern tests short-circuit from outer tags to inner literals before guard reads;
selected bindings then acquire from checked payload subslots. Copy aggregate array
iteration snapshots its input and copies each element. Final acceptance remains
pending O0/O2 target variants, invalid plans/inputs and final regression.
The first full managed run found six warm-allocation regressions from interface
enumeration and one formerly unsupported tuple-pattern expectation; these are
being corrected without weakening allocation or invalid-input checks.

At 08:21 UTC (34 elapsed minutes), the dedicated program-10 script passes 54
Debug checks: original source, byte-identical renamed O0/O2 copies, alternate
values, exhaustion, empty input, immediate Stop, guard cleanup, Abort, and nine
invalid inputs. Expanded native fixtures exposed an undefined zero-size array
slot; value-only Unit iteration now computes an unused zero-offset address without
requiring storage, preserving bounds/length behavior. LLVM/O0/O2 confirmation
passes. Six malformed composite-plan tests and two malformed construction-plan
tests reject before writing; reanalysis restores valid plans. Reload preserves
IR, and warm nested Binding, ownership and physical writing allocate zero.
The earlier allocation regressions were fixed with indexed declaration traversal.
Module preparation retains physical descriptors and is not claimed allocation-free.
Final solution builds are clean; full managed suites and current-identity native
regression are being completed. No source-level special cases or target edits were
introduced; callbacks and later-program-only functionality remain out of scope.

Final acceptance at 08:31 UTC (approximately 44 elapsed minutes): the unchanged
program passes current-source Debug/Release builds, final Binding, ownership,
LLVM verification/linking, ordinary native O0/O2 execution and both CLI run forms.
Exact stdout is the two expected lines above, stderr is empty, and exit is 0;
the deferred cleanup line follows the accepted-total line. Both solution builds
have zero warnings/errors; both full managed suites pass 7,744 tests, with 44 new
tests and no failures/skips. Related LLVM/native regression passes 444 fixtures /
888 O0/O2 executions, and all 2,220 final IR/expectation hashes still match.
Dedicated program-10 integration passes 54 checks per configuration (108 total),
including nine invalid inputs per configuration. Completed programs 1–8 pass
648 Debug/Release checks. All eighteen integration reports match current compiler
and milestone source SHA256 values. Evidence and final logs are retained in
`bin/milestone10-work-20260917/verification.json` and its containing directory.

Required checks: PASS. NativeAOT: NOT_RUN, as instructed. No required check is
environment-blocked or unverified. No target work or blocker remains. Reproduce
target acceptance after a corresponding solution build with
`./backend/windows-x64/test-milestone10.ps1 -Configuration Debug` and
`./backend/windows-x64/test-milestone10.ps1 -Configuration Release`.
The next action for this request is to stop at program 10. Program 9 remains
incomplete; programs 11–14 were not implemented. Composite match generation with
owned destruction or borrowed candidate paths remains an explicit unsupported
boundary outside this target. SPEC.md, draft files and milestone program sources
were not changed. Existing uncommitted work remains preserved.

<a id="execution-9"></a>

## Recorded Program Milestone 9 integration (2026-09-17, in progress)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

### Timed continuation: 2026-09-17 06:41:07 UTC

This execution has a 60-minute soft wall-clock limit (07:41:07 UTC).
Current HEAD remains `cc2cb04`. All pre-existing modified and untracked source
files were copied to `bin/milestone9-resume-20260917/baseline/`; the binary diff
is retained beside them. The execution prompt, AGENTS.md, current plan and
implementation were re-read before changes. The execution baseline Debug build
passed with zero warnings/errors; 109 focused length/generic tests passed. The
unchanged target first failed at 24:20 (`NotCallable_Kd`).

- **P9-L1 DONE (Binding subset):** retain separate call length substitutions, substitute
  dependent fixed-array lengths, and test explicit/inferred arguments, invalid
  formation, overload selection, rebind/reload and allocation behavior. Binding
  completion of this unit does not imply complete Milestone 9 execution.
- **P9-L2 DONE:** carry the admitted length-dependent storage subset through
  universal ownership and shared generation, with LLVM and native O0/O2 evidence.
- **P9-C TODO:** common-function invocation/captures, generic result flow and
  remaining borrowed generic operations, followed by full target execution.
- **P9-L3 DONE:** lower fixed-array length metadata in the shared body,
  including checked shared borrowed-array input representation; verify empty,
  nonempty and zero-sized-element lengths through native O0/O2 execution.
- **P9-L4 DONE:** preserve kind-directed explicit argument lookup when a Type and
  length constant share a name, including nested transparent grouping. Final
  Debug/Release suites pass all five namespace-selection regression cases.
- **P9-V DONE:** final Debug/Release builds, managed suites, fixture hash
  equivalence and completed-program reports match the current source/compiler.
  This verification closes the implemented subset only; program 9 is incomplete.

The historical evidence below describes the preceding execution; new evidence
is recorded in this subsection and `bin/milestone9-resume-20260917/`.

At 07:00 UTC (19 elapsed minutes), P9-L1 implements explicit and inferred length
slots, literal fitting, checked signature substitution, normalized commutative
length expressions and definition-side formation checks. Length substitutions
are retained independently from nullable Type slots and passed into constraints
and storage instantiation. Inapplicable formation conditions eliminate overload
candidates. Rebinding revokes stale call plans; reload and warm zero-allocation
tests pass. Forty-one new tests cover these behaviors and negative boundaries.
Debug build is clean and the complete suite passes 7,668 tests. Two existing
negative call diagnostics now identify a known argument-kind mismatch as
NoApplicableOverload instead of unresolved type information; type-constructor
diagnostics remain unchanged. A normalization test now finds the constant operand
independently of canonical operand order. The unchanged target still first fails
at callback invocation, so complete target execution remains unverified.
P9-L2's initial current-source CLI probe produces an incomplete build manifest;
the next focused tests will identify the ownership/emission boundary precisely.

At 07:10 UTC (29 elapsed minutes), P9-L2 passes 17 focused tests and nine
LLVM/native fixtures at O0/O2 (18 executions); reload adds a tenth fixture for
the final native run. Whole length-dependent arrays use opaque storage policies
in the existing shared CFG, including Copy/Move, empty/nested arrays, choices
and reverse destruction. Length formation obligations retain their checked
expression so ownership accepts only the Binding-certified subset. Entry identity
also retains lengths. Invalid generic reuse is rejected by ownership errors,
not by an unsupported-operation gate. Current-source full regression is running.
Warm length-call Binding and ownership allocate zero after removing boxing in
obligation traversal. Shared module construction still allocates existing plans;
an independent require-expression Binding path without length generics allocates 72 bytes
per analysis and is outside this unit. Neither is claimed allocation-free.

At 07:18 UTC (37 elapsed minutes), P9-L3 implements a logical-length field in
the shared policy, independent of byte size/stride. Shared borrowed-array entries
store the incoming pointer value in their argument slot instead of interpreting
the referent as owned argument storage. Admission is limited to explicitly
declared shared-array inputs; generic T instantiated with a borrow remains gated.
Empty arrays, zero-sized elements and ordinary arrays pass initial native O0/O2
checks. Five additional negative/plan-corruption tests cover uninitialized input,
wrong receiver, wrong operation, unexpected index and replaced receiver producer.
Current Debug/Release solution builds are clean; all 7,695 Debug tests pass.
Final Release tests, native fixture checks and programs 1–8 integration are next.
The unchanged target still fails at callback invocation in both configurations;
target ownership and execution remain unverified. No subsequent milestone is
being implemented.

At 07:25 UTC (44 elapsed minutes), P9-L4 restores Type-only lookup when a same-name
integer value exists, while length-only declarations continue to resolve the
value namespace. Three added tests cover these cases and the explicit boundary:
overloads offering both slot kinds for a spelling present in both namespaces
remain Unsupported until candidate-local dual-namespace binding is implemented.
This prevents silently selecting the length overload. It is not needed by the
target program. The unused Type-only substitution wrapper was removed. Both
managed suites pass 7,698 tests; final formatting/build identity and programs
1–8 integration are being refreshed before handoff.

Current next implementation action: reproduce `accepts(value)` in an isolated
common-function-parameter test, commit a checked indirect-call plan (including
argument acquisition), then implement captures/erasure and its ownership/ABI.
Continue with Copy-constrained borrowed element reads, borrowed generic field
addresses, generic enum results and shared iteration/calls. Do not rebind generic
bodies per concrete instantiation. The unchanged target's required stdout is
`Found 6 at index 3.\nMissing value handled.\nSearch finished.\nBatch destroyed.\n`,
with empty stderr and exit 0. None of those complete-target effects is verified.

At 07:31 UTC (50 elapsed minutes), final Debug/Release builds have zero warnings
or errors and both full suites pass 7,698 tests. All 715 IR/expectation hashes
match the 143 LLVM/native-verified fixtures (286 O0/O2 executions), so current
regeneration is identical in both configurations. The length-transfer probe also
passes ordinary Release CLI `build` followed by `run` with exit 0. Programs 1–8
pass all 324 Release integration checks; final Debug reports are being refreshed
after the formatting-only build identity change. No implementation unit will be
expanded while this final verification/handoff unit is in progress.

Independent current-source resumption probes are retained in the continuation
directory: `next-indirect-call.kimi` fails Binding with NotCallable at 1:60;
`next-capture.kimi` fails anonymous-function Binding;
`next-generic-borrow.kimi` reaches and fails ownership for `ref/T` field access;
`next-enum-result.kimi` reaches generation and fails the enum-container boundary.
`next-copy-read.kimi` fails ownership for a Copy-constrained generic element read;
`next-shared-iteration.kimi` reaches generation and fails shared generic iteration.
These are observed subset failures, separately from the unchanged target's first
Binding failure. They do not establish that later target phases have passed.

Resume with:

```powershell
dotnet build Kimigayo.slnx -c Debug --no-restore
dotnet Kimi/bin/Debug/net10.0/Kimi.dll build bin/milestone9-resume-20260917/next-indirect-call.kimi
dotnet Kimi/bin/Debug/net10.0/Kimi.dll build milestones/Milestone9.kimi
```

The next call plan must retain a shared invocation of the owned common-function
environment instead of moving the callback on each iteration. Follow SPEC 7.6
and 21.2.5: 16-byte common-function handle, layout-selected inline/heap environment
and 24-byte operations table, with capture acquisition and cleanup verified before
erasure. Existing direct `BoundCall.Target` assumptions must not be bypassed by
merely removing NotCallable or anonymous-function Unsupported diagnostics.

At 07:36 UTC (55 elapsed minutes), final review reproduced the same Type/value
namespace regression through parenthesized explicit arguments (`use<(Size)>()`).
The current P9-L4 unit now unwraps transparent grouping before selecting the
declared slot kind, with nested-group Type and length tests. Final builds and
regressions are being refreshed. If verification crosses 07:41:07 UTC, finish
this current unit and its records only; do not start callback implementation.

At 07:40 UTC (59 elapsed minutes), the final grouping correction passes clean
Debug/Release builds and 7,700 managed tests per configuration (73 added in this
continuation). All 715 regenerated IR/expectation hashes still match the 143
LLVM/native-verified fixtures and their 286 O0/O2 executions. The unchanged target
still fails NotCallable at 24:20 in both configurations. Completed-program reports
are being refreshed against the final compiler identities. Sandboxed LLVM startup
was denied; the authorized ordinary native verification is running outside that
sandbox. Only this current verification/documentation unit will finish after the
soft deadline; callback implementation remains the next execution's work.

At 07:41:07 UTC the soft limit elapsed. No new implementation unit was started.
The already-running final completed-program verification and handoff records are
the only remaining work in this execution.

Final handoff at 07:47 UTC (approximately 66 elapsed minutes): P9-L1 through
P9-L4 and their verification are complete within the explicitly bounded subsets.
Both final solution builds have zero warnings/errors; both managed suites pass
7,700 tests, including 73 added cases. LLVM verification and ordinary native O0/O2
checks pass 143 related fixtures / 286 executions; all 715 regenerated IR and
expectation files match the saved native-verified inputs. Programs 1–8 pass
25/21/37/33/47/63/52/46 checks respectively in each configuration (648 total).
All sixteen integration reports match the final compiler and milestone-source
SHA256 values. The evidence summary is
`bin/milestone9-resume-20260917/verification.json`; complete logs and the original
worktree backup are retained beside it. `git diff --check` passes. NativeAOT was
not run, and SPEC.md, milestone programs and draft files are unchanged.

The timed execution stops here after completing only the verification/documentation
unit already in progress at the deadline. Milestone 9 remains IN_PROGRESS: its
first current-source failure is still Binding `NotCallable_Kd` at 24:20 in both
configurations. Whole-target ownership, LLVM/native generation, expected stdout,
exit 0 and cleanup remain unverified. P9-C is the next unit; use the exact resume
commands above to begin checked common-function-value invocation, then captures,
Copy-constrained reads, generic borrowed fields, enum results and shared iteration.
No subsequent program milestone was started.

### Previous execution checkpoint

The target is `milestones/Milestone9.kimi`, independently of this plan's M1–M17
stages. Initial HEAD was `cc2cb04`; the worktree was clean and no uncommitted
changes needed archiving. Root AGENTS.md and all fourteen current programs were
read (the request referred to five). Programs 1–8 provide scalar execution,
control flow, ownership/destruction, borrowing, sequences and generic storage.
Program 9 adds length/type arguments, Copy-constrained array reads, common
function callbacks with immutable captures, generic enum results and borrowed
generic storage. Programs 10–14 inform the design but are outside this request.

The current-source Debug baseline built without warnings or errors. The first
failure was `MissingOriginBinding_Kd` at 11:35 (`self.value@ref/T`); additional
Binding failures involved borrowed-array metadata/indexing, callable values,
length arguments and captures. Ownership and emission were not reached, so
their expected missing support was not classified as an observed target failure.
The 148 existing focused generic-storage, sequence and reference tests passed.
Evidence is retained under `bin/milestone9-work/`.

Implementation order: (1) explicit borrow adaptation and borrowed-array access;
(2) length argument substitution and universal Copy acquisition; (3) callback
capture/erasure/invocation and enum result flow; (4) shared generation, LLVM
verification/linking and exact O0/O2 native execution of the unchanged target.

Completed foundation: explicit `@ref/T` and `@uniq/T` derive omitted outer Origins
from the operand while checking referent compatibility and written Origins.
Concrete fixed-array references support metadata snapshots and checked scalar
reads through ordinary borrow lifetimes and the existing pointer ABI. Empty
borrowed arrays retain aligned nonnull backing storage. Exclusive array reads do
not consume the reference, and a live child reborrow suspends parent access.
The new fixtures cover empty/nonempty arrays, forwarding, immediate temporaries,
returned references, bounds Abort, mutation/escape/type/initialization rejection,
reload and zero-allocation warm analysis/emission. This does not implement
generic borrowed field addressing or the complete program.

**Program 9 remains incomplete at final Binding.** The latest target diagnostic
starts at 24:20, `NotCallable_Kd` for `accepts(value)`. Length-generic invocation,
anonymous functions/captures and dependent result patterns still fail Binding.
Target ownership verification, LLVM generation, linking, expected stdout,
exit status and destruction order have not been verified. Native subset fixture
success must not be interpreted as completion of this milestone.

Next implementation work: retain separate length substitutions alongside Type
substitutions in committed call plans and substitute fixed-array length expressions;
then add checked common-function invocation and capture acquisition. Existing
`TryCandidate` explicitly defers length parameters, `BindFunction` rejects anonymous
functions, and `GenericStoragePlan` currently accepts only owned storage transfers
and boolean control flow. Borrowed generic field addresses, common-function ABI,
generic enum layout/results and shared length-dependent reads need corresponding
ownership and physical plans, not per-instantiation source rebinding.

Reproduce the target from current source:

```powershell
dotnet build Kimigayo.slnx -c Debug --no-restore
dotnet Kimi/bin/Debug/net10.0/Kimi.dll build milestones/Milestone9.kimi
```

No milestone source, language specification or draft is changed. NativeAOT tests
are excluded. Final Debug/Release solution builds have zero warnings/errors;
both complete managed suites pass 7,627 tests without failures or skips,
including 33 new tests. Initial full regression exposed a boxed enumerator in
the new empty-storage scan; indexed traversal removes the allocation, and all
existing and new warmed zero-allocation checks pass. A malformed sequence plan
that substituted a different reference producer is now rejected before emission.

LLVM verification and native O0/O2 regression pass for 3 TypedBorrow, 11
BorrowedArray, 10 BorrowStruct, 23 Sequence, 50 Reference, 18 Struct and 13
GenericStorage fixtures: 128 fixtures and 256 executions. Boundary fixtures
check exact source-position diagnostics and exit 1; successful fixtures check
exact output and exit 0. Final Debug/Release regeneration matches all 640 saved
IR/expectation-file hashes. The initial LLVM sandbox execution denial was resolved
through authorized execution; these subset checks are not environment-blocked.
Target execution is unverified because target compilation still fails.

Completed programs 1–8 pass 25/21/37/33/47/63/52/46 integration checks in each
configuration (648 total), including LLVM verification, O0/O2 native and CLI
execution and invalid-input rejection. Every final integration report matches
its current compiler and source hashes. Debug integration was repeated after
the final borrowed-generic ABI guard and empty-storage scan adjustment; no
compiler changes followed. Logs, hashes, target failure records and the explicit
incomplete summary are under `bin/milestone9-work/verification.json`.

<a id="execution-10"></a>

## Recorded Program Milestone 8 integration (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

This request targets `milestones/Milestone8.kimi`, independently of this plan's
M1–M17 stages. HEAD `cf6fa41` and the initial worktree were clean, so there were
no uncommitted changes to archive. Root AGENTS.md and all fourteen current
programs were read (the request referred to five). Programs 1–7 supply scalar
execution, functions, destruction, borrowing, transfers and sequence views.
Program 8 requires nested groups, generic construction/ownership and direct
fixed-array iteration. Programs 9–14 require additional callbacks, enums,
length generics, specialization, Iterator and object support; those remain outside
this checkpoint. No draft or specification program is modified.

The current-source Debug baseline built cleanly and 174 focused tests passed.
The unchanged target first failed final Binding at generic construction calls
and direct `for` iteration. Ownership and generation were initially untested,
not established failures. Evidence is under `bin/milestone8-work/`.

**Program 8 COMPLETE.** The unchanged target passes Binding, universal ownership
checking, LLVM verification, linking and ordinary native O0/O2 execution. Exact
stdout is `Chosen item.\nBoxed array total is 12.\nGeneric scope finished.\n`,
with empty stderr and exit 0. Generic field acquisition retains CopyOrMove path
state; deinit-bearing partial acquisition and possible reuse are rejected at
definition. Generation shares each admitted generic storage CFG, with concrete
ABI entries, deduplicated symbolic type policies, immutable 8-byte context slots
and operation/context pairs. Entry scratch reserves only required local storage,
excluding parameters/results and empty values; conditional destruction flags are
limited to paths whose live state can vary. Copy fixed arrays are acquired once
for iteration. No source-specific special case or language-rule relaxation is used.

Final Debug/Release solution builds pass with zero warnings/errors. Both full
suites pass 7,567 tests each with no failures/skips, including 40 added cases.
Tests cover Copy and Move instantiations, shared-body/policy identity, exact frame
capacity, reverse field destruction, unused definitions, malformed lowering plans,
reanalysis, snapshot/empty array loops, and related invalid source rejection.
The new `backend/windows-x64/test-milestone8.ps1` passes 46 checks per configuration:
unchanged/renamed inputs, both choices, alternate/empty arrays, explicit Abort,
and thirteen rejected inputs. Completed programs 1–7 also pass their respective
25/21/37/33/47/63/52 checks in both configurations: 648 integration checks total.
Each final report matches the current compiler and source SHA-256 hashes.

LLVM verification and native O0/O2 regressions pass for 13 GenericStorage,
23 Sequence, 466 Element, 18 Struct and 50 Reference fixtures: 570 fixtures and
1,140 executions. Final Debug/Release fixture regeneration matches the saved
native IR/expectation hashes; native execution and regeneration were serialized.
Evidence, logs, hashes and the final summary are in
`bin/milestone8-work/verification.json`. Original LLVM sandbox permission denial
was resolved by authorized execution; no required check remains unverified.
NativeAOT tests were not run. Work stops at program 8; the broader plan stages
below retain their separate incomplete obligations.

The shared backend currently admits owned transfer/placement/cleanup, boolean
selection and direct generic stored fields. Generic forwarding calls, arbitrary
symbolic compound fields, length-dependent storage, specialization, borrowed
generic ABI and general generic arithmetic remain guarded. Direct iteration is
limited to supported scalar Copy fixed arrays; general owned element iteration,
Slice iteration and user Iterable/Iterator dispatch remain separate work.

<a id="execution-11"></a>

## Recorded Program Milestone 7 integration (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

This request targets `milestones/Milestone7.kimi`, independently of this plan's
broader M1–M17 stages. The initial worktree at `6496482` was clean. All nine
checked-in programs were read (the request referred to five): 5 supplies borrowing
and destruction, 6 supplies value control flow and transfers, 7 adds sequence
metadata/iteration/views, 8 needs generic ownership, and 9 additionally needs
length generics, callbacks and generic enum results. Programs 8–9 are not being
implemented in this checkpoint.

The current-source Debug baseline built cleanly; 353 focused existing tests passed.
The unchanged program first failed final Binding at 9:11 (`for`), with additional
unimplemented `indices` and full-range Slice syntax. Ownership and generation
were then untested, rather than established failures. Evidence is retained under
`bin/milestone7-work/`, including `baseline.log` and `baseline-tests.log`.

Implemented the vertical subset in dependency order: array/Slice metadata and
independent ResolvedRange snapshots; immutable isize for bindings and finite
range iteration in the existing ownership CFG; full Slice creation, Copy handles
with backing Origins, and checked scalar indexed reads. Existing element
projections retain receiver evaluation, bounds checks and inline addresses;
existing transfer cleanup handles nested continue/exit/return/yield. Slice Loans
are tracked through copies and last uses, including index evaluation, and retain
static path footprints so writes to a different row remain legal. Physical range
and Slice handles use two words; neither copies backing element storage or
allocates it on the heap. Generation validates sequence plans against their
evaluated sources. No source-program rewrite, filename match, numeric-output
special case, or change to the language rules is used.

**Program 7 COMPLETE.** Debug/Release solution builds pass with zero warnings/errors;
both full suites pass 7,527 tests each, including 40 new sequence cases, with no
failures/skips. The Milestone 7 script passes 52 checks per configuration, including
unchanged and renamed O0/O2 inputs, alternate arithmetic and transfer paths,
matrix/Slice bounds Abort, and thirteen rejected inputs. Exact expected stdout,
empty stderr and exit 0 are verified for the original; Abort variants exit 1.
Reports: `bin/milestone7/Debug/dabe834f49cf46f1aea7b8b66c29c4c9/verification.json`
and `bin/milestone7/Release/602c33a1a4694ec0897c46e9377e418d/verification.json`.

Completed programs 1–6 pass 25/21/37/33/47/63 checks respectively in each
configuration (226 each). LLVM verification and O0/O2 native regression pass for
19 Sequence, 466 Element, 18 Struct and 50 Reference fixtures: 553 fixtures and
1,106 executions. All were freshly emitted by the final Debug suite. All 2,765
fixture/expectation files regenerated by the final Release suite match their
saved SHA-256 hashes, linking that native evidence to both builds without
unnecessary duplicate executions. Native generation/execution and managed fixture
regeneration were serialized. Logs, hashes and `verification.json` are retained
under `bin/milestone7-work/`. Initial LLVM permission denial was resolved by
authorized execution; no required check remains blocked or unverified. NativeAOT
and new throughput benchmarks were not run. Work stops at program 7.

I19/I20 and plan stage M8 remain incomplete: this integration implements the
built-in ResolvedRange path from `indices`, not general user-defined
Iterable/Iterator dispatch. General Kimi sequence declarations and explicit Type
APIs, Index/from-end/bounded Range operations, arbitrary Slice element results,
direct collection/Slice iteration, tuple iteration bindings and generic
collection/iteration ABI remain separate work. Mixed Slice provenance widens to
a conservative root footprint. The unchanged program requires none of these
later extensions. SPEC.md already defines the implemented behavior; this task
does not change its rules. Drafts and NativeAOT are excluded.

Concurrent workspace changes observed at final review added Milestones 10–14,
their README sections, and the SPEC index link. They were preserved and are not
part of this task's implementation or verification. The original nine programs,
including the target, remain unchanged. The shared milestone README retains both
the program-7 verification section and those independent additions.

<a id="old-execution-state"></a>

## Earlier execution-state table and checkpoints (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

## 2. Execution State

The current 60-minute execution and unit 33–35 evidence are recorded at the top
of this plan. The following timed checkpoints are historical; their deadlines and
then-current next actions do not override the current checkpoint.

- Historical timed execution closed **2026-09-17T15:04:22+09:00**; elapsed **20m17s**. Units 31–32 were DONE and M2/M3 remained IN_PROGRESS.

- Last updated: **2026-09-17T15:03:00+09:00** (Asia/Tokyo).
- Current execution: 20-minute soft limit starting **14:44:05 JST**, including inspection and verification; deadline **15:04:05 JST**. At the deadline, finish the current bounded unit or leave a coherent documented checkpoint; do not start another unit. Earlier execution limits are historical.
- Fresh checkpoint: HEAD `cf6fa41`, with the completed program-8 changes still uncommitted. Re-read the current instructions, plan and affected implementation. Preserve those changes; initial tracked/untracked files and patch are archived under `bin/plan-execution/20260917-144405/`. Program 8 is complete; resume the broad plan's latest explicit M2/M3 next action rather than starting program 9.
- **Unit 31 / I6/I8 T4n-r DONE:** unchanged mixed joins retain their constituent targets. Both clean builds and full suites PASS 7,583/configuration; LLVM/native regression PASS 380 Never + 414 Default executions, including 32 new executions. Milestone 1/8 PASS 25/46 checks per configuration. Current evidence: `bin/plan-execution/20260917-144405/`.
- **Unit 32 / I6/I8 T4n-s DONE:** started 14:59:35 JST (15m30s elapsed), verified at 15:03 JST. Bare checking-only transfers retain original targets and pre-cleanup seeds, including consecutive transfers. Both clean builds and full suites PASS 7,594/configuration; 32 new ordinary O0/O2 executions PASS. All prior Never/Default native fixture and expectation hashes match final regeneration. Assignments/calls/operand effects remain guarded; no unverified implementation work remains in units 31–32.
- Previous resumption (unit 30): HEAD `97bebe1` (user commit `.`); the working tree was clean and unit 29's implementation and evidence were available. Root AGENTS.md, plan, relevant specification, implementation and verification setup inspected; no nested AGENTS.md. Current evidence root: `bin/plan-execution/20260917-092418/`.
- Previous checkpoint: **M2/M3 IN_PROGRESS**. **T4n-q DONE** (execution unit 30): per-path transfer targets support completing loop/while continuations and filter internal transfers. Full Debug/Release suites PASS **7,487/configuration**, zero failures/skips, and new fixtures PASS **32 ordinary native O0/O2 executions/configuration**. Broader native regression PASS **348 Never + 414 Default executions**; Milestone 1 PASS **25 checks/configuration**. Unit 29's T4n-p evidence is historical and preserved. Changes are uncommitted.
- Current next action after this timed stop: reproduce `dotnet Kimi/bin/Debug/net10.0/Kimi.dll check bin/plan-execution/20260917-144405/next-mixed-effect.kimi` (UnsupportedOwnership_Kd at 13:13). Carry subsequent checking effects per target before an enclosing extent filters paths; do not propagate one merged state or omit a path. This requires a separate coherent unit and is not started in the remaining minute. Outer-mutating divergent loops, deferred-cleanup joins, unequal Loan joins, short-circuit conditions and broader M2/M3 remain incomplete.
- Earlier executions: the 02:42–03:42 run completed T4a–T4f; the 03:43–04:43 run completed T4g–T4m and added 91 tests. Their changes are part of the current committed baseline and are preserved by this run.
- Historical execution baseline evidence is retained under `bin/plan-execution/20260917-024236/`; the 04:43 execution verified `bin/plan-execution/20260917-034310/final-manifest.txt` at its start. Preserve all prior changes and the committed user draft.
- Previous execution: 01:42:22–02:42:20 JST, 59m58s; T1a, T5a/G9, T27a, T27b and T7a DONE. Final full suites passed 7,050 cases per configuration. Native Milestone 1 passed 25 checks per configuration at T27b. These records remain tied to their inputs; rerun affected checks after new changes.
- Previous execution milestones: **M2/M3 IN_PROGRESS**. That run verified **T4n-a–g DONE**, seven bounded units and 95 additional cases. Final full suites pass **7,302/configuration**, zero failures/skips; builds have zero warnings/errors. Final native replay passes **444 O0/O2 executions**, and Milestone 1 passes **25 checks/configuration**. No implementation unit remains unfinished. All changes remain uncommitted; detailed evidence and remaining limits are in execution unit 20 below.
- Historical next action (resolved by unit 21): **I6/I8 T4n-h**, reproduce `bin/plan-execution/20260917-044326/next-scope-continuation.kimi` and propagate a correct checking continuation out of a noncompleting default do body with local scalar effects. That baseline CLI rejected the initialized caller-local read at line 8:9; final checks now accept it. Do not substitute scope-entry state for terminal state. General effectful/branching joins, recursive-default completion proof, direct Never operand fitting, generic Copy proof, escaping Borrow/captures, mixed nested-array Binding and owned/default cleanup remain unfinished.
- T4a checkpoint (about 9 minutes elapsed): **IMPLEMENTED_UNVERIFIED**. `BoundDefaultArgument` and reusable `BoundCall.DefaultArguments` retain selected omitted expressions, parameter Symbols and substituted parameter Types. Winner slots are reconstructed from the committed receiver/argument mappings; defaults do not participate in inference or selection. Twelve new cases and 105 focused cases pass, including forward declarations, chained/named defaults, inherited Type and declared Origin substitution, declaration replacement, reload and zero-allocation warm Binding. Full suites are running. A first inherited fixture placed a container-parameter premise under a nongeneric method and reached an earlier syntax/Binding boundary; moving that premise to its container makes the intended inherited-substitution test reach the call plan.
- **T4a DONE at about 02:53 JST (11 minutes elapsed)**: both builds have zero warnings/errors and both full suites pass 7,062 cases, zero failures/skips. Runtime rejection remains unchanged. Next unit **I4/T4b IN_PROGRESS**: audit/check every default expression's control flow and ensure its transfers cannot target outside the default, including when all calls supply that argument. This prerequisite precedes any default-execution support; existing `ControlFlowAnalysis.VisitFunction` visits only the function body.
- T4b checkpoint (about 16 minutes elapsed): **IMPLEMENTED_UNVERIFIED**. Ten original-code reproducers failed: default flow was absent, value-producing selections/do were discarded, and jumps escaped into outer function/loop bodies. Defaults are now visited independently, recognized as value contexts and form a transfer boundary. Twelve new cases and 222 focused cases pass, including bodyless requirements, checking-only branches, internal exits, independent callee completion and zero-allocation warmed reanalysis. Both clean builds pass after a helper-order warning fix; final full suites are running.
- **T4b DONE at 03:01 JST (about 18 minutes elapsed)**: both clean builds and full suites pass, 7,074 cases per configuration. Next **T4c IN_PROGRESS**, a bounded I4/I8 default-execution slice whose call-plan and flow prerequisites are T4a/T4b: support scalar literal/preceding-parameter arithmetic defaults through prepared argument slots, ownership and lowering. Check every default declaration; keep generic, borrowed/owned, effectful and other unsupported default expressions rejected. Other M2/M3 obligations remain open. Required evidence: original-code emission failures, ordering/snapshot/repeated-call cases, meaningful malformed-plan checks, full Debug/Release suites and new O0/O2 native execution/Abort cases before DONE.
- Remaining: I3 final R1–R3 clause closure; I4 default/call/inference plans; I5 remaining Contract/projection/Core obligations. Effect-family verification remains I7/M3, default ownership/cleanup I8/M3, persistent semantic artifact reuse I29/M12. G9 is resolved; G1/G2 still block only their undefined public integration slices.
- Applicable rules: root AGENTS.md; no nested instruction file found. English documentation, practical allocation/performance optimization, no draft edits and no NativeAOT tests. The current 20-minute soft stopping rule takes precedence over continuing through all remaining work.
Use states `TODO`, `IN_PROGRESS`, `IMPLEMENTED_UNVERIFIED`, `DONE`, `BLOCKED`, `NOT_APPLICABLE`. DONE requires the milestone's acceptance evidence, not historical reports. Use verification results `PASS`, `FAIL`, `NOT_RUN`, `BLOCKED`.

| ID | State | Completion evidence or remaining work | Next action |
| --- | --- | --- | --- |
| M1 | DONE | 2026-09-17 at HEAD `8c49edb`: I1 baseline (V1–V3 PASS in both configurations, V4–V5 Struct-family PASS, G5 resolved) and I2 Appendix A clause coverage map recorded in Section 4; no discrepancy observed, all uncovered clause families assigned to existing T/I IDs | M2/I3 |
| M2 | IN_PROGRESS | Selected defaults, declaration checks and omitted-call completion verified through T4n; I3 final clause closure and broader I4/I5 obligations remain | Remaining inference/call closure and declaration ownership proofs |
| M3 | IN_PROGRESS | T4n-a–v verify bounded noncompletion/checking, target-aware joins, straight-line and closed CFG per-target effects, and stored-Loan liveness through checking seeds. General effects/Loans/refinement, effectful divergence, region-changing terminal branches, deferred cleanup, unequal Loan joins and owned/borrowed defaults remain incomplete | Investigate the recorded short-circuit reproducer |
| M4 | TODO | Concrete structs already have execution support; inherited layout/accessors/static storage need integration | I9–I11 |
| M5 | TODO | Concrete enum semantic plans exist; general enum/Pattern execution absent | I12 |
| M6 | IN_PROGRESS | Program 8 adds universal stored-field CopyOrMove checking and shared transfer/selection CFGs with concrete entries and fixed frames; broader universal bodies, explicit selection and generic operations remain incomplete | Remaining I13–I16 obligations |
| M7 | TODO | Capture syntax and Callable identities exist; Closure/function-value execution incomplete | I17–I18 |
| M8 | IN_PROGRESS | Program 7 sequence subset and program 8 scalar Copy fixed-array iteration implemented (see integration checkpoints above); general sequence Kimi APIs/witnesses and Iterable/Iterator dispatch remain unfinished | I19–I20 |
| M9 | TODO | Collection/Stringify/comparison contracts specified; executable Core incomplete | I21–I23 |
| M10 | TODO | Object/Weak APIs and profile specified; generation/runtime incomplete | I24–I25 |
| M11 | TODO | Import declarations/pointer syntax partially supported; native execution boundary needs completion | I26 |
| M12 | TODO | Project graph/restore/lock support exists; packages/common generation/semantic persistence incomplete | I27–I29 |
| M13 | TODO | Product exclusion exists; internal test verification/discovery can be developed | I30; external runner interface slice blocked by G2 |
| M14 | BLOCKED | Public Mod host interfaces excluded until separately specified | I32 semantic lifecycle audit can proceed; no invented host API |
| M15 | TODO | Full conformance, performance and documentation closure depend on prior milestones | I33–I34 |

Initial states were TODO except I31 (BLOCKED by G2) and I32's public interface slice (BLOCKED by G1). Current item states and completed slices are recorded in Section 7.

<a id="old-architecture"></a>

## Planning-time architecture and support audit (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

## 3. Current State and Architecture

### Evidence vocabulary

- **Verified fact:** observed command output or a specific inspected implementation branch. Code inspection establishes structure/guards, not successful current execution.
- **Evidence-based inference:** conclusion from inspected architecture and tests, requiring an implementation-time reproducer before changing behavior.
- **Proposed change:** future design in Sections 6–10; new files/symbols are labeled **new**.
- **Unverified assumption:** explicitly tracked in G entries; cannot certify a requirement.

### Actual processing path

| Stage / existing entry point | Input → output and responsibility | Verified fact / required work |
| --- | --- | --- |
| `Kimi/Unit/Program.cs`, `Kimi/Unit/CommandUnit.cs`, `Kimi/Unit/Command/CommandExecution.cs` | CLI → command options and Solution/Project operations | Commands registered: default, lsp, build, check, restore, emit, run. No pack/publish/store/test command is registered. Extend dispatch only for specified commands (M12–M13). |
| `Kimi/SolutionAndProject/Solution.cs`, `Project.cs` (`BuildCore`, `BuildTarget`), `DependencyResolver.cs`, `DependencyLock.cs` | Selected paths/configuration/locks → fixed source graph and target Compilation | Project dependency loading and lock partitions exist. Package branch explicitly throws `ResolutionFailure("UnsupportedPackage", ...)`. Preserve source-first identity and direct-reference visibility. |
| `Kimi/Compiler/Core/Compilation.cs` (`Prepare`), `Compilation.Modules.cs` (`PrepareModules`) | Target/settings/graph → environments, `SourceModules`, per-module aliases/reference maps | `Prepare` forbids changing inputs after parsing. Existing modules are real source modules, despite older STATUS §6 wording. |
| `SourceDocument`, `Kotonoha.AddSource` / `ParseSource`, `Tokenizer`, parser/Koto classes | Immutable source snapshot → source-local `CodeContext`, Koto tree, syntax diagnostics | Selected source merges into root containers; top-level executable items use a private generated-function wrapper. Reached directives are validated during parsing. No separate Bound Tree exists. |
| `Binding.Bind`, `Binding.CheckBound`, `BindingModel.cs` and `Binding.*.cs` | Existing Koto + environments → `BoundType`, Symbols, selected operations, constraints/Origins, obligations/certificates | Reuses tables/capacity and writes semantic fields on Koto. Final binding resets facts and invalidates ownership. Late certificate propagation exists; universal generic/effect completion remains work. Lowering must consume identities/plans, never redo lookup. |
| `ControlFlowAnalysis`, `StructuralCompletion`, `OwnershipAnalysis.Analyze`, `OwnershipBody`, `OwnershipModel.cs` | Bound operations → CFG, places, acquisition/result/cleanup plans, initialization and Loan checks | Concrete paths, source-checking continuations and runtime reachability are distinct. Captures/default parameters and accessors have explicit unsupported paths. Extend this representation, not a parallel ownership engine. |
| `LlvmEmitter.TryPrepare` / `CheckInputs`, `FunctionAbiPool`, `AggregateLayoutPool`, `BodyLowering.Lower` | Current checked semantic/CFG state → physical `EmissionModule` / functions / operands | A retained module is scratch, not a reusable certificate. `CheckInputs` requires final Binding, startup and verified ownership. Rejects external modules, non-Application output, non-struct nested containers, anonymous/specialized/generic signatures, captures/defaults and other unsupported bodies. Root structs and eligible borrowed struct methods are already admitted. |
| `EmissionModule.Complete`, `LlvmModuleWriter` partials, `WindowsLowering` | Validated typed instructions → LLVM text | Syntax-free physical instructions and pools exist. Validate every added instruction's inputs, layout, CFG and responsibility before writing; preserve failure leaving module unwritable. |
| `EmissionArtifacts`, `ArtifactPaths`, `NativeToolchain`, `ToolchainResolver`, `Kernel32Imports` | LLVM/link manifest → checked object/link inputs → staged executable/build record | `emit` is separate from LLVM invocation; `build` verifies/links; `run` executes existing artifacts. Hashes, tool/profile identity, cancellation and process cleanup already exist. Extend rather than replace publication machinery. |
| `WindowsRuntime.ll.in`, `backend/windows-x64/profile.json` | Native startup/runtime calls → output, cleanup, Abort/process exit | Windows x64 profile pins LLVM 22.1.8, backend ABI 2, and archive/dlltool hashes. String/output/allocation/Abort helpers exist; general object/Weak runtime needs work. |

### Support matrix at this checkout

Classifications apply to the stated input range. “Implemented” below means inspected code paths plus named test coverage exist, **not** a current test PASS. P = parsing, B = Binding, A = analysis, G = checked generation. LLVM verification/native execution for every row is **Unknown in this planning run**; historical evidence is separate below.

| Requirements / input range | Support by stage | Evidence / remaining limit |
| --- | --- | --- |
| R1–R2: source identity, directives, lexical forms, exact numeric fitting | P: Implemented for inspected rules; B: Implemented for prepared environments; end-to-end clause completeness: Unknown | `SourceDocument.cs`, `Kotonoha.cs`, `CompileTimeConditionEvaluator.cs`; `SourceEncodingTest`, `UnicodeIdentifierTest`, `DirectiveConditionValidationTest`, `NumberLiteralParseTest`. Audit remaining clauses, reuse tests. |
| R3–R5: names, Types, constraints, calls | P/B: Partially Implemented; A/G: Partially Implemented | Extensive `Binding.*` and certificate tests exist. `Binding.Expressions.cs` explicitly rejects anonymous/specialization functions; general callable Property use remains unsupported. Defaults bind but `OwnershipAnalysis.BuildBody` rejects their execution. |
| R6–R9: ownership, cleanup, control flow, primitives | A/G: Implemented for concrete scalar/string/tuple/fixed-array and supported struct paths; full requirement: Partially Implemented | `OwnershipAnalysis.*`, `BodyLowering.*`, `ScalarEmissionTest`, `ElementMoveEmissionTest`, `CurrentControlFlowTest`, `StructEmissionTest`, reference tests. General effects, captures, borrowed aggregates and refinement still need work. |
| R10: struct construction/destruction/borrowed receivers | P/B/A/G: Partially Implemented | `Binding.Structs.cs`, `StructStorage.cs`, `OwnershipAnalysis.Structs.cs`, `BodyLowering.Structs.cs`, `BodyLowering.StructBorrows.cs`. Do not redo program milestones 4–5. `StructStorage` enumerates stored members; inherited logical layout must be audited. |
| R11: custom/computed/static Properties | P/declaration B: Partially Implemented; general A/G: Not Implemented at inspected accessor boundary | `Binding.Properties*.cs`; `OwnershipAnalysis.Collector` reports `OwnershipFailure.Unsupported` for `PropertyAccessorKoto`. Static initialization/shutdown still lacks an integrated executable path. |
| R12: enums and full Patterns | P/B/A: Partially Implemented; enum G: Not Implemented in current layout/ABI path | `Binding.Enums.cs`, `Binding.Patterns*.cs`, `OwnershipAnalysis.Enums.cs`, `MatchCoverage.cs`; `FunctionAbi.GetValue` / `AggregateLayoutPool.Get` admit tuples/arrays/structs, not general enum storage. Scalar/string match G exists. |
| R13–R14: generic universal proofs/selection/generation | P/schema B: Partially Implemented; general A/G: Not Implemented through emitter guards | `Binding.Constraints*.cs`, `Binding.OriginRequirements.cs`; `LlvmEmitter.CheckInputs` rejects generic arguments, Origins, constraints and specializations. Declaration proof tests do not prove universal body execution. |
| R15: Closures/common function values | P: Implemented syntax; B/A: Partially Implemented identities; G: Not Implemented through emitter guard | Capture Koto/function Types, `CoreIntrinsics.Callable`; captures fail ownership, anonymous functions fail final Binding. |
| R16–R18: Index/Range/Slice/for/dynamic collections | P: Partially Implemented; general B/A/G: Not Implemented for missing Core entries | `CoreIntrinsics` missing entries; for/range syntax exists. Fixed-array isize indexing and literal static Move paths are already implemented and reusable. Do not equate absent Core entries with absent fixed-array support. |
| R19: Stringify/interpolation/Contract comparisons | P and primitive comparisons: Partially Implemented; missing Core Contract operations: Not Implemented | Kimi catalog and `Binding.Expressions.cs`; literal strings and primitive comparisons already execute. No inferred permission for deferred concatenation. |
| R20: objects/Weak/runtime type refinement | B: Partially Implemented runtime tests; A/G/runtime: Partially Implemented foundations only | `Binding.RuntimeTypeTests.cs`, `RuntimeTypeTest`; Kimi catalog has no Weak declaration or object-operation symbols. `CompilerFunctionKind` currently contains WriteLine and Abort only. |
| R21: raw pointers/FFI/C layout | P/declaration B: Partially Implemented; complete native path: Unknown/blocked by current emitter restrictions | `Binding.Attributes.cs`, `LibraryImportTargetBindingTest`, `LayoutAttributeBindingTest`; imported attributed functions cannot simply pass current function generation checks. Inspect operation-by-operation before implementing. |
| R22–R24: modules/packages/reuse/native inputs | Project B/A and restore: Partially Implemented; Package loading/commands: Not Implemented; cross-module G: Not Implemented | `Compilation.Modules.cs`, resolver guard and command registry; `ModuleBindingTest`, `DependencyLockTest`, `EmissionArtifactsTest`. Existing serialization is source reload, not portable semantic records. |
| R25: language tests | Product exclusion P/B/A/G: Partially Implemented; test-mode discovery/runner: Not Implemented through registered CLI | `TestDefinition`, `ProductTestMembershipTest`, `TestAttributeCertificateBindingTest`; `OwnershipAnalysis.Collector` skips marked tests. |
| R26: Mods | Provisional/final Binding and appended-source infrastructure: Partially Implemented; external execution: Not Implemented | `Compilation.Bind` explicitly says no Mod execution exists; public interfaces separately undefined. |
| R27–R28: diagnostics/performance/conformance | Partially Implemented infrastructure; current comprehensive verification: Unknown | Diagnostic collections/spans, reusable pools, benchmark workloads, native scripts. No measured current-run speedup. |

Kimi catalog discrepancy: code currently has 18 entries and constructs six validated declarations; the table in §22.1 now also requires Weak and explicitly named object factories/operations. `CoreIntrinsics.cs` still comments that object operation spellings are deferred. This is an **IMPLEMENTATION_MISMATCH**, not permission to omit R20 or invent a new catalog contract.

Reload/invalidation: `Kotonoha.OnDeserialized` reparses saved `SourceDocument` objects with new contexts; it does not restore authoritative semantic proofs. `Binding.Bind` resets semantic fields, invalidates ownership, recomputes conformance/constraint certificates and prunes reused state. `LlvmEmitter` clears its declaration-to-ABI map in `finally`. Preserve these boundaries when adding facts; serialize verified semantic records only under R23, never mutable Koto graphs. Test added/removed declarations, changed aliases/constraints/effects and source replacement, not just serialization round trips.

Historical evidence only: STATUS's 2026-09-17 entries report 6,986 managed tests per Debug/Release and native programs 1–6. Program 6 reportedly outputs `Selected 7.\nControl flow passed.\n`. Reports/old binaries were not certified against this checkout. Earlier STATUS §3/§4/§6 descriptions lag newer struct/borrow/module implementation; `.github/workflows/test.yml` also now explicitly selects Release, a test project and a nonzero-test minimum. Reconcile these descriptions later under I34; do not copy their old omissions into the implementation backlog.

<a id="appendix-a-baseline"></a>

## I2 Appendix A baseline mapping (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

### Appendix A clause coverage map (I2, 2026-09-17)

Method: every Appendix A section was read against the 190 executed test classes of run `m1-8c49edb` (per-class counts from `full-debug.xml`) and a keyword scan of `xUnitTest/Tests`. "Baseline" means executed, passing classes exercise the section's inspected clauses at the named stages; "Partial" means only some clauses or only earlier stages (P/B) are exercised; "Absent" means no executed test reaches the clause family. A PASS on a listed class is evidence for the clauses that class actually asserts, not for the whole section; the remaining clauses are the coverage obligation of the assigned T IDs. No new stable ID was needed: every uncovered family already maps to a planned T/I item, and no observed discrepancy (failing or contradictory test) exists at this HEAD.

| Appendix A section | Existing executed classes (count) | Coverage | Uncovered or later-stage clauses → assigned IDs |
| --- | --- | --- | --- |
| A.1 Source identity/incremental analysis | SourceEncodingTest (18), SourceDocumentAndDiagnosticTest (13), KotonohaSerializationTest (4), ContainerFragmentBindingTest (29), ModuleBindingTest (206), StartupBindingTest (73) | Baseline for snapshot/context retention and root wrapper; Partial for reuse of verified semantic records | §18.7 correspondence/dependency reuse → T27/I29; no Mod output integration tests → T30/I32 |
| A.2 Directive validation/selection | DirectiveConditionValidationTest (58), CompileTimeSwitchParseTest (41), CompilationSpecificationTest (74), DeferredEmissionTest (47), DependencyConfigurationTest (38) | Baseline | Eager/deferred/cached parsing agreement on mandatory categories only through reload tests → T2/T27 |
| A.3 Binding, caches, incremental validity | BindingTest (55), ApiAccessBindingTest (35), InheritedNameBindingTest (19), DefaultBindingTest (14), FunctionParameterIdentityBindingTest (22), 60+ `*BindingTest` classes for Types/constraints/projections/access, ModuleBindingTest (206), IdentifierIdentityTest (21), ReceiverShorthandTest (23) | Baseline for lookup/access/candidate rules and certificates; Partial for effects and stale-artifact invalidation | Effect-family fixed points and completed public `ObjectCompatible` status (existing InheritedReceiverBindingTest verifies only internal Unknown rejection) → T7/I7, T23; default Loans and partial-call cleanup at execution → T4/I4; §21.3.4 stale-artifact rejection → T27/I29 |
| A.4 Property requirements | PropertyBindingTest (69), PropertyCompletionBindingTest (11), PropertyApiAccessBindingTest (28), PropertyProjectionApiAccessBindingTest (37), AccessorReceiverBindingTest (30), InferredPropertyAccessBindingTest (13), PropertyParseTest (8), PropertyRevisionParseTest (102) | Partial: declaration/B stage only; `OwnershipAnalysis.Collector` still reports Unsupported for accessors | Accessor ownership/lowering, custom getter/setter execution, static accessor effects → T10/I10; first-access static initialization/shutdown (0 hits) → T11/I11 |
| A.5 Raw pointer backend | LayoutAttributeBindingTest (42), LibraryImportTargetBindingTest (8); `unsafe` appears in 34 classes as syntax/Binding context | Partial: P/B only | §21.5.3 instruction mapping, provenance and null/one-past arithmetic → T24/I26 |
| A.6 Literal representation | NumberLiteralParseTest (70), NumberLiteralScanTest (130), CharLiteralParseTest (76), StringLiteralHelperTest (29), KotoHelperStringLiteralTest (37), StringLiteralIntegrationTest (7), KotonohaSerializationTest (4), FloatEmissionTest (63) | Baseline | Interpolation (parse-only hits) → T21/I23 |
| A.7 Cleanup analysis/lowering | DeferredEmissionTest (47), AbortEmissionTest (38), StructEmissionTest (39), BorrowStructEmissionTest (31), ElementMoveEmissionTest (69), ElementParameterMoveEmissionTest (54), AggregateResultEmissionTest (42), UnreachableOwnershipTest (63), OwnershipAnalysisTest (64) | Baseline for concrete scalar/string/tuple/array/root-struct paths | Base/derived per-layer completion and destruction order, base slicing rejection, final-reference object cleanup → T8/I9, T22/I25 |
| A.8 Callable/object verification | FunctionTypeConstraintBindingTest (23), RuntimeTypeTest (56), RuntimeTestFormationBindingTest (15), InheritedReceiverBindingTest (57), OwnershipAnalysisTest/FunctionEmissionTest capture rejections | Partial: identities and rejection paths only; no Closure/function-value execution (0 `closure` hits, `anonymous` only in parse tests) | Captures, three receiver kinds, erasure, per-call Origins → T16/I17–I18; object creation/strong count/Weak (no Core Weak entry, G4) → T22/I24–I25; multi-level generic base projection → T23/I13 |
| A.9 Refinement/require | ControlFlowConformanceTest (101), CurrentControlFlowTest (83), GuardEmissionTest (57), StringGuardEmissionTest (36), RefinementNameBindingTest (19), DeepRefinementPremiseBindingTest (9), UnresolvedRefinementBindingTest (18), PendingRefinementDeclarationBindingTest (13), AssociatedRefinementCapabilityBindingTest (14) | Baseline for statement-form require and static refinement | Runtime `is` refinement with objects and inherited receivers → T23/I24 |
| A.10 Enum/Pattern | EnumBindingTest (74), EnumOwnershipTest (60), PatternBindingTest (89), PatternTypeFormationBindingTest (15), PatternCoverageCompletionTest (4), PatternWarningCompletionTest (7), MatchEmissionTest (73), MatchOwnershipTest (59), EnumGenericConstraintBindingTest (11), EnumContextBindingTest (10) | Baseline for B/A coverage/warnings and scalar/string match G | Enum tag/payload layout and owned/shared payload lowering (`AggregateLayoutPool.Get` admits no general enum) → T12/I12 |
| A.11 Static Contract | ContractBindingTest (73), ConditionalConformanceBindingTest (48), ConditionalMember*/ConditionalPremise*/ConditionalProjection* (127), Associated* (108), Closed* (93), NormalizedConstraintProofBindingTest (24), ProvisionalContractPremiseBindingTest (13), StructuredConstraintSubjectBindingTest (34) | Baseline | Artifact/cache invalidation of proof dependencies → T27/I29; executable witness use (Stringify/Equatable/Comparable have 0 execution hits) → T21/I23 |
| A.12 Generic schemas/specialization | GenericCallFormationBindingTest (16), GenericConstraintCertificateBindingTest (13), GenericTypeArgumentBindingTest (25), TypeArityBindingTest (28), PairAnnotationBindingTest (24), PairGroupingBindingTest (23), ConstantLengthBindingTest (48), ArrayInferenceEmissionTest (30), ArrayArgumentEmissionTest (31) | Partial: schema/B and fixed-array slots; `specialize` appears only in parse/startup tests; `ValidLength` has 0 hits | Universal body checking, public ValidLength obligations, specialization closure/selection, symbolic plans → T13–T15/I13–I16 |
| A.13 Sequence access | ElementEmissionTest (57), ElementPathEmissionTest (50), ElementAssignmentEmissionTest (78), ElementUpdateEmissionTest (114), ElementBorrowEmissionTest (76), ElementBorrowOwnerEmissionTest (60), ElementReplacementEmissionTest (53), RangeIndexParseTest (5), ForParseTest (4), CollectionLiteralParseTest (4) | Baseline for fixed-array isize element paths; Absent beyond parsing for Index/Range/Slice/iteration/dynamic collections (`Slice`, `Iterable`, `Dictionary` hits are parse/Type tests only) | Index/Range/ResolvedRange/Slice → T17/I19; iteration protocol and program 7 → T18/I20; Array/Dictionary mutation, capacity, cost → T19–T20/I21–I22 |
| A.14 Layout/LLVM/runtime | MinimalEmissionTest (23), ScalarEmissionTest (31), IntegerEmissionTest (57), WideIntegerEmissionTest (86), ConversionEmissionTest (192), NumericConversionEmissionTest (65), FloatConversionEmissionTest (31), DivisionEmissionTest (52), BitwiseEmissionTest (65), CharEmissionTest (75), StringEmissionTest (43), StringComparisonEmissionTest (34), StringFunctionEmissionTest (40), StringResultEmissionTest (45), ResultEmissionTest (34), AggregateEmissionTest (34), AggregateFunctionEmissionTest (44), IdentityAcquisitionEmissionTest (34), ReferenceEmissionTest (67), EmissionArtifactsTest (23), EmissionPlanTest (20), NativeToolchainTest (15), Kernel32ImportsTest (8), ToolchainResolverTest (7), LayoutCertificateBindingTest (10), StartupBindingTest (73) plus `backend/windows-x64/test-*.ps1` | Baseline for startup, scalar/string/aggregate ABI, instructions, constants/FP, runtime failures, backend supply and profile | Callable storage, metadata/sharing, generic entry/context/frames/lengths/budgets → T14–T16/I15–I18; Weak/objects → T22/I24–I25; FFI execution → T24/I26; enum match execution → T12/I12 |
| A.15 Control flow | ControlFlowAnalysisTest (76), ControlFlowConformanceTest (101), ControlFlowRevisionParseTest (20), ControlTransferParseTest (4), CurrentControlFlowTest (83), BlockSyntaxParseTest (70), FunctionBodyParseTest (9), GuardEmissionTest (57), SemicolonParseTest (29), FrontEndSyntaxTest (110) | Baseline; program 6 native evidence is historical (V11 NOT_RUN this run) | Anonymous-function expectations at execution → T16/I17; formatter/refactoring tool rows are out of scope (Section 12) |
| A.16 Dependencies/artifacts/reuse | DependencyLockTest (20), DependencyResolutionTest (22), DependencyConfigurationTest (38), SolutionInputTest (21), ModuleBindingTest (206), EmissionArtifactsTest (23), CommandOptionsTest (7), AllocationMeasurementTest (2) | Partial: identity/locks/native artifacts; Absent for packages, storage, semantic reuse and measurement rows | Pack/publication/storage → T28/I27; semantic reuse/cold-warm equivalence → T27/I29; common generation and native supply validation → T28–T29/I28; measurements → V9/I16, I33 |
| A.17 Test verification/runner | ProductTestMembershipTest (35), TestAttributeCertificateBindingTest (10) | Partial: membership/exclusion only; `$require`/`$expect` have 0 hits | Verification operations, cleanup, identity/protocol → T25/I30; bounds/recovery, CLI/artifacts → T26/I31 (BLOCKED by G2) |

Genuine gaps confirmed by this audit (all already carried by the checklist, so no requirement or item changes): executable Properties/statics (A.4), pointer/FFI execution (A.5, A.14), Closures/function values (A.8, A.14, A.15), object/Weak runtime (A.8, A.14; G4), enum layout/lowering (A.10, A.14), universal generic bodies/specialization/sharing (A.12, A.14), Index/Range/Slice/iteration/dynamic collections (A.13), Stringify/interpolation/comparison witnesses (A.6, A.11), packages/semantic reuse/measurements (A.16), and `$require`/`$expect`/runner (A.17). Everything else in A.1–A.3, A.6–A.7, A.9–A.11 and A.15 has an executed passing baseline at this HEAD that must be preserved by later milestones (regression gates, not reimplementation tasks).

<a id="old-decisions"></a>

## Original G1–G9 and D1–D5 decisions (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

## 5. Gaps and Decisions

| ID / classification | Evidence and affected requirements/milestones | Resolution / effect |
| --- | --- | --- |
| G1 `SPEC_GAP` | §20.7.7 and D list concrete Mod query/marker APIs, packaging/compatibility, settings and host limits as design work. R26/M14 | Separate public API/host design must define these before external implementation. Retain existing append/rebind infrastructure and verify settled lifecycle rules internally. Do not treat illustrative `IMod`/`ModContext` as adopted interfaces. |
| G2 `SPEC_GAP` | D.4 explicitly defers temp-directory API, deadlines/recovery defaults/options, budgets, transport/report schemas, empty-set option, exits and ID encodings. R25/M13 | Separate consolidated profile decision required before I31: settle all listed interfaces and bounds together. I30 semantic membership/discovery/verification work is independent. Do not publish a supposedly complete runner using invented public contracts. |
| G3 `INVESTIGATION_GAP` | D's collection storage entry versus settled §4.6.8/§4.7 complexity/storage freedom. R16–R18/M8–M9 | Public storage ABI remains excluded. Proposed internal Slice location/length and Array contiguous storage are implementation choices, subject to layout/Loan/zero-size validation. Proposed Dictionary indexed slots/free-list/order links allow allocation-free churn; benchmark before optimizing search. No user approval is needed for ordinary private layout choices within these contracts. |
| G4 `IMPLEMENTATION_MISMATCH` | Kimi catalog omits required Weak and leaves object operations missing with a stale deferral comment, despite §22.1 and §13.5.8–9. R19–R20/M2, M10 | Add canonical declarations/identities with their implementation milestones; update catalog tests intentionally. Do not make `IsCompleteLibrary` true just by changing a count or weakening required shapes. |
| G5 `INVESTIGATION_GAP` — **RESOLVED 2026-09-17** | `dotnet test --help` printed MTP help then attempted extension discovery/build, reporting `Access to the path 'C:\Users\bwff1\AppData\Roaming\NuGet\NuGet.Config' is denied.` SDK wrapper exited 0 despite the nested failure. All build/test evidence | Resolution: on the implementation run, `dotnet restore Kimigayo.slnx` completed ("all projects up to date", exit 0) and both configurations built and tested without any NuGet configuration error; the earlier denial was a sandboxed-probe environment condition, not a repository or user-configuration defect. No NuGet configuration was edited. Keep using the direct xUnit runner DLL rather than `dotnet test --help` style probes. |
| G6 `INVESTIGATION_GAP` | Historical success is not current evidence; only selected hot paths/guards were inspected. R1–R28/M1, M15 | Reproduce focused baselines, expand clause coverage using Appendix A before each milestone. Unknown support needs a targeted source reproducer/code trace, not “not implemented” from a failed search. |
| G7 `INVESTIGATION_GAP` | `AggregateLayoutPool` uses int offsets/counts, depth 64 and rejects size > int.MaxValue; §21 defines broader checked layout/resource rules. R10/R14/R16/M4, M6 | Determine which are documented resource limits versus unnecessarily restrictive representations. Use checked wide arithmetic internally where needed; keep finite limits explicit and distinguish semantic, resource and representation diagnostics. Do not allocate giant fixtures to test overflow. |
| G8 `IMPLEMENTATION_MISMATCH` | STATUS historical summaries omit newer struct/borrow/module support and current CI test selection. R27/M15 | Reconcile support descriptions against current evidence in I34; no STATUS edit during planning. |
| G9 `IMPLEMENTATION_MISMATCH` — **RESOLVED 2026-09-17 (T5a)** | `struct Reading` with `Self is Copy`, `Self is Comparable` (unresolvable) and `public let value: i32` reports five diagnostics: the legitimate `UnresolvedBinding_Kd` at `Comparable` and `InvalidConstraint_Kd` at that clause, plus `InvalidConstraint_Kd` on the struct header (`ValidateConstraintEnvironments`), on the valid `Self is Copy` clause (`ValidateCopyDeclarations`) and on the field (`ValidateProperties`), because the invalid struct makes `Self` an invalid Constraint subject. `Self is Copy` alone passes and executes. R5/R27, A.3 ("preserve ordinary errors separately"), A.11 (identify the failed requirement and cause) | T5a DONE: retain all internal failures, invalid certificates and proof Error; publish a missing-name cause once and suppress only established derived diagnostics. Multiple independent facts conservatively retain the header. Unit 3 records clause-order, invalid-use, fragment, rebind/reload, full-suite, CLI and allocation evidence. No language rule changed. |

No unresolved **SPEC_CONFLICT** was established by this investigation. Contradictory status text or stale code comments are not specification conflicts. If two applicable normative rules remain contradictory after owning-section/supersession review, add a SPEC_CONFLICT row with both clauses and block only dependent work.

Decisions/proposals:

- D1: Extend Koto semantic fields and existing reusable Binding/ownership/physical plans. No new Bound Tree or mandatory intermediate compiler stage.
- D2: First complete correctness and a valid baseline sharing plan; optional specialization/tuning cannot change selected implementations, caller ABI, checks or acceptance.
- D3: Start persistent reuse with conservative module invalidation; refine only with complete dependency evidence and measured benefit. Source reparse/rebind is the correctness oracle.
- D4: Internal resource/budget defaults and float Stringify spellings are documented implementation choices where explicitly permitted. Record chosen values and measurements at M6/M9; they are not missing language rules.
- D5: Programs `milestones/Milestone7.kimi`–`Milestone9.kimi` are integration targets, not the definition of compiler completion. They depend respectively on M8; M6+M8; and M5–M8 plus general borrowed storage. Preserve their source and expected behavior.

No user decision is needed to create this plan. Before implementing excluded interfaces, request G1 and G2 as consolidated, concrete design decisions in their separate effort; silence never resolves them.

<a id="old-checklist"></a>

## Earlier implementation checklist (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

## 7. Implementation Checklist

Boxes track the entire I item. Verified sub-units do not close a parent while its required scope remains unfinished.

| ID | Milestone / requirements | Implementation item and required outcome |
| --- | --- | --- |
| I1 | M1 / R1–R28 | [x] DONE 2026-09-17: baseline hashes, focused/full results and G5 resolution recorded under "Verification records (M1 baseline)". |
| I2 | M1 / R1–R28 | [x] DONE 2026-09-17: Appendix A.1–A.17 mapped to executed classes/counts in Section 4; genuine gaps confirmed and assigned; passing baseline slices recorded as regression gates. |
| I3 | M2 / R1–R3, R27 | [ ] IN_PROGRESS 2026-09-17. Audit: all 250 ```kimi blocks under `spec/` parse except intentional errors, standalone Type fragments, bodyless signatures and root-level Property accessor fragments (Chapter 11 confines Properties to struct/group/rootgroup); no lexical/type-syntax defect found; the only parser gap is `$expect`/`$require` (R25, recorded under I30). `check` on programs 1–6 passes; 7–9 fail only at planned later-milestone boundaries (see V11 record). SpecTour `check` gives 544 sites, 381 `UnsupportedBinding_Kd`; non-Unsupported codes reviewed, one M2 defect found. **T3a DONE:** merged containers now retain the first declaring fragment's CodeContext (`DeclarationContainerKoto.GetOrAddChild` / `GetOrAddDeclarationContainer` accept the parsing context; `TryParseDeclarationContainer` and rootgroup parsing pass `reader.CodeContext`), so container-level Binding failures report `file:line:col` instead of `Project:@0`. **T1a DONE 2026-09-17:** unavailable modifiers now report `UnavailableFeature_Kd` and recover across all planned declaration/accessor forms without reserving ordinary Names; 40 new cases, 275 focused cases and full Debug/Release suites (7,027 each) pass. Original parser fails all 28 negative cases; V9 comparison and CLI rejection recorded in unit 2. T27a/T27b DONE: append/reload invalidation and persistent source-error state verified. T7a DONE: effect-diagnostic correction and retained rejection/no-reselection verified; effect-family implementation remains I7 and persistent semantic artifact reuse remains I29. G9 resolved under I5. I3 remains IN_PROGRESS for final R1–R3 clause-coverage closure; the concrete effect and in-memory stale-analysis reproducers are now recorded and resolved or assigned to their owning later milestones. |
| I4 | M2 / R4 | [ ] IN_PROGRESS. Selected omitted-default plans, independent declaration checking, scalar execution and selected default completion are verified through T4n-a–g. General inference/call closure, recursive-default completion, effectful/borrowed/owned defaults and pending-slot cleanup remain unfinished. Evidence and exact resumption are in Section 2 and unit 20. |
| I5 | M2 / R5, R19 | [ ] IN_PROGRESS. T5a/G9 DONE: isolated unresolved conformance causes no longer publish derived container/Copy/Property cascades; other facts/errors remain visible. Complete the remaining retained Contract/projection/certificate obligations and canonical Core identities. |
| I6 | M3 / R6–R7 | [ ] IN_PROGRESS. T4n-a–s establish noncompletion checking and target-aware terminal joins. T4n-t–u preserve per-target straight-line/acyclic effects and stored-Loan liveness; T4n-v adds closed cyclic replay, with final program verification tracked above. Region-changing terminal branches, short-circuit scope proofs, general places/Loans/Origins, unequal Loan joins and retained result/storage dependencies remain. |
| I7 | M3 / R7, R20 | [ ] Compute effect-family fixed points and public guarantees; complete dependent usage checks. |
| I8 | M3 / R8–R9, R27 | [ ] IN_PROGRESS. T4c–T4m implement bounded scalar/Unit preparation and subplace inspection; T4g/T4h diagnose definite prepared Moves. T4n-a–g independently check scalar default initialization and preserve no-arrival behavior through calls/operators/state-neutral wrappers. General escaping-Borrow/capture/Copy proofs, owned default cleanup, effectful-divergence/deferred-cleanup continuation joins and refinement remain incomplete; partial scoped terminal joins and target-aware completing-loop continuations are verified by T4n-p–q. |
| I9 | M4 / R10 | [ ] Extend existing struct/base layout/construction/destruction and inherited receiver support. |
| I10 | M4 / R11 | [ ] Connect standard/custom/computed/Contract Property operations through ownership and generation. |
| I11 | M4 / R11, R24 | [ ] Implement first-access static states, effects, cycle checks and ordered shutdown. |
| I12 | M5 / R12 | [ ] Complete enum/tag/payload ABI and owned/shared Pattern lowering. |
| I13 | M6 / R5–R7, R13 | [ ] Program 8 verifies conditional stored-field acquisition and cleanup in the existing ownership CFG; broader universal operations/effects remain. |
| I14 | M6 / R13 | [ ] Close and validate explicit specialization sets; retain selected implementation at every call/reference. |
| I15 | M6 / R14 | [ ] Program 8 supplies shared storage/selection bodies, deduplicated policies, concrete entry adapters and exact fixed scratch frames; general calls/compound fields/lengths remain. |
| I16 | M6 / R14, R28 | [ ] Add deterministic finite resource limits and bounded optional optimization; measure effects. |
| I17 | M7 / R15 | [ ] Implement captures/concrete environments and receiver-specific callable analysis/lowering. |
| I18 | M7 / R7, R14–R15 | [ ] Implement function items, common Function values, erasure and indirect-call adapters. |
| I19 | M8 / R16, R19 | [ ] Program 7 subset: inferred full Slice handles, scalar reads and sequence metadata implemented. General Index/Range/Slice APIs and prerequisite Core witnesses remain open. |
| I20 | M8 / R17 | [ ] Program 7 subset: built-in ResolvedRange iteration and transfer cleanup implemented. General recognized protocol dispatch and other iterable adapters remain open. |
| I21 | M9 / R18 | [ ] Implement Array capacity/mutation/owning iteration with mandatory cost bounds. |
| I22 | M9 / R18–R19 | [ ] Implement Dictionary literal/mutation/equality/order/borrow contracts and allocation-free churn. |
| I23 | M9 / R19 | [ ] Complete comparison/Stringify witnesses and interpolation, documenting float formatting. |
| I24 | M10 / R20 | [ ] Implement object metadata/views/creation/strong ownership and refinement. |
| I25 | M10 / R20 | [ ] Implement Weak migration/upgrade/release/cyclic construction and atomic ordering proof. |
| I26 | M11 / R21, R24 | [ ] Complete specified pointers/C layout/direct foreign calls and ABI validation. |
| I27 | M12 / R22 | [ ] Implement package integrity/loading/packing/local publication/store lifecycle using fixed bytes. |
| I28 | M12 / R22, R24 | [ ] Complete common module/Library generation and native input requirement/supply validation. |
| I29 | M12 / R23, R28 | [ ] Implement verified semantic records/correspondence/invalidation with cold/warm equivalence. |
| I30 | M13 / R25 | [ ] Complete test-mode membership, all-body discovery/checking, verification semantics and plan isolation. Evidence 2026-09-17: `$expect(...)`/`$require(...)` statements do not parse (`Token $abort(Expression) is not expected at this position`, plus `Unmatched token require`), so the §17.5 operations start at the Parser stage. |
| I31 | M13 / R25 | [ ] BLOCKED: after G2, implement bounded isolated runner and adopted public reporting interfaces. |
| I32 | M14 / R26 | [ ] Audit/test settled Mod lifecycle; public host integration BLOCKED by G1. |
| I33 | M15 / R1–R28 | [ ] Close Appendix A coverage and cross-feature performance/conformance evidence. |
| I34 | M15 / R27–R28 | [ ] Maintain English support/spec-choice/usage records and examples; retain honest residual boundaries. |

<a id="units-1-32"></a>

## Baseline and execution units 1–32 (2026-09-17; times as recorded)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

### Verification records (current planning phase)

| Verification ID | Target/configuration | Relevant code/artifact state | Result | Evidence | Reverification needed |
| --- | --- | --- | --- | --- | --- |
| V1 | Repository identity/rules | HEAD above; initial working tree clean | PASS | `git rev-parse HEAD`, `git status --porcelain=v1`, root AGENTS and targeted reads | At resumption and before implementation |
| V1 | SDK inventory | Local SDK only, no compiler execution | PASS | `dotnet --version` = `10.0.401`; SDK list also contains `10.0.100-rc.1.25451.107` | If environment changes |
| V1 | MTP CLI help/extension discovery | `dotnet test --help`; not a test run | BLOCKED | Help printed, then NuGet.Config access denied; output says nested build failed with code 1 although wrapper exit was 0 | Resolve G5; rerun build/runner verification, not repeated broad help |
| V1 | Existing Debug test DLL runner help | Existing artifact, not tied to current source by this plan | PASS | Banner `xUnit.net v3 In-Process Runner v4.0.1+8ed8aa354c (64-bit .NET 10.0.12)`; usage/switches printed; command exit 1 | Fresh V2 build; help validates syntax only |
| V1 | LLVM/backend inventory | `toolchain` contains required executable names and `windows_x64` directory | NOT_RUN | Versions/hashes and actual native launch not checked | Before V4–V7 |
| V2 | Debug/Release solution | Current source | NOT_RUN | No deliberate restore/build run; help-triggered discovery failed before a completed build | M1 |
| V3 | Managed conformance/fixture generation | Current source | NOT_RUN | No tests executed; historical 6,986 figure is not current evidence | M1 then milestone-focused |
| V4–V8 | LLVM/native/CLI/ABI/reuse | Current source/artifacts | NOT_RUN | Script/control-flow inspection only | After fresh matching builds/fixtures |
| V9 | Allocation/time/memory | Current source | NOT_RUN | Existing tests/benchmarks inspected, no new measurement | Baseline and changed hot paths |
| V10 | Language runner | Unimplemented public interface | BLOCKED | G2; internal semantic tests also not yet added | I30, then G2/I31 |
| V11 | Programs 1–9 integration | Historical 1–6 reports; source 7–9 inspected | NOT_RUN | No program compiled or executed in this planning phase | At relevant milestone |
| V12 | Final plan-only review | New PLAN.md; source unchanged | PASS | Twelve numbered sections; contiguous R1–R28/M1–M15/I1–I34/T1–T30/V1–V12 references checked; existing test-class references checked against repository inventory; new paths marked; `git diff --check` clean and `git status --short` shows only `?? PLAN.md`. Untracked plan whitespace checked separately. | After any further plan edit; no compiler support certified |

For future records append run ID, exact command/working directory, HEAD and diff fingerprint, source/compiler/test/tool/backend hashes, configuration, fixture inventory, result counts, logs/report paths and any timeout/cleanup outcome. Keep failures and superseded evidence. PASS survives only while its relevant inputs remain unchanged.

### Verification records (M1 baseline, 2026-09-17)

Run ID `m1-8c49edb`; working directory `C:\Users\bwff1\repos\archi-Doc\Kimigayo`; HEAD `8c49edb09893154d74aefd7ec5849880e5583151`, working tree clean except `PLAN.md`. Logs, xUnit XML results and fixture inventories are under `bin/plan-baseline/m1-8c49edb/` (git-ignored). Tool identity: SDK 10.0.401; `toolchain/opt.exe` and `clang.exe` report LLVM 22.1.8 (`x86_64-pc-windows-msvc`). Built compiler/test identities (SHA-256): Debug `Kimi.dll` `3e045902c8086d2b554f622445f598f27cfb96efbc7d75baeaa9b7b5219bc54c`, Debug `xUnitTest.dll` `496b5065e9ec84fd2665d285046276455d2ec9620a472c51af9373829a871bdf`, Release `Kimi.dll` `4f4cc2f9b59224685a745a1a9d580787821f6d268911599a870c8a0cafcfcb50`, Release `xUnitTest.dll` `fb8580d4d57b74ae0a90bf9ca185998c33817381ac3ade34aae3046ea8bf9958`. No NativeAOT, publish, draft edit or source change occurred.

| Verification ID | Target/configuration | Command (working directory above) | Result | Evidence | Reverification needed |
| --- | --- | --- | --- | --- | --- |
| V1 | Repository identity, SDK, stray processes | `git rev-parse HEAD`; `git status --short`; `dotnet --version`; `dotnet --list-sdks`; `tasklist` filtered for `xUnitTest`/`dotnet`/`Kimi` | PASS | HEAD above; clean tree; SDK `10.0.401` (also `10.0.100-rc.1.25451.107` installed); `global.json` selects only the MTP runner; no leftover test processes before the run | At each resumption |
| V1 | Backend/tool integrity against `backend/windows-x64/profile.json` | `sha256sum toolchain/windows_x64/kimi_backend_windows_x64_v1.lib toolchain/llvm-dlltool.exe backend/windows-x64/kernel32.def` | PASS | Archive `4ef5b90f…52d6`, dlltool `3733b6d4…fdfc`, kernel32.def `d08872d0…494e` all equal the profile's `artifactSha256`, `dlltoolSha256` and `definitionSha256` | If toolchain or profile changes |
| V2 | Restore (G5) | `dotnet restore Kimigayo.slnx` | PASS | Exit 0, all projects up to date, no NuGet.Config error | On package changes |
| V2 | Debug solution build | `dotnet build Kimigayo.slnx -c Debug --no-restore` | PASS | Exit 0, 0 warnings, 0 errors, 32.3 s; `build-debug.log` | After any source change |
| V2 | Release solution build | `dotnet build Kimigayo.slnx -c Release --no-restore` | PASS | Exit 0, 0 warnings, 0 errors, 15.5 s; `build-release.log` | After any source change |
| V3 | Focused Debug check | `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -class XunitTest.StructEmissionTest -parallelMode none -failSkips` | PASS | Total 39, Failed 0, Skipped 0; `focused-debug.log` | After struct/emission changes |
| V3 | Full Debug managed suite and fixture generation | `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -parallelMode none -failSkips -xml bin/plan-baseline/m1-8c49edb/full-debug.xml` | PASS | Total 6,986, Errors 0, Failed 0, Skipped 0, Not Run 0, 20.7 s; regenerated all 10,215 files (2,043 `.ll`) in `bin/scalar-fixtures`; inventory `fixtures-debug-inventory.txt` (SHA-256 `99b51886…3455`) | After any source change |
| V3 | Full Release managed suite and fixture generation | `dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -parallelMode none -failSkips -xml bin/plan-baseline/m1-8c49edb/full-release.xml` | PASS | Total 6,986, Errors 0, Failed 0, Skipped 0, Not Run 0, 17.2 s; regenerated all 10,215 fixture files; `fixtures-release-inventory.txt` is byte-identical to the Debug inventory (same SHA-256, zero `diff` lines), so Debug/Release compilers emit identical IR and expectations at this HEAD | After any source change |
| V4 | LLVM verifier on a fresh fixture | `./toolchain/opt.exe -passes=verify -disable-output bin/scalar-fixtures/StructLocal.ll` (run after the Debug and again after the Release generation) | PASS | Exit 0 both times; `StructLocal.ll` SHA-256 `087234fb…c292` in both configurations | After emitter changes |
| V5 | Native Struct family, Debug-generated then Release-generated fixtures | `./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'Struct*.ll'` | PASS | 18 fixtures; "Passed 36 native scalar executions (O0/O2)" per configuration, exit 0; `scalars-struct-debug.log`, `scalars-struct-release.log` | After emitter/backend changes or new Struct fixtures |
| V6–V9, V11 | Backend build, CLI, reuse, benchmarks, programs 1–6 | Not executed in this unit | NOT_RUN | Outside M1's required set (V1–V3 plus selected V4–V5); historical STATUS results remain historical | Run at the owning milestone or on a justified concern |
| V10 | Language runner | Unimplemented public interface | BLOCKED | G2 unchanged | I30, then G2/I31 |
| V12 | Plan-only diff gate | `git diff --check`; `git status --short` | PASS | Only `PLAN.md` differs; whitespace check clean | After each plan edit |

### Verification records (M2 unit 1, 2026-09-17)

Change set: `Kimi/Compiler/Parsing/Koto/Declarations/DeclarationContainers/DeclarationContainerKoto.cs`, `GroupKoto.cs`, `xUnitTest/Tests/SourceDocumentAndDiagnosticTest.cs`, `STATUS.md`, `PLAN.md` on top of HEAD `8c49edb` (uncommitted). Post-change identities (SHA-256): Debug `Kimi.dll` `1ec4b8a659032663240e4e7f5c9cb25245bc46a29ee8bd38862f223f39f85a48`, Debug `xUnitTest.dll` `1b6e99736b801e289a0152f3e3c418fe93adef61fd79c1f51c7841aa214f42ac`, Release `Kimi.dll` `f9ae59c289d6ab1214e5fb0f63d302d9ea76a214a888bae5bb99d428fcf3e30f`, Release `xUnitTest.dll` `f6bdf74632d5c0d0b8c15332de06469abb71babfacf00a702966f6438ff335af`. Logs: `bin/plan-baseline/m1-8c49edb/*-fix.log`, `*-fix.xml`, `check-milestone1..9.log`, `check-spectour.log`, `spec-probe/`.

| Verification ID | Target/configuration | Command (working directory `C:\Users\bwff1\repos\archi-Doc\Kimigayo`) | Result | Evidence | Reverification needed |
| --- | --- | --- | --- | --- | --- |
| V1 | Spec example parse probe (I3 audit) | Scratch console app (copy in `spec-probe/`) parsing each ```kimi block of `spec/*.md` through `CreateCodeContext().Parse(RootKoto, source)` after `Prepare("x86_64-pc-windows-msvc")` | PASS (audit) | 250 blocks, 39 with errors, all intentional/fragments; targeted reproducers confirmed parenthesized reference Types, nested `ref/uniq/unsafe` layers and struct Property accessors parse; `$expect`/`$require` do not (I30) | After grammar or spec example changes |
| V11 (check only) | `dotnet Kimi/bin/Release/net10.0/Kimi.dll check milestones/MilestoneN.kimi`, N = 1..9, pre-change Release compiler | PASS 1–6; FAIL 7–9 as expected by D5 | 1–6 exit 0 with no diagnostics. 7: 22 sites (`UnsupportedBinding_Kd` at `for row in matrix.indices`, M8). 8: 22 sites (`UnresolvedBinding_Kd` at `Toolkit.Selection.choose(...)` with generic `Box<string>.init`, M6). 9: 75 sites (`MissingOriginBinding_Kd` at `return self.value@ref/T` inside generic `Batch<T>.view`, R7/M3–M6; `InvalidCaptureBinding_Kd`, `NotCallable_Kd` for the callback, M7; iteration, M8) | When M6–M8 land; native V11 scripts for 1–6 remain NOT_RUN this execution |
| V7 (check only) | `dotnet Kimi/bin/Release/net10.0/Kimi.dll check examples/SpecTour/SpecTour.kimiproj` | FAIL as expected (beyond support) | 544 sites: `UnsupportedBinding_Kd` 381, `UnresolvedBinding_Kd` 105, `InvalidConstraint_Kd` 27, `NotCallable_Kd` 12, `InvalidCaptureBinding_Kd` 7, `ControlFlow_Kd` 4, `UnprovenConstraint_Kd` 3, `InvalidAssociatedType_Kd` 3, `TypeMismatch_Kd` 1, `MissingOriginBinding_Kd` 1; per file Sequences 184, Callables 96, ControlFlow 76, Lifetimes 64, Generics 46, Objects 33, Native 22, Basics 19. Basics.kimi review produced G9/T3a; other codes map to M6–M11 boundaries | Rerun as milestones land to shrink the inventory |
| V3 | Reproducer before fix | `git stash push` of the two parser files, Release build, `xUnitTest.dll -method XunitTest.SourceDocumentAndDiagnosticTest.MergedContainerRetainsFirstFragmentSourceDocument`, then `git stash pop` | PASS (expected FAIL observed) | `Assert.Same() Failure: Values are not the same instance`; sources restored, `git diff --stat` unchanged | None |
| V2 | Debug and Release solution builds with T3a | `dotnet build Kimigayo.slnx -c Debug --no-restore`; `-c Release` | PASS | 0 warnings, 0 errors each; `build-release-fix.log` | After further source changes |
| V3 | Focused Debug classes | `xUnitTest.dll -class` SourceDocumentAndDiagnosticTest, ContainerFragmentBindingTest, KotoHierarchyTest, ParserRegressionTest, KotonohaSerializationTest, ModuleBindingTest, ParserOptimizationTest `-parallelMode none -failSkips` | PASS | Total 340, Failed 0, Skipped 0 | After parser/container changes |
| V3 | Full Debug suite with T3a | `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -parallelMode none -failSkips -xml .../full-debug-fix.xml` | PASS | Total 6,987, Failed 0, Skipped 0, 20.6 s | After further source changes |
| V3 | Full Release suite with T3a | `dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -parallelMode none -failSkips -xml .../full-release-fix.xml` | PASS | Total 6,987, Failed 0, Skipped 0, 15.6 s | After further source changes |
| V4–V5 | Native fixtures after T3a | Not rerun | NOT_RUN (justified) | `fixtures-release-fix-inventory.txt` is byte-identical to the M1 Debug inventory (zero `diff` lines), so the M1 V4/V5 PASS records still describe the generated IR | Rerun when emission changes |
| V7 | CLI location after fix | `dotnet Kimi/bin/Debug/net10.0/Kimi.dll check <scratch>/repro/CopyComparable.kimi` | PASS | Container diagnostic now `CopyComparable.kimi:1:1` (was `CopyComparable:@0`); remaining four diagnostics unchanged (G9) | With G9 |
| V11 | Native program 6 with T3a compiler | `./backend/windows-x64/test-milestone6.ps1 -ToolchainRoot ./toolchain -Configuration Debug`, then `Release` | PASS | 63 checks each; run IDs Debug `99b46dee8dbd4fbeba7dcda080bc766c`, Release `f9462400eb0f4ee488fab5ab0097c9b8` under `bin/milestone6/<configuration>/`; logs `milestone6-{debug,release}-fix.log` | After emitter/runtime changes |
| V11 | Native programs 1–5 with T3a compiler | `./backend/windows-x64/test-milestoneN.ps1 -ToolchainRoot ./toolchain -Configuration Debug` then `Release`, N = 1..5 | PASS: 25, 21, 37, 33 and 47 checks in Debug and again in Release; run IDs recorded in each verification.json under bin/milestoneN/<configuration>/ | Logs `milestoneN-{debug,release}-fix.log` under `bin/plan-baseline/m1-8c49edb/` | After emitter/runtime changes |
| V12 | Diff gate | `git diff --check`; `git status --short` | PASS | Whitespace clean; five modified tracked files plus the untracked user draft file, which is untouched | After each edit |

### Verification records (M2 unit 2 / T1a, execution started 2026-09-17 01:42:22 JST)

Base: `826f6077a2654a6d2be8aa3865b655224bf615ae`. Working directory: `C:\Users\bwff1\repos\Kimigayo`. Evidence: `bin/plan-execution/20260917-014222/`. The prior run's `bin/plan-baseline/` logs are absent from this checkout; their recorded results remain historical, not newly inspected artifacts. No draft file was read or edited.

| Verification ID | Target/configuration and command | Result | Evidence / remaining check |
| --- | --- | --- | --- |
| V2 | `dotnet build Kimigayo.slnx -c Debug --no-restore` | PASS | 0 warnings/errors; `t1a-build-debug.log`. Initial member-order warning was corrected before this build. |
| V3 | Debug focused: FrontEndSyntaxTest, ParserRegressionTest, SourceDocumentAndDiagnosticTest, ContainerFragmentBindingTest, ParserOptimizationTest, KotonohaSerializationTest; direct DLL runner with `-parallelMode none -failSkips -result-xml` | PASS | 275 cases, zero failures/skips; `t1a-focused.log/xml`. |
| V3 | Original HEAD parser with new negative tests, Release | PASS (expected FAIL observed) | Temporarily restored the three original parser/reader/container files, built Release, ran `-method XunitTest.FrontEndSyntaxTest.UnavailableModifiersReportOneCauseAndRecover`: all 28 failed, exit 1, for missing dedicated diagnostic/recovery; `t1a-baseline-repro.log/xml`. Current files restored in `finally`. |
| V2–V3 | Current Release `dotnet build Kimigayo.slnx -c Release --no-restore -t:Rebuild`; full Debug/Release DLL runners with `-parallelMode none -failSkips -result-xml` | PASS | Release build: 0 warnings/errors. Both full suites: 7,027 cases, zero failures/skips (`t1a-full-debug.log/xml`, `t1a-full-release.log/xml`). Forced rebuild avoids timestamp reuse after restoring baseline source backups. Compiler/test DLL hashes: `t1a-artifact-hashes.txt`. |
| V7 | Release CLI `check bin/plan-execution/20260917-014222/unavailable.kimi`, with a valid independent startup expression | PASS (required rejection) | Exit 1; exactly `UnavailableFeature_Kd` at `unavailable.kimi:1:1`, highlighting `abstract`; `t1a-cli-check.log`. |
| V9 | Existing FrontEndBenchmark common workload; local harness, Release, tiering disabled, 2,000 warmups then seven samples of 5,000 parses | PASS (measurement) | Median baseline 5,995.7 ns/parse, current 6,206.7 ns/parse (+3.5% in this local sample); all seven allocation samples identical (8,896–9,000.9 B/parse, including buffer growth). No speedup or universal allocation claim. `t1a-perf-common-{baseline,current}.log`, baseline compiler hash and harness under `perf/`. |
| V4–V6 / V11 | Native/LLVM checks | NOT_RUN | This unit changes rejection/recovery and has full parser/emission managed regression coverage; no lowering/runtime/ABI changes or new executable feature. Historical native results remain historical. |
| V12 | `git diff --check` and source review | PASS | Only planned parser/reader/diagnostic/tests and documentation changes; no draft changes. |

Environment notes: the scratch harness restore hit the existing sandbox denial reading user NuGet.Config. An approved `dotnet restore` using the harness's empty package-feed configuration succeeded; the harness references built local assemblies and adds no package dependency. NativeAOT was not run.

Out-of-scope finding from V9: `FrontEndBenchmark.ExtendedSource` fails `Setup()` on the original parser (`Benchmark source must parse without diagnostics`). The common workload was measured with the same existing benchmark method; the invalid extended fixture was not edited. Its failed run is retained as `t1a-perf-baseline.log`; Ctrl+C ended the lingering failed process. Repairing this pre-existing benchmark input is separate from T1a.

### Verification records (M2 unit 3 / T5a, 2026-09-17)

Same execution root and baseline as unit 2, with T1a retained. Source changes: `Binding.Diagnostics.cs` (new failure-only cause storage), four constraint/Property validation call sites, reset/report hooks in `Binding.cs`, and the two existing test classes. No proof result or internal issue is removed; only publication of established derived diagnostics is suppressed. Other environment facts conservatively retain their header diagnostics, since they may contain independent contradictions.

| Verification ID | Target/configuration and command | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before production change: Debug UnresolvedConstraintBindingTest + SourceDocumentAndDiagnosticTest | PASS (expected FAIL observed) | 47 tests, three intended failures: both clause orders reported five diagnostics and the interaction case retained the redundant header. The revised T3a contradictory-premise fixture passed. `t5a-baseline.log/xml`. |
| V2 | `dotnet build Kimigayo.slnx -c Debug --no-restore`; Release equivalent | PASS | Zero warnings/errors in each; `t5a-build-{debug,release}.log`. |
| V3 | Focused Debug: UnresolvedConstraintBindingTest, SourceDocumentAndDiagnosticTest, ConstraintBindingTest, ConstraintContextBindingTest, LateConstraintEnvironmentBindingTest, ContractBindingTest | PASS | 199 cases, zero failures/skips; `t5a-focused.log/xml`. Nine new cases cover diagnostic scope, invalid certificates/uses, independent input contradictions, fragment locations, multiple Names and rebind/reload. |
| V3 | Full Debug and Release DLL runners with `-parallelMode none -failSkips -result-xml` | PASS | 7,036 cases each, zero failures/skips; `t5a-full-{debug,release}.log/xml`; hashes in `t5a-artifact-hashes.txt`. |
| V7 | Release `check .../t5a-unresolved.kimi` with an independent valid startup expression | PASS (required rejection) | Exit 1, one `UnresolvedBinding_Kd` at `Comparable`, line 3 column 13; `t5a-cli-check.log`. |
| V9 | `WarmMissingConformanceBindingReusesDiagnosticCauseStorage` and existing warm Binding tests | PASS | Zero measured bytes after warmup in Debug/Release; lazy cause storage is cleared/reused on rebind. No throughput claim. |
| V4–V6 / V11 | Native/LLVM checks | NOT_RUN | Reporting-only behavior; semantic states are retained and managed emission regression passes. |
| V12 | Diff review and `git diff --check` | PASS | Intended source/tests/docs only; no draft edits. |

### Verification records (M2 unit 4 / T27a, 2026-09-17)

Changes: `Compilation.BeginSourceParsing` and snapshot reload invalidate existing analysis without constructing lazy Binding/ownership services; `Binding.Invalidate` revokes summaries, startup, conformance/Property certificates and cached capability results. Existing ownership invalidation revokes body verification. Rebinding reuses the retained storage. No persistent-cache behavior is claimed.

| Verification ID | Target/configuration and command | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before source change: Debug MinimalEmissionTest | PASS (expected FAIL observed) | Six new append/reload cases all wrote stale IR; 29 total, six intended failures. `t27a-baseline.log/xml`. |
| V2 | Debug/Release solution builds, `--no-restore` | PASS | Zero warnings/errors; `t27a-build-{debug,release}.log`. |
| V3 | Focused Debug: MinimalEmissionTest, ContractBindingTest, UnresolvedConstraintBindingTest, KotonohaSerializationTest, ModuleBindingTest | PASS | 351 cases, zero failures/skips; seven new cases include retained certificate revocation and successful recertification. `t27a-focused.log/xml`. |
| V3 | Full Debug/Release DLL runners, `-parallelMode none -failSkips -result-xml` | PASS | 7,043 tests each, zero failures/skips; `t27a-full-{debug,release}.log/xml`; compiler/test identities in `t27a-artifact-hashes.txt`. |
| V4–V5 / V7 / V11 | `./backend/windows-x64/test-milestone1.ps1 -ToolchainRoot ./toolchain -Configuration Debug`, then Release | PASS | 25 checks each, including newly generated LLVM/O0/O2/native/CLI paths; logs `t27a-native-{debug,release}.log`, run manifests under `bin/milestone1/<configuration>/`. Initial sandboxed `opt.exe` version probe denied; approved native execution succeeded. NativeAOT NOT_RUN. |
| V12 | `git diff --check` and focused diff review | PASS | Source/reset hooks, seven cases and documentation; no draft changes. |

### Verification records (M2 unit 5 / T27b, 2026-09-17)

Both parsing entry points capture a monotonic Error report version before lexing/parsing and latch newly reported source errors on the owning Kotonoha. The version counts attempted reports before location deduplication and survives diagnostic clearing. Existing errors and warnings alone do not poison a valid parse. This adds one integer field per diagnostic collection and no per-report allocation.

| Verification ID | Target/configuration and command | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before production change: Debug MinimalEmissionTest | PASS (expected FAIL observed) | Four direct/custom parsing cases emitted IR after source errors were cleared; 35 tests, four intended failures. `t27b-baseline.log/xml`. |
| V2 | Debug/Release solution builds, `--no-restore` | PASS | Zero warnings/errors; `t27b-build-{debug,release}.log`. |
| V3 | Focused source/diagnostic/emission tests | PASS | 204 cases, including seven new cases for custom destinations, duplicate locations, existing diagnostics and warning-only parsing; `t27b-focused.log/xml`. |
| V3 | Full Debug/Release DLL runners, `-parallelMode none -failSkips -result-xml` | PASS | 7,050 cases each, zero failures/skips; `t27b-full-{debug,release}.log/xml`; identities in `t27b-artifact-hashes.txt`. |
| V4–V5 / V7 / V11 | Existing Milestone 1 script, `-ToolchainRoot ./toolchain -Configuration Debug`, then Release | PASS | 25 checks each; `t27b-native-{debug,release}.log`. Run manifests: Debug `339177f75de748149c5774d70680f746`, Release `171779dbe82741c38e9846e77754e0d1` under `bin/milestone1/<configuration>/`. NativeAOT NOT_RUN. |
| V12 | Diff review and `git diff --check` | PASS | No draft or specification changes. Existing normative rules already require current-source validity. |

### Verification records (M2 unit 6 / T7a, 2026-09-17)

The A.3 audit inspected `Binding.ArgumentOperations.ProjectedReceiverProof`, method selection, Property accesses and inherited Contract witnesses against §12.4.4. The implementation has only internal Error/Unknown receiver proof, with no effect-family fixed point or public Proven/NotProven summary. Standard storage projections are independent Place operations and remain supported. Existing tests already retain base paths, Origins, no-reselection behavior and unverified witnesses; adding duplicate tests would not supply the missing verifier.

T7a corrects the method-call diagnostic for pending effect verification from `UnprovenConstraint_Kd` to `UnsupportedBinding_Kd`, matching the existing custom-accessor boundary. The prior three adaptation-case assertions were inconsistent with §12.4.4.1's prohibition on masking unimplemented verification as a completed negative guarantee; they now also assert no Unproven diagnostic and rejected emission. Internal Unknown and selected operations are unchanged. Full effect-family computation and dependent witness/use completion remain I7, including ordinary generic bodies and every explicit specialization; artifact summary validation remains I29. These are implementation work, not specification gaps or permission requests.

| Verification ID | Target/configuration and command | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before production change: `-method XunitTest.InheritedReceiverBindingTest.ProjectedCallsRetainTheirPlanButRequireEffectProof` | PASS (expected FAIL observed) | All three adaptation cases failed for the intended Unproven-vs-Unsupported mismatch; `t7a-baseline.log/xml`. |
| V2 | Debug/Release solution builds, `--no-restore` | PASS | Zero warnings/errors; `t7a-build-{debug,release}.log`. |
| V3 | Debug InheritedReceiverBindingTest, ReceiverShorthandTest, DefaultBindingTest, ContractBindingTest and MinimalEmissionTest | PASS | 204 cases, zero failures/skips, including direct receivers, standard storage, inherited witness rejection and no overload reselection; `t7a-focused.log/xml`. |
| V3 | Full Debug/Release DLL runners, `-parallelMode none -failSkips -result-xml` | PASS | 7,050 cases each, zero failures/skips; `t7a-full-{debug,release}.log/xml`; final identities in `t7a-artifact-hashes.txt`. |
| V7 | Release CLI `check .../t7a-projected.kimi` | PASS (required rejection) | Exit 1; one `UnsupportedBinding_Kd` at the selected call, line 4 column 30; `t7a-cli-check.log`. |
| V4–V5 / V11 | Additional native run after T7a | NOT_RUN | Only a rejected projected-call diagnostic changed; T27b's 25-check native runs per configuration cover the unchanged valid scalar path. No effect proof or generation guard was relaxed. |
| V9 | Final common FrontEndBenchmark, same harness/settings as T1a, no concurrent build/test | PASS (measurement) | Median 6,145.3 ns/parse versus original 5,995.7 (+2.5% local sample); all seven allocation samples remain identical. `final-perf-common-isolated.log`. An earlier final sample overlapped the full suite and is retained in `final-perf-common.log` but excluded from timing comparison. No speedup claim. |
| V12 | Complete source/test diff review and `git diff --check` | PASS | Five coherent fixes; 63 new cases overall and three strengthened existing effect-boundary cases. New failure-only cause storage is the only new source file. |

### I4 next-unit audit checkpoint (2026-09-17, about 57 minutes elapsed)

`BoundCall.Set` retains explicit argument operations, receiver operation, parameter mappings, Types and Origins, but no ordered omitted-default operation list. `EvaluateCandidate` correctly permits omission only for `IsOptional` and counts defaults after mapping; default expressions are type-checked in the declaration's scope in `Binding.Expressions`. `OwnershipAnalysis` rejects every optional/default parameter before body analysis. Existing DefaultBindingTest (14 cases) covers declaration lookup, invalid Types/Names, rebind/reload and warm allocation, but does not prove pending-slot ownership semantics.

Two current Release CLI probes, each with an independent Unit startup, confirm the boundary: `func f(x: i32, y?: i32 = x) => ()` and `func f(x: string, y?: string = x) => ()` both exit 1 with `UnsupportedOwnership_Kd` on the default. The first is valid per §7.2; the second must eventually fail for moving a preceding prepared argument. Inputs and logs are retained under `default-audit/`. This is a verified support-boundary audit, not completed default implementation or evidence that the invalid program was semantically classified correctly.

Next implementation: extend the winner's reusable call-plan storage with omitted defaults in parameter order, retaining declaration-bound expression identity, substituted Types/Origins and prepared preceding parameter slots. Keep explicit receiver/argument acquisition in source order and prohibit default-based inference/reselection. First tests should inspect named/positional/receiver interleaving, chained defaults and rebind/reload storage reuse. Follow with I8's declaration-time checking of every default, no Move/write/new escaping Borrow from preceding slots, normal-transfer reverse cleanup and Abort behavior before removing the existing ownership/emission guard. This implementation was not started in the current timed execution.

### Verification records (M2 unit 7 / T4a, execution started 2026-09-17 02:42:36 JST)

Fresh inspection: HEAD `826f6077a2654a6d2be8aa3865b655224bf615ae` plus prior uncommitted implementation; all 22 prior source/test hashes matched before editing. Evidence root: `bin/plan-execution/20260917-024236/`. T4a adds selected omitted-default metadata to existing reusable BoundCall storage; it changes no ownership or emission guard.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Existing DefaultBindingTest, InheritedReceiverBindingTest and FunctionParameterIdentityBindingTest, Debug | PASS | 93 baseline cases, zero failures/skips; `t4a-baseline.log/xml`. Inspection established absent default-plan storage; this is a new contract representation, not a changed previous behavior assertion. |
| V2 | `dotnet build Kimigayo.slnx -c Debug --no-restore`, Release equivalent | PASS | Zero warnings/errors; `t4a-build-{debug,release}.log`. |
| V3 | Same focused classes with twelve new cases | PASS | 105 cases, zero failures/skips; `t4a-focused.log/xml`. |
| V3 | Debug/Release direct DLL runners, `-parallelMode none -failSkips -result-xml` | PASS | 7,062 each, zero failures/skips; `t4a-full-{debug,release}.log/xml`. |
| V9 | WarmOmittedDefaultPlansReuseTheirStorage plus prior warm Binding tests | PASS | Zero measured allocation after warmup in both configurations. One empty-default array reference per call and reusable failure-safe scratch; omitted defaults allocate their array on first use. No throughput claim. |
| V4–V6 / V11 | Additional native checks | NOT_RUN | This metadata-only unit retains all prior runtime guards; managed fixture generation and unchanged explicit-call regression pass. No new executable feature or ABI change. |
| V12 | Source/test review and `git diff --check` | PASS | Only Binding.Calls.cs, DefaultBindingTest.cs and English documentation changed in this unit. SPEC already owns these rules; drafts untouched. |

### Verification records (M2 unit 8 / T4b, 2026-09-17)

Same fresh baseline/evidence root as T4a. Changed ControlFlowAnalysis's declaration traversal and ControlFlowContext's value/transfer rules; no runtime default guard changed. Defaults are visited independently even when there is no body, with their parameter expected Type. Transfer lookup stops when crossing a default root. Their flow does not contribute to the callee body.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Before production fix: Debug DefaultBindingTest | PASS (expected FAIL observed) | Ten intended failures among 36 tests: missing flow, discarded values and escaping lexical targets; `t4b-baseline.log/xml`. |
| V2 | Debug/Release solution builds, `--no-restore` | PASS | Zero warnings/errors after member-order correction; `t4b-build-{debug,release}.log`. |
| V3 | DefaultBindingTest, CurrentControlFlowTest and ControlFlowConformanceTest, Debug | PASS | 222 cases including twelve new regressions; `t4b-focused.log/xml`. |
| V3 | Full Debug/Release direct DLL runners with `-parallelMode none -failSkips -result-xml` | PASS | 7,074 each, zero failures/skips; `t4b-full-{debug,release}.log/xml`. |
| V9 | WarmDefaultFlowAnalysisReusesStorage | PASS | Zero measured bytes after warmup in Debug/Release. |
| V4–V6 / V11 | Native default execution | NOT_RUN | Still rejected by ownership; T4c owns the first executable default slice. Existing managed flow/emission regressions pass. |
| V12 | Source review / `git diff --check` | PASS | Existing specification already requires independent defaults, value contexts and contained transfers. No specification or draft edits. |

### Execution unit 9 — T4c scalar default acquisition (DONE)

At about 33 minutes elapsed, scalar defaults are acquired after explicit arguments in declaration order, using copies in prepared slots. Literal, preceding scalar parameter, arithmetic/bit/comparison/unary and short-circuit expressions use existing ownership/SSA and checked arithmetic lowering. Generic, owned/borrowed and effectful default bodies retain rejection. Initial emission reproducers failed before implementation (10 of 13). A short-circuit case exposed branch-local reads overwriting the acquired SSA value; these reads now preserve the prepared value. Malformed tests also exposed missing default-order validation, now checked. The new temporary/result read path rejects aliases to another acquired place. Required-initializer/unused scalar-default expectations were updated because this unit intentionally enables those declarations. Receiver, nested, named, snapshot, repeated, chained and noncompleting cases are covered. Initial native Debug verification passed 24 O0/O2 executions before the receiver case; final native and full Debug/Release verification remain pending. Native LLVM probing was sandbox-denied and passed under authorized escalation. No NativeAOT run.

T4c completed at 03:22 JST, about 40 minutes elapsed. Final serial full suites pass 7,095 cases each, zero failures/skips. Both builds have zero warnings/errors. The 21 new cases include four malformed-plan/read checks with recovery; declaration guards remain for unsupported forms even when supplied. A proposed 128-bit division guard was unnecessary: the probe already fails Binding, so no duplicate guard or misleading ownership-stage test remains. An intermediate pair of full runners overlapped; their failed probe results were superseded by the final serial runs. No blanket I4/I8 completion is claimed.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V2/V3 | Debug/Release clean builds and serial full suites | PASS | `t4c-build-{debug,release}.log`, `t4c-full-{debug,release}.log/xml`; 7,095 each. |
| V4–V6 | Thirteen default fixtures per configuration, O0/O2 | PASS | `t4c-native-{debug,release}.log`; 26 executions each, including checked overflow, nontermination, snapshots, receiver placement and short-circuiting. |
| V4–V6 | Existing Function fixtures generated by Release | PASS | `t4c-native-functions-release.log`; 56 O0/O2 executions. |
| V4–V7 | Milestone 1 Debug/Release | PASS | `t4c-milestone1-{debug,release}.log`; 25 checks each, report paths in logs. |
| V9 | WarmDefaultOwnershipAndEmissionReuseStorage | PASS | Zero bytes after warmup in both full suites. Retained integer slot buffer; no per-call managed allocation after capacity stabilizes. |
| V12 | Diff/specification review and evidence identities | PASS | `git diff --check`; `t4c-source-hashes.txt`, `t4c-artifact-hashes.txt`, `t4c-fixture-hashes.txt`. Existing §7.2–3 rules unchanged; draft untouched; NativeAOT NOT_RUN. |

### Execution unit 10 — T4d scalar conversions in defaults (DONE)

Started 03:23 JST, about 41 minutes elapsed, after T4c verification and documentation. §7.2 admits scalar Copies; §13.5.4 defines existing numeric conversion checks and rounding. The default whitelist currently rejects ConversionKoto even when Binding has a supported scalar conversion plan. Reuse that plan and existing ownership/conversion lowering, retaining rejection for Borrow/Reborrow and nonscalar results. Required evidence: original rejection cases, narrowing/widening/identity/literal/float and supplied-default behavior, Abort source locations, full Debug/Release regressions and O0/O2 native execution. No new conversion semantics or I4/I8 completion claim.

Completed 03:27 JST, about 45 minutes elapsed. The nine new original-code cases all failed at the unsupported-default boundary, then passed after admitting existing Identity/Literal/Integer/Floating/Numeric plans with scalar operands. No Borrow/Reborrow plan is admitted. Existing conversion lowering and its validation were unchanged. Warm ownership/emission coverage now includes a widening/arithmetic/narrowing chain.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Original/fixed ScalarDefaultEmissionTest | PASS | `t4d-baseline.log/xml`: nine expected failures among 30; `t4d-focused.log/xml`: 30 pass. |
| V2/V3 | Debug/Release builds and serial full suites | PASS | Zero warnings/errors; 7,104 cases each, zero failures/skips; `t4d-build-{debug,release}.log`, `t4d-full-{debug,release}.log/xml`. |
| V4–V6 | Nine conversion-default fixtures per configuration | PASS | 18 O0/O2 executions each; `t4d-native-{debug,release}.log`. Widen/narrow/identity/literal/float truncation/ties-to-even/signed-zero/supplied/Abort behavior. |
| V9/V12 | Warm storage, source/spec review and identities | PASS | Zero measured warm allocation; `git diff --check`; `t4d-{source,artifact,fixture}-hashes.txt`. Existing normative conversion rules unchanged. NativeAOT NOT_RUN. |

### Execution unit 11 — T4e scalar selection defaults (DONE)

Started 03:27 JST, about 45 minutes elapsed. After T4a–T4d, extend the same scalar default subset to value-producing if/else expressions with scalar-only conditions and single-expression arms. Check every arm, including supplied/unused defaults and runtime-unreached branches. Reuse existing result joins and cleanup; no effectful arm, external transfer, generic or borrowed default support is inferred. Required checks: baseline rejection, selected-arm overflow avoidance, repeated/nested/default-chain selection, negative hidden effects, full Debug/Release suites and O0/O2 execution.

Completed 03:32 JST, about 50 minutes elapsed. Five original selection emission cases failed before support; the hidden-effect rejection already passed. The initial nested fixture needed parentheses for the existing clause-grouping syntax (§2.2), then exercised the intended nested result joins. The former scalar-if unsupported expectation now checks unsupported do instead; DefaultBindingTest correctly admits the now-supported supplied scalar selection. Every arm is checked by indexed traversal with no iterator allocation.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Original/fixed default tests | PASS | `t4e-baseline.log/xml`: five failures among 36; `t4e-focused.log/xml`: 74 pass. |
| V2/V3 | Debug/Release builds and serial full suites | PASS | Zero warnings/errors; 7,110 each, zero failures/skips; `t4e-build-{debug,release}.log`, `t4e-full-{debug,release}.log/xml`. |
| V4–V6 | Five selection-default fixtures per configuration | PASS | Ten O0/O2 executions each; `t4e-native-{debug,release}.log`; selected-arm overflow avoidance, nesting/chaining, repetition and conversions. |
| V9/V12 | Existing warm default regression and source review | PASS | Existing conversion-default zero-allocation test passes in both suites; no separate selection throughput claim. `git diff --check`; `t4e-{source,artifact,fixture}-hashes.txt`. No specification change or NativeAOT. |

### Execution unit 12 — T4f scalar do defaults (DONE)

Started 03:33 JST, about 50 minutes elapsed. §14.2 single-expression do bodies already have flow/result plans, including T4b's default value context. Admit only a do whose one trailing expression belongs to the supported scalar subset; retain unsupported jumps, loops, declarations and effectful bodies. Required checks: original-code failure, nested/chained/default snapshot cases, warm reuse including do/selection/conversion, full Debug/Release suites and O0/O2 execution. This does not complete general default transfers or cleanup.

Completed at 03:41 JST, about 58 minutes elapsed. Five original do emission cases failed, then passed using the existing scoped-result path. A sixth test verifies rebind/reload invalidation and identical regenerated IR. The existing allocation test now combines do, if and conversion defaults. Unsupported expectations were advanced from simple do to loops; internal jump/loop support remains open. No source implementation work is left unverified in this unit.

| Verification ID | Target/configuration | Result | Evidence |
| --- | --- | --- | --- |
| V3 | Original/final focused defaults | PASS | `t4f-baseline.log/xml`: five failures among 41; final `t4f-focused.log/xml`: 80 pass. |
| V2/V3 | Final Debug/Release builds and serial full suites | PASS | Zero warnings/errors, 7,116 each, zero failures/skips; `t4f-build-{debug,release}.log`, `t4f-full-{debug,release}.log/xml`. |
| V4–V6 | Five new do fixtures; final replay of every default fixture | PASS | Initial `t4f-native-debug.log`: ten executions. `final-defaults-{debug,release}.log`: 64 O0/O2 executions each across 32 fixtures, including all previous default units. |
| V4–V7 | Final Milestone 1 Debug/Release | PASS | `final-milestone1-{debug,release}.log`: 25 checks each; machine-readable reports and exact hashes at their logged paths. |
| V8/V9 | Default plan rebind/reload and warmed compound default reuse | PASS | Rebind/reload rejects premature emission, then reproduces identical IR. Warm do/selection/conversion ownership and IR writing allocate zero measured bytes in both suites. |
| V12 | Final source/doc review, identities and scope | PASS | `git diff --check`; `t4f-{source,artifact}-hashes.txt`, final source/fixture/document manifest. SPEC already defines these rules, so no normative change; draft untouched and NativeAOT NOT_RUN. |

### Execution unit 13 — T4g definite default Move diagnostics (DONE)

Started 03:43:10 JST with a fresh matching prior manifest. §7.2 requires declaration-time rejection even for fully supplied defaults. Nine original tests failed because direct non-Copy acquisitions produced only Unsupported or, for bodyless requirements, no ownership issue. A reusable declaration visitor now recognizes acquired preceding parameters in result/initializer/transfer/committed-call contexts and reports `DefaultArgumentMove_Kd` for Refuted Copy proof. Scalar operators/shared inspection remain reads; generic Unknown proof, field paths, mutation, escaping Loans and captures remain separate unfinished cases. The visitor drops its declaration reference after checking and does not certify runtime support. Eighteen new cases and 98 focused cases pass, including diagnostic severity/span, owned receiver/identity conversion, shared-origin Copy and rebind/reload. The shared-reference control originally used independent elided Origins and was corrected to a common declared Origin. Final builds/full suites remain pending; execution is still guarded for unsupported forms.

Completed 03:54 JST, about 11m30s elapsed. Final clean builds have zero warnings/errors; both serial full suites pass 7,134 cases without failures/skips. Evidence in the current root: `t4g-build-{debug,release}.log`, `t4g-full-{debug,release}.log/xml`, `t4g-focused.log/xml` (98), and `t4g-baseline.log/xml` (nine intended failures among 13). Both `check default-move.kimi` CLI probes exit 1 with `DefaultArgumentMove_Kd` at the declaration (`t4g-cli-{debug,release}.log`). Existing warmed default Binding/flow/ownership/emission tests pass with zero allocation. Source/artifact hashes are `t4g-{source,artifact}-hashes.txt`; `git diff --check` passes. Native execution NOT_RUN for this diagnostic-only unit: no execution boundary was widened; existing managed generation regressions pass. NativeAOT NOT_RUN. A member-order warning was fixed before final verification.

### Execution unit 14 — T4h prepared subplace/aggregate acquisitions (DONE)

Started 03:54 JST, about 11m30s elapsed. Extend the definite-Move declaration check through committed owned field/tuple/array paths and aggregate value construction, preserving Copy fields and shared inspection. This closes missed acquisition contexts rather than enabling default execution. Check bound types/storage identities; do not classify arbitrary property getters or borrowed referents as owned subplaces. Required verification: original-code failures at the ownership stage, positive Copy/inspection controls, full Debug/Release and diagnostic/source review.

Checkpoint at about 16 minutes elapsed: committed owned storage paths now propagate prepared-root identity, while aggregate and enum payload acquisitions and assignment RHS use value acquisition context. Borrowed referents and arbitrary property getters are not treated as owned paths. Fifteen new cases and 189 focused cases pass. Baseline had seven intended ownership failures plus one early index-Type mismatch; changing that index from i32 to the specified isize reaches the intended check. Copy fields, shared inspection, Copy payloads and the borrowed-referent boundary remain covered. Clean builds/full suites are pending.

Completed 04:01 JST, about 18m20s elapsed. Both builds have zero warnings/errors and serial full suites pass 7,149 cases each without failures/skips. Evidence: `t4h-build-{debug,release}.log`, `t4h-full-{debug,release}.log/xml`, `t4h-focused.log/xml` (189). Existing warmed default tests pass. Native execution NOT_RUN for this diagnostic-only change; no execution boundary widened. `git diff --check` passes; source/artifact identities are in `t4h-{source,artifact}-hashes.txt`. No SPEC change, draft edit or NativeAOT.

### Execution unit 15 — T4i contained scalar default transfers (DONE)

Started 04:04 JST, about 21 minutes elapsed. Capture acquisition currently fails earlier Binding and has no committed capture plan; implementing it belongs with M7 rather than inventing a second lookup here. Unknown generic Copy proof remains an explicit unfinished definition-checking obligation. The next dependency-ready I4/I8 execution slice reuses T4b's contained targets and established loop/do result joins: scalar defaults with internal exit/yield/continue and loop expressions. Keep effectful calls, local declarations and general owned/borrowed cleanup unsupported. Required checks: baseline failures, snapshots/chaining/repetition, noncompletion skipping later defaults/callee, Abort source location, full Debug/Release, native O0/O2 and warm reuse.

Checkpoint at about 23 minutes elapsed: eleven new execution cases now pass; 158 focused cases pass including existing result lowering. A yield-to-do reproducer was corrected to the specified yield-to-selection target (the original also had an earlier flow error). Prior supplied-loop rejection expectations now test a loop containing an unsupported call. Warm reuse and rebind/reload tests now include an internal loop transfer. Every transfer's resolved target must be inside its owning default expression; Never is admitted only through the checked expression forms. Final full/native verification remains pending.

Completed 04:08 JST, about 25 minutes elapsed. Both builds have zero warnings/errors; serial full suites pass 7,160 cases each without failures/skips. Eleven new fixtures pass 22 native O0/O2 executions per configuration. Evidence: `t4i-build-{debug,release}.log`, `t4i-full-{debug,release}.log/xml`, `t4i-native-{debug,release}.log`, `t4i-focused.log/xml` (158), baseline (11 failures among 53). Warm compound default analysis/emission still measures zero allocation; rebind/reload reconstruct identical IR. `git diff --check` passes; source/artifact/fixture hashes retained. NativeAOT NOT_RUN; specification unchanged.

### Execution unit 16 — T4j immutable scalar default locals (DONE)

Started 04:10 JST, about 27 minutes elapsed. Extend checked default bodies to sequential expressions and initialized immutable scalar let bindings. Preserve their separate local storage and restrict every local reference to declarations inside the same default. Mutable/uninitialized locals and effectful/owned bodies remain unsupported until declaration-time ownership checking can certify them independently of call omission. Required baseline, scoped/repeated/chained/branch/snapshot cases, supplied negative controls, full Debug/Release, native O0/O2 and warm reuse. No new language rule.

Checkpoint at about 30 minutes elapsed: seven initial failures reproduced the missing local path; admitting shape alone then failed value-flow validation in all seven. Default local reads now use ordinary storage while prepared argument reads retain their SSA snapshots. Thirteen new cases (eight executable fixtures and five guards) include initializer Abort, scoped/repeated/chained/branch reads, supplied rejection and warm/reload coverage. A noncompleting local initializer reaches the existing unsupported checking-continuation state; its admission is explicitly guarded even when supplied, with two regression cases. This is a remaining I6/I8 obligation, not a language restriction. Full/native evidence pending.

Completed 04:16 JST, about 33 minutes elapsed. Clean Debug/Release builds and serial full suites pass 7,173 each without failures/skips. Eight fixtures pass 16 native O0/O2 executions per configuration; 171 focused cases pass. The warmed compound-default case now includes a local binding and measures zero bytes; rebind/reload rebuild identical IR. Evidence: `t4j-build-{debug,release}.log`, `t4j-full-{debug,release}.log/xml`, `t4j-native-{debug,release}.log`, `t4j-focused.log/xml`, initial and local-read baseline logs, source/artifact/fixture hashes. `git diff --check` passes; no SPEC/draft edit or NativeAOT. Noncompleting local-initializer checking and mutable locals remain unfinished.

### Execution unit 17 — T4k Unit defaults (DONE)

Started 04:17 JST, about 34 minutes elapsed. Unit is a Copy value with an existing zero-sized argument path; the default whitelist currently excludes it. Admit Unit literals/copies/results and initialized Unit locals through the same checked plans, without admitting effectful calls or mutable storage. Required baseline, chaining/selection/transfer/local/supplied/noncompletion cases, full Debug/Release and native O0/O2. Preserve scalar rules and established warm checks.

Checkpoint at about 37 minutes elapsed: eleven new tests pass (ten native fixtures plus warm Unit reuse). The initial nine failed before admission; call lowering also needed its matching Unit-Type validation rather than the former scalar-only check. No physical Unit operand is invented. Unit selections without else follow the existing Unit result rule. 182 focused cases and 20 Debug O0/O2 executions pass. Warm Unit default analysis/emission measures zero bytes. Full/Release native verification pending.

Completed 04:22 JST, about 39 minutes elapsed. Debug/Release builds have zero warnings/errors; serial full suites pass 7,184 each without failures/skips. Ten Unit fixtures pass 20 native O0/O2 executions per configuration; 182 focused cases and zero-byte warm Unit reuse pass. Evidence: `t4k-build-{debug,release}.log`, `t4k-full-{debug,release}.log/xml`, `t4k-native-{debug,release}.log`, `t4k-focused.log/xml`, baseline, source/artifact/fixture hashes. Forward/self local references were separately probed and rejected by Binding even when supplied (`default-{forward,self}-local.log`). No SPEC/draft edit or NativeAOT.

### Execution unit 18 — T4l initialized mutable scalar default locals (DONE)

Started 04:23 JST, about 40 minutes elapsed. Reuse T4j's corrected local storage and T4k Unit statement results for initialized scalar/Unit var locals, local assignments/updates, and while loops. Restrict every write target to a var declared inside the same default; prepared slots and owned contents remain immutable. Uninitialized/noncompleting initializers, effects, borrows and owned storage remain guarded. Required baseline, repeated finite loops, snapshot/read/update/float/bit behavior, supplied prepared-slot write rejection, Abort locations, full Debug/Release, native O0/O2 and warm reuse.

Checkpoint at about 42 minutes elapsed: twelve new cases and 194 focused cases pass. Eight original execution cases failed before support; three prepared-parameter write cases already failed Binding as required. Assignment/update targets must be mutable locals inside this default, with supported initializers; while and loop bodies retain existing scalar/Unit control flow. Noncompleting/uninitialized local initializers retain guards. Added update Abort location and extended warm/reload coverage to mutable local updates; zero warmed allocation remains. Full/native evidence pending.

Completed 04:29 JST, about 46 minutes elapsed. Final clean Debug/Release builds have zero warnings/errors; serial full suites pass 7,196 each without failures/skips. Nine mutable-local fixtures pass 18 native O0/O2 executions per configuration. Warm update analysis/emission measures zero bytes and rebind/reload preserve IR. Evidence: `t4l-build-{debug,release}.log`, `t4l-full-{debug,release}.log/xml`, `t4l-native-{debug,release}.log`, focused (194), baseline (eight expected failures), source/artifact/fixture hashes. A formatting warning was corrected before final rebuild/full suites. No SPEC/draft edit or NativeAOT.

### Execution unit 19 — T4m scalar prepared-subplace reads (DONE)

Started 04:30 JST, about 47 minutes elapsed. Admit only Copy scalar/Unit results from bound owned fields, tuple elements and fixed-array elements rooted in preceding prepared arguments. Reuse existing projection/bounds/inspection Loan plans and finish the temporary Loan before callee entry. Do not admit non-Copy acquisitions, borrowed-referent reads, writes or arbitrary property getters. Required baseline, nested/dynamic/repeated/index/Unit reads, owner destruction order, bounds Abort, full Debug/Release, O0/O2 and warm reuse. Time is checked before each substantial operation; no new unit after the deadline.

Checkpoint at about 53 minutes elapsed: eleven new cases and 225 focused cases pass. The eight original cases included seven default-support failures and an earlier nested tuple/array Binding failure. Nested tuple reads now have a correct-stage reproducer; the mixed nested-array Binding limit remains open. Lowering caches the following call once, then verifies default receiver identities against that call's actual acquired explicit argument slot and omitted default source. Wrong-parameter alias corruption is rejected with recovery. Index plans retain declaration-side read identity while preserving prepared SSA values. Nine native fixtures include owner destruction order and bounds Abort; warm prepared-element analysis/emission measures zero allocation. Final full/native evidence pending.

Completed 04:39 JST, about 56 minutes elapsed. Clean Debug/Release builds and serial full suites pass 7,207 each without failures/skips. Nine new Debug fixtures pass 18 O0/O2 executions; the final Release replay passes all 79 default fixtures (158 executions), covering the new reads, bounds Abort and owner destruction. Evidence: `t4m-build-{debug,release}.log`, `t4m-full-{debug,release}.log/xml`, `t4m-native-debug.log`, `final-defaults-release.log`, focused (225), baseline and source/artifact identities. Wrong prepared argument identity is rejected and recovers; warm element reads allocate zero bytes. Final Debug replay and Milestone 1 checks are the remaining verification checkpoint. No specification change, draft edit or NativeAOT.

### Final checkpoint for the third timed continuation

Execution checkpoint: **2026-09-17T04:43:08+09:00**, elapsed **59m58s** from 03:43:10 JST. T4g–T4m are DONE, with no unfinished implementation change in the current unit. Final Debug/Release builds have zero warnings/errors and full suites pass **7,207 each**, zero failures/skips. All **79 default fixtures per configuration pass O0/O2 (158 executions each)**. Milestone 1 passes **25 checks per configuration**; the machine-readable report paths are in `final-milestone1-{debug,release}.log`. Both final CLI checks exit 1 with the correct default Move diagnostic. Warm compound scalar/Unit/local/element checks pass at zero measured allocation.

Evidence root: `bin/plan-execution/20260917-034310/`. Final logs: `t4m-build-{debug,release}.log`, `t4m-full-{debug,release}.log/xml`, `final-defaults-{debug,release}.log`, `final-milestone1-{debug,release}.log`, `final-cli-{debug,release}.log`. `final-manifest.txt` records source, diagnostics, documents, compiler/test assemblies and default fixture identities; `final-status.txt` records the uncommitted workspace. Final tested source hashes match. `git diff --check` passes. No SPEC changes were needed because the owning rules are unchanged; draft untouched, NativeAOT NOT_RUN.

M2/M3 and the compiler-completion plan remain IN_PROGRESS. The exact resumption action is T4n in Section 2. Preserve all existing changes and re-read actual HEAD/diff/PLAN before continuing. This checkpoint closes the current verified unit near the soft deadline; no additional work unit is started.

### Execution unit 20 — T4n checking after noncompleting initializers (IN_PROGRESS)

Started 04:43:26 JST after fresh repository/manifest/instruction inspection. Investigate §14.10.3/§15.1 source-checking states after Never initializers and expressions, first in ordinary function bodies, then in every default declaration independently of omission. Preserve initialization/Move/Loan facts without inventing runtime successors or a value that never arrived. Required failing reproducers, ordinary ownership diagnostics, supported controls, reanalysis/reload, full Debug/Release, appropriate native and warm checks before removing any default guard. Keep separate sub-units if the ordinary-state prerequisite is independently verifiable.

**T4n-a IMPLEMENTED_UNVERIFIED, 04:52 JST (about 9 minutes):** fifteen new ordinary nonreturning-call tests fail on the original implementation. Calls now seed checking from acquired arguments, and end their temporary comparison Loans inside that checking region. This also fixes the lost/reintroduced Loan state revealed by local/element borrow controls. No runtime successor or initializer result is added. The former direct-call unsupported case is now a positive regression; loop/selection safety gates remain. Focused Debug checks pass 116 cases, including zero-allocation warm analysis/emission and source reload. Full Debug/Release and seven O0/O2 fixtures per configuration are pending. Evidence: `bin/plan-execution/20260917-044326/t4na-*`.

**T4n-a DONE, 04:57:12 JST (13m46s):** clean Debug/Release builds; full suites pass **7,223 each**, zero failures/skips. An older nonreturning-first-argument unsupported expectation now becomes supported under the same checking rule, with a new bounded native regression. Sixteen added tests total. After correcting the test's expected Abort column from 24 to the actual source column 25, rebuilt focused suites pass 16 per configuration and all **32 native O0/O2 executions** pass (eight fixtures/configuration). Milestone 1 passes **25 checks each**. STATUS updated; no specification change needed. Evidence is `t4na-*` in this run's root. Broader T4n remains IN_PROGRESS.

**T4n-b IN_PROGRESS:** support the ordinary-body prerequisite for state-neutral divergence: a loop containing only Unit or a bare continue to itself. Such a loop preserves all entry ownership facts; seed only its checking continuation from that entry. Keep effectful/branching loops unsupported until their continuation joins are modeled. Verify Never local initializers remain uninitialized, earlier Moves stay moved, checking assignments work, and no runtime result/backedge/cleanup is fabricated.

T4n-b checkpoint, 05:00 JST (about 17 minutes): **IMPLEMENTED_UNVERIFIED**. All nine original-code reproducers fail for the intended unsupported boundary; 189 focused cases now pass. Added a moved-value effectful-loop safety regression and extended result-state/rebind/reload/warm checks to the state-neutral loop. Full Debug/Release and native loop checks are pending. The declaration-side default guard remains unchanged until independent default ownership checking exists.

**T4n-b DONE, 05:02:19 JST (18m53s):** clean Debug/Release builds and full suites pass **7,235 each**, zero failures/skips. All **16 bounded native O0/O2 executions** pass (four fixtures/configuration). Twelve additional cases include no fabricated result/placement, preserved Move and assignment history, source reload, and zero measured warm allocation. The explicit effectful-loop regression retains Unsupported. STATUS updated; normative §14.10.3/§15.1 already specify the behavior.

**T4n-c IN_PROGRESS:** check every supported scalar/Unit default body from initialized preceding prepared parameters using the existing ownership CFG builder and solver, independently of omitted calls. Reuse a scratch body separate from executable function bodies. Then admit scalar/Unit locals with missing or Never initializers under ordinary initialization checking, replacing the T4n guard with required diagnostics. Keep general unsupported effects/owned results/captures and divergent continuation joins guarded. Required declaration/omitted/supplied/bodyless-requirement negatives, supported initialization and nontermination controls, invalidation/reload/warm, full suites and O0/O2 fixtures before DONE.

**T4n-c DONE, 05:10 JST (about 27 minutes):** eighteen baseline reproducers fail on the prior implementation. Independent declaration CFG checking now diagnoses initialization at the default's source, including bodyless requirements and every supplied/omitted/unused case. Scalar/Unit locals with missing/Never initializers use ordinary state checks; the original T4n guard test now requires UninitializedUse. The former initialized-before-use supplied guard is preserved as a native positive control. Full clean Debug/Release suites pass **7,253 each**, zero failures/skips, and all **344 native O0/O2 default executions** pass (86 fixtures/configuration). CLI, reload, rebind and zero-allocation warm valid/invalid checks pass. Evidence: `t4nc-*` in the current root. No SPEC text change is required.

**T4n-d IN_PROGRESS:** caller-result initialization after failed argument acquisition. CLI reproducers `nested-never-initializer.kimi` and `default-never-initializer.kimi` currently exit 0: an enclosing call fabricates a checking result after a Never argument/default. Require every argument acquisition to finish before producing the call result; preserve the checking state and normal initialization diagnostics. This is an additional I6/I8 defect beyond the direct Never-call slice. Separately inspect omitted-default completion in the flow analyzer before admitting dependent control-flow paths.

T4n-d checkpoint: **IMPLEMENTED_UNVERIFIED**. All seven new acquisition/result reproducers fail on prior code. Acquisitions now explicitly gate result production, including nested calls, missing defaults, assignments and owned outputs. Borrow-call validation distinguishes an independently proven noncompleting call from a missing normal result; a malformed-result regression preserves rejection. Focused checks pass 276 cases. Full suites and four fixtures/configuration are pending; rebind/reload/warm and runtime-vs-checking state checks now include nested noncompletion.

**T4n-d DONE, 05:20 JST (about 37 minutes):** clean Debug/Release suites pass **7,267 each**, zero failures/skips. Four new fixtures/configuration pass all **16 O0/O2 executions**; five existing string/aggregate/nonreturning argument fixtures pass **10 further executions** from the final Release generation. Full regression exposed stored-result validators requiring a ghost Produce; they now accept absence only for an unreachable, independently proven noncompleting call. Fourteen additional tests include result-state/reload/warm and malformed normal-result plans. Evidence: `t4nd-*`.

**T4n-e IN_PROGRESS:** compose selected omitted-default completion into call flow independently of declaration ordering. Cache each default's declaration-time flow summary without importing transfers into callers; structural completion must inspect selected defaults and bound recursive expansion. Preserve declared call result Types, fully supplied-call completion, callee-body independence and declaration checks. Then verify require/condition/result contexts and recursive-default safety before proceeding.

T4n-e checkpoint, 05:25 JST (about 42 minutes): **IMPLEMENTED_UNVERIFIED**. Five of seven initial flow probes fail on the prior code, while both supplied controls pass. Default summaries now include actual cleanup completion and are cached independently of call/declaration order; structural paths include selected omitted expressions. Recursive expansion stays pending instead of recursing indefinitely. Focused checks pass 211 cases; Debug full suite passes 7,282. Release/native verification is pending. A fresh analyzer run surfaced eleven formatting/analyzer warnings in new tests, including earlier units; all are fixed and the current Debug build has zero warnings/errors. Earlier “clean” wording for T4n-c/d refers to incomplete incremental warning output and is superseded by this explicit full recheck.

**T4n-e DONE, 05:27 JST (about 44 minutes):** Debug/Release builds have zero warnings/errors, full suites pass **7,282 each**, zero failures/skips. All **20 native O0/O2 executions** pass (five new fixtures/configuration). Fifteen added cases include forward declarations, unchanged declared call Type, supplied calls, require failure, cleanup-blocked completion, nested defaults, recursion safety and zero-allocation warmed flow reanalysis. Evidence: `t4ne-*`. Recursive default expansion remains pending and general effectful default execution remains unsupported.

**T4n-f IN_PROGRESS:** apply the same no-arrival rule to ordinary scalar unary/binary operands. Evaluate/check later source operands but produce no operator result if an operand supplies none. Verify local/default initialization diagnostics, checking-side effects, native nontermination/Abort, full suites and warm/reload state. Keep other noncompleting control-flow joins explicit.

T4n-f checkpoint, 05:32 JST (about 49 minutes): **IMPLEMENTED_UNVERIFIED**. Direct `stop() + 1` probes stop earlier in Binding; typed `value(stop())` operands and the admitted `1 << (loop => continue)` default reproduce seven missing ownership diagnostics. The boolean probe used the wrong `!` spelling and was corrected to `not`. Unary/binary analysis now checks operand arrival before creating its result. Focused checks pass all semantic cases after that syntax correction; full Debug/Release and two native fixtures/configuration are pending. No Binding rule was changed.

**T4n-f DONE, 05:33:03 JST (49m37s):** clean Debug/Release builds and full suites pass **7,294 each**, zero failures/skips. All **eight native O0/O2 executions** pass (two fixtures/configuration). Twelve new cases verify scalar/boolean missing operands, unused/supplied default checking, later-operand effects and warmed result-state/reload behavior. Evidence: `t4nf-*`.

**T4n-g IN_PROGRESS:** reuse the state-neutral divergence proof through single-expression do/label/parenthesis wrappers. This bounded wrapper has no ownership effects, so entry-state seeding is sound; general scope effects and branch joins retain Unsupported. Verify ordinary/default initialization and Move history, native wrappers, rebind/reload/warm and full regression before the final checkpoint. Deadline remains 05:43:26 JST.

T4n-g checkpoint, 05:36 JST (about 53 minutes): **IMPLEMENTED_UNVERIFIED**. Five baseline wrapper reproducers fail for the unsupported continuation; 257 focused tests now pass. The existing state-neutral proof is shared by loop and do wrappers, without allocating or adding runtime successors. An explicit scope with a Move retains the unsupported gate; warm/reload/result-state cases include the wrapper. Final full Debug/Release and native replay are in progress.

Exact next action after this unit: **T4n-h**, general scoped-default checking continuation. `bin/plan-execution/20260917-044326/next-scope-continuation.kimi` is a current CLI reproducer: a default do body contains a Never initializer, a checking-only scalar assignment and internal exit; after the omitted call, a read of an already initialized caller local receives `UnsupportedOwnership_Kd` at line 8:9. Carry the correct continuation out of that scope without restoring moved facts or importing nonexistent runtime arrivals; extend to multiple terminal paths only with explicit joins. The current state-neutral entry proof must not be broadened to effectful scopes merely by dropping its shape check.

**T4n-g DONE, 05:40:44 JST (57m18s):** clean Debug/Release builds and full suites pass **7,302 each**, zero failures/skips. Eight additional tests include the effectful-scope guard. Final replay passes all **364 default** and **80 noncompletion** native O0/O2 executions, including four wrapper executions per configuration. Warm analysis/emission/result-state and reload checks pass. No implementation unit remains unfinished; T4n-h and broader M2/M3 remain open.

### Final checkpoint for the 04:43:26 execution

Recorded **2026-09-17T05:43:06+09:00**, elapsed **59m40s**. T4n-a–g are DONE; 95 added cases, clean builds, 7,302 managed tests per Debug/Release configuration, 444 final native O0/O2 executions, and 25 Milestone 1 checks per configuration all PASS. Six final CLI checks reject the formerly accepted missing-initialization cases with UninitializedPlace_Kd. Warm checks PASS with zero measured allocation. Evidence is in bin/plan-execution/20260917-044326/final-*; final-manifest.txt binds the current source, test, documentation, binaries and fixture bytes. git diff --check passes; prior changes are preserved, draft files were not edited, and NativeAOT was NOT_RUN as instructed.

No implementation unit is in progress or unverified. Stop near the soft deadline without starting another substantial unit. The compiler-completion plan and M2/M3 remain IN_PROGRESS; resume T4n-h from the exact scoped-default reproducer above after fresh repository/PLAN inspection. No new user decision or external blocker is required. All changes remain uncommitted.

### Execution unit 21 — I6/I8 T4n-h scoped checking continuation

**IN_PROGRESS.** Execution started 2026-09-17 05:43:33 JST; soft deadline 06:43:33 JST. Fresh HEAD is 826f6077a2654a6d2be8aa3865b655224bf615ae; all 1,166 entries of the preceding final manifest match. Root AGENTS.md, execution instructions, current PLAN, ownership implementation and relevant §14.10.3 rules were reread; no nested AGENTS.md. Prior changes are preserved. Evidence root: `bin/plan-execution/20260917-054333/`.

The recorded scoped-default CLI reproducer still fails with UnsupportedOwnership_Kd at line 8:9. Implement the bounded straight-line scope continuation first: retain the terminal checking state before lexical restoration, including checking-only assignments and Moves. Do not seed from scope entry. Branching scopes and deferred cleanup require separate joins and remain guarded. Required verification: positive/negative state cases, runtime/result isolation, rebind/reload and warm allocation checks, Debug/Release suites, and ordinary O0/O2 native fixtures. SPEC already states the required behavior; no specification change is needed.

T4n-h checkpoint, 05:53 JST (~10 minutes): IMPLEMENTED_UNVERIFIED. Fifteen of seventeen new baseline cases failed for the expected lost checking state. The scope now captures the terminal checking seed before lexical restoration, guarded by a retained allocation-free straight-line visitor. The CLI reproducer passes; focused checks pass except a test helper subsequently corrected to identify the outer local write. Initial full Debug found one former unsupported nested outward-transfer fixture now valid. Its expectation is updated from the specified transfer semantics and a native case verifies that the later argument increment never executes. Required final rebuilds/full suites and native verification remain pending.

**T4n-h DONE, 05:55:08 JST (11m35s):** final clean Debug/Release builds and full suites PASS, **7,323 each**, zero failures/skips. Six new fixtures per configuration PASS O0/O2 (**24 native executions**); result-state, rebind/reload and warmed zero-allocation checks PASS. Twenty-one net additional cases preserve the formerly guarded moved-scope rejection as a precise PossiblyMovedUse test. No specification change or draft edit was needed. Evidence: `h-final2-*`, `h-native-*`, baseline CLI/tests and checkpoint manifest in the current evidence root.

### Execution unit 22 — I6/I8 T4n-i terminal conditional joins

**IN_PROGRESS, 05:55:08 JST (11m35s).** Next bounded change: construct a separate checking join when every if/else branch terminates and each branch supplies a verified continuation seed. Merge initialization guarantees by intersection and possible Move/assignment history by union using the existing state lattice. Do not add runtime edges or results. Missing seeds and unequal active comparison-Loan heads remain guarded. Required verification: both branch orders, initialized/uninitialized/Moved/let-history cases, checking-only writes, nested continuations, no runtime-state changes, rebind/reload/warm allocation, full Debug/Release and native O0/O2 behavior.

T4n-i checkpoint, 06:01 JST (~18 minutes): IMPLEMENTED_UNVERIFIED. All twelve baseline cases reproduced unsupported continuation diagnostics. Sixteen new cases now pass, including both branch orders, nested and already-unreachable joins, required rejection when a branch seed is unavailable, runtime-state isolation and zero-allocation warm/reload checks. Focused suite: 293 PASS. The join uses retained seed storage and the existing Must-intersection/May-history-union lattice; it requires every seed and identical active comparison-Loan heads. Full suites and five native fixtures/configuration are pending. Initial test-helper compilation/startup mistakes were corrected before claiming verification; final Debug build is clean.

**T4n-i DONE, 06:04:21 JST (20m48s):** clean builds and full suites PASS **7,338/configuration** before the final two test-only Borrow additions; all **18 conditional tests/configuration** then PASS against unchanged production code. Six fixtures/configuration PASS all **24 native O0/O2 executions**. Nested checking joins, branch-order symmetry, unavailable seeds, identical/unequal Loan stacks, rebind/reload and zero-allocation warm checks are covered. Seventeen net added cases; the former all-return conditional guard is now covered by a positive native/state test. Evidence: `i-full-*`, `i-loan-*`, `i-native-final.log`.

### Execution unit 23 — I6/I8 T4n-j scoped terminal conditional joins

**IN_PROGRESS, 06:04:21 JST (20m48s).** Extend the scope continuation proof to closed terminal if/else selections whose conditions complete and whose branches all terminate. Reuse T4n-i's checking joins; keep partially terminating branches and deferred cleanup guarded. Verify caller/default state, branch-local checking assignments, required rejection for missing initialization/Move on either path, native divergence and ordinary return behavior, nested scopes, reload/warm reuse, and full Debug/Release suites. This extends the existing §14.10.3 implementation; no specification change is needed.

T4n-j checkpoint, 06:07 JST (~23m27s): IMPLEMENTED_UNVERIFIED. Nine of twelve baseline cases failed for missing outer continuation; all 307 focused cases now PASS. The scope proof admits closed terminal if/else trees and reuses the explicit checking joins. Supplied-default declaration checking, both branch paths, caller initialization/history, nested scope effects and warmed reload checks pass. Full suites and three new fixtures/configuration are pending. Recompilation exposed two extra final newlines and a parameter-formatting warning in the preceding unit's final test addition; these were corrected, and final clean builds are required before DONE.

**T4n-j DONE, 06:08:59 JST (25m26s):** final Debug/Release builds are clean and full suites PASS **7,352 each**, zero failures/skips. Twelve new cases, three fixtures/configuration and all **12 native O0/O2 executions** PASS. Nested scopes, default declaration/caller checking, Move/init/history diagnostics and warmed reload checks PASS. Evidence: `j-final-build-*`, `j-full-*`, `j-native-*`. T4n-h–j are DONE; broader M2/M3 remain incomplete.

### Execution unit 24 — I6/I8 T4n-k completing selections before scope termination

**IN_PROGRESS, 06:08:59 JST (25m26s).** Admit normally completing if branches within a scope's continuation proof only when every contained operation completes and no transfer/deferred/loop path can escape that selection. Use the existing runtime/checking branch join before the later terminal operation; retain guards for partial termination. Verify both branch orders, initialization/Move history and static aggregate paths, native effects and missing successors, default-local branches, rebind/reload/warm reuse and full Debug/Release.

T4n-k checkpoint, 06:12 JST (~28m27s): IMPLEMENTED_UNVERIFIED. Twelve of fifteen baseline cases reproduced the lost continuation; all 322 focused cases PASS. Normally completing selections are admitted only while a nested proof forbids any terminal child or transfer, preventing partial-return paths from being silently dropped. Static tuple Move paths and repair, both initialization branch orders, caller/default state and warmed reload pass. Debug build is clean; full suites and five native fixtures/configuration are pending.

**T4n-k DONE, 06:12:55 JST (29m22s):** clean Debug/Release builds and full suites PASS **7,367 each**, zero failures/skips. Fifteen new cases and five fixtures/configuration PASS (**20 native O0/O2 executions**). Static aggregate paths, required partial-termination guards, default-local branches and warmed reload checks PASS. Evidence: `k-build-*`, `k-full-*`, `k-native.log`.

### Execution unit 25 — I6/I8 T4n-l divergence with scalar loop-local effects

**IN_PROGRESS, 06:12:55 JST (29m22s).** Extend the no-enclosing-state-change divergence proof to scalar/Unit loop-local declarations and mutation, scalar reads/arithmetic and contained control flow. Outer writes, owned acquisitions, calls and deferred cleanup remain excluded. A loop with no normal completion may seed its successor checking from the loop head only after this proof; runtime edges/results remain unchanged. Verify outer initialization/history, guarded outer mutations, default execution, nested local control flow, ordinary O0/O2 nontermination, rebind/reload and warm reuse, plus full Debug/Release.

T4n-l checkpoint, 06:18 JST (~34m27s): IMPLEMENTED_UNVERIFIED. Nine of fourteen original baseline cases reproduced the gap; all 336 initial focused cases PASS. The retained proof accepts only scalar/Unit loop-local effects and contained transfers; calls, owned values, outer writes and deferred effects remain excluded. A former unavailable-seed test used a now-proven local-only loop and has been replaced with an outer-mutating loop to preserve its guard responsibility. Six further cases cover contained exits, nested warm control flow, outward-transfer guards and invalid local use. Final full suites and six native fixtures/configuration are pending.

**T4n-l DONE, 06:19:03 JST (35m30s):** clean Debug/Release builds and full suites PASS **7,387 each**, zero failures/skips. Twenty new cases and six fixtures/configuration PASS (**24 native O0/O2 executions**). Nested proof reuse, contained transfers, ordinary invalid local-use diagnostics, caller/default facts and warmed reload checks PASS. Evidence: `l-final-build-*`, `l-full-*`, `l-native-*`.

### Execution unit 26 — I6/I8 T4n-m noncompleting conditional operands

**IN_PROGRESS, 06:19:03 JST (35m30s).** Address a noncompleting if condition: source branches still require ordinary checking and their joined state must continue after the selection, without a condition value or runtime branch/result. Reuse the existing separate checking join for normally completing source branch tails as well as transfer tails. Verify ownership/history across hypothetical branches, default conditions, absent results, Borrow release, reload/warm state, full Debug/Release and native O0/O2. Scoped proof expansion is separate unless required by the bounded cases.

T4n-m checkpoint, 06:23 JST (~39m27s): IMPLEMENTED_UNVERIFIED. All thirteen baseline cases failed for the intended missing condition continuation. The first 355 focused cases pass; five additional cases now cover absent else, else-if predecessor state and required rejection of a partial-return source branch. Branch checking seeds include only proven completing tails or terminal continuations, and the implicit false path is preserved. Full suites and eight native fixtures/configuration are pending; the Debug production build is clean.

T4n-m verification checkpoint: both clean full suites PASS 7,405/configuration. An early Release native attempt had no fixtures because the managed run had not yet generated that class; it is NOT completion evidence. Both final configurations are now present and are being replayed serially in `m-native-final.log`, superseding the premature attempt and overlapping initial native outputs.

**T4n-m DONE, 06:25:07 JST (41m34s):** clean Debug/Release builds and full suites PASS **7,405 each**, zero failures/skips. Eighteen new cases; final serial native replay PASS **56 O0/O2 executions** (32 new-condition executions plus 24 conditional-join regressions matched by the prefix). Borrow/state/default/implicit-path and zero-allocation reload checks PASS. Evidence: `m-final-build-*`, `m-full-*`, `m-native-final.log`.

### Execution unit 27 — I6/I8 T4n-n scoped noncompleting conditions

**IN_PROGRESS, 06:25:07 JST (41m34s).** Reuse the now-verified condition checking join through do wrappers, checking each condition and each source branch with its appropriate completing/terminal proof. Preserve missing-else paths and partial-termination guards. Verify scoped initialization/Move/Borrow behavior, scalar-default divergence, reload/warm reuse, full Debug/Release and native O0/O2.

T4n-n checkpoint, 06:28 JST (~44m27s): IMPLEMENTED_UNVERIFIED. All eight new scoped cases failed on the baseline; all 368 focused cases now PASS. The retained scope proof checks conditions and independently chooses completing/terminal branch proof, including missing else. Scoped result absence, caller/default state, Borrow release and warmed reload PASS. Full suites and four native fixtures/configuration are pending; Debug build is clean. Elapsed time was checked at 06:27:26 (43m53s) before broad verification.

**T4n-n DONE, 06:29:04 JST (45m31s):** clean Debug/Release builds and full suites PASS **7,413 each**, zero failures/skips. Eight new cases and four fixtures/configuration PASS (**16 native O0/O2 executions**). Scoped default/Borrow/result-state checking and warmed reload PASS. Evidence: `n-build-*`, `n-full-*`, `n-native-*`.

### Execution unit 28 — I6/I8 T4n-o noncompleting while conditions

**IN_PROGRESS, 06:29:04 JST (45m31s).** CLI `while-condition.kimi` currently rejects the initialized read at line 4:9 with UnsupportedOwnership_Kd. Preserve the state after a noncompleting condition when a body-local scalar proof excludes enclosing effects; never seed from before condition acquisition. Body source still requires ordinary checking. Verify condition Moves/Borrow release, body-local errors, default divergence and scoped/reload reuse; finish full Debug/Release plus native evidence and final replay within the soft time rule. General outer-mutating while bodies remain guarded.

T4n-o checkpoint, 06:32 JST (~48m27s): IMPLEMENTED_UNVERIFIED. Ten of thirteen baseline cases reproduced missing continuation; all 381 focused cases PASS. The continuation uses state after condition acquisition and cleanup, never the while head. Body-local proof is shared with local loops; outer body writes/calls remain guarded. Scope wrappers use the same proof. Debug build is clean. Final full suites, five new native fixtures/configuration, full Default/Never native replay and ordinary Milestone 1 checks are in progress. No further implementation unit is planned before this execution's soft deadline.

Final verification checkpoint, 06:34:17 JST (50m44s): both final builds are clean and full suites PASS **7,426/configuration**. Four positive CLI checks pass (original scope and while reproducer in each configuration); six negative checks reject uninitialized/Move cases with precise UninitializedPlace_Kd/MovedPlace_Kd. Two next-scope checks retain UnsupportedOwnership_Kd at 8:15. Serial replay covers all 182 Default plus 126 Never fixture files (616 O0/O2 executions expected); it is still running and is not yet PASS. Milestone 1 is next. No NativeAOT or draft edits.

**T4n-o DONE, 06:39:27 JST (55m54s):** final clean Debug/Release builds and full suites PASS **7,426 each**, zero failures/skips. Thirteen new cases and five new native fixtures/configuration pass. Final serial replay passes all **364 Default + 252 Never = 616 ordinary native O0/O2 executions**. Milestone 1 passes **25/configuration**, with reports `bin/milestone1/Debug/81cd458228a34cf6b733bcc5ab58d0b0/verification.json` and `bin/milestone1/Release/5ff41039b4dc486c8209c1767c56802f/verification.json`. Both reports target the final compiler binaries. Warm/reload and exact CLI diagnostics pass. Zero changed source files are newer than either configuration's verified binaries.

### Final checkpoint for the 05:43:33 execution

Final close recorded **2026-09-17T06:41:28+09:00**, elapsed **57m55s**, before the 06:43:33 soft deadline. All current units and required verification are complete. The next scope-wide tracking unit is not started in the remaining two-minute window. The final 1,675-entry manifest was verified with zero mismatches; native smoke report hashes match the final compiler binaries. The plan remains open for the next execution.

T4n-h–o are DONE: eight coherent units, 124 net added cases, full Debug/Release, native O0/O2, Milestone 1, rebind/reload and zero-allocation warmed verification PASS. The source and tests were not edited after final verification began. STATUS.md summarizes current support and limitations; SPEC.md already defines the required rules and needed no edits. No draft file was changed, no NativeAOT test was run, and no commit was created. No new out-of-scope finding or user decision is required. The premature native attempt recorded above was superseded by a complete serial replay.

The compiler-completion plan and M2/M3 remain IN_PROGRESS. **Exact next action: T4n-p**, begin with `bin/plan-execution/20260917-054333/next-partial-scope.kimi`. Both current CLI configurations report UnsupportedOwnership_Kd at line 8:15; a correct join must preserve the Move on the early-return branch and diagnose MovedPlace_Kd. Add an explicit checking join for every terminal path of the scope, including a partial branch followed by later termination; never substitute the scope entry or add runtime predecessors. Verify the positive initialized-state counterpart, both branch orders, missing initialization, Move/let history, cleanup/Borrow boundaries, reload/warm reuse, and full/native behavior. This requires scope-wide terminal-path tracking and is not started in the short remaining window.

Final evidence is under `bin/plan-execution/20260917-054333/final-*`. `final-manifest.txt` binds the final source/tests/docs, compiler/test binaries, all Default/Never fixture metadata, verification logs and native smoke reports. Re-read current repository state and compare this manifest before resuming; historical checks are not proof about later changes.

### Execution unit 29 — I6/I8 T4n-p partial scoped terminal joins

**DONE at 09:09:00 JST (first verified at 08:53, revised and reverified below); execution started 2026-09-17 08:30:38 JST, soft deadline 09:30:38 JST.** HEAD `fb3ebe5`, clean tree; the 05:43 evidence root is gone, so the reproducer was rebuilt as `bin/plan-execution/20260917-083038/next-partial-scope.kimi` (Stop declaration, `do` with `if c` moving `s` then `return`, later `stop()`, then `writeLine(s)` at 8:15). Baseline with rebuilt Debug binaries: `baseline-next-partial-scope.log` reports UnsupportedOwnership_Kd at 8:15; `baseline-partial-scope-positive.log` (both paths initialize `x`) reports UnsupportedOwnership_Kd at 10:13; `baseline-terminal-branch-partial.log` (function-level terminal if/else whose first branch contains `if d` moving `s` and returning before `stop()`) exits 0, so the dead `writeLine(s)` at 10:15 missed the possibly-moved diagnostic. All three share one root cause: a completing selection discarded the continuation seed of a terminal branch, and terminal selection joins used only each branch's final seed.

Change (`OwnershipAnalysis.cs`, `OwnershipAnalysis.ScopedChecking.cs`): the analysis keeps a reused per-function `terminalSeeds` list. `Conditional` records, for a completing selection, the continuation seed of every branch/else body that cannot complete normally (`RecordTerminalSeed`); for a noncompleting selection it records each branch's verified seed as before. `JoinChecking(source, mark)` now joins every seed recorded since the caller's mark: it still requires every path to be available and every active comparison-Loan head to agree, uses the existing single-seed region form for one path, and copies multiple seeds into `CheckingSeeds` with an immediate entry Branch so a nested continuation reads the join. `ScopedBody` takes a mark at entry; a noncompleting scope whose proof passes appends its own continuation and joins the recorded paths, otherwise it releases them; a completing scope leaves its pending paths to the enclosing join. The scope proof no longer forbids termination inside completing selections (every terminal branch is now recorded), so the `IfKoto` special case and `VisitBranchBody` were deleted; loops, `while` with completing conditions, `match`/`for`/`require`/deferred blocks and `and`/`or` remain refused. No runtime edge, result or cleanup is added; single-path regions keep the previous operation shape. Net source change is 62 insertions and 75 deletions.

Tests: new `PartialScopeContinuationTest` (27 cases): both branch orders for Move/uninitialized/let-history diagnostics, nested partial branches inside terminal and completing branches and inside a completing nested `do`, positive initialized/Moved-before/Borrow counterparts with native `done` output, later termination Abort with no checking successor executed, omitted/supplied partial scalar defaults, function-level terminal selections with nested partial paths, retained guards (deferred cleanup, completing `loop` with an inner return, unequal comparison-Loan heads), and reload plus zero-allocation warm reuse. `ScopedContinuationTest.MultipleTerminalPathsAndCleanupRetainTheirGuard` became `DivergentCleanupRetainsItsGuard` (the partial-scope case is now supported and covered positively); `CompletingScopeContinuationTest.PartialTerminationStillRequiresAnOuterJoin` became `PartialTerminationJoinsEveryPath`, asserting verified analysis for the same three shapes. A first default-declaration case used `stop()` inside the default and was rejected as an effectful default (existing I4 boundary, not this unit); it now diverges by `loop => continue` on both paths, and its dead read sits after the inner scope because dead source inside the scope after the second loop correctly sees only that loop's state.

Evidence (`bin/plan-execution/20260917-083038/`): after the change, `after-next-partial-scope.log` reports MovedPlace_Kd at 8:15, `after-terminal-branch-partial.log` reports MovedPlace_Kd at 10:15, and `after-partial-scope-positive.log` exits 0. Focused Debug suites (Continuation/UnreachableOwnership/ScalarDefault/DefaultCompletion/Ownership filters) PASS 740/740 (`focused-debug-5.log`). Clean Debug and Release builds have zero warnings/errors. Full suites PASS **7,452/configuration**, zero failures/skips (`full-debug.log`, `full-release.log`; 26 net added cases). New fixtures `NeverPartialScope{Debug,Release}*` (eight per configuration) PASS **16 ordinary native O0/O2 executions per configuration** (`native-debug-partial.log`, `native-release-partial.log`). Serial broader replay PASS: **284 `Never*` and 406 `*Default*` ordinary native O0/O2 executions** (`native-never-all.log`, `native-default-all.log`; the four `NeverPartialScope*Default*` fixtures match both patterns). Milestone 1 PASS **25 checks/configuration**: `bin/milestone1/Debug/f8276af0d6fc48c9aacc864c6b053987/verification.json` and `bin/milestone1/Release/b9a4a24c520c4742acbf45c024c6f122/verification.json` (`milestone1-debug.log`, `milestone1-release.log`). Both compiler builds postdate the analysis source edits and both test assemblies postdate the test edits. STATUS.md summarizes the new support and remaining limits; SPEC.md already states the rules and needed no edit. No draft file was changed, no NativeAOT test was run, and no commit was created.

**Revision (T4n-q attempt and T4n-p correction, 08:53–09:09:00 JST).** A bounded T4n-q slice (recording a completing loop body's terminal continuation and walking completing loops as ordinary proof children) was implemented and its focused analysis cases passed, but the native fixture `loop\n    if c\n        x = 1\n        return\n    exit\nx = 2\nstop()` reported a spurious UninitializedPlace_Kd at the dead `let y = x` (`loop-fixture.kimi`, `focused-debug-q1.log`): the state at the loop-internal `exit` (x uninitialized) was joined as if it left the scope, although at runtime it continues through `x = 2`. The same over-approximation applied to T4n-p's first form: a partial branch whose terminal transfer is labeled (`exit`/`continue`/`yield` to a loop or selection inside the joining construct) must not be joined as a terminal path; `labeled-partial-terminal-selection.kimi` (function-level terminal if/else whose first branch holds `loop\n    if d => exit\n    x = 1\n    exit` before `x = 2`/`stop()`) would have rejected a valid program in dead source. The T4n-q edits were reverted and T4n-p was made conservative: `OwnershipCheckingRegion` gained `Labeled`, set by `Jump` for every transfer other than a direct function return; `Block` reports whether its terminal region is labeled; `RecordTerminalSeed` records a labeled partial path as unknown (`-2`) instead of a seed; `JoinChecking(source, mark, labeled)` omits unknown paths from a selection's join (exactly the pre-T4n-p behavior for those joins) and refuses a scope join that contains one, and propagates `Labeled` so an enclosing completing selection treats such a branch as unknown. Loops that pass their own divergence/local proof release the seeds recorded inside them, because those proofs already exclude function-terminal paths (this restored `LocalLoopContinuationTest.EmitsLoopsWhoseEffectsStayLocal(Nested)`, which failed once in `focused-debug-fix1.log`). Tests: `LabeledPartialPathsNeverJoinAsTerminal` (two accepted programs: loop-internal `exit` and `exit to inner` partial paths inside a terminal if/else) and a scope guard for a labeled partial path (`loop\n    do\n        if c => exit\n        writeLine(s)\n        stop()\n    writeLine(s)`) were added to `PartialScopeContinuationTest`; the completing-loop guard case was restored. Final CLI evidence (`final-*.log`): partial scope MovedPlace_Kd 8:15, terminal branch MovedPlace_Kd 10:15, positive scope exit 0, labeled partial selection exit 0, completing loop and loop fixture UnsupportedOwnership_Kd (guarded, no spurious error).

Final verification after the revision: focused Debug suites PASS **743/743** (`focused-debug-fix3.log`); clean Debug/Release builds; full suites PASS **7,455/configuration**, zero failures/skips (`final-full-debug.log`, `final-full-release.log`); `NeverPartialScope*` fixtures PASS **16 native O0/O2 executions per configuration** (`final-native-debug-partial.log`, `final-native-release-partial.log`); broader replay PASS **284 `Never*` and 406 `*Default*` executions** (`final-native-never-all.log`, `final-native-default-all.log`); Milestone 1 PASS **25/configuration** (`bin/milestone1/Debug/64187af0f669403f80df9d887ec5926d/verification.json`, `bin/milestone1/Release/994fe9bb29794b7e87bc974d83757ef0/verification.json`). Net source change: 7 files, about 134 insertions and 107 deletions including tests.

**Exact next action: T4n-q with per-path target extents.** `next-completing-loop.kimi` and `loop-fixture.kimi` remain guarded. A correct completing-loop join needs each recorded terminal seed to carry the extent it leaves (function, or the loop/selection it targets) and each join at construct K to include only seeds whose target is outside K, dropping loop-internal ones; multi-seed join regions must keep their seeds individually available for enclosing joins rather than a single merged target, or refuse mixed-target propagation. Only then may `ScopedCheckingProof` accept completing loops. Deferred cleanup, unequal Loan heads and `and`/`or` conditions remain guarded.

### Final checkpoint for the 08:30:38 execution

Final close recorded **2026-09-17T09:10:00+09:00**, elapsed about **39m30s**, before the 09:30:38 soft deadline. T4n-p is DONE with all required verification on the final source; no implementation unit is in progress or unverified. The T4n-q per-path-extent redesign is not a small unit and was not started in the remaining window. The stale `NeverPartialScopeDebugWhile.*` fixture from the reverted slice was deleted before the final Debug partial and `Never*` replays. No draft file was changed, no NativeAOT test was run, and no commit was created; all changes remain uncommitted for the user to review.

### Execution unit 30 — I6/I8 T4n-q completing loops and transfer extents

**DONE, 2026-09-17 09:42 JST.** Resumed from clean HEAD `97bebe1`. Evidence: `bin/plan-execution/20260917-092418/`. The unchanged Release compiler reproduced UnsupportedOwnership_Kd for unit 29's `next-completing-loop.kimi` (10:15) and `loop-fixture.kimi` (12:13); the updated Debug compiler reports the required MovedPlace_Kd for the former and accepts the latter. Logs: `baseline-*` and `after-*`. Initial `dotnet test` probes encountered the sandboxed NuGet config and then selected zero cases with incompatible MTP switches; neither is test evidence. All subsequent tests use the direct xUnit DLL runner with nonzero counts.

Implementation: replace the reused integer seed list and opaque `Labeled` flag with value-type `(Seed, Target)` continuations and region target metadata. Each completing loop/while body records its terminal continuation; loops, scopes and selections remove transfers caught within their own extent before propagating pending paths. The scope proof visits completing loops through its existing child traversal. Never calls, jumps and proved divergence share `BeginChecking`, preserving the original target when another transfer appears in already-dead source. This preserves existing dead-source assignments/defaults instead of treating their later written exit as a new runtime path. No execution edge, result or cleanup is synthesized. Lists and visitors retain their storage; the reload/warmed analysis plus IR-emission test measures zero allocated bytes.

Mixed-target joins remain valid for local checking, but `MixedTargets` explicitly refuses propagation of their merged state into another enclosing join. This implements unit 29's allowed conservative alternative; it does not claim general per-target replay of effects after a mixed join. Other existing cleanup/Loan/short-circuit/divergence guards remain.

Tests: `CompletingLoopContinuationTest` adds **32 cases**, covering early-return Move/uninitialized/let histories, loop and while, internal exit/continue/yield, nested and outer targets, same-target joins, dead transfers, function-terminal selections, Borrow ending, omitted/supplied scalar defaults, mixed-target guards, serialization/reload and zero warmed allocation. Two former partial-scope guard cases now check successful completing-loop analysis and the correct Move diagnostic. Focused Debug checks PASS **639**; full Debug and Release checks PASS **7,487 each**, zero failures/skips; builds have zero warnings/errors. Sixteen new fixtures/configuration PASS **32 O0/O2 native executions/configuration**. Broader replay PASS **348 Never + 414 Default executions** (patterns overlap for default/Never fixtures); Milestone 1 PASS **25/configuration**, reports at `bin/milestone1/Debug/684f853e8e3945e6b5fdd842d32a9632/verification.json` and `bin/milestone1/Release/599310bce5fd4088846e314e000fa532/verification.json`. Native execution initially encountered sandbox tool permissions and then passed under authorized escalation. Source/artifact and fixture hashes are recorded in `source-artifact-hashes.json` and `fixture-hashes.json`. SPEC.md and §14.10.3 already define the required behavior and need no change; no draft edits, NativeAOT tests or commit.

**Exact next action:** mixed-target propagation. `next-mixed-target.kimi` and `next-mixed-target.log` in this unit's evidence root reproduce UnsupportedOwnership_Kd at 12:13 for a scope containing `loop\n    if c\n        x = 1\n        return\n    else => exit`, followed by `x = 2; stop()` and an outer dead read of `x`; the correct result is acceptance. The terminal selection's local join is valid, but an outer loop must retain only the return path while handling the exit normally. Keep constituent targets and any subsequent checking-only effects separate until the enclosing extent has selected its escaping paths; do not assign one target to a merged state or drop an unavailable path. Deferred cleanup, unequal Loan heads, short-circuit conditions and effectful divergent loops remain separate unfinished obligations. M2/M3 remain IN_PROGRESS; this unit has no unverified implementation work.

### Execution unit 31 — I6/I8 T4n-r unchanged mixed-target continuations

**DONE, 2026-09-17 14:58 JST (about 14 minutes elapsed).**
The baseline Debug build was clean and 62 existing loop/partial-scope tests passed.
The current-source CLI reproduced unit 30's UnsupportedOwnership_Kd at 12:13.
Checking seeds now retain both operation and transfer target. A mixed region may
export its original constituent seeds only while its current tail is exactly the
join entry; enclosing constructs filter their own handled targets before joining.
The local common-state join and runtime CFG are unchanged. Subsequent checking
effects still produce an unavailable continuation rather than a guessed target or
discarded state. This is a deliberately bounded slice of unit 30's next action.

Sixteen new tests cover nested selection/loop/scope/yield extents, branch order,
both execution choices, Move/init/let histories, local dead-source checking and
reload/warmed zero-allocation analysis/emission. The old empty-tail guard case is
now positive coverage; its guard slot uses a subsequent assignment instead.
Focused Debug PASS 78; final Debug/Release builds have zero warnings/errors and
both full suites PASS 7,583 each, zero failures/skips. LLVM/native O0/O2 regression
PASS 380 Never + 414 Default executions (overlapping families), including 32 new
executions. Milestone 1/8 PASS 25/46 checks per configuration. Evidence root:
`bin/plan-execution/20260917-144405/`. SPEC §14.10.3 already defines the
behavior; no language change or draft edit is needed. A possible next bounded
slice is an identity continuation through a subsequent bare transfer:
`next-bare-transfer.kimi` still reproduces UnsupportedOwnership_Kd at 13:13.
Preserve original targets and pre-cleanup seeds; never apply a later dead transfer's
target to every incoming path. Additional checking effects remain separate work.

### Execution unit 32 — I6/I8 T4n-s bare transfers after mixed joins

**DONE, 15:03 JST (about 19 minutes elapsed).** Retain the
constituent seed range when a bare transfer preserves the mixed join's exact
pre-cleanup seed. Capture its region before lowering implicit cleanup: the first
focused run exposed three chain/reuse failures because cleanup itself gave the
previously empty region an entry before provenance was inspected. The corrected
path captures the existing value-type region once, without allocating or changing
runtime edges. Ordinary unavailable/effectful continuations remain guarded.
Eleven new cases cover return/exit/continue and consecutive transfers, both native
choices, and invalid initialization/Move/let histories. Reload/warmed allocation
coverage now includes consecutive transfers. Final Debug/Release builds have zero
warnings/errors; full suites PASS 7,594 each with no failures/skips. Sixteen new
fixtures across both configurations PASS 32 LLVM/O0/O2 native executions. Every
previous Never/Default IR and expectation hash matches final regeneration, retaining
unit 31's native evidence without unnecessary repeat execution. Together these
two units add 27 cases and 64 new native executions. No NativeAOT test, draft edit,
specification change or commit was made; prior uncommitted work is preserved.
Evidence uses `s-*` files in `bin/plan-execution/20260917-144405/`.

Next action: `next-mixed-effect.kimi` adds `x = 3` after the mixed terminal selection
and reproduces UnsupportedOwnership_Kd at 13:13. It requires per-target application
of later checking effects; a merged state cannot be filtered correctly afterward.
Do not remove the guard without that representation and its negative/Loan/cleanup
tests. No part of this next implementation was started under the remaining budget.

<a id="old-roadmap"></a>

## Original execution order and resumption instructions (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

## 10. Execution Order and Next Actions

| Step | Preconditions | Changes and rationale | Verification | Completion criteria | Failure or blocking conditions |
| --- | --- | --- | --- | --- | --- |
| 1 | Later implementation instruction; fresh HEAD/diff | M1/I1–I2 baseline and clause mapping | V1–V3, selected V4–V5 | Current evidence and known failures recorded | G5 environment, missing tools/artifacts; retain block evidence |
| 2 | M1 and selected clause reproducers | M2 then M3 semantic/ownership contracts | T1–T9/T27, V3–V5 | Checked plans usable by downstream work | Unresolved proofs or real SPEC_GAP block dependent slice |
| 3 | Checked storage/effect contracts | M4, then M5 concrete complete representations | T8/T10–T12, V3–V6 | Layout/acquisition/destruction verified together | No placeholder zero/undef or unsupported-body omission |
| 4 | Definition-side proof and concrete layout baseline | M6/I13–I15 baseline generic sharing, then I16 bounded optimization | T13–T15, V3–V6/V9 | Explicit selection/entry/frame invariants hold at all budgets | Required growth limit produces explicit resource diagnostic |
| 5 | Generic/call contracts available | M7 callables and M8 views/iteration; independent slices may be developed sequentially in either order | T16–T18, V3–V5/V9 | Program 7 and focused operations verified | Borrow escape or missing witness cannot use special-case fallback |
| 6 | General values/borrows/cleanup | M9 collections/strings; M10 objects after callable/Option support | T19–T23, V3–V6/V9 | API outcomes, effects, cost and lifecycle contracts | Undefined public extensions remain excluded |
| 7 | Checked layout/ABI; package syntax can start earlier | M11 foreign operations; M12 packages/common generation/reuse | T24/T27–T29, V3–V9 | Source closure and native connection independently validated | Corruption/stale proof blocks publication, not hidden by cache |
| 8 | Product semantics fixed | I30 test semantics/discovery; I31 only after G2; I32 external host only after G1 | T25–T26/T30, V3/V8/V10 | Settled contracts verified, public interface blocks resolved explicitly | G1/G2 keep affected items BLOCKED |
| 9 | Relevant features ready | Unchanged programs 8–9 and full M15 clause/interaction audit, docs/performance | V2–V12 | Section 11 satisfied, residual exclusions accurate | Historical results cannot substitute for missing evidence |

Resumption instructions:

1. Read Sections 1–2 and 5, then inspect actual HEAD/diff. Preserve any later user changes. Update baseline/time and invalidate only evidence whose inputs changed.
2. Choose the first dependency-ready I item; mark IN_PROGRESS and record targeted R/T/V IDs before coding. If support already exists, verify and close that item with evidence rather than implement it again.
3. Confirm relevant owning clauses and exact existing tests/helpers; reduce each observed gap to a positive/negative reproducer at the correct stage. Extend stable T IDs with case suffixes.
4. Implement the smallest coherent vertical slice. Keep unfinished paths rejected before generation. After source changes, run focused checks, then the milestone's native/performance interactions; broaden only according to impact.
5. Use IMPLEMENTED_UNVERIFIED until all required evidence exists. Record command/result/artifact identity immediately; update STATUS only for verified product support. Reopen DONE when a dependency change invalidates evidence.
6. Carry unresolved decisions forward with effect and resolution condition. Do not silently delete superseded items or turn an implementation limit into a language restriction.

<a id="plan-changes"></a>

## Original plan changes and out-of-scope findings (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

## 12. Plan Changes and Out-of-Scope Findings

| Date | Change / disposition | Rationale |
| --- | --- | --- |
| 2026-09-17 | Timed continuation from 05:43:33 completed T4n-h–o at unchanged HEAD, preserving all prior work | Eight units add 124 net tests; final suites pass 7,426 each, native replay passes 616 O0/O2 executions, and Milestone 1 passes 25/configuration. Scoped/conditional/loop checking expands under explicit proofs; broader M2/M3 and partial-scope joins remain open. No specification weakening, draft edits or NativeAOT. |
| 2026-09-17 | Third timed continuation completed T4g–T4m at unchanged HEAD `826f6077`, preserving all earlier changes | Seven units add 91 tests; final suites pass 7,207 each, default native replay passes all 79 fixtures at O0/O2 per configuration. Broader M2/M3 remain incomplete; next T4n addresses checking after noncompleting local initializers. No specification weakening, draft edits or NativeAOT. |
| 2026-09-17 | Second timed continuation completed T4a–T4f at unchanged HEAD `826f6077`, preserving prior uncommitted changes | Six default-plan/flow/scalar-execution units, 66 new tests, final Debug/Release and O0/O2 evidence. M2/M3 remain incomplete; next T4g checks forbidden operations on prepared slots at declaration time. No specification weakening, draft edits or NativeAOT. |
| 2026-09-17 | Timed execution at HEAD `826f6077` completed T1a, T5a/G9, T27a, T27b and T7a, with fresh failing reproducers and final Debug/Release verification | Five focused changes close modifier diagnostics, missing-conformance cascades, stale analysis, source-error lifetime and pending-effect diagnostic classification. 63 new tests and three strengthened cases; no specification weakening or draft edits. I4 pending-slot audit records the next implementation boundary. |
| 2026-09-17 | Created PLAN.md at the user-designated path; implementation remains TODO | Full finalized-spec completion requires a maintained execution record, not a single-feature patch. |
| 2026-09-17 | Reused existing struct/borrow/CFG/Project graph work rather than planning replacements | Inspected current code is newer than some STATUS summaries; program milestone numbers are distinct from M IDs. |
| 2026-09-17 | Retained object/Weak and collection contracts in scope; separated undefined host/test interfaces | Owning specifications settle former features while explicitly deferring the latter interfaces. |
| 2026-09-17 | Recorded help-triggered NuGet access failure and existing runner help separately from tests | No current conformance test run occurred; planning did not justify restore/build or native artifact mutation. |
| 2026-09-17 | Corrected representative optional-parameter syntax, verified actual dependency-example commands, and assigned generic enum integration to M6 | Final review prevented an unintended earlier error in T4 and a circular M5/M6 completion dependency. No baseline requirement was removed. |
| 2026-09-17 | Implementation started: I1 completed as a verification-only checkpoint; G5 closed as an environment artifact; evidence archived under `bin/plan-baseline/m1-8c49edb/` | Fresh restore/build/test at HEAD `8c49edb` succeeded with no configuration error, so no NuGet or source change was justified. Historical 6,986 figure is now current evidence for this HEAD only. |
| 2026-09-17 | I2 recorded as a clause coverage map in Section 4 and M1 closed | Mapping used executed per-class counts and keyword scans rather than reading every test body; a class PASS is evidence only for what it asserts, so uncovered clauses stay assigned to their T/I items instead of being marked covered. |
| 2026-09-17 | M2 started; I3 audit recorded; T3a implemented (merged containers keep the first fragment's CodeContext) with a regression test; G9 opened and assigned to I5 as T5a; STATUS gained a dated entry | Appendix A.1 requires retaining fragment contexts after merging; the parent's source-less context produced document-less diagnostics. The sibling cascade is a separate semantic decision, so it was recorded rather than patched in the same unit. |
| 2026-09-17 | Observed a concurrent user addition `draft/Design/2026-09-17 Documentation Comments.md` (untracked) | Preserved unread and unedited per the draft exclusion; not part of this execution's scope or evidence. |
| 2026-09-17 | T4n-p (unit 29): function-terminal partial-path seeds are recorded per function and joined by the enclosing selection or scope; labeled partial transfers are recorded as unknown (omitted from selection joins, guarding scope joins); the scope proof's completing-selection restriction was removed; a T4n-q completing-loop slice was reverted after producing a spurious error; the previous execution's evidence root was found deleted and the reproducer rebuilt | A partial early transfer followed by later termination is a terminal path of the scope; discarding it substituted a single path for the required join, and the same omission let function-level terminal selections miss dead-source diagnostics. Recording every terminal branch makes selections ordinary children of the proof, which deletes code instead of adding a second proof. |
| 2026-09-17 | Execution stopped at 01:38 JST (about 35 of the 60 budgeted minutes) with T1a recorded as the next I3 item rather than started | Modifier recognition across every declaration form, a new catalog diagnostic, tests and two full-suite verifications were estimated to exceed the remaining window, and a partially implemented modifier grammar would leave the tree incoherent; the workspace holds only the verified T3a change. |

Out-of-scope findings: general LSP/editor integration, KimiCode configuration, debug information, additional target profiles, remote registries/network publication and NuGet distribution automation are not necessary to implement the finalized compiler contracts here. Existing CI improvements may be extended only as needed for conformance evidence. Public APIs omitted by the specification belong in separate design work. No unrelated fix, draft edit, or package publication is authorized by this plan. The implementation execution changes and evidence are recorded above. The original extended front-end benchmark input failure remains an out-of-scope finding (unit 2); its common workload is usable.

<a id="status-record-2"></a>

## STATUS record: Partial-terminal ownership continuations (2026-09-18)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Partial-terminal ownership continuations (2026-09-18): mixed-target selections
retain separate normal branch tails and pending terminal histories, including
missing else, nested branches and later effects. Proven partial-terminal logical
RHS expressions preserve evaluated/skipped paths with either a completing or
noncompleting left operand. Noncompleting conditional conditions retain checking
branch histories, including partial-terminal branch bodies; an earlier normal
branch remains the only normal arrival when a later condition terminates.
Initialization, Move, immutable assignment and stored Loan diagnostics retain
each path's effects. Normal tails include local
cleanup; terminal histories retain original targets and pre-cleanup state.
These checking joins introduce no runtime arrivals or missing-operand results.
Ordinary and single-target joins also restore their original region, fixing a
crash after a later noncompleting condition. Closed normal loop CFGs retain their
existing replay path, and selection-local yield arrivals are not discarded.
Completing scopes with partial-terminal branches retain their normal tails after
local cleanup. Scope-caught transfers join their post-cleanup normal arrivals;
their later dead source retains separate histories. This also supports scoped
logical operands containing caught and escaping transfers. Transfers after a
noncompleting condition do not create normal arrivals.
Proven if selections also retain selection-local yield arrivals, including
nested selection/scope targets and scalar results. Final native/integration
verification of this latest extension is recorded in PLAN.md.

Compiler-owned replace/exchange/swap signatures no longer emit erroneous
missing-source-body diagnostics; ordinary source functions still require bodies.
The latest clean Debug/Release builds pass 8,507 managed tests each. Final native
verification passes all 861 fixtures at O0/O2 (1,722 executions), including 122
new fixtures. Programs 1–11 pass all 984 integration checks across both
configurations. PLAN.md records the matching source and artifact identities.
Reload/warmed analysis and IR writing allocate zero bytes in the measured
workloads. General iteration transfer replay, unequal Loan joins, deferred
cleanup and general effectful divergence remain incomplete. PLAN.md records native/integration
evidence, the exact next reproducer and the remaining compiler scope.

<a id="status-record-3"></a>

## STATUS record: Declaration Container nesting adoption (2026-09-18)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Declaration Container nesting adoption (2026-09-18): the 2026-09-17 change is
integrated into SPEC.md, Chapters 3, 6–15 and 18–22, and Appendices A, D and F.
Placement and inherited environments are defined in §6.1; bound paths and access
in §9.6.1; bound Contract collisions in §8.4.9; static storage in §22.2.4.
Examples and cross-references replace the earlier restrictions. The draft is
unchanged. Specification adoption is complete; implementation is partial.

Implemented and tested:

- Recursive group/struct/enum/Contract declarations in groups and structs,
  merged fragments, nearest Self, access boundaries and inherited-name conflicts.
  Enum, Contract, executable and conditional implementation bodies reject nested
  containers. rootgroup remains restricted to source roots.
- Shared declaration trees and interned references retain unused outer Type,
  Semantics and explicit Origin bindings. Own arity is unchanged. Substitution
  identifies slots by their original binder, including through groups and base
  Types. Child variance/Loan summaries do not mutate the parent's analysis state.
- Generic outer paths, intermediate parenthesized Origin mappings, effective
  trailing mappings, construction and enum Cases. Rebinding an Origin, supplying
  a grouped Type as a value, and inferring missing outer arguments are rejected.
  No new tokens or reserved keywords are required.
- Opening and named aliases retain fully bound environments, use root lookup,
  deduplicate identical references and diagnose distinct-binding ambiguity.
- Bound marker/function Contracts use distinct references, substitute outer
  arguments in requirements, and reject duplicate or possibly colliding direct
  conformances. Generic conformance lookup and conditional conformance under
  inherited Type parameters retain the selected bindings and premises.
- Binding validates enclosing input conditions, concrete-argument access and
  retained borrow dependencies. Static Fields in inherited environments require
  the stored value to prove Owned under the declaration's premises.
- Flow analysis treats declaration Constraints as non-executable. Common generic
  storage planning retains inherited slots for nested functions and constructors;
  LLVM emits supported nested construction, Cases, static functions and bound
  alias calls. Serialization/rebinding and 64-level nesting have regression tests.

Remaining implementation work: bound Contracts with associated requirements or
refinement ancestors are rejected rather than reusing declaration-only evidence.
Full bound requirement identity, conditional refinement-path merging, inherited
conformance composition and declaration-path cycle checks remain incomplete.
The existing abstract Origin-bound proof limit also applies here. Origin-erased
mutable static-storage keys, uniform initializer/destructor certificates, shared
initialize-and-address operations and their lazy lifetime protocol are specified
but not implemented. General generic aggregate forwarding and some borrowed
generation paths retain existing lowering limits. Cache/artifact tests do not yet
establish every outer-edit invalidation requirement in Appendix A.20.

[ContainerNesting](examples/ContainerNesting/README.md) executes nested Types,
an inherited generic group function, a bound named alias and a static Contract
implementation. Milestone 8 now places its groups inside Toolkit; the specification
tour also illustrates a generic family's nested Types, Contract and helpers.
These programs do not imply coverage of the remaining requirements above.

Validation: Debug and Release each pass all 8,305 managed tests, including 68
ContainerNestingTest cases, with no build warnings or errors. The five new
executable fixtures pass 10 native executions across O0/O2. Milestone 8 passes
46 Release integration checks. Parser regressions cover all 57 example and
Milestone Kimi files. Local links and anchors pass in the 28 specification
Markdown files and three updated example/Milestone guides; git diff --check
passes. NativeAOT tests were not run.

<a id="status-record-4"></a>

## STATUS record: Whole-value replacement adoption (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Whole-value replacement adoption (2026-09-17): the change is integrated into
SPEC.md and its Type, constraint, expression, ownership, destruction, artifact,
and generation chapters. Section 15.7 owns the three APIs and shared storage
update dependencies; assignment refers to that section instead of repeating
the rules. The original draft is unchanged by this implementation work.

Implemented:

- Kimi.Sealed has an intrinsic Identity and four-valued proof. Classification
  checks the normalized outer Core, rejecting open structs, Never, and non-owner
  Semantics without recursively imposing constraints on fields. User conformance
  cannot manufacture evidence. Declared evidence validates generic object target
  formation and exact-Type explicit payload projection.
- Kimi.replace/exchange/swap are ordinary parsed generic declarations with
  validated compiler implementation identities. Named aliases, source shadowing,
  generic inference, external argument names, and textual evaluation order use
  the existing Binding pipeline. The catalog now validates 10 of 22 entries;
  catalog validation is separate from runtime coverage.
- Binding distinguishes explicit Sealed payload projection, complete payload
  receivers, and protected base projection. Fully specified handle/reference
  storage borrows retain their own Type layer. Custom accessor selection retains
  the same distinction without claiming completed accessor execution support.
- Ownership and LLVM lowering execute updates on complete mutable owner locals,
  including scalars, strings, and supported aggregates. Concrete scalar/struct/
  fixed-array uniq paths also have checked address/update plans when their
  contents are proven Owned. Target Loans begin before later arguments; ordinary
  writes/transfer plans retain original-location destruction and old-value
  responsibility. Exchange/swap transfers perform no user destruction.
- Returning an exchanged aggregate no longer mistakes the function's result slot
  for an input-Origin anchor. Scalar exclusive borrows use ordinary pointer
  storage. Replace does not reserve unnecessary old-value scratch storage.
- The existing tokenizer/parser syntax is sufficient: Sealed and update names
  remain identifiers, and typed @ adaptation and named-argument syntax are reused.
  Serialization/rebinding, invalid library edits, constraints, overlap, incomplete
  targets, argument order, aliases, destruction and Abort have regression coverage.

Implementation boundaries remain explicit: object allocation, object-borrow
runtime plans and payload owner retention are not yet available, so payload
projection binds but cannot pass executable ownership/lowering. General
ObjectCallCompatible effect inference/publication and generated accessor execution
also remain incomplete. Storage-polymorphic generic updates, general field/index
targets, nested reference/handle storage updates, and borrowed updates with
unproven Owned contents need additional plans. Owned is an implementation limit
on that borrowed lowering path, not a constraint on the specified Kimi APIs.
Unsupported paths are rejected; no no-op or approximate payload implementation is
emitted. The object tour and Milestone 14 show the normative behavior and remain
specification examples beyond executable coverage.

The executable WholeValueReplacement example covers ordinary updates and borrowed
struct replacement. Milestone 14 now uses an ordinary uniq projection of its
Sealed payload, exchanges contents without replacing the object, and records the
old payload's extra destruction in its expected output.

Validation: Debug and Release each pass all 8,237 managed tests with no build
warnings or errors. The WholeValue fixtures pass 30 native executions across O0
and O2, and Milestone 5 passes 47 Release integration checks. Parser regressions
cover all 56 example and Milestone Kimi files. The 28 specification Markdown
files have no unresolved local links or anchors; git diff --check also passes.
NativeAOT tests were not run. These checks cover the implemented paths above,
not the remaining object runtime and generic execution requirements.

<a id="status-record-5"></a>

## STATUS record: Kimi library and named aliases (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Kimi library and named aliases (2026-09-17): the adopted change is integrated into
SPEC.md, Chapters 3, 9, 18, 20 and 22, their library references, and Appendices A,
E and F. Core remains the Type component. Container nesting was adopted in the
later entry above. The existing Chapter 22 file path remains stable for draft links.

The compiler exposes its designated library as Kimi and places the existing output
function in its ordinary public Console group. KimiLibrary and KimiDeclaration
replace the internal library names. Analysis, lowering and emission retain the
original function Identity and ownership/runtime behavior. There is no reserved
Core reference or old output forwarding name.

Every defining module receives the mandatory Kimi default alias, together with
configured additions at one lookup stage. Saved ProjectFile additions remain
unchanged; redundant Kimi additions have the same effective settings as none.
Explicit aliases retain their earlier stage. Named aliases resolve root-based
Kotonoha/group references, retain source-document scope and original Identity,
reject unused conflicting mappings, and warn about distinct earlier root
qualifiers. Opening aliases retain direct-member behavior. Both forms reject
invalid placement, targets and unbound Container parameters. Generated sources
and dependencies retain their own environments; re-Bind invalidates cached
targets. Per-document name/member indexes and per-generation target caches avoid
repeated global scans; warmed alias Binding allocates zero bytes.

Examples, all fourteen Milestone sources, and their output test fixtures use the
new library paths. The NamedAliases example demonstrates named group/library
qualifiers and the mandatory default alias. Both Debug and Release pass all
8,170 managed tests (63 added), with zero build warnings/errors. Syntax checks
cover all 55 example/Milestone files. Milestone 1–11 pass 492 Release integration
checks, including LLVM verification and native O0/O2 execution; all eleven
reports match the final compiler and source hashes. The named-alias output fixture
passes both O0/O2 runs with exact UTF-8, LF, empty stderr and exit 0. Specification
file/heading links and git diff whitespace checks pass. Evidence is retained in
`bin/kimi-alias-verification.json` and the `bin/kimi-alias-*.log` files.

Existing implementation limits remain: the library catalog still lacks twelve
declarations; general function-item erasure is Binding-only; source-package
packing, content IDs and persistent semantic reuse are incomplete. The new
specification defines their alias/default-environment requirements without
claiming those broader facilities are implemented. Instantiated parent groups
require the separately unadopted Container placement rules. NativeAOT is excluded.

<a id="status-record-6"></a>

## STATUS record: Earlier whole-value replacement proposal review (2026-09-17), preceding the

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Earlier whole-value replacement proposal review (2026-09-17), preceding the
implementation entry above: reorganized the
[proposal](draft/Changes/2026-09-17%20Whole%20Value%20Replacement.md) in Japanese,
aligned ObjectCallCompatible and Kimi API names, and distinguished complete
payload calls from protected base-receiver calls and ObjectViewCompatible.
Consolidated dependency/destruction rules and added evaluation-order, rejected-use,
and payload-versus-handle swap examples. That earlier review was documentation-only.

<a id="status-record-7"></a>

## STATUS record: Mixed-target ownership continuations (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Mixed-target ownership continuations (2026-09-17): straight-line and closed CFG checking-only
effects now preserve each original transfer target and pre-cleanup state through
enclosing joins. Assignments, Moves, supported calls and transfer chains retain
ordinary diagnostics without creating runtime arrivals or cleanup. Stored-borrow
liveness follows checking seed and replay links, including later uses across an
outer join. Completing conditional branches retain per-target common guarantees.
Closed cycles preserve zero-iteration paths and converge per constituent.
Short-circuit operands retain terminal paths and evaluated/skipped checking joins,
preserving initialization, Move/assignment histories and stored Loans without
inventing runtime arrivals or results after a noncompleting left operand.
Require failure continuations retain pre-cleanup state and transfer targets for
enclosing joins, including mixed-target prefixes, without feeding failure facts
into require's ordinary success successor.
Scalar defaults support require with validated scalar bodies and contained transfers.
Closed terminal if/else branches after mixed-target joins now retain independent
per-target prefixes. Continuation capture freezes its seed range before later
lexical cleanup can change the region entry.
Mixed-target logical expressions with a terminal RHS preserve its separate history;
the skipped path alone supplies the ordinary logical successor. A noncompleting
left operand retains the evaluated/skipped checking join without a runtime result.
Debug/Release suites pass 8,107 tests each; 136 new continuation fixtures pass O0/O2,
and all inputs of the 477 previously verified native fixtures still match.
Programs 1–11 pass all 984 integration checks across Debug/Release, with all
22 reports matching the final compiler and program identities.
Reload/warmed zero-allocation checks pass. Partial-terminal branches, logical
operands and caught scope/selection continuations were extended in the 2026-09-18
entry above. Unequal Loan joins, deferred cleanup and general effectful divergence
remain incomplete. Detailed
execution evidence and remaining compiler work are maintained in PLAN.md.

<a id="status-record-8"></a>

## STATUS record: ObjectCallCompatible implementation plan (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

ObjectCallCompatible implementation plan (2026-09-17): [SPEC.md](spec/appendices/D-deferred-features.md#objectcallcompatible)
records three stages: document the plan, implement inference/public summaries and
call checks, then detect guarantee loss across Package releases. The current stage
is documentation only; stages 2 and 3 are deferred pending further instructions.
Existing language rules and implementation coverage are unchanged.

<a id="status-record-9"></a>

## STATUS record: Object compatibility terminology (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Object compatibility terminology (2026-09-17): the specification now names the
call-operation guarantee ObjectCallCompatible and the runtime Contract View
eligibility predicate ObjectViewCompatible(C). Definitions and specification
references are aligned. This is a terminology-only documentation update; no
Attributes, compatibility rules, extension boundaries, or compiler behavior change.
ObjectCallCompatible body/callee verification remains incomplete, and runtime
Contract designation and View binding syntax remain deferred.

<a id="status-record-10"></a>

## STATUS record: Milestone 11 integration (2026-09-17, complete)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Milestone 11 integration (2026-09-17, complete): the unchanged
program passes Binding, ownership, LLVM verification, native O0/O2 and CLI execution
with stdout `Generic weights are 6, 3, 2.\n`, empty stderr and exit 0. Both builds
have zero warnings/errors; both full managed suites pass 7,881 tests (37 added).
Target integration passes 55 checks per configuration, including ten rejected
inputs. Four Debug/Release O0/O2 IR artifacts independently confirm three shared
bodies, nine entries, six forwarding adapters and one selected specialization.
Closed Type specializations are checked against their original input,
result, argument-name and Origin contracts and are excluded from overload choice.
Shared generic calls carry selected entry adapters without cloning the checked
definition. Indexed shared borrows require no Copy on T and retain bounds checks;
shared i32 addition checks overflow. Verified immutable group integer/bool literal
initializers can be folded because their initialization has no observable effect.
Effectful/mutable static storage, length/receiver/constrained specializations and
explicit specialization Origins remain unsupported. Dependent owned-result
forwarding still rejects generation. Related LLVM/native regression passes
**506 fixtures / 1,012 O0/O2 runs**, with **2,530 matching hashes**. Programs 1–11
pass **984 integration checks**; all 22 reports match current compiler/program
hashes. Target work and required verification are complete. Evidence is in
`bin/milestone11-work-20260917/verification.json` and `shared-generation.json`.
NativeAOT was not run as instructed; no later program was implemented. This
execution did not edit Milestone sources, SPEC.md or draft; independent specification
terminology changes observed during verification were preserved.

<a id="status-record-11"></a>

## STATUS record: Milestone 9 integration (2026-09-17, complete)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Milestone 9 integration (2026-09-17, complete): the unchanged generic search
program passes current-source Debug/Release builds, LLVM verification and ordinary
native O0/O2 execution. Exact stdout is
`Found 6 at index 3.\nMissing value handled.\nSearch finished.\nBatch destroyed.\n`,
with empty stderr and exit 0. Dedicated integration passes **59 checks per compiler
configuration**, including first/last/singleton/absent matches, empty/nonempty
exhaustion, i64 instantiation, Abort without cleanup and eight rejected inputs.

Finite symbolic enum ownership and shared tag/payload construction/results are
implemented. Shared range iteration retains scalar SSA snapshots and checked isize
addition; indexed reads use evaluated index values. Common-function calls keep
receiver Loans and acquired argument storage, with concrete entry adapters for the
callback ABI. A generic destructor that never observes receiver fields uses one
checked representation-independent body; instantiated field destruction follows.
Shared adapters currently accept direct scalar ABI parameters and bool/isize/Unit
results. Generic destructors that observe fields remain explicitly unsupported.
These implementation limits do not narrow language semantics.

Both builds have zero warnings/errors. Both full managed suites pass **7,844 tests**
(32 added). Related LLVM/native regression passes **492 fixtures / 984 O0/O2
executions**, with **2,460 matching fixture/expectation hashes**. Programs 1–10 pass
**874 integration checks**; all twenty reports match current compiler/source hashes.
Evidence is retained in `bin/milestone9-complete-20260917/verification.json` and
PLAN.md's P9 completion record. No target work or required verification remains.
NativeAOT was not run. SPEC.md, draft and all program sources are unchanged;
programs 11–14 were not implemented.

<a id="status-record-12"></a>

## STATUS record: Previous Milestone 9 continuation (2026-09-17, incomplete target)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Previous Milestone 9 continuation (2026-09-17, incomplete target): checked common-function
invocation and direct anonymous-function conversion now support scalar/Unit snapshot
captures in the inline 8-byte environment. Owned function handles move and clean
up normally; calls hold a shared Loan through argument evaluation. Borrowed struct
fields can be returned with declared Origins, including symbolic `ref/T` fields
through shared offset metadata. Borrowed generic array reads require Copy and use
shared element-size/length policies, retaining bounds checks for zero-sized elements.
Both Debug/Release builds have zero warnings/errors, and both full managed suites
pass 7,812 tests (68 added). Related LLVM/native regression passes 471 fixtures /
942 unique O0/O2 runs, with 2,355 matching fixture/expectation hashes. Completed
programs 1–8 and 10 pass 756 Debug/Release integration checks; all eighteen reports
match current compiler/program hashes. Evaluation order, Never-body creation,
nonzero generic field offsets and explicit oversized-environment rejection are covered.
The unchanged target passes final Binding and the `Batch.view()`/generic element
ownership paths, but still fails ownership on `Hit<T>` result/construction. Target
LLVM generation and native execution remain UNVERIFIED. Shared enum results,
iteration/callback adaptation and generic destructor generation remain unfinished.
No target completion is claimed; NativeAOT was not run. Evidence and the exact
next action are in current PLAN.md and `bin/milestone9-final-20260917/`.

<a id="status-record-13"></a>

## STATUS record: Milestone 11 static-member example (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Milestone 11 static-member example (2026-09-17): Added the immutable i32 group Property `Weights.defaultWeight = 1` and used it in the generic default weight implementation. The expected output remains `Generic weights are 6, 3, 2.` README explains inherent group static membership, the single shared slot, and first-access initialization semantics. This is a specification example update only; no compiler capability checks, builds, or tests were performed. Language rules and SPEC.md are unchanged.

<a id="status-record-14"></a>

## STATUS record: Milestone 10 integration (2026-09-17, complete)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Milestone 10 integration (2026-09-17, complete): owned nested tuple/enum
patterns now bind distinct guard candidates and body locals. Scalar/Unit nested
candidate reads and owned tuple decomposition pass ownership analysis. Concrete
enum storage uses an explicit i32 tag and aligned active payload; construction,
whole-value transfer and active-case destruction have LLVM/O0/O2 evidence.
Composite pattern generation currently supports layouts without destruction,
retaining short-circuit tests and post-guard acquisition. Copy aggregate fixed-array
iteration reaches generation. The unchanged program passes LLVM verification,
ordinary native O0/O2 execution and CLI runs in Debug and Release. Exact stdout is
`Accepted total is 12.\nPattern run finished.\n`, with empty stderr and exit 0.
Dedicated integration passes 54 checks per configuration, including nine rejected
invalid inputs. Final Debug/Release builds have zero warnings/errors; both managed
suites pass 7,744 tests (44 added in this execution). Related native regression
passes 444 fixtures / 888 O0/O2 executions, with 2,220 matching IR/expectation
hashes. Completed programs 1–8 pass 648 integration checks; all eighteen program
reports match current compiler/source hashes. Evidence is retained in
`bin/milestone10-work-20260917/verification.json`. No target work or required
verification remains. NativeAOT was not run as instructed. See PLAN.md's separate
P10 items; program 9 was incomplete at that checkpoint and programs 11–14 were not implemented.
Composite matches with owned destruction or borrowed candidate paths remain
explicitly unsupported outside the completed target.

<a id="status-record-15"></a>

## STATUS record: Milestone 9 continuation (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Milestone 9 continuation (2026-09-17): function calls now retain distinct length
and Type substitutions, infer fixed-array lengths from established inputs and
candidate-fitted literals, and substitute checked length expressions. Signature
formation conditions filter candidates; function bodies cannot introduce unproved
length requirements. Length-dependent whole-array Copy/Move and destruction use
the existing shared CFG with concrete policies. Owned and explicitly declared
shared borrowed arrays expose logical length through that policy, including
empty arrays and zero-sized elements. Borrowed entry arguments retain pointer
values in their own slots. Reanalysis, reload, invalid-plan rejection and warm
length-call Binding/ownership allocation checks pass. Shared module construction
is not claimed allocation-free. Dual-namespace explicit arguments across overloads
with different generic slot kinds remain explicitly unsupported.
At the program-9 checkpoint, both managed suites passed 7,700 tests (73 new in that continuation), with clean
Debug/Release solution builds. Type-only and length-only namespace lookup also
preserves transparent argument grouping. Related LLVM and O0/O2 native regression
passes 143 fixtures / 286 executions; all 715 regenerated IR/expectation hashes
match the verified inputs. Completed Milestones 1–8 pass 648 Debug/Release
integration checks, with all reports matching final compiler/source hashes.
Evidence is retained in `bin/milestone9-resume-20260917/verification.json`.
The execution stopped after approximately 66 minutes, completing only the already
running verification/documentation after its 60-minute soft limit. Complete
At that earlier checkpoint, Milestone 9 execution remained unverified; the next unit was checked common-function
invocation at `accepts(value)`, followed by captures and the remaining generic
read, borrowed-field, enum-result and iteration paths.

<a id="status-record-16"></a>

## STATUS record: Program Milestone 9 integration (2026-09-17, incomplete)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 9 integration (2026-09-17, incomplete): explicit typed value
borrows now derive omitted outer Origins from their operands. Borrowed concrete
fixed arrays support indices/length snapshots and checked scalar reads, including
nonnull storage for empty arrays, reference forwarding/results and exclusive
reference inspection without Move. Child reborrows suspend parent access.
At that earlier checkpoint, the unchanged target failed final Binding at callback invocation and lacked
capture/common-function integration. Its ownership, LLVM/native build, output,
exit status and cleanup are **not verified**. See PLAN.md's separate program-9
checkpoint and `bin/milestone9-work/` for reproduction and subset evidence.
This is an implemented foundation, not completion of Milestone 9 or the broader
plan stages. Specification semantics and milestone sources are unchanged.
The previous execution's verification counts are retained in PLAN.md; these
program-9 figures are historical, superseded by program-10 verification above.
NativeAOT was not run.
These results establish the implemented subset only.

<a id="status-record-17"></a>

## STATUS record: Program Milestone 8 integration (2026-09-17, complete)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 8 integration (2026-09-17, complete): the unchanged program
passes final Binding, universal ownership checking, LLVM verification/linking and
ordinary native O0/O2 execution. Exact stdout is
`Chosen item.\nBoxed array total is 12.\nGeneric scope finished.\n`, with empty
stderr and exit 0. Generic
constructors use existing member substitution; unknown-Copy field acquisition
retains conditional Move paths and rejects invalid definition-side reuse/deinit
extraction. Shared storage CFGs use concrete ABI entries and immutable layout,
Copy and destruction policies, deduplicated by symbolic type. Context slots are
8 bytes, fixed scratch excludes external/empty storage, and live flags are emitted
only for conditional destruction. Scalar Copy fixed-array iteration snapshots its
input once. Final Debug/Release builds have zero warnings/errors; both full suites
pass 7,567 tests each, including 40 new cases. Programs 1–8 pass 648 integration
checks across both configurations, including 46 program-8 checks each and thirteen
invalid-input cases. Related LLVM/O0/O2 regression passes 570 fixtures and 1,140
native executions; regenerated final fixtures match the saved native hashes.
Final integration reports match current compiler/source hashes. See PLAN.md's
separate program-8 checkpoint and `bin/milestone8-work/verification.json`.
No required check remains unverified; NativeAOT tests were not run. Language
semantics, draft and milestone source programs are unchanged. Generic forwarding,
compound symbolic fields, lengths, specialization, borrowed generic ABI and
general Iterable/Iterator support remain guarded outside this checkpoint.

<a id="status-record-18"></a>

## STATUS record: Specification programs 10–14 (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Specification programs 10–14 (2026-09-17): Added [Milestone10–14](milestones/README.md#milestone-10-patterns-inside-result-producing-control-flow) combining nested enum/Tuple patterns and transfers, type/length-generic forwarding and explicit specialization, mutable/nested/consuming captures, a generic Slice-backed Iterator, and a callback pipeline with Kimi.makeObj and scoped object borrowing. README records expected outputs, rejection/Abort exercises, and the distinction between semantic output and evidence of physical generic code sharing. These additions are specification targets only; no compiler capability checks, builds, execution, or tests (including NativeAOT) were performed. Language rules are unchanged; SPEC.md links to the expanded series.

<a id="status-record-19"></a>

## STATUS record: Ownership checking continuations (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Ownership checking continuations (2026-09-17): noncompleting do scopes,
if conditions and terminal branches preserve source initialization, Move history
and Borrow state without adding runtime successors or fabricated results. Separate
checking joins intersect initialization guarantees and union possible histories;
all predecessor states must be available and active comparison-Loan stacks must
agree. Ordinary completing branches may feed a later scope termination, and every
function-terminal branch of a completing selection (a partial early return, Never
call or divergent loop followed by later termination) is recorded and joined by the
enclosing scope or terminal selection, so its Move/initialization history reaches
the later dead source. Completing loop/while bodies now retain their escaping
terminal paths. Each pending checking seed carries its transfer target; exits,
continues and yields handled inside a construct are removed before its state
reaches an enclosing join. Transfers in dead source preserve the original path's
target, including through later Never calls and divergence. Joins with different
targets support local checking. An unchanged mixed join retains its constituent
seeds and targets for enclosing constructs to filter independently. Bare dead
return/exit/continue chains retain that provenance before implicit cleanup;
subsequent checking effects still guard propagation. No runtime edge or result
arrival is added. Final builds are clean and all 7,594 managed tests pass in each
configuration, including 27 new cases and zero warmed allocation checks. Native
verification includes 380 Never + 414 Default executions, then 32 new bare-transfer
executions; prior fixture hashes match final regeneration. Milestone 1/8 passes
25/46 checks per configuration at unit 31 (see PLAN.md units 31–32).

Noncompleting loops with only scalar/Unit loop-local effects preserve enclosing
facts. A noncompleting while condition preserves the state after its argument
acquisitions when the body has only local scalar effects. These paths work in
supported scalar defaults and through do wrappers. Every default declaration still
requires independent checking, including when its argument is supplied.

Current limits include outer-mutating or owned/effectful divergent loop bodies,
general iteration transfer replay, deferred-cleanup joins, unequal active
Loan joins, recursive-default
completion proofs and general owned/borrowed defaults. They remain guarded; M2/M3 are incomplete. PLAN.md units
21–30 record the verified slices and the next resumption case.

The earlier continuation checkpoint had clean Debug/Release builds and 7,455 passing tests each. All default
and noncompletion fixtures per configuration pass ordinary native O0/O2, including
the eight new partial-scope fixtures per configuration. Milestone 1 passes 25 checks
per configuration. Rebind/reload and warmed analysis/emission checks pass with zero
measured allocation. No NativeAOT testing was performed.

<a id="status-record-20"></a>

## STATUS record: Scalar operand arrival (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Scalar operand arrival (2026-09-17): unary/binary operations produce no result
when an operand supplies none. Later source operands still receive ordinary
checking, including assignment effects. This applies to typed nested calls and
admitted scalar default expressions. Full Debug/Release suites pass 7,294 each,
two fixtures/configuration pass O0/O2, and warmed result-state/reload checks pass.
Direct Never operand fitting still has earlier Binding limitations.

<a id="status-record-21"></a>

## STATUS record: Omitted-default completion (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Omitted-default completion (2026-09-17): selected defaults now contribute their
completion to caller flow without changing the declared result Type or callee-body
completion. Cached declaration checks work before or after the declaration and
include cleanup that prevents delivery. Structural completion includes omitted
defaults; recursive expansion remains pending and bounded. Full Debug/Release
suites pass 7,282 each with clean builds, five fixtures/configuration pass O0/O2,
and warmed flow reanalysis measures zero allocation.

<a id="status-record-22"></a>

## STATUS record: Incomplete call acquisitions (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Incomplete call acquisitions (2026-09-17): a Never explicit/default argument
prevents caller result initialization, including nested scalar, string and aggregate
calls. Checking-only argument effects and Borrow release remain available. Stored
and borrowed result validators require proof before accepting absent normal results.
Full Debug/Release suites pass 7,267 each; four new fixtures/configuration and five
existing stored-result/nonreturning fixtures pass O0/O2. Nested-result rebind,
reload and zero-allocation warm checks pass.

<a id="status-record-23"></a>

## STATUS record: Default initialization checking (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Default initialization checking (2026-09-17): every supported scalar/Unit default
is checked independently from initialized preceding prepared parameters, including
unused, fully supplied and bodyless requirement declarations. Scalar/Unit locals
may omit an initializer or use a Never initializer; ordinary ownership checks reject
reads without a supplied value. A reusable scratch CFG shares the ordinary solver
without adding executable callee bodies or destroying prepared arguments. Full
Debug/Release suites pass 7,253 each, all 86 default fixtures per configuration pass
O0/O2, and warmed valid/invalid declaration checks allocate zero measured bytes.
General owned/effectful defaults and divergent continuation joins remain unfinished.

<a id="status-record-24"></a>

## STATUS record: State-neutral loop continuations (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

State-neutral loop continuations (2026-09-17): source after `loop => ()` or a
loop containing only a bare continue to itself uses the unchanged loop-entry
ownership facts. A Never initializer supplies no initialized value. Twelve new
cases pass; full Debug/Release suites pass 7,235 each and four fixtures per
configuration pass bounded O0/O2 execution. Result-state, reload and zero-allocation
warm checks pass. Effectful/branching divergent loops remain unsupported where a
checking continuation is needed.

<a id="status-record-25"></a>

## STATUS record: Nonreturning-call continuations (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Nonreturning-call continuations (2026-09-17): ordinary Never calls now preserve
acquired-argument initialization and Move facts for later source checking. Their
temporary argument Loans end in the checking graph; runtime execution gains no
successor, result initialization or cleanup. Sixteen new tests pass, full
Debug/Release suites pass 7,223 each, eight native fixtures per configuration pass
O0/O2, and warmed analysis/emission measures zero allocation. General divergent
loop/selection continuations and declaration-side default initialization checking
remain unfinished.

<a id="status-record-26"></a>

## STATUS record: Final continuation verification (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Final continuation verification (2026-09-17): seven completed default-expression
units add 91 tests. Debug/Release builds are clean, all 7,207 tests pass in each
configuration, all 79 default fixtures pass O0/O2 per configuration, and Milestone 1
passes 25 checks each. The detailed limits and next action are recorded in PLAN.md;
this is a verified subset, not completion of the compiler plan.

<a id="status-record-27"></a>

## STATUS record: Prepared scalar subplace defaults (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Prepared scalar subplace defaults (2026-09-17): defaults can Copy scalar/Unit
values from owned fields, tuples and fixed arrays in preceding prepared arguments.
Reads retain the selected call's argument identity, finish their inspection Loan
before callee entry, and retain declaration bounds-Abort locations. Eleven new
cases pass; clean Debug/Release suites pass 7,207 each, native O0/O2 checks cover
all nine new fixtures, and warm element reads measure zero allocation. This run
adds 91 tests across seven units. Generic Copy proofs, escaping borrows/captures,
noncompleting local-initializer checking and general owned/effectful defaults remain
unfinished; the mixed nested tuple/array Binding case remains an earlier limit.

<a id="status-record-28"></a>

## STATUS record: Mutable scalar locals in defaults (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Mutable scalar locals in defaults (2026-09-17): initialized default-local var
bindings support assignment, numeric/bit updates, increments and finite while/loop
computation. Writes are restricted to locals inside the same default; prepared
parameters remain immutable. Twelve new cases pass; clean Debug/Release full
suites pass 7,196 each, nine fixtures pass O0/O2 per configuration, and warmed
analysis/emission still allocates zero measured bytes. Uninitialized/noncompleting
local-initializer checking, general effects and owned/borrowed defaults remain open.

<a id="status-record-29"></a>

## STATUS record: Unit defaults (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Unit defaults (2026-09-17): Unit literals, preceding Unit copies, local Unit
bindings and supported control-flow results now use the established zero-sized
argument path. No physical value is added for Unit. Eleven new tests pass; full
Debug/Release suites pass 7,184 each, ten fixtures pass O0/O2 per configuration,
and warmed Unit default analysis/emission measures zero allocation.

<a id="status-record-30"></a>

## STATUS record: Immutable scalar locals in defaults (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Immutable scalar locals in defaults (2026-09-17): sequential default bodies may
initialize scalar let bindings and read them through ordinary local storage.
Prepared arguments retain independent snapshots across local scopes and repeated
calls. Thirteen new cases pass; full Debug/Release suites pass 7,173 each and eight
fixtures pass O0/O2 per configuration. Warm analysis/emission with local bindings
allocates zero measured bytes. Mutable/uninitialized locals, noncompleting local
initializer checking continuations and effectful/owned defaults remain unsupported.

<a id="status-record-31"></a>

## STATUS record: Contained scalar default transfers (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Contained scalar default transfers (2026-09-17): single-expression default bodies
can use loop, internal exit/continue and selection yield with existing scalar
operations. Transfers stay inside the default. A noncompleting default skips later
defaults and the callee; checked arithmetic retains declaration Abort locations.
Eleven new cases pass; clean Debug/Release builds and full suites pass 7,160 each.
All eleven fixtures pass O0/O2 in both configurations. Warm compound default
ownership/emission remains allocation-free. General local/effectful/owned defaults
and their cleanup remain unfinished.

<a id="status-record-32"></a>

## STATUS record: Default Move diagnostics (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Default Move diagnostics (2026-09-17): definite non-Copy acquisition of a
preceding prepared argument now reports `DefaultArgumentMove_Kd` at declaration
time, including unused, required-initializer, fully supplied and bodyless
requirement defaults. Checks use committed call acquisitions and lexical Copy
proofs; legal Copy/shared inspection is preserved. Eighteen new tests and full
Debug/Release suites pass (7,134 each), and both CLI builds report the source error.
Owned field/tuple/array subplaces and aggregate/enum payload acquisitions are also
checked (15 more cases; full suites pass 7,149 each). Borrowed referents remain
outside the owned-path check. Mutation, escaping borrows and general default execution
remain unfinished; this diagnostic pass is not an execution certificate.

<a id="status-record-33"></a>

## STATUS record: Scalar do defaults and final verification (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Scalar do defaults and final verification (2026-09-17): a single-expression do
default can use the supported scalar operations, selections and conversions.
Nested/chained do results retain prepared argument snapshots. Jumps, loops,
effectful calls, generic and owned/borrowed defaults remain unsupported.
Rebind and source reload invalidate execution until analysis rebuilds the plans,
then reproduce identical IR. This continuation adds 66 tests in total; final
Debug/Release builds are clean and full suites pass 7,116 each without skips.
All 32 default fixtures pass native O0/O2 in each configuration; Milestone 1
passes 25 checks each. Warm ownership/emission with do, selection and conversion
defaults measures zero allocation. General default ownership/cleanup is unfinished.

<a id="status-record-34"></a>

## STATUS record: Scalar selections in defaults (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Scalar selections in defaults (2026-09-17): value-producing if/else defaults
support scalar conditions and single-expression arms, including nested selections
and reads of earlier default results. All arms require supported operations even
when unreachable or supplied. Six new cases and full Debug/Release suites pass
(7,110 each); five new fixtures pass native O0/O2 in both configurations.

<a id="status-record-35"></a>

## STATUS record: Scalar conversions in defaults (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Scalar conversions in defaults (2026-09-17): defaults now reuse established
identity, literal and numeric conversion plans. Each conversion preserves its
range check, rounding and source location, including chained conversions.
Nine new cases pass; full Debug/Release suites pass 7,104 cases each and all
nine new fixtures pass native O0/O2 in both configurations. The warmed default
ownership/emission test includes conversion chains and still allocates zero bytes.

<a id="status-record-36"></a>

## STATUS record: Scalar default execution (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Scalar default execution (2026-09-17): nongeneric calls acquire explicit arguments
first, then omitted scalar defaults in declaration order. Defaults can read
prepared preceding scalar values and use literals, arithmetic, bit operations,
comparisons, unary operators and short-circuit boolean expressions. Reads retain
argument snapshots across branches. Supplied defaults and required initializers
do not execute; noncompleting arguments skip defaults and the callee. Checked
arithmetic retains the declaration expression's Abort location. Other default
forms, generic defaults and default ownership/borrow effects remain unsupported.
Twenty-one new cases and full Debug/Release suites pass (7,095 each). Thirteen
default fixtures pass O0/O2 per configuration, 28 existing function fixtures
pass O0/O2 from Release, and Milestone 1 passes 25 checks per configuration.
Warmed default ownership/IR generation measures zero allocation.

<a id="status-record-37"></a>

## STATUS record: Default-expression control flow (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Default-expression control flow (2026-09-17): defaults are checked as independent
value expressions even on unused/bodyless declarations or fully supplied calls.
Their transfers may target constructs inside the default but cannot escape into
enclosing function or loop bodies. A noncompleting default does not change the
callee body's completion. Twelve new cases and full Debug/Release suites pass
(7,074 each), including allocation-free warmed control-flow reanalysis.

<a id="status-record-38"></a>

## STATUS record: Omitted-default call plans (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Omitted-default call plans (2026-09-17): selected calls now retain omitted
defaults in parameter declaration order after explicit arguments. Each entry
keeps its declaration expression and parameter Symbol together with the
substituted parameter Type/Origins. Named argument and receiver mappings remain
in source order; defaults supply no inference or overload-selection evidence.
Rebind clears or reuses plan storage, and reload rebuilds source identities.
Twelve new cases and full Debug/Release suites pass (7,062 each), including
zero allocation in warmed repeated Binding. The metadata itself does not certify
ownership, cleanup or execution; the scalar execution slice is recorded above.

<a id="status-record-39"></a>

## STATUS record: Projected-call diagnostic boundary (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Projected-call diagnostic boundary (2026-09-17): inherited borrowed-receiver
method calls whose effect verification is still unimplemented now report
`UnsupportedBinding_Kd`. Their receiver plans keep internal Unknown proof,
and emission remains rejected without overload reselection. This does not
implement public ObjectCallCompatible summaries. The three existing adaptation
cases were corrected and strengthened; 204 focused cases and full Debug/Release
suites (7,050 each) pass.

<a id="status-record-40"></a>

## STATUS record: Source validity and diagnostic lifetime (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Source validity and diagnostic lifetime (2026-09-17): both source parsing entry
points now retain newly reported syntax/lexing errors independently of the
diagnostic destination and later display clearing. Duplicate-location reports
still invalidate source; pre-existing Binding errors and warnings alone do not.
Seven new boundary tests and full Debug/Release suites pass (7,050 cases each).
Milestone 1 passes 25 LLVM/native/CLI checks in each configuration.

<a id="status-record-41"></a>

## STATUS record: Source-change invalidation (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Source-change invalidation (2026-09-17): appending source through `AddSource` or
`CodeContext.Parse`, and reloading a source snapshot (including an empty one),
now revokes prior Binding/startup/ownership results and retained conformance and
Property certificates. Emission writes nothing until the complete current
pipeline runs again. This closes stale-IR acceptance of newly invalid declarations
and empty replacement trees. Seven new tests and full Debug/Release suites pass
(7,043 cases each); the existing native Milestone 1 smoke test also passes in
both configurations. This covers in-memory source changes; persistent semantic
artifact reuse remains a separate implementation item.

<a id="status-record-42"></a>

## STATUS record: Unresolved-conformance diagnostics (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Unresolved-conformance diagnostics (2026-09-17): an isolated unresolved
`Self is Missing` clause now reports its missing Name without repeating derived
errors on the owning Type, a sibling Copy clause, or its Properties. Binding
failures and unverified certificates remain intact. Other constraint inputs,
independent errors, separate missing Names, and invalid uses remain diagnosed;
recorded causes are rebuilt on rebind. Nine new tests cover these boundaries,
fragments, reload and zero allocation in warmed repeated Binding. Full Debug
and Release suites pass 7,036 tests each with no failures or skips.

<a id="status-record-43"></a>

## STATUS record: Unavailable declaration modifiers (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Unavailable declaration modifiers (2026-09-17): `virtual`, `override`, and
`abstract` now produce `UnavailableFeature_Kd` at the first unavailable modifier
in a declaration header, including container/function/Property declarations,
constructors, deinit, Contract requirements, and inline or block accessors.
Recovery skips the invalid declaration or accessor and preserves following
independent items. Recognition stops at item boundaries; the same spellings
remain ordinary Names in declarations, calls, and member access. The 40 new
parser cases and full Debug/Release suites pass (7,027 tests each, zero failures
or skips). This implements the existing rule in SPEC §2.5.1; it introduces no
new valid modifier or reserved word.

<a id="status-record-44"></a>

## STATUS record: Merged-container diagnostic locations (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Merged-container diagnostic locations (2026-09-17): a declaration container
created while parsing a source document now retains that first declaring
fragment's CodeContext instead of the parent's source-less root context, so a
container-level Binding failure (for example an invalid conformance list) is
reported at the container header in its document (`file.kimi:1:1`) rather than
as a document-less `Project:@0` location. Members and later fragments keep their
own contexts as before. Verified by the new
`SourceDocumentAndDiagnosticTest.MergedContainerRetainsFirstFragmentSourceDocument`
and the full Debug/Release suites (6,987 tests each, zero failures); the
regenerated scalar fixtures are byte-identical to the pre-change set. The
former sibling-clause and field cascade after one unresolved conformance name
is resolved as described above. The location regression now uses contradictory
input premises to retain an independently required container-header diagnostic.

<a id="status-record-45"></a>

## STATUS record: Program Milestone 7 complete (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 7 complete (2026-09-17): the unchanged
`milestones/Milestone7.kimi` passes Binding, ownership, LLVM verification, linking
and ordinary Windows x64 native execution. Fixed arrays and full Slice views
provide independent `indices` snapshots; the built-in ResolvedRange iteration
path acquires its range once and supplies immutable isize bindings. Nested for
loops reuse the existing transfer and cleanup CFG. Full Slice handles preserve
backing Origins through Copy, check scalar reads against their stored length,
reject overlapping mutation/Move while needed, and permit statically disjoint
row writes. Slice lifetime checks include index evaluation and temporary-owner
expiry. No intermediate inner-array Copy or heap allocation for view storage is
introduced. Range/Slice handles have two-word physical layouts.

Exact stdout is `Row finished.\nRow finished.\nRow finished.\nMatrix total is 42.\nBorrowed row total is 20.\n`,
with empty stderr and exit 0. `test-milestone7.ps1` checks the original and
byte-identical renamed O0/O2 inputs through native execution and both CLI run
forms, alternative arithmetic, outer exit and exhaustion paths, matrix/Slice
bounds Abort with exact diagnostics and exit 1, and thirteen rejected inputs.
The checked-in milestone sources are unchanged.

Debug/Release solution builds pass with zero warnings/errors; both full managed
suites pass 7,527 tests each with no failures/skips. Forty added sequence
tests cover empty/nonempty/nested arrays, single receiver/index evaluation,
metadata and handle copies, NLL, lifetime and write/Move rejection, disjoint
storage, transfer cleanup, scalar representations, reanalysis, and malformed
generation plans. The milestone script passes all 52 checks per configuration;
completed Milestones 1–6 pass 226 checks per configuration. Native regression
passes LLVM verification and 1,106 O0/O2 runs across 19 Sequence, 466 Element,
18 Struct and 50 Reference fixtures. All 2,765 fixture/expectation files from the
final Release suite match the saved Debug/native input hashes. Logs and source/
fixture identities are retained under `bin/milestone7-work/`. Program-7 reports:
`bin/milestone7/Debug/dabe834f49cf46f1aea7b8b66c29c4c9/verification.json` and
`bin/milestone7/Release/602c33a1a4694ec0897c46e9377e418d/verification.json`.
No required verification remains blocked or unverified. No new throughput
benchmark was performed.

This is the program-7 subset, not completion of PLAN.md's M8/I19/I20. General
Kimi sequence declarations and explicit Type APIs, Index/from-end/bounded Range
operations, general user Iterable/Iterator dispatch, direct array/Slice iteration,
tuple iteration bindings and generic sequence ABI remain unimplemented. The
supported Slice reads are scalar Copy reads; other element-result forms remain
guarded. Mixed Slice provenance conservatively widens its static footprint.
Programs 8–9 were read for dependencies and not implemented. This task does not
change the language rules or drafts; NativeAOT is not run. Concurrent additions
of Milestones 10–14 and their SPEC/README index entries were preserved and were
not implemented or verified by this task.

<a id="status-record-46"></a>

## STATUS record: Program Milestone 6 complete (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 6 complete (2026-09-17): the unchanged
`milestones/Milestone6.kimi` passes Binding, ownership, LLVM verification, native
linking and execution with the existing compiler implementation. Value-producing
loop/if/do, guarded match, continue, yield, named exit and require already compose
correctly through result acquisition, cleanup and scalar SSA joins. No compiler
source change or new language feature was needed. The first attempted build
reached LLVM generation but could not run the sandboxed LLVM version probe;
granting local native-tool execution permission resolved that environment failure.

Exact stdout is `Selected 7.\nControl flow passed.\n`, stderr is empty, exit 0.
`test-milestone6.ps1` adds 63 CLI/native checks per configuration: the original
single-source input and renamed O0/O2 copies, alternate search threshold, guard
evaluation side effects, both selection paths, both validation failure paths,
transfer cleanup and Abort suppression of pending cleanup. Twelve invalid inputs
are rejected before IR/executable publication, including missing/wrong transfer
targets, incompatible or implicit Unit results, unreachable mismatched results,
continuing require failure, non-bool guard, missing match coverage and escaped
pattern binding. Debug/Release builds have zero warnings/errors; each full managed
suite passes 6,986 tests with no skips. The focused baseline passed 602 tests.
Both milestone script runs pass, with source/compiler/build identities retained at
`bin/milestone6/Debug/98a3947647c4494cb4666f78a2c01896/verification.json` and
`bin/milestone6/Release/d945c54bb6b340d798c484200e300889/verification.json`.

Related result, match and guard fixtures pass 40, 144 and 78 O0/O2 native checks,
respectively. Completed programs 1–5 pass their 25, 21, 37, 33 and 47 checks,
respectively, against both current-source compiler configurations. No blocker or
required unverified check remains. Program 6 is distinct from PLAN.md's broader
stages. All nine source programs were read for dependencies, but no later program
was implemented. Source programs, SPEC and drafts remain unchanged. NativeAOT
was not run.

<a id="status-record-47"></a>

## STATUS record: Program Milestone 5 complete (2026-09-17)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 5 complete (2026-09-17): the unchanged
`milestones/Milestone5.kimi` passes Binding, ownership, LLVM verification, native
linking and execution at O0/O2. Eligible structs receive synthesized construction;
explicit struct borrows, returned input Origins, constructor Origin inference,
borrowed method receivers and field reads/writes now reach native code. Borrow
dependencies survive calls, storage and Move. Deinit keeps the view's shared Loan
active until destruction; mutation and ownership transfer resume afterward.
Reborrow conflicts, temporary-owner escapes and unreachable-code violations are
rejected. Existing projection-aware string Loan checks remain in effect.

Expected stdout is exactly
`Borrowed sum is 55.\nView destroyed; counter is still 55.\nFinal value is 56.\nCounter destroyed.\nDone.\n`,
stderr is empty, exit 0. Debug/Release solution builds have zero warnings/errors;
both full managed suites pass 6,986 tests without skips, including 31 new tests.
Ten new fixtures pass 20 O0/O2 native checks; existing reference, string-guard and
element-borrow fixtures pass 100, 52 and 208 checks respectively. The milestone
script passes 47 checks per configuration, including renamed inputs, changed
counts, immediate temporary borrows, two Abort variants and fourteen rejected
inputs. Abort output confirms that defer/deinit are not run during termination.
Reports under `bin/milestone5/<configuration>/<run-id>/` retain exact output and
source/compiler/build identities. Final run IDs are Debug
`711f7e5100bb4995b356f84739f3efda` and Release
`2eaa26fcd8bf4c6dae5e390a9403549c`.
Milestones 1–4 also pass their 25, 21, 37 and 33 checks, respectively, against both
final compiler configurations. No required check remains unverified and no blocker remains.

General borrowed aggregate operations, accessors and generic/inherited structs
remain separate work. This checkpoint concerns program 5, independently of
PLAN.md's broader stages; earlier checkpoint entries below are historical.
No specification change was required. Concurrent specification programs 6–9 and
their documentation were preserved without implementing them. Drafts and NativeAOT
were untouched.

<a id="status-record-48"></a>

## STATUS record: Specification programs 6–9 (2026-09-16)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Specification programs 6–9 (2026-09-16): Added [Milestone6–9](milestones/README.md#milestone-6-value-producing-control-flow) covering value-producing loop/if/do and guarded match, nested fixed-array iteration and cross-loop cleanup, nested groups with generic ownership transfer, and length/type-generic search with Copy constraints, callbacks, enum results, and borrowed storage. README records expected outputs and optional rejection/Abort exercises. These are specification targets; no compiler capability checks, builds, execution, or tests (including NativeAOT) were performed for these additions. Language rules are unchanged; SPEC.md links to the program series.

<a id="status-record-49"></a>

## STATUS record: Program Milestone 4 complete (2026-09-16)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 4 complete (2026-09-16): the unchanged
`milestones/Milestone4.kimi` now passes Binding, ownership, LLVM verification,
native linking and execution. The first failure was outdated rejection of a
single-item deinit Body followed by unsupported constructor Binding. Explicit
constructors and their special field storage now check initialization and
completeness at each successful exit, before and after cleanup. Owned struct
parameters/results reuse aggregate slots. Field reads/updates retain initialization
and access protection; whole-value Move transfers responsibility and partial Move
through a deinit-bearing ancestor is rejected. Native destruction runs deinit,
then fields in reverse logical order, without repeating construction or cleanup
at the moved source. Descriptor pooling preserves distinct destructor identities.

Expected stdout is exactly
`Counter created.\nSum is 55.\nLeaving finish.\nCounter destroyed.\nDone.\n`,
stderr is empty, exit 0. `test-milestone4.ps1` checks the original input plus renamed
O0/O2 copies, an Abort sum variant (only creation output, exit 1, no defer/deinit),
and a destructor that inspects the final value. It also rejects twelve invalid
construction/Move/access/destruction inputs before IR/executable publication.
Reports and source/compiler/build identities remain under
`bin/milestone4/<configuration>/<run-id>/`.

Debug/Release builds pass with zero warnings/errors and both full managed suites
pass 6,955 tests. The 39 new struct tests include reload/rebinding, initialization
diagnostics, normal/conditional Move, replacement, nested destruction, and zero
measured warm ownership/IR allocations. Eighteen fixtures pass 36 O0/O2 native
checks, including exact owned-string release counts and construction Abort.
The milestone script passes 33 checks per Debug/Release compiler. Existing aggregate
function, element-Move and deferred fixtures pass 118, 130 and 50 O0/O2 native checks
respectively, including bounded nontermination. Milestones 1, 2 and 3 pass their
25, 21 and 37 checks in both configurations. No required check remains unverified
and no blocker remains. Final target reports are under Debug run
`96fe216328304e22bdfdade8ebeb5b11` and Release run
`36e2455629f148d493225c8d36c93485` in the report directory above.

SPEC already prescribes these semantics and is unchanged. Drafts and NativeAOT
were untouched. Generic/inherited structs, synthesized construction, structure
methods, borrowed receivers and general accessors remain explicitly outside this
execution subset. Milestone 5 was read for scope only; its implementation has not
been started. Program numbering is independent of PLAN.md's broader stages.

<a id="status-record-50"></a>

## STATUS record: Program Milestone 3 complete (2026-09-16)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 3 complete (2026-09-16): the unmodified
`milestones/Milestone3.kimi` passes final Binding, ownership, LLVM verification,
native linking and execution with the current compiler. Existing explicit-main,
function argument/result, return/defer and Milestone 2 Abort support were
sufficient; no compiler or SPEC changes were needed. The initial native attempt
failed only at the sandbox's LLVM execution permission and passed once local tool
execution was allowed.

Added `backend/windows-x64/test-milestone3.ps1`: Debug/Release each pass 37 checks,
including actual-source and renamed O0/O2 builds, native/CLI execution and ten
rejected argument/result/startup/defer/ownership inputs. Successful stdout is
exactly `Leaving sumTo.\nSum is 55.\nLeaving main.\n`, stderr empty, exit 0.
`sumTo(-1)` produces no stdout or deferred messages and Aborts at 5:9 with exit 1.
`sumTo(9)` prints only `Leaving sumTo.\n` before the caller's Abort at 18:9.
A separate deferred-mutation variant confirms the returned i32 is secured before
cleanup changes its source. Variants never alter the checked-in milestone file.
Reports, diagnostics and source/compiler/build identities are retained under
`bin/milestone3/<configuration>/<run-id>/`.

Both solution builds pass with zero warnings/errors and both full managed suites
pass 6,916 tests, zero failures/skips. Existing function and defer fixtures pass
56 and 50 O0/O2 native checks respectively, including bounded nontermination
checks. Completed Milestones 1 and 2 pass their 25 and 21 checks respectively in
both configurations. No required check remains unverified. NativeAOT and draft
edits were not performed. Milestones 4–5 were read only; this checkpoint makes no
completion claim for them. See PLAN.md's program checkpoint and milestones/README.md
for reproduction commands; program numbers are independent of PLAN's M1–M17.

<a id="status-record-51"></a>

## STATUS record: Program Milestone 2 complete (2026-09-16)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 2 complete (2026-09-16): implemented explicit `$abort(expression)`
through Binding, ownership, LLVM generation and ordinary Windows x64 execution.
The reserved builtin expects one owned string, evaluates/acquires it once, has
Type Never, and is independent of user `abort` declarations; no Kimi API was added.
Its runtime emits the source location and `KIMI_E_ABORT` followed by unchanged
UTF-8 message bytes and LF, then exits 1 without normal message destruction or
enclosing cleanup. A failed stderr write still exits without recursive diagnostics.
Argument return, nested Abort and checked arithmetic failure retain their own
outcomes. Explicit Abort's checking-only continuation diagnoses moved/uninitialized
uses in unreachable source. Never conditions retain no fabricated Boolean value,
and a noncompleting while produces no value storage.

The unmodified `milestones/Milestone2.kimi` builds and runs at O0/O2 with exact
stdout `Sum is 55.\nDone.\n`, empty stderr and exit 0. Separate `expected = 54`
variants have empty stdout, report `KIMI_E_ABORT: Unexpected sum` at 13:5 and exit 1.
`backend/windows-x64/test-milestone2.ps1` passes 21 checks per Debug/Release compiler,
including renamed byte-identical input, CLI forwarding and six rejected inputs.
Reports and compiler/source/build identities are in
`bin/milestone2/<configuration>/<run-id>/`.

Both solution builds pass with zero warnings/errors; all 6,916 managed tests pass
per configuration, zero failures/skips. The 38 new Abort tests cover positive,
negative, ownership, reload and runtime-plan cases; 15 fixtures pass 30 O0/O2 native
executions. Existing Counter/overflow native regressions pass 12 executions and
output/runtime-adapter regressions pass 68. Milestone 1 passes 25 checks in both
configurations. Warm ownership/IR generation allocates zero measured bytes; full
warm Binding on the reload fixture measured 72 bytes/pass and was not optimized.
No required check remains unverified. SPEC §§17.3/22.5 already prescribe this
behavior and were not weakened or changed. Drafts and NativeAOT were untouched.
Programs 3–5 were read only and remain unverified targets in this milestone series.
See the current program checkpoint in PLAN.md for reproduction and scope.

<a id="status-record-52"></a>

## STATUS record: Program Milestone 1 complete (2026-09-16)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Program Milestone 1 complete (2026-09-16): the unmodified
`milestones/Milestone1.kimi` passes source parsing, Binding/startup selection,
ownership analysis, LLVM generation/verification, native linking and execution
using the current compiler. The first native attempt failed only because the
execution sandbox denied `opt.exe`; allowing the local toolchain execution
resolved that failure. No compiler implementation change or SPEC amendment was
needed: §§22.2/22.4 already define the implemented behavior.

Added `backend/windows-x64/test-milestone1.ps1` for repeatable actual-source and
byte-identical renamed-source verification. Debug/Release each pass 25 checks:
O0/O2 native execution and CLI forwarding, exact UTF-8 `Hello, world!` plus LF,
empty stderr and exit 0; owned local Move, Unicode/NUL/empty output; and ten
rejected type/argument/ownership/startup inputs before emission. Borrowed output
is rejected at the existing unsupported-Binding boundary; this does not claim
more precise overload diagnostics or general borrowing support. Build records,
compiler/source hashes, rejection diagnostics and byte results are retained in
`bin/milestone1/<configuration>/<run-id>/`.

Both solution builds have zero warnings/errors; both full managed suites pass
6,878 tests with zero failures/skips. Existing Release emission/runtime regression
coverage passes 68 native executions at O0/O2, including output faults and cleanup.
No required Milestone 1 check remains unverified. NativeAOT was not run. Programs
2–5 were read for dependencies only, with no implementation or completion claim;
their numbers are independent of PLAN.md's M1–M17. See the program checkpoint at
the top of PLAN.md and `milestones/README.md` for scope and reproduction commands.

<a id="status-record-53"></a>

## STATUS record: Dependency API renames (2026-09-15)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Dependency API renames (2026-09-15): Updated call sites to match the upgraded packages: Arc.Collections registration APIs, Arc.Threading termination waits, Arc.Unit logging configuration, empty console, notification and console input interfaces, SimpleCommandLine parser options, Tinyhand map header reads, and Benchmark's Arc.Crypto/FarmHash calls. Updated related comments and the test console implementation. `dotnet build Kimigayo.slnx --no-restore --nologo -v:q` passed with no warnings or errors; `dotnet test --project xUnitTest/xUnitTest.csproj --no-build --no-restore` passed all 4,870 tests. CLI `--help` output and normal exit were also verified. Language rules, CLI syntax, and data formats are unchanged, so the SPEC body needed no update. NativeAOT tests and performance measurements were not run.

Updated: 2026-09-15. Baseline reviewed: `e861ce5ebf7c365f8e8416e0ed692500eb9b1f42`. Static element partial Moves are covered in §4.8/§7.4; static string element comparisons and shared arguments in §4.9/§7.5; element borrowing from owned parameters and temporaries in §4.10/§7.6; and partial Moves from owned parameters in §4.11/§7.7.

<a id="status-record-54"></a>

## STATUS record: Document structure (2026-09-14)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Document structure (2026-09-14): [SPEC.md](SPEC.md) is the main index; the 22 chapters and Appendices A, B, D, E, and F are split into `spec/`. Appendix C is consolidated into the index's implementation-status guide. The `doc/` directory for designs, decisions, and change records was renamed to `draft/`. Chapter numbers, specification text, and existing precedence rules are preserved. Compiler coverage is unchanged.

<a id="status-record-55"></a>

## STATUS record: Test verification failure behavior (2026-09-15)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Test verification failure behavior (2026-09-15): In [§17.5](spec/17-failure-handling.md#175-test-verification-operations), `$expect` evaluates its condition once, records failure if false, and continues. `$require` no longer returns normally on failure: it records failure, cleans up condition and message temporaries, then Aborts the case's child process. Both operations share the same permitted locations in test-only bodies, including helpers, local functions, Closures, and defer. Normal cleanup does not run after Abort begins; the runner retains both verification failures and the actual termination reason. Aligned the SPEC index, Chapters 14, 17, and 22, and Appendices A and F. These are documentation changes only; they add no compiler or runner implementation support.

<a id="status-record-56"></a>

## STATUS record: Design document alignment (2026-09-15)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Design document alignment (2026-09-15): Integrated test declarations, verification operations, discovery/CLI, and execution/recovery/diagnostics into [§6.5.1](spec/06-declarations-and-containers.md#651-test-definitions), [§17.5](spec/17-failure-handling.md#175-test-verification-operations), [§20.9](spec/20-compilation-configuration.md#209-test-command-and-discovery), and [§22.6](spec/22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting). Aligned existing input/generation rules and Appendices A, D, E, and F. Concrete formats, defaults, and additional features remain in Appendix D.4. Removed adoption and precedence references to the withdrawn Composition Root proposal and deleted decision documents; Entry/Provider selection remains unsettled. Also synchronized outdated pending-integration notes, earlier if/match proposals, block→do, object headers/metadata, and Appendix F public API names with the current SPEC. This documentation-only work adds no test language, runner, or Composition implementation support. Links, headings, code fences, and diff formatting were checked; compiler/native/NativeAOT tests and performance measurements were not run.

<a id="status-record-57"></a>

## STATUS record: Dependency and artifact specification integration (2026-09-15)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Dependency and artifact specification integration (2026-09-15): Gave precedence to the designated draft and integrated configuration, resolution, locks, input records, source packages, pack/publish, semantic verification reuse, and test membership into [Chapter 18](spec/18-modules-and-dependencies.md). Aligned native requirements/supplies, member closure, directives, and CLI in [Chapter 20](spec/20-compilation-configuration.md), shared generation and product/test budgets in [Chapter 21](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), and related appendices and index entries. Clarified English rules and examples, replacing outdated statements that formats/configuration were undefined and passages that delegated the specification body to drafts. Checked 681 local references across all 28 specification files, code blocks, and diff formatting. These documentation changes do not establish completion of external dependency resolution, pack/publish, or persistent semantic caching. Drafts and compiler code were unchanged; compiler/native/NativeAOT tests and performance measurements were not run.

This document records coverage verified against current source and related tests. Lexing/parsing, Binding, ownership, LLVM generation, and native execution are separate stages; support in an earlier stage does not establish executability. See [SPEC.md](SPEC.md) for the specification and [PLAN.md](PLAN.md) for the overall plan. Legacy C.* links point to their corresponding areas.

<a id="status-record-58"></a>

## STATUS record: Receiver shorthand (2026-09-15)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

Receiver shorthand (2026-09-15): Binds `self` in instance functions and Contract function requirements as `self: ref/Self`. Instance Property `get()` / `set(value: T)` accessors receive `ref/Self` / `uniq/Self` receivers, respectively. Supports stored custom, computed, and explicit Contract accessors; group/rootgroup accessors remain receiver-free. Preserves explicit receivers, argument positions, Origin completion, and classification of ordinary functions without receivers as type functions. Updated §7.3, §11.2, and the syntax appendix. All 390 related ReceiverShorthand / PropertyRevisionParse / PropertyBinding / FuncDeclarationParse / InheritedReceiverBinding / ContractBinding / TypeBinding / KotonohaSerialization tests passed, covering shorthand parse/write/parse, save/reload, and re-Bind. Warm Binding with shorthand allocated 0 additional bytes. Existing limits on general accessor call expression checking, ownership, generation, and specialization remain; this change does not extend their execution support. NativeAOT tests were not run.

<a id="c1-coverage-summary"></a>
<a id="c3-lexical-forms-and-types"></a>
<a id="c8-specification-review-integration-2026-09-08"></a>
<a id="c9-follow-up-specification-review-items-16"></a>
<a id="c10-type-and-literal-review-items-314"></a>
<a id="c14-tokenizeparse-increment-2026-09-09"></a>
<a id="c15-property-and-move-syntax-update-2026-09-10"></a>
<a id="c32-compile-time-switch-spelling-2026-09-12"></a>

<a id="status-area-snapshot"></a>

## STATUS area snapshot and verification records (2026-09-14–18)

Historical record. Its states, instructions, deadlines, paths and scope refer only to the recorded execution. Later corrections are listed in the migration audit; resume from [PLAN.md §2](PLAN.md#2-execution-state).

## 1. Lexing, Parsing, and Compilation Conditions

`kimi check <input>` runs semantic checks without producing artifacts or invoking native tools. Project-only dependency graphs use exact identities, fixed configuration/source bytes, iterative cycle/conflict checks and direct reference mappings. Binding, control flow and ownership inspect every dependency body in its defining module, including settings, source aliases and separately staged default aliases; transitive modules are not exposed by name. Library runtime bodies are rejected. Product checks exclude explicit `TestSources` without reading them and skip test dependency resolution. Inline `#Test` functions are excluded from product symbols, body checking, startup, ownership and emission; selected syntax-level eligibility and target errors still diagnose. Tests cannot be called through ordinary product lookup. Reload/rebind and dependency modules retain this membership, and seven native fixtures verify product behavior at O0/O2. Test discovery and full test-body verification remain unfinished. Invalid identities, duplicate decoded names and nonempty legacy `KotonohaArray` receive configuration/migration errors.

`kimi restore <project.kimiproj>` records deterministic product/test lock partitions, preserves an unchanged lock, and replaces changed results atomically under exclusive ownership. Failed resolution records unresolved partitions. Product commands validate required locks, including existing locks for empty dependency graphs, and never rewrite them; bare `--locked` is supported. Source edits trigger fresh semantic checks without lock changes. Package loading, persistent processing/semantic records, test compilation and cross-module native generation remain unfinished. Existing unsupported ownership and generation boundaries still apply. Module Binding/ownership reuse has zero measured warm allocations in the focused scenario; no general allocation claim is made for graph loading.

| Verified implementation | Limits and evidence |
| --- | --- |
| UTF-8 source, original positions/diagnostics, identifiers and NFC validation pinned to Unicode 15.0, indentation/brackets/continuation lines, and recovery | [Lexing](Kimi/Compiler/Lexing), [SourceDocument](Kimi/Compiler/Core/SourceDocument.cs), UnicodeIdentifier / SourceEncoding / ParserRegression tests |
| Koto construction for types, generics/lengths/Origins, declarations/Properties/accessors, captures, literals/collections, expressions, and control flow; parse/write/parse and save/reload | [Parsing](Kimi/Compiler/Parsing), FrontEndSyntax / PropertyRevisionParse / NestedTypeParse / KotonohaSerialization. Syntax support does not establish complete semantic analysis or execution. Source artifacts and portable interchange formats are also separate concerns |
| Preserves numeric source text and exact magnitude; rounds float literals directly to target precision; validates character/string escapes | [NumberLiteralHelper](Kimi/Compiler/Helper/NumberLiteralHelper.cs), [FloatingTypes](Kimi/Compiler/FloatingTypes.cs), NumberLiteral / CharLiteralParse / StringLiteralParse |
| `#if` / `#switch` selection and condition validation; condition Names are case-sensitive, with exact duplicates and built-in collisions rejected | [Compilation](Kimi/Compiler/Core/Compilation.cs), [condition evaluation](Kimi/Compiler/Parsing/BasicValue/CompileTimeConditionEvaluator.cs), CompilationSpecification / DirectiveConditionValidation. Distinguishes validation of short-circuited/unselected Cases from exclusion boundaries for false `#if` branches |

Current syntax uses `#switch`, Properties/accessors, and ordinary type adaptation. Legacy `#match` and dedicated `@move` syntax are not supported as compatibility forms. `alias` opens a Container; it does not introduce a type alias.

<a id="c4-declarations-and-compile-time-directives"></a>
<a id="c5-properties"></a>
<a id="c16-initial-binding-pipeline-2026-09-10"></a>
<a id="c17-complete-type-representation-and-declaration-origins-2026-09-10"></a>
<a id="c18-constraint-proof-foundation-and-declaration-validation-2026-09-11"></a>
<a id="c19-core-intrinsic-identities-and-copy--owned-2026-09-11"></a>
<a id="c20-static-function-contracts-and-associated-types-2026-09-11"></a>
<a id="c21-property-conditional-conformance-and-inherited-member-binding-2026-09-11"></a>
<a id="c26-enum-case-construction-and-coreoptionresult-2026-09-11"></a>
<a id="c28-positional-pattern-binding-and-match-coverage-2026-09-11"></a>
<a id="c33-runtime-type-test-binding-2026-09-12"></a>

## 2. Binding, Types, and Kimi

Final expression validation retains associated projection qualifier constraints and conformance proofs that normalization removes from explicit generic call arguments and runtime-test targets. Invalid inputs or late-invalid witnesses revoke their call/runtime-test certificates. Valid nested Types, dependent caller evidence, independent member errors, source reload, replacement recovery and zero measured warm Binding allocations are covered by `ExpressionInputFormationBindingTest` and `ExpressionProjectionCertificateBindingTest`; general generic body proofs remain incomplete.

Named function and Property signatures, enum payloads and direct-base projections also retain final conformance validity. Late failures propagate through dependent declaration and conformance certificates until declaration states stabilize, independent of source order. Invalid projected bases invalidate descendants; invalid enum payloads revoke construction plans. Valid self projections retain their evidence. `SignatureProjectionCertificateBindingTest` and `AggregateProjectionCertificateBindingTest` cover these checks, reload, repair and zero measured warm allocations.

Ordinary, conditional and fixed associated-Type specifications retain late projection conformance validity as well. Invalid specifications revoke matching conformance certificates while independent paths remain verified. `AssociatedProjectionCertificateBindingTest` covers inherited fixed definitions, nested Types, source order, self references, repair/reload and zero measured warm allocations.

Conditional conformance premises also retain projection qualifier and witness validity. Invalid premises revoke their conformance/Copy target and conditional member applicability while independent conformance paths and ordinary containing-Type formation remain separate. The shared declaration-context guard includes invalid conditional scopes, so enclosed computed Properties cannot retain verified certificates after premise failure. Final call completion rechecks the selected function's substituted constraints and member conditions after late evidence changes. `ConditionalProjectionCertificateBindingTest` covers nested/dependent premises, clause/source order, Copy, direct members and computed Properties, independent outer Properties, reload/repair, provisional/final passes and zero measured warm Binding allocations.

Conditional conformance requirements and their retained projection domains must also cover the effective Type/Contract intersection for each ancestor path. Restricted Contracts, concrete Types and projection qualifiers cannot be exposed by a wider conformance. `ConditionalPremiseAccessBindingTest` covers valid narrower intersections, struct/enum declarations, ancestor obligations, independent paths, reload/recovery and zero measured warm allocations. Functions and computed Properties in conditional blocks independently check their published premises against their own effective API domains; a narrower conformance does not narrow a public member. `ConditionalMemberAccessBindingTest` covers member/witness/call certificates, narrow/enclosing domains, independent siblings, repair/reload and zero measured warm allocations.

Ordinary function, struct, enum and Contract constraint projections retain the same proof requirements before dependent declaration certificates are exposed. `ConstraintProjectionCertificateBindingTest` covers unused function declarations, conformance certificates, definition-side evidence, source order, repair and reload. Constraint applicability normalizes associated-Type identities after substitution for calls without receivers as well as member calls. `ProjectedConstraintCallBindingTest` covers direct/nested requirements, Boolean combinations, caller evidence, invalid qualifiers/witnesses, reload, retained-plan recovery and zero measured warm Binding allocations. General generic body proofs and generation remain incomplete.

Constructed structs and enums enforce input clauses whose subjects are associated-Type projections, such as `T.Origin.Item is i32`, in addition to direct generic-parameter clauses. Existing substitution and proof rules apply to every admitted input clause. `AssociatedSubjectConstraintBindingTest` covers identities/capabilities/Boolean requirements, nested Types, dependent caller evidence, generic-call and enum-construction certificates, reload/repair and zero measured warm allocations.

Constraint proof queries and available premises compare normalized associated-Type identities, including nested Types and exact compound assumptions. Equivalent concrete and projected requirements therefore share evidence, and their directly contradictory polarities produce Error. Symbolic subject identities, Type structure and the limited Boolean proof rules remain intact; normalization does not add case analysis or infer a satisfying Type. `NormalizedConstraintProofBindingTest` covers direct/substituted proofs, dependent calls, Unknown boundaries, rebinding, source reload, replacement recovery and zero measured warm allocations. Projection-free environments retain direct fact lookups. General generic proof and generation completion remain open.

Function Type conditions use complete Type parsing on both sides of `is`, including one-parameter Function Parameter Lists, nested/right-associated arrows and Boolean requirements. Binding preserves Function Parameter List arity: optional trailing commas preserve identity, zero parameters differ from one Unit parameter, and one Tuple parameter differs from several parameters. `FunctionTypeConstraintBindingTest` and `FunctionParameterIdentityBindingTest` cover Owned/non-Copy behavior, identity/subtyping, overloads, generic call/substitution checks, declaration domains, replacement, writing/reload and zero measured warm allocations. Common Function Value generation remains a later milestone.

Structs and enums admit closed named, primitive, constructed, Tuple, fixed-array, Semantics-applied and concrete-rooted associated-projection Types as Constraint Clause subjects. These clauses must be proved as declaration obligations; they do not become generic assumptions or declare conformance for the named Type. Refuted obligations invalidate the owning declaration and dependent conformance, Property and call certificates, including late failures and either source order. Subject Types must cover the declaration API domain. `ClosedTypeConstraintBindingTest` covers positive/negative obligations, absent conformance, Boolean requirements, access, reload/replacement and zero measured warm allocations. `StructuredConstraintSubjectBindingTest` covers balanced Type-subject parsing, nested generic formation, canonical writing, reload, API access, malformed-input recovery and unchanged executable/function prefix recognition. `ClosedProjectionConstraintBindingTest` additionally verifies retained qualifier input constraints and API domains, normalization, late invalidation, replacement/reload and zero measured warm allocations. Dependent compound Type subjects in struct/enum Constraints become generic input propositions and are discharged after substitution at uses. `DependentCompoundConstraintBindingTest` covers arrays, Tuples, constructed identities, Boolean requirements, nested associated projections, generic forwarding, Copy derivation, API domains, replacement/reload and zero measured warm allocations. Parentheses around Self preserve conformance and Copy declaration behavior. Input dependence considers both operands and Boolean subpropositions, so a closed subject with a dependent requirement (for example, i32 is T) is checked at each use. `DependentRequirementConstraintBindingTest` covers substitution/normalization, contradictions, malformed formation, forwarding, enums, replacement/reload and zero measured warm allocations. Function subjects retain SPEC 7.4 restrictions to their own generic parameters and rooted associated projections. Contract bodies likewise discharge closed conditions independently, including concrete-rooted projections. Only propositions depending on the conforming Self become implementation premises; a closed condition cannot prove its own missing or cyclic conformance. Refuted conditions invalidate the Contract, its refinements and conformance certificates. An ordinary Self clause referencing an invalid Contract or required Type declaration also invalidates its implementing Type and dependent consumer certificates. Local witness failures retain independent conformance paths. Dependent compound and reversed associated-identity conditions remain implementation obligations. Contract subjects also cover their API domain, and grouped Self cannot declare refinement. `ClosedContractConstraintBindingTest` and `ContractConstraintSubjectBindingTest` cover these distinctions, replacement/reload and zero measured warm allocations. `ModuleBindingTest` covers these complete-Type conditions across explicit/default imports, module reload and source replacement, including revocation of consumer call certificates. Further import cases cover Function Parameter List identity, closed Function obligations, late-invalid refined premises and appended dependency Contracts. Provisional refinement tests also cover imported pending/independent evidence, newly supplied parent Contracts and qualified/root parent Name validation across module reload.

Closed conditions that remain unknown during provisional Binding leave their Type declaration unresolved and withhold dependent conformance, Property and call completion. Final Binding recomputes absence and appended evidence. `ClosedConstraintLifecycleBindingTest` covers positive/negative transitions, Contract conditions, declaration order, valid appended fragments, reload and zero measured warm allocations. Conformance verification also discharges the implementing struct/enum closed prerequisites inside the active proof-query guard: named-self and mutual registration cycles cannot certify themselves, while independent finite Boolean evidence remains usable. `ClosedConformancePrerequisiteBindingTest` covers these cases, rebind/reload and zero measured warm allocations. Pending ancestor conditions also leave child Contracts unresolved, including diamonds, so exact child assumptions and their calls cannot bypass the dependency. `PendingRefinementDeclarationBindingTest` covers source order, missing names versus absent conformance, final rejection/recovery, error precedence, conformance certificates, reload and warm allocations. Absent refinement parents remain pending during provisional Binding. Contract shapes retain incomplete-parent state through descendants; known names still resolve, but incomplete shapes cannot supply conformance or refinement evidence. Qualified parent Names are resolved through transparent syntax wrappers. Complete parent syntax is checked before lookup, including root-qualified names; generic arguments, applied Semantics and grouped-Type forms cannot leave a resolved Contract declaration. `RefinementNameBindingTest` covers declaration identity, malformed syntax, provisional rejection, rebind/reload and warm allocations. `UnresolvedRefinementBindingTest` verifies appended requirements, wrong parents/cycles, final deadlines, independent assumptions, reload and warm allocations. These are internal Binding guarantees; the external Mod host and generated-source persistence remain separate work.

Missing Type/Contract names in provisional Constraint subjects or requirements retain an explicit Unresolved proposition until reanalysis. Incomplete propositions are not assumptions, and a successful Boolean branch cannot complete a declaration or call before all requirement syntax is formed. Available non-Type/invalid inputs and function-subject restrictions remain errors. `UnresolvedConstraintBindingTest` covers source additions, final rejection, nested Types, Boolean formation, calls, reload and zero measured warm provisional allocations.

Direct proof queries retain Unknown for complete operands containing Types with unresolved declaration conditions, including borrows, Function Types and capability queries through generic identities. Error retains priority over Unknown, and unrelated resolved operands keep their ordinary judgments. `UnresolvedCapabilityProofBindingTest` covers these prerequisites, final failure/recovery, reload and zero measured warm query allocations.

Associated-Type Contract premises also supply intrinsic Copy/Owned evidence through refinement during symbolic capability queries. The query checks current provenance and prerequisite validity without eagerly expanding recursive associated-Type families. `AssociatedRefinementCapabilityBindingTest` and module cases cover direct/indirect inheritance, nested projections, pending dependencies, conjunction/disjunction boundaries, generic calls, reload and warm allocations. Derived Constraint facts retain their defining Contract separately from direct input assumptions. Refinement and inherited associated requirements supply evidence only while their closed declaration prerequisites and ancestor prerequisites are proven; pending evidence cannot create a premature contradiction. Independent direct facts and alternate valid refinement paths remain usable. Proof queries, associated identity normalization and overlap environments preserve this provenance. Ancestor availability checks traverse the existing deduplicated closure once; conformance paths are verified in dependency order with reusable per-query proof scratch and existing identity membership for prerequisite joins, so temporary Unknown results do not cause recursive re-expansion. `DeepRefinementPremiseBindingTest` covers 32/64-level chains/diamonds, deep pending/cyclic prerequisites and zero measured warm generic/conformance allocations. `ProvisionalContractPremiseBindingTest` covers pending/complete transitions, negative evidence, calls, reload and zero measured warm Binding allocations.

Late declaration failures also revalidate expanded Constraint environments in the certificate propagation loop. A failed Contract condition invalidates dependent generic declarations and their call evidence, while unrelated valid environments remain usable. `LateConstraintEnvironmentBindingTest` covers refinement-derived Copy premises, generic Type certificates, declaration order, provisional/final passes, recovery/reload and zero measured warm Binding allocations.

Current [Compilation.Bind](Kimi/Compiler/Core/Compilation.cs) performs final Binding once per invocation; re-Bind invalidates old semantic information and ownership verification results. It uses types, Symbols, and call plans on Koto plus reusable tables, without creating a separate Bound tree.

| Area | Verified coverage | Remaining limits |
| --- | --- | --- |
| Names and calls | Separate Type/Value lookup, source-local scopes/aliases, forward functions, Kimi identity, visibility, positional/named arguments, static default Type checking with preceding-parameter lookup, ordinary generic inference and candidate selection | Full access/fragment validation, default argument ownership/execution, general Origin inference, specialization, constructors/deinit, and indirect calls remain incomplete. Defaults do not infer generic arguments; self/later parameters are unavailable in their declaration environment |
| Declaration API accessibility | Recursive complete-Type exposure checks on named member-function parameters/results, generic struct/enum constraints, enum payloads and Property header/storage Types, using effective enclosing domains. Property headers (including inferred Types) retain the Property domain when accessors are restricted; requirement headers inherit the Contract domain. Additional accessor receiver/input/result Types use their own domains. Computed and requirement accessors share ordinary Self receiver validation; static accessors reject receivers, including in unused declarations. Conditional members use their declaring Container for receiver identity. Named member-function/Contract requirement parameter-result, explicit Property header, and explicit accessor receiver/input/result projections retain their qualifier, selected Contract and defining-requirement domains after normalization; invalid Property signatures cannot publish verified Properties or conformance witnesses; named-function API validation precedes final conformance certification, so rejected function signatures cannot retain verified witnesses; concrete bindings still undergo ordinary Type/conformance access checks. Parsed member-function/requirement and struct/enum/Contract constraints also retain projection domains on subjects and requirements, including associated requirements; invalid Contract constraints prevent conformance certification. Ordinary and conditional associated specifications retain qualifier/requirement projection access under the Type/Contract intersection, with independent ancestor checks; invalid paths cannot certify. Enum payload projections retain these domains before certification, including payloads bound early by inferred headers; invalid cases/enums cannot publish valid construction plans or conformances. Direct-base projections likewise retain these domains against the derived Type before inheritance validation; invalid bases propagate to descendants and prevent conformance certification. Private implementation storage remains separate. Unit/Tuple requirements and projections following constructed qualifiers now reach these checks, preserving requirement Boolean grouping and precedence. | Does not export source-local functions or infer named function result Types. Inferred Property Types are checked recursively after normalization; projection syntax confined to initializers/private helpers does not itself become an API component. Unused dependency APIs and explicit/default alias projections retain their defining domains, with module reload coverage. Remaining general access/fragment conformance audits still need completion. ApiAccessBinding, PropertyApiAccessBinding, AccessorReceiverBinding, ProjectionApiAccessBinding, PropertyProjectionApiAccessBinding, ConstraintProjectionApiAccessBinding, AssociatedSpecificationAccessBindingTest, InferredPropertyAccessBindingTest, EnumProjectionAccessBindingTest, BaseProjectionAccessBindingTest and RequirementTypeParsingTest cover private/internal/protected combinations, recursive payload/header Types, reload/replacement and zero measured warm Binding allocations |
| Declaration fragments | Same-name Types with different generic arities keep separate symbols, headers, members and Attributes. Matching arities merge with kind, semantic modifier and access agreement after defaults; enums/contracts cannot split at the same identity. One defining base survives omitted clauses and resolves with its source-local aliases. Explicit group headers determine synthesized path access. Selected Attributes survive generic/base header parsing. Origin headers match ordered names and bounds: under the current source grammar, valid split-struct bounds identify shared declaration slots or static, independently of source-local Type aliases. Exact bound identity, conflicts, source order, rebind/reload/writing and zero measured warm unbounded-fragment Binding allocations are covered | Origin bound proofs remain unfinished: bounded declarations still report Unsupported, and unknown targets are rejected. General logical storage ordering, generated-fragment finalization and proof-dependent artifacts remain incomplete |
| Type arity selection | Explicit and zero-arity Type uses select within the committed lexical, qualified, root, explicit-import or default-import stage; wrong nearer arities cannot fall back to outer/default candidates. Same-arity imports remain ambiguous. Generic Type member/base lookup, separate source fragments, canonical writing, replacement/rebinding, and module-local identities are covered | This does not complete omitted-argument inference or universal generic proofs, and does not enable nominal/generic native generation |
| Layout Attribute validation | Struct-only targets; one unnamed, non-interpolated literal mode (`C` or `Kimigayo`); duplicate-per-fragment and conflicting-mode errors; C instance Fields restricted to one selected source fragment. Matching explicit modes and omission merge, and canonical writing emits matching Layout specifications once. Diagnostics retain source locations; invalid Layout targets and merged declarations invalidate before inheritance/conformance certification, with rebind recovery. Directive exclusion and product Test syntax checks are covered | This is source validation, not nominal layout or ABI support. C open/derived/empty/zero-sized/alignment formation rules, instantiated layouts, generated-fragment provenance/finalization, and physical offsets remain M5/M8 work. Nominal generation remains unsupported |
| Complete Type and Origin representation | Nested Semantics, owner normalization, Tuples/functions/fixed arrays, generic pairs/slots, declaration Origins and input/static/intersection Origins, assignment/variance, and retained-borrow representation | Bound proofs, a general Origin/Loan solver, general SemanticsTarget application, and length inference remain incomplete. Unresolved obligations are retained |
| Original Semantics-pair grouping | For a declared `<s/T>` pair, `s/(T)` and repeated target grouping reconstruct the same WholeType as `s/T`, including nested Types and call-result Origins. Grouping nodes retain the bound projection. Different target bindings and inner Origin annotations retain ordinary validation. Reload/writing, syntax replacement, duplicate signatures and zero measured warm Binding allocations are covered by PairGroupingBindingTest | Does not complete explicit Origin annotations on original pairs, arbitrary Semantics application, universal role proofs, ownership or generic generation |
| Pair Origin annotation representation | `s/T from a` and `(s/T) from a` retain the same annotated WholeType representation without adding a Semantics layer. Insufficient owner/unsafe application evidence cannot certify an annotated WholeType or application. Invalid Origin names/mappings retain ordinary diagnostics; unannotated applications keep their existing proofs. PairAnnotationBindingTest covers nested Types, independent annotations and reload/rebind/writing | Full annotation support remains incomplete: valid borrow annotations still retain unresolved definition obligations. Borrow-role proofs, substituted inner-Origin dependencies, value fitting and Loans need further work; passing safety tests does not certify these programs or native generation |
| Grouped generic Type arguments | Fixed arrays nested inside grouped Type arguments, such as `Box<([2 of i32])>`, parse as Types instead of numeric length expressions. Constructed Types and explicit function Type arguments preserve nested arrays, Tuples, pointers, common Function Types and borrow Origins. Invalid non-Type/incomplete arguments still reject. GenericTypeArgumentBindingTest covers reload/writing, identity, length-expression syntax and zero measured warm Binding allocations | This does not complete general generic argument validity, explicit length calls, generic inference/proofs or native generic generation |
| Fixed-array length constants | Eligible integer let locals and static stored Properties, qualified names and accessible standard getters; recursive initializer evaluation, checked arithmetic in the established integer Type, literal-only subtree fitting, all 12 integer widths including 128-bit intermediates, and final nonnegative-isize checks. Private constants expand into values in symbolic length expressions. | Does not execute getters or initialize static storage. Calls, var, parameters, instance Fields, custom/computed accessors, cycles, and inaccessible constants cannot supply lengths. Generic instantiation, separate-compilation records, and general length inference remain incomplete. ConstantLengthBinding covers reload/rebinding, zero measured warm Binding allocations, and ordinary native fixed-array execution. |
| Local fixed-array inference | Explicit fixed shapes ending in `_` infer a unique element Type from initializer elements or an independently typed array; direct literals fit established evidence. Nested shapes, Copy/Move, cleanup order, evaluation count, rebind/reload and zero measured warm Binding allocations are covered, with 21 ordinary native fixtures | Empty untyped arrays, conflicting evidence and shape errors are rejected. General collection/control-flow/generic inference remains incomplete |
| Fixed-array call arguments | Candidate-local fitting handles fixed shapes, nested arrays/Tuples and element literals; established inner expressions retain their Types. Candidate-order independence, numeric ambiguity, named evaluation order, string cleanup, reload and zero measured warm Binding allocations are covered, with 21 ordinary native fixtures | General length/element generic inference, dynamic collection candidates and shared expected Types for unresolved nested calls remain incomplete |
| Constraints and Contracts | Proven/Refuted/Unknown/Error states, declaration assumptions, associated Types, refinement, verified witnesses, conditional conformance/members, and inherited receiver selection | Unknown is not treated as success. Full candidate equivalence, generic body/effect proofs, and ObjectCallCompatible body/callee validation remain incomplete |
| Properties | Stored/computed/requirement Properties, accessor types, permissions, Copy/Origin handling, and operation-specific witnesses | General expression read/get/set/init, receiver/cleanup handling, and generation remain incomplete |
| Enums and Patterns | Case identity, construction with an expected Type, payload acquisition, Tuple/Case/whole Patterns, exhaustiveness/subsumption warnings, and separate guard-candidate/body binding | Borrowed Subjects, general decomposition/guards, and generic proofs/generation remain incomplete. Arms that receive warnings are still checked |
| Runtime `is` / `is not` | Binds object Semantics over concrete struct Core to bool, retaining original operands/targets and shared-access requirements | Flow Type refinement, object Loans, and execution are unsupported |
| Kimi library | Validates six declarations: Copy, Owned, Callable, writeLine, Option, and Result. Derives Copy/Owned and rejects same-name Kimi impostors | 12 of the catalog's 18 slots are Missing. Option/Result declaration and analysis support does not establish complete generic runtime support |

Evidence: [Binding](Kimi/Compiler/Binding), [KimiLibrary](Kimi/Compiler/Binding/KimiLibrary.cs), [CoreCatalogTest](xUnitTest/Tests/CoreCatalogTest.cs), TypeBinding / ConstraintBinding / ContractBinding / PropertyBinding / ConditionalConformanceBinding / InheritedReceiverBinding / EnumBinding / PatternBinding / RuntimeTypeTest.

<a id="c7-control-flow-and-failure-handling"></a>
<a id="c24-whole-place-ownership-cfg-and-cleanup-plans-2026-09-11"></a>
<a id="c27-enum-construction-ownership-and-ordered-cleanup-2026-09-11"></a>
<a id="c29-owned-match-acquisition-decomposition-and-cleanup-2026-09-11"></a>
<a id="c30-current-control-flow-syntax-results-and-cleanup-2026-09-12"></a>
<a id="c31-transfer-seeded-unreachable-ownership-checking-2026-09-12"></a>

## 3. Control Flow and Ownership

[Analysis](Kimi/Compiler/Analysis) uses Binding acquisition plans to check whole-Place initialization, Moves, and assignment history at a CFG fixed point. It handles conditional replacement/destruction, locals, temporaries, arguments, results, concrete enum construction/decomposition, and whole-value responsibilities for Tuples/fixed arrays. §4.8 connects partial Moves, reinitialization, and destruction of remaining parts for static paths in constructed local Tuples/fixed arrays. General field/index handling and user deinit remain incomplete.

- Analyzes `if`, short-circuiting, `while`, `do`, `loop`, labels, `require`, `match`, return/yield/exit/continue, and defer. Distinguishes structural completion from execution reachability. Results are secured before cleanup and delivered only after normal completion. Abort performs no cleanup.
- Destruction follows reverse logical order. If normal transfer interrupts construction or argument acquisition, already-acquired responsibilities are handled. No result delivery or subsequent destruction is generated after nonterminating cleanup.
- Source checking after an explicit transfer or Never-returning call uses a continuation separate from the execution CFG, including Never Subjects and nonterminating guards. Unsupported remains for unreachable operations whose checking entry state cannot be constructed, such as those after general exitless loops or cleanup that prevents continuation.
- Whole-Subject guards have conservative validation paths in source order; effects on false paths also pass to later arms. Covered arms omitted at runtime are still diagnosed.
- Loans currently cover string comparisons, shared arguments, temporary strings, string guard candidates, parent-storage protection for Tuples/fixed arrays, and exclusive protection for element writes. Statically disjoint element paths are allowed within §4.6's scope. Owners remain protected through later arguments, guards, and cleanup; Loans end after securing a normal result or during normal transfer. Simple element assignment also holds an exclusive Loan from final location resolution through old-value destruction and placement (§4.7). General reference storage/return, uniq/reborrow, effect summaries, and borrowed Subjects remain incomplete.

Evidence: OwnershipAnalysis / EnumOwnership / MatchOwnership / UnreachableOwnership / CurrentControlFlow / ControlFlowConformance / ReferenceEmission / StringGuardEmission tests. Generation coverage is checked separately even after successful analysis.

<a id="c6-expressions-and-operators"></a>
<a id="c12-first-executable-milestone"></a>
<a id="c23-startup-selection-and-corewriteline-binding-2026-09-11"></a>
<a id="c34-minimal-literal-output-executable-2026-09-13"></a>
<a id="c38-compiler-pipeline-and-emission-preparation-2026-09-13"></a>
<a id="c39-compiler-review-dead-code-removal-and-emission-restructuring-2026-09-13"></a>
<a id="c40-scalar-control-flow-emission-and-nativeaot-2026-09-13"></a>
<a id="c41-scalar-selection-and-loop-results-2026-09-13"></a>
<a id="c42-deferred-cleanup-execution-2026-09-13"></a>
<a id="c43-checked-i32-division-and-remainder-2026-09-13"></a>
<a id="c44-scalar-functions-return-and-explicit-main-2026-09-13"></a>
<a id="c45-i32-bitwise-operations-and-checked-shifts-2026-09-13"></a>
<a id="c46-integer-execution-through-64-bits-2026-09-13"></a>
<a id="c47-explicit-integer-conversions-2026-09-13"></a>
<a id="c48-owned-string-locals-and-conditional-cleanup-2026-09-13"></a>
<a id="c49-owned-string-control-flow-results-2026-09-13"></a>
<a id="c50-owned-string-function-parameters-and-results-2026-09-13"></a>
<a id="c51-string-comparisons-and-backend-memcmp-2026-09-13"></a>
<a id="c52-unguarded-whole-subject-match-execution-2026-09-13"></a>
<a id="c53-copy-subject-match-guards-2026-09-13"></a>
<a id="c54-shared-string-arguments-and-referent-comparison-2026-09-13"></a>
<a id="c55-string-guard-candidates-and-temporary-shared-arguments-2026-09-13"></a>
<a id="c56-whole-tuple-and-fixed-array-execution-2026-09-13"></a>

## 4. LLVM Generation Coverage

[LlvmEmitter](Kimi/Compiler/Emission/LlvmEmitter.cs) supports only windows-x64-v1 Applications and rechecks final Binding, startup, and ownership. It selects implicit top-level execution or an eligible `public func main() -> ()`. It rejects external modules, declaration containers, Library generation, and unsupported bodies, even unused ones, before publishing output.

| Area | Current generation coverage | Limits and evidence |
| --- | --- | --- |
| Scalars | bool, 12 integer types (i8/u8 through i128/u128, plus isize/usize), char, f32/f64, and Unit; locals, acquisition/replacement, comparisons, direct arguments/results, existing control flow, and aggregate payloads | [ScalarTypes](Kimi/Compiler/ScalarTypes.cs), Scalar / Integer / WideInteger / Char / FloatEmission |
| Integer operations | Checked add/sub/mul, signed negation, inc/dec, comparisons, bitwise operations, shift-count checks independent of the operand type, and compound updates. Division/remainder through 64 bits check zero and signed min/−1 | The initial profile prohibits i128/u128 division/remainder. char supports only Unicode scalar-order comparisons, not arithmetic. Integer / Division / Bitwise / WideIntegerEmission |
| Floats | f32/f64 arithmetic and comparisons, literals at target precision, and value plans for NaN, ±0, and subnormals | Separate from full numeric conversion support. Float literal Patterns are unsupported. FloatEmission / NumberLiteralParse |
| Numeric conversions | Range checks in all 12×12 integer directions; exact direct integer/float literal fitting; f32↔f64; <=64-bit integer↔float; same-type numeric acquisition, including explicit owner targets. Narrowing preserves NaN/infinities/signed zero and allows underflow; finite overflow Aborts. Float-to-integer truncates then checks range, including negative fractions to unsigned zero. | Typed 128-bit↔float and char/bool numeric conversions are prohibited. [Binding.Conversions](Kimi/Compiler/Binding/Binding.Conversions.cs), Conversion / FloatConversionEmission / NumericConversionEmission |
| Identity Acquisition | Same complete owned primitive, Tuple, and fixed-array Types supported by ordinary generation; explicit owner targets and `@owner` shorthand, including grouping. Copies Copy values and Moves Non-Copy values using ordinary acquisition, partial-Move, repair, loan-conflict, and cleanup rules. | Other representations and Borrow/Reborrow adaptations remain unsupported. IdentityAcquisitionEmission covers native cleanup, evaluation order, reload, stale-plan rejection, and zero measured warm analysis/writing allocations. |
| Functions, results, and cleanup | Root functions and capture-free local functions, named arguments, recursion, scalar/Unit/owned string/Tuple/fixed-array arguments/results, Never results, selection/loop/match results, and defer | Defaults, generics, explicit Origins, captures, indirect calls, and a general aggregate ABI are unsupported. [FunctionAbi](Kimi/Compiler/Emission/FunctionAbi.cs), Function / Result / DeferredEmission |
| Owned strings | Literals, local Moves, same-Type Identity Acquisition, self-assignment/replacement, conditional destruction, temporary/selection/function results, writeLine, and all six comparisons | Strings are Non-Copy even with Static backing; comparisons use UTF-8 byte sequences. Source operations for Heap construction, interpolation/concatenation, and other ownership adaptations are unsupported. String*Emission |
| Shared strings | Required ref/string arguments with implicit input Origins and forwarding, referent comparisons, temporary owned strings as shared arguments, string guard candidates, and comparisons/shared arguments for static string elements of owned locals, parameters, and temporaries (§4.9–4.10) | A reference is one pointer to a handle. Results must be independent values. Local storage/return, explicit @ref, uniq, and nested borrowing are unsupported. [ReferenceTypes](Kimi/Compiler/ReferenceTypes.cs), Reference / StringGuardEmission / ElementBorrowOwnerEmission |
| Match/guards | Exhaustive matches on bool, integers, char, Unit, and owned strings; literal/wildcard/whole let/var/parenthesized Patterns; guards over Copy or ref/string candidates | Float Subjects retain existing whole-Pattern/guard coverage. Borrowed Subjects and Tuple/enum decomposition execution are unsupported. Candidates are read-only and separate from body bindings. Match / Guard / Char / Float / StringGuardEmission |
| Tuples and fixed arrays | Construction from supported scalars, Unit, owned strings, and nested aggregates; whole local Copy/Move/replacement/conditional destruction; if/do/loop/match result delivery and ordinary function value arguments/results; Copy element reads via numeric Tuple selectors or isize fixed-array indices; simple assignment to Copy/supported Non-Copy elements of initialized local vars, compound numeric element assignment, and prefix/postfix integer inc/dec; statically disjoint element operations; static partial Moves from locals/parameters and destruction of remaining parts; local var reinitialization; static string element comparisons and shared arguments | Owners only; maximum depth 64 and size/count int.MaxValue. Dynamic Non-Copy element acquisition, whole-aggregate borrowed arguments/results, aggregate Subjects, and struct/enum generation are unsupported. [AggregateLayout](Kimi/Compiler/Emission/AggregateLayout.cs), AggregateEmission / AggregateResultEmission / AggregateFunctionEmission / ElementEmission / ElementAssignmentEmission / ElementUpdateEmission / ElementPathEmission / ElementReplacementEmission |

Generation uses verified typed-value, CFG, ABI, and cleanup plans. The Writer does not reinterpret the AST. It rejects inconsistencies in type identity, constants, inputs, dominance, result arrivals, live flags, Loans, and destruction plans. Distinct language types remain distinct even when their LLVM widths match.

Strings transfer each field of a 24-byte handle and use flags only for conditional responsibilities. Tuples separate physical alignment order from logical order; fixed arrays use stride-based layout. Construction targets final subslots, whole-value transfer uses memcpy into verified separate storage, and destruction follows reverse logical order (a loop for arrays). These are current internal representations, not a general public ABI.

### 4.1. Aggregate Selection Results (2026-09-14)

Tuples and fixed arrays can be delivered as results of if/else, do/yield/exit, value-producing loops, and matches over already-supported Subjects. One result plan handles Copy aggregates, values containing owned strings, nesting, and zero-sized values, connecting them to local initialization/replacement, construction payloads, outer-result transfer, and destruction of unconsumed results. See [AggregateResults](examples/AggregateResults/README.md) for executable examples.

Generalized StringResults to SlotResults, sharing validation of Declare, result Write, Join, and arrival edges. Type classification is independent of Copy status; Lowering validates concrete layout support. Result plans are registered before layout validation, and only verified expression results are accepted. Function return Places and Subjects are not accepted solely by Kind. No feature additions were needed in the Binder, ABI, LLVM Writer, or runtime.

Results are secured before cleanup and delivered on normal arrival. Validation checks placement into uninitialized storage, MustInit at every arrival, Write dominance, and both logical and physical arrival counts. Consumption must follow the current lifetime's Join, rejecting premature acquisition during cleanup and false approval based on an earlier arrival. Duplicated defer bodies share result slots per source expression but retain separate Declare/Join operations per expansion. A loop's result Declare appears once before its head. No result-specific live flags or aggregate phi nodes are added.

Transfer, reverse-order destruction, and conditional replacement reuse existing handling. The RHS's independent result is secured and cleanup completes before any required old-value destruction, new-value transfer, and flag setting. Abort/nonterminating cleanup generates no subsequent delivery or destruction. Direct construction into result slots, element access/partial Moves, and aggregate function ABI support were outside §4.1; function ABI support was added in §4.2 and Copy element reads in §4.3.

[AggregateResultEmissionTest](xUnitTest/Tests/AggregateResultEmissionTest.cs) checks nesting, expected array types, covered arms/guards, result destruction, defer, back edges, O0 snapshots, Abort/nontermination, invalid plans, and recovery by reanalysis. Warm Bind and Ownership analysis plus IR output, including defer, each allocate 0 B. Existing string-result validation also uses the shared plan, with premature-consumption rejection tested for both types. Throughput improvement has not been measured.

### 4.2. Tuple and Fixed-Array Function ABI (2026-09-14)

Connected supported owned Tuples/fixed arrays to value arguments and returns of ordinary direct calls. Covers nesting, Copy/Move, zero-sized values, named arguments, mixed Unit/scalar/string/ref-string signatures, shallow recursion, and returns from defer/selection results. See [AggregateFunctions](examples/AggregateFunctions/README.md) for executable examples.

Nonzero-sized aggregates pass acquired argument slots and the caller's independent result slot by ptr. Copy retains the original responsibility; Move transfers it. Argument names use logical indices `%a<i>`; the result is `%ret`. Zero-sized values omit physical arguments/result storage but retain CallEntry, acquisition, parameter responsibility, and Produce on normal return. Arguments and results of one call may not share storage. An inner call's result can serve as an outer call's acquired argument without another transfer. Direct result-storage forwarding for `return f(x)` is not implemented.

Generalized string function-slot validation to SlotFunctions, keeping its role separate from selection-result SlotResults. Checks uninitialized storage at the call site, result initialization only on normal return, return Write dominance, Deliver after cleanup, and conditional destruction-flag initialization at parameter Produce. Reuses existing transfer and reverse-logical-order destruction; no additions were needed in the LLVM Writer, runtime, or backend.

Signatures and bodies use the same AggregateLayoutPool. The ABI cache retains only physical passing conventions, omissions, and type shapes; semantic validation checks the current BoundType. Temporary type references are released on both success and failure. Shared-argument Loans end after securing an owned aggregate result containing no references. Warm Bind and Ownership plus IR output with shared borrowing and defer each allocate 0 B. Tests verify ABI reuse across reparse-equivalent input and that syntax trees are not retained. Throughput has not been measured.

This ABI support does not extend the Binder's contextual inference. Fitting array literals to call argument types and some nested Tuple argument inference remain incomplete; pass these through explicitly typed locals. The existing path for constructing array literals with an expected return type remains available. Aggregate borrowing, element writes, Non-Copy element acquisition/partial Moves, structs/enums, generics, and indirect calls were not supported in this increment. See §4.3 for Copy element reads.

### 4.3. Copy Element Reads from Tuples and Fixed Arrays (2026-09-14)

`pair.0` and `values[index]` can read from locals, parameters, temporaries, and selection/function results. Fixed-array indices use isize; untyped integers are fitted to isize. Supported scalars, Unit, and Copy aggregates can be acquired as final values, even from Non-Copy parents containing strings. See [ElementReads](examples/ElementReads/README.md).

Separated place formation (ProjectElement) from final acquisition (Produce/Element). Chained `.0` / `[i]` operations compute addresses incrementally from one root, without intermediate aggregate slots or transfers. Scalars load at the final location; Copy aggregates transfer once into an independent slot. Before generation, validation checks types, input-value/source correspondence, root initialization, Loans, and dominance of parent projections, indices, and acquisition. Invalid plans are rejected before IR is written.

The root is Read before index evaluation and protected through the final Copy by the existing shared-Loan stack. Move, replacement, and destruction during index evaluation are prohibited; shared reads and Copy are allowed. Normal transfer ends Loans at the relevant boundary before cleanup. Parent changes after Copy do not affect the acquired value. Temporary receivers remain alive after Copy until normal end-of-expression cleanup.

Each array projection checks `icmp uge i64 index, length`, covering negative indices too. Stride calculation and address formation occur only in the success block. Invalid indices Abort with `KIMI_E_INDEX_BOUNDS`, without evaluating later indices, defer, or destruction. Constant indices into empty arrays also Abort at runtime. Zero-sized elements retain checks while omitting unnecessary addresses, loads, and transfers. Dedicated address names use the same continuation-label scheme as existing arithmetic checks.

Simple Copy element assignment was added in §4.4, compound numeric updates in §4.5, and static Non-Copy element Moves/partial Moves in §4.8. Shared results, aggregate borrowing, and Array/Slice/Index/^/Range remain unsupported. This root protection is not a general Loan/Origin/Move Path solver.

[ElementEmissionTest](xUnitTest/Tests/ElementEmissionTest.cs) has 57 cases. They cover nesting, scalar widths, Copy results, bool/Unit, zero lengths/sizes, value snapshots, short-circuiting/phi, parameters, loops/defer, transfers, unreachable code, covered arms, Loan conflicts, invalid plans, and recovery. Warm Bind and ownership analysis plus IR generation each allocated 0 B over 128 iterations.

### 4.4. Simple Assignment to Copy Elements of Tuples and Fixed Arrays (2026-09-15)

Copy elements of initialized, complete owned Tuples/fixed arrays in local vars can be replaced with `=` through numeric selectors, isize indices, and nested paths. Supported elements are scalars, Unit, and recursively Copy aggregates; parents may contain owned strings. RHS construction, Copy, function results, and selection results are secured as independent values before each part of the LHS path is evaluated once. See [ElementAssignments](examples/ElementAssignments/README.md).

Location acquisition shares existing LocateElement/ProjectElement operations and bounds checks; only the final operation is distinguished as WriteElement. No whole-parent Write, reinitialization, or destruction plan is generated, preserving parent initialization and responsibility. Scalar stores share existing storage representations; Copy aggregates share existing memcpy generation. No intermediate aggregate Copies or per-element live flags are added. Zero-sized values retain evaluation and bounds checks.

The root is protected during location acquisition. In the §4.4 implementation, ending the access's own Loan was connected directly to the store by a normal edge. §4.7 generalized this to location resolution → exclusive Loan → placement → Loan end. Conflict checks against other Loans remain. Validation checks types, input sources, mutable roots, RHS initialization/dominance, result Join, and parent/index correspondence, rejecting invalid plans before IR publication. Writing another element of the same root inside an index is also rejected to protect storage. This differs from §4.6's disjointness checks after final location resolution.

Aligned simple assignment in structural-completion and control-flow analysis with RHS-first evaluation, retaining syntax-order visitors. Static destination types survive transfers during index evaluation, and value sources of labeled do expressions are normalized. Normal transfer, Abort, or nonterminating cleanup in the RHS or indices prevents subsequent location acquisition/writes, preserving existing result-securing and cleanup rules.

[ElementAssignmentEmissionTest](xUnitTest/Tests/ElementAssignmentEmissionTest.cs) has 79 cases. They cover nesting, scalar representations, snapshots, self-assignment, Copy aggregates, function/selection results, defer duplication, transfer precedence, unreachable code/covered arms, zero lengths/sizes, bounds Abort, nontermination, parent destruction order/counts, Loan conflicts, invalid plans, and recovery by reanalysis. Warm Bind and ownership analysis plus IR generation each allocated 0 B over 128 iterations. Throughput improvement has not been measured.

Compound numeric element assignment/inc/dec was added in §4.5; supported Non-Copy element replacement in §4.7. Non-Copy element acquisition, partial Moves/reinitialization, borrowed/temporary receivers, Properties, and dynamic collections are outside this increment. This does not narrow the specification's permitted behavior.

### 4.5. Numeric Element Updates in Tuples and Fixed Arrays (2026-09-15)

Using the same initialized owned local var roots as §4.4, connected integer elements to `+= -= *= /= %= &= |= ^= <<= >>=` and prefix/postfix `++ --`, and f32/f64 elements to `+= -= *= /=`. Supports nesting, Non-Copy parents, defer expansion, and function bodies. The existing profile still prohibits i128/u128 division/remainder. See [ElementUpdates](examples/ElementUpdates/README.md).

Following SPEC §13.7.2, evaluates location acquisition, bounds checks, old-value Copy, RHS evaluation, numeric computation, and store once each, in that order. This differs from RHS-first simple assignment. After storing, compound assignment returns Unit, prefix inc/dec returns the new value, and postfix returns the old value, without reloading. Integer overflow, division/remainder and shift checks, and IEEE float operations share existing scalar handling. Shift RHS checks retain its independent integer type.

Reads, simple assignments, and updates share LocateElement, CopyElement, and StoreElement. Numeric computation and inc/dec result generation also share ordinary local-update handling. Projection plans reference updates, and a reusable ElementUpdates table connects RHS, computation, and result. No intermediate aggregate Copies, per-element live flags, or update-specific LLVM operations are added. Parent initialization and ownership responsibilities are preserved.

Lowering checks the update source, operator, old value and RHS, types, unique projection owner, Loans and dominance, consecutive edges from computation → store → release of the access's own Loan → result, and prefix/postfix result selection. It also retains validation of RHS-securing order for simple assignment. Invalid plans are rejected before IR output; reanalysis restores valid plans.

Index evaluation uses shared protection. After the final projection's bounds check succeeds, the access's protection switches to an exclusive Loan, held through old-value acquisition, RHS evaluation, computation, and store. Only that access's own store is permitted. Existing outer Loans are retained; another live access to the same parent prevents exclusive acquisition. No new LLVM instructions or value slots are needed; existing Loan tables and integer projection IDs are reused.

`a[0] += a[0]` is rejected because it conflicts with the specification's exclusive Loan. The same rule covers copying the parent into RHS function arguments and reads inside defer. §4.5 initially used conservative root-level checks that also rejected siblings; §4.6 added support for statically disjoint paths. Updates to other roots and shared reads during index evaluation are allowed. Normal transfer ends abandoned-access Loans before cleanup. Bounds/arithmetic Abort, RHS/index transfers, and nonterminating cleanup prevent subsequent stores. Static destination types survive Never indices, and Never shift RHS values are treated as normal transfers.

The initial implementation used shared protection through the RHS and allowed self-reads. This was corrected to match the exclusive-Loan rules in §4.6.4/§15.6.2. Simple assignment retains the current specification's RHS-first order. Executable examples now Copy needed values into locals before updating elements.

[ElementUpdateEmissionTest](xUnitTest/Tests/ElementUpdateEmissionTest.cs) has 114 cases. They cover every integer width; float NaN/infinity/signed zero; evaluation counts/order; shift widths; arithmetic/bounds Abort; parent destruction audits; Loan conflicts; defer, unreachable code, transfers, nontermination, and invalid plans. They also verify Loan states around exclusive acquisition and after stores, rejection of self-reads/parent Copies, release on normal transfer, and rejection of modified plans. Warm Bind and ownership analysis plus IR generation with nested updates and defer each allocated 0 B over 128 iterations. Throughput improvement has not been measured. Non-Copy replacement was added in §4.7; static partial Moves/reinitialization in §4.8. Borrowed/temporary receivers, Properties, and dynamic collections remain unsupported.

### 4.6. Loan Disjointness for Static Element Paths (2026-09-15)

Connected §15.6.2's disjointness rules to existing Copy element reads, simple assignments, and numeric updates on owned Tuples/fixed arrays. Allows `a[0] += a[1]++`, `pair.0 += pair.1`, and assignment to sibling Copy aggregates or Unit elements. Access to the same element, ancestors/descendants, or whole-parent Copy/Move/replacement conflicts with an exclusive Loan. See [ElementPaths](examples/ElementPaths/README.md).

Under §15.1.3, static fixed-array selectors accept only in-range nonnegative integer literals and parentheses. Identity uses numeric value regardless of radix or digit separators. Unary plus, arithmetic, conversions, variables, and conditionals are not constant-folded to prove disjointness. An unknown index widens the check to the entire known parent path. `pair.0[i]` and `pair.1[j]` are disjoint, but disjointness is not proven for `a[i].0` and `a[j].1`. Empty arrays and out-of-range literals provide no disjointness evidence.

Distinguished receiver location acquisition as LocateReceiver from value Read, and linked Copy acquisition/stores directly to projection IDs. Paths use parent IDs, static prefixes, depths, and selectors in the existing projection table. No dedicated path objects or sets are created; comparison takes a scan proportional to path depth. Before overlap checks, validation rechecks source, parent, selector, prefix, and operation cross-references, rejecting modified plans. No new LLVM instructions, runtime Loan checks, or value slots are added.

Separated root-storage protection during index evaluation from the selected element's exclusive Loan after final location resolution. Writes to siblings in the same root remain prohibited during index evaluation. Once the final location is resolved, statically disjoint operations are allowed; an access's own Loan authorizes only its own old-value acquisition and store. Completing an inner sibling update retains the outer Loan. Existing evaluation order, normal transfer, defer, Abort, parent whole-initialization state, and destruction responsibilities are preserved.

[ElementPathEmissionTest](xUnitTest/Tests/ElementPathEmissionTest.cs) checks static/dynamic paths, nested updates, Copy aggregates/Unit, selection results, snapshots, defer/transfers, bounds Abort, parent destruction audits, unreachable code, invalid plans, and recovery by reanalysis. Warm Bind and ownership analysis plus IR generation with nested paths, sibling updates, and defer each allocated 0 B over 128 iterations. Throughput improvement has not been measured. This increment excludes Non-Copy element operations, partial Moves/per-element initialization, and general fields, Properties, ref/Slice, or dynamic collections.

### 4.7. Non-Copy Element Replacement in Complete Owned Values (2026-09-15)

Simple assignment can replace string elements and supported aggregate elements containing strings in initialized, complete owned Tuples/fixed arrays held by local vars. Supports nested paths, dynamic isize indices, literals, Moves from independent locals, function results, if/do/match results, loops, and defer. Existing Copy element operations remain supported. See [ElementReplacements](examples/ElementReplacements/README.md). This increment initially excluded Non-Copy element extraction; static partial Moves/reinitialization, including `a[0] = a[0]`, were added in §4.8.

Following §13.7.1, secures the RHS as an independent value before evaluating the receiver and each index once and checking bounds. For both Copy and Non-Copy values, the access's shared location-acquisition protection switches to an exclusive Loan after final location resolution, held through old-value destruction and placement. Reads, numeric updates, and simple assignments share projection and Loan tables. Writes requiring no old-value Copy use the same exclusive acquisition/end validation. Outer Loans remain active; only statically disjoint sibling replacements are allowed. Root protection during index evaluation is unchanged.

One WriteElement emits old-value destruction for the exact element type, then new-value transfer. It reuses string destruction/handle transfer, aggregate type-specific destruction/memcpy, and shared slot/element-address output. No whole-parent Write/Cleanup, backup Copy of the old element, per-element live flags, or new runtime APIs are added. The parent retains completeness and sibling responsibilities; placement consumes the input. Copy status is distinct from whether destruction is actually needed. Zero-sized values retain evaluation, bounds checks, and responsibility transfer.

If the RHS does not complete, the LHS is not evaluated. Normal transfer from an index cleans up the secured RHS under ordinary temporary-value rules. Bounds Abort performs no old-value destruction, placement, or cleanup. Nonterminating RHS/index cleanup generates no later placement. Only paths where existing string/aggregate destruction calls return normally proceed to transfer. This does not extend to general user deinit or replacement with Origin/Loan dependencies from retained references.

Lowering checks source, exact types, input/parent initialization, dominance, mutable roots, Loans, and consecutive location-resolution/write/Loan-end edges. Destruction targets are derived from verified projections and types rather than duplicated in a separate destruction-plan table. Also fixed slot tracking that treated TransferAggregate with an explicit address operand as the older Place/Constant form when transferring match results into elements.

[ElementReplacementEmissionTest](xUnitTest/Tests/ElementReplacementEmissionTest.cs) has 55 cases. They cover replacement, Moves, dynamic indices, nesting, result delivery, defer/transfers, Abort/nontermination, destruction counts/order, Loan/initialization rejection, modified plans, and recovery by reanalysis. Warm Bind and ownership analysis plus IR generation each allocated 0 additional B over 128 iterations. Destruction audits verify logical responsibilities, including Static-backed strings; they do not establish source-level Heap-string construction support. Throughput improvement has not been measured.

### 4.8. Static Element Partial Moves, Reinitialization, and Destruction of Remaining Parts (2026-09-15)

From constructed owned local let/var roots, strings and supported Non-Copy aggregates can be acquired through numeric Tuple selectors, in-range literal fixed-array indices, or combinations of these. Copy elements remain Copies. Supports Moves from let, use of remaining siblings, reinitialization of static paths in var, whole-value acquisition after repairing all missing parts, and replacement of partially moved parents. See [ElementMoves](examples/ElementMoves/README.md).

Separated constructed storage from current completeness. Moving all children individually leaves the parent constructed, allowing each child to be repaired. Moving a child as a whole prevents access inside it until that child itself is placed again. Rejects initial construction of an unconstructed array through element assignment, repair of let, and acquisition/borrowing of missing parts or incomplete whole values. Branches, loops, transfers, defer, and unreachable checking flow share the same state transitions.

Normalizes each path to an integer ID independent of per-access projection IDs, stored in reusable dictionaries, struct lists, and bit lanes. Tracks only referenced static paths and untracked remainders for relevant roots, without expanding in proportion to array length. Paths are sorted once to build reverse sibling links. Destruction-plan generation reads CFG input once and shares it across elements. Complete parts use existing type-specific destruction, untracked array ranges use reverse loops, and individual parts use direct calls. Only conditionally live remainders need flags.

Simple assignment remains RHS-first. `a.0 = a.0` Moves the element into the RHS, skips destruction of the now-missing old element, then replaces it under location resolution and an exclusive Loan. Replacing an incomplete parent first destroys only its remaining parts in reverse logical order. Numeric update order/exclusive Loans, root protection during index evaluation, and no unwinding on Abort remain unchanged. Dynamic Copy reads/replacements require a complete known prefix, conservatively rejecting operations that may touch missing parts.

Lowering rechecks acquisition kinds, static paths, sources/types, initialization/completeness, Loans, and dominance. Partial destruction and flag transitions derive from ownership state. Validation also checks missing/duplicate/incorrect flag values and placement after dispatch. Existing string handle transfer, aggregate memcpy, and destruction helpers are shared; no runtime APIs or Loan locks are added.

[ElementMoveEmissionTest](xUnitTest/Tests/ElementMoveEmissionTest.cs) initially had 70 cases (now 69 after moving one former parameter-rejection case in §4.11). Covers remaining-responsibility counts/order, branches, parent replacement, self-assignment, nesting, dynamic siblings, loops, defer, result delivery, unreachable code/covered arms, zero-sized values, bounds Abort, nontermination, diagnostic reasons, modified plans/reanalysis, conditional flags, and sparse paths. Warm Bind and Ownership plus IR generation each allocated 0 additional B over 128 iterations. Throughput improvement has not been measured.

Borrowing static string elements for comparisons/shared arguments, rejected in §4.8, was added in §4.9 and extended to owned parameters/temporaries in §4.10. Partial Moves from owned parameters were added in §4.11. Dynamic Non-Copy acquisition, partial Moves from temporary elements, borrowed receivers, general structs/Properties, user deinit, retained-borrow dependencies, and dynamic collections remain unsupported. These are current implementation boundaries, not restrictions on the language specification.

### 4.9. Static String Element Comparisons and Shared Arguments (2026-09-15)

Connected static string elements of Tuples/fixed arrays held in constructed owned local let/var values to all six comparisons and required ref/string arguments of existing direct calls. Supports in-range integer literals with parentheses, radices, or digit separators; nested paths; overlapping shared borrows of one element; named arguments; nested calls; mixed string guard candidates; independent string/aggregate results; loops; defer; unreachable code; and covered arms. See [ElementBorrows](examples/ElementBorrows/README.md).

Comparisons and shared arguments use a common BorrowStringElement operation to borrow the LocateElement projection target, without acquiring an owned temporary through AcquireElement. After the final bounds check, Read/Borrow switches root protection during location formation to a shared Loan on the selected element. This protects through later operands/arguments and cleanup during calls. Only the access's own Loan ends, after comparison or after securing an independent normal call result. Normal transfer releases abandoned Loans before cleanup. Outer Loans, original ownership responsibilities, and no unwinding on Abort are preserved.

Shared element Loans also use existing static-path comparisons for conflicts. Moves, replacements, and exclusive acquisitions of overlapping elements/ancestors are rejected; Moves, replacements, and numeric updates of statically disjoint siblings are allowed. Dynamic sibling operations use the existing static prefix. Root protection during index evaluation remains. For partially moved parents, validation separately checks the constructed state needed for location formation and target-element initialization; only remaining elements can be borrowed.

Binding's PlaceOriginSource is shared with Lowering, mapping Origins to owners for both Tuple selectors and fixed-array indices. Lowering rechecks source, root, type, static path, initialization, Loans, consecutive edges from location formation, and dominance. Existing ElementAddress values serve string comparisons and ref/string arguments. No owned temporary slots, string transfers, new destruction flags, LLVM instructions, runtime APIs, or backend ABI changes are added. Projection tables store integer borrow-operation IDs and reuse existing tables and capacity.

[ElementBorrowEmissionTest](xUnitTest/Tests/ElementBorrowEmissionTest.cs) initially had 78 cases (now 76 after moving two former rejections to success cases in §4.10). Checks normal execution, rejection reasons, remaining responsibilities/destruction order, early returns, Abort in later indices, nontermination, rejection of modified plans, and recovery by reanalysis. Warm Bind and Ownership plus IR generation with partial Moves, sibling updates, and defer each allocated 0 additional B over 128 iterations. Throughput improvement has not been measured.

Borrowing from owned parameters/temporaries was added in §4.10. Non-Copy shared results through dynamic indices, borrowed receivers, explicit @ref/uniq, local reference storage/return, whole-aggregate borrowing, general structs/Properties, user deinit, retained-borrow dependencies, and dynamic collections remain unsupported. This extends execution coverage without changing language rules.

### 4.10. Static String Element Borrowing from Owned Parameters and Temporaries (2026-09-15)

Extended §4.9's shared BorrowStringElement handling to owned parameters, Tuple/fixed-array construction temporaries, direct function results, and if/do/loop/match results. Supports both comparisons and required ref/string arguments. Receivers are evaluated once, using ElementAddress within existing owner slots. No owned element temporary slots, transfers, or partial Moves are added. See [ElementBorrowOwners](examples/ElementBorrowOwners/README.md).

Storage is not accepted solely by Place Kind. Validation checks existing function/construction/selection-result role tables and requires parameter Produce, call-result Produce after normal return, and CompleteConstruction to dominate location formation. Selection results must match the current Declare/Join. Location formation, use, or destruction during cleanup after a result-securing Write is rejected. Parenthesized and labeled sources share existing ValueSource normalization.

Temporary owners remain alive until the expression's prescribed endpoint; ending a Loan does not cause early destruction. For paths skipped by short-circuiting, existing live flags start at 0 on entry, become 1 at construction completion, normal call return, or result Write, and return to 0 at cleanup. Conditional selection-result cleanup additionally traverses existing adjacency tables from every reachable Write to its matching Join, verifying that undelivered values cannot reach that cleanup. Ordinary uses retain existing dominance checks. Separate lifetimes from loops/defer duplication are allowed; Abort does not unwind.

Borrowed temporary owners are recorded once from comparison/Loan tables into reusable arrays, eliminating per-Place table scans. Flag validation shares bits in existing scratch storage; result-delivery path checks also reuse an existing queue and reusable arrays. No additional syntax-tree or BoundType references are retained, and runtime APIs, backend ABI, and LLVM Writer are unchanged.

Owned-parameter partial Moves were added in §4.11. The specification prohibits parameter updates/reinitialization. Partial Moves/updates of temporary elements, dynamic Non-Copy indices, borrowed receivers, explicit @ref/uniq, and reference storage/return are outside scope. Fixed-array literal inference is unchanged; use existing explicitly typed locals or function return types. This extends execution coverage under existing lifetime, delivery, and Loan rules without changing the language specification.

### 4.11. Static Partial Moves from Owned Parameters (2026-09-15)

Strings and supported Non-Copy aggregate elements can be Moved through static paths from owned Tuple/fixed-array parameters of ordinary functions. Supports nesting and in-range integer literal indices. Remaining parts can be Copied, compared, passed as shared arguments, or Moved through other static paths. Rejects acquisition of moved elements, paths under moved ancestors, and incomplete whole values. See [ElementParameterMoves](examples/ElementParameterMoves/README.md).

Owner eligibility is centralized in ElementAccess.SupportsMoveRoot and shared by Analysis and Lowering, distinct from borrowable temporaries/selection results. Lowering retains existing SlotFunctions checks for parameter receipt, types, initialization, and dominance over location formation. Parameters retain let-like behavior; updates and reinitialization are prohibited.

Existing Initialize handling already initializes sparse Move Paths on parameter receipt, so no dedicated state table is added. Generated-code PathFlagTransition now initializes flags at the receipt Produce matching the parameter source. Only necessary path flags are set to 1 on receipt and to 0 when the corresponding part Moves. Existing partial destruction handles remaining parts in reverse logical order. Uses actual argument slots directly, without another whole-parent transfer or conversion to a local. Runtime APIs, LLVM Writer, and backend ABI are unchanged.

Return values are secured before callee cleanup and delivered only after it completes normally. Transfers during evaluation retain responsibilities for acquired arguments and remaining parameter parts. Abort does not unwind; Abort/nontermination during cleanup prevents subsequent destruction and delivery. No new arrays or caches of syntax/type references are added.

Partial Moves from temporaries or function/selection results, dynamic Non-Copy element acquisition, borrowed receivers, general structs/Properties, and user deinit are outside this increment. This extends execution coverage under §7's parameter rules and §15.1.3's Move Path rules without changing the language.

<a id="c2-builds-modules-and-source-artifacts"></a>
<a id="c11-executable-preparation-and-sequence-review-2026-09-09"></a>
<a id="c13-remaining-implementation-selections"></a>
<a id="c22-lowering-and-windows-profile-specification-integration-2026-09-11"></a>
<a id="c25-core-declaration-catalog-and-native-backend-candidate-2026-09-11"></a>
<a id="c35-shared-llvm-version-policy-2026-09-13"></a>
<a id="c36-native-build-execution-and-shared-package-release-2026-09-13"></a>
<a id="c37-generated-kernel32-import-library-2026-09-13"></a>

## 5. CLI, Artifacts, and Windows Runtime

- [Project](Kimi/SolutionAndProject/Project.cs) Check performs semantic validation; Generate/`emit` produces consistent `.ll` and `.link.json` files; Build/`build` produces a native exe. `run` executes an existing Application without automatically rebuilding after source changes.
- [Solution](Kimi/SolutionAndProject/Solution.cs) shares input resolution across build/run/emit: try the specified path, then `.kimiproj` if no extension was given, then `.kimi`. Failure of a selected target does not fall back to another candidate. A single `.kimi` becomes an implicit Application/O2 project containing only that file, with its target selected from the OS/OS architecture (currently Windows x64; `--Target` can override it). It includes no neighboring sources/project settings and creates no `.kimiproj`. run validates and executes existing artifacts without reading source contents. The CLI command is consistently `emit`; unknown commands, including legacy `emit-llvm`, exit with 1.
- [EmissionArtifacts](Kimi/Compiler/Emission/EmissionArtifacts.cs) / [NativeToolchain](Kimi/Compiler/Emission/NativeToolchain.cs) handle schema 3, IR/exe/supply hashes, ABI and LLVM identity, invalidation of previous success on failure, and publication from staging. They collect tool output and support cancellation, time limits, and child-process-tree termination. Application output/exit status is forwarded.
- [ToolchainResolver](Kimi/Compiler/Emission/ToolchainResolver.cs) selects the root from CLI ToolchainRoot, environment variable KIMI_TOOLCHAIN_ROOT, then the default location. The default is beside the executable; source builds search the checkout's toolchain directory. LlvmBin and explicit backend-path overrides are handled separately. emit alone needs no LLVM installation. Ordinary .NET builds do not regenerate the backend.
- [WindowsProfile](Kimi/Compiler/Emission/WindowsProfile.cs) and [profile.json](backend/windows-x64/profile.json) define LLVM 22.1.8, backend ABI 2, and __chkstk/memcmp/memcpy/memmove/memset. Versions are shared with Directory.Build.props; actual archive hashes are checked too. Integrity checks remain mandatory even for explicit unpinned-toolchain trials.
- [WindowsRuntime.ll.in](Kimi/Compiler/Emission/WindowsRuntime.ll.in) implements startup, UTF-8 output, allocation/freeing, string destruction, and Abort. Normal exit is 0; Abort is 1. LF is written separately and NUL is treated as data. Static backing is not freed; Heap responsibilities are released. General object/Weak/metadata runtime support remains incomplete.
- [Kernel32Imports](Kimi/Compiler/Emission/Kernel32Imports.cs) generates and validates an import library from project-owned definitions using llvm-dlltool. It supplies seven Windows APIs for the runtime and three for native tests, removing the need to configure the SDK's kernel32.lib.

Evidence: EmissionArtifacts / NativeToolchain / ToolchainResolver / Kernel32Imports / MinimalEmission tests and [backend verification scripts](backend/windows-x64). See §7 for recorded ordinary native execution results.

## 6. Unconnected Product Features and Performance Infrastructure

| Area | State verified in code |
| --- | --- |
| Modules and Mod | [Compilation](Kimi/Compiler/Core/Compilation.cs) retains configured external Kotonoha identifiers but does not connect them to source loading. Bind does not execute Mod; generation rejects external modules/Libraries. Portable interchange and persistent semantic-plan reuse remain incomplete. Composition Root Entry/Provider integration remains unsettled |
| Generics, objects, and Kimi | Generic shared/specialized generation; Closures/indirect calls; struct/enum/Property/user constructor/static execution; rc/arc/Weak and runtime refinement; Array/Slice/Dictionary and iteration; and the language test runner remain incomplete. See §2–3 for declaration/analysis coverage |
| LSP | [LspServer](Kimi/Lsp/LspServer.cs) handles initialize, document management, shutdown, and related communication. Diagnostic publication for open/change is commented out; only empty diagnostics on close are connected. Dump requests/output are also unconnected |
| KimiCode | [extension.js](KimiCode/extension.js) hard-codes the adjacent Debug DLL and DebugWait=true. General-purpose server configuration and host integration tests are not established |
| CI and distribution | [test.yml](.github/workflows/test.yml) / [publish.yml](.github/workflows/publish.yml) run a Linux Release build followed by tests without a specified configuration/target. PR coverage, Windows native checks, zero-test detection, and log retention are not established. NuGet pack/push is defined but was not run in this work |
| Performance | Reuses tables, scratch storage, and capacity for Binding, types, CFG, ABI, constants, and layouts. Related tests check 0-byte warm allocations and retained references after reparse; [Benchmark](Benchmark) provides lexing, Binding, ownership, and other workloads. This does not guarantee allocation-free execution on every path or improved throughput |

## 7. Verification Records

### 7.1. Non-Copy Element Replacement and Element Operations (2026-09-15)

Verified §4.7's Non-Copy element replacement and regressions for §4.3–4.6 reads, simple assignments, numeric updates, and static paths. Beyond removing the Copy restriction, implemented old-element destruction, responsibility transfer, and exclusive Loans for simple assignment, connecting them to existing string/aggregate handling. Replaced the old blanket rejection of Non-Copy element assignment with a rejection case for then-unsupported self-assignment requiring a partial Move.

| Check | Result |
| --- | --- |
| Solution build (Debug / Release) | Both passed with 0 warnings and 0 errors |
| Managed tests (Debug / Release) | 4,614 passed in each; 0 failures/skips. Added 55 ElementReplacementEmissionTest cases |
| LLVM verifier and ordinary native O0/O2 for Non-Copy replacement and existing element operations | 246 fixtures, 492 successful runs: 60 replacement fixtures/120 runs and 186 existing element-operation fixtures/372 runs. Includes destruction audits, bounds/arithmetic Abort, and nontermination with timeouts |
| Aggregate native regressions for shared destruction/transfer handling | 157 Aggregate fixtures, 314 successful runs. O0/O2 checks cover construction, result delivery, function ABI, and destruction audits. Combined with Element fixtures: 403 fixtures/806 runs |
| Warm allocation | Bind and Ownership plus IR generation with function/selection results, nested element replacement, and defer each measured 0 B over 128 iterations |
| ElementReplacements CLI build/run | Release/O2 passed; printed `element replacements complete` and exited with 0 |
| NativeAOT and new throughput benchmarks | Not run |

To reproduce, run each configuration's build→test sequence in §7.2, then apply `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'` and `-FixturePattern 'Aggregate*.ll'` serially to generated fixtures. Fixture generation and native execution must also run serially. Every Element IR file regenerated after the final build/test matched the native verification inputs by SHA-256. See [ElementReplacements](examples/ElementReplacements/README.md) for the CLI example.

Additional tests exposed and led to fixes for slot tracking during match transfers and LLVM output passing element addresses to destruction functions. Successful results are from verification after those fixes; managed success alone is not used as evidence of native execution or destruction responsibilities. Three rejection tests were strengthened to check completed Binding and the Loan-conflict diagnostic reason, then rerun in Debug/Release.

The preceding §4.6 increment passed 4,559 managed tests per configuration, 186 Element fixtures/372 native runs, and the ElementPaths Release/O2 example.

The preceding §4.5 exclusive-Loan correction passed 4,468 managed tests per configuration and 163 Element fixtures/326 runs. Old fixtures treating self-reads as successful were replaced with valid prior-Copy examples and compile-time rejections. The ElementUpdates Release/O2 example also produced expected output and exit 0.

The initial §4.5 increment passed 4,451 managed tests per configuration, 160 Element fixtures/320 runs, and 14 ordinary local-update fixtures (IntegerValues/WideOperations/FloatArithmetic)/28 runs. Its self-read allowance preceded the Loan correction above and is not evidence of specification conformance.

The preceding §4.4 increment passed 4,354 managed tests per configuration, 95 Element fixtures/190 runs, and 59 AggregateFunction fixtures/118 runs. The ElementAssignments Release/O2 example also produced expected output and exit 0.

### 7.2. Verification of the Preceding Increments (2026-09-14)

The baseline review checked main entry points, type/conversion/ABI/aggregate and ownership rejection conditions, the Kimi catalog, and LSP/extension/CI against current code and related tests. After implementing §4.1 result delivery, §4.2 function ABI, and §4.3 Copy element reads, the following builds and managed checks were rerun.

| Check | Result on 2026-09-14 |
| --- | --- |
| Solution build (Debug / Release) | Both passed with 0 warnings and 0 errors |
| Managed tests (Debug / Release) | 4,275 passed in each; 0 failures/skips. Includes fixture generation |
| LLVM verifier and ordinary native O0/O2 for aggregate selection results (at §4.1 implementation) | 52 fixtures, 104 successful runs. Includes destruction counts/order, dependency symbols, and nontermination with timeouts |
| Native regression for existing string results (at §4.1 implementation) | 47 fixtures, 94 successful O0/O2 runs. Combined with aggregate results: 99 fixtures/198 runs |
| LLVM verifier and ordinary native O0/O2 for aggregate function ABI (at §4.2 implementation) | 59 fixtures, 118 successful runs. Checks destruction order/counts, Abort, nontermination with timeouts, and dependency symbols |
| Native regression for existing string functions (at §4.2 implementation) | 63 fixtures, 126 successful runs. Combined with aggregate functions: 122 fixtures/244 runs |
| LLVM verifier and ordinary native O0/O2 for Copy element reads (§4.3) | 41 fixtures, 82 successful runs. Checks bounds Abort, empty arrays/zero-sized values, index evaluation order, temporary-receiver destruction order/counts, and dependency symbols |
| Aggregate function ABI native regression (after §4.3 implementation) | 59 fixtures, 118 successful runs. Combined with element reads: 100 fixtures/200 runs |
| ElementReads CLI build/run | Release/O2 passed; printed `element reads complete` and exited with 0 |
| AggregateFunctions CLI build/run | Release/O2 passed; printed `leaving echo` twice, then `aggregate functions complete`, and exited with 0 |
| AggregateResults CLI build/run | Release/O2 passed; printed `cleanup`, then `aggregate results complete`, and exited with 0 |
| Standalone runtime/backend and LSP integration scripts | Not run in this work |
| NativeAOT, VS Code host, actual CI workflows, distribution, and new benchmark measurements | Not run in this work |

Reproduction commands (run build→test serially for each configuration):

```powershell
dotnet build Kimigayo.slnx -c Debug --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore
dotnet build Kimigayo.slnx -c Release --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Release --no-build --no-restore
```

Build warning counts reflect the current settings. Tests verify supported and rejected boundaries; they are not proof of conformance to the entire SPEC.

### 7.3. CLI Input Resolution and Implicit Projects (2026-09-15)

For each loaded project, build/run/emit prints a one-line summary of its name, project/source filename, implicit-project status, Targets, OutputKind, and Optimization. An explicit `--Target` is shown separately. This replaces the old filename-only Target Projects list. Direct `.exe` execution does not gain a ProjectFile display.

After adding the display, the Debug compiler build passed with 0 warnings/errors. Existing fixtures verified implicit-project emit/build/run and explicit-project emit with `--Target`; all displayed the summary and exited with 0.

- Debug compiler build passed with 0 warnings/errors. All 52 related SolutionInput / NativeToolchain / EmissionArtifacts managed tests passed.
- `./backend/windows-x64/test-cli.ps1 -Configuration Debug` passed. Using the managed .NET CLI and real LLVM, it checked existing O0/O2 builds, extensionless projects, single-source O2 builds/emit, run after source changes, specified-path precedence, rejection of fallback from a broken project, and rejection of the legacy command name.
- Confirmed exclusion of a broken neighboring source file, no implicit `.kimiproj` creation, unchanged native success records after emit/run, and forwarding of run stdout/exit codes.
- NativeAOT tests, a full managed-suite rerun, and performance benchmarks were not run.

### 7.4. Static Partial Moves and Destruction of Remaining Parts (2026-09-15)

Final specification review found that comparisons/shared arguments could incorrectly Move elements into temporaries. This was corrected by rejecting direct element borrowing as then unsupported. Added checks for rejection of all six comparisons/shared arguments and for diagnostic reasons on Move conflicts during index evaluation or exclusive Loans.

Implemented §4.8 and aligned old rejection tests for static Non-Copy element acquisition, self-assignment, and sibling assignment with supported cases or double-Move rejection. Existing numeric-update/simple-assignment evaluation order and exclusive-Loan rules are unchanged.

| Check | Result |
| --- | --- |
| Solution build (Debug / Release) | Both passed with 0 warnings and 0 errors |
| Managed tests (Debug / Release) | 4,681 passed in each; 0 failures/skips. Added 70 ElementMoveEmissionTest cases and merged three former rejections into new success cases |
| Warm allocation | Bind and Ownership plus IR generation with branch-dependent partial Moves and incomplete-parent replacement each measured 0 additional B over 128 iterations |
| Sparse Move Paths | A declaration with one million elements uses two paths: the root and the referenced element. Acquisition before initial construction is rejected as UninitializedUse |
| ElementMoves CLI build/run | Release/O2 passed; printed `taken`, then `element moves complete`, and exited with 0 |
| NativeAOT and new throughput benchmarks | Not run |

All 311 Element fixtures passed the LLVM verifier and ordinary native O0/O2 execution: 622 runs, comprising 65 partial-Move fixtures/130 runs and 246 existing element-operation fixtures/492 runs. Also checked destruction counts/order, bounds/arithmetic Abort, and nontermination with timeouts. All 311 Element IR files from the final Debug/Release rerun matched the native verification inputs by SHA-256. Fixture generation and native execution run serially. Reproduce with §7.2's build→test sequence, then `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'`. Destruction audits verify logical responsibilities, including Static-backed strings; they do not establish a source API for Heap-string construction.

### 7.5. Static String Element Borrowing (2026-09-15)

Implemented §4.9 and converted ElementMoveEmissionTest's former rejection tests for all six comparisons/shared arguments into success tests. Added 78 ElementBorrowEmissionTest cases. Semantic rules in the specification body are unchanged; aligned the SPEC index and executable-example references.

| Check | Result |
| --- | --- |
| Solution build (Debug / Release) | Both passed with 0 warnings and 0 errors |
| Managed tests (Debug / Release) | 4,759 passed in each; 0 failures/skips |
| LLVM verifier and ordinary native O0/O2 for element borrowing | 47 fixtures, 94 successful runs. Includes mixed guard candidates, destruction audits, bounds Abort in later indices, and nontermination with timeouts |
| Native regression for existing element operations | 311 fixtures, 622 successful runs. Combined with new borrowing: 358 fixtures/716 runs |
| Warm allocation | Bind and Ownership plus IR generation with partial Moves, sibling updates, and defer each measured 0 additional B over 128 iterations |
| ElementBorrows CLI build/run | Release/O2 passed; printed `shared element`, `beta`, then `element borrows complete`, and exited with 0 |
| NativeAOT and new throughput benchmarks | Not run |

A new early-return audit corrected expected Tuple destruction order to reverse logical order. Final review used a structural test to reproduce incorrect address selection from an outer Loan when reading a guard candidate as a shared argument during element borrowing. Selection now depends on whether the reference itself has a projection. Added two guard execution cases and a structural test, then reran the full Debug/Release suites and element-borrowing native checks.

After verifying 356 fixtures (311 existing element-operation and 45 initial borrowing fixtures) with `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'`, confirmed that all 356 IR files regenerated by the final Debug/Release runs matched those native inputs by SHA-256. The final 47 borrowing fixtures were reverified with `-FixturePattern 'ElementBorrow*.ll'`. Counts reflect results after fixes; repeated runs of the same fixture are not counted again in the 358-fixture total. Managed fixture generation and native verification ran serially. Reproduce with §7.2's build→test sequence, then the script for all Element fixtures. Audits verify logical destruction responsibilities for Static-backed strings; they do not establish a new Heap construction API.

### 7.6. Element Borrowing from Owned Parameters and Temporaries (2026-09-15)

Implemented §4.10. [ElementBorrowOwnerEmissionTest](xUnitTest/Tests/ElementBorrowOwnerEmissionTest.cs) initially had 61 cases (now 60 after moving one former parameter partial-Move rejection in §4.11). Moved two former parameter/literal rejections from local-borrowing tests and added successful comparisons/shared arguments, owner plans/initialization, conditional cleanup, ownership retention, destruction counts/order, normal transfers, Abort, nontermination, rejection of modified plans, and recovery by reanalysis.

| Check | Result |
| --- | --- |
| `dotnet build Kimigayo.slnx --no-restore` | Debug / Release both passed with 0 warnings and 0 errors |
| Full managed suite | Debug 4,818 / Release 4,818 passed; no failures/skips |
| LLVM verifier and ordinary native O0/O2 for new owner borrowing | 57 fixtures, 114 successful runs. Includes conditional production, iteration, defer duplication, destruction audits, Abort, and nontermination with timeouts |
| Full Element native regression | 415 fixtures, 830 successful runs, including the 57 new fixtures |
| Native regression for shared construction, result delivery, and function ABI | 157 Aggregate fixtures, 314 successful runs. Combined with Element fixtures: 572 fixtures/1,144 runs |
| Low allocation | Warm Bind and Ownership plus IR generation with parameters, call/selection results, conditional temporaries, and defer each measured 0 B over 128 iterations |
| CLI example | ElementBorrowOwners Release/O2 build/run passed, with the README's four output lines and exit 0 |
| NativeAOT | Not run, as instructed |

During implementation, fixed missing live-flag activation at CompleteConstruction for conditional Tuple construction, and erroneous rejection of cleanup covering short-circuit paths that bypass a selection result's Declare. Tests also check missing/incorrect/duplicate flags and rejection of location formation/destruction during result cleanup. The Abort audit's expected position was corrected to the actual source column. Counts reflect results after fixes; repeated executions of the same fixture are not added to the total.

Reproduce with §7.2's build→test sequence for each configuration, then `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'`, followed by the same script with `-FixturePattern 'Aggregate*.ll'`. Managed fixture generation and native verification ran serially. All 2,860 files regenerated by the final Debug/Release runs—IR, expected stdout/stderr, exit, and timeout—matched the saved native verification inputs by SHA-256. Added arrays reuse capacity; throughput improvement has not been measured. Audits cover logical responsibilities for Static-backed strings.

### 7.7. Partial Moves from Owned Parameters (2026-09-15)

Implemented §4.11. [ElementParameterMoveEmissionTest](xUnitTest/Tests/ElementParameterMoveEmissionTest.cs) has 54 cases, including one former parameter partial-Move rejection moved from each of ElementMove and ElementBorrowOwner. Reused semantic-state initialization on parameter receipt and added the missing generated-code path-flag initialization.

| Check | Result |
| --- | --- |
| Solution build | Debug / Release both passed with 0 warnings and 0 errors |
| Full managed suite | Debug 4,870 / Release 4,870 passed; no failures/skips |
| New parameter partial Moves | 51 fixtures, 102 successful O0/O2 runs |
| Native regression for partial Moves | `Element*Move*.ll`: 122 fixtures, 244 successful runs, including the 51 new fixtures |
| Aggregate function ABI native regression | `AggregateFunction*.ll`: 59 fixtures, 118 successful runs. Combined with partial-Move checks: 181 fixtures/362 runs |
| Low allocation | Warm Bind and Ownership plus IR generation with conditional nested Moves and borrowing of remaining elements in defer each measured 0 B over 128 iterations |
| CLI example | ElementParameterMoves Release/O2 build/run passed, with the README's three output lines and exit 0 |
| NativeAOT | Not run, as instructed |

Verified reverse logical destruction order, remaining responsibilities, joins between conditional and whole Moves, nested aggregates/arrays, zero-sized semantic state, borrowing conflicts, loops/defer/unreachable code/covered arms, transfers during evaluation, Abort, and nontermination. Also checked parameter immutability; missing/incorrect/duplicate initialization flags; rejection of modified receipt/Move plans; and recovery by reanalysis. Structural tests confirm use of the parameter's actual argument address, with no additional whole-parent transfer or parameter-local slot.

Reproduce with §7.2's build→test sequence for each configuration, then `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*Move*.ll'`, followed by the same script with `-FixturePattern 'AggregateFunction*.ll'`. Used fixtures generated by the final Release test; generation and native execution of the same fixtures ran serially. Initial partial runs are not counted again in the total. Audits cover logical responsibilities for Static-backed strings; throughput improvement has not been measured.

## 8. autoframe Execution Infrastructure

For the current Windows automation infrastructure, see the [autoframe specification](autoframe/SPEC.md), [usage guide](autoframe/README.md), and [verification record](autoframe/VERIFICATION.md). The old autoframe.md/autoimpl locations and early stage counts/verification coverage are historical; those documents now track current support. This documentation-alignment work did not run or reverify the infrastructure.

Further review corrected partial acceptance, missing recovery targets, corrupted/expired evidence, audit/stall detection, stop confirmation, and instruction-file protection/signing. Verified 56 simulated tests in total, including 17 added tests. The actual CLI was not retested in this work.

### Numeric literal replacement

Metadata-preserving syntax replacement retains the numeric literal spelling used for fitting and writing while keeping the original diagnostic location. Ordinary parsed literals borrow their source snapshot; only relocated numeric nodes materialize a spelling string. NumericReplacementTest covers integer/float boundaries, repeated replacement, lazy float parsing, rebinding, and zero measured warm Binding allocations. Five generated fixtures passed LLVM verification and ten Windows O0/O2 executions. This does not add Mod APIs or persistence of edited syntax trees.

### Unknown Attribute validation

Wrong-target Test and non-function LibraryImport markers, and selected unrecognized Attributes, invalidate their target declarations before conformance certification, including chained and parameter Attributes. Syntax remains readable during provisional Binding; removing the marker and rebinding restores valid certificates. Directive-excluded markers remain excluded. Earlier entries in a selected Attribute chain are checked independently of an invalid or completed marker. Conformance and Property/accessor certificates also require valid enclosing declarations; unrelated siblings remain independently checked. Enum construction completion also rejects invalid owner contexts and restores reusable plans after a valid rebind. Constraint proofs also reject invalid enclosing Type/Contract contexts before identity or assumption success, including nested Type arguments. No Mod marker recognition API is introduced; LibraryImport retains its separate implementation boundary.

### Generic signature certificate validation

Property/accessor and function-conformance certificates validate nested constructed-Type input constraints before publication, including retained projection qualifier inputs erased by normalization. Property certification follows final Origin/API validation and precedes final conformance verification, preventing late-invalid signature Types or owners from retaining certificates. Valid dependent signatures use their definition-side evidence; rejected Types remain readable for recovery. Binding safely rejects malformed computed/Contract Properties missing the mandatory getter, retaining the parser diagnostic instead of crashing. Enum acquisition plans likewise validate nested constraints in result and payload Types before becoming queryable; enum payload declarations retain and validate projection qualifier inputs before final certification. Generic function applicability validates complete explicit and inferred Type arguments before publishing a call plan, including nested constraints and caller-provided evidence. Call candidates also validate the target declaration context and definition-side signature constraints, including nongeneric signatures. Final Binding completion rechecks selected targets and substituted result/argument/receiver/declaring Types after late API or constraint validation; constrained declaring Types are also checked during candidate selection. Pattern subject/payload constraints are checked before coverage, including wildcard/binding positions, and rechecked after late declaration validation. Invalid Types cannot retain successful coverage; valid dependent patterns keep definition-side evidence. Coverage warnings and missing-coverage diagnostics are published after late Pattern Type validation, preserving ordinary valid-Type coverage failures and independent body-error warnings. Explicit/default module aliases and module reparsing are covered by integration tests. Conformance certificates also require nested input constraints in both normalized associated-Type definitions and their retained pre-normalization inputs, using the path's own declaration and conditional evidence. Base generic input constraints, including retained projection qualifiers erased by normalization, are checked after capability evidence becomes available, and invalid bases propagate through descendants before Property/conformance certification. Runtime Type-test plans validate operand/target input constraints before publication and after late declaration validation. General generic body proofs and specialization remain incomplete.

### Inherited declaration Names

Binding rejects a derived Value-role Name that matches any accessible ancestor member under SPEC 6.2, regardless of signature, conditional premises or narrower derived access. Protected checks use the derived receiver; Property access is independent of accessor visibility. Inaccessible private Names and same-layer overloads remain available. Checks run on merged fragments and propagate invalid bases to descendant certificates. Cross-module aliases/reparse and protected/internal access distinctions are covered, along with imported associated-Type/base/runtime-test input validation. Existing general receiver/effect and generation limits remain.


## Superseded planning wording retained for migration traceability

The following pre-migration paragraphs or tables differ from the current plan. They are preserved exactly, including obsolete scope, proposed paths and procedures; the correction table above and current PLAN take precedence. This section is historical, not additional current requirements or next actions.

Focused independent reproducers are saved as `remaining-enum.kimi`,
`remaining-iteration.kimi`, `remaining-callback.kimi` and `remaining-destructor.kimi`
with corresponding logs in the evidence directory. The enum reproducer fails
ownership; the other three pass ownership and reproduce separate shared-generation
boundaries. Those are **isolated reproducer failures**, not evidence that the
Milestone 9 target has reached generation. Do not advance to another program.
## Program Milestone 10 integration (2026-09-17, complete)

The initial planning phase changed only `PLAN.md`. Subsequent explicit implementation requests authorize the unfinished in-scope work and necessary tests/documentation under `prompts/implementation-execution.md`. Preserve user changes and do not edit `draft/`. NativeAOT is neither run nor a completion requirement. Ordinary LLVM/native execution is a separate verification layer. A user-specified time budget applies to its own execution; earlier 60-minute deadlines are historical.

This file is the authoritative execution record for this effort. Its baseline must not be weakened to match implementation limitations. Future product-wide support summaries belong in `STATUS.md`; detailed execution history belongs here.

1. `SPEC.md` identifies the owning chapters. §1.3 makes unqualified rules normative, distinguishes mandatory requirements from recommendations, and says examples do not add rules.
2. Appendix A is normative verification/implementation guidance. Appendix B is optional algorithms; Appendix F is a non-normative syntax summary. `STATUS.md`, code, tests, and program milestones are evidence, not language authority.
3. Honor explicit supersession: §21.3 integrates the adopted generic sharing/specialization design; integrated dependency/artifact rules are identified by `SPEC.md`; §17.5 supersedes the testing draft's earlier `$require` return behavior. Read a referenced design only if an owning section leaves an authority question; do not edit it.
4. Appendix D and owning sections delimit exclusions. “Specified, not implemented” remains in scope. “Deferred design” and undefined public APIs do not become requirements merely because syntax/examples exist.
5. Specifically include settled object/Weak operations (§13.5.8–9), collection mutation (§4.7), source packages/local publication (§18), and basic test semantics. Old implementation comments calling these unspecified do not supersede the specification.
6. Exclude Composition Root Entry/Provider selection, runtime Contract Views, source concurrency, extra target profiles, stable external Kimigayo/DLL ABI, source transparent aliases, general user-defined arithmetic, extra generic/specialization forms, persistent generation/object-code caches, dynamic generic scratch allocation, and other Appendix D extensions.
7. Exclude undefined raw-storage acquisition/allocation/reference-conversion APIs (§5.6), exchange API spelling (§15.7), checked-cast/exact-test public spellings (§13.6.2), and string concatenation acquisition (§13.3). Preserve their settled constraints without inventing executable syntax. The mention of concatenation in §22.1 does not override §13.3's explicit executable-finalization prohibition. Interpolation and its Stringify contract are independently specified and included.
8. Undefined Mod host interfaces/configuration and test profile interfaces are separate design work (G1–G2). Their settled semantic rules remain baseline requirements; dependent public integration cannot be declared complete by inventing interfaces. Dynamic collection/Slice internal representations may be proposed within settled contracts; a fixed public collection ABI is excluded (G3).

The milestone designs below define the authorized scope; their current execution states are in Sections 2 and 7. Directory names refer to existing directories. Existing files/symbols are reused unless explicitly marked **new**. A family is complete only when its remaining finalized input range is supported and its mandatory negative boundaries still reject. Tests of known behavior are regression gates, not redundant implementation tasks.

- Objective: R1–R5, R19, R27. Audit existing coverage and close actual gaps in function defaults, candidate equivalence/inference, complete-Type/Origin formation, source access, standard operations, static Contract witnesses and obligation deadlines.
- Dependencies: M1. General runtime/effect obligations may remain explicit until M3; they cannot certify completed executable use.
- Files: `Binding.cs`, `Binding.Expressions.cs`, `Binding.Calls.cs`, `Binding.CandidateEvaluation.cs`, `Binding.ArgumentOperations.cs`, `Binding.Contracts*.cs`, `Binding.Constraints*.cs`, `Binding.Properties*.cs`, `CoreIntrinsics.cs`, relevant Koto/parser/diagnostic files only when syntax or diagnostics actually require changes.
- Work: I3–I5. Retain receiver/argument/default evaluation plans and original defining environments. Add new Core declarations together with their operations rather than empty library promises.
- Preserve: winner-only commit, lookup stopping, no usage-failure overload fallback, independent proof paths, source-order independence, immediate directives.
- Complete: T1–T5 and T27 via V3; every finalization obligation either discharged or a legitimate representation obligation with a deadline. R4 default evaluation then integrates through M3/V5.
- Risk: recursive proof stabilization and stale absence facts. Reuse bounded proof scratch/worklists; avoid repeated whole-world fixed points where reverse dependencies suffice.

- Objective: R5, R13–R14 and generic forms of earlier milestones.
- Dependencies: M2–M5. Start definition-side proof work after M2/M3; no need to wait for runtime objects or dynamic collections.
- Files: existing generic/constraint/Origin Binding files, `OwnershipModel.cs`, `LlvmEmitter.cs`, `AggregateLayout.cs`, `FunctionAbiPool.cs`, `EmissionModule.cs`; **new** `GenericGenerationPlan.cs`, `GenericContextSchema.cs`, `GenericFramePlan.cs` under `Kimi/Compiler/Emission/`.
- Work: I13–I16. Verify symbolic Copy/Move, effects and cleanup universally; close explicit-specialization sets; form semantic/selection/body/entry/context keys with distinct roles. Worklist closed substitutions; substitute verified plans without cloning/rebinding syntax. Validate immutable 8-byte GenericContext slots and operation/context pairs, fixed facts, finite recursive graphs, caller-facing ABI and exact per-entry scratch reservation.
- Preserve: explicit selection at budget zero, Origins in proofs even when erased from representation keys, full-width Type tokens, no dynamic scratch layout/alloca/heap fallback, no optimization-induced acceptance change, separate mandatory resource limits.
- Complete: T13–T15/T27 and M5's remaining generic T12 cases via V3–V6/V9. Baseline shared code and adapters must work before bounded optional specialization; measure generation/code-size/runtime tradeoffs. Adding another instance cannot inflate an existing exact frame.
- Risk: exponential key growth or schema/ABI confusion. Require deterministic bounded worklists and separate diagnostics for invalid recursive inline layout versus finite cycles versus required resource exhaustion.

- Objective: R16–R17 plus fixed-array portions of R13 and the supporting Core identities/witnesses of R19.
- Dependencies: M3, M5, M6; existing fixed-array paths are the starting point. M9 dynamic collection adapters follow later.
- Files: `CoreIntrinsics.cs`, `Binding.Elements.cs`, `Binding.Lengths.cs`, `Binding.ArrayInference.cs`, `ForKoto.cs`, `OwnershipAnalysis.Elements.cs`, `BodyLowering.Elements.cs`; **new** `Binding.Iteration.cs`, `OwnershipAnalysis.Iteration.cs`, `BodyLowering.Iteration.cs`, **new** sequence-view physical records as needed under Emission.
- Work: I19–I20. Canonical Index/Range/ResolvedRange/Slice/Iterator/Iterable declarations, their required Copy/Owned/Equatable witnesses, checked access plans, metadata snapshots and consuming/external-borrow iteration. Establish the minimal Equatable identity/mapping prerequisite here using I5, without waiting for M9's general comparison/Dictionary support. Lower for using selected contract operations and existing cleanup/CFG mechanisms, without source desugaring that changes contexts or Name lookup.
- Preserve: RHS-first assignment, exclusive activation timing, no intermediate array Copy, no constant-folding expansion of Move paths, empty/zero-size Slice provenance, next Loan ends before body.
- Complete: T17–T18 via V3–V5/V9; unchanged program 7 passes native O0/O2 plus invalid/Abort variants. Support both built-in and user-defined conformances, not just a special-case numeric loop.
- Risk: iterator-owned reference escape and excessive temporary materialization. Require no element-proportional storage for views/iteration.

- Objective: R18–R19; complete the settled Core surface together with its dependencies.
- Dependencies: M2–M8 for witnesses, general values, returned borrows, iterators and generic cleanup.
- Files: `CoreIntrinsics.cs`, `Binding.Comparisons.cs` if a split is needed (**new**; current comparisons are in existing Binding expression/call files), `Binding.Expressions.cs`, `BodyLowering.Strings.cs`, `WindowsRuntime.ll.in`; **new** `CoreCollectionOperations.cs`, `BodyLowering.Collections.cs`, `CollectionLayout.cs` under compiler Binding/Emission as appropriate.
- Work: I21–I23. Canonical Array/Dictionary APIs and effects, explicit internal allocation/relocation/cleanup plans, literal duplicate checks, insertion-order mutation/iteration. Add Equatable/Comparable and Stringify witnesses plus owned interpolation output. Keep float Contract equality distinct from IEEE operators; document implementation-defined float formatting.
- Preserve: no blanket Owned constraint on collections, no dependency subtraction on clear/None/Err, no allocation within capacity including churn, growth bound, preserved placement on failed shrink, scalar/string operations already working.
- Complete: T19–T21 via V3–V5/V9; counts/instrumentation prove allocation and amortized obligations in addition to timing. String concatenation and extra APIs remain excluded.
- Risk: equality reentry/unstable keys must not break memory safety; whole-collection Loan/effect plans and storage implementation must be integrated, not validated in isolation.

- Objective: R20 with settled runtime metadata and ownership intrinsics.
- Dependencies: M3–M7; cyclic factories require consuming Callable and enum Option results.
- Files: `CoreIntrinsics.cs`, `CoreDeclaration.cs`, `Binding.RuntimeTypeTests.cs`, capability/Origin/effect files, `WindowsRuntime.ll.in`, `WindowsLowering.cs`; **new** `ObjectLayout.cs`, `BodyLowering.Objects.cs`, `ObjectMetadataPlan.cs` in Emission.
- Work: I24–I25. Concrete object payload eligibility and view adjustment, obj/rc/arc creation and sharing, Weak table/count migration, downgrade/upgrade, irreversible last-strong destruction, cyclic Building→Alive publication after builder cleanup, Runtime Type Identity and flow refinements.
- Preserve: complete dynamic destruction, no source-handle Loan retained by clone, no resurrection, count overflow Abort, payload +16 profile, Owned constraints only at specified erasure/cyclic boundaries, no source concurrency feature.
- Complete: T22–T23 via V3–V6/V9. Native tests/fault adapters plus separate memory-order argument for atomic protocols; O0/O2 tests alone do not prove weak-memory correctness.
- Risk: count/table lifetime bugs and incomplete public ObjectCompatible status. Release/upgrade/count/cleanup changes must land together for each ownership mode.

All commands below use working directory `C:\Users\bwff1\repos\archi-Doc\Kimigayo`. They are future commands unless a V1/V12 record says otherwise. Command paths, script parameters and runner switches were checked against source/help; execution success is **not** implied. SDK observed: 10.0.401. Projects target net10.0; `global.json` selects Microsoft.Testing.Platform but does not pin an SDK version. Packages include xunit.v3 4.0.1, Microsoft.NET.Test.Sdk 18.10.0 and BenchmarkDotNet 0.15.8.

| ID | Command / exact target and prerequisites | Success evidence / order / output constraints |
| --- | --- | --- |
| V1 | `git rev-parse HEAD`; `git status --porcelain=v1`; `dotnet --version`; `dotnet --list-sdks`; inspect global/project/profile files. For fresh runner help use `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll --help` only as a help probe | Record HEAD/diff, SDK/tool existence and actual errors; help exit 1 is not a failed test. Resolve G5 before restore. Validate tools/archive hashes against profile before native work; existence is not integrity evidence. |
| V2 | `dotnet restore Kimigayo.slnx`, then `dotnet build Kimigayo.slnx -c Debug --no-restore`, then Release equivalent | Restore/config access and package sources available. Zero build errors; investigate warnings/new warnings. Hash actual compiler/test DLLs and source inputs. Build sequentially; do not use old bin output if either build fails. No publish/AOT. |
| V3 | After matching V2: `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -class XunitTest.StructEmissionTest -parallelMode none -failSkips` as initial focused check; substitute exact T-table class for each item. Full gate: `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -parallelMode none -failSkips`; repeat using Release path | Managed checks and fixture generation are distinct results. All selected cases pass, count >0, no unexplained skips. Inspect T-specific tests' side effects before running. Log exact classes/methods and counts; full suite only at M1, broad semantic changes and M15 or a justified concern. |
| V4 | LLVM verification after V3 generation: `./toolchain/opt.exe -passes=verify -disable-output bin/scalar-fixtures/StructLocal.ll`; future fixture paths recorded per test. `./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'Struct*.ll'` performs pre/post-O2 verification as part of V5 | Pinned LLVM 22.1.8 and a freshly regenerated exact fixture set required. Record IR hash and compiler/test configuration. LLVM verifier PASS does not prove ABI/execution. New fixtures require matching generated names, not an assumed glob. |
| V5 | `./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixturePattern 'Struct*.ll'`; replace pattern with exact generated family; final relevant fixture set `*.ll` only after freshness inventory | Pinned installed backend and dlltool hashes; script verifies O0/O2, inspects undefined symbols, links `/NODEFAULTLIB`, executes and checks stdout/stderr/exit/timeouts. `bin/scalar-fixtures` and `bin/scalar-native` are **shared across Debug/Release**: generate → hash/inventory → execute → archive evidence before switching configuration. |
| V6 | `./backend/windows-x64/build.ps1 -LlvmBin ./toolchain` when backend changes or pinned candidate prerequisite is missing; then `./backend/windows-x64/test-emission.ps1 -ToolchainRoot ./toolchain -Configuration Debug` and Release after fresh MinimalEmissionTest fixtures | Inspect build script adoption/install side effects first; it can install a matching tested reproduction. Clang/llvm-lib/llvm-objdump plus normal tools required. No unpinned override for conformance. Candidate archive/report must match; verify C ABI/layout, unwind, no hidden CRT dependencies and runtime fault adapters. New T24 adapters/commands must be added and verified before calling them available. |
| V7 | After V2: `dotnet Kimi/bin/Release/net10.0/Kimi.dll restore examples/SourceDependencies/Geometry/Geometry.kimiproj`, then `dotnet Kimi/bin/Release/net10.0/Kimi.dll check examples/SourceDependencies/Geometry/Geometry.kimiproj --locked`; native CLI regressions: `./backend/windows-x64/test-cli.ps1 -ToolchainRoot ./toolchain -Configuration Release` | Restore intentionally writes the example lock; check must not rewrite it. Use an isolated copy when preserving an existing example lock matters. Paths verified against examples/SourceDependencies/README.md; script parameters inspected. After M12 add new isolated package/CLI tests rather than invoking unregistered commands now. Validate diagnostic category, failed publication, hashes and stale-artifact rejection. |
| V8 | V3 with `-class XunitTest.ModuleBindingTest`, `-class XunitTest.DependencyLockTest` and `-class XunitTest.KotonohaSerializationTest` (OR filters); after I29 use **new** SemanticReuseTest/PackageStoreTest classes | Same exact source/settings exercised cold, warm, reload, replacement, relocated and corrupt-input modes; compare acceptance, required diagnostics/current spans and selected results. Package/user-cache tests use isolated test-owned roots, not real publication stores. Record actual new filters when created. |
| V9 | `dotnet run --project Benchmark/Benchmark.csproj -c Release -- --filter '*BindingBenchmark*'`; choose existing workloads by inspected Benchmark/Program.cs; new feature workloads added under Benchmark/Benchmarks | Build/restore environment ready, stable machine/load/settings, no concurrent builds/native tests; BenchmarkDotNet MemoryDiagnoser already configured. Record samples, variance, allocated bytes, retained/peak memory, cold/warm/reparse costs, source scale, IR/code size and runtime separately. New collection/generic instrumentation measures required operation/allocation counts, not just elapsed time. |
| V10 | After I30: V3 with **new** VerificationOperationTest/TestDiscoveryTest; after G2/I31: **new** TestRunnerTest using adopted CLI/transport arguments | Commands/fixtures for public runner remain NOT_RUN/BLOCKED until defined; no fabricated flags/defaults. Verify active-case protocol, all-body list/filter semantics, finite recovery, logs, failure latching, final display and artifact identity. |
| V11 | `./backend/windows-x64/test-milestone6.ps1 -ToolchainRoot ./toolchain -Configuration Debug` then Release; same verified parameter shape for scripts test-milestone1.ps1 through test-milestone5.ps1. **New** scripts for programs 7–9 only when implemented | Exact original and renamed/changed inputs, invalid/Abort cases, source/compiler/build identities; preserve expected program outputs in milestones/README.md. Integrates earlier V checks, does not replace Appendix A coverage. Add current Windows native CI evidence without package publishing. |
| V12 | `git diff --check`; `git status --short`; inspect PLAN requirement/item/test references, file/symbol existence and new markers | Planning/documentation-only gate: only PLAN.md differs; 12 required sections, no implemented support inferred from plans, no NativeAOT or draft edits. Repeat after plan edits. |

### Original test-family table and M7 file proposal

Historical planning definitions. Current test purposes/acceptance remain in PLAN §8, while states are maintained only in PLAN §2. File proposals below predate the existing scalar-default/enum/closure implementation.

| ID / requirements | Input/setup and expected result | Stage/evidence | Existing or proposed location |
| --- | --- | --- | --- |
| T1 / R1,R27 | UTF-8/NFC and malformed bytes, CR/LF, comments/indentation/body forms, exact numeric rounding, merged-source aliases; parse/write/reload retain value and original spans | P/B diagnostics and serialization evidence; no early rounding through f64 | SourceEncodingTest, UnicodeIdentifierTest, NumberLiteralParseTest, KotonohaSerializationTest |
| T2 / R2 | Reached invalid condition in unselected `#switch` arm diagnoses; false `#if` target exclusion remains distinct; selected defer/Names share scope; changed settings require fresh Compilation | P/B plus selected cleanup G/native | DirectiveConditionValidationTest, CompilationSpecificationTest, DeferredEmissionTest |
| T3 / R3 | Fragment/header/default-access/base/protected/API-domain variants, added inherited names and source-order permutations; reject restricted Types exposed in broader APIs | B diagnostic at declaration/use; repaired/reloaded input recovers | OriginFragmentBindingTest, InheritedNameBindingTest, FunctionApiCertificateBindingTest |
| T1a / R1, R27 (§2.5.1, A.3) | Positive: `struct abstract`, `let override: i32`, `func virtual(x: i32)`, `self.override`, standalone `abstract` expression followed by `let next: i32 = 2` all accepted. Negative: `abstract open struct S`, `virtual func f(...)`, `virtual init() => ()`, `override deinit => ()`, `abstract get` under a stored Property, `virtual` before a Contract requirement, and root-level `virtual func g()` each produce exactly one unavailable-feature diagnostic at the modifier, no cascade, and the following line still parses as an independent declaration | P diagnostics; DONE via unit 2: 40 new cases, baseline reproducer failures and full Debug/Release PASS | SpecReviewTest or FrontEndSyntaxTest (existing files); no new class needed |
| T3a / R3, R27 (A.1) | Two documents declare fragments of one struct; the first carries contradictory `T is i32` / `T is not i32` inputs. The container keeps its first fragment's SourceDocument and members retain their own. The independently required container diagnostic retains its original header span | DONE; source fixture updated in T5a because a missing-name cascade is no longer independently required | SourceDocumentAndDiagnosticTest.MergedContainerRetainsFirstFragmentSourceDocument |
| T5a / R5, R27 | Missing conformance Name, sibling Copy/Property checks, independent constraints/uses, both clause orders, fragment locations and rebind/reload | DONE; nine new cases and unit 3 evidence; diagnostic suppression never certifies a failed proof | UnresolvedConstraintBindingTest |
| T7a / R7, R27 | Projected ref/ref, uniq/uniq and uniq/ref calls retain Unknown/no-reselection and reject emission while missing effect verification reports Unsupported | DONE; three strengthened cases, CLI rejection and unit 6 evidence; public effect families remain T7/I7 | InheritedReceiverBindingTest |
| T27a / R1, R5, R23 | Append valid/invalid source through both entry points; replace with empty/nonempty snapshot; revoke retained certificates and require fresh analysis | DONE; seven new cases, full managed and native smoke evidence in unit 4 | MinimalEmissionTest, ContractBindingTest |
| T27b / R1, R23, R27 | Parse errors survive display clearing and duplicate-location reports with default/custom destinations; prior errors and warnings alone do not poison source | DONE; seven new cases, full managed and native evidence in unit 5 | MinimalEmissionTest |
| T4 / R4,R8 | Example A below plus named/default/receiver ordering, default borrowing, interrupted acquisition; count each evaluation once and clean acquired values in specified order | B/A plans, native log, failed default/use span | DefaultBindingTest, FunctionEmissionTest; **new** DefaultExecutionTest |
| T5 / R5,R19 | Kimi impostors/invalid shape, conditional witnesses, dependent projections, proof cycles/Unknown/Error, comparison mappings; declaration/order/reload variants keep same result | B certificates and diagnostic causes, no stale winner after repair | CoreCatalogTest, ContractBindingTest, ConditionalConformanceBindingTest, NormalizedConstraintProofBindingTest |
| T6 / R6–R7 | Move then read, partial Move/repair, branch/loop state, simultaneous ref/uniq and Reborrow; returned reference to local rejected, input-derived reference accepted | A at the invalid use/escaping result; native final cleanup after accepted borrow ends | OwnershipAnalysisTest, ElementMoveEmissionTest, ReferenceEmissionTest; **new** BorrowCompletionTest |
| T7 / R7 | Direct/recursive/indirect/imported callee mutates a static protected by a live Loan; changed callee effect invalidates use; independent shared read stays valid | B/A public summaries, caller diagnostics, reload/cross-module comparison | ModuleBindingTest; **new** EffectSummaryTest |
| T8 / R6,R8,R10 | Existing Resource fixtures plus base/derived construction, partial failure, field reordering and zero-size deinit; child/base destruction order observable once | B/A/TypeLayout/IR/native, C cross-check only for C layout | StructEmissionTest; **new** InheritedStructEmissionTest |
| T9 / R8–R9 | Existing checked arithmetic/conversion boundaries, short circuit, Never/Unit results, unreachable invalid use, defer divergence and Abort; acceptance unchanged by O0/O2 | Managed diagnostics then LLVM/native stderr/exit/cleanup | NumericConversionEmissionTest, WideIntegerEmissionTest, CurrentControlFlowTest, DeferredEmissionTest, AbortEmissionTest |
| T10 / R11 | Standard vs custom getter borrowing, private setter Move, Non-Copy self-assignment/custom setter, construction prohibition and Contract slot vs value witness | B/A check exact permission/Origin; one getter/setter call and cleanup observed natively | PropertyBindingTest, PropertyCompletionBindingTest; **new** PropertyEmissionTest |
| T11 / R11,R24 | Access a static twice, first-write replacement, A→B→A initialization cycle, unused invalid initializer, reverse successful-init shutdown and shutdown reentry | B/A reject invalid dependencies; native order/cycle Abort and no extra shutdown | **new** StaticInitializationTest / StaticEmissionTest |
| T12 / R12 | Example B, nested enum/Tuple/shared Subject and guard effects, moved payloads; `.Some(0)` plus `.None` alone must not prove full Option<i32> coverage; warned arms still checked | B/A coverage/warnings/spans, G tag/payload checks, native effect/destruction order | EnumBindingTest, EnumOwnershipTest, PatternCoverageCompletionTest, MatchEmissionTest; **new** EnumEmissionTest |
| T13 / R13 | Generic `func twice<T>(x: T) -> (T, T) => (x, x)` without Copy must fail universal ownership checking even if only i32 is requested; with `T is Copy`, legal uses succeed. N=0/negative/overflow formation, Type/pair slot and Origin bounds | B/A definition/use deadlines, symbolic plans, native instantiated values | GenericConstraintCertificateBindingTest, ConstantLengthBindingTest; **new** GenericBodyTest |
| T14 / R13–R14 | Example C, explicit specialization through generic forwarding/function reference, budget zero/nonzero and reordered inputs; same implementation/result/checks; invalid unused specialization rejected | B selection, G entry/context/schema and native result; no lookup in shared body | **new** GenericGenerationTest / SpecializationEmissionTest |
| T15 / R14,R28 | Same-size different-destructor/alignment Types, finite recursion vs growing keys, full-width u64 tokens, N=0/zero stride, independent product/test classes, new instance added | G validated pairs/layout/frame/code size, deterministic plans, finite resource diagnostic, V9 counts | **new** GenericFrameTest / GenericBudgetTest |
| T16 / R7,R15 | Function item and explicit capture list with Copy/Non-Copy/borrowed captures; each receiver kind, common-value erasure, nested unused captures and input-derived results; reject hidden-receiver/call-local escape | B/A capture/Loan diagnostics; G/native cleanup and empty/inline/heap storage | FunctionTypeConstraintBindingTest; **new** CallableEmissionTest / CaptureOwnershipTest |
| T17 / R16 | Length 0/1, ^0/^1, saved Index/Range, reversed/inclusive/max-isize, Slice split/reslice/try operations, dynamic Non-Copy Move rejection, empty Slice Loan, zero-size elements | B/A intended failure; runtime bounds reason/site, O(1) allocation/count evidence | ElementEmissionTest, ElementPathEmissionTest; **new** SequenceViewTest |
| T18 / R17 | Unchanged program 7; user Iterable/Iterator, tuple bindings, Copy vs consumed arrays, continue/exit/return cleanup, repeated None, reject direct Range iteration and iterator-owned result borrow | B/A and native stdout/remaining-element destruction; exactly one iterate and next per step | ForParseTest; **new** IterationEmissionTest; milestones/Milestone7.kimi |
| T19 / R18–R19 | Example D; literal duplicate `let d: Dictionary<i32, string> = [1:"a", 1:"b"]` fails at later key; replace second literal key by a function returning 1 and verify Abort before second value evaluation; float NaN/signed-zero keys | B literal diagnostic vs native duplicate check, equality call/effect order; no artificial Hash contract | CollectionLiteralParseTest; **new** DictionaryEmissionTest |
| T20 / R7,R18,R28 | Example E; reserve/add/remove churn at full capacity; removal results retain dependencies; mutation while even empty Slice lives rejects; forced shrink allocation failure keeps placement; cleanup-free clear cost | A and native fault/counter evidence; capacity bounds and amortized relocation/index work | **new** CollectionOwnershipTest / CollectionCapacityTest; Benchmark additions |
| T21 / R19 | Interpolation of integers, bool, char, Unit, string and custom Stringify; each value/mapping once, shared source stays initialized, owned output escapes source Loan; locale-independent documented float results | B witness and A borrow, native output/allocation/destruction order | StringLiteralParseTest, StringEmissionTest; **new** InterpolationEmissionTest |
| T22 / R20 | §13.5.8–9 normal/cyclic factories: distinct make identities, clone same identity, upgrade during Building None, after publication Some, after last strong None; count overflow/allocation failure via adapters | B/A, metadata, native count/final-deinit/free evidence and atomic-order review | RuntimeTypeTest, RuntimeTestFormationBindingTest; **new** ObjectWeakEmissionTest |
| T23 / R7,R20 | Runtime `is` branches, short-circuit joins and require refinement; Move/replacement invalidates facts; inherited receiver Whole replacement fails while legal Part replacement works; unknown family effect not Proven | B/A provenance and diagnostic use/cause; native exact/base view tests after runtime exists | InheritedReceiverBindingTest, RuntimeTypeTest; **new** RefinementCompletionTest |
| T24 / R21,R24 | Valid C-layout nested/zero-sized cases, mixed i8/u16/i64/f32/f64/pointer and 5+ args; reject bool/char/aggregate/i128 imported by-value signature and logical-library collisions. Legal null+0/one-past pointer paths only | B/G error location; Clang size/offset/call oracle, LLVM object/unwind/dependency inspection, native result | LayoutAttributeBindingTest, LibraryImportTargetBindingTest, NativeToolchainTest; **new** ForeignEmissionTest |
| T25 / R25 | Example F; condition once, message only false, message/control-boundary errors, nested/defer/test-only helper verification, product helper rejection; condition/message cleanup before require Abort only | B/A membership and source site; native stdout/exit plus retained failure/actual termination | ProductTestMembershipTest, TestAttributeCertificateBindingTest; **new** VerificationOperationTest |
| T26 / R14,R25,R28 | Add tests/generic requests without changing product plan; invalid filtered-out test still rejects; list runs no init/codegen; one artifact across filters; stale ID, exit 0 without completion, flood logs, timeout/cancel/descendant pipe | B/G immutable plan evidence and runner protocol/process recovery; finite memory/time/control reserve | **new** TestDiscoveryTest / TestRunnerTest after G2 |
| T27 / R1,R5,R13,R22–R23 | Existing Project dependency scenarios plus added/removed overload/Name/specialization, changed private effects/access/constraints/source spans, rebind/save/reload/repair; no stale proof accepted | Current-source diagnostics and cold/warm equivalence; native changed selected result after regenerate | ModuleBindingTest, DependencyResolutionTest, KotonohaSerializationTest; **new** SemanticReuseTest |
| T28 / R22–R24 | Malformed/Unicode-colliding/oversized/corrupt packages, different valid ZIP compression, release conflicts across whole closure, interrupted/concurrent publication, pin/GC races, stale native summaries/ambiguous required symbols | Loader/CLI failure stage, atomic files/table snapshot, bounded work; valid local source package compiles/runs | DependencyLockTest, EmissionArtifactsTest, NativeToolchainTest; **new** PackageStoreTest / PackageCommandTest |
| T29 / R9,R24,R27 | Existing Hello/UTF-8/NUL/runtime failures, unique startup/library/no entry, unused unsupported source, stale/tampered build inputs and build/run/emit distinctions | Exact native bytes/stderr/exit, pre-optimization Library IR, object/link records, no output after rejected generation | MinimalEmissionTest, StartupBindingTest; existing backend scripts |
| T30 / R26 | Existing provisional append/rebind transition plus separate generated contexts, dependency-order permutations, missing/cyclic Mod IDs, changed observed input and final unresolved query | B/provenance/record invalidation; public host tests only after G1 | ProvisionalContractPremiseBindingTest, SourceDocumentAndDiagnosticTest; **new** ModLifecycleTest |

- Files: `FunctionKoto.cs`, `Binding.Expressions.cs`, `Binding.CallableContracts.cs`, `Binding.FunctionWitnesses.cs`, `OwnershipAnalysis.*`, `FunctionAbi.cs`, `AggregateLayout.cs`, `EmissionModule.cs`; **new** `BodyLowering.Callables.cs` and `CallableLayout.cs` in Emission.

## Program Milestone 12: baseline and implementation checkpoints (2026-09-18)

<a id="program12-baseline"></a>

The new user request selects `milestones/Milestone12.kimi` and supersedes the earlier documentation-only task. It does not authorize programs 13–14. No historical time limit applies. At start, HEAD was `a9843eced89fc82b1ff79921e9a205fa9a0f0506`; only PLAN.md and STATUS.md were modified and PLAN_HISTORY.md was untracked. Their exact working copies, binary patch, status and hashes were saved in `bin/milestone12-work-20260918/baseline/`, `baseline.patch`, `baseline-status.txt` and `baseline-hashes.json`. The milestone input hashes are included. No implementation changes predated this task.

The repository contains 14 program milestones, despite the request describing five. All 14 were read. Programs 1–11 establish scalar/control flow, struct ownership and borrowing, value-producing control flow, arrays/Slice, shared generics, callbacks, patterns and specialization. Program 12 combines mutable snapshots, `Callable<uniq, S>`, nested concrete closures, common-function erasure and consuming owned captures. Programs 13–14 need general Iterator/object foundations and remain outside this execution. Program numbers are independent of plan M1–M15.

Baseline Debug build PASS: `bin/milestone12-work-20260918/baseline-build-Debug/20260918T0158029248250Z/result.json`. Focused baseline PASS, 60 tests: `bin/milestone12-work-20260918/baseline-focused/20260918T0202144144227Z/result.json`. The first target attempt is retained in `initial-target.log`: `InvalidConstraint_Kd` on `Callable<uniq, (i32) -> i32>`, unsupported inferred closures and cascading unresolved calls. These are observed Binding failures; ownership, ABI and native execution gaps were initially projections.

After the first implementation slice, Binding and ownership passed for the target; generation stopped with `Shared entry parameter requires a concrete owned storage representation.` (`target-closure4.log`). Focused run `closure-tests4.log`: 52 tests, one failure caused by an unnecessary string Read during capture acquisition; existing callback tests passed. Fixing that Read and the instantiated exclusive-reference entry gate is the next code checkpoint, not a recorded PASS. Build/test logs for intermediate changes remain under the same evidence root.

### Paused broader continuation: preserved pre-program-12 state

The following was the resumption state after the documentation migration. It is superseded as the active task, not cancelled. T4n-am remains TODO; its acceptance criteria are retained.

Documentation reconciled on **2026-09-18** at HEAD `a9843eced89fc82b1ff79921e9a205fa9a0f0506`. The worktree was clean before this migration; the old statements that implementation changes were uncommitted describe their historical runs. Only the three management documents are changed here.

Planning readiness: **PARTIALLY_READY** overall; **unit 52 is dependency-ready for a future implementation request**. M2/M3 and compiler completion remain IN_PROGRESS. Units 48–51 / I6/I8 T4n-ai–al are recorded DONE; unit 52 / T4n-am is TODO and has not started. No implemented unit is currently recorded IMPLEMENTED_UNVERIFIED. Historical intermediate states remain unchanged in the history.

The last execution completed verification at **2026-09-18 01:36:58 UTC** and stopped at **01:37:44 UTC**. Its 60-minute budget is historical. During this migration, read-only SHA-256 comparison matched **606 source**, **4 compiler/test DLL**, and **4,305 fixture input** identities in its frozen manifests. All 22 program reports exist and match their recorded source/compiler identities. This connects the saved results to the matching inputs; it is **not a new build, test, native run, or performance measurement**, and the manifest is not a claim about every file in the repository. See the [identity and evidence audit](PLAN_HISTORY.md#migration-audit) and [last execution](PLAN_HISTORY.md#execution-1).

### Exact next unit: T4n-am / unit 52 (TODO)

1. Inspect HEAD/diff and compare affected inputs with the last frozen manifests; preserve later user changes. Read SPEC §14.10.3/§15.1 and the existing `OwnershipAnalysis.ScopedChecking.cs`, `RecordTerminalSeed`, `ScopedCheckingProof`, checking seeds/arrivals and loop handling. Read only [units 48–51](PLAN_HISTORY.md#execution-1) for immediate implementation context.
2. Reproduce the saved completing-while boundary with the matching Debug compiler. The saved log reports `UnsupportedOwnership_Kd` at **11:13**; this migration did not rerun it:

```powershell
dotnet Kimi/bin/Debug/net10.0/Kimi.dll check bin/plan-execution/20260918-003029/next-terminal-while-body.kimi
```

If the binary inputs have changed, build under V2 first. If the ignored file is unavailable, recreate this exact source in a new execution evidence directory:

```kimi
func stop() -> Never => $abort("stop")
func f(c: bool)
    var x = 1
    do
        loop
            if c => return else => exit
            while c => return
            x = 4
        x = 2
        stop()
    let y = x
f(true)
```

3. Retain the skipped normal path and every terminal body history under its original mixed target and cleanup extent. Do not discard zero iterations, invent a runtime result, or admit body backedges without a fixed-point proof. Keep general iteration replay, unequal Loan joins, deferred cleanup and effectful divergence as separate incomplete obligations.
4. Add both original runtime paths, initialization/Move/let/Loan state, nested targets, cleanup and reload/warm reuse cases. Run focused managed checks first, then clean Debug/Release full suites and newly generated LLVM/native O0/O2 fixtures under V2–V5/V8–V9. Record exact selected test names, nonzero counts, diagnostics and input hashes. Keep IMPLEMENTED_UNVERIFIED until all required checks pass.
5. Before closing, compare retained fixture inputs and run affected program integrations (prioritize 1/8/9/11; use all completed programs when the shared control-flow change warrants it). Serialize all program build/run work across configurations: separate report roots do **not** isolate `milestones/bin/...` outputs. Record evidence and remaining work here; put execution details in PLAN_HISTORY.md and verified support in STATUS.md. NativeAOT remains excluded.

No new execution time limit is active. G1/G2 block only their public-interface slices; G3/G6/G7 remain investigations. There is no new user decision required for the bounded unit 52 design. Other outstanding obligations are the current I table above and §5.


## Program Milestone 12 completion (2026-09-18)

<a id="program12-completion"></a>

This execution completed program 12 only. Current disposition and resumption rules are in PLAN.md §2. SPEC.md and the milestone inputs were not simplified; no following-program-only feature was implemented.

### Changes and decisions

- Keep concrete closure identity and captured complete Types separate from common Function handles. Concrete storage is an alignment-sorted aggregate with no embedded function pointer; direct calls use a selected entry and borrowed environment address. Nested returned environments use ordinary result slots.
- Preserve immutable/mutable capture bindings and snapshot acquisition. Capture effects select Shared, Exclusive or Consuming access; exclusive access is protected across arguments, and consuming environments destroy only initialized remaining captures after body locals/defer and explicit parameters. Owned literal-backed strings remain Non-Copy.
- `Callable<uniq, S>` retains its receiver requirement and signature. The shared generic body receives a concrete entry adapter; it does not erase the callable or rebind the body for each type. Common erasure copies an already acquired, eligible inline environment and invokes a distinct environment-word adapter.
- Use the existing ownership CFG, aggregate layout, result slots, lifetime flags and native runner. General heap/borrowed/generic captures, owner witnesses, full signature inference and general erasure remain separate work. No new language specification was necessary.

### Failures, corrections and checkpoint provenance

1. The baseline rejected `Callable<uniq, (i32) -> i32>` and inferred concrete closures during Binding (`initial-target.log`). Adding only Binding was not treated as completion.
2. `closure-tests4.log` caught an unnecessary Read of an owned string during capture acquisition. Acquisition now uses its checked Consume plan; no comparison Loan is fabricated. `target-closure4.log` reached generation and exposed the instantiated exclusive-reference gate in shared entries.
3. LLVM verification then rejected `ptr 0` in a concrete call and an undefined `%p2` capture address. The writer now emits a proper null pointer and names environment addresses consistently with local scalar load/store paths. Empty concrete environments are passed without nonexistent storage. These failures were pre-runtime checks, not successful executions. `target-closure5.log` retains the pointer-spelling failure. The capture-address failure survives only as a tool-output excerpt in `intermediate-diagnostic-note.txt`; `native12-Debug6.log` is empty and its shared intermediate IR was overwritten. This evidence limit does not affect the independently retained final reports.
4. Checkpoint `focused7` passed 63 tests; six standalone fixtures passed 12 O0/O2 executions and `bin/milestone12/Debug/92098b2a2a3e43389929dd6b157db646/verification.json` passed 37 program checks. Later coverage added a borrowed exclusive receiver and repeated shared inspection of an owned string.
5. Full Debug/Release checkpoint suites passed 8,529 tests each. Additional review reproduced a real invalid-input acceptance: `ParenthesizedReceiver.kimi` built despite a let-owned Exclusive callable (`parenthesized-before.log`). The broad run was interrupted before program regressions completed; its identities were saved under `pre-receiver-fix/`. Receiver mutability now checks through parentheses, and ownership revalidates writable/exclusive access. A borrowed Non-Copy Consuming receiver also rejects explicitly. New negative cases raised the final managed total by two.
6. `closure-tests5.log` and `focused8` were inadvertently started before their solution builds completed; they are intermediate observations only. `closure-tests5-complete.log`, `focused8-complete`, and the final suites supersede them. They are not counted as final evidence.

### Final evidence

All final verification passed against the hashes in `bin/milestone12-work-20260918/final-source-hashes.json` and `final-artifact-hashes.json`: 8,531 managed tests per configuration; 1,058 program checks across Debug/Release programs 1–12; 117 standalone fixtures and 234 native O0/O2 executions in the ConcreteClosure, Callback, Generic, Borrow and Struct families. The unchanged target and renamed copies, changed values/names, repeated exclusive calls and ten target-derived invalid programs are checked by `backend/windows-x64/test-milestone12.ps1`. Exact output/exit/stderr and build optimization/toolchain identity are recorded, not inferred from IR.

See [verification.json](bin/milestone12-work-20260918/verification.json) for report paths, counts, hashes and final audit results; `final-tests-Debug.xml` / `final-tests-Release.xml` retain the managed cases. Native builds use LLVM verification before generation and O2 verification after optimization, followed by ordinary native linking/execution. No NativeAOT test was run. All required target checks were executable in this environment. No target blocker remains.

<a id="bounded-continuation-20260918"></a>

## Bounded loop/match/default continuation (2026-09-18)

The user authorized 30 minutes from 03:05:39 UTC, with no new work after 03:35:39 UTC, no execution of existing tests, no builds/checks of existing examples or milestone programs, and minimal new tests. The working tree was clean at HEAD `d7e42915a73c6c06be1ac4b455187eb07608c99e`. This request superseded the completed program-12-only execution restriction. No draft, example, milestone, SPEC, package or dependency files were changed. The normative requirements were already correct; no specification relaxation was needed.

Implemented slices:

- **T4n-am / unit 52, I6/I8:** mixed-target checking forks a completing while's skipped path from its terminal body. Caught exits retain post-cleanup arrivals; escaped histories retain their original targets. Same-loop continue is excluded by a retained visitor proof, rather than dropping a backedge.
- **T4n-an / unit 53, I6/I8:** the same proof admits completing loop bodies with no fall-through/self-continue, including scalar results and owned local cleanup. Existing seed/arrival buffers and LoopFrame records are reused.
- **T4n-ao / unit 54, I6/I8:** unguarded scalar/Unit match Subjects retain normal arm tails, terminal histories and caught named yields. Guarded/owned Subject replay is not certified. All-normal matches keep their existing closed CFG; only mixed terminal arms require separate checking branches.
- **T4o / unit 55, I4/I8:** the scalar-default certificate traverses unguarded scalar/Unit match Subjects and every arm in the declaration environment. Existing ownership, prepared argument storage and match lowering execute the selected default. Pattern-binding reads, guards and general owned defaults retain their guards.

Only the newly authored `TerminalWhileContinuationTest` and `ScalarMatchContinuationTest` classes were selected. The old `CompletingLoopContinuationTest.MixedTargetPropagationRemainsGuarded` expectation was updated and renamed to describe newly supported behavior, but that existing test was **NOT_RUN**. New cases cover zero iterations, caught/escaping effects, Move/let history, stored Loan conflicts, local cleanup/results, same-loop-continue rejection, serialization/reanalysis, and omitted/explicit match defaults.

Verification commands and artifacts (root `bin/plan-execution/20260918-030539/`):

Final result: Debug/Release builds PASS with zero warnings/errors; **25 new managed cases per configuration PASS**, zero failures/skips; **six native builds and six O0/O2 executions PASS**. `verification.json` records the exact selected classes, reports, source/artifact hashes, native results and NOT_RUN boundaries.

- Build compiler/test projects: `dotnet build xUnitTest/xUnitTest.csproj -c Debug --no-restore --nologo -v:minimal`, and the Release counterpart. Final build summaries are retained in the task's tool output; artifact hashes are in `verification.json`.
- Run new cases only: `dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -class XunitTest.TerminalWhileContinuationTest -class XunitTest.ScalarMatchContinuationTest -parallelMode none -failSkips -result-xml bin/plan-execution/20260918-030539/final-debug.xml`, and the Release counterpart. Exact names/counts/results are retained in both XML files.
- Build only the three new sources using `dotnet Kimi/bin/Debug/net10.0/Kimi.dll build <new-source.kimi> --ToolchainRoot ./toolchain` (O2) and their isolated O0 projects. Six native builds validate LLVM and link with pinned LLVM 22.1.8; six bounded native runs pass. True prints `early\ndone\n` and exits 0; false prints `late\n`, reports the expected Abort site and exits 1; the default fixture validates both omitted branches and the explicit argument, prints `match defaults passed\n` and exits 0. See `build-*.log`, build manifests and `native-results.json`.

Intermediate failures were in new test inputs: `exit: 3` was corrected to `exit 3` (§14.5.1), and an unlabeled yield in Discard Context was corrected to `yield to choice` (§14.5.2). Negative cases now also require no control-flow errors. A new static helper was moved to satisfy SA1204. An initial incompatible `dotnet test` wrapper invocation was stopped before any runner output/results; verification subsequently used the repository's direct filtered xUnit runner. These intermediate attempts are not PASS evidence.

Full existing regressions, existing program builds, broader native coverage, allocation/performance measurement and NativeAOT were **NOT_RUN**. Thus all four slices remain **IMPLEMENTED_UNVERIFIED** against full acceptance, while M2/M3 and the full compiler remain IN_PROGRESS. Detailed current states and exact resumption actions remain only in PLAN.md §2.

<a id="guarded-continuation-20260918"></a>

## Guarded match and Copy-tuple defaults (2026-09-18)

The next request dedicated 30 minutes to implementation, from **03:35:41 to 04:05:41 UTC**, followed by completion checks and documentation. New work stopped at that cutoff. Existing tests, including the preceding turn's two new classes, were not executed; existing examples/milestones were not built or checked. Baseline HEAD remained `d7e42915a73c6c06be1ac4b455187eb07608c99e`; the existing uncommitted implementation and documents were preserved. `bin/plan-execution/20260918-033541/baseline.patch`, `baseline-status.txt` and `baseline/` record the starting tracked changes/documents. No draft, example, milestone, dependency or normative specification files changed.

Implemented bounded slices:

- **T4p / unit 56, I4/I8:** default-expression eligibility now recognizes scalar guard candidates and distinct selected let/var bindings. Guard failure preserves effects; supplied arguments still bypass default evaluation while declaration checking remains independent.
- **T4n-ap / unit 57, I6/I8:** each arm starts from the join of the preceding pattern failure and completed false guard. Selected bodies retain separate histories. Contained guard exit/yield is admitted by a retained visitor that excludes outward transfers and unsupported divergence/cleanup.
- **T4n-aq / unit 58, I6/I8:** whole-string Subjects use that proof with existing guard protection, selected binding acquisition and Subject cleanup. No general owned aggregate decomposition was inferred from this slice.
- **T4n-ar / unit 59, I6:** reusable scratch storage groups seeds sharing both original and caught targets, avoiding multiplicative retained alternatives across successive guards. Synthetic joins have no runtime edges. Construction and Loan validation use replay-end states, including ended guard protection.
- **T4q / unit 60, I4/I8/I12:** scalar-only tuples may be prepared argument snapshots, literal/local Subjects or whole-pattern body values. Scalar-leaf reads and mutable local/body updates use existing projections. Checked generation now accepts a Copy from a prepared temporary only after validating declaration ownership, omitted-default extent, exact argument mapping, storage identity, initialization and dominance. Shared writable-root recognition admits var body patterns while excluding let and candidate symbols.
- **T4n-as / unit 61, I6/I12:** nested scalar/Unit tuple decomposition retains candidate/body identities and original terminal targets during mixed-target checking.

Failures and corrections were kept distinct from final evidence. The initial tuple checks passed ownership but failed generation with `Aggregate transfer requires distinct, Type-matched verified storage` (`tuple-initial-*.xml`); exact prepared-slot validation closed that path. A mutable whole-tuple body pattern exposed the Field-only writable-root gate (`mutable-initial.log`). A fresh scoped string guard exposed Loan validation comparing the original seed rather than the replay endpoint (`loan-initial.log`). Those were implementation gaps, not successful executions. SA1513 on the new pattern condition was corrected. An early filtered runner invocation before its build completed discovered zero new cases; it is not counted. Intermediate snapshots could contain different new-case counts while tests were being added; only final reports below certify the final source state.

Final focused evidence in `bin/plan-execution/20260918-033541/`:

- Debug/Release compiler/test builds PASS with **zero warnings/errors** (`debug-build.log`, `release-build.log`).
- Only `XunitTest.GuardedDefaultContinuationTest` and `XunitTest.GuardedMatchContinuationTest` ran: **28 cases per configuration, zero failures/skips** (`final-debug.xml`, `final-release.xml`). Commands use the direct xUnit executable with both `-class` filters, `-parallelMode none`, `-failSkips` and `-result-xml`; no existing test class is selected.
- New checks include false-guard Move/let/initialization history, caught yields, contained guard transfers, string cleanup, tuple decomposition/local updates, immutable binding rejection, independent default declaration checks and rejection of a changed prepared-argument identity. The 24-guard case passes checked IR and reports **zero allocated bytes for one warm ownership reanalysis after eight warmups**. No broader performance claim follows.
- Two fresh sources build and execute at O0 and O2 with LLVM verification and native linking: **four builds and four runs PASS**. The defaults program checks repeated/default/supplied paths and tuple snapshots/updates, then prints `Guarded defaults and tuple snapshots passed.\n`. The continuation program prints `chosen\nGuarded continuation checking passed.\n`. Every run exits 0 with empty stderr; `native-results.json` records executable hashes and exact output.
- `verification.json` binds the reports to final source/compiler/test/native hashes and build manifests. `git diff --check` passes. SPEC.md was unchanged because §§14.8.2–3, 14.10.3 and 15.1 already require these behaviors.

Existing regression suites, existing source-program checks, full Loan/cleanup coverage, broad allocation/retention measurements and NativeAOT remain **NOT_RUN**. Units 56–61 therefore remain **IMPLEMENTED_UNVERIFIED** against full acceptance, while the relevant M2/M3/M5 families remain IN_PROGRESS. Current states, dependencies and exact next actions are maintained only in PLAN.md §2.

<a id="continuation-verification-20260918"></a>

## Verification of existing continuation slices (2026-09-18)

The new request superseded the implementation-only windows: verify existing work, add missing tests, and fix relevant defects without starting new functionality. Existing suites and completed programs were authorized; unit 62, draft edits and NativeAOT remained outside scope. HEAD was `d7e42915a73c6c06be1ac4b455187eb07608c99e`. The prior uncommitted implementation was preserved; starting documents, tracked diff and milestone hashes are retained in `bin/plan-verification/20260918-041105/`.

### Failures and corrections

1. The baseline Debug suite executed **8,584 tests with one failure and no skips** (`baseline-debug.xml`). `BranchReplayContinuationTest` still expected a terminal `while` to be unsupported, contrary to implemented unit 52. The obsolete negative case was replaced by positive return/exit runtime fixtures; divergent and deferred cases still require rejection.
2. A new regression using a local tuple as a named argument passed ownership but failed generation (`focused-debug.xml`). Prepared-default reads recognized literal and returned storage but omitted acquired copies of locals/parameters. Lowering now records each Copy-tuple acquisition in the existing initialization array, rejects competing initializers and checks dominance without adding a new cache allocation.
3. Expanding the regression to an `if` result exposed the corresponding verified-result-storage omission (`storage-debug.xml`). Prepared Copy validation now admits an existing selection result only through its checked result lifetime and exact pending argument slot. Final default results remain scalar/Unit; no general aggregate default feature was added. Literal, local, returned, both selected branches, parameter forwarding, named order, repeated calls, snapshot preservation and projection-free tuple decomposition are covered.
4. `ContinuationVerificationTest` ultimately adds 16 cases, including mixed string/tuple checking with no runtime execution of checking regions, scoped false-guard destruction counts/order, serialized reload and warm analysis/emission, rejection of invalid replay endpoints, 4/16/64-guard history growth, and forwarded Copy/Move aggregate inspection. Existing wrong-argument-identity and immutable-pattern tests remain active.
5. The initial parallel solution build exited 1 with no build diagnostics (`build-debug.log`); serial MSBuild (`-m:1`) completed. Intermediate SA1117/SA1513 formatting warnings were corrected. Final Debug/Release solution builds report zero warnings/errors. Restore confirmed all projects were up to date; existing assets were used for the builds.
6. The first evidence-copy wrapper formed a doubled dot using PowerShell's empty extension conversion. This stopped before native execution; the successful managed run and file-write inventory were retained, and copying resumed with `GetFileNameWithoutExtension`. No failed native attempt was counted as PASS.
7. Reviewing the same initializer check exposed the pre-existing fixed-array variant: a typed local `[2 of i32]` passed final Binding/ownership but failed generation during default inspection (`array-default-before-typed.log`). The first reproducer lacked an explicit fixed-array Type and failed earlier Binding; that separate input error is retained in `array-default-before.log`. Initialization tracking now covers already-supported Copy/Move acquisitions of tuples and fixed arrays; whole-value copying inside defaults remains restricted to scalar/Unit tuples. New forwarded array, owned-tuple and owned-array cases include native destruction-count audits. The prior compiler/test identities and changed source files are preserved under `round1/`; subsequent final suites and program runs use the corrected compiler.

### Verification method

The direct xUnit runner uses `-parallelMode none -failSkips -result-xml`. The first full pass executed 8,598 tests per configuration; after the fixed-array correction, both final suites execute **8,601 tests with zero failures/skips** (`final/Debug/tests.xml`, `final/Release/tests.xml`). The earlier results remain separate records. Current source and compiler/test hashes are in `final/source-hashes.json` and `final/artifact-hashes.json`.

A file-system write-event inventory identifies fixtures produced by each suite. Each selected IR and all output/exit/timeout oracles are copied and hashed into an isolated configuration directory before native use. `test-scalars.ps1 -FixtureDirectory` accepts those directories; native outputs still run serially. The selected families are CompositePattern, Default, Element, Guard, Match, Never, StringGuard, VerificationContinuation and WholeValue: **1,076 final fixtures per compiler configuration**.

The first inventory passed **2,142 O0/O2 executions**. After the final fix, all 1,071 earlier IR/oracle sets remain byte-identical and five new sets pass **10 additional executions**, including owned aggregate destruction audits. All 1,076 final Release sets match the final Debug sets. `final/native-baseline-equivalence.json`, `final/native-release-equivalence.json` and `final/native-summary.json` record the mapping; identical inputs share native evidence and are not counted twice. Tool/backend hashes remain fixed. Programs 1–12 run sequentially across both configurations, in output directories separate from standalone fixtures. Every verification subprocess is bounded, and original milestone source/compiler identities are checked by the program scripts.

The five dedicated programs from the two preceding implementation windows were also copied byte-for-byte into isolated directories and rebuilt with each final compiler at O0/O2. **All 20 final builds/executions passed** (`final/checkpoint-programs.json`), superseding the first compiler's 20 checks. These retain terminal true/false behavior and Abort diagnostics, omitted/supplied match defaults, false-guard effects, mutable tuple snapshots, repeated defaults and string guard effects. The dedicated programs close native coverage that the earlier managed-only continuation cases did not themselves generate. SPEC remained unchanged: §7.2 already requires prepared-slot inspection and snapshot isolation, and normal destruction remains governed by §16.

The 4/16/64-guard workloads retain 115/367/1,375 seed/replay/region records in both configurations. Eight measured warm ownership analyses allocate **zero bytes** at each size; serialized reload followed by warm ownership/IR generation also passes a strict zero-byte check. The separate 64-analysis timing samples are recorded as observations under verification load, not statistically controlled throughput benchmarks. These results establish bounded workload coverage and stable record counts, not universal zero allocation, peak-memory bounds or full M15 performance closure.

### Final disposition

The [final audit](bin/plan-verification/20260918-041105/verification.json) passes: **8,601 managed tests per configuration, 2,152 standalone plus 20 dedicated native executions, and 1,058 completed-program checks across 24 Debug/Release reports**. Final program runs after the aggregate fix supersede the initial 1,058 checks. The audit rechecks 577 source/project/backend input identities, compiler/test and native-tool hashes, immutable fixture/oracle inventories, and all 14 original milestone source hashes. Debug/Release solution builds are warning-free. No draft/specification/dependency change or NativeAOT run was introduced.

Units 52–61 are DONE for their bounded acceptance criteria. M2/M3/M5 and their broader I families remain IN_PROGRESS; unit 62 and general terminal guards, loop backedges, unequal Loan joins, deferred cleanup, enum/non-Copy aggregate replay and general defaults remain unfinished. Current states and exact next actions are maintained only in PLAN.md §2.

<a id="terminal-owned-patterns-20260918"></a>
## 60-minute implementation: terminal guards and owned Patterns (2026-09-18)

The implementation window ran from **05:32:59 to 06:32:59 UTC** on HEAD `d7e42915a73c6c06be1ac4b455187eb07608c99e`, preserving the preceding uncommitted work. No new scope began after the cutoff; already-started checks and documentation were finalized afterward. Evidence: `bin/plan-execution/20260918-053259/`, including `baseline.patch`, original document/untracked-test copies, final artifact inventories and `verification.json`.

### Implemented scope and decisions

- **T4n-at / unit 62:** return/exit/enclosing-yield guards retain separate mixed source histories. Unselected bodies enter a frozen post-guard checking continuation, after ended protection and before runtime scope-exit cleanup. This avoids replacing those histories with the pre-guard fork.
- **T4n-au / unit 63:** Never calls, recognized builtin Abort and proven state-neutral divergence retain checking without inventing runtime selection or cleanup. Partial terminal guard histories end abandoned comparison Loans in separate checking regions; pending regions read replay-end Loan state. Synthetic ends are outside the contiguous normal cleanup-to-branch range required by checked lowering.
- **T4n-av / unit 64:** finite concrete enum/tuple scalar/Unit payloads retain mixed match histories. Whole enum acquisition follows explicit Copy declarations; an all-scalar enum is still Move without that declaration. Every Case participates in the bounded shape proof.
- **T4n-aw / unit 65:** owned string leaves in finite tuples/enums use validated decomposition subslots, Move body acquisition and ordinary remaining-payload cleanup. Conditional consumption, replacement and result delivery update existing lifetime flags. Retained type-shape caches avoid repeated recursive proofs and are cleared at analysis/body boundaries. Borrowed payloads, composite string guard candidates and user destructors are not admitted by this extension.
- **T12a / unit 66:** nested string literal Patterns compare exact UTF-8, including empty/NUL strings and distinct composed/decomposed sequences. Case tests dominate payload reads; literal comparison creates no owning handle. The physical Pattern plan retains a constant-pool index and uses the existing byte-comparison helper only when needed.
- **T4n-ax / unit 67:** unary `not` and both logical operands independently accept Never fitting to bool. A terminal left operand does not execute its right operand. Invalid bool operands still produce TypeMismatch, including checking-only right operands.

SPEC was not changed: §§3.1.5, 13, 14.8–14.10, 15.1.6 and 16 already require these semantics. Implementation limits remain limits rather than weakened language requirements.

### Failures and fixes retained in evidence

Early terminal-guard cases exposed an invalidated linear replay and active abandoned protection; frozen checking continuations and replay-end Loan normalization fixed them. Owned string subslots initially failed the local-only String Declare validation; lowering now accepts only validated decomposition payloads. The new logical guard cases initially failed Binding because Never was used as the right operand expectation; independent bool expectations fixed that behavior.

Test corrections are distinguished from compiler fixes: an enclosing do is an exit target, so yield uses an enclosing selection; divergent ownership graphs conservatively retain Pattern-failure edges, so the test checks guard/body runtime exclusion instead of asserting that every graph cleanup is unreachable. The first nested-string audit omitted the owning `ok` output destruction; its oracle was corrected. A zero-test scalar-enum run and two stale intermediate logical test binaries are superseded by fresh nonzero final reports; neither is accepted as feature evidence. Final builds were completed before invoking their matching reports.

### Final focused verification

Only four test classes created during this window were executed: `TerminalGuardContinuationTest` (25 cases), `ScalarEnumContinuationTest` (6), `OwnedAggregateContinuationTest` (5), and `OwnedPatternLiteralTest` (1). **37 cases pass in each of Debug and Release**, without failures or skips. Both compiler/test-project builds have zero warnings/errors. `run-new.ps1` records the exact class filters and rejects zero-case reports; the final reports are under `final/Debug/` and `final/Release/`.

The 11 newly generated fixture sets cover terminal return/Abort/divergence/logical guards, enum Copy acquisition, owned string cleanup/replacement/results and nested UTF-8 Pattern comparison. The pinned Windows script ran only the immutable new-fixture directory: **22 final O0/O2 executions PASS**, including exact stdout/stderr/exit, expected divergence timeout, and destruction count/order audits. All 55 IR/oracle files are byte-identical across Debug/Release, so the same native evidence is reused without counting another execution. Intermediate native runs are retained but excluded from final counts.

Serialized owned-enum syntax reload, final rebinding, repeated ownership analysis and checked IR generation pass. After warming the new owned-pattern workload, measured ownership analysis allocates **zero bytes** in both configurations; this is workload-specific evidence, not a universal allocation or speed claim.

**NOT_RUN by request:** all pre-existing tests, existing examples/milestones and their build checks. NativeAOT was not run. No draft, normative specification, existing example or milestone source was edited. Five pre-existing untracked tests retain identical hashes; original tracked changes remain preserved in the baseline patch. Units 62–67 are therefore IMPLEMENTED_UNVERIFIED pending omitted regression gates; broader M2/M3/M5/I families are not closed. Current states and exact next actions remain only in PLAN.md §2.

## Program Milestone 13 baseline (2026-09-18)

<a id="program13-baseline"></a>

The new request authorizes program 13 implementation only. HEAD is `d7e42915a73c6c06be1ac4b455187eb07608c99e`. All 35 pre-existing modified/untracked files were copied with hashes under `bin/milestone13-work-20260918/baseline/`; the binary diff, status and milestone hashes are retained beside it. All 14 milestone programs were read (the request described five). Programs 1–12 provide prerequisites; program 14 object/capture additions remain outside scope.

The fresh baseline Debug solution build passed without warnings. The focused SequenceEmission, ReferenceEmission, GenericBorrowedElement and CoreCatalog suites passed 134 cases. `initial-target.log` reproduces the first target failure: `UnresolvedBinding_Kd` at 7:13 on Iterator, followed by associated-Type/Slice failures and cascading errors. Ownership, generic ABI, borrowed Option results and Slice iteration gaps are initially anticipated, not observed later-stage failures.

### Preserved preceding plan state (historical)

## 2. Execution State

**Units 62–67 are IMPLEMENTED_UNVERIFIED** for M2/M3/M5 and I3/I6/I8/I12: terminal guards, finite enum/owned-string decomposition, nested string literal Patterns and logical Never fitting. Focused new-case evidence does not close the omitted regression gates. Units 52–61 retain their historical verified baseline; shared paths changed by this window need regression reverification. Broader milestone and compiler families remain IN_PROGRESS.

| Verified slice | State | Acceptance and current evidence |
| --- | --- | --- |
| T4n-at / unit 62 | IMPLEMENTED_UNVERIFIED | Return/exit/enclosing-yield guards preserve mixed source extents and pre-cleanup histories; unselected bodies use frozen post-guard continuations. |
| T4n-au / unit 63 | IMPLEMENTED_UNVERIFIED | Never calls, recognized builtin Abort and proven state-neutral guard divergence preserve checking without runtime selection/unwinding. Partial terminal histories end abandoned guard protection separately; replay uses endpoint Loans. |
| T4n-av / unit 64 | IMPLEMENTED_UNVERIFIED | Finite concrete enums/tuples with scalar/Unit leaves retain mixed histories, declared Copy versus Move and initialization failures. Every Case is checked. |
| T4n-aw / unit 65 | IMPLEMENTED_UNVERIFIED | Finite owned string tuple/enum leaves have checked subslots, Move acquisition, remaining-payload cleanup, conditional binding lifetime/replacement and result delivery. Type-shape proofs reuse cached entries; serialized reload and zero measured warm ownership allocation pass on the new workload. Composite guard bindings remain scalar/Unit. |
| T12a / unit 66 | IMPLEMENTED_UNVERIFIED | Nested string literal Patterns compare exact UTF-8 (empty, embedded NUL and distinct Unicode sequences), after dominating Case tests and without owning literal construction. |
| T4n-ax / unit 67 | IMPLEMENTED_UNVERIFIED | Logical operands independently fit bool, including Never; `not (return)` and terminal left operands of `and`/`or` retain checking histories. Non-bool operands are rejected and abandoned operands are not executed. |
| T4p / unit 56 | DONE | Depends on T4o. Candidate/body identities, false-guard effects, explicit-argument bypass and independent declaration checks pass managed and native regressions. |
| T4n-ap / unit 57 | DONE | Depends on ao. Pattern failure and post-cleanup false-guard joins preserve selected, terminal and caught-yield histories, including contained do/match result transfers. Outward/noncompleting guards remain excluded. |
| T4n-aq / unit 58 | DONE | Depends on ap. Whole-string Subject Move, guard protection and cleanup pass mixed-target checking, replay-end validation and native destruction-count/order audits. General owned decomposition remains outside this slice. |
| T4n-ar / unit 59 | DONE | Equal original/caught targets coalesce with common replay-end Loans. Reload and invalid-endpoint checks pass; 4/16/64 guards retain 115/367/1,375 history records with zero measured warm analysis allocation. This is bounded workload evidence, not universal complexity/retention closure. |
| T4q / unit 60 | DONE | Depends on p and retained storage. Scalar-tuple Copy/default local/body updates pass, including literal/local/returned/selected argument storage and exact slot validation. Adjacent prepared tuple/array inspection now recognizes verified Copy/Move initializers; owned cleanup audits pass. Immutable/candidate/parameter mutation remains rejected. |
| T4n-as / unit 61 | DONE | Depends on ap/ar and I12. Nested scalar/Unit tuple candidates and body bindings preserve mixed-target histories. Enum/non-Copy aggregate replay remains outside this proof. |
| T4n-am–ao, T4o / units 52–55 | DONE | Bounded terminal while, one-pass loop, unguarded scalar/Unit match and default slices pass existing regressions, dedicated O0/O2 programs, reload, Loan and cleanup checks. |

Current focused evidence: warning-free Debug/Release compiler/test-project builds, **37 new managed cases per configuration**, **11 new fixture sets / 22 O0/O2 executions**, including destruction counts/order and expected Abort/divergence. All 55 IR/oracle files match across configurations; native reuse is not counted twice. The [execution audit](bin/plan-execution/20260918-053259/verification.json) records identities and reports. Existing suites, examples/milestones and NativeAOT are NOT_RUN. The preceding [8,601-case/configuration audit](bin/plan-verification/20260918-041105/verification.json) remains historical evidence for its own source state, not this changed compiler; [history](PLAN_HISTORY.md#continuation-verification-20260918) retains its native/program counts and findings.

| ID | State | Completed target slice / evidence |
| --- | --- | --- |
| P12-B | DONE | Concrete environment identity/signature inference, mutable snapshot bindings, explicit nested captures, selected minimum receiver and bounded Callable constraints. |
| P12-O | DONE | Depends on P12-B. Ordered capture acquisition, exclusive receiver protection through arguments, consuming cleanup and invalid acquisition/reuse rejection, including parenthesized immutable receivers. |
| P12-G | DONE | Depends on P12-B/P12-O. Concrete aggregate storage/direct entries, nested result slots, acquired-value inline erasure, and a compile-time concrete witness for shared `Callable<uniq, S>` generation. |
| P12-V | DONE | Depends on P12-B/P12-O/P12-G. Original completion evidence is preserved in history; both historical program-12 configurations passed 37 checks each in the preceding verification audit. |

Program 12's original output, decisions, baseline preservation and verification remain in [its completion record](PLAN_HISTORY.md#program12-completion). That historical integration evidence does not close the broader I17/I18 closure requirements or certify every language rule.

Use item states `TODO`, `IN_PROGRESS`, `IMPLEMENTED_UNVERIFIED`, `DONE`, `BLOCKED`, `NOT_APPLICABLE`; use verification results `PASS`, `FAIL`, `NOT_RUN`, `BLOCKED`. DONE requires the item's full acceptance evidence. A completed program or sub-unit does not close its broader I/M family. Missing older evidence is disclosed under G6; retained DONE records are historical dispositions, not newly certified clause closure.

<a id="program13-completion"></a>

## Program Milestone 13 completion — 2026-09-18

This is historical execution evidence. [PLAN §2](PLAN.md#2-execution-state) owns the current state and next action. Program 13 is distinct from plan M13. No program-14 implementation was performed.

### Scope and preserved inputs

The active request superseded the earlier documentation-only/program-12 requests and authorized the unchanged `milestones/Milestone13.kimi`. The subsequent user instruction removed tests and existing-Milestone verification from this execution. After that instruction, no managed suite, dedicated negative exercise, earlier Milestone run or NativeAOT run was performed. The baseline's 134 focused cases preceded that instruction; their PASS is not a final-source regression result. All 14 programs were read for dependency context (the request referred to five, but this checkout contains fourteen); programs 1–12 supply the existing foundations, and program 14's object/borrowed-capture features were not implemented ahead of need.

HEAD remained `d7e42915a73c6c06be1ac4b455187eb07608c99e`; final evidence applies to the modified working tree, not that commit alone. The [baseline record](#program13-baseline) retains the original 35 uncommitted files, including untracked files and both current documents, in `bin/milestone13-work-20260918/baseline/` with `baseline-hashes.json`, `baseline.patch`, `baseline-status.txt` and `baseline-head.txt`. All 35 backup hashes were checked after implementation. All 14 source programs still match `milestone-hashes.json`. These are migration/execution artifacts, not a fourth managed planning document. SPEC.md and draft were not edited by this implementation; unrelated changes already present or appearing in the workspace were left intact.

### Failures, changes and decisions

- [Initial target](bin/milestone13-work-20260918/initial-target.log): final Binding first failed on the missing recognized Iterator, followed by associated-Type/Slice failures. Downstream omissions were hypotheses until the subsequent attempts reached them.
- [target1](bin/milestone13-work-20260918/target1.log) and [target2](bin/milestone13-work-20260918/target2.log): the ordinary Iterator declaration/complete Element binding exposed missing named Slice representation. Recognition now uses canonical declaration identity, because ordinary indexing recreates the symbol. Named and inferred Slices share one complete borrowed Type and backing Origin.
- [target3](bin/milestone13-work-20260918/target3.log): the explicit local `ref/Sample` annotation retained an unresolved inferred Origin. Result selection now infers the actual dependency before completing an omitted local Origin; it does not discard the backing dependency or relax explicit Origin checks.
- [target4](bin/milestone13-work-20260918/target4.log): Binding passed; ownership rejected copying `self.values` and indexing the generic Slice. Copying the two-word handle and explicit shared element borrowing now retain the external source Origin, independently of next's short unique receiver Loan. Option payloads and match results reuse the existing retained-reference machinery.
- [target5](bin/milestone13-work-20260918/target5.log): ownership passed; generation rejected generic stored Slice fields. Shared generation now admits their checked complete Types, copies fields, writes scalar fields through unique receivers, and obtains concrete field offsets/element strides from the existing entry metadata. It does not rebind or clone the source body per Type argument.
- [target6](bin/milestone13-work-20260918/target6.log): the shared body passed, but the concrete Slice parameter had no ABI representation. Slice is now admitted to the aggregate argument/result-slot path. Half-open view creation evaluates endpoints once and checks `0 <= start <= end <= length` before address calculation; reference iteration keeps the backing dependency. No element array allocation is introduced.
- [target8](bin/milestone13-work-20260918/target8.log) reached LLVM verification, linking and O2 execution; [first-native.json](bin/milestone13-work-20260918/first-native.json) records the first exact native result. Subsequent declaration-shape checks validate Iterator's complete next signature and Slice's declaration. Style warnings from intermediate builds were corrected before the final builds. A build attempt with no output was interrupted; the later completed build/target logs, not that interrupted attempt, establish success. One redundant no-change build during that investigation is retained as an intermediate log, not counted as additional coverage.

The recognized Iterator uses ordinary static conformance checking. The complete-Type associated exception is restricted to its canonical Element identity. Slice Copy does not require T to be Copy; yielding a reference does not Move T or borrow the iterator itself. General Iterable-based for dispatch, full Slice/Index/Range APIs and later-only features remain outside this target slice. CoreCatalogTest's declaration-count expectations were adjusted from 10 to 12, but that test was not run after the scope change.

### Final required verification and exact state

The final [Debug report](bin/milestone13-work-20260918/Debug/verification.json) and [Release report](bin/milestone13-work-20260918/Release/verification.json) both report PASS. Each includes the compiler source-input manifest, target/DLL/IR/executable hashes, build records and captured stdout/stderr/exit. The commands are retained in [capture-target.ps1](bin/milestone13-work-20260918/capture-target.ps1):

```powershell
& ./bin/milestone13-work-20260918/capture-target.ps1 -Configuration Debug
& ./bin/milestone13-work-20260918/capture-target.ps1 -Configuration Release
```

Each performs `dotnet build Kimi/Kimi.csproj -c <configuration> --no-restore --disable-build-servers -p:EmitCompilerGeneratedFiles=false`, then normal target builds from byte-identical program copies with explicit O0/O2 project settings and the repository toolchain. Both compiler builds have zero warnings and zero errors. The build pipeline runs `opt -passes=verify -disable-output` on input IR and, at O2, on optimized IR before native object/link production; linked records confirm pinned LLVM 22.1.8 identities. The four ordinary native executions produced exactly:

```text
Even sample total is 6; first is still 1.
Iterator remains exhausted.
Middle slice total is 5.
```

Every line ends in LF; each run exits 0 with empty stderr. The target's own checks exercise retained first-reference validity, even-item accumulation, continued exhaustion and bounded Slice iteration. No additional filesystem side effect is specified. The source hash is `2EC749D95ABF0B457910A26BF4E4E80322A883537F02B0794B44A761D2DD611B`; final Debug DLL hash is `5C70220E95FA35348F5D235BBD9E13BDA2D4C253BF4B2C197A95E03108B7A6C1`, and Release DLL hash is `AC65F37A32747ECEF8D57F5E290339760F340154584B50588CDD5840EEB36B80`.

### Unverified boundaries and information preservation

No dedicated invalid-input/bounds/zero-size/aliasing exercises or regression suites were run for the final compiler, per the revised request. Those semantic requirements remain normative and are not claimed as verified. Historical units 62–67 remain IMPLEMENTED_UNVERIFIED for their full original acceptance; a target PASS does not close them or the broader I/M families. NativeAOT is NOT_RUN. The final target reports and linked build records exist; no missing artifact is being treated as present evidence. Earlier missing-evidence disclosures remain unchanged.

<a id="program14-baseline"></a>

## Program Milestone 14 baseline — 2026-09-18

Historical checkpoint; [PLAN §2](PLAN.md#2-execution-state) owns current state and next work. HEAD `d7e42915a73c6c06be1ac4b455187eb07608c99e`; 59 modified/untracked files were copied byte-for-byte to `bin/milestone14-work-20260918/baseline/`, with hashes and binary working-tree/index patches. All 17 available programs were read; the request's reference to five does not match the checkout. No target source was simplified. Prior program 13 state follows below for traceability.

The solution baseline attempt failed without compiler diagnostics (`baseline-build.log`); serial `dotnet build xUnitTest/xUnitTest.csproj --no-restore --disable-build-servers -m:1 -p:EmitCompilerGeneratedFiles=false` passed warning-free (`baseline-project-build.log`). [Initial target](bin/milestone14-work-20260918/initial-target.log) fails final Binding at the borrowed Callable requirement before execution. Slice iterate/makeObj/capture diagnostics also occur; no later-stage PASS is inferred. A VSTest-style filter passed to the MTP dotnet-test runner selected zero cases and exited 5; this was a runner invocation failure, not a test PASS. The direct xUnit runner then passed all 178 cases in ConcreteClosureTest, SequenceEmissionTest, GenericStorageEmissionTest, WholeValueTest and CoreCatalogTest ([XML](bin/milestone14-work-20260918/baseline-tests.xml), `baseline-tests2.log`). SDK is 10.0.401. NativeAOT was not run.

### Superseded program-13 execution state

Program target: **`milestones/Milestone13.kimi` — DONE for the revised requested scope**. Program 13 is distinct from plan M13 (language tests). The unchanged target passes final Binding, ownership analysis, LLVM generation/verification, linking and native execution at O0/O2 from warning-free Debug/Release compiler builds. [Completion evidence and limitations](PLAN_HISTORY.md#program13-completion) identify the exact source/DLL state. Test-suite execution, negative exercises and existing Milestone checks were excluded by the subsequent user instruction; they are not certified by this completion.

| ID | State | Scope and dependencies |
| --- | --- | --- |
| P13-B | DONE | Recognized Iterator contract, its complete-Type Element exception, named Slice Types/Origins and required selection/iteration Binding. |
| P13-O | DONE | Depends on P13-B. The target preserves external backing dependencies through generic storage, Option payloads, matches and yielded references; next's exclusive receiver Loan ends independently. Invalid Origin/Move/mutation/escape must still reject; dedicated negative verification is NOT_RUN under the revised scope. |
| P13-G | DONE | Depends on P13-B/P13-O. Shared generic Slice storage/indexing and borrowed Option ABI; bounded Slice formation and iteration with checked addresses and no element allocation. |
| P13-V | DONE | Depends on P13-B/P13-O/P13-G. Unchanged target build, LLVM verification and native O0/O2 output, exit and effects. The subsequent user instruction excludes test-suite execution and existing Milestone checks; those are NOT_RUN after that instruction and are not completion gates for this execution. |

Expected stdout: `Even sample total is 6; first is still 1.`, `Iterator remains exhausted.`, `Middle slice total is 5.` in order, each followed by LF. Exit code 0, empty stderr. The first borrowed Sample must survive later next calls; exhaustion must persist and Slice iteration must borrow elements.

The original user changes and preceding state remain recoverable under `bin/milestone13-work-20260918/` and [history](PLAN_HISTORY.md#program13-baseline). Units 62–67 remain IMPLEMENTED_UNVERIFIED for their full original criteria; this target's verification does not automatically close unrelated requirements. P12-B/O/G/V remain historically DONE. Broader I/M families remain incomplete.


<a id="program14-completion"></a>

## Program Milestone 14 completion — 2026-09-18

Historical execution record. [PLAN §2](PLAN.md#2-execution-state) owns current state and next actions. This run implemented program 14 only; all 17 available programs were read for dependencies and later boundaries. The request's “five programs” did not match the checkout. Earlier program-13 test exclusions were superseded by the new program-14 acceptance criteria. No NativeAOT, draft edit, target simplification or program-15 implementation was performed.

### Implementation and failure sequence

- P14-C: the first confirmed failure was borrowed Callable Binding (`InvalidConstraint_Kd`, `MissingOriginBinding_Kd`; [initial log](bin/milestone14-work-20260918/initial-target.log)). Function Type inputs now bind per-call input Origins and compare corresponding borrowed parameters; a stronger uniq or fixed-static input contract is rejected. Slice.iterate uses an ordinary source-defined SliceIterator with external backing Origin and non-lending next. Used library bodies enter ordinary flow/ownership checking. Target-independent 0/1 literals exposed an unset native-width problem; conservative target-independent integer fitting restored library-only Binding.
- P14-C generation: shared loop/match/callback lowering required retained Match plans, closed storage descriptions, forwarded member/constructor calls, and complete-place addresses separate from field-leaf addresses. Origin substitutions carry dependencies without a runtime Origin representation. A zero-sized callback borrow initially emitted undefined `%p12`; address-required zero-sized storage now receives an address. Isolated Slice and pipeline native checks passed before object support; they did not certify the whole target. Evidence: `slice-isolated8.log`, `pipeline-initial.log`, `pipeline1.log`, `pipeline3.log`, `pipeline4.log`, `pipeline5.log` under this run's work directory.
- P14-O: the compiler validates the concrete makeObj identity and payload eligibility, moves/acquires the complete payload once, allocates header plus payload, and publishes the owned handle after initialization. Immutable metadata keeps deterministic distinct Type keys, payload size/alignment/Copy/destruction, and descriptor destruction/free separation. Origin-erased identity retains nominal/module/package identity and Type structure; no hash-only equality is used. Sealed projection and member calls address header + 16. Payload exchange uses existing whole-value replacement responsibility and never frees the enclosing object; final owned cleanup destroys then frees the original header. rc/arc/Weak and broader views were not added.
- P14-E: supported ref/uniq and obj captures keep their full Types and dependencies. Captured referent mutation requires an Exclusive call; moving an obj capture requires a Consuming call. Ownership initially confused a capture slot and an input-Origin slot with the same ordinal. Input-Origin matching now requires a Parameter symbol, and nested observable cleanup dependencies remain live. Binding/ownership then passed (`object-target4.log`), exposing missing object aggregate-place registration. Adding that physical storage connection produced the first complete target run (`object-target5.log`, `first-native.stdout`, `first-native.stderr`, `first-native.exit`).
- P14-V hardening: malformed shared match dispatch/Subject initialization plans were initially accepted by the emitter ([reproduction](bin/milestone14-work-20260918/match-before.log)). Checked Subject acquisition, exact dispatch/arm identities, exhaustive coverage, pure tests, ordered continuations and payload acquisition now reject corruption before writing IR. The target remains a flat Copy enum Case subset; this does not claim guarded/nested shared Pattern support.
- Regression correction: planning allocated even for inputs without objects, and a LINQ predicate over the aggregate HashSet boxed an enumerator. The no-object preparation path and direct HashSet enumeration restore existing measured zero-allocation workloads. Temporary allocation instrumentation was removed. Object call contexts are cleared after every emission to avoid retaining old syntax. Library-only Origin obligations required two legacy assertion scopes to distinguish source obligations from library obligations; TypeBindingTest's old inference-only expectation was updated to assert the fixed initializer-resolved Input Origin introduced in program 13, including rebind stability. No language check was disabled. Earlier failures are preserved in `regression-before.log` and `debug-final-tests.log`; final evidence below supersedes them.
- Test-development corrections: a new fixture accidentally used previously unsupported borrowed-field `+=`; it was changed to ordinary assignment matching the target's intended coverage, and all negative cases were rerun against the valid fixture. A generic all-terminal Never match and a general `Option<T>` payload probe exposed unsupported paths and were not used as the positive baseline for match-plan mutation tests. These limitations are retained as P14-L1. An invalid xUnit wildcard invocation selected no tests; a build overlapping a running test process hit a DLL copy lock. Neither was counted as a PASS. Subsequent build/test artifact phases were separated.

### Final verification and identities

[Frozen audit](bin/milestone14-work-20260918/verification.json) validates 954 current build/test/backend inputs (including the adjacent Tinyhand dependency), the compiler DLLs, run reports and preserved files. Target source SHA-256: `EB87D485322EB7B6BC8C82E2B731CFC6479B5CB9A032BBBF5FCFF23F4AC25BCB`. HEAD remains `d7e42915a73c6c06be1ac4b455187eb07608c99e` with the pre-existing work and this implementation uncommitted.

- Debug: [8670 managed tests](bin/milestone14-work-20260918/debug-final3-tests.xml), [build](bin/milestone14-work-20260918/debug-final3-build.log), [48 target checks](bin/milestone14/Debug/3a531c99392340e88fb5b6a48f72a2b9/verification.json), [26 completed-program native checks](bin/milestone14-work-20260918/regressions/Debug/aa4bc5fc814f4f048ed72b937e0a06d2/verification.json); compiler SHA-256 `6B5A785F61750D7BD676D9D84B9AC1D73ED9AC33A024417011882F6E37A49BD4`.
- Release: [8670 managed tests](bin/milestone14-work-20260918/release-final3-tests.xml), [build](bin/milestone14-work-20260918/release-final3-build.log), [48 target checks](bin/milestone14/Release/360550b354a84a4a847933c3ab4ac61c/verification.json), [26 completed-program native checks](bin/milestone14-work-20260918/regressions/Release/328f118b6e9641b5b65f6788d1a8084b/verification.json); compiler SHA-256 `AB2C78C152D637A9F9846C4EA77950871C52E522313A432311323AFA16CCA5EB`.

Build commands use `dotnet build xUnitTest/xUnitTest.csproj -c <configuration> --no-restore --disable-build-servers -m:1 -p:EmitCompilerGeneratedFiles=false`; both final configurations report zero warnings/errors. Tests use the direct xUnit v3 runner with `-parallelMode none -result-xml <file>`, not a VSTest filter. All 8,670 tests pass per configuration, with no skips. Native checks use:

```powershell
./backend/windows-x64/test-milestone14.ps1 -Configuration Debug -ToolchainRoot ./toolchain
./backend/windows-x64/test-milestone14.ps1 -Configuration Release -ToolchainRoot ./toolchain
./bin/milestone14-work-20260918/regress-programs.ps1 -Configuration Debug
./bin/milestone14-work-20260918/regress-programs.ps1 -Configuration Release
./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixtureDirectory bin/milestone14-work-20260918/Release-fixtures -OutputDirectory bin/milestone14-work-20260918/native-fixtures
```

The target harness checks the unchanged original source and byte-identical renamed copies, different identifiers/values, exhaustion, a zero limit and rejection of all jobs. Nine invalid variants reject before native artifacts, including `MovedPlace_Kd` for a second consuming call and `ComparisonLoanConflict_Kd` for owner access during a live captured Loan. Per configuration, 39 native/CLI execution checks plus 9 rejections total 48. Completed programs 1–13 each run natively at O0/O2 with exact output, empty stderr and exit 0; their sources are unchanged.

The [Debug fixture hashes](bin/milestone14-work-20260918/Debug-fixture-hashes.json) and [Release fixture hashes](bin/milestone14-work-20260918/Release-fixture-hashes.json) match for 68 sets: Pipeline (8), SliceIterator (1), ConcreteClosure (8), WholeValue (15), Sequence (23), GenericStorage (13). [Native log](bin/milestone14-work-20260918/native-fixtures.log) records 136 O0/O2 executions after LLVM verification; this includes zero-sized payload destruction, repeated borrowed-capture calls, consuming/unused owned captures, exchange cleanup, and distinct same-layout object Types. These identical bytes are not double-counted across compiler configurations. Normal target builds verify input IR and optimized O2 IR with pinned LLVM 22.1.8 before linking.

Exact program-14 stdout (LF after every line), exit 0, empty stderr:

```text
Accepted three jobs.
Accumulator destroyed.
Object total is 12.
Accumulator destroyed.
Pipeline finished.
```

The old exchanged payload and final object payload each destroy once. Final object cleanup invokes payload destruction before the descriptor's storage-release entry; exchange retains the header/descriptor. No filesystem side effect is required by the program.

### Preservation and remaining boundaries

The original 59 dirty/untracked files remain recoverable byte-for-byte under `bin/milestone14-work-20260918/baseline/`, with the original hash manifest and working-tree/index patches. All 17 milestone sources are unchanged; draft is unchanged. Relevant PLAN/STATUS updates and this history are English. SPEC was not changed by this run; its pre-existing change is retained. New evidence paths in this record exist. Earlier disclosures about missing historical artifacts remain valid.

P14-C/O/E/V are DONE for this target, not for the entire language. P14-L1, full object/Weak/refinement and generic/capture boundaries remain documented; deferred ObjectCallCompatible stages were not implemented. Units 62–67 remain IMPLEMENTED_UNVERIFIED under their full original acceptance, despite the current broad managed regressions. No broader throughput, memory-exhaustion, full API, cross-module object ABI or NativeAOT claim is made.

<a id="units62-67-verification"></a>

## Verification audit: units 62–67 and current regressions — 2026-09-18

Historical record; [PLAN §2](PLAN.md#2-execution-state) owns current state. The request was verification only: no new implementation, program 15 or NativeAOT. HEAD `dd8f239f8375c27accd96b94dbe7a5e35f00ce7b`, clean at start. Evidence is under `bin/verification-20260918-units62-67/` (reports, native shard logs, script logs, new fixtures, probe sources and final compiler hashes).

### Gates run (all PASS)

- Warning-free Debug/Release builds of `xUnitTest/xUnitTest.csproj` (`-m:1 --disable-build-servers`). Direct xUnit runner, `-parallelMode none`: 8,670 per configuration before the fix; **8,673 per configuration** after it, no skips.
- Every generated scalar fixture: 3,147 sets (Debug ⊂ Release; all 13,530 common files byte-identical, 2,205 extra files are Release-named variants), split into 12 immutable shards and run by `test-scalars.ps1`: **6,294 O0/O2 executions**.
- `test-milestone1–12` and `test-milestone14`, `test-emission` (68 each) and `test-cli`, for both configurations. Program 13, which has no script, was built from byte-identical copies at O0 and O2 with each compiler: exact three-line output, exit 0, empty stderr.
- All 31 buildable `examples/` projects in scratch copies; outputs match their READMEs. SpecTour fails Binding as its README states.

These are the regression gates omitted when units 62–67 were implemented, so T4n-at–ax and T12a are recorded DONE for their bounded scope. The Debug milestone/emission/CLI scripts ran on the compiler before the fix below; Release scripts, full suites and the new fixtures ran after it.

### Defect fixed

An arm whose guard is unsupported (for example a guard candidate over an owned composite `(string, i32)` Subject) marked its unbound guard/body syntax Resolved with Type Unit. Control-flow analysis then trusted that Type and added a false cascade, e.g. `Result of type () is incompatible with i32` for `if (return n)`. `Binding.Patterns` now marks such subtrees Unresolved without a failure, so only the enclosing Unsupported diagnostics remain; Pattern syntax still receives Unit. The limitation itself (composite guard candidates remain scalar/Unit) is unchanged.

### Tests added

`OwnedAggregateContinuationTest`: two cases asserting only Unsupported Binding issues and no control-flow issue for those guards; and `BodyExitsAndDeliveredBindingsDestroyEachOwnedLeafOnce`, an audited native fixture for early `return`, loop `exit`, delivered match results and `var` binding replacement over owned string leaves (4 O0/O2 executions PASS; identical across configurations).

### Probes without defects

Command-line probes (sources retained) confirmed: logical Never operands skip the abandoned operand; terminal guards keep Move/let/initialization diagnostics across loops and unselected bodies, while §14.10.3 checking continuations do not merge into reachable paths; declared Copy versus Move enums; Abort in guards; top-level and nested string literal Patterns, including exhaustiveness; object exchange destroys each payload once. Documented limits (borrowed receiver-field compound assignment, generic makeObj, composite guard candidates) still reject with diagnostics rather than miscompiling. The string-literal Pattern IR already compares lengths before `memcmp`; no speed change was justified.

`examples/SpecTour` was removed at the user's request after this audit. It was a non-normative walkthrough that did not pass Binding; SPEC.md, STATUS.md and the WholeValueReplacement README no longer link to it. Earlier records mentioning it are historical.

<a id="programs18-20-design"></a>

## Programs 18–20: specification targets and status inventory — 2026-09-18

The request was to read the milestone roadmap, create programs 18–20, update
related documents, and summarize creation/build/test status. The actual directory
is `milestones/`, not `milestone/`. Added three independent Applications without
changing compiler implementation or weakening the specification to match its
current support. Existing compiler/test/document edits from the units 62–67 audit
were preserved; unrelated concurrent changes were not reverted. Draft and
NativeAOT were excluded.

- P18-D: composite and Non-Copy generic inputs/results, temporary Boxes, array,
  enum and Tuple storage, per-Type Copy/Move acquisition, secured results and
  exact destruction responsibilities (§8.10, §15.1, §16.2–3, §21.3).
- P19-D: associated Core Types and equality proofs, requirement calls, conditional
  and nested conformance, and storage independent of conformance (§8.4.3–5/8).
- P20-D: Type/length/Origin inference, an effectful omitted default, explicit full
  length/Type specialization with inherited Origins/defaults, and forwarding
  (§4.4, §7.2, §8.8, §10.8, §15.3.4).

The README records exact expected LF output, separate rejection exercises and
future verification boundaries for each target. SPEC's example index now points
to 1–20 and proposed 21–34; no normative language rule changed. PLAN owns current
scope/next actions and STATUS distinguishes design from implemented support.
The prior P14 execution scope/stop instruction is superseded by this authoring
request; its P14-C/O/E/V outcomes and detailed evidence remain in the earlier
program 14 record. No broader compiler implementation was attempted here.

### Verification

Evidence: [audit](bin/milestones18-20-design-20260918/verification.json), copied
canonical sources, build logs, `syntax.log` and `syntax.xml` in the same directory.
HEAD was `dd8f239f8375c27accd96b94dbe7a5e35f00ce7b` plus existing worktree changes.
Release compiler SHA-256 was
`220A13CACA36319D4487DF569EB91B70186B0584CCDFC666D7E1F4405B309F6B`;
it was checked unchanged across the audit. Source SHA-256 values are in the report.

```powershell
dotnet build xUnitTest/xUnitTest.csproj -c Release --no-restore
dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -method '*MigratedExamplesAndMilestonesRetainValidSyntax' -parallelMode none -failSkips -result-xml bin/milestones18-20-design-20260918/syntax.xml
dotnet Kimi/bin/Release/net10.0/Kimi.dll build milestones/Milestone18.kimi --ToolchainRoot ./toolchain
dotnet Kimi/bin/Release/net10.0/Kimi.dll build milestones/Milestone19.kimi --ToolchainRoot ./toolchain
dotnet Kimi/bin/Release/net10.0/Kimi.dll build milestones/Milestone20.kimi --ToolchainRoot ./toolchain
```

| Gate | Result | Scope / diagnostic |
| --- | --- | --- |
| Release compiler/test-project build | PASS | Zero warnings/errors; not a native milestone build. |
| Existing syntax-catalog test | PASS | 1 test, no failures/skips, scanning example/milestone sources including all 20 programs. |
| Program 18 implicit Application/O2 build | FAIL, exit 1 | `GenerationFailed_Kd`: shared operation refers to invalid storage or projection. |
| Program 19 implicit Application/O2 build | FAIL, exit 1 | `InvalidPattern_Kd` for concrete Patterns over associated-result Tuples; `UnprovenConstraint_Kd` for nested Wrapper associated equality. |
| Program 20 implicit Application/O2 build | FAIL, exit 1 | `UnsupportedBinding_Kd` on the length specialization argument, `InvalidTypeFormation_Kd`, missing inherited Origin bindings and cascading unresolved/constraint diagnostics. |
| Native output, rejection variants and generation/resource inspection | NOT_RUN | Builds did not produce executable targets; expected output is specification-derived. |
| Debug, O0, full managed regressions, NativeAOT | NOT_RUN | No compiler feature changes were made in this task. |

An initial `dotnet test ... --filter FullyQualifiedName~...` invocation selected
zero tests and exited 5 under Microsoft.Testing.Platform. It is not PASS evidence;
the repository's direct xUnit runner above subsequently selected and passed one
test. Programs 1–14 retain their previous recorded build/native-test results;
15–17 have no build/native-test result claimed here. The new README inventory
separates those historical results, unattempted targets and failed current probes.

<a id="programs38-restructure"></a>

## 38-program restructuring and concise console calls — 2026-09-18

The user accepted the 38-program review, authorized source restructuring and
future verification-scope changes, and requested Console.writeLine except in
Hello World. Programs 1–19 keep their numbers and subjects. Program 20 now covers
ordinary inference/defaults without specialization; new program 21 isolates full
specialization and inherited contracts, using explicit Type/length arguments at
call sites. All remain independent Applications. Future programs 22–38 are design
scopes, not newly created sources or implemented capabilities.

The former 21 becomes 22; Properties 22 splits into 23/24; former 23–27 become
25–29; comparison from former 29 becomes 30 before Dictionary (31); text becomes
32; former objects 30 splits into 33/34; former 31–34 become 35–38. The README
owns the complete number mapping and each future target's prerequisites,
canonical scenario, separate negative/boundary inputs and internal verification.
There is no minimum source line count or requirement to put every check into a
canonical main. Generation limits, allocation/complexity, count/race protocols
and static Abort cases have explicit companion-test responsibilities. Deferred
ObjectCallCompatible, runtime Contract Views and source threading remain excluded.

P18-D/P19-D remain the same subjects. The earlier P20-D/P18-20-V checkpoint refers
to the combined source and is superseded for current program 20 by the split;
its old source and failed-build evidence are retained in the prior authoring
record. P21-D and P38-R/S/V identify this restructuring's new work. Current states
and next actions are in PLAN, not in this historical checkpoint.

### Source and harness changes

- Programs 2–19 differ from their task-start copies only by replacing
  `::Kimi.Console.writeLine` with `Console.writeLine`; byte/text checks confirmed
  this. Program 1 is byte-identical. Programs 20/21 use the short spelling too.
- Existing milestone scripts 4/5/6/8/9/10/12 use the same short spelling for
  source mutation patterns and generated variants. In particular, the program-4
  deinit guard and program-5 moved-read variant still match the canonical source.
  Program-1 source and harness were not edited.
- NamedAliasTest's syntax catalog still parses every non-bin source. Its obsolete
  total >= 55 assertion failed after the separately performed SpecTour removal.
  It now checks that each catalog is nonempty, avoiding a stale global inventory
  count. No production/compiler code was changed by this task.
- Existing compiler/test edits, staged SpecTour removal and concurrent normative
  documentation edits were preserved. SPEC's milestone index, PLAN, STATUS and
  the milestone README were updated for this request. Draft was not edited.

### Verification and reproducibility

Evidence root: `bin/milestones38-20260918-212937/`.
[The manifest](bin/milestones38-20260918-212937/verification.json) records HEAD,
compiler/test DLL hashes, all 21 final source hashes, individual harness reports
and the failed build diagnostic codes. `baseline/` and `baseline-worktree.patch`
retain task-start milestone/docs/script inputs; `sources/` retains final sources.
Native verification checks that the compiler hash is unchanged across its run.

| Gate | Result | Evidence / scope |
| --- | --- | --- |
| Release compiler/test-project build | PASS, zero warnings/errors | `final-managed-build.log` |
| NamedAliasTest | 57 PASS, zero failures/skips | `final-aliases.log/xml`; includes default Console lookup/emission and the source-catalog test covering milestones 1–21 |
| Existing program 1–12/14 harnesses | 577 PASS | Per-program logs and reports in the manifest; ordinary native build/LLVM verification, O0/O2 variants, CLI/output/exit and rejection checks |
| Program 13 | 2 native executions PASS | Byte-identical copied source, O0 and O2; linked/version-verified manifests, exact UTF-8/LF stdout, empty stderr and exit 0 |
| Programs 15–21 | Seven Release/O2 build probes FAIL, exit 1 | `Milestone15-build.log` through `Milestone21-build.log`; no native test claim |
| New scope links and source invariants | PASS | New owning-spec links resolve; program 1 unchanged; only console spelling changes in 2–19; specialization absent in 20 and present in 21 |
| Debug, full managed regressions, NativeAOT | NOT_RUN | Focused source/harness change verification, no compiler feature implementation |

Per-program harness counts: 1=25, 2=21, 3=37, 4=33, 5=47, 6=63, 7=52, 8=46,
9=59, 10=54, 11=55, 12=37, 14=48. Their sum is 577; program 13's two runs are
separate, and 57 managed cases are not native executions.

```powershell
dotnet build xUnitTest/xUnitTest.csproj -c Release --no-restore
dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -class XunitTest.NamedAliasTest -parallelMode none -failSkips -result-xml bin/milestones38-20260918-212937/final-aliases.xml
pwsh -NoProfile -File bin/milestones38-20260918-212937/verify-native.ps1
# Individual existing scripts accept -Configuration Release -ToolchainRoot ./toolchain.
# Repeat for each n from 15 through 21; these commands currently fail:
dotnet Kimi/bin/Release/net10.0/Kimi.dll build milestones/Milestone20.kimi --ToolchainRoot ./toolchain
```

Initial verification observations are not final PASS evidence: the old inventory
assertion failed once; after the catalog fix all 57 tests pass. The first native
attempt failed because sandboxed opt.exe execution returned permission denied;
LLVM 22.1.8 and the native audit then ran successfully with the approved sandbox
override. No toolchain integrity checks were disabled.

The fresh failed probes distinguish the target boundaries: 15 has joined-borrow
Type/control-flow diagnostics; 16 has unsupported ownership and dependent Loan/
initialization diagnostics; 17 has unsupported element-update targets and Move/
Loan diagnostics; 18 fails shared storage/projection generation; 19 fails
associated-result Patterns/nested equality proof; 20 reaches unsupported
operations on dereferenced generic returned element borrows; 21 fails length
specialization/inherited Origin Binding with cascading diagnostics. These are
recorded limitations, not reasons to weaken the specified programs. Their
expected outputs and proposed negative cases remain unverified natively.


<a id="kimi-intrinsics-placement"></a>

## Kimi.Intrinsics placement and reference (2026-09-18)

The requested namespace relocation supersedes the root function placement in older design records. `draft/` and preceding historical evidence are unchanged. Public compiler identity follows the Intrinsics group; no root forwarding compatibility declarations are retained. Missing rc/arc/Weak operations remain implementation gaps.

### Implementation and verification

KI-S/KI-I/KI-V completed. Added the SPEC.md reference table and normative §22.1.1, migrated owning specification clauses and executable sources, and moved the four existing compiler functions into a validated public Intrinsics group with a reusable Binding scope. Catalog IDs and the 12/22 coverage count are unchanged. Tests cover qualified/default/named/opening alias lookup, root-name rejection, repeated Binding, container-shape validation, ordinary same-name shadowing and emission.

- `dotnet test --project xUnitTest/xUnitTest.csproj -c Release --no-restore`: PASS, 8,691 tests, zero skipped; warning-free build.
- The same command with `-c Debug`: PASS, 8,691 tests, zero skipped; warning-free build.
- `backend/windows-x64/test-milestone14.ps1 -Configuration Release`: PASS, 48 checks (39 native/CLI execution checks across canonical O2 and O0/O2 variants, nine rejected variants). [Native report](bin/milestone14/Release/932614fcdf0644e7a198a774012c1177/verification.json).
- `backend/windows-x64/test-scalars.ps1 -FixturePattern 'WholeValue*.ll' -OutputDirectory 'bin/intrinsics-verification/whole-value-native'`: PASS, 18 fixtures / 36 O0/O2 executions, including both Intrinsics group aliases, ordinary user-group shadowing, updates/destruction and the migrated example. Fixtures came from the final Debug managed run.
- [Audit summary and source/binary hashes](bin/intrinsics-verification/verification.json). NativeAOT NOT_RUN; draft records unchanged. No new rc/arc/Weak runtime support is claimed.

Intermediate fixes: the first focused run passed 79/89 tests; ten failures exposed old qualified calls immediately following escaped newlines, missed by an initial word-boundary replacement. Those calls were migrated, and the expanded focused run passed 107/107. Four formatting warnings in the added tests were fixed before both final full suites. Initial sandbox NuGet-config access failed; existing restore assets permitted `--no-restore`. LLVM execution required an approved sandbox escalation; the final native runs passed.

### Superseded preceding plan checkpoint

The active request is to restructure the roadmap into 38 programs, split the existing combined inference/specialization target, refine future verification scopes and use Console.writeLine in milestone sources except Hello World. Programs 1–21 have source files; 22–38 remain planned. This task does not require completing unsupported compiler features or authoring all future programs. Preserve existing uncommitted work; NativeAOT and draft edits remain excluded.

Current request: **38-program restructuring — DONE**.
[Program design/status](milestones/README.md#program-status) owns the catalog;
[future verification scopes](milestones/README.md#verification-scopes-for-future-programs-2238)
separate canonical sources, semantic variants and internal evidence.

| ID | State | Outcome and dependencies |
| --- | --- | --- |
| P18-D, P19-D | DONE | Existing generic value/Contract targets retain their subjects; output calls shortened. Product support remains bounded. |
| P20-D | DONE, revised | Inference/defaults only; depends on I4 and Origin support. Former combined P20 scope is split, not dropped. |
| P21-D | DONE | Independent full specialization/inherited-contract source; depends on I14/I15 and P20 capabilities. |
| P38-R | DONE | 38-program catalog, old/new number mapping, prerequisites and verification scopes for future 22–38; compare before Dictionary. |
| P38-S | DONE | Console.writeLine in programs 2–21 and companion script sources; program 1 unchanged. |
| P38-V | DONE for restructuring | Warning-free Release build, 57 alias/syntax tests, 577 existing harness checks and two program-13 native runs PASS. O2 probes for 15–21 FAIL; native tests NOT_RUN. [Evidence](PLAN_HISTORY.md#programs38-restructure). |

P14-C/O/E/V retain their recorded target completion; former P18-20-V is historical
[authoring evidence](PLAN_HISTORY.md#programs18-20-design) for the pre-split source.
Programs 15–21 remain specification targets; 22–38 have no source files yet.
Units 62–67 (T4n-at–ax, T12a) remain DONE for their bounded criteria after the
[verification audit](PLAN_HISTORY.md#units62-67-verification); their parent
I3/I6/I8/I12 families remain IN_PROGRESS. No product M/I family is closed here.

<a id="program15-completion"></a>

## Program 15 completion (2026-09-18)

P15-B/O/G/V complete the unchanged `milestones/Milestone15.kimi`; these IDs are
program checkpoints, not product M15. Started at HEAD
`9a6352b9a2a7fbb3152ac81afb8bc0db1d1b7d29` with a clean working tree, so no existing
uncommitted changes needed preservation. Read AGENTS.md, current plans/status,
programs 1–21 and the owning result/Origin/ownership/cleanup rules. Later programs
informed scope only. No milestone source, normative specification or draft changed.
SDK: 10.0.401; pinned Windows x64 LLVM: 22.1.8. NativeAOT NOT_RUN.

### Reproduction and decisions

- Initial current-source Debug compiler build passed without warnings. Command
  `dotnet Kimi/bin/Debug/net10.0/Kimi.dll build milestones/Milestone15.kimi`
  reproduced TypeMismatch_Kd at line 28 and ControlFlow_Kd at lines 29–30: the
  borrowed if result could not combine two distinct local Origins. Later ownership
  and generation failures were initially unconfirmed. Existing focused tests passed
  178/178 ([baseline XML](bin/milestone15-work/baseline.xml)); the five initial
  P15 tests failed at Binding ([initial XML](bin/milestone15-work/initial-tests.xml)).
- P15-B uses the existing interned Origin meet for matching borrowed referent Types
  and Semantics. Binding and retained control-flow inference share this operation;
  no common-base search, implicit mode conversion or invariant referent weakening
  was added. After this fix the unchanged target passed ownership, and all four
  initial rejection variants reached their intended ownership diagnostics.
- P15-O reuses existing converged CFG state and backward borrow liveness. No new
  loop solver, runtime ownership flags or filename-specific path was needed.
  Tests cover both normal/continue missing repair, branch initialization, both
  retained owner dependencies, post-last-use mutation and escaping local sources.
- P15-G's confirmed next blocker was `Missing or inconsistent value-flow plan`.
  Pointer Phi/alias validation now permits only matching storage referents with
  complete `FitsType` proof, preserving Origin shortening and Semantics checks.
  Dominance, predecessor coverage, result acquisition and cleanup validation remain.
  A corrupted result plan that drops an incoming Origin is rejected before IR
  writing. The generated target includes the ordinary pointer Phi.
- Intermediate verification issues were confined to the harness/environment:
  one newly written named exit lacked its required colon and was corrected;
  exact diagnostic matching needed ANSI color stripping; sandbox LLVM execution
  returned permission denied, then the approved native runs succeeded. These failed
  attempts are not completion evidence. No execution check remains environment-blocked.

### Final verification

Both configurations use:

```powershell
dotnet build Kimigayo.slnx -c <Debug|Release> --no-restore --disable-build-servers -m:1 -p:EmitCompilerGeneratedFiles=false
dotnet xUnitTest/bin/<Debug|Release>/net10.0/xUnitTest.dll -parallelMode none -failSkips -result-xml bin/milestone15-work/<debug|release>-tests.xml
./backend/windows-x64/test-milestone15.ps1 -Configuration <Debug|Release>
```

- Debug/Release solution builds: zero warnings/errors; each full managed suite:
  **8,708 passed, zero failed/skipped**. [Debug build](bin/milestone15-work/debug-build.log),
  [Debug tests](bin/milestone15-work/debug-tests.xml),
  [Release build](bin/milestone15-work/release-build.log),
  [Release tests](bin/milestone15-work/release-tests.xml).
- Debug target verification passed 56 checks in the first harness version
  ([report](bin/milestone15/Debug/8954ad819269402e8c84791fb147a3b0/verification.json)):
  51 normal execution checks and five O2 rejection checks. After adding explicit
  O0/O2 rejection projects and exact diagnostic checks, only `-Cases Rejections`
  was rerun: ten checks passed
  ([report](bin/milestone15/Debug/6cceb2cb44ac4daaac61cafe529f24a6/verification.json)).
  The compiler identity is identical across both reports. Normal runs were not repeated.
- Release final harness: **61 checks passed**
  ([report](bin/milestone15/Release/7f0e7743ef88488ab693a1bca28b15b4/verification.json)).
  Canonical O2 plus eight variants at O0/O2 are each executed directly and through
  both CLI run forms. Variants preserve a byte-identical source copy, change names
  and input values, reverse selection, run zero/five iterations, distinguish
  consumed/fallback/repaired destruction, and mutate after the borrow's final use.
  Every normal stdout matches exact UTF-8/LF bytes, stderr is empty, and exit is 0.
  Totals/final values are asserted by the source; each canonical run destroys
  exactly three Items in order, including cleanup on continue. Five invalid inputs
  at both O0/O2 diagnose missing initialization, missing repair, moved reads and
  writes to either live borrowed owner; no IR/executable is published.
- Completed-program regression: **84 checks passed**, 14 unchanged byte-identical
  programs × O0/O2 × direct/native and two CLI run forms. Each build runs LLVM
  verification and linking with the current Release compiler; output comes from
  the program's README contract. [Runner](bin/milestone15-work/native-regressions.ps1),
  [report](bin/milestone15-work/native-regressions/verification.json).
- [Frozen identities and evidence index](bin/milestone15-work/verification.json)
  record source/compiler/test hashes. Target SHA-256 remains
  `AD6366A5DECE798F8CAE535638D51D1A8796AD227A08DA295F9A3F31B51920B6`.

P15 adds bounded borrowed-result inference and checked pointer transfer support;
it does not close general variance/Origin inference, unequal active acquisition
stacks, deferred checking-history joins, arbitrary CFG/effects or product M15.
No later milestone implementation was started. The current disposition and next
actions remain solely in PLAN.md.

<a id="documentation-comments-integration"></a>

## 2026-09-18 — Documentation Comments integration

Started from clean HEAD `29f438c50fa51b21347a28ec7da81c8ca701353a`.
Integrated the authoritative `draft/Design/2026-09-17 Documentation Comments.md`
into §2.3.1–6, the Attribute/unsafe/directive cross-references, syntax summary and
Appendix A.21. The draft is unchanged (SHA-256
`EEB6BF197C702C1B69237EF1EF72514575954E7AF4C3CE6045D78170599E755C`).

DOC-S/LP/M/V implement an opt-in path: source ranges beside ordinary tokens;
Parser association before selection/merging loses boundaries; lazy normalized
text and original UTF-16 mappings; pinned Markdig 1.3.2 without HTML block/inline
recognition; original-tree item references, escaped rendering and relative-link
resolution; optional diagnostics; and Binding-backed identity/access/order queries.
Specialization API queries use the original contract; implementation notes and
associated-Type specifications retain their own text. Public publication respects
Contract requirements and the implementing Type's visibility. Generated Mod ID
and addition order survive source serialization; reparsing replaces associations.

Analysis, Lowering and Emit consume the same executable tree and checked facts;
they need no documentation-specific branches. Collection defaults to disabled,
and documentation never enters runtime metadata. Consumers may read completed
snapshots concurrently; exposed Markdown trees must be treated as read-only.
Recovery defers all associations in a source with parse errors, keeping raw text.
This supplies tooling APIs, not a new CLI, LSP server, formatter, public Mod host,
incremental source-edit service or doctest runner. Source edits/generation changes
are handled by rebuilding the source snapshot, retaining raw-source dependencies.

Verification commands:

```powershell
dotnet build Kimigayo.slnx --no-restore -c Debug -v:minimal
dotnet build Kimigayo.slnx --no-restore -c Release -v:minimal
dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -result-xml bin/documentation-comments-20260918/test-debug.xml
dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -result-xml bin/documentation-comments-20260918/test-release.xml
./backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain -FixtureDirectory bin/scalar-fixtures -FixturePattern DocumentationComments.ll -OutputDirectory bin/documentation-comments-20260918/native
```

- Debug/Release solution builds: **zero warnings/errors**.
  [Debug](bin/documentation-comments-20260918/build-debug.log),
  [Release](bin/documentation-comments-20260918/build-release.log).
- Full managed suites: **8,792 passed per configuration**, zero errors, failures,
  skips or unrun tests. Includes **84 documentation cases** covering lexical,
  association, selection, Markdown, access, generated sources, reparse and
  executable-pipeline equivalence.
  [Debug results](bin/documentation-comments-20260918/test-debug.xml),
  [Release results](bin/documentation-comments-20260918/test-release.xml).
- The documented fixture emits byte-identical IR with collection enabled/disabled.
  LLVM verification, linking and **two native O0/O2 executions pass** with exact
  `ok` plus LF stdout, empty stderr and exit 0.
  [Native log](bin/documentation-comments-20260918/native.log).
- Release allocation/time samples: 64 lexical passes over 256 declarations use
  **6,144 bytes** both for ordinary comments and disabled documentation collection
  (zero additional allocation; disabled 13.11 ms). Enabled collection uses
  1,464,320 bytes / 35.76 ms. Thirty-two source parses without candidates use
  **234,496 bytes** with either setting (0.52/0.55 ms). These are bounded samples,
  not stable throughput guarantees; test XML retains both configuration samples.
- Modified specification links resolve; `git diff --check` passes. No draft edit
  or NativeAOT test was performed. [Artifact identities](bin/documentation-comments-20260918/verification.json).

Intermediate tests exposed over-wide ranges at synthetic layout tokens, omitted
excluded trailing comments, implicit Contract access, and associated-specification
publication boundaries; final tests cover each fix. Early input fixtures used
invalid enum/main/deinit/property spelling and were corrected to existing syntax.
The allocation check initially required an allocation-free entire lexer; it now
compares the existing lexical baseline with disabled documentation, as required.
An incompatible VSTest filter selected zero tests; only successful xUnit runs are
counted above. Sandboxed LLVM execution was denied; the authorized normal-permission
rerun passed. None of these intermediate attempts is counted as PASS evidence.

<a id="general-compiler-20260918-142715"></a>

## General compiler continuation (2026-09-18 14:27:15 UTC)

Started from clean HEAD `5b5591e3923e8caba3f7155df938fde69f69446f`. The user
explicitly authorized unfinished compiler Milestones and Checklist items for a
60-minute execution, excluding Milestone Program implementation. This supersedes
the previous program-15 stop/await-target instructions and the documentation-only
active scope; their completed implementation and verification remain intact.
The earlier documentation integration also left a stale program-15 next-action
paragraph, now replaced by the current general implementation action in PLAN.md.

T4r (M2/I4, R4; specification §§10.1–2, 10.5, 10.8 and 12.3): seven new
reproducers failed before implementation. Five valid tuple/array calls could not
bind; two overload-order variants incorrectly selected i32 after premature
literal defaulting. Five preservation cases (independent generic tuples, nested
tuples, temporary shared borrows and Unit) passed on the unchanged compiler.
Evidence: `bin/plan-execution/20260918-142715/t4r-baseline.xml` and
`t4r-preservation-baseline.xml`. An initial test helper used `Children` instead of
`ChildNodes`; corrected before running the reproducers. No failed build is treated
as behavior evidence. Current item states and next steps remain in PLAN.md §2.

T4r completion at approximately 14:52 UTC: candidate-local aggregate preparation
now includes tuple literals, collects structural input evidence before expected
results and numeric defaults, and commits only the winner. Bound member environments
and nested Origins are retained. Existing scratch/type intern storage is reused;
the focused warm Binding allocation test reports zero bytes.

The new native borrow case exposed an existing missing tuple-reference path.
Included its concrete scalar read implementation as a required downstream slice:
checked selector/layout, ref/uniq representation, explicit/implicit acquisition,
reference forwarding and normal cleanup. Additional lifetime tests caught missing
tuple temporary roots in Loan dependency collection; adding those roots rejects
references used after expression cleanup. The initial 18 borrowed-tuple checks
had 12 failures (unsupported shorthand/local representation and the lifetime
hole); all now pass. No requirement or test was weakened.

Verification PASS: warning-free Debug/Release xUnit project builds, full suites
with 8,845 tests each and no skips (`t4r-full-debug.xml`, `t4r-full-release.xml`).
The new classes contribute 53 cases relative to the 8,792-test starting suite.
Sixty archived fresh fixtures from AggregateArgument, BorrowedTuple, ArrayArgument,
BorrowedArray and BorrowStruct pass LLVM verification and 120 native O0/O2 runs
(`t4r-regression-native.log`). Their Release-generated bytes match the archived
Debug fixtures. Input hashes are in `t4r-source-hashes.json` and
`t4r-regression-fixture-hashes.json`. The sandbox initially denied opt.exe; the
same ordinary-native command passed with authorized escalation. NativeAOT NOT_RUN.

Commands: `dotnet build xUnitTest/xUnitTest.csproj -c <Debug|Release> --no-restore
--disable-build-servers -m:1 -p:EmitCompilerGeneratedFiles=false -v:minimal`;
`dotnet xUnitTest/bin/<configuration>/net10.0/xUnitTest.dll -parallelMode none
-failSkips -result-xml <evidence XML>`; `backend/windows-x64/test-scalars.ps1
-FixtureDirectory bin/plan-execution/20260918-142715/t4r-regression-fixtures
-OutputDirectory bin/plan-execution/20260918-142715/t4r-regression-native`.

At the next boundary, about 25 minutes had elapsed. Continued with T6a under the
same general compiler authorization; no Milestone Program source was modified.

T6a (M3/I6/I8, SPEC 13.2 and 13.7) completed at approximately 15:08 UTC.
Four initial reproductions failed: tuple simple/compound writes did not bind;
struct compound/increment reached unsupported ownership. Concrete borrowed tuple
scalar writes now bind, and numeric updates capture one exclusive child borrow
and old scalar value before RHS evaluation. The existing checked arithmetic and
field store pipeline is reused; unary results retain prefix/postfix semantics.
The first implementation double-converted an IncrementOne value ID into a place;
the focused increment test caught it. RHS alias tests required an actual child
reborrow, rather than merely holding the original receiver SSA value.

Verification PASS: warning-free Debug/Release builds, 184 focused managed tests,
and all 8,866 managed tests per configuration (21 new cases, no skips).
`t6a-full-debug.xml`, `t6a-full-release.xml`, `t6a-test4.xml`, and `t6a-native.log`
record results. All 103 freshly regenerated BorrowedFieldUpdate, BorrowedTuple,
BorrowStruct and ElementUpdate fixtures passed 206 LLVM/native O0/O2 runs,
including owned cleanup audits and overflow/no-cleanup exits. Input identities
are in `t6a-source-hashes.json` and `t6a-fixture-hashes.json`; fixture archive is
`t6a-fixtures`. Commands use the same build/xUnit/native forms listed above.
NativeAOT NOT_RUN. No Milestone Program or draft source changed.

A receiver-order test additionally exposed an existing conservative rejection:
inside `change(p: uniq/Counter)`, repeated `locate(p).value` writes, where locate
returns `uniq/Counter from p`, fail ComparisonLoanConflict because call-return
ancestry is not followed to the still-live parent. Direct owned-root calls
`locate(counter@uniq).value` pass the required once-only/order checks. The
returned-parent limitation remains recorded rather than weakening Loan checks.
At about 41 minutes elapsed, continued with T6b borrowed tuple projections.

T6b (M3/I6/I8, SPEC 15.1.3 and 15.2) completed at approximately 15:21 UTC.
All seven initial cases failed: aggregate tuple projections were unsupported,
and two shared-to-exclusive projections incorrectly passed Binding. Borrowable
Place checking now recognizes the tuple receiver's authority and excludes such
projections from the temporary-value fallback. Ownership and Lowering use the
existing checked borrowed-member address path with exact tuple selector/Type
and layout validation. Aggregate elements are borrowed without implicit Moves.

A later last-use case exposed missing ancestry through immutable reference locals.
The verifier now follows the actual single initialization value, with a bounded
walk and a reusable definition table. It does not equate unrelated Loans merely
because their Origins match. Overlapping child Loans still reject. The table is
rebuilt with each verification pass; warm ownership/emission allocates zero bytes.

Verification PASS: warning-free Debug/Release builds, 86 focused tests and all
8,882 managed tests per configuration, no skips (`t6b-test4.xml`,
`t6b-full-debug.xml`, `t6b-full-release.xml`). The 16 new cases cover shared and
exclusive projections, structure/tuple/array layout, forwarding, parent cleanup,
last use, invalid escape/conflicts/escalation, serialization and warm reuse.
Forty-five freshly archived BorrowedTuple, BorrowedFieldUpdate and BorrowStruct
fixtures pass 90 LLVM/native O0/O2 runs (`t6b-native.log`). Source/artifact and
fixture identities are in `t6b-source-hashes.json` and `t6b-fixture-hashes.json`.
No NativeAOT, draft or Milestone Program implementation. At about 54 minutes,
continued with T6c: explicit reborrow of a shared reference stored in a tuple.

T6c (M3/I6, stored shared-reference reborrowing) reproduced two valid programs
rejected during Lowering: `view.0@ref` on a stored `ref/Counter`, including a
reference used after its tuple wrapper ends. Both passed Binding/ownership but
failed `Projected borrow lacks a matching stored field and typed receiver.`
The explicit borrow now reads an already stored reference and reborrows that
value; only owned elements use the slot-address projection path. Exact Type and
dominance validation remains. The existing original referent Origin is preserved;
no wrapper lifetime extension or shared-to-exclusive promotion is introduced.

Four added cases bring the new-test total for this execution to 94. Focused
verification passes 90 tests across the borrowed tuple/projection/update/struct
families (`t6c-focused.xml`); Debug's full 8,886-test suite passes without skips
(`t6c-full-debug.xml`). Both final builds are warning-free. Forty-seven freshly
archived fixtures pass 94 LLVM/native O0/O2 runs (`t6c-native.log`); originals
and oracles are in `t6c-fixtures`, with `t6c-source-hashes.json` and
`t6c-fixture-hashes.json` recording input identities. Final Release confirmation
and execution stop time are recorded below after completion. NativeAOT NOT_RUN.

Final confirmation: Release also passes all 8,886 tests without skips
(`t6c-full-release.xml`); Debug/Release fixture bytes match, and every final
source/compiler/test-artifact hash still matches `t6c-source-hashes.json`.
Milestone Program and draft sources are unchanged; NativeAOT was not run.
Required language behavior was implemented under existing SPEC clauses, so no
normative specification rewrite was needed.

Stopped at 2026-09-18 15:28:22 UTC after 61.12 minutes of elapsed wall-clock work.
The 60-minute duration expired during final verification/documentation; no new
unit was started after the deadline. T4r/T6a/T6b/T6c are complete only for their
stated slices. Remaining compiler scope and the exact next action T6d are
maintained in PLAN.md §2. Final diff whitespace verification passes.

<a id="general-compiler-20260918-153130"></a>

## General compiler continuation (2026-09-18 15:31:30 UTC)

Started from clean HEAD `f31d2badffad84524b0009c230fcf526780e3305` under the
user's 60-minute general compiler authorization (Milestone Programs, NativeAOT
and draft edits excluded). Evidence root: `bin/plan-execution/20260918-153130`.

T6d (M3/I6, SPEC §15.2–15.4 returned Loans): three valid reproducers
(`relay(p).value += 1` then `p.value += 1`, repeated relays, nested relays)
failed with ComparisonLoanConflict at the call, projection and update
(`t6d-baseline.xml`). Cause: `IsBorrowAncestor` stopped at the Call value, so
a returned `uniq/Counter from p` could not be traced to the argument's reborrow
of the still-suspended parent. The walk now continues from a Call to the one
CallEntry named by the callee's declared `Input` result Origin, using the
receiver/explicit-argument parameter mapping. Defaults, intersections, value
calls and compiler functions return no ancestor (conservative rejection). No
Origin-name equality grants permission; no new allocation is introduced.

Verification PASS: warning-free Debug/Release builds; 11 new
ReturnedBorrowAncestryTest cases (5 native positives including second-slot and
named arguments and an immutable local; 5 negatives asserting complete Binding
plus ComparisonLoanConflict; shared-to-exclusive escalation rejected in
Binding). Full suites: 8,897 tests per configuration, no skips
(`t6d-full-debug.xml`, `t6d-full-release.xml`). Fifty-two freshly archived
ReturnedBorrowAncestry, BorrowedFieldUpdate, BorrowedTuple and BorrowStruct
fixtures pass 104 LLVM/native O0/O2 runs (`t6d-native.log`); Release-generated
ReturnedBorrowAncestry fixtures match the archived Debug bytes. Hashes:
`t6d-fixture-hashes.txt`, `t6d-source-hashes.txt`. NativeAOT NOT_RUN.

T6e (M3/I6, SPEC §15.6.2–15.6.3) completed at approximately 15:52 UTC.
Probing borrowed projections found three valid programs rejected with
ComparisonLoanConflict: disjoint `p.left@uniq`/`p.right@uniq` through a `uniq`
parameter, the Tuple equivalent, and two `p.left@ref` borrows. The value-based
check treated any access through a `uniq` source as conflicting with every live
dependent. It now requires a Uniq side (dependent or access) and exempts
proven-disjoint static paths: `IsDisjointProjection` walks single-definition
immutable locals and Borrow temporaries to the same root and compares inline
field/Tuple selectors (bounded depth 16, stackalloc; no heap allocation).
Borrow destinations now also record their single definition.

`BorrowedArrayEmissionTest.Rejects` contained
`let b = a@uniq / let r = b@ref / let n = b[0] / let m = r[0]`, rejected only
because of the removed blanket rule. Under §15.6.2 a Read is allowed against an
existing `ref` Loan, and §4.6.5/§6.3 state that a reborrow suspends only
conflicting parent access, so the case is valid; it is now the positive
`DisjointBorrowedProjectionArrayParentRead` fixture. The Rejects slot keeps a
conflict responsibility with `let c = b@uniq` while `r` lives, which reaches
ComparisonLoanConflict. Seven negative cases assert complete Binding plus
ComparisonLoanConflict (same-field uniq twice, ref then uniq, write through the
parent while a shared child lives, whole-parent uniq and call while a child
lives, Tuple element write).

Verification PASS: warning-free Debug/Release builds; full suites 8,909 tests
each, no skips (`t6e-full-debug.xml`, `t6e-full-release.xml`). Sixty-eight fresh
DisjointBorrowedProjection, ReturnedBorrowAncestry, BorrowedArray,
BorrowedFieldUpdate, BorrowedTuple and BorrowStruct fixtures pass 136 LLVM/native
O0/O2 runs (`t6e-native.log`); Release DisjointBorrowedProjection fixtures match
the archived Debug bytes. Hashes: `t6e-fixture-hashes.txt`,
`t6e-source-hashes.txt`. Probing also found nested direct field access through a
borrowed receiver (`p.left.value = 3`, `let x = p.right.value` with
`p: uniq/Pair`) reported Unsupported in ownership; selected as T6f.

T6f (M3/I6/I8, SPEC §15.6 Place projections) completed at approximately
16:06 UTC. Reproducers: `p.left.value = 3`, `let x = p.right.value` and
compound/increment forms with `p: uniq/Pair` were Unsupported in ownership.
Binding types an intermediate `p.left` as the owned `Counter` Place, so the
dispatch did not recognize a borrowed base. `ElementAccess.BorrowedPathRoot`
now walks inline stored field/Tuple levels (via the existing `TryType`) to the
reference-typed base; ownership reads/writes/updates use that base as the
receiver and its Semantics for write permission. Lowering sums each level's
`AggregateLayout.Offset` and validates each level's owner/element Types before
the single `ElementAddress`. `IsDisjointProjection` records every nested level
(an earlier leaf-only draft would have compared selectors of different
aggregates and was corrected before testing). Tuple-in-Tuple compound updates
(`p.0.1 += 2` with `p: uniq/((i32, i32), Counter)`) fail in Binding
(InvalidCount 1); recorded as a separate Binding gap, not claimed here.

Verification PASS: warning-free Debug/Release; 9 new cases (4 native nested
positives, 4 overlap negatives asserting complete Binding plus
ComparisonLoanConflict, 1 shared-base write rejection); full suites 8,918 tests
each, no skips (`t6f-full-debug.xml`, `t6f-full-release.xml`). Sixty-one fresh
DisjointBorrowedProjection, ReturnedBorrowAncestry, BorrowedFieldUpdate,
BorrowedTuple and BorrowStruct fixtures pass 122 LLVM/native O0/O2 runs
(`t6f-native.log`); Release nested fixtures match Debug bytes. Hashes:
`t6f-fixture-hashes.txt`, `t6f-source-hashes.txt`. NativeAOT NOT_RUN.

T6g (M2/I4-adjacent writability, M3/I6; SPEC §13.7 assignment targets and
§15.6 Places) completed at approximately 16:11 UTC. The T6f Binding gap
reproduced as InvalidCount 1 for `p.0.1 += 2`: `Writable` accepted a Tuple
element only when its immediate Left was a Tuple reference. It now accepts a
literal Tuple selector whose `BorrowedPathRoot` is a `uniq` base; ownership and
lowering reuse the T6f nested path. Four new cases: a native nested Tuple
positive (compound, assignment, postfix decrement, struct-in-Tuple write) and
`ref`-base compound/assignment rejections. Verification PASS: warning-free
Debug/Release; full suites 8,921 each, no skips (`t6g-full-debug.xml`,
`t6g-full-release.xml`); 115 fresh DisjointBorrowedProjection,
BorrowedFieldUpdate, BorrowedTuple and ElementUpdate fixtures pass 230 LLVM/native
O0/O2 runs (`t6g-native.log`); Release fixture bytes match. Hashes:
`t6g-fixture-hashes.txt`, `t6g-source-hashes.txt`.

T6h (M3/I6, SPEC §15.6) completed at approximately 16:19 UTC. Reproducers
`let a = p.inner.c@uniq` / `p.inner.c@ref` bound but were Unsupported at
`p.inner`. `BorrowStruct` now uses `BorrowedPathRoot` for inline paths;
lowering shares a `TryBorrowedPathOffset` helper (per-level owner/element Type
validation, checked summed offset) between projected borrows and borrowed
field access; `ProjectionPath` records every nested level of a Borrow source.
Ten new NestedBorrowedProjectionTest cases: 4 native positives (exclusive,
shared twice, nested siblings, Tuple siblings), 4 prefix-overlap negatives
asserting complete Binding plus ComparisonLoanConflict, and `uniq` through a
`ref` base rejected. The first draft of the exclusive positive used
`p.tag += a.value`; it rejects because `UpdateBorrowedField` reborrows the whole
receiver (existing single-level behavior, recorded as T6j rather than treated as
a T6h regression), so that case uses plain assignment.

Verification PASS: warning-free Debug/Release; full suites 8,930 tests each, no
skips (`t6h-full-debug.xml`, `t6h-full-release.xml`); 66 fresh
NestedBorrowedProjection, DisjointBorrowedProjection, ReturnedBorrowAncestry,
BorrowedFieldUpdate, BorrowedTuple and BorrowStruct fixtures pass 132
LLVM/native O0/O2 runs (`t6h-native.log`); Release nested fixtures match Debug
bytes. Hashes: `t6h-fixture-hashes.txt`, `t6h-source-hashes.txt`.

T6j (M3/I6, SPEC §15.6.2) completed at approximately 16:25 UTC. The
receiver reborrow of `UpdateBorrowedField` covered the whole base, rejecting
`p.tag += a.value` while `a = p.inner.c@uniq` lives. `ProjectionPath` now gives a
receiver Borrow whose source is the base of an update target (compound Binary
Left or increment/decrement Unary operand, found via `BorrowedPathRoot`) that
target's inline selectors; no other Borrow changes footprint. Existing
`KeepsTargetBorrowedAcrossRhs` same-field cases still reject. Four new cases
(one native sibling-update positive; same-path update, parent `ref` plus nested
sibling increment and same-field RHS negatives). Verification PASS:
warning-free Debug/Release; full suites 8,934 tests each, no skips
(`t6j-full-debug.xml`, `t6j-full-release.xml`); 130 fresh NestedBorrowedProjection,
DisjointBorrowedProjection, BorrowedFieldUpdate, BorrowedTuple, BorrowStruct and
ElementUpdate fixtures pass 260 LLVM/native O0/O2 runs (`t6j-native.log`);
Release nested fixtures match Debug bytes. Hashes: `t6j-fixture-hashes.txt`,
`t6j-source-hashes.txt`.

Execution totals: 48 new managed cases (8,886 → 8,934 per configuration), one
spec-incorrect rejection case corrected with its conflict responsibility kept.
No Milestone Program, draft or NativeAOT work; the specification was not
edited. An untracked user draft (`draft/Design/2026-09-19 Shared Ownership and
Using Guards.md`) appeared during the run and was not read or modified.
Stopped at 2026-09-18 16:25 UTC (about 54 minutes elapsed); the remaining time
could not fit another unit with its required full Debug/Release and native
verification. T6i is the next action in PLAN.md §2.

<a id="general-compiler-20260919-082645"></a>

## General compiler continuation (2026-09-19 08:26:45 JST / 2026-09-18 23:26:45 UTC)

Started from clean HEAD `6ca946e317bc0c55eb0b3d99d4f8d4b9732305b8` under the
user's 60-minute general compiler authorization (Milestone Programs, NativeAOT
and draft edits excluded). Evidence root: `bin/plan-execution/20260919-082645`.
The first Debug build failed because Markdig was not restored
(`CS0246 'Markdig'`); `dotnet restore Kimigayo.slnx -m:1` resolved it without
configuration changes.

T6i (M3/I6, SPEC §15.6.2) completed at approximately 08:49 JST. Reproducers
`var pair = Pair.init()` / `let a = pair.left@uniq` (and `@ref`, siblings,
sibling writes) reported Unsupported at `pair.left`. The owned path then
fell to an element Move, which produced cascading PossiblyMovedUse and
ComparisonLoanConflict diagnostics. Probing also found a pre-existing hole:
`let a = pair@uniq` / `let x = pair.left.value` / `a.left.value += x` verified,
because an element read names its root only through a projection and no
Origin-based Loan check treated it as an access.

Changes: `ElementAccess.OwnedPathRoot` recognizes a direct inline field/Tuple
path whose root is an owned local or parameter Name. `BorrowStruct` emits one
Borrow of the root Place for such a path (object handles and non-matching
Types excluded). Lowering validates the root/output Types and uses the
existing `TryBorrowedPathOffset` for a `BorrowAddress` at slot plus offset.
`ProjectionPath` records the owned path's selectors. `VerifyBorrows` treats
a `ProjectElement` under a live exclusive root Loan as an access. It exempts
Borrow/ProjectElement/WriteElement accesses whose static paths are disjoint;
element accesses compare their projection `Path`/`Selector` chain. Element
projections keep expression-scoped comparison Loans, so reusing them for a
local's lifetime (as the plan text suggested) would have needed a second
lifetime model. The Origin-based liveness already used by T6e was reused
instead. No new allocation: selector buffers remain stackalloc.

Boundaries retained: moving a sibling part while a part borrow lives still
rejects, because the root must remain fully initialized
(`let a = pair.left@ref` / `let m = pair.right`). Scalar-part and whole scalar
references (`let t = o.tag@uniq`, `let t = x@uniq` then `t += 1`) do not
complete Binding (UnresolvedCount 6); that is a separate area, not claimed
here. Generic shared-storage bodies reject the new Borrow form explicitly.

Verification PASS: warning-free Debug/Release builds; 25 new
`OwnedPathBorrowTest` cases (13 native positives including arguments,
receivers and sibling calls; 11 negatives asserting complete Binding plus
ComparisonLoanConflict, including the closed hole; an immutable-owner `uniq`
rejection). Before the new tests existed, the change passed the full Debug
suite (8,934, `t6i-pre-debug.xml`). Final full suites: 8,959 tests per
configuration, no skips (`t6i-full-debug.xml`, `t6i-full-release.xml`).
Native: 174 archived OwnedPathBorrow, NestedBorrowedProjection,
DisjointBorrowedProjection, BorrowedFieldUpdate, BorrowedTuple, BorrowStruct
and ElementBorrow fixtures pass 348 LLVM/native O0/O2 runs (`t6i-native.log`).
The five fixtures added later pass 10 more runs (`t6i-native-2.log`). Release-generated
OwnedPathBorrow fixtures match the archived Debug bytes. Hashes:
`t6i-fixture-hashes.txt`, `t6i-source-hashes.txt`. NativeAOT NOT_RUN.

T6k (M2/M3, I6; SPEC §15.6.2, R27) completed at approximately 08:58 JST.
Probing stored references found that `view.0@uniq` and the implicit argument
`bump(view.0)` bound completely with `view: ref/(uniq/Counter, bool)`. §15.6.2
states that borrowed access cannot grant exclusive authority; these programs
were rejected only by an ownership Unsupported at `view.0`. The `AdaptInput`
reborrow branch now refuses a `uniq` target when `ReachedThroughShared` finds a
`ref` base along the inline `BorrowedPathRoot` chain (bounded depth 64, no
allocation). Explicit borrows report InvalidAssignment and calls report
NoApplicableOverload. Two reproducers were added to
`BorrowedTupleProjectionTest.RejectsExclusiveProjectionThroughSharedReceiver`.

Verification PASS: warning-free Debug/Release; full suites 8,961 tests each, no
skips (`t6k-full-debug.xml`, `t6k-full-release.xml`). All 179 fixtures archived
for T6i regenerate byte-identically (Release), so their native results apply
unchanged; 12 BorrowedTupleProjection fixtures pass 24 O0/O2 runs
(`t6k-native.log`). Hashes: `t6k-fixture-hashes.txt`, `t6k-source-hashes.txt`.
Probing also recorded T6l (stored exclusive-reference reborrows from owned
storage and shared reborrows of stored `uniq` through `ref`, both Unsupported)
and scalar references not completing Binding; neither was changed.

T6m (M2/M3, I4/I6; SPEC §15.6.3) completed at approximately 09:11 JST. The
chapter's own example (`func bump(n: uniq/i32)`, `var v = 0`, `bump(v@uniq)`
twice) failed Binding with UnsupportedBinding at `v@uniq`; `bump(v)` worked.
The shorthand branch of `BindConversion` admitted only struct, fixed-array,
Tuple, Closure and reference operands. It now also admits scalar operands
whose source is a Name or member path, reusing the existing `AdaptInput`
Borrow/BorrowablePlace rules and existing ownership/lowering. A first draft
admitted every scalar operand. The full suites then showed `1.25@ref`
passing ownership but failing generation, a newly reachable path that
generation does not implement. The extension was restricted to Places, and
temporaries keep their Unsupported diagnosis. `ConversionEmissionTest`
contained `let x = 1\nx@ref` expecting Unsupported. That encoded the old
limit and conflicted with §15.6.3, so the case became `1@ref`, which is still
Unsupported and keeps the distinct-category responsibility.
`FloatConversionEmissionTest`'s `1.25@ref` case is unchanged and passes.

Verification PASS: warning-free Debug/Release; 7 new `ScalarBorrowShorthandTest`
cases (4 native positives: spec example, shared local, field path, bool; 2
ComparisonLoanConflict negatives; immutable-local `uniq` Binding rejection).
Full suites 8,968 tests each, no skips (`t6m-full-debug.xml`,
`t6m-full-release.xml`). 4 fixtures pass 8 O0/O2 runs (`t6m-native.log`), and
all previously archived fixtures regenerate byte-identically. Hashes:
`t6m-fixture-hashes.txt`, `t6m-source-hashes.txt`. Arithmetic directly on
scalar references (`n + 1` with `n: ref/i32`, `t += 1`) still does not bind and
was not changed.

Execution totals: 34 new managed cases (8,934 → 8,968 per configuration). One
obsolete Unsupported expectation (`let x = 1\nx@ref`) was replaced by
`1@ref`, as justified above. No Milestone Program, draft or NativeAOT work;
the specification was not edited. The user-owned draft
`draft/Design/2026-09-19 Shared Ownership and Using Guards.md` changed during
the run and was neither read nor modified. Stopped at about 09:12 JST (about 45
minutes elapsed). The remaining time could not fit another unit with its
required full Debug/Release and native verification, and the next item (T6l)
first needs an Origin decision. T6l is the next action in PLAN.md §2.

<a id="general-compiler-20260919-094939"></a>

## General compiler continuation (2026-09-19 09:49:39 JST / 00:49:39 UTC)

Started from clean HEAD `95731eb` under the user's 60-minute general compiler
authorization (Milestone Programs, NativeAOT and draft edits excluded).
Evidence root: `bin/plan-execution/20260919-094939`.

T6n (M2/M3, I4/I6; SPEC §10.2 "Borrowed temporaries", §13.5.5.2) completed at
approximately 10:05 JST. Reproducers: `read(one())` with
`read(n: ref/i32)` passed Binding and ownership, then generation failed with
"Borrow source has no matching aggregate storage". `read(1)` failed Binding
(NoApplicableOverload, UnsupportedBinding at the literal). Spec §13.5.4 also
confirms that a callee cannot read a scalar through `ref/i32` ("Safe
references are never implicitly dereferenced"). The native oracle is therefore
call completion and side-effect output, not the value.

Changes:
- `BorrowStruct` records the temporary's prepared value as the Borrow input for scalar temporaries.
- Lowering allocates a slot only for scalar temporaries that a Borrow uses (`IsMaterializedScalar`). It stores the value once at the borrow and borrows the slot, and it requires a shared result.
- Candidate evaluation lets an unfitted scalar literal that fits `T` form a CrossSemanticsBorrow with `SourceType = T`, so commit fits the literal to `T`.
- `VerifyBorrows` makes a borrowed scalar temporary a Loan root for its Projection Origin. Only borrowed temporaries qualify: a first draft admitted every scalar temporary with a matching binder, and the full suite caught a false conflict in `OwnedPathBorrowTest` "Parameter".
- The escape reproducer (`let r = keep(one())`, `let s = r`) verified before the root change. It now rejects.

Verification PASS: warning-free Debug/Release. `ScalarTemporaryBorrowTest` has 11 cases: 7 native positives, including literal/float/rank; the escape negative; and `uniq` literal, ambiguity and unrepresentable-literal Binding rejections. Full suites: 8,979 tests each, no skips (`t6n-full-debug.xml`, `t6n-full-release.xml`). 46 ScalarTemporaryBorrow, ScalarBorrowShorthand, OwnedPathBorrow, BorrowStruct and BorrowedTupleProjection fixtures pass 92 O0/O2 runs (`t6n-native.log`), and Release bytes match Debug. Hashes: `t6n-fixture-hashes.txt`, `t6n-source-hashes.txt`.

T6o (M2/M3, I6; SPEC §3.6.1–3.6.2, §13.5.5.2) completed at approximately 10:11
JST. T6m had restricted scalar shorthand borrows to Places, because
generation could not lower temporaries. With T6n materialization available,
the restriction was lifted. A probe then showed `1@uniq` and `one()@uniq`
passing ownership but failing generation, because the materialization
lowering required a shared result. §3.6.1 gives a new owned temporary
exclusive writable capability, and §3.6.2 permits the applicable explicit
exclusive borrow. The shared-only check was therefore removed; implicit
`uniq` temporary adaptation stays rejected in Binding (§10.2). `ConversionEmissionTest`'s
`1@ref` and `FloatConversionEmissionTest`'s `1.25@ref` Unsupported cases were
removed as spec-contradicting. `1.25@ref` is now a native positive, which
also shows it is not treated as a numeric widening. Verification PASS:
warning-free Debug/Release; full suites 8,982 each, no skips
(`t6o-full-debug.xml`, `t6o-full-release.xml`); 15 ScalarBorrowShorthand and
ScalarTemporaryBorrow fixtures pass 30 O0/O2 runs (`t6o-native.log`); the 46 T6n
fixtures are byte-identical. Hashes: `t6o-fixture-hashes.txt`,
`t6o-source-hashes.txt`.

T6p (M2/M6, I4/I14; SPEC §10.2) completed at approximately 10:20 JST. §10.2's
generic examples `inspect(1)` / `inspect(1.5)` with `inspect<T>(value: ref/T)`
failed with NoApplicableOverload. Literal defaulting called `InferInput` →
`AdaptInput` with the default Type, but the temporary form required an already
bound source Type. `AdaptInput` now also admits an unfitted literal as an owner
temporary for a shared target only. The T6n candidate path then records a
Borrow with the literal fitted to `T`. A generic `uniq/T` literal
(`modify(1)`) still rejects.

A first test version put `Console.writeLine` inside the generic body and failed
generation ("Shared operation refers to invalid storage or projection."). The same
failure occurs for a plain local argument (`inspect(v)`) with the HEAD `95731eb`
compiler sources: the `Kimi/` changes were stashed, the tests rebuilt and the
probe rerun, then the changes restored. It is recorded as G10 and the test uses
an empty generic body with caller-side output.

Verification PASS: warning-free Debug/Release; full suites 8,984 each, no skips
(`t6p-full-debug.xml`, `t6p-full-release.xml`); 16 ScalarTemporaryBorrow and
ScalarBorrowShorthand fixtures pass 32 O0/O2 runs (`t6p-native.log`); the T6n
archive is byte-identical. Hashes: `t6p-fixture-hashes.txt`,
`t6p-source-hashes.txt`.

T6q (M2/M3, I4/I6; SPEC §10.2, §15.6.4) completed at approximately 10:26 JST.
`keep(1)` with `func keep(n: ref/i32) -> ref/i32 from n` failed Binding with
UnprovenConstraint: the T6n literal-borrow candidate branch never ran
`InferInput`, so the result Origin stayed unsubstituted. The branch now calls
`InferInput` with the referent Type, which also exercises the T6p literal
temporary admission. `read(keep(1))` executes, and `let r = keep(1)` with a
later use of `r` rejects. Verification PASS: warning-free Debug/Release; full
suites 8,986 each, no skips (`t6q-full-debug.xml`, `t6q-full-release.xml`).
Those full runs preceded a whitespace-only SA1001 fix in the test file; after
it, both configurations rebuilt warning-free and `ScalarTemporaryBorrowTest`
(15 cases) passed in each. 17 fixtures pass 34 O0/O2 runs (`t6q-native.log`),
and the T6n archive is byte-identical. Hashes: `t6q-fixture-hashes.txt`,
`t6q-source-hashes.txt`.

G10 analysis: shared generic bodies admit direct calls only to generic
targets, and a Unit-result call (for example `Console.writeLine`) has no Place.
It therefore fails `GenericStoragePlan.PrepareBody` validation before any call
lowering. Supporting non-generic calls in shared bodies needs a
string/argument ABI for shared frames (M6/I15 "general calls"), which was
too large for the remaining time.

T6r (M3/I6, regression coverage) completed at approximately 10:30 JST.
Probes showed that scalar-field borrows through borrowed bases (`p.tag@uniq`,
`bump(p.tag)`, `read(p.tag)`, a live field borrow with a sibling write) and
owned Tuple scalar elements already bind, verify and emit through existing
T6f/T6h/T6i/T6m paths. Only tests were added to `ScalarBorrowShorthandTest`:
3 native positives and the `ref`-base `@uniq` Binding rejection. Verification
PASS: warning-free Debug/Release; full suites 8,990 each, no skips
(`t6r-full-debug.xml`, `t6r-full-release.xml`); 11 fixtures pass 22 O0/O2 runs
(`t6r-native.log`), and Release bytes match. Hashes: `t6r-fixture-hashes.txt`,
`t6r-source-hashes.txt`.

Execution totals: 29 new managed cases (8,968 → 8,990 per configuration, net
of 2 removed spec-contradicting Unsupported cases, as justified in T6o). No
Milestone Program, draft or NativeAOT work; the specification was not edited.
Stopped at about 10:31 JST (about 41 minutes elapsed). The remaining
candidates needed more than the remaining time for implementation plus full
Debug/Release and native verification: T6l needs an Origin decision, G10 a
shared-frame non-generic call ABI, and sibling Moves under a live part borrow
path-aware root initialization.


<a id="kimi-library-organization"></a>
## Kimi library organization (KL, 2026-09-19)

Completed the requested source/catalog refactor on baseline HEAD
`55ad003a92b3ed4b067409f8e1576431e731f01e`. The saved original KimiLibrary source
matches that commit's blob. This supersedes the earlier timed execution as the
current bounded checkpoint; product-wide unfinished APIs remain in PLAN.

- Moved the existing definitions into five embedded `Kimi/Library/*.kimi` sources.
  Ordinary source bodies remain ordinary compiler input. Signature-only loading
  is private to the registered Console/Intrinsics groups; user syntax is unchanged.
- Centralized stable declaration identities and compiler hooks. Retired the old
  ObjectOwnership aggregate slot without reusing its numeric ID; 30 individual
  entries now include makeObj and the missing ownership/Weak declarations. Thirteen
  entries validate; this is not a certificate of complete API/runtime support.
  `SourceExpected` distinguishes accidental removal of supported source from an
  intentionally missing implementation. ID lookup is independent of list positions.
- Replaced binding's repeated declaration lists with tree-driven indexing/binding.
  Checked signature-only group shells retain their scopes without redundant node
  passes. Directly called ordinary library bodies are collected by originating
  Kotonoha identity, replacing the Slice/SliceIterator-only ownership collector.
- Separated explicit declaration checks and bound-identity checks from status
  queries. Rebind and emission validate contracts; invalid libraries stop before
  indexing. File-specific source locations survive parsing. Helpers may add methods
  to Slice/Option without changing compiler-managed Slice storage or enum Cases.
- Shared immutable source text and successfully lexed token arrays, with atomic
  publication. No cache retains a compilation, SourceDocument or AST. Lexical errors
  are not cached; each compilation receives its own diagnostics. Added a narrow
  internal TokenReader entry point for immutable token spans. Recognition scans
  existing backing lists as spans rather than allocating snapshots or caching
  syntax positions. ASTs, symbols and semantic state remain compilation-local.

Verification root: `bin/kimi-library-refactor/`.

- Compiler/test builds: `dotnet build xUnitTest/xUnitTest.csproj -c Debug`
  and `-c Release`, both with `--no-restore --nologo -v quiet`: zero warnings/errors.
  The updated Benchmark project also builds warning-free in Release.
- Final suites: `dotnet xUnitTest/bin/<configuration>/net10.0/xUnitTest.dll
  -noLogo -result-xml bin/kimi-library-refactor/full-<configuration>.xml`.
  Debug and Release each pass **8,998**, zero failures/skips. CoreCatalogTest has
  31 cases including concurrent compilation isolation, file locations, declaration
  reordering, helper binding/emission, malformed signatures, missing/duplicate
  declarations, storage rejection and the existing zero-allocation warm Bind check.
- Native: `backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain
  -FixtureDirectory bin/kimi-library-refactor/native-inputs
  -OutputDirectory bin/kimi-library-refactor/native`. Eight fixtures exercise Slice,
  its iterator, whole-value operations/aliases/user-function identity and object
  exchange/zero-sized cleanup. LLVM verification, O0/O2 generation and exact
  stdout/stderr/exit checks pass **16 executions**. All **40** final Debug-generated
  IR/oracle files match the previously executed frozen Release inputs; executions
  are not double-counted. See `native.log`, `native-inputs.json` and
  `native-final-comparison.txt`.
- NativeAOT was not run. No language behavior was intentionally added; SPEC's
  existing loaded/synthesized Kimi identity requirements remain authoritative.

Performance evidence: `perf/Program.cs`, `perf-paired.json` and
`perf-summary.json`. The preserved baseline and final Release compiler DLLs are
loaded in separate AssemblyLoadContexts in one process; paired samples alternate
execution order. `DOTNET_TieredCompilation=0`, 1,000 warmups per variant, 14 samples
per operation/variant; each sample runs 2,000 library creations, 1,000 fresh small
program parses/binds, or 5,000 warm binds. The source cache is warm; this does not
measure first-process resource loading or establish a general compiler speedup.

| Operation | Baseline median | Final median | Baseline/final allocated bytes per operation |
| --- | --- | --- | --- |
| Fresh library, cached sources | 16.78 us | 14.58 us | 47,720 / 47,648 |
| Fresh small program parse + Bind | 110.48 us | 107.20 us | 103,313 / 103,345 |
| Warm Bind, same tree | 68.92 us | 69.36 us | 0 / 0 |

Median paired changes are -12.68%, -2.22% and +2.14%, respectively. Warm timings
vary substantially (baseline 57.04–85.68 us; final 58.83–78.59 us), so no strict
warm-throughput improvement or universal no-regression claim is made. Initialization
is faster in this bounded probe, fresh Bind adds 32 bytes (about 0.03%), and repeated
Bind retains zero allocation. The tracked `KimiLibraryBenchmark` covers all three
operations for future measurements; BenchmarkDotNet timing runs were not used here.

Intermediate findings and fixes:

- The initial `dotnet test --filter` invocation selected zero tests under the
  configured runner; all reported counts use the explicit xUnit DLL runner.
- A duplicate synthetic function node exposed a hang when malformed library syntax
  reached indexing. The pre-binding validation failure now returns before indexing.
  A helper test initially used a root function (ordinary startup syntax) and an
  unprepared fixed-array target; it now uses a group and prepares the target.
- Early separate-process measurements were disturbed by host pauses/load. Raw
  trial files are retained but are not the final comparison. The measurements
  motivated immutable token caching and avoiding redundant signature-group passes.

The current outcome and next actions are recorded only in PLAN.md. Documentation
updates do not change the declared runtime support boundaries.

<a id="general-compiler-20260919-045448"></a>

## General compiler implementation — G10a (2026-09-19 04:54:48 UTC)

Started from clean HEAD `5d9e12c82b878f6b8b7907795e1b669f6884a6e5` under a
30-minute implementation window. User constraints: do not run existing tests or
build-check examples/milestones; keep new tests minimal; update documents at the
end. Evidence root: `bin/plan-execution/20260919-045448`.

G10a (M6/I15). Reproducers in the new `SharedConcreteCallTest` failed as
recorded for G10: "Shared direct call to 'twice' requires a checked generic
function and explicit arguments." for a scalar-result concrete call, and "Shared
operation refers to invalid storage or projection." for a Unit-result call.
Changes (`GenericStoragePlan.cs`, `LlvmModuleWriter.GenericStorage.cs`):
- A Unit-result direct call may have no result Place.
- `IsConcreteDirect` admits ordinary free functions with no receiver, constructor,
  defaults, Type arguments or compiler function. `ConcreteAdapter` builds the
  existing `SharedDirectAdapter` from the function's verified `FunctionAbi`;
  the adapter writer was already ABI-generic.
- `IsSharedScalar` covers bool and 8/16/32/64-bit integers; `SharedScalarType`
  and `SharedScalarAlignment` replace the i1/i32/i64-only mapping.
- Binary scalar operations carry an explicit operation: `s|u` + add/sub/mul
  (overflow intrinsics), div/rem (division by zero, and the signed `MIN / -1`
  including `%` per SPEC §13.4, Abort with the existing reasons), and
  `predicate:type` comparisons (`eq`/`ne`, signedness-aware relational ones).
  `==`/`!=` also accept bool operands.
- Unary `not`, `+` and checked `-` (signed only).
- Common-function callback results accept the same scalar set.

Findings during the unit: `and`/`or` in a shared body produce Phi values, which
`ValidateSharedGraph` rejects by design ("Shared result joins still use secured
storage"). A `Console.writeLine` shared body now reaches the string-leaf
rejection in `Add`. Both remain G10 work. Mutable locals, compound assignment,
`while` and `if` with the new scalar types already lowered. The PLAN question on
scalar reference arithmetic was resolved from SPEC §13.4.3 and §3.2: safe references
are never implicitly dereferenced, so it is not an implementation gap.

Verification: warning-free Debug and Release builds. 14 new cases PASS in both
configurations (`g10-debug.xml`, `g10-release.xml`): 10 positives (concrete
Unit/scalar/wide calls, operators, unary, division, unsigned, mutable locals,
callbacks, bool equality) and 4 native Abort cases (checked negation, division
by zero, `i8` remainder overflow, `u8` addition overflow). 14 fixtures pass 28
LLVM/native O0/O2 runs (`g10-native.log`); Release fixture bytes match Debug.
Hashes: `g10-hashes.txt`. Existing managed tests, existing fixtures and
example/milestone builds were NOT_RUN by instruction; the pre-existing
shared-generic regression gate is therefore open and is the first next action in
PLAN.md. NativeAOT NOT_RUN; no draft or Milestone Program edits.

Implementation stopped at about 05:17 UTC (about 22.5 minutes). The remaining G10
items (Phi joins, string leaves) could not be completed and verified in the rest
of the window.

<a id="general-compiler-20260919-052044"></a>

## General compiler implementation — 50-minute window (2026-09-19 05:20:44 UTC)

Start time recorded before inspection; clean HEAD `a0f9b4c`. This request
supersedes prior checkpoint-specific stopping/testing restrictions, excludes
Milestone Program implementation, draft edits and NativeAOT, and authorizes
unfinished M/I implementation work. Evidence root:
`bin/plan-execution/20260919-052044`.

G10a regression gate PASS: warning-free `dotnet build xUnitTest/xUnitTest.csproj
-c Debug|Release --no-restore --nologo -v quiet`; explicit xUnit DLL runner
`dotnet xUnitTest/bin/<configuration>/net10.0/xUnitTest.dll -noLogo -result-xml
<root>/baseline-<configuration>.xml` passes 9,012 tests per configuration,
zero failures/skips. Frozen Generic*/SharedConcrete* fixtures (69) pass 138
LLVM verification/native O0/O2 executions using
`backend/windows-x64/test-scalars.ps1 -ToolchainRoot ./toolchain
-FixtureDirectory <root>/baseline-fixtures -OutputDirectory <root>/baseline-native`.
Debug/Release bytes match; hashes in `baseline-hashes.json`. The initial sandbox
invocation could not execute pinned opt.exe; authorized escalation passed.
This is ordinary native verification, not NativeAOT.

G10b reproducer: all 9 initial SharedResultEmissionTest cases failed with
"Shared body has an inconsistent value-flow plan." before the change
(`g10b-repro.xml`). Shared graph validation rejected all Phi values. Shared
storage now retains validated arrival value/predecessor pairs, writes scalar
Phis and materializes their result into the existing storage. Dedicated arrival
labels preserve LLVM predecessor identity after checked arithmetic and
conditional destruction split an operation block. Ordinary and shared bodies
reuse the same result-arrival validator; shared coverage uses retained incoming
counts. No source body rebinding or new semantic representation was introduced.
Verification remains in progress; current status/next actions belong in PLAN.md.

G10b PASS: 14 new tests (10 native positives and four corrupt-arrival/reanalysis
cases). Debug/Release builds are warning-free and all 9,026 managed tests pass
per configuration (`g10b-full-*.xml`). Frozen shared-generic/result/cleanup inputs
contain 127 fixtures and pass 254 LLVM/native O0/O2 executions
(`g10b-native.log`); Release-generated bytes match frozen Debug inputs.
`g10b-hashes.json` preserves their hashes. Short-circuit side effects/skipped
overflow, checked arithmetic arrivals, three/one arrivals, dead joins, nested
loops and observable destruction-before-return are covered. G10b's bounded
criteria are complete; M6/I15 remain open. No specification changes were needed.

G10c PASS: the 10 initial SharedStringEmissionTest reproducers failed at the
string-leaf restriction (`g10c-repro.xml`). Owned string leaves now use existing
24-byte layouts, Copy/Move facts and destruction policies. Static literal
production interns UTF-8 constants; the canonical WriteLine symbol uses the
existing runtime ABI and call-site location after the same checked argument
acquisition as direct calls. No name-based dispatch or new allocation path.

Twelve new cases cover non-ASCII/embedded NUL/empty strings, local replacement
and self-Move, conditional Moves, loops, fixed string parameters, concrete and
generic forwarded string results, selections, scalar-join cleanup and a
same-named ordinary function. Every case has a companion lifetime-instrumented
fixture (Static handles; no claim of Heap construction). Warning-free
Debug/Release builds each pass 9,038 tests (`g10c-full-*.xml`); 124 frozen
SharedString/SharedResult/SharedConcrete/GenericStorage/StringFunction fixtures
pass 248 LLVM/native O0/O2 runs (`g10c-native.log`), with identical Debug/Release
bytes and hashes in `g10c-hashes.json`. The original G10 writeLine reproducer is
now verified. Initial test-data style warnings were fixed before final builds.

G10d reproducer: six shared Abort/require cases fail before the change
(`g10d-repro.xml`). Canonical Abort calls now retain their owned message
acquisition, use the Macro node's source location, require Never/no normal CFG
successor and emit the existing noreturn runtime call followed by unreachable.
No cleanup is introduced on Abort. A corrupt normal-edge test rejects output
and recovers after ownership reanalysis. Two intermediate build mistakes
(the actual AST wrapper is MacroKoto; mutable test edges use EdgeStorage)
were corrected before verification.

G10d PASS: warning-free Debug/Release builds, 9,045 managed tests per
configuration (`g10d-full-*.xml`), seven new focused tests including corruption
recovery, and 12 new normal/audited fixtures / 24 LLVM/native O0/O2 executions
(`g10d-native.log`). Expected source positions, exact stderr/stdout, exit and
release counts all match. Debug/Release bytes match; all 124 frozen G10c
fixtures also regenerate byte-identically, so their prior native evidence is
retained without rerunning it. Hashes: `g10d-hashes.json`.

G10e PASS: six new SharedNeverEmissionTest cases reproduce missing concrete
Never adapters and shared Never storage/entries. Never now has a null result
representation, no leaves/scratch/policy, and noreturn shared-body, entry and
adapter contracts. Calls and specializations end in unreachable. The existing
normal-edge dominator graph separates actual execution from the ownership
analysis's Abort exit; the latter previously emitted an unreferenced ret block.
The first implementation incorrectly treated that Abort exit as a normal exit;
using normal-path reachability fixed it. Never selection Places likewise carry
no physical value. No Unit-value substitute was introduced.

Warning-free Debug/Release builds each pass 9,051 tests (`g10e-full-*.xml`).
121 freshly regenerated Generic*/Shared* fixtures pass 242 LLVM/native O0/O2
executions (`g10e-native.log`), including 300-ms bounded divergence with process
cleanup. Concrete, generic, forwarding, skipped and explicitly specialized
Never calls execute with exact diagnostics. Debug/Release bytes match; hashes
are in `g10e-hashes.json`. Shared calls changed physical reachability, so the
full affected native family was rerun rather than relying on old hashes.

G10f PASS: all 11 initial bitwise/shift reproducers fail before the change
(`g10f-repro.xml`). Shared scalar plans now retain bitwise operations and the
original shift-count width. Unsigned comparison rejects negative/out-of-range
counts before any count conversion; only a validated count is extended/truncated
for LLVM. Signed right shift uses ashr, unsigned uses lshr, and left-shift bit
loss is not overflow. Tests cover independently typed counts, compound updates,
left-to-right single evaluation, Phi/short-circuit interactions and four native
Abort boundaries including u64 maximum and 256 before narrowing to i8.
Warning-free Debug/Release builds each pass 9,062 tests (`g10f-full-*.xml`);
11 new fixtures pass 22 LLVM/native O0/O2 runs (`g10f-native.log`), with matching
configuration bytes and `g10f-hashes.json`. All 121 G10e fixtures are unchanged.

G10g PASS: five initial constructor/receiver cases failed at the concrete-call,
reference-reborrow or ordinary owned-method gates (`g10g-repro.xml`). Concrete
constructors and methods now use the existing checked argument mapping and
verified ABI. Shared reference-handle reborrows require matching referents,
compatible authority, initialized storage and an adjacent validated Read;
parent storage stays live. Concrete owner receivers use existing aggregate
parameter, projection and cleanup lowering, verified before opening that gate.
Eight tests cover shared/exclusive/owned receivers, constructors, temporary and
zero-sized receivers, named argument evaluation order, exact owned-field release
order, overlapping access rejection and corrupt reborrow/reanalysis. Warning-free
Debug/Release builds each pass 9,070 tests (`g10g-full-*.xml`); eight new fixtures
pass 16 LLVM/native O0/O2 runs (`g10g-native.log`), with matching configuration
bytes and `g10g-hashes.json`. All 132 G10e/f fixtures remain byte-identical.

G10h implementation: all 11 initial shared integer-conversion cases fail before
implementation (`g10h-repro.xml`). The existing ordinary conversion plan and
LLVM writer now serve shared 8–64-bit integer conversions. Source representation
and range predicates are retained, including checked widening/same-width sign
changes; only checked values reach truncation. Optional conversion-plan storage
is allocated only for bodies that use conversions. Phi arrival labels remain
valid across the reused writer's check blocks. Native verification PASS:
11 fixtures / 22 O0/O2 runs (`g10h-native.log`), covering signed/unsigned widening,
narrowing, equal-width/isize forms, skipped conversion branches and four Abort
bounds. Initial g10h suites passed 9,081 per configuration; the formatting
warnings from the first build were corrected before final warning-free builds.

Final review added a corruption regression that redirects the shared entry to
checking-only code after Abort. The first attempted post-dominator check exposed
an IndexOutOfRangeException in dominator construction (saved as
`reachability-review-failure.*`). Validating edge reachability before construction
fixes the root cause; all eight SharedAbortEmissionTest cases pass, including
reanalysis recovery (`final-corruption.xml`). Final whole-suite verification completed for the corrected validator; no new implementation unit was started
after the 06:10:44 UTC deadline.

Final PASS: warning-free Debug and Release builds each pass 9,082 managed
tests (70 new cases), with zero failures, skips or unrun tests
(`final-build-*.log`, `final-debug.xml`, `final-release.xml`). G10b–h add
82 native fixtures, each verified with LLVM and native O0/O2 execution, alongside
the recorded regression runs. All 755 files across the final 151 distinct shared
fixtures match their native-tested copies (`final-fixture-comparison.json`);
G10h hashes are in `g10h-hashes.json`, and final changed source hashes are in
`final-source-hashes.json`. Final validation therefore retains the native
execution evidence without redundant reruns of byte-identical artifacts.
No draft or Milestone Program source was edited, and no NativeAOT tests ran.
The compiler-wide M6/I15 family remains incomplete; the bounded G10b–h criteria
are verified. Stopping reason: requested duration elapsed, followed by completion
of the active unit, corruption fix, required verification and documentation.
Checkpoint closed 2026-09-19 06:18:41 UTC: 57 minutes 57 seconds elapsed from the recorded pre-inspection start.


## Superseded implementation checkpoint before test implementation

Current checkpoint: **General compiler implementation — completed 50-minute execution window**, started 2026-09-19 05:20:44 UTC before inspection, from clean HEAD `a0f9b4c`. Checkpoint closed at 06:18:41 UTC (57 minutes 57 seconds elapsed). No new implementation unit started after the 06:10:44 UTC deadline; the active unit finished with final verification and documentation. Previous checkpoint-specific restrictions, including the G10a-only testing restrictions, are superseded. Milestone Programs remain excluded as implementation targets and stopping conditions. G10a's regression gate and G10b–h are DONE for their bounded criteria; G10 and M6/I15 remain IN_PROGRESS. Final warning-free Debug/Release builds each pass 9,082 tests; 151 final fixtures match their native-tested copies byte-for-byte. Evidence root: `bin/plan-execution/20260919-052044`; [execution evidence](PLAN_HISTORY.md#general-compiler-20260919-052044). Next action: reproduce shared `f32`/`f64` storage and conversions under M6/I15, starting with `func widen<T>(value: ref/T, n: f32) -> f64 => n@f64`; verify the required ordinary ABI and conversion semantics before extending shared lowering. This next slice is unimplemented and unverified.

## Compiler checkpoint before test implementation (2026-09-19)

T4r (M2/I4 with M3/I6 borrowed-tuple reads) DONE for its bounded criteria: warning-free Debug/Release builds, 8,845 managed tests per configuration and 60 fresh regression fixtures / 120 O0/O2 executions. Shared/unique tuple scalar reads, caller temporaries, expression-end lifetime rejection, nested Origin evidence and one-time owned cleanup are verified. Broader M2/M3 criteria remain open. [Evidence](PLAN_HISTORY.md#general-compiler-20260918-142715).

T6a (M3/I6/I8) DONE for concrete scalar borrowed-field updates: exclusive tuple assignment and tuple/struct numeric compound and integer prefix/postfix updates preserve evaluation order, RHS protection, checked arithmetic and transfer/Abort. Debug/Release each pass 8,866 tests; 103 fresh fixtures pass 206 O0/O2 executions. General shared generic bodies and returned-exclusive ancestry through a still-live borrowed parent remain incomplete. [Evidence](PLAN_HISTORY.md#general-compiler-20260918-142715).

T6b (M3/I6/I8) DONE for stored aggregate projections through borrowed tuples and single-initialization immutable reference-local ancestry. Shared/exclusive permissions, parent lifetime, last use, returned shared projections and no implicit aggregate Moves are verified. Debug/Release each pass 8,882 tests; 45 fresh fixtures pass 90 O0/O2 runs. Warm ownership/emission adds zero allocated bytes. General disjoint projection paths and call-return ancestry remain incomplete.

T6c (M3/I6) DONE: explicit reborrowing of a shared reference stored in a borrowed tuple reads the reference instead of borrowing its slot. The original Origin is retained, including use after the wrapper ends; shared-to-exclusive escalation and use after referent replacement reject. Final warning-free Debug/Release builds each pass all 8,886 managed tests with no skips. Forty-seven fresh fixtures pass 94 O0/O2 runs; Debug/Release fixture bytes match and final source/artifact hashes are unchanged. Evidence root: `bin/plan-execution/20260918-142715`.

T6d (M3/I6) DONE: the Loan verifier follows a returned reference's ancestry to the one acquired argument named by the callee's public `from` result Origin (receiver, source-order explicit arguments with their parameter mapping; defaults and intersections stop conservatively). `relay(p).value += 1` followed by use of `p`, repeated/nested relays, named/second-slot selection and immutable locals initialized from calls now verify; parent use while the returned Loan lives, sibling returned Loans, RHS overlap and shared-to-exclusive escalation still reject. Debug/Release each pass 8,897 tests (11 new); 52 fresh fixtures pass 104 O0/O2 runs. [Evidence](PLAN_HISTORY.md#general-compiler-20260918-153130).

T6e (M3/I6) DONE: Loan checks apply SPEC §15.6.2 paths and permissions to borrowed projections. Distinct inline field/Tuple selectors under the same root (including live exclusive update targets) are disjoint; a shared access against a shared dependent no longer conflicts merely because the parent is `uniq` (§15.6.3 suspends only conflicting access). Array subscripts, stored-reference referents, calls and different roots stay conservative. A spec-incorrect BorrowedArray rejection case (a Read through a `uniq` parent while a shared child lives) now executes as a positive case; its Rejects slot uses `b@uniq` while the shared child lives. Debug/Release each pass 8,909 tests; 68 fresh fixtures pass 136 O0/O2 runs.

T6f (M3/I6/I8) DONE: direct nested inline paths below a borrowed struct/Tuple base (`p.left.value` reads, `=` writes, compound and increment updates, struct-in-Tuple `p.1.value`) bind to one borrowed-field access with a validated summed layout offset; each level's selector joins the Loan path check, so overlapping levels (`p.left.value` against a live `p.left` borrow, parent `ref` against a nested write) reject while sibling paths execute. Writes through a `ref` base still reject. Debug/Release each pass 8,918 tests; 61 fresh fixtures pass 122 O0/O2 runs.

T6g (M2/M3, I6) DONE: Binding writability recognizes an inline Tuple element below a borrowed base (`p.0.1 += 2`, `p.0.0 = ...`, `p.0.1--` with `p: uniq/((i32, i32), Counter)`), requiring the base's `uniq` permission; `ref` bases still reject. Debug/Release each pass 8,921 tests; 115 fresh fixtures pass 230 O0/O2 runs.

T6h (M3/I6) DONE: explicit borrows of nested inline Places below a borrowed base (`p.inner.c@uniq`, `p.inner.d@ref`, Tuple `p.1.0@uniq`) share T6f's path root, validated summed offset and per-level Loan selectors. Nested siblings execute; prefix overlaps (`p.inner@uniq` or a write to `p.inner.c.value` while `p.inner.c` is borrowed, `ref` parent with nested `uniq`) reject, and `uniq` through a `ref` base rejects. Debug/Release each pass 8,930 tests; 66 fresh fixtures pass 132 O0/O2 runs.

T6j (M3/I6) DONE: a compound/increment update's single receiver reborrow now has the updated inline field path as its Loan footprint, so sibling updates (`p.tag += a.value`, `p.inner.d.value += ...`, `p.tag++`) execute while `a = p.inner.c@uniq` lives; same-path updates, parent `ref` with a nested sibling update and same-field RHS reads still reject. Debug/Release each pass 8,934 tests; 130 fresh fixtures pass 260 O0/O2 runs.

The 60-minute execution of 2026-09-18 stopped after T6j ([stop record](PLAN_HISTORY.md#general-compiler-20260918-153130)). The 2026-09-19 08:26 JST execution completed T6i, T6k and T6m ([stop record](PLAN_HISTORY.md#general-compiler-20260919-082645)). The 09:49 JST execution completed T6n–T6r and stopped at about 10:31 JST ([stop record](PLAN_HISTORY.md#general-compiler-20260919-094939)); T6l and sibling Moves remain independent alternatives to the active G10 work below.

T6i (M3/I6, SPEC §15.6.2) DONE: explicit and implicit borrows of inline parts of owned locals and parameters (`let a = pair.left@uniq` / `@ref`, nested struct and Tuple paths, call arguments and `uniq` receivers such as `pair.left.bump()`) borrow the part in place at a validated summed offset. The Loan footprint is the static path, so sibling borrows, element reads/writes and sibling call arguments execute. Overlapping reads, writes, borrows, arguments, whole-root replacement and Moves reject. A pre-existing hole was closed: an element read through an owned root (`pair.left.value`) under a live exclusive whole-root Loan (`a = pair@uniq`) previously verified. Debug/Release each pass 8,959 tests (25 new); 179 fresh fixtures pass 358 O0/O2 runs. [Evidence](PLAN_HISTORY.md#general-compiler-20260919-082645).

Deviation from the T6i plan text: element projections carry expression-scoped comparison Loans, which cannot represent a local's lifetime. The implementation therefore uses the Origin-based borrow liveness of the T6e machinery (`IsDisjointProjection`/`ProjectionPath`) for the borrow. It compares element accesses by their existing projection `Path`/`Selector` chain, which is the same selector space as `ElementAccess.PathSelector`. No parallel Loan model was added.

T6k (M2/M3, I6; SPEC §15.6.2 "cannot … grant exclusive authority" through borrowed shared access; R27) DONE: an exclusive reborrow of a stored `uniq` reference reached through a `ref` base (`view.0@uniq` or an implicit `uniq/Counter` argument `bump(view.0)` with `view: ref/(uniq/Counter, bool)`) now fails in Binding (InvalidAssignment / NoApplicableOverload). Previously it bound completely and was rejected only as an ownership Unsupported. Debug/Release each pass 8,961 tests (2 new); all 179 T6i-archived fixtures are byte-identical after the change, and 12 BorrowedTupleProjection fixtures pass 24 O0/O2 runs.

T6m (M2/M3, I4/I6; SPEC §15.6.3) DONE: shorthand borrows of scalar Places (`v@uniq` / `v@ref` for locals and field paths such as `p.tag@uniq`, including the chapter's own `bump(v@uniq)` example) bind, verify and execute. Before this change, Binding reported UnsupportedBinding at `v@uniq` although the implicit `bump(v)` worked. Borrows of scalar temporaries (`1@ref`, `1.25@ref`) stay Unsupported, because generation does not lower them. `uniq` of an immutable local still fails in Binding, and overlapping uses reject. `ConversionEmissionTest`'s `let x = 1\nx@ref` Unsupported case encoded that implementation limit and contradicted §15.6.3, so it became `1@ref`, which keeps the distinct-Unsupported responsibility. Debug/Release each pass 8,968 tests (7 new); 4 fixtures pass 8 O0/O2 runs; all earlier archived fixtures are byte-identical.

T6n (M2/M3, I4/I6; SPEC §10.2 borrowed temporaries, §13.5.5.2) DONE: owner scalar temporaries are materialized once and shared-borrowed for `ref/T` arguments. This covers call results (`read(one())`, `read(one() + 1)`), `bool` results and untyped literals (`read(1)`, `check(1.5)`).
- Before this change, `read(one())` passed Binding and ownership, then failed in generation ("Borrow source has no matching aggregate storage"). `read(1)` failed Binding with NoApplicableOverload.
- The borrow now stores the prepared value into a slot, allocated only for a borrowed scalar temporary, and borrows that slot.
- Literal borrows rank below direct literal fitting: `pick(1)` selects `i64`. The spec's `chooseBorrow(1)` stays ambiguous; `bump(1)` (no implicit `uniq` temporary borrow) and unrepresentable literals reject.
- A soundness gap was closed: `let r = keep(one())` followed by a later use of `r` previously verified in ownership. Materialized scalar temporaries are now Loan roots for their Projection Origins.
- Debug/Release each pass 8,979 tests (11 new); 46 fixtures pass 92 O0/O2 runs.
- Remaining boundaries: none in this family beyond the general I4/I6 scope. Generic literal inference is covered by T6p, and returned-Origin literal calls by T6q. Explicit temporary borrows are covered by T6o.

T6o (M2/M3, I6; SPEC §3.6.1–3.6.2, §13.5.5.2) DONE: explicit shorthand borrows of owned scalar temporaries (`1@ref`, `one()@ref`, `1.25@ref`, and `1@uniq` / `one()@uniq` under the temporary's exclusive capability) bind, verify and execute through T6n materialization. A borrow that outlives the statement (`let r = one()@ref` then `let s = r`) rejects. Implicit `uniq` temporary adaptation (`bump(1)`) is still rejected per §10.2. Two test cases asserting `1@ref` / `1.25@ref` as Unsupported encoded the removed limit and contradicted §3.6.2, so they were dropped. The distinct-Unsupported responsibility remains covered by `x@(ref)`, `x@obj` and `3.9@i128`. Debug/Release each pass 8,982 tests; 15 fixtures pass 30 O0/O2 runs.

T6p (M2/M6, I4/I14; SPEC §10.2 generic borrowed temporaries) DONE: the §10.2 examples `inspect(1)` ("T defaults to i32 after constraints") and `inspect(1.5)` with `inspect<T>(value: ref/T)` now infer and execute. Before this change they failed with NoApplicableOverload, because the defaulted literal had no bound Type when `AdaptInput` checked the temporary form. `inspect(makeValue())` already worked and is covered natively. A generic `uniq/T` parameter still rejects a literal. Debug/Release each pass 8,984 tests (2 new); 16 fixtures pass 32 O0/O2 runs.


T6q (M2/M3, I4/I6; SPEC §10.2, §15.6.4) DONE: returned-Origin calls with a literal argument (`keep(1)` with `func keep(n: ref/i32) -> ref/i32 from n`) bind their input Origin to the literal temporary. Before this change they failed Binding with UnprovenConstraint, because the literal borrow path skipped `InferInput`'s Origin matching. `read(keep(1))` executes within the statement; `let r = keep(1)` followed by a use of `r` rejects with ComparisonLoanConflict. Debug/Release each pass 8,986 tests (2 new); 17 fixtures pass 34 O0/O2 runs.

T6r (M3/I6; regression coverage, no compiler change) DONE: scalar fields borrowed through borrowed bases now have native coverage. This covers `p.tag@uniq`, implicit `bump(p.tag)` and a live `t = p.tag@uniq` beside a sibling write with `p: uniq/P`; `read(p.tag)` through `p: ref/P`; and owned Tuple scalar elements `t.0@uniq`. `p.tag@uniq` through a `ref` base rejects in Binding. These forms were found already working through T6f/T6h/T6i/T6m paths, so only tests were added. Debug/Release each pass 8,990 tests (4 new); 11 fixtures pass 22 O0/O2 runs.
Finding G10 (M6/I15) remains open for general shared-body operation coverage. Its original concrete-call and `Console.writeLine` generation failures are resolved by G10a/c; [original reproduction and execution evidence](PLAN_HISTORY.md#general-compiler-20260919-052044). Completed bounded slices below do not close universal proofs, wider operations or resource contracts.

G10a (M6/I15; SPEC §21.2–21.3, §13.4) DONE for its concrete-call and scalar-operation subset; [implementation evidence](PLAN_HISTORY.md#general-compiler-20260919-045448). Its previously open regression gate now passes: warning-free Debug/Release builds, 9,012 managed tests per configuration and 69 fresh shared-generic fixtures / 138 LLVM/native O0/O2 executions, with identical Debug/Release fixture bytes. [Current execution evidence](PLAN_HISTORY.md#general-compiler-20260919-052044).

G10b (M6/I15; SPEC §12.2, §14.9, §21.3) DONE for shared bool/integer result joins: `and`/`or`, conditional/multiple/one-arrival results, loops and cleanup use validated Phi arrivals and secured results. Warning-free Debug/Release builds each pass 9,026 tests; 127 fresh shared/result/cleanup fixtures pass 254 LLVM/native O0/O2 executions with identical configuration bytes. [Evidence](PLAN_HISTORY.md#general-compiler-20260919-052044).

G10c (M6/I15; SPEC §21.3, §22.5.5) DONE for owned strings in shared bodies: literal/local/parameter/result storage, conditional Move/replacement/cleanup, concrete and generic forwarded string results and canonical `Console.writeLine`. Warning-free Debug/Release builds each pass 9,038 tests; 124 fresh fixtures (including lifetime-instrumented versions of all 12 new cases) pass 248 LLVM/native O0/O2 executions with identical configuration bytes. This closes the original G10 output reproducer; borrowed-string operations and broader generic operations remain open. [Evidence](PLAN_HISTORY.md#general-compiler-20260919-052044).

G10d (M6/I15; SPEC §17.3, §21.3) DONE: shared canonical `$abort`/`require` preserves Never completion, source diagnostics, one-time message evaluation and absence of normal cleanup. Warning-free Debug/Release builds each pass 9,045 managed tests; 12 new normal/audited fixtures pass 24 LLVM/native O0/O2 runs. Configuration bytes match and all 124 G10c fixtures remain byte-identical. [Evidence](PLAN_HISTORY.md#general-compiler-20260919-052044).

G10e (M6/I15; SPEC §3.1.5, §21.3–4) DONE for concrete/shared/forwarded Never calls and selected specializations: no result representation/storage, noreturn body/entry/adapter ABIs and no normal return. Warning-free Debug/Release builds each pass 9,051 tests; 121 fresh shared-generic fixtures pass 242 LLVM/native O0/O2 executions, including bounded divergence, with identical configuration bytes. [Evidence](PLAN_HISTORY.md#general-compiler-20260919-052044).

G10f (M6/I15; SPEC §13.3, §21.3) DONE for shared 8–64-bit integer bitwise/shift operations, including independently typed 8–64-bit counts checked before narrowing, signed/unsigned right shift, compound updates and single evaluation. Warning-free Debug/Release builds each pass 9,062 tests; 11 new fixtures pass 22 LLVM/native O0/O2 runs. Configuration bytes match and all 121 G10e fixtures remain byte-identical. [Evidence](PLAN_HISTORY.md#general-compiler-20260919-052044).

G10g (M6/I15 with M4/I9 prerequisite; SPEC §6.2.3, §7, §21.3) DONE for supported concrete constructors and shared/exclusive/owned receivers, including adjacent reference-handle reborrows. Warning-free Debug/Release builds each pass 9,070 tests; eight new fixtures pass 16 LLVM/native O0/O2 executions with identical configuration bytes. All 132 G10e/f fixtures remain unchanged. Defaults, inherited/general receiver layouts and broader borrow forms remain gated. [Evidence](PLAN_HISTORY.md#general-compiler-20260919-052044).

G10h (M6/I15; SPEC §13.5, §21.3) DONE for shared 8–64-bit integer conversions using the ordinary range plan/writer: widening, narrowing, same-width signedness changes and conditional/Phi paths preserve checked behavior. Eleven new fixtures pass 22 LLVM/native O0/O2 executions. Final corruption review rejects stale checking-only reachability before dominator construction and verifies reanalysis recovery. Final warning-free Debug/Release builds each pass 9,082 tests; all 151 native-tested shared fixtures regenerate byte-identically. [Evidence](PLAN_HISTORY.md#general-compiler-20260919-052044).

Next unit T6l (M3/I6) TODO: stored exclusive-reference reborrows from owned storage (`var pair = (counter@uniq, true)` then `let item = pair.0@uniq`) are Unsupported at `pair.0`, because the element read would Move the non-Copy reference. A shared reborrow through a `ref` view (`view.0@ref` of a stored `uniq`) is also Unsupported. SPEC §15.6.3 requires the child Loan to suspend the containing slot as well as the referent. Establish how Binding's result Origin records both dependencies before lowering a load-then-reborrow; do not accept by removing the guard. Sibling Moves under a live part borrow (T6i boundary) need path-aware root initialization and are a separate item. Scalar reference use (`t += 1` or `n + 1` on a `uniq/i32` or `ref/i32`) does not bind. This is not an implementation gap: SPEC §13.4.3 states safe references are never implicitly dereferenced, and §3.2 adds no safe `*` operator (resolved 2026-09-19). Scalar temporary borrows are covered by T6n (implicit arguments) and T6o (explicit).

P14-L1 (nonblocking, assigned to the relevant I families): the shared match path covers flat Copy enum Cases with binding/wildcard payloads and no guards. General symbolic `Option<T>` payload matching and a shared all-terminal match with a Never result remain unsupported; concrete factory calls work, but makeObj inside a generic body is not lowered. T6a closes concrete borrowed scalar-field updates; shared generic receiver-field updates remain incomplete. These findings do not justify simplifying target programs or weakening the specification. Full object views, rc/arc/Weak and general capture/erasure remain unfinished compiler work; ObjectCallCompatible stages remain explicitly deferred by the specification.

<a id="exact-next-unit-t4n-am--unit-52-todo"></a>
Unit 52's [preserved criteria](PLAN_HISTORY.md#program12-baseline) are closed for its bounded scope by the preceding verification audit. The previous implementation-only deferrals remain historical records.

## Test profile implementation (20260919)

The user adopted the reviewed decisions and authorized implementation. The initial profile now owns solution-wide selection/barriers, one project Test record with CLI overrides, finite execution/recovery/retention limits, identities, the binary channel and JSON/text results. §22.6 supersedes the former top-level-statement rejection: test mode checks startup bodies but does not execute them or require a product entry. Top-level bindings keep their existing local semantics. TMP/TEMP and Kimi.Test.tempDirectory identify one private case directory. Old G2 public-interface blocks are resolved; named profiles remain deferred.

Implementation uses existing Binding/ownership CFGs and concrete/shared lowering, with dedicated Observe/Message/Abort records. The first false result is transmitted before condition cleanup. Scalar bits are captured without reevaluation; messages remain lazy and nested IssueIds remain distinct. The native passing path allocates no failure diagnostics. Discovery checks all cases before selection and uses one immutable all-case artifact per project. Workers use launch-time Windows jobs, private asynchronous result/log streams, EOF stdin, finite deadlines and shared budgets. Temporary path metadata is captured before user code; each API result is independently owned. Reports use atomic publication and bounded completed-run retention.

Corrections found during verification: fixed scratch-lowering message addresses and empty-defer indexing; avoided a 256-byte ordinary warm-binding regression by bypassing test dependency name conversion in product mode; fixed InvalidDataException classification; added shared scalar snapshots, immutable source membership and mode-change invalidation; made named-pipe draining cancellable; charged message headers to budgets; preserved logs after cleanup failures and recorded unstarted reasons. Failed exploratory fixture syntax and superseded runs are not final evidence.

Verification:

- Warning-free managed Debug and Release builds with EmitCompilerGeneratedFiles=false. Full direct xUnit runs each pass **9,144 tests**, zero failures/skips/not-run: bin/test-full-debug.log and bin/test-full-release.log. These include source membership, all-body checking, ownership/message transfers, malformed/stale protocol, zero budgets, mode invalidation, EOF stdin, zero exit without completion, cancellation, timeout and managed-descendant recovery. NativeAOT was NOT_RUN.
- Native **O0 and O2** profile suite: bin/test-profile-final-verified/verification.json and per-project run/list/zero reports. Each full run has one expected pass and nine deliberate failures; the verifier passes. It checks scalar/float/shared snapshots, nested/lazy messages, cleanup phase, require-Abort without enclosing defer, message Abort, timeout, bounded logs, temporary removal and unchanged identities across filters/budgets. Additional solution runs execute both projects and reject an invalid unselected test before any launch.
- After the full regression runs, the text renderer gained failed-log/result paths. Both configurations rebuilt without warnings; the native O0 text smoke passed with the expected exit 1 and scalar values/log locations (bin/test-text-smoke.log). No semantic or backend change followed the full profile suite.
- Backend import definitions add GetEnvironmentVariableA and SetHandleInformation, with the normalized definition hash updated. The adopted backend archive and LLVM version remain unchanged.

I30/I31 are not whole-language completion claims. External-module emission (I28), dynamic static lifetime (I11), and full independent generic resource plans (I16) remain compiler prerequisites; unsupported emission fails before case launch. Current state and exact next actions remain only in PLAN.md; STATUS.md records the supported boundary. No draft file was changed.


<a id="pre-program16-checkpoint"></a>

## Superseded execution state before program 16 (2026-09-19)

Current execution: **Adopted test profile checkpoint implemented and verified for the supported Windows backend**. G2 is resolved by [the normative profile](spec/testing-profile.md). Discovery, verification operations, isolated project/solution execution and reporting are implemented; [evidence](PLAN_HISTORY.md#test-profile-implementation-20260919) separates managed tests and native runs. I30/I31 remain IN_PROGRESS for full cross-feature conformance with existing I11/I16/I28 prerequisites. NativeAOT and draft edits remain excluded.

| ID | State | Acceptance / exact next action |
| --- | --- | --- |
| KL | DONE | Embedded sources, stable catalog, ordinary helper binding/collection and explicit validation are complete; warm rebind allocates zero bytes. [Verification and measurements](PLAN_HISTORY.md#kimi-library-organization). No KL actions remain; missing library declarations/APIs stay in I5/G4 and I19–I25. |
| DOC-S | DONE | §2.3.1–6 owns the rules; Attribute, unsafe and directive chapters link to it. Appendix A.21 owns verification. Adopted draft unchanged. |
| DOC-LP | DONE | Optional range collection, all declaration targets, interpolation, fragments and directive exclusions; source mappings and generated provenance survive reparse. |
| DOC-M | DONE | Lazy text, pinned CommonMark profile, escaped rendering, relative links, item extraction, separate diagnostics and Binding-backed publication. |
| DOC-V | DONE | Warning-free Debug/Release builds; 8,792 managed tests per configuration, including 84 documentation cases; identical collected/uncollected IR and two verified native O0/O2 executions. |

The preceding P18-D/P19-D/P20-D/P21-D/P38-R/P38-S/P38-V checkpoint is preserved in
[history](PLAN_HISTORY.md#kimi-intrinsics-placement). Programs 16–21 remain
specification targets; 22–38 have no source files. P14 and units 62–67 retain their
bounded prior completion; no product M/I family is closed by this program checkpoint.

KI-S/KI-I/KI-V are complete; their evidence is retained in the linked history. Remaining ownership APIs retain their existing I24/I25 implementation scope.

P15-B/O/G/V remain complete; their scope and evidence are in [history](PLAN_HISTORY.md#program15-completion). General unfinished M/I items remain planned separately from KL. Milestone Program implementation and unspecified documentation CLI/LSP/Mod interfaces remain excluded.


<a id="program16-completion"></a>

## Program Milestone 16: external Origin forwarding (2026-09-19)

Scope: unchanged `milestones/Milestone16.kimi`, independently of product M/I IDs.
Starting HEAD was `ad1dcd63e68b740b5a174d8cd7885d385807798d`; the worktree was
clean, so there were no pre-existing edits to back up. Root AGENTS.md, the owning
Origin/match/storage specifications and adjacent milestone programs were read.
Programs 17–21 informed boundaries only. No draft or specification edits were needed.

The initial current-source Debug build had zero warnings/errors, and 180 focused
borrow/enum/match/join regression tests passed. Reproduction was
`dotnet Kimi/bin/Debug/net10.0/Kimi.dll build milestones/Milestone16.kimi`.
Binding passed, but Origin-bearing Selection storage and match acquisition were
rejected by ownership. Later failures were recorded only as they became observable:

1. Admit enum storage with the declaration's Origin arity and already-substituted
   payload Types, retaining the existing finite-storage and ownership checks.
2. Control flow then remained pending on the enum payload's `source` Type annotation.
   Enum Case declarations now follow declaration flow; Binding still validates their
   payload Types and Origin names, including unknown-Origin rejection.
3. Ownership passed; checked emission rejected the Origin-bearing enum layout.
   Physical layout now uses prepared Case storage without runtime Origin data.
4. Emission next rejected a reference PayloadPlacement whose source Origin could
   validly shorten to the result intersection. Transfers use the existing directed
   reference fitting proof; unrelated aggregate Types still require identity.
5. Emission then rejected the call result's intersection substitution. Call-Origin
   checking reuses Binding's canonical substitution/Meet, including reordered and
   duplicate operands, while preserving complete Type checks.

The unchanged target then emitted checked LLVM. Sandboxed `opt.exe` initially
failed with permission denied; approved local-tool execution enabled the ordinary
LLVM verifier, linker and native runner. This was an environment restriction,
not evidence of native completion until those tools and programs actually passed.
Intermediate repro/build/test logs are in `bin/milestone16-work/` (ignored artifacts).
Verification (SDK 10.0.401, runtime 10.0.12, Windows x64 LLVM 22.1.8/profile ABI 2):

| Check | Result / evidence |
| --- | --- |
| Debug/Release solution builds | PASS, zero warnings/errors; `bin/milestone16-work/final-{debug,release}-build.log` |
| Managed Debug | Full 9,151-test run PASS; then all 13 external-Origin cases PASS, including six subsequently added cases. The initial compilation overlapped those test edits; XML inspection detected the stale test assembly, which was rebuilt before `extended-debug-tests.xml`. No compiler changes occurred after the full run. |
| Managed Release | Full 9,157 tests PASS, zero errors/failures/skips; `final-release-tests.xml` |
| Debug native target/variants/rejections | 57 PASS; [record](bin/milestone16/Debug/6547c467b70d46a788db9b3d1ee305e7/verification.json) |
| Release native target/variants/rejections | 57 PASS; [record](bin/milestone16/Release/12179b710e884af2aa6b2af8f46c9fe3/verification.json) |
| Completed programs 1–15 | 90 Release O0/O2 execution checks PASS; byte-identical source copies, expected output from milestone documentation; [record](bin/milestone16-work/native-regressions/verification.json) |

Each target harness covers the unchanged source through its normal O2 build and
three execution routes (direct executable, `run` executable, `run` input). Seven
variants at both O0/O2 cover byte-identical renamed input, renamed declarations,
changed values, reversed choice, identical source Origins, Missing Case selection
and an additional enum local. Six invalid variants at both levels reject before IR
publication: Static/incomplete result contracts, writes conflicting with either
retained source, parent access during a child reborrow, and a local source escape.
Managed tests additionally cover reordered/duplicate Origin intersections,
unknown payload Origins and both source conflicts while the enum remains live.

Successful executions have exact five-line stdout (including final LF), empty
stderr and exit 0. Both selection branches observe the external Cells after Pair
and match-Subject cleanup; the later mutations are checked as 13 and 24. No file
side effects are specified. Every native build uses ordinary input-IR verification,
O2 verification where applicable, object generation and linking; all tool versions
match the pinned profile. Reports retain compiler/source hashes, and those hashes
still match the final compiler binaries and unchanged target source.

Reproduce current-source verification:

```powershell
dotnet build Kimigayo.slnx -c Debug --no-restore --disable-build-servers -m:1 -p:EmitCompilerGeneratedFiles=false
dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -parallelMode none -failSkips
dotnet build Kimigayo.slnx -c Release --no-restore --disable-build-servers -m:1 -p:EmitCompilerGeneratedFiles=false
dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -parallelMode none -failSkips
& backend/windows-x64/test-milestone16.ps1 -Configuration Debug
& backend/windows-x64/test-milestone16.ps1 -Configuration Release
```

Boundaries: this completes program 16, not general aggregate covariance, arbitrary
Origin-polymorphic calls, borrowed enum Subject decomposition, or all mixed
user-destructor payloads. Existing finite layout/ownership and ABI restrictions
remain; these are implementation limits rather than specification exceptions.
NativeAOT was NOT_RUN as requested. No required Windows-native check remains
unverified. Programs 17+ were not implemented; current next actions remain in PLAN.

<a id="compiler-continuation-20260919-132943"></a>
## Compiler continuation — 2026-09-19 13:29:43 UTC

Start was recorded before inspection (22:29:43 +0900). Baseline: `dev` at `6f44d45`;
the commits after `89552bc` changed only draft files, so the previous checkpoint's code
evidence still described the compiler. A user draft file changed during execution was
not edited. No Milestone Program was an implementation target; NativeAOT excluded.
Evidence root: `TestResults/continuation-20260919-222943/`. Baseline warning-free
Debug test-project build before edits.

### T28c / I28 — native requirement configuration (§20.8.2.1, §20.8.2.4)

DONE. `ProjectFile.NativeRequirements` (per target, logical name to Kind/ContractId/
Sha256) is new; `NativeLibraryInput` gains optional ContractId/Sha256 and no longer
defaults Kind to `import`, because Kind is required after expansion. The shared
`NativeConfiguration.Validate` runs at project load/Prepare and before emission:
requirement Kind must be static/import, ContractId nonempty and NUL-free, Sha256 64
hex digits; self-targeted supplies merge with the requirement of the same name, and
overlapping Kind/ContractId/Sha256 must agree. A `kimi_backend` override takes its Kind
from the expansion, and a Sha256 assertion must equal the adopted backend hash.
`NativeBindings` is diagnosed with migration guidance. One existing emission test
relied on the implicit Kind; it now states `Kind = "import"` and a new case keeps
the missing-Kind rejection (test correction justified by §20.8.2.1).

- `native-config-debug.xml`: 114 focused PASS (Dependency/EmissionArtifacts/Minimal).
  Before the change, the new negatives were accepted (unknown key ignored, implicit
  Kind), so they fail without this implementation.
- Full Debug `t28c-full-debug.xml`: 9,237 PASS. Warning-free Release solution build;
  `native-config-release.xml`: 135 focused PASS (adds SolutionInputTest).

Not implemented: `Name`/`Input` array records, `Package`-targeted supplies,
manifest input records, actual supply hash/kind and connection validation.

### T26a / I26 — LibraryImport arguments and requirement ownership (§22.3.1)

DONE. Binding validates every selected `#LibraryImport` on a function: exactly two
unlabeled, nonempty, non-interpolated, NUL-free string literals on a bodyless function
(`InvalidLibraryImport_Kd`). A nonreserved name must be a NativeRequirements entry or
a self-targeted NativeLibraries record of the *defining* module for the prepared
target (`MissingNativeRequirement_Kd`); `Compilation.Configuration(module)` exposes
each prepared module's configuration. Names compare by Ordinal. Imports still cannot
complete Binding; no lowering is claimed.

### T26b / I26 — import declaration shape and reserved names (§22.3.1, §21.5.2)

DONE. Imports must be `unsafe`, directly in a group or a receiverless struct type
function, without generic/Origin parameters, specializations, constructors/destructors,
requirements, local functions or default/optional arguments. `__kimi_`/`llvm.`
prefixes and `_fltused`, `__chkstk`, `memcmp`, `memcpy`, `memmove`, `memset` are
rejected as external names.

- `import-shape-debug.xml`: 33 focused PASS (argument forms incl. the `\0` escape
  and `\(...)` interpolation, reserved kernel32, case-sensitive names, combined
  supplies, root-versus-dependency module requirements, placement/shape cases). The
  first interpolation case used an invalid spelling and was corrected to `\(...)`.
- Warning-free Release solution and Debug test-project builds; full `final2-release.xml`
  and `final2-debug.xml`: 9,262 PASS each, no skips. An earlier concurrent run was
  interrupted by the user and is not evidence; both suites were rerun afterwards.
- No native execution was needed: no generated IR or runtime behavior changed.
- The user committed the work in progress as `08f2998`. Afterwards, a scan found raw NUL
  characters written by an editing script into two C# char literals (`Binding.Attributes.cs`,
  `NativeConfiguration.cs`; git showed them as binary) and into this record. They were
  replaced by the `'\0'` escape (identical value); rebuild warning-free and 111 focused
  Debug tests PASS in `nul-fix-debug.xml`.

### T26c / I26 — initial Windows C ABI signature Types (§22.3.2)

DONE. After headers bind, each import parameter/result must be i8–i64, u8–u64, f32,
f64 or `unsafe/T` (one component), and Unit is accepted only as the result
(`UnsupportedImportSignature_Kd`). bool, char, string, isize/usize, i128/u128,
tuples and Unit parameters are rejected; Types that failed to bind keep their own
diagnostics without a cascade. No physical-signature record or lowering is claimed.

- `import-abi-debug.xml`: 47 focused PASS (14 new: every table entry, eleven arguments,
  pointer result, and each excluded Type). Warning-free Release solution build;
  full `final3-release.xml` / `final3-debug.xml`: 9,276 PASS each.

### T26d / I26 — one physical signature per external symbol (§21.5.2)

DONE for the source-visible part. The T26c check now also yields a compact physical
code per result/parameter (`1 2 4 8 f d p v`), built in a stack buffer; one lazily
created Ordinal map per Binding pass compares all imports of an external symbol across
source modules (`ConflictingImportSignature_Kd`). Signedness and pointee Types do not
affect the physical Type; width, float kind, arity and Unit/value results do. Types
that failed to bind skip this comparison. Provider/`dllimport` agreement needs actual
supply resolution and remains open, as do runtime/generated-definition collisions.

- `import-conflict-debug.xml`: 54 focused PASS (six pair cases and a root/dependency
  module conflict). Warning-free Release solution build; full `final4-release.xml`
  and `final4-debug.xml`: 9,283 PASS each. Source scan: no NUL bytes in changed files.

### T34b / I34 — native requirement usage, and final checks

DONE. README gains a "Native requirements" section: per-target NativeRequirements,
self-targeted NativeLibraries expansion/agreement, reserved kernel32/kimi_backend,
the NativeBindings rejection and the current import limits (no generation/linking,
no Package/Name supply records). Both README snippets are loaded verbatim by
DependencyConfigurationTest, and one asserts the parsed Kind/ContractId. A rebind
assertion confirms import diagnostics are recomputed (the indexer resets each node).

- Runtime-declaration collision checking (user imports of kernel32 runtime APIs) was
  not started: the runtime's physical signatures exist only in `WindowsRuntime.ll.in`,
  so a shared signature table is needed first; it matters once imports are lowered.
- Final: warning-free Release solution and Debug test-project builds; focused
  `final-focused-debug.xml` 380 PASS, `readme-config-debug.xml` 56 PASS; full
  `final5-release.xml` and `final5-debug.xml`: 9,286 PASS each, no skips. No native
  execution: no IR, runtime or artifact behavior changed in this execution.

### T28e / I28 — repeated native entries (§20.8.2.1)

DONE. The raw-map pre-pass already rejected duplicate CompileTimeSettings and
dependency names; NativeRequirements/NativeLibraries targets and entries were still
overwritten silently by the dictionary formatter. `ValidateNativeMap` now rejects a
repeated (target, name) pair within one setting, including across repeated targets or
repeated top-level keys; the same name in different targets, or in both settings
(combined declaration), remains valid.

- Four new negatives and one positive: `native-duplicates-debug.xml` 61 PASS. The
  negatives were accepted before the change (last entry won).
- Warning-free Release solution / Debug test-project builds (a transient SA1119
  parenthesis warning was removed); full `final6-release.xml` and `final6-debug.xml`:
  9,291 PASS each. Changed files contain no NUL bytes.
- T28d (array/Package records) was assessed but not started: Tinyhand 0.147 exposes
  `ITinyhandSerializable<T>` for a custom map-or-array reader; the exact plan is in
  PLAN.md §2.

### T26e / I26 — imports in generic containers (§22.3.1)

DONE. An import whose enclosing struct/group chain has generic or Origin parameters
would itself be parameterized, so it is rejected as `InvalidLibraryImport_Kd`;
a group nested in a non-generic struct stays valid. Three new placement cases;
the generic ones were accepted before the change. `import-generic-debug.xml`:
57 PASS. Warning-free Release solution / Debug test-project builds; full
`final7-release.xml` and `final7-debug.xml`: 9,294 PASS each; no NUL bytes.

Stopped at about 50 minutes (23:19 +0900). The next units (T28d array/Package supply
records; I26 lowering) each need more than the remaining time to reach verification,
so neither was started. The worktree has uncommitted changes after the user's
`08f2998` commit; the user draft file was not edited.


<a id="compiler-continuation-20260919-113419"></a>
## Compiler continuation — 2026-09-19 11:34:19 UTC

Start was recorded before inspection. Fresh baseline: clean `dev` at `89552bc`.
The 60-minute request superseded the P16 stop/await-target restriction; no Milestone
Program was an implementation target. NativeAOT and draft edits remained excluded.

### T28a / I28 — checked source-module common generation

DONE for this bounded slice. R22/R24/R27: §§18.1/18.8, 21.3.4, 21.4 and
22.2.1–2. Existing Binding/flow/ownership already analyze all source modules.
Emission now validates every module's source diagnostics and supported containers,
uses one declaration-to-ABI map and omits nonselected generated wrappers. There is
no lazy omission of invalid/unsupported dependency bodies. Source-local functions
remain local under §6.1.1; dependency APIs in tests use named containers.

Evidence root: `TestResults/continuation-20260919-113419/`.

- Initial new-suite baseline: 13 tests, 10 FAIL / 3 PASS. Four failures reached the
  old external-module emitter guard; other sources required named Containers or
  the optional-parameter `?` marker. Corrected test syntax preserved scope.
- Warning-free Debug test-project and Release solution builds, with `--no-restore
  --disable-build-servers -m:1 -p:EmitCompilerGeneratedFiles=false`.
- Direct xUnit runner (`dotnet xUnitTest/bin/<configuration>/net10.0/xUnitTest.dll
  -parallelMode none`): 332 focused PASS per configuration in `module-debug.xml`
  and `module-release.xml`. Includes 0-byte measured warm scalar-module IR writes.
- Debug full suite: 9,171 total, 9,169 PASS / two obsolete expected-rejection
  assertions FAIL (`full-debug.xml`). The assertions in SolutionInputTest were
  updated for authorized module emission, retaining lock/live-source checks and
  adding IR/no-native-record checks. All 21 SolutionInputTest cases then PASS in
  `module-solution-debug.xml`; final broad verification is recorded separately.
- Eight module fixtures: LLVM verification, object symbol checking, linking and
  16 exact stdout/stderr/exit O0/O2 executions PASS per compiler configuration.
  Release input hashes/log: `native-records/20260919T1150573632812Z/result.json`.
- `backend/windows-x64/test-modules.ps1`: 13 CLI checks PASS per configuration,
  including shared aliases/transitive source dependencies, native O0/O2 output,
  root test discovery/execution without dependency tests, and failed dependency
  build invalidation. Debug record: `module-cli-debug/ca5f0479e7344e90ad7e34bf12488cf9/result.json`;
  Release: `module-cli-release/bc58b842b17044c1a0681e53abdd2336/result.json`.
- Native verification exposed a pre-existing stale PowerShell kernel32 expected
  list. Added GetEnvironmentVariableA/SetHandleInformation already present in the
  hashed definition and managed runtime catalog; no import/profile was changed.
- Sandbox blocked NuGet configuration reading and LLVM execution. Existing restored
  inputs built without restore; authorized native commands succeeded outside the
  sandbox. The first MTP/VSTest-style test attempt was cancelled and replaced by
  the repository's direct xUnit runner; it is not test completion evidence.

Broader Library output, module input identities and native requirement/supply
records remained unfinished at this boundary. Schema 3 was not asserted to prove
full multi-module connection validity. No specification requirement was narrowed.

### T28b / I28 — Library inspection output

DONE for supported bodies under §§20.8.3, 21.5 and 22.2.2. Checked Library startup
has no selected runtime body. Lower all supported verified functions with internal
linkage, finish without an OS entry, and publish the matched inspection IR/manifest
with `outputKind: Library`, null `entry` and null `subsystem`. Test artifacts use
the checked test startup's Application output kind even for a Library project.
Native Application build/run remain explicit rejection boundaries.

- Warning-free Debug test-project / Release solution builds using the commands
  above. `library-debug.xml`: 139 PASS; `library-release.xml`: 160 PASS (adds all
  SolutionInputTest cases). Debug full `library-full-debug.xml`: 9,185 PASS, no skips.
- Six Library fixtures (empty, ordinary/main/Abort/owned-string/generic/destructor
  bodies), 12 O0/O2 LLVM verification/COFF checks PASS per configuration. No OS
  entry and the required `_fltused` definition were checked in actual objects.
- CLI `emit` succeeds with an absent configured LLVM directory; manifest hash and
  Library fields match. `build` and `run` reject, without publishing an executable.
  Records: `library-native-debug/04b2f66537ef4836abb281d77d2a2340/result.json` and
  `library-native-release/1f8121ef52cf404a9aa325cdf2dd6ef0/result.json`.
- Existing `test-kernel32.ps1` PASS, including import identity, path independence,
  failed-generation preservation, tool policy and manifest validation.

No source package, external Library ABI, native supply or persistent input-record
completion is claimed. At about 25 minutes the continuation selected independent
T6s (the existing I6 sibling-Move backlog) while the larger I28/I11 work remained
open. Library output requirements were already normative; SPEC needed no change.

### T6s / I6 — disjoint sibling Moves under inline-part Loans

DONE for statically selected owned fields/Tuple parts (R6/R7; §§15.1.3,
15.6.2–3/15.6.6). Initialization follows the borrowed subtree and its containing
storage instead of requiring every sibling initialized. Sparse Move-path state
is reused with stack selectors. Pure intermediate projections locate storage;
final reads/Moves retain their actual conflict footprint. Emission rechecks the
same initialized subtree and rejects stale/tampered borrow plans.

- Baseline: `sibling-baseline.xml`, 9 cases, five required positive cases FAIL;
  four overlapping/moved-storage negatives PASS. Initial fix: 70/71 PASS;
  the remaining Move-before-borrow emitter gate was corrected.
- Final focused `sibling-debug.xml`: 80 PASS. Warning-free Debug test-project and
  Release solution builds. Full `sibling-full-debug.xml` and
  `sibling-full-release.xml`: 9,203 PASS each, no skips.
- Ten new fixtures, 20 LLVM/native O0/O2 executions PASS per configuration in
  `sibling-native-debug` / `sibling-native-release`. Shared/exclusive, tuple/nested,
  Move-first, reborrow, conditional Move, checking-only continuation, repaired
  storage and exactly-once remaining/moved-value destruction are covered.
- Seven negative cases retain moved-subtree, whole-owner and overlap rejection;
  mutation after analysis cannot publish partial IR. Uncertain provenance,
  dynamic indices and broader Origin contracts remain conservative.

At the next boundary (~38 minutes), T6t starts on the remaining single-input
returned-reference footprint limitation; parent I6 remains IN_PROGRESS.

### T6t / I6 — single-input returned-reference footprints

The declared single input Origin carries the entire acquired argument footprint
through a direct reference-returning call (§15.6.4). Result-side field offsets
are discarded at calls: the public contract does not identify the returned
subpart. Unique alias definitions preserve call-result temporary provenance;
mutable source locals and unknown/intersected result contracts stay conservative.

- Baseline `returned-part-baseline.xml`: six positives FAIL, four negatives PASS.
  The first fix left one Move-before-call initialization failure, traced to a
  call-result temporary marked mutable internally; a single defining Alias now
  supplies its provenance. Source reassignment still cannot gain this proof.
- `returned-part-debug.xml`: 67 focused PASS, including the added warm 0-byte
  ownership-reanalysis and rebind/invalidation case. Seven fixtures produce 14
  exact native O0/O2 PASS in `returned-part-native-debug`.
- The related Release-compiler native borrow/partial-move regression at the T6s
  boundary passed 196 O0/O2 executions in `borrow-native-regression`.
- Release verification and subsequent snapshot-replay regression evidence follow.

T6t DONE after warning-free Release solution build, 67 focused Release PASS in
`returned-part-release.xml` and 14 native O0/O2 PASS in
`returned-part-native-release`. A temporary StyleCop parenthesis warning was
removed by sharing the single-definition predicate. Final broad checks follow.

### T6v / I33 — one snapshot per borrow-check operation

Started at approximately 49 minutes. Borrow validity previously replayed the same
block prefix for each live Place and again for each referent root. All subsequent
footprint/conflict queries are read-only, so one converged runtime/checking input
can serve the whole operation. Scaled live returned-part Loans exercise independent
part initialization, final values, checking continuations and warm allocation.

- `snapshot-baseline-release.xml`: three scaled scenarios PASS. Five warm analyses
  took 84.498 ms (8 runtime Loans), 120.669 ms (16), and 64.648 ms (8 checking Loans).
- `snapshot-final-release.xml`: all 35 focused cases PASS after reuse; corresponding
  samples were 75.522, 96.939 and 23.459 ms. Both versions measured 0 warm allocated
  bytes in all three scenarios. These short host samples show the measured
  workloads only; they are not a general throughput guarantee.
- Final Debug/Release solution builds both pass with zero warnings/errors.

T6v DONE. Final full suites `final-debug.xml` and `final-release.xml` each pass
9,220 tests, zero failed/skipped. Final compiler-generated fixture snapshots and
SHA-256 lists are `final-borrow-fixtures-{debug,release}` and
`final-borrow-{debug,release}-inputs.json`. Each set passes 252 LLVM/native O0/O2
executions in `final-borrow-native-{debug,release}`, covering existing owned,
nested/disjoint/returned borrows, partial Moves, new sibling/returned-part/snapshot
cases and source-module calls. This supersedes earlier checks for changed code.

Final module CLI harnesses pass 13 checks each:
`final-module-cli-debug/bddd9908d4e14027a3aae259b13d296e/result.json` and
`final-module-cli-release/a4e0932f44244d8d845f1d28f783055c/result.json`.
Final Release Library inspection: 12 O0/O2 LLVM/object checks plus emit/build/run
boundaries PASS in `final-library-release/c9e20ec6d2be437e87ad561736fb28bc/result.json`.

### T34a / I34 — source-dependency usage and executable example

DONE. Removed obsolete module/Library generation exclusions from the root README
and dependency example. Added an Application that calls Geometry, which calls Math;
it verifies the arithmetic result and prints `source modules`. Library inspection
and native Application commands describe their actual purposes and residual limits.
All 12 documented commands (restore, locked Library check/emit and locked Application
build/run, Debug/Release) PASS in isolated copies; `source-example/result.json` and
per-configuration logs retain evidence. Original example locks were not rewritten.

### Completion boundary

The 60-minute duration elapsed while closing the final coherent documentation unit;
no subsequent unit was started. No draft file or Milestone Program was changed,
and NativeAOT was NOT_RUN. SPEC requirements already defined all implemented
behavior and were not weakened. Parent I6/I28/I33/I34 remain IN_PROGRESS; current
next actions and unresolved implementation work are centralized in PLAN.md.

Final checkpoint time: **2026-09-19 12:35:19 UTC**; elapsed **61 minutes** from the recorded start. Stopping reason: requested duration elapsed, with the active unit's required verification and documentation complete. Final git diff --check passed.

<a id="documentation-markdown-parser-20260920"></a>

## 2026-09-20 — Independent documentation Markdown parser (DM2)

Baseline `e01ad63`, initially clean. The user adopted the finalized 2026-09-19
Documentation Markdown design and requested the independent parser while retaining
Markdig. The finalized draft was read, not changed. Its lowercase plain-or-code
items, parameter-first classification, Unicode 15.0.0, explicit continuation and
precise half-open source ranges supersede the earlier draft discussed in the task.

Added the independent syntax/candidate API without redirecting compilation or the
existing Markdig-backed documentation API. Nodes use an immutable flat arena;
ordinary leaf text borrows source slices, decoded scalars occupy a node word, and
only transformed multi-character values/URLs allocate strings. Parser scratch is
pooled and returned on cancellation/failure. Removed delimiter nodes are compacted
before publication. Blocks, emphasis, traversal and depth checks are iterative;
code-run and balanced-parenthesis indexes avoid repeated suffix searches. Common
one/two-backtick runs need no dictionary and flat link destinations need no full
parenthesis index. Heading description boundaries are resolved in reverse order
with six level slots. Candidate publication is atomic; declaration classifications
use ordinal name counts and explicit receiver roles, without spelling heuristics.

The Unicode table contains 338 merged punctuation/symbol ranges generated from
UnicodeData.txt 15.0.0, SHA-256
`806E9AED65037197F1EC85E12BE6E8CD870FC5608B4DE0FFFD990F689F376A73`.
The checked-in generator verifies that identity. Whitespace is the profile's fixed
Zs/TAB/LF/FF/CR set, not host `char.IsWhiteSpace`.

Intermediate checks corrected span-trimming overloads, list blank propagation
through nested items, code-line source endpoints, escaped delimiters inside link
destinations, empty versus absent link titles, and autolinks inside link labels.
An exploratory run against all 652 unfiltered CommonMark examples was used for
inspection only: that corpus includes intentionally removed features and output
policy differences. It is not a limited-profile conformance result. Regressions
for the relevant boundaries were added to the checked-in suite. The initial suite
also exercises 1,500 reproducible mixed/incomplete strings with tree/range checks,
large unmatched delimiters, a 3,000-level explicit quote input with a raised limit,
fixed Unicode, source line endings, concurrent candidate identity and cancellation.

Final verification: Debug and Release `dotnet build Kimigayo.slnx --no-restore
-c <configuration> -v:q` each passed with zero warnings/errors. Running
`dotnet xUnitTest/bin/<configuration>/net10.0/xUnitTest.dll -class '*Documentation*'
-result-xml <output>` passed **155/155 per configuration**, with zero failures,
errors, skips or unrun cases: 71 new cases plus 84 existing documentation cases.
The entire managed suite was not rerun for this additive API.
[Debug build](bin/documentation-markdown/build-debug.log),
[Release build](bin/documentation-markdown/build-release.log),
[Debug test results](bin/documentation-markdown/documentation-debug.xml) and
[Release test results](bin/documentation-markdown/documentation-release.xml)
are local generated evidence, not tracked repository fixtures.

Product HTML/structured URL processing, publication adapters, full comparison
coverage and benchmarks remain later migration stages; no performance ratio or
complete product-profile support is claimed. No NativeAOT or native execution was run.

### Superseded planning checkpoint
Current checkpoint: **60-minute compiler continuation stopped at about 50 minutes (23:19 +0900)**, started **2026-09-19 22:29:43 +0900 (13:29:43 UTC)** before inspection; the duration boundary is **23:29:43 +0900**. Fresh baseline: `dev` at `6f44d45`; code unchanged since the previous checkpoint (only user draft commits), and one user draft file changed during this execution was left untouched. Bounded units **T28c/T28e / I28** (native requirement configuration and duplicate-entry rejection) and **T26a–T26e / I26** (`#LibraryImport` requirement matching, declaration shape including enclosing generic/Origin containers, §22.3.2 signature Types and same-symbol physical signatures) are **DONE**, as is **T34b / I34** (README native-requirement usage, verified by tests); Debug/Release full suites each pass 9,294 tests. The remaining ~10 minutes could not complete a verified T28d or lowering unit, so none was started; see the next actions. [Evidence](PLAN_HISTORY.md#compiler-continuation-20260919-132943). The previous 11:34 UTC continuation (T28a/T28b, T6s/T6t, T6v, T34a) remains recorded in [its history](PLAN_HISTORY.md#compiler-continuation-20260919-113419). Parent implementation items remain open. Previous Program 16 completion/evidence is preserved below.

<a id="documentation-markdown-tests-20260920"></a>

## 2026-09-20 — Documentation Markdown conformance and integration (DM3)

Baseline `2b97404`, initially clean. The user requested step 3: conformance,
differential and Kimigayo integration tests. The finalized design was read without
modification. No product entry point or Markdig dependency was switched.

Added an offline, attributed CommonMark 0.31.2 corpus and a fixed manifest:
372 common examples each test the independent parser against upstream expected
HTML and compare Markdig 1.3.2 against the same oracle. All 280 exclusions have
explicit profile reasons; all 652 inputs receive syntax/range invariant checks.
The upstream JSON identity is pinned by SHA-256. The structural fixture renderer
uses the upstream URL encoding convention. Only the optional newline between
`<li>` and `<p>` is normalized for Markdig comparison; text/code whitespace and
other structure are retained. [Selection, license, matrix and commands](xUnitTest/TestData/DocumentationMarkdown/README.md)
are checked in with the fixtures. Neither exclusions nor the fixture renderer
claim full product HTML conformance.

Additional cases cover every adopted §2.2 row, 568 generated delimiter/link
interactions, 3/4-column container starts, partial tabs, exact character-reference
and multiline ranges across LF/CR/CRLF, UTF-16 EOF/insertions, Unicode 15 under
three cultures, non-NFC and case-sensitive names, stable borrowed scalars after
compacting GC, concurrent candidate publication and cancelled requests, active
large-input cancellation/retry and depth interruption. The original deterministic
mixed-input and deep/unmatched-delimiter cases remain in the suite.

Kimigayo integration consumes real external, generic, Semantics, length and Origin
names through a test-only adapter. Receiver exclusion uses Binding's ReceiverIndex;
ordinary group/top-level `self` remains a parameter. Tests cover namespace
ambiguity, generated source provenance, independent declaration fragments,
configuration reload, changed declaration inputs, effective access and explicit
specialization. All 17 existing declaration-target cases now parse independent
syntax. Collection on/off compilation requests independent Markdown before
ownership/lowering and still emits identical IR. Parser interruption leaves
language diagnostics and Binding validity unchanged.

### Findings and corrections

- The first 372 independent official cases passed. Sixteen initial Markdig
  mismatches were solely its omitted `<li>`/`<p>` formatting newline; the narrowly
  specified normalization now accounts for those without changing expectations.
- Generated comparisons exposed an independent parser defect: `[x]( "title")`
  was incorrectly treated as an empty destination plus title. Destination scanning
  has precedence, so its destination is `"title"`; `[x](<> "title")` explicitly
  expresses an empty destination with a title. Removed the special case and
  corrected/expanded regression expectations. The adopted specification is unchanged.
- Markdig omits empty HTML title attributes and produces nested anchors for an
  autolink wrapped in an inline link (including an empty outer destination).
  Explicit tests record these differences. Independent syntax retains absent versus
  empty titles and follows the adopted prohibition on nested links. These cases
  are not normalized away or treated as reasons to weaken the profile.

### Verification

Environment: Windows x64, .NET 10.0.12 runtime, pinned Markdig 1.3.2 and repository
LLVM 22.1.8 toolchain. Both `dotnet build Kimigayo.slnx --no-restore -c
<configuration> -v:q` runs passed with zero warnings/errors. Direct xUnit runs use
`-parallelMode none -failSkips -result-xml <output>`; focused runs additionally use
`-class '*Documentation*'`.

| Verification | Debug | Release |
| --- | --- | --- |
| Documentation suite | 947 PASS | 947 PASS |
| Full managed suite | 10,157 PASS | 10,157 PASS |
| DocumentationComments fixture, LLVM verification/link/execution | 2 PASS (O0/O2) | 2 PASS (O0/O2) |

All managed runs have zero failures, errors, skips or unrun cases. Net new test
cases: 792 relative to the 155-case DM2 documentation suite. The generated
interaction matrix executes 568 comparisons within one Fact, not 568 separately
counted xUnit cases. Full suites include the focused cases; counts are not additive.
Native runs verify expected stdout/stderr and exit status using the existing
`backend/windows-x64/test-scalars.ps1` command with
`-FixturePattern DocumentationComments.ll`. The initial sandbox denied LLVM
execution; the authorized escalation completed both native checks.

Local generated evidence (not tracked fixtures):
[Debug build](bin/documentation-markdown/dm3-build-debug.log),
[Release build](bin/documentation-markdown/dm3-build-release.log),
[Debug documentation XML](bin/documentation-markdown/dm3-documentation-debug.xml),
[Release documentation XML](bin/documentation-markdown/dm3-documentation-release.xml),
[Debug full XML](bin/documentation-markdown/dm3-full-debug.xml),
[Release full XML](bin/documentation-markdown/dm3-full-release.xml),
[Debug native log](bin/documentation-markdown/dm3-native-debug.log), and
[Release native log](bin/documentation-markdown/dm3-native-release.log).

No benchmark or NativeAOT run was performed. Culture checks are not cross-OS or
cross-runtime verification. Independent product rendering, structured URL
resolution/output, final publication adapters and their associated §7/§9 tests
remain required before product switching. Current next actions are owned by PLAN.md.

<a id="origin-syntax-elision-20260920"></a>

## Origin syntax and elision integration (2026-09-20)

The user authorized integrating `draft/Changes/2026-09-20 Origin Syntax and Elision.md` with precedence over conflicting formal rules, then updating the compiler and source examples. The proposal was read but not edited. The resulting specification is self-contained: Chapters 2–3 own lexical/attachment rules, 6/11 declarations and accessors, 8 reconstruction/specialization, 9 paths, 10 callable compatibility, and 15 Origin contracts and elision. Appendix F summarizes the grammar; Appendix A records verification requirements. Remaining examples were migrated. Two repeated static-storage sections in Chapter 22 were removed.

### Implementation and decisions

- Brace declarations/arguments, prefix borrow Origins, trailing commas, role checks and generic/layout recognition replace the old contextual keywords. Constructors and explicit accessors accept their own lists; unparse and source-backed artifact reload preserve them.
- Missing aggregate input slots retain independent per-occurrence identities. Solver slots are separate from the value-input index used for Loan anchors. Call, conformance and accessor scratch arrays account for additional slots rather than assuming one Origin per parameter.
- Reconstructed Semantics applications retain conditional input slots. Non-borrow substitution drops only the inactive outer slot. Locals/results use their position rules; explicit annotations need safe-borrow proof. Original pair WholeType bindings are preserved.
- Stored accessor omissions inherit storage Types first. Supported specializations select by Origin-erased input structure, then inherit the complete contract. Corresponding preliminary input occurrences are reused; inheritance adds no second quantifier. Preliminary obligations are discarded and regenerated when those Types are rebound, preventing obsolete obligations from blocking ownership.
- Aggregate result defaults require existing Owned evidence. Nested Function Types form their own input/result boundary. Contract comparison accepts equivalent explicit/anonymous binders and rejects fixed-static implementations of universal requirements.
- Single nongeneric receiverless function references with an expected Function Type now check contravariant inputs, covariant results and implementation-side Origin substitution. Required Origins remain rigid. Generic/member/overload selection and inherited generic environments outside this path are explicitly unsupported. This repairs the recorded unchecked-signature path for supported references without claiming general function-item erasure.
- Ownership prepares substituted input field metadata, including abstract-Origin structs whose physical layout is concrete. Storage metadata carries a Binding version and reuses arrays. The initial implementation introduced enumerator allocations; indexed traversal restored the existing warm zero-allocation tests. Unknown field metadata conservatively retains destructor dependencies instead of causing a null dereference.
- Lowering and Emit consume the corrected existing typed plans; no Origin runtime representation or Origin-only code specialization was added. Library, milestone, benchmark and affected test sources were migrated. `examples/` needed no Origin spelling changes.

### Verification

Final solution builds are warning-free in Debug and Release. Direct xUnit runner results are:

| Check | Result |
| --- | --- |
| Full Debug suite | 10,676 passed; no failures, errors, skips or unrun cases |
| Full Release suite | 10,676 passed; no failures, errors, skips or unrun cases |
| Focused Origin revision suite | 48 passed; included in the full counts |
| New borrowed-aggregate and specialization fixtures | 4 native runs passed, O0/O2 |
| Existing BorrowStruct fixtures, including Milestone 5 | 20 native runs passed, O0/O2 |

Native checks include LLVM verification, dependency checks, linking, exact stdout/stderr and exit status. A conflicting mutation while an aggregate retains a borrow is rejected before emission. Artifact reload, repeated Binding and parse/write/parse are covered. The new aggregate fixture also measures 16 warm ownership/emission repetitions after warm-up and asserts zero bytes allocated on the executing thread; this is a bounded reuse check, not a throughput benchmark.

Commands (from the repository root):

```powershell
dotnet build Kimigayo.slnx --no-restore -v quiet
dotnet build Kimigayo.slnx --no-restore -c Release -v quiet
dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll -noLogo -parallelMode none -result-xml bin/origin-final-debug.xml
dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -noLogo -parallelMode none -result-xml bin/origin-final-release.xml
dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -noLogo -parallelMode none -class '*OriginSyntaxRevisionTest' -result-xml bin/origin-focused.xml
./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Anonymous*Origin*.ll' -OutputDirectory bin/origin-native-new
./backend/windows-x64/test-scalars.ps1 -FixturePattern 'BorrowStruct*.ll' -OutputDirectory bin/origin-native-regression
```

Evidence: [Debug build](bin/origin-build-debug.log), [Release build](bin/origin-build-release.log), [Debug XML](bin/origin-final-debug.xml), [Release XML](bin/origin-final-release.xml), [focused XML](bin/origin-focused.xml), [new native cases](bin/origin-native-new.log), and [native regressions](bin/origin-native-regression.log). Logs and generated fixtures are local ignored artifacts. The sandbox initially denied execution of the repository LLVM tools; the authorized elevated verification succeeded. No NativeAOT test was run.

An intermediate Debug build hit a file-copy lock because the test runner was still using its output assembly. After the runner exited, the solution build and full suite were rerun successfully; the final logs above contain the completed verification.

### Remaining coverage

The general declared-bound/principal Origin solver and universal Semantics-role proofs were already incomplete and are not completed here. Explicit-Origin or constrained original specializations, wider conditional contract proofs, custom-accessor execution and general function-item/Callable conversion remain incomplete. The formal revision applies to them; unsupported implementations are not specification exceptions. Current scope and next actions remain in PLAN OSE3 and I5/I12/I18, rather than being duplicated here.

<a id="origin-review-20260920"></a>

## Origin surrounding-code review (2026-09-20)

Follow-up review of the code around the integrated Origin syntax and elision revision, after the integration record above. Scope: defects, improvements, allocation and speed in the parser, Binding Origin model, requirement propagation and the storage metadata the revision touched. No specification rule was changed and no `draft` file was edited.

### Corrected defects

| ID | Defect | Correction |
| --- | --- | --- |
| OR1 | `ParseDeclarationType` overwrote `parameterList` after the grouped Container suffix branch, so `(Outer).Inner -> U` and `(Outer){a} -> U` became Function Types with no diagnostic, against SPEC 3.2. | The Origin check now narrows the flag instead of replacing it. Both spellings are added to `NestedTypeParseTest.FunctionArrowRequiresAParameterList`. |
| OR2 | `PrepareInstantiatedStorage` stamped `StorageVersion` before `PrepareEnumCases`, so a failed case substitution left a stamped, partially filled `StoredCases`; the next call in the same Binding version returned it as valid. | The enum path clears `StoredCases` on failure, matching the struct path. |
| OR3 | `MatchInputOrigin` wrote `target[pattern.Slot]` unchecked. Anonymous aggregate input slots are appended while parameter Types bind, so a pattern can name a slot wider than the width the caller reserved from `InputOriginCount`; other call sites already clamp with `Math.Min`. | The write is bounds-checked and a slot that cannot be carried is skipped. |
| OR4 | `AccumulateRequirements` and `ValidateOriginRequirements` indexed `Schema.Origins` and `Schema.GenericSlots` by the Type's own argument/component count. | Both loops are bounded by the declaration's own slot counts. |
| OR5 | `OriginRequirementWork.Dependents` was cleared only for declarations active in the current pass, while `CollectRequirementEdges` kept appending to producers that had dropped out, growing those lists once per Bind. | Work carries a pass stamp; only producers active in the current pass collect dependents. |

### Improvements and reductions

- `BoundType` computes two whole-subtree summaries at construction: `CarriesOrigin` and `CarriesOriginOrSlot`. Interning makes them exact and permanent. The recursive `HasDeclaredOrigins` helper is removed in favour of the first flag, and `SubstituteStoredOrigins`, `HasUnsubstitutedOrigin`, `MatchInputOrigins`, `RetainInnerOutlives` and `AccumulateRequirements` now skip complete uninteresting subtrees in constant time.
- `CollectRequirementEdges` deduplicates with a monotonic consumer stamp on the producer instead of a `HashSet` of work pairs, since edge collection visits one consumer at a time.
- `TypeSemanticsKoto.HasOrigin` replaces the three-property test in `CompleteOrigins` and in the pair-target check in `BindTypeStructure`.
- `BindOriginName`, the qualified-Origin path of `BindOrigin` and the result branch of `OmittedOrigin` evaluate `InputCount` once per scope and no longer resolve input syntax before the name matches.
- `OriginArgument` is a `struct`; its unused `TinyhandObject` attributes are removed, since the syntax tree is rebuilt by reparsing. `ParseOriginBraces` grows the result array directly instead of a `List` plus a copy.
- `HasBorrowOriginSuffix` stops at statement and block boundaries so an unbalanced brace cannot drag the probe through the rest of the document.

`OriginParameter.Span` and the `OriginNameList` span tracking were examined as apparently dead and were kept: `TypeBindingTest.OriginDeclarationSpansAndExpressionBindingsAreRetained` requires them.

### Measurements

Origin-heavy workload: 256 functions over `struct View<T> {a, b}` and a nested `Outer{o}.Inner{i}`, each with named aggregate arguments, a prefix borrow Origin and a grouped Container qualifier. Pinned to one core at high priority, interleaved baseline/current processes in the same session, medians and minima over 40 warm and 20 cold samples.

| Measure | Baseline | Current |
| --- | --- | --- |
| Warm `Bind` of the existing syntax | 3.41–3.43 ms min | 3.25–3.32 ms min |
| Warm `Bind` allocation | 0 bytes | 0 bytes |
| Cold parse and first `Bind` | 6.24–6.53 ms median | 6.00–6.04 ms median |
| Cold allocation | 2,823,968 bytes | 2,679,720 bytes |

Timing on this hybrid CPU varies by several percent between unpinned runs; only the interleaved pinned comparison above is reported. This is a bounded workload measurement, not a compiler-wide throughput claim.

### Verification

| Check | Result |
| --- | --- |
| Debug solution build | warning-free |
| Release solution build | warning-free |
| Full Debug suite | 10,678 passed; no failures, errors, skips or unrun cases |
| Full Release suite | 10,678 passed; no failures, errors, skips or unrun cases |
| Anonymous Origin native fixtures | 4 native executions passed, O0/O2 |
| BorrowStruct native regressions | 20 native executions passed, O0/O2 |

Commands (from the repository root):

```powershell
dotnet build Kimigayo.slnx -c Debug -v quiet
dotnet build Kimigayo.slnx -c Release -v quiet
./xUnitTest/bin/Debug/net10.0/xUnitTest.exe -parallel all
./xUnitTest/bin/Release/net10.0/xUnitTest.exe -parallel all
./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Anonymous*Origin*.ll' -OutputDirectory bin/origin-review-native
./backend/windows-x64/test-scalars.ps1 -FixturePattern 'BorrowStruct*.ll' -OutputDirectory bin/origin-review-regression
```

The count rises from 10,676 to 10,678 because of the two added parameter-list cases. No NativeAOT test was run. OR2 and OR3 are defensive corrections on paths the suite does not reach today, so they carry no new regression case.

### Remaining coverage

This review did not change the open Origin work. The general declared-bound/principal solver, universal Semantics-role proofs, explicit-Origin and constrained specialization inheritance, custom-accessor execution and general function-item/Callable conversion remain open in PLAN OSE3 and I5/I12/I18.
