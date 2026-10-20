# As Naming Syntax and Implementation Plan

Specification change and implementation plan — 2026-10-20.

## 1. Authority, scope and start gate

The owner adopts `external as internal` for parameter names and `alias A as B` for named aliases.
For the changes stated here, this plan takes precedence over [SPEC](../../docs/SPEC.md) and its referenced
documents; existing specifications govern everything else. This planning task changes no current specification
or executable source and claims no implementation support.

**Implementation starts only after R9 is complete**, as recorded in the
[compiler stage ledger](../../docs/dev/COMPILER_ARCHITECTURE.md#stage-ledger). Every implementation unit below,
including restructuring, executable probes, source migration and optimization, is behind that gate. Read-only
inspection, design and document review may precede it. Do not implement this change inside R9 or backport it
into an earlier stage.

The [Named Alias Declarations plan](2026-10-16%20Named%20Alias%20Declarations.md) retains its approved semantics
and R7 schedule. This plan supersedes only its named-alias separator spelling, effective at this plan's post-R9
cutover. It neither postpones that plan nor restores today's document-local alias semantics. Formal integration
into SPEC is a separate, explicitly instructed task; it is not performed by this proposal.

## 2. Language changes

### 2.1. One spelling for each naming form

| Existing form | Replacement | Meaning preserved |
| --- | --- | --- |
| `external => internal: T` | `external as internal: T` | The call uses `external`; the declaration binds `internal`. |
| `alias A => B` | `alias A as B` | `A` is the introduced name; `B` is its target. The direction is not reversed. |
| `name: T` | Unchanged | External and internal parameter names are the same. |
| `alias Path` | Unchanged | Open members under the applicable opening-alias contract. |

`as` remains a reserved keyword. It is already reserved by Appendix F.3.1; this revision assigns its two
declaration uses. It is not a cast, expression operator, Pattern operator, Type operator or continuation marker.
Recognition is lexical and syntactic, never dependent on lookup or inferred Types.

Replace the affected productions as follows; all referenced productions and context restrictions remain:

```ebnf
ParameterName           := Name ("as" Name)?
SpecializationParameter := ParameterName ":" Type | ReceiverShorthand
AliasDeclaration        := "alias" (Name "as")? ContainerReference OriginClauses?
```

The last production describes the current SPEC's alias contract. When the independently approved named-alias
plan applies, its production instead becomes
`NamedAliasDeclaration := Access? "alias" Name "as" AliasTarget OriginClauses?`; its `AliasTarget` and
`OpeningAlias` rules are unchanged. These are successive semantic contracts, not two parsers or language modes.

The replacement covers named functions, constructors, Contract function requirements, foreign declarations
and explicit specialization headers wherever their existing grammar admits a parameter name pair. It
introduces neither names nor defaults into common Function Types or calls through function values.
Anonymous parameters admit no `as` clause. Receiver parameters also admit no `as` clause, including
`self as self`: the receiver keeps its one existing name and shorthand. This makes the receiver's prohibition
explicit rather than testing whether two written names happen to differ.

Ordinary parameters may explicitly repeat the same name (`x as x: T`), preserving the existing name-pair
semantics; canonical source writing emits `x: T`. A missing name, chained `as`, or forbidden clause is a
located Language syntax error. An identifier such as `aspect` remains an ordinary Name.

### 2.2. Independent contracts

The `!` boundary, external-name uniqueness, internal-name scope, receiver treatment, task slot, argument order
and default rules stay unchanged. In particular:

- Renaming works on either side of `!`; it does not require a named call.
- `!` requires the external name when an argument is supplied; only a default permits omission.
- The ordered external names and positional-prefix count `K` keep their existing meaning.
- Specializations inherit the name contract and write no boundary or defaults. Contract requirements and
  foreign declarations retain their own existing restrictions.
- Default evaluation uses the declaration's internal names, scope and evaluation order.
- Alias target legality, identity, transparency, access, placement, resolution and Origin obligations are
  those of the applicable alias contract. This syntax revision adds no alias capability.

```kimi
func scaled(value: i32 ! by as factor: i32 = 2) -> i32 => value * factor
func flexible(by as factor: i32) -> i32 => factor
func options(! count as n: i32 = 2, limit: i32 = n) -> i32 => limit

scaled(3, by: 4)          // Valid: 12.
scaled(3)                // Valid: the default is 2.
scaled(3, 4)             // Error: by requires its name.
scaled(3, factor: 4)      // Error: factor is the internal name.
flexible(4)              // Valid: renaming alone requires no label.
flexible(by: 4)          // Valid.
```

```kimi
alias Output as ::Kimi.Console
// Output names the target; the declaration does not introduce Console as a local name.
```

The alias fragment belongs before other source items under today's SPEC.

### 2.3. Layout and obsolete spelling

`as` participates only in ordinary token separation and delimiter continuation. It neither starts a Body nor
creates an indentation level. Whitespace or a comment must separate it from an adjacent Name.

```kimi
func scaled(
    value: i32
    ! by as factor: i32 = 2
) -> i32 => value * factor

func identity(
    value as
    item: i32
) -> i32 => item

alias Output as
    Kimi.Console         // Error: as does not continue a line outside a delimiter.
```

Inside parameter parentheses, line breaks around `as` follow the ordinary delimiter rule, including comments
and blank lines. Outside delimiters, a named-alias prefix and the start of its target stay on one effective
line; a target's own delimited content and attached Origin clauses keep their existing continuation rules.
A leading `as` line adds no continuation form.

After the cutover, parameter and alias `=>` spellings are errors, not deprecated alternatives. There is no
compatibility flag, version-selected parser or try-old-then-new path. Valid `=>` syntax is then the existing
single-item Body form, including its use in branches, match arms and declarations; its same-line requirement,
header baseline, nested-body and delimiter-region rules remain unchanged. Rejected legacy syntax may still
be recognized for diagnostic recovery, which must never make it valid.

## 3. Evidence and implementation design

### 3.1. Cause and affected owners

Inspection baseline: `e76e2f20be26f5d3707fe4125a0329b6702d8423`. These are source observations, not executed
reproducers or measured speedups. The current compiler implements the old spelling deliberately; this is a
language revision, not a claim that accepting it is a compiler defect. R9 can move or remove these sites:
locate their then-current owners through [CODEMAP](../../docs/dev/CODEMAP.md) before coding.

Required meaning → naming clauses use `as` while `=>` introduces a Body → the shared arrow currently makes
layout distinguish an alias target from a Body → the parameter/alias parser owns the naming clause and the
existing layout owner handles bodies → the alias-specific layout scan and dormant expression-`as` path become
unnecessary, without changing Binding's name or target representation.

Paths below are relative to `src/Kimi/`, except the extension row.

| Site at the inspection baseline | Work or deletion obligation |
| --- | --- |
| `Compiler/Parsing/Parser.cs`: `TryParseParameter`; `Compiler/Parsing/Koto/Declarations/DeclarationContainers/GroupKoto.cs`: `ParseRootItem` | Replace the naming token in every admitting and rejecting context. Follow the post-R9 canonical parser; consolidate surviving duplicate recognition only where the contract is identical. |
| `Compiler/Lexing/Tokenizer.cs`: `PreviousLineStartsBody` | Delete the backward search for `TokenKind.Alias` used to distinguish a trailing `=>` from a Body. Preserve ordinary header/delimiter scans and rejection/recovery of malformed aliases. |
| `Compiler/Lexing/TokenKind.cs`, `Compiler/Lexing/TokenHelper.cs`; `Misc/Constants.cs` | Reuse `TokenKind.As`, its keyword recognition and `Constants.AsKeyword`; add no token kind or keyword table. |
| `Compiler/Parsing/Koto/Koto.cs`, `Compiler/Parsing/Koto/Common/KotoHelper.Operators.cs`, `Compiler/Parsing/Koto/Expressions/BinaryKoto.cs`; `Compiler/Binding/Binding.Expressions.cs` | Audit and remove dormant expression `KotoKind.As`, its token mapping, text row and unsupported-binary branch if still producer-free, including a serialized-use census. The current infix-precedence table has no `As` entry. Retain the lexical token. |
| `Compiler/Parsing/Koto/Declarations/FunctionKoto.cs`, `Compiler/Parsing/Koto/Declarations/AliasKoto.cs`; `Checking/Hover/HoverBuilder.Headers.cs` and shared writers | Emit canonical `as`; preserve external/internal spans, target structure and identity. Consumers use the owner's writer, not a second formatting rule. |
| `Compiler/Parsing/Parser.cs`: `ParseOriginBraces`, `ParseTypeOrigin` | Preserve rejection of legacy Origin mappings: the current `=>` consumption is recovery, not valid grammar. Do not mechanically turn it into `as`. |
| `src/kimi-ext/syntaxes/kimi.tmLanguage.json` | Change named-alias recognition to `as` with word boundaries; classify it as a naming keyword rather than the current expression-operator grouping with `is`. Follow extension AGENTS when implementing. |

### 3.2. Alternatives and selected structure

| Alternative | Decision |
| --- | --- |
| Change tokens in the existing owners, retain their semantic records, and delete obsolete layout/expression paths | Selected. It removes the shared cause with the least structural change. |
| Introduce a shared naming-clause node/parser or classify every arrow with an extra layout record | Rejected. Parameters bind two names; aliases bind a name to a target. A shared representation adds state and joins different contracts when a reserved token already distinguishes them. |
| Accept both spellings or move the alias exception to another phase | Rejected. It preserves the ambiguity, duplicate canonical forms and cleanup obligation. |

No new AST kind, HIR/MIR operation, parameter mode, side table or Binding responsibility is needed. An `as`
clause is syntax consumed by its existing owner. Keep invalid syntax out of semantic acceptance; record its
typed cause at the failing check and render wording through the diagnostics adapter.

Reduction is part of completion, not a later optional cleanup. The identified layout loop and dead expression
rows must be removed if they survive R9; if R9 already removed them, record that fact instead of recreating
them. Search all arrow consumers, source writers, recovery paths and active syntax samples. Do not delete
body recovery merely because it contains `=>`, or create a generic parser framework for two token changes.

Expected effect: fewer special-case branches and one fewer alias-dependent backward scan; no extra syntax
storage or runtime work. A token replacement alone saves no code, and removing one scan does not establish
a better whole-parser complexity class. Net size and speed remain unmeasured until the post-R9 baseline.

## 4. Units after R9

All units follow [AGENTS](../../AGENTS.md), the post-R9 architecture and
[VERIFICATION](../../docs/dev/VERIFICATION.md). Rebase this plan's site census onto the completed R9 tree;
do not target retired Analysis/Emission or Binding owners.

| Unit | Work and exit condition |
| --- | --- |
| AS0 — Baseline and design gate | Confirm R9 completion and record its commit. Reproduce old/new acceptance and malformed-layout cases; census all producers/consumers, including R7 aliases. Compare independent owner-local and shared-structure designs and review the selection before coding. Record exact sources, configuration and this proposal's blob identity. |
| AS1 — Structure, only if needed | If the final owners need restructuring, commit it with behavior unchanged and the required diagnostic, semantic and generated-code comparisons. Omit an empty restructuring unit. New representations or responsibility changes require the full design/approval process. |
| AS2 — Atomic syntax cutover and deletion | Change both naming forms together, their writers, diagnostics, extension grammar and active executable inputs. Remove the obsolete layout exception and proven-dead expression paths in this unit. Retain existing contracts and reject both old spellings. No accepted form may depend on a consumer scheduled for later. |
| AS3 — Verification and completion | Complete the matrix below, matched measurements and final independent review. Record area sizes, hotspots, duplication candidates, deletions and remaining uncertainty. Update only affected support/navigation facts and close the unit after required checks and commit association pass. |

Each committed unit is verified and pushed without force. A Session covering a last unit's checks on identical
inputs replaces its Unit run. No source edits run concurrently with checks. For implementation evidence,
record the proposal's Git blob separately: Verify excludes `draft/`, even though this plan governs this revision.

Migration edits identified syntax spans, not every textual `=>`. Keep body arrows, strings, comments,
negative legacy cases and frozen historical documents intact. Canonical generated source, library declarations,
test inputs, executable documentation samples, fixtures and milestone sources change together where they use
the replaced forms. Their semantic expectations stay fixed; changed syntax expectations cite this revision.
Do not ship a permanent old-syntax parser or migration service just for this cutover.

## 5. Verification and performance

This document-only task requires diff, reference and independent design review, with no build or compiler run.
The implementation uses existing owning tests and helpers at the lowest public layer:

| Rule or risk | Required evidence |
| --- | --- |
| Naming and applicability | Both sides of `!`; same and different names; defaults present/absent; duplicate external/internal names; ordinary, constructor, requirement, foreign and specialization contexts; inherited `K`; rejection for receivers and anonymous parameters, including equal written names. |
| Preserved semantics | Named/positional calls, parameter-order defaults referring to internal names, argument evaluation order, function-value calls, alias target identity and applicable access/lookup rules. |
| Layout and recovery | Single/multiline parameter lists; breaks before/after `as`; comments, CRLF/LF and nested delimited Types/default bodies; `task;` and `!`; rejected alias line breaks and old arrows; a following independent item still checked. Keep body-arrow, Origin-recovery and ordinary indentation regressions. |
| Source and services | Parse/write/parse preserves the semantic contract; writers and Hover show `as`; spans identify both names and the alias target; edits/rebinding invalidate the right source. Any offered migration edit names its snapshot and verified preconditions, and is rechecked after application. |
| Vocabulary and consumers | No expression `as` is accepted; `aspect` remains a Name; no surviving producer requires deleted `KotoKind.As`; extension recognition and source-producing tools agree. |
| Size and performance | Matched tokenization, parsing and source-writing workloads, with short/long signatures, aliases, nested defaults and unchanged body-heavy controls. Measure time, allocations and retained memory; record actual net lines/tokens by affected area and the removed scans/branches. |

Representative current suites: `ParameterNameDefaultTest`, `ParameterSyntaxDiagnosticTest`, `NamedAliasTest`,
`HeaderDelimiterContinuationTest`, `LayoutRecoveryTest`, `ParserRegressionTest`, `ParserOptimizationTest`
and Hover syntax/writer tests. Select their post-R9 equivalents; library source migration adds
`KimiLibraryShapeTest`, and extension changes use its own workflow checks.

Use semantically equivalent old-spelling baseline and new-spelling candidate sources under fixed conditions.
Retain original sources and a reviewed edit manifest; distinguish intended text/span differences from
semantic differences rather than broadly normalizing snapshots. Timing-only work belongs in `src/Benchmark`;
allocation/reuse regressions keep `Purpose=Allocation`. Preserve failed evidence and existing native allocation
assertions; no extra warm-up, relaxed baseline or unmeasured speedup claim.

Finish with `scripts/verify.ps1 -Mode Session` and explicitly selected existing native checks that exercise the
affected calls/defaults and aliases; compare O2 generated behavior and allocations with the baseline.
NativeAOT requires a separate explicit instruction. Associate successful implementation evidence using
`scripts/verify-commit.ps1` before pushing. If required checks fail, implementation remains incomplete.

## 6. Integration map and Principles

Formal intake, when explicitly requested, updates the authoritative definitions together:

| Proposal scope | Specification targets |
| --- | --- |
| Reserved `as` and naming productions | Appendix F.3.1, F.6.1, F.6.2, F.6.3; §7.2.1–2, §7.3.1, §7.7.1, §8.13.2 and §18.2.1 under the then-applicable alias contract. |
| Body-only valid `=>` and unchanged continuation | §1.2's notation table; Appendix F.4, F.6's alias note, F.10 body production; §2.1 references and affected examples. |
| Source/service expectations | Existing §23 and IMPL source-writing contracts; change no schema or service capability without an actual contract need. |

Update active authoritative examples and implementation inputs consistently at their authorized intake/cutover.
STYLE's parameter spelling and other directly invalidated usage instructions need corresponding edits then.
GUIDE/SPECJ synchronization, policy redesign and historical maintenance retain their explicit-scope rules.
The older named-alias proposal is not rewritten by this task; this plan and its register entry identify the
spelling supersession.

| Principle | Application |
| --- | --- |
| One Concept, One Canonical Form | One naming separator in each affected form, no permanent old spelling; valid `=>` denotes a Body. The distinct parameter and alias contracts retain their own owners. |
| Local Reasoning | Token position determines the form; no lookup, inference, alias scan or compatibility retry distinguishes naming from a Body. |
| Explicit Semantics | External/internal roles and alias direction remain written; `!`, defaults, Types, ownership and target legality remain independent. |
| Compiler Server Protocol | Preserve structured names, target identity and source spans; expose canonical source and snapshot-bound, rechecked edits through existing contracts. Syntax alone promises no new service capability. |
