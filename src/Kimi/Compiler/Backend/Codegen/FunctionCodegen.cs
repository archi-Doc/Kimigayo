// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// FunctionCodegen (R5 plan C0b1, C0b2, C1a, C1b, package C 3.2 and 3.9, design 2 2.2; DECISIONS R5-M65, R5-M73, R5-M76, R5-M80), MIR code
// generation: the LLVM definition of one body item of the checked set from its validated, analyzed MIR body in the item's instance, and the
// coverage rows (PD11) of the MIR forms: a form the frame does not cover yet is one located Unsupported_Kd, never wrong code.
// Inputs: the item and its body; its Types (InstanceCollector.TypeOf) and its records' location rule (InstanceCollector.Subject, K package
// 4.9); the compilation's Layout and runtime location texts; ModuleCodegen's signature table (Abi.Of under the internal convention, IMPL
// 21.3.1), constant pool and record list.
// Output: the definition text appended to the module's buffer; records through ModuleCodegen.Report.
// Invariants (design 2 2.2): I1 deterministic: blocks b<index>, slots %l<local>, values %t<n> numbered per function; I2 one aligned alloca
// per local of nonzero size in the entry block, bool stored as i8 (IMPL 21.1.3), none for a zero-sized local, a Value argument stored
// into its local's slot, a pointer-passed argument and the result slot used in place; I3 exactly the blocks MirReach.Walk(body, 0,
// checks: false) reaches, in index order, so code only the checks reach is never generated (SPEC 14.10.3); I4 an operand is loaded at
// each use; I5 a check not Proven Aborts at its site's location text (SPEC 22.5.1); I7 every Type is Layout.Of(TypeOf(item, t)); I9 no
// nsw, nuw or inbounds; I10 no store i1. Covered: StorageLive, StorageDead, FakeRead and EndBorrow (nothing is generated); an Assign to a
// local without projection of a Use, from a Copy or Move of such a local or a Bool, Integer, Float or Unit constant (a zero-sized value
// writes nothing, a scalar is loaded and stored in its storage representation), or of a string constant to a string local (its UTF-8
// bytes a pooled constant, Static, IMPL 21.1.3), or of a BinaryOp or UnaryOp on integer, wrapping integer, floating-point, bool, char and
// Unit operands by the operator table (FunctionCodegen.Arithmetic), or of a numeric, wrapping, bit or raw pointer Cast by the conversion
// table (FunctionCodegen.Casts); Goto, SwitchInt, Return and Unreachable; a Call of the console writeLine, a compiler function's legacy
// runtime row (RuntimeAbi.WriteLine) given the call site's location text (SPEC 22.5.1). Rows not written yet: Closure (C5a),
// DefaultTemplate (C3a) and StaticInit (C7b) bodies; projections (C2a) and statics (C7b); Ref, Aggregate, Discriminant, Len and Adapt,
// PairAcquire and copies of slot Types (C2b); upcasts (C6a) and erasures (C5b); operators on raw-pointer (C8a) operands and string
// operations (C8c), checked 128-bit arithmetic (ModuleCodegen's wide overflow declarations); FnItem (C5a), Location (C3b), TypeToken (C3a)
// and raw-pointer constants (C8a); calls of a function's instance (C3a), a requirement (C3b), a value (C5a), a virtual slot (C6c), an
// intrinsic (C8a, C10a) or another legacy row (C8b-C8e, C9a); the locations of a caller-location body, which forwards its own (C3b); Drop
// (C4a). A warm write allocates nothing.
// Must not read syntax, HIR, plans, Binding internals or the old Analysis/Emission, re-derive an acceptance fact, compose a Subst or a
// Type (TypeOf does), resolve a callee, select a Semantics view before a covered form depends on it (C2a), or use LINQ, Supports*,
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
    private Slot[] slots = []; // By local.
    private BoundType[] types = []; // By local, in the item's instance.
    private BoundType[] parameters = []; // The argument Types in slot order.
    private Value[] arguments = []; // A runtime call's arguments in parameter order.
    private InstanceCollector collector = null!;
    private MirBody body = null!;
    private TextWriter output = TextWriter.Null;
    private int item;
    private int next; // The number of the next value %t<n>.

    /// <summary>Initializes a new instance of the <see cref="FunctionCodegen"/> class.</summary>
    /// <param name="module">The module whose signatures and records it uses.</param>
    internal FunctionCodegen(ModuleCodegen module) => this.module = module;

    // Where a local lives (I2).
    private enum Home : byte
    {
        None, // Zero-sized: no storage.
        Slot, // Its alloca, %l<local>.
        Argument, // The pointer its caller passed, %a<slot>.
        Result, // The result slot, %ret.
    }

    /// <summary>Writes the definition of a body item, or records why not.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The item.</param>
    /// <param name="body">Its MIR body.</param>
    /// <param name="output">The module's buffer.</param>
    internal void Write(InstanceCollector collector, int item, MirBody body, TextWriter output)
    {
        (this.collector, this.item, this.body, this.output, this.next) = (collector, item, body, output, 0);
        switch (body.Key.Kind)
        {
            case MirBodyKind.Fn or MirBodyKind.Getter or MirBodyKind.Setter or MirBodyKind.Test:
                break; // A function of its own signature.
            case MirBodyKind.Closure or MirBodyKind.DefaultTemplate or MirBodyKind.StaticInit:
                // Rows not written yet, at the body: C5a writes a Closure's environment, C3a a template's prepared inputs, and C7b a static's
                // initializer, which a constant one folds into its slot and is never a function (package C 3.3).
                this.Report(DiagnosticCode.Unsupported_Kd, default);
                return;
            default:
                throw new InvalidOperationException($"The MIR body kind {body.Key.Kind} has no code generation row.");
        }

        if (this.LayOut() is not { } abi)
        {
            return;
        }

        this.Prologue(abi);
        this.reach.Clear(body.Blocks.Length);
        this.reach.Walk(body, 0, checks: false);
        for (var block = 0; block < body.Blocks.Length; block++)
        {
            if (this.reach[block])
            {
                this.Block(block, abi);
            }
        }

        output.Write("}\n");
    }

    // An integer's spelling at its width: the signed value of its low bits (two's complement, IMPL 21.1.3), so an unsigned value above the
    // signed range is the negative number with the same bits.
    private static Int128 Signed(Int128 bits, int width) => width >= 128 ? bits : (bits << (128 - width)) >> (128 - width);

    // Whether a Place is a local without projection, the frame's one Place form (C2a projects, C7b roots statics).
    private static bool IsLocal(MirPlace place) => place.Root >= 0 && place.Projection == 0;

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
    private void Prologue(FunctionAbi abi)
    {
        var output = this.output;
        output.Write(abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        foreach (var parameter in abi.Parameters)
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

        foreach (var parameter in abi.Parameters)
        {
            if (parameter.Kind is AbiParameterKind.Value or AbiParameterKind.SharedReference)
            {
                var local = this.LocalOf(parameter);
                this.Store(local, this.Widen(this.slots[local].Value, new('a', parameter.LogicalIndex)));
            }
        }

        output.Write("  br label %b0\n");
    }

    // The local an argument parameter passes: Arg local 1 plus the parameter's position (SPEC 7.3).
    private int LocalOf(in AbiParameter parameter) => 1 + MirCallee.Position(this.body.Key.Decl, parameter.LogicalIndex);

    private void Block(int index, FunctionAbi abi)
    {
        var block = this.body.Blocks[index];
        this.output.Write('b');
        WriteNumber(this.output, index);
        this.output.Write(":\n");
        foreach (var statement in this.body.Statements.Slice(block.Start, block.Count))
        {
            this.Statement(statement, abi);
        }

        this.Terminator(block.Terminator, abi);
    }

    private void Statement(in MirStatement statement, FunctionAbi abi)
    {
        switch (statement.Kind)
        {
            case MirStatementKind.Assign:
                this.Assign(statement, abi);
                return;
            case MirStatementKind.StorageLive or MirStatementKind.StorageDead or MirStatementKind.FakeRead or MirStatementKind.EndBorrow:
                return; // Nothing is generated (package C 3.9); A2's flag points at them come with C4b.
            default:
                throw new InvalidOperationException($"The MIR statement kind {statement.Kind} has no code generation row.");
        }
    }

    // An Assign by its rvalue's row, to a local without projection (C2a projects).
    private void Assign(in MirStatement statement, FunctionAbi abi)
    {
        switch (statement.Rvalue.Kind)
        {
            case MirRvalueKind.Opaque:
                this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source); // The gate passes no program with an Opaque value.
                return;
            case MirRvalueKind.Use when IsLocal(statement.Place):
                this.Use(statement);
                return;
            case MirRvalueKind.BinaryOp or MirRvalueKind.UnaryOp when IsLocal(statement.Place):
                this.Operator(statement, abi);
                return;
            case MirRvalueKind.Cast when IsLocal(statement.Place):
                this.Cast(statement, abi);
                return;
            default:
                this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // Ref, Aggregate, Discriminant, Len and Adapt (C2b).
                return;
        }
    }

    // A Use: its operand in the local's storage representation, nothing for a zero-sized local; a string constant written whole; a copy
    // of a slot Type is C2b's (memcpy).
    private void Use(in MirStatement statement)
    {
        var local = statement.Place.Root;
        var operand = this.body.Operands[statement.Rvalue.Start];
        if (operand.Kind == MirOperandKind.Const && this.body.Consts[operand.Arg] is { Kind: MirConstKind.String, Value: string text } &&
            ReferenceEquals(this.types[local], BoundType.String))
        {
            this.Text(local, text);
            return;
        }

        if (Abi.HasResultSlot(this.types[local], this.slots[local].Value) || !this.Operand(operand, out var value))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        if (this.slots[local].Home != Home.None)
        {
            this.Store(local, value);
        }
    }

    // A string constant stored into a string local (the string representation, implementation-defined by IMPL 21.1.3: RuntimeAbi.String):
    // its UTF-8 bytes a pooled constant, the Static release kind, so never released; the empty string has no bytes (null, length 0).
    private void Text(int local, string text)
    {
        var output = this.output;
        output.Write("  store ");
        output.Write(this.slots[local].Value.Layout.StorageType);
        output.Write(" { ptr ");
        if (text.Length == 0)
        {
            output.Write("null, i64 0");
        }
        else
        {
            var constant = this.module.Constants[this.module.Constants.Intern(text, LlvmConstantKind.Text)];
            output.Write('@');
            output.Write(constant.Name);
            output.Write(", i64 ");
            WriteNumber(output, constant.ByteLength);
        }

        output.Write(", i8 ");
        WriteNumber(output, RuntimeAbi.StaticReleaseKind);
        output.Write(" }");
        this.WriteAccess(local);
    }

    // An operand's value in its Type's storage representation; false for a form not covered yet. A zero-sized operand has no value.
    private bool Operand(in MirOperand operand, out Value value)
    {
        value = default;
        switch (operand.Kind)
        {
            case MirOperandKind.Copy or MirOperandKind.Move when IsLocal(operand.Place):
                if (this.slots[operand.Place.Root].Home != Home.None)
                {
                    value = this.Load(operand.Place.Root);
                }

                return true;
            case MirOperandKind.Const:
                return this.Constant(this.body.Consts[operand.Arg], out value);
            default:
                return false; // A projected Place (C2a), a static (C7b), a PairAcquire (C2b).
        }
    }

    // A constant of a covered kind: Bool (stored 0 or 1), Integer of an integer or char Type, Float (its IEEE bits), Unit (zero-sized).
    private bool Constant(in MirConst constant, out Value value)
    {
        value = default;
        if (constant.Kind is not (MirConstKind.Bool or MirConstKind.Integer or MirConstKind.Float or MirConstKind.Unit))
        {
            return false; // String other than into a string local (C8c), FnItem (C5a), Location (C3b), TypeToken (C3a).
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
        if (constant.Kind == MirConstKind.Integer && !type.HasIntegerArithmetic && !ReferenceEquals(type, BoundType.Char))
        {
            return false; // A raw pointer's null (C8a).
        }

        value = lowering.Layout.Size == 0 ? default : new('\0', Signed(constant.Bits, 8 * lowering.Layout.Size));
        return true;
    }

    // The representation of a Type of the body in the item's instance (I7); null, with InternalInvariant_Kd at the body, where the instance
    // does not lay it out (the collector laid out the item's Types).
    private ValueLowering? Representation(BoundType type)
    {
        if (this.collector.TypeOf(this.item, type) is { } closed && this.module.Layout.Of(closed).Value is { } lowering)
        {
            return lowering;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, default);
        return null;
    }

    // The representation of a covered operand's Type: a local's, or a constant's in the item's instance (null after a record).
    private ValueLowering? LoweringOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.Representation(this.body.Consts[operand.Arg].Type) : this.slots[operand.Place.Root].Value;

    private void Terminator(in MirTerminator terminator, FunctionAbi abi)
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
                this.Return(abi);
                return;
            case MirTerminatorKind.Unreachable:
                this.output.Write("  unreachable\n");
                return;
            case MirTerminatorKind.Call:
                this.Call(terminator, abi);
                return;
            case MirTerminatorKind.Drop:
                this.Report(DiagnosticCode.Unsupported_Kd, terminator.Source); // C4a writes drops.
                return;
            default:
                throw new InvalidOperationException($"The MIR terminator kind {terminator.Kind} has no code generation row.");
        }
    }

    // A Call by its callee's row: a compiler function of the Body disposition (KimiLibraryKinds' published IntrinsicDisposition) by its
    // legacy row; the other rows are not written yet, each at the call.
    private void Call(in MirTerminator call, FunctionAbi abi)
    {
        var callee = call.Callee;
        switch (callee.Kind)
        {
            case MirCalleeKind.Fn when callee.Symbol!.CompilerFunction is not CompilerFunctionKind.None and var kind &&
                IntrinsicDisposition.Of(kind).Kind == DispositionKind.Body:
                this.Legacy(call, abi);
                return;
            case MirCalleeKind.Fn:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C3a calls the collector's target; C8f and C9b the Shim dispositions.
                return;
            case MirCalleeKind.Requirement:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C3b calls the requirement's target.
                return;
            case MirCalleeKind.Value:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C5a calls a Closure or function value.
                return;
            case MirCalleeKind.Virtual:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C6c loads the virtual slot.
                return;
            case MirCalleeKind.Intrinsic:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C8a and C10a write the intrinsic rows.
                return;
            default:
                throw new InvalidOperationException($"The MIR callee kind {callee.Kind} has no code generation row.");
        }
    }

    // A compiler function the backend serves from a legacy runtime row until R8 writes its body (the Body disposition): the console
    // writeLine (RuntimeAbi.WriteLine, SPEC 22.1-22.5.5: it borrows its string); C8b-C8e add theirs, and C9a's formatting rows come from one
    // Backend table beside LegacyRuntimeHelpers.FormattingRow, not from more arms here. The routine takes the call's operands at its
    // parameters' slots and the call site's location (Location); its result is Unit, so the destination takes nothing.
    private void Legacy(in MirTerminator call, FunctionAbi abi)
    {
        var symbol = call.Callee.Symbol!;
        var routine = symbol.CompilerFunction switch
        {
            CompilerFunctionKind.WriteLine => RuntimeAbi.WriteLine,
            _ => null,
        };
        if (routine is null)
        {
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        var constant = this.Location(call.Source, abi);
        if (constant < 0)
        {
            return; // Recorded.
        }

        var parameters = routine.Parameters;
        if (this.arguments.Length < parameters.Length)
        {
            this.arguments = new Value[parameters.Length];
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            switch (parameters[i].Kind)
            {
                case AbiParameterKind.Value or AbiParameterKind.SharedReference:
                    if (!this.Operand(this.body.Operands[call.Start + MirCallee.Position(symbol, parameters[i].LogicalIndex)], out this.arguments[i]))
                    {
                        this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
                        return;
                    }

                    break;
                case AbiParameterKind.Location:
                    this.arguments[i] = new('@', constant);
                    break;
                case AbiParameterKind.LocationLength:
                    this.arguments[i] = new('\0', this.module.Constants[constant].ByteLength);
                    break;
                default:
                    throw new InvalidOperationException($"The runtime parameter kind {parameters[i].Kind} has no argument row.");
            }
        }

        this.WriteCall(routine, this.arguments.AsSpan(0, parameters.Length));
        this.output.Write("  br ");
        this.WriteLabel(call.Target);
        this.output.Write('\n');
    }

    // SPEC 22.5.1 (I5): the location a runtime routine or an Abort at a site of the body reports, the site's text as a pooled Location
    // constant; -1 after a record in a caller-location body, which forwards its caller's location (C3b), or at a site without its source
    // document, a broken contract.
    private int Location(in SourceInfo source, FunctionAbi abi)
    {
        if (abi.CallerLocation)
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

    // A call of a routine without a result value, each argument with its parameter's Type and attributes.
    private void WriteCall(FunctionAbi routine, ReadOnlySpan<Value> arguments)
    {
        var output = this.output;
        output.Write("  call ");
        output.Write(routine.Result);
        output.Write(" @");
        output.Write(routine.Name);
        output.Write('(');
        for (var i = 0; i < arguments.Length; i++)
        {
            output.Write(i == 0 ? string.Empty : ", ");
            output.Write(routine.Parameters[i].Type);
            output.Write(routine.Parameters[i].Attributes);
            output.Write(' ');
            this.WriteValue(arguments[i]);
        }

        output.Write(")\n");
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

    // Return (IMPL 21.3.1): void for a zero-sized result or a result slot, else local 0's value in its computation representation.
    private void Return(FunctionAbi abi)
    {
        if (abi.ResultSlot || this.slots[0].Home == Home.None)
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

    // A local's value, loaded from its home in its storage representation (I4).
    private Value Load(int local)
    {
        var value = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(value);
        this.output.Write(" = load ");
        this.output.Write(this.slots[local].Value.Layout.StorageType);
        this.WriteAccess(local);
        return value;
    }

    // A value in a local's storage representation, stored into its home.
    private void Store(int local, Value value)
    {
        this.output.Write("  store ");
        this.output.Write(this.slots[local].Value.Layout.StorageType);
        this.output.Write(' ');
        this.WriteValue(value);
        this.WriteAccess(local);
    }

    // The end of a load or store of a local: its home and its Layout's alignment.
    private void WriteAccess(int local)
    {
        this.output.Write(", ptr ");
        this.WriteHome(local);
        this.output.Write(", align ");
        WriteNumber(this.output, this.slots[local].Value.Layout.Alignment);
        this.output.Write('\n');
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

        this.output.Write(Spell(value, stackalloc char[SpellingLength]));
    }

    private void WriteHome(int local)
    {
        var slot = this.slots[local];
        switch (slot.Home)
        {
            case Home.Slot:
                this.output.Write("%l");
                WriteNumber(this.output, local);
                return;
            case Home.Argument:
                this.output.Write("%a");
                WriteNumber(this.output, slot.Index);
                return;
            case Home.Result:
                this.output.Write("%ret");
                return;
            default:
                throw new InvalidOperationException("A zero-sized local has no home.");
        }
    }

    // A record at a site of the body, located by the collector's rule (K package 4.9).
    private void Report(DiagnosticCode code, in SourceInfo source) => this.module.Report(code, this.collector.Subject(this.item, this.body, source));

    // A local's home and representation: Index is a pointer-passed argument's slot.
    private readonly record struct Slot(Home Home, int Index, ValueLowering Value);

    // A value of the text: %t<n> (Prefix t), the argument %a<n> (Prefix a), the pooled constant @<name> of index Number (Prefix @), the
    // floating constant whose double bits are Number (Prefix x, LLVM's hexadecimal spelling) or the literal integer Number (Prefix NUL).
    private readonly record struct Value(char Prefix, Int128 Number);
}
