// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The function frame of FunctionCodegen (R5 package C 3.2 I2; DECISIONS R5-M65): the item's signature, each local's home and layout, the
// entry block (the prologue) and the return.
internal sealed partial class FunctionCodegen
{
    // Where a local lives (I2).
    private enum Home : byte
    {
        None, // Zero-sized: no storage.
        Slot, // Its alloca, %l<local>.
        Argument, // The pointer its caller passed, %a<slot>.
        Result, // The result slot, %ret.
    }

    // Lays out every local in the item's instance (I7), then gets the item's signature from local 0 and the arguments in slot order (SPEC
    // 7.3: the Arg locals hold them receiver first); null after a record.
    private FunctionAbi? LayOut()
    {
        var locals = this.body.Locals;
        if (this.slots.Length < locals.Length)
        {
            this.slots = new Slot[Math.Max(locals.Length, 2 * this.slots.Length)];
            this.types = new BoundType[this.slots.Length];
            this.parameters = new BoundType[this.slots.Length];
        }

        var arguments = 0;
        for (var i = 0; i < locals.Length; i++)
        {
            if (this.collector.TypeOf(this.item, locals[i].Type) is not { } type || this.module.Layout.Of(type).Value is not { } value)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, locals[i].Source); // The collector laid out every local of an admitted item.
                return null;
            }

            (this.types[i], this.slots[i]) = (type, new(value.Layout.Size == 0 ? Home.None : Home.Slot, i, value));
            arguments += locals[i].Kind == MirLocalKind.Arg ? 1 : 0;
        }

        var decl = this.body.Key.Decl;
        for (var slot = 0; slot < arguments; slot++)
        {
            this.parameters[slot] = this.types[1 + MirCallee.Position(decl, slot)];
        }

        // SPEC 22.5.1: a caller-location library function receives its caller's location; the old generator's constructor and destructor
        // result slot is its own ABI choice, which local 0's Type replaces here (IMPL 21.3.1).
        var flags = KimiLibraryCatalog.RequiresCallerLocation(decl) ? Abi.Flags.CallerLocation : Abi.Flags.None;
        return this.module.Signature(this.item, flags, this.types[0], this.parameters.AsSpan(0, arguments));
    }

    // The entry block (I2): a pointer-passed argument and the result slot are their locals' homes; one aligned alloca per other local of
    // nonzero size; each Value argument stored into its local's slot.
    private void Prologue()
    {
        var output = this.output;
        output.Write(this.abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        foreach (var parameter in this.abi.Parameters)
        {
            if (parameter.Kind == AbiParameterKind.OwnedSlot)
            {
                ref var slot = ref this.slots[this.LocalOf(parameter)];
                slot = slot with { Home = Home.Argument, Index = parameter.LogicalIndex };
            }
            else if (parameter.Kind == AbiParameterKind.ResultSlot)
            {
                this.slots[0] = this.slots[0] with { Home = Home.Result };
            }
        }

        for (var i = 0; i < this.body.Locals.Length; i++)
        {
            if (this.slots[i] is { Home: Home.Slot, Value.Layout: var layout })
            {
                output.Write("  %l");
                WriteNumber(output, i);
                output.Write(" = alloca ");
                output.Write(layout.StorageType);
                output.Write(", align ");
                WriteNumber(output, layout.Alignment);
                output.Write('\n');
            }
        }

        foreach (var parameter in this.abi.Parameters)
        {
            if (parameter.Kind is AbiParameterKind.Value or AbiParameterKind.SharedReference)
            {
                var local = this.LocalOf(parameter);
                this.StoreAt(new('h', local), this.slots[local].Value.Layout, this.Widen(this.slots[local].Value, new('a', parameter.LogicalIndex)));
            }
        }

        output.Write("  br label %b0\n");
    }

    // The local an argument parameter passes: Arg local 1 plus the parameter's position (SPEC 7.3).
    private int LocalOf(in AbiParameter parameter) => 1 + MirCallee.Position(this.body.Key.Decl, parameter.LogicalIndex);

    // Return (IMPL 21.3.1): void for a zero-sized result or a result slot, else local 0's value in its computation representation.
    private void Return()
    {
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

    // A local's home and representation: Index is a pointer-passed argument's slot.
    private readonly record struct Slot(Home Home, int Index, ValueLowering Value);
}
