// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared enum declaration and construction vocabulary.

/// <summary>A stable Case identity. Payload syntax is shared with storage/capability validation.</summary>
public sealed class BoundEnumCase
{
    internal BoundEnumCase(BindingSymbol symbol, BindingSymbol owner)
    {
        this.Symbol = symbol;
        this.Owner = owner;
    }

    public BindingSymbol Symbol { get; }

    public BindingSymbol Owner { get; internal set; }

    /// <summary>Gets declaration order, not a numeric discriminant or physical layout.</summary>
    public int Ordinal { get; internal set; }

    public ReadOnlySpan<Koto> Payload => ((SyntaxFormKoto)((SyntaxFormKoto)this.Symbol.Declaration).Operands[1]).Operands;
}

/// <summary>A committed construction in payload evaluation order. It is not an ownership certificate.</summary>
public sealed class BoundEnumConstruction
{
    private BoundArgumentOperation[] operations = [];
    private AcquisitionKind[] acquisitions = [];

    public BoundEnumCase Case { get; private set; } = null!;

    public BoundType Type { get; private set; } = null!;

    public ReadOnlySpan<BoundArgumentOperation> PayloadOperations => this.operations;

    public ReadOnlySpan<AcquisitionKind> Acquisitions => this.acquisitions;

    internal bool IsValid { get; set; }

    internal void Set(BoundEnumCase enumeration, BoundType type, ReadOnlySpan<BoundArgumentOperation> operations)
    {
        this.Case = enumeration;
        this.Type = type;
        if (this.operations.Length != operations.Length)
        {
            this.operations = new BoundArgumentOperation[operations.Length];
            this.acquisitions = new AcquisitionKind[operations.Length];
        }

        operations.CopyTo(this.operations);
        this.IsValid = true;
    }

    internal void SetAcquisition(int index, AcquisitionKind acquisition) => this.acquisitions[index] = acquisition;
}

public sealed partial class Binding
{
    private readonly Dictionary<Koto, BoundEnumConstruction> enumConstructions = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets this pass's construction plan; failed/replaced uses never expose old plans.</summary>
    /// <param name="use">The construction expression.</param>
    /// <param name="construction">The committed plan, or null if unavailable.</param>
    /// <returns>Whether a valid construction was selected in the latest pass.</returns>
    public bool TryGetEnumConstruction(Koto use, out BoundEnumConstruction? construction)
    {
        if (this.coreValid && use.BindingState == BindingState.Resolved && this.enumConstructions.TryGetValue(use, out var plan) && plan.IsValid)
        {
            construction = plan;
            return true;
        }

        construction = null;
        return false;
    }

    private static bool NeedsEnumContext(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        return node is SyntaxFormKoto { Akind: KotoKind.InferredCase } || node is InvocationKoto { Method: SyntaxFormKoto { Akind: KotoKind.InferredCase } };
    }

    private static bool EnumArityMatches(int payloadCount, InvocationKoto? call)
        => payloadCount == (call?.ArgumentNodes.Count ?? 0) && (call is null) == (payloadCount == 0);

    private bool PrepareContextualEnumInputs(Koto node, BindingScope scope)
    {
        node = KotoHelper.UnwrapParentheses(node);
        var valid = true;
        if (node is InvocationKoto call)
        {
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var input = call.ArgumentNodes[i];
                valid &= NeedsEnumContext(input) ? this.PrepareContextualEnumInputs(input, scope) : IsUnfittedLiteral(input) || this.BindNode(input, scope) is not null;
            }
        }

        return valid;
    }

    // Candidate probing reads syntax and bound inputs only. A losing candidate cannot bind
    // a leading-dot expression to its enum or publish a construction/Loan operation.
    private CandidateApplicability ProbeContextualEnum(Koto node, BoundType expected, BindingScope scope)
    {
        node = KotoHelper.UnwrapParentheses(node);
        var call = node as InvocationKoto;
        var reference = (SyntaxFormKoto)(call?.Method ?? node);
        if (expected is not { Semantics: SemanticsKind.Owner, Symbol.Declaration: EnumKoto declaration } || reference.Operands.Length != 1 || reference.Operands[0] is not IdentifierNameKoto name)
        {
            return expected.Kind == BoundTypeKind.Parameter ? CandidateApplicability.Pending : CandidateApplicability.Inapplicable;
        }

        var member = this.LookupTypeMember(expected, name.IdentifierName, scope).Member;
        if (member?.EnumCase is not { } enumeration)
        {
            return CandidateApplicability.Inapplicable;
        }

        if (member.Declaration.BindingState == BindingState.Invalid || declaration.BindingState == BindingState.Invalid)
        {
            return CandidateApplicability.Error;
        }

        if (!EnumArityMatches(enumeration.Payload.Length, call))
        {
            return CandidateApplicability.Inapplicable;
        }

        var proof = this.CheckConstraints(declaration.ConstraintNodes, declaration, (BoundType[])expected.Components, scope);
        var result = proof switch
        {
            ConstraintProof.Proven => CandidateApplicability.Applicable,
            ConstraintProof.Error => CandidateApplicability.Error,
            ConstraintProof.Refuted => CandidateApplicability.Inapplicable,
            _ => CandidateApplicability.Pending,
        };
        for (var i = 0; i < enumeration.Payload.Length; i++)
        {
            var source = call!.ArgumentNodes[i];
            var pattern = enumeration.Payload[i].BoundType;
            if (call.GetArgumentLabel(i) is not null)
            {
                return result == CandidateApplicability.Error ? result : CandidateApplicability.Inapplicable;
            }

            if (pattern is null || this.StoredType(pattern, expected) is not { } type)
            {
                return CandidateApplicability.Error;
            }

            CandidateApplicability input;
            if (NeedsEnumContext(source))
            {
                input = this.ProbeContextualEnum(source, type, scope);
            }
            else if (IsUnfittedLiteral(source))
            {
                input = this.FitsInputLiteral(source, type) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
            }
            else
            {
                input = source.BoundType is not { } actual ? CandidateApplicability.Pending :
                    this.AdaptInput(source, type, actual, scope, null, null, out var adapted, out _, out _) && FitsType(adapted, type) ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
            }

            // Error remains visible even if another payload cannot fit this candidate.
            result = result == CandidateApplicability.Error || input == CandidateApplicability.Error ? CandidateApplicability.Error :
                result == CandidateApplicability.Inapplicable || input == CandidateApplicability.Inapplicable ? CandidateApplicability.Inapplicable :
                result == CandidateApplicability.Pending || input == CandidateApplicability.Pending ? CandidateApplicability.Pending : CandidateApplicability.Applicable;
        }

        return result;
    }

    private BoundType? EnumQualifierType(Koto qualifier, BindingSymbol symbol, BindingScope scope, BoundType? expected)
    {
        BoundType? type;
        if (qualifier is GenericsKoto || (qualifier is SyntaxFormKoto { Akind: KotoKind.RootName } root && root.Operands.Length == 1 && UnwrapTypeSyntax(root.Operands[0]) is GenericsKoto))
        {
            // Only nested complete Type arguments carry annotations here. Enum Origin slots
            // are determined by the construction's expected Type and payloads.
            type = this.BindTypeStructure(qualifier, scope, this.TypeContext(qualifier, scope));
        }
        else if (TypeSpelling(qualifier) == "Self")
        {
            type = this.SelfType(symbol);
        }
        else
        {
            type = expected?.Semantics == SemanticsKind.Owner && ReferenceEquals(expected.Symbol, symbol) ? expected : symbol.Type;
        }

        qualifier.BoundSymbol = symbol;
        return Complete(qualifier, type);
    }

    private BindingSymbol? InferredCase(SyntaxFormKoto reference, BindingScope scope, BoundType? expected)
    {
        if (expected is not { Semantics: SemanticsKind.Owner, Symbol.Declaration: EnumKoto } || reference.Operands.Length != 1 || reference.Operands[0] is not IdentifierNameKoto name)
        {
            Fail(reference, BindingFailure.MissingType, true);
            return null;
        }

        var member = this.LookupTypeMember(expected, name.IdentifierName, scope).Member;
        if (member?.EnumCase is null)
        {
            Fail(reference, BindingFailure.MissingName);
            return null;
        }

        reference.BoundSymbol = name.BoundSymbol = member;
        Complete(name, BoundType.Unit);
        return member;
    }

    private BoundType? BindEnumConstruction(Koto use, Koto reference, BindingSymbol symbol, InvocationKoto? call, BindingScope scope, BoundType? expected)
    {
        var enumeration = symbol.EnumCase!;
        var declaration = (EnumKoto)enumeration.Owner.Declaration;
        var payload = enumeration.Payload;
        var count = call?.ArgumentNodes.Count ?? 0;
        if (!EnumArityMatches(payload.Length, call) || symbol.Declaration.BindingState == BindingState.Invalid || declaration.BindingState == BindingState.Invalid ||
            (reference is MemberAccessKoto member && member.Left.BoundSymbol?.Kind != BindingSymbolKind.Type))
        {
            return Fail(use, BindingFailure.TypeMismatch);
        }

        var owner = reference is MemberAccessKoto access && this.memberSelections.TryGetValue(access, out var selection) ? selection.DeclaringType : expected;
        var slots = declaration.GenericParameterNodes.Count;
        var originCount = declaration.OriginNames.Count;
        var arguments = this.typeScratch.Rent(slots);
        var origins = this.originScratch.Rent(originCount);
        var operations = this.argumentOperationScratch.Rent(count);
        Array.Clear(arguments, 0, slots);
        Array.Clear(origins, 0, originCount);
        try
        {
            if (owner is not null)
            {
                for (var i = 0; i < Math.Min(slots, owner.Components.Count); i++)
                {
                    arguments[i] = owner.Components[i];
                }
            }

            if (expected is not null && !ReferenceEquals(expected, BoundType.Never))
            {
                if (expected.Semantics != SemanticsKind.Owner || !ReferenceEquals(expected.Symbol, enumeration.Owner))
                {
                    return Fail(use, BindingFailure.TypeMismatch);
                }

                for (var i = 0; i < slots; i++)
                {
                    if (i >= expected.Components.Count || (arguments[i] is { } fixedType && !ReferenceEquals(fixedType, expected.Components[i])))
                    {
                        return Fail(use, BindingFailure.TypeMismatch);
                    }

                    arguments[i] = expected.Components[i];
                }

                for (var i = 0; i < Math.Min(originCount, expected.OriginArguments.Count); i++)
                {
                    origins[i] = expected.OriginArguments[i];
                }
            }

            // Infer from typed inputs before fitting literals or expected-Type Case syntax.
            // This is static analysis order; the committed operations remain in source order.
            for (var pass = 0; pass < 3; pass++)
            {
                for (var i = 0; i < count; i++)
                {
                    var source = call!.ArgumentNodes[i];
                    if ((NeedsEnumContext(source) ? 2 : IsUnfittedLiteral(source) ? 1 : 0) != pass)
                    {
                        continue;
                    }

                    if (call.GetArgumentLabel(i) is not null || payload[i].BoundType is not { } pattern)
                    {
                        return Fail(use, BindingFailure.TypeMismatch);
                    }

                    var hint = this.SubstituteType(pattern, declaration, arguments.AsSpan(0, slots));
                    if (hint is not null)
                    {
                        hint = this.SubstituteStoredOrigins(hint, declaration, origins.AsSpan(0, originCount));
                        if (HasUnsubstitutedOrigin(hint, declaration))
                        {
                            hint = null;
                        }
                    }

                    var actual = this.BindNode(source, scope, hint);
                    if (actual is null)
                    {
                        return Complete(use, null);
                    }

                    if (!this.AdaptInput(source, hint ?? pattern, actual, scope, null, null, out var adapted, out var quality, out var kind))
                    {
                        return Fail(use, BindingFailure.TypeMismatch);
                    }

                    this.MatchInputOrigins(pattern, adapted, declaration, origins, []);
                    var inferred = this.SubstituteStoredOrigins(pattern, declaration, origins.AsSpan(0, originCount));
                    if (!this.Infer(inferred, adapted, declaration, arguments, true))
                    {
                        return Fail(use, BindingFailure.TypeMismatch);
                    }

                    operations[i] = new(source, actual, null, kind, quality, ParameterIndex: i);
                }
            }

            for (var i = 0; i < slots; i++)
            {
                if (arguments[i] is null)
                {
                    return Fail(use, BindingFailure.MissingType, true);
                }
            }

            for (var i = 0; i < originCount; i++)
            {
                if (origins[i] is null)
                {
                    return Fail(use, BindingFailure.MissingOrigin, true);
                }
            }

            var result = this.InternType(slots == 0 ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, enumeration.Owner, SemanticsKind.Owner, ((BoundType[])(object)arguments).AsSpan(0, slots), originArguments: origins.AsSpan(0, originCount));
            if (expected is not null && !FitsType(result, expected))
            {
                return Fail(use, BindingFailure.TypeMismatch);
            }

            for (var i = 0; i < count; i++)
            {
                var type = this.StoredType(payload[i].BoundType!, result)!;
                var operation = operations[i];
                if (!this.AdaptInput(operation.Source!, type, operation.SourceType!, scope, null, null, out var adapted, out var quality, out var kind) || !FitsType(adapted, type))
                {
                    return Fail(use, BindingFailure.TypeMismatch);
                }

                operations[i] = operation with { ParameterType = type, Kind = kind, Adaptation = quality };
            }

            var proof = this.CheckConstraints(declaration.ConstraintNodes, declaration, arguments.AsSpan(0, slots), scope);
            if (proof != ConstraintProof.Proven)
            {
                return Fail(use, proof == ConstraintProof.Error ? BindingFailure.InvalidConstraint : proof == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, proof == ConstraintProof.Unknown);
            }

            if (!this.enumConstructions.TryGetValue(use, out var plan))
            {
                this.enumConstructions.Add(use, plan = new());
            }

            plan.Set(enumeration, result, operations.AsSpan(0, count));
            if (call is not null)
            {
                call.CallStorage = null;
            }

            use.BoundSymbol = reference.BoundSymbol = symbol;
            Complete(reference, result);
            return Complete(use, result);
        }
        finally
        {
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(arguments, clearArray: true);
        }
    }

    private void CompleteEnumAcquisitions()
    {
        foreach (var entry in this.enumConstructions)
        {
            var plan = entry.Value;
            if (!plan.IsValid)
            {
                continue;
            }

            if (plan.Case.Symbol.Declaration.BindingState != BindingState.Resolved || plan.Case.Owner.Declaration.BindingState != BindingState.Resolved)
            {
                plan.IsValid = false;
                Fail(entry.Key, BindingFailure.InvalidTypeFormation);
                continue;
            }

            for (var i = 0; i < plan.PayloadOperations.Length; i++)
            {
                var operation = plan.PayloadOperations[i];
                var proof = this.ProveCopy(operation.ParameterType!, entry.Key);
                if (proof == ConstraintProof.Error)
                {
                    plan.IsValid = false;
                    Fail(entry.Key, BindingFailure.InvalidConstraint);
                }

                plan.SetAcquisition(i, operation.Kind != ArgumentOperationKind.Value ? AcquisitionKind.None : proof == ConstraintProof.Proven ? AcquisitionKind.Copy : proof == ConstraintProof.Refuted ? AcquisitionKind.Move : AcquisitionKind.CopyOrMove);
            }
        }
    }
}
