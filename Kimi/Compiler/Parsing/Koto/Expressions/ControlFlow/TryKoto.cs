// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

#pragma warning disable SA1202 // Keep syntax construction and representation together.

namespace Kimi.Compiler.Parsing;

/// <summary>Source try syntax with reusable ordinary match/return semantic children.</summary>
public sealed class TryKoto : MatchKoto
{
    private readonly IdentifierNameKoto successName;
    private readonly SyntaxFormKoto nonePattern;
    private readonly SyntaxFormKoto errorPattern;
    private readonly Koto noneValue;
    private readonly Koto errorValue;

    internal TryKoto(ref TokenReader reader, SourceSpan span, Koto operand)
        : base(ref reader, span, operand, new List<MatchArmKoto>(2))
    {
        var ok = Case(ref reader, span, "Ok");
        this.successName = (IdentifierNameKoto)ok.Operands[0];
        var success = Pattern(ref reader, span, ok, "$try.value");
        this.nonePattern = Pattern(ref reader, span, Case(ref reader, span, "None"), null);
        this.errorPattern = Pattern(ref reader, span, Case(ref reader, span, "Err"), "$try.error");
        this.noneValue = Case(ref reader, span, "None");
        this.errorValue = new InvocationKoto(ref reader, span, Case(ref reader, span, "Err"), [Name(ref reader, span, "$try.error")]);
        this.Failure = new ReturnKoto(ref reader, span, this.errorValue);
        var arms = (List<MatchArmKoto>)this.Arms;
        arms.Add(new(success, Name(ref reader, span, "$try.value")));
        arms.Add(new(this.errorPattern, this.Failure));
        foreach (var arm in arms)
        {
            arm.Pattern.Parent = this;
            arm.Body.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Try;

    internal ReturnKoto Failure { get; }

    internal bool SemanticsIndexed { get; set; }

    internal void SelectOption(bool option)
    {
        this.successName.IdentifierName = option ? "Some" : "Ok";
        this.Arms[1].Pattern = option ? this.nonePattern : this.errorPattern;
        this.Arms[1].Pattern.Parent = this;
        var value = option ? this.noneValue : this.errorValue;
        if (!ReferenceEquals(this.Failure.Expression, value))
        {
            this.Failure.ReplaceChild(this.Failure.Expression!, value);
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("try ");
        this.Expression.WriteTo(ref builder);
    }

    private static IdentifierNameKoto Name(ref TokenReader reader, SourceSpan span, string name)
        => new(ref reader, new Token(TokenKind.Identifier, span), name);

    private static SyntaxFormKoto Case(ref TokenReader reader, SourceSpan span, string name)
        => new(ref reader, span, KotoKind.InferredCase, ".", [Name(ref reader, span, name)]);

    private static SyntaxFormKoto Pattern(ref TokenReader reader, SourceSpan span, Koto reference, string? name)
    {
        if (name is null)
        {
            return new(ref reader, span, KotoKind.CasePattern, string.Empty, [reference]);
        }

        var binding = new SyntaxFormKoto(ref reader, new(span.Start, 0), KotoKind.BindingPattern, "let ", [Name(ref reader, span, name)]);
        var payload = new SyntaxFormKoto(ref reader, span, KotoKind.TuplePattern, "(", [binding], suffix: ")");
        return new(ref reader, span, KotoKind.CasePattern, string.Empty, [reference, payload], separator: string.Empty);
    }
}
