# 3. Types and values

[Specification index](../SPEC.md)

An ordinary **Type** combines Semantics, a Core and any required Origins. Its single-layer form is:

```text
Type = Semantics/Core during Origin
```

| Term | Meaning |
| --- | --- |
| Type | The complete type, including Semantics, its target, and all Origin dependencies. |
| Semantics | The value's representation, ownership, borrowing, access and safety rules. |
| Core | The component that defines the value's kind, structure and identity. |
| Origin | The set of program points where a borrow is guaranteed valid. It constrains borrows and the values that retain them. |

For example, `ref/Dog during source` has Semantics `ref`, Core `Dog` and Origin `source`. Semantics and Origins may be omitted only under the inference and elision rules, and not every layer has an Origin.

A Core is distinct from the **outer** Semantics and Origins. Its elements and generic arguments may be complete Types: the Core of `owner/(i32, ref/Dog during source)` is a Tuple whose second element keeps its borrow and Origin.

The basic form has two extensions:

- `ref`, `uniq` and `unsafe` may enclose a complete Type, as in `ref/obj/Dog`; see [nested Semantics](#336-nested-semantics-and-type-grouping).
- Object Semantics may target a permitted runtime Contract View instead of a Core; the Contract itself is not a Core. See [object views](#335-object-views-and-identity).

The following tree classifies components; it shows neither inheritance relationships nor legal combinations:

```text
Type
├─ Semantics
│  ├─ Value: owner
│  ├─ Value Borrow: ref, uniq
│  ├─ Object: obj, rc, arc
│  ├─ Object Borrow: objref, objuniq
│  └─ Unsafe: unsafe
├─ Core
│  ├─ Scalar: Integer, Floating-point, Boolean, Character
│  ├─ String
│  ├─ Unit
│  ├─ Never
│  ├─ Struct
│  ├─ Enum
│  ├─ Tuple
│  ├─ Fixed Array: [N of T]
│  ├─ Callable
│  │  ├─ Function Item
│  │  ├─ Concrete Closure
│  │  └─ Common Function
│  └─ Other named examples: Array<T>, Dictionary<K, V>, Slice<T>
└─ Origin
   ├─ Derived from a borrow source
   ├─ Declared abstract Origin
   ├─ Intersection of Origins
   └─ static
```

The named examples introduce neither separate declaration forms nor mutually exclusive categories. Structs and enums follow Chapter 6; arrays follow [Chapter 4](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices). The **Kimi Kotonoha** (Chapter 22) is the language's foundation module; it is unrelated to a Type's Core.

## 3.1. Primitive cores

Primitive Cores are built in. Listed sizes are storage sizes.

**Scalar** (short for **primitive scalar**) is the closed set of the integer Cores (`i8`, `u8`, `i16`, `u16`, `i32`, `u32`, `i64`, `u64`, `i128`, `u128`, `isize`, `usize`), the floating-point Cores (`f32`, `f64`), `bool` and `char`. `string`, Unit and Never are not Scalars. Scalar is a specification category, not a source-level Type or Constraint name.

### 3.1.1. Integer types

| Signed | Unsigned | Size |
| --- | --- | --- |
| `i8` | `u8` | 8 bits (1 byte) |
| `i16` | `u16` | 16 bits (2 bytes) |
| `i32` | `u32` | 32 bits (4 bytes) |
| `i64` | `u64` | 64 bits (8 bytes) |
| `i128` | `u128` | 128 bits (16 bytes) |
| `isize` | `usize` | Native pointer size of the target platform |

### 3.1.2. Floating-point and boolean types

| Type | Size |
| --- | --- |
| `f32` | 32 bits (4 bytes) |
| `f64` | 64 bits (8 bytes) |
| `bool` | 8 bits (1 byte) |

The only valid `bool` representations are `0x00` for `false` and `0x01` for `true`. Reading any other bit pattern as `bool` is undefined behavior under the [valid-value requirement](05-raw-pointers-and-unsafe-memory.md#52-dereference-and-ownership).

### 3.1.3. Character type

`char` represents one Unicode scalar value and has a fixed storage size of 32 bits (4 bytes). Its valid ranges are U+0000..U+D7FF and U+E000..U+10FFFF, inclusive; surrogates (U+D800..U+DFFF) and values above U+10FFFF are invalid.

Every scalar value in these ranges is valid, including unassigned code points, private-use characters, noncharacters, controls and combining marks; displayability is irrelevant. [Character literals](02-source-and-lexical-structure.md#28-character-literals) add restrictions on direct spelling.

The size guarantee does not promise the same internal representation as `u32`. Alignment and byte order are not specified here.

### 3.1.4. UTF-8 and strings

`char` is neither a UTF-8 code unit nor a byte sequence. Every scalar value decoded from UTF-8 text can be represented as a `char`.

| Type | Meaning |
| --- | --- |
| `u8` | An 8-bit unsigned integer; it can store a byte or a UTF-8 code unit. |
| `char` | One Unicode scalar value, stored in 4 bytes. |
| `string` | UTF-8 Unicode text. |

Single quotation marks produce `char` and double quotation marks produce `string`. For example, `'🇯🇵'` is invalid because it contains two scalar values, while `"🇯🇵"` is a valid string.

Encoding one `char` as UTF-8 produces one to four bytes:

| Unicode scalar value | UTF-8 length |
| --- | --- |
| U+0000..U+007F | 1 byte |
| U+0080..U+07FF | 2 bytes |
| U+0800..U+D7FF, U+E000..U+FFFF | 3 bytes |
| U+10000..U+10FFFF | 4 bytes |

| Literal | Scalar value | UTF-8 bytes of the value |
| --- | --- | --- |
| `'A'` | U+0041 | `41` |
| `'€'` | U+20AC | `E2 82 AC` |
| `'😀'` | U+1F600 | `F0 9F 98 80` |

Encoded length and storage size differ: the source literal `'€'` occupies five bytes (`27 E2 82 AC 27`), including its three-byte UTF-8 content, while its value U+20AC is stored as a four-byte `char`.

`string` is the built-in Core for UTF-8 text. Its in-memory container and storage layout are implementation-defined. `owner/string` is always Non-Copy; see [Copy capability](#351-copy-capability-and-explicit-duplication).

### 3.1.5. Unit and Never types

`()` is the Unit type. It has exactly one value and represents the absence of a meaningful result.

Unit has storage size 0, alignment 1 byte and stride 0 in the [layout terminology](21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi). It still has a logical value, initialization state and lifetime. A materialized Unit Place needs no distinct address; this grants no permission to access a nonzero-sized Type there. ABI argument and result slots for Unit may be omitted or use implementation-defined physical padding without changing Unit's language storage layout.

Never has no values. `return`, `exit`, `continue`, `yield` and `$abort(...)` have Type Never; transfer operands supply results to their targets. Other control-flow expressions infer Never only under [result validation](14-control-flow.md#149-result-validation), after all result sources and Structural Completion have been checked; Runtime Reachability alone can neither determine their Type nor excuse a missing result. Never is a Type, not a [Completion](14-control-flow.md#141-completions): a transfer completes abruptly, divergence produces no Completion, and [Abort](17-failure-handling.md#173-abort-termination) ends the process.

## 3.2. Compound types

A named Core may be qualified with dots and may have generic arguments:

```kimi
A.B<T, U>
```

A Type declared inside another declaration retains the normalized bindings of its enclosing environment, even when it stores no fields. [Declaration references](06-declarations-and-containers.md#613-inherited-environments-and-declaration-references) distinguish full Type/Contract evidence from Origin-erased runtime and implementation identities. Declaration nesting adds no implicit outer instance.

Tuple Types use parentheses and commas: `()` is Unit, `(T,)` is a one-element Tuple, and `(T, U)` is a two-element Tuple. `(T)` groups a Type without creating a Tuple or changing its Semantics.

A Function Type consists of a parenthesized **Function Parameter List**, `->` and a result Type. The list is a distinct grammar element, not a grouped or Tuple Type; it accepts an optional trailing comma. `->` associates to the right. A bare Type cannot replace the list.

| Function Type | Parameters |
| --- | --- |
| `() -> U` | None |
| `(T) -> U`, `(T,) -> U` | One, of Type `T` |
| `(()) -> U` | One, of Type Unit |
| `((T,)) -> U` | One, of Type `(T,)` |
| `(T, V) -> U` | Two, of Types `T` and `V` |

```kimi
(i32, string)
(i32, string) -> bool
```

### 3.2.1. Callable value types

A **Function Value** is a callable value. Its concrete Type is distinct from a common **Function Type** `(A1, ..., An) -> R`:

| Core | Identity and environment | Value capabilities |
| --- | --- | --- |
| Function Item Type | One resolved function declaration and instantiation; no runtime capture environment | Copy; Shared call; Owned when its bound arguments satisfy §15.2.3 |
| Concrete Closure Type | One anonymous-function expression and instantiation; stores its captures and internal call signature | Copy exactly when every capture's complete Type is Copy |
| Common Function Type | A shared calling contract and an owned, type-erased environment | Non-Copy, even for an empty or Copy environment |

Each evaluation of the same anonymous-function expression with the same Type arguments produces the same anonymous Core; distinct expressions have distinct Types, even with identical text and signatures. Each value keeps its own Origin bindings. A Closure's environment is compiler-managed storage, not a user-accessible struct, and no user-defined `deinit` can be added to the generated Type.

The initial common Function Type requires an `Owned` environment, exposes only Shared call, and cannot return a borrow that depends on its hidden environment receiver. Its arguments and results need not all be owned values. Concrete Closures keep their actual receiver and lifetime contracts; see [function expressions](07-functions-and-callable-values.md#76-function-expressions) and [Callable constraints](08-generics-constraints-and-contracts.md#86-callable-constraints).

Function Type Origin elision follows §15.4. Direct borrowed inputs bind their Origins per call, and results may depend on those Origins independently of the owned environment's lifetime. Nested dependencies that are already bound remain fixed.

```text
Function Item or concrete Closure
    ├─ keep the concrete Type, including through Callable generics
    └─ convert to a fixed common Function Type when its contract is satisfied
         └─ acquire and own the erased environment and its cleanup responsibility
```

An empty environment or `func []` implies neither purity, a function-pointer ABI, absence of allocation, nor concurrency safety. Windows storage is specified in §21.2.5, independently of how functions are passed. A borrow of an existing common function value uses ordinary Semantics: `ref/F` is Copy and `uniq/F` is Non-Copy. Such a borrow neither erases a borrowed concrete Closure nor exposes additional call capabilities.

### 3.2.2. Weak reference values

`Kimi.Weak<S>` is a compiler-managed Non-Copy struct Core. After normalization, `S` must be a complete `rc/T` or `arc/T` satisfying the object View Target rules. In a generic definition, `S` is either `rc/X` or `arc/X` over an [Object Target](08-generics-constraints-and-contracts.md#8472-objectpayload) `X`, or a pair's own `s/T` whose [admitted Semantics set](08-generics-constraints-and-contracts.md#87-constraint-proof-system) is contained in {`rc`, `arc`}, which also supplies the target's pair evidence. A bare payload Core, `obj`, an object borrow, or `Weak` itself is not a valid `S`.

A Weak owns one responsibility for a particular weak management area; it never owns the payload strongly. Its outer Semantics is ordinary `owner`, and `ref/Weak<S>` borrows the Weak slot. Normal acquisition Moves a Weak, and `Kimi.Intrinsics.clone` explicitly duplicates its weak responsibility. Users cannot replace its fields or `deinit`.

Every Weak has a target management area. **There is no empty Weak and no zero-argument Weak constructor**; use `Option<Weak<S>>` with `None` for absence. An expired Weak is a present value whose target cannot be upgraded. Neither expiration nor construction state changes its Non-Copy classification. No niche or one-word Option representation is promised.

A Weak keeps `S`'s complete View Type, mode, Type/Origin arguments and actual Loan dependencies (§13.5.9). OwnedOrigins scans the full `S`, with no exception for construction or expiration. A Closure that captures an owned Weak is Non-Copy; capturing a shared borrow follows the normal Copy and Loan rules. A Weak provides no direct payload member access, object borrow, runtime `is`, checked cast or view conversion: upgrade it first and use the strong result.

### 3.2.3. Optional Type spelling

`T?` is exactly the compiler-recognized `::Kimi.Option<T>`, regardless of local name lookup. Each `?` adds one layer: `T??` is `Option<Option<T>>`, never flattened. Substitution, Identity, constraints, Copy/Owned proofs and layout use the expanded Type. `T` is any valid complete Option argument, including Semantics and Origin dependencies.

`?` binds weaker than the entire Semantics prefix chain and stronger than `->`. Group a Function Type before applying `?`; ordinary whitespace and continuation rules apply. `??` consists of two suffix tokens, not a value operator.

| Spelling | Expanded Type |
| --- | --- |
| `ref/T?` | `Option<ref/T>` |
| `ref/(T?)` | `ref/Option<T>` |
| `obj/T?` | `Option<obj/T>` |
| `obj/(T?)` | `obj/Option<T>` |
| `ref/obj/T?` | `Option<ref/obj/T>` |
| `ref/T??` | `Option<Option<ref/T>>` |
| `[N of T?]` / `[N of T]?` | `[N of Option<T>]` / `Option<[N of T]>` |
| `(T) -> U?` | `(T) -> Option<U>` |
| `((T) -> U)?` | `Option<(T) -> U>` |

`(A)? -> B` is invalid: use `(A?) -> B` or `((A) -> B)?`. A parenthesized list is a Function Parameter List only when immediately followed by an arrow under the existing continuation rules. After Semantics prefixes, an entire Function Type still needs grouping. Existing formation checks apply after expansion; enum targets are not forbidden solely because they are enums.

Resolve Origin attachment before expansion: `ref/T? during a` is `Option<ref/T during a>`, `ref/(T?) during a` is `ref/Option<T> during a`, and `View<T>{v}?` is `Option<View<T>{v}>`. A binding-set suffix belongs to a named Type: `T?{v}` is invalid; name the outer set as `Option<T>{v}`. No dependencies or Loans are erased or extended. Generic Semantics decomposition uses the expanded Type: passing `ref/i32? during a` to `<s/T>` gives `s = owner`, `T = Option<ref/i32 during a>`.

The suffix is accepted wherever general Type syntax is accepted. It does not extend dedicated name positions: runtime `is Dog?`, Case qualifiers such as `T?.Some`, and adaptation shorthand `@ref?`/`@owner?` are invalid. A constructed target `@S?` resolves `S` as a Type, never as Semantics shorthand. `x@ref/T?` targets `Option<ref/T>`: it neither borrows x nor constructs Some. Existing same-Type acquisition remains valid (§13.5).

```kimi
let missing: i32? = .None
let present: i32? = .Some(42)
let nested: i32?? = .Some(.None)
let missingBorrow: ref/i32? during static = .None
```

There is no implicit wrapping, unwrapping, default initialization or argument omission. `null` cannot construct None; for `unsafe/T?`, `.None` differs from `.Some(null)`. Layout follows §21.1.5: an `Option` whose payload is a safe reference or object handle uses the one-word nonnull representation defined there, and every other enum keeps an explicit tag.

## 3.3. Type semantics

Semantics prefixes associate to the right. A postfix `during` annotates the first explicit Semantics of the same AnnotatedType, before Optional expansion. See [nested Semantics](#336-nested-semantics-and-type-grouping) for attachment and layer boundaries.

Only a declared Semantics binding may occupy a generic Semantics position. A pair parameter `<s/T>` binds one complete Type and exposes its outer Semantics and direct target; [generic Type parameters](08-generics-constraints-and-contracts.md#81-generic-type-parameters) define this correspondence. Syntax position alone never changes a parameter's kind.

In the table, `T` denotes a Core; in object forms it may also denote a valid runtime Contract View Target.

| Category      | Semantics    | Syntax         | Layout or meaning                     |
| ------------- | ------------ | -------------- | ------------------------------------- |
| Value         | Owner        | `T`, `owner/T` | Data layout                           |
| Value Borrow  | SharedRef    | `ref/T`        | Shared borrow of a value              |
| Value Borrow  | ExclusiveRef | `uniq/T`       | Exclusive mutable borrow of a value   |
| Object        | Owner        | `obj/T`        | Exclusive object ownership            |
| Object        | Rc           | `rc/T`         | Non-atomic counted ownership          |
| Object        | Arc          | `arc/T`        | Atomic counted ownership              |
| Object Borrow | SharedRef    | `objref/T`     | Shared borrow of an object            |
| Object Borrow | ExclusiveRef | `objuniq/T`    | Exclusive mutable borrow of an object |
| Unsafe        | Pointer      | `unsafe/T`     | Unsafe pointer                        |

**Semantics categories.** The following source-level categories are closed sets. Their names occupy the Requirement role of the Type namespace when the subject is a Semantics binding; they are neither Cores nor concrete Semantics prefixes. In that role their built-in meaning cannot be shadowed; elsewhere they are ordinary contextual Names. A category cannot be used as a value Type or as a shorthand adaptation target.

| Category | Members |
| --- | --- |
| value | owner |
| valueborrow | ref, uniq |
| object | obj, rc, arc |
| objectborrow | objref, objuniq |
| borrow | ref, uniq, objref, objuniq |
| owning | owner, obj, rc, arc |
| reference | ref, uniq, obj, rc, arc, objref, objuniq, unsafe |

A Requirement on a Semantics binding may also name a concrete Semantics (`owner`, `ref`, `uniq`, `obj`, `rc`, `arc`, `objref`, `objuniq`, `unsafe`), which tests equality with it. Concrete names and categories combine under the [Requirement expression rules](08-generics-constraints-and-contracts.md#83-requirement-expressions), as in `s is ref or obj`. In generic checking, every such requirement is decided by the binding's [admitted set](08-generics-constraints-and-contracts.md#87-constraint-proof-system): the Semantics its premises allow.

`reference` includes every non-`owner` representation, including raw pointers, and establishes no safe-borrow guarantee. `s is borrow` requires an outer safe borrow; `s is owning or borrow` permits every outer Semantics except `unsafe`. These tests apply to the outer layer only; they guarantee nothing about nested Types or payloads. The category `owning` is distinct from the recursive `Owned` guarantee. There are no categories named `owned`, `counted`, `pointer`, `safe` or `all`.

### 3.3.1. Value ownership

`T` and `owner/T` are equivalent: both directly own a value with the data layout of `T`. In this section, owning a value or object describes Semantics, not the recursive [Owned capability](15-ownership-and-lifetime-analysis.md#1523-static-and-owned).

### 3.3.2. Value borrows

Value borrows provide non-owning access to the storage of their immediate complete Referent Type, subject to lifetime constraints. They do not automatically follow a pointer stored there: `ref/(rc/T)` borrows a handle slot, while `objref/T` borrows the object. Physical storage follows §21.2.4.

- `ref/T` is a shared borrow; multiple shared references may coexist.
- `uniq/T` is an exclusive mutable borrow; no conflicting reference may coexist.

A complete Sealed object payload may be selected with `@deref` and then supply an ordinary value borrow (§13.5.5.1). Its outer Loan keeps the object owner and referent dependencies. Whole-content updates follow §15.7; allocation lifetime and content lifetime remain distinct.

### 3.3.3. Object ownership

An object keeps its complete dynamic payload, Type metadata and ownership responsibility. These are logical roles, not a prescribed field order or allocation:

- `obj/T` is an exclusively owned object.
- `rc/T` uses non-atomic reference counting; the object stays alive while an owning reference exists.
- `arc/T` uses atomic reference counting. Atomic ownership management does not make concurrent mutation of `T` safe.

The Kimi [object ownership operations](13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing) create these representations from owned values and explicitly duplicate `rc`/`arc` strong owners. Release follows [object destruction](16-scope-exit-and-destruction.md#1633-ownership-object-release-and-reentry). Object ownership does not require heap allocation; any representation must preserve identity, access, lifetime and cleanup.

Concurrency and payload synchronization belong to the [deferred memory-model and thread-transfer design](appendices/D-deferred-features.md#d2-concurrency-memory-model-and-thread-transfer).

### 3.3.4. Object borrows

Object borrows provide non-owning access to objects, subject to lifetime constraints.

- `objref/T` is a shared object borrow; multiple shared references may coexist.
- `objuniq/T` is an exclusive mutable object borrow; no conflicting reference may coexist.

### 3.3.5. Object views and identity

```text
Object View Type = Object Semantics + View Target + Outer Origin (for object borrows)
Object Semantics = obj | rc | arc | objref | objuniq
View Target      = Core | runtime Contract View with fixed associated Types
```

An **Object View Type** is the complete static Type of an object handle or borrow. As in the [projection table](08-generics-constraints-and-contracts.md#811-slots-and-projections), `objref` and `objuniq` require an outer Origin, subject to elision, while `obj`, `rc` and `arc` have none. The View Target and payload dependencies remain part of the complete Type.

Every valid owner Core except Never can be a concrete payload, including non-struct Cores. A Sealed View Target proves that the payload is complete without changing the Supports relation.

References to Contract targets in the object model describe the [runtime Contract extension](08-generics-constraints-and-contracts.md#85-runtime-contracts), which lies outside the static Contracts of this revision. In `objref/Speaker`, the runtime Contract `Speaker` is a View Target, not a Core with its own data layout; the extension creates no `owner/Speaker` or `ref/Speaker`. Concrete Core targets follow the ordinary object view rules.

The **Runtime Object Type** (also **Dynamic Type** or **Dynamic Core**) is the concrete Core actually constructed. Its [Runtime Type Identity](21-layout-runtime-and-code-generation.md#2121-type-identity-and-descriptors) excludes the handle's outer Semantics and Origin bindings; static Types still keep every lifetime dependency.

A completed object has exactly one Dynamic Type, unchanged for its whole lifetime. A view determines the available operations; changing views preserves the object, its identity and its destruction responsibility. Replacing metadata or reinitializing the object as another Type is forbidden. Complete payload replacement (§15.7) preserves Identity, Dynamic Type, allocation and metadata, changing only the contents and their destruction responsibilities. A new object later placed at the same address has a new lifetime and inherits no identity, Loan or flow facts.

**`Supports(D, V)`** relates a concrete Core `D` to a View Target `V`. It holds exactly when either:

- `V` is `D` itself or a direct or indirect base Core of `D`; or
- `V` is a runtime Contract View `C` with fixed associated Types, and both `ObjectViewCompatible(C)` and `Implements(D, C)` hold under [runtime Contracts](08-generics-constraints-and-contracts.md#85-runtime-contracts).

Upcasts, view-support tests, checked casts and metadata all use this relation. It grants no access, ownership, `Owned`, Origin/Loan validity, or permission to invoke an incompatible ordinary member. Generic element relationships do not imply container covariance. Core inheritance alone does not make complete Types substitutable: there is no slicing and no implicit `owner/Dog -> owner/Animal`, ordinary `ref/Dog -> ref/Animal` or `uniq/Dog -> uniq/Animal` conversion. Object upcasts are explicit operations; [inherited receiver projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection) supplies only the receiver of a selected inherited member.

```text
objref/Animal during source       Dog object
    ├─ public view: Animal         ├─ Animal state
    ├─ shared access               ├─ Dog state
    └─ Origin: source              └─ Dog type and destruction information
```

### 3.3.6. Nested Semantics and type grouping

Borrow Origins are postfix annotations. The relevant grammar is:

```ebnf
Type          := FunctionType | AnnotatedType
AnnotatedType := SemanticsType ("?")* BorrowOrigin?
SemanticsType := Semantics "/" SemanticsType | TypeAtom
TypeAtom      := CoreType | "(" Type ")"
BorrowOrigin  := "during" OriginAtom
```

An annotation targets the **first explicit Semantics in that AnnotatedType**, only when its SemanticsType starts with `Semantics "/"`. A TypeAtom has no target. Do not search through grouping, aliases, named Types, arguments, elements or function signatures, or remove `owner` to find a target. Fix attachment syntactically, expand Optional, then apply ordinary formation and use checks. Only safe-borrow Semantics accept the annotation (§15.3.1); `during` adds no Type layer or value operation.

Written order is **body, `?` suffixes, `during`**; application order is **body, Origin annotation, Optional wrapping**. One AnnotatedType admits at most one annotation. Grouping cannot reannotate an inner layer, and errors never overwrite an existing annotation or imply an intersection.

| Type spelling | Meaning or rejection |
| --- | --- |
| `ref/T? during a`, `(ref/T during a)?` | `Option<ref/T during a>` |
| `ref/T?? during a` | `Option<Option<ref/T during a>>` |
| `ref/(T?) during a` | `ref/Option<T> during a` |
| `ref/(uniq/T during b) during a` | Separate outer a and inner b |
| `unsafe/ref/T during a`, `owner/ref/T during a` | Invalid target Semantics; use `unsafe/(ref/T during a)` or `owner/(ref/T during a)` for the inner borrow |
| `(ref/T)? during a`, `(ref/T?) during a`, `Option<ref/T> during a`, `T during a` | No syntactic target |
| `ref/T during a?`, `ref/T during (a and b)?`, `ref/T during a during b` | Invalid suffix order or extra annotation |
| `(ref/T during a) during b` | No target outside the grouping |

Type suffixes never search inside grouping. `?` wraps the preceding complete Type. A binding set attaches only to a named Type: `View<T>{v}?` and `(View<T>{v})` are valid, while `View<T>?{v}`, `(View<T>){v}` and `View<T>{v}{w}` are not. In `ref/View<ref/U during c>{v} during a`, a describes the outer borrow, c the Type argument, and v names only View's schema bindings.

Each tuple/array element and Type argument has its own Type expression. A function's trailing `during` belongs to its result: `(A) -> ref/B? during b` returns `Option<ref/B during b>`. Borrowing the function value requires `ref/((A) -> B) during a`; `((A) -> ref/B) during a` has no target.

Use existing header and delimiter continuation rules for long Types; `during` cannot continue a closed header by itself:

```kimi
func borrow<T>(x: ref/Long<T>)
    -> ref/Long<T> during x
    return x

func borrowWrapped<T>(x: ref/Long<T>)
    -> (ref/Long<T>
        during x)
    return x
```

`ref/V`, `uniq/V` and `unsafe/V` may take a complete value Type `V`, including its own Semantics and Origins, as their immediate **Referent Type**. The outer layer refers to storage holding a value of `V`; it neither replaces `V`'s Semantics nor refers directly to `V`'s eventual referent.

```kimi
ref/ref/T                           // Shared borrow of a shared-reference value.
ref/uniq/T                          // Shared borrow of an exclusive-reference value.
uniq/ref/T                          // Exclusive borrow of a shared-reference value.
ref/obj/T                           // Borrow of object-handle storage, not objref/T.
unsafe/ref/T                        // Raw pointer to shared-reference storage.
ref/(ref/T during inner) during outer    // Separate inner and outer Origins.
```

Prefixes associate to the right: `ref/ref/T during outer` annotates only the outer reference. Parentheses group complete Types and permit per-layer annotations. `ref/((i32) -> bool)` borrows a function value, while `(ref/i32) -> bool` takes one borrowed integer; `ref/(i32) -> bool` is invalid because a Function Type needs its own parameter list. Grouping adds neither a Tuple nor a borrow.

For identity, applicability, layout and Origin analysis, aliases are expanded and every Type layer is preserved: `ref/ref/T` differs from `ref/T`. Grouping and redundant `owner` prefixes normalize away, but `owner/V` keeps `V`'s references and Object Semantics. Object Semantics require an [Object Target](08-generics-constraints-and-contracts.md#8472-objectpayload): an ObjectPayload Core, a runtime Contract View Target or a pair target with pair evidence. A Type that already has Semantics applied or a Core that opts out of ObjectPayload is not one: `ref/obj/T` is valid, while `obj/ref/T` does not box a reference. Generic substitutions obey the same rules; see [generic slots](08-generics-constraints-and-contracts.md#81-generic-type-parameters).

Each Type layer keeps its Origin dependencies under the [position-specific elision rules](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision); an outer annotation cannot replace an inner one. In parameter Types, an outer direct borrow and each omitted aggregate slot introduce independent input Origins under §15.4; nested borrow layers still require explicit bindings. Local initializers may infer all layers, and instance Fields require explicit bindings throughout. The examples above illustrate composition, not unrestricted Origin omission.

At each safe value-borrow layer, all observable Origins of `V` must outlive that layer (`origin inner outlives outer` in the last example). Grouping or a redundant `owner` cannot add a conflicting annotation to one normalized layer. Raw pointers establish neither safe-reference validity nor longer lifetimes.

The outer Semantics determines Copy: `ref/uniq/T` is Copy, while `uniq/ref/T` is not. Copying an outer shared reference does not duplicate the inner exclusive capability. Shared access cannot Move a Non-Copy inner value, use its `uniq` exclusively, or mutate the slot; a shared Reborrow stays within the outer Loan. Moving or destroying an outer reference leaves its referent intact. A valid exclusive slot replacement must preserve the inner Type, Origin and Loan constraints.

**Selecting and reading a referent.** A reference value is not read as its referent by default. The Place a `ref/T` or `uniq/T` value points to is selected explicitly with the postfix [dereference `@deref`](13-operators-and-assignment.md#13551-dereference), or implicitly by the [reference-path selection](#341-reference-path-selection) of member, index and receiver positions and of structural Patterns. Only a **Scalar** referent is read implicitly, by the [Scalar read](#353-scalar-read) at positions that require a Scalar, following every safe reference layer to its terminal Scalar. A Non-Copy referent is never extracted through a reference ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)); it is inspected through the reference by the selected part, by [comparison](13-operators-and-assignment.md#134-comparison-and-logical-operators) or by a borrow of the selected Place.

There is no safe `*` operator and no conversion between pointers and safe references. Borrow formation follows the explicit rules for [Borrow](13-operators-and-assignment.md#1355-dereference-borrow-and-reborrow), which always borrow the immediately written slot, and the implicit [common adaptation](10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types) at positions with a fixed expected Type.

## 3.4. Values, places, and storage

**Storage** is an identifiable region that holds values: a local, a part of an aggregate, the payload of a separate object, or the storage of a materialized temporary. **Destruction responsibility** is the obligation to destroy an owned value at the end of its lifetime, subject to [cleanup and termination](16-scope-exit-and-destruction.md#16-scope-exit-and-destruction).

A **Place** is the result of selecting Storage: it carries the stored **complete Type** (Semantics, Type arguments, nested structure and internal Origins), the access path, the capabilities of that path and the dependencies needed to keep the Storage valid. A **Place expression** designates one, and parentheses preserve that classification. A Place is distinct from a **Temporary Value**, even when that value is materialized into a separate [Temporary Place](#36-temporary-values-places-and-lifetimes). A Place is not a value: it cannot be stored in a variable, passed as an argument, bound to a Type argument or wrapped in `Option`; to keep a location, borrow it and store the reference. Zero-sized Storage has a logical location without a distinct physical address.

**Completeness.** A value is complete when every part needed to treat it as one value is Initialized. A value of a fully determined Type may still be incomplete after a [Partial Move](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move).

**Capabilities.** A Place offers up to three independent capabilities; none includes another:

| Capability | Meaning |
| --- | --- |
| Read | Observe the value: Copy it, borrow it for shared access, compare or inspect it |
| Write | Initialize or replace the value |
| Take | Extract the value together with its destruction responsibility |

An owned `let` root offers Take but not Write, and the referent of `uniq/T` offers Write but not Take. A child Place is derived from its parent's path and the child's declaration: it keeps the child's declared complete Type, never adopts the parent's Semantics, never recovers exclusive capability through a shared path, and keeps the access, Property and owner-protection restrictions of every layer. An active Loan restricts uses of a capability; it does not change the capability itself.

Distinct Fields, distinct Tuple positions and distinct in-range constant fixed-array indices designate non-overlapping Places (§15.6.2). Parentheses do not change the classification. Runtime indices, different Dictionary keys, spare capacity or different calls do not by themselves prove non-overlap.

An **Access Designator** identifies an access target after Name and Type resolution: a local or parameter, Field, computed or required Property, Tuple element, index, or built-in dereference. It includes computed Properties and user indexers, without promising storage or Consume permission. Resolution of a designator has one of three outcomes:

```text
Access Designator -> operation-specific resolution
                    ├─ Place access
                    ├─ value acquisition, such as a getter result
                    └─ error
```

These are alternative outcomes, not fallback stages: a failed direct Move cannot become a borrow or a getter call. Standard Property `get` exposes the permitted Place operations; custom, computed and required `get` produce their declared result (Chapter 11). Permitted function references produce function values. Classification follows the resolved operation, not the spelling, and parentheses preserve it.

**Access paths.** A Place is reached through an access path, which is classified independently of the Place's Type:

| Path | Definition |
| --- | --- |
| Direct | Resolves no reference: a local or parameter, its inline parts, static storage, or a payload reached through an owned `obj` handle |
| Through an exclusive reference | Resolves a `uniq` or `objuniq` reference, including the result of a getter that returns one |
| Through a shared reference | Resolves a `ref`, `objref`, `rc` or `arc` reference |

A **borrow value** is an expression whose outer Semantics belongs to the `borrow` category (§3.3); a stored reference is both a borrow value and, through its path, a Place. The path's authority bounds every borrow of the Place ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-movable-places), [§13.5.5](13-operators-and-assignment.md#1355-dereference-borrow-and-reborrow)); a temporary is not a Place and has no path.

**Value kinds.** For acquisition, the source of an expression is classified in the following order. The access path is used only for permissions and for the Movable Place rules (§15.1.5); it does not change the value kind.

| Value kind | Definition |
| --- | --- |
| Borrow value | A value of `ref`, `uniq`, `objref` or `objuniq` Type, whether stored in a Place (`var r = x@uniq` makes `r` one, as does a field `link: uniq/T`) or returned by a call |
| Owned Place | A Place holding an owned value or an owned handle (`obj`, `rc`, `arc`), whatever its access path |
| Owned temporary | A Temporary Value that is neither of the above |

An **object-kind input** is an owned handle or an `objref`/`objuniq` value; every other input is a **value-kind input**.

A **Receiver Expression** is the expression in the receiver position of a call: a method call, a custom, computed or required accessor, or a direct call of a Closure or function value ([§7.3](07-functions-and-callable-values.md#73-explicit-receivers), §7.6.3, §11.2). It may be a Place or a temporary, and it is acquired implicitly under §7.3 whatever its value kind. The `self` argument of an unbound call `Type.method(x, ...)` is an argument, not a Receiver Expression, and an assignment target is not one either (§13.7). The Subject Place of `match` and `for` (§15.1.6) is a separate notion.

The **lending point** of a borrow, Reborrow or projection is the storage it targets: for an owned Place, the Place reached by following standard field, Tuple-element and array-element projections that call no user code as far as they extend; for an owned temporary, that temporary; for a borrow value, its referent; for an object borrow, the object, and for a dereferenced payload, the payload; for a base projection (§9.5.1), the selected base subobject. Reference slots and owners that are protected to keep the target valid follow the dependency rules of §15.6.7 and are not part of the lending point.

**Initialization** places a value in an empty Place. **Destruction** ends a value's lifetime and its responsibility; if the storage remains, that Place becomes Uninitialized after normal completion. Nothing can access storage after the storage's own lifetime ends.

Value lifetime separates three steps: acquisition (Copy or Move), placement (Initialization or Replacement) and Destruction. One assignment may contain both a Move and a Replacement.

An ownership Move transfers ownership and destruction responsibility, preventing a second destruction at the source. Copying or moving a borrow value duplicates or transfers its access capability without owning the referent; destroying a borrow does not destroy the referent. Destroying `rc/T` or `arc/T` releases an owning reference under the object lifetime rules. A non-owning `unsafe/T` neither destroys its referent nor frees its storage. Move, Take and Consume are distinct notions: Take is a Place capability, Move is the transfer of a value and its responsibility, and Consume is the analysis judgment that records an acquisition's effect.

The [initialization-state rules](15-ownership-and-lifetime-analysis.md#1511-storage-state-and-responsibility) define Initialized, Uninitialized and Moved. A **Partial Move** transfers an inline part and leaves the aggregate incomplete; supported paths and permissions follow [Move Paths](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move).

### 3.4.1. Reference-path selection

A member access, an index expression and the receiver of a method call select a Place from the declaration and the Type of their operand. If the operand's current layer is a safe value reference (`ref` or `uniq`) that has no such member, index or receiver declaration, selection continues at its immediate referent, layer by layer, and stops at the first layer that has the declaration. Selection is decided from Types alone: a later acquisition, adaptation or Loan failure never returns to another layer. Object Semantics keep the object view rules of §12.4.3–4; a complete Sealed payload is selected under the [dereference rules](13-operators-and-assignment.md#13551-dereference).

```kimi
func update(node: uniq/Node)
    node.count += 1       // Selects the Field through the reference.
    node.validate()       // Borrows the receiver as the selected declaration requires.
```

The same selection applies to the receiver of a borrowing `for` entry (§14.6.2) and to structural Patterns, which stop at the first layer that has the required structure (§14.8.1). A Scalar read and a comparison instead select the terminal of the reference chain (§3.5.3, §13.4). Each selected layer keeps its own capabilities, Origins and Loans; exclusive capability is never recovered through a shared layer.

In a generic body, a Type parameter or associated Type whose shape is not determined by the public Constraints and Type equalities, after normalization, is the terminal of the path: only the published capabilities are used there, and instantiation never selects a different layer, member or comparison. A reference shape proven by a declared equality may be followed at definition time.

Bare acquisition and a single-name binding never select a referent. `@ref` and `@uniq` are not subject to selection: they always borrow the immediately written slot (§13.5.5). An update requirement, such as the exclusive access needed by `matrix[f()][j] = value`, propagates only along the selected projection path, never into the argument `f()` or behind an ordinary function or getter.

## 3.5. Copy and move

**Copy** implicitly duplicates a value, leaves its source Initialized and keeps the source's destruction responsibility. It executes no user-defined code, heap-allocating duplication, reference-count change or resource acquisition. Copying a reference does not copy its referent.

**Move** transfers the value and its destruction responsibility, or a borrow value's access capability, and marks the source Moved. It invokes no user code and need not clear the source memory.

**Bare acquisition** is the acquisition of an expression written without an explicit `@` operation. Let the Place have the stored complete Type `T`:

| Operation | Result |
| --- | --- |
| Bare acquisition of a Place | Copy, only when `T` is proven Copy; the result Type is `T`. A Non-Copy or Copy-unproven Place is an error |
| `P@move` | Transfer of `T` whatever its Copy capability; the source becomes Uninitialized |
| `P@ref`, `P@ref/T` | Shared borrow of `P` itself: `ref/T` |
| `P@uniq`, `P@uniq/T` | Exclusive borrow of `P` itself: `uniq/T` |
| Bare acquisition or `@move` of a temporary | The already acquired value is transferred; no extra Copy or Move |

`@move` is the only spelling that transfers a value out of a Place ([transfer operation](13-operators-and-assignment.md#1353-defined-adaptations)); it requires a Take-capable [Movable Place](15-ownership-and-lifetime-analysis.md#1515-movable-places). `@owner`, `@obj`, `@rc`, `@arc` and their full forms are not transfer spellings: they perform the same-Type acquisition or the explicit adaptation they name under the ordinary acquisition rules. A Temporary Value transfers its ownership without any spelling. These rules apply to initialization, assignment sources, by-value arguments, by-value results, aggregate elements, payloads and defaults. When the position has a fixed expected Type, the [common adaptation](10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types) is planned first and the value is acquired once; without an admitted adaptation, an unproven Copy is never replaced by an implicit Move or borrow.

A typed borrow `P@ref/T` requires the written `T` to be exactly the stored Type; it is neither a selection of the referent, a Copy of a same-Type reference nor a Reborrow, and the outer Origin is inferred from the path while the stored Type's internal Origins are kept. An explicit borrow of a temporary materializes it under the ordinary temporary lifetime and borrows that Temporary Place.

```kimi
// r: uniq/Node
r@ref                 // ref/(uniq/Node): the slot of r
r@ref/uniq/Node       // The same slot borrow
r@deref@ref           // ref/Node: a shared Reborrow of the referent
// r@ref/Node         // Error: the stored Type is uniq/Node
```

Copy capability is independent of `let`/`var` and of flow-dependent Loans. At a use site, Copy obeys read restrictions, and a transfer must not conflict with overlapping active Loans. Both preserve Origin dependencies without extending referent lifetimes. Reborrowing does not make exclusive references Copy. The distribution of an already acquired owned Subject or iteration item into Pattern bindings is a transfer inside that construct and needs no `@move` per binding (§15.1.6).

```kimi
let a: i32 = 10
let b = a                 // Copy; a stays Initialized.
var node: obj/Node = makeNode()
let owned = node@move     // Transfer; node becomes Moved.
// let bad = node         // Error: a bare Non-Copy Place never Moves.
// use(node)              // Error until reinitialized.
node = makeNode()
let gone = a@move         // Transfer of a Copy value; a becomes Moved.
```

### 3.5.1. Copy capability and explicit duplication

`Copy` is a [compiler-intrinsic Contract](08-generics-constraints-and-contracts.md#847-intrinsic-contracts-and-guarantees). A user-defined Contract with the same shape does not grant Copy acquisition semantics.

Complete Types are classified by Core, Semantics and stored components:

| Type | Classification |
| --- | --- |
| Integers, floating-point values, `bool`, `char`, Unit under `owner` Semantics | Copy |
| `ref/T`, `objref/T`, `unsafe/T` | Copy regardless of the referent `T` |
| `uniq/T`, `objuniq/T` | Non-Copy |
| `obj/T`, `rc/T`, `arc/T` | Non-Copy, even if `T` is Copy |
| `Slice<T>{source}` | Copy shared handle regardless of `T`; no exclusive-element Slice exists |
| `Index`, `Range`, `ResolvedRange` | Copy |
| Function Item | Copy |
| Concrete Closure | Copy exactly when every captured complete Type is Copy; empty environments qualify |
| Common Function Type under `owner` Semantics | Non-Copy regardless of its hidden environment |
| `Kimi.Weak<S>` under `owner` Semantics | Non-Copy in every state, including expiration; explicit duplication keeps weak-management storage |
| Tuple or fixed-length array under `owner` Semantics | Copy exactly when every component Type is Copy |
| User-defined struct under `owner` Semantics | Non-Copy unless it explicitly opts in |
| Enum under `owner` Semantics | Non-Copy unless it explicitly opts in under [enum Copy](#352-enum-copy) |
| `Array<T>` or `Dictionary<K, V>` under `owner` Semantics | Non-Copy regardless of contents |
| `owner/string` | Non-Copy regardless of its internal representation |

Never has no values and needs no classification. Other Types require their own rules; sharing elements alone does not establish Copy.

`Copy` is compiler-checked. A struct opts in with `Self is Copy`, or conditionally with `Self is Copy when P` (§8.4.8). Under the Type Constraints and any declared conformance condition, every complete own Field Type and the direct base must be Copy, and the struct must have no user `deinit`. Checking the inline base covers inherited storage and destruction. Each derived struct opts in separately; open structs follow the same rules. Computed members contribute no fields. All-Copy fields alone do not opt in, users cannot define Copy bodies, and Copy preserves the exact owning Type without slicing. Active Loans affect use, not Type classification.

```kimi
struct Point
    Self is Copy
    var x: i32
    var y: i32

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)
```

`T is Copy` is a generic constraint; Copy must not be assumed before constraints or instantiation establish it. User-struct derivation is specific to `Self is Copy`, not a general consequence of `Self is Capability`. Compiler-generated Closures use the automatic rule in the table instead.

Unknown Copy capability follows [generic access effects](08-generics-constraints-and-contracts.md#89-generic-access-effects), which preserve conditional acquisition plans and separate shared element-read rules.

Duplication that allocates, increments a reference count or duplicates a resource requires an explicit operation. `rc`/`arc` handles and Weak use [`Kimi.Intrinsics.clone`](13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing); no general duplication API is defined.

### 3.5.2. Enum Copy

`Self is Copy` requests unconditional compiler-derived Copy conformance for an enum; `Self is Copy when P` requests it under `P` (§8.4.8). Every complete payload Type of every Case must be Copy under the applicable premises. Payload-free enums also require the opt-in, and users cannot supply a Copy body. Copy duplicates the active Case and payload without user code; Move transfers responsibility for the whole enum.

```kimi
enum Direction
    Self is Copy
    North
    South

enum CopyOption<T>
    Self is Copy when T is Copy
    Some(T)
    None
```

Derivation must hold for every generic binding admitted by the Type Constraints and by the conformance condition, if any; it never invents parameter constraints. `CopyOption<i32>` is Copy, while `CopyOption<Resource>` remains usable but is Non-Copy when `Resource` is Non-Copy. An unconditional declaration without a sufficient premise is invalid. An enum storing only `ref/T` needs no `T is Copy` premise, but any explicitly written condition still applies.

Proof may depend on declared constraints, but successful individual instantiations do not validate an otherwise unproven declaration. [Generic access effects](08-generics-constraints-and-contracts.md#89-generic-access-effects) may defer exact effect determination only after legality is proven for every admitted case; unknown Copy is never treated as Non-Copy.

### 3.5.3. Scalar read

At a position that requires a Scalar Type `T` (§3.1), a value whose Type is `ref` or `uniq` layers ending in `T` supplies `T` by a **Scalar read**: the safe reference layers are followed to the terminal Place and its value is copied. The read applies at the value positions of [common adaptation](10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types), to built-in operator operands and to `bool` conditions. An operator selects its operation from the terminal Scalar Type of each operand. An unknown generic Type is never assumed to be a Scalar.

```kimi
for number in numbers        // numbers: Array<i32>; number: ref/i32.
    let reference = number   // Copy of the reference; the binding keeps ref/i32.
    let snapshot: i32 = number
    let doubled = number * 2 // i32.
    total += number

for number in numbers@uniq   // number: uniq/i32.
    number@deref = number + 1 // The target is selected explicitly; the right side is a Scalar read.

for number in references     // references: Array<ref/i32>; number: ref/(ref/i32).
    total += number          // Reads the terminal i32.
```

- Only safe value-reference layers are followed. Object Semantics, raw pointers and Fields are not followed, and every layer is checked for initialization, capability and Loans.
- No numeric conversion is added; an unresolved literal is fitted to the terminal Scalar Type. Overflow, operator availability and short-circuit evaluation are unchanged.
- Every read takes the value at that evaluation; nothing is snapshotted at binding time.
- The target of an assignment, compound assignment, increment or decrement is never redirected to a referent: `number += 1` on `number: ref/i32` is an error, and `number@deref += 1` updates the referent.
- The original reference keeps its Loan, and the read result carries no new borrow.
- A non-Scalar Copy referent is copied only through an explicit `@deref` selection; comparisons follow the shared inspection rule of §13.4.

## 3.6. Temporary values, places, and lifetimes

### 3.6.1. Materialization

| Term | Meaning |
| --- | --- |
| Temporary Value | An expression's temporary result; not its original persistent Place |
| Temporary Place | Anonymous storage holding that value |
| Materialization | Giving a Temporary Value a stable Temporary Place when an operation needs storage |

Unqualified *temporary* means Temporary Value. Merely using physical storage does not make a result its source Place.

```text
Temporary Value
    -> materialize if stable storage is needed
       -> Temporary Place
          ├─ permitted Borrow / Reborrow
          └─ permitted field / element operations
```

Materialization neither reevaluates the expression nor adds a Copy, resource duplication, reference-count increment, heap allocation or lifetime extension. It keeps the same value and destruction responsibility. It must not turn a failed Consume into a Read or restore a Moved source.

A newly owned temporary has exclusive writable capability over its whole Temporary Place unless another rule restricts access; it needs no `let`/`var` binding. Temporaries owning a getter result have the additional restrictions of [getter results](11-properties.md#1123-getter-results-and-temporaries). Materialization realizes this capability without upgrading borrows, granting referent or Property permissions, ignoring read-only parts, or bypassing Loans, Origins, construction or `deinit` conditions.

### 3.6.2. Lifetime and borrowing

Unless a construct needs a longer lifetime, a temporary lasts until the outermost expression that created it finishes. Argument temporaries last through the call; iteration sources and `match` subjects last for their required use. Remaining temporaries are destroyed in reverse creation order. After a Move, the transferred value follows its destination's lifetime, while the original Temporary Place keeps its own lifetime and destroys only its remaining Initialized parts.

Each `if`, `else if`, `while`, `require` and match-guard test is a temporary-lifetime boundary: the bool result is secured, condition temporaries and guard-local temporary Loans are cleaned up, and then control branches, under [condition evaluation](14-control-flow.md#1423-conditions-and-temporary-lifetimes). Explicit bindings, match subjects and iterators keep their own scopes, and Moved values follow their destination lifetimes. To keep a value alive through a body, store it outside the condition.

A new borrow of an owned temporary depends on its Temporary Place and cannot outlive it. Exclusive capability permits the applicable explicit exclusive borrow; it does not bypass the [Borrow table](13-operators-and-assignment.md#1355-dereference-borrow-and-reborrow).

```kimi
inspect(makeResource())       // Shared borrow of the temporary (§10.2).
modify(makeResource()@uniq)   // Exclusive borrow of a temporary is explicit.
let taken = resource@move     // Non-Copy Resource transfers to taken.
inspect(taken)                // Borrow the destination; resource remains Moved.

let view = makeResource()@ref
// inspect(view) // Error: borrowed temporary expired after the initializer.

let owned = makeResource()
let lastingView = owned@ref // Borrow the retained local instead.
inspect(lastingView)
```

A temporary that is already a borrow is acquired as that reference value, keeping its referent Origins; it is adapted at a position with an expected Type under §10.2. An explicit `@ref` on it borrows the storage that materializes the reference value and adds a layer.

```kimi
let view = makeView()      // If makeView returns ref/T, view is ref/T with its Origins preserved.
inspect(makeView())        // Passed to ref/T as is.
```

Borrow and Slice formation never extend the source's lifetime. Result transfers secure their values before the common [scope-exit cleanup](16-scope-exit-and-destruction.md#162-scope-exit-destruction); Abort follows [Abort termination](17-failure-handling.md#173-abort-termination).

## 3.7. Origins and loans: overview

Kimigayo uses **Origins** instead of lifetime variables. An Origin describes how long a borrow remains valid; a **Loan** records which Place is borrowed and whether the borrow is shared or exclusive.

The declaration-attached relation `origin a outlives b` requires `region(a) ⊇ region(b)`; `origin a == b` requires equal regions. See [Origin schemas and relations](15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations) and [ordering](15-ownership-and-lifetime-analysis.md#1522-ordering-and-intersection).

Origin annotations appear in signatures and Type declarations. Local Origins may be inferred from initializers and ordinary Origin and Loan constraints. Omission is permitted only under the [position-specific rules](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision): direct borrowed inputs and omitted input aggregate slots introduce input Origins, results use conservative elision, and instance Fields require explicit Origin bindings. Static Fields require `Owned` and the [static-source rules](11-properties.md#1132-static-storage).

The safe value borrows are:

```kimi
ref/T during o   // shared, immutable, and aliasable
uniq/T during o  // exclusive and mutable
```

`uniq/T` is not implicitly copyable and cannot coexist with another overlapping borrow. The object borrows `objref/T` and `objuniq/T` follow the same shared and exclusive rules; examples in this section use `ref` and `uniq`.

## 3.8. Type relations and expression operations

Type relations and expression operations are distinct, even when optimized away: Borrow, Reborrow, Copy and Move remain value operations, and a Type relation alone authorizes no acquisition. The table summarizes the linked rules; it adds no conversions, adaptation chains or overload preferences.

Here `A <: B` covers normalized identity and the explicitly defined subtype rules. Complete Types keep their Semantics, nested Types, generic arguments and Origin bindings. Unresolved generic or Origin information keeps its constraints for later resolution; it is not evidence that Types are identical or compatible.

| Relation or operation | Inputs and normative condition | Effect and boundary |
| --- | --- | --- |
| Normalized Type identity | Two complete Types. Expand aliases, normalize grouping and redundant `owner` prefixes, and compare the resulting structure, declaration identity, generic arguments, Semantics and Origin bindings. Bound Origin parameters correspond by binder, not spelling. | No value operation. Different nominal declarations never become equal through matching names, fields or layout. This is not the Origin-erasing Runtime Type Identity used by object views. |
| Alias equivalence | An alias and its resolved target, with substitutions and complete Type information preserved. | Part of normalized identity; no wrapper, conversion or ownership change. Alias lookup still follows the ordinary visibility and lookup rules. |
| Subtyping | Two complete Types under the established generic and Origin constraints. Prove identity or a subtype relation explicitly defined by this specification. | Static fitting only. The proof inserts no acquisition, Borrow/Reborrow, dereference, numeric conversion, object upcast or user conversion. |
| Origin shortening and variance | Apply [Origin variance](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins) and outlives constraints at each relevant position. Covariance permits shortening, contravariance reverses the relation, and invariance requires equality. | A subtype proof, not a new borrow. Existing dependencies and Loans are preserved; lifetimes are not extended, and inner Origins are not replaced by an outer annotation. Exclusive Referent Type invariance remains mandatory. |
| Callable signature compatibility | For implementation `(A1, ..., An) -> R` and requirement `(P1, ..., Pn) -> Q`, apply [callable compatibility](10-overload-resolution-and-inference.md#107-callable-signature-compatibility): equal arity, `Pi <: Ai`, `R <: Q`, and compatible Origin/Loan contracts. | Static signature fitting. It inserts no argument or result operations and does not itself convert a Function Item or Closure to a common Function Type. Receiver and environment requirements remain separate. |
| Expected-result compatibility | An instantiated candidate result Type and an independently established expected Type, under [expected-result filtering](10-overload-resolution-and-inference.md#103-expected-results). Requires identity or a defined subtype relation. | Excludes candidates without inserting a value operation. Result acquisition and declared Loan propagation are still required; this is not general implicit adaptation. |
| Never fitting | Never has no normally produced value and fits any otherwise valid expected value Type without a value conversion. | No outer value operation executes on a non-completing path. Target, Unsafe and local correctness checks remain, and transfer operands are validated against their own result boundary under [result validation](14-control-flow.md#149-result-validation). The Target Result Type stays separate from inferred Never; every syntactic result source is checked under §14.9. |
| Implicit expression adaptation | An expression, a fixed expected Type and use-site context. Exactly one operation of the [common adaptation table](10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types) or the fixed-expectation [common function conversion](07-functions-and-callable-values.md#764-function-references-and-common-type-conversion) is selected. | May require acquisition, Borrow/Reborrow, a Scalar read or a defined conversion. Literal fitting determines the Type of an unresolved literal; it does not convert an established numeric Type. Adaptations are never chained, and no universal implicit-conversion search exists. |
| Explicit expression adaptation | An expression and a resolved Adaptation Target in context, or the postfix dereference `@deref`. Select one [defined `@` operation](13-operators-and-assignment.md#1353-defined-adaptations), then enforce its requirements and static result fitting. | May select a Place, change value representation, view or Loan state, or perform runtime checks. No hidden sequence of operations is inserted. Origins are inferred as specified for Adaptation Targets. |
| Object upcast | An expression and a different object View Target, with proof of `Supports(S, V)` and a matching [explicit upcast row](13-operators-and-assignment.md#1357-object-upcasts). | One explicit view/acquisition operation. Inheritance or conformance alone establishes neither an implicit complete-value adaptation nor a callable argument/result conversion. |
| Base subobject receiver projection | An instance member selected in a base layer by [inherited lookup](09-names-signatures-and-access.md#951-base-subobject-receiver-projection). | Locates the receiver subobject and applies the permitted receiver access; no standalone conversion or owning base value. |
| Borrow / Reborrow | An expression with the required Place, access and Loan properties, and a permitted implicit or explicit borrow operation. | Establishes or derives Loans under the borrow rules. Changing `uniq/T` to `ref/T` requires a shared Reborrow, selected implicitly at an expected `ref/T` or written `@deref@ref`; it is not a subtype rule. |
| Numeric conversion | An established numeric Type and a target admitted by the [explicit numeric conversion table](13-operators-and-assignment.md#1354-numeric-conversions-and-literals). | A value conversion with the specified rounding, range checks and failure behavior. `i32` is not a subtype of `i64`; representable literal fitting is separate. |
| Acquisition legality | An expression, the selected access operation, and the current initialization, access, ownership and Loan state. Apply the ordinary Copy, Move, Borrow or Consume requirements. | Type compatibility does not prove legality: an exact Type match can still fail because storage is Moved, access is unavailable or a Loan conflicts. Such a failure does not reopen committed lookup or overload selection. |

For example, `ref/T during longer <: ref/T during shorter` may hold when `longer : shorter`, without a new Loan. In contrast, `uniq/T` to `ref/T` needs a Reborrow, and an object view change needs its explicit upcast. The same normalized Type does not force an identity operation: explicit same-Type exclusive adaptation still selects Reborrow, and by-value acquisition Copies when bare and transfers under `@move`.

Candidate analysis may record operation choices and unresolved obligations but must not commit source-state changes while testing candidates. After selection, the chosen acquisition and adaptation are enforced in the specified evaluation order, and static result fitting is applied without replacing that operation. The [implementation correspondence](appendices/B-reference-models.md#b5-type-relation-and-operation-plans) is informative; no particular internal API is required.
