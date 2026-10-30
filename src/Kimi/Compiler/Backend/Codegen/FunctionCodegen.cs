// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// FunctionCodegen (R5 package C 3.2, 3.6 and 3.9, design 2 2.2; DECISIONS R5-M65), MIR code generation: the LLVM definition of one body
// item of the checked set from its validated, analyzed MIR body in the item's instance (a StaticInit item's with its static's slot), of
// one DropGlue item by its Type's row, of one ClosureErasure or ItemAdapter item or of one Builtin or PropertyWitness item. Every MIR
// form of an accepted program has its row; a form the backend does not generate is the capability table's gap at check (DECISIONS
// R5-M146). Each concern file (Files) owns its rows, in its header or at its writer: the forms it
// writes and the DECISIONS entries behind them. This file dispatches each statement, rvalue and terminator to its row, writes the unconditional
// branch and the SwitchInt, and makes the records; StorageLive, StorageDead, FakeRead and EndBorrow generate nothing (package C 3.9).
// Inputs: the item and its body; its Types (InstanceCollector.TypeOf), its view (CheckedSet's View, selected once with its own MirCases,
// X13), its records' location rule (InstanceCollector.Subject, K package 4.9) and targets; the program's drop table and each body's
// exactness (A2, R4-M6a, R4-M102: gate step 2 writes no inexact body); the compilation's Layout and runtime location texts;
// ModuleCodegen's signatures (Signature and Import: Abi.Of under the internal and the C convention, IMPL 21.3.1), constant pool and
// record list.
// Output: the definition text appended to the module's buffer; records through ModuleCodegen.Report.
// Invariants (design 2 2.2): I1 deterministic: blocks b<index>, slots %l<local>, values %t<n> numbered per function; I2 one aligned alloca
// per local of nonzero size in the entry block, bool stored as i8 (IMPL 21.1.3), none for a zero-sized local, a Value argument stored into
// its local's slot, a pointer-passed argument and the result slot used in place, then one per copy slot (R5-M89) and one per drop flag,
// cleared (R5-M99); I3 exactly the blocks MirReach.Walk(body, 0, checks: false) reaches, in index order, so code only the checks reach is
// never generated (SPEC 14.10.3); I4 an operand is read at each use (loaded, or its address when its value is in memory); I5 a check not
// Proven Aborts at its site's location (SPEC 22.5.1; in a caller-location body the location it received, R5-M93); I7 every Type is
// Layout.Of(TypeOf(item, t)); I8 a call's target is the collector's at its site, read by one forward cursor; I9 no nsw, nuw or inbounds;
// I10 no store i1. A warm write allocates nothing.
// Files: the dispatch, the unconditional branch and the records (this file); the operands' and constants' values, the representations
// and a site's location (FunctionCodegen.Values.cs); the text's spelling (.Spelling.cs); the function's frame: homes, prologue and return
// (.Frame.cs); parameter passing: its kind's argument order, each argument by its parameter and the copy slots (.Arguments.cs); the place
// writer (.Places.cs) and the loads, stores and copies through it (.Stores.cs); the rvalues (.Rvalues.cs) and aggregates (.Aggregates.cs),
// operators (.Arithmetic.cs) and the string comparisons among them (.Strings.cs; DECISIONS R5-M118), casts by their kind (.Casts.cs) and
// the scalar conversions (.Conversions.cs), calls (.Calls.cs) and value calls (.Closures.cs), the shims' functions, Builtin and
// PropertyWitness (.Shims.cs; DECISIONS R5-M126, R5-M130, R5-M141), intrinsics (.Intrinsics.cs), the Drop
// terminator and A2's verdict rows (.Drops.cs) and drop flags (.Flags.cs); a value's destruction one level per Type, a DropGlue item's
// function or an Open row's (.Glue.cs); the common Function values: erasure, the adapter items' functions and a Function value's glue row
// (.Erasures.cs); a static's slot, its ensure function and an access's state test (.Statics.cs); the handle operations: a creation, a
// clone, a runtime Type test and a virtual slot's entry (.Objects.cs); the whole-value updates, replace, exchange and swap (.Updates.cs),
// and the inline storage kernels (.Storage.cs), the legacy rows of compiler functions (DECISIONS R5-M116); the formatting rows' Context
// words and witness targets, a user formatter's write wrapper and Text.writer's words (.Formatting.cs; DECISIONS R5-M128, R5-M130); an
// owning interpolation's buffer, hints and end, its estimate and its buffer's Region home (.Interpolation.cs; DECISIONS R5-M131); the
// Array rows written in place: an Array from none, setArrayLength,
// capacity and an Array's glue level (.Arrays.cs; DECISIONS R5-M121); the Dictionary rows written in place: a Dictionary from none and a
// Dictionary's glue level (.Dictionaries.cs; DECISIONS R5-M124); a test build's phases, the cleanup window and a terminator's completion
// (.Phases.cs; DECISIONS R5-M137).
// Must not read syntax, HIR, plans, Binding internals or the old Analysis/Emission, re-derive an acceptance fact or a case selection
// (MirCases decides a PairLayer under the item's view), compose a Subst or a Type (TypeOf does), resolve a callee, or use LINQ, Supports*,
// out-string failures or catch-alls, or lay out a Type outside the item's by-value set (its locals, constants and aggregates) and its
// type-driven Type but through Needed, the Type a form's LayoutNeeds row names (DECISIONS R5-M149), so the collector's K 4.8 uses cover
// every Type code generation lays out. An Opaque value (the gate admits none), a Type TypeOf cannot make, a Layout outcome other than Ok
// and any other input validated MIR never has are broken contracts: InternalInvariant_Kd, recorded once by the owner that detects it, at
// the site it has, else the body's subject (DECISIONS R5-M146).

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>Writes the LLVM definition of one body item from its MIR (R5 package C 3.2).</summary>
internal sealed partial class FunctionCodegen
{
    private readonly ModuleCodegen module;
    private readonly MirReach reach = new();
    private readonly MirCases cases; // The item's case view (X13).
    private readonly TyCtxt typeContext; // Its Sealed proofs, which a handle's glue parts read (DropGlue.Parts).
    private Slot[] slots = []; // By local.
    private BoundType[] types = []; // By local, in the item's instance.
    private Value[] arguments = []; // A runtime call's arguments in parameter order, or a fixed-array aggregate's element values.
    private InstanceCollector collector = null!;
    private MirProgram program = null!; // The program, whose body the item replays and whose drop table its drops read.
    private FunctionAbi abi = null!; // The item's signature.
    private MirBody body = null!;
    private TextWriter output = TextWriter.Null;
    private int item;
    private int at; // The block being written.
    private int position; // The statement being written, its index in its block (a statement site's, I8).
    private int next; // The number of the next value %t<n>.

    /// <summary>Initializes a new instance of the <see cref="FunctionCodegen"/> class.</summary>
    /// <param name="module">The module whose signatures and records it uses.</param>
    /// <param name="types">The Type context of the program, whose case Types the item's view selects.</param>
    internal FunctionCodegen(ModuleCodegen module, TyCtxt types) => (this.module, this.typeContext, this.cases) = (module, types, new(types));

    /// <summary>Writes the definition of a body item, or records why not.</summary>
    /// <param name="program">The program, whose body the item replays and whose drop table its drops read.</param>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void Write(MirProgram program, InstanceCollector collector, int item, TextWriter output)
    {
        var body = program.Bodies[collector.Set.Items[item].BodyOrdinal];
        (this.program, this.collector, this.item, this.body, this.output, this.next) = (program, collector, item, body, output, 0);
        if (program.Drops.Incomplete(body.Ordinal) is { } refusal)
        {
            // Gate step 2 (PD12, package C 3.8; DECISIONS R5-M96): A2's verdicts are exact only for a body A1 accepts (R4-M6a), so a body
            // A1 or A2 left inexact (R4-M102: an inline copy's problem marks its caller) is not written; its refusal is that problem's own
            // code at its subject, by the location rule (K 4.9: out of an inline default outside the root modules, R4-M132), and every
            // other item is still written, so its own refusals are recorded too. Interim: a body A1 skipped at the arena limit (ViewsOf 0,
            // R4-M15a) is one located Unsupported_Kd at the body, since a generation record is location-only, code generation's one
            // interim Unsupported_Kd (COMPILER_ARCHITECTURE, DECISIONS R5-M146, R5-M149); removed at H3 once a record carries the arena
            // problem's Required, Limit and Table facts (refusal.Problem's) as requiredBytes, limitBytes and table.
            var code = program.Drops.ViewsOf(body.Ordinal) == 0 ? DiagnosticCode.Unsupported_Kd : refusal.Code;
            this.module.Report(code, collector.Subject(item, body, refusal.Source));
            return;
        }

        if (body.Key.Kind == MirBodyKind.StaticInit)
        {
            this.WriteStatic(); // A static's slot, and its initializer's function unless that folds into the slot (FunctionCodegen.Statics).
            return;
        }

        _ = this.Define();
    }

    // The function of the body item being written, then its signature; null after a record.
    private FunctionAbi? Define()
    {
        if (this.LayOut() is not { } abi)
        {
            return null;
        }

        _ = this.cases.Select(this.body, this.collector.Set.Items[this.item].View); // X13: the collector's one view of the item's body.
        (this.abi, this.cursor, this.point) = (abi, 0, 0);
        this.reach.Clear(this.body.Blocks.Length);
        this.reach.Walk(this.body, 0, checks: false);
        this.Copies();
        this.StaticSlots();
        this.Prologue();
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            if (this.reach[block])
            {
                this.Block(block);
            }
        }

        this.output.Write("}\n");
        return abi;
    }

    private void Block(int index)
    {
        var block = this.body.Blocks[this.at = index];
        this.window = -1; // A window never spans blocks; a block that ended without a successor left its own open (FunctionCodegen.Phases).
        this.output.Write('b');
        WriteNumber(this.output, index);
        this.output.Write(":\n");
        var statements = this.body.Statements.Slice(block.Start, block.Count);
        for (var i = 0; i < statements.Length; i++)
        {
            this.position = i;
            this.Statement(statements[i]);
            this.Points(new(index, i));
        }

        this.Terminator(block.Terminator);
    }

    private void Statement(in MirStatement statement)
    {
        switch (statement.Kind)
        {
            case MirStatementKind.Assign:
                this.Position(statement.Source);
                this.Assign(statement);
                return;
            case MirStatementKind.StorageLive or MirStatementKind.StorageDead or MirStatementKind.FakeRead or MirStatementKind.EndBorrow:
                return; // Nothing is generated (package C 3.9); A2's flag points at them follow them (Block).
            default:
                throw new InvalidOperationException($"The MIR statement kind {statement.Kind} has no code generation row.");
        }
    }

    // An Assign by its rvalue's row, into its Place through the place writer.
    private void Assign(in MirStatement statement)
    {
        switch (statement.Rvalue.Kind)
        {
            case MirRvalueKind.Opaque:
                this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source); // The gate passes no program with an Opaque value.
                return;
            case MirRvalueKind.Use:
                this.Use(statement);
                return;
            case MirRvalueKind.BinaryOp or MirRvalueKind.UnaryOp:
                this.Operator(statement);
                return;
            case MirRvalueKind.Ref:
                this.Ref(statement);
                return;
            case MirRvalueKind.Cast:
                this.Cast(statement);
                return;
            case MirRvalueKind.Aggregate:
                this.Aggregate(statement);
                return;
            case MirRvalueKind.Discriminant:
                this.Discriminant(statement);
                return;
            case MirRvalueKind.Len:
                this.Len(statement);
                return;
            case MirRvalueKind.Adapt:
                this.Adapt(statement);
                return;
            default:
                throw new InvalidOperationException($"The MIR rvalue kind {statement.Rvalue.Kind} has no code generation row.");
        }
    }

    private void Terminator(in MirTerminator terminator)
    {
        switch (terminator.Kind)
        {
            case MirTerminatorKind.Goto:
                this.Branch(terminator.Target);
                return;
            case MirTerminatorKind.SwitchInt:
                this.Switch(terminator);
                return;
            case MirTerminatorKind.Return:
                this.Return();
                return;
            case MirTerminatorKind.Unreachable:
                this.output.Write("  unreachable\n");
                return;
            case MirTerminatorKind.Call:
                this.Position(terminator.Source);
                this.Call(terminator);
                return;
            case MirTerminatorKind.Drop:
                this.Drop(terminator);
                return;
            default:
                throw new InvalidOperationException($"The MIR terminator kind {terminator.Kind} has no code generation row.");
        }
    }

    // A terminator's transfer to its one runtime successor: its location completed (FunctionCodegen.Phases), then the branch.
    private void Branch(int target)
    {
        this.Completed();
        this.output.Write("  br ");
        this.WriteLabel(target);
        this.output.Write('\n');
    }

    // A SwitchInt on its operand's stored value (bool as i8, I10): each value-target pair a case, otherwise the target; its location
    // completed before it (its flag points hold on every successor).
    private void Switch(in MirTerminator terminator)
    {
        var operand = terminator.Operand;
        if (!this.Operand(operand, out var value) || this.LoweringOf(operand) is not { } lowering)
        {
            return; // A broken contract, recorded.
        }

        this.Completed();
        var output = this.output;
        var type = lowering.Layout.StorageType;
        var width = 8 * lowering.Layout.Size;
        output.Write("  switch ");
        output.Write(type);
        output.Write(' ');
        this.WriteValue(value);
        output.Write(", ");
        this.WriteLabel(terminator.Target);
        output.Write(" [");
        foreach (var arm in this.body.Cases.Slice(terminator.Start, terminator.Count))
        {
            output.Write(' ');
            output.Write(type);
            output.Write(' ');
            WriteNumber(output, Signed(arm.Value, width));
            output.Write(", ");
            this.WriteLabel(arm.Target);
        }

        output.Write(" ]\n");
    }

    // A record at a site of a body item's body, located by the collector's rule (K package 4.9), or at a type-driven item's site (a glue's).
    private void Report(DiagnosticCode code, in SourceInfo source)
    {
        var entry = this.collector.Set.Items[this.item];
        this.module.Report(code, entry.Key.Kind == MonoKind.Body ? this.collector.Subject(this.item, this.body, source) : entry.Site);
    }
}
