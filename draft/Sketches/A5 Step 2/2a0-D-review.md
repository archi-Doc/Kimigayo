# 2a0-D design review and confirmation (A5-F4)

Design review (A vs B, real-compiler probes) followed by the independent confirmation of the package (two rounds). Probe
programs and prototype runs stay in the planning session's scratchpad (s2-2a0-d/rv, cf).

# 2a0-D independent design review: A5-F4 (Origin relations and the Binding boundary)

Reviewer of the full-process family design 2a0-D (R4-M74 OQ2); I wrote neither design. Base: `origin/dev` `ecf62815`. The designs
read `92863171`; between the two, the Origin files (`Binding.OriginFits.cs`, `.OriginProof.cs`, `.OriginDeclarations.cs`,
`.CallRelations.cs`, `M/Analysis/MirReport.cs`, `MirProblem.cs`, `MirBorrows.*`) are unchanged. `Binding.LocalRegions.cs` gained
`328043e2` (G140, identical verdicts), so its PD21 count is 3 on dev (A's table says 2).

Read: AGENTS, `s2-design-common.md`, `brief.md`, `design-A.md`, `design-B.md`, the step 2 plan (PD1, PD6, PD8–PD11, OQ6, OQ7, OQ11,
OQ12, §4.2, §4.3), DECISIONS R4-M4, R4-M5, R4-M9, R4-M10, R4-M11, R4-M74, R4-M86, R4-M89, R4-M90, A3 §4.3 and §7, `origins.md`
(2x-P4), `f4-census.md`, audit-origins §5, the S3 plan §3, PLAN G56, G100, G161, G163, ARCH (closed vocabularies, D5 freeze), SPEC
14.8.3, 15.3.5, 15.3.6, 15.4.4, 15.6 (intro), 15.6.1, 15.6.3–15.6.5, 16.3.1, and on dev the code each design cites
(`RecordOriginFitFailure`, `CheckTypeUse(Core)`, `AddOriginFit`, `JudgeCallPosition`, `RecordLocalRegionBound`,
`ExtractOriginPremises`, `PlaceOriginBinder`, `Binding.Meet`, `MirBorrows.Places.cs`, `MirReport.cs`, `MirProblem.cs`,
`OwnershipAnalysis.Analyze`, `Compilation.Pipeline.cs`, `BoundIteration.cs`).

Evidence (scratch only, `rv/`):
- dev built in `bin\worktrees\rv-2a0d` (toolchain junction, restore, Release; removed after, junction first). CLI probes on both
  pipelines (`rv/run.sh`, one folder per program). MIR problems stay unpublished under `KIMI_PIPELINE=mir` (R4-M1), so "default"
  below is the old path's verdict, which both pipelines print.
- An atom dumper (`rv/RvProbeTest.cs.txt`, a test in the scratch worktree only): Binding's `OriginOutlives` rows with resolved
  atoms, records, node Types, MIR locals and every `Ref`'s Type atom, and rows whose Use has no `FunctionKoto` ancestor.
- Both prototypes re-run from empty folders: A's `run2.txt` and B's `run3.txt` reproduce byte-equal (exit 0 and 2). Reviewer cases:
  `rv/protoA/runA3.txt` (A's file plus a merge variant implementing B's guard text and case `rv-g-annot-esc`), `rv/protoB/runB2.txt`
  (B's file plus `rv-st-sites`).

## 0. Verdict

| Item | A | B | Recommendation |
| --- | --- | --- | --- |
| Row predicate | `BodyChain` (= `JudgeCallPosition`'s test) at calls, failed non-call fits and declared clauses | Same test, failed non-call fits only; declared clauses not named | **A** |
| Twin rows' content and store | Every position of the failed fit via `CheckTypeUse`, into `Binding.Obligations` | The failed chains' facts (`FailedOriginRelation(all)`), into a separate Binding list | **A's positions**; the store question is N-3 |
| Row partition | `(definition, defaultIndex)` from the Use; `NoBody` "expected empty" | By the Use's `SourceRef`, as the builder; outside → `InternalInvariant_Kd` | **Neither** (BX-1): by the definition Binding is binding |
| T1 | Holds by construction, view test | "Drop T1" (OQ-B1) | Same code; **A's wording**, no owner question |
| Atom kinds | Seven (FiniteBody/Static/Guard, Environment, Unbound) | SPEC's three | **B's three**, with A's `Unbound` handling and roles |
| Class order | Static row before "binder outside the body" | Same rows, no stated order (OQ-B5) | **Neither** (BX-2): order given in §5 |
| Environment atoms before 2c-6 | Inferred, no containment | Finite (Binding's present judge) | **B** (A-2) |
| Meet nodes | Split at the longer end only | Split plus the meet node's own edge (MeetNode), canonical keys | **B** |
| Containment classifier | Not named | Shared `MirSteps` (2x-R15) | **B**, the Step carrying its prefix |
| Fixed–fixed entailment | One catalog, two entry points; agreement test to R7; `AddEdge` rule | Same; 13-case test | **Same design**; A's corpus test and rule |
| Unbound after resolution | Located `Unsupported_Kd` (complete compilation) | Inferred node | **A** |
| 2a0-3a records | Label rule at the report site | Same | Same; B's recorded Opaque limit |
| 2a0-3b scope | Every `let` slot a region (OQ-F4-1 (a)) | Plan's letter; D4 to a BU unit (OQ-B4) | **B** (N-1) |
| Preserves skip | Delete at R6-check | Delete now in a Binding unit | One owner question, recommend R6-check |
| Merge rule 1 | `Result` fact, Refuted at a `ResultFit` row, by atom | `Escape` fact, store-into-`_0` site match | **A** |
| Merge rule 2 | GuardReverse where an err(ended) exists | Every guard-sourced Refuted dropped | **A** (B-1) |
| Reason | Pooled `MirRelation` copy in `MirReport`, Equality included | `(Longer, Shorter)` on `MirProblem`, row index fact | **B's representation with A's Equality bit** |
| Stratum order | Judge, Borrows per view, Record | Origins, then Borrows | **A** (needed by rule 2) |
| Units | Plan's chain; 2a0-3b before 2a-1a | 2x-R15 added; 2a0-3b off the stratum's path; 2a0-4 not before 2a-1a | Combination in §6.7 |

Blocking: A-1, A-2, B-1, B-2 and the shared BX-1, BX-2 (§2). Each has an exact fix; none needs a redesign.

## 1. The problem and the general rules

The required meaning, from SPEC:

- **R1 (15.6.1, 15.6.5).** Every relation a body checks is an edge, whatever its verdict or reachability. Fits yield one relation per
  Origin position by variance. Declared, substituted and well-formedness relations are relations too. A failed relation adds no
  constraint to region inference.
- **R2 (15.6.5).** Origins are fixed, finite or inferred, classed relative to the checked body. In a Closure body, captures, the
  call receiver and an inferred whole result are fixed. A finite Origin belongs to a Borrow anchored in a root of the body. A
  mutable-static Borrow is finite in the body that forms it. A Borrow reached through a borrow, or of a raw Place, is inferred.
  Refuted is finite → fixed, with no other refuting rule. Unknown is fixed–fixed and not derivable. Each pair case uses only its
  own premises.
- **R3 (15.6.3).** A child is contained in every borrow it is reached through, up to and including the first shared layer. A raw
  dereference recovers nothing.
- **R4 (15.3.6).** The solver has fixed rules, among them a meet on the right is never split, and `(a and b) outlives c` exactly
  when both operands outlive `c`. So a meet is a node whose requirements reach through it.
- **R5 (15.3.6 as integrated, 15.4.4).** A variable with no candidate is a local region in an executable body. Elsewhere it needs
  an annotation. A local's declaration fixes its variables, and later uses constrain them.
- **R6 (14.8.3).** A guard-local Origin ends at guard cleanup. An escape is judged by the ordinary Origin and Loan checks, and
  either code is admitted. A Refuted relation that does not escape is still an error (R1: no liveness).
- **R7 (16.3.1).** Borrows of the destruction receiver are bounded by the drop body.
- **R8 (23.1.2).** Each independent failure is reported. A report caused only by another failure is implementation-defined.

The general rule for A5-F4 follows from these. **Binding publishes every relation it forms in a body as a row, each end resolved
and classed relative to that body. A5 judges the graph of those rows plus the containment edges of the Refs. Binding keeps judging
only the fixed–fixed relations it forms directly (A3 §7, until R7). Under OQ11 (b) it also keeps a labelled twin record for a body
chain it judges itself.** Every finding below departs from one clause of this rule.

## 2. Blocking findings

### Design A

**A-1. The class table contradicts SPEC 15.6.5 for a captured mutable-static Borrow.**
A §3.1 says "the first match decides" and puts "`Projection` of static storage → FiniteStatic" (row 2) before "any atom whose binder
lies outside B → Fixed" (row 5). On dev, an unqualified group-storage Borrow's atom is per site: `Projection[IdentifierNameKoto
count]`, where the binder is the Borrow's own name node (`PlaceOriginBinder`'s fall-through). The dumper shows that the captured
atom `#81` and the closure's own `count@ref` atom `#82` are distinct (`rv/st-both`).

Inside the closure body, SPEC classes the captured atom as fixed. A's row order makes it finite. Once 2c-6 publishes the
closure-result row, A5 would therefore report `UnsatisfiedOriginRelation_Kd`, a Language error, on `rv/st-capture2`: a SPEC-valid
program (a closure returning its captured static Borrow, with an inferred result) that dev reports as a located `Unsupported_Kd`.
dev's own Binding already gives this wrong code: `rv/st-capture` reports "requires the borrow count outlives static, which is false".
The fault is latent until 2c-6. It is still blocking, because 2a0-2's view test pins one class per table row and would fix the
wrong one.

The same first-match order also misclasses a default template's prepared inputs. A's row 7 names them as roots, but their binders
(the function's parameter declarations) lie outside the default expression, so row 5 matches first and makes them Fixed. In
`rv/dflt`, `func g(n: i32, r: ref/i32 = n@ref)`, dev's record is Refuted ("requires the borrow n@ref outlives r, which is false").
That record is a twin, because `Projection[n]` is not fixed for Binding, but A5 would judge the relation fixed–fixed and report P:
a ledger code mismatch, and a disagreement with 2e-D's E17.
Fix: use the order in §5 item 2. A body's roots come first, including a default template's prepared inputs; then any atom whose
binder lies outside the body is Fixed; then the static rule.

**A-2. The environment interim accepts on the MIR an invalid program that the default pipeline rejects.**
A §6.7 reads environment atoms "as Inferred with no containment until 2c-6" and says both pipelines reject such forms today.
`rv/env-store` disproves that. The program is `var f = func [x, target] () => store(target, value: x@ref)`; Binding completes, and
the old reporter rejects the call-position row `Projection[closure#env0] >= t` as Unsatisfied ("requires the borrow x@ref outlives
t"). That row is A5's input. As Inferred, with no source, it judges Proven. The MIR, which accepts the program today for lack of a
stratum, would keep accepting it after 2a-1a, and the ledger keeps a LOST row in the very Unsatisfied set that 2a-1a's done
condition has to pair.
Fix: B's interim. Class the environment atom as Binding's judge does today (Finite) until 2c-6 classes it per receiver case. The
remaining code difference, Refuted where SPEC 15.6.5 gives Unknown for a borrowed receiver, already exists on both pipelines today
and is 2c-6's to resolve. Record it in 2a-1a's DEC entry with that removal unit.

### Design B

**B-1. The guard half of the merge accepts an invalid program on the MIR.**
B §5 drops every Refuted problem whose source is a guard candidate's Origin, unconditionally. Its justification is a lemma: a
candidate value that reaches a fixed Origin reaches `_0` or storage the body does not own. `rv/guard-annot` is a counterexample:
`var t: ref/i32 during a = anchor` together with the guard `keep(t@uniq, v)`. The value reaches the fixed `a` through a local that
is not used after cleanup, so no err(ended) arises. dev rejects the program (`UnsatisfiedOriginRelation_Kd` at `v`, "requires the
borrow v outlives a, which is false"), and its valid twin `rv/guard-annot-ok` is accepted.

Under B's text, A5 drops its only problem, so the MIR keeps accepting it after 2a-2, leaving a LOST ledger row. In the reviewer
run, the variant `RV: B text, AllSources+GuardDropAll` is UNSOUND on A's `g-annot`. B's own prototype column uses 2x-P4's conditional GuardReverse, so the text departs from B's evidence.
Fix: A §7.1 rule 2, with A's driver order. N-6 records the residual limit.

**B-2. B leaves some body chains with Binding and builds twin rows from verdicts.**
B §4.1 applies the predicate at `RecordOriginFitFailure` only. B §2.1 then leaves "declared relations … OJ1–OJ7" to Binding. dev's
`FailedDeclaredRelation` judges a local clause itself when its longer end is finite and its shorter end fixed, and records it. That
relation is neither fixed–fixed (Binding's until R7) nor a row, so under B no unit ever hands it to A5: two producers without a
removal stage (PD8's risk).

Second, B takes its rows from `FailedOriginRelation(all)`'s facts. Those facts describe failing chains (for a meet, a failing
operand's chain, judged with `LocalRegionSources` where the chain runs through a region), not the fit's positions. Where they
differ (H: census (b)'s 10 region chains), A5 re-judges Binding's conclusion instead of the relation (R1).
Fix: A §5.1 (2) and (3). Rows come from the fit's positions (the existing position producer), and `BodyChain` is applied to
declared clauses too.

### Both

**BX-1. The row partition is wrong on dev's input.**
Every `for` over a borrowed sequence adds a row whose Use is the synthesized `$for.iterator` name (`BoundIteration.cs:16-18`). That
node has no parent. The dumper finds one such row in the library's `Slice.kimi`, so it occurs in every compilation, and one per user
loop (`rv/forloop`: rows=52, nobody=3; Milestones 20, 24, 26 and 27: nobody=1 each).
- A sends these rows to its `NoBody` slice, which it states is "expected empty". There the iterator's `Projection` is judged
  outside its body: as a binder outside B it would be classed Fixed, and its edge is missing from its function's graph. A's own test
  "NoBody empty on the corpus" fails on dev.
- B partitions by Site "with the same rule the builder uses". The builder knows a node's body from its traversal, which Binding
  cannot read, so B's own check would raise `InternalInvariant_Kd` in every compilation.

Fix (§6.1): the producer assigns each row to the definition Binding is binding when it forms the row (its scope's executable owner:
function, accessor, Closure, default template, static initializer), never by walking `Use.Parent`. The 2a0-2 test asserts that the
library's and a user's `for` rows lie in their function's slice and that `NoBody` is empty. `OriginInheritance.cs:375` walks
`Use.Parent` the same way; list it as a site sharing the cause.

**BX-2. The class table's precedence is unstated or wrong in both designs.**
B lists the same rows without an order and raises OQ-B5. OQ-B5's premise is false on dev: B states the static atom is "per static
Field (`PlaceOriginBinder` gives the Field declaration)", but the atom is per Borrow site (A-1's dump). With the order in §5 item 2,
the captured atom (`st_c`) is Fixed and the closure's own (`st_o`) is Finite. B's prototype then gives SPEC's verdict (`rv-st-sites`:
ok in every column), and OQ-B5 dissolves. What remains is an ask to 2b-6 (BU, G100's qualified static Borrow): keep a static-storage
Borrow's binder per site, not per Field declaration, or that unit reopens this item.

## 3. Item by item

### 3.1 Rows and their predicate

- **Predicate.** Both designs take `!FixedOrigin(longer) || !FixedOrigin(shorter)` with ends resolved at their use. A names it
  `BodyChain` and replaces the inline test at `CallRelations.cs:312`, so calls, non-call fits and declared clauses share one
  function. B applies it at `RecordOriginFitFailure` only (B-2). Take A.
- **Verdict independence (N-2).** `JudgeCallPosition` adds a body-chain row whether or not the position is proven
  (`CallRelations.cs:313-317`). At non-call fits (`RequireType`: `!FitsTypeAt && !CheckLocalTypeUse` → `RecordOriginFitFailure`),
  both designs add rows only when Binding's own judgment fails. Within OQ11 (b) this is consistent: Binding still judges these
  chains until H11, and a proven non-region, non-call fit's ends are fixed, finite or anchor atoms. In my reading, its missing edge
  loses at most a second report at the same edge, never an acceptance.

  Neither design states the end state at H11, though. When the twin record goes, the rows must stop depending on Binding's verdict,
  or Binding remains a second judge of body chains. Record it in the H11/AR-4 list: "at H11 the non-call fit producer adds a
  body-chain position's row verdict-independently (`JudgeCallPosition`'s pattern)".
- **Tuple.** A and B agree: Site, ends, Equality, Source/Kind, Clause, Destination, CaseMask in the body's binder order. Take A's
  `Source` set, because it carries `ResultFit` (§3.8). `ResultFit` must be set by Binding where it validates a result (the return
  value or tail fit), not inferred from syntax by the view (exact text in §6.1).
- **T1.** Census (e) finds no row produced by a Constraint proof. The 3 overlaps B counts are the same clause at two different uses
  (a call's Constraint and a local annotation's row), which is not what A3's T1 excludes. A's "T1 holds by construction, pinned by a
  view test" is the right record. OQ-B1 is not needed.
- **Alias rows.** Both replace them with resolution at use. `ResolveInitializerOrigins` records `resolved → key` bounds, and B shows
  that call plans keep the original pending keys. Both designs map every MIR atom through Binding's resolution, which the same
  per-body table serves (A: `OriginAtom(definition, origin, site)` appended to the body's table; B: `OriginAtom.Canonical`). They
  are equivalent if keys are unique per declaration; both mark this (H), and 2a0-2's alias test decides it.
- **CallBorrows** rows are included by both (742 in the census, all Verified). Library bodies carry them too: the dumper shows
  `Parameter[struct FixedBuffer source] >= Input[...]` rows in every compilation. Their oracle cost belongs in +cold.

### 3.2 Atom kinds and identity (G163, G161)

- **Kinds.** No rule reads the bound of a finite Origin: Refuted fires for any finite → fixed pair, and a fixed Origin contains every
  point of the body. So SPEC's three kinds suffice (B). A's FiniteBody, FiniteStatic and FiniteGuard would be three names for one
  judgment class.
  - The merge needs the guard-candidate role. It is already a MIR fact (`MirLocalRole.GuardCandidate`, `MirVocabulary.cs:170`)
    and is read from the `Anchor` fact's Ref.
  - Environment is an interim class (A-2), and Unbound is a state (§3.4), not a kind.
  - `OriginAtomKind` and `MirFactKind` are not closed vocabularies (ARCH §Closed vocabularies), so neither design needs O1.
- **Slots.** Both keep slot decoding inside Binding's view. A's `ArchitectureRulesTest` row ("no file under `Mir/` reads
  `BoundOrigin.Slot` or `.Binder`") is G163's invariant made checkable at no cost. Take it.
- **G161 is A5-F4's, as both say.** Binding completes on `probe/p2`, and the rejection comes from two rows that the old reporter
  judges: `Projection[drop#0] >= Holder.source` and `Projection[drop#HelperSlot] >= Holder2.source` (the dumper). A5's Refuted rule
  over these rows decides it in 2a-1a. A's "helper chain H → whole's region → Reborrow" overstates the shape: Binding's row names H
  directly.
- **Meets.** Binding interns meets by operand identity (`Binding.Meet`'s buckets), but resolution substitutes operands later, so a
  row's meet and a Type's meet can be different objects. Take B's canonical key: resolved, flattened, deduplicated, `static` dropped.

  MeetNode follows from R4 (B8). A splits a mixed meet at a longer end and drops the meet node's own out-edge, which loses
  `b_y → (a and v) → z`: `meet-trans` is UNSOUND under 2x-P4's encoding, which A keeps, and ok with MeetNode. Mixed meets do occur
  on dev at both ends of rows. In `rv/s-demo`, `(x.source & k)` with `k` an Inference atom is the shorter end at `let pair` and the
  longer end at the return. I found no dev reproducer of the loss itself, because dev's call solver mishandles meets in parameter
  Types (incidental finding I-1), so the case stays a prototype one. Take B.

### 3.3 Containment

The rule is the same in both designs: per borrow Deref outward, stop after the first shared layer, an own step is no Deref, stop at
a raw Deref, pair layers by case.

`rv/probe/p1` confirms B's finding: dev accepts `var q = p; return q@follow@follow@uniq`. The Reborrow's Type Origin is `q`'s inner
region (`Inference[var q#-2]`, `== a`). `q`'s outer region (`#-1`, bounded by `b`) is not in the name. The MIR `Ref` carries the
same atom, so the containment edge `#-1 → #-2` gives SPEC's Unknown (`b outlives a`) in both designs. A does not record the
default-pipeline acceptance; B's PLAN row is needed (PD9: a SPEC-invalid acceptance gets a PLAN row).

The step classifier is `MirBorrows.Walk`/`StepClass`, which is private. A names none, which invites a second copy (a symptom). Take
B's 2x-R15, with one correction: `Walk` drops the projection id, and containment needs the prefix whose Type gives the layer's
Origin, so `Step` must carry its prefix. Neither design says which Origin a Slice handle's Index layer contributes (E6): it is the
Slice's `source` slot, not an outer Origin. Add a Slice element Borrow to 2a-1a's identity test.

### 3.4 Fixed–fixed entailment (one owner)

The two designs agree: one catalog (`ExtractOriginPremises`) and one closure (`OriginPremiseClosure`); `UniversalRelations.For(body)`
extracts at the body's root, and `ProvesOriginOutlives` keeps its per-request entry. I checked the equality claim against the
code:
- Extraction walks `Parent` from its start and picks up R4, R2/R3 per enclosing `FunctionKoto`, `FunctionTypeKoto` or
  `PropertyAccessorKoto`, and R1 per enclosing declaration.
- R5 adds only `stored → Projection[local]` edges, and only for Projection nodes already in the graph. A fixed–fixed query from
  the body root puts none there, so R5 adds nothing to it.
- The one start-dependent part is a use inside a `FunctionTypeKoto`, whose input premises hold only there. DIAGNOSTICS states that
  well-formedness inside a nested Function Type stays a definition record, so no body row should start there (H).

This is one form at two lifetimes, not two forms, so no R7 DEC entry is required. Take A's checks: the agreement test over the
corpus's fixed–fixed rows (until R7) and the permanent `ArchitectureRulesTest` row that only `Binding.OriginPremises.cs` adds premise
edges. Also take B's purity note (`originStateVersion` unchanged by a request). G170: both are right that it is not needed, since
the oracle reads no Constraint premise supply.

### 3.5 2a0-3a under OQ11 (b)

- Any record fails its node, Binding is then incomplete, and the old analysis does not run for the whole compilation
  (`OwnershipAnalysis.cs:89`). So rows added at a twin's node are never judged by the old reporter. A's argument holds, and B's
  stated reason for a separate list does not.
- The MIR runs regardless of Binding's completeness (`MirAnalysis.Run`), so A5 judges those rows and the ledger can compare.
- A twin's node is an Opaque in the MIR (`rv/ident-call`: `_7 = opaque 0: Never` at `return r`). Its Loans are therefore absent
  on both pipelines until H11 (`rv/m-write` reports only the relation). B records this as a STATUS/H11 limit. It follows from
  OQ11 (b) and needs no owner question (OQ-B3), only the record.
- The store question (N-3) is open: `AddObligation` feeds `RecordLocalRegionBound`, so A's twin rows enter Binding's
  local-region graph.
- `Unbound` after resolution: a located `Unsupported_Kd` in a complete compilation, and nothing in an incomplete one (A). B's
  "inferred node" would silently pass an end Binding could not resolve.

### 3.6 2a0-3b under OQ12 (a), D4 and A's OQ-F4-1

**dev facts.**
- The S3 demo (`rv/s-demo`) is a located `Unsupported_Kd` at `let pair` on dev (Binding: "local inference does not choose a lower
  bound" for the contravariant `k`). It is not accepted, as 2x-P4's finding 6 assumed (H) and A §12 lists as unverified. Correct
  that record.
- `IncomparableContravariantBoundsRequireAnAnnotation` pins the "no principal candidate" `MissingOriginBinding_Kd`. It is a valid
  form under SPEC 15.3.6 as integrated, and both designs fix it in 2a0-3b.

**A's (a) goes further: every omitted slot of a body local, `let` as `var`, becomes a region.** By SPEC 15.6.5's own model ("a slot
of an unannotated local's Type" is an inferred region) this is the right end state. But it changes Binding's representation for
every `let` borrow local in the library and milestones. Through the old analysis's Loan tracking ("ownership keeps Loans only
through a holder's written Origins", DIAGNOSTICS), it also changes the default pipeline's verdicts for all of them. That is a
shared-representation change across phases, under the D5 freeze, and A states its effects only as (H) with "census first". It
needs its own full-process design with BU, not a widening of 2a0-3b. dev has no D4 reproducer that gives a Language error on a
valid program, since the S3 demo is Unsupported, so the narrow scope costs no soundness now.

Take B's 2a0-3b scope (plan letter, items 1–3) and A's deletions within it:
- `principalOriginFailures` and `Binding.cs:878`'s branch go;
- the unresolved-inference stop goes for body variables;
- the contravariant-clause `Unsupported_Kd` stays, as a valid form's located limit, until the D4 unit.

**Ordering.** B's B12 holds: MirOrigins needs no candidates (2x-P4 finding 7). But 2a-1a's +led needs 2a0-4's split, and the
plan's 2a0-4 waits for 2a0-3b, so §6.7 keeps the plan's order unless BU's hold delays 2a0-3b.

### 3.7 MirOrigins

- The two designs agree on: Tarjan with finite-source bitsets (D1, j-two), fixed-source bitsets with per-target masks for Unknown,
  per-class runs (equivalent keys), no liveness or reachability, inline copies without containment (rows are the template's), the
  cost terms Θ(N_o + E_o·⌈(F+Fin)/64⌉ + Ft·(Pm + ⌈F/64⌉)), and V16 shapes ("F4 Refuted", A3 (9) with k classes).
- Memory: take B's `MirDataflow.Within(long words)`, which `Prepare` also calls, so the limit is decided in one place. A's
  `Admits` "beside `Prepare`" leaves that open.
- Order: take A9 (Judge all classes, then Borrows per view, then Record). Rule 2 needs MirBorrows' err(ended) before an Origin
  problem is added, and with it no problem is removed after it is added.

### 3.8 The merge (2a-2)

- **Rule 1 (destroy half).** A keys rule 1 on the row's `Source = ResultFit`, which Binding publishes, and on a `Result` fact that
  MirBorrows adds when a storage-end conflict's Loan is found only through `_0`. That condition is the one 2x-P4 verified with
  m-store. B matches a store into `_0` against the row's `SourceRef` (H for block, `if` and `match` results); that is a consumer
  correlating two representations by site where the owner can publish the fact. Take A.
- **Per-atom identity.** Both use it: two Borrows of one root share `Projection[x]` (m-twice, same-root). Static atoms are per site
  (A-1's dump), which the rule needs no change for.
- **Rule 2 (guard half).** Take A's conditional GuardReverse (B-1). The S3 `let m = 8` finding is 2c-6's in both designs, and I
  agree.

### 3.9 Reason in the key (2a-3 folded)

Both fold 2a-3 into 2a-1a; I agree (j-two, census (c)).
- **Representation.** B is lighter: `MirProblem` gains the key part and the facts hold a row index; the adapter reads Kind, Clause
  and Destination from the view at publication. A copies the row into a pooled `MirRelation` list. Take B's.
- **Equality.** Add A's Equality bit. A failed `==` in both directions is one problem, as Binding's record is. B's
  `(Longer, Shorter)` key would give two problems, one per direction, against one old record.
- **Facts.** Use the existing `Anchor` kind, a `Relation` row index (−1 for a containment edge, with the Ref's site), and `Result`.
  B's `GuardSource` is unnecessary once the guard role is read from the Anchor's Ref.

### 3.10 Units and records

- Both designs' units are close to the plan's.
- Differences resolved in §6.7: B's 2x-R15, B's decoupling of 2a0-3b, and A's prerequisite of 2a0-4 for 2a-1a. B's 2a-1a lists
  +led without 2a0-4, but without the split, Binding's fixed–fixed records are compared against A5 as LOST rows.
- PD21: `Binding.LocalRegions.cs` is at 3 on dev, so a 2a0-3b edit there needs its response first. Otherwise recount at each unit
  start, as both say.
- The 85 rows and the eight signatures are mapped alike by both designs and stay (H) until 2x-T2 re-triages them.

## 4. Probes on the real compiler (dev `ecf62815`)

"Default" means both pipelines' printed verdict. Under "A" and "B" is what each design's A5 would do on the MIR under its text;
(n) marks no report.

| Probe (`rv/…`) | Boundary | SPEC | Default | A | B |
| --- | --- | --- | --- | --- | --- |
| `probe/p1` f: `var q = p; return q@follow@follow@uniq` | containment through a local layer | P (b ⊒ a not proven) | accepted | P (containment edge `q#-1 → q#-2`) | P (same) |
| `probe/p1` g: the direct twin | same | P | `UnprovenOriginRelation_Kd` (Binding, fixed–fixed) | (n), Binding's | (n), Binding's |
| `probe/p2` G161 and helper | drop receiver | S, S | S, S (rows) + `Unsupported_Kd` at `self@ref` (old) | S, S | S, S |
| `guard-annot` | guard, non-escaping relation | S | S at `v` | S | **(n): accepted** (B-1) |
| `guard-annot-ok` | valid twin | valid | accepted | ok | ok |
| `env-store` | closure environment through a call | P (borrowed receiver) | S (row at `x@ref`) | **(n): accepted** (A-2) | S |
| `env-borrow` | closure result over environment | valid | `Unsupported_Kd` at `f()` | no row before 2c-6 | no row before 2c-6 |
| `st-capture2` | captured static Borrow returned | valid | `Unsupported_Kd` at `count@ref` | **S after 2c-6** (A-1) | depends on order (BX-2) |
| `st-own2` | closure's own static Borrow returned | S | S | S | S |
| `st-leak` (15.6.4 `current`, `leak`) | static through result, parameter | S, S | S, S | S, S | S, S |
| `dflt` g, h (default `n@ref`) | default template | S (and A5-F9's R26) | S, S (Binding records) | **P**: `n` classed Fixed (A-1) | S in template, twins |
| `dflt2` `y: ref/i32 = x` | default, fixed–fixed | P | P (Binding) | Binding's | Binding's |
| `ident-call` `let r = choose(a, y: local@ref); return r` | relation through a call | S | S (record; MIR `return` Opaque) | twin S | twin S |
| `m-write` | merge | S + write conflict | S only | S only (until H11) | S only (until H11) |
| `forloop` | synthesized Use | valid | accepted | NoBody frame (BX-1) | `InternalInvariant_Kd` (BX-1) |
| `s-demo` | S3/D4 | valid | `Unsupported_Kd` | accepted under (a) | `Unsupported_Kd` (located) |
| `meet-trans-ok` | meet of call regions | valid | P + S + S (I-1) | Binding's records | Binding's records |

Prototype re-runs:
- A: 69 cases, 0 UNSOUND, `s3-g-meet` interim; reviewer's `rv-g-annot-esc` is LOST under A and under B, ok under PD11, and `g-two`
  is LOST under PD11 (N-6).
- B: 71 cases; `meet-trans` and `st-capture-fixed` are the two demonstrations; `rv-st-sites` is ok everywhere.
- No reference disagreement on 3,000 random graphs in either run.

## 5. Symptom fixes, shared structure, deletions, vocabulary

1. **A: `NoBody`** is a slice for rows whose body is unknown, a fallback that hides a missing fact. Replace it by BX-1's partition.
   **B: the OQ-B5 conflict** comes from an unordered table; the order resolves it.
2. **The class table** (one function in `Binding.OriginRows.cs`; a new form adds a row). First match decides:
   1. `Static`, `Parameter`, `Input` → Fixed.
   2. `Intersection` → by canonical operands: all Fixed → one Fixed node, otherwise a meet node (B8).
   3. `Projection` whose binder is a root of the body (a local, parameter, temporary, a guard candidate, the destruction receiver's
      parts and its helper borrow, a default template's prepared inputs) → Finite, or Inferred when it is reached through a
      referent.
   4. Any other atom whose binder lies outside the body (captures, enclosing regions and Borrows, an imported static Borrow) → Fixed.
   5. `Projection` of mutable static storage formed in the body → Finite; of immutable static storage → Inferred.
   6. A Closure's environment binding → Finite until 2c-6 (A-2).
   7. `ClosureValue` / `CallResult` roles → as A §3.1.
   8. `Inference`, `Anchor` → Inferred.
   9. `Unbound` after resolution → no edge; a located `Unsupported_Kd` in a complete compilation.
3. **Shared structure.** Each option has its single owner:
   - the row producer and its partition (Binding);
   - the classifier of Place steps (A5, `MirSteps`);
   - the merge rules (`MirReport.Add`);
   - the premise catalog (Binding).

   The partition fix is a change of shared structure inside the owner, and the in-owner alternative (a parent walk with a special
   case for synthesized nodes) is a symptom fix. Prefer stamping at formation.
4. **Deletions.**
   - 2a0-3b: `principalOriginFailures`, `Binding.cs:878`'s branch, and the unresolved-inference stop for body variables.
   - At R6-check (AR-4): the old reporter (`A/OwnershipAnalysis.Borrows.cs:55-161`), the Unrepresentable gate, the `CallBorrows`
     skip, the twins and their label branch, and `OriginFits.cs:85`'s gate.
   - With OQ-1 (b) below: the skip (−55).
5. **Vocabulary.** No closed vocabulary grows. New canonical components are `MirOrigins` and `MirSteps`, plus the C1-F4 view's
   ARCH row; these are the planning session's decisions, recorded in DEC, with the package's owner approval (OQ2). No O1 question.

## 6. Recommended combination

Use A as the base text and apply these replacements. Section numbers are A's.

**6.1 §2.2 "Body key" and the `NoBody` bullet**, replace with:
"Each row belongs to the executable body Binding is binding when it forms the row: the owner of the forming scope (function,
accessor, Closure, a default template by parameter index, static initializer), recorded at formation, never derived from
`Use.Parent` (synthesized Uses such as `BoundIteration`'s `$for.iterator` have no parent). A row with no such owner is
`InternalInvariant_Kd` in the producer's validation. Test: the library's `Slice.kimi` `for` row and a user `for` over `Slice<i32>`
and `ref/Array<i32>` lie in their functions' slices; no row is outside a body."

In the tuple bullet, add: "`ResultFit` is set by Binding where it validates a body result (a return value or tail fit, including
`CheckLocalTypeUse` at that fit); the view does not infer it from syntax."

**6.2 §3.1**, replace the table with §5 item 2 and the kinds enum with `OriginAtomKind : byte { Fixed, Finite, Inferred }`. Keep
`ProjectionRole` and the `ArchitectureRulesTest` slot row. Replace "Environment … MirOrigins reads it as Inferred until 2c-6" with:
"Environment: Finite, as Binding judges it today, until 2c-6 classes it per receiver case (Inferred and contained in the receiver
Origin through a borrowed receiver, Finite in a Consuming call)."

Add to §3.3: "G161's helper row names `Projection[drop#HelperSlot]` directly (dev's row at `whole.buffer@ref`)." Add an ask to 2b-6
(BU): "a static-storage Borrow keeps a per-site binder."

**6.3 §6.3**, add after the row bullet:
"A meet is keyed by its canonical operand set (resolved, flattened, deduplicated, `static` dropped). A meet at a longer end adds an
edge per operand **and**, for a non-fixed meet, the meet node's own edge (SPEC 15.3.6; prototype `meet-trans`)."

Replace the containment bullet's classifier with: "Steps come from A5's one classifier (`MirSteps`, 2x-R15), whose `Step` carries
its prefix. A Slice handle's Index contributes the Slice's `source` slot (E6). A PreparedInput Deref is Inline (E17)."

**6.4 §6.6**, replace the first bullet with: "`MirProblem` gains `Reason (Longer, Shorter, Equality)` for Origin problems (default
otherwise); `MirReport`'s key is `(code, subject, body, Reason)`; `MirFact` gains an `int` used only by `Relation` (the row's index
in the body's view, −1 for a containment edge). A failed `==` row is one problem." Delete `MirRelation`.

**6.5 §5.2**, replace "Change (owner question OQ-F4-1, recommended (a))" through "Fallback (b)" with:

"Change (plan letter). An inference variable of a body that reaches no candidate resolves to an open region at `BodyOrigins`.
Outside a body it is `MissingOriginBinding_Kd`. The 'no principal candidate' record becomes the clause's relation judgment, a row,
with Binding's twin under OQ11 (b). The contravariant-clause `Unsupported_Kd` and `let` candidates stay. The S3 demo is a located
`Unsupported_Kd` on dev, not accepted. A `let` slot as a region (D4) is OQ-1, outside this unit."

In 2a0-3b's row, remove s-demo and the D4 form from the accepted list. Size back to +40/−30.

**6.6 §5.1**, add: "Consequence (record in STATUS and H11's list): a twin's node is Opaque in the MIR (`HirOp.Error`), so its Loans
are absent on both pipelines until H11 (`let r = x@ref; x = 1; return r` reports only the relation)." Add to H11/AR-4: "the
non-call fit producer then adds body-chain rows verdict-independently."

**6.7 §9 units**, applied to A's table:
- Insert 2x-R15 (B's row, plus the prefix in `Step`) after 2e-2 and before 2a-1a.
- 2a-1a prerequisites: 2a0-2, 2a0-3a, 2a0-4, 2x-R5, 2x-R15, G:A5-F9 merged. 2a0-3b is not a prerequisite of 2a-1a or 2a-1b.
- 2a0-4 follows 2a0-3a. Its `UnprovenConstraint` row moves with 2a0-3b if the planning session reorders; otherwise keep the plan's
  2a0-3a → 2a0-3b → 2a0-4 order.
- Add to 2a-1a's tests: env-store (S, failing before), guard-annot (S kept), `rv-g-annot-esc` (LOST pinned as the recorded limit),
  BX-1's `for` rows, p1's local-layer form (P at the Reborrow; a pinned (a) row), a Slice element Borrow in the identity test, and
  st-capture2's class row in 2a0-2's view test (Fixed in the closure).
- 2a-2: A's rules 1 and 2. `MirFactKind.Result`.

**6.8 Records.**
- PLAN rows (new G numbers):
  - p1's default-pipeline acceptance (B §10 (1));
  - the skip (per OQ-2);
  - D4 (per OQ-1);
  - I-1, a Binding defect for BU: a callee parameter Type with a meet of its own Origins is judged at the call with the callee's
    Parameter atoms, not fresh regions (`rv/meet-trans-ok`, SPEC-valid, rejected with P and two S);
  - the A3 note that S3's demo is Unsupported on dev.
- STATUS: the twin limit.
- DEC (2a0-2): the partition at formation, and the class order.

## 7. Owner questions (one batch)

Most of the designs' owner questions dissolve or merge. OQ-B1 (T1 holds by construction), OQ-B3 (a consequence of OQ11 (b),
recorded) and OQ-B5 (false premise; BX-2 and the ask to 2b-6) need no answer. OQ-F4-3 becomes OQ-3.

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-1 (A's OQ-F4-1, B's OQ-B4) | SPEC 15.6.5 makes an unannotated local's slot an inferred region and 15.4.4 lets later uses constrain it, while Binding gives a `let` slot a declaration-time candidate (2x-P4 D4). dev has no reproducer in which this rejects a valid program with a Language error: the S3 demo is a located `Unsupported_Kd` | (a) every body-local omitted slot a region now, in 2a0-3b (A); (b) a SPEC clarification that the declaration fixes the candidate; (c) a separate BU unit with its own full-process design and census of `let` borrow locals in library and milestones, after G:A5-F4, with a PLAN row meanwhile | **(c)**: a cross-phase representation change under D5 needs its own design; 2a0-3b keeps the plan's letter |
| OQ-2 (A's OQ-F4-2, B's OQ-B2, census Q5) | Binding's preserves-results containment skip accepts 18 tested forms SPEC 15.6.3 makes Unknown (library and milestones 0); A5 rejects them from 2a-1a | (a) delete at R6-check with the old path (AR-4), 2a-4 re-expecting the 18 under OldRule now; (b) delete now in a Binding unit (BU review, OQ7 (a)); (c) a SPEC proposal exempting `preserves results`; (d) hold | **(a)**: no default-pipeline change during the freeze, and SPEC's letter on the MIR; (b) if the owner wants both pipelines aligned before R6-check |
| OQ-3 (A's OQ-F4-3, extended) | Confirm the departures from owner-approved text as one list | (a) confirm all: D1 bitsets for A3 §7's one-witness BFS; conditional GuardReverse for PD11's second half and A3 E7's merge sentence; E4 judged against Binding's encoding, with MeetNode; `OriginAtom.Of`'s three SPEC kinds for the plan's four; `UniversalRelations.For(definition)` with per-class conditions; T1 by construction; 2a-3 folded into 2a-1a; rows partitioned at formation; (b) per item | **(a)** |

## 8. Non-blocking findings

- **N-1.** A's OQ-F4-1 (a) scope (§3.6).
- **N-2.** Verdict-dependent rows at non-call fits; the H11 end state (§3.1).
- **N-3.** Twin rows' store. A's `CheckTypeUse` appends to `Binding.Obligations`, and `AddObligation` feeds `RecordLocalRegionBound`
  for Projection, meet, Anchor and region targets. A failed fit's edge can then add region bounds, and so possibly new Binding
  records, on the default pipeline (SPEC 15.6.1: a failed relation adds no constraint).

  Failing non-call fits mostly have fixed shorter ends (results, parameters), so I expect none (H). Make 2a0-3a's snap condition
  explicit: "records unchanged except the requirement label". If it shows a new record, the implementer stops and the planning
  session moves the twin rows into the view's own input list (B's store, for SPEC 15.6.1's reason).
- **N-4.** B's 2a0-3a omits `FailedDeclaredRelation` (B-2's second half).
- **N-5.** A's §6.7 sentence about environment atoms is false on dev (A-2's probe).
- **N-6.** GuardReverse loses an independent non-escaping Refuted relation when the same candidate also escapes (`rv-g-annot-esc`:
  LOST under A and B, ok under PD11). The program stays rejected (R8 completeness only). Record it as a limit with a pinned test.
  A flow fact (2x-P4's option (b)) would fix it later.
- **N-7.** I-1 (incidental, BU): meets in callee parameter Types are judged with the callee's own atoms.
- **N-8.** Prototype classification is hand-assigned in both designs. The atom-identity test per builder Ref site (B's list,
  `MirBuilder` Ref sites) is the stronger form; take B's list for 2a-1a.

## 9. What stays unverified

- No A5-F4 code exists, so every A5 verdict in §4 is derived from the rows, atoms and MIR Types the dumper printed plus each design's
  rules, not executed. The census numbers (85 rows, 41 + 10 records, 742 CallBorrows) are 2x-P4's base, not re-measured.
- That pending keys are unique per declaration (alias resolution equal at every use in a body).
- That no body row starts inside a `FunctionTypeKoto` (the premise-scope equality of §3.4).
- N-3's expectation that no new default-pipeline record appears.
- D4's census over library and milestone `let` borrow locals (OQ-1 (c)'s first step).
- The cost terms on real MIR (+cold and +warm for the view; V16 constants); the memory-limit test's constructibility.
- `ResultFit` coverage for block, `if` and `match` results (both designs' merge tests must include them).
- The 2c-6 interactions: the environment classes per receiver case, the closure result formal (`s3-g`), and st-capture2's verdict
  once the result row exists.


---

# 2a0-D confirmation: A5-F4 package (`package.md`, 530 lines)

Independent confirmer of the consolidated 2a0-D package (R4-M74 OQ2). I wrote neither design, the review nor the package. I read
the package, `review.md`, both designs, `brief.md`, `../s2-design-common.md`, AGENTS, DECISIONS R4-M74, R4-M88–R4-M90 and BU-18,
the step 2 plan (PD8–PD11, OQ11, the 2a0/2a rows, the §5.1 A5-F4 row), PLAN (G56, G100, G161, G170, G189), the 2b-D and 2e-D
packages, and the confirmations in `2b-D-review.md` §7 and `2e-D-review.md` §8. I also read SPEC 4.8.1, 11.8, 14.8.3, 15.3.5,
15.3.6, 15.4.4, 15.6 (intro), 15.6.1–15.6.5, 15.8.2, 16.3.1 and 23.1.2.

**Evidence.**
- dev `0eac4690` was built in `bin\worktrees\cf-2a0d` (toolchain junction, restore, Release) and removed after, junction first.
- During the check, dev moved to `6b436f5a`: bu14 (2b-6 (a), BU-18) and bu16 (2x-R14). Neither changes Origin naming
  (`PlaceAuthority`, `ExplicitAdaptations` and the Contract tables only). Nothing in the repository was edited.
- The review's dumper was extended to print each row's ends resolved by `OriginAtUse` (called by reflection), Binding's own
  verdict (`JudgeOriginObligation`) and the enclosing function. Its output is filtered to the user module.
- The probes are in `cf/p/<name>/` (`Main.kimi`, `Main.kimi.out.txt`). `cf/run.sh` prints the default pipeline's verdict, and
  `cf/probe.sh` prints the rows and MIR problems.

**Verdict: not yet ACCEPT.** The package carries the review's recommendation faithfully, except for one dropped item (P-2). The
real compiler shows five blocking items. Each one has an exact text fix, and none needs a redesign.

## 1. What the package carries over

| Review item (§6) | Package | Status |
| --- | --- | --- |
| A's base; A-1 class precedence | §3, rows 3 → 6 → 7 | Carried |
| A-2 environment interim | Row 4 (Finite until 2c-6), D19 | Carried |
| B-1 guard half | D22, §7 rule 2 (conditional GuardReverse) | Carried |
| B-2 rows from positions and declared clauses | D9, §5.1 (2) and (3) | Carried |
| BX-1 partition at formation | D3, §2 | **The accessor is dropped** (P-2) |
| BX-2 stated precedence | §3 table | Carried, but rows 1 and 6 break on dev's input (P-5) |
| B's three kinds, MeetNode, canonical meets | D6, D15, D4 | Carried |
| 2x-R15 with `Step` carrying its prefix; the Slice `source` layer | D12, D14 | Carried |
| A's driver order, merge rule 1, conditional GuardReverse | D13, D21, D22 | Carried |
| B's Reason with A's Equality bit; `MirDataflow.Within` | D18, D20 | Carried |
| 2a0-3b at the plan's letter; the S3 demo record | D10, §5.2 | Carried |
| Records (p1, skip, D4, I-1, G56; the twin limit; N-3; N-6) | §9, §5.1, §7 | Carried. The twin limit's text is wrong (P-1) and I-1's mechanism is misstated (P-5) |

The four consolidation changes:

- **Unknown-coded tests moved to 2a-1b.** Correct: 2a-1a has no Unknown, so P cannot be tested there. 2a-1a keeps the containment
  edges as structure.
- **ClosureValue row tightened.** Correct. A's "else Finite" would class an enclosing closure's value atom Finite inside a nested
  closure, where SPEC 15.6.5 makes a capture's Origin fixed. Row 5 → row 6 gives Fixed.
- **85 vs 87 rows.** The observation is correct: the plan's per-code list sums to 87. Design A's own split (Unproven 67) sums
  to 85. Deferring the split to 2x-T2 is fine.
- **`IncomparableContravariantBoundsRequireAnAnnotation` marked (H).** Acceptable. Once the principal-candidate branch goes,
  whether the contravariant-clause `Unsupported_Kd` covers this program is a code-path question (`JudgeLocalClause`, `OriginProof.cs:470`).
  Either outcome is a valid-form verdict, and the same unit re-expects the test with its reason.

Silent changes beyond these four:

- §2 lost "accessor" from the review's owner list (P-2).
- Rows 4 and 5 moved ahead of "binder outside B". That changes no class, because both test whether the binder is B.
- §8's BU ask still targets "2b-6, G100's qualified part", which landed as bu14 during the check (P-3).

## 2. Probes on dev (verdicts as printed; MIR problems unpublished, R4-M1)

| Probe (`cf/p/…`) | What it tries to break | dev today | A5 under the package | SPEC |
| --- | --- | --- | --- | --- |
| `m-write` (`let r = x@ref; x = 2; return r`) | the twin's Opaque limit | Default: S. MIR: `ComparisonLoanConflict_Kd` at `x = 2`, because the Opaque site at `return r` names `r`'s root | S and the write conflict | S and the write (PD11's own example) → **P-1** |
| `m-write4` (`slot@follow = x@ref; x = 2`) | a Borrow inside the twin node | Default: S. MIR: nothing (the Borrow is never built) | S only | S and the write: this is the real limit |
| `accessor2`, `accessor-for` | the partition: rows in a getter, including a `for` | Rows exist; their scope's `Function` is null (`Binding.cs:1171`) | No owner, so `InternalInvariant_Kd` | S / valid → **P-2** |
| `st-leak`, `st-imm` | rows 7 and 7' (15.6.4's `current`/`leak`; 11.8's `limitRef`/`nameRef`, unqualified in-group) | Binding records S on all four. `limitRef` is SPEC-valid. After 2b-6 (b) all four are Binding's located `Unsupported_Kd` | The tests expect S/valid, which cannot hold after 2b-6 (b) | S, S, valid, S → **P-3** |
| `handle2` `leak3` (`values@ref` of a `Slice<i32> during s` parameter, returned) | the atom of a Borrow anchored in a root | **Accepted on both pipelines.** The Borrow's Type is `ref/… during Parameter[s]` | Accepted: `Parameter[s] → Parameter[s]`, and there is no Deref, so no containment | S (4.8.1: "borrows the handle"; 15.6.5) → **P-4** |
| `handle2` `leak4`, `handle3` `var` form | the same through a local | Default: accepted. MIR: storage-end conflict | Conflict kept (its atom is not refuted) | S |
| `mto` (`rv/meet-trans-ok`) | an inference end resolved to a parameter (a callee's) | P + S + S. The rows name `put`'s `Parameter` atoms | Row 1 classes them Fixed, so A5 itself reports S and P | valid → **P-5** |
| `for-closure`, `forloop` | the parentless `$for.iterator` Use | Rows; owner by scope | Correct under formation | — |
| `env-store` | a row whose Use is the Closure literal (`var f`'s fit) | Rows relating `g`'s atoms to `var f`'s region (formed while binding `var f` in `g`, a reading) | Correct only by forming scope, not by the Use's own scope | (P-2's test) |
| `st-init`, `call-rows`, `dflt` | rows in a static initializer, a default copy, and at an omitting call | None formed. A default's rows form in the function's own scope (`Binding.Functions.cs:70-84`) | — | (P-2's default clause) |
| `jregion`, `jregion2` | rule 1: a value returned only, and returned and also stored | Default: S and S,S. MIR: one storage-end conflict each | S (conflict dropped); S, S, C (conflict kept, a non-`_0` holder) | S; S, S (+C admitted, 23.1.2) |
| `guard-annot` | row 3, guard candidate | A row; the old reporter gives S | S | S |

Rows 2 and 2' (mixed meets: `call-rows`' `(Input[a] & Projection[local])` at a return), row 3 (default prepared inputs, the
destruction receiver), row 4 (`env-store`), row 8 and Reason (`==`, two chains) gave no SPEC contradiction beyond P-5 and the
non-blocking points below.

## 3. Blocking items

**P-1. The twin's Opaque limit is misstated, and it contradicts PD11** (§5.1 "Recorded limit"; 2a-2's test list).

- An Opaque site names the roots its syntax reads (`MirBuilder.Opaque.cs`).
- So a value the twin node reads keeps its Loans. On dev's MIR, `rv/m-write` already reports the write conflict at `x = 2`.
- PD11 and the plan's 2a-2 row require exactly this: "`let r = x@ref; x = 1; return r` reports the write conflict and the
  relation".
- The package changed that test to a `var r` form and records the opposite in STATUS.
- What is actually lost is a Borrow formed *inside* the failed node.

Fix:
- §5.1, replace the "Recorded limit" bullet with: "**Recorded limit (STATUS and H11's list).** A twin's node is `HirOp.Error`,
  an Opaque in the MIR (`rv/ident-call`). Its site names the roots it reads, so values it reads keep their Loans (on the MIR,
  `let r = x@ref; x = 1; return r` reports the relation and the write, PD11; the default pipeline reports only the relation, since
  the old analysis does not run). Borrows formed inside the failed node are not built.
  Their Loans are absent on both pipelines until H11: `slot@follow = x@ref; x = 2` reports only the relation, while SPEC 23.1.2
  also wants the write conflict (`cf/p/m-write4`)."
- 2a-2 tests, replace "m-write's row form (`var r`; write kept)" with "m-write (the plan's form: the relation and the write
  conflict), its `var r` form, and m-write4 (the relation only; the limit, pinned)".

**P-2. The partition omits accessor bodies, so a valid program gets an internal error** (§2 "Partition at formation", D3).

- The review's BX-1 text listed "function, accessor, Closure, default template, static initializer". The package names "the
  forming scope's `BindingScope.Function`".
- That property is a `FunctionKoto`, and it is null in a Property accessor (`GetScope`, `Binding.cs:1171`). It is also null in a
  `for` plan scope inside an accessor, which copies it (`Binding.Sequences.cs:320`).
- `cf/p/accessor` (valid) and `accessor2` form rows in a getter, which the MIR builds as `getter first.Get`. By the package's
  text they have no owner, so the producer's validation raises `InternalInvariant_Kd` on a valid program.
- A default is likewise bound with the function's own scope (`Binding.Functions.cs:70-84`).

Fix:
- §2, first two Partition bullets, replace with: "Each row is owned by the executable body Binding binds its Use in, recorded when
  the row is formed: the innermost scope on the forming scope's `Parent` chain whose owner is a function or a Closure
  (`BindingScope.Function`) or a Property accessor (where `Function` is null). A default template is identified by the parameter
  index Binding is binding (its default is bound with the function's own scope, `Binding.Functions.cs:70-84`, so the index comes
  from Binding's state there). A static initializer is identified by the declaration being bound (its scope's `Function` is null
  too). The owner is never derived from the Use's own scope or `Use.Parent`: a Closure literal's fit at its declaration
  (`rv/env-store`'s `var f`) belongs to the enclosing body."
- 2a0-2 tests, add: "the rows of a getter (`accessor2`) and of a `for` inside it (`accessor-for`) in the accessor's slice; a row
  whose Use is a Closure literal in the enclosing body's slice".

**P-3. The static rows and tests ignore 2b-6 (b)** (§1's S1015 list, §3 rows 7/7' and "Why the order matters", the 2a0-2 and
2a-1a tests, §8).

- The 2b-D package §1.5 (R4-M89) and R4-M90 PQ-1 apply: after 2b-6, no explicit static Borrow reaches A5. That includes the
  unqualified in-group form, which BU confirmed on 2026-10-21. Binding reports a located `Unsupported_Kd`, and the MIR builds an
  Opaque `{Φ_E}` until P42.
- 2b-6 (b) lands after G:A5-F9's merge, so in all likelihood before 2a-1a.
- These forms then stop binding: 15.6.4's `current`, `leak` and capture store, 11.8's `nameRef` and `limitRef`, and
  `rv/st-both`/`st-capture2`.
- The package's own text also reads "nameRef and limitRef valid". SPEC 11.8 makes `nameRef` an error.
- dev today rejects SPEC 11.8's valid `limitRef` (`return limit@ref`, `let` static, `during static`) with S (`cf/p/st-imm`). This
  is an unrecorded Language error on a valid form.

Fix:
- §3, after "Why the order matters", add: "**Static Borrows and 2b-6.** After 2b-6 (b) (R4-M90 PQ-1; 2b-D §1.5), every explicit
  static Borrow is Binding's located `Unsupported_Kd` until P42. Rows 7 and 7' then class only implicit static Borrows that reach
  a row (a `for` or `match` Subject borrow, a comparison inspection, a writeback reservation, an argument adaptation). The
  explicit forms and the row-6/row-7 argument above are dormant until P42."
- §1, read "§15.6.4 (item 1)" as "§15.6.4 (item 1) by its class rows; its MIR tests wait for P42 once 2b-6 (b) has landed (X-2,
  OQ9)".
- 2a0-2's class-row tests, append: "if 2b-6 (b) has landed at the unit's start, through an implicit static Borrow; otherwise as
  written, and 2b-6 (b) re-expects them as its U".
- 2a-1a's tests, replace "15.6.4's `current`, `leak` and a capture store; nameRef and limitRef valid" with "15.6.4's `current`,
  `leak` and a capture store, SPEC 11.8's `nameRef` (S) and `limitRef` (valid). If 2b-6 (b) has landed at 2a-1a's start, each is
  2b-6's located `Unsupported_Kd` naming P42, pinned."
- §8, replace the BU row "(2b-6, G100's qualified part)" with "BU (2b-6 (b); P42's static-Borrow unit) | 2b-6 (b) re-expects A5-F4's
  static tests as its U. When static Borrows bind again, the binder stays per Borrow site, never the Field declaration (§3's
  validation)".
- §8's 2c-D row, add "after P42" to st-capture2.
- §9 PLAN, add: "dev rejects SPEC 11.8's `limitRef` in-group form with S (Binding's record); it becomes 2b-6 (b)'s U, and P42 must
  give the valid verdict".

**P-4. An unsound acceptance that A5-F4 would keep, unrecorded** (PD9; §3 identity; §9).

- `PlaceOrigin` names a Borrow of a `Slice` handle root by the handle's own Origin
  (`ElementAccess.AccessType(source).Origin`, `Binding.ArgumentOperations.cs:430-432`), not by the root's atom.
- `leak3` returns a reference to the callee's own parameter storage. Both pipelines accept it:
  `func leak3(values: Slice<i32> during s) -> ref/(Slice<i32> during s) during s` with body `return values@ref`.
- The MIR Ref has no Deref, so no containment edge forms. Its node is `Parameter[s]`, so A5-F4 accepts it too.
- `leak4` (through a `let` local) and the `var … @uniq` form are accepted on the default pipeline. The MIR's storage-end conflict
  rejects them, and rule 1 keeps that conflict.
- UniqSlice, struct and tuple-Field forms are already S (`handle3`).
- PD9 requires a repair unit or a PLAN row for this. The census reported "refuted acceptances: none".

Fix:
- §3 Identity, add: "A Ref whose Place reaches no Deref is a Borrow anchored in its root (SPEC 15.6.5). Its Origin must be the
  root's finite atom. dev's `PlaceOrigin` names such a Borrow of a `Slice` handle by the handle's `source`, so A5 inherits `leak3`'s
  acceptance until Binding's repair (§9)."
- §9 PLAN, add item 6: "**Slice handle Borrows.** `PlaceOrigin` names `values@ref` of a `Slice` local or parameter by the handle's
  `source`. `leak3` is accepted on both pipelines and by A5. `leak4` and the `var … @uniq` form are accepted on the default
  pipeline, while the MIR's storage-end conflict rejects them (SPEC 4.8.1, 15.6.5; a miscompile under PD9). Owner: BU, a repair
  unit naming the Borrow by the root's Projection, with OQ7 (a)'s census, before R6-check. Written at approval."
- §8, add the BU ask.
- 2a-1a's identity test, add `leak3` (pinned: no MIR problem, naming the PLAN row) and `leak4` (the storage-end conflict).
- §12, add the item.
- If the planning session prefers A5 to catch this itself (an anchor edge from the root's atom to the Ref's Origin, "layer 0" of
  E4), that is a design change, and the full process applies.

**P-5. Rows 1 and 6 class another declaration's atoms Fixed, so A5 reports a Language error on a valid program** (§3 rows 1, 6
and 9; §8's I-1 sentence).

- In `rv/meet-trans-ok` the call's regions are `put`'s own `Parameter` atoms. They appear in `mt`'s *rows*, not in fixed–fixed
  Binding records: `Projection[y] >= (put.a & put.b)` and `Inference[var t] == (put.a & put.b)` (`cf/p/mto`).
- Row 1 ("`Parameter`, `Input` → Fixed") therefore makes A5 itself report S and P on a SPEC-valid program.
- SPEC 15.6.4 step 1 makes these fresh regions, and 15.6.5 makes fixed only "the body's contract".
- AGENTS forbids a conservative Language error on a valid form.
- §8's "A5 inherits it through Binding's fixed–fixed records" is false.

Fix:
- Row 1, replace with: "`Static`; a `Parameter` or `Input` of B's contract (B's or an enclosing declaration's signature Origin or
  Type slot, a pair binder's `o`) | Fixed".
- Row 6, replace "whose binder lies outside B" with "whose binder lies in a body enclosing B".
- Row 9, append: "A `Parameter`, `Input` or `Projection` of any other declaration (a callee's Origin Binding did not instantiate,
  I-1) gets the same treatment: no class, and a located `Unsupported_Kd` at the row in a complete compilation."
- §8 BU row, replace the last sentence with: "A SPEC-valid program is rejected with P plus two S on the default pipeline. Its rows
  name `put`'s atoms, so A5 gives the located `Unsupported_Kd` (row 9) until BU's fix."
- 2a-1a, add test: "`rv/meet-trans-ok`: the located `Unsupported_Kd`, no S or P; the ledger row (a) with I-1's PLAN row".
- 2a0-2, add: "+cen counts rows with another declaration's universal atom. If any shape other than I-1's appears, the unit stops
  and reports."

## 4. Units (item 3)

**Consistent:**
- 2x-R15 sits on lane C after G:A5-F9's merge, so after 2e-2's E17 row. The unit number is free on dev.
- 2a-1a requires G:A5-F9 merged, 2a0-2, 2a0-3a, 2a0-4, 2x-R5 and 2x-R15. 2a0-4 reaches 2a0-3b unless §5.3's reorder is used.
- The Q6 forms match the approval paragraph.
- PD21 for `.OriginProof.cs` is right: 2a0-2, 2a0-3a and 2a0-3b each edit it (BodyChain, `FailedDeclaredRelation:428`,
  `JudgeLocalClause:470`), so the file is due after 2a0-3b.

**Notes (non-blocking):**

| # | Note | Fix |
| --- | --- | --- |
| U-1 | 2a-2's prerequisite reads "the `Reports.cs` response (2x-R2)", but 2x-R2 has landed (PLAN, lane C's head) | Read "a PD21 response for `MirBorrows.Reports.cs` if due at 2a-2's start (2e-D's response precedes 2e-4)" |
| U-2 | 2a0-4 lists 2x-T3, which was closed as not added (R4-M80) | Drop it |
| U-3 | PD21 omits `Binding.cs`, which is over the limit; 2a0-3b deletes the principal-candidate report branch (`Binding.cs:846` at `0eac4690`; the package cites `:878`) | Add `Binding.cs` (deletion only, a ruling if due); cite branches by name, not line |
| U-4 | 2a0-2 edits `.OriginProof.cs`, `.CallRelations.cs` and `OriginPremiseClosure.cs`, yet its Hold column is "no", while §8 lists BU's holds only for 2a0-3a/3b | State whether BU's hold covers 2a0-2 |
| U-5 | "Each new test fails before its unit" is false for valid counterparts (limitRef, a-split, a-wf, d-g161-ok, CallBorrows Proven) and pins (the S3 demo, `leak3`, N-6) | Read "each new rejecting test fails before; valid counterparts and pins pass before" |
| U-6 | 2a-2's done condition ("the a5-new storage-end rows go") cannot hold for a value both returned and stored (`jregion2`: the conflict is kept by rule 1's own condition) | Add "except a conflict another live holder or storage the body does not own also takes (m-store)" |

## 5. Owner questions (item 4)

| ID | Fair? | Change |
| --- | --- | --- |
| OQ-1 | Mostly | (b) should state its cost: it narrows SPEC 15.4.4's "later uses constrain them", which AGENTS forbids doing to fit the implementation. It should also state what it leaves (the S3 demo's status). The recommendation's "no dev reproducer gives a Language error" should read "none found (H)" |
| OQ-2 | Mostly | (d) "hold" should say what it holds and what it leaves. (a) should say that the 18 Unknown acceptances stay on the default pipeline until R6-check (not miscompiles, census) |
| OQ-3 | Incomplete | Add (9): PD11's first half, "whose Borrow the Origin stratum refuted", now read per atom with the `Result` fact and a `ResultFit` row. Add (10): 2a0-3a adds non-call fits' rows only where Binding records a failure until H11, whereas the plan's 2a0-3a row says "`JudgeCallPosition`'s pattern … every inner chain has a row". Both depart from owner-approved plan text |
| N-6 | — | It loses an independent failure, against SPEC 23.1.2's "each independent failed requirement is reported" (the program stays rejected). As a reporting limit shipped by design, list it for the owner (OQ-3 (11) or the X-2/OQ9 list), with a ledger class for its LOST row |

No new owner question is needed for P-1 to P-5, which are text fixes. P-4's Binding repair is BU's unit under OQ7 (a).

## 6. Non-blocking points

| # | Point |
| --- | --- |
| N-1 | Row 3's "temporary" must exclude static-storage binders. SPEC 15.6.2 lists static keys as a root kind of their own. Both binders are site nodes in B, and row 3 precedes row 7, so "a node in B that is not a declaration" would class an immutable static Borrow Finite and reject `limitRef`. State "a fall-through binder whose node names no static storage". BU's G189 (bu15, in progress) removes static Places classed as Temporary Values, which is the same cause |
| N-2 | "Root of B" and "outside B" should be decided by body ownership (the binder's executable body, as D3 assigns rows), not by syntactic ancestry. An atom matching no row (a nested Closure's root seen in the enclosing body) should be `InternalInvariant_Kd` in the view's validation, not a silent default |
| N-3 | §5.1 (3): `FailedDeclaredRelation` is shared with `JudgeDeclaredRelation` (Fields, Cases, aliases, associated formations). Applying `BodyChain` there could form a row outside any body. None formed in `cf/p/declclause`. Say it applies to local clauses only, and make a declaration-level row `InternalInvariant_Kd` in validation |
| N-4 | Row 9 should also cover a meet end with an `Unbound` operand (the row gets the located `Unsupported_Kd`; no operand edge is silently dropped), and D19's `Unsupported_Kd` should be registered at X-2 (A's text) |
| N-5 | Reason's ends should be the view's canonical atoms (the atom-table representatives), so two Type objects of one canonical meet give one key |
| N-6 | Test additions: the `env-store` partition row (P-2); `cf/p/m-write3` (an annotated `let` initializer is a row today, not a record: default S, MIR S plus the write) |

## 7. Still unverified

- No A5-F4 code exists. Every "A5 under the package" column is derived from dev's rows, atoms and MIR Types plus the package's
  rules.
- Static initializers formed no rows in my probes. Whether any row producer lacks a formation-time owner beyond accessors and
  defaults (P-2) stays §12's stop condition.
- Whether rows with another declaration's universal atoms occur beyond I-1 (P-5's census).
- The coverage of `IncomparableContravariantBoundsRequireAnAnnotation`, the census counts, and the cost terms.

## Round 2 (2026-10-21): the revised package (588 lines) on dev `943c5b90`

dev `943c5b90` (2e-0: a template's PreparedInput local located at its parameter's Type node; no Origin naming change) was rebuilt
in `bin\worktrees\cf-2a0d` with the extended dumper, and removed after, junction first. The affected probes were re-run in
`cf/p2/` and all are unchanged:

| Probe | Result on dev |
| --- | --- |
| `accessor`, `accessor2`, `accessor-for` | Rows in the getter's body, `Function` null |
| `m-write` | MIR: the write conflict at `x = 2` |
| `m-write3` | A row: default S; MIR: the write conflict |
| `m-write4` | The relation only |
| `handle2` | `leak3` accepted on both pipelines; `leak4`: the MIR's storage-end conflict |
| `mto` | Rows name `put`'s `Parameter` atoms |
| `st-imm`, `st-leak` | Unqualified in-group forms still bind (2b-6 (b) has not landed). The qualified forms are now Binding's U (bu14) |
| `env-store` | Rows unchanged |

The prototype was re-run from a copy with one added case (`cf/protoC/`, `runC.txt`, exit 0): 72 cases, 3,000 random graphs, no
reference disagreement.

**Verdict: ACCEPT once P-6 and P-7 below are applied.** Both are text fixes. P-6 corrects OQ-4 (b)'s description; P-7 is a test
expectation that the P-5 edit made unsatisfiable.

### R2.1 The round-1 items

| Item | Where | Status |
| --- | --- | --- |
| P-1 | §5.1's recorded limit; 2a-2's m-write, `var r`, m-write3 and m-write4 tests; STATUS | Applied as worded |
| P-2 | D3; §2's owner list (accessor, Binding's default index, static initializer, the `Parent` chain); 2a0-2's accessor and `env-store` tests; §12 | Applied |
| P-3 | §1; §3's "Static Borrows and 2b-6"; the conditional 2a0-2 and 2a-1a tests (`nameRef` S, `limitRef` valid); §8; 2c-D's "after P42"; §9 item 7 | Applied |
| P-4 | §3 identity; §8; §9 item 6; 2a-1a's `leak3`/`leak4`; §12; STATUS | Applied |
| P-5 | Rows 1, 6 and 9; the order notes; D19; §8's I-1 sentence; 2a-1a's `meet-trans-ok`; 2a0-2's +cen stop | Applied, but see P-7 |
| N-1 to N-6, U-1 to U-6 | — | Applied |
| OQ-1, OQ-2, OQ-3 | — | Changes applied as asked |

### R2.2 OQ-4 (b): soundness

(b) is **sound**. It adds a condition to rule 2, so it drops a subset of what (a) drops.

- **Every drop is covered by a kept problem.** Each drop requires an err(ended) for the same candidate in the same view, and
  every err(ended) stays. A program rule 2 touches therefore stays rejected.
- **An escape through `_0` that is also stored.** The `ResultFit` problem is dropped (the err(ended) covers that escape) and the
  store's S is kept. Both escapes keep a report. The store is reported as S and C together, the stated DOUBLE (`rv-g-param`).
- **A `ResultFit` row with a non-guard source as well.** Reasons are per finite source, so that source's problem has a non-guard
  Anchor and stays.
- **Composition with rule 1.** Rule 1 reads `RefutedAtResult`, which `Judge` writes, not the surviving problems. Rule 1 drops only
  `D` conflicts and rule 2 only S problems, and rule 2 needs the err(ended), so at least that err(ended) survives.
- **A containment-edge chain.** Its last edge is never `ResultFit`, so it is kept. That can only add reports.

### R2.3 New blocking items

**P-6. OQ-4 (b)'s option text overstates it.**

- (b) still loses an independent failure when two things hold together: the candidate escapes elsewhere (an err(ended) exists),
  and a Refuted chain at a `ResultFit` row has no Loan escape behind it.
- Case `cf-g-reassign`: g-two with `saved = fb2` before `return saved`. The complete replacement ends d's Loans in `saved` (SPEC
  15.6), so `_0` carries none. `saved`'s region keeps `g_d`, and judging uses no liveness (15.6.5), so `S ret g_d>packet` is an
  independent failure, as `rv-g-annot-esc`'s annotation is.
- Prototype (`cf/protoC/runC.txt`): LOST under (a) and under (b) (`GuardReverseRet`). It is ok only under the plain design column
  (PD11), which loses g-two.
- (c) has not been prototyped.

Fix:
- OQ-4 (b), replace "A rule variant that keeps it:" with "A rule variant that keeps `rv-g-annot-esc`'s report:".
- After "(DOUBLE).", add: "It still loses an independent Refuted relation at a `ResultFit` row whose returned value no longer
  carries the candidate's Loan while the candidate escapes elsewhere (`cf-g-reassign`: `saved = fb2` before `return saved`; LOST
  under (a) and (b), ok under PD11, which loses g-two). (b) narrows N-6 and does not close it."
- OQ-4 (c), read "exact (H; not prototyped)".
- §7's N-6 bullet for (b), replace "The independent report stays" with "`rv-g-annot-esc`'s report stays; `cf-g-reassign`'s is
  still lost (pinned)".
- 2a-2's tests, add "`cf-g-reassign` (per OQ-4: (a) and (b) S lost, pinned)".
- The recommendation (b) can stand: it drops strictly less than (a) and adds no acceptance.

**P-7. 2a-1a's "`meet-trans` (S)" contradicts the new row 9 on dev's input.**

- `rv/meet-trans` (`cf/p2/mt`) forms its call regions from `put`'s own `Parameter` atoms, as I-1 does.
- Binding records P at `t@uniq` (`z == (a and b)`, fixed–fixed, Binding's). The row `Projection[y] >= (put.a & put.b)` is A5's.
- Under row 9 that row gets the located `Unsupported_Kd`, not S. The program stays rejected (P plus U).
- Under the old row 1 the meet was a Fixed meet, so this program never exercised MeetNode on dev. No dev program forms a non-fixed
  meet at both ends (review §3.2).

Fix:
- 2a-1a's tests, replace "`meet-trans` (S)" with: "`meet-trans`: Binding's P and the located `Unsupported_Kd` of row 9 until BU's
  I-1 repair, which re-expects it as S with MeetNode. MeetNode itself is pinned by a `MirOrigins` test over a view fixture with
  fresh-region rows, until a program reaches it."
- §12, add "MeetNode has no dev reproducer until I-1's repair".

### R2.4 Non-blocking text points

| # | Point |
| --- | --- |
| R2-N1 | §3's header gives "a nested Closure's root seen from the enclosing body" as an atom matching no row (`InternalInvariant_Kd`), but row 9 now covers "a `Projection` of any other declaration" (located `Unsupported_Kd`). Pick one. A closure call's atoms are substituted on dev (`cf/p2/clo`), so I suggest the header names no example, and row 9 applies to nested bodies too, since before 2c-6 such an atom marks a valid form not yet covered |
| R2-N2 | §3 identity: "its Origin must be the root's finite atom" over-generalizes. An immutable static root or a raw anchor is a root without a finite atom. Read "a Borrow anchored in a local, parameter or temporary root" |
| R2-N3 | §5.1 (3): put the new local-clause row in `JudgeLocalClause` (the local caller), so the shared `FailedDeclaredRelation` takes no steering flag (an AGENTS symptom). "That unifies…" now has no clear antecedent. The declaration-level `InternalInvariant_Kd` should be "in a complete compilation" (R4-M10) |
| R2-N4 | §2: "or the forming scope passed to it" cannot identify a default or a static initializer. The bullets above already say so, so add "with the default index or the static declaration" |

### R2.5 Still unverified (added)

- (c)'s exactness.
- Whether any other 2a-1a/2a-1b test or blocking signature (e3312717bde0, bc801917a239) runs through another declaration's atoms
  and so becomes row 9's U (2a0-2's +cen stop decides).
