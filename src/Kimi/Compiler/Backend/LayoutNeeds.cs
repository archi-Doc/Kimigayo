// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the layout needs of the MIR forms and the comparison tree (SPEC 8.15.3; DECISIONS R5-M144, R5-M149): what a form's generated code
// reads a layout of besides its by-value storage, the one fact the instance collector (K 4.8: a use of that Type) and code generation (the
// Type it lays out, FunctionCodegen.Needed) both read. Rows: a projection step by its kind, an operator by its operator and operand, a
// comparison node by its kind and Type; the intrinsic rows' and the legacy rows' Need columns (IntrinsicRow.Need, LegacyRow.Need) carry the
// calls'. TypeOf is the one place that names the Type a need reads, as a Type of the form's body or tree; each reader maps it into its
// item's instance.
// Input: a projection kind, a MIR operator and its first operand's Type, a comparison node; a body Place, rvalue or substitution for TypeOf.
// Output: a LayoutNeed; the Type (or the Types) it names.
// Invariants: closed: a new form adds a row here or a Need on its table; every row's Type is TypeOf's; a need past its root's storage is a
// use (Layout.Indirect states the boundary), a call's Type arguments always (the size SPEC 8.15.3 names: raw allocation, a whole value's
// transfer, an entry's layout).
// Must not: decide a use (the collector does, by the boundary), lay out a Type (Layout does) or read an instance (the readers map).

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>What a form's generated code reads a layout of besides its by-value storage (DECISIONS R5-M149; the owner's closed vocabulary).
/// </summary>
internal enum LayoutNeed : byte
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The record of a Type whose parts the form reads at their offsets: a Field's or a Downcast's prefix, a compared Tuple.</summary>
    Parts,

    /// <summary>The stride of a Type the form steps over: an Index's or a ConstIndex's element, a raw pointer displacement's pointee.</summary>
    Element,

    /// <summary>The layouts of the call's Type arguments: an intrinsic's, a whole-value update's, a Dictionary kernel's.</summary>
    TypeArguments,
}

/// <summary>The layout-need rows of the MIR forms and the comparison tree, and the Type each names (DECISIONS R5-M149).</summary>
internal static class LayoutNeeds
{
    /// <summary>Gets a projection step's need: a Field or Downcast reads its prefix's record, an Index or ConstIndex its element's stride.
    /// </summary>
    /// <param name="kind">The step's kind.</param>
    /// <returns>Its need.</returns>
    internal static LayoutNeed Of(MirProjectionKind kind) => kind switch
    {
        MirProjectionKind.Field or MirProjectionKind.Downcast => LayoutNeed.Parts,
        MirProjectionKind.Index or MirProjectionKind.ConstIndex => LayoutNeed.Element,
        MirProjectionKind.Deref or MirProjectionKind.Base or MirProjectionKind.PairLayer => LayoutNeed.None,
        _ => throw new InvalidOperationException($"The MIR projection kind {kind} has no layout-need row."),
    };

    /// <summary>Gets an operator's need (SPEC 5.3): p + n and p - n on a raw pointer displace it by n times its pointee's stride, the one
    /// statement of which operator displaces; every other operator reads no layout.</summary>
    /// <param name="op">The operator.</param>
    /// <param name="operand">Its first operand's Type.</param>
    /// <returns>Its need.</returns>
    internal static LayoutNeed Of(MirBinaryOp op, BoundType operand)
        => op is MirBinaryOp.Add or MirBinaryOp.Sub && operand is { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Raw }
            ? LayoutNeed.Element : LayoutNeed.None;

    /// <summary>Gets a comparison node's need (SPEC 13.4): a Composite over a Tuple compares its parts at their offsets; a layer, a Builtin
    /// and a Witness read none.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Its need.</returns>
    internal static LayoutNeed Of(ComparisonNode node)
        => node is { Kind: ComparisonKind.Composite, Type.Kind: BoundTypeKind.Tuple } ? LayoutNeed.Parts : LayoutNeed.None;

    /// <summary>Gets the Type a projection step's need names, in its body: a Parts step's prefix, an Element step's own Type.</summary>
    /// <param name="body">The body.</param>
    /// <param name="step">A Place whose last step has a need.</param>
    /// <returns>The Type.</returns>
    internal static BoundType TypeOf(MirBody body, MirPlace step)
    {
        var row = body.Projections[step.Projection];
        return Of(row.Kind) == LayoutNeed.Parts ? body.TypeOf(step with { Projection = row.Prefix })! : row.Type!;
    }

    /// <summary>Gets the Type an operator's need names, in its body: a displacement's pointee, its raw pointer operand's component.</summary>
    /// <param name="body">The body.</param>
    /// <param name="operation">A BinaryOp whose need is Element.</param>
    /// <returns>The Type.</returns>
    internal static BoundType TypeOf(MirBody body, in MirRvalue operation)
    {
        var operand = body.Operands[operation.Start];
        var pointer = operand.Kind == MirOperandKind.Const ? body.Consts[operand.Arg].Type : body.TypeOf(operand.Place)!;
        return pointer.Components[0];
    }

    /// <summary>Gets the Type a comparison node's need names: its own.</summary>
    /// <param name="node">A node whose need is Parts.</param>
    /// <returns>The Type.</returns>
    internal static BoundType TypeOf(ComparisonNode node) => node.Type;

    /// <summary>Gets the Types a call's TypeArguments need names: every Type of its substitution (a whole-value update's T, a Dictionary
    /// kernel's K and V, an intrinsic's one argument).</summary>
    /// <param name="subst">The call's, or its Item's, substitution.</param>
    /// <returns>The Types.</returns>
    internal static ReadOnlySpan<BoundType?> TypeOf(Subst subst) => subst.Types;
}
