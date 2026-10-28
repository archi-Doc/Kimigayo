// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Mir;

// The text of MIR operations: statements, terminators, operands and places, the projections each Place applies.
internal static partial class MirDump
{
    private static void Statement(StringBuilder text, MirBody body, in MirStatement statement)
    {
        if (statement.Kind != MirStatementKind.Assign)
        {
            Place(text.Append(statement.Kind).Append('('), body, statement.Place).Append(')');
            return;
        }

        Place(text, body, statement.Place).Append(" = ");
        var rvalue = statement.Rvalue;
        switch (rvalue.Kind)
        {
            case MirRvalueKind.Use:
                Operand(text, body, body.Operands[rvalue.Start]).Append(rvalue.Op == (byte)MirRefKind.TwoPhaseUniq ? " two-phase" : string.Empty);
                break;
            case MirRvalueKind.Ref:
                text.Append(
                    (MirRefKind)rvalue.Op switch
                    {
                        MirRefKind.Uniq => "&uniq ",
                        MirRefKind.TwoPhaseUniq => "&uniq two-phase ",
                        MirRefKind.Raw => "&raw ",
                        _ => "&",
                    });
                Place(text, body, rvalue.Place).Append(": ").Append(Type(rvalue.Type));
                break;
            case MirRvalueKind.BinaryOp or MirRvalueKind.UnaryOp:
                text.Append(rvalue.Kind == MirRvalueKind.BinaryOp ? ((MirBinaryOp)rvalue.Op).ToString() : ((MirUnaryOp)rvalue.Op).ToString());
                Operands(text, body, rvalue.Start, rvalue.Count).Append(rvalue.Proven ? " proven" : string.Empty);
                break;
            case MirRvalueKind.Cast:
                Operand(text, body, body.Operands[rvalue.Start]).Append(" as ").Append(Type(rvalue.Type)).Append(" (").Append((MirCastKind)rvalue.Op)
                    .Append(rvalue.Proven ? ") proven" : ")");
                break;
            case MirRvalueKind.Aggregate:
                text.Append((MirAggregateKind)rvalue.Op).Append(rvalue.Type is { } adt && AdtDef.IsEnum(adt) ? $" case {rvalue.Arg}" : string.Empty);
                Operands(text, body, rvalue.Start, rvalue.Count).Append(": ").Append(Type(rvalue.Type));
                break;
            case MirRvalueKind.Discriminant or MirRvalueKind.Len:
                Place(text.Append(rvalue.Kind == MirRvalueKind.Len ? "len(" : "discriminant("), body, rvalue.Place).Append(')');
                break;
            case MirRvalueKind.Adapt:
                Operands(text.Append("adapt"), body, rvalue.Start, rvalue.Count);
                if (rvalue.Place != body.Operands[rvalue.Start].Place)
                {
                    Place(text.Append(" payload "), body, rvalue.Place);
                }

                text.Append(": ").Append(Type(rvalue.Type)).Append((rvalue.Op & MirRvalue.AdaptShorthand) != 0 ? " shorthand" : string.Empty)
                    .Append((rvalue.Op & MirRvalue.AdaptInput) != 0 ? " input" : string.Empty);
                break;
            default:
                text.Append("opaque ").Append(rvalue.Arg).Append(": ").Append(Type(rvalue.Type));
                break;
        }
    }

    private static void Terminator(StringBuilder text, MirBody body, in MirTerminator terminator)
    {
        switch (terminator.Kind)
        {
            case MirTerminatorKind.Goto:
                text.Append("goto bb").Append(terminator.Target);
                break;
            case MirTerminatorKind.SwitchInt:
                Operand(text.Append("switch "), body, terminator.Operand).Append(" [");
                text.AppendJoin(
                    string.Empty,
                    body.Cases.Slice(terminator.Start, terminator.Count).ToArray().Select(
                    static x =>
                    FormattableString.Invariant($"{x.Value}: bb{x.Target}, ")));
                text.Append("otherwise: bb").Append(terminator.Target).Append(']');
                break;
            case MirTerminatorKind.Call:
                var callee = terminator.Callee;
                Place(text, body, terminator.Place).Append(" = ").Append(
                    callee.Kind switch
                {
                    MirCalleeKind.Fn => callee.Arg > 0 ? $"{callee.Symbol?.Name}#{callee.Arg - 1}" : callee.Symbol?.Name,
                    MirCalleeKind.Virtual => $"virtual {callee.Symbol?.Name}",
                    MirCalleeKind.Intrinsic => $"intrinsic {(IntrinsicId)callee.Arg}",
                    MirCalleeKind.Requirement when callee.Contract is { } contract => $"requirement {callee.Symbol?.Name} of {contract.Name}",
                    _ => $"{callee.Kind.ToString().ToLowerInvariant()} {callee.Symbol?.Name}",
                });

                if (callee.Kind == MirCalleeKind.Value)
                {
                    Operand(text, body, MirOperand.Const(callee.Arg));
                    Operand(text.Append(' '), body, terminator.Operand);
                }

                if (callee.Subst is { Types.Length: > 0 } subst)
                {
                    text.Append('<').AppendJoin(", ", subst.Types.ToArray().Select(static x => x is null ? "_" : Type(x))).Append('>');
                }

                Operands(text, body, terminator.Start, terminator.Count).Append(" -> ").Append(terminator.Target < 0 ? "!" : "bb" + terminator.Target);
                break;
            case MirTerminatorKind.Drop:
                Place(text.Append("drop "), body, terminator.Place).Append(" -> bb").Append(terminator.Target);
                break;
            default:
                text.Append(terminator.Kind.ToString().ToLowerInvariant());
                break;
        }

        text.Append(terminator.CheckTarget >= 0 ? $" check bb{terminator.CheckTarget}" : string.Empty);
    }

    private static StringBuilder Operands(StringBuilder text, MirBody body, int start, int count)
    {
        text.Append('(');
        for (var i = 0; i < count; i++)
        {
            Operand(text.Append(i == 0 ? string.Empty : ", "), body, body.Operands[start + i]);
        }

        return text.Append(')');
    }

    private static StringBuilder Operand(StringBuilder text, MirBody body, in MirOperand operand)
    {
        switch (operand.Kind)
        {
            case MirOperandKind.Const:
                var constant = body.Consts[operand.Arg];
                text.Append("const ");
                return constant.Kind switch
                {
                    MirConstKind.Integer => text.Append(constant.Bits.ToString(CultureInfo.InvariantCulture)).Append('_').Append(Type(constant.Type)),
                    MirConstKind.Float => text.Append("0x").Append(((ulong)constant.Bits).ToString("x", CultureInfo.InvariantCulture)).Append('_')
                        .Append(Type(constant.Type)),
                    MirConstKind.Bool => text.Append(constant.Bits != 0 ? "true" : "false"),
                    MirConstKind.Unit => text.Append("()"),
                    MirConstKind.String => text.Append('"').Append(
                        ((string?)constant.Value)?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace(
                        "\n",
                        "\\n")).Append('"'),
                    MirConstKind.Location => Source(text.Append("location"), constant.Site),
                    _ => text.Append(constant.Kind == MirConstKind.FnItem ? "fn " : "type ").Append(Type(constant.Type)),
                };
            case MirOperandKind.PairAcquire:
                return Place(text.Append("acquire b").Append(operand.Arg).Append(' '), body, operand.Place);
            default:
                return Place(text.Append(operand.Kind == MirOperandKind.Move ? "move " : "copy "), body, operand.Place);
        }
    }

    // The root, then the projections in the order they apply: a Deref, Downcast or PairLayer wraps what precedes it.
    private static StringBuilder Place(StringBuilder text, MirBody body, MirPlace place)
    {
        if (place.Projection == 0)
        {
            return text.Append(place.IsStatic ? "static" : "_").Append(place.IsStatic ? ~place.Root : place.Root);
        }

        var row = body.Projections[place.Projection];
        Place(
            text.Append(
                row.Kind switch
                {
                    MirProjectionKind.Deref => "(*",
                    MirProjectionKind.Downcast or MirProjectionKind.PairLayer => "(",
                    _ => string.Empty,
                }),
            body,
            place with { Projection = row.Prefix });
        return Step(text, row.Kind, row.Arg);
    }

    // A projection after its prefix; a Deref, Downcast or PairLayer closes the parenthesis its Place opened.
    private static StringBuilder Step(StringBuilder text, MirProjectionKind kind, int arg) => kind switch
    {
        MirProjectionKind.Deref => text.Append(')'),
        MirProjectionKind.Field => text.Append('.').Append(arg),
        MirProjectionKind.Base => text.Append(".base").Append(arg),
        MirProjectionKind.Index => text.Append("[_").Append(arg).Append(']'),
        MirProjectionKind.ConstIndex => text.Append('[').Append(arg).Append(']'),
        MirProjectionKind.Downcast => text.Append(" as case ").Append(arg).Append(')'),
        _ => text.Append("@b").Append(arg).Append(')'),
    };
}
