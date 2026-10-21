# Lifecycle Receivers

Status: 取り込み済み (integrated 2026-10-22): the owner chose option B (OQ-L1 (b); DECISIONS R4-M103); §2's root is in SPEC §15.6.5.
Not implemented. Date: 2026-10-22. Base: `dev` `9031f7bc`. Written by
the R4 planning session on the owner's instruction (2026-10-21): 「この勢いで、先ほどの新しく挙げる候補 2、4、5、6の提案書も書いてください。」
(candidate 5). BU's planning session checked it against G166, G163, G165 and G178 (2026-10-22). Its independent review
(`../Sketches/R4 Proposals 2026-10-21/review-3.md`: ACCEPT WITH FIXES) found that the first draft's unified definition dropped and
added rules; the fixes are applied, and the recommendation is now the review's shorter option B (§3). No unit waits for it; it is
best decided before BU's constructor grant (PLAN G166) and A5's G161 tests (2a-1a). "(H)" marks a claim no run has checked.

## 1. Problem

A constructor's `self` and `drop`'s `self` are one kind of receiver: a value under construction or under destruction, reached
exclusively, only inside its body. The SPEC states them in five places, each partly by reference to another:

| Place | What it says |
| --- | --- |
| [§6.3.4](../../docs/spec/06-declarations-and-containers.md#634-the-construction-receiver) | Constructor `self` is an exclusively writable root for its initialized own Fields, as a `uniq/Self` receiver is (including the implicit acquisition of a Receiver Expression, §7.3); no Non-Copy value Moved out; no computed or inherited Field access, whole-`self` acquisition or borrow, method or custom-accessor call on `self`, capture or exposure; Field borrows end before completion; the privileges exist only in the constructor body itself, not in nested functions, methods or Field initializers |
| [§16.3.1](../../docs/spec/16-scope-exit-and-destruction.md#1631-destruction-receiver) | `drop`'s `self` has exclusive access equivalent to `uniq/Self`, "like the construction receiver"; it is a destruction receiver, not an ordinary borrow value or an owner, not Copied or Moved; no whole-receiver assignment or `uniq/Self` passing; initialized fields follow ordinary permissions, none Moved out; `self@ref` is a `ref/Self` whose Origin ends within the body, passed to helpers but not captured, stored or returned; no handle or object view; no escape; during the destruction of layer `D`, `Self` is `D` and derived layers already cleaned up are unavailable |
| [§7.7.2](../../docs/spec/07-functions-and-callable-values.md#772-captures) | "In a constructor or `drop`, no entry captures `self`" |
| [§5.7](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins) | On a raw layer, `self` "in a constructor … is the value being constructed and in `drop` the destruction receiver, both exclusive" |
| [§15.6.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations) | Lists body-local Borrows (anchored in a local, parameter or temporary root; implicit and capture-entry Borrows) but neither receiver |

What the implementation shows:

- **Atoms.** PLAN G178 (found with G163's probes) is a constructor receiver's Place atom equal to its first parameter's
  (`init(x: i32)`: `self.n@ref` and `x@ref` are both `Projection[init#0]`). BU's G160 C0c gives both receivers one reserved slot
  (`ReceiverSlot`, beside `HelperSlot`; branch `claude/bu20-g160-raw`, DECISIONS BU-23 when it lands, H until then). 2a0-D §3 row 3
  classes the destruction receiver's part and helper borrow Finite by reading §16.3.1; the construction receiver's part is left to
  G166.
- **Open issues.** G161 (A5 accepts a borrow of one Field of the destruction receiver stored into another), G166 (the constructor
  grant: §6.3.4's Field Loans and "end before completion") and G165 (STATUS "Destruction receiver borrows": in `drop` or a
  constructor the old path accepts a write while a Field borrow lives) each need the receivers' Borrows classed; G163 (no Closure
  in a drop body captures the helper borrow) rests on §16.3.1's helper-borrow rows.

The one fact these consumers need and no sentence states is in §15.6.5: a Borrow of either receiver, or of its parts, is
body-local.

## 2. Proposal (option B)

One root in §15.6.5's body-local list:

> A **body-local Origin** belongs to a Borrow anchored in a local, parameter or temporary root of the body, **or in the receiver of
> a constructor or `drop`**, reached by Field, element or owning-layer projections, never through a borrow or raw pointer; its bound
> is the body.

§6.3.4 and §16.3.1 stay as written; §7.7.2 and §5.7 already cite both. The bound is the body for both receivers: §6.3.4's "Field
borrows end before completion" stays a Loan rule (completion conflicts with every live Loan of a Field of `self`), as §15.6.5's "a
use beyond a local Place's lifetime is a Loan conflict at its destruction or Move" is for locals, not a second kind of bound.

What it decides: G161's stored Field borrow is a finite Origin carried to a fixed one (Refuted), the construction receiver's part
joins 2a0-D row 3, and G166's constructor grant and G165 read one classification. What it does not change: every verdict (H, §6).

## 3. Options

| Option | Content | Assessment |
| --- | --- | --- |
| **B (recommended)** | §2: one root in §15.6.5 | Gives G161, G165, G166 and 2a0-D row 3 the fact they need; restates nothing, so no rule can drift |
| A | One definition of "lifecycle receivers" in §6.3.4 with a two-column table, cited by §16.3.1, §7.7.2, §5.7 and §15.6.5 (corrected as below) | One concept by name; editorial, and every sentence of §6.3.4 and §16.3.1 must move without loss. The first draft lost three (the "only in the constructor body itself" scope, "not an ordinary borrow value or an owner", the derived-layer rule) and added a bound; a corrected A keeps the shared part to exclusive `uniq/Self`-equivalent access, no Move out of Fields, no Copy or Move as a whole, no capture, storage, return, handle, view or escape, and §2's root; it keeps in the construction column the own-Field restriction, the computed and inherited Field exclusion, the Receiver Expression acquisition and the scope sentence, and in the destruction column "derived layers already cleaned up are unavailable", "not an ordinary borrow value or an owner" and the helper borrow's rows as §16.3.1 words them |
| C | No change | The classification stays a reading of §6.3.4 and §16.3.1 |

## 4. SPEC text (planned mapping, B)

| Section | Change |
| --- | --- |
| §15.6.5 | The body-local bullet gains "or in the receiver of a constructor or `drop`" |
| §6.3.4, §16.3.1, §7.7.2, §5.7 | Unchanged |

No example changes (H).

## 5. Consequences for the implementation (H)

- **Binding (BU).** C0c's `ReceiverSlot` is the atom of both receivers' parts (the helper borrow keeps `HelperSlot`); G166's
  constructor grant classes them by §2's root.
- **A5 (R4).** 2a0-D §3 row 3 lists the receiver's part of a constructor or `drop` and the destruction receiver's helper borrow,
  Finite; G161's 2a-1a tests and G166's A5 part judge both receivers by one row.
- **R5.** No change.

## 6. Principles

1. **One concept, one canonical form.** The receivers' Borrows are classed where every body-local Borrow is.
2. **Local reasoning.** A Borrow's bound for these receivers is in §15.6.5, where Borrows are judged.
3. **Explicit semantics.** The fact the consumers read is stated instead of inferred from §6.3.4 and §16.3.1.
4. **Compiler Server Protocol.** No new diagnostic requirement or vocabulary entry.

## 7. Owner question

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-L1 | State the receivers' Borrows as body-local? | (b) B, one root in §15.6.5; (a) A, the unified definition as corrected in §3; (c) C | (b) |

## 8. Unverified

- That no program changes verdict under §2's root: §6.3.4's and §16.3.1's examples and the G161, G163, G165 and G166 regressions
  are the check (H).
- C0c's slot and its DECISIONS number until BU's merge.
