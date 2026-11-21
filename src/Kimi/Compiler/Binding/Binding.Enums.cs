// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
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

/// <summary>A committed construction in payload evaluation order. It is not an ownership certificate. A resting construction's plan (a
/// payload failed, PLAN G418 (2)) holds only its completed payloads and is judged only by the pass-end formation and acquisitions
/// (CompleteEnumAcquisitions); no consumer reads it, since <see cref="Binding.TryGetEnumConstruction"/> returns a resolved use's plan.</summary>
public sealed class BoundEnumConstruction
{
    private BoundArgumentOperation[] operations = [];

    public BoundEnumCase Case { get; private set; } = null!;

    public BoundType Type { get; private set; } = null!;

    public ReadOnlySpan<BoundArgumentOperation> PayloadOperations => this.operations;

    internal bool IsValid { get; set; }

    /// <summary>Gets the construction of the enum Case value a node with the Aggregate Op makes, or null for another node.</summary>
    /// <param name="binding">The reader's Binding.</param>
    /// <param name="use">The node.</param>
    /// <returns>The construction the latest pass selected.</returns>
    internal static BoundEnumConstruction? Of(Binding binding, Koto use) => binding.TryGetEnumConstruction(use, out var construction) ? construction : null;

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
    private readonly EnumConstructionRows enumConstructions;

    /// <summary>Gets this pass's construction plan; failed, resting and replaced uses never expose a plan (the Resolved gate).</summary>
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

    // SPEC 6.5.2 steps 2-3, 9.7: whether a construction qualifier omits its own Type-argument list (no list, no parentheses, no Semantics,
    // not `Self`), so the expected owned Type of the same enum supplies it.
    private static bool OmitsOwnList(Koto qualifier)
        => qualifier is not (ParenthesizedTypeKoto or TypeSemanticsKoto { Type: not null } or GenericsKoto) &&
            !(qualifier is SyntaxFormKoto { Akind: KotoKind.RootName } root && root.Operands.Length == 1 && UnwrapTypeSyntax(root.Operands[0]) is GenericsKoto) &&
            TypeSpelling(qualifier) != "Self";

    // SPEC 9.7, 9.7.1: whether such a qualifier keeps an environment from its path (ConstructionTargetType): a structure, a member access,
    // or a nested Container with slots or Origins; otherwise it is the supplied Type or, open in every slot, its declaration Type.
    private static bool FormedByPath(Koto qualifier, BindingSymbol symbol)
        => symbol.Declaration is StructKoto || qualifier is MemberAccessKoto ||
            (symbol.Schema is { GenericSlots.Count: > 0 } or { Origins.Count: > 0 } && symbol.Declaration.Parent is DeclarationContainerKoto { IsRoot: false });

    // SPEC 6.5.2 item 2, 9.7.1, 10.8.1: the owned Type of `symbol` that an expected Type supplies to a construction qualifier (U at an
    // expected `ref/U`), or null.
    private BoundType? SuppliedOwner(BindingSymbol symbol, BoundType? expected)
        => this.ExpectedTypeOf(expected) is { Semantics: SemanticsKind.Owner } result && ReferenceEquals(result.Symbol, symbol) ? result : null;

    private BoundType? EnumQualifierType(Koto qualifier, BindingSymbol symbol, BindingScope scope, BoundType? expected)
    {
        BoundType? type;
        if (qualifier is ParenthesizedTypeKoto or TypeSemanticsKoto { Type: not null })
        {
            type = this.BindType(qualifier, scope, this.TypeContext(qualifier, scope) with { SuppressOuter = true });
        }
        else if (!OmitsOwnList(qualifier) && TypeSpelling(qualifier) != "Self")
        {
            // Only nested complete Type arguments carry annotations here. Enum Origin slots
            // are determined by the construction's expected Type and payloads.
            type = this.BindTypeStructure(qualifier, scope, this.TypeContext(qualifier, scope));
        }
        else if (!OmitsOwnList(qualifier))
        {
            type = this.SelfType(symbol);
        }
        else
        {
            // SPEC 6.5.2 steps 2-3, 9.7: an omitted own list takes the expected owned Type of the same enum. A top-level enum is that Type
            // or, open in every slot, its declaration Type; a nested one (or a structure, which reaches here without own slots) keeps its
            // environment from the path (ConstructionTargetType).
            var supplied = this.SuppliedOwner(symbol, expected);
            type = FormedByPath(qualifier, symbol) ? this.ConstructionTargetType(qualifier, symbol, scope, supplied) : supplied ?? symbol.Type;
        }

        qualifier.BoundSymbol = symbol;
        return Complete(qualifier, type);
    }

    // SPEC 6.5.2 step 2, 9.7.1 (PLAN G924 AF-3b, design-casepath 1): the enum Type a qualified Case construction's qualifier fixes at an
    // expected Type, read without a write: the qualifier's published Type, which the probe bound once with no expectation, where it
    // depends on no expected Type; for an omitted own list the supplied Type (top-level) or the published open reference with its own
    // arguments from the supplied Type (formed by its path, FormReference). Reads neither memberSelections nor
    // inferredConstructionTargets (C3).
    private BoundType? QualifiedEnumAt(Koto qualifier, BindingSymbol symbol, BoundType expected)
    {
        var published = qualifier.BoundType;
        if (!OmitsOwnList(qualifier) || this.SuppliedOwner(symbol, expected) is not { } supplied)
        {
            return published;
        }

        var declaration = (DeclarationContainerKoto)symbol.Declaration;
        var own = declaration.GenericParameterNodes.Count;
        if (!FormedByPath(qualifier, symbol))
        {
            return supplied;
        }

        if (published is null || own == 0)
        {
            return published;
        }

        // N3: the probe's open reference keeps the declaration's own parameters; a stale Type (a completion's) is a broken contract.
        var open = this.SelfType(symbol).Components;
        for (var i = 0; i < own; i++)
        {
            if (!ReferenceEquals(published.Components[i], open[i]))
            {
                throw new InvalidOperationException("A queried construction qualifier lost its open own arguments (InternalInvariant; PLAN G924 AF-3b).");
            }
        }

        var origins = published.OriginArguments.Count == 0 ? default : ((BoundOrigin[])published.OriginArguments).AsSpan(declaration.OriginNames.Count);
        return this.FormReference(symbol, ((BoundType[])supplied.Components).AsSpan(0, own), ((BoundType[])published.Components).AsSpan(own), origins);
    }

    private BindingSymbol? InferredCase(SyntaxFormKoto reference, BindingScope scope, BoundType? expected)
    {
        if (expected is null && this.MissingExpectationCause(reference) is { } cause)
        {
            this.CompleteDependent(reference, cause);
            return null;
        }

        // SPEC 10.2, 10.8.1: the expected enum of a contextual Case at `ref/U` is U's.
        if (this.CaseAt(reference, scope, this.ExpectedTypeOf(expected), out var missing) is not { } member)
        {
            this.Fail(reference, missing, missing == BindingFailure.MissingType);
            return null;
        }

        var name = (IdentifierNameKoto)reference.Operands[0];
        reference.BoundSymbol = name.BoundSymbol = member;
        Complete(name, BoundType.Unit);
        return member;
    }

    // SPEC 6.5.2: the Case that a contextual `.Name` names in the expected Type, a lookup that writes no node (InferredCase publishes
    // it). Null with why there is none: MissingType when the expected Type is no owned enum Type, MissingName when that enum has no
    // such Case.
    private BindingSymbol? CaseAt(SyntaxFormKoto reference, BindingScope scope, BoundType? expected, out BindingFailure missing)
    {
        missing = BindingFailure.MissingType;
        if (expected is not { Semantics: SemanticsKind.Owner, Symbol.Declaration: EnumKoto } || reference.Operands.Length != 1 || reference.Operands[0] is not IdentifierNameKoto name)
        {
            return null;
        }

        missing = BindingFailure.MissingName;
        return this.LookupMember(expected, name.IdentifierName, scope, LookupRole.EnumCase).Member;
    }

    // SPEC 6.5.2: what a Case reference names where it is published: the Case its lookup selected, and for a qualified reference the
    // declaring Type that lookup recorded, for a contextual one the expected Type (U at an expected `ref/U`, SPEC 10.8.1).
    private ConstructionTarget PublishedTarget(Koto reference, BindingSymbol symbol, BoundType? expected)
        => new(symbol, reference is MemberAccessKoto access && this.memberSelections.TryGetValue(access, out var selection) ? selection.DeclaringType : this.ExpectedTypeOf(expected));

    // SPEC 6.5.1, 6.5.2 (design-casepath 2.2 step 2): whether a referenced Case takes a construction's arguments: its arity, its and its
    // enum's declarations not invalid, a qualified reference's member on the Type side, no labelled argument and every payload Type
    // bound. E-free; the publisher's TypeMismatch otherwise.
    private bool CaseTakes(Koto reference, BindingSymbol symbol, InvocationKoto? call)
    {
        var enumeration = symbol.EnumCase!;
        var payload = enumeration.Payload;
        if (!EnumArityMatches(payload.Length, call) || symbol.Declaration.BindingState == BindingState.Invalid ||
            enumeration.Owner.Declaration.BindingState == BindingState.Invalid || (reference is MemberAccessKoto member && this.MemberSideOf(member) != MemberSide.Type))
        {
            return false;
        }

        for (var i = 0; i < payload.Length; i++)
        {
            if (call!.GetArgumentLabel(i) is not null || payload[i].BoundType is null)
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 6.5.2 item 2, 10.8, 10.8.1 (DECISIONS BU-2 U5; design-casepath 2.2 step 3): the slots a construction's qualifier and expected
    // Type fix before any evidence: the owner's components (an inferred qualifier's own slots left open), then every slot still open and
    // the Origins from the expected Type, an owned Type of the enum (none at no expected Type or Never). A qualifier's explicit argument
    // is kept and the expected Type fills only the open slots; the result's fit judges the kept argument (a structure or Semantics
    // difference is its Type mismatch, an Origin-only difference its Origin relation). False when the expected Type is no owned Type of
    // the enum or too short.
    private bool FixConstructionSlots(in ConstructionTarget target, bool inferred, BoundType? expected, SlotFrame frame, BoundOrigin[] origins)
    {
        var enumeration = target.Case.EnumCase!;
        var declaration = (EnumKoto)enumeration.Owner.Declaration;
        var slots = declaration.BoundSymbol!.Schema!.GenericSlots.Count;
        if (target.Owner is { } owner)
        {
            for (var i = 0; i < Math.Min(slots, owner.Components.Count); i++)
            {
                if (!OpenConstructionSlot(declaration, owner, inferred, i))
                {
                    frame.SetType(i, owner.Components[i]);
                }
            }
        }

        if (expected is null || ReferenceEquals(expected, BoundType.Never))
        {
            return true;
        }

        if (ExpectedConstructionOrigins(expected, enumeration.Owner) is not { } supplied || expected.Components.Count < slots)
        {
            return false;
        }

        for (var i = 0; i < slots; i++)
        {
            if (frame.Types[i] is null)
            {
                frame.SetType(i, expected.Components[i]);
            }
        }

        for (var i = 0; i < Math.Min(declaration.BoundSymbol.Schema.Origins.Count, supplied.Count); i++)
        {
            origins[i] = supplied[i];
        }

        return true;
    }

    // SPEC 6.5.2: the constructed Type of an enum from its solved slots and Origins.
    private BoundType FormConstruction(BindingSymbol enumeration, SlotFrame frame, BoundOrigin[] origins)
    {
        var schema = enumeration.Schema!;
        var slots = schema.GenericSlots.Count;
        return this.types.Intern(slots == 0 ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, enumeration, SemanticsKind.Owner, frame.Types[..slots]!, originArguments: origins.AsSpan(0, schema.Origins.Count));
    }

    // SPEC 6.5.2, 10.5.1, 10.2 (PLAN G924 AF-3b; design-casepath 2): whether a Case construction that awaits its call checks at an
    // expected Type, as a query that writes nothing (Binding.ArgumentKinds.Checked): at the construction's expected result (U at
    // `ref/U`, 10.8.1) its target (CaseAt, QualifiedEnumAt), its arguments (CaseTakes), its result formed from the fixed slots
    // (FixConstructionSlots, FormConstruction) and fitted structurally, and each payload fitted at its payload Type (PayloadTypeAt,
    // FitArgument: a payload awaiting its own call recurses through the memo). Origin relations, the payloads' own errors and the
    // Constraints are judged by the selected candidate's completion (SPEC 10.1, 15.6.1). A Case construction is an owner value: its class
    // is a borrowed temporary at `ref/U`, else Expected-Type fitting.
    private CheckedVerdict FitConstructionAt(Koto node, BoundType expected, BindingScope scope)
    {
        var result = this.ExpectedTypeOf(expected);
        var call = node as InvocationKoto;
        var reference = KotoHelper.UnwrapParentheses(call?.Method ?? node);
        var target = reference switch
        {
            SyntaxFormKoto { Akind: KotoKind.InferredCase } contextual when this.CaseAt(contextual, scope, result, out _) is { } member => new ConstructionTarget(member, result),
            MemberAccessKoto { BoundSymbol.EnumCase: not null } qualified when qualified.Left.BoundSymbol is { } enumeration =>
                new ConstructionTarget(qualified.BoundSymbol!, this.QualifiedEnumAt(qualified.Left, enumeration, result)),
            _ => default(ConstructionTarget?),
        };
        var query = this.OpenQuery(); // A payload's fit may defer an acquisition or query its own parts.
        try
        {
            var fits = target is { } found && this.CaseTakes(reference, found.Case, call) && this.FitsConstructionAt(found, call, result, scope, node);
            return fits ? new(null, this.CheckedClass(expected, ownerResult: true)) : new(CallSelection.NoneApplicable, default);
        }
        finally
        {
            this.CloseQuery(query);
        }
    }

    // The result and payload steps of FitConstructionAt, over a rented frame returned before it returns.
    private bool FitsConstructionAt(in ConstructionTarget target, InvocationKoto? call, BoundType result, BindingScope scope, Koto use)
    {
        var enumeration = target.Case.EnumCase!;
        var schema = enumeration.Owner.Schema!;
        var frame = this.infer.Rent(schema.GenericSlots.Count, scope);
        var origins = this.originScratch.Rent(schema.Origins.Count);
        Array.Clear(origins, 0, schema.Origins.Count);
        try
        {
            if (!this.FixConstructionSlots(target, inferred: false, result, frame, origins))
            {
                return false;
            }

            var formed = this.FormConstruction(enumeration.Owner, frame, origins);
            if (!this.FitsType(formed, result) && !this.FitsStructurallyAt(formed, result, use))
            {
                return false;
            }

            for (var i = 0; i < enumeration.Payload.Length; i++)
            {
                var source = call!.ArgumentNodes[i];
                if (this.FitArgument(source, this.PayloadTypeAt(enumeration.Payload[i], formed, scope, out _), scope, source, out _) is
                    CandidateApplicability.Inapplicable or CandidateApplicability.Pending)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
            this.infer.Return(frame);
        }
    }

    private BoundType? BindEnumConstruction(Koto use, Koto reference, in ConstructionTarget target, InvocationKoto? call, BindingScope scope, BoundType? expected)
    {
        var symbol = target.Case;
        var enumeration = symbol.EnumCase!;
        expected = this.ExpectedTypeOf(expected); // SPEC 10.2, 10.8.1: a Case construction is an owner value; its expected result at `ref/U` is U.
        var declaration = (EnumKoto)enumeration.Owner.Declaration;
        var payload = enumeration.Payload;
        var count = call?.ArgumentNodes.Count ?? 0;
        if (!this.CaseTakes(reference, symbol, call))
        {
            return this.Fail(use, BindingFailure.TypeMismatch);
        }

        var owner = target.Owner;
        var inferred = reference is MemberAccessKoto qualified && this.inferredConstructionTargets.Contains(qualified.Left);
        var slots = declaration.BoundSymbol!.Schema!.GenericSlots.Count;
        var originCount = declaration.BoundSymbol.Schema.Origins.Count;
        var frame = this.infer.Rent(slots, scope);
        var origins = this.originScratch.Rent(originCount);
        var operations = this.argumentOperationScratch.Rent(count);
        var settled = this.RentSlotSet(count);
        var adapted = this.typeScratch.Rent(count);
        var fixedTypes = this.typeScratch.Rent(slots);
        var fixedLengths = this.lengthScratch.Rent(slots);
        var rows = this.rowScratch.Rent(count);
        var childSlice = this.indexScratch.Rent(2);
        var childBase = this.childRows.Count;
        var positionBase = this.positionOperations.Count;
        Array.Clear(origins, 0, originCount);
        try
        {
            if (!this.FixConstructionSlots(target, inferred, expected, frame, origins))
            {
                return this.Fail(use, BindingFailure.TypeMismatch);
            }

            // SPEC 6.5.2 step 2, 9.7.1, 10.8: the slots the qualifier and the expected Type fix before any evidence; each payload's adaptation
            // reads them. An inferred qualifier fixes its environment and leaves its own slots open, each as its own parameter (FixedSlots);
            // otherwise the qualifier and the expected Type each supply every slot (SPEC 8.1.3); none fixed is no Types.
            frame.Lengths[..slots].CopyTo(fixedLengths);
            var anyFixed = false;
            for (var i = 0; i < slots; i++)
            {
                fixedTypes[i] = frame.Types[i] ?? (inferred ? owner!.Components[i] : null);
                anyFixed |= frame.Types[i] is not null;
            }

            var solution = this.Solution(call, declaration, scope, frame, origins);
            solution.Fixed = new(declaration, anyFixed ? fixedTypes.AsSpan(0, slots) : default, fixedLengths.AsSpan(0, slots));
            solution.Rows = rows.AsSpan(0, count);
            solution.ChildSlice = ResetSlice(childSlice);
            if (!this.SolveConstruction(use, payload, ref solution, expected, ref settled))
            {
                return null;
            }

            for (var i = 0; i < slots; i++)
            {
                if (frame.Types[i] is null)
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
            solution.OpenSlotVariables(); // G1128: before the rows read the slots.
            solution.ContributePayloads(payload);
            this.RetainChildRows(ref solution); // R3: the payloads' composition, before their completion.
            var slotCount = solution.OriginInference?.SlotVariableCount ?? 0; // G1128 R1.3 (e) 2, as SolveLate's.
            var slotValues = this.originScratch.Rent(slotCount);
            try
            {
                if (expected is null || ReferenceEquals(expected, BoundType.Never) || slotCount > 0)
                {
                    this.SolveConstructionOrigins(ref solution, slotValues.AsSpan(0, slotCount));
                }

                // G1128: the solve's values replace the slot variables; the root rows read them.
                if (solution.RestoreSlotValues(slotValues.AsSpan(0, slotCount)))
                {
                    solution.ContributePayloads(payload);
                }
            }
            finally
            {
                this.originScratch.Return(slotValues, clearArray: true);
            }

            for (var i = 0; i < originCount; i++)
            {
                if (origins[i] is null)
                {
                    return this.Fail(use, BindingFailure.MissingOrigin, true);
                }
            }

            var result = this.FormConstruction(enumeration.Owner, frame, origins);
            if (expected is not null && !this.FitsType(result, expected))
            {
                // SPEC 15.6.1, 10.8: a construction that fits only structurally fails in its Origin relations, never as a Type mismatch, its
                // pair layers read at the use's scope; a Function component's failed contract is the construction's contract record, whose
                // fact its walk gives (RecordMismatch).
                if (!this.FitsStructurallyAt(result, expected, use))
                {
                    return !this.FitsTypeAt(result, expected, use, out var contract) && contract.IsCondition
                        ? this.RecordMismatch(use, use, result, expected, contract) : this.Fail(use, BindingFailure.TypeMismatch);
                }

                if (!this.FitsValueAt(result, expected, use, use, out var failure))
                {
                    return this.RecordMismatch(use, use, result, expected, failure);
                }
            }

            // SPEC 23.1.2, 10.8 (PLAN G418 (2), DECISIONS BU-96): a failed payload stops the payloads after it (PLAN G262 F2), not the
            // judgments over the solution SolveConstruction fixed, since completing a payload fills no slot: the completed payloads'
            // premises, the Constraints and the plan's formation are judged, and then the construction rests on the payload's record.
            var completed = this.CompletePayloads(use, payload, result, scope, operations, adapted, settled);

            // SPEC 15.6.4 step 3: the call's walk over the instantiated inputs, each payload by its rows (JudgePositionRows) where it has
            // child rows, else by its contribution (RequireInputPremises; R4, DECISIONS BU-104).
            var done = 0;
            for (; done < count && operations[done].Source is { } source; done++)
            {
                this.ChildGroup(solution, done, out var childStart, out var childEnd);
                if (childStart != childEnd)
                {
                    this.JudgePositionRows(source, rows[done] with { Type = operations[done].ParameterType }, solution, solution.OriginInference);
                }
                else
                {
                    this.RequireInputPremises(source, payload[done].BoundType!, operations[done].ParameterType!, adapted[done], rows[done].Contribution, solution.OriginInference);
                }
            }

            // A construction that holds its own record (a payload's Type mismatch or acquisition) has no second one: R7-E1's one record
            // per node, whose removal removes this gate (PLAN G418).
            if (!completed && use.BindingFailure != BindingFailure.None)
            {
                return null;
            }

            var proof = this.CheckConstraints(declaration.ConstraintNodes, new CallSubstitution(declaration, frame.Types[..slots]), scope);
            if (proof != ConstraintProof.Proven)
            {
                return proof == ConstraintProof.Unknown ? this.FailUnprovenConstraint(use, this.TakeUnknownFact(), unresolved: true)
                    : this.Fail(use, proof == ConstraintProof.Error ? BindingFailure.InvalidConstraint : BindingFailure.UnsatisfiedConstraint);
            }

            if (!this.enumConstructions.TryGetValue(use, out var plan))
            {
                EnumConstructionRows.Slot(use) = plan = new();
            }

            // A resting construction's plan holds its completed payloads: its formation (the Origin clauses) and their acquisitions are
            // judged as every plan's (CompleteEnumAcquisitions), and no consumer reads it (TryGetEnumConstruction reads a resolved use's).
            plan.Set(enumeration, result, operations.AsSpan(0, done));
            if (!completed)
            {
                return null;
            }

            if (call is not null)
            {
                call.CallStorage = null;
            }

            if (inferred)
            {
                // SPEC 9.7, 10.8.1: an inferred qualifier names the constructed Type, as a structure's does (BindCall).
                Complete(((MemberAccessKoto)reference).Left, result);
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
            this.positionOperations.RemoveRange(positionBase, this.positionOperations.Count - positionBase);
            this.lengthScratch.Return(fixedLengths, clearArray: true);
            this.typeScratch.Return(fixedTypes, clearArray: true);
            this.typeScratch.Return(adapted, clearArray: true);
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.ReturnSlotSet(settled);
            this.originScratch.Return(origins, clearArray: true);
            this.infer.Return(frame);
        }
    }

    // SPEC 6.5.2 item 2, 10.8.1, 10.5.1: a Case construction's own slots after those its qualifier and expected Type fix. A payload whose
    // payload Type those fixed slots settle (`settled`) is bound only at it, on completion; every other payload is prepared as an argument
    // is and is evidence by its kind (SlotSolution.InferArgument). Under the argument probe a construction with a slot that no evidence
    // binds waits; otherwise the parts' own Types apply step by step (OwnTypeSteps: the literals' defaults, then the waiting nested
    // calls) to the slots that neither evidence nor a waiting callable at an F binds (SPEC 10.5, TakeWaiting), and are evidence once
    // applied, each step over the slots the steps before it left unsolved. Once every other slot is bound, the waiting callables at an
    // F are completed with S and are evidence (SPEC 10.5.4), as a call's are after selection. False when the construction is complete:
    // failed or waiting.
    private bool SolveConstruction(Koto use, ReadOnlySpan<Koto> payload, ref SlotSolution solution, BoundType? expected, ref SlotSet settled)
    {
        for (var i = 0; i < payload.Length; i++)
        {
            // Before any evidence the frame holds exactly the fixed bindings, an open slot unbound: a payload is settled when they complete it.
            if (this.SubstituteType(payload[i].BoundType!, solution.SlotOwner, solution.Types, solution.Lengths) is { } fixedType &&
                !this.LacksImage(fixedType, solution.SlotOwner, solution.Origins.Slice(0, solution.OriginCount)))
            {
                settled.Add(i);
            }

            // SPEC 10.5.1 (P-A (a)): every payload is prepared as a named call's argument is (P1), a settled one too, since its fit reads
            // it (CompletePayloads); a payload a probe left awaiting its call is prepared already (design-casepath R4).
            if (!this.AwaitsCall(solution.Call.ArgumentNodes[i]) && !this.PrepareArgument(solution.Call.ArgumentNodes[i], solution.Scope, default, callableContext: true))
            {
                Complete(use, null); // The payload's own failure, on which the construction rests.
                return false;
            }
        }

        var unsolved = this.RentSlotSet(CallSlotCount(solution.Binder));
        var left = this.RentSlotSet(CallSlotCount(solution.Binder));
        try
        {
            if (!this.InferPayloads(use, payload, ref solution, settled, false))
            {
                return false;
            }

            if (!UnsolvedPayloadSlots(payload, solution, ref unsolved))
            {
                return true;
            }

            // SPEC 10.5.1 (D4): under the argument probe a construction waits while its result, which holds all its slots, holds one
            // that neither evidence nor a waiting callable fixes, as a nested call does.
            if (this.HoldsOpenSlot(payload, solution, unsolved) && this.DeferNestedCall(use, expected))
            {
                Complete(use, null);
                return false;
            }

            solution.LeaveUnbound(ref left);
            foreach (var kind in OwnTypeSteps)
            {
                if (unsolved.IsEmpty)
                {
                    break;
                }

                if (solution.DefaultPayloads(payload, unsolved, kind) is { } misfit)
                {
                    this.CompleteDependent(use, misfit); // SPEC 12.3: the literals' own records, on which the construction rests.
                    return false;
                }

                // The late pass with the own Types applied so far, over the slots still unsolved.
                if (!this.InferPayloads(use, payload, ref solution, settled, true))
                {
                    return false;
                }

                if (!UnsolvedPayloadSlots(payload, solution, ref unsolved))
                {
                    return true;
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
            this.ReturnSlotSet(left);
            this.ReturnSlotSet(unsolved);
        }
    }

    // SPEC 15.6.4 steps 1, 3, 15.3.6 (Rule C): the call's solve of the construction's Origins (SolveOriginInference), which the enum's
    // clauses and each leaf row's premises over its payload's instantiated sub-Type bound (CollectPositionPremises; PLAN G1115), as
    // SolveLate's do a candidate's. Evidence that carries an Origin began the inference (InferAdapted); without it or a Signature row
    // nothing bounds them.
    private void SolveConstructionOrigins(ref SlotSolution solution, Span<BoundOrigin> slotValues)
    {
        this.CollectPositionPremises(ref solution);
        this.ReturnSlotSet(solution.EvidenceNamed); // Read by the discoveries above only.
        if (solution.OriginInference is not { } inference)
        {
            return;
        }

        // A selection's solve never fails (SolveOriginInference with `select`); the payload walk and the Type-formation check judge it.
        _ = this.SolveOriginInference(inference, solution.Origins, solution.Inputs, slotValues, select: true);
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
        var wholes = this.wholes.Count;
        for (var i = 0; i < payload.Length; i++)
        {
            if (!settled.Contains(i) && !solution.InferArgument(payload[i].BoundType!, solution.Call.ArgumentNodes[i], late))
            {
                this.wholes.RemoveRange(wholes, this.wholes.Count - wholes);
                this.Fail(use, BindingFailure.TypeMismatch);
                return false;
            }
        }

        if (!solution.BindWholes(wholes, late))
        {
            this.Fail(use, BindingFailure.TypeMismatch);
            return false;
        }

        return true;
    }

    // SPEC 6.5.2, 10.5.1, 10.2: each payload in source order fits its decided payload Type as an argument does (a settled one is bound at
    // it instead), is completed at it (ErasePart for a callable) and adapted to it. The construction judges the fit once: a misfit is its
    // Type mismatch, one only in Origin relations fails at the payload (SPEC 15.6.1), and a bare Place without a plan names its required
    // spelling (SPEC 6.5.2) beside the fit's record (SPEC 23.1.2); a payload's own failure while bound (PartFailed, as at a call
    // argument; an erasure's Owned failure is its value's, DECISIONS BU-107) is its record, on which the construction rests. False at
    // the first failing payload: the payloads after it are not completed (PLAN G262 F2, R7-E1) and keep no operation, those before it keep
    // theirs, and no slot is filled.
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
            var type = this.PayloadTypeAt(payload[i], result, scope, out var stored);
            var fit = this.FitArgument(source, type, scope, source, out var fitted); // SPEC 10.5.1: every payload, settled or not (P-A (a)).
            _ = this.TakeTrialChecked(use); // A failed check is the construction's misfit below.
            if (fit is CandidateApplicability.Inapplicable or CandidateApplicability.Pending)
            {
                this.Fail(use, BindingFailure.TypeMismatch);
                return false;
            }

            this.acquisitionFailure = null;
            var completed = fit == CandidateApplicability.Error ? null : this.PropagateBound(source, type) ?? this.BindNode(source, scope, type);

            // SPEC 5.7, 23.1.2: the layers admit what the bound payload puts in them, a failed part being no evidence (NonNullAtLayers), so
            // the payload's own failure does not hide another layer's record; a refused layer is a relation problem that leaves the
            // construction Resolved (JudgeHolderAccess).
            if (this.HoldsHolder(stored))
            {
                this.JudgeHolderAccess(stored, HolderAccess.Write, source, source);
            }

            if (completed is null || this.PartFailed(source) ||
                (fitted.Adaptation == ArgumentAdaptation.Erasure && completed is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } &&
                !this.ErasePart(source, completed, type)))
            {
                Complete(use, null); // The payload's own failure, on which the construction rests.
                return false;
            }

            var actual = this.ArgumentType(source, completed); // An erased payload adapts as its erasure (AdaptInput, rule E; BU-83).
            var failure = default(ComparisonFailure);

            // The acquisition and the fit are independent (PLAN G262): a failed acquisition is deferred, the fit judged on the Type it
            // supplies, and the failure explains the payload where its structure fits, as a candidate's does (TryCandidate). A payload's
            // Borrow of a static root is the located limit, its relations waiting for P42 (FailStaticBorrow).
            var fits = this.AdaptInput(source, FixedPattern.Open(type), actual, scope, null, null, out var adaptedType, out var quality, out var kind, deferAcquisition: true) &&
                (this.FailStaticBorrow(source, kind, type.Semantics) || this.FitsValueAt(adaptedType, type, source, source, out failure));
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

            operations[i] = new(source, actual, type, kind, quality, ParameterIndex: i, AdaptedType: adaptedType);
            adapted[i] = adaptedType;
            if (this.FormsBorrows)
            {
                // Its writer forms its Borrow, before the payloads after it bind: an awaiting one's header can state its atom
                // (Binding.RawOrigins.Borrows).
                this.OperationBorrow(this.borrows, operations[i]);
            }
        }

        return true;
    }

    // SPEC 6.5.2, 5.7: a payload's Type in the constructed Type `result`: its stored Type there (`stored`), with a Case holding
    // `during self` constructed through no dereference.
    private BoundType PayloadTypeAt(Koto payload, BoundType result, BindingScope scope, out BoundType stored)
    {
        stored = this.PartTypeIn(payload, result, scope)!;
        return this.ReplaceHolder(stored, null);
    }

    private void CompleteEnumAcquisitions()
    {
        // D2b-d's d8 order: the source modules by ordinal, then the library, each by ascending id (the walk's order on a cold compile).
        for (var rows = this.enumConstructions.Values.In(this.pass); rows.MoveNext();)
        {
            var (use, plan) = (rows.Node, rows.Current!);
            if (!plan.IsValid)
            {
                continue;
            }

            this.CompleteEnumOrigins(use, plan);

            if (plan.Case.Symbol.Declaration.BindingState != BindingState.Resolved ||
                plan.Case.Owner.Declaration.BindingState != BindingState.Resolved || this.InvalidDeclarationContext(plan.Case.Owner.Declaration))
            {
                plan.IsValid = false;
                this.Fail(use, BindingFailure.InvalidTypeFormation);
                continue;
            }

            var scope = this.ConstraintScope(use);
            var formation = this.CheckTypeConstraints(plan.Type, scope);
            for (var i = 0; i < plan.PayloadOperations.Length; i++)
            {
                formation = CombineProof(formation, this.CheckTypeConstraints(plan.PayloadOperations[i].ParameterType!, scope), true);
            }

            if (formation != ConstraintProof.Proven)
            {
                plan.IsValid = false;
                this.Fail(use, formation == ConstraintProof.Error ? BindingFailure.InvalidConstraint : formation == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, formation == ConstraintProof.Unknown);
                continue;
            }

            for (var i = 0; i < plan.PayloadOperations.Length; i++)
            {
                var operation = plan.PayloadOperations[i];
                var proof = this.ProveCopy(operation.ParameterType!, use);
                if (proof == ConstraintProof.Error)
                {
                    plan.IsValid = false;
                    this.Fail(use, BindingFailure.InvalidConstraint);
                }
                else if (operation.Kind == ArgumentOperationKind.Value && operation.Source is { } payloadSource && this.IsBarePlace(payloadSource) &&
                    this.BareAcquire(operation.SourceType!, payloadSource, out _) == HirAcquire.Move)
                {
                    // SPEC 6.5.2, 3.5, 8.14: a payload Place is acquired by its bare acquisition, by case for a pair layer; one without a
                    // plan never Moves into a payload; write value@move.
                    plan.IsValid = false;
                    this.FailAcquisition(use, BindingFailure.TransferRequired, payloadSource);
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
            if (!declaration.Resolves || declaration.Substitutions == 0)
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

                // A resting construction forms no result (DECISIONS BU-96 amended; whether it publishes one is PLAN G495's), so only a
                // published result Type is rewritten.
                if (use.BoundType is not null)
                {
                    use.BoundType = type;
                }
            }
            finally
            {
                this.argumentOperationScratch.Return(operations, clearArray: true);
            }
        }
    }

    /// <summary>The `enumConstructions` family's one accessor (DECISIONS BU-220): a kept family, a construction expression's plan kept in its
    /// node's module's NodeColumns at its SyntaxId, made once and reset in place by each pass (ResetPass's IsValid). Its members keep the
    /// names its readers use.</summary>
    private readonly struct EnumConstructionRows(Binding binding)
    {
        internal FamilyRows<BoundEnumConstruction?> Values => new(binding, static (table, _) => table.Hir.Families.EnumConstructions.Enumerate());

        internal static ref BoundEnumConstruction? Slot(Koto use) => ref HirTables.Of(use).Families.EnumConstructions.Ref(use.SyntaxId);

        internal bool TryGetValue(Koto use, [NotNullWhen(true)] out BoundEnumConstruction? plan)
            => (plan = use.SyntaxOwner is { } table ? table.Hir.Families.EnumConstructions.Get(use.SyntaxId) : null) is not null;
    }

    // SPEC 6.5.2: the Case a reference names and the enum Type that its qualifier and the expected Type fix (its declaring Type for a
    // qualified reference, the expected Type for a contextual one): what a Case construction is bound from.
    private readonly record struct ConstructionTarget(BindingSymbol Case, BoundType? Owner);
}
