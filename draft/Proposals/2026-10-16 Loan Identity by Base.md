# Loan Identity by Base

Date: 2026-10-16, revised on 2026-10-17. Revisions followed an independent plan review, which found a hole in the first rule (§8), three independent reviews of the drafts, and the A5 step 0 prototype (§9). Status: proposal for the owner's decision; not integrated or implemented. Written on the owner's instruction of 2026-10-16: a rule of the complete Polonius-style A5 that is not finitely expressible is resolved by a SPEC change decided before its family is implemented. The owner's direction of the same day applies: precision may drop somewhat for a simple and fast rule, soundness may not, and every lost form is listed and counted. Changes SPEC §3.5.1, §15.6, §15.6.2, §15.6.3 and Appendix E. Companion: [Preserves Results by Receiver Place](<2026-10-16 Preserves Results by Receiver Place.md>), which names receiver Places by the access paths defined here. Prototype: [`draft/Sketches/LoanIdentityByBase.mjs`](../Sketches/LoanIdentityByBase.mjs).

Base: `de80a4c0` (`dev`). Line numbers refer to that commit.

## 1. Problem

[§15.6](../../docs/spec/15-ownership-and-lifetime-analysis.md#156-borrow-checking) (line 450) defines a Loan as `(place, mode)`, and [§15.6.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts) (line 495) names a Place reached through a reference by the Loans the reference carries: "Other followed references | Follow Loan provenance, then these rules". A Reborrow through a reference held in a loop variable therefore borrows a new Place on every iteration:

```kimi
struct Tree
    var value: i32
    var children: Array<Tree>

func deepest(root: uniq/Tree) -> uniq/Tree during root
    var cursor = root
    while cursor.children.length > 0
        let next = cursor.children[0]@uniq
        cursor = next
    return cursor
```

The k-th execution of `cursor.children[0]@uniq` borrows `root.children[0]` nested k times. The Loans of one body are unbounded, so a fixed point over them need not exist, and a Polonius-style A5 (facts and a fixed set of rules) has no finite domain to work on.

The direct finite substitute, one Loan per site, is unsound when a holder may act with the authority of the Loans it may carry. At the loop head `cursor` may carry the site's own Loan from the previous iteration, so `cursor.children.clear()` placed before `cursor = next` counts as an access with the authority of the live `next` and is accepted. The withdrawn plan reproduced this ([R4 Completion Plan and Budget](<../Obsolete/2026-10-13 R4 Completion Plan and Budget.md>), line 55: "The reproduced repeated-site parent/child failure rules out blanket self-authority by static Loan ID"). The prototype reproduces it with a reconstruction of that rule (§6).

## 2. Proposal

A Loan is identified by its Borrow site and Semantics case. Its Place is named by its **access path**: the syntax of the borrowed Place from a body-local root, through Fields, indices and dereferences. Conflicts compare paths. Rust's MIR borrow checker uses the same model: a borrow is identified by its location, conflicts are decided by `places_conflict` on paths, and moving or overwriting a reborrowed reference is an error (E0505, E0506) unless the write kills the loan.

The **base** of a Loan whose path dereferences is the longest Move Path prefix of that path before its first dereference: the holder of the first reference followed (`cursor` for `cursor.children[0]`, `h` for `h.next.value`). Assigning the base or an ancestor of it with `=` destroys that reference in place, which **ends** the Loan: no value carries it any longer, and the values that did keep their parent Loans. The end of the base's storage ends it too. Every other operation on a prefix of the path is an ordinary conflict, including Moving the base, borrowing it exclusively (as an `exchange`, `swap` or `replace` target, too) and writing a stored reference through another reference.

### 2.1. Rules

The facts and rules A5 adds or changes (sketch; `P` and `Q` are program points):

```text
loan(L)        :- borrow_site(L).                          // one Loan per Borrow site and Semantics case
path(L,Π)      :- access_path(L,Π).                        // from a body-local root: Fields, indices, dereferences
base(L,B)      :- path(L,Π), deref_in(Π), B = longest Move Path prefix of Π before its first dereference.
ended(L,P)     :- base(L,B), prefix(G,B), (assigned(G,P) ; storage_dead(G,P)).
carries(X,L,Q) :- carries(X,L,P), edge(P,Q), !replaced(X,P), !ended(L,P).   // replaced: =, replace, exchange, swap, Move in
error(P)       :- operation(P,Π',K), live(L,P), overlaps(Π',path(L)), conflicts(K,mode(L)), !ended(L,P).
```

- **Borrow sites.** The sites are explicit Borrows and implicit ones: Receiver Expressions ([§7.3](../../docs/spec/07-functions-and-callable-values.md#73-receivers)), argument adaptations ([§10.2](../../docs/spec/10-overload-resolution-and-inference.md#102-common-adaptation)), comparison inspection Loans ([§13.4](../../docs/spec/13-operators-and-assignment.md#134-comparison-and-logical-operators)) and capture entries ([§7.7.2](../../docs/spec/07-functions-and-callable-values.md#772-captures)). A call's caller-side Loans ([§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls)) have the call's site. The Borrows of a default evaluated at a call ([§7.2.3](../../docs/spec/07-functions-and-callable-values.md#723-default-evaluation)) are identified by that call and the default's operation.
- **Paths.** A path starts at a root: a local, a parameter, a temporary or a static key (§15.6). A dereference is a step through a `ref`, `uniq`, `objref` or `objuniq` reference or a counted handle (`rc`, `arc`). The payload of an owned `obj` handle is an inline step ([§3.4.1](../../docs/spec/03-types-and-values.md#341-access-paths-and-value-kinds)).
- **Children.** A Borrow whose path dereferences only exclusive references forms a child Loan, as [§15.6.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting) does now (line 513: "Borrowing through an exclusive borrow creates a child Loan"). Its values carry the Loans of every reference followed to reach it, its parent Loans. A Borrow past a shared reference (`ref`, `objref`) forms no Loan of its own and carries the Loans of the last shared reference it follows: "A shared Borrow through a shared reference is thus contained only in that reference's Origin" (line 513). So `let c = h.link.x@ref`, with a stored `link: ref/Point`, keeps no Loan on `h.*.link`, and `h.link = o` stays valid. A Borrow whose lending point lies in an object, past an `objref`, `objuniq`, `rc` or `arc` handle, keeps a Loan on the handle Place it was reached through, as [§13.5.5](../../docs/spec/13-operators-and-assignment.md#1355-follow) requires (line 353). That Loan's path ends at the handle, and it carries the Loans of the references followed to reach the handle. While it is live, the handle cannot be Moved, replaced (also by `=`), exclusively borrowed or destroyed. So `h.counted = makeRc(…)` cannot release the object that a live `h.counted.x@ref` reads, and the ending below applies only to the holders of `ref` and `uniq` references.
- **Loans from callers.** The Loans a body receives through its parameters' Origins have no path in it. An operation conflicts with them only through what a call's declared effects may reach ([§8.11.4](../../docs/spec/08-generics-constraints-and-contracts.md#8114-checking-and-use)) and through static anchors, as today.
- **`overlaps(Π1,Π2)`:** one path is a prefix of the other, comparing projection by projection under the rules of §15.6.2. Distinct Fields, Tuple elements and constant indices are disjoint, any other index overlaps, and a dereference matches only a dereference. Paths from distinct roots are disjoint; the Loans each reference carries cover its aliases. Prefix overlap includes the holders themselves: a shared Borrow or read of the base slot or an ancestor conflicts with a live exclusive child, as in Rust (E0502), although "Inline parts exclude referents" ([§3.4](../../docs/spec/03-types-and-values.md#34-places-and-storage), §15.6.2 line 500) made it valid before (§4.5).
- **`operation(P,Π',K)`:** every operation on a Place, with the access kind `K` of the §15.6.2 table. This covers a read or write through a reference, a Move, a shared or exclusive Borrow, an assignment and a destruction. An assignment to the base or an ancestor of it ends the Loan instead of conflicting. An assignment to any other prefix, such as `h.next = n` behind `h`, conflicts.
- **`assigned(G,P)`:** `=` to the Move Path `G`. Because the base is the Move Path before the first dereference, this kills across dereferences: `rr = other` ends the child of `rr.p`'s referent (path `rr.*.p.*`). The child keeps the Loans of `rr`'s old value, so a later write through the new `rr` is valid. The assignment destroys the old value first, and that destruction is checked before the child ends, as at a storage end below. `replace`, `exchange` and `swap` borrow their targets exclusively, so they conflict while a child is live.
- **`live(L,P)`:** as in §15.6, and the Loans live at an operation include those carried by the values it stores. A value in transit from a temporary that ends at the same point is still counted.
- **`storage_dead(G,P)`:** `G`'s scope ends, or the temporary `G` ends. The destruction at that point is checked first ([§15.6.6](../../docs/spec/15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking)), and the child ends after it. A destruction accesses only what its `drop` may observe, and destroying a borrow accesses nothing ("Destroying a borrow or `raw/T` does not destroy the referent", §3.4). So the end of `next` in `deepest` conflicts with nothing, while a `drop` of `G`'s container that writes through `G` conflicts with a live child.
- **Unchanged:** liveness and `carries` at joins (union) and in loops (least fixed point), and Loans whose path does not dereference, the borrows of body-local Places. A raw-Place borrow keeps its fresh anchor ([§5.2.2](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)).

Every relation ranges over the sites, paths and points of one body, so a body's facts are finite and polynomial in its size.

### 2.2. Why ending is sound

The hypothesis H1, which the prototype tests (§6): when a Loan ends because its base or an ancestor is assigned or its storage ends, nothing outside the child's descendants holds the base's old reference, and every live value that reaches the child's Place descends from the child or is an ancestor that the parent Loans suspend.

- **The old reference is destroyed, not moved.** An assignment destroys it in place, and destroying a borrow does not destroy its referent. Every operation that would move the old reference elsewhere while a child is live conflicts: Moving the base or an ancestor, borrowing either exclusively (which `exchange`, `swap` and `replace` require), and passing either to a call.
- **Every other path is covered.** The child's values keep the parent Loans, those of each reference followed to reach the child, so the holders those references came from stay suspended. Other children formed through the same base were kept apart from this one by the overlap rule while both were live.

The kill may cross a dereference (`q = …` ends the child of `q.p`'s referent, base `q`) only because overlap is by path prefix through dereferences. A review of the A5 step 0 prototype found that crossing is unsound in a rule that compares only the base:

```kimi
// S has the Field p: uniq/i32.
var s = S(p: t@uniq)
var o = S(p: u@uniq)
var q = s@uniq
let c = q.p@follow@uniq       // Path q.*.p.*, base q.
let w = q@follow@ref          // Error here: q.* is a prefix of c's path.
q = o@uniq                    // Without that error, this kill would leave w reading t through the old s.p...
c@follow = 5                  // ...while c writes it.
inspect(w.p@follow)
```

Under the prefix rule, `w` conflicts with `c` when it is formed. In the other order, forming `c` conflicts with `w`. So the old reference that the kill leaves in `*q` cannot be reached by any value that outlives the kill, and `c` keeps the Loans of `q` and of `s.p`'s reference. This is why §4.5 is a loss: the current text lets `*q` be borrowed shared while `c` lives, because inline parts exclude referents.

The clause that carries soundness is the conflict for an exclusive Borrow of the base or an ancestor. An `exchange` or `swap` lowered as a call, or performed inside a user callee, shows no target at the call site, only that Borrow. The A5 step 0 prototype (§9) found three of its five counterexamples to the first draft in exactly these forms. The ending is the only operation that does not conflict, and it applies only to Move Paths before the first dereference. Writing a stored reference behind another reference (`h.next = n`) or through a non-path index conflicts, so no write that might not reach the base kills a Loan.

### 2.3. SPEC text

**§15.6, line 450.**

- Now: "**Loans.** A **Loan** is `(place, mode)` with mode `ref` or `uniq`. Its **anchor** is the lending point of the borrow that creates it ([§3.4](03-types-and-values.md#34-places-and-storage))."
- New: "**Loans.** A **Loan** has a Place, named by its access path ([§15.6.2](#1562-place-overlap-and-conflicts)), and a mode, `ref` or `uniq`. It is identified by its Borrow site and Semantics case ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)): every execution of one site in one case forms the same Loan. Implicit Borrows (Receiver Expressions, argument adaptations, comparison inspections, capture entries) have the site of their operation; a call's caller-side Loans ([§15.6.4](#1564-calls)) have the call's; the Borrows of a default evaluated at a call ([§7.2.3](07-functions-and-callable-values.md#723-default-evaluation)) are identified by that call and the default's operation. The Loans a body receives through its parameters' Origins have no access path in it; an operation conflicts with them only through what a call may reach ([§8.11.4](08-generics-constraints-and-contracts.md#8114-checking-and-use)) and through static anchors. Its **anchor** is the lending point of the borrow that creates it ([§3.4](03-types-and-values.md#34-places-and-storage))."

**§15.6.2, line 495** (table row).

- Now: "| Other followed references | Follow Loan provenance, then these rules |"
- New: "| Other followed references | Their access paths from a root (a local, parameter, temporary or static key), compared projection by projection: a dereference matches only a dereference, and paths from distinct roots are disjoint ([§15.6.3](#1563-reborrowing-and-region-splitting)) |"

**§15.6.3, line 513** (the first two sentences).

- Now: "Borrowing through an exclusive borrow creates a child Loan anchored on the referent; while the child is live, access through the parent is suspended. The child does not depend on the variable that holds the parent, so a Reborrow of a parameter may be returned."
- New: "Borrowing through an exclusive borrow creates a child Loan anchored on the referent; its values also carry the Loans of every reference followed to reach it, its parent Loans. A Borrow past a shared reference forms no Loan of its own and carries the Loans of the last shared reference it follows; a Borrow whose lending point lies in an object keeps its Loan on the handle Place ([§13.5.5](13-operators-and-assignment.md#1355-follow)), which no assignment ends. Every operation on a Place whose access path is a prefix or an extension of the child's is checked against it; a destruction accesses only what its `drop` may observe, and destroying a borrow accesses nothing. So, while the child is live, access through the parent is suspended; the parent's holder and its ancestors can be neither Moved nor exclusively borrowed, nor borrowed shared while the child is exclusive. The child's **base** is the longest Move Path prefix of its path before the first dereference, the holder of the first reference followed. Assigning the base or an ancestor of it (`=`), or the end of its storage, ends the child instead of conflicting, once the destruction of the old value there is checked: no value carries it any longer, and the values that did keep the parent Loans. So a Reborrow of a parameter may be returned."

**§3.5.1, line 359.**

- Now: "While the child is live, conflicting uses of the parent are errors; after its last use the parent is usable again."
- New: "While the child is live, conflicting uses of the parent are errors, including Moving or exclusively borrowing `P` or an ancestor of it, and borrowing either shared while the child is exclusive ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)); after its last use the parent is usable again."

**Appendix E, line 66,** and a new row:

- Now: "| Loan | A borrowed Place and its access mode (`ref` or `uniq`); live while a value that carries it may be used. | …"
- New: "| Loan | A borrowed Place, named by its access path, and its access mode (`ref` or `uniq`), identified by its Borrow site and Semantics case; live while a value that carries it may be used. | …"
- New row: "| Base (of a child Loan) | The longest Move Path prefix of a child Loan's access path before its first dereference; assigning it or an ancestor, or the end of its storage, ends the child. | [§15.6.3](../15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting) |"

**§15.6.2, line 500.** Append: "A child Loan is also checked against operations on the prefixes of its access path, its base and ancestors included ([§15.6.3](#1563-reborrowing-and-region-splitting))."

Unchanged: §15.6 line 454 ("A write through a reference … ends none" stays true, since only assignments to Move Paths end Loans), [§13.5.6](../../docs/spec/13-operators-and-assignment.md#1356-borrow-and-reborrow) line 399, and the containment, region-splitting and result-keeping sentences of §15.6.3.

## 3. Examples

The verdicts of `deepest` are unchanged; finite identities now decide them.

```kimi
func deepest(root: uniq/Tree) -> uniq/Tree during root
    var cursor = root                         // Reborrow of root: path root.*, base root.
    while cursor.children.length > 0
        let next = cursor.children[0]@uniq    // Keeps the receiver Loan of indexUniq: path cursor.*.children, base cursor.
        cursor = next                         // Assigning the base cursor ends that Loan; cursor keeps the Loans of root.
    return cursor
```

```kimi
func deepestCleared(root: uniq/Tree) -> uniq/Tree during root
    var cursor = root
    while cursor.children.length > 0
        let next = cursor.children[0]@uniq
        cursor.children.clear()               // Error: cursor.*.children overlaps the Loan next keeps.
        cursor = next
    return cursor
```

`cursor.children[0]` calls `indexUniq` with the receiver Borrow `cursor.*.children`, whose base is `cursor`. The result is a temporary; the `@uniq` Borrow of its referent has the temporary as its base and ends with it at the end of the statement, and `next` keeps the parent Loans, the receiver Loan among them. `cursor = next` Reborrows `next` (base `next`, ended when `next`'s scope ends each iteration) and assigns `cursor`, which ends the receiver Loan. In `deepestCleared`, `clear()` borrows `cursor.*.children` exclusively while that Loan is live, which is an error on every iteration, the first included.

A replacement on one path ends a Loan only on that path:

```kimi
// Pair has the Fields left: i32 and right: i32.
func touch(a: uniq/Pair, other: uniq/Pair, flag: bool)
    var h = a
    let x = h.left@uniq          // Path h.*.left, base h.
    if flag
        h = other                // Ends x's Loan on this path.
    h.left = 1                   // Error: when flag is false, h still holds a, and x is used below.
    x@follow = 2
```

With `h = other` in both branches, `h.left = 1` is valid. In a list walk `let d = h.next` followed by `h = d`, `d`'s Loan has path `h.*.next.*` and base `h`. Assigning `h` ends it, and `d`'s values keep the Loans of `h` and of the reference stored in `h.next`. Writing `h.next = n` while `d` is live conflicts instead (§4.3).

The program that broke the first draft of this rule is rejected:

```kimi
var x = 1
var y = 2
var r = x@uniq
let c = r@follow@uniq                                        // Path r.*, base r.
let old = Kimi.Intrinsics.exchange(r@uniq, with: y@uniq)     // Error: borrows the base r exclusively while c is live.
old@follow = 3
c@follow = 4
```

## 4. What is lost

Each form below was valid under the letter of the current text and is an error under this proposal. Every count is at the base, by the searches of §7, with each hit checked by hand.

1. **Moving the base or an ancestor while a child is live.** The current text allowed this: "the child does not depend on the variable that holds the parent". Rust rejects the same program (E0505).

   ```kimi
   // Point has the Fields x: i32 and y: i32.
   func shift(p: uniq/Point)
       let c = p.x@uniq          // Path p.*.x, base p.
       let q = p@move            // Error: Moves the base while c is live.
       c@follow = 1
       q.y = 2
   ```

   Rewrites: Move `p` after `c`'s last use, or reach `y` through `p` (`p.y = 2`, disjoint from `x`). Counts: library 0, milestones 0. Tests: 1 accepted case becomes an error, `StoredReferenceLoanTest.AChildRetainsItsInputAfterTransferringTheParentReference` (fixture `StoredReferenceLoanTransferred`: `let found = value@follow@ref; let moved = value@move; require found == 42 …`). The 2 rejected rows of `ParentRestrictionsFollowAcquisitionAndCopies` (lines 121–122) keep their verdict, reported at the Move. A Move into an aggregate literal is already rejected (`PlacedExclusiveReferenceTest`, `ChildMoveActivationTest`). Moves into existing Fields and into captures were not checked against the current implementation; the searches found none in the library, milestones or tests.
2. **Borrowing the base or an ancestor exclusively while a child is live,** including as an `exchange`, `swap` or `replace` target, and including a plain `let w = r@uniq` while `c` (a child through `r`) is live, which the current text accepts and Rust rejects. `replace` destroys the old reference in place, as `=` does, but it reaches the target through an exclusive Borrow that could equally reach `exchange` inside any callee; write `r = v` instead. Counts: library 0, milestones 0, and in tests 0 accepted cases, because the implementation already rejects these (`ContentUpdateTest.OldContentAndSlotViewsCannotSurviveReplacement` and `ExclusiveContentChildrenBlockEveryUpdate`, lines 80–122).
3. **Writing a stored reference through another reference or a non-path index while a Reborrow of its referent is live** (`let d = h.next; h.next = n; use(d)`). The current text keeps the child ("The child does not depend on the variable that holds the parent", §15.6.3 line 513), and Rust accepts the program as a shallow write. This proposal ends Loans only by assignments to Move Paths, which need no definiteness rule for the written path (§8). Counts: library 0, milestones 0, tests 0.
4. **Distinct anchors within one site.** Two live exclusive borrows formed by one site are one Loan with one path. The "distinct anchors" row of §15.6.2 stays, but it cannot prove such borrows disjoint, since they share one anchor name. Under exact Places it could, for borrows formed in different iterations through different values. Not measured: no search isolates this shape.
5. **Borrowing or reading shared, while an exclusive child is live, a prefix of the child's path:** the base slot, an ancestor, or a Place behind the base's reference that holds the reference the child follows. Examples: `let c = p.x@uniq; let s = p@ref; inspect(s@follow.y); c@follow = 1`, a shared capture of `p`, or `let w = q@follow@ref` while `c = q.p@follow@uniq` is live (§2.2). The slot does not overlap the referent today ("Inline parts exclude referents", §15.6.2 line 500). Under prefix overlap the shared Borrow of `p` reaches `p.*.x`, so it conflicts, as in Rust (E0502). Use `p.y` (or `q.other`) directly instead. Counts for explicit Borrows: library 0, milestones 0, tests 0 verdict changes. Two scans looked within 10 lines after a child formed through `X` that is used later:
   - for `X@ref` with no projection: only owned Places, shared copies and rejection tests;
   - for a whole use of `X` after `let c = X.f…@follow@uniq|ref`: only `OwnedStoredReferenceTest`. There, `ABorrowThatReachesTheReferenceStillConflicts` and the `slot.0` row of `ConflictingAccessThroughTheParentRejects` stay rejected, but report the conflict at `item@ref` instead of the later read. Implicit shared Borrows of a holder or an ancestor were not counted: a `ref/Self` receiver on a struct that holds the reference, a comparison inspection, a capture or a `ref/T` adaptation. A Receiver Expression on the reference itself selects through it and was already an access through the parent.

Liveness after joins and in loops stays a may-analysis (§15.6, line 455). On fully explored programs, the prototype found no other program that the reference semantics accepts and these rules reject (§6). The toy has no slot Borrows, so it cannot show §4.5.

## 5. Principles

- **One Concept, One Canonical Form.** One Loan identity (the site), one Place name (the access path) and one conflict rule (path overlap) serve every check. One operation ends a child (`=` or a storage end), replacing provenance chains and the self-authority shortcut. `replace` on a lent target is not a second way to end a Loan; `=` is.
- **Local Reasoning.** Identity, path and base are read at the site. A verdict depends on the operations of one body on syntactic paths, with no aliasing analysis and no value identity, and the analysis is bounded.
- **Explicit Semantics.** Every operation that keeps an old reference alive (a Move, a Borrow of its holder, `exchange`, `swap`) is a visible conflict at its site, and the one operation that ends a child is the assignment written at its base.
- **Compiler Server Protocol.** A conflict names the operation, the Borrow site and the base ("the Loan of `cursor.children[0]@uniq`, path `cursor.*.children`"), all stable source locations, so diagnostics stay small and reproducible.

## 6. Prototype

[`LoanIdentityByBase.mjs`](../Sketches/LoanIdentityByBase.mjs) is a dependency-free Node script (`node draft/Sketches/LoanIdentityByBase.mjs`). It evaluates the rules of §2.1 on a toy language. The toy has `uniq` and `ref` holders, struct locals with reference Fields (ancestors), Field paths, `if`, `while`, Moves, Copies, `exchange`, `swap`, `replace`, storage ends and fresh borrows.

Each program is compared with a reference semantics that creates a fresh tag at an exact, unbounded Place for every executed Borrow. An access through tag `t` conflicts with a live tag that is neither `t` nor an ancestor of `t` when the Places overlap and the modes conflict. This is the value-identity reading of the current §15.6.3, run on every path with loops unrolled up to a bound. At every ending, the prototype also checks H1 directly. Two comparison rules run on the same programs:

- **Brief:** the first draft of this proposal. Ending on every complete replacement of the base, `exchange` and `swap` included, and forbidding only Moves of the base itself.
- **Site self-authority:** a reconstruction of the withdrawn shortcut. One Loan per site, no base and no ending, and an access never conflicts with a Loan its holder may carry. It is a hypothesis about the shape of the withdrawn rule, not its code.

Named cases (loop bound 3):

| Case | Proposed rule | Brief | Site self-authority | Reference semantics |
| --- | --- | --- | --- | --- |
| `deepest` | accept | accept | accept | no violation |
| `deepestCleared` | reject at `clear()` | reject | **accept** | violation at `clear()` |
| `deepest` with `cursor = next@move` | accept | accept | accept | no violation |
| Moved base, child live (§4.1) | reject at the Move | reject | accept | no violation |
| Moved base, then written through the new holder | reject at the Move | reject | reject | violation at the write |
| `exchange` of the base, old value written (§3) | reject at `exchange` | **accept** | reject | violation at the write |
| `swap` of the base, old value written | reject at `swap` | **accept** | reject | violation at the write |
| Move of the base's struct, then written | reject at the Move | **accept** | reject | violation at the write |
| `replace` of the base, child live (§4.2) | reject at `replace` | accept | accept | no violation |
| `=` to the base, child live | accept | accept | accept | no violation |
| Join, base assigned on one path | reject | reject | reject | violation on the other path |
| Join, base assigned on both paths | accept | accept | reject | no violation |
| Shared child, base assigned, parent written | reject | reject | reject | violation |

Random differential test (seeded; programs of up to about 20 statements with two levels of nesting, two parameters, four locals and two struct locals):

| Run | Accepted (all paths explored) | Of these, ending a Loan a later use still carries | Accepted with a violation | Rejected, explored, no violation | Brief rule: accepted with a violation | Self-authority: accepted with a violation | H1 checks (failures) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Seed 20261016, 200,000 programs, loop bound 3 | 41,629 (41,453) | 1,267 | **0** | 441: 266 lent base, 174 moved base, 1 violation beyond the bound | 39 | 17 | 13,136,815 (0) |
| Seed 7, 100,000 programs, loop bound 4 | 20,780 (20,517) | 647 | **0** | 208: 120 lent base, 88 moved base | 15 | 9 | 27,185,334 (0) |

The finite-domain invariant held in every accepted program: no holder used later carries a Loan whose base is itself.

The command is `node draft/Sketches/LoanIdentityByBase.mjs --random N --seed S --bound K`. It exits non-zero on an unsound acceptance, an H1 failure, a finite-domain invariant failure (a holder used later carrying a Loan whose base is itself) in an accepted program, or a named case that breaks.

**Result.** No program the proposed rules accept violates the reference semantics, and H1 held at every ending checked. Every program that the rules reject and that was explored on all paths without a violation is classified. Either it is a §4.1 or §4.2 case (with any errors that follow from it), or its violation lies beyond the loop bound and appears with two more iterations. None is unexplained. The brief rule accepts the `exchange`, `swap` and struct-Move programs, each of which violates the semantics. The self-authority rule accepts `deepestCleared` and other violating programs.

This is evidence, not proof. The toy covers:

- holders that are locals or Fields of struct locals;
- Loans formed through references, with Moves, `exchange`, `swap` and `replace` of holders and their ancestors.

It does not cover references stored behind other references (bases such as `h` for `h.next`), non-path indices, borrows of owned Places (Loans without a dereference), temporaries (its `deepest` borrows through `cursor` directly, not through the index result of §3), calls, objects, generics or raw borrows. Its loops run only up to the bound, and those parts of §2.1 rest on the argument of §2.2. In the toy, H1's first condition follows almost from its semantics, which never copies an exclusive reference; the differential test carries the evidence.

## 7. Usage

| Shape | Library (31 files) | Milestones (43) | Tests |
| --- | --- | --- | --- |
| Self-reborrow loops (`x = x.f…`, or `n = x.f…; x = n` in a loop) | 0 | 0 | 0 |
| §4.1: Move of a base or ancestor while its child is live | 0 | 0 | 1 accepted case becomes an error; 2 rejected rows keep their verdict |
| §4.2: exclusive Borrow (`exchange`, `swap`, `replace`) of a base while its child is live | 0 | 0 | 0 accepted; already rejected by the implementation |
| §4.3: a stored reference written through a reference or index while its Reborrow is live | 0 | 0 | 0 |
| §4.5: a shared Borrow of a base slot or ancestor while its exclusive child is live | 0 | 0 | 0 |

Searches over `src/Kimi/Library` and `tests/milestones` (`.kimi`), `tests` (`.kimi`, and the Kimi programs in C# string literals after unescaping `\n`) and `docs/spec`:

- One-step self-reborrow: `grep -rnE '(^|[^=!<>.[:alnum:]_])([a-z][A-Za-z0-9_]*) = \2(\.[a-z]|\[)'`. No Kimi hits.
- Two-step self-reborrow, and §4.1–§4.3: a line scan reporting a reborrowing binding (`let c = X`, `X@follow…`, `X.f…@uniq|ref`, `X[…]@…`) used within the 10 following lines of a Move, an exclusive slot Borrow, an `exchange`/`swap`/`replace` target, or an assignment to `X.f…`/`X[…]`.

The heuristic hits are owned Places, Copies of `Copy` values and owned or counted handles. Their Moves and writes already conflict with the Loans on them (`ObjectExclusiveFieldBorrowTest`, `SharedObjectOwnershipTest`, `SiblingMoveBorrowTest` and others), except the cases named above.

## 8. Rejected options

- **The first draft of this proposal (the "brief" rule):** end a child on every complete replacement of its base, and forbid only Moving the base itself. It is unsound: `exchange` and `swap` replace the base completely while moving its old reference out, and `r@uniq` is a Borrow of the slot, not an access through `r`. The program of §3 then writes `x` through `old` while `c` is live. The current text rejects it, and Rust rejects `mem::replace(&mut r, …)` in the same position. The prototype accepts it under that rule (§6). The hypothesis that draft stated ("the old referent stays reachable only through holders that carry the parent Loan") also holds for this program, so it was the wrong invariant; H1 replaces it.
- **Allowing `replace` as the one exclusive Borrow that ends a child** (the plan review's S1′). It keeps `replace(r@uniq, with: v)` while `c` is live. But it makes one intrinsic an exception to the exclusive-Borrow conflict, the clause that carries soundness, for an operation that `r = v` already writes. The current implementation rejects that form anyway (§4.2).
- **One Loan per site with self-authority:** the local patch. Finite, but unsound (§1, §6).
- **Ending on writes through references, as Rust's shallow writes do.** This keeps §4.3's form, but a kill then depends on whether a written path is the base, which needs a definiteness rule for paths behind references and indices. No program in the library, milestones or tests uses the form.
- **Identity (site, base), renaming the base on a transfer.** Keeps §4.1, but a Loan's identity gains a second dimension (sites × holders), and the rename covers one family, which appears in one test.
- **Bounded place names (k-limiting) or exact Places with widening:** they keep provenance and summarize paths past depth k or at loop heads. Summary Places need weak updates, lose `deepest`'s precision past the cut, and the cut is arbitrary.

## 9. SETTLED, DECISIONS and other proposals

- **SETTLED.** No entry covers Loan identity, provenance or transfers of a reborrowed parent. "A borrow-only uniq pair follow bounded by its slot alone" rests on containment (§15.5, §15.6.3), which this proposal keeps.
- **DECISIONS.** No current entry decides A5's Loan identity. R4-M24, which defined A5-1's holders and Loans, is superseded by R4-M33, which removed its units and its open Q1-ref finding (a complete replacement through a reference). This proposal answers Q1-ref's question for child Loans: such a replacement ends nothing and conflicts (§4.3). The A5 design that implements this proposal records its decision there.
- **[Single MIR plan](<2026-10-10 Compiler Size Reduction and Single MIR Plan.md>).** A5 (§5.5, line 292) checks access with `places_conflict` and records the location in its Loan table (`Loan{Place, 種類, RegionVid, 位置, Bound}`, line 272). This proposal makes the location the identity and the path the Place.
- **[Preserves Results by Receiver Place](<2026-10-16 Preserves Results by Receiver Place.md>).** It names receiver Places by the access paths defined here. Either can be decided alone, but its receiver Places are finite only with this one.
- **[Complete Polonius A5 Plan](<2026-10-16 Complete Polonius A5 Plan.md>)** (merged from `dev`) consumes this rule as its S1. This proposal differs from the plan's S1 row (line 409) and facts (lines 159–161):
  - **The base.** The plan's base is the Place storing the reference. Here it is the Move Path before the first dereference, which answers the plan's open question (line 214) by killing across dereferences. That is sound only together with prefix overlap through dereferences (§2.2). A plan that keeps the Place base and compares only bases must not cross a dereference, and then loses the valid program of its case 23a.
  - **`replace`.** The plan exempts a `replace` target from the exclusive-Borrow ban (line 161) and reports that its prototype needs the exemption (cases 15a, 15b). Here `replace` conflicts while a child is live (§4.2, §8), as the current implementation already does. If MIR lowers `=` through a replace-like operation, that lowering is the assignment, which ends the child.
  - **Object handles.** Their Borrows keep §13.5.5's handle Loan, as the plan's note on object-handle bases also says.
- **The step 0 prototype** (branch `claude/a5-step0`, not in this branch) checks the plan's rules. The step 0 prototype is a C# program with 41 hand-encoded programs. It reports that the first draft of this rule accepts 5 programs the current text rejects: `exchange`, `swap`, and `exchange` performed as a call or inside a user callee. With the exclusive-Borrow ban, all 41 match. Its round 5 (75 cases) agrees with two rules of §2.1: the old value's destruction is checked before the kill (24a, 24b), and the Loans live at an operation include those of the values it stores (24c). Its case 23a, a kill across a dereference, is valid here. A later review found such a kill unsound in a rule that compares only the base; §2.2's prefix rule rejects that program when `w` is formed. That result corroborates §2.2 but is not in this branch, and neither proposal depends on its text.
- **Raw Storage as Storage** (landed in `de80a4c0`) owns the overlap definition in §3.4, which this proposal uses unchanged. Raw-Place borrows keep fresh anchors.
- The other open proposals (Contract bodies and conformance, LSP, debug adapter, closure parentheses, semantic tokens, library binding reuse, work items, AGENTS and PLAN restructuring) do not touch Loans.

## 10. Integration

An intake updates, together:

- **SPEC:** the text of §2.3 (§15.6 line 450, §15.6.2 lines 495 and 500, §15.6.3 line 513, §3.5.1 line 359, Appendix E line 66 and the new row). It adds `deepest`, `deepestCleared` and the `exchange` program (§3) as §15.6.3 examples and the moved-base error (§4.1). The Japanese translation `docs/SPECJ.md` follows only on an explicit translation update (AGENTS).
- **Tests:**
  - `StoredReferenceLoanTest.AChildRetainsItsInputAfterTransferringTheParentReference` and its fixture become a rejection at the Move.
  - The two rows of `ParentRestrictionsFollowAcquisitionAndCopies` expect the Move as the conflict site.
  - `OwnedStoredReferenceTest.ABorrowThatReachesTheReferenceStillConflicts` and the `slot.0` row of `ConflictingAccessThroughTheParentRejects` expect the conflict at `item@ref`.
  - The §2.2 program, rejected at `let w = q@follow@ref`, is a new test, together with its reverse order.
  - New A5 rule tests cover the loop and its invalid counterpart, both join variants, ending by assigning a base or an ancestor, ending at a scope or temporary end, `exchange`, `swap` and `replace` of a base with a live child, a struct Move, `h.next = n` with a live Reborrow, a shared Borrow of the base slot with a live exclusive child, a Borrow past a shared reference (`h.link.x@ref` with `h.link = o`), a Borrow past an `rc` handle with the handle replaced, and a `drop` that writes through a base, both at its storage end and when `=` replaces its container.
- **Milestones and library:** none affected (§7).
- **STATUS and PLAN:** the A5 boundary rows when A5 implements this.
- **DECISIONS:** the A5 design entry in the commit that first implements access paths and bases.
- **`draft/INTEGRATED.md`:** the intake record, written by the intake, not by this proposal.
