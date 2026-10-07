# Arithmetic implementation

Required behavior is in [arithmetic Contracts](../spec/arithmetic-contracts.md), numeric behavior in SPEC §13.3 and literal fitting in §12.3.1. This document records implementation units and verification; it adds no language rules. Integration and implementation completion are separate.

## Position

A0–A5 are implemented. Unit, final Session and matched measurement evidence are recorded in [PLAN_HISTORY](PLAN_HISTORY.md). Program 43 has an original-source O0/O2 harness. Shared inline inherited conformances use the original verified mapping, substituted base paths and associated bindings, including conditional/refinement agreement and external Origin results. Rebinding, ownership and emission reuse storage.

The initial native i128/u128 division/remainder limit is unchanged. General requirement-owned generic slots/named per-call Origins (G78), inherited object receiver projection (G83), exclusive OCC and CSP publication remain separate work; arithmetic requirements introduce none of those signatures or receiver modes. Performance conditions and commands are in [Benchmark/Arithmetic](../../src/Benchmark/Arithmetic.md).

## Units

| Unit | Change and reason | Completion evidence |
| --- | --- | --- |
| A0 | Integrate declarations, provider selection, literals, acquisition, function values and updates in their owning specification sections; remove obsolete deferral. | Self-contained rules, positive/negative acceptance matrix below, explicit STATUS limits. |
| A1 | Declare the eleven Contracts in Kimi; recognize Identity and validate published eligibility, Output and numeric witnesses using existing bound Contracts. Complete requirement Function Item acquisition through the common reference route. | Numeric matrix, ordinary/Left/unary signatures, conditional/inherited/conflicting paths, stored requirement calls and invalid declaration records. Requirement references are a P26/G78 prerequisite, not a separate arithmetic-only representation. |
| A2 | Split operand typing, fixed provider selection, structural fitting, acquisition and result fitting. Retain one selected call plan through ownership and emission. | Same/mixed operand Types, distinct Output, Non-Copy, known reference layers, generic definition stability, direction conflict, Origin and effect failures. |
| A3 | Add numeric-left mapping and common floating-point literal-only fitting without candidate reanalysis. | Left-to-right acquisition and noncommutative results; integer/float, typed, position/range and explicit-conversion boundaries across calls, comparisons and branch results. The independent common literal prerequisite may precede A1. |
| A4 | Share the computation from acquired RHS and old target across local, Field, Property, index and Place-result updates. | RHS-first order; one selector/get/set; self-reference; independent/different Output; temporary destruction, control transfer and dependency conflicts. |
| A5 | Complete Item/Callable/erased Function paths, summaries, reanalysis/public output and measurements. Program 43 is reserved for original-source O0/O2 integration. | All paths below, CLI/JSON/LSP review, related Unit/native tests, Program 43 harness, fixed allocation/reuse and timing/code-size comparisons, final Session. CSP publication remains separately reported if unavailable. |

Share existing comparison selection, requirement instantiation, argument adaptation and replacement plans. Intrinsic witnesses lower to numeric operations only after common acquisition/effect checks. Preserve user Abort locations and operator check locations. Avoid boxing, indirect calls and temporary storage introduced solely by arithmetic; erased Function representation retains its necessary costs. Use interned identities and reusable scratch/storage, invalidated with changed Types, constraints, Origins, effects or source snapshots. Shared borrows do not imply disjoint operands.

## Acceptance matrix

| Case | Required result |
| --- | --- |
| Non-Copy Vec2 ordinary, numeric-left and unary conformances | Direct/generic/reference operators, explicit requirements, stored Items, Callable and compatible common Function calls execute. |
| `Time - Time -> Duration`, `Matrix * Vector -> Vector` | Left provider; result expectation checks only after unique selection. |
| A right Vector with only `LeftMultipliable<f32>` | `2.0 * v`, `(1.0 + 1.0) * v`, `(-2.0) * v` fit f32; pretyped f64 and integer `2` do not. Adding f64 makes untyped uses ambiguous. |
| Structurally fitting candidates with different Origin fitting success | Ambiguous; Origin relations cannot select a candidate. Unknown conditions cannot be discarded. |
| `L is Addable<R>` plus `R is LeftAddable<L>` | Definition-time direction conflict with both evidence locations. |
| `n + bump(n@uniq)` for built-in integers / numeric-left user operation | Built-in value snapshot may permit the former; shared inspection conflicts in the latter. `n@copy` supplies an independent temporary. |
| Result retaining pre-existing external dependencies / fresh inspection Loan | Preserve the former and reject escape of the latter. Result-valued Output needs explicit propagation. |
| Standard get/custom set on Non-Copy target; `x += x` | Permit ordinary shared acquisition and writeback after operation Loans end; reject live conflicting Loans, getter-temporary dependencies or result dependence on replaced contents. |
| Unsupported native i128/u128 division/remainder, including Wrapping witnesses | Keep the existing IMPL §21.5.3 boundary; language conformance remains valid. |

## Verification and costs

Every unit follows [VERIFICATION](VERIFICATION.md) and [DIAGNOSTICS §10](DIAGNOSTICS.md#10-diagnostic-development-workflow-detail). Capture intended public facts independently of implementation, with valid counterparts and independent-error interactions. Inspect representative CLI text/JSON and LSP ranges, Reasons, related locations and Advice before completion. Keep failing evidence; valid unsupported forms do not become language errors.

Start from ComparisonContractTest, ComparisonCompilationCostTest, AssociatedContractParameterTest, CallAdaptationTest, ReferenceLayerAdaptationTest, WrappingIntegerTest, ElementUpdateEmissionTest, ContentUpdateTest and IndexableContractTest. Add distinct-path regressions rather than redundant cross-products. Unit verification includes related allocation/reuse tests and native O0/O2; Session runs once at session end, without NativeAOT or concurrent source edits. Verify and commit each completed unit, associate its manifest, then push.

At A5 compare equivalent observations and inputs under identical O0/O2 conditions: built-in operators versus witnesses, user operators versus named functions, and arithmetic requirement Items/Callable/common Functions versus the corresponding existing function representation. Measure runtime, allocations and emitted code size separately from alias/effect/failure/cleanup semantics. Keep fixed warm-up and repetitions in Benchmark; test zero-allocation and retained-capacity guarantees independently. Do not claim an improvement without matched evidence. Program 43 builds its original source once per O0/O2 and executes each binary once, separately from feature/rejection fixtures.
