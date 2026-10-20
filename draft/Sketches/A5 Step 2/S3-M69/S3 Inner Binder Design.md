# S3 option 2: fixed inner Origin binders and boundary substitution

Date: 2026-10-20. Unselected alternative, not an adopted compiler contract. Correction round 1 after independent review.
Baseline: `f373cd678ea3b8f6e4065c23b08029d17dea06e8`.
Scope: the closure Origin/alias/result publication gap called S3 in this request;
not R4-M74 OQ12's earlier S3 wording about inference candidates.
No implementation, build, runtime probe or timing measurement was performed for this document.

## Problem and evidence

SPEC 15.6.5 fixes capture, receiver and whole inferred-result Origins inside a closure body.
The capture's outer Origin can still be finite or inferred in its creating body.
SPEC 15.8.2 additionally distinguishes captured shared references, environment borrows,
exclusive reborrows, consuming transfers and argument-derived results.
Equality of Origin expressions never identifies or releases Loans (15.6.4).

The following are observed source facts, not claims that a new program was executed:

| Owning code, under `src/Kimi/Compiler/` | Observation and effect |
| --- | --- |
| `Binding/Binding.OriginProof.cs`, `FixedOrigin`, `JudgeOriginAtoms` | Fixed classification recognizes Static/Parameter/Input, recursively meets. It has no closure-body boundary input. |
| `Binding/Binding.Closures.cs`, `Capture`, `CaptureEntry` | Capture environment bindings initially reuse the source Type; a slot-borrow entry adds an outer Projection bound by the source declaration. |
| `Binding/BoundClosure.cs`, `BoundClosure.Of` | Publication has one signature, one environment Type and one minimum receiver. A separate inner Origin namespace is absent. |
| `Binding/Binding.Closures.cs`, `BindConcreteClosure`, `CallLocalOrigin` | Result Types are inferred, then a separate walk rejects call-local sources; it deliberately stops at nested Function Types. |
| `Binding/Binding.FunctionTypes.cs`, `ResultFixedForCall` | A second whole-Type walk scans captures and Copy proofs to reconstruct whether a result is independent of the receiver. |
| `Binding/Binding.ValueCalls.cs`, `BindValueCall`, `HasEnvironmentOrigin`, `FixedInBodyOrigin` | Results dependent on environment Origins remain `Unsupported_Kd`; fixed/per-call distinctions are reconstructed locally. |
| `Binding/Binding.OriginDeclarations.cs`, `ResolveInitializerOrigins`, `ResolveOrigin` | Alias replacements rewrite Types; completed initializer replacements also publish `RecordRegionBound` edges. These are not closure-boundary substitutions. |
| `Binding/Binding.OriginPremises.cs`, `CollectOriginResolvers`, `ExtractOriginPremises` | Proof uses lexical resolvers and R1-R5 premises. Anonymous results are excluded from ordinary named-function result premises; obligations cannot prove themselves. |
| `Binding/Binding.ClosureInstances.cs`, `ClosureSignature`; `TyCtxt.Substitutions.cs`, `SubstOfClosure` | Generic closure context is already an interned Subst; it describes the enclosing instance, not the closure's capture boundary. |
| `Mir/Build/MirBuilder.cs`, `Body`; `MirBuilder.Places.cs`, `Bound`; `MirBuilder.Values.cs`, `Closure` | Body receiver, capture projections and creation aggregate currently read the same published environment representation. |
| `Mir/Analysis/MirBorrows.Calls.cs`, `Call` | A temporary rule puts a value callee's environment into its result, pending C2's retention rows. |

The root cause is one declaration-level Origin identity doing two jobs without a published
boundary: naming the caller's region and serving as the callee body's rigid contract input.
Closure result/capture queries compensate separately. The forthcoming 2a0-D/F4 view cannot
make the distinction from `OriginKind` alone, and C2 must serve both A5 and R5.
This is a cross-phase representation change, not a local fixed-kind predicate repair.

Existing regression sources establish usable entry points:

- `ClosureResultOriginTest.ConcreteResultsKeepFixedExternalOrigins`: direct, slot, tuple,
  Option, array, enclosing input, borrowed closure and unrelated fresh argument forms.
- `ClosureResultOriginTest.AResultOverTheEnvironmentIsUnsupported`: the existing support
  boundary for receiver-dependent results, including nested closures and consuming transfer.
- `ClosureEnvironmentOriginTest.AResultOverCallLocalStorageIsRefuted`: parameter storage
  and consumed environment storage; expected located `UnsatisfiedOriginRelation_Kd`.
- `OriginContextualClosureTest`: fixed expected signatures, per-call input Origins and Loans.

Minimal positive/negative source pair to extend, not a newly reproduced defect:

```kimi
let n = 7
let r = n@ref
let keep = func [r] () => r
let good = keep()
let bad = func (x: i32) => x@ref
```

The positive body must see a rigid imported Origin while the creating body retains `n`'s
finite bound; the negative must retain a finite source distinct from its fixed result target.
Unverified hypotheses to probe are alias chains, admitted-case differences, result joins,
and stale publication after edits. Do not describe any as an observed unsound acceptance.

## Design chain and alternatives

Required meaning: closure boundaries fix inputs and outputs locally while preserving outer
dependencies and each result source's escape obligations -> the representation lacks a rigid
inner identity plus a defined instantiation into the creating/calling body -> Binding publishes
that identity in C2 and TyCtxt owns substitution; F4 judges published rows -> downstream capture
scans, syntactic fixedness guesses and the special result-escape walk become unnecessary.

Options compared inside this candidate:

1. Keep current identities and complete a single body-relative Origin view in Binding.
   This avoids cloned Origin-bearing Types and is probably smaller; it requires every atom
   classifier and every premise lookup to carry the correct body contract consistently.
2. Give the body's boundary Origins explicit fixed binders, with one substitution to the
   external contract. Ordinary fixed-atom proof then works after one normalization.
   This is this candidate's choice; the extra substitution and Type interning are its cost.
3. Add closure cases in `FixedOrigin`, `ResultFixedForCall` and MIR consumers independently.
   Reject: the same boundary rule remains distributed and cannot certify publication.

Option 2 should win only if its single normalization eliminates enough context handling and
consumer walks to offset additional Types/atoms; do not claim it is faster before measurement.

## Published contract

Extend the C2 `ClosureContract` proposal rather than adding an independent side table.
Its producer is `Binding.ClosureOrigins.cs`, a responsibility split from closure binding;
its public view is `TyCtxt.Closures.cs`. The plan/definition owns the retained immutable result.
Its inputs are completed capture acquisitions, header/result typing evidence, admitted cases,
lexical contract premises, and the enclosing generic context. It emits:

- An inner signature and inner environment Type using rigid boundary binders.
- The creation environment Type using the acquired outer Types, as today.
- One canonical Origin-slot sequence and its boundary substitution.
- Case-conditioned premises and obligations through 2a0-D's existing ER-row contract.
- Per-case call receiver and CallRetention through C2's existing contract.

No new `OriginKind` is required: hidden rigid slots use the existing Parameter form, bound by
the closure declaration, after its declared Origin slots. The published slot sequence, not
`function.Origins.Count`, owns their count; no negative sentinel or collision with Input slots.
Using Parameter for these slots is a proposed representation decision that needs review and
an implementing DECISIONS entry. It is not permission to silently change existing slot users.
The count/application census includes Calls, ValueCalls, CallableContracts, ContractMatching,
ResultPremises, OriginInference and Storage. Test composed substitution images, not just slot counts.

A boundary import is written `kappa_i`, with external image `sigma(kappa_i) = outer_i`.
One canonical, fully resolved outer atom gives one import in a boundary, even when two captures
or aliases name it. A meet imports its distinct atoms and retains the ordinary meet operation.
`static` stays `static`. Non-Origin generic slots retain ordinary Subst handling.
A capture's outer inference variable does not become fixed in the creating body.

Alias identity and boundary correspondence are different:

- Established aliases within one scope use canonical resolution and true equality rows.
- `sigma(kappa) = outer` is instantiation, never an equality edge connecting body graphs.
- No premise may make the inner fixed atom equal to an outer finite Origin in the inner graph.
- Captures with equal lifetime names still have separate acquisition/Loan dependencies.

The inner Type projection is applied consistently to header Types, capture binding Types,
all local/result Types, Origin rows and their diagnostic endpoints. The outer creation Type
remains external. This is not a MIR-only remapping after Binding has already checked a
partly outer body. Bind explicit captures before body uses; implicit capture discovery adds
an import through this same producer before the first use is typed. A second semantic pass
must not be needed merely to repair identities created during discovery.

Nested closures compose boundary substitutions; do not flatten them into the nearest named
function's context or steal a sibling's slots. Generic Subst composition and boundary
instantiation are ordered: instantiate the enclosing generic context, then instantiate the
closure's rigid contract at its call. `SubstOfClosure` remains the enclosing-instance query;
its meaning must not be widened to two incompatible kinds of substitution.
Reuse TyCtxt's substitution interning and application; expose the additional contract's
substitution by the C2 view, with no new general substitution engine.

Per-case receiver classification precedes final publication. Borrowed-receiver environment
Fields receive fixed receiver dependencies; consuming-receiver owned Fields are finite body
storage. Exclusive captured referents retain both their external dependencies and receiver
containment through the first shared layer. Case masks cover imports, premises and obligations;
a pair slot absent in a case contributes no atom or relation in that case. No union of cases
may upgrade a case-specific premise to a universal premise (SETTLED pair-slot rejection).

## Inferred and written results

Result positions include the outer Origin, nominal Origin arguments, components, pair slots,
arrays/slices, nested owned values and returned concrete closures. Traverse by existing Type
variance/storage rules; a nested Function's own quantifiers never become outer result slots.
All explicit returns, expression results and unreachable result sources participate (14.9).
An annotated or fixed expected signature uses ordinary result elision; a hidden receiver never
becomes nameable through annotation lookup. Do not alter result-only caller-chosen Origins
(SETTLED), common Function erasure, Callable contracts or Loan identity.

For omitted results, build the per-call result template and source constraints together.
A result slot has a fixed inner target for escape checking and an external per-call image.
Covariant sources constrain its meet; invariant sources require equal Origins. The whole
result target is outside the body, so a finite call-local source cannot satisfy it.
The source keeps its original finite/inferred identity; normalizing the destination must not
normalize the source to that same fixed target merely because their pre-normalization Type
was shared. Result-destination formation is an owning operation, not a global rewrite table.

Separate two inputs to the solver: established universal premises, and constraints to prove.
Return-source edges, alias declarations still being checked, and inferred result constraints
are never inserted into the premise oracle. A result dependency cannot prove its own source
relation. First solve the per-call template using 2a0-D's general scoped inference rules;
then independently judge each source against the fixed result target and publish only a
validated contract. Preserve the source graph used for escape checks while solving slots.
This result-formation algorithm is a pre-coding proof obligation: the current Origin prototype
explicitly excludes closures. Unit B below must demonstrate the fixed/fixed valid cases without
assuming their obligations, alongside finite-source refutation, before production integration.
If it cannot, reject this option, not add a source-shape allowlist or a fallback inference path.

At a concrete call, substitute captured external Origins, arguments and the receiver borrow
into the validated template. CallRetention describes value/content flows and writable receiver
storage, using the same instantiated Types and premises. It must preserve captured shared
reference independence, exclusive reborrow dependence, consuming transfers and argument Loans.
Never derive retention from which imported Origin has an equal spelling or numerical slot.

## Lifecycle and consumer contracts

`BoundClosure.Of` keeps R5-M63: publish only the current successfully completed definition
parts even when creation failed; clear stale signature/contract before a rebind.
Keep stable slot order and interned atoms across unchanged passes. An edit revokes C2/F4 views
through `TyCtxt.ResetViews`; obsolete substitution and Type objects remain immutable, as the
existing interner requires. Reused scratch arrays clear references outside their live ranges.
Do not let `originStateVersion` or pending initializer changes leave a cached contract valid.
Publish at the completed Binding boundary, never during a proof request or candidate trial.

Publication-only staging means an unconsumed C2 view while the active `BoundClosure.Signature`,
capture/body Types and legacy readers remain coherent. A projected shadow inner Type is not evidence
that Binding already checked a wholly inner body. The first consuming unit must promote inner body
checking, signature Types, slot counts, substitution and all call readers together. If this cannot
preserve existing accepted programs, combine those changes into one verified unit and re-estimate.
Retire the temporary unconsumed view/legacy readers at that promotion; add no second runtime mode.

The MIR builder consumes the inner Types/receiver when constructing the closure body and the
outer Types for its creation. `MirValidator` alone validates resulting MIR Type/row agreement;
A5 trusts the result. Missing resolved publication is `InternalInvariant_Kd`, not inference in
MIR; a valid form outside an implemented common rule remains located `Unsupported_Kd`.
R5's layout/instance representation must erase both Type views identically: no ABI change.

Deletions after equivalent checks pass: closure-specific capture scanning in
`ResultFixedForCall`, `HasEnvironmentOrigin` for closure-result dispatch, `CallLocalOrigin` and
its dedicated borrower search when F4 source rows fully replace it, and A5's temporary
value-callee environment-to-result rule. Retain non-closure function signature rules.
R4-M74 OQ11's Binding diagnostic twins remain until H11, and OQ4's old-analysis gate remains
until R6-check. These existing staged deletions must not be accelerated without their gates.

## Units and verification gates

| Unit | Concrete change | Exit evidence |
| --- | --- | --- |
| A: baseline census | Inventory capture/result Origin positions and slot-count consumers; record current inner and outer Types plus all source obligations for the source families below. | Normal-source probes, exact source/configuration identity; distinguish accepted, rejected and Unsupported; no claim from fabricated model inputs. |
| B: boundary design proof | Extend the closure-excluding Origin prototype with scoped imports, result formation and substitution; compare body-relative interpretation independently. | Valid captured/input/raw-unbounded results, finite-result rejection, invariant joins and cases all have SPEC verdicts; no self-proof; selected design reviewed and owner approves 2a0-D/2c-D package. |
| C: restructure only | Extract closure result/contract responsibility from over-limit `Binding.Closures.cs` and `Binding.FunctionTypes.cs`; organize slot application with the owning substitution code. | Separate behavior-preserving commit, required snapshot/reference checks, existing owning tests unchanged; no mechanical splitting or larger exception. |
| D: publication only | Implement stable slot sequence, projected inner Types, boundary Subst, result/source rows and memo reset in an unconsumed C2 view. Active Types/readers stay unchanged. | `TyCtxtTest`, `ConcreteClosureTest`, `GenericClosureIdentityTest`, `OriginContextualClosureTest`; identity/alias/case/rebind assertions and unchanged acceptance, error codes and spans. |
| E: consuming promotion | Atomically switch body checking, signature/result Types, slot counts/substitution and call readers; C2 retention serves A5/R5 (2c-2/3/5). Builder imports validated inner/outer views; remove replaced A5 carrying, preserve OQ4 gate. | `MirBorrowsTest`, `RetentionTest`, `FixedOriginRetentionTest`, closure result and generic dispatch/erasure tests; validator/census/ledger and unchanged default-pipeline verdicts. |
| E-BU: coordinated 0c | BU 0c (2c-4) branches from 2c-3's pushed branch and lands after it in the same G:A5-F7 merge, as AB-2 requires. | Pair-case acceptance in both views; A3-closure still rejected at `v@follow = x`. Separate verified commits are allowed; neither merges into dev/main independently before group review. |
| F: F4 activation | Activate closure-result source relations (2c-6) after 2a-1b, 2c-2 and 2c-3, with AB-2 group completion. Binding CallLocalOrigin remains until H11. | `ClosureEnvironmentOriginTest`, `ClosureResultOriginTest`, Origin premise/lifecycle tests and planned `MirOriginsTest`; all sources/variance/cases, no self-proof or duplicate default-pipeline report. |
| G: close | Independent final review, architecture/navigation/support facts and evidence; reconcile deletions with 2a0-2, 2c-2, 2c-5/6. | No blocking finding; Session and measurements; per-unit verified commits and push; owner chooses representation/whitelist/hold after two unsuccessful correction rounds. |

D-F are sequential in one worktree; publish no partially normalized body to a consumer.
Unit B must resolve the same lossless-source, body-side result binding, raw no-bound and scoped
inference gates recorded in the selected body-relative plan; fresh import atoms do not solve them.
An implementation finding that changes this contract returns to the planning session.

Required coverage extends the existing owning classes, not one new class per source spelling:
imports of finite and inferred outer Origins; explicit slot borrow; shared Copy and exclusive
reborrow; consuming reference transfer versus consumed storage borrow; aliases of imports;
multiple aliases sharing one atom but distinct Loans; explicit/expected/inferred results;
all result positions and unreachable returns; covariant meet/invariant disagreement;
64-view pair cases sharing mask classes; nested and sibling closures; generic instantiations;
call twice with different arguments; shared receiver moved while dependent result survives;
erasure and Callable refusal; return/store through a capture; edit and unchanged rebind.
Add proof-is-pure and no-obligation-as-premise assertions at the lowest observable layer.

Compiler units use focused `scripts/verify.ps1 -Mode Unit -Class <owning classes>` selections,
with mirrored MIR checks and the step-2 census/ledger where prescribed. Builder changes get the
R5-M8 sel check. Finish the implementation session with required Session verification; native
fixtures/milestones are explicit selections, never implicit coverage. NativeAOT is excluded
without separate instruction. The current documentation task needs diff/reference review only.

## Size, performance and decision gates

Planning estimates, not measurements, excluding tests/docs and existing F4/C2 common work:
+280-460 production lines for imports, slot publication, result normalization and lifecycle;
-120-190 lines for replaced closure-specific walkers/dispatch, net +90-340. Tests +220-380.
Count each retained migration twin and structure move honestly; re-estimate after unit A.
A single integer-keyed fixedness decision is cheaper than repeated body-relative classification,
but one extra inner Type graph and retained substitution can outweigh that gain.
Cold overhead should scale with distinct imported atoms and Origin-bearing Type nodes;
normalization work O(nodes + occurrences), with no scan of all captures per result occurrence.
Nested substitution must compose once per cached boundary, not once per ancestor per query.

Measure matched cold Bind+MIR and repeated unchanged Bind+Analyze workloads in `src/Benchmark`;
record elapsed time, allocated bytes and retained memory for increasing capture count, nesting,
result positions, aliases and distinct case masks. Reuse OriginProofWorkloads where applicable.
Keep warm 0 B in existing closure/environment/contextual tests and new C2/F4 views; retain no
syntax after invalidation beyond established interners. Record added interner entries and
capacities after edits to detect monotonic retention. Preserve native O2 allocation guarantees.
No speed or memory claim follows from code inspection alone; do not loosen checks to pass.

Approval: this representation changes a shared contract and needs the full process, including
independent alternatives, chosen-design review and final review. R4-M74 OQ2 still requires the
owner's family-package approval before implementation. New vocabulary, enlarged exceptions or
postponed removal stages require separate recorded owner approval; this design proposes none.
Record slot representation, responsibility and lifecycle decisions in DECISIONS with the first
implementing commit. Proposal selection does not itself mark compiler support implemented.
