# R4 exit evidence (2026-11-03; DECISIONS R4-M165)

The owner's 90% rule (verbatim 「根本的な解決は後回しにする。90%の状態で良いので、R4を完成させて下さい。」 and 「先ほどの手順通りでなくても良いので、
現在までの成果を活用し、早期にR4を完成させて下さい。細かい不具合は、後で修正します。」) shortened the A5 Step 2 plan's exit units X-1 to X-6. These
files are their evidence, read on `origin/dev` 3283d9aaa and the mir ledger of shared base 1eb0cbaa9.

| File | Exit unit | Result |
| --- | --- | --- |
| `x1-half1.tsv`, `x1-half1.md`, `x1-half2.tsv`, `x1-half2.md` | X-1, the ledger's open blocking rows | 105 rows: 73 restated (a paired MIR record named), 17 (a), 15 (d), no A5 miss |
| `x2-census.md` | X-2, the `Unsupported_Kd` census | 36 producers; 25 without a removal stage (DEC O4); 18 unpinned |
| `x4-measure.md` | X-4, the exit measurements | MIR build and A1-A6 against the old analyses: 2.0 times cold, 1.5 warm over 44 milestones; about 1 on the generic series; warm 0 B |
| `x5-review.md` | X-5, a sampled final review | ACCEPT; conditions C1-C5 and non-blocking N1-N11 (PLAN G396) |

X-3 is H0c's (R5-M182). X-6 is the commit that adds this folder.
