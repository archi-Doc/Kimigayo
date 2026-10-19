// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Backend;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Compact physical plan records shared by lowering and serialization.

/// <summary>A physical instruction. Each opcode has exactly one serialization rule in <see cref="LlvmModuleWriter"/>.</summary>
internal enum EmissionOpcode : byte
{
    Label,
    Branch,
    ConditionalBranch,
    LoadScalar,
    LoadElement,
    ElementAddress,
    BorrowAddress,
    ObjectPayload,
    ObjectTypeTest,
    ObjectBorrow,
    Sequence,
    StoreScalar,
    StoreElement,
    SwapScalars,
    Scalar,
    Convert,
    PointerOffset,
    LoadPointer,
    StorePointer,
    Phi,

    /// <summary>First placement of a Static string literal into <c>Place</c>'s slot; <c>Constant</c> is -1 for the empty literal.</summary>
    StoreStaticString,

    /// <summary>Transfer the string handle from the first operand's slot to Place, or to the second operand's element address.</summary>
    MoveString,
    DestroyStringIfLive,
    StoreLiveFlag,
    InitializeLiveFlag,
    StringEquals,
    StringCompare,
    BuiltinComparison,
    TupleRelation,
    StringPattern,
    CompositePattern,
    PatternRead,
    CreateClosure,
    EraseClosure,
    CallValue,

    /// <summary>Memcpy: zero operands use Place/Constant slots; one supplies the source address; two supply source and destination addresses.</summary>
    TransferAggregate,
    FillArray,

    /// <summary>Destroy the exact aggregate Type at Place, or at the sole address operand; only Place supports conditional cleanup.</summary>
    DestroyAggregate,
    DestroyPart,
    EndPartDestruction,
    StorePathFlag,

    /// <summary>A direct call of <c>Callee</c> with prepared operands.</summary>
    Call,
    VirtualCall,
    TestSnapshot,
    TestPhaseEnter,
    TestPhaseLeave,

    /// <summary>A normal return, emitted only after lowering proves a normal Exit.</summary>
    ReturnVoid,
    ReturnScalar,

    /// <summary>Terminates a block after a proven nonreturning call.</summary>
    Unreachable,
}

internal enum EmissionOperandKind : byte
{
    Value,
    Argument,
    ReturnAddress,
    Block,

    /// <summary>The address of the slot prepared for a Place ID.</summary>
    SlotAddress,
    ProjectedSlot,
    ElementAddress,
    NullAddress,

    /// <summary>The address of a pooled constant.</summary>
    ConstantAddress,

    /// <summary>The UTF-8 byte length of a pooled constant.</summary>
    ConstantLength,

    /// <summary>The current standard source entry's forwarded diagnostic context.</summary>
    CallerLocation,
    CallerLocationLength,

    /// <summary>An integer of the parameter's ABI Type.</summary>
    Integer,

    /// <summary>Exact IEEE 754 bits in the selected format.</summary>
    Float32,
    Float64,
    EnvironmentAddress,
    CaptureAddress,
    FunctionAddress,
    FormattingStack,
}

// Internal managed storage only: 8-byte packing avoids 16-byte tail padding for the tag.
// This does not change the language TypeLayout or the emitted native ABI.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly record struct EmissionOperand(EmissionOperandKind Kind, Int128 Value);

internal readonly record struct EmissionSlot(int Place, ValueLowering Value);

internal readonly record struct EmissionSubslot(int Place, int Parent, int Offset);

internal enum ArithmeticCheckKind : byte
{
    None,
    Overflow,

    /// <summary>Signed quotient: a zero divisor and the minimum / −1 pair Abort.</summary>
    Division,

    /// <summary>Unsigned quotient, or any remainder: only a zero divisor Aborts; a signed remainder by −1 is 0 (SPEC 13.3.1).</summary>
    DivisionZero,

    /// <summary>Signed quotient of a wrapping integer Type: only a zero divisor Aborts, and the minimum / −1 wraps to the minimum.</summary>
    WrappingDivision,
    Shift,
    Conversion,
    Bounds,
    Argument,
    FloatingConversion,
}

/// <summary>One instruction; <c>Operation</c> is the source ownership operation ID, or -1 for synthesized startup control.</summary>
internal readonly record struct EmissionInstruction(EmissionOpcode Opcode, int Operation, int Place = -1, int Constant = -1, FunctionAbi? Callee = null, int OperandStart = 0, int OperandCount = 0, string? ScalarType = null, string? ScalarOperator = null, ArithmeticCheckKind Check = ArithmeticCheckKind.None, bool IsComparison = false, ValueLowering? Representation = null, ValueLowering? CountRepresentation = null, string? LowerPredicate = null, string? UpperPredicate = null, AggregateLayout? Aggregate = null, int Continuation = -1, int PatternStart = 0, int PatternCount = 0);

// Text is -2 for a scalar test, -1 for an empty string, or a UTF-8 constant index.
internal readonly record struct PatternTestStep(int Offset, ValueLowering Representation, Int128 Expected, int Text = -2, int DereferenceStart = 0, int DereferenceCount = 0);

// SPEC 4.6.3: the shape flags of a directly applied range slice (the SliceRange operand after its boundaries).
internal static class SliceShape
{
    internal const long EndOmitted = 1;
    internal const long StartFromEnd = 2;
    internal const long EndFromEnd = 4;
    internal const long Closed = 8;
}

/// <summary>One physical function definition. Its lists are reused by later preparations.</summary>
internal sealed class EmissionFunction
{
    internal FunctionAbi Abi { get; private set; } = null!;

    internal bool Exported { get; private set; }

    internal bool NeedsStringComparison { get; set; }

    internal List<EmissionSlot> Slots { get; } = new();

    internal List<EmissionSubslot> Subslots { get; } = new();

    /// <summary>Gets Place-indexed physical storage: local slot, logical parameter or hidden result.</summary>
    internal List<EmissionOperand> SlotAddresses { get; } = new();

    internal List<int> LiveFlags { get; } = new();

    internal List<int> PathFlags { get; } = new();

    internal List<EmissionInstruction> Instructions { get; } = new();

    internal List<EmissionOperand> Operands { get; } = new();

    internal List<FunctionAbi> FunctionAddresses { get; } = new();

    internal List<int> FormattingStacks { get; } = new();

    internal List<PatternTestStep> PatternSteps { get; } = new();

    internal List<int> PatternDereferences { get; } = new();

    internal ReadOnlySpan<PatternTestStep> GetPattern(in EmissionInstruction instruction)
        => CollectionsMarshal.AsSpan(this.PatternSteps).Slice(instruction.PatternStart, instruction.PatternCount);

    internal ReadOnlySpan<int> GetPatternDereferences(in PatternTestStep step)
        => CollectionsMarshal.AsSpan(this.PatternDereferences).Slice(step.DereferenceStart, step.DereferenceCount);

    internal ReadOnlySpan<EmissionOperand> GetOperands(in EmissionInstruction instruction)
        => CollectionsMarshal.AsSpan(this.Operands).Slice(instruction.OperandStart, instruction.OperandCount);

    internal void Reset(FunctionAbi abi, bool exported)
    {
        this.Abi = abi;
        this.Exported = exported;
        this.NeedsStringComparison = false;
        this.Slots.Clear();
        this.Subslots.Clear();
        this.SlotAddresses.Clear();
        this.LiveFlags.Clear();
        this.PathFlags.Clear();
        this.Instructions.Clear();
        this.Operands.Clear();
        this.FunctionAddresses.Clear();
        this.FormattingStacks.Clear();
        this.PatternSteps.Clear();
        this.PatternDereferences.Clear();
    }

    internal void Add(EmissionOpcode opcode, int operation, int place = -1, int constant = -1)
        => this.Instructions.Add(new(opcode, operation, place, constant));

    internal void AddCall(int operation, FunctionAbi callee, ReadOnlySpan<EmissionOperand> operands, int virtualSlot = -1, int receiver = -1)
    {
        if (operands.Length != callee.Parameters.Length)
        {
            throw new InvalidOperationException("Call operands do not match the prepared function ABI.");
        }

        var start = this.Operands.Count;
        this.Operands.AddRange(operands);
        this.Instructions.Add(new(virtualSlot < 0 ? EmissionOpcode.Call : EmissionOpcode.VirtualCall, operation, Place: receiver, Constant: virtualSlot, Callee: callee, OperandStart: start, OperandCount: operands.Length));
    }

    internal void AddScalar(EmissionOpcode opcode, int operation, ReadOnlySpan<EmissionOperand> operands, string? type = null, string? op = null, int place = -1, int location = -1, ArithmeticCheckKind check = ArithmeticCheckKind.None, bool comparison = false, ValueLowering? representation = null, ValueLowering? countRepresentation = null)
    {
        var start = this.Operands.Count;
        this.Operands.AddRange(operands);
        this.Instructions.Add(new(opcode, operation, Place: place, Constant: location, OperandStart: start, OperandCount: operands.Length, ScalarType: type, ScalarOperator: op, Check: check, IsComparison: comparison, Representation: representation, CountRepresentation: countRepresentation));
    }
}

/// <summary>
/// A closed, reusable module plan of constants and physical functions. It never refers to syntax,
/// Binding or ownership state, and is compilation-local scratch consumed before the next preparation.
/// </summary>
internal sealed class EmissionModule
{
    private readonly List<EmissionFunction> functions = new();
    private int functionCount;

    internal LlvmConstantPool Constants { get; } = new();

    internal HashSet<AggregateLayout> Aggregates { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the Array, Dictionary, formatting and string-comparison helper requests (DECISIONS R5-M16).</summary>
    internal LegacyRuntimeHelpers RuntimeHelpers { get; } = new();

    internal List<ObjectCreation> Objects { get; } = new();

    internal bool NeedsObjectRuntime { get; set; }

    /// <summary>Gets the foreign functions (SPEC 22.6), one per external symbol; each call shares its physical signature.</summary>
    internal List<ExternalFunction> Externals { get; } = new();

    internal List<StaticScalarEntry> Statics { get; } = new();

    internal bool IsComplete { get; private set; }

    internal string? TestRuntime { get; set; }

    internal int FunctionCount => this.functionCount;

    internal EmissionFunction GetFunction(int index)
        => (uint)index < (uint)this.functionCount ? this.functions[index] : throw new ArgumentOutOfRangeException(nameof(index));

    internal void Clear()
    {
        this.IsComplete = false;
        this.TestRuntime = null;
        this.functionCount = 0;
        this.Constants.Clear();
        this.Aggregates.Clear();
        this.RuntimeHelpers.Clear();
        this.Objects.Clear();
        this.NeedsObjectRuntime = false;
        this.Externals.Clear();
        this.Statics.Clear();
    }

    internal EmissionFunction AddFunction(FunctionAbi abi, bool exported)
    {
        if (this.IsComplete)
        {
            throw new InvalidOperationException("A completed module is closed.");
        }

        if (this.functionCount == this.functions.Count)
        {
            this.functions.Add(new());
        }

        var function = this.functions[this.functionCount++];
        function.Reset(abi, exported);
        return function;
    }

    // Withdraws the most recently added function after its lowering was refused.
    internal void RemoveLastFunction()
        => this.functionCount--;

    internal bool Complete(out string? failure)
    {
        this.IsComplete = false;
        if (this.RuntimeHelpers.HasUnresolvedReference())
        {
            failure = "Dictionary helper has an unresolved physical function reference.";
            return false;
        }

        failure = null;
        this.IsComplete = true;
        return true;
    }

    internal void WriteIr(TextWriter output)
    {
        if (!this.IsComplete)
        {
            throw new InvalidOperationException("LLVM writing requires a complete emission module.");
        }

        LlvmModuleWriter.Write(this, output);
    }
}
