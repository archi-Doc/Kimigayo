// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteObjects(EmissionModule module, TextWriter output)
    {
        var usesObjects = module.Objects.Count != 0;
        foreach (var layout in module.Aggregates)
        {
            usesObjects |= layout.ObjectHandle;
        }

        if (!usesObjects)
        {
            return;
        }

        output.Write("""
            define internal void @__kimi_object_free(ptr %header, ptr %descriptor, ptr %site) #0 {
            entry:
              %location = load ptr, ptr %site, align 8
              %lengthSlot = getelementptr i8, ptr %site, i64 8
              %length = load i64, ptr %lengthSlot, align 8
              call void @__kimi_free(ptr %header, ptr %location, i64 %length)
              ret void
            }
            define internal void @__kimi_drop_object(ptr %slot, ptr %location, i64 %length) #0 {
            entry:
              %site = alloca { ptr, i64 }, align 8
              store ptr %location, ptr %site, align 8
              %lengthSlot = getelementptr i8, ptr %site, i64 8
              store i64 %length, ptr %lengthSlot, align 8
              %header = load ptr, ptr %slot, align 8
              %descriptor = load ptr, ptr %header, align 8
              %metadata = load ptr, ptr %descriptor, align 8
              %freeSlot = getelementptr i8, ptr %descriptor, i64 8
              %free = load ptr, ptr %freeSlot, align 8
              %destroySlot = getelementptr i8, ptr %metadata, i64 32
              %destroy = load ptr, ptr %destroySlot, align 8
              %needed = icmp ne ptr %destroy, null
              br i1 %needed, label %destroyPayload, label %freeStorage
            destroyPayload:
              %payload = getelementptr i8, ptr %header, i64 16
              call void %destroy(ptr %payload, i64 1, ptr %metadata, ptr %site)
              br label %freeStorage
            freeStorage:
              call void %free(ptr %header, ptr %descriptor, ptr %site)
              ret void
            }

            """);
        foreach (var item in module.Objects)
        {
            var id = item.Id;
            var layout = item.Payload.Layout;
            var destroy = item.Destroy is null ? "null" : "@__kimi_object_destroy_values" + id;
            var key = module.Constants[item.TypeKey];
            output.Write($"@__kimi_object_type_key{id} = private constant {{ i64, ptr, i64 }} {{ i64 {id + 1}, ptr @{key.Name}, i64 {key.ByteLength} }}, align 8\n");
            output.Write($"@__kimi_object_metadata{id} = private constant {{ i64, i64, i64, i64, ptr, ptr }} {{ i64 {id + 1}, i64 {layout.Size}, i64 {layout.Alignment}, i64 {(item.Copy ? 1 : 0)}, ptr {destroy}, ptr null }}, align 8\n");
            output.Write($"@__kimi_object_descriptor{id} = private constant {{ ptr, ptr, ptr }} {{ ptr @__kimi_object_metadata{id}, ptr @__kimi_object_free, ptr null }}, align 8\n");
            if (item.Destroy is not null)
            {
                output.Write($"define internal void @__kimi_object_destroy_values{id}(ptr %first, i64 %count, ptr %metadata, ptr %site) #0 {{\nentry:\n");
                output.Write("  %location = load ptr, ptr %site, align 8\n  %lengthSlot = getelementptr i8, ptr %site, i64 8\n  %length = load i64, ptr %lengthSlot, align 8\n  br label %test\ntest:\n  %remaining = phi i64 [ %count, %entry ], [ %index, %destroy ]\n  %empty = icmp eq i64 %remaining, 0\n  br i1 %empty, label %done, label %destroy\ndestroy:\n  %index = sub i64 %remaining, 1\n");
                output.Write($"  %offset = mul i64 %index, {layout.Stride}\n  %value = getelementptr i8, ptr %first, i64 %offset\n  call void @{item.Destroy}(ptr %value, ptr %location, i64 %length)\n  br label %test\ndone:\n  ret void\n}}\n");
            }

            output.Write(item.Abi.GetDefinition(false));
            output.Write($"entry:\n  %header = call ptr @__kimi_alloc(i64 {(long)layout.Size + 16}, ptr %location, i64 %length)\n  store ptr @__kimi_object_descriptor{id}, ptr %header, align 8\n  %control = getelementptr i8, ptr %header, i64 8\n  store i64 0, ptr %control, align 8\n");
            if (layout.Size != 0)
            {
                output.Write("  %payload = getelementptr i8, ptr %header, i64 16\n");
                if (item.Payload.ArgumentType == "ptr" && item.Payload.ComputationType != "ptr")
                {
                    output.Write($"  call void @llvm.memcpy.p0.p0.i64(ptr align {layout.Alignment} %payload, ptr align {layout.Alignment} %a0, i64 {layout.Size}, i1 false)\n");
                }
                else if (item.Payload.ComputationType == "i1")
                {
                    output.Write("  %byte = zext i1 %a0 to i8\n  store i8 %byte, ptr %payload, align 1\n");
                }
                else
                {
                    output.Write($"  store {layout.StorageType} %a0, ptr %payload, align {layout.Alignment}\n");
                }
            }

            output.Write("  store ptr %header, ptr %ret, align 8\n  ret void\n}\n");
        }
    }
}
