# Origin proof measurements

After a whole-solution Release build, run in isolation:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --origin-proof > artifacts/benchmarks/origin-proof.json
```

Use a fresh filename for each report. `OriginProofWorkloads` (shared with `OriginProofScalingTest`) supplies thirteen families
(PLAN G74, proposal §4.1): `valid`, `composite` and `composite-nokeep` (the original G74 shape, a failed relation over the meet
of an `if` join), `atomic`, `proven` and `transitive` (the same relation with clauses), `result` and `result-failed` (result
well-formedness premises), `anonymous` (an anonymous function input), `generic`, and `chain`, `chain-broken` and `cycle`
(clause graphs). The first eleven add `size` borrowed `ref/Holder` inputs; the clause families have `size` links.

Each family runs at sizes 1, 2, 4, 6, 8, 12 and 16. Every case runs in its own process (`--origin-proof-case family size`) with
a 30-second limit; a case over the limit, and the larger sizes of its family, are recorded as censored. The limit is a
measurement control, never a language rule. A case reports its cold Binding and ownership times, the proof counters of
`OriginProofMetrics` per phase (top-level requests, all proof entries, active-path rejections, deepest path, distinct
normalized queries), the outcome codes and, while one check stays below a second, the median of five warm checks after four
warm-ups. The report records compiler and benchmark module identities, configuration and platform.

## Targets (fixed in U0, 2026-10-09)

- Every case of every family completes below the limit through size 16 (no censored case).
- Proof work at a use is bounded by the extracted premise graph: per closure, worklist insertions do not exceed the reached
  Origin expressions, edge activations the premise edges and operand decrements the meet incidences (asserted in tests).
- The warm check of each family at size 16 stays within four times its size-1 warm check.
- Normal-case cost: the warm check of `valid` at size 1, which is dominated by the library's own proof requests, does not
  regress beyond run-to-run variation, and warm allocation guarantees are unchanged.

Timing has no pass/fail threshold in Verify; the targets are compared in repeated matched runs against the U0 baseline.
