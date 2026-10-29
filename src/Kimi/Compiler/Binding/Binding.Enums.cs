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

    /// <summary>Gets the construction of the enum Case value a node with the Aggregate Op makes, or null for another node.</summary>
    /// <param name="use">The node.</param>
    /// <returns>The construction the latest pass selected.</returns>
    internal static BoundEnumConstruction? Of(Koto use) => use.CodeContext.Compilation.Binding.TryGetEnumConstruction(use, out var construction) ? construction : null;

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
        if (use.BindingState == BindingState.Resolved && this.enumConstructions.TryGetValue(use, out var plan) && plan.IsValid)
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

    // SPEC 10.8, 10.5: the construction's slots that no evidence binds, other than an F that a waiting callable binds (`unsolved`); false
    // when every slot is bound.
    private static bool UnsolvedPayloadSlots(ReadOnlySpan<Koto> payload, in SlotSolution solution, ref SlotSet unsolved)
    {
        unsolved.Clear();
        for (var g = 0; g < CallSlotCount(solution.Binder); g++)
        {
            if (solution.Types[g] is null)
            {
                unsolved.Add(g);
            }
        }

        var unbound = !unsolved.IsEmpty;
        var waiting = new WaitingSlots(ref unsolved, solution.SlotOwner);
        for (var i = 0; i < payload.Length; i++)
        {
            solution.Binding.TakeWaiting(solution.Call.ArgumentNodes[i], payload[i].BoundType!, solution.SlotOwner, waiting);
        }

        return unbound;
    }

    private BoundType? EnumQualifierType(Koto qualifier, BindingSymbol symbol, BindingScope scope, BoundType? expected)
    {
        BoundType? type;
        if (qualifier is ParenthesizedTypeKoto or TypeSemanticsKoto { Type: not null })
        {
            type = this.BindType(qualifier, scope, this.TypeContext(qualifier, scope) with { SuppressOuter = true });
        }
        else if (qualifier is GenericsKoto || (qualifier is SyntaxFormKoto { Akind: KotoKind.RootName } root && root.Operands.Length == 1 && UnwrapTypeSyntax(root.Operands[0]) is GenericsKoto))
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
            type = symbol.Declaration is StructKoto || (symbol.Schema is { GenericSlots.Count: > 0 } or { Origins.Count: > 0 } && symbol.Declaration.Parent is DeclarationContainerKoto { IsRoot: false })
                ? this.BindContainerReference(qualifier, symbol, scope, this.TypeContext(qualifier, scope), [])
                : expected?.Semantics == SemanticsKind.Owner && ReferenceEquals(expected.Symbol, symbol) ? expected : symbol.Type;
        }

        qualifier.BoundSymbol = symbol;
        return Complete(qualifier, type);
    }

    private BindingSymbol? InferredCase(SyntaxFormKoto reference, BindingScope scope, BoundType? expected)
    {
        if (expected is null && this.MissingExpectationCause(reference) is { } cause)
        {
            this.CompleteDependent(reference, cause);
            return null;
        }

        if (expected is not { Semantics: SemanticsKind.Owner, Symbol.Declaration: EnumKoto } || reference.Operands.Length != 1 || reference.Operands[0] is not IdentifierNameKoto name)
        {
            this.Fail(reference, BindingFailure.MissingType, true);
            return null;
        }

        var member = this.LookupTypeMember(expected, name.IdentifierName, scope).Member;
        if (member?.EnumCase is null)
        {
            this.Fail(reference, BindingFailure.MissingName);
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
            return this.Fail(use, BindingFailure.TypeMismatch);
        }

        var owner = reference is MemberAccessKoto access && this.memberSelections.TryGetValue(access, out var selection) ? selection.DeclaringType : expected;
        var slots = declaration.BoundSymbol!.Schema!.GenericSlots.Count;
        var originCount = declaration.BoundSymbol.Schema.Origins.Count;
        var arguments = this.typeScratch.Rent(slots);
        var origins = this.originScratch.Rent(originCount);
        var operations = this.argumentOperationScratch.Rent(count);
        var lengths = this.lengthScratch.Rent(slots);
        var settled = this.RentSlotSet(count);
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
                    return this.Fail(use, BindingFailure.TypeMismatch);
                }

                for (var i = 0; i < slots; i++)
                {
                    if (i >= expected.Components.Count || (arguments[i] is { } fixedType && !ReferenceEquals(fixedType, expected.Components[i])))
                    {
                        return this.Fail(use, BindingFailure.TypeMismatch);
                    }

                    arguments[i] = expected.Components[i];
                }

                for (var i = 0; i < Math.Min(originCount, expected.OriginArguments.Count); i++)
                {
                    origins[i] = expected.OriginArguments[i];
                }
            }

            var solution = this.Solution(call, declaration, scope, arguments, lengths, origins);
            if (!this.SolveConstruction(use, payload, ref solution, expected, ref settled))
            {
                return null;
            }

            for (var i = 0; i < slots; i++)
            {
                if (arguments[i] is null)
                {
                    // SPEC 10.8, 23.1.2: the unbound slot is the construction's own record; its waiting payloads depend on it.
                    if (call is not null)
                    {
                        this.MarkWaitingHeaders(call);
                    }

                    return this.Fail(use, BindingFailure.MissingType, true);
                }
            }

            if (expected is null && solution.OriginInference is { } inference)
            {
                this.SolveConstructionOrigins(use, payload, solution, inference);
            }

            for (var i = 0; i < originCount; i++)
            {
                if (origins[i] is null)
                {
                    return this.Fail(use, BindingFailure.MissingOrigin, true);
                }
            }

            var result = this.types.Intern(slots == 0 ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, enumeration.Owner, SemanticsKind.Owner, ((BoundType[])(object)arguments).AsSpan(0, slots), originArguments: origins.AsSpan(0, originCount));
            if (expected is not null && !FitsType(result, expected))
            {
                // SPEC 15.6.1: a construction that fits only structurally fails in its Origin relations, never as a Type mismatch.
                if (!FitsStructurally(result, expected))
                {
                    return this.Fail(use, BindingFailure.TypeMismatch);
                }

                if (!this.FitsTypeAt(result, expected, use) && !this.CheckLocalTypeUse(result, expected, use))
                {
                    return this.RecordMismatch(use, use, result, expected);
                }
            }

            if (!this.CompletePayloads(use, payload, result, scope, operations))
            {
                return null;
            }

            var proof = this.CheckConstraints(declaration.ConstraintNodes, declaration, arguments.AsSpan(0, slots), scope);
            if (proof != ConstraintProof.Proven)
            {
                return this.Fail(use, proof == ConstraintProof.Error ? BindingFailure.InvalidConstraint : proof == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, proof == ConstraintProof.Unknown);
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
            this.lengthScratch.Return(lengths, clearArray: true);
            this.ReturnSlotSet(settled);
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(arguments, clearArray: true);
        }
    }

    // SPEC 15.3.6, 15.6.4 step 3: without an expected Type, a construction's Origins are fresh regions solved from their bounds: the
    // payloads' fits, the well-formedness of every payload Type beside the payload's own Type, as a call's (SolveLate), and the enum's
    // clauses (SolveOriginInference), so that a covariant slot takes the meet of all its upper bounds.
    private void SolveConstructionOrigins(Koto use, ReadOnlySpan<Koto> payload, in SlotSolution solution, OriginInference inference)
    {
        for (var i = 0; i < payload.Length; i++)
        {
            if (this.SubstituteType(payload[i].BoundType!, solution.SlotOwner, solution.Types, solution.Lengths) is { } payloadType)
            {
                this.CollectFilledPremises(payloadType, this.OwnType(solution.Call.ArgumentNodes[i]), inference, use);
            }
        }

        this.SolveOriginInference(inference, solution.Origins, [], use, select: true);
    }

    // SPEC 6.5.2 item 2, 10.8.1, 10.5.1: a Case construction's own slots after those its qualifier and expected Type fix. Every payload
    // is prepared as an argument is; one whose payload Type those fixed slots settle (`settled`) is no evidence, every other is evidence by
    // its kind (SlotSolution.InferArgument). Under the argument probe a construction with a slot that no evidence
    // binds waits; otherwise the Literal parts' defaults apply to the slots that neither evidence nor a waiting callable at an F binds
    // (SPEC 10.5, TakeWaiting), and are evidence once applied. Once every other slot is bound, the waiting callables at an F are
    // completed with S and are evidence (SPEC 10.5.4), as a call's are after selection. False when the construction is complete: failed
    // or waiting.
    private bool SolveConstruction(Koto use, ReadOnlySpan<Koto> payload, ref SlotSolution solution, BoundType? expected, ref SlotSet settled)
    {
        for (var i = 0; i < payload.Length; i++)
        {
            if (solution.Call.GetArgumentLabel(i) is not null || payload[i].BoundType is null)
            {
                this.Fail(use, BindingFailure.TypeMismatch);
                return false;
            }

            // SPEC 10.5.1: every payload is classified as an argument is, a settled one too (TakesCallType).
            if (!this.PrepareArgument(solution.Call.ArgumentNodes[i], solution.Scope, null, callableContext: true))
            {
                Complete(use, null); // The payload's own failure, on which the construction rests.
                return false;
            }

            if (this.SubstituteType(payload[i].BoundType!, solution.SlotOwner, solution.Types, solution.Lengths) is { } fixedType &&
                !HasUnsubstitutedOrigin(
                    this.SubstituteStoredOrigins(fixedType, solution.SlotOwner, solution.Origins.AsSpan(0, solution.OriginCount)), solution.SlotOwner))
            {
                settled.Add(i);
            }
        }

        var unsolved = this.RentSlotSet(CallSlotCount(solution.Binder));
        try
        {
            for (var late = 0; late < 2; late++)
            {
                if (!this.InferPayloads(use, payload, ref solution, settled, late == 1))
                {
                    return false;
                }

                if (!UnsolvedPayloadSlots(payload, solution, ref unsolved))
                {
                    return true;
                }

                // SPEC 10.5.1 (D4): under the argument probe a construction waits while its result, which holds all its slots, holds one
                // that neither evidence nor a waiting callable fixes, as a nested call does.
                if (late == 0 && this.HoldsOpenSlot(payload, solution, unsolved) && this.DeferNestedCall(use, expected))
                {
                    Complete(use, null);
                    return false;
                }

                if (unsolved.IsEmpty || late == 1)
                {
                    break;
                }

                if (solution.DefaultPayloads(payload, unsolved) is { } misfit)
                {
                    this.CompleteDependent(use, misfit); // SPEC 12.3: the literals' own records, on which the construction rests.
                    return false;
                }
            }

            for (var i = 0; i < payload.Length && unsolved.IsEmpty; i++)
            {
                if (!this.TakeWaiting(solution.Call.ArgumentNodes[i], payload[i].BoundType!, solution.SlotOwner, solution))
                {
                    Complete(use, null); // The callable's own failure, on which the construction rests.
                    return false;
                }
            }

            return !unsolved.IsEmpty || this.InferPayloads(use, payload, ref solution, settled, false);
        }
        finally
        {
            this.ReturnSlotSet(unsolved);
        }
    }

    // SPEC 10.5.1: whether the construction holds a slot that no evidence binds (`unsolved`) or an F that its waiting callable does not
    // fix with no outer expectation (OpenWaitingSlots).
    private bool HoldsOpenSlot(ReadOnlySpan<Koto> payload, in SlotSolution solution, SlotSet unsolved)
    {
        if (!unsolved.IsEmpty)
        {
            return true;
        }

        var open = this.RentSlotSet(CallSlotCount(solution.Binder));
        try
        {
            var taker = new OpenWaitingSlots(ref open, solution);
            for (var i = 0; i < payload.Length; i++)
            {
                this.TakeWaiting(solution.Call.ArgumentNodes[i], payload[i].BoundType!, solution.SlotOwner, taker);
            }

            return !open.IsEmpty;
        }
        finally
        {
            this.ReturnSlotSet(open);
        }
    }

    // SPEC 10.8: each payload but a settled one is evidence by its kind, in the early or the late pass; a payload that refutes the slots
    // is the construction's Type mismatch.
    private bool InferPayloads(Koto use, ReadOnlySpan<Koto> payload, ref SlotSolution solution, SlotSet settled, bool late)
    {
        for (var i = 0; i < payload.Length; i++)
        {
            if (!settled.Contains(i) && !solution.InferArgument(payload[i].BoundType!, solution.Call.ArgumentNodes[i], late))
            {
                this.Fail(use, BindingFailure.TypeMismatch);
                return false;
            }
        }

        return true;
    }

    // SPEC 6.5.2, 10.5.1, 10.2: each payload in source order fits its decided payload Type as an argument does, is completed at it
    // (ErasePart for a callable) and adapted to it. The construction judges the fit once: a misfit is its
    // Type mismatch, one only in Origin relations fails at the payload (SPEC 15.6.1), and a bare Non-Copy Place names its required
    // spelling (SPEC 6.5.2); a payload's own failure while bound is its record, on which the construction rests.
    private bool CompletePayloads(
        Koto use,
        ReadOnlySpan<Koto> payload,
        BoundType result,
        BindingScope scope,
        BoundArgumentOperation[] operations)
    {
        for (var i = 0; i < payload.Length; i++)
        {
            var source = ((InvocationKoto)use).ArgumentNodes[i];
            var stored = this.PartTypeIn(payload[i], result, scope)!;
            var type = this.ReplaceHolder(stored, null); // SPEC 5.7: a Case holding `during self` is constructed through no dereference.
            var fitted = default(BoundArgumentOperation);
            var fit = this.FitArgument(source, type, scope, source, out fitted);
            if (fit is CandidateApplicability.Inapplicable or CandidateApplicability.Pending)
            {
                this.Fail(use, BindingFailure.TypeMismatch);
                return false;
            }

            this.acquisitionFailure = null;
            if (fit == CandidateApplicability.Error || (this.PropagateBound(source, type) ?? this.BindNode(source, scope, type)) is not { } completed ||
                (fitted.Adaptation == ArgumentAdaptation.Erasure && completed is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } &&
                !this.ErasePart(source, completed, type)))
            {
                Complete(use, null); // The payload's own failure, on which the construction rests.
                return false;
            }

            if (HoldsHolder(stored) && this.HolderWriteFailure(stored, HolderAccess.Write, source, source))
            {
                Complete(use, null);
                return false;
            }

            var actual = this.ArgumentType(source, source.ErasedFunctionType ?? completed);
            // A payload's Borrow of a static root is the located limit, its relations waiting for P42 (FailStaticBorrow). D-1: the fit of a
            // payload at a position whose Type its call determines is that call's, at the argument (OpenedAt), also when it waits.
            var judged = this.OpenedAt(source);
            if (!this.AdaptInput(source, type, actual, scope, null, null, out var adapted, out var quality, out var kind) ||
                (!this.FailStaticBorrow(source, kind, type.Semantics) && !this.FitsTypeAt(adapted, type, judged) &&
                !this.CheckLocalTypeUse(adapted, type, judged)))
            {
                if (this.acquisitionFailure is { } acquisition)
                {
                    this.FailAcquisition(use, acquisition.Kind, source, acquisition.Object);
                }
                else if (FitsStructurally(adapted, type))
                {
                    if (ReferenceEquals(judged, source) || !this.RecordOriginFitFailure(judged, judged, adapted, type))
                    {
                        this.RecordMismatch(source, source, adapted, type);
                    }
                    else
                    {
                        this.RelatePayload(judged, source); // D-1: the payload that supplies the call's relation.
                    }

                    Complete(use, null);
                }
                else
                {
                    this.Fail(use, BindingFailure.TypeMismatch);
                }

                return false;
            }

            // SPEC 15.6.1: a payload that takes its Type from the construction has no well-formed Type of its own; that inferred Type is
            // judged at the payload, or at the call argument that supplies it (JudgedAt).
            this.RequireInputPremises(source, type, this.TakesCallType(source) ? null : adapted);
            operations[i] = new(source, actual, type, kind, quality, ParameterIndex: i);
        }

        return true;
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

            this.CompleteEnumOrigins(entry.Key, plan);

            if (plan.Case.Symbol.Declaration.BindingState != BindingState.Resolved ||
                plan.Case.Owner.Declaration.BindingState != BindingState.Resolved || this.InvalidDeclarationContext(plan.Case.Owner.Declaration))
            {
                plan.IsValid = false;
                this.Fail(entry.Key, BindingFailure.InvalidTypeFormation);
                continue;
            }

            var scope = this.ConstraintScope(entry.Key);
            var formation = this.CheckTypeConstraints(plan.Type, scope);
            for (var i = 0; i < plan.PayloadOperations.Length; i++)
            {
                formation = CombineProof(formation, this.CheckTypeConstraints(plan.PayloadOperations[i].ParameterType!, scope), true);
            }

            if (formation != ConstraintProof.Proven)
            {
                plan.IsValid = false;
                this.Fail(entry.Key, formation == ConstraintProof.Error ? BindingFailure.InvalidConstraint : formation == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, formation == ConstraintProof.Unknown);
                continue;
            }

            for (var i = 0; i < plan.PayloadOperations.Length; i++)
            {
                var operation = plan.PayloadOperations[i];
                var proof = this.ProveCopy(operation.ParameterType!, entry.Key);
                if (proof == ConstraintProof.Error)
                {
                    plan.IsValid = false;
                    this.Fail(entry.Key, BindingFailure.InvalidConstraint);
                }
                else if (operation.Kind == ArgumentOperationKind.Value && operation.Source is { } payloadSource && IsBarePlace(payloadSource) &&
                    this.BareAcquire(operation.SourceType!, payloadSource, out _) == HirAcquire.Move)
                {
                    // SPEC 6.5.2, 3.5, 8.14: a payload Place is acquired by its bare acquisition, by case for a pair layer; one without a
                    // plan never Moves into a payload; write value@move.
                    plan.IsValid = false;
                    this.FailAcquisition(payloadSource, BindingFailure.TransferRequired, payloadSource);
                }

                plan.SetAcquisition(i, operation.Kind is ArgumentOperationKind.CopyRead or ArgumentOperationKind.ReferenceRead ? AcquisitionKind.Copy : operation.Kind != ArgumentOperationKind.Value ? AcquisitionKind.None : proof == ConstraintProof.Proven ? AcquisitionKind.Copy : proof == ConstraintProof.Refuted ? AcquisitionKind.Move : AcquisitionKind.CopyOrMove);
            }
        }
    }

    // The enclosing local has now solved all acquisition bounds. Complete this plan against those solutions without
    // changing the source expressions' own call/adaptation certificates or their acquired source Types.
    private void CompleteEnumOrigins(Koto use, BoundEnumConstruction construction)
    {
        for (var node = use.Parent; node is not null; node = node.Parent)
        {
            if (this.initializerOrigins.TryGetValue(node, out var initializer))
            {
                Apply(initializer);
            }

            if (node is VariableKoto && this.originDeclarations.TryGetValue(node, out var local))
            {
                Apply(local);
            }
        }

        void Apply(OriginDeclaration declaration)
        {
            if (declaration.State < 2 || declaration.Replacements.Count == 0)
            {
                return;
            }

            var source = construction.PayloadOperations;
            var operations = this.argumentOperationScratch.Rent(source.Length);
            try
            {
                for (var i = 0; i < source.Length; i++)
                {
                    operations[i] = source[i] with { ParameterType = this.RewriteOrigins(source[i].ParameterType!, declaration) };
                }

                var type = this.RewriteOrigins(construction.Type, declaration);
                construction.Set(construction.Case, type, operations.AsSpan(0, source.Length));
                use.BoundType = type;
            }
            finally
            {
                this.argumentOperationScratch.Return(operations, clearArray: true);
            }
        }
    }
}
