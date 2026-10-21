# Review 2 (re-check): G160/G184 Raw Reach and Raw Origins

- **Reviewer:** the round-1 independent reviewer, 2026-10-21. **Scope:** only whether round 1's B1–B5 are resolved and whether
  the folds introduced new problems. The worktree is `bin/worktrees/g160-raw-reach`, uncommitted, read-only.
- **Read:** ch. 5 in full; the word diff of ch. 3, 8, 13, 15, 22, Appendix E and LIBRARY; design §1, §3.2, §4, §8, §9, §11;
  DECISIONS R4-M95; PLAN (§3, G147, G160, G184); STATUS; INTEGRATED's new section.
- **Probes:** a grep for any remaining ungrouped `@raw/… during` (none). The parser's grouped-type path (`Parser.cs:4645-4686`)
  and its adaptation checks (`Parser.cs:1904-1929`, `:3584-3597`). The library's Dictionary bodies against the new
  `dictionaryStorage` contract (`Dictionary.kimi:39-61`, `DictionaryStorage.kimi:44-53`). The MIR literal expansion
  (`MirBuilder.Expansions.cs:293-329`). PLAN has 222 lines.

## Round-1 blocking findings

| # | Status | Evidence |
| --- | --- | --- |
| B1 | Resolved in SPEC (records error: R2-B2) | The grouped `E@(raw/U during o)` is used everywhere. §13.5.1 adds "The exception is an outer `raw` layer: a grouped target such as `p@(raw/U during o)` states its Origin", and §F.9.4's DelimitedTarget `"(" Type ")"` parses it. |
| B2 | Resolved as stated (records gap: R2-B1) | C1 now states the cost under either wording. G160-3 rewrites `insertOrReplace` and `removeEntry`. I found no other raw write past a shared layer. |
| B3 | Resolved | §5.4 row, §5.7 "Reading a Field" and "Typed paths", and §15.6 Addresses ("through no reference, raw pointers included … past a raw pointer §5.2.2") all count raw dereferences. `(*h.p)@raw` now states none, and `(state.storage[0])@raw` states `state.source`. |
| B4 | Resolved | §5.7 "Origins" defines the borrow an Origin belongs to and restricts signature Origins. "Calls" uses that borrow's Referent Type. G147 (1) closes: `placeEntry(handle: raw/Dictionary<K, V>, …)` is unannotated, so its pointee admits K and V, and the caller's handle reaches the literal's holder. |
| B5 | Resolved | `Lens<T>` has `internal` members. `read` is valid, and `clear` fails on authority (`lens.source`'s requirement is `ref`). |

## New blocking findings

### R2-B1. Three Dictionary bodies hold the exclusive handle across a shared reborrow; G160-3's rewrite keeps the conflict

- **Location:** design §4 table and §8 ("No library or milestone verdict is expected to change"); §9.1 G160-3; PLAN G160;
  DECISIONS R4-M95 Units.
- **Problem:** `Dictionary.tryInsert`, `Dictionary.insertOrReplace` and `DictionaryStorage.removeEntry` all hold the exclusive
  handle across a shared reborrow of the same Dictionary:
  - They first take `let layout = Storage.dictionaryStorage(self)` (`value` in `removeEntry`).
  - Then they form `Storage.borrowStorage(self@follow@ref)`.
  - Then they use `layout.0` (in `placeEntry`, `unlink`, or under G160-3's planned slot derivation).

  Under the folded contract, `layout` retains the call's exclusive Loan:
  - Annotated: `dictionaryStorage(…) -> (raw/Dictionary<K, V> during value, isize)` retains "as the borrow would" (§5.7 Calls,
    §15.6.4).
  - Unannotated: N4's Tuple-element rule also makes the result "retain every argument".

  The later `self@follow@ref` therefore creates `ref` against a live `uniq` (§15.6.2 table; §15.6.3: "access through the parent is
  suspended"). Once G160-4 implements this, A5 rejects all three library bodies. Today A5 gives raw results no value, so it accepts
  them.

  The G160-3 plan "derive the slot from the exclusive handle `layout.0`" does not remove the conflict, and `tryInsert` is not in
  G160-3's scope. A library rejection is owner decision O3. The MIR literal expansion is not affected: it takes the handle after
  the find (`MirBuilder.Expansions.cs:316-321`).
- **Correction:** in G160-3, rewrite all three bodies to search first, while the shared remainder is dead after the link is
  found, and to take `dictionaryStorage` only afterwards, deriving the slot from it. Add `tryInsert` to the design §4 table, §8,
  §9.1, PLAN G160 and DECISIONS. Replace §8's "no verdict change" sentence with this known change and its fix.

### R2-B2. `AdaptationDuring` is not an error on the valid claim; it must stay

- **Location:** design §8 ("for an Origin on a grouped `raw` adaptation target (`AdaptationOrigin`, `AdaptationDuring`)"); §9.1
  G160-0; §11 ("the parser's two adaptation-target errors"); PLAN G160; STATUS "Loans and returned references"; DECISIONS
  R4-M95 Units.
- **Problem:** the two diagnostics cover different spellings:
  - `AdaptationDuring` (`Parser.cs:3592-3596`) fires only for a `during` after a target, i.e. the ungrouped `x@raw/U during o`.
    That spelling stays invalid under §F.9.4 ("an ungrouped Type head consumes no … BorrowOrigin"), which the fold left unchanged.
  - The grouped `x@(raw/U during o)` is parsed inside the parentheses (`ParseDelimitedType`). It draws `AdaptationOrigin`
    (`Parser.cs:1917`) and `BorrowOriginSemantics_Kd` (`Parser.Origins.cs:169`).

  Two consequences follow. STATUS states a wrong fact. And G160-0, as planned, would turn a syntax error for an invalid form into
  `Unsupported_Kd`, which breaks AGENTS Diagnostics (`Unsupported_Kd` is for valid forms only). Round 1's B1 correction asked to
  replace "the two syntax errors" only for the alternative where §F.9.4 is amended; the fold chose the grouped spelling instead.
- **Correction:** drop `AdaptationDuring` from these records, and state that the ungrouped spelling stays a syntax error.

## Non-blocking findings

- **R2-N1** The design's "owner's decisions" table and DECISIONS now present review-fold content as D5, D13 and D15:
  - D5: the grouped spelling.
  - D13: raw dereferences.
  - D15: the definition, the new SPEC restriction "a raw Origin in a signature must belong to an input's borrow, a slot or
    `static`", and G147 (1) via a typed unannotated `placeEntry` handle, which replaces "the handle states the Dictionary's Origin".

  §11 records them as folds. Mark the rows as refined by review, or relay D15's restriction and the `placeEntry` change with C1–C3
  for the owner's confirmation.
- **R2-N2** §5.7 "Origins" has two gaps:
  - The "belongs to" list covers slots, an input's borrow and `static`, but not a local borrow's Origin. §5.4's own
    `let q = r@follow@raw` gives `raw/i32 during r` with `r` a local, so "storage that `o` covers" is undefined there. Say "a borrow
    value's outer Origin or slot (an input's, or in a body a local's)".
  - For an input's slot projection (`x.source`), both "a slot to … the Fields that keep it" and "a slot of an input's borrow to
    that borrow" apply. Say which Referent Type governs. A slot with several keepers should admit the union of their Referent Types.
- **R2-N3** The §5.4 row's "a fit when the result is a supertype of `E`'s Type" misfires in two cases:
  - A pointee-changing conversion that keeps the Origin (`slot@(raw/Links during source)` from `raw/u8 during source`) is never a
    supertype, so it becomes a claim. The conversion sentence below says the Origin is kept.
  - It also contradicts "`null` takes any Origin" for `null@(raw/U during o)`.

  Compare the Origins only: "a fit when `E`'s raw Origin fits `o`".
- **R2-N4** §5.7 "elsewhere it is a Refuted Origin relation": an unproven relation between signature Origins is Unknown (the Proof
  category, §15.6.1), not Refuted. Say "an Origin relation error".
- **R2-N5** §15.6.2 still lists the shared layers as (`ref`, `objref`, `rc`, `arc`), while condition 3's first limit now also
  counts a raw pointer whose Origin keeps only shared Loans. §15.6.4 cites §5.2.1, so there is no contradiction, but add it there
  for consistency.
- **R2-N6** "The last reference that `P` is reached through" (§5.4, §5.7) is ambiguous: §3.3's category `reference` includes `obj`,
  `rc` and `arc`, which have neither of the two listed Origins. Say "the last borrow or raw pointer". Either reading is sound.

The round-1 N items I spot-checked are folded as described: N1, N2, N7–N13, N17, N18; N3 apart from R2-N3/R2-N4. Links in the
changed lines still resolve. No ungrouped claim remains.

## Verdict

**Accept with corrections R2-B1 and R2-B2.** Both are record and plan fixes. R2-B1 widens G160-3 to three bodies with
search-then-handle ordering and corrects design §8. R2-B2 drops `AdaptationDuring` from the G160-0, STATUS and DECISIONS lists.
Neither changes the SPEC text. B1–B5 are resolved, and no further review round is needed once these two are folded, provided
R2-N1 reaches the owner with C1–C3.
