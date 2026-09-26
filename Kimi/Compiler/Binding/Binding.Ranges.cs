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

    /// <summary>Gets the synthesized Range construction of a range expression outside an index position, or null.</summary>
    /// <param name="node">The range expression.</param>
    /// <returns>The bound call, or null for an index-position range or an unbound expression.</returns>
    internal InvocationKoto? RangeValueCall(Koto node)
        => KotoHelper.UnwrapParentheses(node) is RangeKoto range && range.BindingState == BindingState.Resolved &&
            this.rangeCalls.TryGetValue(range, out var call) && call.BindingState == BindingState.Resolved ? call : null;

    private BoundType ResolvedRangeType => this.InternType(BoundTypeKind.Nominal, this.Library.ResolvedRange, SemanticsKind.Owner, []);

    private static void ResetSynthetic(Koto node)
    {
        node.BindingState = BindingState.Unvisited;
        node.BoundType = null;
        node.BoundSymbol = null;
        node.BindingFailure = BindingFailure.None;
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
