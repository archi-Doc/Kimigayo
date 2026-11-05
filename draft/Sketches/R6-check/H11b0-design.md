# R6-check H11b-0: the parts of a definition whose failure they do not read (design, revision 2)

**Revision 2 (2026-11-06):** accepted by the owner (§14, "Accept with B-4 fixed"; 「Aでお願いします」) after BU's review C-44 (re-check 2's B-4 applied).

Designer: R5's H11b-0 agent (2026-11-05). Read on origin/dev `85996e52e` (Binding unchanged on dev `97d6fbab4`). Revision 1 answered
BU's review C-44 (B-1, B-2, N-1 to N-6) and R4's re-consent (§8); revision 2 answers BU's re-check 1 (B-3: §2a's two facts) under the
owner's 「A：続ける」 (§10, §11, §12); changed rows are marked (B-1), (B-2), (B-3) or (N-n). Revisions 0 and 1 are kept as
`H11b0-design.r0.md` and `H11b0-design.r1.md`; revision 2's throwaway is `cli-r4` (fact dumps `edge5.txt`, `edge6.txt`; census `wide4-*.txt`; `cli-r3`/`wide3-*` was its first form, whose
predicate was limited to definitions). Probes: worktree `bin/worktrees/r5-h11b0` (left in place; its edits are reverted;
its bin/ holds probe-built DLLs); CLIs `cli-dev`, `cli-probe` (revision 0's naive row) and `cli-r2` (revision 1's throwaway D2-D4
behind environment switches, never committed); a throwaway fact dumper (every Binding and MIR fact with its `DerivedFrom`); programs
`p/`, `q/`-`q8/`, `r/`, `r2/`; outputs `census-cli-*.txt`, `wide-diff.txt`, `wide-dev-*.txt`, `edge*.txt`, `mirproblems.txt`; all in this folder.

## 0. Findings (probes first)

| # | Finding | Evidence | Consequence |
| --- | --- | --- | --- |
| F1 | R4's (c) works where the parts read no failed input: the body is built and its MIR-only records appear. | c01/c02 (the two pins) +A5 `return local@ref`; c03, c08, c10, u3 +A1 `MovedPlace_Kd`; u5 +A3 `RequireFallthrough_Kd`. | The census's expected gains (Q13: rejected programs only). |
| F2 | R4's first risk is real: the naive row gives `InternalInvariant_Kd` at the definition in 6 programs. | c06, c06w, s2, s3, s3-nobody (2+ pair binders widened by F4: 9 Semantics each, 81 > 64, MirValidator's `Binder` rule, inferred); u2 (a failed result Type). | The row cannot be "BindingFailure is InvalidOrigin" (F3, F4). Under revision 1 none remains (§3). |
| F3 | Masking (dev defect D-2): FailOriginClause fails the owner in its header, so its resting InvalidOrigin takes the one failure slot first; `Fail` drops a later definition failure (DIAGNOSTICS rule 4, "its first failure sets"). | w1, w1p: a bad receiver's `InvalidTypeFormation_Kd` is lost beside the failed clause (present without it, w1n, w1pn); CaseLimit is never recorded behind it; u2: a failed result Type is no definition failure. | The slot must mean "the clause alone" (D3), and the signature is a separate condition (D2). |
| F4 | (B-2) Binder widening is a defect (D-1; BU's ruling, SPEC 8.3.4, 15.3.3, 23.1.2): InvalidOrigin makes the definition a failed declaration context (`InvalidDeclarationCause`, ConstraintOperands.cs:64-72, InvalidOrigin not among `KeepsSignature`'s failures), so `OperandContexts` marks Types naming its own inputs Invalid and the body's constraint and capability proofs return Error. | t1, t2, s1-s3, c06: `TransferRequired_Kd` at `value@a@move`; with D4 (a) on the throwaway, none (`edge4.txt`). | D4 (a), H11b-0's step-0 commit. |
| F5 | Callers rest on the definition's failure (the use's cause, §2a fact (i)). | dev: w0p, w1p, c09, e1: the call's dependent record rests on the clause, through the definition's edge. D4 (a) applied at `InvalidDeclarationCause` itself (the call path's read too) loses w0p's caller dependent entirely (`edge3.txt`, KEEP/DROP), so the hunk belongs to `OperandContexts`' read only; there, w0p, c09 and e1 keep it (`edge4.txt`). | D4 (a)'s location (B-2). |
| F6 | The dependent class is real and the naive row publishes it direct: a fixed source reaching a Fixed node through an Inferred node is A5's verdict; a fixed-fixed end is Binding's alone. | x1, y2: +2 A5 `UnprovenOriginRelation_Kd` at `z` (BU-69 derives the same relation for Binding's record, c04); x2: +1, independent; y1 (dev): 2 such A5 records under an enclosing failed contract (BU: derived by `RepairHolds`/`Below`, probe o5a). w0x (revision 1's throwaway, no D6): +2 at `z` direct. | D6 lands before D2. |
| F8 | (B-3) The conflated fact has two meanings: (i) the cause a use of the declaration rests on, (ii) the failed context of the Types the declaration binds. An own failed Origin clause is (i) but not (ii) (SPEC 8.3.4, 15.3.3, 23.1.2). Revision 1 moved one (ii) reader; the others kept (i)'s answer. | f1 (BU): revision 1 keeps `InvalidConstraint_Kd @Box<T>.init(t)` (`BeginTypeFormation`) and lacks f1n's independent `TransferRequired_Kd @t`. Revision 2 (every (ii) reader moved): f1 and f2m lose the first and gain the second and f1n's `MovedPlace_Kd @o`; w0p, c09, e1 keep their callers' dependents (`edge5.txt`). | D4 (B-3): two facts and a reader table (§2a). |
| F7 | (B-1) The clause edge decides the yielded failure's derivation (§9). | Kept: w1's receiver record stays derived from the clause and the caller rests on the clause; the reverse-order twin w2 shows both direct. Dropped: w1 and w2 give the same records. | D3 drops the edge with the yield. |

## 1. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | The closed list (§2): one row, InvalidOrigin (the owner's resting failure from FailOriginClause), under D2's signature condition. (B-2) No binder condition: D4 (a) removes the widening. Every other failure stays out, with its reason in §2. | The parts of an InvalidOrigin definition read its Types (bound before and apart from the clauses), its binders (narrowed by its own `is` clauses once D4 (a) holds) and the premise oracle; only the oracle reads the failed contract (D6). | `Binding.Hir.Operations.cs` |
| D2 | `PartsPublished`: `Resolved`, or an Invalid definition whose failure is a row of a closed table (`PartsRead(BindingFailure)`: InvalidOrigin true, every other false; a new failure adds a row) and whose signature resolved (`IncompleteSignature` null, extended to a Property's Type). The signature condition is common to every row. After D3 the slot's InvalidOrigin is the own clause alone (N-6). | u2: a failed result Type hides behind InvalidOrigin and the builder breaks; c07, u1: a failed parameter Type, out. | `Binding.Hir.Operations.cs`, `Binding.Functions.cs` (helper visibility) |
| D3 | (B-1) Exact slot (fixes D-2): a resting InvalidOrigin yields. `Fail` on a node for which `RestsOnOwnClause` holds (one predicate over every owner of an Origin declaration, a definition, a container or a local: its failure is InvalidOrigin, its declaration's `Failure` is the clause, and its `partPrerequisites` entry is that clause; IsDerived's InvalidOrigin branch, this yield and §2a's fact (ii) read it; (B-3) a throwaway that limited it to D2's definitions lost IsDerived's branch for local and container owners, +29 direct `InvalidOriginBinding_Kd` (28 local-clause suite programs and o5b), so the predicate keeps IsDerived's domain) writes the later failure in its place **and removes the clause edge** (`partPrerequisites[node]`); a later `unresolved` failure then marks the node Unresolved as for any first failure (`MarkUnresolved`, no part left). Only the own clause's InvalidOrigin yields; CaseLimit and every other failure keep "first failure". The Origin contract stays unavailable (FailOriginDeclaration, BU-69's `RejectedRelations`); dependents that read it keep their own causes (§9). | The edge is InvalidOrigin's explanation; kept, `RestsOnRecovery` derives the yielded failure from a recovery clause (w1 KEEP) and derivation depends on which failure is written first (w1 vs w2). Dropped, w1 and w2 agree (§9). | `Binding.Diagnostics.Prerequisites.cs`, `Binding.CaseLimits.cs` (comment) |
| D4 | (B-2, B-3) Option (a), H11b-0's step-0 commit (R5 implements under C1; BU reviews the hunk), at the cause: `ConstraintOperands` publishes the two facts the conflated one carries, both its own (§2a). (i) The use's cause, `InvalidDeclarationCause`, `InvalidDeclarationContextCause`, `InvalidDeclarationContext`: unchanged. (ii) The Types' failed context, `FailedTypeContext` and `InvalidTypeContext`: the use's walk (`FailedDeclarationContext`), except that a declaration for which `RestsOnOwnClause` holds is passed and the walk goes on from its parent, so an enclosing declaration that failed otherwise still counts. Every reader of today's fact reads the one its question names (§2a); none keeps a check of its own. `InvalidOperandContext` (BU-75's cause partner of `OperandContexts`) walks (ii) too, so verdict and cause agree. | SPEC 8.3.4, 15.3.3, 23.1.2 (BU's OQ1); f1, f2m (B-3); w0p: (i) unchanged keeps the caller's dependent; no bool or mode steers a walk: (ii) is named on (i). | `Binding.ConstraintOperands.cs` (both facts; the predicate is D3's, in `Binding.Diagnostics.Prerequisites.cs`) and the (ii) readers' files of §2a |
| D5 | Builder and validator: no change. After D2-D4 (revision 2's throwaway) no census body gives a validator finding, `InternalInvariant_Kd` or `Unsupported_Kd` (§3, revision 1's throwaway); shown again on the final head (R4's condition). | F2's six are excluded: u2 by D2; c06 by D3 (CaseLimit takes the slot); c06w, s2, s3, s3-nobody become bodies within the bound by D4 (a). | none outside D6 |
| D6 | Dependence: Binding owns it (BU-69). The view gains `TyCtxt.DependenceCause(body, site, longer, shorter, caseBits)`, answering Binding's `DependenceCause` (one premise extraction at the site with `RejectedRelations` and the repairs, the body's own and enclosing failed contracts, under the problem's Semantics condition) with the failed clause's `SourceRef` or none, asked only when the oracle's environment read a failed contract. A5, for each fixed source the oracle does not derive, asks it once and, when it names a clause, adds `Cause(clause, -1)` (RestingCauses, R4-M132). A5 decides nothing. R4 implements A5's part on request (§7 (2)). | x1, y2, w0x: the same relation is derived for Binding's record and direct for A5's. y1: BU confirms the enclosing contract is read (OQ5). | `Binding.OriginDeclarations.Failures.cs` (internal entry), `TyCtxt.Origins.cs`, `Mir/Analysis/MirOrigins.Causes.cs` (R4) |
| D7 | (N-1) DECISIONS: one new entry superseding R4-M18's "a rule of every unresolved function" (kept for CaseLimit and every unlisted failure) and amending its "only as its first failure" to "as its first failure but the resting InvalidOrigin of its own clause, which yields with its edge", with the pointer lines in R4-M18's header (§7 (c), §8); it names D2's table, D3's yield, D4 (a)'s failed-context rule and D6. DIAGNOSTICS rule 4 (`DIAGNOSTICS.md:27`, "its first failure sets") gains the same exception under its "documented contract changes" condition. BU-62 gets no line. CaseLimits', `TyCtxt.Bodyless`' and `PartsPublished`'s comments follow. | AGENTS: a superseding entry names the reason that no longer holds (R4-M18's holds for CaseLimit only). | `docs/dev/DECISIONS.md`, `docs/dev/DIAGNOSTICS.md` |
| D8 | (N-2) G414's `Keeps` is another contract (which children an Error keeps inside a published body), so it stays separate from D2's table (whether a definition's body is published); AGENTS' "a similar shape under another contract stays separate". They compose at defaults (`PositionOf` reads `PartsPublished` for a default): a census row (an InvalidOrigin function whose default holds a rejected labeled `do`) runs after Q-55 lands, and H11b-0 rebases on it. A local whose own clause failed stays G414 (2)'s. | BU's N-2; §6's OQ4 line is superseded by this. | none |

## 2. The closed list

| Failure of the definition | What its parts read | Row |
| --- | --- | --- |
| InvalidOrigin (FailOriginClause's owner; no other site fails a definition with it) | signature Types (bound before CompleteOriginDeclaration), its binders and their `is` clauses (D4 (a)), the premise oracle (the failed contract: A5's fixed-source verdicts only, D6) | **publish** under D2, after D3 and D4 (a) |
| CaseLimit | every view reads the failed case product (R4-M18); takes the slot from a resting InvalidOrigin (D3; c06) | out |
| a failed signature Type (no definition failure: Unresolved through the Type, or behind InvalidOrigin, u2, c07) | parameter and result Types, the locals the builder forms | out (D2's condition) |
| Unsupported (anonymous, unmatched specialization, override forms, captures) | an unimplemented form | out |
| InvalidTypeFormation (receiver, indexer, accessor, specialization) | `self`'s or the accessor's Type | out (w1, w2: takes the slot, D3) |
| Unresolved (a dependent definition) | a failed input by definition | out |
| ProtectedPlacement, ParameterShapeMismatch, ReceiverShapeMismatch | probably nothing failed | out; later rows with their own census (§6 OQ3) |
| Access, Duplicate, VirtualDeclaration, override and specialization failures, AccessorReceiverShape, TypeMismatch (accessor, closure), InvalidConstraint, InvalidAssociatedType, any other | unproven | out |

## 2a. D4's two facts and their readers (B-3; dev `85996e52e` line numbers)

Fact (i), the use's cause: the declaration a use selects, references or verifies failed, or lies in a failed declaration context, and the
use rests on that failure (`InvalidDeclarationCause`, `InvalidDeclarationContextCause`, `InvalidDeclarationContext`; unchanged). Fact
(ii), the Types' failed context: a Type, Contract or conformance whose declaration lies in a failed declaration context has no valid
formation or premise there (`FailedTypeContext`, `InvalidTypeContext`, named on (i)'s walk); a declaration whose only failure is its own
clause's InvalidOrigin (`RestsOnOwnClause`) fails no Type it binds, and the walk goes on outward.

| Reader | Question | Fact | Why |
| --- | --- | --- | --- |
| `ConstraintOperands.cs:55` `InvalidCalledDeclaration` | the declarations a call selects | (i) | a call rests on its callee's failure (w0p, c09, e1 keep their dependents) |
| `CallSelection.Candidates.cs:169`, `CallSelection.cs:155` | a candidate function in a failed context, and the evaluation's cause | (i) | applicability is a use of the function |
| `ConstraintValidation.cs:163` `CheckCallTypeConstraints` | the call's presented target | (i) | the call's own constraints rest on its target |
| `FunctionReferences.cs:90` | a function reference's target | (i) | a use of the function |
| `Properties.cs:326` | a Property's certificate (`IsVerified`, read by hover and `InstanceCollector`) | (i) | the Property's own failure or context |
| `ConditionalMembers.cs:36` | a conditional member's declaration | (i) | the member a use selects |
| `Attributes.cs:372` `CExchangeable` | a struct's C exchange | (i) | it reads the declaration's own failure first (`BindingFailure != None`), so any failed struct, an own-clause one included, stays unknown under either fact; no record changes |
| `ConstraintOperands.cs:109` `OperandContexts` (through it `Capabilities.cs:220`, `:338`, `ComponentSummaries.cs:297`, `ConditionalConformances.cs:343`, `Constraints.cs:159`, `:603`) | the declaration context of each part of a Type | (ii) | a Type's validity (t1, s2: D-1) |
| `ConstraintOperands.cs:126-130` `InvalidOperandContext` (through it `ComponentSummaries.cs:302`, `OwnedConversions.cs:48`) | the cause partner of `OperandContexts` (BU-75) | (ii) | the cause names the context the verdict counted |
| (B-4) `Properties.cs:402` | the Property's own proof: the formation of `property.Type` and the accessor's Result, Input and Receiver (`:408-427`), with that context's `InvalidDeclarationCause` as the prerequisite | (ii) | pr1: a computed Property in a struct whose own clause failed keeps pr1n's independent `UnsatisfiedConstraint_Kd` |
| `ConstraintOperands.cs:89`, `:95` | a proposition's or a constraint's Contract | (ii) | a Contract is a Type-level declaration |
| `ConstraintValidation.Formation.cs:81` `BeginTypeFormation` | a formed Type's declaration | (ii) | f1: the formation of `Box<T>` |
| `ConstraintValidation.cs:329` | a Contract's ancestors | (ii) | Contract shapes |
| `ContractMatching.cs:176-177` | a conformance's Contract and Type | (ii) | a conformance relates Types |
| `ContractSolver.cs:89` `AvailableContractPremise` | a contract premise's sources | (ii) | premises about Types |
| `Enums.cs:559` | an enum case's owner Type | (ii) | a Type's declaration |
| `InheritedConformances.cs:114` | an inherited path's Type | (ii) | a Type's declaration |

The two facts differ only at a declaration for which `RestsOnOwnClause` holds; each reader is assigned by its question, not by the form
it meets. The predicate holds for any owner, so fact (ii) also passes a container whose own Origin clause failed: its members' Types
are no failed context (f2, f2s gain f2n's independent `TransferRequired_Kd @t` and lose the dependent `InvalidConstraint_Kd`), by the
same SPEC reading (an Origin clause fails no Type); a local owner binds no Type, so (ii) never meets one. Rejected: (ii) limited to D2's
definitions, which needs a form test inside the fact and keeps f2s's dependent (the throwaway's D variant, `edge6.txt`).

## 3. Census (B-2, B-3: option (a) with §2a's two facts; dev `85996e52e` vs revision 2's throwaway `cli-r4`, D2-D4 without D6)

| Program (folder) | dev records | revision 2 (D2-D4) | with D6 | Q13 class |
| --- | --- | --- | --- | --- |
| c01 pin 1, c02 pin 2 | clause record, B `local@ref`, TM | + A5 `return local@ref` | same | gain, rejected (H11b then removes the twin) |
| c03, c08, c10, u3 (moves) | clause record, ... | + `MovedPlace_Kd` (+ A5 `return local@ref` in c03, c08, c10) | same | gain, rejected |
| u5 (require) | clause record | + `RequireFallthrough_Kd` | same | gain, rejected |
| c04, c05, v3, w1c (fixed-fixed ends, BU-69 derived at their own site) | clause record, TM | unchanged | unchanged | none |
| v1, v2, c05i (fixed-fixed, BU-69 independent) | B record, ... | unchanged | unchanged | none |
| x1, y2, w0x (fixed sources through an Inferred node, BU-69 derived) | clause record, TM | + 2 A5 Unproven at `z` (direct: the reason for D6) | unchanged (derived) | none |
| x2 (the same, independent) | `MissingOriginBinding_Kd`, TM | + 1 A5 Unproven at `z` | same | gain, rejected |
| y1 (Resolved method, the struct's clause failed) | clause record, 2 A5 Unproven at `z`, TM | unchanged | the 2 become derived (BU: OQ5) | loss of dependents, rejected |
| t1, t2, s1, s2, s3, c06w, s3-nobody (binders; D-1) | clause record, `TransferRequired_Kd` (none in s3-nobody) | − `TransferRequired_Kd`; s2, s3, c06w, s3-nobody bodies published within the bound, no finding | same | loss of dependents, rejected (s3-nobody unchanged) |
| c06 | clause record, 2 `TransferRequired_Kd` | − TransferRequired, + `OwnershipCaseLimit_Kd` (D3), out | same | dependents lost, an independent gained, rejected |
| u2 (failed result Type), c07 | ... | unchanged (D2) | unchanged | none |
| w1, w1c, w1p, w1x (receiver after the clause) | clause record, the definition's derived record; caller on the clause | + `InvalidTypeFormation_Kd` (D3), caller on it; the definition's derived record leaves | same | gain, rejected |
| w2, w2c, w2p, w2x (receiver before the clause) | clause record, `InvalidTypeFormation_Kd`; caller on it | unchanged | unchanged | none |
| w0p, c09, e1 (callers of an InvalidOrigin function) | caller rests on the clause | unchanged | unchanged | none (calls still rest on the clause) |
| (B-3) f1 (BU: `Box<T>.init(t)` in a generic function with `origin a = b`) | `TransferRequired_Kd @item`, clause record, `InvalidConstraint_Kd @Box<T>.init(t)` | − `InvalidConstraint_Kd`, + `TransferRequired_Kd @t`, + `MovedPlace_Kd @o`: f1n's records plus the clause's | same | dependent lost, independents gained, rejected |
| f1n (no clause) | `TransferRequired_Kd @item`, `@t`, `MovedPlace_Kd @o` | unchanged | unchanged | none |
| f2m (f1's method in a struct, the method's clause failed) | as f1, + TM | as f1 | same | as f1 |
| f2 (both the struct's and the method's clauses failed) | `@item`, 2 clause records, `InvalidConstraint_Kd`, TM | − `InvalidConstraint_Kd`, + `TransferRequired_Kd @t`, + `MovedPlace_Kd @o`: f2n's records plus both clause records | same | dependent lost, independents gained, rejected |
| f2s (the struct's clause only) | `@item`, clause record, `InvalidConstraint_Kd`, `MovedPlace_Kd @o`, TM | − `InvalidConstraint_Kd`, + `TransferRequired_Kd @t` (the method is Resolved; fact (ii) passes the container) | same | dependent lost, an independent gained, rejected |
| o5a, o5b, o5c, y1 (struct clauses, BU-69), 28 local-clause suite programs (`h11b\impl\p2`) | ... | unchanged | y1: D6 | none (y1: D6) |
| (B-4) pr1 (a computed Property `v: Need<i32>` in a struct whose own clause failed) | clause record, the Property's `InvalidConstraint` derived | pr1n's records plus the clause record (`UnsatisfiedConstraint_Kd @computed v: Need<i32>` independent) | same | an independent gained, rejected |
| pr1n (no clause) | `UnsatisfiedConstraint_Kd @computed v: Need<i32>` | unchanged | unchanged | none |
| f2n (no clause) | `@item`, `@t`, `MovedPlace_Kd @o`, TM | unchanged | unchanged | none |
| u3 (generic `t: T`, `return t`) | clause record | + `MovedPlace_Kd`, + `TransferRequired_Kd` at `t` (independent: dev reports it without the clause, u3n; D-1 hid it) | same | gain, rejected |
| **wide census**, 657 programs (R5's H11b probes, step 0's axes and suite programs, R4's 8, BU's f1, f1n, o5a-o5c, `p/`, `q/`-`q8/`, `r/`, `r2/`; `wide4-dev-G.txt`, the rejected variant `wide4-dev-D.txt`) | 167 accepted | 34 changed, 167 accepted: +10 A5 `UnsatisfiedOriginRelation_Kd`, +10 `MovedPlace_Kd`, +1 `RequireFallthrough_Kd`, +5 `InvalidTypeFormation_Kd`, +1 `OwnershipCaseLimit_Kd`, +6 `TransferRequired_Kd` (u3, f1 twice, f2m, f2, f2s), −9 `TransferRequired_Kd`, −5 `InvalidConstraint_Kd`, +7 A5 `UnprovenOriginRelation_Kd` (x1, y2, w0x, x2); no `InternalInvariant_Kd`, no `Unsupported_Kd`, no clause record lost, no local or container owner's record changed; the D variant differs only at f2 and f2s | 32 changed: x1, y2, w0x leave (6 records derived), y1 joins (2 derived) | gains only 19, gains and lost dependents 6 (c06, f1 twice, f2, f2m, f2s), lost dependents only 7 (c06w, s1-s3, t1, t2, y1); 0 acceptance moves |

Verdicts: every changed program is rejected on dev for the same definition's own Binding error; no acceptance changes. Q13: gains
and losses of dependents in rejected programs only; the clause's own record (OriginRelationOperator_Kd, InvalidOriginBinding_Kd,
MissingOriginBinding_Kd) appears in every changed program (R4's (b)). (N-5) Before commit (3), step 0 adds Properties and accessors
with a failed clause, defaults read by callers, effect bounds and the OriginRecoveryTest suite; any `InternalInvariant_Kd` there blocks.

## 4. Class, reviews, consents, commits, checks, sizes

- **Major**, one design review by BU's reviewer (C1); BU's same reviewer line-checks B-1 and B-2. Consents: BU to the Binding hunks
  (D2, D3, D4 (a), D6's Binding entry, the DECISIONS entry and DIAGNOSTICS rule 4: YES on conditions, C-44); R4 to D3 as amended (§8)
  and to D6 as R4's part (§7 (2)).
- (N-6) Commits, in this order, each within Q12's hour:
  (0) (B-3) D4 (a): `RestsOnOwnClause` (declared with D3's file) and §2a's fact (ii), `FailedTypeContext`/`InvalidTypeContext`, with
  its 12 readers moved (B-4: `Binding.Properties.cs:402` too; `ConstraintOperands.cs`, `ConstraintValidation.Formation.cs`, `ConstraintValidation.cs`, `ContractMatching.cs`,
  `ContractSolver.cs`, `Enums.cs`, `InheritedConformances.cs`); behavior: t1, t2, s1-s3, c06, f1, f2m lose D-1's dependents, f1, f2m
  and u3 gain the independents D-1 hid; tests: t1/t2 and f1 (f1n's records), s1n/t3/f1n twins unchanged, f2 (the enclosing failure
  passed when it rests on its own clause too), f2s (fact (ii) passes the container), the local-clause programs and o5b unchanged
  (IsDerived keeps its domain), w0p/c09/e1 callers still on the clause.
  (1) D3: the yield with the edge removed; behavior: w1-type gains, c06's CaseLimit; tests: w1 and w2 with equal records (R4's (a′)),
  w1p/w2p callers on the definition's failure, w1c's fit derived at its own site, the clause's own record direct.
  (2) D6 (R4's A5 hunk on request): y1's two records derived; MIR-layer tests: y1 and x1-type Cause facts, x2 direct, a CaseLimit
  counterpart unchanged; warm 0 B.
  (3) D2: the table; the bodies appear (pins gain A5's `return local@ref`, a test change stated in the commit); x1/y2/w0x unchanged
  because (2) precedes it; DECISIONS entry and DIAGNOSTICS rule 4.
- Risks checked on the throwaway: the yielded failure is direct only with the edge removed (§9, re-run on revision 2: the same);
  (B-3) fact (i) keeps every use's cause (F5); fact (ii) moves every Type reader (F8, §2a).
- (N-4) Checks: Unit classes of Binding Origin recovery (OriginRecoveryTest), CaseLimit (GenericAdaptationLimitTest,
  GenericCaseDiagnosticTest), MirBuild/MirValidator, the 4 *ScalingTest classes; the Session's diagnostic and semantic snapshot
  classes; the generated gate before review; the Session at the end.
- Sizes (estimate): Binding 35-55 lines (fact (ii) about 10 and 11 one-line reader changes), view 15-25, A5 15-25, tests 90-120,
  DECISIONS and DIAGNOSTICS 12-18; 3.5-5.5 h over 4 commits.

## 5. Open questions for R5

1. (N-3) Pin y1, x1 and y2 as derived "by BU-69's guessed-operator branch", with their no-clause twins direct, and a missing-name
   twin where it applies; BU's PLAN G446 owns the branch's limit.
2. (B-3) Closed: fact (ii) passes every own-clause owner and counts an enclosing declaration that failed otherwise. BU confirms that
   a container's own failed clause fails no Type its members bind (f2s, f2), the same SPEC reading as D-1; if BU limits (ii) to
   definitions instead, the rejected variant of §2a applies and f2s stays a named open issue.
3. The Q-55 default row (D8) runs after Q-55 lands; H11b-0 rebases on it.

## 6. R5's rulings (2026-11-05)

- D1-D3, D5-D8 accepted as written (one closed table keyed by failure, InvalidOrigin its one row, under the common signature condition;
  D3's yield of a resting InvalidOrigin fixes the slot conflation at its cause; D6 lands before D2).
- OQ1 (D4 (a) or (b)), OQ2 (D3's amendment of BU-62/R4-M118's first-failure rule) and OQ5 (y1: BU-69's query and an enclosing
  declaration's failed contract): BU's answers, asked on the board (C-44) with the design's review; OQ2 also asks R4 (C-45).
- OQ3: later rows (ProtectedPlacement, the two shape mismatches) are not H11b-0's; each joins the table with its own census later.
- OQ4: one table with G414 (2)'s `Keeps` when G414 (2) resumes (BU's); recorded in H11b-0's DECISIONS entry as the intended form.
- OQ6: accepted (the two pins gain A5's record at H11b-0, a test change stated in the commit; H11b's D3/D4 then drops Binding's twin).
- (Revision 2 note, BU's re-check 1 N) Superseded: OQ2's "BU-62/R4-M118" by D7 (DIAGNOSTICS rule 4 and R4-M18's sentence; BU-62 gets
  no line); OQ4's "one table" by D8 (`Keeps` is another contract). The DECISIONS entry copies D7 and D8, not these lines.

## 7. R4's answer, board C-45 (2026-11-05)

- (1) YES to D3, with conditions: (a) the yield applies only to the resting InvalidOrigin of the node's own failed clause, through one
  predicate `RestsOnOwnClause` shared with IsDerived's InvalidOrigin branch, never to CaseLimit or another failure; the prerequisite edge
  to the clause stays; (b) the census shows only gains in rejected programs, no acceptance move, no lost record where the later failure
  replaces InvalidOrigin, and the clause's own record (OriginRelationOperator_Kd, InvalidOriginBinding_Kd) still appears; (c) D7 supersedes
  R4-M18's "a rule of every unresolved function" (kept for CaseLimit and every unlisted failure) and amends "only as its first failure" to
  "as its first failure but a resting InvalidOrigin", with two pointer lines in R4-M18's header; BU-62's line as BU consents.
- (2) YES to D6 as R4's part (`MirOrigins.Causes.cs`), implemented when R5 asks after BU's answers and the design review: A5 decides
  nothing; it asks `TyCtxt.DependenceCause` once per fixed source the oracle does not derive, only when `ReadsFailedContracts`; a named
  clause adds `Cause(clause, -1)` (RestingCauses, R4-M132); warm allocation 0 B; rows: derived under the failed contract, direct without
  the clause, a CaseLimit counterpart unchanged.
- Census: D5's "no builder or validator change" is shown on the final head (the naive row's six InternalInvariant_Kd excluded by D2/D4).

## 8. R4's re-consent (2026-11-05, relayed by R5)

- BU is right: the yield replaces the failure **and drops the definition's edge to the clause**; R4 withdraws "the edge stays" (§7
  (1) (a)'s last clause). The replacing condition (a′) is the probes' acceptance on w1 and its reverse-order twin, edge dropped:
  1. w1's `InvalidTypeFormation_Kd` appears in both orders;
  2. every record that reads the failed contract stays derived, naming the clause through its own cause: A5's problems via D6
     (`TyCtxt.DependenceCause` -> `Cause(clause, -1)` -> RestingCauses, R4-M132); Binding's fit and relation records via BU-69's
     dependence query at the record's own site and a declaration row's PendingFailure (BU-62); the clause's own record
     (OriginRelationOperator_Kd, InvalidOriginBinding_Kd) is direct; callers rest on the definition's failure whichever failure holds
     the slot;
  3. no record that reads only the yielded failure's input is attributed to the clause.
  If a derived record loses its only path to the clause when the edge goes, it is named; its owner then publishes the cause at that
  record's site, not through the definition's edge.
- The amended rule is DIAGNOSTICS rule 4 plus R4-M18's "only as its first failure", not BU-62; D7 says so and R4-M18 gets the pointer
  line. §7's other conditions ((b), (c), (2), the final-head census) stand.

## 9. Edge evidence (B-1; throwaway fact dumps `edge.txt`, `edge2.txt`, `edge3.txt`, `edge4.txt`)

w1 writes the clause's InvalidOrigin first (header) and the receiver's InvalidTypeFormation later (`BindFunctionHeader`); w2, its
reverse-order twin, writes InvalidTypeFormation first (two `self` parameters, at indexing) and the clause second, so FailOriginClause
adds no edge. Records are the dumper's facts ("from" = DerivedFrom).

| Program | dev | yield, edge kept | yield, edge dropped (revision 1) |
| --- | --- | --- | --- |
| w1, w1c | clause direct; the definition's `PrerequisiteUnavailable` from the clause; receiver record lost | the same as dev: the yielded InvalidTypeFormation is derived from the recovery clause through the edge (`RestsOnRecovery`) | clause direct; `InvalidTypeFormation_Kd` **direct**; no record of the definition from the clause |
| w2, w2c | clause direct; `InvalidTypeFormation_Kd` direct | the same | the same |
| w1p (caller) | caller from the clause | caller from the clause (the slot holds InvalidTypeFormation; order-dependent against w2p) | caller from `InvalidTypeFormation_Kd` |
| w2p (caller) | caller from `InvalidTypeFormation_Kd` | the same | the same |
| w0p, c09, e1 (no yield) | caller from the clause | the same | the same |
| w1c, w0c (fit `return x`, BU-69 derived) | no fit record (derived at its own site); no-clause twins: `UnprovenOriginRelation_Kd` direct | the same | the same |
| w0x (A5 chain, no yield) | body not built | + 2 A5 Unproven direct | the same: D6's records (A5's own cause, not the edge) |

- (a′) 1: holds (w1 and w2 give `InvalidTypeFormation_Kd` direct; kept, w1 loses it). 2: the clause's own record is direct in every
  row; Binding's fit records keep BU-69's cause at their own site (w1c, c04, edge-independent); A5's need D6 (w0x, edge-independent);
  callers rest on the definition's failure (w1p, w2p: InvalidTypeFormation; w0p: the InvalidOrigin with its edge). 3: holds dropped;
  kept, w1's receiver record and w1p's caller are attributed to the clause.
- Records that lose their only path to the clause when the edge goes: the definition's own derived record (it is replaced by the
  yielded failure, w1) and the caller's dependent in w1p (it rests on the direct `InvalidTypeFormation_Kd` instead, as (a′) 2 asks).
  Neither reads the failed contract through its own site, so no owner needs to publish a cause there. No other record of the probes
  changes between kept and dropped.

## 10. Q11: suspended (2026-11-05, R5)

BU's re-check 1 (C-44) is REVISE with B-3: D4 (a) sits only in `OperandContexts`, while `InvalidOperandContext` (BU-75),
`BeginTypeFormation`, `ConstraintValidation.cs:329` and `ContractMatching.cs:176-177` still read the definition as failed (probe
`bu\batch\H11b0\probe\f1_formation_clause`: InvalidConstraint_Kd @`Box<T>.init(t)` stays, the twin's independent TransferRequired_Kd @t is
lost). **R5 judges that B-3 shares B-2's cause**: one fact ("this definition's declaration context failed") carries two meanings, (i) the
cause a use rests on (calls rest on the failed clause: w0p's dependent) and (ii) the failed context of the definition's Types
(formation, constraint validation, contract matching, `is` premises), and an Origin clause failure is (i) but not (ii) (SPEC 8.3.4,
15.3.3, 23.1.2). Fixing one reader (B-2) left the others (B-3). Under DECISIONS Q11 the unit does not converge: it is suspended and the
owner is asked with this analysis, the options and a recommendation. BU's proposed fix is one level higher: `ConstraintOperands`
publishes the two facts separately (the use's cause, as today; the Types' failed context, skipping the definition's own clause and
counting enclosing failures), with a reader table and census rows f1, f1n and a both-clauses-failed form.

## 11. The owner's answer (2026-11-05, R5's dialog): verbatim 「A：続ける」

H11b-0 resumes with revision 2: the one-level-higher fix (`ConstraintOperands` publishes the two facts separately; every reader reads
the right one; a reader table; census rows f1, f1n and a both-clauses-failed form). BU's same reviewer line-checks it; a further
non-ACCEPT goes to the owner again (Q11).

## 12. Revision 2's answers to BU's re-check 1 (B-3)

- B-3: the cause is one fact with two meanings (F8, R5's §10). `ConstraintOperands` publishes both (D4, §2a): (i) the use's cause,
  unchanged; (ii) the Types' failed context, named on (i)'s walk, passing a `RestsOnOwnClause` owner and going on outward. All 11
  readers that ask the Types' question move to (ii), `InvalidOperandContext` with them (BU-75: verdict and cause agree); the 9 use
  readers keep (i). No reader tests a form.
- Evidence (revision 2's throwaway, `edge5.txt`, `edge6.txt`, `wide4-dev-G.txt`): f1 and f2m gain f1n's `TransferRequired_Kd @t` and `MovedPlace_Kd @o` and lose
  `InvalidConstraint_Kd @Box<T>.init(t)`; f2 and f2s likewise (the container's own clause is passed); f1n, f2n, o5a-o5c, y1 and the
  local-clause programs unchanged (one predicate with IsDerived's domain); w0p, c09, e1 keep their
  callers' dependents; §9's rows re-run with the same result (w1, w1c, w1p gain `InvalidTypeFormation_Kd` only with the edge dropped;
  w2, w2p unchanged; c06's CaseLimit appears only with the edge dropped); R4's (a′) holds.
- Kept: R4's (a′) and §7 (b), (c), (2); BU's conditions of C-44 (D4 (a) is BU-reviewed, N-5's forms before commit (3)); D6 as R4's part.
- For BU's line-check (needs BU's confirmation, §5 Q2): fact (ii) passes every own-clause owner, a container's included (f2, f2s), not
  only definitions as re-check 1 worded it; the definition-only variant needs a form test and keeps f2s's dependent.
- Unresolved: none blocking in the probes. Not observed: a reader of fact (ii) reached through a Contract or conformance declared inside
  an own-clause declaration (no census form declares one); N-5's forms (Properties, accessors, defaults, effect bounds) run in step 0.

## 13. Q11: suspended again (2026-11-06, R5): BU's re-check 2 is REVISE (B-4), the third non-ACCEPT

- B-4 (BU, `bu\batch\H11b0\review.md` "Re-check 2"): `Binding.Properties.cs:402` gates the Property's own proof (the formation of
  `property.Type`, the accessor's Result, Input and Receiver, `:408-427`; R5 confirmed on dev 8364a0ce9) yet stays on fact (i); pr1 (a
  computed Property in a struct whose own clause failed) loses pr1n's independent `UnsatisfiedConstraint_Kd`. Required: `:402` reads
  (ii) with that context's `InvalidDeclarationCause` as the prerequisite; `:326` stays (i); pr1, pr1n census rows; commit (0) moves 12
  readers and adds `Binding.Properties.cs`. N-7: delete §4 (1)'s "(the predicate's domain is D2's)". BU's container answer: (ii) general
  is right (f2s, bf1s). BU's consent: YES on conditions.
- Cause one level higher (R5): B-3 and B-4 share one cause. The two-fact representation is confirmed; the readers were classified by
  hand, site by site, and checked only where a reviewer built a probe. The general oracle is SPEC 23.1.2's: an Origin clause fails no
  Type, so an own-clause program reports its no-clause twin's records plus the clause's record at every (ii) reader, and a use keeps
  its dependent at every (i) reader.
- Options asked of the owner: A (recommended, with BU): accept revision 2 with B-4 as a condition and a step-0 gate of one twin pair
  per reader of either fact, checked by BU's same reviewer, no fourth design round; a failed pair stops the unit for the owner. B: a
  fourth design round. C: H11b-0 and H11b to R7 (Problem records remove the one failure slot); R6-check exits with R4-M108's twins and
  the two OriginRecoveryTest pins as named exceptions.

## 14. The owner's answer (2026-11-06, BU's dialog, relayed by F on board 00:32): verbatim "Accept with B-4 fixed"

Revision 2 is accepted on B-4's condition, with no fourth design round; B-4 is applied above (§2a: `Properties.cs:402` reads (ii),
`:326` stays (i); §3: pr1, pr1n; §4 (0): 12 readers, `Binding.Properties.cs`); N-7 applied (§4 (1)). BU's reviewer checks B-4 with
pr1/pr1n (`C:\Users\bwff1\AppData\Local\Temp\claude\bu\batch\H11b0\probe\r2\`) when it reviews commit (0)'s D4 (a) hunk. BU's consent: YES on its conditions (D4 (a) reviewed
as BU's hunk, N-5's forms before commit (3), R4's re-consent). R5 adds to commit (0)'s phase the twin-pair gate of §13 (one
own-clause/no-clause pair per reader of §2a; (ii): the twin's records plus the clause record; (i): the use keeps its dependent): a
verification within the approved design, not a design change; a failed pair stops the unit for the owner.

The owner's answer in R5's dialog (2026-11-06 00:34), verbatim 「Aでお願いします」: option A of §13 as R5 put it, including the twin-pair gate.
