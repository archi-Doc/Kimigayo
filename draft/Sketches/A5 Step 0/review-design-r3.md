# Confirmation review: design A3

Inputs: design-A3.md, the plan at `b7705831` (§4.2–§4.3) and prototype round 6 (92 cases). `A3:n` is a line of
design-A3.md.

**Verdict: confirmed.** Both r2 blockers are fixed and the non-blocking items are applied. No unsound rule was found.
Four non-blocking text or precision points follow.

## The checks

**(1) r2 blocking 1, transit: fixed.**

- `CS(ℓ)` adds `transit(ℓ)`, the entry sets of values that ℓ stores (A3:270-273).
- Because the sets are entry sets, a Loan born at ℓ is never in them. 19a and 21a stay valid, since the transit Loan
  lies through a borrow Deref and a shallow write misses it.
- 25a and 25c (`x = f(x@ref)`, and the enum form) are rejected at the store; 25b is valid.
- The escaped and path-exact forms are covered by 29b and 29c.
- The forms match the plan's `stored` rules, flow and `written_through`, except for one stricter case (finding N1).

**(2) r2 blocking 2, destroying calls: fixed.**

- `destroys(ℓ, K, elements)` covers every row that has `Destroys` (A3:167).
- `err(destroy)` reads `CS_call` on `σ_in`, so it fires before `ended` (A3:283-285). It is deep and exclusive (`obs`)
  when the Type observes.
- Argument temps are live into the call, so a value in transit inside a destroying call is covered.
- 26a and 26b are rejected.

**(3) r2 non-blocking items: applied.**

| Item | Where |
| --- | --- |
| 3, the `obs` cut | `throughShared` (A3:231-233); 27 is valid. No loss row is needed. |
| 5 | `InPlace` covers `replace` only, and `clear` is relaxation (c), tied to the destroy access (A3:168, A3:194, A3:472). |
| 6 | P11 counted; I confirmed 39 lib and 20 ms compound lines, and `DictionaryStorage.kimi:41`. |
| 7, 8 | The 1b shadow watch; the Downcast relaxation argued with transit. |
| 9 | 0b lists 25a–25c, 26a/26b, 27, 28a–28d and three added cases. |

**(4) The `within` decision** is stated in §1, §2, §3 and §9. The optional recovery is correct:

- P3 rejects 29a at `w` under either kill. It covers the plan's crossing rule 1 "in either order": a child formed after
  `w` conflicts at its formation.
- Rule 2 is A's R2.
- Rule 3 holds because `StorageDead` of the root is a destruction in place.

Text points: N2, N3.

**(5) Nothing else is broken.** The counts check out.

## Findings

**N1. NON-BLOCKING. Transit through a reference is stricter than the plan.**

- **Location:** A3:270 counts every store "through a reference" without condition. A3:272 claims it equals the plan's
  `stored … live(H,P) ∧ overlap(T,H) or escaped(T)`.
- **Evidence:** the plan counts a store through a reference only when a reached target T is live or escaped. A3 also
  counts it when T is dead. Example: `r@follow = g(r@follow@ref)`, with neither r nor T used later. That is a dead store
  that the SPEC accepts, and A3 rejects it. It is sound, but it is an unlisted rejection, and the equality claim is
  wrong.
- **Why:** A3 has no liveness feedback, so a target reached only through a live reference is not in `live_out`.
- **Fix (either):**
  - count a store through a reference when some reached T is in `live_out`, is E, or is named by a Loan in the
    closure. The closure is computed anyway. This is exactly the plan's rule.
  - or list the difference as a P-row (lib 0 / ms 0) and correct A3:272.

**N2. NON-BLOCKING. The kill across own Derefs is unstated in the plan, and A3 contradicts itself.**

- **A3:98:** own (`obj`, `rc`, `arc`) Derefs count as storage when B is chosen.
- **A3:111:** "A B containing a Deref is never ended".
- **The plan's `within`:** "no Deref step between", over move paths. A move path ends at any Deref (R4-M21).
- **The question:** whether `h = …` ends a child based at `(*h).r`, with `h: obj/S`. A3 implies yes; the plan says no.
- **It is sound:** destroying an `obj` handle destroys its payload in place; `rc` and `arc` are shared layers.
- **Fix:** state "borrow Deref" in both texts, or adopt the plan's literal rule and list obj-held bases.

**N3. NON-BLOCKING. 30c is misattributed.**

- **Location:** A3:118 and §9 item 4 say S1′ without P3 is unsound on 29a and 30c.
- **Evidence:** round 6 attributes 30c to S1′'s id-based authority set: `node` carries its own site's earlier Loan, which
  the set excludes. Only 29a needs P3, as the prototype's `SharedBaseOp` variant shows.
- **Fix:** S1's proposal needs P3 for 29a, and the own-site check, by holder and never by Loan id, for 30c. §9 item 4
  already lists the own-site check; only the attribution changes.

**N4. NON-BLOCKING. The destroy access can report twice.**

- **Location:** A3:283-285.
- **Evidence:** without `obs`, `hits(X, place(L), destroy)` reaches the same Loans as the call's own activation (a
  shallow write for `InPlace`, a deep borrow for `clear`). One Loan then gets two codes at one call, and suppressing one
  afterwards would be a symptom fix.
- **Fix:** generate `err(destroy)` only for the `obs` part, and only where the activation is shallow (`InPlace`).

## Owner decisions

The table at A3:464-478 is complete, provided N1 adds a row (if the plan's form is not taken) and N2's own-Deref rule
is recorded in the plan or S1. The root-most kill stays the owner's optional recovery for 23a and 30d; it is sound with
P3.
