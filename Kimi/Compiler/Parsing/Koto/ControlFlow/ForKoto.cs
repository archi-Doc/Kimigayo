// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a <c>for</c> expression whose value is Unit.</summary>
[TinyhandObject]
public sealed partial class ForKoto : Koto
{
    [Key(1)]
    private List<IdentifierNameKoto> bindings = [];

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.For;

    /// <summary>Gets the iteration bindings in source order.</summary>
    [IgnoreMember]
    public IReadOnlyList<IdentifierNameKoto> Bindings => this.bindings;

    /// <summary>Gets the expression that supplies the values to iterate.</summary>
    [Key(2)]
    public Koto Iterable { get; private set; }

    /// <summary>Gets the loop body.</summary>
    [Key(3)]
    public CodeBlockKoto Body { get; private set; }

    /// <summary>Gets a value indicating whether the bindings use tuple syntax.</summary>
    [Key(4)]
    public bool IsTupleBinding { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="ForKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete expression span.</param>
    /// <param name="bindings">The iteration bindings.</param>
    /// <param name="iterable">The expression that supplies values.</param>
    /// <param name="body">The loop body.</param>
    /// <param name="isTupleBinding">Whether the bindings use tuple syntax.</param>
    public ForKoto(
        ref TokenReader reader,
        SourceSpan range,
        List<IdentifierNameKoto> bindings,
        Koto iterable,
        CodeBlockKoto body,
        bool isTupleBinding)
        : base(ref reader, range)
    {
        this.bindings = bindings;
        this.Iterable = iterable;
        this.Body = body;
        this.IsTupleBinding = isTupleBinding;
        this.SetParents();
    }

    /// <inheritdoc/>
    public override void Bind(Compilation compilation)
    {
        this.Iterable.Bind(compilation);
        this.Body.Bind(compilation);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.ForKeyword);
        builder.AppendSpace();
        if (this.IsTupleBinding)
        {
            builder.Append(Constants.OpenParenthesisChar);
        }

        for (var i = 0; i < this.bindings.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            this.bindings[i].WriteTo(ref builder);
        }

        if (this.IsTupleBinding)
        {
            builder.Append(Constants.CloseParenthesisChar);
        }

        builder.AppendSpace();
        builder.Append(Constants.InKeyword);
        builder.AppendSpace();
        this.Iterable.WriteTo(ref builder);
        this.Body.WriteIndentedTo(ref builder);
    }

    internal override bool ReplaceChild(Koto oldKoto, Koto newKoto)
    {
        for (var i = 0; i < this.bindings.Count; i++)
        {
            if (this.bindings[i] == oldKoto && newKoto is IdentifierNameKoto binding)
            {
                this.bindings[i] = binding;
                binding.Parent = this;
                oldKoto.Parent = default;
                return true;
            }
        }

        if (this.Iterable == oldKoto)
        {
            this.Iterable = newKoto;
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
        foreach (var binding in this.bindings)
        {
            binding.RestoreAfterDeserialization(codeContext, this);
        }

        this.Iterable.RestoreAfterDeserialization(codeContext, this);
        this.Body.RestoreAfterDeserialization(codeContext, this);
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
        => this.SetParents();

    private void SetParents()
    {
        foreach (var binding in this.bindings)
        {
            binding.Parent = this;
        }

        this.Iterable.Parent = this;
        this.Body.Parent = this;
    }
}
