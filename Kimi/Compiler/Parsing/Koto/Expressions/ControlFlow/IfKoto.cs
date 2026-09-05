// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

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
public sealed partial class IfKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.If;

    [Key(1)]
    private List<ConditionalBranchKoto> branches;

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

        foreach (var branch in branches)
        {
            branch.Condition.Parent = this;
            branch.Body.Parent = this;
        }

        this.Adopt(elseBody);
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
            this.branches[i].Body.WriteBranchTo(ref builder);
        }

        if (this.ElseBody is not null)
        {
            builder.AppendLine();
            builder.Append(Constants.ElseKeyword);
            this.ElseBody.WriteBranchTo(ref builder);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var branch in this.branches)
        {
            yield return branch.Condition;
            yield return branch.Body;
        }

        if (this.ElseBody is not null)
        {
            yield return this.ElseBody;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        foreach (var branch in this.branches)
        {
            if (branch.Condition == oldKoto)
            {
                branch.Condition = newKoto;
                return true;
            }

            if (branch.Body == oldKoto && newKoto is CodeBlockKoto block)
            {
                branch.Body = block;
                return true;
            }
        }

        if (this.ElseBody == oldKoto && newKoto is CodeBlockKoto elseBlock)
        {
            this.ElseBody = elseBlock;
            return true;
        }

        return false;
    }
}
