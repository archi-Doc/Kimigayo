// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Statics (R5 plan C7b, package C 3.3 and 3.7; SPEC 22.4.3, 22.4.4, 22.5.1, 22.5.2; DECISIONS R5-M106), FunctionCodegen's static slots.
// Responsibility: a static's slot and its accesses. One slot per StaticInit item, the static's storage key (the collector's item identity,
// which DECISIONS R4-M103 makes P42's static slot key): @__kimi_static<item> in the Layout of the StaticInit's result Type and its state
// @__kimi_static<item>_state, an i8 of SPEC 22.4.3's four states (StaticState), written after the StaticInit's function with the slot's
// ensure function @__kimi_static<item>_ensure (ModuleCodegen.Ensure). A StaticInit whose block 0 holds only storage markers and one
// _0 = Use(Const), then Return (ConstantOf, package C 3.7's fold), is that constant instead: its slot an internal unnamed_addr constant
// spelled as an operand's constant (Constant), with no state, ensure function or function (package C 3.3). A staticEnsure (the StaticEnsure
// row's Ensure writer) of a slot that is not a constant reads the state inline and, unless Initialized, calls the ensure function with the
// access's location (SPEC 22.5.1: the Place expression's start, or a caller-location body's received location, DECISIONS R5-M93). A static
// Place's root is the slot its body's staticEnsures of that static target (StaticSlots, StaticAddress).
// Inputs: the item, its body and the collector's targets (a staticEnsure's StaticInit item, InstanceCollector.Static); the Layout; the
// StaticInit's signature (ModuleCodegen.Signature) and its slot's ensure function's.
// Outputs: the slot's, state's and ensure function's definitions, or the constant slot; an access's state test and call.
// Invariants: the ensure function Aborts with KIMI_E_STATIC_CYCLE at the location it received when the slot has started (state != Not
// started: Initializing in R5, where Finished is never stored, so a Finished slot would Abort rather than initialize again, fail-closed
// until P42 gives Finished KIMI_E_STATIC_SHUTDOWN), else marks it Initializing, calls the StaticInit into the slot and marks it Initialized
// (SPEC 22.4.3), in a test build the call in the initialization phase (FunctionCodegen.Phases; DECISIONS R5-M137); a slot's value is read
// and written only through the place writer and these stores; a warm write allocates nothing.
// Must not decide which statics are generated: the StaticStorage capability row (DECISIONS R5-M82) refuses at check a static that can be
// set, whose Type is not a scalar or that belongs to a generic container's group, so only scalars reach here and no Type is tested. A
// staticEnsure without a StaticInit target and two slots for one static (P42's keys) are broken contracts: InternalInvariant_Kd; the
// validator checks that a static Place's body ensures it (MirBuilder.Places' contract, G191).

using Kimi.Compiler.Mir;
using Kimi.Testing;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>Writes the static slots and their accesses (R5 package C 3.7).</summary>
internal sealed partial class FunctionCodegen
{
    private static readonly TypeLayout StateLayout = Layout.Row(BoundType.Primitives["u8"]).Layout; // A slot's state, an i8.
    private static readonly Site Accessed = new(new('p', 0), new('p', 1)); // An ensure function's location, its own parameters a0 and a1.
    private int[] statics = []; // By static of the body being written, the StaticInit item its staticEnsures target, or -1.

    // SPEC 22.4.3's slot states, a state's stored values.
    private enum StaticState : byte
    {
        NotStarted,
        Initializing,
        Initialized,
        Finished, // P42's shutdown (DECISIONS R5-M13 (b)), never stored in R5.
    }

    // The constant of a StaticInit body whose block 0 holds only storage markers and one _0 = Use(Const), then Return (the fold), its index;
    // -1 for any other body.
    private static int ConstantOf(MirBody body)
    {
        var block = body.Blocks[0];
        var constant = -1;
        foreach (var statement in body.Statements.Slice(block.Start, block.Count))
        {
            if (statement.Kind is MirStatementKind.StorageLive or MirStatementKind.StorageDead)
            {
                continue;
            }

            if (constant >= 0 || statement is not { Kind: MirStatementKind.Assign, Place: { Root: 0, Projection: 0 }, Rvalue.Kind: MirRvalueKind.Use } ||
                body.Operands[statement.Rvalue.Start] is not { Kind: MirOperandKind.Const } operand)
            {
                return -1;
            }

            constant = operand.Arg;
        }

        return block.Terminator.Kind == MirTerminatorKind.Return ? constant : -1;
    }

    // A StaticInit item: its constant slot when it folds, else its function, then its slot, state and ensure function.
    private void WriteStatic()
    {
        if (ConstantOf(this.body) is >= 0 and var constant)
        {
            this.Fold(this.body.Consts[constant]);
        }
        else if (this.Define() is { } initializer)
        {
            this.WriteStorage(initializer);
        }
    }

    // A folded StaticInit's slot: an internal unnamed_addr constant, its value spelled as the operand's (Constant). A static's Type is a
    // scalar (DECISIONS R5-M82), whose constant Constant writes, so a Const it writes no value of is a broken contract.
    private void Fold(in MirConst constant)
    {
        if (this.Representation(this.body.Locals[0].Type) is not { } lowering)
        {
            return; // A broken contract, recorded.
        }

        if (!this.Constant(constant, out var value) || lowering.Layout.Size == 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default);
            return;
        }

        var output = this.output;
        this.WriteValue(new('v', this.item));
        output.Write(" = internal unnamed_addr constant ");
        output.Write(lowering.Layout.StorageType);
        output.Write(' ');
        this.WriteValue(value);
        output.Write(", align ");
        WriteNumber(output, lowering.Layout.Alignment);
        output.Write('\n');
    }

    // A StaticInit item's slot, zero until initialized, its state, Not started, and its ensure function (SPEC 22.4.3; the Invariants above):
    // the state test and the cycle's Abort at the location received, through LlvmScalarText's failure block, then the StaticInit's call
    // into the slot (its result slot, or its value stored) between the Initializing and Initialized stores.
    private void WriteStorage(FunctionAbi initializer)
    {
        var output = this.output;
        var item = this.item;
        var lowering = this.slots[0].Value;
        this.WriteGlobal(new('v', item), lowering.Layout, "zeroinitializer");
        this.WriteGlobal(new('w', item), StateLayout, "0");
        (this.abi, this.next, this.window) = (this.module.Ensure(item), 0, -1);
        output.Write(this.abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        var state = this.LoadAt(new('w', item), StateLayout);
        var id = this.next++;
        Name(output, "  %started", id);
        output.Write(" = icmp ne i8 ");
        this.WriteValue(state);
        output.Write(", ");
        WriteNumber(output, (int)StaticState.NotStarted);
        output.Write('\n');
        LlvmScalarText.WriteFailure(output, "%started", AbortReasons.StaticCycle, this.Failure(id, Accessed, stackalloc char[3 * SpellingLength]));
        this.StoreAt(new('w', item), StateLayout, new('\0', (int)StaticState.Initializing));
        this.Open(TestRuntime.Phase.Initialization);
        if (this.WriteCall(initializer, initializer.ResultSlot ? [new Value('v', item)] : []) is { } result)
        {
            this.Put(new('v', item), this.types[0], lowering, this.Widen(lowering, result));
        }

        this.Close();
        this.StoreAt(new('w', item), StateLayout, new('\0', (int)StaticState.Initialized));
        output.Write("  ret void\n}\n");
    }

    // An internal global of the module, zero-initialized: a slot or its state.
    private void WriteGlobal(Value global, TypeLayout layout, string zero)
    {
        var output = this.output;
        this.WriteValue(global);
        output.Write(" = internal global ");
        output.Write(layout.StorageType);
        output.Write(' ');
        output.Write(zero);
        output.Write(", align ");
        WriteNumber(output, layout.Alignment);
        output.Write('\n');
    }

    // The StaticInit item of each static of the body being written, which its staticEnsures target (K package 4.5 row 7), read from the
    // item's targets at those sites; two items for one static, which one root per Field cannot tell apart, are a broken contract (P42 publishes
    // each access's key instead).
    private void StaticSlots()
    {
        var count = this.body.Statics.Length;
        if (count == 0)
        {
            return;
        }

        if (this.statics.Length < count)
        {
            this.statics = new int[Math.Max(count, 2 * this.statics.Length)];
        }

        this.statics.AsSpan(0, count).Fill(-1);
        foreach (var target in this.collector.Set.Targets(this.item))
        {
            var block = this.body.Blocks[target.At.Block];
            if (target.At.Index != block.Count ||
                block.Terminator is not { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } } call)
            {
                continue;
            }

            ref var slot = ref this.statics[(int)this.body.Consts[this.body.Operands[call.Start].Arg].Bits];
            if (slot >= 0 && slot != target.Item)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
            }

            slot = target.Item;
        }
    }

    // A static Place's root: its slot (StaticSlots), which a staticEnsure of the body targets (the validator's G191, Ensure's target).
    private Value StaticAddress(int index) => new('v', this.statics[index]);

    // SPEC 22.4.3: a staticEnsure, its target the static's StaticInit item (InstanceCollector.Static): nothing for a folded slot; else, after
    // the call's flag points, the slot's state read, the access proceeding when it is Initialized and otherwise after the ensure function,
    // given the access's location (SPEC 22.5.1); its Unit result has no storage.
    private void Ensure(in MirTerminator call, int target)
    {
        if (target < 0 || this.collector.Set.Items[target] is not { Key.Kind: MonoKind.Body } entry)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // The collector targets every staticEnsure of a checked program.
            return;
        }

        if (ConstantOf(this.program.Bodies[entry.BodyOrdinal]) >= 0)
        {
            this.Finish(call, null);
            return;
        }

        if (!this.Location(call.Source, out var site))
        {
            return; // Recorded.
        }

        this.Completed();
        var output = this.output;
        var state = this.LoadAt(new('w', target), StateLayout);
        var ready = new Value('t', this.next++);
        output.Write("  ");
        this.WriteValue(ready);
        output.Write(" = icmp eq i8 ");
        this.WriteValue(state);
        output.Write(", ");
        WriteNumber(output, (int)StaticState.Initialized);
        output.Write("\n  br i1 ");
        this.WriteValue(ready);
        output.Write(", ");
        this.WriteLabel(call.Target);
        Name(output, ", label %ensure", (int)ready.Number);
        Name(output, "\nensure", (int)ready.Number);
        output.Write(":\n");
        _ = this.WriteCall(this.module.Ensure(target), [site.Text, site.Length]);
        output.Write("  br ");
        this.WriteLabel(call.Target);
        output.Write('\n');
    }
}
