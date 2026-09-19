# Embedded Kimi sources

These files ship as resources in the compiler assembly. They are the source of
the currently implemented Kimi declarations, not the complete required library.
SPEC Chapter 22 and its references remain authoritative.

- Put ordinary types, Contracts and method bodies in `.kimi` files. Add new source
  files to `KimiLibrarySources.Sources.All`; the project embeds `Library/*.kimi`.
  Root-level functions follow ordinary startup parsing, so library helper
  functions belong inside declaration containers such as groups or structs.
- `Intrinsics.kimi` and `Console.kimi` contain signatures without source bodies.
  Their private loader supplies the owning group. Only catalog-registered compiler
  implementations are allowed in these groups; this is not public syntax for
  declaring a user intrinsic or omitting a function body.
- Register compiler-recognized identities in `KimiLibraryCatalog` and validate
  their contracts in `KimiLibraryValidation`. Append new stable declaration IDs;
  never derive them from source order. Ordinary helper declarations need no ID. `SourceExpected` distinguishes a
  broken embedded source from an API whose source is not implemented yet.
- Preserve reference identity through binding. Call lowering must use the
  recognized symbol or compiler-function kind, never a user-visible spelling.

Source text and successfully lexed immutable tokens are cached and shared. Source
documents, ASTs, symbols and validation state remain compilation-local; no cached
item retains a compilation. Token publication is thread-safe, and lexical errors
are reported per compilation rather than cached. Rebinding does not perform
resource IO, lexing or parsing. Keep the allocation checks in `CoreCatalogTest` and use
`KimiLibraryBenchmark` for initialization and warm-binding measurements.

Catalog validation does not prove runtime support. Missing API entries do not
create placeholder declarations. Use `GetSymbol` / `GetDeclarationState` for ID
lookup; the `Declarations` sequence is not indexed by the numeric ID. The old
ObjectOwnership ID is reserved; ownership-family completeness is a separate query.
