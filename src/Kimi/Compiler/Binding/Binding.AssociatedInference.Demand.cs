// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The associated-inference owner's demand side (R7-C4, PLAN G172): how the pass determines the inferred bindings and publishes them. Until
// R7-C4's cutover this is the per-definition batch driver, moved here unchanged from Binding.AssociatedInference.cs: a batch per conforming
// definition, run in declaration order and nested when a batch reads a definition whose batch has not run (SPEC 8.5.5, the defect PLAN
// G172 records). The cutover replaces it with one request per component of the definitions (design Revision 2 §3.3).
public sealed partial class Binding
{
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
                    var written = this.MemberType(this.AsWritten(implementation.ReturnType, this.scopes[implementation], actual), match.Selection.DeclaringType)!;
                    actual = this.ContractType(this.MemberType(actual, match.Selection.DeclaringType)!, path.Scope);
                    batch.Pairs.Clear();
                    batch.Source = implementation;
                    this.ExtractAssociatedEvidence(batch, path, required, actual, written, null);
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
}
