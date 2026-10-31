// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A4 (plan 5.5; SPEC 8.11.2, 8.11.3, 8.11.4, 15.6.4; DECISIONS R4-M22): per body view the effect summary — the classes
// Environment, Unknown, Limit, Derived and Deep (R4-M153) and atoms over the definition's abstract Types (Drop(T), a call through a Callable input T) — and
// the `confined` obligations Binding publishes (TyCtxt.EffectObligations).
// Input: validated bodies per MirCases view, their uses (MirUses), the blocks the runtime edges reach (MirDataflow.Begin), A2's verdicts
// (MirProgram.Drops), TyCtxt's obligations and callee facts, and TyCtxt.Comparison for a tuple operator's call (its distinct leaves;
// DECISIONS R5-M166; a published requirement on a composite Type keeps Binding's legacy plan, TyCtxt.Witnesses, until A-12).
// Output: IncompatibleContractImplementation_Kd,
// UnsatisfiedEffectBound_Kd, Unsupported_Kd and OwnershipDepthLimit_Kd through MirReport, and for A5 the per-node summaries and a site's key row and classes
// per view (Keys, Field).
// Atoms compose through each call's substitution into the caller (an atom concrete there is resolved there, one on a projection with a
// recursive base where it arises, SPEC 15.6.4: the fixed point is finite where the view finds the base, and a base G170 hides still ends at
// G128's bound); requirement and virtual calls take the bounds of the caller's own premises; one worklist with recorded dependencies solves
// the least fixed point; storage is pooled and reset by counts, so a warm version allocates nothing.
// Must not read syntax or HIR, re-derive premises, bounds or obligations, or report a failure that only an Opaque site or a callee
// Binding rejected causes (derived, R4-M10).

using System.Numerics;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>A4, the effect summaries (plan P8, R4 shadow): <see cref="Local"/> records each body view's local facts, <see cref="Run"/>
/// solves the summaries of the program version and judges the obligations.</summary>
/// <param name="types">The Type context, whose effect facts Binding published.</param>
/// <param name="cases">The Semantics case views.</param>
/// <param name="flow">The dataflow, begun with the body whose views <see cref="Local"/> records: the blocks the runtime edges reach.</param>
internal sealed partial class MirEffects(TyCtxt types, MirCases cases, MirDataflow flow)
{
    /// <summary>A class of a summary: an effect that cannot be classified, which may access any key (SPEC 15.6.4).</summary>
    internal const byte Unknown = 2;

    /// <summary>A class of a summary: an effect this implementation cannot classify yet (a located <c>Unsupported_Kd</c>).</summary>
    internal const byte Limit = 4;

    /// <summary>A class of a summary: an effect only an Opaque site or a callee Binding rejected causes, a dependent of that error
    /// (R4-M10).</summary>
    internal const byte Derived = 8;

    /// <summary>A class of a summary: a destroyed part past the destruction walk's bound, whose effects the walk does not follow (the depth
    /// limit, <c>OwnershipDepthLimit_Kd</c>; DECISIONS R4-M153).</summary>
    internal const byte Deep = 16;

    private const byte Environment = 1; // The class of an environment effect (SPEC 8.11.2).
    private const byte Drop = 0; // An atom's kind: the destruction of a value of its Type, else 1 + the receiver of a call through it,
    private const byte Shared = 1 + (int)SemanticsKind.Ref; // (a published row's Callable input is called through a shared borrow),
    private const byte Requires = byte.MaxValue; // or a published requirement on its Type (SPEC 4.9.6).

    private readonly HashSet<ComparisonNode> compared = new(ReferenceEqualityComparer.Instance); // A comparison call's distinct nodes (Compared).
    private int[] bases = []; // The first node of each body, by ordinal: a node is a body view.
    private int[] owners = []; // The body of each node.
    private byte[] local = []; // The classes of each node's local facts.
    private int[] itemEnds = []; // The end of each node's local inputs in items.
    private int[] edgeEnds = []; // The end of each node's calls in edges.
    private Atom[] items = [];
    private Edge[] edges = [];
    private int nodeCount;
    private int itemCount;
    private int edgeCount;
    private byte found; // The classes the current evaluation or local walk found.
    private ulong version; // The program version whose body views Local records (MirProgram.Version; 0: none).

    /// <summary>Gets the one answer of classes (DECISIONS R4-M153): the Join of their outcomes, an <see cref="Environment"/> or
    /// <see cref="Unknown"/> effect decided, <see cref="Limit"/> the Limit and <see cref="Deep"/> Deep. A4's obligations and A5's effect
    /// rule record by it.</summary>
    /// <param name="classes">Classes of a summary or a site.</param>
    /// <returns>The classes' outcome.</returns>
    internal static MirOutcome Outcome(byte classes)
    {
        var decided = (classes & (Environment | Unknown)) != 0 ? MirOutcome.Decided : MirOutcome.None;
        return decided.Join((classes & Limit) != 0 ? MirOutcome.Limit : MirOutcome.None).Join((classes & Deep) != 0 ? MirOutcome.Deep : MirOutcome.None);
    }

    /// <summary>Records the local facts of a body view (driver step 2, after the body's A1 and A2, the dataflow begun with the body): its
    /// effects and static accesses, its calls with a body as edges into the callee views their substitution selects, and the parts its
    /// Drops destroy (by A2's verdicts) and the Callable inputs it calls through as inputs of its summary. A program whose
    /// <see cref="MirProgram.Version"/> differs from the one recorded starts a version; a body's views are recorded in order.</summary>
    /// <param name="program">The program.</param>
    /// <param name="body">The body, whose view is selected.</param>
    /// <param name="view">The selected view.</param>
    internal void Local(MirProgram program, MirBody body, long view)
    {
        if (program.Version != this.version)
        {
            this.version = program.Version;
            this.nodeCount = this.itemCount = this.edgeCount = this.keyCount = 0;
            this.fieldIds.Clear();
        }

        var node = this.nodeCount++;
        MirDataflow.Grow(ref this.bases, body.Ordinal + 1);
        MirDataflow.Grow(ref this.owners, this.nodeCount);
        MirDataflow.Grow(ref this.local, this.nodeCount);
        MirDataflow.Grow(ref this.itemEnds, this.nodeCount);
        MirDataflow.Grow(ref this.edgeEnds, this.nodeCount);
        MirDataflow.Grow(ref this.keyEnds, this.nodeCount);
        if (view == 0)
        {
            this.bases[body.Ordinal] = node;
        }

        (this.owners[node], this.found) = (body.Ordinal, 0);
        var uses = new Uses(this, body);
        for (var block = 0; block < body.Blocks.Length; block++)
        {
            for (var index = 0; index <= body.Blocks[block].Count; index++)
            {
                MirUses.ForEach(cases, new(block, index), ref uses);
            }

            this.Terminator(program, body, block, view, body.Blocks[block].Terminator);
        }

        (this.local[node], this.itemEnds[node], this.edgeEnds[node]) = (this.found, this.itemCount, this.edgeCount);
        this.keyEnds[node] = this.keyCount;
    }

    private static byte Class(CompilerFunctionEffect effect) => effect switch
    {
        CompilerFunctionEffect.Inputs => 0,
        CompilerFunctionEffect.Environment => Environment,
        CompilerFunctionEffect.Dependent => Derived,
        _ => Limit,
    };

    // SPEC 8.11.2, 8.11.3, 8.11.4: what a terminator adds. A published operation adds its row through the call's substitution, never
    // its body (SPEC 4.9.6); a call of another function with a body enters each body its substitution may select (SPEC 8.13.3,
    // TyCtxt.Implementation), one without a body is what Binding classifies (TyCtxt.Bodyless). A tuple operator's call runs its parts'
    // comparisons (Compared). A requirement call that selects a built-in
    // target runs what its Self's witnesses run; any other requirement or dynamic virtual call has the bounds of the caller's
    // premises (X2) and may destroy what it is given by value (rule 1); a call through a value calls its Callable input; staticEnsure enters
    // the static's initializer; a Drop destroys what A2 publishes it destroys (MirDropTable.Destroyed).
    private void Terminator(MirProgram program, MirBody body, int block, long view, in MirTerminator terminator)
    {
        var callee = terminator.Callee;
        if (terminator.Kind == MirTerminatorKind.Drop)
        {
            var dropped = new Dropped(this);
            program.Drops.Destroyed(body, block, view, cases.Type(body.TypeOf(terminator.Place)!), flow, ref dropped);
        }
        else if (terminator.Kind != MirTerminatorKind.Call)
        {
            return;
        }
        else if (callee.Kind == MirCalleeKind.Fn)
        {
            this.Function(program, callee);
        }
        else if (callee is { Kind: MirCalleeKind.Requirement, Arg: not 0 })
        {
            var contract = TyCtxt.Contract(callee.Symbol!);
            this.compared.Clear();
            this.Compared(program, body, contract, types.Comparison(contract, callee.Subst!.Self!, operators: true)!); // The builder's root.
        }
        else if (callee is { Kind: MirCalleeKind.Requirement, Symbol.CompilerFunction: not CompilerFunctionKind.None, Subst.Self: { } self })
        {
            this.Item(cases.Type(self), Requires, callee.Symbol); // Binding's built-in target for its Self.
        }
        else if (callee.Kind is MirCalleeKind.Requirement or MirCalleeKind.Virtual)
        {
            var subject = callee.Kind == MirCalleeKind.Requirement ? callee.Subst?.Self : null;
            this.found |= types.DispatchBounds(body.Key.Decl, callee.Symbol!, subject, callee.Contract) == true ? (byte)0 : Unknown;
            this.Given(body, terminator);
        }
        else if (callee.Kind == MirCalleeKind.Value)
        {
            var operand = terminator.Operand;
            var type = cases.Type(operand.Kind == MirOperandKind.Const ? body.Consts[operand.Arg].Type : body.TypeOf(operand.Place)!);
            _ = TyCtxt.Layer(type, out var callable, out _);
            this.Item(callable, (byte)(1 + (int)(type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq ? type.Semantics : SemanticsKind.Owner)));
            if (callable is { IsAbstract: true })
            {
                this.Given(body, terminator);
            }
        }
        else if (callee.Arg == (int)IntrinsicId.StaticEnsure)
        {
            this.AddEdge(program.StaticInit(body, body.Operands[terminator.Start])!, null); // The validator checks it has one (G191).
        }
    }

    private void Function(MirProgram program, in MirCallee call)
    {
        var symbol = call.Symbol!;
        var subst = call.Subst;
        if (call.Arg == 0 && types.Published(symbol) is { } row)
        {
            if (row.Runs is { } requirement)
            {
                this.Item(cases.Below(symbol, subst, row.On), Requires, requirement);
            }

            if (row.Destroys >= 0)
            {
                this.Item(cases.Below(symbol, subst, row.Destroys), Drop);
            }

            if (row.Calls >= 0)
            {
                // SPEC 4.9.6, 8.11.4: the row's Callable input, called through a shared borrow of the actual argument and then destroyed.
                var callable = cases.Below(symbol, subst, row.Calls);
                this.Item(callable, Shared);
                this.Item(callable, Drop);
            }

            return;
        }

        if (program.Callee(call) is { } target)
        {
            if (call.Arg > 0)
            {
                this.AddEdge(target, subst); // SPEC 8.13: a default is always the original's, evaluated before a specialization is called.
                return;
            }

            for (var i = 0; types.Implementation(symbol, subst, i) is { } implementation; i++)
            {
                var own = ReferenceEquals(implementation, symbol);
                if ((own ? target : program.Callee(new(MirCalleeKind.Fn, implementation))) is { } callee)
                {
                    this.AddEdge(callee, own ? subst : null);
                }
            }

            return;
        }

        this.Enter(null, subst, symbol);
    }

    // SPEC 13.4, 8.11.3 (DECISIONS R5-M166): a tuple operator's call runs, once per distinct node of its Self's comparison under source
    // operators (the validator checked it composes), what that node's leaf runs: a Witness leaf its witness (Function), a Requirement leaf
    // the requirement under the caller's premises (X2; its operands are shared borrows, so it destroys nothing), a Builtin leaf and a
    // Composite's own node nothing. A None leaf, no answer (a broken contract the collector records as InternalInvariant_Kd at the item,
    // a dependent in an incomplete Binding), is what Require gives a Type nothing resolves for: the Limit, never a silent empty summary.
    private void Compared(MirProgram program, MirBody body, BindingSymbol contract, ComparisonNode node)
    {
        if (!this.compared.Add(node))
        {
            return;
        }

        foreach (var part in node.Parts)
        {
            this.Compared(program, body, contract, part);
        }

        switch (node.Kind)
        {
            case ComparisonKind.Composite or ComparisonKind.Builtin:
                break;
            case ComparisonKind.Witness:
                this.Function(program, new(MirCalleeKind.Fn, node.Target, node.Subst));
                break;
            case ComparisonKind.Requirement:
                this.found |= types.DispatchBounds(body.Key.Decl, node.Target!, node.Subst!.Self, contract) == true ? (byte)0 : Unknown;
                break;
            case ComparisonKind.None:
                this.found |= Limit;
                break;
            default:
                throw new InvalidOperationException($"The comparison kind {node.Kind} has no effect row.");
        }
    }

    // SPEC 8.11.3 rule 1: a call the caller's premises bound may destroy each value moved into it.
    private void Given(MirBody body, in MirTerminator terminator)
    {
        foreach (var operand in body.Operands.Slice(terminator.Start, terminator.Count))
        {
            if (operand.Kind == MirOperandKind.Move)
            {
                this.Item(cases.Type(body.TypeOf(operand.Place)!), Drop);
            }
        }
    }

    private void Item(BoundType? type, byte kind, BindingSymbol? requirement = null)
    {
        if (type is null)
        {
            this.found |= Limit;
            return;
        }

        MirDataflow.Grow(ref this.items, this.itemCount + 1);
        this.items[this.itemCount++] = new(type, kind, requirement);
    }

    private void AddEdge(MirBody callee, Subst? subst)
    {
        MirDataflow.Grow(ref this.edges, this.edgeCount + 1);
        this.edges[this.edgeCount++] = new(callee.Ordinal, cases.Views(callee, subst), subst);
    }

    // An atom of a summary or a local input: a Type and a kind (Drop, 1 + a call's receiver, or Requires with its requirement).
    private readonly record struct Atom(BoundType Type, byte Kind, BindingSymbol? Requirement);

    // A call of a body: the callee's ordinal, the views its substitution selects and the substitution, null for the callee's own.
    private readonly record struct Edge(int Body, ulong Views, Subst? Subst);

    // A2's destroyed parts of a Drop (MirDropTable.Destroyed): each a Drop item of its Type, a Limit for a part with none.
    private readonly struct Dropped(MirEffects owner) : IMirDropped
    {
        public void Part(BoundType? type, int depth) => owner.Item(type, Drop);
    }

    // The local facts of a location: an environment effect (an access to a mutable static, Console output, an integer made a pointer, a
    // raw pointer read from an immutable static) or an Opaque site's unknown effect, a dependent of the error that made it (R4-M10).
    private readonly struct Uses(MirEffects owner, MirBody body) : IMirUses
    {
        public void Operand(in MirOperand operand)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place.IsStatic && body.Statics[~place.Root].Mutable)
            {
                owner.found |= Environment;
            }
        }

        public void Effect(MirEffect effects, int site)
        {
            owner.found |= (effects & (MirEffect.Environment | MirEffect.IntToPointer | MirEffect.RawStaticRead)) != 0 ? Environment : (byte)0;
            owner.found |= (effects & MirEffect.Unknown) != 0 ? Derived : (byte)0;
            if ((effects & (MirEffect.StaticRead | MirEffect.StaticWrite)) != 0 && body.Statics[site].Mutable)
            {
                owner.AddKey(body.Statics[site].Symbol, (effects & MirEffect.StaticWrite) != 0);
            }
        }
    }
}
