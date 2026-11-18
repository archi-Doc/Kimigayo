// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundType, bool> pendingAssociatedTypes = new(
        KimiLibrarySize.KeyedCapacity(KimiLibrarySize.Stated.AssociatedTypes), ReferenceEqualityComparer.Instance);

    private readonly Dictionary<Koto, (BoundRequirement Identity, AssociatedBinding Binding)> inferenceFailures = new(ReferenceEqualityComparer.Instance);

    // The pass's inference sources, each path whose consumer list (BoundConformancePath.InferenceConsumers) this pass filled, in the order
    // first filled: InvalidateAssociatedEvidence's order, and the lists ResetAssociatedInference empties.
    private readonly List<BoundConformancePath> inferenceSources = new();
    private readonly Queue<BoundConformancePath> inferenceInvalidation = new();
    private AssociatedInference? collectingAssociated;
    private bool associatedInferenceReady;

    // Symbolic parameter projections are complete public Types; an unresolved projection on a nominal
    // declaration still needs a Type value. This query never searches bodies or introduces a proof. KimiLibraryShapeTest states with it
    // that no projection of the library's Type syntax is pending (PLAN G949 S1-r2, rule (c)).
    internal bool PendingAssociatedType(BoundType type)
    {
        if (this.pendingAssociatedTypes.TryGetValue(type, out var pending))
        {
            return pending;
        }

        pending = type.Kind == BoundTypeKind.AssociatedProjection && type.Components[0].Symbol?.Declaration is StructKoto or EnumKoto;
        for (var i = 0; !pending && i < type.Components.Count; i++)
        {
            pending = this.PendingAssociatedType(type.Components[i]);
        }

        this.pendingAssociatedTypes[type] = pending;
        return pending;
    }

    private static bool InferenceBinderInScope(Koto binder, BindingSymbol owner)
    {
        // A formed Function Type binds its own per-call Origins; these do not escape its method.
        if (binder is FunctionTypeKoto)
        {
            return true;
        }

        for (var current = owner.Declaration; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, binder))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InferenceOriginInScope(BoundOrigin? origin, BindingSymbol owner, out Koto? invalid)
    {
        invalid = null;
        if (origin is null)
        {
            return true;
        }

        if (origin.Kind is OriginKind.Unbound or OriginKind.Inference || (origin.Binder is { } binder && !InferenceBinderInScope(binder, owner)))
        {
            invalid = origin.Binder;
            return false;
        }

        foreach (var operand in (BoundOrigin[])origin.Operands)
        {
            if (!InferenceOriginInScope(operand, owner, out invalid))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InferenceLengthInScope(BoundLength? length, BindingSymbol owner, out Koto? invalid)
    {
        invalid = null;
        if (length is null)
        {
            return true;
        }

        if (length.Parameter is { } parameter && !InferenceBinderInScope(parameter.Scope.Owner, owner))
        {
            invalid = parameter.Declaration;
            return false;
        }

        return InferenceLengthInScope(length.Left, owner, out invalid) && InferenceLengthInScope(length.Right, owner, out invalid);
    }

    // An associated projection as written: a projection's own bound node (BindAssociatedProjection's `Right`), or an Origin application's
    // applied projection (BindAssociatedApplication's target).
    private static BoundType? ProjectionAsWritten(Koto syntax) => syntax switch
    {
        MemberAccessKoto { Right.BoundType: { Kind: BoundTypeKind.AssociatedProjection } written } => written,
        OriginApplicationKoto application when UnwrapTransparentType(application.Type)?.BoundType is { Kind: BoundTypeKind.AssociatedProjection } applied => applied,
        _ => null,
    };

    // A Type syntax node below the wrappers that bind to the Type they wrap (TransparentInner, a RootName), but a Function Type's one-input
    // list, whose group is that list (BindFunctionType).
    private static Koto WrittenSyntax(Koto syntax)
    {
        while (true)
        {
            if (syntax is SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root)
            {
                syntax = root.Operands[0];
            }
            else if (TransparentInner(syntax) is { } inner && !InputList(syntax))
            {
                syntax = inner;
            }
            else
            {
                return syntax;
            }
        }
    }

    private static bool InputList(Koto syntax) => syntax is ParenthesizedTypeKoto && syntax.Parent is FunctionTypeKoto function && ReferenceEquals(function.Parameters, syntax);

    // SPEC 8.5.5, F.5 (one row per Type syntax form): how many of the bound Type's components a Type syntax node (WrittenSyntax's) writes, in
    // order: a Constructed Type's own Type arguments, then its qualifier's (BindContainerReference's environment); an Optional's,
    // a fixed array's and a Semantics layer's one; a Tuple's elements; a Function Type's input list (a Tuple, one input in a group) and
    // result. A name writes none.
    private static int WrittenPositions(Koto syntax) => syntax switch
    {
        ParenthesizedTypeKoto or OptionalTypeKoto or FixedArrayTypeKoto or TypeSemanticsKoto { Type: not null } => 1,
        GenericsKoto generic => generic.TypeArguments.Count + WrittenPositions(generic.Identifier!),
        MemberAccessKoto member => WrittenPositions(member.Left),
        TupleTypeKoto tuple => tuple.ElementNodes.Count,
        FunctionTypeKoto => 2,
        _ => 0,
    };

    // The syntax of the bound Type's component `index` (WrittenPositions' row).
    private static Koto WrittenPosition(Koto syntax, int index) => syntax switch
    {
        ParenthesizedTypeKoto input => input.Type,
        OptionalTypeKoto optional => optional.Type,
        FixedArrayTypeKoto array => array.ElementType,
        TypeSemanticsKoto semantics => semantics.Type!,
        GenericsKoto generic => index < generic.TypeArguments.Count ? generic.TypeArguments[index] : WrittenPosition(generic.Identifier!, index - generic.TypeArguments.Count),
        MemberAccessKoto member => WrittenPosition(member.Left, index),
        TupleTypeKoto tuple => tuple.ElementNodes[index],
        FunctionTypeKoto function => index == 0 ? function.Parameters : function.ReturnType,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private void ResetAssociatedInference()
    {
        this.associatedInferenceReady = false;
        this.collectingAssociated = null;
        this.completeTypeIdentities.Clear();
        this.inferenceFailures.Clear();
        this.inferenceInvalidation.Clear();
        foreach (var source in this.inferenceSources)
        {
            source.InferenceConsumers!.Clear();
        }

        this.inferenceSources.Clear();

        this.pendingAssociatedTypes.Clear();
        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            var batch = batches.Current!;
            batch.State = 0;
            batch.Active = false;
            batch.Paths.Clear();
            batch.Normalized.Clear();
            batch.Holes.Clear();
            batch.Pairs.Clear();
            batch.CheckedTypes.Clear();
            batch.ProblemTypes.Clear();
            foreach (var evidence in batch.Evidence.Values)
            {
                evidence.Clear();
            }
        }
    }

    private void PrepareAssociatedInference()
    {
        foreach (var path in this.impls.InPass.Paths)
        {
            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                if (this.InferenceHole(path, identity))
                {
                    InferenceBatches.Batch(path.Type).Active = true;
                    break;
                }
            }
        }

        foreach (var path in this.impls.InPass.Paths)
        {
            if (this.associatedInference.TryGetValue(path.Type, out var batch) && batch.Active)
            {
                batch.Paths.Add(path);
            }
        }

        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            if (batches.Current is { Active: false } inactive)
            {
                InferenceBatches.Remove(inactive);
            }
        }

        this.associatedInferenceReady = true;
        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            if (batches.Current is { Paths.Count: > 0 } batch)
            {
                this.InferAssociatedTypes(batch);
            }
        }

        // Explicit equations may point at a newly completed binding. Normalize their values again,
        // preserving their original candidates for final formation/projection verification.
        foreach (var path in this.impls.InPass.Paths)
        {
            if (path.AssociatedBindings is { } bindings)
            {
                foreach (var (_, binding) in bindings)
                {
                    binding.State = 0;
                }
            }
        }

        foreach (var path in this.impls.InPass.Paths)
        {
            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                if (this.ResolveAssociated(path, identity, out _) is { } result && !this.PendingAssociatedType(result))
                {
                    path.AssociatedStorage[identity] = result;
                    if (this.AssociatedBindingOf(path.RootPath, identity) is { } binding)
                    {
                        foreach (var evidence in binding.EvidenceSources)
                        {
                            path.InferenceStorage.Add(new(identity, result, evidence.Path, evidence.Source));
                            this.AddInferenceConsumer(evidence.Path, path);
                        }
                    }
                }
            }
        }

        foreach (var path in this.impls.InPass.Paths)
        {
            this.PublishInheritedInference(path);
        }
    }

    private void AddInferenceConsumer(BoundConformancePath source, BoundConformancePath target)
    {
        if (ReferenceEquals(source, target))
        {
            return;
        }

        var consumers = source.InferenceConsumers ??= new();
        if (consumers.Count == 0)
        {
            this.inferenceSources.Add(source);
        }

        if (!consumers.Contains(target))
        {
            consumers.Add(target);
        }
    }

    private void PublishInheritedInference(BoundConformancePath path)
    {
        if (path.InheritedFrom is null || path.InferencePublished)
        {
            return;
        }

        path.InferencePublished = true;

        for (var i = 0; i < path.InheritedSourceCount; i++)
        {
            var source = path.InheritedSource(i);
            this.PublishInheritedInference(source);
            this.AddInferenceConsumer(source, path);
            foreach (var evidence in source.InferenceStorage)
            {
                var identity = new BoundRequirement(evidence.Identity.Symbol, this.SubstituteRequirementContract(evidence.Identity.Contract, path.InheritedBase!));
                if (path.AssociatedStorage.TryGetValue(identity, out var type))
                {
                    path.InferenceStorage.Add(new(identity, type, source, evidence.Source));
                }
            }
        }
    }

    private void InferAssociatedTypes(AssociatedInference batch)
    {
        if (batch.State != 0)
        {
            return;
        }

        batch.State = 1;
        var previous = this.collectingAssociated;
        this.collectingAssociated = batch;
        var held = this.BeginFormation(); // SPEC 8.5.5, 8.6.3: the inferred bindings are formed from the signatures, whatever the reader.
        try
        {
            var hasHole = false;
            foreach (var path in batch.Paths)
            {
                foreach (var identity in path.Contract.Contract!.AssociatedStorage)
                {
                    hasHole |= this.InferenceHole(path, identity);
                }
            }

            if (!hasHole)
            {
                return;
            }

            foreach (var path in batch.Paths)
            {
                if (path.InheritedFrom is not null)
                {
                    continue;
                }

                var self = this.ConformanceSelf(path);
                foreach (var identity in path.Contract.Contract!.RequirementStorage)
                {
                    if (identity.Contract != path.Contract || identity.Symbol.Declaration is not FunctionKoto requirement)
                    {
                        continue;
                    }

                    this.BindHeader(identity.Symbol);
                    var match = this.IdentifyRequirement(path, identity);
                    if (match.State != RequirementMatchState.Unique && identity.Symbol.Type is { } waitingResult)
                    {
                        batch.Pairs.Clear();
                        Koto? input = null;
                        if (match.State == RequirementMatchState.Pending)
                        {
                            for (var i = 0; i < requirement.Parameters.Count; i++)
                            {
                                var syntax = requirement.Parameters[i].Type;
                                if (syntax.BoundType is not { } parameter || this.PendingAssociatedType(this.ContractType(parameter, path.Scope, self)))
                                {
                                    input = syntax;
                                    break;
                                }
                            }
                        }

                        this.RecordInferenceProblem(path, this.ContractType(waitingResult, path.Scope, self), match.State == RequirementMatchState.Pending ? InferenceProblem.Input : match.State == RequirementMatchState.Ambiguous ? InferenceProblem.Ambiguous : InferenceProblem.Missing, requirement, input);
                    }

                    if (match.State != RequirementMatchState.Unique || match.Member!.Declaration is not FunctionKoto implementation ||
                        requirement.BoundSymbol!.Type is not { } required || match.Member.Type is not { } actual)
                    {
                        continue;
                    }

                    required = this.ContractType(required, path.Scope, self);
                    actual = this.ContractType(this.MemberType(actual, match.Selection.DeclaringType)!, path.Scope);
                    batch.Pairs.Clear();
                    batch.Signature = (required, match.Selection.DeclaringType);
                    this.ExtractAssociatedEvidence(batch, path, required, actual, implementation);
                }
            }

            // Collection is finished for the entire nominal definition before any inferred value is visible.
            foreach (var path in batch.Paths)
            {
                if (path != path.RootPath || path.InheritedFrom is not null)
                {
                    continue;
                }

                foreach (var identity in path.Contract.Contract!.AssociatedStorage)
                {
                    if (!this.InferenceHole(path, identity) || !batch.Evidence.TryGetValue(identity.Symbol, out var evidence))
                    {
                        continue;
                    }

                    var binding = this.AssociatedBindingOf(path, identity)!;
                    foreach (var source in evidence)
                    {
                        if (!this.SameConformanceType(source.Identity.Contract.Type!, identity.Contract.Type!, path.Scope) ||
                            this.ProveConformanceConditions(source.Path, this.SelfType(path.Type), path.Scope) != ConstraintProof.Proven)
                        {
                            continue;
                        }

                        var type = this.ContractType(source.Type, path.Scope);
                        if (binding.Inferred is not null && !this.SameCompleteType(binding.Inferred, type))
                        {
                            binding.InferenceConflict = true;
                            binding.Problem = InferenceProblem.Conflict;
                            binding.ProblemSource = source.Source;
                            binding.ProblemDetail = null;
                        }

                        binding.Inferred = type;
                        binding.InferenceSource ??= source.Source;
                        binding.EvidenceSources.Add(source);
                    }
                }
            }
        }
        finally
        {
            this.EndFormation(held);
            this.collectingAssociated = previous;
            batch.State = 2;
            this.pendingAssociatedTypes.Clear();
        }
    }

    private bool InferenceHole(BoundConformancePath path, BoundRequirement identity)
        => path.InheritedFrom is null && this.AssociatedParameters(identity.Symbol.Declaration).Length == 0 &&
        this.AssociatedBindingOf(path.RootPath, identity) is { Candidates.Count: 0 } &&
        this.SpecificationPart(path, identity) is null;

    // SPEC 15.3.3, 15.4.4, 23.1.2: the explicit specification of an associated identity in the conformance's declaration or its
    // conditional block that fixes its Type, or that did not bind. An identity so specified is never an inference hole, a missing
    // binding rests on the clause, or on its specified Type when that fails instead (SettleSpecificationLinks), and a binding on an
    // inline cycle is reported there (SPEC 6.2, DECISIONS BU-128).
    private IsKoto? SpecificationPart(BoundConformancePath path, BoundRequirement identity)
    {
        var root = path.RootPath;
        if (root.Declaration.Parent is SyntaxFormKoto syntax && TryConditionalBlock(syntax, out var block))
        {
            for (var i = 0; i < block.Items.Count; i++)
            {
                if (Failed(block.Items[i]) is { } conditional)
                {
                    return conditional;
                }
            }
        }

        var container = (DeclarationContainerKoto)root.Type.Declaration;
        for (var i = 0; i < container.Members.Count; i++)
        {
            if (Failed(container.Members[i]) is { } member)
            {
                return member;
            }
        }

        return null;

        // A clause that fixes the Type (SPEC 8.5.1: conjunction elimination yields its identity atom, AssociatedIdentityType; `is i32 and
        // Copy` specifies i32), or one that did not bind and so cannot be told apart; a capability-only clause (`is Copy`) fixes nothing
        // and leaves the identity to inference.
        IsKoto? Failed(Koto item)
            => item is IsKoto { IsAssociatedConstraint: true } clause && this.SpecifiesContract(clause, root.RootContract) &&
                ReferenceEquals(clause.Left.BoundSymbol ?? AssociatedHead(clause)?.BoundSymbol, identity.Symbol) &&
                (clause.BindingFailure != BindingFailure.None || clause.BindingState != BindingState.Resolved ||
                    clause.BoundConstraint is null or { Kind: ConstraintKind.Error or ConstraintKind.Unresolved } ||
                    AssociatedIdentityType(clause.BoundConstraint) is not null)
                ? clause : null;
    }

    // SPEC 23.1.2: a conformance resting on a specification is derived from what has failed by publication: the clause, or its
    // specified Type when only that failed, such as a formation proven at the Definition deadline.
    private void SettleSpecificationLinks()
    {
        for (var i = 0; this.specificationLinks is { } links && i < links.Count; i++)
        {
            if (this.partPrerequisites.TryGetValue(links[i], out var part) && part is IsKoto { BindingFailure: BindingFailure.None, BindingState: not BindingState.Invalid, Right: { BindingState: BindingState.Invalid } specified })
            {
                this.partPrerequisites[links[i]] = specified;
            }
        }
    }

    private void ExtractAssociatedEvidence(AssociatedInference batch, BoundConformancePath path, BoundType required, BoundType actual, FunctionKoto source)
    {
        if (!this.ContainsInferenceHole(batch, path, required) || !batch.Pairs.Add((required, actual)))
        {
            return;
        }

        if (required.Kind == BoundTypeKind.AssociatedProjection && ReferenceEquals(required.Components[0].Symbol, path.Type) &&
            this.AssociatedInPath(
                path.RootPath,
                required.Components[0],
                required.Symbol!,
                required.Components.Count == 2 ? required.Components[1] : null,
                path.Scope) is { } identity &&
            this.InferenceHole(path, identity))
        {
            batch.CheckedTypes.Clear();
            if (this.PendingAssociatedType(actual))
            {
                this.RecordInferenceProblem(path, required, InferenceProblem.Dependency, source);
                return;
            }

            if (!this.InferenceTypeInScope(actual, path.Type, batch, out var invalid))
            {
                this.RecordInferenceProblem(path, required, InferenceProblem.Scope, source, invalid);
                return;
            }

            if (!batch.Evidence.TryGetValue(identity.Symbol, out var evidence))
            {
                batch.Evidence.Add(identity.Symbol, evidence = new());
            }

            evidence.Add(new(path, identity, actual, source, batch.Signature.Required, batch.Signature.DeclaringType));
            return;
        }

        if (required.Kind != actual.Kind || required.Symbol != actual.Symbol || required.Semantics != actual.Semantics || required.Components.Count != actual.Components.Count ||
            required.Kind is not (BoundTypeKind.Constructed or BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Semantics or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice) ||
            (required.Kind == BoundTypeKind.FixedArray && (required.Length != actual.Length || !ReferenceEquals(required.LengthExpression, actual.LengthExpression))))
        {
            this.RecordInferenceProblem(path, required, InferenceProblem.Shape, source);
            return;
        }

        for (var i = 0; i < required.Components.Count; i++)
        {
            this.ExtractAssociatedEvidence(batch, path, required.Components[i], actual.Components[i], source);
        }
    }

    // SPEC 8.5.5, 8.6.3 (R7-C2e2 e2-3b Revision 4, R4.9): an inferred binding read by a requirement's evidence is the same contract as the
    // equal explicit specification, read from every source's implementation signature as written (HolePart): the one complete Type all
    // usable evidence gives, a part left pending (HeldPending) only where every source's written form leaves it so (JoinEvidence), so the
    // order of sources never matters. None, pending, when every source's hole lies inside a projection the evidence holds pending.
    private BoundType? InferredEvidence(AssociatedBinding binding, out bool pending)
    {
        BoundType? image = null;
        foreach (var source in binding.EvidenceSources)
        {
            if (this.HolePart(source.Path, source.Identity, source.Required, source.Source.BoundSymbol!.Type!, source.Source.ReturnType, source.DeclaringType) is { } part)
            {
                image = image is null ? part : this.JoinEvidence(image, part, binding.Inferred!);
            }
        }

        pending = image is null;
        return image;
    }

    // SPEC 8.5.5: the part at the hole `identity` of `required` (ExtractAssociatedEvidence's pairing) in an implementation's result, formed in
    // the reading evidence: `image` as bound with `syntax` its written form (FormedResult substitutes and forms it), or with no syntax `image`
    // formed already. The walk follows the extraction's structure (Function Types are not entered) and places the written form at the
    // hole only (WrittenType); a hole inside a written projection, or inside a part the conformance's scope forms into another shape, is
    // reached through that part's formed image, where a projection the evidence holds pending names no Type: none, as for no hole.
    private BoundType? HolePart(BoundConformancePath path, BoundRequirement identity, BoundType required, BoundType image, Koto? syntax, BoundType? declaringType)
    {
        if (required.Kind == BoundTypeKind.AssociatedProjection && ReferenceEquals(required.Components[0].Symbol, path.Type) &&
            this.AssociatedInPath(path.RootPath, required.Components[0], required.Symbol!, required.Components.Count == 2 ? required.Components[1] : null, path.Scope) == identity)
        {
            return syntax is null ? image : this.FormedResult(this.WrittenType(syntax, image), path, declaringType);
        }

        syntax = syntax is null ? null : WrittenSyntax(syntax);
        var shaped = required.Kind == image.Kind && required.Symbol == image.Symbol && required.Components.Count == image.Components.Count &&
            (syntax is null || WrittenPositions(syntax) == image.Components.Count);
        if (shaped && required.Kind is not (BoundTypeKind.Constructed or BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Semantics or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice))
        {
            return null;
        }

        if (syntax is not null && (!shaped || ProjectionAsWritten(syntax) is not null))
        {
            return this.HolePart(path, identity, required, this.FormedResult(this.WrittenType(syntax, image), path, declaringType), null, null);
        }

        for (var i = 0; shaped && i < required.Components.Count; i++)
        {
            if (this.HolePart(path, identity, required.Components[i], image.Components[i], syntax is null ? null : WrittenPosition(syntax, i), declaringType) is { } part)
            {
                return part;
            }
        }

        return null;
    }

    // An implementation's Type as bound, in the conformance's terms and formed in the reading answer.
    private BoundType FormedResult(BoundType type, BoundConformancePath path, BoundType? declaringType)
        => this.ContractType(this.MemberType(type, declaringType)!, path.Scope);

    // SPEC 8.5.5: an implementation result's part as written: `image`, the Type `syntax` bound to, with each associated projection written in
    // it in place of the Type it was normalized to (ProjectionAsWritten). Each Type syntax form places its Type positions on the image's
    // components by its own row (WrittenPositions, WrittenPosition), never by Koto.TypeComponent; a name holds none (Self, a parameter). A
    // written projection the rows place on no component is a broken contract (InternalInvariant_Kd), never read as its normal form.
    private BoundType WrittenType(Koto syntax, BoundType image)
    {
        syntax = WrittenSyntax(syntax);
        if (ProjectionAsWritten(syntax) is { } projection)
        {
            return projection;
        }

        var count = image.Components.Count;
        if (WrittenPositions(syntax) != count)
        {
            return !WritesProjection(syntax) ? image
                : throw new InvalidOperationException($"A projection written in `{image}` is placed on none of its parts (InternalInvariant; SPEC 8.5.5).");
        }

        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                components[i] = this.WrittenType(WrittenPosition(syntax, i), image.Components[i]);
                changed |= !ReferenceEquals(components[i], image.Components[i]);
            }

            return changed ? this.types.Rebuild(image, components.AsSpan(0, count), image.Origin, (BoundOrigin[])image.OriginArguments) : image;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }

        static bool WritesProjection(Koto syntax)
        {
            syntax = WrittenSyntax(syntax);
            var writes = ProjectionAsWritten(syntax) is not null;
            for (var i = 0; !writes && i < WrittenPositions(syntax); i++)
            {
                writes = WritesProjection(WrittenPosition(syntax, i));
            }

            return writes;
        }
    }

    // SPEC 8.5.5 (R4.9): two sources' images of one inferred binding (`normal`, the binding) together: a part stays as written, pending, only
    // where both leave it so; every other part is the binding's.
    private BoundType JoinEvidence(BoundType left, BoundType right, BoundType normal)
    {
        if (ReferenceEquals(left, right) || ReferenceEquals(right, normal) || !Shaped(left))
        {
            return right;
        }

        if (ReferenceEquals(left, normal) || !Shaped(right))
        {
            return left;
        }

        var count = normal.Components.Count;
        var components = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                components[i] = this.JoinEvidence(left.Components[i], right.Components[i], normal.Components[i]);
            }

            return this.types.Rebuild(left, components.AsSpan(0, count), left.Origin, (BoundOrigin[])left.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }

        // A part of the binding's shape (a projection left as written is none, unless it is the binding's own part).
        bool Shaped(BoundType part)
            => part.Kind == normal.Kind && part.Kind != BoundTypeKind.AssociatedProjection && part.Symbol == normal.Symbol && part.Components.Count == normal.Components.Count;
    }

    private bool InferenceTypeInScope(BoundType type, BindingSymbol owner, AssociatedInference batch, out Koto? invalid)
    {
        invalid = null;
        if (!batch.CheckedTypes.Add(type))
        {
            return true;
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication &&
            type.Symbol is { } parameter && !InferenceBinderInScope(parameter.Scope.Owner, owner))
        {
            invalid = parameter.Declaration;
            return false;
        }

        if (!InferenceOriginInScope(type.Origin, owner, out invalid) || !InferenceLengthInScope(type.LengthExpression, owner, out invalid))
        {
            return false;
        }

        foreach (var origin in (BoundOrigin[])type.OriginArguments)
        {
            if (!InferenceOriginInScope(origin, owner, out invalid))
            {
                return false;
            }
        }

        foreach (var length in type.LengthArguments)
        {
            if (!InferenceLengthInScope(length, owner, out invalid))
            {
                return false;
            }
        }

        foreach (var component in (BoundType[])type.Components)
        {
            if (!this.InferenceTypeInScope(component, owner, batch, out invalid))
            {
                return false;
            }
        }

        return true;
    }

    private void RecordInferenceProblem(BoundConformancePath path, BoundType required, InferenceProblem problem, FunctionKoto source, Koto? detail = null)
    {
        this.collectingAssociated!.ProblemTypes.Clear();
        this.RecordInferenceProblemCore(path, required, problem, source, detail);
    }

    private bool ContainsInferenceHole(AssociatedInference batch, BoundConformancePath path, BoundType type)
    {
        var key = (path.RootPath, type);
        if (batch.Holes.TryGetValue(key, out var found))
        {
            return found;
        }

        found = type.Kind == BoundTypeKind.AssociatedProjection && ReferenceEquals(type.Components[0].Symbol, path.Type) &&
            this.AssociatedInPath(
                path.RootPath,
                type.Components[0],
                type.Symbol!,
                type.Components.Count == 2 ? type.Components[1] : null,
                path.Scope) is { } identity &&
            this.InferenceHole(path, identity);
        for (var i = 0; !found && i < type.Components.Count; i++)
        {
            found = this.ContainsInferenceHole(batch, path, type.Components[i]);
        }

        batch.Holes[key] = found;
        return found;
    }

    private void RecordInferenceProblemCore(BoundConformancePath path, BoundType required, InferenceProblem problem, FunctionKoto source, Koto? detail)
    {
        if (!this.collectingAssociated!.ProblemTypes.Add(required))
        {
            return;
        }

        if (required.Kind == BoundTypeKind.AssociatedProjection && required.Components[0].Symbol == path.Type &&
            this.AssociatedInPath(
                path.RootPath,
                required.Components[0],
                required.Symbol!,
                required.Components.Count == 2 ? required.Components[1] : null,
                path.Scope) is { } identity &&
            this.InferenceHole(path, identity))
        {
            var binding = this.AssociatedBindingOf(path.RootPath, identity)!;
            if (problem > binding.Problem)
            {
                binding.Problem = problem;
                binding.ProblemSource = source;
                binding.ProblemDetail = detail;
            }

            return;
        }

        // This diagnostic-only walk follows the required graph, including excluded structural positions.
        foreach (var component in (BoundType[])required.Components)
        {
            this.RecordInferenceProblemCore(path, component, problem, source, detail);
        }
    }

    private void InvalidateAssociatedEvidence()
    {
        foreach (var source in this.inferenceSources)
        {
            if (!source.IsVerified)
            {
                this.inferenceInvalidation.Enqueue(source);
            }
        }

        while (this.inferenceInvalidation.TryDequeue(out var source))
        {
            if (source.InferenceConsumers is not { } consumers)
            {
                continue;
            }

            foreach (var consumer in consumers)
            {
                if (consumer.IsVerified)
                {
                    consumer.IsVerified = false;
                    consumer.Invalid = true;
                    consumer.Identity.IsVerified = false;
                    if (!ReferenceEquals(consumer.Use, source.Use))
                    {
                        this.AddPrerequisite(consumer.Use, source.Use);
                        this.Fail(consumer.Use, BindingFailure.UnprovenConstraint);
                    }

                    this.inferenceInvalidation.Enqueue(consumer);
                }
            }
        }
    }

    private bool ReportAssociatedInference(Koto use, Kimi.Diagnostics.DiagnosticRequirement requirement)
    {
        if (!this.inferenceFailures.TryGetValue(use, out var failure))
        {
            return false;
        }

        var binding = failure.Binding;
        var identity = failure.Identity;
        var reason = binding.Problem switch
        {
            InferenceProblem.Input => "the implementation input key depends on an undetermined associated Type",
            InferenceProblem.Shape => "the declared results have no permitted structural correspondence",
            InferenceProblem.Scope => "the declared result contains a Type, length or Origin binder outside the conformance scope",
            InferenceProblem.Dependency => "the declared result depends on an undetermined associated Type in this inference unit or a Type-value cycle",
            InferenceProblem.Conflict => "declaration results supply conflicting complete Types",
            _ => "no eligible declaration result determines this associated Type",
        };
        var name = DiagnosticTypeName(identity.Contract.Type!) + "." + identity.Symbol.Name;
        var source = binding.ProblemSource ?? binding.InferenceSource;
        var related = new List<(string Role, Koto At, string? Label)> { ("associated", identity.Symbol.Declaration, name) };
        if (source is not null)
        {
            related.Add(("result", source.ReturnType ?? source, "declared result of " + source.Name));
        }

        if (binding.ProblemDetail is { } detail)
        {
            related.Add((binding.Problem == InferenceProblem.Input ? "input" : "binder", detail, binding.Problem == InferenceProblem.Input ? "undetermined input Type" : "binder outside the conformance scope"));
        }

        if (binding.InferenceSource is { } first && !ReferenceEquals(first, source))
        {
            related.Add(("result", first.ReturnType ?? first, "other declaration result"));
        }

        use.Report(requirement, DiagnosticCode.AssociatedTypeInferenceFailed_Kd, evidence: [name, reason], related: related.ToArray());
        return true;
    }

    // The pass's identification of a requirement in a path, kept on the path (BoundConformancePath.RequirementMatches) until the pass
    // ends; a pending one is identified again.
    private RequirementMatch IdentifyRequirement(BoundConformancePath path, BoundRequirement identity)
    {
        var identified = path.RequirementMatches ??= new();
        if (MatchIndex(identified, identity) is var known and >= 0 && identified[known].Match.State != RequirementMatchState.Pending)
        {
            return identified[known].Match;
        }

        var self = this.ConformanceSelf(path);
        var selection = this.LookupTypeMember(self, identity.Symbol.Name, path.Scope, self);
        var requirement = (FunctionKoto)identity.Symbol.Declaration;
        BindingSymbol? selected = null;
        var matches = 0;
        var pending = selection.Pending;
        for (var candidate = selection.Member; candidate is not null; candidate = candidate.Next)
        {
            if (candidate.Declaration is not FunctionKoto implementation || !this.Accessible(candidate, path.Scope, receiverType: self))
            {
                continue;
            }

            this.BindHeader(candidate);
            var match = this.MatchesRequirement(requirement, implementation, self, path.Scope, selection);
            if (match is null && IncompleteSignature(implementation) is { } incomplete)
            {
                this.Consulted(incomplete); // SPEC 23.1.2: a pending match read the implementation's incomplete signature (IsDerived).
            }

            pending |= match is null;
            if (match == true)
            {
                matches++;
                selected = candidate;
            }
        }

        var state = selection.Ambiguous || matches > 1 ? RequirementMatchState.Ambiguous : pending ? RequirementMatchState.Pending : selected is null ? RequirementMatchState.Missing : RequirementMatchState.Unique;
        var result = new RequirementMatch(state, selected, selection);
        if (MatchIndex(identified, identity) is var index and >= 0)
        {
            identified[index] = (identity, result); // The identification above may have stored one for the same requirement.
        }
        else
        {
            identified.Add((identity, result));
        }

        return result;

        static int MatchIndex(List<(BoundRequirement Requirement, RequirementMatch Match)> list, BoundRequirement identity)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Requirement == identity)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal enum RequirementMatchState : byte
    {
        Unique,
        Missing,
        Ambiguous,
        Pending,
    }

    internal enum InferenceProblem : byte
    {
        None,
        Shape,
        Input,
        Dependency,
        Scope,
        Conflict,
        Missing,
        Ambiguous,
    }

    internal readonly record struct RequirementMatch(RequirementMatchState State, BindingSymbol? Member, MemberSelection Selection);

    // An inference source: the hole's part (Type) of the implementation's signature, with the requirement's signature it was paired with
    // (Required, the hole unresolved in it) and the implementation's declaring Type (DeclaringType, MemberType's), from which an evidence
    // forms the part as written in Source's result (InferredEvidence).
    internal readonly record struct AssociatedEvidence(BoundConformancePath Path, BoundRequirement Identity, BoundType Type, FunctionKoto Source, BoundType Required, BoundType? DeclaringType);

    // The `associatedInference` family's one accessor (DECISIONS BU-220): a conforming Type's batch is kept in its Type's declaration's
    // module at the declaration's SyntaxId (SPEC 8.6.1: a conformance is declared in its Type's declaration, ImplTable's store rule),
    // across passes: ResetAssociatedInference resets each in place, and PrepareAssociatedInference drops one no inference hole needs.
    internal readonly struct InferenceBatches(Binding binding)
    {
        // Every kept batch in G949 D2b-d's d8 order: the source modules by ordinal, then the library, each by ascending id.
        internal FamilyRows<AssociatedInference?> Values => new(binding, static (table, _) => table.Hir.Families.AssociatedInference.Enumerate());

        // The slots the family's columns hold over the compilation's modules (AssociatedMetrics' retained storage).
        internal int Capacity
        {
            get
            {
                var capacity = Slots(binding.Library.Kotonoha.Syntax);
                foreach (var module in binding.compilation.SourceModules)
                {
                    capacity += Slots(module.Syntax);
                }

                return capacity;

                static int Slots(SyntaxTable table) => table.Hir.Families.AssociatedInference.Used + table.Hir.Families.AssociatedInference.Free;
            }
        }

        // A conforming Type's batch, made at its first need.
        internal static AssociatedInference Batch(BindingSymbol type)
            => Own(HirTables.Of(type.Declaration).Families.AssociatedInference.Ref(type.Declaration.SyntaxId) ??= new(type), type)!;

        // An enumerated batch's row: present, so the write carves nothing.
        internal static void Remove(AssociatedInference batch) => HirTables.Of(batch.Type.Declaration).Families.AssociatedInference.Ref(batch.Type.Declaration.SyntaxId) = null;

        internal bool TryGetValue(BindingSymbol type, [NotNullWhen(true)] out AssociatedInference? batch)
            => (batch = Own(type.Declaration.SyntaxOwner is { } table ? table.Hir.Families.AssociatedInference.Get(type.Declaration.SyntaxId) : null, type)) is not null;

        // A declaration's row is its Type symbol's, the one a path names (a container's BoundSymbol): any other symbol of the declaration
        // reaching it is a broken contract (DECISIONS BU-220's symbol rule).
        private static AssociatedInference? Own(AssociatedInference? batch, BindingSymbol type)
            => batch is null || ReferenceEquals(batch.Type, type) ? batch
                : throw new InvalidOperationException($"`{type.Name}` reads the inference batch of `{batch.Type.Name}` (InternalInvariant; DECISIONS BU-220).");
    }

    internal sealed class AssociatedInference(BindingSymbol type)
    {
        internal BindingSymbol Type { get; } = type;

        internal byte State { get; set; }

        internal bool Active { get; set; }

        internal List<BoundConformancePath> Paths { get; } = new();

        internal Dictionary<BindingSymbol, List<AssociatedEvidence>> Evidence { get; } = new(ReferenceEqualityComparer.Instance);

        internal Dictionary<(BoundType Type, BindingScope Scope, BoundType? Self, bool Normalize, BindingSymbol? Contract), (BoundType Image, ResolutionHops? Hops)> Normalized { get; } = new();

        internal Dictionary<(BoundConformancePath Path, BoundType Type), bool> Holes { get; } = new();

        internal HashSet<(BoundType Required, BoundType Actual)> Pairs { get; } = new();

        // The requirement's signature and the implementation's declaring Type of the extraction in progress (AssociatedEvidence).
        internal (BoundType Required, BoundType? DeclaringType) Signature { get; set; }

        internal HashSet<BoundType> CheckedTypes { get; } = new(ReferenceEqualityComparer.Instance);

        internal HashSet<BoundType> ProblemTypes { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
