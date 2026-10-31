// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// Layout obligations (K package 4.8, X18; DECISIONS R5-M37, R5-M44, R5-M55, R5-M149, R5-M150): the verdict of each use in an item's
// instance, a by-value use or a layout need past its root's storage (LayoutNeeds), each body's list of the latter, and the by-value uses
// of a root module's import signatures.
internal sealed partial class InstanceCollector
{
    // K 4.8 (DECISIONS R5-M149): each body's projection steps whose layout need lies past its roots' storage, `needs` from
    // `needStarts[body.Ordinal]`: the location, the step's Place and the PairLayer step whose case decides it (0: none).
    private (MirLocation At, MirPlace Step, int Pair)[] needs = [];
    private int[] needStarts = [];
    private int[] chain = []; // A Place's steps, last first (ListNeeds' scratch).
    private int needCount;

    /// <summary>Gets the record of a Layout outcome (K 4.8; R5-M37: the collector alone maps Layout's outcomes). A size, count or depth limit
    /// is Resource (IMPL 21.3.2); a C-layout instance whose C layout is unavailable (open, derived, empty or storing a zero-sized Field), at
    /// any depth in the window, is the Language error InvalidCLayout_Kd (SPEC 6.7.1, 8.15.3); Ok has none, and Rejected storage, which rests on
    /// a part Binding rejected, is a broken contract in a complete compilation and otherwise a dependent that Report drops (DECISIONS BU-38,
    /// R5-M151). Oversized is reported through Result.Oversized, which an oversized CLayout carries too.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>Its record, or null for none.</returns>
    internal DiagnosticCode? RecordOf(Layout.Outcome outcome) => outcome switch
    {
        Layout.Outcome.Ok or Layout.Outcome.Oversized => null,
        Layout.Outcome.Rejected => DiagnosticCode.InternalInvariant_Kd,
        Layout.Outcome.SizeLimit or Layout.Outcome.CountLimit or Layout.Outcome.DepthLimit => DiagnosticCode.GenerationResourceLimit_Kd,
        Layout.Outcome.CLayout => DiagnosticCode.InvalidCLayout_Kd,
        _ => throw new InvalidOperationException($"The layout outcome {outcome} has no record row."),
    };

    // SPEC 8.15.3, 22.6 (DECISIONS R5-M150): an import a root module declares is checked as a nongeneric body is, called or not (22.6 makes
    // it nongeneric): its parameters' and result's Types (TyCtxt.Signature; none where Binding failed it) are by-value uses at their written
    // Types (TyCtxt.SignatureSite), beside the Unsupported_Kd Binding gives a struct, Tuple or fixed array there (a valid import's Types are
    // always laid out). It is no item: no body, no target, nothing generated.
    private void LayOutImports()
    {
        var imports = this.types.Imports; // Read by index: a warm run allocates no enumerator.
        for (var i = 0; i < imports.Count; i++)
        {
            var function = imports[i];
            if (TyCtxt.SignatureSite(function, -1).Module >= this.rootModules)
            {
                continue;
            }

            _ = TyCtxt.Signature(function, null, -1, out var count);
            for (var slot = -1; slot < count; slot++)
            {
                if (TyCtxt.Signature(function, null, slot, out _) is { } type && TyCtxt.SignatureSite(function, slot) is var site &&
                    this.Verdict(site, type).IsLimit)
                {
                    this.Limit(this.types.Erase(type), new(function, MirBodyKind.Fn), site); // The import is its own declaration.
                }
            }
        }
    }

    // The locals of an item's body in its instance, each a by-value use at its declaration, but its drop flags and a constructor's
    // construction receiver (MirLocalRole.Receiver), whose storage its caller's construction expression made and laid out (SPEC 6.3.2-6.3.4,
    // 8.15.3; DECISIONS BU-48): the body reaches it only for its Fields' access.
    private void LayOutLocals(int item, MirBody body)
    {
        foreach (var local in body.Locals)
        {
            if (local.Kind != MirLocalKind.DropFlag && local.Role != MirLocalRole.Receiver &&
                this.Subject(item, body, local.Source) is var subject && this.In(item, subject, local.Type) is { } type)
            {
                _ = this.LaysOut(item, subject, type);
            }
        }
    }

    // K 4.8 (DECISIONS R5-M149): each listed need of an item's body, its Type (LayoutNeeds.TypeOf) a use at its location in the item's
    // instance; a need past a PairLayer only where the item's view makes that layer a referent (MirCases.Layer), not the owner case's Place.
    private void LayOutNeeds(int item, MirBody body)
    {
        for (var i = this.needStarts[body.Ordinal]; i < this.needStarts[body.Ordinal + 1]; i++)
        {
            var (at, step, pair) = this.needs[i];
            var block = body.Blocks[at.Block];
            var subject = this.Subject(item, body, at.Index == block.Count ? block.Terminator.Source : body.Statements[block.Start + at.Index].Source);
            if ((pair == 0 || this.Referent(item, body, step with { Projection = pair })) &&
                this.In(item, subject, LayoutNeeds.TypeOf(body, step)) is { } type)
            {
                _ = this.LaysOut(item, subject, type);
            }
        }
    }

    // Whether a PairLayer step of an item's body selects a referent under the item's view (SPEC 13.5.5), its case's; the owner case's is the
    // Place itself.
    private bool Referent(int item, MirBody body, MirPlace layer)
    {
        _ = this.cases.Select(body, this.Set.Items[item].View);
        var referent = this.cases.Layer(layer) is not null;
        this.cases.Deselect();
        return referent;
    }

    // K 4.8 (DECISIONS R5-M149): a call's TypeArguments need, every Type its substitution binds (LayoutNeeds.TypeOf), each a use at the call.
    private void LaysOutArguments(int item, SourceRef subject, Subst subst)
    {
        var types = LayoutNeeds.TypeOf(subst);
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is { } argument && this.In(item, subject, argument) is { } type)
            {
                _ = this.LaysOut(item, subject, type);
            }
        }
    }

    // The needs of a location's Places past their roots' storage, appended to the list (a template's, once per body and version).
    private void ListNeeds(MirBody body, MirLocation at)
    {
        var reader = new NeedReader(this, body, at);
        MirUses.ForEach(body, at, ref reader);
    }

    // A Place's steps root first: one leaves its root's storage where its prefix is laid out without its component (Layout.Indirect) or is
    // a pair layer, whose case decides it per item; the next step with a need (LayoutNeeds) is past that storage, a use, after which the
    // Type it read is the storage again.
    private void ListNeeds(MirBody body, MirLocation at, MirPlace place)
    {
        var projections = body.Projections;
        var length = 0;
        for (var id = place.Projection; id > 0; id = projections[id].Prefix)
        {
            if (length == this.chain.Length)
            {
                Array.Resize(ref this.chain, Math.Max(16, 2 * length));
            }

            this.chain[length++] = id;
        }

        var past = false;
        var pair = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            var step = place with { Projection = this.chain[i] };
            var row = projections[step.Projection];
            if (row.Kind == MirProjectionKind.PairLayer || Layout.Indirect(body.TypeOf(step with { Projection = row.Prefix })!))
            {
                (past, pair) = (true, row.Kind == MirProjectionKind.PairLayer ? step.Projection : 0);
            }

            if (past && LayoutNeeds.Of(row.Kind) != LayoutNeed.None)
            {
                this.AddNeed(body.Ordinal, at, step, pair);
                past = false;
            }
        }
    }

    // A need appended once per location and step of a body.
    private void AddNeed(int body, MirLocation at, MirPlace step, int pair)
    {
        if (this.needCount > this.needStarts[body] && this.needs[this.needCount - 1] is var last && last.At == at && last.Step == step)
        {
            return;
        }

        if (this.needCount == this.needs.Length)
        {
            Array.Resize(ref this.needs, Math.Max(256, 2 * this.needCount));
        }

        this.needs[this.needCount++] = (at, step, pair);
    }

    // K 4.8 (DECISIONS R5-M108): a Function Type's inputs and result, each a by-value use at a site (an erasure's target: its adapter's entry
    // takes and gives them), requested by an item; whether every one is laid out, each checked.
    private bool LaysOutSignature(int item, SourceRef subject, BoundType function)
    {
        var inputs = function.Components[0].Components;
        var laid = this.LaysOut(item, subject, function.Components[1]);
        for (var i = 0; i < inputs.Count; i++)
        {
            laid &= this.LaysOut(item, subject, inputs[i]); // By index: a warm run allocates no enumerator.
        }

        return laid;
    }

    // Whether a by-value use's Type, at a site of an item's records, is laid out (Verdict); a limit is a use of its erased Type in the
    // item's declaration (Limit).
    private bool LaysOut(int item, SourceRef subject, BoundType type)
    {
        var result = this.Verdict(subject, type);
        if (result.IsLimit)
        {
            this.Limit(this.types.Erase(type), this.Declaration(item), subject);
        }

        return result.Outcome == Layout.Outcome.Ok;
    }

    // Layout's verdict on a by-value use's Type, memoized per program version and the same for Types that differ in Origins only (Layout
    // reads no Origin). A Language record is at each use (SPEC 8.15.3; one per code and subject), a layout over 2^63 - 1 bytes (IMPL
    // 21.1.1, R5-M52) beside a CLayout's when both hold; a limit's record is Limit's.
    private Layout.Result Verdict(SourceRef subject, BoundType type)
    {
        var result = this.layout.Of(type);
        if (result.Oversized)
        {
            this.Report(DiagnosticCode.OversizedLayout_Kd, subject);
        }

        if (!result.IsLimit && this.RecordOf(result.Outcome) is { } code)
        {
            this.Report(code, subject);
        }

        return result;
    }

    // SPEC 8.15 (Limits), IMPL 21.3.2, DIAGNOSTICS rule 6 (DECISIONS R5-M171): a use of an erased Type over a limit in a declaration, kept
    // as the least subject of the declaration's uses in SourceRef order (module, then the syntax table's pre-order), whatever the
    // collection's order; RecordLimits makes each Type's one record per declaration.
    private void Limit(BoundType erased, MirDefKey declaration, SourceRef subject)
    {
        ref var least = ref CollectionsMarshal.GetValueRefOrAddDefault(this.limits, (erased, declaration), out var found);
        if (!found || Order(subject, least) < 0)
        {
            least = subject;
        }
    }

    // IMPL 21.3.2 (DECISIONS R5-M171): the record of each erased Type over a limit in each declaration that uses it, at its least use, in
    // subject order (DIAGNOSTICS rule 6), after the run, when every use has been seen.
    private void RecordLimits()
    {
        if (this.limits.Count == 0)
        {
            return;
        }

        this.limited.Clear();
        foreach (var subject in this.limits.Values)
        {
            this.limited.Add(subject);
        }

        this.limited.Sort(static (x, y) => Order(x, y));
        foreach (var subject in this.limited)
        {
            this.Report(DiagnosticCode.GenerationResourceLimit_Kd, subject);
        }
    }

    // The Places a location uses, each listed by its needs (ListNeeds).
    private readonly struct NeedReader(InstanceCollector owner, MirBody body, MirLocation at) : IMirUses
    {
        public void Operand(in MirOperand operand)
        {
        }

        public void Effect(MirEffect effects, int site)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place.Projection != 0)
            {
                owner.ListNeeds(body, at, place);
            }
        }
    }
}
