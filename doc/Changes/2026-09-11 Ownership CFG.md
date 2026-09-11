# Whole-Place ownership CFG and cleanup plans

Implements the first ownership verification subset for SPEC §§3.6.2, 8.10, 13.4, 13.7.1, 15.1.1 and 21.4. This does not generate LLVM IR or executable output.

## Pipeline and supported boundary

`Compilation.Ownership.Analyze()` runs after final Binding. It reuses the existing control-flow analysis for lexical transfer targets, result coverage and unsafe checks. It consumes committed calls and argument adaptations without another member lookup or candidate selection. `Project.Build` now requires successful startup and ownership checks. A successful result still certifies only the implemented front-end subset.

The analysis visits selected project function bodies, including unused functions and the generated source body. Excluded directive syntax is absent. All bodies must pass Binding, control-flow and supported ownership checks before any body is marked verified. Every Bind invalidates verification. Analysis results, body tables and cleanup slices remain valid only until reanalysis or invalidation; callers must not retain them as immutable snapshots.

| Covered | Still unsupported |
| --- | --- |
| Primitive whole locals and parameters, including owned string and Unit | Fields, partial Moves, aggregates, static storage and object/base projections |
| Literals, scalar operations and simple string comparisons | String concatenation and nonnumeric compound assignment |
| Direct calls with explicit owned arguments; receiver-first order from BoundCall | Borrow/Reborrow adaptations, defaults, indirect calls, function values and captures |
| If, short-circuit Boolean expressions, while, return, exit and continue | Match, value-yielding/labeled transfers and general pattern/iteration lifetimes |
| Scope exit, argument interruption, replacement and normal return cleanup | Executing defer, user destruction, Loans, Origin dependencies and escape analysis |
| Symbolic whole-Type Copy/Move acquisition | Concrete generic generation, complete generic metadata and executable finalization |

Simple comparison operands are inspected without Move. A non-Copy Place view retained across a complex right operand remains unsupported because it needs a Loan covering operand evaluation. Unsupported operations in unexecuted branches are still reported. Literal control conditions restrict reachable state propagation; this is not general constant evaluation.

## Operations, state and responsibility

Each body has dense Place IDs for locals, parameters, temporaries and its secured result. Operations classify Read, Consume, Write and Borrow uses. Read checks initialization; a selected Borrow also prevents subset verification. Consume retains Copy, Move or CopyOrMove without changing the static result Type. CopyOrMove preserves the possible initialized state but removes definite initialization and records a possible Move. A later use must therefore be legal at the generic definition; a favorable instantiation cannot rescue it.

At each CFG point, a byte per Place records must-initialized, may-initialized, may-moved and may-assigned facts. Joins intersect the first fact and union the others. A worklist solves loops before diagnostics or placement decisions are finalized. Declare starts a fresh binding lifetime on every loop iteration. Move preserves assignment history, so moving a let never permits assigning it again.

Binding continues to reject structurally forbidden assignment, including parameter assignment and let increment/decrement. Simple assignment to a let local in its own function reaches CFG checking. The CFG diagnoses uninitialized use, possible use after Move and repeated let placement, avoiding duplicate Binding/state diagnostics.

Write placement depends on the state after RHS acquisition. Plans distinguish initialization, reinitialization, replacement and conditional replacement. For example:

```kimi
func print(c: bool)
    var text: string
    if c
        text = "first"
    text = "second" // Conditional replacement: destroy only if initialized.
    text = text     // Move RHS, skip absent old value, restore storage.
    writeLine(text)
    // writeLine(text) // Error: text may not be read after Move.
```

## Cleanup output

`CleanupPlans` indexes ordered slices of `CleanupSteps` on CFG edges. Steps retain a Place, its registration source and Destroy/Skip/Conditional action. Replacement cleanup is recorded on the incoming write edge. The solver emits these plans during verification; lowering must not rerun ownership inference to reconstruct them.

Temporaries have their own initialization and responsibility state, including Static string literals. Outermost expression completion destroys remaining temporaries in reverse creation order. Exiting scopes then destroy locals in reverse declaration order. Deferred registrations occupy their lexical positions but remain explicitly unsupported. Parameters precede body declarations and retain their written order.

Argument temporaries belong to the caller until all explicit arguments are acquired. CallEntry transfers responsibility to the callee. A return during a later argument cleans the earlier acquired temporaries in the caller. The callee secures its result before cleanup and delivers it only on a normal return edge. Call result initialization occurs only after normal completion; Abort edges have no normal cleanup or result production.

```kimi
func makeText() -> string => "temporary"
func accept(text: string, number: i32) => ()
func example(c: bool)
    accept(makeText(), if c => return else => 1)
    // The return path cleans makeText()'s result before accept is entered.
```

## Storage and verification

Body and control-flow objects, identity indexes, adjacency lists, worklists, state arrays and plan buffers retain capacity. Indexed incoming edges avoid scanning the graph again for each write. Existing control-flow result sources are value records with their transfer Identity, replacing a separate reference-keyed mapping. Both analyses traverse collection fields by index where interface enumeration would allocate.

The initial solver uses a dense operation-by-Place byte matrix. Its retained state space is O(operation count × Place count) per body; zero warm allocation is not a constant-memory or throughput claim. Block-level or sparse state storage is a possible later optimization for much larger bodies.

`OwnershipAnalysisTest` covers state joins, loop fixed points, let history, Read and implicit Borrow, symbolic CopyOrMove, temporary and return responsibility, conditional/self replacement, cleanup order, unsupported boundaries, Build integration and reBind invalidation. Workloads with 1, 32 and 128 conditional string locals assert zero bytes for warmed Bind plus both analyses. This class runs separately from other test classes to avoid overlapping allocation measurements. `OwnershipAnalysisBenchmark` exposes analysis-only and combined workloads for independent throughput measurements.

Next: finalize the supported concrete generation set and validate its operations/layout/runtime requirements before LLVM emission. Extend Loans, partial Places and defer through the same operation and cleanup interfaces as their verification becomes available.
