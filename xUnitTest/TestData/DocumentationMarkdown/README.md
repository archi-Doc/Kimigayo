# Documentation Markdown verification data

The authority is the adopted [limited profile](../../../draft/Design/2026-09-19%20Documentation%20Markdown.md), with unchanged syntax defined by CommonMark 0.31.2. Markdig 1.3.2 is a comparison implementation, not the oracle. Tests are offline and embed the JSON fixtures; no data download or external executable is needed for managed conformance tests.

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
| `DocumentationMarkdownConformanceTest` | 372 official conformance cases, 372 Markdig comparisons, manifest audit, and 568 deterministic delimiter/link combinations |
| `DocumentationMarkdownDifferenceTest` | Every §2.2 row, the five-space list case, and explicitly recorded Markdig output differences |
| `DocumentationMarkdownBoundaryTest` | Container 3/4-column boundaries, partial tabs, exact decoded/multiline/EOF ranges under LF/CR/CRLF, Unicode 15 under en-US/ja-JP/tr-TR cultures, non-NFC/case-sensitive names, character-reference limits, GC stability, cancellation and concurrent publication |
| `DocumentationMarkdownIntegrationTest` | Actual external/generic/Semantics/length/Origin names, Binding receiver roles, namespace ambiguity, independent fragments, generated provenance, configuration reload, edited declarations, effective access/specialization, interrupted Markdown without language errors |
| Existing documentation suites | All 17 declaration targets now consume independent syntax; collection on/off compilation also requests independent parsing/extraction before verifying identical generated IR. Existing association, selection, diagnostics and old product-path checks remain. |

The declaration-name adapter is test-only and obtains receiver roles from `BoundSymbol.ReceiverIndex`. It does not replace the product publication API. No direct `self`-spelling exclusion is used.

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

These tests verify the implemented syntax/candidate API and its use with Kimigayo. They do not establish exhaustive conformance, cross-OS/runtime testing, benchmark thresholds or complete §7 URL/rendering support. Culture variation is tested on the current Windows/.NET environment. The structural renderer is deliberately test-only. Independent product rendering, structured URL resolution and final product adapters require their own integration/output tests before switching the product path. Execution results and environment are recorded in [PLAN_HISTORY.md](../../../PLAN_HISTORY.md#documentation-markdown-tests-20260920); current next actions belong in [PLAN.md](../../../PLAN.md#2-execution-state).
