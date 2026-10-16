# A5 step 0: family census

Base: worktree `bin/worktrees/a5-step0` at dev `b2edea8b`. Paths below are relative to the worktree root. These are read-only, grep-level counts. Nothing was built or run.

## Sources and method

| Column | Sources | Size |
| --- | --- | --- |
| lib | `src/Kimi/Library/*.kimi` | 31 files, 2,128 lines |
| ms | `tests/milestones/Milestone1..43.kimi` | 43 files, 2,250 lines |
| corpus | `tests/diagnostics/pair-cases.json` (58 `Source`s), `mutations.json` (20 `After` snippets), `docs/examples/**/*.kimi` (39), `tests/mir/*.kimi` (30 MIR golden inputs) | 147 sources |
| fix | string literals of the 426 C# test files that call `EmitFixture` (1,180 calls) or `WriteFixture` (319 calls) | 12,925 literals |
| tests | string literals of the other 315 test files (`tests/xUnitTest/**`, `tests/Workloads`) | 6,034 literals |
| specA5 | the ```` ```kimi ```` blocks in the requested chapters that A5 decides (39), plus 8.11.3 and 8.11.6 for F6 (2). All of `docs/spec` has 373 blocks. | 41 blocks |

`syntax.json` (358 parse cases) is excluded because it has no A5 forms. The C# literals are extracted from raw, verbatim and regular strings, unescaped, and kept when they look like Kimi source. The fix and tests columns are regex matches over that text, so read them as orders of magnitude. The scripts are in `scratchpad/census-work/`. Old-path codes are counted as occurrences / files of the code name in `tests/xUnitTest/**`, excluding meta files. "Attr." assigns each file that expects an A5 code to one family by its file name, so it is approximate.

## 1. Family table

Counts are occurrences in lib / ms / corpus / fix / tests / specA5.

| Family | Form (regex #) | lib | ms | corpus | fix | tests | specA5 | Old-path codes: occurrences/files; Attr. files/expectations | Size |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| F1 Core Loan flow | 1 shared borrow `@ref`/`@objref` | 14 | 94 | 20 | 2,586 | 148 | 32 | `ComparisonLoanConflict_Kd` 120/57, plus 21 in pair-cases.json; `IsLoanConflict()` (any of the 4 conflict codes) 131/84; these codes are shared with F2, F3, F7, F8 and F10. Attr. 77/165 | **L** |
| | 2 exclusive borrow `@uniq`/`@objuniq` | 23 | 49 | 40 | 1,694 | 97 | 24 | | |
| | 3 raw borrow `@raw` (rule 24) | 20 | 0 | 2 | 140 | 9 | 1 | | |
| | 4 borrow of a call-result temporary (rule 23) | 5 | 0 | 0 | 62 | 1 | 2 | | |
| | 5 reference-typed result (rules 2, 11) | 25 | 21 | 4 | 762 | 151 | 22 | | |
| | 6 `==`/`!=` (rule 27, upper bound) | 36 | 263 | 94 | 5,117 | 188 | 11 | | |
| | 7 equality with a string operand (Non-Copy) | 0 | 0 | 9 | 170 | 23 | 1 | | |
| | 8 handle types `obj/` `rc/` `arc/` (rule 29) | 3 | 11 | 4 | 161 | 56 | 9 | | |
| | 9 handle creation | 0 | 6 | 5 | 369 | 19 | 7 | | |
| F2 Reborrows and splitting | 10 reborrow `@follow@ref/@uniq/…` | 3 | 10 | 4 | 287 | 25 | 6 | conflict codes; pair-cases.json: 20 `uniq` and 1 `owner` `ComparisonLoanConflict_Kd` (rule 28, pair layers). Attr. 12/33 | **M** |
| | 11 `@follow` (all) | 5 | 24 | 126 | 1,671 | 134 | 18 | | |
| F3 Calls | 12 `f(x@uniq, … x …)` | 0 | 0 | 2 | 27 | 2 | 4 | `CallActivationConflict_Kd` 29/17, `CallReservationConflict_Kd` 4/2, `PlacementActivationConflict_Kd` 4/2. Attr. 18/49 | **L** |
| | 13 `x.m(… x …)` (receiver read by an argument) | 0 | 0 | 0 | 17 | 1 | 2 | | |
| | 14 `for` (Iterator descent) | 3 | 28 | 12 | 345 | 37 | 1 | | |
| | (form 5: retention through the public contract) | | | | | | | | |
| F4 Origin relations | 15 `during` | 171 | 36 | 14 | 1,933 | 511 | 49 | `UnsatisfiedOriginRelation_Kd` 53/18, `UnprovenOriginRelation_Kd` 84/20, `UnprovenConstraint_Kd` 96/45. Binding also reports these codes. Attr. 25/160 | **L** |
| | 16 `origin a == b` / `outlives` | 1 | 10 | 0 | 403 | 63 | 5 | | |
| | 17 Origin slot header `struct X {s}` | 16 | 7 | 2 | 218 | 133 | 8 | | |
| | 18 binding set `Type{name}` | 1 | 7 | 0 | 291 | 40 | 4 | | |
| F5 Effects and static anchors | 19 mutable static (`var` member of a `group`) | 0 | 4 | 3 | 13 | 30 | 2 | `CallEffectConflict_Kd` 6/3. Attr. 3/7 | **S** (plus the A4 publication) |
| | 20 static write `Type.member (op)=` | 0 | 5 | 3 | 1 | 12 | 0 | | |
| | 21 `effect` clause | 2 | 0 | 0 | 58 | 134 | 4 | | |
| F6 `preserves results` | 22 `preserves results` | 1 | 0 | 0 | 10 | 34 | 1 | No A5 code. 8 of the 18 C# test files that mention it expect A4 codes. Ledger rows a4-preserves, a4-binding, a4-callable-preserves and a4-binding-limit are (b) U9. | **S** (needs S2) |
| F7 Closures and escape | 23 anonymous `func` | 0 | 17 | 26 | 975 | 64 | 5 | conflict and Origin codes. Attr. 17/54 | **M** |
| | 24 capture list `func [..]` | 0 | 16 | 23 | 688 | 38 | 5 | | |
| | 25 borrowing capture `[x@ref]`/`[x@uniq]` | 0 | 1 | 3 | 52 | 10 | 0 | | |
| F8 Updates and match subjects | 26 write through `@follow =` | 0 | 5 | 58 | 327 | 27 | 3 | activation and conflict codes. Attr. 21/53 | **L** |
| | 27 compound assignment through `@follow` | 0 | 2 | 1 | 152 | 14 | 5 | | |
| | 28 compound assignment (all, rule 25) | 37 | 20 | 38 | 1,102 | 78 | 12 | | |
| | 29 `match` | 36 | 73 | 23 | 789 | 229 | 14 | | |
| | 30 `match x@op` (borrowed subject, lower bound) | 0 | 0 | 0 | 12 | 3 | 2 | | |
| | 31 guard (`Pattern if … =>`) | 0 | 2 | 9 | 130 | 14 | 6 | | |
| | 32 collection operation (rule 30) | 8 | 36 | 5 | 545 | 34 | 7 | | |
| | 33 `replace`/`exchange`/`swap` (rule 16) | 0 | 10 | 10 | 150 | 9 | 6 | | |
| F9 Defaults and cases | 34 parameter default (parsed) | 2 | 2 | 8 | 453 | 139 | 6 | `DefaultArgumentAccess_Kd` 4/1, `DefaultArgumentBorrow_Kd` 8/2. Attr. 6/22 | **M** (M39) |
| | 35 default reading another input or `self` | 0 | 0 | 6 | 243 | 75 | 2 | | |
| F10 Unreachable code | 36 statement after `return`/`break`/`continue`/`$abort(` at the same indentation | 0 | 0 | 1 | 27 | 23 | 4 | conflicts in check-only code. Attr. 1/1 (`UnreachableOwnershipTest`) | **S** |
| A6 Reporting | every problem row | | | | | | | 11 A5 codes, 413 occurrences in 180 files; `OwnershipStorageLimit_Kd` 5/3 | **M** |

**Regexes** (.NET, multiline; all on whole sources):
1 `@(ref|objref)\b` · 2 `@(uniq|objuniq)\b` · 3 `@raw\b` · 4 `\)@(ref|uniq|objref|objuniq)\b` · 5 `->[^\n=]*\b(ref|uniq|objref|objuniq)/` · 6 `(==|!=)` · 7 `(==|!=)\s*"|"\s*(==|!=)` · 8 `\b(obj|rc|arc)/` · 9 `@(obj|rc|arc)\b|\bmake(Obj|Rc|Arc)\(` · 10 `@follow@(ref|uniq|objref|objuniq)\b` · 11 `@follow\b` · 12 `\b([a-z_]\w*)@uniq\b(?:[^()\n]|\([^()\n]*\))*?,(?:[^()\n]|\([^()\n]*\))*?\b\1\b` · 13 `\b([a-z_]\w*)(\.\w+)*\.\w+\((?:[^()\n]|\([^()\n]*\))*?\b\1\b` · 14 `^\s*for\s+\S.*\bin\b` · 15 `\bduring\b` · 16 `^\s*origin\s+\S+\s+(==|outlives)\s` · 17 `^\s*(public\s+|internal\s+|private\s+)?(open\s+|sealed\s+)?(struct|enum)\s+\w+(<[^>\n]*>)?\s*\{[a-z]\w*(\s*,\s*[a-z]\w*)*\}` · 18 `[A-Z]\w*(<[^\n{}]*>)?\{[a-z]\w*\}` · 19 a line `^\s*(public |internal |private )?var \w+` at the member indentation of a `group` (indentation scan) · 20 `\b[A-Z]\w*\.[a-z]\w*\s*(\+|-|\*|/|%|&|\||\^|<<|>>)?=(?!=)` · 21 `^\s*effect\s` · 22 `\bpreserves\s+results\b` · 23 `(?<![A-Za-z_])func\s*(\[|\()` · 24 `(?<![A-Za-z_])func\s*\[[^\]\n]*\]` · 25 `(?<![A-Za-z_])func\s*\[[^\]\n]*@(ref|uniq)` · 26 `@follow\s*=(?!=)` · 27 `@follow\s*(\+|-|\*|/|%|&|\||\^|<<|>>)=` · 28 `(\+|-|\*|/|%|&|\||\^|<<|>>)=(?!=)` · 29 `\bmatch\s+\S` · 30 `\bmatch\s+[\w.\[\]]+@(ref|uniq|follow|objref|objuniq)(@\w+)*[ \t]*($|//)` · 31 `^\s*(?!let\s+\w+\s*[:=])(?!var\s+\w+\s*[:=])(\.[A-Za-z]|\(|_|let\s|var\s|-?\d|"|'|true\b|false\b)[^\n=]*\sif\s[^\n]*=>` · 32 `\.(tryGet|tryGetUniq|tryInsert|insertOrReplace|insert|append|appendAll|appendCopies|remove|removeAll|pop|swap|swapRemove|reverse|sort|clear|truncate)\(` · 33 `\b(replace|exchange|swap)\s*\(` · 34/35 the balanced parameter list after `func name<…>(`, split at top-level commas; `name: Type = expr`, where expr names another parameter or `self` · 36 the next non-blank, non-comment line after `^(\s*)(return\b|break\b|continue\b|\$abort\(|throw\b)` has the same indentation.

## 2. Families

**F1 Core Loan flow (L).** This is the bulk:
- Borrows appear 37 times in lib, 143 in ms and about 4,300 in fixtures, and lib returns references 25 times.
- `ComparisonLoanConflict_Kd` is the most expected A5 code (120/57). Another 84 files accept any conflict code through `IsLoanConflict()` (`tests/xUnitTest/CompilationTestHelper.cs:69`), so many tests do not separate F1 from F2, F3 or F8.
- Rule 27's Non-Copy subset is small (string operands: 9 in corpus, 170 in fix).
- Handles: 11 types in ms and 369 creations in fix.
- `@raw` is only in `Storage.kimi` and `DictionaryStorage.kimi`.

**F2 Reborrows and splitting (M).** Explicit reborrows are few (lib 3, ms 10). All 21 conflicts expected by `pair-cases.json` (its families A1–A10) are pair-layer reborrows (rule 28). Splitting is tested in the `Disjoint*` and `*Projection*` files.

**F3 Calls (L).** Two-phase forms (12, 13) are absent from lib and ms: they occur 44 times in fixtures and 6 times in the SPEC. The family is large because every call does retention by contract (form 5) and Iterator descent (28 ms loops), and it needs C1/C8. Activation codes are expected in 17 files (`CallReservationTest`, `ValueCallActivationTest`, `ArraySortTest`, …).

**F4 Origin relations (L).** It has the most annotations (`during`: 171 in lib, 511 in tests) and the most expectations (Attr. 160: `LocalRegion*`, `OriginPremise*`, `*OriginForwarding*`). All three codes are also Binding codes (OJ1–OJ7), so the Origin boundary table decides which of the 233 occurrences A5 owns.

**F5 Effects and static anchors (S).** There is no mutable static in lib. `Milestone42.kimi` and `Milestone43.kimi` have 4 mutable statics and 5 static writes between them. The rules are few, but they depend on A4 publishing static keys, open keys and anchors. The 15.6.4 SPEC block holds F5's examples.

**F6 `preserves results` (S).** There is one declaration (`Iterator.kimi:6`) and no milestone use. Its backlog is the four (b) U9 ledger rows (old A4 codes), and the family waits for S2.

**F7 Closures and escape (M).** Milestones have 17 closures but only one borrowing capture (`Milestone34.kimi` `[bias@ref]`). Fixtures have 975 closures and 52 borrowing captures. 15.8.3 has no SPEC example.

**F8 Updates and match subjects (L).** It has the most forms, and lib uses them (37 compound assignments, 36 `match`, 8 collection operations). So do the milestones (73 `match`, 36 collection operations, 10 `replace`/`swap`). Form 30 is a lower bound, because grep cannot see a reference-typed subject.

**F9 Defaults and cases (M).** lib and ms have no default that reads another input. The corpus has 6 (`tests/mir/calls.kimi`, `cases.kimi`, `defaults.kimi`) and fix 243 (closure default templates). The size comes from M39 and the Semantics views (section 5).

**F10 Unreachable code (S).** None in lib or ms. 50 in tests (`AbortEmissionTest`, `ControlFlowAnalysisTest`), plus one SPEC block. MIR already builds check-only code (ledger rows a1-c3, a1-c3-moved).

**A6 Reporting (M).** 11 codes, expected 413 times in 180 files. A further 183 `.Ownership.Bodies` reads in 78 files need migrating (plan §8).

## 3. Difference ledger

**How rows are produced** (`tests/xUnitTest/MirDifferenceLedger.cs`, DEC R4-M17): with `KIMI_MIR_DIFF` set, the fixture matches each mir-pipeline compilation's old A-owned records against the MIR records and problems. The old records come from `LegacyRecords.Of`, filtered by the R4-M4 `Compared` table, and the match is on (code, subject node). It writes raw rows. Aggregation (`ledger.tsv`, `summary.md`) works as follows:
- Within a body, an unmatched old row and new row of one code pair into a `location` difference. Other unmatched rows are `missing` or `new`, and a comparison with records on one side only adds an `acceptance` difference.
- Each signature is classified by the first matching row of `tests/xUnitTest/TestData/mir-diff/classification.tsv` (DEC R4-M16): (a) old-path error, (b) new-path error, (c) SPEC ambiguity, (d) intended change, (e) not ported.
- P9 is the number of unclassified signatures.

**Classification table today:** 20 rows. By analysis: A1 11, A4 5, A3 2, U 1, `*` 1. By class: (a) 2, (b) 10, (d) 8. It has **no A5 row**. MIR has reported no A5 problem since the discard (`0d989e68`), so every old A5 record is a `missing` signature with category `?` and counts in P9. So do the ownership `Unsupported_Kd` gates (9 report sites in `src/Kimi/Compiler/Analysis`).

**Recorded output: none available.** None of these exist: `artifacts/verify/mir-diff/` (main checkout or any `bin/worktrees/*/artifacts`), `artifacts/handoff/r4` (not carried over), a `ledger.tsv` under `temp/` or the system temp directory. The last figures are in commit messages:

| Ledger | Comparisons/rows | P9 | Open (b) |
| --- | --- | ---: | ---: |
| r4-u0b baseline (`fdcda7d3`) | 1,746 / 2,043 (both 7, old 1,973, new 63), 206 signatures | 204 | 0 |
| r4-u4 (A3) | | 185 | 5 rows |
| r4-u1 (A1) | | 159 | 58 sig. |
| r4-u6 (A4) | | 143 | 56 |
| r4-resume V13, 2026-10-13 (after U6b) | | 143 | 47 |

U7's A5-1 added no problems (r4-u7r-fixes equal to r4-u5), so its revert should not move P9. The discard commit says the ledger was not rerun. What survives cannot split the 143 by code: apart from "A1 2 unclassified" (a.indices, U11), they are A5 and U signatures.

**Command (not run)** for the step-0 re-baseline, from the worktree root, into a fresh directory:

```powershell
$env:KIMI_MIR_DIFF = (New-Item -ItemType Directory artifacts/verify/mir-diff/a5-step0).FullName
./scripts/verify.ps1 -Pipeline mir -TestPurpose Functional -Name a5-step0-ledger   # tier 1 (RequestedCorpus) + tier 2
Remove-Item Env:KIMI_MIR_DIFF
# -> artifacts/verify/mir-diff/a5-step0/ledger.tsv, summary.md
# re-aggregate: $env:KIMI_MIR_LEDGER = '<dir>'; ./scripts/verify.ps1 -Method XunitTest.MirDifferenceTest.RequestedLedger
```

VERIFICATION adds two conditions: the Functional failure set must equal a mir run without the ledger (V13), and the Allocation tests run without it. To map families, join `ledger.tsv`'s code column with this mapping:
- Conflict codes: F1/F2/F7/F8/F10.
- `Call*`/`PlacementActivation`: F3/F8.
- `CallEffectConflict`: F5.
- `DefaultArgument*`: F9.
- Origin codes: F4 (F7 for escape).
- `OwnershipStorageLimit`: plan §4.6 / A6.
- `Unsupported`: by subject form.

## 4. SPEC-example oracle

The requested chapters hold 64 blocks. 25 are decided elsewhere: 13 non-A5 (13.5.1–4, 13.6, 14.8.4, 14.9.1/3, 14.10.1/2), 6 A1 (3.5, 15.1.1–5) and 6 Binding (15.2, 15.3 except .4/.6, 15.4). That leaves 39 A5 blocks, plus 2 F6 blocks (8.11.3, 8.11.6). A "commented" error line is commented out, so the block compiles and rejects only once the line is uncommented.

| Family | Blocks | With verdict | Error lines active + commented | Valid/OK lines | Sections |
| --- | ---: | ---: | --- | ---: | --- |
| F1 | 13 | 12 | 2 + 9 | 4 | 3.4, 3.4.2, 3.4.3, 3.5.3, 3.6, 5.2.1, 13.4 (2), 13.7.1, 14.9.2, 15.6, 15.6.6, 16.3.3 |
| F2 | 6 | 4 | 2 + 2 | 0 | 3.5.1, 13.5.5 (3), 13.5.6, 15.5 |
| F3 | 2 | 2 | 3 + 2 | 3 | 15.6.4 (it also holds F5's static-anchor and erasure errors), 15.6.7 |
| F3/F8 | 1 | 1 | 1 + 0 | 0 | 4.9.6 |
| F4 | 4 | 4 | 3 + 0 | 3 | 15.3.4, 15.3.6, 15.6.1, 15.6.5 |
| F6 | 2 | 1 | 1 + 0 | 0 | 8.11.3, 8.11.6 (outside the requested list) |
| F7 | 1 | 1 | 0 + 2 | 0 | 15.8.2 |
| F8 | 10 | 7 | 5 + 4 | 3 | 13.7.2, 14.8 (3), 14.8.3 (2), 15.1.6, 15.7, 15.7.1, 15.7.3 |
| F9 | 1 | 1 | 2 + 0 | 1 | 7.2.3 |
| F10 | 1 | 1 | 2 + 0 | 2 | 14.10.3 |
| Total | 41 | 34 | 21 + 19 | 16 | |

Rules 8 (15.6.2), 9/10 (15.6.3), 16 (15.7.2), 18 (15.8.3) and 24's 5.2.2 have no example. F5 has no block of its own.

**What exists:**
- `tests/xUnitTest/Tests/VerifiedSpecExampleTest.cs` is the only SPEC-example harness. It finds the block marked `<!-- verified-example: comparison-inspection … -->` (`docs/spec/13-operators-and-assignment.md:191`), emits it, uncomments its rejection and asserts `IsLoanConflict()`.
- Tier 1 (`MirDifferenceTest.RequestedCorpus` → `DiagnosticSnapshotTest.Take("spec/…")`) checks all 373 blocks, but it only compares the two paths, with no expectation.
- Most A5 blocks are fragments. For example, 15.6.7 uses the undeclared names `tasks`, `makeInventory` and `inspect`, so Binding is incomplete and A5 is derived or skipped (R4-M5).

**Attaching expected codes (proposal):** add a test-side table such as `tests/diagnostics/spec-cases.json` (the `pair-cases.json` precedent). It changes no SPEC text. Each row has:
- the block, located by an anchor that occurs exactly once (the `SyntaxLocation.Resolve` rule), so SPEC drift fails loudly;
- a prelude for the undeclared names;
- variants: the block as written, and each commented rejection uncommented alone (as `VerifiedSpecExampleTest` does);
- per variant, `accept` or `{Code, Line}`, written from SPEC and DIAGNOSTICS independently of the compiler (§9.2).

That is about 40 error variants and 34 accepting bases, run by a Theory on the MIR pipeline. A marker comment before each block would instead need an explicit SPEC revision.

## 5. Semantics case views

Views per body are the product, over the pair binders in scope (the body's and its Container's), of the admitted-set size (§8.3.4 from the `s is …` premises; nine Semantics without premises). Only bodies with at least 2 views are listed.

| Source | Multi-view bodies | Views | Estimated loan-free views |
| --- | ---: | ---: | ---: |
| lib | 0 (one pair function: `Intrinsics.kimi:11` `clone<s/T>`, `s is rc or arc`, 2 cases, no body) | 0 | 0 |
| ms | 3, all in `Milestone28.kimi`: `view` {owner, ref, uniq}, `viewUniq` {owner, uniq}, `total` {owner, ref, uniq} | 8 | 0 (each uses an index call, `@follow@ref/@uniq`, `for` and a receiver borrow in every case) |
| corpus: pair-cases.json | 55 | 112 | 7 |
| corpus: `tests/mir/cases.kimi`, `adaptations.kimi` | 14 | 41 | 24 |
| fix (C# fixture files) | 135 (`PairFollowTest` 66 bodies / 218 views, `PairAnnotationBindingTest` 15/94, `GenericAdaptationTest` 17/56) | 479 | 161 |
| tests (other C#) | 86 (`GenericCaseDiagnosticTest` 7/181, `PairGroupingBindingTest` 10/180) | 663 | 266 |
| SPEC blocks | 11 | 44 | 16 |

The maximum is 9 views per body in lib, ms, corpus, fix and SPEC, and 81 in tests. The case-limit tests (`GenericCaseDiagnosticTest`, `MirBuildTest`, `OriginPremiseCatalogTest`) generate up to 9^7 = 4,782,969 combinations. They exceed the limit on purpose (§8.15.2, §23.1.1) and are not counted.

**Loan-free estimate (heuristic):** take bodies with no borrow, `@follow`, index, method call, `for` or `match`. Their views in which every binder takes a non-borrow Semantics (owner, obj, rc, arc, raw) are counted loan-free. The evaluator should skip a view exactly when its facts introduce no Loan. lib and ms give only 8 views, all with Loans, so R4-M12 exercises Semantics cases through `Milestone28` alone.
