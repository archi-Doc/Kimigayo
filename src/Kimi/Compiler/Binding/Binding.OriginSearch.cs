// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// PLAN G74 U3 comparison mode, temporary until U6: the previous recursive Origin search, run beside the premise closure on every
// top-level proof request when KIMI_ORIGIN_PROOF_COMPARE names an evidence file. It is pure since U1, so only the two truth values
// are compared; a search over its work budget is counted as not compared, never as agreement.
public sealed partial class Binding
{
    internal const int OriginSearchBudget = 200_000;

    private readonly HashSet<(BoundOrigin Longer, BoundOrigin Shorter)> originProofPath = new();
    private int originSearchWork;

    private void CompareOriginSearch(OriginSearchComparison comparison, BoundOrigin longer, BoundOrigin shorter, Koto use, bool entailed)
    {
        this.originSearchWork = 0;
        var searched = this.SearchResolvedOrigins(longer, shorter, use);
        if (this.originSearchWork > OriginSearchBudget)
        {
            comparison.Censor();
            return;
        }

        if (!comparison.Compare(entailed, searched))
        {
            var description = $"`{longer} outlives {shorter}` at {use.GetType().Name} {use.Span}: closure {entailed}, search {searched}";
            comparison.Mismatch(description);
            throw new InvalidOperationException($"The Origin premise closure and the previous search disagree on {description} (PLAN G74 comparison mode).");
        }
    }

    private bool SearchOriginOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use)
        => this.SearchResolvedOrigins(this.OriginAtUse(longer, use), this.OriginAtUse(shorter, use), use);

    private bool SearchResolvedOrigins(BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        // Over the budget every remaining step fails at once, and the request is counted as not compared.
        if (++this.originSearchWork > OriginSearchBudget)
        {
            return false;
        }

        if (OriginOutlives(longer, shorter))
        {
            return true;
        }

        if (!this.originProofPath.Add((longer, shorter)))
        {
            return false;
        }

        try
        {
            // Try explicit edges before transitive search. Otherwise unrelated earlier premises can enumerate
            // cyclic proof paths before reaching a directly stated relation, even for simple scalar inputs.
            for (var node = use; node is not null; node = node.Parent)
            {
                if ((node is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto || IsAssociatedRequirement(node)) &&
                    this.originDeclarations.TryGetValue(node, out var declaration) && declaration.State == 3)
                {
                    foreach (var relation in declaration.Relations)
                    {
                        if ((OriginOutlives(longer, relation.Longer) && OriginOutlives(relation.Shorter, shorter)) ||
                            (relation.Equality && OriginOutlives(longer, relation.Shorter) && OriginOutlives(relation.Longer, shorter)))
                        {
                            return true;
                        }
                    }
                }
            }

            // A borrow of a complete local Place is usable only while every stored
            // dependency is valid. Ownership verifies that availability at each use.
            // This is a premise of borrowing the Place, not a relation declared by
            // the annotation currently being checked.
            if (shorter is { Kind: OriginKind.Projection, Binder: VariableKoto variable } &&
                this.symbols.TryGetValue(variable, out var local) && local.Type is { Semantics: SemanticsKind.Owner or SemanticsKind.Ref or SemanticsKind.Uniq } stored &&
                !IsWithin(use, variable) && this.SearchStoredOriginPremise(stored, longer, use))
            {
                return true;
            }

            if (longer.Kind == OriginKind.Intersection)
            {
                var all = true;
                for (var i = 0; i < longer.Operands.Count; i++)
                {
                    all &= this.SearchOriginOutlives(longer.Operands[i], shorter, use);
                }

                if (all)
                {
                    return true;
                }
            }

            if (shorter.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < shorter.Operands.Count; i++)
                {
                    if (this.SearchOriginOutlives(longer, shorter.Operands[i], use))
                    {
                        return true;
                    }
                }
            }

            for (var node = use; node is not null; node = node.Parent)
            {
                if (IsAssociatedRequirement(node) && AssociatedFormationType(node) is { } formation &&
                    this.SearchTypeOriginPremise(formation, longer, shorter, use))
                {
                    return true;
                }

                if (this.SearchAssociatedRequirementRelation(node, longer, shorter, use))
                {
                    return true;
                }

                if (node is FunctionKoto or FunctionTypeKoto or PropertyAccessorKoto)
                {
                    // A Function Type quantifies well-formed inputs just as a declaration does. Their intrinsic
                    // relations are assumptions only while checking that signature, never in the enclosing body.
                    for (var i = 0; i < InputCount(node); i++)
                    {
                        if (this.BoundInputType(node, i) is { } input && this.SearchTypeOriginPremise(input, longer, shorter, use))
                        {
                            return true;
                        }
                    }

                    // SPEC 15.3.7: the result Type's intrinsic well-formedness is a premise of the definition (Binding.ResultPremises.cs).
                    if (!ReferenceEquals(node, this.resultPremiseExcluded) && node is FunctionKoto { IsAnonymous: false, IsConstructor: false, BoundSymbol.Type: { } result } &&
                        this.SearchWellFormedPremise(result, longer, shorter, use))
                    {
                        return true;
                    }
                }

                // Only declaration contracts are assumptions. A field or local relation
                // being checked must never prove itself.
                if ((node is not (FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto) && !IsAssociatedRequirement(node)) ||
                    !this.originDeclarations.TryGetValue(node, out var declaration) || declaration.State != 3)
                {
                    continue;
                }

                foreach (var relation in declaration.Relations)
                {
                    if (this.SearchOriginOutlives(longer, relation.Longer, use) && this.SearchOriginOutlives(relation.Shorter, shorter, use))
                    {
                        return true;
                    }

                    if (relation.Equality && this.SearchOriginOutlives(longer, relation.Shorter, use) && this.SearchOriginOutlives(relation.Longer, shorter, use))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            this.originProofPath.Remove((longer, shorter));
        }
    }

    private bool SearchStoredOriginPremise(BoundType type, BoundOrigin longer, Koto use)
    {
        if (type.Origin is { } origin && this.SearchOriginOutlives(longer, origin, use))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.SearchOriginOutlives(longer, type.OriginArguments[i], use))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.SearchStoredOriginPremise(type.Components[i], longer, use))
            {
                return true;
            }
        }

        return false;
    }

    private bool SearchTypeOriginPremise(BoundType type, BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (type.Symbol is { } symbol && this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.State == 3)
        {
            foreach (var relation in declaration.Relations)
            {
                var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                if (this.SearchOriginOutlives(longer, a, use) && this.SearchOriginOutlives(b, shorter, use))
                {
                    return true;
                }

                if (relation.Equality && this.SearchOriginOutlives(longer, b, use) && this.SearchOriginOutlives(a, shorter, use))
                {
                    return true;
                }
            }
        }

        if (TryPairLayer(type, out var whole, out var target))
        {
            // SPEC 8.1.1, 15.6.1: in the admitted borrow cases a pair layer is a borrow of its target within its outer-Origin slot,
            // whose well-formedness makes the target's Origins outlive that slot; in the value cases no Type denotes the slot.
            if (target.CarriesOrigin && (this.AdmittedSemantics(whole, this.ConstraintScope(use)) & SemanticsMask.Borrow) != 0 && this.OuterOrigin(type) is { } slot &&
                this.SearchOriginOutlives(slot, shorter, use) && this.SearchStoredOriginPremise(target, longer, use))
            {
                return true;
            }

            return this.SearchTypeOriginPremise(target, longer, shorter, use);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var inner = type.Components[i];
            if (inner.CarriesOrigin && (IsBorrow(type.Semantics) || type.Kind == BoundTypeKind.Slice) && type.Origin is { } outer && this.SearchOriginOutlives(outer, shorter, use))
            {
                if (this.SearchStoredOriginPremise(inner, longer, use))
                {
                    return true;
                }
            }

            if (this.SearchTypeOriginPremise(inner, longer, shorter, use))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6.1 well-formedness as a premise: every Origin stored below a borrow layer of the result outlives that layer's Origin.
    // A nested Function Type ends the premise: its per-call Origins cannot leave their binder (SPEC 15.3.4), so the well-formedness
    // inside it stays an obligation of the definition.
    private bool SearchWellFormedPremise(BoundType type, BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (!type.CarriesOrigin || type.Kind == BoundTypeKind.Function)
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var inner = type.Components[i];
            if ((IsBorrow(type.Semantics) || type.Kind == BoundTypeKind.Slice) && type.Origin is { } outer &&
                this.SearchOriginOutlives(outer, shorter, use) && this.SearchStoredResultPremise(inner, longer, use))
            {
                return true;
            }

            if (this.SearchWellFormedPremise(inner, longer, shorter, use))
            {
                return true;
            }
        }

        return false;
    }

    // Whether `longer` outlives an Origin stored in `type` outside any nested Function Type.
    private bool SearchStoredResultPremise(BoundType type, BoundOrigin longer, Koto use)
    {
        if (type.Kind == BoundTypeKind.Function)
        {
            return false;
        }

        if (type.Origin is { } origin && this.SearchOriginOutlives(longer, origin, use))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.SearchOriginOutlives(longer, type.OriginArguments[i], use))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.SearchStoredResultPremise(type.Components[i], longer, use))
            {
                return true;
            }
        }

        return false;
    }

    private bool SearchAssociatedRequirementRelation(Koto node, BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (AssociatedHead(node) is not OriginApplicationKoto || node.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } associated ||
            ReferenceEquals(associated.Declaration, node))
        {
            return false;
        }

        var arguments = this.AssociatedParameters(node);
        if (this.InheritedAssociatedFormation(node) is { } formation &&
            this.SearchTypeOriginPremise(formation, longer, shorter, use))
        {
            return true;
        }

        if (!this.originDeclarations.TryGetValue(associated.Declaration, out var requirement) || requirement.State != 3)
        {
            return false;
        }

        foreach (var relation in requirement.Relations)
        {
            var a = this.SubstituteStoredOrigin(relation.Longer, associated.Declaration, arguments);
            var b = this.SubstituteStoredOrigin(relation.Shorter, associated.Declaration, arguments);
            if ((this.SearchOriginOutlives(longer, a, use) && this.SearchOriginOutlives(b, shorter, use)) ||
                (relation.Equality && this.SearchOriginOutlives(longer, b, use) && this.SearchOriginOutlives(a, shorter, use)))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Counts of the temporary comparison mode (PLAN G74 U3), written per process to the named evidence file at exit.</summary>
internal sealed class OriginSearchComparison
{
    private readonly string path;
    private readonly object sync = new();
    private long compared;
    private long entailed;
    private long censored;
    private long mismatches;
    private long incomplete;
    private long incompleteUnproven;

    private OriginSearchComparison(string path)
    {
        this.path = path;
    }

    /// <summary>Gets the comparison of this process, or null when the mode is off.</summary>
    internal static OriginSearchComparison? Active { get; } = Create();

    internal bool Compare(bool closure, bool search)
    {
        Interlocked.Increment(ref this.compared);
        if (closure)
        {
            Interlocked.Increment(ref this.entailed);
        }

        return closure == search;
    }

    internal void Censor() => Interlocked.Increment(ref this.censored);

    // A request whose environment met an enclosing declaration contract not yet established (State 1 or 2).
    internal void RecordEnvironment(bool incomplete, bool entailed)
    {
        if (incomplete)
        {
            Interlocked.Increment(ref this.incomplete);
            if (!entailed)
            {
                Interlocked.Increment(ref this.incompleteUnproven);
            }
        }
    }

    internal void Mismatch(string description)
    {
        Interlocked.Increment(ref this.mismatches);
        lock (this.sync)
        {
            File.AppendAllText(this.path + $".{Environment.ProcessId}.mismatches.txt", description + Environment.NewLine);
        }
    }

    private static OriginSearchComparison? Create()
    {
        if (Environment.GetEnvironmentVariable("KIMI_ORIGIN_PROOF_COMPARE") is not { Length: > 0 } path)
        {
            return null;
        }

        var comparison = new OriginSearchComparison(path);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => comparison.Write();
        return comparison;
    }

    private void Write()
        => File.WriteAllText(
            this.path + $".{Environment.ProcessId}.json",
            $"{{\"compared\": {this.compared}, \"entailed\": {this.entailed}, \"censored\": {this.censored}, \"mismatches\": {this.mismatches}, \"incomplete\": {this.incomplete}, \"incompleteUnproven\": {this.incompleteUnproven}, \"budget\": {Binding.OriginSearchBudget}}}{Environment.NewLine}");
}
