// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The parameter passing of FunctionCodegen (IMPL 21.3.1, SPEC 3.5; R5 package C 3.2 I2; DECISIONS R5-M65, R5-M89, R5-M92): the order in
// which a body kind's Arg locals hold its parameters, which the definition, its signature (ModuleCodegen.Signature) and every call read,
// the local an argument parameter passes, each call argument's value by its parameter's kind, and the copy slots of the Copy arguments
// a callee takes by pointer, which the prologue allocates.
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

    // SPEC 3.5 with IMPL 21.3.1 (DECISIONS R5-M89): a Copy of a value in memory that a runtime-reached Call passes to a parameter by pointer,
    // which the callee owns from the call on, is copied into a copy slot of its own, so the callee never sees its source change; a string
    // constant so passed is written into one as its record. One pass over the reached Calls' operands numbers the slots before the prologue
    // allocates them; the call writer finds an operand's by its index. The pass covers the operands a reached Call passes to an OwnedSlot
    // parameter (DECISIONS R5-M92 narrowed R5-M89's every operand): a body's callee takes a value in memory by pointer, so every in-memory
    // operand of a Fn, Requirement, Value or Virtual call; an intrinsic only through its row's routine (Raw.release, $abort), so a Fail's
    // code or Raw.initialize's value gets none.
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
                this.CopySlots(call);
            }
        }
    }

    // The copy slots of a Call's operands that an OwnedSlot parameter takes (Copies).
    private void CopySlots(in MirTerminator call)
    {
        if (call.Callee.Kind != MirCalleeKind.Intrinsic)
        {
            for (var i = call.Start; i < call.Start + call.Count; i++)
            {
                this.CopySlot(i);
            }

            return;
        }

        foreach (var parameter in IntrinsicRows.Of((IntrinsicId)call.Callee.Arg).Routine?.Parameters ?? [])
        {
            if (parameter.Kind == AbiParameterKind.OwnedSlot)
            {
                this.CopySlot(call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), parameter.LogicalIndex));
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

    // An argument for its parameter (IMPL 21.3.1): a Value or a string borrow its operand in its computation representation; an OwnedSlot
    // the address of storage the callee owns from the call on, a Move's Place itself (a string's too, whose Read is its record), or a
    // Copy's copy or a string constant's record in its copy slot (Copies). False for a form not covered yet.
    private bool Argument(int index, in AbiParameter parameter, out Value value)
    {
        var operand = this.body.Operands[index];
        value = default;
        switch (parameter.Kind)
        {
            case AbiParameterKind.Value or AbiParameterKind.SharedReference:
                if (!this.Operand(operand, out value))
                {
                    return false;
                }

                if (this.LoweringOf(operand) is { } lowering)
                {
                    value = this.Narrow(lowering, value);
                }

                return true;
            case AbiParameterKind.OwnedSlot when this.copyOf[index] >= 0 && operand.Kind == MirOperandKind.Const:
                value = new('s', this.copyOf[index]);
                this.Text(value, (string)this.body.Consts[operand.Arg].Value!);
                return true;
            case AbiParameterKind.OwnedSlot when this.copyOf[index] >= 0:
                if (!this.Read(operand.Place, out var source) || this.LoweringOf(operand.Place) is not { } copied)
                {
                    return false;
                }

                value = new('s', this.copyOf[index]);
                this.Put(value, this.InstanceType(operand.Place)!, copied, source);
                return true;
            case AbiParameterKind.OwnedSlot:
                return operand.Kind is MirOperandKind.Copy or MirOperandKind.Move && this.Address(operand.Place, out value);
            default:
                throw new InvalidOperationException($"The parameter kind {parameter.Kind} has no argument row.");
        }
    }

    // The local an argument parameter passes: Arg local 1 plus its position (ArgumentPosition).
    private int LocalOf(in AbiParameter parameter) => 1 + ArgumentPosition(this.body.Key, parameter.LogicalIndex);
}
