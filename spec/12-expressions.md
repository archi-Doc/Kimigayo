# 12. Expressions

[Specification index](../SPEC.md)

Expressions produce values or transfer control. This chapter defines their syntax, Type rules and evaluation.

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

Value and access categories follow [values, places and storage](03-types-and-values.md#34-values-places-and-storage).

A normally completing expression produces a typed result, which may be [Unit](03-types-and-values.md#315-unit-and-never-types). [Value and Discard Contexts](14-control-flow.md#142-blocks-and-evaluation-contexts) determine how that result is used; discarding it still preserves side effects and all Type, ownership and destruction checks. Assignment requires a writable [Place](03-types-and-values.md#34-values-places-and-storage) or an accessible Property setter; a readable Property need not expose borrowable storage.

An indented body is a syntax container, not a standalone expression; use a selection or do expression to obtain a value from several operations. `unsafe`, `defer`, `require` and explicit discard (`_ = expression`) are statements, not initializers or arguments. `let`/`var` declarations are neither expressions nor condition-binding syntax; nested bodies inside conditions keep their normal declaration rules (§14.2.3).

Source delimiters and continuation follow [lines, indentation and continuation](02-source-and-lexical-structure.md#22-lines-indentation-and-continuation).

## 12.2. Evaluation order

Operands are evaluated once, from left to right, unless a construct specifies an exception or conditional evaluation. Precedence determines grouping; evaluation order determines the order of effects.

| Expression | Evaluation order |
| --- | --- |
| `a() + b() * c()` | `a`, `b`, `c`, multiplication, addition. |
| `receiver().method(a(), b())` | Receiver, callee resolution, `a`, `b`, call. |
| `array()[index()]` | Target, index, element access. |
| `(a(), b())` / `[a(), b()]` | Elements in source order. |
| `[key(): value(), ...]` | Per entry: key, duplicate check, value, insertion. |
| `start()..end()` | Start boundary, end boundary. |
| `"\(a()) / \(b())"` | Each interpolation is evaluated and written in source order; failure stops later expressions. |

`and`, `or` and selections evaluate only the required operands or branches. [Simple assignment](13-operators-and-assignment.md#1371-simple-assignment) evaluates and secures its right-hand side before its target, while compound assignment keeps target-first evaluation. Type arguments, length arguments and adaptation-target Type formation are not evaluated at runtime.

An abrupt Completion, divergence or Abort prevents evaluation of later operands and of the enclosing operation. Unevaluated syntax still undergoes name, Type and transfer-target checks; syntax excluded by `#if`/`#switch` follows [conditional compilation](19-compile-time-directives.md#19-compile-time-directives). [Temporary lifetimes](03-types-and-values.md#36-temporary-values-places-and-lifetimes) and scope-exit rules govern retained values.

## 12.3. Primary expressions

### 12.3.1. Type inference

Expected Types from declarations, parameters and results propagate into expressions; otherwise Types are inferred from operands. Ordinary numeric operations require the same numeric Type: integer widths, signedness, and integer and floating-point Types do not mix implicitly.

An untyped integer literal adopts an expected integer Type if its value fits. Without one it defaults to `i32`, and a value outside that range requires an explicit Type. A floating-point literal adopts an expected `f32` or `f64` and otherwise defaults to `f64`. A directly negated integer literal is checked as a signed value, so the minimum of a signed Type is allowed.

An explicit `@f32`/`@f64` on a direct untyped integer literal follows the [explicit-literal adaptation rule](13-operators-and-assignment.md#135-explicit-operations), without an intermediate default integer Type. This adds no implicit integer-to-float fitting.

```kimi
let a: i64 = 10
let b = a + 20          // 20 adopts i64.
let c: i32 = 3
let d = a + c@i64       // Convert an already typed operand explicitly.
let minimum: i8 = -128
```

There are no implicit conversions between `bool`, `char` and numbers, and conditions require `bool`, not an integer or pointer. Borrowing and reborrowing are separate adaptations governed by the ownership rules.

During [overload resolution](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference), unresolved literals are fitted independently to candidates before defaulting, and numeric defaults do not break ties. Expected Types and nested-call and function inference are limited by the [inference boundaries](10-overload-resolution-and-inference.md#105-inference-boundaries-and-specialization). Locals never infer backward from later uses.

For control-flow results, all source constraints, including available constraints from enclosing result expressions (§14.9.1), are collected before defaulting. Body nesting, labels and grouping alone do not force an earlier numeric default.

### 12.3.2. Names, literals, and grouping

| Form | Meaning |
| --- | --- |
| `name` | Reference to a visible binding, function or other named entity. |
| `123`, `0xff`, `1.5`, `true`, `'€'`, `"text"` | Numeric, Boolean, Character and String literals; see [lexical structure](02-source-and-lexical-structure.md#2-source-and-lexical-structure). |
| `"value = \(value)"` | Interpolated string; the embedded Type must support UTF-8 formatting (§12.3.3). |
| `null` | Contextually typed [raw null pointer](05-raw-pointers-and-unsafe-memory.md#51-null-and-equality). |
| `()` | Unit value. |
| `(value)` | Grouped expression; preserves a Place. |
| `(value,)`, `(a, b)` | One-element or multi-element Tuple. |
| `[a, b]`, `[]` | Array literal. |
| `[key: value]`, `[:]` | Dictionary literal. |

Tuple positions may have different Types. An array has one element Type, and a Dictionary one key Type and one value Type. Empty collection literals need an expected Type. Array and Dictionary literals use the [required Cores](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations), with the expected fixed-array exception of [Chapter 4](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices); ambiguity never falls back to a universal object Type.

```kimi
let pair = (10, "ten")
let single = (10,)
let values = [10, 20, 30,]
let names = [1: "one", 2: "two",]
let message = "first = \(values[0])"
```

### 12.3.3. Interpolation formatting

An interpolated literal produces an owning `string`. Each embedded expression fits the shared input of `Utf8Writer.write` under §10.2 and requires `Utf8Format` for the selected referent Type. Borrow Types do not forward conformance. The [formatting profile](utf8-formatting.md#5-interpolation-and-internal-adapters) defines evaluation, temporary lifetime, failure, representations and capacity planning. No intermediate owning string per value is required, and a bare Place is not Moved.

`$tryWrite(writer, literal)` writes directly to an existing adapter, stopping at its first failure without evaluating later substitutions. Its immediate exclusive borrow, literal-only second operand and control-flow boundaries are defined in [the profile](utf8-formatting.md#53-short-circuiting-trywrite). String concatenation (§13.3) still accepts only string operands.

### 12.3.4. Dictionary construction and duplicate keys

Equivalent duplicate keys in a Dictionary literal are errors.

**Mandatory static checking** covers Boolean, integer, `char`, plain string and Unit literals, grouped or not; integers may have one direct unary sign. Each such key is fitted to the determined built-in key Type, its escapes and separators are decoded, and the values are compared without user equality. All eligible entries are compared, even across ineligible entries, and later duplicates are diagnosed even in unreachable syntax. Names (including Constant-readable `let` bindings), arithmetic, conversions, Tuples, floats, interpolation and user Types are excluded, and optimizer folding cannot extend the set.

**Runtime checking.** Otherwise, entries are processed in source order:

1. Evaluate the key once and retain it.
2. Check for an equivalent key among the entries already inserted.
3. On a duplicate, initiate implicit Abort Termination before evaluating this entry's value or any later entry.
4. Otherwise, evaluate the value once.
5. Insert the retained key and the resulting value.

```kimi
let x: i32 = getKey()
let map = [x: first(), x: second()]
```

When checked at runtime, the first entry is evaluated and inserted before the second key is checked. That check fails, so `second()` is not called.

If key evaluation, duplicate checking or value evaluation does not complete normally, that entry is not inserted and no later entries are processed. No partially constructed Dictionary is returned, and completed side effects are not rolled back. The diagnostic location of a duplicate is the later key expression. Termination and cleanup follow [Abort Termination](17-failure-handling.md#173-abort-termination).

**Key equality.** Equivalence follows the Dictionary's key equality, which is the Equatable mapping; matching hash values alone do not make keys duplicates. An implementation may use linear search and requires no source Hash contract. If it hashes internally, equal keys must have equal hashes, and hashing must not change which keys are equivalent. Later literal entries never overwrite existing values; mutation and indexed replacement have the distinct contracts of §4.7.

Each equality call uses the stored key as `self` and the candidate or search key as `other`: `stored.equals(search)`. Candidate visitation order and the number of equality calls are unspecified, independently of the required insertion, iteration and destruction order. Implementations may filter candidates using internal hashes. Every equality call that is made keeps its ordinary effects, Loans, failure and nontermination behavior; the collection API does not make equality pure. Programs must not use equality-call order or counts as an insertion-order observation. The key-stability obligation below applies to every storage and search strategy.

The intrinsic Equatable mapping for `f32`/`f64` treats all NaN values of the same Type as equal and treats both signed zeros as equal; other values follow numeric equality. This makes floating keys usable without changing the built-in IEEE `==`/`!=` operators or adding Comparable. Equatable mappings for shared borrows and Tuples compose these Contract mappings, while their built-in comparison expressions still follow §13.4. Internal hashes, when used, must agree for all NaNs and for both zeros. Floating keys undergo runtime duplicate checking even when written as literals.

**Key stability.** User-defined key equality must be an equivalence relation. The key Type must also guarantee that a key's logical equality and hash value do not change while the Dictionary holds it, and, for a candidate key, from the start of duplicate checking through completion of insertion, including evaluation of its value expression.

These equality and stability laws are semantic API obligations, not a compiler proof or an unchecked memory-safety permission. Violating them may make logical lookup and duplicate-detection results unspecified, but must not cause memory unsafety, invent values, or bypass Copy/Move and destruction responsibility. An implementation need not detect such violations, and detected violations may Abort. Invalid operations actually executed inside equality still follow their ordinary rules. Well-formed keys keep the deterministic equality and duplicate behavior above.

## 12.4. Access and application

### 12.4.1. Member access

`expression.name` selects a member. [Qualified lookup](09-names-signatures-and-access.md#95-qualified-and-inherited-lookup) distinguishes Container and value paths, reports ambiguity when both succeed, and never implicitly inserts `self`. The right side of an ordinary member-access `.` must be a member Name or an in-range decimal integer literal that selects a Tuple element; `pair.0` selects the first element. Dynamic member lookup with an arbitrary expression is not defined.

Type-side qualifiers and construction paths use [bound Container paths](09-names-signatures-and-access.md#961-bound-container-paths), including arguments at each segment and `(Path{...}).member`; they create no runtime value. The reserved `.init(...)` suffix forms a [construction expression](06-declarations-and-containers.md#623-constructors) with a Type qualifier and never falls back to an ordinary value-member call.

Property selection follows Chapter 11: standard `get` exposes the permitted Place operations, while custom, computed and required `get` produce a result. Assignment uses an accessible `set`. Each operation and receiver is checked, and getter results never expose hidden storage. Tuple and fixed-array Places keep the Move Path rules. Raw pointers are not dereferenced automatically: use `(*pointer).name` in an Unsafe Block.

```kimi
let count = collection.count
collection.count = 10   // Requires a setter and write permission.
let first = pair.0
```

### 12.4.2. Invocation and generic application

`callee(arg1, arg2)` invokes a function, method or function value. Zero arguments and a trailing comma are allowed. `callee<T, U>(args)` applies explicit Type arguments before the call.

Argument mapping, Type adaptation, expected-result filtering, candidate comparison and final usage checks follow [overload resolution](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference). Named arguments use `name: expression`; name and value omission follow [§7.2](07-functions-and-callable-values.md#72-parameters-and-defaults). Function values keep positional-only calling, and unsafe calls keep their [additional restrictions](07-functions-and-callable-values.md#75-unsafe-functions). Explicit function Type arguments must provide the entire required list.

In an expression, a `<` that introduces Type arguments must be adjacent to the target name and have a matching `>`. Thus `f<T>(x)` applies Type arguments while `a < b` compares values. Nested Type arguments may split `>>` into two closing delimiters. Spaces around comparison operators avoid ambiguity.

Generic arguments follow [slot binding](08-generics-constraints-and-contracts.md#81-generic-type-parameters), [function length slots](04-arrays-indexing-and-slices.md#44-function-length-parameters) and the [inference boundaries](10-overload-resolution-and-inference.md#105-inference-boundaries-and-specialization); no other general Const arguments exist. After ordinary call resolution, any matching [explicit specialization](08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization) is selected. Structure construction uses the dedicated [`Type.init` invocation](06-declarations-and-containers.md#623-constructors), with ordinary argument evaluation and its own Type-only qualifier lookup; `T(args)` is not a constructor shorthand.

### 12.4.3. Object member calls

The ordinary member, overload, access and public contract are selected statically from the receiver's [Effective Type](14-control-flow.md#14101-stable-bindings-and-effective-types), and that implementation is used even when the Runtime Object Type is more derived. The receiver is adjusted to the selected declaration's subobject under §9.5.1; no new lookup or derived-only overload is introduced.

Shared object access admits shared members and getters. Exclusive access may share by Reborrow or invoke an exclusive member or setter under the normal Loan rules. Returned interior references keep their receiver Loan, and an exclusive result cannot enable overlapping reentry. Calls through an object borrow require the public guarantee of §12.4.4.

### 12.4.4. Object receiver compatibility

**ObjectCallCompatible** is the public guarantee that a borrowed-receiver call preserves receiver completeness, access authority, and its Type/Origin/Loan contract. An ObjectCallCompatible operation may be called through object borrows or base-subobject projections, subject to the ordinary call, access, Type, Origin and Loan checks. The guarantee is keyed by the **call operation identity**, not by a Type substitution or a separate receiver-kind key; receiver kind belongs to the Signature. Functions and custom or computed `get`/`set` each use their declaration identity.

**Complete Sealed payload exception.** When the source View Target of an object-kind receiver is exactly the same complete Sealed Type, a selected `ref/Self` or `uniq/Self` receiver is acquired through the complete payload projection of §13.5.5.1: implicitly under [§7.3](07-functions-and-callable-values.md#73-explicit-receivers), or written explicitly as in `(handle@uniq/T).method()`. The selection rule is common to shared and exclusive receivers; an exclusive projection additionally requires the exclusive acquisition conditions of §15.1.5. Either form is an ordinary complete-value call and needs no ObjectCallCompatible proof; any existing NotProven public status stays unchanged. Ordinary argument positions always require an explicit projection.

Every other object or base-subobject receiver path requires published Proven status, checked after overload selection; the status never excludes a candidate and never causes reselection (§7.3). Open Views and base subobjects cannot undergo whole-value replacement or incomplete MoveOut. Inherited Self remains the defining base Type, and a sealed derived Core never grants whole-base replacement. Owning-handle replacement instead replaces the handle's object and keeps the ordinary borrowing and destruction conditions. `rc`/`arc` provide only shared access, even at count one.

#### 12.4.4.1. Public status and use

Direct standard `get`/`set` remain Place operations, checked by acquisition and Property permissions; they are not implicit getter functions. In particular, borrowing `Box<T>.item` with `@ref` does not require `T is Copy`. Standard Contract witnesses are call operations: their generated identity distinguishes the Requirement Identity, Property identity and bridge kind within the conformance mapping. Each witness's result Type and public premises are preserved; different witnesses are not merged, and no status is published per concrete instantiation.

| Published status | Call through a base-subobject projection or object borrow |
| --- | --- |
| Proven | Allowed if the ordinary call, access, Type, Origin and Loan checks pass |
| NotProven | Error on protected object/base paths; ordinary complete-value calls, including proven Sealed payload projection, follow their normal rules |

NotProven means the absence of a common proof, not a refutation for every binding. Additional caller premises, favorable Type arguments, the exact Dynamic Type or one selected specialization cannot strengthen the status, and a failed use never excludes a candidate or causes overload reselection. Unknown is internal pending work, never a published status. Invalid bodies, missing mandatory artifact information and unimplemented verification cannot be hidden as NotProven.

#### 12.4.4.2. Effect verification

Completeness evidence is kept separately from storage relations. Payload projection does not turn Whole into Part or Separate. A legal complete-target update or borrow return is not itself a preservation failure. A formal `ref`/`uniq` parameter alone does not prove caller storage completeness: callee effects are composed at each actual storage target, and unknown-call, unsafe and specialization checks are preserved.

Verification uses resolved operations and acquisition plans before optimization. Under the declaration's Signature, Constraints and conditional premises, every admitted Type/Origin binding is verified using §8.7–§8.10, without inferring hidden caller Constraints from a body. The abstract rules below determine the public result independently of analysis precision, optimization and processing order; representations and worklist algorithms are implementation choices.

Summaries keep operation kinds and their relation to receiver and input roots, captures, static anchors (§15.6.4) and returned aliases. A **storage relation** is a finite set of: Whole (the root), Base (base edges only), Part (a path including Field, Tuple or array storage), Separate (proven disjoint) and MayAlias (not established). Normal Origin/Loan and target-Place information is kept alongside these relations.

| Operation or dependency | Required summary or check |
| --- | --- |
| Replace, reconstruct or acquire ownership of a protected receiver's Whole/Base | Receiver-preservation violation, unless the actual target is independently proven complete and the operation is legal under §15.7 |
| MoveOut from receiver storage leaving it incomplete | Violation; later restoration does not cancel it |
| Completeness-preserving Part Replacement/Exchange; permitted reads and borrows | No preservation violation by itself; access, Loan and cleanup checks remain |
| Escape of unrestricted exclusive access to a protected receiver or base | Violation unless it is a legal complete-target borrow; includes results, stores, callee paths and all dependencies |
| Borrowed result | Root/alias correspondence is kept; return Origins, Loans and authority are verified |
| Separate operation | No violation against the root from which separation is proven |
| MayAlias, or unverified unsafe/indirect effects that may violate receiver preservation | Unproven effect on every potentially affected root |
| Calls, custom accessors, defaults, cleanup | Published root effects are mapped and composed through actual arguments, captures and static anchors |
| Branches and loops | Union of all Type-checked paths; only environment-excluded syntax is omitted, not paths removed by runtime reasoning or optimization |

Composing base-only paths yields Base; a storage-element edge yields Part. A Field that stores a reference is not that reference's referent: retained anchors and aliases are followed, using MayAlias when the referent relation is unknown. Separation from one input proves nothing about another input, capture or static root. Already proven harmless effects, such as permitted reads, do not become violations merely because their target may alias.

A callee's whole-value replacement mapped onto a caller's Part is not replacement of the whole caller receiver, but the actual target's restrictions are kept: Part classification cannot erase a base-subobject use check. Defined completeness-preserving Replacement and Exchange are summarized as such, rather than counting their internal lowering transfers as separate illegal MoveOuts. Operation summaries are retained instead of simply propagating a callee's NotProven bit.

For same-build calls, direct effects are propagated over the finite domain of declaration schemas and roots to the least union fixed point, including recursion and the implementation families of §12.4.4.3. Concrete Types are not enumerated, and specializations are not removed on the basis of a particular call. Recursion alone is not an unproven effect, and an unfinished empty summary is not a proof; a verified empty summary is valid.

Separately compiled, indirect and generic-requirement calls use validated public summaries or requirement Effect contracts. Without an optional effect guarantee, unproven effects propagate to the potentially affected roots; private bodies are not inspected, and possible implementations are not enumerated. Missing mandatory artifact data is an artifact error, not a missing optional guarantee. Receiverless helpers may publish input-root summaries without an ObjectCallCompatible status.

An implementation succeeds only when normal semantic checking completes and no admitted binding has a receiver violation or unproven effect. Pending call and conformance obligations remain explicit until resolved. The effect fixed point does not prove a circular conformance declaration; the normal proof deadlines and errors still apply.

#### 12.4.4.3. Implementation families

An operation's public status is Proven exactly when the ordinary body and **every implementation in its closed explicit-specialization set** pass §12.4.4.2; a semantically valid family is otherwise NotProven. The set is closed after environment selection, generation and declaration collection, regardless of whether its implementations are currently called.

The ordinary generic body is verified over all admitted bindings independently; bindings covered by specializations are not subtracted. Each specialization is verified under its substituted inherited contract. The ordinary body's inferred Proven imposes no extra declaration obligation on a specialization: an incompatible specialization makes the family NotProven but is not a declaration error for that reason, while declared Signature, Constraint, Safety and Effect obligations still apply. Improving the ordinary body therefore cannot invalidate an unchanged specialization.

A use is checked against the original public contract before an implementation is selected (§8.8.3). A NotProven diagnostic identifies the responsible implementation and reason, including specialization arguments and dependent callee or witness identities where relevant; private bodies need not be exposed. The completed summaries, status and dependencies are published under §18.3 and revalidated on change under §21.3.4.
