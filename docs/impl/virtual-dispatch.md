# Object creation and virtual dispatch generation

These requirements extend [§21.2–21.3](21-layout-runtime-and-code-generation.md). Language contracts are defined by [object adaptation](../spec/13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing) and [virtual dispatch](../spec/virtual-dispatch.md).

## 1. Shared creation plan

After inference and acquisition, creation adaptations and `makeObj` / `makeRc` / `makeArc` use one plan carrying the complete input Type, target ownership mode, metadata, dependencies and destruction responsibility. Identity acquisition and upcasts keep their existing plans. Use verified intrinsic identity, never source-name substitution and ordinary lookup. Factory Function Items reach the same creation processing.

Carry the selected plan through Binding, ownership analysis and generation. Acquire and evaluate once, transfer responsibility once, publish no handle before initialization, and register metadata and destruction dependencies. Diagnostics and allocation Abort locations identify the written adaptation or call, not synthetic implementation syntax. The spelling changes neither object layout, allocation count nor reference management. Metadata sharing never erases per-call Types, Origins or Loans. Retained synthetic nodes and plans must have bounded lifetimes and support analysis reuse.

## 2. Slot layout

Use the existing object header and shared dynamic-Type descriptor to reach the slot table. Add no vptr to ordinary values, value borrows or base subobjects. Preserve the Windows x64 eight-byte handle, sixteen-byte header and payload offset. Descriptor internals may change; they are not a fixed external or persistent ABI.

Within a generation unit, inherit the bound direct base's slots as an unchanged index prefix. Replace an entry for an override; append new slots. Do not compact positions for inapplicable or unreachable slots: later indices stay fixed, without requiring a valid entry or generated body for the omitted position. Renumber only while consistently regenerating all affected tables and references.

Keep semantic SlotId, Origin-erased generation key and physical index separate. Origin differences alone neither duplicate runtime slots nor select different implementations. Reproduce layout from the same inputs; source/load order is not semantic priority. Immutable tables may be shared across Types only when entries, ABI and required context agree.

## 3. Dispatch and dependencies

All call paths share the original public contract, bound signature, checked receiver correspondence, entry ABI and generation context. A normal virtual call reads the known slot index without a mandatory wrapper; a base call uses its selected implementation entry directly.

Generate a dispatch entry only when a Function Item needs an address; it dispatches from the input receiver on every call. Function Items remain environment-free. ABI adaptation, argument acquisition, results and cleanup reuse FunctionAbi and existing plans. Share Type contexts. Dispatch itself requires no heap allocation, reference-count operation, name lookup or Type search; costs inherent in arguments, defaults, bodies and existing function erasure remain separate.

Each retained descriptor's applicable entry contributes dependencies on its selected implementation, ABI adaptation and context, then their ordinary helper, factory and destructor dependencies. Use the existing generation worklist, key deduplication, cycle handling and resource bounds. No direct call is required to keep an override reachable. Inapplicable bindings add no body dependencies. Finite cycles reuse entries; unbounded generic-key growth reports Resource and publishes no incomplete IR. Do not enumerate all descendants or Type arguments.

After legality and public guarantees are checked, proven dynamic Types or reachable implementations may be devirtualized or inlined. Optimization cannot change acceptance, call identity used for effect/result proofs, table-index consistency or unused-declaration checking. Proven-unreachable bodies and references may be omitted.

## 4. Artifacts and invalidation

Persist the semantic slot/implementation mappings and verified public guarantees required by clients. Missing mandatory evidence is an artifact error, not absence of an override. Revalidate lookup, conformance, receiver/effect proofs, generated dependencies/tables and semantic display when contracts, base paths, accessibility, conditions, Type bindings or bodies change. Rebuild physical indices and references together. Share immutable comparison/generation inputs where their checked identities agree; never use a hash alone as proof of semantic equality.
