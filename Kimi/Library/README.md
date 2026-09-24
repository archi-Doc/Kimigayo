# Embedded Kimi sources

These files ship as resources in the compiler assembly. They are the source of
the currently implemented Kimi declarations, not the complete required library.
SPEC Chapter 22 and its references remain authoritative.

- Put ordinary types, Contracts and method bodies in `.kimi` files, subject to the
  Text implementation policy below. Add new source
  files to `KimiLibrarySources.Sources.All`; the project embeds `Library/*.kimi`.
  Root-level functions follow ordinary startup parsing, so library helper
  functions belong inside declaration containers such as groups or structs.
- `Slice.kimi` and `Array.kimi` declare the compiler-managed sequence Types; their storage,
  metadata and built-in operations are supplied by the compiler and they may only add functions.
- `Comparison.kimi` declares Equatable and Comparable as ordinary Contracts with
  validated recognized identities. Primitive witnesses use compiler lowering;
  user witnesses use ordinary calls with the same ownership and effect checks.
- Dictionary algorithms belong in Kimigayo sources; see the implementation
  boundary below. Private storage functions are not public library APIs.
- `Intrinsics.kimi`, `Console.kimi`, `Test.kimi` and `ArrayOperations.kimi` contain signatures without source bodies.
  Their private loader supplies the owning container (a group, or the `Array` struct for its mutation operations). Only catalog-registered compiler
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

## Dictionary implementation policy

[DictionaryStorage.kimi](DictionaryStorage.kimi) is compiled through ordinary
Binding, ownership analysis and generation. It implements initialization, ordered
search, insertion links, free-slot reuse, unlinking, reverse cleanup, compaction
and shrink-to-fit decisions. Its private `Handle` and `Links` records use explicit
C layout to agree with the backend; they do not define a public collection ABI.
Callbacks use the ordinary Function Type ABI with stack handles, without heap
allocation. The compiler passes the selected equality witness and typed entry
destruction, preserving comparison direction and value-before-key cleanup.

The migration is incomplete. [DictionaryOperations.kimi](DictionaryOperations.kimi)
still contains compiler-recognized public signatures. Generic mutation/result
dispatch, reserve/growth and typed index/iteration access remain compiler code.
Move the remaining operation bodies into Kimigayo over common memory/ownership
primitives; do not add new Dictionary algorithms as hand-written LLVM IR.
Platform allocation/release, byte transfer, physical representation and verified
typed ownership operations remain compiler responsibilities. Keep the original
operation's Abort location when bridging platform failures.

`DictionaryLibraryTest` checks ordinary source compilation, private access,
symbol identity, record layout and omission from programs that do not use
Dictionary. The Dictionary operation, order, shrink and cost suites exercise the
source implementation at O0/O2, including allocation failure and warm compiler
allocation checks. These checks establish behavior and allocation bounds, not
an unmeasured runtime speed improvement.

## Text implementation policy

Text and UTF-8 formatting operations currently use compiler-supplied LLVM IR,
including the [generated Ryu conversion cores](../../third_party/ryu/README.md).
This is an accepted implementation choice within Kimigayo's LLVM-based backend;
portability alone does not require a rewrite in Kimigayo. LLVM IR implementation
is not a language requirement, and retaining it does not imply that it is faster
than a Kimigayo implementation.

As Kimigayo's features mature, these operations may be considered for implementation
in Kimigayo. A port is optional: compare its compilation time and runtime performance,
including allocations, with the existing LLVM IR implementation before deciding
whether to adopt it. Use equivalent workloads, target, toolchain and optimization
settings, and include the cost of compiling the library code in the comparison.
Either implementation must preserve the specified behavior, borrowing guarantees
and allocation/copy requirements.
