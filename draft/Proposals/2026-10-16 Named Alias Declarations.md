# Named Alias Declarations

Specification change and implementation plan — 2026-10-16, revised 2026-10-17.

## 1. Authority and purpose

By the owner's instruction, this plan takes precedence over [SPEC](../../docs/SPEC.md) and its referenced documents
for the changes stated here; existing rules govern everything else. In particular, transparency relaxes no Type
formation, access, Constraint, borrowing, construction or dependency rule. The owner approved ordinary named alias
declarations, including public ones, and the boundary rules below. This document changes no current specification and
claims no implementation support; formal integration requires a later instruction.

Today a named alias is a document-local qualifier, but a reusable public Type name needs an ordinary declaration. This
plan makes `alias Name => Target` one declaration form for a complete Type or a bound qualifier reference. The name gets
a declaration identity; the target gets no new Type identity.

| Principle | Application |
| --- | --- |
| One Concept, One Canonical Form | `alias Name => Target` always declares a name; `alias Path` always opens members. Scope never depends on the target kind. |
| Local Reasoning | A target resolves and completes once, in its definition environment, and is never reinterpreted at a use. Opening targets never depend on named aliases. |
| Explicit Semantics | Every Semantics layer and Origin binding is kept. No conversion, ownership operation or implicit lifetime is added. |
| Compiler Server Protocol | The alias declaration and its completed target are published separately, with source identity and the existing diagnostic guarantees. |

## 2. Language rules

### 2.1. Forms and targets

```ebnf
OpeningAlias          := "alias" ContainerReference OriginClauses?
NamedAliasDeclaration := Access? "alias" Name "=>" AliasTarget OriginClauses?
AliasTarget           := Type | ContainerReference
```

`=>` introduces a target, never a Body, and the target is not an expression. A target that is a named path, with or
without Type arguments, is looked up once with the Core and Qualifier roles together ([§9.5](../../docs/spec/09-names-signatures-and-access.md#95-unqualified-lookup)
stops at the first stage with either); it is never retried as the other kind. Any other target is ordinary Type syntax.

A named alias targets exactly one of:

- **A complete value Type:** a primitive, struct, enum, Tuple, fixed array, common Function Type, or a Type with
  explicit Semantics. An associated-Type projection or inherited Type parameter is allowed under its existing contract,
  and so is an already legal object Type over a runtime Contract View; the alias forms no new view.
- **A group reference** whose enclosing Type and Origin arguments are complete.
- **A Kotonoha reference.**

A value, function, Semantics name, category, bare Contract or unapplied generic declaration is not a target.

```kimi
public alias Count => i32
public alias Bytes => Kimi.Array<u8>
public alias Point => (f64, f64)
public alias Predicate => (i32) -> bool
public alias StaticText => ref/string during static
public alias Output => Kimi.Console              // A group reference.
alias K => Kimi                                  // A Kotonoha reference.

alias Opt => Kimi.Option                         // Error: an unapplied generic declaration.
alias Print => Kimi.Console.writeLine            // Error: a function.
alias Items<T> => Kimi.Array<T>                  // Error: an alias has no own parameters.
```

### 2.2. Placement, access and identity

- **Placement.** A named alias may appear wherever a nested Container may ([§6.1.1](../../docs/spec/06-declarations-and-containers.md#611-source-root-and-nested-containers)):
  at the source root and in a group or struct, also inside a selected compile-time directive there; never in an enum,
  Contract, executable Block or conditional-conformance Block. It is an ordinary Type-namespace declaration, visible
  before its declaration. A root alias belongs to the project root, not to the document execution scope.
- **Access.** It takes one access specification under [§9.4.1](../../docs/spec/09-names-signatures-and-access.md#941-access-levels-and-domains):
  `private` by default, which at the root means `Kotonoha(K)`; protected forms only as a struct member. No other
  modifier or Attribute applies.
- **Shape.** It has no Body, storage, receiver, own `Self`, members or conformances, and no own Type, Semantics, length
  or Origin parameters or specializations. It is not a Declaration Container: it receives no fragments, and a
  `rootgroup` path through it is a kind conflict.
- **Identity and conflicts.** Its identity is its originating Kotonoha, parent, Name, kind and zero arity. Under
  [§9.1](../../docs/spec/09-names-signatures-and-access.md#91-signatures), `alias` is a Type-namespace kind that never
  forms fragments: a second same-name alias in one scope is a duplicate even with an equal target, and any other
  same-name Type-namespace declaration there is a kind conflict, whatever its arity. The Value namespace stays
  independent.
- **Inheritance.** [§9.7.1](../../docs/spec/09-names-signatures-and-access.md#971-bound-container-paths)'s ancestor rule
  covers aliases: a nested Container or alias conflicts with an accessible ancestor Container or alias of the same
  Type-namespace Name, whatever its arity or target. An inherited alias is reached only by qualification and keeps its
  defining declaration and bound environment.
- **Enclosing environment.** A nested alias inherits the enclosing generic and Origin environment and its proven
  premises, but no runtime values; `Self` in its target is the enclosing `Self`. A use supplies the enclosing arguments
  as for any nested declaration. The resulting Type depends only on the bindings that occur in the expanded target.

```kimi
public struct Buffer<T>
    public alias Element => T
    public alias Items => Kimi.Array<T>
    public alias Count => i32
// Buffer<i32>.Element is i32.
// Buffer<i32>.Count and Buffer<string>.Count are the same Type.
```

### 2.3. Resolution and lookup

- **Environment.** A target resolves in the alias's declaration environment: its lexical scopes and the source
  environment of its own SourceDocument, also for a fragment of a merged Container, including that document's opening
  and default aliases. It resolves once per declaration; a use substitutes the enclosing bindings and never resolves the
  spelling in the caller's environment.
- **Chains and cycles.** Forward references and acyclic chains are valid. A target-completion cycle is an error, even
  in an unused alias; this includes expanding recursion such as `alias A => Kimi.Array<A>`. Naming a struct is not
  expansion: its Fields are not completed, so nominal recursion stays valid under the inline-storage and dependency-cycle
  rules. A group reference does not complete the group's members.
- **Where an alias is found.** An alias is found where its declaring scope is searched: stages 2–3 of §9.5 for an
  unqualified Name, `::A` for a root alias, `Dependency.A` for a public alias of a direct dependency, `Outer<T>.A` for a
  nested one, and stages 5–6 as a member of a Container or Kotonoha opened by an explicit or default alias. Stage 5 has
  no separate named-alias entries.
- **Roles.** Role filtering of an alias uses its completed target, so the alias is completed first; a failure or cycle
  while completing it is an error, never its absence. A group or Kotonoha alias has only the Qualifier role and, like a
  group, does not stop Core lookup. A Type alias is eligible in the Core role, and in the Qualifier role when its target
  can qualify members; Type Legality then checks the use. Constraints, expected Types, arguments and later members never
  select an alias or reopen lookup.
- **Candidates.** Paths to one alias with equal bindings count once. Different aliases stay different candidates even
  when their expanded Types are equal; Type equality applies only after selection, also in Signature normalization.

```kimi
alias Size => Count                     // Valid: a forward reference.
alias Count => i32
alias A => B
alias B => A                            // Error: a target-completion cycle.
alias List => Kimi.Array<List>          // Error: expanding recursion.

struct Node
    alias Children => Kimi.Array<Node>  // Valid: naming Node does not expand its Fields.
    var children: Children
```

### 2.4. Complete targets and Origins

A target is complete relative to its enclosing bindings, not necessarily ground. Every Type argument, Semantics layer,
associated projection and required Origin binding is fixed at the declaration, by the target, the enclosing bindings or
the existing contract-completion rules, never by a use. There are no inference holes, partial arguments or alias-owned
assumptions.

Attached `origin` clauses resolve in the target and its enclosing environment. They fix bindings through equalities and
introduce no quantified parameters or premises; every formation and input condition must hold for every binding the
enclosing contract admits ([§15.3.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1533-declaration-attached-relations):
assume nothing). A symbolic associated projection stays symbolic; declaring an alias infers no conformance.

`A{v}` names the existing bindings of A's expanded target only when that target is directly a named Type with a
nonempty Origin schema. It creates no slots and completes nothing, and it never looks through reference layers,
arguments, Tuple elements or signatures. On a use's `A{v}`, a clause such as `origin v.source == x` checks the fixed
binding and never rebinds it. Set names declared in a named alias belong to its target and clauses only; opening-alias
set names keep their existing scope ([§15.3.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)).

```kimi
alias R => ref/i32 during static
alias F => (ref/i32) -> ref/i32   // Valid: each call binds its input Origin; the result elides to it.
alias BadBorrow => ref/i32        // Error: nothing completes the Origin.
alias BadArray => [_ of u8]       // Error: an inference hole.

struct Holder<T> {data}
    alias Cursor => Kimi.ArrayIterator<T>{v}
        origin v.source == data   // Declaration-attached: fixes the binding; v is local to this alias.
```

Every use keeps the completed target, inner and outer Origins included:

| Form, with `R` above | Meaning |
| --- | --- |
| `R` or `owner/R` | The same `ref/i32 during static`; a redundant `owner` removes no reference. |
| `ref/R during a` | An additional outer borrow of storage holding an `R`, under the ordinary formation and Loan rules. |
| `R during a` | Error: `during` never searches through an alias or replaces its binding. |
| `R<i32>` | Error: an alias has zero own arity. |

### 2.5. Transparency and uses

Aliases expand in the canonical Type normalization and substitution. Alias spelling and identity never affect Type
identity, Signatures, overload ranking, Copy, Owned, conformance, layout, destruction, runtime Type identity or ABI, and
no conversion exists between a Type and its alias. Members, constructors and statics keep their original declarations;
an alias creates no second initialization or storage.

For `A.member`, `A.init(...)`, Case construction and Case Patterns, the completed target is used under the normal rules
of that position, without discarding Semantics to reach a Core. An alias of an owner struct Type constructs exactly that
bound Type; an alias of an owner enum Type selects its Cases. Expected Types, payloads and constructor arguments check
the fixed bindings, Origins included, and never infer replacements. An alias of a Type parameter does not make it
constructible, and `A.init()` with `A => rc/Node` constructs no object.

Adaptation targets, bases, runtime tests, casts, Constraints and associated-Type syntax keep their admissibility rules
after expansion; an alias does not evade the ban on written Origins in runtime-test targets
([§13.6.4](../../docs/spec/13-operators-and-assignment.md#1364-runtime-is-tests)).

```kimi
alias Number => i32
func consume(value: Number) -> () => ()
func consume(value: i32) -> () => ()    // Error: a duplicate Signature.

alias IntOption => Kimi.Option<i32>
let some = IntOption.Some(1)            // Valid: already Option<i32>.

alias Shared => rc/Node                 // Node is an ObjectPayload struct.
let bad = Shared.init()                 // Error: A.init never strips the rc layer to construct.
let node = Node.init()@Shared           // Valid: the adaptation creates the object (§13.6.2).
```

### 2.6. Access and dependencies

A named alias is a declaration under [§9.4.4](../../docs/spec/09-names-signatures-and-access.md#944-api-signature-accessibility),
whatever its access: its API signature is its expanded target with the concrete arguments it contains, group-reference
qualifiers included, after recursive alias expansion. An intermediate alias is not exposed. The declaration checks
access and formation along its target path; a use needs access to the alias, and a member selected through it needs its
own access. An alias never widens access.

```kimi
internal struct Hidden
public alias Exposed => Hidden          // Error: wider than Hidden.

group Api
    private struct Token
    internal alias Key => Token         // Error: Kotonoha(K) is not within Body(Api).

private alias Raw => Kimi.Array<u8>
public alias Bytes => Raw               // Valid: Raw is not exposed.
```

A public alias may target a public Type, group or Kotonoha reference reached through its Kotonoha's direct
dependencies. A consumer reaching the alias through a direct dependency reaches the target by that path (Name
Reachability, [§18.3](../../docs/spec/18-modules-and-dependencies.md#183-name-reachability-across-kotonoha)) but gains no
reference name for the target's Kotonoha. The target keeps its defining environment, version and specialization set.
The alias is its own Kotonoha's declaration, not a re-export, which stays deferred (Appendix D). Changing the normalized
target changes the public API.

```kimi
// Math depends on Units.
public alias Length => Units.Meter

// Geometry depends on Math only.
func keep(value: Math.Length) -> () => ()   // Valid: the parameter Type is Units.Meter.
func lose(value: Units.Meter) -> () => ()   // Error: Units is not a direct dependency.
```

### 2.7. Opening aliases

An opening alias keeps its document-local effect and the rules of
[§18.2](../../docs/spec/18-modules-and-dependencies.md#182-aliases), which this plan changes in two points:

- **Placement.** It precedes every ordinary declaration and executable item of its SourceDocument, judged over selected
  and excluded syntax ([§19.4](../../docs/spec/19-compile-time-directives.md#194-excluded-syntax)). A named alias is an
  ordinary declaration for this rule.
- **Target.** The whole target, Type arguments included, still resolves from the Compilation root without source or
  default aliases, and a path segment that resolves to a named alias is an error. Opening targets therefore never wait
  for a named alias's completion, and opening and named aliases form no cycle.

Opening stays nonrecursive, creates no root member and never exposes enum Cases or receiver members for unqualified
use. Default aliases keep their configuration, precedence and mandatory Kimi target.

```kimi
// A.kimi
alias Kimi.Console
public alias Output => Kimi.Console
writeLine("Hello")                  // Valid: A.kimi opens Kimi.Console.

// B.kimi, in the same Kotonoha
alias Output                        // Error: an opening target never resolves through a named alias.
alias Count => i32
alias Kimi.Console                  // Error: an opening alias follows an ordinary declaration.
Output.writeLine("Hello")           // Valid: Output is a root alias.
```

## 3. Compatibility and integration

This is an intentional pre-alpha change, with no compatibility mode or version bump. Existing named aliases become
ordinary declarations: their scope widens from the document to the declaring Container or Kotonoha, their lookup moves
from stage 5 to the stage of that scope, same-name aliases in one scope become duplicates, the reserved root names of
[§18.4.1](../../docs/spec/18-modules-and-dependencies.md#1841-identities-and-reference-names) apply, and an opening alias
may no longer follow them. Hidden-named-alias warnings give way to ordinary conflicts and lexical lookup. Migrate sources
and expectations by these rules, never by weakening checks: qualify names or move aliases into their owning Containers
where per-file names collide. No file-private modifier and no old lookup route remain.

These are the later integration targets, not edits authorized now:

| Plan rules | Owning definitions to update together |
| --- | --- |
| §2.1–2.2 | SPEC §§1 (the `=>` row), 6.1 (its table, §§6.1.1 and 6.1.3), 6.7, 9.1–9.3, 9.7.1, 18.2; Appendix F declaration grammar and modifiers |
| §2.3 | SPEC §§9.2, 9.5–9.6, 18.1–18.2, 18.4.1: named aliases leave stage 5 and the `::` exclusion |
| §2.4 | SPEC §§3.3.2, 8.1, 9.7.2, 15.3–15.4; Appendix F Type and Origin grammar |
| §2.5 | SPEC §§6.3.2, 6.5.2, 9.1, 9.7, 10.8; audit the consumers in §§8 and 13–16 without changing unrelated rules |
| §2.6 | SPEC §§9.4, 18.2–18.3: qualify that transitive dependencies are not nameable and that Name Reachability needs a direct reference; Appendix D: remove transparent source Type aliases, keep re-exports |
| §2.7 | SPEC §§18.2.1, 19.1, 19.4 |
| §3 | Directly affected examples; support claims in STATUS only after verification |
| §4 | COMPILER_ARCHITECTURE (R7 scope), DECISIONS, CODEMAP and the affected DIAGNOSTICS and HOVER contracts, under their maintenance conditions |

Translations, guides and historical proposals are out of scope; the frozen
[earlier named-alias proposal](../Changes/2026-09-17%20Kimi%20Library%20and%20Named%20Aliases.md) is not edited. The
SETTLED decisions on inherited lookup and signature Origin names are preserved.

## 4. Implementation design

### 4.1. Baseline and sites

Source baseline: `a76673d005ccb36f21d2b895abd005f8d7d49230`. These sites come from source inspection, not execution;
A0 reproduces the changed cases on the actual baseline and rechecks the inventory against
[CODEMAP](../../docs/dev/CODEMAP.md).

| Site | Shared cause or responsibility |
| --- | --- |
| [AliasKoto](../../src/Kimi/Compiler/Parsing/Koto/Declarations/AliasKoto.cs), [GroupKoto](../../src/Kimi/Compiler/Parsing/Koto/Declarations/DeclarationContainers/GroupKoto.cs) | Root-only parsing, no access, two target representations (name segments or Type syntax) |
| [Binding.Aliases](../../src/Kimi/Compiler/Binding/Binding.Aliases.cs) | `namedAliases` keyed by document and name; opening targets and hidden-name warnings share one lifecycle |
| [Binding.Types](../../src/Kimi/Compiler/Binding/Binding.Types.cs), [Binding.ContainerReferences](../../src/Kimi/Compiler/Binding/Binding.ContainerReferences.cs) | `AliasTarget` returns a scope and admits only groups for named targets; stage 5 substitutes them; `aliasResolutionDepth` suppresses stages 5–6 during alias resolution |
| [BindingModel](../../src/Kimi/Compiler/Binding/BindingModel.cs), [Binding.cs](../../src/Kimi/Compiler/Binding/Binding.cs), [Binding.DuplicateDiagnostics](../../src/Kimi/Compiler/Binding/Binding.DuplicateDiagnostics.cs) | Declaration indexing, identity, access, duplicate checks with the same-target alias exception, the `HiddenNamedAlias_Kd` producer |
| [Binding.OriginDeclarations](../../src/Kimi/Compiler/Binding/Binding.OriginDeclarations.cs), [TyCtxt](../../src/Kimi/Compiler/Binding/TyCtxt.cs) | Origin contracts; canonical Types and substitution |
| [SemanticQuery](../../src/Kimi/Compiler/Binding/SemanticQuery.cs), [HoverBuilder](../../src/Kimi/Checking/Hover/HoverBuilder.cs) | Published declaration facts and their display |
| Binding.Imports, Binding.Qualification, Binding.Expressions, Binding.Startup, source writing; old Analysis/Emission (deleted at R6) | Other alias consumers |
| [NamedAliasTest](../../tests/xUnitTest/Tests/NamedAliasTest.cs), [ModuleBindingTest](../../tests/xUnitTest/Tests/ModuleBindingTest.cs), [RuntimeTypeTest](../../tests/xUnitTest/Tests/RuntimeTypeTest.cs) | Positive, rejection, module, hidden-name and bound-alias expectations to migrate |

### 4.2. Design

Required meaning → one ordinary name for a completed Type or reference → the document-name map and the scope-only
result cannot represent it → declaration indexing owns name, access and identity; alias completion owns the target;
TyCtxt owns normalization and substitution → document-local named lookup, duplicate-target exceptions and per-use target
rebuilding become unnecessary.

- **Parser:** owns both forms and their placement. Both carry one target syntax tree; the name-segment representation is
  removed once source writing is preserved.
- **Declarations:** named aliases enter the ordinary declaration table.
- **Completion:** publishes one closed result per alias — a normalized complete Type or a bound group or Kotonoha
  reference — with its declaration identity, role and established obligations. It uses the existing declaration
  dependency and validity mechanism for ordering, cycles and invalidation; null never means both pending and failed.
- **Uses:** a reference keeps the alias symbol for navigation and the canonical target for semantics; enclosing arguments
  are substituted by the existing substitution owner. No alias-specific Type kind, MIR instruction, runtime descriptor,
  conversion or code-generation path exists; MIR consumers trust the published Types.
- **Opening targets:** one explicit root lookup without source or default aliases, rejecting a named-alias segment,
  replaces `aliasResolutionDepth`.

| Alternative | Decision |
| --- | --- |
| Widen `AliasTarget` inside the document-local owner | Rejected: keeps the wrong scope and identity; public aliases would need a second lookup route. |
| Add `alias Name = Type` beside the named-container form | Rejected: two naming syntaxes with two scope contracts. |
| Keep `=>` and choose scope by the target kind | Rejected: identical syntax with different visibility and declaration behavior. |
| Let opening targets traverse named aliases | Rejected: openings would wait for named-alias completion, which can need stage 5 and so every opening of the document; the resulting cycles depend on unrelated declarations. |
| Ordinary declarations with one completed target contract | Selected: shares access, lookup, substitution and dependency validation, and keeps opening semantics distinct. |

Deletions: `namedAliases` and its stage-5 route, `aliasWarnings` and `CompleteAliasWarnings` with the
`HiddenNamedAlias_Kd` producer and contract, `aliasResolutionDepth`, and the name-segment target representation. No mode
flag or side table replaces them; the opening/default machinery stays document-scoped.

Expected effect: fewer alias-specific lookup paths and more ordinary declaration entries; no native runtime work.
Compilation time, allocations and retained memory are unmeasured, so no speedup is promised; actual area sizes and
matched measurements are recorded without forecasts (DECISIONS Q5).

### 4.3. Schedule and units

The plan waives nothing in [COMPILER_ARCHITECTURE](../../docs/dev/COMPILER_ARCHITECTURE.md) and needs no new HIR or
MIR operation vocabulary; any other vocabulary change or exception follows the existing approval and recording rules.
A1–A2 rewrite name lookup, so they form a unit of R7's resolver consolidation and use its owning files; because R7
follows R6, the D5 freeze does not apply. An earlier start needs an owner exception recorded in DECISIONS.

| Unit | Work and completion condition |
| --- | --- |
| A0 — Baseline and design gate | Reproduce the changed cases, inventory every old named-alias path, compare independent implementation designs and review the chosen representation; obtain required approvals before coding. |
| A1 — Behavior-preserving structure | One target syntax representation; alias completion in its owning files; ordinary declaration publication prepared without accepting new forms. Diagnostics, behavior and IR unchanged ([VERIFICATION](../../docs/dev/VERIFICATION.md#behavior-preserving-commits)); committed separately. |
| A2 — Declaration semantics | All named aliases become ordinary declarations with the complete-target contract, lexical resolution, access, cycles, fixed Origins and the opening-target rule; the old route and warning are removed; conflicting expectations change with this plan as the reason. |
| A3 — Consumers and services | Construction, Cases and Patterns, generic and associated substitution, imports, source round trips and incremental invalidation; alias identity and targets published through existing service contracts; obsolete consumers removed; missing facts fixed at their producer. |
| A4 — Completion | Final review, matched compiler and native measurements, the Session with explicitly selected native checks; migration omissions closed; verified support, area sizes and duplication evidence recorded. |

Each behavior unit adds focused regressions. A2 accepts no form whose consumers are incomplete: it completes a missing
common consumer contract itself or keeps the whole form at the located `Unsupported_Kd`. A3 verifies and publishes; it
does not license unsound intermediate behavior. A temporary structure names its removal unit and is gone by A4.

### 4.4. Diagnostics and services

Invalid declarations are Language errors whose typed causes are recorded at the failing check, reusing existing cause
categories where the meaning matches. Hover and inspection show the written alias and its completed target, Semantics
and Origins included. Navigation and rename identify the alias declaration; target navigation identifies the original
declaration. Rebinding after an edit, deletion, generated-document replacement or dependency or configuration change
invalidates aliases, roles and targets. Tooling reads the published facts; there is no second resolver.

## 5. Verification and completion

This revision is documentation only: diff and reference review. Implementation follows
[VERIFICATION](../../docs/dev/VERIFICATION.md) at the lowest observing layer:

| Rule or risk | Required distinguishing evidence |
| --- | --- |
| Syntax and placement | Public and nested declarations; source-writing round trips; rejected Block, enum and Contract positions, parameters and modifiers; an opening after a named alias rejected, excluded directive items included. |
| Ordinary names | Cross-file visibility, `::`, `Dependency.A`, lexical shadowing, access domains, duplicates with equal targets, kind and arity conflicts, inherited qualification, Container/alias ancestor conflicts both ways, independent Value names. |
| Roles and cycles | Type and qualifier targets; no fallback after a target, role or member failure; a group alias not stopping Core lookup; direct, indirect and expanding cycles rejected; nominal recursion and group references accepted. |
| Opening targets | Opening through a named alias rejected, also in a Type argument; member aliases of opened Containers and Kotonoha found at stages 5–6; no opening/named-alias cycle. |
| Transparency | Alias and target Type and Signature equality; different alias candidates stay ambiguous; nested environments and unused outer parameters; shared conformance and static identities. |
| Complete contracts | The full target family, inherited generic and pair bindings, associated projections; a standalone missing Origin rejected; per-call Function Origins accepted; clauses add no assumptions. |
| Origin preservation | Nested Semantics, `owner/A`, `ref/A`, fixed sets, rejected `A during x`; legal and conflicting nested lifetimes; constructor, Case and Pattern bindings never replaced by context. |
| Access and modules | §9.4.4 at every access level, including an internal alias over a private member; a private intermediate alias accepted; a public dependency path without transitive root names. |
| Uses and services | Member, construction, adaptation, base, test and cast boundaries; `A.init()` through `rc/` rejected and creation by adaptation accepted; structured diagnostics, alias and target navigation, Hover and source edits on exact snapshots. |
| Reuse and scaling | Long chains, dependency diamonds, wide imports, cycles and repeated reloads; bounded completion work and retained memory; no stale symbols or roles after failure or regeneration. |
| Generated behavior | Alias and direct spellings give equivalent validated MIR, ABI, results, cleanup and static initialization; representative native O0/O2 execution with unchanged O2 allocation guarantees. |

`NamedAliasTest` and `ModuleBindingTest` own declaration and lookup behavior; the existing Origin, constructor, enum,
Type-relation, service and allocation classes own their rules. The matrix is not repeated at every layer.

The implementation is complete when the whole declaration contract is supported, the obsolete named-alias paths are
removed, independent review has no blocking finding, the required checks pass, and measurements and remaining
uncertainty are recorded. Because the Verify manifest excludes `draft/`, the evidence also records this proposal's Git
blob identity beside the source and configuration identity; Verify is not claimed to hash it.
