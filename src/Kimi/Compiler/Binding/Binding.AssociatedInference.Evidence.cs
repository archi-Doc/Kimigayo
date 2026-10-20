// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Collects requirement-result evidence for an active nominal inference batch, then applies it
// after collection under the existing source-condition and complete-Type agreement checks.
// The caller owns collector lifetime, State 1/2 and memo reset. Stored inferred values are not
// verified conformances; later validation retains its existing invalidation responsibility.
public sealed partial class Binding
{
    private void CollectInferenceEvidence(AssociatedInference batch)
    {
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
                        input = this.PendingInferenceInput(requirement, path.Scope, self);
                    }

                    var problem = match.State == RequirementMatchState.Pending ? InferenceProblem.Input :
                        match.State == RequirementMatchState.Ambiguous ? InferenceProblem.Ambiguous : InferenceProblem.Missing;
                    this.RecordInferenceProblem(
                        path,
                        this.ContractType(waitingResult, path.Scope, self),
                        problem,
                        requirement,
                        input);
                }

                if (match.State != RequirementMatchState.Unique || match.Member!.Declaration is not FunctionKoto implementation ||
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
    }

    private void ApplyInferenceEvidence(AssociatedInference batch)
    {
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
                    if (!ReferenceEquals(
                        this.ContractType(source.Identity.Contract.Type!, path.Scope), this.ContractType(identity.Contract.Type!, path.Scope)) ||
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

    private Koto? PendingInferenceInput(FunctionKoto requirement, BindingScope scope, BoundType self)
    {
        for (var i = 0; i < requirement.Parameters.Count; i++)
        {
            var syntax = requirement.Parameters[i].Type;
            if (syntax.BoundType is not { } parameter || this.PendingAssociatedType(this.ContractType(parameter, scope, self)))
            {
                return syntax;
            }
        }

        return null;
    }
}
