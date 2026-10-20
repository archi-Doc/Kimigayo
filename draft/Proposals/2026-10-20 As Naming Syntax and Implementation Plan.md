# As Naming Syntax and Implementation Plan

Specification change and implementation plan — 2026-10-20; revised the same day against the Principles (§8).

## 1. Authority, scope and start gate

- **Decision.** The owner adopts `external as internal` for parameter names and `alias A as B` for named aliases.
  For the changes stated here, this plan takes precedence over [SPEC](../../docs/SPEC.md) and its referenced
  documents; existing specifications govern everything else. This revision also proposes two rules for owner
  approval: a parameter clause must rename (§2.3), and a defined recovery for the former spelling (§2.4). This task
  changes no current specification or executable source and claims no support; formal SPEC intake is a separate,
  explicitly instructed task (§6).
- **Start gate.** Implementation starts only after R9 is complete, as recorded in the
  [compiler stage ledger](../../docs/dev/COMPILER_ARCHITECTURE.md#stage-ledger). Every unit in §4, including
  restructuring, executable probes, migration, cleanup and optimization, is behind that gate; read-only inspection,
  design and document review may precede it. Nothing of this change goes into R9 or an earlier stage.
- **Named aliases.** The [Named Alias Declarations plan](2026-10-16%20Named%20Alias%20Declarations.md) keeps its
  approved semantics and R7 schedule; this plan replaces only its separator, at the post-R9 cutover, and restores
  none of today's document-local alias semantics. To keep the cutover a token change, R7's alias parser should keep
  the separator one token test and add no `=>`-specific layout or token classification; anything it adds anyway
  becomes an AS2 deletion (§3.3). PLAN records this request for R7.

## 2. Language changes

### 2.1. Forms

| Existing form | Replacement | Meaning (unchanged) |
| --- | --- | --- |
| `external => internal: T` | `external as internal: T` | Callers write `external`; the declaration binds `internal`. |
| `alias A => B` | `alias A as B` | `A` is the introduced name, `B` its target; the direction is not reversed. |
| `name: T`, `alias Path` | Unchanged | One name for both roles; an opening alias. |

One reading covers both forms, and intake states it in §7.2.1 and §18.2.1: the left Name is what users of the
declaration write, and the right side is what it stands for, a parameter's internal binding or an alias target. For
parameters this matches the import convention, where the right-hand name is the local one; for aliases it does not
(§8).

### 2.2. Grammar

All referenced productions and context restrictions remain.

```ebnf
ParameterName    := Name ("as" Name)?
PlainParameter   := ParameterName ":" Type | ReceiverShorthand   // RequirementParameter and SpecializationParameter
AliasDeclaration := "alias" (Name "as")? ContainerReference OriginClauses?
```

The requirement and specialization productions are already identical; intake merges them as `PlainParameter`, a
change independent of `as`. Under the R7 alias contract the last production is instead
`NamedAliasDeclaration := Access? "alias" Name "as" AliasTarget OriginClauses?`, with `AliasTarget` and
`OpeningAlias` unchanged: successive contracts, not two parsers or modes.

### 2.3. Rules

- **Keyword.** `as` stays reserved ([F.3.1](../../docs/spec/appendices/F-syntax-summary.md#f31-reserved-words)),
  assigned to these two declaration forms only: never a cast or an expression, Pattern, Type or continuation
  operator. Recognition uses tokens alone; `aspect` is an ordinary Name.
- **A parameter clause renames.** Its two Names differ: `x as x` is an error; write `x`. A clause is then written
  exactly when the names differ, so the existing two-name representation is exact: writers emit the clause as
  written, and parse, write and parse again yields the same structure. The rule is the parameter's only: a
  same-name alias such as `alias G as G` keeps its meaning under the alias contract, distinct from `alias G`.
- **Receivers.** [§7.3.1](../../docs/spec/07-functions-and-callable-values.md#731-instance-functions-and-receiver-types)
  forbids renaming the receiver; with the rule above every receiver clause (`this as self`, `self as self`) is an
  error, without a receiver-specific clause test.
- **Anonymous parameters.** F.9's `AnonymousParameter` admits no clause; with the rule above the parser's existing
  name comparison rejects each one. The baseline parser accepts `func (x => x)`, which F.9 does not admit (source
  observation, PLAN G173); the cutover ends it, with that case as AS2's regression.
- **Contexts.** Named functions (overrides included), constructors, Contract function requirements, foreign
  declarations and explicit specialization headers, wherever their grammar admits `ParameterName`. Function Types
  and calls through function values gain no names or defaults.
- **Unchanged.** The `!` boundary, external-name uniqueness, internal-name scope, task slot, argument order, `K`,
  defaults evaluated in the declaration's internal names, specialization inheritance, and the requirement and
  foreign restrictions. Renaming works on either side of `!` and alone requires no label. Alias legality, identity,
  transparency, access, placement, resolution and Origins follow the applicable alias contract.
- **Errors.** A missing Name, a chained `as` or a forbidden clause is a located Language syntax error.

```kimi
func scaled(value: i32 ! by as factor: i32 = 2) -> i32 => value * factor
func flexible(by as factor: i32) -> i32 => factor
func options(! count as n: i32 = 2, limit: i32 = n) -> i32 => limit
func same(x as x: i32) -> i32 => x       // Error: the clause does not rename; write x: i32.

scaled(3, by: 4)                         // Valid: 12.
scaled(3)                                // Valid: the default is 2.
scaled(3, 4)                             // Error: by requires its name.
scaled(3, factor: 4)                     // Error: factor is the internal name.
flexible(4)                              // Valid: renaming alone requires no label.
let twice = func (x as y: i32) => y * 2  // Error: an anonymous parameter has no clause.

struct Meter
    var measured: i32 = 0
    func read(self) -> i32 => self.measured
    // func peek(this as self: ref/Self) -> i32 => 0   // Error: the receiver cannot be renamed.
```

```kimi
alias Output as ::Kimi.Console           // Output names the target; Console is not introduced.
```

### 2.4. Layout and the former spelling

- **Layout.** `as` adds no layout, continuation or Body rule. Inside parameter parentheses, breaks around it follow
  the delimiter rule; outside delimiters a line end continues nothing
  ([F.4.3](../../docs/spec/appendices/F-syntax-summary.md#f43-delimiters-and-continuation)), so a named-alias prefix
  and the start of its target share one effective line.

  ```kimi
  func identity(
      value as
      item: i32
  ) -> i32 => item          // Valid: inside the parameter delimiter.

  alias Output as
      Kimi.Console          // Error: the line end does not continue the alias.
  ```

- **Body-only `=>`.** After the cutover a valid `=>` only introduces a single-item Body, with its existing same-line,
  header-baseline, nested-body and delimiter-region rules. No flag, version-selected parser or retry accepts the
  naming arrow.
- **Former spelling.** Where `as` belongs, after a parameter's external Name or a named alias's Name, `=>` is never
  valid. The parser reports the arrow once as a misplaced token, reads the clause as written, and the parameter or
  alias rests on that Error, as `from` → `during` does for its annotation. Uses of the names and later items report
  nothing further and the same-name rule adds no second report; independent clause errors (anonymous, receiver)
  remain. The form is never accepted.
  - The report offers a `Repair.ReplaceToken` candidate to `as` only where the parser can decide that the clause is
    then valid: always for an alias, and for a parameter in a named list whose two Names differ and whose internal
    Name is not `self`. Otherwise (`x => x`, an anonymous clause, a possible receiver) it offers none.
  - Both owners report through the existing `TokenReader.Unexpected` with one shared `SyntaxForm` row, adding
    `ReplaceToken`'s candidate when their own check finds the clause valid, as the `&&` report does.
  - The recovery is permanent, like `from`, unless the owner decides otherwise.

## 3. Implementation design

### 3.1. Cause and sites

Required meaning: naming clauses use `as`, and `=>` only introduces a Body → the shared token makes the tokenizer
classify a line-ending `=>` by searching back for `alias` → the parameter and alias parsers own their clauses, and
the tokenizer keeps only Body layout → the back-scan becomes unnecessary. Separately, once `as` has a declaration
meaning, the dormant expression-`as` rows contradict F.3.1. Binding's names, targets and representation are
unchanged.

Inspection baseline: `ee0cfa4795913cd647bd187152599ff32d02a027` (source identical to `e76e2f20`). These are source
observations, not executed reproducers. R9 can move the sites; AS0 finds their owners through
[CODEMAP](../../docs/dev/CODEMAP.md). Paths are relative to `src/Kimi/`, except the editor row.

| Area | Site | Work |
| --- | --- | --- |
| Parameter clause | `Compiler/Parsing/Parser.cs`: `TryParseParameter` | Consume `TokenKind.As`; add the same-name check and the former-spelling report (§2.4). Receiver-shorthand, anonymous and duplicate-name checks are unchanged. |
| Alias clause | `Compiler/Parsing/Koto/Declarations/DeclarationContainers/GroupKoto.cs`: `ParseRootItem` (R7's parser after R7) | Two-token lookahead on `as`; the former-spelling report. |
| Writers | `FunctionKoto`'s parameter writer, `AliasKoto.WriteTo` | Write ` as `. Hover (`HoverBuilder.Headers`) and documentation output use these writers: verify only. |
| Layout | `Compiler/Lexing/Tokenizer.cs`: `PreviousLineStartsBody` | Delete the backward `TokenKind.Alias` search; keep the header and delimiter scan and Body recovery. |
| Token | `TokenKind.As`, its `TokenHelper` rows, `Constants.AsKeyword` | Reuse; no new token kind or keyword table. |
| Dead expression `as` | `KotoKind.As`, its `KotoHelper.BinaryOperator` row, `BinaryKoto`'s text row, `Binding.BindBinary`'s `As` test | Producer-free: the infix binding-power table has no `As` row. Delete after a census of serialized and table uses of `KotoKind`; keep the token. |
| Diagnostics | `Compiler/Parsing/SyntaxForm.cs` with its `DiagnosticRequirement.tinyhand` phrases | Two rows: misplaced naming arrow, non-renaming clause (names fixed at AS0). No new DiagnosticCode or repair kind; the closed repair kinds have no deletion, so the same-name error offers none. |
| Unchanged | `Parser.ParseOriginBraces`, `ParseTypeOrigin`; Body consumers | Their `=>` recovers a rejected Origin form, not a naming clause; never turned into `as`. |
| Editor | `src/kimi-ext/syntaxes/kimi.tmLanguage.json` | Alias lookahead `(?=\s+as\b)`; move `as` out of the expression-operator group with `is` into a naming keyword scope ([extension AGENTS](../../src/kimi-ext/AGENTS.md)). |

The dead expression rows do not depend on this revision; the owner may give them to R9's front-end cleanup, and AS2
deletes whatever remains.

### 3.2. Design and alternatives

Selected: change the token in the two existing owners, keep the two-name and alias-target representations, add the
same-name rule and the recovery through the existing report and repair paths, and delete the back-scan and the dead
rows. No new AST kind, HIR or MIR operation, parameter mode, side table, flag or Binding responsibility; the only
vocabulary additions are the two `SyntaxForm` rows, recorded in [DECISIONS](../../docs/dev/DECISIONS.md) by the
commit that implements them.

| Alternative | Decision |
| --- | --- |
| Owner-local token change with the deletions above | Selected: removes the shared cause with the least structure. |
| A shared naming-clause node or parser, or a layout record classifying each arrow | Rejected: parameters bind two names, aliases a name to a target; the reserved token already separates them, and shared structure would join two contracts and add state. |
| Accept both spellings, or move the alias exception to another phase | Rejected: keeps two canonical forms and the ambiguity. |
| Accept `x as x` and write `x` (the earlier text) | Rejected: two spellings of one meaning, a writer that changes source, and receiver- and anonymous-specific clause tests. |
| Generic syntax errors for the former arrow, without recovery | Rejected: the skipped parameter or alias makes every use of its names fail again. |
| Change the alias separator in R7, where its parser is rewritten | Not applied: conflicts with the owner's gate; §1 keeps R7 from adding arrow-specific structure instead. |

### 3.3. Deletions, size and speed

- **Deleted:** the alias back-scan, the four dead expression rows and any arrow-specific alias structure from R7.
  If R9 already removed one, AS0 records that instead of recreating it.
- **Searched:** every `=>` consumer, writer, recovery path and active sample; Body handling and its recovery stay. No
  generic parser framework for two token changes.
- **Added:** two token tests replacing the old ones, one name comparison, one former-spelling report in each owner
  through the existing `Unexpected` and `ReplaceToken`, and two `SyntaxForm` rows.
- **Effect:** no extra syntax storage, allocation or runtime work. The removed scan is bounded and rare, so no
  measurable speed change is expected or claimed. AS3 records the actual net size by area (DECISIONS Q5).

## 4. Units after R9

All units follow [AGENTS](../../AGENTS.md), the post-R9 architecture and
[VERIFICATION](../../docs/dev/VERIFICATION.md) on the completed R9 tree, never retired Analysis/Emission or
pre-consolidation Binding owners.

| Unit | Work and exit condition |
| --- | --- |
| AS0 — Baseline and design gate | Record R9's completion commit and this proposal's Git blob (Verify excludes `draft/`). Reproduce old and new acceptance, `func (x => x)`, same-name and layout cases. Census producers and consumers, including R7's alias parser. Rebase §3.1 onto the current owners; review the design (review weight below). |
| AS1 — Structure, only if needed | Behavior-preserving restructuring with diagnostic, semantic and IR comparisons ([VERIFICATION](../../docs/dev/VERIFICATION.md#behavior-preserving-commits)); omitted when empty. |
| AS2 — Atomic cutover and deletion | Both forms, writers, diagnostics, recovery, editor grammar and every active input, with §3.3's deletions. Both old spellings are rejected; no accepted form depends on a later consumer. |
| AS3 — Completion | §5's evidence and final review; area sizes, hotspots, duplication candidates, deletions and remaining uncertainty; only the affected STATUS and CODEMAP facts. |

- **Review weight.** The change spans Lexing, Parsing, Binding (the dead row) and services: the full process. The
  original design passed an independent comparison and review; this revision's same-name rule and recovery passed
  only this revision's review. Once the owner approves the design, AS0 records it in DECISIONS, and AS1–AS2 take
  DECISIONS Q6 form 1 (the rebased design and one review) and may share one group review (form 2) before merging. A
  departure beyond §3.1, such as a new representation, component or vocabulary entry, takes the full process.
- **Migration by span.** Edit identified syntax spans, never every textual `=>`. A disposable probe of the baseline
  compiler, outside the tree, lists each parameter-clause and named-alias arrow; Markdown samples are edited from a
  reviewed search; one reviewed manifest lists every edit. Keep body arrows, strings, comments and frozen historical
  documents; negative legacy cases become former-spelling tests.
- **Migrated together.** Library declarations, canonical generated source, test inputs, source-keyed test data (such
  as `tests/diagnostics/*.json` anchors), executable documentation samples, fixtures and milestone sources. A row
  keyed by source text is renamed one-to-one, never dropped or added. Semantic expectations stay fixed; changed
  syntax expectations cite this revision. No permanent migration tool ships.
- **Positions kept.** `=>` and `as` have the same length, so migration keeps every source position when each naming
  arrow is space-separated and no parameter clause repeats its name; a search finds no exception in active inputs.
  Same-name aliases (`alias A => A`) migrate to `alias A as A`.
- **Delivery.** Each unit is verified, associated with `scripts/verify-commit.ps1` and pushed without force; a
  Session covering the last unit's checks on identical inputs replaces its Unit run; no edits run during checks.

## 5. Verification and performance

This revision is documentation only: diff, reference and independent review; no build or compiler run. The
implementation uses the existing owning tests and helpers at the lowest public layer.

| Rule or risk | Required evidence |
| --- | --- |
| Naming | Both sides of `!`; defaults present and absent; duplicate external and internal names; ordinary, constructor, override, requirement, foreign and specialization contexts; inherited `K`. |
| Clause rules | `x as x` in every parameter context; `alias G as G` kept; receivers (`this as self`, `self as self`, shorthand `self`); anonymous clauses, including `func (x => x)` before and after; missing or chained `as`; `aspect`; no expression `as`. |
| Former spelling | One located error at both positions; no further report for later uses or an independent next item; a candidate's repaired source is accepted, and `x => x`, an anonymous clause and `this => self` get none; a line-ending `=>` after the back-scan's removal. |
| Preserved semantics | Named and positional calls, defaults using internal names, argument evaluation order, function-value calls, alias identity, access and lookup. |
| Layout | Single-line and multiline lists; breaks around `as`; comments, CRLF and LF; nested delimited Types and default bodies; `task;` and `!`; alias line breaks; the existing Body-arrow, Origin-recovery and indentation regressions. |
| Source and services | Parse, write and parse yields the same structure, with a clause exactly where written; writers and Hover show `as`; published spans of both names and the alias target; rebinding after edits; a repair names its snapshot and verified preconditions and is rechecked after application. |
| Consumers | No producer needs the deleted `KotoKind.As`; the editor grammar and source-producing tools agree. |

Representative suites at the baseline: `ParameterNameDefaultTest`, `ParameterSyntaxDiagnosticTest`, `NamedAliasTest`,
`HeaderDelimiterContinuationTest`, `LayoutRecoveryTest`, `LexicalLayoutRecoveryTest`, `ParserRegressionTest`,
`ParserOptimizationTest`, `HoverSyntaxTest` and `HoverRenderingTest`; use their post-R9 equivalents. Library
migration adds `KimiLibraryShapeTest`; editor changes use the extension's workflow checks, including the alias case
in `src/kimi-ext/src/test/unit/grammar.test.ts`.

- **Equivalence.** Since migration keeps positions, baseline and migrated sources must give identical IR and MIR
  dumps and diagnostics, positions included, except the intended former-spelling cases. This shows unchanged O2
  generated code without broad snapshot normalization; native fixtures run only for an input whose IR differs.
- **Performance.** The existing `ParseBenchmark`, `FrontEndBenchmark` and `TokenReaderBenchmark`, alternating
  baseline and candidate under fixed conditions; the existing `Purpose=Allocation` regressions unchanged. Their
  sources hold no parameter clause or named alias, so they guard the unchanged paths; the changed paths are one
  token test each, and no speed change is claimed. No new benchmark, extra warm-up or relaxed baseline; failed
  evidence is kept.
- **Completion.** `scripts/verify.ps1 -Mode Session` with explicitly selected native checks; NativeAOT only on
  explicit instruction. A failed required check leaves the work incomplete.

## 6. Integration map

Formal intake, when explicitly requested, updates the authoritative definitions together.

| Proposal scope | Specification targets |
| --- | --- |
| `as` and the naming productions | F.3.1 (`as` into "Bindings and functions"; the emptied "Reserved for future use" row goes); F.6.1, F.6.2, F.6.3 (`PlainParameter`); §7.2.1 (§2.1's reading, the parameter same-name rule), §7.2.2's example, §7.3.1, §7.7.1, §8.13.2, §10.1.1's example, §15.7's examples, §18.2.1 or its R7 successor, §22.1's example. |
| Body-only `=>` | §1.2's notation table (the `=>` row loses its naming uses); F.4, F.6.1's alias note, F.10's Body production; §2.1 and other affected examples. |
| Diagnostics and services | The two syntax rows and the repair in [DIAGNOSTICS](../../docs/dev/DIAGNOSTICS.md) when implemented; existing §23 and IMPL source-writing contracts, with no schema or capability change. |

- **With the cutover:** [LIBRARY](../../docs/LIBRARY.md)'s two declarations and invalidated usage instructions;
  [STYLE](../../docs/STYLE.md)'s `[Advice]` on `external => internal` under its explicit revision scope.
- **Explicit only:** [GUIDE](../../docs/GUIDE.md) and [SPECJ](../../docs/SPECJ.md) keep the old spelling until an
  instructed update; since they would then teach a rejected form, the cutover instruction should include them.
- **History:** the 2026-10-16 plan is not rewritten; this plan and its register entry record the supersession.

## 7. Principles

| Principle | Application |
| --- | --- |
| One Concept, One Canonical Form | One naming separator, and one spelling per parameter meaning (`x`, never `x as x`); a valid `=>` only introduces a Body. Parameter and alias contracts keep their own owners. |
| Local Reasoning | Token position decides each form, with no lookup, inference, back-scan or retry; the parameter clause rules need no context. |
| Explicit Semantics | Both names and the alias direction are written, with one reading for both forms that intake writes into SPEC (§2.1); `!`, defaults, Types, ownership and target legality stay independent. |
| Compiler Server Protocol | A clause is written exactly where it was; names, spans and target identity are preserved; the former spelling gets one located error and a repair only where the repaired clause is valid. Syntax alone adds no service capability. |

## 8. Revision notes

| Change | Reason |
| --- | --- |
| `x as x` is a parameter error (§2.3) | The earlier text accepted it but rejected `self as self` and every anonymous clause, which needed clause-presence tests the representation lacks, and its writer dropped the clause. One rule gives one spelling, a writer that keeps clauses as written and context-free checks, and ends the baseline's acceptance of `func (x => x)` (PLAN G173). Same-name aliases keep their meaning. |
| Defined former-spelling recovery (§2.4) | The earlier text both allowed legacy recognition and forbade an old-syntax path. Without recovery, every use of a skipped name fails again; the existing `Unexpected` and `ReplaceToken` path gives one error for the arrow, with a candidate only where it yields a valid clause. |
| Site corrections (§3.1) | Hover has no spelling of its own; the dead expression `as` is confirmed producer-free and unrelated to the arrow's layout cause; the alias node records no Name span, so §5 requires only published spans. |
| Coverage (§4–§6) | Added §10.1.1, §15.7, §22.1, F.6.2, F.3.1's row, LIBRARY, STYLE, GUIDE, SPECJ, source-keyed test data and the editor's unit test. |
| Shorter layout rules (§2.4) | Rules restating F.2 and F.4 removed. |
| Cheaper, stronger evidence (§5) | Position-preserving migration allows identical IR, MIR and diagnostics; existing benchmarks replace new workloads. |
| Process (§1, §3.1, §4) | DECISIONS Q6 after owner approval; R7 adds no arrow-specific structure (recorded in PLAN); dead rows may go to R9. |

Owner questions:

- **Revision rules.** Approve or reject §2.3's parameter same-name rule and §2.4's former-spelling recovery; both
  extend the adopted spelling change.
- **Alias direction.** In Python, Rust and TypeScript imports, `X as Y` introduces `Y`. Parameters follow that
  convention (the body binds the right-hand name), but `alias A as B` introduces `A`. §2.1's reading makes both
  forms agree and keeps declarations name-first, yet readers, human or AI, bring the import convention. The
  alternatives are `alias B as A` (name last) and `alias A = B` (as in Rust, Swift and TypeScript type aliases; `=`
  gains a declaration use). The adopted form stands unless the owner decides otherwise.
- **Scheduling.** Whether the dead expression rows go to R9's front-end cleanup, and whether the cutover instruction
  includes STYLE, GUIDE and SPECJ.
