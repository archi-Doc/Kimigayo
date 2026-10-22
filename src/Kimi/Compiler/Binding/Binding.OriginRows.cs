// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The producer of the C1-F4 view, under its contract (TyCtxt.Origins.cs; 2a0-D §2-§4 with DECISIONS R4-M101): one walk over the
// obligations on a pass's first read, each Origin row in the unit of the body Binding formed it in (BindingObligation.Body); a body's slice
// on its first read, its atoms classed by 2a0-D §3's precedence relative to it, each row's Site converted once to the SourceRef MIR names
// syntax by (DECISIONS R4-M113); and a body's fixed-fixed oracle (UniversalRelations).
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
    private readonly List<UniversalRelations> universalRelations = [];
    private int originUnitCount;
    private int originSliceCount;
    private ulong originRowsVersion;

    // The producer's access to a slice it fills; consumers read a slice only through its read-only members (immutable after Bind).
    private interface IOriginRowSetBuilder
    {
        List<OriginRow> Rows { get; }

        List<OriginAtomEntry> Atoms { get; }

        // Each atom's table index, and the parts (1 own, 2 layer) whose rows name it.
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

        for (var i = 0; i < slice.Atoms.Count; i++)
        {
            // E4: the parts share Fixed atoms only, so a chain never crosses from one oracle's part to the other's.
            if (builder.Parts[i] == 3 && slice.Atoms[i].Kind != OriginAtomKind.Fixed)
            {
                this.OriginFinding(slice.Rows[slice.LayerStart].Site);
            }
        }

        return slice;
    }

    // An atom MIR reads (a Ref's or a dereferenced layer's Origin) at a MIR site: resolved at the site's syntax, classed in the body's table,
    // and returned as its index there.
    internal int OriginAtomIn(ExecutableBody body, BoundOrigin origin, SourceRef site)
    {
        var slice = this.OriginRows(body);
        var use = this.compilation.SyntaxOf(site);
        return this.InternAtom(slice, this.CanonicalOrigin(this.OriginAtUse(origin, use)), use, 0);
    }

    // A body's fixed-fixed oracle (2a0-D §4, D8): one premise environment rented at the body's root, the catalog's premises from there
    // outward (R1-R4; R5 for the nodes already in the graph), queried per target and Semantics case.
    internal UniversalRelations UniversalRelationsOf(ExecutableBody body)
    {
        var root = this.BodyRoot(AccessorBody(body)) ?? throw new InvalidOperationException("A body without a root has no premises.");
        foreach (var relations in this.universalRelations)
        {
            if (!relations.Active)
            {
                return relations.Open(root);
            }
        }

        var created = new UniversalRelations(this);
        this.universalRelations.Add(created);
        return created.Open(root);
    }

    // MIR keys a Getter or Setter by the function its calls target; the formation context names the accessor itself.
    private static ExecutableBody AccessorBody(ExecutableBody body)
        => body.Definition?.Declaration is FunctionKoto { Accessor.SignatureSymbol: { } accessor } ? new(accessor) : body;

    // A Projection slot's role (2a0-D D7: only the view decodes slots; DECISIONS R4-M101 E5): a declaration binder (a function, accessor
    // or Property) by its slot, any other binder its own place.
    private static ProjectionRole RoleOf(BoundOrigin atom) => atom.Kind != OriginKind.Projection ? ProjectionRole.None : atom.Binder switch
    {
        FunctionKoto when atom.Slot == ClosureValueSlot => ProjectionRole.ClosureValue,
        FunctionKoto when atom.Slot == CallResultSlot => ProjectionRole.CallResult,
        FunctionKoto when atom.Slot == ReceiverSlot => ProjectionRole.Receiver,
        FunctionKoto when atom.Slot == HelperSlot => ProjectionRole.Helper,
        FunctionKoto when atom.Slot <= EnvironmentSlot(0) => ProjectionRole.Environment,
        FunctionKoto or PropertyAccessorKoto => ProjectionRole.Parameter,
        PropertyKoto => ProjectionRole.Storage,
        _ => ProjectionRole.Place,
    };

    // Whether an atom is a root of a body, by the body that formed it (a non-declaration binder) or by its slot's role (E5): parameter j
    // of f is a root of f's body and of each template (f, i) with j < i; a receiver and a helper borrow of their own body; an environment
    // binding and a call result of their Closure's; a Closure literal's value of the body that evaluates it; an accessor's self and value
    // of the accessor's; a Property's storage of each of its accessors' (one atom, whichever formed it first).
    private static bool RootOf(BoundOrigin atom, ProjectionRole role, ExecutableBody body) => role switch
    {
        ProjectionRole.None or ProjectionRole.Place => atom.Body == body,
        ProjectionRole.Storage => body.Definition?.Declaration is PropertyAccessorKoto { Parent: var property } && ReferenceEquals(property, atom.Binder),
        ProjectionRole.ClosureValue => body.IsBody && EvaluatingBody(new(atom.Binder!.BoundSymbol)) == body,
        ProjectionRole.Parameter when atom.Binder is FunctionKoto => DeclaredBy(atom, body) && (body.Index < 0 || (uint)atom.Slot < (uint)body.Index),
        ProjectionRole.Receiver or ProjectionRole.Helper => DeclaredBy(atom, body) && body.Index < 0,
        _ => DeclaredBy(atom, body),
    };

    private static bool DeclaredBy(BoundOrigin atom, ExecutableBody body) => body.Definition is { } symbol && ReferenceEquals(symbol.Declaration, atom.Binder);

    // SPEC 15.3.6: a meet in its canonical form, operands flattened, deduplicated and ordered, static dropped (Meet).
    private BoundOrigin CanonicalOrigin(BoundOrigin origin)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return origin;
        }

        var result = BoundOrigin.Static;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            result = this.Meet(result, origin.Operands[i]);
        }

        return result;
    }

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
            this.originFindings.Add(site); // A broken contract is InternalInvariant_Kd in a complete compilation only (R4-M10).
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

    // An atom's index in the slice's table, classed on its first appearance; a class no precedence row gives is a broken contract.
    private int InternAtom(OriginRowSet slice, BoundOrigin atom, Koto site, byte part)
    {
        IOriginRowSetBuilder builder = slice;
        if (!builder.Index.TryGetValue(atom, out var index))
        {
            index = builder.Atoms.Count;
            builder.Index.Add(atom, index);
            if (this.ClassifyAtom(atom, slice) is not { } entry)
            {
                this.OriginFinding(SourceRef.Of(site));
                entry = new(atom, OriginAtomKind.Unclassed, RoleOf(atom));
            }

            builder.Atoms.Add(entry);
            builder.Parts.Add(0);
        }

        builder.Parts[index] |= part;
        return index;
    }

    // SPEC 15.6.5 and 2a0-D §3's precedence (E4, E5): an atom's class in body B, the first row matching; null when none does (a broken
    // contract). Roots, their order given by role (rows 3, 4, 5, 7, 7', 8), and atoms of an enclosing body (row 6) exclude each other.
    private OriginAtomEntry? ClassifyAtom(BoundOrigin atom, OriginRowSet slice)
    {
        switch (atom.Kind)
        {
            case OriginKind.Static:
                return new(atom, OriginAtomKind.Fixed, ProjectionRole.None);
            case OriginKind.Parameter or OriginKind.Input:
                // Rows 1 and 9: B's contract (its or an enclosing declaration's), or another declaration's universal atom (PLAN G195).
                return new(atom, this.InContract(atom.Binder, slice.Body) ? OriginAtomKind.Fixed : OriginAtomKind.Unclassed, ProjectionRole.None);
            case OriginKind.Intersection:
                return this.ClassifyMeet(atom, slice);
            case OriginKind.Unbound:
                return new(atom, OriginAtomKind.Unclassed, ProjectionRole.None);
        }

        var role = RoleOf(atom);
        if (role is ProjectionRole.None or ProjectionRole.Place && !atom.Body.IsBody)
        {
            return null; // E5: a non-declaration atom first formed outside every body.
        }

        if (role is ProjectionRole.ClosureValue or ProjectionRole.CallResult && DeclaredBy(atom, slice.Body))
        {
            return new(atom, OriginAtomKind.Fixed, role); // Row 5: B's own call receiver and call result.
        }

        if (RootOf(atom, role, slice.Body) || (slice.Layer.IsBody && RootOf(atom, role, slice.Layer)))
        {
            return new(atom, this.RootKind(atom, role), role);
        }

        for (var body = EvaluatingBody(slice.Body); body.IsBody; body = EvaluatingBody(body))
        {
            if (RootOf(atom, role, body))
            {
                return new(atom, OriginAtomKind.Fixed, role); // Row 6: an enclosing body's atom.
            }
        }

        return new(atom, OriginAtomKind.Unclassed, role); // Row 9: another body's root seen from B.
    }

    // A root's class: an inference or anchor of B Inferred (row 8); a Borrow of static storage formed in B, a site's or an accessor's
    // `storage`, Finite when mutable, Inferred when immutable (rows 7, 7'); a place read through a reference Inferred (row 3's referent
    // clause: a Borrow through a referent, or a stored accessor's instance `storage` through its receiver); any other root Finite (rows 3-5).
    private OriginAtomKind RootKind(BoundOrigin atom, ProjectionRole role)
    {
        if (role == ProjectionRole.None)
        {
            return OriginAtomKind.Inferred;
        }

        if (role is not (ProjectionRole.Place or ProjectionRole.Storage))
        {
            return OriginAtomKind.Finite;
        }

        if (atom.Binder!.BoundSymbol is { } symbol && IsStatic(symbol))
        {
            return IsMutableDeclaration(symbol.Declaration) ? OriginAtomKind.Finite : OriginAtomKind.Inferred;
        }

        return role == ProjectionRole.Storage || this.ReadsReferent(atom.Binder) ? OriginAtomKind.Inferred : OriginAtomKind.Finite;
    }

    // Rows 2, 2' and 9: a meet is one Fixed node when every operand is Fixed, an Inferred meet node otherwise, and forms no edge when an
    // operand forms none.
    private OriginAtomEntry? ClassifyMeet(BoundOrigin meet, OriginRowSet slice)
    {
        var kind = OriginAtomKind.Fixed;
        for (var i = 0; i < meet.Operands.Count; i++)
        {
            if (this.ClassifyAtom(meet.Operands[i], slice) is not { } operand)
            {
                return null;
            }

            if (operand.Kind == OriginAtomKind.Unclassed)
            {
                return new(meet, OriginAtomKind.Unclassed, ProjectionRole.None);
            }

            kind = operand.Kind == OriginAtomKind.Fixed ? kind : OriginAtomKind.Inferred;
        }

        return new(meet, kind, ProjectionRole.None);
    }

    // SPEC 15.3.4: a declaration atom is B's contract when its binder owns B's definition or a scope enclosing it.
    private bool InContract(Koto? binder, ExecutableBody body)
    {
        if (this.BodyRoot(body) is not { } root)
        {
            return false;
        }

        for (var scope = this.scopes.GetValueOrDefault(root) ?? root.BoundSymbol?.Scope; scope is not null; scope = scope.Parent)
        {
            if (ReferenceEquals(scope.Owner, binder))
            {
                return true;
            }
        }

        return ReferenceEquals(root, binder);
    }

    // A body's fixed-fixed oracle (2a0-D §4): Close from a target in one Semantics case, then ask which sources it reached. The structural
    // instances (I1-I4) answer first, as in EntailsOrigin; a request changes no inference or obligation state (originStateVersion).
    internal sealed class UniversalRelations(Binding binding) : IDisposable
    {
        private OriginPremiseEnvironment? environment;
        private BoundOrigin target = BoundOrigin.Static;
        private int version;

        internal bool Active => this.environment is not null;

        // Returns the environment; oracles open at once are disposed in the reverse order of opening, as premise environments nest.
        public void Dispose()
        {
            if (this.environment is { } rented)
            {
                this.environment = null;
                binding.ReturnPremises(rented);
            }
        }

        internal void Close(BoundOrigin target, ulong caseBits)
        {
            var closure = this.Rented().Closure;
            this.target = target;
            closure.Close(closure.Node(target, out _), -1, caseBits);
        }

        internal bool Outlives(BoundOrigin source)
        {
            var closure = this.Rented().Closure;
            if (OriginOutlives(source, this.target))
            {
                return true;
            }

            if (closure.TryNode(source, out var node))
            {
                return closure.Reached(node);
            }

            if (source.Kind != OriginKind.Intersection)
            {
                return false; // No premise names it.
            }

            for (var i = 0; i < source.Operands.Count; i++)
            {
                if (!this.Outlives(source.Operands[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal UniversalRelations Open(Koto root)
        {
            this.environment = binding.RentPremises(root) ?? throw new InvalidOperationException("The premise environment of a body's oracle is not ready.");
            binding.CollectOriginResolvers(this.environment);
            binding.ExtractOriginPremises(this.environment, root);
            this.version = binding.originStateVersion;
            return this;
        }

        private OriginPremiseEnvironment Rented()
            => this.environment is { } rented && this.version == binding.originStateVersion ? rented
                : throw new InvalidOperationException("A body's fixed-fixed oracle is used after its disposal or after a change of Origin state.");
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
