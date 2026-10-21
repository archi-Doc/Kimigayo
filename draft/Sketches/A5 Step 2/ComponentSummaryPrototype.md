# 2x-P12: component-summary prototype (Owned and Transferable)

Draft record of unit 2x-P12 (package 2b-D §3 and §5, DECISIONS R4-M89; PLAN G124, G174). Base: `origin/dev` `4eb19789`. No
compiler, SPEC, test or tracked-prototype change; no verify run. "(H)" marks a reading that no run checked. Working record, not
authority: SPEC and DECISIONS decide.

## Owner decision (2026-10-21)

The independent review (`p12-review.md`) found blocking defects in two rounds on the peel family (P12-1, then R1-1 and R1-2). The
owner then chose between the two options of the F1/F2 proposal. The owner's answer, verbatim:

> 「案2: その場で U (推奨)」 (option 2: a U on the spot, the recommended one)

- **Decided rule: option 2.** A Transferable judgment that reads a summary needing `outer(X)` or holding a peel cycle raises a
  located `Unsupported_Kd`. It is demand-driven and recorded once per declaration, as F5 (a) is for projection families.
- **Option 1 is a future candidate, registered at X-2.** It is the representation `outer(X)`/`target(X)` with peel templates and
  the use-site self-reference rule. R1-1 and R1-2 stay open against it (see "Option 1" below).

This revision (revision 2) models option 2 as the main rule ("Decided") and keeps option 1 as a variant.

Run in an empty folder outside the repository (.NET SDK 11.0.100-rc.2):

- `dotnet run ComponentSummaryPrototype.cs` runs 2,000 random graphs in about 40 s.
- `-- -random 10000 [-- -seed S]` runs more graphs, in about 4 minutes.
- Exit code: Decided's failures, 0 on every run recorded here.

## Model

- **Types.** A small term language:
  - primitives; Type formals and abstract Types (`var`);
  - projections with segments, where a starred segment is a family;
  - layers whose Semantics is concrete, a pair binder, or `outer(X)`;
  - nominal Types with Type and Origin arguments; tuples; fixed arrays; `fn`; `item(...)`; `dyn C`;
  - `target(X)`;
  - `core:sealed` and `core:open`, the outer core of a Type in a layer obligation.

  Only option 1 records `outer(X)` and `target(X)`; under option 2 they mark what the summary leaves unsupported.
- **Program.** 75 declarations:
  - the Kimi prelude: Option, Array, Dictionary, Slice, the iterator storage, Weak, Sender, FixedBuffer, Box and an open OBox;
  - the case declarations;
  - 9 Contracts and 7 conformances. A conformance is verified without itself (8.3.3), and an intrinsic parent is decided by the
    judgment (8.7.1).
- **Summary (§3.2).** `(ExclT, ExclO, Error, Obl)` comes from one walk of a declaration's storage in its formals. It is the least
  fixed point over all declarations from (no, no, no, ∅), seeded with the previous round.
  - A nominal component adds its bits, walks its arguments and maps its `Obl` through [formals ↦ arguments].
  - `Layer(s, X)` keeps only X's outer core (the core reduction, below).
  - The walk of the declaration's own storage checks SPEC 8.7.3's Object Target.
- **Option 2 (Decided).** When the mapping would produce an obligation naming `outer(X)` or `target(X)`, it is not recorded. That
  happens when E's pair parameter is instantiated with the declaration's own plain formal or projection, which every peel is.
  - The declaration records itself as the site of a located U for Transferable. For Owned too, if the obligation is a projection
    obligation, which Owned reads; no case or random graph produced one.
  - The site set propagates through nominal components, like the exclusion bits.
  - A projection family (a cycle of the projection-link graph with a projection step) is F5 (a)'s site, for both intrinsics.
- **Use.** `Owned(D<A,o>) = ¬ExclO ∧ o = static ∧ Owned(A) ∧ Proj[A]` and `Transferable(D<A>) = ¬ExclT ∧ Transferable(A) ∧ Obl[A]`.
  - Error absorbs, then L (the located U), then Refuted, then Unknown.
  - A judgment that reads a site's summary raises its U (L) and records the site.
  - Abstract Types are judged by the Contract-knowledge closure of the premises (8.3.2).
- **Anchor (§3.5).** One verdict per static under its container's premises; A4's table maps it (L: a located U).
- **Oracles.**
  - Every case's SPEC verdict is written by hand. The review's cases j1–j4, d1, x1–x3, b1–b3 and s1–s6 carry the reviewer's hand
    verdicts, which its reference walk confirmed at depth 90–200, uncut.
  - Bounded8 enumerates family obligations.
  - On random graphs, the reference is an instantiation BFS over SPEC's components; see "Oracle".

## Rules under test

- **Decided.** Option 2, with F3, F4, F5 (a), F6 and the core reduction. On the pair and family cases, the expected verdict
  (SPEC with the located U in place of Transferable's answer, and of both answers for a family) is in parentheses.
- **Option1.** Option 1 as in revision 1: `outer(X)`/`target(X)`, the core reduction, peel templates with step j ≤ 4 and at most 64
  instances at use, the use-site rule, and Q*.s families. R1-1 and R1-2 are left open, as the owner decided.
- **Package.** §3.2's wording. It shares the model's core reduction and coverage, so it isolates the wording.
- **Variants of Decided:**
  - PairBinderOnly: option 2 without its U, so the obligation is dropped silently.
  - NoCore: without the core reduction.
  - A and B: the two designs' original rules. TwoValued: the pre-P-2 wording.
  - NoneRefutes, TargetIfSealed and FamilyPrefix: §3.2's wording, one piece each.
  - FamilyQs: F5 (b)'s Q*.s families. NoInternal: without the iterators' internal storage. Bounded8: the family oracle.
  - Guard, Cap4U, Cap4P: instantiation walks.
  - BodyAnchor: the reading body's anchor.

## Cases

The brief's cases, the cases added in revisions 0 and 1 (listed in those commits), and in revision 2:

- the review's round-2 cases;
  - j1–j4: a five-peel ring `Q0`…`Q4`;
  - d1: a 70-layer argument;
  - x1–x3: two swapped pair binders;
  - b1–b3: a self-reference through Array;
  - s1–s6: a pair binder over a nominal with its own layer obligation;
- g1 and g2: a pair binder over a composite argument that grows non-regularly (`G2<s/T, U>` stores `s/OBox<U>` and
  `Option<obj/G2<s/T, Box<U>>>`). This shape needs neither `outer` nor a peel, and it tests the core reduction.

| Case | Query | Premises | SPEC | why | Package | Option1 | Decided (expected) | meets |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| c1 | `i32` |  | P/P | primitive | P/P | P/P | P/P | yes |
| c2 | `string` |  | P/P | string is a primitive | P/P | P/P | P/P | yes |
| c3 | `rc/Item` |  | P/R | 15.2.4: rc refutes | P/R | P/R | P/R | yes |
| c4 | `ref/(rc/Item) during static` |  | P/R | rc behind ref | P/R | P/R | P/R | yes |
| c5 | `ref/i32 during static` |  | P/P | valid neighbour of c4 | P/P | P/P | P/P | yes |
| c6 | `ref/i32 during local` |  | U/P | 15.2.3: an unproven Origin; 15.2.4 requires no Owned | U/P | U/P | U/P | yes |
| c7 | `arc/Holder` |  | P/R | rc behind arc (Holder stores rc/Item) | P/R | P/R | P/R | yes |
| c8 | `arc/Item` |  | P/P | Sealed payload | P/P | P/P | P/P | yes |
| c9 | `Weak<rc/Item>` |  | P/R | Weak of rc | P/R | P/R | P/R | yes |
| c10 | `ref/(Weak<rc/Item>) during static` |  | P/R | Weak of rc behind ref | P/R | P/R | P/R | yes |
| c11 | `Weak<arc/Item>` |  | P/P | Weak counts as storing arc/Item; only Weak of rc is listed | P/P | P/P | P/P | yes |
| c12 | `raw/i32` |  | P/R | a raw pointer outside the exemption; Owned visits the pointee | P/R | P/R | P/R | yes |
| c13 | `arc/RawHolder` |  | P/R | raw behind arc | P/R | P/R | P/R | yes |
| c14 | `fn` |  | P/R | common Function Type (g3) | P/R | P/R | P/R | yes |
| c15 | `item()` |  | P/P | a Function Item stores nothing | P/P | P/P | P/P | yes |
| c16 | `item(i32)` |  | P/P | a concrete Closure's captures | P/P | P/P | P/P | yes |
| c17 | `item(rc/Item)` |  | P/R | a capture refutes | P/R | P/R | P/R | yes |
| c18 | `obj/Base` |  | P/R | base-object view (g1) | P/R | P/R | P/R | yes |
| c19 | `obj/dyn Shape` |  | P/R | runtime-Contract view (g2) | P/R | P/R | P/R | yes |
| c20 | `obj/Item` |  | P/P | concrete payload | P/P | P/P | P/P | yes |
| c21 | `obj/Holder` |  | P/R | the concrete payload is visited | P/R | P/R | P/R | yes |
| c22 | `Array<i32>` |  | P/P | the collections' exemption (g5) | P/P | P/P | P/P | yes |
| c23 | `Array<rc/Item>` |  | P/R | an element refutes (g8) | P/R | P/R | P/R | yes |
| c24 | `Dictionary<i32, rc/Item>` |  | P/R | a value Type refutes | P/R | P/R | P/R | yes |
| c25 | `Slice<i32>{static}` |  | P/P | exempt; a static source | P/P | P/P | P/P | yes |
| c26 | `Slice<i32>{local}` |  | U/P | borrows of caller storage qualify | U/P | U/P | U/P | yes |
| c27 | `ArrayIterator<i32>` |  | P/P | a standard iterator and its internal storage | P/P | P/P | P/P | yes |
| c28 | `Sender<i32>` |  | P/R | Async.Sender's internal state is not exempt | P/R | P/R | P/R | yes |
| c29 | `[0 of rc/Item]` |  | P/R | a zero-length array keeps its component | P/R | P/R | P/R | yes |
| c30 | `Phantom<rc/Item>` |  | P/R | an unused argument (15.2.3) | P/R | P/R | P/R | yes |
| c31 | `Phantom<ref/i32 during local>` |  | U/P | an unused argument's Origin | U/P | U/P | U/P | yes |
| c32 | `Inner<rc/Item>` |  | P/R | an inherited argument (6.1.3) | P/R | P/R | P/R | yes |
| c33 | `Msg` |  | P/R | an inactive Case payload | P/R | P/R | P/R | yes |
| c34 | `Hook` |  | P/R | a cycle hides nothing (g10) | P/R | P/R | P/R | yes |
| c35 | `ChainS` |  | P/P | a cycle ends (g11) | P/P | P/P | P/P | yes |
| c36 | `Claim` |  | P/R | 8.7.1: `Self is Transferable` is an error at the clause; structure decides | P/R | P/R | P/R | yes |
| c37 | `Broken` |  | E/E | an invalid Field | E/E | E/E | E/E | yes |
| c38 | `UsesBroken<i32>` |  | E/E | Error absorbs (8.3.2) | E/E | E/E | E/E | yes |
| c39 | `FixedBuffer` |  | R/R | verified raw storage (Owned leaf); raw | R/R | R/R | R/R | yes |
| c40 | `Box<FixedBuffer>` |  | R/R | through a Type argument | R/R | R/R | R/R | yes |
| c41 | `Node<i32>{static}` |  | P/R | SPEC 15.3.5's Node: recursion adds nothing; rc | P/R | P/R | P/R | yes |
| c42 | `Node<i32>{local}` |  | U/R | an unproven Origin argument | U/R | U/R | U/R | yes |
| u1 | `U` |  | U/U | abstract Types need premises (g4) | U/U | U/U | U/U | yes |
| u2 | `U` | `U is Transferable` | U/P | exact assumption (g9) | U/P | U/P | U/P | yes |
| u3 | `U` | `U is Transferable and Owned` | P/P |  | P/P | P/P | P/P | yes |
| u4 | `U` | `U is not Transferable` | U/R | an available not P refutes | U/R | U/R | U/R | yes |
| u5 | `U` | `U is my.Transferable` | U/U | a user Contract named Transferable gains nothing (8.7.1) | U/U | U/U | U/U | yes |
| u6 | `U` | `U is Portable` | U/P | Contract refinement (contract Portable: Transferable) | U/P | U/P | U/P | yes |
| r1 | `Nest<i32>` |  | P/P | the Transferable plan's Nest: non-regular, Proven | P/P | P/P | P/P | yes |
| r2 | `Nest<rc/Item>` |  | P/R |  | P/R | P/R | P/R | yes |
| r3 | `Nest15<i32>` |  | P/R | SPEC 15.3.5's own field rc/Nest<(T, T)>? | P/R | P/R | P/R | yes |
| r4 | `Linked<i32>` |  | P/R | rc refutes for every T | P/R | P/R | P/R | yes |
| r5 | `Chain<i32>` |  | P/P | G174: SPEC 15.3.5 gives Proven | P/P | P/P | P/P | yes |
| r6 | `Chain<rc/Item>` |  | P/R |  | P/R | P/R | P/R | yes |
| r7 | `NodeFix<i32>` |  | P/P | IntrinsicCapabilityTest's fixed instance | P/P | P/P | P/P | yes |
| r8 | `Box<Box<i32>>` |  | P/P | finite nesting | P/P | P/P | P/P | yes |
| r9 | `Ping<i32>` |  | P/P | mutual non-regular recursion | P/P | P/P | P/P | yes |
| r10 | `Ping<rc/Item>` |  | P/R |  | P/R | P/R | P/R | yes |
| r11 | `D1<i32>` |  | P/R | rc six declarations deep | P/R | P/R | P/R | yes |
| r12 | `Grow<Item>` |  | P/R | Grow<Opn<Item>> stores obj/Opn<Item>, a base-object view | P/R | P/R | P/R | yes |
| l1 | `HoldObj<Base>` |  | P/R | A-1: obj/Base | P/R | P/R | P/R | yes |
| l2 | `HoldObj<Item>` |  | P/P |  | P/P | P/P | P/P | yes |
| l3 | `HoldRef<Base>{static}` |  | P/R | A-1: objref/Base | P/R | P/R | P/R | yes |
| l4 | `HoldRef<Item>{static}` |  | P/P |  | P/P | P/P | P/P | yes |
| l5 | `HoldUniq<Base>{static}` |  | P/R | A-1: objuniq/Base | P/R | P/R | P/R | yes |
| l6 | `HoldUniq<Item>{static}` |  | P/P |  | P/P | P/P | P/P | yes |
| l7 | `HoldArc<Base>` |  | P/R | B-1: arc/T at an open base | P/R | P/R | P/R | yes |
| l8 | `HoldArc<Item>` |  | P/P |  | P/P | P/P | P/P | yes |
| l9 | `HoldP<obj/dyn Shape>` |  | P/R | arc/T at a runtime Contract (the argument refutes too) | P/R | P/R | P/R | yes |
| l10 | `HoldP<obj/Item>` |  | P/P |  | P/P | P/P | P/P | yes |
| l11 | `HoldP<obj/Base>` |  | P/R |  | P/R | P/R | P/R | yes |
| l12 | `arc/Base` |  | P/R | B-1: arc/OpenBase | P/R | P/R | P/R | yes |
| l13 | `arc/dyn Shape` |  | P/R | B-1: arc over a runtime Contract | P/R | P/R | P/R | yes |
| l14 | `SemHold<ref/Item during static>{static}` |  | P/P | s/Base with s = ref | P/P | P/P | P/P | yes |
| l15 | `SemHold<obj/Item>{static}` |  | P/R | B-1: s/U under a formal binder, s = obj | P/R | P/R | P/R | yes |
| l16 | `Fwd<i32>{static}` |  | P/P | SemAny<i32>: owner/Base | P/P | P/P | P/L (P/L) | yes |
| l17 | `Fwd<obj/Item>{static}` |  | P/R | SemAny<obj/Item> stores obj/Base: a pair binder bound to a plain formal | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| l18 | `HoldObj<U>` | `U is ObjectPayload and Transferable and Owned` | P/U | P-2: obj/U without a Sealed premise | P/U | P/U | P/U | yes |
| l19 | `HoldObj<U>` | `U is ObjectPayload and Transferable and Owned and Sealed` | P/P | P-2: with one | P/P | P/P | P/P | yes |
| l20 | `Option<s/U during static>` | `s is ref|uniq; U is Transferable and Owned` | P/P | every admitted case proves | P/P | P/P | P/P | yes |
| l21 | `Option<s/U during static>` | `s is ref|obj; U is Transferable and Owned and ObjectPayload` | P/U | ref proves, obj is unknown (U not Sealed) | P/P **UNSOUND** | P/U | P/U | yes |
| l22 | `Option<s/U during static>` | `s is rc|raw; U is Owned` | P/R | every admitted case refutes | P/R | P/R | P/R | yes |
| l23 | `Fwd<U>{static}` | `U is Transferable and Owned` | P/U | outer(U) admits all nine | P/P **UNSOUND** | P/U | P/L (P/L) | yes |
| l25 | `Peel<ref/Item during static>{static}` |  | P/P | Peel<Item> is owner/Base | P/P | P/P | P/L (P/L) | yes |
| l26 | `Peel<ref/(obj/Item) during static>{static}` |  | P/R | the nested Peel<obj/Item> stores obj/Base | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| l27 | `Peel<U>{static}` | `U is Transferable and Owned` | P/U | every layer of U applied to Base | P/U | P/U | P/L (P/L) | yes |
| l24 | `obj/U` | `U is ObjectPayload and Owned; U is not Transferable` | P/R | the View Target is a component whatever its Sealed proof | P/U **U for R** | P/R | P/R | yes |
| p1 | `PHold<U>` | `U is Has and Transferable and Owned` | U/U | U.(Has).Out unproven | U/U | U/U | U/U | yes |
| p2 | `PHold<U>` | `U is THas and Transferable and Owned` | P/P | THas supplies Out's proofs | P/P | P/P | P/P | yes |
| p3 | `PHold<U>` | `U is Has and Transferable and Owned; U.(Has).Out is Transferable and Owned` | P/P | a premise on the projection | P/P | P/P | P/P | yes |
| p4 | `PHold<Item>` |  | P/R | Item: Has binds Out = rc/Item | P/R | P/R | P/R | yes |
| p5 | `PHold<Cfg>` |  | P/P | Cfg: Has binds Out = i32 | P/P | P/P | P/P | yes |
| p6 | `Fold<U>` | `U is THas and Transferable and Owned` | P/P | Fold<Pair2<T>>: the conditional conformance maps Out back to T.Out | P/P | P/P | P/P | yes |
| p7 | `Fold<Cfg>` |  | P/P |  | P/P | P/P | P/P | yes |
| p8 | `Fold<Item>` |  | P/R |  | P/R | P/R | P/R | yes |
| p9 | `Two<U>` | `U is Seq and Transferable and Owned; U.(Seq).Elem is Transferable and Owned; U.(Seq).Next.(Seq).Elem is Transferable and Owned` | P/P | two direct projections, no cycle | P/P | P/P | P/P | yes |
| f1 | `TreeNode<U>` | `U is TTree` | P/P | U.Childᵏ: TTree refines Transferable and Owned and supplies Child is TTree | P/P | P/P | L/L (L/L) | yes |
| f2 | `TreeNode<U>` | `U is Tree and Transferable and Owned` | U/U | U.Child unproven | U/U | U/U | L/L (L/L) | yes |
| f3 | `TreeNode<Leaf>` |  | E/E | Leaf: Tree is invalid (8.3.3), so the Type is invalid | E/E | E/E | E/E | yes |
| f4 | `TreeNode<Wrap<i32>>` |  | E/E | concrete growth: Wrap<X>: Tree would need itself (8.3.3) | E/E | E/E | E/E | yes |
| f5 | `Walk<U>` | `U is TSeq and Transferable and Owned` | P/P | U.Nextᵏ and U.Nextᵏ.Elem all proven | NT/NT **NONTERM** | P/P | L/L (L/L) | yes |
| f6 | `Walk<U>` | `U is Seq and Transferable and Owned` | U/U | U.Elem unproven | NT/NT **NONTERM** | U/U | L/L (L/L) | yes |
| f7 | `Even<U>` | `U is TTree` | P/P | a two-declaration cycle | P/P | P/P | L/L (L/L) | yes |
| f8 | `Even<U>` | `U is Tree and Transferable and Owned` | U/U |  | U/U | U/U | L/L (L/L) | yes |
| f9 | `LFam<U>` | `U is STree` | P/P | obj/U.Childᵏ: Sealed and Transferable at every k | P/P | P/P | L/L (L/L) | yes |
| f10 | `LFam<U>` | `U is OTree and TTree` | P/U | U.Child's Sealed proof is unknown | P/U | P/U | L/L (L/L) | yes |
| f11 | `LFamBad<U>` | `U is STree` | E/E | 8.7.3: obj/T.(Tree).Child needs an Object Target; Tree supplies no ObjectPayload | E/E | E/E | E/E | yes |
| k1 | `Fwd2<Item>{static}` |  | P/R | SemAny<rc/Item>: a pair binder bound to a projection, s = rc | P/R | P/R | P/L (P/L) | yes |
| k2 | `Fwd2<Cfg>{static}` |  | P/P | SemAny<i32>: owner/Base | P/P | P/P | P/L (P/L) | yes |
| k3 | `Fwd2<U>{static}` | `U is THas and Transferable and Owned` | P/U | outer(U.Out) unknown | P/P **UNSOUND** | P/U | P/L (P/L) | yes |
| k4 | `PA<ref/(ref/(obj/Item) during static) during static>{static}` |  | P/R | a peel through PB: PA<obj/Item> stores obj/Base | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| k5 | `PA<ref/(ref/Item during static) during static>{static}` |  | P/P | every level owner or ref | P/P | P/P | P/L (P/L) | yes |
| k6 | `Peel3<ref/(obj/Item) during static>{static}` |  | P/R | Peel3<obj/Item> stores obj/OBox<Item>, OBox open | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| k7 | `Peel3<ref/(ref/i32 during static) during static>{static}` |  | P/P | every level ref or owner over a sealed i32 | P/P | P/P | P/L (P/L) | yes |
| k8 | `Peel3<U>{static}` | `U is Transferable and Owned` | P/U | every layer of U applied to OBox<...> | P/U | P/U | P/L (P/L) | yes |
| j1 | `Q0<ref/(ref/(ref/(ref/(ref/(obj/Item) during static) during static) during static) during static) during static>{static}` |  | P/R | five peels per round trip: Q0 at target^5 is obj/Item | P/P **UNSOUND** | NT/NT **NONTERM** | P/L (P/L) | yes |
| j2 | `Q0<ref/(ref/(ref/(ref/(Item) during static) during static) during static) during static>{static}` |  | P/P | five peels, owner at target^5 | P/P | NT/NT **NONTERM** | P/L (P/L) | yes |
| j3 | `Q0<U>{static}` | `U is Transferable and Owned` | P/U |  | P/U | NT/NT **NONTERM** | P/L (P/L) | yes |
| j4 | `Q0<ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(obj/Item) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static>{static}` |  | P/R | Q0 at target^30 is obj/Item (R1-1) | P/P **UNSOUND** | NT/NT **NONTERM** | P/L (P/L) | yes |
| d1 | `Peel<ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(ref/(obj/Item) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static) during static>{static}` |  | P/R | Peel at depth 70: obj/Base (R1-2) | P/P **UNSOUND** | P/P **UNSOUND** | P/L (P/L) | yes |
| x1 | `IX<ref/Item during static, ref/(ref/(ref/(obj/Item) during static) during static) during static>{static}` |  | P/R | swapped peels: s at level 3 is obj | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| x2 | `IX<ref/Item during static, ref/(ref/(ref/(Item) during static) during static) during static>{static}` |  | P/P |  | P/P | P/P | P/L (P/L) | yes |
| x3 | `IX<U, V>{static}` | `U is Transferable and Owned; V is Transferable and Owned` | P/U |  | P/U | P/U | P/L (P/L) | yes |
| b1 | `RB<obj/Item>{static}` |  | P/R | obj/OBox<RB<Item>>: OBox open | P/R | P/R | P/L (P/L) | yes |
| b2 | `RB<ref/(ref/(i32) during static) during static>{static}` |  | P/P | self-reference through Array | P/P | P/P | P/L (P/L) | yes |
| b3 | `RB<ref/(obj/Item) during static>{static}` |  | P/R | level 1 is obj | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| s1 | `PS<ref/(obj/Item) during static>{static}` |  | P/R | level 0's SemAny<obj/Item> stores obj/Base | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| s2 | `PS<ref/(ref/(obj/Item) during static) during static>{static}` |  | P/R | a walk product at level 1 | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| s3 | `PS<ref/(ref/(ref/(obj/Item) during static) during static) during static>{static}` |  | P/R | a walk product at level 2 | P/P **UNSOUND** | P/R | P/L (P/L) | yes |
| s4 | `PS<ref/(ref/(ref/(Item) during static) during static) during static>{static}` |  | P/P |  | P/P | P/P | P/L (P/L) | yes |
| s5 | `PS<U>{static}` | `U is Transferable and Owned` | P/U |  | P/U | P/U | P/L (P/L) | yes |
| s6 | `PS<U>{static}` | `U is Transferable and Owned and ObjectPayload` | P/P | U owner: every level owner/Base | P/P | P/P | P/L (P/L) | yes |
| g1 | `G2<ref/Item during static, i32>{static}` |  | P/P | ref/OBox<Box^k<i32>> at every level | P/P | P/P | P/P | yes |
| g2 | `G2<obj/Item, i32>{static}` |  | P/R | obj/OBox<i32>: OBox open | P/R | P/R | P/R | yes |

Decided: Transferable answers the located U on 39 cases (the pair cases and the families listed by hand), Owned equals SPEC on 126 of 134 (the others: F5 (a)'s families), every other verdict equals SPEC; use-site re-entries 0; peel templates 0; located U raised at Even, Fwd, Fwd2, IX, LFam, LFamBad, Odd, PB, Peel, Peel3, PS, Q4, RB, TreeNode, Walk.

## Generic statics (§3.5)

The SPEC column is the confined read's verdict for the instance read. The model's own judgment of the instance is in parentheses.

| Read | Container | Static's Type | Body premises | Instance | SPEC (instance's verdict) | Decided | BodyAnchor | TwoValued | Package |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| a1 | `Shared` | `i32` |  |  | valid (P) | P → valid | P → valid | P → valid | P → valid |
| a2 | `Shared` | `rc/Item` |  |  | error (R) | R → error | R → error | R → error | R → error |
| a3 | `Shared` | `obj/Base` |  |  | error (R) | R → error | R → error | R → error | R → error |
| a4 | `Shared` | `obj/dyn Shape` |  |  | error (R) | R → error | R → error | R → error | R → error |
| a5 | `Shared` | `fn` |  |  | error (R) | R → error | R → error | R → error | R → error |
| a6 | `Shared` | `Array<i32>` |  |  | valid (P) | P → valid | P → valid | P → valid | P → valid |
| a7 | `Shared` | `Array<rc/Item>` |  |  | error (R) | R → error | R → error | R → error | R → error |
| a8 | `Shared` | `Hook` |  |  | error (R) | R → error | R → error | R → error | R → error |
| a9 | `Shared` | `ChainS` |  |  | valid (P) | P → valid | P → valid | P → valid | P → valid |
| a10 | `Gen` | `T` |  | `Gen<i32>` | valid (P) | U → U | U → U | U → U | U → U |
| a11 | `Gen` | `T` |  | `Gen<rc/Item>` | error (R) | U → U | U → U | U → U | U → U |
| a12 | `Cache` | `Option<T>` | T is Transferable | `Cache<rc/Item>` | error (R) | U → U | P → valid **UNSOUND** | U → U | U → U |
| a13 | `Cache` | `Option<T>` | T is Transferable | `Cache<T>` | valid (P) | U → U | P → valid | U → U | U → U |
| a14 | `Cache` | `Option<T>` |  | `Cache<i32>` | valid (P) | U → U | U → U | U → U | U → U |
| a15 | `TCache` | `Option<T>` |  | `TCache<i32>` | valid (P) | P → valid | P → valid | P → valid | P → valid |
| a16 | `Reg` | `Option<obj/T>` |  | `Reg<Item>` | valid (P) | U → U | U → U | R → error **OVERSTRICT** | U → U |
| a17 | `Reg` | `Option<obj/T>` |  | `Reg<Base>` | error (R) | U → U | U → U | R → error | U → U |
| a18 | `SReg` | `Option<obj/T>` |  | `SReg<Item>` | valid (P) | P → valid | P → valid | P → valid | P → valid |
| a19 | `PG` | `Option<s/T during static>` |  | `PG<obj/Item>` | valid (P) | P → valid | P → valid | P → valid | P → valid |
| a20 | `PGr` | `Option<s/T during static>` |  | `PGr<rc/Item>` | error (R) | U → U | U → U | U → U | U → U |
| a21 | `PGr` | `Option<s/T during static>` |  | `PGr<ref/Item during static>` | valid (P) | U → U | U → U | U → U | U → U |

## Conformances

| Conformance | SPEC | model |
| --- | --- | --- |
| `Leaf: Tree` | rejected: 8.3.3: proving Leaf is Tree needs Leaf is Tree | rejected |
| `Wrap<X>: Tree when X is Tree` | rejected: Child = Wrap<Wrap<X>> needs the conformance itself (concrete growth) | rejected |
| `Item: Has` | accepted | accepted |
| `Cfg: Has` | accepted | accepted |
| `Pair2<X>: Has when X is Has` | accepted | accepted |
| `Holder: Portable` | rejected: Portable: Transferable; Holder stores rc/Item | rejected |
| `Item: Portable` | accepted | accepted |
| `struct Claim` with `Self is Transferable` | rejected at the clause (8.7.1) | the summary ignores the clause; c36 is Refuted |

## Termination

"Not converged" now includes every declaration that reads a non-converged one, which is the review's reporting fix (R1-1 (2)).

| Rules | rounds | not converged | located U: families | located U: pair sites | max obligations | templates |
| --- | --- | --- | --- | --- | --- | --- |
| Decided | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| Option1 | 24 | Q0, Q1, Q2, Q3, Q4 | — | — | 6 | 9 |
| Package | 24 | Walk | — | — | 25 | 0 |
| PairBinderOnly | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | — | 2 | 0 |
| NoCore | 24 | G2 | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 24 | 0 |
| A | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | — | 2 | 0 |
| B | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| TwoValued | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| NoneRefutes | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| TargetIfSealed | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| FamilyPrefix | 24 | Walk | — | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 25 | 0 |
| FamilyQs | 7 | — | — | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| NoInternal | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| Bounded8 | 8 | Even, LFam, LFamBad, Odd, TreeNode, Walk | — | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 16 | 0 |
| Guard | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| Cap4U | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| Cap4P | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |
| BodyAnchor | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | Fwd, Peel, Fwd2, PB, Peel3, Q4, IX, RB, PS | 2 | 0 |

Decided's summaries with obligations or a located-U site:

- Grow: ExclT yes, ExclO no, Error no, Obl {Layer(obj, T)}
- HoldObj: ExclT no, ExclO no, Error no, Obl {Layer(obj, T)}
- HoldRef: ExclT no, ExclO no, Error no, Obl {Layer(objref, T)}
- HoldUniq: ExclT no, ExclO no, Error no, Obl {Layer(objuniq, T)}
- HoldArc: ExclT no, ExclO no, Error no, Obl {Layer(arc, T)}
- HoldP: ExclT no, ExclO no, Error no, Obl {Layer(arc, T)}
- SemHold: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}
- SemAny: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}
- Fwd: ExclT no, ExclO no, Error no, Obl {}, pair U {Fwd}
- Peel: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}, pair U {Peel}
- Fwd2: ExclT no, ExclO no, Error no, Obl {T(T.(Has).Out)}, pair U {Fwd2}
- PA: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}, pair U {PB}
- PB: ExclT no, ExclO no, Error no, Obl {}, pair U {PB}
- Peel3: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}, pair U {Peel3}
- Q0: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}, pair U {Q4}
- Q1: ExclT no, ExclO no, Error no, Obl {}, pair U {Q4}
- Q2: ExclT no, ExclO no, Error no, Obl {}, pair U {Q4}
- Q3: ExclT no, ExclO no, Error no, Obl {}, pair U {Q4}
- Q4: ExclT no, ExclO no, Error no, Obl {}, pair U {Q4}
- IX: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}, pair U {IX}
- RB: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}, pair U {RB}
- PS: ExclT no, ExclO no, Error no, Obl {Layer(s, core:sealed)}, pair U {PS}
- G2: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}
- PHold: ExclT no, ExclO no, Error no, Obl {T(T.(Has).Out)}
- TreeNode: ExclT no, ExclO no, Error no, Obl {T(T.(Tree).Child)}, family U {TreeNode}
- LFam: ExclT no, ExclO no, Error no, Obl {Layer(obj, T.(Tree).Child), T(T.(Tree).Child)}, family U {LFam}
- LFamBad: ExclT no, ExclO no, Error yes, Obl {Layer(obj, T.(Tree).Child), T(T.(Tree).Child)}, family U {LFamBad}
- Walk: ExclT no, ExclO no, Error no, Obl {T(T.(Seq).Elem), T(T.(Seq).Next)}, family U {Walk}
- Even: ExclT no, ExclO no, Error no, Obl {T(T.(Tree).Child)}, family U {Even, Odd}
- Odd: ExclT no, ExclO no, Error no, Obl {T(T.(Tree).Child)}, family U {Even, Odd}
- Fold: ExclT no, ExclO no, Error no, Obl {T(T.(Has).Out)}
- Two: ExclT no, ExclO no, Error no, Obl {T(T.(Seq).Elem), T(T.(Seq).Next.(Seq).Elem)}
- Exempt declarations (the KimiLibraryShapeTest condition: their own storage reaches no rc, Function or view leaf): Array ExclT no, Dictionary ExclT no, Slice ExclT no, IterCursor ExclT no, ArrayIterator ExclT no
- Package's Walk at its cap: 25 obligations, still growing; the longest is T(T.(Seq).Next^23{.(Seq).Elem}*).

**Random graphs.**

- **Shape.** Three quarters of the graphs have 2–4 declarations. The other quarter is a ring of 5–8 pair declarations, each passing
  its pair target on whole.
  - Pair binders apply to Base, to Types built from the pair target, and to other formals.
  - Half of the non-ring pair declarations peel.
  - A fifth of the query arguments carry 2–80 ref layers.
- **The reference** is deepened from depth 9 to 100 when cut.
- **Decided's located U is not a disagreement.** Its rule is the decided one. The table counts what the reference says at those
  queries.

| Seed | Graphs (rings) | Compared | Inconclusive | Decided disagreements | Transferable's located U (reference P / R) | Most rounds | Not converged | Option 1 not converged |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 20261021 | 2,000 (496) | 27,824 | 5,376 | 0 | 1,959 (92 / 1,867) | 13 | 0 | 96 graphs |
| 20261021 | 10,000 (2,494) | 139,960 | 27,486 | 0 | 9,555 (377 / 9,178) | 14 | 0 | 475 graphs |
| 7 | 10,000 (2,503) | 139,638 | 27,228 | 0 | 9,550 (415 / 9,135) | 13 | 0 | 453 graphs |

- **Owned** equals the reference in every compared query (it is part of "disagreements 0").
- **Decided's summaries** form no peel template, and no use-site judgment re-enters itself.
- **Package** is UNSOUND in 1–3 queries per 10,000 graphs.

## Variants

- **Option1** (option 1, the X-2 candidate: outer(X)/target(X), the core reduction, peel templates (step j ≤ 4 and 64 instances at use: R1-1, R1-2 open), the use-site rule, Q*.s families): l16 P/P (SPEC); l17 P/R (SPEC); l23 P/U (SPEC); l25 P/P (SPEC); l26 P/R (SPEC); l27 P/U (SPEC); f1 P/P (SPEC); f2 U/U (SPEC); f5 P/P (SPEC); f6 U/U (SPEC); f7 P/P (SPEC); f8 U/U (SPEC); f9 P/P (SPEC); f10 P/U (SPEC); k1 P/R (SPEC); k2 P/P (SPEC); k3 P/U (SPEC); k4 P/R (SPEC); k5 P/P (SPEC); k6 P/R (SPEC); k7 P/P (SPEC); k8 P/U (SPEC); j1 NT/NT (NONTERM); j2 NT/NT (NONTERM); j3 NT/NT (NONTERM); j4 NT/NT (NONTERM); d1 P/P (UNSOUND); x1 P/R (SPEC); x2 P/P (SPEC); x3 P/U (SPEC); b1 P/R (SPEC); b2 P/P (SPEC); b3 P/R (SPEC); s1 P/R (SPEC); s2 P/R (SPEC); s3 P/R (SPEC); s4 P/P (SPEC); s5 P/U (SPEC); s6 P/P (SPEC)
- **Package** (§3.2 as written (sharing the model's core reduction and coverage): no outer(...), "Proven when none does", the target only under a proven Sealed, Tᵢ.p.qᵏ): l16 P/P (SPEC); l17 P/P (UNSOUND); l21 P/P (UNSOUND); l23 P/P (UNSOUND); l25 P/P (SPEC); l26 P/P (UNSOUND); l27 P/U (SPEC); l24 P/U (U for R); f1 P/P (SPEC); f2 U/U (SPEC); f5 NT/NT (NONTERM); f6 NT/NT (NONTERM); f7 P/P (SPEC); f8 U/U (SPEC); f9 P/P (SPEC); f10 P/U (SPEC); k1 P/R (SPEC); k2 P/P (SPEC); k3 P/P (UNSOUND); k4 P/P (UNSOUND); k5 P/P (SPEC); k6 P/P (UNSOUND); k7 P/P (SPEC); k8 P/U (SPEC); j1 P/P (UNSOUND); j2 P/P (SPEC); j3 P/U (SPEC); j4 P/P (UNSOUND); d1 P/P (UNSOUND); x1 P/P (UNSOUND); x2 P/P (SPEC); x3 P/U (SPEC); b1 P/R (SPEC); b2 P/P (SPEC); b3 P/P (UNSOUND); s1 P/P (UNSOUND); s2 P/P (UNSOUND); s3 P/P (UNSOUND); s4 P/P (SPEC); s5 P/U (SPEC); s6 P/P (SPEC)
- **PairBinderOnly** (option 2 without its U: an obligation that would name outer(X) or target(X) is dropped): l16 P/P (SPEC); l17 P/P (UNSOUND); l23 P/P (UNSOUND); l25 P/P (SPEC); l26 P/P (UNSOUND); l27 P/U (SPEC); k1 P/R (SPEC); k2 P/P (SPEC); k3 P/P (UNSOUND); k4 P/P (UNSOUND); k5 P/P (SPEC); k6 P/P (UNSOUND); k7 P/P (SPEC); k8 P/U (SPEC); j1 P/P (UNSOUND); j2 P/P (SPEC); j3 P/U (SPEC); j4 P/P (UNSOUND); d1 P/P (UNSOUND); x1 P/P (UNSOUND); x2 P/P (SPEC); x3 P/U (SPEC); b1 P/R (SPEC); b2 P/P (SPEC); b3 P/P (UNSOUND); s1 P/P (UNSOUND); s2 P/P (UNSOUND); s3 P/P (UNSOUND); s4 P/P (SPEC); s5 P/U (SPEC); s6 P/P (SPEC)
- **NoCore** (option 2 without the core reduction): g1 NT/NT (NONTERM); g2 NT/NT (NONTERM)
- **A** (design A's summary: a formal adds nothing, no layer obligation): r12 P/P (UNSOUND); l1 P/P (UNSOUND); l3 P/P (UNSOUND); l5 P/P (UNSOUND); l7 P/P (UNSOUND); l15 P/P (UNSOUND); l16 P/P (SPEC); l17 P/P (UNSOUND); l18 P/P (UNSOUND); l23 P/P (UNSOUND); l25 P/P (SPEC); l26 P/P (UNSOUND); l27 P/P (UNSOUND); k1 P/R (SPEC); k2 P/P (SPEC); k3 P/P (UNSOUND); k4 P/P (UNSOUND); k5 P/P (SPEC); k6 P/P (UNSOUND); k7 P/P (SPEC); k8 P/P (UNSOUND); j1 P/P (UNSOUND); j2 P/P (SPEC); j3 P/P (UNSOUND); j4 P/P (UNSOUND); d1 P/P (UNSOUND); x1 P/P (UNSOUND); x2 P/P (SPEC); x3 P/P (UNSOUND); b1 P/P (UNSOUND); b2 P/P (SPEC); b3 P/P (UNSOUND); s1 P/P (UNSOUND); s2 P/P (UNSOUND); s3 P/P (UNSOUND); s4 P/P (SPEC); s5 P/P (UNSOUND); s6 P/P (SPEC); g2 P/P (UNSOUND)
- **B** (design B's ViewTarget (obj, objref, objuniq; two-valued) and Layer(s) (rc, raw); arc continues): l7 P/P (UNSOUND); l12 P/P (UNSOUND); l15 P/P (UNSOUND); l18 P/R (R for U); g2 P/P (UNSOUND)
- **TwoValued** (the pre-P-2 wording: an unproven Sealed refutes): l18 P/R (R for U)
- **NoneRefutes** (§3.2's "Proven when none does" for an admitted set): l21 P/P (UNSOUND)
- **TargetIfSealed** (§3.2's object layer: the target only when Sealed is Proven): l24 P/U (U for R)
- **FamilyPrefix** (§3.2's family (Tᵢ, p, q*) = Tᵢ.p.qᵏ with B's inductive rule): f1 P/P (SPEC); f2 U/U (SPEC); f5 NT/NT (NONTERM); f6 NT/NT (NONTERM); f7 P/P (SPEC); f8 U/U (SPEC); f9 P/P (SPEC); f10 P/U (SPEC)
- **FamilyQs** (F5 (b): Tᵢ.Q*.s families with the knowledge-state judgment): f1 P/P (SPEC); f2 U/U (SPEC); f5 P/P (SPEC); f6 U/U (SPEC); f7 P/P (SPEC); f8 U/U (SPEC); f9 P/P (SPEC); f10 P/U (SPEC)
- **NoInternal** (the exemption without the iterators' internal storage declarations): c27 P/R (over-strict)
- **Bounded8** (oracle for families: obligations enumerated for 8 rounds, each judged by 8.3.2): f1 P/P (SPEC); f2 U/U (SPEC); f5 P/P (SPEC); f6 U/U (SPEC); f7 P/P (SPEC); f8 U/U (SPEC); f9 P/P (SPEC); f10 P/U (SPEC)
- **Guard** (instantiation walk with today's growth guard (Binding.Capabilities.cs:228-245)): r1 U/U (U for P); r2 U/R (U for P); r3 U/R (U for P); r5 U/U (U for P); r6 U/R (U for P); r9 U/U (U for P); r10 U/R (U for P); r12 U/R (U for P); l16 P/P (SPEC); l17 P/R (SPEC); l23 P/U (SPEC); l25 P/P (SPEC); l26 P/R (SPEC); l27 U/U (U for P); p6 U/U (U for P); p7 U/U (U for P); p8 U/R (U for P); f1 U/U (U for P); f2 U/U (SPEC); f5 U/U (U for P); f6 U/U (SPEC); f7 U/U (U for P); f8 U/U (SPEC); f9 U/U (U for P); f10 U/U (U for P); k1 P/R (SPEC); k2 P/P (SPEC); k3 P/U (SPEC); k4 P/R (SPEC); k5 P/P (SPEC); k6 P/R (SPEC); k7 P/P (SPEC); k8 U/U (U for P); j1 P/R (SPEC); j2 P/P (SPEC); j3 U/U (U for P); j4 P/R (SPEC); d1 P/R (SPEC); x1 P/R (SPEC); x2 P/P (SPEC); x3 U/U (U for P); b1 P/R (SPEC); b2 P/P (SPEC); b3 P/R (SPEC); s1 P/R (SPEC); s2 P/R (SPEC); s3 P/R (SPEC); s4 P/P (SPEC); s5 U/U (U for P); s6 U/U (U for P); g1 U/U (U for P); g2 U/R (U for P)
- **Cap4U** (instantiation walk, depth cap 4 answering U): r1 U/U (U for P); r2 U/R (U for P); r3 U/R (U for P); r5 U/U (U for P); r6 U/R (U for P); r9 U/U (U for P); r10 U/R (U for P); r11 U/U (U for P, U for R); r12 U/R (U for P); l16 P/P (SPEC); l17 P/R (SPEC); l23 P/U (SPEC); l25 P/P (SPEC); l26 U/R (U for P); l27 U/U (U for P); p6 U/U (U for P); p7 U/U (U for P); p8 U/R (U for P); f1 U/U (U for P); f2 U/U (SPEC); f5 U/U (U for P); f6 U/U (SPEC); f7 U/U (U for P); f8 U/U (SPEC); f9 U/U (U for P); f10 U/U (U for P); k1 P/R (SPEC); k2 P/P (SPEC); k3 P/U (SPEC); k4 U/U (U for P, U for R); k5 U/U (U for P); k6 U/R (U for P); k7 U/U (U for P); k8 U/U (U for P); j1 U/U (U for P, U for R); j2 U/U (U for P); j3 U/U (U for P); j4 U/U (U for P, U for R); d1 U/U (U for P, U for R); x1 U/U (U for P, U for R); x2 U/U (U for P); x3 U/U (U for P); b1 U/R (U for P); b2 U/U (U for P); b3 U/U (U for P, U for R); s1 U/R (U for P); s2 U/R (U for P); s3 U/U (U for P, U for R); s4 U/U (U for P); s5 U/U (U for P); s6 U/U (U for P); g1 U/U (U for P); g2 U/R (U for P)
- **Cap4P** (instantiation walk, depth cap 4 answering P): r11 P/P (UNSOUND); l16 P/P (SPEC); l17 P/R (SPEC); l23 P/U (SPEC); l25 P/P (SPEC); l26 P/R (SPEC); l27 P/U (SPEC); f1 P/P (SPEC); f2 U/U (SPEC); f5 P/P (SPEC); f6 U/U (SPEC); f7 P/P (SPEC); f8 U/U (SPEC); f9 P/P (SPEC); f10 P/U (SPEC); k1 P/R (SPEC); k2 P/P (SPEC); k3 P/U (SPEC); k4 P/P (UNSOUND); k5 P/P (SPEC); k6 P/R (SPEC); k7 P/P (SPEC); k8 P/U (SPEC); j1 P/P (UNSOUND); j2 P/P (SPEC); j3 P/U (SPEC); j4 P/P (UNSOUND); d1 P/P (UNSOUND); x1 P/P (UNSOUND); x2 P/P (SPEC); x3 P/U (SPEC); b1 P/R (SPEC); b2 P/P (SPEC); b3 P/P (UNSOUND); s1 P/R (SPEC); s2 P/R (SPEC); s3 P/P (UNSOUND); s4 P/P (SPEC); s5 P/U (SPEC); s6 P/P (SPEC)
- **BodyAnchor** (anchor: the reading body's premises (the Transferable plan §5.3)): as Decided on every case

## Oracle

- **Shared code (N-1).** The random reference shares the leaf rules, the substitution and the declarations' Error bit with the
  model. It checks the summary and its fixed point against instantiation; only the hand cases check the leaf rules.
- **Depth cut (N-2).** A walk cut at depth 9 is deepened to 100. If it is still cut (unless it found Error), or exceeds 4,000 nodes
  or an instance of 2,000 terms, it is inconclusive: about 16% of the queries. These are mostly non-regular instances, which are
  infinite.
- **Coverage (N-3).**
  - Random graphs have no projections and only closed queries. The projection families, F3 and abstract uses rest on the hand cases
    and Bounded8.
  - Rings of 5–8 and arguments of up to 80 layers now exceed every constant in option 1's rules, which is the window the review
    named.

## Findings for 2b-4a and 2b-4 (under option 2)

### Contract (R4-M89 and the owner's decision)

| Item | Status | Record |
| --- | --- | --- |
| Option 2 (F1, F2): the pair check and its demand-driven located U for Transferable | Owner, 2026-10-21 | DEC with 2b-4a |
| Option 1: `outer(X)`/`target(X)`, peel templates, use-site rule | Owner: X-2 future candidate, with R1-1 and R1-2 open | X-2 register |
| F3 (admitted sets: all P / all R / else U) | Within R4-M89: a correction of the package's wording | DEC with 2b-4 |
| F4 (an object layer always requires its target) | Within R4-M89 (15.2.4 with 15.2.3) | DEC with 2b-4 |
| F5 (a) (projection families: a located U on read) | Within R4-M89: the package's named fallback | DEC with 2b-4a |
| F6 (Error under rc; through nominal components) | Within R4-M89: a clarification | DEC with 2b-4a |
| The core reduction | **For the planning session to confirm** (below) | DEC with 2b-4a, or a question |
| F7, F8, F9 | Confirmations (F9 is in the approved package) | — |

### What 2b-4a implements

| Concern | Rule |
| --- | --- |
| Projection families (F5 (a)) | The projection-link graph: (D, i) → (E, j) when D's storage names E with argument j = formal i extended by projection steps. A cycle with a projection step marks its declarations as F5 (a) sites, for Owned and Transferable |
| Pair parameters (option 2) | When a nominal component E⟨B⟩ instantiates E's pair parameter with D's own plain formal or projection, or with D's pair target passed whole (a peel), an E obligation that would map onto `outer(X)` or `target(X)` is not recorded. D records itself as a site for Transferable, and for Owned only if a projection obligation is dropped |
| Peels | No separate detection. A peel can only grow obligations through `outer` or `target`, so the pair check catches every peel cycle: the cases Peel, PA/PB, Peel3, Q0–Q4, IX, RB and PS, and about 3,300 random graphs per 10,000 with a pair site, all converged |
| Propagation | The site set and the family set propagate through nominal components, like the exclusion bits |
| Where the U is raised | In the judgment, when it first reads a summary with a site: a located `Unsupported_Kd` at the site declaration, recorded once per declaration. Its answer is Error (Derived in A4), never a Language error. A declaration no judgment reads raises nothing, so programs that compile today keep compiling |
| The core reduction | `Layer(s, X)` stores `core(X)` for an X whose outer core is known |
| Owned | Reads ExclO, Proj, Error and the instance's Origins, never a layer obligation, so option 2 leaves Owned exact. G174's fix holds (r5 Proven) |

The cost of option 2 (R1-3 (b), corrected):

- Only Transferable judgments of such Types give the located U, concrete instances included. In the cases these are l16
  `Fwd<i32>`, k2, k5, k7, j2, x2, b2, s4 and s6, where SPEC proves, and the refuted or unknown ones.
- Owned stays exact.
- No current verdict changes: Transferable is new with 2b-4 (today `UnresolvedBinding_Kd`), and no struct or enum in the Kimi library
  or the milestones declares a pair parameter (grep of `src/Kimi/Library`, `tests/milestones`).

### What 2b-4 implements

The leaves with F3 and F4, the exempt table with the iterators' internal storage (F9), the anchor of §3.5 (F8), and A4's mapping of
the located U (Derived). A confined read of a static whose Type reads a site gives the located U, never a Language error.

### The core reduction (needed under option 2; for the planning session)

- **The problem.** §3.2 records `Layer(s, X)` for a pair binder s with X arbitrary. When X is a composite whose arguments grow
  non-regularly, the obligation grows without end, and the case needs no `outer` and no peel: g1 and g2,
  `G2<s/T, U> = s/OBox<U>, Option<obj/G2<s/T, Box<U>>>`. NoCore does not converge on G2.
- **The fix is exact.** `Layer(s, X) = Layer(s, core(X)) ∧ Transferable(X)`. The layer rule reads X only through its Sealed proof
  (the outer core alone, 8.7.2) and its Transferable verdict, which the walk already requires as a component.
- **It changes no verdict.** g1 and g2 meet SPEC, and it is in force on every case.
- **Classification.** It was listed under option 1 in revision 1, but it is independent of `outer`, `target` and peels. I read it as
  a correction within R4-M89 that restores §3.2's finiteness. The alternative is a located U for a pair binder over a growing
  composite. The planning session should confirm which.

### F3–F9 (as in revision 1)

- **F3.** "Proven when none does" is unsound (l21). The rule is: Proven when every admitted Semantics proves; Refuted when every one
  refutes; otherwise Unknown, with Error absorbing.
- **F4.** An object layer is the Sealed condition ∧ Transferable(target). As written, l24 gives U for SPEC's R; not unsound.
- **F5 (a).**
  - The package's pattern (Tᵢ, p, q*) = "Tᵢ.p.qᵏ" does not terminate on `Walk<T>`, and the correct shape is Tᵢ.Q*.s.
  - 2b-4a takes A's fallback: the located U on read, recorded once per declaration.
  - FamilyQs shows the Q*.s alternative meeting SPEC on f1–f10. It is rejected for the same evidence reason as option 1 (below).
- **F6.** The walk continues under `rc` and `raw`. Error, which now includes 8.7.3's ill-formedness, propagates through nominal
  components.
- **F7.** Owned shares the summary, and no guard or cap is needed. Guard gives Unknown Owned on 24 cases; Cap4P is UNSOUND on r11,
  k4, j1, j4, d1, x1, b3 and s3.
- **F8.** The container anchor holds. BodyAnchor accepts a12, and TwoValued rejects a16.
- **F9.** The exemption needs the iterators' internal storage (NoInternal: c27).

### Option 1: the X-2 candidate

- **What it adds.**
  - `outer(X)` and `target(X)` as obligation subjects.
  - Peel templates `∀m. O[$h ↦ target^(j·m)(Y)]`, detected as a shift of a held obligation on a growing path, with pointwise
    coverage, and no walk of an instance at m ≥ 1.
  - The use-site self-reference rule.
- **What it would give.** SPEC's exact verdict where option 2 answers U: the Option1 column on l16–l27, k1–k8, x1–x3, b1–b3 and s1–s6.
- **Open against it (the review's round 2):**
  - **R1-1.** Detection only tries steps j ≤ 4.
    - A ring of five peels forms no template. With the reporting fix, j1–j4 are NONTERM, and option 1 does not converge on about
      460 of 10,000 random graphs (the rings).
    - With revision 1's reporting, j4 was UNSOUND (P/P for P/R), which is the review's finding.
    - The fix the review names: derive j from the hole depths, and propagate non-convergence to every reader.
  - **R1-2.** Templates are judged at most 64 instances deep at use: d1 (70 layers) is UNSOUND (P/P for P/R). The fix: iterate until
    the hole stops changing.
- **Evidence standard (R1-3 (a)).**
  - Template detection is a search over variables and shifts. Its completeness rests on this prototype alone, which is the evidence
    class for which F5 (b)'s Q*.s families were set aside.
  - Both stay future candidates under one standard: a written argument for detection's completeness, beyond prototype runs.
- **When to adopt.** When a program needs exact Transferable for such Types, after R1-1 and R1-2.

## Not modelled

- **Neighbouring analyses.** Copy and the census of `RequestCapability`'s other growers; G170's premise supply; identity premises;
  A4's per-view consumption beyond the anchor table.
- **Origins and environments.** Origin relations beyond "= static"; associated Types' Origin parameters; inherited environments,
  which are modelled as extra formals.
- **Conformances.** Verification covers associated-Type requirements and intrinsic parents only.
- **Object Targets.** The check covers only the declaration's own storage. Opt-outs are not modelled, and a Tuple counts as an
  Object Target, as in the engine's leaf.
- **Option 2's Owned site** (a projection obligation over a pair target that is dropped) is implemented, but no valid case or random
  graph produced one (H).
- **Random graphs.** No projections, and only closed queries.
- **Other.** Diagnostic categories, costs and allocations.
