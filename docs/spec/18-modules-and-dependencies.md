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

Source can name only directly referenced Kotonoha libraries, by their reference names. Reach external members by qualification, such as `ExternalLib.GroupA.StructB`, or through an explicit `alias` declaration; they are never found unqualified. Different versions may use different reference names (§18.4). Loading transitive metadata for Type checking does not make those libraries nameable.

### 18.1.1. Forms and resolution

| Form | Introduction |
| --- | --- |
| `alias Path` | Opens the Container's direct members in their respective namespaces. |
| `alias Name => Path` | Introduces the target itself as a Type-namespace Qualifier named `Name`. |

Both forms appear at the SourceDocument top level, before ordinary declarations and executable items. Nested aliases and access modifiers on named aliases are invalid. Directive selection and generation follow Chapter 19. `Name` is one ordinary identifier. The right side is a Container reference, not an expression or executable Body.

A named alias may target only a Kotonoha or a group. It creates no Core, value, storage or declaration identity and does not open the target's members. The opening form keeps its existing Container target range. Neither form adds Container placements or ways to instantiate Containers (§6.1). Where the Container rules permit a group under an instantiated parent, a named alias may refer to that group with all required Type and Origin arguments bound; the alias has no generic parameters of its own.

The whole target, including its Type arguments and Origin specifications, resolves from the Compilation root without source or default aliases. A leading `::` is optional and does not change resolution. Built-in syntax such as `i32` and `during static` remains valid. The bodies and constraints of referenced declarations are checked in their definition environments. There are no alias chains, cycles, source-order resolution or transitive dependency reachability. A real root declaration or direct reference with the same spelling remains usable.

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

An alias keeps the original declaration identity and normalized argument bindings. It is neither path substitution nor use-site reinference: its reference is fixed at the declaration. The alias declaration registers access, Type-formation, input-condition and Origin obligations, including those of intermediate qualifiers. Established errors are rejected. Unresolved generated information may wait only until its existing deadline, and final Binding completes every required obligation. Member access and use conditions are checked at each use; an alias supplies neither access rights nor conformance evidence.

Reference identity and validation completion are separate. Equal fixed references may share structure while obligations remain unresolved, but each path keeps its own obligations, state and dependencies. Sharing and deduplication never imply that validation succeeded.

An alias applies only to its own SourceDocument. It does not propagate to other files, merged fragments, generated documents or callers, is not re-exported, and creates no root member addressable as `Project.A` or `::A`. Public Signatures still require access, direct dependencies and Name Reachability for the original Types. Collision and warning rules are in §9.4.1.

An alias may refer to a fully bound nested Container, as in `alias Family<i32>.Helpers` or `alias H => Family<i32>.Helpers`. An opening alias imports only direct members, not formal parameters, Origins or `Self`. Deduplication requires equal declarations with equal full bindings: opening both `Family<i32>.Helpers` and `Family<string>.Helpers` leaves two candidates for an inner `Token`, even an empty one, and the ordinary same-name, same-arity ambiguity applies. This is Container lookup, not transparent Type-alias syntax.

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

The effective default aliases combine the language version's mandatory aliases with the defining module's configured additions. This revision requires an alias that opens the reserved Kimi Kotonoha in every document, including dependency and generated sources. Configuration can neither remove it nor replace its target.

Mandatory and additional aliases form the default lookup stage together, and identical resolved references are deduplicated within that stage. Explicit source aliases belong to the earlier explicit stage, so a source `alias Kimi` can change precedence. Deduplication never crosses stages in a way that changes lookup order. Opening `Kimi` exposes `Console`, not its members; an additional default `Kimi.Console` exposes a bare `writeLine`.

Project configuration and the manifest `aliases` store only user additions; saving never appends mandatory aliases. The effective environment is rebuilt from the language version, the selected Kimi contract and the saved additions, so empty additions and an addition of only `Kimi` have the same effective settings. Equivalences that need no name resolution may be normalized before input hashing; semantic analysis decides general path equivalence later.

## 18.2. Re-exports

**Deferred design.** Re-export syntax and artifact representation are undefined; aliases are not re-exports (§18.1.2). A consumer that names an external Type or requirement must directly reference its originating Kotonoha and have an accessible Symbol path. Neither a public Signature containing that Type nor transitive metadata loading supplies Name Reachability.

A future re-export design must preserve the original Symbol, must not widen access, and must reject cycles that lack a real target. That feature's specification will define its syntax and compatibility requirements.

## 18.3. Source artifacts and binary interfaces

Initial distribution is **source-first**: Project references, source packages, exact versions, local publication stores and optional verified semantic caches. Each package contains one Library Kotonoha. Inspection `.ll` files and serialized Koto graphs are not distribution formats. Distribution does not change definition-site lookup, access, generic selection or ownership boundaries. Unsupported required Types, Loans, effects, cleanup or generation paths must be diagnosed.

Private-source binaries, machine-code caches, stable external Kimigayo ABIs, network registries, version ranges, dynamic loading and re-exports are deferred. A future binary format must keep the complete semantic information of §18.7 plus the corresponding code, ABI, layout, calling conventions and target identity. It must define trust, the correspondence between code and proofs, recheckable evidence, and updates that cannot be rebuilt. Private generic plans may disclose implementation details even without the original source. No independent certificate or signing system is introduced.

There is no Package-to-Project override. Republishing a changed child to the same store can require new versions for the published ancestors whose content changes (§18.6.4), and `pack` reserves no version (§18.6.1).

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

A PackageId starts with a lowercase ASCII letter and consists of nonempty segments of lowercase ASCII letters and digits separated by `.` or `-`. A PackageVersion starts with an ASCII letter or digit, followed by ASCII letters, digits, `.`, `-` and `+`. Versions compare by exact, case-sensitive equality; no ordering or compatibility is inferred. Referenced Libraries and packing roots require both fields. An Application needs no package identity merely to consume dependencies.

A ReferenceName is an ordinary language identifier. It must not collide with a project-root declaration or with the reserved `Kimi` reference name. Several names or paths for one dependency create no new Types or statics, and Types and Symbols from different versions remain distinct.

The module graph is a DAG. Self-references and cycles are diagnosed with their dependency paths; mutually dependent declarations may reside in one Kotonoha. Within a target graph, including its test extension, each ID/version has one input kind, content and semantic configuration. Identical nodes reached by several paths are merged, and conflicting inputs are reported with both paths. A Project is never implicitly substituted for a Package.

Each module keeps its own aliases and default aliases, access context, declarations and closed specialization set. Consumers can neither append declarations nor rebind the definition environment. The compiler selects one Kimi identity and contract for the graph.

### 18.4.2. Project references and sources

`Dependencies` maps each ReferenceName to a required `PackageId` and `PackageVersion`. A record may specify `Project` (`.kimiproj`) or `Package` (`.kimipkg`), but not both; omitting both requests a Package from `PackageSources`. Paths are relative to the declaring project. The loaded identity is checked against the requested one.

```text
OutputKind="Library"
PackageId="example.geometry"
PackageVersion="1.0.0"
LangVersion="0.0.2"
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

`PackageSources` is an array of `{PackageId, PackageVersion, Package}` or `{Store}` records; a Store is a directory. When the SourceId is not fixed, the explicit packages and the stores' release tables supply candidates for the exact ID/version. Exactly one candidate SourceId is required. Conflicting candidates are errors; the first match is never chosen. Release tables are lookup indexes, not integrity or semantic proof, so the selected manifest and bytes are validated.

```text
PackageSources=
  { Store="packages" }
Dependencies=
  Math={ PackageId="example.math" PackageVersion="1.2.0" }
```

A manifest-fixed dependency, or a command using a valid lock, accepts only the recorded SourceId and may open `<Store>/<SourceId>.kimipkg` directly. A release table that claims other content for that release is inconsistent, and the pin never changes silently. A plain pack directory without a release table can serve known SourceIds. The user cache only speeds up retrieval of already selected content; it never chooses a version.

Candidates include direct Package locations and the sources declared by reachable Projects. The Project configurations of the required partition are collected before Packages are resolved, so load order cannot select content. Test sources cannot supply product-resolution candidates, and registering a candidate grants no source name. Directories are never scanned for arbitrary packages, version ranges are never chosen, and missing sources are never guessed. `Dependencies` replaces the old, unimplemented `KotonohaArray` setting; a nonempty old setting receives a migration diagnostic.

### 18.4.3. Effective environment

One target and one debug/release mode apply across the graph, and every dependency must allow that target. CompileTimeSettings belong to the defining module, like its aliases (§18.4.1). Dependency-edge overrides, implicit feature unions and conditional dependency edges are not initial features; publish separate configurations under separate PackageIds.

An omitted LangVersion is resolved under §20.5. Each Project packed into a source package must declare LangVersion and Targets explicitly. Its other distributed settings come only from its own declarations or from the defaults fixed by its language or artifact version; absent CompileTimeSettings mean an empty map. An explicit value is required wherever an outer environment or compiler build would otherwise supply a changing default. The manifest stores the resolved settings.

Initially, the graph is verified with its one effective language version (§20.5) and the current compiler build; the publisher's compiler build need not match. Each content ID uses the effective values within its scope, so equivalent omitted and explicit values compare equal. The consumer's target, mode and compiler enter the ModuleInputId, not the SourceId. Publication immutability is scoped to the destination store.

## 18.5. Lock files and input records

### 18.5.1. Resolution state

Only `restore` updates dependency locks; command responsibilities are listed in §20.8.6. Restore resolves the product first, then the root's test extension, and saves both results together. It reads declared local sources without network access, Mod execution or source execution. A product failure gives a nonzero result. A test-only failure is reported and recorded but does not fail the product restore. An error that prevents interpretation of the whole configuration cannot be isolated as a test-only failure.

Restore re-resolves Project-declared Package references from the current sources, so changing an explicit trial Package path and restoring may pin different content under the same version. It never rewrites the pinned children of a Package manifest.

For `Name.kimiproj`, the lock is `Name.kimi.lock.json`, schemaVersion 1. Each partition records its resolved or unresolved status with a reason code, the path-free dependency structure, each node's ID/version/input kind, the reference-name edges and the Package SourceIds. A dedicated node identifies the root. Detailed diagnostics containing host paths belong in the processing record, not the lock.

The lock does not freeze the root or Project sources and settings; they remain live. Changes to IDs, versions, reference names, edges, input kinds or pinned Package content require a restore. Source edits and other setting changes require the appropriate revalidation, not a lock rewrite. Locations are recomputed from the current project settings, so moving identical inputs requires no restore. Project snapshots and source bytes do not belong in the lock.

When a lock exists, the command's required partitions are always validated, even if the current dependency declarations are empty. Only a missing lock with empty required dependencies denotes an implicit empty resolution; a corrupt lock is never treated as empty. Product commands require only the product partition; `test` requires both. `--locked` explicitly requests failure when a restore is needed. Ordinary input commands already perform that check and never restore automatically. `--locked` does not prohibit Project source edits.

Restore builds both partitions from one configuration snapshot and keeps no old test partition; a product failure also makes the test partition unresolved. `test` checks the success and current structure of both partitions, with no separate product-generation digest. Under a write lock, the prior lock content is compared again before atomic replacement, and a concurrent change is a failure. Serialization is deterministic, and an unchanged result is not rewritten. A failed resolution may be recorded as unresolved, never as an earlier success.

```powershell
kimi restore Geometry.kimiproj
kimi check Geometry.kimiproj --locked
# Editing Math's body needs rechecking, but no restore or lock update.
kimi check Geometry.kimiproj --locked
```

### 18.5.2. Immutable processing records

Each input byte snapshot is fixed, and verification, generation and packing use the same bytes. A mutable path is never reread after it has been checked.

**ProjectSnapshotId** covers the logical product-source paths, order and bytes, and the normalized effective semantic settings, including reference names and dependency IDs/versions. It reflects source membership, not raw project-file formatting. It excludes test-only files and dependencies, retrieval and output paths, and generation-only optimization settings; their own stages record those. Ordinary files containing `#Test` keep their raw bytes. Supported Mod registrations, implementations and additional inputs are included under §20.7.5. A ProjectSnapshotId is not a SourceId: a SourceId reflects the actual manifest bytes and content-hashed files, so different saved bytes remain different content even when the effective defaults are equal (§18.1.3).

Stage input records keep their language, compiler and verification versions and the Kimi contract. Input IDs do not require full alias resolution. Semantic reuse keeps resolved references, lookup stages, definition environments, verification assumptions and dependencies, including candidate absence and access changes. Alias names and paths do not enter the underlying declaration or Type identity, and a rename alone never makes old inputs compatible.

A build input record covers the root and every actual input, the effective environment, resolved edges, verification rules and the native inputs needed at that stage. Unresolved native inputs and incomplete work are marked explicitly. Each output is bound to its input record and output hash. Lock equality alone does not fix the root source, native files, toolchain or runtime environment.

Immutable records are stored by content ID. They hold IDs, hashes and the necessary structure and settings, not duplicated input byte arrays. Records whose reproducibility must be preserved keep their transitive content closure. Latest pointers, explicitly retained records and locks, and active processing pins are collection roots. Pins are acquired before use and coordinated with collection; unreferenced records and content may be reclaimed. Hashes cannot reconstruct missing bytes. Pack directories and publication stores are not collected automatically as user caches.

## 18.6. Source package format and publication

### 18.6.1. Packing a fixed graph

`pack` accepts a Library Project and performs these steps:

1. Validate the product lock and the required explicit settings, reporting missing settings across reachable Projects, and fix the current root and Project inputs.
2. Convert Project dependencies bottom-up to distribution inputs and compute their SourceIds; validate existing Packages for reuse.
3. Pin parent edges to the child ID/version/SourceId and check uniqueness in the resulting graph.
4. Verify that final graph with the normal package loader and semantic verifier.
5. Store the complete source-package closure, including existing Packages, children before parents, and report all content IDs and locations.

Packing preserves product membership, logical paths and order, effective settings, user-added default aliases (§18.1.3) and native requirements, and removes host locations. It distributes the original conditional source, not a rewrite with branches selected. Neither a ProjectSnapshotId nor an ID/version alone may select earlier packed content. Validation may use a logical manifest and file view before ZIP serialization, provided the writer receives the same verified bytes.

The default output directory is `bin/packages`; `--output <directory>` overrides it. The root and its children are named `<SourceId>.kimipkg`, and identical content is stored once. Each temporary archive is completed and then placed with an atomic no-overwrite rename. An existing or racing file is validated under §18.6.3 and reused only if its content matches. A parent becomes available only after its children are present. Packing reserves no version: repeated trial packs may produce different SourceIds for the same version. Native files, runtime DLLs and toolchains are outside this source closure.

By default, `pack` verifies the single environment that `--Target` and `--Debug` select, as for `build` (§20.8.6). `--verify-all` verifies every declared target in both modes. It conflicts with target and debug selection, and it fails on any unsupported environment or failed check. No output is finalized after a failure. Semantic verification requires no code generation, native linking or execution.

Manifest targets state the allowed use environments, not successful verification. Successful combinations are recorded separately with the SourceId, compiler build and effective settings; unverified combinations are not successes. Consumers verify in their own environment. Initial packing excludes inputs that require Mods, so generated output cannot masquerade as original source. A future extension must preserve the registration, implementation, API, input, target, order and provenance contract of §20.7.5.

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

Empty maps and arrays are kept. Each `compileTimeSettings` value contains exactly one `bool`, integer or string alternative. `sources` entries are unique and refer to `files` entries. `files` entries correspond one-to-one with the non-manifest archive entries. Test-only files and dependencies are excluded. Inline `#Test` source is kept unchanged; the membership rules exclude it, not source rewriting.

Logical paths are `/`-separated relative names. Empty components, `.`, `..`, absolute paths, drives, NUL, backslashes, link entries and duplicates are rejected. Paths must be NFC under Unicode 15.0.0, matching §2.5, and are never silently normalized. Collisions are detected by default simple case folding (C and S mappings, not F or T) followed by NFC, using the same Unicode release; the original NFC spelling is hashed. This path rule does not change case-sensitive language Name lookup. Host extraction is optional. If performed, it rejects host-reserved or aliasing paths rather than overwriting another file. Source newlines and Unicode bytes are preserved.

Unknown schemas, keys and required features, duplicate keys, invalid references, out-of-range integers, missing or extra entries, and size or hash mismatches are rejected. Entry counts, expanded bytes and string lengths are bounded. Malformed input is distinguished from resource exhaustion.

### 18.6.3. Content identity and integrity

The SourceId is the lowercase, 64-digit hexadecimal SHA-256 of:

```text
ASCII "Kimigayo.Source.v1" + NUL + exact manifest.json bytes
```

The manifest excludes its own SourceId and commits to all other files through their paths, lengths and hashes. Compression and ZIP timestamps do not define the SourceId. Matching manifest hashes alone do not validate the actual archive entries.

The canonical writer sorts map keys and files by UTF-8 bytes, sorts set arrays by element order and preserves the semantic `sources` order. It writes decimal integers, no insignificant whitespace, no BOM and one final LF. It escapes only the quotation mark, backslash and control characters, using lowercase `\u00xx` for controls. Readers may accept other valid JSON spellings, but different manifest bytes produce different SourceIds.

Each archive-writer format version fixes the entry path order, the timestamp (1980-01-01 00:00:00), attributes and extra fields, and the compression method, settings and implementation; host-specific metadata is excluded. The writer version and the whole-archive SHA-256 are recorded separately from the SourceId. A trusted validated copy or the current verified writer output can supply the expected archive hash. If a sequential hash of an external archive's actual snapshot matches it, decompression is skipped; otherwise all entries are validated. A different valid compression of the same SourceId is reusable. An external hash claim or writer-version label alone is not evidence. The checked snapshot is used afterward.

### 18.6.4. User cache and publication stores

Cache registration validates the complete external archive and copies that same snapshot. On Windows, the initial user-scoped cache defaults to `%LOCALAPPDATA%/Kimigayo/Cache/v1`. It stores immutable content by ID and keeps no ID/version release table. Integrity records and semantic verification records have separate roles.

Skipping repeated cache checks assumes that only the compiler writes registered content; outside changes are not guaranteed to be detected automatically. Structural read errors indicate corruption. `store verify` checks all stored formats, actual hashes and references. Corrupt content is quarantined, dependent results are invalidated, and the content must be reacquired and reverified. External pack and publication directories are outside this trust boundary. Read-only attributes and manifest hashes alone do not establish cache integrity.

Only `publish <package> --store <directory>` changes a publication store's release table. It reads the fixed root Package and the SourceId-named closure from the same input directory and checks integrity, manifests and the graph. It neither repacks live Projects nor claims new semantic or native verification. Initially the destination is an explicitly selected local directory; there is no network upload.

`releases.json` contains schemaVersion 1 and an `entries` array of packageId/packageVersion/sourceId records, sorted by ID and then version in UTF-8 order; duplicate ID/version pairs are rejected. Within that store, an existing release maps permanently to one SourceId, and repeating the same mapping succeeds. Other stores and unrelated projects do not share this reservation.

Before updating the table, `publish` inspects the entire closure and reports every same-release content conflict with its old and new SourceIds and dependency paths. Published ancestors whose content changes also require new versions. Versions and settings are never edited automatically, and no mapping is updated if any conflict exists. Complete content is placed first. Then the mappings are rechecked under the destination lock, and the table is replaced atomically as one transaction. Readers use one table snapshot. An interruption must not expose mappings to incomplete closures, but valid unreferenced content need not be rolled back. A corrupt table is an error, not an empty store. Published mappings are kept even if their content is later removed.

## 18.7. Verified information and reuse

### 18.7.1. Distinct validity checks

| Check | Required facts |
| --- | --- |
| Input/distribution identity | SourceId or ProjectSnapshotId, resolved graph, effective environment |
| Semantic validity | Declaration content, premises, effects, conformance, selections, absence dependencies, verification rules |
| Code connection | Target, layout and ABI, Kimi and runtime, actual native supply and symbols |

Version equality, equal Type sizes and LLVM verification cannot substitute for another check. Unknown, Error, a missing implementation and unfinished validation are never evidence of success.

A **ModuleInputId** includes the raw input identity, effective language, compiler build, verification rules, Kimi, target/layout/semantic profile, mode, settings, direct-reference mappings and dependency ModuleInputIds. It is the whole-module fast path, not a declaration identity or a generation-order seed.

### 18.7.2. Correspondence and semantic records

Old and new graphs are matched in two stages. First, within the same project history, the root is matched by a dedicated root correspondence key and dependencies by PackageId. Ambiguity, including several versions of one ID, disables this matching-based reuse. The root key does not identify unrelated Applications. Second, within matched nodes, a **DeclarationKey** consists of the Container, declaration kind and normalized declaration identifier, excluding version, content hash and position.

These keys only find candidates; actual Type and Symbol identities keep the defining version. Reuse compares all content and environment facts read by a judgment, and the current identities of its dependencies. Hashes index comparisons but do not replace necessary structural and premise checks. Cross-version references are remapped and validated before reuse; otherwise the judgment is reverified. Complete edit tracking is not required. This reuse mechanism introduces no Composition Entry identity (§13.8).

| Record group | Preserved information |
| --- | --- |
| Declarations and contracts | Defining Symbol, access and enclosing domains, public paths, open/base relations, Field identity, complete Types/Semantics/Origins, generic slots and projections, Constraints, `unsafe` and length conditions |
| Property and callable contracts | Stored Types, standard/custom permissions, accessor signatures and Origins, environment and capture identity, internal and public call signatures, receiver acquisition, per-call Origins, effects and returned Loan anchors |
| Conformance and public guarantees | Witness and associated-Type mappings, conditional premises, closed specialization sets, completed ObjectCallCompatible and root-operation summaries with causes, Supports relationships, Runtime Type Identity, and required adjustment/destruction/release contracts |
| Semantic plans | Verified bodies, acquisition/ownership/cleanup, definition-site binding and private dependencies, legitimate representation obligations |
| Verification records | Subject, property, premises, result, content dependencies, rule identity, source references and generation provenance |

Private information may support generation and reverification without entering consumer lookup. Using a public contract must not require searching private bodies. Origins are preserved even when erased at runtime. ObjectCallCompatible is exported only as a completed Proven/NotProven status with causes, never as a pending status. Legitimate generic representation obligations keep their subject, premises, dependencies and deadline; they are not completed proofs. Each use still checks its own obligations, Loans, initialization and cleanup.

Openness is published, together with the dependencies of Sealed proofs, payload follows, complete-target effects and all allowed Type/Origin/full-specialization checks. An openness change is an API change, and a breaking change when it invalidates a previously valid use; positive and negative proofs are rechecked after it. Sealed alone never replaces the required ObjectCallCompatible checks.

### 18.7.3. Invalidation and persistence

Judgments that read changed content are invalidated. If rechecking reaches the same conclusion, propagation stops for clients that depend only on that conclusion, although bodies used for generation still change. Absence is a content dependency on a completed Container or selection set, not missing data, so additions, removals, access changes and specialization changes are detected. Recursive effect and function components are recomputed to a fixed point before completed results are hashed. They do not recursively expand each other's hashes or use circular unfinished proofs. This permits ordinary recursion without relaxing the module DAG.

Module-wide invalidation is an acceptable initial implementation. Fine-grained facts are reused only where correspondence and validation are implemented, and stale proofs are never combined with new mappings. Lost requirements are diagnosed at the dependent use; upstream modules need not know every downstream consumer.

Initial persistent semantic and generation caches stop at compiler-owned verified semantic plans and require a matching cache format, compiler build and verification rules. ABI, context, frame and budget plans, IR and machine code are regenerated; generation-only settings need not invalidate semantic plans. Input-integrity records and native summaries (§20.8.2) may be stored separately. A package's own claim to be verified is untrusted, and hashes prove neither semantic correctness nor publisher identity. Koto object graphs, host pointers and runtime context graphs are never serialized as portable interfaces.

Old or corrupt local caches are discarded and the source is reverified; missing inputs require a rebuild. An old successful result never hides package corruption. Without a cache, the compiler must verify the same semantics.

### 18.7.4. Observable source information

Raw content IDs reflect source edits. Fine-grained comparisons may omit only information that the consuming compiler judgment, Mod or generated artifact does not observe. Product token and indentation comparisons preserve literal contents, meaningful line breaks, membership and source order, plus all binding and environment dependencies.

If Mods can observe comments (including [Documentation Comments](02-source-and-lexical-structure.md#236-tooling-and-diagnostics)) or positions, their module input is compared as raw bytes, and they rerun on any change until complete observation tracking exists. Token equality alone cannot reuse their output. After regeneration, unchanged semantic conclusions can still stop downstream invalidation.

Plans use token references within logical files and declarations. Current-source mappings provide lines and columns, and added test tokens do not shift product references. Required lexical and syntax checks run on the current input, and uncertain mappings are rebuilt. Abort locations and Testing SiteId expression strings are bound to the current source during generation. Changed observable displays update the relevant generated artifacts, diagnostic tables and artifact IDs, even when meaning is reused.

## 18.8. Product and test inputs

`TestDependencies` has the shape of `Dependencies`. `TestSources` explicitly lists project-relative files, without globs; malformed paths and duplicates are configuration errors. A listed file that normal discovery also finds is test-only and is not added twice. Changing membership changes the product input. Only test processing checks for and reads test-only files; ordinary builds do not require test dependencies.

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

Tests may use the fixed product declarations and dependencies but cannot modify them, reassign a reference name or change a same-release input. Identical dependencies are merged. Dependencies' own tests and TestDependencies do not propagate. Inline `#Test` edits may change raw hashes and require reparsing, but never change product meaning, layout, sharing or budget choices; changed locations and provenance are updated. Test generation and its separate budgets follow [§21.3.7](../impl/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation).

Declarations in TestSources, and Test functions in normal sources, are test-only. Internal declarations inherit the membership of their enclosing declaration. All other declarations in normal sources, including unmarked helpers, are product declarations. Directory and file names such as `tests` have no special meaning. Product lookup, overloads, Types, layout, conformance, specialization, initialization and destruction are fixed from product inputs alone. A test instantiation of a product generic keeps product definition-time lookup and the ordinary instantiation rules.

Tests may add functions to a product Container without adding product candidates; the duplicate-declaration and accessibility rules still apply. Tests must not change a product Type's Fields, bases, conformance or other fixed meaning. Test-only Types follow the ordinary declaration rules. Generated declarations carry the same membership: product generation cannot consume test-only inputs, generated Test functions are test-only, and test generation cannot modify the fixed product result.

Testing uses one fixed set of verified inputs and settings and one all-case test artifact (§20.9). When those inputs change, affected product plans are revalidated before test generation requests are added. Filters and execution order do not alter compilation inputs. The test host uses dedicated startup (§22.6.1). Test membership does not define Entry/Provider selection or product/TestSources composition Bindings; those Composition Root extensions remain unsettled (§13.8).
