// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 4.6.2, 4.6.3: prefix ^ and range syntax construct FromEnd<T>, Range<S, E> and ClosedRange<S, E> through the
    // internal Kimi group PositionSyntax. Each synthesized call pins its target declaration, so neither a same-named source
    // declaration nor the source's access rules affect it; its arguments are the source operands, bound beforehand under the
    // ordinary rules. A key applied directly (below) constructs no value and has no call.
    private readonly Dictionary<Koto, InvocationKoto> rangeCalls = new(ReferenceEqualityComparer.Instance);

    // SPEC 4.6.4, 4.6.9: a position or range key of a built-in selection that is not applied directly resolves against the
    // receiver's length by a synthesized PositionSyntax.resolved<P> or resolvedRange<R> call whose length argument reads
    // the receiver the selection evaluated (an EvaluatedKoto), so any receiver is evaluated once. A failed resolution
    // yields a value that the selection's own bounds check rejects. A ResolvedRange key is applied as written and rechecked.
    private readonly Dictionary<IndexKoto, InvocationKoto> resolvedKeys = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IndexKoto> resolvedSlices = new(ReferenceEqualityComparer.Instance);

    private BoundType ResolvedRangeType => this.InternType(BoundTypeKind.Nominal, this.Library.ResolvedRange, SemanticsKind.Owner, []);

    private BoundType StartType => this.InternType(BoundTypeKind.Nominal, this.Library.Start, SemanticsKind.Owner, []);

    private BoundType EndType => this.InternType(BoundTypeKind.Nominal, this.Library.End, SemanticsKind.Owner, []);

    /// <summary>Gets the synthesized construction of a prefix <c>^</c> or range value, or null for a directly applied key.</summary>
    /// <param name="node">The <c>^</c> or range expression.</param>
    /// <returns>The bound call, or null for a directly applied key or an unbound expression.</returns>
    internal InvocationKoto? RangeValueCall(Koto node)
    {
        var syntax = KotoHelper.UnwrapParentheses(node);
        return syntax is RangeKoto or FromEndIndexKoto && syntax.BindingState == BindingState.Resolved &&
            this.rangeCalls.TryGetValue(syntax, out var call) && call.BindingState == BindingState.Resolved ? call : null;
    }

    /// <summary>Gets the synthesized resolution call of a position or range key that is not applied directly, or null.</summary>
    /// <param name="node">The selection expression.</param>
    /// <returns>The bound call, or null.</returns>
    internal InvocationKoto? ResolvedKeyCall(Koto node)
        => node is IndexKoto index && index.BindingState == BindingState.Resolved && this.resolvedKeys.TryGetValue(index, out var call) && call.BindingState == BindingState.Resolved ? call : null;

    /// <summary>Gets a value indicating whether the selection applies one ResolvedRange value, written or resolved from a range key.</summary>
    /// <param name="node">The selection expression.</param>
    /// <returns>Whether the selection is a resolved range selection.</returns>
    internal bool IsResolvedSlice(Koto node) => node is IndexKoto index && index.BindingState == BindingState.Resolved && this.resolvedSlices.Contains(index);

    private static void ResetSynthetic(Koto node)
    {
        node.BindingState = BindingState.Unvisited;
        node.BoundType = null;
        node.BoundSymbol = null;
        node.BindingFailure = BindingFailure.None;
    }

    // SPEC 4.6.2: `^a` whose operand is literal-only (SPEC 12.3.1).
    private static bool IsLiteralOnlyFromEnd(Koto node) => node is FromEndIndexKoto fromEnd && IsIntegerLiteralOnly(fromEnd.Operand);

    // A literal-only integer or `^a` position.
    private static bool IsLiteralOnlyPosition(Koto node)
    {
        node = KotoHelper.UnwrapParentheses(node);
        return IsIntegerLiteralOnly(node) || IsLiteralOnlyFromEnd(node);
    }

    private BoundType FromEndType(BoundType offset) => this.InternType(BoundTypeKind.Constructed, this.Library.FromEnd, SemanticsKind.Owner, [offset]);

    // The T of FromEnd<T>, or null for any other Type.
    private BoundType? FromEndOffsetType(BoundType? type)
        => type is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && ReferenceEquals(type.Symbol, this.Library.FromEnd) ? type.Components[0] : null;

    // Whether the Type is one that prefix ^ or range syntax constructs, so a literal-only expression may fit it.
    private bool IsSyntaxPositionType(BoundType type)
        => type.Kind == BoundTypeKind.Constructed && (ReferenceEquals(type.Symbol, this.Library.FromEnd) || ReferenceEquals(type.Symbol, this.Library.Range) || ReferenceEquals(type.Symbol, this.Library.ClosedRange));

    // SPEC 12.3.1: the default Type of a literal-only `^a` or range: i32 boundaries and offsets, Start and End for omitted ones.
    private BoundType? LiteralPositionDefault(Koto node) => node switch
    {
        FromEndIndexKoto => this.FromEndType(BoundType.I32),
        RangeKoto range => this.InternType(
            BoundTypeKind.Constructed,
            range.IsInclusive ? this.Library.ClosedRange : this.Library.Range,
            SemanticsKind.Owner,
            [this.LiteralBoundaryDefault(range.Start, this.StartType), this.LiteralBoundaryDefault(range.End, this.EndType)]),
        _ => null,
    };

    private BoundType LiteralBoundaryDefault(Koto? boundary, BoundType omitted)
        => boundary is null ? omitted : KotoHelper.UnwrapParentheses(boundary) is FromEndIndexKoto ? this.FromEndType(BoundType.I32) : BoundType.I32;

    // SPEC 4.6.3.1, 12.3.1: whether a literal-only `^a` or range fits a candidate Type: a range fits the Type of its shape
    // when each written boundary fits its S or E and each omitted one is Start or End.
    private bool FitsLiteralPosition(Koto node, BoundType type, BindingScope scope) => node switch
    {
        FromEndIndexKoto fromEnd => this.FromEndOffsetType(type) is { } offset && this.FitsInputLiteral(fromEnd.Operand, offset, scope),
        RangeKoto range => type is { Kind: BoundTypeKind.Constructed, Components.Count: 2 } && ReferenceEquals(type.Symbol, range.IsInclusive ? this.Library.ClosedRange : this.Library.Range) &&
            (range.Start is null ? ReferenceEquals(type.Components[0], this.StartType) : this.FitsInputLiteral(range.Start, type.Components[0], scope)) &&
            (range.End is null ? ReferenceEquals(type.Components[1], this.EndType) : this.FitsInputLiteral(range.End, type.Components[1], scope)),
        _ => false,
    };

    // Synthesized calls and Places are reused across passes. Each pass rebinds the ones its syntax still needs, so one left
    // by an earlier pass, such as after an edit changed its node's role, is never Resolved for a lookup.
    private void ResetSyntheticCalls()
    {
        foreach (var call in this.propertyCalls.Values)
        {
            ResetSynthetic(call);
        }

        foreach (var call in this.indexerCalls.Values)
        {
            ResetSynthetic(call);
        }

        foreach (var call in this.viewRangeCalls.Values)
        {
            ResetSynthetic(call);
        }

        foreach (var call in this.rangeCalls.Values)
        {
            ResetSynthetic(call);
        }

        foreach (var call in this.resolvedKeys.Values)
        {
            ResetSynthetic(call);
        }

        foreach (var projection in this.storageProjections.Values)
        {
            ResetSynthetic(projection);
        }

        foreach (var storage in this.propertyUpdateStorage.Values)
        {
            ResetSynthetic(storage);
        }

        this.resolvedSlices.Clear();
        this.exclusiveIndexers.Clear();
        this.positionWarnings.Clear();
    }

    // SPEC 4.6.1, 4.6.9: the key of a fixed-array, Slice or Array selection. An integer key of any PrimitiveInteger Type and
    // a written `^x` are applied directly by the element paths of the caller (false with no result); a range whose written
    // boundaries are integers or `^x` is applied directly here; any other position or range key is resolved by a
    // synthesized call. Returns false without binding for other receivers.
    private bool TryBindKeyedSelection(IndexKoto source, BindingScope scope, BoundType receiver, out BoundType? result)
    {
        result = null;
        this.resolvedSlices.Remove(source);
        var core = receiver is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ? receiver.Components[0] : receiver;
        if (core.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array))
        {
            return false;
        }

        var element = core.Components[0];
        BoundType? key;
        if (source.Right is RangeKoto range)
        {
            var direct = this.BindDirectRange(range, scope, out var failed);
            if (failed)
            {
                result = Complete(source, null);
                return true;
            }

            this.CheckLiteralKey(source, core);
            if (direct)
            {
                this.SharedElementView(source.Left);
                result = Complete(source, this.InternType(BoundTypeKind.Slice, null, SemanticsKind.Owner, [element], origin: this.PlaceOrigin(source.Left)));
                return true;
            }

            key = this.BindRangeValue(range, scope, null);
        }
        else if (source.Right is FromEndIndexKoto fromEnd)
        {
            // SPEC 4.6.9: `^x` resolves as `length - x` within the element access's one bounds check.
            var offset = this.FromEndOffset(fromEnd, scope, null);
            if (offset is null)
            {
                Complete(fromEnd, null);
                result = Complete(source, null);
                return true;
            }

            Complete(fromEnd, ReferenceEquals(offset, BoundType.Never) ? BoundType.Never : this.FromEndType(offset));
            this.CheckLiteralKey(source, core);
            return false;
        }
        else
        {
            key = this.BindPositionKey(source.Right, scope);
            if (key is not null && (ReferenceEquals(key, BoundType.Never) || this.IsIntegerOperand(key, scope)))
            {
                this.CheckLiteralKey(source, core);
                return false; // SPEC 4.6.9: an integer key is converted to an isize position by the element access.
            }
        }

        if (key is null)
        {
            result = Complete(source, null);
            return true;
        }

        if (source.Right is not RangeKoto)
        {
            this.CheckLiteralKey(source, core); // A parenthesized `^a` resolves through a call.
        }

        var resolved = ReferenceTypes.IsResolvedRange(key);
        var position = !resolved && this.ProvesClosedContract(key, this.Library.Position, scope);
        if (!resolved && !position && !this.ProvesClosedContract(key, this.Library.PositionRange, scope))
        {
            this.RecordMismatch(source.Right, source.Right, key, "a Type proven Position or PositionRange");
            result = Complete(source, null);
            return true;
        }

        if (!resolved && this.ResolveKeyCall(source, scope, key, !position) is null)
        {
            result = Complete(source, null);
            return true;
        }

        if (position)
        {
            if (receiver is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner })
            {
                this.ReceiverElement(source.Left, receiver);
            }

            result = Complete(source, element);
            return true;
        }

        this.resolvedSlices.Add(source);
        this.SharedElementView(source.Left);
        result = Complete(source, this.InternType(BoundTypeKind.Slice, null, SemanticsKind.Owner, [element], origin: this.PlaceOrigin(source.Left)));
        return true;
    }

    // SPEC 4.6.1, 4.6.9: a key binds without an expected Type, so a literal-only key is i32; a reference to a read Type is
    // read as its value (SPEC 3.5.3), whose Type is returned.
    private BoundType? BindPositionKey(Koto key, BindingScope scope)
    {
        var type = this.BindNode(key, scope);
        return type is null || ReferenceEquals(type, BoundType.Never) ? type : this.ReadReferent(key, type);
    }

    private InvocationKoto? ResolveKeyCall(IndexKoto source, BindingScope scope, BoundType key, bool range)
    {
        // A cached call is reused only while it names the current key and receiver; an edit may replace either.
        if (!this.resolvedKeys.TryGetValue(source, out var call) || !ReferenceEquals(call.ArgumentNodes[0], source.Right) ||
            !ReferenceEquals(((EvaluatedKoto)((MemberAccessKoto)call.ArgumentNodes[1]).Left).Source, source.Left))
        {
            // The length is read from the receiver the selection evaluates, never from a second evaluation of its syntax.
            var receiver = new EvaluatedKoto(source.Left);
            var length = new MemberAccessKoto(source, receiver, new IdentifierNameKoto(source, "length"));
            receiver.Parent = length;
            var callee = new SyntheticKoto(source) { Parent = source };
            call = new InvocationKoto(source, new GenericsKoto(source, callee, [new SyntheticKoto(source) { Parent = source }]), [source.Right, length]);
            this.resolvedKeys[source] = call;
        }

        var generic = (GenericsKoto)call.Method;
        var lengthArgument = (MemberAccessKoto)call.ArgumentNodes[1];
        ((SyntheticKoto)generic.Identifier!).Resolve(this.PositionSyntaxMember(range ? "resolvedRange" : "resolved"), null, null);
        ((SyntheticKoto)generic.TypeArguments[0]).Resolve(null, key, null);
        ResetSynthetic(call);
        ResetSynthetic(generic);
        ResetSynthetic(lengthArgument);
        ResetSynthetic(lengthArgument.Left);
        ResetSynthetic(lengthArgument.Right);
        return this.BindCall(call, scope, null) is null ? null : call;
    }

    // SPEC 4.6.4: a range key whose written boundaries are integers of any PrimitiveInteger Type, read through references,
    // or `^x` with such an operand, is applied directly: each boundary becomes an isize position without a range value.
    // Literal-only boundaries take the other boundary's integer Type, else i32 (SPEC 4.6.3.1). Returns false, with
    // `failed` unset, when a boundary has another Position Type, so the range is constructed instead.
    private bool BindDirectRange(RangeKoto range, BindingScope scope, out bool failed)
    {
        failed = false;
        if (range.IsInclusive && range.End is null)
        {
            this.Fail(range, BindingFailure.TypeMismatch); // SPEC 4.6.3.1: an inclusive end cannot be omitted.
            failed = true;
            return false;
        }

        var start = this.DirectBoundary(range.Start, scope, out var startDirect, out var startFailed);
        var end = this.DirectBoundary(range.End, scope, out var endDirect, out var endFailed);
        if (startFailed || endFailed || (startDirect && endDirect && (!this.FitDirectBoundary(range.Start, scope, end) || !this.FitDirectBoundary(range.End, scope, start))))
        {
            Complete(range, null);
            failed = true;
            return false;
        }

        if (!startDirect || !endDirect)
        {
            return false;
        }

        Complete(range, BoundType.Range);
        return true;
    }

    // A written boundary of a directly applied range: the established integer Type of the boundary or of its `^` operand, or
    // null when it is omitted or literal-only. `direct` is false for a boundary of another Position Type. A `^x` node is
    // completed only once the range is known to be direct, since a constructed range constructs it through a call.
    private BoundType? DirectBoundary(Koto? boundary, BindingScope scope, out bool direct, out bool failed)
    {
        direct = true;
        failed = false;
        if (boundary is not null and not FromEndIndexKoto && KotoHelper.UnwrapParentheses(boundary) is FromEndIndexKoto)
        {
            // A parenthesized `^x` is constructed as a value: the direct application reads only a written `^x` operand.
            direct = false;
            return null;
        }

        if (boundary is null || IsUnfittedLiteral(boundary))
        {
            return null;
        }

        var fromEnd = boundary as FromEndIndexKoto;
        if (fromEnd is not null && IsUnfittedLiteral(fromEnd.Operand))
        {
            return null;
        }

        var type = this.BindPositionKey(fromEnd?.Operand ?? boundary, scope);
        if (type is null)
        {
            failed = true;
            return null;
        }

        if (ReferenceEquals(type, BoundType.Never))
        {
            return null;
        }

        if (this.IsIntegerOperand(type, scope))
        {
            return type;
        }

        if (fromEnd is not null)
        {
            this.Fail(boundary, BindingFailure.TypeMismatch); // SPEC 4.6.2: `^x` needs an integer operand.
            failed = true;
        }

        direct = false;
        return null;
    }

    private bool FitDirectBoundary(Koto? boundary, BindingScope scope, BoundType? other)
    {
        if (boundary is null)
        {
            return true;
        }

        if (boundary is FromEndIndexKoto fromEnd)
        {
            var offset = IsUnfittedLiteral(fromEnd.Operand) ? this.RequireType(fromEnd.Operand, scope, other ?? BoundType.I32) : this.BindPositionKey(fromEnd.Operand, scope);
            Complete(fromEnd, offset is null ? null : ReferenceEquals(offset, BoundType.Never) ? BoundType.Never : this.FromEndType(offset));
            return offset is not null;
        }

        return !IsUnfittedLiteral(boundary) || this.RequireType(boundary, scope, other ?? BoundType.I32) is not null;
    }

    // SPEC 4.6.3: a range value. Its shape is fixed by the syntax; each written boundary is a value of a Type satisfying
    // Position, known independently of the context, and a literal-only boundary takes the S or E of an expected range of
    // the same shape, else the integer Type of the other boundary, else i32 (SPEC 4.6.3.1). Construction checks nothing.
    private BoundType? BindRangeValue(RangeKoto range, BindingScope scope, BoundType? expected)
    {
        if (range.IsInclusive && range.End is null)
        {
            return this.Fail(range, BindingFailure.TypeMismatch); // SPEC 4.6.3.1: an inclusive end cannot be omitted.
        }

        var shape = range.IsInclusive ? this.Library.ClosedRange : this.Library.Range;
        var fitted = expected is { Kind: BoundTypeKind.Constructed, Components.Count: 2 } && ReferenceEquals(expected.Symbol, shape) ? expected : null;
        var start = this.IndependentBoundary(range.Start, scope, out var startInteger, out var startFailed);
        var end = this.IndependentBoundary(range.End, scope, out var endInteger, out var endFailed);
        if (startFailed || endFailed)
        {
            return Complete(range, null);
        }

        start = range.Start is null ? this.StartType : start ?? this.FitRangeBoundary(range.Start, scope, fitted?.Components[0], endInteger);
        end = range.End is null ? this.EndType : end ?? this.FitRangeBoundary(range.End, scope, fitted?.Components[1], startInteger);
        if (start is null || end is null)
        {
            return Complete(range, null);
        }

        if (ReferenceEquals(start, BoundType.Never) || ReferenceEquals(end, BoundType.Never))
        {
            return Complete(range, BoundType.Never);
        }

        var type = this.InternType(BoundTypeKind.Constructed, shape, SemanticsKind.Owner, [start, end]);
        var call = range.IsInclusive
            ? range.Start is null ? this.PositionSyntaxCall(range, "upTo", end, null, range.End, null) : this.PositionSyntaxCall(range, "through", start, end, range.Start, range.End)
            : range.Start is not null && range.End is not null ? this.PositionSyntaxCall(range, "between", start, end, range.Start, range.End)
            : range.Start is not null ? this.PositionSyntaxCall(range, "from", start, null, range.Start, null)
            : range.End is not null ? this.PositionSyntaxCall(range, "to", end, null, range.End, null)
            : this.PositionSyntaxCall(range, "all", null, null, null, null);
        return this.BindCall(call, scope, null) is null ? Complete(range, null) : Complete(range, type);
    }

    // The Type of a written range boundary known independently of the context, read through references; null for an
    // omitted or literal-only boundary. `integer` is the integer Type a literal-only other boundary takes from it.
    private BoundType? IndependentBoundary(Koto? boundary, BindingScope scope, out BoundType? integer, out bool failed)
    {
        integer = null;
        failed = false;
        if (boundary is null || IsUnfittedLiteral(boundary))
        {
            return null;
        }

        var type = this.BindPositionKey(boundary, scope);
        if (type is null || ReferenceEquals(type, BoundType.Never))
        {
            failed = type is null;
            return type;
        }

        if (this.IsIntegerOperand(type, scope))
        {
            integer = type;
        }
        else if (this.FromEndOffsetType(type) is { } offset)
        {
            integer = offset;
        }
        else if (!this.ProvesClosedContract(type, this.Library.Position, scope))
        {
            this.Fail(boundary, BindingFailure.TypeMismatch); // SPEC 4.6.3.1: each boundary satisfies Position.
            failed = true;
            return null;
        }

        return type;
    }

    private BoundType? FitRangeBoundary(Koto boundary, BindingScope scope, BoundType? expected, BoundType? other)
    {
        var integer = other ?? BoundType.I32;
        return KotoHelper.UnwrapParentheses(boundary) is FromEndIndexKoto
            ? this.RequireType(boundary, scope, expected ?? this.FromEndType(integer))
            : this.RequireType(boundary, scope, expected ?? integer);
    }

    // SPEC 4.6.2: `^x` constructs FromEnd<T> from an operand of any PrimitiveInteger Type T, read through references.
    private BoundType? BindFromEnd(FromEndIndexKoto node, BindingScope scope, BoundType? expected)
    {
        var offset = this.FromEndOffset(node, scope, expected);
        if (offset is null || ReferenceEquals(offset, BoundType.Never))
        {
            return Complete(node, offset);
        }

        var call = this.PositionSyntaxCall(node, "fromEnd", offset, null, node.Operand, null);
        return this.BindCall(call, scope, null) is null ? Complete(node, null) : Complete(node, this.FromEndType(offset));
    }

    // The T of `^x`: a literal-only operand takes the T of an expected FromEnd<T>, else i32 (SPEC 4.6.3.1).
    private BoundType? FromEndOffset(FromEndIndexKoto node, BindingScope scope, BoundType? expected)
    {
        if (IsUnfittedLiteral(node.Operand))
        {
            return this.RequireType(node.Operand, scope, this.FromEndOffsetType(expected) ?? BoundType.I32);
        }

        var offset = this.BindPositionKey(node.Operand, scope);
        return offset is null || ReferenceEquals(offset, BoundType.Never) || this.IsIntegerOperand(offset, scope) ? offset : this.Fail(node, BindingFailure.TypeMismatch);
    }

    // A synthesized call of the PositionSyntax function `name`, cached per source node, with the Type arguments `firstType`
    // and `secondType` (each null when absent) and the source operands `first` and `second` as arguments. The callee is
    // pinned to the declaration, so ordinary lookup, shadowing and access do not apply to it (SPEC 4.6.3).
    private InvocationKoto PositionSyntaxCall(Koto root, string name, BoundType? firstType, BoundType? secondType, Koto? first, Koto? second)
    {
        var count = (first is null ? 0 : 1) + (second is null ? 0 : 1);
        var types = (firstType is null ? 0 : 1) + (secondType is null ? 0 : 1);
        if (!this.rangeCalls.TryGetValue(root, out var call) || call.ArgumentNodes.Count != count || (call.Method is GenericsKoto cached ? cached.TypeArguments.Count : 0) != types ||
            (first is not null && !ReferenceEquals(call.ArgumentNodes[0], first)) || (second is not null && !ReferenceEquals(call.ArgumentNodes[count - 1], second)))
        {
            var callee = new SyntheticKoto(root) { Parent = root };
            Koto method = types == 0 ? callee : new GenericsKoto(root, callee, types == 1 ? [new SyntheticKoto(root) { Parent = root }] : [new SyntheticKoto(root) { Parent = root }, new SyntheticKoto(root) { Parent = root }]);
            call = new InvocationKoto(root, method, count == 0 ? [] : count == 1 ? [first ?? second!] : [first!, second!]);
            this.rangeCalls[root] = call;
        }

        var generic = call.Method as GenericsKoto;
        ((SyntheticKoto)(generic?.Identifier ?? call.Method)).Resolve(this.PositionSyntaxMember(name), null, null);
        if (generic is not null)
        {
            ((SyntheticKoto)generic.TypeArguments[0]).Resolve(null, firstType ?? secondType, null);
            if (types == 2)
            {
                ((SyntheticKoto)generic.TypeArguments[1]).Resolve(null, secondType, null);
            }

            ResetSynthetic(generic);
        }

        ResetSynthetic(call);
        return call;
    }

    private BindingSymbol? PositionSyntaxMember(string name)
        => this.Library.PositionSyntax is { } group && this.scopes.TryGetValue(group, out var members) && members.Values.TryGetValue(name, out var member) ? member : null;
}
