# 2a0-D package: A5-F4, Origin relations and the Binding boundary

The design reviewer consolidated this package on the planning session's instruction (2026-10-21). It writes out the review's
recommended combination (`review.md` §6), which the planning session adopted as written: design A is the base, folded with the
items taken from design B and the review's corrections.

Base: `origin/dev` `ecf62815` (the designs read `92863171`; no Origin file differs). The confirmation (`confirmation.md`) probed
`0eac4690`; dev then reached `6b436f5a` (bu14: 2b-6 (a), BU-18; bu16: 2x-R14), with no change to Origin naming. Its five blocking
fixes, P-1 to P-5, are applied here as worded. No repository file was edited. "(H)" marks a
reading or estimate no run checked. `B/` = `src/Kimi/Compiler/Binding/`, `M/` = `src/Kimi/Compiler/Mir/`, `T/` =
`tests/xUnitTest/Tests/`. `rv/<name>` names a probe in the review's scratch folder (programs run on both pipelines; an atom dumper
over Binding's rows and MIR `Ref` Types).

**Sources:** **A** design A; **B** design B; **R** the review's correction: A-1 (class precedence), A-2 (environment interim), B-1
(guard half), B-2 (rows from positions and declared clauses), BX-1 (partition at formation), BX-2 (precedence stated), N-3 and N-6
(recorded conditions).

**Approval** (once, R4-M74 OQ2) makes this the owner-approved design: DEC Q6 form 1 for the Binding view units 2a0-2, 2a0-3a and
2a0-3b, each with one BU review (PD6, AB-5); form 2 for G:A5-F4 (2a-1a, 2a-1b, 2a-2, 2a-4, 2a-5, one group review); own reviews
(R) for 2a0-4 and 2x-R15.

## 0. Decisions

| # | Rule | SPEC | Source | Unit |
| --- | --- | --- | --- | --- |
| D1 | **Body chain.** A relation is a body chain when an end, resolved at its use, is not fixed (`Binding.BodyChain(longer, shorter)`, today's call test `!FixedOrigin(longer) \|\| !FixedOrigin(shorter)`; an `Unbound` end counts). Calls, non-call fits and declared clauses decide row versus Binding judgment with this one function | 15.6.1, 15.6.5 | A (R: B-2) | 2a0-2, 2a0-3a |
| D2 | **Rows** `(Site, Longer, Shorter, Equality, Source, Clause, Destination, CaseMask)`, ends resolved at their use. `Source ∈ {Fit, ResultFit, Declared, WellFormed, CallBorrows, Contained}`; Binding sets `ResultFit` where it validates a body result | 15.6.1 | A | 2a0-2 |
| D3 | **Partition at formation.** Each row belongs to the executable body Binding binds its Use in (a function, Closure, Property accessor, default template by Binding's default index, or static initializer), recorded when the row is formed, never derived from the Use's own scope or `Use.Parent` | 15.6 | R (BX-1, P-2) | 2a0-2 |
| D4 | **Resolution, not alias rows.** Every row end and every MIR atom goes through Binding's resolution in the body. Meets are keyed by their canonical operand set | 15.3.6, 15.4.4 | A, B | 2a0-2 |
| D5 | **T1 by construction.** No row comes from a Constraint proof; a view test pins it | A3 §7 | A | 2a0-2 |
| D6 | **`OriginAtom.Of`**: SPEC's three kinds `Fixed, Finite, Inferred`, decided by the precedence table of §3 (first match decides) | 15.6.5, 14.8.3, 16.3.1 | B's kinds, R's order (A-1, BX-2) | 2a0-2 |
| D7 | **Slots stay Binding's.** Only the view decodes `Projection` slots. An `ArchitectureRulesTest` row forbids `Mir/` reading `BoundOrigin.Slot` or `.Binder` (G163) | — | A | 2a0-2 |
| D8 | **One owner of fixed–fixed entailment.** The premise catalog and `OriginPremiseClosure`; `UniversalRelations.For(body)` extracts at the body's root | 15.3.6, 15.6.5 | A, B | 2a0-2 |
| D9 | **2a0-3a under OQ11 (b).** Binding forms position rows at its recorded body chains (non-call fits and declared clauses) and labels the twin `Ownership.UnprovenOrigin`. N-3's stop condition applies. The twin's Opaque limit is recorded | 15.6.1, 23.1.2 | A (R: B-2, N-3), B's record | 2a0-3a |
| D10 | **2a0-3b under OQ12 (a), plan letter.** A variable with no candidate is a local region in a body and `MissingOriginBinding_Kd` elsewhere. The "no principal candidate" record becomes the clause's relation judgment. `let` candidates and the contravariant-clause `Unsupported_Kd` stay (OQ-1) | 15.3.6 | B (R: N-1) | 2a0-3b |
| D11 | **Ledger split.** Twins are compared by the existing `Ownership(UnprovenOrigin)` rows. `Binding(OriginRelation)` leaves A5's compared set | — | A, B | 2a0-4 |
| D12 | **A5's Place steps**: one classifier, `M/Analysis/MirSteps.cs`, read by `MirBorrows` and `MirOrigins`. A `Step` carries its prefix | 15.6.2, 15.6.3 | B (R: prefix) | 2x-R15 |
| D13 | **MirOrigins**, a new canonical component. Step 4 per body: `Judge` (all classes), `MirBorrows` per view, then `Record` | 15.6.1, 15.6.3, 15.6.5 | A | 2a-1a |
| D14 | **Containment.** One edge per borrow layer outward from the Borrow, up to and including the first shared layer; a raw Deref stops; an own step is no Deref; a pair layer by its case; a Slice Index contributes the Slice's `source` slot; a PreparedInput Deref is Inline | 15.6.3, 5.2.2 | A, B (R: Slice) | 2a-1a |
| D15 | **Meet nodes (MeetNode).** A non-fixed meet is a node. At a longer end, a meet adds an edge per operand and the node's own edge | 15.3.6 | B | 2a-1a |
| D16 | **Refuted** by finite-source bitsets (2x-P4 D1) | 15.6.5 | A, B | 2a-1a |
| D17 | **Unknown** by Tarjan and fixed-source bitsets with per-target masks from D8 | 15.6.5 | A, B | 2a-1b |
| D18 | **Reason** `(Longer, Shorter, Equality)` on `MirProblem` and in `MirReport`'s key. Facts: `Anchor`, `Relation` (the row index), `Result`. 2a-3 folded into 2a-1a | 23.1.2 | B's representation, A's Equality | 2a-1a |
| D19 | **Edge states.** An `Unbound` end, or another declaration's universal atom (I-1), gives a located `Unsupported_Kd` in a complete compilation. Environment atoms are Finite until 2c-6 | 15.6.5, 15.8.2 | A (Unbound), B (env; R: A-2) | 2a-1a |
| D20 | **Memory.** `MirDataflow.Within(long words)` is the one byte-limit decision, called by `Prepare` and `MirOrigins` | — | B | 2a-1b |
| D21 | **Merge rule 1 (destroy half).** By the `Result` fact and a Refuted chain ending at a `ResultFit` row, per atom | 15.6.5, 23.1.2 | A | 2a-2 |
| D22 | **Merge rule 2.** Conditional GuardReverse; N-6 per OQ-4 | 14.8.3, 23.1.2 | A (R: B-1, N-6) | 2a-2 |
| D23 | **G161 is A5-F4's**, decided by the Refuted rule over dev's existing call-position rows | 16.3.1, 15.6.4 | A, B | 2a-1a |

## 1. Required meaning (SPEC)

- **R1 (15.6.1, 15.6.5).** Every relation a body checks is an edge, whatever its verdict or reachability. Fits yield one relation
  per Origin position by variance. Declared, substituted and well-formedness relations are relations too. A failed relation adds
  no constraint to region inference.
- **R2 (15.6.5).** Each Origin is fixed, finite or inferred, relative to the checked body. In a Closure body, captures, the call
  receiver and an inferred whole result are fixed. Refuted is finite → fixed, and is the only refuting rule. Unknown is a
  fixed–fixed relation that cannot be derived. Each pair case uses only its own premises.
- **R3 (15.6.3).** A child is contained in every borrow it is reached through, up to and including the first shared layer. A raw
  dereference recovers nothing (5.2.2).
- **R4 (15.3.6).** A meet on the right is never split. `(a and b) outlives c` holds exactly when both `a` and `b` outlive `c`.
- **R5 (15.3.6 as integrated, 15.4.4).** A variable with no candidate is a local region in an executable body; elsewhere an
  annotation is needed. Later uses constrain a local's variables.
- **R6 (14.8.3).** A guard-local Origin ends at guard cleanup. An escape is judged by the ordinary checks, and either code is
  admitted.
- **R7 (16.3.1).** Borrows of the destruction receiver are bounded by the drop body.
- **R8 (23.1.2).** Each independent failure is reported. Derived reports are implementation-defined.

**General rule.** Binding publishes every relation it forms in a body as a row, with its ends resolved and classed relative to that
body. A5 judges the graph of those rows plus the containment edges of the Refs. Binding keeps judging only the fixed–fixed
relations it forms directly (A3 §7, until R7). Under OQ11 (b), it also keeps a labelled twin record for each body chain it judges
itself.

S1015 sections implemented, each with MIR tests: §15.3.5 (item 28), §15.3.6 (item 27, S3), §15.6.1 (item 25), 15.6.3, 15.6.5,
16.3.1 (G161) and 14.8.3. §15.6.4 (item 1) is implemented by its class rows; its MIR tests wait for P42 once 2b-6 (b) has landed
(X-2, OQ9).

## 2. Rows: the C1-F4 view (2a0-2)

- **Chain.**
  - *Meaning:* R1.
  - *What is missing:* rows exist only as `Binding.Obligations`. They hold raw ends (39 `Unbound` atoms resolved only at use), form
    one flat list mixed with Length and TypeRole obligations, and have no body. Binding judges non-call body chains itself without
    a row.
  - *Owner:* Binding, which binds the relations and owns resolution.
  - *What becomes unnecessary:* the old reporter's row walk and `LocalRegionSources` reads (R6-check), a per-pass `TyCtxt` reset,
    and alias rows.
- **Interface** (`B/TyCtxt.Origins.cs` holds the records and forwarders; `B/Binding.OriginRows.cs` is the producer):
  - `TyCtxt.OriginRows(definition, defaultIndex)` returns a body's rows and its atom table.
  - `TyCtxt.OriginAtom(definition, defaultIndex, origin, site)` returns the class (§3); an atom no row names is appended to the
    body's table.
  - `TyCtxt.UniversalRelations(definition, defaultIndex)` serves §4.
  - The body key is `(definition, defaultIndex)` as MIR keys bodies. Binding never reads `MirBodyKind`.
- **Membership.** Rows are the `OriginOutlives` obligations with deadline `BodyOrigins`, after Bind's compactions
  (`ConstraintValidation:417-431`, `ImplicitConstructors:216-250`, `Binding.cs:1005-1011`). They include `CallBorrows` rows and
  the 2a0-3a rows. Excluded: Length, TypeFormation, TypeRole, `OriginInference`, and Definition-deadline rows. A row whose ends
  resolve to one atom is dropped.
- **Tuple.**
  - `Source`: `Declared` when `Clause` is set; `WellFormed`; `CallBorrows` (`Discharge`); `ResultFit`; otherwise `Fit`.
    `Contained` is MirOrigins' own, for E4 edges. **Binding sets `ResultFit` where it validates a body result** (a return value
    or tail fit, including `CheckLocalTypeUse` at that fit); the view never infers it from syntax.
  - `Destination`: the expected Type at the position.
  - `CaseMask`: the obligation's `Condition`, renumbered once per row to the body's binder order (`PairBinder.Collect`, which
    `MirBody.Binders` holds). A bit for a binder outside the body is `InternalInvariant_Kd` in the producer's validation.
- **Partition at formation (D3).**
  - Each row is owned by the executable body Binding binds its Use in, recorded when the row is formed:
    - the innermost scope on the forming scope's `Parent` chain whose owner is a function or a Closure (`BindingScope.Function`);
    - or a Property accessor, where `Function` is null (`GetScope`, `Binding.cs:1171`);
    - a default template, identified by the parameter index Binding is binding. Its default is bound with the function's own scope
      (`Binding.Functions.cs:70-84`), so the index comes from Binding's state there;
    - a static initializer, identified by the declaration being bound (its scope's `Function` is null too).
  - The owner is never derived from the Use's own scope or `Use.Parent`. A Closure literal's fit at its declaration
    (`rv/env-store`'s `var f`) belongs to the enclosing body.
  - Binding records the owner when it forms the row: a field of the obligation set at `AddObligation`, or the forming scope passed
    to it together with the default index or the static declaration Binding is binding. The implementer chooses. Every compaction site keeps the owner with its row.
  - Synthesized scopes already carry their owner (`Binding.Sequences.cs:320` copies `Function`; inside an accessor that copy is
    null, so the accessor is found on the `Parent` chain). This covers the parentless `$for.iterator` Use (`BoundIteration.cs:16-18`):
    one row in the library's `Slice.kimi` (so in every compilation) and one per user loop over a borrowed sequence (`rv/forloop`,
    `cf/p/accessor-for`).
  - A row with no owner is `InternalInvariant_Kd` in the producer's validation. No `NoBody` slice exists.
  - Another site shares the cause: `Binding.OriginInheritance.cs:375` walks `Use.Parent`. Listed for BU, not changed here.
- **Resolution (D4).**
  - Every end and every atom MIR reads (a Ref's Type Origin, a dereferenced layer's case Type Origin) is resolved at its use by the
    same `OriginAtUse`. A pending key in a call plan therefore reads as its resolution, and `ResolveInitializerOrigins`' bound
    becomes a self-loop.
  - A meet is keyed by its canonical operand set: operands resolved, flattened, deduplicated, `static` dropped. `Binding.Meet`
    interns by operand identity, but resolution substitutes operands after that.
- **T1 (D5).** `CheckTypeOriginRelations` adds no row. Clause rows come only from `AddTypeClauseObligations` at local annotations,
  where no Constraint proof runs (census (e)). A3 §7's T1 holds by construction.
- **Lifecycle and cost.**
  - Built on a pass's first read, for all bodies in one walk, stamped with the pass version (R4-M87's pattern). No `ResetViews`
    line; `TyCtxt.cs` and `Binding.cs` are untouched.
  - Storage is pooled, so warm runs are 0 B. Nothing runs under `-Pipeline legacy`.
  - Cost O(rows · resolution depth). Retained memory about 40 B per row and 16 B per atom (H).
- **Rejected.** MIR reading `Binding.Obligations` itself (a second resolver); per-definition lazy scans (quadratic); a `NoBody`
  slice (a fallback that hides a missing fact); a body found by walking `Use.Parent` (fails on synthesized Uses).

## 3. `OriginAtom.Of`: kinds, precedence, identity (2a0-2)

The kinds are SPEC's three. No rule reads a finite Origin's bound: Refuted fires for any finite → fixed pair, and a fixed Origin
contains every point of the body. A rule that needs a role reads it where it is published. The guard candidate's role is MIR's
`MirLocalRole.GuardCandidate`, read through the `Anchor` fact's Ref. `OriginAtomKind` is not a closed vocabulary (no O1).

**Precedence, for an atom resolved at its use in body B. The first match decides.** "Of B", "in B" and "enclosing B" are decided
by body ownership: a binder's executable body, as D3 assigns rows. Syntactic ancestry does not decide them. An atom that matches no
row is `InternalInvariant_Kd` in the view's validation, never a silent default. An atom of another declaration, including a nested
body's root seen from B, is row 9's: before 2c-6, such an atom marks a valid form not yet covered.

| # | Atom | Class | SPEC |
| --- | --- | --- | --- |
| 1 | `Static`; a `Parameter` or `Input` of B's contract: B's or an enclosing declaration's signature Origin or Type slot, a pair binder's `o` | Fixed | 15.6.5 |
| 2 | `Intersection`, by canonical key: every operand Fixed (by this table) | one Fixed node | 15.3.6 |
| 2′ | `Intersection`, any other | a meet node, Inferred, with the edges of §6.2 | 15.3.6 |
| 3 | `Projection` whose binder is a root of B, Finite: a local, parameter or temporary of B (a fall-through binder whose node names no static storage, SPEC 15.6.2); a guard candidate (`CandidateOriginBinder`); the destruction receiver's part (`Projection[drop#0]`) or its helper borrow (`Projection[drop#HelperSlot]`); in a default template, the function's parameters (prepared inputs, E17). Inferred instead when its binder reads a referent (a Borrow through a reference) | Finite (or Inferred) | 15.6.5, 14.8.3, 16.3.1 |
| 4 | `Projection` of B's own Closure environment binding (environment slot) | Finite, until 2c-6 classes it per receiver case | 15.6.5, 15.8.2 |
| 5 | `Projection`, role `ClosureValue`: Fixed when its binder is B (B's own call receiver); Finite when its binder is a Closure literal inside B (a temporary). Role `CallResult` of B: Fixed. A binder outside B falls to row 6 | as stated | 15.8.2 |
| 6 | Any other atom whose binder lies in a body enclosing B: a capture's Origin, an enclosing body's region or Borrow, an imported static Borrow | Fixed | 15.6.5 (Closure bodies) |
| 7 | `Projection` of mutable static storage formed in B (its binder is the Borrow's site in B) | Finite | 15.6.4 |
| 7′ | `Projection` of immutable static storage formed in B | Inferred | 11.8 |
| 8 | `Inference` of B (local regions, call regions, pending variables), `Anchor` | Inferred | 15.6.5, 5.2.2 |
| 9 | `Unbound` after resolution, also as a meet operand (the row gets the treatment; no operand edge is silently dropped). A `Parameter`, `Input` or `Projection` of any other declaration (a callee's Origin Binding did not instantiate, I-1; a nested body's root seen from B) gets the same treatment | no class: no edge. A located `Unsupported_Kd` at the row in a complete compilation (registered at X-2); nothing in an incomplete one (R4-M10) | 15.6.4 step 1 |

Why the order matters:

- **Row 3 before row 6.** A default template's prepared inputs have binders outside the default expression. Under row 6 first,
  `rv/dflt`'s `n@ref` would be judged fixed–fixed (P), while dev's twin is Refuted.
- **Rows 1 and 6 restricted, row 9 widened (P-5).** In `rv/meet-trans-ok` (`cf/p/mto`), the call's regions are `put`'s own
  `Parameter` atoms, and they appear in `mt`'s rows: `Projection[y] >= (put.a & put.b)` and `Inference[var t] == (put.a &
  put.b)`. SPEC 15.6.4 step 1 makes them fresh regions, and 15.6.5 makes fixed only the body's contract. An unrestricted row 1
  would make A5 report S and P on that SPEC-valid program. Row 9 gives the located `Unsupported_Kd` until BU's fix (§8).
- **Row 6 before row 7.** On dev, an unqualified group-storage Borrow's atom is per site (`PlaceOriginBinder`'s fall-through). A
  closure's captured static Borrow (`rv/st-both` `#81`) is therefore distinct from its own `count@ref` (`#82`). Under row 7 first,
  a SPEC-valid program (`rv/st-capture2`) would get a Language error once 2c-6 adds result rows.
- **The view's validation.** A mutable-static `Projection` whose binder is a static Field declaration is `InternalInvariant_Kd`,
  so a binder change (P42's static-Borrow unit, §8) cannot silently flip rows 6 and 7.
- **Row 4 is an interim (A-2).** `rv/env-store` is rejected on the default pipeline through its call-position row
  `Projection[closure#env0] >= t`. Inferred there would leave the MIR accepting it. Finite reproduces Binding's present judgment,
  including its code where SPEC 15.6.5 gives Unknown for a borrowed receiver. That code difference is 2c-6's.

**Static Borrows and 2b-6 (P-3).** After 2b-6 (b) (R4-M90 PQ-1; 2b-D §1.5), every explicit static Borrow, qualified or the
unqualified in-group form, is Binding's located `Unsupported_Kd` until P42. Rows 7 and 7' then class only implicit static Borrows
that reach a row: a `for` or `match` Subject borrow, a comparison inspection, a writeback reservation, or an argument adaptation.
The explicit forms, and the row-6/row-7 argument above, are dormant until P42.

**Identity between MIR Types and rows.**

- A Ref's own Origin is its `MirRvalue.Ref` Type's outer Origin, resolved at the Ref statement's `SourceInfo.Syntax`.
- A dereferenced layer's Origin is the case Type (`MirCases.Type`) of the Place prefix ending at that Deref.
- A Ref whose Type has no Origin forms no node. These are the builder's own Refs: writeback, a synthesized input borrow, a
  selection's raw pointer.
- dev's dumper confirms that MIR `Ref` Types carry Binding's node-Type atoms (`rv/probe/p1`, `rv/ident-call`).
- A Ref whose Place reaches no Deref is a Borrow anchored in its root (SPEC 15.6.5). For a Borrow anchored in a local, parameter
  or temporary root, its Origin must be the root's finite atom (an immutable static root or a raw anchor has none). dev's `PlaceOrigin` names such a Borrow of a `Slice` handle by the handle's `source`
  (`ElementAccess.AccessType(source).Origin`, `Binding.ArgumentOperations.cs:430-432`). A5 therefore inherits `leak3`'s
  acceptance until Binding's repair (P-4, §9 item 6).

  If the planning session prefers A5 to catch this itself, by an anchor edge from the root's atom to the Ref's Origin ("layer 0"
  of E4), that is a design change, and the full process applies.
- Test (2a-1a): one program per builder Ref site (B's list: `MirBuilder.Values.cs:115`, `.Acquisitions.cs:58, 82, 95, 132`,
  `.Match.cs:86`, `.Selections.cs:183`, `.Expansions.cs:247`), plus a Slice element Borrow. Each Ref with an Origin maps to a
  node that a row or a containment edge names, or forms no chain.

**G161 and G163.**

- dev's G161 programs (`probe/p2`) bind completely. The old reporter rejects them from two call-position rows:
  `Projection[drop#0] >= Holder.source` at `self.buffer@ref`, and `Projection[drop#HelperSlot] >= Holder2.source` at
  `whole.buffer@ref`. Both atoms are Finite (row 3), so 2a-1a's Refuted rule decides G161 (`UnsatisfiedOriginRelation_Kd` at the
  argument).
- The constructor half stays G166's (BU).
- G163's asks are 2a0-2's view tests and D7's `ArchitectureRulesTest` row.

## 4. Fixed–fixed entailment: one owner (2a0-2)

- **Meaning.** Unknown needs `entails(f, g)` for fixed `f` and `g`, from the definition's premises, per case.
- **Owner.** The premise catalog (`ExtractOriginPremises`, `B/Binding.OriginPremises.cs`) and `OriginPremiseClosure`, which
  already form one implementation.
- **Design.**
  - `UniversalRelations.For(body)` rents one premise environment at the body's root (R1–R4 of the body and its enclosing
    declarations), keeps conditioned edges, and offers `Close(target, caseBits)` and then `Outlives(source)`. The structural
    instances I1–I4 answer first, as in `EntailsOrigin`.
  - `OriginPremiseClosure` gains the reached-node test (about 10 lines).
  - `ProvesOriginOutlives` keeps its per-request entry during Bind.
  - Requests change no inference or obligation state, which the `originStateVersion` check guards.
- **Why one form.** The walk from a use and the walk from the body's root reach the same declaration and signature nodes. R5
  adds only `stored → Projection[local]` edges, for nodes already in the graph; a fixed–fixed query from the root puts none
  there. The one start-dependent case is a use inside a `FunctionTypeKoto`, and no body row starts there (H; well-formedness
  inside a nested Function Type stays a definition record).
- **Checks.**
  - An agreement test over the census corpus's fixed–fixed rows: `UniversalRelations` equals `ProvesOriginOutlives`. It stays until
    R7 moves Binding's fixed–fixed judgments.
  - A permanent `ArchitectureRulesTest` row: only `Binding.OriginPremises.cs` calls `OriginPremiseClosure.AddEdge`.
- **G170.** Not needed. The oracle reads no Constraint premise supply, so a premise G170 hides changes both pipelines together;
  no `Unsupported_Kd` is required.
- **Rejected.** A mode switch in `ProvesOriginOutlives` by phase (a symptom); a MIR-side `Entails` over copied premises (two
  forms).

## 5. The boundary

### 5.1 2a0-3a (OQ11 (b), D9)

- **Meaning.** A5 judges body chains. Binding keeps its records of them until H11, labelled `Ownership.UnprovenOrigin` with code
  and span unchanged, and the ledger compares.
- **Change.**
  1. `BodyChain` lands in 2a0-2 (`Binding.OriginProof.cs`). `JudgeCallPosition` uses it, behavior-preserving.
  2. `RecordOriginFitFailure` (`B/Binding.OriginFits.cs`): when a failing chain of the fit is a body chain, it calls
     `CheckTypeUse(actual, expected, node)` once. That is the existing producer of a fit's position rows; every position of the
     fit becomes a row, fixed–fixed ones included. Every `OriginRelationFact` recorded for the node is marked `Twin`.
  3. For a local's clause, `JudgeLocalClause` (the local caller) adds the clause's row when `BodyChain` holds. This replaces
     today's test `HasLocalRegion || !FixedOrigin(shorter)` with `BodyChain`, which adds the finite → fixed case. It keeps today's
     row-only return for a local region or an inferred shorter end, and marks as a twin a finite → fixed record that
     `FailedDeclaredRelation` still makes. `FailedDeclaredRelation` is shared with `JudgeDeclaredRelation` (Fields, Cases, aliases,
     associated formations) and takes no steering flag, and those relations stay Binding's. A declaration-level row is
     `InternalInvariant_Kd` in the view's validation in a complete compilation (R4-M10).
  4. The report site in `Binding.Diagnostics.cs` names `Ownership.UnprovenOrigin` for a twin and `Binding.OriginRelation`
     otherwise: one expression, net 0 tokens in the over-limit file.
- **Why the old reporter never judges these rows.** Any record fails its node, so Binding is incomplete and the old analysis does
  not run for that compilation (`OwnershipAnalysis.cs:89`). The MIR runs regardless (`MirAnalysis.Run`), so A5 judges the rows.
- **N-3's stop condition.** `AddObligation` feeds `RecordLocalRegionBound` for Projection, meet, Anchor and region targets. A
  twin's rows could therefore reach Binding's local-region graph (SPEC 15.6.1: a failed relation adds no constraint). The done
  condition requires the default pipeline's records to be byte-equal on corpus, fixtures and SPEC variants, except the requirement
  label. If a new or changed record appears, the implementer stops and reports. The planning session then moves twin rows into
  a view-owned input list.
- **Recorded limit (STATUS and H11's list).** A twin's node is `HirOp.Error`, an Opaque in the MIR (`rv/ident-call`).
  - Its site names the roots it reads, so values it reads keep their Loans. On the MIR, `let r = x@ref; x = 1; return r` reports
    the relation and the write (PD11). The default pipeline reports only the relation, since the old analysis does not run.
  - Borrows formed inside the failed node are not built, so their Loans are absent on both pipelines until H11.
    `slot@follow = x@ref; x = 2` reports only the relation, while SPEC 23.1.2 also wants the write conflict (`cf/p/m-write4`).
- **H11 end state (AR-4).** When the twins go, the non-call fit producer adds body-chain position rows independent of Binding's
  verdict, as `JudgeCallPosition` does. Until then, rows at non-call fits exist where Binding's judgment fails. In my reading, a
  proven fit's ends are fixed, finite or anchor atoms, whose missing edge loses at most a second report at the same edge.
- **Listed for H11.** The twins, the `Twin` field, the label branch, and `B/Binding.OriginFits.cs:85`'s
  `Fail(node, Unsupported)` gate.

### 5.2 2a0-3b (OQ12 (a), D10)

- **Meaning.** R5. A "no principal candidate" outcome is the clause's relation judgment, not an error category. The plan's
  letter is PD8 and A3 §7's unowned items.
- **Change.**
  1. An inference variable of an executable body that reaches no candidate resolves to an open local region at `BodyOrigins`,
     which `IsVerifiedOriginObligation` then verifies.
  2. `JudgeLocalClause`'s principal-candidate branch records no `MissingOriginBinding_Kd` in a body. The clause is a row, and
     Binding's own failing judgment is its twin.
  3. Outside a body (Field, payload, alias, signature), an unresolved slot stays `MissingOriginBinding_Kd`.
- **Unchanged here.** `let` candidates (`SolveLocalBounds`, `MeetLocalUpperBounds`) and the contravariant-clause `Unsupported_Kd`.
  The latter is a valid form's located limit; OQ-1 decides it.
- **Record correction.** The S3 demo is a located `Unsupported_Kd` on dev at `let pair` (`rv/s-demo`), not accepted as 2x-P4's
  finding 6 assumed (H). A note goes into A3/origins via 2a-5's PLAN row.
- **Deletes.** `principalOriginFailures`; its report branch in `Binding.cs` (deletion only); the old reporter's unresolved-inference stop for body
  variables.
- **Default pipeline (OQ7 (a); clause 15.3.6, named in the commit).**
  - c8c1f3f534b0 and s-vars are accepted.
  - `IncomparableContravariantBoundsRequireAnAnnotation`'s program becomes valid, or becomes the contravariant-clause located
    `Unsupported_Kd` if that limit covers it (H). Either way it is re-expected by the same unit, which states the reason.
  - s-noprincipal-err gives the relation record.
- **Census first.** Library and milestone programs whose verdict changes; an O3 consultation if any.

### 5.3 2a0-4: the ledger split (D11; `MirDifferenceLedger.cs` is R5's after R5-Q17, AR-3, hold adv)

- Twins arrive as `Ownership.UnprovenOrigin` records, which the existing `Same(Ownership(UnprovenOrigin), …, "A5")` rows compare.
- The two `(Binding(OriginRelation), …, "A5", …)` rows of `MirDifferenceLedger.Compared` go. Binding's remaining records are
  fixed–fixed, Binding's until R7, and pass through.
- `Same(Ownership(UnprovenOrigin), UnprovenConstraint_Kd)` goes when its producer goes (2a0-3b's item 1).
- `classification.tsv`: a5-old-origins-binding (30 signatures, 422 rows) is re-pointed (twins paired, fixed–fixed rows pass
  through), with an R4-M4/R4-M17 note in DEC.
- Test: `MirDifferenceTest`. A twin is compared; a fixed–fixed Binding record is not.
- Order: 2a0-3a → 2a0-3b → 2a0-4 (the plan's). If BU's hold delays 2a0-3b, the planning session may land 2a0-4 after 2a0-3a. The
  `UnprovenConstraint` row then moves with 2a0-3b, under the same adv hold.

## 6. MirOrigins (2x-R15, 2a-1a, 2a-1b)

### 6.1 Contract and order (D13)

`M/Analysis/MirOrigins.cs` holds the contract, per-body frame, nodes, classes and containment. `MirOrigins.Chains.cs` holds the
components, Refuted, Unknown and reports.

- **Responsibility.** A5's Origin stratum (SPEC 15.6.1, 15.6.3, 15.6.5), per body and per class of its views.
- **Inputs.** The body, `MirCases`, `MirSteps`, and the C1-F4 view.
- **Outputs.** Origin problems (`UnsatisfiedOriginRelation_Kd`, Language; `UnprovenOriginRelation_Kd`, Proof; requirement
  `Ownership.UnprovenOrigin`) with Reason and facts; for the merge, the atoms refuted at a `ResultFit` row, with views;
  `OwnershipStorageLimit_Kd` once at a body over the limit.
- **Invariants.** Reads no liveness, CFG order, Loan set or syntax. Every atom comes through the view. A chain's verdict is
  independent of reachability.
- **Must not.** Judge a relation Binding records as fixed–fixed; decode a slot; port the preserves-results skip or the top-like
  Anchor (PD9); drop a problem (that is `MirReport.Add`'s).
- **Order (step 4, per body; `drv` hold):** `origins.Judge(program, body)` over all classes, then `borrows.Run` per view, then
  `origins.Record(program.Problems)`. Rule 2 needs MirBorrows' err(ended) before an Origin problem is added, and no problem is removed after it is added.
- **Inline copies.** A Ref inside an `Inlined` scope adds no containment. Rows are the template's, judged once in its
  `DefaultTemplate` body (R4-M69, R4-M90 D11).

### 6.2 Classes, nodes and edges (D14–D16)

- **Classes.** Per view, compute the active rows (`CaseMask ⊆` the view's borrow binders) and the containment edges (pair layers by
  case). Views with equal sets form one class, judged once, whose problems carry its views. The oracle's condition is the class's
  borrow binders (SPEC 15.6.5: premises that hold in every case that requires the relation). Prototype `k-views`: 64 views, 2
  classes.
- **Rows.** `Longer → Shorter`, and back for `Equality`.
  - A meet at the longer end adds an edge per operand. For a non-fixed meet it also adds the meet node's own edge (D15; prototype
    `meet-trans`: UNSOUND under the split alone, ok with MeetNode). No dev program reaches it until I-1's repair; 2a-1a pins it
    with a view fixture of fresh-region rows.
  - A non-fixed meet at the shorter end is its node, with an edge from each operand.
  - A Fixed meet is one node.
- **Containment (D14).** For a Ref of Place X whose own Origin is `n`, the steps of X come from `MirSteps`, last step first.
  - A `Uniq` Deref of a layer with Origin `o` adds `o → n` and continues.
  - A `Shared` Deref (a `ref`/`objref` layer; a Slice handle's Index, E6, whose layer Origin is the Slice's `source` slot) adds its
    edge and stops.
  - An `Own` step or an inline step continues without an edge.
  - A `Raw` Deref stops (2x-P4 D3).
  - A pair layer counts as the step its case gives, none in the owner case.
  - A PreparedInput Deref is Inline (E17).
  - The edge's site is the Ref's statement, with Source `Contained`. Where Binding's name already is the meet of the layers, the
    edge is proven by the meet law. Where Binding dropped a layer, the edge is judged: `rv/probe/p1`'s
    `var q = p; return q@follow@follow@uniq`, which dev accepts, gives SPEC's Unknown through `q#-1 → q#-2`.
- **What is judged.** Every row, fixed–fixed rows included (`Unbound`-resolved ends, `CallBorrows`, call positions), every
  containment edge, and every chain through inner nodes.

### 6.3 Refuted, Unknown, reports (D16–D18)

- **Refuted (2a-1a).**
  - Tarjan runs once per class over the inner nodes (Inferred and Finite).
  - Finite-source bitsets (64 per word) propagate in topological order.
  - At each edge `u → g` with `g` Fixed, every finite bit at `u` (and `u` itself when finite) gives `UnsatisfiedOriginRelation_Kd`
    at the edge's site with Reason `(source, g, false)`.
  - A report caused only by an earlier failed edge is admitted as derived (R8).
- **Unknown (2a-1b).**
  - Fixed-source bitsets over the same components. The sources are Fixed nodes with an edge into an inner node; a Fixed meet's
    sources are its operands.
  - For each Fixed target `g` with an incoming edge, `UniversalRelations.Close(g, class)` runs once and gives a mask over the
    sources.
  - Each edge reports `bits(u) & ~mask(g)` as `UnprovenOriginRelation_Kd` with Reason `(f, g, false)`.
  - A direct Fixed → Fixed edge is one `Outlives` test.
  - A failed `Equality` row in both directions is one problem with `Equality` set.
- **Reason and facts (D18).**
  - `MirProblem` gains `Reason(BoundOrigin Longer, BoundOrigin Shorter, bool Equality)`, default for other codes. `MirReport`'s key
    becomes `(code, subject, body, Reason)` (R4-M11, R4-M71). j-two gives two problems at one subject; census (c) finds 6 nodes
    with several chains. The Reason's ends are the view's canonical atoms (the atom-table representatives), so two Type objects of
    one canonical meet give one key.
  - `MirFact` gains an `int` used only by the new kind `Relation`: the row's index in the body's view, or −1 with the Ref site for
    a containment edge. The adapter reads Source, Clause and Destination from the view at publication (H2).
  - `Anchor` (existing kind): the first Ref in body order whose canonical destination is the finite source.
  - `Result`: §7.
  - DIAGNOSTICS gains the source value `contained`. No closed vocabulary grows.
- **Subject.** The last edge's site: the row's Use, or the Ref's statement for a containment edge.

### 6.4 Cost, memory, V16 (D20)

- **Work** per body and class, times k classes:
  `Θ(N_o + E_o + (N_c + E_o)·⌈Fin/64⌉ + (N_c + E_o)·⌈F/64⌉ + Ft·(Pm + ⌈F/64⌉))`, where:
  - N_c is the number of inner components;
  - Fin and F are the finite and fixed sources;
  - Ft is the Fixed targets with an incoming edge;
  - Pm is the premise graph.

  This is A3 §7's term, with ρ removed by the topological propagation and D1's `Fin` term added. Every non-linear term is a /64
  Dense term. `MirOrigins.Work` counts node visits, edge steps, bitset words, mask words and the closure's steps.
- **Memory.** One pooled arena of `N_c·max(⌈Fin/64⌉, ⌈F/64⌉) + Ft·⌈F/64⌉` words, admitted by `MirDataflow.Within(long words)`.
  That one decision of R4-M15a's 64 MiB limit is also called by `Prepare`. Over the limit: `OwnershipStorageLimit_Kd` once at the
  body, and the stratum skips it. Warm runs are 0 B.
- **V16 rows** (Work checked at 2× and 4×):

| Shape | Unit | Bound |
| --- | --- | --- |
| "F4 Refuted": N finite Borrows into one chain into one Fixed Origin | 2a-1a | ≤ c·(N_o + E_o·⌈Fin/64⌉) |
| "F4 containment": a Reborrow through N exclusive layers, then N shared | 2a-1a | containment edges ≤ the layers up to the first shared one |
| A3 (9): N chained locals, N signature Origins, k mask classes | 2a-1b | ≤ c·k·(N_o + E_o·⌈F/64⌉ + Ft·(Pm + ⌈F/64⌉)) |
| a 64-view pair body | 2a-1b | class count 2; Work within c of the 1-view body's |

Prototype scaling (2x-P4, unchanged in both re-runs): Work/(N_o + E_o·⌈F/64⌉ + E_o·⌈Fin/64⌉) is 0.90–1.99 for N = 64 to 512.

## 7. The merge (2a-2; D21, D22)

1. **Rule 1 (destroy half, PD11).**
   - MirBorrows adds the `Result` fact to a storage-end destruction conflict whose Loan is found only through `_0`, with no other
     live holder or transit value taking it.
   - Such a problem loses each view in which its Anchor's atom is the finite source of a chain MirOrigins refuted at a `ResultFit`
     row. It is dropped when no view remains.
   - The rule works by atom: two Borrows of one local root share `Projection[x]` (prototype `m-twice`, `same-root`), and static
     Borrows are per site.
   - Confirmed by 2x-P4: dropping by Borrow alone loses m-write and m-scope-use, and dropping by Loan membership accepts 36f.
2. **Rule 2 (guard half: conditional GuardReverse).**
   - Every err(ended) stays.
   - An `UnsatisfiedOriginRelation_Kd` whose Anchor Ref's destination is a `GuardCandidate` local loses the views in which
     MirBorrows recorded an err(ended) for that candidate.
   - It supersedes PD11's second half and A3 E7's merge sentence (2x-P4 D2).
   - An unconditional drop is rejected. `rv/guard-annot` (`var t: ref/i32 during a`, guard `keep(t@uniq, v)`) is rejected on dev
     and has no err(ended). The unconditional drop would accept it on the MIR (reviewer prototype: UNSOUND on `g-annot`).
3. Nothing else is dropped, never by Loan membership. A5-F9's PreparedInput rule stays 2g-5's.

**Mechanism.** `MirReport` gains a per-body merge context: `RefutedAtResult` (atom → views), written by `Judge`, and `Ended`
(candidate → views), written by MirBorrows' err(ended) recorder. `Add` applies rules 1 and 2 to arriving problems.

**N-6 (OQ-4).** A candidate that escapes (err(ended)) and also has an independent non-escaping Refuted relation loses that
report under rule 2 as written (`rv-g-annot-esc`: LOST; ok under PD11, which instead loses g-two). The program stays rejected,
but SPEC 23.1.2 wants the independent failure reported.
- Under OQ-4 (a), N-6 is a recorded limit: a pinned test, a ledger class, the X-2/OQ9 list, DIAGNOSTICS' A6 merge row and STATUS.
- Under OQ-4 (b), rule 2 adds "and its last edge is a `ResultFit` row". `rv-g-annot-esc`'s report stays; `cf-g-reassign`'s
  (g-two with `saved = fb2` before `return saved`) is still lost (pinned). A guard escape into
  storage the body does not own reports both codes (`rv-g-param`, a derived report; prototype `rv/protoA/runA4.txt`).

**S3's finding.** `g`'s storage-end `ComparisonLoanConflict_Kd` at `let m = 8` is rule 1's input once 2c-6 publishes the closure
result relation. Until then it is a location/code row owned by 2c-6 (prototype `s3-g-meet`).

## 8. Asks

| To | Ask |
| --- | --- |
| BU (AB-5, PD6) | Reviewer of 2a0-2, 2a0-3a and 2a0-3b. BU's hold covers 2a0-2's edits of `Binding.OriginProof.cs` (`BodyChain`), `.CallRelations.cs` (`JudgeCallPosition`) and `OriginPremiseClosure.cs`; its new files (`TyCtxt.Origins.cs`, `Binding.OriginRows.cs`) need none. It also covers `Binding.OriginFits.cs`, `.OriginProof.cs`, `.OriginDeclarations.cs` and `Binding.cs` for 2a0-3a/3b. `Binding.LocalRegions.cs` is at PD21 count 3 on dev, so any 2a0-3b edit there needs its response first |
| BU (2b-6 (b); P42's static-Borrow unit) | 2b-6's qualified part has landed (bu14, BU-18). 2b-6 (b) re-expects A5-F4's static tests as its U. When static Borrows bind again (P42), the binder stays per Borrow site, never the Field declaration (§3's validation) |
| BU (new PLAN row, §9) | `rv/meet-trans-ok`: a callee parameter Type with a meet of its own Origins (`slot: uniq/(ref/i32 during (a and b))`) is judged at the call with the callee's `Parameter` atoms, not fresh regions. A SPEC-valid program is rejected with P plus two S on the default pipeline. Its rows name `put`'s atoms, so A5 gives the located `Unsupported_Kd` (row 9) until BU's fix |
| BU (new PLAN row, §9 item 6; PD9) | A repair unit, before R6-check, that names a Borrow of a `Slice` handle root by the root's `Projection`, not the handle's `source` (`PlaceOrigin`, `Binding.ArgumentOperations.cs:430-432`), with OQ7 (a)'s census (`cf/p/handle2` `leak3`, `leak4`, the `var … @uniq` form) |
| BU (OQ-1 (c)) | The D4 unit: a `let` local's omitted slots as regions, with its own full-process design and a census of `let` borrow locals in the library and milestones, after G:A5-F4 |
| R5 | 2a0-4 edits `MirDifferenceLedger.cs` after R5-Q17 (AR-3, hold adv). H2/H3: the Origin problems' Reason, `Relation` index (read from the view at publication), `Result` fact and the `contained` source are their facts (PD31) |
| R5 (H11, AR-4 additions) | The twins, `Twin`, the label branch and `OriginFits.cs:85`'s gate; the old reporter (`A/OwnershipAnalysis.Borrows.cs:55-161`), its Unrepresentable gate and `CallBorrows` skip; the preserves skip (OQ-2 (a)); non-call fits' rows independent of Binding's verdict (§5.1) |
| 2c-D | Environment classes per receiver case, replacing row 4. The closure result formal as a Fixed atom with its contract binding as a premise (`s3-f`, `s3-g`); `g` with and without `n` captured as 2c-6 tests. st-capture2's verdict once the result row exists, after P42 (row 6: Fixed, valid) |

## 9. Records

**DECISIONS.** The approval entry (written at approval), "R4-M9x A5-F4's design package (2a0-D) approved", names the package,
its review and the owner's words; DEC Q6 form 1 for 2a0-2, 2a0-3a and 2a0-3b, form 2 for G:A5-F4 (2a-1a, 2a-1b, 2a-2, 2a-4,
2a-5), and own reviews for 2a0-4 and 2x-R15; the owner's answers to OQ-1 to OQ-4, with OQ-3's departures as confirmed text; and
a short summary of D1–D23.

Unit entries, each written by its first implementing unit:

| Unit | Entry |
| --- | --- |
| 2a0-2 | ER-rows and the C1-F4 view: tuple, Source with `ResultFit`, partition at formation, resolution and canonical meets, T1 by construction. `OriginAtom.Of`: three kinds and the precedence table. One entailment owner, with the agreement test (removal at R7) and the `AddEdge` rule |
| 2a0-3a | The boundary predicate, twins and the H11 list (superseding R4-M9b's view list where it applies); N-3's condition; the Opaque limit |
| 2a0-3b | No-candidate variables as body regions; the principal-candidate record as a relation judgment; OQ-1's answer |
| 2a0-4 | The ledger split, as an R4-M4/R4-M17 note |
| 2x-R15 | A5's Place steps as a canonical component |
| 2a-1a | MirOrigins as a canonical component; the step-4 order; D1 bitsets; MeetNode; Reason in the key (PD10 met, 2a-3 folded); E4 over Binding's encoding with the raw stop and the Slice layer; the environment interim with removal unit 2c-6; `Unbound`'s located `Unsupported_Kd` |
| 2a-1b | The Unknown oracle; `MirDataflow.Within` |
| 2a-2 | The merge contract (rules 1–3): PD11's first half read per atom with the `Result` fact and a `ResultFit` row; PD11's second half and A3 E7's merge sentence superseded; N-6 per OQ-4 |

**DIAGNOSTICS.** The Origin-relations paragraph gets the twins and A5's owners, the `contained` source (SPEC 15.6.3), and the S3
wording for "no principal candidate". The A6 table gets the two merge rows and N-6. The contravariant-clause sentence stays until OQ-1's unit.

**ARCH.** Canonical rows for the C1-F4 view ("Binding facts for MIR"), `MirOrigins` and `MirSteps`; the driver's step-4 sentence.

**CODEMAP.** The new files.

**STATUS.** The Origin boundary row: Binding judges fixed–fixed directly until R7; A5 judges every row in shadow; the twin's
Opaque limit (a Borrow formed inside the failed node is not built, so `slot@follow = x@ref; x = 2` reports only the relation
until H11; values the node reads keep their Loans); N-6 per OQ-4; the skip per OQ-2; p1's acceptance; the Slice handle
acceptance (item 6).

**PLAN** (new G numbers from dev's next free; written by 2a-5 unless noted):

1. **p1.** Binding's `PathReborrowOrigin` names a Reborrow through a local's layers by the inner layer only. dev therefore accepts
   `func f(p: uniq/(uniq/i32 during a) during b) -> uniq/i32 during a` with `var q = p; return q@follow@follow@uniq`, while SPEC
   15.6.3/15.6.5 make it Unknown (`b outlives a`); its direct twin is rejected. It is not shown to be a miscompile: 15.6.4's
   per-argument retention keeps the caller's Loan. A5 rejects it from 2a-1a. Closes at R6-check. Written by 2a-1a.
2. **The preserves skip (OQ-2 (a)).** Binding's preserves-results containment skip (`B/Binding.ArgumentOperations.cs:563-668`)
   accepts 18 tested forms SPEC 15.6.3 makes Unknown (library and milestones 0, census (a)). A5 rejects them. 2a-4 re-expects the
   18 under OldRule now. The skip is deleted with the old path at R6-check (AR-4).
3. **D4 (OQ-1 (c)).** Binding gives a `let` local's omitted slot a declaration-time candidate. SPEC 15.6.5/15.4.4 make it an
   inferred region constrained by later uses (2x-P4 D4). No dev program was found that this rejects with a Language error although
   valid (H); the S3 demo is a located `Unsupported_Kd`. BU's unit, with its own full process and census, after G:A5-F4.
4. **The meet defect.** `rv/meet-trans-ok`'s Binding defect (§8; BU). Written at approval.
5. **G56.** The sentence "a Binding-level relation stops ownership analysis" is unchanged under OQ11 (b) until H11, with the
   twin's Opaque limit added. G161 is closed by 2a-1a, and its constructor half stays G166's.
6. **Slice handle Borrows (P-4; owner BU; written at approval).** `PlaceOrigin` names `values@ref` of a `Slice` local or
   parameter by the handle's `source`.
   - `leak3` is accepted on both pipelines and by A5: `func leak3(values: Slice<i32> during s) -> ref/(Slice<i32> during s)
     during s` with body `return values@ref`.
   - `leak4` and the `var … @uniq` form are accepted on the default pipeline, while the MIR's storage-end conflict rejects them.

   This is a miscompile under PD9 (SPEC 4.8.1, 15.6.5). The repair is a BU unit that names the Borrow by the root's `Projection`,
   with OQ7 (a)'s census, before R6-check.
7. **`limitRef` (P-3; owner BU, P42; written at approval).** dev rejects SPEC 11.8's valid in-group `limitRef` (`return
   limit@ref`, a `let` static, `during static`) with S (Binding's record, `cf/p/st-imm`). It becomes 2b-6 (b)'s U, and P42 must
   give the valid verdict.

## 10. Units

Lane O: 2a0-2 → 2a0-3a → 2a0-3b → 2a0-4 (§5.3's permitted reorder). Lane C (PD1, after G:A5-F9's merge): 2x-R5 → 2x-R15 →
[`MirReport`/`MirProblem` responses if due] → 2a-1a → 2a-1b → [a PD21 response for `MirBorrows.Reports.cs` if due] → 2a-2. Then
2a-4 → 2a-5.
G:A5-F4 merges into dev after its group review.

Rules common to every code unit:
- +cen.
- MIR tests go in new partial files `T/MirOriginsTest.cs` and `T/MirOriginsTest.Merge.cs`, at the MIR-problem layer. Each new
  rejecting test fails before its unit (dev has no stratum). Valid counterparts and pins pass before: `limitRef`, a-split, a-wf,
  d-g161-ok, CallBorrows Proven, the S3 demo, `leak3`, N-6.
- View tests go in `T/OriginRowsTest.cs`.

| ID | Unit | Change | Prerequisites | Verification (tests fail before) | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2a0-2 | C1-F4 view | §2–§4: `TyCtxt.Origins.cs`, `Binding.OriginRows.cs` (rows, partition at formation, atom table, `ProjectionRole`, precedence and validation, `UniversalRelations`); `OriginPremiseClosure`'s reached test; `BodyChain` used by `JudgeCallPosition` (bp); `PublishedBindingTypes` row; two `ArchitectureRulesTest` rows; DEC | 2a0-D approved (OQ2); OQ6 (a); 2a0-1 landed | Comp: `OriginRowsTest`: each obligation once in its body; resolved `Unbound` ends; CaseMask for nested pair binders; one class per §3 row, including `rv/st-both`'s two atoms, `rv/dflt`'s prepared input, `Projection[drop#0]`, `Projection[drop#HelperSlot]`, a guard candidate, an environment atom, mutable and immutable statics (if 2b-6 (b) has landed at the unit's start, through an implicit static Borrow; otherwise as written, and 2b-6 (b) re-expects them as its U), an anchor; the `for` rows of `Slice.kimi` and `rv/forloop` in their functions; the rows of a getter (`cf/p/accessor2`) and of a `for` inside it (`cf/p/accessor-for`) in the accessor's slice; a row whose Use is a Closure literal (`rv/env-store`'s `var f`) in the enclosing body's slice; no owner-less row; +cen counts rows naming another declaration's universal atom, and if any shape other than I-1's appears the unit stops and reports; an initializer alias resolves to the local's atom; T1 fixture; the agreement test over the census's fixed–fixed rows. `OriginPremiseCatalogTest`, `OriginPremiseLifecycleTest`; default pipeline bp; warm 0 B; +cold (first build, retained bytes); +cen | +330, t+250 | R (BU; Q6 form 1) | BU (`.OriginProof.cs`, `.CallRelations.cs`, `OriginPremiseClosure.cs`) |
| 2a0-3a | Twins (OQ11 (b)) | §5.1; DIAGNOSTICS' Origin-relations paragraph; DEC | 2a0-2 | Comp: `LocalRegionInferenceTest`, `LocalRegionCompositionTest`, `LocalOriginClauseTest`, `CallOriginRelationTest`, `EnumLocalOriginTest`, `OriginObligationVerdictTest`, `PairFollowTest:358-386`. New: `bad`'s return publishes its position row and keeps its record under `Ownership.UnprovenOrigin`; a finite → fixed declared clause publishes its row and its twin; a fixed–fixed record keeps `Binding.OriginRelation`. Snap record by record (N-3: only the label changes); +cen; +led with the interim rows listed. Done when every recorded body chain has its rows and no program goes from rejected to accepted | +30/−6, t+90 | R (BU; Q6 form 1) | BU |
| 2a0-3b | No-candidate regions (OQ12 (a)) | §5.2; census first; DEC | 2a0-3a | Comp: c8c1f3f534b0 and s-vars accepted on both (clause 15.3.6); s-noprincipal valid; `IncomparableContravariantBoundsRequireAnAnnotation` re-expected (valid, or the located contravariant limit, H); s-noprincipal-err gives the relation record; a variable outside a body keeps `MissingOriginBinding_Kd`; the S3 demo stays a located `Unsupported_Kd` (pinned); `LocalRegionInferenceTest`; snap; +cen; +led. Done when no `principalOriginFailures` remains, `OriginDeclarations.cs` does not grow, and every vanished rejection is a recorded 15.3.6 reading | +40/−30, t+70 | R (BU; Q6 form 1) | BU |
| 2a0-4 | Ledger split | §5.3 | 2a0-3b (or 2a0-3a, §5.3); R5-Q17 merged (AR-3) | Tool: `MirDifferenceTest`; +led. Done when every `UnprovenOrigin`-family row has one owner | +2/−8, tsv ±3 | R | adv |
| 2x-R15 | A5's Place steps (bp) | `StepClass`, `Deref`, `Walk`, `Step` move from `MirBorrows.Places.cs` into `M/Analysis/MirSteps.cs` (contract first); `Step` gains its prefix; `Through` stays | 2e-2 (E17's row), on lane C | as 2x-R1: bp triple, ledger raw rows equal, `MirAnalysisTest` | move +95/−90, +5 | R | no |
| 2a-1a | MirOrigins: frame, rows, containment, Refuted, Reason | §6.1–§6.3 without Unknown; driver step 4 in its final order; `MirProblem.Reason`, the key, `MirFactKind.Relation` and the `int`; DIAGNOSTICS (`contained`, owners); ARCH canonical row; spec-case 15.5/0 relabelled F4; DEC | 2a0-2, 2a0-3a, 2a0-4, 2x-R5, 2x-R15, G:A5-F9 merged | Comp `MirOriginsTest`: audit-origins rows 7, 9c and 19 with twins; 15.6.5's `bad`; j-region, j-unreach; m-scope (S at the return); j-two (two problems, one subject); 15.6.4's `current`, `leak` and a capture store, SPEC 11.8's `nameRef` (S) and `limitRef` (valid). If 2b-6 (b) has landed at 2a-1a's start, each is 2b-6's located `Unsupported_Kd` naming P42, pinned; c-own; a-split and a-wf valid (raw stop); n-inner; G161 (`probe/p2` both forms, and d-g161-ok); `rv/guard-annot` (S); `rv/env-store` (S); `meet-trans`: Binding's P and the located `Unsupported_Kd` of row 9 until BU's I-1 repair, which re-expects it as S with MeetNode. MeetNode itself is pinned by a `MirOrigins` test over a view fixture with fresh-region rows, until a program reaches it; the containment edges of p1's local-layer form and of `x-skip` present (their P is 2a-1b's); the per-builder-site identity test with a Slice element, `leak3` (pinned: no MIR problem, naming §9 item 6) and `leak4` (the storage-end conflict); `rv/meet-trans-ok` (the located `Unsupported_Kd`, no S or P; ledger row (a) with I-1's PLAN row); PD31 facts; `VerifiedSpecExampleTest`, `MirAnalysisTest`; +V16 "F4 Refuted", "F4 containment"; +warm; +cen; +led. Done when the Unsatisfied rows 2x-T2 assigned pair or are (a)/(d) | +300, t+340 | G:A5-F4 | drv |
| 2a-1b | MirOrigins: Unknown | §6.3's Unknown, §6.4: oracle use, masks, `Work`, arena, `MirDataflow.Within` | 2a-1a | Comp `MirOriginsTest`: r7-inv, r7-subst(-ok), c-detach, c-box, c-peekU, c-cut, c-pair per case, j-f, j-pick per case, j-bits (two words), n-pass, x-skip from `IteratorIndependenceTest` (P at the Reborrow), p1's local-layer form (P, a pinned (a) row), bc801917a239 (two operands), e3312717bde0 (two rows); a failed `==` is one problem; S1015 items 25, 27, 28; CallBorrows rows Proven; MIR equals Binding's direct verdict on the r7/j cases; the limit test (if no source binds within budget, `Within` driven with MirOrigins' counts and the shortfall recorded); +V16 (9) and the 64-view body; +warm; +cen; +led. Done when the Unproven rows 2x-T2 assigned pair or are (a)/(d) | +170, MirDataflow +6, t+260 | G:A5-F4 | no |
| 2a-2 | The merge | §7: merge context, rules 1–3, `MirFactKind.Result`, `Ended` from MirBorrows' recorder; DIAGNOSTICS A6 rows with N-6; DEC | 2a-1b; a PD21 response for `MirBorrows.Reports.cs` if due at 2a-2's start (2e-D's response precedes 2e-4) | Comp `MirOriginsTest.Merge`: j-bad and j-region (one Unsatisfied, no conflict); g-return; g-two (S, C at cleanup, P); `rv/guard-annot` (S kept); `rv-g-annot-esc` (per OQ-4: (a) S lost, pinned; (b) S and C); `rv-g-param` (per OQ-4: (a) C and P; (b) S, C and P, pinned); `cf-g-reassign` (per OQ-4: (a) and (b) S lost, pinned); m-36f (C kept); m-write (the plan's form: the relation and the write conflict), its `var r` form, `cf/p/m-write3` (an annotated `let` initializer, a row: S and the write), and m-write4 (the relation only; the limit, pinned); m-pair; m-scope; m-scope-use; m-store; m-twice/same-root; a block, `if` and `match` result for `ResultFit`; a conflict without a Refuted twin kept; +led. Done when the a5-new storage-end rows 2x-T2 assigned go (≈ 13, H), except a conflict another live holder or storage the body does not own also takes (m-store, `cf/p/jregion2`), and their Unsatisfied rows pair | +45, t+140 | G:A5-F4 | no |
| 2a-4 | Tests and target sides | The 13 test files reading `Binding.Obligations`, `JudgeOriginObligation` or `LocalRegionSources` gain MIR twins or OldRule markers (2x-T5). The 18 skip forms (`IteratorIndependenceTest` 10, `IteratorOriginEffectsTest` 5, `IteratorIndependenceEditingTest` 3) and `VirtualEffectSelectionTest`'s 2 get MIR target sides under OldRule (PD9, OQ-2). p1's acceptance under OldRule. `PairFollowTest` detach rows on MIR. `Landed += F4, F2` | 2a-2; 2x-T5 | Comp: migrated classes on both pipelines; `VerifiedSpecExampleTest`. Done when no A5-F4 rule is asserted only through old internals and the ratchets do not rise | t±220 | G:A5-F4 | no |
| 2a-5 | Records and ledger close | §9's ARCH, PLAN, CODEMAP and STATUS; `classification.tsv`; AR-4 additions | 2a-1a–2a-4; 2a0-3b; 2a0-4 | Doc + Evid: +led. Done when the A5-F4 blocking rows 2x-T2 assigned are gone and the eight signatures report on the MIR: e3312717bde0, bc801917a239 (2a-1b); c8c1f3f534b0 (2a0-3b, valid); 1720228ea1c3 (the clause's Loan row, re-triaged here); 9b64b783b09b, cd1535825466, 53d750a49d82, 6b46c86d05a2 by their codes at 2x-T2's base (H) | docs ~45, tsv ±6 | G:A5-F4 | no |

**Ledger.** §5.1's 85 blocking rows (old base `806110fc`): Unsatisfied → 2a-1a; Unproven → 2a-1b; UnprovenConstraint 1
(c8c1f3f534b0) → 2a0-3b; Loan Comparison/Binary 1 → 2a-2/2a-5. The plan's per-code list sums to 87 (Unproven 69, Unsatisfied 16, plus 2) against its total of 85, so 2x-T2's re-triage fixes the
split (H).

**PD21** (count at each unit's start; a restructure resets): `Binding.OriginProof.cs` is due after 2a0-3b (2a0-2, 2a0-3a,
2a0-3b), with no later A5-F4 edit; `Binding.cs` is over the limit, and 2a0-3b only deletes there (a ruling if due); `Binding.Diagnostics.cs` and `.OriginDeclarations.cs` are over the limit (net 0 and net ≤ 0);
`Binding.LocalRegions.cs` is at 3 on dev; `MirAnalysis.cs` takes 2x-R5 before 2a-1a; `MirReport.cs`, if due at 2a-1a or 2a-2,
takes a ruling or a split of the derived-problem rules (bp); `MirProblem.cs` is due (2e-D's response, then recount);
`MirBorrows.Reports.cs` takes a PD21 response before 2a-2 if due (2x-R2 has landed); `MirDataflow.cs` takes a ruling at 2a-1b if due.

**Sizes (H).** Binding about +390/−40; Mir about +520, plus 2x-R15's move; tests about +1,150. At R6-check the old reporter (about 110 lines), the twins' branch and the gates go.

**Speed (H).** MirOrigins is linear per class in rows and Refs, plus the /64 terms. The view's first read is one pass over the
obligations. The oracle extracts once per body with fixed rows. +cold and +warm measure it; the `CallBorrows` rows of library
bodies dominate the oracle's count.

## 11. Owner questions (with OQ2's package approval)

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-1 (A's OQ-F4-1, B's OQ-B4) | SPEC 15.6.5 makes an unannotated local's slot an inferred region, and 15.4.4 lets later uses constrain it. Binding gives a `let` slot a declaration-time candidate (2x-P4 D4). Should every body-local omitted slot become a region? | (a) Now, in 2a0-3b: one representation for one concept, but every `let` borrow local's Binding representation and old-analysis verdict change on the default pipeline under D5 (H). (b) A SPEC clarification that the declaration fixes the candidate. This narrows SPEC 15.4.4's "later uses constrain them" to fit the implementation, which AGENTS forbids without a SPEC rationale; it leaves the S3 demo a located `Unsupported_Kd` and makes the D4 forms errors by SPEC. (c) A separate BU unit with its own full-process design and census, after G:A5-F4, with a PLAN row meanwhile | **(c)**. A cross-phase representation change needs its own design. No dev program was found that D4 rejects with a Language error although valid (H; the S3 demo is a located `Unsupported_Kd`), so the plan-letter 2a0-3b costs no known soundness |
| OQ-2 (A's OQ-F4-2, B's OQ-B2, census Q5) | Binding's preserves-results containment skip accepts 18 tested forms that SPEC 15.6.3 makes Unknown (library and milestones 0; not miscompiles, census (a)). A5 rejects them from 2a-1a | (a) Delete it at R6-check with the old path (AR-4). The 18 Unknown acceptances stay on the default pipeline until then, and 2a-4 re-expects them under OldRule now. (b) Delete it now in a BU-reviewed Binding unit (−55; the 18 tests and one fixture re-expected; OQ7 (a)). (c) A SPEC proposal exempting `preserves results` bodies. (d) Hold: the skip stays on the default pipeline and A5 still rejects the forms in shadow, but their ledger rows and the 18 tests' target sides stay unclassified, and the skip has no removal stage, until the owner decides | **(a)**: no default-pipeline change during the freeze, and SPEC's letter on the MIR. (b) if the owner wants both pipelines aligned before R6-check |
| OQ-3 (A's OQ-F4-3, extended) | Confirm the departures from owner-approved texts as one list (OQ6's pattern) | (a) Confirm all: (1) A3 §7's one-witness BFS → finite-source bitsets (D1); (2) PD11's second half and A3 E7's merge sentence → conditional GuardReverse (2x-P4 D2); (3) A3 §7's "edges between fixed atoms are entailed by Binding's meet" read as containment edges judged against Binding's encoding, plus MeetNode; (4) the plan's `OriginAtom.Of` kinds (Fixed, FiniteBody, FiniteStatic, Inferred) → SPEC's three with §3's precedence; (5) `UniversalRelations.For(definition, case)` → per body with per-class conditions; (6) T1 by construction; (7) 2a-3 folded into 2a-1a; (8) rows partitioned at formation, no alias rows; (9) PD11's first half, "whose Borrow the Origin stratum refuted", read per atom with the `Result` fact and a `ResultFit` row (§7 rule 1); (10) 2a0-3a adds non-call fits' rows only where Binding records a failure until H11, while the plan's 2a0-3a row says "`JudgeCallPosition`'s pattern … every inner chain has a row" (§5.1's H11 end state restores it). (b) Per item | **(a)** |
| OQ-4 (N-6) | Rule 2 (GuardReverse) loses an independent failure when one guard candidate both escapes (err(ended)) and has a separate non-escaping Refuted relation (`rv-g-annot-esc`). SPEC 23.1.2 says each independent failed requirement is reported; the program stays rejected | (a) Accept as a recorded limit: a pinned test, a ledger class for its LOST row, and registration on the X-2/OQ9 list. (b) A rule variant that keeps `rv-g-annot-esc`'s report: rule 2 drops a guard-sourced Refuted problem only when its last edge is a `ResultFit` row. Prototype (`rv/protoA/runA4.txt`): `rv-g-annot-esc` ok, every other case unchanged, but `rv-g-param` (a candidate stored through a parameter's referent) reports both codes for one failure (DOUBLE). It still loses an independent Refuted relation at a `ResultFit` row whose returned value no longer carries the candidate's Loan while the candidate escapes elsewhere (`cf-g-reassign`: `saved = fb2` before `return saved`; LOST under (a) and (b), ok under PD11, which loses g-two). (b) narrows N-6 and does not close it. The DOUBLE is a derived report SPEC 23.1.2 leaves implementation-defined, but it departs from A3 E12's "one failure, one code". (c) A Loan-stratum flow fact (2x-P4's option (b)): drop only when the Refuted chain's fixed end is reached through a holder err(ended) witnesses; exact (H; not prototyped), but a new fact in MirBorrows | **(b)**. SPEC 23.1.2 is the authority and (b) drops strictly less than (a), so it adds no acceptance. Its cost is a second code only for guard escapes into storage the body does not own. If (b) is chosen, §7 rule 2 gains the `ResultFit` condition, 2a-2 tests `rv-g-annot-esc` for both codes and pins `rv-g-param`'s two codes, and OQ-3 (2) reads "GuardReverse at a `ResultFit` edge". Under (a), the package text applies as written |

Closed without an owner answer:
- **OQ-B1 (T1).** The 3 overlaps are one clause at two different uses, which T1 does not exclude. T1 holds by construction and is
  confirmed in OQ-3 (6).
- **OQ-B3 (the twin's Opaque).** A consequence of OQ11 (b), which the owner chose. It is recorded in STATUS and H11's list.
- **OQ-B5 (a static atom classed twice).** The premise is false on dev: static Borrow atoms are per site (`rv/st-both`). §3's
  order decides both. The forms are dormant after 2b-6 (b), and §8 asks P42's unit to keep per-site binders.

No O1 question: no closed vocabulary grows (`OriginAtomKind` and `MirFactKind` are not listed in ARCH).

## 12. Still unverified

- No A5-F4 code exists. The verdicts are derived from dev's rows, atoms and Types (the dumper) and from the rules; the prototypes'
  atom classes are hand-assigned. The census counts (85 rows; 41 + 10 records; 742 `CallBorrows`) are 2x-P4's base, re-measured
  by 2x-T2.
- That pending keys are unique per declaration, so resolving an alias gives the same atom at every use in a body (2a0-2's alias
  test).
- That no body row starts inside a `FunctionTypeKoto` (§4; the agreement test).
- That Binding has a formation-time owner for every row producer (§2): functions, Closures, accessors, defaults (from Binding's
  default index) and static initializers. Static initializers formed no rows in the confirmation's probes. If a producer lacks an
  owner, 2a0-2 stops and reports.
- Whether rows naming another declaration's universal atoms occur beyond I-1 (2a0-2's +cen stop condition), and whether another
  2a-1a/2a-1b test or blocking signature (e3312717bde0, bc801917a239) runs through such atoms and so becomes row 9's U.
- MeetNode has no dev reproducer until I-1's repair (2a-1a pins it with a view fixture).
- OQ-4 (c)'s exactness (not prototyped).
- The Slice-handle repair's census and its default-pipeline effect (§9 item 6, BU).
- N-3: that twin rows add no default-pipeline record (2a0-3a's snap).
- `ResultFit` for block, `if` and `match` results (2a-2's tests).
- That a proven non-call fit's missing edge never hides an acceptance before H11 (§5.1, a reading).
- OQ-1's census; the cost terms on real MIR and the memory-limit test's constructibility.
- The 2c-6 interactions: environment classes per receiver case, the closure result formal, and st-capture2.
