# Review: the later SPEC session's options for the items after N2

Date: 2026-10-25. R4's reviewer slot. I wrote neither `options.md` nor its probes. Base: `origin/dev` `a56a309d0`, then
`230938d7a` (G248's merge, R4-M129). `origin/dev` is now `3319d5db2`, which adds only a PLAN commit to `230938d7a`. I checked
the options against N2's SPEC text on `origin/claude/r4-spec-n2` `81d714bc2` (R4-M131, not yet on dev), R4-M116, R4-M129 and
R4-M130. My probes are in `rev-probe\` here: `cli-dev2\verdicts.txt`, `out*\`, `native\`. My worktree
`bin\worktrees\spec-rest-rev` is removed. I changed no SPEC, code or test, and made no commit.

## Verdict

**REVISE.** Six findings block the document as it stands:

- **B1.** Q4's recommended RZ7 option (a′) is unsound. An earlier recorded reading is missing from the document.
- **B2.** Q9 leaves out pair parameters and the call-side instantiation of a name, and either changes the recommendation.
- **B3–B5.** Q8, Q10 and Q12 are already settled, but the document presents them as open.
- **B6.** G253's O1 is missing; it belongs with Q11.

The other recommendations hold, some with wording fixes. Corrected list: ten owner questions, Q11 in two parts.

**The designer's probe verdicts still hold.** All of them reproduce on `a56a309d0`.

At G248's merge (current dev), the F1 verdicts change as R4-M129 intends:
- sel prints `one show two` for `g248-f1-if2`, the same as old;
- A5 accepts `g248-f1-arm`;
- A5 rejects `borrow-if` and `borrow-do` (`ComparisonLoanConflict_Kd`).

The F2 verdicts do not change:
- `g248-f2-default` still differs (old `two show two three one`, sel `three two show two one`);
- A5 still accepts `g248-f2-guard`, and the old path still rejects it.

## Blocking findings

**B1 (RZ7, Q4): (a′) leaves a forged write into the body's own declared Place defined.**

The probe `rev-probe\src\rv-rz7-leak.kimi`:
- `func rz7Leak(a: ref/i32, b: ref/i32) -> ref/i32 during a` declares `var r: ref/i32 during a = a`.
- It forms `q = forgeL<ref/i32>(r@raw@raw/u8)`, writes `unsafe => *q = b`, then `return r`.

What happens today:
- Binding and A5 accept it.
- The caller's rows are `0 Value result`, so `y = 8` is accepted while the result is used. The result reads `y`'s referent.
- `rv-rz7-leak-param` does the same through an owned parameter's Field and is accepted too.

Under the letter the write is defined:
- designation treats Origins as equal (§5.2);
- conditions 1–5 hold, and `q` reaches `r` through `forgeL`'s untied result.

(a′) covers only "storage reached from its inputs". `r` is the body's own holder, and §15.6's carriage does not help, because a parameter's Loans have no access path in the callee. By reasoning, (a′) does not cover static storage reached by name either. I could not probe that: a `ref` static is `Unsupported_Kd` today (`out-static\`).

Option (a), "a value written or placed through a raw Place into a Place outside raw storage must fit that Place's Type, Origins included", makes all three undefined. It is not stricter where it matters:
- It requires only the fit a safe write through the Place's path would need. `rz7-local`-style writes whose Loans are tracked stay defined when that fit holds.
- For a Place reached through an input, the Type is the one the contract gives it. The callee decides the fit from its own signature (Local Reasoning). This is the same kind of promise a §5.7 claim makes.

The document also omits R4-M116's recorded observation (the re-review's R-N2): "a callee that stores a converted pointer where its contract's Type cannot hold the argument breaks its contract (undefined behavior); no text change". R4-M131 superseded it only at unstated positions.

**RZ7 is therefore not settled by N2.** N2 governs raw layers without an Origin; `rz7-other`'s target is a stated `ref/i32 during c`. The reading for stated positions is recorded without text, and (a) or (a′) gives it text. Placement: N2's Retained dependencies paragraph (still on the N2 branch) keeps raw storage. The new sentence goes beside the §5.2 designation rule, and the R4-M116 observation gets a superseding DECISIONS note.

**B2 (§15.4.2, Q9): a missing consideration that changes the recommendation.**

(a+) is motivated by "a function whose signature has no borrow to name cannot state `T is ref/U` at all". For the outer layer it can: SPEC's canonical form is a pair parameter, whose outer Origin is "one fixed Origin of the body ... with no source name" (§8.1.1).
- `rv-e5-pair-item`, `func f<s/U>(x: s/U, item: U) -> U` with `s is ref`, is clean today.
- `rv-e5-pair-result` (`-> s/U`) is clean too.

(a+) also needs a rule it does not state: how a call instantiates a name that appears only in a Constraint. Constraints are not part of the Signature (§7.5), and §15.3.4 quantifies only names in a named callable signature. Today even a signature-bound name cannot be proven at a call:
- `e5-ref-sig` and `e5-ref-other` give `UnprovenConstraint_Kd`;
- so does `rv-e5-same-borrow`, which passes the same borrow `b` for both arguments.

So a Constraint-only universal would be unprovable at every call until that is fixed (G170's identity unit).

If omission is wanted for nested positions (`T is Option<ref/U>`), the alternative to compare is (c): the omitted Origin is the subject's own Origin at that position, an unnamed fixed Origin of the body, as a pair binder's is.
- No call-side solving is needed.
- `T is ref/U` and `T is ref/i32` agree without a name.

(b) still needs names for two Constraints, as the document says.

I recommend (a), the row "must be complete from explicit information". The pair parameter is the outer-layer form, and nested positions name a signature-bound Origin. (a+) or (c) can come later as an explicit extension.

**B3 (Q10): settled.**
- R4-M130 on dev says: "The mode is the Subject's, so a ResolvedRange Subject through a captured `uniq` (mode Exclusive, its interval read once by 14.6.2) is Exclusive too".
- 2c-1 is merged (`a228d3ed7`, with `124e2a47c` "a `for` Subject is Exclusive by its published mode").
- `rv-c-range`, the m01 form with a `let` Closure, gives `InvalidAssignment_Kd`, so the Closure is Exclusive on dev.

Option (b) would contradict the owner's answer. Drop Q10; "whatever the iteration entry does" goes into item 6's wording. The document's item 6 Effects paragraph ("2c-1 ... not merged; under (a) ... m01 becomes Exclusive") is stale.

**B4 (Q8): settled by the letter.**
- §8.15.3: "Forming the Type only behind a borrow, raw pointer or object handle ... does not check them. A failed obligation is a Language error at the use that needs the layout."
- §6.7.1 makes "a use that needs such a layout" the error.

(b)'s only reason is the implementation's declaration check. That narrows SPEC to fit the implementation (AGENTS), so it is no owner option.

G158 (3) is an implementation issue:
- BU: `Binding.Attributes`' `ValidateLayoutFields`, `ValidateCLayoutFields` and `CheckCLayoutInstance`.
- R5: `Layout`'s CLayout outcome gains the open, derived and empty cases.
- A planning entry supersedes R5-M55's "Binding's declaration check is lasting (X26)", whose reason was duplicate records, not SPEC.

The optional clarifying sentence is unnecessary: §8.15.3 already says it.

**B5 (Q12): settled by R4-M129** (on dev since `230938d7a`): "An if clause's and a do's body has a scope of its own, as a match arm's ... (SPEC 14.2.1, 16.2.2 step 3)". Every F1 probe now agrees across pipelines (Verdict above). The §3.6 clarification is editorial and records a decided reading, so it needs no owner decision; it goes into the SPEC edits with their review. A more general wording fits better: "A Body's items are outermost expressions (§14.2.1)". It also covers Discard Context and `defer` bodies.

The document's claim (§9, and verdicts.md) that "the verdict probes (A5, old) do not depend on that order" is wrong for `borrow-if`, `borrow-do` and `g248-f1-arm`. Their A5 verdicts changed with R4-M129.

**B6 (with Q11): G253's O1 is missing.** §16.2.1 orders "each placed component of an unfinished Tuple, array or enum Case construction". It does not cover a Dictionary literal's inserted entries.

The old generator, at `230938d7a` (`rev-probe\native\g248\`):

| Probe | Interrupted literal | Old generator |
| --- | --- | --- |
| `rv-o1-tuple` | Tuple `(pass(Noisy.init(1)), Noisy.init(try n))` | `two one` (sel too) |
| `rv-o1-array` | Array literal, same entries | `two one` (sel: `Unsupported_Kd`) |
| `rv-o1-dict` | `[1: pass(Noisy.init(1)), 2: Noisy.init(try n)]` | `one two` (sel: `Unsupported_Kd`) |

The MIR (R4-M129: "places its entries in a temp of its own, made at the empty Dictionary") gives `one two`, which `ADictionaryLiteralsEntriesArePlacedInAValueOfItsOwn` asserts.

Options:
- **(a) per entry:** add Dictionary literals to §16.2.1's list. Each inserted entry is destroyed at its insertion point in reverse acquisition order, as Tuple and Array elements are.
- **(b) one temporary:** the unfinished Dictionary is a temporary acquired where the literal begins, holding its entries.

I recommend (a), one rule for literal components (One Concept):
- Both the old generator's and the MIR's Dictionary order change.
- The MIR test's expectation changes, citing the owner decision as the reason.

## Findings per item (non-blocking)

**RZ1 (Q1): agree (a).**
- The one I tried is no better: B makes the safe write the undefined one.
- Wording: condition 3 can name the capabilities by §15.1.5's operation table ("Shared borrow: Read ... Exclusive borrow: Read and Write"), with no second list. "For as long as it ... is used" repeats §5.2.2's existing sentence; drop it.
- Consistent with dev: `rv-c-rawuniq` is Exclusive.

**RZ3 (Q2): agree (a).** It is the letter: §5.2.3 is specific, and §5.2.1 (3) and §5.7 list in-place destruction apart from Take. (b) narrows SPEC to fit the implementation, and (c) adds a second form. `rz3-inplace` reproduces on dev: old and sel both print `moved moved end`.

**RZ5 (Q3): agree (b), with three wording fixes.**
1. Padding bytes. "the innermost Initialized Place that holds it" makes changing a non-punnable struct's padding undefined (it is defined today). Exempt padding ("unless the byte is padding").
2. Stated Origins. A punned read of a raw pointer whose Type states an Origin should make §5.7's claim, or the list should take only raw layers without `during`.
3. Field-by-Field punning. A Field projection's base designates nothing, so `(*p).f` puns a punnable Field even when `*p`'s Type is a non-punnable struct. That keeps C-struct byte parsing possible Field by Field. State it, or add an example, so readers do not think FFI parsing is lost.

The library census (only `isize` and designated Types) holds.

**RZ7 (Q4):** see B1. Recommend (a).

**Element storage (Q5): agree (a), with a wording fix.**
- The draft names "a dynamic collection's element storage" among Places owned "through own steps (§15.6.2)". §15.6.2 defines own steps only for `obj`/`rc`/`arc` payloads, and §5.7 adds `during self` storage.
- If element storage became an own step, §5.2.1 (3)'s third limit and the `OwnStepDeparture_Kd` Warning would also apply to element pointers: authority would end at a Move of the Array, contrary to RZ6's `allocate` authority.
- Write: "...or of the dynamic collection whose element storage it acts on".

**G86 (Q6): agree (a).** It is the letter's most consistent reading:
- §15.3.5 computes requirements "in terms of its own Type parameters", which includes the pair binder;
- §5.7 and §15.6.1 judge per admitted case;
- §8.15.2: "An instantiation is the case its arguments select";
- SETTLED rejects one premise set for all cases.

The edit must also amend §15.3.5's Fixed points sentence (one value per declaration per admitted case). `g86-static-uniqonly` (`s is uniq`, accepted) is a Binding defect under every option. File it now, apart from the question. The implementation fact holds (`Binding.TypeOrigins.AccumulateRequirements`: `IsExclusive(pair)` is false, so `Ref`).

**G159 (2) (Q7): agree (b), with the wording generalized.**
- The draft lists "Tuple, array or Dictionary literal" sources. A nested selection all of whose sources await is not in the list: `rv-g159-nested` is `TypeMismatch_Kd` ×2. Word it over the sources that supply no candidate, the parts compared as sources are.
- New defect under the current letter: `rv-g159-nested-cand`, `if c => (if d => (a, (return 1)) else => ((return 2), a)) else => (a, a)`, has the candidate `(i64, i64)`. §14.9.2 ("Nested results propagate the same way, and a literal result is not fixed before outer constraints") makes it valid; Binding gives `TypeMismatch_Kd` ×2. Record it in G159.
- `rv-g159-match`, the `match` twin, behaves as the `if` does.

**G158 (3) (Q8):** see B4. Drop it.

**§15.4.2 (Q9):** see B2. Recommend (a).

**Item 6, the Closure wording.** The wording is right apart from three gaps:

- **The raw list.** It says "a write through it (`*p = v`, `p[n] = v`, a compound assignment), a Take or an in-place destruction". Under RZ1 (a) it must also include an exclusive borrow of a raw Place, and pointers derived from one read from the environment. §5.2.1 (3)'s first limit covers "every pointer derived from it". Shorter: "an access through it, or through a pointer derived from it, that needs write capability (§5.2.1 (3))".
- **The document's "2c-1 implements both readings" overstates.** On dev:

  | Probe | Form | `let` Closure call |
  | --- | --- | --- |
  | `rv-c-rawwrite`, `rv-c-rawcompound` | `*p = 2`, `*p += 2` | Exclusive (`InvalidAssignment_Kd`) |
  | `rv-c-rawuniq`, `rv-c-rawuniq-write` | `(*p)@uniq` | Exclusive (`InvalidAssignment_Kd`) |
  | `rv-c-rawtake` | `consume((*p)@move)`, a Take | Shared (clean) |
  | `rv-c-rawdestroy` | `_ = (*p)@move` | Shared (clean) |
  | `rv-c-rawcopyptr` | `let q = p + 0; *q = 2` | Shared (clean) |

  The Shared rows follow the owner's rationale ("under a Shared call the write would always be undefined") but not today's implementation. Rejecting their `let` calls is an acceptance change, so I ask it as Q10′.
- **Open issue (no rule).** A callee writing through a captured pointer the body passes it: `rv-c-rawinit`, `Raw.initialize(p, 5)` in a `let` Closure, is clean (Shared), so the placement is always undefined. No receiver annotation exists to choose Exclusive. Options:
  - the author's obligation, stated;
  - an operation contract's write (`Raw.initialize`) counts;
  - passing a pointer read from the environment to an unsafe call makes the call Exclusive.

  File it in PLAN; it is not for this round.

**G248 F2 (Q11): agree (a).** One correction to the argument. Under (b), "Inspection Loans end before callee entry" would read as an obligation, not as false: `g248-f2-guard` becomes an error, as the document's (b) says. (a) is still the better reading: the sentence is declarative, a default stays self-contained in the declaration's scope, and reservations activate cleanly (§15.6.7). `default-ref` is weak evidence, since the declaration-scope check against a universal parameter Origin also explains it.

## Corrected owner questions

| # | Question | Options | Recommendation |
| --- | --- | --- | --- |
| Q1 | RZ1: what capability does a borrow of a raw Place need? | (a) shared: read; exclusive: read and write; for as long as §5.2.2 says; (b) only each access made through it | (a): under (b) the undefined behavior lands at a safe write |
| Q2 | RZ3: `_ = (*p)@move` | (a) destroys in place, raw Places only (the letter of §5.2.3); (a′) every `_ = P@move` in place; (b) a transfer, as both pipelines do | (a): the letter, one form, no change to MIR tracking |
| Q3 | RZ5: which Types may a pun read or write? | (b) a closed punnable list (with the padding, Origin-claim and Field-projection wording); (a) Copy, "valid" meaning a held live value; (c) one rule per class | (b) |
| Q4 | RZ7: a value written through a raw Place into a declared Place | (a) must fit that Place's Type, Origins included, judged through the writer's contract for Places it reaches from inputs; (a′) the contract rule for storage reached from inputs only | (a): (a′) leaves `rv-rz7-leak` (own Places) and static storage defined while the caller trusts the contract. Records R4-M116's R-N2 in text |
| Q5 | Do a dynamic collection's Loans cover its element storage under condition 4? | (a) yes ("or of the collection whose element storage it acts on"); (b) authority alone | (a), without classifying element storage as an own step |
| Q6 | G86: the requirement of a slot used only by a conditional pair slot | (a) per admitted case; (b) the strongest admitted case | (a): (b) rejects valid owner instances (`g86-static-owner`) |
| Q7 | G159 (2): the common Type when no source supplies a candidate | (b) formed part by part, nested selections included; (a) own Types (an error); (c) Never | (b) |
| Q9 | §15.4.2: an omitted Origin in a Constraint's Type outside a Function Type | (a) explicit (a pair parameter is the outer-layer form); (a+) explicit, unbound names in a function's Constraints quantify, solved at each call; (c) omitted means the subject's own unnamed Origin there | (a) now; (a+) or (c) later as an explicit extension |
| Q10′ | Closures: does the owner's raw-write reading cover every access needing write capability (Take, in-place destruction, exclusive borrow) through a pointer read from the environment or derived from one? | (a) yes; (b) only `*p` and `p[n]` writes on the pointer itself | (a): same rationale. 2c-1 then extends to `rv-c-rawtake`, `rv-c-rawdestroy` and `rv-c-rawcopyptr` |
| Q11 | Temporaries G248 left open: (F2) when an omitted default's temporaries end; (O1) cleanup of an interrupted Dictionary literal's entries | F2: (a) before callee entry; (b) through the call. O1: (a) per entry, as Tuple and array components; (b) with the Dictionary at its start (both pipelines today) | F2 (a); O1 (a) |

Dropped:
- **Q8:** the letter is explicit; an implementation issue for BU and R5.
- **Q10:** R4-M130 and 2c-1 settle it.
- **Q12:** R4-M129 settles it; the sentence is an editorial edit.

## What each SPEC edit would touch

Base the RZ edits on N2's text (`origin/claude/r4-spec-n2`), which rewrites §5.2.1 condition 5, Retained dependencies and §5.7 Calls.

| Q | SPEC | Elsewhere |
| --- | --- | --- |
| Q1 | §5.2.1 condition 3 (borrows by §15.1.5's table) | DECISIONS. No Binding, MIR or library change |
| Q2 | §5.2.3 (in place, at statement temporary cleanup); §14.2.4 (the raw-operand exception) | HIR discard classification; `MirBuilder.Discard` (after G248's hold); sel's Drop through the pointer (R5); STATUS raw row (the old generator transfers until R6-check); a MIR golden; the `rz3-inplace` fixture under sel |
| Q3 | §5.2 Type-punned access (list, provenance, write limit, padding); the §15.1.2 counterpart of "a punned Place holds a complete value" (raw draft's N13); a §5.5 cross-reference | — |
| Q4 | §5.2, beside designation: the write-fit sentence; Retained dependencies keeps raw storage | DECISIONS superseding R4-M116's "no text change" for stated positions |
| Q5 | §5.2.1 condition 4 | — |
| Q6 | §15.3.5 (Loan requirements, Phantom, Fixed points); a cross-reference in §8.1.2 Annotation | Binding representation (BU, full process): `OriginParameter` per case; consumers `ValidateOriginRequirements`, `RawStep.Shared`, `RawMembers`, retention; the `s is uniq` defect now |
| Q7 | §14.9.2 step 2 (one sentence, an example) | BU: `ResultCandidates`; the `rv-g159-nested-cand` defect in G159 |
| Q9 | §15.4.2 table row (a). For (a+): also §15.3.4 and §8.2.3, with call-site instantiation | (a): no Binding change |
| Q10′ | §7.7.3 table and paragraph, §7.7.2 sentence, §15.1.6 sentence (the document's wording plus the derived-pointer and exclusive-borrow fixes) | `Binding.ClosureReceivers` (a 2c-1 follow-up); the DIAGNOSTICS Note wording; PLAN: the callee-write open issue |
| Q11 | F2: §7.2.3 and §3.6. O1: §16.2.1 Interrupted expressions (Dictionary entries), cross-referenced from §12.4.3 | F2: STATUS (the old generator until R6-check); `MirBuilder.Defaults` unchanged. O1: the MIR Dictionary literal (R4-M129's own temp) and the old generator; `ADictionaryLiteralsEntriesArePlacedInAValueOfItsOwn`'s expectation (owner decision) |
| (Q12) | §3.6 "A Body's items are outermost expressions (§14.2.1)" | — |
| (Q8) | none | BU `Binding.Attributes`; R5 `Layout`; superseding entry for R5-M55's lasting clause; STATUS |

## Unverified

- Everything at undefined-behavior level is reasoned (RZ1, RZ3's semantics, RZ5, RZ7's rules, Q5). No checker exists.
- The static-storage hole under (a′) is reasoning only: a `ref` static is `Unsupported_Kd` today.
- Options (a+), (c), G159 (b) and O1 (a) have no prototype.
- The MIR's own order for an interrupted Array literal was not observed: sel cannot build it.
- 2c-1's coverage was probed only through the `let`-Closure `InvalidAssignment_Kd` signal on the CLI.
