// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The rvalues of FunctionCodegen (R5 package C 3.2, plan C2b; DECISIONS R5-M78, R5-M83, R5-M87), each computed from its operands, then put
// into its Place through the place writer (SPEC 13.7.1): a Use, a Ref, a Discriminant, a Len and an Adapt; the aggregates are in
// FunctionCodegen.Aggregates.cs, an Adapt's object creation in FunctionCodegen.Objects. A string has no Len (Binding's sequence members take
// no string, SPEC 4.7).
internal sealed partial class FunctionCodegen
{
    // A Use: its operand put into the Place in its representation (a value in memory copied; nothing for a zero-sized Place); a string
    // constant, which has nothing to evaluate, written whole once its Place is located.
    private void Use(in MirStatement statement)
    {
        var operand = this.body.Operands[statement.Rvalue.Start];
        if (this.TextOf(operand) is { } text)
        {
            if (this.Destination(statement, out var address))
            {
                this.Text(address, text);
            }

            return;
        }

        if (this.Operand(operand, out var value))
        {
            this.Store(statement, value);
        }
    }

    // Ref (IMPL 21.1.3: a shared, exclusive, two-phase or raw borrow is one nonnull pointer to its Place's storage): the address the place
    // writer gives, stored into the statement's Place.
    private void Ref(in MirStatement statement)
    {
        if (this.Address(statement.Rvalue.Place, out var address))
        {
            this.Store(statement, address);
        }
    }

    // A Discriminant (SPEC 6.5.1, 14.8): the Case ordinal its enum Place's tag holds (Layout.Tag, at the record's offset 0), widened to the
    // Place's isize.
    private void Discriminant(in MirStatement statement)
    {
        if (this.Address(statement.Rvalue.Place, out var address) && this.LoweringOf(statement.Place) is { } isize)
        {
            var tag = Layout.Tag;
            this.Store(statement, this.Convert("zext ", tag.ComputationType, this.LoadAt(address, tag.Layout), isize.ComputationType));
        }
    }

    // A Len (SPEC 4.7.3): the length of a fixed array, Array, Slice or Dictionary Place (Length; a Dictionary's live count, DECISIONS
    // R5-M124), its Place located first, so an Index on its path checks its bounds; a string has no Len (no MIR producer), a broken contract.
    private void Len(in MirStatement statement)
    {
        var place = statement.Rvalue.Place;
        var type = this.InstanceType(place)!;
        if (type.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Slice or BoundTypeKind.Dictionary))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source);
            return;
        }

        var record = type.Kind == BoundTypeKind.FixedArray ? null : this.LoweringOf(place); // A fixed array's length is its Type's.
        if (this.Address(place, out var address) && (record is not null || type.Kind == BoundTypeKind.FixedArray))
        {
            this.Store(statement, this.Length(type, record, address));
        }
    }

    // An Adapt (SPEC 8.14, 13.5.3): the ordinary rvalue of the operation the item's view selects (MirCases.Adapt), written by that rvalue's
    // row: an Identity its operand, a pointer conversion or a non-exclusive upcast a Proven Cast, a borrow or an address a Ref, an object
    // creation its payload's factory (Creation); a view that selects none is a broken contract (Binding admitted each case).
    private void Adapt(in MirStatement statement)
    {
        var rvalue = this.cases.Adapt(statement.Rvalue, out var operation);
        switch (operation)
        {
            case ConversionBinding.None:
                this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source);
                return;
            case ConversionBinding.ObjectCreation:
                this.Creation(statement, rvalue); // FunctionCodegen.Objects (DECISIONS R5-M111).
                return;
            default:
                this.Assign(statement with { Rvalue = rvalue });
                return;
        }
    }
}
