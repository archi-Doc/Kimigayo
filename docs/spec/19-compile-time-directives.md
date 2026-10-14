# 19. Compile-time directives

[Specification index](../SPEC.md)

Directives select source syntax during compilation; they produce no runtime control flow. `#if` includes or excludes one syntax node; `#switch` introduces a **Case Group** whose `#case` arms are alternatives. An uppercase-initial `#Name` is an Attribute, not a directive ([§6.7](06-declarations-and-containers.md#67-attributes)).

## 19.1. Placement and structure

A directive selects whole items of the list that contains it:

| Surrounding list | Items a directive may select |
| --- | --- |
| SourceDocument root | Source declarations, source-local aliases and the executable items allowed there |
| group, rootgroup, struct body | The Container items allowed by that kind |
| enum body | Enum Cases, functions, Constraints, associated-Type specifications |
| contract body | Requirements, associated-Type declarations, Constraints |
| Conditional-conformance implementation block | Functions, computed members and associated-Type specifications |
| Executable or function body | Executable items and a function's leading Constraints, including Origin relations |

Directives never replace part of an expression, Pattern or Type, and never appear directly in match-arm, parameter, argument, generic, accessor, Capture, Tuple or collection-element lists. An indented executable body nested in an expression, such as a Closure body, is an executable body and may contain directives; a single-item Body cannot contain a directive ([§14.2](14-control-flow.md#142-bodies-and-evaluation-contexts)).

`#if Condition` controls the next syntax node at the same indentation, or one indented Block of items. The forms of `#if`, `#switch` and `#case` are defined in [§F.11](appendices/F-syntax-summary.md#f11-compile-time-directives). A Case Group ends with its `#switch` body, so two groups may be adjacent. A `#switch` is one item, which an `#if` may control.

Selected items are spliced into the surrounding list in source order. Directive indentation creates no scope, lifetime boundary, cleanup point or control-transfer target: a selected `defer` or local belongs to the surrounding scope and is visible to later items there. After selection and source generation, the final declaration tree is validated ([§6.1.1](06-declarations-and-containers.md#611-source-root-and-nested-containers)).

```kimi
#if debug
    let logging = true
    let assertions = true

func run() -> ()
    #switch
        #case windows
            useWindowsPath()
        #case linux
            useLinuxPath()
        #case _
            useGenericPath()
    #if debug
        defer => log("run done")   // Registers in the body of run.
    work()

#case debug                        // Error: #case outside a #switch body.
    log("misplaced")
```

```kimi
struct Box<T>
    let value: T
    Self is Describe when T is Describe
        #if debug           // Valid: directives select items of a conditional-conformance block.
            func describe(self) -> string => "Box(debug)"
        #if not debug
            func describe(self) -> string => "Box"
```

## 19.2. Conditions

A Condition has Type `bool` and uses only these forms, with ordinary precedence:

| Form | Rule |
| --- | --- |
| `true`, `false`, integer and string literals | Integers are `i64` and must fit; a sign is allowed only directly on an integer literal. Strings have no interpolation. |
| Condition Name | A built-in Condition ([§19.2.1](#1921-built-in-conditions)) or a CompileTimeSettings entry of the module. |
| `not E`, `E and E`, `E or E` | `bool` operands. |
| `E == E`, `E != E` | Operands of the same Type: `bool`, `i64` or `string`; strings compare ordinally and case-sensitively. |
| `(E)` | Grouping. |

Every other form is an invalid Condition, including `is` tests, calls, member access, indexing, arithmetic, ordering comparisons, conversions, collections, and floating-point, character and null literals. A Condition never depends on a Type, Semantics, Contract, Origin or generic argument, in any scope.

Condition Names resolve case-sensitively ([§2.5](02-source-and-lexical-structure.md#25-names)) only among the built-in Conditions and the module's CompileTimeSettings ([§20.2.1](../impl/20-compilation-configuration.md#2021-targets-and-compile-time-settings)). Declarations, generic parameters, aliases and runtime values neither supply nor shadow them, and Constraints add none. `Windows` is not `windows`. The Condition Names of a module, with their Types, are the same in every Compilation; only their values differ. So an unknown Name is unknown for every target and build mode.

```kimi
#if os == "windows" or os == "linux"
    func supported() -> bool => true

#if debug and featureEnabled          // featureEnabled: a configured bool setting.
    func trace() -> () => log("trace")

#if T is i32                          // Error: Type tests are not Conditions.
    func wide() -> () => ()
```

### 19.2.1. Built-in Conditions

| Name | Type | Value |
| --- | --- | --- |
| `os` | `string` | The target OS family: `"windows"`, `"linux"`, `"macos"` or `"unknown"` |
| `arch` | `string` | The target architecture family: `"x86_64"`, `"x86"`, `"aarch64"`, `"riscv64"` or `"unknown"` |
| `windows`, `linux`, `macos` | `bool` | `os == "windows"`, `os == "linux"`, `os == "macos"` |
| `debug`, `release` | `bool` | The build mode; `release == not debug` |
| `pointerWidth` | `i64` | The raw-pointer width in bits |

`os` and `arch` are derived from the target ([§20.2.1](../impl/20-compilation-configuration.md#2021-targets-and-compile-time-settings)). A module's CompileTimeSettings must not reuse a built-in Name.

```kimi
#switch
    #case os == "windows" and arch == "x86_64"
        func platformName() -> string => "win-x64"
    #case _
        func platformName() -> string => "other"
```

## 19.3. Evaluation and selection

The target, build mode and the module's CompileTimeSettings fix every Condition input before parsing. A Condition evaluates to True, False or Error. Error means an invalid form, an unknown Name, mismatched operand Types or a non-`bool` result; it is a compile-time error and is absorbing for `not`, `and` and `or`. Both operands are always validated, so `false and missing`, `true or (T is i32)` and `false and 1` are errors. Every Condition in a SourceDocument is validated, including those in excluded syntax ([§19.4](#194-excluded-syntax)), in later `#switch` arms and in a misplaced `#case`.

Only directives outside excluded syntax select:

- An `#if` selects its target when its Condition is True.
- A `#switch` whose structure and Conditions are all valid selects its first True arm, or `#case _` when none is True. With no True arm and no `#case _`, it is an error; omitting `#case _` thus rejects environments that match no arm.

Selection completes during parsing. There is no deferred Condition: generic instantiation, name resolution and source generation never supply Condition inputs or reselect a directive, and generated source sees the same inputs. Names in the selected items resolve afterwards under the ordinary rules of the surrounding scope. Another target or configuration needs its own Compilation.

```kimi
func width<T>() -> i64
    #switch
        #case pointerWidth == 64
            return 64          // Selected for every T in this Compilation.
        #case _
            return 32

#if false and 1                // Error: 1 is not bool, although false decides the result.
    useFeature()
```

## 19.4. Excluded syntax

**Excluded syntax** is the target of an `#if` whose Condition is False or Error, every arm of a `#switch` except the selected one (all arms when none is selected), and the body of a `#case` outside a `#switch` — together with everything inside it. Directives inside excluded syntax select nothing and report no selection error.

Excluded syntax receives every source check of selected syntax and no semantic check:

- **Checked everywhere:** encoding, tokens, layout and grammar; modifier, Attribute and directive placement; nonempty bodies ([§14.2.1](14-control-flow.md#1421-body-forms)); placement rules (below); Condition validation.
- **Checked only in selected syntax:** selection, Names, Types, ownership, Attribute resolution, count and uniqueness rules, declaration merging, documentation association and all later compilation.

Placement rules are judged in source order before selection, over selected and excluded items alike: aliases precede ordinary declarations and executable items ([§18.2.1](18-modules-and-dependencies.md#1821-forms-and-targets)), and Constraints and Origin relations stay in a leading region ([§7.5](07-functions-and-callable-values.md#75-function-constraints), [§11.6](11-properties.md#116-property-types-and-origins), [§15.3.3](15-ownership-and-lifetime-analysis.md#1533-declaration-attached-relations)). A directive is not an item; the items in its targets and arms count by their own kinds. Count and uniqueness rules, such as no duplicate declarations and at least one enum Case, apply after selection to selected items only. The nonempty-body rule is a source rule: a body whose items are all excluded remains valid.

Source checks read no Condition value, so a lexical, grammar, placement or Condition error in a non-generated SourceDocument occurs in every Compilation of its module; a diagnostic in excluded syntax has the same code and primary location as in selected syntax. Excluded syntax contributes no declaration, item, Constraint or Attribute.

```kimi
#if windows
    alias WindowsSupport
#if linux
    alias LinuxSupport
alias Kimi.Console          // Valid: only aliases precede it.

func process<T>(value: T) -> ()
    #if debug
        log("process")
    T is Copy               // Error in every build: follows an executable item.

#switch
    #case windows
        func platformName() -> string => "windows"
    #case _
        func platformName() -> string => "other"   // Valid: uniqueness is judged after selection.

#if windows
    #if useDirectWirte      // Error in every build: unknown Name.
        useDirectWrite()
```
