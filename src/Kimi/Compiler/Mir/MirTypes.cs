// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The walks' outcome belongs to the Type facts.

// Owns the Type facts needed by MIR construction and analyses.
// Input: published Types, their stored shapes, Binding's Copy and Sealed proofs and the root Type statements the operator domain composes
// (ArithmeticContracts.Supports, ComparisonTypes.OperatorCompares, ReferenceTypes.IsPointer). Output: the destruction walk and the drop,
// observation, Loan-holding with what a call may write past a value (its guard), raw, exclusivity, shared-layer, selection, view-start,
// agreement, borrow-kind, pointer-cast, integer and operator-domain facts (Operates, the operator table's domain, which the Backend's
// operator columns read too), and the pairing of the arithmetic Contracts with MIR's operators (ArithmeticOperations, read by Contract
// through ArithmeticOperation).
// Inline storage is traversed under each fact's documented bounds and Type rules; what destroying a value does is the one Destruction walk.
// The walks share one depth bound (Bound): past it the guard and the Destruction walk answer Deep, the fact that the part is unknown
// (DECISIONS R4-M153), and HoldsRaw yes (its reading is PLAN 2b-5's). Their answers at a verdict are one value, MirOutcome, combined only by
// its Join: decided outranks Limit, Limit outranks Deep, Deep outranks none. A decided answer is the verdict whatever an unknown part is
// (SPEC 23.1.1: a decided Language violation is not "may be valid"); an unsupported or abstract part (Limit, R4-M147) leaves the verdict
// undecided even past any bound, so it outranks Deep, and a verdict both leave undecided is one failure with one record (SPEC 23.1.2), the
// located Unsupported_Kd. A walk composes its parts' outcomes only by Join and stops early only where no other part changes its answer
// (decided, the outcome's top; the holding walk's LoanOnly mode, which reads no outcome, at Loan; a disjunction at yes), and its memo
// enters a Type at the shallowest depth it meets it (MirDepths; the holding walk keys a stored part's inline parts by what their facts
// read, MirHoldingKey), so the answer is the same in every order of the parts; a reader asks the published answer and never ranks two
// outcomes itself.
// Must not inspect source syntax or substitute new Binding decisions.
// Its concerns: the destruction walk and the facts over it, with the order of a level's parts (LevelCount, LevelPart: the direct parts
// of a Type in destruction order, SPEC 16.3.2's one statement, which A2's walk and the builder's Field cleanup read)
// (MirTypes.Destruction.cs), what a value may hold (MirTypes.Holding.cs), and
// here the reference, view, shared-layer, selection, view-start, borrow-kind, pointer-cast, integer, operator-domain and agreement facts.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

/// <summary>The Type facts MIR decides by, each one walk of a Type's inline storage: its Tuple, fixed-array and Closure components,
/// a struct's Fields and base and an enum's Case payloads (plan 5.5).</summary>
internal static partial class MirTypes
{
    // The deepest part the walks of a Type enter, the holding walks and the destruction walk alike: past it a part may be anything (SPEC
    // 15.3.5's growing recursion). The guard and the destruction walk answer it as its own outcome (Deep), never as a value they did not
    // establish; a check whose verdict rests on such a part records OwnershipDepthLimit_Kd (DECISIONS R4-M153).
    private const int Bound = 32;

    // SPEC 13.3.3 (DECISIONS R5-M126): each arithmetic Contract's built-in witness as its MIR operator, the one pairing of the two
    // vocabularies: Operates' arithmetic rows read it by operator, the Backend's arithmetic shim and its capability check by Contract
    // (ArithmeticOperation). The MIR builder's operator for Binding's token (ArithmeticContractSyntax.Operator) is the same; a test ties
    // them.
    private static readonly (KimiDeclarationId Contract, MirRvalueKind Kind, byte Op)[] ArithmeticOperations =
    [
        (KimiDeclarationId.Addable, MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Add),
        (KimiDeclarationId.Subtractable, MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Sub),
        (KimiDeclarationId.Multipliable, MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Mul),
        (KimiDeclarationId.Dividable, MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Div),
        (KimiDeclarationId.RemainderProvider, MirRvalueKind.BinaryOp, (byte)MirBinaryOp.Rem),
        (KimiDeclarationId.Negatable, MirRvalueKind.UnaryOp, (byte)MirUnaryOp.Neg),
    ];

    /// <summary>Gets whether a value of a Type is a reference whose Loans an EndBorrow ends: a ref, uniq, objref or objuniq, or a pair layer.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether the value is a reference.</returns>
    internal static bool IsReference(BoundType? type)
        => (type is { Kind: BoundTypeKind.Semantics } && IsBorrow(type.Semantics)) ||
            TyCtxt.PairBinder(type) is not null;

    /// <summary>Gets whether a Semantics is a borrow's: ref, uniq, objref or objuniq (SPEC 15.6.5's borrow cases).</summary>
    /// <param name="semantics">The Semantics.</param>
    /// <returns>Whether it is a borrow's.</returns>
    internal static bool IsBorrow(SemanticsKind semantics)
        => semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq;

    /// <summary>Gets the borrow a view handle is (SPEC 4.8.1, 4.8.2): a Slice's elements are its handle's referent, shared, and a UniqSlice's,
    /// exclusive, so a read through the handle reaches its backing as a read through a reference does; null for another Type.</summary>
    /// <param name="type">The Type.</param>
    /// <returns>The view's mode, <c>ref</c> or <c>uniq</c>, or null.</returns>
    internal static SemanticsKind? View(BoundType? type) => type switch
    {
        { Kind: BoundTypeKind.Slice } => SemanticsKind.Ref,
        { Symbol.LibraryDeclaration: KimiDeclarationId.UniqSlice } => SemanticsKind.Uniq,
        _ => null,
    };

    /// <summary>Gets whether a projection step passes a shared layer, past which no case grants write or exclusive access (SPEC 3.3.2,
    /// 4.8.1, 13.5.5): a Deref of a ref or objref borrow or of an rc or arc handle, an Index of a Slice (its view, <see cref="View"/>), or a
    /// PairLayer whose binder admits a shared Semantics (its weakest case); not a raw pointer's referent, an exclusive or owning layer or
    /// another step.</summary>
    /// <param name="type">The Type of the Place the step applies to.</param>
    /// <param name="step">The step's kind.</param>
    /// <param name="admitted">For a PairLayer, its binder's admitted Semantics, whose cases MirCases views; else ignored.</param>
    /// <returns>Whether the step passes a shared layer.</returns>
    internal static bool IsSharedLayer(BoundType? type, MirProjectionKind step, SemanticsMask admitted)
    {
        const SemanticsMask shared = SemanticsMask.Ref | SemanticsMask.ObjRef | SemanticsMask.Rc | SemanticsMask.Arc;
        var cases = step switch
        {
            MirProjectionKind.Deref when type is { Kind: BoundTypeKind.Semantics } => type.Semantics.ToMask(),
            MirProjectionKind.Index when View(type) is { } view => view.ToMask(),
            MirProjectionKind.PairLayer => admitted,
            _ => SemanticsMask.None,
        };
        return (cases & shared) != 0;
    }

    /// <summary>Gets whether a Type is an exclusive reference, <c>uniq</c> or <c>objuniq</c>: a borrow of its mode is exclusive, and a bare
    /// acquisition of a Place storing it Reborrows it in its own Semantics where another Type's Copies (SPEC 3.5, 8.14).</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether it is an exclusive reference.</returns>
    internal static bool IsExclusive(BoundType? type) => type?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

    /// <summary>Gets whether a built-in selection reads its receiver's value, a Slice handle or a raw pointer, whose element is its
    /// referent (SPEC 4.7.4, 4.8.1, 5.3); else the receiver is storage, which the selection lends where it locates it (its guard, which
    /// is the receiver's check, DECISIONS R4-M56).</summary>
    /// <param name="type">The receiver's Type.</param>
    /// <returns>Whether the selection reads the receiver's value.</returns>
    internal static bool SelectsByValue(BoundType? type) => type is { Kind: BoundTypeKind.Slice } or { Semantics: SemanticsKind.Raw };

    /// <summary>Gets where a range key's view starts (SPEC 4.6.1-4.6.2, 8.15.3; DECISIONS R8-M13; G597), the one rule of the start, by
    /// the Type that decides it, the start boundary's of the library's Range or ClosedRange, else the key's own: at its receiver's
    /// storage where that boundary is Start, the static zero; open where the deciding Type stands for any Type of an instance (R, or
    /// the S of Range&lt;S, End&gt;), which each instance decides; else at a position, which displaces (another boundary, a resolved
    /// range, another PositionRange, a Type that did not bind). Neither the key's own IsAbstract nor "the key holds a parameter" is
    /// the test: the first is false for Range&lt;S, End&gt;, whose start is open, and the second holds for Range&lt;i32, E&gt;, whose
    /// start is decided, a position. The builder reads it at the key's Type in the body and in a default copy's instance, the
    /// validator at a keyed Storage's token, and the instance step at the key's Type in the item's instance (LayoutNeeds).</summary>
    /// <param name="key">The key's Type.</param>
    /// <returns>True at the storage, false at a position, null where open.</returns>
    internal static bool? StartsAtStorage(BoundType? key)
    {
        var range = key?.Symbol?.LibraryDeclaration is KimiDeclarationId.Range or KimiDeclarationId.ClosedRange;
        var decides = range ? key!.Components[0] : key;
        return decides?.IsAbstract == true ? null : range && decides!.Symbol?.LibraryDeclaration == KimiDeclarationId.Start;
    }

    /// <summary>Gets the kind of the borrow that forms a reference Type (SPEC 13.5.6, 5.4), the mode of every Ref: an address for a raw
    /// pointer, a shared borrow for a shared reference, else an exclusive one, two-phase for a reservation: reserved at the borrow and
    /// activated at the Call that takes it, a call's input on its preparation path (SPEC 15.6.7), or at the write through it, an update's
    /// destination (SPEC 13.7.2).</summary>
    /// <param name="reference">The reference Type.</param>
    /// <param name="input">Whether the borrow is a reservation.</param>
    /// <returns>The borrow's kind.</returns>
    internal static MirRefKind RefKind(BoundType reference, bool input)
    {
        if (reference.Semantics == SemanticsKind.Raw)
        {
            return MirRefKind.Raw;
        }

        if (!IsExclusive(reference))
        {
            return MirRefKind.Shared;
        }

        return input ? MirRefKind.TwoPhaseUniq : MirRefKind.Uniq;
    }

    /// <summary>Gets the Cast of a raw pointer conversion from a source Type (SPEC 5.4): Pointer from a raw pointer, else IntToPointer, an
    /// integer's, an environment effect (SPEC 8.11.2).</summary>
    /// <param name="source">The converted value's Type.</param>
    /// <returns>The Cast's kind.</returns>
    internal static MirCastKind PointerCast(BoundType source) => source.Semantics == SemanticsKind.Raw ? MirCastKind.Pointer : MirCastKind.IntToPointer;

    /// <summary>Gets whether a Type has the integer operators (SPEC 13.3.1: an integer or wrapping integer Type), or one whose instance
    /// decides it: an abstract Type (a PrimitiveInteger T, SPEC 8.7.4) or Wrapping over one, which stays constructed until substitution
    /// (SPEC 3.1.1; TyCtxt.Intern). Binding proves the instance (its IsArithmeticInteger); MIR reads the shape, so no other Type with an
    /// abstract part, such as Option&lt;T&gt; or ref/T, is one. An Integer, wrapping, numeric, bit or integer-to-pointer Cast converts such a
    /// Type (MirValidator.Casts).</summary>
    /// <param name="type">The Type.</param>
    /// <returns>Whether it has the integer operators, concrete or generic.</returns>
    internal static bool IsArithmeticInteger(BoundType type) => type.HasIntegerArithmetic || InstanceDecides(type);

    /// <summary>Gets whether a MIR operator's built-in operation takes an operand's terminal Type (SPEC 13.2-13.4): the operator table's
    /// domain, the one statement MirValidator's operator rvalues and the Backend's operator columns (FunctionCodegen.Column) read, keyed by
    /// MIR's operator over the root statements. + - * / % and unary - where the operator's arithmetic Contract has a built-in witness
    /// (ArithmeticContracts.Supports, SPEC 13.3.3, by ArithmeticOperations), + and - a raw pointer too (its displacement, SPEC 5.3); &amp; | ^
    /// and a shift's left operand an integer or wrapping integer Type (SPEC 13.3.1); not bool; a comparison a Type the operator table
    /// compares (ComparisonTypes.OperatorCompares). An abstract Type's on Binding's proof of its instance, which MIR trusts by the shape:
    /// a PrimitiveInteger T's or Wrapping over one's every binary operator (SPEC 13.3.3, 3.5.3), Wrapping over one's negation (SPEC 3.1.1); a
    /// T's negation is a requirement call and no T is bool by proof, so neither stands there. Any other operator is a broken contract.</summary>
    /// <param name="kind">The rvalue's kind, BinaryOp or UnaryOp.</param>
    /// <param name="op">Its operator.</param>
    /// <param name="type">The operand's terminal Type.</param>
    /// <returns>Whether the operator's built-in operation takes it.</returns>
    internal static bool Operates(MirRvalueKind kind, byte op, BoundType type) => kind == MirRvalueKind.UnaryOp
        ? (MirUnaryOp)op switch
        {
            MirUnaryOp.Neg => Conforms(kind, op, type) || IsGenericWrapping(type),
            MirUnaryOp.Not => ReferenceEquals(type, BoundType.Boolean),
            _ => false,
        }
        : InstanceDecides(type) || (MirBinaryOp)op switch
        {
            MirBinaryOp.Add or MirBinaryOp.Sub => Conforms(kind, op, type) || ReferenceTypes.IsPointer(type),
            MirBinaryOp.Mul or MirBinaryOp.Div or MirBinaryOp.Rem => Conforms(kind, op, type),
            var bitwise when IsBitwiseOrShift(bitwise) => type.HasIntegerArithmetic,
            MirBinaryOp.Eq or MirBinaryOp.Ne => ComparisonTypes.OperatorCompares(type, KimiDeclarationId.Equatable),
            MirBinaryOp.Lt or MirBinaryOp.Le or MirBinaryOp.Gt or MirBinaryOp.Ge => ComparisonTypes.OperatorCompares(type, KimiDeclarationId.Comparable),
            _ => false,
        };

    /// <summary>Gets the MIR operator of an arithmetic Contract's built-in witness (ArithmeticOperations; SPEC 13.3.3), which the Backend's
    /// arithmetic shim applies and its capability check reads; any other Contract is a broken contract.</summary>
    /// <param name="contract">The Contract's declaration identity.</param>
    /// <returns>Its rvalue kind and operator.</returns>
    internal static (MirRvalueKind Kind, byte Op) ArithmeticOperation(KimiDeclarationId? contract)
    {
        foreach (var row in ArithmeticOperations)
        {
            if (row.Contract == contract)
            {
                return (row.Kind, row.Op);
            }
        }

        throw new InvalidOperationException($"The Contract {contract} has no built-in arithmetic witness.");
    }

    /// <summary>Gets whether an Integer constant (MirConstKind.Integer) has a Type: one with the integer operators, concrete or generic
    /// (IsArithmeticInteger), char or a raw pointer, whose one constant is null (SPEC 3.1, 5.1). The one family MirValidator checks a
    /// constant by and code generation reads in an instance (FunctionCodegen.Constant), where no Type is abstract.</summary>
    /// <param name="type">The constant's Type.</param>
    /// <returns>Whether an Integer constant has the Type.</returns>
    internal static bool HoldsIntegerConstant(BoundType type)
        => IsArithmeticInteger(type) || ReferenceEquals(type, BoundType.Char) || type.Semantics == SemanticsKind.Raw;

    // SPEC 8.2, 8.2.3: a Type as the body's identity premises make it (TyCtxt.Premised; itself in a body without a definition), since MIR Types
    // keep a premised parameter (DECISIONS BU-72); Agrees and the builder's walk to a compared Place read it.
    internal static BoundType Premised(MirBody body, BoundType type) => body.Key.Decl is { } definition ? TyCtxt.Premised(definition, type) : type;

    // SPEC 8.2: the one agreement under premises: Types agree as they are (Agree) or as the body's premises make them (E and S under
    // `E is S`), which the validator checks every Type by.
    internal static bool Agrees(MirBody body, BoundType? expected, BoundType? actual)
        => Agree(expected, actual) || (expected is not null && actual is not null && Agree(Premised(body, expected), Premised(body, actual)));

    /// <summary>Gets whether two Types agree, the one Type agreement of MIR: equal but for the Origins and Origin arguments of either Type
    /// and its components, which an assignment or a call relates instead of equating (SPEC 15.3) and the ownership analysis checks (R4).
    /// Interned Types that carry no Origin agree only when identical; an unknown Type (null) agrees with any.</summary>
    /// <param name="left">A Type.</param>
    /// <param name="right">The other Type.</param>
    /// <returns>Whether they agree.</returns>
    internal static bool Agree(BoundType? left, BoundType? right)
    {
        if (ReferenceEquals(left, right) || left is null || right is null)
        {
            return true;
        }

        if ((!left.CarriesAnyOrigin && !right.CarriesAnyOrigin) || left.Kind != right.Kind || left.Semantics != right.Semantics ||
            !ReferenceEquals(left.Symbol, right.Symbol))
        {
            return false;
        }

        if (left.Length != right.Length || !ReferenceEquals(left.LengthExpression, right.LengthExpression))
        {
            return false;
        }

        if (!ReferenceEquals(left.ClosureContext, right.ClosureContext) || left.Components.Count != right.Components.Count ||
            !left.LengthArguments.AsSpan().SequenceEqual(right.LengthArguments))
        {
            return false;
        }

        for (var i = 0; i < left.Components.Count; i++)
        {
            if (!Agree(left.Components[i], right.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    // An abstract Type (a PrimitiveInteger T, SPEC 8.7.4) or Wrapping over one: its instance decides which operators it has.
    private static bool InstanceDecides(BoundType type) => type.IsAbstract || IsGenericWrapping(type);

    // Wrapping over an abstract Type, which stays constructed until substitution (SPEC 3.1.1; TyCtxt.Intern).
    private static bool IsGenericWrapping(BoundType type)
        => type is { Kind: BoundTypeKind.Constructed, Components: [{ IsAbstract: true }] } && type.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping;

    // SPEC 13.3.1's bitwise and shift group, each operator named: the shifts << >> and the bitwise & | ^.
    private static bool IsBitwiseOrShift(MirBinaryOp op)
        => op is MirBinaryOp.Shl or MirBinaryOp.Shr || op is MirBinaryOp.BitAnd or MirBinaryOp.BitOr or MirBinaryOp.BitXor;

    // Whether an arithmetic operator's Contract has a built-in witness at a Type (ArithmeticOperations; ArithmeticContracts.Supports).
    private static bool Conforms(MirRvalueKind kind, byte op, BoundType type)
    {
        foreach (var row in ArithmeticOperations)
        {
            if (row.Kind == kind && row.Op == op)
            {
                return ArithmeticContracts.Supports(type, row.Contract);
            }
        }

        return false; // Not an arithmetic Contract's operator: Operates names none such.
    }
}

/// <summary>The walks' one answer at a verdict (MirTypes' contract; DECISIONS R4-M153): decided, Limit (an unsupported or abstract part,
/// R4-M147), Deep (a part past the walks' bound) or none. Its rank is private: the one operation over answers is <see cref="Join"/>, and a
/// reader asks what an answer is, never which of two outranks the other.</summary>
internal readonly struct MirOutcome
{
    private const byte DeepRank = 1;
    private const byte LimitRank = 2;
    private const byte DecidedRank = 3;

    private readonly byte rank;

    private MirOutcome(byte rank) => this.rank = rank;

    /// <summary>Gets the answer of nothing met: the Join's identity.</summary>
    internal static MirOutcome None => default;

    /// <summary>Gets the answer of a part past the walks' bound, which leaves the verdict undecided.</summary>
    internal static MirOutcome Deep => new(DeepRank);

    /// <summary>Gets the answer of an unsupported or abstract part, which leaves the verdict undecided past any bound.</summary>
    internal static MirOutcome Limit => new(LimitRank);

    /// <summary>Gets the decided answer, the Join's top, after which a walk may stop.</summary>
    internal static MirOutcome Decided => new(DecidedRank);

    /// <summary>Gets a value indicating whether the answer is decided.</summary>
    internal bool IsDecided => this.rank == DecidedRank;

    /// <summary>Gets a value indicating whether the answer is Limit: the verdict's record is the located Unsupported_Kd.</summary>
    internal bool IsLimit => this.rank == LimitRank;

    /// <summary>Gets a value indicating whether the answer is Deep: the verdict's record is OwnershipDepthLimit_Kd.</summary>
    internal bool IsDeep => this.rank == DeepRank;

    /// <summary>Gets a value indicating whether nothing was met.</summary>
    internal bool IsNone => this.rank == 0;

    /// <inheritdoc/>
    public override string ToString() => this.rank switch
    {
        DecidedRank => "Decided",
        LimitRank => "Limit",
        DeepRank => "Deep",
        _ => "None",
    };

    /// <summary>Gets the answer of two answers met at one verdict, MirTypes' order: commutative and associative, with None its identity and
    /// Decided its absorbing top.</summary>
    /// <param name="other">The other answer.</param>
    /// <returns>The joined answer.</returns>
    internal MirOutcome Join(MirOutcome other) => this.rank >= other.rank ? this : other;
}

/// <summary>The outcome walks' one depth policy (DECISIONS R4-M58, R4-M153's revision 3), over a walk's per-query slot for a Type (the
/// destruction walk's) or a Type in a mode (the holding walk's): 0 never met, else one more than the depth it was entered at, or
/// <see cref="Past"/>, met only past the bound. A Type is entered at the shallowest depth the query meets it at, again where it is met
/// shallower than before (at most once per depth within the bound), so the walk's answer is the same in every order of the parts; a part
/// past the bound is Deep only where its slot stays <see cref="Past"/> at the walk's end, its Type never entered within the bound, since a
/// Type's parts are its own wherever it lies.</summary>
internal static class MirDepths
{
    /// <summary>The slot of a Type met only past the bound: deeper than every depth within it, so a later meeting within enters it.</summary>
    internal const byte Past = byte.MaxValue;

    /// <summary>Gets whether a slot was entered at the depth of a meeting or shallower.</summary>
    /// <param name="at">The slot.</param>
    /// <param name="depth">The depth of the meeting.</param>
    /// <returns>Whether it was entered there or shallower.</returns>
    internal static bool Entered(byte at, int depth) => at != 0 && at <= depth + 1;

    /// <summary>Gets whether the walk enters a slot it meets within the bound: never entered, or entered deeper than this meeting.</summary>
    /// <param name="at">The slot, set to this meeting where entered; read before any recursion, which may move it.</param>
    /// <param name="depth">The depth of the meeting.</param>
    /// <returns>Whether the walk enters it.</returns>
    internal static bool Enter(ref byte at, int depth)
    {
        if (Entered(at, depth))
        {
            return false;
        }

        at = (byte)(depth + 1);
        return true;
    }

    /// <summary>Notes a meeting past the bound, where the walk does not enter the slot's Type.</summary>
    /// <param name="at">The slot, Past where never met.</param>
    internal static void Meet(ref byte at) => at = at == 0 ? Past : at;
}
