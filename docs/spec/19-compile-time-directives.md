# 19. Compile-time directives

[Specification index](../SPEC.md)

Compile-time directives select source syntax during compilation, without producing runtime control flow.

| Term | Meaning |
| --- | --- |
| Condition | A compile-time Boolean expression controlling syntax selection. |
| Prepared environment | The fixed target values and configured compile-time settings available before source selection. |
| Environment selection | A choice fixed by target values and configured Project settings, independently of generic arguments. |
| Excluded syntax | The syntax a directive does not select, with everything inside it (§19.5). It is parsed and source-checked but never checked semantically. |

| Form | Purpose |
| --- | --- |
| `#if` | Independently includes or excludes one syntax node. |
| `#switch` | Introduces an ordered Case Group and selects one arm. |
| `#case` | Introduces an arm directly inside a `#switch` body. |
| `#Name` | Attaches an Attribute; it is not a compile-time directive. |

## 19.1. Syntax and structural selection

A directive may select only whole items, in these categories:

| Permitted surrounding list | Selected item category |
| --- | --- |
| SourceDocument root | Source declarations, source-local aliases and the executable items allowed there |
| group, rootgroup, struct body | The Container items allowed by that kind |
| enum body | Enum Cases, functions, Constraints, associated-Type specifications |
| contract body | Requirements, associated-Type declarations, Constraints |
| Executable or function body | Executable items and a function's permitted leading Constraints |

Directives cannot replace part of an expression, Pattern or Type. They cannot appear directly in runtime match-arm, parameter, argument, generic, accessor, Capture, or Tuple and collection-element lists. An indented executable body nested in an expression remains a permitted item list, but a single-item Body cannot directly contain a directive (§14.2). Attributes obey §6.5 and do not extend these permissions.

`#if` controls either the next syntax node at the same indentation or one indented Block:

```kimi
#if windows
alias Kimi.Windows

#if debug
    let logging = true
    let assertions = true
```

A **Case Group** is introduced by `#switch` and consists of the `#case` arms indented one level under it. The group's extent is the `#switch` body. Nothing outside that body joins the group, so two Case Groups may appear adjacently. The optional catch-all `#case _` may occur at most once, as the final arm. Arm selection, including the error when no arm is selected, follows [§19.3](#193-condition-evaluation-and-selection).

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

A `#switch` header has no subject expression. Its body must contain one or more `#case` arms and nothing else except blank lines and comments, which do not split the group. Each arm must have an indented Block. A `#case` outside the direct arm list of a `#switch` body is an error; a nested selection requires its own `#switch`. Runtime `match subject` keeps its separate Pattern syntax (§14.8); `#match` is neither a directive nor an alias for `#switch`.

A `#switch` construct is one syntax item, and a preceding `#if` may control it as a whole. Directive indentation groups source items for selection; it introduces no runtime Block or lookup scope. This applies to indented `#if` targets and `#case` bodies alike, both in executable bodies and in Declaration Containers.

**Splicing.** Selected items are spliced into the surrounding list in source order and keep their SourceDocument and CodeContext. The directive creates no lifetime boundary, cleanup point, `defer` registration scope or control-transfer target. A selected `defer` registers in the surrounding executable scope, and selected locals live and are destroyed there under the ordinary rules. Explicit constructs within selected items keep their own scopes. [Excluded syntax](#195-diagnostics-and-excluded-syntax) contributes no items and is never checked semantically.

The [nonempty Block rule](14-control-flow.md#1421-nonempty-executable-blocks) checks source structure before selection; removing all executable syntax does not by itself make a Block invalid. Excluded syntax receives the same source checks as selected syntax (§19.5).

After directive selection and source generation, the final declaration tree is validated against §6.1.1; a selected `rootgroup` remains legal only at the source root. Eligibility checks for tests, specializations and foreign declarations include inherited Type, Semantics and Origin parameters, even through groups and when unused.

## 19.2. Environment condition forms

A Condition uses only the following closed set of expressions over the prepared Compilation environment. The whole expression must have Type `bool`. Ordinary operator precedence and explicit parentheses apply.

| Form | Rule |
| --- | --- |
| `true`, `false`, integer and string literals | Integers have Type `i64` and must fit its range; a leading `+` or `-` is permitted only directly on an integer literal. Strings use the ordinary literal rules, without interpolation. |
| Compile-time value Name | A built-in Compilation value or an explicitly configured Project setting. |
| `not E`, `E and E`, `E or E` | Boolean operands, under the [Condition evaluation rules](#193-condition-evaluation-and-selection). |
| `E == E`, `E != E` | Operands of matching Types: `bool`, `i64` or `string`. No implicit cross-Type conversion; string comparison is ordinal and case-sensitive. |
| `(E)` | Grouping of one permitted expression. |

All other expression forms are invalid Conditions, including every `is`/`is not` test, calls, runtime member access, indexing, arithmetic, ordering comparisons, conversions, collections, interpolation, and floating-point, character and null literals. They are rejected wherever §19.3 validates a Condition, including short-circuited operands and later arms. No directive Condition depends on a Type, Semantics, Contract, Origin or generic specialization, in any scope, including function bodies.

**Condition lookup.** Names resolve only in the disjoint built-in and Project-setting environment established before parsing, using the case-sensitive Name rules of §2.5. Built-ins use the spellings of §20.4: `Windows` does not select `windows`, and `POINTERWIDTH` does not select `pointerWidth`. Either is unknown unless configured as a Project setting of its own. Ordinary declarations, generic parameters, aliases, Types, Contracts and runtime values neither supply nor shadow Condition values. A missing Name is an Error (§19.3). Constraints supply no additional Condition values or narrowing facts.

**Name invariant.** The set of Condition Names visible to a SourceDocument, and the Type of each, is the same in every Compilation of its module; Compilations differ only in values. Every built-in value is defined for every target. A future per-target, per-build-mode or command-line setting must likewise declare its Name and Type once per module and vary only its value. A Name that is unknown in one Compilation is therefore unknown in all of them, so its error is never an artifact of the current target.

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

Constraint Clauses and ordinary runtime Type tests keep their separate rules. Environment directives select syntax but introduce no Type or capability assumptions into generic proofs.

## 19.3. Condition evaluation and selection

`#if` and `#switch` Conditions share these evaluation rules. The target and Project settings fix all valid Condition inputs before source selection. Evaluation has exactly three outcomes:

| Result | Meaning |
| --- | --- |
| **True** | The Condition is satisfied. |
| **False** | The Condition is not satisfied. |
| **Error** | Invalid syntax, an unavailable Name, incompatible operand Types, or a non-Boolean Condition. |

There is no Deferred Condition result, so no generic dependency can defer selection. Generic Binding and instantiation cannot supply missing Condition inputs and never change a valid selection within one fixed Compilation environment.

Truth determination does not waive validation: both operands of `and` and `or` are validated even when one operand determines the truth value. **Error** is absorbing for `and`, `or` and `not`; otherwise they have their ordinary Boolean meaning. For example, `false and missing`, `true or missing`, `debug and missing`, `false and 1` and `true or (T is i32)` are errors wherever they appear, including in excluded syntax. Neither short-circuit truth nor the build mode hides the invalid operand.

**Validation.** Every directive Condition in a SourceDocument is validated, including Conditions inside [excluded syntax](#195-diagnostics-and-excluded-syntax) and the Condition of a `#case` outside a `#switch` body. Every explicit Condition of a `#switch` is validated, including later arms that cannot change the selection; a catch-all does not suppress their errors. Validation is independent of parser scheduling and semantic reachability.

**Selection.** Only directives outside excluded syntax select. An `#if` selects its target when its Condition is True. A `#switch` selects only when its structure and all of its Conditions are valid: the first True arm in source order is selected, or `#case _` when no arm is True. A valid `#switch` with no True arm and no `#case _` reports a selection error; omitting `#case _` thus deliberately rejects environments that match no arm. Directives inside excluded syntax select nothing and report no selection error. No arbitrary theorem proving or enumeration of Types is involved.

## 19.4. Name-resolution boundary

Directives are selected using only the prepared environment, before Names in the affected source are resolved. The scope's lookup environment includes the selected declarations, overload candidates and aliases. [Mods](../impl/20-compilation-configuration.md#207-mods-source-generation) may add declarations during generation, which invalidates provisional Binding; final Binding uses the complete generated declaration set (§20.7.2). Generated source uses the same fixed prepared environment. Extension imports are not introduced. Generation, instantiation and implementation selection never reselect an existing directive or change Condition inputs.

Environment directives may select declarations, aliases or local syntax for a fixed target and configuration. The spliced items resolve under the ordinary visibility rules of their surrounding scope (§19.1), so selected local declarations are visible to later items in that scope. For example, `logging` and `assertions` in §19.1 are available after the directive when `debug` is true and absent otherwise.

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

The selected body of `example` is the same for every `T` in that Compilation. Source Types and member declarations cannot affect the prepared environment. Different target or configuration inputs require their own Compilation and selection.

## 19.5. Diagnostics and excluded syntax

**Excluded syntax** is the following syntax together with everything inside it:

| Directive | Excluded syntax |
| --- | --- |
| `#if` whose Condition is False | Its target |
| `#if` whose Condition is Error, or whose header is invalid | Its target |
| `#switch` that selects an arm | Every other arm |
| `#switch` that selects no arm (an invalid Condition or structure, or no match) | Every arm |
| `#case` outside a `#switch` body | Its body |

Directives inside excluded syntax select nothing, so their targets and arms are excluded syntax too. When an `#if` is followed by a `#case` outside a `#switch`, that whole `#case` is the target. An Attribute written before an `#if` whose target is a single declaration attaches to that declaration and shares its selection; before a directive Block or a `#switch`, no declaration follows at the Attribute's indentation, so it is a dangling Attribute (§6.5).

**Checks.** Excluded syntax receives exactly the source checks of selected syntax and no semantic checks:

| Check | Selected syntax | Excluded syntax |
| --- | --- | --- |
| Encoding, tokens, indentation and block structure | Required | Required |
| Grammar of items, declarations, expressions, Patterns and Types; modifier and Attribute placement; directive placement and structure (§19.1); source-level nonempty bodies (§14.2.1); unavailable features | Required | Required |
| Source-order placement rules (below) | Required | Required |
| Condition validation (§19.2, §19.3) | Required | Required |
| Selection and the selection error (§19.3) | Required | Not performed |
| Names, Types and ownership; Attribute resolution; count and uniqueness rules; final declaration-tree validation (§6.1.1); declaration merging; documentation association (§2.3.3); Mod visibility; lowering and code generation | Required | Not performed |

The boundary is the one in §19.1: checks determined by the source alone, before selection and generation, apply everywhere; checks that use the final declaration tree or Names apply only to selected syntax. Apart from Condition validation, source checks read no prepared-environment value. Speculative parsing cannot change acceptance and follows the same rules in selected and excluded syntax. Conditions are validated immediately against the prepared environment; an unknown Name is an Error, never False and never an instantiation dependency, regardless of caching or evaluation schedule.

**Placement and count rules.** A placement rule requires an item to precede other items or to lie in a leading region: aliases precede ordinary declarations and executable items (§18.1.1), and Constraints and Origin relations lie in the leading region of a function, accessor or Type (§7.4, §11.3, §15.3.3). Placement is judged before selection, in source order, over selected and excluded items alike. A directive is not itself an item; the items in its target and in all of its arms count by their own kinds. Count and uniqueness rules, such as one Constraint-defining fragment, at least one selected enum Case (§6.1.1) and no duplicate declarations, are judged after selection over selected items only.

```kimi
#if windows
    alias Kimi.Windows
#if linux
    alias Kimi.Linux
alias Kimi.Console      // Valid: only aliases precede it.

func process<T>(value: T) -> ()
    #if debug
        log("process")
    T is Copy           // Error in every build: it follows an executable item in source order.

func store<T>(value: T) -> ()
    #switch
        #case debug
            log("store")
        #case _
            T is Copy   // Error: it follows the earlier arm's executable item in source order.
    return

#switch
    #case windows
        func platformName() -> string => "windows"
    #case _
        func platformName() -> string => "other" // Valid: uniqueness is judged after selection.
```

**Environment independence.** For a SourceDocument that is not generated, the code, primary location and Reason of every lexical, grammar, placement and Condition diagnostic, except the selection error, are the same in every Compilation of its module (§19.2). Compilations differ only in selection, in the semantic checks of selected syntax and in the related location that marks excluded syntax. Rewriting independent `#if` directives as a `#switch` with the same selection therefore never changes acceptance:

```kimi
#if linux
    let pending =       // Error in every build.
#if not linux
    useDefaultPath()

#switch
    #case linux
        let pending =   // The same error.
    #case _
        useDefaultPath()

#if windows
    #if useDirectWirte  // Error in every build: unknown Name, even where the outer target is excluded.
        useDirectWriteRenderer()
```

**Diagnostics.** A diagnostic in excluded syntax keeps the code, primary location, Reason and Advice it would have in selected syntax. When its primary location lies in excluded syntax, it also carries a related location with the role `excludedBy` at the innermost excluding directive: the Condition of an `#if`, or the header of a `#case` arm (§23.3.6.2). That location is not a Reason fact. An `#if` with an invalid Condition reports the Condition error and the independent syntax errors of its target; the target is never checked semantically, so it causes no Name or Type cascade. A `#case` outside a `#switch` body reports its placement error, and its Condition and body are still validated and parsed.

**Recovery.** Excluded syntax is parsed with the recovery rules of selected syntax and never affects selected syntax: it contributes no merged declaration, root runtime item, Constraint, Origin relation, omission or recovery record, documentation association or Attribute. An invalid Case Group may keep its arms for error recovery, but they remain excluded syntax. An uppercase-initial `#Name` is Attribute syntax, and other lowercase hash forms are errors under §6.5. Attributes in excluded syntax are parsed but not resolved.

Misplaced non-arm items in a Case Group receive source checks using the enclosing owner's item grammar as recovery, including its generic context and source-order placement rules. This recovery stops before the next direct `#case` or the end of the Case Group; a misplaced prefix cannot attach to that arm. These items receive no semantic checks and contribute none of the declarations or records listed above. Misplacement alone does not create an `excludedBy` location.

```kimi
#if false and 1
    useFeature()
```

The numeric operand is not Boolean. Short-circuit truth does not waive validation, so this Condition is a compile-time error. Its target is excluded syntax: it is parsed and source-checked, and any syntax error there is reported as well.
