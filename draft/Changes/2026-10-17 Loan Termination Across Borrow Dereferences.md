# Loan Termination Across Borrow Dereferences

Date: 2026-10-17, revised the same day after an independent review and prototype round 8 (§2), and for round 9
(PLAN G113). Status: adopted by the owner on 2026-10-17 as option (c), the same rule worded without the base
(DECISIONS R4-M52), and integrated into SPEC on 2026-10-17 (`draft/INTEGRATED.md`); not implemented.
Requested outcome: accept case 23a with a small, predictable extension of S1, preserving soundness rather than
maximizing acceptance. This document proposes a change to the adopted rule; it does not authorize its implementation
or supersede DECISIONS R4-M39 by itself.

## 1. Decision proposed

Keep S1's static Loan identities, complete access paths and existing checks. Change its one **base** from the Place
before the **last** borrow dereference to the Place before the **first** borrow dereference. Keep the definite,
in-place destruction rule for ending a Loan. This recovers outer-holder replacement, including 23a and the
`uniq`-linked walks 30d, 35a and 35b, without a points-to graph, value versions, another base column or an exception
for a particular program. It also makes the one-step form agree with the two-step form: in `t = rr@follow@uniq;
c = t@follow@uniq`, `t`'s Loan has base `rr` under either rule, so S1 already accepts 23a written that way (37).

Required meaning -> missing fact -> owner -> removal:
after an outer reference is replaced, access through its new value must not be blocked solely by a child of its old
value -> the current last-dereference base prevents that termination even when P3 already blocks competing aliases
-> A5's Loan fact producer computes the first-dereference base once per Borrow site and Semantics case, and the
existing ending rule consumes it -> the last-dereference selection and the 23a/30d/35a/35b losses become unnecessary.
Conflict paths, content provenance and Origin containment do not change.

This is an extension of S1, not a plan to import Rust's whole borrow checker. Writes through references and dynamic
indexes remain conservative. Soundness is a condition of adoption; imperfect precision is an intentional tradeoff.

## 2. Baseline and evidence

The baseline is S1 revision `61b1abf2`, merged into `origin/dev` at `b04b979e`, and its adoption recorded by
`172237f9` (DECISIONS R4-M39, merged into `dev` at `48ca66d1`). S1 was integrated into SPEC §15.6 at `4372b058`
(`draft/INTEGRATED.md`) and moved to `draft/Changes`, where its text is frozen. §7 states the change against the
current SPEC.

| Record | What it establishes |
| --- | --- |
| [S1](<../Changes/2026-10-16 Loan Identity by Base.md>) §§2.1, 2.2, 4.5, 8 | Last-dereference base; no end across a borrow dereference; 23a and 30d are listed losses. P3, by-value checks and transit are required. |
| [A5 plan](<2026-10-16 Complete Polonius A5 Plan.md>) §4.3; [A3](<../Sketches/A5 Step 0/design-A3.md>) §§2, 9 | The root-most recovery was not adopted. The reason recorded is the owner's selection of `within`, not a demonstrated performance disadvantage of the recovery. |
| [Prototype](<../Sketches/A5 Step 0/prototype.md>) round 6, `e32c5850` | On 92 hand-encoded programs, P3 plus the root-most ending accepts 23a/30d and rejects the crossing counterexample 29a. This is bounded evidence, not a proof. |
| Prototype round 7, `193ff65c` | 99 programs; the report does not establish a fresh root-most comparison for the exact rule proposed here. |
| Prototype round 8 (this revision) | 109 programs. `FirstDerefBase`, the rule of §3.1, accepts 23a, 30d, 34c, 35a and 35b and rejects 34a and 34b, which the round-6 variant accepts (unsound). It and design A are unsound when an F6 phantom lacks the receiver's Loans (36a, 36c; below). |
| Prototype round 9, `f1ab1f2f` (PLAN G113) | 113 programs. With A3's F6 erratum `desc(c)`, `FirstDerefBase` and design A both reject 36a–36e and 36g and keep `takeTwo`. Both accept 36h, an A3 gap outside this proposal (PLAN G119; below). |
| [RustComparison.rs](<../Sketches/A5 Step 0/RustComparison.rs>) | rustc 1.97.1 accepts 23a, 34c, 35a and 35b, and rejects 34a and the write through 23a's old `slot`. |

The earlier `5d61561d` proposal used a first-dereference base, but its other rules differ from adopted S1. Do not
restore that revision wholesale. Retain the later shared-layer dependencies, `replace` contract, observing
destruction, by-value checks and object-step classification.

Rounds 8 and 9 establish five points:

- **The matcher is part of the rule.** The root-most branch of `EndedBy` (`KillAcrossDeref`) uses `Path.Within`,
  whose `SegEq` matches a dynamic index with a constant one. So `arr[0] = …` ends a child based at `arr[i]`, and a
  later write through `arr[i]` is accepted (34a, one dereference; 34b, two). Round 6's 92 cases had no such program.
  The plan's matcher errs the other way: it refuses a runtime index after the destroyed Move Path, so it keeps a
  child based at `arr[i]` after `arr = …` (34c), which S1's text ends. §3.1 follows S1's text.
- **S1's examples understate its loss.** S1 §4.5 states the loss in general (once the holder holds a new reference,
  access through it to an overlapping Place conflicts while the child lives), and its scans counted none in the
  repository. But its examples show only the write 23a and the read 30d, and its §3 says the next step of a walk
  stays valid (30b). In 35a and 35b, the next step itself is an error: a value Borrowed through the walk's holder,
  `b = node.value@uniq`, carries the previous step's Loan, whose path now names the new node's link, and the next
  step's Borrow conflicts with it. This is the `to_refs` shape, keeping a Borrow of each node while advancing; it is
  valid under the current SPEC and in Rust. It weighs on the precision judgment behind R4-M39.
- **The rule changes only these verdicts.** Against design A (A3's encoding of S1) on the 109 cases (113 in round 9),
  `FirstDerefBase` makes 23a, 30d, 34c, 35a and 35b valid and moves 30c's error to the write through the old link.
  In round 8 it also moved 36b's error to `cur = a` and made 36a unsound (next point). In round 9 it reports 36a and 36b
  only at `cur = a`, where design A also reports the clear. Nothing else changes.
- **An F6 phantom must carry its parent Loans (met).** A3 F6 issues `Phi_c` on the receiver's referent into an
  abstract call's result. Rounds 7 and 8 encoded it as the result's only Loan there. Design A was then already
  unsound: after `first = cur.inner.take(); cur = b`, nothing keeps `a`, and `a.inner.clear()` is accepted (36c).
  The first-dereference base also ends `Phi_c` at `cur = b`, so `cur = a; cur.inner.clear()` was accepted too (36a).
  §4 therefore requires that every value carrying a child Loan or `Phi_c` also carries its parent Loans.
  - A3's F6 erratum `desc(c)` (prototype round 9, PLAN G113) meets this. The result keeps the receiver's value with
    the receiver's own Loan replaced by `Phi_c`, so `Phi_c` travels with its parent Loans. Exempting `Phi_c` from
    base ending would not have fixed 36c.
  - The owner approved its cost, loss P12, on 2026-10-17 (A3 §9 item 10).
  - In round 9, `FirstDerefBase` rejects 36a and 36b at `cur = a`, and 36c and 36d at the clear. Design A rejects
    them too.
  - The open F6 stores item (PLAN G113, H) must keep the precondition too: its fix issues `desc(c)` into stores.
- **36h does not affect the proposal.** A3's write check misses SPEC §15.6.7's protection of the references a call
  reservation was reached through, so `first = cur.inner.takeWith(do { cur = b; 0 })` is accepted (PLAN G119).
  - The gap is in the check, not in the base: both rules give 36e–36h the same verdicts, and SPEC rejects 36h at the
    write under either base.
  - This proposal changes no reservation rule. Under the first-dereference base that write would also end the
    reservation's own Loan. Accesses are checked on the state before their point (A3 §5, `σ_in`), so the ending does
    not hide the write.
  - So G119's check belongs at the write. Deferred to the activation, it would come after a first-dereference ending.

Another evidence gap remains before implementation is accepted (PLAN G110): the prototype and S1 differ on Loans
formed past shared references. Map prototype facts to S1's actual shared-layer rule before treating the comparison
as evidence for the whole language.

With their parent Loans (`desc(c)`), `Phi_c` and its disturbed form may keep S1's child-ending contract or a separate
call contract. Under child ending, round 9 rejects 36a–36d and 36g under either base. A separate contract keeps
`Phi_c` at least as long, so it should reject them too and may reject more; it was not run. The producer publishes the
choice; no feature-specific bypass is added.

## 3. The rule

### 3.1 One base, one ending rule

For a child Loan L with complete path Pi:

1. Its base B is the syntactic prefix immediately before Pi's first **borrow dereference**. An own step through
   `obj`, `rc` or `arc` is storage, not a borrow dereference. Classification is per Semantics case.
2. A Loan with no borrow dereference has no such ending base. Its ordinary lifetime and conflicts still apply.
   A Borrow past a shared reference follows S1's existing rule; this proposal creates no additional Loan for it.
3. End L only when an operation destroys in place a Move Path G that is B or a prefix of B. G's steps are compared
   exactly, so a constant index matches only the same constant and never a runtime index, which forms no Move Path
   ([§15.1.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)). B's steps
   after G are inline steps, runtime indexes included, or own steps. This is S1's rule (§2.1: `prefix(G,B)` "compares
   exactly") with the new B; the plan's `within` matcher also refuses a runtime index after G, which loses 34c.
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

`definite_storage_contains` requires G to be a Move Path and an exact prefix of B followed only by inline or own
steps. It is not the conflict overlap relation. In particular:

| Loan path | Proposed base | Operation | Ends this Loan? |
| --- | --- | --- | --- |
| `rr.*.*` | `rr` | `rr = other` | Yes, after checking the old-value destruction. |
| `h.*.next.*.value` | `h` | `h = other` | Yes. |
| `s.r.*.value` | `s.r` | Whole replacement of `s` | Yes, subject to its destruction check. |
| `rr.*.*` | `rr` | `rr@follow = other` | No: a write through a reference. |
| `refs[i].*.value` | `refs[i]` | `refs[0] = other` | No: `refs[0]` is not a prefix of `refs[i]` (34a). |
| `refs[i].*.value` | `refs[i]` | `refs = other` | Yes: destroying `refs` destroys every element (34c). |
| `refs[0].*.value` | `refs[0]` | `refs[1] = other` | No: different constant indexes. |
| `rr.*.items[i].*` | `rr` | `rr = other` | Yes: the replaced reference is definitely `rr`; the later index is not part of its base. |
| `s.field` | None | Whole replacement of `s` | No child-ending exemption; ordinary conflict checking applies. |

Only the destroyed Move Path is compared, so no index identity is assumed. A generic call is not treated as `replace`
merely because it destroys something: the producer's existing target contract must guarantee in-place destruction
without retaining or returning the old target value. No new intrinsic exception is introduced.

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
So are 35a and 35b: a Borrow of the new node's Field, such as `b = node.value@uniq`, kept while the walk advances
again. Under the last-dereference base, `b` carries the previous step's Loan through `node`, and the next step's
Borrow conflicts with it; here that Loan ends when `node` is replaced. 30c, writing through the old route before
replacing the traversal holder while its child is live, remains an error, and so do 34a, 34b and the F6 cases 36a
and 36c, now that their precondition holds (§2, round 9). No rule recognizes a list, a particular expression
spelling or a case number.

## 4. Safety argument and its limits

The inner references survive an outer-holder replacement, so S1's argument, that the end destroys the reference
followed last (S1 §2.2), does not apply. The argument goes instead by the holders on the path. Write Pi as
`B *1 Q1 *2 ... *n Qn`, where each `*k` is a borrow dereference and no `Qk` contains one. Y1 = B holds the reference
followed at `*1`; for k > 1, Yk = `B *1 Q1 ... *(k-1) Q(k-1)` holds the one followed at `*k` and lies in the referent
of Y(k-1)'s reference. The argument assumes that every value carrying L also carries L's parent Loans, the Loans of
the references at Y1 ... Yn. An F6 phantom satisfies it through A3's `desc(c)` erratum, whose loss P12 the owner
approved on 2026-10-17 (§2; round 9, 36a–36d). When L ends at the destruction of G, the routes to L's Place are:

- **Through B.** B's old reference was destroyed in place, not transferred. While L is checked, P3 rejects every Move
  or Borrow of a prefix of Pi in either order, including `exchange` and `swap` targets and implicit receivers,
  arguments and captures, and no transferring operation ends L (§3.1 rule 4). After the destruction, the name Pi
  denotes the new value's referent, which L never protected.
- **To Y2 ... Yn and the final Place from elsewhere.** Each lies in the referent of the preceding holder's reference.
  The child's values keep the Loans those references carry, its parent Loans (§3.2), so every other route to that
  storage conflicts while the child is used: the direct `x` and old `slot` cases of §3.3. A parent Loan that ends at
  the same point ends for the same reason, its own holder's destruction, and the argument applies to it in turn. The
  chain stops at Loans without a borrow dereference, which no base ends, or at storage the body reaches only through
  a destroyed holder, such as a parameter's referent.
- **Siblings.** Children with the same B end together. They were checked against each other while both were live,
  and each keeps its own parent Loans. A child formed through `c` has its own path and base; it does not inherit B.
- **Calls and other layers.** Calls retain only what their published contracts allow; reservations and activation
  still check conflicting arguments. A Borrow past a shared reference forms no Loan to end (§3.1 rule 2), and owning
  handles keep their existing rules.

Rust's assignment kill has the same structure: an assignment to a local ends the loans of every path through it
(S1 §4.5), and its reborrow constraints keep the loans of the references followed, as the parent Loans do here.
rustc accepts and rejects the corresponding programs as this rule does (§2, RustComparison.rs).

This argument is reviewable, not a completed proof. Repeated sites, joins, transfers, escaping storage,
observing destruction and shared/own steps must satisfy it under the exact fact representation. An unexplained
unsound acceptance blocks adoption; do not repair it with an expression-specific exception or an authority bypass.
If the common rule requires substantial new state, return the design to the owner rather than silently growing it.

## 5. Cost and alternatives

| Option | Benefit | Cost / decision |
| --- | --- | --- |
| Keep last-dereference base | No rule change | Continues to reject 23a/30d/35a/35b; baseline. |
| **First-dereference base** | Recovers the family using existing path and ending facts | Recommended. One canonical base selection and exact matcher; retains conservative indirect/index writes. |
| Keep last base plus a separate outer ending relation | Similar recovery | Rejected: two representations of the same termination decision and another propagation condition. |
| Track actual referents or version each replaced value | More precision | Rejected for this unit: new state, joins and alias/update obligations. |
| End on assignments through references as well | More precision | Deferred: requires a common definite-update rule beyond direct Move Paths. |
| Special-case 23a or exempt the current Loan ID | Small apparent patch | Rejected: duplicates a rule or reintroduces the repeated-site unsoundness. |

The main design and an independently derived alternative both selected the first-dereference base. The independent
analysis additionally identified the prototype's may-overlap index matcher, which round 8 shows unsound (34a/34b);
the rule above excludes it.

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
| 1. Design and executable comparison | Round 8 implemented the exact rule (`FirstDerefBase`), ran 109 cases and found the F6 precondition (§2). Round 9 (113 cases) met it with A3's `desc(c)` erratum (PLAN G113; P12 approved 2026-10-17). Remaining: independently review §4's argument and the alternatives; reconcile shared-layer contracts with S1; encode the missing cases below and run them against the baseline. Record exact sources and expected verdicts. Prototype variants remain experimental, not production modes. |
| 2. Adoption and specification | After the argument and exact-rule evidence pass independent review, obtain the owner's adoption. Then supersede only R4-M39's last-base/within restriction and align the sources in §7. Preserve the other S1 requirements. A failed safety obligation keeps the current rule in force. |
| 3. A5 implementation | Publish the base once per site/case and consume it through the canonical ending/step path. No consumer recomputes it from syntax. If restructuring is necessary, first commit behavior-preserving restructuring under VERIFICATION. |
| 4. Focused verification and measurements | Assert verdicts and check locations at the lowest public A5/MIR layer; reuse the owning tests. Run the required Unit/Session checks and selected generated-code fixtures, then compare matched compiler workloads. |

Required comparisons, reusing existing cases rather than cloning them:

| Family | Required outcome / check |
| --- | --- |
| 23a, 30d, 35a, 35b | Accepted; assert new and old referents remain distinct in one selected generated-code fixture. |
| 29a, reverse order, receiver/call alias | Rejected when the conflicting Borrow forms. |
| 20a–20f, 30c | Preserve the established verdicts; no self-authority from a repeated Loan ID. |
| 23b; direct `x`, old `slot` and an additional child of `c` | Preserve ancestor and descendant protection after the outer end. |
| Simultaneous sibling termination | Two compatible children sharing the first base can both end on its replacement; their values and required parent dependencies survive. Conflicting siblings must be rejected before ending. |
| Conditional replacement and loops | Both-branch replacement permits the recovered access; one branch that retains the old route still conflicts. Include a back edge and a live value from an earlier iteration. |
| 21a/21b; 24a–24c; 25a–25c; 26a/26b | Preserve established verdicts through temporaries, observing Drop, dead/live/escaped stores and destroying calls. No kill before the check. |
| Move, `exchange`, `swap`, `replace` | Transfer forms stay rejected with a live conflicting child, including calls. Only the established destroying-target contract is exempt. |
| Constant/dynamic indexes and sibling Fields | Exact same constant can end; different or unknown indexes cannot accidentally kill. Test the matcher directly and a program with later old-route access (34a, 34b), and the whole replacement `refs = other` with a base `refs[i]`, which ends it (34c). |
| Shared layers, Semantics pairs, owning and borrowed handles | Compare against S1's actual materialization and containment. Include a surviving `rc`/`arc` clone and protection of its payload; do not infer physical destruction from handle replacement. |
| Calls and F6 phantom Loans | Recheck round-7 33a–33c after its recorded reservation correction, and 36a–36d: a value carrying `Phi_c` also carries the receiver's parent Loans (A3 `desc(c)`), so both are rejected through `cur` and through `a`. Rerun 36h under both bases once PLAN G119's check exists. Verify parent materialization, disturbance and ending against the producer's contract. |
| Unchanged boundaries | Single-dereference programs keep S1's verdicts; only the plan's narrower matcher differs, on 34c. No-dereference Loans keep theirs. Indirect-slot replacement stays conservative. |

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
`verify-commit.ps1`. Select native fixtures explicitly; NativeAOT needs a separate request. This revision ran the
prototype outside the repository (round 8; round 9 for G113); no compiler build is claimed.

## 7. Adoption mapping and boundaries

This proposal reopens the first-base recovery because the owner now asks for 23a acceptance with bounded cost, and
because S1's examples understate its loss (35a, 35b; §2). It does not claim the former no-crossing decision was a
soundness proof against the recovery. If the owner keeps the last-dereference base, A3's loss rows add 35a and 35b
(S1's frozen text stays; `draft/INTEGRATED.md` records the correction), and A3's matcher follows SPEC §15.6.3 on
34c. The F6 precondition (§2) applies under either base. A3's `desc(c)` meets it for results, and the open stores item
(PLAN G113) must keep it.

On adoption, update together:

- **SPEC:** §15.6.3's base and ending rule; the Base glossary entry in Appendix E; affected examples and §15.6
  wording that depends on the base. Retain §15.6.2/P3 and §15.6.6 destruction checks. Review §13.5.5's handle
  guarantees for consistency without weakening them. The specification must stand alone, not depend on this draft.
  The text below replaces the current sentences; the rest of S1's integrated text is unchanged.
- **DECISIONS:** a superseding entry naming R4-M39 and the newly selected precision/cost tradeoff. Do not erase its
  historical adoption. No new vocabulary or structure-limit exception is requested.
- **A5 plan/A3:** replace last-base selection, the no-crossing decision and the `within` matcher's refusal of a
  runtime index after G; remove 23a/30d from the loss rows, and leave 35a/35b out, only after the agreed checks
  establish the new boundary. Align the precise by-value check rather than copying the ambiguous "all Loans carried
  by the accessed holder are excused" wording.
- **Proposal lifecycle:** S1 is integrated and frozen. Record this proposal's intake and its exact integrated
  sections in `draft/INTEGRATED.md`. Update STATUS/PLAN when their verified boundaries or next actions change.
  SPECJ, GUIDE and historical records require separate scope.

SPEC text, against the current SPEC (S1 as integrated at `4372b058`):

- **§15.6.3,** the sentences on the base. Now: "The child's **base** is the Place before the last dereference of its
  path. Destroying in place a Move Path that is the base or contains it with no dereference between them ends the
  child, once … A child whose base lies behind a dereference does not end this way; it ends when no value that
  carries it may be used." New: "The child's **base** is the Place before the first dereference of its path.
  Destroying in place a Move Path whose access path is a prefix of the base's ends the child, once …" The elided
  text is unchanged; the last quoted sentence is deleted.
- **Appendix E,** the Base row. Now: "The Place before the last dereference (§15.6.2) in a child Loan's access path;
  destroying in place a Move Path that is it or contains it with no dereference between them ends the child." New:
  "The Place before the first dereference (§15.6.2) in a child Loan's access path; destroying in place a Move Path
  whose access path is a prefix of the base's ends the child." The links stay.
- **§13.5.5,** the sentence for borrow handles: "assigning it ends a child Loan whose base it is" becomes "assigning
  it ends a child Loan whose base it is when it is a Move Path". `views[i] = w` ends nothing, under the current text
  too.
- **Examples:** 23a as a §15.6.3 example, with the competing-alias error of §3.3.

The base never contains a dereference, so "with no dereference between them" and the sentence for a base behind a
dereference have no case left. "A prefix of the base's" access path states S1's exact comparison (S1 §2.1), which
"contains" left open for a runtime index; a prefix includes the base itself and passes own steps.

Remaining limits are intentional: no general points-to analysis, no ending on writes through a reference or at a
runtime index, no relaxation of P3 to preserve whole-prefix aliases, no change to call effects or Origin inference.
The settled
[borrow-only uniq pair rule](../../docs/SETTLED.md#a-borrow-only-uniq-pair-follow-bounded-by-its-slot-alone) continues
to require every relevant reaching lifetime; an earlier Loan end does not authorize a detached return.

The Principles hold through one base and one ending path, decisions derived from local paths and published
contracts, explicit replacement/destruction semantics, and diagnostics attached to existing operation and Borrow
sites. Evidence and cost measurements remain tied to exact source/configuration rather than an assertion that a
small rule must be fast or sound.
