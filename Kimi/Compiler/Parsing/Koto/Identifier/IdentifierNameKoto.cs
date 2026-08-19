// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class IdentifierNameKoto : Koto
{
    public override KotoKind Akind => KotoKind.IdentifierName;

    public static readonly IdentifierNameKoto Error;

    static IdentifierNameKoto()
    {
        Error = IdentifierNameKoto.UnsafeConstructor();
    }

    public static bool TryCreate(ref TokenReader reader, Token token, [MaybeNullWhen(false)] out IdentifierNameKoto koto)
    {
        var identifierName = reader.GetSpan(token).ToString();
        if (token.Kind.IsIdentifierOrContextualKeyword() &&
            IdentifierHelper.IsValidIdentifier(identifierName))
        {
            koto = new(ref reader, token, identifierName);
            return true;
        }
        else
        {
            reader.AddDiagnostic(DiagnosticCode.InvalidIdentifier_Kd, identifierName);

            koto = default;
            return false;
        }
    }

    [Key(1)]
    public string IdentifierName { get; private set; }

    protected IdentifierNameKoto(ref TokenReader reader, Token token, string identifierName)
        : base(ref reader, token.SourceSpan)
    {
        this.IdentifierName = identifierName;
    }

    public override string ToString()
        => $"{this.IdentifierName}";

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(this.IdentifierName);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.IdentifierName})", default);
    }
}
