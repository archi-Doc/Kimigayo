# Shared object measurements

Use the commands and fixed conditions in [ObjectPlans.md](ObjectPlans.md). Compiler plan measurements include the
same rc/arc factory/clone workload used by the allocation regressions, with 32 warm-up iterations and seven samples
of 64 iterations for each phase. Native measurements run the fixed finalization and Dictionary workloads at O0/O2,
seven executions per binary with no warm-up or allocation-audit instrumentation. Reports retain source/compiler hashes
and every sample; process startup is included. Do not run builds or other measurements concurrently.

`SharedObjectRuntimeTest` and `SharedArcRuntimeTest` separately verify checked counts, clone lifetime and exact native
allocation/free bounds. Arc additionally checks whole-word CAS orderings, observed-value retries, the final acquire
fence and controlled failed-comparison executions. The argument and proof boundary are recorded in
[SHARED_OBJECT_RUNTIME.md](../../docs/dev/SHARED_OBJECT_RUNTIME.md). Timing samples do not establish correctness,
cross-thread payload safety or a speedup; source concurrency and Weak transitions are outside this implementation.

U5 opens inline arc and original Program 34 execution. Content-sensitive updates with external Origins (U6/G70) were
verified on 2026-10-06; Program 34 remains IN_PROGRESS pending its remaining milestone completion checks. These measurements do not
remove that boundary.

The 2026-10-04 U5 run retained all 56 compiler samples with zero measured managed bytes in
`artifacts/benchmarks/object-plans-u5-20261004.json`. Native evidence is under
`artifacts/benchmarks/object-finalization/20261004-180634-u5-rc-arc/`: both modes and both workloads passed their
checked O0/O2 executions, with seven timing samples per binary. These are baseline observations, not a matched
before/after speed comparison.
