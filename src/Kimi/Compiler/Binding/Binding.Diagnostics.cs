// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.2.2, 10.7: no ordering is defined between an erasure and a direct match at one argument.
    private const string ErasureAmbiguityNote = "At one argument, one candidate erases the argument to a common Function Type and another takes it directly; these adaptations are incomparable (SPEC 10.2.2, 10.7)";

    private const string SharedObjectAuthorityNote = "rc and arc provide shared payload access only; even a strong count of one does not grant objuniq or uniq/Self authority";

    private readonly List<ProblemFact> acquisitionFacts = []; // FailAcquisition's facts, a kept scratch (AcquisitionFacts).

    private MissingConstraintNameVisitor? missingConstraintNameVisitor;

    private FailedSignaturePartVisitor? failedSignaturePartVisitor;

    // The expression being bound without its expected Type because the syntax that supplies it failed.
    private (Koto Expression, Koto Cause)? missingExpectation;

    internal static DiagnosticCode OriginRelationCode(OriginRelationFact relation)
        => relation.Refuted ? DiagnosticCode.UnsatisfiedOriginRelation_Kd : DiagnosticCode.UnprovenOriginRelation_Kd;

    /// <summary>Forms the Transfer candidate of a bare Place (SPEC 23.1.3): <c>@move</c> after the Place, or <c>(*p)@move</c> around a dereference.</summary>
    /// <param name="store">The store the record goes to (Compilation.StoreOf), which locates the edits.</param>
    /// <param name="node">The reporting node, in the Place's document.</param>
    /// <param name="place">The Place.</param>
    /// <param name="judgment">The Take judgment; never refuted.</param>
    /// <returns>The candidate.</returns>
    internal static DiagnosticRepairFact[] TransferRepair(DiagnosticCollection store, Koto node, Koto place, AcquisitionJudgment judgment)
    {
        var document = node.CodeContext.SourceDocument;
        DiagnosticEditFact[] edits = place is DereferenceKoto
            ? [store.Edit(new(place.Span.Start, 0), "(", document), store.Edit(new(place.Span.End, 0), ")@move", document)]
            : [store.Edit(new(place.Span.End, 0), "@move", document)];
        var verified = judgment == AcquisitionJudgment.Verified ? RepairConditionSet.Take : RepairConditionSet.None;
        var required = RepairConditionSet.UsageLegality | (judgment == AcquisitionJudgment.Required ? RepairConditionSet.Take : RepairConditionSet.None);
        return [new(RepairKind.Transfer, [place.ToString(), TransferDestination(place)], edits, verified, required)];
    }

    // The destination a Place is acquired for, as the Transfer title names it.
    internal static string TransferDestination(Koto place)
        => Destination(place) switch
        {
            InvocationKoto call when ReferenceEquals(KotoHelper.UnwrapParentheses(call.Method), KotoHelper.UnwrapParentheses(place)) => "its call",
            InvocationKoto call => call.Method is MemberAccessKoto member ? member.Right.ToString() : call.Method.ToString(),
            VariableKoto variable => $"the binding {variable.NameKoto.IdentifierName}",
            ReturnKoto => "the result",
            ArrayLiteralKoto or TupleLiteralKoto or DictionaryLiteralKoto => "the element",
            _ => "its destination",
        };

    // SPEC 15.6.1, 23.1: the Reason facts and related locations of an Origin relation record, shared by every phase that reports
    // one (Binding's ReportOriginRelation, A5's through the publication's Relation row); `source` is `fit` for a fit, `declared` for a
    // relation clause and `wellFormed` for a Type occurrence. `Spans` are the related syntax without a node of its own: a shorter end
    // that is an anonymous function's call result is related at its header, from `func` through the parameter list (DIAGNOSTICS).
    internal (object[] Evidence, (string Role, Koto At, string? Label)[]? Related, (string Role, Koto In, SourceSpan Span, string? Label)[]? Spans) OriginRelationFacts(OriginRelationFact relation, string source)
    {
        // SPEC 15.6.5: a local initialized by a Borrow shows that Borrow, which is also the related location. An input's well-formedness
        // fails over an Origin that another input of the call supplied, so its Borrow is found in that call.
        var borrow = this.BorrowSource(relation.At, relation.Supplier) ??
            (relation.WellFormed && EnclosingCall(relation.At) is { } call ? this.BorrowSource(call, relation.Supplier) : null);
        // A clause of a Field or Case names its Type's own slots as written there, without `self.`; a Type's clause substituted at
        // a use in a body names the substituted Origins as that body does.
        var typeLevel = relation.Clause is not null && !relation.Substituted && TypeLevelClause(relation.At);
        var longer = OriginDisplay(relation.Longer, borrow, typeLevel);
        // SPEC 15.6.1, 23.1: a shorter end that an equality fixed to another argument's Borrow shows that Borrow too.
        var shorterBorrow = relation.FixedBy is { } fixedBy && relation.Shorter.Kind is OriginKind.Projection or OriginKind.Anchor ? this.BorrowSource(fixedBy, relation.Shorter) : null;
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
        if (relation.Clause is { } clause)
        {
            // SPEC 23.1: a declared relation relates its clause with the role `relation`.
            related = related is null ? [("relation", clause, null)] : [.. related, ("relation", clause, null)];
        }

        if (relation.FixedBy is { } input)
        {
            // SPEC 15.6.1: the input whose equality made a call's fresh Origin equal to a fixed one is related with the role `relation`.
            related = related is null ? [("relation", input, null)] : [.. related, ("relation", input, null)];
        }

        (string Role, Koto In, SourceSpan Span, string? Label)[]? spans = relation.Shorter is { Kind: OriginKind.Projection, Slot: CallResultSlot, Binder: FunctionKoto { IsAnonymous: true } closure }
            ? [("origin", closure, SourceSpan.FromBounds(closure.Span.Start, Math.Max(closure.Span.Start, closure.HeaderEnd)), null)] : null;
        if (source != "fit")
        {
            // SPEC 23.1: only a fit names its destination Type; a declared relation relates its clause instead, and well-formedness
            // its Type occurrence, which is the primary location.
            return ([operation, longer, shorter, source], related, spans);
        }

        // SPEC 23.1: the destination shows only the Origin at the failed position, also below another borrow layer; a borrow, omitted
        // or closure end is never written as an Origin expression.
        var destination = relation.Destination is not { } type ? $"during {shorter.Text}"
            : DiagnosticTypeName(type, shorter.Kind == "expression" ? relation.Shorter : null, shorter.Text);
        return ([operation, longer, shorter, source, destination], related, spans);
    }

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
        => origin.Occurrence ?? OmittedInput(origin) ?? (origin is { Kind: OriginKind.Inference, Binder: { } occurrence } && !IsRegionOwner(occurrence) && !origin.IsOpenStandIn ? occurrence : null);

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
        while (actual.SafeReferent is { } referent)
        {
            actual = referent;
        }

        while (expected.SafeReferent is { } referent)
        {
            expected = referent;
        }

        return actual.Symbol?.LibraryDeclaration is KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange &&
            expected.Symbol?.LibraryDeclaration is KimiDeclarationId.Range or KimiDeclarationId.ClosedRange or KimiDeclarationId.ResolvedRange && actual.Symbol != expected.Symbol;
    }

    // SPEC 15.6.1, 23.1: an Origin relation record at the value that supplies the longer end, with both ends as Origin
    // displays, the relation's source and the destination Type, in which only the Origin at the failed position is shown; a
    // borrow end is related at its syntax; the SPEC 15.4.3 elision Note follows an omitted result Origin.
    // SPEC 15.6.1, 23.1: the Reason names the comparison, the member, the relation and its two ends, rigid symbols of the comparison
    // displayed as the required contract writes them; an omitted end is related at its Type occurrence.
    private static void ReportOriginContract(Koto node, DiagnosticCollection? store, in BindingProblem problem, OriginContractFact contract)
    {
        if (contract.Required is { } original)
        {
            var relation = contract.Equality ? "==" : "outlives";
            node.ReportIn(
                store,
                problem.Requirement,
                problem.Code,
                evidence: ["implementation", contract.Member, relation, OriginDisplay(contract.Longer, null), OriginDisplay(contract.Shorter, null)],
                related: [("requirement", original, "the original implementation contract")],
                at: contract.At,
                condition: problem.Condition);
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
            node.ReportIn(
                store,
                problem.Requirement,
                problem.Code,
                note: Note,
                evidence: ["conversion", contract.Member, contract.Equality ? "==" : "outlives", longer, shorter],
                related: related,
                relatedSpans: [("origin", closure, header, null)],
                at: contract.At,
                condition: problem.Condition);
            return;
        }

        node.ReportIn(
            store,
            problem.Requirement,
            problem.Code,
            evidence: ["conversion", contract.Member, contract.Equality ? "==" : "outlives", longer, shorter],
            related: related,
            at: contract.At,
            condition: problem.Condition);
    }

    // The capture entry that names an environment binding, when the closure has a capture list (CaptureEntryIndex).
    private static CaptureKoto? CaptureEntryOf(FunctionKoto closure, string name)
        => CaptureEntryIndex(closure, name) is { } entry ? closure.Captures![entry] : null;

    // The index of the written entry that names an environment binding; null when the closure has no such entry or no capture list.
    private static int? CaptureEntryIndex(FunctionKoto closure, string name)
    {
        if (closure.Captures is { } captures)
        {
            for (var i = 0; i < captures.Length; i++)
            {
                if (captures[i].Name == name)
                {
                    return i;
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
                if (capture.Environment.Name == name && capture.Source is { } source && InLaterDefault(closure, source))
                {
                    return source;
                }
            }
        }

        return null;
    }

    // A missing-information record (a missing Name or Type, or Unsupported_Kd): the one test of a Constraint record's diagnostic cause.
    private static bool IsMissingInformation(Koto? node)
        => node?.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported;

    // SPEC 9.7.1: a Type or container named with the wrong number of its own Type arguments, or without the Type arguments of the generic
    // container that declares it: that declaration (Related) and its declared and written counts (Count, in that order).
    private BoundType? RecordArity(Koto node, BindingSymbol declaration, int declared, int written)
    {
        var subject = SourceRef.Of(node);
        return this.Record(
            node,
            BindingFailure.InvalidTypeFormation,
            [new(ProblemFactKind.Related, this.FactSite(declaration.Declaration), 0), new(ProblemFactKind.Count, subject, declared), new(ProblemFactKind.Count, subject, written)]);
    }

    private void ReportArity(Koto node, DiagnosticCollection? store, int problem, in BindingProblem record)
    {
        var facts = this.report.Facts(problem);
        node.ReportIn(store, record.Requirement, record.Code, evidence: [(int)facts[1].Value, (int)facts[2].Value], related: [("declaration", this.compilation.SyntaxOf(facts[0].Site), null)]);
    }

    // An Origin relation record of `node` under the problem's code, requirement and condition, into Core's store decision for the node.
    private void ReportOriginRelation(Koto node, DiagnosticCollection? store, in BindingProblem problem, OriginRelationFact relation, string? note)
    {
        var (evidence, related, spans) = this.OriginRelationFacts(relation, relation.Clause is not null ? "declared" : relation.WellFormed ? "wellFormed" : "fit");
        // A closure end's record at a block body is at the Borrow that supplied it.
        var at = spans is not null && relation.At is CodeBlockKoto ? this.BorrowSource(relation.At, relation.Supplier) ?? relation.At : relation.At;
        node.ReportIn(store, problem.Requirement, problem.Code, note: note, evidence: evidence, related: related, relatedSpans: spans, at: at, condition: problem.Condition);
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
    /// <param name="failure">The failure of the comparison that decided, or <see cref="ComparisonFailure.Shape"/> for a description.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailMismatch(Koto node, Koto at, BoundType actual, BoundType expected, in ComparisonFailure failure)
        => this.RecordMismatch(node, at, actual, expected, failure);

    // Keep semantic identities even when their short names agree. Format only at publication, never during Binding. A fit whose
    // structural part holds failed only in its Origin part, which is an Origin relation, never a Type mismatch (SPEC 15.6.1).
    // The one record of a failed comparison, by the Kind of the failure its walk found (no reader walks it again): a condition of a
    // whole-contract comparison whose structure holds is one UnprovenOriginContract_Kd at the value (SPEC 10.7, 15.6.1); a plain Origin
    // position is a row of its body that A5 judges (R7-O1: CheckTypeUse formed it, so no record here); a Shape or a structure that
    // differs is a Type mismatch.
    // A failed comparison whose structure holds always gives its fact, so one without a fact is a broken contract (design-r2 §2, D2-N4).
    private BoundType? RecordMismatch(Koto node, Koto at, MismatchSide actual, MismatchSide expected, in ComparisonFailure failure)
    {
        // SPEC 15.2.3, 7.7.4: the Owned condition of a common Function conversion is a Constraint failure, never a Type mismatch; the
        // erasure recorded it at this value (DECISIONS BU-107), so this reader only checks that an outcome exists.
        if (expected.Type is { Kind: BoundTypeKind.Function } owner &&
            actual.Type is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } value &&
            this.FailOwnedConversion(value, owner, at, node))
        {
            return null;
        }

        // SPEC 8.2.3, 15.6.1 (F's C2 rulings 307 (2), 315): the record reads the pair its walk compared, as the walk's intake formed it
        // and published it with its facts (comparisonRoot), so identity decides the structure and an Origin difference is the Origin
        // relation's record; a failure whose facts no recording walk published is read as written.
        var (walkedActual, walkedExpected) = this.comparisonFacts is [var first, ..] && first.Same(failure) && this.comparisonRoot is (BoundType a, BoundType e)
            ? (a, e) : (actual.Type, expected.Type);
        if (TyCtxt.StorageMatches(walkedExpected, walkedActual) && failure.Kind != ComparisonFailureKind.Shape)
        {
            return failure.Kind == ComparisonFailureKind.None ? this.Fail(node, BindingFailure.Internal)
                : this.RecordComparisonFacts(node, at, walkedActual!, walkedExpected!, failure);
        }

        return this.RecordTypeMismatch(node, at, actual, expected);
    }

    // SPEC 15.6.1, 23.1.2 (design-r2 Amendment 1): every fact of a failed comparison whose structure holds, each an Origin record of the
    // node (RecordOrigin): each Function component's failed condition is one UnprovenOriginContract_Kd at the value (ContractFact); a limit
    // is the located Unsupported_Kd. The plain positions are rows A5 judges (R7-O1), formed by the recording caller's use-site fit
    // (CheckTypeUse, FitsValueAt): a failure that is a plain position, at a node that fit left unfailed, reaches here on a path that
    // bypassed it, a broken contract, the node's Internal failure before any record. The walk's facts are read only when their first is
    // `failure` (another fit may have walked since); otherwise `failure` is the one fact.
    private BoundType? RecordComparisonFacts(Koto node, Koto at, BoundType actual, BoundType expected, in ComparisonFailure failure)
    {
        if (failure.Kind == ComparisonFailureKind.Limit)
        {
            return this.Fail(node, BindingFailure.Unsupported);
        }

        if (!failure.IsCondition && this.RecordedFailure(node) == BindingFailure.None)
        {
            this.Fail(node, BindingFailure.Internal); // Before any RecordOrigin, so the node keeps its Internal slot.
        }

        var facts = this.comparisonFacts;
        var count = facts.Count != 0 && facts[0].Same(failure) ? facts.Count : 1;
        for (var i = 0; i < count; i++)
        {
            var fact = i == 0 ? failure : facts[i];
            if (fact.IsCondition)
            {
                this.RecordOrigin(node, new(null, this.ContractFact(at, fact, node)));
            }
        }

        return null;
    }

    /// <summary>Records a write whose target's path denies it (SPEC 3.4, 15.1.5) with the target (`Related` role 0, "target"), beside any
    /// other failure of the write (SPEC 23.1.2: the check reads the target's path, never the value or the route); an unsupported path is
    /// located only.</summary>
    /// <param name="node">The write.</param>
    /// <param name="target">The written target.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailWrite(Koto node, Koto target)
        => this.RecordWrite(node, this.AccessFailure(target, out _), target);

    private BoundType? FailObjectAuthority(Koto node, Koto target)
        => this.RecordWrite(node, BindingFailure.SharedPathAccess, target);

    // The one recorder of a denied write (AccessFailure's code, or the object authority's): the target related; a write the implementation
    // does not support yet is the located limit (SPEC 23.1.1), at the write as before.
    private BoundType? RecordWrite(Koto node, BindingFailure failure, Koto target)
    {
        if (failure == BindingFailure.Unsupported)
        {
            this.RecordLocated(node, node.Span);
            return null;
        }

        return this.RecordNumbered(node, failure, [new(ProblemFactKind.Related, this.FactSite(target), 0)]);
    }

    // The one recorder of a bare Place that needs its acquisition spelled (TransferRequired or ExclusiveBorrowRequired; SPEC 15.1.5),
    // numbered: the Place as `Related` role 0, "place"; a Reason: bit 0 whether an exclusive borrow is of an object handle, above it a
    // transfer's Take judgment (O-A1); and, for a transfer of a Place whose Type has a pair layer, a second Reason: the mask of the cases
    // its bare acquisition moves in (R7-E4 ruling 6, D1), the judgments the adapter once made at publication, made here at the failure.
    private BoundType? FailAcquisition(Koto node, BindingFailure failure, Koto place, bool @object = false, bool unresolved = false)
    {
        this.acquisitionFacts.Clear();
        this.AcquisitionFacts(this.acquisitionFacts, node, failure, place, @object);
        return this.RecordNumbered(node, failure, CollectionsMarshal.AsSpan(this.acquisitionFacts), unresolved);
    }

    // The facts of a bare Place's acquisition record, appended to `facts` (FailAcquisition's scratch, or the selection's when a call's
    // selection records it through RecordSelection's one guard).
    // An implicit exclusive acquisition the lending rule denies (OwnedLending, ExclusiveAcquisitionDenial: AccessFailure's codes) records
    // its written twin's form, RecordWrite's: the Place alone, as role 0, "target" (SPEC 7.3.3); a limit (BU-15's construction receiver,
    // PLAN G762's object pair layer) is located at the Place, as RecordLocated's (SPEC 23.1.1). The denials are SharedPathAccess and
    // InvalidAssignment; any kind but a spelling, a denial or a limit is a broken contract (InternalInvariant_Kd).
    private void AcquisitionFacts(List<ProblemFact> facts, Koto node, BindingFailure failure, Koto place, bool @object)
    {
        if (failure == BindingFailure.Unsupported)
        {
            facts.Add(LocatedFact(node, place.Span));
            return;
        }

        var transfer = failure == BindingFailure.TransferRequired;
        var judgment = transfer ? this.TakeJudgment(place) : AcquisitionJudgment.Verified;
        facts.Add(new(ProblemFactKind.Related, this.FactSite(place), 0));
        if (failure is BindingFailure.SharedPathAccess or BindingFailure.InvalidAssignment)
        {
            return;
        }

        if (failure is not (BindingFailure.TransferRequired or BindingFailure.ExclusiveBorrowRequired))
        {
            throw new InvalidOperationException($"An acquisition record is a spelling, a lending denial or a limit, not {failure} (InternalInvariant; R7-E4 E4a-3).");
        }

        facts.Add(new(ProblemFactKind.Reason, SourceRef.Of(node), (@object ? 1L : 0L) | ((long)judgment << 1)));
        if (transfer && this.AcquisitionCaseMask(place) is { } cases)
        {
            facts.Add(new(ProblemFactKind.Reason, SourceRef.Of(node), (long)cases));
        }
    }

    // SPEC 3.5, 15.1.5, 23.1.3: the acquisition a bare Place needs spelled, located at the Place with the candidate that writes it, from
    // the record's facts (FailAcquisition): an exclusive borrow is verified exclusively writable (BorrowablePlace held), a transfer's Take
    // judgment and moving cases were made at the failure.
    private void ReportAcquisition(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        if (store is null)
        {
            return;
        }

        var (requirement, code) = (problem.Requirement, problem.Code);
        var place = this.compilation.SyntaxOf(facts[0].Site);
        var text = place.ToString();
        if (code == DiagnosticCode.ExclusiveBorrowRequired_Kd)
        {
            var spelling = (facts[1].Value & 1) != 0 ? "@objuniq" : "@uniq";
            var edit = store.Edit(new(place.Span.End, 0), spelling, node.CodeContext.SourceDocument);
            node.ReportIn(store, requirement, code, at: place, evidence: [text], repairs: [new(RepairKind.BorrowExclusively, [text, spelling], [edit], RepairConditionSet.ExclusiveAccess, RepairConditionSet.UsageLegality)], condition: problem.Condition);
            return;
        }

        // SPEC 8.15, 23.1.2 (R6-check H1): a pair-layer Place carries the cases its bare acquisition moves in, as the `case` fact. A
        // default's move of a prepared argument (SPEC 7.2.3) names no target and offers no transfer, since none is valid in a default.
        var judgment = (AcquisitionJudgment)(facts[1].Value >> 1);
        var single = false;
        var found = facts.Length > 2 ? this.AcquisitionCases(place, (ulong)facts[2].Value, out single) : null;
        var prepared = code == DiagnosticCode.DefaultArgumentMove_Kd;
        node.ReportIn(
            store,
            requirement,
            code,
            note: CaseNote(null, found, single),
            at: place,
            evidence: CaseEvidence(code, prepared ? null : text, found),
            related: WithCaseDeclarations(null, this.namedCaseBinders, found),
            repairs: prepared || judgment == AcquisitionJudgment.Refuted ? null : TransferRepair(store, node, place, judgment),
            condition: problem.Condition);
    }

    // Reports a write failure at its target; an assignment names the target. An
    // Exclusive call of a closure (SPEC 7.7.3) borrows the callee exclusively, so the callee is the written target.
    private void ReportWrite(Koto node, DiagnosticCollection? store, Koto target, DiagnosticRequirement requirement, DiagnosticCode code, ushort condition)
    {
        var call = node is InvocationKoto invocation && ReferenceEquals(invocation.Method, target);
        var callee = target.BoundType is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } reference ? reference.Components[0] : target.BoundType;
        var objectCallee = call && ((target.BoundType is { Kind: BoundTypeKind.Semantics } layer && IsObjectSemantics(layer.Semantics)) ||
            KotoHelper.UnwrapParentheses(target) is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow });
        var plan = BoundClosure.Of(callee);
        var consuming = call && target.BoundType is { } calleeType && this.TryCallable(calleeType, this.ConstraintScope(node), out _, out var calleeReceiver) && calleeReceiver == SemanticsKind.Owner;
        var callNote = !call ? null : consuming ? objectCallee
            ? "The call is Consuming (SPEC 7.7.3): it may move values out of the callee's environment, and an object payload offers no Take (SPEC 13.5.5)"
            : "The call is Consuming (SPEC 7.7.3): it may move values out of the callee's environment, which a reference cannot supply, since a callable value is no read Type (SPEC 3.5.3, 7.3)"
            : plan is { ExclusiveReborrow: true, ExclusiveUse: { } use }
            ? $"The call is Exclusive (SPEC 7.7.3): it borrows the callee exclusively, because the callee Reborrows the captured exclusive reference {use} exclusively"
            : "The call is Exclusive (SPEC 7.7.3): it borrows the callee exclusively, because a call of the callee may change its environment, or use it or what it reaches with exclusive access";
        if (code != DiagnosticCode.InvalidAssignment_Kd)
        {
            var handle = KotoHelper.UnwrapParentheses(target) is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } followed ? followed.Left.BoundType : target.BoundType;
            var authority = handle?.HandleMode is { PayloadAuthority: LoanRequirement.Ref } ? SharedObjectAuthorityNote : null;
            var note = code != DiagnosticCode.SharedPathAccess_Kd ? null : objectCallee ? authority is null ? callNote : $"{callNote}. {authority}" : authority ?? this.SharedIndexNote(target);
            node.ReportIn(store, requirement, code, note: note, at: target, evidence: code == DiagnosticCode.SharedPathAccess_Kd ? [target.ToString()] : null, condition: condition);
            return;
        }

        var root = KotoHelper.UnwrapParentheses(target);
        while (root is MemberAccessKoto or IndexKoto)
        {
            root = KotoHelper.UnwrapParentheses(root is MemberAccessKoto member ? member.Left : ((IndexKoto)root).Left);
        }

        node.ReportIn(store, requirement, code, note: callNote, at: target, evidence: [target.ToString()], condition: condition);
    }

    // SPEC 4.7.1, 8.9.1: an ambiguous index key's record (RecordKeyAmbiguity), at the index expression, each conformance its selection
    // weighed related by the instance it stands for and the recorded members counted.
    private void ReportKeyAmbiguity(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var related = new (string Role, Koto At, string? Label)[facts.Length / 2];
        for (var i = 0; i < related.Length; i++)
        {
            related[i] = ("conformance", this.compilation.SyntaxOf(facts[2 * i].Site), DiagnosticTypeName(this.report.Value(facts[(2 * i) + 1])));
        }

        node.ReportIn(store, problem.Requirement, problem.Code, evidence: [related.Length], related: related, condition: problem.Condition);
    }

    // SPEC 4.7.1, 8.3.3: a user index publishes its element exclusively only through indexUniq. When the receiver's Type
    // lacks it, the shared layer is the element published by index, and the Note names why indexUniq is absent.
    private string? SharedIndexNote(Koto target)
    {
        if (KotoHelper.UnwrapParentheses(target) is not IndexKoto index || this.HasExclusiveIndexer(index) || !this.IsUserIndex(index) ||
            this.Library.UniqIndexable is not { } uniqIndexable)
        {
            return null;
        }

        var core = index.Left.BoundType;
        while (core?.SafeReferent is { } referent)
        {
            core = referent;
        }

        if (core?.Symbol is { Declaration: StructKoto or EnumKoto } owner)
        {
            // The check's fact alone (uniqIndexing): the conformance it selected, whose condition does not hold here; else the declaration's
            // UniqIndexable identities (a query by declaration: no judgment), with the key's reference that selected none (Key, SPEC 4.7.1).
            var name = DiagnosticTypeName(core);
            var fact = this.uniqIndexing.GetValueOrDefault(index);
            if (fact.Conformance is not null)
            {
                return $"{name} conforms to UniqIndexable only under a condition that does not hold here, so its element is published by index alone, which grants Read only";
            }

            var identities = string.Empty;
            foreach (var identity in new SelectionMembers(this, core, uniqIndexable, null, owner.Scope, QuerySite.AfterPass))
            {
                identities = identities.Length == 0 ? DiagnosticTypeName(identity.Contract.Type!) : $"{identities}, {DiagnosticTypeName(identity.Contract.Type!)}";
            }

            return identities.Length == 0 ? $"{name} has no UniqIndexable conformance, so its element is published by index alone, which grants Read only" :
                fact.Key is { } key ? $"{name} conforms to {identities} but not to {DiagnosticTypeName(key)}, so its element is published by index alone, which grants Read only" :
                $"{name} conforms to {identities}, none of which is selected here, so its element is published by index alone, which grants Read only";
        }

        return core is { Kind: BoundTypeKind.Parameter } ? $"{DiagnosticTypeName(core)} is not required to be UniqIndexable here, so its element is published by index alone, which grants Read only" : null;
    }

    // SPEC 11.3: an accessor receiver has the shape of its operation. The report is located at the written receiver Type and
    // relates the Property header; the written Type is a bounded display value, as the message argument of a transfer is.
    private void ReportAccessorReceiverShape(PropertyAccessorKoto accessor, DiagnosticCollection? store, Koto written, DiagnosticRequirement requirement)
    {
        var property = (PropertyKoto)accessor.Parent!;
        var getter = accessor.AccessorKind == PropertyAccessorKind.Get;
        accessor.ReportIn(
            store,
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
        this.invalidations = 0;
        this.caseBinders?.Clear();
        this.report.Clear();
        this.nameSteps.Clear();
        this.erasureOutcomes?.Clear();
        this.ResetParameterShapes();
        this.duplicateOrder.Clear();
        this.specificationLinks?.Clear();
        this.derivedUses?.Clear();
        this.prerequisiteStore.Clear();
        this.derivedIssues.Clear();
        this.consulted.Clear();
        this.consultationStart = -1;
        this.consultationNode = null;
        this.consultationHeld = false;
        this.missingExpectation = null;
    }

    // A lookup that misses a Name the language defines but the implementation does not yet provide meets an implementation
    // limit, not a missing Name (DIAGNOSTICS.md rule 3): a Property requirement whose combined identities or access path are not yet
    // supported. A declaration this implementation does not support is written `#Unsupported` and never missed (UnsupportedUses).
    private BindingFailure MissingFailure(Koto name, BindingFailure missing)
        => name is MemberAccessKoto member && this.requirementGroups.TryGetValue(member, out var group) && group.PropertyRequirement ? BindingFailure.Unsupported : missing;

    // A Name that no lookup selects, at a Type position (MissingType) or as a value member (MissingName): an inaccessible qualifier
    // explains the miss, otherwise it is the resolver's miss (FailUnselected).
    private BoundType? FailMissing(Koto name, BindingScope scope, BindingFailure missing)
        => this.ReportUnavailableQualifier(name, scope) is { } qualifier
            ? this.CompleteDependent(name, qualifier)
            : this.FailUnselected(name, this.MissingFailure(name, missing));

    // Called only after both value and Type lookup failed. A tentative Type path must not
    // publish access errors when a valid value path exists in the other namespace.
    // Returns the inaccessible qualifier it failed, which explains the failed lookup; binds the qualifier of an inaccessible or a missing member.
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

        if (this.TypeName(member.Left, scope) is not { } qualifier || !this.scopes.TryGetValue(qualifier.Declaration, out var members) ||
            TypeSpelling(member.Right) is not { } name)
        {
            return null;
        }

        var first = MemberTypes(members, name);
        var missing = first is null;

        for (var candidate = first; candidate is not null; candidate = candidate.Next)
        {
            if (this.Accessible(candidate, scope))
            {
                return null;
            }
        }

        // An inaccessible member is still the qualifier's nested declaration (SPEC 9.4), and a missing one selects no other role, so a
        // qualifier no form has bound (a projection base is bound by its projection) is bound in its container role, as
        // BindContainerReference binds it for an accessible member, its own failures included (SPEC 9.7.1, 23.1.2; PLAN G250, G481).
        // Only a group or a struct declares nested Containers (SPEC 6.1's table; an enum has none by 6.5.1, a Contract only
        // requirements, associated Types and parents), so no other qualifier has that role.
        if (member.Left.BindingState == BindingState.Unvisited && qualifier.Declaration is GroupKoto { IsRoot: false } or StructKoto)
        {
            member.Left.BoundSymbol = qualifier;
            this.BindContainerQualifier(member.Left, qualifier, scope, this.TypeContext(member.Left, scope));
        }

        if (missing)
        {
            return null;
        }

        this.RecordAccess(member, first!, null); // SPEC 9.4: every candidate is inaccessible; the first the walk met is related.
        return member;
    }

    /// <summary>Rejects an object form, creation, cast or runtime test over a Type that opts out of ObjectPayload (SPEC 8.7.3), relating the
    /// declaration that opts out, its own or an inherited base's (`Related` role 0), whose name the record shows.</summary>
    private BoundType? FailObjectPayload(Koto use, BindingSymbol renounced)
        => this.Record(use, BindingFailure.NotObjectPayload, [new(ProblemFactKind.Related, this.FactSite(renounced.Declaration), 0)]);

    [Transitional("R7", Reason = "Records only at a node without a failure, so the node keeps its first failure until R7-E5's per-problem inputs (the owner's O-470 (a); R7-E2 E2-5's O2, the Front's C4): RequireConstraint's Error proof at a node that already failed, which would otherwise publish a dependent record (a clause's own Refuted conformance) or an internal error; InheritVirtualConstraints and BindArrayFill, where no program records otherwise (a repeat records the same key). Constraints.Requirements' cause-less Fail keeps the requirement's own Type record (BindType's, at the node).")]
    private void FailConstraint(Koto use, Koto? diagnosticCause = null)
    {
        if (this.RecordedFailure(use) != BindingFailure.None)
        {
            return;
        }

        // SPEC 23.1.1, 23.1.2 (rule W; R7-C3 b, F's ruling 5): a cause whose failure rests on a part that is missing information (a
        // conformance on its unsupported specification, RestOnSpecification) carries that part's record, which this record then rests on.
        if (!IsMissingInformation(diagnosticCause) && diagnosticCause is not null && this.partPrerequisites.TryGetValue(diagnosticCause, out var part) &&
            IsMissingInformation(part))
        {
            diagnosticCause = part;
        }

        if (!IsMissingInformation(diagnosticCause) && use is IsKoto clause && this.FindMissingConformanceName(clause) is { } missing)
        {
            diagnosticCause = missing; // What the outcome rests on is consulted; the record explains itself by missing information.
        }

        // SPEC 8.3, 23.1.2: an invalid Constraint, with the part its check named as the cause it rests on (a Cause fact, Value -1: a Binding
        // cause at the site, never shown); a cause whose own record is missing information makes this record derived from that one
        // (ReportConstraint). Later validators still see an invalid node. The guard above leaves RecordConstraint's read always passing.
        this.RecordConstraint(use, ConstraintProof.Error, diagnosticCause is null ? [] : [new(ProblemFactKind.Cause, this.FactSite(diagnosticCause), -1)]);
    }

    // The part a node's InvalidConstraint problem names as its cause, or null.
    private Koto? ConstraintCause(Koto node)
    {
        var problem = this.report.Count == 0 ? -1 : this.report.Find(SourceRef.Written(node), DiagnosticRequirement.Binding(BindingFailure.InvalidConstraint), 0);
        return problem >= 0 && this.FactOf(problem, ProblemFactKind.Cause) is { } cause ? this.compilation.SyntaxOf(cause.Site) : null;
    }

    // The adapter's InvalidConstraint row (F's C2 ruling 306): derived from a cause that is missing information, else direct; a
    // declaration's premise error relates the premises that linked it (SPEC 8.2.3), read from their published Failed state
    // (FailedPremiseClauses), never proved again.
    private void ReportConstraint(Koto node, DiagnosticCollection? store, DiagnosticRequirement requirement, DiagnosticCode code, ushort condition, Koto? cause)
    {
        if (cause is not null && IsMissingInformation(cause))
        {
            node.ReportDerivedIn(store, requirement, [cause.KeyIn(this.compilation.StoreOf(cause), DiagnosticRequirement.Binding(cause.BindingFailure))]);
        }
        else
        {
            node.ReportIn(store, requirement, code, condition: condition, related: this.FailedPremiseClauses(node)?.ToArray());
        }
    }

    private Koto? ConformanceDiagnosticCause(Koto owner)
        => owner is StructKoto or EnumKoto && this.RecordedFailure(owner) == BindingFailure.InvalidConstraint ? this.ConstraintCause(owner) : null;

    private Koto? FindConformanceDiagnosticCause(Koto owner, BoundConstraint fact)
    {
        if (fact.Kind == ConstraintKind.Error && owner is FunctionKoto function)
        {
            // A function's environment that is invalid only through its one Callable clause rests on that clause's failed part.
            for (var i = 0; i < function.TypeConstraints.Count; i++)
            {
                if (function.TypeConstraints[i] is IsKoto { Right: { } requirement } clause && ReferenceEquals(clause.BoundConstraint, fact) &&
                    this.ConstraintCause(KotoHelper.UnwrapParentheses(requirement)) is { } part)
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
                    ReferenceEquals(subject.BoundType, owner) && this.ConstraintCause(KotoHelper.UnwrapParentheses(requirement)) is not null)
                {
                    return KotoHelper.UnwrapParentheses(requirement);
                }
            }
        }

        return null;
    }

    private Koto? FindMissingConformanceName(IsKoto clause)
    {
        if (!DeclaresConformance(clause) || clause.BoundConstraint is null)
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

            // A name the requirement could not resolve: missing, or a projection before registration (G621's limit, Unsupported).
            if (node.BoundSymbol is null && node.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported)
            {
                this.Cause = node;
                return;
            }

            node.VisitChildren(this);
        }
    }
}
