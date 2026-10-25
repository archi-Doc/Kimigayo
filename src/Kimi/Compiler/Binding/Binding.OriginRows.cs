// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The producer of the C1-F4 view, under its contract (TyCtxt.Origins.cs; 2a0-D §2-§4 with DECISIONS R4-M101): one walk over the
// obligations on a pass's first read, each Origin row in the unit of the body Binding formed it in (BindingObligation.Body); a body's slice
// on its first read, a constructor's own rows then its layer's (E4), each row's Site converted once to the SourceRef MIR names syntax by
// (DECISIONS R4-M113). The slice's atom table is Binding.OriginRows.Atoms.cs's, the body's fixed-fixed oracle Binding.OriginRows.Oracle.cs's.
// Input: the pass's obligations of the body-origin deadline. Output: OriginRows (a body's OriginRowSet) and OriginFindings.
// Invariants: a slice is read-only for consumers and filled only through IOriginRowSetBuilder; a row formed with no body is in no slice
// (DECISIONS BU-26); storage is kept across passes.
// Must not: judge a relation, or decode a slot for MIR.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The pass's rows by body, each unit's in obligation order (ends resolved and canonical, CaseMask in the body's binder order), the
    // slices read so far this pass, the findings, and reused scratch; their storage is kept across passes.
    private readonly Dictionary<ExecutableBody, int> originUnitIndex = [];
    private readonly List<List<OriginRowDraft>> originUnits = [];
    private readonly Dictionary<ExecutableBody, int> originSliceIndex = [];
    private readonly List<OriginRowSet> originSlices = [];
    private readonly List<SourceRef> originFindings = [];
    private readonly List<PairBinder> originUseBinders = [];
    private readonly List<PairBinder> originBodyBinders = [];
    private int originUnitCount;
    private int originSliceCount;
    private ulong originRowsVersion;

    // The producer's access to a slice it fills; consumers read a slice only through its read-only members (immutable after Bind).
    private interface IOriginRowSetBuilder
    {
        List<OriginRow> Rows { get; }

        List<OriginAtomEntry> Atoms { get; }

        // Each atom's table index, and its parts (1 own, 2 layer): those whose rows name it, or for an atom MIR reads its forming body's.
        Dictionary<BoundOrigin, int> Index { get; }

        List<byte> Parts { get; }

        void Reset(ExecutableBody body, ExecutableBody layer);

        // Ends the body's own rows: the layer's rows, if any, follow.
        void EndOwnRows();
    }

    internal IReadOnlyList<SourceRef> OriginFindings
    {
        get
        {
            this.PrepareOriginRows();
            return this.originFindings;
        }
    }

    // A body's slice, materialized on its first read this pass: its unit's rows, then for a constructor's own body its layer's (R4-M101 E4).
    internal OriginRowSet OriginRows(ExecutableBody body)
    {
        this.PrepareOriginRows();
        body = AccessorBody(body);
        if (this.originSliceIndex.TryGetValue(body, out var index))
        {
            return this.originSlices[index];
        }

        if (this.originSliceCount == this.originSlices.Count)
        {
            this.originSlices.Add(new());
        }

        var slice = this.originSlices[this.originSliceCount];
        IOriginRowSetBuilder builder = slice;
        this.originSliceIndex.Add(body, this.originSliceCount++);

        // E4: the layer's initializers run in the constructor's own body, never in the template of one of its defaults (the body key decides).
        var layer = AdtDef.IsConstructor(body.Definition) && body.Index < 0 ? LayerOf(body.Definition!) : default;
        builder.Reset(body, layer);
        this.AddOriginRows(slice, body, 1);
        builder.EndOwnRows();
        if (layer.IsBody)
        {
            this.AddOriginRows(slice, layer, 2);
        }

        return slice;
    }

    // MIR keys a Getter or Setter by the function its calls target; the formation context names the accessor itself.
    private static ExecutableBody AccessorBody(ExecutableBody body)
        => body.Definition?.Declaration is FunctionKoto { Accessor.SignatureSymbol: { } accessor } ? new(accessor) : body;

    // The declaration a body's premises and pair binders start from: its definition's, a layer's struct, a module's generated function.
    private Koto? BodyRoot(ExecutableBody body)
    {
        var modules = this.compilation.SourceModules;
        return body.Definition?.Declaration ?? ((uint)body.Index < (uint)modules.Length ? modules[body.Index].GeneratedFunction : null);
    }

    private void OriginFinding(SourceRef site)
    {
        if (this.Result.IsComplete)
        {
            this.originFindings.Add(site); // InternalInvariant_Kd in a complete compilation only (R4-M10), from MirOrigins.Findings.
        }
    }

    // The walk: every Origin obligation of the body-origin deadline, each in its body's unit; a declaration's is a finding.
    private void PrepareOriginRows()
    {
        if (this.originRowsVersion == this.storageVersion)
        {
            return;
        }

        this.originRowsVersion = this.storageVersion;
        (this.originUnitCount, this.originSliceCount) = (0, 0);
        this.originUnitIndex.Clear();
        this.originSliceIndex.Clear();
        this.originFindings.Clear();
        foreach (var obligation in this.obligations)
        {
            if (obligation is not { Kind: BindingObligationKind.OriginOutlives, Deadline: BindingDeadline.BodyOrigins } ||
                obligation is not { Longer: { } longer, Shorter: { } shorter })
            {
                continue;
            }

            if (!obligation.Body.IsBody)
            {
                // DECISIONS BU-26: Binding's declaration judge consumes every row formed with no body whose ends are declaration atoms
                // (JudgeDeclarationRows), so a remaining one is a broken contract.
                this.OriginFinding(SourceRef.Of(obligation.Use));
                continue;
            }

            longer = this.CanonicalOrigin(this.OriginAtUse(longer, obligation.Use));
            shorter = this.CanonicalOrigin(this.OriginAtUse(shorter, obligation.Use));
            if (!ReferenceEquals(longer, shorter))
            {
                var source = obligation.Clause is not null ? OriginRowSource.Declared : obligation.WellFormed ? OriginRowSource.WellFormed
                    : obligation.Discharge == OriginDischarge.CallBorrows ? OriginRowSource.CallBorrows : OriginRowSource.Fit;
                var mask = this.BodyCaseMask(obligation);
                var unit = this.OriginUnit(obligation.Body);
                unit.Add(new(obligation.Use, longer, shorter, obligation.Equality, source, obligation.Clause, obligation.Type, mask));
            }
        }
    }

    private List<OriginRowDraft> OriginUnit(ExecutableBody body)
    {
        if (this.originUnitIndex.TryGetValue(body, out var index))
        {
            return this.originUnits[index];
        }

        if (this.originUnitCount == this.originUnits.Count)
        {
            this.originUnits.Add([]);
        }

        var unit = this.originUnits[this.originUnitCount];
        unit.Clear();
        this.originUnitIndex.Add(body, this.originUnitCount++);
        return unit;
    }

    // An obligation's Condition (bits of the pair binders in scope of its Use) renumbered to its body's binders, MirBody.Binders' order; a
    // binder outside the body is a broken contract.
    private ulong BodyCaseMask(in BindingObligation obligation)
    {
        if (obligation.Condition == 0)
        {
            return 0;
        }

        this.PairBinders(obligation.Use, this.originUseBinders);
        this.PairBinders(this.BodyRoot(obligation.Body)!, this.originBodyBinders);
        var mask = 0UL;
        for (var bits = obligation.Condition; bits != 0; bits &= bits - 1)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
            var at = bit < this.originUseBinders.Count ? this.BodyBinder(this.originUseBinders[bit].Target) : -1;
            if (at < 0)
            {
                this.OriginFinding(SourceRef.Of(obligation.Use));
                continue;
            }

            mask |= 1UL << at;
        }

        return mask;
    }

    private int BodyBinder(BindingSymbol target)
    {
        for (var i = 0; i < this.originBodyBinders.Count; i++)
        {
            if (ReferenceEquals(this.originBodyBinders[i].Target, target))
            {
                return i;
            }
        }

        return -1;
    }

    private void AddOriginRows(OriginRowSet slice, ExecutableBody unit, byte part)
    {
        if (!this.originUnitIndex.TryGetValue(unit, out var index))
        {
            return;
        }

        IOriginRowSetBuilder builder = slice;
        foreach (var draft in this.originUnits[index])
        {
            var longer = this.InternAtom(slice, draft.Longer, draft.Site, part);
            var shorter = this.InternAtom(slice, draft.Shorter, draft.Site, part);
            builder.Rows.Add(new(SourceRef.Of(draft.Site), longer, shorter, draft.Equality, draft.Source, draft.Clause, draft.Destination, draft.CaseMask));
        }
    }

    /// <summary>A body's slice of the view: its rows, a constructor's layer rows from <see cref="LayerStart"/>, and its atom table.</summary>
    internal sealed class OriginRowSet : IOriginRowSetBuilder
    {
        private readonly List<OriginRow> rows = [];
        private readonly List<OriginAtomEntry> atoms = [];
        private readonly Dictionary<BoundOrigin, int> index = new(ReferenceEqualityComparer.Instance);
        private readonly List<byte> parts = [];

        List<OriginRow> IOriginRowSetBuilder.Rows => this.rows;

        List<OriginAtomEntry> IOriginRowSetBuilder.Atoms => this.atoms;

        Dictionary<BoundOrigin, int> IOriginRowSetBuilder.Index => this.index;

        List<byte> IOriginRowSetBuilder.Parts => this.parts;

        internal ExecutableBody Body { get; private set; }

        /// <summary>Gets the layer whose rows follow <see cref="LayerStart"/>, judged with its oracle (DECISIONS R4-M101 E4); no body when none.</summary>
        internal ExecutableBody Layer { get; private set; }

        internal int LayerStart { get; private set; }

        internal IReadOnlyList<OriginRow> Rows => this.rows;

        internal IReadOnlyList<OriginAtomEntry> Atoms => this.atoms;

        void IOriginRowSetBuilder.Reset(ExecutableBody body, ExecutableBody layer)
        {
            (this.Body, this.Layer, this.LayerStart) = (body, layer, 0);
            this.rows.Clear();
            this.atoms.Clear();
            this.index.Clear();
            this.parts.Clear();
        }

        void IOriginRowSetBuilder.EndOwnRows() => this.LayerStart = this.rows.Count;

        /// <summary>Gets whether an atom belongs to the layer's part only (DECISIONS R4-M101 E4, R4-M114): a non-Fixed atom the layer's rows
        /// name or a Field initializer formed; a Fixed atom is shared by both parts.</summary>
        /// <param name="atom">The atom's index.</param>
        /// <returns>Whether it is the layer's.</returns>
        internal bool InLayer(int atom) => this.parts[atom] == 2 && this.atoms[atom].Kind != OriginAtomKind.Fixed;
    }

    // A row of a body's unit before its slice indexes its ends.
    private readonly record struct OriginRowDraft(
        Koto Site,
        BoundOrigin Longer,
        BoundOrigin Shorter,
        bool Equality,
        OriginRowSource Source,
        Koto? Clause,
        BoundType? Destination,
        ulong CaseMask);
}
