// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Backend;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler;

/// <summary>
/// Serializes a closed <see cref="EmissionModule"/> directly into the destination writer, the module frame through
/// <see cref="LlvmModuleText"/>. It never reads Binding, AST or ownership state, and formats numbers on the stack so warm
/// writes allocate nothing.
/// </summary>
internal static partial class LlvmModuleWriter
{
    internal static void Write(EmissionModule module, TextWriter output)
    {
        var constants = module.Constants;
        var helpers = module.RuntimeHelpers;
        LlvmModuleText.WriteHeader(output, constants);
        LlvmModuleText.WriteRuntime(output, module.TestRuntime);
        LlvmModuleText.WriteExternals(output, CollectionsMarshal.AsSpan(module.Externals), module.TestRuntime);
        WriteStatics(module, output);
        output.Write(LlvmModuleText.OverflowDeclarations);
        WriteWideOverflowDeclarations(module, output);
        if (module.Aggregates.Count != 0 || module.TestRuntime is not null || helpers.NeedsArrayRuntime || helpers.NeedsFormattingRuntime)
        {
            output.Write(LlvmModuleText.MemoryDeclarations);
            WriteArrayFillHelper(module, output);
            foreach (var aggregate in module.Aggregates)
            {
                WriteAggregateDestructor(output, aggregate);
            }
        }

        helpers.WriteArrays(output);
        helpers.WriteFormatting(output);
        helpers.WriteDictionaries(output);

        if (helpers.NeedsStringComparison)
        {
            output.Write(LegacyRuntimeHelpers.StringComparisons);
        }

        for (var i = 0; i < module.FunctionCount; i++)
        {
            WriteFunction(output, constants, module.GetFunction(i));
        }

        WriteObjects(module, output);

        output.Write(LlvmModuleText.Footer);
    }

    private static void WriteFunction(TextWriter output, LlvmConstantPool constants, EmissionFunction function)
    {
        foreach (var instruction in function.Instructions)
        {
            if (instruction.Opcode == EmissionOpcode.EraseClosure)
            {
                WriteErasureAdapter(output, function, instruction);
            }

            if (instruction.Opcode == EmissionOpcode.CreateClosure && instruction.Aggregate is null)
            {
                output.Write('@');
                WriteClosureTableName(output, function, instruction.Operation);
                output.Write(" = private constant { ptr, ptr, ptr } { ptr @");
                output.Write(instruction.Callee!.Name);
                output.Write(", ptr null, ptr null }, align 8\n");
            }

            if (instruction.Opcode == EmissionOpcode.CompositePattern)
            {
                WriteCompositePatternHelper(output, constants, function, instruction);
            }
        }

        output.Write(function.Abi.GetDefinition(function.Exported));
        output.Write("entry:\n");
        foreach (var parameter in function.Abi.Parameters)
        {
            if (parameter.Kind == AbiParameterKind.Environment)
            {
                if (parameter.Type == "i64")
                {
                    output.Write("  %environmentSlot = alloca i64, align 8\n  store i64 %environment, ptr %environmentSlot, align 8\n");
                }
                else
                {
                    for (var p = 0; p < function.SlotAddresses.Count; p++)
                    {
                        if (function.SlotAddresses[p] is { Kind: EmissionOperandKind.CaptureAddress } capture)
                        {
                            Name(output, "  %p", p);
                            output.Write(" = getelementptr i8, ptr %environment, i64 ");
                            WriteNumber(output, capture.Value);
                            output.Write('\n');
                        }
                    }
                }
            }
        }

        // Fixed-size allocas precede calls in the entry block (SPEC 21.2).
        foreach (var slot in function.Slots)
        {
            output.Write("  %p");
            WriteNumber(output, slot.Place);
            output.Write(" = alloca ");
            output.Write(slot.Value.Layout.Size == 0 ? "i8" : slot.Value.Layout.StorageType);
            output.Write(", align ");
            WriteNumber(output, slot.Value.Layout.Alignment);
            output.Write('\n');
        }

        for (var i = 0; i < function.FormattingStacks.Count; i++)
        {
            Name(output, "  %formatBytes", i);
            output.Write(" = alloca [");
            WriteNumber(output, function.FormattingStacks[i]);
            output.Write(" x i8], align 1\n");
        }

        foreach (var path in function.PathFlags)
        {
            Name(output, "  %pathSlot", path);
            output.Write(" = alloca i8, align 1\n  store i8 0, ptr ");
            Name(output, "%pathSlot", path);
            output.Write(", align 1\n");
        }

        foreach (var place in function.LiveFlags)
        {
            Name(output, "  %liveSlot", place);
            output.Write(" = alloca i8, align 1\n");
        }

        foreach (var slot in function.Subslots)
        {
            Name(output, "  %p", slot.Place);
            output.Write(" = getelementptr i8, ptr ");
            if (slot.Parent < 0)
            {
                output.Write("%ret"); // Dedicated construction/destruction receiver address.
            }
            else
            {
                WriteSlot(output, function, slot.Parent);
            }

            output.Write(", i64 ");
            WriteNumber(output, slot.Offset);
            output.Write('\n');
        }

        foreach (var instruction in function.Instructions)
        {
            switch (instruction.Opcode)
            {
                case EmissionOpcode.DestroyPart:
                case EmissionOpcode.EndPartDestruction:
                case EmissionOpcode.StorePathFlag:
                    WritePartDestruction(output, constants, function, instruction);
                    break;
                case EmissionOpcode.TransferAggregate:
                case EmissionOpcode.FillArray:
                case EmissionOpcode.DestroyAggregate:
                    WriteAggregate(output, constants, function, instruction);
                    break;
                case EmissionOpcode.StringPattern:
                    WriteStringPattern(output, constants, function, instruction);
                    continue;
                case EmissionOpcode.CompositePattern:
                    Name(output, "  %v", instruction.Operation);
                    output.Write(" = call i1 @");
                    WritePatternName(output, function, instruction.Operation);
                    output.Write("(ptr ");
                    WriteSlot(output, function, instruction.Place);
                    output.Write(")\n");
                    break;
                case EmissionOpcode.PatternRead:
                    WritePatternRead(output, function, instruction);
                    break;
                case EmissionOpcode.MoveString:
                case EmissionOpcode.DestroyStringIfLive:
                case EmissionOpcode.StoreLiveFlag:
                case EmissionOpcode.InitializeLiveFlag:
                    WriteString(output, constants, function, instruction, function.GetOperands(instruction));
                    break;
                case EmissionOpcode.Sequence:
                    WriteSequence(output, constants, function, instruction);
                    break;
                case EmissionOpcode.ElementAddress:
                    WriteElementAddress(output, constants, function, instruction);
                    break;
                case EmissionOpcode.BorrowAddress:
                    Name(output, "  %v", instruction.Operation);
                    output.Write(" = getelementptr i8, ptr ");
                    var address = function.GetOperands(instruction)[0];
                    if (address.Kind == EmissionOperandKind.SlotAddress)
                    {
                        WriteSlot(output, function, (int)address.Value);
                    }
                    else
                    {
                        WriteOperand(output, address);
                    }

                    output.Write(", i64 ");
                    var borrowOperands = function.GetOperands(instruction);
                    output.Write(borrowOperands.Length == 2 ? (long)borrowOperands[1].Value : 0);
                    output.Write('\n');
                    break;
                case EmissionOpcode.ObjectBorrow:
                    Name(output, "  %v", instruction.Operation);
                    output.Write(" = load ptr, ptr ");
                    WriteStorageAddress(output, function, function.GetOperands(instruction)[0]);
                    output.Write(", align 8\n");
                    break;
                case EmissionOpcode.ObjectPayload:
                    Name(output, "  %objectHeader", instruction.Operation);
                    output.Write(" = load ptr, ptr ");
                    WriteStorageAddress(output, function, function.GetOperands(instruction)[0]);
                    output.Write(", align 8\n");
                    Name(output, "  %v", instruction.Operation);
                    Name(output, " = getelementptr i8, ptr %objectHeader", instruction.Operation);
                    output.Write(", i64 16\n");
                    break;
                case EmissionOpcode.ObjectTypeTest:
                    var testOperands = function.GetOperands(instruction);
                    Name(output, instruction.ScalarOperator == "not" ? "  %tested" : "  %v", instruction.Operation);
                    output.Write(" = call i1 @__kimi_object_supports(ptr ");
                    WriteOperand(output, testOperands[0]);
                    output.Write(", i64 ");
                    WriteOperand(output, testOperands[1]);
                    output.Write(")\n");
                    if (instruction.ScalarOperator == "not")
                    {
                        Name(output, "  %v", instruction.Operation);
                        Name(output, " = xor i1 %tested", instruction.Operation);
                        output.Write(", true\n");
                    }

                    break;
                case EmissionOpcode.SwapScalars:
                    WriteScalarSwap(output, function, instruction);
                    break;
                case EmissionOpcode.StringEquals:
                case EmissionOpcode.StringCompare:
                    WriteStringComparison(output, function, instruction);
                    break;
                case EmissionOpcode.BuiltinComparison:
                    WriteBuiltinComparison(output, function, instruction);
                    break;
                case EmissionOpcode.TupleRelation:
                    WriteTupleRelation(output, function, instruction);
                    break;
                case EmissionOpcode.StoreStaticString:
                    output.Write("  store %kimi.string { ptr ");
                    if (instruction.Constant < 0)
                    {
                        output.Write("null, i64 0");
                    }
                    else
                    {
                        var constant = constants[instruction.Constant];
                        output.Write('@');
                        output.Write(constant.Name);
                        output.Write(", i64 ");
                        WriteNumber(output, constant.ByteLength);
                    }

                    output.Write(", i8 ");
                    WriteNumber(output, RuntimeAbi.StaticReleaseKind);
                    output.Write(" }, ptr ");
                    WriteSlot(output, function, instruction.Place);
                    output.Write(", align ");
                    WriteNumber(output, RuntimeAbi.String.Layout.Alignment);
                    output.Write('\n');
                    break;

                case EmissionOpcode.Call:
                    WriteCall(output, constants, instruction.Callee!, function.GetOperands(instruction), instruction.Operation, function);
                    break;

                case EmissionOpcode.VirtualCall:
                    WriteVirtualCall(output, constants, function, instruction);
                    break;

                case EmissionOpcode.TestSnapshot:
                    WriteTestSnapshot(output, function, instruction);
                    break;

                case EmissionOpcode.TestPhaseEnter:
                    Testing.TestRuntime.WritePhase(output, "%testphase", instruction.Operation, Testing.TestRuntime.Phase.Cleanup);
                    break;

                case EmissionOpcode.TestPhaseLeave:
                    Testing.TestRuntime.RestorePhase(output, "%testphase", instruction.Operation);
                    break;

                case EmissionOpcode.CreateClosure:
                    WriteClosure(output, function, instruction);
                    break;

                case EmissionOpcode.EraseClosure:
                    WriteErasure(output, constants, function, instruction);
                    break;

                case EmissionOpcode.CallValue:
                    WriteValueCall(output, constants, function, instruction);
                    break;

                case EmissionOpcode.ReturnVoid:
                    output.Write("  ret void\n");
                    break;

                case EmissionOpcode.Unreachable:
                    output.Write("  unreachable\n");
                    break;

                default:
                    WriteScalar(output, constants, function, instruction, function.GetOperands(instruction));
                    break;
            }
        }

        output.Write("}\n");
    }

    private static void WriteSlot(TextWriter output, EmissionFunction function, int place)
        => output.Write(SpellSlot(function, place, stackalloc char[OperandLength]));

    // A slot's address, formatted into text, which holds OperandLength characters: its alloca or projection %p<n>, its
    // argument or the return slot.
    private static ReadOnlySpan<char> SpellSlot(EmissionFunction function, int place, Span<char> text)
    {
        var address = function.SlotAddresses[place];
        switch (address.Kind)
        {
            case EmissionOperandKind.SlotAddress:
            case EmissionOperandKind.ProjectedSlot:
                place = (int)address.Value;
                break;
            case EmissionOperandKind.Argument:
                return Spell(address, text);
            case EmissionOperandKind.ReturnAddress:
                return "%ret";
            case EmissionOperandKind.CaptureAddress:
                break;
            default:
                throw new InvalidOperationException("Unprepared slot address.");
        }

        "%p".CopyTo(text);
        place.TryFormat(text[2..], out var length, default, CultureInfo.InvariantCulture);
        return text[..(2 + length)];
    }

    private static void WriteStorageAddress(TextWriter output, EmissionFunction function, EmissionOperand address)
        => output.Write(SpellStorageAddress(function, address, stackalloc char[OperandLength]));

    // An address operand, formatted into text, which holds OperandLength characters: a slot's address, a closure's
    // environment slot or the operand's own spelling.
    private static ReadOnlySpan<char> SpellStorageAddress(EmissionFunction function, EmissionOperand address, Span<char> text) => address.Kind switch
    {
        EmissionOperandKind.EnvironmentAddress => "%environmentSlot",
        EmissionOperandKind.SlotAddress => SpellSlot(function, (int)address.Value, text),
        _ => Spell(address, text),
    };

    // A library record Field at its named layout offset (LegacyRuntimeHelpers.DefineField).
    private static void DefineField(TextWriter output, EmissionFunction function, EmissionOperand record, string name, int id, Int128 offset)
        => LegacyRuntimeHelpers.DefineField(output, SpellStorageAddress(function, record, stackalloc char[OperandLength]), name, id, (long)offset);

    private static void WriteField(TextWriter output, EmissionFunction function, EmissionOperand record, string name, int id, Int128 offset)
        => LegacyRuntimeHelpers.WriteField(output, SpellStorageAddress(function, record, stackalloc char[OperandLength]), name, id, (long)offset);

    private static void WriteCall(TextWriter output, LlvmConstantPool constants, FunctionAbi callee, ReadOnlySpan<EmissionOperand> operands, int result = -1, EmissionFunction? function = null, bool indirect = false)
    {
        if (callee.Result != RuntimeAbi.Unit.ComputationType)
        {
            if (result < 0)
            {
                throw new InvalidOperationException("Call results need prepared result values.");
            }

            Name(output, "  %v", result);
            output.Write(" = ");
        }

        output.Write(callee.Result == RuntimeAbi.Unit.ComputationType ? "  call " : "call ");
        output.Write(callee.Result);
        if (indirect)
        {
            Name(output, " %virtual_entry", result);
        }
        else
        {
            output.Write(" @");
            output.Write(callee.Name);
        }

        output.Write('(');
        for (var i = 0; i < operands.Length; i++)
        {
            if (i != 0)
            {
                output.Write(", ");
            }

            output.Write(callee.Parameters[i].Type);
            output.Write(callee.Parameters[i].Attributes);
            output.Write(' ');
            var operand = operands[i];
            switch (operand.Kind)
            {
                case EmissionOperandKind.Value:
                case EmissionOperandKind.ElementAddress:
                case EmissionOperandKind.Argument:
                case EmissionOperandKind.Float32:
                case EmissionOperandKind.Float64:
                case EmissionOperandKind.NullAddress:
                case EmissionOperandKind.CallerLocation:
                case EmissionOperandKind.CallerLocationLength:
                    WriteOperand(output, operand);
                    break;
                case EmissionOperandKind.ReturnAddress:
                    output.Write("%ret");
                    break;
                case EmissionOperandKind.SlotAddress:
                    WriteSlot(output, function ?? throw new InvalidOperationException("Slot argument without a function."), (int)operand.Value);
                    break;
                case EmissionOperandKind.ConstantAddress:
                    output.Write('@');
                    output.Write(constants[(int)operand.Value].Name);
                    break;
                case EmissionOperandKind.FunctionAddress:
                    output.Write('@');
                    output.Write((function ?? throw new InvalidOperationException("Function address without a containing function.")).FunctionAddresses[(int)operand.Value].Name);
                    break;
                case EmissionOperandKind.FormattingStack:
                    Name(output, "%formatBytes", (int)operand.Value);
                    break;
                case EmissionOperandKind.ConstantLength:
                    WriteNumber(output, constants[(int)operand.Value].ByteLength);
                    break;
                default:
                    WriteNumber(output, operand.Value);
                    break;
            }
        }

        output.Write(")\n");
    }

    private static void WriteWideOverflowDeclarations(EmissionModule module, TextWriter output)
    {
        for (var i = 0; i < module.FunctionCount; i++)
        {
            foreach (var instruction in module.GetFunction(i).Instructions)
            {
                if (instruction.Check != ArithmeticCheckKind.Overflow || instruction.ScalarType != "i128")
                {
                    continue;
                }

                output.Write(LlvmModuleText.WideOverflowDeclarations);
                return;
            }
        }
    }
}
