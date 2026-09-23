// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents one semantics layer and its Origin, retaining the complete inner type.
/// </summary>
public sealed class TypeSemanticsKoto : TypeKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.TypeSemantics;

    private SemanticsKind semanticsKind;

    /// <inheritdoc/>
    public override SemanticsKind SemanticsKind
        => this.isTransparentWrapper && this.Type is TypeKoto type ? type.SemanticsKind : this.semanticsKind;

    // A simple type stores its name here; a compound type stores its custom semantics parameter.
    // The two never coexist, so one slot keeps the node small.
    private string? nameOrSemanticsParameter;

    /// <inheritdoc/>
    public override string? SemanticsParameter
        => this.isTransparentWrapper && this.Type is TypeKoto type ? type.SemanticsParameter : this.Type is null ? null : this.nameOrSemanticsParameter;

    private TokenKind coreTypeToken;

    /// <summary>
    /// Gets the complete inner type, including any nested semantics and Origins.
    /// </summary>
    public Koto? Type { get; private set; }

    // Most types carry no Origin, so annotation data shares one lazily created object.
    private Origin? origin;

    /// <inheritdoc/>
    public override string? OriginName => this.origin?.Name;

    private bool isTransparentWrapper;

    /// <summary>Gets the qualified or intersected Origin expression.</summary>
    public Koto? OriginExpression => this.origin?.Expression;

    /// <summary>Gets named Origin arguments, or null for an ordinary Origin annotation.</summary>
    public OriginArgument[]? OriginArguments => this.origin?.Arguments;

    /// <summary>Gets the leaf identifier; this alone does not identify the complete layered type.</summary>
    public override string Identifier
        => this.Type is TypeKoto simpleType
            ? simpleType.Identifier
            : this.Type is not null
            ? string.Empty
            : this.coreTypeToken.IsPrimitiveType()
            ? this.coreTypeToken.ToText()
            : this.nameOrSemanticsParameter ?? string.Empty;

    /// <summary>Gets a value indicating whether this layer writes any Origin annotation.</summary>
    /// <remarks>Equivalent to testing <see cref="OriginName"/>, <see cref="OriginExpression"/> and
    /// <see cref="OriginArguments"/> together, in one field read.</remarks>
    internal bool HasOrigin => this.origin is not null;

    internal string? BindingSetName => this.origin is { IsBindingSet: true } set ? set.Name : null;

    internal bool IsLegacyBorrowCandidate => this.origin is { IsBindingSet: true, FollowedBySlash: true };

    internal SourceSpan BorrowOriginSpan => this.origin?.SourceSpan ?? default;

    internal void MarkBindingSet(bool followedBySlash = false)
    {
        if (this.origin is { } annotation)
        {
            annotation.IsBindingSet = true;
            annotation.FollowedBySlash = followedBySlash;
        }
    }

    internal bool IsTransparentWrapper => this.isTransparentWrapper;

    /// <summary>Initializes a new instance of the <see cref="TypeSemanticsKoto"/> class for a simple named or primitive type with owner semantics.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="typeToken">The type name token.</param>
    internal TypeSemanticsKoto(ref TokenReader reader, Token typeToken)
        : base(ref reader, typeToken.Span)
    {
        this.coreTypeToken = typeToken.Kind;
        this.semanticsKind = SemanticsKind.Owner;

        if (!this.coreTypeToken.IsPrimitiveType())
        {
            this.nameOrSemanticsParameter = reader.GetIdentifier(typeToken);
        }
    }

    /// <summary>Initializes a new instance of the <see cref="TypeSemanticsKoto"/> class for a synthesized bare operation target such as <c>move</c>.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The source span of the generating syntax.</param>
    /// <param name="operation">The operation target name.</param>
    internal TypeSemanticsKoto(ref TokenReader reader, SourceSpan range, string operation)
        : base(ref reader, range)
    {
        this.coreTypeToken = TokenKind.Identifier;
        this.semanticsKind = SemanticsKind.Owner;
        this.nameOrSemanticsParameter = operation;
    }

    /// <summary>Initializes a new instance of the <see cref="TypeSemanticsKoto"/> class for a compound type with explicit semantics.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="type">The type to which the semantics applies.</param>
    /// <param name="semanticsKind">The ownership semantics.</param>
    /// <param name="semanticsParameter">The custom semantics parameter, if present.</param>
    internal TypeSemanticsKoto(
        ref TokenReader reader,
        SourceSpan range,
        Koto type,
        SemanticsKind semanticsKind,
        string? semanticsParameter)
        : base(ref reader, range)
    {
        this.semanticsKind = semanticsKind;
        this.nameOrSemanticsParameter = semanticsParameter;
        this.Type = type;
        type.Parent = this;
    }

    /// <summary>Initializes a new instance of the <see cref="TypeSemanticsKoto"/> class as a transparent wrapper that only carries an origin or a compound type.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete source span.</param>
    /// <param name="type">The wrapped type.</param>
    /// <param name="originName">The origin name, if present.</param>
    internal TypeSemanticsKoto(ref TokenReader reader, SourceSpan range, Koto type, string? originName = null)
        : base(ref reader, range)
    {
        this.semanticsKind = SemanticsKind.Owner;
        this.Type = type;
        if (originName is not null)
        {
            this.origin = new() { Name = originName };
        }

        this.isTransparentWrapper = true;
        type.Parent = this;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
        => this.WriteTypeTo(ref builder, writeBorrowOrigin: true);

    internal void WriteTypeTo(ref IndentedStringBuilder builder, bool writeBorrowOrigin)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);

        if (this.Type is not null)
        {
            if (!this.isTransparentWrapper)
            {
                builder.Append(this.SemanticsKind == SemanticsKind.Parameter ? this.SemanticsParameter : this.SemanticsKind.ToText());
                builder.Append(Constants.SlashChar);
            }

            var needsParentheses = !this.isTransparentWrapper && (this.Type is FunctionTypeKoto or OptionalTypeKoto ||
                this.Type is TypeSemanticsKoto { IsTransparentWrapper: false, HasOrigin: true });
            if (needsParentheses)
            {
                builder.Append('(');
            }

            this.Type.WriteTo(ref builder);
            if (needsParentheses)
            {
                builder.Append(')');
            }
        }
        else
        {
            builder.Append(this.Identifier);
        }

        if (this.Type is null || this.isTransparentWrapper || writeBorrowOrigin)
        {
            this.WriteOriginTo(ref builder);
        }
    }

    internal void SetBorrowOrigin(Koto expression, SourceSpan sourceSpan)
    {
        this.SetOrigin(expression, null, sourceSpan.End);
        this.origin!.SourceSpan = sourceSpan;
    }

    internal void SetOrigin(string originName, int end)
    {
        (this.origin ??= new()).Name = originName;
        this.Span = SourceSpan.FromBounds(this.Span.Start, end);
    }

    internal void SetOrigin(Koto? expression, OriginArgument[]? arguments, int end)
    {
        if (expression is null && arguments is null)
        {
            this.origin = null;
        }
        else
        {
            this.origin = new()
            {
                Expression = expression,
                Arguments = arguments,
                Name = (expression as IdentifierNameKoto)?.IdentifierName,
            };
        }

        this.Adopt(expression);
        if (arguments is not null)
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                this.Adopt(arguments[i].Value);
            }
        }

        this.Span = SourceSpan.FromBounds(this.Span.Start, Math.Max(this.Span.End, end));
    }

    internal void WriteOriginTo(ref IndentedStringBuilder builder)
    {
        if (this.origin is null)
        {
            return;
        }

        var bindingSet = this.origin.IsBindingSet || this.Type is null || this.isTransparentWrapper;
        var intersection = !bindingSet && this.OriginExpression is AndKoto;
        builder.Append(bindingSet ? "{" : " during ");
        if (intersection)
        {
            builder.Append('(');
        }

        if (this.OriginArguments is { } arguments)
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                builder.Append(arguments[i].Name);
                builder.Append(" => ");
                arguments[i].Value.WriteTo(ref builder);
            }
        }
        else if (this.OriginExpression is { } expression)
        {
            expression.WriteTo(ref builder);
        }
        else
        {
            builder.Append(this.origin.Name);
        }

        if (bindingSet || intersection)
        {
            builder.Append(bindingSet ? '}' : ')');
        }
    }

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        if (this.Type is not null)
        {
            visitor.Visit(this.Type);
        }

        if (this.OriginExpression is not null)
        {
            visitor.Visit(this.OriginExpression);
        }

        if (this.OriginArguments is not null)
        {
            for (var argumentIndex = 0; argumentIndex < this.OriginArguments.Length; argumentIndex++)
            {
                var argument = this.OriginArguments[argumentIndex];
                visitor.Visit(argument.Value);
            }
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.Type is not null)
        {
            yield return this.Type;
        }

        if (this.OriginExpression is not null)
        {
            yield return this.OriginExpression;
        }

        if (this.OriginArguments is { } yielded)
        {
            for (var i = 0; i < yielded.Length; i++)
            {
                yield return yielded[i].Value;
            }
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.origin is { } origin && origin.Expression == oldKoto)
        {
            origin.Expression = newKoto;
            origin.Name = (newKoto as IdentifierNameKoto)?.IdentifierName;
            return true;
        }

        if (this.OriginArguments is { } arguments)
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].Value == oldKoto)
                {
                    arguments[i].Value = newKoto;
                    return true;
                }
            }
        }

        if (this.Type != oldKoto)
        {
            return false;
        }

        this.Type = newKoto;
        return true;
    }

    /// <summary>Stores the Origin annotation of a type layer.</summary>
    private sealed class Origin
    {
        public bool IsBindingSet { get; set; }

        public bool FollowedBySlash { get; set; }

        public SourceSpan SourceSpan { get; set; }

        public string? Name { get; set; }

        public Koto? Expression { get; set; }

        public OriginArgument[]? Arguments { get; set; }
    }
}

/// <summary>Represents a named Origin argument.</summary>
/// <remarks>The syntax tree is rebuilt by reparsing, so this carries no serialized state.</remarks>
/// <param name="name">The Origin parameter name.</param>
/// <param name="value">The Origin expression.</param>
public struct OriginArgument(string name, Koto value)
{
    /// <summary>Gets the declared Origin parameter name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the supplied Origin expression.</summary>
    public Koto Value { get; internal set; } = value;
}
