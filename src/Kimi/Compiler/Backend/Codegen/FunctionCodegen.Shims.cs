// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The shims of FunctionCodegen (R5 plans C8f, C9b and C3c, PD3, K package 4.3; SPEC 11.9.2, 12.4.3, 13.3.3, 13.4, 13.4.1, 22.5.1, 22.10.1;
// DECISIONS R5-M126, R5-M130, R5-M141): a Builtin item's function under its requirement's signature in its instance (ModuleCodegen.Shim: the
// admitting call's substitution, a caller-location function), which a Requirement call, a requirement Item's Value call and its adapter
// entry call (FunctionCodegen.Calls' Witness). Its operands are ref/Self addresses, read in place; a comparison acquires, destroys and
// reports nothing. By its function's compiler function, one of the Shim disposition's closed kinds (PD21), as the collector's shim targets:
// - BuiltinEquals and BuiltinCompare: TyCtxt.Comparison of its Contract (TyCtxt.Contract) on its Self under the Contract's mapping, the
//   collector's node, so the parts that are not Builtin are the item's targets in order. A Tuple's parts left to right at their Field
//   offsets, each but the last deciding when it is not equal (equals' false, compare's non-zero i32; SPEC 13.4.1), its value then
//   returned; a ref layer's referent; a Builtin part through the operator table's column of its Type (Leaf); a Witness leaf or a Tuple
//   part its target's call (Witness: a body, its virtual slot included, or the part's own shim) on the two part addresses, so a DAG of
//   Types is one shim per distinct Tuple (DECISIONS R5-M164).
// - BuiltinArithmetic: its Contract's MIR operator (BuiltinArithmetic, SPEC 13.3.3) through the operator table's row, as a statement's, on
//   the loaded operands with its check, which Aborts at the location the shim received (SPEC 22.5.1).
// - BuiltinFormat: the built-in formatting witness, its parameters forwarded to P1d's BuiltinFormat routine with its Self's kind (Format).
// A node the collector refuses (Requirement, None, no tree) is its record at the item's admission, which stops generation; an arithmetic
// operation the capability table refuses is the collector's record at each requesting site, whose edge admits no item.
// A PropertyWitness item's function (C3c, SPEC 11.9.2; DECISIONS R5-M141) is a standard property witness under its key's signature
// (InstanceCollector.StorageSignature, ModuleCodegen.Shim), caller-location as every shim: its Field's address in the layer that stores it,
// a Set's replacement of the value there through the item's target, the Field Type's glue, at the location it received, then the row's
// result: the value (Copy), the address (Borrow) or none (Set) (Storage). It has no MIR body (PD3).
internal sealed partial class FunctionCodegen
{
    private int leaf; // The shim's next part target.

    /// <summary>Writes the function of a shim item, Builtin or PropertyWitness, or records why not.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The Builtin or PropertyWitness item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteShim(InstanceCollector collector, int item, TextWriter output)
    {
        (this.collector, this.item, this.output, this.next, this.leaf) = (collector, item, output, 0, 0);
        var key = collector.Set.Items[item].Key;
        var self = collector.Set.Items[item].RepresentativeType!;
        if (this.module.Shim(item) is not { } abi)
        {
            return; // Recorded at the item's site.
        }

        this.abi = abi;
        output.Write(abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        if (key.Kind == MonoKind.PropertyWitness)
        {
            this.Storage(key.Symbol, (PropertyWitnessKind)key.Variant, self);
            return;
        }

        var function = key.Symbol!;
        var contract = TyCtxt.Contract(function);
        switch (function.CompilerFunction)
        {
            case CompilerFunctionKind.BuiltinFormat:
                this.Format(self);
                return;
            case CompilerFunctionKind.BuiltinArithmetic:
                this.Returned(this.Arithmetic(contract, self));
                return;
            default:
                var tree = this.typeContext.Comparison(contract, self, operators: false);
                this.Returned(this.Compare(tree, contract.LibraryDeclaration, new('p', 0), new('p', 1)));
                return;
        }
    }

    // SPEC 22.10.1-22.10.2 (DECISIONS R5-M130): a BuiltinFormat shim is the built-in formatting witness of its Self: its own parameters
    // (format's value at logical index 0, the writer at 1, the received location) forwarded to P1d's BuiltinFormat routine (Forward) with the
    // formatting runtime's kind of its Self (LegacyRuntimeHelpers.FormatKind; the collector admits a built-in formattable Self only, so another
    // is a broken contract), the function then ended (End).
    private void Format(BoundType self)
    {
        var row = LegacyRuntimeHelpers.FormattingRow(CompilerFunctionKind.BuiltinFormat)!;
        if (LegacyRuntimeHelpers.FormatKind(self) is not (>= 0 and var kind))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default);
            return;
        }

        this.module.Request(row.Needs);
        this.slotless[0] = new('\0', kind);
        if (this.Forward(row.Abi, this.slotless.AsSpan(0, 1), null, out var result))
        {
            this.End(row.Abi, result);
        }
    }

    // A comparison's or an arithmetic operation's value returned (ReturnValue) and the function ended; nothing after a record (no value).
    private void Returned(Value? value)
    {
        if (value is { } result)
        {
            this.ReturnValue(result);
            this.output.Write("}\n");
        }
    }

    // SPEC 13.4: the comparison of the values at two addresses by the shim's Self's node under Equatable or Comparable: equals' i1 or
    // compare's i32; null after a record. A Tuple's parts (Parts) or a reference layer's referent (Part); a Builtin Self natively.
    private Value? Compare(ComparisonNode? node, KimiDeclarationId? contract, Value left, Value right)
    {
        switch (node?.Kind)
        {
            case ComparisonKind.Composite when node.Type.Kind == BoundTypeKind.Tuple:
                return this.Parts(node, contract, left, right);
            case ComparisonKind.Composite:
                return this.Part(node.Parts[0], contract, this.LoadAt(left, PointerLayout), this.LoadAt(right, PointerLayout)); // A referent.
            case ComparisonKind.Builtin:
                return this.Leaf(node.Type, contract, left, right);
            default:
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector records every other node at the item's admission.
                return null;
        }
    }

    // SPEC 13.4: a Tuple's parts left to right at their Field offsets in its record (the shim's Self, laid out at its admission); each but the
    // last returns its value from the shim when it is not equal (Decide), and the last one's value is the Tuple's.
    private Value? Parts(ComparisonNode node, KimiDeclarationId? contract, Value left, Value right)
    {
        if (this.Laid(node.Type) is not { } tuple)
        {
            return null; // Recorded.
        }

        var offsets = tuple.Layout.FieldOffsets.Span;
        Value? value = null;
        for (var i = 0; i < node.Parts.Length; i++)
        {
            if (value is { } previous)
            {
                this.Decide(previous, contract);
            }

            if ((value = this.Part(node.Parts[i], contract, this.Offset(left, offsets[i]), this.Offset(right, offsets[i]))) is null)
            {
                return null; // Recorded.
            }
        }

        return value;
    }

    // A part of the shim's Self: a Builtin one natively (Leaf), a reference layer's referent loaded, any other the call of the shim's next
    // target (Called): a Witness leaf's body, a Tuple part's own shim (DECISIONS R5-M164), so the shim's text is linear in its Self's parts.
    private Value? Part(ComparisonNode node, KimiDeclarationId? contract, Value left, Value right) => node switch
    {
        { Kind: ComparisonKind.Builtin } => this.Leaf(node.Type, contract, left, right),
        { Kind: ComparisonKind.Composite, Type.Kind: not BoundTypeKind.Tuple }
            => this.Part(node.Parts[0], contract, this.LoadAt(left, PointerLayout), this.LoadAt(right, PointerLayout)),
        _ => this.Called(left, right),
    };

    // SPEC 13.4.1: a part that is not equal decides the comparison, its value returned from the shim (equals' false, compare's non-zero i32,
    // which is tested against 0); an equal one goes on to the next part.
    private void Decide(Value value, KimiDeclarationId? contract)
    {
        var equal = contract == KimiDeclarationId.Equatable ? value : this.Compared("eq", "i32", value, "0");
        var id = this.next++;
        var output = this.output;
        output.Write("  br i1 ");
        this.WriteValue(equal);
        Name(output, ", label %same", id);
        Name(output, ", label %decided", id);
        output.Write('\n');
        Name(output, "decided", id);
        output.Write(":\n");
        this.ReturnValue(value);
        Name(output, "same", id);
        output.Write(":\n");
    }

    // SPEC 13.4.1, 12.4.3: a Builtin leaf through the operator table's column of its Type. A string's: its row's routine on the records'
    // bytes (equals' StringEqual compared with 0 by Eq's entry, compare's StringCompare i32 itself). Any other: its loaded values; equals'
    // Eq, or both values unequal to themselves (Ne), which only a NaN is (SPEC 13.4), so all NaNs are one value and +0.0 == -0.0 by Eq;
    // compare's Gt less Lt as an i32 (zero-sized operands are Apply's: the row's Reflexive). Null after a record.
    private Value? Leaf(BoundType type, KimiDeclarationId? contract, Value left, Value right)
    {
        var column = Column(type);
        var equality = contract == KimiDeclarationId.Equatable;
        if (column == StringColumn)
        {
            var row = Rows[(int)(equality ? MirBinaryOp.Eq : MirBinaryOp.Lt)];
            var order = this.CompareBytes(row.Routine!, this.Record(left), this.Record(right));
            return equality ? this.Compared(row.Entries[StringColumn].Operator!, "i32", order, "0") : order;
        }

        if (this.Laid(type) is not { } lowering)
        {
            return null; // Recorded.
        }

        var a = this.Loaded(type, lowering, left);
        var b = this.Loaded(type, lowering, right);
        if (!equality)
        {
            return this.Apply((int)MirBinaryOp.Gt, column, a, b, proven: false, default) is { } greater &&
                this.Apply((int)MirBinaryOp.Lt, column, a, b, proven: false, default) is { } less
                ? this.Combine("sub", "i32", this.Convert("zext ", "i1", greater, "i32"), this.Convert("zext ", "i1", less, "i32")) : null;
        }

        return this.Apply((int)MirBinaryOp.Eq, column, a, b, proven: false, default) is { } same &&
            this.Apply((int)MirBinaryOp.Ne, column, a, a, proven: false, default) is { } leftNaN &&
            this.Apply((int)MirBinaryOp.Ne, column, b, b, proven: false, default) is { } rightNaN
            ? this.Combine("or", "i1", same, this.Combine("and", "i1", leftNaN, rightNaN)) : null;
    }

    // A part's call: the shim's next target, a witness's body (BodyRun, through its virtual slot when it implements one; G157 (28)) or a
    // part's shim (Witness, the one target rule), called (CallWitness) with the two part addresses at its logical inputs 0 and 1, the shared
    // borrows its requirement takes (SPEC 13.4.1), and with the location the shim received at a caller-location function's location
    // parameters; its value. Null after a record.
    private Value? Called(Value left, Value right)
    {
        var targets = this.collector.Set.Targets(this.item);
        if (this.leaf >= targets.Length || this.Witness(targets[this.leaf++].Item, default) is not { Signature: var callee } run)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets each part in order (or recorded).
            return null;
        }

        if (callee.Parameters.Length != 2 + (callee.CallerLocation ? 2 : 0) || callee.Result is not ("i1" or "i32"))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A witness's signature is its requirement's (SPEC 8.6.3).
            return null;
        }

        _ = this.Location(default, out var site); // The shim's own location parameters (a caller-location function).
        return this.CallWitness(run, [left, right], null, site, out var value) ? value : null;
    }

    // SPEC 13.3.3: the built-in operation of an arithmetic shim's Contract, its MIR operator (BuiltinArithmetic) through the operator
    // table's row (RowOf), on its loaded operands (a unary one's on both sides), with its check, which Aborts at the location the shim
    // received (Apply, Location).
    private Value? Arithmetic(BindingSymbol contract, BoundType self)
    {
        if (this.Laid(self) is not { } lowering)
        {
            return null; // A broken contract, recorded.
        }

        var (kind, op) = BuiltinArithmetic.Operation(contract.LibraryDeclaration);
        var left = this.Loaded(self, lowering, new('p', 0));
        var right = kind == MirRvalueKind.BinaryOp ? this.Loaded(self, lowering, new('p', 1)) : left;
        return this.Apply(RowOf(kind, op), Column(self), left, right, proven: false, default);
    }

    // SPEC 11.9.2 (DECISIONS R5-M141): a standard property witness, one rule over its operation's three rows. The Field's address A is the
    // receiver (input 0) plus the Field's offset in the layer that stores it (AdtDef.FieldOwner, that layer's Layout; the complete base is
    // a prefix, X1); a Set first replaces the value at A (Replace); the row gives only its result's form: the value at A (Copy), A itself
    // (Borrow) or none (Set), returned by the signature. A Field the representative Self does not store is a broken contract.
    private void Storage(BindingSymbol? field, PropertyWitnessKind access, BoundType self)
    {
        if (AdtDef.FieldOwner(self, field) is not { } owner || AdtDef.FieldType(owner.Layer, owner.Index) is not { } stored)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector admitted the item on the Field (Agrees).
            return;
        }

        var type = this.collector.TypeOf(this.item, stored);
        if (this.Laid(this.collector.TypeOf(this.item, owner.Layer)) is not { } record || this.Laid(type) is not { } lowering)
        {
            return; // Recorded: the collector laid out the representative Self (DECISIONS R5-M44).
        }

        var address = this.Offset(new('p', this.Input(0)), record.Layout.FieldOffsets.Span[owner.Index]);
        if (access == PropertyWitnessKind.StorageSet && !this.Replace(type!, lowering, address))
        {
            return; // Recorded.
        }

        switch (access)
        {
            case PropertyWitnessKind.StorageCopy:
                this.Give(type!, lowering, address);
                return;
            case PropertyWitnessKind.StorageBorrow:
                this.Returned(address);
                return;
            default:
                this.output.Write("  ret void\n}\n");
                return;
        }
    }

    // SPEC 13.7.1, 16.2: a Set's replacement of the value at A: the old value destroyed by the item's target, its Field Type's glue, when
    // that Type may need a drop (MirTypes.MayNeedDrop, the rule the collector admits the target by), at the location the function received,
    // the invoking assignment's, as a direct assignment's (SPEC 22.5.1); then the input (1) put at A in its representation, a pointer-passed
    // one from its address; a zero-sized input has no parameter. False after a record.
    private bool Replace(BoundType type, ValueLowering lowering, Value address)
    {
        if (MirTypes.MayNeedDrop(type, this.seen))
        {
            var targets = this.collector.Set.Targets(this.item);
            if (targets.Length == 0)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets a Set's Field glue (package C 3.12).
                return false;
            }

            _ = this.Location(default, out var site); // The function's own location parameters (a caller-location function).
            this.Destroy(this.module.Glue(targets[0].Item), address, site);
        }

        if (this.Input(1) is >= 0 and var input)
        {
            Value value = new('p', input);
            var passed = this.abi.Parameters[input].Kind == AbiParameterKind.OwnedSlot ? this.ValueAt(value, type, lowering) : this.Widen(lowering, value);
            this.Put(address, type, lowering, passed);
        }

        return true;
    }

    // IMPL 21.3.1: the value of a Type at an address returned by the signature: put into its result slot, nothing for a zero-sized one, else
    // loaded and returned in its computation representation.
    private void Give(BoundType type, ValueLowering lowering, Value address)
    {
        if (this.abi.ResultSlot)
        {
            this.Put(new('p', this.Input(-1)), type, lowering, this.ValueAt(address, type, lowering));
        }
        else if (lowering.Layout.Size != 0)
        {
            this.Returned(this.Narrow(lowering, this.LoadAt(address, lowering.Layout)));
            return;
        }

        this.output.Write("  ret void\n}\n");
    }

    // The function's parameter that passes an input (Abi.Of numbers its argument parameters by slot), or its result slot for -1; -1 for
    // none, a zero-sized input.
    private int Input(int slot)
    {
        var parameters = this.abi.Parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].LogicalIndex == slot && (slot >= 0 || parameters[i].Kind == AbiParameterKind.ResultSlot))
            {
                return i;
            }
        }

        return -1;
    }

    // A value of a Type at an address in its computation representation (bool narrowed), nothing for a zero-sized one.
    private Computed Loaded(BoundType type, ValueLowering lowering, Value address)
        => new(lowering.Layout.Size == 0 ? default : this.Narrow(lowering, this.LoadAt(address, lowering.Layout)), type, lowering);

    // `%t<n> = <operation> <type> <left>, <right>`, through LlvmScalarText: its value.
    private Value Combine(string operation, string type, Value left, Value right)
    {
        var result = new Value('t', this.next++);
        Span<char> text = stackalloc char[3 * SpellingLength];
        var a = Spell(left, text[..SpellingLength]);
        var b = Spell(right, text.Slice(SpellingLength, SpellingLength));
        LlvmScalarText.WriteOperation(this.output, new ScalarOperation(operation, type, a, b, Spell(result, text[(2 * SpellingLength)..]), comparison: false));
        return result;
    }

    // `%t<n> = icmp <predicate> <type> <value>, <constant>`: its value.
    private Value Compared(string predicate, string type, Value value, string constant)
    {
        var result = new Value('t', this.next++);
        Span<char> text = stackalloc char[2 * SpellingLength];
        var operand = Spell(value, text[..SpellingLength]);
        var name = Spell(result, text[SpellingLength..]);
        LlvmScalarText.WriteOperation(this.output, new ScalarOperation(predicate, type, operand, constant, name, comparison: true));
        return result;
    }

    // `ret <result type> <value>` of the function being written.
    private void ReturnValue(Value value)
    {
        this.output.Write("  ret ");
        this.output.Write(this.abi.Result);
        this.output.Write(' ');
        this.WriteValue(value);
        this.output.Write('\n');
    }
}
