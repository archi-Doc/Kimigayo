# 2a-2 design review: designs A and B for rule W on the MIR (full process)

R4 reviewer slot, 2026-10-25. I wrote neither design nor any of the code. Base: worktree `bin\worktrees\2a-2-rev` =
`origin/claude/a5-2a-1c` `c546f636a` merged locally with `origin/dev` `d0ed7f085` (DECISIONS and CODEMAP conflicts resolved by union).
Both prototypes (`probes-a\proto-a.patch`, `probes-b\w-b.patch`) applied to it unchanged. Probes and outputs are in `probes-rev\` (§10).
No repository edit, commit or push. The worktree was removed at the end, with its junction unlinked first. "(H)" marks a claim no run
checked.

## 0. Verdict

**Neither design as written.** A has two blocking findings and B has one (§3). Each has a small fix. **Chosen: combination C** (§5):

- A's representation and owner split: indirect markers below `loanBase`, MirOrigins' `Causes`/`Fixed`, Judge records first, U1–U3.
  Two fixes are added: `Indirect` is built linearly, and all W setup is gated on one MirOrigins fact.
- From B: the exact R4-M96 scan, the per-holder read-through, the Content-retention and write-through rows, the MirValidator rule for
  the role (corrected), the merge of derived-ness (with causes unioned) and G213's three facts through one recorder.
- From A: the receiving check over every Origin of the receiving Type (it closes OQ-W2), the guard rule over every binding Ref, the
  role condition "Destination is local 0" and the ledger rule.

The two designs agree on the core shape: one `MirFact(Kind, Site, long Value)` row with `Cause` facts, `Add` returning the index,
`MirLocalRole.Result`, recording before the Loan stratum, and H3 publishing `PrerequisiteUnavailable_Kd` with `DerivedFrom`. They
differ in representation and in how precise each transfer rule is.

On the 1,240 sources of `shared-base\65be867e9` and the round's 113 programs, base, A and B have identical problem keys (code,
subject, span, counts): no verdict or record changes, only labels. Both A and B reproduce the round's 113 readings.

## 1. The problem, and whether a shorter design exists

W is a predicate on the carriers of each conflicting Loan at one check. It holds when all of these are true:

- every carrier in the check set is an *escape holder*, that is, the result in transit or E;
- each carrier received the Loan as the borrow or a Copy or Reborrow of it (whole-value transfers, the call clause, the read-through
  clause), into a part whose Origins are fixed;
- the Borrow's Origin has Refuted problems in every view of the problem.

So the facts needed are:

1. the escape holders (the builder's result temp, local 0, E);
2. a may-indirect fact per (Loan, carrier), joined by union;
3. the class of a receiving part (MirOrigins);
4. Borrow → causes (MirOrigins);
5. a cause slot on a problem (MirFact);
6. a merge rule for problems that group several reports.

I tried three shorter routes. None works:

- **Decide W in `MirReport` from carrier facts.** MirReport would re-derive what MirBorrows owns: a consumer deciding.
- **One "dirty" bit per set (A's B4).** One indirect W Loan then taints every W Loan of its holder. `jregion2`/`two-flows-v`, where
  E holds two Loans, would lose precision.
- **A backward query at conflict time from each carrier to its Ref.** It needs the same per-path fact the forward state already
  carries, plus a second walk.

Both designs' common shape is the shortest coherent one, so the review compares their details.

## 2. Differences (each with its evidence and my pick)

| # | Question | A | B | Evidence (probes-rev unless stated) | Pick |
| --- | --- | --- | --- | --- | --- |
| 1 | Indirect fact's representation | One marker element per W element, below `loanBase`. No reader changes. Nothing to do where k = 0 | A state word of two interned sets: Loans in the low half, the indirect subset in the high half. Every Loan value becomes `ulong` | Valid shapes, shape-body Loan stratum, N=128, `DOTNET_TieredCompilation=0`, 60 repeats × 6 interleaved processes: A 0.98–1.05× base, B **1.18–1.31×** (B's own +20–31%). Library `MirAnalysis.Run` (217 bodies, TC=0, 200 repeats × 8 processes, min/median): A +3.3/+3.1 %, B +1.8/+1.7 %, A with an early-out in `Caused` (a2) +2.5/+2.2 %. W-dense `escape-reads`: A Bytes **72,184 / 549,880 / 4,294,648** against base 2,240 / 8,576 / 33,536 at N=32/64/128 (cubic overall) and Work ×4.5/×9/×18, while B equals base. Same for `escape-aggs` and `escape-copies` | **A**, with `Indirect` built in one pass (§3 A-B1), and all W setup behind the gate of row 2 |
| 2 | Zero-cost path | k = 0 (no W element), checked in each hook | `Refutes(body)`: `tracking` empties the high half | A's library overhead is per-run fixed work (fills, `Escapes`, `IndexCauses`) done before its k test | One published MirOrigins fact ("the judged body has a Refuted problem from a Ref's own Origin") gates all of MirBorrows' W setup. B's fact, A's representation |
| 3 | Derived test's candidate set | Every candidate on the root that the check set takes (a superset: A's departure 4) | Exactly the conflicting candidates (R4-M96) | `noncfl-candidate-v` (`keep = s.0@ref` live, `s.1`'s Loan only in E, `s.1 = 5`): A gives I, B gives D; SPEC says D | **B** (no departure) |
| 4 | Transit values | Always kept (a carrier other than an escape holder) | The carrier is the holder the value is stored into | No reading differs on 113 + 1,240 programs | **A** (shorter; a conservative reading, recorded in DECISIONS) |
| 5 | Read-through clause | All or nothing per reference set (`WholeContent`) | Per holder inside `Content`: a whole-root holder's state as it is, a part holder or E indirect | Equal on all programs run | **B** (the clause's literal reading, in Content's owner) |
| 6 | Content retention | Indirect | The read-through rule | `content-retention` (`return first(h@ref)`): A gives I, B gives D | **B** (follows from R4-M122's clause) |
| 7 | A local written through a reference (store, or a call's write) | Indirect | Kept when the reference's direct Loan names the holder's whole root (the write-through row) | `d4a09871a00b`, `f9cd563f19a0` (`assign(x@uniq, y@uniq); return x`) and `write-through-store`: B gives D, A gives I | **B** (the call clause applied to locals, as A applies it to E) |
| 8 | Receiving part's class | `Fixed(type)`: every Origin of the receiving or written Type is Fixed, with no raw or abstract part. One rule | Outer Origin only. For a call write into E, E must also be a first-level target | `f02fb5c218bc` (`target@follow.append(local@ref)`), `container-deep`, and the closure appends `3c4ffb8ea44b`, `50881dcb6732`, `a0348a4af117`, `a0bf23226e44`: A gives D, B gives I (B's OQ-W2). `store-every-origin`: both D | **A**. It derives what SPEC calls derived, it is sound because every part that can hold the value has a fixed Origin, and it closes OQ-W2 without BU |
| 9 | A guard marker's Borrows | Every Ref statement that binds H | The first Ref issued into H | Today one Ref per candidate (`MirBuilder.Match.cs` 84–86), so both are equal | **A** (relies on no unvalidated builder property) |
| 10 | Which temp gets `Result` | Destination is local 0 | `targets[0]` | `ctor-return` (a constructor's `return` from a cleanup scope): B gives `_6: () temp (Result)` while `_0: Res`. A gives no role | **A's condition** (§3 B-B1) |
| 11 | Validating the role | None | MirValidator: at most one, a Temp of local 0's Type | Under A's condition a body can have two (the return target's and a tail construct's whose destination is `_0`) | **B's rule, corrected**: every `Result` local is a Temp of local 0's Type, and its only reads are whole Moves into local 0. MirBorrows' derived label rests on this, and MirValidator owns the MIR's validation |
| 12 | Merging derived-ness in `MirReport` | `Derived` in the key, so a derived and a direct problem at one subject stay two. A derived merge keeps the first problem's causes | One key. A problem is derived only when every report is derived and they name the same causes; otherwise the `Cause` tail is dropped | `merge-derived-direct-v`: A gives two problems (D, I), B gives one I. `merge-two-paths-v`/`-v2`: both give one D with both causes (one atom) | **B's rule, with causes unioned instead of "else independent"**: a problem is derived exactly when every report it groups is, and its causes are their union. This is R4-M96's rule at the report level, and DIAGNOSTICS §4.3 unions causes. R4-M71's key is unchanged |
| 13 | Recording order | `Judge` records; `Record` deleted | `Record` moved before the Loan stratum | Neither changes a test (A: 595/595; B: 716 run) | **A** (one member fewer) |
| 14 | Publishing the causes | A span of report indices per Ref (counting sort); the source added to `Pending` in `MirOrigins.Chains.cs` | A list of (index, views, subject) found by a loop per query; the source read from `Reason.Longer` against the canonical atoms | `MirOrigins.Chains.cs` is about 4,200 code tokens and 5,770 with comments by my approximate count. B's per-query loop is quadratic in a W-dense body | **A's span, with B's source** (Chains.cs untouched) |
| 15 | Ledger | A derived problem leaves the compared MIR side (step-2 plan's 2g-1 wording) | It pairs with an old record of its code and node where one exists, and is dropped otherwise | On `65be867e9`'s 23 programs no derived problem has an old twin, so both rules are equal there | **A** (no departure, and it compares what H3 will publish: a derived record is suppressed) |
| 16 | G213's arguments | `Required` and `Table` facts. `limitBytes` comes from MirDataflow | `Required`, `Limit` and `Table` through one recorder, `MirDataflow.StorageLimit` | R5-M96's removal condition: "A1's arena problem carries its arguments" | **B**. Check `MirDataflow.cs`'s size first: about 5,630 tokens with comments by my count. If comments count, the recorder goes in a new concern file |
| 17 | How MirBorrows gets MirOrigins | A constructor parameter (6, at the limit) | An init property (prototype) | — | **A** |
| 18 | Units | U1 bp, U2 (role + W), U3 G213 | 2a-2a bp with pair plumbing, 2a-2b role, 2a-2c W, 2a-2d G213, 2a-2e docs | — | A's split, with B's separate role unit (W1 hold, R5 review) and docs unit (§6) |
| — | Same in both | `MirFact(Kind, Site, long Value)`. `Cause`'s Value is the index (−1 is 2g-1's Binding cause, which 2g-D decides). `MirProblem` unchanged. `Add` returns the index. H3 publishes `PrerequisiteUnavailable_Kd` with `DerivedFrom`. The same deletions | | | — |

**Reconciling the numbers.**

- A's "14 derived on 1,240 sources" and B's "11 of 23" count the same population. The other 1,217 sources derive nothing under
  either design, re-run here on all three variants.
- Within the 23 programs:
  - 9 are derived by both: `0433a531b1ba`, `08ef74956a0a`, `27e4888a2ae9`, `38df74b325ce`, `67eeb3e27fc8`, `7229e0a39170`,
    `c6dc73a3a287`, `dbc5c8932247`, `e3c4d30c20af`.
  - 5 are derived by A only (row 8).
  - 2 are derived by B only (row 7).
  - 7 stay independent in both, for SPEC's reasons: `34b46fd732a0`, `6227f1ba89a2`, `6d75d7b343f0`, `afb5f655e370`,
    `c19cc38ae40c`, `c502b66e356d`, `e1116aced452`.
- C derives the union, 16 (H: this follows from rows 7 and 8; no combined prototype ran).
- A's "Work +0.4–2.6 %" counts Work over the whole program, the library's 217 bodies included. B's figures are the shape body alone.
  Re-measured on the shape body: W stores and joins are within +1 %, `aggregates+S` is +2.4 %.

## 3. Blocking findings

**A-B1 (V16, measured).**

- What fails: `Indirect` adds one marker at a time with `MirLoanSets.Add`. Every intermediate node is interned, so marking a set
  that holds m W Loans costs O(m·|set|). A read through a parameter after each refuted store (`escape-reads`) makes the Loan
  stratum's Bytes cubic: ×32, ×64 and ×128 of base at N = 32, 64 and 128. `escape-aggs` and `escape-copies` behave the same.
- What the design claims instead: §8 says "within a constant of the existing set work" and "Bytes ≤ 2× those sets". It names no
  construction that achieves that.
- Fix: MirLoanSets interns a sorted run in one pass (a new operation in its contract). `Indirect(S)` is then
  `Union(S, Of(markers of S's W elements))`, O(|S|) per distinct set and memoized. Add a V16 row "W reads through E" (this shape;
  Bytes ≤ c × the W-free run's).

**A-B2 (contract: DIAGNOSTICS §4.3).**

- What fails: "two derived ones merge keeping the first's causes" leaves out the later report's causes. DIAGNOSTICS aggregates one
  problem's causes as a union.
- When it can happen: causes are per root atom and filtered by the run's views. So it can happen across the views of a
  view-dependent body whose Refuted problems differ per view (H: not constructed; `two-views` had one class).
- Effect: none today, since every cause is a published direct Error. Still, H3's `DerivedFrom` would be incomplete.
- Fix: union, as in row 12.

**B-B1 (internal fault on a valid program).**

- What fails: the role is given to `targets[0]`'s result temp. In a constructor, `targets[0]`'s destination is a Unit temp
  (`MirBuilder.cs` 238–240), so a `return` from a cleanup scope gets `_6: () temp (Result)` while `_0: Res` (`ctor-return`).
- Consequence: B's planned MirValidator rule ("a Temp of local 0's Type") then reports `InternalInvariant_Kd` on valid input.
- Fix: the role only where the Destination is local 0 (A), with row 11's corrected rule. Test: no `Result` local in a constructor.

## 4. Non-blocking findings (open items unless C adopts the other design's part)

- **A:**
  - No validator rule for the role (row 11).
  - Its R4-M96 superset (row 3).
  - Conservative Content retention and local writes (rows 6 and 7).
  - `limitBytes` read from MirDataflow (row 16).
  - Its Chains.cs edit (row 14).
  - Per-run W setup before the k test, which costs the library +3 % of MIR analysis (row 2).
  - The prototype computes `Fixed` for every escape store in a W body, even for values without a W element; the implementer gates it
    on the value.
  - The prototype's guard scan is O(guards × statements); the design's per-Ref lists replace it.
- **B:**
  - The pair plumbing costs every accepted program: +18–31 % of the Loan stratum on valid stress shapes (TC=0) and +1.7 % of the
    library's MIR analysis. That is decisive for row 1, but not a contract violation.
  - "Different causes → independent" (row 12).
  - The first-Ref guard rule (row 9).
  - The conservative container calls (row 8, OQ-W2).
  - The ledger pairing, which departs from the plan's wording and would hide a published-output difference (row 15).
  - `Causes` is a loop per query (row 14).
  - The 2a-2a bp commit for pairs, which C does not need.
- **Both:**
  - Causes are per root atom (2a0-D's identity), so a derived conflict lists every Refuted problem of the root's Borrows. That is a
    superset of "the Refuted relations of those chains", harmless while every cause is a published direct Error. Record it with H3.
  - `Fixed` reaches `TyCtxt.OriginAtom`, whose append of an unseen atom is the view's memo (H: no Judge output changes).
  - Arrivals through raw pointers with the root atom's causes (H).
  - The literal twin forms derive nothing until H11 (R4-M122's open item).
  - `MirFact` grows from 16 to 24 bytes.
- **Outside W, for the planning session:** in B's own data (`probes-b\t13-base.txt`), base's `escapes` shape (N stores through a
  parameter) takes Work 306,086 at N=128 and 48,062,742 at N=256 (×157). A V16 question for the Loan stratum, not this unit's.

## 5. Chosen design C (each part and its source)

1. **Facts** (A = B). `MirFact(MirFactKind Kind, SourceRef Site, long Value = -1)`, contract "a site the problem relates and, for a
   kind that names one, its typed value; never a message; `Cause` facts last".
   - Kinds: `Relation` (the row), `Cause` (the index in `MirReport.All`; −1 for 2g-1's Binding cause, which 2g-D decides), and
     G213's `Required`, `Limit` (bytes) and `Table` (evidence).
   - `MirProblem` unchanged. `MirReport.Add` returns the index, or −1 for a dropped problem.
2. **Merge** (B, corrected; row 12). A problem is derived exactly when every report it groups is derived, and its causes are their
   union. A direct arrival drops the first problem's `Cause` tail. A derived arrival with new causes appends them, moving the span to
   the end of the pooled list when needed (warm 0 B). R4-M71's key is unchanged. H3's merge across bodies applies the same rule.
3. **Origin stratum** (A; source per B).
   - `Judge(body, views, report)` records at its end. `Record` and its ordering line go.
   - Outputs, read after Judge and before the next body:
     - `Causes(statement) → ReadOnlySpan<int>`: per-Ref spans by counting sort, the source matched as `Reason.Longer` against
       the Ref's own atom.
     - `Fixed(type, site)` (A, row 8).
     - The gate fact of row 2.
4. **Builder** (A's condition; B's validator, corrected).
   - `MirLocalRole.Result` on the result temp `Transfer` creates where the target's Destination is local 0.
   - MirValidator checks: a Temp of local 0's Type, read only by whole Moves into local 0.
   - `MirDump` already prints roles.
5. **Loan stratum** (`MirBorrows.Causes.cs`, contract first).
   - Representation: A's indirect markers (`[Φ_E][per-local][indirect markers][Loans]`). `Indirect` is linear (A-B1's fix), and
     there is no W setup at all unless MirOrigins' gate fact holds.
   - W elements (A): Loans whose Ref's causes cover the run's views, and γ_H when every Ref binding H is one.
   - Transfer rules:
     - part steps, aggregates and a Value callee's environment: indirect (both);
     - per-holder read-through in `Content`, used for reads, Reborrow parents and Content retention (B);
     - the Value row keeps the argument (both);
     - arrival into an escape holder is kept iff `Fixed` (A);
     - arrival into a local: whole replacement, or a write-through by a store or a call's write (B);
     - joins by union; endings remove the markers with the Loans (both).
   - Check: `Kept` is one bit over all passes, transit kept (A).
   - Derived test: `Conflict` returns the access's conflict and whether every conflicting candidate is a W element without `Kept`
     (B, exact). The causes are their union, as `Cause` facts. No seventh parameter.
   - err(ended): γ_H's own test (both). A held view records `Unsupported_Kd` with no cause.
6. **Ledger** (A). A problem with a `Cause` fact leaves the compared MIR side.
7. **Publication** (both; R5 confirms). `PrerequisiteUnavailable_Kd` with `DerivedFrom` = the causes' check keys. DIAGNOSTICS §4.3's
   invariant is unchanged; only its "stay direct" sentence gains W's kind.
8. **G213** (B). One recorder `MirDataflow.StorageLimit(report, body, views, table, words)` with `Required`, `Limit` and `Table`.
   Its three callers lose their inline constructions. `MirDropTable.Incomplete(body) → (Code, SourceInfo Source, int Problem)?`.
9. **Deletions** (both lists). 2a0-D §7's rules 1–2 with `RefutedAtResult` and `Ended`; `MirFactKind.Result` and its R5 ask; D2's
   `ResultFit` Source (the `TyCtxt.Origins.cs` comment, a BU file, comment only); 2g-5's A5-F4/A5-F7 escape parts; the planned N-6
   and DOUBLE records; `MirOrigins.Record`; the round's def-use pre-pass (never written); in G213, R5-M96's arena interim and
   `InstanceCollector.Subject(item, SourceRef)` (R5's unit).

## 6. Units, sizes and tests

| Unit | Change | Size (H) | Verification and done condition |
| --- | --- | --- | --- |
| **U1 (bp)** | `MirFact.Value` (long) with its contract; `Add` returns the index; `Judge` records first; the driver's step-4 sentence; ARCH's Origin-stratum row; DECISIONS entry (the representation) | src +25/−25 | VERIFICATION's behavior-preserving procedure: diagnostic snapshot, semantic snapshot (`KIMI_SEMANTIC_MIR=1`), MIR classes, mir ledger equal as a multiset |
| **U2 (W1 hold, R5 review)** | `MirLocalRole.Result` (row 10), MirValidator rule (row 11), the 30 `MirDumpTest` goldens and the semantic snapshot's MIR dumps (role text only) | src +15, goldens ±30 | `MirBuildTest`: a Result temp on a return leaving a cleanup scope; none without cleanup; none in a constructor (`ctor-return`); a tail construct into `_0` (`labeled-tail-v`). `MirValidatorTest`: a Result of another Type, a Result read otherwise → `InternalInvariant_Kd`. Done when every golden differs only by ` (Result)`; a mir Session (G226 lesson) |
| **U3 (W)** | §5 items 2, 3, 5, 6: `MirLoanSets` run interning with its test; `MirOrigins.Causes.cs`; `MirBorrows.Causes.cs`; edits in Values/Reach/Calls/Flow/Candidates/CheckSets/Checks/Reports/Solve and the conflict's callers; the `MirReport` rule; the ledger line; DIAGNOSTICS §4.3, the MIR Loan paragraph and fact rows; ARCH rows; CODEMAP; DECISIONS | src ≈ +360/−20, tests ≈ +420 | See the test list below the table |
| **U4 (G213)** | §5 item 8; DIAGNOSTICS' storage-limit sentence | src +40/−15 (+ a concern file if `MirDataflow.cs` is over), tests +60 | `MirInitTest`, `MirBorrowsTest`, `MirOriginsTest` arena tests assert the three facts (fail before). `MirDropsTest`: `Incomplete` names A1's index, −1 for an inline copy's. R5's gate unit can then drop its interim |
| **U5 (docs)** | PLAN (G213's R4 side, R5's gate row), STATUS (A5's derived labels in shadow; the conservative readings of §4) | docs | Documentation-only, so no review |

**U3's tests.** In `T/MirBorrowsTest.Causes.cs`, at the MIR-problem layer; derived rows fail before, pins pass before.

- **Derived:** A's list plus B's extra rows:
  - j-region, j-bad-v, row2-v, m-write4-v, m-store-v;
  - two-flows-v and jregion2 (two causes);
  - loop-store-v, id-return-v, rv-g-param, rv-g-param-i;
  - keepA (end D, write I);
  - rf-block/if/match-v2;
  - ledger shapes `e3c4d30c20af`, `0433a531b1ba`, `38df74b325ce`, `27e4888a2ae9`;
  - write-through `d4a09871a00b` and `write-through-store`;
  - `content-retention`;
  - containers `f02fb5c218bc`, `container-deep`, and the closure append `3c4ffb8ea44b` (2c-6 re-expects it if environment classes
    change);
  - `noncfl-candidate-v`;
  - `store-every-origin`.
- **Independent pins:**
  - m-write (SPEC's `keep`) and its var forms, m-write3, m-scope, m-scope-use;
  - m-twice-v, same-root-v, m-pair-v, fK;
  - r3-b1c, r3-b1d-v, rc-fT-v, rc-flow, r2-b1a-v, r2-b1b-v;
  - g-two, g-return, cf-g-reassign (S at the return present), rv-g-annot-esc, no-twin;
  - `.Some(local@ref)`, `put(slot@uniq, …)` (`6227f1ba89a2`), a part read of a call result (`6d75d7b343f0`);
  - `merge-derived-direct-v` (one I);
  - rv/guard-annot (S kept);
  - PreparedInput (no `Cause`).
- **Twins' limit:** j-bad and m-write4 literal forms report the relation only.
- **Repair:** j-bad-v.r1 and m-store-v.r1 lose the conflict.
- **`MirAnalysisTest`:** `Add`'s index; a derived and a direct report of one key give one direct problem; two derived reports with
  different causes give their union.
- **Contract test (A, PD31):** every `Cause` names an Unsatisfied problem of the same body whose `Reason.Longer` is the Borrow's atom.
- **`MirDifferenceTest`:** a derived problem leaves the MIR side.
- **V16:** W stores, W aggregates, W joins (A), W reads through E (A-B1's shape), and a valid shape's Work and Bytes equal with and
  without a Refuted relation elsewhere.
- **Warm 0 B:** the driver's step 4 over the derived programs.
- **Existing classes:** `VerifiedSpecExampleTest` (15.6.5-derived-conflict's Dependent row), `MirOriginsTest` (Judge's report),
  `OriginRowsTest`.

**U3 is done when:**

- every derived row passes and every pin holds;
- Unit classes, a default Session and a mir Session pass;
- the diagnostic snapshot is byte-equal;
- the mir ledger loses exactly the derived problems' new-side rows (16 problems on `65be867e9`'s 23 programs, re-measured at the
  unit's base) and gains none;
- the V16 rows and warm 0 B pass;
- the Loan stratum on the valid stress shapes is within noise of base (TC=0 interleaved), and the library's `MirAnalysis.Run` is
  within +1 % (H; the implementer measures, and profiles with the sampler if above).

**Order.** U1 → U2 → U3 → U4 → U5 on one R4 branch stacked on `claude/a5-2a-1c`. U4 is independent of U2 and U3 after U1. Then
2a-4 and 2a-5 and the group review of G:A5-F4 (U1, U3 and U4 under Q6 form 2; U2 with R5's review).

## 7. Departures for the owner (A §11 merged with B's OQ-W3)

1. 2a0-D D13 / §6.1's order "Judge, MirBorrows per view, then Record" → Judge records before the Loan stratum (rule 2, its reason,
   is superseded).
2. R4-M104's direction (H) "the link from a MIR transfer to its relation row", 2a0-D D2's `ResultFit` Source and D18's `Result` fact
   → the builder's `Result` role (validated) and MirOrigins' `Fixed`. No row link.
3. 2a0-D §10's 2a-2 row (its tests, done condition, size and the file `MirOriginsTest.Merge.cs`) → §6's units and
   `MirBorrowsTest.Causes.cs`.
4. R4-M71's `MirReport` contract "a merge keeps the first problem's facts" → a merged problem is derived only when every report is,
   its causes the union. The key is unchanged.
5. "A `MirFact` is a site" (R4-M3a, R4-M71) → "a site and its kind's typed value (a long)".

Dropped from the designs' lists: A's item 4 (C uses the exact R4-M96 scan) and B's item 5 (C keeps the plan's ledger wording). B's
item 3 is already R4-M104's supersession.

## 8. Owner questions (OQ-A1/OQ-A2 merged with OQ-W1–OQ-W3)

**OQ-1 (OQ-A1, OQ-W3).** Confirm §7's departures as one list.

- Options: (a) all; (b) per item.
- **Recommended: (a).** Each item follows from R4-M104/R4-M122 or from a fact the round found.

**OQ-2 (OQ-A2, OQ-W1; R5's contract, R5 confirms first).** How is a derived MIR Loan problem published?

- (a) `PrerequisiteUnavailable_Kd` with `DerivedFrom` = its causes' check keys. §4.3's invariant is unchanged, and the record is
  suppressed whenever its causes are published.
- (b) Its own Loan code with `DerivedFrom`. The invariant gains a MIR exception and `DiagnosticCollection`'s validation changes.
- (c) Not recorded. Rejected by R4-M104.
- **Recommended: (a).** Same published result while the causes publish, with no contract change.

**OQ-3 (OQ-W2).** A call that stores a borrow into a container through a parameter or capture.

- (a) Conservative (B: independent).
- (b) BU extends the retention view.
- (c) A5 reads the relation row.
- (d) The receiving check over every Origin of the written referent's Type (A's `Fixed`): derived now, within MIR.
- **Recommended: (d).** No owner decision is needed if the planning session adopts C. The closure forms rest on today's environment
  classes, which 2c-6 re-expects.

## 9. What R5 must confirm (A §12 merged with B §10)

1. The row `MirFact(Kind, Site, long Value)` as H3's input, and the kind → record mapping:
   - `Required`/`Limit` → Arguments;
   - `Table` → the `table` evidence;
   - `Relation` → H2's row;
   - `Cause` → `DerivedFrom`;
   - the others → related locations and evidence.
2. OQ-2 (a).
3. H3 resolves a `Cause`'s index to the check key it publishes for that problem after R4-M71's merge across bodies, and merges
   derived-ness the same way: derived only when every merged problem is, with the causes unioned. This extends AR-7 to MIR-to-MIR
   causes, beside 2g-1's Binding causes (Value −1).
4. The ledger line in `tests/xUnitTest/MirDifferenceLedger.ComparedSet.cs` (shared file, hold adv), and that R4 edits it in U3.
5. U2's hold on `Mir/Build/MirBuilder.Transfers.cs`, `Mir/MirVocabulary.cs`, `Mir/MirValidator*.cs` and the MIR goldens and
   semantic-snapshot dumps, plus R5's review of the role and its validator rule.
6. The gate (A-2c, G213):
   - `Incomplete` = (Code, SourceInfo, report index);
   - R5's unit records `OwnershipStorageLimit_Kd` with the three facts, applies KD12 through `Subject(item, body, source)`, and deletes
     R5-M96's interim and the `SourceRef` overload;
   - who writes the gate's edit.
7. No further H3 need for A5's codes (PD31's rows Reason, Relation, Anchor, `contained` and Cause; Q18's Loan facts).
8. 2g-5's PreparedInput rule never drops a problem another problem names as a cause. This is unreachable today; §4.3 would publish
   such a derived record unsuppressed.

## 10. Probes (all in `...\s2-2a-2\probes-rev\`)

- **Builds:** `build3.sh` builds base, A and B. A variant `a2` adds a `Caused` early-out (`MirOrigins.Refutes`). Each build was copied
  out as `bin-*` and run from the copy. The bins were deleted afterwards.
- **Probe test:** `ScratchRevProbeTest.cs` (never committed): `Run`, `Cost` and `Library`.
- **Comparison:** `compare3.py` compares problem keys and labels across the variants and against the round's `.on.out`.
- **Readings:**
  - `round\` (the round's 113 programs): equal keys, and labels equal to the round (19 derived of 69 Loan problems) for A and B.
  - `corpus\` (A's 1,240 sources of `65be867e9`): equal keys, 1,080 Loan problems, A 14 derived and B 11. 10 sources fail the test
    helper's compile in every variant. The result is in `corpus-compare.txt`.
  - `ledger\` (B's 23 programs).
  - `cases\` and `cases2\` (my adversarial programs; `cases2` holds the var forms and dumps).
- **Cost:**
  - `cost-*.txt` hold Work and Bytes for the shapes copies/joins/aggregates/calls (± a Refuted relation), escapes, escape-reads,
    escape-aggs and escape-copies.
  - Timing was tiered (`tiered\`; noisy under load), TC=0 shapes (`tc0\`, `tc0b\`), and TC=0 library (`t-lib-*.txt`).
  - Scripts: `timing*.ps1`, `summarize*.py`, and `tokens.py` (an approximate count).
- **Not run:** no heavy run (no Session, snapshot or full ledger). MIR classes and the warm 0 B test were not re-run.

## 11. Unverified

- A-B2's reachability: per-view different causes in a view-dependent body was not constructed.
- That gating all W setup removes A's +2–3 % library overhead (row 2; a2's partial gate gave +2.2–2.5 %).
- C as a whole: no combined prototype ran. The union of 16 is inferred from rows 7 and 8.
- Inline default copies and held views with W elements; raw-pointer arrivals; the twins after H11.
- `Fixed`'s abstract-part and raw-layer exclusions, and its cost on DAG-shaped Types (G140).
- The structure-limit status of `MirOrigins.Chains.cs`, `MirDataflow.cs` and `MirDrops.cs`. My count is approximate; whether comments
  count is the reviewer's call at the unit.
- MIR classes, warm 0 B, Sessions and the full mir ledger were not re-run here. Both designers report their class runs; U1 and U3
  produce this evidence.
