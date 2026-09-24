// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConstraint Proposition, BindingScope Scope, bool Derivation), CapabilityWork> capabilityNodes = new();
    private readonly HashSet<(CapabilityWork Source, CapabilityWork Consumer)> capabilityEdges = new();
    private readonly Queue<CapabilityWork> capabilityQueue = new();
    private readonly Dictionary<BoundType, int> capabilityTypeDepths = new(ReferenceEqualityComparer.Instance);
    private CapabilityWork? capabilityConsumer;
    private bool capabilitiesReady;
    private BindingMode capabilityMode;

    /// <summary>Queries compiler-intrinsic Copy for a complete Type under lexical Constraints.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The proof result, without choosing any acquisition operation.</returns>
    public ConstraintProof ProveCopy(BoundType type, Koto context)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Copy)), this.ConstraintScope(context));

    /// <summary>Queries the recursive Owned guarantee, independently of heap/global borrow restrictions.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The proof result; unresolved Origins remain Unknown.</returns>
    public ConstraintProof ProveOwned(BoundType type, Koto context)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Owned)), this.ConstraintScope(context));

    /// <summary>Queries complete owner Core evidence without inspecting stored fields.</summary>
    /// <param name="type">The normalized complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The four-valued Sealed proof.</returns>
    public ConstraintProof ProveSealed(BoundType type, Koto context)
        => this.RequestCapability(type, this.Library.Sealed, this.ConstraintScope(context));

    private static bool TryLeafCapability(BoundType type, IntrinsicKind kind, out ConstraintProof result)
    {
        result = ConstraintProof.Unknown;
        if (kind == IntrinsicKind.ObjectPayload)
        {
            if (type.Symbol?.Declaration.BindingState == BindingState.Invalid)
            {
                result = ConstraintProof.Error;
                return true;
            }

            // SPEC 8.4.7.2: symbolic targets and a Contract's Self are decided by their premises.
            if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection ||
                type.Symbol?.Declaration is ContractKoto)
            {
                return false;
            }

            // The judgment is shallow: outer owner Semantics, a Core other than Never, and no inherited opt-out.
            result = type.Semantics != SemanticsKind.Owner || ReferenceEquals(type, BoundType.Never) || type.Symbol?.ObjectPayloadOptOut is not null
                ? ConstraintProof.Refuted : ConstraintProof.Proven;
            return true;
        }

        if (kind == IntrinsicKind.Sealed)
        {
            if (type.Symbol?.Declaration.BindingState == BindingState.Invalid)
            {
                result = ConstraintProof.Error;
                return true;
            }

            if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection)
            {
                return false;
            }

            result = type.Semantics != SemanticsKind.Owner || ReferenceEquals(type, BoundType.Never) ||
                type.Symbol?.Declaration is ContractKoto ||
                (type.Symbol?.Declaration is StructKoto structure && (structure.Modifier & ModifierKind.Open) != 0)
                ? ConstraintProof.Refuted : ConstraintProof.Proven;
            return true;
        }

        if (type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary && kind == IntrinsicKind.Copy)
        {
            result = ConstraintProof.Refuted; // SPEC 4.5: Array is Non-Copy for every element Type.
            return true;
        }

        if (type.Semantics == SemanticsKind.Owner && type.Symbol?.LibraryDeclaration is KimiDeclarationId.FixedBuffer or KimiDeclarationId.WriteWindow or KimiDeclarationId.Utf8Writer)
        {
            result = ConstraintProof.Refuted; // Verified raw storage retains an exclusive external dependency.
            return true;
        }

        if (type.Kind == BoundTypeKind.ResolvedRange || (type.Kind == BoundTypeKind.Slice && kind == IntrinsicKind.Copy))
        {
            result = kind == IntrinsicKind.Copy || type.Kind == BoundTypeKind.ResolvedRange ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        if (ReferenceEquals(type, BoundType.Never))
        {
            return true;
        }

        if (type.Kind == BoundTypeKind.Primitive || (type.Kind == BoundTypeKind.Function && kind == IntrinsicKind.Copy))
        {
            result = kind == IntrinsicKind.Owned || (type.Kind == BoundTypeKind.Primitive && type.Name != "string") ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        if (type.Kind == BoundTypeKind.Semantics && kind == IntrinsicKind.Copy)
        {
            result = type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Unsafe ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        return false;
    }

    private static FunctionTypeKoto? OwnFunctionBinder(BoundType type, BoundType signature)
    {
        if (type.Origin is { Kind: OriginKind.Input, Binder: FunctionTypeKoto binder } && ReferenceEquals(binder.BoundType, signature))
        {
            return binder;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (OwnFunctionBinder(type.Components[i], signature) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private int CapabilityTypeDepth(BoundType type)
    {
        if (this.capabilityTypeDepths.TryGetValue(type, out var depth))
        {
            return depth;
        }

        depth = 1;
        for (var i = 0; i < type.Components.Count; i++)
        {
            depth = Math.Max(depth, 1 + this.CapabilityTypeDepth(type.Components[i]));
        }

        this.capabilityTypeDepths.Add(type, depth);
        return depth;
    }

    private void ResetCapabilities(BindingMode mode)
    {
        this.capabilitiesReady = false;
        this.capabilityMode = mode;
        this.ClearCapabilityResults();
    }

    private void ClearCapabilityResults()
    {
        this.capabilityQueue.Clear();
        this.capabilityEdges.Clear();
        this.capabilityConsumer = null;
        foreach (var work in this.capabilityNodes.Values)
        {
            work.Reset();
        }
    }

    private ConstraintProof RequestCapability(BoundType type, BindingSymbol intrinsic, BindingScope scope, bool derivation = false)
    {
        if (UnresolvedConstraintType(type))
        {
            return InvalidConstraintType(type) ? ConstraintProof.Error : ConstraintProof.Unknown;
        }

        if (!this.capabilitiesReady)
        {
            if (this.running && this.kimiValid && !derivation && TryLeafCapability(type, intrinsic.Intrinsic, out var concrete))
            {
                return concrete;
            }

            return ConstraintProof.Unknown;
        }

        if (!this.kimiValid)
        {
            return ConstraintProof.Error;
        }

        if (!derivation && TryLeafCapability(type, intrinsic.Intrinsic, out var leaf))
        {
            return leaf;
        }

        // Expanding recursive generic storage must not materialize an infinite sequence of Types.
        // Exact recursive instances use graph edges; changing instances retain an explicit unknown.
        if (!derivation && type.Symbol is not null)
        {
            var growing = false;
            var depth = 0;
            for (var ancestor = this.capabilityConsumer; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor.Type.Symbol, type.Symbol) && !ReferenceEquals(ancestor.Type, type))
                {
                    depth = depth == 0 ? this.CapabilityTypeDepth(type) : depth;
                    var previous = this.CapabilityTypeDepth(ancestor.Type);
                    if (depth > previous)
                    {
                        if (growing)
                        {
                            return ConstraintProof.Unknown;
                        }

                        growing = true;
                        depth = previous;
                    }
                }
            }
        }

        var proposition = this.InternConstraint(new(ConstraintKind.Contract, type, contract: intrinsic));
        var key = (proposition, scope, derivation);
        if (!this.capabilityNodes.TryGetValue(key, out var work))
        {
            work = new(type, intrinsic, scope, derivation);
            this.capabilityNodes.Add(key, work);
        }

        if (!work.Evaluated && !work.Queued)
        {
            work.Parent = this.capabilityConsumer;
            work.Queued = true;
            this.capabilityQueue.Enqueue(work);
        }

        if (this.capabilityConsumer is { } consumer)
        {
            if (this.capabilityEdges.Add((work, consumer)))
            {
                work.Dependents.Add(consumer);
            }

            return work.Result;
        }

        try
        {
            while (this.capabilityQueue.TryDequeue(out var next))
            {
                next.Queued = false;
                next.Evaluated = true;
                this.capabilityConsumer = next;
                var result = this.ComputeCapability(next);
                if (result == next.Result)
                {
                    continue;
                }

                next.Result = result;
                for (var i = 0; i < next.Dependents.Count; i++)
                {
                    var dependent = next.Dependents[i];
                    if (!dependent.Queued)
                    {
                        dependent.Queued = true;
                        this.capabilityQueue.Enqueue(dependent);
                    }
                }
            }
        }
        finally
        {
            this.capabilityConsumer = null;
        }

        return work.Result;
    }

    private ConstraintProof ComputeCapability(CapabilityWork work)
    {
        var type = work.Type;
        if (InvalidConstraintType(type))
        {
            return ConstraintProof.Error;
        }

        if (TryLeafCapability(type, work.Intrinsic.Intrinsic, out var leaf))
        {
            return leaf;
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection ||
            (work.Intrinsic.Intrinsic == IntrinsicKind.ObjectPayload && type.Symbol?.Declaration is ContractKoto))
        {
            // A Contract's Self has no structure of its own; its ObjectPayload evidence is the Contract's clause (SPEC 8.4.7.2).
            return this.SymbolicCapability(work);
        }

        if (work.Intrinsic.Intrinsic == IntrinsicKind.Copy)
        {
            if (type.Symbol?.Declaration is DeclarationContainerKoto container)
            {
                if (work.Derivation)
                {
                    if (!this.storageShapes.TryGetValue(container, out var shape))
                    {
                        return ConstraintProof.Unknown;
                    }

                    return CombineProof(shape.HasDestructor ? ConstraintProof.Refuted : ConstraintProof.Proven, this.StoredCapability(work, shape), true);
                }

                return this.NominalCopy(work, container);
            }
        }
        else
        {
            var origin = type.Origin is null ||
                this.ProvesOriginOutlives(type.Origin, BoundOrigin.Static, work.Scope.Owner) ? ConstraintProof.Proven : ConstraintProof.Unknown;
            return CombineProof(origin, this.StructuralCapability(work), true);
        }

        return this.StructuralCapability(work);
    }

    private ConstraintProof NominalCopy(CapabilityWork work, DeclarationContainerKoto container)
    {
        if (!this.storageShapes.TryGetValue(container, out var shape))
        {
            return ConstraintProof.Unknown;
        }

        for (var i = 0; i < shape.Types.Count; i++)
        {
            if (shape.Types[i].BindingState == BindingState.Invalid)
            {
                return ConstraintProof.Error;
            }

            if (shape.Types[i].BoundType is null)
            {
                return ConstraintProof.Unknown;
            }
        }

        if (!this.copyByType.TryGetValue(container, out var declarations) || declarations.Count == 0)
        {
            return this.capabilityMode == BindingMode.Final && this.storageShapes.ContainsKey(container) ? ConstraintProof.Refuted : ConstraintProof.Unknown;
        }

        var result = ConstraintProof.Refuted;
        for (var i = 0; i < declarations.Count; i++)
        {
            var declaration = declarations[i];
            var validation = this.RequestCapability(this.SelfType(container.BoundSymbol!), this.Library.Copy, declaration.Scope, derivation: true);
            if (declaration.Clause.BindingState == BindingState.Invalid || validation is ConstraintProof.Error or ConstraintProof.Refuted)
            {
                return ConstraintProof.Error;
            }

            var condition = this.ProveConditionalPremises(declaration.Premises, container, work.Type, work.Scope);

            result = CombineProof(result, CombineProof(validation, condition, true), false);
        }

        return result;
    }

    private ConstraintProof StructuralCapability(CapabilityWork work)
    {
        if (work.Intrinsic.Intrinsic == IntrinsicKind.Owned && work.Type.Kind == BoundTypeKind.Function &&
            OwnFunctionBinder(work.Type, work.Type) is { } binder)
        {
            // Only this signature's own quantified Origins are closed. Querying a
            // component separately must still observe its free lifetime dependency.
            var count = InputOriginCount(binder);
            var inputs = this.originScratch.Rent(count);
            try
            {
                inputs.AsSpan(0, count).Fill(BoundOrigin.Static);
                var closed = this.SubstituteStoredOrigins(work.Type, binder, [], inputs.AsSpan(0, count));
                return this.RequestCapability(closed, work.Intrinsic, work.Scope);
            }
            finally
            {
                this.originScratch.Return(inputs, clearArray: true);
            }
        }

        if (work.Type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary)
        {
            // SPEC 4.5: collection ownership follows every stored Type, even when empty.
            if (work.Intrinsic.Intrinsic != IntrinsicKind.Owned)
            {
                return work.Intrinsic.Intrinsic == IntrinsicKind.Copy ? ConstraintProof.Refuted : ConstraintProof.Unknown;
            }

            var owned = ConstraintProof.Proven;
            for (var i = 0; i < work.Type.Components.Count; i++)
            {
                owned = CombineProof(owned, this.RequestCapability(work.Type.Components[i], work.Intrinsic, work.Scope), true);
            }

            return owned;
        }

        if (work.Type.Symbol?.Declaration is DeclarationContainerKoto container)
        {
            return this.storageShapes.TryGetValue(container, out var shape) ? this.StoredCapability(work, shape) : ConstraintProof.Unknown;
        }

        var result = ConstraintProof.Proven;
        for (var i = 0; i < work.Type.Components.Count; i++)
        {
            result = CombineProof(result, this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, work.Type.Components[i], contract: work.Intrinsic)), work.Scope), true);
        }

        return result;
    }

    private ConstraintProof StoredCapability(CapabilityWork work, StorageShape shape)
    {
        var result = ConstraintProof.Proven;
        if (work.Intrinsic.Intrinsic == IntrinsicKind.Owned && work.Type.Symbol?.Declaration is DeclarationContainerKoto declaration)
        {
            // Every complete Type argument is retained, including unused and inherited slots.
            for (var i = 0; i < work.Type.Components.Count; i++)
            {
                result = CombineProof(result, this.RequestCapability(work.Type.Components[i], work.Intrinsic, work.Scope), true);
            }

            for (var i = 0; i < work.Type.OriginArguments.Count; i++)
            {
                result = CombineProof(result, work.Type.OriginArguments[i] is { } origin && this.ProvesOriginOutlives(origin, BoundOrigin.Static, work.Scope.Owner) ? ConstraintProof.Proven : ConstraintProof.Unknown, true);
            }
        }

        for (var i = 0; i < shape.Types.Count; i++)
        {
            var type = this.StoredType(shape.Types[i], work.Type);
            var proof = shape.Types[i].BindingState == BindingState.Invalid ? ConstraintProof.Error : type is null ? ConstraintProof.Unknown : this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: work.Intrinsic)), work.Scope);
            result = CombineProof(result, proof, true);
        }

        return result;
    }

    private ConstraintProof SymbolicCapability(CapabilityWork work)
    {
        var positive = false;
        var negative = false;
        for (var scope = work.Scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { } environment)
            {
                continue;
            }

            if (environment.Invalid)
            {
                return ConstraintProof.Error;
            }

            foreach (var fact in environment.Facts)
            {
                if (!this.AvailableConstraintFact(environment, fact))
                {
                    continue;
                }

                var appliedSemantics = fact.Kind == ConstraintKind.Semantics && work.Type.Kind == BoundTypeKind.SemanticsApplication && ReferenceEquals(fact.Subject, work.Type.Symbol?.WholeType);
                if (!ReferenceEquals(fact.Subject, work.Type) && !appliedSemantics)
                {
                    continue;
                }

                var evidence = ConstraintProof.Unknown;
                if (fact.Kind == ConstraintKind.TypeIdentity && fact.RequiredType is { } required)
                {
                    evidence = this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, required, contract: work.Intrinsic)), work.Scope);
                }
                else if (fact.Kind == ConstraintKind.Contract && IsRefinement(fact.Contract!, work.Intrinsic) && this.AvailableContractPremise(fact.Contract!))
                {
                    evidence = ConstraintProof.Proven;
                }
                else if (fact.Kind == ConstraintKind.Semantics)
                {
                    const SemanticsMask copy = SemanticsMask.Ref | SemanticsMask.ObjRef | SemanticsMask.Unsafe;
                    const SemanticsMask nonCopy = SemanticsMask.Uniq | SemanticsMask.ObjUniq | SemanticsMask.Object;
                    if (fact.Mask == SemanticsMask.Owner)
                    {
                        var target = appliedSemantics ? work.Type.Components[0] : work.Type.Symbol?.Type;
                        if (target is not null && !ReferenceEquals(target, work.Type))
                        {
                            evidence = this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, target, contract: work.Intrinsic)), work.Scope);
                        }
                    }
                    else if (work.Intrinsic.Intrinsic == IntrinsicKind.Copy)
                    {
                        evidence = (fact.Mask & ~copy) == 0 ? ConstraintProof.Proven : (fact.Mask & ~nonCopy) == 0 ? ConstraintProof.Refuted : ConstraintProof.Unknown;
                    }
                    else if (work.Intrinsic.Intrinsic is IntrinsicKind.Sealed or IntrinsicKind.ObjectPayload && (fact.Mask & SemanticsMask.Owner) == 0)
                    {
                        evidence = ConstraintProof.Refuted;
                    }
                    else if (work.Intrinsic.Intrinsic == IntrinsicKind.Owned && fact.Mask == SemanticsMask.Unsafe)
                    {
                        var target = appliedSemantics ? work.Type.Components[0] : work.Type.Symbol?.Type;
                        if (target is not null && !ReferenceEquals(target, work.Type))
                        {
                            evidence = this.RequestCapability(target, work.Intrinsic, work.Scope);
                        }
                    }
                }

                if (evidence == ConstraintProof.Error)
                {
                    return evidence;
                }

                positive |= evidence == ConstraintProof.Proven;
                negative |= evidence == ConstraintProof.Refuted;
            }
        }

        return positive && negative ? ConstraintProof.Error : positive ? ConstraintProof.Proven : negative ? ConstraintProof.Refuted : ConstraintProof.Unknown;
    }

    private sealed class CapabilityWork(BoundType type, BindingSymbol intrinsic, BindingScope scope, bool derivation)
    {
        internal BoundType Type { get; } = type;

        internal BindingSymbol Intrinsic { get; } = intrinsic;

        internal BindingScope Scope { get; } = scope;

        internal bool Derivation { get; } = derivation;

        // Recursive Owned structure starts optimistically; everything else starts unknown.
        internal ConstraintProof Result { get; set; } = InitialResult(intrinsic, type);

        internal bool Evaluated { get; set; }

        internal bool Queued { get; set; }

        internal CapabilityWork? Parent { get; set; }

        internal List<CapabilityWork> Dependents { get; } = new();

        internal void Reset()
        {
            this.Result = InitialResult(this.Intrinsic, this.Type);
            this.Evaluated = false;
            this.Queued = false;
            this.Parent = null;
            this.Dependents.Clear();
        }

        private static ConstraintProof InitialResult(BindingSymbol intrinsic, BoundType type)
            => intrinsic.Intrinsic == IntrinsicKind.Owned && type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection)
                ? ConstraintProof.Proven
                : ConstraintProof.Unknown;
    }
}
