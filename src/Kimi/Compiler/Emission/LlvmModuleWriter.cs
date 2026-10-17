// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
    private static readonly string FormattingRuntime = ReadFormattingRuntime();

    internal static void Write(EmissionModule module, TextWriter output)
    {
        var constants = module.Constants;
        LlvmModuleText.WriteHeader(output, constants);
        LlvmModuleText.WriteRuntime(output, module.TestRuntime);
        LlvmModuleText.WriteExternals(output, CollectionsMarshal.AsSpan(module.Externals), module.TestRuntime);
        WriteStatics(module, output);
        output.Write(LlvmModuleText.OverflowDeclarations);
        WriteWideOverflowDeclarations(module, output);
        if (module.Aggregates.Count != 0 || module.TestRuntime is not null || module.NeedsArrayRuntime || module.NeedsFormattingRuntime)
        {
            output.Write(LlvmModuleText.MemoryDeclarations);
            WriteArrayFillHelper(module, output);
            foreach (var aggregate in module.Aggregates)
            {
                WriteAggregateDestructor(output, aggregate);
            }
        }

        if (module.NeedsArrayRuntime)
        {
            output.Write(ArrayRuntime);
        }

        WriteArrayHelpers(module, output);

        if (module.NeedsFormattingRuntime)
        {
            output.Write(FormattingRuntime);
            WriteFormattingWrappers(module, output);
        }

        if (module.NeedsStorageBytes)
        {
            output.Write(StorageBytePrimitives);
        }

        if (module.NeedsDictionaryRuntime)
        {
            output.Write(DictionaryDuplicateAdapter);
            WriteDictionaryStorage(module, output);
            WriteDictionaryHelpers(module, output);
        }

        if (module.NeedsStringComparison)
        {
            output.Write(StringComparisons);
        }

        for (var i = 0; i < module.FunctionCount; i++)
        {
            WriteFunction(output, constants, module.GetFunction(i));
        }

        WriteObjects(module, output);

        output.Write(LlvmModuleText.Footer);
    }

    private static string ReadFormattingRuntime()
    {
        using var stream = typeof(LlvmModuleWriter).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission.Utf8BufferRuntime.ll.in")!;
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        using var formatting = typeof(LlvmModuleWriter).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission.Utf8FormatRuntime.ll.in")!;
        using var formatReader = new StreamReader(formatting);
        text += "\n" + formatReader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var name in new[] { "Utf8FloatRuntime.ll.in", "Utf8FloatRyu.ll.in" })
        {
            using var resource = typeof(LlvmModuleWriter).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission." + name)!;
            using var source = new StreamReader(resource);
            text += "\n" + source.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        for (var kind = CompilerFunctionKind.TextFixed; kind <= CompilerFunctionKind.BuiltinFormat; kind++)
        {
            if (WindowsLowering.GetFormattingFunction(kind) is { } function)
            {
                text = text.Replace("{{" + function.Name + "}}\n", function.GetDefinition(false), StringComparison.Ordinal);
            }
        }

        return text.Replace("{{reason_argument_range}}", Reason(WindowsLowering.ArgumentRangeReason), StringComparison.Ordinal)
            .Replace("{{reason_format}}", Reason(WindowsLowering.FormatReason), StringComparison.Ordinal)
            .Replace("{{reason_size}}", Reason(WindowsLowering.AllocationSizeReason), StringComparison.Ordinal);
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
                    WriteNumber(output, WindowsLowering.StaticReleaseKind);
                    output.Write(" }, ptr ");
                    WriteSlot(output, function, instruction.Place);
                    output.Write(", align ");
                    WriteNumber(output, WindowsLowering.String.Layout.Alignment);
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
                    output.Write("  %testphase");
                    WriteNumber(output, instruction.Operation);
                    output.Write(" = load i32, ptr @__kimi_test_phase\n  store i32 2, ptr @__kimi_test_phase\n");
                    break;

                case EmissionOpcode.TestPhaseLeave:
                    output.Write("  store i32 %testphase");
                    WriteNumber(output, instruction.Operation);
                    output.Write(", ptr @__kimi_test_phase\n");
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
    {
        var address = function.SlotAddresses[place];
        switch (address.Kind)
        {
            case EmissionOperandKind.SlotAddress:
            case EmissionOperandKind.ProjectedSlot:
                Name(output, "%p", (int)address.Value);
                break;
            case EmissionOperandKind.Argument:
                WriteOperand(output, address);
                break;
            case EmissionOperandKind.ReturnAddress:
                output.Write("%ret");
                break;
            case EmissionOperandKind.CaptureAddress:
                Name(output, "%p", place);
                break;
            default:
                throw new InvalidOperationException("Unprepared slot address.");
        }
    }

    private static void WriteStorageAddress(TextWriter output, EmissionFunction function, EmissionOperand address)
    {
        if (address.Kind == EmissionOperandKind.EnvironmentAddress)
        {
            output.Write("%environmentSlot");
        }
        else if (address.Kind == EmissionOperandKind.SlotAddress)
        {
            WriteSlot(output, function, (int)address.Value);
        }
        else
        {
            WriteOperand(output, address);
        }
    }

    // A library record Field at its named layout offset: offset 0 is the record's own address, and a nonzero offset is
    // reached through `{name}{id}_{offset}`, which DefineField writes before its use.
    private static void DefineField(TextWriter output, EmissionFunction function, EmissionOperand record, string name, int id, Int128 offset)
    {
        if (offset != 0)
        {
            output.Write("  ");
            WriteField(output, function, record, name, id, offset);
            output.Write(" = getelementptr i8, ptr ");
            WriteStorageAddress(output, function, record);
            output.Write(", i64 ");
            WriteNumber(output, offset);
            output.Write('\n');
        }
    }

    private static void WriteField(TextWriter output, EmissionFunction function, EmissionOperand record, string name, int id, Int128 offset)
    {
        if (offset == 0)
        {
            WriteStorageAddress(output, function, record);
            return;
        }

        Name(output, name, id);
        output.Write('_');
        WriteNumber(output, offset);
    }

    private static void WriteCall(TextWriter output, LlvmConstantPool constants, FunctionAbi callee, ReadOnlySpan<EmissionOperand> operands, int result = -1, EmissionFunction? function = null, bool indirect = false)
    {
        if (callee.Result != WindowsLowering.Unit.ComputationType)
        {
            if (result < 0)
            {
                throw new InvalidOperationException("Call results need prepared result values.");
            }

            Name(output, "  %v", result);
            output.Write(" = ");
        }

        output.Write(callee.Result == WindowsLowering.Unit.ComputationType ? "  call " : "call ");
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
