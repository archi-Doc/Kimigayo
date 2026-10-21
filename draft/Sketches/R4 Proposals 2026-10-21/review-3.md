# Independent review: candidates 2, 4, 5 and 6 (2026-10-22)

Reviewer: independent (wrote none of the four). Read in `bin/worktrees/r4-proposals` at `dev` `4902dd3c` (uncommitted files):

- `draft/Proposals/2026-10-22 Raw Pointer Rules Left Open by G160.md` (candidate 4)
- `draft/Proposals/2026-10-22 Lifecycle Receivers.md` (candidate 5)
- `draft/Proposals/2026-10-22 Static Slots for P42.md` (candidate 2)
- `draft/Proposals/2026-10-22 Layout Requirements on Type Parameters.md` (candidate 6)

Checked against AGENTS.md; SPEC §1.3, §5 (intro, §5.1, §5.2.1, §5.4, §5.6, §5.7), §6.3.3–§6.3.5, §6.7.1, §7.7.2, §8.2.1,
§8.7.1, §8.15.1, §8.15.3, §11.7, §11.8, §15.2.1–§15.2.3, §15.3.1, §15.3.5, §15.3.6, §15.4.3, §15.6 (intro), §15.6.2–§15.6.5,
§16.3, §16.3.1, §22.4.3, §22.4.4, §23.1.1–§23.1.3, Appendix D; IMPL 21.1.1; `draft/Changes/2026-10-21 G160 Raw Reach and Raw
Origins.md`; DECISIONS R4-M95, R5-M39–R5-M41, R5-M52, R5-M82, BU-17; PLAN P42, G100, G161, G165, G166, G178, G191, G197, G210;
STATUS "Destruction receiver borrows", "Static initialization", "Static members"; A5 plan §5.2; R5/R6 plan OQ5; 2a0-D §3; BU's
B+ (`artifacts/handoff/2026-10-21-bu-pause-2/bu-notes/g160-12/design-final.md`, main checkout, read only); library
`DictionaryStorage.kimi`; xUnit sources for generic C-layout structs. Nothing was built, run or edited. "(H)" marks a reading of
mine that no run checked.

## Summary

| Proposal | Verdict | Blocking |
| --- | --- | --- |
| Raw Pointer Rules Left Open by G160 | **NOT ACCEPTED** (2.2 acceptable with fixes; 2.1, 2.3 and 2.4 need their recommended options redone) | RP-B1 … RP-B4 |
| Lifecycle Receivers | **ACCEPT WITH FIXES** | LR-B1 … LR-B3 |
| Static Slots for P42 | **ACCEPT** | none |
| Layout Requirements on Type Parameters | **ACCEPT** | none |

---

## 1. Raw Pointer Rules Left Open by G160 — NOT ACCEPTED

Citations are accurate: the §5.2.1 condition 3 quotation, §5.7's "one Origin atom other than an intersection", the Calls
sentence, §15.4.3's `choose`, §15.3.6's meet rule, B+'s Q-O4/Q-O5 wording (§0) and `Formed`/`RawStep` (§4), G160 §7's
"static check of D7", and §23.1.1 ("The specification requires some warnings, and an implementation may issue others"). The
problem table is a fair list of what B+ left open, and U1 is rightly classed as an implementation limit. The findings are in
what the recommended options do.

### Blocking

**RP-B1 (§2.1, option A): a valid form is lost, and "deferred" contradicts "stays Unsupported".**

1. `null` takes any Origin (§5.7 "Fits and claims"). So `b.data = null` on an owned local `b: Buffer<T>`, and `.Node(null)` for a
   Case whose payload is `raw/T during self`, fit under every reading of the destination's Origin; they are valid today. A's
   deferral (and D's error) would make them invalid, an unstated narrowing. The cost line "a Case with a `during self` payload
   can be declared (D17) but not constructed" is therefore false as written (it can be constructed with `null`; only a non-null
   source has no spelling).
2. §1.3: "Appendix D lists features outside this revision; it grants no syntax or permission." A form listed there is not valid,
   so the located `Unsupported_Kd` ("valid but unimplemented", §23.1.1, AGENTS Diagnostics) is the wrong report for it; it would
   be a Language error. "Listed in Appendix D (D.6) and stay the located `Unsupported_Kd`" cannot both hold, and §4's BU
   consequence ("2.1 A keeps B+'s U3 as written") follows the contradiction.

Redo A as a rule with a decidable verdict, for example: through no dereference the destination's raw Origin is the holder's,
which no source can name, so only `null` fits and any other source is an Origin relation error (Language), whose repair is a
write through a `uniq` borrow (`let r = b@uniq; unsafe => r.data = p@(raw/T during r)`); a Case payload has no such route, so it
takes only `null` until a revision adds Place Origins (which Appendix D.4's "static-Place Origins" line could generalize). Then
B+'s U3 becomes that Language error, and OQ-R1 states the narrowing it makes, if any, explicitly.

**RP-B2 (§2.2, option B): it revises owner decision D18 without saying so, and the problem omits substitution.**

1. G160 D18 is "No intersection is a raw Origin (§5.7, §15.3.1)", and R4-M95 records "no intersection" among the owner's raw
   Origin rules. B makes formed intersections raw Origins. That may be right, but OQ-R2 must say it supersedes D18 for formed
   Origins and why D18's reason no longer holds there (a formed intersection belongs to the borrow it is formed through, and B+'s
   `Formed` carries that borrow's mode; a written one may belong to no single borrow, so the rule stays for `during`).
2. Today's SPEC already forms intersections on raw layers through substitution: "A slot of the Field's Type is replaced by the
   value's binding" (§5.7). Counterexample: `let lens = Lens.init(choose(x@ref, y@ref))` (§5.7's `Lens`; `source` bound to the
   meet), then `lens.at` has `raw/i32 during (x and y)`; likewise `dataOf(choose(x@ref, y@ref))` with G160's
   `dataOf<T>(b: ref/Buffer<T>) -> raw/T during b`. B+ calls `Formed` at every substitution (G1), so these are U2's
   `Unsupported_Kd` too, beyond Q-O5's two forms. B's first clause ("the one-atom rule applies to Origins written in source")
   covers them, but the problem table, A's assessment and §3's mapping do not: under A as written substitution would still form
   intersections (two treatments of one concept), and if A also dropped them there, `*dataOf(choose(x@ref, y@ref)) = v` would lose
   the Authority error (D11) as well. Add the substitution route to row 2, to A's cost and to §3 ("Reading a Field" and calls).

**RP-B3 (§2.3, option A): the representation claim is false, and A rejects defined programs.**

1. "So the checker knows which values carry an own-step address of a holder" is not supported. R4-M95 and G160 §9.2 approve "a
   non-conflicting set element naming a holder", the root holder (G160 §3.1). A pointer read from a `during self` Field "reaches
   what `@raw` of the Field's storage would", so `b.data` carries the same element as `b@raw`; and §5.2.1's Local-variables example
   (`p = a@raw; consume(a@move); a = make(); unsafe => inspect((*p)@ref)`, valid) requires that element not to conflict with a
   Move of `a`. Holder granularity also over-rejects: with `p` read from `b.inner.data`, `consume(b.other@move); use(p)` keeps `p`'s
   authority (the owning value `b.inner` stays) but conflicts under "a Move of a value whose holder a live own-step address names".
   A needs a new element kind or flag (own step) carrying the owning value's Place, compared by §15.6.2's overlap rules: a change
   to an owner-approved representation and to D1's "an address conflicts with nothing", not "one conflicting operation". The
   implementation note and OQ-R3 must say so (and the vocabulary entry needs the owner, ARCH closed vocabularies).
2. Any later use of the stale pointer is defined behavior unless it accesses through it: `p == b2.data`, `p == null`, `p@usize`, or
   copying `p` into a call that never dereferences it (§5: `==` and `@usize` need no valid provenance; §5.1: comparisons "work on
   null and dangling pointers"). A rejects all of them. The cost line's "which liveness already requires (H)" is wrong: D2 makes
   the holder used wherever the address is, for Loan liveness; it never rejects a use after a Move. State the narrowing with an
   example, and weigh it against B (a Warning with the same condition rejects nothing, so its over-approximation is harmless).
   A use-only-by-access variant would need G160 §7's rejected second liveness kind, which is worth saying in the assessment.

**RP-B4 (§2.4, option A): the C-layout exemption is a proxy, its justification is wrong, and the adopted Advice is missing.**

1. `DictionaryStorage.Links` has no raw Field. `DictionaryStorage.Handle.buffer: raw/u8` is not "a foreign pointer of unknown
   provenance": it is the Dictionary's own entry buffer, filled from `Raw.allocate` and released by `Raw.release`
   (`DictionaryStorage.kimi:108, 129, 137`), holding `K` and `V` (G147 (1)'s storage). The exemption silences exactly the library
   Field the warning is for, while a Kimigayo-layout wrapper of a real foreign handle still warns.
2. Layout mode and provenance are unrelated contracts (AGENTS "Share by meaning": a similar shape under another contract stays
   separate; the symptom-fix list's shape allowlist). Unannotated `raw/T` is G160's canonical spelling of "unstated" (D3), which G160
   calls sound (§4's `Cursor<T>` row, §6 "omission means unstated, which is sound"); B's "no syntax exists to say untraced
   explicitly" overlooks that. A Warning on it leaves no warning-free spelling for a legitimately unstated Field outside C layout,
   and the offered repairs `during self` and `during static` would be false claims there (§23.1.3: a candidate lists the
   ownership or static-storage condition as undecided; AGENTS: never invent repair guarantees).
3. G160 §6 (owner-adopted, R4-M95) already plans a meaning-based diagnostic: "as Advice, a Field a `drop` releases without `during
   self`". It is not among the options. Add it (and, if a broader Warning is still wanted, a meaning-based trigger such as "the
   pointee Type may carry a Loan", condition 5's actual concern), and drop the layout exemption.

### Non-blocking

- §2.2 B: B+'s `RawStep` table has no intersection row; "any other label: Exclusive" is BU's reading. B should add the row
  explicitly (Exclusive, because `Formed` wraps every shared formation), as a B+ amendment.
- §2.2 B: a formed `raw/T during (x and y)` in a local's inferred Type is displayed but cannot be written back (§15.3.1). State
  the cost. A named local or projection already denotes such a region as one atom (`during r` for `let r = choose(…)`, `x.source`;
  §15.2.1), so only temporaries strictly need a formed intersection; worth one sentence.
- §2.1: the example `r.data = p@(raw/T during r)` is a claim and needs `unsafe =>`. "Through a dereference the target's Type gives
  the expected Origin" is B+'s reading, not SPEC text; §3 adds it, so call it an addition.
- §2.1 D: enums have no constructors (§6.3), so "a constructor is the only body that can construct a Case payload's holder, and
  none does" is garbled; and D does not say which Origin a write through no dereference expects in the Type's own bodies (an owned
  receiver `self: Self`).
- §2.3: authority also ends when the owning value leaves its holder by Take, `exchange` or `swap`, and own storage ends at
  replacement or destruction (condition 2). A covers Move only. Also say the check is defined over reach, so integer round trips
  and pointers hidden in nominal Fields stay undefined behavior, not errors.
- §2.4: the scope of "a raw Field … whose layer states no Origin" is unclear for raw layers inside Type arguments (`Option<raw/T>`),
  where `during self` is unavailable (D17).
- §3, 2.4: §23.1.1 has no list of required warnings; required warnings are stated in their chapters (§17.5, §14.3). The "(H)" can
  be resolved: the Warning belongs in §5.7 alone.
- §4: "adds its Appendix D row to STATUS" mixes the two records; STATUS records implementation limits only.
- §7: add RP-B3's representation question and the library's remaining unannotated raw Fields after G160-3 (G160 §4: `dispatch`,
  `pending` "unannotated if unknown") to the unverified list.

### Shorter or more general formulations

- 2.1: the rule in RP-B1 (only `null` fits through no dereference; otherwise write through a `uniq` borrow) needs no Appendix D
  line and no new Origin kind.
- 2.2: one sentence replaces D18's scope: "A formed raw Origin is the outer Origin of the borrow it is formed through, whatever its
  form; `during` writes one atom." It covers typed paths, `self` reads and substitution uniformly.
- 2.3: "An own-step address conflicts with every operation that ends the owning value's stay in its holder", as a Warning (B),
  covers Move, Take, exchange and replacement without narrowing; it still needs the own-step marker of RP-B3.
- 2.4: G160 §6's Advice ("a raw Field the Type's `drop` releases, or a constructor fills from `Raw.allocate`, without `during
  self`") is meaning-based and needs no exemption.

### Principles

Holds for 2.2 B. For 2.1 the "explicit semantics" claim fails until RP-B1 is fixed (a deferral that keeps an Unsupported report
is not an explicit boundary). For 2.4 Principle 1 is strained: the canonical "unstated" form would be warned everywhere but in C
layout. For 2.3, Principle 3 is fine but the stated reach of the error must match the representation (RP-B3).

---

## 2. Lifecycle Receivers — ACCEPT WITH FIXES

The §1 table of the five places is accurate (§6.3.4, §16.3.1, §7.7.2's "In a constructor or `drop`, no entry captures `self`",
§5.7's "both exclusive", §15.6.5's body-local list). G161, G163, G166 and G178 are described as PLAN states them; 2a0-D §3 row 3
lists the destruction receiver's part and helper borrow (Finite) and leaves the constructor half to G166, as claimed. The idea is
sound and option B is a fair minimal alternative. The restatement, which is the proposal's whole content, does not yet restate
every rule and adds some.

### Blocking

**LR-B1 (§2 definition and table, "Field access"): the destruction column inverts §16.3.1 and narrows `drop`.**
§16.3.1: "During the destruction of layer `D`, `Self` is `D`; **derived** layers already cleaned up are unavailable." Destruction
runs derived-to-base (§16.3.2), so in `D`'s `drop` the inherited (base) layers are still alive. The table says "inherited layers
already destroyed are unavailable". The definition also grants exclusive access only "to its initialized own Fields", while §16.3.1
gives `self` access equivalent to `uniq/Self` with "Initialized fields follow ordinary permissions" (inherited ones included).
Counterexample, valid today: `public struct Entry : Named` (§6.3.1) with `drop => Audit.observe(self.name@ref)`, reading the
inherited `name`. Under the definition and table it is at best ambiguous, at worst an error. The "own Fields" restriction and
"no inherited Field access" are construction rules (§6.3.4, §11.7); keep them in the construction column only.

**LR-B2 ("Bound of its Borrows" row; §2 "bounded as the table states"): a Loan rule is moved into §15.6.5 as a bound.**
§15.6.5's body-local Origins have one bound, the body, and "judging uses neither liveness nor reachability". §6.3.4's "Field
borrows end before completion" is a liveness point: a Loan that must not be live at completion, like §15.6.5's "a use beyond a
local Place's lifetime is a Loan conflict at its destruction or Move". Listing "before completion" as the bound of a body-local
Origin gives §15.6.5 a second kind of bound, which is a new rule (the proposal says it adds none) and would steer G166 toward the
wrong mechanism. Fix: both receivers' Borrows are body-local with the body as bound; §6.3.4's completion rule stays where it is,
as a Loan rule (completion conflicts with every live Loan of a Field of `self`).

**LR-B3 (§2): rules dropped, one possibly added.**

- Dropped: §6.3.4 "These privileges exist only in the constructor body itself, not in nested functions, methods or Field
  initializers." The definition's "In its body" does not exclude nested functions; extending the sentence to `drop` (if
  intended) is an addition to §16.3.1 and must be listed.
- Dropped: §16.3.1 "It is a destruction receiver, not an ordinary borrow value or an owner" (it decides, among other things,
  whether `self` in `drop` can serve as an Origin name like a borrow value, §15.2.1).
- Possibly added: "(not captured, stored or returned; values that carry it)" for the helper borrow. "Values that carry it" is
  BU-17's implementation reading (DECISIONS BU-17), not §16.3.1's text. If §16.3.1 gains it, list it as an addition; if it is only
  a description, remove it from the planned text.

§8's first unverified item delegates exactly this check to the review; it fails on these three points.

### Non-blocking

- Keep §6.3.4's "(including the implicit acquisition of a Receiver Expression, §7.3)" explicit in the construction column; G166's
  Receiver Expression case (`NoApplicableOverload_Kd`) turns on it, and "equivalent to `uniq/Self`" alone leaves it implicit.
- "What does not change: every verdict (H, §6)": §6 is the Principles section; the unverified list is §8.
- §1 "Atoms": the collision is PLAN G178 (found with G163's probes, c02), earlier than G160's verify-first; and "BU's unit C0c …
  (`ReceiverSlot`)" has no recorded source in B+ (whose §13 A3 says to stop and report a collision), PLAN or DECISIONS. Cite where
  it is recorded or mark it (H).
- G165 covers constructors as well as `drop` (STATUS "Destruction receiver borrows"); §1 describes only the `drop` half.
- Principle 4 ("Diagnostics name one requirement") implies a DIAGNOSTICS requirement-vocabulary entry; say whether it needs the
  owner (ARCH closed vocabularies).
- §3: given LR-B1–LR-B3, option B (one sentence in §15.6.5) delivers everything G161, G166 and 2a0-D row 3 need with no drift risk;
  the recommendation of A should say why the editorial benefit outweighs that.

### Shorter formulation

Option B, written as a root: §15.6.5's body-local Origins are those "anchored in a local, parameter or temporary root of the body,
or in the receiver of a constructor or `drop`". §6.3.4 and §16.3.1 stay as they are; §7.7.2 and §5.7 already cite both.

---

## 3. Static Slots for P42 — ACCEPT

Every row of §1 matches SPEC (§11.8, §22.4.3, §22.4.4, §15.6.2's root list quoted exactly, §15.6.4, §15.6.5). "SPEC needs no
change" holds as far as I can find: per-key storage, state and destruction; one root per Field for Loans and effects; first-write
initialization, replacement and shutdown order are all stated. §2's facts agree with BU-18/R4-M90 PQ-1 (via 2a0-D §3), G197,
G100, R5-M82 (which supersedes R5-M60's per-access row), PLAN G191 (the three `staticEnsure` treatments) and the `MirStatic` record
(`Mir/MirForms.cs:214`, keyed by symbol). The two A5-plan narrowings and their milestone costs are quoted correctly, and rejecting
them follows AGENTS (never narrow the SPEC for implementation reasons). `TyCtxt.Erase` (R5-M39) erases exactly what §22.4.4
erases.

### Non-blocking

- §2's closing sentence ("the key would exist three times (MIR, A4/A5, code generation)") contradicts §3 item 1 ("A4's and A5's
  keys read the Field and need no change"). The three places are MIR, code generation and the runtime.
- §3 item 1: MIR is generic, so `staticEnsure` can carry only the open key (the Field and the enclosing Subst in the body's own
  Type parameters); the closed key exists after `TyCtxt.Compose` at admission (R5-M40). Also "MIR's … StaticInit body" is one per
  Field, instantiated per key; it is not identified by the key.
- Cite PLAN G191 for "2b-7's side finding; a validator rule pending".
- §8: add "(H) `Erase` keeps unused enclosing arguments", since §22.4.4 says unused arguments distinguish keys and R5-M39's null
  rule reads `Instantiates` (Milestone 42's `Cache<T>.Statistics.count`, which does not use `T`, is recorded by R5-M82, which
  suggests it does).
- Option A fixes a shared representation from one design; the package's MIR part still takes the full process (two designs), and
  AGENTS Q6's lighter form does not apply because this proposal has not passed the full process. Say so, so that A is not read as
  lightening the package.

### Shorter formulation

"A static slot is the collector's instance of the Field declaration: (Field, erased enclosing Subst), R5-M40's key." This is item
1 in one line; nothing shorter preserves the single owner.

---

## 4. Layout Requirements on Type Parameters — ACCEPT

The citations hold: §8.15.3, §6.7.1's quoted sentence, §8.7.1, R5-M41's allocation figures (0.15/1.29/2.31/4.61 MB; 0 B warm;
about 2 KB retained per item from 3.56 MB for 1,794 items), R5-M44's 472 KB, OQ5's answer ("IMPL 21.3.3 requires a report for
every instantiation of the checked set"), R5-M52 with the owner's 「新しい言語エラーのコードを追加して下さい」, and KD12 (R5-M40). The
withdrawn item's reasoning is right: reclassifying `OversizedLayout_Kd` as Resource reverses R5-M52 and IMPL 21.1.1, and the
collector stays at `check` for the other records, so A′ saves no cost. The library's two C-layout structs are nongeneric; the xUnit
list matches the generic C-layout structs I found (LayoutTest, LayoutAttributeBindingTest, InstanceCollectorTest, InlineLayoutTest,
LibraryImportTargetBindingTest; ForeignEmissionTest not checked). D follows: no narrowing, no vocabulary entry.

### Non-blocking

- §5 understates A′'s narrowing. A declaration's Constraints are public well-formedness obligations (§8.15.1), proven wherever
  the Type is formed (H: my reading; no sentence exempts formation behind a borrow), so under A′ a nongeneric `func h(p: raw/P<()>)` or `ref/P<()>` becomes a Constraint failure, although §8.15.3 says forming a Type
  "only behind a borrow, raw pointer or object handle … does not check" layout obligations. Add it to §5 (A′ is not recommended,
  so this only affects the record).
- §3 item 2: "a Field Type [that] names a Type parameter" should be "whose size may depend on a Type parameter" (`Wrapper<T>`), and
  proving `CField` for such a Field needs structural recursion over sizes (not over C-layout eligibility); `ref/T` and `(T, i32)`
  are never zero-sized and should need no premise.
- §4 C: its Resource half also conflicts with §6.7.1 and §8.15.3 (a zero-sized C-layout Field is a Language error), not only with
  R5-M52 and IMPL 21.1.1. "Never only a Resource record" is R5-M52's wording; IMPL 21.1.1 says "a Language error at the use".
- §1: "the only obligation that depends on a Type argument" holds for C-layout restrictions only; size limits also depend on Type
  arguments (`InlineLayoutTest.cs:111`), which §3's withdrawn item treats. And "g's error is a record of the instance collector at
  `check`" holds under MIR code generation; the collector does not run on the default pipeline until G2 (R5-M41), so say which
  pipeline (H for the default pipeline's reporter).

### Shorter formulation

D alone: "No change; the A5 plan §5.2 item is not adopted (Resource half: R5-M52; declaration half: narrows generic C-layout
structs for one record kind)." The proposal already says this; §3 and §5 could shrink to a footnote.

---

## 5. Cross-cutting

- "(H)" discipline is good in all four, except RP-B3 (an unmarked, wrong representation claim) and LR's C0c/`ReceiverSlot` claim.
- Each proposal states rationale and Principles, as AGENTS requires for proposals. None reopens a SETTLED entry: the constructor
  entries ("A conditional implicit constructor over an Unknown base premise"; "Constructor delegation, whole-`self` placement or
  a factory") are untouched by Lifecycle Receivers, and no entry covers raw Origins, static keys or layout requirements.
- Owner questions: OQ-S1/S2 and OQ-Y1 are decidable as written. OQ-L1 is decidable once LR-B1–LR-B3 are fixed. OQ-R1, OQ-R3 and
  OQ-R4 are not yet decidable: their recommended options state wrong consequences (RP-B1, RP-B3, RP-B4); OQ-R2 needs D18 named and
  the substitution route included (RP-B2).
