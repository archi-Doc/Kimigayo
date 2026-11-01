# G350 design: the check pipeline builds code generation's runtime text

Base `e2c98b204` (dev), Debug, .NET 10.0.14. Design only; implementation waits for the planning session's GO. Probes (uncommitted):
each series' text at sizes 16, 16, 32, 64 in a fresh process (the second 16 a fresh `Compilation` of the same text), each `MeasureOnce`
window with the thread's `GCAllocationTick` types and JIT events; suspect class constructors run alone under `MeasureOnce`.

| Revision | Change |
| --- | --- |
| 0 (aecf6bc91) | Measurements and cause; recommended (b), an `AllocationMeasurement` cold-series entry. |
| 1 | Planning ruling: (b) rejected as warm-up; (c), the cause, chosen. Prototype measured; RequireEnds still fails alone. |

## Failing condition

`allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0])` (bytes, cold `BuildMir`, sizes 16/32/64):

| Test (owner) | Alone | In its class |
| --- | --- | --- |
| `MirBuildTest.DefaultCopiesScaleLinearly` (R5, W1) | 11,477,496 / 3,477,096 / 3,915,688: fails | 3,271,464 / 3,477,096 / 3,915,688: passes |
| `MirAnalysisTest.RequireEndsScaleLinearly` (R4) | 11,353,800 / 3,243,056 / 3,474,984: fails | passes |
| `MirBuildTest.DropTreesScaleLinearly` (R5) | 11,448,544 / 3,488,280 / 4,046,032: fails | 3,246,264 / 3,488,280 / 4,046,032: passes |
| `MirBuildTest.BodiesScaleLinearly` (R5) | 12,970,576 / 6,750,424 / 11,717,192: fails | 4,764,312 / 6,750,424 / 11,717,192: passes |

Two more than G350 names fail alone. The MIR-size assertions pass in every run; only the first size's allocation moves. In this
worktree's `Purpose=Allocation` selection (1,191 tests) all four pass, an earlier test having paid: the outcome depends on the order,
which xUnit v3 derives from the assembly path (G347's runs saw them fail). Its four other failures are warm `Measure` windows, not
this cause (a PLAN open row).

## Cause (measured, by owner)

The first `BuildMir` in the process allocates 8,205,296-8,206,376 B more than a second cold build of the same text on a fresh
`Compilation` (Defaults 16: 11,475,640 vs 3,269,608). It is not per compilation and not a leak: a later fresh build's figure repeats
in every process (Defaults 16: 3,269,608 in four processes, after one or two earlier builds), and a trivial program's `BuildMir` as the
process's first build leaves 824 B of it (that series' own first generic instantiations).

- **Compiler, Backend: 8,197,832 B.** `BuildMir` → `Compilation.Collect` → `InstanceCollector` → `IntrinsicRows.Of`, whose `Rows` hold
  `LegacyRuntimeHelpers` ABIs (`FormattingRow`, `FormatHint`, `FormatFinish`). That runs `LegacyRuntimeHelpers`' class constructor,
  i.e. every static field of the partial class, among them `FormattingRuntime` (`ReadFormattingRuntime`: four `Utf8*.ll.in` resources,
  about 135 K characters, through `LlvmModuleText.ExpandRuntime`, whose `AbortReasons.Expand` and per-definition `string.Replace` copy the
  whole text each time) and `StringComparisons`. Alone: `LegacyRuntimeHelpers` 8,192,768 (about 14 KB of it rows, revision 1),
  `RuntimeAbi` 3,768, `IntrinsicRows` 840, `AbortReasons` 456 (`LlvmModuleText`'s own 621,344 is not reached). The first window's
  allocation ticks are 7.77 MB of `System.String`; later windows have none.
- **Compiler, other class constructors** first run by the build (`Layout`, `MirBody`, two lambda caches): 4,736 B (with Backend's run
  first the excess is 7,280 B; with every Kimi class constructor run first, 2,544 B).
- **Runtime: 2,544 B.** Statics of the first instantiations over compiler types that the first build JIT-compiles (2,598 methods in the
  window, 0-2 in later ones): `Array.EmptyArray<T>`, `List<T>`, `EqualityComparer<T>` (88 B each; G347's comparer class).
- **Test helpers: none.** `AllocationMeasurement`'s class constructor runs before `MeasureOnce` reads the counter.
- Residual noise: size 64 differs by up to 2.5 KB between processes with no JIT in the window, inside the allocation-context phantom
  (up to ~8 KB) that a mid-window runtime pause can charge (`MeasureOnce` retires the context only at its start).

The Backend part is the cause G350 fixes (revision 1): the check pipeline must not build code generation's runtime text.

## Required meaning (revision 1)

V11 / V16 (plan 5.6.2, 5.6.4; DECISIONS R4-M7a): the cold build's allocation grows linearly with N. A cold scaling test that fails
alone flags a one-time cost on the `BuildMir`/check path; that sensitivity is intended, so the fix removes the cost from the path, not
from the measurement. `MeasureOnce`'s contract and every test stay as they are.

## Options

- (a) an unmeasured build before each series, (b) the same as one `AllocationMeasurement` entry: **rejected** (planning ruling): an
  unmeasured run added so that the series passes is the warm-up AGENTS forbids, whatever state it touches, and it removes the
  sensitivity that found this cost.
- **(c) chosen:** the expanded texts are initialized only when code generation writes them; the rows' ABIs stay where the collector
  reads them. One owner, `LegacyRuntimeHelpers` (src/Kimi/Compiler/Backend), not R4's hold.
- Rejected inside (c): `Lazy<T>`, a flag or a mode (the CLR's class initialization of a holder does it); a holder per text (no measured
  need: every text is built by the same first code generation in practice, and one holder keeps one rule).

## Design

- `LegacyRuntimeHelpers.Texts`, a `private static partial class` at the end of each file that owns a text, with one comment stating the
  contract (code generation's expanded runtime texts; initialized when a writer first reads one; never by the check pipeline):
  `StringComparisons` (from the field in LegacyRuntimeHelpers.cs), `Formatting` (was `FormattingRuntime`, .Formatting.cs) and `Array`
  (was `ArrayRuntime`, .Arrays.cs). These are all of `LegacyRuntimeHelpers`' expanded-text statics; its other statics are rows and ABIs.
- Readers keep their signatures: `WriteArrayRuntime`, `WriteFormattingRuntime`, and `StringComparisons`, a field that becomes a get-only
  property over `Texts.StringComparisons` (its readers, `ModuleCodegen.Frame` and `FunctionCodegenTest.Strings`, unchanged).
- Stay: `FormattingRows`, `ArrayRows`, `DictionaryRows`, `StorageKernelRows`, `FormatBounds` and the ABIs (14,008 B), which
  `IntrinsicRows` and the collector read.
- No check-time reader remains: `Texts` is private, its three readers are called only by `ModuleCodegen.Frame` (and the test); the
  first `BuildMir` no longer runs its constructor (below). Its class is beforefieldinit; an eager initialization at JIT time happens only
  when a method reading it is compiled, i.e. a writer. `LlvmModuleText`'s texts (621,344 B) are not reached by check (no class
  constructor in the first build's JIT events) and stay.

## Measurements (prototype of exactly this design, uncommitted; Debug)

| | Before | After |
| --- | --- | --- |
| `LegacyRuntimeHelpers` class constructor, after a `Compile` | 8,197,792 B, 23-97 ms | 14,008 B; `Texts` 8,183,784 B, 12-45 ms, at first code generation |
| First / second `BuildMir` (trivial program, fresh `Compilation`s) | 11,278,440 / 3,073,232 B | 3,094,688 / 3,073,232 B |
| First `kimi check` of a small program, six interleaved fresh processes from one folder | 1,316-1,380 ms | 1,310-1,380 ms (about 40 ms, inside the spread) |
| `kimi emit` of it (Array, string `==`, interpolation of an integer and a float) | 169,601 B | byte-identical |

The four tests alone, after (a0 / a1 / a2, d2/d1, margin to `3 * d1`): Defaults 3,293,744 / 3,477,096 / 3,915,688 pass (2.39, 111 KB);
DropTrees 3,264,792 / 3,488,280 / 4,046,032 pass (2.50, 113 KB); Bodies 4,786,824 / 6,750,424 / 11,717,192 pass (2.53, 924 KB);
**RequireEnds 3,170,048 / 3,243,056 / 3,474,984 still fails** (3.18; it passes only with a0 at most 3,165,747).

## Residual and RequireEnds (open: the planning session decides)

After (c) the first build still pays 21,456-22,300 B (RequireEnds 21,544 = 3,170,048 - 3,148,504): `LegacyRuntimeHelpers`' rows
14,008, `RuntimeAbi` 3,768, `IntrinsicRows` 840, `AbortReasons` 456, the rest other first-run statics of the compiler and the
runtime. Revision 0's "7.3 KB" was wrong: it counted the whole class constructor as text. RequireEnds' increments are the smallest
(94,552 B from 16 to 32), so it tolerates at most 17,243 B of one-time cost and fails alone by 4.3 KB. These are check-time tables,
not a large cost. Not decided here: (i) land (c) and keep RequireEnds-alone an open item for R4 (its sizes and quantity are R4's); (ii)
also keep the 14 KB of Array, Dictionary and Storage rows from check (three formatting ABIs in `IntrinsicRows` initialize every legacy
row table): a restructuring for check-time tables, not recommended.

## ExpandRuntime's cost for code generation (figures only)

`Texts`' constructor allocates 8,183,784 B in 12-45 ms for about 135 K characters of formatting templates (four resources: 13,901,
22,557, 9,007, 89,833 bytes) and two small texts: about 30 whole-text copies (`AbortReasons.Expand` replaces per reason, then
`ExpandRuntime` per definition, each over the whole text). A one-pass expansion is a separate follow-up, not trivial (two splices with
their uniqueness checks).

## Tests

- The four `ScaleLinearly` tests alone: fail before (figures above); after, Defaults, DropTrees and Bodies pass, RequireEnds as above.
  In their classes they pass before and after. The commit states the residual and the margins.
- Generated IR: a fixture selection over formatting, string comparison and Arrays, `compare-fixtures.ps1` against the base run:
  byte-identical `.ll`.
- Regression: the four tests fail before only alone, so a full run would not catch a return of the cost. Proposed (needs the ruling's
  "no test file changes" relaxed for R5's own file): a `FreshProcess` pair in `MirBuildTest`, where after the process's first
  `BuildMir`, `MeasureOnce(() => LegacyRuntimeHelpers.WriteFormattingRuntime(TextWriter.Null))` is above 0 (the text is built only
  then); before the fix it is 0. It compiles before and after.

## Class and size

Major, one owner (Backend; R5 takes `LegacyRuntimeHelpers` for this unit). Prototype size: LegacyRuntimeHelpers.cs +48/-41 (the
StringComparisons text moves into `Texts` with its indentation), .Formatting.cs +7/-4, .Arrays.cs +14/-11: net +13 lines, compiler
only (+about 12 with the proposed regression). DECISIONS: none expected (no new representation or vocabulary); CODEMAP unaffected.
