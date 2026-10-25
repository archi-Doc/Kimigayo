# SPEC session, the items after N2: the designer's probe verdicts

Record (R4-M134). Corrections after the review (`review.md`): at G248's merge (`230938d7a`, R4-M129) the F1 verdicts changed as
R4-M129 intends: sel prints `one show two` for g248-f1-if2, A5 accepts g248-f1-arm and rejects borrow-if and borrow-do. The F2
verdicts did not change. The paths named below are the session's notes folder; the sources that R4-M134 cites are in `probes\`.

Base: dev `cdd3c99da`, built Release in `bin\worktrees\spec-rest` (detached; removed after the run). Three probe kinds:

- **Native** (`native\`): `run.sh <Kimi.dll> <probe>` builds each probe with the old generator ("old") and with
  `KIMI_PIPELINE=mir KIMI_CODEGEN=mir` ("sel"), then runs it. Outputs are in `native\out\<probe>-{old,sel}\`.
- **CLI** (`cli\`): `check.sh <Kimi.dll> <probe>` runs `check` and lists each diagnostic as `code@line`, leaving out
  `DeclarationOrderWarning_Kd` from the shared `Noisy` prelude. The CLI publishes Binding and the old analysis; A5 is shadow.
  `cli\old-*.kimi` are the harness sources below with their prelude, run this way for the old path.
- **A5** (`src\`, `src2\` with their `prelude.kimi`): the throwaway harness `ScratchSpecRestProbeTest.cs.txt` is the N2
  harness with renamed variables. It prints Binding issues (`B`), each call's retention rows and the MIR's problems (`M`).
  It was copied into the worktree's `tests\xUnitTest\Tests\`, built, run with
  `xUnitTest.exe -method XunitTest.ScratchSpecRestProbeTest.Dump` (`SPECREST_IN=<src>`, `SPECREST_OUT=<a5>`), and deleted.
  Results are in `a5\` and `a5-2\`.

## RZ

| Probe | Form | Result |
| --- | --- | --- |
| rz3-inplace (native) | `_ = (*p)@move` of a `Probe` placed in `Raw.allocate` storage, then `_ = local@move`. Probe's `drop` compares its Field's address with the one recorded at placement | old `moved moved end`; sel `moved moved end`. Both pipelines move the value into a temporary before destroying it, for the raw Place and for the local |
| rz7-local | inside one body, `*q = z@ref` through `q = r@raw` in an inner scope, then `r@follow` after it | A5 and old: `ComparisonLoanConflict_Kd` at `var z` |
| rz7-forge | `forge<ref/i32>(slot@follow@raw@raw/u8)` drops the inner Origin of `slot: uniq/(ref/i32 during c)`; `*q = z@ref` | A5: `ComparisonLoanConflict_Kd` at `var z` (storage the body does not own keeps z's Loan); old: accepted |
| rz7-raw-storage | the same write into `slot: raw/(ref/i32 during c)` | A5: err at `var z`; old: accepted |
| **rz7-other** | the forged pointer receives `other: ref/i32 during d`; the caller then writes `x = 8` (other's referent) while `r` (slot's referent) is used | **A5 and old: accepted.** Rows: `0 Content store 0` only |
| rz7-other-direct, rz7-other-safe | the same store without conversion, and `slot@follow = other` | `UnprovenOriginRelation_Kd` at the store (Binding) |
| rz7-param-direct, rz7-param-omit | `*q = z@ref` unconverted; the target `q@raw/(ref/i32)` | `UnsatisfiedOriginRelation_Kd`; `MissingOriginBinding_Kd` (`@raw/(ref/i32 during w)`: `MisplacedSyntax_Kd`) |

## G86 (CLI; Binding)

| Probe | Declaration and use | Result |
| --- | --- | --- |
| g86-static-owner | `struct Holder<s/T> {a}`, `s is owner or uniq`, `let item: s/T during a`; `Holder<i32> during static` | clean |
| **g86-static-uniq** | the same declaration; `Holder<uniq/i32 during r> during static` (the Field is `uniq/i32 during static` there) | **clean** |
| **g86-static-uniqonly** | `s is uniq`; the same use | **clean** |
| g86-static-ref | `s is ref or uniq`; `Holder<ref/i32 during r> during static` | clean |
| g86-static-concrete | `struct Holder {a}` with `let item: uniq/i32 during a`; `Holder during static` | `InvalidOriginBinding_Kd` ×2 |
| g86-uniq-use | `Holder<uniq/i32>.init(x@uniq)`, then `x = 2` while the Holder is used | `ComparisonLoanConflict_Kd` (instance Loans are per case) |

## G159 (2) (CLI)

| Probe | Form | Result |
| --- | --- | --- |
| **g159-both-await** | `let t = if c => (a, (return 1)) else => ((return 2), a)` | **`TypeMismatch_Kd`** at the `if` |
| g159-annotated | the same with `let t: (i64, i64)` | Binding clean; old analysis `Unsupported_Kd` (G159 (4)) |
| g159-one-candidate | `(a, (return 1))` beside `(a, a)` | clean |
| g159-same-never | `(a, (return 1))` beside `(a, (return 2))` | Binding clean (`(i64, Never)`); old analysis `Unsupported_Kd` |
| g159-arrays | `[a, (return 1)]` beside `[(return 2), a]` | Binding clean (each literal `Array<i64>` by its own elements); old analysis `Unsupported_Kd` |
| g159-conflict | `(a, (return 1))` beside `(b, (return 2))`, `a: i64`, `b: i32` | `TypeMismatch_Kd` |
| g159-fresh | `let t = (a, (return 1))` | Binding clean; old analysis `Unsupported_Kd` |

## G158 (3) (CLI; native for the by-value instance)

| Probe | Form | Result |
| --- | --- | --- |
| g158-empty-unused | `#Layout("C") struct Empty` with a method only, used as `raw/Empty` | `InvalidCLayout_Kd` at the attribute |
| g158-open | an open C-layout struct, unused | `InvalidCLayout_Kd` at the attribute |
| g158-unit-borrow | a C-layout struct with a `()` Field, used only as `ref/Native` and `raw/Native` | `InvalidTypeFormation_Kd` at the struct, `InvalidCLayout_Kd` at the Field |
| g158-unit-value | the same, constructed by value | the same records, at the declaration, not the use |
| g158-generic-borrow, g158-generic-raw | `#Layout("C") struct Pair<T>` with a Field `T`; `ref/Pair<()>`, `raw/Pair<()>` in a signature | `InvalidCLayout_Kd` at the written instance |
| g158-generic-ok | `ref/Pair<i32>` | clean |
| g158-generic-value | `make(())` returning `Pair<()>` by value (inferred instance) | **`check` clean**; build: old `GenerationFailed_Kd`, sel `InvalidCLayout_Kd` ×3 |
| g158-never-field | a `Never` Field, borrowed only | clean (G158 (2)) |

## Item 5: omitted Origins in a Constraint's Type (CLI)

| Probe | Form | Result |
| --- | --- | --- |
| e5-ref-one | `func f<T, U>(x: T, item: U)`, `T is ref/U` | `MissingOriginBinding_Kd` at `ref/U`, `InvalidConstraint_Kd` at the declaration |
| e5-ref-two | adds `T is ref/i32`, returns `item` | the same, and `TypeMismatch_Kd` at `return item` |
| e5-ref-unbound | `T is ref/U during a`, `a` written nowhere else | `MissingOriginBinding_Kd` at `ref/U during a` (an unbound name introduces no Origin there) |
| e5-ref-named | `a` from a parameter `first: ref/U during a`; both Constraints name `a` | Binding accepts the Constraints; `TypeMismatch_Kd` at `return item`; `UnprovenConstraint_Kd` at the call `f(y@ref, y@ref, 2)` |
| e5-option, e5-view, e5-type, e5-contract | `T is Option<ref/U>`, `T is View` (an omitted slot), a struct's `T is ref/U`, a Contract's `Self.Item is ref/i32` | `MissingOriginBinding_Kd` (and `InvalidConstraint_Kd`) |
| e5-type-slot | `struct S<T, U> {a}`, `T is ref/U during a` | clean |
| e5-fn | `F is (ref/i32) -> i32` | clean (§8.2.3's Function Type sentence) |

## G248 F1 and F2 (native on dev, before G248's builder fix; A5 and old path)

| Probe | Form | Result |
| --- | --- | --- |
| g248-f2-default | `show(both(Noisy.init(1)))`, `b: Noisy = pass(Noisy.init(3))` | old `two show two three one`; sel `three two show one two`. The MIR ends the default's temporary (three) before the callee; the old generator ends it at the statement's end |
| g248-f2-twodefaults | the same; `both`'s body prints `body` | old `body two show two three one`; sel `three body two show one two` |
| **g248-f2-guard** | `g(u: uniq/i32, k: i32 = Guard.init(u@follow@ref).get())` called as `g(x@uniq)`; `Guard` borrows and has a `drop` | **old: `CallActivationConflict_Kd` at `g(x@uniq)`; A5: accepted** |
| g248-f2-explicit | an explicit Guard argument borrowing another local | both accept |
| default-ref | `b: ref/Noisy = Noisy.init(3)` | `UnsatisfiedOriginRelation_Kd` at `Noisy.init(3)` (Binding, both pipelines) |
| g248-f1-if2 | `show(if flag => pass(Noisy.init(1)) else => Noisy.init(3))`; the same with `match` | `if`: old `one show two`, sel `show one two`. `match`: both `one show two` |
| g248-f1-let, g248-f1-ret | the `if` as a `let` initializer and as a function result | both `one ...`: the arm's temporaries end with the arm |
| **g248-f1-arm** | `both(if c => Guard.init(x@ref).get() else => 0, x@uniq)` | **A5: `CallActivationConflict_Kd`; old: accepted.** The `match` twin is accepted by both. g248-f1-stmt, the Guard as a direct argument, is an error on both, as SPEC 3.6 requires |
| **borrow-if, borrow-do** | `show(if c => Noisy.init(1)@ref else => other@ref)`, `show(do => Noisy.init(1)@ref)` | **A5: accepted; old: `ComparisonLoanConflict_Kd`.** borrow-match: both error |

## Closure receiver (item 6)

There is no dev probe: 2c-1 is not on dev. The evidence is the 2c-1 re-check's probes
(`..\..\s2-2c-1\rc\probe\{x,q,m}`, `rc\head-*`), notably m01: a ResolvedRange `for` Subject through a captured `uniq` has
`loop.Mode` Exclusive, no entry call and MIR `copy (*(*_1).0)`, and 2c-1 classes it Shared. EffectBounds reads the mode (Uniq).
