# Kimigayo Implementation Status

Whole-value replacement adoption (2026-09-17): the change is integrated into
SPEC.md and its Type, constraint, expression, ownership, destruction, artifact,
and generation chapters. Section 15.7 owns the three APIs and shared storage
update dependencies; assignment refers to that section instead of repeating
the rules. The original draft is unchanged by this implementation work.

Implemented:

- Kimi.Sealed has an intrinsic Identity and four-valued proof. Classification
  checks the normalized outer Core, rejecting open structs, Never, and non-owner
  Semantics without recursively imposing constraints on fields. User conformance
  cannot manufacture evidence. Declared evidence validates generic object target
  formation and exact-Type explicit payload projection.
- Kimi.replace/exchange/swap are ordinary parsed generic declarations with
  validated compiler implementation identities. Named aliases, source shadowing,
  generic inference, external argument names, and textual evaluation order use
  the existing Binding pipeline. The catalog now validates 10 of 22 entries;
  catalog validation is separate from runtime coverage.
- Binding distinguishes explicit Sealed payload projection, complete payload
  receivers, and protected base projection. Fully specified handle/reference
  storage borrows retain their own Type layer. Custom accessor selection retains
  the same distinction without claiming completed accessor execution support.
- Ownership and LLVM lowering execute updates on complete mutable owner locals,
  including scalars, strings, and supported aggregates. Concrete scalar/struct/
  fixed-array uniq paths also have checked address/update plans when their
  contents are proven Owned. Target Loans begin before later arguments; ordinary
  writes/transfer plans retain original-location destruction and old-value
  responsibility. Exchange/swap transfers perform no user destruction.
- Returning an exchanged aggregate no longer mistakes the function's result slot
  for an input-Origin anchor. Scalar exclusive borrows use ordinary pointer
  storage. Replace does not reserve unnecessary old-value scratch storage.
- The existing tokenizer/parser syntax is sufficient: Sealed and update names
  remain identifiers, and typed @ adaptation and named-argument syntax are reused.
  Serialization/rebinding, invalid library edits, constraints, overlap, incomplete
  targets, argument order, aliases, destruction and Abort have regression coverage.

Implementation boundaries remain explicit: object allocation, object-borrow
runtime plans and payload owner retention are not yet available, so payload
projection binds but cannot pass executable ownership/lowering. General
ObjectCallCompatible effect inference/publication and generated accessor execution
also remain incomplete. Storage-polymorphic generic updates, general field/index
targets, nested reference/handle storage updates, and borrowed updates with
unproven Owned contents need additional plans. Owned is an implementation limit
on that borrowed lowering path, not a constraint on the specified Kimi APIs.
Unsupported paths are rejected; no no-op or approximate payload implementation is
emitted. The object tour and Milestone 14 show the normative behavior and remain
specification examples beyond executable coverage.

The executable WholeValueReplacement example covers ordinary updates and borrowed
struct replacement. Milestone 14 now uses an ordinary uniq projection of its
Sealed payload, exchanges contents without replacing the object, and records the
old payload's extra destruction in its expected output.

Validation: Debug and Release each pass all 8,237 managed tests with no build
warnings or errors. The WholeValue fixtures pass 30 native executions across O0
and O2, and Milestone 5 passes 47 Release integration checks. Parser regressions
cover all 56 example and Milestone Kimi files. The 28 specification Markdown
files have no unresolved local links or anchors; git diff --check also passes.
NativeAOT tests were not run. These checks cover the implemented paths above,
not the remaining object runtime and generic execution requirements.

Kimi library and named aliases (2026-09-17): the adopted change is integrated into
SPEC.md, Chapters 3, 9, 18, 20 and 22, their library references, and Appendices A,
E and F. Core remains the Type component. The separate Container nesting proposal
is not adopted. The existing Chapter 22 file path remains stable for draft links.

The compiler exposes its designated library as Kimi and places the existing output
function in its ordinary public Console group. KimiLibrary and KimiDeclaration
replace the internal library names. Analysis, lowering and emission retain the
original function Identity and ownership/runtime behavior. There is no reserved
Core reference or old output forwarding name.

Every defining module receives the mandatory Kimi default alias, together with
configured additions at one lookup stage. Saved ProjectFile additions remain
unchanged; redundant Kimi additions have the same effective settings as none.
Explicit aliases retain their earlier stage. Named aliases resolve root-based
Kotonoha/group references, retain source-document scope and original Identity,
reject unused conflicting mappings, and warn about distinct earlier root
qualifiers. Opening aliases retain direct-member behavior. Both forms reject
invalid placement, targets and unbound Container parameters. Generated sources
and dependencies retain their own environments; re-Bind invalidates cached
targets. Per-document name/member indexes and per-generation target caches avoid
repeated global scans; warmed alias Binding allocates zero bytes.

Examples, all fourteen Milestone sources, and their output test fixtures use the
new library paths. The NamedAliases example demonstrates named group/library
qualifiers and the mandatory default alias. Both Debug and Release pass all
8,170 managed tests (63 added), with zero build warnings/errors. Syntax checks
cover all 55 example/Milestone files. Milestone 1–11 pass 492 Release integration
checks, including LLVM verification and native O0/O2 execution; all eleven
reports match the final compiler and source hashes. The named-alias output fixture
passes both O0/O2 runs with exact UTF-8, LF, empty stderr and exit 0. Specification
file/heading links and git diff whitespace checks pass. Evidence is retained in
`bin/kimi-alias-verification.json` and the `bin/kimi-alias-*.log` files.

Existing implementation limits remain: the library catalog still lacks twelve
declarations; general function-item erasure is Binding-only; source-package
packing, content IDs and persistent semantic reuse are incomplete. The new
specification defines their alias/default-environment requirements without
claiming those broader facilities are implemented. Instantiated parent groups
require the separately unadopted Container placement rules. NativeAOT is excluded.

Earlier whole-value replacement proposal review (2026-09-17), preceding the
implementation entry above: reorganized the
[proposal](draft/Changes/2026-09-17%20Whole%20Value%20Replacement.md) in Japanese,
aligned ObjectCallCompatible and Kimi API names, and distinguished complete
payload calls from protected base-receiver calls and ObjectViewCompatible.
Consolidated dependency/destruction rules and added evaluation-order, rejected-use,
and payload-versus-handle swap examples. That earlier review was documentation-only.

Mixed-target ownership continuations (2026-09-17): straight-line and closed CFG checking-only
effects now preserve each original transfer target and pre-cleanup state through
enclosing joins. Assignments, Moves, supported calls and transfer chains retain
ordinary diagnostics without creating runtime arrivals or cleanup. Stored-borrow
liveness follows checking seed and replay links, including later uses across an
outer join. Completing conditional branches retain per-target common guarantees.
Closed cycles preserve zero-iteration paths and converge per constituent.
Short-circuit operands retain terminal paths and evaluated/skipped checking joins,
preserving initialization, Move/assignment histories and stored Loans without
inventing runtime arrivals or results after a noncompleting left operand.
Require failure continuations retain pre-cleanup state and transfer targets for
enclosing joins, including mixed-target prefixes, without feeding failure facts
into require's ordinary success successor.
Scalar defaults support require with validated scalar bodies and contained transfers.
Closed terminal if/else branches after mixed-target joins now retain independent
per-target prefixes. Continuation capture freezes its seed range before later
lexical cleanup can change the region entry.
Mixed-target logical expressions with a terminal RHS preserve its separate history;
the skipped path alone supplies the ordinary logical successor. A noncompleting
left operand retains the evaluated/skipped checking join without a runtime result.
Debug/Release suites pass 8,107 tests each; 136 new continuation fixtures pass O0/O2,
and all inputs of the 477 previously verified native fixtures still match.
Programs 1–11 pass all 984 integration checks across Debug/Release, with all
22 reports matching the final compiler and program identities.
Reload/warmed zero-allocation checks pass. Mixed-target partial-terminal branches,
partial logical operands, unequal Loan joins,
deferred cleanup and general effectful divergence remain incomplete. Detailed
execution evidence and remaining compiler work are maintained in PLAN.md.

ObjectCallCompatible implementation plan (2026-09-17): [SPEC.md](SPEC.md#objectcallcompatible)
records three stages: document the plan, implement inference/public summaries and
call checks, then detect guarantee loss across Package releases. The current stage
is documentation only; stages 2 and 3 are deferred pending further instructions.
Existing language rules and implementation coverage are unchanged.

Object compatibility terminology (2026-09-17): the specification now names the
call-operation guarantee ObjectCallCompatible and the runtime Contract View
eligibility predicate ObjectViewCompatible(C). Definitions and specification
references are aligned. This is a terminology-only documentation update; no
Attributes, compatibility rules, extension boundaries, or compiler behavior change.
ObjectCallCompatible body/callee verification remains incomplete, and runtime
Contract designation and View binding syntax remain deferred.

Milestone 11 integration (2026-09-17, complete): the unchanged
program passes Binding, ownership, LLVM verification, native O0/O2 and CLI execution
with stdout `Generic weights are 6, 3, 2.\n`, empty stderr and exit 0. Both builds
have zero warnings/errors; both full managed suites pass 7,881 tests (37 added).
Target integration passes 55 checks per configuration, including ten rejected
inputs. Four Debug/Release O0/O2 IR artifacts independently confirm three shared
bodies, nine entries, six forwarding adapters and one selected specialization.
Closed Type specializations are checked against their original input,
result, argument-name and Origin contracts and are excluded from overload choice.
Shared generic calls carry selected entry adapters without cloning the checked
definition. Indexed shared borrows require no Copy on T and retain bounds checks;
shared i32 addition checks overflow. Verified immutable group integer/bool literal
initializers can be folded because their initialization has no observable effect.
Effectful/mutable static storage, length/receiver/constrained specializations and
explicit specialization Origins remain unsupported. Dependent owned-result
forwarding still rejects generation. Related LLVM/native regression passes
**506 fixtures / 1,012 O0/O2 runs**, with **2,530 matching hashes**. Programs 1–11
pass **984 integration checks**; all 22 reports match current compiler/program
hashes. Target work and required verification are complete. Evidence is in
`bin/milestone11-work-20260917/verification.json` and `shared-generation.json`.
NativeAOT was not run as instructed; no later program was implemented. This
execution did not edit Milestone sources, SPEC.md or draft; independent specification
terminology changes observed during verification were preserved.

Milestone 9 integration (2026-09-17, complete): the unchanged generic search
program passes current-source Debug/Release builds, LLVM verification and ordinary
native O0/O2 execution. Exact stdout is
`Found 6 at index 3.\nMissing value handled.\nSearch finished.\nBatch destroyed.\n`,
with empty stderr and exit 0. Dedicated integration passes **59 checks per compiler
configuration**, including first/last/singleton/absent matches, empty/nonempty
exhaustion, i64 instantiation, Abort without cleanup and eight rejected inputs.

Finite symbolic enum ownership and shared tag/payload construction/results are
implemented. Shared range iteration retains scalar SSA snapshots and checked isize
addition; indexed reads use evaluated index values. Common-function calls keep
receiver Loans and acquired argument storage, with concrete entry adapters for the
callback ABI. A generic destructor that never observes receiver fields uses one
checked representation-independent body; instantiated field destruction follows.
Shared adapters currently accept direct scalar ABI parameters and bool/isize/Unit
results. Generic destructors that observe fields remain explicitly unsupported.
These implementation limits do not narrow language semantics.

Both builds have zero warnings/errors. Both full managed suites pass **7,844 tests**
(32 added). Related LLVM/native regression passes **492 fixtures / 984 O0/O2
executions**, with **2,460 matching fixture/expectation hashes**. Programs 1–10 pass
**874 integration checks**; all twenty reports match current compiler/source hashes.
Evidence is retained in `bin/milestone9-complete-20260917/verification.json` and
PLAN.md's P9 completion record. No target work or required verification remains.
NativeAOT was not run. SPEC.md, draft and all program sources are unchanged;
programs 11–14 were not implemented.

Previous Milestone 9 continuation (2026-09-17, incomplete target): checked common-function
invocation and direct anonymous-function conversion now support scalar/Unit snapshot
captures in the inline 8-byte environment. Owned function handles move and clean
up normally; calls hold a shared Loan through argument evaluation. Borrowed struct
fields can be returned with declared Origins, including symbolic `ref/T` fields
through shared offset metadata. Borrowed generic array reads require Copy and use
shared element-size/length policies, retaining bounds checks for zero-sized elements.
Both Debug/Release builds have zero warnings/errors, and both full managed suites
pass 7,812 tests (68 added). Related LLVM/native regression passes 471 fixtures /
942 unique O0/O2 runs, with 2,355 matching fixture/expectation hashes. Completed
programs 1–8 and 10 pass 756 Debug/Release integration checks; all eighteen reports
match current compiler/program hashes. Evaluation order, Never-body creation,
nonzero generic field offsets and explicit oversized-environment rejection are covered.
The unchanged target passes final Binding and the `Batch.view()`/generic element
ownership paths, but still fails ownership on `Hit<T>` result/construction. Target
LLVM generation and native execution remain UNVERIFIED. Shared enum results,
iteration/callback adaptation and generic destructor generation remain unfinished.
No target completion is claimed; NativeAOT was not run. Evidence and the exact
next action are in current PLAN.md and `bin/milestone9-final-20260917/`.

Milestone 11 static-member example (2026-09-17): Added the immutable i32 group Property `Weights.defaultWeight = 1` and used it in the generic default weight implementation. The expected output remains `Generic weights are 6, 3, 2.` README explains inherent group static membership, the single shared slot, and first-access initialization semantics. This is a specification example update only; no compiler capability checks, builds, or tests were performed. Language rules and SPEC.md are unchanged.

Milestone 10 integration (2026-09-17, complete): owned nested tuple/enum
patterns now bind distinct guard candidates and body locals. Scalar/Unit nested
candidate reads and owned tuple decomposition pass ownership analysis. Concrete
enum storage uses an explicit i32 tag and aligned active payload; construction,
whole-value transfer and active-case destruction have LLVM/O0/O2 evidence.
Composite pattern generation currently supports layouts without destruction,
retaining short-circuit tests and post-guard acquisition. Copy aggregate fixed-array
iteration reaches generation. The unchanged program passes LLVM verification,
ordinary native O0/O2 execution and CLI runs in Debug and Release. Exact stdout is
`Accepted total is 12.\nPattern run finished.\n`, with empty stderr and exit 0.
Dedicated integration passes 54 checks per configuration, including nine rejected
invalid inputs. Final Debug/Release builds have zero warnings/errors; both managed
suites pass 7,744 tests (44 added in this execution). Related native regression
passes 444 fixtures / 888 O0/O2 executions, with 2,220 matching IR/expectation
hashes. Completed programs 1–8 pass 648 integration checks; all eighteen program
reports match current compiler/source hashes. Evidence is retained in
`bin/milestone10-work-20260917/verification.json`. No target work or required
verification remains. NativeAOT was not run as instructed. See PLAN.md's separate
P10 items; program 9 was incomplete at that checkpoint and programs 11–14 were not implemented.
Composite matches with owned destruction or borrowed candidate paths remain
explicitly unsupported outside the completed target.

Milestone 9 continuation (2026-09-17): function calls now retain distinct length
and Type substitutions, infer fixed-array lengths from established inputs and
candidate-fitted literals, and substitute checked length expressions. Signature
formation conditions filter candidates; function bodies cannot introduce unproved
length requirements. Length-dependent whole-array Copy/Move and destruction use
the existing shared CFG with concrete policies. Owned and explicitly declared
shared borrowed arrays expose logical length through that policy, including
empty arrays and zero-sized elements. Borrowed entry arguments retain pointer
values in their own slots. Reanalysis, reload, invalid-plan rejection and warm
length-call Binding/ownership allocation checks pass. Shared module construction
is not claimed allocation-free. Dual-namespace explicit arguments across overloads
with different generic slot kinds remain explicitly unsupported.
At the program-9 checkpoint, both managed suites passed 7,700 tests (73 new in that continuation), with clean
Debug/Release solution builds. Type-only and length-only namespace lookup also
preserves transparent argument grouping. Related LLVM and O0/O2 native regression
passes 143 fixtures / 286 executions; all 715 regenerated IR/expectation hashes
match the verified inputs. Completed Milestones 1–8 pass 648 Debug/Release
integration checks, with all reports matching final compiler/source hashes.
Evidence is retained in `bin/milestone9-resume-20260917/verification.json`.
The execution stopped after approximately 66 minutes, completing only the already
running verification/documentation after its 60-minute soft limit. Complete
At that earlier checkpoint, Milestone 9 execution remained unverified; the next unit was checked common-function
invocation at `accepts(value)`, followed by captures and the remaining generic
read, borrowed-field, enum-result and iteration paths.

Program Milestone 9 integration (2026-09-17, incomplete): explicit typed value
borrows now derive omitted outer Origins from their operands. Borrowed concrete
fixed arrays support indices/length snapshots and checked scalar reads, including
nonnull storage for empty arrays, reference forwarding/results and exclusive
reference inspection without Move. Child reborrows suspend parent access.
At that earlier checkpoint, the unchanged target failed final Binding at callback invocation and lacked
capture/common-function integration. Its ownership, LLVM/native build, output,
exit status and cleanup are **not verified**. See PLAN.md's separate program-9
checkpoint and `bin/milestone9-work/` for reproduction and subset evidence.
This is an implemented foundation, not completion of Milestone 9 or the broader
plan stages. Specification semantics and milestone sources are unchanged.
The previous execution's verification counts are retained in PLAN.md; these
program-9 figures are historical, superseded by program-10 verification above.
NativeAOT was not run.
These results establish the implemented subset only.

Program Milestone 8 integration (2026-09-17, complete): the unchanged program
passes final Binding, universal ownership checking, LLVM verification/linking and
ordinary native O0/O2 execution. Exact stdout is
`Chosen item.\nBoxed array total is 12.\nGeneric scope finished.\n`, with empty
stderr and exit 0. Generic
constructors use existing member substitution; unknown-Copy field acquisition
retains conditional Move paths and rejects invalid definition-side reuse/deinit
extraction. Shared storage CFGs use concrete ABI entries and immutable layout,
Copy and destruction policies, deduplicated by symbolic type. Context slots are
8 bytes, fixed scratch excludes external/empty storage, and live flags are emitted
only for conditional destruction. Scalar Copy fixed-array iteration snapshots its
input once. Final Debug/Release builds have zero warnings/errors; both full suites
pass 7,567 tests each, including 40 new cases. Programs 1–8 pass 648 integration
checks across both configurations, including 46 program-8 checks each and thirteen
invalid-input cases. Related LLVM/O0/O2 regression passes 570 fixtures and 1,140
native executions; regenerated final fixtures match the saved native hashes.
Final integration reports match current compiler/source hashes. See PLAN.md's
separate program-8 checkpoint and `bin/milestone8-work/verification.json`.
No required check remains unverified; NativeAOT tests were not run. Language
semantics, draft and milestone source programs are unchanged. Generic forwarding,
compound symbolic fields, lengths, specialization, borrowed generic ABI and
general Iterable/Iterator support remain guarded outside this checkpoint.

Specification programs 10–14 (2026-09-17): Added [Milestone10–14](milestones/README.md#milestone-10-patterns-inside-result-producing-control-flow) combining nested enum/Tuple patterns and transfers, type/length-generic forwarding and explicit specialization, mutable/nested/consuming captures, a generic Slice-backed Iterator, and a callback pipeline with Kimi.makeObj and scoped object borrowing. README records expected outputs, rejection/Abort exercises, and the distinction between semantic output and evidence of physical generic code sharing. These additions are specification targets only; no compiler capability checks, builds, execution, or tests (including NativeAOT) were performed. Language rules are unchanged; SPEC.md links to the expanded series.

Ownership checking continuations (2026-09-17): noncompleting do scopes,
if conditions and terminal branches preserve source initialization, Move history
and Borrow state without adding runtime successors or fabricated results. Separate
checking joins intersect initialization guarantees and union possible histories;
all predecessor states must be available and active comparison-Loan stacks must
agree. Ordinary completing branches may feed a later scope termination, and every
function-terminal branch of a completing selection (a partial early return, Never
call or divergent loop followed by later termination) is recorded and joined by the
enclosing scope or terminal selection, so its Move/initialization history reaches
the later dead source. Completing loop/while bodies now retain their escaping
terminal paths. Each pending checking seed carries its transfer target; exits,
continues and yields handled inside a construct are removed before its state
reaches an enclosing join. Transfers in dead source preserve the original path's
target, including through later Never calls and divergence. Joins with different
targets support local checking. An unchanged mixed join retains its constituent
seeds and targets for enclosing constructs to filter independently. Bare dead
return/exit/continue chains retain that provenance before implicit cleanup;
subsequent checking effects still guard propagation. No runtime edge or result
arrival is added. Final builds are clean and all 7,594 managed tests pass in each
configuration, including 27 new cases and zero warmed allocation checks. Native
verification includes 380 Never + 414 Default executions, then 32 new bare-transfer
executions; prior fixture hashes match final regeneration. Milestone 1/8 passes
25/46 checks per configuration at unit 31 (see PLAN.md units 31–32).

Noncompleting loops with only scalar/Unit loop-local effects preserve enclosing
facts. A noncompleting while condition preserves the state after its argument
acquisitions when the body has only local scalar effects. These paths work in
supported scalar defaults and through do wrappers. Every default declaration still
requires independent checking, including when its argument is supplied.

Current limits include outer-mutating or owned/effectful divergent loop bodies,
effects after mixed-target continuation joins, deferred-cleanup joins, unequal active
Loan joins, short-circuit conditions, recursive-default
completion proofs and general owned/borrowed defaults. They remain guarded; M2/M3 are incomplete. PLAN.md units
21–30 record the verified slices and the next resumption case.

The earlier continuation checkpoint had clean Debug/Release builds and 7,455 passing tests each. All default
and noncompletion fixtures per configuration pass ordinary native O0/O2, including
the eight new partial-scope fixtures per configuration. Milestone 1 passes 25 checks
per configuration. Rebind/reload and warmed analysis/emission checks pass with zero
measured allocation. No NativeAOT testing was performed.

Scalar operand arrival (2026-09-17): unary/binary operations produce no result
when an operand supplies none. Later source operands still receive ordinary
checking, including assignment effects. This applies to typed nested calls and
admitted scalar default expressions. Full Debug/Release suites pass 7,294 each,
two fixtures/configuration pass O0/O2, and warmed result-state/reload checks pass.
Direct Never operand fitting still has earlier Binding limitations.

Omitted-default completion (2026-09-17): selected defaults now contribute their
completion to caller flow without changing the declared result Type or callee-body
completion. Cached declaration checks work before or after the declaration and
include cleanup that prevents delivery. Structural completion includes omitted
defaults; recursive expansion remains pending and bounded. Full Debug/Release
suites pass 7,282 each with clean builds, five fixtures/configuration pass O0/O2,
and warmed flow reanalysis measures zero allocation.

Incomplete call acquisitions (2026-09-17): a Never explicit/default argument
prevents caller result initialization, including nested scalar, string and aggregate
calls. Checking-only argument effects and Borrow release remain available. Stored
and borrowed result validators require proof before accepting absent normal results.
Full Debug/Release suites pass 7,267 each; four new fixtures/configuration and five
existing stored-result/nonreturning fixtures pass O0/O2. Nested-result rebind,
reload and zero-allocation warm checks pass.

Default initialization checking (2026-09-17): every supported scalar/Unit default
is checked independently from initialized preceding prepared parameters, including
unused, fully supplied and bodyless requirement declarations. Scalar/Unit locals
may omit an initializer or use a Never initializer; ordinary ownership checks reject
reads without a supplied value. A reusable scratch CFG shares the ordinary solver
without adding executable callee bodies or destroying prepared arguments. Full
Debug/Release suites pass 7,253 each, all 86 default fixtures per configuration pass
O0/O2, and warmed valid/invalid declaration checks allocate zero measured bytes.
General owned/effectful defaults and divergent continuation joins remain unfinished.

State-neutral loop continuations (2026-09-17): source after `loop => ()` or a
loop containing only a bare continue to itself uses the unchanged loop-entry
ownership facts. A Never initializer supplies no initialized value. Twelve new
cases pass; full Debug/Release suites pass 7,235 each and four fixtures per
configuration pass bounded O0/O2 execution. Result-state, reload and zero-allocation
warm checks pass. Effectful/branching divergent loops remain unsupported where a
checking continuation is needed.

Nonreturning-call continuations (2026-09-17): ordinary Never calls now preserve
acquired-argument initialization and Move facts for later source checking. Their
temporary argument Loans end in the checking graph; runtime execution gains no
successor, result initialization or cleanup. Sixteen new tests pass, full
Debug/Release suites pass 7,223 each, eight native fixtures per configuration pass
O0/O2, and warmed analysis/emission measures zero allocation. General divergent
loop/selection continuations and declaration-side default initialization checking
remain unfinished.

Final continuation verification (2026-09-17): seven completed default-expression
units add 91 tests. Debug/Release builds are clean, all 7,207 tests pass in each
configuration, all 79 default fixtures pass O0/O2 per configuration, and Milestone 1
passes 25 checks each. The detailed limits and next action are recorded in PLAN.md;
this is a verified subset, not completion of the compiler plan.

Prepared scalar subplace defaults (2026-09-17): defaults can Copy scalar/Unit
values from owned fields, tuples and fixed arrays in preceding prepared arguments.
Reads retain the selected call's argument identity, finish their inspection Loan
before callee entry, and retain declaration bounds-Abort locations. Eleven new
cases pass; clean Debug/Release suites pass 7,207 each, native O0/O2 checks cover
all nine new fixtures, and warm element reads measure zero allocation. This run
adds 91 tests across seven units. Generic Copy proofs, escaping borrows/captures,
noncompleting local-initializer checking and general owned/effectful defaults remain
unfinished; the mixed nested tuple/array Binding case remains an earlier limit.

Mutable scalar locals in defaults (2026-09-17): initialized default-local var
bindings support assignment, numeric/bit updates, increments and finite while/loop
computation. Writes are restricted to locals inside the same default; prepared
parameters remain immutable. Twelve new cases pass; clean Debug/Release full
suites pass 7,196 each, nine fixtures pass O0/O2 per configuration, and warmed
analysis/emission still allocates zero measured bytes. Uninitialized/noncompleting
local-initializer checking, general effects and owned/borrowed defaults remain open.

Unit defaults (2026-09-17): Unit literals, preceding Unit copies, local Unit
bindings and supported control-flow results now use the established zero-sized
argument path. No physical value is added for Unit. Eleven new tests pass; full
Debug/Release suites pass 7,184 each, ten fixtures pass O0/O2 per configuration,
and warmed Unit default analysis/emission measures zero allocation.

Immutable scalar locals in defaults (2026-09-17): sequential default bodies may
initialize scalar let bindings and read them through ordinary local storage.
Prepared arguments retain independent snapshots across local scopes and repeated
calls. Thirteen new cases pass; full Debug/Release suites pass 7,173 each and eight
fixtures pass O0/O2 per configuration. Warm analysis/emission with local bindings
allocates zero measured bytes. Mutable/uninitialized locals, noncompleting local
initializer checking continuations and effectful/owned defaults remain unsupported.

Contained scalar default transfers (2026-09-17): single-expression default bodies
can use loop, internal exit/continue and selection yield with existing scalar
operations. Transfers stay inside the default. A noncompleting default skips later
defaults and the callee; checked arithmetic retains declaration Abort locations.
Eleven new cases pass; clean Debug/Release builds and full suites pass 7,160 each.
All eleven fixtures pass O0/O2 in both configurations. Warm compound default
ownership/emission remains allocation-free. General local/effectful/owned defaults
and their cleanup remain unfinished.

Default Move diagnostics (2026-09-17): definite non-Copy acquisition of a
preceding prepared argument now reports `DefaultArgumentMove_Kd` at declaration
time, including unused, required-initializer, fully supplied and bodyless
requirement defaults. Checks use committed call acquisitions and lexical Copy
proofs; legal Copy/shared inspection is preserved. Eighteen new tests and full
Debug/Release suites pass (7,134 each), and both CLI builds report the source error.
Owned field/tuple/array subplaces and aggregate/enum payload acquisitions are also
checked (15 more cases; full suites pass 7,149 each). Borrowed referents remain
outside the owned-path check. Mutation, escaping borrows and general default execution
remain unfinished; this diagnostic pass is not an execution certificate.

Scalar do defaults and final verification (2026-09-17): a single-expression do
default can use the supported scalar operations, selections and conversions.
Nested/chained do results retain prepared argument snapshots. Jumps, loops,
effectful calls, generic and owned/borrowed defaults remain unsupported.
Rebind and source reload invalidate execution until analysis rebuilds the plans,
then reproduce identical IR. This continuation adds 66 tests in total; final
Debug/Release builds are clean and full suites pass 7,116 each without skips.
All 32 default fixtures pass native O0/O2 in each configuration; Milestone 1
passes 25 checks each. Warm ownership/emission with do, selection and conversion
defaults measures zero allocation. General default ownership/cleanup is unfinished.

Scalar selections in defaults (2026-09-17): value-producing if/else defaults
support scalar conditions and single-expression arms, including nested selections
and reads of earlier default results. All arms require supported operations even
when unreachable or supplied. Six new cases and full Debug/Release suites pass
(7,110 each); five new fixtures pass native O0/O2 in both configurations.

Scalar conversions in defaults (2026-09-17): defaults now reuse established
identity, literal and numeric conversion plans. Each conversion preserves its
range check, rounding and source location, including chained conversions.
Nine new cases pass; full Debug/Release suites pass 7,104 cases each and all
nine new fixtures pass native O0/O2 in both configurations. The warmed default
ownership/emission test includes conversion chains and still allocates zero bytes.

Scalar default execution (2026-09-17): nongeneric calls acquire explicit arguments
first, then omitted scalar defaults in declaration order. Defaults can read
prepared preceding scalar values and use literals, arithmetic, bit operations,
comparisons, unary operators and short-circuit boolean expressions. Reads retain
argument snapshots across branches. Supplied defaults and required initializers
do not execute; noncompleting arguments skip defaults and the callee. Checked
arithmetic retains the declaration expression's Abort location. Other default
forms, generic defaults and default ownership/borrow effects remain unsupported.
Twenty-one new cases and full Debug/Release suites pass (7,095 each). Thirteen
default fixtures pass O0/O2 per configuration, 28 existing function fixtures
pass O0/O2 from Release, and Milestone 1 passes 25 checks per configuration.
Warmed default ownership/IR generation measures zero allocation.

Default-expression control flow (2026-09-17): defaults are checked as independent
value expressions even on unused/bodyless declarations or fully supplied calls.
Their transfers may target constructs inside the default but cannot escape into
enclosing function or loop bodies. A noncompleting default does not change the
callee body's completion. Twelve new cases and full Debug/Release suites pass
(7,074 each), including allocation-free warmed control-flow reanalysis.

Omitted-default call plans (2026-09-17): selected calls now retain omitted
defaults in parameter declaration order after explicit arguments. Each entry
keeps its declaration expression and parameter Symbol together with the
substituted parameter Type/Origins. Named argument and receiver mappings remain
in source order; defaults supply no inference or overload-selection evidence.
Rebind clears or reuses plan storage, and reload rebuilds source identities.
Twelve new cases and full Debug/Release suites pass (7,062 each), including
zero allocation in warmed repeated Binding. The metadata itself does not certify
ownership, cleanup or execution; the scalar execution slice is recorded above.

Projected-call diagnostic boundary (2026-09-17): inherited borrowed-receiver
method calls whose effect verification is still unimplemented now report
`UnsupportedBinding_Kd`. Their receiver plans keep internal Unknown proof,
and emission remains rejected without overload reselection. This does not
implement public ObjectCallCompatible summaries. The three existing adaptation
cases were corrected and strengthened; 204 focused cases and full Debug/Release
suites (7,050 each) pass.

Source validity and diagnostic lifetime (2026-09-17): both source parsing entry
points now retain newly reported syntax/lexing errors independently of the
diagnostic destination and later display clearing. Duplicate-location reports
still invalidate source; pre-existing Binding errors and warnings alone do not.
Seven new boundary tests and full Debug/Release suites pass (7,050 cases each).
Milestone 1 passes 25 LLVM/native/CLI checks in each configuration.

Source-change invalidation (2026-09-17): appending source through `AddSource` or
`CodeContext.Parse`, and reloading a source snapshot (including an empty one),
now revokes prior Binding/startup/ownership results and retained conformance and
Property certificates. Emission writes nothing until the complete current
pipeline runs again. This closes stale-IR acceptance of newly invalid declarations
and empty replacement trees. Seven new tests and full Debug/Release suites pass
(7,043 cases each); the existing native Milestone 1 smoke test also passes in
both configurations. This covers in-memory source changes; persistent semantic
artifact reuse remains a separate implementation item.

Unresolved-conformance diagnostics (2026-09-17): an isolated unresolved
`Self is Missing` clause now reports its missing Name without repeating derived
errors on the owning Type, a sibling Copy clause, or its Properties. Binding
failures and unverified certificates remain intact. Other constraint inputs,
independent errors, separate missing Names, and invalid uses remain diagnosed;
recorded causes are rebuilt on rebind. Nine new tests cover these boundaries,
fragments, reload and zero allocation in warmed repeated Binding. Full Debug
and Release suites pass 7,036 tests each with no failures or skips.

Unavailable declaration modifiers (2026-09-17): `virtual`, `override`, and
`abstract` now produce `UnavailableFeature_Kd` at the first unavailable modifier
in a declaration header, including container/function/Property declarations,
constructors, deinit, Contract requirements, and inline or block accessors.
Recovery skips the invalid declaration or accessor and preserves following
independent items. Recognition stops at item boundaries; the same spellings
remain ordinary Names in declarations, calls, and member access. The 40 new
parser cases and full Debug/Release suites pass (7,027 tests each, zero failures
or skips). This implements the existing rule in SPEC §2.5.1; it introduces no
new valid modifier or reserved word.

Merged-container diagnostic locations (2026-09-17): a declaration container
created while parsing a source document now retains that first declaring
fragment's CodeContext instead of the parent's source-less root context, so a
container-level Binding failure (for example an invalid conformance list) is
reported at the container header in its document (`file.kimi:1:1`) rather than
as a document-less `Project:@0` location. Members and later fragments keep their
own contexts as before. Verified by the new
`SourceDocumentAndDiagnosticTest.MergedContainerRetainsFirstFragmentSourceDocument`
and the full Debug/Release suites (6,987 tests each, zero failures); the
regenerated scalar fixtures are byte-identical to the pre-change set. The
former sibling-clause and field cascade after one unresolved conformance name
is resolved as described above. The location regression now uses contradictory
input premises to retain an independently required container-header diagnostic.

Program Milestone 7 complete (2026-09-17): the unchanged
`milestones/Milestone7.kimi` passes Binding, ownership, LLVM verification, linking
and ordinary Windows x64 native execution. Fixed arrays and full Slice views
provide independent `indices` snapshots; the built-in ResolvedRange iteration
path acquires its range once and supplies immutable isize bindings. Nested for
loops reuse the existing transfer and cleanup CFG. Full Slice handles preserve
backing Origins through Copy, check scalar reads against their stored length,
reject overlapping mutation/Move while needed, and permit statically disjoint
row writes. Slice lifetime checks include index evaluation and temporary-owner
expiry. No intermediate inner-array Copy or heap allocation for view storage is
introduced. Range/Slice handles have two-word physical layouts.

Exact stdout is `Row finished.\nRow finished.\nRow finished.\nMatrix total is 42.\nBorrowed row total is 20.\n`,
with empty stderr and exit 0. `test-milestone7.ps1` checks the original and
byte-identical renamed O0/O2 inputs through native execution and both CLI run
forms, alternative arithmetic, outer exit and exhaustion paths, matrix/Slice
bounds Abort with exact diagnostics and exit 1, and thirteen rejected inputs.
The checked-in milestone sources are unchanged.

Debug/Release solution builds pass with zero warnings/errors; both full managed
suites pass 7,527 tests each with no failures/skips. Forty added sequence
tests cover empty/nonempty/nested arrays, single receiver/index evaluation,
metadata and handle copies, NLL, lifetime and write/Move rejection, disjoint
storage, transfer cleanup, scalar representations, reanalysis, and malformed
generation plans. The milestone script passes all 52 checks per configuration;
completed Milestones 1–6 pass 226 checks per configuration. Native regression
passes LLVM verification and 1,106 O0/O2 runs across 19 Sequence, 466 Element,
18 Struct and 50 Reference fixtures. All 2,765 fixture/expectation files from the
final Release suite match the saved Debug/native input hashes. Logs and source/
fixture identities are retained under `bin/milestone7-work/`. Program-7 reports:
`bin/milestone7/Debug/dabe834f49cf46f1aea7b8b66c29c4c9/verification.json` and
`bin/milestone7/Release/602c33a1a4694ec0897c46e9377e418d/verification.json`.
No required verification remains blocked or unverified. No new throughput
benchmark was performed.

This is the program-7 subset, not completion of PLAN.md's M8/I19/I20. General
Kimi sequence declarations and explicit Type APIs, Index/from-end/bounded Range
operations, general user Iterable/Iterator dispatch, direct array/Slice iteration,
tuple iteration bindings and generic sequence ABI remain unimplemented. The
supported Slice reads are scalar Copy reads; other element-result forms remain
guarded. Mixed Slice provenance conservatively widens its static footprint.
Programs 8–9 were read for dependencies and not implemented. This task does not
change the language rules or drafts; NativeAOT is not run. Concurrent additions
of Milestones 10–14 and their SPEC/README index entries were preserved and were
not implemented or verified by this task.

Program Milestone 6 complete (2026-09-17): the unchanged
`milestones/Milestone6.kimi` passes Binding, ownership, LLVM verification, native
linking and execution with the existing compiler implementation. Value-producing
loop/if/do, guarded match, continue, yield, named exit and require already compose
correctly through result acquisition, cleanup and scalar SSA joins. No compiler
source change or new language feature was needed. The first attempted build
reached LLVM generation but could not run the sandboxed LLVM version probe;
granting local native-tool execution permission resolved that environment failure.

Exact stdout is `Selected 7.\nControl flow passed.\n`, stderr is empty, exit 0.
`test-milestone6.ps1` adds 63 CLI/native checks per configuration: the original
single-source input and renamed O0/O2 copies, alternate search threshold, guard
evaluation side effects, both selection paths, both validation failure paths,
transfer cleanup and Abort suppression of pending cleanup. Twelve invalid inputs
are rejected before IR/executable publication, including missing/wrong transfer
targets, incompatible or implicit Unit results, unreachable mismatched results,
continuing require failure, non-bool guard, missing match coverage and escaped
pattern binding. Debug/Release builds have zero warnings/errors; each full managed
suite passes 6,986 tests with no skips. The focused baseline passed 602 tests.
Both milestone script runs pass, with source/compiler/build identities retained at
`bin/milestone6/Debug/98a3947647c4494cb4666f78a2c01896/verification.json` and
`bin/milestone6/Release/d945c54bb6b340d798c484200e300889/verification.json`.

Related result, match and guard fixtures pass 40, 144 and 78 O0/O2 native checks,
respectively. Completed programs 1–5 pass their 25, 21, 37, 33 and 47 checks,
respectively, against both current-source compiler configurations. No blocker or
required unverified check remains. Program 6 is distinct from PLAN.md's broader
stages. All nine source programs were read for dependencies, but no later program
was implemented. Source programs, SPEC and drafts remain unchanged. NativeAOT
was not run.

Program Milestone 5 complete (2026-09-17): the unchanged
`milestones/Milestone5.kimi` passes Binding, ownership, LLVM verification, native
linking and execution at O0/O2. Eligible structs receive synthesized construction;
explicit struct borrows, returned input Origins, constructor Origin inference,
borrowed method receivers and field reads/writes now reach native code. Borrow
dependencies survive calls, storage and Move. Deinit keeps the view's shared Loan
active until destruction; mutation and ownership transfer resume afterward.
Reborrow conflicts, temporary-owner escapes and unreachable-code violations are
rejected. Existing projection-aware string Loan checks remain in effect.

Expected stdout is exactly
`Borrowed sum is 55.\nView destroyed; counter is still 55.\nFinal value is 56.\nCounter destroyed.\nDone.\n`,
stderr is empty, exit 0. Debug/Release solution builds have zero warnings/errors;
both full managed suites pass 6,986 tests without skips, including 31 new tests.
Ten new fixtures pass 20 O0/O2 native checks; existing reference, string-guard and
element-borrow fixtures pass 100, 52 and 208 checks respectively. The milestone
script passes 47 checks per configuration, including renamed inputs, changed
counts, immediate temporary borrows, two Abort variants and fourteen rejected
inputs. Abort output confirms that defer/deinit are not run during termination.
Reports under `bin/milestone5/<configuration>/<run-id>/` retain exact output and
source/compiler/build identities. Final run IDs are Debug
`711f7e5100bb4995b356f84739f3efda` and Release
`2eaa26fcd8bf4c6dae5e390a9403549c`.
Milestones 1–4 also pass their 25, 21, 37 and 33 checks, respectively, against both
final compiler configurations. No required check remains unverified and no blocker remains.

General borrowed aggregate operations, accessors and generic/inherited structs
remain separate work. This checkpoint concerns program 5, independently of
PLAN.md's broader stages; earlier checkpoint entries below are historical.
No specification change was required. Concurrent specification programs 6–9 and
their documentation were preserved without implementing them. Drafts and NativeAOT
were untouched.

Specification programs 6–9 (2026-09-16): Added [Milestone6–9](milestones/README.md#milestone-6-value-producing-control-flow) covering value-producing loop/if/do and guarded match, nested fixed-array iteration and cross-loop cleanup, nested groups with generic ownership transfer, and length/type-generic search with Copy constraints, callbacks, enum results, and borrowed storage. README records expected outputs and optional rejection/Abort exercises. These are specification targets; no compiler capability checks, builds, execution, or tests (including NativeAOT) were performed for these additions. Language rules are unchanged; SPEC.md links to the program series.

Program Milestone 4 complete (2026-09-16): the unchanged
`milestones/Milestone4.kimi` now passes Binding, ownership, LLVM verification,
native linking and execution. The first failure was outdated rejection of a
single-item deinit Body followed by unsupported constructor Binding. Explicit
constructors and their special field storage now check initialization and
completeness at each successful exit, before and after cleanup. Owned struct
parameters/results reuse aggregate slots. Field reads/updates retain initialization
and access protection; whole-value Move transfers responsibility and partial Move
through a deinit-bearing ancestor is rejected. Native destruction runs deinit,
then fields in reverse logical order, without repeating construction or cleanup
at the moved source. Descriptor pooling preserves distinct destructor identities.

Expected stdout is exactly
`Counter created.\nSum is 55.\nLeaving finish.\nCounter destroyed.\nDone.\n`,
stderr is empty, exit 0. `test-milestone4.ps1` checks the original input plus renamed
O0/O2 copies, an Abort sum variant (only creation output, exit 1, no defer/deinit),
and a destructor that inspects the final value. It also rejects twelve invalid
construction/Move/access/destruction inputs before IR/executable publication.
Reports and source/compiler/build identities remain under
`bin/milestone4/<configuration>/<run-id>/`.

Debug/Release builds pass with zero warnings/errors and both full managed suites
pass 6,955 tests. The 39 new struct tests include reload/rebinding, initialization
diagnostics, normal/conditional Move, replacement, nested destruction, and zero
measured warm ownership/IR allocations. Eighteen fixtures pass 36 O0/O2 native
checks, including exact owned-string release counts and construction Abort.
The milestone script passes 33 checks per Debug/Release compiler. Existing aggregate
function, element-Move and deferred fixtures pass 118, 130 and 50 O0/O2 native checks
respectively, including bounded nontermination. Milestones 1, 2 and 3 pass their
25, 21 and 37 checks in both configurations. No required check remains unverified
and no blocker remains. Final target reports are under Debug run
`96fe216328304e22bdfdade8ebeb5b11` and Release run
`36e2455629f148d493225c8d36c93485` in the report directory above.

SPEC already prescribes these semantics and is unchanged. Drafts and NativeAOT
were untouched. Generic/inherited structs, synthesized construction, structure
methods, borrowed receivers and general accessors remain explicitly outside this
execution subset. Milestone 5 was read for scope only; its implementation has not
been started. Program numbering is independent of PLAN.md's broader stages.

Program Milestone 3 complete (2026-09-16): the unmodified
`milestones/Milestone3.kimi` passes final Binding, ownership, LLVM verification,
native linking and execution with the current compiler. Existing explicit-main,
function argument/result, return/defer and Milestone 2 Abort support were
sufficient; no compiler or SPEC changes were needed. The initial native attempt
failed only at the sandbox's LLVM execution permission and passed once local tool
execution was allowed.

Added `backend/windows-x64/test-milestone3.ps1`: Debug/Release each pass 37 checks,
including actual-source and renamed O0/O2 builds, native/CLI execution and ten
rejected argument/result/startup/defer/ownership inputs. Successful stdout is
exactly `Leaving sumTo.\nSum is 55.\nLeaving main.\n`, stderr empty, exit 0.
`sumTo(-1)` produces no stdout or deferred messages and Aborts at 5:9 with exit 1.
`sumTo(9)` prints only `Leaving sumTo.\n` before the caller's Abort at 18:9.
A separate deferred-mutation variant confirms the returned i32 is secured before
cleanup changes its source. Variants never alter the checked-in milestone file.
Reports, diagnostics and source/compiler/build identities are retained under
`bin/milestone3/<configuration>/<run-id>/`.

Both solution builds pass with zero warnings/errors and both full managed suites
pass 6,916 tests, zero failures/skips. Existing function and defer fixtures pass
56 and 50 O0/O2 native checks respectively, including bounded nontermination
checks. Completed Milestones 1 and 2 pass their 25 and 21 checks respectively in
both configurations. No required check remains unverified. NativeAOT and draft
edits were not performed. Milestones 4–5 were read only; this checkpoint makes no
completion claim for them. See PLAN.md's program checkpoint and milestones/README.md
for reproduction commands; program numbers are independent of PLAN's M1–M17.

Program Milestone 2 complete (2026-09-16): implemented explicit `$abort(expression)`
through Binding, ownership, LLVM generation and ordinary Windows x64 execution.
The reserved builtin expects one owned string, evaluates/acquires it once, has
Type Never, and is independent of user `abort` declarations; no Kimi API was added.
Its runtime emits the source location and `KIMI_E_ABORT` followed by unchanged
UTF-8 message bytes and LF, then exits 1 without normal message destruction or
enclosing cleanup. A failed stderr write still exits without recursive diagnostics.
Argument return, nested Abort and checked arithmetic failure retain their own
outcomes. Explicit Abort's checking-only continuation diagnoses moved/uninitialized
uses in unreachable source. Never conditions retain no fabricated Boolean value,
and a noncompleting while produces no value storage.

The unmodified `milestones/Milestone2.kimi` builds and runs at O0/O2 with exact
stdout `Sum is 55.\nDone.\n`, empty stderr and exit 0. Separate `expected = 54`
variants have empty stdout, report `KIMI_E_ABORT: Unexpected sum` at 13:5 and exit 1.
`backend/windows-x64/test-milestone2.ps1` passes 21 checks per Debug/Release compiler,
including renamed byte-identical input, CLI forwarding and six rejected inputs.
Reports and compiler/source/build identities are in
`bin/milestone2/<configuration>/<run-id>/`.

Both solution builds pass with zero warnings/errors; all 6,916 managed tests pass
per configuration, zero failures/skips. The 38 new Abort tests cover positive,
negative, ownership, reload and runtime-plan cases; 15 fixtures pass 30 O0/O2 native
executions. Existing Counter/overflow native regressions pass 12 executions and
output/runtime-adapter regressions pass 68. Milestone 1 passes 25 checks in both
configurations. Warm ownership/IR generation allocates zero measured bytes; full
warm Binding on the reload fixture measured 72 bytes/pass and was not optimized.
No required check remains unverified. SPEC §§17.3/22.5 already prescribe this
behavior and were not weakened or changed. Drafts and NativeAOT were untouched.
Programs 3–5 were read only and remain unverified targets in this milestone series.
See the current program checkpoint in PLAN.md for reproduction and scope.

Program Milestone 1 complete (2026-09-16): the unmodified
`milestones/Milestone1.kimi` passes source parsing, Binding/startup selection,
ownership analysis, LLVM generation/verification, native linking and execution
using the current compiler. The first native attempt failed only because the
execution sandbox denied `opt.exe`; allowing the local toolchain execution
resolved that failure. No compiler implementation change or SPEC amendment was
needed: §§22.2/22.4 already define the implemented behavior.

Added `backend/windows-x64/test-milestone1.ps1` for repeatable actual-source and
byte-identical renamed-source verification. Debug/Release each pass 25 checks:
O0/O2 native execution and CLI forwarding, exact UTF-8 `Hello, world!` plus LF,
empty stderr and exit 0; owned local Move, Unicode/NUL/empty output; and ten
rejected type/argument/ownership/startup inputs before emission. Borrowed output
is rejected at the existing unsupported-Binding boundary; this does not claim
more precise overload diagnostics or general borrowing support. Build records,
compiler/source hashes, rejection diagnostics and byte results are retained in
`bin/milestone1/<configuration>/<run-id>/`.

Both solution builds have zero warnings/errors; both full managed suites pass
6,878 tests with zero failures/skips. Existing Release emission/runtime regression
coverage passes 68 native executions at O0/O2, including output faults and cleanup.
No required Milestone 1 check remains unverified. NativeAOT was not run. Programs
2–5 were read for dependencies only, with no implementation or completion claim;
their numbers are independent of PLAN.md's M1–M17. See the program checkpoint at
the top of PLAN.md and `milestones/README.md` for scope and reproduction commands.

Dependency API renames (2026-09-15): Updated call sites to match the upgraded packages: Arc.Collections registration APIs, Arc.Threading termination waits, Arc.Unit logging configuration, empty console, notification and console input interfaces, SimpleCommandLine parser options, Tinyhand map header reads, and Benchmark's Arc.Crypto/FarmHash calls. Updated related comments and the test console implementation. `dotnet build Kimigayo.slnx --no-restore --nologo -v:q` passed with no warnings or errors; `dotnet test --project xUnitTest/xUnitTest.csproj --no-build --no-restore` passed all 4,870 tests. CLI `--help` output and normal exit were also verified. Language rules, CLI syntax, and data formats are unchanged, so the SPEC body needed no update. NativeAOT tests and performance measurements were not run.

Updated: 2026-09-15. Baseline reviewed: `e861ce5ebf7c365f8e8416e0ed692500eb9b1f42`. Static element partial Moves are covered in §4.8/§7.4; static string element comparisons and shared arguments in §4.9/§7.5; element borrowing from owned parameters and temporaries in §4.10/§7.6; and partial Moves from owned parameters in §4.11/§7.7.

Document structure (2026-09-14): [SPEC.md](SPEC.md) is the main index; the 22 chapters and Appendices A, B, D, E, and F are split into `spec/`. Appendix C is consolidated into the index's implementation-status guide. The `doc/` directory for designs, decisions, and change records was renamed to `draft/`. Chapter numbers, specification text, and existing precedence rules are preserved. Compiler coverage is unchanged.

Test verification failure behavior (2026-09-15): In [§17.5](spec/17-failure-handling.md#175-test-verification-operations), `$expect` evaluates its condition once, records failure if false, and continues. `$require` no longer returns normally on failure: it records failure, cleans up condition and message temporaries, then Aborts the case's child process. Both operations share the same permitted locations in test-only bodies, including helpers, local functions, Closures, and defer. Normal cleanup does not run after Abort begins; the runner retains both verification failures and the actual termination reason. Aligned the SPEC index, Chapters 14, 17, and 22, and Appendices A and F. These are documentation changes only; they add no compiler or runner implementation support.

Design document alignment (2026-09-15): Integrated test declarations, verification operations, discovery/CLI, and execution/recovery/diagnostics into [§6.5.1](spec/06-declarations-and-containers.md#651-test-definitions), [§17.5](spec/17-failure-handling.md#175-test-verification-operations), [§20.9](spec/20-compilation-configuration.md#209-test-command-and-discovery), and [§22.6](spec/22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting). Aligned existing input/generation rules and Appendices A, D, E, and F. Concrete formats, defaults, and additional features remain in Appendix D.4. Removed adoption and precedence references to the withdrawn Composition Root proposal and deleted decision documents; Entry/Provider selection remains unsettled. Also synchronized outdated pending-integration notes, earlier if/match proposals, block→do, object headers/metadata, and Appendix F public API names with the current SPEC. This documentation-only work adds no test language, runner, or Composition implementation support. Links, headings, code fences, and diff formatting were checked; compiler/native/NativeAOT tests and performance measurements were not run.

Dependency and artifact specification integration (2026-09-15): Gave precedence to the designated draft and integrated configuration, resolution, locks, input records, source packages, pack/publish, semantic verification reuse, and test membership into [Chapter 18](spec/18-modules-and-dependencies.md). Aligned native requirements/supplies, member closure, directives, and CLI in [Chapter 20](spec/20-compilation-configuration.md), shared generation and product/test budgets in [Chapter 21](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation), and related appendices and index entries. Clarified English rules and examples, replacing outdated statements that formats/configuration were undefined and passages that delegated the specification body to drafts. Checked 681 local references across all 28 specification files, code blocks, and diff formatting. These documentation changes do not establish completion of external dependency resolution, pack/publish, or persistent semantic caching. Drafts and compiler code were unchanged; compiler/native/NativeAOT tests and performance measurements were not run.

This document records coverage verified against current source and related tests. Lexing/parsing, Binding, ownership, LLVM generation, and native execution are separate stages; support in an earlier stage does not establish executability. See [SPEC.md](SPEC.md) for the specification and [PLAN.md](PLAN.md) for the overall plan. Legacy C.* links point to their corresponding areas.

Receiver shorthand (2026-09-15): Binds `self` in instance functions and Contract function requirements as `self: ref/Self`. Instance Property `get()` / `set(value: T)` accessors receive `ref/Self` / `uniq/Self` receivers, respectively. Supports stored custom, computed, and explicit Contract accessors; group/rootgroup accessors remain receiver-free. Preserves explicit receivers, argument positions, Origin completion, and classification of ordinary functions without receivers as type functions. Updated §7.3, §11.2, and the syntax appendix. All 390 related ReceiverShorthand / PropertyRevisionParse / PropertyBinding / FuncDeclarationParse / InheritedReceiverBinding / ContractBinding / TypeBinding / KotonohaSerialization tests passed, covering shorthand parse/write/parse, save/reload, and re-Bind. Warm Binding with shorthand allocated 0 additional bytes. Existing limits on general accessor call expression checking, ownership, generation, and specialization remain; this change does not extend their execution support. NativeAOT tests were not run.

<a id="c1-coverage-summary"></a>
<a id="c3-lexical-forms-and-types"></a>
<a id="c8-specification-review-integration-2026-09-08"></a>
<a id="c9-follow-up-specification-review-items-16"></a>
<a id="c10-type-and-literal-review-items-314"></a>
<a id="c14-tokenizeparse-increment-2026-09-09"></a>
<a id="c15-property-and-move-syntax-update-2026-09-10"></a>
<a id="c32-compile-time-switch-spelling-2026-09-12"></a>

## 1. Lexing, Parsing, and Compilation Conditions

`kimi check <input>` runs semantic checks without producing artifacts or invoking native tools. Project-only dependency graphs use exact identities, fixed configuration/source bytes, iterative cycle/conflict checks and direct reference mappings. Binding, control flow and ownership inspect every dependency body in its defining module, including settings, source aliases and separately staged default aliases; transitive modules are not exposed by name. Library runtime bodies are rejected. Product checks exclude explicit `TestSources` without reading them and skip test dependency resolution. Inline `#Test` functions are excluded from product symbols, body checking, startup, ownership and emission; selected syntax-level eligibility and target errors still diagnose. Tests cannot be called through ordinary product lookup. Reload/rebind and dependency modules retain this membership, and seven native fixtures verify product behavior at O0/O2. Test discovery and full test-body verification remain unfinished. Invalid identities, duplicate decoded names and nonempty legacy `KotonohaArray` receive configuration/migration errors.

`kimi restore <project.kimiproj>` records deterministic product/test lock partitions, preserves an unchanged lock, and replaces changed results atomically under exclusive ownership. Failed resolution records unresolved partitions. Product commands validate required locks, including existing locks for empty dependency graphs, and never rewrite them; bare `--locked` is supported. Source edits trigger fresh semantic checks without lock changes. Package loading, persistent processing/semantic records, test compilation and cross-module native generation remain unfinished. Existing unsupported ownership and generation boundaries still apply. Module Binding/ownership reuse has zero measured warm allocations in the focused scenario; no general allocation claim is made for graph loading.

| Verified implementation | Limits and evidence |
| --- | --- |
| UTF-8 source, original positions/diagnostics, identifiers and NFC validation pinned to Unicode 15.0, indentation/brackets/continuation lines, and recovery | [Lexing](Kimi/Compiler/Lexing), [SourceDocument](Kimi/Compiler/Core/SourceDocument.cs), UnicodeIdentifier / SourceEncoding / ParserRegression tests |
| Koto construction for types, generics/lengths/Origins, declarations/Properties/accessors, captures, literals/collections, expressions, and control flow; parse/write/parse and save/reload | [Parsing](Kimi/Compiler/Parsing), FrontEndSyntax / PropertyRevisionParse / NestedTypeParse / KotonohaSerialization. Syntax support does not establish complete semantic analysis or execution. Source artifacts and portable interchange formats are also separate concerns |
| Preserves numeric source text and exact magnitude; rounds float literals directly to target precision; validates character/string escapes | [NumberLiteralHelper](Kimi/Compiler/Helper/NumberLiteralHelper.cs), [FloatingTypes](Kimi/Compiler/FloatingTypes.cs), NumberLiteral / CharLiteralParse / StringLiteralParse |
| `#if` / `#switch` selection and condition validation; condition Names are case-sensitive, with exact duplicates and built-in collisions rejected | [Compilation](Kimi/Compiler/Core/Compilation.cs), [condition evaluation](Kimi/Compiler/Parsing/BasicValue/CompileTimeConditionEvaluator.cs), CompilationSpecification / DirectiveConditionValidation. Distinguishes validation of short-circuited/unselected Cases from exclusion boundaries for false `#if` branches |

Current syntax uses `#switch`, Properties/accessors, and ordinary type adaptation. Legacy `#match` and dedicated `@move` syntax are not supported as compatibility forms. `alias` opens a Container; it does not introduce a type alias.

<a id="c4-declarations-and-compile-time-directives"></a>
<a id="c5-properties"></a>
<a id="c16-initial-binding-pipeline-2026-09-10"></a>
<a id="c17-complete-type-representation-and-declaration-origins-2026-09-10"></a>
<a id="c18-constraint-proof-foundation-and-declaration-validation-2026-09-11"></a>
<a id="c19-core-intrinsic-identities-and-copy--owned-2026-09-11"></a>
<a id="c20-static-function-contracts-and-associated-types-2026-09-11"></a>
<a id="c21-property-conditional-conformance-and-inherited-member-binding-2026-09-11"></a>
<a id="c26-enum-case-construction-and-coreoptionresult-2026-09-11"></a>
<a id="c28-positional-pattern-binding-and-match-coverage-2026-09-11"></a>
<a id="c33-runtime-type-test-binding-2026-09-12"></a>

## 2. Binding, Types, and Kimi

Final expression validation retains associated projection qualifier constraints and conformance proofs that normalization removes from explicit generic call arguments and runtime-test targets. Invalid inputs or late-invalid witnesses revoke their call/runtime-test certificates. Valid nested Types, dependent caller evidence, independent member errors, source reload, replacement recovery and zero measured warm Binding allocations are covered by `ExpressionInputFormationBindingTest` and `ExpressionProjectionCertificateBindingTest`; general generic body proofs remain incomplete.

Named function and Property signatures, enum payloads and direct-base projections also retain final conformance validity. Late failures propagate through dependent declaration and conformance certificates until declaration states stabilize, independent of source order. Invalid projected bases invalidate descendants; invalid enum payloads revoke construction plans. Valid self projections retain their evidence. `SignatureProjectionCertificateBindingTest` and `AggregateProjectionCertificateBindingTest` cover these checks, reload, repair and zero measured warm allocations.

Ordinary, conditional and fixed associated-Type specifications retain late projection conformance validity as well. Invalid specifications revoke matching conformance certificates while independent paths remain verified. `AssociatedProjectionCertificateBindingTest` covers inherited fixed definitions, nested Types, source order, self references, repair/reload and zero measured warm allocations.

Conditional conformance premises also retain projection qualifier and witness validity. Invalid premises revoke their conformance/Copy target and conditional member applicability while independent conformance paths and ordinary containing-Type formation remain separate. The shared declaration-context guard includes invalid conditional scopes, so enclosed computed Properties cannot retain verified certificates after premise failure. Final call completion rechecks the selected function's substituted constraints and member conditions after late evidence changes. `ConditionalProjectionCertificateBindingTest` covers nested/dependent premises, clause/source order, Copy, direct members and computed Properties, independent outer Properties, reload/repair, provisional/final passes and zero measured warm Binding allocations.

Conditional conformance requirements and their retained projection domains must also cover the effective Type/Contract intersection for each ancestor path. Restricted Contracts, concrete Types and projection qualifiers cannot be exposed by a wider conformance. `ConditionalPremiseAccessBindingTest` covers valid narrower intersections, struct/enum declarations, ancestor obligations, independent paths, reload/recovery and zero measured warm allocations. Functions and computed Properties in conditional blocks independently check their published premises against their own effective API domains; a narrower conformance does not narrow a public member. `ConditionalMemberAccessBindingTest` covers member/witness/call certificates, narrow/enclosing domains, independent siblings, repair/reload and zero measured warm allocations.

Ordinary function, struct, enum and Contract constraint projections retain the same proof requirements before dependent declaration certificates are exposed. `ConstraintProjectionCertificateBindingTest` covers unused function declarations, conformance certificates, definition-side evidence, source order, repair and reload. Constraint applicability normalizes associated-Type identities after substitution for calls without receivers as well as member calls. `ProjectedConstraintCallBindingTest` covers direct/nested requirements, Boolean combinations, caller evidence, invalid qualifiers/witnesses, reload, retained-plan recovery and zero measured warm Binding allocations. General generic body proofs and generation remain incomplete.

Constructed structs and enums enforce input clauses whose subjects are associated-Type projections, such as `T.Origin.Item is i32`, in addition to direct generic-parameter clauses. Existing substitution and proof rules apply to every admitted input clause. `AssociatedSubjectConstraintBindingTest` covers identities/capabilities/Boolean requirements, nested Types, dependent caller evidence, generic-call and enum-construction certificates, reload/repair and zero measured warm allocations.

Constraint proof queries and available premises compare normalized associated-Type identities, including nested Types and exact compound assumptions. Equivalent concrete and projected requirements therefore share evidence, and their directly contradictory polarities produce Error. Symbolic subject identities, Type structure and the limited Boolean proof rules remain intact; normalization does not add case analysis or infer a satisfying Type. `NormalizedConstraintProofBindingTest` covers direct/substituted proofs, dependent calls, Unknown boundaries, rebinding, source reload, replacement recovery and zero measured warm allocations. Projection-free environments retain direct fact lookups. General generic proof and generation completion remain open.

Function Type conditions use complete Type parsing on both sides of `is`, including one-parameter Function Parameter Lists, nested/right-associated arrows and Boolean requirements. Binding preserves Function Parameter List arity: optional trailing commas preserve identity, zero parameters differ from one Unit parameter, and one Tuple parameter differs from several parameters. `FunctionTypeConstraintBindingTest` and `FunctionParameterIdentityBindingTest` cover Owned/non-Copy behavior, identity/subtyping, overloads, generic call/substitution checks, declaration domains, replacement, writing/reload and zero measured warm allocations. Common Function Value generation remains a later milestone.

Structs and enums admit closed named, primitive, constructed, Tuple, fixed-array, Semantics-applied and concrete-rooted associated-projection Types as Constraint Clause subjects. These clauses must be proved as declaration obligations; they do not become generic assumptions or declare conformance for the named Type. Refuted obligations invalidate the owning declaration and dependent conformance, Property and call certificates, including late failures and either source order. Subject Types must cover the declaration API domain. `ClosedTypeConstraintBindingTest` covers positive/negative obligations, absent conformance, Boolean requirements, access, reload/replacement and zero measured warm allocations. `StructuredConstraintSubjectBindingTest` covers balanced Type-subject parsing, nested generic formation, canonical writing, reload, API access, malformed-input recovery and unchanged executable/function prefix recognition. `ClosedProjectionConstraintBindingTest` additionally verifies retained qualifier input constraints and API domains, normalization, late invalidation, replacement/reload and zero measured warm allocations. Dependent compound Type subjects in struct/enum Constraints become generic input propositions and are discharged after substitution at uses. `DependentCompoundConstraintBindingTest` covers arrays, Tuples, constructed identities, Boolean requirements, nested associated projections, generic forwarding, Copy derivation, API domains, replacement/reload and zero measured warm allocations. Parentheses around Self preserve conformance and Copy declaration behavior. Input dependence considers both operands and Boolean subpropositions, so a closed subject with a dependent requirement (for example, i32 is T) is checked at each use. `DependentRequirementConstraintBindingTest` covers substitution/normalization, contradictions, malformed formation, forwarding, enums, replacement/reload and zero measured warm allocations. Function subjects retain SPEC 7.4 restrictions to their own generic parameters and rooted associated projections. Contract bodies likewise discharge closed conditions independently, including concrete-rooted projections. Only propositions depending on the conforming Self become implementation premises; a closed condition cannot prove its own missing or cyclic conformance. Refuted conditions invalidate the Contract, its refinements and conformance certificates. An ordinary Self clause referencing an invalid Contract or required Type declaration also invalidates its implementing Type and dependent consumer certificates. Local witness failures retain independent conformance paths. Dependent compound and reversed associated-identity conditions remain implementation obligations. Contract subjects also cover their API domain, and grouped Self cannot declare refinement. `ClosedContractConstraintBindingTest` and `ContractConstraintSubjectBindingTest` cover these distinctions, replacement/reload and zero measured warm allocations. `ModuleBindingTest` covers these complete-Type conditions across explicit/default imports, module reload and source replacement, including revocation of consumer call certificates. Further import cases cover Function Parameter List identity, closed Function obligations, late-invalid refined premises and appended dependency Contracts. Provisional refinement tests also cover imported pending/independent evidence, newly supplied parent Contracts and qualified/root parent Name validation across module reload.

Closed conditions that remain unknown during provisional Binding leave their Type declaration unresolved and withhold dependent conformance, Property and call completion. Final Binding recomputes absence and appended evidence. `ClosedConstraintLifecycleBindingTest` covers positive/negative transitions, Contract conditions, declaration order, valid appended fragments, reload and zero measured warm allocations. Conformance verification also discharges the implementing struct/enum closed prerequisites inside the active proof-query guard: named-self and mutual registration cycles cannot certify themselves, while independent finite Boolean evidence remains usable. `ClosedConformancePrerequisiteBindingTest` covers these cases, rebind/reload and zero measured warm allocations. Pending ancestor conditions also leave child Contracts unresolved, including diamonds, so exact child assumptions and their calls cannot bypass the dependency. `PendingRefinementDeclarationBindingTest` covers source order, missing names versus absent conformance, final rejection/recovery, error precedence, conformance certificates, reload and warm allocations. Absent refinement parents remain pending during provisional Binding. Contract shapes retain incomplete-parent state through descendants; known names still resolve, but incomplete shapes cannot supply conformance or refinement evidence. Qualified parent Names are resolved through transparent syntax wrappers. Complete parent syntax is checked before lookup, including root-qualified names; generic arguments, applied Semantics and grouped-Type forms cannot leave a resolved Contract declaration. `RefinementNameBindingTest` covers declaration identity, malformed syntax, provisional rejection, rebind/reload and warm allocations. `UnresolvedRefinementBindingTest` verifies appended requirements, wrong parents/cycles, final deadlines, independent assumptions, reload and warm allocations. These are internal Binding guarantees; the external Mod host and generated-source persistence remain separate work.

Missing Type/Contract names in provisional Constraint subjects or requirements retain an explicit Unresolved proposition until reanalysis. Incomplete propositions are not assumptions, and a successful Boolean branch cannot complete a declaration or call before all requirement syntax is formed. Available non-Type/invalid inputs and function-subject restrictions remain errors. `UnresolvedConstraintBindingTest` covers source additions, final rejection, nested Types, Boolean formation, calls, reload and zero measured warm provisional allocations.

Direct proof queries retain Unknown for complete operands containing Types with unresolved declaration conditions, including borrows, Function Types and capability queries through generic identities. Error retains priority over Unknown, and unrelated resolved operands keep their ordinary judgments. `UnresolvedCapabilityProofBindingTest` covers these prerequisites, final failure/recovery, reload and zero measured warm query allocations.

Associated-Type Contract premises also supply intrinsic Copy/Owned evidence through refinement during symbolic capability queries. The query checks current provenance and prerequisite validity without eagerly expanding recursive associated-Type families. `AssociatedRefinementCapabilityBindingTest` and module cases cover direct/indirect inheritance, nested projections, pending dependencies, conjunction/disjunction boundaries, generic calls, reload and warm allocations. Derived Constraint facts retain their defining Contract separately from direct input assumptions. Refinement and inherited associated requirements supply evidence only while their closed declaration prerequisites and ancestor prerequisites are proven; pending evidence cannot create a premature contradiction. Independent direct facts and alternate valid refinement paths remain usable. Proof queries, associated identity normalization and overlap environments preserve this provenance. Ancestor availability checks traverse the existing deduplicated closure once; conformance paths are verified in dependency order with reusable per-query proof scratch and existing identity membership for prerequisite joins, so temporary Unknown results do not cause recursive re-expansion. `DeepRefinementPremiseBindingTest` covers 32/64-level chains/diamonds, deep pending/cyclic prerequisites and zero measured warm generic/conformance allocations. `ProvisionalContractPremiseBindingTest` covers pending/complete transitions, negative evidence, calls, reload and zero measured warm Binding allocations.

Late declaration failures also revalidate expanded Constraint environments in the certificate propagation loop. A failed Contract condition invalidates dependent generic declarations and their call evidence, while unrelated valid environments remain usable. `LateConstraintEnvironmentBindingTest` covers refinement-derived Copy premises, generic Type certificates, declaration order, provisional/final passes, recovery/reload and zero measured warm Binding allocations.

Current [Compilation.Bind](Kimi/Compiler/Core/Compilation.cs) performs final Binding once per invocation; re-Bind invalidates old semantic information and ownership verification results. It uses types, Symbols, and call plans on Koto plus reusable tables, without creating a separate Bound tree.

| Area | Verified coverage | Remaining limits |
| --- | --- | --- |
| Names and calls | Separate Type/Value lookup, source-local scopes/aliases, forward functions, Kimi identity, visibility, positional/named arguments, static default Type checking with preceding-parameter lookup, ordinary generic inference and candidate selection | Full access/fragment validation, default argument ownership/execution, general Origin inference, specialization, constructors/deinit, and indirect calls remain incomplete. Defaults do not infer generic arguments; self/later parameters are unavailable in their declaration environment |
| Declaration API accessibility | Recursive complete-Type exposure checks on named member-function parameters/results, generic struct/enum constraints, enum payloads and Property header/storage Types, using effective enclosing domains. Property headers (including inferred Types) retain the Property domain when accessors are restricted; requirement headers inherit the Contract domain. Additional accessor receiver/input/result Types use their own domains. Computed and requirement accessors share ordinary Self receiver validation; static accessors reject receivers, including in unused declarations. Conditional members use their declaring Container for receiver identity. Named member-function/Contract requirement parameter-result, explicit Property header, and explicit accessor receiver/input/result projections retain their qualifier, selected Contract and defining-requirement domains after normalization; invalid Property signatures cannot publish verified Properties or conformance witnesses; named-function API validation precedes final conformance certification, so rejected function signatures cannot retain verified witnesses; concrete bindings still undergo ordinary Type/conformance access checks. Parsed member-function/requirement and struct/enum/Contract constraints also retain projection domains on subjects and requirements, including associated requirements; invalid Contract constraints prevent conformance certification. Ordinary and conditional associated specifications retain qualifier/requirement projection access under the Type/Contract intersection, with independent ancestor checks; invalid paths cannot certify. Enum payload projections retain these domains before certification, including payloads bound early by inferred headers; invalid cases/enums cannot publish valid construction plans or conformances. Direct-base projections likewise retain these domains against the derived Type before inheritance validation; invalid bases propagate to descendants and prevent conformance certification. Private implementation storage remains separate. Unit/Tuple requirements and projections following constructed qualifiers now reach these checks, preserving requirement Boolean grouping and precedence. | Does not export source-local functions or infer named function result Types. Inferred Property Types are checked recursively after normalization; projection syntax confined to initializers/private helpers does not itself become an API component. Unused dependency APIs and explicit/default alias projections retain their defining domains, with module reload coverage. Remaining general access/fragment conformance audits still need completion. ApiAccessBinding, PropertyApiAccessBinding, AccessorReceiverBinding, ProjectionApiAccessBinding, PropertyProjectionApiAccessBinding, ConstraintProjectionApiAccessBinding, AssociatedSpecificationAccessBindingTest, InferredPropertyAccessBindingTest, EnumProjectionAccessBindingTest, BaseProjectionAccessBindingTest and RequirementTypeParsingTest cover private/internal/protected combinations, recursive payload/header Types, reload/replacement and zero measured warm Binding allocations |
| Declaration fragments | Same-name Types with different generic arities keep separate symbols, headers, members and Attributes. Matching arities merge with kind, semantic modifier and access agreement after defaults; enums/contracts cannot split at the same identity. One defining base survives omitted clauses and resolves with its source-local aliases. Explicit group headers determine synthesized path access. Selected Attributes survive generic/base header parsing. Origin headers match ordered names and bounds: under the current source grammar, valid split-struct bounds identify shared declaration slots or static, independently of source-local Type aliases. Exact bound identity, conflicts, source order, rebind/reload/writing and zero measured warm unbounded-fragment Binding allocations are covered | Origin bound proofs remain unfinished: bounded declarations still report Unsupported, and unknown targets are rejected. General logical storage ordering, generated-fragment finalization and proof-dependent artifacts remain incomplete |
| Type arity selection | Explicit and zero-arity Type uses select within the committed lexical, qualified, root, explicit-import or default-import stage; wrong nearer arities cannot fall back to outer/default candidates. Same-arity imports remain ambiguous. Generic Type member/base lookup, separate source fragments, canonical writing, replacement/rebinding, and module-local identities are covered | This does not complete omitted-argument inference or universal generic proofs, and does not enable nominal/generic native generation |
| Layout Attribute validation | Struct-only targets; one unnamed, non-interpolated literal mode (`C` or `Kimigayo`); duplicate-per-fragment and conflicting-mode errors; C instance Fields restricted to one selected source fragment. Matching explicit modes and omission merge, and canonical writing emits matching Layout specifications once. Diagnostics retain source locations; invalid Layout targets and merged declarations invalidate before inheritance/conformance certification, with rebind recovery. Directive exclusion and product Test syntax checks are covered | This is source validation, not nominal layout or ABI support. C open/derived/empty/zero-sized/alignment formation rules, instantiated layouts, generated-fragment provenance/finalization, and physical offsets remain M5/M8 work. Nominal generation remains unsupported |
| Complete Type and Origin representation | Nested Semantics, owner normalization, Tuples/functions/fixed arrays, generic pairs/slots, declaration Origins and input/static/intersection Origins, assignment/variance, and retained-borrow representation | Bound proofs, a general Origin/Loan solver, general SemanticsTarget application, and length inference remain incomplete. Unresolved obligations are retained |
| Original Semantics-pair grouping | For a declared `<s/T>` pair, `s/(T)` and repeated target grouping reconstruct the same WholeType as `s/T`, including nested Types and call-result Origins. Grouping nodes retain the bound projection. Different target bindings and inner Origin annotations retain ordinary validation. Reload/writing, syntax replacement, duplicate signatures and zero measured warm Binding allocations are covered by PairGroupingBindingTest | Does not complete explicit Origin annotations on original pairs, arbitrary Semantics application, universal role proofs, ownership or generic generation |
| Pair Origin annotation representation | `s/T from a` and `(s/T) from a` retain the same annotated WholeType representation without adding a Semantics layer. Insufficient owner/unsafe application evidence cannot certify an annotated WholeType or application. Invalid Origin names/mappings retain ordinary diagnostics; unannotated applications keep their existing proofs. PairAnnotationBindingTest covers nested Types, independent annotations and reload/rebind/writing | Full annotation support remains incomplete: valid borrow annotations still retain unresolved definition obligations. Borrow-role proofs, substituted inner-Origin dependencies, value fitting and Loans need further work; passing safety tests does not certify these programs or native generation |
| Grouped generic Type arguments | Fixed arrays nested inside grouped Type arguments, such as `Box<([2 of i32])>`, parse as Types instead of numeric length expressions. Constructed Types and explicit function Type arguments preserve nested arrays, Tuples, pointers, common Function Types and borrow Origins. Invalid non-Type/incomplete arguments still reject. GenericTypeArgumentBindingTest covers reload/writing, identity, length-expression syntax and zero measured warm Binding allocations | This does not complete general generic argument validity, explicit length calls, generic inference/proofs or native generic generation |
| Fixed-array length constants | Eligible integer let locals and static stored Properties, qualified names and accessible standard getters; recursive initializer evaluation, checked arithmetic in the established integer Type, literal-only subtree fitting, all 12 integer widths including 128-bit intermediates, and final nonnegative-isize checks. Private constants expand into values in symbolic length expressions. | Does not execute getters or initialize static storage. Calls, var, parameters, instance Fields, custom/computed accessors, cycles, and inaccessible constants cannot supply lengths. Generic instantiation, separate-compilation records, and general length inference remain incomplete. ConstantLengthBinding covers reload/rebinding, zero measured warm Binding allocations, and ordinary native fixed-array execution. |
| Local fixed-array inference | Explicit fixed shapes ending in `_` infer a unique element Type from initializer elements or an independently typed array; direct literals fit established evidence. Nested shapes, Copy/Move, cleanup order, evaluation count, rebind/reload and zero measured warm Binding allocations are covered, with 21 ordinary native fixtures | Empty untyped arrays, conflicting evidence and shape errors are rejected. General collection/control-flow/generic inference remains incomplete |
| Fixed-array call arguments | Candidate-local fitting handles fixed shapes, nested arrays/Tuples and element literals; established inner expressions retain their Types. Candidate-order independence, numeric ambiguity, named evaluation order, string cleanup, reload and zero measured warm Binding allocations are covered, with 21 ordinary native fixtures | General length/element generic inference, dynamic collection candidates and shared expected Types for unresolved nested calls remain incomplete |
| Constraints and Contracts | Proven/Refuted/Unknown/Error states, declaration assumptions, associated Types, refinement, verified witnesses, conditional conformance/members, and inherited receiver selection | Unknown is not treated as success. Full candidate equivalence, generic body/effect proofs, and ObjectCallCompatible body/callee validation remain incomplete |
| Properties | Stored/computed/requirement Properties, accessor types, permissions, Copy/Origin handling, and operation-specific witnesses | General expression read/get/set/init, receiver/cleanup handling, and generation remain incomplete |
| Enums and Patterns | Case identity, construction with an expected Type, payload acquisition, Tuple/Case/whole Patterns, exhaustiveness/subsumption warnings, and separate guard-candidate/body binding | Borrowed Subjects, general decomposition/guards, and generic proofs/generation remain incomplete. Arms that receive warnings are still checked |
| Runtime `is` / `is not` | Binds object Semantics over concrete struct Core to bool, retaining original operands/targets and shared-access requirements | Flow Type refinement, object Loans, and execution are unsupported |
| Kimi library | Validates six declarations: Copy, Owned, Callable, writeLine, Option, and Result. Derives Copy/Owned and rejects same-name Kimi impostors | 12 of the catalog's 18 slots are Missing. Option/Result declaration and analysis support does not establish complete generic runtime support |

Evidence: [Binding](Kimi/Compiler/Binding), [KimiLibrary](Kimi/Compiler/Binding/KimiLibrary.cs), [CoreCatalogTest](xUnitTest/Tests/CoreCatalogTest.cs), TypeBinding / ConstraintBinding / ContractBinding / PropertyBinding / ConditionalConformanceBinding / InheritedReceiverBinding / EnumBinding / PatternBinding / RuntimeTypeTest.

<a id="c7-control-flow-and-failure-handling"></a>
<a id="c24-whole-place-ownership-cfg-and-cleanup-plans-2026-09-11"></a>
<a id="c27-enum-construction-ownership-and-ordered-cleanup-2026-09-11"></a>
<a id="c29-owned-match-acquisition-decomposition-and-cleanup-2026-09-11"></a>
<a id="c30-current-control-flow-syntax-results-and-cleanup-2026-09-12"></a>
<a id="c31-transfer-seeded-unreachable-ownership-checking-2026-09-12"></a>

## 3. Control Flow and Ownership

[Analysis](Kimi/Compiler/Analysis) uses Binding acquisition plans to check whole-Place initialization, Moves, and assignment history at a CFG fixed point. It handles conditional replacement/destruction, locals, temporaries, arguments, results, concrete enum construction/decomposition, and whole-value responsibilities for Tuples/fixed arrays. §4.8 connects partial Moves, reinitialization, and destruction of remaining parts for static paths in constructed local Tuples/fixed arrays. General field/index handling and user deinit remain incomplete.

- Analyzes `if`, short-circuiting, `while`, `do`, `loop`, labels, `require`, `match`, return/yield/exit/continue, and defer. Distinguishes structural completion from execution reachability. Results are secured before cleanup and delivered only after normal completion. Abort performs no cleanup.
- Destruction follows reverse logical order. If normal transfer interrupts construction or argument acquisition, already-acquired responsibilities are handled. No result delivery or subsequent destruction is generated after nonterminating cleanup.
- Source checking after an explicit transfer or Never-returning call uses a continuation separate from the execution CFG, including Never Subjects and nonterminating guards. Unsupported remains for unreachable operations whose checking entry state cannot be constructed, such as those after general exitless loops or cleanup that prevents continuation.
- Whole-Subject guards have conservative validation paths in source order; effects on false paths also pass to later arms. Covered arms omitted at runtime are still diagnosed.
- Loans currently cover string comparisons, shared arguments, temporary strings, string guard candidates, parent-storage protection for Tuples/fixed arrays, and exclusive protection for element writes. Statically disjoint element paths are allowed within §4.6's scope. Owners remain protected through later arguments, guards, and cleanup; Loans end after securing a normal result or during normal transfer. Simple element assignment also holds an exclusive Loan from final location resolution through old-value destruction and placement (§4.7). General reference storage/return, uniq/reborrow, effect summaries, and borrowed Subjects remain incomplete.

Evidence: OwnershipAnalysis / EnumOwnership / MatchOwnership / UnreachableOwnership / CurrentControlFlow / ControlFlowConformance / ReferenceEmission / StringGuardEmission tests. Generation coverage is checked separately even after successful analysis.

<a id="c6-expressions-and-operators"></a>
<a id="c12-first-executable-milestone"></a>
<a id="c23-startup-selection-and-corewriteline-binding-2026-09-11"></a>
<a id="c34-minimal-literal-output-executable-2026-09-13"></a>
<a id="c38-compiler-pipeline-and-emission-preparation-2026-09-13"></a>
<a id="c39-compiler-review-dead-code-removal-and-emission-restructuring-2026-09-13"></a>
<a id="c40-scalar-control-flow-emission-and-nativeaot-2026-09-13"></a>
<a id="c41-scalar-selection-and-loop-results-2026-09-13"></a>
<a id="c42-deferred-cleanup-execution-2026-09-13"></a>
<a id="c43-checked-i32-division-and-remainder-2026-09-13"></a>
<a id="c44-scalar-functions-return-and-explicit-main-2026-09-13"></a>
<a id="c45-i32-bitwise-operations-and-checked-shifts-2026-09-13"></a>
<a id="c46-integer-execution-through-64-bits-2026-09-13"></a>
<a id="c47-explicit-integer-conversions-2026-09-13"></a>
<a id="c48-owned-string-locals-and-conditional-cleanup-2026-09-13"></a>
<a id="c49-owned-string-control-flow-results-2026-09-13"></a>
<a id="c50-owned-string-function-parameters-and-results-2026-09-13"></a>
<a id="c51-string-comparisons-and-backend-memcmp-2026-09-13"></a>
<a id="c52-unguarded-whole-subject-match-execution-2026-09-13"></a>
<a id="c53-copy-subject-match-guards-2026-09-13"></a>
<a id="c54-shared-string-arguments-and-referent-comparison-2026-09-13"></a>
<a id="c55-string-guard-candidates-and-temporary-shared-arguments-2026-09-13"></a>
<a id="c56-whole-tuple-and-fixed-array-execution-2026-09-13"></a>

## 4. LLVM Generation Coverage

[LlvmEmitter](Kimi/Compiler/Emission/LlvmEmitter.cs) supports only windows-x64-v1 Applications and rechecks final Binding, startup, and ownership. It selects implicit top-level execution or an eligible `public func main() -> ()`. It rejects external modules, declaration containers, Library generation, and unsupported bodies, even unused ones, before publishing output.

| Area | Current generation coverage | Limits and evidence |
| --- | --- | --- |
| Scalars | bool, 12 integer types (i8/u8 through i128/u128, plus isize/usize), char, f32/f64, and Unit; locals, acquisition/replacement, comparisons, direct arguments/results, existing control flow, and aggregate payloads | [ScalarTypes](Kimi/Compiler/ScalarTypes.cs), Scalar / Integer / WideInteger / Char / FloatEmission |
| Integer operations | Checked add/sub/mul, signed negation, inc/dec, comparisons, bitwise operations, shift-count checks independent of the operand type, and compound updates. Division/remainder through 64 bits check zero and signed min/−1 | The initial profile prohibits i128/u128 division/remainder. char supports only Unicode scalar-order comparisons, not arithmetic. Integer / Division / Bitwise / WideIntegerEmission |
| Floats | f32/f64 arithmetic and comparisons, literals at target precision, and value plans for NaN, ±0, and subnormals | Separate from full numeric conversion support. Float literal Patterns are unsupported. FloatEmission / NumberLiteralParse |
| Numeric conversions | Range checks in all 12×12 integer directions; exact direct integer/float literal fitting; f32↔f64; <=64-bit integer↔float; same-type numeric acquisition, including explicit owner targets. Narrowing preserves NaN/infinities/signed zero and allows underflow; finite overflow Aborts. Float-to-integer truncates then checks range, including negative fractions to unsigned zero. | Typed 128-bit↔float and char/bool numeric conversions are prohibited. [Binding.Conversions](Kimi/Compiler/Binding/Binding.Conversions.cs), Conversion / FloatConversionEmission / NumericConversionEmission |
| Identity Acquisition | Same complete owned primitive, Tuple, and fixed-array Types supported by ordinary generation; explicit owner targets and `@owner` shorthand, including grouping. Copies Copy values and Moves Non-Copy values using ordinary acquisition, partial-Move, repair, loan-conflict, and cleanup rules. | Other representations and Borrow/Reborrow adaptations remain unsupported. IdentityAcquisitionEmission covers native cleanup, evaluation order, reload, stale-plan rejection, and zero measured warm analysis/writing allocations. |
| Functions, results, and cleanup | Root functions and capture-free local functions, named arguments, recursion, scalar/Unit/owned string/Tuple/fixed-array arguments/results, Never results, selection/loop/match results, and defer | Defaults, generics, explicit Origins, captures, indirect calls, and a general aggregate ABI are unsupported. [FunctionAbi](Kimi/Compiler/Emission/FunctionAbi.cs), Function / Result / DeferredEmission |
| Owned strings | Literals, local Moves, same-Type Identity Acquisition, self-assignment/replacement, conditional destruction, temporary/selection/function results, writeLine, and all six comparisons | Strings are Non-Copy even with Static backing; comparisons use UTF-8 byte sequences. Source operations for Heap construction, interpolation/concatenation, and other ownership adaptations are unsupported. String*Emission |
| Shared strings | Required ref/string arguments with implicit input Origins and forwarding, referent comparisons, temporary owned strings as shared arguments, string guard candidates, and comparisons/shared arguments for static string elements of owned locals, parameters, and temporaries (§4.9–4.10) | A reference is one pointer to a handle. Results must be independent values. Local storage/return, explicit @ref, uniq, and nested borrowing are unsupported. [ReferenceTypes](Kimi/Compiler/ReferenceTypes.cs), Reference / StringGuardEmission / ElementBorrowOwnerEmission |
| Match/guards | Exhaustive matches on bool, integers, char, Unit, and owned strings; literal/wildcard/whole let/var/parenthesized Patterns; guards over Copy or ref/string candidates | Float Subjects retain existing whole-Pattern/guard coverage. Borrowed Subjects and Tuple/enum decomposition execution are unsupported. Candidates are read-only and separate from body bindings. Match / Guard / Char / Float / StringGuardEmission |
| Tuples and fixed arrays | Construction from supported scalars, Unit, owned strings, and nested aggregates; whole local Copy/Move/replacement/conditional destruction; if/do/loop/match result delivery and ordinary function value arguments/results; Copy element reads via numeric Tuple selectors or isize fixed-array indices; simple assignment to Copy/supported Non-Copy elements of initialized local vars, compound numeric element assignment, and prefix/postfix integer inc/dec; statically disjoint element operations; static partial Moves from locals/parameters and destruction of remaining parts; local var reinitialization; static string element comparisons and shared arguments | Owners only; maximum depth 64 and size/count int.MaxValue. Dynamic Non-Copy element acquisition, whole-aggregate borrowed arguments/results, aggregate Subjects, and struct/enum generation are unsupported. [AggregateLayout](Kimi/Compiler/Emission/AggregateLayout.cs), AggregateEmission / AggregateResultEmission / AggregateFunctionEmission / ElementEmission / ElementAssignmentEmission / ElementUpdateEmission / ElementPathEmission / ElementReplacementEmission |

Generation uses verified typed-value, CFG, ABI, and cleanup plans. The Writer does not reinterpret the AST. It rejects inconsistencies in type identity, constants, inputs, dominance, result arrivals, live flags, Loans, and destruction plans. Distinct language types remain distinct even when their LLVM widths match.

Strings transfer each field of a 24-byte handle and use flags only for conditional responsibilities. Tuples separate physical alignment order from logical order; fixed arrays use stride-based layout. Construction targets final subslots, whole-value transfer uses memcpy into verified separate storage, and destruction follows reverse logical order (a loop for arrays). These are current internal representations, not a general public ABI.

### 4.1. Aggregate Selection Results (2026-09-14)

Tuples and fixed arrays can be delivered as results of if/else, do/yield/exit, value-producing loops, and matches over already-supported Subjects. One result plan handles Copy aggregates, values containing owned strings, nesting, and zero-sized values, connecting them to local initialization/replacement, construction payloads, outer-result transfer, and destruction of unconsumed results. See [AggregateResults](examples/AggregateResults/README.md) for executable examples.

Generalized StringResults to SlotResults, sharing validation of Declare, result Write, Join, and arrival edges. Type classification is independent of Copy status; Lowering validates concrete layout support. Result plans are registered before layout validation, and only verified expression results are accepted. Function return Places and Subjects are not accepted solely by Kind. No feature additions were needed in the Binder, ABI, LLVM Writer, or runtime.

Results are secured before cleanup and delivered on normal arrival. Validation checks placement into uninitialized storage, MustInit at every arrival, Write dominance, and both logical and physical arrival counts. Consumption must follow the current lifetime's Join, rejecting premature acquisition during cleanup and false approval based on an earlier arrival. Duplicated defer bodies share result slots per source expression but retain separate Declare/Join operations per expansion. A loop's result Declare appears once before its head. No result-specific live flags or aggregate phi nodes are added.

Transfer, reverse-order destruction, and conditional replacement reuse existing handling. The RHS's independent result is secured and cleanup completes before any required old-value destruction, new-value transfer, and flag setting. Abort/nonterminating cleanup generates no subsequent delivery or destruction. Direct construction into result slots, element access/partial Moves, and aggregate function ABI support were outside §4.1; function ABI support was added in §4.2 and Copy element reads in §4.3.

[AggregateResultEmissionTest](xUnitTest/Tests/AggregateResultEmissionTest.cs) checks nesting, expected array types, covered arms/guards, result destruction, defer, back edges, O0 snapshots, Abort/nontermination, invalid plans, and recovery by reanalysis. Warm Bind and Ownership analysis plus IR output, including defer, each allocate 0 B. Existing string-result validation also uses the shared plan, with premature-consumption rejection tested for both types. Throughput improvement has not been measured.

### 4.2. Tuple and Fixed-Array Function ABI (2026-09-14)

Connected supported owned Tuples/fixed arrays to value arguments and returns of ordinary direct calls. Covers nesting, Copy/Move, zero-sized values, named arguments, mixed Unit/scalar/string/ref-string signatures, shallow recursion, and returns from defer/selection results. See [AggregateFunctions](examples/AggregateFunctions/README.md) for executable examples.

Nonzero-sized aggregates pass acquired argument slots and the caller's independent result slot by ptr. Copy retains the original responsibility; Move transfers it. Argument names use logical indices `%a<i>`; the result is `%ret`. Zero-sized values omit physical arguments/result storage but retain CallEntry, acquisition, parameter responsibility, and Produce on normal return. Arguments and results of one call may not share storage. An inner call's result can serve as an outer call's acquired argument without another transfer. Direct result-storage forwarding for `return f(x)` is not implemented.

Generalized string function-slot validation to SlotFunctions, keeping its role separate from selection-result SlotResults. Checks uninitialized storage at the call site, result initialization only on normal return, return Write dominance, Deliver after cleanup, and conditional destruction-flag initialization at parameter Produce. Reuses existing transfer and reverse-logical-order destruction; no additions were needed in the LLVM Writer, runtime, or backend.

Signatures and bodies use the same AggregateLayoutPool. The ABI cache retains only physical passing conventions, omissions, and type shapes; semantic validation checks the current BoundType. Temporary type references are released on both success and failure. Shared-argument Loans end after securing an owned aggregate result containing no references. Warm Bind and Ownership plus IR output with shared borrowing and defer each allocate 0 B. Tests verify ABI reuse across reparse-equivalent input and that syntax trees are not retained. Throughput has not been measured.

This ABI support does not extend the Binder's contextual inference. Fitting array literals to call argument types and some nested Tuple argument inference remain incomplete; pass these through explicitly typed locals. The existing path for constructing array literals with an expected return type remains available. Aggregate borrowing, element writes, Non-Copy element acquisition/partial Moves, structs/enums, generics, and indirect calls were not supported in this increment. See §4.3 for Copy element reads.

### 4.3. Copy Element Reads from Tuples and Fixed Arrays (2026-09-14)

`pair.0` and `values[index]` can read from locals, parameters, temporaries, and selection/function results. Fixed-array indices use isize; untyped integers are fitted to isize. Supported scalars, Unit, and Copy aggregates can be acquired as final values, even from Non-Copy parents containing strings. See [ElementReads](examples/ElementReads/README.md).

Separated place formation (ProjectElement) from final acquisition (Produce/Element). Chained `.0` / `[i]` operations compute addresses incrementally from one root, without intermediate aggregate slots or transfers. Scalars load at the final location; Copy aggregates transfer once into an independent slot. Before generation, validation checks types, input-value/source correspondence, root initialization, Loans, and dominance of parent projections, indices, and acquisition. Invalid plans are rejected before IR is written.

The root is Read before index evaluation and protected through the final Copy by the existing shared-Loan stack. Move, replacement, and destruction during index evaluation are prohibited; shared reads and Copy are allowed. Normal transfer ends Loans at the relevant boundary before cleanup. Parent changes after Copy do not affect the acquired value. Temporary receivers remain alive after Copy until normal end-of-expression cleanup.

Each array projection checks `icmp uge i64 index, length`, covering negative indices too. Stride calculation and address formation occur only in the success block. Invalid indices Abort with `KIMI_E_INDEX_BOUNDS`, without evaluating later indices, defer, or destruction. Constant indices into empty arrays also Abort at runtime. Zero-sized elements retain checks while omitting unnecessary addresses, loads, and transfers. Dedicated address names use the same continuation-label scheme as existing arithmetic checks.

Simple Copy element assignment was added in §4.4, compound numeric updates in §4.5, and static Non-Copy element Moves/partial Moves in §4.8. Shared results, aggregate borrowing, and Array/Slice/Index/^/Range remain unsupported. This root protection is not a general Loan/Origin/Move Path solver.

[ElementEmissionTest](xUnitTest/Tests/ElementEmissionTest.cs) has 57 cases. They cover nesting, scalar widths, Copy results, bool/Unit, zero lengths/sizes, value snapshots, short-circuiting/phi, parameters, loops/defer, transfers, unreachable code, covered arms, Loan conflicts, invalid plans, and recovery. Warm Bind and ownership analysis plus IR generation each allocated 0 B over 128 iterations.

### 4.4. Simple Assignment to Copy Elements of Tuples and Fixed Arrays (2026-09-15)

Copy elements of initialized, complete owned Tuples/fixed arrays in local vars can be replaced with `=` through numeric selectors, isize indices, and nested paths. Supported elements are scalars, Unit, and recursively Copy aggregates; parents may contain owned strings. RHS construction, Copy, function results, and selection results are secured as independent values before each part of the LHS path is evaluated once. See [ElementAssignments](examples/ElementAssignments/README.md).

Location acquisition shares existing LocateElement/ProjectElement operations and bounds checks; only the final operation is distinguished as WriteElement. No whole-parent Write, reinitialization, or destruction plan is generated, preserving parent initialization and responsibility. Scalar stores share existing storage representations; Copy aggregates share existing memcpy generation. No intermediate aggregate Copies or per-element live flags are added. Zero-sized values retain evaluation and bounds checks.

The root is protected during location acquisition. In the §4.4 implementation, ending the access's own Loan was connected directly to the store by a normal edge. §4.7 generalized this to location resolution → exclusive Loan → placement → Loan end. Conflict checks against other Loans remain. Validation checks types, input sources, mutable roots, RHS initialization/dominance, result Join, and parent/index correspondence, rejecting invalid plans before IR publication. Writing another element of the same root inside an index is also rejected to protect storage. This differs from §4.6's disjointness checks after final location resolution.

Aligned simple assignment in structural-completion and control-flow analysis with RHS-first evaluation, retaining syntax-order visitors. Static destination types survive transfers during index evaluation, and value sources of labeled do expressions are normalized. Normal transfer, Abort, or nonterminating cleanup in the RHS or indices prevents subsequent location acquisition/writes, preserving existing result-securing and cleanup rules.

[ElementAssignmentEmissionTest](xUnitTest/Tests/ElementAssignmentEmissionTest.cs) has 79 cases. They cover nesting, scalar representations, snapshots, self-assignment, Copy aggregates, function/selection results, defer duplication, transfer precedence, unreachable code/covered arms, zero lengths/sizes, bounds Abort, nontermination, parent destruction order/counts, Loan conflicts, invalid plans, and recovery by reanalysis. Warm Bind and ownership analysis plus IR generation each allocated 0 B over 128 iterations. Throughput improvement has not been measured.

Compound numeric element assignment/inc/dec was added in §4.5; supported Non-Copy element replacement in §4.7. Non-Copy element acquisition, partial Moves/reinitialization, borrowed/temporary receivers, Properties, and dynamic collections are outside this increment. This does not narrow the specification's permitted behavior.

### 4.5. Numeric Element Updates in Tuples and Fixed Arrays (2026-09-15)

Using the same initialized owned local var roots as §4.4, connected integer elements to `+= -= *= /= %= &= |= ^= <<= >>=` and prefix/postfix `++ --`, and f32/f64 elements to `+= -= *= /=`. Supports nesting, Non-Copy parents, defer expansion, and function bodies. The existing profile still prohibits i128/u128 division/remainder. See [ElementUpdates](examples/ElementUpdates/README.md).

Following SPEC §13.7.2, evaluates location acquisition, bounds checks, old-value Copy, RHS evaluation, numeric computation, and store once each, in that order. This differs from RHS-first simple assignment. After storing, compound assignment returns Unit, prefix inc/dec returns the new value, and postfix returns the old value, without reloading. Integer overflow, division/remainder and shift checks, and IEEE float operations share existing scalar handling. Shift RHS checks retain its independent integer type.

Reads, simple assignments, and updates share LocateElement, CopyElement, and StoreElement. Numeric computation and inc/dec result generation also share ordinary local-update handling. Projection plans reference updates, and a reusable ElementUpdates table connects RHS, computation, and result. No intermediate aggregate Copies, per-element live flags, or update-specific LLVM operations are added. Parent initialization and ownership responsibilities are preserved.

Lowering checks the update source, operator, old value and RHS, types, unique projection owner, Loans and dominance, consecutive edges from computation → store → release of the access's own Loan → result, and prefix/postfix result selection. It also retains validation of RHS-securing order for simple assignment. Invalid plans are rejected before IR output; reanalysis restores valid plans.

Index evaluation uses shared protection. After the final projection's bounds check succeeds, the access's protection switches to an exclusive Loan, held through old-value acquisition, RHS evaluation, computation, and store. Only that access's own store is permitted. Existing outer Loans are retained; another live access to the same parent prevents exclusive acquisition. No new LLVM instructions or value slots are needed; existing Loan tables and integer projection IDs are reused.

`a[0] += a[0]` is rejected because it conflicts with the specification's exclusive Loan. The same rule covers copying the parent into RHS function arguments and reads inside defer. §4.5 initially used conservative root-level checks that also rejected siblings; §4.6 added support for statically disjoint paths. Updates to other roots and shared reads during index evaluation are allowed. Normal transfer ends abandoned-access Loans before cleanup. Bounds/arithmetic Abort, RHS/index transfers, and nonterminating cleanup prevent subsequent stores. Static destination types survive Never indices, and Never shift RHS values are treated as normal transfers.

The initial implementation used shared protection through the RHS and allowed self-reads. This was corrected to match the exclusive-Loan rules in §4.6.4/§15.6.2. Simple assignment retains the current specification's RHS-first order. Executable examples now Copy needed values into locals before updating elements.

[ElementUpdateEmissionTest](xUnitTest/Tests/ElementUpdateEmissionTest.cs) has 114 cases. They cover every integer width; float NaN/infinity/signed zero; evaluation counts/order; shift widths; arithmetic/bounds Abort; parent destruction audits; Loan conflicts; defer, unreachable code, transfers, nontermination, and invalid plans. They also verify Loan states around exclusive acquisition and after stores, rejection of self-reads/parent Copies, release on normal transfer, and rejection of modified plans. Warm Bind and ownership analysis plus IR generation with nested updates and defer each allocated 0 B over 128 iterations. Throughput improvement has not been measured. Non-Copy replacement was added in §4.7; static partial Moves/reinitialization in §4.8. Borrowed/temporary receivers, Properties, and dynamic collections remain unsupported.

### 4.6. Loan Disjointness for Static Element Paths (2026-09-15)

Connected §15.6.2's disjointness rules to existing Copy element reads, simple assignments, and numeric updates on owned Tuples/fixed arrays. Allows `a[0] += a[1]++`, `pair.0 += pair.1`, and assignment to sibling Copy aggregates or Unit elements. Access to the same element, ancestors/descendants, or whole-parent Copy/Move/replacement conflicts with an exclusive Loan. See [ElementPaths](examples/ElementPaths/README.md).

Under §15.1.3, static fixed-array selectors accept only in-range nonnegative integer literals and parentheses. Identity uses numeric value regardless of radix or digit separators. Unary plus, arithmetic, conversions, variables, and conditionals are not constant-folded to prove disjointness. An unknown index widens the check to the entire known parent path. `pair.0[i]` and `pair.1[j]` are disjoint, but disjointness is not proven for `a[i].0` and `a[j].1`. Empty arrays and out-of-range literals provide no disjointness evidence.

Distinguished receiver location acquisition as LocateReceiver from value Read, and linked Copy acquisition/stores directly to projection IDs. Paths use parent IDs, static prefixes, depths, and selectors in the existing projection table. No dedicated path objects or sets are created; comparison takes a scan proportional to path depth. Before overlap checks, validation rechecks source, parent, selector, prefix, and operation cross-references, rejecting modified plans. No new LLVM instructions, runtime Loan checks, or value slots are added.

Separated root-storage protection during index evaluation from the selected element's exclusive Loan after final location resolution. Writes to siblings in the same root remain prohibited during index evaluation. Once the final location is resolved, statically disjoint operations are allowed; an access's own Loan authorizes only its own old-value acquisition and store. Completing an inner sibling update retains the outer Loan. Existing evaluation order, normal transfer, defer, Abort, parent whole-initialization state, and destruction responsibilities are preserved.

[ElementPathEmissionTest](xUnitTest/Tests/ElementPathEmissionTest.cs) checks static/dynamic paths, nested updates, Copy aggregates/Unit, selection results, snapshots, defer/transfers, bounds Abort, parent destruction audits, unreachable code, invalid plans, and recovery by reanalysis. Warm Bind and ownership analysis plus IR generation with nested paths, sibling updates, and defer each allocated 0 B over 128 iterations. Throughput improvement has not been measured. This increment excludes Non-Copy element operations, partial Moves/per-element initialization, and general fields, Properties, ref/Slice, or dynamic collections.

### 4.7. Non-Copy Element Replacement in Complete Owned Values (2026-09-15)

Simple assignment can replace string elements and supported aggregate elements containing strings in initialized, complete owned Tuples/fixed arrays held by local vars. Supports nested paths, dynamic isize indices, literals, Moves from independent locals, function results, if/do/match results, loops, and defer. Existing Copy element operations remain supported. See [ElementReplacements](examples/ElementReplacements/README.md). This increment initially excluded Non-Copy element extraction; static partial Moves/reinitialization, including `a[0] = a[0]`, were added in §4.8.

Following §13.7.1, secures the RHS as an independent value before evaluating the receiver and each index once and checking bounds. For both Copy and Non-Copy values, the access's shared location-acquisition protection switches to an exclusive Loan after final location resolution, held through old-value destruction and placement. Reads, numeric updates, and simple assignments share projection and Loan tables. Writes requiring no old-value Copy use the same exclusive acquisition/end validation. Outer Loans remain active; only statically disjoint sibling replacements are allowed. Root protection during index evaluation is unchanged.

One WriteElement emits old-value destruction for the exact element type, then new-value transfer. It reuses string destruction/handle transfer, aggregate type-specific destruction/memcpy, and shared slot/element-address output. No whole-parent Write/Cleanup, backup Copy of the old element, per-element live flags, or new runtime APIs are added. The parent retains completeness and sibling responsibilities; placement consumes the input. Copy status is distinct from whether destruction is actually needed. Zero-sized values retain evaluation, bounds checks, and responsibility transfer.

If the RHS does not complete, the LHS is not evaluated. Normal transfer from an index cleans up the secured RHS under ordinary temporary-value rules. Bounds Abort performs no old-value destruction, placement, or cleanup. Nonterminating RHS/index cleanup generates no later placement. Only paths where existing string/aggregate destruction calls return normally proceed to transfer. This does not extend to general user deinit or replacement with Origin/Loan dependencies from retained references.

Lowering checks source, exact types, input/parent initialization, dominance, mutable roots, Loans, and consecutive location-resolution/write/Loan-end edges. Destruction targets are derived from verified projections and types rather than duplicated in a separate destruction-plan table. Also fixed slot tracking that treated TransferAggregate with an explicit address operand as the older Place/Constant form when transferring match results into elements.

[ElementReplacementEmissionTest](xUnitTest/Tests/ElementReplacementEmissionTest.cs) has 55 cases. They cover replacement, Moves, dynamic indices, nesting, result delivery, defer/transfers, Abort/nontermination, destruction counts/order, Loan/initialization rejection, modified plans, and recovery by reanalysis. Warm Bind and ownership analysis plus IR generation each allocated 0 additional B over 128 iterations. Destruction audits verify logical responsibilities, including Static-backed strings; they do not establish source-level Heap-string construction support. Throughput improvement has not been measured.

### 4.8. Static Element Partial Moves, Reinitialization, and Destruction of Remaining Parts (2026-09-15)

From constructed owned local let/var roots, strings and supported Non-Copy aggregates can be acquired through numeric Tuple selectors, in-range literal fixed-array indices, or combinations of these. Copy elements remain Copies. Supports Moves from let, use of remaining siblings, reinitialization of static paths in var, whole-value acquisition after repairing all missing parts, and replacement of partially moved parents. See [ElementMoves](examples/ElementMoves/README.md).

Separated constructed storage from current completeness. Moving all children individually leaves the parent constructed, allowing each child to be repaired. Moving a child as a whole prevents access inside it until that child itself is placed again. Rejects initial construction of an unconstructed array through element assignment, repair of let, and acquisition/borrowing of missing parts or incomplete whole values. Branches, loops, transfers, defer, and unreachable checking flow share the same state transitions.

Normalizes each path to an integer ID independent of per-access projection IDs, stored in reusable dictionaries, struct lists, and bit lanes. Tracks only referenced static paths and untracked remainders for relevant roots, without expanding in proportion to array length. Paths are sorted once to build reverse sibling links. Destruction-plan generation reads CFG input once and shares it across elements. Complete parts use existing type-specific destruction, untracked array ranges use reverse loops, and individual parts use direct calls. Only conditionally live remainders need flags.

Simple assignment remains RHS-first. `a.0 = a.0` Moves the element into the RHS, skips destruction of the now-missing old element, then replaces it under location resolution and an exclusive Loan. Replacing an incomplete parent first destroys only its remaining parts in reverse logical order. Numeric update order/exclusive Loans, root protection during index evaluation, and no unwinding on Abort remain unchanged. Dynamic Copy reads/replacements require a complete known prefix, conservatively rejecting operations that may touch missing parts.

Lowering rechecks acquisition kinds, static paths, sources/types, initialization/completeness, Loans, and dominance. Partial destruction and flag transitions derive from ownership state. Validation also checks missing/duplicate/incorrect flag values and placement after dispatch. Existing string handle transfer, aggregate memcpy, and destruction helpers are shared; no runtime APIs or Loan locks are added.

[ElementMoveEmissionTest](xUnitTest/Tests/ElementMoveEmissionTest.cs) initially had 70 cases (now 69 after moving one former parameter-rejection case in §4.11). Covers remaining-responsibility counts/order, branches, parent replacement, self-assignment, nesting, dynamic siblings, loops, defer, result delivery, unreachable code/covered arms, zero-sized values, bounds Abort, nontermination, diagnostic reasons, modified plans/reanalysis, conditional flags, and sparse paths. Warm Bind and Ownership plus IR generation each allocated 0 additional B over 128 iterations. Throughput improvement has not been measured.

Borrowing static string elements for comparisons/shared arguments, rejected in §4.8, was added in §4.9 and extended to owned parameters/temporaries in §4.10. Partial Moves from owned parameters were added in §4.11. Dynamic Non-Copy acquisition, partial Moves from temporary elements, borrowed receivers, general structs/Properties, user deinit, retained-borrow dependencies, and dynamic collections remain unsupported. These are current implementation boundaries, not restrictions on the language specification.

### 4.9. Static String Element Comparisons and Shared Arguments (2026-09-15)

Connected static string elements of Tuples/fixed arrays held in constructed owned local let/var values to all six comparisons and required ref/string arguments of existing direct calls. Supports in-range integer literals with parentheses, radices, or digit separators; nested paths; overlapping shared borrows of one element; named arguments; nested calls; mixed string guard candidates; independent string/aggregate results; loops; defer; unreachable code; and covered arms. See [ElementBorrows](examples/ElementBorrows/README.md).

Comparisons and shared arguments use a common BorrowStringElement operation to borrow the LocateElement projection target, without acquiring an owned temporary through AcquireElement. After the final bounds check, Read/Borrow switches root protection during location formation to a shared Loan on the selected element. This protects through later operands/arguments and cleanup during calls. Only the access's own Loan ends, after comparison or after securing an independent normal call result. Normal transfer releases abandoned Loans before cleanup. Outer Loans, original ownership responsibilities, and no unwinding on Abort are preserved.

Shared element Loans also use existing static-path comparisons for conflicts. Moves, replacements, and exclusive acquisitions of overlapping elements/ancestors are rejected; Moves, replacements, and numeric updates of statically disjoint siblings are allowed. Dynamic sibling operations use the existing static prefix. Root protection during index evaluation remains. For partially moved parents, validation separately checks the constructed state needed for location formation and target-element initialization; only remaining elements can be borrowed.

Binding's PlaceOriginSource is shared with Lowering, mapping Origins to owners for both Tuple selectors and fixed-array indices. Lowering rechecks source, root, type, static path, initialization, Loans, consecutive edges from location formation, and dominance. Existing ElementAddress values serve string comparisons and ref/string arguments. No owned temporary slots, string transfers, new destruction flags, LLVM instructions, runtime APIs, or backend ABI changes are added. Projection tables store integer borrow-operation IDs and reuse existing tables and capacity.

[ElementBorrowEmissionTest](xUnitTest/Tests/ElementBorrowEmissionTest.cs) initially had 78 cases (now 76 after moving two former rejections to success cases in §4.10). Checks normal execution, rejection reasons, remaining responsibilities/destruction order, early returns, Abort in later indices, nontermination, rejection of modified plans, and recovery by reanalysis. Warm Bind and Ownership plus IR generation with partial Moves, sibling updates, and defer each allocated 0 additional B over 128 iterations. Throughput improvement has not been measured.

Borrowing from owned parameters/temporaries was added in §4.10. Non-Copy shared results through dynamic indices, borrowed receivers, explicit @ref/uniq, local reference storage/return, whole-aggregate borrowing, general structs/Properties, user deinit, retained-borrow dependencies, and dynamic collections remain unsupported. This extends execution coverage without changing language rules.

### 4.10. Static String Element Borrowing from Owned Parameters and Temporaries (2026-09-15)

Extended §4.9's shared BorrowStringElement handling to owned parameters, Tuple/fixed-array construction temporaries, direct function results, and if/do/loop/match results. Supports both comparisons and required ref/string arguments. Receivers are evaluated once, using ElementAddress within existing owner slots. No owned element temporary slots, transfers, or partial Moves are added. See [ElementBorrowOwners](examples/ElementBorrowOwners/README.md).

Storage is not accepted solely by Place Kind. Validation checks existing function/construction/selection-result role tables and requires parameter Produce, call-result Produce after normal return, and CompleteConstruction to dominate location formation. Selection results must match the current Declare/Join. Location formation, use, or destruction during cleanup after a result-securing Write is rejected. Parenthesized and labeled sources share existing ValueSource normalization.

Temporary owners remain alive until the expression's prescribed endpoint; ending a Loan does not cause early destruction. For paths skipped by short-circuiting, existing live flags start at 0 on entry, become 1 at construction completion, normal call return, or result Write, and return to 0 at cleanup. Conditional selection-result cleanup additionally traverses existing adjacency tables from every reachable Write to its matching Join, verifying that undelivered values cannot reach that cleanup. Ordinary uses retain existing dominance checks. Separate lifetimes from loops/defer duplication are allowed; Abort does not unwind.

Borrowed temporary owners are recorded once from comparison/Loan tables into reusable arrays, eliminating per-Place table scans. Flag validation shares bits in existing scratch storage; result-delivery path checks also reuse an existing queue and reusable arrays. No additional syntax-tree or BoundType references are retained, and runtime APIs, backend ABI, and LLVM Writer are unchanged.

Owned-parameter partial Moves were added in §4.11. The specification prohibits parameter updates/reinitialization. Partial Moves/updates of temporary elements, dynamic Non-Copy indices, borrowed receivers, explicit @ref/uniq, and reference storage/return are outside scope. Fixed-array literal inference is unchanged; use existing explicitly typed locals or function return types. This extends execution coverage under existing lifetime, delivery, and Loan rules without changing the language specification.

### 4.11. Static Partial Moves from Owned Parameters (2026-09-15)

Strings and supported Non-Copy aggregate elements can be Moved through static paths from owned Tuple/fixed-array parameters of ordinary functions. Supports nesting and in-range integer literal indices. Remaining parts can be Copied, compared, passed as shared arguments, or Moved through other static paths. Rejects acquisition of moved elements, paths under moved ancestors, and incomplete whole values. See [ElementParameterMoves](examples/ElementParameterMoves/README.md).

Owner eligibility is centralized in ElementAccess.SupportsMoveRoot and shared by Analysis and Lowering, distinct from borrowable temporaries/selection results. Lowering retains existing SlotFunctions checks for parameter receipt, types, initialization, and dominance over location formation. Parameters retain let-like behavior; updates and reinitialization are prohibited.

Existing Initialize handling already initializes sparse Move Paths on parameter receipt, so no dedicated state table is added. Generated-code PathFlagTransition now initializes flags at the receipt Produce matching the parameter source. Only necessary path flags are set to 1 on receipt and to 0 when the corresponding part Moves. Existing partial destruction handles remaining parts in reverse logical order. Uses actual argument slots directly, without another whole-parent transfer or conversion to a local. Runtime APIs, LLVM Writer, and backend ABI are unchanged.

Return values are secured before callee cleanup and delivered only after it completes normally. Transfers during evaluation retain responsibilities for acquired arguments and remaining parameter parts. Abort does not unwind; Abort/nontermination during cleanup prevents subsequent destruction and delivery. No new arrays or caches of syntax/type references are added.

Partial Moves from temporaries or function/selection results, dynamic Non-Copy element acquisition, borrowed receivers, general structs/Properties, and user deinit are outside this increment. This extends execution coverage under §7's parameter rules and §15.1.3's Move Path rules without changing the language.

<a id="c2-builds-modules-and-source-artifacts"></a>
<a id="c11-executable-preparation-and-sequence-review-2026-09-09"></a>
<a id="c13-remaining-implementation-selections"></a>
<a id="c22-lowering-and-windows-profile-specification-integration-2026-09-11"></a>
<a id="c25-core-declaration-catalog-and-native-backend-candidate-2026-09-11"></a>
<a id="c35-shared-llvm-version-policy-2026-09-13"></a>
<a id="c36-native-build-execution-and-shared-package-release-2026-09-13"></a>
<a id="c37-generated-kernel32-import-library-2026-09-13"></a>

## 5. CLI, Artifacts, and Windows Runtime

- [Project](Kimi/SolutionAndProject/Project.cs) Check performs semantic validation; Generate/`emit` produces consistent `.ll` and `.link.json` files; Build/`build` produces a native exe. `run` executes an existing Application without automatically rebuilding after source changes.
- [Solution](Kimi/SolutionAndProject/Solution.cs) shares input resolution across build/run/emit: try the specified path, then `.kimiproj` if no extension was given, then `.kimi`. Failure of a selected target does not fall back to another candidate. A single `.kimi` becomes an implicit Application/O2 project containing only that file, with its target selected from the OS/OS architecture (currently Windows x64; `--Target` can override it). It includes no neighboring sources/project settings and creates no `.kimiproj`. run validates and executes existing artifacts without reading source contents. The CLI command is consistently `emit`; unknown commands, including legacy `emit-llvm`, exit with 1.
- [EmissionArtifacts](Kimi/Compiler/Emission/EmissionArtifacts.cs) / [NativeToolchain](Kimi/Compiler/Emission/NativeToolchain.cs) handle schema 3, IR/exe/supply hashes, ABI and LLVM identity, invalidation of previous success on failure, and publication from staging. They collect tool output and support cancellation, time limits, and child-process-tree termination. Application output/exit status is forwarded.
- [ToolchainResolver](Kimi/Compiler/Emission/ToolchainResolver.cs) selects the root from CLI ToolchainRoot, environment variable KIMI_TOOLCHAIN_ROOT, then the default location. The default is beside the executable; source builds search the checkout's toolchain directory. LlvmBin and explicit backend-path overrides are handled separately. emit alone needs no LLVM installation. Ordinary .NET builds do not regenerate the backend.
- [WindowsProfile](Kimi/Compiler/Emission/WindowsProfile.cs) and [profile.json](backend/windows-x64/profile.json) define LLVM 22.1.8, backend ABI 2, and __chkstk/memcmp/memcpy/memmove/memset. Versions are shared with Directory.Build.props; actual archive hashes are checked too. Integrity checks remain mandatory even for explicit unpinned-toolchain trials.
- [WindowsRuntime.ll.in](Kimi/Compiler/Emission/WindowsRuntime.ll.in) implements startup, UTF-8 output, allocation/freeing, string destruction, and Abort. Normal exit is 0; Abort is 1. LF is written separately and NUL is treated as data. Static backing is not freed; Heap responsibilities are released. General object/Weak/metadata runtime support remains incomplete.
- [Kernel32Imports](Kimi/Compiler/Emission/Kernel32Imports.cs) generates and validates an import library from project-owned definitions using llvm-dlltool. It supplies seven Windows APIs for the runtime and three for native tests, removing the need to configure the SDK's kernel32.lib.

Evidence: EmissionArtifacts / NativeToolchain / ToolchainResolver / Kernel32Imports / MinimalEmission tests and [backend verification scripts](backend/windows-x64). See §7 for recorded ordinary native execution results.

## 6. Unconnected Product Features and Performance Infrastructure

| Area | State verified in code |
| --- | --- |
| Modules and Mod | [Compilation](Kimi/Compiler/Core/Compilation.cs) retains configured external Kotonoha identifiers but does not connect them to source loading. Bind does not execute Mod; generation rejects external modules/Libraries. Portable interchange and persistent semantic-plan reuse remain incomplete. Composition Root Entry/Provider integration remains unsettled |
| Generics, objects, and Kimi | Generic shared/specialized generation; Closures/indirect calls; struct/enum/Property/user constructor/static execution; rc/arc/Weak and runtime refinement; Array/Slice/Dictionary and iteration; and the language test runner remain incomplete. See §2–3 for declaration/analysis coverage |
| LSP | [LspServer](Kimi/Lsp/LspServer.cs) handles initialize, document management, shutdown, and related communication. Diagnostic publication for open/change is commented out; only empty diagnostics on close are connected. Dump requests/output are also unconnected |
| KimiCode | [extension.js](KimiCode/extension.js) hard-codes the adjacent Debug DLL and DebugWait=true. General-purpose server configuration and host integration tests are not established |
| CI and distribution | [test.yml](.github/workflows/test.yml) / [publish.yml](.github/workflows/publish.yml) run a Linux Release build followed by tests without a specified configuration/target. PR coverage, Windows native checks, zero-test detection, and log retention are not established. NuGet pack/push is defined but was not run in this work |
| Performance | Reuses tables, scratch storage, and capacity for Binding, types, CFG, ABI, constants, and layouts. Related tests check 0-byte warm allocations and retained references after reparse; [Benchmark](Benchmark) provides lexing, Binding, ownership, and other workloads. This does not guarantee allocation-free execution on every path or improved throughput |

## 7. Verification Records

### 7.1. Non-Copy Element Replacement and Element Operations (2026-09-15)

Verified §4.7's Non-Copy element replacement and regressions for §4.3–4.6 reads, simple assignments, numeric updates, and static paths. Beyond removing the Copy restriction, implemented old-element destruction, responsibility transfer, and exclusive Loans for simple assignment, connecting them to existing string/aggregate handling. Replaced the old blanket rejection of Non-Copy element assignment with a rejection case for then-unsupported self-assignment requiring a partial Move.

| Check | Result |
| --- | --- |
| Solution build (Debug / Release) | Both passed with 0 warnings and 0 errors |
| Managed tests (Debug / Release) | 4,614 passed in each; 0 failures/skips. Added 55 ElementReplacementEmissionTest cases |
| LLVM verifier and ordinary native O0/O2 for Non-Copy replacement and existing element operations | 246 fixtures, 492 successful runs: 60 replacement fixtures/120 runs and 186 existing element-operation fixtures/372 runs. Includes destruction audits, bounds/arithmetic Abort, and nontermination with timeouts |
| Aggregate native regressions for shared destruction/transfer handling | 157 Aggregate fixtures, 314 successful runs. O0/O2 checks cover construction, result delivery, function ABI, and destruction audits. Combined with Element fixtures: 403 fixtures/806 runs |
| Warm allocation | Bind and Ownership plus IR generation with function/selection results, nested element replacement, and defer each measured 0 B over 128 iterations |
| ElementReplacements CLI build/run | Release/O2 passed; printed `element replacements complete` and exited with 0 |
| NativeAOT and new throughput benchmarks | Not run |

To reproduce, run each configuration's build→test sequence in §7.2, then apply `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'` and `-FixturePattern 'Aggregate*.ll'` serially to generated fixtures. Fixture generation and native execution must also run serially. Every Element IR file regenerated after the final build/test matched the native verification inputs by SHA-256. See [ElementReplacements](examples/ElementReplacements/README.md) for the CLI example.

Additional tests exposed and led to fixes for slot tracking during match transfers and LLVM output passing element addresses to destruction functions. Successful results are from verification after those fixes; managed success alone is not used as evidence of native execution or destruction responsibilities. Three rejection tests were strengthened to check completed Binding and the Loan-conflict diagnostic reason, then rerun in Debug/Release.

The preceding §4.6 increment passed 4,559 managed tests per configuration, 186 Element fixtures/372 native runs, and the ElementPaths Release/O2 example.

The preceding §4.5 exclusive-Loan correction passed 4,468 managed tests per configuration and 163 Element fixtures/326 runs. Old fixtures treating self-reads as successful were replaced with valid prior-Copy examples and compile-time rejections. The ElementUpdates Release/O2 example also produced expected output and exit 0.

The initial §4.5 increment passed 4,451 managed tests per configuration, 160 Element fixtures/320 runs, and 14 ordinary local-update fixtures (IntegerValues/WideOperations/FloatArithmetic)/28 runs. Its self-read allowance preceded the Loan correction above and is not evidence of specification conformance.

The preceding §4.4 increment passed 4,354 managed tests per configuration, 95 Element fixtures/190 runs, and 59 AggregateFunction fixtures/118 runs. The ElementAssignments Release/O2 example also produced expected output and exit 0.

### 7.2. Verification of the Preceding Increments (2026-09-14)

The baseline review checked main entry points, type/conversion/ABI/aggregate and ownership rejection conditions, the Kimi catalog, and LSP/extension/CI against current code and related tests. After implementing §4.1 result delivery, §4.2 function ABI, and §4.3 Copy element reads, the following builds and managed checks were rerun.

| Check | Result on 2026-09-14 |
| --- | --- |
| Solution build (Debug / Release) | Both passed with 0 warnings and 0 errors |
| Managed tests (Debug / Release) | 4,275 passed in each; 0 failures/skips. Includes fixture generation |
| LLVM verifier and ordinary native O0/O2 for aggregate selection results (at §4.1 implementation) | 52 fixtures, 104 successful runs. Includes destruction counts/order, dependency symbols, and nontermination with timeouts |
| Native regression for existing string results (at §4.1 implementation) | 47 fixtures, 94 successful O0/O2 runs. Combined with aggregate results: 99 fixtures/198 runs |
| LLVM verifier and ordinary native O0/O2 for aggregate function ABI (at §4.2 implementation) | 59 fixtures, 118 successful runs. Checks destruction order/counts, Abort, nontermination with timeouts, and dependency symbols |
| Native regression for existing string functions (at §4.2 implementation) | 63 fixtures, 126 successful runs. Combined with aggregate functions: 122 fixtures/244 runs |
| LLVM verifier and ordinary native O0/O2 for Copy element reads (§4.3) | 41 fixtures, 82 successful runs. Checks bounds Abort, empty arrays/zero-sized values, index evaluation order, temporary-receiver destruction order/counts, and dependency symbols |
| Aggregate function ABI native regression (after §4.3 implementation) | 59 fixtures, 118 successful runs. Combined with element reads: 100 fixtures/200 runs |
| ElementReads CLI build/run | Release/O2 passed; printed `element reads complete` and exited with 0 |
| AggregateFunctions CLI build/run | Release/O2 passed; printed `leaving echo` twice, then `aggregate functions complete`, and exited with 0 |
| AggregateResults CLI build/run | Release/O2 passed; printed `cleanup`, then `aggregate results complete`, and exited with 0 |
| Standalone runtime/backend and LSP integration scripts | Not run in this work |
| NativeAOT, VS Code host, actual CI workflows, distribution, and new benchmark measurements | Not run in this work |

Reproduction commands (run build→test serially for each configuration):

```powershell
dotnet build Kimigayo.slnx -c Debug --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore
dotnet build Kimigayo.slnx -c Release --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Release --no-build --no-restore
```

Build warning counts reflect the current settings. Tests verify supported and rejected boundaries; they are not proof of conformance to the entire SPEC.

### 7.3. CLI Input Resolution and Implicit Projects (2026-09-15)

For each loaded project, build/run/emit prints a one-line summary of its name, project/source filename, implicit-project status, Targets, OutputKind, and Optimization. An explicit `--Target` is shown separately. This replaces the old filename-only Target Projects list. Direct `.exe` execution does not gain a ProjectFile display.

After adding the display, the Debug compiler build passed with 0 warnings/errors. Existing fixtures verified implicit-project emit/build/run and explicit-project emit with `--Target`; all displayed the summary and exited with 0.

- Debug compiler build passed with 0 warnings/errors. All 52 related SolutionInput / NativeToolchain / EmissionArtifacts managed tests passed.
- `./backend/windows-x64/test-cli.ps1 -Configuration Debug` passed. Using the managed .NET CLI and real LLVM, it checked existing O0/O2 builds, extensionless projects, single-source O2 builds/emit, run after source changes, specified-path precedence, rejection of fallback from a broken project, and rejection of the legacy command name.
- Confirmed exclusion of a broken neighboring source file, no implicit `.kimiproj` creation, unchanged native success records after emit/run, and forwarding of run stdout/exit codes.
- NativeAOT tests, a full managed-suite rerun, and performance benchmarks were not run.

### 7.4. Static Partial Moves and Destruction of Remaining Parts (2026-09-15)

Final specification review found that comparisons/shared arguments could incorrectly Move elements into temporaries. This was corrected by rejecting direct element borrowing as then unsupported. Added checks for rejection of all six comparisons/shared arguments and for diagnostic reasons on Move conflicts during index evaluation or exclusive Loans.

Implemented §4.8 and aligned old rejection tests for static Non-Copy element acquisition, self-assignment, and sibling assignment with supported cases or double-Move rejection. Existing numeric-update/simple-assignment evaluation order and exclusive-Loan rules are unchanged.

| Check | Result |
| --- | --- |
| Solution build (Debug / Release) | Both passed with 0 warnings and 0 errors |
| Managed tests (Debug / Release) | 4,681 passed in each; 0 failures/skips. Added 70 ElementMoveEmissionTest cases and merged three former rejections into new success cases |
| Warm allocation | Bind and Ownership plus IR generation with branch-dependent partial Moves and incomplete-parent replacement each measured 0 additional B over 128 iterations |
| Sparse Move Paths | A declaration with one million elements uses two paths: the root and the referenced element. Acquisition before initial construction is rejected as UninitializedUse |
| ElementMoves CLI build/run | Release/O2 passed; printed `taken`, then `element moves complete`, and exited with 0 |
| NativeAOT and new throughput benchmarks | Not run |

All 311 Element fixtures passed the LLVM verifier and ordinary native O0/O2 execution: 622 runs, comprising 65 partial-Move fixtures/130 runs and 246 existing element-operation fixtures/492 runs. Also checked destruction counts/order, bounds/arithmetic Abort, and nontermination with timeouts. All 311 Element IR files from the final Debug/Release rerun matched the native verification inputs by SHA-256. Fixture generation and native execution run serially. Reproduce with §7.2's build→test sequence, then `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'`. Destruction audits verify logical responsibilities, including Static-backed strings; they do not establish a source API for Heap-string construction.

### 7.5. Static String Element Borrowing (2026-09-15)

Implemented §4.9 and converted ElementMoveEmissionTest's former rejection tests for all six comparisons/shared arguments into success tests. Added 78 ElementBorrowEmissionTest cases. Semantic rules in the specification body are unchanged; aligned the SPEC index and executable-example references.

| Check | Result |
| --- | --- |
| Solution build (Debug / Release) | Both passed with 0 warnings and 0 errors |
| Managed tests (Debug / Release) | 4,759 passed in each; 0 failures/skips |
| LLVM verifier and ordinary native O0/O2 for element borrowing | 47 fixtures, 94 successful runs. Includes mixed guard candidates, destruction audits, bounds Abort in later indices, and nontermination with timeouts |
| Native regression for existing element operations | 311 fixtures, 622 successful runs. Combined with new borrowing: 358 fixtures/716 runs |
| Warm allocation | Bind and Ownership plus IR generation with partial Moves, sibling updates, and defer each measured 0 additional B over 128 iterations |
| ElementBorrows CLI build/run | Release/O2 passed; printed `shared element`, `beta`, then `element borrows complete`, and exited with 0 |
| NativeAOT and new throughput benchmarks | Not run |

A new early-return audit corrected expected Tuple destruction order to reverse logical order. Final review used a structural test to reproduce incorrect address selection from an outer Loan when reading a guard candidate as a shared argument during element borrowing. Selection now depends on whether the reference itself has a projection. Added two guard execution cases and a structural test, then reran the full Debug/Release suites and element-borrowing native checks.

After verifying 356 fixtures (311 existing element-operation and 45 initial borrowing fixtures) with `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'`, confirmed that all 356 IR files regenerated by the final Debug/Release runs matched those native inputs by SHA-256. The final 47 borrowing fixtures were reverified with `-FixturePattern 'ElementBorrow*.ll'`. Counts reflect results after fixes; repeated runs of the same fixture are not counted again in the 358-fixture total. Managed fixture generation and native verification ran serially. Reproduce with §7.2's build→test sequence, then the script for all Element fixtures. Audits verify logical destruction responsibilities for Static-backed strings; they do not establish a new Heap construction API.

### 7.6. Element Borrowing from Owned Parameters and Temporaries (2026-09-15)

Implemented §4.10. [ElementBorrowOwnerEmissionTest](xUnitTest/Tests/ElementBorrowOwnerEmissionTest.cs) initially had 61 cases (now 60 after moving one former parameter partial-Move rejection in §4.11). Moved two former parameter/literal rejections from local-borrowing tests and added successful comparisons/shared arguments, owner plans/initialization, conditional cleanup, ownership retention, destruction counts/order, normal transfers, Abort, nontermination, rejection of modified plans, and recovery by reanalysis.

| Check | Result |
| --- | --- |
| `dotnet build Kimigayo.slnx --no-restore` | Debug / Release both passed with 0 warnings and 0 errors |
| Full managed suite | Debug 4,818 / Release 4,818 passed; no failures/skips |
| LLVM verifier and ordinary native O0/O2 for new owner borrowing | 57 fixtures, 114 successful runs. Includes conditional production, iteration, defer duplication, destruction audits, Abort, and nontermination with timeouts |
| Full Element native regression | 415 fixtures, 830 successful runs, including the 57 new fixtures |
| Native regression for shared construction, result delivery, and function ABI | 157 Aggregate fixtures, 314 successful runs. Combined with Element fixtures: 572 fixtures/1,144 runs |
| Low allocation | Warm Bind and Ownership plus IR generation with parameters, call/selection results, conditional temporaries, and defer each measured 0 B over 128 iterations |
| CLI example | ElementBorrowOwners Release/O2 build/run passed, with the README's four output lines and exit 0 |
| NativeAOT | Not run, as instructed |

During implementation, fixed missing live-flag activation at CompleteConstruction for conditional Tuple construction, and erroneous rejection of cleanup covering short-circuit paths that bypass a selection result's Declare. Tests also check missing/incorrect/duplicate flags and rejection of location formation/destruction during result cleanup. The Abort audit's expected position was corrected to the actual source column. Counts reflect results after fixes; repeated executions of the same fixture are not added to the total.

Reproduce with §7.2's build→test sequence for each configuration, then `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'`, followed by the same script with `-FixturePattern 'Aggregate*.ll'`. Managed fixture generation and native verification ran serially. All 2,860 files regenerated by the final Debug/Release runs—IR, expected stdout/stderr, exit, and timeout—matched the saved native verification inputs by SHA-256. Added arrays reuse capacity; throughput improvement has not been measured. Audits cover logical responsibilities for Static-backed strings.

### 7.7. Partial Moves from Owned Parameters (2026-09-15)

Implemented §4.11. [ElementParameterMoveEmissionTest](xUnitTest/Tests/ElementParameterMoveEmissionTest.cs) has 54 cases, including one former parameter partial-Move rejection moved from each of ElementMove and ElementBorrowOwner. Reused semantic-state initialization on parameter receipt and added the missing generated-code path-flag initialization.

| Check | Result |
| --- | --- |
| Solution build | Debug / Release both passed with 0 warnings and 0 errors |
| Full managed suite | Debug 4,870 / Release 4,870 passed; no failures/skips |
| New parameter partial Moves | 51 fixtures, 102 successful O0/O2 runs |
| Native regression for partial Moves | `Element*Move*.ll`: 122 fixtures, 244 successful runs, including the 51 new fixtures |
| Aggregate function ABI native regression | `AggregateFunction*.ll`: 59 fixtures, 118 successful runs. Combined with partial-Move checks: 181 fixtures/362 runs |
| Low allocation | Warm Bind and Ownership plus IR generation with conditional nested Moves and borrowing of remaining elements in defer each measured 0 B over 128 iterations |
| CLI example | ElementParameterMoves Release/O2 build/run passed, with the README's three output lines and exit 0 |
| NativeAOT | Not run, as instructed |

Verified reverse logical destruction order, remaining responsibilities, joins between conditional and whole Moves, nested aggregates/arrays, zero-sized semantic state, borrowing conflicts, loops/defer/unreachable code/covered arms, transfers during evaluation, Abort, and nontermination. Also checked parameter immutability; missing/incorrect/duplicate initialization flags; rejection of modified receipt/Move plans; and recovery by reanalysis. Structural tests confirm use of the parameter's actual argument address, with no additional whole-parent transfer or parameter-local slot.

Reproduce with §7.2's build→test sequence for each configuration, then `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*Move*.ll'`, followed by the same script with `-FixturePattern 'AggregateFunction*.ll'`. Used fixtures generated by the final Release test; generation and native execution of the same fixtures ran serially. Initial partial runs are not counted again in the total. Audits cover logical responsibilities for Static-backed strings; throughput improvement has not been measured.

## 8. autoframe Execution Infrastructure

For the current Windows automation infrastructure, see the [autoframe specification](autoframe/SPEC.md), [usage guide](autoframe/README.md), and [verification record](autoframe/VERIFICATION.md). The old autoframe.md/autoimpl locations and early stage counts/verification coverage are historical; those documents now track current support. This documentation-alignment work did not run or reverify the infrastructure.

Further review corrected partial acceptance, missing recovery targets, corrupted/expired evidence, audit/stall detection, stop confirmation, and instruction-file protection/signing. Verified 56 simulated tests in total, including 17 added tests. The actual CLI was not retested in this work.

### Numeric literal replacement

Metadata-preserving syntax replacement retains the numeric literal spelling used for fitting and writing while keeping the original diagnostic location. Ordinary parsed literals borrow their source snapshot; only relocated numeric nodes materialize a spelling string. NumericReplacementTest covers integer/float boundaries, repeated replacement, lazy float parsing, rebinding, and zero measured warm Binding allocations. Five generated fixtures passed LLVM verification and ten Windows O0/O2 executions. This does not add Mod APIs or persistence of edited syntax trees.

### Unknown Attribute validation

Wrong-target Test and non-function LibraryImport markers, and selected unrecognized Attributes, invalidate their target declarations before conformance certification, including chained and parameter Attributes. Syntax remains readable during provisional Binding; removing the marker and rebinding restores valid certificates. Directive-excluded markers remain excluded. Earlier entries in a selected Attribute chain are checked independently of an invalid or completed marker. Conformance and Property/accessor certificates also require valid enclosing declarations; unrelated siblings remain independently checked. Enum construction completion also rejects invalid owner contexts and restores reusable plans after a valid rebind. Constraint proofs also reject invalid enclosing Type/Contract contexts before identity or assumption success, including nested Type arguments. No Mod marker recognition API is introduced; LibraryImport retains its separate implementation boundary.

### Generic signature certificate validation

Property/accessor and function-conformance certificates validate nested constructed-Type input constraints before publication, including retained projection qualifier inputs erased by normalization. Property certification follows final Origin/API validation and precedes final conformance verification, preventing late-invalid signature Types or owners from retaining certificates. Valid dependent signatures use their definition-side evidence; rejected Types remain readable for recovery. Binding safely rejects malformed computed/Contract Properties missing the mandatory getter, retaining the parser diagnostic instead of crashing. Enum acquisition plans likewise validate nested constraints in result and payload Types before becoming queryable; enum payload declarations retain and validate projection qualifier inputs before final certification. Generic function applicability validates complete explicit and inferred Type arguments before publishing a call plan, including nested constraints and caller-provided evidence. Call candidates also validate the target declaration context and definition-side signature constraints, including nongeneric signatures. Final Binding completion rechecks selected targets and substituted result/argument/receiver/declaring Types after late API or constraint validation; constrained declaring Types are also checked during candidate selection. Pattern subject/payload constraints are checked before coverage, including wildcard/binding positions, and rechecked after late declaration validation. Invalid Types cannot retain successful coverage; valid dependent patterns keep definition-side evidence. Coverage warnings and missing-coverage diagnostics are published after late Pattern Type validation, preserving ordinary valid-Type coverage failures and independent body-error warnings. Explicit/default module aliases and module reparsing are covered by integration tests. Conformance certificates also require nested input constraints in both normalized associated-Type definitions and their retained pre-normalization inputs, using the path's own declaration and conditional evidence. Base generic input constraints, including retained projection qualifiers erased by normalization, are checked after capability evidence becomes available, and invalid bases propagate through descendants before Property/conformance certification. Runtime Type-test plans validate operand/target input constraints before publication and after late declaration validation. General generic body proofs and specialization remain incomplete.

### Inherited declaration Names

Binding rejects a derived Value-role Name that matches any accessible ancestor member under SPEC 6.2, regardless of signature, conditional premises or narrower derived access. Protected checks use the derived receiver; Property access is independent of accessor visibility. Inaccessible private Names and same-layer overloads remain available. Checks run on merged fragments and propagate invalid bases to descendant certificates. Cross-module aliases/reparse and protected/internal access distinctions are covered, along with imported associated-Type/base/runtime-test input validation. Existing general receiver/effect and generation limits remain.
