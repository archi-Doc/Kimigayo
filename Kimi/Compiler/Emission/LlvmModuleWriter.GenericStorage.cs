// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteSharedStorage(EmissionModule module, TextWriter output)
    {
        if (module.SharedBodies.Count == 0)
        {
            return;
        }

        // Metadata executes already verified ownership operations. Context carries no live flags.
        output.Write("%kimi.shared.policy = type { i64, i64, ptr, ptr, i64, ptr }\n");
        output.Write("define internal void @__kimi_shared_drop(ptr %slot, ptr %context, ptr %location, i64 %length) #0 {\nentry:\n  ret void\n}\n");
        foreach (var entry in module.SharedEntries)
        {
            WriteSharedEntry(output, entry);
        }

        foreach (var body in module.SharedBodies)
        {
            WriteSharedBody(output, module.Constants, body);
        }
    }

    private static int SharedScalarAlignment(string type) => type switch { "i8" => 1, "i16" => 2, "i32" => 4, _ => 8 };

    private static void WriteSharedEntry(TextWriter output, SharedStorageEntry entry)
    {
        var name = entry.Abi.Name;
        output.Write($"@{name}_direct = private constant [{entry.DirectCalls.Length} x ptr] [");
        for (var i = 0; i < entry.DirectCalls.Length; i++)
        {
            output.Write(i == 0 ? string.Empty : ", ");
            output.Write($"ptr @{name}_direct{i}");
        }

        output.Write("]\n");
        for (var i = 0; i < entry.DirectCalls.Length; i++)
        {
            WriteSharedDirectAdapter(output, name + "_direct" + i, entry.DirectCalls[i]);
        }

        output.Write($"@{name}_offsets = private constant [{entry.Offsets.Length} x i64] [");
        for (var i = 0; i < entry.Offsets.Length; i++)
        {
            output.Write(i == 0 ? string.Empty : ", ");
            output.Write($"i64 {entry.Offsets[i]}");
        }

        output.Write($"]\n@{name}_policies = private constant [{entry.Policies.Length} x %kimi.shared.policy] [");
        for (var i = 0; i < entry.Policies.Length; i++)
        {
            var policy = entry.Policies[i];
            output.Write(i == 0 ? string.Empty : ", ");
            output.Write($"%kimi.shared.policy {{ i64 {policy.Size}, i64 {(policy.Copy ? 1 : 0)}, ptr @{(policy.Destructor is null ? "__kimi_shared_drop" : name + "_drop" + i)}, ptr null, i64 {policy.Length}, ptr {(policy.Call is null ? "null" : "@" + name + "_call" + i)} }}");
        }

        output.Write("]\n");
        for (var i = 0; i < entry.Policies.Length; i++)
        {
            if (entry.Policies[i].Call is { } adapter)
            {
                WriteSharedCallAdapter(output, name + "_call" + i, adapter);
            }

            if (entry.Policies[i].Destructor is { } destructor)
            {
                output.Write($"define internal void @{name}_drop{i}(ptr %slot, ptr %context, ptr %location, i64 %length) #0 {{\nentry:\n  call void @{destructor}(ptr %slot, ptr %location, i64 %length)\n  ret void\n}}\n");
            }
        }

        output.Write(entry.Abi.GetDefinition(false));
        output.Write("entry:\n");
        if (entry.Selected is { } selected)
        {
            output.Write(selected.Result == "void" ? "  call void" : $"  %selected = call {selected.Result}");
            output.Write($" @{selected.Name}(");
            for (var i = 0; i < entry.Abi.Parameters.Length; i++)
            {
                output.Write(i == 0 ? string.Empty : ", ");
                var parameter = entry.Abi.Parameters[i];
                output.Write($"{parameter.Type} %{parameter.Name}");
            }

            output.Write(")\n");
            output.Write(selected.NoReturn ? "  unreachable\n}\n" : selected.Result == "void" ? "  ret void\n}\n" : $"  ret {selected.Result} %selected\n}}\n");
            return;
        }

        if (entry.ScratchSize != 0)
        {
            output.Write($"  %scratch = alloca [{entry.ScratchSize} x i8], align {entry.ScratchAlignment}\n");
        }

        if (!entry.Abi.ResultSlot && entry.Result is { Layout.Size: > 0 })
        {
            output.Write($"  %ret = alloca {entry.Result.Layout.StorageType}, align {entry.Result.Layout.Alignment}\n");
        }

        for (var i = 0; i < entry.Parameters.Length; i++)
        {
            var value = entry.Parameters[i];
            if (value.ArgumentType == "ptr" && value.ComputationType != "ptr" && value.Layout.Size != 0)
            {
                continue;
            }

            if (value.Layout.Size == 0)
            {
                continue;
            }

            output.Write($"  %arg{i} = alloca {value.Layout.StorageType}, align {value.Layout.Alignment}\n");

            if (value.ComputationType == "i1")
            {
                output.Write($"  %bool{i} = zext i1 %a{i} to i8\n  store i8 %bool{i}, ptr %arg{i}, align 1\n");
            }
            else
            {
                output.Write($"  store {value.ComputationType} %a{i}, ptr %arg{i}, align {value.Layout.Alignment}\n");
            }
        }

        output.Write($"  call void @{entry.Body.Name}(ptr {(entry.Abi.ResultSlot || entry.Result is { Layout.Size: > 0 } ? "%ret" : "null")}");
        for (var i = 0; i < entry.Parameters.Length; i++)
        {
            output.Write(entry.Parameters[i].Layout.Size == 0 ? ", ptr null" : entry.Parameters[i].ArgumentType == "ptr" && entry.Parameters[i].ComputationType != "ptr" ? $", ptr %a{i}" : $", ptr %arg{i}");
        }

        output.Write($", ptr @{name}_offsets, ptr @{name}_policies, ptr {(entry.ScratchSize == 0 ? "null" : "%scratch")}, ptr @{name}_direct)\n");
        if (entry.Abi.NoReturn)
        {
            output.Write("  unreachable\n}\n");
        }
        else if (entry.Abi.Result == "void")
        {
            output.Write("  ret void\n}\n");
        }
        else if (entry.Abi.Result == "i1")
        {
            output.Write("  %byte = load i8, ptr %ret, align 1\n  %value = trunc i8 %byte to i1\n  ret i1 %value\n}\n");
        }
        else
        {
            output.Write($"  %value = load {entry.Abi.Result}, ptr %ret, align {entry.Result!.Layout.Alignment}\n  ret {entry.Abi.Result} %value\n}}\n");
        }
    }

    private static void WriteSharedBody(TextWriter output, LlvmConstantPool constants, SharedStorageBody body)
    {
        output.Write($"define internal void @{body.Name}(ptr %ret");
        for (var p = 0; p < body.ParameterCount; p++)
        {
            output.Write($", ptr %a{p}");
        }

        output.Write($", ptr %offsets, ptr %policies, ptr %scratch, ptr %direct){(body.NoReturn ? " noreturn" : string.Empty)} #0 {{\nentry:\n");
        for (var i = 0; i < body.DirectArguments.Length; i++)
        {
            output.Write($"  %directSlot{i} = getelementptr ptr, ptr %direct, i64 {i}\n  %directCall{i} = load ptr, ptr %directSlot{i}, align 8\n");
        }

        for (var i = 0; i < body.Leaves.Length; i++)
        {
            var leaf = body.Leaves[i];
            var address = leaf.Result ? "%ret" : leaf.Parameter >= 0 ? "%a" + leaf.Parameter : "%scratch";
            if (body.LiveFlags[i])
            {
                output.Write($"  %live{i} = alloca i8, align 1\n  store i8 0, ptr %live{i}, align 1\n");
            }

            output.Write($"  %offptr{i} = getelementptr i64, ptr %offsets, i64 {i}\n  %off{i} = load i64, ptr %offptr{i}, align 8\n");
            output.Write($"  %p{i} = getelementptr i8, ptr {address}, i64 %off{i}\n");
        }

        for (var i = 0; i < body.Addresses.Length; i++)
        {
            var place = body.Addresses[i];
            var address = place.Result ? "%ret" : place.Parameter >= 0 ? "%a" + place.Parameter : "%scratch";
            output.Write($"  %placeoffptr{i} = getelementptr i64, ptr %offsets, i64 {body.AddressOffset + i}\n  %placeoff{i} = load i64, ptr %placeoffptr{i}, align 8\n  %place{i} = getelementptr i8, ptr {address}, i64 %placeoff{i}\n");
        }

        for (var i = 0; i < body.PolicyCount; i++)
        {
            output.Write($"  %meta{i} = getelementptr %kimi.shared.policy, ptr %policies, i64 {i}\n  %size{i} = load i64, ptr %meta{i}, align 8\n");
            output.Write($"  %copyptr{i} = getelementptr %kimi.shared.policy, ptr %meta{i}, i32 0, i32 1\n  %copyword{i} = load i64, ptr %copyptr{i}, align 8\n  %copy{i} = trunc i64 %copyword{i} to i8\n");
            output.Write($"  %dropptr{i} = getelementptr %kimi.shared.policy, ptr %meta{i}, i32 0, i32 2\n  %drop{i} = load ptr, ptr %dropptr{i}, align 8\n");
            output.Write($"  %ctxptr{i} = getelementptr %kimi.shared.policy, ptr %meta{i}, i32 0, i32 3\n  %ctx{i} = load ptr, ptr %ctxptr{i}, align 8\n");
            output.Write($"  %lengthptr{i} = getelementptr %kimi.shared.policy, ptr %meta{i}, i32 0, i32 4\n  %length{i} = load i64, ptr %lengthptr{i}, align 8\n");
            output.Write($"  %callptr{i} = getelementptr %kimi.shared.policy, ptr %meta{i}, i32 0, i32 5\n  %call{i} = load ptr, ptr %callptr{i}, align 8\n");
        }

        output.Write("  br label %b0\n");
        for (var id = 0; id < body.Instructions.Length; id++)
        {
            var op = body.Instructions[id];
            if (!op.Reachable)
            {
                continue;
            }

            output.Write($"b{id}:\n");
            if (op.Kind is SharedStorageOperation.Destroy or SharedStorageOperation.Transfer)
            {
                for (var k = op.Count - 1; k >= 0; k--)
                {
                    var action = body.Destructions[op.DestructionStart + k];
                    if (action == CleanupAction.Skip)
                    {
                        continue;
                    }

                    var dest = op.Destination + k;
                    var policy = body.Leaves[dest].Policy;
                    var location = constants[op.Location];
                    if (action == CleanupAction.Conditional)
                    {
                        output.Write($"  %alivebyte{id}_{k} = load i8, ptr %live{dest}, align 1\n  %alive{id}_{k} = icmp ne i8 %alivebyte{id}_{k}, 0\n  br i1 %alive{id}_{k}, label %drop{id}_{k}, label %after{id}_{k}\ndrop{id}_{k}:\n");
                    }

                    output.Write($"  call void %drop{policy}(ptr %p{dest}, ptr %ctx{policy}, ptr @{location.Name}, i64 {location.ByteLength})\n");
                    if (action == CleanupAction.Conditional)
                    {
                        output.Write($"  br label %after{id}_{k}\nafter{id}_{k}:\n");
                    }
                }
            }

            for (var k = 0; k < op.Count; k++)
            {
                var dest = op.Destination + k;
                var source = op.Source + k;

                switch (op.Kind)
                {
                    case SharedStorageOperation.StringLiteral:
                        var text = source < 0 ? null : constants[source];
                        output.Write($"  store %kimi.string {{ ptr {(text is null ? "null" : "@" + text.Name)}, i64 {text?.ByteLength ?? 0}, i8 {WindowsLowering.StaticReleaseKind} }}, ptr %p{dest}, align {WindowsLowering.String.Layout.Alignment}\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.WriteLine:
                    case SharedStorageOperation.AbortMessage:
                        var callLocation = constants[op.Location];
                        var runtime = op.Kind == SharedStorageOperation.WriteLine ? WindowsLowering.WriteLine : WindowsLowering.AbortMessage;
                        output.Write($"  call void @{runtime.Name}(ptr %p{source}, ptr @{callLocation.Name}, i64 {callLocation.ByteLength})\n");
                        break;
                    case SharedStorageOperation.StorageAddress:
                        output.Write($"  store ptr %place{source}, ptr %p{dest}, align 8\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.Reborrow:
                        output.Write($"  %reborrow{id} = load ptr, ptr %p{source}, align 8\n  store ptr %reborrow{id}, ptr %p{dest}, align 8\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.CaseTest:
                        output.Write($"  %tag{id} = load i32, ptr %p{dest}, align 4\n  %v{id} = icmp eq i32 %tag{id}, {source}\n");
                        break;
                    case SharedStorageOperation.DecomposeEnum:
                        var split = body.Constructions[source];
                        for (var field = 0; field < split.Sources.Length; field++)
                        {
                            var payload = split.Sources[field];
                            output.Write($"  %splitoffptr{id}_{field} = getelementptr i64, ptr %offsets, i64 {split.Offsets[field]}\n  %splitoff{id}_{field} = load i64, ptr %splitoffptr{id}_{field}, align 8\n  %split{id}_{field} = getelementptr i8, ptr %p{dest}, i64 %splitoff{id}_{field}\n");
                            output.Write($"  call void @llvm.memcpy.p0.p0.i64(ptr %p{payload}, ptr %split{id}_{field}, i64 %size{body.Leaves[payload].Policy}, i1 false)\n");
                            Live(payload, true);
                        }

                        Live(dest, false);
                        break;
                    case SharedStorageOperation.DirectCall:
                        output.Write($"  call void %directCall{op.Source}(ptr {(dest < 0 ? "null" : "%place" + op.Index)}");
                        foreach (var argument in body.DirectArguments[op.Source])
                        {
                            output.Write($", ptr %place{argument}");
                        }

                        output.Write(")\n");
                        if (dest >= 0)
                        {
                            for (var field = 0; field < op.FieldOffset; field++)
                            {
                                Live(dest + field, true);
                            }
                        }

                        break;
                    case SharedStorageOperation.Call:
                        var call = body.Calls[op.Source];
                        output.Write($"  call void %call{op.CopyPlace}(ptr {(dest < 0 ? "null" : "%p" + dest)}, ptr %p{call.Receiver}");
                        foreach (var argument in call.Arguments)
                        {
                            output.Write($", ptr %p{argument}");
                        }

                        output.Write(")\n");
                        if (dest >= 0)
                        {
                            Live(dest, true);
                        }

                        break;
                    case SharedStorageOperation.Initialize:
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.Declare:
                    case SharedStorageOperation.Deliver:
                    case SharedStorageOperation.Destroy:
                        Live(dest, false);
                        break;
                    case SharedStorageOperation.Transfer:
                    case SharedStorageOperation.Acquire:
                        output.Write($"  call void @llvm.memcpy.p0.p0.i64(ptr %p{dest}, ptr %p{source}, i64 %size{body.Leaves[dest].Policy}, i1 false)\n");
                        Live(dest, true);
                        if (op.Kind == SharedStorageOperation.Acquire && body.LiveFlags[source])
                        {
                            output.Write($"  store i8 %copy{op.CopyPlace}, ptr %live{source}, align 1\n");
                        }
                        else
                        {
                            Live(source, false);
                        }

                        break;
                    case SharedStorageOperation.Boolean:
                        output.Write($"  store i8 {op.Source}, ptr %p{dest}, align 1\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.Length:
                        output.Write($"  store i64 %length{op.CopyPlace}, ptr %p{dest}, align 8\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.Indices:
                        output.Write($"  store i64 0, ptr %p{dest}, align 8\n  %rangeEnd{id} = getelementptr i8, ptr %p{dest}, i64 8\n  store i64 %length{op.CopyPlace}, ptr %rangeEnd{id}, align 8\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.RangePart:
                        output.Write($"  %rangePart{id} = getelementptr i8, ptr %p{source}, i64 {op.FieldOffset}\n  %endpoint{id} = load i64, ptr %rangePart{id}, align 8\n  store i64 %endpoint{id}, ptr %p{dest}, align 8\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.FieldAddress:
                        output.Write($"  %fieldOffsetPtr{id} = getelementptr i64, ptr %offsets, i64 {op.FieldOffset}\n  %fieldOffset{id} = load i64, ptr %fieldOffsetPtr{id}, align 8\n");
                        output.Write($"  %receiver{id} = load ptr, ptr %p{source}, align 8\n  %field{id} = getelementptr i8, ptr %receiver{id}, i64 %fieldOffset{id}\n  store ptr %field{id}, ptr %p{dest}, align 8\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.FieldRead:
                    case SharedStorageOperation.FieldWrite:
                        var writeField = op.Kind == SharedStorageOperation.FieldWrite;
                        output.Write($"  %fieldOffsetPtr{id} = getelementptr i64, ptr %offsets, i64 {op.FieldOffset}\n  %fieldOffset{id} = load i64, ptr %fieldOffsetPtr{id}, align 8\n");
                        output.Write($"  %receiver{id} = load ptr, ptr %p{(writeField ? dest : source)}, align 8\n  %field{id} = getelementptr i8, ptr %receiver{id}, i64 %fieldOffset{id}\n");
                        output.Write(writeField
                            ? $"  call void @llvm.memcpy.p0.p0.i64(ptr %field{id}, ptr %p{source}, i64 %size{body.Leaves[source].Policy}, i1 false)\n"
                            : $"  call void @llvm.memcpy.p0.p0.i64(ptr %p{dest}, ptr %field{id}, i64 %size{body.Leaves[dest].Policy}, i1 false)\n");
                        if (!writeField)
                        {
                            Live(dest, true);
                        }

                        break;
                    case SharedStorageOperation.SliceAddress:
                    case SharedStorageOperation.ArrayRead:
                    case SharedStorageOperation.ArrayAddress:
                        var elementPolicy = body.Leaves[dest].Policy;
                        var readLocation = constants[op.Location];
                        if (op.Kind == SharedStorageOperation.SliceAddress)
                        {
                            output.Write($"  %sliceLengthPtr{id} = getelementptr i8, ptr %p{source}, i64 8\n  %sliceLength{id} = load i64, ptr %sliceLengthPtr{id}, align 8\n");
                        }

                        output.Write($"  %index{id} = or i64 0, %v{op.Index}\n  %outOfBounds{id} = icmp uge i64 %index{id}, %{(op.Kind == SharedStorageOperation.SliceAddress ? "sliceLength" + id : "length" + op.CopyPlace)}\n");
                        output.Write($"  br i1 %outOfBounds{id}, label %boundsAbort{id}, label %elementRead{id}\nboundsAbort{id}:\n");
                        output.Write($"  call void @{WindowsLowering.Abort.Name}(i32 {WindowsLowering.IndexBoundsReason}, ptr @{readLocation.Name}, i64 {readLocation.ByteLength}, i64 -2)\n  unreachable\nelementRead{id}:\n");
                        if (op.Kind is SharedStorageOperation.ArrayAddress or SharedStorageOperation.SliceAddress)
                        {
                            output.Write($"  %strideSlot{id} = getelementptr i64, ptr %offsets, i64 {op.FieldOffset}\n  %stride{id} = load i64, ptr %strideSlot{id}, align 8\n");
                        }

                        output.Write($"  %array{id} = load ptr, ptr %p{source}, align 8\n  %byteOffset{id} = mul i64 %index{id}, %{(op.Kind != SharedStorageOperation.ArrayRead ? "stride" + id : "size" + elementPolicy)}\n  %element{id} = getelementptr i8, ptr %array{id}, i64 %byteOffset{id}\n");
                        output.Write(op.Kind != SharedStorageOperation.ArrayRead ? $"  store ptr %element{id}, ptr %p{dest}, align 8\n" : $"  call void @llvm.memcpy.p0.p0.i64(ptr %p{dest}, ptr %element{id}, i64 %size{elementPolicy}, i1 false)\n");
                        Live(dest, true);
                        break;
                    case SharedStorageOperation.ConstructEnum:
                        var construction = body.Constructions[op.Source];
                        for (var p = 0; p < construction.Sources.Length; p++)
                        {
                            var payload = construction.Sources[p];
                            output.Write($"  %payloadOffsetPtr{id}_{p} = getelementptr i64, ptr %offsets, i64 {construction.Offsets[p]}\n  %payloadOffset{id}_{p} = load i64, ptr %payloadOffsetPtr{id}_{p}, align 8\n");
                            output.Write($"  %payload{id}_{p} = getelementptr i8, ptr %p{dest}, i64 %payloadOffset{id}_{p}\n  call void @llvm.memcpy.p0.p0.i64(ptr %payload{id}_{p}, ptr %p{payload}, i64 %size{body.Leaves[payload].Policy}, i1 false)\n");
                            Live(payload, false);
                        }

                        output.Write($"  store i32 {construction.Tag}, ptr %p{dest}, align 4\n");
                        Live(dest, true);
                        break;
                }
            }

            if (op.Scalar is { } scalar)
            {
                if (scalar.Kind == OwnershipValueKind.Phi)
                {
                    output.Write($"  %v{id} = phi {scalar.Type} ");
                    for (var n = 0; n < scalar.Second; n++)
                    {
                        var input = body.PhiInputs[scalar.First + n];
                        if (n != 0)
                        {
                            output.Write(", ");
                        }

                        output.Write($"[ %v{input.Value}, %phiFrom{input.Predecessor} ]");
                    }

                    output.Write('\n');
                }
                else if (scalar.Kind == OwnershipValueKind.Constant)
                {
                    output.Write($"  %v{id} = or {scalar.Type} 0, {scalar.Constant}\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Alias)
                {
                    output.Write($"  %v{id} = or {scalar.Type} 0, %v{scalar.First}\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Convert)
                {
                    var conversion = body.Conversions[scalar.Second];
                    var plan = conversion.Plan;
                    var instruction = new EmissionInstruction(EmissionOpcode.Convert, id, Place: body.Instructions.Length + id, Constant: op.Location, ScalarType: scalar.Type, ScalarOperator: plan.Operator, Check: plan.Checked ? ArithmeticCheckKind.Conversion : ArithmeticCheckKind.None, Representation: conversion.Source, LowerPredicate: plan.LowerPredicate, UpperPredicate: plan.UpperPredicate);
                    WriteConversion(output, constants, instruction, [new(EmissionOperandKind.Value, scalar.First), new(EmissionOperandKind.Integer, plan.Lower), new(EmissionOperandKind.Integer, plan.Upper)]);
                    if (plan.Operator is null)
                    {
                        output.Write($"  %v{id} = or {scalar.Type} 0, %v{scalar.First}\n");
                    }
                }
                else if (scalar.Kind == OwnershipValueKind.Unary && scalar.Operator == "not")
                {
                    output.Write($"  %v{id} = xor i1 %v{scalar.First}, true\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Unary && scalar.Operator == "pos")
                {
                    output.Write($"  %v{id} = or {scalar.Type} 0, %v{scalar.First}\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Unary)
                {
                    // Checked negation: only the minimum value overflows.
                    var location = constants[op.Location];
                    output.Write($"  %sum{id} = call {{ {scalar.Type}, i1 }} @llvm.ssub.with.overflow.{scalar.Type}({scalar.Type} 0, {scalar.Type} %v{scalar.First})\n  %v{id} = extractvalue {{ {scalar.Type}, i1 }} %sum{id}, 0\n  %overflow{id} = extractvalue {{ {scalar.Type}, i1 }} %sum{id}, 1\n");
                    output.Write($"  br i1 %overflow{id}, label %overflowAbort{id}, label %sumReady{id}\noverflowAbort{id}:\n  call void @{WindowsLowering.Abort.Name}(i32 {WindowsLowering.IntegerOverflowReason}, ptr @{location.Name}, i64 {location.ByteLength}, i64 -2)\n  unreachable\nsumReady{id}:\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Binary && scalar.Operator is "sdiv" or "srem" or "udiv" or "urem")
                {
                    // SPEC 13.4: zero divisor and signed minimum by -1 (including %) abort.
                    var location = constants[op.Location];
                    var width = int.Parse(scalar.Type.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
                    var minimum = width == 64 ? long.MinValue : -(1L << (width - 1));
                    output.Write($"  %zero{id} = icmp eq {scalar.Type} %v{scalar.Second}, 0\n  %min{id} = icmp eq {scalar.Type} %v{scalar.First}, {minimum}\n  %minus{id} = icmp eq {scalar.Type} %v{scalar.Second}, -1\n");
                    output.Write(scalar.Operator[0] == 's' ? $"  %wrap{id} = and i1 %min{id}, %minus{id}\n" : $"  %wrap{id} = or i1 false, false\n");
                    output.Write($"  %bad{id} = or i1 %zero{id}, %wrap{id}\n  br i1 %bad{id}, label %divAbort{id}, label %divReady{id}\n");
                    output.Write($"divAbort{id}:\n  %reason{id} = select i1 %zero{id}, i32 {WindowsLowering.IntegerDivisionZeroReason}, i32 {WindowsLowering.IntegerOverflowReason}\n");
                    output.Write($"  call void @{WindowsLowering.Abort.Name}(i32 %reason{id}, ptr @{location.Name}, i64 {location.ByteLength}, i64 -2)\n  unreachable\ndivReady{id}:\n");
                    output.Write($"  %v{id} = {scalar.Operator} {scalar.Type} %v{scalar.First}, %v{scalar.Second}\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Binary && scalar.Operator is "sadd" or "ssub" or "smul" or "uadd" or "usub" or "umul")
                {
                    var location = constants[op.Location];
                    output.Write($"  %sum{id} = call {{ {scalar.Type}, i1 }} @llvm.{scalar.Operator}.with.overflow.{scalar.Type}({scalar.Type} %v{scalar.First}, {scalar.Type} %v{scalar.Second})\n  %v{id} = extractvalue {{ {scalar.Type}, i1 }} %sum{id}, 0\n  %overflow{id} = extractvalue {{ {scalar.Type}, i1 }} %sum{id}, 1\n");
                    output.Write($"  br i1 %overflow{id}, label %overflowAbort{id}, label %sumReady{id}\noverflowAbort{id}:\n  call void @{WindowsLowering.Abort.Name}(i32 {WindowsLowering.IntegerOverflowReason}, ptr @{location.Name}, i64 {location.ByteLength}, i64 -2)\n  unreachable\nsumReady{id}:\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Binary && scalar.CountWidth != 0)
                {
                    var location = constants[op.Location];
                    var width = int.Parse(scalar.Type.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
                    // Unsigned comparison also rejects every negative signed count.
                    // Check in the original count Type before widening or truncating it.
                    output.Write($"  %badShift{id} = icmp uge i{scalar.CountWidth} %v{scalar.Second}, {width}\n  br i1 %badShift{id}, label %shiftAbort{id}, label %shiftReady{id}\nshiftAbort{id}:\n");
                    output.Write($"  call void @{WindowsLowering.Abort.Name}(i32 {WindowsLowering.IntegerShiftCountReason}, ptr @{location.Name}, i64 {location.ByteLength}, i64 -2)\n  unreachable\nshiftReady{id}:\n");
                    if (scalar.CountWidth != width)
                    {
                        output.Write($"  %shiftCount{id} = {(scalar.CountWidth < width ? "zext" : "trunc")} i{scalar.CountWidth} %v{scalar.Second} to {scalar.Type}\n");
                    }

                    output.Write($"  %v{id} = {scalar.Operator} {scalar.Type} %v{scalar.First}, %{(scalar.CountWidth == width ? "v" + scalar.Second : "shiftCount" + id)}\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Binary && scalar.Operator is "and" or "or" or "xor")
                {
                    output.Write($"  %v{id} = {scalar.Operator} {scalar.Type} %v{scalar.First}, %v{scalar.Second}\n");
                }
                else if (scalar.Kind == OwnershipValueKind.Binary)
                {
                    var separator = scalar.Operator!.IndexOf(':');
                    output.Write($"  %v{id} = icmp {scalar.Operator[..separator]} {scalar.Operator[(separator + 1)..]} %v{scalar.First}, %v{scalar.Second}\n");
                }
                else if (scalar.Type == "i1")
                {
                    output.Write($"  %readByte{id} = load i8, ptr %p{op.Destination}, align 1\n  %v{id} = trunc i8 %readByte{id} to i1\n");
                }
                else
                {
                    output.Write($"  %v{id} = load {scalar.Type}, ptr %p{op.Destination}, align {SharedScalarAlignment(scalar.Type)}\n");
                }

                if (scalar.Store)
                {
                    if (scalar.Type == "i1")
                    {
                        output.Write($"  %storeByte{id} = zext i1 %v{id} to i8\n  store i8 %storeByte{id}, ptr %p{op.Destination}, align 1\n");
                    }
                    else
                    {
                        output.Write($"  store {scalar.Type} %v{id}, ptr %p{op.Destination}, align {SharedScalarAlignment(scalar.Type)}\n");
                    }
                }
            }

            if (op.Alternative >= 0)
            {
                output.Write($"  br i1 %v{op.Condition}, label %b{op.Next}, label %b{op.Alternative}\n");
            }
            else if (op.Kind == SharedStorageOperation.Return)
            {
                output.Write("  ret void\n");
            }
            else if (op.Next >= 0)
            {
                // Checks and conditional cleanup may split an operation's block.
                // A dedicated arrival label keeps Phi predecessors independent of those splits.
                if (body.Instructions[op.Next].Scalar is { Kind: OwnershipValueKind.Phi })
                {
                    output.Write($"  br label %phiFrom{id}\nphiFrom{id}:\n");
                }

                output.Write($"  br label %b{op.Next}\n");
            }
            else
            {
                output.Write("  unreachable\n");
            }
        }

        output.Write("}\n");

        void Live(int leaf, bool initialized)
        {
            if (body.LiveFlags[leaf])
            {
                output.Write($"  store i8 {(initialized ? "1" : "0")}, ptr %live{leaf}, align 1\n");
            }
        }
    }
}
