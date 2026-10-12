// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A4's summaries and verdicts (DECISIONS R4-M22): the least fixed point of the summaries over one worklist, each node re-evaluated when a
// summary it read grows, then each obligation judged with its remaining atoms resolved by its definition's premises: an owing body per
// view, a Callable binding as a call through it.
internal sealed partial class MirEffects
{
    private readonly List<EffectObligation> rows = [];
    private readonly HashSet<BoundType> glued = new(ReferenceEqualityComparer.Instance); // The parts one evaluation destroyed.
    private readonly List<(BindingSymbol Target, BoundType? Declaring)> targets = []; // What requirements on Types that are not abstract run, a stack.
    private byte[] classes = []; // Each node's summary: its classes,
    private int[] atomStarts = []; // and its atoms, a segment of atoms (a node that grows appends a new segment).
    private int[] atomCounts = [];
    private Atom[] atoms = [];
    private Atom[] scratch = []; // The atoms of the current evaluation.
    private int[] queue = [];
    private ulong[] queued = [];
    private int[] readers = []; // The last node that read each node, so consecutive reads record one dependency,
    private int[] dependentHeads = []; // and each node's readers, a list in dependents.
    private (int Node, int Next)[] dependents = [];
    private MirProgram program = null!;
    private BindingSymbol? definition; // The definition whose premises resolve atoms: the evaluated node's, or an obligation's.
    private int reader; // The evaluated node, -1 while judging an obligation, whose atoms are resolved at once.
    private int atomCount;
    private int scratchCount;
    private int dependentCount;

    /// <summary>Gets the node evaluations of the last <see cref="Run"/>, linear in the program's calls (V16).</summary>
    internal int Evaluations { get; private set; }

    /// <summary>Solves the summaries of the nodes <see cref="Local"/> recorded (driver step 3) and judges each <c>confined</c> obligation:
    /// per view of its target an environment or unknown effect is its code at its subject, else a limit <c>Unsupported_Kd</c>; a failure the
    /// premises decide covers every view.</summary>
    /// <param name="program">The program whose bodies were recorded.</param>
    /// <param name="report">The problems of the program version.</param>
    internal void Run(MirProgram program, MirReport report)
    {
        this.program = program;
        (this.atomCount, this.dependentCount, this.Evaluations) = (0, 0, 0);
        var count = this.nodeCount;
        MirDataflow.Grow(ref this.classes, count);
        MirDataflow.Grow(ref this.atomStarts, count);
        MirDataflow.Grow(ref this.atomCounts, count);
        MirDataflow.Grow(ref this.queue, count);
        MirDataflow.Grow(ref this.queued, (count + 63) >> 6);
        MirDataflow.Grow(ref this.readers, count);
        MirDataflow.Grow(ref this.dependentHeads, count);
        this.classes.AsSpan(0, count).Clear();
        this.atomCounts.AsSpan(0, count).Clear();
        this.readers.AsSpan(0, count).Fill(-1);
        this.dependentHeads.AsSpan(0, count).Fill(-1);
        this.queued.AsSpan(0, (count + 63) >> 6).Fill(ulong.MaxValue);
        for (var node = 0; node < count; node++)
        {
            this.queue[node] = node;
        }

        // A ring of the queued nodes, seeded in program order: a node is queued at most once at a time.
        var (head, length) = (0, count);
        while (length > 0)
        {
            var node = this.queue[head];
            (head, length) = (head + 1 == count ? 0 : head + 1, length - 1);
            this.queued[node >> 6] &= ~(1UL << node);
            for (var d = this.Evaluate(node) ? this.dependentHeads[node] : -1; d >= 0; d = this.dependents[d].Next)
            {
                var dependent = this.dependents[d].Node;
                if ((this.queued[dependent >> 6] & (1UL << dependent)) == 0)
                {
                    this.queued[dependent >> 6] |= 1UL << dependent;
                    this.queue[(head + length++) % count] = dependent;
                }
            }
        }

        this.Obligations(report);
    }

    private static void Verdict(MirReport report, in EffectObligation row, byte found, ulong views)
    {
        if ((found & (Environment | Unknown)) != 0)
        {
            report.Add(new(row.Code, row.Subject, views));
        }
        else if ((found & Limit) != 0)
        {
            report.Add(new(DiagnosticCode.Unsupported_Kd, row.Subject, views));
        }
    }

    // A node's summary from its local facts and its callees' current summaries: whether it grew.
    private bool Evaluate(int node)
    {
        var body = this.program.Bodies[this.owners[node]];
        this.Evaluations++;
        cases.Select(body, node - this.bases[this.owners[node]]);
        (this.definition, this.reader, this.found, this.scratchCount) = (body.Key.Decl, node, this.local[node], 0);
        this.glued.Clear();
        for (var i = node == 0 ? 0 : this.itemEnds[node - 1]; i < this.itemEnds[node]; i++)
        {
            this.Process(this.items[i].Type, this.items[i].Kind, this.items[i].Requirement);
        }

        for (var e = node == 0 ? 0 : this.edgeEnds[node - 1]; e < this.edgeEnds[node]; e++)
        {
            this.Compose(this.edges[e].Body, this.edges[e].Views, this.edges[e].Subst);
        }

        if (this.found == this.classes[node] && this.scratchCount == this.atomCounts[node])
        {
            return false; // The summary only grows, so equal counts are equal sets.
        }

        MirDataflow.Grow(ref this.atoms, this.atomCount + this.scratchCount);
        this.scratch.AsSpan(0, this.scratchCount).CopyTo(this.atoms.AsSpan(this.atomCount));
        (this.classes[node], this.atomStarts[node], this.atomCounts[node]) = (this.found, this.atomCount, this.scratchCount);
        this.atomCount += this.scratchCount;
        return true;
    }

    // The summaries of a body's views a call selects, composed through its substitution under the selected view; read nodes record the
    // reader.
    private void Compose(int body, ulong views, Subst? subst)
    {
        for (var mask = views; mask != 0; mask &= mask - 1)
        {
            var node = this.bases[body] + BitOperations.TrailingZeroCount(mask);
            if (this.reader >= 0 && this.readers[node] != this.reader)
            {
                this.readers[node] = this.reader;
                MirDataflow.Grow(ref this.dependents, this.dependentCount + 1);
                this.dependents[this.dependentCount] = (this.reader, this.dependentHeads[node]);
                this.dependentHeads[node] = this.dependentCount++;
            }

            this.found |= this.classes[node];
            for (var a = this.atomStarts[node]; a < this.atomStarts[node] + this.atomCounts[node]; a++)
            {
                var atom = this.atoms[a];
                var type = subst is null ? atom.Type : types.Apply(atom.Type, subst);
                this.Process(type is null ? null : cases.Type(type), atom.Kind, atom.Requirement);
            }
        }
    }

    // An input of a summary in the selected view: a part destroyed (every part its destruction reaches), a published requirement on a Type
    // (Require), or a call through a Callable value (SPEC 8.4.10.7): a Closure's or a function item's body entered through its substitution,
    // a common Function Type unknown (erasure keeps no bound), an abstract input by the definition's premises.
    private void Process(BoundType? type, byte kind, BindingSymbol? requirement = null)
    {
        if (type is not null && kind == Requires)
        {
            this.Require(type, requirement!);
            return;
        }

        if (type is null || kind == Drop)
        {
            var glue = new Glue(this);
            this.found |= type is null ? Limit : (byte)0;
            MirTypes.Destruction(type, ref glue);
            return;
        }

        var receiver = (SemanticsKind)(kind - 1);
        if (type.IsAbstract)
        {
            // A call through an abstract input: none where the premises declare confined, a limit where it has several signatures (the
            // call's choice among them is not kept), else the summary's atom.
            var bounds = types.CallableBounds(this.definition, type, receiver);
            this.found |= bounds is null ? Limit : (byte)0;
            if (bounds is { Confined: false })
            {
                this.AddAtom(type, kind);
            }

            return;
        }

        switch (type.Kind)
        {
            case BoundTypeKind.Closure:
                this.Enter(this.program[new(type.Symbol, MirBodyKind.Closure)], types.SubstOfClosure(type), type.Symbol!);
                break;
            case BoundTypeKind.FunctionItem: // A closed requirement item runs the witness its substitution names; another dispatches.
                var item = types.SubstOfItem(type);
                var function = item?.Target ?? type.Symbol!;
                if (types.DispatchBounds(this.definition, function, item?.Self, types.ItemContract(type)) is { } dispatched)
                {
                    this.found |= dispatched.Confined ? (byte)0 : Unknown;
                    break;
                }

                for (var i = 0; types.Implementation(function, item, i) is { } implementation; i++)
                {
                    var own = ReferenceEquals(implementation, function);
                    this.Enter(this.program.Callee(new(MirCalleeKind.Fn, implementation)), own ? item : null, implementation);
                }

                break;
            default:
                this.found |= type.Kind == BoundTypeKind.Function ? Unknown : Limit;
                break;
        }
    }

    // A body a part's destruction or a call enters, composed in the views its substitution selects; a published operation's row instead
    // (SPEC 4.7.5); a function without a body is what Binding classifies.
    private void Enter(MirBody? body, Subst? subst, BindingSymbol symbol)
    {
        if (types.Published(symbol) is { } row)
        {
            if (row.Runs is { } requirement)
            {
                this.Process(this.Below(symbol, subst, row.On), Requires, requirement);
            }

            if (row.Destroys >= 0)
            {
                this.Process(this.Below(symbol, subst, row.Destroys), Drop);
            }

            return;
        }

        if (body is null)
        {
            this.found |= Class(TyCtxt.Bodyless(symbol));
            return;
        }

        this.Compose(body.Ordinal, cases.Views(body, subst), subst);
    }

    // SPEC 4.7.5, 8.4.10.4: a published requirement on a Type. An abstract Type has no implementation: the requirement is nothing where
    // the definition's premises declare it confined, else the summary's atom, composed at the callers (unknown at the top). Any other Type
    // runs each target Binding resolves under those premises (TyCtxt.Witnesses: a compiler function, a composite comparison's leaf
    // witnesses, a witness body as a dynamic edge), and none resolving (a composite with an abstract part) is a limit.
    private void Require(BoundType type, BindingSymbol requirement)
    {
        if (type.IsAbstract)
        {
            if (types.DispatchBounds(this.definition, requirement, type, null)?.Confined != true)
            {
                this.AddAtom(type, Requires, requirement);
            }

            return;
        }

        var start = this.targets.Count;
        this.found |= types.Witnesses(requirement, type, this.definition, this.targets) ? (byte)0 : Limit;
        for (var i = start; i < this.targets.Count; i++)
        {
            var (target, declaring) = this.targets[i];
            this.Enter(this.program.Callee(new(MirCalleeKind.Fn, target)), declaring is null ? null : types.Synthesized(target, declaring, []), target);
        }

        this.targets.RemoveRange(start, this.targets.Count - start);
    }

    // SPEC 16.3, 8.4.10.4 rule 1: a part a destruction reaches. An abstract part is an atom; a Function value's environment is unknown; an
    // owning handle destroys its payload when it is proven Sealed (SPEC 16.3.3), else any more-derived destructor may run; a struct's
    // destructor runs; a string's, an Array's or a Dictionary's storage is released (its elements are parts of their own).
    private void Destroy(BoundType type)
    {
        if (!this.glued.Add(type))
        {
            return;
        }

        if (type.IsAbstract)
        {
            this.AddAtom(type, Drop);
        }
        else if (type.Kind == BoundTypeKind.Function)
        {
            this.found |= Unknown;
        }
        else if (type.Kind == BoundTypeKind.Semantics)
        {
            var glue = new Glue(this);
            var proven = types.Sealed(this.definition, type.Components[0]);
            this.found |= proven ? (byte)0 : Unknown;
            MirTypes.Destruction(proven ? type.Components[0] : null, ref glue);
        }
        else if (AdtDef.DestructorSymbol(type) is { } destructor)
        {
            this.Enter(this.program.Callee(new(MirCalleeKind.Fn, destructor)), types.Synthesized(destructor, type, []), destructor);
        }
    }

    // An atom of the evaluated summary, once; while judging an obligation it is resolved at once by the target's premises: a destroyed
    // abstract part proven Copy does nothing (SPEC 8.4.7.1), any other is unknown (a call through an input reaches here only without
    // confined).
    private void AddAtom(BoundType type, byte kind, BindingSymbol? requirement = null)
    {
        if (this.reader < 0)
        {
            this.found |= kind == Drop && types.Copy(this.definition, type) ? (byte)0 : Unknown;
            return;
        }

        var atom = new Atom(type, kind, requirement);
        for (var i = 0; i < this.scratchCount; i++)
        {
            if (ReferenceEquals(this.scratch[i].Type, type) && this.scratch[i] == atom)
            {
                return;
            }
        }

        MirDataflow.Grow(ref this.scratch, this.scratchCount + 1);
        this.scratch[this.scratchCount++] = atom;
    }

    // R4-M22: each obligation Binding published. A Callable binding is a call through its subject with the selected receiver, judged in
    // every view under the use's premises with no caller view (its open pair layers admit every case), and an owner acquisition also
    // destroys the remaining environment; a failure the premises decide is reported in every view. Every implementation of an owing
    // definition owes (SPEC 8.4: specializations included): its own body per view; a specialization, closed, and a function without a
    // body (what Binding classifies) in every view at once.
    private void Obligations(MirReport report)
    {
        this.rows.Clear();
        types.EffectObligations(this.rows);
        cases.Deselect();
        this.reader = -1;
        foreach (var row in this.rows)
        {
            (this.found, this.scratchCount, this.definition) = (0, 0, row.Definition);
            this.glued.Clear();
            if (row.Callable is { } callable)
            {
                this.Process(callable, (byte)(1 + (int)row.Receiver));
                if (row.Owner)
                {
                    this.Process(callable, Drop);
                }

                Verdict(report, row, this.found, ulong.MaxValue);
            }
            else if (row.Definition is not { } target)
            {
                report.Add(new(row.Code, row.Subject, ulong.MaxValue));
            }
            else
            {
                for (var i = 0; types.Implementation(target, null, i) is { } implementation; i++)
                {
                    this.Owe(report, row, implementation, own: i == 0);
                }
            }
        }
    }

    // An implementation an owing row judges (SPEC 8.4): the definition's own body per view; a specialization, which is closed, and a
    // function without a body in every view at once.
    private void Owe(MirReport report, in EffectObligation row, BindingSymbol implementation, bool own)
    {
        var body = this.program.Callee(new(MirCalleeKind.Fn, implementation));
        var perView = own && body is not null;
        for (var mask = perView ? cases.Views(body!, null) : 1UL; mask != 0; mask &= mask - 1)
        {
            var view = perView ? 1UL << BitOperations.TrailingZeroCount(mask) : ulong.MaxValue;
            (this.found, this.scratchCount) = (0, 0);
            this.glued.Clear();
            if (perView)
            {
                this.Compose(body!.Ordinal, view, null);
            }
            else
            {
                this.Enter(body, null, implementation);
            }

            Verdict(report, row, this.found, view);
        }
    }

    // The destruction walk's visitor: each part reached, and past the walk's depth bound a part this implementation does not follow.
    private readonly struct Glue(MirEffects owner) : IMirDestruction
    {
        public bool Visit(BoundType type, int depth)
        {
            owner.Destroy(type);
            return false;
        }

        public bool Deep()
        {
            owner.found |= Limit;
            return false;
        }
    }
}
