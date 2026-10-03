# Shared object runtime verification

IMPL §21.2.3 is authoritative. This record explains the implemented transitions and their checks; it does not define
language behavior. Atomic handles and Weak operations remain gated until their separate transitions are implemented.

## Inline rc transitions

- **Creation:** one payload-specific factory serves obj and rc with a checked static control argument, respectively
  zero and two. It initializes the descriptor, control and payload before returning the original header pointer.
  Metadata describes the payload; the handle's static mode chooses cleanup. No clone allocation is needed.
- **Retention:** a valid shared borrow of a live strong-handle slot supplies the header. Because no executable operation
  can create a side table, control is an even encoding of a positive strong count. The maximum encoding is `-2` as an
  LLVM i64 bit pattern. That equality branches to a located `KIMI_E_REF_COUNT` Abort before any write. Otherwise adding
  two cannot wrap and transfers one additional destruction responsibility to the returned handle.
- **Release:** a valid owning handle supplies one existing strong reference. Subtracting two publishes the reduced
  count before any destructor runs. Only an old encoding of two selects finalization. Other releases perform no payload
  destruction or free. Finalization retains its descriptor through payload destruction and then frees the original
  header; the release helper returns without any further header access. rc requires no atomic ordering.

These conclusions rely on the ownership analysis's Move/Loan rules and the closed Weak/atomic execution gates, not on
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
