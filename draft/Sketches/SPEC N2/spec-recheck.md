# N2 SPEC edits: re-check of the delta 907bd797f..e1904d53f

Date: 2026-10-25. R4's reviewer slot, the same reviewer as `review.md`. The transfer rule's text is my own draft from `review.md` §5,
so this re-check is not independent of it. I re-read the rule against the SPEC sections it now cites and probed it.

Worktree `bin\worktrees\spec-n2-srev2`, detached at e1904d53f. It was removed after the run, with the junction unlinked first.

Evidence in the R4 notes folder `r4-notes\spec-n2\srev\` (not kept here; its probe sources are copied to `probes\`):
- `src-at\`: the writer's g30 and g31 plus my `g32-recheck.kimi`.
- `out3-none\` (branch head) and `out3-AT\` (the writer's `prototype-at.diff.txt` with `prototype.diff.txt`'s Formatting hunk, mode
  AT). Both were run with the harness `ScratchSpecN2SrevProbeTest.cs.txt`, then reverted.

## Verdict: ACCEPT WITH FIXES

R1–R4 are small text fixes and are needed before the merge. R2 and R3 are contradictions under a literal reading. N1–N5 are not
blocking.

What holds:
- **Owner answers.** Both are recorded verbatim, and the transfer rule replaces (i).
- **Earlier findings.** F1–F6 are applied:
  - F2's rewording is right.
  - F3 and F5 are right.
  - The Open list (F1) is accurate apart from N1.
  - The Rejected list (F6) is complete.
  - R4-M116 points to R4-M131, and BU-30 is left for BU.
- **The transfer probe's claims I re-ran.** Under AT the following are valid: `swap` and `exchange` over `raw/u8` and `Box`, `pop`,
  `remove`, `swapRemove`, `insertOrReplace`, and Dictionary `remove` in its result and in storage. Still errors: `fillBad`,
  `appendTupBad2`, `swapRawBad` and `twoOut`. The controls are unchanged. I did not re-run the other programs of the 142.
- **The prototype implements the intended reading, which R1 and R2 ask the text to state.** Every row below is A5's verdict on the
  head and under AT:

| Program | Head | AT | Shows |
| --- | --- | --- | --- |
| `popRawBad`: pop a pointer that addresses `w`, then `y = 5` | err at `a` and at `y = 5` | err at `y = 5` only | a removed value keeps the receiver's chain (R2) |
| `toArrayRaw`: `src.slice().toArray()`, then `src.append(null)` | err (CallActivationConflict) | acc | a Slice argument counts as borrowed (R1) |
| `toArrayRawBad`: the same with an addressed element, `y = 5` | err | err at `y = 5` | the chain is kept |
| `exchangeValue`: `old = exchange(x@uniq, with: q)`, then `x = null; y = 5` | err at both | err at `y = 5` | `old` keeps `value`'s own value (N1) |

## Findings

**R1 (fix before the merge). "A borrowed argument" must count a Slice.**
- A `Slice<T>{source}` is a Copy view with a slot (§4.8.1), not a borrow layer. So read strictly, the rule does not narrow a Slice
  argument.
- Then `toArray` over raw elements keeps its source Loan. §4.8.2 says it "keeps the dependencies inside `T` but not the outer
  borrow", and the head errs on `toArrayRaw` today.
- `appendCopies` keeps the Slice's own Loan through the second sentence ("its added inputs").
- AT treats `BoundTypeKind.Slice` as borrowed, which is the intended reading.
- Write "a borrowed argument, a Slice or UniqSlice included". R′'s "behind a borrow" should say the same; the prototypes already
  treat it so.

**R2 (fix before the merge; the sentence came from my draft). The §4.9 sentence needs a scope and a receiver clause.** The sentence
is "At an element's raw layer without an Origin, an operation of §4.9 retains only its added and replacing inputs". It has two
problems:
- **(a) No scope.** Read at a removed element in a result (`pop`, `remove`, `swapRemove`, `insertOrReplace`'s old value, Dictionary
  `remove`), it retains nothing.
  - That contradicts §4.9.6: "removed values carry the stored Types' full dependencies".
  - It would also lose a popped pointer's address. AT keeps it (`popRawBad` errs at `y = 5`), so the prototype applies the sentence
    to storage only.
- **(b) "Only" drops the receiver's chain.**
  - `sort`, `remove`, `removeAll` and `swapRemove` relocate elements: their library bodies store the receiver's own elements back
    through the raw buffer.
  - By the letter, the contract no longer lets them retain those elements there, so the bodies break Retained dependencies.
  - AT keeps the chain (`0 Content store 0` on `pop`).
- Suggested: "In the element storage it writes, an operation of §4.9 retains at a raw layer without an Origin, of its arguments
  other than the receiver, only its added and replacing inputs (§4.9.6)." Retained dependencies' parenthetical needs the same
  change: "(an operation of §4.9, of its arguments other than the receiver, only its added and replacing inputs)".

**R3 (fix before the merge). §3.1.2 contradicts §12.4.2 for interpolated literals.**
- An interpolated literal is a string literal: §2.9.1, and §22.6 says "non-interpolated string literals".
- §12.4.2 says "An interpolated literal produces an owning `string`". So "a string literal's bytes are immutable static storage …
  not owned by the literal's value" is false for it.
- Write "A non-interpolated string literal's bytes …".
- If the library unit's `pending` can hold an interpolated literal's constant segments (through `$tryWrite`, §22.10.4), the sentence
  needs "the text a string literal spells outside its interpolations" instead. The library unit should check before relying on it.
- Otherwise the sentence is consistent:
  - read-only authority (condition 3, "an immutable static");
  - §11.8's `during static` shared borrows;
  - §15.6 Reach (static storage is storage the body does not own);
  - no Place root or effect-summary key, since those are keyed on static Fields (§15.6.2, §22.4.4);
  - "For the whole execution" keeps §22.4.3's first access and shutdown away from it.
- Its placement in §3.1.2, beside `string`'s storage, is right.

**R4 (fix before the merge; STATUS accuracy).** The new STATUS sentence says "on the MIR a raw Iterator item, a removed or exchanged
raw element and a swapped raw pointer keep the borrowed argument's Loan". That is false for swap on the head:
- `swap<raw/u8>` has only untied stores there.
- `swapRaw` is accepted, and so is `swapRawBad`, a required error the head misses.
- Write: "…a raw Iterator item and a removed or exchanged raw element keep the borrowed argument's Loan, and a swapped raw pointer
  keeps nothing (a swapped address is lost), until …".

**N1 (Open, or extend the rule).** The rule narrows only borrowed arguments, so an owned argument of a transfer operation is still
retained in every unstated output.
- For `exchange`, the result is the old contents (§15.7's table), yet it keeps `value`'s own value: `exchangeValue` errs under AT at
  `y = 5` through `q`'s address. §15.7 promises nothing narrower, so this is no contradiction.
- For `insertOrReplace`'s old value it is harmless: §4.9.6's removed values carry the full dependency set, which the added inputs
  join.
- Record it under Open (an over-approximation inside the transfer family), or add "and `exchange`'s result only what a chain of
  reads through its target carries" if the owner's "one rule" is meant to cover it.

**N2 (nit).** The R4-M116 line that now carries the back-reference is 177 characters; rewrap it.

**N3 (note).** R4-M131's Order item 1 prescribes `Handle.buffer` `during self`. That is the library unit's design to confirm: a
`during self` Field makes `Handle` non-Copy, and changes what reads of it through `layout.0@raw/Handle` give. Unless that was
checked, write "states its Origin".

**N4 (nit).** The probe record should say that AT keys the standard `next` by file (Core.kimi, Iteration.kimi and the collection
files), not by the Iterator conformance, so a user Iterator's `next` is unprobed. The `spec-review.md` copy's "Evidence in this
folder" refers to my notes folder, not to `draft/Sketches`.

**N5 (optional).** §5.7's `raw/T during o` row ("or static storage for `static`") could cite §3.1.2 for literal bytes.
