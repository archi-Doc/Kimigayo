// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The rvalues of FunctionCodegen (R5 package C 3.2, plan C2b; DECISIONS R5-M78, R5-M83, R5-M87), each computed from its operands, then put
// into its Place through the place writer (SPEC 13.7.1): a Use, a Ref, a Discriminant, a Len and an Adapt; the aggregates are in
// FunctionCodegen.Aggregates.cs.
internal sealed partial class FunctionCodegen
{
    // A Use: its operand put into the Place in its representation (a value in memory copied; nothing for a zero-sized Place); a string
    // constant written whole.
    private void Use(in MirStatement statement)
    {
        var operand = this.body.Operands[statement.Rvalue.Start];
        if (operand.Kind == MirOperandKind.Const && this.body.Consts[operand.Arg] is { Kind: MirConstKind.String, Value: string text } &&
            ReferenceEquals(this.InstanceType(statement.Place), BoundType.String))
        {
            if (this.Destination(statement, out var address))
            {
                this.Text(address, text);
            }

            return;
        }

        if (!this.Operand(operand, out var value))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        this.Store(statement, value);
    }

    // Ref (IMPL 21.1.3: a shared, exclusive, two-phase or raw borrow is one nonnull pointer to its Place's storage): the address the place
    // writer gives, stored into the statement's Place.
    private void Ref(in MirStatement statement)
    {
        if (!this.Address(statement.Rvalue.Place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        this.Store(statement, address);
    }

    // A Discriminant (SPEC 6.5.1, 14.8): the Case ordinal its enum Place's tag holds (Layout.Tag, at the record's offset 0), widened to the
    // Place's isize.
    private void Discriminant(in MirStatement statement)
    {
        if (!this.Address(statement.Rvalue.Place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        if (this.LoweringOf(statement.Place) is { } isize)
        {
            var tag = Layout.Tag;
            this.Store(statement, this.Convert("zext ", tag.ComputationType, this.LoadAt(address, tag.Layout), isize.ComputationType));
        }
    }

    // A Len (SPEC 4.7.3): the length of a fixed array, Array or Slice Place (Length), its Place located first, so an Index on its path
    // checks its bounds. A Dictionary's count (its DictionaryStorage length word) is C8e's row; a string's has no Len row yet (C8c).
    private void Len(in MirStatement statement)
    {
        var place = statement.Rvalue.Place;
        var type = this.InstanceType(place)!;
        if (type.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Slice) || !this.Address(place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        if (this.LoweringOf(place) is { } record)
        {
            this.Store(statement, this.Length(type, record, address));
        }
    }

    // An Adapt (SPEC 8.14, 13.5.3): the ordinary rvalue of the operation the item's view selects (MirCases.Adapt), written by that rvalue's
    // row: an Identity its operand, a pointer conversion or a non-exclusive upcast a Proven Cast, a borrow or an address a Ref. An object
    // creation allocates, C6a's row; a view that selects none is a broken contract (Binding admitted each case).
    private void Adapt(in MirStatement statement)
    {
        var rvalue = this.cases.Adapt(statement.Rvalue, out var operation);
        switch (operation)
        {
            case ConversionBinding.None:
                this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source);
                return;
            case ConversionBinding.ObjectCreation:
                this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // C6a creates the object.
                return;
            default:
                this.Assign(statement with { Rvalue = rvalue });
                return;
        }
    }
}
