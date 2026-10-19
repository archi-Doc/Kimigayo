// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns how a comparison runs on a Type in MIR terms (Comparison; DECISIONS R5-M68; R5 package F 4.6, 4.7, S2-S4 F 2, F 5, X25), by
// Binding's one composition rule (Binding.ComparisonKindOf, shared with the legacy plan) with its closed leaves from Witness
// (TyCtxt.Dispatch.cs).
// Input: Equatable or Comparable, a Type and the mode (source operators, IEEE for floats, or the Contract's mapping, whose equals is
// NaN-reflexive).
// Output: a ComparisonNode tree: Composite over its parts, Builtin, Witness (a closed leaf: Witness's Body under its declaring instance,
// Synthesized), Requirement (a leaf with an abstract part: the Contract's own requirement on the leaf in every instance, Synthesized on a
// Self) or None (no answer, a broken contract for a closed accepted Self that its reader records as InternalInvariant_Kd); null for a
// Limit (Witness's). One node per (contract, self, operators), discarded at an edit (ResetDispatch).
// Invariants: a comparison of a closed Type has the legacy plan's leaves wherever that plan exists, which it does not for an inherited
// witness (ComparisonCompositionTest's twin).
// Must not: create a bridge or a CallPlan, write a diagnostic, HIR or pass state, or prove on an erased value.

namespace Kimi.Compiler;

/// <summary>How a comparison composes a Type (<see cref="TyCtxt.Comparison"/>, <see cref="Binding.ComparisonKindOf"/>).</summary>
internal enum ComparisonKind : byte
{
    None,
    Composite,
    Builtin,
    Witness,
    Requirement,
}

/// <summary>A comparison of <see cref="Type"/>: elementwise over <see cref="Parts"/> (Composite), natively (Builtin), by the witness
/// <see cref="Target"/> under its declaring instance <see cref="Subst"/> (Witness) or by the Contract's requirement <see cref="Target"/> on
/// the Self <see cref="Subst"/> binds (Requirement); None has no answer.</summary>
internal sealed record ComparisonNode(BoundType Type, ComparisonKind Kind, BindingSymbol? Target, Subst? Subst, ComparisonNode[] Parts);

internal sealed partial class TyCtxt
{
    private readonly Dictionary<(BindingSymbol Contract, BoundType Self, bool Operators), ComparisonNode?> comparisons = new();

    // SPEC 13.4, 13.4.1, 12.4.3: how a comparison of Self under Equatable or Comparable runs, as source operators or the Contract's mapping
    // compare it.
    internal ComparisonNode? Comparison(BindingSymbol contract, BoundType self, bool operators)
    {
        if (!this.comparisons.TryGetValue((contract, self, operators), out var node))
        {
            node = this.Compose(contract, self, operators);
            this.comparisons.Add((contract, self, operators), node);
        }

        return node;
    }

    // Binding's composition rule over the leaves: a closed one runs what the Contract's requirement on it runs (Witness's Body), one with an
    // abstract part that requirement on it in every instance.
    private ComparisonNode? Compose(BindingSymbol contract, BoundType self, bool operators)
    {
        var kind = Binding.ComparisonKindOf(self, contract.LibraryDeclaration, operators);
        if (kind == ComparisonKind.Composite)
        {
            var parts = new ComparisonNode[self.Components.Count];
            for (var i = 0; i < parts.Length; i++)
            {
                if (this.Comparison(contract, self.Components[i], operators) is not { } part)
                {
                    return null;
                }

                parts[i] = part;
            }

            return new(self, kind, null, null, parts);
        }

        if (kind == ComparisonKind.Builtin)
        {
            return new(self, kind, null, null, []);
        }

        var requirement = contract.Contract!.OwnRequirement()!; // Equatable's equals, Comparable's compare.
        var subst = this.Synthesized(requirement, self, contract.Type!);
        if (kind == ComparisonKind.Requirement)
        {
            return new(self, kind, requirement, subst, []);
        }

        return this.Witness(requirement, contract, subst) switch
        {
            { Kind: RequirementTargetKind.Limit } => null,
            { Kind: RequirementTargetKind.Body, Target: { } witness, Subst: { } instance }
                => new(self, kind, witness, this.Synthesized(witness, instance.Declaring, []), []),
            _ => new(self, ComparisonKind.None, null, null, []),
        };
    }
}
