# G160 plan: independent review, round 5

Reviewer: the reviewer of rounds 1–4; author of none of the designs.

Scope:

- the revised plan (792 lines);
- the PLAN rows G160 and G182.

Both are uncommitted, in worktree `bin/worktrees/g160-plan` on base `59b78023`.

The new probe is `scratchpad/g160/review5/v1.kimi` (with `.out`), run on the dev `a1677549` binary through the same harness:

| Probe | Shape | Today |
| --- | --- | --- |
| v1 `projOwner` | `contract Src {associate Item}`; `RefSrc {a}` with `associate Src.Item is ref/i32 during a`; `PD<S> {let data: raw/(S.Src.Item)}`, its own allocation, `put(self: uniq/Self, v: S.Src.Item)` through `Raw.initialize`; `d = PD<RefSrc>.init(); d.put(x@ref); x = 5` | binds; error `x = 5` (raw clause: `PD<RefSrc>` holds no Loan) |

## Round-4 findings

- **R4-B2: resolved.**
  - K′ sentence 2 again allows storage whose owner's Loan a `Loan<T>` Field keeps (`uniq` for writes).
  - "Therefore not K′-kept" is gone.
  - The note that a K′-kept view with an exclusive marker is sound (rule G's guard and P13 for calls, E in-body) is right.
- **R4-B1: stated, but the interim boundary is not yet exact.** See R5-B1 and N1. These parts are right:
  - §0, §4.6 and the G182 row state the class.
  - Q5 offers (a) a limit, (b) a hold and (c) a reader obligation.
  - Minimal-core item 1 (non-public `let` Fields) confines in-body raw access to the declaring module. The library's raw Fields
    already are `internal let` or private `let`.
- **N1–N3: adopted.**
  - §4.6 (ii) is a body-level hold with its cost and a whitelist form.
  - §1.2 and §7 carry `ownRefsRead`.
  - G182's wording is fixed.

**Reporting the interim at the declaration in Binding is acceptable.** It is a valid form under the revised SPEC that the
implementation cannot yet support soundly, so it gets the single location-only `Unsupported_Kd` (AGENTS Diagnostics). Binding owns
declaration validation (G160-2). It is a temporary structure with a removal condition (G182), so its DECISIONS entry belongs in
G160-2's commit.

## Blocking finding

### R5-B1. The "slot made non-Phantom" predicate misses part of the widening class

- **Location.** §1.3 (the paragraph after item 5), §4.6 ("such a declaration is a located `Unsupported_Kd`"), G160-2's work, the G182
  row.
- **Problem.** The plan defines the boundary twice and treats the two definitions as equal: "a pointee [that] holds a
  Loan-capable part other than through its Type's parameters", and "such a pointee makes one of the Type's slots non-Phantom".
  The Binding check uses the second. The two differ wherever a Loan reaches the pointee through an associated projection rather
  than through a slot of D:
  - `PD<S> {data: raw/(S.Src.Item)}` has no slots, so nothing becomes non-Phantom.
  - With `RefSrc {a}`'s `Src.Item is ref/i32 during a`, `d.put(x@ref)` stores a local Loan (v1).
  - Today `PD<RefSrc>` holds no Loan, so the raw clause sends the store to E: v1 is an error.
  - Declared `via self`, the guard walk walks the pointee (`RefSrc.Src.Item`, abstract or resolved) as `PD`'s own storage, so `PD`
    becomes a holder. Its stores move from E to the keeper, and an in-body raw read joins G182's hole.
  - The Phantom check does not catch it.

  The same escape occurs one level down: a pointee `Wrap2<S>` with `Wrap2<S> {v: S.Src.Item}` mentions only D's parameter, yet
  holds a Loan through the projection. Of the other candidate escapes:
  - a callable pointee without a slot is not one: a common Function Type's environment must be Owned (15-ownership:203, :207);
  - a `during static` borrow is not one either: it cannot receive a local Loan.
- **Evidence.** v1 binds and errs today through the raw clause. §4.1 itself counts "an abstract part that is not D's parameter (an
  associated projection counts)" as a Loan-capable part, and `Concrete` for a nested instance.
- **Correction.**
  - Define the boundary as §4.1's `Concrete`, restricted to the `via self` pointee: a borrow or pair layer, a Slice, a callable,
    an abstract non-parameter part (projections included), or a nested instance whose `Concrete` is set. K′ already uses this
    test ("`Concrete` restricted to the pointee").
  - Give it one owner, because Binding reports and `Keeping` consumes. §4.5 already allows this: Binding publishes it on `AdtDef`
    beside the declarations, and `Keeping` reads it there instead of recomputing it.
  - Add `projOwner` (declared) to §1.2 and to the declaration tests.
  - This is the second round on the widening-class family (R4-B1, R5-B1). If this fix does not settle it, the owner chooses between
    Q5's (a)–(c), or the whitelist form: a `via self` pointee is built only from D's Type parameters, and everything else is
    `Unsupported_Kd`.

## Non-blocking findings

- **N1. The boundary over-catches, and §1.2 contradicts it.**
  - Under either definition, a `via self` keeper that is a holder today through another Field is `Unsupported_Kd`, although its
    declaration widens nothing (its stores already reach the keeper alone). Two examples:
    - `MarkBox {a}`, which has its `Loan<ref/i32 during a>` marker;
    - a keeper with a separate `n: ref/i32 during b`.
  - §1.2's `MarkBox` row still says "valid with `via self`".
  - Either narrow the boundary to "the pointee is `Concrete` and the rest of the declared storage is not", which is exact for a
    declaration, or state the over-catch and fix the `MarkBox` cell to `Unsupported_Kd` until G182.
- **N2. §7's guard test.** "A `via self` pointee holding `uniq/T` with `T = ref/i32`" is itself in the `Unsupported_Kd` class if
  the pointee is `uniq/T`, which is a borrow layer. Pin the rule with a parameter pointee instance, such as `KeptBox<uniq/(ref/i32)>`
  with `via self`.
- **N3. An unlisted held-view change.** Walking a `via self` pointee as own storage gives a keeper over an exclusive-reference
  argument the guard concretely (`Through`) instead of through its component (`ThroughArgument`). Examples are
  `ArrayOwningIterator<uniq/(ref/i32)>` (through `OwnedRemainder`) and `Buffer<uniq/…>`.
  - A store past them is then no longer P13-held; its conflicts become Language problems.
  - This agrees with R4-M76's per-part direction, and no existing held test uses `@move` iteration over exclusive elements.
  - It still changes verdicts: list it in §1.2 or §7 and check it in the census.

## Checks with no finding

- The call side is unchanged and sound: `Keeping`, the Fields-free Unowned walk, `via self` in the guard walk, and W rules 2 and 3.
- In-body raw access stays E, and minimal-core item 5 and K′ sentence 3 state it.
- §1.2's read rows and `keptDirectDead` rows are consistent.
- The library is not caught by the boundary: `Buffer` and `OwnedRemainder` have parameter pointees, and the views are `via loan`.
- K′ needs no boundary, because its kept pointees are Type arguments.
- The G160/G182 rows state facts.

## Verdict

**Reject (blocking finding R5-B1).** The fix is local: define the boundary by §4.1's `Concrete` on the pointee, with one owner
published on `AdtDef`. Also fix N1's `MarkBox` cell.
