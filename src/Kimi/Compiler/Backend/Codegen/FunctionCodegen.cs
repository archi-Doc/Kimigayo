// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// FunctionCodegen (R5 plan C0b1-C0c, C1a, C1b, C2a, C2b, C3a, C8a, package C 3.2 and 3.9, design 2 2.2; DECISIONS R5-M65, R5-M73, R5-M76,
// R5-M78, R5-M80, R5-M83, R5-M87, R5-M89, R5-M92), MIR code generation: the LLVM definition of one body item of the checked set from its validated,
// analyzed MIR body in the item's instance, and the coverage rows (PD11) of the MIR forms: a form the frame does not cover yet is one
// located Unsupported_Kd, never wrong code.
// Inputs: the item and its body; its Types (InstanceCollector.TypeOf), its view (CheckedSet's View, selected once with its own MirCases,
// X13), its records' location rule (InstanceCollector.Subject, K package 4.9) and targets; the program's drop table and analysis problems
// (A2, R4-M6a); the compilation's Layout and runtime location texts; ModuleCodegen's signatures (Signature and Import: Abi.Of under the
// internal and the C convention, IMPL 21.3.1), constant pool and record list.
// Output: the definition text appended to the module's buffer; records through ModuleCodegen.Report.
// Invariants (design 2 2.2): I1 deterministic: blocks b<index>, slots %l<local>, values %t<n> numbered per function; I2 one aligned alloca
// per local of nonzero size in the entry block, bool stored as i8 (IMPL 21.1.3), none for a zero-sized local, a Value argument stored into
// its local's slot, a pointer-passed argument and the result slot used in place, then one per copy slot (R5-M89); I3 exactly the blocks
// MirReach.Walk(body, 0, checks: false) reaches, in index order, so code only the checks reach is never generated (SPEC 14.10.3); I4 an
// operand is read at each use (loaded, or its address when its value is in memory); I5 a check not Proven Aborts at its site's location
// text (SPEC 22.5.1); I7 every Type is Layout.Of(TypeOf(item, t)); I8 a call's target is the collector's at its site, read by one forward
// cursor; I9 no nsw, nuw or inbounds; I10 no store i1. Covered: StorageLive, StorageDead, FakeRead and EndBorrow (nothing is generated); an
// Assign into a Place the place writer locates (FunctionCodegen.Places: a local's home, a zero-sized one's the module's zero-sized symbol,
// and the projections, an Index with its bounds check), after its rvalue, each value put in its representation (a value in memory, a slot
// Type other than the string's record, copied by memcpy; nothing for a zero-sized one): of a Use, from a Copy, Move or PairAcquire of such
// a Place (a PairAcquire's two cases write the same bits) or a Bool, Integer, Float or Unit constant or a raw pointer's null, or of a
// string constant to a string Place (its UTF-8 bytes a pooled constant, Static, IMPL 21.1.3), or of a BinaryOp or UnaryOp on integer,
// wrapping integer, floating-point, bool, char and Unit operands and a raw pointer's == and != and displacement by the operator table
// (FunctionCodegen.Arithmetic; R5-M92), or of a numeric, wrapping, bit or raw pointer Cast by the conversion table (FunctionCodegen.Casts),
// or of a Ref, or of a Tuple, struct, enum Case or fixed-array Aggregate of such operands, or of a Fill of one, a counted loop over its
// instance's length, or of a Discriminant, of a Len of a fixed array, Array or Slice, or of an Adapt by the rvalue the item's view selects
// (FunctionCodegen.Rvalues and .Aggregates; R5-M78, R5-M83, R4-M81, R5-M87); Goto, SwitchInt, Return and Unreachable; a Call of the console
// writeLine, a compiler function's legacy runtime row (RuntimeAbi.WriteLine) given the call site's location text (SPEC 22.5.1), of a
// function's instance under its signature or of a #LibraryImport function under its C signature, each argument by its parameter, a Copy of
// a value in memory and a string constant through a copy slot, the result slot the destination, unreachable after a call without a target
// (FunctionCodegen.Calls; R5-M89), of an intrinsic by its row's writer (FunctionCodegen.Intrinsics: Raw's storage operations, the byte
// copy, the nullable Alloc, $abort and Fail; R5-M92); a DefaultTemplate body, its prepared inputs in slot order (OrderOf); a Drop's Keep
// and Elide verdicts (FunctionCodegen.Drops.cs; R5-M78). Rows not written yet: Closure (C5a) and StaticInit (C7b) bodies; statics (C7b); a
// handle's or object borrow's Deref (C6a); Closure aggregates (C5a), an Array or Dictionary from none (C8d, C8e), a Dictionary's (C8e) and
// a string's (C8c) Len, an Adapt's object creation (C6a); upcasts (C6a) and erasures (C5b); string operations (C8c), checked 128-bit
// arithmetic (ModuleCodegen's wide overflow declarations); FnItem (C5a) and other string constants (C8c), Location and TypeToken constants
// being intrinsic operands (C10a's TestObserve; C6a's TypeTest, C9a); calls of a requirement or a caller-location callee (C3b), a value
// (C5a), a virtual slot (C6c), an intrinsic of a later row (its IntrinsicRows row names the unit), a Builtin shim (C8f, C9b), a property
// witness (C3c) or another legacy row (C8b-C8e, C9a); the locations of a caller-location body, which forwards its own (C3b); a Drop's Flag
// (C4b) and Open (C4c) verdicts. A warm write allocates nothing. Files: the dispatch (this file); the function's frame: its kind's argument
// order, homes, copy slots, prologue and return (FunctionCodegen.Frame.cs); the place writer and the loads, stores and copies through it
// (.Places.cs); the rvalues (.Rvalues.cs) and aggregates (.Aggregates.cs), operators (.Arithmetic.cs), conversions (.Casts.cs), calls
// (.Calls.cs), intrinsics (.Intrinsics.cs) and drops (.Drops.cs).
// Must not read syntax, HIR, plans, Binding internals or the old Analysis/Emission, re-derive an acceptance fact or a case selection
// (MirCases decides a PairLayer under the item's view), compose a Subst or a Type (TypeOf does), resolve a callee, or use LINQ, Supports*,
// out-string failures or catch-alls. An Opaque value (the gate admits none), a Type TypeOf cannot make and a Layout outcome other than
// Ok are broken contracts: InternalInvariant_Kd.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>Writes the LLVM definition of one body item from its MIR (R5 package C 3.2).</summary>
internal sealed partial class FunctionCodegen
{
    private readonly ModuleCodegen module;
    private readonly MirReach reach = new();
    private readonly MirCases cases; // The item's case view (X13).
    private Slot[] slots = []; // By local.
    private BoundType[] types = []; // By local, in the item's instance.
    private Value[] arguments = []; // A runtime call's arguments in parameter order, or a fixed-array aggregate's element values.
    private InstanceCollector collector = null!;
    private FunctionAbi abi = null!; // The item's signature.
    private MirBody body = null!;
    private TextWriter output = TextWriter.Null;
    private int item;
    private int next; // The number of the next value %t<n>.

    /// <summary>Initializes a new instance of the <see cref="FunctionCodegen"/> class.</summary>
    /// <param name="module">The module whose signatures and records it uses.</param>
    /// <param name="types">The Type context of the program, whose case Types the item's view selects.</param>
    internal FunctionCodegen(ModuleCodegen module, TyCtxt types) => (this.module, this.cases) = (module, new(types));

    /// <summary>Writes the definition of a body item, or records why not.</summary>
    /// <param name="program">The program, whose body the item replays and whose drop table and problems its drops read.</param>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void Write(MirProgram program, InstanceCollector collector, int item, TextWriter output)
    {
        var body = program.Bodies[collector.Set.Items[item].BodyOrdinal];
        (this.program, this.collector, this.item, this.body, this.output, this.next) = (program, collector, item, body, output, 0);
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
        (this.abi, this.cursor) = (abi, 0);
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

    // An integer's spelling at its width: the signed value of its low bits (two's complement, IMPL 21.1.3), so an unsigned value above the
    // signed range is the negative number with the same bits.
    private static Int128 Signed(Int128 bits, int width) => width >= 128 ? bits : (bits << (128 - width)) >> (128 - width);

    private void Block(int index)
    {
        var block = this.body.Blocks[this.at = index];
        this.output.Write('b');
        WriteNumber(this.output, index);
        this.output.Write(":\n");
        foreach (var statement in this.body.Statements.Slice(block.Start, block.Count))
        {
            this.Statement(statement);
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
                return; // Nothing is generated (package C 3.9); A2's flag points at them come with C4b.
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

    // An operand's value in its Type's representation (Read; a value in memory is its address); false for a form not covered yet. A
    // zero-sized operand has no value. A PairAcquire Copies its Place or Reborrows it, as the item's view decides (SPEC 8.14), and both
    // write the Place's bits in its case Type: a Reborrow of an exclusive reference is the pointer the Place holds (IMPL 21.1.3), so it
    // needs no case decision here (DECISIONS R5-M87).
    private bool Operand(in MirOperand operand, out Value value)
    {
        value = default;
        switch (operand.Kind)
        {
            case MirOperandKind.Copy or MirOperandKind.Move or MirOperandKind.PairAcquire:
                return this.Read(operand.Place, out value);
            case MirOperandKind.Const:
                return this.Constant(this.body.Consts[operand.Arg], out value);
            default:
                throw new InvalidOperationException($"The MIR operand kind {operand.Kind} has no code generation row.");
        }
    }

    // A constant of a covered kind: Bool (stored 0 or 1), Integer of an integer or char Type or a raw pointer's null (SPEC 5.1), Float (its
    // IEEE bits), Unit (zero-sized).
    private bool Constant(in MirConst constant, out Value value)
    {
        value = default;
        if (constant.Kind is not (MirConstKind.Bool or MirConstKind.Integer or MirConstKind.Float or MirConstKind.Unit))
        {
            return false; // String other than into a string local (C8c), FnItem (C5a), Location (C10a), TypeToken (C6a, C9a).
        }

        if (this.Representation(constant.Type) is not { } lowering)
        {
            return true; // A broken contract, recorded.
        }

        if (constant.Kind == MirConstKind.Float)
        {
            value = new('x', Hexadecimal(constant.Bits, lowering.Layout.Size));
            return true;
        }

        var type = this.collector.TypeOf(this.item, constant.Type)!;
        if (constant.Kind == MirConstKind.Integer && type.Semantics == SemanticsKind.Raw)
        {
            value = new('n', 0); // SPEC 5.1: null, the one raw pointer constant.
            return true;
        }

        if (constant.Kind == MirConstKind.Integer && !type.HasIntegerArithmetic && !ReferenceEquals(type, BoundType.Char))
        {
            return false; // A later row's Type.
        }

        value = lowering.Layout.Size == 0 ? default : new('\0', Signed(constant.Bits, 8 * lowering.Layout.Size));
        return true;
    }

    // The representation of a Type of the body in the item's instance (I7): Laid of its instance Type.
    private ValueLowering? Representation(BoundType type) => this.Laid(this.collector.TypeOf(this.item, type));

    // The representation of a Type in the item's instance (I7); null, with InternalInvariant_Kd at the body, where TypeOf made none or Layout
    // has none (the collector laid out every Type of an admitted item).
    private ValueLowering? Laid(BoundType? closed)
    {
        if (closed is not null && this.module.Layout.Of(closed).Value is { } lowering)
        {
            return lowering;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, default);
        return null;
    }

    // The representation of a covered operand's Type: its Place's, or a constant's in the item's instance (null after a record).
    private ValueLowering? LoweringOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.Representation(this.body.Consts[operand.Arg].Type) : this.LoweringOf(operand.Place);

    private void Terminator(in MirTerminator terminator)
    {
        switch (terminator.Kind)
        {
            case MirTerminatorKind.Goto:
                this.output.Write("  br ");
                this.WriteLabel(terminator.Target);
                this.output.Write('\n');
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

    // SPEC 22.5.1 (I5): the location a runtime routine or an Abort at a site of the body reports, the site's text as a pooled Location
    // constant; -1 after a record in a caller-location body, which forwards its caller's location (C3b), or at a site without its source
    // document, a broken contract.
    private int Location(in SourceInfo source)
    {
        if (this.abi.CallerLocation)
        {
            this.Report(DiagnosticCode.Unsupported_Kd, source);
            return -1;
        }

        if (this.module.Locations.Text(this.body.Subject(source)) is not { } text)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // A site of a body has its source document.
            return -1;
        }

        return this.module.Constants.Intern(text, LlvmConstantKind.Location);
    }

    // A SwitchInt on its operand's stored value (bool as i8, I10): each value-target pair a case, otherwise the target.
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

    // IMPL 21.1.3: bool, the one Type whose computation (i1) and storage (i8) representations differ, widens to be stored and narrows to
    // be computed; every other value is the same in both.
    private Value Widen(ValueLowering lowering, Value value) => this.Convert("zext ", lowering.ComputationType, value, lowering.Layout.StorageType);

    private Value Narrow(ValueLowering lowering, Value value) => this.Convert("trunc ", lowering.Layout.StorageType, value, lowering.ComputationType);

    private Value Convert(string operation, string from, Value value, string to)
    {
        if (from == to)
        {
            return value;
        }

        var converted = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(converted);
        this.output.Write(" = ");
        this.output.Write(operation);
        this.output.Write(from);
        this.output.Write(' ');
        this.WriteValue(value);
        this.output.Write(" to ");
        this.output.Write(to);
        this.output.Write('\n');
        return converted;
    }

    // A branch target: the label of a block, b<index> (I1).
    private void WriteLabel(int block)
    {
        this.output.Write("label %b");
        WriteNumber(this.output, block);
    }

    private void WriteValue(Value value)
    {
        if (value.Prefix == '@')
        {
            this.output.Write('@');
            this.output.Write(this.module.Constants[(int)value.Number].Name);
            return;
        }

        if (value.Prefix == 'h')
        {
            this.WriteHome((int)value.Number);
            return;
        }

        this.output.Write(Spell(value, stackalloc char[SpellingLength]));
    }

    // A record at a site of the body, located by the collector's rule (K package 4.9).
    private void Report(DiagnosticCode code, in SourceInfo source) => this.module.Report(code, this.collector.Subject(this.item, this.body, source));

    // A value of the text: %t<n> (Prefix t), the argument %a<n> (Prefix a), the copy slot %s<n> (Prefix s; never c, a check's success
    // label), the pooled constant @<name> of index Number (Prefix @), the home of local Number, its address (Prefix h), the floating
    // constant whose double bits are Number (Prefix x, LLVM's hexadecimal spelling), the null pointer (Prefix n) or the literal integer
    // Number (Prefix NUL).
    private readonly record struct Value(char Prefix, Int128 Number);
}
