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
`OriginProofMetrics` (top-level requests and premise closures per phase, nested environments, nodes, edges and meet
incidences with the largest environment, worklist insertions, edge activations and operand decrements, closures over their
work bound, the time spent building and closing environments, edges per catalog rule and distinct normalized queries), the
outcome codes and, while one check stays below a second, the median of five warm checks after four warm-ups and one more warm
check with the counters on, which reports the share of that check spent in environments and closures. The report records compiler and benchmark module identities,
configuration and platform. The U0 baseline (`artifacts/benchmarks/g74-u0`) predates the closure and reports the previous
search's counters instead: all proof entries, active-path rejections and the deepest path.

## Work bound (U2-U3)

One request resolves its endpoints at the use and, unless a structural instance of I1-I4 answers, builds one environment:
the clauses of the established enclosing declarations (R1), the inputs (R2) and result (R3) of each enclosing signature,
associated formation and requirements (R4), and the stored Origins of each borrowed complete local Place among its
expressions (R5). Each Type is visited once per environment, so the graph has `V` nodes, `E` edges and `M` meet incidences
bounded by the contracts and complete Types visible from the use's declaration chain plus the local Place chains reachable
there, and one closure is `O(V + E + M)`. `OriginProofScalingTest` asserts the per-closure bound at sizes 8 and 32 and that
the largest environment grows linearly with a family's size.

## Targets (fixed in U0, 2026-10-09)

- Every case of every family completes below the limit through size 16 (no censored case).
- Proof work at a use is bounded by the extracted premise graph: per closure, worklist insertions do not exceed the reached
  Origin expressions, edge activations the premise edges and operand decrements the meet incidences (asserted in tests).
- The warm check of each family at size 16 stays within four times its size-1 warm check.
- Normal-case cost: the warm check of `valid` at size 1, which is dominated by the library's own proof requests, does not
  regress beyond run-to-run variation, and warm allocation guarantees are unchanged.

Timing has no pass/fail threshold in Verify; the targets are compared in repeated matched runs against the U0 baseline.

## Environment lifetime (U5)

An environment lives for one proof request: its storage is reused by the next request at the same nesting depth, released of
every Origin, Type, declaration and syntax reference when the request ends, and its capacity is the largest environment it has
held (`OriginPremiseLifecycleTest`). Sharing environments or closures across requests was measured and not adopted: building and
closing all environments of a warm check takes 0.2-0.4 ms of a 36-45 ms check at sizes 1 and 16 (2026-10-09), so reuse could
save at most about one percent while adding invalidation dependencies on premises, substitutions and the excluded result owner.

## Results against the targets (G74 closure, 2026-10-09)

The final run (`artifacts/benchmarks/g74-final`, c6506786) meets every U0 target: no case is censored through size 16, no
closure exceeds its work bound, each family's warm check at size 16 is 0.80-1.40 times its size-1 check, and the normal-case
`valid` check matched the pre-closure build within run-to-run variation in the interleaved A/B of `artifacts/benchmarks/g74-u3`.
