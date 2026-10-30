// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The parameter passing of FunctionCodegen (IMPL 21.3.1, SPEC 3.5; R5 package C 3.2 I2; DECISIONS R5-M65, R5-M89, R5-M92, R5-M102): the
// order in which a body kind's Arg locals hold its parameters, which the definition, its signature (ModuleCodegen.Signature) and every call
// read, the local an argument parameter passes, each call argument's value by its parameter's kind, and the copy slots of the Copy arguments
// (and of a Value call's callee operand) a callee takes by pointer, which the prologue allocates.
internal sealed partial class FunctionCodegen
{
    private TypeLayout[] copies = []; // The copy slots %s<k>, by number.
    private int[] copyOf = []; // By operand of the body, its copy slot or -1.
    private int copyCount;

    /// <summary>The order in which a body kind's Arg locals hold its parameters.</summary>
    internal enum ArgumentOrder : byte
    {
        /// <summary>The receiver first, then the other parameters in slot order (SPEC 7.3, MirCallee.Position).</summary>
        Positions,

        /// <summary>In slot order: a default template's prepared inputs p_i (plan 5.6.2).</summary>
        Slots,

        /// <summary>The environment first, Abi's [environment] (a Closure body; SPEC 7.7.3, DECISIONS R5-M102), then the parameters in slot
        /// order.</summary>
        Environment,
    }

    /// <summary>Gets the order in which a body kind's Arg locals hold its parameters: the one switch over the closed MirBodyKind vocabulary
    /// (package C 3.2 Dispatch), which the definition, its signature (ModuleCodegen.Signature) and every call of it read.</summary>
    /// <param name="kind">The body kind.</param>
    /// <returns>The order.</returns>
    internal static ArgumentOrder OrderOf(MirBodyKind kind) => kind switch
    {
        MirBodyKind.Fn or MirBodyKind.Test or MirBodyKind.StaticInit => ArgumentOrder.Positions, // Keyed by its own declaration.
        MirBodyKind.Getter or MirBodyKind.Setter => ArgumentOrder.Positions, // An accessor's, keyed by the function its calls target.
        MirBodyKind.DefaultTemplate => ArgumentOrder.Slots,
        MirBodyKind.Closure => ArgumentOrder.Environment,
        _ => throw new InvalidOperationException($"The MIR body kind {kind} has no code generation row."),
    };

    /// <summary>Gets the position of a callee's parameter at a slot among its Arg locals, which follow local 0, and so among the operands of
    /// every direct call of it (OrderOf): a body's own key, or a #LibraryImport function's, which has no body (a Fn of its declaration); a
    /// Closure's after its environment. A Value call's operands are its Callable signature's inputs in order, which its own key gives (a Fn
    /// without a function: MirCallee.Position without a receiver, the builder's rule).</summary>
    /// <param name="callee">The callee's key: its declaration and body kind.</param>
    /// <param name="slot">The parameter's slot.</param>
    /// <returns>The position.</returns>
    internal static int ArgumentPosition(MirDefKey callee, int slot) => OrderOf(callee.Kind) switch
    {
        ArgumentOrder.Slots => slot,
        ArgumentOrder.Environment => slot + 1,
        _ => MirCallee.Position(callee.Decl, slot),
    };

    // Whether a parameter takes no operand but the next of the values its call writer is given (Pass, Forward): a runtime routine's
    // slot-less value (a stride, a reason, a Value without a logical index), a Closure's environment or a context that names no parameter (a
    // context naming one, a formatting row's capacity, is measured from it; FunctionCodegen.Formatting's Measure).
    private static bool Given(in AbiParameter parameter)
        => parameter is { Kind: AbiParameterKind.Value or AbiParameterKind.Context, LogicalIndex: < 0 } or { Kind: AbiParameterKind.Environment };

    // SPEC 3.5 with IMPL 21.3.1 (DECISIONS R5-M89): a Copy of a value in memory that a runtime-reached Call passes to a parameter by pointer,
    // which the callee owns from the call on, is copied into a copy slot of its own, so the callee never sees its source change; a string
    // constant so passed is written into one as its record. One pass over the reached Calls' operands numbers the slots before the prologue
    // allocates them; the call writer finds an operand's by its index. The pass covers the operands a reached Call passes to an OwnedSlot
    // parameter (DECISIONS R5-M92 narrowed R5-M89's every operand): a body's callee takes a value in memory by pointer, so every in-memory
    // operand of a Fn, Requirement, Value or Virtual call; an intrinsic only through its row's routine (Raw.release, $abort) or a creation's
    // factory (its payload, DECISIONS R5-M110), so a Fail's code or Raw.initialize's value gets none; a compiler function of the Body
    // disposition, called directly or as an Item's Value call, by its legacy row (RowSlots; DECISIONS R5-M116). A Value call's callee
    // operand passed by value is its environment, which a Consuming body owns (SPEC 7.7.3: a Consuming call Copies a Copy Closure; DECISIONS
    // R5-M102): its slot is numbered after the body's operands by its block (CalleeCopy), since a block's terminator is its one call. The same
    // pass gives an interpolation buffer its Region home when its estimate places it on the frame (FunctionCodegen.Interpolation's Region)
    // and a segment step's non-empty literal text the slots of its record and of its write's result (TextSlots).
    private void Copies()
    {
        var count = this.body.Operands.Length + this.body.Blocks.Length;
        if (this.copyOf.Length < count)
        {
            this.copyOf = new int[Math.Max(count, 2 * this.copyOf.Length)];
        }

        this.copyOf.AsSpan(0, count).Fill(-1);
        this.copyCount = 0;
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            if (this.reach[block] && this.body.Blocks[block].Terminator is { Kind: MirTerminatorKind.Call } call)
            {
                this.CopySlots(call, block);
            }
        }
    }

    // The copy slots of a Call's operands that an OwnedSlot parameter takes (Copies), and of a Value callee's operand.
    private void CopySlots(in MirTerminator call, int block)
    {
        if (LegacyRuntimeHelpers.Row(this.CompilerFunction(call)) is { } legacy)
        {
            this.RowSlots(call, legacy, block);
            return;
        }

        if (call.Callee.Kind != MirCalleeKind.Intrinsic)
        {
            this.OperandSlots(call, block);
            return;
        }

        var row = IntrinsicRows.Of((IntrinsicId)call.Callee.Arg);
        switch (row.Writer)
        {
            case IntrinsicWriter.Create:
                // A creation's factory takes its payload at its kind, as a body callee does (ModuleCodegen.Factory; DECISIONS R5-M110).
                var payload = call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0);
                this.CopySlot(this.body.Operands[payload], payload);
                return;
            case IntrinsicWriter.Buffer:
                this.Region(call); // An interpolation buffer's home: its frame's region when its estimate places it there (DECISIONS R5-M131).
                return;
            case IntrinsicWriter.Segment:
                this.TextSlots(call, block); // A segment's literal text's record and its write's result (DECISIONS R5-M133).
                return;
            default:
                this.RoutineSlots(call, row.Routine);
                return;
        }
    }

    // The compiler function a Call calls: a Fn callee's, or the function Item a Value callee's operand holds below its reference layer (the
    // collector's Callable rule, FunctionCodegen.Closures' ValueCall); None for any other.
    private CompilerFunctionKind CompilerFunction(in MirTerminator call)
    {
        if (call.Callee.Kind == MirCalleeKind.Fn)
        {
            return call.Callee.Symbol!.CompilerFunction;
        }

        if (call.Callee.Kind != MirCalleeKind.Value || this.InstanceType(call.Operand) is not { } type)
        {
            return CompilerFunctionKind.None;
        }

        _ = TyCtxt.Layer(type, out var callable, out _);
        return callable is { Kind: BoundTypeKind.FunctionItem, Symbol: { } function } ? function.CompilerFunction : CompilerFunctionKind.None;
    }

    // The copy slots of a call whose callee takes its in-memory operands by pointer, a body's (Copies): each Copy or string constant operand's,
    // and a Value callee's operand's.
    private void OperandSlots(in MirTerminator call, int block)
    {
        for (var i = call.Start; i < call.Start + call.Count; i++)
        {
            this.CopySlot(this.body.Operands[i], i);
        }

        if (call.Callee.Kind == MirCalleeKind.Value)
        {
            this.CopySlot(call.Operand, this.body.Operands.Length + block);
        }
    }

    // A legacy row's slots (DECISIONS R5-M116, R5-M121, R5-M124, R5-M128): a fixed-symbol routine's OwnedSlot parameters (LegacyRow.Fixed:
    // writeLine's, a formatting routine's), as an intrinsic routine's; a row's in its function's instance (LegacyRow.InInstance) as a body
    // callee's (OperandSlots), since its helper takes an element in memory by pointer and copies it after it grows (`a.append(a[0])`);
    // a swap of a value in memory the one slot holding its first value (FunctionCodegen.Updates), numbered at its first operand, a reference,
    // which has no copy slot of its own; none for the others, which read their operands in place, no callee seeing them.
    private void RowSlots(in MirTerminator call, LegacyRow row, int block)
    {
        if (row.Fixed)
        {
            this.RoutineSlots(call, row.Routine);
            return;
        }

        if (row.InInstance)
        {
            this.OperandSlots(call, block);
            return;
        }

        var first = call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), 0);
        if (row.Writer == LegacyWriter.Swap && this.Referent(this.InstanceType(this.body.Operands[first])) is { } type &&
            this.module.Layout.Of(type).Value is { Layout.Size: > 0 } lowering && InMemory(type, lowering))
        {
            this.AddSlot(first, lowering.Layout);
        }
    }

    // The copy slots of the operands a routine's OwnedSlot parameters take.
    private void RoutineSlots(in MirTerminator call, FunctionAbi? routine)
    {
        foreach (var parameter in routine?.Parameters ?? [])
        {
            if (parameter.Kind == AbiParameterKind.OwnedSlot)
            {
                var index = call.Start + ArgumentPosition(new(call.Callee.Symbol, MirBodyKind.Fn), parameter.LogicalIndex);
                this.CopySlot(this.body.Operands[index], index);
            }
        }
    }

    // The copy slot of the callee operand of the Value call that ends the block being written (Copies), or -1.
    private int CalleeCopy() => this.copyOf[this.body.Operands.Length + this.at];

    // An operand's copy slot, numbered by its index, when it is a Copy of a value in memory or a string constant (its record, IMPL 21.1.3).
    private void CopySlot(in MirOperand copy, int operand)
    {
        TypeLayout? layout = null;
        if (this.TextOf(copy) is not null)
        {
            layout = RuntimeAbi.String.Layout;
        }
        else if (copy.Kind == MirOperandKind.Copy && this.LoweringOf(copy.Place) is { Layout.Size: > 0 } lowering &&
            InMemory(this.InstanceType(copy.Place)!, lowering))
        {
            layout = lowering.Layout;
        }

        if (layout is not null)
        {
            this.AddSlot(operand, layout);
        }
    }

    // A slot of the entry block numbered by an operand's index.
    private void AddSlot(int operand, TypeLayout layout)
    {
        if (this.copyCount == this.copies.Length)
        {
            Array.Resize(ref this.copies, Math.Max(4, 2 * this.copies.Length));
        }

        this.copyOf[operand] = this.copyCount;
        this.copies[this.copyCount++] = layout;
    }

    // An argument by its parameter's kind (IMPL 21.3.1): a Value or a string borrow its operand in its computation representation; an
    // OwnedSlot the address of storage the callee owns from the call on, a Move's Place itself (a string's too, whose Read is its record),
    // or a Copy's copy or a string constant's record in its copy slot (Copies; -1: none). False after a broken contract's record (the
    // operand's, or a constant or pair acquisition without the copy slot Copies gives it; DECISIONS R5-M146).
    private bool Argument(in MirOperand operand, int copy, AbiParameterKind kind, out Value value)
    {
        value = default;
        switch (kind)
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
            case AbiParameterKind.OwnedSlot when copy >= 0:
                value = new('s', copy);
                return this.LoweringOf(operand) is { } copied && this.Put(value, this.InstanceType(operand)!, copied, operand);
            case AbiParameterKind.OwnedSlot when operand.Kind is MirOperandKind.Copy or MirOperandKind.Move:
                return this.Address(operand.Place, out value);
            case AbiParameterKind.OwnedSlot:
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A constant or a pair acquisition takes its copy slot (Copies).
                return false;
            default:
                throw new InvalidOperationException($"The parameter kind {kind} has no argument row.");
        }
    }

    // The local an argument parameter passes: Arg local 1 plus its position (ArgumentPosition); a Closure body's environment, Arg local 1.
    private int LocalOf(in AbiParameter parameter)
        => parameter.Kind == AbiParameterKind.Environment ? 1 : 1 + ArgumentPosition(this.body.Key, parameter.LogicalIndex);
}
