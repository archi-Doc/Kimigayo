# Documentation Markdown verification data

The authority is the formal [Documentation Markdown profile](../../../spec/documentation-markdown.md), with unchanged syntax defined by CommonMark 0.31.2. Markdig 1.3.2 is a comparison implementation, not the oracle. Tests are offline and embed the JSON fixtures; no data download or external executable is needed for managed conformance tests.

## Upstream corpus and attribution

`commonmark-0.31.2.json` is the unmodified [official 652-example corpus](https://spec.commonmark.org/0.31.2/spec.json) from the [CommonMark specification](https://spec.commonmark.org/0.31.2/) by John MacFarlane, licensed under [Creative Commons Attribution-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-sa/4.0/). The upstream corpus retains that license, independently of this repository's code license. Its UTF-8/LF SHA-256 is `D431B29D97B6F73E69D547109CF5081578FAC931E72AFE95639EBE766C1B2A20`. Input, expected HTML, section, example number and original specification line numbers are preserved. No upstream expectations were rewritten.

## Selection and comparison conditions

`exclusions.json` explicitly records each of the 280 examples outside the common comparison set. Selection is fixed independently of test results, never based on whether a parser passed. Whole chapters devoted to omitted rules are excluded; cross-feature examples elsewhere have individual reasons. Each exclusion has one principal reason even when multiple omitted rules occur.

| Principal reason | Examples |
| --- | ---: |
| Raw HTML | 76 |
| Reference links/definitions | 70 |
| Indented code | 45 |
| Setext headings | 29 |
| Images | 24 |
| Thematic breaks | 23 |
| Lazy continuation, replaced by explicit continuation | 9 |
| Unadopted named character references | 4 |

The remaining **372 examples** each have two tests: an independent-parser assertion against official expected HTML, and a Markdig comparison also checked against that oracle. Markdig uses `DisableHtml().UsePreciseSourceLocation()` with no extensions. Both receive identical LF text. The structural fixture renderer uses the examples' URL percent-encoding convention; it performs no URL validation, source-relative resolution, heading relocation or publication policy. Markdig's omitted newline between `<li>` and `<p>` is the only layout normalization. Text/code whitespace, attributes and inline structure are otherwise compared exactly.

All 652 examples also receive tree and source-range invariant checks (selected examples in their conformance tests, excluded examples in the manifest audit). The audit verifies upstream identity, unique exclusions, valid reasons and complete accounting. Excluded examples are not reported as conformance passes or silently skipped xUnit cases.

## Additional coverage

| Suite | Scope |
| --- | --- |
| `DocumentationMarkdownConformanceTest` | 372 official conformance cases, 372 Markdig comparisons, 320 exact official non-link HTML expectations through the product renderer, manifest audit, and 592 deterministic delimiter/link combinations |
| `DocumentationMarkdownDifferenceTest` | Every profile §2.4 boundary row, the five-space list case, and explicitly recorded Markdig output differences |
| `DocumentationMarkdownBoundaryTest` | Container 3/4-column boundaries, partial tabs, exact decoded/multiline/EOF ranges under LF/CR/CRLF, Unicode 15 under en-US/ja-JP/tr-TR cultures, non-NFC/case-sensitive names, character-reference limits, GC stability, cancellation and concurrent publication |
| `DocumentationMarkdownIntegrationTest` | Actual external/generic/Semantics/length/Origin names, Binding receiver roles, namespace ambiguity, independent fragments, generated provenance, configuration reload, edited declarations, effective access/specialization, interrupted Markdown without language errors |
| `DocumentationMarkdownOutputTest` | URL components, one-pass UTF-8 decoding, source/output mapping and project identity, display page versus HTML base, generated references, rewrite validation, escaping, deep/concurrent/reentrant/cancelled output, Binding-based classification and assembly dependency isolation |
| Existing documentation suites | All 17 declaration targets consume independent syntax; collection on/off compilation invokes the product facade, item classification, HTML and diagnostics before verifying identical generated IR. Old product tests now assert the adopted limited profile, including lowercase items and omitted reference links/named entities. |

The syntax-integration adapter remains test-only and obtains receiver roles from
`BoundSymbol.ReceiverIndex`. The product facade now obtains the same real Binding
facts independently; output tests verify ordinary `self` parameters, explicit
receiver exclusion, and querying before/after Binding. No spelling-based receiver
exclusion is used. Markdig is a private dependency of tests/benchmarks only.

Recorded comparison findings:

- `[x]( "title")` denotes a quoted destination, not an empty destination with a title. Destination scanning precedes title scanning, also in the [CommonMark reference implementation](https://github.com/commonmark/cmark/blob/0.31.1/src/inlines.c). The independent parser's earlier special case was removed; `[x](<> "title")` remains the explicit empty-destination form with a title. Regression expectations were corrected and expanded.
- Markdig omits an empty HTML `title` attribute. The independent syntax API retains absent versus empty titles; this difference is asserted separately rather than erased by normalization.
- Markdig emits nested anchors for `[<https://a.b>](u)` and its empty-destination variant. The independent parser follows the adopted no-nested-links rule, keeping the outer brackets/destination as text. These are explicit comparison differences, not exceptions to the independent parser's acceptance criteria.

## Reproduction and limits

```powershell
dotnet build Kimigayo.slnx --no-restore -c Release -v:q
dotnet xUnitTest/bin/Release/net10.0/xUnitTest.dll -class '*Documentation*' -parallelMode none -failSkips
```

Repeat with `Debug`. Full managed regression uses the same runner without `-class`. Repository restore is required on a fresh checkout. Native integration uses the repository's Windows x64 toolchain and `backend/windows-x64/test-scalars.ps1` with `-FixturePattern DocumentationComments.ll`; this is not NativeAOT.

DM5 additionally verifies the product renderer, structured URL resolver and facade.
The 320 official output cases exclude syntax trees containing links because the
product URL policy intentionally differs from CommonMark serialization; link
expectations are tested explicitly in the output suite. The structural renderer
remains test-only. This is bounded coverage, not exhaustive conformance or
cross-OS/runtime certification. Culture variation uses the current Windows/.NET
environment. [Product API](../../../Kimi/Compiler/Documentation/README.md),
[measurements](../../../Benchmark/DocumentationMarkdown.md), and
[execution evidence](../../../PLAN_HISTORY.md#documentation-markdown-product-switch-20260920)
record conditions and limits; current next actions belong in
[PLAN.md](../../../PLAN.md#6-next-actions).

## 2026-09-20 parser review

`DocumentationMarkdownReviewTest` adds focused regressions for original delimiter-run lengths in emphasis, NUL replacement in links and delimiter classification, exact empty-link depth, bounded node formatting and UTF-8 URL output. It also validates structure/ranges and an official-output digest for 10,000 deterministic inline inputs.

The generator in that test uses `Random(20260920)` and its fixed token list. Expected output was generated with the official [commonmark.js 0.31.2](https://github.com/commonmark/commonmark.js/tree/0.31.2) distribution (SHA-256 `4D124568B8D4490DF72FEA8AA0F9A144C5B581B0DE0EB63D3A996104B2797EDC`). Each input and HTML result is hashed as UTF-8 followed by one zero byte. The input digest pins generation as well as the output digest. The tests remain offline; no JavaScript runtime or new product dependency is needed.

Markdig differs from the official implementation for partially escaped backtick runs and some partially consumed emphasis runs. An escaped first backtick can leave a code-span opener; Markdig keeps some such runs literal. These differences were investigated rather than adopted as expected behavior. The generated inputs contain no omitted block features, reference links, images, raw HTML or version-sensitive Unicode characters; broader profile and Unicode coverage remains in the existing suites.
