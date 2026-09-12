// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The concrete layout/value/ABI vocabulary used by Windows lowering.

// These are the implemented runtime layouts, not a fallback for arbitrary language Types.
internal sealed record TypeLayout(string StorageType, int Size, int Alignment, int Stride, ReadOnlyMemory<int> FieldOffsets);

internal sealed record ValueLowering(TypeLayout Layout, string ComputationType, string? ArgumentType);

internal readonly record struct AbiParameter(string Type, string Name);

internal readonly record struct LlvmOperand(string? Text, int Number = 0)
{
    internal void Append(StringBuilder text)
    {
        if (this.Text is null)
        {
            text.Append(this.Number);
        }
        else
        {
            text.Append(this.Text);
            if (this.Text == "%p")
            {
                text.Append(this.Number);
            }
        }
    }
}

// ccc is LLVM's default. Both definitions and calls use these same physical parameters.
internal sealed record FunctionAbi(string Name, string Result, AbiParameter[] Parameters, bool NoReturn = false)
{
    internal void AppendDefinition(StringBuilder text, bool exported = false)
    {
        text.Append(exported ? "define " : "define internal ").Append(this.Result).Append(" @").Append(this.Name).Append('(');
        for (var i = 0; i < this.Parameters.Length; i++)
        {
            if (i != 0)
            {
                text.Append(", ");
            }

            text.Append(this.Parameters[i].Type).Append(" %").Append(this.Parameters[i].Name);
        }

        text.Append(this.NoReturn ? ") noreturn #0 {\n" : ") #0 {\n");
    }

    internal void AppendCall(StringBuilder text, ReadOnlySpan<LlvmOperand> operands)
    {
        if (operands.Length != this.Parameters.Length)
        {
            throw new InvalidOperationException("Call operands do not match the prepared function ABI.");
        }

        text.Append("  call ").Append(this.Result).Append(" @").Append(this.Name).Append('(');
        for (var i = 0; i < operands.Length; i++)
        {
            if (i != 0)
            {
                text.Append(", ");
            }

            text.Append(this.Parameters[i].Type).Append(' ');
            operands[i].Append(text);
        }

        text.Append(")\n");
    }
}

internal static class WindowsLowering
{
    internal static readonly ValueLowering Unit = new(new("void", 0, 1, 0, ReadOnlyMemory<int>.Empty), "void", null);
    internal static readonly ValueLowering String = new(new("%kimi.string", 24, 8, 24, new[] { 0, 8, 16 }), "%kimi.string", "ptr");
    internal static readonly FunctionAbi Entry = new("__kimi_entry_body", Unit.ComputationType, []);
    internal static readonly FunctionAbi Start = new("__kimi_start", Unit.ComputationType, [], NoReturn: true);
    internal static readonly FunctionAbi Exit = new("__kimi_exit", Unit.ComputationType, [new("i32", "code")], NoReturn: true);
    internal static readonly FunctionAbi WriteLine = new("__kimi_write_line", Unit.ComputationType, [new(String.ArgumentType!, "text"), new("ptr", "location"), new("i64", "location_length")]);
    internal static readonly FunctionAbi DestroyString = new("__kimi_destroy_string", Unit.ComputationType, WriteLine.Parameters);

    internal static ValueLowering? GetValue(BoundType type)
        => ReferenceEquals(type, BoundType.Unit) ? Unit : ReferenceEquals(type, BoundType.String) ? String : null;

    internal static void AppendTypes(StringBuilder text)
        => text.Append(String.Layout.StorageType).Append(" = type { ptr, i64, i8 }\n");
}
