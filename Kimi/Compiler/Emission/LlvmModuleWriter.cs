// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler;

/// <summary>
/// Serializes a closed <see cref="EmissionModule"/> directly into the destination writer. It never reads Binding,
/// AST or ownership state, and formats numbers on the stack so warm writes allocate nothing.
/// </summary>
internal static partial class LlvmModuleWriter
{
    // Every generated definition carries the same profile attributes (SPEC 21.5.1).
    private const string Footer =
        "attributes #0 = { uwtable(" + WindowsProfile.UnwindTables + ") \"target-cpu\"=\"" + WindowsProfile.Cpu + "\" \"target-features\"=\"" + WindowsProfile.Features +
        "\" \"denormal-fp-math\"=\"ieee,ieee\" }\n!llvm.module.flags = !{!0}\n!0 = !{i32 8, !\"PIC Level\", i32 2}\n";

    // Fixed profile vocabulary: no per-module tracking or warm declaration construction.
    // The runtime Array capacity routines and the aggregate helpers copy bytes through these intrinsics.
    private const string MemoryDeclarations = "declare void @llvm.memcpy.p0.p0.i64(ptr noalias nocapture writeonly, ptr noalias nocapture readonly, i64, i1 immarg)\ndeclare void @llvm.memmove.p0.p0.i64(ptr nocapture writeonly, ptr nocapture readonly, i64, i1 immarg)\n";

    private const string OverflowDeclarations = """
        declare { i8, i1 } @llvm.sadd.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.ssub.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.smul.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.uadd.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.usub.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.umul.with.overflow.i8(i8, i8)
        declare { i16, i1 } @llvm.sadd.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.ssub.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.smul.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.uadd.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.usub.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.umul.with.overflow.i16(i16, i16)
        declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.ssub.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.smul.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.uadd.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.usub.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.umul.with.overflow.i32(i32, i32)
        declare { i64, i1 } @llvm.sadd.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.ssub.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.smul.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.uadd.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.usub.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.umul.with.overflow.i64(i64, i64)
        """ + "\n";

    // Target information, shared Types, and exactly one strong _fltused definition (SPEC 21.5.7).
    private static readonly string Header =
        "; Kimigayo checked pre-optimization IR (" + WindowsProfile.Name + ")\ntarget triple = \"" + WindowsProfile.Target + "\"\ntarget datalayout = \"" + WindowsProfile.DataLayout + "\"\n" +
        WindowsLowering.String.Layout.StorageType + " = type { ptr, i64, i8 }\n@" + WindowsProfile.FloatMarker + " = global i32 0, align 4\n";

    private static readonly string Runtime = ReadRuntime();
    private static readonly string FormattingRuntime = ReadFormattingRuntime();

    private static readonly string TestRuntimeBase = Runtime
        .Replace(WindowsLowering.Abort.GetDefinition(false) + "entry:\n", WindowsLowering.Abort.GetDefinition(false) + "entry:\n  call void @__kimi_test_aborted()\n", StringComparison.Ordinal)
        .Replace(WindowsLowering.AbortMessage.GetDefinition(false) + "entry:\n", WindowsLowering.AbortMessage.GetDefinition(false) + "entry:\n  call void @__kimi_test_aborted()\n", StringComparison.Ordinal);

    internal static void Write(EmissionModule module, TextWriter output)
    {
        output.Write(Header);
        var constants = module.Constants;
        for (var i = 0; i < constants.Count; i++)
        {
            output.Write(constants[i].Definition);
        }

        output.Write(module.TestRuntime is null ? Runtime : TestRuntimeBase);
        output.Write(module.TestRuntime);
        WriteExternals(module, output);
        output.Write(OverflowDeclarations);
        WriteWideOverflowDeclarations(module, output);
        if (module.Aggregates.Count != 0 || module.TestRuntime is not null || module.NeedsArrayRuntime || module.NeedsFormattingRuntime)
        {
            output.Write(MemoryDeclarations);
            WriteArrayFillHelper(module, output);
            foreach (var aggregate in module.Aggregates)
            {
                WriteAggregateDestructor(output, aggregate);
            }
        }

        if (module.NeedsArrayRuntime)
        {
            output.Write(ArrayRuntime);
            WriteArrayHelpers(module, output);
        }

        if (module.NeedsFormattingRuntime)
        {
            output.Write(FormattingRuntime);
            WriteFormattingWrappers(module, output);
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

        output.Write(Footer);
    }

    // The LLVM spelling after @ of an external symbol: the plain identifier, or a quoted
    // name whose UTF-8 bytes other than printable ASCII, " and \ are written as \XX.
    internal static string ExternalName(string symbol)
    {
        var plain = symbol.Length != 0 && !char.IsAsciiDigit(symbol[0]);
        foreach (var c in symbol)
        {
            plain &= char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '$' or '-';
        }

        if (plain)
        {
            return symbol;
        }

        var text = new StringBuilder(symbol.Length + 8).Append('"');
        foreach (var b in Encoding.UTF8.GetBytes(symbol))
        {
            if (b is >= 0x20 and < 0x7F and not (byte)'"' and not (byte)'\\')
            {
                text.Append((char)b);
            }
            else
            {
                text.Append('\\').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return text.Append('"').ToString();
    }

    // Recovers the external symbol from its ExternalName spelling.
    internal static string SymbolFromName(ReadOnlySpan<char> name)
    {
        if (name.Length < 2 || name[0] != '"' || name[^1] != '"')
        {
            return name.ToString();
        }

        name = name[1..^1];
        var bytes = new byte[name.Length];
        var count = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\' && i + 2 < name.Length && byte.TryParse(name.Slice(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var escaped))
            {
                bytes[count++] = escaped;
                i += 2;
            }
            else
            {
                bytes[count++] = (byte)name[i];
            }
        }

        return Encoding.UTF8.GetString(bytes, 0, count);
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

    // SPEC 22.3: one declaration per foreign symbol. A kernel32 import that the written runtime already
    // declares shares that declaration; Binding proved their physical Types equal (SPEC 21.5.2).
    private static void WriteExternals(EmissionModule module, TextWriter output)
    {
        foreach (var external in module.Externals)
        {
            var abi = external.Abi;
            if (Declares(module.TestRuntime is null ? Runtime : TestRuntimeBase, abi.Name) || (module.TestRuntime is { } test && Declares(test, abi.Name)))
            {
                continue;
            }

            output.Write(external.DllImport ? "declare dllimport " : "declare ");
            output.Write(abi.Result);
            output.Write(" @");
            output.Write(abi.Name);
            output.Write('(');
            for (var i = 0; i < abi.Parameters.Length; i++)
            {
                if (i != 0)
                {
                    output.Write(", ");
                }

                output.Write(abi.Parameters[i].Type);
            }

            output.Write(")\n");
        }

        static bool Declares(string runtime, string name)
        {
            for (var index = runtime.IndexOf(name, StringComparison.Ordinal); index >= 0; index = runtime.IndexOf(name, index + 1, StringComparison.Ordinal))
            {
                var end = index + name.Length;
                if (index != 0 && runtime[index - 1] == '@' && end < runtime.Length && runtime[end] == '(' &&
                    runtime.LastIndexOf('\n', index) is var line && runtime.AsSpan(line + 1).StartsWith("declare ", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
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
                            output.Write($"  %p{p} = getelementptr i8, ptr %environment, i64 {capture.Value}\n");
                        }
                    }
                }
            }
        }

        // Fixed-size allocas precede calls in the entry block (SPEC 21.5.5).
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
                    WriteErasure(output, function, instruction);
                    break;

                case EmissionOpcode.CallValue:
                    WriteValueCall(output, function, instruction);
                    break;

                case EmissionOpcode.ReturnVoid:
                    output.Write("  ret void\n");
                    break;

                case EmissionOpcode.Unreachable:
                    output.Write("  unreachable\n");
                    break;

                default:
                    WriteScalar(output, constants, instruction, function.GetOperands(instruction));
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

    private static void WriteCall(TextWriter output, LlvmConstantPool constants, FunctionAbi callee, ReadOnlySpan<EmissionOperand> operands, int result = -1, EmissionFunction? function = null)
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
        output.Write(" @");
        output.Write(callee.Name);
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
                    WriteOperand(output, operand);
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

                output.Write("declare { i128, i1 } @llvm.sadd.with.overflow.i128(i128, i128)\n" +
                    "declare { i128, i1 } @llvm.ssub.with.overflow.i128(i128, i128)\n" +
                    "declare { i128, i1 } @llvm.smul.with.overflow.i128(i128, i128)\n" +
                    "declare { i128, i1 } @llvm.uadd.with.overflow.i128(i128, i128)\n" +
                    "declare { i128, i1 } @llvm.usub.with.overflow.i128(i128, i128)\n" +
                    "declare { i128, i1 } @llvm.umul.with.overflow.i128(i128, i128)\n");
                return;
            }
        }
    }

    // TextWriter.Write(long) formats through a temporary string; format on the stack instead.
    private static void WriteNumber(TextWriter output, long value)
    {
        Span<char> digits = stackalloc char[20];
        value.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        output.Write(digits[..length]);
    }

    private static void WriteNumber(TextWriter output, Int128 value)
    {
        if (value >= long.MinValue && value <= long.MaxValue)
        {
            WriteNumber(output, (long)value);
            return;
        }

        Span<char> digits = stackalloc char[40];
        value.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        output.Write(digits[..length]);
    }

    private static string ReadRuntime()
    {
        using var stream = typeof(LlvmModuleWriter).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission.WindowsRuntime.ll.in")!;
        using var reader = new StreamReader(stream);
        var runtime = WindowsLowering.ExpandAbortReasons(reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal));
        foreach (var abi in WindowsLowering.RuntimeDefinitions)
        {
            // Compiler-facing runtime signatures come from the same FunctionAbi records as calls; each is defined once.
            var marker = "{{" + abi.Name + "}}\n";
            var index = runtime.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0 || runtime.IndexOf(marker, index + marker.Length, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException("The runtime template must contain each shared ABI definition marker exactly once.");
            }

            runtime = runtime.Replace(marker, abi.GetDefinition(exported: false), StringComparison.Ordinal);
        }

        if (runtime.Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The runtime template has an unexpanded ABI definition.");
        }

        return runtime + "\n";
    }
}
