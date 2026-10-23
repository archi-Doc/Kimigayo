// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

// Owns structural attachment, source provenance and syntax edits. Inputs are child slots and detached replacements;
// outputs are parent links and edit notifications. Replacements cannot have another parent; binding owns semantic rebuilding.
public abstract partial class Koto
{
    /// <summary>Gets the diagnostic destination associated with this node.</summary>
    public DiagnosticCollection? DiagnosticCollection => this.CodeContext?.DiagnosticCollection;

    /// <summary>Gets the node span in the source document.</summary>
    public SourceSpan Span { get; internal set; }

    /// <summary>Gets the code context that owns this node.</summary>
    public CodeContext CodeContext { get; internal set; }

    /// <summary>Gets the parent node, or <see langword="null"/> for the root.</summary>
    public Koto? Parent { get; internal set; }

    /// <summary>Gets the table that numbers this node, or <see langword="null"/> while the node has no id.</summary>
    public SyntaxTable? SyntaxOwner { get; internal set; }

    /// <summary>Gets the dense id of this node in <see cref="SyntaxOwner"/>; 0 is none.</summary>
    public int SyntaxId { get; internal set; }

    /// <summary>Removes an attribute from this node.</summary>
    /// <param name="attributeKoto">The attribute to remove.</param>
    /// <returns><see langword="true"/> when the attribute was removed.</returns>
    public bool RemoveAttribute(AttributeKoto attributeKoto)
    {
        Koto previous = this;
        var current = this.AttributeChain;
        while (current is not null)
        {
            if (current == attributeKoto)
            {
                var next = current.AttributeChain;
                if (previous == this)
                {
                    this.AttributeChain = next;
                }
                else
                {
                    previous.AttributeChain = next;
                }

                if (next is not null)
                {
                    next.Parent = previous;
                }

                current.Parent = default;
                current.AttributeChain = default;
                this.CodeContext.Compilation.NoteSyntaxEdit(this);
                return true;
            }

            previous = current;
            current = current.AttributeChain;
        }

        return false;
    }

    /// <summary>Attaches an attribute chain and links its parents.</summary>
    /// <param name="attributeChain">The attribute chain, or <see langword="null"/>.</param>
    internal void SetAttributeChain(AttributeKoto? attributeChain)
    {
        this.AttributeChain = attributeChain;
        Koto parent = this;
        while (attributeChain is not null)
        {
            attributeChain.Parent = parent;
            parent = attributeChain;
            attributeChain = attributeChain.AttributeChain;
        }
    }

    internal bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (newKoto.Parent is not null)
        {
            throw new InvalidOperationException("A replacement must be detached.");
        }

        if (this.AttributeChain == oldKoto && newKoto is AttributeKoto attribute)
        {
            // Only the head is replaced; the remaining attributes stay attached behind it.
            if (attribute.AttributeChain is null && oldKoto.AttributeChain is { } rest)
            {
                oldKoto.AttributeChain = null;
                attribute.AttributeChain = rest;
                rest.Parent = attribute;
            }

            this.AttributeChain = attribute;
        }
        else
        {
            // The attribute chain is replaced above as a chain; every other slot is replaced by the walk.
            var slots = new ChildSlots(oldKoto, newKoto);
            slots.List(OriginClauses.Get(this));
            this.ForEachChildSlot(ref slots);
            if (!slots.Replaced)
            {
                return false;
            }
        }

        oldKoto.Parent = default;
        newKoto.Parent = this;
        this.CodeContext.Compilation.NoteSyntaxEdit(this);
        return true;
    }

    /// <summary>Replaces a known child slot in constant time, retaining source and attribute provenance.</summary>
    /// <typeparam name="T">The child slot type.</typeparam>
    /// <param name="list">The owned mutable array or list.</param>
    /// <param name="index">The already known child index.</param>
    /// <param name="replacement">The detached replacement node.</param>
    protected void ReplaceAt<T>(IReadOnlyList<T> list, int index, T replacement)
        where T : Koto
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var current = list[index];
        if (ReferenceEquals(current, replacement))
        {
            return;
        }

        if (list is not IList<T> mutable || (mutable.IsReadOnly && list is not T[]))
        {
            throw new InvalidOperationException("The child storage is not mutable.");
        }

        if (replacement.Parent is not null)
        {
            throw new InvalidOperationException("A replacement must be detached.");
        }

        if (replacement.AttributeChain is not null && current.AttributeChain is not null)
        {
            throw new InvalidOperationException("A replacement cannot overwrite an existing attribute chain.");
        }

        replacement.CodeContext = current.CodeContext;
        replacement.Span = current.Span;
        if (current.AttributeChain is { } attributes)
        {
            current.AttributeChain = null;
            replacement.SetAttributeChain(attributes);
        }

        mutable[index] = replacement;
        current.Parent = null;
        replacement.Parent = this;
        // A rewrite invalidates facts of the owner; a later Bind rebuilds dependent facts.
        this.BindingState = BindingState.Unvisited;
        this.BoundMeaning = null;
        this.BoundSymbol = null;
        this.BindingFailure = BindingFailure.None;
        this.CodeContext.Compilation.NoteSyntaxEdit(this);
    }

    /// <summary>Sets this node as the parent of a child.</summary>
    /// <param name="child">The child node, or <see langword="null"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void Adopt(Koto? child)
    {
        if (child is not null)
        {
            child.Parent = this;
        }
    }

    /// <summary>Sets this node as the parent of each child in a list.</summary>
    /// <typeparam name="T">The child node type.</typeparam>
    /// <param name="children">The child nodes, or <see langword="null"/>.</param>
    protected void Adopt<T>(List<T>? children)
        where T : Koto
    {
        if (children is null)
        {
            return;
        }

        foreach (var child in children)
        {
            child.Parent = this;
        }
    }

    /// <summary>Sets this node as the parent of an array or list of child nodes.</summary>
    /// <param name="children">The child nodes.</param>
    protected void Adopt(IReadOnlyList<Koto>? children)
    {
        if (children is Koto[] array)
        {
            foreach (var child in array)
            {
                child.Parent = this;
            }
        }
        else if (children is List<Koto> list)
        {
            this.Adopt(list);
        }
        else if (children is not null)
        {
            for (var i = 0; i < children.Count; i++)
            {
                children[i].Parent = this;
            }
        }
    }
}
