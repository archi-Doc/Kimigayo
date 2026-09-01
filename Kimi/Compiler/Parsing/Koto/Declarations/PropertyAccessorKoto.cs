// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Identifies a Property accessor kind.</summary>
public enum PropertyAccessorKind : byte
{
    /// <summary>A getter.</summary>
    Get,

    /// <summary>A setter.</summary>
    Set,
}

/// <summary>Represents a Property accessor declaration.</summary>
[TinyhandObject]
public sealed partial class PropertyAccessorKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PropertyAccessor;

    /// <summary>Gets the accessor access restriction.</summary>
    [Key(1)]
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets the accessor kind.</summary>
    [Key(2)]
    public PropertyAccessorKind AccessorKind { get; private set; }

    /// <summary>Gets the custom accessor body, or <see langword="null"/> for a bodyless accessor.</summary>
    [Key(3)]
    public Koto? Body { get; private set; }

    /// <summary>Gets a value indicating whether the accessor has no custom body.</summary>
    public bool IsBodyless => this.Body is null;

    /// <summary>Gets the source keyword for this accessor.</summary>
    public string AccessorText => this.AccessorKind == PropertyAccessorKind.Get ? Constants.GetKeyword : Constants.SetKeyword;

    /// <summary>Initializes a new instance of the <see cref="PropertyAccessorKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete accessor span.</param>
    /// <param name="modifier">The accessor access restriction.</param>
    /// <param name="accessorKind">The accessor kind.</param>
    /// <param name="body">The custom body, if present.</param>
    public PropertyAccessorKoto(
        ref TokenReader reader,
        SourceSpan range,
        ModifierKind modifier,
        PropertyAccessorKind accessorKind,
        Koto? body)
        : base(ref reader, range)
    {
        this.Modifier = modifier;
        this.AccessorKind = accessorKind;
        this.Body = body;
        if (body is not null)
        {
            body.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
        => this.Body?.Bind(compilation);

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.AccessorText);

        if (this.Body is CodeBlockKoto block)
        {
            block.WriteIndentedTo(ref builder);
        }
        else if (this.Body is not null)
        {
            builder.Append(" => ");
            this.Body.WriteTo(ref builder);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.Body is not null)
        {
            yield return this.Body;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Body != oldKoto)
        {
            return false;
        }

        this.Body = newKoto;
        return true;
    }
}
