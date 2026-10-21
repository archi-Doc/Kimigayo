# Review of the R4-M103 records (static slots, layout requirements, lifecycle receivers)

Reviewer: independent (wrote none of it). Documentation only; nothing built, edited or switched.
Subject: the uncommitted diff in `bin/worktrees/r4-proposals` (base origin/dev `127b6f0e`): `git diff`, `git diff --cached`
(two staged renames). Read against HEAD blobs for the moved files. The untracked Raw Pointer Rules file is ignored, as asked.

## Verdict: ACCEPT

No blocking finding. The SPEC sentence is exactly the proposal's option B plus two citations, its anchors resolve, and it contradicts
no sentence I found. The records agree with the proposals and the owner's answers. The non-blocking findings below are
record-accuracy and wording notes; N1 to N4 are cheap to apply before the commit and I recommend doing so.

## Blocking findings

None.

## Non-blocking findings

**N1. R4-M103 states two facts the proposals mark (H), without the mark.**
- "C0c's `ReceiverSlot`" is not on dev. `grep` finds it only in the Lifecycle proposal ("branch `claude/bu20-g160-raw`, DECISIONS BU-23
  when it lands, H until then"); dev has `HelperSlot` (DECISIONS BU-17) but no `ReceiverSlot`. review-3 asked that it be cited or
  marked (H). DECISIONS is authoritative over planning files, so write "(BU's C0c, not yet on dev)" or drop the parenthesis.
- "2a0-D §3 row 3 lists the receiver's part of a constructor or `drop` ... and the destruction receiver's helper borrow, Finite" is
  not true of `draft/Sketches/A5 Step 2/2a0-D-package.md` today: row 3 lists only the destruction receiver's part
  (`Projection[drop#0]`) and its helper borrow (`drop#HelperSlot`), with SPEC column "15.6.5, 14.8.3, 16.3.1". The constructor
  receiver's part is the addition (the proposal's §5 says the same "lists" and is equally ambiguous). Write "gains" and name the unit
  that edits the row (G166's A5 part or 2a-1a), or the sentence reads as an accomplished fact.

**N2. Two names for the SPEC's one key.** SPEC §22.4.4 and the proposal's own §1 table call it the *static storage key*; R4-M103
and the new PLAN sentence introduce "static slot key" (the owner's OQ-S1 wording). The PLAN P42 row now says "one static slot key
owned by `TyCtxt`" and, at its end, "static storage keys are Place roots (§15.6.2)". AGENTS asks for one name in every phase and a SPEC
name for a concept. Add one clause to R4-M103, for example "(SPEC 22.4.4's static storage key)", so that an implementer does not read
two keys. Keeping the owner's wording otherwise is fine.

**N3. R4-M103 drops the proposal's own caveat on the key.** The proposal's §8 marks (H) that `TyCtxt.Erase` keeps unused enclosing
arguments, because SPEC §22.4.4 says "Semantics layers and unused arguments still distinguish keys" while R5-M39's null rule
(`Erase(Subst)` is null when the Target does not instantiate) reads `Instantiates`, and Milestone 42's `Cache<T>.Statistics.count`
does not use `T`. R4-M103 says the key is `(Field, TyCtxt.Erase of the enclosing Subst)` and names no open point. Add one sentence:
"For the package to confirm: that Erase keeps unused enclosing arguments (SPEC 22.4.4), and that TyCtxt interns the key without a new
table." The proposal text stays open, but the decision record is what readers use.

**N4. Wording and references in R4-M103 and PLAN.**
- R4-M103 cites "(OD4)" for the collector's records at `check`. In DECISIONS "OD4" most often means R5's parallel run (R4-M35,
  R5-M1); the records rule is R5-M41 ("Collector records under MIR code generation (OD4)"). Cite R5-M41.
- Numbering: R4-M101 is reserved for 2a0-2 (R4-M99's note) and R4-M102 exists on `claude/r5-a2b` at the same insertion point, so dev
  will have a gap and the later merge a trivial conflict. R4-M99 recorded such a note; add "Numbering: R4-M101 is 2a0-2's, R4-M102
  is A-2b's (on their branches)".
- PLAN P42: "Its design is one full-process package ..." can be read as a package that exists. The proposal's item 2 and R4-M103 say it
  comes first, before any P42 unit; "Its design starts with one full-process package ..., before any P42 unit" is accurate.

**N5. INTEGRATED, Lifecycle entry: a rejection without its reason; the consequences section has no disposition.**
- The register requires "rejected with a reason" for every item of an 取り込み済み file. Option A has its reason; "option C (no
  change) is rejected" has none. Add the proposal's §3 assessment, for example "(the classification would stay a reading of §6.3.4
  and §16.3.1)", or "the owner chose B".
- The proposal's §5 (consequences for Binding, A5, R5) is not mapped. Add "§5 → DECISIONS R4-M103 (2a0-D row 3; G161, G165, G166)",
  as the older entries do for non-SPEC sections.

**N6. Static Slots entry and status line (minor).**
- The proposal stays 一部取り込み although nothing is integrated and no SPEC change will come. This is consistent with the register
  (a close needs every item incorporated, rejected or transferred to an identified proposal, and the P42 package is not a file yet),
  but the entry should say plainly "integrated scope: none; open: the key and the package" and, when the package's file exists,
  transfer both items to it so the proposal can close as 完了.
- The new status line dropped "not implemented"; the Lifecycle status line keeps "Not implemented." Add it for consistency.
- The INTEGRATED entry still begins "Proposes one static slot key" and then says "Adopted by the owner"; "Proposed one ...; adopted by
  the owner ..." reads cleaner.

**N7. SPEC vocabulary (optional).** §15.1.5 (line 108) and §5.7 call the receivers "a construction receiver" and "the destruction
receiver", the headings of §6.3.4 and §16.3.1. The new words, "the receiver of a constructor or `drop`", mean the same and are the
owner-approved option B text, so I do not ask for a change. A later SPEC session could say "the construction or destruction
receiver" (shorter, the SPEC's defined terms). A second, tiny point: the interposed ", or in the receiver ... (§6.3.4, §16.3.1)," now
separates "reached by Field, element or owning-layer projections" from "a Borrow"; it still attaches to the Borrow, so it is not
ambiguous enough to change.

**N8. Process: the worktree is shared and moved while I reviewed.** At my last look `docs/dev/PLAN.md` also carries a G211 row, and
`draft/Proposals/2026-10-21 Failures Caused Only by Other Failures.md` has 145 changed lines, neither in this unit's list (another
session's round-3 review work). The two renames are staged but their status-line edits are not. Stage explicit paths for this unit,
and take only the P42 hunk of PLAN.md (`git add -p`), so G211 and the Failures edits do not ride in this commit.

## Check 1. The SPEC sentence (docs/spec/15 §15.6.5, one line)

- **Exactly option B.** Proposal §2: "A body-local Origin belongs to a Borrow anchored in a local, parameter or temporary root of the
  body, or in the receiver of a constructor or `drop`, reached by Field, element or owning-layer projections, never through a borrow
  or raw pointer; its bound is the body." The SPEC line is that text with "([§6.3.4], [§16.3.1])" after "`drop`" (the mapping of
  proposal §4 and review-3's "shorter formulation"). The rest of the bullet is unchanged. Grammar is sound.
- **Anchors resolve.** `06-declarations-and-containers.md#634-the-construction-receiver` (heading "6.3.4. The construction receiver")
  and `16-scope-exit-and-destruction.md#1631-destruction-receiver` (heading "16.3.1. Destruction receiver") exist; both are already
  used elsewhere in the same style. A script over all 245 links of the chapter found none broken.
- **No contradiction.**
  - §6.3.4: "Field borrows end before completion" is a Loan point, as the proposal says; the bound stays "the body", and §15.6.5's own
    closing paragraph treats a use past a local's lifetime the same way (a Loan conflict, "never an Origin relation failure").
    Constructor `self` is not a borrow, so "never through a borrow" does not exclude its Fields; §6.3.4 forbids borrowing the whole
    `self`, so only Field projections occur.
  - §16.3.1: `self@ref` "whose Origin ends within the `drop` body" is a finite Origin bounded by the body; "not an ordinary borrow
    value" keeps `self` from being "a borrow it is reached through"; "may be passed to helpers but cannot be captured, stored or
    returned" agrees with a Refuted relation to a fixed Origin.
  - §15.6.5's other bullets: the fixed-Origin bullet ("a parameter or receiver Origin, a projection of one") concerns a receiver's
    outer borrow Origin; §5.7 says that in a constructor `self` is "the value being constructed" and in `drop` the destruction
    receiver, so they have none. The mutable-static bullet and the inferred-region bullet are untouched. The Closure sentence
    ("borrowing them through a borrowed receiver is not body-local; in a Consuming call it is") concerns a call receiver; it is
    explicit about that.
  - §7.7.2 ("In a constructor or `drop`, no entry captures `self`") and the capture-entry clause: no overlap. §5.7 ("both exclusive"):
    no overlap; raw claims on `self` are not Borrows.
- **No other sentence needs the same change.** Every mention of the two receivers in docs/spec and docs/impl is a root or access
  statement (§15.1.2, §15.1.5, §11.2.2, §11.7, §16.3.1, §16.4's note on objects); none classifies a Borrow's
  Origin. "Body-local" occurs only in §15.6.5. Appendix E has a "Finite Origin" row and no body-local row, so there is nothing to
  add.
- **No example's comment changes meaning.** I scanned every `kimi` block under docs/spec and docs/*.md that has a constructor or
  `drop` and a borrow of `self`: §6.3.4's `Tally`, §16.3.1's `Connection`, §5.6's `Buffer` and `BufferView`, §11.4's `Holder` (a
  getter, an ordinary receiver). None stores a receiver Borrow into a fixed destination, so no "Allowed" or "Error" comment changes.
  (The no-verdict-change claim is still (H) in the proposal's §8; it belongs to G161, G163, G165, G166 and 2a-1a.)

## Check 2. DECISIONS R4-M103

- Matches the proposals and the answers: OQ-S1 (a) (one key owned by `TyCtxt`, one package, reviewers from R4, R5, BU), OQ-S2 (a)
  (both narrowings not adopted, quoted as in the A5 plan), OQ-Y1 (d) = D (A′ and the A5 optional candidate not adopted;
  `OversizedLayout_Kd` stays Language, R5-M52, IMPL 21.1.1), OQ-L1 (b) = B (the quoted words are the SPEC words; §6.3.4's completion
  rule stays a Loan rule; the unified definition not adopted). R5-M39, R5-M40, R5-M52 and review-3 exist.
- Format matches R4-M99: bold id and title, parenthesis with owner, date, prompting instruction and answers, then sub-bullets.
  Placed after R4-M100 and before the "Owner decisions" bullet.
- Not adopted is named for static slots (the narrowings), layout (A′, the A5 candidate) and lifecycle (the unified definition).
  Options B and D of the proposal's §4 (static slots) and C (lifecycle) are not named; acceptable since the chosen option is stated.
- Findings: N1, N2, N3, N4.

## Check 3. INTEGRATED

- Follows the lifecycle rules: the Lifecycle file closes 取り込み済み and moves to `Changes` (something was incorporated: §2's root);
  the Layout file closes 完了 and moves to `Obsolete` (nothing incorporated; A′ rejected with reasons, the first draft's third item
  withdrawn with its reason, the A5 candidate not adopted); both moves and the register edit are in one unit; the intro's
  "no text is frozen" and "Nothing is integrated" lines are correctly replaced by the closing line. Static Slots stays in `Proposals`
  as 一部取り込み, which the rules allow (see N6).
- Mapping for the integrated item: §2's root to §15.6.5's bullet, citing §6.3.4 and §16.3.1; §6.3.4, §16.3.1, §7.7.2, §5.7
  unchanged. Complete for the SPEC (proposal §4 has exactly those rows); §5 is unmapped (N5).
- No support is claimed: "No support is claimed (STATUS unchanged)", "SPECJ and GUIDE are not refreshed" (explicit-only per AGENTS);
  the closing line repeats it. Folder rules and statuses use the register's own words.
- Findings: N5, N6.

## Check 4. The moved files

- Compared with their HEAD blobs: the Lifecycle file differs only in the status paragraph (one line replaced by a two-line one: the
  new status and "Not implemented."); the Layout file differs in exactly one line (its status). Nothing else changed.
- Relative links: `../../docs/spec/...` from `draft/Changes/` and `draft/Obsolete/` reaches the repository's `docs/` (same depth as
  `Proposals`); all 5 and 3 links resolve (script). The `../Sketches/R4 Proposals 2026-10-21/review-3.md` mentions are code spans and
  the path exists from both folders.
- Other references to the old paths: only `draft/Sketches/R4 Proposals 2026-10-21/review-3.md`, a review record that names the files
  as reviewed (a Sketches file, not a link); no repository link breaks. Both files are 100 percent renames.
- The Layout status line is now one 177-character line before "Written by"; cosmetic only.

## Check 5. PLAN and the Static Slots status line

- PLAN P42 sentence: accurate against the proposal (full-process package over Binding, MIR, A4/A5, code generation, runtime; one key
  owned by `TyCtxt`; DECISIONS R4-M103 exists); it fulfils the proposal's §5 promise ("PLAN P42's row gains one design package first").
  It adds one sentence to an existing row, so no new line and no unit history; PLAN is 223 lines with another session's G211 row (225
  allowed). Wording notes in N2 and N4.
- Static Slots status line: accurate (date, OQ-S1 (a), OQ-S2 (a), R4-M103, "no SPEC change", open until the package takes it up);
  the only change to that file. See N6.

## What I could not verify

- That no program changes verdict under the new sentence (the proposal's own (H)); that BU's `ReceiverSlot` lands as described;
  that `Erase` keeps unused arguments (N3). I did not fetch origin: "origin/dev unchanged" and the branch contents (`claude/r5-a2b`'s
  R4-M102) are as of the local refs.
