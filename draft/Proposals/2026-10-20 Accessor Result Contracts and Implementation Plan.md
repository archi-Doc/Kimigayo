# Accessor Result Contracts and Implementation Plan

Specification change and implementation plan — 2026-10-20.

## 1. Authority and start gate

The owner adopts removal of result annotations from getters and setters. For the changes expressly defined
here, this plan takes precedence over [SPEC](../../docs/SPEC.md) and its referenced documents. Existing
specifications govern everything else. This authority does not make an unimplemented form supported.

**Implementation starts only after R9 is complete**, as recorded in the
[compiler stage ledger](../../docs/dev/COMPILER_ARCHITECTURE.md#stage-ledger). Every implementation unit,
including restructuring, executable probes, migration helpers, source migration and optimization, is behind
that gate. Read-only inspection, design and document review may precede it. Do not implement inside R9 or
backport the change into an earlier stage.

This task creates the proposal, its required intake record and a scheduling entry. It changes no current SPEC/IMPL text or
executable source. Formal integration into the specification is a separate, explicitly instructed task.
Other approved changes retain their own scope and gates; this plan introduces no ordering between
independent post-R9 changes.

## 2. Language rules

### 2.1. Result contracts and grammar

An accessor has no result annotation. A custom or computed getter, and an explicit Contract getter
requirement, obtains its result Type from its Property contract. Every setter has result Type Unit.
Neither the result Type nor the public Origin contract is inferred from an accessor body.

Replace the affected productions in Appendix F.7 with:

```ebnf
GetterSignature   := "get" "(" AccessorReceiver? ")"
SetterSignature   := "set" "(" (AccessorReceiver ",")? "value" ":" Type ")"
AccessorReceiver  := "self" ":" Type
CustomAccessor    := Access? (GetterSignature | SetterSignature) AccessorBody
StoredAccessor    := Access? ("get" | "set") | CustomAccessor
RequiredSignature := (GetterSignature | SetterSignature) OriginClauses?
```

Other productions and restrictions remain. A computed or required Property still needs its header `: T`.
An explicit setter still needs `value: U`. Each accessor occurs at most once, in either order.
Concrete custom accessors need bodies; requirements admit Origin clauses but no executable body.
Bare `get` and `set` remain standard stored access, distinct from signatures with parentheses.

`get() -> T` and `set(value: U) -> ()` are Language syntax errors, even when the result annotation agrees
with the contract. There is no accepted compatibility spelling, flag or fallback parser. Recovery may
recognize rejected text without accepting it. Remove Appendix F.4.3's accessor-result `->` continuation:
function headers retain their rule, and arrows inside Function Types remain valid, including
`set(value: (i32) -> i32)`. Ordinary Body and delimiter continuation rules still apply.

### 2.2. Property kinds

| Property | Type source | Custom getter result | Setter input and result |
| --- | --- | --- | --- |
| Stored `let` | Complete storage Type `T`, from the existing annotation/initializer rules | Exactly `T`; `T is Copy` must be proven | Forbidden |
| Stored `var` | Complete storage Type `T`, from the existing annotation/initializer rules | Exactly `T`; `T is Copy` must be proven | Written `U` must equal `T` after completion; result Unit |
| `computed p: T` | Header completed as a getter result | That completed `T` | Written `U` may differ from `T`; result Unit |
| Explicit `property p: T` requirement | Header completed as a getter result | That completed `T` | Written `U` may differ from `T`; result Unit |

Complete-Type equality includes normalized structure and Origin bindings, not just spelling.
Stored Type inference still uses only a declaration initializer, never accessor bodies, setter inputs
or later assignments. Storage Origins still follow the storage rules; initializer inference introduces
no hidden storage Origin. Computed Properties remain storage-free, require a getter body and permit an
optional setter body. Requirements remain instance-only, with a mandatory getter and optional setter.

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
        set(value: i64) => self.level = value@i32
```

Standard `get` remains Place access, not a function returning `T`; standard `set` remains storage update.
Their borrowing, Move and initialization rules do not change. A custom getter neither exposes hidden
storage nor changes its result to `ref/T` when Copy is unavailable.

### 2.3. Receivers, bodies and access

Instance getters retain `self: ref/Self`, setters `self: uniq/Self`; omission inserts those receivers.
Written receiver Origin annotations remain meaningful. Static accessors have no receiver and reject one.
Accessor parameters admit no defaults or `!` boundary; `value` remains an immutable parameter binding.

Getters check body results and explicit returns against their complete result Type. A Unit getter follows
the existing discard rule; other getters follow the existing value-result rule. Setters discard a
single-item expression and complete normally with Unit. Explicit return values must fit Unit:
`return 123` is an error. Discard cleanup and warnings, including discarded `Result`, remain; `try` cannot
propagate failure from the setter's Unit target. A body that always diverges does not change the declared
result contract to Never.

Accessor access restrictions, construction/destruction, getter-result temporaries, assignment and compound
update evaluation, receiver compatibility and Loan rules are unchanged. API accessibility checks still
cover the Property result/storage Type, each receiver and each setter input at the applicable access domain.
Deleting a written getter result does not delete these checks.

## 3. Origins

### 3.1. One contract per operation

Getter and setter calls have independent Origin binders. No Property-wide call Origin binder is introduced.
For a computed Property, its header Type is a result-Type occurrence in the getter signature.
Resolve these parts as one getter contract before checking the executable body:

1. The explicit or inserted getter receiver.
2. The Property header Type, including nested annotations and binding sets.
3. Property-level Origin clauses followed by the getter's leading Origin clauses.

The two clause regions form a conjunction governed by the ordinary premise and obligation rules; they are
not separate contracts or two proof passes. Property clauses precede accessors, and accessor clauses precede
executable items, with the existing source-order rules for excluded syntax.

### 3.2. Names and completion

The header and getter receiver form one signature scope; name resolution within that signature does not
depend on textual declaration order. A receiver-introduced Origin can be named in the header. An unbound
simple Origin name introduced in a header annotation follows the ordinary named-callable quantification
rules, including a name occurring only in the result. A binding set in the header belongs to this getter
contract. Relation clauses reference names; they do not introduce them.

Names introduced by the getter contract are visible in its header, receiver, clauses and body, including
the computed Property's leading clauses. They are not visible in an explicit setter. Enclosing Origins
remain visible under the existing lookup and no-shadowing rules. Header `self` denotes the getter receiver;
static headers have no `self`. Setter names and `value` cannot supply facts to the getter contract.

```kimi
// source belongs to this getter signature; the surrounding Property is computed.
public computed view: ref/Resource during source
    get(self: ref/Self during source) => self.item@ref
```

Apply SPEC §15.4's completion once: retain complete/inherited bindings, resolve explicit annotations and
relations, quantify aggregate result slots as §15.4.1 requires, then elide the remaining positions.
For an instance getter, omitted result Origins use the ordinary result rules with its receiver; a getter
whose only directly borrowed input is `self` normally obtains that receiver Origin. Explicit external
Origins are preserved. Static getters use the same ordinary result rules, not an unconditional `static`
substitution for every result slot. Missing required information is a declaration error, not body inference.

### 3.3. Stored accessors

A stored Property's header and Property-level Origin clauses remain its storage contract. They do not
acquire the computed getter's signature scope. Complete storage first under SPEC §15.3.2 and §15.4.

A custom getter inherits that entire complete storage Type as its result. Do not elide it again against the
getter receiver. A custom setter inherits omitted input Origins from storage before ordinary function
elision; written input bindings must agree with storage. An accessor parameter cannot narrow the calls
allowed by storage. Accessor clauses keep their existing obligations and cannot change the storage contract.
Receiver Origins stay per operation; inherited storage Origins are not new call parameters.

## 4. Contract requirements

### 4.1. Explicit and shorthand forms

The explicit form uses the same result-free signatures:

```kimi
contract ReplaceableItem
    property item: ref/Resource
        get()
        set(value: Resource)

contract ReadableItems
    property items: Slice<i32>{result}
        get()
            origin result.source == self
```

The header belongs to the getter result contract as in §3.2. Origin clauses attach beneath the relevant
`get()` or `set(...)`. Do not add Property-level clauses to the Contract requirement grammar. There is
no storage, initializer, executable body, accessor access modifier, Attribute, default or `!` boundary.

`property p: T has get, set` denotes the same operations as:

```kimi
property p: T
    get()
    set(value: T)
```

Each written/implicit occurrence of the Type expression `T` is resolved in its own operation's position:
getter result or setter input. Do not copy the getter's completed result into the shorthand setter.
Enclosing, already-bound Origins refer to the same definitions; `self` denotes the respective receiver.
New Origin names, binding sets and omitted slots are bound independently for each operation. Identical
spelling does not identify their per-call Origins.

Thus `property p: ref/i32 has get, set` gives the getter a result dependent on its receiver, while the setter
input receives its own input Origin, independent of its receiver. This preserves the existing operation
contracts. Use the explicit form for a different input Type or operation-specific Origin clauses.

### 4.2. Conformance

Conformance compares completed operation contracts under existing substitution, accessibility and
requirement-compatibility rules, without implicit conversions or stronger preconditions. It does not
require a stored implementation's header to equal the requirement header.

Standard witnesses remain: Copy of storage `F` for a by-value `F` result when Copy is proven, shared borrow
of `F` for a `ref/F` result, and permitted standard `set` of `F` for input `F` and result Unit. For example,
standard storage `Resource` can satisfy the getter and setter of `ReplaceableItem` above through those
bridges. Custom getters expose no hidden storage. Inherited and object-receiver requirements keep their
existing contracts and checks.

## 5. Implementation design and migration

### 5.1. Cause, owner and alternatives

Inspection baseline: `ee0cfa4795913cd647bd187152599ff32d02a027`. Current acceptance of result annotations
implements the old specification; this is a language revision, not a claim of a compiler defect.
Inspection is read-only evidence, not an executed reproducer or performance measurement.

Required meaning → the Property supplies the getter result and the setter operation fixes Unit →
independent accessor result syntax repeats that contract and requires agreement checks →
the declaration-contract owner completes and publishes each operation once →
accessor result syntax, duplicate result binding/comparison and result-header formatting become unnecessary.

| Alternative | Decision |
| --- | --- |
| Make the annotation optional inside the existing parser/binder | Rejected: two source forms and an unnecessary agreement path survive. |
| Remove accessor result syntax and use the existing complete declaration contracts | Selected: one result source, shared across stored, computed and required forms. |
| Add a shared Property call binder, new result IR form or feature-specific side table | Rejected: calls have distinct Origin contracts; the existing complete-Type/declaration representation carries the needed facts. |
| Infer results from bodies or move the sole Type to the getter | Rejected: unnecessary inference or loss of the header's published result contract. |

No new HIR/MIR operation, intrinsic or runtime mechanism is required by this design. If the post-R9 owners
cannot express it without a new representation or moved responsibility, stop and follow AGENTS' full design
and approval process; do not add a consumer-side syntax decision or a second completion rule.

### 5.2. Sites and deletion obligations

These paths are navigation at the inspection baseline, not instructions to preserve obsolete owners.
After R9, rediscover them through [CODEMAP](../../docs/dev/CODEMAP.md) and follow the
[target architecture](../../docs/dev/COMPILER_ARCHITECTURE.md#target-pipeline).
File paths in the table are relative to `src/Kimi/`.

| Current site | Responsibility after the change |
| --- | --- |
| `Compiler/Parsing/Parser.cs`; `Compiler/Parsing/Koto/Declarations/PropertyAccessorKoto.cs` | Parse/write the one signature form; remove accessor result syntax and its child/span handling. Preserve ordinary Function Type syntax and bounded invalid-syntax recovery. |
| `Compiler/Parsing/Koto/Common/ControlFlowContext.cs`; `Compiler/Parsing/Koto/Declarations/FunctionKoto.cs` (`RefreshAccessor`) | Audit direct result-syntax consumers if they survive R9. Remove their dependency on accessor result syntax rather than recreating a result subtree. |
| `Compiler/Binding/Binding.Properties*.cs`, `Compiler/Binding/Binding.Origins.cs`, `Compiler/Binding/Binding.OriginDeclarations.cs` | Complete storage or each operation in its correct scope/position. Publish the getter result and Unit setter result through declaration contracts; delete the duplicate getter-result binding and agreement path. |
| `Compiler/Binding/Binding.PropertyCalls.cs`, `Compiler/Binding/Binding.PropertyWitnesses.cs`, `Compiler/Binding/Binding.PropertyMatching.cs` | Consume complete operation contracts; preserve input fitting, receiver rules and standard witness bridges. |
| Syntax writers, `Checking/Hover/HoverBuilder.Headers.cs`, `SemanticQuery` and existing source/service consumers | Emit canonical source; expose complete results, Origins and their declaration locations without inventing a source return annotation. |
| Library, active tests/samples, fixtures, milestone sources and any extension syntax consumers | Migrate actual accessor result annotations together; preserve function arrows, negative cases and frozen history. Follow extension AGENTS if extension files change. |

Binding owns the decisions; MIR and Backend consume validated published contracts. Post-parsing phases must
not mutate syntax or synthesize a getter result subtree. In particular, shorthand `has set` must complete
its input without temporarily rebinding header syntax and restoring semantic slots.

Delete result-annotation-only diagnostics, writers and obsolete state when no other contract uses them;
retain stored setter input agreement and body-result checks. Search all accessor producers, consumers and
serialized/source-writing surfaces before removal. Record needed contract/responsibility decisions in
DECISIONS in the first implementing commit. Expected effect: less syntax and duplicate declaration work,
with unchanged runtime semantics, ABI and native allocation guarantees. Net size and speed are unmeasured.

### 5.3. Migration and diagnostics

Migration operates on identified source spans and resolved old contracts, not textual arrow replacement.
For computed/required getters, preserve the header's Type meaning and merge any Origin annotations or
binding-set information present only in the old result. Rewrite dependent clauses/body references and
rename bound names when needed. For stored getters, replace references to a removed result binding set
with the corresponding complete storage Origin expressions; do not move a getter-local name into storage
or turn it into a new storage slot. Setter migration removes only its Unit result annotation.

Alias expansion and scope/access checks may be needed. Guarantee preservation only after comparing the
normalized Types, quantification, premises and result guarantees; a source that cannot be transformed with
that evidence needs a reviewed rewrite, not a guessed edit or a relaxed contract. Preserve evaluation,
ownership, cleanup and diagnostics apart from the intentional syntax/source-location changes.

Old arrows receive a located Language error. Valid new forms outside implementation support receive
location-only `Unsupported_Kd`; broken internal contracts remain Internal errors. Record typed causes
where checks fail and use the diagnostics adapter for wording. Any offered repair must identify its source
snapshot and verified/undecided preconditions, then be checked after application. This plan adds no new
service endpoint, schema or permanent migration parser.

## 6. Units and verification after R9

| Unit | Work and exit condition |
| --- | --- |
| AR0 — Baseline and design | Confirm the recorded R9 completion commit. Reidentify owners, reproduce current/new syntax and Origin cases, record all shared sites and the reviewed source migration map. Compare independently written owner-local and shared-structure implementation designs and review the choice before coding. Record the governing proposal's Git blob, the then-applicable SPEC revision and the source/configuration identity. |
| AR1 — Structure, if needed | Restructure only where necessary, in a behavior-preserving commit with the required diagnostic, semantic and generated-code comparisons. Omit an empty unit; follow the full process for new representations or responsibilities. |
| AR2 — Atomic cutover | Change grammar, contract completion, writers/services and active executable inputs together. Remove redundant result machinery and any temporary migration helper in this unit; preserve its edit/equivalence data as evidence. No accepted form depends on a later consumer fix. |
| AR3 — Completion | Finish the matrix below, equivalent-work measurements and final independent review. Record area sizes, hotspots, duplication candidates, deletions and uncertainty; update only affected support/navigation facts. All required checks must pass. |

Use the owning test classes and shared helpers, with a positive and independently invalid counterpart for
each changed rule where relevant. Do not multiply fixtures for syntactic variants.

| Area | Required evidence |
| --- | --- |
| Syntax and layout | Stored/computed/required signatures; explicit/omitted receivers; Unit and function-valued getters; setters taking Function Types; multiline headers and bodies; rejection of old arrows, incomplete signatures and wrong bodies; excluded-syntax checks and recovery to a following independent item. |
| Types and bodies | Initializer-derived storage; Copy and Non-Copy custom getters; stored input mismatch versus computed/required differing input; Unit discard and explicit returns; public accessibility and failed-header/body recovery. |
| Origins | Implicit self, explicit external/static and result-only Origins; receiver restrictions; header/receiver order independence; nested/aggregate slots, binding sets and relation clauses; stored complete-Type inheritance; independent getter/setter binders; shorthand versus explicit requirements; setter non-visibility and invalid or contradictory relations. |
| Contract/use paths | Standard Copy/borrow/set witnesses, custom access, generic and associated Types, inherited and object receivers, input fitting and compound updates; preserve failure boundaries and call selection. |
| Services and migration | Canonical parse/write/parse; complete semantic Types and source attribution; header edits/rebinding; precise legacy diagnostics; snapshot-bound edits; migrated binding-set references and no new storage Origin. |
| Generated behavior | Representative O0/O2 getter/setter calls and witnesses, single evaluation, borrow lifetime, exactly-once cleanup and existing allocation guarantees, compared with equivalent old-contract programs. |
| Size and performance | Equivalent short/long Type and Origin-heavy Property workloads; unchanged function-heavy controls. Measure compilation time, allocations and retained memory under fixed conditions; record net size by affected area and removed duplicate work. |

Current representative suites include `PropertyRevisionParseTest`, `PropertyBindingTest`,
`PropertyRecoveryTest`, `AccessorReceiverShapeTest`, `PropertyResultEmissionTest`,
`PropertyWitnessEmissionTest`, `InheritedAccessorEmissionTest` and the existing Origin/Hover suites.
Use their post-R9 equivalents. Library changes add `KimiLibraryShapeTest`; timing-only measurements belong
in `src/Benchmark`, and allocation/reuse regressions retain `Purpose=Allocation`.

Keep old and migrated sources with a reviewed span map. Compare semantic contracts and diagnostic facts
with mapped locations; explain expected source-location differences in snapshots or generated IR rather
than broadly normalizing them. Investigate every unexplained difference. Retain failed evidence and do not
relax assertions, native allocation guarantees or baselines to obtain a pass.

Follow [VERIFICATION](../../docs/dev/VERIFICATION.md): focused Unit checks and an end-of-session Session,
with required native fixtures and milestones explicitly selected. A Session on identical inputs may cover
the last Unit. No edits run concurrently with checks. NativeAOT needs separate explicit instruction.
Commit each verified unit, associate implementation evidence with `scripts/verify-commit.ps1`, then push
without force. Record the proposal's Git blob separately because verification manifests exclude `draft/`.

This document-only task uses diff, reference and independent review, with no builds or compiler tests.
The requested document review permits at most two revision rounds; unresolved blocking findings after
those rounds are reported to the owner rather than marked complete.

## 7. Integration map and Principles

Formal integration, when separately authorized, updates the affected definitions together:

| Proposal | Specification target |
| --- | --- |
| §2.1–2.2 | SPEC §11.1, §11.3, §11.4 and Appendix F.7; remove accessor-result continuation from F.4.3. Ordinary named-function defaults in §7.1 remain. |
| §3 | SPEC §11.6, §15.3.3–4 and §15.4, including the header's result position, signature scope, clause ownership and stored inheritance. |
| §4 | SPEC §11.9's explicit/shorthand requirements and independent completion; preserve its conformance and witness rules. |
| §2.3, §5.3 | Preserve §9.4.4, §11.2/§11.5/§11.7–8, §13.7, §14.2/§14.9, §17.3/§17.5 and §23; adjust affected examples and source/service descriptions without changing their independent guarantees. |

Migrate active authoritative examples at formal integration and executable inputs at the cutover.
GUIDE/SPECJ synchronization and historical or policy changes retain their explicit-scope rules.
Frozen proposal text is not rewritten; the intake register records mappings and later integration.

| Principle | Application |
| --- | --- |
| One Concept, One Canonical Form | One accessor signature form and one source for each result contract; no permanent legacy form. |
| Local Reasoning | Results and Origin contracts are fixed by declarations before bodies; each call has its own bounded signature scope. |
| Explicit Semantics | Header Types, setter inputs, receivers and necessary Origin relations remain explicit; standard Place access and getter values stay distinct. |
| Compiler Server Protocol | Expose complete results and their source provenance; retain structured diagnostics and reviewable, snapshot-bound, rechecked changes. |
