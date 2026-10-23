# Accessor Result Contracts and Implementation Plan

Specification change and implementation plan — 2026-10-20; revised the same day against the Principles (§10).

## 1. Authority, scope and start gate

- **Decision.** The owner adopts result-free getter and setter signatures: the Property supplies the getter result,
  and every setter returns Unit. For the changes stated here, this plan takes precedence over
  [SPEC](../../docs/SPEC.md) and its referenced documents; existing specifications govern everything else. This
  revision also proposes one rule for owner approval: only stored Properties take Property-level Origin clauses
  (§3.3). This plan changes no current specification or executable source and claims no support; formal intake is a
  separate, explicitly instructed task (§8).
- **Start gate.** Implementation starts only after R9 is complete, as recorded in the
  [compiler stage ledger](../../docs/dev/COMPILER_ARCHITECTURE.md#stage-ledger). Every unit in §6, including
  restructuring, executable probes, migration and optimization, is behind that gate; read-only inspection, design and
  document review may precede it. Nothing of this change goes into R9 or an earlier stage. Other approved post-R9
  changes keep their own scope and gates; this plan orders none of them.

## 2. Language changes

### 2.1. Grammar

Appendix F.7 changes these productions; all others, including `StoredAccessor`, `CustomAccessor`,
`AccessorReceiver` and `RequiredSignature`, remain.

```ebnf
GetterSignature     := "get" "(" AccessorReceiver? ")"
SetterSignature     := "set" "(" (AccessorReceiver ",")? "value" ":" Type ")"
ComputedDeclaration := Access? "computed" Name ":" Type IndentedList<CustomAccessor>   // §3.3 (proposed)
```

- A computed or required Property still writes its header `: T`, and an explicit setter still writes `value: U`.
  Bare `get` and `set` remain standard stored access.
- `get() -> T` and `set(value: U) -> ()` are syntax errors, even when the annotation agrees with the contract (§5.5).
  There is no compatibility spelling, flag or second parser.
- F.4.3: a leading `->` line continues only a function header. Arrows inside Function Types stay valid, as in
  `set(value: (i32) -> i32)`.

### 2.2. Result contracts

| Property | Getter result | Setter input | Setter result |
| --- | --- | --- | --- |
| Stored `let` / `var` | The complete storage Type `T`; a custom getter needs `T is Copy` proven | `var`: written `U` must equal `T`; `let` has no setter | Unit |
| `computed p: T` | The completed header `T` | Written `U`; may differ from `T` | Unit |
| Requirement `property p: T` | The completed header `T` | Written `U` (explicit form) or `T` (`has set`, §4) | Unit |

- No result is inferred from a body. §7.1's rule that a missing `->` means Unit applies to named functions only.
- Unchanged: storage Types and their inference (§11.6), receivers (§11.3.1), accessor access (§11.3.2), API
  accessibility of the Property Type, each receiver and each setter input (§9.4.4), standard access and witnesses
  (§11.2, §11.9.2), getter temporaries (§11.5), construction and destruction (§11.7) and bodies (§11.3, §14.2, §14.9):
  a getter's results must fit its result Type; a setter discards a single-item expression, `return 123` is an error
  and `try` is invalid (§17.3).

```kimi
struct Gauge
    public var level: i32 = 0
        get() => storage
        set(value: i32)
            storage = if value > 100 => 100 else => value

    public computed isEmpty: bool
        get() => self.level == 0

    public computed adjustedLevel: i32
        get() => self.level
        set(value: i64) => self.level = value@i32    // A checked conversion (§13.5.4).
```

## 3. Origins

### 3.1. One contract per operation

Getter and setter calls keep independent Origin binders; there is no Property-wide call binder. Each operation's
contract is completed once, at definition, before any body is checked (§15.4).

### 3.2. Computed and required headers

The header of a computed Property or a Contract requirement is the result position of its getter signature.

- Its annotations, binding sets and unbound simple names belong to the getter, with the receiver. Resolution does
  not depend on textual order, so the header may name a receiver Origin. An unbound simple name is quantified as in
  any named callable, also when it occurs only in the result (§15.3.4; SETTLED "Signature Origin names are
  introduced implicitly").
- The getter's names are visible in its receiver, header, clauses and body, never in the setter; the setter's names
  and `value` supply no facts to the getter. Enclosing Origins keep the ordinary lookup and no-hiding rules. Header
  `self` is the getter receiver; a static header has no `self`.
- Completion follows §15.4 with the header as the result (§15.4.2's Accessor row): an instance getter whose only
  direct borrowed input is its receiver obtains the receiver's Origin, a static getter uses the same result defaults
  (§15.4.3), and missing information is a declaration error.

### 3.3. Clause placement (proposed for owner approval)

Property-level `origin` clauses are storage clauses (§15.3.3): only a stored Property has them. A computed getter
writes its clauses in its body's leading region and a required accessor in its attached list, as functions do. Each
operation clause then has one place, and computed Properties match requirements, which F.7 already gives no
Property-level clauses. No active source writes such a clause on a computed Property or a requirement. Source
observation at the baseline (§5.1), not reproduced: the shared accessor-block loop (`Parser.ParsePropertyAccessorBlock`)
also attaches Property-level clauses to requirements, which F.7 forbids; the same rule closes it (PLAN G174).

```kimi
struct Window {source}
    let data: Slice<i32> during source

    // The header is the getter's result; self is the getter's receiver.
    public computed items: Slice<i32> during self.source
        get() => self.data

    // The same contract through a binding set; its clause leads the getter body.
    public computed view: Slice<i32>{result}
        get()
            origin result.source == self.source
            return self.data
```

### 3.4. Stored Properties

Unchanged (§11.6): the header and Property-level clauses are the storage contract, completed under §15.3.2 and §15.4.
A custom getter's result is the entire complete storage Type, never elided again against the receiver. A custom
setter's input inherits omitted Origins from storage before function elision; written bindings must agree, and no
accessor parameter narrows the calls storage allows. Accessor clauses cannot change the storage contract.

## 4. Contract requirements

```kimi
contract ReplaceableItem
    property item: ref/Resource
        get()
        set(value: Resource)

contract Counted
    property count: i32 has get         // The same operation as an explicit get().
```

- **Shorthand.** `has get` and `has set` denote `get()` and `set(value: T)`. Each occurrence of `T` is completed in
  its own operation's position, getter result or setter input; new names, binding sets and omitted slots are bound
  per operation, enclosing names are shared, and equal spelling does not identify per-call Origins. So
  `property p: ref/i32 has get, set` gives the getter a result during its receiver and the setter an input with its
  own Origin. A different input Type or operation-specific clauses need the explicit form.
- **Unchanged.** No storage, initializer, executable body, accessor access modifier, Attribute, default or `!`
  boundary; no Property-level clauses (§3.3). Conformance compares completed operation contracts under §11.9.1, and
  the standard bridges of §11.9.2 remain: a standard `var item: Resource` satisfies `ReplaceableItem` through the
  shared-borrow and standard-`set` bridges.

## 5. Implementation design

### 5.1. Cause

Required meaning: each operation's result has one source, the Property or Unit → accessor result syntax repeats it,
so the parser, Binding and services carry a second Type per getter and check its agreement → Binding, the owner of
declaration contracts, completes each operation once from the Property and publishes it → result syntax, its
binding, its agreement checks and its diagnostics become unnecessary.

Two baseline facts make the cutover mostly a deletion:

- **The result-free path exists.** Binding already completes a getter without result syntax: `has get`
  requirements, recovery and witnesses use it (`BindAccessorSignature` falls back to the Property Type,
  `Binding.ControlFlow.DeclaredResult` has a Property branch, `FunctionKoto.RefreshAccessor` gives witnesses no
  result syntax). The cutover makes it the only path.
- **One header, two binders.** `Binding.Origins.TypeContext` completes a computed or required header as its getter's
  result, but `Binding.OriginDeclarations.OriginOwner` gives the header's binding sets and clauses to the Property and
  discovers no Origin name there. Today the written result supplies those names; without it, a header-only name or
  set would fail. Both must answer "the getter". Source observation, not reproduced: a header-only name in a
  `has get` requirement may already fail this way (PLAN G174).

Inspection baseline: `ee0cfa4795913cd647bd187152599ff32d02a027` (source identical to `b4ac3c77`). These are source
observations, not executed reproducers. R7 (Binding consolidation) and R9 can move every site; AR0 finds the post-R9
owners through [CODEMAP](../../docs/dev/CODEMAP.md).

### 5.2. Sites

Paths are relative to `src/Kimi/` at the baseline, except inputs and the editor row.

| Area | Site | Work |
| --- | --- | --- |
| Syntax | `Compiler/Parsing/Parser.cs` (accessor loop of `ParsePropertyAccessorBlock`, `ParseAccessorReturnType`); `PropertyAccessorKoto` (`ReturnType`, its child slot and writer branch) | One signature form; the former arrow's report (§5.5); Property-level clauses only on stored Properties (§3.3). |
| Result readers | `ControlFlowContext.DiscardsFunctionBody`, `FunctionKoto.RefreshAccessor`, `Binding.ControlFlow.DeclaredResult` | Decide from the Property contract (its header or published result), as functions decide from theirs; no accessor result node. `Compiler/Analysis/ControlFlowAnalysis.cs` goes at R6-check; a surviving copy gets the same change. |
| Origin binder | `Binding.Origins.TypeContext`, `Binding.OriginDeclarations.OriginOwner` | One decision: a computed or required header belongs to its getter signature, a stored header to storage. The Property's own Origin declaration then serves only storage. |
| Contract | `Binding.Properties` (`BindPropertyHeader`, `BindAccessorSignature`, `ValidateAccessor`, `PropertySignatureOwner`) | Getter result = Property Type, setter result = Unit. Keep the stored setter-input check, Copy proof, receiver shape and access checks; access domains are another fact, and the header keeps the Property's. |
| Consumers | `Binding.PropertyCalls`, `PropertyWitnesses`, `PropertyMatching`; the MIR builder | Contracts unchanged: verify only. |
| Services | `Checking/Hover/HoverBuilder.Headers.cs` (visits `ReturnType`), documentation output, `SemanticQuery` | Drop the result visit; writers emit canonical source; Hover keeps showing completed operations ([HOVER](../../docs/dev/HOVER.md)). |
| Diagnostics | `SyntaxForm.AccessorResult`, `SetterResult` and their `Diagnostics/DiagnosticRequirement.tinyhand` rows; [DIAGNOSTICS](../../docs/dev/DIAGNOSTICS.md)' recovery list ("a setter with a result Type other than `()` keeps the Unit result") | §5.5. |
| Inputs | `Library/*.kimi` (9 annotations), `tests/milestones` (10), `tests/mir` (8), `tests/diagnostics/*.json` (26); 346 in 60 C# files: tests, `BindingBenchmark` and `VerificationWorkloads` | §6.2. |
| Not changed | `Binding.BindImplicitSetterInput` (`has set`) | Its temporary rebinding of the shared header serves only Origin-bearing setter inputs, which are `Unsupported_Kd`. This plan neither needs nor extends it; G79 records its removal. |
| Editor | `src/kimi-ext/syntaxes/kimi.tmLanguage.json` | Its accessor rule matches only the keyword and parameter list: verify, no change expected ([extension AGENTS](../../src/kimi-ext/AGENTS.md)). |

### 5.3. Design and alternatives

Selected: delete the written-result path and keep the existing result-free completion as the only one; give the
header one Origin binder; one parser rule for Property-level clauses. No new syntax node, HIR or MIR operation,
intrinsic, side table, mode or Binding responsibility. No phase adds, moves or rebinds syntax to stand for a result;
consumers read the published contract. Record the decision in [DECISIONS](../../docs/dev/DECISIONS.md) in AR0.

| Alternative | Decision |
| --- | --- |
| Remove result syntax; complete each operation from the Property | Selected: one source per result; the agreement path disappears. |
| Make the annotation optional | Rejected: two forms, and the agreement path survives. |
| A Property-wide call binder, a new result IR form or a side table | Rejected: the operations have distinct Origin contracts, and complete Types carry the facts. |
| Infer the result from the body, or move the Type from the header to the getter | Rejected: inference breaks local reasoning; standard access and witnesses read the header. |
| Keep Property-level clauses on computed Properties as a second getter region | Rejected (proposed, §3.3): two places for one clause. |
| Also omit a stored setter's `value` Type | Rejected: computed and required inputs may differ from `T`, so one setter form keeps the written input. |
| Synthesize a result node for syntax readers | Rejected: a second representation of one contract. |

### 5.4. Deletions, size and speed

- **Deleted:** `PropertyAccessorKoto.ReturnType` with its parameter, slot and writer branch; `ParseAccessorReturnType`
  and the missing-result `Expect`; the `SetterResult` form, row and branch; result binding in `BindAccessorSignature`;
  the getter-result and setter-Unit comparisons in `ValidateAccessor`; the `ReturnType` branches of `TypeContext`,
  `PropertySignatureOwner`, `DeclaredResult`, `DiscardsFunctionBody`, `RefreshAccessor` and Hover; the computed
  Property's own Origin declaration; tests of removed rules (§6.2). Every producer, consumer, writer and source-keyed
  form is searched first.
- **Added:** one former-arrow branch, one Property-level clause test and the getter as the header's Origin owner.
- **Effect:** a custom getter loses one Type node, one Type binding with Origin completion and one complete-Type
  comparison; a custom setter loses its Unit node and check. Contracts are identical, so generated code is unchanged.
  No speedup is claimed; AR3 records net size by area (DECISIONS Q5) and §7's measurements.

### 5.5. Diagnostics

- **Former arrow.** `-> Type` after an accessor parameter list is one `MisplacedSyntax_Kd` over the arrow and Type,
  with the `AccessorResult` form reworded (the Property supplies the result). The parser reads the Type for its
  extent and drops it, so the accessor binds under its contract, independent errors stay visible, and nothing else
  rests on the arrow. This branch replaces the missing-result `Expect`, the setter-result branch and their recovery.
- **No repair candidate.** Deleting the text keeps the old contract only when it introduces no name used elsewhere,
  which needs the old binding; the migration probe decides that (§6.2).
- **Misplaced clause.** A Property-level clause on a computed Property or requirement reuses the `OriginClause` form
  and is dropped, as a clause after an accessor already is.
- **Unchanged.** Valid forms outside implementation support stay location-only `Unsupported_Kd`, broken contracts are
  Internal errors, checks record typed facts and the adapter words them. No new DiagnosticCode, service, schema or
  migration parser.

## 6. Units after R9

### 6.1. Units

All units follow [AGENTS](../../AGENTS.md), the post-R9 architecture and
[VERIFICATION](../../docs/dev/VERIFICATION.md) on the completed R9 tree, never retired Analysis or Emission or
pre-consolidation Binding owners.

| Unit | Work and exit condition |
| --- | --- |
| AR0 — Baseline and design gate | Record R9's completion commit, this proposal's Git blob (verification manifests exclude `draft/`) and the SPEC revision. Reproduce current and new acceptance, header-only names and sets, and the G174 observations. Census producers, consumers and inputs; classify every annotation (§6.2). Rebase §5 onto the post-R9 owners; compare independently written owner-local and shared-structure designs and review the choice before coding. |
| AR1 — Structure, only if needed | For example, one header-binder decision if R7 leaves two walkers. Behavior-preserving, with diagnostic, semantic and IR comparisons ([VERIFICATION](../../docs/dev/VERIFICATION.md#behavior-preserving-commits)); omitted when empty. |
| AR2 — Atomic cutover | Grammar, completion, diagnostics, writers, services and every active input together, with §5.4's deletions. Formal integration (§8) lands in the same group, since SPEC `kimi` blocks are compiler test inputs (`DiagnosticCorpus.SpecBlocks`, `tests/diagnostics/spec-cases.json`). No accepted form depends on a later fix. |
| AR3 — Completion | §7's evidence and final review; area sizes, hotspots, duplication candidates, deletions and remaining uncertainty; only the affected STATUS and CODEMAP facts. |

- **Review weight.** Parsing, Binding and services change: the full process. After the owner approves the AR0
  design, DECISIONS records it, and AR1–AR2 may share one group review (DECISIONS Q6 form 2) before merging. A
  departure, such as a new representation, component or vocabulary entry, takes the full process.
- **Delivery.** Each unit is verified, associated with `scripts/verify-commit.ps1` and pushed without force. A Session
  on identical inputs replaces the last unit's Unit run; no edits run during checks; NativeAOT needs explicit
  instruction.

### 6.2. Migration

- **Classify by span.** A disposable probe of the pre-cutover compiler, outside the tree, lists every accessor result
  span and whether its Type introduces an Origin or set name used outside it. About two dozen test annotations carry
  Origins or binding sets; executable sources carry none.
- **Edit.** Setters delete ` -> ()`; unmarked getters delete ` -> T`. A marked computed or required getter moves the
  name into its header, renaming on conflict; a marked stored getter replaces the references with the complete storage
  Origin expressions and never adds a storage slot. One reviewed manifest lists every edit.
- **Inputs together.** Library, milestones, MIR fixtures, benchmark and workload sources, source-keyed test data
  (`tests/diagnostics/*.json`, anchors renamed one-to-one) and C# test sources. Semantic expectations stay fixed;
  tests of removed rules become former-arrow tests or are deleted, citing this plan.
- **Exact equivalence.** No input writes a multiline accessor header, so each edit deletes one subtree inside one
  line: positions shift by a column offset on that line and syntax ids (the `@` numbers of MIR dumps) by the deleted
  pre-order nodes. After that mapping, baseline and migrated IR, MIR and diagnostic dumps, including the `tests/mir`
  goldens, must be identical, except the intended former-arrow cases. Native fixtures run only where IR differs.

## 7. Verification and performance

This revision is documentation only: diff, reference and independent review; no build or compiler run. The
implementation uses the owning tests and helpers at the lowest public layer, with an invalid counterpart per rule.

| Rule or risk | Required evidence |
| --- | --- |
| Syntax | Stored, computed and required signatures with and without receivers; Unit and Function-Type results and inputs; single-line and indented bodies; former arrows on getters and setters (one report, no cascade, the next item independent); incomplete signatures; Property-level clauses per Property kind; parse, write and parse. |
| Types and bodies | Initializer-inferred storage; Copy and Non-Copy custom getters; stored input mismatch versus differing computed and required inputs; Unit getters, setter discard and `return` values; accessibility at Property and accessor domains. |
| Origins | Receiver, external, `static` and result-only names; a header naming a receiver Origin; nested slots, binding sets and relations; stored inheritance; per-operation binders; shorthand versus explicit requirements; setter non-visibility; invalid relations. |
| Contract and use | Standard bridges, custom witnesses, generic and associated Types, inherited and object receivers, input fitting and compound updates; call selection and failure boundaries unchanged. |
| Support boundaries | Origin-bearing setter inputs (G79), static accessor execution and exclusive object accessors (OCC-X) keep the same `Unsupported_Kd` before and after. |
| Services | Writers and Hover show canonical source and completed operations with declaration locations; rebinding after header edits. |

Representative suites at the baseline: `PropertyParseTest`, `PropertyRevisionParseTest`, `PropertyBindingTest`,
`PropertyRecoveryTest`, `AccessorReceiverShapeTest`, `ReceiverShorthandTest`, `OriginRedesignTest`,
`PropertyResultEmissionTest`, `PropertyWitnessEmissionTest`, `InheritedAccessorEmissionTest` and `HoverSyntaxTest`;
use their post-R9 equivalents. Library migration adds `KimiLibraryShapeTest`.

- **Equivalence.** §6.2's mapped comparison shows unchanged O0 and O2 code without broad normalization.
- **Performance.** Existing measurements cover the changed paths: `--property-plans` (Milestone 36, which has
  accessor results; [Property measurements](../../src/Benchmark/Properties.md)), `BindingBenchmark`'s `Properties`
  scenario, `ParseBenchmark` and `FrontEndBenchmark`, on baseline and migrated inputs in alternating runs under fixed
  conditions. Record time, allocations and retained memory; the existing `Purpose=Allocation` regressions stay
  unchanged. No new workload, extra warm-up or relaxed baseline; failed evidence is kept.
- **Completion.** `scripts/verify.ps1 -Mode Session` with explicitly selected native checks. A failed required check
  leaves the work incomplete.

## 8. Integration map

Formal intake, when explicitly requested, updates the authoritative definitions together, in AR2's group (§6.1).

| Proposal | Specification targets |
| --- | --- |
| §2.1 | Appendix F.7 (productions and example); F.4.3 (a leading `->` continues only function headers). |
| §2.2 | §11.1 (table and example), §11.3 (opening sentence and example), §11.3.1 (text and example), §11.4, and the examples of §9.4.6, §11.2.2, §11.3.3 and §13.7.1 (its comment). |
| §3 | §11.6 (storage-only Property clauses; the header as the getter result), §15.3.4 (the name-owner table), §15.4.2 (the Accessor row). |
| §4 | §11.9 (text and example). |
| §5.5 | DIAGNOSTICS' recovery list, when implemented. |

- **Preserved:** §7.1, §9.4.4, §11.2, §11.5, §11.7–§11.8, §11.9.1–§11.9.2, §13.7, §14.2, §14.9, §17.3 and §23. LIBRARY
  and HOVER need no change.
- **Explicit only:** [GUIDE](../../docs/GUIDE.md) (two accessor results), [SETTLED](../../docs/SETTLED.md)'s
  "Exclusive or owning accessor receivers" example (its decision is unaffected) and SPECJ. Since they would show a
  rejected form, the cutover instruction should include them.
- **History:** frozen texts and other proposals, such as the 2026-10-10 Contract Conformance Redesign Options, are not
  rewritten.

## 9. Principles

| Principle | Application |
| --- | --- |
| One Concept, One Canonical Form | One accessor signature form, one source for each result and one place for each operation's clauses; no legacy form. |
| Local Reasoning | Results and Origin contracts come from declarations and are completed before bodies; each operation has one bounded binder, and its header is beside it. |
| Explicit Semantics | Header Types, setter inputs, receivers and needed relations stay written; standard Place access and getter values stay distinct; the function Unit default never applies to accessors. |
| Compiler Server Protocol | Completed operations are exposed with their locations; the former arrow gets one located error without cascades; migration is checked by an exact mapped comparison. |

## 10. Revision notes

| Change | Reason |
| --- | --- |
| Property-level clauses only on stored Properties (§3.3) | The earlier text gave a computed getter two equivalent clause regions joined by an order and conjunction rule. |
| One header binder (§5.1) | `TypeContext` and `OriginOwner` disagree at the baseline; the written result hid it. |
| Result readers use the Property contract (§5.2) | No consumer keeps a second result source or a synthesized node. |
| Shorthand rebinding left to G79 (§5.2) | The earlier text required its removal at the cutover without a design; it is independent of result syntax. |
| Defined former-arrow report (§5.5) | The earlier text left recovery open; one branch replaces three paths and deletes a form. |
| Integration with AR2 (§6.1) | SPEC `kimi` blocks are compiler test inputs; a later intake would leave them rejected. |
| Sites, census and boundaries (§5.2, §6.2, §7, §8) | Missing readers, walkers, syntax forms, test data, benchmark sources, documents, SPEC examples and Unsupported boundaries. |
| Exact equivalence, existing measurements (§6.2, §7) | In-line deletions allow a mapped identical comparison; existing measurements already run accessor results. |
| Shorter text (§1–§4) | Unchanged rules became references; the authoring task's process text was removed. |

Owner questions:

- **Clause placement.** Approve or reject §3.3. If rejected, computed Property-level clauses join the getter contract
  as the earlier text said, and the requirement observation becomes a separate fix.
- **Shorthand.** With result-free signatures, an explicit requirement accessor is one short line (`get()`,
  `set(value: T)`). Removing `has get, set` would leave one requirement form and delete inline accessor parsing and the
  shared header's dual position (`BindImplicitSetterInput`'s rebinding), at the cost of one line per operation. Not
  applied; the shorthand stands unless the owner decides otherwise.
- **Scheduling.** Whether the cutover instruction includes formal integration, GUIDE and SETTLED's example.
