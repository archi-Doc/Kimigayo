# Preserves Results by Receiver Place

Date: 2026-10-16 (revised the same day after an independent plan review and two independent reviews of the drafts; §8). Status: proposal for the owner's decision; not integrated or implemented. Written on the owner's instruction of 2026-10-16: a rule of the complete Polonius-style A5 that is not finitely expressible is resolved by a SPEC change decided before its family is implemented. The owner's direction of the same day applies: precision may drop somewhat for a simple and fast rule, soundness may not, and every lost form is listed and counted. Changes SPEC §6.4.6, §8.11.3, §8.11.4, §8.11.5, §8.11.6, §15.6.3, §22.2.3 and Appendix E. Companion: [Loan Identity by Base](<2026-10-16 Loan Identity by Base.md>), whose access paths name the receiver Places used here.

Base: `de80a4c0` (`dev`). Line numbers refer to that commit.

## 1. Problem

`preserves results` ([§8.11.3](../../docs/spec/08-generics-constraints-and-contracts.md#8113-preserves-results)) has two rules with no finite static form.

**Callers use value identity.** Line 774: "a call of `r` on a value `v` conflicts with no Loan kept by a result of an earlier call of `r` on `v`". Rule 3 of §8.11.4 (line 791) repeats "on the same value", §8.11.6 (line 843) "of the same callable value", and §6.4.6 (line 349) relates calls "on the same object (by dependency tracking, not Origin equality)". Whether two calls receive the same value depends on Moves, replacements, references, joins, calls that return their argument and dynamic containers; no rule over a body decides it. The current implementation reports the later call as `Unsupported_Kd` ([PLAN](../../docs/dev/PLAN.md) line 38, R1g; `CallableEffectBoundTest` lines 134–149 and 396–407).

**Conformance uses an undefined summary.** Line 774: "Conformance compares the implementation's summary with the Loans its results may keep, using region splitting". §8.11.4 (line 785) and §8.11.6 (line 845) check bounds "against the completed transitive summaries" or "on completed summaries", with the fixed point of [§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls). The §15.6.4 summary (line 531) lists static storage keys and is finite. A comparison with Loans also needs Place effects: the Places the body and its callees reach from `self` through Fields and borrowed Fields. In a recursive Type with borrowed Fields those Field roots are unbounded, and no SPEC text defines that summary or its fixed point.

**An ambiguity in the current text.** Value identity survives an update of a part of the value, unless "Destroying or replacing `v`" (line 774) is read to cover a part. Under the first reading, a generic caller keeps excluding an earlier result after swapping or resetting the Field that the implementation delegates to (§8.11.5); the later call then runs another delegate. §8.11.5's exception covers only replacements inside the body. No unsound program was built from this, and for concrete callers the caller's own Loans usually reject the replacement first (`EffectBoundImplementationTest.AReplacementBetweenCallsMeetsTheCallerLoans`). This proposal removes the ambiguity: a part write invalidates.

## 2. Proposal

Calls are related by their receiver **Place**, not their value. An earlier call counts when it was a call of `r` on the same receiver Place and nothing between the two calls wrote, destroyed or exclusively lent that Place, a part of it or an ancestor of it, other than further calls of `r` on it. A Move carries the relation to the destination. Conformance becomes the ordinary body check of A5 with one extra value, an earlier result live throughout. Calls contribute their effects as in any body, never the Place effects of their callees' bodies.

### 2.1. Callers

The receiver Place is the access path of the receiver (Loan Identity by Base §2): `source.*` for `source.take()` with `source: uniq/S`, `it` for `it.next()` on a local, and `self.*.iterator.*` for `self.iterator.next()` through a borrowed Field. The rules (sketch; `T` is `none` or a pair `(R,X)` of a requirement and a receiver Place):

```text
keeps(V,L,none,Q)  :- forms(P,V,L), !bounded_result(P,V), edge(P,Q).           // ordinary keeping (§15.6)
keeps(V,L,(R,X),Q) :- call(P,R,X), preserves(R), result(P,V), result_keeps(P,L), edge(P,Q).
keeps(V,L,T,Q)     :- keeps(V,L,T,P), edge(P,Q), !replaced(V,P), !moves_tag(P,T), !invalidates(P,T).
keeps(W,L,T,Q)     :- keeps(V,L,T,P), flows(P,V,W), edge(P,Q).                  // transfers and Reborrows of a result
keeps(V,L,(R,Y),Q) :- keeps(V,L,(R,X),P), moves(P,G,G2), prefix(G,X), Y = X with G replaced by G2, edge(P,Q).
keeps(V,L,none,Q)  :- keeps(V,L,(R,X),P), invalidates(P,(R,X)), edge(P,Q).
excluded(P,V,L)    :- call(P,R,X), preserves(R), keeps(V,L,(R,X),P), !other_tag(V,L,(R,X),P).
other_tag(V,L,T,P) :- keeps(V,L,T2,P), T2 != T.                                // none, or another requirement or Place
invalidates(P,(R,X)) :- operation(P,G,K), K in {write, assign, destroy, Move of a part, exclusive Borrow},
                        overlaps(G,X), !(call(P,R,X) and G is that call's receiver Borrow).
invalidates(P,(R,X)) :- root(X) is a static key, call(P,_,_), (summary_touches(P,root(X)) ; unknown_environment(P)).
```

- `bounded_result(P,V)`: `V` is the result of a call of a bounded `R` at `P`. Its Loans are kept with the tag `(R,X)` instead of `none`.
- A pair `(V,L)` is excluded only when every path on which `V` keeps `L` tags it with this requirement and this receiver Place. The tag is a must-fact, written with a may-fact for every other tag.
- At a call of `r` on `X`, the call's effects (rules 1 to 3 of §8.11.4) are compared with every live Loan except the excluded pairs.

Invalidation is syntactic over access paths and needs no aliasing analysis:

- A write through another reference to `X` needs that reference first, and forming it is an exclusive Borrow of `X`, a part or an ancestor, which invalidates already.
- Passing any of these exclusively to a call invalidates too, since the callee may write through it.
- A receiver rooted at a static key is also invalidated by a call whose static-key summary may write, borrow exclusively, replace or destroy that static, or whose environment effects are unknown.
- A raw write to `X` is outside the rule; not breaking the relation is the raw access's obligation ([§5.2.1](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#521-access-conditions)).
- A receiver path with a non-path index (`arr[i]`) relates no calls: one name could denote different elements.
- A Move renames a tag only to a receiver path that occurs in the body; otherwise the tag is dropped. The set of paths stays finite.

A call of `r` on `X` does not invalidate `X`: its conservative input effects alone do not end the relation, as §6.4.6 already states. Shared Borrows and reads invalidate nothing. Every fact ranges over values, Loans, requirements and access paths of one body, so the domain is finite.

### 2.2. Conformance

An implementation of `r` is checked as an ordinary body ([§15.6](../../docs/spec/15-ownership-and-lifetime-analysis.md#156-borrow-checking)), with one addition: an **earlier result** is live from entry to every exit and keeps the Loans that the body's results may keep. The ordinary analysis computes those Loans at the body's returns, so they include:

- the Loans of Borrows formed in the body, with their access paths (by site, so every execution of the body forms the same Loans);
- the caller-side Loans of calls;
- the Loans of the receiver's Origins, which have no path in the body (Loan Identity by Base §2.1).

Every body of the implementation is checked, its explicit specializations included, because they inherit the bound ([§8.13.2](../../docs/spec/08-generics-constraints-and-contracts.md#8132-inherited-contract)). A Function Item or concrete Closure offered as evidence for a Callable `preserves results` bound is checked the same way, with its call receiver as the receiver; a Closure's captures are Fields of that receiver.

Calls contribute their effects as in any body: §15.6.4 for a concrete call (its argument Borrows) and rules 1 to 3 of §8.11.4 for requirement and Callable calls, each with the static keys of its summary. Callee bodies contribute no Place effects. An effect that cannot be classified ([§8.11.2](../../docs/spec/08-generics-constraints-and-contracts.md#8112-confined)) conflicts, as today. "Completed transitive summaries" then applies to `confined` alone; the static-key summaries stay and are finite.

Two refinements:

- **Delegated results.** A Loan the body obtains from a call of a callee `d` with a `preserves results` bound (a requirement, a Callable or a virtual slot), on a Place `w` reached from `self`, is a result of `d` on `w` (§8.11.5). Rule 3 excludes it at the body's calls of `d` on `w`, unless the body moves a value on the path to `w` or invalidates `w` for `d` anywhere. Anywhere, because the earlier result comes from an earlier execution of the body.
- **Statics.** The Loans of the receiver's Origins may be anchored in any mutable static, so the body's static-key effects, direct and through its callees' summaries, are compared with them as [§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls) compares a call with live static Loans.

This replaces the "region splitting" comparison with the Loans themselves:

- A shared Borrow through a stored shared reference forms no child, so its result keeps only that reference's Loans, which reads do not conflict with.
- A Reborrow through `self` cannot be a result at all. It is contained in the receiver's Origin ([§15.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#155-exclusive-origins), [§15.6.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting), which Loan Identity by Base keeps), so returning `.Some(self.value)` for a stored `value: uniq/i32 during source` as `ref/i32 during source` is an error at the return. The earlier result therefore never keeps a Loan whose path runs through `self`. Its Loans are path-less Loans of the receiver's Origins, Loans of values it transfers out, and Loans past a shared reference. ([STATUS](../../docs/STATUS.md) line 158 at the base records this containment as specified by the P34 decisions and not yet implemented; see §5.)
- A split, raw in the body or inside a helper such as `Storage.splitFirst`, returns Loans of the receiver's `source`, which have no path. The next split's argument Borrow (`self.*.state`) does not overlap them. That the split lends no Place twice remains its unsafe obligation (§15.6.3: "never lending overlapping Places to two live exclusive children").

### 2.3. Soundness

The hypothesis H2, argued here and not prototyped: if every implementation of `r` passes §2.2, and every raw split keeps its obligations, then no operation inside a call of `r` on `X` conflicts at run time with a Loan that the caller rule of §2.1 excluded.

1. **Only calls of `r` changed the value at `X`.** Between an earlier call and the later one, every other write, destruction, Move of a part, exclusive Borrow or static-summary write of `X`, a part or an ancestor invalidates, and a Move carries the relation with the value. The later call therefore runs on the value the earlier calls left.
2. **The later call runs a checked body.** It executes an implementation body `B` of `r`, and `B` was checked with an earlier result live from entry. That earlier result keeps every Loan `B`'s results may keep, under the same identities in every execution, because Loans are identified by site. Every call of `r` on `X` in between was also an execution of a checked body, and specializations and Callable evidence are checked alike.
3. **Calls inside `B` are covered.**
   - A concrete call inside `B` is checked by its argument Borrows, as in every body.
   - By containment, the earlier result keeps only path-less Loans of the receiver's Origins, Loans of values transferred out, and Loans past a shared reference (§2.2).
   - A callee cannot write through a shared reference, and it cannot get back a value that was transferred out. So it could reach an earlier item's Place only by lending or writing again a Place that a raw split had lent out.
   - That is forbidden for every safe caller. A concrete caller with no bound in sight is held to the same rule: `let a = j.next(); j.rewind(); let b = j.next()` is checked by argument Borrows alone.
   - Requirement calls are covered by rule 1. Delegated results are excluded only at calls of `d` on `w` with `d`'s own bound, verified by the same rule; the run-time nesting of calls is finite, so induction on it covers recursion through one requirement.
4. **Statics.** Static effects are compared with the receiver-Origin Loans as if those were anchored in any mutable static.

H2 thus reduces to three things: the soundness of the ordinary body check (A5, including [Loan Identity by Base](<2026-10-16 Loan Identity by Base.md>)'s H1), containment, and the obligations of raw splits. These are the assumptions every concrete caller already makes.

The independent reviews proposed counterexamples:

- **`Wrap.next`**, which returns `step(self.inner@uniq)` where `step` calls `it.rewind()` and then `it.next()`, and **`Twice.pair`**, which calls `step(it)` twice. Both need a `J` whose `rewind` lets `next` lend again an element an earlier item still holds.
  - With a raw split, such a `J` breaks its obligation for the concrete caller above.
  - Without one, its `next` returns a Reborrow through `self` and is rejected at the return by containment. Example: a `Cursor` with a stored `value: uniq/i32 during source` whose `rewind` writes `self.value@follow`.
  - Either way it is not a valid `J`, and excluding it adds no assumption.

### 2.4. SPEC text

**§8.11.3, line 774.** The paragraph becomes two paragraphs.

- Now: "**Guarantee.** Under `T is C` with `preserves results` on `r`, a call of `r` on a value `v` conflicts with no Loan kept by a result of an earlier call of `r` on `v`, including Loans kept after transfer or Reborrow of that result. The excluded effects include reads, writes, borrows, the Loans of the new result, access to statics and captures, and cleanup inside the call; dependencies added to a result after it was returned are outside the guarantee. Destroying or replacing `v`, other requirements and unrelated Loans are outside it; it promises no purity. Conformance compares the implementation's summary with the Loans its results may keep, using region splitting ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting))."
- New: "**Guarantee.** Under `T is C` with `preserves results` on `r`, a call of `r` on a receiver Place `X`, named by its access path ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)), conflicts with no Loan kept by a result of an **earlier call** of `r` on `X`, including Loans kept after transfer or Reborrow of that result. An operation **invalidates** `X` for `r` when it is not the receiver Borrow of a call of `r` on `X` and it writes, assigns, destroys, Moves a part of or exclusively borrows `X`, a part of it or an ancestor. For `X` rooted at a static key, a call whose summary may write, exclusively borrow, replace or destroy that static, or whose environment effects are unknown, invalidates it too. A call is earlier when no operation between the two calls invalidates `X` for `r`; a Move of `X` or an ancestor carries the earlier calls to the destination Place. A receiver whose path has a non-path index relates no calls. The excluded effects include reads, writes, borrows, the Loans of the new result, access to statics and captures, and cleanup inside the call; dependencies added to a result after it was returned are outside the guarantee. Other requirements and unrelated Loans are outside it; it promises no purity.

  **Conformance.** Each body of an implementation, its explicit specializations included, is checked as an ordinary body ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)) in which an **earlier result** is live from entry to every exit and keeps the Loans the body's results may keep; those of the receiver's Origins may be anchored in any mutable static. Calls contribute their effects as in any body, with the static keys of their summaries, never the Place effects of their bodies; an effect that cannot be classified ([§8.11.2](#8112-confined)) conflicts. A Loan obtained from a call of a callee `d` with `preserves results` on a Place `w` reached from `self` is a result of `d` on `w` ([§8.11.5](#8115-delegation)). That a split never lends one Place twice remains its obligation ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting))."

**§8.11.4, line 785.**

- Now: "Its effect bounds are a separate obligation of the conformance, checked against the completed transitive summaries, including the recursive fixed point of [§15.6.4](…)."
- New: "Its effect bounds are a separate obligation of the conformance: `confined` is checked against the completed transitive summaries, including the recursive fixed point of [§15.6.4](…), and `preserves results` by [§8.11.3](#8113-preserves-results)."

**§8.11.4, line 791** (rule 3).

- Now: "*Earlier results:* with `preserves results`, the Loans kept by earlier results of the same requirement on the same value are excluded."
- New: "*Earlier results:* with `preserves results`, the Loans kept by results of earlier calls of the same requirement on the same receiver Place ([§8.11.3](#8113-preserves-results)) are excluded."

**§8.11.5, line 816.** Three phrases change; the rest of the paragraph is unchanged.

- "the result of a requirement `d` called on a value `w`" becomes "the result of a callee `d` with `preserves results` called on a Place `w`".
- "rule 3 of [§8.11.4](#8114-checking-and-use) excludes it when `d` has `preserves results` for `w`" becomes "rule 3 of [§8.11.4](#8114-checking-and-use) excludes it at calls of `d` on `w`".
- "If the body replaces, swaps or moves a value on the path to `w`, nothing is excluded" becomes "If the body moves a value on the path to `w`, or invalidates `w` for `d` ([§8.11.3](#8113-preserves-results)), nothing is excluded".

**§8.11.6, line 843.**

- Now: "`preserves results` excludes conflict with Loans of earlier results of the same callable value."
- New: "`preserves results` excludes conflict with Loans of results of earlier calls through the same Place ([§8.11.3](#8113-preserves-results))."

**§8.11.6, line 845,** and the evidence table (lines 849 and 852).

- "checked on completed summaries" becomes "checked on completed summaries for `confined` and by [§8.11.3](#8113-preserves-results) for `preserves results`".
- Function Item, nonvirtual, Now: "Its effect summary ([§15.6.4](…))." New: "Its effect summary ([§15.6.4](…)); for `preserves results`, its body checked by [§8.11.3](#8113-preserves-results)."
- Concrete Closure, Now: "Its body summary with acquisition and environment destruction." New: "Its body summary with acquisition and environment destruction; for `preserves results`, its body checked by [§8.11.3](#8113-preserves-results) with its call receiver as the receiver."

**§6.4.6, line 349** (second paragraph).

- Now: "On a slot, `preserves results` ([§8.11.3](…)) relates calls on the same object (by dependency tracking, not Origin equality) through the same slot, or for direct base calls the same implementation; replacing or Moving the payload contents ends the relation. Handle Moves, Reborrows and lawful View changes preserve the relation; a guaranteed call's conservative input effects alone do not end it."
- New: "On a slot, `preserves results` ([§8.11.3](…)) relates calls through the same slot, or for direct base calls the same implementation, on the same receiver Place; replacing or Moving the payload contents ends the relation. A Move of the handle, including a lawful View change, carries the relation; a call through another handle or a Reborrow is on another receiver Place. A guaranteed call's conservative input effects alone do not end the relation."

**§15.6.3, line 517.**

- Now: "A result may be kept across later calls on the same receiver when its Type does not depend on its call's receiver Loan and the callee's effects (for `preserves results`, its effect bound, [§8.11.3](…)) do not conflict with it."
- New: "A result may be kept across later calls on the same receiver Place when its Type does not depend on its call's receiver Loan and the callee's effects (for `preserves results`, its effect bound, [§8.11.3](…)) do not conflict with it."

**§22.2.3, line 135.** The first sentence changes; "A Move of the Iterator carries the bound" and the rest are unchanged.

- Now: "`preserves results` ([§8.11.3](…)) means that the effects of `next` conflict with no Loan kept by an item the same Iterator returned earlier, including through transfer or Reborrow of that item."
- New: "`preserves results` ([§8.11.3](…)) means that the effects of `next` conflict with no Loan kept by an item that an earlier call of `next` on the same Iterator Place returned, including through transfer or Reborrow of that item."

**Appendix E, line 87.**

- Now: "| `preserves results` | Effect bound: a call conflicts with no Loan of an earlier result of the same requirement. | …"
- New: "| `preserves results` | Effect bound: a call conflicts with no Loan of a result of an earlier call of the same requirement on the same receiver Place. | …"

The eligibility paragraph (line 772) is unchanged.

## 3. Examples

Valid, as in §8.11.1 (line 687):

```kimi
func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is StableTake
    let first = source.take()
    let second = source.take()        // Valid: an earlier call on the receiver Place source.*.
    return (first@move, second@move)
```

Invalid: the receiver Place is lent exclusively between the calls.

```kimi
func takeAcross<S>(source: uniq/S, other: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is StableTake
    let first = source.take()
    Kimi.Intrinsics.swap(source, other) // Borrows source.* exclusively: invalidates it.
    let second = source.take()          // Error: no earlier call; the call may reach first's Loans.
    return (first@move, second@move)
```

Without the `swap`, `takeAcross` is `takeTwo` and valid. A `swap` is used because `source@follow = …` would also destroy the old `S`, and that destruction has its own conflict with `first`. Both verdicts are the ones value identity gives; a Place rule now decides them.

A Move carries the relation:

```kimi
func moved<I>(iterator: I) -> (Option<I.Item>, Option<I.Item>)
    I is Iterator
    var it = iterator@move
    let first = it.next()
    var later = it@move               // The earlier call moves with the value to the Place later.
    let second = later.next()         // Valid.
    return (first@move, second@move)
```

Conformance, delegated results (verdicts unchanged, §8.11.5): `Drain` (`return self.inner.next()`) conforms, because the delegated earlier result is excluded at the call on the same `w`. `Merge` is still an error at `self.left.next()`: an earlier result of `self.right.next()` is not a result of `next` on `self.left`, and rule 1 lets the call reach the Loans `J`'s Origins may denote.

## 4. The Iterator conformances

Each body is read against §2.2: 12 library conformances and 3 in the milestones, all bound by `Iterator.kimi:6`. This is a reading of the source at the base, not a run of an implementation. If an implementation finds a row that does not conform, the rejection of a library or milestone program goes to the owner as DECISIONS O3.

| Conformances | `next` body | The earlier result keeps | Effects of the body | Expected |
| --- | --- | --- | --- | --- |
| `ArrayIterator`, `DictionaryIterator` (`Array.kimi:274`, `Dictionary.kimi:96`) | `Storage.splitFirst(self.state@uniq)` | Loans of the receiver's `source`, without a path (the result names `state.source`, not the argument's Origin) | Argument Borrow of `self.*.state`; no static keys | Conforms |
| `ArrayUniqIterator`, `DictionaryUniqIterator` (`Array.kimi:284`, `Dictionary.kimi:106`) | Same | Same | Same | Conforms |
| `ArrayOwningIterator`, `FixedArrayOwningIterator`, `DictionaryOwningIterator` (`Array.kimi:293`, `:304`, `Dictionary.kimi:115`) | `Storage.takeFirst(self.state@uniq)` | Loans of the element Types' Origins, without a path | Argument Borrow of `self.*.state`; nothing destroyed | Conforms |
| `RangeIterator`, `ClosedRangeIterator` (`Core.kimi:326`, `:359`) | Reads and writes `self.current`, `self.end`, `self.includesEnd` | Nothing (`T is PrimitiveInteger`) | Writes of Copy values through `self` | Conforms |
| `SliceIterator` (`Slice.kimi:89`) | Writes `self.position`, returns `self.values[index]@ref/T` | Loans of the stored Slice's `source`: a shared Borrow past a shared view forms no child | Shared reads through `self`; the Copy write of `self.position` | Conforms |
| `Iteration.OwningIterator<I>` when `I is Iterator` (`Iteration.kimi:15`) | `self.iterator.next()` | Results of `next` on `self.*.iterator` (delegated) | Rule 3 excludes them, together with rule 2's unknown environment effects | Conforms |
| `Iteration.BorrowingIterator<I>` when `I is Iterator` (`Iteration.kimi:42`) | `self.iterator.next()` through `iterator: uniq/I during source` | Results of `next` on `self.*.iterator.*` (delegated) | Same; nothing on the path is moved or invalidated | Conforms |
| `Cursor<T>` (`Milestone13.kimi:6`, `Milestone29.kimi:45`) | The shape of `SliceIterator` | Loans of `source` | Same as `SliceIterator` | Conforms |
| `Drain<T>` (`Milestone29.kimi:13`) | `exchange(self.first@uniq, with: .None)` and the like, by position | Loans of `T`'s Origins (the old Field value moves out) | Exclusive Borrows of `self.*.first`…; `exchange` runs no user code and destroys nothing | Conforms |

`Storage.splitFirst` and `takeFirst` (`Storage.kimi:102–179`) update the remainder's `position`, `count` and `link` Fields, access raw storage and call the raw helpers `keyAt`/`valueAt`. They touch no static, so their static-key summaries are empty. Their splits keep their raw-split obligation. This, too, is a reading of the source.

## 5. What is lost

Each form below was valid under the letter of the current text and is an error under this proposal. Every count is at the base, by the searches of §7, with each hit checked by hand.

1. **The same value through Places that do not share a path.** This covers:
   - calls through a Borrow or Reborrow of the receiver (`let r = it@uniq; r.next()` after `it.next()`);
   - a receiver reached through a join of different references;
   - a value returned by a call that received it;
   - a clone of a counted handle;
   - an element reloaded from a dynamic container.

   Any static checker of value identity has to be conservative on most of these shapes. Counts: library 0, milestones 0, tests 0 calls through a Borrow of a receiver after an earlier call on it.
2. **Other operations on the receiver between the calls:** a write to a part (`it.count += 1` on a public Field), another exclusive method (`it.next(); it.skip(); it.next()`), or a call that resets a delegated Field. Value identity kept the relation across these; keeping it would keep the ambiguity of §1. Place them before the first call or after the last one. Counts: library 0, milestones 0. Tests: 2 rows call `drain.reset(…)` between two calls (`CallReservationTest` line 218, `EffectBoundImplementationTest` line 193). Both are already rejected by the caller's Loans, so no verdict changes.
3. **Calls through a Reborrow of an object handle on a slot.** §6.4.6 preserved the relation across "Reborrows". Counts: library 0, milestones 0, tests 0 (no test calls a `preserves results` slot through a Reborrow after an earlier call).
4. **A receiver value moved to another Place by `exchange` or `swap`.** Value identity followed the value to its new Place. These operations borrow the receiver exclusively, which invalidates it, and they are not Moves, so they carry nothing. Counts: library 0, milestones 0, tests 0.
5. **A receiver with a non-path index** (`arr[i].next()` twice). Counts: library 0, milestones 0, tests 0.

Kept: the Iterator guarantee for every library and milestone conformance (§4), the Move rule of §22.2.3, and §8.11.5's verdicts. The R1g `Unsupported_Kd` rows become defined verdicts (§10).

**Not a loss: results through `self`.** Many tests model a `next` that returns a Reborrow through `self`, such as `.Some(self.value)` for a stored `value: uniq/i32 during source` returned as `ref/i32 during source`. The SPEC already rejects that return by containment ([§15.6.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)). [STATUS](../../docs/STATUS.md) line 158 at the base records it as specified by the P34 decisions and not yet implemented. The current implementation accepts these programs and compares callee summaries instead. Under this proposal they stay errors at the return, so their tests change when A5 implements containment, not because of this proposal's text. Rows expecting such a conformance to hold include:

- in `IteratorIndependenceTest`:
  - `HelperEffectsParticipateInIndependence`, `RecursiveGenericHelperEffectsParticipateInIndependence`, `GenericCallsCheckEverySelectableSpecialization` (2 rows), `LengthCallsCheckTheSelectedSpecialization`, `CompoundSpecializationKeysKeepTheirFixedParts`, `PropertySetterEffectsParticipateInIndependence`, `LocalDestructorEffectsParticipateInIndependence`;
  - `UncalledMutatingHelperDoesNotAffectIndependence`;
  - `PublishedPayloadLoansAreEffectsOfNext("ref")`;
- the 3 restored states of `IteratorIndependenceEditingTest.HelperEditRevokesAndRestoresIteratorConformance`;
- in `IteratorOriginEffectsTest`: the valid rows of `ImplicitLocalReborrowsKeepTheirOriginalAccessEffect` and `IntersectionResultsKeepEveryPossibleSource`, and the native fixture `SharedImplicitReborrowsAllowRetainedItems`;
- `VirtualEffectObligationTest.PreservesResultsComparesLoansRetainedByTheDeclaredResult("", false)`.

The list comes from reading the tests and from the third independent review. It is not exhaustive: accessors and constructors were found only by reading.

## 6. Principles

- **One Concept, One Canonical Form.** The relation between calls uses the access paths that every other Loan rule uses. Conformance is the ordinary body check with one extra live value, and calls are checked as in every other body; there is no second summary representation.
- **Local Reasoning.** Whether a call is earlier is decided by the operations on one path between the two calls in one body, with no aliasing analysis. Conformance reads the body and its callees' contracts, never callee bodies.
- **Explicit Semantics.** What ends the relation is a written operation on the receiver's path (a write, a destruction, an exclusive Borrow), and the Move rule is explicit. What the check relies on, the obligations of raw splits, is stated in the SPEC text.
- **Compiler Server Protocol.** A rejected exclusion points at two source locations: the earlier call and the operation that invalidated its receiver Place.

## 7. Usage

| Measure | Library | Milestones (43) | Tests |
| --- | --- | --- | --- |
| `preserves results` declarations | 1 (`Iterator.kimi:6`) | 0 | 70 occurrences on 59 lines in 19 files (18 C# test files and `TestData/mir-diff/classification.tsv`) |
| Conformances bound by it | 12 (§4) | 3: `Milestone13.kimi:7`, `Milestone29.kimi:14` and `:46` | not counted |
| Rows pinning R1g's `Unsupported_Kd` | 0 | 0 | 2 theories in `CallableEffectBoundTest` (lines 134–149 and 396–407) |
| §5 losses | 0 | 0 | 0 verdict changes against the SPEC; tests pinning the unimplemented containment rule are listed in §5 |

Searches:

- `grep -rn 'preserves results'` over `src/Kimi/Library` (1 hit), `tests/milestones` (0) and `tests`: 19 files by `grep -rl … | wc -l`, 59 lines by summing `grep -rc`, 70 occurrences by `grep -rho … | wc -l`.
- `grep -rn 'Self is Iterator'` over `src/Kimi/Library` (12) and `tests/milestones` (3).
- For §5, two line scans over the 19 test files, the files with `is Iterator`, the milestones, the library and `docs/spec`, with Kimi programs in C# strings unescaped:
  - a binding `let r = x…` within 6 lines after a call `x.next|take|…(` and followed within 8 lines by `r.next|take|…(`;
  - pairs of calls of one such method on one receiver within 8 lines, with a write, Move, exclusive Borrow, `exchange`/`swap`/`replace` or other method call on that receiver between them.
- For the test list of §5, the `next`/`take` bodies of the Kimi programs in C# tests that call anything other than a delegated `self.f.next()`/`self.f.take()`, an Option case or an intrinsic. The scan uses string literals joined and unescaped and finds 20 of 88; it misses accessor and constructor calls, which an independent reviewer found by reading.

## 8. Rejected options

- **A signature rule.** Exclude earlier results whenever the result Type passes eligibility, with no body check. Finite and short, but it drops the obligation that makes the exclusion sound: `Merge` (§8.11.5), or an implementation writing a static that an earlier result points into, would be accepted.
- **Invalidating on Move.** One rule simpler, but it loses §22.2.3's "A Move of the Iterator carries the bound". Tags move with the Move at no other cost.
- **Invalidation by may-reach aliasing.** Precise for writes through other references, but it needs an aliasing judgment per operation. Every such write first needs an exclusive Borrow of the receiver's path, which the syntactic rule already counts.
- **Treating results as split off the receiver,** disjoint from everything `self` reaches (the first draft). Wrong when a result Reborrows through a stored exclusive reference (`VirtualEffectObligationTest`); computing the result's Loans is both simpler and exact.
- **Taking calls at their declared effects in conformance, with `preserves results` on non-virtual methods** (the second draft). This takes every call by rule 1 inside a conformance check, so `Storage.splitFirst` would have needed a declared bound. That meant a SPEC extension (part of the Appendix D deferral) and a library change, and more test rows would change. The hazard it closed (`Wrap`, §2.3) needs a delegate that already breaks its raw-split obligation for concrete callers, so it closed nothing. It would also have made a conformance check stricter than any other body about the same call.
- **Keeping value identity with a points-to analysis,** or **summaries over Field roots with a depth bound.** A second analysis or a second effect representation beside A5's, still conservative at joins and calls, with an arbitrary cut-off.

## 9. SETTLED, DECISIONS and other proposals

- **SETTLED.** No entry covers `preserves results` or effect bounds.
- **DECISIONS.** R4-M22 assigns `preserves results` to A5 (A4 decides `confined` only). R4-M25, superseded by Q5, had rejected cutting the `preserves results` witnesses from R4. This proposal keeps the bound complete and gives A5 a finite rule. A conformance that an implementation rejects goes to O3.
- **[Loan Identity by Base](<2026-10-16 Loan Identity by Base.md>).** Receiver Places and invalidation use its access paths, Loans from callers have no path there, and H2 relies on its H1.
- **[Contract Requirement Bodies and External Conformance](<2026-10-10 Contract Requirement Bodies and External Conformance.md>) §3.3.** That proposal checks a requirement body's bounds once under `Self is X`. Its `preserves results` check would be §2.2 of this proposal under that premise; the two are compatible.
- **[Contract Conformance Redesign Options](<2026-10-10 Contract Conformance Redesign Options.md>).** Moves where bounds are declared, not how they are checked; no interaction.
- **[Single MIR plan](<2026-10-10 Compiler Size Reduction and Single MIR Plan.md>).** A5's last step is "preserves results" (line 292); this proposal defines it over A5's own facts.
- **[Complete Polonius A5 Plan](<2026-10-16 Complete Polonius A5 Plan.md>)** (merged from `dev`) consumes this rule as its S2. This proposal differs from the plan's S2 row (line 410):
  - **Moves** carry the relation instead of invalidating it (keeping §22.2.3).
  - **Invalidation** is syntactic over access paths: writes, destruction and exclusive Borrows of the receiver, a part or an ancestor. The plan's "writes through a reference that may reach it" needs no aliasing judgment here (§2.1).
  - **Calls** in a conformance check contribute the effects they contribute in any body, not their declared effects as the plan's row and census say (line 447). The reasons are in §2.3 and §8. The census of the 15 conformances (§4) then holds by reading.

## 10. Integration

An intake updates, together:

- **SPEC:** the text of §2.4 (§8.11.3 line 774, §8.11.4 lines 785 and 791, §8.11.5 line 816, §8.11.6 lines 843, 845, 849 and 852, §6.4.6 line 349, §15.6.3 line 517, §22.2.3 line 135, Appendix E line 87). `takeAcross` and `moved` (§3) join `takeTwo` in §8.11.1 as examples. The Japanese translation `docs/SPECJ.md` follows only on an explicit translation update (AGENTS).
- **Tests:**
  - `CallableEffectBoundTest.EarlierResultsRequirePreservation(true)` becomes valid.
  - In `ReplacingTheCallableCannotReuseTheEarlierValuesGuarantee`, the first row becomes valid and the second a `CallEffectConflict_Kd`.
  - The tests listed under "Not a loss" in §5 change with the containment rule (STATUS line 158 at the base) when A5 implements it, independently of this proposal.
  - The other test files that mention the bound or call helpers in `next`/`take` are rerun, and each change is classified against §5.
  - New tests cover:
    - a `swap` or a part write between calls, a Move between calls, a call through a Borrow of the receiver, and a delegated Field swapped inside and outside the body;
    - a static-rooted receiver whose static is written by a call between the calls;
    - an explicit specialization of an implementation, and Function Item and Closure evidence for a Callable bound;
    - each §4 row as a conformance check.
- **Milestones and library:** no change; Milestones 13 and 29 are rerun as conformance checks.
- **PLAN and STATUS:** R1g (PLAN line 38) closes when implemented, and so does the R1g limit in the STATUS "Callable effects" row. STATUS was consolidated on `dev` after the base (`bb8f33e7`), so its rows on transitive effect comparison (lines 48, 50 and 234 at the base) and the P34 containment row (line 158) are no longer separate rows.
- **DECISIONS:** the A5 design entry for the earlier-call facts.
- **`draft/INTEGRATED.md`:** the intake record, written by the intake.
