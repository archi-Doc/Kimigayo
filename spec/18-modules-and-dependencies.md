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

`alias ExternalLib.GroupA` opens a Container's direct members for unqualified lookup:

- Declare it at top level before ordinary declarations or executable code; it applies only to that SourceDocument. Nested aliases are invalid.
- Resolve its Container path from the Compilation root, without other source aliases or default aliases. Check target accessibility at the declaration.
- Introduce direct Types, functions, Fields, computed members, and child Containers in their namespaces; do not recursively introduce descendants. Conditional-member use still requires its published premises.
- Retain a reference to the target Container. Check member access at each actual use, rather than caching one source-wide list of accessible members.
- Treat explicit aliases together at their lookup stage and defaults together at a later stage. Order is irrelevant; deduplicate paths to the same Symbol. Different same-name functions form one candidate set, distinct Types use Type Name Selection, and mixed value kinds conflict.
- Do not automatically re-export source aliases to other files or consumers.

```kimi
// A.kimi; GroupA exports StructB and Child.StructC.
alias ExternalLib.GroupA
group Work
    func accept(value: StructB) -> () => ()
    func nested(value: Child.StructC) -> () => ()

// B.kimi: merged Work does not inherit A.kimi's alias.
group Work
    func reject(value: StructB) -> () => () // Error: not imported here.
```

Import `ExternalLib.GroupA.Child` explicitly to use `StructC` alone; `alias Child` cannot resolve through another alias. Library reference-name configuration is distinct from source `alias`.

**Type-alias boundary.** Source alias only opens a Container; it neither renames Types nor accepts `alias Name = Type`. Imported Names retain complete Types and Symbol Identity; use qualification to avoid conflicts. Alias expansion/equivalence elsewhere applies to internal transparent references and constrains any future Type-alias feature, without adding a source binding kind or alias-cycle checker. Existing reference/dependency cycle checks still apply.

Dependency configuration and graph diagnostics follow §18.4. Re-export syntax remains deferred under [Re-exports](#182-re-exports).

## 18.2. Re-exports

**Deferred design:** re-export syntax and artifact representation are undefined; aliases never re-export. Consumers naming an external Type/requirement must directly reference its originating Kotonoha and have an accessible Symbol path. Neither a public Signature containing that Type nor transitive metadata loading supplies Name Reachability.

Any future re-export design must preserve the original Symbol, avoid widening access, and reject cycles without a real target. Its syntax and compatibility requirements belong to that feature's specification.

## 18.3. Source artifacts and binary interfaces

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

ReferenceName is a normal language identifier and must not collide with project-root declarations or reserved Core reference names. Multiple names/paths for one dependency do not create new Types or statics. Types and Symbols from different versions remain distinct.

The module graph is a DAG. Diagnose self-reference and cycles with dependency paths; mutually dependent declarations may reside in one Kotonoha. Within a target graph, including its test extension, each ID/version has one input kind, content, and semantic configuration. Merge identical nodes reached by several paths; report both paths for conflicting inputs. Do not implicitly substitute a Project for a Package.

Each module retains its own aliases/default aliases, access context, declarations, and closed specialization set. Consumers cannot append declarations or rebind the definition environment. The compiler selects one Core identity and contract for the graph.

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

Fix each input byte snapshot and use those same bytes for verification, generation, and packing; do not re-read a mutable path after checking it. ProjectSnapshotId covers logical product-source paths/order/bytes and normalized effective semantic settings, including reference names and dependency IDs/versions. It reflects source membership rather than raw project-file formatting. Exclude test-only files/dependencies, retrieval/output paths, and generation-only optimization settings; record those in their appropriate stages. Ordinary files containing `#Test` retain their raw bytes. Include supported Mod registrations, implementations, and additional inputs under §20.7.5. ProjectSnapshotId is not SourceId.

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

Preserve product membership, logical paths/order, effective settings/default aliases, and native requirements; remove host locations. Distribute original conditional source, not a selected-branch rewrite. Neither a ProjectSnapshotId nor ID/version alone may select some earlier packed content. Validation may use a logical manifest/file view before ZIP serialization, provided the writer receives the same verified bytes.

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
| compileTimeSettings, aliases | Definition settings and default aliases |
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
| Code connection | Target/layout/ABI, Core/runtime, actual native supply and symbols |

Version equality, equal Type sizes, or LLVM verification cannot replace another check. Unknown, Error, missing implementation, and unfinished validation are never success evidence.

ModuleInputId includes raw input identity, effective language, compiler build, verification rules, Core, target/layout/semantic profile, mode, settings, direct-reference mappings, and dependency ModuleInputIds. It is the whole-module fast path, not a declaration identity or a generation-order seed.

### 18.7.2. Correspondence and semantic records

Match old/new graphs in two stages. Within the same project history, use a dedicated root correspondence key and PackageId for dependencies. Ambiguity, including several versions of one ID, disables this matching-based reuse. The root key does not identify unrelated Applications. Within matched nodes, DeclarationKey comprises Container, declaration kind, and normalized declaration identifier, excluding version, content hash, and position.

These keys find candidates only. Actual Type, Symbol, and Composition Entry identities retain the defining version. Compare all content/environment facts read by the judgment and the current identities of its dependencies. Hashes index comparisons; they do not replace necessary structural/premise checks. Remap and validate cross-version references before reuse, otherwise reverify. Complete edit tracking is not required.

| Record group | Preserved information |
| --- | --- |
| Declarations/contracts | Defining Symbol, access/enclosing domains, public paths, open/base relations, Field identity, complete Types/Semantics/Origins, generic slots/projections, Constraints, unsafe and length conditions |
| Property/callable contracts | Stored Types, standard/custom permissions, accessor signatures/Origins, environment/capture identity, internal/public call signatures, receiver acquisition, per-call Origins, effects and returned Loan anchors |
| Conformance/public guarantees | Witness and associated-Type mappings, conditional premises, closed specialization sets, completed ObjectCompatible/root-operation summaries and causes, Supports relationships, Runtime Type Identity and required adjustment/destruction/release contracts |
| Semantic plans | Verified bodies, acquisition/ownership/cleanup, definition-site binding and private dependencies, legitimate representation obligations |
| Verification records | Subject, property, premises, result, content dependencies, rule identity, source references, and generation provenance |

Private information may support generation/reverification without entering consumer lookup; using a public contract must not require searching private bodies. Preserve Origins even when erased at runtime. Export ObjectCompatible only as completed Proven/NotProven plus causes, never pending status. Legitimate generic representation obligations retain subject, premises, dependencies, and deadline; they are not completed proofs. Each use still checks its own obligations, Loans, initialization, and cleanup.

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
