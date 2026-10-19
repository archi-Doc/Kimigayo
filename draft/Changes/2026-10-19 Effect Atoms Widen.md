# Effect Atoms Widen

Date: 2026-10-19, revised twice the same day after independent reviews (§11). Status: adopted by the owner on 2026-10-19 as form (c)
(DECISIONS R4-M74, R4-M76) and integrated into SPEC on 2026-10-19 (`draft/INTEGRATED.md`); not implemented. Written by R4 unit 2x-S5 of the
[A5 Step 2 Implementation Plan](<2026-10-18 A5 Step 2 Implementation Plan.md>) under the standing rule OD7 of the
[Complete Polonius A5 Plan](<2026-10-16 Complete Polonius A5 Plan.md>) (A5P; DECISIONS R4-M35): a rule that is not finitely
expressible gets its own SPEC proposal. The owner adopted S5 on 2026-10-19 (OQ13 (a), DECISIONS R4-M74) as A5P §5.2 words it:
an effect atom on an associated projection that grows under polymorphic recursion widens. After the second blocking review round
on the definition of a recursive Contract, **the owner chose on 2026-10-19 the form (c) below, with "recursive" defined through
the existing proof rule** (DECISIONS R4-M76, the owner's answers of 2026-10-19, which supersedes R4-M74 OQ13's "grows"). That
form departs from OQ13's word "grows": it widens every parameter-dependent effect on a projection with a recursive base, growing
or not (§9). Changes SPEC §4.9.6, §8.3.3, §15.6.4, §16.3.3 and Appendix E.

Base: `48a42168` (`dev`). Line numbers refer to that commit; SPEC, `docs/impl` and A4 are unchanged up to `da9ea8f7`. "(H)" marks
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
recursion. The cleanup cycle (probe rv-F, from the review) reaches the same Limit in all three bodies with no recursive call edge:
A4 enters destructor bodies through `Enter` (`MirEffects.Solve.cs:425`), not through its call edges.

## 2. Proposal

A summary has **unknown environment effects** in place of every parameter-dependent effect on a projection with a recursive base
(form (c), the owner's choice). The test reads one Type and the premises of the summary's declaration; it needs no notion of
recursion, so it holds for every way a summary includes another.

### 2.1. Definitions

- **Recursive Contract.** A Contract `C` is recursive when `T is C` supplies
  ([§8.3.2](../../docs/spec/08-generics-constraints-and-contracts.md#832-proof-rules): `C`'s own and inherited Constraints, with `T`
  for `Self`), directly or through the Constraints of the Contracts so supplied, a conformance of a projection of `T` to `C`'s
  declaration, with any Type arguments, or to a Contract that refines it. This covers, without listing Constraint kinds:
  - `contract Tree: associate Child is Tree` and `CopyTree: Tree, Copy` with `Self.(Tree).Child is CopyTree` (§8.5.3);
  - `A.X is B` with `B.Y is A` (each supplies the other's conformance for a projection);
  - `Rec: Node` with `Self.(Node).Child is Rec`, a Constraint on an inherited associated Type;
  - `contract Seq<E>: associate Next is Seq<Option<E>>`, though `Seq<E>` and `Seq<Option<E>>` are distinct bound Contract
    references ([§8.4.1](../../docs/spec/08-generics-constraints-and-contracts.md#841-contract-declarations), line 190; probe rv-A);
  - `contract A: associate X is B; Self.(A).X.(B).Y is A` with an unconstrained `B.Y`: `T is A` supplies `T.X.Y is A`, a
    Constraint on a projection of Self two steps deep (review B5; probe rv-G, model case 18). (H): SPEC does not state outright
    that such a two-step clause is valid. Appendix F admits it (`ConstraintSubject := Name | "Self" | AssociatedTypeReference`,
    lines 405, 419, 421), §6.1.4 (line 94) makes a Constraint on the conforming Self an implementation requirement, and Binding
    accepts it and proves `g`'s premise from it. If the SPEC session rules it invalid, the case vanishes; the definition is
    unchanged.

  §8.3.3 (line 162) already says that no concrete Type can be proven to conform to such a Contract (H: a concrete proof would
  need an infinite descending chain of conformance proofs, or would use itself, which §8.3.3 and Finality forbid; Binding rejects
  every concrete conformer the reviews tried: `Node: Tree`, rv-G3, and conditional and polymorphic self-uses rv-H1 to rv-H3).
  Disjunctions supply nothing (§8.3.2), so a Constraint `Child is Tree or Leafy` makes no Contract recursive.
- **Recursive base.** A projection has a recursive base when one of its bases (its base, that base's base, and so on) is proven
  by the premises of the summary's declaration to conform to a recursive Contract. "Any base" matters: `T.(Tree3).Item.(Box).Inner`,
  with `Tree3.Child is Tree3` and `Tree3.Item is Box`, grows under recursion on `T.Child` although its immediate base `T.Item` is a
  `Box` (review B1; model case 13, probe rv-B). A `drop` body is a summary too, so its declaration's premises are the struct's.
- **Widening.** The summary has unknown environment effects in place of the widened effect. That is an existing summary property
  (a callee's unknown call or task call gives it), so §15.6.4's second bullet (line 599: a conflict with every live Loan anchored
  in mutable static storage) and §8.11.2's row "A call or a destruction whose environment effects are unknown" (a conflict under
  either bound) apply unchanged. `drop1<T>(value: T)` called as `drop1(5)` keeps its exact effect: `T` is a Type parameter, not a
  projection, and the destruction composes with `i32`.

### 2.2. The rule

When a summary would keep a parameter-dependent effect on a projection with a recursive base, it has unknown environment effects
instead. A destruction counts only for a Type not proven Copy: a Copy value has no destruction effects (§16.3.3 "unless the Type
is Copy", §8.11.2's row "not proven Copy" and line 767, §3.5.2), and a callee's Copy proof carries to every caller, because a call
proves the callee's premises ([§8.15.1](../../docs/spec/08-generics-constraints-and-contracts.md#8151-universal-verification)).

In `walk`, `T` is kept; the destruction of `T.Child` that the recursive call composes has the recursive base `T` (a `Tree`), so
`walk`'s summary is `{destruction of T}` plus unknown environment effects. The rule also widens effects that do not grow (§4
item 2), all in declarations no concrete Type can instantiate.

### 2.3. Why the fixed point is finite

Every step of a projection needs conformance evidence for its base
([§8.5.2](../../docs/spec/08-generics-constraints-and-contracts.md#852-projections), line 269). In a declaration's scope that
evidence comes from its finitely many premises, from identity premises (which normalize the projection) or from a Constraint of a
Contract proven for a shorter base, which may reach several projection steps at once (`Self.(A).X.(B).Y is A`). Each such step
consumes at most the longest Constraint path of the program. A projection deeper than the deepest premise plus the number of
Contract declarations times the longest Constraint path therefore draws its evidence from a chain of Constraints that repeats a
Contract declaration, and that Contract is recursive under §2.1's definition. So the effects with no recursive base have bounded
depth, and they are the only projection effects a summary keeps, whatever its dependencies are.

Bounded depth gives finitely many atoms only if the Contract references in projection qualifiers stay finite too (H). A base
proven to conform to infinitely many instances of one Contract declaration would need a chain that revisits the declaration:
for refinement that is an error ([§8.9.2](../../docs/spec/08-generics-constraints-and-contracts.md#892-merging-paths-and-cycles),
line 633, "a refinement path that revisits a Contract declaration, even with different arguments"; probe rv-I is
`InvalidConstraint_Kd`), and through Constraints it makes the Contract recursive, so the base has a recursive base and its effects
are widened.

(H) as a proof. The model checks it on 50,000 random well-formed programs (§10), whose grammar has two associated Types under one
Contract, Constraints on a two-step projection of Self, constructors with `drop` bodies and no Contract Type arguments: 7,806 had
an infinite exact fixed point, each with an atom with a recursive base, and the rule terminated on all. Recursive Contracts with
Type arguments (rv-A) are covered by the argument only.

The probe confirms the premise side on the compiler at the base: recursion `unwind(inner)` with `T := T.Inner` is
`UnprovenConstraint_Kd` at the recursive call when `Inner` is unconstrained, and also with the premise `T.(Layer).Inner is
Layer`, which does not reach `T.Inner.Inner`; `Node: Tree` with `Child is Node` is `UnprovenConstraint_Kd` (§8.3.3's example).

### 2.4. Soundness

Unknown environment effects conflict wherever the replaced effects could: under `confined` and `preserves results` (§8.11.2)
and with every live Loan anchored in mutable static storage (§15.6.4). A summary covers static storage only (§15.6.4); the
effects through inputs come from the signature (§8.11.4 rule 1, §12.7.1 "Exclusive input bound"), and Loans in immutable statics
are never affected. So the rule accepts nothing the current letter rejects; it only rejects more (§4). (H) for §12.7.1's
receiver statuses.

### 2.5. SPEC text

**§8.3.3, line 162.**

- Now: "When a Contract's associated-Type Constraint leads back to the same Contract, the Contract is valid, but no concrete Type
  can be proven to conform."
- New: "A Contract `C` is **recursive** when `T is C` supplies ([§8.3.2](#832-proof-rules): `C`'s own and inherited
  Constraints, with `T` for `Self`), directly or through the Constraints of the Contracts so supplied, a conformance of a
  projection of `T` to `C`'s declaration, with any Type arguments, or to a Contract that refines it. A recursive Contract is
  valid, but no concrete Type can be proven to conform." The `Tree`/`Node` example stays.

**§15.6.4, line 596**, after "…as the least fixed point over recursive calls.", insert:

> Its **parameter-dependent effects** are those it keeps in terms of a Type parameter or an associated-Type projection:
> destructions of such a Type not proven Copy ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)), the
> user-code effects of published operations on such a Type
> ([§4.9.6](04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects)) and calls through Callable inputs. When a
> summary would keep one on a projection one of whose bases (its base, that base's base, and so on) the premises of the summary's
> declaration prove to conform to a recursive Contract
> ([§8.3.3](08-generics-constraints-and-contracts.md#833-recursion-and-finality)), it has unknown environment effects instead.
> The other parameter-dependent effects have a bounded projection depth, so the fixed point is finite.

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
// walk's summary has unknown environment effects: T.Child has the recursive base T.

group Counters
    public var count: i32 = 0

func visitCopies<U>(node: U)
    U is CopyTree
    let c = Counters.count@ref
    Trees.walk(node, 2)           // Error: walk's environment effects are unknown and conflict with c.
    inspect(c)
```

Under the current letter `visitCopies` is valid: every Type `walk<U>` destroys is a `U.Child…`, proven Copy. The first draft's
`visit` (`U is Tree`) is an error under the letter too, so it does not show S5.

**§16.3.3, line 196**, last sentence: "…the body's summary publishes it as a parameter-dependent effect that callers compose
with the actual Type ([§4.9.6](…))" gains ", unless [§15.6.4](…) widens it".

**§4.9.6, line 568**: "Parameter-dependent destruction is composed at callers for Loan checking" gains ", unless
[§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls) widens it" (review N8).

**Appendix E.** Two rows: "Parameter-dependent effect | An effect a summary keeps in terms of a Type parameter or associated-Type
projection, composed by callers with the actual Type unless §15.6.4 widens it. | §15.6.4" and "Recursive Contract | A Contract
whose Constraints require a projection of the conforming Type to conform to it again; no concrete Type conforms. | §8.3.3".

No change: §15.6.4 line 599 and §8.11.2 (unknown environment effects are already covered; the first draft's edit of line 599,
"a call whose callee's summary records an effect that cannot be classified", read with §8.11.2's abstract-destruction row, would
have made every call of a generic destroyer such as `drop1(5)` unknown; review B4), §8.11.4, §8.11.6 (a Function Item's evidence
is its summary), §12.7.1 line 220 (it takes "the summary domain of §15.6.4"; (H) that it needs no own sentence), §15.3.5 and
§8.15.3 (concrete growth such as `rc/Nest<(T, T)>?` or `f<Wrap<T>>` stays finite per declaration; its implementation bound is
PLAN G128 and the destruction-walk bound, separate O4 items).

## 3. Examples

Invalid under the rule and under the current letter: `visit<U>`, as `visitCopies` but with `U is Tree` and
`walk(node@move, 2)`. `walk<U>`'s exact effects are destructions of `U`, `U.Child`, …; none is proven Copy, so they are unknown
for `visit`'s own Loan checks (§16.3.3).

Valid and exact (Source is not recursive, so nothing widens; the probe's `ping` case keeps `L.Item` and `R.Item`):

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

Valid and exact: an identity premise `T.(Tree).Child is T` makes `T.Child` normalize to `T` (model case 2); under
`T is CopyTree`, `T.Child` is Copy, so its destruction is no effect at all (cases 7 and 8).

Growing, so widened: mutual recursion through `A.X is B` and `B.Y is A` (case 3, probe `mutual`); a conformance rule
`Wrap2<X>.I is X.I.I` (case 6); a refinement `Rec: Node` with `Self.(Node).Child is Rec` (case 10); the deep base of §2.1
(case 13, rv-B); the cleanup cycle (case 15, rv-F); the two-step Constraint of Self (case 18, rv-G); `Seq<E>` (rv-A, argument
only). The probed ones (`mutual`, rv-A, rv-B, rv-F, rv-G) reach the depth-64 Limit today.

## 4. What is lost

Each form is valid under the current letter and an error under the rule. All are in declarations whose premises name a recursive
Contract, which no concrete Type satisfies (§8.3.3): they can never be instantiated, yet they are checked
([§8.15.1](../../docs/spec/08-generics-constraints-and-contracts.md#8151-universal-verification), line 955). A5P §5.2 described
the loss as callers whose concrete instances would classify the effect; by §8.3.3 no such concrete instance exists, so the losses
are generic callers whose premises are stronger than the callee's.

1. **Callers whose premises classify every effect of a growing chain** (SPEC: §15.6.4 effect summaries, §16.3.3, §8.11.2):
   `visitCopies`, and a `confined` implementation under such premises (`struct Holder<T>` with `T is CopyTree` calling `walk` in
   a `Sink.put`; the probe's second case). Binding and A4 both reject that witness today (`IncompatibleContractImplementation_Kd`),
   so the rule changes no default-pipeline verdict there (review N4); A4's rejection is the side finding of §10 (H).
2. **Stronger callers of any effect with a recursive base, growing or not** (SPEC: §15.6.4): `h<T: Tree>` calling `k(t.child())`
   that destroys its `U`, used by `g<V: CopyTree>` (case 16); `f<T: Tree>` destroying a `T.Child` and recursing on `T` itself,
   used the same way (case 17); an effect passed between two declarations, or to another such projection (`f<L: Tree, R: Tree>`
   calling `f(r, l)` while destroying an `L.Child`). This is where the rule departs from "grows".

Counts: library 0, milestones 0, SPEC examples 0, xUnit 0 (H: the grep-level scan of §5). Kept: every program without a recursive
Contract (model cases 4, 5, 11, 12 and every random program with no atom with a recursive base), and every effect on a Type its
premises prove Copy (cases 7, 8).

## 5. Census

Probe at the base (§10), mir pipeline; the library is the empty-source compilation of `MirAnalysisTest.TheLibraryHasNoProblems`.

| Measure | Library | Milestones 1–44 |
| --- | ---: | ---: |
| A4 body views | 214 | 350 |
| Views with a Limit (any growth reaches one, by any dependency kind) | 0 | 0 |
| Recursive call edges (calls only; cleanup cycles are invisible here) | 0 | 0 |
| Summary atoms on Type parameters / on projections | 102 / 0 | 24 / 1 (Milestone20 `readTwice`, `T.Element` of a non-recursive `Source`) |
| Recursive Contracts (H: grep-level scan) | 0 | 0 |
| Verdicts the rule changes | 0 | 0 |

The rule changes nothing in the library or the milestones because no atom has a recursive base: no recursive Contract exists
there, and the one projection atom belongs to a non-recursive `Source`. Independently, nothing grows there: the "Limit 0" row
shows it, as every growth reaches G128's Limit (the call-edge row cannot see cleanup cycles).

Milestones 39, 41 and 43 have 20 Binding issues between them (Binding-incomplete, known), so A4 sees only their completed bodies;
none declares an associated Type. The probe's scan (`Benchmark contracts`) reads each `contract` block of a `.kimi` file, a fenced
block of a `.md` file or a physical line of a `.cs` or `.json` file. It follows, within that unit, the Contract names on the right
of the block's Constraints on projections of Self at any depth (`associate X is …`, `Self.(C).X.(D).Y is …`) and its parents and
`Self is C` clauses, Type arguments ignored, and reports the recursive Contracts of §2.1, through other Contracts included. (The
second draft's scan read only one-step `Self.(C).X` clauses; a grep finds no deeper clause in the library, the milestones,
`docs/spec`, `docs/impl`, `tests/mir` or `tests/diagnostics`.) Over those and `tests/xUnitTest` and `tests/Workloads` it finds 3,
all naming themselves: SPEC §8.3.3's `Tree` (a declaration without calls) and xUnit
`AssociatedRefinementCapabilityBindingTest.cs:77` `Trait.Next` and `ConditionalConformanceBindingTest.cs:170` `C.E` (neither with
a call). A declaration split over several C# lines is not followed (H). The scan does not read `or`, so it would count
`Child is Tree or Leafy` as recursive, which §2.1 excludes: a harmless over-count, since it can only add hits and each of the 3 was
checked by reading. By the reviewer's reading, no library Contract is
recursive (Iterator, LendingIterator, IntoIterable, Iterable, UniqIterable, Indexable and Comparable checked). This replaces A5P
§5.2's "(H) lib 0, ms 0"; step 0's `census.md` has no S5 count.

## 6. Alternatives

- **Form (a), growing effects only (rejected by the owner's choice).** It keeps OQ13's "grows": a summary would widen only an
  effect with a recursive base that passes along a cycle of summary dependencies and is not mapped to itself. Its precision on
  instantiable programs and its counts equal the rule's, and it avoids §4 item 2. But a growth test must follow every way a summary
  includes another, cleanup included: the first draft's "recursive call" missed rv-F, and the model's call-edge variant S5A1 does
  not terminate on case 15 and on 1,210 random programs (review B3). It needs a SPEC definition of summary dependencies and their
  cycles plus a self-map rule, and in A4 cycles maintained during the worklist, because destructor, published-row, Closure and
  witness entries (`Enter`, `Require`) are found while atoms are composed (about +60 lines, H). Its finiteness holds only if that
  definition names every dependency kind.
- **A depth limit whose excess is a Resource failure** (A5P §5.2's alternative; OQ13 (b)). Every growing chain is infinite, so
  every limit is exceeded wherever a verdict needs the summary: it accepts nothing S5 rejects (model: DEPTH is Resource wherever
  EXACT is infinite, including case 1's `copied`). It also fails finite programs deeper than the limit that S5 computes exactly
  (case 12: a five-step chain of non-recursive Contracts, instantiable, Resource at limit 4). Its result is implementation-defined
  and no larger limit repairs it (Principles 2 and 4). Today's implementation is this option with limit 64 and `Unsupported_Kd`.
- **The open-key form as written** ("a projection effect that passes along a cycle and is not mapped to itself"). It widens
  instantiable programs: case 4 (`ping`) becomes Unknown, and so would any mutual recursion passing an `Item` along.
- **Exact summaries**: the set of grown effects as a regular set of projection paths, resolved at callers by induction over
  Contract Constraints. The image set is decidable (H: prefix rewriting), but it is a second effect representation and a proof
  rule for code that can never run.
- **"Recursive" by a list of Constraint kinds** (the second draft): it missed Constraints on deeper projections of Self (review
  B5); the model's ENUM does not terminate on case 18 and on 72 random programs. The proof-rule definition needs no list.
- **Undecided at R4's exit** (OQ13 (c)): growing atoms stay a located `Unsupported_Kd`, an O4 item in OQ9.

## 7. Principles

- **One Concept, One Canonical Form.** The widened effect becomes the same unknown environment effects as an unknown call; no new
  code, effect kind or representation. "Recursive" reuses the proof rule that already says what `T is C` supplies.
- **Local Reasoning.** The test reads one Type and the premises of the declaration that keeps it, with no recursion test; it
  makes the fixed point finite without a work budget, whatever dependency kinds a summary has (§2.3).
- **Explicit Semantics.** The widening is a SPEC sentence; its consequence is the stated rule for unknown effects. The losses are
  listed (§4) and confined to declarations that no concrete Type can instantiate.
- **Compiler Server Protocol.** The conflict is reported at the call with the existing facts; the widened effect can be a related
  location (H: a fact A4 can publish at no cost).

## 8. Implementation and tests

- **A4 (`MirEffects`).** Where an atom would be kept (`AddAtom`, `MirEffects.Solve.cs:432`, and the destruction walk that reaches
  it), a projection with a recursive base under the definition's premises, not proven Copy for a destruction, records Unknown
  instead. (H) +10 lines, no new MIR vocabulary or diagnostic code. Growth then costs about one extra evaluation instead of 64
  rounds; V16 stays linear (H).
- **Binding view.** "Recursive" is decided through the proof rule, not by reading Constraint kinds: for each Contract
  declaration, what `T is C` supplies for projections of a fresh `T`, Constraints on deeper projections of Self included
  (`Self.(A).X.(B).Y is A`), searched to a conformance to `C`'s declaration with any Type arguments or to a refining Contract; then
  "a base proven to conform to a recursive Contract" per projection under the definition's premises. Per PD6 a new R4 view with a
  BU reviewer; (H) +30 lines.
- **Interim.** Until the SPEC session, the forms the rule widens keep today's verdicts, deliberately. A growing atom is already a
  located `Unsupported_Kd` (today's Limit at G128's bound; A5-F5's 2b-3 maps Limit to `Unsupported_Kd` at the call). The rule's
  other losses (§4 item 2) stay accepted: the current letter accepts them and no SPEC example covers them, so nothing reports a
  Language error for a valid form, and no test expects `Unsupported_Kd` for them. PD30 does not apply before the SPEC session: no
  unit implements S5 until then (the A4 unit follows the session), so no program is rejected by S5 in the meantime (planning
  session, 2026-10-19). The unit after the SPEC session adds the test and records Unknown in one step, with existing
  codes: `IncompatibleContractImplementation_Kd` for `confined`, `UnsatisfiedEffectBound_Kd` for a Callable bound,
  `ComparisonLoanConflict_Kd` at a call against a live mutable-static Loan (R4-M72).
- **Old path.** Unchanged (OD5: SPEC changes on MIR only; D5).
- **Tests.** `MirEffectsTest` rows for model cases 1 (growth), 3 (mutual), 4 (swap kept exact), 10 (refinement), 12 (a deep
  finite chain with no limit), 13 (deep base), 15 (cleanup cycle), 16 or 17 (a stronger caller) and 18 (a two-step Constraint of
  Self, rv-G); the growing rows expect the Language codes and fail before the change, which gives a Limit. A Binding-view test for
  each §2.1 example of a recursive Contract and for `Child is Tree or Leafy` (not recursive). A `MirBorrowsTest.Effects` row for a
  growing call against a live implicit static Loan belongs to 2b-3. The new SPEC example joins `tests/diagnostics/spec-cases.json`
  like §15.6.4's `run` (`15.6.4-static-anchors`, with its `inspect` prelude; without it the probe reports `UnresolvedBinding_Kd`):
  it expects `ComparisonLoanConflict_Kd` at the call, with the same status for the explicit static borrow (PLAN G100, P42).

## 9. Owner decision

OQ13 adopted "an effect atom on an associated projection that grows under polymorphic recursion widens". The first draft asked
the owner (Q1) to choose between form (a), which keeps "grows", and form (c), which widens every parameter-dependent effect on a
projection with a recursive base; the first review found that a growth test must follow every summary dependency (B3), and the
second found a gap in the list of Constraint kinds that made a Contract recursive (B5), the second blocking round on that family.
**On 2026-10-19 the owner chose form (c), with "recursive" defined through the existing proof rule** (DECISIONS R4-M76, which
supersedes R4-M74 OQ13's "grows"). The rule departs from OQ13's word "grows" by that choice: it also widens effects with a
recursive base that do not grow (§4 item 2), which matters only in declarations no concrete Type can instantiate.

| | The rule, form (c) | Form (a), rejected |
| --- | --- | --- |
| SPEC | §2.1's definitions plus one sentence | the same plus a definition of summary dependencies and their cycles, and the self-map rule |
| A4 | a test where an atom is kept (+10, H) | cycles maintained during the worklist and a self-map test (+60, H) |
| Finiteness | §2.3's bound alone, for any dependency kind | needs every dependency kind in its definition |
| Losses (all uninstantiable) | §4 items 1 and 2 | §4 item 1 and the finite orbits of item 2 |
| Counts: lib, ms, SPEC, xUnit (H) | 0, 0, 0, 0 | 0, 0, 0, 0 |
| OQ13's "grows" | departs from it (the owner's choice) | keeps it |

## 10. Evidence and integration

- **Model.** `S5EffectWidening.cs`: summaries over Type parameters and projections with normalization, conformance rules, premise
  proofs (no coinduction), Constraints on deeper projections of Self, Copy, destruction resolution and `drop` bodies as
  dependencies. Rules: EXACT (the letter, with a depth cap marking an infinite fixed point), S5C (the rule), ENUM (S5C with the
  second draft's list of Constraint kinds), S5A (form (a), self-map on the substituted Type; review N2), S5A1 (the first draft's
  call edges), IMM (the immediate base only), the open-key form and a depth limit. Copy it to an empty folder outside the
  repository and run `dotnet run S5EffectWidening.cs` (.NET SDK 11.0.100-rc.2, about 40 s). Result at the base: 18 hand cases
  (13, 14 and 18 are the reviews'); S5C equal to the exact verdict on every instantiable one (4, 5, 11, 12) and terminating on
  all; ENUM does not terminate on case 18, IMM on cases 13 and 18, S5A1 on case 15. Random: 50,000 well-formed programs, 38,175
  with a recursive Contract, 24,854 with a `drop` body, 8,287 with a Constraint on a two-step projection of Self, 7,806 with an
  infinite exact fixed point; 0 failures (S5C and S5A always terminate; every infinite exact fixed point has an atom with a
  recursive base; where no atom has one, S5C and S5A equal EXACT); ENUM does not terminate on 72, IMM on 252, S5A1 on 1,210. The
  grammar has no Contract Type arguments (§2.3), and the model treats every parameter-dependent effect kind alike and does not
  model Origins or Semantics cases.
- **Probe.** `S5Probe.cs`: the `Program.cs` of a scratch console project named `Benchmark` that references a copy of `Kimi.dll`
  built at the base (as in `artifacts/handoff/2026-10-18-pause/r4-scratch/1c-design/probe`). `Benchmark cases` runs its 18
  programs (the reviews' rv-A, rv-B, rv-F, rv-G and rv-G3 included; the outputs quoted in §1–§4 and below), `Benchmark census
  <repository>` the census of §5 and `Benchmark contracts <root>...` its scan (bin/ and obj/ are skipped relative to each root, so
  absolute roots under `bin/worktrees/` work). It reads A4's private state by reflection.
- **Integration.** A separate SPEC session after this proposal's review (R4-M74) applies §2.5 and adds the example's spec case;
  INTEGRATED records the intake; SPECJ and GUIDE follow only on explicit instruction (SPECJ's §15.6.4 still has the pre-P5 open
  key). PLAN: OQ9's S5 item and the unit of §8 are the planning session's.
- **Side finding outside S5** (the probe's last three cases; reproduced by the review, N9). The mir pipeline does not prove
  `T.(Tree).Child.(Tree).Child` Copy under `T is CopyTree`, whose Copy comes through a Constraint on an inherited associated Type,
  while Binding does: A1 reports `MovedPlace_Kd` for `(value, value)` of that Type, and A4 reports
  `IncompatibleContractImplementation_Kd` for `Holder<T: CopyTree>` calling a non-recursive helper chain that destroys a
  `T.Child.Child`; Binding accepts both, and the chain one step shorter passes. These are Language rejections of valid forms in
  the mir pipeline. A4's Copy query is Binding's own `ProveCopy` under `EffectPremises(definition)`
  (`src/Kimi/Compiler/Binding/TyCtxt.Effects.cs:59`, `Binding.EffectObligations.cs:119`), so the cause more likely lies in that
  premise scope or in the Type form MIR passes (Binding, BU) than in A4; not diagnosed. An open-issue candidate for the planning
  session.
- **SPEC gaps found.** (1) §8.3.2's row "Contract refinement" (ch. 8 line 145) says "`T is C` supplies each ancestor
  conformance and inherited Constraint, with `T` for `Self`": it names neither `C`'s own Constraints nor what generic checking
  needs from them, though §2.1's definition and every generic body under `T is C` rely on both. The S5 SPEC session fixes the row
  together with §2.5. (2) Whether a Constraint on a two-step projection of Self (`Self.(A).X.(B).Y is A`) is valid is not stated
  outright (§2.1, H); the same session states it.
- **Not verified.** §2.3 as a proof (argued; model-checked without Contract Type arguments); whether two-step Constraints of Self
  are valid by SPEC's letter (H, §2.1); §12.7.1's reading; recursion through Closures, accessors, defaults or specialization sets
  (covered by the rule's independence from dependencies, not tested); whether rv-F's `drop` form is valid by every clause of §16
  (Binding accepts it); the A4 and Binding sizes; the scan's completeness for xUnit; SPECJ.

## 11. Review record

**Round 1** (`3d2fd284`): four blocking and nine non-blocking findings, applied in `74019731`.

- B1 "a base" means any base (§2.1, the SPEC sentence, model case 13, probe rv-B).
- B2 a recursive Contract ranges over its declaration with any Type arguments (probe rv-A); superseded by B5's definition.
- B3 a growth test must follow summary dependencies, cleanup included (model `drop` bodies, case 15, probe rv-F; §5's "Limit"
  row carries the census).
- B4 the widening records unknown environment effects; the line-599 edit is dropped.
- N1 `visitCopies` is the SPEC example; N2 the model's self-map compares the substituted Type; N3 `L.Child`; N4 Binding rejects
  the confined witness today; N5 counts per form; N6 the scan works with absolute roots and follows other Contracts; N7 the random
  grammar's limits are stated and extended; N8 §4.9.6's clause; N9 the side finding's cause is worded neutrally.

**Round 2** (`74019731`): one blocking finding and one required precision edit; then the owner's choice (§9).

- B5 the list of Constraint kinds missed Constraints on deeper projections of Self (probe rv-G, model case 18): "recursive" is now
  defined through the proof rule (§2.1, §2.5, Appendix E), §2.3's bound counts Contract declarations times the longest Constraint
  path, §8's Binding view decides it through the proof rule, and the scan reads clauses at any depth (it read only one-step
  clauses before; the counts stand). (H) that SPEC admits such two-step clauses.
- R1 the SPEC sentence says "destructions of such a Type not proven Copy"; the model's stale "SPEC 8.7.2" citation (now §8.7.2
  `Sealed`) is §16.3.3 and §8.11.2. A4's own comment at `MirEffects.Solve.cs:430` cites "SPEC 8.7.2" too; it is reported to the
  planning session, not edited here.
- §2.3 gains the (H) sentence on finitely many Contract references in projection qualifiers (§8.9.2, rv-I).
- §8's interim states deliberately that the rule's extra losses stay accepted until the SPEC session, outside PD30.
- Form (a) is kept only as a rejected alternative (§6).

**Round 3** (`29974860`): passed, with edits applied in the next commit: the supply in the definition is explicit (`C`'s own and
inherited Constraints, with `T` for `Self`) and §8.3.2's row is listed as a SPEC gap (§10); PD30's non-application before the SPEC
session is recorded as the planning session's reading; the owner's choice cites DECISIONS R4-M76; §6.1.4's line is 94; the scan's
over-count of `or` Constraints is noted (§5).
