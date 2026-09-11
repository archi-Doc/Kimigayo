# Property declaration contracts and unconditional conformance

Implemented the next Property milestone in Binding, against SPEC §§11.1–11.4.

## Declaration contracts

- `BindingSymbol.Property` retains reusable `BoundProperty` and `BoundAccessor` metadata. An omitted stored accessor has no generated syntax, function, or lookup candidate. Standard get records Place permissions and deliberately has no callable receiver signature.
- Explicit accessors bind semantic `self`, immutable `value`, and direct-slot `storage` symbols. The slot symbol cannot recursively invoke an accessor. Direct writes to contextual storage through a shared getter are rejected.
- Stored input/result Types match the complete storage Type, including Origins. Custom stored getters require definition-side Copy evidence; custom setters do not.
- Computed/required headers use getter-result Origin completion. An implicit requirement setter completes its input separately. Origin-free headers reuse their complete Type directly; Origin-bearing shared headers are rebound under the second signature context using reusable semantic snapshots, then restored.
- Access restrictions are checked against declared Property access. API visibility uses each accessor's own domain. Computed/required setter input Types can differ from the getter result.
- Accessor bodies use the existing expression and return-Type checks, and the existing control-flow Type provider understands their result boundaries. Declaration verification does not certify ownership, lifetime, or complete executable-body validity.

## Witnesses and lookup

- Function and accessor contracts share a nonallocating view and the same input-structure, Origin correspondence, substitution, and result-compatibility comparison. No temporary `FunctionKoto` is used.
- `BoundConformance.PropertyWitnesses` and `GetPropertyWitness` retain separate get/set operations: explicit accessor call, storage Copy, shared slot borrow, or standard set.
- Each operation retains the selected accessor/Property identities, required complete signature, implementation Type substitution, Origin input correspondence, and base path. Retained Origin correspondence has its own reusable storage; it never aliases Binding scratch.
- Ordinary qualified lookup and Property matching use the same committed member selection. Type, operation, or access failure does not restart base lookup.
- Standard inherited storage witnesses retain the exact path and generic substitutions. Base declarations check open structs, visibility, single inheritance, and declaration-identity cycles. No base-to-derived or derived-to-base complete-Type conversion is added.
- Contract refinements propagate operation witnesses by requirement identity. Final conformance verification reruns after declaration/body-Type validation; re-Bind clears old verification and mappings, including removed Property metadata.

## Deliberately pending

- General conditional conformance, evidence-path agreement, and conditional member scopes.
- Callable Property expression operations, call-site Origin inference, getter temporary restrictions, and full Place permissions. These uses remain explicitly unsupported instead of using the Property header as a caller result Type.
- Inherited callable-accessor witnesses requiring projected receiver correspondence and ObjectCompatible evidence. Standard storage bridges are supported independently.
- CFG ownership/lifetime/cleanup checking and lowering of retained operations. A verified declaration witness alone is not an executable-program certificate.

## Validation

`PropertyBindingTest` covers implicit accessors, custom signatures/body Types, independent Origin completion, Copy evidence, access domains, the limited witness bridges, reference Copy versus reference-slot borrow, associated Types, refinement identity, base paths/hiding, invalid bases, syntax replacement, and warm Binding reuse.

Warm allocation assertions cover 1, 32, and 512 implementing Types, custom get/set plus a standard borrow witness, and Origin-bearing requirements with inherited storage. Each measured pass must allocate zero bytes on the Binding thread. The existing allocation assertions for function calls, Contracts, Origins, and capabilities remain in place.

`BindingBenchmark` includes a Properties scenario at 32 and 512 implementing Types. An exploratory in-process run completed; its timing variance and the runner's aggregate allocation figures are not treated as a throughput or zero-allocation guarantee. The configured out-of-process MediumRun could not restore its generated project because the sandbox denied access to the user's NuGet.Config. The per-thread allocation regression tests are the validation used for this milestone.
