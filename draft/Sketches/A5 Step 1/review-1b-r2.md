# Independent review, round 2: A5 step 1's 1b design diff (B1, B2 redone; the records)

Reviewed: `bin/worktrees/a5-step1` (`b06aeaaf` plus the uncommitted records, G121 renumbered to G123) against SPEC §15.6,
§15.6.6, §15.6.7, §14.10.3, §3.1.3, the MirBuilder/MirInit code and `design-1b-mir-B.md`; I wrote none of it and edited or
built nothing. Evidence: `proto.cs` (round 10 + cases 120–127b and a `DestroyAt` node; 246 cases; round 10's 234 rows
unchanged), `run.txt` (`dotnet run proto.cs`, exit 26 as before), `rust/r109.rs` (rustc 1.97.1, `--cfg c<case>`).

## 0. Verdicts

| Item | Verdict |
| --- | --- |
| 1. B1 redone (E5 own-value seed) | **Fix listed text.** E5 is complete for its family on `CS` and `CS_call` (120–127b). Blocking: A3's `err(destroy)` row is vacuous, so a destroying call misses what E5 seeds (123), and round 10's encoding does not exercise it (F1). |
| 2. B2 redone (Never value, `Replace`) | **Accept the design; fix listed text.** The code paths agree. R4-M52 and E9 leave out four decided conditions, and choice §2 contradicts the chosen MIR-B on `Overwrite` (F3, F4). |
| 3. Records | **Fix listed text.** One wrong record: 109 is not a SPEC gap, because Rust rejects it (F2). There are also stale and unmarked A3 lines, misattributed and unnamed supersessions, and choice-1b's status (F5–F8). |

Run (246): A 2 UNSOUND/23 listed/197 same/24 moves; PrefixKill 2/7/216/21. Both UNSOUND are 120b and 123 (F1), not E5.

## 1. Item 1: the own-value seed (E5)

General rule (SPEC 15.6:455, holders per root by :453): at ℓ, a holder T is used after ℓ when some value used after ℓ
carries a direct Loan on T. The values used after ℓ that are in no live holder's entry set are: the Ref ℓ forms (its own
Loan and its R2 parents); at an activation, the reservation holder's value (excluded by the activation sentence, but it
still reaches other values); and markers born at ℓ (`Φ_c`, `γ_H`, `τ_s`), which name no holder. `Φ_E` names E, which
both sets already contain. E5 seeds exactly the first two; stored flows are already covered by transit plus the closure.
New cases (SPEC verdict by §15.6, then Rust; the result is the same under A and PrefixKill):

| Case | Form | SPEC / Rust | A, PrefixKill | E5 needed (ablation) |
| --- | --- | --- | --- | --- |
| 120 | 100 stored into a part of h, used after only through `p = h.a@ref` (MIR: value temp, then Move) | error / E0502 | rejected at the Reborrow | `FreshOnly` UNSOUND |
| 120b | 120 with the Ref formed directly into `h.b` | error | **accepted** | see F1(c) |
| 121 | writeback through a reference: `let r = s@uniq; r.r = r.n@ref; r.n += 1` | error / E0506 | rejected at the activation | `FreshParents` UNSOUND |
| 122 | retaining store: `stash(into: h@uniq, item: r@follow@uniq)` | error / (100d) | rejected at the call | `FreshParents` UNSOUND |
| 123 | `s.q = s.r@follow@ref; replace(s@uniq, with: S(…))`, S's drop writes `self.r`, s dead after; round 10's encoding (a Drop on `CS`, as 26a) | error / E0713, E0502 | **accepted** | — |
| 123b | 123 with the destruction on `CS_call(ℓ, $t)` (A3's `err(destroy)`) | error | rejected at the call | `FreshParents` UNSOUND |
| 123c | 123 with s used afterwards | error | rejected | no |
| 124/125 | loop: the self-reference made after the Reborrow, second iteration / shared Reborrow | error, valid / E0502, ok | rejected at `let q` / valid | no |
| 126 | γ: the candidate stored into `holder.saved`, used after only through `p = holder.a@ref` | error | rejected at the cleanup | `EndedByLoan` UNSOUND |
| 127/127b | E8: `var r = s@uniq; r.r = r.n@ref; r = s2@uniq; s.n = 5` with / without `inspect(s.r@follow)` | error, valid / E0506, E0506 | rejected at `s.n = 5` / valid | no (the parent Lr kept in s saves it) |

No unlisted rejection. 127b and 109w (`s.n = 7`) are SPEC-valid; Rust, the stricter side, rejects them.
**F1 (blocking, text).**
(a) A3 §5:296-299 `err(destroy)` requires `hits(X, place(L), obs) ∧ ¬hits(X, place(L), uborrow)`. By §5's own `hits`,
uborrow is deep, so every obs hit is also a uborrow hit and the row never fires, with or without E3. r3 N4's intent
("only the obs part beyond the shallow activation") is `¬hits(X, place(L), write)` for an `InPlace` target, the
activation's own kind (`clear` stays `uborrow`). As written, A3 accepts 123, and also 26a, which SPEC 15.6.6 lists ("in a
call that destroys a target, such as `replace`").
(b) Round 10 encodes a destroying call as a Drop checked on `CS` (26a), not on `CS_call(ℓ, t_K)`, so its "no unsound
acceptance" never exercised E5's `CS_call` seed at a destroying call. Review r1's "E5 also covers `err(destroy)` (H)"
holds only with (a) fixed: 123b is rejected, and is UNSOUND without the `CS_call` seed. Fix: add an erratum for the row's
exclusion kind, mark §5:296, and add 123/123b (or `DestroyAt`) to the prototype's cases.
(c) Non-blocking: E5 says "a live holder", and the prototype seeds only for `live_out`. 120b shows "live" must be transit's
(A3 §5: "in live_out(ℓ), or made live by closure"). No builder form produces 120b today (`Replace` secures into a temp,
Values.cs:345; aggregate parts go through temps), so this is a wording fix. Shorter: E5's "(or `E` for `Φ_E`)" is vacuous (`σ_in(E)` is in both sets).

## 2. Item 2: the Never value and `Replace` (G92a, E9)

Checked against the code. `x = stop()`: `Replace` (Values.cs:345) types the value temp by the target only while the
target completes; with B2's fix a right side of Type Never gives a Never temp, `Assign`→`Into` (:125) builds the Call into
it, a diverging Call writes no destination (`MirDescriptors.cs:270`, `Target < 0`), and `Overwrite`→`Use`→`Set` (:398)
sees only that Never input, so x is unwritten. `x = (return 1)`: the same through the transfer. `let x: i32 = stop()`:
`Let` (MirBuilder.cs:381) uses `Assign(new(local), …)`, so the Call's destination is x itself, unwritten, and C3 stays
`UninitializedPlace_Kd x` (MirInitTest:150). `x = (return 1) + 1`: the right side has Type i32, so the temp is i32; the
Add has a const input, so it is built and x is written, as MIR-B states. Its unwritten Never input is a temp, which A1
never reports (`MirInit.cs:262`, Var/Arg/Return only). `Value` (Places.cs:282) gives a Never node its own Never temp, and
other temps are typed by their nodes. The one destination-typed exception, a default's pending slot (Defaults.cs:106),
takes `Move(slot)` once `Reached` goes, which is harmless: a temp, no Loans, and it agrees with its parameter.

**F3 (fix listed text).** R4-M52 (DECISIONS:1146) and A3 E9 (:602) state the predicate without the conditions that the
choice and review r1 decided: (i) "that has an input" (otherwise a no-input rvalue is vacuously "all Never");
(ii) `Replace`'s Never-typed temp; (iii) MIR-B's `Overwrite` row (design-1b-mir-B.md:42, :60), "the old value at `=` is
then not dropped either"; (iv) that the predicate governs a placement rvalue (`Set`), not a Call. `Reached` goes, and a
Call is built with its Never operands (MIR-B:59), except the three skip sites of MIR-B:62. Read literally, R4-M52 does not
build `f(stop())`; then `let y = f(stop()); return y` becomes `UninitializedPlace_Kd`, and f's effects go unchecked.

**F4 (fix listed text).** choice §2 G92a says "Deletes … the non-completion gates (Set, **Overwrite**, …)". The Drop has
the target as its input, so it is then built: `var b = Box(…); b = stop(); consume(b@move)` drops b on the check-only
continuation, and A1 reports `UninitializedPlace_Kd` (a DropUse marks the path uninitialized, `MirInit.cs:181`), where
today and MIR-B report nothing. That contradicts "as today" and "B's placement rule". State `Overwrite` as MIR-B's
predicate and add the row `b = stop(); consume(b@move)` to the C3 test.

## 3. Item 3: the records

- **F2 (blocking, wrong record): 109 is not a SPEC gap.** rustc rejects `s.r = &s.n; s.n += 1` with E0506 ("borrow later
  used here"), and the plain write too (109w); 100, 100d and 101 are E0502 as review r1 guessed (`rust/r109.rs`). Wrong
  text: choice-1b §1 own-value row ("Rust accepts it (SPEC gap, §5)") and §5:73; R4-M52 (DECISIONS:1152, "15.6's case 109"
  among the gaps for the SPEC session); prototype.md:827; A5RulesPrototype.cs:814's case title. Remove 109 from the gaps;
  optionally record that SPEC accepts 109w while Rust rejects it (precision, not soundness). choice §5's "Rust agrees" on
  `g(s@uniq, s.r@follow@ref)` holds only for the two-phase form: `s.h(&*s.r)` compiles, `g(&mut s, &*s.r)` is E0502.
- **F5 (A3 stale lines without markers).** These still assert the last-dereference base or the Loan-based end: §1:15
  ("named by base"), §1:25-26 ("The kill never crosses a Deref (plan base: … last Deref)"), §3:98 (`base(L, B)`), §4:168
  (the `base(L, B)` pre-pass row), §6:369 ("CSR by … base root"), and §4.1:202-203, the `EndBorrow` meaning "every Loan
  issued into it must be dead … the guard-local Origin end for guards", which E7 replaces. They need (E8)/(E7) markers.
  Also unmarked: the step classes §3:83/:88 (E6: a Slice handle's Index is a borrow Deref, not "Index" inline), §5:313's
  excuse row (E2) and §1:22-23's activation set (E5). §9 states item 11 as text, but its table has no row 11.
- **F6 (attribution).** E9's heading "(owner, …; supersede parts of R4-M37 and R4-M40)" also covers G123, G94 and G91,
  planning-session decisions (§4 asked only G92a, G92b): split it, G92a/G92b owner (R4-M52), the rest planning session.
- **F7 (supersessions not named).** R4-M37's header names only G5's path clause and G6's value rule, but its restated
  `EndBorrow` meaning ("every Loan issued into it must be dead"), which the owner approved as §9 item 8, is superseded by
  E7, a planning-session decision. R4-M40's handle copy and its Open are amended by G123 and G94, and R4-M40 has no note
  at all. AGENTS ("departing from one adds a superseding entry naming the reason") needs at least header notes now,
  pointing to A3 E7/E9 and to the units that record the entries; alternatively, R4-M52's last line can name which parts
  are planning-session decisions recorded at implementation.
- **F8 (choice-1b / R4-M52 consistency).** choice-1b's status still says "owner decisions pending (§4)". §4 lists three
  questions, while R4-M52 records four answers (the fourth, 「別セッションで実施」, asked outside §4) and calls them "the
  choices of choice-1b.md §4". R4-M52's conditions omit choice §4's "A3's matcher is aligned on 34c", which is moot under
  (c) because E8's "any steps" covers 34c: say so, or drop it from the choice. PLAN §3 and ARCH say unit 1a is "in
  review", but review-1a-r1 is "redo": say "redo after review".
- **Checked and correct.** R4-M52 attributes the owner's four answers with quotations, says "Not an integration", claims
  no SPEC revision and names R4-M39's superseded ending rule, and R4-M39's header points back to it. E8's wording matches
  `PrefixKill` (exact steps, no runtime index or Deref in K, any continuation, direct Loans too). Round 10's counts in
  R4-M52, A3 §10 0b, §12, prototype.md, both READMEs and PLAN match `run.txt` (234; 187/24/23 and 206/21/7; the ablation
  rows). PLAN has 205 lines (≤ 225); its rows G91/G92/G94/G110/G113/G119/G123 are short and agree with choice §2/§3 and
  E1–E9, and the ARCH R4 row agrees with PLAN. The markers are on the right lines: E1 on `hits`, E3 on `issued` and the
  hits comment, E4 on Containment, E5 on `CS`/`CS_call`, E7 on `err(ended)` and the marker list, E8 on the `ended` row and
  §3's kill bullets.

## 4. Non-blocking

- E5 has no census proxy for its new rejections (a holder storing a Loan on its own part, then an activation or update of
  that part); 1b-5's shadow run is the check, so say so in 1b-2's condition. The prototype models no Types, views or statics.
