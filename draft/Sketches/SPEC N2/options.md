# N2: retention at a raw layer below an argument's top (SPEC §15.6.4) — options

**Revised after review** (`review.md`, ACCEPT WITH FIXES): B1–B4 and N1–N6 are folded in, and the owner questions are corrected in §7.
Date: 2026-10-25. R4's designer lane, the later SPEC session's item N2 (DECISIONS BU-30, "The review's N2"; R4-M122's last bullet; PLAN
G160). Base: dev `8e928c630`. The reviewer reran on `cdd3c99da` and got byte-identical output for the original nine groups.

Process: options → independent review → the owner's answers → SPEC edits with their own review. No SPEC, IMPL, code or test is
changed. Evidence: my probes in `probe\verdicts.md`; the reviewer's probes in `rev\all-src\g10`–`g18`, the 109-program table
`rev\verdicts-rev.md` and a throwaway prototype of the options' rows (`rev\prototype.diff.txt`, never committed).

## 0. Summary

- **Today's letter is untracked (option C).** Across a call, a value carries what the callee's contract retains (§15.6). 15.6.4 retains
  an argument's own value only for a raw pointer argument. So storage a call fills with a Tuple's, an Option's or a Box's raw layer
  carries nothing and reaches only storage the body does not own. A5 implements this letter and has no defect here.
- **The letter contradicts itself.** §5.2.1's Retained dependencies lets a body store through raw storage only what its contract
  retains. SPEC's own operations break that: `Raw.initialize`, `Array.append` and `insert`, and Array and Dictionary literals with Tuple
  elements. Every unsafe callee storing a layer from below an argument's top breaks it too, so those programs, Ok twins included, are
  undefined under the letter. Safe callees only leave untracked pointers, which makes later unsafe access silent undefined behavior.
- **The decision is tracked against untracked (Q1).** The tracked rule is G160 D3's meaning of "unstated": at a call, what the callee may
  reach. 15.6.4 states it only for results, and only one read deep. Stated once, it has four parts:
  - where: results and written storage (Q2), not behind a result borrow (Q5);
  - from whom: every *other* argument;
  - how deep (Q4);
  - plus storage the body does not own.
- **Recommendation** (the reviewer's and mine): A2 (Q1 (a), Q2 (a)); transitive depth (Q4 (a)); R′ (Q5 (a)); condition 5 across calls
  (Q3 (a)).
- **Order:** owner → SPEC edit and review → Utf8Writer library unit → BU's rows → A5's Content closure in G160-5's follow-up.

## 1. Required meaning

1. **Whose storage.** §5.7's table: `raw/T` addresses "storage it does not state: what the value reaches (§15.6), and at a call what
   §15.6.4 gives". G160's D3 meant "at a call, what the callee may reach" and deferred it to 15.6.4. 15.6.4 states it only for results
   (D6: "may retain every argument, a raw one with what a read through it carries"). §5.7's Calls bullet is wider ("storage the callee
   reaches through any argument"), so SPEC disagrees with itself today.
2. **Across a call, carriage is the contract's.** §15.6: "After a call, its results and the writable storage its public contract lets it
   write carry the Loans of the arguments they may retain". An address "is carried as a Loan is". D2 is a rule within one body; it
   crosses a call only through 15.6.4's rows. So under the letter, `s` carries nothing after `fT((q, 1), s@raw)`.
3. **What a body can put at a written raw position without an Origin.** By §5.7 Fits, such a layer "counts as stating an Origin that
   every Origin outlives", so every raw value fits:
   - a raw argument's own value;
   - a layer below another argument's top (N2), also two or more reads deep (`*t.0`, `**ppp`, `r.*.*`);
   - a pointer the body forms from a borrowed or raw argument (`r@follow@raw`), converted or not.

   Safe code does all of these (`slot@follow = r@follow`, `slot@follow = r@follow@raw@raw/u8`, `put<T>`, `replace<T>`).
4. **Where the letter breaks.** SPEC's own operations, and unsafe callees, store values that 15.6.4 retains nowhere. So one of two
   things must change. Either 15.6.4 retains them (tracked), or Retained dependencies admits untracked stores and the caller gets storage
   the body does not own (untracked, made consistent).

## 2. What 15.6.4 states, and the cause

| A raw layer without an Origin that the call writes | Retained there today | Probes (dev A5) |
| --- | --- | --- |
| In a result Type outside a nominal Type's Fields (a Tuple element, Option payload or Type argument) | Every argument, a raw one with one read through it, plus storage the body does not own (D6) | getT, getRF, getRR, formR, idTup: err; two-read getTRRepl, getRRRRepl, getRFRRepl: acc |
| Behind a result borrow | D6 too (BU-30: "conservative") | iterRaw, iterTup: err, against §22.2.3 |
| In a nominal result's Field | A raw argument's own value where the Field can hold its Type (R4-M116); any other pointer is "hidden", under condition 5 (§5.7 Calls) | wrapRR, wrapRF, idBox, leak, tb, ob: acc |
| In writable storage (a raw or borrowed parameter's pointee, its Fields, its Type arguments) | A raw argument's own value of agreeing Type, plus what Origins tie | t, o, n, nr, rr, rf, ph, gRF, gT, put, boxPut, repl, libInit, arrInsert, appendTup, litTup, dictTup, fillR, fillRB, fillRS: acc |

A raw layer with an Origin has no gap: an unstated value reaches it only through a claim.

**Cause.** D3's meaning is stated only for results, and there only one read deep. N2's forms, the formed pointer and the deeper reads are
all values that the fit admits at a written position without an Origin and that no row names. A fix for N2's forms alone (B) leaves the
other two.

**Sites.**
- SPEC: 15.6.4's raw-layer and own-value sentences; §5.7's Calls bullet and table row; §5.2.1's Retained dependencies and condition 5;
  §15.6's Reach.
- Binding's hold walk (`Binding.Held`). Its `untied` merges `Unstated` and `Static` (Rows.cs:198), and it gives Value rows only to raw
  inputs.
- A5's `Retained` (`MirBorrows.Calls.cs`), under Q4 (a) only.

## 3. Forms and verdicts

**Setup.** Each Bad program has `q = w@raw@raw/u8` address `w: ref/i32 = y@ref`. The call puts `q`'s layer into `s` or a result, then
`y = 5` runs while that place is used. Each Ok twin uses the place before `y = 5`.

**Columns.** "err" means A5 reports a Loan conflict. The C′, A2 and A2+R′ columns are the reviewer's prototype; the Q4 (a) column is
reasoned. "UB" means the program is undefined under today's letter, because the callee breaks Retained dependencies.

| Class | Probes | dev (= C) | C′ | A2 | A2+R′ | +Q4 (a) |
| --- | --- | --- | --- | --- | --- | --- |
| N2 forms, unsafe callee (Tuple, Option, Box by value and by ref, raw-of-raw, ref-of-raw, pair-supplied) | t, o, n, nr, rr, rf, ph | acc; **UB**, Ok too | acc | err | err | err |
| Formed pointer, unsafe / safe callee | fillR, fillRB / fillRS | acc; UB / untracked | acc | err | err | err |
| Safe callee, projection or read | gRF, gT, gRFRepl | acc (untracked) | acc | err | err | err |
| Generic, library, constructors | put, boxPut, repl, libInit, arrInsert, appendTup, litTup, dictTup, tb, ob, idBox, leak | acc (SPEC's own operations contradict) | err | err | err | err |
| Nominal result from a read | wrapRR, wrapRF | acc (hidden) | acc | err (A1: hidden) | err | err |
| Two reads deep (B2) | getTRRepl, fTRRepl / getRRRRepl, fRRRRepl, getRFRRepl | acc | acc | acc | acc | err |
| Iterator items (B4) | iterRaw, iterTup / iterBox, forBox | err (defect) / acc | same | err | **acc** | acc |
| Lent from a raw argument | lendBad | err | err | err | err | err |
| Cost: valid programs that become errors (N5) | costSortBy, costTupleArray (`removeAll`), costAppendCopies | acc | acc | **err** | **err** | err |
| Untracked under every option (§5) | stash0, stashT; closureTop, closureT, closureVar | acc | acc | acc | acc | acc |
| Controls | c0, putRaw, arrInsertRaw, appendRaw, litRaw, dictRaw, getT, getRF, getRR, formR, idTup, c1, cw, boxCtor, erasedTop, bodyRRRRepl | err | err | err | err | err |

Every Ok twin is accepted in every mode except `dictTupOk`, which errs under C′ and A. A Dictionary literal of raw values already errs on
dev (`dictRawOk`), a separate A5 open issue (hypothesis: the Dictionary's destruction counts as a use of the addresses). On dev,
`erasedTBad` reports only an unrelated conflict; A2 adds the required one at `y = 5`.

## 4. Options

The decision is tracked (A, B, C′, D) against untracked (C). The brief's "unstated (conservative)" option is A, which applies D3's
meaning at every written position. C is the letter, made consistent. The brief's typed generalization is B. C′ is the reviewer's
shorter rule.

### A. D3's rule at every written raw layer without an Origin (recommended: A2 with Q4 (a) and Q5 (a))

**15.6.4.** Current text: "…without one, a parameter's raw layer admits what its pointee Type admits, and a raw layer in a result Type,
outside a nominal Type's Fields, may retain every argument, a raw one with what a read through it carries, and reaches storage the body
does not own."

A2 text: "…without one, a parameter's raw layer admits what its pointee Type admits, and a raw layer that the call may write, in its
result or in storage it writes through an argument, may retain every other argument with what any chain of reads through it reaches,
and reaches storage the body does not own. Behind a borrow in the result it retains only the raw arguments, as a result the call forms
within their storage does (§5.2.2, §5.6)."

Variants:
- A1 adds "outside a nominal Type's Fields" after "in its result".
- Q4 (b) reads "with what a read through it carries"; Q4 (c) reads "a raw one with what a read through it carries".
- Q5 (b) retains nothing behind a result borrow; Q5 (c) drops the last sentence.

The own-value sentence gains "…or by a raw layer without an Origin that the call may write (below)", so C′ is implied. R4-M116's R-N2 (a
converted store breaks the contract) no longer applies at unstated positions; DECISIONS records this.

**Other sections.**
- §5.7 Calls (A2): "Through a raw layer without an Origin in a parameter, a call may store what its pointee Type admits. A raw layer
  without an Origin that a call may write, in its result or in storage it writes through an argument (also as a Tuple element, an Option
  payload, a Type argument or a Field), may address what the callee reaches through every other argument and storage the body does not
  own (§15.6.4)." Delete "A raw pointer in a Field of a nominal result is stated by that Type's Fields alone…"; A1 keeps it.
- §5.2.1 Retained dependencies: add "a raw layer without an Origin lets the call retain every other argument there (§15.6.4)". Condition
  5's examples become an integer round trip and a static written by a call.
- §15.6 Reach: add "…or from a raw layer without an Origin that a call writes (§15.6.4)".
- §22.2.3: no change under Q5 (a).
- New examples, each with a SPEC-example test row: `fill(r: ref/i32, slot: uniq/(raw/u8))` and its `during r` repair; an Array literal of
  `(raw/u8, isize)` pairs; an iterator over raw elements whose items are kept across `next`.

**Principles.**
- One Concept: "unstated" means what the callee may reach, in results and storage alike.
- Local Reasoning: it is read from the contract's Types.
- Explicit Semantics: precision is stated with an Origin, and omission is conservative.
- Compiler Server Protocol: a broad row fails as a located Loan conflict, not as silent undefined behavior.

**Verdicts.** Under A2+R′ every Bad program errs except the two-read forms and §5's; Q4 (a) closes the two-read forms. The iterator
programs stay valid, and the cost class errs.

**Binding** (BU's unit; the reviewer's N1).
- `Held` gets an accumulated per-output flag where `DeclaredStep` is `Unstated`, apart from `Static`. Reusing `untied` would put rows on
  `during static` layers (prototype AH). `retentionMet` keeps its (Type, written) key.
- Rows come from the input list: Value from every input but the written one, and Content from raw inputs (from every input under Q4 (b)
  or (a)).
- Under A2, results take the same flag and `retentionUnstatedResult` (`Binding.Retention.Atoms.cs:157`) becomes a deletion candidate. A1
  needs a key bit or two detection sites.
- Under Q5 (a), result holds split by whether a borrow lies above them (a key bit or two passes).
- Tests: failing-first RetentionTest rows for §3's forms; the 17 grown expectations are updated with their reason.

**A5** (G160-5's follow-up).
- Under Q4 (a): the closure of Content into untied outputs, a fixed point over the state's holders in `MirBorrows.Calls.cs` `Retained`,
  keyed on the existing Untied flags, with no new vocabulary. Its cost is unmeasured; V16 scaling rows land with it.
- Otherwise no change, beyond the MirBorrowsTest.Raw verdicts, the (b) ledger rows and the census, allocation and scaling rows.

**Library prerequisite** (the reviewer's N2).
- Utf8Writer's `dispatch` holds the selected `reserve` implementation's address or null (`PrepareWriterDispatch`,
  BodyLowering.Formatting.cs:27–74).
- `pending` holds only a string literal's bytes (`__kimi_format_literal`, BodyLowering.Calls.cs:222–226).
- Without the unit, A2 rejects four library `format` bodies in every program (Core.kimi `"^"`, `".."`, `"..="`, `".."`), against LIBRARY's
  "no output dependency on the input borrow".
- With both Fields `during static`, in their own unit before BU's rows, MirCensusTest is clean under A2 and A2+R′. The annotation alone
  changes none of 507 tests.
- Open: SPEC never says literal bytes or code are static storage. For `dispatch` the claim is vacuous.

**Cost.**
- An out-parameter retains every other argument: `lookup(t: ref/Table, k: ref/Key, out: uniq/(raw/u8))` keeps `k` borrowed while `out` is
  used. Repair: `out: uniq/(raw/u8 during t)`.
- A container of unstated raw pointers retains its mutators' Callable and Slice arguments:
  - `a.sort(by:)` with a closure capturing `bias@ref`, then `bias = 2`;
  - `removeAll` over `Array<(raw/u8, i32)>`;
  - `dst.appendCopies(src.slice())`, then `src.append(null)`.
- Some positions have no Origin to state, so no repair exists there: a fresh allocation, an OS handle, a pair-supplied layer (§8.1.1,
  "none in a pair"). D6 has this limit today, which G160 §7 accepted.
- A2: a nominal result with an unstated raw Field retains every argument (`make(cfg: ref/Config, p: raw/u8) -> Cursor`). Repair:
  `Cursor {source}`, `p: raw/u8 during source`. The prototype, not the census of declared contracts, found the `Array<Box>` item cases;
  Q5 (a) answers them.
- Refinement A′ (the reviewer's N5): an owned argument whose Type holds no raw layer and no borrow outside a Callable contributes
  nothing. It is per-input and answers most of the container class, but it waits for corpus measurements.
- Corpus, milestones and ledger are unmeasured.

### B. Typed: every raw layer an argument's value reaches by Content

**Wording**, replacing 15.6.4's raw-argument sentences: "A raw layer that an argument's value reaches by Content (the argument, its
parts, Fields and Type arguments, and through a raw or borrowed argument its pointee or referent and theirs) is retained by a result or
storage that can hold a value of the layer's Type, with the argument's Loans and addresses and what reads through it carry to that
layer. A pointer the call forms to a part of such a pointee or referent is retained as that part's layer would be."

**Assessment.**
- Principles: results keep D6's untyped rule, so one kind of position has two rules, and a conversion breaks the Type agreement.
- Verdicts: the N2 forms and fillR err, provided the walk follows every read. fillRB and the safe fillRS stay untracked. A contract
  cannot tell a formed pointer from a copy, so closing them makes B into A.
- Binding: a new Content walk per input, within BU-35's memo, matched by `Erase` at each hold position. That is more code than A for
  less coverage.
- Cost: fewer broad rows where Types disagree, and every converted store in an unsafe body becomes a broken promise (R-N2, extended).

### C. Untracked: the letter, made consistent

**Wording.**
- §5.2.1 Retained dependencies: "…only values that its public contract lets the call retain there; a raw layer without an Origin may
  receive any raw value, which after the call reaches only storage the body does not own (§15.6.4)".
- 15.6.4: "A raw layer without an Origin in storage the call writes retains only what the sentences above state."
- Q3's clause, and A's §15.6 clause.

**Principles.** C works against all three. One Concept: a pointer stays tracked at an argument's top but not one layer below it
(`put<raw/u8>` against `put<(raw/u8, i32)>`). Local Reasoning: whether a later access is defined depends on whether some callee wrapped
the pointer. Explicit Semantics: the consequence is silent undefined behavior.

**Verdicts.**
- §3's first five classes are valid, as on dev.
- Later accesses are undefined: reading `w`'s reference back through `s` after `fT((q, 1), s@raw)` (condition 5), and
  `unsafe => *s.0 = 2` after `put((u@follow@raw, 1), s@uniq)` (condition 3: `u`'s Loan is not live in the model).
- Without the §5.2.1 amendment, the unsafe-callee programs and SPEC's own operations over Tuple elements stay undefined.

**Binding and A5.** No change; STATUS records the reading.

**Soundness.** C needs Q3. Probe `g9-leak` is Binding-clean. A5 and the old path accept `leak`; A5 rejects `leakDirect`, which passes `q`
directly.

```kimi
unsafe func peek<T>(p: raw/u8) -> T          // Safety: p addresses a live, initialized T.
    T is Copy
    unsafe => return *(p@raw/T)
func leak() -> i32
    var y: i32 = 1
    var w: ref/i32 = y@ref
    let b = id(Box.init(p: w@raw@raw/u8))   // b.p is hidden (5.7 Calls): it reaches only storage the body does not own.
    y = 5                                   // Accepted: nothing used later carries w's address.
    var z: i32 = 0
    var r: ref/i32 = z@ref
    unsafe => r = peek(b.p)                 // peek reads w, storage its body does not own: condition 5 holds in peek.
    return r@follow                         // Reads y through w's reference, after y = 5.
```

If condition 5 is read per body, this program is defined and unsound. It relies only on today's nominal-result reading.

### C′. Every argument's own value where a position can hold its Type (the reviewer's shorter rule)

**Wording.** "An argument's own value, its Loans and the holders it addresses, is retained only by a result or storage that can hold a
value of its Type…", moved before the raw-pointer sentence. C's §5.2.1 amendment covers what remains.

**Assessment.**
- Principles: parametricity, since an instance keeps what the generic body's carrier gave. But "unstated" then means two things: every
  argument in a result (D6), and Type agreement in storage.
- Verdicts (prototype): it fixes put, boxPut, repl, libInit, arrInsert, idBox, leak, the literals, `append`, and tb and ob. It changes no
  cost or iteration probe. Projections (`t.0`), reads through raw or borrowed arguments, formed pointers, wrapRR and wrapRF stay
  untracked, so they keep C's silent undefined behavior.
- Binding: R-B4's `Erase` test, applied to every input.
- Cost: none measured. A subsumes C′ at every unstated position, so C′ is the minimum any answer needs.

### D. A shorter wording of A: an unstated layer counts as an Origin

**Wording**, after 15.6.4's "never per Origin:": "here a raw layer without an Origin counts as stating an Origin that every Origin
outlives (§5.7), so a result or storage that holds one may retain every Origin, and an argument whose Type, referent or pointee holds
one has that Origin;".

**Assessment.** D gives A's rows for Tuples, Options, Type arguments, reads and Origin-bearing arguments, but it has four flaws. It misses
a by-value nominal argument's Fields ("or its stored content" would be needed). It gives "Origin" a second meaning, since §15.6.1 exempts
raw Origins from regions. It retains the written argument itself (B3). And it leaves depth and borrows implicit. A's wording is preferred.

### Rejected

- **Static rejection in the callee:** an unstated position in writable storage would admit only `null`, an allocation and a raw
  argument's own value. It rejects valid safe bodies (gRF, gT, fillRS) and cannot reject a generic body at an instantiation (§8.1.2,
  "discovers no missing proof").
- **Unstated layers in signatures as elided Origins:** this reverses D3 and changes every raw signature.

## 5. Untracked under every option (the reviewer's N4)

- **Static storage written by a call.** `stash0(q)` loses even a top-level raw argument, while the in-body `Stash.g = q` errs. Condition
  5's example list should name it.
- **A direct closure call that writes its environment.** `keep(q)` with `[s@uniq]` loses a top-level raw argument too; the same closure
  through a Callable bound is tracked.
  - Hypothesis: the Value-call retention reads only the declared signature (`TyCtxt.Retention.Calls.cs:45`).
  - §15.8.2 already makes environment bindings Fields of the receiver's referent, so this is an implementation gap of R4-M116's rule.
    It is an open issue for BU and R4, not N2's decision.

## 6. Sequence and records

1. The owner's answers.
2. The SPEC edit, with A's sections and examples, and its own review.
3. The Utf8Writer library unit: `dispatch` and `pending` become `during static`, once the static-storage status of literal bytes and code
   is settled.
4. BU's rows.
5. A5's Content closure under Q4 (a), with G160-5's follow-up: verdict tests, ledger rows, census, allocation and scaling.

Records:
- DECISIONS: the owner's answers; R-N2's scope; BU-30's "conservative behind a borrow", superseded under Q5 (a).
- STATUS: the Loans row.
- PLAN G160: this order.
- Open issues: the closure environment (BU/R4) and the Dictionary-literal destruction error (A5).
- IMPL and LIBRARY are unchanged: IMPL states no retention, and Utf8Writer's Fields are private.

## 7. Owner questions

**Q1. Is a raw layer without an Origin that a call writes tracked, and by which rule?**
(a) Tracked, by A. (b) Tracked, by B. (c) Untracked, by C. (d) C′ alone, plus C's amendment. (e) D.

The reviewer recommends (a), and so do I. A gives "unstated" one meaning in results and storage alike, removes both contradictions, and
its failures are located Loan conflicts, with an existing repair wherever an Origin can be stated. The others fall short:
- C makes natural programs silent undefined behavior and depends on Q3.
- C′ is the precise minimum but leaves projections, reads and formed pointers untracked, and gives "unstated" two meanings.
- B leaves fillRB and fillRS untracked, or becomes A.

My addition: (c) still needs C′, or SPEC's own operations stay contradictory, so (c) is really (d).

**Q2. Under A, does a nominal result's raw Field without an Origin follow the rule?**
(a) A2: yes. (b) A1: keep D6's exclusion, under condition 5.

The reviewer recommends (a) together with Q5 (a), and so do I. `TB.init`, `OB.init` and `id<Box>` then track the pointer as `put<Box>`
and `Box.init` do, and `retentionUnstatedResult` can go. Without Q5 (a), A2 makes iteration over `Array<S>` depend on the receiver Loan
whenever S has an unstated raw Field, against §22.2.3, and (b) is the lesser change. My first draft claimed that no function gains a row
of consequence. That claim came from declared contracts, which cannot see instances, and it is withdrawn.

**Q3. Does condition 5 cover an access that a callee makes through a pointer the body passes it?**
(a) Yes: "by the body or by a call to which it passes it, directly or within a value or storage". (b) Leave the text as it is.

The reviewer recommends (a), whatever Q1's answer, and so do I. Integer round trips and statics written by a call stay untracked under
every option, and so do A1's nominal Fields. Read per body, `leak` is defined and unsound.

**Q4. How deep does retention through an argument go, D6 included?**
(a) Transitively, through any chain of reads. (b) One read through every argument. (c) One read through a raw argument (today).

The reviewer recommends (a), and so do I. Addresses do not block replacement, so anything shallower leaves getTRRepl, getRRRRepl and
getRFRRepl to condition 5, although the same code in one body errs (`bodyRRRRepl`). (a) is also what §5.7 Calls already says. Its cost,
A5's Content closure at untied outputs, is unmeasured, so V16 scaling rows land with it.

**Q5. Does the rule apply behind a borrow in a result?**
(a) R′: only raw arguments are retained there, by their own values and reads through them (the within clause), since the call lends the
referent and does not write it. (b) R: nothing is retained there. (c) Yes (today).

The reviewer recommends (a), and so do I. It removes today's conflict with §22.2.3 and LIBRARY §2.1: `iterRaw` and `iterTup` err on dev,
and A2 would add `iterBox` and `forBox`. It also keeps `lendBad` tracked, where (b) loses it to condition 5. It supersedes BU-30's
"conservative behind a borrow".

On all five I agree with the reviewer. My one addition is Q1's note that (c) needs C′.

## 8. Unverified

- The prototype is throwaway and partial. It covers R′ only through `RetentionParts`, not a nominal Field lent from a raw argument. It
  covers C′ only at struct, enum and Tuple positions. Q4 (a)'s closure and A′ are not prototyped.
- Not measured: the corpus, milestones, native fixtures, goldens beyond MirCensusTest, the ledger, and the warm allocation and timing of
  the rows and the closure.
- Hypotheses: the closure-environment gap and the Dictionary-literal destruction error.
- Whether literal bytes and code are "static storage", which the library unit's `during static` needs.
- The CLI old path was run only on the original probes. It accepts all of them except `wAddr`.
