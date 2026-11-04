// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The summary's reader contract belongs to its one composition.

namespace Kimi.Compiler.Mir;

/// <summary>A reader of a declaration's destruction summary, which <see cref="MirEffects.Compose"/> composes at an instance below an
/// indirection (PLAN G349 U2): A4's own walk and A5's observation, each a destruction walk's visitor too, through which the summary's Drop
/// atoms are walked (<see cref="MirTypes.Below"/>).</summary>
internal interface IMirSummaryReader
{
    /// <summary>Folds a declaration node's classes into the reader's answer.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Whether its atoms still matter to the answer.</returns>
    bool Summary(int node);

    /// <summary>An atom the walk does not take: one that is no destruction (a requirement or a call through its Type), or one whose
    /// instance is null, a substitution that did not complete, which the reader reads as its Limit (DECISIONS R4-M139).</summary>
    /// <param name="type">The atom's instance, or null.</param>
    /// <param name="atom">The atom.</param>
    void Effect(BoundType? type, in MirEffects.Atom atom);
}

// A4's declaration destruction nodes, each declaration's one destruction summary (PLAN G349; SPEC 16.3.3, 15.3.5, 15.6.4, 15.6.6;
// DECISIONS R4-M172, R4-M173): per struct or enum declaration met below an indirection, one node of the run's worklist whose summary is
// the destruction of the declaration's Type in its own parameters (TyCtxt.DeclarationType), evaluated under no definition's premises
// (Evaluate): classes, Drops where it runs a declared drop, and atoms over its own parameters, Drop(X) for an abstract part and Drop(obj/X)
// for a handle over an abstract payload, whose Sealed is decided at use (8.7.2; Destroy).
// Input: a struct or enum instance the destruction walk declares below an indirection (MirTypes.Below, IMirDestruction.Declared) and its
// reader: A4's Glue, or A5's observation after A4's Run (MirBorrows.Accesses).
// Output: the composition (Compose): the declaration's node, its classes to the reader (Summary, Classes) and its atoms in the instance
// its arguments make (TyCtxt.Arguments, TyCtxt.SummaryInstance), a Drop atom walked below the indirection at the instance's depth, any
// other, or one whose instance is null, to the reader's Effect.
// Invariants: one node per declaration and run, never per instance or meeting, so a growing instance needs no depth and the answer is the
// same in every part order; a node made after Run (an A5 reader's) is solved at once and has no keys, which is safe because no Keys query
// after Run meets it (Keys re-walks Run's items) and no A5 caller holds a Keys row across an observation (MirBorrows.Effects' Met and
// Destroying never reach Destruction); during an at-once solve only declaration nodes evaluate and no Select runs, so restoring the
// selection width restores the caller's view; its storage is the run's pooled arrays (a warm version allocates nothing).
// Must not: decide a premise of its own parameters, compose an instance above an indirection (inline depth keeps MirDepths' bound), read
// an atom's instance by its storage (only MirTypes.Below walks it), or give a body view's summary the Drops class.
internal sealed partial class MirEffects
{
    private readonly Dictionary<BindingSymbol, int> declarationNodes = new(ReferenceEqualityComparer.Instance); // Each declaration's node.
    private BoundType[] declarationTypes = []; // Each declaration node's Type in its own parameters, by its node after the body views'.
    private int bodyNodes; // The run's body view nodes; each declaration node the run adds follows them (DeclarationNode).

    /// <summary>Composes the destruction summary of a struct or enum instance below an indirection (SPEC 16.3.3, 15.3.5; PLAN G349 U2):
    /// its declaration's node, the reader's <see cref="IMirSummaryReader.Summary"/>, then, where the atoms matter, each atom read once in
    /// the instance the instance's arguments make (<see cref="TyCtxt.Arguments"/>, <see cref="TyCtxt.SummaryInstance"/>): a Drop atom with
    /// an instance walked below the indirection at the instance's own depth (<see cref="MirTypes.Below"/>; composition adds no level), any
    /// other atom, or one whose instance is null, to the reader's <see cref="IMirSummaryReader.Effect"/>.</summary>
    /// <typeparam name="TReader">The reader's struct type.</typeparam>
    /// <param name="instance">The instance.</param>
    /// <param name="depth">Its depth in the walk.</param>
    /// <param name="reader">The reader.</param>
    internal void Compose<TReader>(BoundType instance, int depth, ref TReader reader)
        where TReader : struct, IMirDestruction, IMirSummaryReader
    {
        var node = this.DeclarationNode(instance.Symbol!);
        if (!reader.Summary(node))
        {
            return;
        }

        var (subst, start, end) = (types.Arguments(instance), this.atomStarts[node], this.atomStarts[node] + this.atomCounts[node]);
        for (var a = start; a < end; a++)
        {
            var atom = this.atoms[a];
            var type = types.SummaryInstance(atom.Type, subst);
            if (type is not null && atom.Kind == Drop)
            {
                _ = MirTypes.Below(type, ref reader, depth);
            }
            else
            {
                reader.Effect(type, atom);
            }
        }
    }

    /// <summary>Gets a declaration node's classes as its last solve left them, <see cref="Drops"/> included (A5's observation).</summary>
    /// <param name="node">The node <see cref="Compose"/> passed its reader.</param>
    /// <returns>The classes.</returns>
    internal byte Classes(int node) => this.classes[node];

    // PLAN G349 (SPEC 16.3.3, 15.3.5, 15.6.4): the destruction node of a struct or enum declaration, its summary in its own parameters,
    // added and queued once per run where an instance below an indirection is first met; one met outside the solve (an obligation judged,
    // an A5 reader) is solved at once, the caller's state and selected view kept (the file's invariants).
    private int DeclarationNode(BindingSymbol declaration)
    {
        if (this.declarationNodes.TryGetValue(declaration, out var known))
        {
            return known;
        }

        var node = this.nodes++;
        this.declarationNodes.Add(declaration, node);
        MirDataflow.Grow(ref this.classes, this.nodes);
        MirDataflow.Grow(ref this.atomStarts, this.nodes);
        MirDataflow.Grow(ref this.atomCounts, this.nodes);
        MirDataflow.Grow(ref this.readers, this.nodes);
        MirDataflow.Grow(ref this.dependentHeads, this.nodes);
        MirDataflow.Grow(ref this.queued, (this.nodes + 63) >> 6);
        MirDataflow.Grow(ref this.declarationTypes, this.nodes - this.bodyNodes);
        MirDataflow.Grow(ref this.keys, this.nodes * this.keyWords); // Keys are composed once Spread solved them (a node met while judging).
        this.keys.AsSpan(node * this.keyWords, this.keyWords).Clear();
        (this.classes[node], this.atomStarts[node], this.atomCounts[node], this.readers[node], this.dependentHeads[node]) = (0, 0, 0, -1, -1);
        this.declarationTypes[node - this.bodyNodes] = types.DeclarationType(declaration);
        this.queued[node >> 6] &= ~(1UL << node);
        this.Enqueue(node);
        if (this.reader < 0)
        {
            var (found, definition, width) = (this.found, this.definition, cases.Width);
            (this.glued, this.spare) = (this.spare, this.glued);
            this.Solve();
            (this.glued, this.spare) = (this.spare, this.glued);
            (this.found, this.definition, this.reader, this.scratchCount) = (found, definition, -1, 0);
            cases.Width = width;
        }

        return node;
    }
}
