// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a numeric literal expression.
/// </summary>
public sealed class NumberLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.NumberLiteral;

    private NumberLiteralParseResult parseResult;

    private Int128 uv;

    private bool hasParsedValue;

    /// <summary>Gets the exact source spelling without allocating or rounding.</summary>
    public ReadOnlySpan<char> SourceSpelling => this.CodeContext.SourceDocument!.AsSpan().Slice(this.Span.Start, this.Span.Length);

    /// <summary>Gets a value indicating whether the literal has an integer representation.</summary>
    public bool IsInteger
    {
        get
        {
            var text = this.SourceSpelling;
            return (text.Length > 1 && text[0] == '0' && text[1] is 'x' or 'X' or 'b' or 'B' or 'o' or 'O') || text.IndexOfAny('.', 'e', 'E') < 0;
        }
    }

    /// <summary>Gets the unsigned source magnitude without formatting its signed storage bit pattern.</summary>
    /// <param name="magnitude">The original magnitude, including values above Int128.MaxValue.</param>
    /// <returns>Whether this is an integer literal.</returns>
    public bool TryGetIntegerMagnitude(out UInt128 magnitude)
    {
        this.EnsureParsedValue();
        magnitude = unchecked((UInt128)this.uv);
        return this.parseResult == NumberLiteralParseResult.I128;
    }

    /// <summary>Gets the normalized literal text.</summary>
    public string Literal
    {
        get
        {
            if (field is not null)
            {
                return field;
            }

            if (!this.IsInteger)
            {
                return field = this.SourceSpelling.ToString();
            }

            this.EnsureParsedValue();

            if (this.parseResult == NumberLiteralParseResult.I128)
            {
                field = unchecked((UInt128)this.uv).ToString(CultureInfo.InvariantCulture);
            }
            else if (this.parseResult == NumberLiteralParseResult.F64)
            {
                var literal = BitConverter.UInt64BitsToDouble((ulong)this.uv).ToString("R", CultureInfo.InvariantCulture);
                field = literal.AsSpan().IndexOfAny('.', 'E', 'e') < 0 ? string.Concat(literal, ".0") : literal;
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
        // Fitting is deferred; the source snapshot owns the exact decimal digits.
        if (this.IsInteger)
        {
            this.EnsureParsedValue();
            if (this.parseResult == NumberLiteralParseResult.Invalid)
            {
                reader.Diagnostic.Add(token.Span, DiagnosticCode.InvalidNumericLiteral_Kd);
            }
        }
    }

    /// <summary>Attempts to convert the literal to a compile-time value.</summary>
    /// <param name="basicValue">The converted value.</param>
    /// <returns><see langword="true"/> when the literal is supported.</returns>
    public bool TryGetBasicValue(out BasicValue basicValue)
    {
        this.EnsureParsedValue();
        if (this.parseResult == NumberLiteralParseResult.I128)
        {
            if (this.uv >= 0 && this.uv <= long.MaxValue)
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
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);
        if (this.IsInteger)
        {
            builder.Append(this.Literal);
        }
        else
        {
            builder.Append(this.SourceSpelling);
        }
    }

    private void EnsureParsedValue()
    {
        if (!this.hasParsedValue)
        {
            this.parseResult = NumberLiteralHelper.ParseNumberLiteral(this.SourceSpelling, out this.uv);
            this.hasParsedValue = true;
        }
    }
}
