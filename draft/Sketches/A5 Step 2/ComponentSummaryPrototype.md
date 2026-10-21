# 2x-P12: component-summary prototype (Owned and Transferable)

Draft record of unit 2x-P12 (package 2b-D §3 and §5, DECISIONS R4-M89; PLAN G124, G174). Base: `origin/dev` `4eb19789`. No
compiler, SPEC, test or tracked-prototype change; no verify run. "(H)" marks a reading that no run checked. Working record, not
authority: SPEC and DECISIONS decide.

Revision 1 (2026-10-21) answers the independent review (`p12-review.md`, not yet ACCEPT):

- **P12-1.** F2's peel family is now a template. Three more growth shapes found by the random graphs are closed.
- **P12-2.** The LFam cases are corrected, and the model checks SPEC 8.7.3's Object Target.
- **Oracle.** It is tightened as the review asks.
- **F5 (a).** The located U is demand-driven.
- **Owner question.** A contract section and one owner proposal are added.

Run in an empty folder outside the repository (.NET SDK 11.0.100-rc.2):

- `dotnet run ComponentSummaryPrototype.cs` runs 2,000 random graphs in about 6 s.
- `-- -random 10000 [-- -seed S]` runs more graphs.
- Exit code: Corrected's failures, 0 on every run recorded here.

## Model

- **Types.** A small term language:
  - primitives; Type formals and abstract Types (`var`);
  - projections with segments, where a starred segment is a family;
  - layers whose Semantics is concrete, a pair binder, or `outer(X)` (the outer Semantics of a plain formal or projection X);
  - nominal Types with Type and Origin arguments; tuples; fixed arrays; `fn`; `item(...)` (bound arguments or captures); `dyn C`;
  - `target(X)`, the pair target of a plain formal X;
  - `core:sealed`, `core:open` and `core:error`, the outer core of a Type in a layer obligation.
- **Program.** 66 declarations:
  - the Kimi prelude: Option, Array, Dictionary, Slice, the iterator storage, Weak, Sender, FixedBuffer, Box and an open OBox;
  - the case declarations;
  - 9 Contracts and 7 conformances. A conformance is verified without itself (SPEC 8.3.3), and an intrinsic parent is decided by the
    judgment (8.7.1).
- **Summary (§3.2).** `(ExclT, ExclO, Error, Obl)` comes from one walk of a declaration's storage in its formals. It is the least
  fixed point over all declarations, from (no, no, no, ∅), seeded with the previous round.
  - A nominal component adds its bits, walks its arguments and maps its `Obl` through [formals ↦ arguments].
  - The walk of the declaration's own storage checks that every object form has an Object Target (8.7.3), including a pair binder
    applied to another Type. A failure sets Error.
- **Growth and families.** `Obl` grows only around a cycle of the projection-link graph that adds a projection step, or that passes
  a pair target back as a whole argument (a peel).
  - Corrected keeps a projection family as Tᵢ.Q*.s.
  - It keeps a peel family as the template `∀m. O[$h ↦ target^(j·m)(Y)]`, where j is the number of peels per round trip (F2).
- **Use.** `Owned(D<A,o>) = ¬ExclO ∧ o = static ∧ Owned(A) ∧ Proj[A]` and `Transferable(D<A>) = ¬ExclT ∧ Transferable(A) ∧ Obl[A]`,
  with Error absorbing.
  - Abstract Types are judged by the Contract-knowledge closure of the premises: exact assumption, refinement, and associated-Type
    requirements (8.3.2).
  - A use-site judgment that meets itself again contributes no exclusion (F2).
- **Anchor (§3.5).** One verdict per static under its container's premises; A4's table maps it to an outcome.
- **Oracles.**
  - Every case's SPEC verdict is written by hand.
  - Bounded8 enumerates the family obligations for 8 rounds and judges each by 8.3.2.
  - On random graphs, the reference is an instantiation BFS over SPEC's components (15.2.3); see "Oracle" below.

## Rules under test

- **Package**: §3.2–§3.5 as written.
  - `Layer(S, X)` only for a pair binder S (it has no `outer(...)`).
  - An admitted set is "Proven when none [refutes]".
  - An object layer requires its target only when Sealed is Proven.
  - Families are `(Tᵢ, p, q*)` = Tᵢ.p.qᵏ, formed when a new path extends a held one, and judged by B's inductive rule.
  - Package also shares the model's representation fixes (the core reduction, target coverage and peel templates), so its column
    isolates the package's wording; PairBinderOnly isolates the missing `outer(...)`.
- **Corrected**: the package with the findings' fixes. Every case meets SPEC under it.
- **Variants**: each changes one rule of Corrected.
  - A and B are the two designs' original rules.
  - TwoValued is the pre-P-2 wording.
  - NoneRefutes, PairBinderOnly, TargetIfSealed and FamilyPrefix are each one piece of Package.
  - FamilyU is A's fallback: a projection family answers Error, and the located U is raised when a judgment reads it.
  - NoInternal drops the iterators' internal storage from the exemption.
  - Guard is an instantiation walk with today's growth guard (`Binding.Capabilities.cs:228–245`).
  - Cap4U and Cap4P are instantiation walks with a depth cap answering U or P.
  - BodyAnchor anchors at the reading body's premises.

## Cases

Owned/Transferable verdicts. The brief's cases are:

- the Transferable plan's §7.2 matrix (c1–c42, u1–u6);
- SPEC 15.3.5's examples, `Nest`, `Linked` and G174's `Chain` (c41, c42, r1–r12);
- A-1 (l1–l6), B-1 (l7, l12, l13, l15) and P-2 (l18, l19);
- projections, families with and without the inductive premise, the two-declaration cycle (f7, f8), an invalid Field (c37, c38)
  and the concrete-growth negative check (f4).

Added by the modelling:

- a pair binder bound to a plain formal (l16, l17, l23);
- admitted sets (l20–l22) and an object layer over a refuted abstract target (l24);
- peeling (l25–l27);
- a conditional-conformance projection (p6–p8) and two direct projections without a cycle (p9);
- a Layer family (f9, f10).

Added in revision 1:

- **P12-2.** LFam now requires `T is OTree`, where `contract OTree: Tree, ObjectPayload` supplies `Tree.Child is OTree`, and STree
  refines OTree. f10's premise is OTree and TTree, without Sealed. f11 (`LFamBad`, the old `T is Tree` form) is E/E by 8.7.3.
- **The review's k1–k8.**
  - Its k9 (`s/(T, Base)`) is left out, as the review decided. It terminates here with a constant-core template.
  - Its verdict hinges on whether a Tuple is an Object Target: 8.7.2 lists Tuples as Sealed, and the engine's ObjectPayload leaf
    accepts one. That is outside this round.

| Case | Query | Premises | SPEC | why | Package | Corrected | meets |
| --- | --- | --- | --- | --- | --- | --- | --- |
| c1 | `i32` |  | P/P | primitive | P/P | P/P | yes |
| c2 | `string` |  | P/P | string is a primitive | P/P | P/P | yes |
| c3 | `rc/Item` |  | P/R | 15.2.4: rc refutes | P/R | P/R | yes |
| c4 | `ref/(rc/Item) during static` |  | P/R | rc behind ref | P/R | P/R | yes |
| c5 | `ref/i32 during static` |  | P/P | valid neighbour of c4 | P/P | P/P | yes |
| c6 | `ref/i32 during local` |  | U/P | 15.2.3: an unproven Origin; 15.2.4 requires no Owned | U/P | U/P | yes |
| c7 | `arc/Holder` |  | P/R | rc behind arc (Holder stores rc/Item) | P/R | P/R | yes |
| c8 | `arc/Item` |  | P/P | Sealed payload | P/P | P/P | yes |
| c9 | `Weak<rc/Item>` |  | P/R | Weak of rc | P/R | P/R | yes |
| c10 | `ref/(Weak<rc/Item>) during static` |  | P/R | Weak of rc behind ref | P/R | P/R | yes |
| c11 | `Weak<arc/Item>` |  | P/P | Weak counts as storing arc/Item; only Weak of rc is listed | P/P | P/P | yes |
| c12 | `raw/i32` |  | P/R | a raw pointer outside the exemption; Owned visits the pointee | P/R | P/R | yes |
| c13 | `arc/RawHolder` |  | P/R | raw behind arc | P/R | P/R | yes |
| c14 | `fn` |  | P/R | common Function Type (g3) | P/R | P/R | yes |
| c15 | `item()` |  | P/P | a Function Item stores nothing | P/P | P/P | yes |
| c16 | `item(i32)` |  | P/P | a concrete Closure's captures | P/P | P/P | yes |
| c17 | `item(rc/Item)` |  | P/R | a capture refutes | P/R | P/R | yes |
| c18 | `obj/Base` |  | P/R | base-object view (g1) | P/R | P/R | yes |
| c19 | `obj/dyn Shape` |  | P/R | runtime-Contract view (g2) | P/R | P/R | yes |
| c20 | `obj/Item` |  | P/P | concrete payload | P/P | P/P | yes |
| c21 | `obj/Holder` |  | P/R | the concrete payload is visited | P/R | P/R | yes |
| c22 | `Array<i32>` |  | P/P | the collections' exemption (g5) | P/P | P/P | yes |
| c23 | `Array<rc/Item>` |  | P/R | an element refutes (g8) | P/R | P/R | yes |
| c24 | `Dictionary<i32, rc/Item>` |  | P/R | a value Type refutes | P/R | P/R | yes |
| c25 | `Slice<i32>{static}` |  | P/P | exempt; a static source | P/P | P/P | yes |
| c26 | `Slice<i32>{local}` |  | U/P | borrows of caller storage qualify | U/P | U/P | yes |
| c27 | `ArrayIterator<i32>` |  | P/P | a standard iterator and its internal storage | P/P | P/P | yes |
| c28 | `Sender<i32>` |  | P/R | Async.Sender's internal state is not exempt | P/R | P/R | yes |
| c29 | `[0 of rc/Item]` |  | P/R | a zero-length array keeps its component | P/R | P/R | yes |
| c30 | `Phantom<rc/Item>` |  | P/R | an unused argument (15.2.3) | P/R | P/R | yes |
| c31 | `Phantom<ref/i32 during local>` |  | U/P | an unused argument's Origin | U/P | U/P | yes |
| c32 | `Inner<rc/Item>` |  | P/R | an inherited argument (6.1.3) | P/R | P/R | yes |
| c33 | `Msg` |  | P/R | an inactive Case payload | P/R | P/R | yes |
| c34 | `Hook` |  | P/R | a cycle hides nothing (g10) | P/R | P/R | yes |
| c35 | `ChainS` |  | P/P | a cycle ends (g11) | P/P | P/P | yes |
| c36 | `Claim` |  | P/R | 8.7.1: `Self is Transferable` is an error at the clause; structure decides | P/R | P/R | yes |
| c37 | `Broken` |  | E/E | an invalid Field | E/E | E/E | yes |
| c38 | `UsesBroken<i32>` |  | E/E | Error absorbs (8.3.2) | E/E | E/E | yes |
| c39 | `FixedBuffer` |  | R/R | verified raw storage (Owned leaf); raw | R/R | R/R | yes |
| c40 | `Box<FixedBuffer>` |  | R/R | through a Type argument | R/R | R/R | yes |
| c41 | `Node<i32>{static}` |  | P/R | SPEC 15.3.5's Node: recursion adds nothing; rc | P/R | P/R | yes |
| c42 | `Node<i32>{local}` |  | U/R | an unproven Origin argument | U/R | U/R | yes |
| u1 | `U` |  | U/U | abstract Types need premises (g4) | U/U | U/U | yes |
| u2 | `U` | `U is Transferable` | U/P | exact assumption (g9) | U/P | U/P | yes |
| u3 | `U` | `U is Transferable and Owned` | P/P |  | P/P | P/P | yes |
| u4 | `U` | `U is not Transferable` | U/R | an available not P refutes | U/R | U/R | yes |
| u5 | `U` | `U is my.Transferable` | U/U | a user Contract named Transferable gains nothing (8.7.1) | U/U | U/U | yes |
| u6 | `U` | `U is Portable` | U/P | Contract refinement (contract Portable: Transferable) | U/P | U/P | yes |
| r1 | `Nest<i32>` |  | P/P | the Transferable plan's Nest: non-regular, Proven | P/P | P/P | yes |
| r2 | `Nest<rc/Item>` |  | P/R |  | P/R | P/R | yes |
| r3 | `Nest15<i32>` |  | P/R | SPEC 15.3.5's own field rc/Nest<(T, T)>? | P/R | P/R | yes |
| r4 | `Linked<i32>` |  | P/R | rc refutes for every T | P/R | P/R | yes |
| r5 | `Chain<i32>` |  | P/P | G174: SPEC 15.3.5 gives Proven | P/P | P/P | yes |
| r6 | `Chain<rc/Item>` |  | P/R |  | P/R | P/R | yes |
| r7 | `NodeFix<i32>` |  | P/P | IntrinsicCapabilityTest's fixed instance | P/P | P/P | yes |
| r8 | `Box<Box<i32>>` |  | P/P | finite nesting | P/P | P/P | yes |
| r9 | `Ping<i32>` |  | P/P | mutual non-regular recursion | P/P | P/P | yes |
| r10 | `Ping<rc/Item>` |  | P/R |  | P/R | P/R | yes |
| r11 | `D1<i32>` |  | P/R | rc six declarations deep | P/R | P/R | yes |
| r12 | `Grow<Item>` |  | P/R | Grow<Opn<Item>> stores obj/Opn<Item>, a base-object view | P/R | P/R | yes |
| l1 | `HoldObj<Base>` |  | P/R | A-1: obj/Base | P/R | P/R | yes |
| l2 | `HoldObj<Item>` |  | P/P |  | P/P | P/P | yes |
| l3 | `HoldRef<Base>{static}` |  | P/R | A-1: objref/Base | P/R | P/R | yes |
| l4 | `HoldRef<Item>{static}` |  | P/P |  | P/P | P/P | yes |
| l5 | `HoldUniq<Base>{static}` |  | P/R | A-1: objuniq/Base | P/R | P/R | yes |
| l6 | `HoldUniq<Item>{static}` |  | P/P |  | P/P | P/P | yes |
| l7 | `HoldArc<Base>` |  | P/R | B-1: arc/T at an open base | P/R | P/R | yes |
| l8 | `HoldArc<Item>` |  | P/P |  | P/P | P/P | yes |
| l9 | `HoldP<obj/dyn Shape>` |  | P/R | arc/T at a runtime Contract (the argument refutes too) | P/R | P/R | yes |
| l10 | `HoldP<obj/Item>` |  | P/P |  | P/P | P/P | yes |
| l11 | `HoldP<obj/Base>` |  | P/R |  | P/R | P/R | yes |
| l12 | `arc/Base` |  | P/R | B-1: arc/OpenBase | P/R | P/R | yes |
| l13 | `arc/dyn Shape` |  | P/R | B-1: arc over a runtime Contract | P/R | P/R | yes |
| l14 | `SemHold<ref/Item during static>{static}` |  | P/P | s/Base with s = ref | P/P | P/P | yes |
| l15 | `SemHold<obj/Item>{static}` |  | P/R | B-1: s/U under a formal binder, s = obj | P/R | P/R | yes |
| l16 | `Fwd<i32>{static}` |  | P/P | SemAny<i32>: owner/Base | P/P | P/P | yes |
| l17 | `Fwd<obj/Item>{static}` |  | P/R | SemAny<obj/Item> stores obj/Base: a pair binder bound to a plain formal | P/P **UNSOUND** | P/R | yes |
| l18 | `HoldObj<U>` | `U is ObjectPayload and Transferable and Owned` | P/U | P-2: obj/U without a Sealed premise | P/U | P/U | yes |
| l19 | `HoldObj<U>` | `U is ObjectPayload and Transferable and Owned and Sealed` | P/P | P-2: with one | P/P | P/P | yes |
| l20 | `Option<s/U during static>` | `s is ref|uniq; U is Transferable and Owned` | P/P | every admitted case proves | P/P | P/P | yes |
| l21 | `Option<s/U during static>` | `s is ref|obj; U is Transferable and Owned and ObjectPayload` | P/U | ref proves, obj is unknown (U not Sealed) | P/P **UNSOUND** | P/U | yes |
| l22 | `Option<s/U during static>` | `s is rc|raw; U is Owned` | P/R | every admitted case refutes | P/R | P/R | yes |
| l23 | `Fwd<U>{static}` | `U is Transferable and Owned` | P/U | outer(U) admits all nine | P/P **UNSOUND** | P/U | yes |
| l25 | `Peel<ref/Item during static>{static}` |  | P/P | Peel<Item> is owner/Base | P/P | P/P | yes |
| l26 | `Peel<ref/(obj/Item) during static>{static}` |  | P/R | the nested Peel<obj/Item> stores obj/Base | P/P **UNSOUND** | P/R | yes |
| l27 | `Peel<U>{static}` | `U is Transferable and Owned` | P/U | every layer of U applied to Base | P/U | P/U | yes |
| l24 | `obj/U` | `U is ObjectPayload and Owned; U is not Transferable` | P/R | the View Target is a component whatever its Sealed proof | P/U **U for R** | P/R | yes |
| p1 | `PHold<U>` | `U is Has and Transferable and Owned` | U/U | U.(Has).Out unproven | U/U | U/U | yes |
| p2 | `PHold<U>` | `U is THas and Transferable and Owned` | P/P | THas supplies Out's proofs | P/P | P/P | yes |
| p3 | `PHold<U>` | `U is Has and Transferable and Owned; U.(Has).Out is Transferable and Owned` | P/P | a premise on the projection | P/P | P/P | yes |
| p4 | `PHold<Item>` |  | P/R | Item: Has binds Out = rc/Item | P/R | P/R | yes |
| p5 | `PHold<Cfg>` |  | P/P | Cfg: Has binds Out = i32 | P/P | P/P | yes |
| p6 | `Fold<U>` | `U is THas and Transferable and Owned` | P/P | Fold<Pair2<T>>: the conditional conformance maps Out back to T.Out | P/P | P/P | yes |
| p7 | `Fold<Cfg>` |  | P/P |  | P/P | P/P | yes |
| p8 | `Fold<Item>` |  | P/R |  | P/R | P/R | yes |
| p9 | `Two<U>` | `U is Seq and Transferable and Owned; U.(Seq).Elem is Transferable and Owned; U.(Seq).Next.(Seq).Elem is Transferable and Owned` | P/P | two direct projections, no cycle | P/P | P/P | yes |
| f1 | `TreeNode<U>` | `U is TTree` | P/P | U.Childᵏ: TTree refines Transferable and Owned and supplies Child is TTree | P/P | P/P | yes |
| f2 | `TreeNode<U>` | `U is Tree and Transferable and Owned` | U/U | U.Child unproven | U/U | U/U | yes |
| f3 | `TreeNode<Leaf>` |  | E/E | Leaf: Tree is invalid (8.3.3), so the Type is invalid | E/E | E/E | yes |
| f4 | `TreeNode<Wrap<i32>>` |  | E/E | concrete growth: Wrap<X>: Tree would need itself (8.3.3) | E/E | E/E | yes |
| f5 | `Walk<U>` | `U is TSeq and Transferable and Owned` | P/P | U.Nextᵏ and U.Nextᵏ.Elem all proven | NT/NT **NONTERM** | P/P | yes |
| f6 | `Walk<U>` | `U is Seq and Transferable and Owned` | U/U | U.Elem unproven | NT/NT **NONTERM** | U/U | yes |
| f7 | `Even<U>` | `U is TTree` | P/P | a two-declaration cycle | P/P | P/P | yes |
| f8 | `Even<U>` | `U is Tree and Transferable and Owned` | U/U |  | U/U | U/U | yes |
| f9 | `LFam<U>` | `U is STree` | P/P | obj/U.Childᵏ: Sealed and Transferable at every k | P/P | P/P | yes |
| f10 | `LFam<U>` | `U is OTree and TTree` | P/U | U.Child's Sealed proof is unknown | P/U | P/U | yes |
| f11 | `LFamBad<U>` | `U is STree` | E/E | 8.7.3: obj/T.(Tree).Child needs an Object Target; Tree supplies no ObjectPayload | E/E | E/E | yes |
| k1 | `Fwd2<Item>{static}` |  | P/R | SemAny<rc/Item>: a pair binder bound to a projection, s = rc | P/R | P/R | yes |
| k2 | `Fwd2<Cfg>{static}` |  | P/P | SemAny<i32>: owner/Base | P/P | P/P | yes |
| k3 | `Fwd2<U>{static}` | `U is THas and Transferable and Owned` | P/U | outer(U.Out) unknown | P/P **UNSOUND** | P/U | yes |
| k4 | `PA<ref/(ref/(obj/Item) during static) during static>{static}` |  | P/R | a peel through PB: PA<obj/Item> stores obj/Base | P/P **UNSOUND** | P/R | yes |
| k5 | `PA<ref/(ref/Item during static) during static>{static}` |  | P/P | every level owner or ref | P/P | P/P | yes |
| k6 | `Peel3<ref/(obj/Item) during static>{static}` |  | P/R | Peel3<obj/Item> stores obj/OBox<Item>, OBox open | P/P **UNSOUND** | P/R | yes |
| k7 | `Peel3<ref/(ref/i32 during static) during static>{static}` |  | P/P | every level ref or owner over a sealed i32 | P/P | P/P | yes |
| k8 | `Peel3<U>{static}` | `U is Transferable and Owned` | P/U | every layer of U applied to OBox<...> | P/U | P/U | yes |

## Generic statics (§3.5)

The SPEC column is the confined read's verdict for the instance read. The model's own judgment of the instance is in parentheses,
as a consistency check.

| Read | Container | Static's Type | Body premises | Instance | SPEC (instance's verdict) | Corrected | BodyAnchor | TwoValued | Package |
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

Rounds until no summary changes (cap 24; Bounded8 stops at 8 by design):

| Rules | rounds | not converged | family declarations (FamilyU) | max obligations |
| --- | --- | --- | --- | --- |
| Corrected | 7 | — | — | 2 |
| Package | 24 | Walk | — | 25 |
| A | 7 | — | — | 2 |
| B | 7 | — | — | 2 |
| TwoValued | 7 | — | — | 2 |
| NoneRefutes | 7 | — | — | 2 |
| PairBinderOnly | 7 | — | — | 2 |
| TargetIfSealed | 7 | — | — | 2 |
| FamilyPrefix | 24 | Walk | — | 25 |
| FamilyU | 7 | — | Even, LFam, LFamBad, Odd, TreeNode, Walk | 2 |
| NoInternal | 7 | — | — | 2 |
| Bounded8 | 8 | Even, LFam, LFamBad, Odd, PA, PB, Peel, Peel3, TreeNode, Walk | — | 16 |
| Guard | 7 | — | — | 2 |
| Cap4U | 7 | — | — | 2 |
| Cap4P | 7 | — | — | 2 |
| BodyAnchor | 7 | — | — | 2 |

Corrected's summaries with obligations:

- Grow: ExclT yes, ExclO no, Error no, Obl {Layer(obj, T)}
- HoldObj: ExclT no, ExclO no, Error no, Obl {Layer(obj, T)}
- HoldRef: ExclT no, ExclO no, Error no, Obl {Layer(objref, T)}
- HoldUniq: ExclT no, ExclO no, Error no, Obl {Layer(objuniq, T)}
- HoldArc: ExclT no, ExclO no, Error no, Obl {Layer(arc, T)}
- HoldP: ExclT no, ExclO no, Error no, Obl {Layer(arc, T)}
- SemHold: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}
- SemAny: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open)}
- Fwd: ExclT no, ExclO no, Error no, Obl {Layer(outer(T), core:open)}
- Peel: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open), ∀m. Layer(outer($h), core:open) with $h = target^m(T)}
- Fwd2: ExclT no, ExclO no, Error no, Obl {T(T.(Has).Out), Layer(outer(T.(Has).Out), core:open)}
- PA: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open), ∀m. Layer(outer($h), core:open) with $h = target^m(T)}
- PB: ExclT no, ExclO no, Error no, Obl {∀m. Layer(outer($h), core:open) with $h = target^m(U)}
- Peel3: ExclT no, ExclO no, Error no, Obl {Layer(s, core:open), ∀m. Layer(outer($h), core:open) with $h = target^m(T)}
- PHold: ExclT no, ExclO no, Error no, Obl {T(T.(Has).Out)}
- TreeNode: ExclT no, ExclO no, Error no, Obl {T(T{.(Tree).Child}*.(Tree).Child)}
- LFam: ExclT no, ExclO no, Error no, Obl {T(T{.(Tree).Child}*.(Tree).Child), Layer(obj, T{.(Tree).Child}*.(Tree).Child)}
- LFamBad: ExclT no, ExclO no, Error yes, Obl {T(T{.(Tree).Child}*.(Tree).Child), Layer(obj, T{.(Tree).Child}*.(Tree).Child)}
- Walk: ExclT no, ExclO no, Error no, Obl {T(T{.(Seq).Next}*.(Seq).Elem), T(T{.(Seq).Next}*.(Seq).Next)}
- Even: ExclT no, ExclO no, Error no, Obl {T(T.(Tree).Child), T(T.(Tree).Child{.(Tree).Child}*.(Tree).Child)}
- Odd: ExclT no, ExclO no, Error no, Obl {T(T{.(Tree).Child}*.(Tree).Child)}
- Fold: ExclT no, ExclO no, Error no, Obl {T(T.(Has).Out)}
- Two: ExclT no, ExclO no, Error no, Obl {T(T.(Seq).Elem), T(T.(Seq).Next.(Seq).Elem)}
- Exempt declarations (the KimiLibraryShapeTest condition: their own storage reaches no rc, Function or view leaf): Array ExclT no, Dictionary ExclT no, Slice ExclT no, IterCursor ExclT no, ArrayIterator ExclT no
- Package's Walk at its cap: 25 obligations, still growing; the longest is T(T.(Seq).Next^23{.(Seq).Elem}*).

- **Random graphs.** 2–4 declarations with pair parameters, Origin slots, ObjectPayload premises, and open, exempt, invalid and
  Owned-leaf declarations; closed queries.
  - The pair binders apply to Base, to Types built from the pair target (Box, OBox, Array, Option and random declarations), and to
    other formals.
  - Half the pair declarations peel.

| Seed | Graphs | Compared | Inconclusive | Corrected disagreements | Most rounds | Not converged | With a peel template |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 20261021 | 2,000 | 32,088 | 4,170 | 0 | 5 | 0 | 226 |
| 20261021 | 10,000 | 161,244 | 19,644 | 0 | 6 | 0 | 1,022 |
| 7 | 10,000 | 159,882 | 20,064 | 0 | 6 | 0 | 1,053 |
| 99 | 10,000 | 160,764 | 19,728 | 0 | 6 | 0 | 970 |

On seed 20261021 with 10,000 graphs:

- Package is UNSOUND in 2 queries (F1).
- Guard gives Unknown for a Proven Owned in 444 queries, Unknown for a Proven Transferable in 16, and Unknown for a Refuted
  Transferable in 3.
- Corrected's family judgment explores Contract-knowledge states, which are finite: they are sets of Contracts.

## Variants

- **Package** (§3.2 as written: no outer(...), "Proven when none does", the target only under a proven Sealed, Tᵢ.p.qᵏ): l17 P/P (UNSOUND); l21 P/P (UNSOUND); l23 P/P (UNSOUND); l26 P/P (UNSOUND); l24 P/U (U for R); f5 NT/NT (NONTERM); f6 NT/NT (NONTERM); k3 P/P (UNSOUND); k4 P/P (UNSOUND); k6 P/P (UNSOUND)
- **A** (design A's summary: a formal adds nothing, no layer obligation): r12 P/P (UNSOUND); l1 P/P (UNSOUND); l3 P/P (UNSOUND); l5 P/P (UNSOUND); l7 P/P (UNSOUND); l15 P/P (UNSOUND); l17 P/P (UNSOUND); l18 P/P (UNSOUND); l23 P/P (UNSOUND); l26 P/P (UNSOUND); l27 P/P (UNSOUND); f10 P/P (UNSOUND); k3 P/P (UNSOUND); k4 P/P (UNSOUND); k6 P/P (UNSOUND); k8 P/P (UNSOUND)
- **B** (design B's ViewTarget (obj, objref, objuniq; two-valued) and Layer(s) (rc, raw); arc continues): l7 P/P (UNSOUND); l12 P/P (UNSOUND); l15 P/P (UNSOUND); l17 P/P (UNSOUND); l18 P/R (R for U); l23 P/P (UNSOUND); l26 P/P (UNSOUND); l27 P/P (UNSOUND); f10 P/R (R for U); k3 P/P (UNSOUND); k4 P/P (UNSOUND); k6 P/P (UNSOUND); k8 P/P (UNSOUND)
- **TwoValued** (the pre-P-2 wording: an unproven Sealed refutes): l18 P/R (R for U)
- **NoneRefutes** (§3.2's "Proven when none does" for an admitted set): l21 P/P (UNSOUND)
- **PairBinderOnly** (Layer(S, X) only for a pair binder S: drops outer(formal)): l17 P/P (UNSOUND); l23 P/P (UNSOUND); l26 P/P (UNSOUND); k3 P/P (UNSOUND); k4 P/P (UNSOUND); k6 P/P (UNSOUND)
- **TargetIfSealed** (§3.2's object layer: the target only when Sealed is Proven): l24 P/U (U for R)
- **FamilyPrefix** (§3.2's family (Tᵢ, p, q*) = Tᵢ.p.qᵏ with B's inductive rule): f5 NT/NT (NONTERM); f6 NT/NT (NONTERM)
- **FamilyU** (A's fallback: a projection family answers Error, with a located U at its declaration raised when a judgment reads it; peels stay exact): f1 E/E (E for P); f2 E/E (E for U); f5 E/E (E for P); f6 E/E (E for U); f7 E/E (E for P); f8 E/E (E for U); f9 E/E (E for P); f10 E/E (E for P, E for U). Located U raised by the judgments that read a family: Even, LFam, LFamBad, Odd, TreeNode, Walk
- **NoInternal** (the exemption without the iterators' internal storage declarations): c27 P/R (over-strict)
- **Bounded8** (oracle: obligations enumerated for 8 rounds, each judged by 8.3.2): every case meets SPEC
- **Guard** (instantiation walk with today's growth guard (Binding.Capabilities.cs:228-245)): r1 U/U (U for P); r2 U/R (U for P); r3 U/R (U for P); r5 U/U (U for P); r6 U/R (U for P); r9 U/U (U for P); r10 U/R (U for P); r12 U/R (U for P); l27 U/U (U for P); p6 U/U (U for P); p7 U/U (U for P); p8 U/R (U for P); f1 U/U (U for P); f5 U/U (U for P); f7 U/U (U for P); f9 U/U (U for P); f10 U/U (U for P); k8 U/U (U for P)
- **Cap4U** (instantiation walk, depth cap 4 answering U): r1 U/U (U for P); r2 U/R (U for P); r3 U/R (U for P); r5 U/U (U for P); r6 U/R (U for P); r9 U/U (U for P); r10 U/R (U for P); r11 U/U (U for P, U for R); r12 U/R (U for P); l26 U/R (U for P); l27 U/U (U for P); p6 U/U (U for P); p7 U/U (U for P); p8 U/R (U for P); f1 U/U (U for P); f5 U/U (U for P); f7 U/U (U for P); f9 U/U (U for P); f10 U/U (U for P); k4 U/U (U for P, U for R); k5 U/U (U for P); k6 U/R (U for P); k7 U/U (U for P); k8 U/U (U for P)
- **Cap4P** (instantiation walk, depth cap 4 answering P): r11 P/P (UNSOUND); k4 P/P (UNSOUND)
- **BodyAnchor** (anchor: the reading body's premises (the Transferable plan §5.3)): every case meets SPEC

## Oracle

- **Shared code (N-1).** The random reference shares the leaf rules (`SealedOf`, `Orig`, the abstract judgments), the
  substitution (`Bind`, `Subst`, `Resolve`) and the declarations' Error bit (invalid, or ill-formed by 8.7.3) with the model. It
  checks the summary and its fixed point against instantiation, not the leaf rules. Only the hand cases check the leaf rules.
- **Depth cut (N-2).** A reference walk cut at nominal depth 9 counts its unexplored part as "no exclusion", so it is deepened to 14.
  If it is still cut (unless it found Error), or exceeds 4,000 nodes or an instance of 2,000 terms, it counts as inconclusive. These
  are non-regular instances whose instantiation is infinite: 19,644 of 180,888 queries on seed 20261021, 5,926 of them answering P
  within depth 14.
- **Coverage (N-3).** Random graphs have no projections and only closed queries. F1 at an abstract use, F3 and the projection
  families rest on the hand cases and Bounded8.

## Findings for 2b-4a and 2b-4

### Contract (R4-M89)

| Finding | Within R4-M89's approved contract? | Record |
| --- | --- | --- |
| F1 with F2 | **No: owner.** New obligation forms and a second growth source; §3.2's finiteness sentence becomes false | The proposal below |
| F3 | Yes: a correction of the package's wording (8.3.4 decides; R4-M89 approved a three-valued layer obligation) | DEC with 2b-4 |
| F4 | Yes: 15.2.4 with 15.2.3 (the View Target is a component); no representation | DEC with 2b-4 |
| F5 (a) | Yes: the package's named fallback, raised on demand | DEC with 2b-4a |
| F6 | Yes: a clarification of "the target stays a component" and Error's failure path | DEC with 2b-4a |
| F9 | Yes: in the approved package's §3.2 | — |
| F7, F8 | Confirmations | — |

### Proposal for the owner: F1 and F2, a representation change to §3.2

**Add to the per-declaration summary:**

1. **`outer(X)` and `target(X)`.** These name the outer Semantics and the target of a plain formal or projection X, when a
   declaration instantiates a pair parameter with one of its own plain formals or projections.
2. **The core reduction.** `Layer(s, X)` becomes `Layer(s, core(X))` for an X whose outer core is known. This is exact: the layer
   rule reads X only through its Sealed proof (outer core only) and its Transferable verdict, which the walk already requires.
3. **Peel growth.** The projection-link graph also counts a pair target passed back as a whole argument.
   - On a growing cycle, the summary keeps the template `∀m. O[$h ↦ target^(j·m)(Y)]`, where j is the number of peels per round
     trip.
   - An obligation on `target(Y)` is covered by the same obligation on Y.
   - An instance at m ≥ 1 needs no walk.
   - At use, a template runs to the argument's layer depth, or two steps past the first abstract hole.
4. **Use-site self-reference.** A use-site judgment that meets itself again contributes no exclusion (the least fixed point), and
   `target(target(X))` of a context's abstract X is identified with `target(X)`.

**Why:**

- Without (1) the package accepts l17, l23, l26, k3, k4 and k6 (UNSOUND), and 1–2 random queries per 10,000 graphs.
- Without (3), peels do not terminate. The cases are Peel3 (k6–k8), a cycle with two peels per round trip, and peels mixed with
  non-regular arguments.
- (2) and (4) are what make (1) and (3) terminate.
- **Evidence:** 115 hand cases and 3 × 10,000 random graphs, with 0 disagreements and at most 7 rounds.

**Alternative: a demand-driven located `Unsupported_Kd`, like F5 (a).** It is raised when a judgment reads a summary that would
need `outer(X)` or that holds a peel cycle, and the answer is Error.

- It needs no new obligation form: only the projection-link graph, which F5 (a) needs anyway, and a check for a pair parameter
  instantiated with a plain formal.
- **Cost:** every Owned or Transferable judgment of such a Type gives a located U, concrete instances included (`Fwd<obj/Item>`,
  `Fwd<i32>`, `Peel<…>`).
- No struct or enum in the Kimi library or the milestones declares a pair parameter (grep of `src/Kimi/Library` and
  `tests/milestones`). So the cost today falls only on user generic code that instantiates a pair parameter with its own formal.

### F1. A pair binder bound to a plain formal (2b-4a; part of the proposal)

- **Cause.** §3.2 records `Layer(S, X)` when S is a pair binder. Suppose D's storage instantiates `E<s/U>` with D's plain formal T,
  or with a projection. Then s becomes T's outer Semantics and U becomes T's target, and no formal of D names either.
- **Evidence.** Dropping the obligation accepts l17 (`Fwd<obj/Item>` stores `SemAny<obj/Item>`, hence `obj/Base`), l23, k3, and
  the peel cases l26, k4 and k6. It is UNSOUND on random graphs too.
- **At use.** `outer(W)` is W's outer Semantics, which is owner for a non-layer W. An abstract X admits all nine Semantics, or only
  owner when X is proven ObjectPayload or Sealed.

### F2. Peeling and its family (2b-4a; part of the proposal; revised for P12-1)

- **Cause.** A declaration passes its pair target back as a whole argument, as in `Peel<s/T>{a} = s/Base during a,
  Option<obj/Peel<T>{a}>`. That peels one layer per level and adds obligations on `outer(target^k(T))` for every k.
- **Revision 1.** The family is the review's template. `outer*` from round 1 was its constant-X case, and it looped on `Peel3`
  (`s/OBox<T>`). The random graphs then found three more shapes, each closed by a rule of the proposal:
  - **Peels mixed with non-regular arguments** (`s/R0<T0, R2<raw/T1>>`) grew the obligation's Type. The core reduction (2) closes
    them. The proposal's peel cases now have constant-core templates; the template stays general for an abstract X.
  - **Two peels per round trip** never match a one-step shift. The template step j closes them, and it is exact: it does not
    require the layers the cycle skips.
  - **A peel reached from outside the cycle** loops through a target that names the peeling declaration. Two rules close it:
    - the shift is detected when the edge or the mapped declaration lies on a growing cycle;
    - template coverage is pointwise (a deeper hole and a covered body, or a plain obligation covered by some instance); this is
      sound because coverage is monotone through the layer rule and the admitted-set combination.
- **Use-site rule (4).** It closes a pair binder applied to a Type that names its own declaration (`R<s/T> = s/R<T>`). There the
  judgment of `Transferable(R<…>)` reads itself again.
- **Result.** k6, k7 and k8 meet SPEC (P/R, P/P, P/U). Peel, PA/PB and Peel3 converge in 7 rounds. A work budget inside one fixed-point
  step reports a runaway walk as NONTERM, and no run reaches it.

### F3. "Proven when none does" (2b-4; within R4-M89)

- **Evidence.** In l21, s is ref or obj and U is Transferable without a Sealed premise. The wording gives Proven where SPEC gives
  Unknown. Under the anchor, that accepts a confined read whose `obj/OpenBase` instance is an error.
- **Required (8.3.4).**
  - Proven when every admitted Semantics proves.
  - Refuted when every one refutes.
  - Otherwise Unknown, with Error absorbing.

### F4. An object layer always requires its target (2b-4; within R4-M89)

- **Evidence.** §3.2 says the layer is "Unknown when [the Sealed proof] is Unknown". For l24 (`obj/U` with `U is not Transferable`)
  that gives U where SPEC gives R: the View Target is an OwnedOrigins component whatever its Sealed proof (15.2.3). This is not
  unsound.
- **Required.** The layer is the Sealed condition ∧ Transferable(target).

### F5. Projection families: A's fallback, raised on demand (2b-4a; within R4-M89)

- **Cause.** The package's pattern `(Tᵢ, p, q*)` = "Tᵢ.p.qᵏ" does not terminate on `Walk<T>`. The obligations are T.Nextᵏ.Elem,
  so the correct shape is Tᵢ.Q*.s. f5 and f6 are NONTERM under Package, with 25 obligations at round 24 and still growing.
- Corrected's Q*.s families with the knowledge-state judgment meet SPEC on f1–f11 and agree with Bounded8. B's inductive rule is
  sound as a proof rule. It is the s = ε case of that exploration.
- **2b-4a takes (a), A's fallback.** A cycle of the projection-link graph with a projection step makes the declaration's answers
  Error.
  - The located `Unsupported_Kd` is raised when a judgment first reads the family, and recorded once per declaration. It is never
    raised for a declaration no judgment reads. 2b-4a changes Owned on both pipelines, so a declaration-time U would reject valid
    programs that compile today.
  - FamilyU models this: each U (Even, LFam, LFamBad, Odd, TreeNode, Walk) is raised by a case that reads it.
  - It is registered at X-2 with the SPEC gap "15.3.5's fixed point for projection families".
  - The Q*.s form would need the owner, and it waits for that SPEC session. Its detection and inclusion test are heuristics, and no
    library or milestone declaration stores a projection recursively (both designs' scans, H).

### F6. Error under rc and through nominal components (2b-4a; within R4-M89)

The walk continues into every layer's target, including under `rc` and `raw`: Owned reads the target, and Error absorbs (8.3.2). A
nominal component contributes its Error bit, and that bit now includes 8.7.3's ill-formedness, which is P12-2's model check.

### F7. Owned shares the summary; no guard or cap is needed (G174; confirmation)

- **Corrected's Owned** meets SPEC on every case, including `Chain<i32>` (r5), `Nest` (r1), `Ping` (r9) and `Fold` (p6, p7).
- **Today's guard (Guard)** gives Unknown Owned on 18 cases.
- **A depth cap is no substitute.** Cap4P accepts r11 and k4 (UNSOUND), and Cap4U answers U on 24 cases.
- 2b-4a's planned change of `ExpandingGenericRecursionRemainsUnknownWithoutUnboundedExpansion` to Proven agrees with SPEC 15.3.5.
  The census of other growers (Copy) is not modelled.

### F8. The container anchor holds (§3.5; A-2, P-2; confirmation)

- Closed statics (a1–a9) get exactly SPEC's verdict.
- A generic static whose verdict depends on the instance gets Unknown, which A4 maps to Limit, a located U. It never accepts a SPEC
  error and never gives a Language error for a valid read.
- It is Proven where the container's premises prove.
- BodyAnchor accepts a12 (A-2, UNSOUND), and TwoValued rejects a16 (a Language error for a valid read).

### F9. The exemption (2b-4; in the approved package)

The exempt table needs the declarations of the iterators' internal storage. Without them `ArrayIterator<i32>` is Refuted
(NoInternal: c27). The exempt declarations' own summaries have no ExclT.

## Not modelled

- **Neighbouring analyses.** Copy and the census of `RequestCapability`'s other growers; G170's premise supply, where the model's
  knowledge closure is exact over its own facts; identity premises (8.2.3); A4's per-view consumption beyond the anchor table.
- **Origins and environments.** Origin relations beyond "= static"; associated Types' Origin parameters; inherited environments,
  which are modelled as extra formals.
- **Conformances.** Verification covers associated-Type requirements and intrinsic parents only.
- **Object Targets.** The check covers only the declaration's own storage. Opt-outs are not modelled, and a Tuple counts as an
  Object Target, as in the engine's ObjectPayload leaf.
- **Random graphs.** No projections, so families rely on the cases and Bounded8.
- **Other.** Diagnostic categories, costs and allocations.
