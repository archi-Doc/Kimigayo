// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// The uses an Opaque site names (plan 5.4, SPEC 23.1.2; DECISIONS R5-M63, Z17, Z27).
// - Responsibility: collecting, for the Opaque value MirBuilder.Opaque.cs forms in place of a node, the uses its site names, each a Place,
//   undecided (MirDescriptors.IsUndecided), in their published order: an Error node's kept operands, evaluated first in syntax order as
//   the call builder evaluates its operands and never acquired (KeptOperands, KeptParts; DECISIONS Z17c), their Places counted as the
//   site's kept uses (MirOpaque.Kept); for a derived write, its target Error's kept operands (TargetUses), the read or input ones kept,
//   the others its enclosing uses (MirOpaque.Enclosing: the target designates a Place strictly within each, so A1 checks its
//   construction, not its value; SPEC 13.7.1, 15.1.2); then the locals, parameters, environment bindings and statics the rest of the
//   node's syntax names or a Closure in it captures (SyntaxUses), at their roots. A located part is taken as its consumer takes it
//   (Part, DECISIONS Z17): read at its own node by a FakeRead of its Place where its consumer reads it (Reads), no use where it is a
//   Temporary's built value, else an undecided use at its Place, as a part the builder's own failed step goes below is. A derived write's
//   right side is taken at its own node before its target's parts are evaluated (SPEC 13.7.1), so it is none of its site's uses.
// - Inputs: the node; the published Op and Adjust, a kept operand's form's take and input (HirTables.KeptAdjust, KeptInput), each
//   node's Res and symbol; the Places the builder has bound or located.
// - Outputs: uses and their kept and enclosing counts, the use list Site reads, or the use of a part built already
//   (SiteUses(MirPlace?)); Defaults names a default's earlier slots and Unlent an Opaque temp, neither kept; the kept operands' MIR with a
//   read part's Copy (Secure) and a located read part's FakeRead, right before the site.
// - Invariants: each Place once, a Place both enclosing and read named as read (the stronger check); the kept and enclosing counts are
//   cleared with the list (ClearUses); a kept operand is evaluated once, before the site, and its syntax's roots are not named again,
//   also where several consumers take an Error node an expansion located (R4-M109).
// - Prohibitions: no record, no acquisition of a kept operand, no site formed here (the gate's, MirBuilder.Opaque.cs).
internal sealed partial class MirBuilder
{
    private readonly List<MirPlace> uses = []; // The uses of the Opaque site being formed (Site).
    private readonly List<(Koto Node, MirPlace Place)> kept = []; // The kept operands collected, then their Places, innermost Error node last.
    private int keptUses; // The count of the leading uses that are the site's kept parts' Places (MirOpaque.Kept).
    private int enclosingUses; // The count of the uses after those that are the site's enclosing parts' Places (MirOpaque.Enclosing).
    private SyntaxUses? collector;
    private KeptOperands? keptOperands;

    // Whether the HIR keeps a child of an Error node in place: a part Binding resolved that the publisher keeps (Binding's Keeps: a failed
    // compound Place's operands, an unresolved operation's, a rejected do's parts; DECISIONS R5-M63, R4-M88, BU-106).
    private static bool Kept(Koto node) => HirTables.AdjustOf(node) == new HirAdjust(HirAcquire.None, HirAdaptation.None);

    // The uses of the Opaque site formed for a node (Opaque). SPEC 23.1.2 (DECISIONS R5-M63): an Error node's kept operands are evaluated
    // first, in syntax order, each as its Place (a value in a temp) and never acquired, and the site names their Places instead of their
    // syntax's roots, so a moved operand is not used again there; so does a part built already where it is located (Located: an
    // expansion's operand or a failed jump's operand, Transfer). An Error node located already had its kept operands evaluated there,
    // once, so a consumer's own derived value of it takes only the located part (DECISIONS R4-M109), the Error's derived value, a
    // Temporary, which is no use (Part, Z17). The site names each kept operand's Place first, its kept uses, which it activates as a Call
    // does its inputs (DECISIONS Z17c, MirBorrows.Activations).
    private void SiteUses(Koto node)
    {
        var start = this.KeptParts(node, node);
        this.ClearUses();
        for (var i = start; i < this.kept.Count; i++)
        {
            this.SiteUse(this.kept[i].Place);
        }

        this.keptUses = this.uses.Count;
        this.kept.RemoveRange(start, this.kept.Count - start);
        (this.collector ??= new(this)).Collect(node);
    }

    // DECISIONS Z27 (SPEC 13.7.1 step 2, 13.7.2 step 2, 15.1.2, 23.1.2): the uses of a derived write's site (DerivedWrite), its target
    // Error's kept operands evaluated at the write as an Error node's are (KeptParts). The target is located in place: it takes the values
    // it reads, the receivers of the getters or subscripts that locate it and the receiver of the accessor calls its write makes, a `set`
    // and an update's `get` (KeptAdjust's Reads, KeptInput; SPEC 11.3.1, 13.7.2), its kept uses, and of every other operand only a Place
    // strictly within it, an enclosing use (a value operand whose take failed has KeptAdjust's default too, so it is enclosing); then the
    // names of the target's unkept children (SyntaxUses), read.
    private void TargetUses(Koto node, Koto target)
    {
        var start = this.KeptParts(node, target);
        this.ClearUses();
        for (var i = start; i < this.kept.Count; i++)
        {
            if (Takes(this.kept[i].Node))
            {
                this.SiteUse(this.kept[i].Place);
            }
        }

        this.keptUses = this.uses.Count;
        for (var i = start; i < this.kept.Count; i++)
        {
            if (!Takes(this.kept[i].Node))
            {
                this.SiteUse(this.kept[i].Place);
            }
        }

        this.enclosingUses = this.uses.Count - this.keptUses;
        this.kept.RemoveRange(start, this.kept.Count - start);
        (this.collector ??= new(this)).Collect(target);

        static bool Takes(Koto part) => HirTables.KeptAdjust(part).Reads || HirTables.KeptInput(part);
    }

    // The kept operands of an Error node not located already, evaluated before the site at a node, their Places in the kept list from the
    // returned start (SPEC 23.1.2; DECISIONS R5-M63, R4-M109). DECISIONS Z17c (SPEC 12.2, 15.6.7): they are evaluated as the call builder
    // evaluates its operands, the last that builds code as the Error's form takes it (Builds over KeptAdjust) found first; each is formed
    // as an invocation's reserved input where the form takes it as one (Reserved over KeptInput: a written exclusive borrow is two-phase; a
    // Case payload's, an aggregate element's or a built-in operator's operand stays a live Loan); each the form reads whole that is a
    // Place, no temp, of a Copy Type is secured, its read, at its node where it or a later kept operand builds code, else at the site's
    // node: the decided twin's location in both.
    private int KeptParts(Koto site, Koto error)
    {
        var start = this.kept.Count;
        if (HirTables.Op(error) == HirOp.Error && this.Located(error) is null)
        {
            error.VisitChildren(this.keptOperands ??= new(this));
        }

        var (count, last) = (this.kept.Count, start - 1);
        for (var i = start; i < count; i++)
        {
            var part = this.kept[i].Node;
            last = Builds(part, HirTables.KeptAdjust(part)) ? i : last;
        }

        for (var i = start; i < count; i++)
        {
            var part = this.kept[i].Node;
            var place = this.Place(part, Reserved(HirTables.KeptInput(part), part, HirTables.KeptAdjust(part), expected: null));
            this.kept[i] = (part, this.Secures(part, place) ? this.Secure(MirOperand.Copy(place), i <= last ? part : site).Place : place);
        }

        return start;
    }

    // The use of an Opaque site derived from a part built already, which the builder's own failed step goes below: that part's Place, a
    // local's or a static's, undecided (the step below reads an unknown part), none for a constant (null), never the node's syntax (R5-M63,
    // R5-M84).
    private void SiteUses(MirPlace? built)
    {
        this.ClearUses();
        if (built is { } place)
        {
            this.SiteUse(place);
        }
    }

    // DECISIONS Z17 (SPEC 12, 13.7.1 step 1, 15.1.4-15.1.6, 15.6.2, 23.1.2): a located part, built already, as its consumer takes it: read
    // at its own node where its consumer reads it (Reads), a FakeRead of its Place that every analysis reads as it reads any
    // (A5's Read, A1's check), so its failures are reported there, as its resolved twin's are; no use where it is a Temporary's value,
    // built already (its evaluation is ordinary MIR); else an undecided use at its Place. The rest of the taking (a Move's state, an
    // exclusive borrow's Loan) is the consumer's, which rests on its failure (rule W, DECISIONS R4-M145 C2), and is not built.
    private void Part(Koto part, MirPlace place)
    {
        if (this.Reads(part, place))
        {
            this.body.Push(this.current, MirStatement.Of(MirStatementKind.FakeRead, place, this.Info(part)));
        }
        else if (HirTables.AdjustOf(part).Acquire != HirAcquire.Temporary)
        {
            this.SiteUse(place);
        }
    }

    // Whether a part's consumer reads it, as Binding published it (its Adjust, total over HirAcquire): a part acquired from its Place
    // (Copy, Move, Reborrow or ByCase, any adaptation; SPEC 15.1.5: every acquisition requires its Place readable and complete) or
    // inspected through a shared borrow in place (SPEC 13.4, 15.1.6), of a Type; not a Temporary (its value), a part used in place
    // otherwise (a selection's receiver or base, a Deref's operand, a write's target, an exclusive borrow; SPEC 15.1.4), one whose
    // acquisition failed (BU-103), one no consumer takes (Unset) or a Place of no Type (PLAN G1073).
    private bool Reads(Koto part, MirPlace place)
    {
        var adjust = HirTables.AdjustOf(part);
        return (adjust.Acquire is HirAcquire.Copy or HirAcquire.Move or HirAcquire.Reborrow or HirAcquire.ByCase ||
            adjust == new HirAdjust(HirAcquire.None, HirAdaptation.SharedBorrow)) && this.body.TypeOf(place) is not null;
    }

    // DECISIONS Z17c (R5-M63): whether a kept part's read is its secured Copy: its form reads it whole (KeptAdjust's Reads) and it is a
    // Place, no temp, of a Copy Type; a temp, or any other Place, stays as it is (its securing would be the consumer's taking, rule W).
    private bool Secures(Koto part, MirPlace place) => HirTables.KeptAdjust(part).Reads && !this.IsTemp(place) &&
        this.body.TypeOf(place) is { } type && compilation.Types.Copies(this.body.Key.Decl).Copy(type);

    // The use list of a new site and its kept and enclosing counts, cleared together (MirOpaque.Kept, Enclosing), so no count carries into
    // a later site.
    private void ClearUses()
    {
        this.uses.Clear();
        (this.keptUses, this.enclosingUses) = (0, 0);
    }

    // A use of the Opaque site being formed: each Place once; an enclosing one named again as read leaves the enclosing uses for the read
    // rest (the stronger check).
    private void SiteUse(MirPlace place)
    {
        var at = this.uses.IndexOf(place);
        if (at < 0)
        {
            this.uses.Add(place);
        }
        else if (at >= this.keptUses && at < this.keptUses + this.enclosingUses)
        {
            this.uses.RemoveAt(at);
            this.uses.Add(place);
            this.enclosingUses--;
        }
    }

    // An Error node's kept operands collected in syntax order, which SiteUses evaluates; an operand that is an Error again evaluates its
    // own as its Opaque value is formed.
    private sealed class KeptOperands(MirBuilder builder) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (Kept(node))
            {
                builder.kept.Add((node, default));
            }
        }
    }

    // The locals, parameters, environment bindings and statics a replaced node's syntax names (their Res) or a Closure in it captures,
    // the roots of its Opaque site's undecided uses, but in the operands an Error node keeps, which are evaluated already, and in a part
    // located already, which is taken as its consumer takes it (Part).
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

        // Whether a part is built already where it is located, taken as its consumer takes it (Part) instead of naming its syntax's roots
        // (R5-M63).
        private bool Located(Koto node)
        {
            if (builder.Located(node) is not { } place)
            {
                return false;
            }

            builder.Part(node, place);
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
