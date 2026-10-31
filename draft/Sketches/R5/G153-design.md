# G153 (1) design: where the instance collector's Resource record sits

Design phase only (PLAN G153 part (1); base dev `f438a77a9`, after G332 / DECISIONS R5-M170). No source change. Part (2), the Mir
sites' order-dependent storage bound (BU-11, BU-12), is not this unit, and this design does not touch it: every change below stays in
`Backend/Instances`; `MirTypes.Holding`, `MirTypes.Destruction`, `MirEffects.Solve.Destroy` and Binding's storage walk are unchanged.

| Revision | Change |
| --- | --- |
| 0 | First design (A2: records keyed (cause, declaration), growth refusals included). |
| 1 | Review 1 (REVISE). B1: the residual restated (every shared admitted item: library bodies and type-driven expansions of root-module Types), the reviewer's `t1`/`t2` pinned as a Known limit, option C widened (§2, §3). B2: "first use" defined as the least source position per (cause, declaration), kept in a table and recorded at the collection's end (§1, §3). Planning ruling: growth and identity-count refusals keep R5-M170's one record per key; the height cap moves to the Type rule (§1, §3). Non-blocking: `Declaration`'s cost and memo, where it reads a body's module, imports without a sentinel item, closure bodies, U2's readers and the mir ledger, units re-sized (§3, §4). |

## 1. Required meaning

- SPEC 23.1.2: each independent failed requirement is reported; grouping into records (one failure under several instantiations
  included) is implementation-defined and never changes acceptance. IMPL 21.3.2: a limit is a Resource diagnostic "at the affected
  declaration, call or token". SPEC 8 (Limits): a limit is reported once, at the declaration that reaches it, and whether it is reached
  never depends on the order of checks. DIAGNOSTICS rule 6: positions order by source table order, never by a traversal; Principle 2:
  a declaration's diagnostics follow from its own code and published contracts.
- Two kinds of cause, one rule each:
  - A Type's verdict (Layout's size, count or depth limit; the type-driven height cap, a property of the Type alone): decided once (Layout's
    memo; `BoundType.Height`), recorded once per (erased Type, declaration) at the **least source position** among that declaration's
    failing uses, whatever the traversal. Uses in one declaration stay grouped (R5-M44's rejected "a record at every use" stays rejected);
    instantiations stay out of the key (DIAGNOSTICS rule 5).
  - An admission decision (growth past 16 generations, a 1,025th item of an identity): its cause is the chain that decided it, not the
    key, so it keeps R5-M170's one record per key at its first request; a later request from another declaration is that refusal's
    dependent (SPEC 23.1.2), and recording it there would give a call that does not grow a growth record depending on another
    declaration.
- Least source position: `SourceRef` order (module, id). SyntaxTable numbers parsed nodes 1 to `ParsedCount` in pre-order and gives a
  node outside the tree (Binding's synthetic nodes) a later id in creation order; that is rule 6's "source table order", read without
  syntax. A declaration's subjects lie in one module.
- Declaration = the MIR body the subject lies in (`MirDefKey`): a function, method, accessor, anonymous function, test, static
  initializer or a module's top-level statements; an import (no body) is its own `MirDefKey(function, Fn)`. Under K 4.9 the subject of
  a root-module body lies in it, and that of any other item is its `Site`, which lies in the body of the nearest item on its admission
  chain whose body is in a root module: `Declaration(item)`, the item itself included.
- An anonymous function is its own body: one Type held by value in its environment and by its host can give two records on one line
  (the host's use and the anonymous function's environment local, located at the anonymous function); the same subject merges in
  `CheckedSet.Report`. Accepted: each body is checked as its own definition; folding it into the enclosing named declaration
  (`TyCtxt.EnclosingDeclaration` walk) was rejected (another walk, and it groups uses fixed in different bodies).

## 2. Census (dev `f438a77a9`)

- Diagnostic snapshot (`artifacts/verify/shared-base/f438a77a9/diagnostic-snapshot.json`): 0 `GenerationResourceLimit_Kd` records;
  MIR ledger (`mir-diff/ledger.tsv`): 0 rows; the library as roots and the milestones: none (R5-M44). No snapshot change is expected.
- Tests asserting the record, each one declaration or distinct causes: `InstanceCollectorTest` (`ALayoutLimitIsARecordAtItsTypesFirstUse`,
  `ALayoutOverTheBoundIsARecordAtEachUse` GRL rows, `TheDepthLimitCountsPlacedLevels`, `GrowthStopsAtItsSite` (its `height` row through
  the moved cap: same subject), `TheLocationRuleLeavesNonRootModules` (two growth keys in `main`), the set limit, LayoutNeeds),
  `InlineLayoutTest` (4), `TypeDagScalingTest` (2 theories, one body), `ComponentSummaryTest`, `GenericDestructorEmissionTest`,
  `GenericObjectFactoryTest`, `GenericStorageEmissionTest`, `RecursiveDefaultTest`, `VirtualGenericDispatchTest`,
  `MinimalEmissionTest.AssertGenerationResourceLimit`. Read against the rule none changes; the units' runs confirm it.
- CLI probes (uncommitted, `Kimi.dll check`; mine in `C:\Users\bwff1\AppData\Local\Temp\claude\r5\g153\probe`, the reviewer's `t1`,
  `t2`, `g1`, `g2` in the session scratchpad's `probe`):
  - v1 `func a(x: [2147483648 of u8])` and `func b(y: …)`: one record, at `a`'s parameter Type; v3 (fix `a`): it appears at `b`.
  - v5/v6: `g1<T>`, `g2<T>` each `let o: Option<T> = .None`, called with `[2147483648 of u8]` from `r1` and `r2`: the record is in `g2`;
    routing only `r1`'s call through a generic `w<U>` moves it to `g1`.
  - g1/g2 (inside one declaration): `g<T>` with `let a: Option<T>` and `let b: Option<[2147483648 of u8]>`, `r1` calls `g<i32>`, `r2`
    calls `g<…>`: the record is at `b` (3:5); routing `r1` through `w<U>` moves it to `a` (2:5). Admission order decides within a
    declaration too, so the least position is required.
  - Parameters: PLAN's "a parameter's record lands on its function's declaration, since MIR records no parameter syntax" is stale:
    `c9f28ae45` (G158 (3), BU-48) sources each Arg local at its written Type (v1; v8 `init(v: …)`). Residual (v7): an anonymous
    function's untyped parameter, `func (x) => 1`, sits at the whole anonymous function (`MirBuilder.cs`: `Parameters[slot].Type ??
    declaration`). A destructor's `self`, an accessor's value and a Closure's environment have no own syntax.
- Residual (B1, K 4.9; code and `t1`/`t2`): every shared admitted item keeps its records at its first admitter's `Site`. A later request,
  from another declaration or another site, finds the item (`Set.Find` in `Admit`, `AdmitType` at Admit.cs:128-131, `AdmitRow`), records
  a target and replays nothing, so it gets no record, of any kind. It covers library body items and type-driven expansions of
  root-module Types: `t1`, `struct S { public var a: Array<[2147483648 of u8]> }` with `d` and `e` each `let o: Option<S> = .None`, one
  record at `d` (4:5), found by `ElementsLayOut` under DropGlue(Array) under DropGlue(S) under DropGlue(Option<S>); `t2` (without `d`) at
  `e`. Not in the residual: a key whose own verdict fails is not admitted (a type-driven key's Layout or height, a LegacyRow's
  components, an erasure's signature), so each request re-reads it and records in its declaration.

## 3. Options

| | A' (recommended): Type causes per (erased Type, declaration), least position | B: one record per Type, least position | C (G153 (3)): a shared item's Type causes at each requesting declaration |
| --- | --- | --- | --- |
| Rule | §1's two rules; Type causes in a table made at the run's end | the table keyed by Type alone | A', plus: a table entry whose subject was an item's `Site` also enters the declarations of every root-module site whose targets reach that item through items located at their `Site` (a reverse walk of `MonoTarget`s, found items included) |
| Owner | collector (R5) | collector | collector; for other record kinds a KD12 (R5-M40) change |
| Fixes | v3, v5/v6, g1/g2, imports, keys re-read on failure | g1/g2-like order within a run; not v3 | the B1 residual (`t1`) for Type causes |
| Size | src about +35/−15 | about +25 | about +50 more and a reverse index built only when the table is non-empty |
| Records | +1 per extra declaration using a failing Type (none in the census) | unchanged count | +1 per extra declaration reaching a failing shared item |
| Risks | more records than today in multi-declaration programs (bounded by declarations × failing Types) | "fix one reveals the next" stays | for Unsupported and capability records, DIAGNOSTICS rule 3 ("one location") against several records of one library form |

A' in detail (collector-private state; existing `MirDefKey`; no vocabulary or representation entry):

- `InstanceCollector.Admit.cs` (K 4.9's owner): `Declaration(int item)`: the `MirDefKey` of the nearest body item on the admission chain,
  itself included, whose body lies in a root module, read as `program.Bodies[items[i].BodyOrdinal].Source.Module < rootModules` (Run
  keeps the program it collects for the run). Memoized per item for the run in an int array (the declaring item) allocated at the run's
  first failing use and reused; a walk fills every item it passes, so each item is walked once per run; steps count in `Work`. Imports
  use no item and no sentinel: `LayOutImports` passes the import's own key.
- The table `limits`: `Dictionary<(BoundType Type, MirDefKey Declaration), SourceRef>`, replacing `limited`; a failing use keeps the
  lesser of its subject and the entry's. One private step `Limit(BoundType erased, MirDefKey declaration, SourceRef subject)` serves
  `LaysOut(int item, SourceRef subject, BoundType type)` (the declaration read only on a limit verdict) and `LayOutImports` (Layout's
  verdict and the Language records stay one shared step). At the end of `Run` each entry is recorded through `Report`, the one record
  path, in subject order (rule 6); `CheckedSet.Records` is read only after `Run` (`Compilation.MirRecords`, code generation at emit), so
  no consumer needs the record during collection. Warm and failure-free runs allocate nothing more (the table and scratch are cleared,
  not freed).
- Height cap: `AdmitType`'s `Height > 128` goes through `Limit` with the key's erased Type and `Declaration(parent)`, without a `refused`
  entry (a later request re-reads it in O(1)). `refused` keeps growth and identity-count refusals, recorded once per key (R5-M170).
- Callers gain the item they hold: `Edges.cs` (3), `Glue.ElementsLayOut` (`AdmitType`'s parent), `Witnesses.Uses` (from `Compared`),
  `AdmitRow`. `Callees.cs` is unchanged (`LaysOutArguments` already takes the item), so this is not "the next unit changing
  `InstanceCollector.Callees.cs`" and G289 (4) does not ride.
- Contract text: the collector header's Layout, admission and Records sentences; `LaysOut`'s and `AdmitType`'s comments.

Rejected: per use (R5-M44's noise); growth refusals per declaration (planning ruling above); admission order as "first use" (g1/g2);
a per-item declaration field (memory on every item for a value read only on failure); the outermost named declaration (§1).

Parameter residual (U2): the builder sources an untyped parameter at its parameter node (`Parameters[slot]` when its `Type` is null),
one line in `MirBuilder.cs` (an R4 hold file); `MirLocal.Source` is unchanged, but the Arg local's source is also read by MirBorrows
(anchors, reports), MirInit, Drops and MirDump, so U2 runs the mir ledger.

## 4. Classification and sizing

- U1a, Major (collector; state and a parameter added; amends R5-M44's grouping): `Declaration` with its memo, the `limits` table,
  the run-end recording, the callers, imports; rows in `InstanceCollectorTest`: v1 (two records), v5 and v6 (records in `g1` and `g2`
  either way), g1 and g2 (one record at `a`, 2:5, either way), one generic body under two instantiations (one record), an import with
  two slots of one failing Type (one record); Unit checks of the census classes. About 1 h.
- U1b, Major (after U1a; amends R5-M170 for the height cap only): the cap through `Limit`; rows: a Type above 128 requested from two
  declarations (two records), a growth-refused key requested from a second declaration (still one record), `t1`/`t2` pinned as the B1
  Known limit. About 40 min.
- U2, Minor, R4's lane (holds `MirBuilder.cs`): the untyped parameter; one row (v7 at `x`); the mir ledger. About 30 min.
- C (G153 (3)), not sized here: a new PLAN item if the planning session wants it (Type causes about 2 units; other record kinds
  are a KD12 decision).

Open: PLAN G153's parameter clause is stale (planning session's PLAN edit). No owner question.
