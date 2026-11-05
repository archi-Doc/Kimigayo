# N2 SPEC edits: independent review (claude/r4-spec-n2 907bd797f, DECISIONS R4-M131)

Date: 2026-10-25. R4's reviewer slot. I wrote none of the SPEC text, the analysis or the probes. Worktree
`bin\worktrees\spec-n2-srev` (detached at 907bd797f, removed after the run, junction unlinked first). The branch merges cleanly into
origin/dev `1dd0f967f` (`git merge-tree`). R4-M131 is unique on dev and on every `origin/claude/*` branch.

Evidence in the R4 notes folder `r4-notes\spec-n2\srev\` (not kept here; its probe sources are copied to `probes\`):
- `out-none\parse-blocks.txt`: the SPEC example parse probe over every `kimi` block of chapters 5 and 15.
- `src\g30-transfer.kimi`: my probes. `out2-none\` is the branch head (dev `ac14e8fde` + docs); `out2-AR2\` is the same build with
  the reviewer's throwaway prototype (`draft/Sketches/SPEC N2/prototype.diff.txt`, mode AR2: A2 + R′ + "every other argument").
  The prototype has no Iterator clause and no Content closure.
- `out-old\old-path.txt`: old-path (O) and A5 (M) Error records for the reviewer's depth groups g13/g15, g20 and g30, plus a
  control. The harness is `ScratchSpecN2SrevProbeTest.cs.txt` (the earlier reviewer's, plus `ParseBlocks` and `OldPath`). It
  was never committed, and the prototype was reverted.

## Verdict: ACCEPT WITH FIXES (records only), and one owner question to settle before BU's N2 rows

The SPEC text states each decision exactly, no more and no less. It introduces no contradiction beyond the one the owner's answers
imply and the commit records (open point 1). The examples parse and mean what they say. The difference classes are right, and no
frozen text changed.

Two things need action:
- F1: the commit's Open list understates one class of consequences. Fix it before or at the merge (records only).
- The owner question (§5): open points (1) and (2) are members of a larger family, and the Iterator clause is one fix inside it.
  SPEC's own `Kimi.Intrinsics.swap` now makes two raw pointers unusable together after a swap. Several §4.9 operations also contradict
  §4.9.6's "complete published effect". The question must be answered before BU's rows land. It need not hold this merge.

## 1. The decisions against the text

| Decision | Where | Stated |
| --- | --- | --- |
| Q1 A: tracked at every written raw position without an Origin | 15.6.4 raw-layer sentence; 5.7 Calls; 5.2.1 Retained dependencies | exactly |
| Q2 A2: nominal result Fields included | 15.6.4 "a nominal Type's Fields included"; 5.7's nominal-Field sentence deleted; condition 5's hidden-Field example replaced | exactly |
| Q3: condition 5 covers a call the pointer is passed to | 5.2.1 condition 5, "by the body or by a call to which the body passes the pointer, directly or within a value or storage" | exactly |
| Q4: transitive depth | 15.6.4 "any chain of reads" plus its definition sentence | exactly |
| Q5 R′, with the ruling that it reaches no storage the body does not own | 15.6.4 "lent, not written: ... raw arguments ... reaches no storage the body does not own"; 5.7 "behind a borrow in the result, only ... a raw argument" | exactly |
| (i): an Iterator's `next` never retains its receiver's own Loans | 15.6.4, citing 22.2.3 | exactly |
| §4.9.6 (a): §4.9.6 unchanged; the library states its Origins | §4.9.6 untouched; Retained dependencies' "applies only where no Origin is stated" | exactly (wording: F2) |
| The written-through argument: only its own value is excluded | 15.6.4 "not the own value (its Loans and the holders it addresses) of the argument through which the call writes it"; 5.7 "(through the one it writes through, only by a read through it)" | exactly (nit: F5) |

The own-value sentence gains "or by a raw layer without an Origin, below". It stays consistent with "a pointer is never part of its
own pointee", which the narrower exclusion generalizes to borrowed written-through arguments.

## 2. Consistency with the rest of SPEC and LIBRARY

- **§15.6 Reach** gains the written unstated layer. A lent layer is "not written", so it adds nothing, as R′ says. The old part of the
  list ("a part of a call result that its contract ties to no argument") does not conflict: a referent is not a part.
- **§5.7.** The table row (`raw/T`: "at a call what §15.6.4 gives") and Fits are unchanged and still hold. The Calls bullet now
  states results and storage once.
- **§5.2.1.** Condition 5's new example, a pointer "a call stored in static storage", matches §15.6 (static storage is storage the
  body does not own). Retained dependencies is now consistent with §5.6's `Buffer<T>.push` for `T = (raw/u8, i32)`: `push`'s
  contract (an unstated element layer written through `self`) lets the call retain `value` where the body stores it. Under the old
  letter it did not.
- **§15.6.3 and §15.6.5** (per-argument retention of items split from an Iterator; a retained argument counts as a Copy) are
  unchanged and consistent.
- **§22.2.3, LIBRARY §2.1 and §3.3 indexing ("the key borrow is not a result dependency").** Iteration is consistent through R′ and the
  Iterator clause. Indexing is consistent through R′. The clause also protects 22.2.3's `nextPair` example for every instantiation.
- **§8.1.1, §3.5, §16, IMPL and the appendices.** A pair-supplied raw layer has no Origin, so the rule applies to it (BU-34). Nothing
  else is stale. A grep for "outside a nominal Type's Fields", "hidden in a Field", "Fields alone" and "a raw one with what a read"
  finds no remaining copy outside chapters 5 and 15. IMPL states no retention.
- **§4.9.6 and §15.7.** Contradiction and cost; see §4 and §5.

## 3. Examples, rows and classes

- **Parse.** The three new blocks (chapter 5, lines 261, 284 and 295) parse with no hits. The only hits in chapters 5 and 15 are the
  known bodyless signatures (§5.6 `Raw`, §15.7 `Intrinsics`).
- **`fill`/`fillFrom`.**
  - After `fill(x@ref, y@ref, s@uniq)`, `s` retains `r` and `k`, so the commented `y = 3` is the stated error. `usep(s)` now comes
    before the second half, so `y = 4` is valid: `t`'s layer states `r` and retains `r` alone.
  - The `during r` local passes `fillFrom`'s invariant referent. Both paths bind the block as written (the writer's
    VerifiedSpecExampleTest runs).
- **`pairs`.** `q` addresses `w`, which carries `y`'s Loan. The literal's insertion retains the element's own value, so `pairs`
  reaches `w` and `y = 5` is the stated error.
- **Items block.** Behind the borrowing item, R′ retains no raw argument. The owning item retains only the chain through the
  receiver (the Iterator clause). Both comments are right; F3 is a wording point in the intro sentence.
- **Rows.**
  - `5.7-out-parameter/1` and `5.7-raw-pairs/1` are (a) on the old path. The old analysis keeps no raw retention or address (STATUS
    Loans row), so its miss goes with it at R6-check.
  - On the MIR they are (b): Binding's rows are missing, and BU fixes them.
  - Family F1 matches `5.2.1-raw-place`.
  - The items block cannot have a row yet: an A5 conflict in an accepted variant fails
    `MirCensusTest.AcceptedSpecVariantsHaveNoProblems` (checked in the test source). R4-M131 records that the row lands with BU's
    rows.
- **STATUS.**
  - "The N2 analysis's 22 forms and its two-read forms are accepted on both pipelines" is now verified. The 22 were checked earlier
    by CLI. The two-read forms (`getTRRepl`, `getRRRRepl`, `getRFRRepl`, `fTRRepl`, `fRRRRepl`, `fRR2Repl`) had never been run on
    the old path; `out-old\old-path.txt` shows it accepts them (control `controlBad` errs). A5 also accepts the Repl forms.
  - "A raw Iterator item keeps its receiver's Loan on the MIR" is true (`ownRaw`, `iterRaw`).
  - No support is claimed.
- **AGENTS Documentation.** R4-M131 states the rationale and how the Principles hold. SPECJ, GUIDE and PLAN are untouched. The only
  draft change is new files under `draft/Sketches/SPEC N2/`; no Proposals or Changes text changed.

## 4. Findings

**F1 (records; fix before or at the merge). R4-M131's Open list understates the class.** Open says only that `sort(by:)`,
`removeAll` and `appendCopies` retain their Callable or Slice argument, and that `remove`/`swapRemove` keep the receiver "(D6)".

The same cause gives the following. The verdicts are A5's, from `out2-none` (head) and `out2-AR2` (the decided rule's prototype):

| Program (all valid by intent) | Head | AR2 | What retains what |
| --- | --- | --- | --- |
| `swapRaw`: `Kimi.Intrinsics.swap(x@uniq, y@uniq); usep(x); usep(y)` over `raw/u8` | acc | **err** at both uses | `*first` retains `second`'s own (exclusive) Loan, and vice versa |
| `swapBox`: the same over `Box { p: raw/u8 }` | acc | **err** | as above, through A2 |
| `twoOut`: a user `split(src: raw/u8, a: uniq/(raw/u8), b: uniq/(raw/u8))`, then both used | acc | **err** at both uses | each out-parameter keeps the other's exclusive Loan; no Origin can be stated for an unstated `src`, so there is no repair |
| `exchangeRaw`: `let old = exchange(x@uniq, with: null); usep(x); usep(old)` | err | err | the result retains the target's own Loan (D6, pre-existing) |
| `exchangeBox` | acc | **err** | as above, widened by A2 |
| `popRaw`: `let x = a.pop(); a.append(null); inspect(x)` | err | err | D6, as `remove` |
| `removeBox` | acc | **err** | widened by A2 (also in the writer's own table) |
| `dictRemoveKey`: `let e = d.remove(k); k = 2; inspect(e)` over `Dictionary<i32, raw/u8>` | err | err | the result keeps the key borrow's Loan (D6) |
| `dictRemoveStore`: `_ = d.remove(k); k = 2; inspect(d)` | acc | **err** | the Dictionary's element layers keep the key borrow's Loan (new storage rule) |
| `swapI32`, `swapRef`, `dictRemoveStoreI32` (controls) | acc | acc | — |

Also by the same rows, though not probed: `tryInsert`'s `Err` and `insertOrReplace`'s old value keep the receiver Loan. Indexed
replacement of a raw `V` keeps the key borrow in the Dictionary. `ownBox` and `forMoveBox` err under AR2; the Iterator clause
answers them in SPEC, which no prototype has.

Fix: list the class as one Open item, as members of one cause:
- SPEC's own transfer operations (§15.7 `swap`/`exchange`, §4.9's removals and rejections, Dictionary key borrows in results and in
  storage, and the mutators of open point 1);
- user functions with two exclusive out-parameters;
- A2's widening of (2) to nominal element Types. "(D6)" is accurate only for raw, Tuple and Option elements.

The owner question in §5 is the decision.

**F2 (wording, non-blocking). Retained dependencies' last sentence.** "storage that a raw layer addresses `during self` or `during o`
retains only what that Origin admits" speaks of the pointee storage of a stated layer. Read alone, it exempts the contents of
during-self storage from the unstated-element rule that the same paragraph applies to `Array.append` and to §5.6's `Buffer.push`,
whose buffers are during-self storage. It is consistent only through a recursive reading of "admits" (`Array<(raw/u8, i32)>` admits
an unstated element layer, which then follows the rule). The intended statement is about the layer the call writes. Suggested:
"This applies only to a layer that states no Origin: a layer stating `during self` or `during o` retains as that Origin's borrow
would (§5.7), and the unstated layers in the storage it addresses follow this rule."

**F3 (wording, non-blocking).** "Neither a lent item nor an Iterator's item keeps its receiver's Loan" suggests the borrowing
iterator's item is not an Iterator's. `ArrayIterator` is an Iterator, and the clause covers it too. Suggested: "Neither an item
lent behind a borrow nor an item an owning Iterator moves out keeps its receiver's Loan".

**F4 (records, non-blocking).** AGENTS: "a superseded entry stays and names its replacement". R4-M116's R-N2 observation and BU-30's
holds behind a borrow (and R4-M117 item 5's "conservative reading" sentence inside BU-30, if that is the text meant) do not name
R4-M131. Add "(superseded at unstated positions by R4-M131)" back-references, with BU's agreement for BU-30.

**F5 (nit).** 5.7 Calls' "(through the one it writes through, only by a read through it)" reads as one read, where 15.6.4 keeps the
whole chain. Write "only by reads through it".

**F6 (nit).** R4-M131's Rejected list:
- It omits options §4's "unstated layers in signatures as elided Origins".
- "for the cost class, a §4.9.6 edit" does not say which of design-spec S2's two alternatives was offered and rejected: §4.9.6
  deciding collection operations, or §4.9.6 admitting the cost. That matters for §5's options.

**F7 (planning, non-blocking).** PLAN G160 should carry the items block's spec-cases row and the coming MIR-only ledger rows with BU's
unit (the planning session's PLAN).

## 5. Open points and the owner question

**(1) The cost class over raw element Types.**
- It is a SPEC contradiction. For `Array<raw/u8>.sort(by:)` with a closure capturing `bias@ref` (`costSortBy`), 15.6.4 makes `bias`
  retained in the elements. §4.9.6 says only "added and replacing inputs' dependencies join the collection's set", and calls its row
  "the operation's complete published effect. Callers use it, never a library body".
- This edit introduces the storage half. On dev, unstated element layers in storage retained only a raw argument's own value of
  agreeing Type. A2 widens the result half to nominal element Types.
- The owner's (a) does not resolve it. Its premise, that library-stated Origins narrow the rule, reaches only the library's own
  Fields. It cannot reach the element Type's layers, and those are exactly the programs S2 cited: the probes are over `Array<raw/u8>`
  and `Array<(raw/u8, i32)>`.
- So an owner decision is needed before BU's N2 rows land. Otherwise those rows would report Loan conflicts on programs §4.9.6 calls
  valid. That is a conservative Language error on a valid form (AGENTS, Diagnostics), or ledger rows of class (c), a SPEC ambiguity.
- It need not hold this merge: no implementation reads the text before BU's rows, the library unit that comes next does not depend
  on it, and every option adds text.

**(2) `remove`/`swapRemove` keeping the receiver.**
- Same cause as the Iterator clause. An unstated result layer retains a borrowed argument's own Loans, where a published contract
  says the result is transferred out:
  - §4.9.1 "Removal transfers the stored responsibility"; §4.9.6 "removed values carry the stored Types' full dependencies";
  - §15.7's table ("Transferred", "Exchanged");
  - §22.2.3.
- The family is F1's table. A requirement whose result is an associated Type, such as §8.11.1's `Take.take`, is the same shape:
  through the requirement its result keeps only reads, while a concrete conformance whose Item has an unstated layer keeps the
  receiver's Loans. That is pre-existing, reasoned and not probed.
- Per AGENTS ("a second fix for a related symptom means the cause was missed"), the Iterator sentence should become one rule for the
  family rather than gain siblings one by one.

**(3) Static storage and integer round trips.** Acceptable as recorded. Condition 5 now names the static case explicitly, so a later
access is undefined, not silently accepted. Tracking a call's static writes would need effect summaries to carry retention. That
belongs with P42's static protocol; record it as an open issue there or under G160.

**(4) A direct closure call writing its environment.** Acceptable as recorded. It is an implementation gap, not SPEC: §15.8.2 makes
environment bindings Fields of the receiver's referent. Under the new text, those writes are storage written through the receiver,
so `keep(q)` with `[s@uniq]` retains `q` once Binding reads the environment as an output. PLAN open issue (BU and R4).

**(5) `dictRawOk`.** Acceptable as an A5 open issue (a false error on dev today, shadow). Triage it before BU's rows: those rows add
Dictionary rows (`dictRemoveStore`) that would mix with it.

**(6) Literal bytes and code as static storage.** Acceptable as recorded for the library unit. For `dispatch` the claim is vacuous,
since nothing is accessed through it. For `pending`, the unit's design should either cite a SPEC statement that a string literal's
bytes are static storage, or add one (SPEC review). N2's text does not need it.

**(7) The ledger's coming MIR-only rows.** Acceptable. With BU's rows, both new error variants become MIR-only rows of class (a)
against the old path, and the items block's row lands. The cost family's rows need §5's answer first, or they have no right
class. Corpus and milestone effects are unmeasured; BU's unit measures them.

### Owner question: how do SPEC's own transfer operations retain at a raw layer without an Origin?

**(a) Recommended.** Replace the Iterator sentence in 15.6.4 with one rule keyed on published contracts. Draft:

"A published operation that transfers values retains a borrowed argument at a raw layer without an Origin only by what a chain of
reads through it carries, never by its own Loans: an Iterator's `next` (§22.2.3), `exchange` and `swap` (§15.7) and the operations
of §4.6 to §4.9. At an element's raw layer without an Origin, an operation of §4.9 retains only its added and replacing inputs
(§4.9.6)."

- Resolves (1), (2) and the swap and exchange rows.
- Keeps §4.9.6 unchanged, as the owner asked, and makes 15.6.4 defer to it. §4.9.6 already says callers use its rows, never a library
  body.
- Binding keys on three published sources: the Iterator conformance (already planned), the two §15.7 intrinsics, and the §4.9
  library operations. This is a closed set of declarations required by §22.1, not an open family.
- User code keeps A's cost (`twoOut`, a user container's `pop`), with Origins as the repair where one can be stated.
- One concept: a published contract states precision, as an Origin does.

**(b) Keep (i) alone and accept the rest as cost.** Make the letter consistent by giving §4.9.6 the sentence that admits it (the
alternative already rejected).
- SPEC's own `swap`, `exchange`, `pop`, `remove`, `swapRemove`, the Dictionary key forms and `sort(by:)`/`removeAll`/`appendCopies`
  over element Types with an unstated raw layer then report Loan conflicts on natural programs.
- No repair exists for raw element Types.

**(c) The general (ii).** An unstated layer never retains a borrowed argument's own Loans. A pointer formed within a borrowed referent
states the borrow's Origin.
- It covers user code too, but reverses the `fill` example (`s` would retain neither `r` nor `k`) and Q1's tracked meaning.
- It needs a callee obligation that nothing checks (R-N2's shape).
- Rejected once (`formR`). Reopen it only if (a) is judged a shape allowlist.

Timing: answer before BU's N2 rows. Under (a), the sentence lands as a small SPEC commit with its own review, and BU's row design
takes the three contract keys.
