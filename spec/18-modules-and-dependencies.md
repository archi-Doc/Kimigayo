# 18. Modules and dependencies

[Specification index](../SPEC.md)

A module groups source declarations and controls the names exposed through dependencies. Source environments belong to definitions, not to their callers or to merged-Container wrappers.

| Term | Meaning |
| --- | --- |
| Kotonoha | One named module; initial external distribution uses source packages. |
| SourceDocument | One immutable source input belonging to a Kotonoha. |
| Compilation root | The lookup entry point for the project root and the direct-dependency reference names. |
| Project root | The root of the primary Kotonoha's declaration hierarchy. |
| Source environment | A document's definition-site aliases and lookup context. |

Each Compilation owns a **Compilation root**, direct-dependency reference-name mappings and default aliases. The primary Kotonoha's Container hierarchy ends at the **project root**. A resolved Symbol identifies its declaration, including its originating Kotonoha and version, independently of spelling or alias path.

Merged declarations keep each fragment's definition-site source environment for names, Types, Constraints and diagnostics; merging never applies one fragment's aliases to another. Generated documents have independent source environments.

## 18.1. External references and aliases

Only directly referenced Kotonoha libraries are source-addressable by reference name. Use qualification, such as `ExternalLib.GroupA.StructB`, or an explicit `alias` declaration; external members are never searched unqualified. Multiple versions may use different reference names (§18.4). Loading transitive metadata for Type checking does not expose those libraries by name.

### 18.1.1. Forms and resolution

| Form | Introduction |
| --- | --- |
| `alias Path` | Opens the Container's direct members in their respective namespaces. |
| `alias Name => Path` | Introduces the target itself as a Type-namespace Qualifier named `Name`. |

Both forms precede ordinary declarations and executable items at the SourceDocument top level. Nested aliases and access modifiers on named aliases are invalid. Directive selection and generation follow Chapter 19. `Name` is one ordinary identifier, and the right side is a Container reference, not an expression or executable Body.

Named targets are limited to Kotonoha and groups. A named alias creates no Core, value, storage or declaration identity and does not open the target's members; the opening form keeps its existing Container target range. Neither form adds Container placements or ways to instantiate Containers (§6.1). Where the Container rules permit a group under an instantiated parent, a named alias may keep that reference, with all required Type and Origin arguments bound; it acquires no generic parameters of its own.

The entire target, including inside Type arguments and Origin specifications, is resolved from the Compilation root without source or default aliases. A leading `::` is optional and does not change resolution. Built-in syntax such as `i32` and `from static` remains valid. Referenced declarations' bodies and constraints are checked in their definition environments. Alias chains, cycles, source-order resolution and transitive dependency reachability are not introduced. A real root declaration or direct reference with the same spelling remains usable.

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

An alias keeps the original declaration identity and normalized argument bindings; it is neither path substitution nor use-site reinference. Access, Type formation, input-condition and Origin obligations, including those of intermediate qualifiers, are registered at the declaration. Established errors are rejected; unresolved generated information may wait only until its existing deadline, and final Binding completes every required obligation. Member access and use conditions are checked at each use; an alias supplies neither access rights nor conformance evidence.

Reference identity and validation completion are separate. Equal fixed references may share structure while obligations remain unresolved, but each path keeps its own obligations, state and dependencies; sharing and deduplication never imply that validation succeeded.

Aliases apply only to their SourceDocument. They do not propagate to other files, merged fragments, generated documents or callers, are not re-exported, and create no root member addressable as `Project.A` or `::A`. Public Signatures still require access, direct dependencies and Name Reachability for the original Types. Collision and warning rules are in §9.4.1.

An alias may keep a fully bound nested Container, as in `alias Family<i32>.Helpers` or `alias H => Family<i32>.Helpers`. Its reference is fixed at the declaration, and uses never infer arguments again. Opening aliases import only direct members, not formal parameters, Origins or `Self`. Only equal declarations with equal full bindings are deduplicated: opening `Family<i32>.Helpers` and `Family<string>.Helpers` leaves two candidates for an inner `Token`, even when `Token` is empty, and the ordinary same-name/same-arity ambiguity applies. This is Container lookup, not transparent Type-alias syntax.

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

The effective defaults combine the language version's mandatory aliases with the defining module's configured additions. This revision requires an alias that opens the reserved Kimi Kotonoha in every document, including dependency and generated sources; configuration can neither remove it nor replace its target.

Mandatory and additional aliases participate together in the default lookup stage, and identical resolved references are deduplicated within that stage. Explicit source aliases belong to the earlier explicit stage, so `alias Kimi` can change precedence; deduplication never crosses stages in a way that changes lookup order. Opening `Kimi` exposes `Console`, not its members; an additional default `Kimi.Console` exposes a bare `writeLine`.

Project configuration and manifest `aliases` store only user additions; mandatory aliases are not appended when saving. The effective environment is reconstructed from the language version, the selected Kimi contract and the saved additions, so empty additions and an addition of only `Kimi` have equal effective settings. Configuration equivalences that require no name resolution may be normalized before input hashing; general path equivalence is determined later by semantic analysis.

## 18.2. Re-exports

**Deferred design.** Re-export syntax and artifact representation are undefined, and aliases never re-export. A consumer naming an external Type or requirement must directly reference its originating Kotonoha and have an accessible Symbol path; neither a public Signature containing that Type nor transitive metadata loading supplies Name Reachability.

A future re-export design must preserve the original Symbol, avoid widening access and reject cycles without a real target. Its syntax and compatibility requirements belong to that feature's specification.

## 18.3. Source artifacts and binary interfaces

Initial distribution is **source-first**: Project references, source packages, exact versions, local publication stores and optional verified semantic caches. Each package contains one Library Kotonoha. Inspection `.ll` files and serialized Koto graphs are not distribution formats. Distribution does not change definition-site lookup, access, generic selection or ownership boundaries, and unsupported required Types, Loans, effects, cleanup or generation paths must be diagnosed.

Private-source binaries, machine-code caches, stable external Kimigayo ABIs, network registries, version ranges, dynamic loading and re-exports are deferred. A future binary format must keep the complete semantic information of §18.7 plus the corresponding code, ABI, layout, calling conventions and target identity. It must define trust, correspondence between code and proofs, recheckable evidence, and updates that cannot be rebuilt. Private generic plans may disclose implementation details even without the original source. No independent certificate or signing system is introduced.

There is no Package-to-Project override. Updating a child and republishing it to the same store can require new versions for all published ancestors whose dependency edges change. Repeated local `pack` operations do not reserve versions.

## 18.4. Dependency configuration and resolution

### 18.4.1. Identities and graph

| Identity | Purpose |
| --- | --- |
| ReferenceName | The consumer's source name for a direct dependency |
| PackageId / PackageVersion | The defining module and its exact release |
| SourceId | Exact source-package content (§18.6) |
| ProjectSnapshotId | Local product source and effective semantic settings (§18.5.2) |
| ModuleInputId | Whole-module verification inputs (§18.7.1) |
| DeclarationKey | Declaration correspondence across revisions; not semantic identity (§18.7.2) |

A PackageId starts with an ASCII lowercase letter and consists of nonempty segments of lowercase ASCII letters and digits separated by `.` or `-`. A PackageVersion starts with an ASCII letter or digit, followed by ASCII letters, digits, `.`, `-` and `+`. Versions compare by exact, case-sensitive equality; neither ordering nor compatibility is inferred. Referenced Libraries and packing roots require both fields; an Application needs no package identity merely to consume dependencies.

A ReferenceName is a normal language identifier and must not collide with project-root declarations or with the reserved `Kimi` reference name. Multiple names or paths for one dependency create no new Types or statics, and Types and Symbols from different versions remain distinct.

The module graph is a DAG. Self-reference and cycles are diagnosed with dependency paths; mutually dependent declarations may reside in one Kotonoha. Within a target graph, including its test extension, each ID/version has one input kind, content and semantic configuration. Identical nodes reached by several paths are merged, and conflicting inputs are reported with both paths. A Project is never implicitly substituted for a Package.

Each module keeps its own aliases and default aliases, access context, declarations and closed specialization set; consumers can neither append declarations nor rebind the definition environment. The compiler selects one Kimi identity and contract for the graph.

### 18.4.2. Project references and sources

`Dependencies` maps each ReferenceName to a required `PackageId` and `PackageVersion`. A record may specify `Project` (`.kimiproj`) or `Package` (`.kimipkg`), never both; omitting both requests a Package from `PackageSources`. Paths are relative to the declaring project, and the loaded identity is checked against the requested one.

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

`PackageSources` is an array of `{PackageId, PackageVersion, Package}` or `{Store}` records; a Store is a directory. For an unfixed SourceId, the explicit packages and the store release tables are candidates for the exact ID/version. Exactly one candidate SourceId is required; conflicting candidates are errors, not first-match choices. Release tables are lookup indexes, not integrity or semantic proof, so the selected manifest and bytes are validated.

```text
PackageSources=
  { Store="packages" }
Dependencies=
  Math={ PackageId="example.math" PackageVersion="1.2.0" }
```

A manifest-fixed dependency, or a command using a valid lock, accepts only the recorded SourceId and may open `<Store>/<SourceId>.kimipkg` directly. A release table claiming other content for that release is inconsistent, and the pin never changes silently. A plain pack directory without a release table can serve known SourceIds. The user cache speeds up retrieval of already selected content and never chooses a version.

Candidates include direct Package locations and the sources declared by reachable Projects. The required partition's Project configurations are collected before Packages are resolved, so load order cannot select content. Test sources cannot supply product-resolution candidates, and registering a candidate grants no source name. Directories are never scanned for arbitrary packages, version ranges are not chosen, and missing sources are not guessed. The old unimplemented `KotonohaArray` setting is replaced by `Dependencies`; a nonempty old setting receives a migration diagnostic.

### 18.4.3. Effective environment

One target and one debug/release mode apply across the graph, and every dependency must allow that target. CompileTimeSettings and default aliases belong to the defining module. Dependency-edge overrides, implicit feature unions and conditional dependency edges are not initial features; publish separate configurations under separate PackageIds.

Ordinary builds resolve an omitted LangVersion from the root's solution and compiler defaults (§20.5), without discovering another solution for a dependency. Each Project packed into a source package must declare LangVersion and Targets explicitly. Other distributed settings use only that Project's declarations or the defaults fixed by its language or artifact version; absent CompileTimeSettings mean an empty map. Explicit values are required wherever an outer environment or compiler build would otherwise supply a changing default. Resolved settings are stored in the manifest.

Initially, the graph is verified with one effective language version and the current compiler build; the publisher's compiler build need not match. Each content ID uses effective values within its scope, so equivalent omitted and explicit values compare equal. The consumer's target, mode and compiler do not enter the SourceId; they enter the ModuleInputId. Publication immutability is scoped to the destination store.

## 18.5. Lock files and input records

### 18.5.1. Resolution state

Only `restore` updates dependency locks. It resolves the product first, then the root's test extension, and saves both results together. It reads declared local sources without network access, Mod execution or source execution. A product failure gives a nonzero result; a test-only failure is reported and recorded but does not fail the product restore. An error that prevents interpretation of the whole configuration cannot be isolated as a test-only failure.

Restore re-resolves Project-declared Package references from the current sources, so changing an explicit trial Package path and restoring may pin different content under the same version. It never rewrites a Package manifest's pinned children. Other commands do not update locks; command responsibilities are listed in §20.8.6.

For `Name.kimiproj`, the lock is `Name.kimi.lock.json`, schemaVersion 1. Each partition records its resolved or unresolved status and a reason code, the path-free dependency structure, each node's ID/version/input kind, reference-name edges and Package SourceIds. The root is identified by a dedicated node. Detailed diagnostics containing host paths belong in the processing record, not the lock.

The root and Project sources and settings remain live. Changes to IDs, versions, reference names, edges, input kinds or pinned Package content require a restore. Source edits and other settings require the appropriate revalidation, not a lock rewrite. Locations are recomputed from the current project settings, so moving identical inputs requires no restore. Project snapshots and source bytes do not belong in the lock.

When a lock exists, the command's required partitions are always validated, even if the current dependency declarations are empty. Only a missing lock with empty required dependencies denotes an implicit empty resolution, and a corrupt lock is never treated as empty. Product commands require only the product partition; `test` requires both. `--locked` explicitly requests failure when a restore is needed; ordinary input commands already perform that check and never restore automatically. `--locked` does not prohibit Project source edits.

Restore builds both partitions from one configuration snapshot and keeps no old test partition; a product failure also makes the test partition unresolved. `test` checks the success and current structure of both partitions, with no separate product-generation digest. Under a write lock, the prior lock content is compared again before atomic replacement, and a concurrent change is a failure. Serialization is deterministic, and an unchanged result is not rewritten. A failed resolution may be recorded as unresolved, never as an earlier success.

```powershell
kimi restore Geometry.kimiproj
kimi check Geometry.kimiproj --locked
# Editing Math's body needs rechecking, but no restore or lock update.
kimi check Geometry.kimiproj --locked
```

### 18.5.2. Immutable processing records

Each input byte snapshot is fixed, and the same bytes are used for verification, generation and packing; a mutable path is never reread after it has been checked.

**ProjectSnapshotId** covers the logical product-source paths, order and bytes and the normalized effective semantic settings, including reference names and dependency IDs/versions. It reflects source membership, not raw project-file formatting. It excludes test-only files and dependencies, retrieval and output paths, and generation-only optimization settings, which are recorded in their own stages. Ordinary files containing `#Test` keep their raw bytes. Supported Mod registrations, implementations and additional inputs are included under §20.7.5. A ProjectSnapshotId is not a SourceId: a SourceId reflects the actual manifest bytes and content-hashed files, so different saved bytes remain different content even when the effective defaults are equal (§18.1.3).

Stage input records keep their language, compiler and verification versions and the Kimi contract. Input IDs do not require full alias resolution. Semantic reuse keeps resolved references, lookup stages, definition environments, verification assumptions and dependencies, including candidate absence and access changes. Alias names and paths do not enter the underlying declaration or Type identity, and a rename alone never makes old inputs compatible.

A build input record covers the root and every actual input, the effective environment, resolved edges, verification rules and the native inputs needed at that stage. Unresolved native inputs and incomplete work are marked explicitly. Each output is bound to its input record and output hash. Lock equality alone fixes neither the root source, native files, toolchain nor runtime environment.

Immutable records are stored by content ID, with IDs, hashes and the necessary structure and settings rather than duplicated input byte arrays. The transitive content closure is kept for records whose reproducibility must be preserved. Latest pointers, explicitly retained records and locks, and active processing pins are collection roots; pins are acquired before use and coordinated with collection, and unreferenced records and content may be reclaimed. Hashes cannot reconstruct missing bytes. Pack directories and publication stores are not collected automatically as user caches, and published release mappings survive content removal.

## 18.6. Source package format and publication

### 18.6.1. Packing a fixed graph

`pack` accepts a Library Project and performs these steps:

1. Validate the product lock and the required explicit settings, reporting missing settings across reachable Projects, and fix the current root and Project inputs.
2. Convert Project dependencies bottom-up to distribution inputs and compute their SourceIds; validate existing Packages for reuse.
3. Pin parent edges to the child ID/version/SourceId and check uniqueness in the resulting graph.
4. Verify that final graph with the normal package loader and semantic verifier.
5. Store the complete source-package closure, including existing Packages, children before parents, and report all content IDs and locations.

Product membership, logical paths and order, effective settings, user-added default aliases (§18.1.3) and native requirements are preserved, and host locations are removed. The original conditional source is distributed, not a rewrite with branches selected. Neither a ProjectSnapshotId nor an ID/version alone may select earlier packed content. Validation may use a logical manifest and file view before ZIP serialization, provided the writer receives the same verified bytes.

The default output directory is `bin/packages`; `--output <directory>` overrides it. The root and its children are named `<SourceId>.kimipkg`, and identical content is stored once. Each temporary archive is completed and then placed with an atomic no-overwrite rename; an existing or racing file is validated under §18.6.3 and reused only if its content matches. A parent becomes available only after its children are present. Trial packs may keep different SourceIds for the same version. Native files, runtime DLLs and toolchains are outside this source closure.

Default verification uses build's single-environment selection: an explicit `--Target`, otherwise the only configured target; multiple targets require a selection. Release is the default mode, and `--Debug` selects debug (Boolean-value spelling: `--Debug true`). `--verify-all` verifies every declared target in both modes, conflicts with target and debug selection, and fails on unsupported environments or any failed check. Outputs are not finalized after a failure. Semantic verification requires neither code generation nor native linking or execution.

Manifest targets state the allowed use environments, not successful verification. Successful combinations are recorded separately with the SourceId, compiler build and effective settings, and unverified combinations are not successes; consumers verify in their own environment. Initial packing excludes inputs that require Mods, so generated output cannot masquerade as original source. A future extension must preserve the registration, implementation, API, input, target, order and provenance contract of §20.7.5.

```powershell
# Set LangVersion and Targets in Geometry and each Project dependency.
kimi pack Geometry.kimiproj --output trial --verify-all
# Trial edits may keep the same version.
kimi pack Geometry.kimiproj --output trial --verify-all
# Replace <SourceId> with the root ID reported by pack.
kimi publish "trial/<SourceId>.kimipkg" --store packages
```

### 18.6.2. Manifest and logical files

A `.kimipkg` is a ZIP of regular files with a root UTF-8 `manifest.json`, whose schemaVersion is 1 and kind is `source`. JSON keys are lowerCamelCase; project settings are PascalCase.

| Required key | Meaning |
| --- | --- |
| `schemaVersion`, `kind` | Format version and artifact kind |
| `packageId`, `packageVersion`, `langVersion` | Defining identity and resolved language version |
| `targets` | Set of allowed targets |
| `compileTimeSettings`, `aliases` | Definition settings and user-added default aliases; mandatory defaults are reconstructed (§18.1.3) |
| `files` | Every regular entry except the manifest itself: path, size, sha256 |
| `sources` | Ordered product-source paths |
| `dependencies` | Map from ReferenceName to packageId/packageVersion/sourceId |
| `requiredFeatures` | Set of required compiler-catalog features |
| `nativeRequirements` | Per-target native requirements (§20.8.2) |

Empty maps and arrays are kept. Each `compileTimeSettings` value contains exactly one `bool`, integer or string alternative. `sources` entries are unique and refer to `files` entries, and `files` entries and non-manifest archive entries correspond one-to-one. Test-only files and dependencies are excluded; inline `#Test` source is kept unchanged and excluded by the membership rules, not by source rewriting.

Logical paths are `/`-separated relative names. Empty components, `.`, `..`, absolute paths, drives, NUL, backslashes, link entries and duplicates are rejected. Paths must be NFC under Unicode 15.0.0, matching §2.5, and are never silently normalized. Collisions are detected by default simple case folding (C and S mappings, not F or T), then NFC, using the same Unicode release, and the original NFC spelling is hashed. This path rule does not change case-sensitive language Name lookup. Host extraction is optional; if performed, host-reserved or aliasing paths are rejected rather than overwriting another file. Source newlines and Unicode bytes are preserved.

Unknown schemas, keys and required features, duplicate keys, invalid references, out-of-range integers, missing or extra entries, and size or hash mismatches are rejected. Entry counts, expanded bytes and string lengths are bounded, and malformed input is distinguished from resource exhaustion.

### 18.6.3. Content identity and integrity

The SourceId is the lowercase, 64-digit hexadecimal SHA-256 of:

```text
ASCII "Kimigayo.Source.v1" + NUL + exact manifest.json bytes
```

The manifest excludes its own SourceId and commits to all other files through their paths, lengths and hashes. Compression and ZIP timestamps do not define the SourceId, and matching manifest hashes alone do not validate the actual archive entries.

The canonical writer sorts map keys and files by UTF-8 bytes, sorts set arrays by element order and preserves the semantic `sources` order. It writes decimal integers, no insignificant whitespace or BOM, and one final LF, and escapes only the quotation mark, backslash and control characters, using lowercase `\u00xx` for controls. Other valid JSON spellings may be read, but different manifest bytes produce different SourceIds.

For each archive-writer format version, the entry path order, timestamp (1980-01-01 00:00:00), attributes and extra fields, and compression method, settings and implementation are fixed, and host-specific metadata is excluded. The writer version and the whole-archive SHA-256 are recorded separately from the SourceId. A trusted validated copy or the current verified writer output can supply the expected archive hash; if a sequential hash of an external archive's actual snapshot matches it, decompression is skipped, and otherwise all entries are validated. A different valid compression of the same SourceId is reusable. An external hash claim or writer-version label alone is not evidence. The checked snapshot is used afterward.

### 18.6.4. User cache and publication stores

On cache registration, the complete external archive is validated and the same snapshot copied. On Windows, the initial user-scoped cache defaults to `%LOCALAPPDATA%/Kimigayo/Cache/v1`. It stores immutable content by ID and keeps no ID/version release table. Integrity records and semantic verification records have separate roles.

Skipping repeated cache checks assumes that only the compiler writes registered content; outside changes are not promised to be detected automatically. Structural read errors indicate corruption. `store verify` checks all stored formats, actual hashes and references. Corrupt content is quarantined, dependent results are invalidated, and reacquisition and reverification are required. External pack and publication directories are outside this trust boundary; read-only attributes and manifest hashes alone do not establish cache integrity.

Only `publish <package> --store <directory>` changes a publication store's release table. It reads the fixed root Package and the SourceId-named closure from the same input directory and checks integrity, manifests and the graph; it neither repacks live Projects nor claims new semantic or native verification. Initially the destination is an explicitly selected local directory, with no network upload.

`releases.json` contains schemaVersion 1 and an `entries` array of packageId/packageVersion/sourceId records, sorted by ID and then version in UTF-8 order; duplicate ID/version pairs are rejected. Within that store, an existing release maps permanently to one SourceId, and repeating the same mapping succeeds. Other stores and unrelated projects do not share this reservation.

Before updating the table, `publish` inspects the entire closure and reports every same-release content conflict, with old and new SourceIds and dependency paths. Published ancestors whose content changes also require new versions. Versions and settings are never edited automatically, and no mapping is updated if any conflict exists. Complete content is placed first; then the mappings are rechecked under the destination lock and the table replaced atomically as one transaction. Readers use one table snapshot. An interruption must not expose mappings to incomplete closures, but valid unreferenced content need not be rolled back. A corrupt table is an error, not an empty store. Published mappings are kept even if their content is later removed.

## 18.7. Verified information and reuse

### 18.7.1. Distinct validity checks

| Check | Required facts |
| --- | --- |
| Input/distribution identity | SourceId or ProjectSnapshotId, resolved graph, effective environment |
| Semantic validity | Declaration content, premises, effects, conformance, selections, absence dependencies, verification rules |
| Code connection | Target, layout and ABI, Kimi and runtime, actual native supply and symbols |

Version equality, equal Type sizes or LLVM verification cannot replace another check. Unknown, Error, missing implementation and unfinished validation are never evidence of success.

A **ModuleInputId** includes the raw input identity, effective language, compiler build, verification rules, Kimi, target/layout/semantic profile, mode, settings, direct-reference mappings and dependency ModuleInputIds. It is the whole-module fast path, not a declaration identity or a generation-order seed.

### 18.7.2. Correspondence and semantic records

Old and new graphs are matched in two stages. Within the same project history, a dedicated root correspondence key is used for the root and the PackageId for dependencies; ambiguity, including several versions of one ID, disables this matching-based reuse. The root key does not identify unrelated Applications. Within matched nodes, a **DeclarationKey** consists of the Container, declaration kind and normalized declaration identifier, excluding version, content hash and position.

These keys only find candidates; actual Type and Symbol identities keep the defining version. All content and environment facts read by a judgment, and the current identities of its dependencies, are compared. Hashes index comparisons but do not replace necessary structural and premise checks. Cross-version references are remapped and validated before reuse; otherwise the judgment is reverified. Complete edit tracking is not required. This reuse mechanism introduces no Composition Entry identity (§13.8).

| Record group | Preserved information |
| --- | --- |
| Declarations and contracts | Defining Symbol, access and enclosing domains, public paths, open/base relations, Field identity, complete Types/Semantics/Origins, generic slots and projections, Constraints, `unsafe` and length conditions |
| Property and callable contracts | Stored Types, standard/custom permissions, accessor signatures and Origins, environment and capture identity, internal and public call signatures, receiver acquisition, per-call Origins, effects and returned Loan anchors |
| Conformance and public guarantees | Witness and associated-Type mappings, conditional premises, closed specialization sets, completed ObjectCallCompatible and root-operation summaries with causes, Supports relationships, Runtime Type Identity, and required adjustment/destruction/release contracts |
| Semantic plans | Verified bodies, acquisition/ownership/cleanup, definition-site binding and private dependencies, legitimate representation obligations |
| Verification records | Subject, property, premises, result, content dependencies, rule identity, source references and generation provenance |

Private information may support generation and reverification without entering consumer lookup; using a public contract must not require searching private bodies. Origins are preserved even when erased at runtime. ObjectCallCompatible is exported only as a completed Proven/NotProven status with causes, never as a pending status. Legitimate generic representation obligations keep their subject, premises, dependencies and deadline; they are not completed proofs. Each use still checks its own obligations, Loans, initialization and cleanup.

Openness and the dependencies of Sealed proofs, payload projections, complete-target effects and all allowed Type/Origin/full-specialization checks are published. Positive and negative proofs are rechecked after an openness change, which is an API change, and a breaking change when it invalidates a previously valid use. Sealed alone never replaces the required ObjectCallCompatible checks.

### 18.7.3. Invalidation and persistence

Judgments that read changed content are invalidated. If rechecking produces the same conclusion, propagation stops for clients that depend only on that conclusion, although bodies used for generation still change. Absence is a content dependency on a completed Container or selection set, not missing data; additions, removals, access changes and specialization changes are detected. Recursive effect and function components are recomputed to a fixed point before completed results are hashed; they do not recursively expand each other's hashes or use circular unfinished proofs. This permits ordinary recursion without relaxing the module DAG.

Module-wide invalidation is an acceptable initial implementation. Fine-grained facts are reused only where correspondence and validation are implemented, and stale proofs are never combined with new mappings. Lost requirements are diagnosed at the dependent use rather than requiring upstream knowledge of every downstream consumer.

Initial persistent semantic and generation caches stop at compiler-owned verified semantic plans, with a matching cache format, compiler build and verification rules. ABI, context, frame and budget plans, IR and machine code are regenerated, and generation-only settings need not invalidate semantic plans. Input-integrity records and native summaries (§20.8.2) may be stored separately. A package's own claim to be verified is untrusted, and hashes prove neither semantic correctness nor publisher identity. Koto object graphs, host pointers and runtime context graphs are never serialized as portable interfaces.

Old or corrupt local caches are discarded and the source reverified; missing inputs require a rebuild. Package corruption is never hidden behind an old successful result. Without a cache, the compiler must verify the same semantics.

### 18.7.4. Observable source information

Raw content IDs reflect source edits. Fine-grained comparisons may omit only information not observed by the consuming compiler judgment, Mod or generated artifact. Product token and indentation comparisons preserve literal contents, meaningful line breaks, membership and source order, plus all binding and environment dependencies.

If Mods can observe comments (including [Documentation Comments](02-source-and-lexical-structure.md#236-tooling-and-diagnostics)) or positions, their module input is compared as raw bytes and rerun on change, until complete observation tracking exists; token equality alone cannot reuse their output. After regeneration, unchanged semantic conclusions can still stop downstream invalidation.

Plans use token references within logical files and declarations; current-source mappings provide lines and columns, without letting added test tokens shift product references. Required lexical and syntax checks run on the current input, and uncertain mappings are rebuilt. Abort locations and Testing SiteId expression strings are bound to the current source during generation. Changed observable displays update the relevant generated artifacts, diagnostic tables and artifact IDs, even when meaning is reused.

## 18.8. Product and test inputs

`TestDependencies` has the shape of `Dependencies`. `TestSources` explicitly lists project-relative files, without globs; malformed paths and duplicates are configuration errors. A file also found by normal discovery is classified as test-only and not added twice. Changing membership changes the product input. Only test processing checks for and reads test-only files; ordinary builds do not require test dependencies.

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

Tests may use the fixed product declarations and dependencies but cannot modify them, reassign a reference name or change a same-release input. Identical dependencies are merged, and dependencies' own tests and TestDependencies do not propagate. Test-only inputs are excluded from product meaning. Inline `#Test` edits may change raw hashes and require reparsing, but never product meaning, layout, sharing or budget choices; changed locations and provenance are updated. Test generation and separate budgets follow [§21.3.7](21-layout-runtime-and-code-generation.md#2137-product-and-test-generation).

Declarations in TestSources, and Test functions in normal sources, are test-only; internal declarations inherit their enclosing declaration's membership. All other declarations in normal sources are product declarations, including unmarked helpers. Directory and file names such as `tests` have no special meaning. Product lookup, overloads, Types, layout, conformance, specialization, initialization and destruction are fixed from product inputs alone. A test instantiation of a product generic keeps product definition-time lookup and the ordinary instantiation rules.

Tests may add functions to a product Container without adding product candidates; the duplicate-declaration and accessibility rules still apply. They must not change a product Type's Fields, bases, conformance or other fixed meaning. Test-only Types use the ordinary declaration rules. Generated declarations carry the same membership: product generation cannot consume test-only inputs, generated Test functions are test-only, and test generation cannot modify the fixed product result.

One fixed set of verified inputs and settings and one all-case test artifact are used. When those inputs change, affected product plans are revalidated before test generation requests are added; filters and execution order do not alter compilation inputs. The test host uses dedicated startup (§22.6.1). Entry/Provider selection and product/TestSources composition Bindings are not defined by test membership; those Composition Root extensions remain unsettled (§13.8).
