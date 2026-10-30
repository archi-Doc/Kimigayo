// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the closed statement, rvalue, terminator, projection, operand and callee kinds.
// Input: the approved MIR forms. Output: kind values with one published meaning each.
// Access/effect kinds share MirDescriptors; vocabulary growth needs a SPEC basis and owner decision.
// Must not encode an unreviewed form as a new kind or a consumer-only opcode.

#pragma warning disable SA1402 // The MIR vocabularies belong together.
#pragma warning disable SA1649 // The file names the vocabulary.

namespace Kimi.Compiler.Mir;

/// <summary>A MIR statement (plan 5.4). The six kind vocabularies of MIR and its aggregate kinds are closed (COMPILER_ARCHITECTURE, Closed
/// vocabularies): a new member needs a SPEC basis and owner approval, and every member has a row in <see cref="MirDescriptors"/>.</summary>
internal enum MirStatementKind : byte
{
    Assign, // Place = Rvalue.
    StorageLive, // The storage of a Var or Temp local begins; its value is uninitialized.
    StorageDead, // The storage of a Var or Temp local ends.
    FakeRead, // The Place is read for the analyses only (a match subject, a discarded value); nothing is generated.
    EndBorrow, // A use of a reference local, after which its carrying ends (an abandoned comparison or reservation, which ends unactivated);
    // at a guard candidate's cleanup its guard-local Origin ends, so no value used afterwards may keep it (design A3 E7).
}

/// <summary>A MIR rvalue (plan 5.4). Abort on a failed check is implicit in the rvalue; Proven marks a check IMPL 21.3.3 omits.</summary>
internal enum MirRvalueKind : byte
{
    Use, // The operand.
    Ref, // A borrow of the Place (MirRefKind), whose Type is the reference Type.
    BinaryOp,
    UnaryOp,
    Cast, // The operand converted to the Type (MirCastKind).
    Aggregate, // A Tuple, struct or enum Case, fixed array, fill, Closure environment or Array from the operands (MirAggregateKind).
    Discriminant, // The Case ordinal of an enum Place.
    Len, // The length of a sequence Place.
    Opaque, // A value of the Type that a Binding error or an Unsupported form replaced; its site lists the Places it touched.
    Adapt, // A case adaptation (SPEC 8.14), whose operation each Semantics case selects: MirCases.Adapt resolves it per view.
}

/// <summary>A MIR terminator (plan 5.4); there is no unwinding. <c>CheckTarget</c> is an attribute of Goto, a Call without a target
/// and Unreachable: the lexical successor that only the checks reach (SPEC 14.10.3).</summary>
internal enum MirTerminatorKind : byte
{
    Goto,
    SwitchInt, // Its operand against value-target pairs; otherwise the target.
    Call, // Callee(arguments) into the destination; without a target it diverges.
    Drop,
    Return, // The value of local 0.
    Unreachable,
}

/// <summary>A step of a Place (plan 5.4): what the Type of the Place it applies to admits.</summary>
internal enum MirProjectionKind : byte
{
    Deref, // The referent of a reference, raw pointer or object handle.
    Field, // A Field of a struct (its own, in declaration order), an element of a Tuple or a capture of a Closure.
    Base, // The base of a struct at a depth from 1.
    Index, // The element at an isize local; a failed bounds check aborts.
    ConstIndex, // The element of a fixed array at a constant position below its length.
    Downcast, // The payload Tuple of an enum Case.
    PairLayer, // The target of a pair layer: the Place itself or its referent, as the binder's Semantics case decides.
}

/// <summary>A MIR operand (plan 5.4).</summary>
internal enum MirOperandKind : byte
{
    Copy,
    Move,
    PairAcquire, // A Copy or a Reborrow of the Place, as the binder's Semantics case decides.
    Const,
}

/// <summary>What a MIR Call calls (plan 5.4); a Call of a Value callee calls its operand.</summary>
internal enum MirCalleeKind : byte
{
    Fn, // A function under its Subst.
    Requirement, // A Contract requirement under its Subst, resolved by the instance.
    Virtual, // A virtual function's slot.
    Value, // A Function, Closure or function item value.
    Intrinsic, // An IntrinsicId.
}

/// <summary>How a comparison of a composite Self compares its Builtin leaves (SPEC 13.4, 13.4.1; DECISIONS R5-M163): a Requirement
/// callee's <see cref="MirCallee.Arg"/> and a comparison Builtin item's variant.</summary>
internal enum ComparisonMode : byte
{
    Mapping, // The Contract's mapping (SPEC 13.4.1): NaN-reflexive equality; every requirement call but a tuple operator's.
    Operator, // A tuple operator's (SPEC 13.4): IEEE equality; an ordering's unordered pair compares as greater (<, <=).
    Reversed, // A tuple operator's ordering whose unordered pair compares as less (>, >=).
}

/// <summary>A binary operator of MIR; the Type decides checked or wrapping arithmetic.</summary>
internal enum MirBinaryOp : byte
{
    Add,
    Sub,
    Mul,
    Div,
    Rem,
    Shl,
    Shr,
    BitAnd,
    BitOr,
    BitXor,
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
}

internal enum MirUnaryOp : byte
{
    Neg,
    Not, // Logical on bool, bitwise on an integer.
}

internal enum MirRefKind : byte
{
    Shared,
    Uniq,
    TwoPhaseUniq, // Reserved at the borrow, activated at the Call that uses it or at the write through it (a compound update's writeback).
    Raw, // An address, through an immediately ending shared borrow (SPEC 5.4).
}

internal enum MirCastKind : byte
{
    Integer, // Between integer Types, checked.
    Floating, // Between floating-point Types.
    Numeric, // Between integer and floating-point Types, checked.
    Wrap, // An integer wrapped to the Type (SPEC 13.5.4).
    Bits, // The bits of a floating-point value as a same-width integer, or back (SPEC 13.5.4).
    Pointer, // Between raw pointer Types, or a raw pointer to usize (SPEC 5.4).
    IntToPointer, // An integer to a raw pointer, an environment effect (SPEC 8.11.2).
    Upcast, // An object handle to one of its base (SPEC 13.6).
    Erasure, // A Closure or function item to its Function Type (SPEC 10.2).
}

internal enum MirAggregateKind : byte
{
    Tuple,
    Adt, // A struct (its base first, then its own Fields) or the Case its ordinal names.
    FixedArray, // A fixed array from its elements, one operand each, or from none (noinit); a Dictionary from none, the empty Dictionary
    // its literal's calls fill (until PLAN G255 (C2) gives the Dictionary literal its form; DECISIONS R5-M129).
    Closure, // A Closure environment from its captures.
    Fill, // SPEC 4.3.3: a fixed array whose every element is a copy of its one operand, the value acquired once, also for length 0; its
    // length is its Type's, which an instance closes (DECISIONS R4-M81).
    Array, // SPEC 4.3.1, 4.9.5, 4.9.6, 12.4.1: an Array from its elements, one operand each in source order, moved into one buffer allocated
    // for their count, none for no element (`[]`); the allocation may Abort, and no user code runs (DECISIONS R5-M129).
}

internal enum MirConstKind : byte
{
    Integer, // Bits: the value.
    Float, // Bits: the IEEE bits.
    Bool,
    Unit,
    String, // Value: the text.
    FnItem, // The function item its Type names.
    Location, // Bits: a SourceRef, as Module << 32 | Id.
    TypeToken, // Value: the Type.
}

internal enum MirLocalKind : byte
{
    Return, // Local 0.
    Arg, // Locals 1 to the argument count, the receiver first.
    Var,
    Temp,
    DropFlag,
}

/// <summary>Why a local exists, which maps a finding to its diagnostic code; a guard candidate's role also decides A5's guard-local
/// Origin (design A3 E7), and a Result local is the body's result in transit, one of A5's escape holders (DECISIONS R4-M132).</summary>
internal enum MirLocalRole : byte
{
    None,
    PreparedInput, // A parameter a later default reads (SPEC 7.2.3).
    PendingSlot, // A prepared argument slot of an inline default.
    GuardCandidate, // A match guard's candidate binding: its value carries γ, which its EndBorrow ends (SPEC 14.8.3, err(ended)).
    Subject, // A match subject.
    Result, // The body's result in transit: the temp a transfer delivers whole into _0 after the Scope Exit (SPEC 16.2.2, R5-M50).
    Receiver, // A constructor's _0, its construction receiver self (SPEC 6.3.3-6.3.4): storage its caller's construction expression made.
}

internal enum MirScopeKind : byte
{
    Lexical,
    Unsafe,
    Guard,
    Deferred,
    Inlined, // An inline default expanded at the call its source names.
}

/// <summary>Which body of a definition a <see cref="MirDefKey"/> names.</summary>
internal enum MirBodyKind : byte
{
    Fn,
    Closure, // A Closure's body, keyed by the Closure's own symbol.
    Getter, // An accessor's, keyed by the function its calls target.
    Setter,
    StaticInit,
    DefaultTemplate, // The template of the index-th parameter's default (plan 5.6.2).
    Test, // A #Test function's, which only the test entry calls.
}
