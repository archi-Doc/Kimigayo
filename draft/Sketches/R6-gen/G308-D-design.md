# G308 design: one comparison per distinct node of TyCtxt.Comparison's DAG

Status: the design-first candidate (D) of the Full change G308, revision 1, for the implementation on `claude/r6-g308-d` from the
shared acceptance tests `claude/r6-g308-tests` `2bb98839d` (dev `9613a512e`). Working record, not authority: what it decides is
authority only as DECISIONS R5-M166 once a choice lands it. Revision 0 is `artifacts/handoff/2026-10-30-r5-pause/designs/g308-design.md`
(dev 6518409c8); the planning session's structure check (2026-10-31) passed it with the rulings below.

## Revision 1 (post-G7 dev and the rulings)

| Item | Revision 0 | Revision 1 | Source |
| --- | --- | --- | --- |
| Class | Major or Full (Q1) | **Full**: two candidates judged by the shared acceptance tests; §8's design-review paragraph does not apply | ruling Q1 |
| Old generator | "sel", "legacy twin", "both generators", `-Pipeline mir` + sel | gone with G4-G7 (no `KIMI_CODEGEN`, sel lane or pending ledger); only Binding's `RequirementInstances`/`EffectBounds` readers stay (§1 rows 4-5, §9 (i)-(ii)) | structure check |
| A4's row | who writes it (Q2) | this candidate writes it on its branch; R4 owns `Mir/Analysis/**` and reviews that hunk if D is chosen | ruling Q2 |
| Open issue (i) | join or not (Q3) | stays R4's (A-12); (ii) stays H11's | ruling Q3 |
| Ref layers | an item or inline (Q4) | every composite part is an item, a ref layer's included | ruling Q4 |
| `ComparisonMode` | three values or derived (Q5) | three values (`Mapping` 0, `Operator` 1, `Reversed` 2), in `Mir/MirVocabulary.cs` beside `MirCalleeKind` | ruling Q5 |
| Layout records | per item or per site (Q6) | per requesting site: the comparison call lays out its Self and each Tuple node past a reference layer at its subject, a walk over the distinct nodes (a visited set, so linear) | ruling Q6, problem D1 |
| Requirement callee's Contract | null | null: `MirCallee`'s own-Contract form (R5-M75's leaf calls wrote the Contract) | §5.1 |
| Template-keyed item's representative | the enclosing body item's (so `TypeOf` closes template Types) | the Witness target's substitution, as every Builtin item (its Self the instance Self), so `ModuleCodegen.Shim` and R5-M126's one Abi per key are unchanged. The template node gives only the shape (which part is a composite, a Builtin leaf, a Requirement leaf); the instance Self gives each part's instance Type (`Components[i]`, parallel to the node's parts by `TyCtxt.Compose`); a Requirement leaf's witness is its Contract's requirement on the instance part (`Runs`: a body, or a `Mapping` item). No item needs the enclosing substitution beyond its key | implementation finding (shorter) |
| The item's node | read by the collector and codegen each | the collector publishes it (`InstanceCollector.Comparison(item)`: `TyCtxt.Comparison` of the key's Type for a template-keyed item, else of the representative Type, under the mode); codegen reads it | AGENTS "a consumer lacking a fact has the owning component publish it" |
| Tests | §6's lists | the shared acceptance tests replace §6's regression and new rows; §6's retargets (K2) and the goldens (K1) stay this candidate's, each named in the commit | structure check |
| O2 measurement | §6 | a completion check of the chosen candidate, not this candidate's work | structure check |
| Checks | the window list with sel | incremental builds and the selected classes; the acceptance rows, the retargeted classes, `MirEffectsTest`, `InstanceCollectorTest`, `FunctionCodegenTest`, `MirBuildTest`, `ComparisonCompositionTest`, `-Fixtures 'Comparison*.ll'` on the default pipeline and `-Pipeline mir`, the non-incremental `-warnaserror` build; `check` of `P_16`, `P_20`, `P_24` timed | candidate brief |
| Owner question | O1 | unchanged: asked at the choice, only if this candidate is chosen | ruling O1 |

Sections 0-10 below are revision 0's text; where they mention sel, a legacy twin or both generators, the table above governs.

Base: `origin/dev` 6518409c8 (read in the throwaway worktree `bin/worktrees/r6-g308-d`, removed). Design only; no build or probe was
run (the cause is in the code; the measurements are the buildmir probe's, `C:\Users\bwff1\AppData\Local\Temp\claude\r5\buildmir\`).

## 0. Recommendation (one paragraph)

Option (b2): a tuple operator is ONE call in MIR, a Requirement call of its Contract's own requirement (`equals`/`compare`) on its
Self whose `MirCallee.Arg` carries the operator's comparison mode; the instance collector admits one comparison item per distinct
node (the existing `MonoKind.Builtin` shim kind, keyed by compiler function, node Type and mode), whose targets are the node's DIRECT
parts (a composite part's own item, a witness body, a requirement leaf's witness); code generation writes one function per item that
calls its composite parts' functions instead of inlining them; A4 gets one row that runs a comparison's distinct leaves. This removes
R5-M75's expansion, and the same rule makes the existing Contract-mapping shims linear. A node with an abstract part, under the operator
mode, is keyed by its Type at the template and the enclosing instance (SPEC 13.4's generic rule, §3). No ARCH vocabulary entry; one
owner question (K package 4.3's Builtin key).

## 1. Failing condition and cause

- Reproducer: BU's V16 family, `D_i<T> { x: D_{i+1}<(T, T)> }`, `func eq(a: ref/D1<i32>, b: ref/D1<i32>) -> bool => a.x…x == b.x…x`:
  Self is `T_n`, a pair nest of depth n-1 with 2^(n-1) leaves but only n-1 distinct Types. `eq` builds 2^(n+1)+3 statements and 2^n
  blocks (8,195 / 131,075 / 2,097,155 at n = 12 / 16 / 20); `check` 28.5 s at n = 24 on dev before G2, 56.6 s with G2's analyses
  (loaded machine); from n = 16 `eq` exceeds A1's arena (`OwnershipStorageLimit_Kd`, unpublished until H3).
- Cause: `TyCtxt.Comparison` memoizes one node per (contract, Type, operators), so `T_k`'s node has Parts `[T_{k-1}, T_{k-1}]`, one
  object twice: a DAG of n-1 nodes. Every consumer walks it as a tree, once per root-to-leaf path. Sites with this cause (search for
  readers of `ComparisonNode.Parts` and `BoundComparison.Parts`):

| Site | Path | On the default pipeline | Owner |
| --- | --- | --- | --- |
| `MirBuilder.Calls.Comparisons.cs` `Expand` :48, `Leaves` :96 (W1) | tuple operator | yes (BU-48, G3) | R5 builder |
| `InstanceCollector.Witnesses.cs` `Shim` :181, `Leaves` :190 | Contract-mapping shim (generic `T is Equatable` with a V16 Self, a Dictionary key) | yes (G2 check, G3 build) | R5/R6-gen collector |
| `FunctionCodegen.Shims.cs` `Compare` :104, `Parts` :124 | the same shim's text | yes (G3) | R5/R6-gen codegen |
| `Binding.RequirementInstances.cs` `Witnesses.Leaves` :31 | A4's built-in-target row (`MirEffects.Solve.cs` `Require` :292) | yes | R4 (A-12), legacy twin |
| `Binding.EffectBounds.cs` `Comparison` :775 | the old effect walk (H5) | until H11 | Binding, legacy |

  The first three are this unit; the last two read the legacy plan (`BoundComparison`, a DAG by its memo, also walked per path) and
  become open issues (§9). The old generator writes one `__kimi_comparison<N>` per plan node (`GenericStoragePlan.Comparisons.cs`), so
  the legacy path was linear: the MIR path regressed it.

## 2. Required meaning, and why one body per node is equivalent

- SPEC 13.4: tuples compare elementwise for equality and lexicographically for ordering, left to right until decided; an unordered pair
  makes all four orderings false; `+0.0 == -0.0`; a NaN makes `!=` true and the other five false. Operands of safe value-reference
  layers denote their terminal Place (§3.4.3, R5-M143); a comparison never Moves; a Non-Copy owned operand is inspected through a shared
  Loan formed when it is evaluated, live while the right operand is evaluated, ended at completion; the `bool` keeps none.
- SPEC 13.4.1: a user leaf runs `Equatable.equals` / `Comparable.compare` on shared borrows of the two leaf Places (bool, or i32 against
  0). SPEC 13.4's generic rule: in a generic body a leaf of Type `T` under `T is Equatable` uses T's Contract mapping in every instance,
  so `(T, f64) == (T, f64)` with `T = f64` is NaN-reflexive at the T leaf and IEEE at the f64 leaf (`tests/mir/tuples.kimi` documents it).
- SPEC 8.15.3: comparing parts behind an indirection needs that layout, a Language error at the use; SPEC 22.5.1: a caller-location
  witness reports the invoking expression.
- Observable effects: the sequence of witness calls (their effects and arguments), the Loans, the result. No built-in leaf aborts.
- Equivalence (induction on a node N): R5-M75's expansion runs leaves(N) = leaves(p_1) ++ … ++ leaves(p_k) in order and stops at the
  first unequal pair, deciding by the operator on it, else the last pair decides. A per-node function cmp(N) runs cmp(p_i) for i = 1..k
  and returns the first result that is not "equal", else cmp(p_k)'s. By induction cmp(p_i) runs exactly leaves(p_i) up to its own first
  unequal pair, so cmp(N) runs the same witness calls in the same order and stops at the same leaf. Values: equality returns false at
  that leaf, else true (the last pair's equality), the same bool. Ordering returns the deciding leaf's i32 unchanged (a witness's
  `compare`, or a built-in leaf's -1/0/1), which the root compares with 0 by the relation: the same as "the operator on the pair" for
  every ordered pair; for an unordered pair the function returns the sign that makes the relation false (+1 for `<`, `<=`; -1 for `>`,
  `>=`; §4), and an all-equal tuple gives 0, the relation's reflexive value, as the expansion's last pair does. Places: each function
  receives `ref/Self` addresses of the two terminal Places and reads a part at its Field offset or through a ref layer's pointer, the
  Places R5-M75's `Part` projects; witness leaves get shared borrows of the same leaf Places. Loans: the expansion holds two shared
  Loans to its join with reborrows into each leaf call; the call holds the two shared borrows for its duration; the conflict set is the
  same, since nothing else runs between the operands' evaluation and the completion. A Never operand diverges before either form runs.
- Template versus instance: the operator mode's tree depends on the template (which leaves are abstract), the Contract mapping's does
  not (it is uniform). So an operator-mode function for a node with an abstract part cannot be keyed by the instance Type alone:
  `(T, f64)` at `T = f64` and a written `(f64, f64)` have one instance Type and different leaves. This decides the key (§5.3).

## 3. What the representation lacks, and who owns it

- MIR has no callable comparison of a node: R5-M75 inlines the tree into each body (MIR is per template), and the Backend's shim inlines
  it into each item's function. The missing representation is "the comparison of a node as a callable", once per distinct node.
- Owners: `TyCtxt.Comparison` (Binding) owns the composition and publishes the DAG, unchanged. The MIR builder owns the call site (the
  one fact it reads, `CallPlan.TupleOperator`). The instance collector owns items: one comparison item per (compiler function, node,
  mode[, instance]) and its targets. Code generation owns each item's function. A4 owns what a comparison call runs (effects).

## 4. Options

(a) A memo inside the builder (share sub-comparisons within one body). Each occurrence of a node sits at different Places
(`l.0.1.0` versus `l.1.1.0`) and MIR has no subroutine or indirect jump, so blocks cannot be shared without a hand-made return switch
through reference locals; statements stay at least the leaf count, 2^(n-1). Fails V16 by construction. Rejected.

(b1) One synthesized MIR body per distinct node (a new `MirBodyKind.Comparison`, keyed `MirDefKey(enclosing definition, Comparison,
ordinal)`, called by a Fn callee). Correct for generics by construction (a template body per node, instantiated like its enclosing
body) and A4 would enter it through the existing body edge. But every MIR body today is Binding-formed: A4's premises come from
`body.Key.Decl` (so the key must name the enclosing definition, one copy per definition and instance), A5's C1-F4 view is keyed by
`MirDefKey.Body` (a synthesized body needs an empty, distinct `ExecutableBody`, unverified that the view tolerates it), codegen's body
signature comes from the declaration (`ModuleCodegen.Signature`, a new rule), the body must be caller-location, and the builder must
build bodies outside its definition walk. It also leaves the Contract-mapping shim (§1 rows 2-3) exponential, which then needs the (b2)
fix anyway: two mechanisms for one concept. Size about +150-250 lines across MirBuilder, MirProgram, MirDefKey, the validator, A3/A5
views (R4, BU), the collector and codegen. Vocabulary: `MirBodyKind.Comparison` (not an ARCH closed vocabulary; a MIR identity change,
R3 decision 6). O2: a call per node at O0, inlined at O2. Rejected for the special cases and the second mechanism.

(b2, recommended) One comparison item per distinct node on the existing Builtin shim path, both modes (§5). Sites: the builder (−),
the validator, MirDump, A4 (one row), the collector (one Edge row, Shim targets one level), codegen (part calls, mode leaves). No
ARCH vocabulary entry. Size about net 0 code (−95 builder, +90 elsewhere), §7. O0: one call per tuple operator and per composite part;
O2: small internal functions are inlined (C8f: "no shim call survives at O2"), deep DAGs keep linear calls.

(c) Shorter variants, each rejected:
- (c1) operator shims keyed by the instance Type and a mode only: wrong for `(T, f64)` at `T = f64` (§2), the golden's own row.
- (c2) the pre-R5-M75 whole-tuple call into the Contract-mapping shim: NaN-reflexive where `==` must be IEEE, and no Comparable mapping
  exists for float orderings (f32, f64 are Equatable only); A4 meets the Limit R5-M75 removed.
- (c3) inline expansion below a leaf threshold (rejected by the probe as a symptom fix; a second form of one comparison).
- (c4) a four-valued result (-1, 0, 1, 2 = unordered) for one ordering function per node: the legacy form R5-M75 rejected, a non-uniform
  root decision and a sign normalization of every witness `compare`. The two ordering modes of §5.1 keep compare's "i32 against 0".

## 5. The recommended design (b2)

### 5.1 MIR form and builder (R5 hold: `MirBuilder.Calls.Comparisons.cs`, `MirValidator.Calls.cs`, `MirForms.cs`, `MirDump`)

- `ComparisonMode` (new local enum, not an ARCH vocabulary, as `ComparisonKind`, R5-M68): `Mapping` (SPEC 13.4.1, every requirement
  call, 0), `Operator` (SPEC 13.4's built-in operators: IEEE equality; for `<` and `<=` an unordered pair compares as greater),
  `OperatorReversed` (for `>` and `>=`, an unordered pair compares as less; Comparable only). Home: `Mir/MirVocabulary.cs` beside
  `MirCalleeKind` (outside TyCtxt's hold; the Backend reads Mir). Open: derive it from the relation instead (Q5).
- `MirCallee.Arg` of a Requirement callee: 0, or a tuple operator's `ComparisonMode`; the Symbol is the Contract's own requirement
  (`OwnRequirement`), Subst `TyCtxt.Synthesized(requirement, Self, contract.Type)` (Self the conforming Type at the template), Contract
  null. `MirCalleeKind` stays at 5; `Arg` is already the kind-specific field (Fn's template, Intrinsic's id, Value's token).
- `Compare` (one path for every comparison operator): a tuple operator keeps R5-M75's operand rule (the left view pending and secured as
  a built-in operator's, the right view) and its root check (`TyCtxt.Comparison(contract, Self, operators: true)` null: one located
  `Unsupported_Kd` with an Opaque value, operands not built; `opaque.mir` and `ATupleOperatorWithoutItsComparisonIsUnsupported` keep it);
  then one Call of the comparison callee on shared borrows of the two terminal Places (`Inspected(Follow(view, 16, Self), shared)`, R5-M75's
  Contract-leaf rule); then the code every Contract comparison already has: `==` the bool, `!=` its `Not`, an ordering the i32 against 0
  by the relation. Deleted: `Expand`'s leaf loop, `Leaves`, `Part`, `Pair`, `Decided`, `Push`, the `leaves` list (~95 lines with
  comments).
- Validator: a Requirement callee with `Arg != 0` names Equatable's or Comparable's own requirement, its Self is composite
  (`ComparisonTypes.IsComposite`), `OperatorReversed` only with compare; the existing signature rule types its two `ref/Self` operands and
  its result. MirDump prints the mode.

### 5.2 A4 (R4 hold: `MirEffects.cs`, `MirEffects.Solve.cs`; R4 reviews)

- One row before the generic requirement row: a Requirement callee with `Arg != 0` runs, once per distinct node of
  `TyCtxt.Comparison(Contract(Symbol), Subst.Self, operators: true)` (a pooled visited set; warm 0 B), what R5-M75's leaf call of that
  node ran: a Witness leaf the Fn row on its Target and Subst (`Function` takes the callee instead of the terminator), a Requirement leaf
  the requirement row under the body's premises (`DispatchBounds`, X2; its operands are shared borrows, so `Given` adds nothing), a
  Builtin leaf nothing. This is the expansion's verdict by construction: `ACompositeComparisonRunsItsPartsWitnesses` (five rows) must
  pass unchanged.
- Not in this row: `Require` on a composite Type (the built-in-target row, atoms) keeps `TyCtxt.Witnesses` (another contract: a published
  requirement on a Type, whose abstract leaves become atoms); its per-path legacy walk is open issue (i), §9.

### 5.3 Instance collector (`InstanceCollector.Edges.cs`, `.Witnesses.cs`, `.Admit.cs`, `Instantiation.cs`)

- Edge row for a comparison callee: compose as now (`Made` reads the instance Self's storage), resolve the built-in target
  (`TyCtxt.Witness(requirement, null, composed)` is Builtin for a composite Self; Limit is the storage gap at the call; anything else a
  broken contract), lay out the instance Self at the call's subject (its comparison use, SPEC 8.15.3, per requesting site), and admit
  the item: `MonoKey(Builtin, Symbol: the built-in target, Variant: mode, Type: the node's erased Type)`. A node with an abstract part
  under `Operator`/`OperatorReversed` keeps its Type AT THE TEMPLATE (`callee.Subst.Self`, erased) and the enclosing body item's erased
  instance in `Body.Erased` (LegacyRow's precedent of an erased substitution in the key), its Representative that item's (so `TypeOf`
  closes the template Types) and its RepresentativeType the instance Self; a closed node keeps `Body` default, shared by every site.
  `AdmitType` keeps a preset key Type (`key.Type ?? Erase(type)`).
- `Shim`'s comparison targets become ONE level: for each direct part of the item's node (`TyCtxt.Comparison(contract, key.Type,
  operators: mode != Mapping)`), a composite part's own item (same function and mode; template-keyed when the part has an abstract part),
  a Witness leaf's body (`Implementation`), a Requirement leaf's witness under the instance (`Requirement(…, Witness(leaf.Target, null,
  Compose(leaf.Subst, representative)))`: a body or a Mapping item), none for a Builtin leaf. Every composite part is an item, a ref
  layer's included (one rule; Q4). Each item lays out its own Self at admission, so `Leaves`' `pastStorage`/`LayoutNeeds` bookkeeping
  (R5-M149) goes: a part inside the parent's storage lays out with it, a referent past a ref layer is its item's admission.
- Mapping items are unchanged in key (`Variant` 0, instance Self): the Mapping is uniform, so the instance Type decides them.

### 5.4 Code generation (`FunctionCodegen.Shims.cs`, `ModuleCodegen.Shim`)

- `WriteShim` reads the mode from `key.Variant` and the node from `key.Type`; the item's own node is compared by its direct parts (a
  Tuple at its Field offsets of the instance layout, a ref layer through its loaded pointers); a part is the next target's call
  (`Witness(target)`, a body or a shim run, then `CallWitness` with the two part addresses and the received location; today's
  `WitnessLeaf` generalized) or, for a Builtin leaf, `Leaf`. `Decide` is unchanged.
- `Leaf` by mode, through the operator table's columns (no float clause): Mapping equality `Eq or (Ne(a,a) and Ne(b,b))` (as now);
  Operator equality `Eq`; Mapping ordering `zext Gt - zext Lt` (as now); Operator orderings `select(Ne(a,a) or Ne(b,b), ±1, zext Gt -
  zext Lt)` (the NaN term folds for non-floats at O2, as C8f's); strings and zero-sized leaves as now (one `StringCompare` pass).
- A template-keyed item's signature is its requirement's on RepresentativeType (Equatable and Comparable declare no parameter; R5-M126's
  "one Abi per key" holds).

### 5.5 What becomes unnecessary (deletions)

R5-M75's expansion (§5.1's list); the shim writer's recursion into composite parts; the collector's recursive `Leaves` and its
past-storage bookkeeping; MIR's string `Binary` for tuple leaves and its two string passes (G157 (16)'s form, `ATuplesStringLeaf…`).
Kept: R5-M75's operand rule and root Limit; the legacy twin (A-12/H11).

## 6. Tests

- Regression, fails before and passes after (V16 is the deterministic signal; `MirBuildTest`, beside `DropTreesScaleLinearly`):
  `eq` (`==`) and an ordering `lt` (`<`) on `T_n` at n = 8 / 12 / 16: Blocks + Statements of each body equal at the three sizes (before:
  2^(n+1)+3 statements), and the program's statements linear. `InstanceCollectorTest`: comparison items n-1 per mode with two targets
  each, the collector's `Work` linear; the same with a generic `eq<T: Equatable>` on `D1<T>` instanced at `i32` (template-keyed items,
  n-1). `FunctionCodegenTest`: `__kimi_builtin` functions n-1 and the module text linear (the ratio rule of `DropTreesScaleLinearly`).
  A4 has no work counter (none is added): the V16 row with a printing `Key` leaf asserts its verdict at n = 16.
- Goldens: `tests/mir/tuples.mir` (`less`, `nested`, `text`, `generic`: one call each with its mode); `opaque.mir` unchanged (root
  Limit); any golden of `every-form.mir` with a tuple operator; regenerate and read each.
- Retargets by this decision (named in the commit): `InstanceCollectorTest.AComparisonShimsTargetsAreItsLeafWitnesses` (targets are
  direct parts' items and witnesses), its comment's "a tuple operator is no shim"; `FunctionCodegenTest.Strings.ATuplesStringLeafTests
  EqualityThenOrder` (one StringCompare in the ordering function); `MirBuildTest.Calls.ATupleOperatorOverRejectedStorageBuildsItsLeaves`
  (its comparison call builds); `MirEffectsTest`'s R5-M75 rows unchanged in verdict.
- New unit rows: template versus instance key (`(T, f64)` at `T = f64` and a written `(f64, f64)` in one program: two items); a closed
  node shared by two definitions (one item); a mode per relation (`<`/`<=` share, `>`/`>=` share); the validator's row (a mode on a
  non-comparison requirement is a finding); a comparison at two sites of a Type whose referent's layout fails (records per site, Q6).
- Native fixtures (`ScalarEmissionTest.EmitFixture`, new `ComparisonOperatorNodes`): a DAG `((K, K), (K, K))` of a struct `K` whose
  `equals`/`compare` print their arguments: `==`, `!=`, `<`, `>=` deciding at the first, third and last leaf (order and short-circuit), a
  Copy (`i32`) and a non-Copy (`string`) leaf, a `K` with `drop` (operands neither moved nor destroyed); generic `(T, f64)` at
  `T = f64` with a NaN at each position (true at the T leaf, false at the f64 leaf); a generic ordering `(T, f64)` at `T = i32` with an
  unordered f64 under `<` and `>` (both false). `ComparisonCompositionFloating` and `ComparisonCompositionContractFloating` unchanged.
  These pass before (R5-M75 is correct) and guard the semantics; the scaling rows are the fail-before.
- Ledger and windows (post-G7): the chosen candidate's default Session with `-DiagnosticSnapshot`, the comparison fixture families, the
  milestones and the full mir ledger before review (no verdict should move); warm rebuild and warm Write stay 0 B.
- O2 (R5-M8, R5-M135, quiet slot): the corpus has no tuple operator (`O2Corpus.md`), so its IR must be byte-identical; a tuple kernel
  (a sort by `(i32, f64, string)` and a tuple-keyed equality) A dev 6518409c8 vs B, one folder, 15+ interleaved samples: B/A within
  A/A, optimized instruction counts equal ±1, no comparison call surviving `opt -O2` for nodes of up to four leaves; aligned control
  only if beyond A/A.

## 7. Size and cost

- Code (estimate): builder −95; MirVocabulary/MirForms +12; validator +10; MirDump +2; A4 +35; collector +40 −20; codegen +30 −20;
  MonoKey docs +3: about net −5 to +15. Tests about +200; `tuples.mir` shrinks.
- Compile time: builder O(1) per operator (the root node is memoized); collector and codegen O(distinct nodes × parts); A4 O(distinct
  nodes). On V16 at n = 24 the probe's Build 35.1 s and G2's analyses of `eq` (about 30 s) should vanish; W2 (G307) and W3 remain.
  Measure `check` at n = 16/20/24 after.
- Generated code: O0 a call per tuple operator and per composite part (R5-M75 was inline); O2 inlined back for small nodes (C8f's
  evidence; the old generator's per-node helpers had this shape).

## 8. Class, records, documents

- Class: the PLAN row says Major. WORKFLOW defines Full as "a change to a shared representation or across phases"; this changes MIR's
  tuple-operator form, the checked set's Builtin key and A4: the planning session decides (step 2; Q1). If Major, step 4 calls for a
  design review (options differ in representation; departs from R5-M75 and R5-M126; sites span components).
- DECISIONS: one entry superseding R5-M75's expansion and R5-M126's rejected "one function per tree node (more functions and calls)"
  (the reason no longer holds: a shared node is written once per path, exponential on a DAG; per-node functions are bounded by distinct
  Types and inlined at O2), recording `ComparisonMode`, `MirCallee.Arg`'s new meaning and the template key.
- ARCH (auto): the Calls row ("which expands it over TyCtxt.Comparison's leaves (until R7; R5-M75)"). CODEMAP if navigation moves.
  PLAN G308 done; new rows for §9 (i)-(ii). STATUS: no boundary change.

## 9. Risks and open issues

- Template key: a new key use of `MonoKind.Builtin` (template Type + `Body.Erased`); its representative must close the template Types
  (TypeOf) while the signature uses the instance Self. A reviewer checks `(T, f64)` at `T = f64` natively.
- Admission depth: each nesting level is one admission (`DepthLimit` 128); Layout's 64-level window binds first for storage, but a deep
  caller chain plus a 64-deep nest could reach a `GenerationResourceLimit_Kd` the inline form did not (unverified).
- Layout records: R5-M75's MIR had a layout use per site for nodes past a ref layer; a shared item admits its children once (as the
  Mapping shims already do, R5-M149). §5.3 lays out the root per site; children past a ref per site need a walk per site (Q6).
- (i) open issue for R4 (A-12): `Binding.Witnesses`' comparison clause walks the legacy plan per path (A4's `Require`); the same DAG
  walk with atoms for abstract leaves would close it and delete the clause. (ii) open issue for H11: `Binding.EffectBounds.Comparison`.

## 10. Open questions

Planning session: Q1 class (Major or Full) and the design review; Q2 who implements and reviews A4's row while R4 is paused (R4's hold
files; the PLAN row already names R4 for the builder hunk); Q3 whether (i) joins this unit; Q4 every composite part an item (one rule,
recommended) or ref layers followed inline (fewer items); Q5 `ComparisonMode`'s home and whether the direction is derived from the
relation; Q6 one layout record per item or per site for referents past a ref layer.
Owner: O1 K package 4.3's Builtin row gains the mode (`Variant`) and, for an operator node with an abstract part, the template Type and
the enclosing instance in its key (a departure from an owner-approved package row, as R5-M127 confirmed the previous one). No ARCH
closed vocabulary changes (`MirCalleeKind` stays 5; no `MirBodyKind`, `MonoKind`, `CompilerFunctionKind` or `IntrinsicId` entry).
