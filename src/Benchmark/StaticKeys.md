# A4 static keys

`Benchmark --static-keys` measures A4's static keys (DECISIONS R4-M38, PLAN G93) on generated programs: N members of
group `H` (256, 512, 1,024, 2,048) and F mutable static Fields of group `G` (0, 16, 64, 256, 1,024). Member n touches the
Fields n mod F, then every N-th, so every Field is touched; even members write, odd members read. Three shapes:

- `chain`: each member calls the next and `chainHead` the first (callers first, as R4-M38's probe);
- `ring`: the chain, and the last member calls the first under an immutable flag, so every member reaches every Field;
- `star`: `chainHead` calls every member and no member calls another, so each node holds few keys.

F = 0 is the baseline. Each Field adds its initializer body, and each access a `staticEnsure` call into it, so nodes are
the library's 217, plus N + 1, plus F.

The MIR is built (`Bind`, `BuildMir`) outside every interval. A4 then runs the driver's rows, as `MirEffectsTest.Analyze`
does: `Local` per view, `Run` (the fixed point, `Spread`, the obligations), then a `Keys` query at every Call and Drop of
every view A2 completed, as A5 reads them. One pass warms the shared case views and dataflow (A1–A3 do in the driver), and
each measured A4 is a fresh `MirEffects`.

- The allocation mode reports current-thread bytes per phase over three fresh instances (minimum, maximum), a warm re-run,
  and the managed heap one fresh instance retains (`GC.GetTotalMemory(true)` around it, ±8 KB).
- `--static-keys timing` needs `DOTNET_TieredCompilation=0`. It pins the process to CPU 2 at High priority and interleaves
  the five F programs of one shape and size round-robin: 4 warm-up and 7 recorded rounds, with a full collection before
  each pass, outside the intervals. It reports minimum, median and samples per phase.

## 2026-10-21 (A5 step 2 unit 2b-2)

Windows x64, Release, .NET 10.0.13, `dev` at 4b36db41. Allocation is deterministic: two runs agree in every cell, and
min = max in each. A warm re-run allocates 0 B everywhere. A throwaway reflection probe confirmed that the key storage is
exactly the implemented sizes in all 60 cells:

- `keys`: nodes × ⌈2F/64⌉ words;
- `keyScratch`: ⌈2F/64⌉ words;
- Spread's four arrays: one entry per node, but only when F > 0.

Chain shape (ring and star allocate the same in Local and Run):

| N | F | Nodes | Key words | Cold A4 bytes (Local + Run + queries) | Key arrays, share of cold / retained | Retained bytes |
| --- | --- | --- | --- | --- | --- | --- |
| 2,048 | 0 | 2,265 | 0 | 504,312 | 0 / 0 | 275,488 |
| 2,048 | 16 | 2,281 | 2,281 | 849,272 | 9% / 15% | 479,888 |
| 2,048 | 64 | 2,329 | 4,658 | 876,304 | 11% / 18% | 504,056 |
| 2,048 | 256 | 2,521 | 20,168 | 1,035,328 | 21% / 34% | 650,288 |
| 2,048 | 1,024 | 3,289 | 105,248 | 1,864,608 | 49% / 64% | 1,423,112 |
| 256 | 1,024 | 1,497 | 47,904 | 844,472 | 49% / 65% | 646,976 |

Growth of the key words:

- per doubling of N at fixed F, ×1.17–1.81, tending to 2: linear in nodes;
- per ×4 of F at fixed N = 2,048, ×2.04, ×4.33 and ×5.22: ⌈2F/64⌉ steps by 2, 4 and 4, and the initializer nodes grow with F.

So the storage is quadratic where F grows with the program. The queries allocate only their result buffer, once
(16 B × the largest result).

What they return grows with sites × reached Fields: 2,098,176 keys over 4,826 sites at ring N = 2,048, F = 1,024,
against 2,048 at star.

Timing was measured on the same build on a quiet machine (CPU load 2–3%), in two runs. Medians in ms at N = 2,048,
run 1 / run 2:

| Shape | F | Local | Run | Queries |
| --- | --- | --- | --- | --- |
| chain | 0 | 0.77 / 0.71 | 0.21 / 0.13 | 0.48 / 0.41 |
| chain | 16 | 1.11 / 1.15 | 0.40 / 0.34 | 0.80 / 0.73 |
| chain | 256 | 1.20 / 1.15 | 0.38 / 0.37 | 1.67 / 1.59 |
| chain | 1,024 | 1.36 / 1.23 | 0.47 / 0.46 | 3.93 / 3.84 |
| ring | 0 | 0.68 / 0.70 | 0.18 / 0.11 | 0.43 / 0.40 |
| ring | 1,024 | 1.23 / 1.15 | 0.47 / 0.48 | 4.74 / 4.85 |
| star | 0 | 0.74 / 0.79 | 0.14 / 0.15 | 0.43 / 0.44 |
| star | 1,024 | 1.24 / 1.30 | 0.40 / 0.40 | 0.82 / 0.80 |

- **Run** times the fixed point, Spread and the obligations together; Spread cannot be timed alone without a compiler
  counter. Run stays at or below 0.48 ms. F > 0 adds the initializer edges' evaluations: about ×2 in chain and ring.
  F = 1,024 adds 0.1–0.2 ms over F = 16–256, which bounds Spread's (nodes + dependencies) × ⌈2F/64⌉ work at this size.
  Per doubling of N, Run grows ×1.1–2.0 (linear). Values below 0.5 ms differ by up to ×1.6 between runs.
- **Queries** grow with the keys they return. At F = 1,024, per doubling of N: ×1.39–2.57 (chain) and ×1.56–1.90
  (ring), against ×1.11–1.58 (star). Ring N = 2,048 returns 2.1 M keys in 4.7 ms (about 2 ns per key). That is the
  largest A4 cost measured, and it is quadratic where both sites and Fields grow with the program.
- **Local** gains 0.4–0.6 ms once F > 0, from the initializer bodies and the `staticEnsure` calls, and stays flat
  across F.

Evidence: `artifacts/benchmarks/20261021-000546-g93-keys/` (raw JSON, tables, command lines, the probe patch).
