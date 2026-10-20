# Inline Generic Parameter Constraints

Date: 2026-10-20. Status: 一部取り込み (open, intake only; no specification integration).
This is a discussion proposal. No syntax, policy, compiler change or migration is approved.

## 1. Problem and proposed form

A short generic callback API separates a parameter's introduction from its requirement:

```kimi
func apply<F>(value: i32, transform: ref/F) -> i32
    F is Callable<(i32) -> i32>
    return transform(value)
```

The owner requests consideration of this alternative:

```kimi
// Proposed syntax; not current Kimigayo.
func apply<F is Callable<(i32) -> i32>>(value: i32, transform: ref/F) -> i32
    return transform(value)
```

The benefit is proximity: the generic parameter and its requirement are read together,
and a short contract no longer needs a separate line. This does not shorten the closure
expression itself, eliminate `ref/F`, or expand inference.

## 2. Existing rules and semantic boundary

- [SPEC §7.5](../../docs/spec/07-functions-and-callable-values.md#75-function-constraints)
  owns function constraint regions and their permitted subjects.
- [SPEC §§8.1–8.3](../../docs/spec/08-generics-constraints-and-contracts.md#8-generics-constraints-and-contracts)
  own generic parameters, requirements and proof. A requirement is more than a Contract:
  it may express Type identity, Boolean combinations or Semantics where the subject permits.
- [SPEC §8.10](../../docs/spec/08-generics-constraints-and-contracts.md#810-callable-constraints)
  owns Callable receivers and signatures. `ref/F` acquires the argument; `Callable` states
  how it can be called. Neither is redundant.
- [SPEC §8.11.6](../../docs/spec/08-generics-constraints-and-contracts.md#8116-callable-effect-bounds)
  attaches effect bounds to Callable clauses.
- [SPEC §10.5](../../docs/spec/10-overload-resolution-and-inference.md#105-inference-boundaries)
  owns expected signatures and inference boundaries.
- [Appendix F](../../docs/spec/appendices/F-syntax-summary.md) currently defines
  `GenericParameter := Name | Name "/" Name | "length" Name`.

The proposed invariant is semantic equivalence to an existing constraint on the same
declaration. Placement must not change scope, proof, overload selection, specialization,
expected signatures, acquisition, erasure, Origins or effects. Inline placement must not
turn a requirement on `T` into a conformance declaration for `Self`.

## 3. Alternatives

| Option | Benefit | Cost / unresolved point |
| --- | --- | --- |
| Keep existing syntax; improve completion and presentation | One source form; no grammar change | Declaration and requirement remain separated |
| Allow inline requirements alongside the existing region | Short requirements stay near their parameters; complex contracts retain a home | Two surface forms; placement and formatting policy needed |
| Replace the existing region with header constraints | One location for the public contract | The owner's example alone cannot express every existing subject, Origin relation or attached effect list |

The working recommendation is coexistence with one semantic constraint path, not a
Callable-specific feature. This is advice, not an owner decision. A full header redesign
would be a separate, larger choice.

Principle 1 needs a demonstrated practical benefit for the second surface form.
Principle 2 favors declaration-local scope with no order-dependent proof.
Principle 3 is preserved only if every requirement remains explicit.
Principle 4 requires diagnostics and source edits to retain the written constraint's location.

## 4. Decisions required before adoption

| Question | Working recommendation / issue |
| --- | --- |
| Which declarations? | Evaluate every owner of `GenericParameters`: functions, structures, enums, Contracts and function requirements. Avoid an accidental function-only rule; decide explicit specialization and override interactions under their existing contracts. |
| Which parameters? | Ordinary Type parameters have an unambiguous subject. `s/T` introduces a binder and a target: a suffix must not silently choose between `s`, `T` and `s/T`. `length N` has no equivalent ordinary Type requirement. Decide these explicitly; the simple example settles neither. |
| Which requirement expressions? | Reuse existing `IsRequirement` and subject legality, including `and`, `or`, `not`, grouping and Type identity. Restricting this to Callable or Contract names would introduce a second requirement language. |
| Which names are in scope? | Prefer the same declaration-wide scope as existing clauses, including later parameters: evaluate `T is C<U>, U` and `F is Callable<(T) -> T>, T`. Binding order must not change legality. Check self-reference and cycles under existing proof rules. |
| Can forms mix? | Prefer conjunction with existing clauses. Decide repeated clauses, equivalent clauses and contradictions by the existing constraint rules; do not silently invent a new duplicate-error policy. |
| What about projections and `Self`? | Keep subjects such as `T.Item`, complete constructed Types and `Self` in the existing region unless a general header design is chosen. Inline `T` constrains `T` only. |
| How are Callable effects attached? | Existing indented `effect` lists belong to a particular clause. Do not omit effects or infer attachment from another same-subject clause. Keeping the entire affected Callable clause in the existing region is the simplest candidate. |
| What is canonical source style? | Decide whether inline syntax is optional or preferred for a defined class of clauses. Long Callable signatures can make headers harder to scan. Formatting should not move clauses or change their attachment merely to meet a line width. |

## 5. Parsing, tooling and compatibility

Before choosing grammar, examine nested `>` tokens, commas inside Callable signatures,
multiple generic parameters, grouped requirement expressions, multiline headers and
trailing commas under the existing lexical and continuation rules. Parsing must not use
type checking to choose a meaning. A tentative extension for ordinary parameters is
`Name ("is" IsRequirement)?`; this is not a complete adopted grammar.

Diagnostics must point to the written requirement and preserve related locations.
AST inspection and edits must distinguish its source placement while consumers receive
the same semantic constraints. Do not create a second proof or inference path. The exact
syntax representation and owning compiler changes require code analysis before implementation;
this proposal does not authorize a new IR field, component or vocabulary entry.

Coexistence aims to preserve existing source behavior. Verify that claim against parsing,
declaration identity and constraint comparison rather than assuming sugar is risk-free.
No runtime or allocation change is intended; compile-time size and performance are unmeasured.
No automatic source migration, STYLE revision or GUIDE/SPECJ synchronization is authorized.

## 6. Adoption checks and prospective mapping

Before adoption, compare the original and proposed forms for ordinary capabilities,
Callable receiver modes, multiple constraints, Type identity, forward parameter references,
contradictions and cyclic premises. Include nested signatures, malformed headers, every
admitted declaration kind, mixed placement and clauses with effects. Verify equivalent
acceptance, inferred signatures and semantic diagnostics; source locations should differ
only as their written positions require. These are future checks, not executed tests.

If adopted, update the affected authoritative definitions together: SPEC §7.5,
§§8.1–8.3 and Appendix F; inspect declaration forms in §6 and §8.4, Callable/effect rules
in §§8.10–8.11, inference §10.5 and compiler-service syntax contracts §23 for affected facts.
Implementation tests and examples must follow the chosen scope. Other documents retain
their existing update conditions.

No proposal section is integrated or frozen. Adoption, implementation scope and the
decisions in §4 remain with the owner.
