# Shared object runtime verification

IMPL §21.2.3 is authoritative. This record explains the implemented transitions and their checks; it does not define
language behavior. Weak operations remain gated until their side-table transitions are implemented.

## Inline rc transitions

- **Creation:** one payload-specific factory serves obj, rc and arc with a checked static control argument, respectively
  zero, two and two. It initializes the descriptor, control and payload before returning the original header pointer.
  Metadata describes the payload; the handle's static mode chooses cleanup. No clone allocation is needed.
- **Retention:** a valid shared borrow of a live strong-handle slot supplies the header. Because no executable operation
  can create a side table, control is an even encoding of a positive strong count. The maximum encoding is `-2` as an
  LLVM i64 bit pattern. That equality branches to a located `KIMI_E_REF_COUNT` Abort before any write. Otherwise adding
  two cannot wrap and transfers one additional destruction responsibility to the returned handle.
- **Release:** a valid owning handle supplies one existing strong reference. Subtracting two publishes the reduced
  count before any destructor runs. Only an old encoding of two selects finalization. Other releases perform no payload
  destruction or free. Finalization retains its descriptor through payload destruction and then frees the original
  header; the release helper returns without any further header access. rc requires no atomic ordering.

These conclusions rely on the ownership analysis's Move/Loan rules and the closed Weak execution gate, not on
the payload being Copy or Owned. Opening side-table operations must add representation dispatch to both transitions.
`SharedObjectRuntimeTest` checks generated branch/store order, maximum and preceding-count execution, source locations,
clone lifetime, mixed obj/rc cleanup, returned/replaced handles and zero-allocation warm analysis/emission. Native
allocation audits require one allocation per factory, none per clone, and exactly one final free.

`SharedObjectOwnershipTest` additionally verifies that clone results keep external payload Loans without retaining the
source handle slot, including scope exit and replacement. Payload destructor observations retain those Loans through
release. Complete handles move through Tuple/Case decomposition without decomposing their pointees; inactive Cases
release nothing. Borrowed object views still prevent their protecting handle from being moved (CLI/LSP checked).

`SharedObjectCleanupTest` exercises rc upcast/clone with dynamic Type tests and complete payload destruction, Array
clear and early owning-iteration exit, and acquired clones abandoned by a later returning argument. Raw storage Moves
transfer the complete obj/rc handle through the ordinary storage plan; bare acquisition still requires explicit Move.

## Inline arc transitions

Creation uses the same ordinary stores while the complete allocation is unpublished. Returning the initialized handle
publishes it to the creating thread only; no source concurrency or cross-thread payload-publication rule is introduced.

Retain loads the control atomically with monotonic ordering, checks the maximum and compares the entire word with CAS
(monotonic success/failure). Only success writes the returned handle. Failure retries with the value returned by CAS,
including a fresh maximum check. A live handle-slot borrow supplies an existing strong responsibility, so the inline
count cannot be zero. Weak/cyclic factories are unavailable, so no admitted operation can produce an odd control.

Release uses a whole-word CAS with release success and monotonic failure ordering. Failure retries the observed value;
only successful 2-to-0 selects finalization. All successful counter updates are read-modify-write operations, so they
carry the release sequence through intervening retain/release operations. The final successful CAS reads that sequence,
and its following acquire fence establishes the ordering from earlier strong releases to payload destruction and free.
Nonfinal releases do neither. Sealed and dynamic cleanup share this transition; both perform no header access after
free. Ownership supplies exactly one release responsibility per owning handle, independently of payload Copy/Owned.

`SharedArcRuntimeTest` checks atomic accesses, orderings, whole-word update targets, observed-value retries, maximum
checks before writes, the success/finalization branches, the acquire fence and no post-free access. O0/O2 cases exercise
clone lifetime, maximum and preceding counts, generic factory/clone calls and exact allocation/free counts. Warm compiler
analysis/emission retain zero-byte assertions. Before Weak is opened, both rc and arc retry paths must gain representation
dispatch and the remaining IMPL table-resolution/migration/Weak/publication transitions; this proof makes no claim about
those currently unreachable states or about source-level thread safety.

The ownership and cleanup cases above share the same source workloads and expectations for rc and arc. They cover
external Loans, destructor observations, dynamic base identity, Tuple/Case/fixed-array/closure storage, Array/Dictionary
removal and owning exit, abandoned arguments and CLI/LSP handle protection. `GenericObjectFactoryTest` exercises arc
factories in concrete function/container instances. `ObjectSlotUpdateTest` covers complete Owned handle exchange,
replacement and swap; content-sensitive updates with external Origins remain a separate implementation limit.
`SealedObjectFinalizationTest` covers direct arc cleanup, zero-sized payloads and recursive dynamic fallback with exact
allocation/free counts and unchanged zero-allocation compiler measurements.
