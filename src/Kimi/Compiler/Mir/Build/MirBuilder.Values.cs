// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Values and Places: a node's own value into a destination (Into), as its consumer takes it (Assign, Operand), and the Place it
// designates (Place), each by the node's Op, Adjust and PlaceShape.
internal sealed partial class MirBuilder
{
    // The value a node's own form produces, into the destination.
    private void Into(MirPlace destination, Koto node)
    {
        switch (HirTables.Op(node))
        {
            case HirOp.Literal when this.Literal(node) is var constant and >= 0:
                this.Use(destination, MirOperand.Const(constant), node);
                break;
            case HirOp.Group: // Its operand, which the group acquires as the operand's Adjust says.
                this.Assign(destination, node switch { LabeledKoto label => label.Target, BinaryKoto conversion => conversion.Left, _ => ((UnaryKoto)node).Operand });
                break;
            case HirOp.Block:
                this.Block(destination, (CodeBlockKoto)node);
                break;
            case HirOp.Call:
                this.Call(destination, node);
                break;
            case HirOp.Jump when ReferenceEquals(HirTables.PlanOf(node).Plan, this.declaration):
                this.Return((JumpKoto)node);
                break;
            case HirOp.Error: // Binding reported it; its value is derived.
                this.Opaque(node, destination);
                break;
            default:
                this.Unsupported(node, destination);
                break;
        }
    }

    // The destination takes a node's value as its consumer takes it (its Adjust): a Temporary, or a group's operand, is built into the
    // destination; any other acquisition is an operand.
    private void Assign(MirPlace destination, Koto node)
    {
        if (HirTables.AdjustOf(node) is { Acquire: HirAcquire.Temporary or HirAcquire.None, Adaptation: HirAdaptation.None })
        {
            this.Into(destination, node);
        }
        else
        {
            this.Use(destination, this.Operand(node), node);
        }
    }

    // A node's value as its consumer takes it (plan: Adjust to operand): a Place Copied or moved, a literal as its constant, another
    // Temporary through a temp. The other acquisitions and the adaptations are not built yet (U7, U8).
    private MirOperand Operand(Koto node)
    {
        var adjust = HirTables.AdjustOf(node);
        if (adjust is { Acquire: HirAcquire.Copy or HirAcquire.Move, Adaptation: HirAdaptation.None })
        {
            var place = this.Place(node);
            return adjust.Acquire == HirAcquire.Copy ? MirOperand.Copy(place) : MirOperand.Move(place);
        }

        if (adjust == new HirAdjust(HirAcquire.Temporary, HirAdaptation.None) && HirTables.Op(node) == HirOp.Literal && this.Literal(node) is var constant and >= 0)
        {
            return MirOperand.Const(constant);
        }

        var temp = this.Temp(node);
        if (adjust == new HirAdjust(HirAcquire.Temporary, HirAdaptation.None))
        {
            this.Into(temp, node);
        }
        else
        {
            this.Unsupported(node, temp);
        }

        return MirOperand.Move(temp);
    }

    // The Place a node designates (its PlaceShape): a local or parameter, or that of a parenthesized Place. The other Places and the
    // layers a consumer follows are not built yet (U7).
    private MirPlace Place(Koto node)
    {
        var shape = HirTables.PlaceOf(node);
        if (shape is { Kind: HirPlaceKind.Local, Layers: 0 } && HirTables.Op(node) == HirOp.Name && HirTables.Symbol(node) is { } symbol && this.locals.TryGetValue(symbol, out var local))
        {
            return new(local);
        }

        if (shape is { Kind: not HirPlaceKind.Value, Layers: 0 } && node is ParenthesizedKoto group && HirTables.Op(node) == HirOp.Group)
        {
            return this.Place(group.Operand);
        }

        var temp = this.Temp(node);
        this.Unsupported(node, temp);
        return temp;
    }

    // SPEC 3.1: a literal's constant, a number's being the value Binding fitted (its plan row's Folded); -1 without one.
    private int Literal(Koto node)
    {
        var type = TypeOf(node);
        MirConst? constant = node switch
        {
            BoolLiteralKoto boolean => new(MirConstKind.Bool, type, boolean.Value ? 1 : 0),
            CharLiteralKoto { Value: { } character } => new(MirConstKind.Integer, type, character.Value),
            StringLiteralKoto text => new(MirConstKind.String, type, Value: text.Literal),
            UnitLiteralKoto => new(MirConstKind.Unit, type),
            NullLiteralKoto => new(MirConstKind.Integer, type),
            _ => HirTables.PlanOf(node).Folded is { } value ? new(type.IsFloatingPoint ? MirConstKind.Float : MirConstKind.Integer, type, value) : null,
        };

        return constant is { } known ? this.body.Const(known) : -1;
    }

    private void Use(MirPlace destination, in MirOperand operand, Koto node)
        => this.body.Push(this.current, MirStatement.Assign(destination, MirRvalue.Use(this.body.AddOperand(operand)), this.Info(node)));

    private void Unit(MirPlace destination, Koto node) => this.Use(destination, this.UnitOperand(), node);

    private MirOperand UnitOperand() => MirOperand.Const(this.body.Const(new(MirConstKind.Unit, BoundType.Unit)));

    private MirPlace Temp(Koto node) => this.Temp(TypeOf(node), node);

    private MirPlace Temp(BoundType type, Koto node) => new(this.body.AddLocal(new(type, MirLocalKind.Temp, this.Info(node, true))));
}
