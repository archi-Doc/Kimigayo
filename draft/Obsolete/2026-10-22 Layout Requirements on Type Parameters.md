# Layout Requirements on Type Parameters

Status: 完了 (closed 2026-10-22): the owner chose D, no change (OQ-Y1 (d); DECISIONS R4-M103). Nothing is integrated. Date: 2026-10-22. Base: `dev` `4902dd3c`. Written by
the R4 planning session on the owner's instruction (2026-10-21): 「この勢いで、先ほどの新しく挙げる候補 2、4、5、6の提案書も書いてください。」
(candidate 6). This is R5's area (the instance collector, layout and code generation). R5's planning session reviewed the first
draft (2026-10-22): its facts are corrected here, its third item is withdrawn (§3), and R5's recommendation is D, no change (§4).
Its independent review (`../Sketches/R4 Proposals 2026-10-21/review-3.md`) accepted it; the non-blocking notes are applied.
It would narrow the SPEC for generic C-layout structs (§5), so any change needs the owner's explicit decision. "(H)" marks a claim no
run has checked.

## 1. Problem

[§8.15.3](../../docs/spec/08-generics-constraints-and-contracts.md#8153-concrete-layout-and-representation) checks a concrete
Type's layout obligations at every use that needs the layout, for every instantiation a checked nongeneric body requires, reachable
or not; a failure is a Language error. [§6.7.1](../../docs/spec/06-declarations-and-containers.md#671-layout): "C layout is
unavailable for an open or derived struct, an empty struct, and a struct with a zero-sized Field". Open, derived and empty concern
the C-layout struct itself, which Binding decides at its declaration. Among C-layout restrictions, the only one that depends on a
Type argument is a zero-sized Field (size limits depend on Type arguments too; §3's withdrawn item treats them) (sketch):

```kimi
#Layout("C")
struct P<T>
    var n: i32
    var value: T

func f<T>(value: T) -> P<T> => P(n: 0, value: value@move)   // Valid as a generic body.
func g() => _ = f(())                                     // Error today: P<()> has a zero-sized Field.
```

Under MIR code generation, `g`'s error is a record of the instance collector at `check` (R5 plan OD4: K4b, R5-M41, records decide acceptance; K10a–K10c:
R5-M44 limits, R5-M55 C layout, R5-M52 oversized layouts). For a user generic in a root module, KD12's location rule (R5-M40) puts
the record at its cause site inside `f<()>`; for a library instance, at the root-module site of its first admission. Neither `P`'s
nor `f`'s declaration states that `T` must not be zero-sized (Principle 2). On the default pipeline the collector does not run
until R5's G2 (R5-M41), so the old generator reports it (H for where).

## 2. What the collector costs and why it stays

- **Measured (R5-M41, R5-M44):** a fresh `check` under sel allocates 0.15 / 1.29 / 2.31 / 4.61 MB more than the MIR pipeline alone
  at Generic shapes 1 / 32 / 64 / 128, linear in items, plus up to 472 KB for layout; a warm Analyze allocates the same as without
  it (the collector's warm run is 0 B); retained memory is about 2 KB per item. Timing waits for G2.
- **It stays at `check` for other records.** The owner answered the R5 plan's OQ5: `check` reports instance-stage `Unsupported_Kd`
  and Resource records for the checked set, and IMPL 21.3.3 requires a report for every instantiation of it:
  `GenerationResourceLimit_Kd` (depth 128, 16 growing items, 1,024 per definition, layout size, count and depth limits),
  instance-stage capability gaps (StorageNesting, G128; EnclosingInstance, G149; K9b's rows) and `OversizedLayout_Kd`.

So a Type-parameter requirement removes one record kind (C layout at an instance), not the collection.

## 3. Proposal A′ and the withdrawn item

1. **A compiler-intrinsic Contract for C-layout Fields**
   ([§8.7.1](../../docs/spec/08-generics-constraints-and-contracts.md#871-compiler-intrinsic-contracts)), provisionally
   `Kimi.CField`: a Type conforms when it is not zero-sized, the condition Layout's C-layout outcome tests. (The first draft also
   excluded open and derived struct Types and recursed into Fields, which would have rejected a Field of a Kimigayo-layout struct
   Type that §6.7.1 allows.)
2. **C-layout structs state it.** A `#Layout("C")` struct whose Field Type's size may depend on a Type parameter (`T`,
   `Wrapper<T>`; not `ref/T` or `(T, i32)`, which are never zero-sized) is valid only when its premises prove that Field Type
   `CField` (by structural recursion over sizes, not over C-layout eligibility), typically by a written requirement (`T is Kimi.CField`). A use of `P<U>` proves the requirement
   by ordinary Constraint proof where the Type is formed (§8.3), in the caller's body; a generic body forming `P<T>` needs the
   premise itself. Binding's `ValidateCLayoutFields` takes the premise for generic Field Types (BU), and R5-M55's instance mapping
   becomes unreachable for proven arguments (an `InternalInvariant_Kd` guard, or deleted).

**Withdrawn: size and alignment limits as Resource failures.** The first draft's third item would have reclassified
`OversizedLayout_Kd`. It reverses the owner's decision (2026-10-18, on PLAN G146: 「新しい言語エラーのコードを追加して下さい」;
DECISIONS R5-M52) and IMPL 21.1.1 (a size, stride or Field offset over 2^63 − 1 is a Language error at the use, never only a
Resource record), and the collector stays at `check` anyway (§2). It is not proposed.

## 4. Options

| Option | Content | Assessment |
| --- | --- | --- |
| A′ | §3, items 1–2 | The zero-sized-Field error moves to the Type formation in the caller, and the requirement is visible in the declaration. No cost benefit (§2). Costs one intrinsic Contract (vocabulary: owner), about ten xUnit re-expectations and a narrowing (§5) for a form the library and the milestones never use (H) |
| **D (recommended, R5's view)** | No change: the collector at `check` (OD4) keeps reporting C layout per instance, at its cause site under KD12 | No narrowing, no vocabulary entry; the error stays at the instance's use for library instances |
| C | A5 plan §5.2's optional candidate: "instance layout obligations are checked at the declaration, or are Resource failures" | Its Resource half conflicts with R5-M52 ("never only a Resource record"), IMPL 21.1.1 ("a Language error at the use") and §6.7.1/§8.15.3 (a zero-sized C-layout Field is a Language error); its declaration half is A′ |

The R4 planning session agrees with R5's recommendation: A′ buys locality for one record kind of a form nobody writes yet, at the
price of a narrowing and a vocabulary entry. It becomes worth revisiting if generic C-layout structs appear in the library or in
foreign-function bindings.

## 5. What A′ narrows

- Generic C-layout structs and the generic bodies that form them must state `CField`: the library has none (its `#Layout("C")`
  structs `DictionaryStorage.Handle` and `Links` are nongeneric), the milestones none (H), and about ten xUnit tests declare one
  (`LayoutTest`, `LayoutAttributeBindingTest`, `InstanceCollectorTest`, `InlineLayoutTest`, `LibraryImportTargetBindingTest`,
  `ForeignEmissionTest`), each a re-expectation with the requirement written.
- A declaration's Constraints are public well-formedness obligations (§8.15.1), proven wherever the Type is formed, so under A′ a
  nongeneric `func h(p: raw/P<()>)` or `ref/P<()>` becomes a Constraint failure, although §8.15.3 does not check layout obligations
  for a Type formed only behind a borrow, raw pointer or object handle (H: no sentence exempts formation behind a borrow).

## 6. SPEC text (planned mapping, A′ only)

| Item | Sections |
| --- | --- |
| 1 | §8.7.1 (the Contract), §22.1 (`Kimi.CField` among the required declarations) |
| 2 | §6.7.1 (C layout of a generic struct), §8.15.3 (the zero-sized-Field obligation becomes a requirement) |

## 7. Principles (A′)

1. **One concept, one canonical form.** A layout restriction on a Type argument is a requirement, the form other restrictions on
   Type arguments take.
2. **Local reasoning.** The error is at the Type formation in the caller, and the requirement is in the declaration.
3. **Explicit semantics.** A generic C-layout struct states what its Type arguments must be.
4. **Compiler Server Protocol.** A Constraint failure carries the requirement and a repair candidate.

## 8. Owner question

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-Y1 | Declare the zero-sized-Field restriction on Type parameters? | (a) A′; (d) D, no change | (d) |

## 9. Unverified

- The xUnit count and the milestones' census (§5).
- G2's timing of the collector at `check` (§2 has allocations and retained memory only).
