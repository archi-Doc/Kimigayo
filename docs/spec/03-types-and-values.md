# 3. Types and values

[Specification index](../SPEC.md)

A **Type** combines Semantics, a Core and any required Origins. One layer is written `Semantics/Core during Origin`.

| Term | Meaning |
| --- | --- |
| Type | The complete Type: Semantics, its target and all Origin dependencies. |
| Semantics | The representation, ownership, borrowing, access and safety rules of a value ([§3.3](#33-semantics)). |
| Core | The value's kind, structure and identity: a primitive Core ([§3.1](#31-primitive-cores)), a struct or enum ([§6](06-declarations-and-containers.md#6-declarations-and-containers)), a Tuple, a fixed array ([§4](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices)), a Callable Core ([§3.2.1](#321-callable-value-types)) or a named library Type such as `Array<T>`. |
| Origin | A symbolic lifetime that denotes a region, the program points where a borrow is valid ([§3.7](#37-origins-and-loans)). It is derived from a borrow source, declared, an intersection, or `static`. |

In `ref/Dog during source`, the Semantics is `ref`, the Core `Dog` and the Origin `source`. Semantics and Origins may be omitted only under the inference and elision rules, and not every layer has an Origin. The Core's elements and Type arguments may be complete Types: the Core of `owner/(i32, ref/Dog during source)` is a Tuple whose second element keeps its borrow and Origin. `ref`, `uniq` and `raw` may enclose a complete Type ([§3.3.2](#332-nested-semantics-and-origin-annotations)), and object Semantics may target a runtime Contract View instead of a Core ([§3.3.1](#331-object-views-and-identity)).

## 3.1. Primitive Cores

**Scalar** is the closed set of the integer Cores (`i8`, `u8`, `i16`, `u16`, `i32`, `u32`, `i64`, `u64`, `i128`, `u128`, `isize`, `usize`), the floating-point Cores `f32` and `f64`, `bool`, `char` and the twelve wrapping integer Cores. `string`, Unit and Never are not Scalars. Scalar is not a source-level name.

An **integer Type** is an integer Core under `owner` Semantics, a **wrapping integer Type** is `Wrapping<T>` for an integer Type `T`, and a **numeric Type** is any of these or a floating-point Type. A rule for integer Types covers wrapping integer Types only where it says so.

| Types | Storage size |
| --- | --- |
| `i8`, `u8`, `bool` | 8 bits |
| `i16`, `u16` | 16 bits |
| `i32`, `u32`, `f32` | 32 bits |
| `i64`, `u64`, `f64` | 64 bits |
| `i128`, `u128` | 128 bits |
| `isize`, `usize` | The target's pointer size |

The only valid `bool` bit patterns are `0x00` (`false`) and `0x01` (`true`); reading any other as `bool` is undefined behavior ([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)).

### 3.1.1. Wrapping integer Types

`Kimi.Wrapping<T>`, for an integer Type `T`, is a distinct Scalar with the representation and value interpretation of `T`. An arithmetic result that `T` cannot represent wraps modulo 2ᴺ instead of Aborting ([§13.3.1](13-operators-and-assignment.md#1331-built-in-numeric-operators)).

- Only `::Kimi.Wrapping<T>` ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)) is a Scalar, whatever a local `Wrapping` denotes; the default alias makes `Wrapping<u32>` available.
- The argument must be an integer Type: `Wrapping<f32>`, `Wrapping<ref/u32>` and `Wrapping<Wrapping<u32>>` are invalid. Over a Type parameter `T`, `T is PrimitiveInteger` must be proven ([§8.7.4](08-generics-constraints-and-contracts.md#874-primitiveinteger)).
- It has no members and no public constructor; its values come from literals ([§12.3](12-expressions.md#123-literal-typing)), arithmetic and conversions ([§13.5.4](13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions)).
- `Wrapping<T>` and `T` are unrelated, with no implicit conversion; so are wrapping Types over different arguments. A literal argument that fits overloads for both is ambiguous.
- It is Copy, Owned, Sealed, ObjectPayload and a read Type ([§3.5.3](#353-value-read)), with the Equatable, Comparable and Utf8Format conformances of `T` and the arithmetic conformances ([§13.3.3](13-operators-and-assignment.md#1333-arithmetic-contracts)), including Negatable for unsigned arguments. Infix `&`, `|` and `^` (XOR) apply to wrapping integer Types ([§13.3.1](13-operators-and-assignment.md#1331-built-in-numeric-operators)).
- It satisfies neither `PrimitiveInteger` nor `Position`: it cannot be an array or Slice position, a range boundary, the operand of prefix `^` (a from-end position), a length constant, a constant index ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)) or a shift count.

```kimi
let a: Wrapping<u8> = 250
let b: Wrapping<u8> = a + 10      // 4: wraps modulo 2⁸.
let h = a ^ b                     // XOR.
// let p = ^a                     // Error: Wrapping<u8> is not a Position.
let c: u8 = 250
let d = c + 10                    // Abort: 260 is not representable in u8.

func describe(value: u32) -> () => ()
func describe(value: Wrapping<u32>) -> () => ()
// describe(5)                    // Error: both candidates fit.
describe(5@Wrapping<u32>)
```

### 3.1.2. Characters and strings

`char` is one Unicode scalar value, U+0000..U+D7FF or U+E000..U+10FFFF, stored in 32 bits. Every value in these ranges is valid, assigned or not; character literals ([§2.8](02-source-and-lexical-structure.md#28-character-literals)) restrict only spelling. `char` need not share the representation of `u32`, and it is not a UTF-8 code unit (`u8` stores bytes). Every scalar value decoded from UTF-8 is a valid `char`.

`string` is the built-in Core for UTF-8 text. Its storage layout is implementation-defined, and `owner/string` is Non-Copy.

```kimi
let euro: char = '€'          // One scalar value, three UTF-8 bytes.
let flag: string = "🇯🇵"
// let bad: char = '🇯🇵'      // Error: two scalar values.
```

### 3.1.3. Unit and Never

`()` is the Unit Type. It has exactly one value, the absence of a meaningful result, and has an initialization state and a lifetime even when it occupies no storage.

**Never** is the compiler-intrinsic value Type `::Kimi.Never` ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)) and has no values or constructors. Its written name follows ordinary lookup and may be shadowed; inferred Never always denotes that Kimi declaration. `return`, `exit`, `continue`, `yield` and `$abort(...)` have Type Never; their operands supply results to their targets. Other control-flow expressions have Type Never only under result validation ([§14.9](14-control-flow.md#149-result-validation)), after every result source and Structural Completion is checked; Runtime Reachability alone neither decides their Type nor excuses a missing result. Never is a Type, not a Completion ([§14.1](14-control-flow.md#141-completions)).

Never is Copy, Owned and Transferable. It is not Sealed, ObjectPayload, PrimitiveInteger or Callable and conforms to no other Contract. Never may be used wherever a complete value Type is allowed: as a Type argument, a Field, a Tuple or fixed-array element, or an enum payload. A value of a Type that stores Never inline can never be constructed, and neither can an enum Case whose payload stores Never inline. Exhaustiveness checking does not treat such Cases as unreachable. The layout of Never is implementation-defined.

```kimi
let a: Option<Never> = .None
let b = a                // Copy: Never is Copy, so Option<Never> is Copy.
match r                  // r: Result<i32, Never>
    .Ok(let v) => use(v)
    .Err(_) => ()        // Still required: Never payloads give no coverage.
```

## 3.2. Compound Types

A named Core may be qualified and have Type arguments, as in `A.B<T, U>`. A Type declared inside another declaration keeps the normalized bindings of its enclosing environment, even without Fields, and gains no implicit outer instance ([§6.1.3](06-declarations-and-containers.md#613-inherited-environments-and-declaration-references)).

`()` is Unit, `(T,)` a one-element Tuple and `(T, U)` a two-element Tuple; `(T)` only groups. A **Function Type** is a Function Parameter List, `->` and a result Type (grammar: `FunctionType`, `FunctionParameters`, [§F.5](appendices/F-syntax-summary.md#f5-types)). The list is not a grouped or Tuple Type: `(T) -> U` and `(T,) -> U` take one `T`, `(()) -> U` takes one Unit and `((T,)) -> U` one Tuple. It may begin with the task slot `task;`, which is no parameter ([§24.2.1](24-suspension-and-asynchronous-tasks.md#2421-form)).

```kimi
func total(single: (i32,), pair: (i32, string), test: (i32) -> bool) -> i32
    return single.0 + pair.0
```

### 3.2.1. Callable value Types

A **Function Value** is a callable value of one of three Cores:

- A **Function Item Type** denotes one resolved function declaration and instantiation, with no capture environment. It is Copy, has Shared call, and is Owned when its bound arguments satisfy [§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned).
- A **Concrete Closure Type** denotes one anonymous-function expression and instantiation and stores its captures. Every evaluation with the same Type arguments has the same Core; distinct expressions have distinct Types. It is Copy exactly when every capture's complete Type is Copy. Its environment is not a user-accessible struct and cannot receive a user `drop`.
- A **common Function Type** `(A1, ..., An) -> R` holds an owned, type-erased environment. It is Non-Copy, requires an Owned environment, exposes only Shared call and cannot return a borrow that depends on its environment. Its arguments and results need not be owned.

A Function Item or Closure keeps its concrete Type, with its actual receiver and lifetime contracts, including through Callable generics ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)), or converts to a common Function Type when it satisfies that contract, and the result owns the environment and its cleanup ([§7.7.4](07-functions-and-callable-values.md#774-function-references-and-erasure)). Task-slot presence is part of Function Type identity; Types that differ in it never convert, even through a common Function Type ([§24.2.4](24-suspension-and-asynchronous-tasks.md#2424-compatibility)). Each value keeps its own Origin bindings, and elision follows [§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision). A direct borrowed input whose Origin is omitted binds it per call, and results may depend on it independently of the environment. A written input Origin names an existing Origin and is fixed ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)); an implementation whose input is bound per call fits it by instantiating that Origin ([§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility)); already bound nested dependencies stay fixed. An empty environment implies neither purity, absence of allocation nor concurrency safety. `ref/F` of a common function value is Copy and `uniq/F` Non-Copy; neither adds call capabilities.

```kimi
func twice(x: i32) -> i32 => x * 2

let item = twice                    // Function Item: Copy.
let common: (i32) -> i32 = twice    // Converted to a common Function Type.
// let copy = common                // Error: a common Function Type is Non-Copy.
let moved = common@move
```

### 3.2.2. Weak references

`Kimi.Weak<s/T>` is a compiler-managed Non-Copy struct declared with `s is rc or arc`. Its argument `S` is `rc/X` or `arc/X` for an Object Target `X` ([§8.7.3](08-generics-constraints-and-contracts.md#873-objectpayload)), or a pair `s/T` whose admitted set ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)) lies in {`rc`, `arc`}; anything else is rejected. A Weak owns one weak responsibility for its target, never the payload. Its outer Semantics is `owner`, and `ref/Weak<S>` borrows the Weak slot. It moves with `@move` and is duplicated with `Kimi.Intrinsics.clone` ([§13.6.3](13-operators-and-assignment.md#1363-weak-references)). Users cannot replace its Fields or `drop`.

There is no empty Weak; use `Option<Weak<S>>` for absence. An expired Weak is a present value that cannot be upgraded. For variance, Loan requirements and OwnedOrigins, `Weak<S>` is analyzed as storing an `S` in every state ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)); it keeps the complete Type and Loan dependencies of `S` and grants no access. A Weak has no member access, object borrow, runtime `is`, checked cast or view conversion: upgrade it first.

```kimi
let strong: rc/Item = makeItem()@rc
let weak = Kimi.Intrinsics.downgrade(strong@ref)    // Weak<rc/Item>
let again: Option<rc/Item> = Kimi.Intrinsics.upgrade(weak)
// let copy = weak                                  // Error: Weak is Non-Copy.
```

### 3.2.3. Optional Types

`T?` is exactly `::Kimi.Option<T>`, whatever local lookup finds, and every rule applies to the expanded Type. Each `?` adds a layer: `T??` is `Option<Option<T>>`, never flattened (grammar: `AnnotatedType`, [§F.5](appendices/F-syntax-summary.md#f5-types)):

| Spelling | Expanded Type |
| --- | --- |
| `ref/T?`, `ref/obj/T?` / `ref/(T?)` | `Option<ref/T>`, `Option<ref/obj/T>` / `ref/Option<T>` |
| `[N of T?]` / `[N of T]?` | `[N of Option<T>]` / `Option<[N of T]>` |
| `(T) -> U?` / `((T) -> U)?` | `(T) -> Option<U>` / `Option<(T) -> U>` |

`(A)? -> B` is invalid, and after Semantics prefixes a whole Function Type needs grouping. Origin attachment is resolved before expansion ([§3.3.2](#332-nested-semantics-and-origin-annotations)): `ref/T? during a` is `Option<ref/T during a>`. A binding-set suffix attaches to a named Type before `?`: write `View<T>{v}?` (that is, `Option<View<T>{v}>`); `View<T>?{v}`, `(View<T>){v}` and `View<T>{v}{w}` are invalid. Expansion erases or extends no dependency or Loan. Passing `ref/i32? during a` to `<s/T>` gives `s = owner` and `T = Option<ref/i32 during a>`.

The suffix is not accepted in dedicated name positions: `is Dog?`, `T?.Some` and `@ref?` are invalid. In `@S?`, `S` is a Type. `x@ref/T?` targets `Option<ref/T>`: it neither borrows `x` nor constructs Some, and is valid only when `x` already has that Type ([§13.5.3](13-operators-and-assignment.md#1353-defined-adaptations)). There is no implicit wrapping, unwrapping, default value or argument omission, and `null` is not None: for `raw/T?`, `.None` differs from `.Some(null)`. The storage representation of Option and of Unit is implementation-defined.

```kimi
let nested: i32?? = .Some(.None)
let noBorrow: ref/i32? during static = .None

func wrap<T>(value: View<T>) -> View<T>{result}?
    origin result.source == value.source
    return .Some(value@move)
```

## 3.3. Semantics

In the table, `T` is a Core or, in object forms, a runtime Contract View Target (grammar: `SemanticsType`, [§F.5](appendices/F-syntax-summary.md#f5-types)).

| Category | Syntax | Meaning |
| --- | --- | --- |
| `value` | `T`, `owner/T` | Directly owned value |
| `valueborrow` | `ref/T`, `uniq/T` | Shared / exclusive mutable borrow of a value |
| `object` | `obj/T`, `rc/T`, `arc/T` | Exclusively owned / non-atomically counted / atomically counted object |
| `objectborrow` | `objref/T`, `objuniq/T` | Shared / exclusive mutable borrow of an object |
| (none) | `raw/T` | Raw pointer |

`borrow` is `ref`, `uniq`, `objref`, `objuniq`; `owning` is `owner`, `obj`, `rc`, `arc`; `reference` is every Semantics except `owner`, raw included. These seven are the only Semantics categories.

- Only a pair binder may occupy a generic Semantics position: in `<s/T>`, the pair binder `s` is the outer Semantics of one complete Type and the pair target `T` its direct target ([§8.1.1](08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters)). Syntax position never changes a parameter's kind.
- Category names are Requirements when the subject is a pair binder, and cannot be shadowed there; elsewhere they are ordinary Names. A category is not a value Type or an adaptation shorthand. A Requirement may also name a concrete Semantics, combined under [§8.2](08-generics-constraints-and-contracts.md#82-constraints-and-requirement-expressions) (`s is ref or obj`), and the binder's admitted set ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)) decides it.
- The tests concern the outer layer only. `reference` gives no safe-borrow guarantee, `s is borrow` requires an outer safe borrow, and `s is owning or borrow` admits everything except `raw`. `owning` differs from the recursive Owned guarantee ([§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned)).

Value borrows give non-owning access to the storage of their Referent Type and do not follow a pointer stored there: `ref/(rc/T)` borrows a handle slot, `objref/T` the object. Many shared borrows may coexist; nothing conflicting coexists with an exclusive one. An object keeps its dynamic payload, Type metadata and ownership responsibility; it need not be heap-allocated. `rc` and `arc` keep it alive while an owning reference exists; atomic counting does not make concurrent mutation safe. Objects are created and duplicated by the operations of [§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle) and released under [§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release). A complete Sealed payload may be selected with `@follow` and borrowed as a value ([§13.5.5](13-operators-and-assignment.md#1355-follow)); whole-content updates follow [§15.7](15-ownership-and-lifetime-analysis.md#157-whole-value-updates). Concurrency is deferred ([§D.5](appendices/D-deferred-features.md#d5-concurrency)).

```kimi
let node: rc/Node = makeNode()@rc
let slot: ref/(rc/Node) = node@ref     // Borrows the handle slot.
let view: objref/Node = node@objref    // Borrows the object.

func measure<s/T>(value: s/T) -> isize
    s is owning or borrow
    return 0
// measure(pointer)                    // Error: pointer: raw/i32; raw is not admitted.
```

### 3.3.1. Object views and identity

An **Object View Type** is the static Type of an object handle or borrow: object Semantics, a **View Target** (a Core or a runtime Contract View with fixed associated Types) and, for `objref` and `objuniq`, an outer Origin ([§8.1.1](08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters)). Object borrows obey the same well-formedness rule as value borrows ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)). Every owner Core that satisfies ObjectPayload ([§8.7.3](08-generics-constraints-and-contracts.md#873-objectpayload)) can be a concrete payload; Never and Cores that opt out cannot. A Sealed View Target proves the payload complete. Runtime Contract Views belong to the runtime Contract extension ([§8.12](08-generics-constraints-and-contracts.md#812-runtime-contracts)): in `objref/Speaker`, `Speaker` is not a Core, and there is no `owner/Speaker`.

The **Dynamic Type** is the concrete Core constructed. Its **runtime identity** is that Core with every Origin erased, recursively; Semantics inside Type arguments are kept, so `Box<ref/i32 during a>` and `Box<ref/i32 during b>` have one runtime identity while `Box<ref/i32>` and `Box<i32>`, or `Box<objref/C>` and `Box<obj/C>`, differ. A completed object keeps one Dynamic Type, identity and destruction responsibility through view changes and complete payload replacement ([§15.7](15-ownership-and-lifetime-analysis.md#157-whole-value-updates)); reinitializing it as another Type is forbidden. A new object at the same address inherits no identity, Loan or flow facts.

**`Supports(D, V)`** holds exactly when `V` is the concrete Core `D` or a direct or indirect base of it, or `V` is a runtime Contract View `C` with `ObjectViewCompatible(C)` and `Implements(D, C)` ([§8.12](08-generics-constraints-and-contracts.md#812-runtime-contracts)). Upcasts, view tests and checked casts use it; it grants no access, ownership, Owned, Loan validity or call of an incompatible member. There is no slicing, no implicit `owner/Dog` to `owner/Animal`, `ref/Dog` to `ref/Animal` or `uniq/Dog` to `uniq/Animal` conversion, and no container covariance; object upcasts are explicit ([§13.6.1](13-operators-and-assignment.md#1361-object-upcasts)), and inherited receiver projection ([§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection)) supplies only a receiver.

```kimi
// Dog is a struct whose base is Animal.
let dog: obj/Dog = makeDog()
let animal = dog@move@obj/Animal     // Explicit upcast; animal owns the whole Dog.
let view = animal@objref
// let back = view@objref/Dog        // Error: Animal does not imply Dog.
```

### 3.3.2. Nested Semantics and Origin annotations

`ref/V`, `uniq/V` and `raw/V` take a complete value Type `V` as their **Referent Type**: the outer layer refers to storage holding `V` and leaves `V`'s Semantics in place. Parentheses group complete Types without adding a Tuple or borrow: `ref/((i32) -> bool)` borrows a function value, `(ref/i32) -> bool` takes a borrowed integer, and `ref/(i32) -> bool` is invalid.

**Attaching `during`** (grammar: `AnnotatedType`, [§F.5](appendices/F-syntax-summary.md#f5-types)). The annotation targets the first explicit Semantics of its AnnotatedType. Without one, it binds the only schema slot of a named Type written directly there ([§15.3.1](15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets)); otherwise it has no target, which is an error. It is never sought through grouping, aliases, arguments, elements or signatures, or by removing `owner`. `during` is accepted only on `ref`, `uniq`, `objref`, `objuniq` and a pair binder `s/T` (a conditional slot) ([§15.3.1](15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets)). It is written after the `?` suffixes but applied before Optional wrapping: attachment is fixed syntactically, then Optional is expanded, then formation checks apply. An AnnotatedType has at most one annotation, and grouping cannot reannotate an inner layer. `during` adds no Type layer or value operation.

| Spelling | Meaning |
| --- | --- |
| `ref/T? during a`, `(ref/T during a)?` | `Option<ref/T during a>` |
| `ref/(uniq/T during b) during a` | Outer `a`, inner `b` |
| `Slice<T> during a` | Binds Slice's single slot |
| `raw/ref/T during a` | Error: write `raw/(ref/T during a)` |
| `(ref/T)? during a`, `T during a` for a Type parameter `T`, `(ref/T during a) during b`, `ref/T during a?` | Error |

In `ref/View<ref/U during c>{v} during a`, `a` is the outer borrow, `c` belongs to the Type argument and `v` names View's schema bindings. A function's trailing `during` belongs to its result: `(A) -> ref/B? during b` returns `Option<ref/B during b>`, and borrowing the function value needs `ref/((A) -> B) during a`.

**Normalization.** Aliases expand and every layer is kept: `ref/ref/T` differs from `ref/T`. Grouping and redundant `owner` prefixes normalize away, but `owner/V` keeps `V`'s references. Object Semantics require an Object Target ([§8.7.3](08-generics-constraints-and-contracts.md#873-objectpayload)): `ref/obj/T` is valid, `obj/ref/T` is not. Generic substitutions obey the same rules.

**Layers.** Each layer keeps its own Origins ([§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision)); an outer annotation never replaces an inner one. Each borrow layer must be well-formed ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)). Raw pointers establish neither safe-reference validity nor longer lifetimes. The outer Semantics decides Copy: `ref/uniq/T` is Copy, `uniq/ref/T` is not. Copying an outer shared reference does not duplicate an inner exclusive capability: shared access cannot Move a Non-Copy inner value, use its `uniq` exclusively or write the slot, and a shared Reborrow stays within the outer Loan. Moving or destroying an outer reference leaves its referent intact, and an exclusive slot replacement keeps the inner Type, Origin and Loan constraints.

```kimi
var number: i32 = 1
var exclusive = number@uniq                  // uniq/i32
let shared: ref/uniq/i32 = exclusive@ref     // Shared borrow of the slot; Copy.
let handle: obj/Node = makeNode()@obj
let slot: ref/obj/Node = handle@ref          // The handle slot, not objref/Node.
// let boxed: obj/ref/i32 = number@ref       // Error: a borrow is not an Object Target.
```

### 3.3.3. Selecting and reading a referent

A reference value is not read as its referent by default. The referent Place is selected explicitly with `@follow` ([§13.5.5](13-operators-and-assignment.md#1355-follow)) or by reference-path selection ([§3.4.3](#343-reference-path-selection)). A referent is read implicitly only by the value read ([§3.5.3](#353-value-read)) and at a fixed expected `ref/U`, where one shared reference is formed through layers ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)). A Non-Copy referent is never extracted through a reference ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)); inspect it by selecting a part, by comparison ([§13.4](13-operators-and-assignment.md#134-comparison-and-logical-operators)) or by borrowing.

There is no safe `*`. A safe reference becomes a raw pointer only through `@raw`, and a raw pointer becomes safe only by borrowing a raw Place in an unsafe context ([§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)). Explicit borrows borrow the written slot ([§13.5.5](13-operators-and-assignment.md#1355-follow)), and bare acquisition of a stored exclusive reference Reborrows it ([§3.5.1](#351-reborrow-of-a-stored-exclusive-reference)).

```kimi
func bump(counter: uniq/i32, limit: ref/i32)
    counter@follow += 1          // Explicit selection of the referent.
    let snapshot: i32 = limit    // Value read.
    // counter += 1              // Error: an assignment target is never redirected.
```

## 3.4. Places and storage

**Storage** holds values: a local, a static slot ([§22.4.4](22-core-execution-and-foreign-functions.md#2244-static-storage-keys)), a part of an aggregate, an object payload or a materialized temporary. **Destruction responsibility** is the obligation to destroy an owned value ([§16](16-scope-exit-and-destruction.md#16-scope-exit-and-destruction)). A **Place** is selected Storage with its complete Type, access path, capabilities and dependencies; a **Place expression** designates one, and parentheses keep that classification. A Place is not a value: it cannot be stored, passed, bound to a Type argument or wrapped in Option. To keep a location, borrow it. A complete value has construction completion and current completeness ([§15.1.2](15-ownership-and-lifetime-analysis.md#1512-construction-and-completeness)).

| Capability | Meaning |
| --- | --- |
| Read | Copy, borrow shared, compare or inspect the value |
| Write | Initialize or replace the value |
| Take | Extract the value with its destruction responsibility |

The three capabilities are independent; none implies another.

An owned `let` root offers Take but not Write; the referent of `uniq/T` offers Write but not Take. A child Place keeps its declared complete Type, never adopts the parent's Semantics, never recovers exclusive capability through a shared path, and keeps the restrictions of every layer ([§11.2.2](11-properties.md#1122-field-paths-and-partial-moves)). A live Loan restricts the use of a capability without changing it. Place overlap follows [§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts).

An **Access Designator** (a local, Field, Property, Tuple element, index, followed referent or raw dereference) resolves to a Place access, a value acquisition such as a getter result, or an error. These are alternatives, not fallbacks: a failed Move never becomes a borrow or getter call. Custom, computed and required `get` produce their declared result ([§11.1](11-properties.md#111-property-kinds)).

**Initialization** places a value in an empty Place; **Destruction** ends a value's lifetime and leaves remaining storage Uninitialized; nothing accesses storage after the storage's own lifetime ends. A value's lifetime is acquisition (Copy or Move), placement (Initialization or Replacement) and Destruction; one assignment may both Move and Replace. A Move prevents a second destruction at the source. Destroying a borrow or `raw/T` does not destroy the referent; destroying `rc/T` or `arc/T` releases one owning reference. Take is a Place capability, Move transfers a value and its responsibility, and Consume is the static check of [§15](15-ownership-and-lifetime-analysis.md#15-ownership-and-lifetime-analysis). States follow [§15.1.1](15-ownership-and-lifetime-analysis.md#1511-initialization-state); a **Partial Move** transfers an inline part ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)).

```kimi
var pair: (string, i32) = ("a", 1)
let n = pair.1               // Read: Copy.
let name = pair.0@move       // Take: Partial Move.
// let all = pair@move       // Error: pair is not complete.
pair.0 = "b"                 // Write; pair is complete again.
```

### 3.4.1. Access paths and value kinds

| Path | Definition |
| --- | --- |
| Direct | Resolves no reference: a local or parameter, static storage, a Temporary Place, their inline parts, or a payload reached through an owned `obj` handle |
| Through an exclusive reference | Resolves a `uniq` or `objuniq` reference |
| Through a shared reference | Resolves a `ref`, `objref`, `rc` or `arc` reference |

A raw Place (`*p`, `p[n]` and their parts) has no access path; its borrows, Take and authority follow the raw-Place rules ([§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places), [§5.2.3](05-raw-pointers-and-unsafe-memory.md#523-taking-from-raw-places), [§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)). The path's authority bounds every borrow ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)). A standard projection (Field, Tuple element, constant fixed-array index) of a temporary materializes the temporary and designates a Place inside its Temporary Place, reached by a Direct path; its value kind follows the Value kinds table below.

A source has the value kind of the first matching row; the path never changes it:

| Value kind | Definition |
| --- | --- |
| Borrow value | A value whose outer Semantics is `ref`, `uniq`, `objref` or `objuniq`, stored or returned. A stored reference is also a Place |
| Owned Place | Any other Place, including one storing `raw/T` or an owned handle |
| Owned temporary | Any other temporary |

An **object-kind input** is an owned handle or an `objref`/`objuniq` value; others are **value-kind inputs**. A **Receiver Expression** is the receiver of a method call, an accessor, or a direct call of a function value; it is acquired under [§7.3](07-functions-and-callable-values.md#73-receivers). The `self` argument of `Type.method(x)`, an assignment target and a `match` or `for` Subject ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)) are not Receiver Expressions.

### 3.4.2. Lending points

The **lending point** of a borrow is the storage it targets, and the Loan anchor ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)):

- for an explicit `@ref`, `@ref/V`, `@uniq` or `@uniq/V`: the written Place itself, even when it stores a reference (the reference slot is borrowed);
- for a Reborrow of a borrow value (implicit, bare, or written `@follow@ref` or `@follow@uniq`): its referent;
- for an owned Place: the Place reached through standard Field, Tuple-element and array-element projections;
- for an owned temporary: that temporary; for an object borrow: the object; for a followed payload: the payload; for a base projection: the base subobject.

An exclusive borrow requires its lending point to be exclusively writable under [§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition); a reference slot borrowed with `@uniq` follows the same root rules as an owned Place. Owners protected to keep the target valid are not part of the lending point ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)).

```kimi
var number = 1
var other = 2
var reference = number@ref
let slot = reference@ref     // Borrows the slot `reference`.
// reference = other@ref     // Error: slot is used below.
inspect(slot)
```

### 3.4.3. Reference-path selection

A member access, an index and a method receiver select a Place from their operand's Type. At each layer, selection stops when the complete Type has the required declaration, declared or provided by a Contract conformance or Type-identity Constraint. Otherwise, at a `ref` or `uniq` layer or a pair layer whose admitted set lies in `value or valueborrow` ([§13.5.5](13-operators-and-assignment.md#1355-follow)), it continues at the referent and repeats. These are the **safe value-reference layers**.

- Selection uses Types alone; a later acquisition or Loan failure never returns to another layer. Each selected layer keeps its capabilities, Origins and Loans.
- An object handle or object borrow has its View Target's declarations, so selection stops there ([§13.5.5](13-operators-and-assignment.md#1355-follow), [§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection), [§12.6](12-expressions.md#126-object-member-calls)).
- The same selection applies to a borrowing `for` entry ([§14.6.2](14-control-flow.md#1462-iteration-protocol)) and to structural Patterns ([§14.8.1](14-control-flow.md#1481-patterns)). A value read and a comparison select the terminal instead.
- In a generic body, a Type parameter or associated Type whose shape the Constraints do not determine is the terminal; only the capabilities its Constraints declare are used, and instantiation never selects another layer. A shape proven by a declared equality may be followed. A pair layer is followed unless its complete Type has the required declaration.
- Bare acquisition, a single-name binding and Type inference never select a referent or follow a pair layer. An update requirement such as that of `matrix[f()][j] = value` propagates only along the selected path, never into `f()` or behind a function or getter.

The acquisition of a selected Place applies to its own value kind, never its parent's. A Place storing `uniq/U` or `objuniq/U` is Reborrowed when its path grants exclusive capability. An owned Place lent exclusively outside a Receiver Expression needs `@uniq` or `@objuniq` on that Place: `holder@uniq.items` lends `holder`, not `items`. An index selects `index` or `indexUniq` by the capability its use requires ([§4.7.1](04-arrays-indexing-and-slices.md#471-index-expression-resolution)).

```kimi
// holder: a var Holder with Fields items: Array<i32> and link: uniq/Counter; values: var Array<Counter>; fill takes uniq/Array<i32>.
func update(node: uniq/Node)
    node.count += 1           // Selects the Field through the reference.

let link = holder.link        // Reborrow of a stored uniq/Counter.
fill(holder.items@uniq)
// fill(holder@uniq.items)    // Error: lending holder does not lend items.
values[0].bump()              // A Receiver Expression needs no spelling.
```

## 3.5. Copy and Move

**Copy** duplicates a value implicitly, leaving the source Initialized; it runs no user code, allocation, count change or resource acquisition. **Move** transfers the value and its responsibility, or a borrow's access, runs no user code and marks the source Moved. **Bare acquisition** is an expression without an `@` operation; for a Place `P` storing `T`, the first matching row applies:

| Operation | Result |
| --- | --- |
| Bare, `T` proven Copy | Copy |
| Bare, `T` is `uniq/U` or `objuniq/U` | Reborrow in the same Semantics ([§3.5.1](#351-reborrow-of-a-stored-exclusive-reference)) |
| Bare, any other Place | Error: write `@move` |
| `P@move` | Transfer, Copy or not; `P` becomes Moved |
| `P@copy` | Explicit Copy; `T` must be proven Copy |
| `P@ref`, `P@uniq` (or `@ref/T`, `@uniq/T`) | Shared or exclusive borrow of `P` itself |
| Bare or `@move` of a temporary | Transfers the value; no extra Copy or Move |

- `@move` is the only transfer out of a Place and requires a Movable Place ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)); `@copy` never transfers. `@owner/T`, `@obj/Base`, `@obj`, `@rc` and `@arc` perform what they name ([§13.5.3](13-operators-and-assignment.md#1353-defined-adaptations), [§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)) and never Move a Place implicitly.
- These rules apply at every value position: initialization, assignment, arguments, results ([§14.9.2](14-control-flow.md#1492-target-result-type-and-expression-type)), aggregate elements, payloads, defaults, Capture entries ([§7.7.2](07-functions-and-callable-values.md#772-captures)) and discard ([§14.2.4](14-control-flow.md#1424-explicit-discard)). Receivers follow [§7.3](07-functions-and-callable-values.md#73-receivers). At a fixed expected Type, common adaptation ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)) permits at most one implicit adaptation after explicit acquisition; an unproven Copy never becomes an implicit Move or borrow.
- A generic body plans the rows per admitted case ([§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects)). The functions of one Name acquire overlapping parameters in one mode ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)).
- Copy capability is independent of `let`/`var` and of Loans. A Copy obeys read restrictions and a transfer must not conflict with live Loans; neither extends a referent's lifetime. Distributing an acquired Subject into Pattern bindings needs no `@move` per binding ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)).

```kimi
let a: i32 = 10
let b = a                 // Copy; a stays Initialized.
var node: obj/Node = makeNode()@obj
let owned = node@move     // Transfer; node becomes Moved.
// let bad = node         // Error: a bare Non-Copy Place never Moves.
```

### 3.5.1. Reborrow of a stored exclusive reference

The Reborrow row of bare acquisition is the operation written explicitly as `P@follow@uniq` or `P@objuniq` ([§13.5.6](13-operators-and-assignment.md#1356-borrow-and-reborrow)). It does not Move `P`. The child keeps the stored Type with an outer Origin within the parent's ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)). It keeps the Loan ancestry, initialization state, slot protection and destruction dependencies, and depends on the referent and the parent Loan. While the child is live, conflicting uses of the parent are errors; after its last use the parent is usable again. It requires exclusive capability for the referent through the path:

- A `let` binding or parameter holding the reference may be Reborrowed; a `let` owned value still grants no exclusive access.
- A `uniq` reached through a shared path, as in `ref/(uniq/U)`, cannot be; its bare acquisition is an error, and an expected `ref/U` gives shared access.
- Only an outer exclusive reference is Reborrowed: `Option<uniq/U>`, a struct or an array is acquired whole, while an aggregate literal such as `[r]` acquires each element separately.

A Reborrow saved in storage is live when formed; one that produces a receiver or argument starts a call reservation ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)). `r@copy` on `r: uniq/U` is an error. `r@move` transfers the reference ([§13.5.6](13-operators-and-assignment.md#1356-borrow-and-reborrow)); a later adaptation never restores `r`, and a directly fitting exclusive reference is transferred without Reborrow ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)). `_ = r` Reborrows and drops a child ([§14.2.4](14-control-flow.md#1424-explicit-discard)).

```kimi
var number: i32 = 1
let r = number@uniq
let first = r                 // Reborrow: uniq/i32.
first@follow += 1
let held = r
// r@follow += 1              // Error: conflicts with the later use of held.
held@follow += 1
let inner = r@follow@ref      // ref/i32; r@ref would be ref/(uniq/i32).
let moved = r@move
```

### 3.5.2. Copy capability

`Copy` is a compiler-intrinsic Contract ([§8.7](08-generics-constraints-and-contracts.md#87-compiler-intrinsic-and-closed-contracts)); a user Contract of the same shape grants nothing.

| Type | Copy |
| --- | --- |
| Integers, wrapping integers, floats, `bool`, `char`, Unit, Never | Yes |
| `ref/T`, `objref/T`, `raw/T`, `Slice<T>{s}`, Function Items | Yes |
| `FromEnd<T>`, `Start`, `End`, `Range<S, E>`, `ClosedRange<S, E>`, `ResolvedRange` | Yes |
| `Kimi.Loan<T>` | When `T` is ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)) |
| Concrete Closure; Tuple or fixed array | When every capture or component is |
| `uniq`, `objuniq`, `obj`, `rc`, `arc`, `UniqSlice`, common Function, Weak | No |
| `Array`, `Dictionary`, `owner/string` | No |
| User struct or enum | Only by opt-in |

A struct or enum opts in with `Self is Copy`, or `Self is Copy when P` ([§8.8](08-generics-constraints-and-contracts.md#88-conditional-conformance)). Under its Constraints and condition, a struct's own Field Types and direct base must be Copy and it must have no user `drop`; every enum payload Type must be Copy. Each derived struct opts in separately, and a payload-free enum also needs the opt-in. Users cannot write Copy bodies, all-Copy Fields alone do not opt in, and Copy never slices. The proof must hold for every admitted binding; it invents no constraints, successful instantiations do not validate an unproven declaration, and an enum storing only `ref/T` needs no `T is Copy`. `T is Copy` is a constraint; unknown Copy is neither assumed nor treated as Non-Copy. In each admitted case, a bare by-value Place needs Copy evidence or a proven exclusive-reference Type ([§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects), [§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)), and element Places keep their stored Type ([§4.7](04-arrays-indexing-and-slices.md#47-indexing)). Duplicating an `rc`, `arc` or Weak uses `Kimi.Intrinsics.clone`.

```kimi
enum CopyOption<T>
    Self is Copy when T is Copy
    Some(T)
    None

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)
```

### 3.5.3. Value read

A **read Type** is a Scalar or a Type satisfying `Position` or `PositionRange` ([§4.6.1](04-arrays-indexing-and-slices.md#461-positions)); a generic Type qualifies only with `PrimitiveInteger` ([§8.7.4](08-generics-constraints-and-contracts.md#874-primitiveinteger)), `Position` or `PositionRange` proven, or as `Wrapping<T>` with `T is PrimitiveInteger`. Read Types are Copy and Owned.

Where a read Type `T` is required, a value of `ref` or `uniq` layers ending in `T` supplies `T` by a **value read**: the safe value-reference layers are followed and the terminal value is copied. The positions are common-adaptation value positions ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)), result sources of a read Type ([§14.9.2](14-control-flow.md#1492-target-result-type-and-expression-type)), built-in operator operands (including range boundaries and the operand of prefix `^`), `bool` conditions, an owning receiver of a read Type ([§7.3](07-functions-and-callable-values.md#73-receivers)), and the operands of numeric, wrapping and bit conversions and Identity Acquisition at a read Type ([§13.5.2](13-operators-and-assignment.md#1352-selection-and-inference)). An operator selects its operation from each operand's terminal Type, while the arithmetic route still decides acquisition ([§13.3.5](13-operators-and-assignment.md#1335-evaluation-of-arithmetic-operators)) and a shared Contract requirement borrows numeric inputs.

Object Semantics, raw pointers and Fields are not followed, and every layer is checked for initialization, capability and Loans; for `factor: s/i32` under `s is value or valueborrow`, `factor * 2` reads in every admitted case. No conversion is added, an unresolved literal fits the terminal Type ([§12.3](12-expressions.md#123-literal-typing)), and overflow and short-circuit rules are unchanged. Each read sees the current value and adds no borrow. An assignment target is never redirected to a referent. A Copy referent that is not of a read Type is copied only through explicit `@follow`; comparisons follow [§13.4](13-operators-and-assignment.md#134-comparison-and-logical-operators).

```kimi
for number in numbers        // numbers: Array<i32>; number: ref/i32.
    let reference = number   // ref/i32.
    let doubled = number * 2 // i32.
    let wide = number@i64    // Value read, then conversion.
    // number += 1           // Error: the target is the reference, not its referent.
```

## 3.6. Temporaries

A **Temporary Value** (a *temporary*) is an expression result that is not a persistent Place. **Materialization** gives it a Temporary Place when storage is needed, without reevaluation, Copy, allocation or lifetime extension; it never turns a failed Consume into a Read. A newly owned temporary is exclusively writable unless restricted, such as a getter result ([§11.5](11-properties.md#115-getter-results)); this permits an explicit exclusive borrow but bypasses no Loan, Origin or read-only part.

A temporary lasts until its outermost expression finishes. Argument temporaries last through the call. Temporaries created while evaluating a `match` or `for` Subject expression live until that construct finishes; they are destroyed after the Subject, in reverse creation order. Remaining temporaries are destroyed in reverse creation order. A transfer that interrupts the expression cleans them up under [§16.2.1](16-scope-exit-and-destruction.md#1621-cleanup-order). After a Move, a Temporary Place destroys only its remaining parts. Each condition test is a lifetime boundary ([§14.2.3](14-control-flow.md#1423-conditions)). A temporary that is already a borrow is acquired as that reference with its Origins; `@ref` on it adds a layer. A borrow of a temporary cannot outlive it, and borrow or Slice formation extends no lifetime. Result transfers secure their values before scope-exit cleanup ([§16.2](16-scope-exit-and-destruction.md#162-scope-exit)).

```kimi
let view = makeResource()@ref
// inspect(view)                // Error: the temporary expired.
match makeHolder().item         // A Place in a temporary: shared borrow.
    .Some(let v) => inspect(v)  // v: ref/Item; the Holder temporary is still alive.
    _ => ()
```

## 3.7. Origins and Loans

An Origin denotes a region ([§15.2.1](15-ownership-and-lifetime-analysis.md#1521-origin-expressions)); a Loan records which Place is borrowed and how ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)). `origin a outlives b` and `origin a == b` relate their regions ([§15.2.2](15-ownership-and-lifetime-analysis.md#1522-ordering-and-intersection)). Origins are written in signatures and Type declarations and inferred for locals; omission follows [§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision).

```kimi
func first(items: ref/Array<i32>) -> ref/i32 during items
    return items[0]@ref
```

## 3.8. Type relations and operations

A Type relation authorizes no value operation: Borrow, Reborrow, Copy and Move stay value operations, and unresolved generic or Origin information is no evidence of compatibility. **Normalized identity** compares two Types after normalization ([§3.3.2](#332-nested-semantics-and-origin-annotations)): structure, declarations, Type arguments, Semantics and Origin bindings; bound Origin parameters correspond by binder. Distinct nominal declarations are never identical, and runtime object identity ([§3.3.1](#331-object-views-and-identity)) is a different relation.

**Subtyping.** `A <: B` holds exactly when `A` and `B` are identical after normalization except for Origin bindings, and every Origin position satisfies its variance ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)): `a outlives b` at a covariant position, `b outlives a` at a contravariant one, and `a == b` at an invariant one. This is the only subtype rule. Function Types are related only by it. Never is a subtype of no other Type. `Wrapping<T>` and `T`, `uniq/T` and `ref/T`, and derived and base Types are unrelated. Each Origin position yields a required Origin relation ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)), so a fit whose structure holds is never a Type mismatch. Subtyping inserts no operation and extends no lifetime.

**Never fitting.** A Never expression fits any otherwise valid expected value Type without a value conversion. This is not subtyping. No outer value operation runs on a non-completing path, and transfer operands are checked against their own result boundary ([§14.9](14-control-flow.md#149-result-validation)).

Other relations and operations are owned elsewhere: callable compatibility and result compatibility ([§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility)), expected-result filtering ([§10.3](10-overload-resolution-and-inference.md#103-expected-results)), implicit adaptation ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)), which permits at most one implicit adaptation after acquisition and never chains implicit adaptations, explicit adaptation ([§13.5.3](13-operators-and-assignment.md#1353-defined-adaptations)), object upcasts ([§13.6.1](13-operators-and-assignment.md#1361-object-upcasts)), borrows ([§13.5.6](13-operators-and-assignment.md#1356-borrow-and-reborrow)) and numeric conversion ([§13.5.4](13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions)); `i32` is not a subtype of `i64`. An exact Type match can still fail the Copy, Move or Borrow requirements of acquisition legality ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)) because storage is Moved, access is unavailable or a Loan conflicts; such a failure does not reopen lookup or overload selection.

```kimi
func pick(long: ref/i32 during a, short: ref/i32 during b) -> ref/i32 during b
    origin a outlives b
    return long                          // OK: ref/i32 during a <: ref/i32 during b.

func f(handler: (i32) -> Never) -> ()
    let g: (i32) -> i32 = handler@move   // Error: (i32) -> Never is not a subtype of (i32) -> i32.
```
