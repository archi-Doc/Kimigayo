# Named Alias Declarations

Specification change and implementation plan — 2026-10-16.

## 1. Authority and purpose

By the owner's instruction, this plan takes precedence over [SPEC](../../docs/SPEC.md) and its referenced documents
for the changes stated here. Existing rules govern everything this plan does not change. In particular, transparency
does not relax Type formation, access, Constraints, borrowing, construction, or dependency validation.

The owner approved ordinary named alias declarations, including public declarations, and the boundary rules below.
This document records that design; it does not claim implementation support or integration into SPEC. The current
authoring task changes only this proposal and its intake record in [INTEGRATED](../INTEGRATED.md). SPEC and its
referenced specifications remain unchanged. Formal integration requires a later instruction.

The problem is that the existing named alias is a document-local qualifier, whereas a reusable public Type name needs
an ordinary declaration. The solution is one named declaration form for a complete Type or a bound qualifier reference.
It gives the name a declaration identity without giving its target a new Type identity.

| Principle | Application |
| --- | --- |
| One Concept, One Canonical Form | `alias Name => Target` always declares a name; `alias Path` opens members. Scope never depends on the target kind. |
| Local Reasoning | Resolve and complete the target in its definition environment; never reinterpret it at a use. |
| Explicit Semantics | Preserve every Semantics layer and Origin binding. No new conversion, ownership operation, or implicit lifetime. |
| Compiler Server Protocol | Publish the alias declaration and its completed target separately, with source identity and existing diagnostic guarantees. |

## 2. Language rules

### 2.1. Forms and targets

```ebnf
OpeningAlias          := "alias" ContainerReference OriginClauses?
NamedAliasDeclaration := Access? "alias" Name "=>" AliasTarget OriginClauses?
AliasTarget           := Type | ContainerReference
```

The overlapping named-path syntax is parsed once. A plain target path admits complete Types, groups and Kotonoha
references in one name lookup; other Type syntax uses its ordinary lookup roles. There is no attempt as a Type followed
by a fallback as a Container. `=>` introduces a target, never an executable Body.

An opening alias keeps its existing document-local effect. A named alias targets exactly one of:

- A complete value Type, including a primitive, struct, enum, Tuple, fixed array, common Function Type, or a Type with
  explicit Semantics. An associated-Type projection or inherited Type parameter is allowed under its existing contract.
- A group reference with its enclosing Type and Origin arguments complete.
- A Kotonoha reference.

The target is not an expression. A value, function declaration, Semantics name, category, bare Contract, or unapplied
generic declaration is invalid. A legal object Type over a runtime Contract View is a complete Type and is allowed.
The alias introduces no new way to form such a view.

```kimi
public alias Count => i32
public alias Bytes => Kimi.Array<u8>
public alias Point => (f64, f64)
public alias Predicate => (i32) -> bool
public alias StaticText => ref/string during static
public alias Output => Kimi.Console
```

### 2.2. Declaration scope and identity

A named alias is an ordinary Type-namespace declaration at the source root or inside a group or struct. A root alias
belongs to the project root, not the document execution scope. It may appear before or after other ordinary items and
is visible before its declaration. It cannot appear in an executable Block, enum, Contract, or conditional-conformance
Block. Selected compile-time directives may contain it wherever their surrounding context permits it.

Existing access specifications and effective access domains apply. The default is `private`; at the root this means
the defining Kotonoha. Protected forms require a struct member. No other modifier or existing Attribute applies.
A named alias has no executable Body, storage, receiver, independent `Self`, members, or conformance declarations.

Its declaration identity uses its originating Kotonoha, parent, Name, kind and zero own generic arity. It has no own
Type, Semantics, length, or Origin parameters, no specialization, and no fragments. Two same-name aliases in one scope
are duplicate declarations even if their targets are equal. An alias conflicts with any other same-name Type-namespace
declaration in that scope, regardless of arity; the ordinary independent Value namespace remains available.

Nested aliases inherit the enclosing generic and Origin environment and its proven premises, but no runtime values.
`Self` in a target means the enclosing `Self`. All enclosing bindings remain on the alias declaration reference; only
dependencies actually present in the expanded target belong to the resulting Type. Thus an unused outer parameter
does not make two identical expanded Types distinct.

```kimi
public struct Buffer<T>
    public alias Element => T
    public alias Items => Kimi.Array<T>
    public alias Count => i32
// Buffer<i32>.Element is i32.
// Buffer<i32>.Count and Buffer<string>.Count are the same Type.
```

An alias is not a Declaration Container. It cannot receive fragments or make `rootgroup Alias.Child` a way to add
declarations to its target. For inheritance name conflicts, nested Containers and named aliases form one family: either
kind conflicts with an accessible ancestor declaration of either kind with the same Type-namespace Name, regardless of
arity or completed target role. Qualified access to inherited aliases follows the original defining declaration and
bound environment; the existing prohibition on unqualified inherited lookup remains in force.

### 2.3. Resolution, lookup and cycles

Index declarations before completing targets. Resolve a named alias in its own lexical and source environment,
including ordinary declarations, opening aliases and defaults. Forward references and acyclic alias chains are allowed.
Resolve each target once per source/configuration snapshot and enclosing symbolic environment; uses substitute the
already-established enclosing bindings. They never resolve its spelling in the caller's environment.

Publish the completed target's lookup roles before using the alias as a candidate. A group or Kotonoha alias has the
Qualifier role, not Core. A complete-Type alias participates as an existing complete-Type binding does: selection is
followed by ordinary Type Legality. Constraints, expected Types, arguments and later member names never select an alias
or reopen lookup. Failure or a cycle while completing a potentially relevant alias cannot be treated as its absence.
An already-completed qualifier-only alias remains ineligible for Core lookup, just as a group is.

The source form decides where lookup starts. `::A` may name a root alias declaration; `Dependency.A` may name a public
alias in that direct dependency. Explicit and default opening stages remain stages 5 and 6. Named aliases no longer
have a separate document-local stage; an alias declaration can still be found through an opened Container's members.

Only repeated paths to the same alias declaration with equal bindings are deduplicated. Different alias declarations
remain different lookup candidates even when their expanded Types are equal. Type equality is applied after name
selection, including during Signature normalization.

An unresolved target-completion cycle is a Language error, even for an unused declaration. This includes recursive
Type expansion such as `alias A => Kimi.Array<A>`. Ordinary nominal recursion is not alias expansion: completing a
reference to a struct does not recursively expand its Fields. Normal inline-storage and dependency-cycle rules still
apply. A group reference likewise does not require recursively completing all its members.

```kimi
alias Size => Count
alias Count => i32              // Valid forward reference.
alias A => B
alias B => A                    // Error: target-completion cycle.
```

### 2.4. Complete Types and Origins

Complete means complete relative to the declared enclosing bindings, not necessarily a concrete ground Type. Every
target argument, Semantics layer, associated projection and required Origin contract must be determined at definition.
Omitted information may come from established enclosing bindings or existing contract-completion rules, never from a
later use. There are no inference holes, partially supplied arguments, or alias-owned assumptions.

Attached `origin` clauses resolve within the target and its enclosing environment. They fix bindings by established
equalities and prove remaining obligations; they introduce no universally quantified alias parameters or new premises.
All formation and input conditions must hold for every binding admitted by the enclosing contract. A symbolic
associated projection remains symbolic under that contract; alias declaration does not infer a new conformance.

```kimi
alias R => ref/i32 during static
alias F => (ref/i32) -> ref/i32
// F is complete: its Function Type binds a direct-input Origin per call,
// and its result uses that Origin under the existing elision rules.

alias BadBorrow => ref/i32       // Error: no context completes this Origin.
alias BadArray => [_ of u8]      // Error: an inference hole.
alias Items<T> => Kimi.Array<T>  // Error: alias-owned parameters.
```

Every use preserves the completed target, including inner and outer Origins:

| Form, with `R` above | Meaning |
| --- | --- |
| `R` or `owner/R` | The same `ref/i32 during static` Type; redundant `owner` removes no reference. |
| `ref/R during a` | An additional outer borrow of storage holding R, subject to ordinary formation and Loan rules. |
| `R during a` | Invalid: `during` does not search through an alias or replace its binding. |
| `R<i32>` | Invalid: an alias has zero own generic arity. |

`A{v}` may name the existing bindings of A's expanded target only when that target directly has a known, nonempty
named-Type schema. It creates no slots and performs no completion. Do not peel reference layers or search arguments,
Tuple elements or function signatures for a schema. A clause such as `origin v.source == x` checks the fixed binding;
it does not rebind it. Set names introduced inside a named alias declaration are local to its target and clauses, not
exports. Opening-alias set names keep their existing scope in the clauses and the document's alias scope.

### 2.5. Transparency and use-site legality

Expand aliases through the canonical Type normalization and substitution path. Alias spelling and declaration identity
do not affect Type identity, Signatures, overload ranking, Copy, Owned, conformance, layout, destruction, runtime Type
identity, or ABI. No conversion is needed between a Type and its alias. Members, constructors and static storage keep
their original declaration identities; another alias creates no second initialization or storage instance.

```kimi
alias Count => i32
func consume(value: Count) -> () => ()
func consume(value: i32) -> () => () // Error: duplicate Signature.
```

For `A.member`, `A.init(...)`, enum Case construction and Case Patterns, use the completed target and the normal rules
for that position. Do not discard Semantics to recover a Core. An owner struct alias may construct that exact bound
Type; an owner enum alias may select its Cases. Expected Types, payloads and constructor arguments check the fixed
bindings and never infer replacements, including for Origins. An alias to a Type parameter does not make that parameter
constructible. An alias to `rc/Node` cannot construct an object by `A.init()`.

```kimi
alias IntOption => Kimi.Option<i32>
let value = IntOption.Some(1)    // Already fixed to Option<i32>.
```

Adaptation targets, bases, runtime tests, casts, constraints and associated-Type syntax keep their existing admissibility
rules after expansion. In particular, aliases do not evade restrictions on written Origins in runtime-test targets.

### 2.6. Access, public APIs and opening aliases

A use needs access to the alias declaration and the selected target/member under the existing rules. At declaration,
check access and formation along the target path. For an exported alias, its effective access domain must be included
in the access domain of every concrete Type or requirement exposed by its recursively expanded target and contract.
Apply the same check to public group references and their enclosing concrete arguments. Existing symbolic generic
access rules still apply. A private intermediate alias is not itself an exposed Type when the final target is public.

```kimi
internal struct Hidden
public alias Exposed => Hidden  // Error: wider access than its target.
```

A public alias may expose a public Type, group or Kotonoha reached through the defining module's direct dependencies.
A consumer can reach it through a directly referenced module's public alias path. This does not add a transitive
dependency's reference name to the consumer's Compilation root or change the target's defining environment, version,
or specialization set. Changing an alias to a different normalized Type changes the public API; it is not an opaque Type.

Opening aliases retain root-based resolution without using opening/default alias lookup. That root now contains named
alias declarations. An opening target may therefore traverse them, but must finally denote a Kotonoha or a fully bound
group, struct, enum or Contract. A complete-Type alias qualifies only when its target itself is such a Container Type;
references, Tuples and functions are not stripped or searched to find one. Access checks still apply to every opened use.

An opening alias stays at the source root before every ordinary declaration or executable item, counting selected and
excluded syntax. A named alias counts as an ordinary declaration. Consequently the following forward reference is
valid, and reversing the two alias lines is invalid:

```kimi
alias Output
alias Output => Kimi.Console
writeLine("Hello")
```

Opening remains nonrecursive, creates no root member, and never exposes enum Cases or receiver members for unqualified
use. Default aliases retain their current configuration, precedence and mandatory Kimi target.

## 3. Compatibility and integration map

This is an intentional pre-alpha language change, with no compatibility mode or version bump. Existing named aliases
become ordinary declarations: their default scope widens to the defining Container/Kotonoha, lookup precedence changes,
same-scope repeated declarations become errors, and reserved root-name checks apply. Former hidden-named-alias warnings
are replaced by ordinary declaration conflicts or ordinary lexical lookup. Opening aliases stay document-local.

Migrate affected sources and assertions according to these rules, not by weakening checks. Qualify names or put aliases
in their owning Containers when former per-file names collide. Do not add a file-private alias modifier or retain the
old lookup route. Review interleaved opening/named aliases for the ordering rule.

The following map identifies later integration targets, not edits authorized by this authoring task:

| Plan rules | Existing owning definitions to update together |
| --- | --- |
| §2.1–2.2: forms, placement, declaration identity | SPEC §§6.1, 6.7, 9.1–9.3, 18.2; Appendix F declaration grammar and modifiers |
| §2.3: lookup, chains and scope | SPEC §§9.5–9.7, 18.1–18.2, 18.4.1; remove named aliases from stage 5 and the blanket exclusion from `::` |
| §2.4: complete targets and Origin sets | SPEC §§3.3.2, 8.1, 15.3–15.4, 18.2; Appendix F Type/Origin grammar |
| §2.5: transparency and construction | SPEC §§6.3.2, 6.5.2, 9.1, 9.7, 10.8; audit §§8 and 13–16 consumers without changing unrelated rules |
| §2.6: public access and dependency paths | SPEC §§9.4, 18.2–18.4; qualify the statement that transitive dependencies are not nameable |
| §§2–3: changed support boundary | SPEC Appendix D's transparent-source-Type-alias entry and directly affected examples; support claims only after verification |
| §5: compiler responsibilities and services | COMPILER_ARCHITECTURE, DECISIONS, CODEMAP and affected DIAGNOSTICS/HOVER contracts under their maintenance conditions |

Translations, guides and historical proposals are outside scope. The frozen
[earlier named-alias proposal](../Changes/2026-09-17%20Kimi%20Library%20and%20Named%20Aliases.md) is not edited.
The existing SETTLED decisions on inherited lookup and signature Origin names are preserved.

## 4. Implementation basis and design

### 4.1. Inspected baseline

Source baseline: `a76673d005ccb36f21d2b895abd005f8d7d49230`. These are source-inspection findings, not new execution results.
Before changing behavior, reproduce the relevant accepted/rejected cases on the actual implementation baseline.

| Current site | Shared cause or responsibility |
| --- | --- |
| [AliasKoto](../../src/Kimi/Compiler/Parsing/Koto/Declarations/AliasKoto.cs), [GroupKoto](../../src/Kimi/Compiler/Parsing/Koto/Declarations/DeclarationContainers/GroupKoto.cs) | Root-only parsing, no access modifiers, and two target representations: name segments or Type syntax. |
| [Binding.Aliases](../../src/Kimi/Compiler/Binding/Binding.Aliases.cs) | `namedAliases` is keyed by document/name; opening targets and hidden-name warnings share the old alias lifecycle. |
| [Binding.Types](../../src/Kimi/Compiler/Binding/Binding.Types.cs), [Binding.ContainerReferences](../../src/Kimi/Compiler/Binding/Binding.ContainerReferences.cs) | `AliasTarget` returns a scope; named targets are restricted to groups; stage 5 substitutes their target symbol. |
| [BindingModel](../../src/Kimi/Compiler/Binding/BindingModel.cs), [Binding.cs](../../src/Kimi/Compiler/Binding/Binding.cs) | Ordinary declaration indexing, symbol identity, access, duplicate checks and reusable binding state. |
| [Binding.OriginDeclarations](../../src/Kimi/Compiler/Binding/Binding.OriginDeclarations.cs), [TyCtxt](../../src/Kimi/Compiler/Binding/TyCtxt.cs) | Complete Origin contracts and canonical Types/substitutions. |
| [SemanticQuery](../../src/Kimi/Compiler/Binding/SemanticQuery.cs), [HoverBuilder](../../src/Kimi/Checking/Hover/HoverBuilder.cs) | Publish and display declaration facts; consumers must not redo target resolution. |
| [NamedAliasTest](../../tests/xUnitTest/Tests/NamedAliasTest.cs), [ModuleBindingTest](../../tests/xUnitTest/Tests/ModuleBindingTest.cs), [RuntimeTypeTest](../../tests/xUnitTest/Tests/RuntimeTypeTest.cs) | Existing positive/rejection, module, hidden-name and bound-alias expectations requiring semantic migration. |

The search also found alias consumers in Binding imports, qualification, startup, expressions, duplicate diagnostics,
source writing, and the old Analysis/Emission paths. [CODEMAP](../../docs/dev/CODEMAP.md) identifies the owning semantic
areas. Recheck this inventory at implementation start; follow the target architecture, not retired paths.

### 4.2. Chosen ownership and alternatives

Required meaning → one ordinary name for a completed Type/reference → the document-name map and scope-only result cannot
represent that contract → declaration indexing owns name/access/identity, alias completion owns its target, and TyCtxt
owns normalization/substitution → document-local named lookup, duplicate-target exceptions and per-use target rebuilding
become unnecessary.

Parser owns the two syntactic forms and placement. Both carry one complete target syntax tree; remove the alternate
name-segment representation after preserving source-writing behavior. Named aliases enter the ordinary declaration
table. The alias completion owner publishes one closed result: a normalized complete Type or a bound group/Kotonoha
reference, together with the alias declaration identity, role and established obligations. Use the existing declaration
dependency/validity mechanism for completion, cycles and invalidation; do not use null as both pending and failed.

References to aliases keep the alias symbol for navigation and the canonical target for semantic operations. Bind the
target in the declaration environment and substitute enclosing arguments through the existing substitution owner. No
alias-specific Type kind, MIR instruction, runtime descriptor, conversion, or code-generation path is required. MIR
consumers trust published Types; only the producer's validator diagnoses a broken IR contract.

| Alternative | Decision |
| --- | --- |
| Widen `AliasTarget` inside the existing document-local owner | Rejected: preserves the wrong scope and identity; public declarations would need a second lookup route. |
| Add `alias Name = Type` beside the old named-container form | Rejected: preserves two naming syntaxes and scope contracts. It was the narrower compatibility alternative considered independently. |
| Keep `=>` but choose scope from the resolved target kind | Rejected: identical syntax has different visibility and declaration behavior. |
| Use ordinary declarations plus one completed target contract | Selected: shares access, lookup, substitution and dependency validation, while retaining distinct opening semantics. |

Delete `namedAliases`, its stage-5 lookup, and `aliasWarnings`/`CompleteAliasWarnings` with their obsolete
`HiddenNamedAlias_Kd` producer and contract. Retain the document-scoped opening/default machinery. Replace
`aliasResolutionDepth` as a global named-alias restriction with the existing explicit root lookup path for openings;
named targets use normal lexical lookup. Do not replace these deletions with feature-specific mode flags or side tables.

Expected effects are fewer alias-specific lookup paths and more ordinary declaration entries. Aliases add no native
runtime work. Compilation time, allocations and retained memory are unmeasured; do not promise a speedup. Record actual
area size changes and matched measurements, without numerical size forecasts (DECISIONS Q5).

## 5. Implementation sequence

The proposal does not waive [COMPILER_ARCHITECTURE](../../docs/dev/COMPILER_ARCHITECTURE.md), its migration freeze,
closed vocabularies, or verification policy. Schedule acceptance of new forms after R6; earlier implementation needs an
explicit owner exception. Reassess file locations against R7 before editing. New vocabulary entries or exceptions must
follow the existing approval and decision-recording rules; this plan requires no new HIR/MIR operation vocabulary.

| Unit | Work and completion condition |
| --- | --- |
| A0 — Baseline and design gate | Reproduce changed cases, inventory every old named-alias path, compare independent implementation designs, and review the chosen representation. Identify actual vocabulary changes and obtain any required approvals before coding. Record decisions when first implemented. |
| A1 — Behavior-preserving structure | Give targets one syntax representation and move alias completion into its owning files; prepare ordinary declaration publication without accepting new forms. Preserve existing diagnostics, behavior and generated IR under the behavior-preserving verification procedure. Commit separately. |
| A2 — Declaration semantics | Switch all named aliases to ordinary declarations and implement the complete-Type/reference contract, lexical resolution, access, cycles and fixed Origins together. Remove the old named lookup/warning route in this unit. Update conflicting old expectations with this plan as the reason. No compatibility subset remains. |
| A3 — Consumers and services | Verify construction, Cases/Patterns, generic and associated substitutions, imports/reexports, source round trips and incremental invalidation. Publish alias identity and expanded targets through existing service contracts; remove obsolete consumers. Fix missing facts at their producer. |
| A4 — Completion | Run final review, matched compiler/native measurements and the required Session with explicitly selected native checks. Close migration omissions and record verified support and area/duplication evidence under the existing maintenance policy. |

Every behavior unit includes focused regressions before completion. A2 must not leave an accepted form with incomplete
consumers until A3: if a common consumer contract is missing, include its completion in A2 or leave the whole affected
form at the located `Unsupported_Kd` boundary until it is complete. A3 is a verification/publication unit, not permission
to accept unsound intermediate behavior. Any temporary structure needs an explicit removal unit and is gone by A4.

Diagnostics record typed causes at the failing check; their adapter owns wording. Invalid declarations are Language
errors, valid unimplemented forms use the single location-only `Unsupported_Kd`, and broken internal contracts are
Internal errors. Reuse existing cause categories where their meaning matches; do not invent repair guarantees.

Hover and inspection show the written alias and its completed target, including Semantics and Origins. Navigation and
rename identify the alias declaration; target navigation identifies the original declaration. Rebinding after an edit,
deletion, generated-document replacement or dependency/configuration change must invalidate both roles and targets.
Checks and edits remain tied to exact source snapshots. Do not add a second resolver to tooling.

## 6. Verification and completion evidence

This documentation-only task needs diff and reference review, no builds or tests. Implementation follows
[VERIFICATION](../../docs/dev/VERIFICATION.md) and uses existing test owners/helpers at the lowest observing layer.

| Rule or risk | Required distinguishing evidence |
| --- | --- |
| Syntax and placement | Public/nested declarations and source-writing round trips; opening-before-named forward reference; rejected reverse order, Blocks, enum/Contract bodies, parameters and modifiers; excluded directive ordering. |
| Ordinary names | Cross-file visibility, root `::`, lexical shadowing, access domains, duplicate same-target aliases, kind/arity conflicts, inherited qualification and Container/alias conflicts in both directions, independent Value names. |
| Roles and cycles | Type and qualifier targets; no fallback after target/role/member failure; direct/indirect/expanding cycles rejected; valid nominal recursion and nonrecursive group references accepted. |
| Transparency | Alias/target Type and Signature equality; different alias candidates stay ambiguous; nested environments and unused outer parameters; shared original conformance/static identities. |
| Complete contracts | Full Type family, inherited generic/pair bindings and associated projections; standalone missing Origin rejected; per-call Function Origins accepted; no new assumptions from clauses. |
| Origin preservation | Nested Semantics, `owner/A`, `ref/A`, fixed sets, forbidden `A during x`; legal and conflicting nested lifetimes; constructor/Case/Pattern bindings never replaced by context. |
| Access and modules | Public alias to public target accepted, private target exposure rejected, private intermediate alias accepted when target is public; reexport through a direct dependency without adding transitive root names. |
| Uses and services | Member/construction/adaptation/base/test/cast boundaries; alias to object handle cannot construct implicitly; structured diagnostics, alias/target navigation, Hover and source edits on exact snapshots. |
| Reuse and scaling | Long chains, shared dependency diamonds, wide imports, cycles and repeated reloads; bound completion work and retained memory, no stale symbols/roles after failure or regeneration. |
| Generated behavior | Equivalent alias/direct spelling has equivalent validated MIR, ABI, results, cleanup and static initialization; representative native O0/O2 execution and unchanged O2 allocation guarantees. |

Use `NamedAliasTest` and `ModuleBindingTest` for declaration/lookup behavior and the existing Origin, constructor, enum,
Type-relation, service and allocation classes for their own rules. Do not duplicate the whole matrix at every layer.
Tag allocation/reuse tests `Purpose=Allocation`. Add native fixtures only for generated-code behavior; keep timing-only
benchmarks in `src/Benchmark`. Compare equivalent workloads under fixed conditions and preserve failed evidence.

Run focused Unit verification during implementation and a Session at working-session completion; a matching final
Session replaces the last Unit run. Select required native fixtures and milestones explicitly. NativeAOT requires an
explicit instruction. Never weaken a native allocation assertion, ratchet or baseline to pass.

The implementation is complete only when the whole declaration contract is supported, obsolete named-alias paths are
removed, independent review has no blocking finding, required checks pass, and measurements and remaining uncertainty
are recorded. Commit each verified unit, associate applicable Verify evidence with `scripts/verify-commit.ps1`, and push
without force. Because this plan is in `draft/`, which the existing Verify manifest excludes, implementation evidence must
also record this proposal's Git blob identity alongside the source/configuration identity; do not claim Verify hashes it.
