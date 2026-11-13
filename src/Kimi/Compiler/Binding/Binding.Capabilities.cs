// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConstraint Proposition, BindingScope Scope, bool Derivation), CapabilityWork> capabilityNodes = new(
        KimiLibrarySize.Stated.CapabilityNodes);

    private readonly HashSet<(CapabilityWork Source, CapabilityWork Consumer)> capabilityEdges = new();
    private readonly Queue<CapabilityWork> capabilityQueue = new();
    private CapabilityWork? capabilityConsumer;
    private bool capabilitiesReady;
    private BindingMode capabilityMode;

    /// <summary>Queries compiler-intrinsic Copy for a complete Type under lexical Constraints.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The proof result, without choosing any acquisition operation.</returns>
    public ConstraintProof ProveCopy(BoundType type, Koto context) => this.ProveCopy(type, this.ConstraintScope(context));

    /// <summary>Queries the recursive Owned guarantee, independently of heap/global borrow restrictions.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The proof result; unresolved Origins remain Unknown.</returns>
    public ConstraintProof ProveOwned(BoundType type, Koto context)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Owned)), this.ConstraintScope(context));

    /// <summary>Queries compiler-intrinsic PrimitiveInteger (SPEC 8.7.4) under lexical Constraints. Its reader, WrappingIntegerTest,
    /// observes through it that a wrapping integer Type satisfies no PrimitiveInteger (SPEC 3.1.1); no program of that class reaches
    /// the Contract with one.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The proof result; a proven Type is an owner integer scalar in every instance.</returns>
    public ConstraintProof ProvePrimitiveInteger(BoundType type, Koto context)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.PrimitiveInteger)), this.ConstraintScope(context));

    /// <summary>Queries complete owner Core evidence without inspecting stored fields.</summary>
    /// <param name="type">The normalized complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The four-valued Sealed proof.</returns>
    public ConstraintProof ProveSealed(BoundType type, Koto context) => this.ProveSealed(type, this.ConstraintScope(context));

    // The Copy and Sealed queries under a scope's premises, such as a definition's effect premises (EffectPremises).
    internal ConstraintProof ProveCopy(BoundType type, BindingScope scope)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Copy)), scope);

    internal ConstraintProof ProveSealed(BoundType type, BindingScope scope) => this.RequestCapability(type, this.Library.Sealed, scope);

    private static bool TryLeafCapability(BoundType type, IntrinsicKind kind, out ConstraintProof result)
    {
        result = ConstraintProof.Unknown;
        if (kind == IntrinsicKind.PrimitiveInteger)
        {
            // SPEC 8.7.4: exactly the twelve owner integer Cores; a symbolic Type is decided by its premises.
            if (type.IsAbstract)
            {
                return false;
            }

            result = type.Kind == BoundTypeKind.Primitive && type.Semantics == SemanticsKind.Owner && type.IsInteger ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        if (kind == IntrinsicKind.ObjectPayload)
        {
            if (type.Symbol?.Declaration.BindingState == BindingState.Invalid)
            {
                result = ConstraintProof.Error;
                return true;
            }

            // SPEC 8.7.3: symbolic targets, a Contract's Self among them, are decided by their premises.
            if (type.IsAbstract)
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

            if (type.IsAbstract)
            {
                return false;
            }

            result = type.Semantics != SemanticsKind.Owner || ReferenceEquals(type, BoundType.Never) ||
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

        if (type.Kind == BoundTypeKind.Slice && kind == IntrinsicKind.Copy)
        {
            result = ConstraintProof.Proven;
            return true;
        }

        if (type.Kind == BoundTypeKind.FunctionItem && kind == IntrinsicKind.Copy)
        {
            // SPEC 7.7.4: an Item stores nothing, so it is Copy; it is Owned when its bound arguments and its call signature are, which
            // the structural proof decides (CallSignature).
            result = ConstraintProof.Proven;
            return true;
        }

        if (type.Kind == BoundTypeKind.Primitive || (type.Kind == BoundTypeKind.Function && kind == IntrinsicKind.Copy))
        {
            result = kind == IntrinsicKind.Owned || (type.Kind == BoundTypeKind.Primitive && type.Name != "string") ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        if (type.Kind == BoundTypeKind.Semantics && kind == IntrinsicKind.Copy)
        {
            result = type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Raw ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        return false;
    }

    // Whether a capability judgment reads a Type's declaration (a struct, enum or other container) rather than its parts (SPEC 4.5: a
    // collection by its element Types, whatever its Symbol).
    private static bool JudgedByDeclaration(BoundType type)
        => type.Kind is not (BoundTypeKind.Array or BoundTypeKind.Dictionary) && type.Symbol?.Declaration is DeclarationContainerKoto;

    // A requirement Item's Contract reference: metadata, of which only the bound Type arguments are retained bindings.
    private static BoundType? RequirementReference(BoundType type)
        => type.Kind == BoundTypeKind.FunctionItem && type.Symbol?.Declaration is FunctionKoto { IsRequirement: true } && type.Components.Count > 1
            ? type.Components[1] : null;

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

    // Whether a binder (an atom's, or a Type parameter's declaration) is inherited by `function`, nested in `body`: it is outside the
    // function and is no Container that the function is a member of below the body.
    private static bool Inherited(Koto binder, FunctionKoto function, Koto body)
        => !IsWithin(binder, function) && !(IsWithin(function, binder) && !IsWithin(body, binder));

    // The declaration a Type parameter's syntax belongs to (a function or a Container, a Contract's Self included).
    private static Koto DeclaringNode(Koto declaration)
    {
        for (var current = declaration; current is not null; current = current.Parent)
        {
            if (current is DeclarationKoto)
            {
                return current;
            }
        }

        return declaration;
    }

    // The body a declaration is nested in: its nearest enclosing function or accessor, the execution scope's generated function for a root
    // function (which lies there with the top-level bindings, SPEC 6.1.1); null outside every body (a Container's member).
    private static Koto? EnclosingBody(Koto declaration)
    {
        for (var current = declaration.Parent; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto or PropertyAccessorKoto)
            {
                return current;
            }
        }

        return null;
    }

    // SPEC 15.2.3, 15.3.4 (DECISIONS BU-73): a callable's call signature as an Owned part, with the binder of its own per-call Origins: a
    // Function Type that quantifies an input is its own, under its syntax; a Closure's is its value's (ClosureSignature), under its literal,
    // which every Closure Type names (ClosureType); a Function Item's, for a function nested in a body, root functions included
    // (EnclosingBody), is what it inherits (InheritedEnvironment), read from its declaration, and none for any other function, whose bound
    // arguments are its whole dependence.
    // A signature is unreadable, none, where its header did not bind (an Item's function, the Type BindConcreteClosure starts with) or
    // BU-60's view withholds a Closure's held instance (an Origin position closed in no instance, PLAN G300).
    private (BoundType? Signature, Koto Binder)? CallSignature(BoundType type)
    {
        if (type.Kind == BoundTypeKind.Function)
        {
            return OwnFunctionBinder(type, type) is { } binder ? (type, binder) : null;
        }

        if (type.Kind is not (BoundTypeKind.Closure or BoundTypeKind.FunctionItem))
        {
            return null;
        }

        if (type.Symbol?.Declaration is not FunctionKoto definition)
        {
            throw new InvalidOperationException($"Callable Type `{type}` names no function (InternalInvariant; BU-73).");
        }

        if (type.Kind == BoundTypeKind.FunctionItem)
        {
            return EnclosingBody(definition) is { } body ? (this.InheritedEnvironment(definition, body), definition) : null;
        }

        return (type.ClosureContext is not null && TyCtxt.ClosureInstance(type) is null ? null : this.ClosureSignature(type), definition);
    }

    private void ResetCapabilities(BindingMode mode)
    {
        this.capabilitiesReady = false;
        this.capabilityMode = mode;
        this.readings.Clear();
        this.ResetIdentityClasses();
        this.ClearNeededSites();
        this.ClearProofScopes();
        this.ClearCapabilityResults();
    }

    private void ClearCapabilityResults()
    {
        this.summaryEpoch++; // Component summaries follow the capability results' lifecycle.
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
        var contexts = this.OperandContexts(type);
        if (contexts.Unresolved)
        {
            return contexts.Invalid ? ConstraintProof.Error : ConstraintProof.Unknown;
        }

        if (!this.capabilitiesReady)
        {
            if (this.running && !derivation && TryLeafCapability(type, intrinsic.Intrinsic, out var concrete))
            {
                return concrete;
            }

            return ConstraintProof.Unknown;
        }

        if (!derivation && TryLeafCapability(type, intrinsic.Intrinsic, out var leaf))
        {
            return leaf;
        }

        // A proof can re-enter a declaration with ever larger arguments: Copy's conditional premise over a projection, and Owned's
        // projection normalized at the use through a conformance binding that uses itself (SPEC 8.3.3, DECISIONS BU-22); the second
        // growth step stays Unknown. Removal condition: SPEC 8.3.3's proof-depth limit is a resource failure (PLAN G200).
        if (!derivation && type.Symbol is not null)
        {
            var growing = false;
            var depth = type.Height;
            for (var ancestor = this.capabilityConsumer; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor.Type.Symbol, type.Symbol) && !ReferenceEquals(ancestor.Type, type))
                {
                    var previous = ancestor.Type.Height;
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

            this.ScopeReads.Origin ??= work.Result == ConstraintProof.Proven ? null : work.Origin;
            return this.ReadVerdict(work.Result, work.Cause, work.Fact);
        }

        this.BeginProof();
        try
        {
            while (this.capabilityQueue.TryDequeue(out var next))
            {
                next.Queued = false;
                next.Evaluated = true;
                this.capabilityConsumer = next;
                this.ScopeReads = default;
                next.Parts?.Clear();
                var result = this.ComputeCapability(next);
                var cause = this.ProofCause(result);
                next.Fact = this.ProofFact(result); // Payload of the final iteration, outside the fixpoint's equality.
                next.Origin = result == ConstraintProof.Proven ? null : this.ScopeReads.Origin;
                if (result == next.Result && ReferenceEquals(cause, next.Cause))
                {
                    continue;
                }

                next.Result = result;
                next.Cause = cause;
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

        this.ScopeReads = default;
        this.ScopeReads.Origin = work.Result == ConstraintProof.Proven ? null : work.Origin;
        return this.EndProof(this.ReadVerdict(work.Result, work.Cause, work.Fact));
    }

    private ConstraintProof ComputeCapability(CapabilityWork work)
    {
        var type = work.Type;
        if (this.OperandContexts(type).Invalid)
        {
            return ConstraintProof.Error;
        }

        if (TryLeafCapability(type, work.Intrinsic.Intrinsic, out var leaf))
        {
            return leaf;
        }

        if (type.Kind == BoundTypeKind.SemanticsAdaptation)
        {
            var result = ConstraintProof.Unknown;
            for (var i = 0; i < type.Components.Count; i++)
            {
                var child = this.RequestCapability(type.Components[i], work.Intrinsic, work.Scope);
                if (child == ConstraintProof.Error)
                {
                    return child;
                }

                result = i == 0 || result == child ? child : ConstraintProof.Unknown;
            }

            return result;
        }

        if (type.IsAbstract)
        {
            // A Contract's Self, a Type parameter, has no structure of its own; its ObjectPayload evidence is the Contract's clause
            // (SPEC 8.7.3).
            return this.SymbolicCapability(work);
        }

        if (work.Intrinsic.Intrinsic == IntrinsicKind.Copy)
        {
            if (type.Symbol?.Declaration is DeclarationContainerKoto container)
            {
                if (work.Derivation)
                {
                    if (container.BoundSymbol?.Adt is not { } shape)
                    {
                        return ConstraintProof.Unknown;
                    }

                    return CombineProof(shape.DestructorKoto is not null ? ConstraintProof.Refuted : ConstraintProof.Proven, this.StoredCapability(work, shape), true);
                }

                return this.NominalCopy(work, container);
            }
        }
        else
        {
            var origin = OwnedOrigin(type) is not { } owned ? ConstraintProof.Proven : this.StaticOrigin(owned, work.Scope);
            return CombineProof(origin, this.StructuralCapability(work), true);
        }

        return this.StructuralCapability(work);
    }

    private ConstraintProof NominalCopy(CapabilityWork work, DeclarationContainerKoto container)
    {
        if (container.BoundSymbol?.Adt is not { } shape)
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
            return this.capabilityMode == BindingMode.Final ? ConstraintProof.Refuted : ConstraintProof.Unknown;
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

            var condition = this.ProveConditionalPremises(declaration.Premises, work.Type, work.Scope);

            result = CombineProof(result, CombineProof(validation, condition, true), false);
        }

        return result;
    }

    private ConstraintProof StructuralCapability(CapabilityWork work)
    {
        // A Function Type is its call signature: only its own quantified Origins are closed, so querying a component separately still
        // observes its free lifetime dependency.
        var call = work.Intrinsic.Intrinsic == IntrinsicKind.Owned ? this.CallSignature(work.Type) : null;
        if (call is { Signature: { } own, Binder: var binder } && ReferenceEquals(own, work.Type))
        {
            return this.SignatureCapability(work, own, binder);
        }

        if (work.Type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary)
        {
            // SPEC 4.5: collection ownership follows every stored Type, even when empty.
            if (work.Intrinsic.Intrinsic != IntrinsicKind.Owned)
            {
                return work.Intrinsic.Intrinsic == IntrinsicKind.Copy ? ConstraintProof.Refuted : ConstraintProof.Unknown;
            }

            var owned = ConstraintProof.Proven;
            foreach (var (part, _, _) in new CapabilityParts(work.Type))
            {
                owned = part is null ? owned : CombineProof(owned, this.RequestCapability(part, work.Intrinsic, work.Scope), true);
            }

            return owned;
        }

        if (JudgedByDeclaration(work.Type))
        {
            return this.SummaryCapability(work, (DeclarationContainerKoto)work.Type.Symbol!.Declaration);
        }

        // Owned records each part that is not Proven with its member and Origin (CapabilityWork.Parts, R4-M161).
        var result = ConstraintProof.Proven;
        var parts = new CapabilityParts(work.Type);
        while (parts.MoveNext())
        {
            var (part, origin, argument) = parts.Current;
            var saved = this.ScopeReads.Origin;
            this.ScopeReads.Origin = null;
            var proof = part is null ? (work.Intrinsic.Intrinsic == IntrinsicKind.Owned ? this.StaticOrigin(origin, work.Scope) : ConstraintProof.Proven)
                : argument ? this.RequestCapability(part, work.Intrinsic, work.Scope)
                : this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, part, contract: work.Intrinsic)), work.Scope);
            result = CombineProof(result, proof, true);
            this.ScopeReads.Origin = work.Part(parts.Member, proof, this.ScopeReads.Origin, saved);
        }

        // A Closure's call signature, or what a Function Item inherits (InheritedEnvironment), is a part after its captures or bound
        // arguments, member Components.Count; an unreadable one (CallSignature) answers BU-22's limit with its definition as the site, on
        // which a failed definition's use rests (DECISIONS BU-73).
        if (call is { } closure)
        {
            var saved = this.ScopeReads.Origin;
            this.ScopeReads.Origin = null;
            var proof = closure.Signature is { } signature ? this.SignatureCapability(work, signature, closure.Binder)
                : this.ReadVerdict(ConstraintProof.Unknown, work.Site ??= new(this.neededSites) { Use = closure.Binder }, null);
            result = CombineProof(result, proof, true);
            this.ScopeReads.Origin = work.Part(work.Type.Components.Count, proof, this.ScopeReads.Origin, saved);
        }

        return result;
    }

    // SPEC 15.2.3, 15.8.2 (DECISIONS BU-73): a callable signature is Owned when it is with its own per-call Origins closed to static: its
    // direct inputs, its receiver and its call result (PerCallImages); every other Origin it names is fixed.
    private ConstraintProof SignatureCapability(CapabilityWork work, BoundType signature, Koto binder)
    {
        var count = InputOriginCount(binder);
        var inputs = this.originScratch.Rent(count);
        try
        {
            inputs.AsSpan(0, count).Fill(BoundOrigin.Static);
            var images = new PerCallImages(inputs.AsSpan(0, count), BoundOrigin.Static, BoundOrigin.Static);
            return this.RequestCapability(this.SubstituteStoredOrigins(signature, binder, [], images), work.Intrinsic, work.Scope);
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
        }
    }

    // SPEC 15.3.4 (last bullet), 15.2.3: what a named function nested in a body (a root function in the execution scope's) inherits, read
    // from its declaration as its Item is formed: the Origins and Type parameters its declared signature names whose binder lies outside it
    // and is no Container it is a member of below that body (whose slots its bound arguments instantiate), as one environment Tuple
    // (EnvironmentTuple). Atoms bound inside its declaration (its own Origins, a Constraint set's slot, a nested Function Type's inputs)
    // are per-call, and a Type a projection or a bound argument derives from them is never judged itself (15.2.3 counts the arguments).
    // Null when a declared Type did not bind.
    private BoundType? InheritedEnvironment(FunctionKoto function, Koto body)
    {
        if (function.BoundSymbol?.Type is not { } result)
        {
            return null;
        }

        var count = function.Parameters.Count;
        var declared = this.RentTypes(count + 1);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (function.Parameters[i].Type.BoundType is not { } parameter)
                {
                    return null;
                }

                declared[i] = parameter;
            }

            declared[count] = result;
            var signature = this.types.Tuple(declared.AsSpan(0, count + 1));
            var summary = this.SummaryOf(signature);
            var origins = this.originScratch.Rent(summary.Nodes.Length + summary.RawNodes.Length);
            var types = this.RentTypes(this.InheritedParameters(signature, function, body, null, 0, this.visitedParts.Begin(this.storageVersion)));
            try
            {
                var o = InheritedOrigins(summary.Nodes, function, body, origins, 0);
                o = InheritedOrigins(summary.RawNodes, function, body, origins, o);
                var t = this.InheritedParameters(signature, function, body, types, 0, this.visitedParts.Begin(this.storageVersion));
                return this.EnvironmentTuple(types.AsSpan(0, t), origins.AsSpan(0, o));
            }
            finally
            {
                this.originScratch.Return(origins, clearArray: true);
                this.typeScratch.Return(types, clearArray: true);
            }
        }
        finally
        {
            this.typeScratch.Return(declared, clearArray: true);
        }

        static int InheritedOrigins(BoundOrigin[] nodes, FunctionKoto function, Koto body, BoundOrigin[] into, int count)
        {
            for (var i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] is { Kind: not (OriginKind.Static or OriginKind.Intersection), Binder: { } binder } atom && Inherited(binder, function, body))
                {
                    into[count++] = atom;
                }
            }

            return count;
        }
    }

    // The Type parameters a declared signature inherits, each part visited once per walk (DECISIONS BU-19): counted, or written into `into`.
    private int InheritedParameters(BoundType type, FunctionKoto function, Koto body, BoundType[]? into, int count, uint query)
    {
        if (type.Kind == BoundTypeKind.Parameter)
        {
            if (type.Symbol?.Declaration is { } declaration && Inherited(DeclaringNode(declaration), function, body))
            {
                if (into is not null)
                {
                    into[count] = type;
                }

                count++;
            }

            return count;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var part = type.Components[i];
            if (part.HasAbstractPart && this.FirstVisit(part, query))
            {
                count = this.InheritedParameters(part, function, body, into, count, query);
            }
        }

        return count;
    }

    // An environment's parts as one Type (SPEC 7.7.4, 15.2.3): a Tuple of its Type parts whose Origin arguments are its Origin parts, which
    // CapabilityParts reads back in this order; Unit when it has none. Invariant: it is the only Tuple whose Origin arguments are Origin
    // parts, and the only producer of a Tuple with Origin arguments (TyCtxt.Tuple gives none); it is a capability subject (the erasure's,
    // an Item's signature part), never a value's Type. A new producer of a Tuple with Origin arguments must not reach CapabilityParts.
    private BoundType EnvironmentTuple(ReadOnlySpan<BoundType> types, ReadOnlySpan<BoundOrigin> origins)
        => types.IsEmpty && origins.IsEmpty ? BoundType.Unit : this.types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, types, originArguments: origins);

    // Copy's derivation over the stored Types of a declaration's Self (SPEC 3.5.2); Owned reads component summaries.
    private ConstraintProof StoredCapability(CapabilityWork work, AdtDef shape)
    {
        var result = ConstraintProof.Proven;
        for (var i = 0; i < shape.Types.Count; i++)
        {
            var type = this.StoredType(shape.Types[i], work.Type);
            var proof = shape.Types[i].BindingState == BindingState.Invalid ? ConstraintProof.Error : type is null ? ConstraintProof.Unknown : this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: work.Intrinsic)), work.Scope);
            if (work.Intrinsic.Intrinsic == IntrinsicKind.Copy && type is not null && this.HoldsHolder(type))
            {
                proof = ConstraintProof.Refuted; // SPEC 5.7: a `during self` layer makes its Type non-Copy.
            }

            result = CombineProof(result, proof, true);
        }

        return result;
    }

    private ConstraintProof SymbolicCapability(CapabilityWork work)
    {
        var positive = false;
        var negative = false;
        var parameterIdentity = HasParameterIdentity(work.Scope);
        var subject = parameterIdentity ? this.SubstituteIdentity(work.Type, work.Scope, parametersOnly: true, out _) : work.Type;
        for (var scope = work.Scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { } environment)
            {
                continue;
            }

            if (environment.Invalid)
            {
                return this.InvalidEnvironment(environment);
            }

            foreach (var available in environment.Facts)
            {
                if (!this.AvailableConstraintFact(environment, available, out _))
                {
                    continue;
                }

                var fact = parameterIdentity ? this.Reading(available, work.Scope) : available;
                var appliedSemantics = fact.Kind == ConstraintKind.Semantics && work.Type.Kind == BoundTypeKind.SemanticsApplication && ReferenceEquals(fact.Subject, work.Type.Symbol?.WholeType);
                if (!this.FactStates(fact, subject, out var stated) && !appliedSemantics)
                {
                    continue;
                }

                var evidence = ConstraintProof.Unknown;
                if (fact.Kind == ConstraintKind.TypeIdentity && stated is { } required)
                {
                    evidence = this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, required, contract: work.Intrinsic)), work.Scope);
                }
                else if (fact.Kind == ConstraintKind.Contract && IsRefinement(fact.Contract!, work.Intrinsic) && this.AvailableContractPremise(fact.Contract!))
                {
                    evidence = ConstraintProof.Proven;
                }
                else if (fact.Kind == ConstraintKind.Contract && fact.Contract!.Intrinsic == IntrinsicKind.PrimitiveInteger && work.Intrinsic.Intrinsic is IntrinsicKind.Copy or IntrinsicKind.Owned)
                {
                    evidence = ConstraintProof.Proven; // SPEC 8.7.4: every admitted integer Type is Copy and Owned.
                }
                else if (fact.Kind == ConstraintKind.Semantics)
                {
                    const SemanticsMask copy = SemanticsMask.Ref | SemanticsMask.ObjRef | SemanticsMask.Raw;
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
                    else if (work.Intrinsic.Intrinsic == IntrinsicKind.Owned && fact.Mask == SemanticsMask.Raw)
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

    // The parts a capability judgment decomposes a Type into when no declaration judges it (SPEC 15.2.3): its components, a requirement
    // Item's Contract reference replaced by that reference's Type arguments (Argument), then its Origin parts: that reference's Origin
    // arguments (a Contract's inherited Origins, SPEC 6.1.3), or an environment Tuple's (EnvironmentTuple). An environment's parts have
    // this one numbering for the erasure and the Owned proof alike (DECISIONS BU-73: a Closure's capture index). StructuralCapability,
    // ErasureSubject and the component summaries' walk read this one enumeration.
    private ref struct CapabilityParts(BoundType type)
    {
        private readonly BoundType type = type;
        private readonly BoundType? reference = RequirementReference(type);
        private int index = -1;
        private int argument = -1;
        private int origin = -1;

        // A Type part, or an Origin part (Part null); Argument for a requirement reference's Type argument.
        public (BoundType? Part, BoundOrigin? Origin, bool Argument) Current { readonly get; private set; }

        // The current part's member: its component's index; a requirement reference's Type and Origin arguments are member 1; an
        // environment Tuple's Origin part follows its components.
        public readonly int Member => this.argument >= 0 || (this.origin >= 0 && this.reference is not null) ? 1
            : this.origin >= 0 ? this.type.Components.Count + this.origin : this.index;

        // The Origin parts: a requirement reference's Origin arguments, or an environment Tuple's, read by EnvironmentTuple's invariant
        // that only an environment Tuple has Origin arguments.
        private readonly IReadOnlyList<BoundOrigin> Origins => this.reference?.OriginArguments ?? (this.type.Kind == BoundTypeKind.Tuple ? this.type.OriginArguments : []);

        // The member of the part at an ordinal of this enumeration, an environment Tuple's member (ErasureSubject).
        public static int MemberAt(BoundType type, int ordinal)
        {
            var parts = new CapabilityParts(type);
            for (var i = 0; parts.MoveNext(); i++)
            {
                if (i == ordinal)
                {
                    return parts.Member;
                }
            }

            throw new InvalidOperationException($"`{type}` has no part {ordinal} (InternalInvariant; BU-73).");
        }

        public readonly CapabilityParts GetEnumerator() => this;

        public bool MoveNext()
        {
            if (this.origin >= 0 || this.index >= this.type.Components.Count)
            {
                this.origin++;
                if (this.origin < this.Origins.Count)
                {
                    this.Current = (null, this.Origins[this.origin], false);
                    return true;
                }

                return false;
            }

            if (this.argument >= 0 && ++this.argument < this.reference!.Components.Count)
            {
                this.Current = (this.reference.Components[this.argument], null, true);
                return true;
            }

            this.argument = -1;
            while (++this.index < this.type.Components.Count)
            {
                if (this.index != 1 || this.reference is null)
                {
                    this.Current = (this.type.Components[this.index], null, false);
                    return true;
                }

                if (this.reference.Components.Count != 0)
                {
                    this.argument = 0;
                    this.Current = (this.reference.Components[0], null, true);
                    return true;
                }
            }

            return this.MoveNext();
        }
    }
}
