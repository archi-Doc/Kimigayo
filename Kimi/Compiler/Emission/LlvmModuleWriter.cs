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

    // Target information, shared Types, and exactly one strong _fltused definition (SPEC 21.5.7).
    private static readonly string Header =
        "; Kimigayo checked pre-optimization IR (" + WindowsProfile.Name + ")\ntarget triple = \"" + WindowsProfile.Target + "\"\ntarget datalayout = \"" + WindowsProfile.DataLayout + "\"\n" +
        WindowsLowering.String.Layout.StorageType + " = type { ptr, i64, i8 }\n@" + WindowsProfile.FloatMarker + " = global i32 0, align 4\n";

    private static readonly string Runtime = ReadRuntime();

    internal static void Write(EmissionModule module, TextWriter output)
    {
        output.Write(Header);
        var constants = module.Constants;
        for (var i = 0; i < constants.Count; i++)
        {
            output.Write(constants[i].Definition);
        }

        output.Write(Runtime);
        output.Write("declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)\ndeclare { i32, i1 } @llvm.ssub.with.overflow.i32(i32, i32)\ndeclare { i32, i1 } @llvm.smul.with.overflow.i32(i32, i32)\n");
        for (var i = 0; i < module.FunctionCount; i++)
        {
            WriteFunction(output, constants, module.GetFunction(i));
        }

        output.Write(Footer);
    }

    private static void WriteFunction(TextWriter output, LlvmConstantPool constants, EmissionFunction function)
    {
        output.Write(function.Abi.GetDefinition(function.Exported));
        output.Write("entry:\n");
        // Fixed-size allocas precede calls in the entry block (SPEC 21.5.5).
        foreach (var slot in function.Slots)
        {
            output.Write("  %p");
            WriteNumber(output, slot.Place);
            output.Write(" = alloca ");
            output.Write(slot.Value.Layout.StorageType);
            output.Write(", align ");
            WriteNumber(output, slot.Value.Layout.Alignment);
            output.Write('\n');
        }

        foreach (var instruction in function.Instructions)
        {
            switch (instruction.Opcode)
            {
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
                    output.Write(" }, ptr %p");
                    WriteNumber(output, instruction.Place);
                    output.Write(", align ");
                    WriteNumber(output, WindowsLowering.String.Layout.Alignment);
                    output.Write('\n');
                    break;

                case EmissionOpcode.Call:
                    WriteCall(output, constants, instruction.Callee!, function.GetOperands(instruction));
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

    private static void WriteCall(TextWriter output, LlvmConstantPool constants, FunctionAbi callee, ReadOnlySpan<EmissionOperand> operands)
    {
        if (callee.Result != WindowsLowering.Unit.ComputationType)
        {
            throw new InvalidOperationException("Call results need prepared result values.");
        }

        output.Write("  call ");
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
            output.Write(' ');
            var operand = operands[i];
            switch (operand.Kind)
            {
                case EmissionOperandKind.SlotAddress:
                    output.Write("%p");
                    WriteNumber(output, operand.Value);
                    break;
                case EmissionOperandKind.ConstantAddress:
                    output.Write('@');
                    output.Write(constants[(int)operand.Value].Name);
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

    // TextWriter.Write(long) formats through a temporary string; format on the stack instead.
    private static void WriteNumber(TextWriter output, long value)
    {
        Span<char> digits = stackalloc char[20];
        value.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        output.Write(digits[..length]);
    }

    private static string ReadRuntime()
    {
        using var stream = typeof(LlvmModuleWriter).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission.WindowsRuntime.ll.in")!;
        using var reader = new StreamReader(stream);
        var runtime = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
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
