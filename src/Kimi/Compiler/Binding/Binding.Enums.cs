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

    public BoundEnumCase Case { get; private set; } = null!;

    public BoundType Type { get; private set; } = null!;

    public ReadOnlySpan<BoundArgumentOperation> PayloadOperations => this.operations;

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
        }

        operations.CopyTo(this.operations);
        this.IsValid = true;
    }
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
        var adapted = this.typeScratch.Rent(count);
        var rows = this.rowScratch.Rent(count);
        var childSlice = this.indexScratch.Rent(2);
        var childBase = this.childRows.Count;
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
            solution.Rows = rows.AsSpan(0, count);
            solution.ChildSlice = ResetSlice(childSlice);
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

            // SPEC 15.6.4 steps 1, 3, 15.3.6 (F1, DECISIONS R4-M164 U4): a construction is a call. The Origins that its expected Type leaves
            // open are solved by the call's solve, which its clauses and each Signature payload's premises bound (SolveLate).
            solution.ContributePayloads(payload);
            this.RetainChildRows(ref solution); // R3: the payloads' composition, before their completion.
            if (expected is null || ReferenceEquals(expected, BoundType.Never))
            {
                this.SolveConstructionOrigins(ref solution);
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
                // SPEC 15.6.1: a construction that fits only structurally fails in its Origin relations, never as a Type mismatch; a Function
                // component's failed contract is the construction's contract record, whose fact its walk gives (RecordMismatch).
                if (!FitsStructurally(result, expected))
                {
                    return !this.FitsTypeAt(result, expected, use, out var contract) && contract.IsCondition
                        ? this.RecordMismatch(use, use, result, expected, contract) : this.Fail(use, BindingFailure.TypeMismatch);
                }

                if (!this.FitsTypeAt(result, expected, use, out var failure) && !this.CheckLocalTypeUse(result, expected, use))
                {
                    return this.RecordMismatch(use, use, result, expected, failure);
                }
            }

            if (!this.CompletePayloads(use, payload, result, scope, operations, adapted, settled))
            {
                return null;
            }

            // SPEC 15.6.4 step 3: the call's walk over the instantiated inputs, each payload by its rows (JudgePositionRows) where it has
            // child rows, else by its contribution (RequireInputPremises; R4, DECISIONS BU-104).
            this.callRelationScratch.Clear();
            for (var i = 0; i < count; i++)
            {
                this.ChildGroup(solution, i, out var childStart, out var childEnd);
                if (childStart != childEnd)
                {
                    this.JudgePositionRows(operations[i].Source!, rows[i] with { Type = operations[i].ParameterType }, solution);
                }
                else
                {
                    this.RequireInputPremises(operations[i].Source!, payload[i].BoundType!, operations[i].ParameterType!, adapted[i], rows[i].Contribution);
                }
            }

            this.PublishCallRelations();

            var proof = this.CheckConstraints(declaration.ConstraintNodes, declaration, arguments.AsSpan(0, slots), scope);
            if (proof != ConstraintProof.Proven)
            {
                return proof == ConstraintProof.Unknown ? this.FailUnprovenConstraint(use, this.TakeUnknownFact(), unresolved: true)
                    : this.Fail(use, proof == ConstraintProof.Error ? BindingFailure.InvalidConstraint : BindingFailure.UnsatisfiedConstraint);
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
            this.rowScratch.Return(rows, clearArray: true);
            this.indexScratch.Return(childSlice);
            this.childRows.RemoveRange(childBase, this.childRows.Count - childBase);
            this.typeScratch.Return(adapted, clearArray: true);
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.lengthScratch.Return(lengths, clearArray: true);
            this.ReturnSlotSet(settled);
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(arguments, clearArray: true);
        }
    }

    // SPEC 6.5.2 item 2, 10.8.1, 10.5.1: a Case construction's own slots after those its qualifier and expected Type fix. A payload whose
    // payload Type those fixed slots settle (`settled`) is bound only at it, on completion; every other payload is prepared as an argument
    // is and is evidence by its kind (SlotSolution.InferArgument). Under the argument probe a construction with a slot that no evidence
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

            if (this.SubstituteType(payload[i].BoundType!, solution.SlotOwner, solution.Types, solution.Lengths) is { } fixedType &&
                !this.LacksImage(fixedType, solution.SlotOwner, solution.Origins.AsSpan(0, solution.OriginCount)))
            {
                settled.Add(i);
                _ = this.RecordsWaitingForm(solution.Call.ArgumentNodes[i]); // Recorded as a named call's argument is (P1), unbound.
            }
            else if (!this.PrepareArgument(solution.Call.ArgumentNodes[i], solution.Scope, null, callableContext: true))
            {
                Complete(use, null); // The payload's own failure, on which the construction rests.
                return false;
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

    // SPEC 15.6.4 steps 1, 3, 15.3.6 (Rule C): the call's solve of the construction's Origins (SolveOriginInference), which the enum's
    // clauses and each Signature leaf row's premise over its payload's instantiated sub-Type bound (CollectPositionPremises), as
    // SolveLate's do a candidate's. Evidence that carries an Origin began the inference (InferAdapted); without it or such a row nothing
    // bounds them.
    private void SolveConstructionOrigins(ref SlotSolution solution)
    {
        this.CollectPositionPremises(ref solution);
        this.ReturnSlotSet(solution.EvidenceNamed); // Read by the discoveries above only.
        if (solution.OriginInference is not { } inference)
        {
            return;
        }

        // A selection's solve never fails (SolveOriginInference with `select`); the payload walk and the Type-formation check judge it.
        _ = this.SolveOriginInference(inference, solution.Origins, solution.Inputs, solution.Call, select: true);
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

    // SPEC 6.5.2, 10.5.1, 10.2: each payload in source order fits its decided payload Type as an argument does (a settled one is bound at
    // it instead), is completed at it (ErasePart for a callable) and adapted to it. The construction judges the fit once: a misfit is its
    // Type mismatch, one only in Origin relations fails at the payload (SPEC 15.6.1), and a bare Place without a plan names its required
    // spelling (SPEC 6.5.2) beside the fit's record (SPEC 23.1.2); a payload's own failure while bound (PartFailed, as at a call
    // argument; an erasure's Owned failure is its value's, DECISIONS BU-107) is its record, on which the construction rests.
    private bool CompletePayloads(
        Koto use,
        ReadOnlySpan<Koto> payload,
        BoundType result,
        BindingScope scope,
        BoundArgumentOperation[] operations,
        BoundType?[] adapted,
        SlotSet settled)
    {
        for (var i = 0; i < payload.Length; i++)
        {
            var source = ((InvocationKoto)use).ArgumentNodes[i];
            var stored = this.PartTypeIn(payload[i], result, scope)!;
            var type = this.ReplaceHolder(stored, null); // SPEC 5.7: a Case holding `during self` is constructed through no dereference.
            var fitted = default(BoundArgumentOperation);
            var fit = settled.Contains(i) ? CandidateApplicability.Applicable : this.FitArgument(source, type, scope, source, out fitted);
            if (fit is CandidateApplicability.Inapplicable or CandidateApplicability.Pending)
            {
                this.Fail(use, BindingFailure.TypeMismatch);
                return false;
            }

            this.acquisitionFailure = null;
            if (fit == CandidateApplicability.Error || (this.PropagateBound(source, type) ?? this.BindNode(source, scope, type)) is not { } completed ||
                this.PartFailed(source) ||
                (fitted.Adaptation == ArgumentAdaptation.Erasure && completed is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } &&
                !this.ErasePart(source, completed, type)))
            {
                Complete(use, null); // The payload's own failure, on which the construction rests.
                return false;
            }

            if (this.HoldsHolder(stored) && this.HolderWriteFailure(stored, HolderAccess.Write, source, source))
            {
                Complete(use, null);
                return false;
            }

            var actual = this.ArgumentType(source, completed); // An erased payload adapts as its erasure (AdaptInput, rule E; BU-83).
            var failure = default(ComparisonFailure);

            // The acquisition and the fit are independent (PLAN G262): a failed acquisition is deferred, the fit judged on the Type it
            // supplies, and the failure explains the payload where its structure fits, as a candidate's does (TryCandidate). A payload's
            // Borrow of a static root is the located limit, its relations waiting for P42 (FailStaticBorrow).
            var fits = this.AdaptInput(source, type, actual, scope, null, null, out var adaptedType, out var quality, out var kind, deferAcquisition: true) &&
                (this.FailStaticBorrow(source, kind, type.Semantics) || this.FitsTypeAt(adaptedType, type, source, out failure) ||
                this.CheckLocalTypeUse(adaptedType, type, source));
            var acquisition = this.acquisitionFailure is { } deferred && (fits || this.FitsStructuralPartAt(adaptedType, type, source))
                ? deferred : default(AcquisitionFailure?);
            if (acquisition is { } failed)
            {
                this.FailAcquisition(use, failed.Kind, source, failed.Object);
            }

            if (!fits && (failure.IsCondition || this.FitsStructurallyAt(adaptedType, type, source)))
            {
                this.RecordMismatch(source, source, adaptedType, type, failure);
                Complete(use, null);
            }
            else if (!fits)
            {
                this.Fail(use, BindingFailure.TypeMismatch);
            }

            if (!fits || acquisition is not null)
            {
                return false;
            }

            operations[i] = new(source, actual, type, kind, quality, ParameterIndex: i);
            adapted[i] = adaptedType;
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
                    this.FailAcquisition(entry.Key, BindingFailure.TransferRequired, payloadSource);
                }
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
