# Step 0 design review, round 2: design A2

Inputs: design-A2.md, the plan at `fe1a5b34` (§4.2–§4.3), and prototype rounds 4 and 5 (75 cases; re-read after the
coordinator's note). `A2:n` is a line of design-A2.md.

**Verdict: revise.** A2 fixes round 1's three blocking findings as asked. A blocking hole remains in the family of
round-1 finding 1, which asks what an access at P is checked against. This is the second round on that family, so under
AGENTS the owner chooses between a representation and a hold. I recommend the representation in finding 1, which is one
clause. Finding 2 is new: the revision's `Destroys` column exposes it.

## Round 1 items, as applied

| Item | A2 |
| --- | --- |
| B1: exclusion by holder, not by Loan id | **Done.** `CS` and `CS_call` work by holder (A2:228-229), and the mode is per holder (A2:188-190). Round 4 rejects 20a–20f, and the earlier 60 cases are unchanged. |
| B2: `!ended` via `step` | **Done** (A2:199, A2:206-208). Round 5 confirms 21a and 21b. |
| B3: missing losses | **Done.** P3 lists both forms (A2:38). The `replace` exemption is kept through `Destroys` (A2:48). |
| Graft 2, narrowed | **Done.** TwoPhaseUniq activates at its Call or at the writeback write; `EndBorrow` never activates (A2:146). Placement is coded at the write (A2:270). |
| Graft 4, text only | **Done.** B's step classes are used on `MirBody.Projections`, with no A5 interning (A2:60-67). |
| Non-blocking items 4–12 | **Done:** `Φ_E`, relaxations, V16 wording, `Retains(callee, Subst)`, the boundary records, T1, unit 2a0, the partition lemma, units 0a and 0b, and the errata. |

## Findings

### 1. BLOCKING, second round in finding 1's family: a value in transit at P is not checked

**Location:** A2:228. `CS` takes the entry sets of holders live after P, minus the holders P overwrites. A2:472-475
records this as an open item.

**Two gaps:**

- A value that P stores comes from a Temp that dies at P. Its entry set is in no live holder, so it is never in `CS`.
- The destination's new value is excluded as overwritten.

**First consequence: `x = f(x@ref)` with retention is accepted.**

- MIR evaluates `_a = Ref x` and `_r = f(_a)`. It then emits `Drop x` only when `MayNeedDrop` holds, and stores
  `x = move _r` (`MirBuilder.Values.cs:341-354`).
- If x's Type needs a drop, the separate Drop point catches the conflict, because `_r` is live into the store. This is
  the 24a shape.
- If x's Type needs no drop, the store is checked against nothing: `_r` dies at P and x is overwritten.
- x's new value then carries a live `ref` Loan on x itself. The SPEC rejects the write, under S15:453 and S15:502, and
  so does Rust (E0506).

**This is not only a relaxation. The memory-safety counterexample:**

```kimi
// E{o}: .A(n: i64) | .B(r: ref/i64 during o). E is Copy and has no drop, so MIR emits no Drop.
// pin(p: ref/E during o) -> E{o} returns .B(a reference to p's .A payload), (H) by a binding of the borrowed subject (S15:128-139).
var x: E = .A(n: 42)
x = pin(x@ref)            // A2: accepted. x is now .B, and its payload aliases its own old .A payload.
match x
    .B(let r) => inspect(r@follow)   // reads the reference's own bits as an i64: type confusion
```

Distinct-Downcast disjointness is not the cause: the variant changes by a whole write that the check set skips.

**Second consequence: 24c.** In current MIR, `=` keeps the drop and the store apart. A call whose row destroys a target,
such as `replace`, does both at one point, and A2 has the same transit gap there.

**Fix (the representation): add the transit clause to `CS(ℓ)`.** `CS(ℓ)` also takes `σ_in` of every value that ℓ stores
into a holder live after ℓ, or into `E`, or through a reference. This is the prototype's NoTransit fix, restricted to
live destinations. Without that restriction, a dead store would be over-strict.

The clause adds no exclusion by Loan id:

- At a formation `_t = Ref X`, the new Loan is in no entry set.
- At `cursor = move _t`, the stored Loan lies through a borrow Deref, so a shallow write misses it. 19a and 21a stay
  valid.

**The alternative**, a MIR contract that destruction and store are separate points, does not close the drop-free case.

### 2. BLOCKING, new: a call whose row destroys its target performs no destruction access

**Location:** `Destroys` (A2:123), the destroying Ref read as shallow (A2:232), activation with `deep = false`
(A2:239), and `base_op` (A2:126), which reaches observing Drop *statements* only.

`ended` removes the child Loan at the call. Neither the old value's destruction nor its `drop` is an access. Under
A2's own rule an observing destruction is an exclusive deep use (A2:233), and the plan's S1 (H) agrees, as does Rust
E0713.

**Counterexample:**

```kimi
// S has r: uniq/Array<i32> and a drop that calls self.r@follow.clear().
let c = s.r@follow[0]@uniq
Kimi.Intrinsics.replace(s@uniq, with: S(r: y@uniq))   // old s dropped inside: x's elements freed
inspect(c@follow)                                      // A2: accepted; use after free
```

`arr.clear()` on `Array<S>` with a child based at `arr[0].r` shows the same hole.

**Fix:**

- A call with a destroying row makes a `destroy` access of its target at the call, checked before `ended`. For
  `clear`, the access covers the elements.
- The access is a deep exclusive use if the target's Type `observes`.
- Finding 1's transit clause supplies the stored value.

### 3. NON-BLOCKING: an observing destruction reaches past shared layers

**Location:** A2:233.

**Cause:** the deep `uborrow` meets child Loans formed through a *shared* field. A2 rejects this program, which the SPEC
accepts:

```kimi
var v = other@ref
do
    let s = S(r: x@ref)    // S has a drop
    v = s.r@follow@ref     // contained only in r's Origin (S15:513)
inspect(v)
```

A drop cannot write past a shared layer (S15:509). Rust ignores Loans through `&` derefs.

**Fix:** stop the exclusive deep reach at the first shared Deref, the same cut that containment uses. Otherwise, list it
as a loss with counts.

### 4. NON-BLOCKING: case 23a

A2 already kills at the root-most base: B is taken before the *first* borrow Deref (A2:69, A2:79). That equals the
prototype's `KillAcrossDeref`, so A2 as written accepts 23a. That variant changes no other case and stayed sound,
because the child keeps the outer reference's Loan (13b, 16e, 23b).

Round 5's A mode used the plan's B, taken before the *last* Deref, so it diverges from A2's text.

**Fix:**

- Keep A2's kill.
- Rerun the A mode with A2's base.
- Record the rule as the answer to the plan's (H) and in S1's proposal.
- No loss row is needed.

### 5. NON-BLOCKING: `clear` in `Destroys` is a relaxation of S1

S1 names only `=`, `replace` and the end of storage. Rust rejects `let c = &mut *v[0]; v.clear(); use(c)`. Extending the
exemption to `clear` is sound only with finding 2's access.

**Fix:** keep `Target` for `replace` only, unless the owner approves `clear` (relaxation c).

### 6. NON-BLOCKING: P11 is an acceptable loss

Writes through `$w` only add Loans, so the loss is sound. The count of 0 / 0 / 0 is (H). The census should include raw
pointer `+=` and `-=` (S5 §5.3), if raw pointers are Loan-capable.

### 7. NON-BLOCKING: `Φ_E` on unknown provenance

It is correct and monotone, as A2:191-192 state it. **Risk:** writes through raw-derived or static-derived values move
Loans into `σ(E)`, where they are live forever. Watch the raw writes in `Storage.kimi` and `DictionaryStorage.kimi` in
1b's shadow run.

### 8. NON-BLOCKING: distinct enum-case payloads are disjoint

The relaxation is sound once finding 1 is fixed. A variant can change under a live payload Loan only by a whole write,
or by a formation that the deep ban rejects. The owner confirms it with row 8.

### 9. NON-BLOCKING: verification gaps

Plain (non-reserved) arguments of one call are still unprototyped (round 4's limitation). A2:240-244 argues them. Unit
0b must cover them, together with:

- finding 1's drop-free counterexample;
- finding 2's `replace` and `clear` cases;
- finding 3's shared-field case.

The round-1 correction from 41 to 39 cases changes nothing.

## Owner decisions

1. **The finding 1 family** (AGENTS, second round): choose the representation (the transit clause in `CS`) or a hold.
   I recommend the representation.
2. **The OD13 loss list:**
   - P1, P3 (both forms), P4, P10 and P11;
   - optional: P5, P6 and P9;
   - finding 3's row, if the shared-layer cut is not taken.
3. **Relaxations:**
   - row 8;
   - distinct Downcasts;
   - `clear` in `Destroys`, only with finding 2.
4. **S1 proposal:**
   - the own-site check;
   - the kill on every step;
   - the root-most base kill (23a);
   - an observing destruction as an exclusive use up to the first shared layer, including destructions inside
     destroying calls.
5. **Clarifications:** c1 is inferred; c2 is dissolved by P6.
6. **V16:** adopt the wording at A2:335-338.
7. **Origin boundary:** as at A2:380-400.
8. **MIR contracts:**
   - the TwoPhaseUniq writeback activation;
   - the RawSlice operand;
   - `MirCallee.Arg`;
   - the `EndBorrow` meaning.

   No closed-vocabulary entry is needed.
9. **DEC and DIAGNOSTICS records:** as at A2:437-438.
