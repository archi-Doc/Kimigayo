// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a source-level Property declaration.</summary>
public sealed class PropertyKoto : VariableKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Property;

    private List<PropertyAccessorKoto>? accessors;

    /// <summary>Gets a value indicating whether the accessors use the inline <c>has</c> form.</summary>
    public bool HasInlineAccessors { get; private set; }

    /// <summary>Gets a value indicating whether this is a contract accessor requirement.</summary>
    public bool IsContractRequirement { get; internal set; }

    /// <summary>Gets the explicit accessors in source order.</summary>
    public IReadOnlyList<PropertyAccessorKoto> Accessors
        => (IReadOnlyList<PropertyAccessorKoto>?)this.accessors ?? [];

    /// <summary>Initializes a new instance of the <see cref="PropertyKoto"/> class.</summary>
    /// <remarks>The modifiers and attribute chain are taken from the reader's current context.</remarks>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The <c>let</c> or <c>var</c> token.</param>
    /// <param name="nameKoto">The declared name.</param>
    /// <param name="typeKoto">The declared Type, if specified.</param>
    /// <param name="initializerKoto">The initializer expression, if present.</param>
    /// <param name="hasInlineAccessors">Whether the declaration uses the inline <c>has</c> form.</param>
    public PropertyKoto(
        ref TokenReader reader,
        Token token,
        IdentifierNameKoto nameKoto,
        Koto? typeKoto,
        Koto? initializerKoto,
        bool hasInlineAccessors)
        : base(ref reader, token, nameKoto, typeKoto, initializerKoto)
    {
        this.HasInlineAccessors = hasInlineAccessors;
    }

    /// <summary>Gets the explicit accessor of the requested kind, if present.</summary>
    /// <param name="kind">The accessor kind.</param>
    /// <returns>The accessor, or <see langword="null"/> when it is not declared.</returns>
    public PropertyAccessorKoto? GetAccessor(PropertyAccessorKind kind)
    {
        if (this.accessors is { } accessors)
        {
            foreach (var accessor in accessors)
            {
                if (accessor.AccessorKind == kind)
                {
                    return accessor;
                }
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        base.WriteTo(ref builder);

        if (this.accessors is not { Count: > 0 } accessors)
        {
            return;
        }

        if (this.HasInlineAccessors)
        {
            builder.AppendSpace();
            builder.Append(Constants.HasKeyword);
            builder.AppendSpace();
            for (var i = 0; i < accessors.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                accessors[i].WriteTo(ref builder);
            }

            return;
        }

        builder.AppendLine();
        builder.IncrementIndent();
        for (var i = 0; i < accessors.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            accessors[i].WriteTo(ref builder);
        }

        builder.DecrementIndent();
    }

    /// <summary>Adds an explicit accessor unless that kind is already present.</summary>
    /// <param name="accessor">The accessor to add.</param>
    /// <returns><see langword="true"/> when the accessor was added.</returns>
    internal bool TryAddAccessor(PropertyAccessorKoto accessor)
    {
        if (this.GetAccessor(accessor.AccessorKind) is not null)
        {
            return false;
        }

        (this.accessors ??= []).Add(accessor);
        accessor.Parent = this;
        this.CompleteSpan(accessor.Span.End);
        return true;
    }

    /// <summary>Extends the Property span through its accessor block.</summary>
    /// <param name="end">The end of the accessor block.</param>
    internal void CompleteSpan(int end)
        => this.Span = SourceSpan.FromBounds(this.Span.Start, Math.Max(this.Span.End, end));

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var child in base.GetChildNodes())
        {
            yield return child;
        }

        if (this.accessors is not null)
        {
            foreach (var accessor in this.accessors)
            {
                yield return accessor;
            }
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
        => base.ReplaceChildCore(oldKoto, newKoto) ||
            (oldKoto is PropertyAccessorKoto && ReplaceInList(this.accessors, oldKoto, newKoto));
}
