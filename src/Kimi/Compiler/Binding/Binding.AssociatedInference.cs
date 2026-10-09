// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BindingSymbol, AssociatedInference> associatedInference = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(BoundConformancePath Path, BoundRequirement Requirement), RequirementMatch> requirementMatches = new();
    private readonly Dictionary<BoundType, bool> pendingAssociatedTypes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, (BoundRequirement Identity, AssociatedBinding Binding)> inferenceFailures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundConformancePath, List<BoundConformancePath>> inferenceConsumers = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<BoundConformancePath> inferenceInvalidation = new();
    private readonly HashSet<BoundConformancePath> publishedInference = new(ReferenceEqualityComparer.Instance);
    private AssociatedInference? collectingAssociated;
    private bool associatedInferenceReady;

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

    private void ResetAssociatedInference()
    {
        this.associatedInferenceReady = false;
        this.collectingAssociated = null;
        this.requirementMatches.Clear();
        this.completeTypeIdentities.Clear();
        this.inferenceFailures.Clear();
        this.inferenceInvalidation.Clear();
        this.publishedInference.Clear();
        foreach (var consumers in this.inferenceConsumers.Values)
        {
            consumers.Clear();
        }

        this.pendingAssociatedTypes.Clear();
        foreach (var batch in this.associatedInference.Values)
        {
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
        foreach (var path in this.activeConformancePaths)
        {
            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                if (this.InferenceHole(path, identity))
                {
                    if (!this.associatedInference.TryGetValue(path.Type, out var batch))
                    {
                        this.associatedInference.Add(path.Type, batch = new(path.Type));
                    }

                    batch.Active = true;
                    break;
                }
            }
        }

        foreach (var path in this.activeConformancePaths)
        {
            if (this.associatedInference.TryGetValue(path.Type, out var batch) && batch.Active)
            {
                batch.Paths.Add(path);
            }
        }

        foreach (var entry in this.associatedInference)
        {
            if (!entry.Value.Active)
            {
                this.associatedInference.Remove(entry.Key);
            }
        }

        this.associatedInferenceReady = true;
        foreach (var batch in this.associatedInference.Values)
        {
            if (batch.Paths.Count != 0)
            {
                this.InferAssociatedTypes(batch);
            }
        }

        // Explicit equations may point at a newly completed binding. Normalize their values again,
        // preserving their original candidates for final formation/projection verification.
        foreach (var binding in this.associatedBindings.Values)
        {
            binding.State = 0;
        }

        foreach (var path in this.activeConformancePaths)
        {
            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                if (this.ResolveAssociated(path, identity) is { } result && !this.PendingAssociatedType(result))
                {
                    path.AssociatedStorage[identity] = result;
                    if (this.associatedBindings.TryGetValue((path.RootPath, identity), out var binding))
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

        foreach (var path in this.activeConformancePaths)
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

        if (!this.inferenceConsumers.TryGetValue(source, out var consumers))
        {
            this.inferenceConsumers.Add(source, consumers = new());
        }

        if (!consumers.Contains(target))
        {
            consumers.Add(target);
        }
    }

    private void PublishInheritedInference(BoundConformancePath path)
    {
        if (path.InheritedFrom is null || !this.publishedInference.Add(path))
        {
            return;
        }

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

                var self = this.SelfType(path.Type);
                foreach (var identity in path.Contract.Contract!.RequirementStorage)
                {
                    if (identity.Contract != path.Contract || identity.Symbol.Declaration is not FunctionKoto requirement)
                    {
                        continue;
                    }

                    this.BindHeader(identity.Symbol);
                    var match = this.IdentifyRequirement(path, identity, self);
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

                    if (match.State != RequirementMatchState.Unique || requirement.ReturnType is PlaceResultKoto ||
                        match.Member!.Declaration is not FunctionKoto implementation || implementation.ReturnType is PlaceResultKoto ||
                        requirement.BoundSymbol!.Type is not { } required || match.Member.Type is not { } actual)
                    {
                        continue;
                    }

                    required = this.ContractType(required, path.Scope, self);
                    actual = this.ContractType(this.MemberType(actual, match.Selection.DeclaringType)!, path.Scope);
                    batch.Pairs.Clear();
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

                    var binding = this.associatedBindings[(path, identity)];
                    foreach (var source in evidence)
                    {
                        if (!ReferenceEquals(this.ContractType(source.Identity.Contract.Type!, path.Scope), this.ContractType(identity.Contract.Type!, path.Scope)) ||
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
            this.collectingAssociated = previous;
            batch.State = 2;
            this.pendingAssociatedTypes.Clear();
        }
    }

    private bool InferenceHole(BoundConformancePath path, BoundRequirement identity)
        => path.InheritedFrom is null && this.AssociatedParameters(identity.Symbol.Declaration).Length == 0 &&
        this.associatedBindings.TryGetValue((path.RootPath, identity), out var binding) && binding.Candidates.Count == 0 &&
        this.SpecificationPart(path, identity) is null;

    // SPEC 15.3.3, 15.4.4, 23.3.6.4: the explicit specification of an associated identity in the conformance's declaration or its
    // conditional block that fixes its Type, or that did not bind. An identity so specified is never an inference hole, and a missing
    // binding rests on the clause, or on its specified Type when that fails instead (SettleSpecificationLinks).
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

        // A clause that fixes the Type, or one that did not bind and so cannot be told apart; a capability-only clause (`is Copy`)
        // fixes nothing and leaves the identity to inference.
        IsKoto? Failed(Koto item)
            => item is IsKoto { IsAssociatedConstraint: true } clause && SpecifiesContract(clause, root.RootContract) &&
                ReferenceEquals(clause.Left.BoundSymbol ?? AssociatedHead(clause)?.BoundSymbol, identity.Symbol) &&
                (clause.BindingFailure != BindingFailure.None || clause.BindingState != BindingState.Resolved || clause.BoundConstraint is not { Kind: ConstraintKind.Contract or ConstraintKind.And or ConstraintKind.Or or ConstraintKind.Not or ConstraintKind.Semantics or ConstraintKind.Callable })
                ? clause : null;
    }

    // SPEC 23.3.6.4: a conformance resting on a specification is derived from what has failed by publication: the clause, or its
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
            this.AssociatedInPath(path.RootPath, required.Components[0], required.Symbol!, required.Components.Count == 2 ? required.Components[1] : null) is { } identity &&
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

            evidence.Add(new(path, identity, actual, source));
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

    // Symbolic parameter projections are complete public Types; an unresolved projection on a nominal
    // declaration still needs a Type value. This query never searches bodies or introduces a proof.
    private bool PendingAssociatedType(BoundType type)
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
            this.AssociatedInPath(path.RootPath, type.Components[0], type.Symbol!, type.Components.Count == 2 ? type.Components[1] : null) is { } identity && this.InferenceHole(path, identity);
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
            this.AssociatedInPath(path.RootPath, required.Components[0], required.Symbol!, required.Components.Count == 2 ? required.Components[1] : null) is { } identity && this.InferenceHole(path, identity))
        {
            var binding = this.associatedBindings[(path.RootPath, identity)];
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
        foreach (var source in this.inferenceConsumers.Keys)
        {
            if (!source.IsVerified)
            {
                this.inferenceInvalidation.Enqueue(source);
            }
        }

        while (this.inferenceInvalidation.TryDequeue(out var source))
        {
            if (!this.inferenceConsumers.TryGetValue(source, out var consumers))
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

    private RequirementMatch IdentifyRequirement(BoundConformancePath path, BoundRequirement identity, BoundType self)
    {
        var key = (path, identity);
        if (this.requirementMatches.TryGetValue(key, out var known) && known.State != RequirementMatchState.Pending)
        {
            return known;
        }

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
            pending |= match is null;
            if (match == true)
            {
                matches++;
                selected = candidate;
            }
        }

        var state = selection.Ambiguous || matches > 1 ? RequirementMatchState.Ambiguous : pending ? RequirementMatchState.Pending : selected is null ? RequirementMatchState.Missing : RequirementMatchState.Unique;
        var result = new RequirementMatch(state, selected, selection);
        this.requirementMatches[key] = result;
        return result;
    }

    private enum InferenceProblem : byte
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

    private enum RequirementMatchState : byte
    {
        Unique,
        Missing,
        Ambiguous,
        Pending,
    }

    private readonly record struct RequirementMatch(RequirementMatchState State, BindingSymbol? Member, MemberSelection Selection);

    private readonly record struct AssociatedEvidence(BoundConformancePath Path, BoundRequirement Identity, BoundType Type, FunctionKoto Source);

    private sealed class AssociatedInference(BindingSymbol type)
    {
        internal BindingSymbol Type { get; } = type;

        internal byte State { get; set; }

        internal bool Active { get; set; }

        internal List<BoundConformancePath> Paths { get; } = new();

        internal Dictionary<BindingSymbol, List<AssociatedEvidence>> Evidence { get; } = new(ReferenceEqualityComparer.Instance);

        internal Dictionary<(BoundType Type, BindingScope Scope, BoundType? Self, bool Normalize, BindingSymbol? Contract), BoundType> Normalized { get; } = new();

        internal Dictionary<(BoundConformancePath Path, BoundType Type), bool> Holes { get; } = new();

        internal HashSet<(BoundType Required, BoundType Actual)> Pairs { get; } = new();

        internal HashSet<BoundType> CheckedTypes { get; } = new(ReferenceEqualityComparer.Instance);

        internal HashSet<BoundType> ProblemTypes { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
