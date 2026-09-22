# 3. Types and values

[Specification index](../SPEC.md)

An ordinary **Type** combines Semantics, a Core and any required Origins. Its single-layer form is:

```text
Type = Semantics{Origin}/Core
```

| Term | Meaning |
| --- | --- |
| Type | The complete type, including Semantics, its target, and all Origin dependencies. |
| Semantics | The value's representation, ownership, borrowing, access and safety rules. |
| Core | The component that defines the value's kind, structure and identity. |
| Origin | The set of program points where a borrow is guaranteed valid. It constrains borrows and the values that retain them. |

For example, `ref{source}/Dog` has Semantics `ref`, Core `Dog` and Origin `source`. Semantics and Origins may be omitted only under the inference and elision rules, and not every layer has an Origin.

A Core is distinct from the **outer** Semantics and Origins. Its elements and generic arguments may be complete Types: the Core of `owner/(i32, ref{source}/Dog)` is a Tuple whose second element keeps its borrow and Origin.

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
   ├─ Derived{a} borrow source
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

`Kimi.Weak<S>` is a compiler-managed Non-Copy struct Core. After normalization, `S` must be a complete `rc/T` or `arc/T` satisfying the object View Target rules. A generic definition needs the same evidence: for a pair `<s/T>`, `s` must be `rc` or `arc`. A bare payload Core, `obj`, an object borrow, or `Weak` itself is not a valid `S`.

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

Resolve Origin attachment before expansion: `ref{a}/T?` is `Option<ref{a}/T>`, `ref{a}/(T?)` is `ref{a}/Option<T>`, and `View<T>{v}?` is `Option<View<T>{v}>`. A binding-set suffix belongs to a named Type: `T?{v}` is invalid; name the outer set as `Option<T>{v}`. No dependencies or Loans are erased or extended. Generic Semantics decomposition uses the expanded Type: passing `ref{a}/i32?` to `<s/T>` gives `s = owner`, `T = Option<ref{a}/i32>`.

The suffix is accepted wherever general Type syntax is accepted. It does not extend dedicated name positions: runtime `is Dog?`, Case qualifiers such as `T?.Some`, and adaptation shorthand `@ref?`/`@owner?` are invalid. A constructed target `@S?` resolves `S` as a Type, never as Semantics shorthand. `x@ref/T?` targets `Option<ref/T>`: it neither borrows x nor constructs Some. Existing same-Type acquisition remains valid (§13.5).

```kimi
let missing: i32? = .None
let present: i32? = .Some(42)
let nested: i32?? = .Some(.None)
let missingBorrow: ref{static}/i32? = .None
```

There is no implicit wrapping, unwrapping, default initialization or argument omission. `null` cannot construct None; for `unsafe/T?`, `.None` differs from `.Some(null)`. No layout/allocation guarantee is added; §21.1.5 still requires explicit enum tags.

## 3.3. Type semantics

Semantics prefixes associate to the right, and an unparenthesized `{Origin}` annotates the outermost layer. See [the start of this chapter](#3-types-and-values) for the basic form and [nested Semantics](#336-nested-semantics-and-type-grouping) for layer boundaries and permitted combinations.

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

A Requirement on a Semantics binding may also name a concrete Semantics (`owner`, `ref`, `uniq`, `obj`, `rc`, `arc`, `objref`, `objuniq`, `unsafe`), which tests equality with it. Concrete names and categories combine under the [Requirement expression rules](08-generics-constraints-and-contracts.md#83-requirement-expressions), as in `s is ref or obj`.

`reference` includes every non-`owner` representation, including raw pointers, and establishes no safe-borrow guarantee. `s is borrow` requires an outer safe borrow; `s is owning or borrow` permits every outer Semantics except `unsafe`. These tests apply to the outer layer only; they guarantee nothing about nested Types or payloads. The category `owning` is distinct from the recursive `Owned` guarantee. There are no categories named `owned`, `counted`, `pointer`, `safe` or `all`.

### 3.3.1. Value ownership

`T` and `owner/T` are equivalent: both directly own a value with the data layout of `T`. In this section, owning a value or object describes Semantics, not the recursive [Owned capability](15-ownership-and-lifetime-analysis.md#1523-static-and-owned).

### 3.3.2. Value borrows

Value borrows provide non-owning access to the storage of their immediate complete Referent Type, subject to lifetime constraints. They do not automatically follow a pointer stored there: `ref/(rc/T)` borrows a handle slot, while `objref/T` borrows the object. Physical storage follows §21.2.4.

- `ref/T` is a shared borrow; multiple shared references may coexist.
- `uniq/T` is an exclusive mutable borrow; no conflicting reference may coexist.

A complete Sealed object payload may supply an ordinary value borrow (§13.5.5.1). Its outer Loan keeps the object owner and referent dependencies. Whole-content updates follow §15.7; allocation lifetime and content lifetime remain distinct.

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
objref{source}/Animal       Dog object
    ├─ public view: Animal         ├─ Animal state
    ├─ shared access               ├─ Dog state
    └─ Origin: source              └─ Dog type and destruction information
```

### 3.3.6. Nested Semantics and type grouping

Borrow Origins are prefix annotations: `ref{outer}/uniq{inner}/T`. Semantics prefixes associate to the right, and each annotation belongs only to that prefix. A named aggregate may name its slot bindings: `uniq{borrow}/Utf8Writer<W>{target}` names the inner binding set `target`; relations bind its slots. Parentheses group a complete Type and do not move Origins: `(View<T>{a})` is valid; `(View<T>){a}`, `(uniq/T){a}` and `View<T>{a}{b}` are not. Only safe-borrow Semantics accept a borrow annotation (§15.3.1).

`ref/V`, `uniq/V` and `unsafe/V` may take a complete value Type `V`, including its own Semantics and Origins, as their immediate **Referent Type**. The outer layer refers to storage holding a value of `V`; it neither replaces `V`'s Semantics nor refers directly to `V`'s eventual referent.

```kimi
ref/ref/T                           // Shared borrow of a shared-reference value.
ref/uniq/T                          // Shared borrow of an exclusive-reference value.
uniq/ref/T                          // Exclusive borrow of a shared-reference value.
ref/obj/T                           // Borrow of object-handle storage, not objref/T.
unsafe/ref/T                        // Raw pointer to shared-reference storage.
ref{outer}/(ref{inner}/T)    // Separate inner and outer Origins.
```

Prefixes associate to the right: `ref{outer}/ref/T` annotates only the outer reference. Parentheses group complete Types and permit per-layer annotations. `ref/((i32) -> bool)` borrows a function value, while `(ref/i32) -> bool` takes one borrowed integer; `ref/(i32) -> bool` is invalid because a Function Type needs its own parameter list. Grouping adds neither a Tuple nor a borrow.

For identity, applicability, layout and Origin analysis, aliases are expanded and every Type layer is preserved: `ref/ref/T` differs from `ref/T`. Grouping and redundant `owner` prefixes normalize away, but `owner/V` keeps `V`'s references and Object Semantics. Object Semantics require a supported Core or runtime Contract View Target, not a Type that already has Semantics applied: `ref/obj/T` is valid, while `obj/ref/T` does not box a reference. Generic substitutions obey the same rules; see [generic slots](08-generics-constraints-and-contracts.md#81-generic-type-parameters).

Each Type layer keeps its Origin dependencies under the [position-specific elision rules](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision); an outer annotation cannot replace an inner one. In parameter Types, an outer direct borrow and each omitted aggregate slot introduce independent input Origins under §15.4; nested borrow layers still require explicit bindings. Local initializers may infer all layers, and instance Fields require explicit bindings throughout. The examples above illustrate composition, not unrestricted Origin omission.

At each safe value-borrow layer, all observable Origins of `V` must outlive that layer (`origin inner outlives outer` in the last example). Grouping or a redundant `owner` cannot add a conflicting annotation to one normalized layer. Raw pointers establish neither safe-reference validity nor longer lifetimes.

The outer Semantics determines Copy: `ref/uniq/T` is Copy, while `uniq/ref/T` is not. Copying an outer shared reference does not duplicate the inner exclusive capability. Shared access cannot Move a Non-Copy inner value, use its `uniq` exclusively, or mutate the slot; a shared Reborrow stays within the outer Loan. Moving or destroying an outer reference leaves its referent intact. A valid exclusive slot replacement must preserve the inner Type, Origin and Loan constraints.

This syntax adds no implicit repeated dereference, safe `*` operator, or conversion between pointers and safe references. Borrow formation follows the explicit rules for [Borrow and Reborrow](13-operators-and-assignment.md#1355-explicit-borrow-and-reborrow).

## 3.4. Values, places, and storage

**Storage** is a region that holds values. **Destruction responsibility** is the obligation to destroy an owned value at the end of its lifetime, subject to [cleanup and termination](16-scope-exit-and-destruction.md#16-scope-exit-and-destruction).

A **Place** is a storage location that can hold a value; a **Place expression** designates one, and parentheses preserve that classification. A Place is distinct from a **Temporary Value**, even when that value is materialized into a separate [Temporary Place](#36-temporary-values-places-and-lifetimes).

An **Access Designator** identifies an access target after Name and Type resolution: a local or parameter, Field, computed or required Property, Tuple element, index, or built-in dereference. It includes computed Properties and user indexers, without promising storage or Consume permission. Resolution of a designator has one of three outcomes:

```text
Access Designator -> operation-specific resolution
                    ├─ Place access
                    ├─ value acquisition, such as a getter result
                    └─ error
```

These are alternative outcomes, not fallback stages: a failed direct Move cannot become a borrow or a getter call. Standard Property `get` exposes the permitted Place operations; custom, computed and required `get` produce their declared result (Chapter 11). Permitted function references produce function values. Classification follows the resolved operation, not the spelling, and parentheses preserve it.

**Initialization** places a value in an empty Place. **Destruction** ends a value's lifetime and its responsibility; if the storage remains, that Place becomes Uninitialized after normal completion. Nothing can access storage after the storage's own lifetime ends.

Value lifetime separates three steps: acquisition (Copy or Move), placement (Initialization or Replacement) and Destruction. One assignment may contain both a Move and a Replacement.

An ownership Move transfers ownership and destruction responsibility, preventing a second destruction at the source. Copying or moving a borrow value duplicates or transfers its access capability without owning the referent; destroying a borrow does not destroy the referent. Destroying `rc/T` or `arc/T` releases an owning reference under the object lifetime rules. A non-owning `unsafe/T` neither destroys its referent nor frees its storage.

The [initialization-state rules](15-ownership-and-lifetime-analysis.md#1511-storage-state-and-responsibility) define Initialized, Uninitialized and Moved. A **Partial Move** transfers an inline part and leaves the aggregate incomplete; supported paths and permissions follow [Move Paths](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move).

## 3.5. Copy and move

**Copy** implicitly duplicates a value, leaves its source Initialized and keeps the source's destruction responsibility. It executes no user-defined code, heap-allocating duplication, reference-count change or resource acquisition. Copying a reference does not copy its referent.

**Move** transfers the value and its destruction responsibility, or a borrow value's access capability, and marks the source Moved. It invokes no user code and need not clear the source memory.

Ordinary value acquisition selects Copy for a Copy Type and Move otherwise, and rejects an unavailable operation. This applies to initialization, assignment sources, by-value arguments and result transfers. A Non-Copy owned value Moves in a consuming context, and a value can be Moved only out of a Movable Place (§15.1.5). Borrow creation and Reborrowing are separate operations. No explicit operator forces a Copy value to Move.

Copy capability is independent of `let`/`var` and of flow-dependent Loans. At a use site, Copy obeys read restrictions, and Move must not conflict with overlapping active Loans. Both preserve Origin dependencies without extending referent lifetimes. Reborrowing does not make exclusive references Copy.

```kimi
let a: i32 = 10
let b = a                 // Copy; a stays Initialized.
var node: obj/Node = makeNode()
let owned = node          // Move; node becomes Moved.
// use(node)              // Error until reinitialized.
node = makeNode()
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

A new borrow of an owned temporary depends on its Temporary Place and cannot outlive it. Exclusive capability permits the applicable explicit exclusive borrow; it does not bypass the [Borrow table](13-operators-and-assignment.md#1355-explicit-borrow-and-reborrow).

```kimi
inspect(makeResource()@ref)
modify(makeResource()@uniq)
let taken = resource // Non-Copy Resource moves to taken.
inspect(taken@ref) // Borrow the destination; resource remains Moved.

let view = makeResource()@ref
// inspect(view) // Error: borrowed temporary expired after the initializer.

let owned = makeResource()
let lastingView = owned@ref // Borrow the retained local instead.
inspect(lastingView)
```

A temporary that is already a borrow follows the shared-reference Copy or Reborrow rules. Its referent Origins are preserved; it is not replaced by a borrow of the storage holding the reference value.

```kimi
let view = makeView()@ref // If makeView returns ref/T, its Origins are preserved.
```

Borrow and Slice formation never extend the source's lifetime. Result transfers secure their values before the common [scope-exit cleanup](16-scope-exit-and-destruction.md#162-scope-exit-destruction); Abort follows [Abort termination](17-failure-handling.md#173-abort-termination).

## 3.7. Origins and loans: overview

Kimigayo uses **Origins** instead of lifetime variables. An Origin describes how long a borrow remains valid; a **Loan** records which Place is borrowed and whether the borrow is shared or exclusive.

The declaration-attached relation `origin a outlives b` requires `region(a) ⊇ region(b)`; `origin a == b` requires equal regions. See [Origin schemas and relations](15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations) and [ordering](15-ownership-and-lifetime-analysis.md#1522-ordering-and-intersection).

Origin annotations appear in signatures and Type declarations. Local Origins may be inferred from initializers and ordinary Origin and Loan constraints. Omission is permitted only under the [position-specific rules](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision): direct borrowed inputs and omitted input aggregate slots introduce input Origins, results use conservative elision, and instance Fields require explicit Origin bindings. Static Fields require `Owned` and the [static-source rules](11-properties.md#1132-static-storage).

The safe value borrows are:

```kimi
ref{o}/T   // shared, immutable, and aliasable
uniq{o}/T  // exclusive and mutable
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
| Implicit expression adaptation | An expression, target Type and use-site context. Only adaptations allowed in that position are selected, including the finite [argument adaptation rules](10-overload-resolution-and-inference.md#102-argument-adaptation-and-literals) and fixed-expectation [common function conversion](07-functions-and-callable-values.md#764-function-references-and-common-type-conversion). | May require acquisition, Borrow/Reborrow or a defined conversion. Literal fitting determines the Type of an unresolved literal; it does not convert an established numeric Type. No universal implicit-conversion search exists. |
| Explicit expression adaptation | An expression and a resolved Adaptation Target in context. Select one [defined `@` operation](13-operators-and-assignment.md#1353-defined-adaptations), then enforce its requirements and static result fitting. | May change value representation, view or Loan state, or perform runtime checks. No hidden sequence of operations is inserted. Origins are inferred as specified for Adaptation Targets. |
| Object upcast | An expression and a different object View Target, with proof of `Supports(S, V)` and a matching [explicit upcast row](13-operators-and-assignment.md#1357-object-upcasts). | One explicit view/acquisition operation. Inheritance or conformance alone establishes neither an implicit complete-value adaptation nor a callable argument/result conversion. |
| Base subobject receiver projection | An instance member selected in a base layer by [inherited lookup](09-names-signatures-and-access.md#951-base-subobject-receiver-projection). | Locates the receiver subobject and applies the permitted receiver access; no standalone conversion or owning base value. |
| Borrow / Reborrow | An expression with the required Place, access and Loan properties, and a permitted implicit or explicit borrow operation. | Establishes or derives Loans under the borrow rules. Changing `uniq/T` to `ref/T` requires a shared Reborrow; it is not a subtype rule. |
| Numeric conversion | An established numeric Type and a target admitted by the [explicit numeric conversion table](13-operators-and-assignment.md#1354-numeric-conversions-and-literals). | A value conversion with the specified rounding, range checks and failure behavior. `i32` is not a subtype of `i64`; representable literal fitting is separate. |
| Acquisition legality | An expression, the selected access operation, and the current initialization, access, ownership and Loan state. Apply the ordinary Copy, Move, Borrow or Consume requirements. | Type compatibility does not prove legality: an exact Type match can still fail because storage is Moved, access is unavailable or a Loan conflicts. Such a failure does not reopen committed lookup or overload selection. |

For example, `ref{longer}/T <: ref{shorter}/T` may hold when `longer : shorter`, without a new Loan. In contrast, `uniq/T` to `ref/T` needs a Reborrow, and an object view change needs its explicit upcast. The same normalized Type does not force an identity operation: explicit same-Type exclusive adaptation still selects Reborrow, and ordinary by-value acquisition may Copy or Move.

Candidate analysis may record operation choices and unresolved obligations but must not commit source-state changes while testing candidates. After selection, the chosen acquisition and adaptation are enforced in the specified evaluation order, and static result fitting is applied without replacing that operation. The [implementation correspondence](appendices/B-reference-models.md#b5-type-relation-and-operation-plans) is informative; no particular internal API is required.
