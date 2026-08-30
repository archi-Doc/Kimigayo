// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an indentation-delimited expression block.
/// </summary>
[TinyhandObject]
public sealed partial class CodeBlockKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CodeBlock;

    [Key(1)]
    private List<Koto> items = [];

    /// <summary>
    /// Gets a value indicating whether the final item is the block's trailing expression.
    /// </summary>
    [Key(2)]
    public bool HasTrailingExpression { get; private set; }

    /// <summary>Gets the block items in source order.</summary>
    [IgnoreMember]
    public IReadOnlyList<Koto> Items => this.items;

    /// <summary>
    /// Gets the trailing expression, or <see langword="null"/> when this block evaluates to Unit.
    /// </summary>
    [IgnoreMember]
    public Koto? TrailingExpression => this.HasTrailingExpression && this.items.Count > 0 ? this.items[^1] : null;

    /// <summary>Initializes a new instance of the <see cref="CodeBlockKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete block span.</param>
    /// <param name="items">The parsed block items.</param>
    /// <param name="hasTrailingExpression">Whether the final item supplies the block value.</param>
    public CodeBlockKoto(ref TokenReader reader, SourceSpan range, List<Koto> items, bool hasTrailingExpression)
        : base(ref reader, range)
    {
        this.items = items;
        this.HasTrailingExpression = hasTrailingExpression;

        foreach (var item in items)
        {
            item.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        foreach (var item in this.items)
        {
            item.Bind(compilation);
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        for (var i = 0; i < this.items.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            if (this.items[i] is GroupKoto group)
            {
                group.WriteAsBlockItem(ref builder);
            }
            else
            {
                this.items[i].WriteTo(ref builder);
            }
        }
    }

    internal void WriteIndentedTo(ref IndentedStringBuilder builder)
    {
        builder.AppendLine();
        builder.IncrementIndent();
        this.WriteTo(ref builder);
        builder.DecrementIndent();
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        for (var i = 0; i < this.items.Count; i++)
        {
            if (this.items[i] == oldKoto)
            {
                this.items[i] = newKoto;
                newKoto.Parent = this;
                oldKoto.Parent = default;
                return true;
            }
        }

        return false;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        foreach (var item in this.items)
        {
            item.RestoreAfterDeserialization(codeContext, this);
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        foreach (var item in this.items)
        {
            item.Parent = this;
        }
    }
}

/// <summary>Describes one conditional branch of an <see cref="IfKoto"/>.</summary>
[TinyhandObject]
public sealed partial class ConditionalBranchKoto
{
    /// <summary>Gets the branch condition.</summary>
    [Key(0)]
    public Koto Condition { get; internal set; } = default!;

    /// <summary>Gets the branch body.</summary>
    [Key(1)]
    public CodeBlockKoto Body { get; internal set; } = default!;

    /// <summary>Initializes a new instance of the <see cref="ConditionalBranchKoto"/> class.</summary>
    /// <param name="condition">The branch condition.</param>
    /// <param name="body">The branch body.</param>
    public ConditionalBranchKoto(Koto condition, CodeBlockKoto body)
    {
        this.Condition = condition;
        this.Body = body;
    }
}

/// <summary>Represents an <c>if</c> expression.</summary>
[TinyhandObject]
public sealed partial class IfKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.If;

    [Key(1)]
    private List<ConditionalBranchKoto> branches = [];

    /// <summary>Gets the conditional branches.</summary>
    [IgnoreMember]
    public IReadOnlyList<ConditionalBranchKoto> Branches => this.branches;

    /// <summary>Gets the final else body, if present.</summary>
    [Key(2)]
    public CodeBlockKoto? ElseBody { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="IfKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="branches">The conditional branches.</param>
    /// <param name="elseBody">The final else body, if present.</param>
    public IfKoto(ref TokenReader reader, SourceSpan range, List<ConditionalBranchKoto> branches, CodeBlockKoto? elseBody)
        : base(ref reader, range)
    {
        this.branches = branches;
        this.ElseBody = elseBody;
        this.SetParents();
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        foreach (var branch in this.branches)
        {
            branch.Condition.Bind(compilation);
            branch.Body.Bind(compilation);
        }

        this.ElseBody?.Bind(compilation);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        for (var i = 0; i < this.branches.Count; i++)
        {
            if (i == 0)
            {
                builder.Append(Constants.IfKeyword);
            }
            else
            {
                builder.AppendLine();
                builder.Append(Constants.ElseKeyword);
                builder.AppendSpace();
                builder.Append(Constants.IfKeyword);
            }

            builder.AppendSpace();
            this.branches[i].Condition.WriteTo(ref builder);
            this.branches[i].Body.WriteIndentedTo(ref builder);
        }

        if (this.ElseBody is not null)
        {
            builder.AppendLine();
            builder.Append(Constants.ElseKeyword);
            this.ElseBody.WriteIndentedTo(ref builder);
        }
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        foreach (var branch in this.branches)
        {
            if (branch.Condition == oldKoto)
            {
                branch.Condition = newKoto;
                newKoto.Parent = this;
                oldKoto.Parent = default;
                return true;
            }

            if (branch.Body == oldKoto && newKoto is CodeBlockKoto block)
            {
                branch.Body = block;
                block.Parent = this;
                oldKoto.Parent = default;
                return true;
            }
        }

        if (this.ElseBody == oldKoto && newKoto is CodeBlockKoto elseBlock)
        {
            this.ElseBody = elseBlock;
            elseBlock.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        return false;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        foreach (var branch in this.branches)
        {
            branch.Condition.RestoreAfterDeserialization(codeContext, this);
            branch.Body.RestoreAfterDeserialization(codeContext, this);
        }

        this.ElseBody?.RestoreAfterDeserialization(codeContext, this);
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
        => this.SetParents();

    private void SetParents()
    {
        foreach (var branch in this.branches)
        {
            branch.Condition.Parent = this;
            branch.Body.Parent = this;
        }

        if (this.ElseBody is not null)
        {
            this.ElseBody.Parent = this;
        }
    }
}

/// <summary>Describes one arm of a <see cref="MatchKoto"/> expression.</summary>
[TinyhandObject]
public sealed partial class MatchArmKoto
{
    /// <summary>Gets the arm pattern expression.</summary>
    [Key(0)]
    public Koto Pattern { get; internal set; } = default!;

    /// <summary>Gets the arm result expression or block.</summary>
    [Key(1)]
    public Koto Body { get; internal set; } = default!;

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
[TinyhandObject]
public sealed partial class MatchKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Match;

    /// <summary>Gets the expression being matched.</summary>
    [Key(1)]
    public Koto Expression { get; private set; }

    [Key(2)]
    private List<MatchArmKoto> arms = [];

    /// <summary>Gets the match arms.</summary>
    [IgnoreMember]
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
        this.SetParents();
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        this.Expression.Bind(compilation);
        foreach (var arm in this.arms)
        {
            arm.Pattern.Bind(compilation);
            arm.Body.Bind(compilation);
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

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Expression == oldKoto)
        {
            this.Expression = newKoto;
            newKoto.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        foreach (var arm in this.arms)
        {
            if (arm.Pattern == oldKoto)
            {
                arm.Pattern = newKoto;
                newKoto.Parent = this;
                oldKoto.Parent = default;
                return true;
            }

            if (arm.Body == oldKoto)
            {
                arm.Body = newKoto;
                newKoto.Parent = this;
                oldKoto.Parent = default;
                return true;
            }
        }

        return false;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Expression.RestoreAfterDeserialization(codeContext, this);
        foreach (var arm in this.arms)
        {
            arm.Pattern.RestoreAfterDeserialization(codeContext, this);
            arm.Body.RestoreAfterDeserialization(codeContext, this);
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
        => this.SetParents();

    private void SetParents()
    {
        this.Expression.Parent = this;
        foreach (var arm in this.arms)
        {
            arm.Pattern.Parent = this;
            arm.Body.Parent = this;
        }
    }
}

/// <summary>Represents a <c>while</c> expression whose value is Unit.</summary>
[TinyhandObject]
public sealed partial class WhileKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.While;

    /// <summary>Gets the loop condition.</summary>
    [Key(1)]
    public Koto Condition { get; private set; }

    /// <summary>Gets the loop body.</summary>
    [Key(2)]
    public CodeBlockKoto Body { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="WhileKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="condition">The loop condition.</param>
    /// <param name="body">The loop body.</param>
    public WhileKoto(ref TokenReader reader, SourceSpan range, Koto condition, CodeBlockKoto body)
        : base(ref reader, range)
    {
        this.Condition = condition;
        this.Body = body;
        condition.Parent = this;
        body.Parent = this;
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        this.Condition.Bind(compilation);
        this.Body.Bind(compilation);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.WhileKeyword);
        builder.AppendSpace();
        this.Condition.WriteTo(ref builder);
        this.Body.WriteIndentedTo(ref builder);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Condition == oldKoto)
        {
            this.Condition = newKoto;
            newKoto.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        if (this.Body == oldKoto && newKoto is CodeBlockKoto block)
        {
            this.Body = block;
            block.Parent = this;
            oldKoto.Parent = default;
            return true;
        }

        return false;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Condition.RestoreAfterDeserialization(codeContext, this);
        this.Body.RestoreAfterDeserialization(codeContext, this);
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        this.Condition.Parent = this;
        this.Body.Parent = this;
    }
}

/// <summary>Represents a Never-valued <c>return</c> expression.</summary>
[TinyhandObject]
public sealed partial class ReturnKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Return;

    /// <summary>Gets the returned expression, if present.</summary>
    [Key(1)]
    public Koto? Expression { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="ReturnKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The returned expression, if present.</param>
    public ReturnKoto(ref TokenReader reader, SourceSpan range, Koto? expression)
        : base(ref reader, range)
    {
        this.Expression = expression;
        if (expression is not null)
        {
            expression.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
        => this.Expression?.Bind(compilation);

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.ReturnKeyword);
        if (this.Expression is not null)
        {
            builder.AppendSpace();
            this.Expression.WriteTo(ref builder);
        }
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Expression != oldKoto)
        {
            return false;
        }

        this.Expression = newKoto;
        newKoto.Parent = this;
        oldKoto.Parent = default;
        return true;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Expression?.RestoreAfterDeserialization(codeContext, this);
    }
}

/// <summary>Represents a Never-valued <c>break</c> expression.</summary>
[TinyhandObject]
public sealed partial class BreakKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Break;

    /// <summary>Gets the break value, if present.</summary>
    [Key(1)]
    public Koto? Expression { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="BreakKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="expression">The break value, if present.</param>
    public BreakKoto(ref TokenReader reader, SourceSpan range, Koto? expression)
        : base(ref reader, range)
    {
        this.Expression = expression;
        if (expression is not null)
        {
            expression.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
        => this.Expression?.Bind(compilation);

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.BreakKeyword);
        if (this.Expression is not null)
        {
            builder.AppendSpace();
            this.Expression.WriteTo(ref builder);
        }
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        if (this.Expression != oldKoto)
        {
            return false;
        }

        this.Expression = newKoto;
        newKoto.Parent = this;
        oldKoto.Parent = default;
        return true;
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        this.Expression?.RestoreAfterDeserialization(codeContext, this);
    }
}

/// <summary>Represents a Never-valued <c>continue</c> expression.</summary>
[TinyhandObject]
public sealed partial class ContinueKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Continue;

    /// <summary>Initializes a new instance of the <see cref="ContinueKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The keyword span.</param>
    public ContinueKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
        => builder.Append(Constants.ContinueKeyword);
}
