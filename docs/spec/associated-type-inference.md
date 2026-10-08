# Bounded associated-Type inference

This chapter defines the declaration completion allowed by [§8.4.3](08-generics-constraints-and-contracts.md#843-associated-types). It introduces no syntax, implicit conformance or general Constraint solving. Explicit bindings remain available. The same completed associated-Type and witness mappings govern calls and generation, regardless of how a binding was obtained.

## 1. Unit and frozen premises

Inference supplies missing associated Types **without Origin parameters** in declared static conformances of structs and enums. One inference unit comprises every conformance of one merged nominal definition, including direct and refinement paths. Nested definitions are separate units. Generic instantiation never reruns inference.

Before collecting evidence, freeze each path's explicit specifications, Contract/ancestor identity Constraints, inherited bindings and consequences of the existing limited proof rules (§8.7). Only premises valid throughout that path are available: Type Constraints, conformance conditions and Contract Constraints. Requirement-specific and implementation-function premises cannot determine a public binding.

An associated identity includes the complete conforming Type, associated declaration and bound defining Contract. Normalize identities and compare Types in the destination path's frozen environment. Same names do not establish identity; an equality conditional on one path is not an unconditional equality on another.

Forward substitution of fixed identities is allowed. For example, Iterator's `LentItem(step) is Item` exposes Item through the parent's result without inferring a family. Existing invalid cyclic specifications remain invalid.

No newly inferred value in this unit enters its frozen premises, even through another conformance, alias, projection or retained cache. An independently completed public binding of another nominal definition may be used, including an inferred binding. Dependency scheduling waits for the associated **Type value** actually needed by normalization, matching or extraction, not for nominal names or entire conformance verification. A dependency cycle returning to an inference result of the current unit supplies no evidence. Thus `A.make -> B` and `B.make -> A` need no mutual conformance wait: their nominal result identities are already known. Formation and conformance verification obligations remain attached to early Type values.

## 2. Identify once

Use ordinary lookup and the complete [§8.4.5 identification key](08-generics-constraints-and-contracts.md#845-implementation-matching), evaluated only under frozen premises. Do not filter or rank by result Type, Origins, Constraints, conditional-member applicability or Effects. Existing access rules keep their existing lookup/compatibility roles. An unresolved candidate is not a mismatch.

Record each identification as unique, missing, ambiguous or pending. A unique match retains its Member Identity, substitutions and base-receiver correspondence. Only a unique match whose whole distinguishing input key is independent of the unit's missing associated Types can supply evidence. Both requirement and implementation must have value results. Property, Field, accessor and Place results supply none.

The implementation result is its declared `-> Type`, or Unit under the existing omitted-result rule for named functions. Bodies, reachability, call-site expectations and successful instantiations supply no evidence. An input-dependent pending match never becomes a new evidence source after completion, even if it then has one candidate.

## 3. Extract complete Types

Normalize both declared result Types using frozen aliases and fixed identities. Traverse only structural paths to missing eligible associated Types:

| Requirement position | Permitted extraction |
| --- | --- |
| Eligible missing associated Type | Take the corresponding complete implementation Type. |
| Same nominal construction or Tuple | Follow corresponding arguments with the same nominal declaration and arity, or Tuple arity. |
| Fixed array | Follow its element only when lengths are already equal under frozen premises; never infer the length. |
| Concrete Semantics layer | Follow the same layer, using ordinary owner normalization. |
| Function Type, unresolved projection or unresolved Semantics application | Do not invert its internals. Forward normalization may expose another permitted shape. |

A missing associated Type can receive an entire formed Function Type, whose own bound variables stay local to that Type. This does not permit extraction from inside a Function Type.

Preserve Semantics, arguments, nested structure, lengths and all Origins. Every free Type, length or Origin binder in the extracted Type must be valid in the conformance scope. A method's generic parameter, per-call Origin or elided receiver Origin cannot escape. A Type-scope parameter, `Box<T>` or an independently justified symbolic projection such as `T.(Source).Element` is permitted. Neither an unresolved associated value from this unit nor an indirect dependency on one is complete evidence.

Origins along a traversed wrapper that are absent from the extracted Type remain ordinary result-compatibility obligations. Perform no new Origin solving, lifetime shortening, borrowing, implicit conversion, Copy judgment, subtype inversion, common-supertype selection or search for a capability-satisfying Type.

Known portions of a result are checked by ordinary compatibility, not an extra exact-result comparison. In `(Item, Known)`, extraction at Item does not require the Known portions to be identical. A shape without a permitted correspondence supplies no evidence at that position; its compatibility is checked later.

## 4. Share, complete and verify

Retain each piece of evidence as associated identity, complete Type, source premises, source location and outstanding validation obligations. Index by associated declaration and bound structure. A destination may use it only when its frozen premises prove its source premises using §8.7. Sharing neither augments the frozen basis nor triggers another collection round.

Aggregate usable evidence per destination-normalized identity. One or more exactly identical complete Types establish that binding. Conflicting Types, including distinct Origins, are an error; no first-candidate choice or common Type search is allowed. With no evidence a required missing binding needs an explicit specification. A Type containing an escaping binder is unusable evidence, with that cause retained.

Bindings already fixed explicitly, by an ancestor or by inheritance are not inference holes. A capability-only clause such as `associate C.Item is Copy` leaves a hole; the completed Type must then satisfy Copy.

After simultaneous completion, finish headers and all ordinary formation, capability, accessibility, Origin, Loan, Safety, Effect and overlapping-path checks. Reuse each stored identification. Only genuinely pending matches receive their first completed identification; missing/ambiguous matches are not retried. Compatibility failure never reselects. Shared evidence does not by itself certify its source conformance, nor introduce artificial parent/child verification cycles: discharge the underlying obligations.

Explicit bindings retain ordinary result compatibility. For example, an enclosing `longer outlives shorter` may permit an implementation returning `ref/T during longer` for a published `ref/T during shorter`. Inference does not search for that shorter public Type. No nominal base-result conversion is added.

Inherited mappings and bindings retain their existing meaning under §8.4.4; they are not reinferred from derived Self or new members. A newly declared conformance using inherited members follows ordinary receiver correspondence.

## 5. Examples and boundaries

```kimi
contract Source
    associate Item
    func read(self: ref/Self) -> Self.Item

struct NumberSource
    Self is Source
    drop => ()
    public func read(self: ref/Self) -> i32 => 42
```

NumberSource publishes `Source.Item is i32`. Arithmetic implementations similarly infer each Output independently from `added`, `multiplied`, `multipliedFrom` or `negated` when their keys are determined. Output need not equal Self. `Option<Item>` against `Option<T>` supplies Item = T when T belongs to the conformance scope; Iterator's fixed family equality admits this case for `next`.

If `C: P` adds a function returning P.A, unconditional evidence for P.A is shared with a redundant direct `Self is P`. If C is available only under Q, the destination must independently prove Q. All path validation obligations remain.

In one Type, inferring Source.Item does not allow an implementation result `Self.(Source).Item` to infer Factory.Output. Explicitly fixing Source.Item can enable both. Similarly, `convert(item: Item) -> Output` cannot seed Output when Item is missing from the frozen key.

An omitted result is Unit: `func discarded() => 123` discards a value under the usual unused-value rules, whereas `func invalid() => return 123` fails Unit compatibility. Neither infers i32 from its body.

## 6. Publication, diagnostics and reuse

Publish completed bindings through ordinary verified conformance metadata (§18.7.2), retaining inferred provenance and source references separately from semantic identity. Consumers need no body search. Equal explicit and inferred bindings with the same mappings are the same semantic contract. Source/candidate/premise/base edits invalidate dependent headers, evidence, positive and negative results before old bindings can certify a use. Later formation or witness failures invalidate dependent projection proofs.

Under §23, distinguish no evidence, a key depending on a missing associated Type, absent structural correspondence, conflicting evidence and an escaping binder. Preserve original lookup, duplicate-declaration, missing and ambiguous implementation causes. Locate omission failure at its conformance declaration; relate the associated requirement, relevant implementation result and, when needed, input or escaping binder. An invalid explicit specification keeps its own primary location. CLI, JSON and LSP expose the same cause and locations, including under display limits; ordinary prerequisite rules suppress only dependent errors.

Advice may show where an explicit `associate` belongs, but must not offer a guessed Type as an automatic repair. A proven semantics-preserving explicitization may be offered only with the ordinary repair preconditions; implementing that edit is not required. Hover shows a relevant inferred binding and its source, using structured semantic provenance and never presenting an unverified conformance as valid.

Acceptance depends on declaration information, never an inference-work budget. Resource exhaustion is a distinct incomplete result, not missing evidence or a demand for an explicit binding. [Implementation requirements](../impl/associated-type-inference.md) bound work and reuse without narrowing these rules.
