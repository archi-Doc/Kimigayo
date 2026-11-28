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
            // Not yet inferred and not held: an image formed over it holds for its call only (unkeyedReads; design R3.1.4, CC-50 N1).
            this.unkeyedReads += resolved.Pending && !resolved.Held ? 1 : 0;

            // A projection is itself abstract.
            return resolved with { Pending = resolved.Pending || subject.HasAbstractPart || qualifier is { HasAbstractPart: true }, Completion = completion };
        }

        return resolved with { Type = this.SubstituteStoredOrigins(binding, projection.Symbol!.Declaration, (BoundOrigin[])projection.OriginArguments) };
    }

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
            this.SelectConformance(member, contract, owner.Scope, site, out _) is not { } identity)
        {
            return null; // a Contract reference with Type arguments selects by reference (SelectConformance, a proof): no ground (named limit)
        }

        BoundType? found = null;
        for (var i = 0; i < this.impls.PathsOf(identity).Count; i++)
        {
            var path = this.impls.PathsOf(identity)[i];
            if (path.InheritedFrom is not null || path.Premises is not null || this.AssociatedInPath(path, member, associated, qualifier, path.Scope, site) is not { } selected)
            {
                continue;
            }

            var answer = this.ResolveAssociated(path, selected);
            var binding = answer.Type;
            var instance = binding is null ? null : this.StoredType(this.SubstituteStoredOrigins(binding, associated.Declaration, (BoundOrigin[])projection.OriginArguments), member);
            pending |= instance is null && (binding is not null || answer.Pending); // StoredType's null (a refused instance) is pending, as ContractTypeCore's route
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
    // evidence, a path the evidence holds pending (PathConditions) names no Type: the projection stays itself there, its outcome Held (the
    // held mark, EvidenceProjection), and only a judgment that reads that part reads the obligation pending. A formation read uses dev's
    // deferred binding, whose retained obligation the written site's formation proves after the conformance holds.
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
                    var found = this.ResolveAssociated(path, selected);
                    return found.Type is not { } type ? found : new(type, receiver, false, path, selected, Completion.Provisional);
                }
            }
        }

        var constructed = qualifier is { Kind: BoundTypeKind.Constructed };
        if (receiver.Symbol is null ||
            this.SelectConformance(receiver, associated.Scope.Owner.BoundSymbol!, constructed ? qualifier : null, scope, site, out _) is not { } identity)
        {
            return new(null, null, pending, null, default, named || (receiver.Symbol is not null && constructed) ? Completion.Provisional : Completion.Complete);
        }

        (BoundType Binding, BoundConformancePath Path, BoundRequirement Identity)? available = null;
        (BoundType Binding, BoundConformancePath Path, BoundRequirement Identity)? unproven = null;
        var conflicting = false;
        var heldRoute = false;
        var undecided = false;
        for (var i = 0; i < this.impls.PathsOf(identity).Count; i++)
        {
            var path = this.impls.PathsOf(identity)[i];
            var condition = this.PathConditions(path, receiver, scope, site, out var held);
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
                heldRoute = true;
                continue;
            }

            var answer = this.ResolveAssociated(path, selected);
            if (answer.Type is not { } binding)
            {
                pending |= answer.Pending;
                undecided |= answer.Undecided;
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
            : new(null, null, pending, null, default, Completion.Provisional) { Held = heldRoute, Undecided = undecided };
    }

    // SPEC 8.2.3, 8.6.1, 8.6.3, 8.3.3 Finality (R7-C2e2 e2-5b, e2-3b Revision 4): a path's conditions at a receiver, and whether the
    // requirement's evidence in progress holds the path pending (`held`): the path's evidence (its conditions and, for a path they do not
    // rule out, the receiver's formation, one answer) depends on an obligation that evidence holds (EndHeldAnswer). A formation read holds
    // none, so it reads the conditions alone. Its `held` is the held mark's one source (ProjectionOutcome.Held, EvidenceProjection).
    private ConstraintProof PathConditions(BoundConformancePath path, BoundType receiver, BindingScope scope, QuerySite site, out bool held)
    {
        held = false;
        if (this.FormationRead)
        {
            return this.PathEvidence(path, receiver, scope, site);
        }

        var mark = this.BeginAnswer();
        var condition = this.PathEvidence(path, receiver, scope, site);
        var evidence = condition is ConstraintProof.Proven or ConstraintProof.Unknown ? CombineProof(condition, this.InstanceFormation(receiver, scope, site), true) : condition;
        held = this.EndHeldAnswer(mark, evidence);
        return condition;
    }

    // SPEC 8.3.3 Finality, 8.6.3, 8.3.1 (R7-C2e2 e2-5b, design R3.1; DECISIONS R7-M47): a comparison's side `type` as the requirement's
    // evidence in progress reads it, the one answer the relations' projection arms ask. Held (true): a projection that evidence keeps as
    // itself because a path its receiver meets rests on an obligation the evidence holds pending (ProjectionOutcome.Held, from
    // PathConditions' `held`); such a part is neither its binding nor absent: a relation reading it holds and binds nothing, and its
    // reader's verdict is Unknown. Else `image`: a projection whose specification names a Type, as its image in the comparison's scope
    // (ContractType, the evidence query's entry), which the relation reads in its place. The kind test's rule: an image that is itself a
    // projection gives no image, so the relation reads the written side. It is also the relation walk's termination guarantee: an image
    // read in the side's place that were a projection would ask this answer again on the same kind, without end on cyclic bindings. Else
    // neither, which keeps the side's verdict (Refuted where a relation reads it): a projection that fails, has no conformance or is not
    // yet inferred, any other Type, and every side in a formation read (it holds none: the bracket is ConditionalConformances'
    // BeginEvidence and EndEvidence, read through FormationRead). Asked inside the evidence that kept the part, so it reads the same open
    // and held obligations.
    private bool EvidenceProjection(BoundType type, BindingScope scope, QuerySite site, out BoundType? image)
    {
        image = null;
        if (this.FormationRead || type.Kind != BoundTypeKind.AssociatedProjection)
        {
            return false;
        }

        var outcome = this.ProjectionSpecification(type, scope, site);
        image = !outcome.Held && outcome.Type is not null && this.ContractType(type, scope) is { Kind: not BoundTypeKind.AssociatedProjection } formed ? formed : null;
        return outcome.Held;
    }

    // SPEC 8.6.3, 8.3.3 Finality (R7-C2e2 e2-5b, design R3.3.4 (H), CC-57 item 5): whether a comparison's pair of the implementation's
    // `actual` and the requirement's `expected` holds, the one answer the judge's and CollectOriginInference's projection arms read. A
    // pair that is one Type under the comparison's binders (SignatureEquals, its Origins excluded) is not held: its reader relates it
    // structurally, Origins included. Else a held side (EvidenceProjection) holds the pair; where neither does, each side's image (null:
    // none) is read in its place. Outside a comparison's scope nothing holds and no side has an image.
    private bool HeldPair(BoundType actual, BoundType expected, in CallableInstance comparison, QuerySite site, out BoundType? actualImage, out BoundType? expectedImage)
    {
        actualImage = null;
        expectedImage = null;

        // A comparison without binders compares slots by identity (SignatureSlotEquals).
        return comparison.Scope is { } scope && !SignatureEquals(actual, expected, comparison.ActualBinder!, comparison.ExpectedBinder!) &&
            (this.EvidenceProjection(actual, scope, site, out actualImage) || this.EvidenceProjection(expected, scope, site, out expectedImage));
    }

    // SPEC 8.5.5, 8.6.3 (R7-C2e2 e2-5b, design R3.3.5, CC-57 item 3): whether `type` is a projection whose binding depends on a missing
    // associated Type of the current definition (ProjectionOutcome.Undecided), the one fact an identification reads as Unknown at that
    // node (SignatureRelation). No receiver test, no held mark (HeldPair's) and no pass-wide flag decide it.
    private bool UndecidedProjection(BoundType type, BindingScope scope, QuerySite site)
        => type.Kind == BoundTypeKind.AssociatedProjection && this.ProjectionSpecification(type, scope, site).Undecided;

    // SPEC 8.9.2 (DECISIONS BU-50, revised by bu55): two conformance paths of a receiver agree on an associated Type when their bindings are
    // one Type as written or as the receiver's Types. Each binding is in the subject declaration's terms, normalized under its own path's
    // conditions only: OwningIterator's direct `Self is LendingIterator` binds `I.(LendingIterator).LentItem(step)` and its refinement path
    // through `Self is Iterator when I is Iterator` binds `I.Item` (Iterator's `LendingIterator.LentItem(step) is Item`), both i32 under
    // OwningIterator<Counter>. The instances are judged by SameConformanceType, as the declaration's agreement check judges two paths.
    private bool PathsAgree(BoundType left, BoundType right, BoundType receiver, BindingScope scope, QuerySite site)
        => this.SameCompleteType(left, right) ||
            (this.StoredType(left, receiver) is { } x && this.StoredType(right, receiver) is { } y && this.SameConformanceType(x, y, scope, site));

    // ProjectionSpecification's one outcome (DECISIONS BU-50): the Type, with the Subject it is instantiated under (none for a builtin's
    // complete Type), the conformance path it comes from (its Scope the binding's conformance scope) and the path's identity it binds (a
    // binding's node, DECISIONS BU-65); else Pending (nothing decides it yet) or none; with its Completion (W5). A path's binding answer
    // (ResolveAssociated(path)) is one too: its Type, Pending and Undecided, the route naming the Subject, Path and Identity.
    private readonly record struct ProjectionOutcome(BoundType? Type, BoundType? Subject, bool Pending, BoundConformancePath? Path, BoundRequirement Identity, Completion Completion)
    {
        // R7-C2e2 e2-5b: no Type because the requirement's evidence in progress holds a path the receiver meets pending (PathConditions'
        // `held`), so Pending too; set only by ResolveAssociated's held path, never in a formation read (EvidenceProjection reads it).
        internal bool Held { get; init; }

        // R7-C2e2 e2-5b (design R3.3.5, CC-57 item 3, CC-63): no Type because a route the receiver meets has a binding nothing decides
        // yet (a missing associated Type of the current definition, SPEC 8.5.5), published by ResolveAssociated(path) from its own arm,
        // never from the pass-wide flag or a held path (UndecidedProjection reads it). Pending's contract (the pass's evidence in
        // progress) is another: neither implies the other.
        internal bool Undecided { get; init; }
    }
}
