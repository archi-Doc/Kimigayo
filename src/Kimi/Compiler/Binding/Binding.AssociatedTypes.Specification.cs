// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Owns a projection's specification (SPEC 8.5.2, 8.6.1, 8.9.2): the one outcome of what Type an associated projection names on its
// receiver, through the builtin routes, a chain scope's conformance path naming the receiver, or the receiver's conformances registered
// by declaration or selected by a bound reference, whose paths must agree (PathsAgree). Reads the associated bindings
// (ResolveAssociated(path), AssociatedInPath: Binding.AssociatedTypes) and the conformance tables.
public sealed partial class Binding
{
    // SPEC 8.6.1 (DECISIONS BU-50): what a projection names before its subject substitutes it, asked in the owner declaration's scope, one
    // outcome for every subject. Finality is independence of scope: a Type found there is the answer, since the declaration's premises are
    // each instance's obligations (a builtin arithmetic output or a fixed array's iterator Type, complete, no Subject; else the subject's
    // conformance binding, ResolveAssociated, an inherited conformance's under the base clause's arguments, in the subject declaration's
    // terms with the associated Type's Origin parameters bound to the projection's arguments, for the caller to instantiate under the
    // Subject: as a view, ContractTypeCore, or as a kept part, CompleteProjections); no Type is pending while the pass's evidence is
    // incomplete or the projection has an abstract part (a use scope may carry a premise the declaration's lacks: `T is PrimitiveInteger`
    // for `Wrapping<T>.Output`), else none, final, whatever the reason: the projection names no Type, and the records are the
    // declarations' and the use's (SPEC 23.1.2). No subject-kind test decides pending or none.
    private ProjectionOutcome ProjectionSpecification(BoundType projection, BindingScope scope)
    {
        if ((this.BuiltinArithmeticOutput(projection, scope) ?? this.FixedArrayIteratorType(projection)) is { } builtin)
        {
            return new(builtin, null, false, null, default);
        }

        var subject = projection.Components[0];
        var qualifier = projection.Components.Count == 2 ? projection.Components[1] : null;
        if (this.ResolveAssociated(subject, projection.Symbol!, scope, qualifier, out var pending) is not { } resolved)
        {
            // A projection is itself abstract.
            return new(null, null, pending || subject.HasAbstractPart || qualifier is { HasAbstractPart: true }, null, default);
        }

        var specified = this.SubstituteStoredOrigins(resolved.Binding, projection.Symbol!.Declaration, (BoundOrigin[])projection.OriginArguments);
        return new(specified, subject, false, resolved.Path, resolved.Identity);
    }

    // `qualifier` is the declaring Contract reference of the projection (SPEC 8.6.1); a bound reference of a Contract with Type
    // arguments selects that conformance, and a Contract without them has one conformance per Type. `pending`: a null result is not final
    // (as the path's ResolveAssociated); once the pass's evidence is complete, a Type without the conformance gives none. The paths whose
    // conditions the receiver meets must agree (PathsAgree), else none. The path the binding comes from is returned with it, its Scope the
    // conformance scope (SPEC 8.8.2, D + P) the binding was formed in, and the path's associated identity it binds.
    private (BoundType Binding, BoundConformancePath Path, BoundRequirement Identity)? ResolveAssociated(
        BoundType receiver, BindingSymbol associated, BindingScope scope, BoundType? qualifier, out bool pending)
    {
        pending = this.AssociatedEvidencePending;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.ConformancePath is { } path && ReferenceEquals(path.Type, receiver.Symbol) &&
                this.AssociatedInPath(path, receiver, associated, qualifier, scope) is { } selected)
            {
                return this.ResolveAssociated(path, selected, out pending) is { } found ? (found, path, selected) : null;
            }
        }

        if (receiver.Symbol is not { } owner ||
            (qualifier is not { Kind: BoundTypeKind.Constructed } ? this.ConformanceByDeclaration(owner, associated.Scope.Owner.BoundSymbol!, out _)
                : this.ConformanceByReference(owner, receiver, associated, qualifier, scope)) is not { } identity)
        {
            return null;
        }

        (BoundType Binding, BoundConformancePath Path, BoundRequirement Identity)? available = null;
        (BoundType Binding, BoundConformancePath Path, BoundRequirement Identity)? unproven = null;
        var conflicting = false;
        for (var i = 0; i < identity.PathStorage.Count; i++)
        {
            var path = identity.PathStorage[i];
            var condition = this.ProveConformanceConditions(path, receiver, scope);
            if (condition == ConstraintProof.Error)
            {
                return null;
            }

            if (condition == ConstraintProof.Refuted || this.AssociatedInPath(path, receiver, associated, qualifier, scope) is not { } selected)
            {
                continue;
            }

            if (this.ResolveAssociated(path, selected, out var bindingPending) is not { } binding)
            {
                pending |= bindingPending;
                continue;
            }

            if (condition == ConstraintProof.Proven)
            {
                if (available is { } agreed && !this.PathsAgree(agreed.Binding, binding, receiver, scope))
                {
                    return null;
                }

                available = (binding, path, selected);
            }

            conflicting |= unproven is { } other && !this.PathsAgree(other.Binding, binding, receiver, scope);
            unproven = (binding, path, selected);
        }

        // Explicit header metadata may normalize a common binding before witnesses are ready.
        // The separately retained projection obligation must still prove conformance at finalization.
        return available ?? (conflicting ? null : unproven);
    }

    // SPEC 8.9.2 (DECISIONS BU-50, revised by bu55): two conformance paths of a receiver agree on an associated Type when their bindings are
    // one Type as written or as the receiver's Types. Each binding is in the subject declaration's terms, normalized under its own path's
    // conditions only: OwningIterator's direct `Self is LendingIterator` binds `I.(LendingIterator).LentItem(step)` and its refinement path
    // through `Self is Iterator when I is Iterator` binds `I.Item` (Iterator's `LendingIterator.LentItem(step) is Item`), both i32 under
    // OwningIterator<Counter>. The instances are judged by SameConformanceType, as the declaration's agreement check judges two paths.
    private bool PathsAgree(BoundType left, BoundType right, BoundType receiver, BindingScope scope)
        => this.SameCompleteType(left, right) ||
            (this.StoredType(left, receiver) is { } x && this.StoredType(right, receiver) is { } y && this.SameConformanceType(x, y, scope));

    // The one conformance of `owner` whose bound reference, instantiated for `receiver` (`Indexable<K>` of `Keyed<isize>`),
    // names `qualifier` in `scope` (NamesReference) as the declaring reference of `associated`.
    private BoundConformance? ConformanceByReference(BindingSymbol owner, BoundType receiver, BindingSymbol associated, BoundType qualifier, BindingScope scope)
    {
        if (this.ConformancesOf(owner) is not { } identities)
        {
            return null;
        }

        BoundConformance? found = null;
        for (var i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            if (identity.Paths.Count != 0 && identity.Contract.Type is { } reference && ReferenceEquals(reference.Symbol?.Declaration, associated.Scope.Owner) &&
                (ReferenceEquals(reference, qualifier) || this.NamesReference(reference, receiver, qualifier, scope)))
            {
                if (found is not null && !ReferenceEquals(found, identity))
                {
                    return null;
                }

                found = identity;
            }
        }

        return found;
    }

    // ProjectionSpecification's one outcome (DECISIONS BU-50): the Type, with the Subject it is instantiated under (none for a builtin's
    // complete Type), the conformance path it comes from (its Scope the binding's conformance scope) and the path's identity it binds (a
    // binding's node, DECISIONS BU-65); else Pending (nothing decides it yet) or none.
    private readonly record struct ProjectionOutcome(BoundType? Type, BoundType? Subject, bool Pending, BoundConformancePath? Path, BoundRequirement Identity);
}
