# 12. Expressions

[Specification index](../SPEC.md)

Type-side qualifiers and construction paths use [bound Container paths](09-names-signatures-and-access.md#961-bound-container-paths), including arguments at each segment and (Path from (...)).member. They create no runtime value. Reserved .init(...) remains construction and cannot fall back to an ordinary value-member call.

Expressions produce values or transfer control. This chapter defines their syntax, Type rules, and evaluation.

## 12.1. Classification and contexts

The source-language expression forms are:

```text
Expressions
├─ Primary Expression
│  ├─ Name
│  ├─ Literal
│  │  ├─ Number / Boolean / Character / String / Null / Unit
│  │  ├─ Interpolated String
│  │  └─ Tuple / Array / Dictionary
│  ├─ Parenthesized Expression
│  ├─ Function Expression
│  └─ Enum Case Construction
├─ Member Access
├─ Application
│  ├─ Invocation (including $abort(...))
│  └─ Generic Application
├─ Index / Slice Expression
├─ Explicit @ Operation: Type/Semantics adaptation
├─ Unary Expression
│  ├─ Sign / Logical Negation / Dereference / From-end Index
│  └─ Prefix / Postfix Increment and Decrement
├─ Binary Expression
│  ├─ Arithmetic / Shift / Bitwise
│  ├─ Comparison / Logical
│  └─ Runtime Type Test
├─ Range Expression
├─ Assignment Expression
│  ├─ Simple Assignment
│  └─ Compound Assignment
├─ Selection Expression: if / match
├─ Iteration / Do Expression: for / while / loop / do / Label: do
└─ Control Transfer Expression: return / exit / continue / yield

Related Syntax
├─ Match Pattern
├─ Statement: unsafe / defer / require
├─ Compile-time Directive: #if / #switch
├─ Attribute: #Name
└─ Composition Root: $
```

Value and access categories follow [Values, places, and storage](03-types-and-values.md#34-values-places-and-storage).

A normally completing expression produces a typed result, including [Unit](03-types-and-values.md#315-unit-and-never-types). [Value and Discard Contexts](14-control-flow.md#142-blocks-and-evaluation-contexts) determine how that result is used. Discarding it preserves side effects and type, ownership, and destruction checks. Assignment requires a writable [place](03-types-and-values.md#34-values-places-and-storage) or an accessible Property setter; a readable Property need not expose borrowable storage.

An indented body is a syntax container, not a standalone expression. Use a selection or do expression to obtain a value from several operations. Unsafe, defer, and require are statements, not initializers or arguments. Let/var declarations are not expressions or condition-binding syntax; nested bodies inside conditions retain their normal declaration rules (§14.2.3).

Source delimiters and continuation follow [Lines, indentation, and continuation](02-source-and-lexical-structure.md#22-lines-indentation-and-continuation).

## 12.2. Evaluation order

Evaluate operands once, from left to right, unless a construct specifies an exception or conditional evaluation. Precedence determines grouping; evaluation order determines the order of effects.

| Expression | Evaluation order |
| --- | --- |
| `a() + b() * c()` | a, b, c, multiplication, addition. |
| `receiver().method(a(), b())` | Receiver, resolve callee, a, b, call. |
| `array()[index()]` | Target, index, element access. |
| `(a(), b())` / `[a(), b()]` | Elements in source order. |
| `[key(): value(), ...]` | Per entry: key, duplicate check, value, insertion. |
| `start()..end()` | Start boundary, end boundary. |
| `"\(a()) / \(b())"` | Evaluate and stringify each interpolation in source order. |

`and`, `or`, and selections evaluate only the required operands or branches. [Simple assignment](13-operators-and-assignment.md#1371-simple-assignment) evaluates and secures its right side before its target; compound assignment retains target-first evaluation. Type arguments, length arguments, and adaptation-target Type formation are not evaluated at runtime.

An abrupt Completion, divergence, or Abort prevents evaluation of later operands and the enclosing operation. Unevaluated syntax still undergoes name, Type, and transfer-target checks; syntax excluded by `#if` / `#switch` follows [conditional compilation](19-compile-time-directives.md#19-compile-time-directives). [Temporary lifetimes](03-types-and-values.md#36-temporary-values-places-and-lifetimes) and scope-exit rules govern retained values.

## 12.3. Primary expressions

### 12.3.1. Type inference

Expected Types from declarations, parameters, and results propagate into expressions. Otherwise infer from operands. Ordinary numeric operations require the same numeric Type; integer widths, signedness, and integer/floating-point Types do not mix implicitly.

An untyped integer literal adopts an expected integer Type if its value fits. Without one, it defaults to `i32`; a value outside that range requires an explicit Type. A floating-point literal adopts an expected `f32` or `f64`, defaulting to `f64`. Check a directly negated integer literal as a signed value, allowing the minimum of a signed Type.

Explicit `@f32` / `@f64` on a direct untyped integer literal follows the [explicit-literal adaptation rule](13-operators-and-assignment.md#135-explicit-operations), without an intermediate default integer Type. This does not add implicit integer-to-float fitting.

```kimi
let a: i64 = 10
let b = a + 20          // 20 adopts i64.
let c: i32 = 3
let d = a + c@i64       // Convert an already typed operand explicitly.
let minimum: i8 = -128
```

There are no implicit conversions between `bool`, `char`, and numbers. Conditions require `bool`, not an integer or pointer. Borrowing and reborrowing are separate adaptations governed by ownership rules.

During [overload resolution](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference), fit unresolved literals independently to candidates before defaulting; numeric defaults do not break ties. Expected Types and nested-call/function inference are limited by [inference boundaries](10-overload-resolution-and-inference.md#105-inference-boundaries-and-specialization). Locals never infer backward from later uses.

For control-flow results, collect all source constraints before defaulting, including available constraints from enclosing result expressions (§14.9.1). Body nesting, labels, and grouping alone do not force an earlier numeric default.

### 12.3.2. Names, literals, and grouping

| Form | Meaning |
| --- | --- |
| `name` | Reference to a visible binding, function, or other named entity. |
| `123`, `0xff`, `1.5`, `true`, `'€'`, `"text"` | Numeric, Boolean, Character, and String literals; see [lexical structure](02-source-and-lexical-structure.md#2-source-and-lexical-structure). |
| `"value = \(value)"` | Interpolated string; the embedded Type must support stringification. |
| `null` | Contextually typed [raw null pointer](05-raw-pointers-and-unsafe-memory.md#51-null-and-equality). |
| `()` | Unit value. |
| `(value)` | Grouped expression; preserves a place. |
| `(value,)`, `(a, b)` | One-element or multi-element Tuple. |
| `[a, b]`, `[]` | Array literal. |
| `[key: value]`, `[:]` | Dictionary literal. |

Tuples may have different Types at each position. An array has one element Type; a dictionary has one key Type and one value Type. Empty collection literals need an expected Type. Array and Dictionary literals use the [required Cores](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations), with the expected fixed-array exception in [sequence Types](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices); ambiguity does not fall back to a universal object Type.

```kimi
let pair = (10, "ten")
let single = (10,)
let values = [10, 20, 30,]
let names = [1: "one", 2: "two",]
let message = "first = \(values[0])"
```

### 12.3.3. Interpolation stringification

Each embedded value’s complete Type must satisfy the required Stringify Contract. Evaluate it once, borrow it shared for the call, invoke its verified mapping once, and append the owned result before the next interpolation. Printing does not implicitly Move a non-Copy source. End the call Loan on completion; neither returned nor combined strings retain source borrows. Normal temporary cleanup and Abort rules apply. Allocation may be optimized, but independent owned-string semantics must remain.

Scalars, Unit, and string under `owner` Semantics conform to Stringify. Integers use decimal with a minus sign only for negative values; bool uses true/false, char its scalar’s UTF-8 bytes, Unit `()`, and string its contents. Float formatting is locale-independent; finite, NaN, and infinity spellings are implementation-defined and documented. Safe shared/exclusive borrows forward through shared access without taking ownership. User Types need `Self is Stringify` and a matching public implementation. No implicit object-address/raw-pointer formatting is provided; concatenation remains string-only.

### 12.3.4. Dictionary construction and duplicate keys

Equivalent duplicate keys are errors. **Mandatory static checking** covers grouped or ungrouped Boolean, integer, char, plain string, and Unit literals; integers may have one direct unary sign. Fit each to the determined built-in key Type, decode escapes/separators, and compare values without user equality. Compare all eligible entries, even across ineligible ones, and diagnose later duplicates even in unreachable syntax.

Names (including constant-readable let), arithmetic, conversions, Tuples, floats, interpolation, and user Types are excluded; optimizer folding cannot expand this set. Otherwise process entries in source order:

1. Evaluate its key once and retain it.
2. Check for an equivalent key among entries already inserted.
3. On a duplicate, initiate implicit Abort Termination before evaluating this entry's value or any later entry.
4. Otherwise evaluate its value once.
5. Insert the retained key and resulting value.

Equivalence follows dictionary key equality; matching hash values alone do not make keys duplicates.

The Kimi Dictionary uses the Equatable mapping for key equality. An implementation may use linear search and requires no source Hash contract. If it uses hashing internally, equal keys must have equal hashes; hashing must not change which keys are equivalent. User-defined key equality must be an equivalence relation, in addition to the stability conditions below. The key Type must guarantee that the logical equality and hash value of a stored key remain unchanged while the dictionary holds it. Later literal entries never overwrite existing values. Mutation and indexed replacement have the distinct contracts in §4.7.

The intrinsic Equatable mapping for f32/f64 treats all NaN values of the same Type as equal and also treats signed zeros as equal. Other values follow numeric equality. This makes floating keys usable without changing the built-in IEEE `==`/`!=` operators or adding Comparable. Equatable mappings for shared borrows and Tuples compose these Contract mappings; their built-in comparison expressions still follow §13.4. Internal hashes, when used, must agree for all NaNs and for both zeros. Floating keys undergo runtime duplicate checking even when written as literals.

User-defined equality/stability laws are semantic API obligations, not a new compiler proof or unchecked memory-safety permission. Violating them may make logical lookup and duplicate-detection results unspecified, but must not cause memory unsafety, invent values, or bypass Copy/Move and destruction responsibility. An implementation need not detect such law violations; detected API-law failures may Abort. Invalid operations actually executed inside equality still follow their ordinary rules. Normal well-formed keys retain the deterministic equality/duplicate behavior above.

A candidate key's logical equality and hash value must also remain unchanged from the start of duplicate checking through completion of insertion, including evaluation of its value expression.

```kimi
let x: i32 = getKey()
let map = [x: first(), x: second()]
```

When checked at runtime, the first entry is evaluated and inserted before checking the second key. That check fails, so `second()` is not called.

If key evaluation, duplicate checking, or value evaluation does not complete normally, do not insert that entry or process later entries. No partially constructed dictionary is returned, and completed side effects are not rolled back. The duplicate's diagnostic location is the later key expression. Termination and cleanup follow [Abort Termination](17-failure-handling.md#173-abort-termination).

## 12.4. Access and application

### 12.4.1. Member access

`expression.name` selects a member. [Qualified lookup](09-names-signatures-and-access.md#95-qualified-and-inherited-lookup) distinguishes Container and value paths, reports ambiguity when both succeed, and never implicitly inserts `self`. The right side of an ordinary member-access `.` must be a member Name or an in-range decimal integer literal selecting a Tuple element; `pair.0` selects its first element. The reserved `.init(...)` suffix instead forms a [construction expression](06-declarations-and-containers.md#623-constructors) with a Type qualifier. Dynamic member lookup with an arbitrary expression is not defined.

Property selection follows §11: standard get exposes permitted Place operations, while custom/computed/required get produces a result. Assignment uses accessible set. Check each operation and receiver; getter results never expose hidden storage. Tuple/fixed-array Places retain Move Path rules. Raw pointers do not dereference automatically: use `(*pointer).name` in an Unsafe Block.

```kimi
let count = collection.count
collection.count = 10   // Requires a setter and write permission.
let first = pair.0
```

### 12.4.2. Invocation and generic application

`callee(arg1, arg2)` invokes a function, method, or function value. Zero arguments and a trailing comma are allowed. `callee<T, U>(args)` applies explicit type arguments before calling.

Argument mapping, Type adaptation, expected-result filtering, candidate comparison, and final usage checks follow [overload resolution](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference). Named arguments use `name: expression`; positional arguments must precede them. Function values retain positional-only calling, and unsafe calls retain their [additional restrictions](07-functions-and-callable-values.md#75-unsafe-functions). Explicit function type arguments must provide the entire required list.

In an expression, `<` introducing type arguments must be adjacent to the target name and have a matching `>`. Thus `f<T>(x)` applies type arguments while `a < b` compares values. Nested type arguments may split `>>` into two closing delimiters. Use spaces around comparison operators to avoid ambiguity.

Generic arguments follow [slot binding](08-generics-constraints-and-contracts.md#81-generic-type-parameters), [function length slots](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices), and [inference boundaries](10-overload-resolution-and-inference.md#105-inference-boundaries-and-specialization); other general Const arguments are not introduced. After ordinary call resolution, select any matching [explicit specialization](08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization). Structure construction uses the dedicated [Type.init invocation](06-declarations-and-containers.md#623-constructors), with ordinary call argument evaluation and its own Type-only qualifier lookup. `T(args)` is not a constructor shorthand.

### 12.4.3. Object member calls

Select the ordinary member, overload, access, and public contract statically from the receiver's [Effective Type](14-control-flow.md#14101-stable-bindings-and-effective-types). Use that implementation even when the Runtime Object Type is more derived. Adjust the receiver to the selected declaration's subobject under §9.5.1; no new lookup or derived-only overload is introduced.

Shared object access admits shared members/getters. Exclusive access may share by Reborrow or invoke an exclusive member/setter under normal Loan rules. Returned interior references retain their receiver Loan; an exclusive result cannot enable overlapping reentry. Calls through an object borrow require the public guarantee below.

### 12.4.4. Object receiver compatibility

A selected `ref/Self` or `uniq/Self` receiver may use the complete payload projection of §13.5.5 when the source View Target is exactly the same complete Sealed Type. This is an ordinary complete-value call and requires no additional ObjectCallCompatible proof. Keep any existing NotProven public status unchanged. Ordinary argument positions still require explicit projection.

Every other object or base-subobject receiver path requires the published Proven guarantee below. Open Views and base subobjects cannot undergo whole-value replacement or incomplete MoveOut. Inherited Self remains the defining base Type; a sealed derived Core never grants whole-base replacement. Owning-handle replacement instead replaces the handle's object and retains ordinary borrowing/destruction conditions. rc/arc provide only shared access, even at count one.

#### 12.4.4.1. Public status and use

**ObjectCallCompatible** is the public guarantee that a borrowed-receiver call preserves receiver completeness, access authority, and its Type/Origin/Loan contract. An ObjectCallCompatible operation is compatible with calls through object borrows or base-subobject projections, subject to the ordinary call, access, Type, Origin, and Loan checks. Its key is the **call operation Identity**, not a Type substitution or a separate receiver-kind key; receiver kind belongs to the Signature. Functions and custom/computed get/set each use their declaration Identity.

Direct standard get/set remain Place operations, checked by acquisition and Property permissions; they are not implicit getter functions. In particular, borrowing `Box<T>.item` with `@ref` does not require T is Copy. Standard Contract witnesses are call operations: their generated Identity distinguishes the Requirement Identity, Property Identity, and bridge kind within the conformance mapping. Preserve each witness's result Type and public premises; do not merge different witnesses or publish a status for each concrete instantiation.

| Published status | Call through a base-subobject projection or object borrow |
| --- | --- |
| Proven | Allowed if the ordinary call, access, Type, Origin, and Loan checks pass |
| NotProven | Error on protected object/base paths; ordinary complete-value calls, including proven Sealed payload projection, follow their normal rules |

NotProven means absence of a common proof, not Refuted for every binding. Additional caller premises, favorable Type arguments, the exact Dynamic Type, or one selected specialization cannot strengthen this status. A failed use never causes overload reselection. Unknown is internal pending work, never a published status. Invalid bodies, missing mandatory artifact information, and unimplemented verification cannot be hidden as NotProven.

#### 12.4.4.2. Effect verification

Retain completeness evidence separately from storage relation. Payload projection does not turn Whole into Part or Separate. A legal complete-target update or borrow return is not itself a preservation failure. A formal ref/uniq parameter alone does not prove caller storage completeness: compose callee effects at each actual storage target and preserve unknown-call, unsafe, and specialization checks.

Use resolved operations and acquisition plans before optimization. Under the declaration's Signature, Constraints, and conditional premises, verify every admitted Type/Origin binding using §8.7–§8.10. Do not infer hidden caller Constraints from a body. The following abstract rules determine the public result independently of analysis precision, optimization, and processing order; representations and worklist algorithms are implementation choices.

Summaries retain operation kinds and their relation to receiver/input roots, captures, static anchors (§15.6.4), and returned aliases. A storage relation is a finite set of Whole (the root), Base (base edges only), Part (a path including Field/Tuple/array storage), Separate (proven disjoint), or MayAlias (not established). Preserve normal Origin/Loan and target-Place information alongside these relations.

| Operation or dependency | Required summary/check |
| --- | --- |
| Replace, reconstruct, or acquire ownership of protected receiver Whole/Base | Receiver-preservation violation unless the actual target is independently proven complete and the operation is legal under §15.7 |
| MoveOut from receiver storage leaving it incomplete | Violation; later restoration does not cancel it |
| Completeness-preserving Part Replacement/Exchange, permitted reads/borrows | No preservation violation by itself; retain access, Loan, and cleanup checks |
| Escape of unrestricted exclusive access to protected receiver/base | Violation unless a legal complete-target borrow; include results, stores, callee paths, and all dependencies |
| Borrowed result | Retain root/alias correspondence and verify return Origins, Loans, and authority |
| Separate operation | No violation against the root from which separation is proven |
| MayAlias or unverified unsafe/indirect effects that may violate receiver preservation | Unproven effect on every potentially affected root |
| Calls, custom accessors, defaults, cleanup | Map and compose published root effects through actual arguments, captures, and static anchors |
| Branches and loops | Union all Type-checked paths; omit only environment-excluded syntax, not paths removed by runtime reasoning or optimization |

Base-only path composition yields Base; a storage-element edge yields Part. A Field storing a reference is not that reference's referent: follow retained anchors/aliases, using MayAlias when the referent relation is unknown. Separation from one input proves nothing about another input, capture, or static root. Already proven harmless effects, such as permitted reads, do not become violations merely because their target may alias.

A callee's whole-value replacement mapped onto a caller's Part is not replacement of the whole caller receiver. Keep the actual target's restrictions, however: Part classification cannot erase a base-subobject use check. Summarize defined completeness-preserving Replacement/Exchange as such, rather than counting their internal lowering transfers as separate illegal MoveOut operations. Retain operation summaries instead of simply propagating a callee's NotProven bit.

For same-build calls, propagate direct effects over the finite declaration-schema/root domain to the least union fixed point, including recursion and the implementation families below. Do not enumerate concrete Types or remove specializations based on a particular call. Recursion alone is not an unproven effect; an unfinished empty summary is not a proof. A verified empty summary is valid.

Separate, indirect, and generic-requirement calls use validated public summaries or requirement Effect contracts. Without an optional effect guarantee, propagate unproven effects to potentially affected roots; do not inspect private bodies or enumerate possible implementations. Missing mandatory artifact data is an artifact error, not an optional missing guarantee. Receiverless helpers may publish input-root summaries without an ObjectCallCompatible status.

An implementation succeeds only when normal semantic checking completes and every admitted binding has no receiver violation or unproven effect. Pending call/conformance obligations remain explicit until resolved. The effect fixed point does not prove a circular conformance declaration; normal proof deadlines and errors still apply.

#### 12.4.4.3. Implementation families

An operation's public status is Proven exactly when the ordinary body and **every implementation in its closed explicit-specialization set** pass §12.4.4.2. Otherwise a semantically valid family is NotProven. Close the set after environment selection, generation, and declaration collection, regardless of whether implementations are currently called.

Verify the ordinary generic body over all admitted bindings independently; do not subtract bindings covered by specializations. Verify each specialization under its substituted inherited contract. The ordinary body's inferred Proven imposes no extra declaration obligation on a specialization. An incompatible specialization makes the family NotProven, but is not a declaration error for that reason; declared Signature, Constraints, Safety, and Effect obligations still apply. Improving the ordinary body therefore cannot invalidate an unchanged specialization.

Check use against the original public contract before selecting an implementation (§8.8.3). A NotProven diagnostic identifies the responsible implementation and reason, including specialization arguments and dependent callee/witness identities where relevant; private bodies need not be exposed. Publish the completed summaries, status, and dependencies under §18.3 and revalidate changes under §21.3.4.
