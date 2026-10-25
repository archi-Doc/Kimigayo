// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The Builtin shims of FunctionCodegen (R5 plan C8f, PD3, K package 4.3; SPEC 12.4.3, 13.3.3, 13.4, 13.4.1, 22.5.1; DECISIONS R5-M126): a
// Builtin item's function under its requirement's signature in its instance (ModuleCodegen.Shim: the admitting call's substitution, a
// caller-location function), which a Requirement call, a requirement Item's Value call and its adapter entry call (FunctionCodegen.Calls'
// Witness). Its operands are ref/Self addresses, read in place; a comparison acquires, destroys and reports nothing. By its function's
// compiler function, one of the Shim disposition's closed kinds (PD21), as the collector's shim targets:
// - BuiltinEquals and BuiltinCompare: TyCtxt.Comparison of its Contract (TyCtxt.Contract) on its Self under the Contract's mapping, the
//   collector's tree, so its Witness leaves are the item's targets in order. A Tuple's parts left to right at their Field offsets, each
//   but the last deciding when it is not equal (equals' false, compare's non-zero i32; SPEC 13.4.1), its value then returned; a ref
//   layer's referent; a Builtin leaf through the operator table's column of its Type (Leaf); a Witness leaf its target's call (BodyRun,
//   its virtual slot included) on the two leaf addresses.
// - BuiltinArithmetic: its Contract's MIR operator (BuiltinArithmetic, SPEC 13.3.3) through the operator table's row, as a statement's, on
//   the loaded operands with its check, which Aborts at the location the shim received (SPEC 22.5.1).
// - BuiltinFormat: C9b's row, one located Unsupported_Kd at the item's site.
// A node the collector refuses (Requirement, None, no tree) is its record at the item's admission, which stops generation; an arithmetic
// operation the capability table refuses is the collector's record at each requesting site, whose edge admits no item.
internal sealed partial class FunctionCodegen
{
    private int leaf; // The shim's next Witness leaf target.

    /// <summary>Writes the function of a Builtin item, or records why not.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The Builtin item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteShim(InstanceCollector collector, int item, TextWriter output)
    {
        (this.collector, this.item, this.output, this.next, this.leaf) = (collector, item, output, 0, 0);
        var (function, self) = (collector.Set.Items[item].Key.Symbol!, collector.Set.Items[item].RepresentativeType!);
        if (this.Shim(item, default) is not { } abi)
        {
            return; // Recorded at the item's site.
        }

        this.abi = abi;
        output.Write(abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        var contract = TyCtxt.Contract(function);
        var value = function.CompilerFunction == CompilerFunctionKind.BuiltinArithmetic ? this.Arithmetic(contract, self)
            : this.Compare(this.typeContext.Comparison(contract, self, operators: false), contract.LibraryDeclaration, new('p', 0), new('p', 1));
        if (value is { } result)
        {
            this.ReturnValue(result);
            output.Write("}\n");
        }
    }

    // A Builtin item's function (ModuleCodegen.Shim), which a call of it runs and its definition writes; a format shim's is C9b's row, one
    // located Unsupported_Kd at the source (the item's site at its definition). Null after a record.
    private FunctionAbi? Shim(int target, in SourceInfo source)
    {
        if (this.collector.Set.Items[target].Key.Symbol!.CompilerFunction == CompilerFunctionKind.BuiltinFormat)
        {
            this.Report(DiagnosticCode.Unsupported_Kd, source); // C9b writes the format shim.
            return null;
        }

        return this.module.Shim(target);
    }

    // SPEC 13.4: the comparison of the values at two addresses by its node under Equatable or Comparable: equals' i1 or compare's i32; null
    // after a record.
    private Value? Compare(ComparisonNode? node, KimiDeclarationId? contract, Value left, Value right)
    {
        switch (node?.Kind)
        {
            case ComparisonKind.Composite when node.Type.Kind == BoundTypeKind.Tuple:
                return this.Parts(node, contract, left, right);
            case ComparisonKind.Composite:
                return this.Compare(node.Parts[0], contract, this.LoadAt(left, PointerLayout), this.LoadAt(right, PointerLayout)); // A referent.
            case ComparisonKind.Builtin:
                return this.Leaf(node.Type, contract, left, right);
            case ComparisonKind.Witness:
                return this.WitnessLeaf(left, right);
            default:
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector records every other node at the item's admission.
                return null;
        }
    }

    // SPEC 13.4: a Tuple's parts left to right at their Field offsets; each but the last returns its value from the shim when it is not
    // equal (Decide), and the last one's value is the Tuple's.
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

            if ((value = this.Compare(node.Parts[i], contract, this.Offset(left, offsets[i]), this.Offset(right, offsets[i]))) is null)
            {
                return null; // Recorded.
            }
        }

        return value;
    }

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

        var (a, b) = (this.Loaded(type, lowering, left), this.Loaded(type, lowering, right));
        if (!equality)
        {
            return this.Apply((int)MirBinaryOp.Gt, column, a, b, false, default) is { } greater &&
                this.Apply((int)MirBinaryOp.Lt, column, a, b, false, default) is { } less
                ? this.Combine("sub", "i32", this.Convert("zext ", "i1", greater, "i32"), this.Convert("zext ", "i1", less, "i32")) : null;
        }

        return this.Apply((int)MirBinaryOp.Eq, column, a, b, false, default) is { } same &&
            this.Apply((int)MirBinaryOp.Ne, column, a, a, false, default) is { } leftNaN &&
            this.Apply((int)MirBinaryOp.Ne, column, b, b, false, default) is { } rightNaN
            ? this.Combine("or", "i1", same, this.Combine("and", "i1", leftNaN, rightNaN)) : null;
    }

    // A Witness leaf: the shim's next target, its witness's body (BodyRun, through its virtual slot when it implements one; G157 (28)),
    // called with the two leaf addresses at its logical inputs 0 and 1, the shared borrows its requirement takes (SPEC 13.4.1), and with
    // the location the shim received at a caller-location body's location parameters; its value. Null after a record.
    private Value? WitnessLeaf(Value left, Value right)
    {
        var targets = this.collector.Set.Targets(this.item);
        if (this.leaf >= targets.Length || this.BodyRun(targets[this.leaf++].Item) is not { Signature: var callee } run)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets each Witness leaf's body in order (or recorded).
            return null;
        }

        var parameters = callee.Parameters;
        if (parameters.Length != 2 + (callee.CallerLocation ? 2 : 0) || callee.Result is not ("i1" or "i32"))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A witness's signature is its requirement's (SPEC 8.6.3).
            return null;
        }

        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        _ = this.Location(default, out var site); // The shim's own location parameters (a caller-location function).
        for (var i = 0; i < parameters.Length; i++)
        {
            this.arguments[i] = parameters[i].Kind switch
            {
                AbiParameterKind.Location => site.Text,
                AbiParameterKind.LocationLength => site.Length,
                _ => parameters[i].LogicalIndex == 0 ? left : right,
            };
        }

        return this.WriteCall(callee, this.arguments.AsSpan(0, parameters.Length), this.EntryOf(callee, run.Slot));
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
        return this.Apply(RowOf(kind, op), Column(self), left, right, false, default);
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
