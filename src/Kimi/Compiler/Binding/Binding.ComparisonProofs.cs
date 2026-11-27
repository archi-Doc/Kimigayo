// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly TypeMemo<BoundType, (ConstraintProof Proof, BoundConstraint? Failed)> comparedParts = new(); // ComparisonProof's composite parts.

    // SPEC 13.4, 13.4.1, 12.4.3: how a comparison under Equatable or Comparable composes a Type, the one rule of TyCtxt.Comparison and of
    // the comparison built-in rows (ImplTable.Builtin): elementwise over its parts (a Tuple or a safe `ref` layer),
    // natively (a built-in Type; under source operators also a numeric Type, by IEEE), else by the Contract's mapping for the Type: its
    // witness when the Type is closed, its requirement in every instance when it has an abstract part.
    internal static ComparisonKind ComparisonKindOf(BoundType self, KimiDeclarationId? contract, bool operators)
        => TyCtxt.IsComposite(self) ? ComparisonKind.Composite
            : TyCtxt.OperatorTableRuns(self, contract) || (operators && self.IsNumeric) ? ComparisonKind.Builtin
            : self.HasAbstractPart ? ComparisonKind.Requirement : ComparisonKind.Witness;

    // SPEC 13.4.1: whether a comparison under `contract` composes a Type: each part of a composite (ComparisonKindOf) by its own, a
    // numeric one natively under source operators, any other by its conformance; the requirement the answer first failed on. A composite
    // part's answer depends on the part alone in one query (its contract, scope and operators fixed), so each distinct one is composed
    // once (DECISIONS BU-19, BU-55).
    private ConstraintProof ComparisonProof(
        BoundType type, BindingSymbol contract, BindingScope scope, QuerySite site, bool operators, out BoundConstraint? failedRequirement)
    {
        (var proof, failedRequirement) = this.ComparisonProof(type, contract, scope, site, operators, this.comparedParts.Begin(this.storageVersion));
        return proof;
    }

    private (ConstraintProof Proof, BoundConstraint? Failed) ComparisonProof(
        BoundType type, BindingSymbol contract, BindingScope scope, QuerySite site, bool operators, uint query)
    {
        if (!TyCtxt.IsComposite(type))
        {
            if (operators && type.IsNumeric)
            {
                return (ConstraintProof.Proven, null);
            }

            var requirement = this.InternConstraint(new(ConstraintKind.Contract, type, contract: contract));
            var proof = this.ProveConstraint(requirement, scope, site);
            return (proof, proof == ConstraintProof.Proven ? null : requirement);
        }

        if (this.comparedParts.TryGet(type, query, out var known))
        {
            return known;
        }

        var result = (Proof: ConstraintProof.Proven, Failed: (BoundConstraint?)null);
        for (var i = 0; i < type.Components.Count; i++)
        {
            var part = this.ComparisonProof(type.Components[i], contract, scope, site, operators, query);
            var combined = CombineProof(result.Proof, part.Proof, true);
            result = (combined, combined != result.Proof ? part.Failed : result.Failed);
        }

        this.comparedParts.Set(type, query, result);
        return result;
    }
}
