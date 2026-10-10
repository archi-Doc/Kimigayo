// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Numbers the syntax of one module with dense <see cref="Koto.SyntaxId"/> values, the keys of its semantic tables: the nodes
/// reachable from the module root hold 1 to <see cref="ParsedCount"/> in the pre-order of <see cref="Koto.VisitChildren"/>, and a
/// node outside that tree, such as one Binding synthesizes, takes the next id at its first semantic write (<see cref="EnsureId"/>).
/// Id 0 is no node.
/// </summary>
/// <remarks>
/// Binding numbers the tree at the start of a pass, and only when <see cref="Version"/> changed: parsing into the module and
/// every edit outside Binding change it, Binding's own normalization does not, so a warm rebind keeps every id. Renumbering keeps
/// the ids of nodes outside the tree after the reachable range in their relative order, takes over the nodes of a subtree that
/// another module numbered, and clears the id of a parsed node that left the tree; an id the table owns always names its node.
/// </remarks>
public sealed class SyntaxTable
{
    private static readonly Koto?[] Unnumbered = new Koto?[1];
    private Koto?[] nodes = Unnumbered;
    private Numbering? numbering;
    private int numberedVersion;

    /// <summary>Gets the version of the module's syntax; it changes with every parse into the module and every edit outside Binding.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>Gets the number of nodes the latest numbering reached from the root; they hold ids 1 to this count.</summary>
    public int ParsedCount { get; private set; }

    /// <summary>Gets the number of ids in use, the ids of nodes outside the tree included.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the node of each id from 0 (no node) to <see cref="Count"/>. A node that another module took over keeps its
    /// entry here, but its <see cref="Koto.SyntaxOwner"/> names that module's table.</summary>
    public ReadOnlySpan<Koto?> Nodes => this.nodes.AsSpan(0, this.Count + 1);

    /// <summary>Gets the id of a node, giving a node without one the next id of its module's table.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The id in <see cref="Koto.SyntaxOwner"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int EnsureId(Koto node)
    {
        if (node.SyntaxOwner is null)
        {
            node.Kotonoha.Syntax.Allocate(node);
        }

        return node.SyntaxId;
    }

    /// <summary>Records that the module's syntax changed, so the next pass renumbers it.</summary>
    internal void Edit() => this.Version++;

    /// <summary>Gives a node the next id.</summary>
    /// <param name="node">A node without an id, or one the current numbering has not reached.</param>
    internal void Allocate(Koto node)
    {
        var id = ++this.Count;
        if (id == this.nodes.Length)
        {
            Array.Resize(ref this.nodes, Math.Max(64, id * 2));
        }

        this.nodes[id] = node;
        node.SyntaxOwner = this;
        node.SyntaxId = id;
    }

    /// <summary>Numbers the tree under the module root when the syntax changed since the latest numbering.</summary>
    /// <param name="root">The module root.</param>
    internal void Number(Koto root)
    {
        if (this.numberedVersion == this.Version)
        {
            return;
        }

        this.numberedVersion = this.Version;
        var (previous, previousCount, previousParsed) = (this.nodes, this.Count, this.ParsedCount);
        var count = (this.numbering ??= new(this)).Run(root, previousCount, previousCount - previousParsed);
        this.Count = this.ParsedCount = count;
        for (var id = 1; id <= previousCount; id++)
        {
            var node = previous[id]!;
            if (!ReferenceEquals(node.SyntaxOwner, this) || (node.SyntaxId <= count && ReferenceEquals(this.nodes[node.SyntaxId], node)))
            {
                continue; // Taken over by another module, or reached by the walk.
            }

            if (id > previousParsed)
            {
                this.Allocate(node);
            }
            else
            {
                node.SyntaxOwner = null;
                node.SyntaxId = 0;
            }
        }
    }

    private sealed class Numbering(SyntaxTable table) : KotoVisitor
    {
        private Koto?[] buffer = [];
        private int count;

        public override void Visit(Koto node)
        {
            if (++this.count == this.buffer.Length)
            {
                this.buffer = this.MoveTo(ArrayPool<Koto?>.Shared.Rent(this.count * 2), this.count);
            }

            this.buffer[this.count] = node;
            node.SyntaxOwner = table;
            node.SyntaxId = this.count;
            node.VisitChildren(this);
        }

        // The walk fills a pooled buffer, so the table keeps one array: the tree, then room for the nodes outside it.
        internal int Run(Koto root, int previousCount, int room)
        {
            this.buffer = ArrayPool<Koto?>.Shared.Rent(Math.Max(256, previousCount + 1));
            this.count = 0;
            this.Visit(root);
            table.nodes = this.MoveTo(new Koto?[this.count + room + 1], this.count + 1);
            this.buffer = [];
            return this.count;
        }

        // Copies the used part of the buffer and returns the buffer, cleared, to the pool.
        private Koto?[] MoveTo(Koto?[] destination, int length)
        {
            var used = this.buffer.AsSpan(0, length);
            used.CopyTo(destination);
            used.Clear();
            ArrayPool<Koto?>.Shared.Return(this.buffer);
            return destination;
        }
    }
}
