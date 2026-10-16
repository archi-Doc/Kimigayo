# 18. Modules and dependencies

[Specification index](../SPEC.md)

A module groups source declarations and controls which names other modules can reach. Project-file syntax, settings, commands and package, lock and release formats are implementation contracts ([§20.2](../impl/20-compilation-configuration.md#202-settings), [§20.3](../impl/20-compilation-configuration.md#203-commands), [§20.5](../impl/20-compilation-configuration.md#205-packages-and-locks)).

## 18.1. Modules and source environments

| Term | Meaning |
| --- | --- |
| Kotonoha | One named module. |
| Project root | The top of a Kotonoha's declaration hierarchy; its declarations are the **root declarations**. |
| Compilation root | Per Kotonoha: its root declarations, the reference names of its own direct dependencies, and the reserved `Kimi`. |
| Source environment | A SourceDocument's aliases and lookup context. |

Lookup stage 4 ([§9.5](09-names-signatures-and-access.md#95-unqualified-lookup)), `::` paths ([§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)) and alias targets use the Compilation root of the Kotonoha that contains the SourceDocument, never the consumer's.

A resolved Symbol identifies its declaration, including its originating Kotonoha and version, independently of spelling or alias path. Bodies, Types and Constraints of a declaration are always checked in its definition environment; a consumer can neither add declarations to another module nor rebind its environment.

Each fragment of a merged declaration keeps the source environment of its own SourceDocument; merging never applies one fragment's aliases to another. A generated document has its own source environment.

```kimi
// A.kimi
alias Kimi.Console
struct Config
    func show() -> () => writeLine("a")   // Valid: A.kimi opens Kimi.Console.

// B.kimi
struct Config
    func hide() -> () => writeLine("b")   // Error: B.kimi does not open Kimi.Console.
```

## 18.2. Aliases

Source names only directly referenced Kotonoha, by their reference names. External members are reached by qualification, such as `Geometry.Shapes.Circle`, or through an alias; they are never found unqualified. A Kotonoha that is only a transitive dependency is not nameable.

### 18.2.1. Forms and targets

| Form | Meaning |
| --- | --- |
| `alias Path` | Opens the direct members of the target. |
| `alias Name => Path` | Introduces the target itself as a Type-namespace Qualifier `Name`. |

- **Placement.** Aliases appear at the top level of a SourceDocument, before any ordinary declaration or executable item. The order is judged in source order over selected and excluded items alike ([§19.4](19-compile-time-directives.md#194-excluded-syntax)); an alias in a root directive target or arm is a top-level item. Nested aliases and access modifiers on aliases are errors.
- **Opening target.** `alias Path` opens a Kotonoha reference or any fully bound Declaration Container (group, struct, enum or contract). Its direct members join lookup stage 5 ([§9.5](09-names-signatures-and-access.md#95-unqualified-lookup)) in their own namespaces. Formal parameters, Origins and `Self` are not opened. Lookup of opened members, including the qualification they need for Cases and receiver members, follows [§9.5](09-names-signatures-and-access.md#95-unqualified-lookup).
- **Named target.** `alias Name => Path` targets only a Kotonoha or a group, including a group under an instantiated parent with all Type and Origin arguments bound. It does not open the target's members, has no generic parameters, and creates no Core, value, storage or declaration identity.
- **Resolution.** The whole target, including Type arguments and Origin specifications, resolves from the Compilation root of the alias's Kotonoha ([§18.1](#181-modules-and-source-environments)), without source or default aliases. A leading `::` is optional. Built-in syntax such as `i32` and `during static` stays valid. There are no alias chains or cycles, and alias order does not affect resolution.

Neither form adds Container placements or ways to instantiate Containers ([§6.1](06-declarations-and-containers.md#61-declaration-containers)). The right side is a Container reference, not an expression.

```kimi
// Counter.kimi declares struct Counter with `var count: i32` and `func zero() -> Counter`.
alias Counter
alias Kimi.Option<i32>
alias Output => ::Kimi.Console
alias K => Kimi
Output.writeLine("Hello")
K.Console.writeLine("Hello")
let c = zero()        // Valid: a Type function of Counter.
let n = count         // Error: an instance member needs a receiver; write c.count.
func pick() -> Option<i32>
    return Some(1)    // Error: write Option<i32>.Some(1) or .Some(1).
```

```kimi
alias K => Kimi
alias Output => K.Console          // Error: K exists only as an alias.
alias Number => i32                // Error: a Core is not a named-alias target.
alias O => Kimi.Option<i32>        // Error: an enum is not a named-alias target.
alias f => Kimi.Console.writeLine  // Error: a function is not a Container.
alias Number = i32                 // Error: there is no Type-alias syntax.
```

### 18.2.2. Meaning and scope

An alias fixes its reference at the declaration: the original declaration identity and its argument bindings. It is neither path substitution nor reinference at the use. The alias declaration checks access, Type formation, input conditions and Origin obligations of the target and its intermediate qualifiers, and proofs follow Finality ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)). Member access and use conditions are checked again at each use; an alias supplies neither access rights nor conformance evidence.

An alias applies only to its own SourceDocument. It does not reach other documents, merged fragments, generated documents or callers, is not re-exported, and creates no root member addressable as `Project.A` or `::A`. A real root declaration or direct reference with the same spelling stays usable. Collisions follow [§9.5.1](09-names-signatures-and-access.md#951-aliases).

Opened members are deduplicated only when they are the same declaration with equal full bindings. Opening both `Family<i32>.Helpers` and `Family<string>.Helpers` leaves two candidates for an inner `Token`, and the ordinary same-name ambiguity applies.

```kimi
// Definitions.kimi
struct Family<T>
    public group Helpers
        public struct Token

// Use.kimi, in the same Kotonoha
alias Family<i32>.Helpers
group Use
    func make() -> Token => Token.init()   // Valid: Family<i32>.Helpers.Token.
```

### 18.2.3. Default aliases

Every SourceDocument, including dependency and generated sources, has a mandatory default alias that opens the reserved Kimi Kotonoha selected for the graph. Configuration cannot remove it or change its target. A module may configure additional default aliases ([§20.2](../impl/20-compilation-configuration.md#202-settings)); they apply to that module's documents only.

Mandatory and additional default aliases form lookup stage 6 together, and identical resolved references are deduplicated within that stage. Explicit source aliases form the earlier stage 5, so a source `alias Kimi` can change precedence. Adding a default alias that is already mandatory changes nothing.

```kimi
// The module configures no additional default aliases.
Console.writeLine("Hello")   // Valid: the mandatory alias opens Kimi.
writeLine("Hello")           // Error: opening Kimi exposes Console, not its members.
```

## 18.3. Name reachability across Kotonoha

Re-exports are not part of this revision; aliases are not re-exports. A consumer that names an external Type or requirement must directly reference its originating Kotonoha and have an accessible Symbol path to it (Name Reachability, [§9.4](09-names-signatures-and-access.md#94-access)). Neither a public Signature that contains the Type nor loading a transitive dependency supplies reachability.

```kimi
// Geometry depends on Math; Math depends on Units. Math.Arithmetic.scale returns Units.Meter.
alias Math.Arithmetic
alias Units.Meter         // Error: Units is not a direct dependency of Geometry.
let size = scale(2)       // Valid: no Units name is written.
```

## 18.4. Dependencies

### 18.4.1. Identities and reference names

| Identity | Meaning |
| --- | --- |
| ReferenceName | The consumer's source name for a direct dependency. |
| PackageId / PackageVersion | The defining module and its exact release. |
| SourceId | The exact content of a source package ([§18.5](#185-source-packages)). |

A Kotonoha is identified by its PackageId and PackageVersion; one without a PackageId (an Application or an unpacked Project) is identified by its Project. Two versions of one PackageId are distinct Kotonoha: `internal` access and Symbol identity do not cross them. A module may reference two versions of one PackageId directly, each under its own ReferenceName.

A PackageId starts with a lowercase ASCII letter and consists of nonempty segments of lowercase ASCII letters and digits separated by `.` or `-`. A PackageVersion starts with an ASCII letter or digit, followed by ASCII letters, digits, `.`, `-` and `+`. Versions compare by exact, case-sensitive equality; no ordering or compatibility is inferred. A referenced Library and a packed Project need both; an Application needs neither to consume dependencies.

A ReferenceName is an ordinary identifier and must not be `Kimi`. A root declaration, in any namespace, must not use a direct-dependency ReferenceName or `Kimi`; root declarations among themselves follow the ordinary namespace rules ([§9.2](09-names-signatures-and-access.md#92-namespaces-and-lookup-roles)). In the Kimi Kotonoha's own sources, `Kimi` names that Kotonoha, and the mandatory default alias opens it. Several names or paths for one dependency create no new Types or statics.

```kimi
// Geometry has a dependency named Math.
group Kimi          // Error: `Kimi` is reserved in the Compilation root.
    func f() -> () => ()
struct Math         // Error: Math is a dependency reference name.
```

### 18.4.2. Dependency graph

The module graph is acyclic. A reference to the module's own identity and a cycle are errors; mutually dependent declarations belong in one Kotonoha. Within a target graph, including its test extension, each identity, including the root's own, has one input kind (Project or Package), one content and one semantic configuration. A test dependency that reaches the root's own identity therefore joins the root product node; a different input kind or content is an error. Identical nodes reached by several paths are merged. A Project is never substituted for a Package or the reverse.

Each module keeps its own aliases, default aliases, access context, declarations and closed specialization set. The graph uses one Kimi Kotonoha.

Geometry's project file declares its dependency on Math; the settings are listed in [§20.2](../impl/20-compilation-configuration.md#202-settings).

```text
PackageId="example.geometry"
PackageVersion="1.0.0"
Dependencies=
  Math={ PackageId="example.math" PackageVersion="1.2.0" Project="../Math/Math.kimiproj" }
```

```kimi
// Math/Arithmetic.kimi
public group Arithmetic
    public func twice(value: i32) -> i32 => value + value

// Geometry/Measure.kimi
alias Math.Arithmetic
public group Measure
    public func doubled(value: i32) -> i32 => twice(value)
```

### 18.4.3. Resolution and pinning

Each dependency names a PackageId and PackageVersion and is supplied by a Project or a source package ([§20.2](../impl/20-compilation-configuration.md#202-settings)). A dependency record names its content by `Project` (a `.kimiproj` path) or `Package` (a `.kimipkg` path), never both; with neither, candidates come from `PackageSources`, which lists explicit packages and store directories. Paths are relative to the declaring project. The loaded identity must equal the requested one. A Package dependency resolves to exactly one SourceId: conflicting candidates are an error, a first match is never chosen, version ranges are never chosen and missing sources are never guessed. Test-only inputs never supply product candidates. A source package fixes the SourceIds of its dependencies; they never change. A lock pins the SourceIds of a Project's Package dependencies, and only an explicit restore changes a lock pin ([§20.5](../impl/20-compilation-configuration.md#205-packages-and-locks)).

### 18.4.4. Effective environment

One target and one Debug setting apply across the graph, and every dependency must allow that target by exact triple-string equality ([§20.2.1](../impl/20-compilation-configuration.md#2021-targets-and-compile-time-settings)). CompileTimeSettings belong to the defining module and are never supplied by a consumer. Dependency-edge overrides, feature unions and conditional dependency edges do not exist in this revision.

The whole graph is checked under one language version ([§20.2.2](../impl/20-compilation-configuration.md#2022-language-version)) by the consumer's compiler; the publisher's compiler does not matter. A packed Project's settings come only from its own declarations or from defaults fixed by its language version, and the package records them.

```kimi
// Math's source; Math's own CompileTimeSettings decide `fast`, whatever Geometry sets.
public group Arithmetic
    #if fast
        public func half(value: i32) -> i32 => value / 2
```

## 18.5. Source packages

Kotonoha are distributed as source packages. A source package holds one Library Kotonoha: its product sources with their logical names ([§20.1](../impl/20-compilation-configuration.md#201-projects-and-sources)), its PackageId and PackageVersion, LangVersion, Targets, CompileTimeSettings, user-added default aliases, native requirements, and the SourceIds of its dependencies. A SourceId identifies the exact package content. The package format is implementation-defined ([§20.5](../impl/20-compilation-configuration.md#205-packages-and-locks)).

- A package holds the original conditional source, not a rewrite with branches selected. The Compilation's target and Debug setting, together with the package's own CompileTimeSettings, select the branches ([§19.2](19-compile-time-directives.md#192-conditions)).
- Package Targets state the allowed use environments, not successful verification. Every consumer checks the package in its own environment.
- Distribution never changes definition-site lookup, access, generic selection or ownership boundaries.

Binary interfaces, version ranges and network registries are not part of this revision ([§D](appendices/D-deferred-features.md#appendix-d-deferred-features)).

```kimi
// Packed unchanged; a consumer building for windows selects the first arm.
#switch
    #case os == "windows"
        func lineEnd() -> string => "\r\n"
    #case _
        func lineEnd() -> string => "\n"
```

## 18.6. Product and test inputs

A Project's sources are those of [§20.1](../impl/20-compilation-configuration.md#201-projects-and-sources). Files listed in `TestSources` are test-only, even inside the project directory. Test dependencies are resolved only for tests and never reach the product.

- **Membership.** Declarations in TestSources, and Test functions in other sources, are test-only. A nested declaration inherits the membership of its enclosing declaration. Every other declaration, unmarked helpers included, is a product declaration. Generated declarations carry the membership of their inputs: product generation cannot consume test-only inputs, and generated Test functions are test-only. Directory and file names such as `tests` have no meaning.
- **Product fixed.** Product lookup, overloads, Types, layout, conformance, specialization, initialization and destruction are decided from product inputs alone. Test-only code changes none of them, including through inline `#Test` edits.
- **What tests may do.** Tests use product declarations and dependencies as they are. They may add functions to a product Container, which never become product candidates; the duplicate-declaration and access rules still apply. They must not change a product Type's Fields, bases, conformance or other fixed meaning, reassign a reference name or change a dependency's content. A test instantiation of a product generic keeps product definition-time lookup.
- **Propagation.** A dependency's own tests and test dependencies do not propagate.

```kimi
// tests/MeasureTests.kimi, listed in TestSources
group MeasureTests
    #Test
    func doubled()
        $expect(Measure.doubled(3) == 6)
```
