// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Compact physical plan records shared by lowering and serialization.

/// <summary>A physical instruction. Each opcode has exactly one serialization rule in <see cref="LlvmModuleWriter"/>.</summary>
internal enum EmissionOpcode : byte
{
    Label,
    Branch,
    ConditionalBranch,
    LoadScalar,
    StoreScalar,
    Scalar,
    Phi,

    /// <summary>First placement of a Static string literal into <c>Place</c>'s slot; <c>Constant</c> is -1 for the empty literal.</summary>
    StoreStaticString,

    /// <summary>A direct call of <c>Callee</c> with prepared operands.</summary>
    Call,

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
    Block,

    /// <summary>The address of the slot prepared for a Place ID.</summary>
    SlotAddress,

    /// <summary>The address of a pooled constant.</summary>
    ConstantAddress,

    /// <summary>The UTF-8 byte length of a pooled constant.</summary>
    ConstantLength,

    /// <summary>An integer of the parameter's ABI Type.</summary>
    Integer,
}

internal readonly record struct EmissionOperand(EmissionOperandKind Kind, long Value);

internal readonly record struct EmissionSlot(int Place, ValueLowering Value);

internal enum ArithmeticCheckKind : byte
{
    None,
    Overflow,
    Division,
    UnsignedDivision,
    Shift,
}

/// <summary>One instruction; <c>Operation</c> is the source ownership operation ID, or -1 for synthesized startup control.</summary>
internal readonly record struct EmissionInstruction(EmissionOpcode Opcode, int Operation, int Place = -1, int Constant = -1, FunctionAbi? Callee = null, int OperandStart = 0, int OperandCount = 0, string? ScalarType = null, string? ScalarOperator = null, ArithmeticCheckKind Check = ArithmeticCheckKind.None, bool IsComparison = false, ValueLowering? Representation = null, ValueLowering? CountRepresentation = null);

/// <summary>One physical function definition. Its lists are reused by later preparations.</summary>
internal sealed class EmissionFunction
{
    internal FunctionAbi Abi { get; private set; } = null!;

    internal bool Exported { get; private set; }

    internal List<EmissionSlot> Slots { get; } = new();

    internal List<EmissionInstruction> Instructions { get; } = new();

    internal List<EmissionOperand> Operands { get; } = new();

    internal ReadOnlySpan<EmissionOperand> GetOperands(in EmissionInstruction instruction)
        => CollectionsMarshal.AsSpan(this.Operands).Slice(instruction.OperandStart, instruction.OperandCount);

    internal void Reset(FunctionAbi abi, bool exported)
    {
        this.Abi = abi;
        this.Exported = exported;
        this.Slots.Clear();
        this.Instructions.Clear();
        this.Operands.Clear();
    }

    internal void Add(EmissionOpcode opcode, int operation, int place = -1, int constant = -1)
        => this.Instructions.Add(new(opcode, operation, place, constant));

    internal void AddCall(int operation, FunctionAbi callee, ReadOnlySpan<EmissionOperand> operands)
    {
        if (operands.Length != callee.Parameters.Length)
        {
            throw new InvalidOperationException("Call operands do not match the prepared function ABI.");
        }

        var start = this.Operands.Count;
        this.Operands.AddRange(operands);
        this.Instructions.Add(new(EmissionOpcode.Call, operation, Callee: callee, OperandStart: start, OperandCount: operands.Length));
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

    internal bool IsComplete { get; private set; }

    internal int FunctionCount => this.functionCount;

    internal EmissionFunction GetFunction(int index)
        => (uint)index < (uint)this.functionCount ? this.functions[index] : throw new ArgumentOutOfRangeException(nameof(index));

    internal void Clear()
    {
        this.IsComplete = false;
        this.functionCount = 0;
        this.Constants.Clear();
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

    internal void Complete()
        => this.IsComplete = true;

    internal void WriteIr(TextWriter output)
    {
        if (!this.IsComplete)
        {
            throw new InvalidOperationException("LLVM writing requires a complete emission module.");
        }

        LlvmModuleWriter.Write(this, output);
    }
}
