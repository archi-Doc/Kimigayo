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
    private int consultationStart = -1;
    private Koto? consultationNode;

    // The expression being bound without its expected Type because the syntax that supplies it failed.
    private (Koto Expression, Koto Cause)? missingExpectation;

    // SPEC 23.3.6.2: the Types a mismatch compared and the syntax that shows it, recorded only when a check fails.
    private Dictionary<Koto, (Koto At, object Actual, object Expected)>? mismatches;

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

    private readonly record struct RejectedCandidate(FunctionKoto Function, BoundType? Actual, BoundType? Expected);

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

    /// <summary>Gets final failures that are explained by failed prerequisites; they are reported as derived problems.</summary>
    internal IReadOnlyList<Koto> DerivedIssues => this.derivedIssues;

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

    private BoundType? RecordMismatch(Koto node, Koto at, object actual, object expected)
    {
        // Keep semantic identities even when their short names agree. Format only at publication, never during Binding.
        if (node.BindingFailure == BindingFailure.None)
        {
            (this.mismatches ??= new(ReferenceEqualityComparer.Instance))[node] = (at, actual, expected);
        }

        return this.Fail(node, BindingFailure.TypeMismatch);
    }

    /// <summary>Fails an operation whose operand Type has no such operator, or a shift whose count is not an integer Type
    /// (SPEC 13.2, 13.3), recording that Type.</summary>
    /// <param name="node">The operation.</param>
    /// <param name="operand">The operand Type as compared, or the count Type.</param>
    /// <param name="failure"><see cref="BindingFailure.NonNumericOperand"/>, <see cref="BindingFailure.NonIntegerOperand"/> or
    /// <see cref="BindingFailure.InvalidShiftCount"/>.</param>
    /// <returns><see langword="null"/>.</returns>
    private BoundType? FailOperand(Koto node, BoundType operand, BindingFailure failure)
    {
        if (node.BindingFailure == BindingFailure.None)
        {
            (this.operatorOperands ??= new(ReferenceEqualityComparer.Instance))[node] = operand;
        }

        return this.Fail(node, failure);
    }

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
    {
        if (node.BindingFailure == BindingFailure.None)
        {
            (this.writeTargets ??= new(ReferenceEqualityComparer.Instance))[node] = target;
        }

        return this.Fail(node, AccessFailure(target));
    }

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

    private void NoteAcquisition(Koto node, Koto place, bool @object = false)
        => (this.acquisitionPlaces ??= new(ReferenceEqualityComparer.Instance))[node] = (place, @object);

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
            node.Report(requirement, code, note: code == DiagnosticCode.SharedPathAccess_Kd ? this.SharedIndexNote(target) : null, at: target, evidence: code == DiagnosticCode.SharedPathAccess_Kd ? [target.ToString()] : null);
            return;
        }

        var root = KotoHelper.UnwrapParentheses(target);
        while (root is MemberAccessKoto or IndexKoto)
        {
            root = KotoHelper.UnwrapParentheses(root is MemberAccessKoto member ? member.Left : ((IndexKoto)root).Left);
        }

        var immutable = root is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto { VariableKind: VariableKind.Let } } };
        var call = node is InvocationKoto invocation && ReferenceEquals(invocation.Method, target);
        node.Report(
            requirement,
            code,
            note: call ? "The call is Exclusive (SPEC 7.6.3): it borrows the callee exclusively, because the callee changes its environment or a captured referent" : null,
            at: target,
            evidence: [target.ToString()],
            advice: immutable ? call ? "Declare the binding with var to call it" : "Declare the binding with var to assign it again" : null);
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
        this.operatorOperands?.Clear();
        this.rangeIterationFailures?.Clear();
        this.writeTargets?.Clear();
        this.captureFailures?.Clear();
        this.rejectedCandidates?.Clear();
        this.acquisitionPlaces?.Clear();
        this.ResetParameterShapes();
        this.duplicateDeclarations?.Clear();
        this.prerequisites.Clear();
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
    private bool IsDerived(Koto node)
        => IsRecovery(node, out _) || this.RestsOnRecovery(node) ||
            (node.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType or BindingFailure.Unsupported &&
            this.HasUnresolvedPrerequisite(node) && this.BorrowOriginHint(node) is null);

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
    /// Declaration checks run once per Bind, so the record outlives the per-pass prerequisite storage.</summary>
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
