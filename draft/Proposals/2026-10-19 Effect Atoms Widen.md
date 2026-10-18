# Effect Atoms Widen

Date: 2026-10-19 (revised the same day after an independent review: findings B1–B4 and N1–N9 applied, §11). Status: proposal
for the owner's decision; not integrated or implemented. Written by R4 unit 2x-S5 of the
[A5 Step 2 Implementation Plan](<2026-10-18 A5 Step 2 Implementation Plan.md>) under the standing rule OD7 of the
[Complete Polonius A5 Plan](<2026-10-16 Complete Polonius A5 Plan.md>) (A5P; DECISIONS R4-M35): a rule that is not finitely
expressible gets its own SPEC proposal. The owner adopted S5 on 2026-10-19 (OQ13 (a), DECISIONS R4-M74) as A5P §5.2 words it:
an effect atom on an associated projection that grows under polymorphic recursion widens. This proposal makes the rule precise in
two forms and recommends the one that drops the word "grows" (§9, Q1). Changes SPEC §4.9.6, §8.3.3, §15.6.4, §16.3.3 and
Appendix E.

Base: `48a42168` (`dev`). Line numbers refer to that commit; SPEC, `docs/impl` and A4 are unchanged up to `d55bce08`. "(H)" marks
an inference not checked by running code. Evidence: the model [S5EffectWidening.cs](<../Sketches/A5 Step 2/S5EffectWidening.cs>)
and the probe [S5Probe.cs](<../Sketches/A5 Step 2/S5Probe.cs>) (§10).

## 1. Problem

An effect summary "is computed once per declaration, in terms of its own Type parameters, as the least fixed point over
recursive calls" ([§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls), line 596). Besides static Fields it
keeps **parameter-dependent effects**: the destruction of a value of a Type parameter or associated-Type projection not proven
Copy ([§16.3.3](../../docs/spec/16-scope-exit-and-destruction.md#1633-handles-and-object-release), line 196;
[§4.9.6](../../docs/spec/04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects), line 568), the user-code
effects of published operations on such a Type (§4.9.6) and calls through Callable inputs (line 596). Callers compose them with
the actual Types. Under polymorphic recursion through an associated Type the domain is unbounded:

```kimi
func walk<T>(node: T, depth: i32)
    T is Tree                                       // contract Tree: associate Child is Tree
    if depth == 0 => return
    match node.child()
        .Some(let next) => walk(next@move, depth - 1) // T := T.Child
        .None => return
```

`walk` destroys a `T`; through the recursive call that is a `T.Child`, then a `T.Child.Child`, and so on. The least fixed point
has infinitely many atoms and no finite form. The cycle need not be a call: a summary also includes "defaults, accessors, lazy
initialization and cleanup" (line 596), and a `drop` body that calls a helper which destroys a `Holder<T.Child>` grows the same
way. Before P5, §15.6.4 cut such growth for static keys: "When a static key passes through a recursive call and substitution does
not map it to itself, the summary records the open key of its Field instead" (`4372b058^`, line 531). P5 made all keys of one
Field one root, so that sentence went and nothing replaced it for parameter-dependent effects. Widening without a SPEC rule would
be a conservative Language error (A5P §5.1).

**Today.** A4 (`MirEffects`) has no growth rule. Each round of the worklist composes one more projection step until
`TyCtxt.Apply` (Binding's storage instantiation, depth 64, PLAN G128) returns no Type; the atom then becomes a Limit
(`src/Kimi/Compiler/Mir/Analysis/MirEffects.Solve.cs:280` and `:300`), the located `Unsupported_Kd` where a bound is owed. The
probe's `walk` case: 65 atoms per summary (depths 0–64), Limit in `walk` and its caller, 338 evaluations against 239 for a plain
recursion. The cleanup cycle (probe rv-F, from the review) reaches the same Limit in all three bodies with no recursive
call edge: A4 enters destructor bodies through `Enter` (`MirEffects.Solve.cs:425`), not through its call edges.

## 2. Proposal

S5 records **unknown environment effects** in place of an effect that could grow. Two forms decide which effects: (c) every
parameter-dependent effect on a projection with a recursive base, recommended; (a) only such an effect that passes along a cycle
of summary dependencies without being mapped to itself, which keeps OQ13's word "grows". Both share the definitions of §2.1.

### 2.1. Shared definitions

- **Recursive Contract.** A Contract is recursive when conjunction elimination of the Constraints on its associated Types
  (declared, inherited, or placed on inherited ones by
  [§8.5.3](../../docs/spec/08-generics-constraints-and-contracts.md#853-constraints-on-inherited-associated-types)) yields a
  conformance to the same Contract declaration, with any Type arguments, or to a Contract that refines it, directly or through
  the Constraints, counted the same way, of the Contracts so yielded. `contract Seq<E>: associate Next is Seq<Option<E>>` is
  recursive though `Seq<E>` and `Seq<Option<E>>` are distinct bound Contract references
  ([§8.4.1](../../docs/spec/08-generics-constraints-and-contracts.md#841-contract-declarations), line 190). §8.3.3 (line 162)
  already says that no concrete Type can be proven to conform to such a Contract (H: a concrete proof would need an infinite
  descending chain of conformance proofs, or would use itself, which §8.3.3 forbids); disjunctions are excluded because a
  conformer may satisfy another disjunct.
- **Recursive base.** A projection has a recursive base when one of its bases (its base, that base's base, and so on) is proven
  by the declaration's premises to conform to a recursive Contract. "Any base" matters: `T.(Tree3).Item.(Box).Inner`, with
  `Tree3.Child is Tree3` and `Tree3.Item is Box`, grows under recursion on `T.Child` although its immediate base `T.Item` is a
  `Box` (review B1; model case 13, probe rv-B).
- **Widening.** The summary has unknown environment effects in place of the widened effect. That is an existing summary property
  (a callee's unknown call or task call gives it), so §15.6.4's second bullet (line 599: a conflict with every live Loan anchored
  in mutable static storage) and §8.11.2's row "A call or a destruction whose environment effects are unknown" (a conflict under
  either bound) apply unchanged. `drop1<T>(value: T)` called as `drop1(5)` keeps its exact effect: `T` is a Type parameter, not a
  projection, and the destruction composes with `i32`.

### 2.2. Form (c), recommended: every effect with a recursive base

When a summary would keep a parameter-dependent effect on a projection with a recursive base under its own declaration's
premises, it has unknown environment effects instead. A destruction of a Type the premises prove Copy is no effect and is not
kept. In `walk`, `T` is kept; the destruction of `T.Child` that the recursive call composes has the recursive base `T` (a `Tree`),
so `walk`'s summary is `{destruction of T}` plus unknown environment effects.

The test reads one Type and the declaration's premises. It needs no notion of recursion or of "mapped to itself", and it holds
for every way a summary includes another (calls, specialization sets, defaults, accessors, lazy initialization, cleanup). It
also widens effects that do not grow (§4 item 3), all in declarations no concrete Type can instantiate. It departs from OQ13's
word "grows".

### 2.3. Form (a): growing effects only

A summary **depends** on each summary it includes: its callees', those of every implementation in its closed
explicit-specialization sets, and those of the defaults, accessors, lazy initialization and cleanup it runs, a destroyed Type's
`drop` included. When a parameter-dependent effect of a summary it depends on, on a projection with a recursive base under that
summary's declaration's premises, passes along a dependency that lies on a cycle of dependencies and substitution does not map its
Type to itself, the summary has unknown environment effects in its place. An effect passed from one declaration to another is
never mapped to itself.

In `walk`, the destruction of `T` passes the recursive call as `T.Child` (its source is a Type parameter, so it is kept); the
destruction of `T.Child` then passes as `T.Child.Child`, not itself, and is widened. The first draft said "passes through a
recursive call"; the cleanup cycle above has no recursive call, and the model's S5A1 (call edges only) does not terminate on it
(case 15) or on 1,197 random programs (§10) (review B3).

### 2.4. Why the fixed point is finite

Every step of a projection needs conformance evidence for its base
([§8.5.2](../../docs/spec/08-generics-constraints-and-contracts.md#852-projections), line 269). In a declaration's scope that
evidence comes from its finitely many premises, from identity premises (which normalize the projection) or from the
associated-Type Constraints of a Contract proven for the next shorter base. A projection deeper than the deepest premise plus the
number of Contract declarations therefore draws its evidence from a chain of Constraints that repeats a Contract declaration, and
that Contract is recursive under §2.1's definition. So the effects with no recursive base have bounded depth and are finitely
many in each declaration.

- Under (c) these are the only projection effects a summary keeps, whatever its dependencies are.
- Under (a), an effect with a recursive base enters a summary only as a local effect, along a dependency that lies on no cycle,
  as the image of one of the bounded effects, or mapped to itself. This holds only if "depends" covers every way §15.6.4 includes
  a summary.

(H) as a proof. The model checks it on 50,000 random well-formed programs, whose grammar has two associated Types under one
Contract, constructors with `drop` bodies and no Contract Type arguments: 7,650 had an infinite exact fixed point, each with an
atom with a recursive base, and both forms terminated on all (§10). Recursive Contracts with Type arguments (rv-A) are covered by
the argument only.

The probe confirms the premise side on the compiler at the base: recursion `unwind(inner)` with `T := T.Inner` is
`UnprovenConstraint_Kd` at the recursive call when `Inner` is unconstrained, and also with the premise `T.(Layer).Inner is
Layer`, which does not reach `T.Inner.Inner`; `Node: Tree` with `Child is Node` is `UnprovenConstraint_Kd` (§8.3.3's example).

### 2.5. Soundness

Unknown environment effects conflict wherever the replaced effects could: under `confined` and `preserves results` (§8.11.2)
and with every live Loan anchored in mutable static storage (§15.6.4). A summary covers static storage only (§15.6.4); the
effects through inputs come from the signature (§8.11.4 rule 1, §12.7.1 "Exclusive input bound"), and Loans in immutable statics
are never affected. So neither form accepts anything the current letter rejects; each only rejects more (§4). (H) for §12.7.1's
receiver statuses.

### 2.6. SPEC text

**§8.3.3, line 162.**

- Now: "When a Contract's associated-Type Constraint leads back to the same Contract, the Contract is valid, but no concrete Type
  can be proven to conform."
- New: "A Contract is **recursive** when conjunction elimination ([§8.3.2](#832-proof-rules)) of the Constraints on its
  associated Types, those it declares or inherits and those it places on inherited ones
  ([§8.5.3](#853-constraints-on-inherited-associated-types)), yields a conformance to the same Contract, with any Type arguments,
  or to a Contract that refines it, directly or through the Constraints, counted the same way, of the Contracts so yielded. A
  recursive Contract is valid, but no concrete Type can be proven to conform." The `Tree`/`Node` example stays.

**§15.6.4, line 596**, after "…as the least fixed point over recursive calls.":

- Insert, form (c): "Its **parameter-dependent effects** are those it keeps in terms of a Type parameter or an associated-Type
  projection: destructions ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)), the user-code effects
  of published operations on such a Type
  ([§4.9.6](04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects)) and calls through Callable inputs. When a
  summary would keep one on a projection one of whose bases (its base, that base's base, and so on) the declaration's premises
  prove to conform to a recursive Contract ([§8.3.3](08-generics-constraints-and-contracts.md#833-recursion-and-finality)), it has
  unknown environment effects instead. The other parameter-dependent effects have a bounded projection depth, so the fixed point
  is finite."
- Or, form (a): the same first sentence, then: "A summary **depends** on each summary it includes: its callees', those of every
  implementation in its closed explicit-specialization sets, and those of the defaults, accessors, lazy initialization and
  cleanup it runs, a destroyed Type's `drop` included. When a parameter-dependent effect of a summary it depends on, on a
  projection one of whose bases (its base, that base's base, and so on) that summary's declaration's premises prove to conform to
  a recursive Contract ([§8.3.3](08-generics-constraints-and-contracts.md#833-recursion-and-finality)), passes along a
  dependency that lies on a cycle of dependencies and substitution does not map its Type to itself, the summary has unknown
  environment effects in its place; an effect passed from one declaration to another is never mapped to itself. The effects a
  summary keeps then have a bounded projection depth, so the fixed point is finite."

**§15.6.4, after the `run` example (line 622).** A new example, an error only under S5 (review N1):

```kimi
contract Tree
    associate Child is Tree       // Recursive (§8.3.3).
    func child(self: ref/Self) -> Option<Self.Child>
        effect confined

contract CopyTree: Tree, Copy
    Self.(Tree).Child is CopyTree // Every U.Child… of a CopyTree U is Copy.

group Trees
    public func walk<T>(node: T, depth: i32)
        T is Tree
        if depth == 0 => return
        match node.child()
            .Some(let next) => Trees.walk(next@move, depth - 1)
            .None => return
// walk's summary has unknown environment effects: the widening above.

group Counters
    public var count: i32 = 0

func visitCopies<U>(node: U)
    U is CopyTree
    let c = Counters.count@ref
    Trees.walk(node, 2)           // Error: walk's environment effects are unknown and conflict with c.
    inspect(c)
```

Under the current letter `visitCopies` is valid: every Type `walk<U>` destroys is a `U.Child…`, proven Copy. The example `visit`
of the first draft (`U is Tree`) is an error under the letter too, so it does not show S5.

**§16.3.3, line 196**, last sentence: "…the body's summary publishes it as a parameter-dependent effect that callers compose
with the actual Type ([§4.9.6](…))" gains ", unless [§15.6.4](…) widens it".

**§4.9.6, line 568**: "Parameter-dependent destruction is composed at callers for Loan checking" gains ", unless
[§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls) widens it" (review N8).

**Appendix E.** Two rows: "Parameter-dependent effect | An effect a summary keeps in terms of a Type parameter or associated-Type
projection, composed by callers with the actual Type unless §15.6.4 widens it. | §15.6.4" and "Recursive Contract | A Contract
whose associated-Type Constraints lead back to its declaration; no concrete Type conforms. | §8.3.3".

No change: §15.6.4 line 599 and §8.11.2 (unknown environment effects are already covered; the first draft's edit of line 599,
"a call whose callee's summary records an effect that cannot be classified", read with §8.11.2's abstract-destruction row, would
have made every call of a generic destroyer such as `drop1(5)` unknown; review B4), §8.11.4, §8.11.6 (a Function Item's evidence
is its summary), §12.7.1 line 220 (it takes "the summary domain of §15.6.4"; (H) that it needs no own sentence), §15.3.5 and
§8.15.3 (concrete growth such as `rc/Nest<(T, T)>?` or `f<Wrap<T>>` stays finite per declaration; its implementation bound is
PLAN G128 and the destruction-walk bound, separate O4 items).

## 3. Examples

Invalid under both forms and under the current letter: `visit<U>`, as `visitCopies` but with `U is Tree` and `walk(node@move, 2)`.
`walk<U>`'s exact effects are destructions of `U`, `U.Child`, …; none is proven Copy, so they are unknown for `visit`'s own Loan
checks (§16.3.3).

Valid and exact under both forms (Source is not recursive, so nothing widens; the probe's `ping` case keeps `L.Item` and
`R.Item`):

```kimi
func ping<L, R>(left: ref/L, right: ref/R, budget: i32)
    L is Source                   // contract Source: associate Item (no Constraint)
    R is Source
    if budget == 0 => return
    let item = left.make()        // Destroys an L.Item.
    ping(right, left, budget - 1) // L.Item passes as R.Item: exact.

func run(a: ref/Name, b: ref/Name) // Name is Source, Item is string.
    let c = Counters.count@ref
    ping(a, b, 2)                 // Valid: destroying a string has no static effect.
    inspect(c)
```

Valid and exact under both forms: an identity premise `T.(Tree).Child is T` makes `T.Child` normalize to `T` (model case 2);
under `T is CopyTree`, `T.Child` is Copy, so its destruction is no effect at all (cases 7 and 8).

Growing, so widened by both forms: mutual recursion through `A.X is B` and `B.Y is A` (case 3, probe `mutual`); a conformance
rule `Wrap2<X>.I is X.I.I` (case 6); a refinement `Rec: Node` with `Self.(Node).Child is Rec` (case 10); the deep base of §2.1
(case 13, rv-B); `Seq<E>` (rv-A, argument only); the cleanup cycle (case 15, rv-F). The probed ones (`mutual`, rv-A, rv-B,
rv-F) reach the depth-64 Limit today.

## 4. What is lost

Each form is valid under the current letter and an error under S5. All are in declarations whose premises name a recursive
Contract, which no concrete Type satisfies (§8.3.3): they can never be instantiated, yet they are checked
([§8.15.1](../../docs/spec/08-generics-constraints-and-contracts.md#8151-universal-verification), line 955). A5P §5.2 described
the loss as callers whose concrete instances would classify the effect; by §8.3.3 no such concrete instance exists, so the losses
are generic callers whose premises are stronger than the callee's.

1. **Both forms: callers whose premises classify every effect of a growing chain** (SPEC: §15.6.4 effect summaries, §16.3.3,
   §8.11.2): `visitCopies`, and a `confined` implementation under such premises (`struct Holder<T>` with `T is CopyTree` calling
   `walk` in a `Sink.put`; the probe's second case). Binding and A4 both reject that witness today
   (`IncompatibleContractImplementation_Kd`), so S5 changes no default-pipeline verdict there (review N4); A4's rejection is the
   side finding of §10 (H).
2. **Form (a) also: finite orbits it widens** (SPEC: §15.6.4): an effect on a projection with a recursive base passed between two
   declarations of one cycle, mapped to another such projection (`f<L: Tree, R: Tree>` calling `f(r, l)` while destroying an
   `L.Child`), or mapped to a concrete Type that destroys it (case 14, `Wrap<X>.Child is Pair<X.Child>`).
3. **Form (c) also: stronger callers of any effect with a recursive base, growing or not** (SPEC: §15.6.4): `h<T: Tree>` calling
   `k(t.child())` that destroys its `U`, used by `g<V: CopyTree>` (case 16); `f<T: Tree>` destroying a `T.Child` and recursing on
   `T` itself, used the same way (case 17). Item 2's effects have a recursive base, so form (c) widens them too: item 2 is part of
   item 3.

Counts for every form: library 0, milestones 0, SPEC examples 0, xUnit 0 (H: the grep-level scan of §5). Kept by both: every
program without a recursive Contract (model cases 4, 5, 11, 12 and every random program with no atom with a recursive base), and
every effect on a Type its premises prove Copy (cases 7, 8).

## 5. Census

Probe at the base (§10), mir pipeline; the library is the empty-source compilation of `MirAnalysisTest.TheLibraryHasNoProblems`.

| Measure | Library | Milestones 1–44 |
| --- | ---: | ---: |
| A4 body views | 214 | 350 |
| Views with a Limit (any growth reaches one, by any dependency kind) | 0 | 0 |
| Recursive call edges (calls only; cleanup cycles are invisible here) | 0 | 0 |
| Summary atoms on Type parameters / on projections | 102 / 0 | 24 / 1 (Milestone20 `readTwice`, `T.Element` of a non-recursive `Source`) |
| Recursive Contracts (H: grep-level scan) | 0 | 0 |
| Verdicts form (a) changes; form (c) changes | 0; 0 | 0; 0 |

Form (a) changes nothing in the library or the milestones because nothing grows there: the "Limit 0" row carries this, as every
growth reaches G128's Limit (review B3; the call-edge row cannot see cleanup cycles). Form (c) changes nothing because no atom has
a recursive base: no recursive Contract exists there, and the one projection atom belongs to a non-recursive `Source`.

Milestones 39, 41 and 43 have 20 Binding issues between them (Binding-incomplete, known), so A4 sees only their completed bodies;
none declares an associated Type. The probe's scan (`Benchmark contracts`) reads each `contract` block of a `.kimi` file, a fenced
block of a `.md` file or a physical line of a `.cs` or `.json` file, follows the Contract names on the right of its associated-Type
Constraints and its parents within that unit, Type arguments ignored, and reports §2.1's recursive Contracts, through other
Contracts included. Over the library, the milestones, `tests/mir`, `tests/diagnostics`, `docs/spec`, `docs/impl`,
`tests/xUnitTest` and `tests/Workloads` it finds 3, all naming themselves: SPEC §8.3.3's `Tree` (a declaration without calls) and
xUnit `AssociatedRefinementCapabilityBindingTest.cs:77` `Trait.Next` and `ConditionalConformanceBindingTest.cs:170` `C.E` (neither
with a call). A declaration split over several C# lines is not followed (H). By the reviewer's reading, no library Contract is
recursive (Iterator, LendingIterator, IntoIterable, Iterable, UniqIterable, Indexable and Comparable checked). This replaces A5P
§5.2's "(H) lib 0, ms 0"; step 0's `census.md` has no S5 count.

## 6. Alternatives

- **A depth limit whose excess is a Resource failure** (A5P §5.2's alternative; OQ13 (b)). Every growing chain is infinite, so
  every limit is exceeded wherever a verdict needs the summary: it accepts nothing S5 rejects (model: DEPTH is Resource wherever
  EXACT is infinite, including case 1's `copied`). It also fails finite programs deeper than the limit that S5 computes exactly
  (case 12: a five-step chain of non-recursive Contracts, instantiable, Resource at limit 4). Its result is implementation-defined
  and no larger limit repairs it (Principles 2 and 4). Today's implementation is this option with limit 64 and `Unsupported_Kd`.
- **The open-key form as written** ("a projection effect that passes along a cycle and is not mapped to itself"). It widens
  instantiable programs: case 4 (`ping`) becomes Unknown, and so would any mutual recursion passing an `Item` along.
- **Exact summaries** (Q1's (b)): the set of grown effects as a regular set of projection paths, resolved at callers by induction
  over Contract Constraints. The image set is decidable (H: prefix rewriting), but it is a second effect representation and a
  proof rule for code that can never run.
- **Undecided at R4's exit** (OQ13 (c)): growing atoms stay a located `Unsupported_Kd`, an O4 item in OQ9.

## 7. Principles

- **One Concept, One Canonical Form.** The widened effect becomes the same unknown environment effects as an unknown call; no new
  code, effect kind or representation. Form (c) adds one predicate on a Type, "has a recursive base"; form (a) adds that predicate
  and the dependency graph with its cycles.
- **Local Reasoning.** Under (c) the test reads one Type and the premises of the declaration that keeps it; under (a) it also
  needs the cycles of the dependency graph, which a reader finds by following calls, specializations, defaults, accessors and
  `drop` bodies. Both make the fixed point finite without a work budget (§2.4).
- **Explicit Semantics.** The widening is a SPEC sentence; its consequence is the stated rule for unknown effects. The losses are
  listed (§4) and confined to declarations that no concrete Type can instantiate.
- **Compiler Server Protocol.** The conflict is reported at the call with the existing facts; the effect that was widened, and
  under (a) the dependency where it grew, can be related locations (H: facts A4 can publish at no cost).

## 8. Implementation and tests

- **A4 (`MirEffects`), form (c).** Where an atom would be kept (`AddAtom`, `MirEffects.Solve.cs:432`, and the destruction walk
  that reaches it), a projection with a recursive base under the definition's premises, not proven Copy for a destruction,
  records Unknown instead. A Binding view answers "recursive Contract" (once per Contract declaration) and "a base proven to
  conform to one" (per projection under the definition's premises); per PD6 a new R4 view with a BU reviewer. (H) A4 +10 lines,
  Binding +30, no new MIR vocabulary or diagnostic code. Growth then costs about one extra evaluation instead of 64 rounds; V16
  stays linear (H).
- **A4, form (a).** The same view, plus the dependency graph with its cycles during the worklist. A4's call edges are fixed
  before it, but its destructor, published-row, Closure and witness entries (`Enter`, `Require`) are found while atoms are
  composed, so the cycles must be maintained as the graph grows, then the self-map test at each composition. (H) A4 +60 lines.
- **Interim (PD30).** Until the SPEC integration a growing atom is already a located `Unsupported_Kd` (today's Limit at G128's
  bound; A5-F5's 2b-3 maps Limit to `Unsupported_Kd` at the call). Form (c)'s other losses (§4 item 3) stay accepted until the
  unit after the SPEC session, which adds the test and records Unknown in one step. Its codes are existing ones:
  `IncompatibleContractImplementation_Kd` for `confined`, `UnsatisfiedEffectBound_Kd` for a Callable bound,
  `ComparisonLoanConflict_Kd` at a call against a live mutable-static Loan (R4-M72).
- **Old path.** Unchanged (OD5: SPEC changes on MIR only; D5).
- **Tests.** `MirEffectsTest` rows for model cases 1 (growth), 3 (mutual), 4 (swap kept exact), 10 (refinement), 12 (a deep
  finite chain with no limit), 13 (deep base), 15 (cleanup cycle) and, for form (c), 16 or 17 (a stronger caller); the growing
  rows expect the Language codes and fail before the change, which gives a Limit. A `MirBorrowsTest.Effects` row for a growing
  call against a live implicit static Loan belongs to 2b-3. The new SPEC example joins `tests/diagnostics/spec-cases.json` like
  §15.6.4's `run` (`15.6.4-static-anchors`, with its `inspect` prelude; without it the probe reports `UnresolvedBinding_Kd`): it
  expects `ComparisonLoanConflict_Kd` at the call, with the same status for the explicit static borrow (PLAN G100, P42).

## 9. Owner question

**Q1. Which form of S5.** OQ13 adopted "an effect atom on an associated projection that grows under polymorphic recursion
widens". R4-M74: a review finding that changes the rule returns to the owner. The review found that a growth test must follow
every summary dependency, cleanup included (B3), not calls, which makes form (a) longer than the first draft assumed.

| | (c) every effect with a recursive base | (a) growing effects only | (b) exact growth |
| --- | --- | --- | --- |
| SPEC | §2.1's definitions plus one sentence | the same plus a definition of summary dependencies and their cycles, and the self-map rule | a regular set of projection paths per summary and an induction rule at callers |
| A4 | a test where an atom is kept (+10, H) | cycles maintained during the worklist and a self-map test (+60, H) | a second effect representation |
| Finiteness | §2.4's bound alone, for any dependency kind | needs every dependency kind in "depends" | exact |
| Losses (all uninstantiable) | §4 items 1 and 3 | §4 items 1 and 2 | none |
| Counts: lib, ms, SPEC, xUnit (H) | 0, 0, 0, 0 | 0, 0, 0, 0 | 0, 0, 0, 0 |
| OQ13's "grows" | departs from it | keeps it | keeps it |

- **(c), recommended.** It is shorter, local and robust: its finiteness does not depend on finding every dependency kind, which
  the first draft got wrong. It is as precise as (a) on every instantiable program, with the same counts. **It departs from
  OQ13's word "grows"**: it also widens effects with a recursive base that do not grow (§4 item 3), which matters only in
  declarations no concrete Type can instantiate.
- **(a)** if the owner keeps "grows": exact on instantiable programs as well, with the extra definition and A4 work above.
- **(b)** is not recommended: a second effect representation for code that can never run.

## 10. Evidence and integration

- **Model.** `S5EffectWidening.cs`: summaries over Type parameters and projections with normalization, conformance rules, premise
  proofs (no coinduction), Copy, destruction resolution and `drop` bodies as dependencies, under EXACT (the letter, with a depth
  cap marking an infinite fixed point), S5C (form (c)), S5A (form (a), self-map on the substituted Type as §2.3 says; review N2),
  S5A1 (the first draft's call edges), IMM (the immediate base only), the open-key form and a depth limit. Copy it to an empty
  folder outside the repository and run `dotnet run S5EffectWidening.cs` (.NET SDK 11.0.100-rc.2, about 25 s). Result at the
  base: 17 hand cases (13 and 14 are the review's), S5C and S5A equal to the exact verdict on every instantiable one (4, 5, 11, 12)
  and terminating on all; IMM does not terminate on case 13, S5A1 on case 15. Random: 50,000 well-formed programs, 37,783 with a
  recursive Contract, 25,010 with a `drop` body, 7,650 with an infinite exact fixed point; 0 failures (S5C and S5A always
  terminate; every infinite exact fixed point has an atom with a recursive base; where no atom has one, S5C and S5A equal EXACT);
  S5A1 does not terminate on 1,197 and IMM on 204. The grammar has no Contract Type arguments (§2.4), and the model treats every
  parameter-dependent effect kind alike and does not model Origins or Semantics cases.
- **Probe.** `S5Probe.cs`: the `Program.cs` of a scratch console project named `Benchmark` that references a copy of `Kimi.dll`
  built at the base (as in `artifacts/handoff/2026-10-18-pause/r4-scratch/1c-design/probe`). `Benchmark cases` runs its 16
  programs (the review's rv-A, rv-B and rv-F included; the outputs quoted in §1–§4 and below), `Benchmark census <repository>`
  the census of §5 and `Benchmark contracts <root>...` its scan (bin/ and obj/ are skipped relative to each root, so absolute
  roots under `bin/worktrees/` work; review N6). It reads A4's private state by reflection.
- **Integration.** A separate SPEC session after the owner's answer to Q1 (R4-M74) applies §2.6 with the chosen form and adds the
  example's spec case; INTEGRATED records the intake; SPECJ and GUIDE follow only on explicit instruction (SPECJ's §15.6.4 still
  has the pre-P5 open key). PLAN: OQ9's S5 item and the unit of §8 are the planning session's.
- **Side finding outside S5** (the probe's last three cases; reproduced by the review, N9). The mir pipeline does not prove
  `T.(Tree).Child.(Tree).Child` Copy under `T is CopyTree`, whose Copy comes through a Constraint on an inherited associated Type,
  while Binding does: A1 reports `MovedPlace_Kd` for `(value, value)` of that Type, and A4 reports
  `IncompatibleContractImplementation_Kd` for `Holder<T: CopyTree>` calling a non-recursive helper chain that destroys a
  `T.Child.Child`; Binding accepts both, and the chain one step shorter passes. These are Language rejections of valid forms in
  the mir pipeline. A4's Copy query is Binding's own `ProveCopy` under `EffectPremises(definition)`
  (`src/Kimi/Compiler/Binding/TyCtxt.Effects.cs:59`, `Binding.EffectObligations.cs:119`), so the cause more likely lies in that
  premise scope or in the Type form MIR passes (Binding, BU) than in A4; not diagnosed. An open-issue candidate for the planning
  session.
- **Not verified.** §2.4 as a proof (argued; model-checked without Contract Type arguments); §12.7.1's reading; recursion through
  Closures, accessors, defaults or specialization sets (argument only); whether rv-F's `drop` form is valid by every clause of §16
  (Binding accepts it); the A4 and Binding sizes; the scan's completeness for xUnit; SPECJ.

## 11. Review record

The independent review of `3d2fd284` found four blocking and nine non-blocking findings, all applied:

- B1 "a base" means any base (§2.1, both SPEC sentences, model case 13, probe rv-B).
- B2 a recursive Contract ranges over its declaration with any Type arguments, and the definition now names a conformance to the
  same Contract or a refining one (§2.1, §2.6, §2.4 counts declarations, probe rv-A).
- B3 a growth test follows summary dependencies, cleanup included (§2.3, the model's `drop` bodies, case 15, probe rv-F; §5's
  "Limit" row carries the census).
- B4 the widening records unknown environment effects; the line-599 edit is dropped (§2.1, §2.6).
- N1 the SPEC example is `visitCopies`. N2 the model's self-map compares the substituted Type. N3 `L.Child`. N4 Binding rejects
  the confined witness today. N5 counts per form (§9). N6 the scan works with absolute roots, follows other Contracts and is
  labelled (H). N7 the random grammar's limits are stated and extended (a second associated Type, `drop` bodies). N8 §4.9.6
  gains the clause. N9 the side finding's cause is worded neutrally.
- With B3 the recommendation moved from (a) to (c), as the review suggested; the Copy rule (a Copy destruction is no effect)
  was made explicit in the model, which removed the first draft's losses at cases 7 and 8.
