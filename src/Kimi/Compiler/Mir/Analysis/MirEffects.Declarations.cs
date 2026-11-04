// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A4's declaration destruction nodes (PLAN G349; SPEC 16.3.3, 15.3.5, 15.6.4): per struct or enum declaration met below an indirection (a
// Sealed handle's payload, an Array's or a Dictionary's element: MirTypes' IMirDestruction.Below, Glue.Below), one node of the run's worklist
// whose summary is the destruction of the declaration's Type in its own parameters (TyCtxt.DeclarationType), evaluated under no definition's
// premises (Evaluate): classes and atoms over its own parameters, Drop(X) for an abstract part and Drop(obj/X) for a handle over an abstract
// payload, whose Sealed is decided at use (8.7.2; Destroy).
// Input: a struct or enum instance the destruction walk enters below an indirection (Glue.First) and the substitution its Type arguments
// make of the declaration's slots (TyCtxt.Arguments).
// Output: the declaration's node, numbered after the run's body view nodes and queued once per run, and its summary composed into the
// current evaluation through the instance's arguments (Read), with the reader recorded so the least fixed point covers both node kinds.
// Invariants: one node per declaration and run, never per instance or meeting, so a growing instance needs no depth and the answer is the
// same in every part order; the evaluated declaration's own Type at its root is walked, not composed; a node met while an obligation is
// judged is solved at once with the judged row's state kept; its storage is the run's pooled arrays (a warm version allocates nothing).
// Must not: decide a premise of its own parameters, compose an instance above an indirection (inline depth keeps MirDepths' bound), or give
// its atoms a storage verdict of their own (they are the instance's stored Types, as AdtDef reads Fields).
internal sealed partial class MirEffects
{
    private readonly Dictionary<BindingSymbol, int> declarationNodes = new(ReferenceEqualityComparer.Instance); // Each declaration's node.
    private BoundType[] declarationTypes = []; // Each declaration node's Type in its own parameters, by its node after the body views'.
    private int bodyNodes; // The run's body view nodes; each declaration node the run adds follows them (DeclarationNode).
    private bool below; // Whether the walked value lies below an indirection, where every struct or enum instance composes its declaration.

    // PLAN G349 (SPEC 16.3.3, 15.3.5, 15.6.4): the destruction node of a struct or enum declaration, its summary in its own parameters,
    // added and queued once per run where an instance below an indirection is first met; one met while an obligation is judged is solved
    // at once, the judged row's state kept.
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
            var (found, definition, below) = (this.found, this.definition, this.below);
            (this.glued, this.spare) = (this.spare, this.glued);
            this.Solve();
            (this.glued, this.spare) = (this.spare, this.glued);
            (this.found, this.definition, this.below, this.reader, this.scratchCount) = (found, definition, below, -1, 0);
            cases.Deselect();
        }

        return node;
    }

    // PLAN G349 (SPEC 16.3.3, 15.3.5): a struct or enum instance below an indirection, but the evaluated declaration's own Type at its
    // root: its declaration's node composed through the instance's arguments, exact at any depth of the instance.
    private bool Declaration(BoundType type)
    {
        if (!this.below || !(AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) ||
            (this.reader >= this.bodyNodes && ReferenceEquals(type, this.declarationTypes[this.reader - this.bodyNodes])))
        {
            return false;
        }

        this.Read(this.DeclarationNode(type.Symbol!), type.Components.Count == 0 ? null : types.Arguments(type));
        return true;
    }
}
