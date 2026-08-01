// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class IdentifierNameKoto : Koto
{
    public static readonly IdentifierNameKoto Error;

    static IdentifierNameKoto()
    {
        Error = IdentifierNameKoto.UnsafeConstructor();
    }

    [Key(1)]
    public string Identifier { get; private set; }

    public IdentifierNameKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Identifier = token.Text.ToString();
    }

    public override string ToString()
        => $"{this.Identifier}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            KotoParser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(this.Identifier);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.Identifier})", default);
    }
}
