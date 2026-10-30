// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class Binding
{
    [Transitional("R7", Reason = "The legacy comparison composition beside TyCtxt.Comparison, read by A4's Witnesses until A-12 reads the view, else R7.")]
    private readonly Dictionary<(BindingSymbol Site, BoundType Type, KimiDeclarationId Contract, bool Operators), (ulong Version, BoundComparison Plan)> comparisonPlans = new();

    private readonly TypeMemo<(ConstraintProof Proof, BoundConstraint? Failed)> comparedParts = new(); // ComparisonProof's composite parts.

    // SPEC 13.4, 13.4.1, 12.4.3: how a comparison under Equatable or Comparable composes a Type, the one rule of the legacy plan, of
    // TyCtxt.Comparison and of the built-in target (CompilerRequirementTarget): elementwise over its parts (a Tuple or a safe `ref` layer),
    // natively (a built-in Type; under source operators also a numeric Type, by IEEE), else by the Contract's mapping for the Type: its
    // witness when the Type is closed, its requirement in every instance when it has an abstract part.
    internal static ComparisonKind ComparisonKindOf(BoundType self, KimiDeclarationId? contract, bool operators)
        => ComparisonTypes.IsComposite(self) ? ComparisonKind.Composite
            : ComparisonTypes.IsBuiltin(self, contract) || (operators && self.IsNumeric) ? ComparisonKind.Builtin
            : self.HasAbstractPart ? ComparisonKind.Requirement : ComparisonKind.Witness;

    // One verified tree supplies both effect traversal and generation. Selection of a leaf uses
    // the conformance witness map, exactly like an explicit requirement invocation.
    [Transitional("R6", Reason = "H11 deletes it with its readers, the old effect walk (H5) and the old generator (G7).")]
    internal BoundComparison? ComparisonPlan(CallPlan site)
        => site.ConformingType is { } self ? this.ComparisonPlan(site.Target, self, site.Target.CompilerFunction == CompilerFunctionKind.BuiltinEquals ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable, site.TupleOperator) : null;

    [Transitional("R6", Reason = "H11 deletes it with its reader, the old effect walk's Dictionary comparison (H5).")]
    internal BoundComparison? DictionaryComparison(BoundType dictionary)
        => this.ComparisonPlan(this.Library.GetSymbol(KimiDeclarationId.Dictionary)!, dictionary.Components[0], KimiDeclarationId.Equatable, false);

    // The legacy composition: CallPlan leaves from RequirementImplementation under the built-in target's scope, null where a leaf does not
    // resolve there (a Type parameter's), retained per storage version (DECISIONS R5-M68).
    [Transitional("R7", Reason = "The legacy comparison composition beside TyCtxt.Comparison, read by A4's Witnesses until A-12 reads the view, else R7.")]
    private BoundComparison? ComparisonPlan(BindingSymbol site, BoundType self, KimiDeclarationId identity, bool operators)
    {
        var key = (site, self, identity, operators);
        if (this.comparisonPlans.TryGetValue(key, out var cached) && cached.Version == this.storageVersion)
        {
            return cached.Plan;
        }

        CallPlan? implementation = null;
        BoundComparison[] parts = [];
        var kind = ComparisonKindOf(self, identity, operators);
        if (kind == ComparisonKind.Composite)
        {
            parts = cached.Plan?.Parts.Length == self.Components.Count ? cached.Plan.Parts : new BoundComparison[self.Components.Count];
            for (var i = 0; i < parts.Length; i++)
            {
                if (this.ComparisonPlan(site, self.Components[i], identity, operators) is not { } part)
                {
                    return null;
                }

                if (!ReferenceEquals(parts[i], part))
                {
                    if (ReferenceEquals(parts, cached.Plan?.Parts))
                    {
                        parts = (BoundComparison[])parts.Clone();
                    }

                    parts[i] = part;
                }
            }
        }
        else if (kind != ComparisonKind.Builtin)
        {
            implementation = this.RequirementImplementation(site, self, identity, identity == KimiDeclarationId.Equatable ? "equals" : "compare");
            if (implementation is null)
            {
                return null;
            }
        }

        // Revalidate witnesses every Binding pass, retaining only unchanged immutable plans.
        var plan = cached.Plan is { } previous && ReferenceEquals(previous.Implementation, implementation) && ReferenceEquals(previous.Parts, parts)
            ? previous : new BoundComparison(self, identity == KimiDeclarationId.Equatable, operators, implementation, parts);
        this.comparisonPlans[key] = (this.storageVersion, plan);
        return plan;
    }

    // SPEC 13.4.1: whether a comparison under `contract` composes a Type: each part of a composite (ComparisonKindOf) by its own, a
    // numeric one natively under source operators, any other by its conformance; the requirement the answer first failed on. A composite
    // part's answer depends on the part alone in one query (its contract, scope and operators fixed), so each distinct one is composed
    // once (DECISIONS BU-19, BU-55).
    private ConstraintProof ComparisonProof(
        BoundType type, BindingSymbol contract, BindingScope scope, bool operators, out BoundConstraint? failedRequirement)
    {
        (var proof, failedRequirement) = this.ComparisonProof(type, contract, scope, operators, this.comparedParts.Begin(this.storageVersion));
        return proof;
    }

    private (ConstraintProof Proof, BoundConstraint? Failed) ComparisonProof(
        BoundType type, BindingSymbol contract, BindingScope scope, bool operators, uint query)
    {
        if (!ComparisonTypes.IsComposite(type))
        {
            if (operators && type.IsNumeric)
            {
                return (ConstraintProof.Proven, null);
            }

            var requirement = this.InternConstraint(new(ConstraintKind.Contract, type, contract: contract));
            var proof = this.ProveConstraint(requirement, scope);
            return (proof, proof == ConstraintProof.Proven ? null : requirement);
        }

        if (this.comparedParts.TryGet(type, query, out var known))
        {
            return known;
        }

        var result = (Proof: ConstraintProof.Proven, Failed: (BoundConstraint?)null);
        for (var i = 0; i < type.Components.Count; i++)
        {
            var part = this.ComparisonProof(type.Components[i], contract, scope, operators, query);
            var combined = CombineProof(result.Proof, part.Proof, true);
            result = (combined, combined != result.Proof ? part.Failed : result.Failed);
        }

        this.comparedParts.Set(type, query, result);
        return result;
    }
}
