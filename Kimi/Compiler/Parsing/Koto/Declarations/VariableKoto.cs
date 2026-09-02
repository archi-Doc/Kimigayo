// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Defines whether a variable binding is mutable.
/// </summary>
public enum VariableKind
{
    /// <summary>A mutable variable.</summary>
    Var,

    /// <summary>An immutable binding.</summary>
    Let,
}

/// <summary>
/// Provides the shared <c>let</c>/<c>var</c> declaration storage for local bindings and Properties.
/// </summary>
[TinyhandObject(ReservedKeyCount = 6)]
public abstract partial class VariableKoto : DeclarationKoto
{
    /// <summary>Gets the declaration modifiers.</summary>
    [Key(1)]
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets the variable binding kind.</summary>
    [Key(2)]
    public VariableKind VariableKind { get; private set; }

    /// <summary>Gets the declared name.</summary>
    [Key(3)]
    public IdentifierNameKoto NameKoto { get; private set; }

    /// <summary>Gets the declared type, if specified.</summary>
    [Key(4)]
    public Koto? TypeKoto { get; private set; }

    /// <summary>Gets the initializer expression, if present.</summary>
    [Key(5)]
    public Koto? InitializerKoto { get; private set; }

    /// <summary>Gets the source keyword for the binding kind.</summary>
    public string VariableText => this.VariableKind == VariableKind.Var ? Constants.VarKeyword : Constants.LetKeyword;

    /// <summary>Initializes a new instance of the <see cref="VariableKoto"/> class for deserialization.</summary>
    /// <param name="codeContext">The owning code context.</param>
    internal VariableKoto(CodeContext codeContext)
        : base(codeContext, default)
    {
        this.NameKoto = default!;
    }

    /// <summary>Initializes a new instance of the <see cref="VariableKoto"/> class.</summary>
    /// <remarks>The modifiers and attribute chain are taken from the reader's current context.</remarks>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The <c>let</c> or <c>var</c> token.</param>
    /// <param name="nameKoto">The declared name.</param>
    /// <param name="typeKoto">The declared type, if specified.</param>
    /// <param name="initializerKoto">The initializer expression, if present.</param>
    protected VariableKoto(ref TokenReader reader, Token token, IdentifierNameKoto nameKoto, Koto? typeKoto, Koto? initializerKoto)
        : base(
            ref reader,
            SourceSpan.FromBounds(
                token.Span.Start,
                Math.Max(nameKoto.Span.End, Math.Max(typeKoto?.Span.End ?? 0, initializerKoto?.Span.End ?? 0))))
    {
        this.Modifier = reader.ModifierKind;
        this.VariableKind = token.Kind == TokenKind.Let ? VariableKind.Let : VariableKind.Var;
        this.NameKoto = nameKoto;
        this.TypeKoto = typeKoto;
        this.InitializerKoto = initializerKoto;

        nameKoto.Parent = this;
        this.Adopt(typeKoto);
        this.Adopt(initializerKoto);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendLineFeed);
        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.VariableText);
        builder.AppendSpace();
        this.NameKoto.WriteTo(ref builder);

        if (this.TypeKoto is not null)
        {
            builder.Append(": ");
            this.TypeKoto.WriteTo(ref builder);
        }

        if (this.InitializerKoto is not null)
        {
            builder.Append(" = ");
            this.InitializerKoto.WriteTo(ref builder);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.NameKoto;
        if (this.TypeKoto is not null)
        {
            yield return this.TypeKoto;
        }

        if (this.InitializerKoto is not null)
        {
            yield return this.InitializerKoto;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.NameKoto == oldKoto && newKoto is IdentifierNameKoto name)
        {
            this.NameKoto = name;
        }
        else if (this.TypeKoto == oldKoto)
        {
            this.TypeKoto = newKoto;
        }
        else if (this.InitializerKoto == oldKoto)
        {
            this.InitializerKoto = newKoto;
        }
        else
        {
            return false;
        }

        return true;
    }
}
