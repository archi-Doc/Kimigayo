// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BindingSymbol Site, BoundType Type, KimiDeclarationId Contract, bool Operators), (ulong Version, BoundComparison Plan)> comparisonPlans = new();

    internal static bool HasDictionarySearch(BoundCall site)
        => site.Target.CompilerFunction is CompilerFunctionKind.DictionaryTryInsert or CompilerFunctionKind.DictionaryInsertOrReplace or CompilerFunctionKind.DictionaryRemove or CompilerFunctionKind.DictionaryTryGet;

    // One verified tree supplies both effect traversal and generation. Selection of a leaf uses
    // the conformance witness map, exactly like an explicit requirement invocation.
    internal BoundComparison? ComparisonPlan(BoundCall site)
        => HasDictionarySearch(site) && site.DeclaringType is { Kind: BoundTypeKind.Dictionary, Components.Count: 2 } dictionary
            ? this.ComparisonPlan(site, dictionary.Components[0], KimiDeclarationId.Equatable, false)
            : site.ConformingType is { } self ? this.ComparisonPlan(site, self, site.Target.CompilerFunction == CompilerFunctionKind.BuiltinEquals ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable, site.TupleOperator) : null;

    private BoundComparison? ComparisonPlan(BoundCall site, BoundType self, KimiDeclarationId identity, bool operators)
    {
        var key = (site.Target, self, identity, operators);
        if (this.comparisonPlans.TryGetValue(key, out var cached) && cached.Version == this.storageVersion)
        {
            return cached.Plan;
        }

        BoundCall? implementation = null;
        BoundComparison[] parts = [];
        if (ComparisonTypes.IsComposite(self))
        {
            parts = new BoundComparison[self.Components.Count];
            for (var i = 0; i < parts.Length; i++)
            {
                if (this.ComparisonPlan(site, self.Components[i], identity, operators) is not { } part)
                {
                    return null;
                }

                parts[i] = part;
            }
        }
        else if (!ComparisonTypes.IsBuiltin(self, identity) && !(operators && self.IsNumeric))
        {
            implementation = this.RequirementImplementation(site, self, identity, identity == KimiDeclarationId.Equatable ? "equals" : "compare");
            if (implementation is null)
            {
                return null;
            }
        }

        var plan = new BoundComparison(self, identity == KimiDeclarationId.Equatable, operators, implementation, parts);
        this.comparisonPlans[key] = (this.storageVersion, plan);
        return plan;
    }

    private ConstraintProof ComparisonProof(BoundType type, BindingSymbol contract, BindingScope scope, bool operators)
    {
        if (!ComparisonTypes.IsComposite(type))
        {
            return operators && type.IsNumeric ? ConstraintProof.Proven : this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: contract)), scope);
        }

        var result = ConstraintProof.Proven;
        for (var i = 0; i < type.Components.Count; i++)
        {
            result = CombineProof(result, this.ComparisonProof(type.Components[i], contract, scope, operators), true);
        }

        return result;
    }
}
