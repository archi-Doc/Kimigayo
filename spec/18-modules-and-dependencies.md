# 18. Modules and dependencies

[Specification index](../SPEC.md)

A module groups source declarations and controls the names exposed through dependencies. Source environments belong to definitions, not to their callers or merged-container wrappers.

| Term | Meaning |
| --- | --- |
| Kotonoha | One named source or binary module. |
| SourceDocument | One immutable source input belonging to a Kotonoha. |
| Compilation root | Entry point for the project root and direct-dependency reference names. |
| Project root | Root of the primary Kotonoha's declaration hierarchy. |
| Source environment | A document's definition-site aliases and lookup context. |

Merged declarations keep each fragment's definition-site source environment for names, Types, Constraints, and diagnostics. Merging must not apply one fragment's aliases to another. Generated documents have independent source environments.

Each Compilation owns a **Compilation root**, direct-dependency reference-name mappings, and default aliases. The primary Kotonoha's Container hierarchy ends at the **project root**. A resolved Symbol identifies its declaration, including originating Kotonoha/version, independently of spelling or alias path.

## 18.1. External references and aliases

Only directly referenced Kotonoha libraries are source-addressable by library name. Use qualification such as `ExternalLib.GroupA.StructB` or an explicit `alias` declaration; do not search all external members unqualified. Multiple library versions may use different reference names, with configuration syntax separately specified. Loading transitive metadata for type checking does not expose those libraries by name.

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

**Design boundary:** Versioned dependency reference configuration and reference-graph diagnostics remain separately specified. Re-export syntax follows [Re-exports](#182-re-exports).

## 18.2. Re-exports

**Deferred design:** re-export syntax and artifact representation are undefined; aliases never re-export. Consumers naming an external Type/requirement must directly reference its originating Kotonoha and have an accessible Symbol path. Neither a public Signature containing that Type nor transitive metadata loading supplies Name Reachability.

Any future re-export design must preserve the original Symbol, avoid widening access, and reject cycles without a real target. Its syntax and compatibility requirements belong to that feature's specification.

## 18.3. Source artifacts and binary interfaces

Portable interchange uses source artifacts or binary interfaces, not serialized Koto internals. Source artifacts preserve source/configuration for reconstruction. Binary interfaces record:

- Symbol/version identity, access/enclosing domains, visibility/public paths, and open/base relationships;
- normalized Signatures, complete API Types/requirements, Constraints, Origins, and unsafe requirements;
- Stored Property Types and standard/custom operation permissions, accessor signatures and Origins, verified witness mappings, conditional-conformance premises, and generic specialization inputs;
- ABI, layout, calling conventions, and target/language/compiler identity.

Private generic dependencies preserve defining Symbols and access context without becoming public Names. These are information categories; encoding, required fields, validation, and compatibility belong to the separate artifact-interface specification.

Callable interfaces additionally preserve concrete environment identities, capture dependencies, internal/public call signatures, receiver acquisition contracts, per-call Origin quantification, and conservative effects/returned storage anchors (§15.6.4). Borrowed-receiver operations publish §12.4.4's completed root summaries, operation Identity, Proven/NotProven status, and diagnostic cause information; no pending ObjectCompatible state is exported. Object interfaces retain defined Supports relationships, verified conformance mappings, Runtime Type Identity, receiver adjustment, and complete destruction/storage-release information. These requirements prescribe no encoding or fixed ABI.

Generic interfaces also retain complete slot/projection and Origin schemas, deferred obligations, and the defining Kotonoha's closed explicit-specialization set and mappings. Preserve the body and dependency information needed for correct implementation selection and code generation under [generic artifacts](21-layout-runtime-and-code-generation.md#2134-artifacts-verification-and-invalidation).

Record the declaration-content dependencies used by inherited-Name checks, conformance, and public effect guarantees, including completed Container contents when absence of a declaration matters. Existing Symbol/access information can supply these contents; no separate Name-set encoding is required. Accessible Names of open structs and public compatibility guarantees are API facts. Adding a Name, expanding access, or withdrawing Proven can break a consumer, but does not require upstream diagnosis of unknown downstream code. Revalidation and consumer-side diagnostics follow §21.3.4; ordinary Type/Origin/layout obligations remain distinct from a completed public status.
