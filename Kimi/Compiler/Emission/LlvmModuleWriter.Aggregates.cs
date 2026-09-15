// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteAggregate(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var layout = instruction.Aggregate!;
        if (instruction.Opcode == EmissionOpcode.TransferAggregate)
        {
            output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr align ");
            WriteNumber(output, layout.Value.Layout.Alignment);
            output.Write(' ');
            if (instruction.OperandCount == 2)
            {
                WriteOperand(output, function.GetOperands(instruction)[1]);
            }
            else
            {
                WriteSlot(output, function, instruction.Place);
            }

            output.Write(", ptr align ");
            WriteNumber(output, layout.Value.Layout.Alignment);
            output.Write(' ');
            if (instruction.OperandCount >= 1)
            {
                var source = function.GetOperands(instruction)[0];
                if (source.Kind == EmissionOperandKind.SlotAddress)
                {
                    WriteSlot(output, function, (int)source.Value);
                }
                else
                {
                    WriteOperand(output, source);
                }
            }
            else
            {
                WriteSlot(output, function, instruction.Constant);
            }

            output.Write(", i64 ");
            WriteNumber(output, layout.Value.Layout.Size);
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
        WriteSlot(output, function, instruction.Place);
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
