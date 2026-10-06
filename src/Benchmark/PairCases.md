# Semantics-case runs

`Benchmark --pair-cases` shares its input with `GenericCaseAllocationTest` through
`tests/Workloads/VerificationWorkloads.PairCaseFamilies`: one generic definition with one pair binder
(`s is owner or uniq`, two cases) whose uniq case Reborrows a local, stores it through an exclusive
reference, captures it in a closure and passes it bare, one definition with two binders (four cases),
the closure body and `main`, all valid in every case (SPEC §8.10). The stored-reference workload of
`Benchmark --borrow-storage` (32 locals, stored) runs beside it as a reference. It measures the first
ownership-analysis allocation, the bytes of one warm analysis, five samples of eight warm analyses after
eight fixed warm-ups, and five samples of eight warm rebindings after 32 warm-ups. Parsing is outside the
measurement. Normal verification asserts warm zero allocation without timing.

## 2026-10-06 (G75 U6)

Windows x64, Release, .NET 10.0.12, `dev` at the U6 commit. The definition bodies run once per
Semantics case (seven case runs over the ten listed bodies); side bodies are pooled.

| Workload | Listed bodies | Places | Operations | First analysis bytes | Warm analysis bytes | Warm analysis ms (5 × 8) | Warm rebind ms (5 × 8) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| pair-case-families | 10 | 117 | 424 | 344,784 | 0 | 1.45, 1.46, 1.45, 1.43, 1.44 | 11.8, 10.7, 9.8, 9.7, 9.6 |
| borrow-dependency-reference | 3 | 263 | 889 | 551,832 | 0 | 7.67, 7.75, 8.02, 7.84, 7.63 | 6.23, 6.17, 6.14, 6.25, 6.07 |

A second run agrees within the sample spread (analysis 1.45–1.48 ms; rebind 8.1–13.1 ms, still
descending with tiering). Timings are observations under the fixed conditions, not a comparison: the
switch did not exist before G75, and the case runs replace the owner-view template run of the same
bodies. Evidence: `artifacts/benchmarks/20261006-g75/pair-cases-{1,2}.json`.
