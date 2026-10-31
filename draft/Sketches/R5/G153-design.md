# G153 (1) design: where the instance collector's Resource record sits

Design phase only (PLAN G153 part (1); base dev `f438a77a9`, after G332 / DECISIONS R5-M170). No source change. Part (2), the Mir
sites' order-dependent storage bound (BU-11, BU-12), is not this unit, and this design does not touch it: every change below stays in
`Backend/Instances`; `MirTypes.Holding`, `MirTypes.Destruction`, `MirEffects.Solve.Destroy` and Binding's storage walk are unchanged.

## 1. Required meaning

- SPEC 23.1.2: each independent failed requirement is reported; grouping failures into records (one failure under several
  instantiations included) is implementation-defined and never changes acceptance. IMPL 21.3.2: a limit is a Resource diagnostic "at the
  affected declaration, call or token" and does not stop independent checks. DIAGNOSTICS rule 6: positions order by source, never by a
  traversal; Principle 2: a declaration's diagnostics follow from its own code and published contracts.
- The cause of a Layout limit is the erased Type (Layout reads no Origin, its verdict is memoized per erased Type, R5-M37/R5-M44), so the
  per-erased-Type key is right for the decision. It is wrong as the record key: one record for the whole program sits at the Type's
  first use in the collector's FIFO order, so whether declaration D shows a record depends on other declarations and on admission order,
  not on D. That breaks Principle 2 and rule 6, not SPEC 23 (grouping is allowed) and not soundness (acceptance is unchanged).
- Required: the decision stays once per cause; the record is once per (cause, declaration that uses it), at the first use in that
  declaration. Uses in one declaration stay grouped (R5-M44's rejected "a record at every use" stays rejected: one Type held by a
  local, temporaries and an erasure in one statement is one record). Instantiations stay out of the key (DIAGNOSTICS rule 5).
- Declaration = the MIR body the subject lies in (`MirDefKey`): a function, method, accessor, anonymous function, test, static
  initializer or a module's top-level statements; an import (no body) is its own `MirDefKey(function, Fn)`. Under K 4.9 the subject of
  a root-module body is in that body, and that of any other item is its `Site`, which lies in the body of the nearest item on its
  admission chain whose body is in a root module; so the declaration of every subject is `Declaration(item)`: that walk, the item
  itself included.
- Same class (R5-M170's amendment names it): a key refused for growth, the identity count or the height cap is decided once and
  recorded at its first request only. Under the same rule its record is once per (key, declaration requesting it); the decision is
  unchanged (`refused`, read after `Find`).

## 2. Census (dev `f438a77a9`)

- Diagnostic snapshot (`artifacts/verify/shared-base/f438a77a9/diagnostic-snapshot.json`): 0 `GenerationResourceLimit_Kd` records;
  MIR ledger (`mir-diff/ledger.tsv`): 0 rows; the library as roots and the milestones: none (R5-M44). No snapshot change is expected.
- Tests asserting the record (one declaration each, or distinct causes): `InstanceCollectorTest` (`ALayoutLimitIsARecordAtItsTypesFirstUse`,
  `ALayoutOverTheBoundIsARecordAtEachUse` GRL rows, `TheDepthLimitCountsPlacedLevels`, `GrowthStopsAtItsSite`, `TheLocationRuleLeavesNonRootModules`
  (two keys in `main`), the set limit, LayoutNeeds), `InlineLayoutTest` (4), `TypeDagScalingTest` (2 theories, one body),
  `ComponentSummaryTest`, `GenericDestructorEmissionTest`, `GenericObjectFactoryTest`, `GenericStorageEmissionTest`,
  `RecursiveDefaultTest`, `VirtualGenericDispatchTest`, `MinimalEmissionTest.AssertGenerationResourceLimit`. Read against the rule, none
  changes; to be confirmed by the unit's run.
- CLI probes (uncommitted, `C:\Users\bwff1\AppData\Local\Temp\claude\r5\g153\probe`, `Kimi.dll check`):
  - v1 `func a(x: [2147483648 of u8])` and `func b(y: …)`: one record, at `a`'s parameter Type. v3 (fix `a`): the record appears at `b`
    (fixing one use reveals the next).
  - v5: `g1<T>`, `g2<T>` each `let o: Option<T> = .None`; `r1` calls `g2<[2147483648 of u8]>`, `r2` calls `g1<…>`: one record, in `g2`
    (5:5). v6: only `r1`'s call routed through `func w<U>() -> i32 => g2<…>()`, no use of the Type changed: the record moves to `g1`
    (2:5). The unrelated edit moves it to another function.
  - Parameters: PLAN's "a parameter's record lands on its function's declaration, since MIR records no parameter syntax" is stale.
    `c9f28ae45` (G158 (3), BU-48, 2026-10-27) sources each Arg local at its written Type (v1, v8: `init(v: …)` at its Type). Residual
    (v7): an anonymous function's parameter without a written Type, `func (x) => 1`, is located at the whole anonymous function
    (`MirBuilder.cs`: `written?.Parameters[slot].Type ?? declaration`). A destructor's `self`, an accessor's value and a Closure's
    environment have no own syntax; their declaration is right.
- Residual of options A2 and B (code, K 4.9): a library body item is admitted once and replayed once; its records sit at its
  `Site`, the first root-module request in FIFO order. A second declaration requesting it finds the item (`Set.Find`) and gets no record,
  for every record kind (Resource, Unsupported, capability). Example: library `pair<T>` holding `Option<(T, T)>`, called with
  `T = [1073741824 of u8]` from `A` and `B` (`T` itself fits): one record at `A`'s call; removing it moves the record to `B`.

## 3. Options

| | A2 (recommended): key (cause, declaration) in the collector | B: one record per Type at the structurally first use | C: K 4.9 places a library item's records at each requesting declaration |
| --- | --- | --- | --- |
| Rule | Layout limits and growth refusals recorded once per (cause, `Declaration(item)`) | keep one record; defer it to the run's end; subject = least SourceRef among uses | records whose subject is an item's `Site` are re-made, at run end, at each root-module request reaching the item through non-root items (reverse walk of targets) |
| Owner | collector (R5) | collector | collector; amends KD12 (R5-M40) for every record kind |
| Fixes | v3, v5/v6, imports, type-driven and LegacyRow keys (re-requested on failure) | v5/v6 within a run; not v3 | the library residual |
| Size | src about +30/−10 (below) | about +25; a deferral path beside `Report` | about +70 to +90 and a reverse index |
| Records | +1 per extra declaration using a failing Type (none in the census) | unchanged count | +1 per extra requesting declaration, every kind |
| Risks | a refused growth key now also recorded in a second declaration whose own chain did not grow (the decision's order dependence, R5-M170, stays) | "fix one reveals next" stays; records stop being made where the check fails | DIAGNOSTICS rule 3 ("Unsupported is one location") read against several records of one library form; cost of the walk |

A2 in detail (no new representation entry: collector-private state, existing `MirDefKey`):

- `InstanceCollector.Admit.cs` (K 4.9 owner): `Declaration(int item)`: the `MirDefKey` of the nearest body item on the admission chain,
  itself included, whose body lies in a root module; read only when a record is made, so the common path pays nothing. `Refuse(key,
  parent, site)` (the height cap and `Grows`) and `Refused(key, parent, site)` (after `Find` in `Admit`, `AdmitType`, `AdmitRow`) replace
  the three copies of `refused.Contains` and the two refusal blocks; a refused key records when `(key, Declaration(parent))` is new.
- `InstanceCollector.Layouts.cs`: `LaysOut(int item, SourceRef subject, BoundType type)`; its limit record keys `limited` by
  `(Erase(type), Declaration(item))`; `LayOutImports` keys by the import's own `MirDefKey`, through the same private limit step (no
  optional or steering parameter). Callers gain the item they already hold: `Edges.cs` (3), `Glue.ElementsLayOut` (from `AdmitType`'s
  parent), `Witnesses.Uses` (from `Compared`). `Callees.cs` is unchanged (`LaysOutArguments` already takes the item), so this unit is
  not "the next unit changing `InstanceCollector.Callees.cs`" and G289 (4) does not ride.
- Fields: `limited` becomes `HashSet<(BoundType, MirDefKey)>`; a new `refusals` `HashSet<(MonoKey, MirDefKey)>` beside the decision
  `refused`. Warm runs allocate nothing more (cleared per run); the empty set adds a construction allocation to measure.
- Contract text: the collector header's Layout and admission sentences, `LaysOut`'s and `Grows`' comments.

Rejected: per use (R5-M44's noise); the outermost named declaration as the grain (`TyCtxt.EnclosingDeclaration` walk: an anonymous
function's uses are fixed in it, and a body is MIR's unit); a per-item declaration field (memory on every item for a value read only on
failure); A without growth refusals (two "once" rules of one meaning at different grains: a type-driven key refused by Layout would
record per declaration, one refused by height once).

Parameter residual: the builder sources an untyped parameter at its parameter node (`written.Parameters[slot]` when its `Type` is
null): one line in `MirBuilder.cs`, an R4 hold file, `MirLocal.Source` unchanged (no representation entry).

## 4. Classification and sizing

- U1, Major (collector only; state and a parameter added; amends R5-M44's grouping and R5-M170's "refused without a record"; no
  vocabulary, no shared representation, no owner approval needed). One open design issue for the planning session's ruling or a short
  review: growth refusals in the rule (recommended yes). About 1 h: code, DECISIONS amendment, rows in `InstanceCollectorTest`: v1 (two
  records), v5 and v6 (records in `g1` and `g2` in both orders), one generic body under two instantiations (one record), a refused key
  requested from a second declaration (its own record), the library residual pinned as a known limit; Unit checks of the classes above.
- U2, Minor, R4's lane (holds `MirBuilder.cs`): the untyped parameter; one row (v7 at `x`). About 20 min.
- Not sized: C, a new PLAN item G153 (3) if the planning session wants it (KD12 change, every record kind; about 2 units).

Open: PLAN G153's parameter clause is stale (planning session's PLAN edit). No owner question.
