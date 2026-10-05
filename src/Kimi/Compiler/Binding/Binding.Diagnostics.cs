// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private const string RangeShapeAdvice = "If the function only resolves a range for slicing, accept R with R is PositionRange; if it enumerates, require the matching Iterable, UniqIterable or IntoIterable entry and its Item constraints; if it accesses boundaries, retain the required concrete range Type. Verify the function body after changing its contract";

    // SPEC 11.2: the Advice keeps the written receiver in a function and never proposes another receiver Type.
    private const string AccessorGetterShapeAdvice = "Keep this receiver, its Origins and the body in a function instead, func name(self: R) -> T, named by SPEC 4.7.1: into + noun when it consumes the receiver, verb + noun when it advances state";
    private const string AccessorSetterShapeAdvice = "Keep this receiver, its Origins and the body in a function instead, func name(self: R, value: U) -> (), named by SPEC 4.7.1";

    private const string SharedObjectAuthorityNote = "rc and arc provide shared payload access only; even a strong count of one does not grant objuniq or uniq/Self authority";
    private const string StrongCloneAdvice = "Strong clone accepts only rc or arc handles; obj ownership cannot be duplicated";

    // SPEC 12.3.3, 13.3: string has no arithmetic; an interpolated literal is the one way to join strings, and a buffer builds text.
    private const string StringOperatorNote = "string has no arithmetic operators; an interpolated literal creates an owning string and borrows the values it embeds (SPEC 12.3.3, 13.3)";
    private const string StringBuildingAdvice = "; to build text in steps, write to a Text.HeapBuffer through Text.writer and $tryWrite, then intoString";

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
    private Dictionary<Koto, (CaptureKoto Capture, BoundType Type)>? captureFailures;

    // SPEC 9.6.1: a Type or container named with the wrong number of its own Type arguments, or without the Type arguments of
    // the generic container that declares it (Outer), with the declared and the written counts.
    private Dictionary<Koto, (BindingSymbol Declaration, int Declared, int Written, bool Outer)>? arityFailures;

    private readonly record struct RejectedCandidate(FunctionKoto Function, BoundType? Actual, BoundType? Expected, bool SharedReceiver = false, bool ObjectClone = false, bool CallableSignature = false, bool Selected = false, SemanticsKind? ActualReceiver = null, SemanticsKind? RequiredReceiver = null, bool ReferenceSignature = false, bool UnfixedReference = false);

    // SPEC 15.6.1, 23.3.6.5: the Reason facts, Advice and related locations of an Origin relation record, shared by every phase that
    // reports one; `source` is `fit` for a fit and `wellFormed` for a Type occurrence.
    internal static (object[] Evidence, string Advice, (string Role, Koto At, string? Label)[]? Related) OriginRelationFacts(OriginRelationFact relation, string source)
    {
        // SPEC 15.6.5: a local initialized by a Borrow shows that Borrow, which is also the related location.
        var borrow = BorrowSource(relation.At, relation.Longer);
        var longer = OriginDisplay(relation.Longer, borrow);
        var shorter = OriginDisplay(relation.Shorter, null);
        var operation = relation.Equality ? "==" : "outlives";
        var destination = relation.Destination is not { } type ? $"during {shorter.Text}"
            : ReferenceEquals(type.Origin, relation.Shorter) ? $"{DiagnosticTypeName(type)} during {shorter.Text}" : DiagnosticTypeName(type);
        var advice = OriginRelationAdvice(relation, longer, shorter, source);
        // SPEC 23.3.6.5: a borrow or omitted end is also related at its syntax.
        var longerAt = longer.Kind == "borrow" ? borrow ?? relation.At : longer.Kind == "omitted" ? OmittedAt(relation.Longer) : null;
        var shorterAt = shorter.Kind == "omitted" ? OmittedAt(relation.Shorter) : null;
        (string Role, Koto At, string? Label)[]? related = longerAt is not null && shorterAt is not null ? [("origin", longerAt, null), ("origin", shorterAt, null)]
            : longerAt is not null ? [("origin", longerAt, null)]
            : shorterAt is not null ? [("origin", shorterAt, null)]
            : null;
        return ([operation, longer, shorter, source, destination], advice, related);
    }

    // SPEC 15.6.1: whether a relation is Refuted: its longer end is a body-local finite Origin and its shorter end a fixed one.
    internal static bool RefutesOriginRelation(BoundOrigin longer, BoundOrigin shorter) => BodyLocalOrigin(longer) && FixedOrigin(shorter);

    internal static DiagnosticCode OriginRelationCode(OriginRelationFact relation)
        => relation.Refuted ? DiagnosticCode.UnsatisfiedOriginRelation_Kd : DiagnosticCode.UnprovenOriginRelation_Kd;

    // SPEC 15.6.1: conditional Advice, never a repair candidate. A Refuted relation is not repaired by an annotation; an omitted end is
    // named by a set first; bounding the result is offered only at a result source, and binding to static never for an exclusive input.
    private static string OriginRelationAdvice(OriginRelationFact relation, DiagnosticOrigin longer, DiagnosticOrigin shorter, string source)
    {
        if (relation.Refuted)
        {
            return "Return or store an owned value, or a borrow of an input, instead of a borrow of storage that ends with the body";
        }

        if (OmittedInput(relation.Shorter) is not null || OmittedInput(relation.Longer) is not null)
        {
            return "A Function Type quantifies the Origin of an unnamed borrowed input per call, and no fixed Origin stands for it; write the fixed Origin in that input's Type, as in 'ref/T during x', or accept any borrow there";
        }

        var omitted = longer.Kind == "omitted" ? longer.Text : shorter.Kind == "omitted" ? shorter.Text : null;
        if (omitted is not null)
        {
            return $"Name the omitted Origin with a set on that Type, as in '{omitted}{{name}}', then relate it by that name";
        }

        var inner = relation.Destination is { } type && !ReferenceEquals(type.Origin, relation.Shorter);
        var bound = source == "fit" && IsResultValue(relation.At) ? $", or bound the {(inner ? "inner result" : "result")} by {longer.Text}" : string.Empty;
        var input = OuterInput(relation.Longer);
        var anonymous = relation.Longer is { Kind: OriginKind.Input, Binder: FunctionKoto { IsAnonymous: true } };
        var advice = relation.Equality ? $"Use one Origin at both positions, or bind {shorter.Text} to {longer.Text} where it is introduced"
            : anonymous && relation.Shorter.Kind != OriginKind.Static ? $"An anonymous function has no origin clauses; write the input as '{(input is not null ? $"{input.InternalName}: {WrittenInputType(input)} during {shorter.Text}" : $"during {shorter.Text}")}' so that it accepts only borrows that outlive {shorter.Text}{bound}"
            : relation.Shorter.Kind != OriginKind.Static ? $"If {longer.Text} always outlives {shorter.Text}, add 'origin {longer.Text} outlives {shorter.Text}', which changes the public contract{bound}"
            : input?.Type is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq } ? $"{longer.Text} is an exclusive borrow, which cannot be bound to static; return an owned value instead"
            : $"Bind {longer.Text} to static where it is introduced, as in '{(input is not null ? $"{input.InternalName}: {input.Type} during static" : "during static")}'{bound}";
        return (SimilarNames(relation.Shorter) ?? SimilarNames(relation.Longer)) is { } similar ? advice + similar : advice;
    }

    // SPEC 23.3.6.5: the Type occurrence that an omitted Origin end is related at.
    private static Koto? OmittedAt(BoundOrigin origin)
        => origin.Occurrence ?? OmittedInput(origin) ?? (origin is { Kind: OriginKind.Inference, Binder: TypeKoto occurrence } ? occurrence : null);

    // SPEC 23.3.6.5: the input Type occurrence of a Function Type whose outer Origin is this per-call Origin; it has no name.
    private static Koto? OmittedInput(BoundOrigin origin)
        => origin is { Kind: OriginKind.Input, Occurrence: null, Binder: FunctionTypeKoto binder } && origin.InputIndex >= 0 && origin.InputIndex < InputCount(binder)
            ? InputType(binder, origin.InputIndex) is ParenthesizedTypeKoto { Type: { } inner } ? inner : InputType(binder, origin.InputIndex) : null;

    // A parameter's Type as written, or its bound Type when the anonymous function omitted it.
    private static string WrittenInputType(FunctionParameterKoto input)
        => input.Type is SyntaxFormKoto { Akind: KotoKind.InferredType } && input.Type.BoundType is { } bound ? DiagnosticTypeName(bound) : input.Type.ToString();

    // The parameter whose outer borrow an Input Origin is.
    private static FunctionParameterKoto? OuterInput(BoundOrigin origin)
        => origin is { Kind: OriginKind.Input, Occurrence: null, Binder: FunctionKoto function } && origin.InputIndex >= 0 && origin.InputIndex < function.Parameters.Count
            ? function.Parameters[origin.InputIndex] : null;

    // SPEC 15.6.1: a signature Origin name is introduced by writing it, so a misspelling introduces a new one; similar names visible in
    // the signature, its parameters and the Origins written on their Types and on the result, are shown.
    private static string? SimilarNames(BoundOrigin origin)
    {
        if (origin is not { Kind: OriginKind.Parameter, Binder: FunctionKoto function } || origin.Name.Length < 3)
        {
            return null;
        }

        List<string>? similar = null;
        for (var i = 0; i <= function.Parameters.Count; i++)
        {
            var type = i < function.Parameters.Count ? function.Parameters[i].Type : function.ReturnType;
            if (i < function.Parameters.Count)
            {
                Consider(function.Parameters[i].InternalName);
            }

            for (var depth = 0; depth < 16 && type is TypeSemanticsKoto layer; depth++, type = layer.Type)
            {
                Consider(layer.OriginName);
            }
        }

        return similar is null ? null : $"; {origin.Name} is introduced by this signature, so if {string.Join(" or ", similar)} was meant, write it instead";

        void Consider(string? name)
        {
            if (name is not null && name != origin.Name && !(similar?.Contains(name) ?? false) && EditDistance(name, origin.Name) is var distance &&
                distance <= 2 && distance * 2 < origin.Name.Length)
            {
                (similar ??= new(1)).Add(name);
            }
        }
    }

    private static int EditDistance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2 || a.Length > 64 || b.Length > 64)
        {
            return int.MaxValue;
        }

        Span<int> row = stackalloc int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            row[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }

        return row[b.Length];
    }

    // The value of a return or of an expression body, through parentheses, blocks and if or match arms, is a result source.
    private static bool IsResultValue(Koto at)
    {
        for (Koto? node = at, parent = at.Parent; parent is not null; node = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case ReturnKoto returned:
                    return KotoHelper.ResolveTransferTarget(returned) is FunctionKoto;
                case FunctionKoto function:
                    return ReferenceEquals(function.ExpressionBody, node);
                case ParenthesizedKoto or IfKoto or MatchKoto or CodeBlockKoto:
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

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

    private string? ClosureConversionNote(Koto node, object actual, object expected)
    {
        if (expected is not BoundType { Kind: BoundTypeKind.Function } signature)
        {
            return null;
        }

        // SPEC 10.5, 7.6.4: a generic reference binds its slots from the expected signature, and its Item converts only
        // when its bound arguments are Owned.
        if (actual is BoundType { Kind: BoundTypeKind.FunctionItem, Components.Count: > 0 } item)
        {
            return this.FunctionItemSignature(item) is { } itemSignature && CallableSignatureFits(itemSignature, signature, SignatureOwner(item))
                ? "Common Function conversion requires Owned bound generic arguments; this Item's arguments are not proven Owned"
                : null;
        }

        if (actual is BoundType { Kind: BoundTypeKind.Function } && node.BoundSymbol is { Kind: BindingSymbolKind.Function, Next: null, Declaration: FunctionKoto { GenericArguments.Count: > 0 } })
        {
            return "The generic function's Type parameters are bound from the expected signature without adaptations; no binding fits it, " +
                "or a bound argument fails its Constraints or would hold a per-call Origin of that signature";
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
    }

    /// <summary>Gets final failures that are explained by failed prerequisites; they are reported as derived problems.</summary>
    internal IReadOnlyList<Koto> DerivedIssues => this.derivedIssues;

    // SPEC 15.6.1: the Origin relation that a structurally fitting value leaves at its destination, for phases that judge the fit.
    internal OriginRelationFact? OriginRelationOf(BoundType actual, BoundType expected, Koto at)
        => ReferenceTypes.StorageMatches(expected, actual) && this.FailedOriginRelation(actual, expected, at, false) is { } relation
            ? relation with { At = at, Destination = expected } : null;

    /// <summary>Gets the Binding causes of a node that a later phase checks: none when Binding resolved it or recorded no cause.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The check keys, or <see langword="null"/>.</returns>
    internal DiagnosticKey[]? FailureCauses(Koto node)
        => node.BindingState != BindingState.Resolved && (IsRecovery(node, out _) || node.BindingFailure != BindingFailure.None || this.HasUnresolvedPrerequisite(node))
            ? this.CauseKeys(node) : null;

    // A recovery node, an ErrorKoto or synthesized syntax kept in place of a rejected form, stands for its syntax error.
    private static bool IsRecovery(Koto node, out DiagnosticKey cause)
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

    // SPEC 13.5.4.4: a floating-point value has no bitwise or shift operator, but its bits are an integer of the same width.
    private static string? BitPatternAdvice(BoundType operand, string symbol, KotoKind operation)
    {
        if (operation is KotoKind.Percent or KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement)
        {
            return null;
        }

        var (bits, type) = ReferenceEquals(operand, BoundType.F32) ? ("u32", "f32") : ("u64", "f64");
        return $"If the bit pattern is meant, reinterpret each {type} operand with @bits<{bits}> before applying {symbol}; @bits<{type}> turns resulting bits back into an {type}";
    }

    // A + whose operands are strings or string joins, through parentheses; the depth bound keeps pathological chains cheap.
    private static bool IsStringJoin(Koto node, int depth)
        => depth < 32 && KotoHelper.UnwrapParentheses(node) is PlusKoto join && IsStringOperand(join.Left, depth + 1) && IsStringOperand(join.Right, depth + 1);

    private static bool IsStringOperand(Koto node, int depth)
        => ReferenceTypes.EndsInString(KotoHelper.UnwrapParentheses(node).BoundType) || IsStringJoin(node, depth);

    // The failed node is the innermost join of the chain around it; the whole chain is one interpolated literal.
    private static string StringJoinAdvice(BinaryKoto operation)
    {
        Koto top = operation;
        while (true)
        {
            var parent = top.Parent;
            while (parent is ParenthesizedKoto)
            {
                parent = parent.Parent;
            }

            if (parent is PlusKoto join && IsStringJoin(join, 0))
            {
                top = join;
            }
            else if (parent is PlusEqualsKoto append && ReferenceTypes.EndsInString(append.Left.BoundType))
            {
                return StringAppendAdvice(append); // The chain is the value appended to a string target.
            }
            else
            {
                break;
            }
        }

        var builder = default(IndentedStringBuilder);
        try
        {
            builder.Append("If the strings are to be joined, write the interpolated literal \"");
            AppendJoinSegment(ref builder, top, 0);
            builder.Append("\" in place of ");
            top.WriteTo(ref builder);
            builder.Append(StringBuildingAdvice);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    // target += value becomes the replacement target = "\(target)value" (SPEC 13.7.1), spelled from the written operands.
    private static string StringAppendAdvice(BinaryKoto operation)
    {
        var builder = default(IndentedStringBuilder);
        try
        {
            builder.Append("If text is to be appended to ");
            operation.Left.WriteTo(ref builder);
            builder.Append(", assign a new string: ");
            operation.Left.WriteTo(ref builder);
            builder.Append(" = \"\\(");
            operation.Left.WriteTo(ref builder);
            builder.Append(')');
            AppendJoinSegment(ref builder, operation.Right, 0);
            builder.Append('"');
            builder.Append(StringBuildingAdvice);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    // An escaped or interpolated literal contributes its content as written, a string join its operands, and every other
    // operand an interpolation of its spelling.
    private static void AppendJoinSegment(ref IndentedStringBuilder builder, Koto operand, int depth)
    {
        operand = KotoHelper.UnwrapParentheses(operand);
        if (operand is StringLiteralKoto { IsRaw: false } literal)
        {
            literal.WriteContentTo(ref builder);
        }
        else if (operand is InterpolatedStringKoto interpolated)
        {
            interpolated.WriteContentTo(ref builder);
        }
        else if (depth < 32 && operand is PlusKoto join && IsStringJoin(join, depth))
        {
            AppendJoinSegment(ref builder, join.Left, depth + 1);
            AppendJoinSegment(ref builder, join.Right, depth + 1);
        }
        else
        {
            builder.Append("\\(");
            operand.WriteTo(ref builder);
            builder.Append(')');
        }
    }

    // SPEC 15.6.1, 23.3.6.5: an Origin relation record at the value that supplies the longer end, with both ends as Origin
    // displays, the relation's source and the destination Type, in which only the Origin at the failed position is shown; a
    // borrow end is related at its syntax; the SPEC 15.4.3 elision Note follows an omitted result Origin.
    // SPEC 15.6.1, 23.3.6.5: the Reason names the comparison, the member, the relation and its two ends, rigid symbols of the comparison
    // displayed as the required contract writes them; an omitted end is related at its Type occurrence. Advice only describes a repair.
    private static void ReportOriginContract(Koto node, OriginContractFact contract, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var longer = OriginDisplay(contract.Longer, null);
        var shorter = OriginDisplay(contract.Shorter, null);
        var longerAt = longer.Kind == "omitted" ? OmittedAt(contract.Longer) : null;
        var shorterAt = shorter.Kind == "omitted" ? OmittedAt(contract.Shorter) : null;
        (string Role, Koto At, string? Label)[]? related = longerAt is not null && shorterAt is not null ? [("origin", longerAt, null), ("origin", shorterAt, null)]
            : longerAt is not null ? [("origin", longerAt, null)]
            : shorterAt is not null ? [("origin", shorterAt, null)]
            : null;
        var advice = contract.Member != "the result" ? $"Write {contract.Member} of the required Type over {shorter.Text}, or convert an implementation that accepts any borrow there"
            : longer.Kind == "omitted" ? $"Leave the required result's Origin omitted, which bounds it by the borrowed inputs, or convert an implementation whose result outlives {shorter.Text}"
            : $"Write the required result over {longer.Text}, or convert an implementation whose result outlives {shorter.Text}";
        node.Report(requirement, code, evidence: ["conversion", contract.Member, contract.Equality ? "==" : "outlives", longer, shorter], related: related, advice: advice, at: contract.At);
    }

    private static void ReportOriginRelation(Koto node, OriginRelationFact relation, DiagnosticRequirement requirement, DiagnosticCode code, string? note)
    {
        var (evidence, advice, related) = OriginRelationFacts(relation, "fit");
        node.Report(requirement, code, note: note, evidence: evidence, related: related, advice: advice, at: relation.At);
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

    // The Borrow that supplies a body-local Origin: the value itself, or the initializer of the local it names.
    private static Koto? BorrowSource(Koto value, BoundOrigin origin)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(value);
        if (unwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Borrow })
        {
            return unwrapped;
        }

        return origin.Kind == OriginKind.Projection && unwrapped is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto { InitializerKoto: { } initializer } } } &&
            KotoHelper.UnwrapParentheses(initializer) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } source && ReferenceEquals(source.BoundType?.Origin, origin) ? source : null;
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
    private static DiagnosticOrigin OriginDisplay(BoundOrigin origin, Koto? value)
    {
        switch (origin.Kind)
        {
            case OriginKind.Static:
                return new("expression", "static");
            case OriginKind.Intersection:
                var parts = new string[origin.Operands.Count];
                for (var i = 0; i < parts.Length; i++)
                {
                    parts[i] = OriginDisplay(origin.Operands[i], null).Text;
                }

                return new("expression", "(" + string.Join(" and ", parts) + ")");
            case OriginKind.Input when origin.Occurrence is { } occurrence:
                return SlotProjection(origin, occurrence) is { } projection ? new("expression", projection) : new("omitted", occurrence.ToString());
            case OriginKind.Input when OmittedInput(origin) is { } input:
                return new("omitted", input.ToString());
            case OriginKind.Input when origin.Binder is { } binder && origin.InputIndex >= 0 && origin.InputIndex < InputCount(binder):
                return new("expression", InputName(binder, origin.InputIndex));
            case OriginKind.Parameter:
                return new("expression", origin.Binder is DeclarationContainerKoto ? "self." + origin.Name : origin.Name);
            case OriginKind.Inference when origin.Occurrence is null && origin.Binder is TypeKoto occurrence:
                return new("omitted", occurrence.ToString());
            case OriginKind.Projection or OriginKind.Anchor:
                var text = value is not null && KotoHelper.UnwrapParentheses(value) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow ? borrow.ToString()
                    : origin.Binder is VariableKoto variable ? variable.NameKoto.IdentifierName : origin.Binder?.ToString() ?? origin.Name;
                return new("borrow", text);
            default:
                return new("omitted", origin.Occurrence?.ToString() ?? origin.Name);
        }
    }

    // SPEC 9.6.1: a Type's own Type arguments are written in full, and those of a container are never inferred from call
    // arguments or an expected Type; outside a generic container, a nested declaration is named through that container.
    private static void ReportArity(Koto node, (BindingSymbol Declaration, int Declared, int Written, bool Outer) arity, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var name = arity.Declaration.Name;
        var parameters = arity.Declaration.Declaration is DeclarationContainerKoto container ? string.Join(", ", container.GenericParameterNodes) : string.Empty;
        var declared = $"{name}<{parameters}>";
        var member = node.Parent is MemberAccessKoto access && ReferenceEquals(KotoHelper.UnwrapParentheses(access.Left), node) ? access.Right.ToString() : null;
        string note, advice;
        if (arity.Outer)
        {
            note = $"{node} is declared in {declared}; outside it, the Type arguments of {name} are written on the qualifier and are never inferred (SPEC 9.6.1)";
            advice = $"Name it through {name} with its Type arguments, one for each of {parameters}";
        }
        else if (arity.Written == 0 && member is not null)
        {
            note = $"{declared} is named without its Type arguments; the Type arguments of a container are never inferred from call arguments or an expected Type (SPEC 9.6.1)";
            advice = $"Write them on the qualifier, one for each of {parameters}, as in {name}<...>.{member}";
        }
        else
        {
            note = $"{declared} declares {arity.Declared} Type parameter{(arity.Declared == 1 ? string.Empty : "s")}, and {arity.Written} Type argument{(arity.Written == 1 ? " is" : "s are")} written (SPEC 9.6.1)";
            advice = $"Write exactly one Type argument for each of {parameters}";
        }

        node.Report(requirement, code, note: note, advice: advice, related: [("declaration", arity.Declaration.Declaration, null)]);
    }

    // SPEC 7.6.2: an anonymous function without a capture list never captures contextual self or a setter's value; its use is
    // the location, and the declaration of the binding is related.
    private static void ReportContextualCapture(Koto node, BindingSymbol contextual, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var declaring = contextual.Declaration!;
        var self = contextual.Name == "self";
        var note = self ? "Contextual self is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.6.2)"
            : "A setter's value is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.6.2)";
        var advice = !self ? "Name it in a capture list, as in [value]"
            : declaring is FunctionKoto { IsConstructor: true } or FunctionKoto { IsDestructor: true } ? "Capture the Fields the body needs instead, as in let id = self.id and [id]; in a constructor or destructor, self is reached only through its Fields"
            : "Name it in a capture list, as in [self] or [self@ref]";
        node.Report(requirement, code, note: note, advice: advice, related: [("declaration", declaring is FunctionKoto { Accessor.Declaration: { } accessor } ? accessor : declaring, null)]);
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

    // An untyped literal is described by its category, such as "integer literal".
    private BoundType? FailMismatch(Koto node, Koto at, string actual, string expected)
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

    private BoundType? RecordMismatch(Koto node, Koto at, object actual, object expected)
    {
        // SPEC 10.7, 15.6.1: a common Function conversion compares whole contracts; with matching signatures, its Origin failure is
        // one UnprovenOriginContract_Kd at the converted value, never a Type mismatch or a relation per position.
        if (expected is BoundType { Kind: BoundTypeKind.Function } required && actual is BoundType implementation &&
            this.ConversionSignature(implementation, node, out var signature, out var own) && ReferenceTypes.StorageMatches(required, signature) &&
            this.ConversionContractFailure(signature, required, own, at, node) is { } contract)
        {
            return this.FailExplained(ref this.originContracts, node, BindingFailure.OriginContract, contract);
        }

        if (actual is BoundType actualType && expected is BoundType expectedType && ReferenceTypes.StorageMatches(expectedType, actualType))
        {
            if (this.FailedOriginRelation(actualType, expectedType, node, false, judged: true) is { } relation)
            {
                return this.FailExplained(ref this.originRelations, node, BindingFailure.OriginRelation, relation with { At = at, Destination = expectedType });
            }

            if (this.FailedOriginRelation(actualType, expectedType, node, false) is { } unproven)
            {
                // An Unbound end follows from a failed Origin declaration (SPEC 15.3.2) and stays that failure's derived record.
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
    // finite Origin and the shorter end a fixed one, and Unknown otherwise.
    // With `judged`, only a position that the relation judge fails counts (SPEC 15.6.5), as a body fit judges it; without it, any
    // unproven position does, as for a fit that judges Origins by proof alone.
    private OriginRelationFact? FailedOriginRelation(BoundType actual, BoundType expected, Koto use, bool invariant, int depth = 0, bool judged = false)
    {
        if (depth > 64)
        {
            return null;
        }

        if (actual.Origin is { } longer && expected.Origin is { } shorter && !ReferenceEquals(longer, shorter) &&
            (this.OriginPartFails(longer, shorter, use, judged) || (invariant && this.OriginPartFails(shorter, longer, use, judged))))
        {
            return new(use, longer, shorter, invariant, expected, RefutesOriginRelation(longer, shorter));
        }

        var exclusive = actual.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;
        for (var i = 0; i < actual.Components.Count && i < expected.Components.Count && actual.Kind == BoundTypeKind.Semantics; i++)
        {
            if (this.FailedOriginRelation(actual.Components[i], expected.Components[i], use, invariant || exclusive, depth + 1, judged) is { } target)
            {
                return target;
            }
        }

        for (var i = 0; i < actual.OriginArguments.Count && i < expected.OriginArguments.Count; i++)
        {
            var a = actual.OriginArguments[i];
            var b = expected.OriginArguments[i];
            if (!ReferenceEquals(a, b) && (this.OriginPartFails(a, b, use, judged) || (invariant && this.OriginPartFails(b, a, use, judged))))
            {
                return new(use, a, b, invariant, expected, RefutesOriginRelation(a, b));
            }
        }

        for (var i = 0; i < actual.Components.Count && i < expected.Components.Count && actual.Kind != BoundTypeKind.Semantics; i++)
        {
            if (this.FailedOriginRelation(actual.Components[i], expected.Components[i], use, invariant, depth + 1, judged) is { } argument)
            {
                return argument;
            }
        }

        return null;
    }

    private bool OriginPartFails(BoundOrigin longer, BoundOrigin shorter, Koto use, bool judged)
        => !this.ProvesOriginOutlives(longer, shorter, use) && (!judged || this.OriginRelationFails(longer, shorter, use));

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

    // SPEC 13.2, 13.3: the operand Type and the operator are the facts. A string operand is told that interpolation joins strings,
    // and + or += whose other operand is a string gets the literal that joins the same operands in the same order as Advice. A
    // floating-point operand of a bit operator is told how to reach its bits; a shift count is located at the count, and a
    // wrapping count is told to leave Wrapping<U> through U.
    private void ReportOperatorOperand(Koto operation, BoundType operand, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var symbol = operation is BinaryKoto binary ? binary.InfixText.Trim() : ((UnaryKoto)operation).OperatorText;
        if (code == DiagnosticCode.NonNumericOperand_Kd)
        {
            var text = ReferenceTypes.EndsInString(operand);
            var advice = !text ? null
                : operation.Akind == KotoKind.Plus && IsStringJoin(operation, 0) ? StringJoinAdvice((BinaryKoto)operation)
                : operation.Akind == KotoKind.PlusEquals && IsStringOperand(((BinaryKoto)operation).Right, 0) ? StringAppendAdvice((BinaryKoto)operation)
                : null;
            operation.Report(requirement, code, DiagnosticTypeName(operand), symbol, note: text ? StringOperatorNote : null, advice: advice);
        }
        else if (code == DiagnosticCode.NonIntegerOperand_Kd)
        {
            // A compound form applies its operator to the reinterpreted value; the assignment is not part of the advice.
            var compound = operation.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
            var kind = compound ? KotoHelper.CompoundOperation(operation.Akind) : operation.Akind;
            operation.Report(requirement, code, DiagnosticTypeName(operand), symbol, advice: BitPatternAdvice(operand, compound ? symbol[..^1] : symbol, kind));
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
                advice: integer is not null ? $"Convert the count to {integer} with @{integer}" : null,
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

    // SPEC 3.5, 15.1.5, 23.3.6.9: the Advice of a bare Place that needs @move states what the Transfer candidate cannot: the borrow
    // alternative where a reference may be meant, or, for a Place without Take, the alternatives that remain.
    internal const string NoTakeAdvice = "This Place offers no Take and cannot be transferred; borrow it with @ref or @uniq instead";
    internal const string BorrowAlternativeAdvice = "Borrow it with @ref or @uniq instead when a reference is meant";

    /// <summary>Gets the Advice of a Place that needs @move: nothing for a call argument, whose position acquires by value for every candidate (SPEC 7.3.1).</summary>
    /// <param name="place">The Place.</param>
    /// <param name="judgment">The Take judgment.</param>
    /// <returns>The Advice, or <see langword="null"/>.</returns>
    internal static string? TransferAdvice(Koto place, AcquisitionJudgment judgment)
        => judgment == AcquisitionJudgment.Refuted ? NoTakeAdvice : Destination(place) is InvocationKoto call && !ReferenceEquals(KotoHelper.UnwrapParentheses(call.Method), KotoHelper.UnwrapParentheses(place)) ? null : BorrowAlternativeAdvice;

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
        node.Report(requirement, code, note: this.BorrowOriginHint(node), at: place, evidence: [text], advice: TransferAdvice(place, judgment), repairs: judgment == AcquisitionJudgment.Refuted ? null : TransferRepair(node, place, judgment));
    }

    // SPEC 7.6.2: a capture entry initializes its environment binding as `let x = x` or `let x = x@op` would. The report is
    // located at the entry and names the initialization it stands for; a bare entry of a Non-Copy binding offers the transfer
    // and the borrow as candidates (SPEC 23.3.6.9), so no Advice repeats them.
    private void ReportCaptureEntry(Koto node, CaptureKoto capture, BoundType type, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var name = capture.Name;
        switch (code)
        {
            case DiagnosticCode.InvalidAssignment_Kd:
                node.Report(requirement, code, note: $"The capture entry {name}@uniq borrows the slot of the let binding {name} exclusively, as let {name} = {name}@uniq would", evidence: [name], advice: "Declare the binding with var, or capture it with @ref when shared access suffices", span: capture.Span);
                break;
            case DiagnosticCode.TransferRequired_Kd:
                node.Report(
                    requirement,
                    code,
                    note: $"The bare capture entry {name} initializes its environment binding as let {name} = {name} would; {DiagnosticTypeName(type)} is neither proven Copy nor an exclusive reference",
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
                node.Report(requirement, code, note: "In a constructor or destructor, self is reached only through its Fields, so a capture entry cannot take self (SPEC 7.6.2)", advice: "Capture the Fields the body needs instead, as in let id = self.id and [id]", span: capture.Span);
                break;
            default:
                node.Report(requirement, code, span: capture.Span);
                break;
        }
    }

    // Reports a write failure at its target; an assignment names the target, and a let root gets conditional advice. An
    // Exclusive call of a closure (SPEC 7.6.3) borrows the callee exclusively, so the callee is the written target.
    private void ReportWrite(Koto node, Koto target, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (code != DiagnosticCode.InvalidAssignment_Kd)
        {
            var note = code != DiagnosticCode.SharedPathAccess_Kd ? null : ObjectTypes.HandleMode(target.BoundType) is { PayloadAuthority: LoanRequirement.Ref } ? SharedObjectAuthorityNote : this.SharedIndexNote(target);
            node.Report(requirement, code, note: note, at: target, evidence: code == DiagnosticCode.SharedPathAccess_Kd ? [target.ToString()] : null);
            return;
        }

        var root = KotoHelper.UnwrapParentheses(target);
        while (root is MemberAccessKoto or IndexKoto)
        {
            root = KotoHelper.UnwrapParentheses(root is MemberAccessKoto member ? member.Left : ((IndexKoto)root).Left);
        }

        var immutable = root is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto { VariableKind: VariableKind.Let } } };
        var call = node is InvocationKoto invocation && ReferenceEquals(invocation.Method, target);
        var callee = target.BoundType is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } reference ? reference.Components[0] : target.BoundType;
        var plan = (callee?.Symbol?.Declaration as FunctionKoto)?.ClosureStorage;
        var shared = target.BoundType?.Semantics == SemanticsKind.Ref;
        var callNote = !call ? null : plan is { ExclusiveReborrow: true, ExclusiveUse: { } use }
            ? $"The call is Exclusive (SPEC 7.6.3): it borrows the callee exclusively, because the callee Reborrows the captured exclusive reference {use} exclusively"
            : "The call is Exclusive (SPEC 7.6.3): it borrows the callee exclusively, because the callee changes its environment or a captured referent";
        var callAdvice = call && shared ? "A ref/F value cannot supply an Exclusive call; call the closure through its own var binding or a uniq/F borrow"
            : immutable ? call ? "Declare the binding with var to call it" : "Declare the binding with var to assign it again" : null;
        node.Report(requirement, code, note: callNote, at: target, evidence: [target.ToString()], advice: callAdvice);
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
            advice: getter ? AccessorGetterShapeAdvice : AccessorSetterShapeAdvice,
            related: [("property", property.NameKoto, $"{property.NameKoto.IdentifierName} is read through ref/Self and written through uniq/Self")]);
    }

    private void ResetPrerequisites()
    {
        this.mismatches?.Clear();
        this.originRelations?.Clear();
        this.originContracts?.Clear();
        this.operatorOperands?.Clear();
        this.rangeIterationFailures?.Clear();
        this.writeTargets?.Clear();
        this.captureFailures?.Clear();
        this.arityFailures?.Clear();
        this.rejectedCandidates?.Clear();
        this.acquisitionPlaces?.Clear();
        this.ResetParameterShapes();
        this.duplicateDeclarations?.Clear();
        this.prerequisites.Clear();
        this.partPrerequisites.Clear();
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
            this.HasUnresolvedPrerequisite(node) && this.BorrowOriginHint(node) is null) ||
            (node.BindingFailure == BindingFailure.InvalidConstraint && this.partPrerequisites.TryGetValue(node, out var part) && part.BindingState == BindingState.Invalid);

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
    // requirement read through a generic receiver (P24).
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

        var visitor = this.failedSignaturePartVisitor ??= new();
        visitor.Visit(generic.TypeArguments[^1]);
        var part = visitor.Part;
        visitor.Part = null;
        return part;
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
        internal Koto? Part { get; set; }

        public override void Visit(Koto node)
        {
            if (this.Part is not null)
            {
                return;
            }

            if (node.BindingFailure is BindingFailure.Unsupported or BindingFailure.MissingName or BindingFailure.MissingType)
            {
                this.Part = node;
                return;
            }

            node.VisitChildren(this);
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
