// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// PLAN G651 U2b R5 (D-C4): the builder's node sites. Each node entry the builder dispatches to (a statement, a statement's value, a value
// into a destination, an operand, a held value, a Place, a declaration, an argument local, a body's end) is a node site at its syntax
// node, opened before the entry and restored after it, so an early return inside the entry cannot skip the restore; the innermost entry's
// node is the site of a read of a published height refusal (IMPL 21.3.2), whose mark the body site (Body) records. No try/finally: the
// body site restores everything below it after a throw.
internal sealed partial class MirBuilder
{
    private void Statement(Koto item)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(item));
        this.StatementAt(item);
        compilation.Types.CloseSite(site);
    }

    private void Evaluated(Koto item)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(item));
        this.EvaluatedAt(item);
        compilation.Types.CloseSite(site);
    }

    private void Into(MirPlace destination, Koto node, bool input = false)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(node));
        this.IntoAt(destination, node, input);
        compilation.Types.CloseSite(site);
    }

    private MirOperand Operand(Koto node, HirAdjust adjust, BoundType? expected, bool input = false, MirPlace? into = null)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(node));
        var operand = this.OperandAt(node, adjust, expected, input, into);
        compilation.Types.CloseSite(site);
        return operand;
    }

    private MirPlace Value(Koto node, MirPlace? into = null, bool input = false, BoundType? expected = null)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(node));
        var place = this.ValueAt(node, into, input, expected);
        compilation.Types.CloseSite(site);
        return place;
    }

    private MirPlace Place(Koto node, bool input = false)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(node));
        var place = this.PlaceAt(node, input);
        compilation.Types.CloseSite(site);
        return place;
    }

    private void Let(VariableKoto variable)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(variable));
        this.LetAt(variable);
        compilation.Types.CloseSite(site);
    }

    private int Argument(BoundType? type, string? name, Koto source, int parameters, MirLocalRole role = MirLocalRole.None, int offset = -1)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(source));
        var local = this.ArgumentAt(type, name, source, parameters, role, offset);
        compilation.Types.CloseSite(site);
        return local;
    }

    private void End(Koto body, bool value)
    {
        var site = compilation.Types.OpenSite(SourceRef.Of(body));
        this.EndAt(body, value);
        compilation.Types.CloseSite(site);
    }
}
