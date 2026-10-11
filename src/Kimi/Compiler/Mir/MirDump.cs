// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Mir;

/// <summary>The deterministic text of MIR (R3: the dump goldens and the MIR snapshot). A body is its header, one line per local, scope,
/// pair binder, static and Opaque site, then each block's statements and terminator. Every line that comes from syntax ends with it,
/// <c>@id</c> (<c>@module:id</c> outside module 0), and its scope (<c>s1</c>) when it is not the body's; <c>*</c> marks where a source
/// statement starts. Types are spelled as diagnostics spell them (<see cref="TyCtxt.Name"/>). Places: <c>(*p)</c> Deref,
/// <c>p.0</c> Field, <c>p.base1</c> Base, <c>p[_3]</c> Index, <c>p[2]</c> ConstIndex, <c>(p as case 1)</c> Downcast and <c>(p@b0)</c>
/// PairLayer; borrows <c>&amp;</c>, <c>&amp;uniq</c>, <c>&amp;uniq two-phase</c> and <c>&amp;raw</c>; an Opaque value <c>opaque 0: T</c>.</summary>
internal static class MirDump
{
    /// <summary>Writes the bodies of one module, then the construction records and validator findings located there.</summary>
    /// <param name="program">The program.</param>
    /// <param name="module">The module ordinal.</param>
    /// <param name="text">The output.</param>
    internal static void Write(MirProgram program, ushort module, StringBuilder text)
    {
        foreach (var body in program.Bodies)
        {
            if (body.Source.Module == module)
            {
                Write(body, text);
            }
        }

        foreach (var site in program.Unsupported.Where(x => x.Module == module))
        {
            Source(text.Append("unsupported"), site).Append('\n');
        }

        foreach (var finding in program.Findings.Where(x => x.Subject.Module == module))
        {
            Source(text.Append("finding ").Append(finding.Rule), finding.Subject).Append('\n');
        }
    }

    /// <summary>Writes one body.</summary>
    /// <param name="body">The body.</param>
    /// <param name="text">The output.</param>
    internal static void Write(MirBody body, StringBuilder text)
    {
        text.Append(body.Key.Kind.ToString().ToLowerInvariant()).Append(' ').Append(body.Key.Decl?.Name ?? "top-level");
        if (body.Key.Kind is MirBodyKind.Closure or MirBodyKind.DefaultTemplate || body.Key.Decl is null)
        {
            text.Append('#').Append(body.Key.Index);
        }

        Source(text, body.Source).Append('\n');
        var locals = body.Locals;
        for (var i = 0; i < locals.Length; i++)
        {
            var local = locals[i];
            text.Append("  _").Append(i).Append(": ").Append(Type(local.Type)).Append(' ').Append(local.Kind.ToString().ToLowerInvariant());
            text.Append(local.Mutable ? " mut" : string.Empty).Append(local.Name is null ? string.Empty : " " + local.Name);
            text.Append(local.Role == MirLocalRole.None ? string.Empty : " (" + local.Role + ")");
            Info(text, local.Source);
        }

        for (var i = 1; i < body.Scopes.Length; i++)
        {
            var scope = body.Scopes[i];
            Source(text.Append("  s").Append(i).Append(": ").Append(scope.Kind.ToString().ToLowerInvariant()).Append(" in s").Append(scope.Outer), scope.Source).Append('\n');
        }

        for (var i = 0; i < body.Binders.Count; i++)
        {
            text.Append("  b").Append(i).Append(": ").Append(body.Binders[i].Target.Name).Append(' ').Append(body.Binders[i].Admitted).Append('\n');
        }

        for (var i = 0; i < body.Statics.Length; i++)
        {
            var entry = body.Statics[i];
            text.Append("  static").Append(i).Append(": ").Append(entry.Symbol.Name).Append(": ").Append(Type(entry.Type));
            text.Append(entry.Mutable ? " mut" : string.Empty).Append(entry.RawHolding ? " raw" : string.Empty).Append('\n');
        }

        for (var i = 0; i < body.OpaqueSites.Length; i++)
        {
            var site = body.OpaqueSites[i];
            Source(text.Append("  opaque ").Append(i), site.Source).Append(':');
            foreach (var root in body.OpaqueRoots.Slice(site.Start, site.Count))
            {
                Place(text.Append(' '), body, new(root));
            }

            text.Append('\n');
        }

        for (var i = 0; i < body.Blocks.Length; i++)
        {
            var block = body.Blocks[i];
            text.Append("  bb").Append(i).Append(":\n");
            foreach (var statement in body.Statements.Slice(block.Start, block.Count))
            {
                Statement(text.Append(statement.Source.StatementStart ? "    * " : "      "), body, statement);
                Info(text, statement.Source);
            }

            var terminator = block.Terminator;
            Terminator(text.Append(terminator.Source.StatementStart ? "    * " : "      "), body, terminator);
            Info(text, terminator.Source);
        }
    }

    private static StringBuilder Source(StringBuilder text, SourceRef source) => text.Append(source.Module == 0 ? " @" : $" @{source.Module}:").Append(source.Id);

    private static void Info(StringBuilder text, SourceInfo info) => Source(text, info.Syntax).Append(info.Scope == 0 ? string.Empty : $" s{info.Scope}").Append('\n');

    private static string Type(BoundType? type) => type is null ? "?" : TyCtxt.Name(type);

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
                Operand(text, body, body.Operands[rvalue.Start]);
                break;
            case MirRvalueKind.Ref:
                text.Append((MirRefKind)rvalue.Op switch { MirRefKind.Uniq => "&uniq ", MirRefKind.TwoPhaseUniq => "&uniq two-phase ", MirRefKind.Raw => "&raw ", _ => "&" });
                Place(text, body, rvalue.Place).Append(": ").Append(Type(rvalue.Type));
                break;
            case MirRvalueKind.BinaryOp or MirRvalueKind.UnaryOp:
                text.Append(rvalue.Kind == MirRvalueKind.BinaryOp ? ((MirBinaryOp)rvalue.Op).ToString() : ((MirUnaryOp)rvalue.Op).ToString());
                Operands(text, body, rvalue.Start, rvalue.Count).Append(rvalue.Proven ? " proven" : string.Empty);
                break;
            case MirRvalueKind.Cast:
                Operand(text, body, body.Operands[rvalue.Start]).Append(" as ").Append(Type(rvalue.Type)).Append(" (").Append((MirCastKind)rvalue.Op).Append(rvalue.Proven ? ") proven" : ")");
                break;
            case MirRvalueKind.Aggregate:
                text.Append((MirAggregateKind)rvalue.Op).Append(rvalue.Type is { } adt && AdtDef.IsEnum(adt) ? $" case {rvalue.Arg}" : string.Empty);
                Operands(text, body, rvalue.Start, rvalue.Count).Append(": ").Append(Type(rvalue.Type));
                break;
            case MirRvalueKind.Discriminant or MirRvalueKind.Len:
                Place(text.Append(rvalue.Kind == MirRvalueKind.Len ? "len(" : "discriminant("), body, rvalue.Place).Append(')');
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
                text.AppendJoin(string.Empty, body.Cases.Slice(terminator.Start, terminator.Count).ToArray().Select(static x => FormattableString.Invariant($"{x.Value}: bb{x.Target}, ")));
                text.Append("otherwise: bb").Append(terminator.Target).Append(']');
                break;
            case MirTerminatorKind.Call:
                var callee = terminator.Callee;
                Place(text, body, terminator.Place).Append(" = ").Append(callee.Kind switch
                {
                    MirCalleeKind.Fn => callee.Symbol?.Name,
                    MirCalleeKind.Virtual => $"virtual slot {callee.Arg} {callee.Symbol?.Name}",
                    MirCalleeKind.Intrinsic => $"intrinsic {(IntrinsicId)callee.Arg}",
                    _ => $"{callee.Kind.ToString().ToLowerInvariant()} {callee.Symbol?.Name}",
                });

                if (callee.Kind == MirCalleeKind.Value)
                {
                    Operand(text, body, terminator.Operand);
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
                    MirConstKind.Float => text.Append("0x").Append(((ulong)constant.Bits).ToString("x", CultureInfo.InvariantCulture)).Append('_').Append(Type(constant.Type)),
                    MirConstKind.Bool => text.Append(constant.Bits != 0 ? "true" : "false"),
                    MirConstKind.Unit => text.Append("()"),
                    MirConstKind.String => text.Append('"').Append(((string?)constant.Value)?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append('"'),
                    MirConstKind.Location => Source(text.Append("location"), new((ushort)(constant.Bits >> 32), (int)(constant.Bits & uint.MaxValue))),
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
        Place(text.Append(row.Kind switch { MirProjectionKind.Deref => "(*", MirProjectionKind.Downcast or MirProjectionKind.PairLayer => "(", _ => string.Empty }), body, place with { Projection = row.Prefix });
        return row.Kind switch
        {
            MirProjectionKind.Deref => text.Append(')'),
            MirProjectionKind.Field => text.Append('.').Append(row.Arg),
            MirProjectionKind.Base => text.Append(".base").Append(row.Arg),
            MirProjectionKind.Index => text.Append("[_").Append(row.Arg).Append(']'),
            MirProjectionKind.ConstIndex => text.Append('[').Append(row.Arg).Append(']'),
            MirProjectionKind.Downcast => text.Append(" as case ").Append(row.Arg).Append(')'),
            _ => text.Append("@b").Append(row.Arg).Append(')'),
        };
    }
}
