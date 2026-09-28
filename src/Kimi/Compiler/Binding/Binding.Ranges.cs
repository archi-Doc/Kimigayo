// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 4.6.3: range syntax constructs Range<T> for two integer boundaries and IndexRange for an Index or an omitted
    // boundary, through internal Type functions of the Kimi Kotonoha. Each synthesized call pins its target declaration, so
    // neither a same-named source declaration nor the source's access rules affect it; its arguments are the source
    // boundaries, bound beforehand under the ordinary rules.
    private readonly Dictionary<Koto, InvocationKoto> rangeCalls = new(ReferenceEqualityComparer.Instance);

    // SPEC 4.6.2: an integer IndexRange boundary becomes Index.unchecked<I>(offset:limit:), whose invalid offset IndexRange
    // construction rejects in boundary order after both boundaries are evaluated (SPEC 4.6.4).
    private readonly Dictionary<Koto, InvocationKoto> boundaryCalls = new(ReferenceEqualityComparer.Instance);

    // SPEC 4.6.1, 4.6.4: an Index, IndexRange or Range<I> key of a built-in selection is resolved against the receiver's
    // length by a synthesized key.resolve(receiver.length) call whose length argument shares the receiver Place, so the
    // receiver is restricted to a Place written as a path; a ResolvedRange key is applied as written and rechecked.
    private readonly Dictionary<IndexKoto, InvocationKoto> resolvedKeys = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IndexKoto> resolvedSlices = new(ReferenceEqualityComparer.Instance);

    private BoundType ResolvedRangeType => this.InternType(BoundTypeKind.Nominal, this.Library.ResolvedRange, SemanticsKind.Owner, []);

    /// <summary>Gets the synthesized construction of a range value, or null for a direct index-position range.</summary>
    /// <param name="node">The range expression.</param>
    /// <returns>The bound call, or null for an index-position range or an unbound expression.</returns>
    internal InvocationKoto? RangeValueCall(Koto node)
        => KotoHelper.UnwrapParentheses(node) is RangeKoto range && range.BindingState == BindingState.Resolved &&
            this.rangeCalls.TryGetValue(range, out var call) && call.BindingState == BindingState.Resolved ? call : null;

    /// <summary>Gets the synthesized resolution call of an Index or unresolved range key, or null for an isize or ResolvedRange key.</summary>
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

    // A receiver written as a path: its shared use by the synthesized length read and the selection evaluates no call twice.
    private static bool IsPlaceSyntax(Koto node) => KotoHelper.UnwrapParentheses(node) switch
    {
        IdentifierNameKoto => true,
        MemberAccessKoto member => IsPlaceSyntax(member.Left),
        IndexKoto index => IsPlaceSyntax(index.Left),
        ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } followed => IsPlaceSyntax(followed.Left),
        _ => false,
    };

    // The element Type T of a Range<T>, or null for any other Type.
    private BoundType? RangeElement(BoundType? type)
        => type is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && ReferenceEquals(type.Symbol, this.Library.Range) ? type.Components[0] : null;

    private bool IsIndexType(BoundType? type) => type is { Kind: BoundTypeKind.Nominal } && ReferenceEquals(type.Symbol, this.Library.Index);

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

        foreach (var call in this.rangeCalls.Values)
        {
            ResetSynthetic(call);
        }

        foreach (var call in this.boundaryCalls.Values)
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
    }

    private bool TryBindKeyedSelection(IndexKoto source, BindingScope scope, BoundType receiver, out BoundType? result)
    {
        result = null;
        this.resolvedSlices.Remove(source);
        var core = receiver is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ? receiver.Components[0] : receiver;
        if (core.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array))
        {
            return false;
        }

        BoundType? key;
        if (source.Right is RangeKoto range)
        {
            if (!range.IsInclusive && !this.IsIndexBoundary(range.Start, scope) && !this.IsIndexBoundary(range.End, scope))
            {
                return false; // Integer or omitted boundaries select directly.
            }

            key = this.BindRangeValue(range, scope, null);
            if (key is null)
            {
                result = Complete(source, null);
                return true;
            }
        }
        else
        {
            // A key whose Type its declaration fixes binds as written; any other integer form keeps the isize expectation.
            var declared = KotoHelper.UnwrapParentheses(source.Right) is IdentifierNameKoto or MemberAccessKoto or InvocationKoto or FromEndIndexKoto or IndexKoto;
            key = IsUnfittedLiteral(source.Right) ? null : this.BindNode(source.Right, scope, declared ? null : BoundType.ISize);
        }

        var index = this.IsIndexType(key);
        var unresolved = (key is { Kind: BoundTypeKind.Nominal } && ReferenceEquals(key.Symbol, this.Library.IndexRange)) || this.RangeElement(key) is not null;
        if (!index && !unresolved && !ReferenceTypes.IsResolvedRange(key))
        {
            return false;
        }

        if (!IsPlaceSyntax(source.Left))
        {
            result = Fail(source, BindingFailure.Unsupported);
            return true;
        }

        if ((index || unresolved) && this.ResolveKeyCall(source, scope) is null)
        {
            result = Complete(source, null);
            return true;
        }

        var element = core.Components[0];
        if (index)
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

    private bool IsIndexBoundary(Koto? boundary, BindingScope scope)
        => boundary is not null && !IsUnfittedLiteral(boundary) && this.IsIndexType(this.BindNode(boundary, scope));

    private InvocationKoto? ResolveKeyCall(IndexKoto source, BindingScope scope)
    {
        if (!this.resolvedKeys.TryGetValue(source, out var call))
        {
            var length = new MemberAccessKoto(source, source.Left, new IdentifierNameKoto(source, "length"));
            call = new InvocationKoto(source, new MemberAccessKoto(source, source.Right, new IdentifierNameKoto(source, "resolve")), [length]);
            this.resolvedKeys[source] = call;
        }

        var callee = (MemberAccessKoto)call.Method;
        var lengthArgument = (MemberAccessKoto)call.ArgumentNodes[0];
        ResetSynthetic(call);
        ResetSynthetic(callee);
        ResetSynthetic(callee.Right);
        ResetSynthetic(lengthArgument);
        ResetSynthetic(lengthArgument.Right);
        return this.BindCall(call, scope, null) is null ? null : call;
    }

    private BoundType? BindRangeValue(RangeKoto range, BindingScope scope, BoundType? expected)
    {
        if (range.IsInclusive && range.End is null)
        {
            return Fail(range, BindingFailure.TypeMismatch); // SPEC 4.6.3.1: an inclusive end cannot be omitted.
        }

        // SPEC 4.6.3.1: the kind depends only on omitted boundaries and on boundary Types known independently of the context.
        var start = this.IndependentBoundary(range.Start, scope, out var startFailed);
        var end = this.IndependentBoundary(range.End, scope, out var endFailed);
        if (startFailed || endFailed)
        {
            return Complete(range, null);
        }

        return range.Start is null || range.End is null || this.IsIndexType(start) || this.IsIndexType(end)
            ? this.BindIndexRange(range, scope) : this.BindIntegerRange(range, scope, start, end, expected);
    }

    // The Type of a boundary known independently of the context, read through safe references; null for a missing or
    // literal-only boundary.
    private BoundType? IndependentBoundary(Koto? boundary, BindingScope scope, out bool failed)
    {
        failed = false;
        if (boundary is null || IsUnfittedLiteral(boundary))
        {
            return null;
        }

        var type = this.BindNode(boundary, scope);
        failed = type is null;
        return ScalarReferent(type) ?? this.GenericIntegerReferent(type, scope) ?? type;
    }

    // SPEC 4.6.3.1: both boundaries of Range<T> have one integer Type. An established Type fits a literal-only other
    // boundary, and two literal-only boundaries take T from an expected Range<X> or default to i32 (SPEC 12.3.1).
    private BoundType? IntegerRangeElement(RangeKoto range, BindingScope scope, BoundType? start, BoundType? end, BoundType? expected)
    {
        if ((start is not null && !this.IsIntegerOperand(start, scope)) || (end is not null && !this.IsIntegerOperand(end, scope)) ||
            (start is not null && end is not null && !ReferenceEquals(start, end)))
        {
            Fail(range, BindingFailure.TypeMismatch);
            return null;
        }

        var element = start ?? end ?? (this.RangeElement(expected) is { } fitted && this.IsIntegerOperand(fitted, scope) ? fitted : BoundType.I32);
        return this.RequireType(range.Start!, scope, element) is null || this.RequireType(range.End!, scope, element) is null ? null : element;
    }

    private BoundType? BindIntegerRange(RangeKoto range, BindingScope scope, BoundType? start, BoundType? end, BoundType? expected)
    {
        if (this.IntegerRangeElement(range, scope, start, end, expected) is not { } element)
        {
            return Complete(range, null);
        }

        // SPEC 4.6.3.2: construction checks no order; Range<T>.between/through only store the boundaries.
        var type = this.InternType(BoundTypeKind.Constructed, this.Library.Range, SemanticsKind.Owner, [element]);
        var call = this.SyntheticCall(this.rangeCalls, range, this.Library.Range, range.IsInclusive ? "through" : "between", type, null, range.Start, range.End, false);
        return this.BindCall(call, scope, null) is null ? Complete(range, null) : Complete(range, type);
    }

    // SPEC 4.6.3.3: an IndexRange takes the Type function named by its written boundaries, between/from/to/all, or
    // through/upTo for an inclusive end; an omitted start is 0 and an omitted end ^0.
    private BoundType? BindIndexRange(RangeKoto range, BindingScope scope)
    {
        var start = range.Start is null ? null : this.PositionArgument(range.Start, scope);
        var end = range.End is null ? null : this.PositionArgument(range.End, scope);
        if ((range.Start is not null && start is null) || (range.End is not null && end is null))
        {
            return Complete(range, null);
        }

        var form = start is not null && end is not null ? (range.IsInclusive ? "through" : "between")
            : start is not null ? "from" : end is not null ? (range.IsInclusive ? "upTo" : "to") : "all";
        var call = this.SyntheticCall(this.rangeCalls, range, this.Library.IndexRange, form, null, null, start, end, false);
        return this.BindCall(call, scope, null) is { } type ? Complete(range, type) : Complete(range, null);
    }

    // SPEC 4.6.2: an Index boundary is passed as written. An integer boundary of any PrimitiveInteger Type, a literal-only
    // one fitted to isize, becomes Index.unchecked<I>(offset: boundary, limit: the target's isize maximum).
    private Koto? PositionArgument(Koto boundary, BindingScope scope)
    {
        var type = IsUnfittedLiteral(boundary) ? this.RequireType(boundary, scope, BoundType.ISize) : this.BindNode(boundary, scope);
        if (type is null)
        {
            return null;
        }

        if (this.IsIndexType(type))
        {
            return boundary;
        }

        var integer = ScalarReferent(type) ?? this.GenericIntegerReferent(type, scope) ?? type;
        if (!this.IsIntegerOperand(integer, scope))
        {
            Fail(boundary, BindingFailure.TypeMismatch);
            return null;
        }

        if (!ReferenceEquals(integer, type) && this.RequireType(boundary, scope, integer) is null)
        {
            return null;
        }

        var call = this.SyntheticCall(this.boundaryCalls, boundary, this.Library.Index, "unchecked", null, integer, boundary, null, true);
        return this.BindCall(call, scope, null) is null ? null : call;
    }

    // A synthesized call of the Type function `name` of `container`, cached per source node. `declaringType` fixes a generic
    // container's arguments and `typeArgument` the function's own; `limit` appends the target's isize maximum. The callee
    // is pinned to the declaration, so ordinary lookup, shadowing and access do not apply to it (SPEC 4.6.3).
    private InvocationKoto SyntheticCall(Dictionary<Koto, InvocationKoto> cache, Koto root, BindingSymbol container, string name, BoundType? declaringType, BoundType? typeArgument, Koto? first, Koto? second, bool limit)
    {
        var count = (first is null ? 0 : 1) + (second is null ? 0 : 1) + (limit ? 1 : 0);

        // Index formation checks representability as isize without a public maximum-value API. Target-independent Binding
        // emits no code and uses a value that every isize holds; a later target preparation replaces it.
        var maximum = this.compilation.PointerWidth switch { 32 => (ulong)int.MaxValue, 64 => (ulong)long.MaxValue, _ => 127UL };
        if (!cache.TryGetValue(root, out var call) || call.ArgumentNodes.Count != count || (call.Method is GenericsKoto) != (typeArgument is not null) ||
            (first is not null && !ReferenceEquals(call.ArgumentNodes[0], first)) || (second is not null && !ReferenceEquals(call.ArgumentNodes[first is null ? 0 : 1], second)) ||
            (limit && !(((NumberLiteralKoto)call.ArgumentNodes[count - 1]).TryGetIntegerMagnitude(out var cached) && cached == maximum)))
        {
            var callee = new SyntheticKoto(root) { Parent = root };
            Koto method = typeArgument is null ? callee : new GenericsKoto(root, callee, [new SyntheticKoto(root) { Parent = root }]);
            var arguments = new Koto[count];
            var next = 0;
            if (first is not null)
            {
                arguments[next++] = first;
            }

            if (second is not null)
            {
                arguments[next++] = second;
            }

            if (limit)
            {
                arguments[next] = new NumberLiteralKoto(root, maximum);
            }

            call = new InvocationKoto(root, method, arguments);
            cache[root] = call;
        }

        var generic = call.Method as GenericsKoto;
        ((SyntheticKoto)(generic?.Identifier ?? call.Method)).Resolve(this.ContainerMember(container, name), null, declaringType);
        if (generic is not null)
        {
            ((SyntheticKoto)generic.TypeArguments[0]).Resolve(null, typeArgument, null);
            ResetSynthetic(generic);
        }

        if (limit)
        {
            ResetSynthetic(call.ArgumentNodes[count - 1]);
        }

        ResetSynthetic(call);
        return call;
    }

    // A single directly selected boundary: an integer of any PrimitiveInteger Type, read through references, or a literal-only
    // one at isize (SPEC 4.6.2).
    private bool DirectPosition(Koto? boundary, BindingScope scope)
    {
        if (boundary is null)
        {
            return true;
        }

        if (IsUnfittedLiteral(boundary))
        {
            return this.RequireType(boundary, scope, BoundType.ISize) is not null;
        }

        var type = this.IndependentBoundary(boundary, scope, out var failed);
        if (failed)
        {
            return false;
        }

        if (type is null || !this.IsIntegerOperand(type, scope))
        {
            Fail(boundary, BindingFailure.TypeMismatch);
            return false;
        }

        return this.RequireType(boundary, scope, type) is not null;
    }

    private BindingSymbol? ContainerMember(BindingSymbol container, string name)
        => this.scopes.TryGetValue(container.Declaration, out var members) && members.Values.TryGetValue(name, out var member) ? member : null;
}
