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
public sealed class PropertyAccessorKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.PropertyAccessor;

    /// <summary>Gets the accessor access restriction.</summary>
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets the accessor kind.</summary>
    public PropertyAccessorKind AccessorKind { get; private set; }

    /// <summary>Gets the custom accessor body, or <see langword="null"/> for a bodyless accessor.</summary>
    public Koto? Body { get; private set; }

    /// <summary>Gets the explicitly declared result Type and Origin, if present.</summary>
    public Koto? ReturnType { get; private set; }

    /// <summary>Gets the explicit self parameter Type, or null for a receiverless signature.</summary>
    public Koto? ReceiverType { get; private set; }

    /// <summary>Gets the setter value parameter Type.</summary>
    public Koto? ValueType { get; private set; }

    /// <summary>Gets a value indicating whether parentheses explicitly declare an accessor signature.</summary>
    public bool HasExplicitSignature { get; }

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
    /// <param name="returnType">The explicit result Type, if present.</param>
    /// <param name="hasExplicitSignature">Whether a parameter list was written.</param>
    /// <param name="receiverType">The explicit self Type, if present.</param>
    /// <param name="valueType">The setter input Type, if present.</param>
    public PropertyAccessorKoto(
        ref TokenReader reader,
        SourceSpan range,
        ModifierKind modifier,
        PropertyAccessorKind accessorKind,
        Koto? body,
        Koto? returnType = null,
        bool hasExplicitSignature = false,
        Koto? receiverType = null,
        Koto? valueType = null)
        : base(ref reader, range)
    {
        this.Modifier = modifier;
        this.AccessorKind = accessorKind;
        this.Body = body;
        this.ReturnType = returnType;
        this.HasExplicitSignature = hasExplicitSignature;
        this.ReceiverType = receiverType;
        this.ValueType = valueType;
        this.Adopt(receiverType);
        this.Adopt(valueType);
        this.Adopt(returnType);
        this.Adopt(body);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.AccessorText);

        if (this.HasExplicitSignature)
        {
            builder.Append('(');
            if (this.ReceiverType is { } receiverType)
            {
                builder.Append("self: ");
                receiverType.WriteTo(ref builder);
            }

            if (this.ValueType is { } valueType)
            {
                if (this.ReceiverType is not null)
                {
                    builder.AppendCommaAndSpace();
                }

                builder.Append("value: ");
                valueType.WriteTo(ref builder);
            }

            builder.Append(')');
        }

        if (this.ReturnType is { } returnType)
        {
            builder.Append(" -> ");
            returnType.WriteTo(ref builder);
        }

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
        if (this.ReceiverType is { } receiverType)
        {
            yield return receiverType;
        }

        if (this.ValueType is { } valueType)
        {
            yield return valueType;
        }

        if (this.ReturnType is { } returnType)
        {
            yield return returnType;
        }

        if (this.Body is { } body)
        {
            yield return body;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.ReceiverType == oldKoto)
        {
            this.ReceiverType = newKoto;
            return true;
        }

        if (this.ValueType == oldKoto)
        {
            this.ValueType = newKoto;
            return true;
        }

        if (this.ReturnType == oldKoto)
        {
            this.ReturnType = newKoto;
            return true;
        }

        if (this.Body != oldKoto)
        {
            return false;
        }

        this.Body = newKoto;
        return true;
    }
}
