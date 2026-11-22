// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns how a comparison runs on a Type in MIR terms (Comparison; DECISIONS R5-M68; R5 package F 4.6, 4.7, S2-S4 F 2, F 5, X25), by
// Binding's one composition rule (Binding.ComparisonKindOf, shared with the legacy plan) with its closed leaves from Witness
// (TyCtxt.Dispatch.cs).
// Input: Equatable or Comparable, a Type and the mode (source operators, IEEE for floats, or the Contract's mapping, whose equals is
// NaN-reflexive).
// Output: a ComparisonNode tree: Composite over its parts, Builtin, Witness (a closed leaf, string's among them: Witness's Body at its
// instance without its Origins, a library witness's at the Self's arguments), Requirement (a leaf with an abstract part: the Contract's own requirement on the leaf in every
// instance, Synthesized on a Self) or None (no answer, a broken contract for a closed accepted Self that its reader records as
// InternalInvariant_Kd). One node per (contract, self, operators), discarded at an edit (ResetDispatch).
// Invariants: a comparison of a closed Type has the legacy plan's leaves wherever that plan exists, which it does not for an inherited
// witness (ComparisonCompositionTest's twin).
// OperandTerminal: a comparison operand's node and Type to the Type it compares, a read of Binding's decision in that node's scope
// (Binding.OperandTerminal: the scope's premises by CallableContractType, then each qualifying pair layer, then the ref and uniq layers),
// the one Binding view the MIR builder's Witnessed asks before Comparison; it resolves nothing.
// The operator table's comparison statements, pure functions of a Type and a Contract that Binding and MIR read: IsComposite (a Tuple
// or a ref layer composes), OperatorTableRuns (the table runs a Type's comparison under a Contract) and OperatorCompares (the comparison
// rows of the operator table's domain, MirTypes.Operates; DECISIONS R8-M20).
// Must not: create a bridge or a CallPlan, write a diagnostic, HIR or pass state, or prove on an erased value.

using Kimi.Compiler.Parsing;

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
    private readonly Dictionary<(BindingSymbol Contract, BoundType Self, bool Operators), ComparisonNode> comparisons = new();

    internal static bool IsComposite(BoundType? type)
        => type?.Kind == BoundTypeKind.Tuple || type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 };

    // SPEC 13.4: whether the operator table runs a Type's comparison under a Contract. Whether a library group serves a Type is the witness
    // table's question (Binding.WitnessGroup); conformance asks both.
    internal static bool OperatorTableRuns(BoundType? type, KimiDeclarationId? contract)
        => contract == KimiDeclarationId.Equatable ? type is { IsScalar: true } || ReferenceEquals(type, BoundType.Unit)
            : contract == KimiDeclarationId.Comparable && (type is { HasIntegerArithmetic: true } || ReferenceEquals(type, BoundType.Char));

    // SPEC 13.4, 13.4.1, 5.1, 3.1.3: whether the operator table runs a built-in comparison operator at a terminal Type, == and != under
    // Equatable, the ordering ones under Comparable: the Contract's built-in comparison (OperatorTableRuns), a floating-point Type's
    // order (IEEE; no float is Comparable), a raw pointer's equality (no pointer is Equatable) and every one at Never, which has no value,
    // so none runs (Binding admits each at a Never terminal). The comparison rows of the operator table's domain (MirTypes.Operates,
    // which MirValidator's operator rvalues and the Backend's Column read): every Type with a built-in operator has equality; Unit and
    // Never, zero-sized, compare by their row's result on equal operands. No other Type: a string's comparison is its library witness's
    // (R8-S1e-b), a Tuple's, a reference layer's and a user Type's a Contract call (Comparison). Binding's operator rules restate it
    // until the Front's staged removal (its PLAN row, from G662): ComparisonApplicability, CompleteComparison, and ComparisonKindOf's
    // and ComparisonProof's float order under operators.
    internal static bool OperatorCompares(BoundType? type, KimiDeclarationId contract)
        => OperatorTableRuns(type, contract) || type is { IsFloatingPoint: true } || ReferenceEquals(type, BoundType.Never) ||
            (contract == KimiDeclarationId.Equatable && type is { IsPointer: true });

    // SPEC 13.4, 13.4.1, 12.4.3: how a comparison of Self under Equatable or Comparable runs, as source operators or the Contract's mapping
    // compare it.
    internal ComparisonNode Comparison(BindingSymbol contract, BoundType self, bool operators)
    {
        if (!this.comparisons.TryGetValue((contract, self, operators), out var node))
        {
            node = this.Compose(contract, self, operators);
            this.comparisons.Add((contract, self, operators), node);
        }

        return node;
    }

    // SPEC 8.2.3, 13.4, 13.5.5: the Type a comparison operand compares, Binding's decision for the operand (Binding.OperandTerminal): its Type
    // as the premises of the operand's scope make it (CallableContractType), then below each pair layer that scope admits following, then
    // below its ref and uniq layers, which the MIR builder asks Comparison at.
    internal BoundType OperandTerminal(Koto operand, BoundType type) => binding.OperandTerminal(operand, type);

    // Binding's composition rule over the leaves: a closed one runs what the Contract's requirement on it runs (Witness's Body), one with an
    // abstract part that requirement on it in every instance. Any other target is None, the broken contract its reader records: Compose
    // composes a template or a Type a site formed within IMPL A.4's height, whose conformance Binding proved at that site, and a refusal of
    // a resolution is reported where the call forms (Witness's HeightLimit at its requesting site), so none reaches it (R7-T1 c4).
    private ComparisonNode Compose(BindingSymbol contract, BoundType self, bool operators)
    {
        var kind = Binding.ComparisonKindOf(self, contract.LibraryDeclaration, operators);
        if (kind == ComparisonKind.Composite)
        {
            var parts = new ComparisonNode[self.Components.Count];
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = this.Comparison(contract, self.Components[i], operators);
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
            { Kind: RequirementTargetKind.Body, Target: { } witness, Subst: { } instance }
                => new(self, kind, witness, this.Intern(witness, null, instance.Declaring, instance.Types, instance.Lengths, [], []), []),
            _ => new(self, ComparisonKind.None, null, null, []),
        };
    }
}
