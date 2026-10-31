# G332 design: the instance collector's depth rule

Status: design phase of PLAN G332 (R5, 2026-10-31), revision 3: design reviews 1 and 2 REVISE, review 3 ACCEPT with the planning
session's rulings folded. Working record, not
authority: what it decides becomes authority only as a DECISIONS entry in the commit that implements it. Base: `origin/dev`
`def70d15d`. No source change committed; throwaway prototypes (uncommitted, reverted; revision 2's is
`C:\Users\bwff1\AppData\Local\Temp\claude\r5\g332\proto3.py`, the collector's height memo standing in for BU's `BoundType.Height`) and
CLI probes under `...\r5\g332\probes\` and the reviewer's scratchpad.

## Revisions

| Rev | Finding | Change |
| --- | --- | --- |
| 1 | B1: every non-subterm repeat counted (sideways rings, a shared `wrap<T>`, descents through a generic) | a generation counts only growth: a height above the largest recorded for the identity on the chain (`Peak`); lower or equal heights inherit |
| 1 | N1-N5 | `Above` link (walk <= W), identity table per Type kind, KD9's whole clause superseded, W by A/B |
| 2 | R1: a type-driven cycle longer than W never stops (`Repeats` resets with the W-bounded search) | **cause one level up (§2)**: each item kind's non-termination has one measure; every backstop is window-free: bodies keep the global SetLimit, type-driven keys get a height cap (128) that replaces `Repeats` |
| 2 | N1: the chain count refused long sideways chains | gone with `Repeats` |
| 2 | N2: written growth without recurrence (helpergrow) still GRL | a Known limit (§5) |
| 2 | N3: ushort wrap | `Generation` and `Peak` are saturating bytes (type-driven heights <= 128 by the cap, body heights by Binding's storage bound) |
| 2 | N4: U1 carried unread fields and a memo | BU publishes `BoundType.Height` (owner's approval); no memo; units re-cut after BU's unit (§6) |
| 3 | Review 3 ACCEPT; planning ruling (1) | one window-free breadth rule for every item: a global count of 1,024 per type-driven identity (DropGlue, declaration), the bodies' SetLimit rule (the reviewer's prototype: b = 2 stops at 1,025 items in 3.4 s; ring 50, nest16s, cyc130, cycd130 unchanged) |
| 3 | Ruling (2) | Known limits corrected: the branching bound is b^16 within W; a written Type above height 128 reaches the cap only behind a handle (hnest130, tnest130: GRL on dev too, no regression) |
| 3 | Ruling (3) | U1 changes behavior: it carries `StorageRingsMeetTheGlueDepth`'s expectation change and the fail-first rows |
| 3 | Ruling (4) | the b = 3 three-shape program hangs on dev before the collector: BU's open issue, outside G332 (the planning session records it) |

## 1. Required meaning

SPEC 8.15.3: polymorphic recursion with a growing Type argument is valid and may reach an implementation-defined limit, a resource
failure. SPEC 6.1: "Only growth of distinct instantiations through an indirection (for example polymorphic recursion) can reach a
generation limit." IMPL 21.3.2: limits (instantiation growth, context depth, default-expansion count, analysis storage,
string-interpolation nesting) are implementation-defined; exceeding one is a Resource diagnostic at the affected declaration, call or
token. IMPL A.4: deep input never exhausts the host stack. So the collector bounds growth, and a valid program without growth is
accepted. No SPEC/IMPL gap in the rules; reported only: IMPL A.4 names the 64 Semantics cases, not the instantiation limits.

## 2. Cause, one level up

Both reviews' findings came from measuring growth by counting repeats along a W-bounded chain (revision 0 counted hops, revision 1
`Repeats`): a count along a bounded window is not a measure of growth, so it either refuses sideways repeats or resets on long cycles.
The collector's keys grow in exactly two ways, each with one window-free measure:

| Kind | Key | How an infinite set arises | Measure |
| --- | --- | --- | --- |
| Body, LegacyRow | definition + erased substitution | a definition cycle whose substitution grows in height, or in a function length slot (SPEC 4.4: only functions have length parameters) at fixed height | the global per-definition count (SetLimit 1,024, KD9; window-free, catches both); the generation on height growth within W is the early, located stop |
| Type-driven (DropGlue, comparison, shims, property witness, adapters, descriptor, VTable) | kind + erased Type (+ symbol, variant) | only through Types of unbounded height: a struct has no length slot, so with finitely many declarations, arities and written lengths, Types of bounded height are finitely many; type-driven expansion alone grows them only through a non-regular Type behind an owning handle (§3's `obj/` probes) | `BoundType.Height` of the key's Type, capped |

The flat depth measured neither (it counted the input's nesting). A single height cap cannot replace the bodies' rule: a body's
substitution is Made, so its storage verdict reaches Binding's storage bound (BU-12, height 64) first and a polymorphic recursion would
become G128's `Unsupported_Kd` instead of Resource; a cap low enough to stay Resource (about 32) refuses valid written Types (p1's
51-high Tuple nest as a generic argument). Order: the depth was the hop-BFS distance; the generation reads the first admitter's chain,
as Grows today (placement only, G153).

## 3. The rule

- **Bodies (and LegacyRow):** identity = the definition; height = the largest `BoundType.Height` over the erased substitution's Self,
  declaring Type and Types. At admission the nearest same-identity ancestor within W identity items (through `Above`); none:
  generation 1, `Peak` = height; a height above its `Peak`: its generation + 1; otherwise its generation and `Peak`. Past 16 is
  `GenerationResourceLimit_Kd` at the requesting call. SetLimit (1,024 items of a definition, global) unchanged: the window-free stop
  for cycles past W and length-slot growth.
- **Type-driven items:** a key whose Type's `BoundType.Height` exceeds 128 is `GenerationResourceLimit_Kd` at the requesting site
  (window-free; replaces `Repeats`). DropGlue items of a Nominal, Constructed or Closure Type also take the generation rule (identity
  = (DropGlue, declaration)) for the early stop and SetLimit's breadth rule: the 1,025th item of one identity, counted globally, is
  `GenerationResourceLimit_Kd` (revision 3: one breadth rule for every item; it refuses only a program with more than 1,024
  drop-needing instantiations of one declaration, the scale the bodies' SetLimit already refuses); every other type-driven kind has
  no identity (its parts are subterms or bodies).
- **Why 128:** every Type an accepted program stores, passes, creates or compares by value is Prepared under Binding's storage height
  64 (BU-12), and each nesting level adds at least one storage level, so its `BoundType.Height` stays near 64; 128 leaves the
  factor two. A Type past it arises only from type-driven growth (a non-regular Type behind a handle) or from a Type Binding already
  refuses at its storage (G128); reaching it is IMPL 21.3.2's "instantiation growth" limit, Resource. The cap also bounds the height
  Binding's recursive walks receive from the collector (IMPL A.4; G348).
- Deleted: `DepthLimit`, the three depth checks, Grows' full walk, `MonoItem.Depth` (`Generation` and `Peak` bytes, `Above` int).
  About +45/-35 lines. Cost: at most W steps per new body or DropGlue key, O(1) height reads; no allocation.

## 4. Prototype (revision 2, proto3: W = 1,024 unless stated; 60 s bound per probe; dev = `def70d15d`)

| Probe | Shape | dev | Revision 2 |
| --- | --- | --- | --- |
| cycd130, W = 128 | 130 structs through `obj/`, the last to `C1<(T, T)>` | GRL 262:5 | GRL 262:5 by the height cap (h 129), 4.2 s process |
| cycd130, W = 1,024 | | GRL 262:5 | GRL 262:5, generation 17 |
| cyc130, W = 128 / 1,024 | the same through `Option<obj/>` | GRL 262:5 | GRL 262:5, generation 17 (Option's repeats within W) |
| ring 17, ring 50 | `Option<rc/S_{k+1}>` rings | none / GRL 104:18 | none / none |
| ring 120 | past the storage walk's 256 Types | GRL + `Unsupported_Kd` | `Unsupported_Kd` 244:13 (G128) |
| nest16s | `Option<W^16<string>>` | none | none |
| helper20 | 20 `A_k.run` through one `wrap<T>` | GRL 70:47 | none |
| helpergrow | 17 `A_k` with `wrap(A_{k+1}<Option<T>>)` | GRL 70:47 | GRL 70:47 (Known limit) |
| p1, p1b, p6 | comparisons 80 x 50, 0 x 50, 80 x 40 | GRL / none / none | none |
| p2, p8 | 50 nested constructors; 300-function chain | 4 GRL; GRL | none; none |
| p3, p7 | polymorphic; mutual growing recursion | GRL | GRL (generation 17) |
| p4, p5; p4b | non-regular `rc/`; regular | 2 `Unsupported_Kd`; none | same |
| p9, W = 1,024 / 128 | growing cycle through 140 functions | GRL 130:42 | GRL 142:42 / `Unsupported_Kd` 142:42 (storage bound before SetLimit) |
| p11 | non-regular `obj/` (`ComponentSummaryTest`) | GRL + `Unsupported_Kd` | same (generation 17) |

The test classes were not run on revision 2; U1 runs them.

## 5. DECISIONS entry (outline) and Known limits

Supersedes K package 4.6/KD9's limit clause (owner question OQ1, asked) and amends R5-M166's Known limit; DIAGNOSTICS:361 and STATUS's
generic generation row follow. Known limits: (1) growth written into a non-recursive chain (helpergrow: 17 distinct functions each
adding `Option`) is GRL at generation 17, since the generation measures height growth per definition on the chain, not recurrence;
(2) a Type past Binding's storage walk (ring 120) is G128's `Unsupported_Kd` at the call, not this rule's; (3) a body cycle that grows
but repeats only past W meets Binding's storage bound first (`Unsupported_Kd`) or SetLimit; W from the A/B (the planning session leans
to 1,024); (4) a branching non-regular Type (b growing Fields) is bounded by b^16 items within W and by the global count of 1,024
items per declaration (revision 3); (5) a written Type above height 128 reaches the cap only behind a handle (hnest130, tnest130),
GRL on dev too.

## 6. Classification and units

Major (one owning component; `Depth` read only in `Admit.cs`), departing from an owner-approved contract (OQ1). G153 and G289 (4) do
not ride. After BU's `BoundType.Height` unit:

| Unit (each <= 1 h) | Content | Checks |
| --- | --- | --- |
| U1 | the rule reading `BoundType.Height`, the global count per type-driven identity; the depth and Grows' walk deleted; `ComponentSummaryTest`'s comment; behavior change: `StorageRingsMeetTheGlueDepth`'s expectation (OQ1) and the fail-first rows p1, p2, helper20 and the ring | InstanceCollectorTest and the GRL classes (ComponentSummary, GenericStorage/GenericDestructorEmission, RecursiveDefault, VirtualGenericDispatch, MinimalEmission, TypeDagScaling); snapshot (only programs dev refuses change) |
| U2 | rows: the largest ring under the storage cap, an 800-function chain with `Work` linear; p7, p11, cycd130 at W, helpergrow (Known limit), the cap row, a b = 2 breadth row; A/B (Generic 128, the 800 chain, cycd130) choosing W; DECISIONS, DIAGNOSTICS, STATUS, PLAN | the same classes; measurement; Session |

No native fixture: only programs dev refuses change.

## 7. Open questions

- **OQ1 (owner, asked):** depart from KD9 (depth deleted; growth generations and SetLimit for bodies, a height cap and generations for
  type-driven items; `StorageRingsMeetTheGlueDepth`'s 50-ring accepted)?
- Planning: W by U2's A/B (lean 1,024). Implementation waits for OQ1 and BU's `BoundType.Height` unit.
- Outside G332 (BU's open issue, recorded by the planning session): the b = 3 three-shape program hangs on dev before the collector.
