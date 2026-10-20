// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Owns the existing reset and final validation of retained conformance state for a Binding pass.
// Reset clears pass-local results while retaining storage. Validation rechecks the selected paths,
// invalidates dependent evidence and publishes typed failures in their existing order.
public sealed partial class Binding
{
    private void ResetContracts()
    {
        foreach (var bound in this.boundContracts.Values)
        {
            bound.Contract!.State = 0;
        }

        this.ResetAssociatedInference();
        this.failedConformance = null;
        this.contractHeadersReady = false;
        this.bindingConstraintTypes = false;
        this.activeConformancePaths.Clear();
        foreach (var list in this.conformancesByType.Values)
        {
            list.Clear();
        }

        this.projectionUses.Clear();
        this.userIterations.Clear();
        this.associatedApplications.Clear();
        this.associatedAmbiguities?.Clear();
        this.contractAgreementFailures?.Clear();
        this.ambiguousAssociatedMatches?.Clear();
        this.memberSelections.Clear();
        foreach (var group in this.requirementGroups.Values)
        {
            group.Active = false;
        }

        foreach (var binding in this.associatedBindings.Values)
        {
            binding.Candidates.Clear();
            binding.Result = null;
            binding.Inferred = null;
            binding.InferenceSource = null;
            binding.InferenceConflict = false;
            binding.Problem = InferenceProblem.None;
            binding.ProblemSource = null;
            binding.ProblemDetail = null;
            binding.EvidenceSources.Clear();
            binding.State = 0;
            binding.FormationScope?.Reset();
        }

        foreach (var conformance in this.conformances.Values)
        {
            conformance.IsVerified = false;
            conformance.Invalid = false;
            conformance.DirectClause = null;
            conformance.PathStorage.Clear();
        }

        foreach (var conformance in this.conformancePaths.Values)
        {
            conformance.Active = false;
            conformance.IsVerified = false;
            conformance.Checking = false;
            conformance.RejectedSelfSignature = null;
            conformance.Invalid = false;
            conformance.WitnessStorage.Clear();
            conformance.WitnessMap.Clear();
            conformance.PropertyWitnessStorage.Clear();
            conformance.PropertyWitnessMap.Clear();
            conformance.Unsupported = false;
            conformance.Needed = false;
            conformance.AssociatedStorage.Clear();
            conformance.InferenceStorage.Clear();
            conformance.Scope.Reset();
        }
    }

    private void ValidateConformances(BindingMode mode, bool final)
    {
        this.contractHeadersReady = true;
        if (final)
        {
            // Body/header validation may invalidate an earlier declaration-side witness of the sources. The Kimi library's
            // declarations are valid and fixed (a malformed library fails the pass), so a library conformance proven before the
            // bodies stays proven, and only an unproven one is judged again.
            foreach (var identity in this.conformances.Values)
            {
                if (!this.IsLibraryDeclaration(identity.Type.Declaration))
                {
                    identity.IsVerified = false;
                }
            }

            for (var i = 0; i < this.activeConformancePaths.Count; i++)
            {
                if (!this.IsLibraryDeclaration(this.activeConformancePaths[i].Type.Declaration))
                {
                    this.activeConformancePaths[i].IsVerified = false;
                }
            }
        }

        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            var conformance = this.activeConformancePaths[i];
            this.failedConformance = null;
            var proof = this.VerifyConformance(conformance);
            if (final && conformance.InheritedFrom is null)
            {
                if (conformance.Unsupported && mode == BindingMode.Final)
                {
                    this.Fail(conformance.Use, BindingFailure.Unsupported); // SPEC 12.7, 23.1.1: the limit's one location
                    continue;
                }

                this.RequireConstraint(conformance.Use, proof, mode);
            }
        }

        if (final)
        {
            this.InvalidateAssociatedEvidence();
            foreach (var identity in this.conformances.Values)
            {
                if (identity.PathStorage.Count != 0)
                {
                    this.failedConformance = null;
                    this.conformanceChecks++;
                    var agreement = this.VerifyConformanceAgreement(identity);
                    this.conformanceChecks--;
                    var explicitPath = false;
                    foreach (var path in identity.PathStorage)
                    {
                        explicitPath |= path.InheritedFrom is null;
                    }

                    if (explicitPath || agreement == ConstraintProof.Error)
                    {
                        this.RequireConstraint(identity.PathStorage[0].Use, agreement, mode);
                    }
                }
            }

            this.ValidateBoundConformanceAgreement();
            for (var i = 0; i < this.projectionUses.Count; i++)
            {
                var use = this.projectionUses[i];
                this.failedConformance = null;
                this.RequireConstraint(use.Use, this.ProveConformance(use.Type, use.Contract, this.ConstraintScope(use.Use)), mode);
            }
        }
    }
}
