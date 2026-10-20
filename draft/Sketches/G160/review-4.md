# G160 plan: independent review, round 4

Reviewer: the reviewer of rounds 1–3; author of none of the designs. Scope:

- the rewritten plan (742 lines) in worktree `bin/worktrees/g160-plan`, base `59b78023`, uncommitted;
- PLAN rows G160 and G182;
- the INTEGRATED entry.

Re-read for this round:

- AGENTS: Implementation Design, Symptom fixes, Review weight, Independent review, Diagnostics.
- Library: `UniqSlice.kimi`, `Storage.kimi`.
- Code: the round-3 code reading, still valid.

New probes, on the dev `a1677549` binary through the same harness, are in `scratchpad/g160/review4/`:

| Probe | Shape | Today |
| --- | --- | --- |
| u2 `ownRefsDead` | `OwnRefs {a} {data: raw/(ref/i32 during a)}`, its own allocation; `storeO(o@uniq, x@ref)`; `x = 5`, o dead | error `x = 5` (raw clause) |
| u2 `ownRefsRead` | the same, then `unsafe => q = o.data[0]`; `o` replaced; `x = 5; inspect(q)` | error `x = 5` (raw clause) |

`u1` and `u3`–`u4` were malformed probe sources (`MissingOriginBinding_Kd` in my generic Box8/Box9); they found nothing.

## The questions asked

**(1) Is the split legitimate and honestly stated?**

- **Legitimate: yes.**
  - The in-body read/store side drew rejections in rounds 1 and 3 (B2/B4; R3-B1–B3): two rounds on one family. AGENTS hands that
    family to the owner, and allows "keep … open issue and record the proposed change in PLAN" when the common fix exceeds the unit.
  - G182 records the family with its candidates (Q5), and §0, §4.6 and §11 explain the withdrawal.
  - The original G160 row covered only call shapes (a)–(c), so moving the newly found read side to its own row narrows nothing
    the PLAN had promised.
- **Honest: not yet.** The statement "the call-side fix neither creates nor widens that hole" is false for one class of Types
  (R4-B1). Q5's account of a hold is also incomplete (N1).

**(2) Does the call side still depend on anything withdrawn?** No.

- `Keeping` (Kept Fields, `Concrete`, `Bare`, `BareParams`), the Unowned walk, `via self` in the guard walk, and W rules 2 and 3
  use the kept Fields only in the DFS and the guard walk.
- §4.3's last paragraph keeps `Read`, `Issued` and `Store` as today.
- No call-side verdict in §1.2 relied on the kept step:
  - (a)–(c), Tuple, by-value, nested-`uniq` and `rawHolder*` reach E through rule 2;
  - `bufferDead`, `OwnRefs` dead with `via self`, `keptDead`, `TupBox`/`Outer` and `cursorWrite` follow W alone;
  - `keptDirectDead` and `keptInitDead` are in-body forms, now errors by item 5 or sentence 3.
- §7 is consistent with this.

**(3) Does the call-side fix leave the read-side hole as it is?** No, see R4-B1.

**(4) The read-side and `keptDirectDead` cells.**

- `keptDirectDead` and `keptInitDead`: error, error (item 5), error (sentence 3). These are consistent and are now the letter, not a
  conservative error.
- The read-side rows (G182) are right for the shapes listed. They omit the shape R4-B1 adds.

**(5) Anything else blocking?** Yes, R4-B2.

## Blocking findings

### R4-B1. Declaring `via self` on a Type that holds no Loan outside its raw storage moves its stores from E to the keeper, which widens G182's hole

- **Location.** §0 ("The call-side fix neither creates nor widens that hole"), §4.6 ("declared stores reach the keeper exactly as
  today"), the G182 row ("G160's fix does not widen them"), §1.2's read-side row, §7 ("not this plan's regressions").
- **Problem.** The claim holds for keepers that are holders today (`Buffer<ref/i32>`, `KeptBox<ref/i32>`, `MarkBox`): their stores
  already go to the keeper alone. It fails for a Type whose only Loan-capable content is its raw pointee (`OwnRefs`):
  - Today it holds no Loan, so the raw clause sends every call store to E. That is sound, and u2 shows both `ownRefsDead` and
    `ownRefsRead` rejected.
  - Under X-decl with `unsafe storage data of ref/i32 during a via self`, minimal-core item 2 and §4.2 make `OwnRefs` a holder.
    `Keeping` makes it non-`Bare`, so `storeO` stores into o alone, with no E.
  - The in-body read `q = o.data[0]` stays Raw ({Φ_E}), and E no longer holds x's Loan. So after `o` is replaced, `x = 5;
    inspect(q)` is accepted: an unsound acceptance this plan creates.
  - The exposed class is every `via self` keeper whose concrete pointee is its only Loan-capable part. It is reachable only
    through the new declaration, so no existing program regresses. Every program written to the revised SPEC with such a keeper
    is exposed. K′ is not affected: its kept pointees are Type arguments, whose instances are holders today.
- **Evidence.** u2 output (today's errors), §4.2 (guard walk), §4.3 (rule 2), §4.3's last paragraph (reads unchanged).
- **Correction.** State the class, then choose one of these and record the choice:
  - (a) Put the class in G182 and §1.2, and add to Q5 whether G160-3 may land with it as part of the recorded limit (iii).
  - (b) Keep "exactly as today" with an interim: until G182 lands, a `via self` storage on which the keeper's Loan fact rests
    alone still gives `Unowned` (E, as today), with G182 as the removal condition. `OwnRefs` dead then stays an error until G182.
  - (c) Make minimal-core item 5 say what K′ sentence 3 says, that "the reader keeps a value's Loans live itself" for in-body
    raw reads of declared storage. G182's declared-keeper half then becomes the unsafe reader's obligation, not an A5 hole, and
    G182's row and §4.6 change accordingly.

### R4-B2. K′ sentence 2, as revised for R3-B3, contradicts the library and K′'s own kept test

- **Location.** §1.3 K′ sentence 2 ("addresses only storage its value owns … A non-owning view with a marker is therefore not
  K′-kept"), §1.2's library-iteration row (K′: "unchanged").
- **Problem.** K′'s kept test is shape-only: a layer at route Own whose pointee holds Loans only through parameters. So
  `UniqRemainder<E>`, `UniqSlice<T>` and `RefRemainder<E>` are K′-kept, markers or not. "Therefore not K′-kept" does not follow.
  Read as an obligation, sentence 2 makes the library's own writes breaches:
  - `UniqSlice` lends `self.storage[key]@uniq` (`UniqSlice.kimi:45`) into its source Array's storage;
  - `Storage.splitFirst` on `UniqRemainder` lends `state.storage[position]@uniq`.

  Read instead as part of the kept test, the exclusive views become bare. Their `BareParams` then give `ArrayUniqIterator<ref/Data>`
  `Unowned`, and exclusive iteration (`viewsUniq`) gets a concrete E: errors, not "unchanged". Either reading breaks the K′ column.
  The revision answered R3-B3, whose cause was the withdrawn Own class. With in-body raw access back to E, a K′-kept view with an
  exclusive marker is sound again: a call reaches the owner through the marker's guard (P13 hold), and an in-body store reaches E.
- **Evidence.** `UniqSlice.kimi:39-45`, `Storage.kimi` `splitFirst`, §1.3's K′ kept test, §4.4 (P13).
- **Correction.** Restore sentence 2's marker alternative: kept storage is storage the value owns, or storage whose owner's Loan
  it keeps in a `Loan<T>` Field, `uniq` for writes. A marker-less non-owning keeper (`cursorWrite`) remains the writer's breach.
  Drop "therefore not K′-kept" and keep the K′ cells as they are.

## Non-blocking findings

- **N1. Q5's hold is cheaper than §4.6 says, and AGENTS' third choice is a whitelist.**
  - A body-level hold, like P13's, needs no carried set. It can come from facts A5 already has: a view holds when it contains an
    in-body raw read and also takes `@raw` of a holder's Place or stores through a call into a keeper with kept storage. Every
    raw-read problem it owns then becomes a located `Unsupported_Kd`.
  - Its cost is library bodies that take `@raw` of a parameter's Place, for example `FixedOwnedRemainder.drop`, which the census
    would show.
  - Present this option with its cost. Also say why no whitelist applies, or name one: in-body raw reads admitted only through
    raw-only locals that never derive from a holder Place.
- **N2. §1.2's read-side row and §7.** After R4-B1, add the declared raw-only keeper (`ownRefsRead`) with its verdict under the
  chosen correction. Drop "not this plan's regressions" unless (b) or (c) is chosen.
- **N3. G182's row.** After R4-B1, replace "G160's fix does not widen them" with the chosen correction's statement.

## Checks with no finding

- The call-side representation is unchanged from round 3, where it was found sound, and depends on nothing withdrawn. This covers
  `Keeping` with `Concrete`, the Fields-free Unowned walk, `via self` in the guard walk, W rules 2 and 3, P13, 2e-D, 2c and 2f.
- §4.1–§4.3, §5, §6 (the withdrawn option is listed) and §9 (G182 outside the group; G160-0 answers Q1–Q5) are consistent.
- The PLAN G160 row is narrowed to calls and states facts. G182 records the read side with t1, its cause (no carried set; `p[n]`
  through a temporary) and Q5. Both rows fit PLAN's format.
- The INTEGRATED entry is unchanged and still accurate ("review-1.md and later rounds").

## Verdict

**Reject (blocking findings R4-B1 and R4-B2).** Both are local.

- R4-B1 makes the split's central claim true or honest; the choice belongs to the owner.
- R4-B2 restores K′ sentence 2's marker alternative, which the withdrawal of the Own class made safe again.

The split itself follows AGENTS, and the call-side design is sound and complete.
