// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The function frame of FunctionCodegen (R5 package C 3.2 I2; DECISIONS R5-M65, R5-M89): the order a body kind's Arg locals hold its
// parameters in, each local's home and layout under the signature ModuleCodegen gives the item, the copy slots of its calls' Copy
// arguments passed by pointer, the entry block (the prologue) and the return.
internal sealed partial class FunctionCodegen
{
    private TypeLayout[] copies = []; // The copy slots %s<k>, by number.
    private int[] copyOf = []; // By operand of the body, its copy slot or -1.
    private int copyCount;

    /// <summary>The order in which a body kind's Arg locals hold its parameters, or that the frame does not write the kind yet.</summary>
    internal enum ArgumentOrder : byte
    {
        /// <summary>Not written yet: a Closure's environment is C5a's row, a static's initializer C7b's.</summary>
        Unwritten,

        /// <summary>The receiver first, then the other parameters in slot order (SPEC 7.3, MirCallee.Position).</summary>
        Positions,

        /// <summary>In slot order: a default template's prepared inputs p_i (plan 5.6.2).</summary>
        Slots,
    }

    // Where a local lives (I2).
    private enum Home : byte
    {
        None, // Zero-sized: no storage.
        Slot, // Its alloca, %l<local>.
        Argument, // The pointer its caller passed, %a<slot>.
        Result, // The result slot, %ret.
    }

    /// <summary>Gets the order in which a body kind's Arg locals hold its parameters: the one switch over the closed MirBodyKind vocabulary
    /// (package C 3.2 Dispatch), which the definition, its signature (ModuleCodegen.Signature) and every call of it read.</summary>
    /// <param name="kind">The body kind.</param>
    /// <returns>The order, or Unwritten.</returns>
    internal static ArgumentOrder OrderOf(MirBodyKind kind) => kind switch
    {
        MirBodyKind.Fn or MirBodyKind.Getter or MirBodyKind.Setter or MirBodyKind.Test => ArgumentOrder.Positions,
        MirBodyKind.DefaultTemplate => ArgumentOrder.Slots,
        MirBodyKind.Closure or MirBodyKind.StaticInit => ArgumentOrder.Unwritten,
        _ => throw new InvalidOperationException($"The MIR body kind {kind} has no code generation row."),
    };

    /// <summary>Gets the position of a callee's parameter at a slot among its Arg locals, which follow local 0, and so among the operands of
    /// every call of it (OrderOf): a body's own key, or a #LibraryImport function's, which has no body (a Fn of its declaration).</summary>
    /// <param name="callee">The callee's key: its declaration and body kind.</param>
    /// <param name="slot">The parameter's slot.</param>
    /// <returns>The position.</returns>
    internal static int ArgumentPosition(MirDefKey callee, int slot)
        => OrderOf(callee.Kind) == ArgumentOrder.Slots ? slot : MirCallee.Position(callee.Decl, slot);

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
            if (this.collector.TypeOf(this.item, locals[i].Type) is not { } type || this.module.Layout.Of(type).Value is not { } value)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, locals[i].Source); // The collector laid out every local of an admitted item.
                return null;
            }

            (this.types[i], this.slots[i]) = (type, new(value.Layout.Size == 0 ? Home.None : Home.Slot, i, value));
        }

        return this.module.Signature(this.item);
    }

    // SPEC 3.5 with IMPL 21.3.1 (DECISIONS R5-M89): a Copy of a value in memory that a runtime-reached Call passes to a parameter by pointer,
    // which the callee owns from the call on, is copied into a copy slot of its own, so the callee never sees its source change; a string
    // constant so passed is written into one as its record. One pass over the reached Calls' operands numbers the slots before the prologue
    // allocates them; the call writer finds an operand's by its index. The pass covers every reached Call's in-memory Copy operands, whatever
    // its callee's row, until a later call row (C3b, C5a, C6c) uses these slots or narrows the scan; C8a's Abort and routine rows use them.
    private void Copies()
    {
        var operands = this.body.Operands;
        if (this.copyOf.Length < operands.Length)
        {
            this.copyOf = new int[Math.Max(operands.Length, 2 * this.copyOf.Length)];
        }

        this.copyOf.AsSpan(0, operands.Length).Fill(-1);
        this.copyCount = 0;
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            if (this.reach[block] && this.body.Blocks[block].Terminator is { Kind: MirTerminatorKind.Call } call)
            {
                for (var i = call.Start; i < call.Start + call.Count; i++)
                {
                    this.CopySlot(i);
                }
            }
        }
    }

    // An operand's copy slot, when it is a Copy of a value in memory or a string constant (its record, IMPL 21.1.3).
    private void CopySlot(int operand)
    {
        var copy = this.body.Operands[operand];
        TypeLayout? layout = null;
        if (copy.Kind == MirOperandKind.Const && this.body.Consts[copy.Arg] is { Kind: MirConstKind.String } constant &&
            ReferenceEquals(constant.Type, BoundType.String))
        {
            layout = RuntimeAbi.String.Layout;
        }
        else if (copy.Kind == MirOperandKind.Copy && this.LoweringOf(copy.Place) is { Layout.Size: > 0 } lowering &&
            InMemory(this.InstanceType(copy.Place)!, lowering))
        {
            layout = lowering.Layout;
        }

        if (layout is null)
        {
            return;
        }

        if (this.copyCount == this.copies.Length)
        {
            Array.Resize(ref this.copies, Math.Max(4, 2 * this.copies.Length));
        }

        this.copyOf[operand] = this.copyCount;
        this.copies[this.copyCount++] = layout;
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
                this.WriteSlot("  %l", i, layout);
            }
        }

        for (var i = 0; i < this.copyCount; i++)
        {
            this.WriteSlot("  %s", i, this.copies[i]);
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

    // The local an argument parameter passes: Arg local 1 plus its position (ArgumentPosition).
    private int LocalOf(in AbiParameter parameter) => 1 + ArgumentPosition(this.body.Key, parameter.LogicalIndex);

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
