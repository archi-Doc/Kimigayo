// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

#pragma warning disable SA1402 // The HIR column vocabularies belong together.
#pragma warning disable SA1649 // The file names the vocabulary.

namespace Kimi.Compiler;

/// <summary>What the MIR builder does with a syntax node: the Op column of <see cref="HirTables"/>. The vocabulary is closed; a
/// form no member describes keeps <see cref="Unset"/>, and the builder reports the located Unsupported.</summary>
/// <remarks><see cref="Syntax"/>: not evaluated (a declaration without a buildable body, a Type, a qualifier, the callee of a selected
/// call or construction, a member name, a binder, a Pattern, which its match plan describes, an attribute, or the inside of an Error).
/// <see cref="Body"/>: a function, accessor or Property whose body or initializer Binding bound, to be built. <see cref="Error"/>: Binding rejected
/// the expression or left it unresolved; the builder replaces it whole. <see cref="Name"/>: its Res says what it reads.
/// <see cref="Group"/>: evaluates its one operand (parentheses, a label, a macro, and <c>@move</c>, <c>@copy</c> or a same-Type
/// conversion, which acquire it). <see cref="Call"/>: the calls <see cref="CallPlan.Of"/> lists. <see cref="Jump"/> and
/// <see cref="Continue"/>: the transfer to the target the node's plan row records.</remarks>
internal enum HirOp : byte
{
    Unset,
    Syntax,
    Body,
    Error,
    Literal,
    Name,
    Field, // A stored Field or a tuple element of the receiver.
    Deref, // *e and e@follow.
    Index, // A built-in element or slice selection; a key it resolves is its call.
    Group,
    Call,
    PrimitiveOp, // A built-in arithmetic, bitwise, comparison or negation, or the metadata of a sequence.
    Logical, // and, or.
    Borrow, // @ref, @uniq, @objref, @objuniq, @raw.
    Convert, // A numeric, pointer, wrap, bits, object upcast or case-adaptation conversion, or one of a Never operand.
    Assign,
    CompoundAssign, // op=, ++ and --.
    Aggregate, // A tuple, an array or an enum Case value.
    Closure,
    RuntimeTypeTest,
    Range, // A position or range key its selection applies directly.
    Interpolation, // A formatting plan.
    DictionaryLiteral,
    Block,
    Let,
    If,
    Match,
    Loop,
    While,
    For,
    Do,
    Jump, // return, exit, yield.
    Continue,
    Try,
    Require,
    Defer,
    Unsafe,
    Discard,
    NoInit,
    TestVerification,
}

/// <summary>What a node's selected symbol names: the Res column of <see cref="HirTables"/>.</summary>
internal enum HirRes : byte
{
    None,
    Local, // A local or a Pattern binding.
    Param,
    PreparedParam, // A parameter read in the default of a later parameter of its function: the slot its call prepared (SPEC 7.2.3).
    Capture,
    Static, // A group Property.
    Decl, // A function, Type, container, Type or length parameter, or another declaration.
    Field, // A Property of a receiver, or the storage of an accessor.
    Variant,
}

/// <summary>Whether a node's evaluation can complete normally: the Completion column of <see cref="HirTables"/>, the structural
/// completion Binding's Never rule applies (SPEC 14.10). The builder ends a block that has no runtime successor where a node
/// <see cref="Diverges"/> and gives it the check-only continuation. Set on every node whose Op the builder evaluates.</summary>
internal enum HirCompletion : byte
{
    Unset,
    Completes,
    Diverges,
}

/// <summary>How a parent takes a child it consumes: the Adjust column of <see cref="HirTables"/> (SPEC 3.5, 10.2, 15.1.5, 15.1.6), written
/// for each child an Op consumes from the operation Binding recorded, which the builder reads instead of re-deriving an acquisition or an
/// adaptation. An explicit conversion is its node's Op with its <see cref="ConversionBinding"/> (the plan row's Mode). The static members
/// are the one conversion from Binding's acquisition vocabularies.</summary>
/// <param name="Acquire">What happens to the child's own value or Place.</param>
/// <param name="Adaptation">The implicit adaptation that gives the parent another Type.</param>
internal readonly record struct HirAdjust(HirAcquire Acquire, HirAdaptation Adaptation)
{
    // A Pattern binding (SPEC 15.1.6): Copied, moved, by case, or borrowed in the mode its path grants.
    internal static HirAdjust Of(PatternAcquisition acquisition, PatternAccessMode access) => acquisition switch
    {
        PatternAcquisition.Copy => new(HirAcquire.Copy, HirAdaptation.None),
        PatternAcquisition.Move => new(HirAcquire.Move, HirAdaptation.None),
        PatternAcquisition.CopyOrMove => new(HirAcquire.ByCase, HirAdaptation.None),
        PatternAcquisition.Borrow => new(HirAcquire.None, access == PatternAccessMode.Exclusive ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow),
        _ => default,
    };

    // A capture entry, as `let x = x` or `let x = x@op` acquires (SPEC 7.6.2).
    internal static HirAdjust Of(CaptureAcquisition acquisition) => acquisition switch
    {
        CaptureAcquisition.Copy => new(HirAcquire.Copy, HirAdaptation.None),
        CaptureAcquisition.Move => new(HirAcquire.Move, HirAdaptation.None),
        CaptureAcquisition.Reborrow => new(HirAcquire.Reborrow, HirAdaptation.None),
        CaptureAcquisition.Bare => new(HirAcquire.ByCase, HirAdaptation.None),
        _ => new(HirAcquire.None, acquisition == CaptureAcquisition.SharedSlotBorrow ? HirAdaptation.SharedBorrow : HirAdaptation.ExclusiveBorrow),
    };

    // A call's argument, receiver or payload operation (SPEC 7.3, 10.2); a Reborrow in the same mode keeps the Type, the bare acquisition.
    internal static HirAdaptation Of(in BoundArgumentOperation operation) => operation.Kind switch
    {
        ArgumentOperationKind.Value => operation.Adaptation == ArgumentAdaptation.Erasure ? HirAdaptation.Erasure : HirAdaptation.None,
        ArgumentOperationKind.Borrow => operation.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? HirAdaptation.ExclusiveBorrow : HirAdaptation.SharedBorrow,
        ArgumentOperationKind.Reborrow => operation.Adaptation == ArgumentAdaptation.SameSemanticsReborrow ? HirAdaptation.None : HirAdaptation.ReferenceRead,
        ArgumentOperationKind.BaseBorrow => HirAdaptation.BaseBorrow,
        ArgumentOperationKind.StorageProjection => HirAdaptation.StorageProjection,
        ArgumentOperationKind.PayloadProjection => HirAdaptation.PayloadProjection,
        ArgumentOperationKind.CopyRead => HirAdaptation.ReferentRead,
        _ => HirAdaptation.ReferenceRead,
    };

    // An adaptation recorded at a node's fixed expected Type or for a receiver (SPEC 3.4.1, 3.5.3, 10.2); a Reborrow in the node's own mode
    // keeps the Type, the bare acquisition.
    internal static HirAdaptation Of(in BoundAdaptation adaptation, BoundType? type) => adaptation.Kind switch
    {
        ExpectedAdaptationKind.ReferentRead => HirAdaptation.ReferentRead,
        ExpectedAdaptationKind.SharedBorrow => HirAdaptation.SharedBorrow,
        ExpectedAdaptationKind.ExclusiveBorrow => HirAdaptation.ExclusiveBorrow,
        ExpectedAdaptationKind.Reborrow when adaptation.Type.Semantics == type?.Semantics => HirAdaptation.None,
        _ => HirAdaptation.ReferenceRead,
    };
}

/// <summary>What happens to a consumed child's own value or Place (SPEC 3.5, 15.1.5).</summary>
internal enum HirAcquire : byte
{
    Unset,
    None, // Used in place: a target, a projected, followed or borrowed Place, the Place its adaptation reads, or a group's operand.
    Copy,
    Move,
    Reborrow, // The exclusive reference a Place stores, Reborrowed in its own mode.
    ByCase, // As each Semantics case's Type acquires: a pair layer's Copy or Reborrow (SPEC 8.9), or a Copy unproven for an abstract Type.
    Temporary, // A value, not a Place, taken as it is.
}

/// <summary>The implicit adaptation of a consumed child (SPEC 10.2); the child keeps its own Type.</summary>
internal enum HirAdaptation : byte
{
    None,
    ReferentRead, // The Scalar the reference layers end in is read (SPEC 3.5.3).
    ReferenceRead, // One reference, in the destination's mode, to the referent the layers reach (SPEC 3.4.1, 10.2).
    SharedBorrow,
    ExclusiveBorrow,
    BaseBorrow, // A borrow of the base a projected receiver's path selects (SPEC 7.3).
    PayloadProjection, // A shared borrow of an object handle's payload (SPEC 13.5.5.1).
    StorageProjection, // The receiver's base that declares the selected stored Field.
    Erasure, // A closure or function item erased to its Function Type (SPEC 10.2).
}
