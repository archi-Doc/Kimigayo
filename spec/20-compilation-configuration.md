# 20. Compilation configuration

[Specification index](../SPEC.md)

A Compilation processes one Project for fixed source, dependency, target and configuration inputs. The source-language rules determine meaning; this chapter defines compilation invariants and configuration, while implementation requirements and reference algorithms are in Appendices A and B.

## 20.1. Build units

The build model separates workspace orchestration, project configuration, source modules and target compilation:

| Element | Responsibility |
| ------- | -------------- |
| Solution | Holds multiple Projects and supplies options shared by their builds. |
| Project | Defines one application or library build unit, configured by a `.kimiproj` file. |
| Kotonoha | Defines a named module unit for an application or library, built from one or more SourceDocuments. |
| SourceDocument | An immutable source snapshot, including its path and text; replacing its text creates a new snapshot. |
| Compilation | Compiles one Project under one fixed set of source, dependency, target and build inputs. |

A Solution discovers and loads Projects. A Project stores target triples, aliases and [dependency declarations](18-modules-and-dependencies.md#184-dependency-configuration-and-resolution), and creates target-specific Compilations. Referenced modules keep their own definition environments. The saved alias setting contains only user additions; mandatory and additional defaults are constructed together under [§18.1.3](18-modules-and-dependencies.md#1813-effective-default-aliases), including for generated documents.

## 20.2. Build inputs

Compilation inputs comprise the full target triple (including ABI and environment), the backend and layout, the build mode and code-affecting options, the effective Project settings, the language and compiler version, the source snapshots, the resolved dependency content, and the [Mod registrations, implementations and additional inputs](#2075-inputs-and-regeneration). Actual inputs and processing records are fixed under §18.5.2. Reuse requires agreement of all facts read by the judgment (§18.7); OS and architecture, version labels and lock equality alone are insufficient. Changes to generation-only multipliers may preserve semantic plans, while target-dependent proofs remain validation inputs.

## 20.3. Target preparation

Each Compilation owns the primary Kotonoha and provides target information and compile-time variables. A target must provide the Kimigayo data-layout and ABI facts required by the language; backend-specific representations are implementation details.

## 20.4. Compile-time values

Prepared Compilations provide these values, fixed throughout analysis:

| Name | Type | Value |
| --- | --- | --- |
| `os` | `string` | Canonical lowercase OS family: `windows` for Win32, `macos` for MacOSX, `linux` for Linux; other recognized families use their lowercase target-family name, and an unrecognized OS uses `unknown`. Version suffixes are excluded. |
| `arch` | `string` | Canonical lowercase architecture family, such as `x86`, `x86_64`, `aarch64` or `riscv64`; target aliases for the same family give the same value. |
| `windows`, `linux`, `macos` | `bool` | Exactly `os == "windows"`, `os == "linux"` and `os == "macos"`, respectively. At most one is true, and all are false for other OS families. |
| `debug`, `release` | `bool` | The selected build mode and its negation: `release == not debug`. |
| `pointerWidth` | `i64` | The default raw-pointer width in bits from the prepared target layout; the supported values in this revision are 16, 32 and 64. |

Project settings provide explicit `bool`, `i64` or `string` values. Configured Names are validated under §2.5, including the pinned Unicode categories, NFC and reserved words. Exact duplicate setting names and collisions with built-in values are rejected; collisions are errors, not overrides. Under [Condition lookup](19-compile-time-directives.md#192-environment-condition-forms), `Feature` and `FEATURE` are distinct settings, and `WINDOWS` is distinct from the built-in `windows`. Spelling is preserved, and settings are copied into the prepared environment before parsing. `.kimiproj` uses a `CompileTimeSettings` map whose entries set exactly one of `Bool`, `Integer` or `String`.

## 20.5. Language-version selection

An optional `.kimiproj` `LangVersion` requests an exact supported language version. If it is omitted, ordinary builds use the root solution's version when supplied and otherwise the compiler's current version; dependencies do not discover another solution for defaults. Packing requires each converted Project to declare LangVersion and Targets explicitly and to obey the portable-default rules of §18.4.3. The initial graph has one effective language version. Unsupported requests are errors, never fallbacks. The effective language and the compiler build identity are recorded in processing metadata; equal pre-alpha language labels do not promise compiler compatibility.

The `!` argument-name boundary uses language version `0.0.2`; the former `0.0.1` parameter-name rules are not silently reinterpreted. Projects and source packages must agree with the graph's effective version. Versionless sources follow the selection above: a compiler cannot infer whether an unmarked declaration was intended for an older version. No per-file syntax switch is provided. Serialized source graphs must validate format, language version and compiler build before reparsing; incompatible or unversioned older graphs are rejected.

## 20.6. Compilation invariants

Compilation follows semantic dependencies, not necessarily whole-program passes; the [reference models](appendices/B-reference-models.md#appendix-b-non-normative-reference-models) show optional schedules. Conditions are resolved in the prepared environment. Mod analysis may use incomplete declarations under §20.7, but all generation completes before final Binding and dependent layout and operation plans are committed. Instantiation and implementation selection never reselect directives. Analyses may share facts, but unresolved obligations never count as successful finalization.

## 20.7. Mods: source generation

A **Mod** is Kimigayo's source generator: one compiler-invoked generation step. It searches and reads Koto, generates Kimigayo source, and asks the compiler to parse that source and append it to a Declaration Container. Each registered Mod runs once per Compilation, subject to failure or cancellation. A Mod may process many targets, append many fragments, or succeed without output.

Mods process generic declarations, not each generic instantiation, and may emit generic source. Each target-specific Compilation runs its own Mods. Several steps from one package use separate ModIds. There is no automatic retry, marker-driven rerun, or iteration until generation converges.

**Deferred host.** The Mod host interface and configuration — concrete query, marker-registration and context APIs, assembly packaging and compatibility checks, project configuration syntax and cache formats (§20.7.7) — are deferred ([Appendix D](appendices/D-deferred-features.md)). Until they are specified, no Mod is registered or executed. The execution, ordering, Binding, append and diagnostic rules of this section are retained for that host.

### 20.7.1. Registration and execution order

Registrations and dependency lists are frozen before execution. Each registration has:

| Field | Meaning |
| --- | --- |
| `ModId` | A stable, case-sensitive ID, unique within the Compilation. |
| `Requires` | IDs of required Mods that must finish, and integrate their output, before this Mod. |
| `RequiresAfter` | IDs of required Mods that must run after this Mod finishes and integrates its output. |

Both lists require their targets to be registered; neither registers them automatically. There is no priority. The lists combine into one graph: `A.Requires = [B]` gives `B -> A`, and `A.RequiresAfter = [B]` gives `A -> B`. Duplicate edges count once. Duplicate ModIds, missing targets, self-dependencies and cycles are rejected before any Mod runs.

Mods run sequentially. After each successful output integration and Binding update, the next Mod is the smallest ModId among the unexecuted Mods whose graph predecessors have all succeeded, compared in culture-independent Ordinal order. Dependency-list order and assembly loading order do not affect execution.

```text
A.Model.RequiresAfter = [C.Serializer]
B.Extra.RequiresAfter = [C.Serializer]
D.Report has no dependency declarations

A.Model ----+
            +--> C.Serializer
B.Extra ----+

Execution: A.Model -> B.Extra -> C.Serializer -> D.Report
```

Readiness is reconsidered after every step: `C` becomes ready after `B` and sorts before `D`. `RequiresAfter` guarantees an ordering edge, not the last position in the whole Compilation.

At entry, a Mod sees the original source and the output of every successful earlier Mod, not only its directly named dependencies. Required ordering must be declared rather than relying on incidental ID order. A consumer of all members, such as a serializer, must follow every member-producing Mod: the consumer may declare `Requires`, producers may declare `RequiresAfter`, or a transitive path may guarantee the order. Final Binding does not detect omitted serialization work if a producer runs too late.

Graph cycles differ from references between generated Types. Mutual Type references are allowed when Mods can emit them without mutually requiring completed Binding; they are validated normally after generation, including finite value layout.

### 20.7.2. Compilation and Binding

The required generation boundaries are:

```text
Fix build inputs and validate Mod graph
    -> parse original sources and select environment directives
    -> collect declarations and perform provisional Binding
    -> for each ready Mod:
         read syntax and any required Binding information
         first parse-and-append call ends Binding access for this Mod
         continue syntax searches and appends as needed
         on success, integrate declarations and update Binding
    -> after all Mods: final Binding and required semantic checks
    -> finalize layouts and operation plans, then lower and emit
```

Only new source needs parsing; reparsing all original documents after each Mod is unnecessary. Generated declarations obey the normal merge, header, duplicate-member, access, Type and ownership rules. The diagram sets dependency boundaries, not an internal order for every final check.

**Provisional results.** Whole-program Binding success is not a prerequisite for running Mods, and original source may refer to Types or members that a later Mod supplies. Queries distinguish:

| Result | Meaning |
| --- | --- |
| Resolved | The information is available in the current Binding; it is not a final commitment. |
| Unresolved | The required declarations or information are not yet available. |
| Invalid | The available information establishes a rule violation. |

Unresolved means neither permanent absence nor a promise of later generation, and a problem that later additions may resolve is not treated as a definite error. A Mod may use syntax alone; if resolved semantics are essential but unavailable, it reports an error rather than requesting a retry.

**Binding access period.** A Mod may read Binding from entry until its first parse-and-append call, which ends access for all targets, including existing Koto. A Mod that never appends may read Binding until it returns. Syntax searches and further appends remain available after the boundary, and no Binding update occurs inside the Mod.

Names, Type names, flags and other values extracted beforehand may be used to generate source, but retained Symbols or Binding objects must not provide semantic access after the boundary or from another Mod; the API must reject such access. A Mod that needs semantics for several targets gathers all required values before appending:

```csharp
// Illustrative API: Analyze returns values and a target Koto, not Symbols.
var plans = context.FindTargets()
    .Select(target => Analyze(context.Binding, target))
    .ToArray(); // Complete all semantic reads before the first append.

foreach (var plan in plans)
    context.ParseAndAppend(plan.Target, Generate(plan));
```

The compiler need not preserve an immutable Binding snapshot after an append; after a Mod succeeds, it may discard and rebuild Binding. Any incremental alternative must match full reanalysis, invalidating affected successful lookups as well as unresolved ones, because added overloads can change earlier results. Provisional results never constrain final Binding and are not verified generic-definition obligations. Before emission, all required unresolved information must be resolved and all normal checks must succeed.

### 20.7.3. Koto queries and appending source

Mods receive read-only access to existing Koto. The only mutation parses source and appends allowed declarations or members to a Declaration Container in the Compilation's target Kotonoha. The target may be original or generated, including one just added by the current Mod. Direct collection writes, deletion, replacement, renaming, body rewriting, reparenting, insertion into referenced libraries, and insertion of statements or expressions into function bodies are not permitted.

The target Koto and source text are passed directly, conceptually as `ParseAndAppend(targetKoto, sourceCode)`. No TargetContainerId, output record, output ID, parse-call number or required OriginLocation argument is introduced. Each fragment follows the target's grammar; its top level denotes direct children, without copying the target file's indentation, and its internal indentation follows the ordinary source rules.

Successful appends are immediately visible as syntax. A query fixes its result membership and order when it is called, not when enumeration starts, so later additions do not extend that result, while a fresh query can find them:

```text
Query S1 -> [A, B]
Process A; append C
Continue S1 -> B only
Query S2 -> [A, B, C]  (when this is their logical order)
```

This snapshots the result list, not the whole tree; a new member query on an existing Container may observe newly appended members. Queries use [logical declaration order](#2074-generated-sources-and-declaration-order). For merged declarations, the first fragment is the ordering key, and each API must state whether it returns fragments or merged declarations. Attribute queries follow the [Mod marker rules](06-declarations-and-containers.md#65-attributes).

### 20.7.4. Generated sources and declaration order

Each generated fragment has its own immutable SourceDocument and CodeContext under [source identity](appendices/A-compiler-requirements.md#a1-source-identity-and-incremental-analysis), even when appended below the root, and these sources are preserved for regeneration and diagnostics. Generated names resolve in the target's enclosing declaration scopes and in the generated document's own alias environment; source-local aliases of the target's original file are not inherited. Use qualified names or aliases permitted by the fragment grammar.

The compiler keeps links between the producing Mod, the source, the target Koto and the generated Koto. No per-output identity or correspondence across builds is required.

**Logical declaration order** is defined independently of layout and Mod execution order:

1. Ordinary sources precede generated declarations and are sorted by stable logical source name, then by source declaration order.
2. Generated declarations are sorted by ModId in Ordinal order; within a Mod, Koto addition order and the written order inside each fragment are preserved.

Ordinary logical names are normalized project-relative paths; external files need assigned project-relative names. Separators are normalized to `/`, redundant segments removed, and collisions rejected. Names are independent of absolute checkout and temporary paths and are compared without host case folding or locale rules. Addition order is kept directly and needs no parse-call numbering.

Identical inputs must produce identical additions and order. Changing dependency declarations alone does not change the logical order when ModIds, generated content and each Mod's addition order stay the same. Renaming sources or Mods, or changing addition order, may change initializer side-effect order. Kimigayo-layout storage uses this order under [split structures](06-declarations-and-containers.md#621-split-structures-and-storage-order). C-layout Fields occupy one fragment in written order (§21.1.2); moving that whole fragment or renaming method-only fragments does not reorder its Fields. Physical layout remains governed by §21.1. Conflicting declarations follow the normal integration rules, never last-writer-wins replacement.

### 20.7.5. Inputs and regeneration

In addition to the [general build inputs](#202-build-inputs), the Compilation records ModIds, both dependency lists, settings, Mod API compatibility, implementation assemblies and their dependencies by content, and additional files by logical name and content. The compiler provides managed access to declared additional inputs, enumerated by normalized logical name in Ordinal order. A Mod runs on the host but obtains target facts from its Compilation.

Generation must be deterministic for identical inputs. It must not depend implicitly on the current time, randomness, undeclared environment variables, file enumeration order or other external state; external data is supplied as fixed declared input. This is a Mod contract, not a promise of OS-level isolation for C# code.

Each build starts from the original inputs rather than treating previously generated Koto as original source. Each Mod's output collection is replaced, removing outputs that are no longer produced, including after a Mod or input is removed. Old Koto or Binding objects are never carried into a new Compilation. Output caches are optional; reuse must validate all relevant inputs, including earlier Mod outputs, and restore equivalent source, target associations, addition order, diagnostics and provenance. If queries can observe comments or locations, their raw source bytes are dependencies under §18.7.4. Initial source packing excludes Mod-dependent input (§18.6.1); this does not remove ordinary local Mod processing.

### 20.7.6. Failures and diagnostics

A Mod exception, reported error, invalid append operation, generated syntax error or definite integration error fails the Mod and the Compilation; a still-unresolved provisional dependency alone is not such an error. All descendants of a failed Mod in the combined graph, including `RequiresAfter` successors, are skipped. The compiler may stop all remaining Mods; continuing independent diagnostics must not expose failed partial output as valid input.

Partial Koto may be kept for inspection but is never published or cached as successful output, and a previous successful output is never substituted to make a failed build succeed.

Diagnostics keep the ModId, the generated source and position, and the target Koto, following provenance recursively when the target is itself generated. A diagnostic may point to an input Koto or Attribute, but a compiler cannot infer every cause from the append target alone; the recorded append chain is distinguished from causes explicitly reported by the Mod.

```text
Demo.kimi: Demo
    -> Example.Model adds Item
        -> Example.Describe adds getVersion
            -> diagnostic in generated source: line and column
```

Implementations provide generated-source viewing and saving, dependency and execution-order inspection, per-Mod timing, and failure and skip reasons, and show cycle paths such as `A -> B -> C -> A`. Saved diagnostic copies are not automatically ordinary source inputs.

### 20.7.7. Two-step example and host boundary

The following API names are illustrative, not existing implementation guarantees. The initial host uses a C# interface and prebuilt assemblies, so loading a Mod cannot depend on completion of its target program. A package may register several implementations with distinct IDs.

```csharp
public interface IMod
{
    string ModId { get; }
    IReadOnlyList<string> Requires { get; }
    IReadOnlyList<string> RequiresAfter { get; }
    void Execute(ModContext context);
}
```

Assume that the `GenerateModels` and `Describe` markers are recognized with suitable target and argument contracts. Original source may refer to both generated declarations before either exists:

```kimi
#GenerateModels
public group Demo
    public func readVersion() -> i32 => Item.getVersion()
```

Register `Example.Model.RequiresAfter = [Example.Describe]` and `Example.Describe.Requires = [Example.Model]`. Both describe the same edge, and each Mod still runs once. Their `Execute` bodies are:

```csharp
// Example.Model: syntax-only queries return fragment snapshots.
foreach (var target in context.FindContainersWithAttribute("GenerateModels"))
    context.ParseAndAppend(target, """
        #Describe
        public struct Item
            public var value: i32 = 0
        """);

// Example.Describe, in its later invocation:
foreach (var target in context.FindStructuresWithAttribute("Describe"))
    context.ParseAndAppend(target, """
        public func getVersion() -> i32 => 1
        """);
```

The `GenerateModels` contract restricts its target to a group; a complete implementation validates the target and argument rules. Neither body needs Binding, so each can alternate syntax reads and appends; Binding is updated between Mods. The resulting tree is:

```text
Demo                         original source
├─ readVersion               original source
└─ Item                      Example.Model
   ├─ value                  Example.Model
   └─ getVersion             Example.Describe
```

These nodes keep separate source contexts, and no original file is rewritten. Final Binding resolves `Item.getVersion()` and validates the complete program.

Concrete query and marker-registration Types, assembly packaging and compatibility checks, project configuration syntax, cache formats and IDE presentation remain implementation design work. Parallel Mod execution, arbitrary Koto rewriting, function-body insertion, per-instantiation execution and automatic retries are outside this initial model. A single invocation and snapshot queries do not prevent a Mod's own infinite loop; cancellation and time-limit mechanisms belong to the host.

## 20.8. LLVM output, native build and execution

### 20.8.1. Output scope and settings

The windows-x64-v1 compiler produces one pre-optimization textual `.ll` and one `.link.json` per project and target, after final semantic acceptance and the supported-operation checks (§21.4). The `emit` command stops after publishing this pair. The `build` command continues through external LLVM verification, optimization, object generation and linking. The `run` command executes an existing binary without compilation. Build resolves the compiler-managed toolchain under §20.8.8; these commands never download or install LLVM or the Windows SDK. No dedicated runtime DLL is required: runtime bodies are emitted in the same module, with separate native backend support (§21.5.7).

| Setting | Initial rule |
| --- | --- |
| Targets | `x86_64-pc-windows-msvc` |
| OutputKind | `Application` (default) or `Library`. Library source distribution follows §18.6; its `.ll` output remains for inspection (§22.2.2) |
| OutputPath | The `.ll` destination; default `bin/<target>/<ProjectName>.ll` |
| PackageId / PackageVersion, Dependencies / PackageSources | Module identity and dependency selection (§18.4) |
| TestSources / TestDependencies | Root test inputs and dependency extension (§18.8) |
| NativeRequirements / NativeLibraries | Definition-side contracts and per-target supplies (§20.8.2); reserved runtime supplies are automatic |
| Optimization | `O0` or `O2` (default); applied during the native build |
| LlvmBin | Optional legacy LLVM-only override, omitted by normal projects (§20.8.8). A relative project value is project-relative; the CLI `--LlvmBin` overrides it and is invocation-relative. `emit` records a project value without executing tools. Neither changes the target/version contract or the default backend location. |
| EntrySource | Not an initial selection setting; startup uses the unique-candidate rules of §22.2 |

**First execution subset.** The first executable subset is ordinary functions, simple local bindings, Unit, string literals, the required ownership and cleanup, and `Kimi.Console.writeLine`. Arrays, Dictionary, inheritance, closures, static Property execution, general generic sharing and multiple-Kotonoha linking need not be included in this first execution test. Their language rules are not weakened, and unsupported required operations fail. Layout computability, physical ABI support and runtime availability are separate checks.

Generation success certifies the matched IR/manifest pair, not LLVM acceptance, a linked executable or successful execution; LLVM verification and object generation, linking, and execution are separately reported stages. A Library `.ll` supports inspection, verification and object-generation experiments; source packages are separate rebuildable inputs, not a stable external machine ABI.

### 20.8.2. NativeLibraries

#### 20.8.2.1. Requirements and supplies

A native logical name belongs to its defining Kotonoha and compares by case-sensitive Ordinal equality. `NativeRequirements` maps each target to logical-name requirements. Each requirement needs a `Kind` (`static` or `import`) and optionally a `ContractId` and a `Sha256`; package JSON uses lowerCamelCase. Contract IDs are nonempty exact-match strings, and hash assertions add acceptance conditions. Targets are checked separately. `import` controls dllimport generation; `static` does not.

Each selected nonreserved `#LibraryImport` must have a matching requirement. An author may declare it in `NativeRequirements` or combine it with a self-targeted `NativeLibraries` record: a self-targeted `Kind`/`ContractId`/`Sha256` expands into both a requirement and a supply, and overlapping explicit fields must agree. `Kind` is required after expansion. Supplies that target another module can neither fill in nor change that module's requirements. Explicit auxiliary requirements may satisfy native-internal references without a direct source import. Packing keeps requirements, never host `Input` paths.

```text
// Standalone application: declare the requirement and supply once.
NativeLibraries=
  x86_64-pc-windows-msvc=
    observer={ Kind="static" Input="native/observer.lib" }
```

```text
// In the defining Library; no native file is needed for semantic checking.
NativeRequirements=
  x86_64-pc-windows-msvc=
    codec={ Kind="static" ContractId="example.codec.v1" }
```

`NativeLibraries` maps each target to an array of Name/Input records, optionally with `Package={PackageId, PackageVersion}`; omitting `Package` targets the declaring Project itself. `Name` is the target module's native name, not the consumer's reference alias. `ContractId` and `Sha256` are optional, and `Kind` is allowed only in self-targeted combined declarations, not in another module's supply. The existing logical-name-to-Kind/Input map is shorthand for self-targeted records whose `Name` equals the key. The superseded `NativeBindings` setting is diagnosed with migration guidance.

```text
// In the consuming Application.
NativeLibraries=
  x86_64-pc-windows-msvc=
    {
      Package={ PackageId="example.codec" PackageVersion="1.0.0" }
      Name="codec"
      ContractId="example.codec.v1"
      Input="native/codec.lib"
    }
```

`Input` names one `.lib`, not a DLL. Paths with separators are relative to the declaring Project, and absolute paths remain absolute. A simple linker search name is resolved to an actual file before use. Empty and NUL values and embedded linker options are rejected, and these strings are never executed as commands. When a `ContractId` is required, the supply must assert the matching contract. Several supplies for one target merge only when their content and contract agree. Registering another module's unused supply creates no requirement.

The actual kind is checked at native-use time from each connected symbol's static definition or import route, including short and long import objects and support records. A whole archive is not classified from a short header and is not rejected merely for mixing member kinds. Unsupported formats, and required connections inconsistent with the declared `Kind`, are diagnosed. Semantic checking and `pack` use definition requirements without reading native files. A contract or hash assertion is not proof of initialization, floating-point, unwind, ownership or other foreign-code behavior.

#### 20.8.2.2. Immutable native inputs

The actual SHA-256 of each native input is recorded, and both requirement and supply hash assertions are checked. A verified immutable content copy is reused, or the fixed snapshot copied into staging; it is never hard-linked to the mutable original. These same bytes, not the original path, are passed to the linker. Moving an unchanged input does not change meaning, while changed bytes change the link inputs even at the same path. Kinds, contracts and call conditions belong to the affected semantic and generation inputs, and actual file content belongs at least to the link inputs. Pinning an import library pins neither the runtime DLL nor the OS.

#### 20.8.2.3. Symbol resolution and directives

Native symbol sharing requires matching function/data kind, physical Type, calling convention, attributes and provider; native logical names of different modules are never merged by spelling alone. Required supplies are deduplicated by content hash, and the definitions, references, imports, relocations and directives of all members are indexed. Starting from the generated objects, the entry and the mandatory profile inputs, undefined references and effective directives are followed to a fixed point of included members.

Unused symbols duplicated only in unextracted members are not errors. For each required symbol, candidate providers are inspected in the full index: non-equivalent candidates in different supplies are ambiguous, even if the linker would take the first. Other definitions in extracted members are checked against already included definitions, and every Kimigayo import must bind its declared supply. Generated objects and reserved supplies are included. Any profile rule that the resolver cannot reproduce is diagnosed rather than guessed.

The same rules apply to public import symbols and `__imp_` symbols. Weak, COMDAT and import support records merge only when their format rules establish the same supply or equivalent definitions, including destinations and relocations; a weak flag or equal size alone is insufficient. Content sharing never removes the caller's ABI and unsafe obligations.

`.drectve` sections are processed only in generated objects and included archive members, using the adopted linker profile:

| Directive | Initial rule |
| --- | --- |
| `/INCLUDE` | Adds the symbol as a required reference |
| `/ALTERNATENAME` | Resolved as a weak/alias reference; candidates, cycles and conflicts are checked |
| `/FAILIFMISMATCH` | Requires identical values for each key |
| `/DEFAULTLIB` | No extra input when disabled by the profile's `/NODEFAULTLIB`; otherwise implicit acquisition is unsupported and requires an explicit supply |
| `/EXPORT` | Diagnosed as outside the initial export scope |
| Other directives and encodings | Accepted only with meanings defined by the profile; otherwise the unsupported feature is diagnosed |

The final linker inputs and options are fixed to the same profile and closure; options and implicit libraries cannot bypass these checks.

#### 20.8.2.4. Reserved supplies and input summaries

`kernel32` is reserved as an automatically generated import library, and `kimi_backend` as the compiler-managed static library at `<toolchain root>/windows_x64/kimi_backend_windows_x64_v1.lib` (§20.8.8); neither needs a `NativeLibraries` entry. A `kernel32` entry in `NativeLibraries` is an error with a diagnostic instructing its removal; neither an SDK `kernel32.lib` nor a user-provided replacement is used. Other keys require configuration, and `.lib` names are never guessed from DLL names. An explicit `kimi_backend` path remains a compatibility override and must select the adopted supply.

The compiler embeds the project-owned `backend/windows-x64/kernel32.def`, which contains `KERNEL32.dll` and the seven runtime APIs of §22.5.6 plus `VirtualAlloc`, `VirtualProtect` and `VirtualFree` for backend tests. The definition is normalized to UTF-8 without BOM, with LF line endings and one terminal newline, and its SHA-256 is verified against `profile.json`. During a native build, the definition is materialized in a fresh staging directory and `llvm-dlltool -m i386:x86-64 -d kernel32.def -l kernel32.lib` is invoked. The generated DLL name, the x64 COFF formats and the exact public/`__imp_` symbol set are verified before the library is published and linked. The runtime still uses the OS-provided `KERNEL32.dll`. Additional kernel32 imports require a reviewed definition and profile update; the current library does not replace the entire SDK export surface.

Static libraries must not depend on CRT startup, automatic C/C++ dynamic initialization, custom TLS initialization or termination, or automatic `atexit` handlers; zero-initialized and constant data are allowed. Code that needs such startup or termination first needs a supported adapter. DLL initialization follows §22.2.3. These are connection contracts between supplier and user: a `.lib` filename and `/NODEFAULTLIB` neither establish nor perform initialization.

The Kimi, backend and kernel32 supplies occur once for the entire graph and cannot be replaced by arbitrary packages. Module loading order is not initialization order (§22.2.3).

Native member summaries and indexes are persisted by content hash, summary format, parser/interpretation-rule version and COFF profile. They keep definitions, references, imports, weak and COMDAT records, relocations and directives, including those of unused members. The closure and conflicts are recomputed for the current root objects, supply set, contracts and linker options; another graph's resolution success is never reused. Changed content, incompatible formats or corruption require reparsing, and unknown information is not an empty set. Indexes and integer references are shared instead of rereading and reallocating every archive for each link.

### 20.8.3. Link manifest and publication

The compiler replaces the OutputPath extension with `.link.json` in the same directory and writes UTF-8 JSON for both output kinds. This generation/link record is distinct from the source manifest, the dependency lock and the semantic cache. The existing single-module profile schema is:

```json
{
  "schemaVersion": 3,
  "target": "x86_64-pc-windows-msvc",
  "codegen": {
    "profile": "windows-x64-v1",
    "llvmVersion": "22.1.8",
    "cpu": "x86-64",
    "features": ["+sse2"],
    "relocationModel": "pic",
    "codeModel": "small",
    "unwindTables": "async",
    "optimization": "O2"
  },
  "backendSupport": {
    "packageId": "kimi-backend-windows-x64",
    "abiVersion": 2,
    "packageVersion": "0.1.1",
    "library": "kimi_backend",
    "artifactSha256": "<64 hex digits for the adopted archive>",
    "providedSymbols": ["__chkstk", "memcmp", "memcpy", "memmove", "memset"]
  },
  "irFile": "ProjectName.ll",
  "irSha256": "<64 hex digits for the generated IR>",
  "outputKind": "Application",
  "entry": "__kimi_start",
  "subsystem": "console",
  "libraries": [
    { "name": "kernel32", "kind": "import", "generator": "llvm-dlltool", "dll": "KERNEL32.dll", "definitionSha256": "<64 hex digits for the normalized definition>" },
    { "name": "kimi_backend", "kind": "static", "resolution": "toolchain" },
    { "name": "observer", "kind": "import", "input": "observer.lib" }
  ],
  "providedRuntimeSymbols": ["_fltused"],
  "expectedUndefinedSymbols": [
    { "symbol": "__chkstk", "provider": "kimi_backend" }
  ]
}
```

Hashes must be actual SHA-256 values. `packageVersion` is supplied by the `Directory.Build.props` Version (currently 0.1.1), not by a separate backend release counter; a version alone does not establish an adopted archive, and the catalog hash and ABI must also match. The `observer` entry and the `__chkstk` expected reference are conditional examples; `_fltused` is always supplied.

- Libraries required by external declarations or the profile are deduplicated and sorted by Ordinal logical name. Path inputs are rewritten relative to the manifest, and linker search names are preserved. `irFile` is manifest-relative.
- A Library uses a null `entry` and `subsystem`. Its dependency record establishes neither an external `.lib`/DLL ABI nor a runnable artifact.
- `codegen` is mandatory and matches §21.5.1; `irSha256` hashes the pre-optimization `.ll`.
- `backendSupport` comes from the adopted catalog. Its `library` references a static `libraries` entry, and the ABI, version and symbols must agree. Manual builds verify the actual `.lib` hash before use.
- The default `kimi_backend` entry uses `"resolution": "toolchain"` and has no input path; build resolves it under §20.8.8, which keeps the manifest portable and emission LLVM-free. An explicit legacy backend override instead has an `input` and no `resolution`. Unknown `resolution` values, and entries combining `resolution` with `input`, are rejected. Schema 3 is required; schema 1 and 2 inputs must be re-emitted.
- `providedRuntimeSymbols` lists generated backend definitions (always `_fltused`), not ordinary `__kimi_` helpers. `expectedUndefinedSymbols` lists backend references anticipated at generation, each with a known provider in `libraries`. Backend symbols are verified against `backendSupport.providedSymbols`, and unknown providers for known dependencies fail generation.
- Both symbol arrays are sorted by Ordinal symbol name, without duplicates or overlap. They are not the final object's undefined-symbol list; even an empty list cannot promise that no later dependency exists. LibraryImport and Windows inputs are recorded in `libraries`.
- Generated `kernel32` entries have no input path. The generator, DLL and definition hash are validated against the embedded profile, and substitutions and extra input paths are rejected. `emit` still requires no native tools and publishes no import library.
- Build records keep the actual library hashes, generator and tool identities, normalized definition hashes and tool settings. Each project and optimization level writes its own `.kernel32.def`/`.kernel32.lib`, generated into a fresh staging directory and published only after validation; if generation fails, the build fails without linking stale libraries. Absolute build paths are never embedded in the generated library, and local report paths are redacted.

Combined-module processing records must connect each module input, each native owner/logical-name requirement and its actual supply, the toolchain, the IR and the link result (§18.5.2). A logical native name alone cannot distinguish the requirements of different modules, so those scoped identities are preserved through final content deduplication. If required fields are added to the single-module encoding above, its schema must be revised and records missing those fields rejected; schema 3 alone is not evidence of complete multi-module input validation.

Both temporary outputs are completed before publication, the manifest is published last, and success is reported only after both are published. Here publication means making the generated files available, not updating a package release table with `publish`. A partial publication is a failure, and old files are not evidence of current success. Consumers check `irSha256`, because an interruption can leave a mixed pair. A success report gives both paths, the purpose (Application input or Library inspection), the entry and the required link inputs.

When the legacy `LlvmBin` setting is configured, the manifest may also contain `"toolchain": { "llvmBin": "<manifest-relative directory>" }`, with a relative setting resolved from the project directory. This is a local build-tool location, neither part of the code-generation profile nor evidence of a tool's version. A build command or separately invoked builder may override the location but must still check the actual tool versions under §20.8.5; a directory name or configured path cannot certify version compatibility. Default projects record no local toolchain path. External user library files remain configured through `NativeLibraries`; `kimi_backend` resolves from the toolchain, and `kernel32` is generated from the embedded definition.

### 20.8.4. Manual toolchain example

With LLVM 22.1.8 and all manifest inputs resolved, including a verified backend archive:

```powershell
$tools = 'C:/path/to/Kimigayo/toolchain'
& "$tools/opt.exe" -S -passes="default<O2>" -mtriple=x86_64-pc-windows-msvc ProjectName.ll -o ProjectName.opt.ll
& "$tools/llc.exe" -O2 -filetype=obj -mtriple=x86_64-pc-windows-msvc -mcpu=x86-64 -mattr=+sse2 -relocation-model=pic -code-model=small ProjectName.opt.ll -o ProjectName.obj
& "$tools/llvm-dlltool.exe" -m i386:x86-64 -d kernel32.def -l kernel32.lib
& "$tools/lld-link.exe" ProjectName.obj kernel32.lib "$tools/windows_x64/kimi_backend_windows_x64_v1.lib" /entry:__kimi_start /subsystem:console /nodefaultlib /debug /out:Application.exe
.\Application.exe
```

Use every manifest input, not just the libraries of the example. O0 omits `opt` and passes the original `.ll` to `llc -O0` with the same profile. IR is verified before and after optimization, and actual object dependencies are inspected. `/debug` creates no Kimigayo line or variable information; CodeView/PDB emission remains separate from Abort source context.

Before a profile is adopted, it is validated with the pinned LLVM version; another version's preliminary result is not acceptance. Semantic tests, representative IR structure and golden files, object ABI/unwind/dependency checks and execution results remain distinct (§A.14). Performance decisions use measured execution time, code size and build time, never weakened checks.

### 20.8.5. LLVM version checks and exploratory builds

The expected LLVM release has one machine-readable source, `backend/windows-x64/profile.json`, currently 22.1.8. The compiler embeds this catalog, exposes its version through `WindowsProfile.LlvmVersion`, and writes it as `codegen.llvmVersion`. Native build and verification scripts read the same catalog; the expected version is not duplicated in executable code. This value describes the intended profile, not a detected installation. Parsing, semantic analysis and IR/manifest generation neither execute LLVM nor require it to be installed.

Immediately before native toolchain work, every selected version-reporting tool's actual `--version` output is checked. The full release version, including the patch level, is compared: newer versions are not implicitly supported, and development or prerelease suffixes do not match the release. A recognized tool version banner is parsed, not an arbitrary occurrence of the expected number. Missing tools, failed probes, and absent or ambiguous version information are errors, even in exploratory mode. Diagnostics identify the tool path, the expected version, and the actual version or the probe failure.

Two tools have no version banner. The versionless `llvm-lib` exception is explicit: its executable path and hash are kept and it is recorded as unversioned, never with an invented matching version. For `llvm-dlltool`, the executable SHA-256 is compared against `profile.json`'s `kernel32.dlltoolSha256` before invocation, recording `expectedSha256`, `sha256` and `hashMatched` without fabricating an `actualVersion`; an explicit exploratory override permits a different hash with a warning and `unverifiedToolchain=true`, but does not bypass definition or generated-library validation. `reportedVersionsMatched` describes only version-reporting tools.

Normal native builds fail on a mismatch before IR verification, optimization, object generation or linking. `kimi build --AllowUnpinnedToolchain true`, and the `-AllowUnpinnedToolchain` option of the separately invoked manual and backend builders, permit exploratory work only: each mismatched tool produces a visible warning, and the ordinary verification and build steps may continue. This option does not override manifest/profile mismatches, IR or archive hashes, ABI contracts, dependency checks or other errors. Adoption and generated-module profile verification still require matching tools and cannot use it.

After successful version probing, native build records keep the expected version, the actual per-tool versions and executable identities, `reportedVersionsMatched` and `unverifiedToolchain`; these facts are kept even if subsequent native work fails, with status `incomplete`. A successfully linked or executed exploratory program remains unverified for the pinned profile: its manifest is not relabeled with the detected version, and it does not count as profile adoption evidence. Formal support for another LLVM release requires renewed profile validation and a deliberate catalog update.

### 20.8.6. Compiler commands and artifact lifecycle

| Command | Required behavior |
| --- | --- |
| `kimi restore <project>` | Resolves the current product/test partitions and atomically updates their lock (§18.5.1); no source execution, Mods or network access. |
| `kimi check <project>` | Validates the required lock and the current source and semantic inputs, without native generation or execution. |
| `kimi emit <input>` | Resolves the input (§20.8.6.1), performs the required source, ownership and generation checks, and publishes the matched pre-optimization `.ll`/`.link.json` pair. Never executes LLVM, validates an installed LLVM version, links or runs. A successful run reports both paths; LLVM acceptance is a separate stage. |
| `kimi build <input>` | Resolves the input, generates fresh LLVM inputs, validates the actual tool versions and native inputs, runs `opt` verification (and `default<O2>` only at O2), `llc` and `lld-link`, and publishes the executable and a successful build record. Never executes the Application. |
| `kimi test <project>` | Requires valid product/test resolution and records the current test inputs. [§20.9](#209-test-command-and-discovery) defines discovery and options, and [§22.6](22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting) execution. `--list` requires semantic verification, not code generation; §18.8 and §21.3.7 define the input and generation boundaries. |
| `kimi pack <project>` | Verifies the fixed source-package graph and saves its closure (§18.6.1), without reserving a release. |
| `kimi publish <package> --store <directory>` | Validates the fixed Package closure and atomically updates only the named local publication store (§18.6.4); no Project lock update or repacking. |
| `kimi store verify` | Rechecks all user-cache content, formats and references and invalidates corrupt results (§18.6.4). |
| `kimi run <input>` | Resolves the input and its existing executable, requires a successful latest build record and a matching executable hash, and executes it without source analysis, IR generation, LLVM version checks or rebuilding. Source changes never trigger compilation; users build explicitly when needed. |
| `kimi run <path.exe>` | Executes the explicitly selected existing binary directly, without a project or build record. |

Source-processing commands validate their required lock partitions and use immutable current input records (§18.5); only `restore` changes locks. `build`, `emit` and `run` share [input resolution and implicit projects](#20861-input-resolution-and-implicit-projects). Failing to load any selected project is a failure, and empty discovery is not success. `run` through source, project, directory or solution discovery requires exactly one loaded Application; the first of several projects is never chosen silently. A source input to `run` selects an existing implicit-project executable and never compiles the source. The initial native executable profile is a Windows x64 Application; Library inspection output and source packing remain distinct. Unsupported target, operation or output requests are diagnosed rather than emitting placeholder binaries (§21.4). The command name is `emit`; the former `emit-llvm` spelling is not a compatibility alias.

**Artifacts and modes.** OutputPath continues to name the pre-optimization `.ll`. For `Name.ll`, optimization O0 or O2 selects `Name.O0.obj`/`Name.O2.obj` and `Name.O0.exe`/`Name.O2.exe`, and O2 also keeps `Name.O2.ll`. `Name.link.build.json` is the latest build-record view, backed by immutable content-addressed processing records (§18.5.2). For single-environment `build`, `emit` and `pack`, `--Target` selects a configured target; without it, the sole target is used, and multiple targets are diagnosed. Release is the default semantic mode and `--Debug` selects debug; the mode is separate from O0/O2. The Boolean-value CLI spelling is, for example, `--Debug true` or `--AllowUnpinnedToolchain true`. `pack`'s `--verify-all` follows §18.6.1.

**Build lifecycle.** At the beginning of a native build attempt, the latest-success pointer is invalidated before semantic analysis, while immutable historical records and active pins are preserved (§18.5.2). The link goes into a fresh temporary executable, which is published only after success. A failed build may leave a prior executable for inspection, but a project `run` must not treat it as the result of that attempt. `emit` does not rewrite the native success record. The version, actual input and tool identities, optimization and executable hash are recorded; remapped descriptive paths do not identify content. A project launch derives the executable path from the current output settings and verifies the recorded hash, so changing those settings requires the corresponding built artifact. An explicitly selected `.exe` path remains independently runnable.

**Processes.** External processes are launched directly with separately supplied arguments, never through constructed shell commands. Native-tool stdout and stderr are drained concurrently, failures reported, cancellation propagated to child process trees, and individual native-tool invocations bounded (currently five minutes). `run` forwards stdin and the child's stdout and stderr, preserves output bytes, and returns the child's exit code. Project runs use the project directory as the working directory, and direct binary runs the caller's current directory. Build and emit failures and launch errors return 1, successful build and emit return 0, and command cancellation returns 130. No fixed Application runtime timeout is imposed.

#### 20.8.6.1. Input resolution and implicit projects

After loading each selected project, and before `build`, `emit` or `run`, the compiler prints one concise settings summary: the project name, the selected project or source filename, an implicit-project marker when applicable, the ProjectFile Targets, OutputKind and Optimization, and, if `--Target` was supplied, that selected target separately. Failed loads have no settings summary, and direct `.exe` execution has no ProjectFile to summarize.

`build`, `emit` and `run` resolve each input relative to the invocation directory; with no input, the current directory is used. For an extensionless input `A`, the order is:

1. If the exact path `A` exists, select it. A directory uses the existing solution/project discovery, and a file must have a supported input extension.
2. Otherwise, select the file `A.kimiproj` if it exists.
3. Otherwise, select the file `A.kimi` if it exists.
4. Otherwise, fail and report the attempted paths.

An explicitly written extension selects only that exact path: `A.kimi` never selects `A.kimiproj` or `A.kimi.kimiproj`. An existing unsupported file, an unreadable or invalid selected file, an empty selected directory, or a failed compilation is an error, not permission to try the next candidate. Directory discovery prefers a contained `.kimisln` and otherwise collects the contained `.kimiproj` files; it never collects loose `.kimi` files or synthesizes a project from a directory. `run <path.exe>` keeps its separate direct-execution form.

A selected `.kimi` file creates an in-memory **implicit project** with these defaults:

| Setting | Implicit project value |
| --- | --- |
| Name | The source filename without `.kimi` |
| Base directory | The directory containing the selected source |
| Sources | Exactly the selected file; no sibling-source discovery |
| Targets | One target matching the host OS and architecture |
| OutputKind | `Application` |
| Optimization | `O2` |
| Other settings | Ordinary ProjectFile defaults; no adjacent project or solution settings are imported |

Explicit CLI settings apply normally. In an implicit project, `--Target` supplies the single target instead of the host default; in an explicit project, it still selects from the configured Targets. An unsupported host or target is diagnosed rather than silently targeting another environment. The initial executable implementation supports Windows x64; this default introduces no other native profile.

No `.kimiproj` file is created. The ordinary artifact paths under the source directory are used: for `A.kimi` and target `T`, `bin/T/A.ll`, `bin/T/A.link.json`, `bin/T/A.O2.exe` and `bin/T/A.link.build.json`. `build` and `emit` read only the selected source. `run` reconstructs the same settings and output paths without reading or analyzing the source contents, and then applies the ordinary latest-build and executable-hash checks; missing or invalid build records require an explicit build. The working directory for this run is the source directory.

### 20.8.7. Compiler and backend release version

The `Directory.Build.props` Version is the single release-version source for Kimigayo and its backend package. The compiler embeds that MSBuild value as assembly metadata and uses it for the default version display, the compiler build identity prefix and `backendSupport.packageVersion`. Repository tools combine the same props value with `profile.json`, which keeps the LLVM release, ABI, helper symbols and adopted archive hash but no package-version literal of its own. The language version, ABI version and LLVM version are independent identifiers and do not change merely because the package release changes. Candidate reports carry the shared release with `adopted=false`; release equality never substitutes for native archive and hash validation. Changes to adopted archive contents require renewed validation and a shared release update before distribution.

### 20.8.8. Toolchain storage and native library lifecycle

Kimigayo manages the LLVM executables and the adopted native backend as one relocatable toolchain. In a source checkout the canonical location is `Kimigayo/toolchain`; a standalone compiler distribution places `toolchain` beside `Kimi.exe` (or `Kimi.dll`). Normal `.kimiproj` files specify language and build choices such as Targets, OutputKind and Optimization, without any `LlvmBin` or `kimi_backend` path.

```text
Kimigayo/
  toolchain/
    opt.exe
    llc.exe
    lld-link.exe
    llvm-nm.exe
    llvm-readobj.exe
    llvm-dlltool.exe
    clang.exe
    llvm-lib.exe
    llvm-objdump.exe
    <supporting LLVM DLLs, when required>
    windows_x64/
      kimi_backend_windows_x64_v1.lib
```

**Location resolution.** A compiler build selects the root in this order: an explicit `--ToolchainRoot`, then `KIMI_TOOLCHAIN_ROOT`, then an executable-adjacent `toolchain`. Relative explicit or environment roots are relative to the invoking working directory. For source builds only, if the adjacent directory is absent, the compiler walks the ancestors of the executing compiler or test-host directory to the checkout identified by `Kimigayo.slnx`, `Kimi/Kimi.csproj` and `backend/windows-x64/profile.json`, and uses its `toolchain`. A toolchain is never discovered by walking the user's project or current working directory, searching PATH, or guessing an SDK installation. An explicitly selected missing or incomplete root fails rather than falling back. A standalone installation without tools reports the expected executable-adjacent path.

Repository PowerShell builders share the same root layout: `-ToolchainRoot`, then `KIMI_TOOLCHAIN_ROOT`, then the checkout's `toolchain` located relative to the script. Their `-LlvmBin` option, and the compiler's CLI and project `LlvmBin`, remain optional LLVM-only compatibility overrides that do not relocate the backend. The compiler applies the CLI `--LlvmBin` before the project `LlvmBin` before the selected root; the manual builder applies `-LlvmBin` before the manifest's legacy `toolchain.llvmBin` before the selected root. Normal use needs none of these overrides. Root configuration is local installation state, not a `.kimiproj` field or a language or ABI version.

**Initial setup.** `backend/windows-x64/setup.ps1 -LlvmBin <existing LLVM directory>` validates the complete selected LLVM tool set against the embedded profile, copies the nine executables above and the adjacent support DLLs to the selected toolchain root, and then invokes the backend build and verification using those copies. Without `-LlvmBin`, it validates and uses the tools already in the root. The helper sources and the freestanding test harness require no Windows SDK or additional Clang headers. Setup uses an existing local installation; it neither downloads tools nor changes PATH. It succeeds only if this run produces the adopted backend and the installed archive matches the catalog. Missing tools, failed version probes and mismatches are errors, and setup has no exploratory override. A distribution of LLVM must keep the applicable license notices. Generated and copied toolchain contents are ignored by Git, while sources, scripts and the authoritative catalog remain version-controlled.

```powershell
# Once per toolchain installation, {the} Kimigayo checkout:
./backend/windows-x64/setup.ps1 -LlvmBin C:/App/llvm

# Rebuild the native backend after changing its sources:
./backend/windows-x64/build.ps1

# Ordinary project builds need no tool or native-library path settings:
dotnet run --project Kimi -c Release -- build examples/Hello/Hello.kimiproj
dotnet run --project Kimi -c Release -- run examples/Hello/Hello.kimiproj
```

**Backend generation and installation.** `build.ps1` explicitly assembles the project-owned `src/memcmp.S`, `memcpy.S`, `memmove.S`, `memset.S` and `chkstk.S` with `clang --target=x86_64-pc-windows-msvc -c`, producing one native COFF `.obj` per helper. `llvm-readobj` checks the x64 format, unwind records and the absence of CRT, TLS and default-library directives; `llvm-nm` checks that no symbols are undefined; and `llvm-objdump` checks that there are no calls. `llvm-lib` archives the five objects with filename-only member names. The archive must export exactly `__chkstk`, `memcmp`, `memcpy`, `memmove` and `memset`, with no `_fltused`. These assembly bodies stay outside LLVM IR optimization, which prevents memory loops from becoming recursive libcalls (§21.5.7).

The script creates `kernel32.lib` from the reviewed `kernel32.def` with `llvm-dlltool` for its test harness. It compiles `tests/native.c` to LLVM IR and `tests/probe.S` to COFF, verifies the IR, builds O0 and O2 variants with `opt`/`llc`, links through `lld-link` with a custom entry and `/NODEFAULTLIB`, and requires zero native exit codes within the timeout. The tests cover memory boundaries, overlap and return values, guard pages, and the special `__chkstk` ABI. Source, tool and archive hashes and diagnostics are kept under `backend/windows-x64/bin`; `verification.json` begins as `incomplete` and becomes `tested-candidate` only after this run's checks pass, including unchanged source identities.

After successful verification with pinned tools, an archive matching `profile.json`'s adopted SHA-256 is copied through a fresh temporary file, rehashed and published at `toolchain/windows_x64/kimi_backend_windows_x64_v1.lib`. The verification report remains `adopted=false`: a successful reproduction creates no new catalog adoption. An exploratory or different-hash candidate stays in `backend/windows-x64/bin` with a warning and does not replace the installed library. A changed archive requires native and generated-module review, a catalog update and the shared release policy of §20.8.7 before installation. A failed candidate build does not make old output evidence of current verification. Setup additionally rejects a non-installable candidate even when an older installed file exists.

**Reference and Application build.** `emit` writes the schema-3 logical `kimi_backend` entry and the expected catalog identity without locating the default toolchain or reading its archive, so semantic checking and emission work before setup. `kimi build` resolves LLVM from the selected root and the backend from its `windows_x64` subdirectory; validates the tool versions, the catalog ABI and release, and the actual archive hash; and creates a separately validated kernel32 import library for each project and optimization level. The generated `kernel32.def`/`.lib` stay beside the project build outputs; they are not a shared mutable toolchain cache. Explicit legacy `NativeLibraries` backend paths undergo the same hash and ABI checks.

```text
backend src/*.S -> clang -> COFF objects -> llvm-lib -> candidate .lib
  -> O0/O2 native verification -> adopted-hash check -> toolchain/windows_x64/*.lib

Kimigayo source -> semantic checks -> .ll + .link.json
  -> opt verification / optional O2 -> llc -> Application.obj
  -> lld-link(Application.obj, toolchain/windows_x64/backend.lib, generated kernel32.lib)
  -> Application.exe + successful build record
```

The linker receives the backend as a profile-wide static input and extracts only the needed members. Build records keep the actual resolved tool and library paths, subject to the report's path-redaction policy, their hashes, the selected settings and the executable identity. The executable contains the selected helpers and does not need the `.lib` at execution time. `run` uses the existing executable and performs no LLVM or backend regeneration. Neither `dotnet build`, an ordinary `kimi build`, nor the current .NET CI workflows invoke the backend `build.ps1` automatically; setup or explicit backend regeneration supplies the library. This toolchain packages build inputs and adds no dedicated runtime DLL, extra runtime operation or stable language-function ABI.

## 20.9. Test command and discovery

`kimi test <input>` uses the [product/test inputs](18-modules-and-dependencies.md#188-product-and-test-inputs), [Test definitions](06-declarations-and-containers.md#651-test-definitions) and [test generation](21-layout-runtime-and-code-generation.md#2137-product-and-test-generation). Input resolution follows §20.8.6.1; solutions select every listed project. The [test profile](testing-profile.md) owns cross-project selection, configuration, limits and output. Product selection, generation and meaning are fixed before test inputs and generated declarations are added. Tests are discovered at compile time without running user initialization or test code. Excluded definitions and dependencies' own tests are not collected; to test a dependency, select that project independently.

Test discovery and identity keep the complete declaring Container environment. Test eligibility for non-generic declarations includes inherited parameters, which a nested group cannot hide. Placement is applied after directive selection and source generation (§6.1.1).

Every selected test declaration and body is verified with the ordinary Type, ownership and control-flow checks before listing or filtering execution, so a filter never hides a compile error. `--list` uses verified declarations and requires no generation plan, native code generation, link or child startup. Execution generates or reuses one immutable executable and static diagnostic table containing all cases; filtering or case selection causes no per-case recompile or relink.

Product and test artifacts, manifests and caches are distinct. Product analysis may be shared when source, generated output, dependencies, target, settings and compiler agree; fixing product meaning does not require two full compilations. Test-specific product conditions are reported, and analysis from different conditions is never reused. Both product and test resolution partitions are validated under §18.4–§18.5; no lock is required for an empty required partition, but an existing stale lock is not treated as absent. `test` does not update locks. Generation plans and budgets follow §21.3.7, independently of filters.

Selection options, concurrency, defaults and conflicts are defined once in the [test profile](testing-profile.md#options-and-settings). Execution order is unspecified, even in serial mode; tests may not depend on it. Listing and final display have a stable order.

Zero discovered or selected cases across the command are an error unless `--allow-empty` is present; that option cannot rescue an invalid CaseId. Successful verified listing exits zero. Execution exits zero only when all selected cases succeed, or when an explicitly allowed empty set succeeds. This differs from child completion (§22.6.4). The [test profile](testing-profile.md) defines machine-readable results, time/storage controls and exit codes. Runtime, environment and reporting requirements are in §22.6.
