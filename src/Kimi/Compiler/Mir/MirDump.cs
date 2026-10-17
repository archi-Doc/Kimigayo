// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns deterministic textual inspection of MIR.
// Input: MIR records and published Type names. Output: stable text for goldens and semantic snapshots.
// SourceRef and indexed identities are rendered consistently without mutating the program.
// Must not infer semantics from syntax or become a second MIR validator.
// Its concerns: statements, terminators, operands and places (MirDump.Operations.cs), drop elaboration's output (MirDump.Drops.cs), and
// here a program's and a body's text, the layout of its blocks and the shared source, scope and Type spellings.

using System.Text;

namespace Kimi.Compiler.Mir;

/// <summary>The deterministic text of MIR (R3: the dump goldens and the MIR snapshot). A body is its header, one line per local, scope,
/// pair binder, static, Opaque site and failure body that must not complete (<c>must not complete bb4 from bb2</c>: its end from its
/// entry), then each block's statements and terminator. Every line that comes from syntax ends with it,
/// <c>@id</c> (<c>@module:id</c> outside module 0), and its scope (<c>s1</c>) when it is not the body's; <c>*</c> marks where a source
/// statement starts. Types are spelled as diagnostics spell them (<see cref="TyCtxt.Name"/>). Places: <c>(*p)</c> Deref,
/// <c>p.0</c> Field, <c>p.base1</c> Base, <c>p[_3]</c> Index, <c>p[2]</c> ConstIndex, <c>(p as case 1)</c> Downcast and <c>(p@b0)</c>
/// PairLayer; borrows <c>&amp;</c>, <c>&amp;uniq</c>, <c>&amp;uniq two-phase</c> and <c>&amp;raw</c>; an Opaque value <c>opaque 0: T</c>; a case
/// adaptation <c>adapt(copy _1, const type T) payload (_1@b0): s/T shorthand</c>, its payload where it is not the operand's Place and
/// <c>shorthand</c> for the Semantics shorthand; a default template, as a body and as a callee, <c>f#1</c>. An analyzed body adds drop
/// elaboration's output (DECISIONS R4-M6a): its flags with the points at its entry (<c>flags 2 [set f0]</c>), each Drop's verdict groups
/// with their views where they are not all (<c>[keep]</c>, <c>[flag f0]</c>, <c>[elide in v0; open f1 (.0 elide, .1 flag f2) in v1]</c>)
/// and each location's flag points (<c>[clear f0]</c>, <c>[set f0-f2 in v1]</c>); a Drop only the checks reach has no verdict.</summary>
internal static partial class MirDump
{
    /// <summary>Writes the bodies of one module, then the construction records and validator findings located there.</summary>
    /// <param name="program">The program.</param>
    /// <param name="module">The module ordinal.</param>
    /// <param name="text">The output.</param>
    internal static void Write(MirProgram program, ushort module, StringBuilder text)
    {
        for (var ordinal = 0; ordinal < program.Bodies.Length; ordinal++)
        {
            if (program.Bodies[ordinal].Source.Module == module)
            {
                Write(program.Bodies[ordinal], text, program.Drops, ordinal);
            }
        }

        foreach (var record in program.Records.Where(x => x.Subject.Module == module))
        {
            Source(text.Append(record.Code == DiagnosticCode.Unsupported_Kd ? "unsupported" : record.Code.ToString()), record.Subject).Append('\n');
        }

        foreach (var finding in program.Findings.Where(x => x.Subject.Module == module))
        {
            Source(text.Append("finding ").Append(finding.Rule), finding.Subject).Append('\n');
        }
    }

    /// <summary>Writes one body.</summary>
    /// <param name="body">The body.</param>
    /// <param name="text">The output.</param>
    /// <param name="drops">The drop table of the body's program, whose verdicts and flags are written (none for a body it does not hold).</param>
    /// <param name="ordinal">The body's ordinal in the table.</param>
    internal static void Write(MirBody body, StringBuilder text, MirDropTable drops, int ordinal)
    {
        var name = body.Key.Decl?.Name ?? "top-level"; // A Closure has no name.
        text.Append(body.Key.Kind.ToString().ToLowerInvariant()).Append(name.Length == 0 ? string.Empty : " ").Append(name);
        if (body.Key.Kind == MirBodyKind.DefaultTemplate || body.Key.Decl is null)
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
            Source(
                text.Append("  s").Append(i).Append(": ").Append(scope.Kind.ToString().ToLowerInvariant()).Append(" in s").Append(scope.Outer),
                scope.Source).Append('\n');
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

        foreach (var row in body.MustNotComplete)
        {
            Source(text.Append("  must not complete bb").Append(row.End).Append(" from bb").Append(row.Entry), row.Subject).Append('\n');
        }

        Blocks(text, body, drops, ordinal);
    }

    // The flags, then each block's statements and terminator with their flag points and each Drop's verdicts (DECISIONS R4-M6a).
    private static void Blocks(StringBuilder text, MirBody body, MirDropTable drops, int ordinal)
    {
        var points = drops.PointsOf(ordinal);
        if (drops.FlagsOf(ordinal) is > 0 and var flags)
        {
            Points(text.Append("  flags ").Append(flags), body, points, new(0, -1)).Append('\n');
        }

        var next = 0;
        for (var i = 0; i < body.Blocks.Length; i++)
        {
            var block = body.Blocks[i];
            var first = next;
            while (next < points.Length && points[next].At.Block == i)
            {
                next++;
            }

            text.Append("  bb").Append(i).Append(":\n");
            for (var s = 0; s < block.Count; s++)
            {
                var statement = body.Statements[block.Start + s];
                Statement(text.Append(statement.Source.StatementStart ? "    * " : "      "), body, statement);
                Info(Points(text, body, points[first..next], new(i, s)), statement.Source);
            }

            var terminator = block.Terminator;
            Terminator(text.Append(terminator.Source.StatementStart ? "    * " : "      "), body, terminator);
            if (terminator.Kind == MirTerminatorKind.Drop)
            {
                Verdicts(text, body, drops, ordinal, i);
            }

            Info(Points(text, body, points[first..next], new(i, block.Count)), terminator.Source);
        }
    }

    private static StringBuilder Source(StringBuilder text, SourceRef source) => text.Append(source.Module == 0 ? " @" : $" @{source.Module}:")
        .Append(source.Id);

    private static void Info(StringBuilder text, SourceInfo info) => Source(text, info.Syntax).Append(
        info.Scope == 0 ? string.Empty
        : $" s{info.Scope}").Append('\n');

    private static string Type(BoundType? type) => type is null ? "?" : TyCtxt.Name(type);
}
