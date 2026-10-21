// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// FunctionCodegen (R5 plan C0b1-C0c, C1a, C1b, C2a, C2b, C3a, C3b, C8a, C4a, C4b, package C 3.2, 3.6 and 3.9, design 2 2.2; DECISIONS
// R5-M65, R5-M73, R5-M76, R5-M78, R5-M80, R5-M83, R5-M87, R5-M89, R5-M92, R5-M93, R5-M95, R5-M99), MIR code generation: the LLVM definition of one body item
// of the checked set from its validated, analyzed MIR body in the item's instance, or of one DropGlue item by its Type's row, and the
// coverage rows (PD11) of the MIR forms: a form the frame does not cover yet is one located Unsupported_Kd, never wrong code.
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
// I10 no store i1. Covered: StorageLive, StorageDead, FakeRead and EndBorrow (nothing is generated); an Assign into a Place the place
// writer locates (FunctionCodegen.Places: a local's home, a zero-sized one's the module's zero-sized symbol, and the projections, an Index
// with its bounds check), after its rvalue, each value put in its representation (a value in memory, a slot Type other than the string's
// record, copied by memcpy; nothing for a zero-sized one): of a Use, from a Copy, Move or PairAcquire of such a Place (a PairAcquire's two
// cases write the same bits) or a Bool, Integer, Float or Unit constant or a raw pointer's null, or of a string constant to a string Place
// (its UTF-8 bytes a pooled constant, Static, IMPL 21.1.3), or of a BinaryOp or UnaryOp on integer, wrapping integer, floating-point, bool,
// char and Unit operands and a raw pointer's == and != and displacement by the operator table (FunctionCodegen.Arithmetic; R5-M92), or of a
// numeric, wrapping, bit or raw pointer Cast by the conversion table (FunctionCodegen.Casts), or of a Ref, or of a Tuple, struct, enum Case
// or fixed-array Aggregate of such operands, or of a Fill of one, a counted loop over its instance's length, or of a Discriminant, of a Len
// of a fixed array, Array or Slice, or of an Adapt by the rvalue the item's view selects (FunctionCodegen.Rvalues and .Aggregates; R5-M78,
// R5-M83, R4-M81, R5-M87); Goto, SwitchInt, Return and Unreachable; a Call of the console writeLine, a compiler function's legacy runtime
// row (RuntimeAbi.WriteLine) given the call site's location text (SPEC 22.5.1), of a function's instance or a requirement's witness under
// its signature, a caller-location callee given the call's location, or of a #LibraryImport function under its C signature, each argument
// by its parameter, a Copy of a value in memory and a string constant through a copy slot, the result slot the destination, unreachable
// after a call without a target (FunctionCodegen.Calls; R5-M89, R5-M93), of an intrinsic by its row's writer (FunctionCodegen.Intrinsics:
// Raw's storage operations, the byte copy, the nullable Alloc, $abort and Fail; R5-M92); a DefaultTemplate body, its prepared inputs in
// slot order (OrderOf); a Drop's Keep, Elide and Flag verdicts and A2's flag points, written where their locations complete
// (FunctionCodegen.Drops.cs and .Flags.cs; R5-M78, R5-M99). Rows not written yet: Closure (C5a) and StaticInit (C7b) bodies; statics (C7b);
// a handle's or object borrow's Deref (C6a); Closure aggregates (C5a), an Array or Dictionary from none (C8d, C8e), a Dictionary's (C8e)
// and a string's (C8c) Len, an Adapt's object creation (C6a); upcasts (C6a) and erasures (C5b); string operations (C8c), checked 128-bit
// arithmetic (ModuleCodegen's wide overflow declarations); FnItem (C5a) and other string constants (C8c), Location and TypeToken constants
// being intrinsic operands (C10a's TestObserve; C6a's TypeTest, C9a); calls of a requirement whose witness has no body, of a value (C5a), a
// virtual slot (C6c), an intrinsic of a later row (its IntrinsicRows row names the unit), a Builtin shim (C8f, C9b), a property witness
// (C3c) or another legacy row (C8b-C8e, C9a); a Drop's Open verdict (C4c). A warm write allocates nothing. Files: the dispatch, the
// unconditional branch and the records (this file); the operands' and constants' values, the representations, a site's location and the
// text's spelling (FunctionCodegen.Values.cs); the function's frame: homes, prologue and return (.Frame.cs); parameter passing: its kind's
// argument order, each argument by its parameter and the copy slots (.Arguments.cs); the place writer (.Places.cs) and the loads, stores
// and copies through it (.Stores.cs); the rvalues (.Rvalues.cs) and aggregates (.Aggregates.cs), operators (.Arithmetic.cs), conversions
// (.Casts.cs), calls (.Calls.cs), intrinsics (.Intrinsics.cs), drops (.Drops.cs) and drop flags (.Flags.cs); the DropGlue items' functions
// (.Glue.cs).
// Must not read syntax, HIR, plans, Binding internals or the old Analysis/Emission, re-derive an acceptance fact or a case selection
// (MirCases decides a PairLayer under the item's view), compose a Subst or a Type (TypeOf does), resolve a callee, or use LINQ, Supports*,
// out-string failures or catch-alls. An Opaque value (the gate admits none), a Type TypeOf cannot make and a Layout outcome other than
// Ok are broken contracts: InternalInvariant_Kd.

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
            // code at its subject, by the location rule, and every other item is still written, so its gaps are recorded too. Interim: a
            // body A1 skipped at the arena limit (ViewsOf 0, R4-M15a) is one located Unsupported_Kd at the body, since its problem carries
            // no arguments for OwnershipStorageLimit_Kd; removed once A1's arena problem publishes requiredBytes, limitBytes and the table.
            var (code, subject) = program.Drops.ViewsOf(body.Ordinal) == 0 ? (DiagnosticCode.Unsupported_Kd, body.Source) : refusal;
            this.module.Report(code, collector.Subject(item, subject));
            return;
        }

        if (OrderOf(body.Key.Kind) == ArgumentOrder.Unwritten)
        {
            // Rows not written yet, at the body: C5a writes a Closure's environment, and C7b a static's initializer, which a constant one
            // folds into its slot and is never a function (package C 3.3).
            this.Report(DiagnosticCode.Unsupported_Kd, default);
            return;
        }

        if (this.LayOut() is not { } abi)
        {
            return;
        }

        _ = this.cases.Select(body, collector.Set.Items[item].View); // X13: the collector's one view of the item's body.
        (this.abi, this.cursor, this.point) = (abi, 0, 0);
        this.reach.Clear(body.Blocks.Length);
        this.reach.Walk(body, 0, checks: false);
        this.Copies();
        this.Prologue();
        for (var block = 0; block < body.Blocks.Length; block++)
        {
            if (this.reach[block])
            {
                this.Block(block);
            }
        }

        output.Write("}\n");
    }

    private void Block(int index)
    {
        var block = this.body.Blocks[this.at = index];
        this.output.Write('b');
        WriteNumber(this.output, index);
        this.output.Write(":\n");
        var statements = this.body.Statements.Slice(block.Start, block.Count);
        for (var i = 0; i < statements.Length; i++)
        {
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
                this.Call(terminator);
                return;
            case MirTerminatorKind.Drop:
                this.Drop(terminator);
                return;
            default:
                throw new InvalidOperationException($"The MIR terminator kind {terminator.Kind} has no code generation row.");
        }
    }

    // A terminator's transfer to its one runtime successor: its location's flag points (FunctionCodegen.Flags), then the branch.
    private void Branch(int target)
    {
        this.Points(new(this.at, this.body.Blocks[this.at].Count));
        this.output.Write("  br ");
        this.WriteLabel(target);
        this.output.Write('\n');
    }

    // A SwitchInt on its operand's stored value (bool as i8, I10): each value-target pair a case, otherwise the target; its location's flag
    // points, which hold on every successor, before it.
    private void Switch(in MirTerminator terminator)
    {
        var operand = terminator.Operand;
        if (!this.Operand(operand, out var value))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, terminator.Source);
            return;
        }

        if (this.LoweringOf(operand) is not { } lowering)
        {
            return; // A broken contract, recorded.
        }

        this.Points(new(this.at, this.body.Blocks[this.at].Count));
        var (output, type, width) = (this.output, lowering.Layout.StorageType, 8 * lowering.Layout.Size);
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
