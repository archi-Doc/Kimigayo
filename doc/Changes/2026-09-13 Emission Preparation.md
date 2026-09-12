# Compiler pipeline and emission preparation

This refactoring preserves the literal-output execution boundary of C.34–C.37 while preparing the concrete layout, value and ABI records required by SPEC §21.4. It adds no new source-language execution features. SPEC's language rules are unchanged.

## Pipeline review

`Compilation.Bind` now performs one final pass. The former provisional pass was immediately discarded by final Binding's complete semantic reset; the intervening `RunMods` method was empty. Explicit `Binding.Bind(Provisional)` remains available and tested. When Mod execution is implemented, provisional queries and append/rebind boundaries must be inserted before final Binding as specified in §20.7.2. Provisional results must never constrain that final pass.

Parsing, final Binding, startup selection, control-flow and ownership verification remain distinct obligations. Their analyses and reusable storage are retained. They are not interchangeable merely because this small executable slice exercises only a few of their operations. In particular, the syntax-only control-flow API is still useful to front-end clients; emission requires the analyzed ownership CFG.

Project compilation runs each synchronous target directly, without an async wrapper per target or redundant public forwarding state machines. One Task boundary preserves the existing asynchronous exception/cancellation contract. Configured targets are no longer copied before a synchronous traversal. `Check`, `Generate`, `Build` and `Run` retain their respective contracts.

## Emission boundaries

| Component | Responsibility |
| --- | --- |
| `MinimalEmission` | Existing public entry point; checks the entire selected input and prepares a closed plan. |
| `EmissionPlan` | Lowers verified evaluation order, physical slots, call operands and existing cleanup decisions. Retains original operation/Place IDs without cloning the AST. |
| `WindowsLowering` | Reused concrete `TypeLayout`, `ValueLowering` and `FunctionAbi` records for the implemented profile. |
| `LlvmModuleWriter` | Serializes only prepared physical facts; never queries Binding, startup syntax or ownership state. |
| `EmissionArtifacts` | Publishes a matched IR/manifest pair, manifest last. |
| `ArtifactPaths` / `ArtifactFiles` | Shared output identity, path validation and streamed SHA-256 calculation for generation and native tools. |

The public name `MinimalEmission` remains compatible with existing callers. It describes the supported language slice, not the organization of the writer. Its former unchecked `WriteValidatedIr` entry point is removed. Publication prepares once and synchronously consumes that plan instead of validating, then independently rediscovering the AST and argument state. Plans and buffers are compilation-local scratch; they are not immutable snapshots, concurrent work items or certificates for later compilations. Each public emission request checks current analyses again, and failed preparation makes the retained plan unwritable.

The straight normal path is followed in CFG order, rather than operation-list order. Unit operations retain their semantic verification but produce no physical instructions or slots. Empty blocks and unconditional branch chains disappear. The caller's terminal abort edge belongs to the nonreturning Core implementation and creates no artificial continuation block. Every reachable operation must be accounted for; unsupported selected operations are rejected before this compaction. Cycles, unsupported branches, missing successors, missing/mismatched cleanup steps/plans, and nonterminal abort destinations fail preparation. A `ret void` is emitted only after a normal Exit is proved; no guessed return or `unreachable` substitutes for missing lowering.

The resulting Hello body contains one string slot, one constant aggregate store, one Core call and one return. Ownership of the argument still transfers to Core, whose normal path performs destruction once. The caller does not duplicate it.

## Concrete representation and reuse

Unit has no physical argument or storage. String retains its runtime layout: allocation size/stride 24, alignment 8, and field offsets 0/8/16. Its computation/storage Type is `%kimi.string`; its owned physical argument is an ordinary `ptr` to the initialized slot. Unknown Types have no fallback representation. These fixed runtime records do not implement general aggregate layout modes, field-Identity mapping, scalar conversions, borrowed/object representations or aggregate results.

Compiler-generated calls and the corresponding runtime definitions use the same `FunctionAbi` records. `WindowsRuntime.ll.in` is explicitly a template: compiler-facing exit, string destruction and writeLine signatures are expanded from those records once. The other hand-written runtime helpers retain their reviewed implementations. The ABI uses LLVM's default ccc, omits Unit arguments, and does not infer `byval`, `sret`, aliasing or optimization attributes.

Each literal/location constant retains its escaped UTF-8 encoding and byte length. Warm output copies that prepared text instead of counting and encoding the same bytes again. NUL remains data and supplementary Unicode characters remain complete UTF-8 sequences. Scratch lists, CFG marks, text capacity and escaped source locations are reused. Iteration over ownership's read-only lists uses indexing to avoid boxing enumerators.

Native builds share one resolved artifact set through publication. The IR hash already verified against the manifest is reused in the build record; the file is not redundantly hashed for that record. Both publication and native tools use the same stack-buffer SHA-256 helper and path rejection rules. The embedded profile JSON is parsed once for both backend and kernel32 generator identities.

## Subsequent implementation

Extend the selected-body gate and lowering together, with unsupported-case tests before enabling a feature. For functions, introduce the SPEC worklist keyed by declaration Identity, concrete arguments and selected implementation, and establish all physical signatures before writing bodies. For new Types, supply actual layout, value conversion and ABI evidence instead of treating an unknown value as `ptr`. For branches/locals, replace the currently proved straight path with explicit blocks and terminators, while retaining edge-specific cleanup ordering and initialized/moved state. Loans/Origins must be verified before borrowed representations can enter the plan.

These are remaining implementation boundaries, not placeholder classes or claims that general execution is already supported.

## Validation

Eleven new cases cover the compact physical body, rejected continuations/cleanup, reuse after reanalysis, ABI/template consistency and changing UTF-8 constants. Existing stale-analysis, unsupported selected-body, publication integrity and warm zero-allocation regressions continue to pass. Debug and Release each pass 2,840 C# tests. Native LLVM 22.1.8 validation passes 64 O0/O2 executions per compiler configuration, including LLVM/C string layout, unwind/dependencies, byte-exact output and runtime faults. Release CLI integration covers emission, native builds, execution, failure invalidation and tool identity handling. No throughput measurement is claimed.
