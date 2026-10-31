# G332 design: the instance collector's depth rule

Status: design phase of PLAN G332 (R5, 2026-10-31), revision 0. Working record, not authority: what it decides becomes authority only
as a DECISIONS entry in the commit that implements it. Base: `origin/dev` `def70d15d`. No source change committed; one throwaway
prototype (uncommitted, reverted) and CLI probes under `C:\Users\bwff1\AppData\Local\Temp\claude\r5\g332\probes\`.

## 1. Required meaning

| Source | Rule |
| --- | --- |
| SPEC 8.15.3 | Polymorphic recursion with a growing Type argument is valid; one copy per instantiation "may reach an implementation-defined limit", a resource failure. |
| SPEC 6.1 (finite inline storage) | "Only growth of distinct instantiations through an indirection (for example polymorphic recursion) can reach a generation limit." |
| IMPL 21.3.2 | Analysis and generation limits (instantiation growth, context depth, default-expansion count, analysis storage, string-interpolation nesting) are implementation-defined; exceeding one is a Resource diagnostic at the affected declaration, call or token, never invalidity, never stopping independent checks. |
| IMPL A.4, SPEC 23.1.1 | Deep or large input never exhausts the host stack; a limit is `Resource`, never Language or acceptance. |

So the collector's limit is "instantiation growth": it must bound growth of distinct instantiations (a recurrence), and every valid
program without one must be accepted. A program's own nesting (a comparison's composite parts, a chain of distinct definitions, a ring
of distinct structs) is bounded by its input; counting it is not a growth limit (and V16 needs no limit for work linear in input and
output). No SPEC/IMPL gap in the rules (the numbers are implementation-defined). Reported, not edited: IMPL A.4 names this
implementation's 64 Semantics cases but not its instantiation limits (DIAGNOSTICS:361 states them).

## 2. Cause chain

- K package 4.6, KD9 (owner-approved with S2-S4, R5-M18; inherited from the old generator): "depth 128 on every item; growing key 16
  and set 1,024 on Body items only". Code: `InstanceCollector.Admit.cs:11` `DepthLimit`; checks at `:79-85` (Admit), `:122-127`
  (AdmitType), `:178-183` (AdmitRow); `Grows` `:222-236` walks the whole chain (at most 128 items because of the depth).
- Every hop counts one level: a call of a distinct definition, a comparison's composite part (R5-M166), a DropGlue part, a glue's
  destructor, a descriptor's VTable. The row's probes and two more valid classes meet 128 with no recurrence at all (§3).
- What actually terminates collection: an infinite set needs an infinite admission chain of distinct keys (finitely many targets per
  item); with finitely many definitions and declarations, one of them repeats on the chain with other keys without bound. Grows (16)
  catches that for Body items; SetLimit bounds breadth. The depth adds no termination for bodies.
- But the depth is today the ONLY bound on a type-driven recurrence: DropGlue's parts are a declaration's Fields under its
  substitution, read without a storage verdict (a type-driven item has no representative, so `In` is Prepared), and an owning handle
  is indirect for Layout. A non-regular Type behind a handle with a Sealed payload (`Chain<T>` holding `Option<obj/Chain<(T, T)>>`,
  `ComponentSummaryTest.ANewlyOwnedNonRegularValueMeetsTheGenerationLimits`) expands DropGlue items forever. Evidence: the prototype
  that deleted the depth (bodies-only recurrence) overflowed the host stack in `Binding.OperandContexts` (via `DropGlue.Parts` ->
  `TyCtxt.Sealed` -> `ProveSealed`, ~6,000 frames). That test's `GenerationResourceLimit_Kd c@move` is therefore the collector's depth,
  not R5-M44's layout depth its comment names. (p4/p5 below, `rc/` to an open payload, whose glue has no target (K8), stop at
  Binding's storage bound at the creating call instead; inferred, not traced.)
- Order: `Depth` is the hop-BFS distance from the roots (FIFO, roots first), so the depth itself is order-independent. Grows reads the
  first admitter's chain, chosen among equal-depth parents by site order, and refused keys are not remembered, so a key refused under
  one parent can be admitted under another; SetLimit's 1,025th victim is order-dependent (deterministic; placement only, as G153).
  A weighted depth under the same FIFO would make the depth itself order-dependent.

## 3. Census

| Item kind (admission) | Targets | Recurs by itself? |
| --- | --- | --- |
| Body (`Admit`: callee, witness, destructor, Closure, StaticInit, slot) | its sites | Yes: a definition cycle with a growing substitution |
| LegacyRow (`AdmitRow`) | `append` body, element glue | No: through its body (counted) |
| DropGlue (`AdmitType`) | destructor body, parts' glue, `clearEntries` | **Yes**: Field expansion of a non-regular Type behind an owning handle (above); structural parts are subterms |
| Builtin comparison (`Comparing`) | composite parts, Witness body, Requirement witness | No: composite parts are subterms (R5-M166) |
| Builtin Format/Arithmetic, PropertyWitness | none / Field glue | No |
| ClosureErasure, ItemAdapter | callee body, glue | No: through the body |
| ObjectDescriptor, VTable | glue, VTable / slot bodies | No: through bodies |

| Probe (`probes\`) | Shape | Today | Prototype (bodies-only recurrence) |
| --- | --- | --- | --- |
| p1-compare80x50 | 80-deep generic chain comparing a closed 50-deep Tuple nest | GRL at `x == x` (241:12) | none |
| p6 / p1b | 80 x 40 / 0 x 50 | none | none |
| p2-ctor80x50 | the chain ending in 50 nested generic constructors (`D1<T>`..`D50<T>`) | 4 GRL (D48/D49 `init`) | none |
| p8-chain300 | 300 distinct generic functions, no recursion | GRL at g128's call | none |
| p10-ring50 | 50 structs linked by `Option<rc/S>` (= `StorageRingsMeetTheGlueDepth(50)`) | GRL at `v@move` | none |
| p3-polyrec | SPEC 8.15.3's `depth<Option<T>>` | GRL at the call | GRL, same |
| p7-mutual | growing 3-cycle f -> g<Option<T>> -> h -> f | GRL 5:39 | GRL 5:39 |
| p9-longcycle | growing cycle through 140 distinct functions | GRL at g128's call | `Unsupported_Kd` (storage nesting) at the cycle's call: Grows' window missed it |
| p4/p5 non-regular `rc/` (open payload) | Field `Option<rc/Nest<(T, T)>>` | `Unsupported_Kd` at `init` (G128) | same |
| ComponentSummaryTest non-regular `obj/` | above | GRL at `c@move` | **stack overflow** |

## 4. Options

**A. Type-driven items inherit their parent's depth** (AdmitType/AdmitRow add 0). Owner `InstanceCollector.Admit.cs`; deletes two
checks (about -10 lines). Fixes p1 and p10, not p2 or p8 (body chains of distinct definitions still count); removes the only bound on
DropGlue recurrence (the overflow); the depth becomes a weighted path under hop-FIFO, so order-dependent. Rejected.

**A'. Depth +1 only on a Body edge that changes the substitution.** Fixes p1, p2, p8, p10, but the same overflow, still a flat count
of non-recurring chains whose arguments change, a substitution comparison per admission and an order-dependent depth. Rejected.

**B (recommended). One recurrence rule for every item; the flat depth deleted.**
- Identity: a Body or LegacyRow item's definition (today's Grows key); a type-driven item's (kind, declaration) when its Type is
  nominal; none for a structural Type (Tuple, reference layer, handle, array, Function), whose parts are its subterms.
- Generation, at admission: the nearest item with the same identity on the parent's admission chain, searched over at most 128
  identity-bearing items (the walk bound today's depth gives Grows); none: 1; a type-driven key whose Type is a proper subterm of
  that ancestor's (a descent: `Box^20<i32>`'s glue) inherits its generation; otherwise its generation + 1. Past 16 (GrowthLimit) is
  `GenerationResourceLimit_Kd` at the requesting site, as Grows today. SetLimit (1,024 per identity, now per declaration too) stays
  the breadth limit and the backstop for a cycle longer than the window.
- Deletes `DepthLimit`, the three depth checks, `MonoItem.Depth` (becomes `Generation`, a byte) and Grows' full walk; the subterm
  test (a walk over the ancestor Type's distinct parts) runs only on a same-identity hit. Size: about +25/-30 in `Admit.cs` and
  `Instantiation.cs` (the bodies-only prototype was +10/-23).
- Valid programs: p1, p2, p8, p10 and written nests get no record. Non-terminating: p3, p7, every existing growth row (`grow<Box<T>>`,
  `AGrowingDestructorStopsAtItsDrop`, `ATablesDependencies...`, `GrowingSubstitutionKeys...`, `GrowingDefaultSubstitutions...`,
  `GrowingDestructorContexts...`) keep their record (the same count while repeats are within 128 identity items); the non-regular
  `obj/` chain gets GRL at `c@move` at generation 16 instead of depth 128; never a hang (FIFO worklist, no admission recursion).
  Known limit: a growing cycle through more than 128 distinct identities (p9) is stopped by SetLimit or Binding's storage bound, a
  located `Unsupported_Kd` (G128's gap), not Resource.
- Cost (V16): at most 128 identity steps per new key, as today's Grows for bodies; O(1) before for type-driven keys, now the same
  bounded walk; no allocation (fields only). Measure warm Analyze on the Generic 128 workload and the collector's `Work`.
- Order: the first admitter's chain, as Grows today; G153's placement unchanged.

**B2. Type-driven parts read their storage verdict** (`Made`), so a non-regular Type is BU-12's storage-nesting gap and bodies-only
recurrence suffices. Rejected as the lasting rule: it leans on G128's interim bound (gone at R7, which would reopen the overflow) and
turns the `obj/` program's Resource record into `Unsupported_Kd`.

**C. Static recurrence (SCCs of the definition graph).** Rejected: requirement, virtual, Closure and glue targets exist only per
instance; a static graph over- or under-approximates; a second mechanism beside Grows.

**D. Substitution growth by height per definition.** Rejected: counts a non-recursive generic used at a shallow and a deep Type (the
same defect); length slots grow without height.

Tests (B): fail-first at the collector layer (`InstanceCollectorTest`): p1's and p2's shapes, no record; `StorageRingsMeetTheGlueDepth`
50 -> no record plus a 120-ring (an expectation change under the owner's decision); a scaling row, a non-recursive chain of 200, 400
and 800 generic functions, no record and `Work` linear. Invalid counterparts: the existing growth rows unchanged; p7's mutual cycle; a
non-regular `obj/` ring through three declarations without bodies (GRL at the destroyed value); p9 (terminates, located record);
`Box^40<i32>` destroyed (descents, no record). `ComponentSummaryTest`'s comment corrected (the record is the recurrence rule's).

## 5. Classification and sizing

Major: one owning component (`MonoItem.Depth` is read only in `Admit.cs`), but it departs from K package 4.6/KD9, an owner-approved
contract, so the owner approves the departure (OQ1) and the open design issue (the window's measure) takes a design review. DECISIONS:
one entry (the recurrence rule; supersedes KD9's depth clause; amends R5-M166's Known limit); DIAGNOSTICS:361, STATUS:81 (generic
generation), `InstanceCollector.cs:62-63`, the MonoItem comment; PLAN G332 done. G153 and G289 (4) do not ride: no record path,
placement or `InstanceCollector.Callees.cs` change.

| Unit (each <= 1 h) | Content | Checks |
| --- | --- | --- |
| U1 | identity and generation for every item, SetLimit per identity, depth kept; the `obj/` ring and `Box^40` rows; the test comment | Unit: InstanceCollectorTest, ComponentSummaryTest, GenericStorage/GenericDestructorEmissionTest, RecursiveDefaultTest, VirtualGenericDispatchTest, MinimalEmissionTest, TypeDagScalingTest; snapshot |
| U2 | delete the depth; p1, p2, ring, scaling, p7, p9 rows; the ring expectation; DECISIONS, DIAGNOSTICS, STATUS, PLAN | the same classes; warm Analyze A/B; Session |

No native fixture: a program under today's limits keeps its set and order (every same-identity gap is within its depth of 128, and
U1's type-driven count refuses only non-subterm repeats of a declaration, which need a non-regular Type; U1's run confirms it).

## 6. Open questions

- **OQ1 (owner):** depart from K package 4.6/KD9: the collector's limits become recurrence (16 generations of one definition or
  declaration on an admission chain) and breadth (1,024 items of one), the flat depth 128 deleted?
- Planning: is p9's category (storage-nesting `Unsupported_Kd` past a 128-identity window) acceptable, or a larger window?
- Planning (open issue for BU/R7): `Binding.OperandContexts` recurses on Type height; any path admitting a very high Type overflows.
