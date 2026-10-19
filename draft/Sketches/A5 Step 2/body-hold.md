# Body hold: prototype round 17 (P13's interim, round 16's review B1)

Status (2026-10-19): prototype round run; its independent review says implement; implemented by unit G148 under the planning
session's rulings (below; DECISIONS R4-M73). Working record, not authority.

## Why

P13's interim (DECISIONS R4-M76: "a conflict that depends on such a store is a located `Unsupported_Kd`") has missed routes in
three rounds:

1. The unit's F1: steps along Φ_E and exclusive Loans.
2. The final review's B1: the check's closure and err(ended), giving [route taint](route-taint.md), round 16.
3. Round 16's review B1: writes through a reference and E5's seed pass no taint (`rv-r16/review.md`; compiler probes `writeq.kimi`
   and `seed.kimi` give Language errors at `b335393b`).

Under AGENTS the owner chose (2026-10-19, verbatim): 「本体単位で保留 (推奨)」. The option described to the owner: in a body where
rule G takes a step past a holder that passes the guard only through a Type argument, a pair layer or an abstract part, every Loan
conflict of that body is a located `Unsupported_Kd` at its access. Routes are not tracked, so the rule is complete by construction.
It never accepts. P13's marker and the route rules are deleted. Precision falls in such bodies until the per-part guard (R4-M76,
before R4's exit), measured on the ledger.

## The rule

- **Held body:** a body is held when some Call's W (A3 E16) stores a non-empty value into a target reached only past a holder whose
  Type passes rule G's guard only by argument (`MirWrites.ThroughArgument`). This is the first P13 target `Writes` already returns.
  An empty value holds nothing (the final review's N1).
- **Effect:** in a held body, every A5 Loan problem is reported as `Unsupported_Kd` (`Ownership.Unsupported`, PD30) at the access the
  Language problem would have named: the four Loan codes, err(destroy), and the guard's err(ended). Limit records and other
  analyses' problems are unchanged.
- **Default taint (R4-M70) unchanged:** a Loan only default-tainted sets hold stays `Unsupported_Kd` at the omitting call. In a held
  body, a problem the default taint already makes `Unsupported_Kd` keeps that location, so there is one record per problem.
- **Deleted:** P13's marker element, its `MarkerOf` row and its location rule, and every route rule of round 16. `Writes`' two-pass W
  stays: it finds the held condition.

**Why complete.** A5 is intraprocedural. A by-argument store changes only the states of the body whose Call makes it; a caller sees
the callee only through Binding's retention rows, and a callee checks its own body. So a conflict that depends on such a store is
found in a body where W returns a P13 target, and that body is held. Every Loan problem of a held body is reclassified, so no route
needs to be followed. The three earlier misses were route misses; this rule has none to miss.

**Never an acceptance.** The hold changes no state, no conflict and no check set, only the code and location of a problem already
found.

## The prototype round

`draft/Sketches/A5 Step 0/A5RulesPrototype.cs`, `dotnet run proto.cs -- -r17`. Round 16's review's 30 cases (`R16X`, rv-r16/work/rx)
are copied in unchanged.

- **`R17Hold`:** rule G (`R14G`), R4-M70's default taint as in round 16 (markers and the set rule), no P13 marker, and the held flag,
  computed at the fixpoint from the store rows (`P13Store`).
- **`R17PerPart`:** rule G whose W takes no step from a `ByArgument` holder, the per-part guard's program (R4-M76). It is the
  reference: a conflict depends on a by-argument store when `R14G` finds it and `R17PerPart` does not.

Run on 425 cases (the 382 earlier, round 16's 13, its review's 30): **0 failures**.

- **(i)** Every dependent conflict is `Unsupported_Kd`. These include closureRoute, closureRouteB, guardP13, rx-write, rx-seed,
  rx-storeThrough, rx-read, rx-chain, rx-loop, rx-E, rx-guardRoute, rx-guardLoop, rx-mixed, cleanRoute's xs end and r14B-u, P13's
  shape.
- **(ii)** Every body that is not held has R16Set's outcome (the unit as reviewed, the compiler at `b335393b`), Language errors
  included: the NoStore twins, the per-part twins, the default cases and all 381 other earlier cases.
- **(iii)** Under every mode (R16Set, R16Route, its two ablations, R17Hold), every (Loan, access) that rule G reports is reported
  exactly once, as a Language problem or a located `Unsupported_Kd` (round 16's review N5).
- **Held bodies:** 26 of 425: r14B-u; closureRoute, closureRouteB, closureRouteClean, guardP13, guardP13Dead, guardP13Clean and
  cleanRoute (round 16); and rx-chain, rx-chainClean, rx-loop, rx-loopClean, rx-joinCfg, rx-joinClosure, rx-E, rx-EClean,
  rx-guardRoute, rx-guardRouteClean, rx-guardLoop, rx-write, rx-storeThrough, rx-read, rx-mixed, rx-seed, rx-writeLv and rx-eJoin
  (the review's).

| Case | R16Set (today) | R16Route (superseded) | R17Hold (decided) | R17PerPart |
| --- | --- | --- | --- | --- |
| closureRoute | error `y = 5`; U `inspect(w)` | U both | U both | valid |
| closureRouteClean | error `y = 5`; U `inspect(w)` | the same | U both (cost) | error `y = 5` |
| guardP13 | error at d's cleanup; U `inspect(saved)` | U both | U both | valid |
| guardP13Clean | error at d's cleanup; U `inspect(saved)` | the same | U both (cost) | error at d's cleanup |
| cleanRoute | error at the append; U at xs's end | the same | U both (cost: the append) | error at the append |
| rx-write | error `c = 5` | error `c = 5` (B1) | U `c = 5` | valid |
| rx-seed | error at the borrow through w | the same (B1) | U | valid |
| rx-storeThrough | error `c = 5`; U at `$s` | the same (B1) | U both | valid |
| rx-dWriteOnly | error `c = 5` | the same | error `c = 5` (not held) | error `c = 5` |
| closureRouteD, guardDefault, rx-dChain, rx-dLoop, rx-dGuardRoute | error | U at the omitting call | error (not held) | error |
| r14B-u | U at arr's end | the same | the same | valid |

U is the located `Unsupported_Kd`. The full table is `-r17`'s output.

## Cost

**(iv)** A Language problem that does not depend on the store (`R17PerPart` reports it) becomes `Unsupported_Kd` only because its
body is held. This happens in 11 of the 26 held bodies:

- closureRouteClean (`y = 5`), guardP13Clean (err(ended)) and cleanRoute (the append's activation);
- rx-chainClean, rx-loopClean, rx-joinCfg, rx-joinClosure, rx-EClean, rx-guardRouteClean and rx-eJoin (`y = 5` or err(ended));
- rx-writeLv (two conflicts).

Route taint kept 9 of them (rx-joinCfg and rx-eJoin were already its T2 losses). Each is still a rejection, never an acceptance.
The compiler's cost is the ledger's held bodies: `count<S>`, B6-for, MemberThroughLayersTest's `[1 of uniq/P]` and any other body
whose P13 store holds it. Unit step 3 measures it.

**Unchanged default-taint limits.** R4-M70 is unchanged, so the default taint's analogs keep the Language errors they have today:
closureRouteD, closureRouteDJoin, guardDefault, guardDefaultOnly, rx-dChain, rx-dLoop and rx-dGuardRoute. Route taint would have
reclassified them. The planning session records them (round 17's review N3) as of R4-M70's class, beside its recorded err(ended)
limit: an inline default copy is precise and its result or slot is tied to the explicit argument by Origin, so its Language errors
through those routes are right per value (SPEC 15.6.4); a template call (a recursive default) over-approximates, and every attempt to
write a conservative error through one was rejected by Binding (unverified for an accepted recursive default). Naming: the compiler
probe called closureRouteD is the prototype's closureRouteDJoin; the "only" forms (closureRouteD, guardDefaultOnly, rx-dWriteOnly)
cannot be written in Kimigayo; rx-dGuardRoute is not type-correct as the prototype encodes it.

**rx-dWriteOnly.** A default-taint "only" form (a write through a default-only result) with no by-argument holder, so its body is not
held and it keeps its Language error. The planning session ruled this intended: the owner's choice leaves route taint for the default
taint out.

**Work.** The held flag costs one bit per body (or view), set where `Writes` returns a P13 target with a non-empty value; it adds no
V16 term. Round 16's per-holder rescans and marker bits are not needed.

## Implementation (unit G148 step 2, the planning session's rulings)

- **Grain: (body, view).** A view's store changes only that view's states, `MirReport` keys a problem by (code, subject, body), so a
  Language problem from a view not held and an `Unsupported_Kd` from a held one are two records, and the stratum runs each view's
  solve and replay before the next.
- **Timing: the forward solve.** `MirBorrows.Calls.cs` sets the fact where `Writes` returns a target past its clean count and the
  row's value is not empty, on the σ_in the Call plans with, in the forward solve only (never in the checks replay); `Initial` resets
  it per run. The solve applies each reached chain last at its converged state, so the fact is a may-fact that includes the
  fixpoint's, and the replay reports with it known.
- **One owner.** `Writes` is the only store path (a Call's rows today; A5-F7's capture stores and A5-F6's desc(c) stores take W there
  too), so the fact has one place. Every Loan-problem producer reports through `MirBorrows.Reports`' common `Report(code, anchor, via,
  at, reservation)` (`Checks.cs` at an access and at err(ended), `Activations.cs` at an activation and at err(destroy)); a future
  producer must too. The default taint's branch in the candidate overload returns its omitting-call `Unsupported_Kd` first.
- **Inlined scopes (R4-M69).** A Loan problem inside an inline default copy's scope is its template's and dropped whatever the hold
  says; the hold reclassifies only problems the held view owns (MirBorrowsTest's heldCopy).
- **Deleted:** P13's marker element, its `MarkerOf` row and location rule; `MirBorrows.cs` and `MirBorrows.Candidates.cs` are as
  before the unit except one line that seeds a holder's slot.

## Not verified

- Work and timing beyond MirBorrowsTest's scaling shape.
- The forward-solve fact's difference from the exact fixpoint fact (a may-fact; it can only add `Unsupported_Kd`).
