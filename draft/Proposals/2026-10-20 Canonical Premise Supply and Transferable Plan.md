# Canonical Premise Supply and Transferable Plan

Status: 一部取り込み (open; implementation design, no specification integration).
Owner direction: 2026-10-20, shared premise supply (option A); resolve G170 before connecting Transferable.
Investigation baseline: `64649f97d732dfd6f875dbfbfe66d5805300b310` (`dev`).

G170 supply follow-up: the selected [G170 Premise Dependencies and Inference Plan](2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md)
replaces this proposal's PS0–PS5 work and premise-supply design, including the target ContractSolver ownership and
frozen-inference evidence contract. The original investigation below remains historical evidence. Section 5 and
T0–T3 retain the separate Transferable obligations; their premise-supply prerequisite is now the follow-up's C4 exit.
S5 connection, R4-M83's hold and all applicable owner gates remain unchanged.

This plan covers investigation, design, implementation units and their completion gates. The current request authorizes
this reviewed plan, not compiler implementation. The direction is approved; the detailed implementation package and
new vocabulary still take their applicable approvals. R4-M83's hold, R4-M79's BU/R5 pause and the normal 2b-D gates
remain until their stated conditions are met. No SPEC change is proposed.

## 1. Required meaning and evidence

The required chain is:

> SPEC §8.3 makes declaration premises available independently of spelling and checking order → the current fact bag
> contains only the consequences materialized so far → Binding's Contract solver must own demand-driven premise
> supply, including its source dependencies → syntax-triggered expansion and independent consumer fact scans disappear.

The public invariant is: for the same bound proposition, premise scope and completed source state, proof results do
not depend on unrelated written projections, parameter order, declaration order or earlier queries. Different scopes
may legitimately give different results. Candidate discovery and proof must use the same sources without confusing
a discoverable declaration with evidence that is valid for use.

| Authority | Obligation |
| --- | --- |
| [SPEC §8.2.3](../../docs/spec/08-generics-constraints-and-contracts.md#823-type-identity-and-identity-premises) | Identity classes respect congruence, preserve complete Type structure and never decompose identity into inferred arguments. |
| [SPEC §8.3](../../docs/spec/08-generics-constraints-and-contracts.md#83-constraint-proof) | Only the specified proof rules; four outcomes; conjunction elimination, no disjunction elimination; independent evidence; no self-certifying conformance; Resource failure on exhausted proof resources. |
| SPEC §8.6, §8.8, §8.15 | Preserve bound Contract references, conditional prerequisites, associated mappings and definition-time checking. |
| [SPEC §8.11.2](../../docs/spec/08-generics-constraints-and-contracts.md#8112-confined) | An immutable static read/borrow is free of environment effect only when its Type is proven Transferable. |
| [SPEC §15.2.3–§15.3.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1524-transferable) | Transferable visits the specified components, with Kimi-only exceptions, and uses declaration-level structural fixed points. It requires no Owned proof. |
| [DECISIONS R4-M83](../../docs/dev/DECISIONS.md) | Complete the proof-input supply before 2b-4/2b-5; do not assume existing Facts complete or add local cases. |

### 1.1 Evidence status

The investigation inspected source, tests and recorded findings. It ran no compiler, build, test or benchmark: this
is a documentation-only unit under [VERIFICATION](../../docs/dev/VERIFICATION.md#verification-scope).

- [PLAN G170](../../docs/dev/PLAN.md) and [INTEGRATED's S5 correction](../INTEGRATED.md) record the measured defect:
  lexical and effect queries for the same deep Type and scope return Unknown; an unused CopyTree-qualified parameter
  before or after the original supplies a missing premise and changes that original Type's answer to Proven.
- [S5Probe.cs](../Sketches/A5%20Step%202/S5Probe.cs), its final three cases and `Helpers`, retains the earlier programs.
  Its labels and the frozen S5 §10 interpretation are superseded by that correction. Zero `Binding.Issues` was not
  acceptance: the duplicate-value program had ordinary transfer/move failures; both helper chains had an ordinary
  effect-bound failure, while the shorter passed MIR A4.
- The historical handoff output directory is absent in this checkout. No fresh reproduction or performance claim is
  made here. PS0 must reproduce on its exact head; changed observations require revising this plan before coding.
- G170 is an observed missing proof, not an observed unsound acceptance. G124 is the separately recorded unsound
  immutable-static classification. Do not conflate their evidence or claim this plan closes either.

### 1.2 Reproducer and acceptance oracle

The core of the G170 query is:

```kimi
contract Tree
    associate Child is Tree

contract CopyTree: Tree, Copy
    Self.(Tree).Child is CopyTree

func inspect<T>(value: T.(Tree).Child.(Tree).Child)
    T is CopyTree
    ()
```

Query the first parameter's already-bound Type with `Binding.ProveCopy`, then with the definition's TyCtxt effect
query. Expected by §8.3: Proven. No concrete implementation of recursive CopyTree is needed to check this generic
definition under its stated premise. A concrete conformance cannot certify itself through recursion.

PS0 captures the original program and variants with an unused reference parameter spelling the equivalent projection
through CopyTree, placed before and after `value`. It must verify that the original queried Type and scope remain the
same within each comparison; compare semantic identities across reloads, not object addresses from different
compilations. Record raw proof outcomes, final Binding completeness, ordinary diagnostics and MIR A1/A4 records
separately. Keep failing desired regressions as evidence until their implementing unit passes; do not commit a test
asserting the defective behavior as a permanent contract.

## 2. Current control flow, responsibilities and dependencies

Source paths below are relative to `src/Kimi/Compiler/Binding/` unless another owner is named. Line locations refer
to the investigation baseline; method names remain the navigation anchors.

### 2.1 Binding lifecycle

```text
Binding.Bind
  ResetPass → IndexSources / IndexLibrary
  → BindDeclarations
      PrepareContracts → BindConstraints → inherited conformances / associated inference
      → headers / storage / signatures / overrides / declaration validation
      → PrepareEffectBounds → capabilitiesReady → conformances → environment validation
  → BindBodies
  → ValidateBoundDeclarations
      clear capability results → Origins / associated / Copy / API / property validation
      → conformances / uses → clear results → effects → projection-certificate invalidation
      → expression projection checks / acquisitions / virtual checks
  → Check
```

`Binding.cs:85`, `:316`, `:446`, `:525` own these stages. `InvalidateProjectionCertificates`
(`Binding.ConstraintValidation.cs:209`) repeats dependent declaration/certificate checks until source states stop
changing. Late invalidity is therefore part of the proof contract, not merely an edit-time cache concern.

`ResetPass` clears facts but retains interned Types, Origins and propositions, scope/symbol storage and reusable
buffers. `ResetContracts` resets Contract shapes, paths, associated evidence and projection uses. The new owner must
follow these lifetimes and must not introduce a second compilation generation scheme.

### 2.2 Cause and shared sites

| Site | Current responsibility and consequence |
| --- | --- |
| `Binding.Constraints.cs`, `BindConstraints` | Binds constraints in three passes, then invokes one expansion before associated bindings. |
| `Binding.Contracts.cs`, `ExpandScopeContractPremises` | Copies the current Facts count and expands that snapshot once. New facts are not another worklist round. |
| `Binding.Contracts.cs`, `AddContractPremises` | Substitutes own/inherited clauses and publishes ancestors; it does not provide a complete query contract. |
| `Binding.AssociatedTypes.cs`, `BindAssociatedProjection` | Expands the proven Contract selected by the written qualifier/reference of the receiver. |
| `Binding.ContractCalls.cs`, `ContractMembers` | Performs analogous expansion when a receiver's members are referenced. |
| `Binding.VirtualOverrides.cs`, `PrepareVirtualOverridePremises` | Adds inherited implementation inputs and invokes the one-pass expansion. |
| `Binding.Constraints.cs`, `ProveConstraint`; `Binding.Capabilities.cs`, `SymbolicCapability` | Consume materialized facts and structural judgment; neither guarantees missing declaration consequences are supplied. |

The code explains why syntax expansion was bounded: recursive associated requirements can generate an infinite
chain. Replacing the one-pass loop with global saturation would remove that bound and is not the fix.

The source-consistent explanation of G170 is that `T is CopyTree` supplies the first Child's CopyTree premise, but a
deep projection written through Tree expands only Tree on the successive receivers. CopyTree on the intermediate
receiver is not expanded until another spelling requests it. Projection identity uses the declaring Contract, so
that expansion can change proof about the original Type. PS0 must confirm this path with current-head observations.

### 2.3 Representations already present

| Representation | Invariant to keep |
| --- | --- |
| `BoundConstraint` / `ConstraintKey` (`Binding.Constraints.Model.cs`) | Immutable compilation-interned proposition DAG; identity hashing, not recursive hashing. Reuse it. |
| `ConstraintProof` | Unknown, Proven, Refuted, Error. Error absorbs; evidence for both polarities is Error. No fifth logical result for resource exhaustion. |
| `ConstraintEnvironment` | Direct assumptions and derived facts currently coexist. `DerivedFacts` names a source Contract; a new multi-step derivation needs its full support dependencies. |
| `BoundContract.ClauseStorage`, bound Contract references | Existing declaration templates, inherited requirements and applied arguments; no duplicate Boolean language or second Contract table. |
| `ContractConstraint`, `ContractType`, `AssociatedIdentityMatches`, `FactStates`, `AppliedAssociatedContract` | Existing substitution, associated-family matching and identity owners; preserve declaring reference, Type/Origin arguments and domain conditions. |
| Capability work graph | Existing keys include proposition, scope and derivation purpose. Its structural capability initialization is not the rule for recursive Contract evidence. |
| `ConstraintScope` / `EffectPremises` | Lexical context and inherited implementation-premise context. TyCtxt.Copy already delegates to Binding.ProveCopy; MIR has no independent Copy rule to repair. |

`AvailableConstraintFact` validates a derived fact's source through `AvailableContractPremise`, including closed
obligations and a recursion guard. The new supplier must retain original assumptions and every required intermediate
source. One unavailable source must not erase another independent valid derivation.

### 2.4 Consumer and producer census

This is the mandatory cutover inventory, not a request to replace each loop with an unbounded "all facts" enumerator.

| Meaning | Current readers / writers | Target responsibility |
| --- | --- | --- |
| Proof, capability, conformance | `Constraints`, `Capabilities`, `Contracts` | Common supported derivations; capability/conformance-specific judgment stays with its owner. |
| Associated/member/indexer discovery | `AssociatedTypes`, `ContractCalls`, `Indexers` | Common subject candidates with source dependencies; lookup still selects names/members. |
| Callable/signature candidates | `CallableSelection`, `ValueCalls`, `Retention` | Same candidate sources; existing signature/effect selection rules remain. |
| Arithmetic candidates / contradictions | `ArithmeticOperators`, `ArithmeticDiagnostics`, `ArithmeticContracts` | Common supplied constraints; arithmetic-specific rules remain canonical there. |
| Identity and normalization | `TypeIdentity`, `AssociatedTypes`, `Constraints`; `Structs.FailConstructorAbsence`'s identity shortcut | One identity owner used by proof, matching and lookup; cached presence flags cannot hide a derivable identity. |
| Semantics and Type roles | `ConstraintValidation` | Supplied premises; existing admitted-set and formation rules remain. |
| Effect requirements | `EffectDeclarations`, `CallableEffects.AvailableCallableEffects`, `EffectObligations`, `TyCtxt.Effects` | Supplied evidence under the existing definition scope, including readers through cached callable clauses. |
| Scope composition / overlap | `ConditionalConformances`, `ContractAgreement`, `ContractMatching`, `InheritedConformances`, `VirtualOverrides` | Compose finite input assumptions and source references; do not copy cached derived facts as assumptions. |
| Declaration validity / contradictions | `ValidateConstraintEnvironments`, `ValidateContractDeclarations`, projection certificates | Explicit declaration obligations and relevant derived evidence; no enumeration of infinite closure. |

Candidate discovery currently sometimes sees a pending Contract before proof may use it. Filtering these candidates
to Proven would change name discovery and diagnostics. Conversely, a discovered member is not evidence that its
Contract's prerequisites hold. Both distinctions must survive.

The census includes indirect readers of `AvailableConstraintFact`, `AvailableContractPremise`,
`HasParameterIdentity` and `HasAssociatedProjection`, not only loops over Facts. A flag meaning "already materialized"
cannot be used to skip a demand that the new supplier could satisfy.

## 3. Independent designs and choice

The planning session considered **F**, subject-driven forward expansion: seed a query's subjects and prefixes, expand
their available Contracts, and reuse the current fact machinery. An independent design agent, after inspecting the
same source, supplied **D**, goal-directed declaration-rule evaluation with explicit support dependencies.

| Design | Benefit | Reason for choice or rejection |
| --- | --- | --- |
| F: subject/prefix forward closure inside Binding | Smallest apparent change; reuses expansion functions. | Rejected as the complete design. Prefix-only relevance misses deep clause subjects and identities; admitting every generated subject can diverge. Adding all missing relevance machinery approaches D. |
| D: demand-directed existing-rule evaluator | Makes source support, completion and candidate discovery explicit; reuses existing propositions/declarations. | Selected, subject to PS1's executable completeness and resource gates below. |
| Eager global closure | Short loop. | Rejected: recursive Contracts can create unbounded projection terms. |
| Closed concrete whitelist | Small, auditable support boundary. | Not selected by the owner. It leaves G170; adding abstract premises requires closure over their positive and negative dependencies. No implicit fallback to this option. |
| Transferable-local premise scan / larger expansion depth | Small patch. | Rejected: repeats the cause and makes source spelling or arbitrary depth semantic. |

The planning session selects D, using F's useful subject-oriented consumer interface. The independent document
review is separate from both design authors. This is a proposed implementation design, not a claim that the hard
relevance and finite-summary proofs have already been executed.

## 4. Selected premise design

### 4.1 One owner and one source of meaning

Binding remains the owner; this is the Contract-solver part of R7's target brought forward for its current consumers.
Place responsibility in `Binding.Premises.cs`, with rule application and request storage split by responsibility
when necessary. Keep the existing `Binding.Constraints.Model.cs` vocabulary and existing declaration tables.
No service interface, second proposition IR, Transferable-only premise table or syntax-keyed proof cache is needed.

The component entry contract states:

- Inputs: bound proposition or subject demand, its existing premise scope, finite declared assumptions, bound
  declaration templates and current validity/conformance dependencies.
- Outputs: the existing four-valued proof, or subject candidate constraints carrying their support dependencies.
  Incomplete evaluation has a separate failure/provisional path and is never published as a completed answer.
- Invariants: all applicable allowed derivations, correct scope/substitution, both polarities, no self-certification,
  no semantic dependence on which consumer first asks.
- Prohibitions: source/member search to prove a proposition; inventing Type arguments; case analysis over disjunction;
  silently treating unavailable work as Unknown; global recursive instantiation.

Keep finite declared input facts. Derived facts become request results with support edges; they must not be copied
into another scope as direct assumptions. A scope union or inherited-implementation scope composes roots and source
references, then uses the same supplier. Existing `ImplementationPremises` boundaries remain intact.

Two consumer operations are required: prove an existing bound proposition; enumerate candidate constraints about an
existing bound subject. They share declaration-rule application and provenance. Candidate rows can be discoverable
while their prerequisites are pending; only available support can prove or normalize anything. Lookup ambiguity and
selection stay with the lookup owner. Enumeration itself must finish or report a resource failure, not silently trim.

### 4.2 Rule application

1. Seed a request from the scope's explicit assumptions, including outer scopes and the proper implementation scope.
   Registered-but-unverified conformances are candidate sources, not positive evidence.
2. Use existing bound declaration clauses as templates. Ancestor conformances and own/inherited constraints are
   consequences of an available bound Contract instance; the instance and declaration prerequisites are dependencies.
3. For each demanded proposition inspect every template head that can supply it or its negation. This includes deep
   subjects such as `Self.(A).X.(B).Y`; it is not a walk limited to the query's immediate receiver or written qualifier.
   And retains the compound and supplies conjuncts. Or supplies no disjunct. Preserve the existing compound-operand
   validation and exact-assumption rules.
4. Match template binders using existing substitution/identity rules. Only a template's Self and declared parameters
   are instantiated. A program's abstract Type parameter is never solved to make a rule match. Slots absent from a
   head must come from an available antecedent bound Contract instance; never enumerate or guess language Types.
5. Register the instantiated antecedent and every source-validity dependency. New independent evidence wakes its
   dependents. Explore stronger refinements even when the projection is written through their base Contract.
6. Finalize only when all relevant positive, negative and error dependencies have been considered. A positive hit
   cannot conceal contradictory evidence or an error that the existing proof contract requires checking.

A declaration-head index may key by existing constraint kind, Contract declaration and polarity. It refers to
original clauses; it is not another rule language. PS1 starts with exhaustive template selection on bounded models
to check an indexed selector. Keep an index only if measured time or code reduction justifies its retained memory.

### 4.3 Identity, provenance and cycles

Use raw interned proposition plus existing scope as a request key, rather than a representative that may change
while identity evidence is being established. Identity matching uses only proven equivalence and reconsiders affected
matches after a new equality becomes available. Preserve applied Contract and associated-family Origin identities.
This is the premise-demand key, not permission to collapse capability keys: retain the existing capability
derivation purpose as well as proposition and scope. Nominal Copy derivation and an ordinary Copy query are distinct
operations even when their proposition is the same.

There is one identity owner. `TypeIdentity` currently describes parameter substitution, and associated normalization
has its own fact lookup. PS1 must establish the necessary §8.2.3 congruence across these paths. If a shared identity
change is needed, PS2 isolates and reviews it before supply cutover; do not implement a second identity solver inside
the supplier. An existing broader identity defect is an open issue until reproduced, not a finding established here.

Each derivation carries the input/root support, the bound Contract instance and the validity dependencies of every
clause used. Track alternative supports separately. A pending or cyclic path supplies no evidence; an independent
finite path may still prove the same goal. Contradictory available supports produce Error.

Revisiting a Contract obligation adds a dependency edge, not a proof. A finite dependency component with no seeded
evidence closes without that evidence. Structural capability fixed points retain their own rules; in particular,
Owned's current initialization must not become Contract coinduction.

Also preserve SPEC §8.3.3's stronger rule: a concrete Type cannot be proven to conform to a Contract classified as
recursive by that section, even if another finite path appears available. Independently seeded prerequisite-query
cycles for other Contracts are a different case. Abstract input premises on recursive Contracts remain usable;
G170 needs those finite consequences. PS1 explicitly tests all three cases and the recursive classification owner.

### 4.4 Completion, resources and lifecycle

| State | Treatment |
| --- | --- |
| Completed Proven / Refuted / Error | Existing logical meaning. |
| Completed Unknown | No allowed evidence for either polarity in the relevant completed search. |
| Unresolved source during provisional binding | Existing provisional unavailability; later source completion must re-evaluate it. It is not a permanently cached final result. |
| Work still in the request | Internal dependency state, never a final Unknown merely because a fact has not yet been visited. |
| Resource exhausted | Located Resource failure; no accepted proof or Language rejection derived from an incomplete search. |
| Required semantic rule unimplemented | Located `Unsupported_Kd`; no accidental narrowing of SPEC. |

Finite templates do not imply finite instantiated obligations. PS1 must establish relevance completeness for the
supported inputs and bounded execution for growing terms. Its reference evaluator and production design include
limits on proof exploration and materialized work; select and record concrete limits from the adversarial workloads,
before implementing the supplier. Test the exact boundary and limit+1. Do not reuse S5 effect widening for proof.

The existing catalog has a generation Resource code, not an established generic-proof Resource path. PS1 must
specify the typed failure propagation and an appropriate proof Resource diagnostic (proposed name
`ConstraintProofResourceLimit_Kd`), including the first located producer failure and typed dependent-use handling.
Dependent logical judgments stop at that failure; independent errors remain. Do not generate unproven errors and
filter duplicate reports afterwards.
This is an explicit vocabulary/diagnostic approval item, not permission to misuse `GenerationResourceLimit_Kd`.

Start with reusable request-local work storage. Nested proof, identity and candidate demands use the same request;
no new completed premise result persists between top-level requests. Retain allocation capacity, not valid evidence.
Bring premise-dependent capability results into this request lifetime too: retaining its current evaluated bits while
only making the new supplier request-local would leave stale answers. Preserve its distinct structural semantics.

No authoritative premise is added because a query was made. Declaration/source mutations occur through Binding's
existing lifecycle, outside a completed query; dependency checks may memoize work but cannot certify a source from
its own in-progress judgment. PS1 tests invalidation within the final-validation sequence, not just ResetPass.
If existing source-validation callbacks mutate these inputs during a request, separate that validation from the read
before cutover. A new persistent cache or generation hierarchy needs a measured, separately reviewed design.

### 4.5 Cutover and deletions

First restructure with behavior unchanged. Then one semantic cutover unit routes every dependency in §2.4 through
the common source contract. Split preparatory moves and independent identity work, not the meaning of premise supply.
Do not leave half the consumers reading a mutable derived-fact bag on a mergeable branch.

Remove in that cutover:

- syntax-triggered `AddContractPremises` calls from associated and member access;
- one-pass expansion as the semantic source of completeness, including the override call site;
- copied derived facts treated as input assumptions and the obsolete `DerivedFacts` materialization;
- independent semantic scans of that bag in proof, normalization and candidate consumers.

Finite input-fact validation, source-use tracking and projection certificates are not deleted merely because their
files mention Facts. Their separate contracts remain, with common supplied evidence where needed. Keep the closed
census of remaining raw accesses: each must be input construction, declaration validation or storage maintenance,
not another derivation path. Remove temporary adapters in the same cutover; no fallback to the old supplier.

## 5. Transferable connection after G170

### 5.1 Structural design gate

The common supplier answers abstract obligations; it does not establish Transferable's finite structural domain.
After PS5 closes G170, T0 must produce and review that domain before 2b-4 resumes.

The candidate representation is a per-declaration symbolic dependency summary over formal Type parameters/slots,
declared components and substitutions, with exclusion propagation from the bottom (no exclusion). It retains
abstract obligations and their source contracts. It must not be a set of all instantiated Types or ever-growing
projection strings. T0 defines exact node/edge meaning, substitution composition, finite cardinality and monotonic
transfer functions; merely drawing a finite declaration graph is insufficient.

The prototype must cover regular and non-regular recursion, changing Type arguments, recursive associated obligations,
negative and unknown abstract evidence, and exceptions behind library storage. If a required obligation cannot be
represented finitely, keep the hold and redesign that representation; do not add a depth cap returning Proven or
Unknown, silently use a whitelist, or copy the current capability growth heuristic.

### 5.2 Full Type and library contract

Transferable is currently absent from `Library/Core.kimi`, `IntrinsicKind` and the Kimi declaration catalog. The
2b-4 implementation includes its public declaration, intrinsic identity, catalog access, ordinary lookup and the
structural-intrinsic prohibition on user conformance declarations. A user Contract named Transferable gains no
intrinsic behavior. Abstract `T is Transferable` must use the same proof path as other input constraints.

Use SPEC's component definition, not `HoldsRaw` or Loan holding: Type arguments (including unused/inherited ones),
bases, fields, Case payloads, concrete object payloads, closures and Function Item bound arguments matter. A callable
signature alone stores no value. Common Function Types and base/runtime-Contract views refute. Borrow Origins do not
create an Owned requirement. A reachable rc, Weak of rc or non-exempt raw pointer refutes through ref and arc too.

The raw-pointer exception is attached to the exact Kimi collection/standard iterator identity and its internal
storage, not to user shape or all descendants. An `Array<rc/X>` is still refuted. An empty container, inactive Case
or zero-length array does not erase its component Type obligation. Async execution and worker implementation remain
outside this plan.

### 5.3 MIR publication and consumption

Binding decides the Type verdict under the correct declaration/definition premises; a pure TyCtxt view publishes it.
An Error or incomplete/resource result follows its producer failure path, never becoming an ordinary Unknown static.
MIR consumes a valid published verdict; it does not inspect syntax, repeat proof or repair a broken contract.

T0 must fix the exact proof anchor and substitution before choosing the publication shape: the static's bound Type,
the reading body's `EffectPremises(definition)` (including inherited implementation premises), and any selected
Semantics case must agree with the SPEC judgment. `MirStatic` is body-local, while A4 runs per MirCases view.
A single verdict per entry is the preferred representation only if T0 proves it sufficient across those views.
Compare the same generic Type in two definition scopes, inherited contexts and mixed pair admitted sets. If cases
need distinct facts, T0 must obtain review/approval for producer-owned per-case publication before T1/T2; MIR must
not recover the missing fact by re-proving it or silently classify a proven case as an environment effect.

The current paths are `MirBuilder.Places.StaticRoot` → `MirStatic` in **`MirForms.cs`** → `MirDescriptors` →
`MirEffects`. Under T0's approved publication shape, replace `RawHolding` with the existing proof verdict for valid static entries. The dump prints that
verdict. For immutable reads not Proven, combine existing `MirEffect.StaticRead` with existing `MirEffect.Environment`.
Delete `RawStaticRead` and its special A4 bit check: no new effect kind is needed. Preserve mutable-static and initializer
summary rules. Update Environment's comment to cover SPEC's environment authority, including this access to program
storage. Review this representation with R5 under the existing shared-file hold protocol.

After a fresh reader census, delete `MirTypes.HoldsRaw` and its private Raw walk and replace its approximation tests
at the new owner. Preserve `HoldsLoans`, `MirWrites`, G148/P13 guards and independent destruction tests in the same file.
The old pipeline's `ContainsRawPointer` path remains under D5; changing it requires the applicable separate authority.

S5 is a separate connection: verify G170's canonical proof and A1/A4 effects at its implementing head, then record the
S5 expected matrix. S5 can widen a weaker callee's summary even when a caller knows Copy; do not promise that repairing
G170 makes the frozen helper-chain examples accepted after S5. G124 closure and F5 Landed still require the other F5
units and 2b-8; this plan does not absorb static-key, M69, G136 or unrelated effects work.

## 6. Small, verifiable implementation units

PS/T identifiers are local to this proposal; the existing A5 unit mapping is explicit below. Each unit has its own
evidence and commit where it changes tracked work. An implementation unit starts only after its prerequisite design
has passed independent review and the applicable owner gates. Keep worktree edits/builds/checks/commits serialized.

| Unit | Change and owning files | Completion condition / checks |
| --- | --- | --- |
| PS0 — current-head evidence | Reconstruct G170 in the existing projection-test owner; capture baseline diagnostics, semantic snapshots and proof results; census all supply/read sites and token sizes/hotspots. | Absent/before/after unused parameter, weak/strong qualifier, lexical/effect, ordinary/MIR observations recorded. Desired regression fails before repair. Do not land a failing suite. |
| PS1 — executable supply design | Prototype D under `draft/Sketches`; bounded reference evaluator versus demand selector; specify provenance, candidate semantics, identity, request lifetime and Resource publication. | §7.1's prototype obligations pass; independently review the resulting exact representation and measured limits. Record proposed vocabulary and obtain applicable approvals, including the default-acceptance disposition below. No production change until this passes. |
| PS2 — prerequisite restructuring | Move proof-supply responsibility into `Binding.Premises*`; split due files by responsibility. If PS1 shows identity machinery insufficient, first design its owning change and implement/verify it as a distinct behavior unit after its own restructuring. | For moves: unchanged diagnostic, semantic HIR/MIR and generated-fixture snapshots. Identity behavior, if needed, gets an independent regression and cannot masquerade as a move. |
| PS3 — canonical supply cutover | Implement D and request-local proof-dependent state; migrate all semantic readers/writers in §2.4; remove syntax expansion and derived-bag alternatives. Resource failure publication ships with its bounded evaluator. | G170 regression and candidate/identity/lifecycle matrices pass; no semantic old-bag readers; focused Unit plus allocations and compiler census. This is one coherent behavior unit after the preparatory splits. |
| PS4 — proof cost and structural audit | Add shared generated workloads and timing entry in Benchmark if existing entries cannot measure this meaning; measure PS0 vs PS3; remove redundant work revealed by measurements with separately verified changes. | Time, allocation, retained-state and work counts by depth/width/sharing/edit; existing allocation assertions hold. Record sizes/hotspots/duplicates; no invented speed claim. Benchmark changes require Session. |
| PS5 — G170 completion review | Final review of PS2–PS4 against the problem and §7, normal and MIR consumers, source validity and deletion census. Update affected PLAN/CODEMAP/ARCH/DECISIONS facts as their policies require. | Canonical proof and scoped consumers agree; G170 evidence-backed closure, independent review passes, default acceptance matches its pre-implementation D5/O3 disposition. S5 connection may then use this supply under its own unit. |
| T0 — finite Transferable design (2b-D input) | Define/prototype §5.1 with real proof inputs; add complete source/shape/census inventory to the F5 design package. | Finite-summary argument and adversarial prototype pass; BU/R5 design review, OQ2 approval and applicable vocabulary/O1 approvals. Only then lift the limited 2b-4/2b-5 hold. |
| T1 — Transferable proof (2b-4) | Library declaration/catalog, intrinsic judgment, per-declaration summary and `TyCtxt.Transferable` publication; no MIR consumer yet. Precede with a separate behavior-preserving split if due. | Public/source lookup, structural denial of user declarations, abstract/recursive/exception tests, KimiLibraryShapeTest, warm/cold/retained measures and census pass. Record new representation/identities and LIBRARY support accurately. |
| T2 — MIR connection (2b-5) | Static verdict, descriptor Environment classification, dump, delete RawStaticRead and obsolete raw walk. Shared-file/R5 slot review. | MirBuild/Effects/Validator/Types/Dump checks, G124 failing-before regressions, goldens/census and sel pending check; no binding re-proof in MIR, no raw approximation reader. |
| T3 — F5 closure (existing 2b-8) | Combine T2 with 2b-3 and 2b-6 evidence and existing F5 obligations. | Existing 2b-8 ledger/verified SPEC/census requirements and final F5 review pass before F5 Landed or G124 closure. R4/R6 exit obligations remain. |

PS3 is larger than a one-function patch because premise supply is one shared semantic contract. If PS1's measured
change cannot fit a reviewable unit after PS2, revise the unit decomposition before coding. Do not split by keeping
two suppliers live or by routing selected Type shapes to a new supplier.

PS0/PS1 must assess changes to default-pipeline acceptance before PS3 starts: correcting shared Binding proof can
change old-pipeline outcomes even without editing old Analysis. Obtain any required D5/O3 exception and native
generation evidence then. If the owner instead requires a temporary legacy boundary, its exact located Unsupported
gate and removal stage must be designed and approved before implementation. This plan authorizes neither an
unreviewed acceptance change nor an ad hoc legacy workaround; failure to decide this blocks PS3, not just PS5.

## 7. Proof and verification gates

### 7.1 PS1/PS3 matrix

| Rule or risk | Required case and oracle | Existing test owner |
| --- | --- | --- |
| G170 / spelling | Original deep Type; unused stronger spelling absent/before/after; depth 1/2/4/8 and generated queries without syntax-triggered expansion. | `AssociatedRefinementCapabilityBindingTest` |
| Stronger and deep templates | Refinement on inherited associated Type, deep non-immediate clause subject, Contract Type arguments, head with unused binder, associated Origin family. | `AssociatedSubjectConstraintBindingTest`, `AssociatedContractParameterTest`, `AssociatedIdentityTest` |
| Only SPEC logic | And vs Or, exact compounds, not, both polarities, operand Error; no `P or Q, not P ⇒ Q`. | `ConstraintBindingTest`, `NormalizedConstraintProofBindingTest` |
| Identity | Parameter aliases, associated identities, congruent nested Types; no inverse argument inference; conflicting concrete identities and proper-part cycles. | `TypeIdentityTest`, `AssociatedIdentityTest` |
| Source support | Pending/invalid outer and refined source; one cyclic source plus independent finite source; contradictory evidence becoming available late. | `ProvisionalContractPremiseBindingTest`, `ClosedConformancePrerequisiteBindingTest` |
| Discovery vs proof | Pending associated/member candidates remain discoverable; final use validates; Callable/indexer/arithmetic/effect facts agree with direct evidence. | Existing associated/member, `CallableSignatureSelectionTest`, indexer/arithmetic/effect test owners |
| Finality | Reverse declaration/query order, repeat, provisional→final, append/remove/repair, reload, source invalidation inside final validation. | `ClosedConstraintLifecycleBindingTest`, `LateConstraintEnvironmentBindingTest`, `ConstraintProjectionCertificateBindingTest`, `UnresolvedCapabilityProofBindingTest` |
| Scope isolation | Conditional vs outer scope, property witness, inherited override implementation premises, same Type in different contexts. | `ConditionalConformanceBindingTest`, `VirtualOverrideContractTest`, existing property witness tests |
| Contract recursion | Finite derived demand from recursive input; no concrete conformance to a recursive Contract; independent finite evidence for other prerequisite cycles. | `AssociatedRefinementCapabilityBindingTest`, `ClosedConformancePrerequisiteBindingTest` |
| Structural recursion | Existing Copy/Owned fixed points retain behavior; changing instantiations do not become proof cycles. | `IntrinsicCapabilityTest` |
| Resource completion | Finite near-limit success; bound+1/growing substitutions produce a located Resource result; never swallowed as Unknown or a Language rejection. | Extend proof/diagnostic owner selected by PS1; new code approval required. |
| Consumers | Direct Copy query plus double-use acquisition and destruction/effect counterpart; retain ordinary diagnostics separately. | `MirInitTest`, `MirEffectsTest` and existing ordinary-analysis owner |

The reference evaluator's bounded examples are an oracle for selector equivalence, not a proof that all infinite
searches terminate. PS1 must also give a rule-by-rule completeness argument for demand selection, including sources
that are not syntactic prefixes, and define honest resource exits. A missing argument blocks PS3; it does not permit
an extra local case. Tests named above exist at the baseline; new methods reuse their owning helpers and assert the
lowest public observable rule. Do not duplicate native fixtures for proof-only behavior.
PS1 demonstrates the matrix in the model and request-lifecycle prototype; PS3 verifies the corresponding production
regressions. Transferable's §7.2 matrix belongs to T0/T1/T2, not to the earlier PS1 gate.

### 7.2 Transferable matrix

Cover each §15.2.4 exclusion and each Kimi exemption with an invalid neighbor: rc/Weak/raw through ref and arc;
common Function versus concrete Function Item/closure; base/runtime view versus concrete payload; arbitrary and
proven abstract Types; ordinary shadowed Transferable; user opt-in/opt-out rejection; collections with transferable
and excluded elements; unused/inherited arguments; all Case payloads; empty/zero-length components; regular and
changing recursive storage. Include malformed source Error and missing proof Unknown separately.
Include pair Semantics cases, two reading-definition scopes and inherited implementation contexts; T0 must settle
their publication shape before a single per-static verdict is treated as sufficient.

Existing owners include `IntrinsicCapabilityTest`, `TyCtxtTest`, `KimiLibraryShapeTest`, `MirEffectsTest`,
`MirBuildTest`, `MirValidatorTest`, `MirDumpTest` and `MirTypesTest`. Replace the raw approximation assertions rather
than preserving a second supposed Transferable contract. Actual new summary tests belong at its producer.

### 7.3 Commands and evidence selection

Feedback uses an incremental test-project build and selected existing/new methods. Completion uses
`scripts/verify.ps1`; ArchitectureRulesTest is added automatically. A focused starting selection for PS3 is:

```powershell
$proofClasses = @(
    'AssociatedRefinementCapabilityBindingTest', 'AssociatedSubjectConstraintBindingTest',
    'ConstraintBindingTest', 'NormalizedConstraintProofBindingTest', 'TypeIdentityTest',
    'AssociatedIdentityTest', 'ProvisionalContractPremiseBindingTest',
    'ClosedConformancePrerequisiteBindingTest', 'ClosedConstraintLifecycleBindingTest',
    'LateConstraintEnvironmentBindingTest', 'ConstraintProjectionCertificateBindingTest',
    'UnresolvedCapabilityProofBindingTest', 'IntrinsicCapabilityTest',
    'MirInitTest', 'MirEffectsTest', 'MirCensusTest'
)
./scripts/verify.ps1 -Mode Unit -Class $proofClasses
```

Add the touched candidate/conditional/virtual owners from the final census; the sample is not permission to omit
them. Use one selector scheme unless an intersection is intended. Keep allocation coverage (`TestPurpose All`).
Every requested selector must execute tests from the newly built assembly. At session end run the ordinary Session;
a Session on identical inputs covering the final unit replaces its separate Unit run.

T2 additionally runs the affected sel Unit (`-Pipeline mir -Codegen mir`) and records pending-ledger differences
under R5-M8 without rewriting R5's ledger. Run each unit's required +cen and +led measures, including the existing
2b-8 verified examples and explicitly selected milestone obligations. Session does not implicitly select native
fixtures/milestones. Native generated-code behavior checks and O2 measurements are added where the final diff or
F5 plan requires them; never run NativeAOT without explicit instruction.

PS2 behavior-preserving commits require baseline/candidate diagnostic snapshots, semantic snapshots with HIR/MIR,
and generated fixture comparison under VERIFICATION's procedure. Preserve failed and successful evidence.
Compiler/benchmark units commit after checks, associate exact inputs with `verify-commit.ps1`, then push without
force. The current documentation-only proposal uses diff/reference and independent review; the association tool
has no documentation mode, and no Verify run is fabricated for it.

## 8. Size, cost, ownership and document maintenance

Planning estimates, not measured commitments:

| Area | Expected change / performance consequence |
| --- | --- |
| Premise supply | Several hundred production lines added for explicit demand/support, offset by expansion/provenance/consumer-scan deletions. Exact net size follows PS1 and PS2's census. No new proposition hierarchy. |
| Identity | Zero new mechanism if existing owner satisfies the needed contract; otherwise a separate measured owner change, not an unbudgeted addition inside PS3. |
| Query lifetime | More recomputation is possible than with persistent result caches; reused capacity targets existing warm 0 B tests. Accept only measured equivalent-work cost, never extra warm-up or weakened assertions. |
| Transferable | Old plan's +80 estimate omits the missing public intrinsic and unresolved finite summary. Re-estimate at T0; do not treat it as a cap or promise. |
| MIR consumption | Small net reduction expected from removing RawHolding/RawStaticRead and the unused Raw walk; loan holding remains. |

Time-only workloads stay in `src/Benchmark`. Measure cold/warm time, allocations, retained state and evaluated rule
instances by demand depth, declaration width, shared derivations, repeated calls and edits. Compare equal source and
configuration in matched runs; old incorrect Unknown and corrected deep Proven are not equivalent semantic work,
so also use already-correct cases for regression comparisons. Record source identities and retain failed runs.

The baseline line counts are 1,913 (`Binding.cs`), 783 (`Constraints`), 987 (`Contracts`), 784 (`AssociatedTypes`),
641 (`Capabilities`), 176 (`TypeIdentity`) and 691 (`ConstraintValidation`). These are not token-limit findings.
PS0 measures Roslyn tokens and recent unit history; split due files before growth under ARCH's 5,000-token,
60-statement and other limits, and before a fourth consecutive unit touches a hotspot. Do not add exceptions here.

R4 coordinates the A1/A4 and F5 consumers; G170's Binding ownership stays with BU or a specifically assigned
implementer. This plan request does not globally resume BU/R5. Shared MIR/TyCtxt holds follow ARCH; R5 reviews T2
and BU reviews producer contracts. Implementation prompts carry this design, unit boundaries, required deletions,
oracles and checks; a design mismatch stops implementation for a planning decision.

| Document | Future update trigger |
| --- | --- |
| DECISIONS | First implementation of the chosen supply representation, request lifetime, identity departure, Resource code, Transferable summary and public intrinsic identities; preserve superseded decisions. |
| COMPILER_ARCHITECTURE / CODEMAP | Actual owner/contract moves and stage state; no speculative change in this proposal unit. |
| PLAN / STATUS | Actual next action/blocker or verified boundary change; G170/G124 stay open until their evidence passes. |
| LIBRARY | Transferable's implemented public declaration and verified guarantees in T1. |
| SPEC / IMPL | No change planned. A discovered gap is reported separately; implementation must not narrow the language. |
| INTEGRATED | Register this intake and its existing-rule mapping in this commit. No SPEC text integrated or frozen by intake. |

Relevant SETTLED constraints remain: no added spelling for constraint identity, no hidden use conditions from generic
instantiation, and no change to defaults' existing interpretation/premise context. This plan changes availability of
already-declared evidence, not the language's inference or syntax.

## 9. Review record and completion of this proposal

Independent design D was written by an agent that did not author planning design F. Source/lifecycle and
verification inventories were independently inspected. The final document reviewer must not have authored either
design; it starts from G170, the general §8.3 rule and a shorter/general alternative.

The review must check especially: finite-demand claims, all semantic fact consumers, candidate versus proof
availability, late invalidation, non-coinductive Contract evidence, missing intrinsic registration, Transferable
summary gates, D5/S5 boundaries, and whether unit size or approvals have been hidden.

Two independent reviewers examined the first draft and source. One requested changes on four blocking
clarifications; the other identified compatible clarifications. Revisions made before final review:

| Review clarification | Final disposition |
| --- | --- |
| Premise request key must not erase capability derivation purpose. | §4.3 preserves the distinct capability key explicitly. |
| Concrete recursive Contract prohibition is stronger than an unseeded proof cycle. | §4.3 and the test matrix distinguish it from valid abstract premises and other prerequisite cycles. |
| A static verdict may depend on definition context or pair case. | §5.3/T0 require a proof anchor and case-publication decision before T1/T2; no MIR re-proof fallback. |
| Indirect readers escape a Facts-only grep. | §2.4 includes CallableEffects and constructor identity shortcuts; the cutover census includes source-availability and hint readers. |
| Default acceptance authority must precede the behavior change. | PS1/PS3 now gate on D5/O3 disposition; PS5 verifies the approved outcome. |

Final document verdict (2026-10-20): both independent reviewers passed the revised plan; no blocking finding remains.
Their shorter projection-prefix alternative was rejected for the general reasons in §3. File references and named
test owners were checked. Implementation/prototype gates in §6–§7 remain unexecuted; this document review does not
constitute their evidence or lift R4-M83.
