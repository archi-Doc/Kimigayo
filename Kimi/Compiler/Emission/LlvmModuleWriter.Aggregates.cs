// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteArrayFillHelper(EmissionModule module, TextWriter output)
    {
        for (var f = 0; f < module.FunctionCount; f++)
        {
            foreach (var instruction in module.GetFunction(f).Instructions)
            {
                if (instruction.Opcode != EmissionOpcode.FillArray)
                {
                    continue;
                }

                // Layout formation already checked count * stride. Doubling copies only initialized
                // bytes to disjoint storage; one helper serves every Copy element Type and array length.
                output.Write("""
                    define internal void @__kimi_fill_array(ptr %destination, ptr %value, i64 %count, i64 %stride) #0 {
                    entry:
                      %size = mul i64 %count, %stride
                      %empty = icmp eq i64 %size, 0
                      br i1 %empty, label %done, label %first
                    first:
                      call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %value, i64 %stride, i1 false)
                      br label %next
                    next:
                      %filled = phi i64 [ %stride, %first ], [ %end, %copy ]
                      %complete = icmp eq i64 %filled, %size
                      br i1 %complete, label %done, label %copy
                    copy:
                      %remaining = sub i64 %size, %filled
                      %last = icmp ult i64 %remaining, %filled
                      %amount = select i1 %last, i64 %remaining, i64 %filled
                      %target = getelementptr i8, ptr %destination, i64 %filled
                      call void @llvm.memcpy.p0.p0.i64(ptr %target, ptr %destination, i64 %amount, i1 false)
                      %end = add i64 %filled, %amount
                      br label %next
                    done:
                      ret void
                    }

                    """);
                return;
            }
        }
    }

    private static void WriteAggregate(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var layout = instruction.Aggregate!;
        if (instruction.Opcode == EmissionOpcode.FillArray)
        {
            output.Write("  call void @__kimi_fill_array(ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", ptr ");
            WriteSlot(output, function, instruction.Constant);
            output.Write(", i64 ");
            WriteNumber(output, layout.Count);
            output.Write(", i64 ");
            WriteNumber(output, layout.Fields[0].Layout.Stride);
            output.Write(")\n");
            return;
        }

        if (instruction.Opcode == EmissionOpcode.TransferAggregate)
        {
            var value = instruction.Representation ?? layout.Value;
            output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr align ");
            WriteNumber(output, value.Layout.Alignment);
            output.Write(' ');
            if (instruction.OperandCount == 2)
            {
                WriteStorageAddress(output, function, function.GetOperands(instruction)[1]);
            }
            else
            {
                WriteSlot(output, function, instruction.Place);
            }

            output.Write(", ptr align ");
            WriteNumber(output, value.Layout.Alignment);
            output.Write(' ');
            if (instruction.OperandCount >= 1)
            {
                WriteStorageAddress(output, function, function.GetOperands(instruction)[0]);
            }
            else
            {
                WriteSlot(output, function, instruction.Constant);
            }

            output.Write(", i64 ");
            WriteNumber(output, value.Layout.Size);
            output.Write(", i1 false)\n");
            return;
        }

        var conditional = instruction.Continuation >= 0;
        if (conditional)
        {
            Name(output, "  %live", instruction.Operation);
            Name(output, " = load i8, ptr %liveSlot", instruction.Place);
            output.Write(", align 1\n");
            Name(output, "  %hasLive", instruction.Operation);
            Name(output, " = icmp ne i8 %live", instruction.Operation);
            output.Write(", 0\n");
            Name(output, "  br i1 %hasLive", instruction.Operation);
            Name(output, ", label %destroy", instruction.Operation);
            Name(output, ", label %b", instruction.Continuation);
            output.Write('\n');
            Name(output, "destroy", instruction.Operation);
            output.Write(":\n");
        }

        Name(output, "  call void @__kimi_drop_aggregate", layout.Id);
        output.Write("(ptr ");
        if (instruction.OperandCount == 1)
        {
            WriteStorageAddress(output, function, function.GetOperands(instruction)[0]);
        }
        else
        {
            WriteSlot(output, function, instruction.Place);
        }

        output.Write(", ptr @");
        output.Write(constants[instruction.Constant].Name);
        output.Write(", i64 ");
        WriteNumber(output, constants[instruction.Constant].ByteLength);
        output.Write(")\n");
        if (conditional)
        {
            Name(output, "  br label %b", instruction.Continuation);
            output.Write('\n');
            Name(output, "b", instruction.Continuation);
            output.Write(":\n");
        }
    }

    private static void WriteAggregateDestructor(TextWriter output, AggregateLayout aggregate)
    {
        if (!aggregate.NeedsDestruction)
        {
            return;
        }

        Name(output, "define internal void @__kimi_drop_aggregate", aggregate.Id);
        output.Write("(ptr %slot, ptr %location, i64 %length) #0 {\nentry:\n");
        if (aggregate.ObjectHandle)
        {
            output.Write("  call void @__kimi_drop_object(ptr %slot, ptr %location, i64 %length)\n  ret void\n}\n");
            return;
        }

        if (aggregate.FunctionHandle)
        {
            output.Write("  %word = load i64, ptr %slot, align 8\n  %tableSlot = getelementptr i8, ptr %slot, i64 8\n  %table = load ptr, ptr %tableSlot, align 8\n  %dropSlot = getelementptr i8, ptr %table, i64 16\n  %drop = load ptr, ptr %dropSlot, align 8\n  %present = icmp ne ptr %drop, null\n  br i1 %present, label %destroy, label %done\ndestroy:\n  %contextSlot = getelementptr i8, ptr %table, i64 8\n  %context = load ptr, ptr %contextSlot, align 8\n  call void %drop(i64 %word, ptr %context, ptr %location, i64 %length)\n  br label %done\ndone:\n  ret void\n}\n");
            return;
        }

        if (aggregate.Destructor >= 0)
        {
            Name(output, "  call void @__kimi_f", aggregate.Destructor);
            output.Write("(ptr %slot)\n");
        }

        if (aggregate.Cases is { } cases)
        {
            output.Write("  %tag = load i32, ptr %slot, align 4\n  %payload = getelementptr i8, ptr %slot, i64 ");
            WriteNumber(output, aggregate.PayloadOffset);
            output.Write("\n  switch i32 %tag, label %invalid [\n");
            for (var i = 0; i < cases.Length; i++)
            {
                Name(output, "    i32 ", i);
                Name(output, ", label %case", i);
                output.Write('\n');
            }

            output.Write("  ]\ninvalid:\n  unreachable\n");
            for (var i = 0; i < cases.Length; i++)
            {
                Name(output, "case", i);
                output.Write(":\n");
                if (cases[i].NeedsDestruction)
                {
                    Name(output, "  call void @__kimi_drop_aggregate", cases[i].Id);
                    output.Write("(ptr %payload, ptr %location, i64 %length)\n");
                }

                output.Write("  ret void\n");
            }

            output.Write("}\n");
            return;
        }

        if (aggregate.IsArray)
        {
            output.Write("  br label %test\ntest:\n  %remaining = phi i64 [ ");
            WriteNumber(output, aggregate.Count);
            output.Write(", %entry ], [ %index, %body ]\n  %done = icmp eq i64 %remaining, 0\n  br i1 %done, label %end, label %body\nbody:\n  %index = sub i64 %remaining, 1\n  %offset = mul i64 %index, ");
            WriteNumber(output, aggregate.Fields[0].Layout.Stride);
            output.Write("\n  %field0 = getelementptr i8, ptr %slot, i64 %offset\n");
            WriteAggregateFieldDestructor(output, aggregate.Children[0], 0);
            output.Write("  br label %test\nend:\n");
        }
        else
        {
            for (var i = aggregate.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(aggregate.Fields[i], WindowsLowering.String) && aggregate.Children[i]?.NeedsDestruction != true)
                {
                    continue;
                }

                Name(output, "  %field", i);
                output.Write(" = getelementptr i8, ptr %slot, i64 ");
                WriteNumber(output, aggregate.Offset(i));
                output.Write('\n');
                WriteAggregateFieldDestructor(output, aggregate.Children[i], i);
            }
        }

        if (aggregate.Base is { NeedsDestruction: true } parent)
        {
            Name(output, "  call void @__kimi_drop_aggregate", parent.Id);
            output.Write("(ptr %slot, ptr %location, i64 %length)\n");
        }

        output.Write("  ret void\n}\n");
    }

    private static void WriteAggregateFieldDestructor(TextWriter output, AggregateLayout? child, int index)
    {
        if (child is null)
        {
            output.Write("  call void @__kimi_destroy_string");
        }
        else
        {
            Name(output, "  call void @__kimi_drop_aggregate", child.Id);
        }

        Name(output, "(ptr %field", index);
        output.Write(", ptr %location, i64 %length)\n");
    }
}
