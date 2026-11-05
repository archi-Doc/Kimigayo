# N2 options document: independent review

Date: 2026-10-25. R4's reviewer slot; I wrote neither `options.md` nor its probes. Base: dev `cdd3c99da` (newer than the document's
`8e928c630`; the nine original probe groups give byte-identical A5 output on it). Worktree `bin\worktrees\spec-n2-rev` (removed).
Evidence in `rev\`: my probes `rev\all-src\g10`–`g18`, the harness `rev\ScratchSpecN2RevProbeTest.cs.txt` (the document's, with more
library callees printed), a throwaway env-gated prototype of the options' rows `rev\prototype.diff.txt` (never committed), the
verdict table `rev\verdicts-rev.md` (109 programs × 5 modes), MirCensusTest and selected-class logs (`census-*.log`, `sel-*.log`).

## Verdict: ACCEPT WITH FIXES

The recommendation stands: A (as A2) and Q3 (a). The prototype confirms the document's A verdicts: under its rows every one of the 22
Bad programs errs and every Ok twin stays valid. Four blocking fixes:

- B1: the SPEC reading in the summary.
- B2: the depth of A's rows.
- B3: A2's wording retains the written argument itself.
- B4: an Iterator conflict that A2 widens.

The fixes add two owner questions (Q4, Q5) and one option, C′. None of them changes the choice of A over B and C.

## 1. The rule, from the problem

- **Across a call, carriage is the contract's.** §15.6: "After a call, its results and the writable storage its public contract lets
  it write carry the Loans of the arguments they may retain". "A call writes the storage its contract lets it write the same way". An
  address "is carried as a Loan is". So under the letter, `s` after `fT((q, 1), s@raw)` carries nothing. It is a raw pointer that
  reaches no holder and so "reaches only such storage" (storage the body does not own). Today's letter is C. The document says so in
  §1.2 and §4 C. Its §0, §1.1 and `verdicts.md` ("Required = SPEC 15.6's carriage") read D2, a rule within one body, as if it crossed
  the call (B1).
- **The SPEC defect the letter does have.** §5.2.1, Retained dependencies: "A function stores through raw storage reached from its
  inputs only values that its public contract lets the call retain there". 15.6.4 retains "the argument's own value, its Loans and the
  holders it addresses" only for a raw pointer argument (R4-M116). A value of Type `(raw/u8, i32)`, `Option<raw/u8>` or `Box` that
  carries an address is retained nowhere.
  - Contradiction 1, SPEC's own operations: `Raw.initialize<(raw/u8, i32)>`, `Array.append`/`insert` (the Array literal is built
    through them, §4.9.5) and the Dictionary literal's `placeEntry`. Each stores `value` there, yet by 15.6.4 the call retains none of
    it. Probes `libInitBad`, `arrInsertBad`, `appendTupBad`, `litTupBad`, `dictTupBad` are accepted, while their bare `raw/u8` twins
    err where probed (`arrInsertRawBad`, `appendRawBad`, `litRawBad`, `dictRawBad`).
  - Contradiction 2, unsafe callees: every unsafe-callee probe that stores through raw storage (t, o, n, nr, rr, rf, ph, fillR,
    fillRB) breaks the same promise. So under the letter those programs are undefined, the Ok twins included, not "valid" as the C
    column says. wrapRR returns its pointer in a result, so condition 5 governs it instead.
  - This, not "safe code would cause UB", is why the letter cannot stay as it is. Safe callees (gT, gRF, put, fillRS) only produce
    pointers the caller no longer tracks. A later unsafe access to Loan-carrying values through such a pointer is undefined under
    condition 5, or under condition 3 for a formed pointer whose `uniq` Loan is no longer live. That is silent undefined behavior, but
    not undefined behavior caused by safe code alone.
- **The general rule.** G160 D3 already states it: an unstated layer means "at a call, what the callee may reach". §5.7 Calls
  writes it for results as "storage the callee reaches through any argument". D6 is its instance for results, and A extends it to
  writable storage. Stated once, the rule has four parts:
  1. the layers the call writes (the result's own value and storage it writes through an argument);
  2. every argument other than the one the storage is written through;
  3. what reads through those arguments reach, transitively;
  4. storage the body does not own.

  Each part is a finding below: (1) B4, (2) B3, (3) B2.
- **A shorter rule (C′).** Generalize the own-value sentence from raw pointer arguments to every argument: "an argument's own value
  ... is retained only by a result or storage that can hold a value of its Type". This is parametricity: an instance keeps what the
  generic body's carrier gave.
  - It removes both contradictions above for the operations and constructors. In the prototype it fixes put, boxPut, repl, libInit,
    arrInsert, idBox, leak, the literals, `append` and the TB and OB constructors.
  - It costs nothing measurable: no cost probe and no iteration probe changes.
  - It leaves projections (`t.0`), reads through raw or borrowed arguments, and formed pointers untracked.
  - A subsumes it at every unstated position. So C′ is the minimum any answer needs, and the real choice is C′ alone (precise, with an
    untracked remainder) or A (complete and coarse). I agree with A. Under C′, "unstated" means two things: every argument in a result
    (D6) and Type agreement in storage (against Principle 1). Its remainder is silent undefined behavior, where A gives located Loan
    conflicts (Explicit Semantics, Compiler Server Protocol).

## 2. Findings

**B1 (blocking: a wrong SPEC reading in the headline).**
- §0 ("22 probe programs are accepted where SPEC 15.6's carriage requires an error"), §1.1, §1.4 ("so for a safe body 15.6.4 must
  retain what the body stores") and the "Required" column all contradict §15.6's first bullet and the document's own §1.2 and §4 C.
- Q1's reason ("the only option that is sound for safe callees") inherits the misreading.
- Fix: present the decision as tracked against untracked, with the contradiction of §1 as the reason the letter must change. Mark the
  unsafe-callee programs (Bad and Ok) under C as "undefined: the callee breaks Retained dependencies".
- A5 has no defect here: it implements the letter.

**B2 (blocking: a missing form that changes A's rule and its A5 work).**
- A's rows ("Value from every input, Content from every raw input") and D6 itself take one read through a top-level raw argument
  (15.6.4: "a raw one with what a read through it carries"). A body can read through any raw layer of any argument, as often as it
  likes. Addresses do not conflict (D1), so a holder replaced after the call drops what the result or storage really holds.
- Accepted on dev and under the prototype's A2, though the in-body equivalent `bodyRRRRepl` errs:
  - `getTRRepl`/`fTRRepl`: `t: (raw/(raw/u8), i32)`, `*t.0`, then `h = null; y = 5`;
  - `getRRRRepl`/`fRRRRepl`: `**ppp`;
  - `getRFRRepl`: `r: ref/(raw/(raw/u8))`.
- Content from every input (prototype AC) fixes the first pair only. The other two need the closure of Content.
- So A as drafted does not meet its own §1.3 ("15.6.4 must give at least what the body can put there"), and "A5 needs no change" is
  wrong for a complete A. 15.6.4's D6 sentence is also narrower than §5.7 Calls' "storage the callee reaches through any argument":
  SPEC disagrees with itself today.
- Fix: Q4 below, with A5 as a site.

**B3 (blocking: an unsound wording, a wrong result).** A2's 15.6.4 text gives "a raw layer that the call may write, in a result Type or
in writable storage, may retain every argument". The self-store sentence excludes only a raw argument's own value from storage
written through it. Read literally, a borrowed out-parameter retains its own exclusive Loan in its referent. The prototype with that
reading (AJ) rejects valid programs: `gTOk`, `gRFOk`, `putOk`, `litTupOk`, `arrInsertOk`, `arrRawOk`, and every container literal and
mutation after it (rows in `run-AJ.m`). The document's Binding plan already excludes input j. The SPEC text must say "every other
argument", or extend "a pointer is never part of its own pointee" to borrowed arguments.

**B4 (blocking: A2 conflicts with §22.2.3 and LIBRARY; a cost the document misses).**
- §22.2.3: "an item depends neither on the receiver Loan of its call nor on the Iterator's Storage"; LIBRARY §2.1 promises the same.
  D6 includes layers behind a result borrow (BU-30: "conservative"). So `ArrayIterator<raw/u8>.next` gets `0 Value result` (i32
  elements: `0 Content result`), and keeping two items is an error on dev (`iterRaw`, `iterTup`). This is a defect today.
- A2 extends it to every nominal Item with an unstated raw Field. `Array<Box>` iteration (`iterBox`) and a plain `for b in a` that
  keeps an item (`forBox`) become errors under the prototype.
- The document's Q2 claim ("no function that would gain a row of consequence") comes from the census of declared contracts, which
  cannot see instances.
- Fix: Q5 below. In the prototype, A2+R′ makes those four valid and changes no other of the 109 verdicts. R′: no unstated-layer rule
  behind a result borrow except for raw arguments, the existing "result the call forms within the argument's storage" clause applied
  to any raw argument.
- R alone (no rows behind a result borrow) loses `lendBad`, an unsafe body returning `(*p)@ref during x`, to condition 5.

**N1 (BU's point 1: the memo).**
- A's branch needs no layers of the arguments (B's shape). It keys on the output's positions only. The prototype sets an accumulated
  per-output flag in `Held` where `DeclaredStep` is Unstated. The rows come from the input list. `retentionMet` keeps its
  (Type, written) key.
- Correction to "one branch": `Held`'s `untied` merges `RawStep.Unstated or RawStep.Static` (Binding.Retention.Rows.cs:198). Using it
  puts A's rows on `during static` layers (prototype AH: `write<T>` and `format` keep their new rows even with the library unit
  applied). A second flag is required.
- A2 lets results take the same flag, so `retentionUnstatedResult` (Atoms.cs:157) becomes a deletion candidate. A1 would need a "below
  a nominal result's Field" path bit in the key, or keep two detection sites.
- R′ (Q5) needs the result's holds split by whether a borrow lies above them: RetentionWalk knows `at.Layer`, the memo does not, so
  the key gains a bit or each class gets its own pass. R needs neither.
- Q4's closure belongs to A5 and does not touch the memo.

**N2 (BU's point 2: the library prerequisite, checked).**
- `dispatch` holds the address of the selected `reserve` implementation, or null:
  - `PrepareWriterDispatch` (BodyLowering.Formatting.cs:27–74) selects it;
  - `__kimi_text_writer` stores it at offset 8 (Utf8BufferRuntime.ll.in);
  - the `user:` block calls it (Utf8FormatRuntime.ll.in:207).
- `pending` holds a string literal's bytes, and only those:
  - BodyLowering.Calls.cs:222–226 selects `__kimi_format_literal` for a `StringLiteralKoto` argument whose estimate is 0;
  - `defer:` stores the literal's data pointer at offset 32;
  - `prepend:` copies from it.
- Neither addresses an argument's storage. With both Fields `during static` and A's branch on Unstated only, MirCensusTest is clean
  under A2 and A2+R′ (library, 44 milestones, goldens, SPEC variants).
- The annotation alone changes none of 507 tests in RetentionTest, MirBorrowsTest, FixedOriginRetentionTest, Utf8Borrow, Utf8Format,
  Utf8FormatBinding and InterpolationScanRecovery. Under A2 only 17 RetentionTest row expectations grow; no verdict test fails.
- Without the unit, A2 rejects four library `format` bodies in every program: Core.kimi `"^"`, `".."`, `"..="`, `".."`. The literal
  temporary's Loan is retained in the writer, against LIBRARY's "no output dependency on the input borrow".
- So it is its own library unit, landing before BU's rows. Open for that unit: SPEC states `static` as "static storage" but nowhere
  says that literal bytes (or code) are static storage. For `dispatch` the claim is vacuous (nothing is accessed through it).

**N3 (forms the document misses that strengthen A2).**
- Constructors of structs with Tuple or Option Fields lose the pointer on dev and under A1 (`TB.init(t: (q, 1))`,
  `OB.init(o: .Some(q))`), while `Box.init(p: q)` keeps it: the strongest case for Q2 (a).
- Literals and `append` (B1's operations).
- A Callable-bounded `applyT((q, 1), keep@uniq)` (`erasedTBad`). Dev reports only an unrelated conflict at `usep(s)`; A2 adds the
  required one at `y = 5`.
- A2 fixes all of them.

**N4 (forms that stay untracked under every option, so Q2's "shrinks to integer round trips" is wrong and Q3 is needed regardless).**
- Static storage written by a call: `stash0(q)` loses even a top-level raw argument, while the in-body `Stash.g = q` errs.
- A direct closure call that writes its environment: `keep(q)` with `[s@uniq]` loses a top-level raw argument too, while the same
  closure through a Callable bound is tracked.
  - Hypothesis: the Value-call retention reads only the declared signature (TyCtxt.Retention.Calls.cs:45), so the receiver's
    environment is no output.
  - This is an implementation gap of R4-M116's rule on the MIR (cf. G75 E7). SPEC 15.8.2 already makes environment bindings Fields of
    the receiver's referent. An open issue for BU and R4, not N2's decision.

**N5 (A's cost: classes the document misses).** Each is a valid program that becomes an error under the prototype unless noted.
- A container of unstated raw pointers retains its mutators' Callable and Slice arguments:
  - `a.sort(by:)` with a closure capturing `bias@ref`, then `bias = 2` (`costSortBy`; also `removeAll` over
    `Array<(raw/u8, i32)>`);
  - `dst.appendCopies(src.slice())`, then `src.append(null)` (`costAppendCopies`).
- Positions that hold a fresh allocation or an OS handle have no Origin to state. Neither has a pair-supplied raw layer (§8.1.1:
  "none in a pair"). So the repair "state the relation" is not always available. The same holds for D6 today (G160 §7 accepted it).
- `dictTupOk` errs under A, but only because a Dictionary literal of raw values already errs on dev (`dictRawOk`; `dictRefOk` passes).
  That is a separate A5 open issue (hypothesis: the Dictionary's destruction counted as a use of the addresses).
- A refinement, A′, would answer most of the first class: an owned argument whose Type holds no raw layer and no borrow outside a
  Callable, nor a Callable whose result does, contributes nothing, since a pointer formed to it addresses callee-local storage. It is a
  per-input property (no memo change), at the price of one more sentence. I would not adopt it before the corpus effect is measured.

**N6 (non-blocking corrections).**
- §4's "§5.7 gives an unstated layer's meaning at a call as 'what the callee may reach'" quotes G160 D3 and §3.2. SPEC §5.7's table
  says "at a call what §15.6.4 gives". The correct argument is stronger: SPEC deferred D3's meaning to 15.6.4, which states it only
  for results.
- The census note for `compact` also gains `1 Content store 0` from `memory: raw/u8`; it is harmless.
- Under A, R4-M116's observation R-N2 ("a callee that stores a converted pointer where its contract's Type cannot hold the argument
  breaks its contract") no longer applies at unstated positions; its DECISIONS entry should say so.

## 3. Owner questions, corrected, with my recommendations

**Q1. Which rule decides retention at a raw layer without an Origin that a call writes?**
- Options: (a) A, D3's rule at every written layer: every other argument and what reads through it reach; (b) B; (c) C, the letter;
  (e) C′, the own value of every argument by Type agreement; (d) D.
- Recommendation: (a). It is the one rule ("unstated = what the callee may reach") for results and storage alike. Its failures are
  located Loan conflicts with an existing repair, an Origin, where one can be stated.
- C is internally inconsistent (§1). C′ is the minimum fix, precise, but leaves projections, reads and formed pointers to silent
  undefined behavior, with two meanings of "unstated".
- B leaves fillRB and fillRS, or becomes A.

**Q2. Under A, does a nominal result's raw Field without an Origin follow the rule?**
- Options: (a) A2, yes; (b) A1, keep the exclusion.
- Recommendation: (a), together with Q5 (a). Constructors (`TB.init`) and `id<Box>` then track the pointer as `put<Box>` does, and
  `retentionUnstatedResult` can go.
- Without Q5 (a), A2 makes iteration over `Array<S>` item-dependent on the receiver Loan whenever S has an unstated raw Field,
  against §22.2.3. Then (b) is the lesser change.

**Q3. Does condition 5 cover an access a callee makes through a pointer the body passes it?**
- Recommendation: (a), whatever Q1's answer, worded to cover indirect passing: "by the body or by a call to which it passes it,
  directly or within a value or storage".
- Untracked classes remain under every option: integer round trips, static storage written by a call (N4), and A1's nominal Fields.

**Q4 (new). How deep does the retention go (also D6)?**
- Options: (a) transitively: what any chain of reads through the argument reaches; (b) one read through every argument (the
  prototype's AC); (c) one read through a raw argument only (today's D6).
- Recommendation: (a). Addresses do not block replacement, so anything less leaves B2's programs to condition 5. It is also what
  §5.7 Calls already says.
- Cost: A5 takes the closure of Content into outputs that are untied, a fixed point over the state's holders; unmeasured.

**Q5 (new). Does the rule apply behind a result borrow?**
- Options: (a) R′, no: the call lends a referent and does not write it, so only raw arguments are retained there (own value and reads
  through them; the within clause); (b) R, no, nothing; (c) yes (today).
- Recommendation: (a). It closes today's conflict with §22.2.3 and LIBRARY's Iterator guarantee, and it keeps `lendBad` tracked.

## 4. What the SPEC edit and the units would touch

- **SPEC.**
  - §15.6.4: the raw-layer sentence (one statement for results and writable storage; "every other argument"; Q4's depth; Q5's
    borrows), with or without the own-value generalization (C′ is implied by A).
  - §5.7 Calls: the merged bullet. Under A2 delete "A raw pointer in a Field of a nominal result is stated by that Type's Fields
    alone".
  - §5.7's table row `raw/T` (optionally restate D3).
  - §5.2.1: condition 5 (Q3's clause; examples: an integer round trip, static storage), and a note under Retained dependencies that
    unstated positions retain every argument.
  - §15.6 Reach: the document's clause.
  - §22.2.3: no change under Q5 (a).
  - Examples: an out-parameter (`fill(r: ref/i32, slot: uniq/(raw/u8))` and its `during r` repair), an Array literal of
    `(raw/u8, isize)` pairs, and an iterator over raw elements kept across `next`. A SPEC-example test row for each.
- **IMPL.** Nothing found (`docs/impl` states no retention).
- **LIBRARY.** Utf8Writer's Fields are private, so no table row changes. `Utf8Format.format`'s guarantee then holds; record the
  library unit there only if the reviewer of that unit asks.
- **Records.** DECISIONS (the owner's answers; R-N2's scope; BU-30's "conservative behind a borrow" superseded under Q5 (a)); STATUS
  Loans row; PLAN G160 (the order below); open issues: N4's closure environment and N5's Dictionary destruction.
- **Order.**
  1. The owner's answers.
  2. The SPEC edit and its review.
  3. The library unit: `dispatch` and `pending` `during static`, after settling the static-storage status of literal bytes.
  4. BU's rows.
  5. G160-5's follow-up.
- **BU's rows.**
  - `Held`'s Unstated flag, separate from Static.
  - Value from every input but the written one, Content from raw inputs (or from every input under Q4 (b) and (a)).
  - Results from the same flag under A2, deleting `retentionUnstatedResult`.
  - Under Q5 (a), the result's holds split by a borrow above them (a key bit or two passes).
  - Failing-first RetentionTest rows: the 22, N3's, B4's iterators. The 17 grown expectations are updated with their reason.
- **A5 (G160-5's follow-up).** Under Q4 (a), the closure of Content at untied outputs (MirBorrows.Calls.cs:90 `Retained`, keyed on
  the existing Untied flags, so no new vocabulary). Otherwise no change. Plus the MirBorrowsTest.Raw verdicts, the (b) ledger rows
  where the old path accepts, and the census, allocation and scaling rows.

## 5. Unverified

- The prototype is throwaway and partial:
  - R′ covers raw layers behind a result borrow through `RetentionParts` only, not a nominal Field lent from a raw argument;
  - C′ matches struct, enum and Tuple positions only;
  - Q4 (a)'s closure and A′ are not prototyped.
- Not run: the old path (CLI) for my probes; native fixtures, milestones and goldens beyond MirCensusTest's A5 census; the
  difference ledger; warm allocation and timing of A's rows.
- Two causes are hypotheses: N4's closure-environment gap and N5's Dictionary destruction error.
- Whether literal bytes are "static storage" in SPEC terms, which the library unit's `during static` needs.
