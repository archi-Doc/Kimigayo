# 20. Compilation configuration

[Specification index](../SPEC.md)

A Compilation processes one Project for fixed source, dependency, target, and configuration inputs. The source-language rules determine meaning; this chapter defines compilation invariants with implementation requirements and reference algorithms in separate appendices.

## 20.1. Build units

The build model separates workspace orchestration, project configuration, source modules, and target compilation:

| Element | Responsibility |
| ------- | -------------- |
| Solution | Holds multiple Projects and supplies options shared by their builds. |
| Project | Defines one application or library build unit. It is configured by a `.kimiproj` file. |
| Kotonoha | Defines a named module unit for an application or library, built from one or more SourceDocuments. |
| SourceDocument | An immutable source snapshot, including its path and text. Replacing its text creates a new snapshot. |
| Compilation | Compiles one Project under one fixed set of source, dependency, target, and build inputs. |

A Solution discovers and loads Projects. A Project stores target triples, aliases, and external Kotonoha descriptors, and creates one Compilation for each target.

## 20.2. Build inputs

Compilation inputs comprise the full target triple (including ABI/environment), backend/layout, build mode and code-affecting options, Project settings, language/compiler version, source snapshots, resolved dependency versions/interfaces, and [Mod registrations, implementations, and additional inputs](#2075-inputs-and-regeneration). Reuse requires all inputs relevant to the reused judgment to agree; OS/architecture alone is insufficient. Invalidate the affected stages when inputs change. A generation-only multiplier change can reuse still-valid semantic plans, while target-dependent proofs and semantic settings remain validation inputs (§21.3.4). Artifact cache formats are separately specified.

## 20.3. Target preparation

Each Compilation owns the primary Kotonoha and provides target information and compile-time variables.

A target must provide the Kimigayo data-layout and ABI facts required by the language. Backend-specific representations are implementation details.

## 20.4. Compile-time values

Prepared Compilations provide these values, fixed throughout analysis:

| Name | Type | Value |
| --- | --- | --- |
| `os` | `string` | Canonical lower-case OS family: `windows` for Win32, `macos` for MacOSX, `linux` for Linux; other recognized families use their lower-case target-family name, and an unrecognized OS uses `unknown`. Version suffixes are excluded. |
| `arch` | `string` | Canonical lower-case architecture family, such as `x86`, `x86_64`, `aarch64`, or `riscv64`; target aliases for the same family give the same value. |
| `windows`, `linux`, `macos` | `bool` | Exactly the respective comparisons `os == "windows"`, `os == "linux"`, and `os == "macos"`. At most one is true; all are false for other OS families. |
| `debug`, `release` | `bool` | The selected build mode and its negation: `release == not debug`. |
| `pointerWidth` | `i64` | Default raw-pointer width in bits from the prepared target layout; supported values in this revision are 16, 32, and 64. |

Project settings provide explicit bool, i64, or string values. Validate configured Names under §2.5, including pinned Unicode categories, NFC, and reserved words. Reject exact duplicate setting names and collisions with built-in values; collisions are errors, not overrides. Under [Condition lookup](19-compile-time-directives.md#192-environment-condition-forms), `Feature` and `FEATURE` are distinct settings, and `WINDOWS` is distinct from the built-in `windows`. Preserve spelling and copy settings into the prepared environment before parsing. `.kimiproj` uses a `CompileTimeSettings` map whose entries set exactly one of Bool, Integer, or String.

## 20.5. Language-version selection

An optional `.kimiproj` `LangVersion` requests an exact supported language version. If omitted, use the solution's version when supplied, otherwise the compiler's current version. Unsupported requests are errors, never silent fallback. Record the effective language version and compiler build identity in build metadata. This setting does not promise compatibility with older compilers bearing the same pre-alpha version label.

## 20.6. Compilation invariants

Compilation follows semantic dependencies, not necessarily whole-program passes; [reference models](appendices/B-reference-models.md#appendix-b-non-normative-reference-models) show optional schedules. Resolve Conditions in the prepared environment. Mod analysis may use incomplete declarations under §20.7; complete all generation before committing final Binding or dependent layout and operation plans. Instantiation and implementation selection never reselect directives. Analyses may share facts, but unresolved obligations cannot count as successful finalization.

## 20.7. Mods: source generation

A **Mod** is Kimigayo's Source Generator: one compiler-invoked generation step. It searches and reads Koto, generates Kimigayo source, and asks the compiler to parse and append it to a Declaration Container. Run each registered Mod once per Compilation, subject to failure or cancellation. A Mod may process many targets, append many fragments, or succeed without output.

Mods process generic declarations, not each generic instantiation; they may emit generic source. Each target-specific Compilation runs its own Mods. Several steps from one package use separate ModIds. There is no automatic retry, marker-driven rerun, or iteration until generation converges.

### 20.7.1. Registration and execution order

Freeze registrations and dependency lists before execution. Each registration has:

| Field | Meaning |
| --- | --- |
| `ModId` | Stable, case-sensitive ID unique within the Compilation. |
| `Requires` | IDs of required Mods that must finish and integrate their output before this Mod. |
| `RequiresAfter` | IDs of required Mods that must run after this Mod finishes and integrates its output. |

Both lists require their targets to be registered; neither automatically registers them. There is no Priority. Combine both lists into one graph: `A.Requires = [B]` gives `B -> A`, while `A.RequiresAfter = [B]` gives `A -> B`. Duplicate edges count once. Reject duplicate ModIds, missing targets, self-dependencies, and cycles before running any Mod.

Run Mods sequentially. After each successful output integration and Binding update, select the smallest ModId among unexecuted Mods whose graph predecessors have all succeeded. Compare IDs in culture-independent Ordinal order. Dependency-list order and assembly loading order do not affect execution.

```text
A.Model.RequiresAfter = [C.Serializer]
B.Extra.RequiresAfter = [C.Serializer]
D.Report has no dependency declarations

A.Model ----+
            +--> C.Serializer
B.Extra ----+

Execution: A.Model -> B.Extra -> C.Serializer -> D.Report
```

Reconsider readiness after every step: C becomes ready after B and sorts before D. RequiresAfter guarantees an ordering edge, not the last position in the whole Compilation.

At entry, a Mod sees original source and every successful earlier Mod's output, not only directly named dependencies. Required ordering must be declared rather than relying on incidental ID order. A consumer of all members, such as a serializer, must follow every member-producing Mod. The consumer may declare Requires, producers may declare RequiresAfter, or a transitive path may guarantee the order. Final Binding does not detect omitted serialization work if a producer runs too late.

Graph cycles differ from references between generated Types. Mutual Type references are allowed when Mods can emit them without mutually requiring completed Binding; validate them normally after generation, including finite value layout.

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

Only new source needs parsing; reparsing all original documents after each Mod is unnecessary. Generated declarations obey normal merge, header, duplicate-member, access, Type, and ownership rules. The diagram sets dependency boundaries, not an internal ordering for every final check.

**Provisional results.** Whole-program Binding success is not a prerequisite for running Mods. Original source may refer to Types or members that a later Mod supplies. Queries distinguish:

| Result | Meaning |
| --- | --- |
| Resolved | Information is available in the current Binding; it is not a final commitment. |
| Unresolved | Required declarations or information are not yet available. |
| Invalid | Available information establishes a rule violation. |

Unresolved means neither permanent absence nor a promise of later generation. Do not treat a problem that later additions may resolve as a definite error. A Mod may use syntax alone; if resolved semantics are essential but unavailable, it reports an error rather than requesting a retry.

**Binding access period.** A Mod may read Binding from entry until its first parse-and-append call. That call ends access for all targets, including existing Koto. A Mod that never appends may read Binding until return. Syntax searches and further appends remain available after the boundary; no Binding update occurs inside the Mod.

Names, Type names, flags, and other values extracted beforehand may be used to generate source. Retained Symbols or Binding objects must not provide semantic access after the boundary or from another Mod; the API must reject such access. A Mod needing semantics for several targets gathers all required values before appending:

```csharp
// Illustrative API: Analyze returns values and a target Koto, not Symbols.
var plans = context.FindTargets()
    .Select(target => Analyze(context.Binding, target))
    .ToArray(); // Complete all semantic reads before the first append.

foreach (var plan in plans)
    context.ParseAndAppend(plan.Target, Generate(plan));
```

The compiler need not preserve an immutable Binding snapshot after append. After a Mod succeeds, it may discard and rebuild Binding. Any incremental alternative must match full reanalysis, invalidating affected successful lookups as well as unresolved ones; added overloads can change earlier results. Provisional results never constrain final Binding and are not verified generic-definition obligations. Before emission, all required unresolved information must be resolved and all normal checks must succeed.

### 20.7.3. Koto queries and appending source

Mods receive read-only access to existing Koto. The only mutation operation parses source and appends allowed declarations or members to a Declaration Container in the Compilation's target Kotonoha. The target may be original or generated, including one just added by the current Mod. Do not permit direct collection writes, deletion, replacement, renaming, body rewriting, reparenting, insertion into referenced libraries, or insertion of statements/expressions inside function bodies.

Pass the target Koto and source text directly, conceptually `ParseAndAppend(targetKoto, sourceCode)`. No TargetContainerId, output record, output ID, Parse-call number, or required OriginLocation argument is introduced. Each fragment follows the target's grammar; its top level denotes direct children without copying the target file's indentation. Internal indentation follows ordinary source rules.

Successful appends are immediately visible as syntax. A query fixes its result membership and order when called, not when enumeration starts. Additions cannot extend that result; a fresh query can find them:

```text
Query S1 -> [A, B]
Process A; append C
Continue S1 -> B only
Query S2 -> [A, B, C]  (when this is their logical order)
```

This snapshots the result list, not the whole tree. A new member query on an existing Container may observe newly appended members. Queries use [logical declaration order](#2074-generated-sources-and-declaration-order). For merged declarations, use the first fragment as the ordering key; each API must state whether it returns fragments or merged declarations. Attribute queries follow [Mod marker rules](06-declarations-and-containers.md#65-attributes).

### 20.7.4. Generated sources and declaration order

Each generated fragment has its own immutable SourceDocument and CodeContext under [source identity](appendices/A-compiler-requirements.md#a1-source-identity-and-incremental-analysis), even when appended below the root. Preserve these sources for regeneration and diagnostics. Resolve generated names in the target's enclosing declaration scopes and the generated document's own alias environment; do not inherit source-local aliases from the target's original file. Use qualified names or aliases permitted by the fragment grammar.

The compiler retains links between the producing Mod, source, target Koto, and generated Koto. No per-output identity or correspondence across builds is required.

Define **logical declaration order** independently of layout and Mod execution order:

1. Ordinary sources precede generated declarations. Sort them by stable logical source name, then source declaration order.
2. Sort generated declarations by ModId in Ordinal order. Within a Mod, preserve Koto addition order and the written order inside each fragment.

Ordinary logical names are normalized project-relative paths; external files need assigned project-relative names. Normalize separators to `/`, remove redundant segments, and reject collisions. Names are independent of absolute checkout and temporary paths and are compared without host case folding or locale rules. Addition order is retained directly and needs no Parse-call numbering.

Identical inputs must produce identical additions and order. Changing dependency declarations alone does not change logical order when ModIds, generated content, and each Mod's addition order remain the same. Renaming sources or Mods, or changing addition order, may change initializer side-effect order. Kimigayo-layout storage uses this order under [split structures](06-declarations-and-containers.md#621-split-structures-and-storage-order). C-layout Fields occupy one fragment in written order (§21.1.2); moving that whole fragment or renaming method-only fragments does not reorder its Fields. Physical layout remains governed by §21.1. Conflicting declarations follow normal integration rules, never last-writer-wins replacement.

### 20.7.5. Inputs and regeneration

In addition to [general build inputs](#202-build-inputs), record ModIds, both dependency lists, settings, Mod API compatibility, implementation assemblies and dependencies by content, and additional files by logical name and content. Provide compiler-managed access to declared additional inputs, enumerated by normalized logical name in Ordinal order. A Mod runs on the host but obtains target facts from its Compilation.

Generation must be deterministic for identical inputs. Do not implicitly depend on current time, randomness, undeclared environment variables, file enumeration order, or other external state. Supply external data as fixed declared input. This is a Mod contract, not a promise of OS-level isolation for C# code.

Rebuild from original inputs rather than treating previous generated Koto as original source. Replace each Mod's output collection, removing outputs no longer produced, including after a Mod or input is removed. Do not carry old Koto or Binding objects into a new Compilation. Output caches are optional; reuse must validate all relevant inputs, including earlier Mod outputs, and restore equivalent source, target associations, addition order, diagnostics, and provenance.

### 20.7.6. Failures and diagnostics

A Mod exception, reported error, invalid append operation, generated syntax error, or definite integration error fails the Mod and Compilation. A still-unresolved provisional dependency is not by itself such an error. Skip all descendants of a failed Mod in the combined graph, including RequiresAfter successors. The compiler may stop all remaining Mods; continuing independent diagnostics must not expose failed partial output as valid input.

Partial Koto may be retained for inspection, but never published or cached as successful output. Do not substitute a previous successful output to make a failed build succeed.

Retain ModId, generated source and position, and target Koto for diagnostics. Follow provenance recursively when the target is generated. A diagnostic may point to an input Koto or Attribute, but a compiler cannot infer every cause from the append target alone. Distinguish the recorded append chain from causes explicitly reported by the Mod.

```text
Demo.kimi: Demo
    -> Example.Model adds Item
        -> Example.Describe adds getVersion
            -> diagnostic in generated source: line and column
```

Provide generated-source viewing/saving, dependency and execution-order inspection, per-Mod timing, and failure/skip reasons. Show cycle paths such as `A -> B -> C -> A`. Saved diagnostic copies are not automatically ordinary source inputs.

### 20.7.7. Two-step example and host boundary

The following API names are illustrative, not existing implementation guarantees. The initial host uses a C# interface and prebuilt assemblies; loading a Mod cannot depend on completion of its target program. A package may register several implementations with distinct IDs.

```csharp
public interface IMod
{
    string ModId { get; }
    IReadOnlyList<string> Requires { get; }
    IReadOnlyList<string> RequiresAfter { get; }
    void Execute(ModContext context);
}
```

Assume the example's GenerateModels and Describe markers are recognized with suitable target/argument contracts. Original source may refer to both generated declarations before either exists:

```kimi
#GenerateModels
public group Demo
    public func readVersion() -> i32 => Item.getVersion()
```

Register `Example.Model.RequiresAfter = [Example.Describe]` and `Example.Describe.Requires = [Example.Model]`. Both describe the same edge; each Mod still runs once. Their Execute bodies are:

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

The example's GenerateModels contract restricts its target to a group; a complete implementation validates target and argument rules. Neither body needs Binding, so it can alternate syntax reads and appends. Binding is updated between Mods. The resulting tree is:

```text
Demo                         original source
├─ readVersion               original source
└─ Item                      Example.Model
   ├─ value                  Example.Model
   └─ getVersion             Example.Describe
```

These nodes retain separate source contexts; no original file is rewritten. Final Binding resolves `Item.getVersion()` and validates the complete program.

Concrete query and marker-registration types, assembly packaging/compatibility checks, project configuration syntax, cache formats, and IDE presentation remain implementation design work. Parallel Mod execution, arbitrary Koto rewriting, function-body insertion, per-instantiation execution, and automatic retries are outside this initial model. A single invocation and snapshot queries do not prevent a Mod's own infinite loop; cancellation and time-limit mechanisms belong to the host.

## 20.8. LLVM output, native build and execution

### 20.8.1. Output scope and settings

The windows-x64-v1 compiler produces one pre-optimization textual .ll and one .link.json per project/target, after final semantic acceptance and supported-operation checks (§21.4). The `emit-llvm` command stops after publishing this pair. The `build` command continues through external LLVM verification, optimization, object generation and linking. The `run` command executes an existing binary without compilation. Build resolves the compiler-managed toolchain under §20.8.8; these commands never download/install LLVM or the Windows SDK. A dedicated runtime DLL is not required; runtime bodies are emitted in the same module, with separate native backend support (§21.5.7).

| Setting | Initial rule |
| --- | --- |
| Targets | x86_64-pc-windows-msvc |
| OutputKind | Application (default) or inspection-only Library (§22.2.2) |
| OutputPath | .ll destination; default bin/<target>/<ProjectName>.ll |
| NativeLibraries | Optional per-target logical name to kind/input mapping (§20.8.2); kimi_backend is resolved automatically when omitted |
| Optimization | O0 or O2 (default); applied during native build |
| LlvmBin | Optional legacy LLVM-only override; omitted by normal projects using §20.8.8. A relative project value is project-relative; CLI --LlvmBin overrides it and is invocation-relative. emit-llvm records a project value without executing tools. Neither changes the target/version contract or the default backend location. |
| EntrySource | Not an initial selection setting; use §22.2's unique-candidate rules |

The first execution subset is ordinary functions, simple local bindings, Unit, string literals, required ownership/cleanup, and Core.writeLine. Arrays, Dictionary, inheritance, closures, static Property execution, general generic sharing, and multiple-Kotonoha linking need not be included in this first execution test. Their language rules are not weakened; unsupported required operations fail. Layout computability, physical ABI support, and runtime availability are separate checks.

Generation success certifies the matched IR/manifest pair, not LLVM acceptance, a linked executable, or successful execution. LLVM verification/object generation, manual linking, and running the produced executable are separately reported stages. Library output supports inspection/verification/object generation only.

### 20.8.2. NativeLibraries

Each Ordinal logical key maps to `kind` (import or static) and one .lib `input`, never a DLL file. Import produces dllimport declarations; static does not. A simple filename is a linker search name, a relative path with separators is project-relative, and an absolute path is unchanged. Diagnose empty/NUL values or embedded linker options; never execute these strings as commands.

Reserve kernel32 as an automatically generated import library and kimi_backend as the compiler-managed static library at `<toolchain root>/windows_x64/kimi_backend_windows_x64_v1.lib` (§20.8.8). Neither requires a NativeLibraries entry. A kernel32 entry in NativeLibraries is an error with a diagnostic instructing removal; no SDK kernel32.lib or user-provided replacement is used. Other keys require configuration; do not guess .lib names from DLL names. An explicit kimi_backend path remains a compatibility override and must select the adopted supply.

The compiler embeds the project-owned backend/windows-x64/kernel32.def. It contains KERNEL32.dll and the seven runtime APIs in §22.5.6 plus VirtualAlloc, VirtualProtect and VirtualFree for backend tests. Normalize the definition to UTF-8 without BOM, LF line endings and one terminal newline; verify its SHA-256 against profile.json. During native build, materialize the definition in a fresh staging directory and invoke `llvm-dlltool -m i386:x86-64 -d kernel32.def -l kernel32.lib`. Verify the generated DLL name, x64 COFF formats and exact public/__imp_ symbol set before publishing and linking the library. The runtime still uses the OS-provided KERNEL32.dll. Additional kernel32 imports require a reviewed definition/profile update; the current library is not a replacement for the entire SDK export surface.

Static libraries must not depend on CRT startup, automatic C/C++ dynamic initialization, custom TLS initialization/termination, or automatic atexit handlers. Zero-initialized and constant data are allowed. Code needing such startup/termination needs a supported adapter first. DLL initialization follows §22.2.3. These are supplier/user connection contracts: a .lib filename and /NODEFAULTLIB do not establish or perform initialization.

### 20.8.3. Link manifest and publication

Replace OutputPath's extension with .link.json in the same directory and write UTF-8 JSON for both output kinds. The schema is:

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

Hashes must be actual SHA-256 values. packageVersion is supplied by Directory.Build.props Version (currently 0.1.1), not a separate backend release counter. A version alone does not establish an adopted archive; the catalog hash and ABI must also match. observer and the __chkstk expected reference are conditional examples; _fltused is always supplied.

- Deduplicate libraries required by external declarations or the profile and sort by Ordinal logical name. Rewrite path inputs relative to the manifest; preserve linker search names. irFile is manifest-relative.
- Library uses null entry and subsystem. Its dependency record does not establish an external .lib/DLL ABI or runnable artifact.
- codegen is mandatory and matches §21.5.1; irSha256 hashes pre-optimization .ll.
- backendSupport comes from the adopted catalog. Its library references a static libraries entry; ABI/version/symbols must agree. Manual builds verify the actual .lib hash before use.
- The default kimi_backend entry uses resolution=toolchain and has no input path. Build resolves it under §20.8.8, preserving manifest portability and LLVM-free emission. An explicit legacy backend override instead has input and no resolution. Reject unknown resolution values and entries combining resolution with input. Schema 3 is required; schema 1/2 inputs must be re-emitted.
- providedRuntimeSymbols lists generated backend definitions, always _fltused, not ordinary __kimi_ helpers. expectedUndefinedSymbols lists backend references anticipated at generation, with a known provider in libraries. Verify backend symbols against backendSupport.providedSymbols; unknown providers for known dependencies fail generation.
- Sort both symbol arrays by Ordinal symbol name, without duplicates or overlap. They are not the final object's undefined-symbol list; even an empty list cannot promise no later dependency. LibraryImport/Windows inputs are recorded in libraries.
- Generated kernel32 entries have no input path. Validate the generator, DLL and definition hash against the embedded profile; reject substitutions and extra input paths. emit-llvm still requires no native tools and publishes no import library.
- Build records retain actual library hashes, generator/tool identities, normalized definition hashes and tool settings. Each project/optimization writes its own .kernel32.def/.kernel32.lib. Generate into a fresh staging directory, publish only after validation, and fail without linking stale libraries if generation fails. Never embed absolute build paths in the generated library; redact local report paths.

Complete both temporary outputs before publication, publish the manifest last, and report success only after both are published. A partial publication is failure; old files are not evidence of current success. Consumers check irSha256 because interruption can leave a mixed pair. Success reports both paths, purpose (Application input or Library inspection), entry, and required link inputs.

When the legacy LlvmBin setting is configured, the manifest may additionally contain `"toolchain": { "llvmBin": "<manifest-relative directory>" }`. Resolve a relative setting from the project directory. This is a local build-tool location, not part of the code-generation profile or evidence of a tool's version. A build command or separately invoked builder may override the location, but must still check the actual tool versions under §20.8.5; a directory name or configured path cannot certify version compatibility. Default projects record no local toolchain path. External user library files remain configured through NativeLibraries; kimi_backend resolves from the toolchain and kernel32 is generated from the embedded definition.

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

Use every manifest input, not just the example's libraries. O0 omits opt and passes the original .ll to llc -O0 with the same profile. Verify IR before/after optimization and inspect actual object dependencies. /debug does not create Kimigayo line/variable information; CodeView/PDB emission remains separate from Abort source context.

Before adopting a profile, validate it with the pinned LLVM version; another version's preliminary result is not acceptance. Keep semantic tests, representative IR structure/goldens, object ABI/unwind/dependency checks, and execution results distinct (§A.14). Performance decisions use measured execution time, code size, and build time, never weakened checks.

### 20.8.5. LLVM version checks and exploratory builds

The expected LLVM release has one machine-readable source: `backend/windows-x64/profile.json`, currently 22.1.8. The compiler embeds this catalog, exposes its version through WindowsProfile.LlvmVersion, and writes it as codegen.llvmVersion. Native build and verification scripts read the same catalog; do not duplicate the expected version in executable code. This value describes the intended profile, not a detected installation. Parsing, semantic analysis and IR/manifest generation neither execute LLVM nor require it to be installed.

Immediately before native toolchain work, check every selected version-reporting tool's actual `--version` output. Compare the full release version, including patch level; newer versions are not implicitly supported, and development/prerelease suffixes do not match the release. Parse a recognized tool version banner, not an arbitrary occurrence of the expected number. Missing tools, failed probes, and absent or ambiguous version information are errors even in exploratory mode. Diagnostics identify the tool path, expected version, and actual version or probe failure. The versionless llvm-lib exception remains explicit: retain its executable path/hash and record it as unversioned, never invent a matching version. llvm-dlltool also has no version banner: compare its executable SHA-256 against profile.json kernel32.dlltoolSha256 before invocation. Record expectedSha256, sha256 and hashMatched without fabricating actualVersion. An explicit exploratory override permits a different hash with a warning and unverifiedToolchain=true; it does not bypass definition or generated-library validation. reportedVersionsMatched describes only version-reporting tools.

Normal native builds fail on a mismatch before IR verification, optimization, object generation or linking. `kimi build --AllowUnpinnedToolchain true` and the separately invoked manual/backend builders' `-AllowUnpinnedToolchain` permit exploratory work only. Each mismatched tool then produces a visible warning and may continue through the ordinary verification/build steps. This option does not override manifest/profile mismatches, IR/archive hashes, ABI contracts, dependency checks or other errors. Adoption and generated-module profile verification still require matching tools and cannot use this override.

After successful version probing, native build records retain the expected version, actual per-tool versions and executable identities, reportedVersionsMatched, and unverifiedToolchain. Retain these facts even if subsequent native work fails, with status incomplete. A successfully linked or executed exploratory program remains unverified for the pinned profile; do not relabel its manifest with the detected version or count it as profile adoption evidence. Formal support for another LLVM release requires renewed profile validation and a deliberate catalog update.

### 20.8.6. Compiler commands and artifact lifecycle

| Command | Required behavior |
| --- | --- |
| `kimi emit-llvm <project-or-solution>` | Perform the required source/ownership/generation checks and publish the matched pre-optimization .ll/.link.json pair. Never execute LLVM, validate an installed LLVM version, link or run. Successful output reports both paths; LLVM acceptance is a separate stage. |
| `kimi build <project-or-solution>` | Generate fresh LLVM inputs, validate the actual tool versions and native inputs, run opt verification (and default<O2> only at O2), llc and lld-link, and publish the executable and a successful build record. Never execute the Application. |
| `kimi run <project>` | Resolve the configured existing executable, require a successful latest build record and matching executable hash, and execute without source analysis, IR generation, LLVM version checks or rebuilding. Source changes do not trigger compilation; users explicitly build when needed. |
| `kimi run <path.exe>` | Execute the explicitly selected existing binary directly without a project or build record. |

Build and emit-llvm accept configured projects/solutions or discover them in the specified directory (current directory when omitted). Loading any selected project unsuccessfully is failure; empty discovery is not a successful build. Run through project/directory/solution discovery requires exactly one loaded Application. Do not silently choose the first of several projects. Standalone source compilation is not part of run. The current native profile supports Windows x64 Applications; unsupported targets or Library emission must receive diagnostics rather than placeholder binaries. The existing limited emitter remains limited (§21.4); command automation does not add language-feature support.

OutputPath continues to name the pre-optimization .ll. For `Name.ll`, optimization O0/O2 selects `Name.O0.obj` / `Name.O2.obj` and `Name.O0.exe` / `Name.O2.exe`; O2 also retains `Name.O2.ll`. The build record is `Name.link.build.json`. A configured --Target selects one of the project's configured targets; the current emission implementation requires exactly one Windows x64 target. The CLI Boolean option requires an explicit value, e.g. `--AllowUnpinnedToolchain true`.

At the beginning of a native build attempt, invalidate the previous success record before semantic analysis. Link into a fresh temporary executable and publish it only after success. A failed build may retain a prior executable for inspection, but the project run command must not treat it as a successful result of that attempt. An emit-llvm command does not rewrite the native build record. Build records retain version, input/tool identity, optimization and executable hash information; descriptive machine paths are remapped and are not execution inputs. Launch through a project derives the executable path from its current output settings and verifies the recorded hash; changing those settings requires the corresponding built artifact. An explicitly selected .exe path remains independently runnable.

External processes are launched directly with separately supplied arguments, without constructing shell commands. Drain native-tool stdout/stderr concurrently, report failures, propagate cancellation to child process trees, and bound individual native-tool invocations (currently five minutes). Run forwards stdin and the child's stdout/stderr, preserves output bytes, and returns the child exit code. Project runs use the project directory as working directory; direct binary runs use the caller's current directory. Build/emit failures and launch errors return 1, successful build/emit return 0, and command cancellation returns 130. No fixed Application runtime timeout is imposed.

### 20.8.7. Compiler and backend release version

Directory.Build.props Version is the single release-version source for Kimigayo and its backend package. The compiler embeds that MSBuild value as assembly metadata and uses it for the default version display, compiler build identity prefix and backendSupport.packageVersion. Repository tools combine that same props value with profile.json; profile.json retains the LLVM release, ABI, helper symbols and adopted archive hash, without its own package-version literal. Language version, ABI version and LLVM version are independent identifiers and do not change simply because the package release changes. Candidate reports carry the shared release with adopted=false; release equality never substitutes for native archive/hash validation. Changes to adopted archive contents require renewed validation and a shared release update before distribution.

### 20.8.8. Toolchain storage and native library lifecycle

Kimigayo manages the LLVM executables and adopted native backend as one relocatable toolchain. In a source checkout the canonical location is `Kimigayo/toolchain`; a standalone compiler distribution places `toolchain` beside Kimi.exe (or Kimi.dll). Normal .kimiproj files specify language/build choices such as Targets, OutputKind and Optimization, with no LlvmBin or kimi_backend path.

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

**Location resolution.** A compiler build selects the root in this order: explicit `--ToolchainRoot`, `KIMI_TOOLCHAIN_ROOT`, then an executable-adjacent toolchain. Relative explicit/environment roots are relative to the invoking working directory. For source builds only, if the adjacent directory is absent, walk ancestors of the executing compiler/test-host directory to the checkout identified by Kimigayo.slnx, Kimi/Kimi.csproj and backend/windows-x64/profile.json, and use its toolchain. Never discover a toolchain by walking the user's project or current working directory, searching PATH, or guessing an SDK installation. An explicitly selected missing or incomplete root fails rather than falling back. A standalone installation without tools reports the expected executable-adjacent path.

Repository PowerShell builders share the same root layout: `-ToolchainRoot`, then KIMI_TOOLCHAIN_ROOT, then the checkout's toolchain located relative to the script. Their `-LlvmBin` option and the compiler's CLI/project LlvmBin remain optional LLVM-only compatibility overrides; they do not relocate the backend. The compiler applies CLI --LlvmBin before project LlvmBin before the selected root. The manual builder applies -LlvmBin before the manifest's legacy toolchain.llvmBin before the selected root. Normal use needs none of these overrides. Root configuration is local installation state, not a new .kimiproj field or a language/ABI version.

**Initial setup.** `backend/windows-x64/setup.ps1 -LlvmBin <existing LLVM directory>` validates the complete selected LLVM tool set against the embedded profile, copies the nine executables above and adjacent support DLLs to the selected toolchain root, then invokes backend build/verification using those copies. Without -LlvmBin it validates and uses tools already in the root. The helper sources and freestanding test harness require no Windows SDK or additional Clang headers. This setup command uses an existing local installation; it does not download tools or change PATH. It succeeds only if this run produces the adopted backend and the installed archive matches the catalog. Missing tools, failed version probes and mismatches are errors; setup has no exploratory override. Distribution of LLVM must retain its applicable license notices. Generated/copied toolchain contents are ignored by Git; sources, scripts and the authoritative catalog remain version-controlled.

```powershell
# Once per toolchain installation, from the Kimigayo checkout:
./backend/windows-x64/setup.ps1 -LlvmBin C:/App/llvm

# Rebuild the native backend after changing its sources:
./backend/windows-x64/build.ps1

# Ordinary project builds need no tool or native-library path settings:
dotnet run --project Kimi -c Release -- build examples/Hello/Hello.kimiproj
dotnet run --project Kimi -c Release -- run examples/Hello/Hello.kimiproj
```

**Backend generation and installation.** build.ps1 explicitly assembles the project-owned src/memcmp.S, memcpy.S, memmove.S, memset.S and chkstk.S with `clang --target=x86_64-pc-windows-msvc -c`, producing one native COFF .obj per helper. llvm-readobj checks x64 format, unwind records and absence of CRT/TLS/default-library directives; llvm-nm checks absent undefined symbols; llvm-objdump checks absent calls. llvm-lib archives the five objects using filename-only member names. The archive must export exactly __chkstk, memcmp, memcpy, memmove and memset, with no _fltused. These assembly bodies stay outside LLVM IR optimization, preventing memory loops from becoming recursive libcalls (§21.5.7).

The script creates kernel32.lib from the reviewed kernel32.def with llvm-dlltool for its test harness. It compiles tests/native.c to LLVM IR and tests/probe.S to COFF, verifies IR, builds both O0 and O2 variants with opt/llc, links through lld-link with a custom entry and /NODEFAULTLIB, and requires zero native exit codes within the timeout. Tests cover memory boundaries/overlap/returns, guard pages and the special __chkstk ABI. Source/tool/archive hashes and diagnostics are retained under backend/windows-x64/bin; verification.json begins incomplete and becomes tested-candidate only after this run's checks, including unchanged source identities.

After successful verification with pinned tools, an archive matching profile.json's adopted SHA-256 is copied through a fresh temporary file, rehashed, and published at toolchain/windows_x64/kimi_backend_windows_x64_v1.lib. The verification report remains adopted=false: successful reproduction does not create a new catalog adoption. An exploratory or different-hash candidate remains in backend/windows-x64/bin with a warning and does not replace the installed library. A changed archive requires native/generated-module review, a catalog update and the shared release policy in §20.8.7 before installation. A failed candidate build does not make old output evidence of current verification. Setup additionally rejects a non-installable candidate even when an older installed file exists.

**Reference and Application build.** emit-llvm writes the schema 3 logical kimi_backend entry and expected catalog identity, without locating the default toolchain or reading its archive. Thus semantic checking/emission works before setup. `kimi build` resolves LLVM from the selected root and the backend from its windows_x64 subdirectory, validates tool versions, catalog ABI/release and the actual archive hash, and creates a separate validated kernel32 import library for each project/optimization. The generated kernel32.def/.lib remain beside project build outputs; they are not a shared mutable toolchain cache. Explicit legacy NativeLibraries backend paths undergo the same hash/ABI checks.

```text
backend src/*.S -> clang -> COFF objects -> llvm-lib -> candidate .lib
  -> O0/O2 native verification -> adopted-hash check -> toolchain/windows_x64/*.lib

Kimigayo source -> semantic checks -> .ll + .link.json
  -> opt verification / optional O2 -> llc -> Application.obj
  -> lld-link(Application.obj, toolchain/windows_x64/backend.lib, generated kernel32.lib)
  -> Application.exe + successful build record
```

The linker receives the backend as a profile-wide static input; only needed members are extracted. Build records retain the actual resolved tool/library paths with the report's path-redaction policy, their hashes, selected settings and executable identity. The executable contains the selected helpers and does not need the .lib at execution time. run uses the existing executable and performs no LLVM/backend regeneration. Neither dotnet build, ordinary kimi build, nor the current .NET CI workflows invoke backend build.ps1 automatically; setup or explicit backend regeneration supplies the library. This toolchain packages build inputs and adds no dedicated runtime DLL, extra runtime operation or stable language-function ABI.
