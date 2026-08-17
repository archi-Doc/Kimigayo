// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class TypeSemanticsKoto : Koto
{
    public override KotoKind Akind => KotoKind.TypeSemantics;

    private readonly TokenKind tokenKind;
    private readonly string? identifier;
    private readonly SemanticsForm semanticsForm;

    public SemanticsKind SemanticsKind { get; }

    public string? SemanticsParameter { get; }

    public string Identifier
        => this.tokenKind.IsPrimitiveType()
            ? this.tokenKind.ToText()
            : this.identifier ?? string.Empty;

    internal TypeSemanticsKoto(
        ref TokenReader reader,
        SourceRange range,
        Token typeToken,
        SemanticsKind semanticsKind,
        string? semanticsParameter,
        SemanticsForm semanticsForm)
        : base(ref reader, range)
    {
        this.tokenKind = typeToken.Kind;
        this.SemanticsKind = semanticsKind;
        this.SemanticsParameter = semanticsParameter;
        this.semanticsForm = semanticsForm;

        if (!this.tokenKind.IsPrimitiveType())
        {
            this.identifier = reader.GetSpan(typeToken).ToString();
        }
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        if (this.semanticsForm == SemanticsForm.Slash)
        {
            builder.Append(Constants.SlashChar);
        }
        else if (this.semanticsForm == SemanticsForm.Named)
        {
            if (this.SemanticsKind == SemanticsKind.Parameter)
            {
                builder.Append(this.SemanticsParameter);
            }
            else
            {
                builder.Append(this.SemanticsKind.ToText());
            }

            builder.Append(Constants.SlashChar);
        }

        builder.Append(this.Identifier);
    }

    public override (string Text, Koto[]? Children) Dump()
        => ($"{this.GetType().Name}({this.SemanticsKind}, {this.Identifier})", default);
}

internal enum SemanticsForm : byte
{
    None,
    Named,
    Slash,
}
