# A5 step 1: the 1b design diff, planning-session choice

Date: 2026-10-17. Base: origin/dev `5c42b577`. Inputs: two independent rule designs
([design-1b-rules-A](design-1b-rules-A.md), [design-1b-rules-B](design-1b-rules-B.md)), two independent MIR-fact designs
([design-1b-mir-A](design-1b-mir-A.md), [design-1b-mir-B](design-1b-mir-B.md)), the case 23a proposal's independent review
([review-23a](review-23a.md)) and the design review of this choice ([review-1b-r1](review-1b-r1.md)). Working records, not
authority: SPEC and DECISIONS decide. Status: **decided.** Design review round 1 (B1, B2 redone below) and round 2 ([review-1b-r2](review-1b-r2.md):
record fixes and E11) are done; the owner decided §4 on 2026-10-17 (DECISIONS R4-M52). Prototype: `../A5 Step 0/A5RulesPrototype.cs`, round 10 (`../A5 Step 0/prototype.md`).

## 1. Rules (A3 errata)

| Item | Chosen | From | Why |
| --- | --- | --- | --- |
| G119 (36h) | While a Loan sits in its reservation holder (the witness holder is its reservation holder, as `mode(L, H)` reads it), a write or destruction of a prefix of its Place, across borrow Derefs, reaches it (`hits` gains one disjunct; checked at the access on `σ_in`). A destruction includes a destroying call's activation (`replace(cur@uniq, …)`, 38g). | B's rule, A's window (review W1) | SPEC 15.6.7. With G92b's fold (§2) no reservation is formed through another reservation's holder, so A's `prep(R)` chain fact is unnecessary: round 10 rejects A's 38d at the write. One rule for call, writeback and destroying activations. If the owner chooses G92b (b1), `prep(R)` returns (≈ +25 lines). Regressions: 36h, 38a–38i, 84, 85, 107. |
| 33c | `excused(L, ℓ1) :- reserved(R, _, ℓ1, ℓ2), excused(L, ℓ2)`; 33c keeps "valid". | both (identical) | SPEC 8.11.3 `takeTwo`. 33d/33e guard against over-excusing. Unit 2f. |
| G110 | A Ref whose Place has a shared Deref forms no Loan; its value carries `content^j(σ(root))` for the layers from its last shared Deref on. | both (identical) | SPEC 15.6.3 (S1), integrated; no loss, fewer Loans. Aligns A3 with adopted text (R4-M39), so no owner decision. |
| obs shared cut | 1b's `hits` has no `throughShared` (vacuous once no Loan lies past a shared Deref). Unit 2f re-adds a cut only if F6 places `Φ_c` past a shared Deref. | A's condition, B's deletion | No speculative mechanism in 1b; 2f owns `Φ_c`. |
| Containment (A3 §7) | Wording: the containment cut is the derivation cut, counted outward from the Borrow (layers from its last shared Deref on), not "the same cut as obs". | both | SPEC 15.6.3. Unit 2a. |
| Own-value seed (39o, review B1) | The closure of `CS(ℓ)` is also seeded with `σ_in(T)` for each holder T named by a direct Loan (or `E` for `Φ_E`) of the value ℓ stores into a live holder or `E` (the Ref's own Loan and its R2 parents from the layers it carries); the closure of `CS_call(ℓ, t)` with those of `σ_in(t)`. Conflict candidates are unchanged. | B (39o), review (general rule) | SPEC 15.6: "a value is also used wherever a value that carries a Loan on a Place overlapping the first value's holder is used". A3's closure starts from the holders live after ℓ, where ℓ's own value is absent: unsound on 39o (a direct Loan), 100 (an R2 parent), 100d (through a retaining call), 101 (`CS_call` drops `t` whole). 39o's narrower row is replaced. 109 (`s.r = s.n@ref; s.n += 1`, s dead after) becomes an error, as SPEC's sentence reads and rustc does (E0506). "Live" is `transit`'s: in `live_out(ℓ)` or made live by the closure (review r2). |
| Slice index | An Index whose receiver is a Slice handle is a shared borrow Deref in A3's step classes, not an inline step (a step-class row). UniqSlice has no built-in Index (its selections are `index`/`indexUniq` calls). | MIR-B finding (ii); review N1 | SPEC 4.8.1: a Slice element is the handle's referent; element Copies read the content layer. The inline step either accepts 42a or rejects SPEC-valid 42b and SPEC 4.8.2's `first`. |
| Guard-local end | The guard-local Origin's marker `γ_H` (SPEC 14.8.3): each value issued into a guard-candidate holder H (`MirLocalRole.GuardCandidate`) also carries `γ_H`, which never conflicts and is carried like a Loan but is no Place Loan (`ended` does not remove it); `err(ended)` is `EndBorrow H at ℓ ∧ H a guard candidate ∧ γ_H ∈ CS(ℓ)`. The Loan-based `err(ended)` row is deleted; other `EndBorrow`s get no ended check. A3's partition row extends to `err(ended)` when `γ` reaches `_0` (one failure, one code; unit 1c). | planning session; review N2 | Under G110 a shared-mode Subject's candidates form no Loan, so the Loan row accepts SPEC 14.8.3's own example (43a, 92g). A marker on every `EndBorrow` rejects the valid 44c (earlier iterations' markers that nothing ends). It is the Loan-side placeholder of SPEC's `g`, an Origin with an end inside the body that the Origin stratum (no liveness) cannot judge, as `Φ_E`/`τ_s` are markers; it makes review-23a's C2 lowering contract unnecessary (N3). Rejected: a Loan for candidates only (a clause for one form); the Origin stratum. |
| Destroying call (E11) | A3 §5's `err(destroy)` excludes `hits(X, place(L), write)` for an `InPlace` target (`uborrow` for the `clear`s), the activation's own kind, and is checked on `CS_call(ℓ, t_K)` with the own-value seed. | records review r2 (F1) | As written (`¬hits(…, uborrow)`, a deep kind) the row never fires, so A3 accepted a `replace` that destroys an observing target while a child lives (26a; SPEC 15.6.6, "in a call that destroys a target") and 123 (`s.q = s.r@follow@ref; replace(s@uniq, …)`, s dead after; Rust E0713/E0502). |

## 2. MIR facts (builder contracts; supersede parts of R4-M37 and R4-M40)

| Item | Chosen | From | Why |
| --- | --- | --- | --- |
| G92a | **Assumed completion:** on a check-only continuation every node is built and makes its own value and accesses as at run time. A node whose Binding Type is Never (transfer, `$abort`, a call or intrinsic of Type Never) has no value: its consumer takes an unwritten Never temp that carries no Loan and fits any operand position (SPEC 3.8; the validator reads a Never operand's Type as unknown, one rule for Use, operators, aggregates and calls). A placement rvalue (`Set`) that has an input and whose every input is that Never value is not built, and then the old value at `=` is not dropped either (`Overwrite` keeps MIR-B's predicate), so `let x: i32 = stop()`, `x = stop()` (`Replace` takes a Never-typed temp when its right side has Type Never, review B2) and `b = stop(); consume(b@move)` stay as today; a Call is still built with its Never operands, except MIR-B's three skip sites (a Never Place has nothing to borrow). Deletes `Reached` and the non-completion gates (Set's, Slice, Boundary, Collection, Observe, Format); the validator's call-only exception goes. Prerequisite α: Binding gives a Never element of a tuple, fixed-array or enum-payload literal its expected element Type. | both; B's placement rule; review B2 | A5 then needs no assumed-path clause. Checks: `MirBuildTest`, `MirInitTest` (C3 rows plus `x = stop()` and `b = stop(); consume(b@move)`), `MirEffectsTest` and the ledger (A4 counts check-only effects, N4), 12 goldens (N5). A's "a diverging Call writes its destination on its check edge" is not taken (SPEC 14.10.3 leaves it open, §5). |
| G92b | **(b2) Fold:** on a preparation path, a written exclusive borrow that the path continues through is not formed; the path's Place is the Place it lends, so the input has one reservation at its lending point, as `holder.items.append(x)` has. The fold applies only on a reserved input's path; on a shared input's path a written exclusive borrow stays a live exclusive Borrow (SPEC 7.3.3: `tasks@uniq.length`): corrected by the planning session on the SPEC revision session's finding against N7's first reading (DECISIONS R4-M52). A3's `mode` rule stays. | both | SPEC 15.6.7: "the same call". (b1) would reject `let r = holder.count@ref; holder@uniq.items.append(1); inspect(r)`. Owner decision (§4): supersedes R4-M37 G5's path clause. |
| G123 (new; planning session, A3 E10) | **An operand is read where it is evaluated:** a Place operand (Copy or Move) of an operation is secured into a temp (the existing `Secure`) when a later operand of the same operation builds code; covers call receivers and arguments, aggregates, operators, collection literals, raw and Slice selection receivers and pair-layer arguments. | review §4 (MIR-B finding (i)) | SPEC 12.2 ("operands are evaluated once, left to right"): today `f(x, do { x = 5; 0 })`, `(x, do { … })`, `x + do { … }` read the new x: wrong code at run time on the MIR path (a written `@move` is already moved where evaluated). Takes over G94's handle copy and deletes `Slice`'s own copy. |
| G94 (planning session, A3 E10) | **Locate:** a built-in selection lends its receiver's storage shared where it locates it: the range's `RawSlice` backing or, for an element, a guard ended by `EndBorrow` once the index is evaluated, before the element's own access; the guard has the element access's problem subject (N8). The receiver of a built-in index is no reservation. (The Slice handle and raw pointer copy is G123's.) | both; review N1, N8 | SPEC 4.7.4. Amends R4-M40; closes its Open. Regressions 112 (rejected), 112b (valid). |
| G91 (planning session, A3 E10) | The Destruction walk enters each reported part once per query (`IMirDestruction.Enter`); the visitors' own cuts (Observer's payload set, A4's `glued` check) go. `HoldsRaw` gets the per-query cut `HoldsLoans` has. The unit probes growth through a composed destructor's or witness's atoms (A4's `Process` restarts at depth 0); a hang is a separate cause, its own finding and commit (N9). | B | Same sets as today, no new allocation; the bound of 32 stays exact for growing Types. |

## 3. Units and order

- **Parallel, no serialized MIR file:** α (Binding); G91 (one MirBuilder line for `HoldsRaw`'s scratch); A5 core
  development in its own files (`MirLoanSets`, `MirBorrows*`) with §1's rules.
- **Serialized MirBuilder lane** (with R5 and the G96 session's hold): G92b → G123 → G94 → G92a.
- **1b completes** after G92b (38d, 38l–38n need the fold), G94 (112 is accepted without the guard) and G91 (recursive Types'
  Drops are deep `Unsupported_Kd` without it). G92a may follow the core in shadow, its check-only errors listed as known gaps.
- **1b's own units:** 1b-1 `MirLoanSets`; 1b-2 `MirBorrows` pre-pass (holders, Loans, step classes with G110 and the Slice
  row), liveness, carrying with call retention (`TyCtxt.Retains`, unit 1a) and stores, check sets (transit, closure with
  the own-value seed) and the conflict check; 1b-3 reservations (mode, activation, G119, destroying calls and `obs`);
  1b-4 guards (`γ`, `err(ended)`), with `partition` leaving the Loan stratum for A6's merge (A3 E12); 1b-5 the driver's step 4 in shadow over the library and the
  milestones (no A5 problem; R4-M12 from here), the SPEC-example families F1, F2, F3, F10, round 10's cases as MIR-layer
  tests, scaling shapes 1–8 and `MirBorrows.Work` (V16). Each lands with its rule tests; until 1b-5 the library and
  milestones may report A5 problems (plan §6.2).
- **Case 23a**, if adopted: its kill lands with or after the core (G119 in it: C1); C2 holds by `γ` (N3); C3/C4 are text
  conditions of its SPEC revision.

## 4. Owner decisions (2026-10-17, each as recommended; DECISIONS R4-M52)

1. **G92b: (b2) fold** (chosen) over **(b1)** an A5 reservation chain. Both depart from R4-M37 G5, which the owner
   approved as A3 §9 item 8. (b2) follows SPEC 15.6.7's "same call" sentence and is smaller (≈ +5 lines against ≈ +25 plus
   G119's chain fact); (b1) adds the loss above.
2. **G92a: assumed completion with α** (chosen) over **keeping `Reached`** for Never-part operands with a located
   `Unsupported_Kd` where the value may hold Loans until α lands. Both depart from R4-M37 G6's `Set` sentence.
3. **Case 23a** ([review](review-23a.md): adopt with conditions; no unsound acceptance the rule causes; it only removes
   errors). Options: (a) keep the last-dereference base (23a, 30d, 35a, 35b and 11 of the review's cases stay losses; 35a and
   35b join A3's loss rows); (b) adopt the first-dereference base; (c) adopt (b) worded without "base": destroying in place
   a Move Path ends every Loan whose access path it exactly prefixes (identical verdicts on every round-10 case; deletes the
   base pre-pass and the Base glossary row). Chosen: (c), answered 「(c) base なしで採用」. Conditions: G119 lands first or with it (84, 85); `γ` lands
   with or before it (C2, by N3); the proposal's §4 argument is amended (rc/arc own steps, reservations, guards, the "new
   value cannot reach old storage" step) and §7 adds SPEC §13.5.5's pair-layer cell; A3's matcher is aligned on 34c under
   any option. Adoption supersedes only R4-M39's last-base/`within` part and needs an explicit SPEC revision (§15.6.3,
   Appendix E, §13.5.5), which can go with P12's. Census: library 0, milestones 0, xUnit 0 verdict changes (grep proxies, H).
   Under (c) the matcher condition on 34c is moot: E8's "any steps" after K ends a Loan based at `refs[i]` when `refs` is
   destroyed.
4. **The SPEC revision** (P12, the Loan ending and §5's gaps): in a separate session. Answered 「別セッションで実施」.
5. **Retention through an erased callable** (unit 1a, second review round, the same family as its first: missing
   premises): a callable whose implementation a call cannot see (a Function Type value, compared under its conversion
   site's premises; a Callable-bound value with no concrete body; each callable Type inside a contract) relates every
   fixed input-position Origin to every fixed Origin of its result or writable storage (SPEC 15.6.4: an unproven absence
   of retention never permits access); per-call Origins by the Function Type's own clauses; Closures and function items
   keep their binder chain. Answered
   「固定 Origin を関係ありと扱う」 (rather than a hold with a located `Unsupported_Kd`). Census: library 0, milestones 0.

## 5. SPEC gaps to report (no rule change)

- (Clarified in SPEC at `0779b975`) SPEC 14.10.3: a Never form's assumed completion does not initialize what it is placed
  into (`let y = fail(); use(y)` in check-only code).
- (Clarified in SPEC at `0779b975`) SPEC 15.6.7: "overlapping arguments" read through Loans (`g(s@uniq, s.r@follow@ref)` with `r: ref/i32` is valid by S1's
  "forms no Loan"; read on Places it would be an error). Both rule designs read it through Loans; Rust agrees for the two-phase method form (`s.h(&*s.r)`) but rejects `g(&mut s, &*s.r)` (E0502).
- (Withdrawn) SPEC 15.6.7 against a written exclusive borrow on a shared input's path: SPEC 7.3.3 already decides it (a
  live exclusive Borrow), so it is no gap; the fold excludes shared inputs.
