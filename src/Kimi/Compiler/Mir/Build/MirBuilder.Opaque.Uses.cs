// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// The uses an Opaque site names (plan 5.4, SPEC 23.1.2; DECISIONS R5-M63, Z17).
// - Responsibility: collecting, for the Opaque value MirBuilder.Opaque.cs forms in place of a node, the uses its site names, each a Place,
//   undecided (MirDescriptors.IsUndecided): an Error node's kept operands, evaluated first in syntax order as their Places and never
//   acquired (KeptOperands), then the locals, parameters, environment bindings and statics the rest of the node's syntax names or a
//   Closure in it captures (SyntaxUses), a part located already named by its Place's root.
// - Inputs: the node; the published Op and Adjust, each node's Res and symbol; the Places the builder has bound or located.
// - Outputs: uses, the use list Site reads, or the use of a part built already (SiteUses(MirPlace?)); Defaults names a default's earlier
//   slots and Unlent an Opaque temp; the kept operands' MIR.
// - Invariants: each Place once; a kept operand is evaluated once, before the site, and its syntax's
//   roots are not named again, also where several consumers take an Error node an expansion located (DECISIONS R4-M109).
// - Prohibitions: no record, no acquisition of a kept operand, no site formed here (the gate's, MirBuilder.Opaque.cs).
internal sealed partial class MirBuilder
{
    private readonly List<MirPlace> uses = []; // The uses of the Opaque site being formed (Site).
    private readonly List<MirPlace> kept = []; // The Places of the kept operands evaluated, innermost Error node last.
    private SyntaxUses? collector;
    private KeptOperands? keptOperands;

    // Whether the HIR keeps a child of an Error node in place: a part Binding resolved that the publisher keeps (Binding's Keeps: a failed
    // compound Place's operands, an unresolved operation's, a rejected do's parts; DECISIONS R5-M63, R4-M88, BU-106).
    private static bool Kept(Koto node) => HirTables.AdjustOf(node) == new HirAdjust(HirAcquire.None, HirAdaptation.None);

    // The uses of the Opaque site formed for a node (Opaque). SPEC 23.1.2 (DECISIONS R5-M63): an Error node's kept operands are evaluated
    // first, in syntax order, each as its Place (a value in a temp) and never acquired, and the site names their Places instead of their
    // syntax's roots, so a moved operand is not used again there; so does a part built already where it is located (Located: an
    // expansion's operand, a derived write's right side, DerivedWrite, or a failed jump's operand, Transfer). An Error node located already
    // had its kept operands evaluated there, once, so a consumer's own derived value of it names only the located Place (DECISIONS R4-M109).
    private void SiteUses(Koto node)
    {
        var start = this.kept.Count;
        if (HirTables.Op(node) == HirOp.Error && this.Located(node) is null)
        {
            node.VisitChildren(this.keptOperands ??= new(this));
        }

        this.uses.Clear();
        for (var i = start; i < this.kept.Count; i++)
        {
            this.SiteUse(new(this.kept[i].Root));
        }

        this.kept.RemoveRange(start, this.kept.Count - start);
        (this.collector ??= new(this)).Collect(node);
    }

    // The use of an Opaque site derived from a part built already, which the builder's own failed step goes below: that part's Place, a
    // local's or a static's, none for a constant (null), never the node's syntax (R5-M63, R5-M84).
    private void SiteUses(MirPlace? built)
    {
        this.uses.Clear();
        if (built is { } place)
        {
            this.SiteUse(new(place.Root));
        }
    }

    // A use of the Opaque site being formed: each Place once.
    private void SiteUse(MirPlace place)
    {
        if (!this.uses.Contains(place))
        {
            this.uses.Add(place);
        }
    }

    // An Error node's kept operands in syntax order, each evaluated as its Place and kept; an operand that is an Error again evaluates its
    // own as its Opaque value is formed.
    private sealed class KeptOperands(MirBuilder builder) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (Kept(node))
            {
                builder.kept.Add(builder.Place(node));
            }
        }
    }

    // The locals, parameters, environment bindings and statics a replaced node's syntax names (their Res) or a Closure in it captures,
    // the roots of its Opaque site's undecided uses, but in the operands an Error node keeps, which are evaluated already, and in a part
    // located already, which is named by its Place.
    private sealed class SyntaxUses(MirBuilder builder) : KotoVisitor
    {
        private bool operands; // The nodes visited are an Error node's own children, whose kept ones its site names already.

        public override void Visit(Koto node)
        {
            if ((this.operands && Kept(node)) || this.Located(node))
            {
                return;
            }

            var operands = this.operands;
            this.operands = false;
            this.Named(node);
            node.VisitChildren(this);
            this.operands = operands;
        }

        // The uses of a node's syntax, collected after its kept operands' (Opaque): an Error node's own children but those it keeps.
        internal void Collect(Koto node)
        {
            if (this.Located(node))
            {
                return;
            }

            this.Named(node);
            this.operands = HirTables.Op(node) == HirOp.Error;
            node.VisitChildren(this);
            this.operands = false;
        }

        // Whether a part is built already where it is located, whose Place the site names instead of its syntax's roots (R5-M63).
        private bool Located(Koto node)
        {
            if (builder.Located(node) is not { } place)
            {
                return false;
            }

            builder.SiteUse(new(place.Root));
            return true;
        }

        // The roots a node names, undecided (PLAN G121): its Res's binding or static, and a Closure's capture sources.
        private void Named(Koto node)
        {
            var symbol = HirTables.Symbol(node);
            switch (HirTables.ResOf(node))
            {
                case HirRes.Local or HirRes.Param or HirRes.PreparedParam or HirRes.Capture:
                    this.Add(symbol);
                    break;
                case HirRes.Static when symbol is not null:
                    builder.SiteUse(new(builder.StaticRoot(symbol).Root));
                    break;
            }

            var captures = node is FunctionKoto function ? BoundClosure.Of(function)?.Captures : null; // A named function has no definition.
            for (var i = 0; i < captures?.Count; i++)
            {
                this.Add(captures[i].Source);
            }
        }

        private void Add(BindingSymbol? symbol)
        {
            if (symbol is not null && builder.Bound(symbol) is { } place)
            {
                builder.SiteUse(new(place.Root));
            }
        }
    }
}
