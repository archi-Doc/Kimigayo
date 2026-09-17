# 18. Modules and dependencies

[Specification index](../SPEC.md)

A module groups source declarations and controls the names exposed through dependencies. Source environments belong to definitions, not to their callers or merged-container wrappers.

| Term | Meaning |
| --- | --- |
| Kotonoha | One named module; initial external distribution uses source packages. |
| SourceDocument | One immutable source input belonging to a Kotonoha. |
| Compilation root | Entry point for the project root and direct-dependency reference names. |
| Project root | Root of the primary Kotonoha's declaration hierarchy. |
| Source environment | A document's definition-site aliases and lookup context. |

Merged declarations keep each fragment's definition-site source environment for names, Types, Constraints, and diagnostics. Merging must not apply one fragment's aliases to another. Generated documents have independent source environments.

Each Compilation owns a **Compilation root**, direct-dependency reference-name mappings, and default aliases. The primary Kotonoha's Container hierarchy ends at the **project root**. A resolved Symbol identifies its declaration, including originating Kotonoha/version, independently of spelling or alias path.

## 18.1. External references and aliases

Only directly referenced Kotonoha libraries are source-addressable by reference name. Use qualification such as `ExternalLib.GroupA.StructB` or an explicit `alias` declaration; do not search all external members unqualified. Multiple versions may use different reference names under §18.4. Loading transitive metadata for type checking does not expose those libraries by name.

### 18.1.1. Forms and resolution

| Form | Introduction |
| --- | --- |
| `alias Path` | Open the Container's direct members in their respective namespaces. |
| `alias Name => Path` | Introduce the target itself as a Type-namespace Qualifier named `Name`. |

Both forms precede ordinary declarations and executable items at the SourceDocument top level. Nested aliases and access modifiers on named aliases are invalid. Directive selection and generation follow Chapter 19. `Name` is one ordinary identifier; the right side is a Container reference, not an expression or executable Body.

Named targets are limited to Kotonoha and groups. They create no Core, value, storage, or declaration Identity and do not open the target's members. The opening form retains its existing Container target range. Neither form adds new Container placements or ways to instantiate them (§6.1). Where the Container rules permit a group under an instantiated parent, a named alias may retain that reference, with all required Type and Origin arguments bound; it acquires no generic parameters of its own.

Resolve the entire target from the Compilation root without source or default aliases, including inside Type arguments and Origin specifications. Leading `::` is optional and does not change resolution. Built-in syntax such as `i32` and `from static` remains valid. Retain the definition environment when checking referenced declarations' bodies and constraints. Alias chains, cycles, source-order resolution, and transitive dependency reachability are not introduced. A real root declaration or direct reference with the same spelling remains usable.

```kimi
alias Output => ::Kimi.Console
alias K => Kimi
Output.writeLine("Hello")
K.Console.writeLine("Hello")
```

```kimi
alias K => Kimi
alias Output => K.Console       // Error if K exists only as an alias.
alias Number => i32             // Error: a Core is not a named-alias target.
alias O => Kimi.Option<i32>      // Error: an enum is not a named-alias target.
alias f => Kimi.Console.writeLine // Error: a function is not a Container.
alias Number = i32              // Error: no Type-alias declaration syntax.
```

### 18.1.2. References, validation, and scope

An alias retains the original declaration Identity and normalized argument bindings, not path substitution or use-site reinference. Register access, Type formation, input-condition and Origin obligations at the declaration, including intermediate qualifiers. Reject established errors; unresolved generated information may wait only until its existing deadline. Final Binding completes every required obligation. Check member access and use conditions at each use; an alias supplies neither access rights nor conformance evidence.

Reference identity and validation completion are separate. Equal fixed references may share structure while obligations remain unresolved, but each path retains its obligations, state and dependencies. Sharing and deduplication never imply that validation succeeded.

Aliases apply only to their SourceDocument. They do not propagate to other files, merged fragments, generated documents, or callers. They are not re-exported and create no root member addressable as `Project.A` or `::A`. Public Signatures still require access, direct dependencies and Name Reachability for the original Types. See §9.4.1 for collision and warning rules.

An alias may retain a fully bound nested Container, such as alias Family<i32>.Helpers or alias H => Family<i32>.Helpers. Its reference is fixed at declaration; uses never infer arguments again. Opening aliases import only direct members, not formal parameters, Origins or Self. Deduplicate only equal declarations with equal full bindings. Opening Family<i32>.Helpers and Family<string>.Helpers leaves two candidates for an inner Token even when Token is empty; ordinary same-name/same-arity ambiguity applies. This is Container lookup, not transparent Type-alias syntax.

```kimi
// Definitions.kimi
struct Family<T>
    public group Helpers
        public struct Token

// Use.kimi, in the same Kotonoha
alias Family<i32>.Helpers
group Use
    func make() -> Token => Token.init()
```

### 18.1.3. Effective default aliases

The effective defaults combine the language version's mandatory aliases with the defining module's configured additions. This revision requires an alias opening the reserved Kimi Kotonoha in every document, including dependency and generated sources. Configuration cannot remove it or replace its target.

Mandatory and additional aliases participate together in the default lookup stage. Deduplicate identical resolved references within that stage. Explicit source aliases belong to the earlier explicit stage: `alias Kimi` can therefore change precedence. Never deduplicate across stages in a way that changes lookup order. Opening Kimi exposes `Console`, not its members; an additional default `Kimi.Console` exposes bare `writeLine`.

Project configuration and manifest `aliases` store only user additions; do not append mandatory aliases when saving. Reconstruct the effective environment from the language version, selected Kimi contract, and saved additions. Empty additions and an addition of only `Kimi` have equal effective settings. Configuration equivalence that requires no name resolution may be normalized before input hashing; general path equivalence is determined later by semantic analysis.

## 18.2. Re-exports

**Deferred design:** re-export syntax and artifact representation are undefined; aliases never re-export. Consumers naming an external Type/requirement must directly reference its originating Kotonoha and have an accessible Symbol path. Neither a public Signature containing that Type nor transitive metadata loading supplies Name Reachability.

Any future re-export design must preserve the original Symbol, avoid widening access, and reject cycles without a real target. Its syntax and compatibility requirements belong to that feature's specification.

## 18.3. Source artifacts and binary interfaces

Publish openness and the dependencies of Sealed proofs, payload projections, complete-target effects, and all allowed Type/Origin/full-specialization checks. Recheck positive and negative proofs after openness changes. Treat an openness change as an API change and a breaking change when it invalidates a previously valid use. Do not replace required ObjectCallCompatible checks with Sealed alone.

Initial distribution is **source-first**: Project references, source packages, exact versions, local publication stores, and optional verified semantic caches. Each package contains one Library Kotonoha. Inspection `.ll` files and serialized Koto graphs are not distribution formats. Distribution does not change definition-site lookup, access, generic selection, or ownership boundaries. Unsupported required Types, Loans, effects, cleanup, or generation paths must be diagnosed.

Private-source binaries, machine-code caches, stable external Kimigayo ABIs, network registries, version ranges, dynamic loading, and re-exports are deferred. A future binary format must retain the complete semantic information in §18.7 plus corresponding code, ABI, layout, calling conventions, and target identity. It must define trust, code/proof correspondence, recheckable evidence, and updates that cannot be rebuilt. Private generic plans may disclose implementation details even without original source. No independent certificate or signing system is introduced.

There is no Package-to-Project override. Updating a child and republishing to the same store can require new versions for all published ancestors whose dependency edges change. Repeated local `pack` operations do not reserve versions.

## 18.4. Dependency configuration and resolution

### 18.4.1. Identities and graph

| Identity | Purpose |
| --- | --- |
| ReferenceName | The consumer's source name for a direct dependency |
| PackageId / PackageVersion | Defining module and exact release |
| SourceId | Exact source-package content (§18.6) |
| ProjectSnapshotId | Local product source and effective semantic settings (§18.5.2) |
| ModuleInputId | Whole-module verification inputs (§18.7.1) |
| DeclarationKey | Declaration correspondence across revisions, not semantic identity (§18.7.2) |

PackageId starts with an ASCII lowercase letter and consists of lowercase ASCII letters/digits in nonempty segments separated by `.` or `-`. PackageVersion starts with an ASCII letter/digit and then permits ASCII letters/digits, `.`, `-`, and `+`. Compare versions by exact, case-sensitive equality; infer neither ordering nor compatibility. Referenced Libraries and packing roots require both fields. An Application need not have a package identity merely to consume dependencies.

ReferenceName is a normal language identifier and must not collide with project-root declarations or reserved Kimi reference name. Multiple names/paths for one dependency do not create new Types or statics. Types and Symbols from different versions remain distinct.

The module graph is a DAG. Diagnose self-reference and cycles with dependency paths; mutually dependent declarations may reside in one Kotonoha. Within a target graph, including its test extension, each ID/version has one input kind, content, and semantic configuration. Merge identical nodes reached by several paths; report both paths for conflicting inputs. Do not implicitly substitute a Project for a Package.

Each module retains its own aliases/default aliases, access context, declarations, and closed specialization set. Consumers cannot append declarations or rebind the definition environment. The compiler selects one Kimi identity and contract for the graph.

### 18.4.2. Project references and sources

`Dependencies` maps ReferenceName to required `PackageId` and `PackageVersion`. A record may specify `Project` (`.kimiproj`) or `Package` (`.kimipkg`), never both. Omitting both requests a Package from `PackageSources`. Paths are relative to the project that declares them. Check the loaded identity against the requested one.

```text
OutputKind="Library"
PackageId="example.geometry"
PackageVersion="1.0.0"
LangVersion="0.0.1"
Targets=
  "x86_64-pc-windows-msvc"
Dependencies=
  Math={
    PackageId="example.math"
    PackageVersion="1.2.0"
    Project="../Math/Math.kimiproj"
  }
```

```kimi
// Math/Arithmetic.kimi
public group Arithmetic
    public func twice(value: i32) -> i32 => value + value
```

```kimi
// Geometry/Measure.kimi
alias Math.Arithmetic
public group Measure
    public func doubled(value: i32) -> i32 => twice(value)
```

`PackageSources` is an array of either `{PackageId, PackageVersion, Package}` or `{Store}` records. A Store is a directory. For an unfixed SourceId, explicit packages and store release tables are candidates for the exact ID/version. Require one candidate SourceId; conflicting candidates are errors, not first-match choices. Release tables are lookup indexes, not integrity or semantic proof: validate the selected manifest and bytes.

```text
PackageSources=
  { Store="packages" }
Dependencies=
  Math={ PackageId="example.math" PackageVersion="1.2.0" }
```

A manifest-fixed dependency, or a command using a valid lock, accepts only the recorded SourceId and may open `<Store>/<SourceId>.kimipkg` directly. A release table claiming another content for that release is inconsistent; do not silently change the pin. A plain pack directory without a release table can serve known SourceIds. The user cache accelerates retrieval of already selected content and never chooses a version.

Include direct Package locations and sources declared by reachable Projects. Collect the required partition's Project configurations before resolving Packages; load order must not select content. Test sources cannot supply product-resolution candidates. Candidate registration grants no source name. Do not scan directories for arbitrary packages, choose a version range, or guess missing sources. Replace the old unimplemented `KotonohaArray` setting with Dependencies; a nonempty old setting receives a migration diagnostic.

### 18.4.3. Effective environment

Use one target and debug/release mode across the graph; every dependency must allow that target. CompileTimeSettings and default aliases belong to the defining module. Dependency-edge overrides, implicit feature unions, and conditional dependency edges are not initial features. Publish separate configurations under separate PackageIds.

Ordinary builds resolve omitted LangVersion using the root's solution/compiler defaults (§20.5), without discovering another solution for a dependency. Each Project packed into a source package must explicitly declare LangVersion and Targets. Other distributed settings use only that Project's declarations or defaults fixed by its language/artifact version; absent CompileTimeSettings means an empty map. Require explicit values where an outer environment or compiler build would otherwise supply a changing default. Store resolved settings in the manifest.

Initially, verify the graph with one effective language version and the current compiler build; the publisher's compiler build need not match. Each content ID uses effective values within its scope, so equivalent omission and explicit values compare equally. Consumer target/mode/compiler do not enter SourceId; they enter ModuleInputId. Publication immutability is scoped to the destination store.

## 18.5. Lock files and input records

### 18.5.1. Resolution state

`restore` alone updates dependency locks. It resolves the product first, then the root's test extension, and saves both results together. It reads declared local sources without network access, Mod execution, or source execution. Product failure gives a nonzero result; test-only failure is reported and recorded but does not fail product restore. An error preventing interpretation of the whole configuration cannot be isolated as a test-only failure.

Restore re-resolves Project-declared Package references from current sources. Thus changing an explicit trial Package path and restoring may pin different content under the same version. It never rewrites a Package manifest's pinned children. Other commands do not update locks; command responsibilities are listed in §20.8.6.

For `Name.kimiproj`, use `Name.kimi.lock.json`, schemaVersion 1. Each partition records resolved/unresolved status and a reason code, path-free dependency structure, node ID/version/input kind, reference-name edges, and Package SourceIds. Identify the root by a dedicated node. Keep detailed diagnostics containing host paths in the processing record, not the lock.

Root and Project sources/settings remain live. Changes to IDs, versions, reference names, edges, input kinds, or pinned Package content require restore. Source edits and other settings require appropriate revalidation, not lock rewrites. Recompute locations from current project settings; moving identical inputs does not require restore. Project snapshots and source bytes do not belong in the lock.

When a lock exists, always validate the command's required partitions, even if current dependency declarations are empty. Only a missing lock with empty required dependencies denotes an implicit empty resolution. Never hide a corrupt lock as empty. Product commands require only the product partition; test requires both. `--locked` explicitly requests failure when restore is needed; ordinary input commands already make that check and never restore automatically. It does not prohibit Project source edits.

Restore builds both partitions from one configuration snapshot and does not retain an old test partition. Product failure also makes the test partition unresolved. Test checks success and current structure of both partitions; no separate product-generation digest is needed. Under a write lock, compare the prior lock content again before atomic replacement; fail on concurrent change. Serialize deterministically and avoid rewriting an unchanged result. Failed resolution may be recorded as unresolved, never as an earlier success.

```powershell
kimi restore Geometry.kimiproj
kimi check Geometry.kimiproj --locked
# Editing Math's body needs rechecking, but no restore or lock update.
kimi check Geometry.kimiproj --locked
```

### 18.5.2. Immutable processing records

Fix each input byte snapshot and use those same bytes for verification, generation, and packing; do not re-read a mutable path after checking it. ProjectSnapshotId covers logical product-source paths/order/bytes and normalized effective semantic settings, including reference names and dependency IDs/versions. It reflects source membership rather than raw project-file formatting. Exclude test-only files/dependencies, retrieval/output paths, and generation-only optimization settings; record those in their appropriate stages. Ordinary files containing `#Test` retain their raw bytes. Include supported Mod registrations, implementations, and additional inputs under §20.7.5. ProjectSnapshotId is not SourceId. SourceId reflects the actual manifest bytes and content-hashed files; different saved bytes remain different content even when effective defaults are equal (§18.1.3). Stage input records retain their language/compiler/verification versions and Kimi contract. Input IDs do not require full alias resolution. Semantic reuse retains resolved references, lookup stages, definition environments, verification assumptions and dependencies, including candidate absence and access changes. Alias names and paths do not enter the underlying declaration or Type Identity. A rename alone never makes old inputs compatible.

A build input record covers the root and every actual input, effective environment, resolved edges, verification rules, and native inputs needed at that stage. Mark unresolved native inputs and incomplete work explicitly. Bind each output to its input record and output hash. Lock equality alone does not fix root source, native files, toolchain, or runtime environment.

Store immutable records by content ID, with IDs/hashes and necessary structure/settings rather than duplicate input byte arrays. Retain the transitive content closure for records whose reproducibility must be preserved. Latest pointers, explicitly retained records/locks, and active processing pins are collection roots. Acquire pins before use and coordinate them with collection; unreferenced records/content may be reclaimed. Hashes cannot reconstruct missing bytes. Pack directories and publication stores are not automatically collected as user caches, and published release mappings survive content removal.

## 18.6. Source package format and publication

### 18.6.1. Packing a fixed graph

`pack` accepts a Library Project and performs these steps:

1. Validate the product lock and required explicit settings; report missing settings across reachable Projects. Fix current root/Project inputs.
2. Convert Project dependencies bottom-up to distribution inputs and compute SourceIds. Validate existing Packages for reuse.
3. Pin parent edges to child ID/version/SourceId and check uniqueness in the resulting graph.
4. Verify that final graph with the normal package loader and semantic verifier.
5. Store the complete source-package closure, including existing Packages, children before parents. Report all content IDs and locations.

Preserve product membership, logical paths/order, effective settings, user-added default aliases (§18.1.3), and native requirements; remove host locations. Distribute original conditional source, not a selected-branch rewrite. Neither a ProjectSnapshotId nor ID/version alone may select some earlier packed content. Validation may use a logical manifest/file view before ZIP serialization, provided the writer receives the same verified bytes.

The default directory is `bin/packages`; `--output <directory>` overrides it. Root and children use `<SourceId>.kimipkg` names and identical content is stored once. Complete each temporary archive, then use an atomic no-overwrite rename. Validate an existing/racing file under §18.6.3 and reuse only matching content. A parent becomes available only after its children are present. Trial packs may retain different SourceIds for the same version. Native files, runtime DLLs, and toolchains are outside this source closure.

Default verification uses build's single-environment selection: explicit `--Target`, otherwise the only configured target; multiple targets require selection. Release is the default mode, with `--Debug` selecting debug (Boolean-value spelling: `--Debug true`). `--verify-all` verifies every declared target times both modes, conflicts with target/debug selection, and fails on unsupported environments or any failed check. Do not finalize outputs after failure. Semantic verification requires neither code generation nor native link/execution.

Manifest targets state allowed use environments, not successful verification. Record successful combinations separately with SourceId, compiler build, and effective settings; unverified combinations are not successes. Consumers verify in their own environment. Initial packing excludes inputs requiring Mods; generated output cannot masquerade as original source. A future extension must preserve §20.7.5's registration, implementation, API, input, target, order, and provenance contract.

```powershell
# Set LangVersion and Targets in Geometry and each Project dependency.
kimi pack Geometry.kimiproj --output trial --verify-all
# Trial edits may keep the same version.
kimi pack Geometry.kimiproj --output trial --verify-all
# Replace <SourceId> with the root ID reported by pack.
kimi publish "trial/<SourceId>.kimipkg" --store packages
```

### 18.6.2. Manifest and logical files

A `.kimipkg` is a ZIP of regular files with a root UTF-8 `manifest.json`. SchemaVersion is 1 and kind is `source`. JSON keys are lowerCamelCase; project settings are PascalCase.

| Required key | Meaning |
| --- | --- |
| schemaVersion, kind | Format version and artifact kind |
| packageId, packageVersion, langVersion | Defining identity and resolved language version |
| targets | Set of allowed targets |
| compileTimeSettings, aliases | Definition settings and user-added default aliases; mandatory defaults are reconstructed (§18.1.3) |
| files | Every regular entry except manifest itself: path, size, sha256 |
| sources | Ordered product-source paths |
| dependencies | ReferenceName to packageId/packageVersion/sourceId map |
| requiredFeatures | Set of required compiler-catalog features |
| nativeRequirements | Per-target native requirements (§20.8.2) |

Keep empty maps/arrays. Each compileTimeSettings value contains exactly one bool/integer/string alternative. Sources are unique and refer to files entries; files and non-manifest archive entries correspond one-to-one. Exclude test-only files/dependencies. Keep inline `#Test` source unchanged and exclude it by membership rules, not source rewriting.

Logical paths use `/`-separated relative names. Reject empty components, `.`, `..`, absolute paths, drives, NUL, backslashes, link entries, and duplicates. Require NFC under Unicode 15.0.0, matching §2.5; do not silently normalize. Detect collisions by default simple case folding (C/S mappings, not F/T), then NFC, using the same Unicode release. Hash the original NFC spelling. This path rule does not change case-sensitive language Name lookup. Host extraction is optional; if performed, reject host-reserved/aliasing paths rather than overwriting another file. Preserve source newlines and Unicode bytes.

Reject unknown schemas/keys/required features, duplicate keys, invalid references, out-of-range integers, missing/extra entries, and size/hash mismatches. Bound entry counts, expanded bytes, and string lengths; distinguish malformed input from resource exhaustion.

### 18.6.3. Content identity and integrity

SourceId is the lowercase, 64-digit hexadecimal SHA-256 of:

```text
ASCII "Kimigayo.Source.v1" + NUL + exact manifest.json bytes
```

The manifest excludes its own SourceId and commits to all other files through their paths, lengths, and hashes. Compression and ZIP timestamps do not define SourceId. Matching manifest hashes alone do not validate actual archive entries.

The canonical writer sorts map keys and files by UTF-8 bytes, set arrays by element order, and preserves semantic sources order. Write decimal integers, no insignificant whitespace/BOM, and one final LF. Escape quote, backslash, and control characters only; use lowercase `\u00xx` for controls. Other valid JSON spellings may be read, but different manifest bytes produce different SourceIds.

For each archive writer format version, fix entry path order, timestamp (1980-01-01 00:00:00), attributes/extra fields, and compression method/settings/implementation. Exclude host-specific metadata. Record writer version and whole-archive SHA-256 separately from SourceId. A trusted validated copy or current verified writer output can supply the expected archive hash. If a sequential hash of an external archive's actual snapshot matches it, skip decompression; otherwise validate all entries. A different valid compression of the same SourceId is reusable. An external hash claim or writer-version label alone is not evidence. Use the checked snapshot afterward.

### 18.6.4. User cache and publication stores

On cache registration, validate the complete external archive and copy the same snapshot. The initial user-scoped cache defaults on Windows to `%LOCALAPPDATA%/Kimigayo/Cache/v1`. Store immutable content by IDs and do not keep an ID/version release table. Integrity records and semantic verification records have separate roles.

Skipping repeated cache checks assumes that only the compiler writes registered content; it does not promise automatic detection of outside changes. Structural read errors indicate corruption. `store verify` checks all stored formats, actual hashes, and references. Quarantine corrupt content, invalidate dependent results, and require reacquisition/reverification. External pack/publication directories are outside this trust boundary; read-only attributes and manifest hashes alone do not establish cache integrity.

Only `publish <package> --store <directory>` changes a publication store's release table. It reads the fixed root Package and SourceId-named closure from the same input directory, checks integrity/manifests/graph, and does not repack live Projects or claim new semantic/native verification. Initially the destination is an explicitly selected local directory, with no network upload.

`releases.json` contains schemaVersion 1 and an entries array of packageId/packageVersion/sourceId records, sorted by ID then version in UTF-8 order. Reject duplicate ID/version pairs. Within that store, an existing release maps permanently to one SourceId; repeating the same mapping succeeds. Other stores and unrelated projects do not share this reservation.

Before updating the table, inspect the entire closure and report every same-release content conflict, old/new SourceIds, and dependency paths. Published ancestors whose content changes also require new versions. Do not edit versions/settings automatically, and update no mappings if conflicts exist. Place complete content first, recheck mappings under the destination lock, and atomically replace the table as one transaction. Readers use one table snapshot. Interruption must not expose mappings to incomplete closures; valid unreferenced content need not be rolled back. A corrupt table is an error, not an empty store. Retain published mappings even if their content is later removed.

## 18.7. Verified information and reuse

### 18.7.1. Distinct validity checks

| Check | Required facts |
| --- | --- |
| Input/distribution identity | SourceId or ProjectSnapshotId, resolved graph, effective environment |
| Semantic validity | Declaration content, premises, effects, conformance, selections, absence dependencies, verification rules |
| Code connection | Target/layout/ABI, Kimi/runtime, actual native supply and symbols |

Version equality, equal Type sizes, or LLVM verification cannot replace another check. Unknown, Error, missing implementation, and unfinished validation are never success evidence.

ModuleInputId includes raw input identity, effective language, compiler build, verification rules, Kimi, target/layout/semantic profile, mode, settings, direct-reference mappings, and dependency ModuleInputIds. It is the whole-module fast path, not a declaration identity or a generation-order seed.

### 18.7.2. Correspondence and semantic records

Match old/new graphs in two stages. Within the same project history, use a dedicated root correspondence key and PackageId for dependencies. Ambiguity, including several versions of one ID, disables this matching-based reuse. The root key does not identify unrelated Applications. Within matched nodes, DeclarationKey comprises Container, declaration kind, and normalized declaration identifier, excluding version, content hash, and position.

These keys find candidates only. Actual Type and Symbol identities retain the defining version. Compare all content/environment facts read by the judgment and the current identities of its dependencies. Hashes index comparisons; they do not replace necessary structural/premise checks. Remap and validate cross-version references before reuse, otherwise reverify. Complete edit tracking is not required. Composition Entry identity is not introduced by this reuse mechanism (§13.8).

| Record group | Preserved information |
| --- | --- |
| Declarations/contracts | Defining Symbol, access/enclosing domains, public paths, open/base relations, Field identity, complete Types/Semantics/Origins, generic slots/projections, Constraints, unsafe and length conditions |
| Property/callable contracts | Stored Types, standard/custom permissions, accessor signatures/Origins, environment/capture identity, internal/public call signatures, receiver acquisition, per-call Origins, effects and returned Loan anchors |
| Conformance/public guarantees | Witness and associated-Type mappings, conditional premises, closed specialization sets, completed ObjectCallCompatible/root-operation summaries and causes, Supports relationships, Runtime Type Identity and required adjustment/destruction/release contracts |
| Semantic plans | Verified bodies, acquisition/ownership/cleanup, definition-site binding and private dependencies, legitimate representation obligations |
| Verification records | Subject, property, premises, result, content dependencies, rule identity, source references, and generation provenance |

Private information may support generation/reverification without entering consumer lookup; using a public contract must not require searching private bodies. Preserve Origins even when erased at runtime. Export ObjectCallCompatible only as completed Proven/NotProven plus causes, never pending status. Legitimate generic representation obligations retain subject, premises, dependencies, and deadline; they are not completed proofs. Each use still checks its own obligations, Loans, initialization, and cleanup.

### 18.7.3. Invalidation and persistence

Invalidate judgments that read changed content. If rechecking produces the same conclusion, stop propagation to clients depending only on that conclusion; bodies used for generation still change. Absence is a content dependency on a completed Container or selection set, not missing data. Detect additions, removals, access changes, and specialization changes. Recompute recursive effect/function components to a fixed point before hashing completed results; do not recursively expand each other's hashes or use circular unfinished proofs. This permits ordinary recursion without relaxing the module DAG.

Module-wide invalidation is an acceptable initial implementation. Reuse fine-grained facts only where correspondence and validation are implemented; never combine stale proofs and new mappings. Diagnose lost requirements at the dependent use instead of requiring upstream knowledge of every downstream consumer.

Initial persistent semantic/generation caches stop at compiler-owned verified semantic plans, with matching cache format, compiler build, and verification rules. Regenerate ABI/context/frame/budget plans, IR, and machine code. Generation-only settings need not invalidate semantic plans. Input-integrity records and native summaries (§20.8.2) may be stored separately. A package's own claim to be verified is untrusted; hashes prove neither semantic correctness nor publisher identity. Do not serialize Koto object graphs, host pointers, or runtime context graphs as portable interfaces.

Discard old/corrupt local caches and reverify source; missing inputs require rebuilding. Do not hide package corruption behind an old successful result. Without a cache, the compiler must verify the same semantics.

### 18.7.4. Observable source information

Raw content IDs reflect source edits. Fine-grained comparisons may omit only information unobserved by the consuming compiler judgment, Mod, and generated artifact. Product token/indentation comparisons preserve literal contents, meaningful line breaks, membership, and source order, plus all binding/environment dependencies.

If Mods can observe comments/positions, compare their module input as raw bytes and rerun on change until complete observation tracking exists. Token equality alone cannot reuse their output. After regeneration, unchanged semantic conclusions can still stop downstream invalidation.

Plans use token references within logical files/declarations; current-source mappings provide lines/columns without letting added test tokens shift product references. Run required lexical/syntax checks on current input and rebuild uncertain mappings. Bind Abort locations and Testing SiteId expression strings to current source during generation. Changed observable displays update the relevant generated artifacts, diagnostic tables, and artifact IDs, even when meaning is reused.

## 18.8. Product and test inputs

`TestDependencies` has the Dependencies shape. `TestSources` explicitly lists project-relative files, with no globs; malformed paths and duplicates are configuration errors. A file also found by normal discovery is classified as test-only, not added twice. Changing membership changes product input. Only test processing checks existence/reads test-only files; ordinary builds do not require test dependencies.

```text
TestSources=
  "tests/MeasureTests.kimi"
```

```kimi
// tests/MeasureTests.kimi
group MeasureTests
    #Test
    func doubled()
        $expect(Measure.doubled(3) == 6)
```

Tests may use the fixed product declarations/dependencies but cannot modify them, reassign a reference name, or change a same-release input. Merge identical dependencies; dependencies' own tests and TestDependencies do not propagate. Test-only inputs are excluded from product meaning. Inline `#Test` edits may change raw hashes and require reparsing, but not product meaning, layout, sharing, or budget choices; update changed locations/provenance. Test generation and separate budgets follow [§21.3.7](21-layout-runtime-and-code-generation.md#2137-product-and-test-generation).

Declarations in TestSources and Test functions in normal sources are test-only; internal declarations inherit their enclosing declaration's membership. Other declarations in normal sources remain product declarations, including unmarked helpers. Directory/file names such as tests have no special meaning. Fix product lookup, overloads, Types, layout, conformance, specialization, initialization and destruction from product inputs alone. A test instantiation of a product generic retains product definition-time lookup and ordinary instantiation rules.

Tests may add functions to a product Container without adding product candidates; existing duplicate-declaration and accessibility rules still apply. They must not change a product Type's Fields, bases, conformance or other fixed meaning. Test-only Types use ordinary declaration rules. Generated declarations carry the same membership: product generation cannot consume test-only inputs; generated Test functions are test-only, and test generation cannot modify the fixed product result.

Use one fixed set of verified inputs/settings and one all-case test artifact. Revalidate affected product plans before adding test generation requests when those inputs change; filters and execution order do not alter compilation inputs. The test host uses dedicated startup (§22.6.1). Entry/Provider selection and product/TestSources composition Bindings are not defined by test membership; those Composition Root extensions remain unsettled (§13.8).
