// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.2.1, 10.7: no ordering is defined between an erasure and a direct match at one argument; the erasure's receiver and Owned
    // conditions are judged only after selection, so they never make it inapplicable.
    private const string ErasureAmbiguityNote = "At one argument, one candidate erases the argument to a common Function Type and another takes it directly; these adaptations are incomparable (SPEC 10.2.1, 10.7), and the erasure's receiver and Owned conditions are judged only after selection";

    private const string SharedObjectAuthorityNote = "rc and arc provide shared payload access only; even a strong count of one does not grant objuniq or uniq/Self authority";

    // SPEC 12.3.3, 13.3: string has no arithmetic; an interpolated literal is the one way to join strings, and a buffer builds text.
    private const string StringOperatorNote = "string has no arithmetic operators; an interpolated literal creates an owning string and borrows the values it embeds (SPEC 12.3.3, 13.3)";

    // SPEC 23.3.6.4: the operands a node consulted that did not resolve are its explicit prerequisites. They are recorded
    // when consulted (BindNode, failures of other nodes, symbol uses), never searched in the tree afterwards. The storage is
    // reused across passes, so neither resolving nodes nor a warm rebind of invalid code allocates.
    private readonly List<Koto> consulted = [];
    private readonly List<Koto> derivedIssues = [];
    private readonly List<Koto> prerequisiteStore = [];
    private readonly Dictionary<Koto, (int Start, int Count)> prerequisites = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, Koto> partPrerequisites = new(ReferenceEqualityComparer.Instance);
    private readonly List<DiagnosticKey> prerequisiteKeys = [];
    private readonly HashSet<Koto> prerequisiteVisited = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<Koto> prerequisitePending = new();

    // Keep proof failures intact. Only diagnostic publication follows these recorded
    // missing-name causes; later validators must still see an invalid declaration.
    private Dictionary<Koto, Koto>? constraintDiagnosticCauses;
    private Dictionary<Koto, BindingSymbol>? objectPayloadCauses;
    private MissingConstraintNameVisitor? missingConstraintNameVisitor;

    private FailedSignaturePartVisitor? failedSignaturePartVisitor;
    private int consultationStart = -1;
    private Koto? consultationNode;

    // The expression being bound without its expected Type because the syntax that supplies it failed.
    private (Koto Expression, Koto Cause)? missingExpectation;

    // SPEC 23.3.6.2: the Types a mismatch compared and the syntax that shows it, recorded only when a check fails.
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

    // SPEC 13.2, 13.3: the operand Type an operator rejected, or the count Type a shift rejected, recorded only when the check fails.
    private Dictionary<Koto, BoundType>? operatorOperands;

    // The selected iteration entry and the range whose boundary Types cannot supply it.
    private Dictionary<Koto, (BoundType Subject, BindingSymbol Entry)>? rangeIterationFailures;

    // The target a write could not use, recorded only when the check fails; it is the smallest syntax that shows the failure.
    private Dictionary<Koto, Koto>? writeTargets;

    // The candidates a failed overload selection considered, recorded only when it fails.
    private Dictionary<Koto, RejectedCandidate[]>? rejectedCandidates;

    // SPEC 15.1.5, 23.3.6.9: the bare Place whose acquisition a failed node needs spelled, and whether an exclusive borrow of it is of an
    // object handle, recorded only when the check fails.
    private Dictionary<Koto, (Koto Place, bool Object)>? acquisitionPlaces;

    // SPEC 7.6.2: the explicit capture entry a closure failed at, with its outer binding's Type, recorded only when it fails.
    private Dictionary<Koto, (CaptureKoto Capture, BoundType Type, BindingSymbol? Source)>? captureFailures;

    // SPEC 23.3.6.1: the part of an unsupported node that shows the form, such as one capture entry or a callee.
    private Dictionary<Koto, SourceSpan>? unsupportedSpans;

    // SPEC 9.6.1: a Type or container named with the wrong number of its own Type arguments, or without the Type arguments of
    // the generic container that declares it (Outer), with the declared and the written counts.
    private Dictionary<Koto, (BindingSymbol Declaration, int Declared, int Written, bool Outer)>? arityFailures;

    private readonly record struct RejectedCandidate(FunctionKoto Function, BoundType? Actual, BoundType? Expected, bool SharedReceiver = false, bool ObjectClone = false, bool CallableSignature = false, bool Selected = false, SemanticsKind? ActualReceiver = null, SemanticsKind? RequiredReceiver = null, bool ReferenceSignature = false, bool UnfixedReference = false, int ReceiverParameter = -1, bool ErasureIncomparable = false, ReferenceConstraintFailure? ConstraintFailure = null);

    // The referenced declaration, its Type parameter, the parameters of the fixed expected call signature whose per-call Origins the
    // slot would hold (as bits) and that signature.
    private readonly record struct PerCallSlotFact(FunctionKoto Declaration, int Slot, ulong Parameters, BoundType Signature);

    // The referenced declaration, its Type parameter when one is known (else -1), the failure and whether Type arguments are written.
    private readonly record struct ReferenceSlotFact(FunctionKoto Declaration, int Slot, ReferenceSlotFailure Failure, bool Explicit);

    // SPEC 15.6.1, 23.3.6.5: the Reason facts and related locations of an Origin relation record, shared by every phase that reports
    // one; `source` is `fit` for a fit, `declared` for a relation clause and `wellFormed` for a Type occurrence.
    internal static (object[] Evidence, (string Role, Koto At, string? Label)[]? Related) OriginRelationFacts(OriginRelationFact relation, string source)
    {
        // SPEC 15.6.5: a local initialized by a Borrow shows that Borrow, which is also the related location. An input's well-formedness
        // fails over an Origin that another input of the call supplied, so its Borrow is found in that call.
        var borrow = BorrowSource(relation.At, relation.Longer) ??
            (relation.WellFormed && EnclosingCall(relation.At) is { } call ? BorrowSource(call, relation.Longer) : null);
        // A clause of a Field or Case names its Type's own slots as written there, without `self.`; a Type's clause substituted at
        // a use in a body names the substituted Origins as that body does.
        var typeLevel = relation.Clause is not null && !relation.Substituted && TypeLevelClause(relation.At);
        var longer = OriginDisplay(relation.Longer, borrow, typeLevel);
        // SPEC 15.6.1, 23.3.6.5: a shorter end that an equality fixed to another argument's Borrow shows that Borrow too.
        var shorterBorrow = relation.FixedBy is { } fixedBy && relation.Shorter.Kind is OriginKind.Projection or OriginKind.Anchor ? BorrowSource(fixedBy, relation.Shorter) : null;
        var shorter = OriginDisplay(relation.Shorter, shorterBorrow, typeLevel);
        var operation = relation.Equality ? "==" : "outlives";
        // SPEC 23.3.6.5: a borrow or omitted end is also related at its syntax.
        var longerAt = longer.Kind == "borrow" ? borrow ?? relation.At : longer.Kind == "omitted" ? OmittedAt(relation.Longer) : null;
        var shorterAt = shorter.Kind == "omitted" ? OmittedAt(relation.Shorter) : shorter.Kind == "borrow" ? shorterBorrow : null;
        (string Role, Koto At, string? Label)[]? related = longerAt is not null && shorterAt is not null ? [("origin", longerAt, null), ("origin", shorterAt, null)]
            : longerAt is not null ? [("origin", longerAt, null)]
            : shorterAt is not null ? [("origin", shorterAt, null)]
            : null;
        if (relation.Clause is { } clause)
        {
            // SPEC 23.3.6.5: a declared relation relates its clause with the role `relation`.
            related = related is null ? [("relation", clause, null)] : [.. related, ("relation", clause, null)];
        }

        if (relation.FixedBy is { } input)
        {
            // SPEC 15.6.1: the input whose equality made a call's fresh Origin equal to a fixed one is related with the role `relation`.
            related = related is null ? [("relation", input, null)] : [.. related, ("relation", input, null)];
        }

        if (source != "fit")
        {
            // SPEC 23.3.6.5: only a fit names its destination Type; a declared relation relates its clause instead, and well-formedness
            // its Type occurrence, which is the primary location.
            return ([operation, longer, shorter, source], related);
        }

        // SPEC 23.3.6.5: the destination shows only the Origin at the failed position, also below another borrow layer; a borrow, omitted
        // or closure end is never written as an Origin expression.
        var destination = relation.Destination is not { } type ? $"during {shorter.Text}"
            : DiagnosticTypeName(type, shorter.Kind == "expression" ? relation.Shorter : null, shorter.Text);
        return ([operation, longer, shorter, source, destination], related);
    }

    // SPEC 15.6.1: whether a relation is Refuted: its longer end is a body-local finite Origin and its shorter end a fixed one.
    internal static bool RefutesOriginRelation(BoundOrigin longer, BoundOrigin shorter) => BodyLocalOrigin(longer) && FixedOrigin(shorter);

    internal static DiagnosticCode OriginRelationCode(OriginRelationFact relation)
        => relation.Refuted ? DiagnosticCode.UnsatisfiedOriginRelation_Kd : DiagnosticCode.UnprovenOriginRelation_Kd;

    /// <summary>Forms the Transfer candidate of a bare Place (SPEC 23.3.6.9): <c>@move</c> after the Place, or <c>(*p)@move</c> around a dereference.</summary>
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

    // SPEC 23.3.6.5: the Type occurrence that an omitted Origin end is related at.
    private static Koto? OmittedAt(BoundOrigin origin)
        => origin.Occurrence ?? OmittedInput(origin) ?? (origin is { Kind: OriginKind.Inference, Binder: TypeKoto occurrence } ? occurrence : null);

    // SPEC 23.3.6.5: the input Type occurrence of a Function Type whose outer Origin is this per-call Origin; it has no name.
    private static Koto? OmittedInput(BoundOrigin origin)
        => origin is { Kind: OriginKind.Input, Occurrence: null, Binder: FunctionTypeKoto binder } && origin.InputIndex >= 0 && origin.InputIndex < InputCount(binder)
            ? InputType(binder, origin.InputIndex) is ParenthesizedTypeKoto { Type: { } inner } ? inner : InputType(binder, origin.InputIndex) : null;

    private static bool SharedObjectAuthorityMismatch(BoundType actual, BoundType expected)
        => ObjectTypes.HandleMode(actual) is { PayloadAuthority: LoanRequirement.Ref } &&
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

    // The call signature of a Function Item, closure or Function value.
    private BoundType? ValueSignature(BoundType value) => value.Kind switch
    {
        BoundTypeKind.FunctionItem => this.FunctionItemSignature(value),
        BoundTypeKind.Closure => this.ClosureSignature(value),
        BoundTypeKind.Function => value,
        _ => null,
    };

    private string? ClosureConversionNote(Koto node, object actual, object expected)
    {
        if (expected is not BoundType { Kind: BoundTypeKind.Function } signature)
        {
            return null;
        }

        // SPEC 10.5, 7.6.4: a generic reference binds its slots from the expected signature, and its Item converts only
        // when its bound arguments are Owned.
        if (actual is BoundType { Kind: BoundTypeKind.FunctionItem } item && (item.Components.Count != 0 || item.LengthArguments.Length != 0))
        {
            return this.FunctionItemSignature(item) is { } itemSignature && CallableSignatureFits(itemSignature, signature, SignatureOwner(item))
                ? "Common Function conversion requires Owned bound generic arguments; this Item's arguments are not proven Owned"
                : null;
        }

        if (actual is BoundType { Kind: BoundTypeKind.Function } && node.BoundSymbol is { Kind: BindingSymbolKind.Function, Next: null, Declaration: FunctionKoto { GenericArguments.Count: > 0 } })
        {
            return ReferenceSlotNote(this.referenceSlotFacts is { } facts && facts.TryGetValue(node, out var fact) ? fact : null);
        }

        // The closure plan, not BoundClosure: a closure that failed at a call argument or a default keeps its plan (SPEC 7.6.4).
        if (actual is not BoundType { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { ClosureStorage: { Signature: not null } closure } declaration })
        {
            return null;
        }

        return closure.Receiver switch
        {
            SemanticsKind.Uniq => "This closure requires an Exclusive call; a common Function value permits Shared calls only",
            SemanticsKind.Owner => "This closure requires a Consuming call; a common Function value permits Shared calls only",
            _ => !CallableSignatureFits(closure.Signature, signature, declaration)
                ? "The closure's parameter or result contract does not match the expected common Function signature"
                : "Common Function conversion requires an Owned environment; captured non-static borrows cannot be erased",
        };

        // SPEC 10.5, 10.8: why the one generic candidate's slots did not bind from S, when the binding recorded it.
        static string ReferenceSlotNote(ReferenceSlotFact? fact)
        {
            var prefix = fact is { } entry && ItemTypeArgumentCount(entry.Declaration) != entry.Declaration.GenericArguments.Count
                ? "The generic function's Type and length parameters are bound from the expected signature without adaptations; "
                : "The generic function's Type parameters are bound from the expected signature without adaptations; ";
            var slot = fact is { Slot: >= 0 } known && known.Slot < known.Declaration.GenericArguments.Count
                ? $"{(known.Declaration.GenericArguments[known.Slot] is LengthParameterKoto ? "length" : "Type")} parameter '{known.Declaration.GenericArguments[known.Slot].Identifier}'" : "a generic parameter";
            return fact?.Failure switch
            {
                ReferenceSlotFailure.Structure when fact.Value.Slot >= 0 => prefix + $"the signature does not bind {slot}",
                ReferenceSlotFailure.Structure => prefix + "no binding of them fits its structure",
                ReferenceSlotFailure.Constraint => prefix + "the binding fits, but it is not proven to satisfy the declaration's Constraints or to form its signature Types",
                ReferenceSlotFailure.OriginConflict when fact.Value.Explicit => prefix + $"the written Type arguments and the signature give {slot} Types that differ only in their Origins, " +
                    "and a bound Type argument holds one Origin for every call",
                ReferenceSlotFailure.OriginConflict => prefix + $"its parameters and result bind {slot} to Types that differ only in their Origins, " +
                    "and a bound Type argument holds one Origin for every call",
                ReferenceSlotFailure.InputOrigin => prefix + $"{slot} would hold an input Origin that is bound at each call, such as a per-call Origin of the signature, " +
                    "which never becomes part of a bound Type argument (SPEC 10.5)",
                _ => prefix + "no binding fits it, or a bound argument fails its Constraints",
            };
        }
    }

    /// <summary>Gets final failures that are explained by failed prerequisites; they are reported as derived problems.</summary>
    internal IReadOnlyList<Koto> DerivedIssues => this.derivedIssues;

    // A recovery node, an ErrorKoto or synthesized syntax kept in place of a rejected form, stands for its syntax error.
    internal static bool IsRecovery(Koto node, out DiagnosticKey cause)
    {
        if (node is ErrorKoto error)
        {
            cause = error.Cause ?? DiagnosticKey.Unresolved;
            return true;
        }

        if (node.CodeContext.RecoveryCause(node) is { } recorded)
        {
            cause = recorded;
            return true;
        }

        cause = default;
        return false;
    }

    /// <summary>Gets the Binding causes of a node that a later phase checks: none when Binding resolved it or recorded no cause.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The check keys, or <see langword="null"/>.</returns>
    internal DiagnosticKey[]? FailureCauses(Koto node)
        => node.BindingState != BindingState.Resolved && (IsRecovery(node, out _) || node.BindingFailure != BindingFailure.None || this.HasUnresolvedPrerequisite(node))
            ? this.CauseKeys(node) : null;

    // A + whose operands are strings or string joins, through parentheses; the depth bound keeps pathological chains cheap.
    private static bool IsStringJoin(Koto node, int depth)
        => depth < 32 && KotoHelper.UnwrapParentheses(node) is BinaryKoto { Akind: KotoKind.Plus } join && IsStringOperand(join.Left, depth + 1) && IsStringOperand(join.Right, depth + 1);

    private static bool IsStringOperand(Koto node, int depth)
        => ReferenceTypes.EndsInString(KotoHelper.UnwrapParentheses(node).BoundType) || IsStringJoin(node, depth);

    // SPEC 15.6.1, 23.3.6.5: an Origin relation record at the value that supplies the longer end, with both ends as Origin
    // displays, the relation's source and the destination Type, in which only the Origin at the failed position is shown; a
    // borrow end is related at its syntax; the SPEC 15.4.3 elision Note follows an omitted result Origin.
    // SPEC 15.6.1, 23.3.6.5: the Reason names the comparison, the member, the relation and its two ends, rigid symbols of the comparison
    // displayed as the required contract writes them; an omitted end is related at its Type occurrence.
    private static void ReportOriginContract(Koto node, OriginContractFact contract, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (contract.Required is { } original)
        {
            node.Report(requirement, code, evidence: ["implementation", contract.Member, contract.Equality ? "==" : "outlives", OriginDisplay(contract.Longer, null), OriginDisplay(contract.Shorter, null)], related: [("requirement", original, "the original implementation contract")], at: contract.At);
            return;
        }

        // An erased closure's environment binding lies within its call receiver, which no Origin expression names (SPEC 15.8.2).
        var longer = contract.Longer is { Kind: OriginKind.Projection, Binder: FunctionKoto { IsAnonymous: true } } environment && environment.Slot <= EnvironmentSlot(0)
            ? new DiagnosticOrigin("closure", "call receiver") : OriginDisplay(contract.Longer, null);
        var shorter = OriginDisplay(contract.Shorter, null);
        var longerAt = longer.Kind == "omitted" ? OmittedAt(contract.Longer) : null;
        var shorterAt = shorter.Kind == "omitted" ? OmittedAt(contract.Shorter) : null;
        (string Role, Koto At, string? Label)[]? related = longerAt is not null && shorterAt is not null ? [("origin", longerAt, null), ("origin", shorterAt, null)]
            : longerAt is not null ? [("origin", longerAt, null)]
            : shorterAt is not null ? [("origin", shorterAt, null)]
            : null;
        if (longer.Kind == "closure" && contract.Longer.Binder is FunctionKoto closure)
        {
            // SPEC 7.6.4, 23.3.6.5: a common Function value cannot return a borrow of its hidden environment receiver; the closure end
            // is related at the anonymous function's header.
            var header = SourceSpan.FromBounds(closure.Span.Start, Math.Max(closure.Span.Start, closure.HeaderEnd));
            const string Note = "A common Function value cannot return a borrow of its hidden environment receiver (SPEC 7.6.4)";
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
            // SPEC 23.3.6.5: a closure end is related at the anonymous function's header, from `func` through the parameter list.
            var header = SourceSpan.FromBounds(closure.Span.Start, Math.Max(closure.Span.Start, closure.HeaderEnd));
            var at = relation.At is CodeBlockKoto ? BorrowSource(relation.At, relation.Longer) ?? relation.At : relation.At;
            node.Report(requirement, code, note: note, evidence: evidence, related: related, relatedSpans: [("origin", closure, header, null)], at: at);
            return;
        }

        node.Report(requirement, code, note: note, evidence: evidence, related: related, at: relation.At, condition: condition);
    }

    private static bool BodyLocalOrigin(BoundOrigin origin)
    {
        if (origin.Kind is OriginKind.Projection or OriginKind.Anchor)
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (BodyLocalOrigin(origin.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }

    // The Borrow that supplies a body-local Origin: the value itself, or the initializer of the local it names. For a meet, it is the
    // Borrow of its first body-local operand, which the meet's display shows (SPEC 23.3.6.5).
    private static Koto? BorrowSource(Koto value, BoundOrigin origin)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(value);
        if (unwrapped.BoundType?.Origin is { } region && IsLocalRegion(region) &&
            value.CodeContext.Compilation.Binding.LocalRegionSourceUse(region, origin) is { } evidence && !ReferenceEquals(evidence, value))
        {
            return BorrowSource(evidence, origin);
        }

        if (unwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrowValue)
        {
            // SPEC 23.3.6.5: at an inner position of a Borrow, such as the stored `local@ref` of `y@uniq` after `var y = local@ref`, the
            // failing Origin is not the Borrow's own: it is found through the borrowed Place.
            if (borrowValue.BoundType?.Origin is not { } own || OriginIncludes(own, origin) || OriginIncludes(origin, own))
            {
                return unwrapped;
            }
        }

        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (origin.Operands[i].Kind is OriginKind.Projection or OriginKind.Anchor && BorrowSource(value, origin.Operands[i]) is { } operand)
                {
                    return operand;
                }
            }

            return null;
        }

        if (origin.Kind == OriginKind.Projection && unwrapped is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto { InitializerKoto: { } initializer } } } &&
            KotoHelper.UnwrapParentheses(initializer) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } source && ReferenceEquals(source.BoundType?.Origin, origin))
        {
            return source;
        }

        // A call whose result takes an argument's implicit Borrow names that argument (SPEC 15.6.1: `take(local)`).
        if (origin.Kind is OriginKind.Projection or OriginKind.Anchor && unwrapped is InvocationKoto { BoundCall: { } call })
        {
            foreach (var operation in call.ArgumentOperations)
            {
                if (operation is { Kind: ArgumentOperationKind.Borrow, Source: { } argument, AdaptedType.Origin: { } borrowed } && ReferenceEquals(borrowed, origin))
                {
                    return argument;
                }
            }
        }

        // A value that contains the Borrow, such as a call or Tuple over it, names that Borrow; so does the initializer of the local
        // whose Place the value reads or moves, such as `h0@move` or `h0.item` after `let h0 = H.init(a@ref)`.
        if (origin.Kind is not (OriginKind.Projection or OriginKind.Anchor))
        {
            return null;
        }

        return LocalBorrow(value, origin) ?? (RootLocal(unwrapped) is { InitializerKoto: { } rootValue } ? LocalBorrow(rootValue, origin) : null);

        static VariableKoto? RootLocal(Koto value)
        {
            for (var depth = 0; depth < 64; depth++)
            {
                switch (value)
                {
                    case ConversionKoto { ConversionBinding: ConversionBinding.Transfer or ConversionBinding.Borrow } transfer:
                        value = KotoHelper.UnwrapParentheses(transfer.Left);
                        continue;
                    case MemberAccessKoto access:
                        value = KotoHelper.UnwrapParentheses(access.Left);
                        continue;
                    case IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto local } }:
                        return local;
                    default:
                        return null;
                }
            }

            return null;
        }
    }

    // SPEC 15.2.1: the omitted slot of a parameter's Type, written directly or under one borrow layer, is the projection `p.slot`;
    // any other omitted slot has no name.
    private static string? SlotProjection(BoundOrigin origin, Koto occurrence)
    {
        if (origin.Binder is not FunctionKoto function || origin.InputIndex < 0 || origin.InputIndex >= function.Parameters.Count)
        {
            return null;
        }

        var type = function.Parameters[origin.InputIndex].Type;
        if (!ReferenceEquals(type, occurrence) && !(type is TypeSemanticsKoto { Type: { } target } && ReferenceEquals(target, occurrence)))
        {
            return null;
        }

        return occurrence.BoundType?.Symbol?.Schema?.Origins is { } slots && origin.TargetSlot >= 0 && origin.TargetSlot < slots.Count
            ? function.Parameters[origin.InputIndex].InternalName + "." + slots[origin.TargetSlot].Name : null;
    }

    // SPEC 23.3.6.5: an Origin by its display kind and string: an Origin expression for static, a parameter or receiver, a slot or
    // a meet; the source text of a Borrow, or of its Place, for a body-local finite Origin; the Type occurrence of an omitted slot.
    private static DiagnosticOrigin OriginDisplay(BoundOrigin origin, Koto? value, bool typeLevel = false)
    {
        switch (origin.Kind)
        {
            case OriginKind.Static:
                return new("expression", "static");
            case OriginKind.Intersection:
                // A body-local operand shows the Borrow that supplies it, never the text of its binder.
                var parts = new string[origin.Operands.Count];
                for (var i = 0; i < parts.Length; i++)
                {
                    var supplied = value is not null && KotoHelper.UnwrapParentheses(value) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Origin: { } borrowed } &&
                        OriginIncludes(borrowed, origin.Operands[i]) ? value : null;
                    parts[i] = OriginDisplay(origin.Operands[i], supplied, typeLevel).Text;
                }

                return new("expression", "(" + string.Join(" and ", parts) + ")");
            case OriginKind.Input when origin.Occurrence is { } occurrence:
                return SlotProjection(origin, occurrence) is { } projection ? new("expression", projection) : new("omitted", occurrence.ToString());
            case OriginKind.Input when OmittedInput(origin) is { } input:
                return new("omitted", input.ToString());
            case OriginKind.Input when origin.Binder is { } binder && origin.InputIndex >= 0 && origin.InputIndex < InputCount(binder):
                return new("expression", InputName(binder, origin.InputIndex));
            case OriginKind.Parameter when origin.Occurrence is GenericParameterKoto declaration:
                return new("omitted", declaration.ToString()); // SPEC 8.1.1, 23.3.6.5: the outer Origin `o` of a pair binder.
            case OriginKind.Parameter:
                return new("expression", origin.Binder is DeclarationContainerKoto && !typeLevel ? "self." + origin.Name : origin.Name);
            case OriginKind.Inference when origin.Occurrence is null && origin.Binder is TypeKoto occurrence:
                return new("omitted", occurrence.ToString());
            case OriginKind.Projection when origin.Slot == CallResultSlot && origin.Binder is FunctionKoto { IsAnonymous: true }:
                return new("closure", "call result");
            case OriginKind.Projection or OriginKind.Anchor:
                // Without its Borrow, the Place: a local or a parameter by name, and a temporary by its text.
                var text = value is not null && KotoHelper.UnwrapParentheses(value) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow ? borrow.ToString()
                    : origin.Binder is VariableKoto variable ? variable.NameKoto.IdentifierName
                    : origin.Binder is FunctionKoto function && origin.Slot >= 0 && origin.Slot < function.Parameters.Count ? function.Parameters[origin.Slot].InternalName
                    : origin.Binder?.ToString() ?? origin.Name;
                return new("borrow", text);
            default:
                return new("omitted", origin.Occurrence?.ToString() ?? origin.Name);
        }
    }

    // Whether `atom` is `whole` or one of the operands of its meet, at any depth.
    private static bool OriginIncludes(BoundOrigin whole, BoundOrigin atom)
    {
        if (ReferenceEquals(whole, atom))
        {
            return true;
        }

        for (var i = 0; i < whole.Operands.Count; i++)
        {
            if (OriginIncludes(whole.Operands[i], atom))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 9.6.1: outside constructor-own inference, required Type arguments are written in full; an outer container never takes call
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
            note = $"{node} is declared in {declared}; outside it, the Type arguments of {name} are written on the qualifier and are never inferred (SPEC 9.6.1)";
        }
        else if (arity.Written == 0 && member is not null)
        {
            note = $"{declared} is named without its Type arguments; this qualifier requires explicit Type arguments; only the construction target itself can infer its own slots (SPEC 9.6.1, 10.8.1)";
        }
        else
        {
            note = $"{declared} declares {arity.Declared} Type parameter{(arity.Declared == 1 ? string.Empty : "s")}, and {arity.Written} Type argument{(arity.Written == 1 ? " is" : "s are")} written (SPEC 9.6.1)";
        }

        node.Report(requirement, code, note: note, related: [("declaration", arity.Declaration.Declaration, null)]);
    }

    // SPEC 15.2.3, 23.3.6.5: an Owned failure of a common Function conversion is a Constraint record whose Reason names the subject, the
    // member through which the Origin enters OwnedOrigins and, when one is displayable, that Origin; a capture borrow is shown by its
    // entry and related there.
    private void ReportOwnedConversion(Koto node, OwnedConversionFact owned, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var subject = DiagnosticTypeName(owned.Subject);
        var entry = owned.Entry is { Operation: not null and not Constants.MoveOperation } borrowEntry ? borrowEntry : (CaptureKoto?)null;
        var origin = owned.Origin is not { } failing ? (DiagnosticOrigin?)null
            : entry is { } written ? new DiagnosticOrigin("borrow", written.Name + "@" + written.Operation) : OriginDisplay(failing, owned.Borrow);

        // SPEC 23.3.6.5: a borrow end is related at its syntax: a capture entry that borrows, or the Borrow a captured binding was
        // initialized by; otherwise at the capture entry or the converted value. An omitted end is related at its Type occurrence.
        (string Role, Koto Owner, SourceSpan Span, string? Label)[]? spans = null;
        (string Role, Koto At, string? Label)[]? related = null;
        if (origin is { Kind: "borrow" or "omitted" } relatedOrigin)
        {
            if (entry is { } at && owned.Closure is { } closure)
            {
                spans = [("origin", closure, at.Span, null)];
            }
            else if (relatedOrigin.Kind == "omitted" && OmittedAt(owned.Origin!) is { } occurrence)
            {
                related = [("origin", occurrence, null)];
            }
            else if (owned.Borrow is { } borrow)
            {
                related = [("origin", borrow, null)];
            }
            else if (owned.Entry is { } named && owned.Closure is { } owner)
            {
                spans = [("origin", owner, named.Span, null)];
            }
            else
            {
                related = [("origin", owned.At, null)];
            }
        }

        string note;
        if (owned.Closure is { } converted && PreparedCapture(converted, owned.Member) is { } preparedSource)
        {
            // SPEC 7.2.3: in a default, a capture of a preceding argument can neither move it nor keep a new borrow of it, and the
            // closure always converts to the parameter's Function Type, so only a Copy that holds no borrow or a new parameter remain.
            note = (origin is { } shown
                ? $"Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture {owned.Member} depends on {OriginText(shown)}, which is not static"
                : $"Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture {owned.Member} is not proven Owned") +
                "; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)";
            // An exclusive reference is captured by a Reborrow, which no proof makes valid there.
            var parameter = $"give the Function Type a parameter for {owned.Member} and pass {owned.Member} where the function value is called";
            // A borrowing entry of a Copy Owned argument, such as [k@ref] of k: i32, is repaired by its bare Copy entry, [k].
        }
        else if (owned.Closure is not null)
        {
            note = origin is { } shown
                ? $"Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture {owned.Member} depends on {OriginText(shown)}, which is not static"
                : $"Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture {owned.Member} is not proven Owned";
        }
        else
        {
            note = $"Common Function conversion requires Owned bound generic arguments (SPEC 7.6.4, 15.2.3); the {owned.Member} is not proven Owned";
        }

        object[] evidence = origin is { } fact ? [subject, owned.Member, fact] : [subject, owned.Member];
        node.Report(requirement, code, note: note, evidence: evidence, related: related, relatedSpans: spans, at: owned.At);

        static string OriginText(DiagnosticOrigin origin) => origin.Kind switch
        {
            "borrow" => "the borrow " + origin.Text,
            "omitted" => "the omitted Origin of " + origin.Text,
            "closure" => "the closure's " + origin.Text,
            _ => origin.Text,
        };
    }

    // SPEC 7.6.2: an anonymous function without a capture list never captures contextual self or a setter's value; its use is
    // the location, and the declaration of the binding is related. In a later default of the receiver's function, an entry can
    // neither move self nor keep a new borrow of it (SPEC 7.2.3), so only a Copy that holds no borrow, [self], can be offered.
    private void ReportContextualCapture(Koto node, BindingSymbol contextual, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var declaring = contextual.Declaration!;
        var self = contextual.Name == "self";
        var prepared = self && DefaultParameters.InLaterDefault(node, contextual);
        var note = self ? "Contextual self is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.6.2)"
            : "A setter's value is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.6.2)";
        if (prepared)
        {
            note += "; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)";
        }

        node.Report(requirement, code, note: note, related: [("declaration", declaring is FunctionKoto { Accessor.Declaration: { } accessor } ? accessor : declaring, null)]);
    }

    private BoundType? CompleteDependent(Koto node, Koto cause)
    {
        this.prerequisites[node] = (this.prerequisiteStore.Count, 1);
        this.prerequisiteStore.Add(cause);
        return Complete(node, null);
    }

    /// <summary>Binds an expression against the Type that a written syntax supplies; when that syntax failed, a check that
    /// needs the expected Type names it as the prerequisite (SPEC 23.3.6.4).</summary>
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
    // inference and is a located Unsupported (SPEC 23.3.6.1), never a relation record.
    // The signature and own binder an implementation converts to a common Function Type with (SPEC 7.6.4): a Function value, a Function
    // Item proven Owned, or a closure with a Shared receiver and a proven Owned environment. Other failures stay Type mismatches.
    private bool ConversionSignature(BoundType implementation, Koto use, out BoundType signature, out Koto? own)
    {
        own = SignatureOwner(implementation);
        signature = implementation;
        if (implementation.Kind == BoundTypeKind.Function)
        {
            return implementation.Components.Count == 2;
        }

        if (implementation.Kind == BoundTypeKind.FunctionItem && this.FunctionItemSignature(implementation) is { } item && this.ProveOwned(implementation, use) == ConstraintProof.Proven)
        {
            signature = item;
            return true;
        }

        if (implementation is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { ClosureStorage: { Receiver: SemanticsKind.Ref, Signature: not null } } } &&
            this.ClosureSignature(implementation) is { } closure && this.ProveOwned(implementation, use) == ConstraintProof.Proven)
        {
            signature = closure;
            return true;
        }

        return false;
    }

    // SPEC 15.2.3, 7.6.4: the first member of a conversion subject's OwnedOrigins through which a non-static Origin enters: a capture of a
    // closure with a Shared receiver, or a bound Type argument of an Item, whose signature matches structurally. The first Refuted member
    // (a body-local Origin) wins, else the first Unknown one; the Owned proof itself stays unchanged.
    private OwnedConversionFact? OwnedConversionFailure(BoundType implementation, BoundType required, Koto at, Koto use)
    {
        BoundType? signature;
        FunctionKoto? closure = null;
        IReadOnlyList<BoundCapture>? captures = null;
        if (implementation.Kind == BoundTypeKind.FunctionItem)
        {
            signature = this.FunctionItemSignature(implementation);
        }
        else if (implementation is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { ClosureStorage: { Receiver: SemanticsKind.Ref, Signature: not null } plan } declaration })
        {
            signature = this.ClosureSignature(implementation);
            closure = declaration;
            captures = plan.Captures;
        }
        else
        {
            return null;
        }

        if (signature is null || !ReferenceTypes.StorageMatches(required, signature) || this.ProveOwned(implementation, use) == ConstraintProof.Proven)
        {
            return null;
        }

        OwnedConversionFact? unknown = null;
        var count = captures?.Count ?? implementation.Components.Count;
        for (var i = 0; i < count; i++)
        {
            var member = captures is null ? implementation.Components[i]
                : implementation.Components.Count == captures.Count ? implementation.Components[i] : captures[i].Environment.Type;
            if (member is null || this.ProveOwned(member, use) == ConstraintProof.Proven)
            {
                continue;
            }

            var origin = this.FirstUnownedOrigin(member, use, 0);
            var name = captures is null ? $"{Ordinal(i + 1)} Type argument" : captures[i].Environment.Name;

            // A captured binding initialized by a Borrow shows that Borrow (SPEC 15.6.5).
            var borrow = captures is not null && origin is not null && captures[i].Source.Declaration is VariableKoto { InitializerKoto: { } initializer } &&
                KotoHelper.UnwrapParentheses(initializer) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } source && ReferenceEquals(source.BoundType?.Origin, origin) ? source : null;
            var fact = new OwnedConversionFact(at, implementation, name, member, origin, closure is null ? null : CaptureEntryOf(closure, name), closure, borrow, origin is not null && BodyLocalOrigin(origin));
            if (fact.Refuted)
            {
                return fact;
            }

            unknown ??= fact;
        }

        return unknown;

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // The first Origin in a Type's OwnedOrigins, in the order of SPEC 15.2.3, that is not proven static; a common Function Type
    // contributes no per-call Origin (SPEC 15.2.3).
    private BoundOrigin? FirstUnownedOrigin(BoundType type, Koto use, int depth)
    {
        if (depth > 32)
        {
            return null;
        }

        if (type.Origin is { } origin && !this.ProvesOriginOutlives(origin, BoundOrigin.Static, use))
        {
            return origin;
        }

        for (var i = 0; i < type.Components.Count && type.Kind != BoundTypeKind.Function; i++)
        {
            if (this.FirstUnownedOrigin(type.Components[i], use, depth + 1) is { } inner)
            {
                return inner;
            }
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (type.OriginArguments[i] is { } argument && !this.ProvesOriginOutlives(argument, BoundOrigin.Static, use))
            {
                return argument;
            }
        }

        return null;
    }

    private BoundType? RecordMismatch(Koto node, Koto at, object actual, object expected)
    {
        // SPEC 15.2.3, 7.6.4: the Owned condition of a common Function conversion is a Constraint failure, never a Type mismatch.
        if (expected is BoundType { Kind: BoundTypeKind.Function } owner && actual is BoundType { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } value &&
            this.OwnedConversionFailure(value, owner, at, node) is { } owned)
        {
            return this.FailExplained(ref this.ownedConversions, node, owned.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, owned);
        }

        // SPEC 10.7, 15.6.1: a common Function conversion compares whole contracts; with matching signatures, its Origin failure is
        // one UnprovenOriginContract_Kd at the converted value, never a Type mismatch or a relation per position.
        if (expected is BoundType { Kind: BoundTypeKind.Function } required && actual is BoundType implementation &&
            this.ConversionSignature(implementation, node, out var signature, out var own) && ReferenceTypes.StorageMatches(required, signature) &&
            (implementation.Kind == BoundTypeKind.FunctionItem ? this.ItemContractFailure(implementation, signature, required, at, node)
            : this.ConversionContractFailure(signature, required, own, at, node)) is { } contract)
        {
            return this.FailExplained(ref this.originContracts, node, BindingFailure.OriginContract, contract);
        }

        if (actual is BoundType actualType && expected is BoundType expectedType && ReferenceTypes.StorageMatches(expectedType, actualType))
        {
            var all = this.relationScratch;
            all.Clear();
            if (this.FailedOriginRelation(actualType, expectedType, node, OriginVariance.Covariant, judged: true, all: all) is { } relation)
            {
                if (!relation.Refuted && this.FailedOriginEnvironment(node) is { } cause)
                {
                    var refuted = false;
                    for (var i = 0; i < all.Count; i++)
                    {
                        refuted |= all[i].Refuted;
                    }

                    if (!refuted)
                    {
                        all.Clear();
                        return this.CompleteDependent(node, cause);
                    }
                }

                // SPEC 15.6.1: every failed chain is reported; the records other than the node's own are published with it.
                var failed = node.BindingFailure == BindingFailure.None;
                for (var i = 0; i < all.Count && failed; i++)
                {
                    if (!all[i].Equals(relation))
                    {
                        this.RecordMoreRelation(node, all[i] with { At = at, Destination = expectedType });
                    }
                }

                all.Clear();
                return this.FailExplained(ref this.originRelations, node, BindingFailure.OriginRelation, relation with { At = at, Destination = expectedType });
            }

            if (this.FailedOriginRelation(actualType, expectedType, node, OriginVariance.Covariant) is { } unproven)
            {
                if (this.FailedOriginEnvironment(node) is { } cause)
                {
                    return this.CompleteDependent(node, cause);
                }

                // No concrete lifetime was refuted above. An unresolved end without an identified failed contract remains
                // an independent proof failure; it must not be hidden by the presence of an unrelated declaration error.
                return unproven.Longer.Kind == OriginKind.Unbound || unproven.Shorter.Kind == OriginKind.Unbound
                    ? this.FailExplained(ref this.originRelations, node, BindingFailure.OriginRelation, unproven with { At = at, Destination = expectedType })
                    : this.Fail(node, BindingFailure.Unsupported);
            }
        }

        return this.FailExplained(ref this.mismatches, node, BindingFailure.TypeMismatch, (at, actual, expected));
    }

    // SPEC 15.6.1: a Refuted Origin relation is UnsatisfiedOriginRelation_Kd, one that is not proven UnprovenOriginRelation_Kd.
    private DiagnosticCode OriginRelationCode(Koto node)
        => this.originRelations?.TryGetValue(node, out var relation) == true ? OriginRelationCode(relation) : DiagnosticCode.UnprovenOriginRelation_Kd;

    // SPEC 15.6.1: the first Origin position of a fit, in the order of the outer borrow layer, the Semantics target, then slots and
    // Type arguments, whose relation is not proven; `==` at an invariant position. It is Refuted when the longer end is a body-local
    // finite Origin and the shorter end a fixed one, and Unknown otherwise. With `all`, every failing position, and every failing
    // operand of a meet at its longer end, is added in that order (SPEC 15.6.1: every failed chain is reported).
    // With `judged`, only a position that the relation judge fails counts (SPEC 15.6.5), as a body fit judges it; without it, any
    // unproven position does, as for a fit that judges Origins by proof alone.
    // `polarity` is the variance of the compared position within the fitted Type, composed as FitsTypeCore and CheckTypeUse compose
    // it (SPEC 15.3.5): a contravariant position names the reverse relation, and an invariant or unused one an `==`, also for every
    // position nested in it, such as the slots of a Type argument stored through `uniq/T` or read by `(T) -> i32`.
    private OriginRelationFact? FailedOriginRelation(BoundType actual, BoundType expected, Koto use, OriginVariance polarity, int depth = 0, bool judged = false, List<OriginRelationFact>? all = null, bool verified = false)
    {
        if (depth > 64)
        {
            return null;
        }

        OriginRelationFact? first = null;
        // SPEC 8.1.2, 15.6.5: the outer Origins as the fit relates them; a pair layer's slot exists only in its binder's borrow cases,
        // whose premises the relation may use.
        if (FitOuterOrigins(actual, expected, this, use, out var outer, out var required) == OuterOriginFit.Relation &&
            this.FailedOriginPart(outer, required, expected, use, polarity, judged, all, verified, SlotCondition(actual, this, use)) is { } layer)
        {
            if (all is null)
            {
                return layer;
            }

            first ??= layer;
        }

        var exclusive = IsInvariantLayer(actual, this);
        for (var i = 0; i < actual.Components.Count && i < expected.Components.Count && actual.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication; i++)
        {
            var target = exclusive ? OriginVariance.Invariant : polarity;
            if (this.FailedOriginRelation(actual.Components[i], expected.Components[i], use, target, depth + 1, judged, all, verified) is { } failed)
            {
                if (all is null)
                {
                    return failed;
                }

                first ??= failed;
            }
        }

        for (var i = 0; i < actual.OriginArguments.Count && i < expected.OriginArguments.Count; i++)
        {
            var variance = ComposeVariance(polarity, expected.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant);
            if (this.FailedOriginPart(actual.OriginArguments[i], expected.OriginArguments[i], expected, use, variance, judged, all, verified) is { } slot)
            {
                if (all is null)
                {
                    return slot;
                }

                first ??= slot;
            }
        }

        for (var i = 0; i < actual.Components.Count && i < expected.Components.Count && actual.Kind is not (BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication); i++)
        {
            var position = actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count ? schema.GenericSlots[i].OriginVariance
                : actual.Kind == BoundTypeKind.Function && actual.Components.Count == 2 && i == 0 ? OriginVariance.Contravariant
                : OriginVariance.Covariant;
            if (this.FailedOriginRelation(actual.Components[i], expected.Components[i], use, ComposeVariance(polarity, position), depth + 1, judged, all, verified) is { } argument)
            {
                if (all is null)
                {
                    return argument;
                }

                first ??= argument;
            }
        }

        return first;
    }

    // One Origin position of a fit by its variance: `actual` outlives `expected` at a covariant position, the reverse at a
    // contravariant one, and both, as `==` with `actual` first, at an invariant one.
    private OriginRelationFact? FailedOriginPart(BoundOrigin actual, BoundOrigin expected, BoundType type, Koto use, OriginVariance variance, bool judged, List<OriginRelationFact>? all = null, bool verified = false, ulong condition = 0)
    {
        if (ReferenceEquals(actual, expected))
        {
            return null;
        }

        if (variance == OriginVariance.Contravariant)
        {
            return Fails(expected, actual) ? this.FailedChain(use, expected, actual, type, judged, all, condition) : null;
        }

        if (variance == OriginVariance.Covariant)
        {
            return Fails(actual, expected) ? this.FailedChain(use, actual, expected, type, judged, all, condition) : null;
        }

        if (!Fails(actual, expected) && !Fails(expected, actual))
        {
            return null;
        }

        OriginRelationFact equality = new(use, actual, expected, true, type, RefutesOriginRelation(actual, expected));
        all?.Add(equality);
        return equality;

        bool Fails(BoundOrigin longer, BoundOrigin shorter) => verified
            ? !this.VerifiedRegionOutlives(longer, shorter, use)
            : this.OriginPartFails(longer, shorter, use, judged, condition);
    }

    // SPEC 15.3.6, 15.6.1: a meet at the longer end outlives an Origin exactly when each operand does, so an `outlives` relation names
    // the chain of one failing operand, never the meet itself, and keeps the whole meet for a result bound. With `all`, the chain of
    // every failing operand is added in the meet's order (every failed chain is reported).
    private OriginRelationFact FailedChain(Koto use, BoundOrigin longer, BoundOrigin shorter, BoundType type, bool judged, List<OriginRelationFact>? all = null, ulong condition = 0)
    {
        var operand = this.FailingOperand(longer, shorter, use, judged, true, condition) ?? this.FailingOperand(longer, shorter, use, judged, false, condition) ?? longer;
        OriginRelationFact chain = new(use, operand, shorter, false, type, RefutesOriginRelation(operand, shorter));
        if (all is not null && (ReferenceEquals(operand, longer) || !this.AddFailingChains(use, longer, shorter, type, judged, all, condition)))
        {
            all.Add(chain);
        }

        return chain;
    }

    // Adds the chain of each failing operand of `origin`, nested meets flattened in order; whether any was added.
    private bool AddFailingChains(Koto use, BoundOrigin origin, BoundOrigin shorter, BoundType type, bool judged, List<OriginRelationFact> all, ulong condition = 0)
    {
        if (IsLocalRegion(origin))
        {
            var found = false;
            foreach (var source in this.LocalRegionSources(origin))
            {
                found |= this.AddFailingChains(use, source, shorter, type, judged, all, condition);
            }

            return found;
        }

        if (origin.Kind != OriginKind.Intersection)
        {
            if (!this.OriginPartFails(origin, shorter, use, judged, condition))
            {
                return false;
            }

            all.Add(new(use, origin, shorter, false, type, RefutesOriginRelation(origin, shorter)));
            return true;
        }

        var added = false;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            added |= this.AddFailingChains(use, origin.Operands[i], shorter, type, judged, all, condition);
        }

        return added;
    }

    private bool OriginPartFails(BoundOrigin longer, BoundOrigin shorter, Koto use, bool judged, ulong condition = 0)
        => !this.ProvesOriginOutlives(longer, shorter, use, condition) && (!judged || this.OriginRelationFails(longer, shorter, use, condition));

    // SPEC 15.6.5: the failing operand that the record names follows the judgment, a Refuted chain, which decides the record's code,
    // before an Unknown one, each first in the meet's order; with `refuted`, only a Refuted chain counts.
    private BoundOrigin? FailingOperand(BoundOrigin longer, BoundOrigin shorter, Koto use, bool judged, bool refuted, ulong condition = 0)
    {
        if (IsLocalRegion(longer))
        {
            foreach (var source in this.LocalRegionSources(longer))
            {
                if (this.FailingOperand(source, shorter, use, judged, refuted, condition) is { } failed)
                {
                    return failed;
                }
            }

            return null;
        }

        if (longer.Kind != OriginKind.Intersection)
        {
            return (!refuted || RefutesOriginRelation(longer, shorter)) && this.OriginPartFails(longer, shorter, use, judged, condition) ? longer : null;
        }

        for (var i = 0; i < longer.Operands.Count; i++)
        {
            if (this.FailingOperand(longer.Operands[i], shorter, use, judged, refuted, condition) is { } operand)
            {
                return operand;
            }
        }

        return null;
    }

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

    /// <summary>Fails an operation whose operand Type has no such operator, or a shift whose count is not an integer Type
    /// (SPEC 13.2, 13.3), recording that Type.</summary>
    /// <param name="node">The operation.</param>
    /// <param name="operand">The operand Type as compared, or the count Type.</param>
    /// <param name="failure"><see cref="BindingFailure.NonNumericOperand"/>, <see cref="BindingFailure.NonIntegerOperand"/> or
    /// <see cref="BindingFailure.InvalidShiftCount"/>.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailOperand(Koto node, BoundType operand, BindingFailure failure)
        => this.FailExplained(ref this.operatorOperands, node, failure, operand);

    // SPEC 13.2, 13.3: the operand Type and the operator are the facts. A string operand is told that interpolation joins strings;
    // a shift count is located at the count, and a wrapping count is told to leave Wrapping<U> through U.
    private void ReportOperatorOperand(Koto operation, BoundType operand, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var symbol = operation is BinaryKoto binary ? binary.InfixText.Trim() : ((UnaryKoto)operation).OperatorText;
        if (code == DiagnosticCode.NonNumericOperand_Kd)
        {
            var text = ReferenceTypes.EndsInString(operand);
            operation.Report(requirement, code, DiagnosticTypeName(operand), symbol, note: text ? StringOperatorNote : null);
        }
        else if (code == DiagnosticCode.NonIntegerOperand_Kd)
        {
            operation.Report(requirement, code, DiagnosticTypeName(operand), symbol);
        }
        else
        {
            // SPEC 13.5.4.1: a numeric conversion leaves Wrapping<U> to U itself, which is always exact.
            var integer = operand.IsWrappingInteger ? DiagnosticTypeName(operand.Underlying) : null;
            operation.Report(
                requirement,
                code,
                DiagnosticTypeName(operand),
                symbol,
                note: integer is not null ? "A wrapping integer Type is never a shift count (SPEC 13.3)" : null,
                at: ((BinaryKoto)operation).Right);
        }
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

    // SPEC 3.5, 15.1.5, 23.3.6.9: the acquisition a bare Place needs spelled, located at the Place with the candidate that writes it:
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

    // SPEC 7.6.2: a capture entry initializes its environment binding as `let x = x` or `let x = x@op` would. The report is
    // located at the entry and names the initialization it stands for; a bare entry of a Non-Copy binding offers the transfer
    // and the borrow as candidates (SPEC 23.3.6.9). In a default, the binding of a preceding argument admits neither, nor an
    // exclusive borrow (SPEC 7.2.3), so no candidate is offered.
    private void ReportCaptureEntry(Koto node, CaptureKoto capture, BoundType type, BindingSymbol? source, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var name = capture.Name;
        switch (code)
        {
            case DiagnosticCode.InvalidAssignment_Kd when source is not null && DefaultParameters.InLaterDefault(node, source):
                node.Report(requirement, code, note: $"The capture entry {name}@uniq borrows the slot of the let binding {name} exclusively, as let {name} = {name}@uniq would; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)", evidence: [name], span: capture.Span);
                break;
            case DiagnosticCode.InvalidAssignment_Kd:
                node.Report(requirement, code, note: $"The capture entry {name}@uniq borrows the slot of the let binding {name} exclusively, as let {name} = {name}@uniq would", evidence: [name], span: capture.Span);
                break;
            case DiagnosticCode.TransferRequired_Kd when source is not null && DefaultParameters.InLaterDefault(node, source):
                node.Report(
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type)} is not proven Copy, and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)",
                    evidence: [name],
                    span: capture.Span);
                break;
            case DiagnosticCode.TransferRequired_Kd:
                node.Report(
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type)} is neither proven Copy nor an exclusive reference{this.FailingCaseNote(type, node)}",
                    evidence: [name],
                    span: capture.Span,
                    repairs:
                    [
                        new(RepairKind.Transfer, [name, "the closure's environment"], [node.Edit(new(capture.Span.End, 0), "@move")], RepairConditionSet.Take, RepairConditionSet.UsageLegality),
                        new(RepairKind.Borrow, [name, "the closure's environment"], [node.Edit(new(capture.Span.End, 0), "@ref")], RepairConditionSet.None, RepairConditionSet.UsageLegality),
                    ]);
                break;
            case DiagnosticCode.InvalidCaptureBinding_Kd:
                // SPEC 7.6.2: an explicit capture of self obeys the construction and destruction restrictions.
                node.Report(requirement, code, note: "In a constructor or destructor, self is reached only through its Fields, so a capture entry cannot take self (SPEC 7.6.2)", span: capture.Span);
                break;
            default:
                node.Report(requirement, code, span: capture.Span);
                break;
        }
    }

    // Reports a write failure at its target; an assignment names the target. An
    // Exclusive call of a closure (SPEC 7.6.3) borrows the callee exclusively, so the callee is the written target.
    private void ReportWrite(Koto node, Koto target, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var call = node is InvocationKoto invocation && ReferenceEquals(invocation.Method, target);
        var callee = target.BoundType is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } reference ? reference.Components[0] : target.BoundType;
        var closure = callee?.Symbol?.Declaration as FunctionKoto;
        var objectCallee = call && ((target.BoundType is { Kind: BoundTypeKind.Semantics } layer && IsObjectSemantics(layer.Semantics)) ||
            KotoHelper.UnwrapParentheses(target) is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow });
        var plan = closure?.ClosureStorage;
        var consuming = call && target.BoundType is { } calleeType && this.TryCallable(calleeType, this.ConstraintScope(node), out _, out var calleeReceiver) && calleeReceiver == SemanticsKind.Owner;
        var callNote = !call ? null : consuming ? objectCallee
            ? "The call is Consuming (SPEC 7.6.3): it moves values out of the callee's environment, and an object payload offers no Take (SPEC 13.5.5.1)"
            : "The call is Consuming (SPEC 7.6.3): it moves values out of the callee's environment, which a reference cannot supply, since a callable value is no read Type (SPEC 3.5.3, 7.3)"
            : plan is { ExclusiveReborrow: true, ExclusiveUse: { } use }
            ? $"The call is Exclusive (SPEC 7.6.3): it borrows the callee exclusively, because the callee Reborrows the captured exclusive reference {use} exclusively"
            : "The call is Exclusive (SPEC 7.6.3): it borrows the callee exclusively, because the callee changes its environment or a captured referent";
        if (code != DiagnosticCode.InvalidAssignment_Kd)
        {
            var handle = KotoHelper.UnwrapParentheses(target) is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } followed ? followed.Left.BoundType : target.BoundType;
            var authority = ObjectTypes.HandleMode(handle) is { PayloadAuthority: LoanRequirement.Ref } ? SharedObjectAuthorityNote : null;
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

    // SPEC 4.6.9, 8.4.8.2: a user index publishes its element exclusively only through indexUniq. When the receiver's Type
    // lacks it, the shared layer is the element published by index, and the Note names why indexUniq is absent.
    private string? SharedIndexNote(Koto target)
    {
        if (KotoHelper.UnwrapParentheses(target) is not IndexKoto index || this.exclusiveIndexers.Contains(index) || !ElementAccess.IsUserIndex(index) ||
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
            // Several Key conformances are distinct; the key's own Type selects one (SPEC 4.6.9).
            var name = DiagnosticTypeName(core);
            var registered = this.ConformanceByDeclaration(owner, uniqIndexable, out var byKey) ?? (byKey ? this.ConformanceForKey(owner, uniqIndexable, index.Right) : null);
            return registered is not null ? $"{name} conforms to UniqIndexable only under a condition that does not hold here, so its element is published by index alone, which grants Read only" :
                byKey ? $"{name} has no UniqIndexable conformance for this key, so its element is published by index alone, which grants Read only" :
                $"{name} has no UniqIndexable conformance, so its element is published by index alone, which grants Read only";
        }

        return core is { Kind: BoundTypeKind.Parameter } ? $"{DiagnosticTypeName(core)} is not required to be UniqIndexable here, so its element is published by index alone, which grants Read only" : null;
    }

    // SPEC 11.2: an accessor receiver has the shape of its operation. The report is located at the written receiver Type and
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
        this.caseLimits?.Clear();
        this.caseLimitCauses?.Clear();
        this.adaptationBinders?.Clear();
        this.mismatches?.Clear();
        this.qualificationStops?.Clear();
        this.qualificationFailures?.Clear();
        this.originRelations?.Clear();
        this.callRelations?.Clear();
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

    private (int Start, Koto? Node) BeginConsultation(Koto node)
    {
        var parent = (this.consultationStart, this.consultationNode);
        this.consultationStart = this.consulted.Count;
        this.consultationNode = node;
        return parent;
    }

    private void EndConsultation(Koto node, (int Start, Koto? Node) parent)
    {
        var start = this.consultationStart;
        var count = this.consulted.Count - start;
        if (count != 0)
        {
            if (node.BindingState != BindingState.Resolved)
            {
                this.prerequisites[node] = (this.prerequisiteStore.Count, count);
                for (var i = start; i < start + count; i++)
                {
                    this.prerequisiteStore.Add(this.consulted[i]);
                }
            }

            this.consulted.RemoveRange(start, count);
        }

        (this.consultationStart, this.consultationNode) = parent;
    }

    /// <summary>Records that the node being bound read the result of another node; one that did not resolve becomes a prerequisite.</summary>
    /// <param name="dependency">The node whose result was read.</param>
    private void Consulted(Koto dependency)
    {
        if (this.consultationStart >= 0 && dependency.BindingState != BindingState.Resolved && !ReferenceEquals(dependency, this.consultationNode))
        {
            this.consulted.Add(dependency);
        }
    }

    // A recovery node derives from its syntax error, and so does every failure of a check that consulted a recovery node,
    // directly or through unresolved operands: the parser's guess explains what the check combined (DIAGNOSTICS.md §4.3).
    // A failure that reports missing information is derived when the check consulted prerequisites that stayed unresolved;
    // a failure explained by the Origin rule, or any other definite failure, is direct.
    // A constraint proof that a declaration-level check found failing because a part it read failed, such as the invalid
    // declaration context of a Property (AddPrerequisite), is a consequence of that part.
    private bool IsDerived(Koto node)
        => IsRecovery(node, out _) || this.RestsOnRecovery(node) ||
            (node.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported &&
            this.HasUnresolvedPrerequisite(node)) ||
            (node.BindingFailure == BindingFailure.InvalidOrigin && this.originDeclarations.TryGetValue(node, out var declaration) &&
            declaration.Failure is { } clause && this.partPrerequisites.TryGetValue(node, out var cause) && ReferenceEquals(cause, clause)) ||
            (node.BindingFailure is BindingFailure.InvalidConstraint or BindingFailure.Unsupported && this.partPrerequisites.TryGetValue(node, out var part) && part.BindingState == BindingState.Invalid) ||
            (node.BindingFailure == BindingFailure.InvalidOrigin && node is IsKoto { IsAssociatedConstraint: true } && this.partPrerequisites.TryGetValue(node, out var occurrence) &&
            this.formationCauses?.ContainsKey(occurrence) == true) ||
            (node.BindingFailure == BindingFailure.InvalidAssociatedType && this.partPrerequisites.TryGetValue(node, out var specification) &&
            (specification.BindingFailure != BindingFailure.None || specification.BindingState == BindingState.Invalid)) ||
            (node.BindingFailure is BindingFailure.NoApplicableCandidate or BindingFailure.TypeMismatch && this.partPrerequisites.TryGetValue(node, out var read) &&
            this.formationCauses?.ContainsKey(read) == true);

    // The walk reuses the prerequisite storage of PrerequisiteKeys; both run only at publication.
    private bool RestsOnRecovery(Koto node)
    {
        if (!this.prerequisites.ContainsKey(node) && !this.partPrerequisites.ContainsKey(node))
        {
            return false;
        }

        var visited = this.prerequisiteVisited;
        var pending = this.prerequisitePending;
        visited.Add(node);
        this.PushPrerequisites(node, pending);
        var found = false;
        while (pending.TryPop(out var cause))
        {
            if (!visited.Add(cause))
            {
                continue;
            }

            if (IsRecovery(cause, out _))
            {
                found = true;
                pending.Clear();
                break;
            }

            this.PushPrerequisites(cause, pending);
        }

        visited.Clear();
        return found;
    }

    /// <summary>Records that a declaration-level check read a part outside a consultation frame; the part explains the node's failure when it did not resolve.
    /// The record belongs to the pass, like every other prerequisite.</summary>
    /// <param name="node">The checked node.</param>
    /// <param name="part">The part it read.</param>
    private void AddPrerequisite(Koto node, Koto part)
    {
        // A recovery part explains the failure even when a Type was formed for it (a length slot binds as isize).
        if (IsRecovery(part, out _) || (part.BindingState != BindingState.Resolved && !this.partPrerequisites.ContainsKey(node)))
        {
            this.partPrerequisites[node] = part;
        }
    }

    // A consulted operand can resolve after it was consulted, such as a callee completed by overload selection; only
    // operands still unresolved when Binding ends are prerequisites.
    private bool HasUnresolvedPrerequisite(Koto node)
    {
        if (this.prerequisites.TryGetValue(node, out var range))
        {
            for (var i = range.Start; i < range.Start + range.Count; i++)
            {
                if (this.prerequisiteStore[i].BindingState != BindingState.Resolved)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Gets the check keys that explain why a node did not resolve: its own failure, or what it rests on.</summary>
    /// <param name="node">The unresolved node.</param>
    /// <returns>The keys; the unresolved mark when no cause was recorded.</returns>
    private DiagnosticKey[] CauseKeys(Koto node)
        => !IsRecovery(node, out _) && node.BindingFailure != BindingFailure.None ? [node.KeyOf(DiagnosticRequirement.Binding(node.BindingFailure))] : this.PrerequisiteKeys(node);

    /// <summary>Gets the check keys a derived failure rests on; a skipped prerequisite contributes its own prerequisites.</summary>
    /// <param name="node">The derived node.</param>
    /// <returns>The keys; the unresolved mark when no cause was recorded.</returns>
    private DiagnosticKey[] PrerequisiteKeys(Koto node)
    {
        if (IsRecovery(node, out var own))
        {
            return [own];
        }

        // The walk reuses its storage; only the published array is allocated.
        var keys = this.prerequisiteKeys;
        var visited = this.prerequisiteVisited;
        var pending = this.prerequisitePending;
        visited.Add(node);
        this.PushPrerequisites(node, pending);
        while (pending.TryPop(out var cause))
        {
            if (!visited.Add(cause))
            {
                continue;
            }

            var key = IsRecovery(cause, out var syntax) ? syntax
                : cause.BindingFailure != BindingFailure.None ? cause.KeyOf(DiagnosticRequirement.Binding(cause.BindingFailure))
                : this.PushPrerequisites(cause, pending) ? (DiagnosticKey?)null
                : DiagnosticKey.Unresolved;
            if (key is { } found && !keys.Contains(found))
            {
                keys.Add(found);
            }
        }

        DiagnosticKey[] result = keys.Count == 0 ? [DiagnosticKey.Unresolved] : [.. keys];
        keys.Clear();
        visited.Clear();
        return result;
    }

    private bool PushPrerequisites(Koto node, Stack<Koto> pending)
    {
        var pushed = false;
        if (this.partPrerequisites.TryGetValue(node, out var part) && (part.BindingState != BindingState.Resolved || IsRecovery(part, out _)))
        {
            pending.Push(part);
            pushed = true;
        }

        if (!this.prerequisites.TryGetValue(node, out var range))
        {
            return pushed;
        }

        for (var i = range.Start + range.Count - 1; i >= range.Start; i--)
        {
            if (this.prerequisiteStore[i].BindingState != BindingState.Resolved || IsRecovery(this.prerequisiteStore[i], out _))
            {
                pending.Push(this.prerequisiteStore[i]);
                pushed = true;
            }
        }

        return pushed;
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

    /// <summary>Rejects an object form, creation, cast or runtime test over a Type that opts out of ObjectPayload, naming the declaring Type (SPEC 8.4.7.2).</summary>
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
        => owner is StructKoto or EnumKoto && owner.BindingFailure == BindingFailure.InvalidConstraint &&
            this.constraintDiagnosticCauses?.TryGetValue(owner, out var cause) == true ? cause : null;

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

    // SPEC 23.3.6.4: the first part of a Callable requirement's signature that failed on its own: an unsupported form or a missing
    // name, whose record explains the requirement.
    private Koto? FailedSignaturePart(Koto requirement)
    {
        if (KotoHelper.UnwrapParentheses(requirement) is not GenericsKoto { TypeArguments.Count: > 0 } generic)
        {
            return null;
        }

        return (this.failedSignaturePartVisitor ??= new()).Find(generic.TypeArguments[^1]);
    }

    // SPEC 8.6, 23.3.6.4: a call through F whose only Callable clause failed for a part of its signature rests on that clause.
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
