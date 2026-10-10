// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// The one walk over the child slots of a node, in source order: it visits them, collects them, or replaces the slot that
/// holds one child. A node lists its slots once (<see cref="Koto.ForEachChildSlot"/>), writing each back as
/// <c>this.Child = slots.Slot(this.Child)</c>; outside a replacement the slot keeps its child.
/// </summary>
public ref struct ChildSlots
{
    private readonly KotoVisitor? visitor;
    private readonly List<Koto>? collected;
    private readonly Koto? original;
    private readonly Koto? replacement;

    internal ChildSlots(KotoVisitor visitor) => this.visitor = visitor;

    internal ChildSlots(List<Koto> collected) => this.collected = collected;

    internal ChildSlots(Koto original, Koto replacement)
    {
        this.original = original;
        this.replacement = replacement;
    }

    /// <summary>Gets the replacement child while a replacement is pending, so a slot can refuse a form it cannot hold; otherwise null.</summary>
    public readonly Koto? Replacement => this.Replaced ? null : this.replacement;

    /// <summary>Gets a value indicating whether this walk has replaced a child.</summary>
    public bool Replaced { readonly get; private set; }

    internal readonly KotoVisitor? Visitor => this.visitor;

    internal readonly List<Koto>? Collected => this.collected;

    internal readonly Koto? Original => this.original;

    /// <summary>Walks one slot.</summary>
    /// <typeparam name="T">The slot type; a replacement of another type leaves the slot unchanged.</typeparam>
    /// <param name="child">The child the slot holds, or null.</param>
    /// <returns>The child the slot holds after the walk.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Slot<T>(T child)
        where T : Koto?
    {
        if (this.visitor is { } visitor)
        {
            if (child is not null)
            {
                visitor.Visit(child);
            }

            return child;
        }

        return this.Other(child);
    }

    /// <summary>Walks a list of slots; a replacement needs a writable list or an array.</summary>
    /// <typeparam name="T">The element type; a replacement of another type leaves the list unchanged.</typeparam>
    /// <param name="children">The children, or null.</param>
    public void List<T>(IReadOnlyList<T>? children)
        where T : Koto
    {
        if (children is null)
        {
            return;
        }

        if (this.visitor is { } visitor)
        {
            for (var i = 0; i < children.Count; i++)
            {
                visitor.Visit(children[i]);
            }
        }
        else if (this.collected is { } collected)
        {
            for (var i = 0; i < children.Count; i++)
            {
                collected.Add(children[i]);
            }
        }
        else if (this.Replacement is T replacement && children is IList<T> mutable && (!mutable.IsReadOnly || children is T[]))
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], this.original))
                {
                    mutable[i] = replacement;
                    this.Replaced = true;
                    return;
                }
            }
        }
    }

    // Transitional (R2a U2a): a declaration that replaced a child through its earlier override.
    internal void MarkReplaced() => this.Replaced = true;

    private T Other<T>(T child)
        where T : Koto?
    {
        if (child is null)
        {
            return child;
        }

        if (this.collected is { } collected)
        {
            collected.Add(child);
        }
        else if (ReferenceEquals(child, this.original) && this.Replacement is T replacement)
        {
            this.Replaced = true;
            return replacement;
        }

        return child;
    }
}
