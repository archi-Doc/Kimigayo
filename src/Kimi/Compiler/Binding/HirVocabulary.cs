// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
