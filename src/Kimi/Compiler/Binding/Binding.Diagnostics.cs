// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.2.2, 10.7: no ordering is defined between an erasure and a direct match at one argument; the erasure's receiver and Owned
    // conditions are judged only after selection, so they never make it inapplicable.
    private const string ErasureAmbiguityNote = "At one argument, one candidate erases the argument to a common Function Type and another takes it directly; these adaptations are incomparable (SPEC 10.2.2, 10.7), and the erasure's receiver and Owned conditions are judged only after selection";

    private const string SharedObjectAuthorityNote = "rc and arc provide shared payload access only; even a strong count of one does not grant objuniq or uniq/Self authority";

    // Keep proof failures intact. Only diagnostic publication follows these recorded
    // missing-name causes; later validators must still see an invalid declaration.
    private Dictionary<Koto, Koto>? constraintDiagnosticCauses;
    private Dictionary<Koto, BindingSymbol>? objectPayloadCauses;
    private MissingConstraintNameVisitor? missingConstraintNameVisitor;

    private FailedSignaturePartVisitor? failedSignaturePartVisitor;

    // The expression being bound without its expected Type because the syntax that supplies it failed.
    private (Koto Expression, Koto Cause)? missingExpectation;

    // SPEC 23.3: the Types a mismatch compared and the syntax that shows it, recorded only when a check fails.
    private Dictionary<Koto, (Koto At, object Actual, object Expected)>? mismatches;

    // SPEC 15.6.1: the Origin relation that an Origin-only fit failure leaves, at the value that supplies its longer end.
    private Dictionary<Koto, OriginRelationFact>? originRelations;

    private Dictionary<Koto, OriginContractFact>? originContracts;

    // SPEC 15.2.3: the Owned failure of a common Function conversion, recorded only when it fails.
    private Dictionary<Koto, OwnedConversionFact>? ownedConversions;

    // SPEC 10.6, 15.3.6: the reference slot that only per-call Origins of its fixed expected call signature would satisfy.
    private Dictionary<Koto, PerCallSlotFact>? perCallSlots;

    // SPEC 10.5: why a single generic reference's slots did not bind from its fixed expected call signature, for its Type-mismatch Note.
    private Dictionary<Koto, ReferenceSlotFact>? referenceSlotFacts;

    // The selected iteration entry and the range whose boundary Types cannot supply it.
    private Dictionary<Koto, (BoundType Subject, BindingSymbol Entry)>? rangeIterationFailures;

    // The target a write could not use, recorded only when the check fails; it is the smallest syntax that shows the failure.
    private Dictionary<Koto, Koto>? writeTargets;

    // The candidates a failed overload selection considered, recorded only when it fails.
    private Dictionary<Koto, RejectedCandidate[]>? rejectedCandidates;

    // SPEC 15.1.5, 23.1.3: the bare Place whose acquisition a failed node needs spelled, and whether an exclusive borrow of it is of an
    // object handle, recorded only when the check fails.
    private Dictionary<Koto, (Koto Place, bool Object)>? acquisitionPlaces;

    // SPEC 23.1.1: the part of an unsupported node that shows the form, such as one capture entry or a callee.
    private Dictionary<Koto, SourceSpan>? unsupportedSpans;

    // SPEC 9.7.1: a Type or container named with the wrong number of its own Type arguments, or without the Type arguments of
    // the generic container that declares it (Outer), with the declared and the written counts.
    private Dictionary<Koto, (BindingSymbol Declaration, int Declared, int Written, bool Outer)>? arityFailures;

    private readonly record struct RejectedCandidate(FunctionKoto Function, BoundType? Actual, BoundType? Expected, bool SharedReceiver = false, bool ObjectClone = false, bool CallableSignature = false, bool Selected = false, SemanticsKind? ActualReceiver = null, SemanticsKind? RequiredReceiver = null, bool ReferenceSignature = false, bool UnfixedReference = false, int ReceiverParameter = -1, bool ErasureIncomparable = false, ReferenceConstraintFailure? ConstraintFailure = null);

    // The referenced declaration, its Type parameter, the parameters of the fixed expected call signature whose per-call Origins the
    // slot would hold (as bits) and that signature.
    private readonly record struct PerCallSlotFact(FunctionKoto Declaration, int Slot, ulong Parameters, BoundType Signature);

    // The referenced declaration, its Type parameter when one is known (else -1), the failure and whether Type arguments are written.
    private readonly record struct ReferenceSlotFact(FunctionKoto Declaration, int Slot, ReferenceSlotFailure Failure, bool Explicit);

    // SPEC 15.6.1, 23.1: the Reason facts and related locations of an Origin relation record, shared by every phase that reports
    // one; `source` is `fit` for a fit, `declared` for a relation clause and `wellFormed` for a Type occurrence.
    internal static (object[] Evidence, (string Role, Koto At, string? Label)[]? Related) OriginRelationFacts(OriginRelationFact relation, string source)
    {
        // SPEC 15.6.5: a local initialized by a Borrow shows that Borrow, which is also the related location. An input's well-formedness
        // fails over an Origin that another input of the call supplied, so its Borrow is found in that call.
        var borrow = BorrowSource(relation.At, relation.Supplier) ??
            (relation.WellFormed && EnclosingCall(relation.At) is { } call ? BorrowSource(call, relation.Supplier) : null);
        // A clause of a Field or Case names its Type's own slots as written there, without `self.`; a Type's clause substituted at
        // a use in a body names the substituted Origins as that body does.
        var typeLevel = relation.Clause is not null && !relation.Substituted && TypeLevelClause(relation.At);
        var longer = OriginDisplay(relation.Longer, borrow, typeLevel);
        // SPEC 15.6.1, 23.1: a shorter end that an equality fixed to another argument's Borrow shows that Borrow too.
        var shorterBorrow = relation.FixedBy is { } fixedBy && relation.Shorter.Kind is OriginKind.Projection or OriginKind.Anchor ? BorrowSource(fixedBy, relation.Shorter) : null;
        var shorter = OriginDisplay(relation.Shorter, shorterBorrow, typeLevel);
        var operation = relation.Equality ? "==" : "outlives";
        // SPEC 23.1: a borrow or omitted end is also related at its syntax.
        var longerAt = longer.Kind == "borrow" ? borrow ?? relation.At : longer.Kind == "omitted" ? OmittedAt(relation.Longer)
            : longer.Kind == "closure" && RoleOf(relation.Longer) == ProjectionRole.ClosureValue ? relation.Longer.Binder : null; // ρ's literal.
        var shorterAt = shorter.Kind == "omitted" ? OmittedAt(relation.Shorter) : shorter.Kind == "borrow" ? shorterBorrow : null;
        (string Role, Koto At, string? Label)[]? related = longerAt is not null && shorterAt is not null ? [("origin", longerAt, null), ("origin", shorterAt, null)]
            : longerAt is not null ? [("origin", longerAt, null)]
            : shorterAt is not null ? [("origin", shorterAt, null)]
            : null;
        // SPEC 23.1: a declared relation relates its clause with the role `relation`; SPEC 15.6.1: so does the input whose equality made a
        // call's fresh Origin equal to a fixed one; D-1: the contextual payload that supplies a relation the call records at its argument has
        // the role `payload`.
        foreach (var (role, at) in (ReadOnlySpan<(string, Koto?)>)[("relation", relation.Clause), ("relation", relation.FixedBy), ("payload", relation.Payload)])
        {
            if (at is not null)
            {
                related = related is null ? [(role, at, null)] : [.. related, (role, at, null)];
            }
        }

        if (source != "fit")
        {
            // SPEC 23.1: only a fit names its destination Type; a declared relation relates its clause instead, and well-formedness
            // its Type occurrence, which is the primary location.
            return ([operation, longer, shorter, source], related);
        }

        // SPEC 23.1: the destination shows only the Origin at the failed position, also below another borrow layer; a borrow, omitted
        // or closure end is never written as an Origin expression.
        var destination = relation.Destination is not { } type ? $"during {shorter.Text}"
            : DiagnosticTypeName(type, shorter.Kind == "expression" ? relation.Shorter : null, shorter.Text);
        return ([operation, longer, shorter, source, destination], related);
    }

    internal static DiagnosticCode OriginRelationCode(OriginRelationFact relation)
        => relation.Refuted ? DiagnosticCode.UnsatisfiedOriginRelation_Kd : DiagnosticCode.UnprovenOriginRelation_Kd;

    /// <summary>Forms the Transfer candidate of a bare Place (SPEC 23.1.3): <c>@move</c> after the Place, or <c>(*p)@move</c> around a dereference.</summary>
    /// <param name="node">The reporting node, in the Place's document.</param>
    /// <param name="place">The Place.</param>
    /// <param name="judgment">The Take judgment; never refuted.</param>
    /// <returns>The candidate.</returns>
    internal static DiagnosticRepairFact[] TransferRepair(Koto node, Koto place, AcquisitionJudgment judgment)
    {
        DiagnosticEditFact[] edits = place is DereferenceKoto
            ? [node.Edit(new(place.Span.Start, 0), "("), node.Edit(new(place.Span.End, 0), ")@move")]
            : [node.Edit(new(place.Span.End, 0), "@move")];
        var verified = judgment == AcquisitionJudgment.Verified ? RepairConditionSet.Take : RepairConditionSet.None;
        var required = RepairConditionSet.UsageLegality | (judgment == AcquisitionJudgment.Required ? RepairConditionSet.Take : RepairConditionSet.None);
        return [new(RepairKind.Transfer, [place.ToString(), TransferTarget(place)], edits, verified, required)];
    }

    // The destination a Place is acquired for, as the Transfer title names it.
    internal static string TransferTarget(Koto place)
        => Destination(place) switch
        {
            InvocationKoto call when ReferenceEquals(KotoHelper.UnwrapParentheses(call.Method), KotoHelper.UnwrapParentheses(place)) => "its call",
            InvocationKoto call => call.Method is MemberAccessKoto member ? member.Right.ToString() : call.Method.ToString(),
            VariableKoto variable => $"the binding {variable.NameKoto.IdentifierName}",
            ReturnKoto => "the result",
            ArrayLiteralKoto or TupleLiteralKoto or DictionaryLiteralKoto => "the element",
            _ => "its destination",
        };

    private static Koto? Destination(Koto place)
    {
        var parent = place.Parent;
        while (parent is ParenthesizedKoto)
        {
            parent = parent.Parent;
        }

        return parent;
    }

    // The call whose argument or receiver `at` is.
    private static InvocationKoto? EnclosingCall(Koto at)
    {
        for (var node = at.Parent; node is not null and not (FunctionKoto or CodeBlockKoto); node = node.Parent)
        {
            if (node is InvocationKoto call)
            {
                return call;
            }
        }

        return null;
    }

    // Whether a clause belongs to a Field or Case, whose Type's premises decide it, rather than to a local of a function or accessor.
    private static bool TypeLevelClause(Koto clause)
    {
        for (var node = clause.Parent; node is not null and not DeclarationContainerKoto; node = node.Parent)
        {
            if (node is FunctionKoto or PropertyAccessorKoto)
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 23.1: an omitted Origin is related at its Type occurrence. Closed annotation atoms and open local-input atoms name
    // such occurrences; open call/evidence atoms do not. A local region is shown by the local's name.
    private static Koto? OmittedAt(BoundOrigin origin)
        => origin.Occurrence ?? OmittedInput(origin) ?? (origin is { Kind: OriginKind.Inference, Binder: { } occurrence and not VariableKoto } && (!origin.Open || origin.IsLocalInput) ? occurrence : null);

    // SPEC 23.1: the input Type occurrence of a Function Type whose outer Origin is this per-call Origin; it has no name.
    private static Koto? OmittedInput(BoundOrigin origin)
        => origin is { Kind: OriginKind.Input, Occurrence: null, Binder: FunctionTypeKoto binder } && origin.InputIndex >= 0 && origin.InputIndex < InputCount(binder)
            ? InputType(binder, origin.InputIndex) is ParenthesizedTypeKoto { Type: { } inner } ? inner : InputType(binder, origin.InputIndex) : null;

    private static bool SharedObjectAuthorityMismatch(BoundType actual, BoundType expected)
        => actual.HandleMode is { PayloadAuthority: LoanRequirement.Ref } &&
            expected is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 } &&
            ReferenceEquals(actual.Components[0], expected.Components[0]);

    private static bool DifferentRangeShapes(BoundType actual, BoundType expected)
    {
        while (actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            actual = actual.Components[0];
        }

        while (expected is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            expected = expected.Components[0];
        }

        return actual.Symbol?.LibraryDeclaration is KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange &&
            expected.Symbol?.LibraryDeclaration is KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange && actual.Symbol != expected.Symbol;
    }

    // A + whose operands are strings or string joins, through parentheses; the depth bound keeps pathological chains cheap.
    private static bool IsStringJoin(Koto node, int depth)
        => depth < 32 && KotoHelper.UnwrapParentheses(node) is BinaryKoto { Akind: KotoKind.Plus } join && IsStringOperand(join.Left, depth + 1) && IsStringOperand(join.Right, depth + 1);

    private static bool IsStringOperand(Koto node, int depth)
        => ReferenceTypes.EndsInString(KotoHelper.UnwrapParentheses(node).BoundType) || IsStringJoin(node, depth);

    // SPEC 15.6.1, 23.1: an Origin relation record at the value that supplies the longer end, with both ends as Origin
    // displays, the relation's source and the destination Type, in which only the Origin at the failed position is shown; a
    // borrow end is related at its syntax; the SPEC 15.4.3 elision Note follows an omitted result Origin.
    // SPEC 15.6.1, 23.1: the Reason names the comparison, the member, the relation and its two ends, rigid symbols of the comparison
    // displayed as the required contract writes them; an omitted end is related at its Type occurrence.
    private static void ReportOriginContract(Koto node, OriginContractFact contract, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (contract.Required is { } original)
        {
            node.Report(requirement, code, evidence: ["implementation", contract.Member, contract.Equality ? "==" : "outlives", OriginDisplay(contract.Longer, null), OriginDisplay(contract.Shorter, null)], related: [("requirement", original, "the original implementation contract")], at: contract.At);
            return;
        }

        // An erased closure's environment binding lies within its call receiver, which no Origin expression names (SPEC 15.8.2).
        var longer = ReceiverAtom(contract.Longer) ? new DiagnosticOrigin("closure", "call receiver") : OriginDisplay(contract.Longer, null);
        var shorter = OriginDisplay(contract.Shorter, null);
        var longerAt = longer.Kind == "omitted" ? OmittedAt(contract.Longer) : null;
        var shorterAt = shorter.Kind == "omitted" ? OmittedAt(contract.Shorter) : null;
        (string Role, Koto At, string? Label)[]? related = longerAt is not null && shorterAt is not null ? [("origin", longerAt, null), ("origin", shorterAt, null)]
            : longerAt is not null ? [("origin", longerAt, null)]
            : shorterAt is not null ? [("origin", shorterAt, null)]
            : null;
        if (longer.Kind == "closure" && contract.Longer.Binder is FunctionKoto closure)
        {
            // SPEC 7.7.4, 23.1: a common Function value cannot return a borrow of its hidden environment receiver; the closure end
            // is related at the anonymous function's header.
            var header = SourceSpan.FromBounds(closure.Span.Start, Math.Max(closure.Span.Start, closure.HeaderEnd));
            const string Note = "A common Function value cannot return a borrow of its hidden environment receiver (SPEC 7.7.4)";
            node.Report(requirement, code, note: Note, evidence: ["conversion", contract.Member, contract.Equality ? "==" : "outlives", longer, shorter], related: related, relatedSpans: [("origin", closure, header, null)], at: contract.At);
            return;
        }

        node.Report(requirement, code, evidence: ["conversion", contract.Member, contract.Equality ? "==" : "outlives", longer, shorter], related: related, at: contract.At);
    }

    // The capture entry that names an environment binding, when the closure has a capture list.
    private static CaptureKoto? CaptureEntryOf(FunctionKoto closure, string name)
    {
        if (closure.Captures is { } captures)
        {
            for (var i = 0; i < captures.Length; i++)
            {
                if (captures[i].Name == name)
                {
                    return captures[i];
                }
            }
        }

        return null;
    }

    // SPEC 7.2.3: the preceding argument of the function whose later default holds a closure that its environment binding takes,
    // if any. The plan is read even when the closure's own conversion failed.
    private static BindingSymbol? PreparedCapture(FunctionKoto closure, string name)
    {
        if (closure.ClosureStorage is { } bound)
        {
            for (var i = 0; i < bound.Captures.Count; i++)
            {
                var capture = bound.Captures[i];
                if (capture.Environment.Name == name && capture.Source is { } source && DefaultParameters.InLaterDefault(closure, source))
                {
                    return source;
                }
            }
        }

        return null;
    }

    private static void ReportOriginRelation(Koto node, OriginRelationFact relation, DiagnosticRequirement requirement, DiagnosticCode code, string? note, ushort condition = 0)
    {
        var (evidence, related) = OriginRelationFacts(relation, relation.Clause is not null ? "declared" : relation.WellFormed ? "wellFormed" : "fit");
        if (relation.Shorter is { Kind: OriginKind.Projection, Slot: CallResultSlot, Binder: FunctionKoto { IsAnonymous: true } closure })
        {
            // SPEC 23.1: a closure end is related at the anonymous function's header, from `func` through the parameter list.
            var header = SourceSpan.FromBounds(closure.Span.Start, Math.Max(closure.Span.Start, closure.HeaderEnd));
            var at = relation.At is CodeBlockKoto ? BorrowSource(relation.At, relation.Supplier) ?? relation.At : relation.At;
            node.Report(requirement, code, note: note, evidence: evidence, related: related, relatedSpans: [("origin", closure, header, null)], at: at);
            return;
        }

        node.Report(requirement, code, note: note, evidence: evidence, related: related, at: relation.At, condition: condition);
    }

    // SPEC 9.7.1: outside constructor-own inference, required Type arguments are written in full; an outer container never takes call
    // arguments or an expected Type; outside a generic container, a nested declaration is named through that container.
    private static void ReportArity(Koto node, (BindingSymbol Declaration, int Declared, int Written, bool Outer) arity, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var name = arity.Declaration.Name;
        var parameters = arity.Declaration.Declaration is DeclarationContainerKoto container ? string.Join(", ", container.GenericParameterNodes) : string.Empty;
        var declared = $"{name}<{parameters}>";
        var member = node.Parent is MemberAccessKoto access && ReferenceEquals(KotoHelper.UnwrapParentheses(access.Left), node) ? access.Right.ToString() : null;
        string note;
        if (arity.Outer)
        {
            note = $"{node} is declared in {declared}; outside it, the Type arguments of {name} are written on the qualifier and are never inferred (SPEC 9.7.1)";
        }
        else if (arity.Written == 0 && member is not null)
        {
            note = $"{declared} is named without its Type arguments; this qualifier requires explicit Type arguments; only the construction target itself can infer its own slots (SPEC 9.7.1, 10.8)";
        }
        else
        {
            note = $"{declared} declares {arity.Declared} Type parameter{(arity.Declared == 1 ? string.Empty : "s")}, and {arity.Written} Type argument{(arity.Written == 1 ? " is" : "s are")} written (SPEC 9.7.1)";
        }

        node.Report(requirement, code, note: note, related: [("declaration", arity.Declaration.Declaration, null)]);
    }

    // The call signature of a Function Item, closure or Function value.
    private BoundType? ValueSignature(BoundType value) => value.Kind switch
    {
        BoundTypeKind.FunctionItem => this.FunctionItemSignature(value),
        BoundTypeKind.Closure => this.ClosureSignature(value),
        BoundTypeKind.Function => value,
        _ => null,
    };

    /// <summary>Binds an expression against the Type that a written syntax supplies; when that syntax failed, a check that
    /// needs the expected Type names it as the prerequisite (SPEC 23.1.2).</summary>
    private BoundType? BindExpected(Koto node, BindingScope scope, BoundType? expected, Koto? supplier)
    {
        if (expected is not null || supplier is null || supplier.BindingState == BindingState.Resolved)
        {
            return this.BindNode(node, scope, expected);
        }

        var saved = this.missingExpectation;
        this.missingExpectation = (node, supplier);
        var type = this.BindNode(node, scope);
        this.missingExpectation = saved;
        return type;
    }

    // An inferred case that is the value of an expression whose expected Type failed rests on that failure.
    private Koto? MissingExpectationCause(Koto reference)
    {
        if (this.missingExpectation is not { } missing)
        {
            return null;
        }

        var value = missing.Expression;
        while (true)
        {
            value = KotoHelper.UnwrapParentheses(value);
            if (value is not CodeBlockKoto { IsExpressionBody: true, Items.Count: 1 } body)
            {
                break;
            }

            value = body.Items[0];
        }

        return ReferenceEquals(value, reference) || (value is InvocationKoto { Method: var method } && ReferenceEquals(method, reference)) ? missing.Cause : null;
    }

    /// <summary>Fails a node whose value does not have the Type its use expects, recording both Types as evidence.</summary>
    /// <param name="node">The node whose check failed.</param>
    /// <param name="at">The smallest syntax that shows the mismatch, such as the value.</param>
    /// <param name="actual">The value's Type.</param>
    /// <param name="expected">The expected Type.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailMismatch(Koto node, Koto at, BoundType actual, BoundType expected)
        => this.RecordMismatch(node, at, actual, expected);

    // Keep semantic identities even when their short names agree. Format only at publication, never during Binding. A fit whose
    // structural part holds failed only in its Origin part, which is an Origin relation, never a Type mismatch (SPEC 15.6.1).
    // An Origin part that only proof leaves unproven, between finite Origins or inferred regions, is a chain the relation judge
    // accepts (SPEC 15.6.5); a fit that still needs it, such as reassigning a borrow local to another Borrow, needs local-region
    // inference and is a located Unsupported (SPEC 23.1.1), never a relation record.
    private BoundType? RecordMismatch(Koto node, Koto at, object actual, object expected)
    {
        // SPEC 15.2.3, 7.7.4: the Owned condition of a common Function conversion is a Constraint failure, never a Type mismatch.
        if (expected is BoundType { Kind: BoundTypeKind.Function } owner && actual is BoundType { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } value &&
            this.FailOwnedConversion(value, owner, at, node))
        {
            return null;
        }

        // SPEC 10.7, 15.6.1: a common Function conversion whose condition fails is recorded where its judgment fails (ErasesToFunction,
        // CheckTypeUse; DECISIONS R4-M155), so it never reaches a Type mismatch here.
        // SPEC 15.6.1: a failed Origin position of matching storage is recorded as such (Binding.OriginFits), never as a Type mismatch.
        if (actual is BoundType actualType && expected is BoundType expectedType && ReferenceTypes.StorageMatches(expectedType, actualType) &&
            this.RecordOriginFitFailure(node, at, actualType, expectedType))
        {
            return null;
        }

        return this.FailExplained(ref this.mismatches, node, BindingFailure.TypeMismatch, (at, actual, expected));
    }

    // SPEC 15.6.1: a Refuted Origin relation is UnsatisfiedOriginRelation_Kd, one that is not proven UnprovenOriginRelation_Kd.
    private DiagnosticCode OriginRelationCode(Koto node)
        => this.originRelations?.TryGetValue(node, out var relation) == true ? OriginRelationCode(relation) : DiagnosticCode.UnprovenOriginRelation_Kd;

    // Fails a node with the facts that explain the failure. A node keeps its first failure only, so the facts are recorded only
    // with that failure: a fact in a publication table always explains the failure of its node, whatever the failure's code.
    private BoundType? FailExplained<T>(ref Dictionary<Koto, T>? facts, Koto node, BindingFailure failure, T fact, bool unresolved = false)
    {
        if (node.BindingFailure == BindingFailure.None)
        {
            (facts ??= new(ReferenceEqualityComparer.Instance))[node] = fact;
        }

        return this.Fail(node, failure, unresolved);
    }

    /// <summary>Fails a write whose target's path denies it (SPEC 3.4, 15.1.5), recording the target.</summary>
    /// <param name="node">The write.</param>
    /// <param name="target">The written target.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailWrite(Koto node, Koto target)
        => this.FailExplained(ref this.writeTargets, node, AccessFailure(target), target);

    private BoundType? FailObjectAuthority(Koto node, Koto target)
        => this.FailExplained(ref this.writeTargets, node, BindingFailure.SharedPathAccess, target);

    // A bare Place that needs its acquisition spelled (TransferRequired or ExclusiveBorrowRequired), with the Place it names.
    private BoundType? FailAcquisition(Koto node, BindingFailure failure, Koto place, bool @object = false, bool unresolved = false)
        => this.FailExplained(ref this.acquisitionPlaces, node, failure, (place, @object), unresolved);

    // SPEC 3.5, 15.1.5, 23.1.3: the acquisition a bare Place needs spelled, located at the Place with the candidate that writes it:
    // an exclusive borrow is verified exclusively writable (BorrowablePlace held), a transfer has Take judged from the Place's path.
    private void ReportAcquisition(Koto node, Koto place, bool @object, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var text = place.ToString();
        if (code == DiagnosticCode.ExclusiveBorrowRequired_Kd)
        {
            var spelling = @object ? "@objuniq" : "@uniq";
            node.Report(requirement, code, at: place, evidence: [text], repairs: [new(RepairKind.BorrowExclusively, [text, spelling], [node.Edit(new(place.Span.End, 0), spelling)], RepairConditionSet.ExclusiveAccess, RepairConditionSet.UsageLegality)]);
            return;
        }

        var judgment = TakeJudgment(place);
        node.Report(requirement, code, at: place, evidence: [text], repairs: judgment == AcquisitionJudgment.Refuted ? null : TransferRepair(node, place, judgment));
    }

    // Reports a write failure at its target; an assignment names the target. An
    // Exclusive call of a closure (SPEC 7.7.3) borrows the callee exclusively, so the callee is the written target.
    private void ReportWrite(Koto node, Koto target, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var call = node is InvocationKoto invocation && ReferenceEquals(invocation.Method, target);
        var callee = target.BoundType is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } reference ? reference.Components[0] : target.BoundType;
        var objectCallee = call && ((target.BoundType is { Kind: BoundTypeKind.Semantics } layer && IsObjectSemantics(layer.Semantics)) ||
            KotoHelper.UnwrapParentheses(target) is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow });
        var plan = BoundClosure.Of(callee);
        var consuming = call && target.BoundType is { } calleeType && this.TryCallable(calleeType, this.ConstraintScope(node), out _, out var calleeReceiver) && calleeReceiver == SemanticsKind.Owner;
        var callNote = !call ? null : consuming ? objectCallee
            ? "The call is Consuming (SPEC 7.7.3): it moves values out of the callee's environment, and an object payload offers no Take (SPEC 13.5.5)"
            : "The call is Consuming (SPEC 7.7.3): it moves values out of the callee's environment, which a reference cannot supply, since a callable value is no read Type (SPEC 3.5.3, 7.3)"
            : plan is { ExclusiveReborrow: true, ExclusiveUse: { } use }
            ? $"The call is Exclusive (SPEC 7.7.3): it borrows the callee exclusively, because the callee Reborrows the captured exclusive reference {use} exclusively"
            : "The call is Exclusive (SPEC 7.7.3): it borrows the callee exclusively, because the callee changes its environment or a captured referent";
        if (code != DiagnosticCode.InvalidAssignment_Kd)
        {
            var handle = KotoHelper.UnwrapParentheses(target) is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } followed ? followed.Left.BoundType : target.BoundType;
            var authority = handle?.HandleMode is { PayloadAuthority: LoanRequirement.Ref } ? SharedObjectAuthorityNote : null;
            var note = code != DiagnosticCode.SharedPathAccess_Kd ? null : objectCallee ? authority is null ? callNote : $"{callNote}. {authority}" : authority ?? this.SharedIndexNote(target);
            node.Report(requirement, code, note: note, at: target, evidence: code == DiagnosticCode.SharedPathAccess_Kd ? [target.ToString()] : null);
            return;
        }

        var root = KotoHelper.UnwrapParentheses(target);
        while (root is MemberAccessKoto or IndexKoto)
        {
            root = KotoHelper.UnwrapParentheses(root is MemberAccessKoto member ? member.Left : ((IndexKoto)root).Left);
        }

        node.Report(requirement, code, note: callNote, at: target, evidence: [target.ToString()]);
    }

    // SPEC 4.7.1, 8.3.3: a user index publishes its element exclusively only through indexUniq. When the receiver's Type
    // lacks it, the shared layer is the element published by index, and the Note names why indexUniq is absent.
    private string? SharedIndexNote(Koto target)
    {
        if (KotoHelper.UnwrapParentheses(target) is not IndexKoto index || this.HasExclusiveIndexer(index) || !ElementAccess.IsUserIndex(index) ||
            this.Library.UniqIndexable is not { } uniqIndexable)
        {
            return null;
        }

        var core = index.Left.BoundType;
        while (core is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            core = core.Components[0];
        }

        if (core?.Symbol is { Declaration: StructKoto or EnumKoto } owner)
        {
            // Several Key conformances are distinct; the key's own Type selects one (SPEC 4.7.1): the check's selection.
            var name = DiagnosticTypeName(core);
            var registered = this.uniqIndexing.GetValueOrDefault(index).Conformance;
            _ = this.ConformanceByDeclaration(owner, uniqIndexable, out var byKey);
            return registered is not null ? $"{name} conforms to UniqIndexable only under a condition that does not hold here, so its element is published by index alone, which grants Read only" :
                byKey ? $"{name} has no UniqIndexable conformance for this key, so its element is published by index alone, which grants Read only" :
                $"{name} has no UniqIndexable conformance, so its element is published by index alone, which grants Read only";
        }

        return core is { Kind: BoundTypeKind.Parameter } ? $"{DiagnosticTypeName(core)} is not required to be UniqIndexable here, so its element is published by index alone, which grants Read only" : null;
    }

    // SPEC 11.3: an accessor receiver has the shape of its operation. The report is located at the written receiver Type and
    // relates the Property header; the written Type is a bounded display value, as the message argument of a transfer is.
    private void ReportAccessorReceiverShape(PropertyAccessorKoto accessor, Koto written, DiagnosticRequirement requirement)
    {
        var property = (PropertyKoto)accessor.Parent!;
        var getter = accessor.AccessorKind == PropertyAccessorKind.Get;
        accessor.Report(
            requirement,
            DiagnosticCode.AccessorReceiverShape_Kd,
            accessor.AccessorText,
            written.ToString(),
            at: written,
            evidence: [getter ? "ref/Self" : "uniq/Self"],
            related: [("property", property.NameKoto, $"{property.NameKoto.IdentifierName} is read through ref/Self and written through uniq/Self")]);
    }

    private void ResetPrerequisites()
    {
        this.caseBinders?.Clear();
        this.mismatches?.Clear();
        this.qualificationStops?.Clear();
        this.qualificationFailures?.Clear();
        this.ResetOriginRecords();
        this.originContracts?.Clear();
        this.ownedConversions?.Clear();
        this.objectErasureFailures?.Clear();
        this.unsupportedSpans?.Clear();
        this.objectErasures?.Clear();
        this.perCallSlots?.Clear();
        this.referenceSlotFacts?.Clear();
        this.callableConstraints?.Clear();
        this.referenceConstraints?.Clear();
        this.operatorOperands?.Clear();
        this.arithmeticConformanceFailures?.Clear();
        this.arithmeticSelectionFailures?.Clear();
        this.arithmeticDirectionFailures?.Clear();
        this.rangeIterationFailures?.Clear();
        this.writeTargets?.Clear();
        this.captureFailures?.Clear();
        this.captureRepeats?.Clear();
        this.arityFailures?.Clear();
        this.constructorAbsences?.Clear();
        this.invalidStaticOriginSlots?.Clear();
        this.phantomRawOrigins?.Clear();
        this.rejectedCandidates?.Clear();
        this.unboundSlots?.Clear();
        this.acquisitionPlaces?.Clear();
        this.ResetParameterShapes();
        this.duplicateDeclarations?.Clear();
        this.prerequisites.Clear();
        this.partPrerequisites.Clear();
        this.specificationLinks?.Clear();
        this.prerequisiteStore.Clear();
        this.derivedIssues.Clear();
        this.consulted.Clear();
        this.consultationStart = -1;
        this.consultationNode = null;
        this.missingExpectation = null;
    }

    // A lookup that misses a Name the language defines but the implementation does not yet provide meets an implementation
    // limit, not a missing Name (DIAGNOSTICS.md rule 3): a cataloged declaration without source (PLAN G4), or a Property
    // requirement whose combined identities or access path are not yet supported.
    private BindingFailure MissingFailure(Koto name, BindingScope scope, BindingFailure missing)
    {
        var limit = name switch
        {
            MemberAccessKoto member when this.requirementGroups.TryGetValue(member, out var group) && group.PropertyRequirement => true,
            MemberAccessKoto member => TypeSpelling(member.Right) is { } spelled && KimiLibraryCatalog.IsUnsourced(KimiLibraryContainer.Intrinsics, spelled) &&
                ReferenceEquals(this.TypeName(member.Left, scope, false)?.Declaration, this.Library.Intrinsics),
            _ => TypeSpelling(name) is { } spelled && KimiLibraryCatalog.IsUnsourced(KimiLibraryContainer.Root, spelled),
        };
        return limit ? BindingFailure.Unsupported : missing;
    }

    // A Type position names a Type: an inaccessible qualifier explains the miss, otherwise the Name is missing.
    private BoundType? FailMissingType(Koto name, BindingScope scope)
        => this.ReportUnavailableQualifier(name, scope) is { } qualifier
            ? this.CompleteDependent(name, qualifier)
            : this.Fail(name, this.MissingFailure(name, scope, BindingFailure.MissingType), true);

    // Called only after both value and Type lookup failed. A tentative Type path must not
    // publish access errors when a valid value path exists in the other namespace.
    // Returns the inaccessible qualifier it failed, which explains the failed lookup.
    private Koto? ReportUnavailableQualifier(Koto syntax, BindingScope scope)
    {
        if (syntax is GenericsKoto generic)
        {
            return this.ReportUnavailableQualifier(generic.Identifier!, scope);
        }

        if (syntax is not MemberAccessKoto member)
        {
            return null;
        }

        if (this.ReportUnavailableQualifier(member.Left, scope) is { } inner)
        {
            return inner;
        }

        if (this.TypeName(member.Left, scope, false) is not { } qualifier || !this.scopes.TryGetValue(qualifier.Declaration, out var members) ||
            TypeSpelling(member.Right) is not { } name || !members.Types.TryGetValue(name, out var first))
        {
            return null;
        }

        for (var candidate = first; candidate is not null; candidate = candidate.Next)
        {
            if (this.Accessible(candidate, scope))
            {
                return null;
            }
        }

        this.Fail(member, BindingFailure.Access);
        return member;
    }

    /// <summary>Rejects an object form, creation, cast or runtime test over a Type that opts out of ObjectPayload, naming the declaring Type (SPEC 8.7.3).</summary>
    private BoundType? FailObjectPayload(Koto use, BindingSymbol renounced)
    {
        (this.objectPayloadCauses ??= new(ReferenceEqualityComparer.Instance))[use] = renounced;
        return this.Fail(use, BindingFailure.NotObjectPayload);
    }

    private void FailConstraint(Koto use, Koto? diagnosticCause = null)
    {
        if (use.BindingFailure != BindingFailure.None)
        {
            return;
        }

        this.Fail(use, BindingFailure.InvalidConstraint);
        if (diagnosticCause is null && use is IsKoto clause)
        {
            diagnosticCause = this.FindMissingConformanceName(clause);
        }

        if (diagnosticCause is not null)
        {
            (this.constraintDiagnosticCauses ??= new(ReferenceEqualityComparer.Instance))[use] = diagnosticCause;
        }
    }

    private Koto? ConformanceDiagnosticCause(Koto owner)
        => owner is StructKoto or EnumKoto && owner.BindingFailure == BindingFailure.InvalidConstraint ?
            this.constraintDiagnosticCauses?.GetValueOrDefault(owner) : null;

    private Koto? FindConformanceDiagnosticCause(Koto owner, BoundConstraint fact)
    {
        if (fact.Kind == ConstraintKind.Error && owner is FunctionKoto function)
        {
            // A function's environment that is invalid only through its one Callable clause rests on that clause's failed part.
            for (var i = 0; i < function.TypeConstraints.Count; i++)
            {
                if (function.TypeConstraints[i] is IsKoto { Right: { } requirement } clause && ReferenceEquals(clause.BoundConstraint, fact) &&
                    this.constraintDiagnosticCauses?.TryGetValue(KotoHelper.UnwrapParentheses(requirement), out var part) == true)
                {
                    return part;
                }
            }
        }

        if (fact.Kind == ConstraintKind.Error && owner is DeclarationContainerKoto container)
        {
            for (var i = 0; i < container.ConstraintNodes.Count; i++)
            {
                var clause = container.ConstraintNodes[i];
                if (ReferenceEquals(clause.BoundConstraint, fact) && this.FindMissingConformanceName(clause) is { } cause)
                {
                    return cause;
                }
            }
        }

        return null;
    }

    // SPEC 23.1.2: the first part of a Callable requirement's signature that failed on its own: an unsupported form or a missing
    // name, whose record explains the requirement.
    private Koto? FailedSignaturePart(Koto requirement)
    {
        if (KotoHelper.UnwrapParentheses(requirement) is not GenericsKoto { TypeArguments.Count: > 0 } generic)
        {
            return null;
        }

        return (this.failedSignaturePartVisitor ??= new()).Find(generic.TypeArguments[^1]);
    }

    // SPEC 8.10, 23.1.2: a call through F whose only Callable clause failed for a part of its signature rests on that clause.
    private Koto? FailedCallableClause(BoundType callee, BindingScope scope)
    {
        var owner = callee.Kind == BoundTypeKind.Semantics && callee.Components.Count == 1 ? callee.Components[0] : callee;
        if (owner.Kind != BoundTypeKind.Parameter)
        {
            return null;
        }

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Owner is not FunctionKoto function)
            {
                continue;
            }

            for (var i = 0; i < function.TypeConstraints.Count; i++)
            {
                if (function.TypeConstraints[i] is IsKoto { Left: { } subject, Right: { } requirement } clause && clause.BoundConstraint?.Kind == ConstraintKind.Error &&
                    ReferenceEquals(subject.BoundType, owner) && this.constraintDiagnosticCauses?.ContainsKey(KotoHelper.UnwrapParentheses(requirement)) == true)
                {
                    return KotoHelper.UnwrapParentheses(requirement);
                }
            }
        }

        return null;
    }

    private Koto? FindMissingConformanceName(IsKoto clause)
    {
        if (clause.Parent is not (StructKoto or EnumKoto) || !IsSelfConstraint(clause) ||
            clause.BoundConstraint is null)
        {
            return null;
        }

        var visitor = this.missingConstraintNameVisitor ??= new();
        visitor.Visit(clause.Right);
        var cause = visitor.Cause;
        visitor.Cause = null;
        return cause;
    }

    private sealed class FailedSignaturePartVisitor : KotoVisitor
    {
        private Koto? part;
        private bool invalidState;

        public override void Visit(Koto node)
        {
            if (this.part is not null)
            {
                return;
            }

            if (this.invalidState ? node.BindingState == BindingState.Invalid : node.BindingFailure is BindingFailure.Unsupported or BindingFailure.MissingName or BindingFailure.MissingType)
            {
                this.part = node;
                return;
            }

            node.VisitChildren(this);
        }

        internal Koto? Find(Koto node, bool invalidState = false)
        {
            this.invalidState = invalidState;
            this.Visit(node);
            var found = this.part;
            this.part = null;
            return found;
        }
    }

    private sealed class MissingConstraintNameVisitor : KotoVisitor
    {
        internal Koto? Cause { get; set; }

        public override void Visit(Koto node)
        {
            if (this.Cause is not null)
            {
                return;
            }

            if (node.BoundSymbol is null && node.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType)
            {
                this.Cause = node;
                return;
            }

            node.VisitChildren(this);
        }
    }
}
