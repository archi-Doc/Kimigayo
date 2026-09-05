// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Stores a compile-time <c>#if</c> whose condition must be evaluated after parsing.</summary>
[TinyhandObject]
public sealed partial class CompileTimeIfKoto : Koto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CompileTimeIf;

    /// <summary>Gets the compile-time condition.</summary>
    [Key(1)]
    public Koto Condition { get; private set; }

    /// <summary>Gets the controlled syntax node.</summary>
    [Key(2)]
    public Koto Target { get; private set; }

    internal CompileTimeIfKoto(CodeContext codeContext, SourceSpan range, Koto condition, Koto target)
        : base(codeContext, range)
    {
        this.Condition = condition;
        this.Target = target;
        this.Adopt(condition);
        this.Adopt(target);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("#if ");
        this.Condition.WriteTo(ref builder);
        builder.AppendLine();
        if (this.Target is CodeBlockKoto block)
        {
            builder.IncrementIndent();
            block.WriteTo(ref builder);
            builder.DecrementIndent();
        }
        else if (this.Target is DeclarationContainerKoto container)
        {
            container.WriteAsBlockItem(ref builder);
        }
        else
        {
            this.Target.WriteTo(ref builder);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
        => [this.Condition, this.Target];

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Condition == oldKoto)
        {
            this.Condition = newKoto;
            return true;
        }

        if (this.Target == oldKoto)
        {
            this.Target = newKoto;
            return true;
        }

        return false;
    }
}

/// <summary>Temporarily associates a deferred condition with the syntax node that follows it.</summary>
/// <param name="Span">The directive prefix span.</param>
/// <param name="Condition">The parsed condition.</param>
public readonly record struct CompileTimeIfPrefix(SourceSpan Span, Koto Condition);
