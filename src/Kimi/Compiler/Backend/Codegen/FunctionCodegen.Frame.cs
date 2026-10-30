// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The function frame of FunctionCodegen (R5 package C 3.2 I2; DECISIONS R5-M65, R5-M89, R5-M102, R5-M131): each local's home and layout
// under the signature ModuleCodegen gives the item, the entry block (the prologue: the homes' and the copy slots' allocas, a Region's bytes,
// the Value arguments' stores) and the return. The argument order and the copy slots are FunctionCodegen.Arguments', a Region home
// FunctionCodegen.Interpolation's.
internal sealed partial class FunctionCodegen
{
    // Where a local lives (I2).
    private enum Home : byte
    {
        None, // Zero-sized: no storage.
        Slot, // Its alloca, %l<local>.
        Parameter, // The pointer its caller passed, a pointer-passed argument or the result slot: its parameter of the signature.
        Region, // Its alloca, as Slot's, holding a buffer whose bytes are the frame's region %r<local> of Index bytes (FunctionCodegen.Interpolation).
    }

    // Lays out every local in the item's instance (I7), then gets the item's signature, which ModuleCodegen gives from the item's own body
    // (DECISIONS R5-M89); null after a record.
    private FunctionAbi? LayOut()
    {
        var locals = this.body.Locals;
        if (this.slots.Length < locals.Length)
        {
            this.slots = new Slot[Math.Max(locals.Length, 2 * this.slots.Length)];
            this.types = new BoundType[this.slots.Length];
        }

        for (var i = 0; i < locals.Length; i++)
        {
            if (this.collector.TypeOf(this.item, locals[i].Type) is not { } type)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, locals[i].Source);
                return null;
            }

            if (this.module.Layout.Of(type).Value is not { } value)
            {
                // The collector laid out every local of an admitted item but a constructor's construction receiver, which its callers lay out
                // (DECISIONS BU-48), and a constructor whose receiver has no layout is not Generated (InstanceCollector.Generates; DECISIONS
                // R5-M149), so this is a broken contract.
                this.Report(DiagnosticCode.InternalInvariant_Kd, locals[i].Source);
                return null;
            }

            (this.types[i], this.slots[i]) = (type, new(value.Layout.Size == 0 ? Home.None : Home.Slot, i, value));
        }

        return this.module.Signature(this.item);
    }

    // The entry block (I2): a pointer-passed argument and the result slot are their locals' homes; one aligned alloca per other local of
    // nonzero size, its bytes' for a Region, per copy slot and per drop flag, cleared (FunctionCodegen.Flags); each Value argument stored into
    // its local's slot; the entry's flag points (an argument's flag set). A parameter is its signature's (Prefix p, spelled by its name); a
    // Closure body's environment passes its local as its Type would (Passing).
    private void Prologue()
    {
        var (output, parameters) = (this.output, this.abi.Parameters);
        output.Write(this.abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        for (var i = 0; i < parameters.Length; i++)
        {
            if (this.Passing(parameters[i]) is AbiParameterKind.OwnedSlot or AbiParameterKind.ResultSlot)
            {
                ref var slot = ref this.slots[parameters[i].Kind == AbiParameterKind.ResultSlot ? 0 : this.LocalOf(parameters[i])];
                slot = slot with { Home = Home.Parameter, Index = i };
            }
        }

        for (var i = 0; i < this.body.Locals.Length; i++)
        {
            if (this.slots[i] is { Home: Home.Slot or Home.Region, Value.Layout: var layout })
            {
                this.WriteSlot("  %l", i, layout);
            }

            if (this.slots[i] is { Home: Home.Region, Index: var size })
            {
                output.Write("  %r");
                WriteNumber(output, i);
                output.Write(" = alloca [");
                WriteNumber(output, size);
                output.Write(" x i8], align 1\n");
            }
        }

        for (var i = 0; i < this.copyCount; i++)
        {
            this.WriteSlot("  %s", i, this.copies[i]);
        }

        this.FlagSlots();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (this.Passing(parameters[i]) is AbiParameterKind.Value or AbiParameterKind.SharedReference)
            {
                var local = this.LocalOf(parameters[i]);
                this.StoreAt(new('h', local), this.slots[local].Value.Layout, this.Widen(this.slots[local].Value, new('p', i)));
            }
        }

        this.Points(new(0, -1));
        output.Write("  br label %b0\n");
    }

    // Whether a Place's value owns storage that its destruction releases (SPEC 16.2.1): every one but a Region local's, whose bytes are the
    // frame's and have no release (the windows-x64 stack rule; DECISIONS R5-M131).
    private bool OwnsStorage(MirPlace place) => place is not { Root: >= 0, Projection: 0 } || this.slots[place.Root].Home != Home.Region;

    // How a parameter passes its local: by its kind; a Closure body's environment pointer (Abi's [environment], SPEC 7.7.3; DECISIONS
    // R5-M102) as Abi.Of passes an argument of its local's Type: an owned environment, a slot Type, by pointer, used in place; a borrowed one
    // by value, stored; a zero-sized one has no local storage (Environment: neither).
    private AbiParameterKind Passing(in AbiParameter parameter)
    {
        if (parameter.Kind != AbiParameterKind.Environment || this.slots[1].Home == Home.None)
        {
            return parameter.Kind;
        }

        return Abi.Kind(Abi.Convention.Internal, this.types[1]);
    }

    // An aligned alloca of the entry block (I2).
    private void WriteSlot(string prefix, int number, TypeLayout layout)
    {
        var output = this.output;
        output.Write(prefix);
        WriteNumber(output, number);
        output.Write(" = alloca ");
        output.Write(layout.StorageType);
        output.Write(", align ");
        WriteNumber(output, layout.Alignment);
        output.Write('\n');
    }

    // Return (IMPL 21.3.1): void for a zero-sized result or a result slot, else local 0's value in its computation representation; the
    // block's window closed before it (FunctionCodegen.Phases).
    private void Return()
    {
        this.Close();
        if (this.abi.ResultSlot || this.slots[0].Home == Home.None)
        {
            this.output.Write("  ret void\n");
            return;
        }

        var lowering = this.slots[0].Value;
        var value = this.Narrow(lowering, this.Load(0));
        this.output.Write("  ret ");
        this.output.Write(lowering.ComputationType);
        this.output.Write(' ');
        this.WriteValue(value);
        this.output.Write('\n');
    }

    // A local's home and representation: Index is a Parameter home's parameter, a Region home's size in bytes.
    private readonly record struct Slot(Home Home, int Index, ValueLowering Value);
}
