// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class FunctionTypeKoto : Koto
{
    public override KotoKind Akind => KotoKind.FunctionType;

    [Key(1)]
    public Koto Parameters { get; private set; }

    [Key(2)]
    public Koto ReturnType { get; private set; }

    public FunctionTypeKoto(ref TokenReader reader, SourceSpan range, Koto parameters, Koto returnType)
        : base(ref reader, range)
    {
        this.Parameters = parameters;
        this.ReturnType = returnType;
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Parameters.WriteTo(ref builder);
        builder.Append(" -> ");
        this.ReturnType.WriteTo(ref builder);
    }
}
