// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Owns a projection's specification (SPEC 8.5.2, 8.6.1, 8.9.2): the one outcome of what Type an associated projection names on its
// receiver, through the builtin routes, a chain scope's conformance path naming the receiver, or the receiver's conformances registered
// by declaration or selected by a bound reference, whose paths must agree (PathsAgree). R7-C2c2 c2-4's UnconditionalBinding is the
// unconditional, proof-free restriction of the by-declaration route, with the same qualifier selection, which the identity table's
// grounds and ContractTypeCore's Member specification read. Reads the associated bindings
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
    // W5 (ContractSolver's Completion): Complete only where no route applies (each route's static gate, no chain path naming the
    // receiver, no registered conformance for it), Provisional past any gate.
    private ProjectionOutcome ProjectionSpecification(BoundType projection, BindingScope scope, QuerySite site)
    {
        var builtin = this.BuiltinArithmeticOutput(projection, scope, site, out var completion);
        if (builtin is null)
        {
            builtin = this.FixedArrayIteratorType(projection, out var iterator);
            completion = Meet(completion, iterator);
        }

        if (builtin is not null)
        {
            return new(builtin, null, false, null, default, completion);
        }

        var subject = projection.Components[0];
        var qualifier = projection.Components.Count == 2 ? projection.Components[1] : null;
        var resolved = this.ResolveAssociated(subject, projection.Symbol!, scope, site, qualifier);
        completion = Meet(completion, resolved.Completion);
        if (resolved.Type is not { } binding)
        {
            // A projection is itself abstract.
            return resolved with { Pending = resolved.Pending || subject.HasAbstractPart || qualifier is { HasAbstractPart: true }, Completion = completion };
        }

        return resolved with { Type = this.SubstituteStoredOrigins(binding, projection.Symbol!.Declaration, (BoundOrigin[])projection.OriginArguments) };
    }

    // R7-C6 a's census (PLAN G651 a1), C6a-1b's frontier: ProjectionSpecification's callers in SubstituteType's normalization family
    // (ContractTypeCore, CompletedProjection), whose site is ContractType's and StoredType's input, read as Nested (no frame of their own,
    // as before QuerySite). Removal: C6a-1b names it with SubstituteType's family.
    [Transitional("C6a-1b", Reason = "R7-C6 a's census (PLAN G651 a1): ProjectionSpecification's callers in SubstituteType's normalization family that do not name their site yet; C6a-1b names it and deletes every member carrying this stage.")]
    private ProjectionOutcome ProjectionSpecification(BoundType projection, BindingScope scope) => this.ProjectionSpecification(projection, scope, QuerySite.Nested);

    // R7-C6 a's census (PLAN G651 a1), C6a-1b's frontier: UnconditionalBinding's callers in SubstituteType's normalization family and the
    // identity table (ContractTypeCore, IdentityClasses), read as Nested (no frame of their own, as before QuerySite). Removal: C6a-1b
    // names it with SubstituteType's family.
    [Transitional("C6a-1b", Reason = "R7-C6 a's census (PLAN G651 a1): UnconditionalBinding's callers in SubstituteType's normalization family and the identity table that do not name their site yet; C6a-1b names it and deletes every member carrying this stage.")]
    private BoundType? UnconditionalBinding(BoundType member, BoundType projection, out bool pending) => this.UnconditionalBinding(member, projection, QuerySite.Nested, out pending);

    // R7-C2c2 c2-4 (design RB3, S3; SPEC 8.2.3 "the available completed associated-Type bindings" are grounds): the binding a
    // constructed member's own unconditional conformance (no `when`, not inherited) declares for a projection, as the member's Type;
    // null without one or where two such paths disagree. Declarations only (no identity, no proof); `pending` when a binding is not yet
    // complete. Its readers are the identity table's binding grounds and ContractTypeCore's specification on a Member, so one
    // projection has one form; a conditional conformance's binding is no ground and completes nothing here (N8's named limit).
    private BoundType? UnconditionalBinding(BoundType member, BoundType projection, QuerySite site, out bool pending)
    {
        pending = false;
        var associated = projection.Symbol!;
        var qualifier = projection.Components.Count == 2 ? projection.Components[1] : null;
        if (member.Symbol is not { } owner || qualifier is { Kind: BoundTypeKind.Constructed } || associated.Scope.Owner.BoundSymbol is not { } contract ||
            this.ConformanceByDeclaration(owner, contract, out _) is not { } identity)
        {
            return null; // a Contract reference with Type arguments selects by reference (ConformanceByReference, a proof): no ground (named limit)
        }

        BoundType? found = null;
        for (var i = 0; i < this.impls.PathsOf(identity).Count; i++)
        {
            var path = this.impls.PathsOf(identity)[i];
            if (path.InheritedFrom is not null || path.Premises is not null || this.AssociatedInPath(path, member, associated, qualifier, path.Scope, site) is not { } selected)
            {
                continue;
            }

            var binding = this.ResolveAssociated(path, selected, out var bindingPending);
            var instance = binding is null ? null : this.StoredType(this.SubstituteStoredOrigins(binding, associated.Declaration, (BoundOrigin[])projection.OriginArguments), member);
            pending |= instance is null && (binding is not null || bindingPending); // StoredType's null (a refused instance) is pending, as ContractTypeCore's route
            if (instance is null)
            {
                continue;
            }

            // Interned: equal instances are one object.
            if (found is not null && !ReferenceEquals(found, instance))
            {
                return null;
            }

            found = instance;
        }

        return found;
    }

    // `qualifier` is the declaring Contract reference of the projection (SPEC 8.6.1); a bound reference of a Contract with Type
    // arguments selects that conformance, and a Contract without them has one conformance per Type. `pending`: a null result is not final
    // (as the path's ResolveAssociated); once the pass's evidence is complete, a Type without the conformance gives none. The paths whose
    // conditions the receiver meets must agree (PathsAgree), else none. The path the binding comes from is returned with it, its Scope the
    // conformance scope (SPEC 8.8.2, D + P) the binding was formed in, and the path's associated identity it binds, as the outcome's
    // Type (the binding in the subject declaration's terms), Path and Identity. W5: Complete when no chain path names the receiver and no
    // conformance is registered for it by declaration. SPEC 8.3.3 Finality, 8.6.3 (R7-C2e2 e2-3b Revision 4): in a requirement's
    // evidence, a path the evidence holds pending (PathConditions) names no Type: the projection stays itself there, and only a judgment
    // that reads that part reads the obligation pending. A formation read uses dev's deferred binding, whose retained obligation the
    // written site's formation proves after the conformance holds.
    private ProjectionOutcome ResolveAssociated(BoundType receiver, BindingSymbol associated, BindingScope scope, QuerySite site, BoundType? qualifier)
    {
        var pending = this.AssociatedEvidencePending;
        var named = false;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.ConformancePath is { } path && ReferenceEquals(path.Type, receiver.Symbol))
            {
                named = true;
                if (this.AssociatedInPath(path, receiver, associated, qualifier, scope, site) is { } selected)
                {
                    var found = this.ResolveAssociated(path, selected, out pending);
                    return found is null ? new(null, null, pending, null, default, Completion.Provisional) : new(found, receiver, false, path, selected, Completion.Provisional);
                }
            }
        }

        var constructed = qualifier is { Kind: BoundTypeKind.Constructed };
        if (receiver.Symbol is not { } owner ||
            (!constructed ? this.ConformanceByDeclaration(owner, associated.Scope.Owner.BoundSymbol!, out _)
                : this.ConformanceByReference(owner, receiver, associated, qualifier!, scope, site)) is not { } identity)
        {
            return new(null, null, pending, null, default, named || (receiver.Symbol is not null && constructed) ? Completion.Provisional : Completion.Complete);
        }

        (BoundType Binding, BoundConformancePath Path, BoundRequirement Identity)? available = null;
        (BoundType Binding, BoundConformancePath Path, BoundRequirement Identity)? unproven = null;
        var conflicting = false;
        for (var i = 0; i < this.impls.PathsOf(identity).Count; i++)
        {
            var path = this.impls.PathsOf(identity)[i];
            var condition = this.PathConditions(path, receiver, scope, out var held);
            if (condition == ConstraintProof.Error)
            {
                return new(null, null, pending, null, default, Completion.Provisional);
            }

            if (condition == ConstraintProof.Refuted || this.AssociatedInPath(path, receiver, associated, qualifier, scope, site) is not { } selected)
            {
                continue;
            }

            if (held)
            {
                pending = true;
                continue;
            }

            if (this.ResolveAssociated(path, selected, out var bindingPending) is not { } binding)
            {
                pending |= bindingPending;
                continue;
            }

            if (condition == ConstraintProof.Proven)
            {
                if (available is { } agreed && !this.PathsAgree(agreed.Binding, binding, receiver, scope, site))
                {
                    return new(null, null, pending, null, default, Completion.Provisional);
                }

                available = (binding, path, selected);
            }

            conflicting |= unproven is { } other && !this.PathsAgree(other.Binding, binding, receiver, scope, site);
            unproven = (binding, path, selected);
        }

        // Explicit header metadata may normalize a common binding before witnesses are ready.
        // The separately retained projection obligation must still prove conformance at finalization.
        return (available ?? (conflicting ? null : unproven)) is { } chosen ? new(chosen.Binding, receiver, false, chosen.Path, chosen.Identity, Completion.Provisional)
            : new(null, null, pending, null, default, Completion.Provisional);
    }

    // SPEC 8.2.3, 8.6.1, 8.6.3, 8.3.3 Finality (R7-C2e2 e2-5b, e2-3b Revision 4): a path's conditions at a receiver, and whether the
    // requirement's evidence in progress holds the path pending (`held`): the path's evidence (its conditions and, for a path they do not
    // rule out, the receiver's formation, one answer) depends on an obligation that evidence holds (EndHeldAnswer). A formation read holds
    // none, so it reads the conditions alone.
    private ConstraintProof PathConditions(BoundConformancePath path, BoundType receiver, BindingScope scope, out bool held)
    {
        held = false;
        if (this.FormationRead)
        {
            return this.PathEvidence(path, receiver, scope);
        }

        var mark = this.BeginAnswer();
        var condition = this.PathEvidence(path, receiver, scope);
        var evidence = condition is ConstraintProof.Proven or ConstraintProof.Unknown ? CombineProof(condition, this.InstanceFormation(receiver, scope), true) : condition;
        held = this.EndHeldAnswer(mark, evidence);
        return condition;
    }

    // SPEC 8.9.2 (DECISIONS BU-50, revised by bu55): two conformance paths of a receiver agree on an associated Type when their bindings are
    // one Type as written or as the receiver's Types. Each binding is in the subject declaration's terms, normalized under its own path's
    // conditions only: OwningIterator's direct `Self is LendingIterator` binds `I.(LendingIterator).LentItem(step)` and its refinement path
    // through `Self is Iterator when I is Iterator` binds `I.Item` (Iterator's `LendingIterator.LentItem(step) is Item`), both i32 under
    // OwningIterator<Counter>. The instances are judged by SameConformanceType, as the declaration's agreement check judges two paths.
    private bool PathsAgree(BoundType left, BoundType right, BoundType receiver, BindingScope scope, QuerySite site)
        => this.SameCompleteType(left, right) ||
            (this.StoredType(left, receiver) is { } x && this.StoredType(right, receiver) is { } y && this.SameConformanceType(x, y, scope, site));

    // The one conformance of `owner` whose bound reference, instantiated for `receiver` (`Indexable<K>` of `Keyed<isize>`),
    // names `qualifier` in `scope` (NamesReference) as the declaring reference of `associated`.
    private BoundConformance? ConformanceByReference(BindingSymbol owner, BoundType receiver, BindingSymbol associated, BoundType qualifier, BindingScope scope, QuerySite site)
    {
        if (this.ConformancesOf(owner) is not { } identities)
        {
            return null;
        }

        BoundConformance? found = null;
        for (var i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            if (identity.Contract.Type is { } reference && ReferenceEquals(reference.Symbol?.Declaration, associated.Scope.Owner) &&
                (ReferenceEquals(reference, qualifier) || this.NamesReference(reference, receiver, qualifier, scope, site)))
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
    // binding's node, DECISIONS BU-65); else Pending (nothing decides it yet) or none; with its Completion (W5).
    private readonly record struct ProjectionOutcome(BoundType? Type, BoundType? Subject, bool Pending, BoundConformancePath? Path, BoundRequirement Identity, Completion Completion);
}
