# 19. Compile-time directives

[Specification index](../SPEC.md)

After directive selection and source generation, validate the final declaration tree against §6.1.1. A selected rootgroup remains legal only at the source root. Eligibility checks for tests, specializations and foreign declarations include inherited Type/Semantics/Origin parameters, even through groups and when unused.

Compile-time directives choose source syntax without runtime branching. The following terms are used in this chapter:

| Term | Meaning |
| --- | --- |
| Condition | A compile-time Boolean expression controlling syntax selection. |
| Prepared environment | Fixed target values and configured compile-time settings available before source selection. |
| Environment selection | A choice fixed by target values and configured Project settings, independently of generic arguments. |

Compile-time Directives select Syntax during compilation without producing runtime control flow:

| Form | Purpose |
| --- | --- |
| `#if` | Independently includes or excludes one Syntax node. |
| `#switch` | Introduces an ordered Case Group and selects one arm. |
| `#case` | Introduces an arm directly inside a `#switch` body. |
| `#Name` | Attaches an Attribute; it is not a Compile-time Directive. |

## 19.1. Syntax and structural selection

Directive placement is restricted to whole items in these categories:

| Permitted surrounding list | Selected item category |
| --- | --- |
| SourceDocument root | Source declarations, source-local aliases, and executable items allowed there |
| group, rootgroup, struct body | Container items allowed by that particular kind |
| enum body | Enum Cases, functions, Constraints, associated-Type specifications |
| contract body | Requirements, associated-Type declarations, Constraints |
| Executable/function body | Executable items and a function's permitted leading Constraints |

Directives cannot replace part of an expression, Pattern, or Type, or appear directly in runtime match-arm, parameter/argument, generic, accessor, Capture, or Tuple/collection-element lists. An indented executable body nested in an expression remains a permitted item list; a single-item Body cannot directly contain a directive (§14.2). Select a whole allowed item. #case occurs only directly in a #switch directive body. Attributes obey §6.5 and do not expand these permissions.

`#if` controls either the next Syntax node at the same indentation or one indented Block:

**Basic example.**

```kimi
#if windows
alias Kimi.Windows

#if debug
    let logging = true
    let assertions = true
```

A **Case Group** is introduced by `#switch` and consists of the `#case` arms indented one level under it. The group's extent is the `#switch` body; nothing outside that body joins the group, so two Case Groups may appear adjacently. Select the first matching arm in source order. The optional catch-all `#case _` must occur once at most, as the final arm.

```kimi
func useImplementation<T>(value: T) -> ()
    #switch
        #case windows
            useWindowsImplementation(value)
        #case linux
            useLinuxImplementation(value)
        #case _
            useGenericImplementation(value)

    #switch
        #case pointerWidth == 64
            useWidePath(value)
        #case _
            useNarrowPath(value)
```

A `#switch` header has no subject expression. Its body must contain at least one `#case` arm and contains only such arms, apart from blank lines and comments. Each arm must have an indented Block. A `#case` outside the direct arm list of a `#switch` body is an error; nested selections require their own `#switch`. Blank lines and comments do not split a group within its body. Runtime `match subject` retains its separate Pattern syntax (§14.8); `#match` is not a directive or an alias for `#switch`.

A `#switch` construct is one Syntax item and may be controlled as a whole by a preceding `#if`. Directive indentation groups source items for selection; it does not introduce a runtime Block or lookup scope. This applies to both indented `#if` targets and `#case` bodies, in executable bodies and Declaration Containers.

Every valid Case Group must select an arm for the prepared environment. Without `#case _`, at least one explicit Condition must evaluate to **True**; otherwise report an error. No generic dependency can defer selection. A catch-all supplies an unconditional alternative, but does not suppress errors in other Conditions.

Splice selected items into the surrounding list in source order, retaining their SourceDocument and CodeContext. The directive creates no lifetime boundary, cleanup point, defer registration scope, or control-transfer target. A selected `defer` registers in the surrounding executable scope; selected locals live and are destroyed there under ordinary rules. Explicit constructs within selected items retain their own scopes. An early-false `#if` target is excluded. Unselected `#case` arms do not undergo ordinary semantic checking or contribute executable code.

The [nonempty Block rule](14-control-flow.md#1421-nonempty-executable-blocks) checks source structure before selection. Removing all executable Syntax does not itself make a Block invalid.

Validation of excluded targets follows [Diagnostics and excluded syntax](#195-diagnostics-and-excluded-syntax).

## 19.2. Environment condition forms

A Condition uses only the following closed expression set over the prepared Compilation environment. The whole expression must have Type `bool`; ordinary operator precedence and explicit parentheses apply.

| Form | Rule |
| --- | --- |
| `true`, `false`, integer and string literals | Integers have Type `i64` and must fit its range; a leading `+` or `-` is permitted only directly on an integer literal. Strings use the ordinary literal rules, without interpolation. |
| Compile-time value Name | A built-in Compilation value or an explicitly configured Project setting. |
| `not E`, `E and E`, `E or E` | Boolean operands and the [Condition evaluation rules](#193-condition-evaluation-and-selection). |
| `E == E`, `E != E` | Matching operand Types: `bool`, `i64`, or `string`. No implicit cross-Type conversion; string comparison is ordinal and case-sensitive. |
| `(E)` | Grouping of one permitted expression. |

All other expression forms are invalid Conditions, including every `is`/`is not` test, calls, runtime member access, indexing, arithmetic, ordering comparisons, conversions, collections, interpolation, and floating-point/character/null literals. Reject them even in short-circuited operands or later Conditions after a selected Case. There are no Type-, Semantics-, Contract-, Origin-, or generic-specialization-dependent directive conditions. This restriction applies in every scope, including function bodies.

**Condition lookup.** Resolve Names only in the disjoint built-in and Project-setting environment established before parsing, using the case-sensitive Name rules of §2.5. Built-ins use the spellings in §20.4: `Windows` does not select `windows`, and `POINTERWIDTH` does not select `pointerWidth`; either is unknown unless independently configured as a Project setting. Ordinary declarations, generic parameters, aliases, Types, Contracts, and runtime values neither supply nor shadow Condition values. A missing Name is an error, not a dependency for generic Binding. Constraints supply no additional Condition values or narrowing facts.

```kimi
windows
windows or linux
os == "windows" or os == "linux"
pointerWidth == 64
debug and featureEnabled // featureEnabled must be a configured bool setting.
```

```kimi
#if T is i32            // Error: Type conditions are not supported.
    ()
#if false and (s is ref) // Error even though false determines truth.
    ()
```

Constraint Clauses and ordinary runtime Type tests retain their separate rules. Environment directives select syntax but introduce no Type or capability assumptions into generic proof.

## 19.3. Condition evaluation and selection

`#if` and `#switch` Conditions use the same evaluation rules. All valid Condition inputs are fixed by the target and Project settings before source selection. Language evaluation has exactly three outcomes:

| Result | Meaning |
| --- | --- |
| **True** | The Condition is satisfied. |
| **False** | The Condition is not satisfied. |
| **Error** | Invalid syntax, an unavailable Name, incompatible operand Types, or a non-Boolean Condition. |

There is no Deferred Condition result. Generic Binding and instantiation cannot supply missing Condition inputs and never change a valid selection within one fixed Compilation environment.

Truth determination does not waive validation. Validate both operands of `and` and `or`, even when one determines truth. **Error** is absorbing for `and`, `or`, and `not`. Otherwise evaluate their ordinary Boolean meaning. For example, `false and missing`, `true or missing`, `debug and missing`, `false and 1`, and `true or (T is i32)` are errors when reached; neither short-circuit truth nor build mode hides the invalid operand.

Evaluate the single Condition of a `#if` and validate every explicit Condition of a reached `#switch`, including later arms whose values cannot change the selection. After successful validation, select the first True arm; False arms are skipped. If none is True, select `#case _` when present; otherwise report an error. No arbitrary theorem proving or enumeration of Types is involved.

Directive validation reaches source items, True #if targets, and **all arms** of a reached #switch; it stops at False #if interiors. Thus an unselected #switch arm still validates nested Conditions unless a False #if encloses them. This source traversal is independent of parser scheduling and semantic reachability; [excluded-syntax rules](#195-diagnostics-and-excluded-syntax) specify the remaining checks.

## 19.4. Name-resolution boundary

Select directives using only the prepared environment before resolving Names in the affected source. The scope's lookup environment includes selected declarations, overload candidates, and aliases. [Mods](20-compilation-configuration.md#207-mods-source-generation) may add selected declarations during generation and invalidate provisional Binding; final Binding uses the complete generated declaration set. Generated source uses the same fixed prepared environment. Extension imports are not introduced, and generation, instantiation, and implementation selection never reselect an existing directive or change Condition inputs.

Environment directives may select declarations, imports, or local syntax for a fixed target/configuration. Resolve the spliced items under their surrounding scope's ordinary visibility rules (§19.1); selected local declarations are visible to subsequent items in that scope. For example, `logging` and `assertions` in §19.1 are available after the directive when `debug` is true and are absent otherwise.

```kimi
#if windows
func platformName() -> string => "windows"

func example<T>() -> i32
    #switch
        #case pointerWidth == 64
            let result = 64
            return result
        #case _
            return 32
```

The selected body of `example` is the same for every `T` in that Compilation. Source Types and member declarations cannot affect the prepared environment. Different target/configuration inputs require their own Compilation and selection.

## 19.5. Diagnostics and excluded syntax

**Excluded Syntax.** Tokenize every SourceDocument and diagnose all encoding, token, and indentation errors. Validate each reached #if’s complete Condition before deciding target grammar checks. In a False target, check only balanced Blocks, required executable bodies, and #switch/#case structural placement and nonemptiness. Skip ordinary expression/declaration grammar; an incomplete initializer is allowed there. Parse every reached #switch arm, applying the same nested False #if exception. Unselected arms skip ordinary semantic checking.

Speculative parsing cannot change acceptance. Suppress speculative ordinary-grammar errors in confirmed False #if targets; retain mandatory token/layout/structure errors. Validate reached Conditions immediately against the prepared environment; truth cannot hide invalid operands or missing Names. Unknown Names are Error, never False or an instantiation dependency, regardless of caching or evaluation schedule.

| Check | False `#if` target | Unselected arm of a reached `#switch` |
| --- | --- | --- |
| Tokenization and indentation | Required | Required |
| Block/directive structure and source-level nonempty executable bodies | Required | Required |
| Ordinary expression/declaration grammar | Skipped, including speculative diagnostics | Required, except inside nested False #if targets |
| Nested Directive Condition evaluation | Skipped | Required under the source-defined traversal, except inside False #if targets |
| Ordinary Name/Type/ownership checks, lowering and code generation after exclusion | Skipped | Skipped |

The controlling #if Condition and all explicit Conditions of the current #switch are checked independently of their targets under [Condition evaluation](#193-condition-evaluation-and-selection). An uppercase-initial #Name has Attribute syntax; other lowercase hash forms are errors under §6.5. Resolving an Attribute is not required in excluded Syntax.

**Error example.**

```kimi
#if false and 1
    useFeature()
```

The numeric operand is not Boolean. Short-circuit truth does not waive validation, so the Condition is a compile-time error.
