// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Tinyhand.Tree;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a numeric literal expression.
/// </summary>
[TinyhandObject]
public sealed partial class NumberLiteralKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.NumberLiteral;

    [Key(1)]
    private NumberLiteralParseResult parseResult;

    [Key(2)]
    private Int128 uv;

    /// <summary>Gets the normalized literal text.</summary>
    public string Literal
    {
        get
        {
            if (field is not null)
            {
                return field;
            }

            if (this.parseResult == NumberLiteralParseResult.I128)
            {
                field = this.uv.ToString();
            }
            else if (this.parseResult == NumberLiteralParseResult.F64)
            {
                field = BitConverter.UInt64BitsToDouble((ulong)this.uv).ToString();
            }
            else
            {
                field = string.Empty;
            }

            return field;
        }
    }

    /// <summary>Initializes a new instance of the <see cref="NumberLiteralKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The numeric literal token.</param>
    public NumberLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Span)
    {
        this.parseResult = NumberLiteralHelper.ParseNumberLiteral(reader.GetSpan(token), out var uv);
        this.uv = uv;
    }

    /// <summary>Attempts to convert the literal to a compile-time value.</summary>
    /// <param name="basicValue">The converted value.</param>
    /// <returns><see langword="true"/> when the literal is supported.</returns>
    public bool TryGetBasicValue(out BasicValue basicValue)
    {
        if (this.parseResult == NumberLiteralParseResult.I128)
        {
            if (NumberLiteralHelper.IsInt64(this.uv))
            {
                basicValue = new((long)this.uv);
                return true;
            }
        }
        else if (this.parseResult == NumberLiteralParseResult.F64)
        {
            basicValue = new(BitConverter.UInt64BitsToDouble((ulong)this.uv));
            return true;
        }

        basicValue = default;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString()
        => this.Literal;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
        }

        builder.Append(this.Literal);
    }
}
