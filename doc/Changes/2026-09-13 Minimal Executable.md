# Minimal literal-output executable

This increment implements the one-line `::Core.writeLine("Hello, world!")` Application from SPEC §22.4, through checked pre-optimization `.ll`/`.link.json` publication and separate native validation. It deliberately does not implement the broader ordinary-function/local-variable execution subset of STATUS C.12.

## Reused analyses and boundary

`Compilation.Emission` uses the existing final Binding, canonical Core Symbol, startup selection, ownership CFG/Place states and cleanup plans. The gate requires one document, one implicit startup body and one direct literal call; additional selected implementation bodies, containers, external modules and other executable operations fail before output. This includes unused nongeneric bodies; optimization cannot hide them. The current implementation also conservatively rejects additional generic declarations. Front-end acceptance of such programs is unchanged.

Only Unit and a Static string temporary can appear in the accepted CFG. Literal construction establishes valid UTF-8 and permanent backing; the sole owned argument is initialized before call entry, and no borrow, Origin-dependent operation, Flow Type refinement, projection or unsupported cleanup is permitted. Runtime implementations preserve that contract. This closed subset discharges its lifetime requirements without claiming a general Loan solver or a complete Core library. Validation failure supplies no IR or publication certificate.

The emitter writes the existing CFG directly. It does not clone Koto trees, resolve calls again, create a second general IR, or add an SSA optimizer. Unit retains logical state while having no physical slot. The internal string slot is `{ ptr, i64, i8 }`, with the verified Windows DataLayout; LLVM and C layout tests agree on size 24, alignment 8 and offsets 0/8/16. Core's owned string parameter is a pointer to its argument slot, without `byval`/`sret`. Abort exits never return or perform normal cleanup.

## Runtime and performance

One embedded LLVM runtime definition supplies the six operations. Stdout and stderr share a numeric-range-checked, DWORD-chunked, short-write-aware adapter. Abort uses constants and a ten-byte decimal buffer, stops diagnostic writes after failure and terminates without recursion. String output writes its bytes and a separate LF, then destroys its argument. Static storage is never freed; Heap storage is released once on normal return. Hello World requires no heap allocation or concatenation buffer.

Builder capacity and escaped location text are retained per compilation. UTF-8 constants are encoded with stack scratch and Rune iteration instead of temporary UTF-8 arrays or formatted strings. The common runtime text loads once. Warm validation and warm IR writing to `TextWriter.Null` each allocate zero bytes in the measured regression; source parsing, first-use buffers, file/JSON publication and output storage are outside that assertion. `DiagnosticCollection.HasErrors` removes snapshot-array creation from source/error gates and tracks add/remove/clear. No throughput improvement is claimed.

## Configuration and output

`OutputPath`, `Optimization`, `LlvmBin` and per-target `NativeLibraries` serialize in `.kimiproj`. `OutputKind` already existed. LlvmBin is recorded as an optional manifest-relative hint, never executed by the compiler or used to override LLVM 22.1.8. The supplied `C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin` reports 22.1.5 and is rejected by the manual builder; formal validation used the existing local 22.1.8 archive extracted into ignored workspace storage. The independently configured kernel32 file matches SDK 10.0.22621.0 x64.

The compiler embeds the single backend catalog from `backend/windows-x64/profile.json`. Both reserved libraries are recorded, regardless of whether optimization removes backend references. Explicit backend files must match the catalog hash. The manual builder verifies actual files again, checks all actual object dependencies and retains actual tool/library identities. Each complete output pair contains a hash of the exact pre-optimization IR; temporary files finish first and the manifest publishes last. Publication failure never returns a successful path, even if one destination was replaced. Consumers still verify the hash against interruptions/mixed pairs.

`Project.Generate` and `Solution.Generate` add artifact production through the shared existing build path; `build` now invokes generation and reports failure as exit code 1. The existing `Build` APIs remain explicitly front-end-only for callers/tests. A loaded project includes `.kimi` files immediately in its project directory. The example and documentation make that source selection and the narrow generation scope visible. No compiler-driven tool discovery, download, optimization, linking, execution, debugger integration or new source I/O API is added.

## Verification

Unit coverage includes positive literals/qualified and named calls, unsupported/unused bodies, ownership failures, stale-analysis invalidation, zero-allocation warm paths, settings roundtrip and the actual example, hash/path/schema checks, failed publication and checkout-independent logical diagnostics. The native suite separately verifies byte-exact Hello/empty/Japanese/NUL output, O0/O2 LLVM verification, object/unwind data and custom-entry `/NODEFAULTLIB` execution for compiler Debug/Release. Each configuration has 64 native executions, including 29 fault-adapter modes at both optimization levels. Additional manual-builder tests reject mixed IR hashes, wrong catalog version/hash, unknown dependencies, wrong CPU and LLVM 22.1.5.

The helper archive is adopted as package 1.0.0 after two reproducible builds, helper tests, assembly/unwind review and emitted-module execution. This is the literal-output milestone, not a claim of complete SPEC §A.14 coverage for unsupported language features. Full Core execution, general source functions/locals, Library emission, general layout/ABI and borrowed/object execution remain pending.
