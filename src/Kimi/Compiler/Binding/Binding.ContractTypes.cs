// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns a Type's Contract environment rewrite (ContractType): a Contract's Self replaced by the conforming Type, a bound Contract
// reference's arguments substituted, and completed associated identities normalized in a scope, without member selection, each distinct
// part once per call (contractParts); and a declaration's Contract reference instantiated for a conforming Type (InstantiatedContract).
// Reads the conformance tables and the scope's facts; publishes each answer's hops (ResolutionHops): the conformance paths a projection's
// specification resolved it through, outermost first, each with the step it resolved, written by the run that made the image and carried
// by the memo and the associated collection's batch, so a hit never resolves again (DECISIONS BU-76).
public sealed partial class Binding
{
    // The images of ContractType's queries (DECISIONS BU-64): one outermost call is a query, its scope, Self, normalize flag,
    // activeRequirementContract and normalizingAssociated's set fixed, so each distinct part is rewritten once per query. The set changes
    // only by a push after a part's components are rewritten, popped before that part returns, and what runs under the push enters
    // ContractType afresh: a nested query, which can only evict this query's entries, recomputed then (TypeMemo's contract).
    private readonly TypeMemo<BoundType, (BoundType Image, ResolutionHops? Hops, Completion Completion)> contractParts = new();

    // The hop chains, one per path, step and inner chain: kept for the Binding's lifetime, as the paths they name (ImplTable), so
    // a warm pass allocates none.
    private readonly Dictionary<(BoundConformancePath Path, BoundType Step, ResolutionHops? Inner), ResolutionHops> hopChains = new();

    /// <summary>Substitutes Contract Self and normalizes completed associated identities without performing member selection.</summary>
    /// <remarks>A Type with neither an abstract part nor an Origin is its own image: the environment substitutes slots and Origins, a
    /// Contract's Self is a Parameter and normalization rewrites only abstract kinds, so a shared closed part is never walked
    /// (DECISIONS BU-19). Any other part is rewritten once per call (contractParts, DECISIONS BU-64).</remarks>
    private BoundType ContractType(BoundType type, BindingScope scope, BoundType? self = null, bool normalize = true)
        => this.ContractTypeAnswer(type, scope, self, normalize).Image;

    // ContractType's answer with the hops it resolved through (the component summaries' walk) and its Completion (W4, ContractSolver's):
    // Complete for its own image, else ContractTypeCore's; the associated collection's batch is Provisional whatever it stored.
    private (BoundType Image, ResolutionHops? Hops, Completion Completion) ContractTypeAnswer(BoundType type, BindingScope scope, BoundType? self = null, bool normalize = true)
    {
        if (!type.HasAbstractPart && !type.CarriesOrigin)
        {
            return (type, null, Completion.Complete);
        }

        var query = this.contractParts.Begin(this.storageVersion);
        return this.collectingAssociated is null ? this.ContractTypeCore(type, scope, self, normalize, query) : this.BatchedContractType(type, scope, self, normalize, query);
    }

    // SPEC 8.4, 8.3.2: a Contract reference written in a Contract's declaration (a clause's Contract, an ancestor) for the conforming Type
    // `self`, itself where unchanged. With a bound `reference` (the premise supply's), the Self the declaration writes is `self` first and
    // the reference's arguments come after, so a Self they carry from the premise's context stays the context's (DECISIONS R4-M166).
    private BindingSymbol InstantiatedContract(BindingSymbol contract, BindingScope scope, BoundType? self, bool normalize, BindingSymbol? reference)
    {
        if (!IsBoundContractReference(contract))
        {
            return contract;
        }

        var type = contract.Type!;
        var image = reference is null || self is null ? this.ContractType(type, scope, self, normalize)
            : this.ContractType(this.SubstituteContractReference(this.WithConformingSelf(type, self), reference, out _), scope, null, normalize);
        return ReferenceEquals(image, type) ? contract : this.BoundContractReference(image);
    }

    // `type` with each Contract's Self in it as `self` and nothing else rewritten: a declaration's Types name only its own Self and its
    // ancestors', each the conforming Type (SPEC 8.4).
    private BoundType WithConformingSelf(BoundType type, BoundType self)
    {
        if (IsContractSelf(type))
        {
            return self;
        }

        var count = type.Components.Count;
        if (count == 0 || !type.HasAbstractPart)
        {
            return type;
        }

        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.WithConformingSelf(type.Components[i], self);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            return changed ? this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // A component of a query: the associated collection's batch answers it where one is open (unchanged); otherwise a component with
    // components is recorded, a leaf being cheaper to rewrite again than to record and a query's root met once (DECISIONS BU-55).
    private (BoundType Image, ResolutionHops? Hops, Completion Completion) ContractPart(BoundType type, BindingScope scope, BoundType? self, bool normalize, uint query)
    {
        if (!type.HasAbstractPart && !type.CarriesOrigin)
        {
            return (type, null, Completion.Complete);
        }

        if (this.collectingAssociated is not null)
        {
            return this.BatchedContractType(type, scope, self, normalize, query);
        }

        if (type.Components.Count == 0)
        {
            return this.ContractTypeCore(type, scope, self, normalize, query);
        }

        if (!this.contractParts.TryGet(type, query, out var answer))
        {
            answer = this.ContractTypeCore(type, scope, self, normalize, query);
            this.contractParts.Set(type, query, answer);
        }

        return answer;
    }

    // The associated collection's answer, by every input including the active requirement (Binding.AssociatedInference's batch);
    // Provisional (W4) whatever the batch stored, as the batch is open.
    private (BoundType Image, ResolutionHops? Hops, Completion Completion) BatchedContractType(BoundType type, BindingScope scope, BoundType? self, bool normalize, uint query)
    {
        var batch = this.collectingAssociated!;
        var key = (type, scope, self, normalize, this.activeRequirementContract);
        if (!batch.Normalized.TryGetValue(key, out var known))
        {
            var result = this.ContractTypeCore(type, scope, self, normalize, query);
            batch.Normalized[key] = known = (result.Image, result.Hops);
        }

        return (known.Image, known.Hops, Completion.Provisional);
    }

    // The image and its hops: a projection resolved by a specification adds its path and itself, its components normalized, to its
    // subject's (component 0's) hops. W4: the meet of the environment's and the requirement's substitutions, the parts', the held
    // Type's, the length's, the projection's specification (W5) and every availability the identity loop asks (W6); a Contract Self, a
    // kind not normalized and the guard's hit (normalizingAssociated, empty when a reading's formation starts) add nothing.
    private (BoundType Image, ResolutionHops? Hops, Completion Completion) ContractTypeCore(BoundType type, BindingScope scope, BoundType? self, bool normalize, uint query)
    {
        type = this.ApplyContractEnvironment(type, scope, out var completion);
        if (this.activeRequirementContract is { } bound)
        {
            type = this.SubstituteContractReference(type, bound, out var substituted); // SPEC 8.4.1, 8.6.4: the requirement of a bound reference.
            completion = Meet(completion, substituted);
        }

        // SPEC 8.4: Self of any Contract, including an ancestor whose requirement is read here, is the conforming Type. A
        // reference to a Contract, such as a projection's declaring Contract, only has its Type arguments substituted.
        if (IsContractSelf(type))
        {
            return (self ?? this.EnclosingContractSelf(scope) ?? type, null, completion);
        }

        var count = type.Components.Count;
        var components = count == 0 ? null : this.RentTypes(count);
        try
        {
            var changed = false;
            ResolutionHops? inner = null;
            for (var i = 0; i < count; i++)
            {
                var part = this.ContractPart(type.Components[i], scope, self, normalize, query);
                components![i] = part.Image;
                inner = i == 0 ? part.Hops : inner;
                completion = Meet(completion, part.Completion);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            var result = changed ? this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments) : type;
            if (TyCtxt.HeldType(type) is { } held)
            {
                // DECISIONS BU-60: the Type instance a closure's context holds is normalized with the Closure Type.
                (var image, _, var heldPart) = this.ContractTypeAnswer(held, scope, self, normalize);
                completion = Meet(completion, heldPart);
                result = ReferenceEquals(image, held) ? result : this.WithContext(result, this.types.HeldContext(type.Symbol!, image));
            }

            result = this.ContractLength(result, scope, self, normalize, out var length);
            completion = Meet(completion, length);
            if (!normalize || result.Kind is not (BoundTypeKind.AssociatedProjection or BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication) || !this.normalizingAssociated.Add((result, scope)))
            {
                return (result, null, completion);
            }

            try
            {
                if (result.Kind == BoundTypeKind.AssociatedProjection)
                {
                    var outcome = this.ProjectionSpecification(result, scope);
                    completion = Meet(completion, outcome.Completion);
                    if (outcome.Type is { } specified)
                    {
                        var hops = outcome.Path is { } path ? this.Hop(path, result, inner) : inner;

                        // A container substitution can expose another associated projection (Wrapper<S>.Element -> S.Element). Normalize
                        // that identity too; the active query above still guards recursive specifications.
                        if (outcome.Subject is not { } subject)
                        {
                            return (specified, hops, completion);
                        }

                        if (this.StoredType(specified, subject) is not { } stored)
                        {
                            return (result, hops, Completion.Provisional);
                        }

                        var instance = this.ContractTypeAnswer(stored, scope, self);
                        return (instance.Image, hops, Meet(completion, instance.Completion));
                    }
                }

                // The first available premise naming the Type completes it, the choice dev's member substitution makes (SubstituteIdentity;
                // R7-C2c2 replaces both); a Type in a final erroneous identity class stays as written (Binding.TypeIdentity).
                BoundType? chosen = null;
                for (var current = scope; current is not null; current = current.Parent)
                {
                    if (current.Constraints is not { Invalid: false } environment)
                    {
                        continue;
                    }

                    foreach (var fact in environment.Facts)
                    {
                        if (fact.Kind != ConstraintKind.TypeIdentity || !this.FactStates(fact, result, out var required))
                        {
                            continue;
                        }

                        var available = this.AvailableConstraintFact(environment, fact, out var availability);
                        completion = Meet(completion, availability);
                        if (available && required is not null && chosen is null)
                        {
                            chosen = required;
                        }
                    }
                }

                if (chosen is not null)
                {
                    var failed = this.ProjectionClassFailed(result, scope, out var guard);
                    completion = Meet(completion, guard);
                    if (!failed)
                    {
                        var identity = this.ContractTypeAnswer(chosen, scope, self);
                        return (identity.Image, null, Meet(completion, identity.Completion));
                    }
                }

                return (result, null, completion);
            }
            finally
            {
                this.normalizingAssociated.Remove((result, scope));
            }
        }
        finally
        {
            if (components is not null)
            {
                this.typeScratch.Return(components, clearArray: true);
            }
        }
    }

    // R8-W2: a fixed array whose length is an interpolation-bound term over abstract value Types, with the term's value Types normalized as
    // this rewrite normalizes the Type, a Contract's Self and a completed projection included, which a substitution leaves
    // (Binding.SubstituteLength), and the term folded again (TyCtxt.InterpolationBound); any other Type itself. A term is only a fixed
    // array's length or a substitution's length entry (Binding.InstanceLength), never an operand or an Item's length argument (R8-W2 a2).
    private BoundType ContractLength(BoundType type, BindingScope scope, BoundType? self, bool normalize, out Completion completion)
    {
        completion = Completion.Complete;
        if (type is not { Kind: BoundTypeKind.FixedArray, LengthExpression: { Values: { HasAbstractPart: true } values } term })
        {
            return type;
        }

        (var image, _, completion) = this.ContractTypeAnswer(values, scope, self, normalize);
        var length = this.types.InterpolationBound(image, term.Value);
        return this.types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [type.Components[0]], length.IsConstant ? length.Value : 0, lengthExpression: length.IsConstant ? null : length);
    }

    // The interned chain of a hop resolving step through path before inner.
    private ResolutionHops Hop(BoundConformancePath path, BoundType step, ResolutionHops? inner)
    {
        if (!this.hopChains.TryGetValue((path, step, inner), out var hop))
        {
            this.hopChains.Add((path, step, inner), hop = new(path, step, inner));
        }

        return hop;
    }

    // The conformance paths one ContractType answer resolved through, outermost hop first, each with the projection step it resolved;
    // interned (Hop), so one chain is one object. As a cause its candidates are the hops' clauses (DECISIONS BU-76).
    internal sealed class ResolutionHops(BoundConformancePath path, BoundType step, ResolutionHops? inner) : ICheckCause
    {
        public Koto Use => this.Path.Use;

        public ICheckCause? Next => this.Inner;

        public ICheckCause? ReentrySite => null;

        internal BoundConformancePath Path { get; } = path;

        // The associated projection this hop resolved, its components normalized (the component summaries' re-entry test).
        internal BoundType Step { get; } = step;

        internal ResolutionHops? Inner { get; } = inner;

        public void Rest()
        {
        }
    }
}
