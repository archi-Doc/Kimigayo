# Closure Calls as Receiver Calls

Status: adopted by the owner (2026-10-21, OQ-C1 (a), OQ-C2 (a); DECISIONS R4-M99); not integrated, not implemented. Date:
2026-10-21. Base: `dev` `217139ae`. Written by the R4 planning session on the owner's instruction (2026-10-21):
「候補1（クロージャを受け手つき呼び出しとして定義）と候補3（派生失敗の定義）を、2c-D・2g-D の前に判断できるよう draft/Proposals に提案書として書いてください。」
Revised after its independent review (`proposals-review.md` in the R4 planning session's notes, round 1: ACCEPT WITH FIXES), which
proposed the narrower formulation used here. To be decided before 2c-D, A5-F7's design
([A5 step 2 plan](2026-10-18%20A5%20Step%202%20Implementation%20Plan.md) §4.5). "(H)" marks a claim no run has checked.

## 1. Problem

The SPEC states a concrete Closure's call twice, in two vocabularies.

- **As a receiver call.** [§7.7.3](../../docs/spec/07-functions-and-callable-values.md#773-call-receiver) gives each concrete
  Closure a Call Receiver Requirement whose receiver is `ref/Self`, `uniq/Self` or `owner/Self`, one per admitted case, and "a
  direct call acquires the receiver like a method receiver". [§15.6.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations)
  says "a Closure's environment bindings are Fields of its call receiver", and
  [§15.6.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting) already contains a child
  in "a Closure's call receiver" like any receiver.
- **As a closure rule.** [§15.8.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1582-closure-dependencies) gives the
  call's results by its own five-row table (result source → contract), and §15.6.5 lists a Closure body's fixed Origins
  separately ("the Origins of its captures, of its call receiver and of a whole result inferred from the body").

Every row of the table is what §15.6.3 and §15.6.5 already give for a body whose receiver's referent holds the environment
bindings as Fields (§3). One concept has two canonical forms (Principle 1), and each implementation layer builds the closure form
beside the receiver form it already has:

| Layer | Closure-specific construction today or planned |
| --- | --- |
| Binding | `CallLocalOrigin`, `CallResultOrigin`, `ResultFixedForCall`'s environment scan; R1's value-call boundary makes results over the environment `Unsupported_Kd` (STATUS "Closures and Function values"; PLAN §3, R1) |
| S3 plan | [S3 Closure Origin Publication](2026-10-20%20S3%20Closure%20Origin%20Publication%20Plan.md) §4.2 steps 2 and 4 restate the five rows as a formation algorithm |
| C1-F4 view | [2a0-D](../Sketches/A5%20Step%202/2a0-D-package.md) §3 row 4: the Origin of a Borrow of an environment binding is an interim Finite class "until 2c-6 classes it per receiver case" |
| A5-F7 (planned) | 2c-2 a closure retention view `TyCtxt.Closure`; 2c-5 "environment Origins replaced by the receiver's Origin at the call"; 2c-6 closure result edges |
| MIR builder | the closure receiver Type comes from `BoundClosure.Receiver`, and "a borrowed receiver has no explicit receiver Origin" (S3 plan §2) |

## 2. Proposal

One sentence in §15.8.2, before the table:

> A Closure body is checked against its call receiver ([§7.7.3](../../docs/spec/07-functions-and-callable-values.md#773-call-receiver))
> as an instance function's body is against its receiver: its environment bindings are Fields of the receiver's referent
> ([§15.6.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations)), so its result Origins,
> containment ([§15.6.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)) and
> retention ([§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls)) follow the receiver rules. The table
> restates them.

The table becomes informative. Nothing else changes in meaning:

- **§7.7.2 and §7.7.3 stay as written.** The per-call permissions (each environment binding acts as a local of the call with its
  capture's `let`/`var` mutability; only a Consuming call moves a capture out; a consumed `let` capture cannot be reinitialized)
  and the per-case minimum receiver inferred from the body are closure rules, not instance-function rules: no declared instance
  function has a per-case receiver ([§7.3.1](../../docs/spec/07-functions-and-callable-values.md#731-instance-functions-and-receiver-types),
  [§7.3.2](../../docs/spec/07-functions-and-callable-values.md#732-receiver-shape)), and an environment binding is not an
  owned-Type parameter ([§15.1.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)).
  The sentence borrows only the receiver's Origin rules.
- **The Closure Type stays as [§3.2.1](../../docs/spec/03-types-and-values.md#321-callable-value-types) defines it**, with its
  captures and its fixed signature Origins (both in OwnedOrigins, [§15.2.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1523-static-and-owned));
  no new Type category.
- **Result inference.** When the result Type is omitted, each Origin in the inferred result is a per-call Origin of the call
  contract, outlived by every source of the returned values (§15.8.2's second paragraph; "per the table" becomes "by the receiver
  rules").
- **Annotated results.** With an annotated result or a fixed expected call signature the receiver is hidden: no Origin expression
  names it and result elision does not take it (§15.8.2's third paragraph, kept), because common Function Types and Callable
  Constraints cannot depend on it (§3.2.1, [§8.10](../../docs/spec/08-generics-constraints-and-contracts.md#810-callable-constraints)).
  `{environment}` and receiver-dependent public results stay deferred
  ([Appendix D.2](../../docs/spec/appendices/D-deferred-features.md#d2-declarations-functions-and-generics)).

The sentence is meant to change no verdict (H, §8).

## 3. Derivation of the current rules

`self` is the call receiver and `c` an environment binding of Type `C`, a Field of the receiver's referent. "Contained" is
§15.6.3's sentence: a child is contained in the Origin of the parent and of each safe borrow through which the parent is reached,
up to and including the first shared layer.

| §15.8.2 row | The same source by the receiver rules | Result |
| --- | --- | --- |
| Borrow of environment-owned data | a Borrow of `c` through `ref/Self` or `uniq/Self` is reached through the receiver's borrow, so it is contained in the receiver Origin | depends on the receiver |
| Captured shared reference, or a shared Borrow through it (`C` = `ref/T during o`) | reading `c` copies `ref/T during o`; a shared Borrow through it meets `c`'s own shared layer first, so it is contained in `o` only | keeps `o` |
| Reborrow of a captured exclusive reference (`C` = `uniq/T during o`), Shared or Exclusive call | through `ref/Self`: contained in `o` and in the receiver, the first shared layer, included; through `uniq/Self`: both layers are exclusive, so in both | keeps `o` and depends on the receiver |
| The same, Consuming call | under `owner/Self` the binding is a local of the call (§7.7.2; body-local, §15.6.5): a Reborrow through it is contained in `o` only, and the end of the binding's storage ends the child while its parent Loans stay (§15.6.3, "a Reborrow of a parameter may be returned") | keeps `o` and its Loans |
| Reference moved out by a Consuming call | moving `c` transfers `C` with `o` | transfers `o` and its capability |
| Borrowed argument | an ordinary parameter | its parameter contract |
| "Any other source, such as a Borrow of call-local storage or of an environment consumed by the call, is an error" | a Borrow anchored in a local of the body, or in a binding of a consumed environment (body-local, §15.6.5), relates a finite Origin to the fixed result Origin | Refuted |

The rest follows the same way:

- **§15.6.5's fixed list.** The captures' Origins are Origins of the receiver's Type, "a projection of" the receiver; the receiver
  Origin is a receiver Origin; an inferred whole result's Origin is the call contract's result Origin. The general rule fixes each,
  so the closure sentence becomes redundant.
- **§15.6.5's body-local sentence.** Through a borrowed receiver, a Borrow of a binding is reached through a borrow and so is an
  inferred region contained in the receiver; in a Consuming call it is anchored in a local of the call and body-local.
- **§7.7.4's erasure condition** "its result does not borrow the environment" is the same condition as "its result contract does
  not depend on its receiver".

## 4. Options

| Option | Content | Assessment |
| --- | --- | --- |
| **A (recommended)** | §2's sentence; the table informative | One form for results, containment and retention; no verdict change (H); A5-F7, S3's C2 and the C1-F4 view reuse the receiver rules (§6) |
| B | A, and an annotated result's elision also takes the borrowed receiver, as a method's does ([§15.4.3](../../docs/spec/15-ownership-and-lifetime-analysis.md#1543-result-defaults) rule 1) | A closure with a borrowed parameter and an elided borrowed result, such as `func (y: ref/i32) -> ref/i32 => y`, would get `during (y and receiver)` and then fit no Callable Constraint or common Function Type. About 13 closures in `tests/xUnitTest/Tests` have that shape (the other matches of the grep write their result Origin, which elision does not touch); the library and the milestones have none. B would also make §15.8.2's `invalid` example valid |
| C | A5 plan §5.2's optional candidate: Closure results never depend on the call receiver | Narrows the SPEC for implementation reasons (lib 0, ms 0, about 15 xUnit forms, §15.8.2's own `get` example). Under A the dependence costs nothing closure-specific |
| D | No change: 2c-D implements the table directly | Keeps two forms and the closure-specific constructions of §1 |

## 5. SPEC text (planned mapping)

| Section | Change |
| --- | --- |
| §15.8.2 | Add §2's sentence before the table; introduce the table as a restatement (informative); "per the table" in the second paragraph → "by the receiver rules"; keep the third paragraph and add its reason (§2) |
| §15.6.5 | Delete "In a Closure body, the Origins of its captures, of its call receiver and of a whole result inferred from the body ([§15.8.2]) are fixed too." (covered by "a parameter or receiver Origin, a projection of one" and the call contract's result Origin); keep the body-local sentence, now a consequence |
| §7.7.4 | "its result does not borrow the environment" → "its result contract does not depend on its receiver" |
| §7.7.2, §7.7.3, §15.6.3, §3.2.1 | Unchanged |

No example changes (H). The examples of §7.7 and §15.8.2 stay compiled tests (`VerifiedSpecExampleTest`).

## 6. Consequences for the implementation (H)

- **A5-F7 (2c-D, 2c-2 … 2c-6).** The closure contract's Origin part is the receiver rule per admitted case. 2c-2's retention (the
  store output into the receiver: Exclusive, the environment and captured exclusive referents; Consuming, captured referents;
  Shared, none) is what retention through `uniq/Self`, `owner/Self` and `ref/Self` gives when the referent's Fields are the
  environment bindings. 2c-5 substitutes the contract's receiver Origin by the call's receiver borrow, as any instance call does.
  2c-6 is ordinary result edges. Receiver selection stays closure-specific (§2).
- **C1-F4 view (2a0-D §3).** Row 4's atom is the Origin of a Borrow of an environment binding. Its interim Finite class goes: under
  a borrowed receiver the Borrow is Inferred, contained in the receiver by D14's walk (the receiver's Origin is Fixed by row 5's
  ClosureValue role); under `owner/Self` it is anchored in a local of the call (row 3, Finite). Capture Origins stay row 6
  (Fixed). This is a 2c-D input, not a change to the approved 2a0-2.
- **S3 plan.** C2 steps 2 and 4 classify each source by §15.6.3's containment walk, one rule, instead of the five rows. Its Gate R
  evidence and the rest of the plan are unaffected.
- **Binding.** At 2c-5 the receiver substitution replaces `CallResultOrigin`, `ResultFixedForCall`'s environment scan and the
  receiver-dependent `Unsupported_Kd`; the deletions are counted in that unit.
- **MIR builder.** The closure receiver becomes an input with its Origin, the gap S3 §2 records.
- **R5.** Closure code generation already passes the environment as the receiver (H); R5's planning session is asked to confirm.
- **Default pipeline.** No change before R6-check (D5).

## 7. Principles

1. **One concept, one canonical form.** Results, containment and retention of a closure call use the receiver rules; the table
   survives as a restatement. Receiver selection (per case, from the body) stays the closure's own rule.
2. **Local reasoning.** A closure body's Origins read like a method body's: its environment is its receiver's referent.
3. **Explicit semantics.** The receiver mode is §7.7.3's requirement; the sentence adds no inferred contract.
4. **Compiler Server Protocol.** Diagnostics on closure results name the receiver with the vocabulary methods use.

## 8. Verification before integration

1. **Derivation round.** 2x-P7 (A5-F7's prototype, 2c-D's prerequisite) adds one column: each of its cases f7a–f7n and every
   example of §7.7 and §15.8.2, plus the review's Consuming forms (a `var` capture reinitialized after a Move in a Consuming body,
   `count += 1` there, a nested `[count@uniq]` inside a Consuming outer call) and a no-capture closure with a fixed signature Origin
   passed where `T is Owned` is required, judged under §2, must equal today's verdicts. A difference stops the proposal and
   returns to the owner with the case.
2. **Census.** Closures in the library (no capture lists at the base), the 44 milestones (16 capture lists in six of them) and the
   closure test classes: no verdict changes (H).
3. **SPEC integration** in one SPEC session after step 1, before 2c-D's design review.

## 9. Owner questions (answered 2026-10-21, DECISIONS R4-M99)

| ID | Question | Options | Recommendation | Answer |
| --- | --- | --- | --- | --- |
| OQ-C1 | Adopt the receiver formulation? | (a) A, as 2c-D's basis, with the SPEC text integrated after the derivation round (§8); (b) A for 2c-D only, SPEC unchanged; (c) D; (d) C | (a) | (a) |
| OQ-C2 | The receiver in an annotated result's elision (option B)? | (a) reject, keep `{environment}` in Appendix D.2; (b) keep open as a later Appendix D item | (a) | (a) |

The owner answered on the round-1 text, whose definition made the Closure Type a "structural Type" with the call as an instance
function. The review showed two verdict changes in it (Consuming `var` bindings, a no-capture closure's fixed Origins); the
narrower sentence of §2 keeps option A's intent (one form, no verdict change) without them.

## 10. Unverified

- The derivation is by reading; 2x-P7's column checks it (§8).
- Per-case receivers with pair captures (§7.7.3's conditional plans).
- The xUnit form count of option C (A5 plan §5.2's figure) and the closure census.
- R5's code generation of the receiver.
