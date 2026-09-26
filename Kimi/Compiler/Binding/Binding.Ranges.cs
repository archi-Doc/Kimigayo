// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 4.6.3: range syntax outside an index position constructs the library Range through the labeled constructor
    // that names the written boundaries; an isize boundary is normalized to a start-relative Index, and Range
    // construction checks both signs after both boundaries are evaluated (SPEC 4.6.4). The synthesized calls are
    // ordinary calls of the Kimi Kotonoha's Types, whatever names the source scope declares.
    private readonly Dictionary<RangeKoto, InvocationKoto> rangeCalls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, InvocationKoto> boundaryCalls = new(ReferenceEqualityComparer.Instance);

    // SPEC 4.6.1, 4.6.4: an Index or Range key of a built-in selection is resolved against the receiver's length by a
    // synthesized key.resolve(receiver.length) call whose length argument shares the receiver Place, so the receiver is
    // restricted to a Place written as a path; a ResolvedRange key is applied as written and rechecked by the selection.
    private readonly Dictionary<IndexKoto, InvocationKoto> resolvedKeys = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IndexKoto> resolvedSlices = new(ReferenceEqualityComparer.Instance);

    private BoundType ResolvedRangeType => this.InternType(BoundTypeKind.Nominal, this.Library.ResolvedRange, SemanticsKind.Owner, []);

    /// <summary>Gets the synthesized Range construction of a range expression outside an index position, or null.</summary>
    /// <param name="node">The range expression.</param>
    /// <returns>The bound call, or null for an index-position range or an unbound expression.</returns>
    internal InvocationKoto? RangeValueCall(Koto node)
        => KotoHelper.UnwrapParentheses(node) is RangeKoto range && range.BindingState == BindingState.Resolved &&
            this.rangeCalls.TryGetValue(range, out var call) && call.BindingState == BindingState.Resolved ? call : null;

    /// <summary>Gets the synthesized resolution call of an Index or Range key, or null for an isize or ResolvedRange key.</summary>
    /// <param name="node">The selection expression.</param>
    /// <returns>The bound call, or null.</returns>
    internal InvocationKoto? ResolvedKeyCall(Koto node)
        => node is IndexKoto index && index.BindingState == BindingState.Resolved && this.resolvedKeys.TryGetValue(index, out var call) && call.BindingState == BindingState.Resolved ? call : null;

    /// <summary>Gets a value indicating whether the selection applies one ResolvedRange value, written or resolved from a Range.</summary>
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
        ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } followed => IsPlaceSyntax(followed.Left),
        _ => false,
    };

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
                return false; // Two isize boundaries select directly.
            }

            key = this.BindRangeValue(range, scope);
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

        var index = key is { Kind: BoundTypeKind.Nominal } && ReferenceEquals(key.Symbol, this.Library.Index);
        var unresolved = key is { Kind: BoundTypeKind.Nominal } && ReferenceEquals(key.Symbol, this.Library.Range);
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
        result = Complete(source, this.InternType(BoundTypeKind.Slice, null, SemanticsKind.Owner, [element], origin: this.PlaceOrigin(source.Left)));
        return true;
    }

    private bool IsIndexBoundary(Koto? boundary, BindingScope scope)
        => boundary is not null && !IsUnfittedLiteral(boundary) && this.BindNode(boundary, scope) is { Kind: BoundTypeKind.Nominal } type && ReferenceEquals(type.Symbol, this.Library.Index);

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

    private BoundType? BindRangeValue(RangeKoto range, BindingScope scope)
    {
        var start = range.Start is null ? null : this.BoundaryArgument(range.Start, scope);
        var end = range.End is null ? null : this.BoundaryArgument(range.End, scope);
        if ((range.Start is not null && start is null) || (range.End is not null && end is null))
        {
            return Complete(range, null);
        }

        if (range.IsInclusive && end is null)
        {
            return Fail(range, BindingFailure.TypeMismatch); // SPEC 4.6.3: an inclusive end cannot be omitted.
        }

        if (!this.rangeCalls.TryGetValue(range, out var call))
        {
            // The Type function named by the written boundaries: between/from/to/all, or through/upTo for an inclusive end.
            var form = start is not null && end is not null ? (range.IsInclusive ? "through" : "between")
                : start is not null ? "from" : end is not null ? (range.IsInclusive ? "upTo" : "to") : "all";
            var arguments = new List<Koto>(2);
            if (start is not null)
            {
                arguments.Add(start);
            }

            if (end is not null)
            {
                arguments.Add(end);
            }

            call = new InvocationKoto(range, new MemberAccessKoto(range, new IdentifierNameKoto(range, "Range"), new IdentifierNameKoto(range, form)), arguments);
            this.rangeCalls[range] = call;
        }

        var type = this.BindSynthesizedConstruction(call, this.Library.Range, scope);
        return type is null ? Complete(range, null) : Complete(range, type);
    }

    // An Index boundary is passed as written; an integer boundary is an unchecked start-relative Index.
    private Koto? BoundaryArgument(Koto boundary, BindingScope scope)
    {
        // SPEC 4.6.2: an integer boundary has the expected Type isize; a value already typed Index is passed as written.
        var type = IsUnfittedLiteral(boundary) ? this.RequireType(boundary, scope, BoundType.ISize) : this.BindNode(boundary, scope);
        if (type is null)
        {
            return null;
        }

        if (type is { Kind: BoundTypeKind.Nominal } && ReferenceEquals(type.Symbol, this.Library.Index))
        {
            return boundary;
        }

        if (this.RequireType(boundary, scope, BoundType.ISize) is null)
        {
            return null;
        }

        if (!this.boundaryCalls.TryGetValue(boundary, out var call))
        {
            call = new InvocationKoto(boundary, new MemberAccessKoto(boundary, new IdentifierNameKoto(boundary, "Index"), new IdentifierNameKoto(boundary, "init")), [boundary]);
            call.SetArgumentLabels(["unchecked"]);
            this.boundaryCalls[boundary] = call;
        }

        return this.BindSynthesizedConstruction(call, this.Library.Index, scope) is null ? null : call;
    }

    // Binds Type.init(...) with the Type name pre-bound to the designated library declaration; a synthesized node is
    // outside the tree that each Bind resets.
    private BoundType? BindSynthesizedConstruction(InvocationKoto call, BindingSymbol type, BindingScope scope)
    {
        var callee = (MemberAccessKoto)call.Method;
        ResetSynthetic(call);
        ResetSynthetic(callee);
        ResetSynthetic(callee.Right);
        ResetSynthetic(callee.Left);
        this.BindReference(callee.Left, type, scope);
        return this.BindCall(call, scope, null);
    }
}
