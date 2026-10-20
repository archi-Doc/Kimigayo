# 2x-P12: component-summary prototype (Owned and Transferable)

Draft record of unit 2x-P12 (package 2b-D §3 and §5, DECISIONS R4-M89; PLAN G124, G174). Base: `origin/dev` `4eb19789`. No
compiler, SPEC, test or tracked-prototype change; no verify run. "(H)" marks a reading that no run checked. Working record, not
authority: SPEC and DECISIONS decide.

Run in an empty folder outside the repository (.NET SDK 11.0.100-rc.2): `dotnet run ComponentSummaryPrototype.cs` (2,000 random
graphs, about 10 s), or `-- -random 10000`. Exit code: Corrected's failures, 0 on both.

## Model

- **Types.** A small term language: primitives; Type formals and abstract Types (`var`); projections with segments (a starred
  segment is a family); layers whose Semantics is concrete, a pair binder, `outer(X)` (the outer Semantics of a plain formal or
  projection X) or `outer*(X)` (every layer of X); nominal Types with Type and Origin arguments; tuples; fixed arrays; `fn`;
  `item(...)` (bound arguments or captures); `dyn C`; `target(X)` (the pair target of a plain formal X).
- **Program.** One program of 60 declarations: the Kimi prelude (Option, Array, Dictionary, Slice, the iterator storage, Weak,
  Sender, FixedBuffer), the case declarations, 8 Contracts and 7 conformances. A conformance is verified without itself (SPEC
  8.3.3), and an intrinsic parent is decided by the judgment (8.7.1).
- **Summary (§3.2).** `(ExclT, ExclO, Error, Obl)` comes from one walk of a declaration's storage in its formals. It is the least
  fixed point over all declarations, from (no, no, no, ∅), with the previous round as seed. A nominal component adds its bits,
  walks its arguments and maps its `Obl` through [formals ↦ arguments].
- **Use.** `Owned(D<A,o>) = ¬ExclO ∧ o = static ∧ Owned(A) ∧ Proj[A]` and `Transferable(D<A>) = ¬ExclT ∧ Transferable(A) ∧ Obl[A]`,
  with Error absorbing. Abstract Types are judged by the Contract-knowledge closure of the premises: exact assumption, refinement,
  and associated-Type requirements (8.3.2).
- **Anchor (§3.5).** One verdict per static under its container's premises; A4's table maps it to an outcome.
- **Oracles.**
  - Every case's SPEC verdict is written by hand.
  - Bounded8 enumerates the family obligations for 8 rounds and judges each by 8.3.2.
  - On random graphs, the reference is an instantiation BFS over SPEC's components (15.2.3), cut at nominal depth 6 and 9. A query
    whose two answers differ is skipped.

## Rules under test

- **Package**: §3.2–§3.5 as written.
  - `Layer(S, X)` only for a pair binder S (it has no `outer(...)`).
  - An admitted set is "Proven when none [refutes]".
  - An object layer requires its target only when Sealed is Proven.
  - Families are `(Tᵢ, p, q*)` = Tᵢ.p.qᵏ, formed when a new path extends a held one, and judged by B's inductive rule.
- **Corrected**: the package with findings F1–F5's fixes. Every case meets SPEC under it.
- **Variants**: each changes one rule of Corrected.
  - A and B are the two designs' original rules.
  - TwoValued is the pre-P-2 wording.
  - NoneRefutes, PairBinderOnly, TargetIfSealed and FamilyPrefix are each one piece of Package.
  - FamilyU is A's fallback; NoInternal drops the iterators' internal storage from the exemption.
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

The modelling added these cases:

- a pair binder bound to a plain formal (l16, l17, l23);
- admitted sets (l20–l22) and an object layer over a refuted abstract target (l24);
- peeling (l25–l27);
- a conditional-conformance projection (p6–p8) and two direct projections without a cycle (p9);
- a Layer family (f9, f10).

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
| f10 | `LFam<U>` | `U is TTree` | P/U | U.Child's Sealed proof is unknown | P/U | P/U | yes |

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

| Rules | rounds | not converged | located U (FamilyU) | max obligations |
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
| FamilyU | 7 | — | Even, LFam, Odd, TreeNode, Walk | 2 |
| NoInternal | 7 | — | — | 2 |
| Bounded8 | 8 | Even, LFam, Odd, Peel, TreeNode, Walk | — | 16 |
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
- SemHold: ExclT no, ExclO no, Error no, Obl {Layer(s, Base)}
- SemAny: ExclT no, ExclO no, Error no, Obl {Layer(s, Base)}
- Fwd: ExclT no, ExclO no, Error no, Obl {Layer(outer(T), Base)}
- Peel: ExclT no, ExclO no, Error no, Obl {Layer(s, Base), Layer(outer*(T), Base)}
- PHold: ExclT no, ExclO no, Error no, Obl {T(T.(Has).Out)}
- TreeNode: ExclT no, ExclO no, Error no, Obl {T(T{.(Tree).Child}*.(Tree).Child)}
- LFam: ExclT no, ExclO no, Error no, Obl {T(T{.(Tree).Child}*.(Tree).Child), Layer(obj, T{.(Tree).Child}*.(Tree).Child)}
- Walk: ExclT no, ExclO no, Error no, Obl {T(T{.(Seq).Next}*.(Seq).Elem), T(T{.(Seq).Next}*.(Seq).Next)}
- Even: ExclT no, ExclO no, Error no, Obl {T(T.(Tree).Child), T(T.(Tree).Child{.(Tree).Child}*.(Tree).Child)}
- Odd: ExclT no, ExclO no, Error no, Obl {T(T{.(Tree).Child}*.(Tree).Child)}
- Fold: ExclT no, ExclO no, Error no, Obl {T(T.(Has).Out)}
- Two: ExclT no, ExclO no, Error no, Obl {T(T.(Seq).Elem), T(T.(Seq).Next.(Seq).Elem)}
- Exempt declarations (the KimiLibraryShapeTest condition: their own storage reaches no rc, Function or view leaf): Array ExclT no, Dictionary ExclT no, Slice ExclT no, IterCursor ExclT no, ArrayIterator ExclT no
- Package's Walk at its cap: 25 obligations, still growing; the longest is T(T.(Seq).Next^23{.(Seq).Elem}*).

- **Random graphs** (no projections; 2–4 declarations with pair parameters, Origin slots and open, exempt, invalid and Owned-leaf
  declarations; closed queries):
  - 2,000 graphs: 36,108 queries compared, 12 inconclusive, Corrected disagreements 0, at most 5 rounds.
  - 10,000 graphs: 179,754 compared, 72 inconclusive, 0 disagreements, at most 6 rounds.
- **Package on 10,000 graphs:** 8 queries are UNSOUND (F1).
- **Guard on 10,000 graphs:**
  - Unknown for a Proven Owned in 6,710 queries and for a Proven Transferable in 409;
  - Unknown for a Refuted Transferable in 46;
  - misses an Error in 6.
- **Corrected's family judgment** explores Contract-knowledge states, which are finite: they are sets of Contracts.

## Variants

- **Package** (§3.2 as written: no outer(...), "Proven when none does", the target only under a proven Sealed, Tᵢ.p.qᵏ): l17 P/P (UNSOUND); l21 P/P (UNSOUND); l23 P/P (UNSOUND); l26 P/P (UNSOUND); l24 P/U (U for R); f5 NT/NT (NONTERM); f6 NT/NT (NONTERM)
- **A** (design A's summary: a formal adds nothing, no layer obligation): r12 P/P (UNSOUND); l1 P/P (UNSOUND); l3 P/P (UNSOUND); l5 P/P (UNSOUND); l7 P/P (UNSOUND); l15 P/P (UNSOUND); l17 P/P (UNSOUND); l18 P/P (UNSOUND); l23 P/P (UNSOUND); l26 P/P (UNSOUND); l27 P/P (UNSOUND); f10 P/P (UNSOUND)
- **B** (design B's ViewTarget (obj, objref, objuniq; two-valued) and Layer(s) (rc, raw); arc continues): l7 P/P (UNSOUND); l12 P/P (UNSOUND); l15 P/P (UNSOUND); l17 P/P (UNSOUND); l18 P/R (R for U); l23 P/P (UNSOUND); l26 P/P (UNSOUND); l27 P/P (UNSOUND); f10 P/R (R for U)
- **TwoValued** (the pre-P-2 wording: an unproven Sealed refutes): l18 P/R (R for U)
- **NoneRefutes** (§3.2's "Proven when none does" for an admitted set): l21 P/P (UNSOUND)
- **PairBinderOnly** (Layer(S, X) only for a pair binder S: drops outer(formal)): l17 P/P (UNSOUND); l23 P/P (UNSOUND); l26 P/P (UNSOUND)
- **TargetIfSealed** (§3.2's object layer: the target only when Sealed is Proven): l24 P/U (U for R)
- **FamilyPrefix** (§3.2's family (Tᵢ, p, q*) = Tᵢ.p.qᵏ with B's inductive rule): f5 NT/NT (NONTERM); f6 NT/NT (NONTERM)
- **FamilyU** (A's fallback: a growing projection cycle is a located U at the declaration (answers Error); peeling stays exact): f1 E/E (E for P); f2 E/E (E for U); f5 E/E (E for P); f6 E/E (E for U); f7 E/E (E for P); f8 E/E (E for U); f9 E/E (E for P); f10 E/E (E for P, E for U)
- **NoInternal** (the exemption without the iterators' internal storage declarations): c27 P/R (over-strict)
- **Bounded8** (oracle: obligations enumerated for 8 rounds, each judged by 8.3.2): every case meets SPEC
- **Guard** (instantiation walk with today's growth guard (Binding.Capabilities.cs:228-245)): r1 U/U (U for P); r2 U/R (U for P); r3 U/R (U for P); r5 U/U (U for P); r6 U/R (U for P); r9 U/U (U for P); r10 U/R (U for P); r12 U/R (U for P); l27 U/U (U for P); p6 U/U (U for P); p7 U/U (U for P); p8 U/R (U for P); f1 U/U (U for P); f5 U/U (U for P); f7 U/U (U for P); f9 U/U (U for P); f10 U/U (U for P)
- **Cap4U** (instantiation walk, depth cap 4 answering U): r1 U/U (U for P); r2 U/R (U for P); r3 U/R (U for P); r5 U/U (U for P); r6 U/R (U for P); r9 U/U (U for P); r10 U/R (U for P); r11 U/U (U for P, U for R); r12 U/R (U for P); l26 U/R (U for P); l27 U/U (U for P); p6 U/U (U for P); p7 U/U (U for P); p8 U/R (U for P); f1 U/U (U for P); f5 U/U (U for P); f7 U/U (U for P); f9 U/U (U for P); f10 U/U (U for P)
- **Cap4P** (instantiation walk, depth cap 4 answering P): r11 P/P (UNSOUND)
- **BodyAnchor** (anchor: the reading body's premises (the Transferable plan §5.3)): every case meets SPEC

## Findings for 2b-4a and 2b-4

F1, F2 and F5 change §3.2's rules, so they return to the owner (package §5, 2x-P12's row). F3 and F4 correct §3.2's wording; F6–F9
confirm or clarify it.

### F1. A pair binder bound to a plain formal (2b-4a, unsound as written)

- **Cause.** §3.2 records `Layer(S, X)` when S is a pair binder. Suppose D's storage instantiates `E<s/U>` with D's plain formal T,
  or with a projection. Then s becomes T's outer Semantics and U becomes T's target, and no formal of D names either.
- **Evidence.** Dropping the obligation accepts l17: `Fwd<obj/Item>` stores `SemAny<obj/Item>`, hence `obj/Base`. It also accepts
  l23 and l26. On random graphs, Package is UNSOUND in 8 of 179,754 queries.
- **Required.** The Semantics of a layer obligation is one of:
  - concrete;
  - a pair binder;
  - `outer(X)`, for X a plain formal or projection.

  The pair target is `target(X)`. At use, `outer(W)` is W's outer Semantics, which is owner for a non-layer W. An abstract X admits
  all nine Semantics, or only owner when X is proven ObjectPayload or Sealed.

### F2. Peeling, a second source of growth (2b-4a, non-terminating without a family)

- **Cause.** §3.2 says Obl grows only around a projection cycle. A declaration can also pass its pair target back as a whole
  argument, as in `Peel<s/T>{a} = s/Base during a, Option<obj/Peel<T>{a}>`. That peels one layer per level and adds
  `outer(target^k(T))` for every k.
- **Evidence.** Before the fix, the first random runs did not converge on such graphs (NONTERM at round 24). Without
  `outer(...)`, l26 is UNSOUND.
- **Required.** This was verified on l25–l27 and on the random graphs.
  - A pair target passed as a whole argument is a growing edge of the projection-link graph.
  - On such a cycle, the `outer(target^k(Y))` obligations merge into `outer*(Y)`. At use, `outer*(Y)` is the conjunction of the
    layer rule over W's layers.
  - An obligation on `target(Y)` is covered by the same obligation on Y. The target is a component of Y, and a Sealed Y is owner,
    so Ob(Y) ≥ Ob(target(Y)) in P < U < R.

### F3. "Proven when none does" (2b-4, unsound as worded)

- **Evidence.** In l21, s is ref or obj and U is Transferable without a Sealed premise. The ref case proves, but the obj case is
  Unknown. The wording gives Proven where SPEC gives Unknown. Under the anchor, that accepts a confined read whose `obj/OpenBase`
  instance is an error.
- **Required (8.3.4).**
  - Proven when every admitted Semantics proves.
  - Refuted when every one refutes.
  - Otherwise Unknown, with Error absorbing.

### F4. An object layer always requires its target (2b-4, precision)

- **Evidence.** §3.2 says the layer is "Unknown when [the Sealed proof] is Unknown". For l24 (`obj/U` with `U is not Transferable`)
  that gives U where SPEC gives R: the View Target is an OwnedOrigins component whatever its Sealed proof (15.2.3). This is not
  unsound, since A4 gives Limit, a located U.
- **Required.** The layer is the Sealed condition ∧ Transferable(target). The summary walk already visits every target, so only the
  use-site leaf over an abstract target changes.

### F5. Projection families: the package's pattern does not terminate (2b-4a, owner choice)

- **Cause.** Take `Walk<T> = T.(Seq).Elem, Option<obj/Walk<T.(Seq).Next>>`. The cycle prefixes `.(Seq).Next` to every obligation,
  so the obligations are T.Nextᵏ.Elem. `(Tᵢ, p, q*)` with "already held" detection reads T.Next.Elem as "T.Next, then Elem*" and
  never covers them. f5 and f6 still grow at round 24, with 25 obligations.
- **The correct shape is Tᵢ.Q*.s:** the cycle's increments apply to the base, then the direct suffix follows. Corrected forms such
  families only on edges inside a growing SCC, and judges them by exploring Contract-knowledge states. It meets SPEC on f1–f10 and
  agrees with Bounded8. Without the SCC condition it would over-approximate p9 (`Two<T>`).
- **B's inductive rule** is sound as a proof rule: each Tᵢ.p.qᵏ follows by k applications of 8.3.2's refinement row, with no
  coinduction. It is the s = ε case of that exploration.
- **Options for the owner:**
  - (a) A's fallback, which the package already names. Any cycle of the projection-link graph with a projection step gives a
    located `Unsupported_Kd` at the declaration, and its answers are Error. It is registered at X-2 with the SPEC gap "15.3.5's
    fixed point for projection families". The cycle check is cheap. FamilyU shows f1–f10 as Error (U at TreeNode, LFam, Walk, Even,
    Odd), sound, in 7 rounds.
  - (b) The Q*.s families with SCC-gated detection and the state judgment.
- **Recommendation: (a).**
  - The detection and inclusion test in (b) are heuristics, checked only on these cases and Bounded8.
  - SCCs with nested cycles give regular sets beyond one star.
  - No library or milestone declaration stores a projection recursively (both designs' scans, H).

  Peeling (F2) stays exact either way, because `outer*` is judged directly on W's layers.

### F6. Error under rc and through nominal components (2b-4a, clarification)

The walk continues into every layer's target, including under `rc` and `raw`: Owned reads the target, and Error absorbs (8.3.2). A
nominal component contributes its Error bit. So an invalid declaration reached only under `rc` makes Transferable Error, not
Refuted. A first reference that stopped at `rc` disagreed with the summary on such random queries; SPEC's components include the
target.

### F7. Owned shares the summary; no guard or cap is needed (G174, 2b-4a)

- **Corrected's Owned** meets SPEC on every case, including `Chain<i32>` (r5), `Nest` (r1), `Ping` (r9) and `Fold` (p6, p7). All
  fixed points converge in at most 7 rounds on the cases and 6 on random graphs, and no declaration holds more than 2 obligations.
- **Today's guard (Guard)** gives Unknown Owned on 17 cases: G174's `Chain` (r5), and also `Nest`, `Ping`, `Grow`, `Fold`, `Peel`
  and the families.
- **A depth cap is no substitute.** Cap4P accepts r11 (UNSOUND: rc six declarations deep), and Cap4U answers U on 19 cases.
- 2b-4a's planned change of `ExpandingGenericRecursionRemainsUnknownWithoutUnboundedExpansion` to Proven agrees with SPEC 15.3.5.
  The census of other growers (Copy) is not modelled.

### F8. The container anchor holds (§3.5; A-2, P-2)

- Closed statics (a1–a9) get exactly SPEC's verdict.
- A generic static whose verdict depends on the instance gets Unknown, which A4 maps to Limit, a located U (a10–a14, a16, a17,
  a20, a21). It never accepts a SPEC error and never gives a Language error for a valid read.
- It is Proven where the container's premises prove (a15, a18, a19).
- BodyAnchor accepts a12 (A-2, UNSOUND). TwoValued rejects a16 (a Language error for a valid read), so P-2's fix holds.

### F9. The exemption (2b-4)

The exempt table needs the declarations of the iterators' internal storage. Without them `ArrayIterator<i32>` is Refuted
(NoInternal: c27). The exempt declarations' own summaries have no ExclT, which is the condition `KimiLibraryShapeTest` asserts.

### What 2b-4a and 2b-4 implement (if the owner takes F5 (a))

- **2b-4a (summary):**
  - the representation F1 and F2 need;
  - a walk that visits every target (F6) and propagates Excl and Error;
  - the projection-link graph's growing SCCs (projection steps and peels);
  - for peels, the `outer*` merge and the target coverage;
  - for projection families, the located U at the declaration;
  - Owned reading ExclO, Proj, Error and the instance's Origins.
- **2b-4 (leaves and anchor):**
  - F3's admitted-set rule and F4's object leaf;
  - the exempt table with the iterators' internal storage (F9);
  - the anchor as §3.5 states it (F8).

## Not modelled

- **Neighbouring analyses.** Copy and the census of `RequestCapability`'s other growers; G170's premise supply, where the model's
  knowledge closure is exact over its own facts; identity premises (8.2.3); A4's per-view consumption beyond the anchor table.
- **Origins and environments.** Origin relations beyond "= static", such as `outlives` premises; associated Types' Origin
  parameters; inherited environments, which are modelled as extra formals.
- **Conformances.** Verification covers associated-Type requirements and intrinsic parents only.
- **Random graphs.** No projections, so families rely on the cases and Bounded8.
- **Other.** Diagnostic categories, costs and allocations.
