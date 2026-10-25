# N2 SPEC edits: final independent review of the delta 907bd797f..943e1e01e (DECISIONS R4-M131)

Date: 2026-10-25. R4's reviewer slot, new to this unit. I wrote none of the SPEC text, the analysis, the probes or the earlier reviews.
Worktree `bin\worktrees\spec-n2-frev` (detached at 943e1e01e). It was removed after the run, with the junction unlinked first. No
repository edit, commit or push.

The branch merges cleanly into origin/dev `a56a309d0` (`git merge-tree`). R4-M131 appears only on `origin/claude/r4-spec-n2` among
dev and every `origin/claude/*` branch.

Evidence in the R4 notes folder `r4-notes/spec-n2/frev/` (not kept here; its probe sources are copied to `probes/`):
- `src\`: the earlier probes g30–g33 (copied from `draft/Sketches/SPEC N2/probes`), plus mine:
  - `g34-frev.kimi`: a raw pointer argument whose own value carries a Loan that no read through it carries;
  - `g35-exchange-scope.kimi`: `exchange` with no raw layer.
- `verdicts.txt`: A5's Error records under three modes, plus old-path (O) and A5 (M) records.
  - `none` is the head. With the prototype applied and its environment unset, the records equal the clean head's (`out-none`) on
    g30–g34.
  - `AT` and `ATX` are the writer's `prototype-at.diff.txt` with `prototype.diff.txt`'s Formatting hunk.
  - Raw outputs: `out-*\`. Harness: `ScratchSpecN2FrevProbeTest.cs.txt` (the earlier one plus chapters 3, 4 and 22 in
    `ParseBlocks`).
- `out-none\parse-blocks.txt`: the SPEC example parse probe over chapters 3, 4, 5, 15 and 22. It covers 126 blocks.
  - Hits occur only in known blocks: 4.2's intended error, the bodyless signatures of 5.6, 15.7 and 22.9.2, and the 22.3
    declarations.
  - The delta changes no `kimi` block.

## Verdict: ACCEPT WITH FIXES

Two text fixes are blocking: B1 and B2, one phrase each, in SPEC 15.6.4 and in R4-M131's quote of it. Neither needs a redesign.
Otherwise the delta states the decisions exactly:
- The transfer rule is one rule keyed on three closed published sources.
- It agrees with 4.6–4.9, 4.9.6, 15.7, 22.2.3, R′ and the own-value sentence, read as the prototype implements it.
- 3.1.2's sentence agrees with 2.9.1, 11.8, 12.4.2, 22.4, 22.6 and 22.10.
- The records are right.
- Every probe claim I re-ran holds.

The blocking findings come from two readings the letter allows and the prototype does not take. The earlier probes cannot tell those
readings apart.

## Blocking

**B1. The scope of `exchange`'s result clause.**

15.6.4 adds "`exchange`'s result, the old contents, retains only what a chain of reads through its target carries". This is an
independent clause after the semicolon, and it does not repeat "at a raw layer without an Origin".

Read as written, it narrows `exchange`'s whole result for every `T`. That overrides 15.6.4's per-argument rule for safe Types: "one
that may retain any other Origin of an argument carries the argument's Loans", where `T`'s Origin is also `value`'s. It would change
acceptance outside N2.
- `g35` `exchangeRefValue`: `slot = a@ref; old = exchange(slot@uniq, with: b@ref); b = 3; inspect(old)`.
  - A5 on the head errs at `b = 3` (`1 Value result`), as the current letter says.
  - Under ATX it is valid. So is `exchangePairValue` over `(ref/i32, i32)`.
- The prototype's N1 extension is not scoped. `ProtoExchange` clears every non-target input from the result for every `T`.
- R4-M131 says "on the other programs ... AT and ATX agree". That is true, but none of those programs is an `exchange` without a raw
  layer, so this effect went unobserved.
- The decision is scoped to the raw layer. The owner's (a) answered how transfer operations retain at a raw layer without an Origin,
  and N1 extends that rule.

Fix: scope the clause, for example "…; there, `exchange`'s result, the old contents, retains only what a chain of reads through its
target carries". Make the same change in R4-M131's quote. Add to its probe paragraph that ATX applies the extension to the whole
result, so ATX's verdicts without a raw layer are not the rule, and that BU's row scopes it.

If the universal reading was meant, it is a safe-code acceptance change outside N2 and needs its own decision. It would also change a
difference class: the old path already accepts `exchangeRefValue` (verdicts.txt: only `exchangeRefTarget` has an O record), while A5
rejects it.

**B2. A raw pointer argument read as "a borrowed argument".**

15.6.4 says "A raw pointer argument is retained as a borrowed one". The transfer rule retains "a borrowed argument, a Slice or
UniqSlice included" only by its chain, "never by its own Loans".

Read together, an added raw pointer input would keep only what reads through it carry. It would lose its own Loans and addresses in
these cases:
- `Array<raw/i32>.append(p)`, `insert`, `insertOrReplace`;
- the storage `exchange(x@uniq, with: p)` writes.

That contradicts three statements:
- Retained dependencies (5.2.1): "`Array.append` and `insert` … keep the Loans and addresses of a stored value of any Type";
- 4.9.6: "Added and replacing inputs' dependencies join the collection's set";
- 15.6.4's own-value sentence: storage "that can hold a value of its Type".

The prototype takes the other reading. `ProtoBorrowed` counts only borrow Semantics and Slice, and a raw input gets its own value with
Content. In `g34`, `appendRawOwnBad`, `insertRawOwnBad`, `dictInsertRawOwnBad` and `exchangeRawOwnStoreBad`
(`p = r@follow@raw`, which carries `y`'s Loan, while a read through `p` carries nothing) err at `y = 5` on the head, under AT and under
ATX. `appendRawOwnCtl` is clean.

No earlier probe tells the readings apart. Their pointers address `w`, whose Loan the chain carries too (`exchangeStoreBad`,
`appendTupBad2`).

Fix: "a borrowed argument (not a raw pointer argument), a Slice or UniqSlice included", or "an argument of a borrow Type, …". Make
the same change in R4-M131's quote. BU's rows then key on borrow Types and Slice, as the prototype does.

## Non-blocking

**N1. Make the list definitional.** "A published operation that transfers values … : an Iterator's `next`, …" can read as a category
with examples. SPEC defines neither "published operation" nor "transfers values". R4-M131's Open list assumes the closed reading: a
user container's `pop` and `Take.take` conformances keep A's cost. Suggested: "The published operations that transfer values, an
Iterator's `next` (…), `exchange` and `swap` (…) and the operations of 4.6 to 4.9, retain …".

"An Iterator's `next`" covers user conformances through the Contract (22.2.3 already binds them). That is consistent with 22.1's
identity rule.

**N2. The two restatements now say more than 15.6.4.**
- Retained dependencies' parenthetical narrows only 4.9's non-receiver inputs.
- 5.7 Calls does not narrow at all.
- So for a transfer operation's borrowed argument (`swap`'s other target, `appendCopies`'s Slice), both state a broader callee
  permission than 15.6.4's chain-only rule.
- 5.2.1's first sentence defers to 15.6.4, and the bodies are the library's and the intrinsics', so this is not a defect today.
- Suggested: "(narrower for the published transfer operations, §15.6.4)" in place of 5.2.1's parenthetical, and the same pointer in
  5.7 Calls.

**N3.** "Never by its own Loans": the section's term is the own value, "its Loans and the holders it addresses". "Only by …" already
excludes addresses, which a borrow of a raw Place carries (5.2.2). "Never by its own value" would say the same thing in the section's
own words.

**N4.** 5.7's `static` row says "the bytes a string literal spells", without 3.1.2's "outside its interpolations". Align the wording,
or write "a string literal's static bytes".

**N5.** STATUS says "a removed or exchanged raw element". `exchange` is not an element operation; write "a removed raw element or an
exchanged raw pointer".

**N6.** R4-M131 wraps the quote 「(a) 公開した契約で1つの / 規則 (推奨)」 inside the phrase. A renderer inserts a space there, so the
rendered quote is not verbatim. Rewrap at a space, as the other quotes are.

**N7. Probe record (`spec-session-probes.md`).**
- ATX's line should say the extension applies to the whole result for every `T` (B1).
- "A borrowed or Slice input" differs from SPEC's "a Slice or UniqSlice included": the prototype does not narrow a UniqSlice
  argument. No 4.6–4.9 operation takes one by value, so the difference is vacuous.

**N8. BU's third key.** 22.1 lets the Kimi library declare more than the table requires. The key is the declarations implementing
4.6–4.9's operations, not every function of `Array.kimi`, `ArrayOperations.kimi`, `Dictionary.kimi` and `Slice.kimi`, which is how the
prototype keys it. This is a row-design note; R4-M131's "a closed set of declarations that 22.1 requires" already says so.

**N9 (pre-existing, outside the delta).** 4.8.2 says "Slice members … results retain `s.source`", which was already inexact for
`toArray`. Its own sentence ("not the outer borrow") and now the transfer rule settle it.

## What holds

- **Transfer rule (focus 1).**
  - The rule is closed: three keys, each a published source that 22.1 requires.
    - Every `next` of an `Iterator` conformance.
    - `Kimi.Intrinsics.exchange` and `swap`; `replace`'s general-rule rows already match it.
    - The operations of 4.6–4.9.
  - It agrees with 4.9.6:
    - The receiver keeps its chain, so relocating bodies stay within the contract.
    - Callables, key borrows and positions are not retained in element storage.
    - A Slice that is an added input keeps its chain (`appendCopies`).
    - A removed element keeps the stored Types' dependencies through the receiver's chain (`popRawOwnBad` errs at `y = 5` only, and
      `popRawOwnCtl` is clean).
    - `tryInsert`'s Err and `insertOrReplace`'s inputs stay over-approximations that 4.9.6's joined set already contains.
  - It agrees with 15.7: the old value is transferred, and `swap`'s other target keeps the chain, so `swapRawBad` and
    `swapRawOwnBad` err.
  - It agrees with 22.2.3: the item keeps only what reads through the receiver carry, never the receiver's Loan or the Iterator's
    Storage.
  - With R′, the rule is moot behind a result borrow.
  - With the own-value sentence, it narrows the third retainer (the unstated layer) for borrowed arguments, subject to B2.
  - The three keys are enough for BU's rows, given B1, B2 and N8.
- **3.1.2 (focus 2).**
  - "Outside its interpolations" and the 12.4.2 parenthetical agree with 2.9.1, 12.4.2, 22.10.1 ("first builds an owning string")
    and 22.10.3–22.10.4.
  - 11.8: initialized immutable static storage.
  - 22.4: no slot, key, first-access state or shutdown destruction, kept apart by "for the whole execution".
  - 22.6: an import's literal is compile-time.
  - 5.2.1 condition 3 (read-only) and 5.7 (`static` belongs to static storage): consistent.
  - R4-M131's R3 code claim is verified.
    - `BodyLowering.Calls.cs` 223–226 selects `__kimi_format_literal` for a literal argument whose parent is an interpolation. An
      extra capacity-estimate condition there does not bear on the wording.
    - `Utf8FormatRuntime.ll.in`'s `defer:` stores the literal's data pointer into the writer.
- **Records (focus 3).**
  - R4-M131 quotes both new owner answers and the earlier ones verbatim, apart from the N6 rendering issue.
  - Its quotes of the SPEC text match the SPEC.
  - The Rejected list carries (b), (c) and both 4.9.6 edits; the Open list matches the closed reading.
  - Order item 1 defers `Handle.buffer`'s `during self` to the library unit.
  - R4-M116 and BU-30 point to R4-M131.
  - STATUS claims no support. Its claims about the head reproduce:
    - `exchangeRaw`, `popRaw`, `removeRaw2`, `swapRemoveRaw`, `insertOrReplaceRaw` and `dictRemoveKey` err;
    - `toArrayRaw` errs (CallActivationConflict);
    - `swapRawBad` and `swapRawOwnBad` are accepted.
- **Probes (focus 4).** On head, AT and ATX, every verdict that `spec-session-probes.md` and R4-M131 state for g30–g33 reproduces:
  - `twoOut`, `fillBad`, `appendTupBad2` and `swapRawBad` err under AT;
  - `popRawBad` and `toArrayRawBad` err at `y = 5`;
  - `toArrayRaw` and `exchangeValue` are accepted under ATX;
  - `exchangeTargetBad` and `exchangeStoreBad` err at `y = 5` under ATX;
  - the controls are clean.
