// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class UnresolvedKoto : Koto
{
    public static readonly UnresolvedKoto Error;

    static UnresolvedKoto()
    {
        Error = UnresolvedKoto.UnsafeConstructor();
    }

    [Key(1)]
    public string Identifier { get; private set; }

    public UnresolvedKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Identifier = token.Text.ToString();
    }

    public override string ToString()
        => $"{this.Identifier}";

    public override void WriteTo(ref IndentedStringBuilder writer)
    {
        writer.Append(this.Identifier);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.Identifier})", default);
    }
}
