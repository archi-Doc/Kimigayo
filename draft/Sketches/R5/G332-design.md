# G332 design: the instance collector's depth rule

Status: design phase of PLAN G332 (R5, 2026-10-31), revision 1 after design review 1 (REVISE). Working record, not authority: what it
decides becomes authority only as a DECISIONS entry in the commit that implements it. Base: `origin/dev` `def70d15d`. No source change
committed; two throwaway prototypes of the growth test (uncommitted, reverted, the second's script
`C:\Users\bwff1\AppData\Local\Temp\claude\r5\g332\proto2.py`) and CLI probes under `...\r5\g332\probes\` and the reviewer's scratchpad.

## Revision 1

| Finding | Revision 0 | Revision 1 |
| --- | --- | --- |
| B1 (blocking) | B counted every repeat of an identity that is not a proper subterm, so sideways repeats (a ring's `Option<rc/S_k>`, a shared `Helpers.wrap<T>`) and descents through an intermediate generic (`Option<W^k>`) were refused, which dev accepts; U1 was not behavior-preserving | the generation counts only growth: the key's height above the largest recorded for its identity on the chain (`Peak`, carried); repeats at the same or a lower height inherit. Re-prototyped (§3): ring 17/42/50, nest 16/20, the helper chain, p1, p2 and p8 get no record; every growing probe keeps its GRL. U1 is now the behavior-preserving restructuring |
| Height source | a subterm walk per hit | Binding publishes no height (BoundType's summary flags only: `ContainsParameter`, `CarriesOrigin`, ...; BU-55 has none); the collector memoizes `Height` once per distinct interned Type (a reference-keyed table cleared per run: linear in distinct parts, no warm allocation). BU proposal, not required: `BoundType.Height` beside `containsParameter` (one int per Type, O(components) at interning; owner BU) would delete the memo |
| N1 | p9's class only | a type-driven cycle past the window (130 structs through `obj/`) stays Resource; an invalid counterpart and an A/B row |
| N2 | walk over every chain item | a nearest-identity-ancestor link (`Above`) per item: at most W steps per new key |
| N3 | identity table incomplete | §4 names each Type kind |
| N4 | KD9's depth clause | the entry supersedes KD9's whole limit clause (generation, chain count and SetLimit); OQ1 names `StorageRingsMeetTheGlueDepth`'s change |
| N5 | W = 128 | no window-free formulation; W decided by U3's A/B (the planning session leans to 1,024 if flat): O(W) per new key |

## 1. Required meaning

| Source | Rule |
| --- | --- |
| SPEC 8.15.3 | Polymorphic recursion with a growing Type argument is valid; one copy per instantiation "may reach an implementation-defined limit", a resource failure. |
| SPEC 6.1 (finite inline storage) | "Only growth of distinct instantiations through an indirection (for example polymorphic recursion) can reach a generation limit." |
| IMPL 21.3.2 | Analysis and generation limits (instantiation growth, context depth, default-expansion count, analysis storage, string-interpolation nesting) are implementation-defined; exceeding one is a Resource diagnostic at the affected declaration, call or token. |
| IMPL A.4, SPEC 23.1.1 | Deep or large input never exhausts the host stack; a limit is `Resource`, never Language or acceptance. |

The collector's limit is "instantiation growth": it bounds growth of distinct instantiations, and a valid program without growth must
be accepted. A program's own nesting is bounded by its input. No SPEC/IMPL gap in the rules; reported only: IMPL A.4 names this
implementation's 64 Semantics cases but not its instantiation limits (DIAGNOSTICS:361 states them).

## 2. Cause chain

- K package 4.6, KD9 (owner-approved, R5-M18): "depth 128 on every item; growing key 16 and set 1,024 on Body items only".
  `InstanceCollector.Admit.cs:11` `DepthLimit`; checks at `:79-85`, `:122-127`, `:178-183`; `Grows` `:222-236` walks the whole chain.
- Every hop counts a level, and Grows counts every repeat of a definition, growing or not (the helper chain: 17 sideways
  `Helpers.wrap<A_k<T>>` on one chain is GRL on dev).
- The depth is today the only bound on DropGlue recurrence: DropGlue's parts are Fields under a substitution, read without a storage
  verdict, and an owning handle is indirect for Layout, so a non-regular Type behind `obj/` (`Chain<T>` holding
  `Option<obj/Chain<(T, T)>>`) expands forever without it: revision 0's bodies-only prototype overflowed the host stack in
  `Binding.OperandContexts` (`ComponentSummaryTest.ANewlyOwnedNonRegularValueMeetsTheGenerationLimits`, whose comment misnames the
  record as R5-M44's layout depth).
- Order: `Depth` is the hop-BFS distance (order-independent); Grows reads the first admitter's chain, chosen by site order, and a refused
  key is not remembered; SetLimit's victim is order-dependent (placement only, as G153).

## 3. Census and prototype results

| Item kind | Recurs by itself? |
| --- | --- |
| Body, LegacyRow | Yes (a definition cycle with a growing substitution); LegacyRow only through its body |
| DropGlue | Yes: Field expansion of a non-regular Type behind an owning handle; structural parts are subterms |
| Builtin comparison, Format, Arithmetic; PropertyWitness; ClosureErasure, ItemAdapter; ObjectDescriptor, VTable | No: subterms, or through a body |

Prototype: B's rule, depth deleted, W = 1,024 identity items; dev is `def70d15d`. "r1a" used a global per-declaration count for
type-driven items, "r1b" the chain count of §4 (the design).

| Probe | Shape | dev | r1a / r1b |
| --- | --- | --- | --- |
| p1 | 80-deep chain comparing a 50-deep Tuple nest | GRL `x == x` | none |
| p1b, p6 | 0 x 50, 80 x 40 | none | none |
| p2 | the chain ending in 50 nested generic constructors | 4 GRL | none |
| p8 | 300 distinct generic functions | GRL at g128's call | none |
| p10, ring 50 | 50 structs, `Option<rc/S_{k+1}>` | GRL 104:18 | none |
| ring 16, 17, 20, 42 | | none | none |
| ring 120 | past Binding's storage walk (256 Types) | GRL + `Unsupported_Kd` | `Unsupported_Kd` (G128) only |
| nest16s, nest20s | `Option<W^n<string>>`, `W<T>.next: Option<T>` | none | none |
| nest20 | `Option<rc/W^20<i32>>`, `W<T>.next: Option<rc/T>` | `UnprovenConstraint_Kd` 2:29 (Binding) | same |
| helper20 | 20 `A_k<T>.run` through one `Helpers.wrap<T>` | GRL 70:47 | none |
| p3, p7 | polymorphic / mutual growing recursion | GRL at the call | same (generation 17) |
| p9 | growing cycle through 140 functions | GRL at g128's call (130:42) | GRL at the cycle's call (142:42), Resource with W = 1,024 |
| p4, p5 | non-regular `rc/` to an open payload | 2 `Unsupported_Kd` | same |
| p11, chain | non-regular `obj/` (`ComponentSummaryTest`'s shape) | GRL at the local + `Unsupported_Kd` | same (generation 17) |
| cycd130 (N1) | 130 structs through `obj/`, the last to `C1<(T, T)>` | GRL 262:5 | same (generation 17, lap 17) |
| cyc130 | the same through `Option<obj/>` | GRL 262:5 | same (r1a: 1,025 Option glue items, lap 8; r1b: chain count 1,025, lap 8) |

The test classes were not run on r1 (revision 0's run found the ring row and the overflow); U2 runs them.

## 4. Options

A (type-driven items inherit the depth) and A' (depth only on substitution-changing body edges): rejected as in revision 0 (p2 and p8
stay refused; the `obj/` overflow; an order-dependent weighted depth). B2 (type-driven parts read a storage verdict): rejected, leans on
G128's interim bound. C (static SCCs) and D (height per definition, unchained): rejected.

**B (recommended): one growth rule for every item; the flat depth deleted.**
- Identity: a Body or LegacyRow item's definition; a DropGlue item's declaration when its Type is Nominal or Constructed (a struct, an
  enum, Option, Array, Dictionary, a library container) or a Closure Type (its definition); none for Tuple, FixedArray, Function,
  Slice, Range, reference and handle layers and Primitives (their glue's parts are subterms) and for every other type-driven kind
  (census). A function Item Type has nothing to destroy, so no glue item.
- Height: a type-driven key's Type's height; a body key's, the largest over its substitution's Self, declaring Type and Types
  (`Height`, memoized per distinct Type).
- Generation at admission: the nearest item with the same identity, found through the `Above` links over at most W identity items;
  none: generation 1, `Peak` the height; a height above that ancestor's `Peak`: its generation + 1 and the new peak; otherwise its
  generation and peak. Past 16 is `GenerationResourceLimit_Kd` at the requesting site.
- Backstops: a body definition's 1,024 items (KD9's SetLimit, global, unchanged); a type-driven identity's count on the chain
  (`Repeats`, the ancestor's + 1) past 1,024, for growth height does not show (length slots) and cycles past the window. A global
  per-declaration count is rejected: it counts breadth (r1a reached 1,024 `Option` glue items in cyc130), which a large valid program
  can have.
- Fields: `MonoItem.Depth` becomes `Generation` (byte) plus `Peak` (ushort), `Repeats` (ushort) and `Above` (int); `DepthLimit`, the
  three depth checks and Grows' walk deleted. About +70/-35 lines (`Admit.cs`, `Instantiation.cs`, `InstanceCollector.cs`).
- Effect: every no-growth probe above passes; every growing one keeps its GRL (Resource), at the growing call or the destroyed local;
  never a hang or an overflow (the worklist; heights only grow 16 generations per identity within W). Known limit: a cycle past W
  falls to the backstops, and a body cycle that never repeats within W to SetLimit or Binding's storage bound (`Unsupported_Kd`).
- Cost: at most W steps per new key (O(W·n) worst, N5) plus the memo, linear in distinct Type parts; no warm allocation.
- Order: the first admitter's chain, as Grows today.

Tests: fail-first (`InstanceCollectorTest`): p1, p2, helper20 and an 800-function chain without a record, `Work` linear over
200/400/800; `StorageRingsMeetTheGlueDepth` 50 -> no record plus the largest ring under the storage walk's cap (about 85 structs; the owner's decision); nest20s.
Invalid counterparts: the existing growth rows unchanged; p7; p11 and cycd130 (GRL at the destroyed value); p9 (Resource with W =
1,024). `ComponentSummaryTest`'s comment corrected.

## 5. Classification and sizing

Major: one owning component (`Depth` is read only in `Admit.cs`), departing from K package 4.6/KD9, an owner-approved contract (OQ1,
asked). DECISIONS: one entry superseding KD9's whole limit clause and amending R5-M166's Known limit; DIAGNOSTICS:361, STATUS:81,
`InstanceCollector.cs:62-63`; PLAN G332 done. G153 and G289 (4) do not ride.

| Unit (each <= 1 h) | Content | Checks |
| --- | --- | --- |
| U1 | restructuring, behavior unchanged: `Height`, `Above`, `Generation`/`Peak`/`Repeats` carried, Grows read through them counting every repeat as today; depth kept | InstanceCollectorTest and the GRL classes (ComponentSummary, GenericStorage/GenericDestructorEmission, RecursiveDefault, VirtualGenericDispatch, MinimalEmission, TypeDagScaling); snapshot 0 |
| U2 | the growth test, type-driven identities, the chain count; the depth deleted; §4's rows | the same classes; snapshot (only programs dev refuses change) |
| U3 | A/B (Generic 128, the 800-function chain, cycd130) choosing W; DECISIONS, DIAGNOSTICS, STATUS | measurement; Session |

No native fixture: U1 keeps every set; U2 changes only programs dev refuses.

## 6. Open questions

- **OQ1 (owner, asked):** depart from K package 4.6/KD9 (the flat depth deleted; growth 16 per identity on a chain; the chain count and
  SetLimit as backstops; `StorageRingsMeetTheGlueDepth`'s 50-ring accepted)?
- Planning: W by U3's A/B (lean 1,024).
- Open issue for BU/R7: `Binding.OperandContexts` recurses on Type height.
