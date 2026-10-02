# Wrapping arithmetic timing (P42)

`src/backend/windows-x64/benchmark-p42.ps1` builds `src/Benchmark/Kimi/HashWrapping.kimi` and
`HashChecked.kimi` at O0 and O2 through the milestone harness (`milestone-harness.ps1 -Source -WorkRoot -Runs`),
checks each first run's output and times the repetitions. Timing only, outside normal Verify.

The wrapping program computes FNV-1a over a 63-byte text for 1,048,576 rounds and 33,554,432 xorshift64* draws
on `Wrapping<u32>` and `Wrapping<u64>`: every step wraps by Type, with no check and no mask. The checked program
computes the same values on `u64` and `u128` with masks, so every multiply carries its overflow check and every
narrowing its range check; this is the form STYLE §3.4 asks to avoid. Both print the same line, which the harness
compares byte for byte. The literal shift counts carry no check in either program (impl §21.5.3).

## 2026-10-01 measurement

Windows x64, Release compiler, 5 runs per level; the first run of each fresh executable is an outlier in every
series, so medians are compared.

| Program | O0 median s | O0 min s | O2 median s | O2 min s |
| --- | --- | --- | --- | --- |
| HashWrapping | 0.4398 | 0.4119 | 0.1121 | 0.1116 |
| HashChecked | 0.4488 | 0.4440 | 0.1118 | 0.1111 |

Checked / wrapping median run time: O0 1.02, O2 0.997. A quarter-size workload (262,144 rounds, 8,388,608 draws)
measured 1.025 and 1.034 in the same session.

The checks of the masked form cost nothing measurable on this workload: at O2 the optimizer removes or folds them,
and at O0 the loads and stores of every temporary dominate both programs. On these loops the wrapping Types buy
the absence of the widening and the mask, and the compile-time guarantee that nothing Aborts, not throughput.
Evidence: `artifacts/benchmarks/p42-hash-prng/20261001-154147` (full workload) and `20261001-154039`
(quarter-size workload); each holds `summary.json`, `summary.md` and the harness reports.
