# Effect Atoms Widen

Date: 2026-10-19. Status: proposal for the owner's decision; not integrated or implemented. Written by R4 unit 2x-S5 of the
[A5 Step 2 Implementation Plan](<2026-10-18 A5 Step 2 Implementation Plan.md>) under the standing rule OD7 of the
[Complete Polonius A5 Plan](<2026-10-16 Complete Polonius A5 Plan.md>) (A5P; DECISIONS R4-M35): a rule that is not finitely
expressible gets its own SPEC proposal. The owner adopted S5 on 2026-10-19 (OQ13 (a), DECISIONS R4-M74) as A5P §5.2 words it;
this proposal makes the rule precise, and §9 asks the owner to confirm that precision. Changes SPEC §8.3.3, §15.6.4, §16.3.3 and
Appendix E.

Base: `48a42168` (`dev`). Line numbers refer to that commit; SPEC and A4 are unchanged up to `f8bd3856`. "(H)" marks an
inference not checked by running code. Evidence: the model [S5EffectWidening.cs](<../Sketches/A5 Step 2/S5EffectWidening.cs>)
and the probe [S5Probe.cs](<../Sketches/A5 Step 2/S5Probe.cs>) (§10).

## 1. Problem

An effect summary "is computed once per declaration, in terms of its own Type parameters, as the least fixed point over
recursive calls" ([§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls), line 596). Besides static Fields it
keeps **parameter-dependent effects**: the destruction of a value of a Type parameter or associated-Type projection
([§16.3.3](../../docs/spec/16-scope-exit-and-destruction.md#1633-handles-and-object-release), line 196;
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
has infinitely many atoms and no finite form. Before P5, §15.6.4 cut the same growth for static keys: "When a static key passes
through a recursive call and substitution does not map it to itself, the summary records the open key of its Field instead"
(`4372b058^`, line 531). P5 made all keys of one Field one root, so that sentence went and nothing replaced it for
parameter-dependent effects. Widening without a SPEC rule would be a conservative Language error (A5P §5.1).

**Today.** A4 (`MirEffects`) has no growth rule. Each round of the worklist composes one more projection step until
`TyCtxt.Apply` (Binding's storage instantiation, depth 64, PLAN G128) returns no Type; the atom then becomes a Limit
(`src/Kimi/Compiler/Mir/Analysis/MirEffects.Solve.cs:280` and `:300`), the located `Unsupported_Kd` where a bound is owed. The
probe's `walk` case: 65 atoms per summary (depths 0–64), Limit in `walk` and its caller, 338 evaluations against 239 for a plain
recursion.

## 2. Proposal

A growing parameter-dependent effect is recorded as an effect that cannot be classified, as the open key once widened a growing
static key. "Growing" is decided per recursive call, as the open key was, and only for projections whose base conforms to a
recursive Contract, the only projections that can grow (§2.2).

### 2.1. The rule

- A Contract is **recursive** when conjunction elimination of the Constraints on its associated Types (declared, inherited, or
  placed on inherited ones by [§8.5.3](../../docs/spec/08-generics-constraints-and-contracts.md#853-constraints-on-inherited-associated-types)) yields a
  conformance that entails it, directly or through the associated-Type Constraints of the Contracts so yielded. §8.3.3 (line 162)
  already says that no concrete Type can be proven to conform to such a Contract; the definition names the Contracts it means
  (H: "leads back" already covers transitive paths and Constraints on inherited associated Types; disjunctions are excluded
  because a conformer may satisfy another disjunct).
- When a parameter-dependent effect of a callee, on a projection with a base that the callee's premises prove to conform to a
  recursive Contract, passes through a recursive call and substitution does not map it to itself, the caller's summary records an
  effect that cannot be classified in its place. An effect passed from one declaration to another is never mapped to itself.
- A call whose callee's summary records one has unknown environment effects
  ([§8.11.2](../../docs/spec/08-generics-constraints-and-contracts.md#8112-confined) row "A call or a destruction whose environment
  effects are unknown"; §15.6.4 line 599).

In `walk`, the destruction of `T` passes the call as `T.Child` (its source is a Type parameter, so it is kept). The destruction of
`T.Child` (base `T`, a `Tree`) then passes as `T.Child.Child`, not itself, and the summary records an unclassifiable effect.

### 2.2. Why the fixed point is finite

Every step of a projection needs conformance evidence for its base
([§8.5.2](../../docs/spec/08-generics-constraints-and-contracts.md#852-projections), line 269). In a declaration's scope that
evidence comes from its finitely many premises, from identity premises (which normalize the projection) or from the
associated-Type Constraints of a Contract proven for the next shorter base. A projection deeper than the deepest premise plus the
number of Contracts therefore draws its evidence from a chain of Constraints that repeats a Contract, and that Contract is
recursive. So effects whose bases conform to no recursive Contract have bounded depth and are finitely many. An effect with a
recursive-Contract base enters a summary only as a local effect, through a non-recursive call, as the image of one of those
finitely many effects, or mapped to itself, so the kept effects are finite too. (H) as a proof; checked by the model on 50,000
random well-formed programs (§10): every infinite exact fixed point had an atom with a recursive-Contract base, and S5
terminated on all.

The probe confirms the premise side on the compiler at the base: recursion `unwind(inner)` with `T := T.Inner` is
`UnprovenConstraint_Kd` at the recursive call when `Inner` is unconstrained, and also with the premise `T.(Layer).Inner is
Layer`, which does not reach `T.Inner.Inner`; `Node: Tree` with `Child is Node` is `UnprovenConstraint_Kd` (§8.3.3's example).

### 2.3. Soundness

The rule replaces effects by an effect that conflicts wherever any of them could: an unclassifiable effect fails `confined` and
`preserves results` (§8.11.2) and, as unknown environment effects, conflicts with every live Loan anchored in mutable static
storage (§15.6.4). Loans in immutable statics and the effects through inputs (§8.11.4 rule 1) do not depend on the summary. So
S5 accepts nothing the current letter rejects; it only rejects more (§4).

### 2.4. SPEC text

**§8.3.3, line 162.**

- Now: "When a Contract's associated-Type Constraint leads back to the same Contract, the Contract is valid, but no concrete Type
  can be proven to conform."
- New: "A Contract is **recursive** when conjunction elimination ([§8.3.2](#832-proof-rules)) of the Constraints on its associated
  Types, those it declares or inherits and those it places on inherited ones ([§8.5.3](#853-constraints-on-inherited-associated-types)),
  yields a conformance that entails it, directly or through the associated-Type Constraints of the Contracts so yielded. A
  recursive Contract is valid, but no concrete Type can be proven to conform."

**§15.6.4, line 596**, after "…as the least fixed point over recursive calls.":

- Insert: "Its **parameter-dependent effects** are those it keeps in terms of a Type parameter or an associated-Type projection:
  destructions ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)), the user-code effects of published
  operations on such a Type ([§4.9.6](04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects)) and calls
  through Callable inputs. When a parameter-dependent effect of a callee, on a projection with a base that the callee's premises
  prove to conform to a recursive Contract ([§8.3.3](08-generics-constraints-and-contracts.md#833-recursion-and-finality)), passes
  through a recursive call and substitution does not map it to itself, the caller's summary records an effect that cannot be
  classified ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)) in its place; an effect passed from one
  declaration to another is never mapped to itself. The effects a summary keeps then have a bounded projection depth, so the
  fixed point is finite."

**§15.6.4, line 599.**

- Now: "A call with unknown environment effects, or an erased destruction ([§16.3.3](…)), conflicts with every live Loan…"
- New: "A call with unknown environment effects, including one whose callee's summary records an effect that cannot be
  classified, or an erased destruction ([§16.3.3](…)), conflicts with every live Loan…"

**§15.6.4, after the `run` example (line 622).** A new example:

```kimi
contract Tree
    associate Child is Tree       // Recursive (§8.3.3).
    func child(self: ref/Self) -> Option<Self.Child>
        effect confined

group Trees
    public func walk<T>(node: T, depth: i32)
        T is Tree
        if depth == 0 => return
        match node.child()
            .Some(let next) => Trees.walk(next@move, depth - 1) // T.Child would pass as T.Child.Child.
            .None => return
// walk's summary: the destructions of T and T.Child, and an effect that cannot be classified.

group Counters
    public var count: i32 = 0

func visit<U>(node: U)
    U is Tree
    let c = Counters.count@ref
    Trees.walk(node@move, 2)      // Error: walk's environment effects are unknown and conflict with c.
    inspect(c)
```

**§16.3.3, line 196**, last sentence: "…the body's summary publishes it as a parameter-dependent effect that callers compose
with the actual Type ([§4.9.6](…))" gains ", unless recursion widens it ([§15.6.4](…))".

**Appendix E.** Two rows: "Parameter-dependent effect | An effect a summary keeps in terms of a Type parameter or associated-Type
projection, composed by callers with the actual Type, unless recursion widens it. | §15.6.4" and "Recursive Contract | A Contract
whose associated-Type Constraints lead back to it; no concrete Type conforms. | §8.3.3".

No change: §8.11.2 (a callee's unclassifiable effect is in the caller's completed summary, which "includes the effects of its
callees"), §8.11.4, §8.11.6 (a Function Item's evidence is its summary), §12.7.1 line 220 (it takes "the summary domain of
§15.6.4"; (H) that it needs no own sentence), §15.3.5 and §8.15.3 (concrete growth such as `rc/Nest<(T, T)>?` or `f<Wrap<T>>`
stays finite per declaration; its implementation bound is PLAN G128 and the destruction-walk bound, separate O4 items).

## 3. Examples

Invalid (under S5 and under the current letter): `visit` above. `walk<U>`'s exact effects are destructions of `U`, `U.Child`, …;
none is proven Copy, so they are unknown for `visit`'s own Loan checks (§16.3.3) either way.

Valid, exact under S5 (Source is not recursive, so nothing widens; the probe's `ping` case keeps `L.Item` and `R.Item`):

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

Valid, exact under S5: an identity premise `T.(Tree).Child is T` makes the recursive call substitute `T` itself; a recursion
`f(node)` with the same `T` maps `T.Child` to itself; a non-recursive call `k(next)` with `U := T.Child` passes no recursive call.

Invalid under S5, valid under the current letter (the loss of §4):

```kimi
contract CopyTree: Tree, Copy
    Self.(Tree).Child is CopyTree // Recursive; every U.Child… of a CopyTree U is Copy.

func visitCopies<U>(node: U)
    U is CopyTree
    let c = Counters.count@ref
    Trees.walk(node, 2)           // Error under S5: unknown effects. Valid by the letter: every destroyed Type is Copy.
    inspect(c)
```

Mutual recursion grows too: `f<T: A>` calling `g(t.x())` and `g<U: B>` calling `f(u.y())` with `A.X is B` and `B.Y is A` widen
`T.X` in `f` (the probe's `mutual` case reaches the same depth-64 Limit today).

## 4. What is lost

Each form is valid under the current letter and an error under S5. All are in declarations whose premises name a recursive
Contract, which no concrete Type satisfies (§8.3.3): they can never be instantiated, yet they are checked
([§8.15.1](../../docs/spec/08-generics-constraints-and-contracts.md#8151-universal-verification), line 955). A5P §5.2 described
the loss as callers whose concrete instances would classify the effect; by §8.3.3 no such concrete instance exists, so the losses
are generic callers whose premises are stronger than the callee's.

1. **Callers whose premises classify every effect of the chain** (SPEC: §15.6.4 effect summaries, §16.3.3, §8.11.2):
   `visitCopies` above, and a `confined` implementation under such premises (`struct Holder<T>` with `T is CopyTree` calling
   `walk` in a `Sink.put`; the probe's second case, where A4 already reports `IncompatibleContractImplementation_Kd` today, H:
   through the side finding of §10).
2. **Finite orbits the trigger still widens** (SPEC: §15.6.4): an effect on a recursive-Contract projection passed between two
   declarations of one recursive component, or mapped to another such projection (`f<L: Tree, R: Tree>` calling `f(r, l)` while
   destroying an `L.Item`).

Counts: library 0, milestones 0, xUnit 0, SPEC examples 0 (§5). Kept: every program without a recursive Contract keeps its exact
summary (model cases 4, 5, 11, 12 and every random program with no recursive-Contract atom), and so do recursion that maps an
effect to itself (case 8) and non-recursive calls over a recursive Contract (case 7).

## 5. Census

Probe at the base (§10), mir pipeline; the library is the empty-source compilation of `MirAnalysisTest.TheLibraryHasNoProblems`.

| Measure | Library | Milestones 1–44 |
| --- | ---: | ---: |
| A4 body views | 214 | 350 |
| Views with a Limit | 0 | 0 |
| Recursive call edges | 0 | 0 |
| … whose substitution names an associated-Type projection | 0 | 0 |
| Summary atoms on Type parameters / on projections | 102 / 0 | 24 / 1 (Milestone20 `readTwice`, `T.Element`, no recursion) |
| Recursive Contracts (scan) | 0 | 0 |
| Growing atoms; verdicts S5 changes | 0; 0 | 0; 0 |

Milestones 39, 41 and 43 have 20 Binding issues between them (Binding-incomplete, known), so A4 sees only their completed bodies;
none declares an associated Type. The probe's scan for Contracts whose associated-Type Constraint names the Contract itself, over
the library, the milestones, `tests/mir`, `tests/diagnostics`, `docs/spec`, `docs/impl`, `tests/xUnitTest` and `tests/Workloads`,
finds 5 lines: SPEC 1 (§8.3.3's `Tree`, a declaration without calls), xUnit 2 (`AssociatedRefinementCapabilityBindingTest.cs:77`
`Trait.Next`, `ConditionalConformanceBindingTest.cs:170` `C.E`; neither has a call) and 2 false positives by reading
(`ConstraintProjectionApiAccessBindingTest.cs:141`, `ModuleBindingTest.cs:883`); a scratch check for cycles through two
Contracts of one file found none. By §2.2 no growth is possible without one, so S5 changes no verdict there. This replaces A5P
§5.2's "(H) lib 0, ms 0"; step 0's `census.md` has no S5 count.

## 6. Alternatives

- **A depth limit whose excess is a Resource failure** (A5P §5.2's alternative; OQ13 (b)). Every growing chain is infinite, so
  every limit is exceeded wherever a verdict needs the summary: it accepts nothing S5 rejects (model: DEPTH is Resource wherever
  EXACT is infinite, including case 1's `copied`). It also fails finite programs deeper than the limit that S5 computes exactly
  (case 12: a five-step chain of non-recursive Contracts, instantiable, Resource at limit 4). Its result is implementation-defined
  and no larger limit repairs it (Principles 2 and 4). Today's implementation is this option with limit 64 and `Unsupported_Kd`.
- **The open-key form as written** ("a projection effect that passes through a recursive call and is not mapped to itself").
  Simplest, but it widens instantiable programs: case 4 (`ping`) becomes Unknown, and so would any mutual recursion passing an
  `Item` along.
- **Every effect on a recursive-Contract projection**, recursion or not. No recursion test, but it widens effects that do not
  grow (cases 7 and 8), beyond S5 as adopted. Its losses are also uninstantiable.
- **Exact summaries**: the set of grown effects as a regular set of projection paths, resolved at callers by induction over
  Contract Constraints. The image set is decidable (H: prefix rewriting), but it is a second effect representation and a proof
  rule for code that can never run.
- **Undecided at R4's exit** (OQ13 (c)): growing atoms stay a located `Unsupported_Kd`, an O4 item in OQ9.

## 7. Principles

- **One Concept, One Canonical Form.** The growing effect becomes the same unclassifiable effect as an erased destruction or an
  unknown call; no new code, effect kind or representation. The trigger repeats the form the open key had.
- **Local Reasoning.** The trigger is decided at one call from the callee's summary, the substitution and the callee's premises;
  recursiveness is a property of Contract declarations. The fixed point is finite by §2.2, without a work budget.
- **Explicit Semantics.** The widening is a SPEC sentence; its consequence is the stated rule for unknown effects. The losses are
  listed (§4) and confined to declarations that no concrete Type can instantiate.
- **Compiler Server Protocol.** The conflict is reported at the call with the existing facts; the recursive call where the effect
  grew can be a related location (H: a fact A4 can publish at no cost).

## 8. Implementation and tests

- **A4 (`MirEffects`).** In `Compose`, for an edge inside a recursive component of A4's call edges (a Tarjan pass over `edges`
  before the worklist, linear), a source atom on a projection with a recursive-Contract base, and an image that is not the source
  in the same body: record Unknown instead of the image. A Binding view answers "recursive Contract" (once per Contract) and "a
  base proven to conform to one" (per projection under the definition's premises); per PD6 a new R4 view with a BU reviewer.
  (H) A4 +25 lines, Binding +30, no new MIR vocabulary or diagnostic code. Growth then costs about one extra evaluation instead of
  64 rounds; V16 stays linear (H).
- **Interim (PD30).** Until the SPEC integration a growing atom is already a located `Unsupported_Kd` (today's Limit at G128's
  bound, and A5-F5's 2b-3 maps Limit to `Unsupported_Kd` at the call), so no interim change is needed. The unit after the SPEC
  session adds the trigger and records Unknown in one step. Its codes are existing ones: `IncompatibleContractImplementation_Kd`
  for `confined`, `UnsatisfiedEffectBound_Kd` for a Callable bound, `ComparisonLoanConflict_Kd` at a call against a live
  mutable-static Loan (R4-M72).
- **Old path.** Unchanged (OD5: SPEC changes on MIR only; D5).
- **Tests.** `MirEffectsTest` rows for model cases 1, 3, 4, 8, 10 and 12 (growth, mutual growth, swap kept exact, self-map kept,
  refinement-made recursion, a deep finite chain with no limit); the growing rows expect the Language codes and fail before the
  change, which gives a Limit. A `MirBorrowsTest.Effects` row for a growing call against a live implicit static Loan belongs to
  2b-3. The new SPEC example joins `tests/diagnostics/spec-cases.json` like §15.6.4's `run` (`15.6.4-static-anchors`): it
  expects `ComparisonLoanConflict_Kd` at the call, with the same status for the explicit static borrow (PLAN G100, P42).

## 9. Owner question

**Q1. The precise form of "grows"** (R4-M74: a review finding that changes the rule returns to the owner). OQ13 adopted "an
effect atom on an associated projection that grows under polymorphic recursion widens". §2.1 decides growth per recursive call,
for projections with a recursive-Contract base, by "not mapped to itself"; besides the infinite chains it widens the finite
orbits of §4 item 2, all uninstantiable.

- (a) §2.1 as written. Recommended: exact on every instantiable program, finite by §2.2, one SPEC paragraph.
- (b) Widen only effects with infinitely many images (exact growth). No item-2 losses; needs the image analysis of §6.
- (c) Widen every effect on a recursive-Contract projection (§6). Simpler; more uninstantiable losses (cases 7, 8).

## 10. Evidence and integration

- **Model.** `S5EffectWidening.cs`: summaries over Type parameters and projections with normalization, conformance rules, premise
  proofs (no coinduction) and destruction resolution, under EXACT (the letter, with a depth cap marking an infinite fixed point),
  S5, the open-key form, the every-projection form and a depth limit. Copy it to an empty folder outside the repository and run
  `dotnet run S5EffectWidening.cs` (.NET SDK 11.0.100-rc.2, about 15 s). Result at the base: 12 hand cases, S5 equal to the exact
  verdict on every instantiable one (4, 5, 11, 12) and terminating on all; random: 50,000 well-formed programs, 35,261 with a
  recursive Contract, 11,997 with an infinite exact fixed point, 13,531 widened by S5, 0 failures (S5 always terminates; every
  infinite exact fixed point has an atom with a recursive-Contract base; S5 equals EXACT where no atom has one). The model
  treats every parameter-dependent effect kind alike and does not model Origins or Semantics cases.
- **Probe.** `S5Probe.cs`: the `Program.cs` of a scratch console project named `Benchmark` that references a copy of `Kimi.dll`
  built at the base (as in `artifacts/handoff/2026-10-18-pause/r4-scratch/1c-design/probe`). `Benchmark cases` runs its twelve
  programs (the outputs quoted in §1–§3 and below), `Benchmark census <repository>` the census of §5 and `Benchmark contracts
  <root>...` its scan. It reads A4's private state by reflection.
- **Integration.** A separate SPEC session after this proposal's review (R4-M74) applies §2.4 and adds the example's spec case;
  INTEGRATED records the intake; SPECJ and GUIDE follow only on explicit instruction (SPECJ's §15.6.4 still has the pre-P5 open
  key). PLAN: OQ9's S5 item and the unit of §8 are the planning session's.
- **Not verified.** §2.2 as a proof (argued, model-checked only); §12.7.1's reading; the A4 sizes; the C# literal scan's
  completeness for xUnit. A side finding outside S5 (the probe's last three cases): the MIR side does not prove
  `T.(Tree).Child.(Tree).Child` Copy under `T is CopyTree`, whose Copy comes through a Constraint on an inherited associated Type,
  while Binding does. A1 reports `MovedPlace_Kd` for `(value, value)` of that Type, and A4 reports
  `IncompatibleContractImplementation_Kd` for `Holder<T: CopyTree>` calling a non-recursive helper chain that destroys a
  `T.Child.Child`; Binding accepts both, and the chain one step shorter passes. These are MIR rejections of valid forms (H: the
  MIR side's Copy query stops at depth 1 for such Constraints); reported to the planning session as a candidate issue.
