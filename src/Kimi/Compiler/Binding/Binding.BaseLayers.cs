// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// BaseLayers (R7-N2d; DECISIONS R7-Mnn): a struct's base chain by declaration. SPEC 6.2.2: a struct names at most one direct base.
// - Responsibility: the one reading of a base clause as the declaration it names (DirectBase, DeclQueryKind.DirectBase), the one walk of
//   those readings, and the one rule for a read while a clause is not ready.
// - Inputs: the struct's declaration; its clause's Type, else the clause's name evaluated in the struct's scope, role NoContainer.
// - Outputs: the clause's Type's Symbol, else AdtDef.DirectBase, a clean read's name or a failed clause's, which BindType's read of the same
//   name repeats, so a layer and its clause's Type cannot disagree; earlyReads.
// - In progress: while a clause is read, by this query (Running) or by its BindType (resolvingTypes), its struct's layers read as ended: a
//   clause resolves without them (SPEC 9.5), its name and arguments alike. A verification evaluates a clause's read the same way.
// - Ready: a clause with a Type, a clause naming a struct, and from clausesBindable on a failed clause. Before clausesBindable no
//   associated specification can be read, so a clause is read by its name; from then on a demand binds it, its demander's consultation
//   frame suspended. A read of a clause not ready ends the chain and counts in notReadyReads; so does a read of one in progress. A read
//   that moved the count stores nothing: it is read again at the next demand.
// - Early reads: a TypeName that saw the count move is noted with what it selected, the last such read of a use, and evaluated again
//   after the first ValidateBaseDeclarations (VerifyEarlyReads). An equal answer stands. On a different one the declarations run once more
//   with knownBases, the final Types of the clauses that name no struct, read as ready and counted (secondRun). A difference in that run is
//   Unsupported_Kd at the use. Only a first run fills knownBases, and it starts from none, so they are what a cold Bind of the source and
//   mode computes; a later Bind of both unchanged (knownMode, set as a Bind completes) starts from them as that cold Bind's second run, runs
//   once and reports what it reports. An edit clears them (Invalidate).
// - Ends: a chain follows values final in the run, each a function of the declaration, so it is periodic and BaseWalk ends it; a parameter
//   or a projection through one is no struct and ends it. A recursion among a name's evaluation, Accessible and LookupTypeMember passes
//   DirectBase, Running at most once per struct on the stack, or BindType, resolvingTypes at most once per syntax.
// - Removal: clausesBindable, notReadyReads, earlyReads, knownBases, knownMode, secondRun, verifiedClause, StartRuns, VerifyEarlyReads,
//   Bind's loop and the second PublishInheritedOptOuts go at R7-D2 d3 (plan §5.0, conformances), when a conformance's specification is read
//   by demand.
// - Prohibited: a guard, depth bound or visited set in a reader; a judgment or a record here (ValidateBaseDeclaration owns them).
public sealed partial class Binding
{
    private bool clausesBindable;
    private int notReadyReads;
    private Dictionary<Koto, EarlyRead>? earlyReads;
    private Dictionary<StructKoto, BoundType>? knownBases;
    private BindingMode? knownMode;
    private bool secondRun;
    private Koto? verifiedClause;
    private int baseSteps;

    private readonly record struct EarlyRead(BindingScope Scope, TypeLookupRole Role, int Arity, BindingSymbol? Symbol);

    /// <summary>Gets the steps the walks over BaseLayers took in the latest pass, read by the scaling tests.</summary>
    internal int BaseLayerWork => this.baseSteps;

    // A Bind starts as a second run only from the bases a completed Bind of this mode computed, so no run's result seeds the run after it.
    private void StartRuns(BindingMode mode)
    {
        this.secondRun = this.knownMode == mode && this.knownBases is { Count: > 0 };
        this.knownMode = null;
        if (!this.secondRun)
        {
            this.knownBases?.Clear();
        }
    }

    private BindingSymbol? DirectBase(StructKoto structure)
    {
        if (structure.BaseClause is not { } clause)
        {
            return null;
        }

        if (clause.BoundType is null ? this.resolvingTypes.Count != 0 && this.resolvingTypes.Contains(clause) : ReferenceEquals(clause, this.verifiedClause))
        {
            this.notReadyReads++;
            return null;
        }

        var symbol = structure.BoundSymbol!;
        var shape = symbol.Adt ??= new();
        switch (this.BeginQuery(symbol, DeclQueryKind.DirectBase))
        {
            case DeclState.Done:
                return clause.BoundType?.Symbol ?? shape.DirectBase;
            case DeclState.Running:
                this.notReadyReads++;
                return null;
        }

        var scope = this.scopes[structure];
        var reads = this.notReadyReads;
        if (this.clausesBindable && clause.BoundType is null)
        {
            var consultation = this.SuspendConsultation(); // The clause is its declaration's, never the demander's prerequisite.
            try
            {
                this.BindType(clause, scope);
            }
            finally
            {
                this.ResumeConsultation(consultation);
            }
        }
        else if (!this.clausesBindable && clause.BoundType is null && this.knownBases?.GetValueOrDefault(structure) is { } known)
        {
            this.AbandonQuery(symbol, DeclQueryKind.DirectBase);
            this.notReadyReads++;
            return known.Symbol;
        }

        var named = clause.BoundType?.Symbol ?? this.ProbePath(clause, scope, TypeLookupRole.NoContainer, 0);
        var failed = this.clausesBindable && clause.BindingFailure != BindingFailure.None;
        if (clause.BoundType is not null || failed || (reads == this.notReadyReads && named?.Declaration is StructKoto))
        {
            shape.DirectBase = named;
            this.CompleteQuery(symbol, DeclQueryKind.DirectBase);
            return named;
        }

        this.AbandonQuery(symbol, DeclQueryKind.DirectBase);
        if (named?.Declaration is StructKoto)
        {
            return named; // Noted by the read above it, and verified with the clause's own read.
        }

        this.notReadyReads++; // A projected clause names its struct once bound.
        return null;
    }

    private BaseChain BaseLayers(StructKoto structure) => new(this, structure.BoundSymbol!);

    // True when the declarations must run again, with every clause that names no struct known.
    private bool VerifyEarlyReads()
    {
        var again = false;
        if (this.earlyReads is { Count: > 0 } reads)
        {
            this.earlyReads = null;
            foreach (var (use, read) in reads)
            {
                // A clause's own read is evaluated as it was read: without its struct's layers.
                this.verifiedClause = read.Scope.Owner is StructKoto { BaseClause: { } own } && IsWithin(use, own) ? own : null;
                var final = this.ProbePath(use, read.Scope, read.Role, read.Arity);
                this.verifiedClause = null;
                var missing = use.BindingFailure is BindingFailure.MissingName or BindingFailure.MissingType;
                if (ReferenceEquals(final, read.Symbol) || !(missing || read.Symbol is not null || use.BoundType is not null))
                {
                    continue;
                }

                again |= !this.secondRun;
                if (!this.secondRun)
                {
                    continue;
                }

                if (missing)
                {
                    use.BindingFailure = BindingFailure.None;
                    this.prerequisites.Remove(use);
                }

                this.Fail(use, BindingFailure.Unsupported, missing);
            }

            reads.Clear();
            this.earlyReads = reads;
        }

        if (!again)
        {
            return false;
        }

        this.secondRun = true;
        (this.knownBases ??= new(ReferenceEqualityComparer.Instance)).Clear();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto { BaseClause: { BoundType: { } type } clause } structure && this.ProbePath(clause, this.scopes[structure], TypeLookupRole.NoContainer, 0)?.Declaration is not StructKoto)
            {
                this.knownBases[structure] = type;
            }
        }

        return true;
    }

    // Brent's test over a walk of declarations: no memory beyond a mark, a cycle found within about three times the walk's distinct steps.
    private struct BaseWalk
    {
        private BindingSymbol mark;
        private int steps;
        private int lap;

        internal BaseWalk(BindingSymbol start) => (this.mark, this.lap) = (start, 1);

        internal bool Repeats(BindingSymbol next)
        {
            if (ReferenceEquals(next, this.mark))
            {
                return true;
            }

            if (++this.steps == this.lap)
            {
                (this.mark, this.steps, this.lap) = (next, 0, this.lap * 2);
            }

            return false;
        }
    }

    // The declarations above a struct, nearest first; a non-struct, a clause not ready and a repeat end it.
    private struct BaseChain
    {
        private readonly Binding binding;
        private BaseWalk walk;

        internal BaseChain(Binding binding, BindingSymbol start) => (this.binding, this.Current, this.walk) = (binding, start, new(start));

        public BindingSymbol Current { get; private set; }

        public readonly BaseChain GetEnumerator() => this;

        public bool MoveNext()
        {
            this.binding.baseSteps++;
            if (this.Current.Declaration is not StructKoto structure || this.binding.DirectBase(structure) is not { } next || this.walk.Repeats(next))
            {
                return false;
            }

            this.Current = next;
            return true;
        }
    }
}
