# O2 timing corpus

The pre-R5 timing baselines (COMPILER_ARCHITECTURE R0 row; A5 plan §9.1 "Timing"): a fixed corpus of native O2 kernels and
the compiler pipeline series, both run A/B/A so that a later code generator is compared with the same inputs, order and
statistics. Generated O2 code may not regress (plan §18 P1); a timing regression exceeds the variation of repeated matched
runs (P4), which the baseline records by running the same checkout as A and B.

## Kernels

`src/Benchmark/Kimi/Corpus` holds one Application per kernel. Each prints one checksum line, which the harness checks on every
run; O0 and O2 print the same value. Each round's values are summed and the sums folded by an FNV-1a step and an xorshift, both
invertible, so a wrong value in any round changes the line. Each runs a few tenths of a second at O2, so process start is a small part; the series records the actual times.

| Kernel | Work |
| --- | --- |
| `Array` | `Array<T>` of a two-Field struct grown from empty by 1,024 appends, shared iteration, strided indexing, `swap`, `pop` and `clear`, 131,072 rounds |
| `ArrayLiteral` | Array literals of 1, 8 and 32 `i64` elements and of 4 two-Field structs, each built (one allocation), indexed and destroyed, 2,097,152 rounds; its checksum is compared in place and printed as a fixed line (DECISIONS R5-M129) |
| `Dictionary` | `Dictionary<i64, i64>` of 64 entries (insertion, lookup, removal, shared iteration) and one of 32 entries keyed by a user `Equatable` struct, 16,384 rounds; lookup is a linear search |
| `Interpolation` | An interpolated owning string with a user `Utf8Format` value, and `$tryWrite` into a cleared fixed buffer, 1,048,576 times |
| `Iterators` | A user Iterator through a generic consumer, Slice iteration and `Iteration.owning`, 8,388,608 rounds |
| `ErasedCalls` | Calls through erased `(i64) -> i64` values of two function items and two closures (environments of 8 and 24 bytes) held in an Array, 2^27 calls |
| `VirtualDispatch` | `virtual`/`override` calls through `objref` base views of four Dynamic Types, with direct base calls, 2^26 calls |
| `SharedHandles` | rc creation, clone into an Array, owning iteration and release, and arc clone and release, 1,048,576 rounds |
| `Tuples` | Tuple operators: an insertion sort of 512 `(i32, f64, string)` by `<` (later leaves decide ties), `==` over all pairs of 128 `(i64, i64)` and `((i32, i32), (i32, i32))`, and `<=`/`>` over all pairs of 32 `(f64, f64)` with a NaN and ±0, 512 rounds |

The erased and virtual calls stay indirect calls in the optimized IR, so the kernels time dispatch rather than inlined code.

## Protocol

`src/backend/windows-x64/benchmark-o2-corpus.ps1 [-Baseline <checkout>] [-Candidate <checkout>] [-CandidateEnvironment NAME=value,...] [-Samples 15] [-PipelineRuns 3] [-Optimization O2]`
takes two checkouts with Release builds of Kimi and Benchmark (`dotnet build Kimigayo.slnx -c Release`); each defaults to the
script's own checkout, and Benchmark's copy of Kimi.dll must be that checkout's compiler.

`-CandidateEnvironment` sets its variables only for B's kernel builds and B's `--compiler-pipeline` runs, for example
`KIMI_PIPELINE=mir`; both variants' executables run in the script's environment. A runs the default pipeline (MIR code generation
is the one code generator from R6-gen's G4): the script fails before any work if it would inherit `KIMI_PIPELINE`.
Pass the list with `pwsh -Command` or as separate array elements: `pwsh -File` binds `A=x,B=y` as one string, which the
script rejects.

- Both compilers build every kernel at O2 before any timing, recording build time, IR and optimized IR bytes, `.text` size
  and the SHA-256 of the instructions without addresses and immediates (`sameCode`); each executable's first run is checked
  and timed apart.
- `-Optimization O0` builds and times the same kernels at O0 instead, for the O0 times R5 records (plan §5.3 item 5): the
  same samples, order, statistics and evidence, with `optimization` in the conditions and no optimized IR bytes (an O0
  build runs no `opt`). The pipeline series does not depend on it.
- Per kernel, `Samples` + 1 runs of A alternate with `Samples` runs of B: A, B, A, ..., A, with no warm-up, process start
  included and each run's CPU seconds recorded. Per variant: minimum, median, maximum, mean, standard deviation and
  coefficient of variation; per B run, its ratio to the mean of the A runs before and after it, summarized the same way.
- `Benchmark --compiler-pipeline` ([CompilerPipeline.md](CompilerPipeline.md)) of A and B then alternates A, B, ..., A for
  `PipelineRuns` + 1 runs of A. Every report must have the first one's schema, stage names and input hashes. Its generic shape
  at 32, 64 and 128 item Types is the generic-heavy input at 1×, 2× and 4×. Collection is timed in stage 3 (`BuildMir`), so
  emission lowering plus IR writing is code generation alone; CompilerPipeline.md gives the comparison that adds stage 3's
  collection share. Per
  input, each run gives the median of its samples for that stage and for the whole compilation; their spread across runs and
  the B/A ratios over them are the matched-run variation. Allocated bytes are each run's mean.

`artifacts/benchmarks/o2-corpus/<stamp>[-<name>]/` keeps `summary.json` (status, conditions, every sample and the statistics),
written also when a run fails, and each pipeline report with its stderr; built executables stay under `temp/o2-corpus/`. The
conditions record the checkouts' commits, `git status` lines, compiler hashes and the variables set for each (A none), the
machine, power scheme, .NET version and inherited `KIMI_*`/`DOTNET_*` variables, and the build, test and LLVM processes with
their CPU seconds at start and end.

Run only on a quiet machine: no build, test or other measurement, here or in another worktree, which the process lists and
the runs' CPU seconds should confirm (Windows counts CPU time in steps of about 16 ms, enough to show preemption, not
small differences). Use committed checkouts. Keep failed and noisy runs. A timing sample does not establish
correctness, allocation bounds or a speedup; `sameCode` separates a code change from noise.
