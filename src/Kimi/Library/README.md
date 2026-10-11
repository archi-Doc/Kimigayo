# Embedded Kimi sources

These files ship as resources in the compiler assembly. They are the source of
the currently implemented Kimi declarations, not the complete required library.
SPEC Chapter 22 and its references remain authoritative. The public API is
summarized in [LIBRARY.md](../../../docs/LIBRARY.md); this file covers how the sources are
embedded and implemented.

- Put ordinary types, Contracts and method bodies in `.kimi` files, subject to the
  Text implementation policy below. Add new source
  files to `KimiLibrarySources.Sources.All`; the project embeds `Library/*.kimi`.
  Root-level functions follow ordinary startup parsing, so library helper
  functions belong inside declaration containers such as groups or structs.
- `Slice.kimi` and `Array.kimi` declare the compiler-managed sequence Types; their storage,
  metadata and built-in operations are supplied by the compiler and they may only add functions.
  The internal group `Kimi.Storage.FixedArray` in `Storage.kimi` holds the fixed-array receiver
  functions. The `length` and `indices` of a fixed array stay compiler sequence metadata, shared
  with Array and Slice: a group declares functions, not Properties, and the fixed length folds to `N`.
- A single-position operation that calls an internal `isize` operation passes a resolved position on its
  own path (`match index.tryResolve(length)`, with `-1` on the `None` path) rather than a merged
  `PositionSyntax.resolved` value, where LLVM cannot relate the merged value to the length (as for
  the one-element move of `insert(^1, value)`). `swap` passes both positions merged: `swapAt` checks
  both before its equal-position shortcut, so `-1` still fails its one check.
- Array and fixed-array read APIs retain small Kimigayo wrappers over the same Position/PositionRange
  resolution used by Slice. Their required equivalence is observable behavior, not a fixed internal
  call graph: delegation through a full Slice would create a whole-sequence view before a failed try
  lookup. Keep the native API-equivalence, receiver-once and Partial Move checks when changing this
  choice, and measure any claimed benefit before replacing the wrappers.
- `Comparison.kimi` declares Equatable and Comparable as ordinary Contracts with
  recognized identities. Primitive witnesses use compiler lowering;
  user witnesses use ordinary calls with the same ownership and effect checks.
- Array's two sorting overloads use the same heapsort traversal in Kimigayo. The
  default overload calls Comparable directly: a generic anonymous Callable adapter
  is not supported yet. The child-index calculation is bounded by the internal-node
  test before multiplication. Both overloads swap initialized elements without
  Copy, destruction or allocation; stored concrete callbacks execute through the
  ordinary value-call path.
- Dictionary algorithms belong in Kimigayo sources; see the implementation
  boundary below. Private storage functions are not public library APIs.
- `Windows.kimi` contains ordinary `#LibraryImport` declarations; `Time.kimi` contains Duration,
  Stopwatch and the counter conversion algorithms. Both join `KimiLibrarySources.Sources.All`
  and follow normal Binding, ownership analysis and lowering. Only reachable library imports
  are emitted. The private `counterFrequency` is initialized by the ordinary first-access static
  protocol; it has no compiler-recognized name or special startup hook.
  `Storage.addressOfI64` is the sole new compiler operation: it exposes the existing exclusive
  argument's address for the synchronous native output call, without a copy or allocation.
- `Iteration.kimi` declares the public `Kimi.Iteration` adapters (`owning`, `borrowing`, `OwningIterator<I>`, `BorrowingIterator<I>`) as ordinary Kimigayo.
- `Intrinsics.kimi`, `Console.kimi`, `Test.kimi`, `ArrayOperations.kimi`, `StorageOperations.kimi` and `RawOperations.kimi` contain signatures without source bodies.
  Their private loader supplies the owning container (a group, the `Array` struct for its constructor and mutation operations, the
  internal `Storage` group of `Storage.kimi` for the standard storage boundary, or the public `Raw` group of `Raw.kimi` for raw storage; a struct also admits bodiless `init` signatures).
  Within these signature resources, only catalog-registered compiler implementations may omit bodies
  without an ordinary `#LibraryImport` declaration. A catalog link may instead name an internal source function of the same
  struct as a bodiless constructor's definition (`Array.init(! capacity:)` → `Array.initCapacity` and
  `Array.init(! repeating:count:)` → `Array.initRepeating`, SPEC 22.1); calls select and present the public constructor and
  execute the implementation. Ordinary
  helpers in these containers use the normal compilation pipeline; this is not
  public syntax for declaring a user intrinsic or omitting a function body.
- `Storage.kimi` implements contiguous shared/exclusive `splitFirst` by borrowing raw element
  Places (SPEC 5.2.2), and owning `takeFirst` by taking them with `@move`.
  Array and fixed-array `tryGetPairUniq` share its ordinary Kimigayo position resolution
  and disjoint pair splitting, using logical positions even for zero-sized elements.
  Its owning remainder destroys unreturned elements in reverse order in a concrete
  generic `drop`, then calls the internal unsafe `release` primitive. Only handle
  construction, capability creation and raw region release remain compiler operations.
- Register compiler-recognized identities in `KimiLibraryCatalog`; `KimiLibraryShapeTest` fixes the shapes the
  compiler and the runtime rely on, so a change to them updates that test with its compiler consumer. Append new
  stable declaration IDs; never derive them from source order. Ordinary helper declarations need no ID. A
  `SourceExpected` declaration that is missing or duplicated fails library construction; the other entries are APIs
  whose source is not implemented yet.
- Preserve reference identity through binding. Call lowering must use the
  recognized symbol or compiler-function kind, never a user-visible spelling.

Source text and successfully lexed immutable tokens are cached and shared. Source
documents, ASTs, symbols and binding state remain compilation-local; no cached
item retains a compilation. Token publication is thread-safe, and lexical errors
are reported per compilation rather than cached. Rebinding does not perform
resource IO, lexing or parsing. Keep the allocation checks in `CoreCatalogTest` and use
`KimiLibraryBenchmark` for initialization and warm-binding measurements.

Recognition does not prove runtime support. Missing API entries do not create
placeholder declarations. Use `GetSymbol` for ID lookup. The old ObjectOwnership
ID is reserved.

## Dictionary implementation policy

[DictionaryStorage.kimi](DictionaryStorage.kimi) is compiled through ordinary
Binding, ownership analysis and generation. It implements initialization, ordered
search, insertion links, free-slot reuse, unlinking, reverse cleanup, compaction
and shrink-to-fit, reserve, checked capacity growth and append decisions, plus the literal's duplicate-key rejection. Its private `Handle` and `Links` records use explicit
C layout to agree with the backend; they do not define a public collection ABI.
Callbacks use the ordinary Function Type ABI with stack handles, without heap
allocation. The compiler passes the selected equality witness and typed entry
destruction, preserving comparison direction and value-before-key cleanup.

`Dictionary.tryGet` in [Dictionary.kimi](Dictionary.kimi) searches through the standard shared storage remainder and
constructs its optional reference through ordinary generic source. `remove` shares that typed key search, unlinks one entry
and transfers its complete key/value Types into the optional pair through ordinary source. `clear` uses a private unsafe projection of the mutable
handle and entry stride, and shares typed reverse destruction with the owning remainder. The projection acquires no entries
and exposes no public ABI. The three iteration entries also use Kimigayo.
Dictionary declares `UniqIndexable<K>` with ordinary `index` and `indexUniq` bodies over those remainders.
Subscripts, direct calls and generic Contract calls share these bodies; catalog metadata adds a private caller-location pair to their
physical ABI so the missing-key primitive reports the access rather than the embedded source.
The same catalog metadata and private ABI carry the caller context through Position/range `resolve`, integer range
iteration entries and their `starting` helpers, and Array `truncate`. Their Kimigayo bodies choose the failure;
`Storage.argumentOutOfRange` only reports its canonical code at the forwarded location. Ordinary user `$abort` is unchanged.
`tryInsert` and `insertOrReplace` search through the shared storage remainder in ordinary source. The private
`Storage.placeEntry` appends one slot and transfers the acquired pair, carrying the standard operation's caller location
for growth failure; `Storage.placeValue` refills a live slot after `valueAt` moved its value out, so replacement secures
the old value before placing the new one and destroys the unused input key through ordinary cleanup.
`reserve` and `shrinkToFit` call `DictionaryStorage.reserveEntries`/`shrinkEntries`, caller-location source bodies like
`resolve`: growth, append and compaction call `Raw.allocate`/`Raw.release`, which report the forwarded location inside
such bodies, and the private `Storage.countOverflow`, `allocationSizeExceeded`, `tryAllocateBytes` and `transferBytes`
primitives (SPEC 22.1.2.5). No callback carries the location: a call through an erased or generic Callable value has
unknown environment effects, which `confined` users of `reserve`/`tryInsert`/`shrinkToFit` exclude, and a captured
location would need a heap environment.
Move the remaining operation bodies into Kimigayo over common memory/ownership
primitives; do not add new Dictionary algorithms as hand-written LLVM IR.
Platform allocation/release, byte transfer, physical representation and verified
typed ownership operations remain compiler responsibilities. Keep the original
operation's Abort location when bridging platform failures. Nonempty literals initialize a live empty handle, acquire
and check each key before acquiring its value, and transfer each completed pair through the same typed placement bridge
as insertion. The partial handle and pending key use ordinary temporary cleanup on an early return. The duplicate-key
Abort callback retains the later key's location; it and the equality adapter use stack handles only.
Capacity callbacks provide checked allocation, release, byte transfer and source-located failure. Growth copies the high-water slot range,
including free links, and publishes the new buffer only after allocation succeeds. Geometric rounding saturates at the representable
entry count; it never rejects an otherwise representable minimum. The shrink path keeps its separate nullable-allocation contract.

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
