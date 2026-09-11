// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Describes one arm of a <see cref="MatchKoto"/> expression.</summary>
public sealed class MatchArmKoto
{
    /// <summary>Gets the arm pattern expression.</summary>
    public Koto Pattern { get; internal set; } = default!;

    /// <summary>Gets the arm result expression or block.</summary>
    public Koto Body { get; internal set; } = default!;

    /// <summary>Gets the optional guard expression.</summary>
    public Koto? Guard { get; internal set; }

    /// <summary>Initializes a new instance of the <see cref="MatchArmKoto"/> class.</summary>
    /// <param name="pattern">The arm pattern.</param>
    /// <param name="body">The arm body.</param>
    public MatchArmKoto(Koto pattern, Koto body)
    {
        this.Pattern = pattern;
        this.Body = body;
    }
}

/// <summary>Represents a <c>match</c> expression.</summary>
public sealed class MatchKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Match;

    /// <summary>Gets the expression being matched.</summary>
    public Koto Expression { get; private set; }

    private List<MatchArmKoto> arms;

    /// <summary>Gets the match arms.</summary>
    public IReadOnlyList<MatchArmKoto> Arms => this.arms;

    /// <summary>Initializes a new instance of the <see cref="MatchKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The expression being matched.</param>
    /// <param name="arms">The parsed match arms.</param>
    public MatchKoto(ref TokenReader reader, SourceSpan range, Koto expression, List<MatchArmKoto> arms)
        : base(ref reader, range)
    {
        this.Expression = expression;
        this.arms = arms;

        expression.Parent = this;
        foreach (var arm in arms)
        {
            arm.Pattern.Parent = this;
            arm.Body.Parent = this;
            this.Adopt(arm.Guard);
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.MatchKeyword);
        builder.AppendSpace();
        this.Expression.WriteTo(ref builder);
        builder.AppendLine();
        builder.IncrementIndent();
        for (var i = 0; i < this.arms.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            var arm = this.arms[i];
            arm.Pattern.WriteTo(ref builder);
            if (arm.Guard is not null)
            {
                builder.Append(" if ");
                arm.Guard.WriteTo(ref builder);
            }

            builder.Append(" =>");
            if (arm.Body is CodeBlockKoto block)
            {
                block.WriteIndentedTo(ref builder);
            }
            else
            {
                builder.AppendSpace();
                arm.Body.WriteTo(ref builder);
            }
        }

        builder.DecrementIndent();
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        visitor.Visit(this.Expression);
        for (var armIndex = 0; armIndex < this.arms.Count; armIndex++)
        {
            var arm = this.arms[armIndex];
            visitor.Visit(arm.Pattern);
            if (arm.Guard is not null)
            {
                visitor.Visit(arm.Guard);
            }

            visitor.Visit(arm.Body);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Expression;
        foreach (var arm in this.arms)
        {
            yield return arm.Pattern;
            if (arm.Guard is not null)
            {
                yield return arm.Guard;
            }

            yield return arm.Body;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Expression == oldKoto)
        {
            this.Expression = newKoto;
            return true;
        }

        foreach (var arm in this.arms)
        {
            if (arm.Guard == oldKoto)
            {
                arm.Guard = newKoto;
                return true;
            }

            if (arm.Pattern == oldKoto)
            {
                arm.Pattern = newKoto;
                return true;
            }

            if (arm.Body == oldKoto)
            {
                arm.Body = newKoto;
                return true;
            }
        }

        return false;
    }
}
