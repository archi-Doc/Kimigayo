# G350 design: a cold scaling series measures the process's one-time initialization

Base `e2c98b204` (dev), Debug, .NET 10.0.14. Design only; implementation waits for the planning session's GO. Probes (uncommitted):
each series' text at sizes 16, 16, 32, 64 in a fresh process (the second 16 a fresh `Compilation` of the same text), each `MeasureOnce`
window with the thread's `GCAllocationTick` types and JIT events; suspect class constructors run alone under `MeasureOnce`.

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
which xUnit v3 derives from the assembly path (G347's runs saw them fail). That selection's four other failures are warm `Measure`
windows (`MirModelTest.PooledRebuildAllocatesNothing` 864 B, `LexicalLayoutRecoveryTest.OrdinaryIndentationAndUnicodeReuseTokenStorage`
1,728, `TyCtxtTest.AWarmStoredRawLayerReadAllocatesNothing` 3,456, `GenericAdaptationTest.RepeatedCaseBindingOwnershipAndGenerationAllocateNothing`
17,120): not this cause, not investigated (proposed open issue).

## Cause (measured, by owner)

The first `BuildMir` in the process allocates 8,205,296-8,206,376 B more than a second cold build of the same text on a fresh
`Compilation` (Defaults 16: 11,475,640 vs 3,269,608). It is not per compilation and not a leak: a later fresh build's figure repeats
in every process (Defaults 16: 3,269,608 in four processes, after one or two earlier builds), and a trivial program's `BuildMir` as the
process's first build leaves 824 B of it (that series' own first generic instantiations).

- **Compiler, Backend: 8,197,832 B.** `BuildMir` → `Compilation.Collect` → `InstanceCollector` → `IntrinsicRows.Of`, whose `Rows` hold
  `LegacyRuntimeHelpers` ABIs (`FormattingRow`, `FormatHint`, `FormatFinish`). That runs `LegacyRuntimeHelpers`' class constructor,
  i.e. every static field of the partial class, among them `FormattingRuntime` (`ReadFormattingRuntime`: four `Utf8*.ll.in` resources,
  about 135 K characters, through `LlvmModuleText.ExpandRuntime`, whose `AbortReasons.Expand` and per-definition `string.Replace` copy the
  whole text each time) and `StringComparisons`. Alone: `LegacyRuntimeHelpers` 8,192,768, `RuntimeAbi` 3,768, `IntrinsicRows` 840,
  `AbortReasons` 456 (`LlvmModuleText`'s own 621,344 is not reached). The first window's allocation ticks are 7.77 MB of `System.String`;
  later windows have none.
- **Compiler, other class constructors** first run by the build (`Layout`, `MirBody`, two lambda caches): 4,736 B (with Backend's run
  first the excess is 7,280 B; with every Kimi class constructor run first, 2,544 B).
- **Runtime: 2,544 B.** Statics of the first instantiations over compiler types that the first build JIT-compiles (2,598 methods in the
  window, 0-2 in later ones): `Array.EmptyArray<T>`, `List<T>`, `EqualityComparer<T>` (88 B each; G347's comparer class).
- **Test helpers: none.** `AllocationMeasurement`'s class constructor runs before `MeasureOnce` reads the counter.
- Residual noise: size 64 differs by up to 2.5 KB between processes with no JIT in the window, inside the allocation-context phantom
  (up to ~8 KB) that a mid-window runtime pause can charge (`MeasureOnce` retires the context only at its start). Margins after the
  fix are at least 46 KB.

**Compiler finding (outside G350; proposed open issue, Backend's owner):** every pipeline's first `BuildMir` (so `kimi check` and the
LSP) builds code generation's LLVM runtime text: 8.19 MB allocated once per process, mostly transient whole-text copies (about 30
passes over ~270 KB), for a collector that reads only `IntrinsicRows`' targets and flags. The expanded texts belong to code generation;
a fix makes their initialization independent of the rows' ABIs (or the expansion one pass). G350 does not hide it: PLAN records it.

## Required meaning

V11 / V16 (plan 5.6.2, 5.6.4; DECISIONS R4-M7a): the cold build's allocation of a fresh `Compilation` grows linearly with N. Equivalent
workloads under fixed conditions: every size's cold run starts from fresh measured state in the same process state. One-time process
initialization (class constructors, the runtime's first instantiations) depends on neither N nor the compilation, and whichever run is
first in the process pays it: size 16 alone, an earlier test in a class. In one process the only condition all sizes can share is that
none is the process's first run of the operation. `MeasureOnce`'s "cold first run" is cold for the measured state, not for the process.

This is not the warm-up AGENTS forbids: no measured state is warmed (each is made after the extra run and its whole first run is
measured), the extra run is never a measured run's own state, and what it removes is measured and owned above, the Backend part
reported as a finding.

## Sites sharing the cause

`git grep -n "MeasureOnce\|ScaleLinearly\|ScalesLinearly"`, plus every size series with `allocated[]` (all use `MeasureOnce`):

| Site (owner) | Before each measured run | Shares the cause | Alone (dev) |
| --- | --- | --- | --- |
| `MirBuildTest.DropTreesScaleLinearly`, `BodiesScaleLinearly`, `.Defaults` `DefaultCopiesScaleLinearly` (R5) | fresh `Compile` | yes | fail |
| `MirAnalysisTest.RequireEndsScaleLinearly` (R4) | fresh `Compile` | yes | fails |
| `MirLoanSetsTest.UnionsScaleLinearlyInTheBytesCreated` (R4) | nothing; a fresh `MirLoanSets` in the window | yes, alone its first run is the process's first `MirLoanSets` run; effect not separated | passes (524,464 / 1,085,496 / 1,752,240) |
| `MirDropTest.DropFlagsScaleLinearly`, `MirInitTest.InitializationScalesLinearly` (3 rows), `MirEffectsTest.SummariesScaleLinearly` (2 rows) (R4) | an unmeasured `BuildMir`, whose driver runs A1, A2 and A4 | no: each size's own build fixes the process state | pass |
| `MirBorrowsTest.Stratum.MeasuredRun` (R4) | a designed second pass per view, not a series | no | - |
| `InstanceCollectorTest.AChainWithoutGrowthScalesLinearly`, `MirBuildTest.ADagComparisonsMirGrowsWithItsDistinctTypes` | work counts only | no | - |

## Options

- **(a) Inside each test:** one unmeasured `Compile(text(sizes[0])).BuildMir()` before the loop of the five sharing sites. Five copies of
  one condition (three texts must first leave their loops); the next cold series repeats the defect. Rejected.
- **(b) One entry of `AllocationMeasurement`:** `internal static long[] MeasureSeries(int count, Func<int, Action> fresh)`. Contract: the
  allocation of one cold run per index, each of the run `fresh(i)` returns, `fresh` making the index's fresh state outside the window;
  before the series, one run of a second `fresh(0)`, unmeasured, so the process's one-time initialization of the operation falls in no
  measured run and every index is measured in the same process state, alone or after other tests. Each run is a `MeasureOnce`; no
  retry, no excused cause, no bound. Every cold scaling series (the eight sites above) goes through it; `MeasureOnce` stays for one
  cold run or a designed second pass (`MeasuredRun`), and its comment gains the "cold for the measured state" sentence.
- **(c) Compiler:** stop `BuildMir` building the runtime text (the finding). It removes 8.19 of the 8.2 MB, but 7.3 KB still lands in
  size 16 alone, so the workloads stay unequal; the tests would pass by margin until the next static a first build reaches. A
  separate unit, not G350's fix.
- **(d) Rejected otherwise:** an assembly fixture that builds once at process start (a hidden global warm-up, not local); measuring 16
  again after 64 (the same extra run, less clear); running every compiler class constructor first (misses the runtime's 2,544 B).

## Recommendation and class

(b). **Major**: a shared test-tooling entry and sites in six test files of two owners; not Minor (eight sites share the form, five the
cause); not Full (no compiler representation or phase). R4 releases its hold for exactly those windows and reviews its files, as in
G347. Design review: the planning session decides whether the "not a warm-up" reading above needs the owner (AGENTS forbids added
warm-up; (b) adds an unmeasured run of a twin state). (c) becomes its own PLAN row (Backend). Deletions: each site's own measuring
loop; nothing in the compiler.

## Tests

- The four failing tests, alone, fail before (figures above) and pass after; probe of exactly (b)'s order, alone: Defaults
  3,269,608 / 3,475,304 / 3,914,928 (d2/d1 2.14), RequireEnds 3,148,504 / 3,243,056 / 3,480,232 (2.51), DropTrees 3,241,416 /
  3,486,512 / 4,050,968 (2.30), Bodies 4,762,368 / 6,748,656 / 11,716,040 (2.50) (the probe's window is `BuildMir()` alone; the
  tests' also hold `Bodies.ToArray()`, about 1.8 KB). In their classes they pass before and after; after, alone and in class print
  the same figures (within the phantom).
- The other four sites: DropFlags, Initialization and Summaries print the same figures before and after, alone and in class; Unions'
  first figure may drop by its one-time cost, recorded in the commit.
- `AllocationMeasurementTest.MeasureSeriesExcludesOneTimeInitialization`: an operation that first touches a private holder whose class
  constructor allocates 4 KB, then allocates 64 B per unit of its index's size: `MeasureSeries` reports 64 x size for every index,
  index 0 included, and calls `fresh(0)` twice. Without the extra run it fails (index 0 carries the 4 KB).
- Checks: each changed test alone and in its class, the `Purpose=Allocation` selection, then `verify.ps1` per VERIFICATION.

## Expected size

Tests only, no compiler line: `AllocationMeasurement` +14 (entry and contract), `AllocationMeasurementTest` +15, eight sites +2 to +4
each (the loop splits into making the state and reading its facts after the series): about +55 lines. Time: one extra smallest-size
run per series (eight runs, each under 0.3 s alone).
