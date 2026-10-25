// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// MirOrigins' publication for the Loan stratum's rule W (SPEC 15.6.5; DECISIONS R4-M132), of the body Judge last judged, read after Judge
// and before the next body's.
// - Responsibility: per Ref statement, the recorded Refuted problems (UnsatisfiedOriginRelation_Kd) whose finite source, their Reason's
//   longer end, is that Ref's own Origin (Causes), counted by the atom's index once per Judge; whether the body has any (Refutes, the Loan
//   stratum's gate); and whether a receiving Type holds an Origin and every Origin it holds is Fixed at a site (Fixed).
// - Inputs: Judge's Refs and its problems with the indices MirReport.Add returned; the C1-F4 view's atoms (TyCtxt.OriginAtom).
// - Outputs: Causes, indices in MirReport.All; Refutes; Fixed.
// - Invariants: two Refs of one root share an atom and so share causes; a body judged with no view or over the arena limit has none;
//   Fixed walks each distinct part of a Type once and fails closed on a raw layer, a Type parameter, a projection or a Callable.
// - Prohibitions: no Loan set read; no fact of a body other than the one judged.
internal sealed partial class MirOrigins
{
    private readonly List<int> recorded = []; // Per pending problem: its index in MirReport.All (-1: dropped).
    private readonly Dictionary<BoundOrigin, int> owns = new(ReferenceEqualityComparer.Instance); // Each Ref's own Origin's atom index.
    private readonly HashSet<BoundType> parts = new(ReferenceEqualityComparer.Instance); // Fixed's walk.
    private int[] causeStart = []; // By atom index below anchorCount: its causes in causeList, from causeStart[a] to causeStart[a + 1].
    private int[] causeList = [];
    private int[] cursors = [];
    private int causeCount;

    /// <summary>Gets the recorded Refuted problems whose finite source is the own Origin of the Ref at a statement of the body Judge last
    /// judged (none for another statement), as indices in MirReport.All (SPEC 15.6.5: the causes of a conflict on its Loan).</summary>
    /// <param name="statement">The statement.</param>
    /// <returns>The problems.</returns>
    internal ReadOnlySpan<int> Causes(int statement)
    {
        var (low, high) = (0, this.refCount - 1);
        while (low <= high)
        {
            var mid = (low + high) >> 1;
            var at = this.refs[mid].Statement;
            if (at == statement)
            {
                var own = this.refs[mid].Own;
                return this.causeList.AsSpan(this.causeStart[own], this.causeStart[own + 1] - this.causeStart[own]);
            }

            (low, high) = at < statement ? (mid + 1, high) : (low, mid - 1);
        }

        return default;
    }

    /// <summary>Gets whether a body is the one Judge last judged and has a Refuted problem from a Ref's own Origin: where it has none, rule W
    /// derives nothing, so the Loan stratum sets nothing up for it.</summary>
    /// <param name="body">The body.</param>
    /// <returns>Whether it has causes.</returns>
    internal bool Refutes(MirBody body) => ReferenceEquals(body, this.body) && this.causeCount > 0;

    /// <summary>Gets whether a receiving Type of the body Judge last judged holds an Origin and every Origin it holds, its own, its Origin
    /// arguments' and its components', is Fixed at a site (SPEC 15.6.5: a part with a fixed Origin, every part that can hold the value).</summary>
    /// <param name="type">The case Type, or null.</param>
    /// <param name="site">The site the Origins are resolved at.</param>
    /// <returns>Whether every Origin is Fixed.</returns>
    internal bool Fixed(BoundType? type, SourceRef site)
    {
        this.parts.Clear();
        return this.FixedOrigins(type, site) > 0;
    }

    // The Origins a part holds, each Fixed, or -1 where one is not or the part is one the body does not know (a raw layer, a Type
    // parameter, a projection, a Callable); a part met before counts none.
    private int FixedOrigins(BoundType? type, SourceRef site)
    {
        if (type is null || type.Semantics == SemanticsKind.Raw || type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or
            BoundTypeKind.AssociatedProjection or BoundTypeKind.Function or BoundTypeKind.Closure or BoundTypeKind.FunctionItem)
        {
            return -1;
        }

        if (!this.parts.Add(type))
        {
            return 0;
        }

        var count = 0;
        if (type.Origin is { } origin)
        {
            count = this.IsFixed(origin, site) ? 1 : -1;
        }

        for (var i = 0; count >= 0 && i < type.OriginArguments.Count; i++)
        {
            count = this.IsFixed(type.OriginArguments[i], site) ? count + 1 : -1;
        }

        for (var i = 0; count >= 0 && i < type.Components.Count; i++)
        {
            var inner = this.FixedOrigins(type.Components[i], site);
            count = inner < 0 ? -1 : count + inner;
        }

        return count;
    }

    private bool IsFixed(BoundOrigin origin, SourceRef site) => this.slice.Atoms[types.OriginAtom(this.key, origin, site)].Kind == OriginAtomKind.Fixed;

    // After Judge records: the recorded Refuted problems by the atom of their finite source that is some Ref's own (counting sort); nothing
    // where Judge found none, as Refutes is then false and Causes unread.
    private void IndexCauses()
    {
        var refuted = false;
        for (var p = 0; !refuted && p < this.pending.Count; p++)
        {
            refuted = this.pending[p].Code == DiagnosticCode.UnsatisfiedOriginRelation_Kd;
        }

        if (!refuted)
        {
            return;
        }

        this.owns.Clear();
        for (var r = 0; r < this.refCount; r++)
        {
            this.owns.TryAdd(this.slice.Atoms[this.refs[r].Own].Atom, this.refs[r].Own);
        }

        var atoms = this.anchorCount;
        MirDataflow.Grow(ref this.causeStart, atoms + 1);
        MirDataflow.Grow(ref this.cursors, atoms);
        this.causeStart.AsSpan(0, atoms + 1).Clear();
        this.causeCount = 0;
        for (var p = 0; p < this.pending.Count; p++)
        {
            if (this.Source(p) is var own and >= 0)
            {
                this.causeStart[own + 1]++;
                this.causeCount++;
            }
        }

        for (var a = 0; a < atoms; a++)
        {
            this.causeStart[a + 1] += this.causeStart[a];
        }

        this.causeStart.AsSpan(0, atoms).CopyTo(this.cursors);
        MirDataflow.Grow(ref this.causeList, this.causeCount);
        for (var p = 0; p < this.pending.Count; p++)
        {
            if (this.Source(p) is var own and >= 0)
            {
                this.causeList[this.cursors[own]++] = this.recorded[p];
            }
        }
    }

    // A recorded Refuted problem's finite source, where it is a Ref's own atom; -1 otherwise.
    private int Source(int problem)
        => this.pending[problem] is { Code: DiagnosticCode.UnsatisfiedOriginRelation_Kd, Reason.Longer: { } longer } && this.recorded[problem] >= 0 &&
            this.owns.TryGetValue(longer, out var own) ? own : -1;
}
