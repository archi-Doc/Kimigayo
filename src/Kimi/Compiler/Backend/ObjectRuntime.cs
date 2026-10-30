// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the object runtime text of the generated LLVM module, permanently (IMPL 21.1.3, SHARED_OBJECT_RUNTIME; PLAN10 §5.8; plan
// P3b; DECISIONS R5-M69): the payload-independent runtime every module that uses objects holds once (the dynamic Type test through a
// descriptor's base map, the descriptor's free entry, finalization, which destroys the payload through its metadata before the free,
// and the rc and arc clone and drop with their release transitions, ObjectRuntime.Handles.cs), each object's private constants
// (payload metadata, descriptor, virtual table, base map), its payload destructor and its factory, the factory's signature (Factory),
// whose parameters its text names, and whether it copies its payload by memcpy (Copies), a Sealed handle's direct release
// (ObjectRuntime.Handles.cs), and the facts a caller of the runtime reads: the signatures of its Type test (Supports), its retains
// (Clone) and its releases (Release), a factory's control value (InitialControl), the payload's offset in an object (PayloadOffset) and the
// virtual table's in a descriptor (TableOffset). Both generators write objects through it (PD2: promoted, never copied). Runtime
// identity is the module's Type token (PD17); no Runtime Type key text is written (DECISIONS R5-M110).
// Input: a writer; per object its ObjectCreation row; for a factory's signature the object's number, its payload's representation and
// the payload parameter's kind; for a Sealed handle's release its ObjectCountingStep (BoundType.HandleMode's) and the symbol of the
// payload's drop routine; for a retain or a control value the handle's ObjectCountingStep.
// Output: LLVM text written to the writer: the runtime, then per object its constants, its destructor and its factory; the signatures and
// values above.
// Invariants: a release publishes the reduced count before any destruction, destroys the payload before the free and accesses the
// header after neither (SHARED_OBJECT_RUNTIME, Release); the legacy writer's .ll stays byte-identical while it shares this text;
// warm writes allocate nothing.
// Must not decide whether a module uses objects or build an object's row (code generation's), take syntax, HIR or MIR types, or use
// LINQ.

#pragma warning disable SA1402 // The object row is this writer's input.

using System.Globalization;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>
/// The syntax-free physical row of one object payload: its number, the payload representation, Copy, the factory's ABI and the
/// Runtime Type token; the payload destructor, the base tokens and the virtual table are the descriptor's optional entries, absent when
/// null or empty.
/// </summary>
internal sealed record ObjectCreation(int Id, ValueLowering Payload, bool Copy, FunctionAbi Abi, int TypeToken)
{
    internal string? Destroy { get; init; }

    internal int[] BaseTokens { get; init; } = [];

    internal FunctionAbi?[] VirtualSlots { get; init; } = [];
}

/// <summary>Writes the object runtime of the LLVM text that every generator shares.</summary>
internal static partial class ObjectRuntime
{
    /// <summary>The offset of an object's payload from its header, after its descriptor and control words (IMPL 21.1.3), at which the
    /// runtime's text reads and writes it.</summary>
    internal const int PayloadOffset = 16;

    /// <summary>The offset of a descriptor's virtual table, after its metadata, free entry and base map (IMPL 21.1.3), at which the
    /// descriptor's text holds it.</summary>
    internal const int TableOffset = 24;

    // IMPL 21.1.3: the payload-independent object runtime: the dynamic Type test through the descriptor's base map, the free
    // entry of every current descriptor, and finalization, which destroys the payload through its metadata before the free.
    private const string ObjectFunctions = """
        define internal i1 @__kimi_object_supports(ptr %header, i64 %target) #0 {
        entry:
          %descriptor = load ptr, ptr %header, align 8
          %metadata = load ptr, ptr %descriptor, align 8
          %token = load i64, ptr %metadata, align 8
          %same = icmp eq i64 %token, %target
          br i1 %same, label %yes, label %bases
        bases:
          %map_slot = getelementptr i8, ptr %descriptor, i64 16
          %map = load ptr, ptr %map_slot, align 8
          %empty = icmp eq ptr %map, null
          br i1 %empty, label %no, label %prepare
        prepare:
          %count = load i64, ptr %map, align 8
          br label %test
        test:
          %index = phi i64 [ 0, %prepare ], [ %next, %advance ]
          %end = icmp eq i64 %index, %count
          br i1 %end, label %no, label %candidate
        candidate:
          %scaled = mul i64 %index, 16
          %offset = add i64 %scaled, 8
          %map_entry = getelementptr i8, ptr %map, i64 %offset
          %base = load i64, ptr %map_entry, align 8
          %matches = icmp eq i64 %base, %target
          br i1 %matches, label %yes, label %advance
        advance:
          %next = add i64 %index, 1
          br label %test
        yes:
          ret i1 true
        no:
          ret i1 false
        }
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
          call void @__kimi_object_finalize(ptr %header, ptr %site)
          ret void
        }
        define internal void @__kimi_object_finalize(ptr %header, ptr %site) #0 {
        entry:
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

        """;

    /// <summary>Gets the runtime's dynamic Type test (SPEC 13.6.4): whether the object at a header supports a Type token, its
    /// descriptor's own or one of its base map's.</summary>
    internal static FunctionAbi Supports { get; } = new("__kimi_object_supports", "i1", [new("ptr", "header"), new("i64", "target")]);

    // The payload-independent runtime, once per module that uses objects, before its objects.
    internal static void WriteRuntime(TextWriter output)
    {
        output.Write(ObjectFunctions);
        WriteRcObjects(output);
        WriteArcObjects(output);
    }

    // One object: its private constants, its payload destructor and its factory.
    internal static void WriteObject(TextWriter output, ObjectCreation item)
    {
        WriteDescriptor(output, item);
        WriteDestroyValues(output, item);
        WriteCreation(output, item);
    }

    // The signature of an object's factory (SHARED_OBJECT_RUNTIME, Creation), whose parameters WriteCreation's text names: the result
    // slot, the payload at its kind unless it is zero-sized, the static control value and the creation's location; named after the
    // object's number.
    internal static FunctionAbi Factory(int id, ValueLowering payload, AbiParameterKind kind)
    {
        var sized = payload.Layout.Size != 0;
        var parameters = new AbiParameter[sized ? 5 : 4];
        var n = 0;
        parameters[n++] = new("ptr", "ret", AbiParameterKind.ResultSlot);
        if (sized)
        {
            parameters[n++] = new(payload.ArgumentType!, "a0", kind, 0);
        }

        parameters[n++] = new("i64", "controlValue", AbiParameterKind.Context);
        parameters[n++] = new("ptr", "location", AbiParameterKind.Location);
        parameters[n] = new("i64", "length", AbiParameterKind.LocationLength);
        return new("__kimi_make_object" + id.ToString(CultureInfo.InvariantCulture), "void", parameters) { ResultSlot = true };
    }

    // Whether a factory copies its payload by memcpy from the pointer it receives (a value in memory), so the module declares the memory
    // intrinsics.
    internal static bool Copies(ValueLowering payload)
        => payload.Layout.Size != 0 && payload.ArgumentType == "ptr" && payload.ComputationType != "ptr";

    // An object's private constants: the payload metadata (identity, layout, Copy and payload destructor), and the descriptor naming
    // that metadata, the free entry, the base map and the virtual table.
    private static void WriteDescriptor(TextWriter output, ObjectCreation item)
    {
        var id = item.Id;
        var layout = item.Payload.Layout;
        Name(output, "@__kimi_object_metadata", id);
        output.Write(" = private constant { i64, i64, i64, i64, ptr, ptr } { i64 ");
        WriteNumber(output, item.TypeToken);
        output.Write(", i64 ");
        WriteNumber(output, layout.Size);
        output.Write(", i64 ");
        WriteNumber(output, layout.Alignment);
        output.Write(item.Copy ? ", i64 1, ptr " : ", i64 0, ptr ");
        if (item.Destroy is null)
        {
            output.Write("null");
        }
        else
        {
            Name(output, "@__kimi_object_destroy_values", id);
        }

        output.Write(", ptr null }, align 8\n");
        Name(output, "@__kimi_object_descriptor", id);
        output.Write(" = private constant { ptr, ptr, ptr, ptr } { ptr ");
        Name(output, "@__kimi_object_metadata", id);
        output.Write(", ptr @__kimi_object_free, ptr ");
        if (item.BaseTokens.Length == 0)
        {
            output.Write("null");
        }
        else
        {
            Name(output, "@__kimi_object_views", id);
        }

        output.Write(", ptr ");
        if (item.VirtualSlots.Length == 0)
        {
            output.Write("null");
        }
        else
        {
            Name(output, "@__kimi_object_virtuals", id);
        }

        output.Write(" }, align 8\n");
        if (item.VirtualSlots.Length != 0)
        {
            Name(output, "@__kimi_object_virtuals", id);
            output.Write(" = private constant [");
            WriteNumber(output, item.VirtualSlots.Length);
            output.Write(" x ptr] [");
            for (var s = 0; s < item.VirtualSlots.Length; s++)
            {
                output.Write(s == 0 ? "ptr " : ", ptr ");
                if (item.VirtualSlots[s] is { } entry)
                {
                    output.Write('@');
                    output.Write(entry.Name);
                }
                else
                {
                    output.Write("null");
                }
            }

            output.Write("], align 8\n");
        }

        if (item.BaseTokens.Length != 0)
        {
            Name(output, "@__kimi_object_views", id);
            output.Write(" = private constant [");
            WriteNumber(output, (item.BaseTokens.Length * 2) + 1);
            output.Write(" x i64] [i64 ");
            WriteNumber(output, item.BaseTokens.Length);
            foreach (var token in item.BaseTokens)
            {
                output.Write(", i64 ");
                WriteNumber(output, token);
                output.Write(", i64 0");
            }

            output.Write("], align 8\n");
        }
    }

    // The payload destructor the metadata names: destroys count payload values at the payload's stride through its drop.
    private static void WriteDestroyValues(TextWriter output, ObjectCreation item)
    {
        if (item.Destroy is null)
        {
            return;
        }

        Name(output, "define internal void @__kimi_object_destroy_values", item.Id);
        output.Write("""
            (ptr %first, i64 %count, ptr %metadata, ptr %site) #0 {
            entry:
              %location = load ptr, ptr %site, align 8
              %lengthSlot = getelementptr i8, ptr %site, i64 8
              %length = load i64, ptr %lengthSlot, align 8
              br label %test
            test:
              %remaining = phi i64 [ %count, %entry ], [ %index, %destroy ]
              %empty = icmp eq i64 %remaining, 0
              br i1 %empty, label %done, label %destroy
            destroy:
              %index = sub i64 %remaining, 1
              %offset = mul i64 %index,
            """);
        output.Write(' ');
        WriteNumber(output, item.Payload.Layout.Stride);
        output.Write("\n  %value = getelementptr i8, ptr %first, i64 %offset\n  call void @");
        output.Write(item.Destroy);
        output.Write("(ptr %value, ptr %location, i64 %length)\n  br label %test\ndone:\n  ret void\n}\n");
    }

    // The payload-specific factory (SHARED_OBJECT_RUNTIME, Creation): allocates the header and payload, stores the
    // descriptor and the static control value, initializes the payload from its argument and returns the header.
    private static void WriteCreation(TextWriter output, ObjectCreation item)
    {
        var layout = item.Payload.Layout;
        output.Write(item.Abi.GetDefinition(exported: false));
        output.Write("entry:\n  %header = call ptr @__kimi_alloc(i64 ");
        WriteNumber(output, (long)layout.Size + 16);
        output.Write(", ptr %location, i64 %length)\n  store ptr ");
        Name(output, "@__kimi_object_descriptor", item.Id);
        output.Write(", ptr %header, align 8\n  %control = getelementptr i8, ptr %header, i64 8\n  store i64 %controlValue, ptr %control, align 8\n");
        if (layout.Size != 0)
        {
            output.Write("  %payload = getelementptr i8, ptr %header, i64 16\n");
            if (Copies(item.Payload))
            {
                output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr align ");
                WriteNumber(output, layout.Alignment);
                output.Write(" %payload, ptr align ");
                WriteNumber(output, layout.Alignment);
                output.Write(" %a0, i64 ");
                WriteNumber(output, layout.Size);
                output.Write(", i1 false)\n");
            }
            else if (item.Payload.ComputationType == "i1")
            {
                output.Write("  %byte = zext i1 %a0 to i8\n  store i8 %byte, ptr %payload, align 1\n");
            }
            else
            {
                output.Write("  store ");
                output.Write(layout.StorageType);
                output.Write(" %a0, ptr %payload, align ");
                WriteNumber(output, layout.Alignment);
                output.Write('\n');
            }
        }

        output.Write("  store ptr %header, ptr %ret, align 8\n  ret void\n}\n");
    }
}
