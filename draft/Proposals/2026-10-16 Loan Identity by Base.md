# Loan Identity by Base

Date: 2026-10-16, revised on 2026-10-17. Revisions followed an independent plan review, which found a hole in the first rule (§8), three independent reviews of the drafts, the A5 step 0 prototype, and A5 design A3, whose additions to this rule the owner approved on 2026-10-17 (§9). Status: proposal for the owner's decision; not integrated or implemented. Written on the owner's instruction of 2026-10-16: a rule of the complete Polonius-style A5 that is not finitely expressible is resolved by a SPEC change decided before its family is implemented. The owner's direction of the same day applies: precision may drop somewhat for a simple and fast rule, soundness may not, and every lost form is listed and counted. Changes SPEC §3.5.1, §13.5.5, §15.6, §15.6.2, §15.6.3, §15.6.6 and Appendix E. Companion: [Preserves Results by Receiver Place](<2026-10-16 Preserves Results by Receiver Place.md>), which names receiver Places by the access paths defined here. Prototype: [`draft/Sketches/LoanIdentityByBase.mjs`](../Sketches/LoanIdentityByBase.mjs).

Base: `de80a4c0` (`dev`). Line numbers refer to that commit; the SPEC chapters are unchanged up to `8d60a7d0`.

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

The direct finite substitute, one Loan per site, is unsound when a check excuses a Loan by its identity. At the loop head `cursor` may carry the site's own Loan from the previous iteration, so `cursor.children.clear()` placed before `cursor = next` counts as an access with the authority of the live `next` and is accepted. The withdrawn plan reproduced this ([R4 Completion Plan and Budget](<../Obsolete/2026-10-13 R4 Completion Plan and Budget.md>), line 55: "The reproduced repeated-site parent/child failure rules out blanket self-authority by static Loan ID"). The prototype reproduces it with a reconstruction of that rule (§6), and the A5 step 0 prototype found the same hole in two more forms (§2.2).

## 2. Proposal

A Loan is identified by its Borrow site and Semantics case. Its Place is named by its **access path**: the syntax of the borrowed Place from a body-local root, through Fields, indices and dereferences. Conflicts compare paths, and a Loan is checked where a value that carries it may be used, never excused by its identity. Rust's MIR borrow checker uses the same model: a borrow is identified by its location, conflicts are decided by `places_conflict` on paths, and an assignment kills the loans of the overwritten place.

The **base** of a child Loan is the Place before the last borrow dereference of its path: the Place that holds the reference the Borrow follows last (`cursor` for `cursor.children[0]`, `h.*.next` for `h.next.value` with a `uniq/Node` link). Destroying the base in place, or a Move Path that contains it with no borrow dereference between them, **ends** the Loan: by `=`, by the end of its storage, or as a `replace` target. No value carries the Loan any longer, and the values that did keep their parent Loans. A base reached through a reference never ends this way. A Move or a Borrow of a Place reaches the Loans formed through the references stored in it, so moving, exchanging or borrowing the reference that a live child was formed through conflicts with the child as any access to its Place does. A read, write or destruction does not reach them, except a destruction that may observe them.

### 2.1. Rules

The facts and rules A5 adds or changes (sketch; `P` and `Q` are program points, `K` the Place an operation accesses):

```text
loan(L)         :- borrow_site(L).                         // one Loan per Borrow site and Semantics case
path(L,Π)       :- access_path(L,Π).                       // from a body-local root: inline, own and borrow steps
base(L,B)       :- path(L,Π), borrow_deref_in(Π), B = the prefix of Π before its last borrow dereference.
ended(L,P)      :- base(L,B), destroyed_in_place(G,P), move_path(G), prefix(G,B), no_borrow_deref_between(G,B).
step(L,P,Q)     :- edge(P,Q), !ended(L,P).                 // every carrying rule takes it: the end holds after P

carries(X,L,Q)  :- issued(L,P,X), step(L,P,Q).             // a Borrow through exclusive references only
carries(X,L2,Q) :- borrow(P,X), derived(P,L2), step(L2,P,Q).
    // derived: the Loans carried by the references the Borrow follows; past a shared reference, only those of the
    // last shared one and of the exclusive references followed after it
carries(X,L,Q)  :- carries(X,L,P), step(L,P,Q), !replaced(X,P).
carries(Y,L,Q)  :- flows(X,Y,P), carries(X,L,P), step(L,P,Q).     // Copies, Moves, temporaries, call results

used(X,P)       :- the value of X may be used after P (including an observation by its destruction).
used(T,P)       :- used(X,P), carries(X,L,P), overlaps(place(L),T).   // liveness through references
stored(L,P)     :- P stores a value that carries L into a holder T, used(T,P) or T escaped.   // values in transit
checked(L,P)    :- carries(X,L,P), used(X,P), !replaced(X,P) ; stored(L,P).   // by value, never by Loan id

error(P)        :- operation(P,K,A), checked(L,P), reaches(K,A,path(L)), conflicts(A,mode(L)).
reaches(K,A,Π)  :- prefix(Π,K) ; prefix(K,Π), ( deep(A) ; no_borrow_deref_between(K,Π)
                                              ; observing(A), no_shared_deref_between(K,Π) ).
deep(A)         :- A is a Move or a Borrow, shared or exclusive, other than the Borrow of a target the operation destroys
                   in place (§15.7: `replace`).
```

`error` reads the Loans carried before `P`, so an operation that ends a Loan, such as `=` to its base, is checked against it first; `step` removes it after `P` from every value, the values flowing at `P` included. MIR lowers `cursor = next` as a temporary, a destruction and a store, so the end must hold on the flow out of the temporary too.

- **Borrow sites.** The sites are explicit Borrows and implicit ones: Receiver Expressions ([§7.3](../../docs/spec/07-functions-and-callable-values.md#73-receivers)), argument adaptations ([§10.2](../../docs/spec/10-overload-resolution-and-inference.md#102-common-adaptation)), comparison inspection Loans ([§13.4](../../docs/spec/13-operators-and-assignment.md#134-comparison-and-logical-operators)) and capture entries ([§7.7.2](../../docs/spec/07-functions-and-callable-values.md#772-captures)). A call's caller-side Loans ([§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls)) have the call's site. The Borrows of a default evaluated at a call ([§7.2.3](../../docs/spec/07-functions-and-callable-values.md#723-default-evaluation)) are identified by that call and the default's operation.
- **Paths.** A path starts at a root: a local, a parameter, a temporary or a static key (§15.6). Its steps are inline (Fields, Tuple elements, indices), **own** steps into the payload of an `obj`, `rc` or `arc` handle, whose paths extend the handle's as storage does (A3: "part of the handle's storage"), and **borrow dereferences** through a `ref`, `uniq`, `objref` or `objuniq` reference; one through `ref` or `objref` is shared.
- **Children.** A Borrow through one or more exclusive references and no shared one forms a child Loan, as [§15.6.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting) does now (line 513: "Borrowing through an exclusive borrow creates a child Loan"). Its values carry the Loans of every reference followed to reach it, its parent Loans. A Borrow past a shared reference forms no Loan of its own. It derives only from the last shared reference it follows and the exclusive references it follows after that one, and carries their Loans: "A shared Borrow through a shared reference is thus contained only in that reference's Origin" (line 513), and the references derived from a borrow carry its Loan (§15.6, line 452). So `let c = h.link.x@ref`, with a stored `link: ref/Point`, keeps no Loan on `h.*.link`, and `h.link = o` stays valid, while this program stays an error, as §13.5.6 makes it today:

  ```kimi
  // S {o} has `var p: uniq/Array<i32> during o`.
  var t = [1, 2, 3]
  var s = S(p: t@uniq)
  let q = s@ref
  let e = q.p@follow@ref        // Past the shared q: no Loan of its own; e carries q's Loan on s and s.p's on t.
  t = [4]                       // Error: e is used below.
  inspect(e@follow)
  ```

  Liveness through references (`used(T,P)`) rejects it as well: `e` carries a Loan on `s`, so `s.p`'s value, which carries the Loan on `t`, is used wherever `e` is. That clause is needed in any case, for a Borrow of a holder such as `let r = slot@ref`, which carries only its own Loan on `slot`.
- **Objects.** A Borrow into an object payload reached through an own step has the handle in its path with no borrow dereference between, so the handle cannot be Moved, replaced, exclusively borrowed or destroyed while it is live, as [§13.5.5](../../docs/spec/13-operators-and-assignment.md#1355-follow) requires (line 353) for an owning handle: `=` on the handle stays an error. These Loans follow the rules of every other Loan. So `h.counted = makeRc(…)` cannot release the object that a live `h.counted.x@ref` reads, while `let c = h.counted@follow.x@ref; h = other@uniq; inspect(c)`, with `h` a `uniq` reference, stays valid: the Loan's base is `h`, and the assignment ends it. An `objref` or `objuniq` handle is a borrow, and a Borrow through it is a dereference like one through `ref` or `uniq`, with no separate Loan on the handle (design A3 §3). So `=` on such a base ends the child, which is sound: replacing or destroying a borrow handle releases nothing, and the child keeps the parent Loan on the owning handle. In `func edit(view: objuniq/Item)`, `let f = view.id@uniq; view.count = 1; f@follow += 2` stays valid, as with a `uniq/Item` reference. This changes §13.5.5 for borrow handles (§2.3).
- **Loans from callers.** The Loans a body receives through its parameters' Origins have no path in it. An operation conflicts with them only through what a call's declared effects may reach ([§8.11.4](../../docs/spec/08-generics-constraints-and-contracts.md#8114-checking-and-use)) and through static anchors, as today.
- **`reaches(K,A,Π)`:** the access `A` to `K` meets a Loan on `Π`, comparing step by step under the rules of §15.6.2. Distinct Fields, Tuple elements and constant indices are disjoint, any other index overlaps, an own step matches only the same own step, and a dereference matches only a dereference. Paths from distinct roots are disjoint; the Loans each reference carries cover its aliases. A Loan on `Π` is reached by every access to an extension of `Π`, so an access through a reference reaches a Loan on the Place that holds it. A Loan on an extension of `K` is reached across a borrow dereference only by a deep access, a Move or a Borrow of `K` (shared Borrows included, as in Rust, E0502), or by an observing destruction up to the first shared dereference. Reads, writes and other destructions of `K` are shallow, as in Rust: they do not reach the referents of the references stored in `K`, as "Inline parts exclude referents" (§15.6.2 line 500) says now.
- **The `replace` exemption.** The Borrow of a `replace` target is not deep: `replace` destroys the target's old contents in place ([§15.7](../../docs/spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates), line 646: "Destroyed in place"), as `=` does, so it is checked as `=` is and, when the target is a Move Path, ends the children whose base it holds. `exchange` and `swap` transfer the old contents, so their targets are deep exclusive Borrows. No other operation is exempt; in particular `clear` ends no Loan.
- **`operation(P,K,A)`:** every operation on a Place, with the access kind of the §15.6.2 table: a read or write, also through a reference, a Move, a shared or exclusive Borrow, an assignment and a destruction. An assignment writes its target and destroys the old value there.
- **Destructions.** A destruction that may observe a Loan ([§15.6.6](../../docs/spec/15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking), such as one that runs a user-defined `drop`) is an exclusive use of the destroyed Place and of the Places reached from it through exclusive references, up to the first shared one (Rust rejects the same programs, E0713). This holds at `=`, at the end of storage and for the destruction a call performs, such as `replace`'s. A destruction that observes nothing accesses only the destroyed Place: "Destroying a borrow or `raw/T` does not destroy the referent" (§3.4). The destruction is checked before it ends any Loan.
- **`destroyed_in_place(G,P)`:** `=` to the Move Path `G`, the end of `G`'s scope or of the temporary `G`, or `G` as a `replace` target. A Move Path has no dereference ([§15.1.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)), and `prefix(G,B)` compares exactly, so a constant index matches only the same constant. The steps of `B` after `G` may be inline or own steps, never a borrow dereference: destroying `G` destroys the reference stored at `B`, and nothing else does. Past an own step into an `rc` or `arc` payload, another handle may keep the payload, but it is read-only, so only shared children can have been formed through it, and the reference stays reachable only for reading. So `rr = other` does not end a child of `rr.*.*`, and a write through a reference or a non-path index ends nothing.
- **`replaced(X,P)`:** `P` gives the holder `X` a whole new value: `=` to it, a Move into it, `X` as a `replace`, `exchange` or `swap` target, or the start or end of its storage. `X` no longer carries the Loans of its old value, as §15.6 line 454 says for a complete replacement; a write through a reference or a non-path index replaces nothing.
- **`carries`:** the end removes the Loan from every value after `P`, including the values that flow at `P`, such as a temporary moved into the base (`cursor = next` lowered through a temporary).
- **`checked(L,P)`, the check by value.** An operation at `P` is checked against the Loans carried before `P` by the values that may be used after `P`, other than the values `P` replaces, and by the values `P` stores into a Place used after `P` or into storage the body does not own (a value in transit from a temporary that ends at `P` is still counted, A5 step 0 case 24c; a store into a dead Place counts nothing, so `x = f(x@ref)` with `x` dead afterwards stays valid, case 25b). A value is also used wherever a value that carries a Loan on a Place overlapping its holder is used. A Borrow is not checked against the Loan it forms, which no value carries before it, but it is checked against an earlier execution of its own site that a value used later still carries. The activation of a reservation ([§15.6.7](../../docs/spec/15-ownership-and-lifetime-analysis.md#1567-call-reservations)) is checked against the values live into its call other than its own argument. No rule excludes a Loan by its identity.
- **Unchanged:** liveness and `carries` at joins (union) and in loops (least fixed point), and Loans whose path has no borrow dereference, the borrows of body-local Places and of object payloads reached from a body-local root through own steps only, which no assignment ends. A raw-Place borrow keeps its fresh anchor ([§5.2.2](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)).

Every relation ranges over the sites, paths and points of one body, so a body's facts are finite and polynomial in its size.

### 2.2. Why the rule is sound

The hypothesis H1, which the prototype tests (§6): when a Loan ends because its base is destroyed in place, nothing outside the child's descendants holds the base's old reference, and every live value that reaches the child's Place descends from the child or is an ancestor that the parent Loans suspend.

- **The old reference is destroyed, not moved.** A Loan ends only where the reference stored at its base is destroyed in place, and destroying a borrow does not destroy its referent. Every operation that would copy or move that reference elsewhere while a child is live is deep and conflicts: Moving the base or another prefix of the child's path, such as a Place containing the base or the holder of a reference on the way, borrowing one (exclusively, as `exchange` and `swap` require, or shared while the child is exclusive) and passing one to a call. `replace` keeps nothing of the old contents, so its target needs no ban.
- **Every other path is covered.** The child's values keep the parent Loans, those of each reference followed to reach the child, so the holders those references came from stay suspended. Other children formed through the same base were kept apart from this one by the overlap rule while both were live.
- **Shallow accesses keep the child valid.** A read of a Place that holds a reference leaves it in place. A write or destruction destroys it, which leaves its referent and the child intact, and only an observing destruction can act through it, which is deep up to the first shared reference. Past a shared reference no Loan is formed and only reads are possible. The child's path still names its Place through the overwritten holder, so later accesses through the holder's new reference conflict with it while it lives (§4.5): a loss, not a hole.

The deep Borrow is required. The A5 step 0 prototype (case 29a) found this program accepted by the A5 plan's S1′, whose ban covered Moves and exclusive Borrows of a prefix but not shared ones:

```kimi
// S has the Field p: uniq/i32.
var s = S(p: t@uniq)
var o = S(p: u@uniq)
var q = s@uniq
let c = q.p@follow@uniq       // Path q.*.p.*, base q.*.p.
let w = q@follow@ref          // Error: a shared Borrow of q.*, a prefix of c's path, while c is exclusive.
q = o@uniq                    // Without that error, this would end w's Loan (path q.*, base q)...
c@follow = 5                  // ...while w still reaches t through the old s.p, which c writes here.
inspect(w.p@follow)
```

`w`'s own Loan has base `q`, so `q = o@uniq` ends it whether or not the end may cross a dereference. Here `w` is an error when it is formed, as in Rust (E0502); in the other order, forming `c` conflicts with `w`, because its path extends `w`'s. A call can form the alias too. With `Slot` holding `data: uniq/Array<i32>` and `func first(self: ref/Self) -> ref/i32 during self`, `let c = slot.data@follow@uniq; let x = slot.first()` borrows `slot` shared while `c` is live, and `x` could read the array that `c` replaces. The receiver Borrow of `slot` reaches `c`'s Loan, so the call is an error, and in the other order `c` is. The A5 plan reports that the current text may accept this program ("Inline parts exclude referents"); (H) not checked against the implementation. Apart from a Move, which conflicts too, a Borrow of a Place is the only way to reach the references stored in it later.

The check by value is required. Two more forms of the hole of §1 appear once one site has one Loan (A5 step 0, rounds 4 and 6). In a loop `let cur = v@uniq; inspect(prev); prev = cur`, the Borrow forms the Loan that `prev` still carries from the previous iteration, so a check that skips the Loan a Borrow forms by its identity accepts it (case 20a; 20b–20f vary it). In a traversal of `uniq` links, `t = node.next@follow@uniq` followed by a write `node.next@follow.value = 1` while `t` is live, `node` carries its own site's Loan from the previous iteration, because the Loan's base `node.*.next` lies behind `node`'s reference and `node = t` does not end it. A check that lets an access through `node` use the Loans `node` carries accepts the write (30c). Here both are errors: the Loans checked are those of the values used later, `prev` and `t`.

The end never crosses a borrow dereference. With deep Borrows, ending a child of `q.*.p.*` at `q = o@uniq` would also be sound (step 0 prototype round 6, 92 cases) and would keep the programs of §4.5. The owner did not adopt it on 2026-10-17 (A5 design A3 §9); A3 keeps the A5 plan's `within` end, for consistency with the plan.

The clauses that carry soundness are the deep Move and Borrow. An `exchange` or `swap` lowered as a call, or performed inside a user callee, shows no target at the call site, only that Borrow. The step 0 prototype found three of the five counterexamples to the first draft of this rule in exactly these forms (§9). The end applies only to bases that a Move Path contains with no borrow dereference between, so no write that might not reach the base, through a reference or a non-path index, ends a Loan.

### 2.3. SPEC text

**§15.6, line 450.**

- Now: "**Loans.** A **Loan** is `(place, mode)` with mode `ref` or `uniq`. Its **anchor** is the lending point of the borrow that creates it ([§3.4](03-types-and-values.md#34-places-and-storage))."
- New: "**Loans.** A **Loan** has a Place, named by its access path ([§15.6.2](#1562-place-overlap-and-conflicts)), and a mode, `ref` or `uniq`. It is identified by its Borrow site and Semantics case ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)): every execution of one site in one case forms the same Loan. Implicit Borrows (Receiver Expressions, argument adaptations, comparison inspections, capture entries) have the site of their operation; a call's caller-side Loans ([§15.6.4](#1564-calls)) have the call's; the Borrows of a default evaluated at a call ([§7.2.3](07-functions-and-callable-values.md#723-default-evaluation)) are identified by that call and the default's operation. The Loans a body receives through its parameters' Origins have no access path in it; an operation conflicts with them only through what a call may reach ([§8.11.4](08-generics-constraints-and-contracts.md#8114-checking-and-use)) and through static anchors. Its **anchor** is the lending point of the borrow that creates it ([§3.4](03-types-and-values.md#34-places-and-storage))."

**§15.6, line 453.**

- Now: "A Loan is **live** at a point P when a value that carries it may be used after P, including an observation by its destruction ([§15.6.6](#1566-destruction-lifetime-checking))."
- New: "A Loan is **live** at a point P when a value that carries it may be used after P, including an observation by its destruction ([§15.6.6](#1566-destruction-lifetime-checking)). A value is also used wherever a value that carries a Loan on a Place overlapping the first value's holder is used. An operation at P is checked against the Loans carried before P by the values used after P, other than those P replaces, and by the values P stores into a Place used after P or into storage the body does not own. These values decide, never a Loan's identity: a Borrow is not checked against the Loan it forms, which no value carries before it, but it is checked against an earlier execution of its own site that such a value carries. The activation of a reservation ([§15.6.7](#1567-call-reservations)) is checked against the values live into its call other than its own argument."

**§15.6.2, line 495** (table row).

- Now: "| Other followed references | Follow Loan provenance, then these rules |"
- New: "| Other followed references | Their access paths from a root (a local, parameter, temporary or static key), compared step by step. A **dereference** is a step through a `ref`, `uniq`, `objref` or `objuniq` reference, **shared** through `ref` or `objref`. A step into the payload of an `obj`, `rc` or `arc` handle is not a dereference: the payload's paths extend the handle's, whatever access [§3.4.1](03-types-and-values.md#341-access-paths-and-value-kinds) grants through it. A dereference matches only a dereference, and paths from distinct roots are disjoint ([§15.6.3](#1563-reborrowing-and-region-splitting)) |"

**§15.6.2, line 500** (the first two sentences of the bullet).

- Now: "Inline parts exclude referents. Distinct reference variables, runtime comparisons such as `i != j` and integer proofs prove nothing."
- New: "Overlap compares access paths. A Loan whose path is a prefix of an operation's path is reached by the operation, so an access through a reference reaches a Loan on the Place that holds it. A Loan whose path extends the operation's path is reached by a read, write or destruction only when no dereference lies between them; a Move or Borrow reaches it across dereferences, except the Borrow of a target that the operation destroys in place ([§15.7](#157-whole-value-updates): `replace`), and so does a destruction that may observe a Loan ([§15.6.6](#1566-destruction-lifetime-checking)), up to the first shared dereference. Paths from distinct roots are disjoint because the Loans each reference carries cover its aliases; runtime comparisons such as `i != j` and integer proofs prove nothing."

**§15.6.3, line 513** (the first two sentences).

- Now: "Borrowing through an exclusive borrow creates a child Loan anchored on the referent; while the child is live, access through the parent is suspended. The child does not depend on the variable that holds the parent, so a Reborrow of a parameter may be returned."
- New: "Borrowing through an exclusive borrow creates a child Loan anchored on the referent; its values also carry the Loans of every reference dereferenced to reach it, its parent Loans. A Borrow past a shared dereference ([§15.6.2](#1562-place-overlap-and-conflicts)) forms no Loan of its own; it derives only from the reference of its last shared dereference and the references it dereferences after that one, and carries their Loans. While the child is live, access through the parent is suspended, and no prefix of the child's access path, such as the Place that holds the parent, can be Moved or borrowed, except shared while the child is shared or as a target that the operation destroys in place ([§15.7](#157-whole-value-updates): `replace`). The child's **base** is the Place before the last dereference of its path. Destroying in place a Move Path that is the base or contains it with no dereference between them ends the child, once the destruction of the old value there is checked ([§15.6.6](#1566-destruction-lifetime-checking)): by `=`, at the end of its storage, or as such a target. No value carries an ended child any longer, and the values that did keep the parent Loans. So a Reborrow of a parameter may be returned. A child whose base lies behind a dereference does not end this way; it ends when no value that carries it may be used."

**§15.6.6, line 596.** Append: "A destruction that may observe a Loan is an exclusive use of the destroyed Place and of the Places reached from it, across `obj`, `rc` and `arc` payloads and exclusive dereferences up to the first shared dereference ([§15.6.2](#1562-place-overlap-and-conflicts)), at `=`, at the end of storage and in a call that destroys a target, such as `replace`; it is checked before it ends any child Loan ([§15.6.3](#1563-reborrowing-and-region-splitting)). A destruction that observes nothing accesses only the destroyed Place."

**§13.5.5, line 353** (the second sentence).

- Now: "A Borrow whose lending point lies in an object keeps a Loan in its own mode on the handle Place it was reached through: while it is live, that handle cannot be Moved, replaced, exclusively borrowed or destroyed ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)), though a shared Borrow permits reading and cloning it."
- New: "A Borrow whose lending point lies in an object reached through an `obj`, `rc` or `arc` handle keeps its Loan, in its own mode, on an access path through that handle Place with no dereference between ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)): while it is live, that handle cannot be Moved, replaced, exclusively borrowed or destroyed ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)), though a shared Borrow permits reading and cloning it. Through an `objref` or `objuniq` handle, a borrow, the Borrow carries that handle's Loans, which keep the owning handle in place; replacing or destroying the borrow handle releases nothing, and assigning it ends a child Loan whose base it is ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting))."

**§3.5.1, line 359.**

- Now: "While the child is live, conflicting uses of the parent are errors; after its last use the parent is usable again."
- New: "While the child is live, conflicting uses of the parent are errors, including Moving `P` or a Place containing it and borrowing either, exclusively other than as a target that the operation destroys in place (`replace`), or shared while the child is exclusive ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)); after its last use the parent is usable again."

**Appendix E, line 66,** and a new row:

- Now: "| Loan | A borrowed Place and its access mode (`ref` or `uniq`); live while a value that carries it may be used. | …"
- New: "| Loan | A borrowed Place, named by its access path, and its access mode (`ref` or `uniq`), identified by its Borrow site and Semantics case; live while a value that carries it may be used. | …"
- New row: "| Base (of a child Loan) | The Place before the last dereference ([§15.6.2](../15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)) in a child Loan's access path; destroying in place a Move Path that is it or contains it with no dereference between them ends the child. | [§15.6.3](../15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting) |"

Unchanged: §15.6 line 454 ("A write through a reference … ends none" stays true, since only in-place destructions of Move Paths end Loans), [§13.5.6](../../docs/spec/13-operators-and-assignment.md#1356-borrow-and-reborrow) line 399, §15.7, and the containment, region-splitting and result-keeping sentences of §15.6.3.

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

With `h = other` in both branches, `h.left = 1` is valid. `replace` ends a child as `=` does, and `exchange` conflicts:

```kimi
var x = 1
var y = 2
var r = x@uniq
let c = r@follow@uniq                              // Path r.*, base r.
Kimi.Intrinsics.replace(r@uniq, with: y@uniq)      // Valid: destroys r's old reference in place, which ends c's Loan.
c@follow = 3                                       // c keeps the parent Loan on x: writing x before this would conflict.
```

The program that broke the first draft of this rule stays rejected:

```kimi
var x = 1
var y = 2
var r = x@uniq
let c = r@follow@uniq                                        // Path r.*, base r.
let old = Kimi.Intrinsics.exchange(r@uniq, with: y@uniq)     // Error: borrows the base r exclusively while c is live.
old@follow = 3
c@follow = 4
```

An observing destruction is checked before it ends a child:

```kimi
// S has the Field r: uniq/i32 and a drop that writes through it.
var s = S(r: x@uniq)
let c = s.r@follow@uniq          // Path s.r.*, base s.r.
s = S(r: y@uniq)                 // Error: the old s's drop may write through s.r while c is live.
c@follow = 1
```

Without the `drop`, the assignment ends `c`'s Loan and is valid. With a `ref/i32` Field, the destruction stops at the shared reference, and `let v = s.r@follow@ref` forms no Loan.

In a list walk over `uniq` links, `let d = h.next` followed by `h = d`, `d`'s Loan has path `h.*.next.*` and base `h.*.next`, reached through `h`'s reference, so `h = d` does not end it, and `h` then carries a Loan whose path names its own new referent. The next `let d = h.next` is valid, because the `h = d` after it overwrites `h` before any use (step 0 case 30b), but an access through the new `h.next` between `h = d` and that Reborrow conflicts (30d, §4.5). With `obj` links, the payload is an own step, so the base is `h` and `h = d` ends the Loan. Writing `h.next = n` while `d` is live destroys the reference stored there, which neither conflicts with `d` nor ends its Loan.

## 4. What is lost

Each form below was valid under the letter of the current text and is an error under this proposal. Every count is by the searches of §7, with each hit checked by hand. The searches for §4.1–§4.4 ran at the base; those for §4.5, the `replace` exemption and observing destructions ran on this branch after merging `dev` at `e4457e24`, and the files they cite are unchanged since the base.

1. **Moving the base or a Place containing it while a child is live.** The current text allowed this: "the child does not depend on the variable that holds the parent". Rust rejects the same program (E0505).

   ```kimi
   // Point has the Fields x: i32 and y: i32.
   func shift(p: uniq/Point)
       let c = p.x@uniq          // Path p.*.x, base p.
       let q = p@move            // Error: Moves the base while c is live.
       c@follow = 1
       q.y = 2
   ```

   Rewrites: Move `p` after `c`'s last use, or reach `y` through `p` (`p.y = 2`, disjoint from `x`). Counts: library 0, milestones 0. Tests: 1 accepted case becomes an error, `StoredReferenceLoanTest.AChildRetainsItsInputAfterTransferringTheParentReference` (fixture `StoredReferenceLoanTransferred`: `let found = value@follow@ref; let moved = value@move; require found == 42 …`). The 2 rejected rows of `ParentRestrictionsFollowAcquisitionAndCopies` (lines 121–122) keep their verdict, reported at the Move. A Move into an aggregate literal is already rejected (`PlacedExclusiveReferenceTest`, `ChildMoveActivationTest`). Moves into existing Fields and into captures were not checked against the current implementation; the searches found none in the library, milestones or tests.
2. **Borrowing the base or a Place containing it exclusively while a child is live,** as an `exchange` or `swap` target or as a plain `let w = r@uniq` while `c`, a child through `r`, is live. The implicit exclusive Borrows count too: a `uniq/Self` receiver on the holder (`let c = s.p@follow@uniq; s.bump(); c@follow = 1`), a `uniq/T` argument adaptation, an exclusive capture, and the reservation of the holder in a call whose argument is a child through it (`s.m(s.p@follow@ref)`). The current text accepts these, (H) except where a callee's declared effects already reach the child, and Rust rejects them. Where the callee may write through the reference, the error is a soundness fix rather than a loss, as in §4.4's call case. `exchange` and `swap` move the old reference out, and an exclusive Borrow can reach `exchange` inside any callee. A target destroyed in place (`replace`) is exempt (§2.1); `r = v` is the other way to replace the reference. Counts: library 0, milestones 0, and in tests 0 accepted cases. The implementation already rejects the explicit forms (`ContentUpdateTest.ExclusiveContentChildrenBlockEveryUpdate`, its `exchange`, `swap` and `let target = slot@uniq` rows); in `OwnedStoredReferenceTest.ConflictsThroughBothLayersReject`, the row `let view = t.0@follow@ref; let r = t.0@uniq; …` (line 206) keeps its verdict but reports the conflict at `t.0@uniq` instead of `r@follow`. The scan for implicit Borrows (§7) found, near a child formed through the holder, only Slices and Borrows of owned Places, owning handles, Copies of integers or shared references, and receivers or arguments that are the reference itself, which select through it and already conflict.
3. **Distinct anchors within one site.** Two live exclusive borrows formed by one site are one Loan with one path. The "distinct anchors" row of §15.6.2 stays, but it cannot prove such borrows disjoint, since they share one anchor name. Under exact Places it could, for borrows formed in different iterations through different values. Not measured: no search isolates this shape.
4. **Deep Borrows and accesses through a reference** (A3's needed loss P3, both forms). Only Borrows and Moves reach across a dereference; reads, writes and destructions do not, so `h.next = n` with a live Reborrow of `h.next`'s referent neither conflicts nor ends it. The slot that holds a reference does not overlap its referent today ("Inline parts exclude referents", §15.6.2 line 500). Part of the change is a soundness fix rather than a loss: when the alias is formed by a call, as `let x = slot.first()` with a live child of `slot.data` (§2.2), the current text may accept a use-after-free (the A5 plan's finding; (H), not checked against the implementation). Two forms are lost, and Rust rejects both (E0502, E0506):
   - Borrowing shared, while an exclusive child is live, a prefix of the child's path: the base slot, a Place containing it, or a Place behind another reference that holds the reference the child follows. Examples: `let c = p.x@uniq; let s = p@ref; inspect(s@follow.y); c@follow = 1`, a shared capture of `p`, or `let w = q@follow@ref` while `c = q.p@follow@uniq` is live (§2.2). Use `p.y` (or `q.other`) directly instead.
   - Writing through a reference while a shared Borrow of its slot is live: `let z = r@ref; r@follow = 5; inspect(z)`.

   Counts: library 0, milestones 0, tests 0 verdict changes. Two scans of the first form looked within 10 lines after a child formed through `X` that is used later:
   - for `X@ref` with no projection: only owned Places, shared Copies and rejection tests;
   - for a whole use of `X` after `let c = X.f…@follow@uniq|ref`: only `OwnedStoredReferenceTest`. There, `ABorrowThatReachesTheReferenceStillConflicts` and the `slot.0` row of `ConflictingAccessThroughTheParentRejects` stay rejected, but report the conflict at `item@ref` instead of the later read.

   The implicit shared Borrows of a holder or a Place containing it are lost the same way: a `ref/Self` receiver on a struct that holds the reference, a comparison inspection, a capture or a `ref/T` adaptation. The scan for implicit Borrows (§7) counts them with the exclusive ones of §4.2: library 0, milestones 0, tests 0 verdict changes; its hits are the kinds §4.2 names, and one comparison `w == v` of a shared child with its parent reference, shared against shared (`StorageBoundaryTest`). A Receiver Expression on the reference itself selects through it and was already an access through the parent. The implementation already rejects the second form (`OwnedStoredReferenceTest.ASharedSlotBorrowStillFreezesItsReference`, the rows writing `p@follow`, borrowing `p@follow@uniq` and writing `t.0@follow`, lines 231, 232 and 234).
5. **No end through a reference.** A child whose base is reached through a reference does not end when the holder of that reference is replaced. Its path still names the Place through the holder, so once the holder holds a new reference, access through it to an overlapping Place conflicts while the child lives. The current text accepts these programs ("The child does not depend on the variable that holds the parent"), and so does Rust, whose assignment kills the loans of every path through the assigned place:

   ```kimi
   var x = 1
   var z = 3
   var slot = x@uniq
   var slot2 = z@uniq
   var rr = slot@uniq
   let c = rr@follow@follow@uniq   // Path rr.*.*, base rr.*.
   rr = slot2@uniq                 // Ends nothing: the base lies behind rr's reference.
   rr@follow@follow = 7            // Error: rr.*.* overlaps c's Loan (A5 step 0, case 23a).
   c@follow = 1
   ```

   The traversal form (step 0 case 30d) reads through the new node's link while the walk's holder carries the Loan of the previous step, as in §3's list walk. Rewrites: reach the new referent through a local, or end the child's last use first. Counts, A3's census (design A3 §2): 23a library 0, milestones 0 (Milestone33's two-layer follows are compound writes), tests at most 8 reborrows in 3 files; 30d library 0 (library links are `isize` indexes), milestones 0, tests 3 files with reference links and 0 traversal lines. This revision's searches (§7) agree on the verdicts: no child formed through a second dereference is followed within 10 lines by an assignment of its root while the child or the root is used later. Measured here, against A3's "at most 8": of the lines containing `@follow@follow` (53 in tests after unescaping their string literals, 2 in milestones, none in the library or SPEC), 9 lines in 3 test files form a Borrow through two dereferences (`OwnedStoredReferenceTest` 5, `DictionaryStoredReferenceTest` 3, `StoredReferenceResultBoundTest` 1); the rest read or write through two dereferences and form no child. The count is of source lines, not of reborrows: `DictionaryStoredReferenceTest`'s three are a template that builds test sources (line 83) and two `InlineData` fragments (lines 89–90), which a count of reborrows may take as fewer; A3 names its counts grep proxies without giving the search. Milestone33's two (lines 73–74) are compound assignments. No struct in the library, milestones or tests holds a reference to its own Type; the `Node` structs of Milestones 28 and 41 hold none or a `Weak<rc/Node>`.

Not a loss: an observing destruction (§2.1) states what §15.6.6's observation accesses. The step 0 prototype reads the current text the same way: it rejects 24a (`s = S(r: s.r@follow@uniq)` with a `drop` that writes through `self.r`), 26a (`replace` with a writing `drop`) and 26b (`Array<S>.clear()` with a live child through an element's reference), and accepts 27 (a shared Field). Types with a `drop` and an exclusive reference Field: library 0, milestones 0, tests 1 (a parse test of §15.6.6's `Logger`, which forms no child). Not a loss either: the `replace` exemption accepts two test programs that the implementation rejects, which the current text accepts (§10). Decision (b) on object handles (§2.1) also accepts `=` or the destruction of an `objref` or `objuniq` handle while a Borrow through it is live, which the current §13.5.5 rejects. A search for such handles reassigned within 20 lines found 4 sites, all in tests; only `StoredObjectReferenceTest.TheLoadedViewDoesNotRetainTheOuterSlot` has a Borrow through the handle live, and the implementation already accepts it.

Liveness after joins and in loops stays a may-analysis (§15.6, line 455). On fully explored programs, the prototype found no other program that the reference semantics accepts and these rules reject (§6). The toy has no slot Borrows and no Loans through two references, so it cannot show §4.4 or §4.5; the step 0 prototype covers them (§9).

## 5. Principles

- **One Concept, One Canonical Form.** One Loan identity (the site), one Place name (the access path, in which every reference, `objref` and `objuniq` included, is a dereference and every owning handle's payload extends the handle's path) and one conflict rule (path overlap, shallow or deep by the access kind) serve every check. One event ends a child, the in-place destruction of its base, written as `=`, a storage end or a `replace` target, replacing provenance chains and the self-authority shortcut.
- **Local Reasoning.** Identity, path and base are read at the site, and the Loans an operation is checked against are those of the values used after it. A verdict depends on the operations of one body on syntactic paths, with no aliasing analysis and no value identity, and the analysis is bounded.
- **Explicit Semantics.** Every operation that keeps an old reference alive (a Move, a Borrow of its holder, `exchange`, `swap`) is a visible conflict at its site, an observing destruction is a visible exclusive use, and the event that ends a child is an in-place destruction written at its base.
- **Compiler Server Protocol.** A conflict names the operation, the Borrow site and the base ("the Loan of `cursor.children[0]@uniq`, path `cursor.*.children`"), all stable source locations, so diagnostics stay small and reproducible.

## 6. Prototype

[`LoanIdentityByBase.mjs`](../Sketches/LoanIdentityByBase.mjs) is a dependency-free Node script (`node draft/Sketches/LoanIdentityByBase.mjs`). It evaluates the rules of §2.1 on a toy language. The toy has `uniq` and `ref` holders, struct locals with reference Fields (Places containing a base), Field paths, `if`, `while`, Moves, Copies, `exchange`, `swap`, `replace`, storage ends and fresh borrows.

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
| `replace` of the base, child live (§3) | accept | accept | accept | no violation |
| `replace` of the base, then the parent written | reject at the write | reject | reject | violation at the write |
| `=` to the base, child live | accept | accept | accept | no violation |
| Join, base assigned on one path | reject | reject | reject | violation on the other path |
| Join, base assigned on both paths | accept | accept | reject | no violation |
| Shared child, base assigned, parent written | reject | reject | reject | violation |

Random differential test (seeded; programs of up to about 20 statements with two levels of nesting, two parameters, four locals and two struct locals):

| Run | Accepted (all paths explored) | Of these, ending a Loan a later use still carries | Accepted with a violation | Rejected, explored, no violation | Brief rule: accepted with a violation | Self-authority: accepted with a violation | H1 checks (failures) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Seed 20261016, 200,000 programs, loop bound 3 | 41,670 (41,494) | 1,308 | **0** | 400: 225 lent base, 174 moved base, 1 violation beyond the bound | 39 | 17 | 13,136,815 (0) |
| Seed 7, 100,000 programs, loop bound 4 | 20,800 (20,537) | 667 | **0** | 188: 100 lent base, 88 moved base | 15 | 9 | 27,185,334 (0) |

The finite-domain invariant held in every accepted program: no holder used later carries a Loan whose base is itself.

The command is `node draft/Sketches/LoanIdentityByBase.mjs --random N --seed S --bound K`. It exits non-zero on an unsound acceptance, an H1 failure, a finite-domain invariant failure (a holder used later carrying a Loan whose base is itself) in an accepted program, or a named case that breaks.

**Result.** No program the proposed rules accept violates the reference semantics, and H1 held at every ending checked, `replace` targets included. Every program that the rules reject and that was explored on all paths without a violation is classified. Either it is a §4.1 or §4.2 case (with any errors that follow from it), or its violation lies beyond the loop bound and appears with two more iterations. None is unexplained. The brief rule accepts the `exchange`, `swap` and struct-Move programs, each of which violates the semantics. The self-authority rule accepts `deepestCleared` and other violating programs.

This is evidence, not proof. The toy covers:

- holders that are locals or Fields of struct locals;
- Loans formed through one reference, so every base is a holder, with Moves, `exchange`, `swap` and `replace` of holders and of the structs containing them;
- the check by value, in loops that execute one site repeatedly.

It does not cover references stored behind other references (bases such as `h.*.next`, where no end applies), slot Borrows (§4.4), observing destructions, non-path indices, borrows of owned Places (Loans without a dereference), temporaries (its `deepest` borrows through `cursor` directly, not through the index result of §3), calls, objects, generics or raw borrows. Its loops run only up to the bound, and those parts of §2.1 rest on the argument of §2.2 and on the step 0 prototype's round 6 (92 cases, §9), which covers the crossing counterexample 29a, the traversals 30a–30d with the check by value (30c), 23a, and observing destructions (24a–26b). In the toy, H1's first condition follows almost from its semantics, which never copies an exclusive reference; the differential test carries the evidence.

## 7. Usage

| Shape | Library (31 files) | Milestones (43) | Tests |
| --- | --- | --- | --- |
| Self-reborrow loops (`x = x.f…`, or `n = x.f…; x = n` in a loop) | 0 | 0 | 0 |
| §4.1: Move of a base or a Place containing it while its child is live | 0 | 0 | 1 accepted case becomes an error; 2 rejected rows keep their verdict |
| §4.2: exclusive Borrow (`exchange`, `swap`, `let w = r@uniq`, implicit receivers, arguments and captures) of a base while its child is live | 0 | 0 | 0 accepted; already rejected by the implementation; 1 row reports at the Borrow |
| §4.4: a shared Borrow of a prefix while an exclusive child is live; a write through a reference while its slot is borrowed shared | 0 | 0 | 0 |
| §4.5: an access through a replaced holder to a live child's Place | 0 | 0 | 0 |
| `replace` of a base while its child is live (now valid) | 0 | 0 | 2 rejected rows become valid |
| Types with a `drop` and an exclusive reference Field (observing destruction) | 0 | 0 | 1 parse test, no child |

Searches over `src/Kimi/Library` and `tests/milestones` (`.kimi`), `tests` (`.kimi`, and the Kimi programs in C# string literals after unescaping `\n`) and `docs/spec`:

- One-step self-reborrow: `grep -rnE '(^|[^=!<>.[:alnum:]_])([a-z][A-Za-z0-9_]*) = \2(\.[a-z]|\[)'`. No Kimi hits.
- Two-step self-reborrow, §4.1, §4.2 and the `replace` row: a line scan reporting a reborrowing binding (`let c = X`, `X@follow…`, `X.f…@uniq|ref`, `X[…]@…`) used within the 10 following lines of a Move, an exclusive slot Borrow, an `exchange`/`swap`/`replace` target, or an assignment to `X.f…`/`X[…]`.
- §4.5: a line scan for a binding formed through `X.f…`, `X[…]` or `X@follow…` followed within 10 lines by a whole assignment `X = …`, with the binding or `X` used later, and every line containing `@follow@follow`; and a search for Field declarations of reference Type naming a `Node` or a link (`next`, `link`, `child`, `parent`, `prev`, `left`, `right`).
- Implicit Borrows (§4.2, §4.4): after a child formed through `X`, a line within the next 10 that uses `X` as a receiver (`X.m(`), a call argument, a capture or a comparison operand, with the child used after it. Hits: library 1 (a Copy of an `isize` Field, `Storage.kimi:95`), milestones 0, tests 48, each checked by hand. The one-line reservation form, a call on `X` whose arguments name `X.f…`: library 0, milestones 0, and in tests only `values.insert(^0, values.length)` on an owned Array and a Tuple element call, `pair.0(pair.1)`.
- Observing destruction: struct and enum declarations with a `drop` member and a Field of `uniq/` or `objuniq/` Type.
- Reassigned borrow handles (decision (b), §4): a binding declared with an `objref/` or `objuniq/` Type or initialized with `@objref`/`@objuniq`, assigned again within 20 lines.

The heuristic hits are owned Places, Copies of `Copy` values and owned or counted handles. Their Moves and writes already conflict with the Loans on them (`ObjectExclusiveFieldBorrowTest`, `SharedObjectOwnershipTest`, `SiblingMoveBorrowTest` and others), except the cases named above.

## 8. Rejected options

- **The first draft of this proposal (the "brief" rule):** end a child on every complete replacement of its base, and forbid only Moving the base itself. It is unsound: `exchange` and `swap` replace the base completely while moving its old reference out, and `r@uniq` is a Borrow of the slot, not an access through `r`. The second program of §3 then writes `x` through `old` while `c` is live. The current text rejects it, and Rust rejects `mem::replace(&mut r, …)` in the same position. The prototype accepts it under that rule (§6). The hypothesis that draft stated ("the old referent stays reachable only through holders that carry the parent Loan") also holds for this program, so it was the wrong invariant; H1 replaces it.
- **An end across a borrow dereference** (the root-most kill of the first revision): end a child where the holder before its first borrow dereference is destroyed in place. With deep Borrows it is sound (step 0 prototype round 6, 92 cases) and keeps §4.5's programs. The owner did not adopt it (§2.2).
- **`clear` as a target destroyed in place** (A3's relaxation (c)): end the children based in the cleared elements. Sound only together with the deep destruction of the elements, and not adopted by the owner; `clear` borrows its receiver exclusively, as any other method does.
- **Excusing Loans by identity:** one Loan per site with self-authority (the local patch), an authority set of the Loans a holder carries, or skipping the Loan a Borrow forms by its identity. Finite, but unsound (§1, §2.2, §6).
- **Ending on writes through references, as Rust's assignment kills do.** This keeps §4.5's programs, but a kill then depends on whether a written path is the base, which needs a definiteness rule for paths behind references and indices. No program in the library, milestones or tests uses the form.
- **Identity (site, base), renaming the base on a transfer.** Keeps §4.1, but a Loan's identity gains a second dimension (sites × holders), and the rename covers one family, which appears in one test.
- **Bounded place names (k-limiting) or exact Places with widening:** they keep provenance and summarize paths past depth k or at loop heads. Summary Places need weak updates, lose `deepest`'s precision past the cut, and the cut is arbitrary.

## 9. SETTLED, DECISIONS and other proposals

- **SETTLED.** No entry covers Loan identity, provenance or transfers of a reborrowed parent. "A borrow-only uniq pair follow bounded by its slot alone" rests on containment (§15.5, §15.6.3), which this proposal keeps.
- **DECISIONS.** No current entry decides A5's Loan identity. R4-M24, which defined A5-1's holders and Loans, is superseded by R4-M33, which removed its units and its open Q1-ref finding (a complete replacement through a reference). This proposal answers Q1-ref's question for child Loans: such a replacement neither conflicts nor ends the child, and later accesses through the replaced reference conflict (§4.5). The A5 design that implements this proposal records its decision there.
- **[Single MIR plan](<2026-10-10 Compiler Size Reduction and Single MIR Plan.md>).** A5 (§5.5, line 292) checks access with `places_conflict` and records the location in its Loan table (`Loan{Place, 種類, RegionVid, 位置, Bound}`, line 272). This proposal makes the location the identity and the path the Place.
- **[A5 design A3](<../Sketches/A5 Step 0/design-A3.md>)** (§9 item 4, approved by the owner on 2026-10-17 with A3's other items and recorded in commit `72a781f8`) asked this proposal to add five points, which §2.1 now states:
  - deep Moves and Borrows, shared Borrows included (A3's P3);
  - the check by value, never by Loan id (A3's check sets by holder);
  - the end on every step, never across a borrow dereference, with own steps as storage (the A5 plan's `within`);
  - an observing destruction as an exclusive use up to the first shared reference, at `=` and in destroying calls;
  - the exemption of a target the operation destroys in place, `replace` only.

  Not adopted, by the same decision: the root-most end, although it is sound with P3 on all 92 cases of round 6, and `clear` as a target destroyed in place (§8). Evidence: [the step 0 prototype](<../Sketches/A5 Step 0/prototype.md>), rounds 1–6 (92 cases), and `A5RulesPrototype.cs`. Object handles follow A3's step classes (§3), by the planning session's decision (b) of 2026-10-17: `obj`, `rc` and `arc` payloads are own steps that keep §13.5.5's handle Loan, and `objref` and `objuniq` are borrow dereferences with no separate handle Loan, which changes §13.5.5 for borrow handles (§2.3). One difference remains between this text and A3, for A3 to adopt this rule or to list the form:
  - **Borrows past a shared reference** form no Loan here (Rust's rule), so `let v = s.r@follow@ref; let t = s@move; inspect(v)`, with `r: ref/i32`, stays valid as today. A3 forms a Loan per Ref site, these included (its case 27 ends one), so its deep Move rejects that program, a form A3's loss table does not list. (H) from A3's text; not run.
- **[Complete Polonius A5 Plan](<2026-10-16 Complete Polonius A5 Plan.md>)** (merged from `dev` at `e4457e24`) consumes this rule as its S1: the row of §5.2 (line 480), the facts of §4.2 (lines 164–166) and the rules of §4.3 (lines 183–232). Since its revision `f57df2b7`, it states the same rule: the base before the last borrow dereference, the `within` end on every step with own steps as storage (line 183), P3's two forms with the `replace` exemption (lines 218–227), the own-site check (line 244), "No kill across a Deref" (§4.3, line 247) and the losses 23a and 30d. Its object-handle note (§5.2, line 516) follows decision (b) in `25dfb297` on `claude/test-time`, which is not in `dev` yet: an owning handle's payload is an own step that keeps §13.5.5's handle Loan, and `objref` and `objuniq` are borrow dereferences, so `=` on such a base ends the child, as §2.1 states. One wording differs: the plan's own-site bullet checks an access through a reference held in `X` against the Loans "that holders other than X keep live". Read literally, that also excuses the Loans `X` itself carries, which would accept 30d, which the plan lists as a loss: there the walk's holder `node` carries the Loan and the read goes through `node`. With A3's root holders (its P1) it would also accept (H, not run) `s.b = s.a@follow.f@ref; s.a@follow.f = 3; inspect(s.b@follow)`, with `a: uniq/T` and `b: ref/i32`, where `s` carries the shared Loan on `s.a.*.f` and is written through; with the plan's Move Path holders, `s.b` is another holder and the program is rejected. A3 and this proposal exclude only the values the operation replaces.
- **The step 0 prototype** (in `dev`, merged into this branch) checks the plan's rules and A3's on 92 hand-encoded programs. It reports that the first draft of this rule accepts 5 programs the current text rejects: `exchange`, `swap`, and `exchange` performed as a call or inside a user callee. The plan's S1′ is unsound on 29a, without deep Borrows, and on 30c and 20b/20f, through exclusion by Loan id. A3, the rule this proposal now states, has no unsound acceptance and lists 23a and 30d (§4.5) among its losses.
- **Raw Storage as Storage** (landed in `de80a4c0`) owns the overlap definition in §3.4, which this proposal uses unchanged. Raw-Place borrows keep fresh anchors.
- The other open proposals (Contract bodies and conformance, LSP, debug adapter, closure parentheses, semantic tokens, library binding reuse, work items, AGENTS and PLAN restructuring, named aliases) do not touch Loans.

## 10. Integration

An intake updates, together:

- **SPEC:** the text of §2.3 (§13.5.5 line 353, §15.6 lines 450 and 453, §15.6.2 lines 495 and 500, §15.6.3 line 513, §15.6.6 line 596, §3.5.1 line 359, Appendix E line 66 and the new row). It adds `deepest`, `deepestCleared`, the `replace` and `exchange` programs and the observing destruction (§3) as §15.6.3 and §15.6.6 examples, and the moved-base error (§4.1). The Japanese translation `docs/SPECJ.md` follows only on an explicit translation update (AGENTS).
- **Tests:**
  - `StoredReferenceLoanTest.AChildRetainsItsInputAfterTransferringTheParentReference` and its fixture become a rejection at the Move.
  - The two rows of `ParentRestrictionsFollowAcquisitionAndCopies` expect the Move as the conflict site.
  - `OwnedStoredReferenceTest.ABorrowThatReachesTheReferenceStillConflicts` and the `slot.0` row of `ConflictingAccessThroughTheParentRejects` expect the conflict at `item@ref`, and the second row of `ConflictsThroughBothLayersReject` at `t.0@uniq`.
  - `ContentUpdateTest.OldContentAndSlotViewsCannotSurviveReplacement`'s first row (`let child = slot`, then `replace(slot@uniq, …)` and a read through `child`) and the `replace` row of `ExclusiveContentChildrenBlockEveryUpdate` become valid programs; the slot-view row and the other rows stay rejected.
  - The §2.2 program, rejected at `let w = q@follow@ref`, is a new test, together with its reverse order, and so are the call case (`let x = slot.first()` with a live child of `slot.data`, and the reverse order) and the §2.1 program that reads through `q.p` past a shared Borrow while `t` is assigned.
  - New A5 rule tests cover: the loop and its invalid counterpart; both join variants; the traversal `node = node.next@follow@uniq` with `obj` and `uniq` links (30a, 30b) and the write through the old link (30c); ending by assigning a base or a Place containing it; ending at a scope or temporary end; `replace` of a base with a live child, valid, and a later write of the parent, rejected; `exchange` and `swap` of a base and a struct Move, rejected; the loops 20a and 20b of the step 0 prototype; §4.5's 23a and 30d; `h.next = n` with a live Reborrow, valid; a shared Borrow of the base slot with a live exclusive child; a Borrow past a shared reference (`h.link.x@ref` with `h.link = o`); a Borrow past an `rc` handle with the handle replaced, rejected, and with the `uniq` reference in front of it assigned, valid; a Borrow through an `objuniq` handle with the handle assigned, valid, and `edit`'s write through `view` while a Borrow of another Field of its payload is live, valid; and an observing destruction with a live child, at a storage end, at `=` and in `replace`, with its shared-Field counterpart valid.
- **Milestones and library:** none affected (§7).
- **STATUS and PLAN:** the A5 boundary rows when A5 implements this.
- **DECISIONS:** the A5 design entry in the commit that first implements access paths and bases.
- **`draft/INTEGRATED.md`:** the intake record, written by the intake, not by this proposal.
