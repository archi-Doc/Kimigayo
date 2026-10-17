# Loan Termination Across Borrow Dereferences

Date: 2026-10-17. Status: proposal for the owner's decision; not adopted, integrated or implemented.
Requested outcome: accept case 23a with a small, predictable extension of S1, preserving soundness rather than
maximizing acceptance. This document proposes a change to the adopted rule; it does not authorize its implementation
or supersede DECISIONS R4-M39 by itself.

## 1. Decision proposed

Keep S1's static Loan identities, complete access paths and existing checks. Change its one **base** from the Place
before the **last** borrow dereference to the Place before the **first** borrow dereference. Keep the definite,
in-place destruction rule for ending a Loan. This recovers outer-holder replacement, including 23a, without a
points-to graph, value versions, another base column or an exception for a particular program.

Required meaning -> missing fact -> owner -> removal:
after an outer reference is replaced, access through its new value must not be blocked solely by a child of its old
value -> the current last-dereference base prevents that termination even when P3 already blocks competing aliases
-> A5's Loan fact producer computes the first-dereference base once per Borrow site and Semantics case, and the
existing ending rule consumes it -> the last-dereference selection and the listed 23a/30d losses become unnecessary.
Conflict paths, content provenance and Origin containment do not change.

This is an extension of S1, not a plan to import Rust's whole borrow checker. Writes through references and dynamic
indexes remain conservative. Soundness is a condition of adoption; imperfect precision is an intentional tradeoff.

## 2. Baseline and evidence

The baseline is S1 revision `61b1abf2`, merged into `origin/dev` at `b04b979e`, and its adoption recorded by
`172237f9` (DECISIONS R4-M39, on `claude/a5-step0-contracts`). S1 integration is proceeding separately; if it moves
from Proposals to Changes, use its frozen revision as the source and revise the authoritative rules on adoption.

| Record | What it establishes |
| --- | --- |
| [S1](<2026-10-16 Loan Identity by Base.md>) §§2.1, 2.2, 4.5, 8 | Last-dereference base; no end across a borrow dereference; 23a and 30d are listed losses. P3, by-value checks and transit are required. |
| [A5 plan](<2026-10-16 Complete Polonius A5 Plan.md>) §4.3; [A3](<../Sketches/A5 Step 0/design-A3.md>) §§2, 9 | The root-most recovery was not adopted. The reason recorded is the owner's selection of `within`, not a demonstrated performance disadvantage of the recovery. |
| [Prototype](<../Sketches/A5 Step 0/prototype.md>) round 6, `e32c5850` | On 92 hand-encoded programs, P3 plus the root-most ending accepts 23a/30d and rejects the crossing counterexample 29a. This is bounded evidence, not a proof. |
| Prototype round 7, `193ff65c` on `claude/a5-step0` | 99 programs; the report does not establish a fresh root-most comparison for the exact rule proposed here. |

The earlier `5d61561d` proposal used a first-dereference base, but its other rules differ from adopted S1. Do not
restore that revision wholesale. Retain the later shared-layer dependencies, `replace` contract, observing
destruction, by-value checks and object-step classification.

Two evidence gaps must be closed before implementation is accepted:

- In `193ff65c:A5RulesPrototype.cs`, the root-most branch of `EndedBy` uses `Path.Within`; its `SegEq` treats a
  dynamic index as matching a constant index. That is may-overlap, not definite destruction. Reuse the production
  definite matcher and add negative index cases; the old variant is not the specification.
- The prototype and S1 differ on Loans formed past shared references. Map prototype facts to S1's actual
  shared-layer rule before treating the comparison as evidence for the whole language.

The producer audit also includes A3 F6's `preserves results` phantom Loans (`Phi_c` and its disturbed form).
Determine whether each uses S1's child-ending contract or a separate call contract, and preserve that contract;
do not route an unclassified Loan through the new rule or add a feature-specific bypass. The shared representation
must publish the distinction it already requires. An unresolved contract blocks the implementation unit.

## 3. The rule

### 3.1 One base, one ending rule

For a child Loan L with complete path Pi:

1. Its base B is the syntactic prefix immediately before Pi's first **borrow dereference**. An own step through
   `obj`, `rc` or `arc` is storage, not a borrow dereference. Classification is per Semantics case.
2. A Loan with no borrow dereference has no such ending base. Its ordinary lifetime and conflicts still apply.
   A Borrow past a shared reference follows S1's existing rule; this proposal creates no additional Loan for it.
3. End L only when an operation definitely destroys B, or a Move Path containing B, in place. Use S1's existing
   inline/own storage containment and exact constant-index rules. A required dynamic/non-path index prevents an
   ending proof. Do not truncate such a base to a convenient tracked ancestor.
4. The destroying operations remain `=`, storage end and the published `replace` target contract. A write through
   a borrow dereference or a non-path index ends nothing. Move, `exchange`, `swap` and ordinary method calls do not
   end a Loan by transferring its old reference elsewhere.

Illustrative notation; the relation names denote existing S1 responsibilities, not new persistent tables:

```text
base(L, B)      := prefix of path(L) before its first borrow dereference
ended(L, P)    := base(L, B) and destroyed_in_place(G, P)
                 and definite_storage_contains(G, B)
step(L, P, Q)  := edge(P, Q) and not ended(L, P)
```

`definite_storage_contains` requires a Move Path target and the established inline/own containment, no borrow
dereference, and identical constant indexes. It is not the conflict overlap relation. In particular:

| Loan path | Proposed base | Operation | Ends this Loan? |
| --- | --- | --- | --- |
| `rr.*.*` | `rr` | `rr = other` | Yes, after checking the old-value destruction. |
| `h.*.next.*.value` | `h` | `h = other` | Yes. |
| `s.r.*.value` | `s.r` | Whole replacement of `s` | Yes, subject to its destruction check. |
| `rr.*.*` | `rr` | `rr@follow = other` | No: a write through a reference. |
| `refs[i].*.value` | `refs[i]` | `refs[0] = other` | No: index identity is not proven. |
| `refs[0].*.value` | `refs[0]` | `refs[1] = other` | No: different constant indexes. |
| `rr.*.items[i].*` | `rr` | `rr = other` | Yes: the replaced reference is definitely `rr`; the later index is not part of its base. |
| `s.field` | None | Whole replacement of `s` | No child-ending exemption; ordinary conflict checking applies. |

The dynamic-index restriction is inherited conservatism. This unit does not add a broader whole-container rule
for a base containing an unknown index. A generic call is not treated as `replace` merely because it destroys
something: the producer's existing target contract must guarantee in-place destruction without retaining or
returning the old target value. No new intrinsic exception is introduced.

### 3.2 Conditions kept together

- **P3 in both orders.** A Borrow or Move is deep across borrow dereferences; shared Borrows count. A Loan on a
  prefix also constrains accesses through its extensions. Reads/writes remain shallow and observing destruction
  retains its exclusive-layer reach. Conflict checks use the complete path, never the shortened base.
- **Parent dependencies.** A child keeps the required Loans of followed references, with S1's last-shared-layer
  rule and Origin containment unchanged. Changing the termination base must not shorten dependency collection.
  Parent Loans are governed by their own lifetimes and ending rules; they are not erased just because a child ends.
- **Checks before ending.** Check the operation, old-value destruction and values in transit using the incoming
  facts. Only propagation after the operation applies `step`. An ended Loan must not reappear through an issue,
  parent materialization, ordinary flow or write-through flow at that same point.
- **Check by value.** Use S1's checked-value set, including stored values needed afterwards or escaping. Exclude
  replaced old values and the reservation's own argument only as the established rules permit. Never excuse a Loan
  by its ID or by membership in the accessed reference's carried set. Another value may carry the same site ID.
- **Owning storage remains protected.** Direct Loans on owned payloads/handles remain subject to S1's ordinary
  checks. Replacing a borrow handle is not releasing the owning handle. A surviving `rc`/`arc` clone is not evidence
  that all payload storage was destroyed; shared access and retained dependencies must account for it.

### 3.3 Expected behavior

Case 23a becomes valid:

```kimi
var x = 1
var z = 3
var slot = x@uniq
var slot2 = z@uniq
var rr = slot@uniq
let c = rr@follow@follow@uniq   // Path rr.*.*, base rr.
rr = slot2@uniq                 // Ends this child Loan; c keeps the required parent Loans.
rr@follow@follow = 7            // Writes z.
c@follow = 1                   // Writes x.
```

The competing-alias counterexample remains an error. `S` below has a mutable `p: uniq/i32` Field; initialization
and Origins are omitted to focus on the rule.

```kimi
var q = s@uniq
let c = q.p@follow@uniq         // Path q.*.p.*, base q.
let w = q@follow@ref            // Error: deep shared Borrow of a prefix of c's path.
q = o@uniq
c@follow = 5
inspect(w.p@follow)
```

If `w` is formed first and used later, forming `c` is the error. A receiver Borrow that lets a call retain the old
route is checked the same way. After the valid 23a replacement, a direct write to `x`, a write through old `slot`,
or a conflicting reborrow of `slot` while `c` is used later must still fail.

30d, traversal through stored `uniq` links followed by a read through the new link, is the same recovery family.
30c, writing through the old route before replacing the traversal holder while its child is live, remains an error.
No rule recognizes a list, a particular expression spelling or either case number.

## 4. Safety argument and its limits

The old inner reference can survive an outer-holder replacement. Therefore the old last-base argument, "ending
destroys the inner reference," cannot justify this rule. The required invariant is instead:

> After ending, each surviving route that could conflict with a still-used child's referent is either governed by
> the surviving descendant's own restrictions or constrained by retained parent Loans and lifetime/capability
> contracts. An independent competing route cannot become usable merely because the outer holder was replaced.

The candidate argument separates the routes:

- The replaced outer slot holds its new reference. It no longer supplies the old route, so its old path restriction
  can end. The child's reference value and its required dependencies survive.
- Original storage and older ancestor routes are protected by retained parent Loans. The direct `x` and `slot`
  negative cases check this obligation.
- A conflicting live alias formed by borrowing or moving a prefix is blocked by P3, whether the alias or child is formed first.
  This includes implicit receiver/argument/capture Borrows and targets of transferring calls.
- Sibling Loans with the same base can end together. Their coexistence must have passed conflict checks before
  ending, and their values and required dependencies survive. A new child formed through `c` has its own base and
  Loan; it does not acquire the old outer holder as its base merely because its parent once used it.
- Calls retain only the dependencies their published contracts allow; reservations and activation still check
  conflicting arguments. Shared layers and owning handles retain their existing rules.

This is a reviewable proof obligation, not a completed proof. Repeated sites, joins, transfers, escaping storage,
observing destruction and shared/own steps must satisfy it under the exact fact representation. An unexplained
unsound acceptance blocks adoption; do not repair it with an expression-specific exception or an authority bypass.
If the common rule requires substantial new state, return the design to the owner rather than silently growing it.

## 5. Cost and alternatives

| Option | Benefit | Cost / decision |
| --- | --- | --- |
| Keep last-dereference base | No rule change | Continues to reject 23a/30d; baseline. |
| **First-dereference base** | Recovers the family using existing path and ending facts | Recommended. One canonical base selection and exact matcher; retains conservative indirect/index writes. |
| Keep last base plus a separate outer ending relation | Similar recovery | Rejected: two representations of the same termination decision and another propagation condition. |
| Track actual referents or version each replaced value | More precision | Rejected for this unit: new state, joins and alias/update obligations. |
| End on assignments through references as well | More precision | Deferred: requires a common definite-update rule beyond direct Move Paths. |
| Special-case 23a or exempt the current Loan ID | Small apparent patch | Rejected: duplicates a rule or reintroduces the repeated-site unsoundness. |

The main design and an independently derived alternative both selected the first-dereference base. The independent
analysis additionally identified the prototype's may-overlap index matcher; the rule above explicitly excludes it.

Implementation expectation, not a measurement:

- Keep the existing Loan row and base field; replace the prefix selection. No new persistent graph, table, mode,
  projection kind or per-iteration state. No new fixed-point dimension.
- A prepass remains O(sum of static Loan-path lengths), or uses an existing interned-prefix walk. Loan-set and
  dataflow costs retain their present dimensions; broader ending does not prove fewer iterations or a speedup.
- Expected code scope is one base-selection responsibility, its existing ending matcher and focused tests. Remove
  the displaced last-base logic in the same behavior unit. If production contains no implementation yet, implement
  the chosen rule directly instead of creating both alternatives.
- Generated code needs no additional runtime check, reference count or allocation. Actual compiler time,
  allocations and retained memory must be measured before claiming a performance result.

## 6. Implementation and verification plan

Navigation: [CODEMAP](../../docs/dev/CODEMAP.md), [architecture](../../docs/dev/COMPILER_ARCHITECTURE.md), A3 F1/F2
and its joint-core unit. A5's planned `MirBorrows` owns Loan paths, bases and ending; `MirLoanSets` remains the set
representation. `MirMoveData` supplies existing Move Path facts, and MIR/A2/A4 publish operation and destruction
facts. No Binding/legacy-Analysis patch decides termination. Confirm these owners against the branch being
implemented; follow the architecture's target files rather than adding another owner.

| Unit | Work and completion condition |
| --- | --- |
| 1. Design and executable comparison | Independently review the surviving-route argument and alternatives. In the existing prototype, implement the exact rule and correct the ending matcher. Reconcile shared-layer and phantom-Loan contracts with S1. Run the complete current corpus and the missing cases below against the baseline. Record exact sources and expected verdicts. Prototype variants remain experimental, not production modes. |
| 2. Adoption and specification | After the argument and exact-rule evidence pass independent review, obtain the owner's adoption. Then supersede only R4-M39's last-base/within restriction and align the sources in §7. Preserve the other S1 requirements. A failed safety obligation keeps the current rule in force. |
| 3. A5 implementation | Publish the base once per site/case and consume it through the canonical ending/step path. No consumer recomputes it from syntax. If restructuring is necessary, first commit behavior-preserving restructuring under VERIFICATION. |
| 4. Focused verification and measurements | Assert verdicts and check locations at the lowest public A5/MIR layer; reuse the owning tests. Run the required Unit/Session checks and selected generated-code fixtures, then compare matched compiler workloads. |

Required comparisons, reusing existing cases rather than cloning them:

| Family | Required outcome / check |
| --- | --- |
| 23a, 30d | Accepted; assert new and old referents remain distinct in one selected generated-code fixture. |
| 29a, reverse order, receiver/call alias | Rejected when the conflicting Borrow forms. |
| 20a–20f, 30c | Preserve the established verdicts; no self-authority from a repeated Loan ID. |
| 23b; direct `x`, old `slot` and an additional child of `c` | Preserve ancestor and descendant protection after the outer end. |
| Simultaneous sibling termination | Two compatible children sharing the first base can both end on its replacement; their values and required parent dependencies survive. Conflicting siblings must be rejected before ending. |
| Conditional replacement and loops | Both-branch replacement permits the recovered access; one branch that retains the old route still conflicts. Include a back edge and a live value from an earlier iteration. |
| 21a/21b; 24a–24c; 25a–25c; 26a/26b | Preserve established verdicts through temporaries, observing Drop, dead/live/escaped stores and destroying calls. No kill before the check. |
| Move, `exchange`, `swap`, `replace` | Transfer forms stay rejected with a live conflicting child, including calls. Only the established destroying-target contract is exempt. |
| Constant/dynamic indexes and sibling Fields | Exact same constant can end; different or unknown indexes cannot accidentally kill. Test the matcher directly and a program with later old-route access. |
| Shared layers, Semantics pairs, owning and borrowed handles | Compare against S1's actual materialization and containment. Include a surviving `rc`/`arc` clone and protection of its payload; do not infer physical destruction from handle replacement. |
| Calls and F6 phantom Loans | Recheck round-7 33a–33c after its recorded reservation correction, plus receiver-holder replacement with a retained result. Verify parent materialization, disturbance and ending against the producer's contract. |
| Unchanged boundaries | Single-dereference programs and no-dereference Loans keep their verdicts. Indirect-slot replacement stays conservative. |

Use bounded mutation comparisons to remove P3, parent retention, transit or by-value checking independently; the
corresponding negative cases must expose the missing safeguard. This validates the tests' relevance, not general
soundness. Do not ship switches for these experiments.

For performance, compare identical inputs under identical build/runtime conditions: replacement-heavy straight-line
bodies, branching bodies, repeated-site loops and many disjoint sibling paths, increasing one size dimension at a
time. Record compile time, allocated bytes, retained memory, Loan/path counts and transfer visits. Existing workloads
must not acquire a new state dimension or an unexplained scaling regression. Use `src/Benchmark` for timings and
existing allocation-test owners (`Purpose=Allocation`); preserve failed evidence and native allocation assertions.
If a material cost regression appears, report its measured cause before accepting a more complicated design.

Follow [VERIFICATION](../../docs/dev/VERIFICATION.md) for implementation checks, exact-input evidence and
`verify-commit.ps1`. Select native fixtures explicitly; NativeAOT needs a separate request. Writing this proposal
requires only diff/reference review and independent design review; no compiler build or prototype rerun is claimed.

## 7. Adoption mapping and boundaries

This proposal reopens the first-base recovery because the owner now asks for 23a acceptance with bounded cost.
It does not claim the former no-crossing decision was a soundness proof against the recovery.

On adoption, update together:

- **SPEC:** §15.6.3's base and ending rule; the Base glossary entry in Appendix E; affected examples and §15.6
  wording that depends on the base. Retain §15.6.2/P3 and §15.6.6 destruction checks. Review §13.5.5's handle
  guarantees for consistency without weakening them. The specification must stand alone, not depend on this draft.
- **DECISIONS:** a superseding entry naming R4-M39 and the newly selected precision/cost tradeoff. Do not erase its
  historical adoption. No new vocabulary or structure-limit exception is requested.
- **A5 plan/A3:** replace last-base selection and the no-crossing decision; remove 23a/30d from the loss rows only
  after the agreed checks establish the new boundary. Align the precise by-value check rather than copying the
  ambiguous "all Loans carried by the accessed holder are excused" wording.
- **Proposal lifecycle:** if S1 is still open, revise only its unfrozen affected text under the adoption's scope;
  if integrated, leave its frozen text unchanged and record this new proposal's mapping in `draft/INTEGRATED.md`.
  Record the intake and its exact integrated sections in either case. Update STATUS/PLAN when their verified
  boundaries or next actions change. SPECJ, GUIDE and historical records require separate scope.

Remaining limits are intentional: no general points-to analysis, no ending on indirect or dynamic-index writes,
no relaxation of P3 to preserve whole-prefix aliases, no change to call effects or Origin inference. The settled
[borrow-only uniq pair rule](../../docs/SETTLED.md#a-borrow-only-uniq-pair-follow-bounded-by-its-slot-alone) continues
to require every relevant reaching lifetime; an earlier Loan end does not authorize a detached return.

The Principles hold through one base and one ending path, decisions derived from local paths and published
contracts, explicit replacement/destruction semantics, and diagnostics attached to existing operation and Borrow
sites. Evidence and cost measurements remain tied to exact source/configuration rather than an assertion that a
small rule must be fast or sound.
