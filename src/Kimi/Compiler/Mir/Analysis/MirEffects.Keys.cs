// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A4's static keys for A5 (SPEC 15.6.4, 16.3.3, 22.4.4; DECISIONS R4-M38): the mutable static Fields a Call or a Drop may access through
// what it runs, from the summaries Run solved. Under the owner's P5 every key of one Field is one key, so keys are Fields, never
// substituted: a node's keys are its local accesses joined with those of every body view it composes.
internal sealed partial class MirEffects
{
    private readonly List<(BindingSymbol Field, bool Write)> accessed = []; // The last query's keys.

    /// <summary>Gets the mutable static Fields a Call or a Drop of a body view may access through what it runs, from the summaries of
    /// the last <see cref="Run"/>: its terminator's inputs, as <see cref="Local"/> records them, evaluated against them, each Field once,
    /// written where any access writes it. What the site runs and A4 does not classify (an unknown effect, an atom left abstract under
    /// the body's premises, a limit, or a dependent of an error) makes it unknown, so it may access any key.</summary>
    /// <param name="body">A body of the last <see cref="Run"/>'s program, with which the dataflow is begun.</param>
    /// <param name="view">The view, which the case views select.</param>
    /// <param name="block">The block whose terminator is the Call or the Drop.</param>
    /// <param name="unknown">Whether the site may access any key.</param>
    /// <returns>The keys, in a buffer the next query reuses; none for another terminator.</returns>
    internal ReadOnlySpan<(BindingSymbol Field, bool Write)> Keys(MirBody body, long view, int block, out bool unknown)
    {
        cases.Select(body, view);
        var (items, edges) = (this.itemCount, this.edgeCount);
        (this.definition, this.reader, this.found, this.scratchCount) = (body.Key.Decl, -1, 0, 0);
        this.glued.Clear();
        this.keyScratch.AsSpan(0, this.keyWords).Clear();
        this.Terminator(this.program, body, block, view, body.Blocks[block].Terminator);
        this.Inputs(items, this.itemCount, edges, this.edgeCount);
        (this.itemCount, this.edgeCount) = (items, edges); // The site's inputs are not the body's.
        unknown = (this.found & (Unknown | Limit | Derived)) != 0; // With no reader an atom left abstract is Unknown (AddAtom).
        this.accessed.Clear();
        for (var word = 0; word < this.keyWords; word++)
        {
            for (var bits = this.keyScratch[word]; bits != 0; bits &= bits - 1)
            {
                // A read bit beside its write bit is the write's key.
                var bit = (word << 6) + BitOperations.TrailingZeroCount(bits);
                if ((bit & 1) != 0 || (bits & (2UL << (bit & 63))) == 0)
                {
                    this.accessed.Add((this.fields[bit >> 1], (bit & 1) != 0));
                }
            }
        }

        return CollectionsMarshal.AsSpan(this.accessed);
    }
}
