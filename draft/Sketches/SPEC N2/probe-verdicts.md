# N2 probes: verdict table

Base: dev `8e928c630` (G160-4 4a–4c and G160-5 merged, BU-34, BU-35), built in `bin\worktrees\spec-n2` (Debug).
Sources: `src\prelude.kimi` + `src\g*.kimi`. Old path: the CLI (`cli\<group>\out-{legacy,mir}.txt`); under `KIMI_PIPELINE=mir`
the CLI still publishes the old path's verdicts (A5 is shadow, `Compilation.Pipeline.cs`: MIR analysis problems stay unpublished),
and both pipelines printed the same records for every group. A5 and Binding's rows: the throwaway harness
`ScratchSpecN2ProbeTest.cs.txt` (uncommitted; `a5\g*.txt`, library census `a5\census-untied-stores.txt`).
Reproduce: CLI `dotnet <worktree>\src\Kimi\bin\Debug\net10.0\Kimi.dll check cli\<group>\P.kimiproj` (with `KIMI_PIPELINE=legacy|mir`);
harness: copy the `.cs.txt` to `tests\xUnitTest\Tests\ScratchSpecN2ProbeTest.cs` in a worktree, build the test project, and run
`xUnitTest.exe -method XunitTest.ScratchSpecN2ProbeTest.Dump` (or `.Census`) with `SPECN2_IN=<this folder>\src` and
`SPECN2_OUT=<a folder>`. The copy was deleted from `bin\worktrees\spec-n2` after the run, and the test project was rebuilt.

Witness in every Bad program: `w: ref/i32 = y@ref` carries y's Loan; `q = w@raw@raw/u8` addresses w through an Origin-free pointee
(Bad: `y = 5`, then a use of the value the call wrote; Ok: the use before `y = 5`). "Required" here means the verdict under option A2 of
`options.md` (tracked). Corrected after review (B1): under today's letter carriage across a call is the contract's (§15.6), so these
acceptances conform to the letter (option C), and the unsafe-callee programs, Ok twins included, are undefined there because the
callee breaks §5.2.1 Retained dependencies. The reviewer's 109-program table is `..\rev\verdicts-rev.md`.

| Probe | Form (callee) | Safe callee | Binding rows today | A5 Bad / Ok | Old path Bad / Ok | Required Bad |
| --- | --- | --- | --- | --- | --- | --- |
| wAddr, wConv, wLoanRef, wLoanUniq, wTuple | no call: address, conversion, Loans through `r@follow@raw`/`u@follow@raw`, a Tuple local | – | – | err / ok | wAddr err; others acc | err |
| c0 | `f0(p: raw/u8, slot: raw/(raw/u8))` raw argument at the top | no | `0 Value store 1, untied store 1` | err / ok | acc / ok | err |
| putRaw, arrInsertRaw | `put<raw/u8>`, `Array<raw/u8>.insert` | yes | `0 Value store 1, untied store 1` | err | acc | err |
| c1 | `t: (raw/(ref/i32 during a), i32)` into `raw/(raw/(ref/i32 during a))` | no | `0 Value store 1`, `1 Content store 1`, … | err / ok | acc / ok | err |
| cw | `fW(t: (raw/(raw/u8), i32), v: raw/u8)` stores through t.0 | no | `1 Value store 0, untied store 0` | err / ok | acc / ok | err |
| getT, idTup | result `raw/u8` / `(raw/u8, i32)` from a Tuple argument (D6) | yes | `0 Value result, untied` | err / ok | acc | err |
| getRF, getRR, formR | result from `ref/(raw/u8)`, `raw/(raw/u8)`, a pointer formed from `r: ref/i32` (D6) | getRR no | `0 Value result[, 0 Content result], untied` | err | acc | err |
| **t** | `fT(t: (raw/u8, i32), slot: raw/(raw/u8))`, `*slot = t.0` | no | `untied store 1` | **acc** / ok | acc / ok | err |
| **o** | `fO(o: Option<raw/u8>, slot)` | no | `untied store 1` | **acc** / ok | acc / ok | err |
| **n** | `fN(b: Box, slot)`, Box's Field `p: raw/u8` | no | `untied store 1` | **acc** / ok | acc / ok | err |
| **nr** | `fNR(b: ref/Box, slot)` | no | `untied store 1` | **acc** / ok | acc / ok | err |
| **rr** | `fRR(pp: raw/(raw/u8), slot)`, `*slot = *pp` | no | `untied store 0, untied store 1` | **acc** / ok | acc / ok | err |
| **rf** | `fRF(r: ref/(raw/u8), slot)`, `*slot = r@follow` | no | `untied store 1` | **acc** / ok | acc / ok | err |
| **ph** | `fPH(h: ref/PH<raw/u8>, slot)`, a pair-supplied layer | no | `untied store 1` | **acc** / ok | acc / ok | err |
| **gRF** | `gRF(r: ref/(raw/u8), slot: uniq/(raw/u8)) => slot@follow = r@follow` | **yes** | `untied store 1` | **acc** / ok | acc / ok | err |
| **gT** | `gT(t: (raw/u8, i32), slot: uniq/(raw/u8))` | **yes** | `untied store 1` | **acc** / ok | acc / ok | err |
| **put** | `put<T>(v: T, slot: uniq/T)` at `T = (raw/u8, i32)` | **yes** | `untied store 1` | **acc** / ok | acc / ok | err |
| **boxPut** | `put<Box>` | **yes** | `untied store 1` | **acc** | acc | err |
| **repl** | `Kimi.Intrinsics.replace(s@uniq, with: (q, 1))` | **yes** (library) | `untied store 0` | **acc** / ok | acc / ok | err |
| **libInit** | `Raw.initialize<(raw/u8, i32)>(p, (q, 1))` | library | `untied store 0` | **acc** / ok | acc / ok | err |
| **arrInsert** | `Array<(raw/u8, i32)>.insert(0, (q, 1))` | **yes** (library) | (not dumped) | **acc** / ok | acc / ok | err |
| **fillR** | `*slot = r@follow@raw` from `r: ref/i32` (a formed pointer, Origin dropped by the fit) | no | `untied store 1` | **acc** / ok | acc / ok | err |
| **fillRB** | the same converted to `raw/u8` | no | `untied store 1` | **acc** | acc | err |
| **fillRS** | `fillRS(r: ref/i32, slot: uniq/(raw/u8)) => slot@follow = r@follow@raw@raw/u8` | **yes** | `untied store 1` | **acc** | acc | err |
| **wrapRR** | `wrapRR(pp: raw/(raw/u8)) -> Box`, `Box.init(p: *pp)` | no | `untied, untied store 0` | **acc** / ok | acc / ok | err (A2) / hidden (A1) |
| **wrapRF** | `wrapRF(r: ref/(raw/u8)) -> Box => Box.init(p: r@follow)` | **yes** | `untied` | **acc** | acc | err (A2) / hidden (A1) |
| **idBox** | `id<T>(x: T) -> T` at `T = Box` | **yes** | `untied` | **acc** / ok | acc / ok | err (A2) / hidden (A1) |
| gRFRepl | gRF, then `h` replaced, then `y = 5` | yes | `untied store 1` | acc | acc | err |
| getRFRepl | getRF, then `h` replaced: the retained `h@ref` Loan forbids the replacement | yes | `0 Value result, untied` | err (both) | acc | err |
| leak / leakDirect | `options.md` §4 C: `id<Box>`'s hidden `b.p` (or `q` directly) read back by `unsafe func peek<T>(p: raw/u8) -> T` after `y = 5` | id yes | `id<Box>: untied`, `peek: untied` | leak acc / leakDirect err | acc / acc | err (A2); under A1 or C defined unless condition 5 reaches calls (Q3) |

Every Ok twin is valid on A5 and on the old path (after `c1Ok`'s use was changed from `usep(s)` to `s == null`: `usep<ref/i32>`'s
own contract, `0 Content store 0`, writes through the untied `s` into storage the body does not own, a conservative row of `usep`,
not of the probe). No program has a Binding record.

Library census (declared contracts, 302 functions): untied stores in `Utf8Writer.write<T>` (`0 Content store 0, untied store 0`), seven
`format(self: ref/Self, writer: uniq/Utf8Writer)` (`1 Content store 1, untied store 1`; Utf8Writer's `dispatch` and `pending` are
`raw/u8` without an Origin) and nine `DictionaryStorage` functions over `handle: raw/Handle` (`Handle.buffer: raw/u8`; their other
inputs are `isize`, and `compact`'s `memory` already has `1 Value store 0`). Untied results: `Text.writer` (`0 Value result`, its one
input), `Array.initCapacity`/`init(capacity:)`, `keyAt`, `valueAt`, `tryAllocateBytes`, `Raw.allocate` (only `isize` or one raw input).
