// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteSharedDirectAdapter(TextWriter output, string name, SharedDirectAdapter adapter)
    {
        output.Write($"define internal void @{name}(ptr %result");
        for (var i = 0; i < adapter.Parameters.Length; i++)
        {
            output.Write($", ptr %a{i}");
        }

        output.Write(") #0 {\nentry:\n");
        for (var i = 0; i < adapter.Parameters.Length; i++)
        {
            var value = adapter.Parameters[i];
            if (value.Layout.Size == 0 || (value.ArgumentType == "ptr" && value.ComputationType != "ptr"))
            {
                continue;
            }

            if (value.ComputationType == "i1")
            {
                output.Write($"  %byte{i} = load i8, ptr %a{i}, align 1\n  %value{i} = trunc i8 %byte{i} to i1\n");
            }
            else
            {
                output.Write($"  %value{i} = load {value.ComputationType}, ptr %a{i}, align {value.Layout.Alignment}\n");
            }
        }

        var result = adapter.Abi.Result;
        output.Write(result == "void" ? "  call void" : $"  %value = call {result}");
        output.Write($" @{adapter.Abi.Name}(");
        var comma = false;
        if (adapter.Abi.ResultSlot)
        {
            output.Write("ptr %result");
            comma = true;
        }

        for (var i = 0; i < adapter.Parameters.Length; i++)
        {
            var value = adapter.Parameters[i];
            if (value.Layout.Size == 0)
            {
                continue;
            }

            output.Write(comma ? ", " : string.Empty);
            output.Write($"{value.ArgumentType} %{(value.ArgumentType == "ptr" && value.ComputationType != "ptr" ? "a" : "value")}{i}");
            comma = true;
        }

        output.Write(")\n");
        if (result == "i1")
        {
            output.Write("  %resultByte = zext i1 %value to i8\n  store i8 %resultByte, ptr %result, align 1\n");
        }
        else if (result != "void")
        {
            output.Write($"  store {result} %value, ptr %result, align {adapter.Result.Layout.Alignment}\n");
        }

        output.Write("  ret void\n}\n");
    }

    // Only the entry adapter knows concrete parameter ABIs. The definition-side
    // shared body passes already acquired storage and keeps the receiver Loan live.
    private static void WriteSharedCallAdapter(TextWriter output, string name, SharedCallAdapter adapter)
    {
        output.Write($"define internal void @{name}(ptr %result, ptr %receiver");
        for (var i = 0; i < adapter.Parameters.Length; i++)
        {
            output.Write($", ptr %a{i}");
        }

        output.Write(") #0 {\nentry:\n  %environment = load i64, ptr %receiver, align 8\n  %tableSlot = getelementptr i8, ptr %receiver, i64 8\n  %table = load ptr, ptr %tableSlot, align 8\n  %callee = load ptr, ptr %table, align 8\n  %contextSlot = getelementptr i8, ptr %table, i64 8\n  %context = load ptr, ptr %contextSlot, align 8\n");
        for (var i = 0; i < adapter.Parameters.Length; i++)
        {
            var value = adapter.Parameters[i];
            if (value.ComputationType == "i1")
            {
                output.Write($"  %byte{i} = load i8, ptr %a{i}, align 1\n  %value{i} = trunc i8 %byte{i} to i1\n");
            }
            else
            {
                output.Write($"  %value{i} = load {value.ComputationType}, ptr %a{i}, align {value.Layout.Alignment}\n");
            }
        }

        var result = adapter.Result.ComputationType;
        output.Write(result == "void" ? "  call void" : $"  %value = call {result}");
        output.Write(" %callee(i64 %environment");
        for (var i = 0; i < adapter.Parameters.Length; i++)
        {
            output.Write($", {adapter.Parameters[i].ArgumentType} %value{i}");
        }

        output.Write(", ptr %context)\n");
        if (result == "i1")
        {
            output.Write("  %resultByte = zext i1 %value to i8\n  store i8 %resultByte, ptr %result, align 1\n");
        }
        else if (result != "void")
        {
            output.Write($"  store {result} %value, ptr %result, align {adapter.Result.Layout.Alignment}\n");
        }

        output.Write("  ret void\n}\n");
    }
}
