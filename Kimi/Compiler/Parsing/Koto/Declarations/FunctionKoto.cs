// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Describes a parsed function parameter.
/// </summary>
[TinyhandObject]
public sealed partial record class FunctionParameterKoto
{
    /// <summary>Gets the parameter name used by callers.</summary>
    [Key(0)]
    public string ExternalName { get; private set; } = string.Empty;

    /// <summary>Gets the parameter name used in the function body.</summary>
    [Key(1)]
    public string InternalName { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether callers may omit the parameter.</summary>
    [Key(2)]
    public bool IsOptional { get; private set; }

    /// <summary>Gets the parameter type.</summary>
    [Key(3)]
    public Koto Type { get; internal set; } = default!;

    /// <summary>Gets the default value, if present.</summary>
    [Key(4)]
    public Koto? DefaultValue { get; internal set; }

    /// <summary>Gets the attributes applied to this parameter.</summary>
    [Key(5)]
    public AttributeKoto? AttributeChain { get; internal set; }

    /// <summary>Initializes a new instance of the <see cref="FunctionParameterKoto"/> class.</summary>
    /// <param name="externalName">The caller-facing name.</param>
    /// <param name="internalName">The body-facing name.</param>
    /// <param name="isOptional">Whether callers may omit the parameter.</param>
    /// <param name="type">The parameter type.</param>
    /// <param name="defaultValue">The default value, if present.</param>
    /// <param name="attributeChain">The parameter attributes, if present.</param>
    public FunctionParameterKoto(
        string externalName,
        string internalName,
        bool isOptional,
        Koto type,
        Koto? defaultValue,
        AttributeKoto? attributeChain = null)
    {
        this.ExternalName = externalName;
        this.InternalName = internalName;
        this.IsOptional = isOptional;
        this.Type = type;
        this.DefaultValue = defaultValue;
        this.AttributeChain = attributeChain;
    }
}

/// <summary>
/// Represents a function declaration.
/// </summary>
[TinyhandObject]
public sealed partial class FunctionKoto : IdentifiableKoto
{
    private static readonly TypeKoto[] EmptyGenericArguments = [];
    private static readonly FunctionParameterKoto[] EmptyParameters = [];

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Function;

    /// <summary>Gets the function modifiers.</summary>
    [Key(2)]
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets the function name.</summary>
    [Key(3)]
    public string Name { get; private set; } = string.Empty;

    [Key(4)]
    private List<TypeKoto>? genericArguments;

    [Key(5)]
    private List<FunctionParameterKoto>? parameters;

    /// <summary>Gets the return type, if specified.</summary>
    [Key(6)]
    public Koto? ReturnType { get; private set; }

    /// <summary>Gets the function body, if present.</summary>
    [Key(7)]
    public CodeBlockKoto? Body { get; private set; }

    /// <summary>Gets a value indicating whether this function was synthesized for top-level syntax.</summary>
    [Key(8)]
    public bool IsGenerated { get; private set; }

    /// <summary>Gets the generic parameters.</summary>
    [IgnoreMember]
    public IReadOnlyList<TypeKoto> GenericArguments
        => this.genericArguments is { } genericArguments ? genericArguments : EmptyGenericArguments;

    /// <summary>Gets the function parameters.</summary>
    [IgnoreMember]
    public IReadOnlyList<FunctionParameterKoto> Parameters
        => this.parameters is { } parameters ? parameters : EmptyParameters;

    /// <summary>Gets a value indicating whether conditional attributes exclude this function.</summary>
    [IgnoreMember]
    public bool IsExcluded { get; }

    /// <summary>Initializes a new instance of the <see cref="FunctionKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="context">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <param name="name">The function name.</param>
    /// <param name="genericArguments">The generic parameters, if present.</param>
    /// <param name="parameters">The function parameters.</param>
    /// <param name="returnType">The return type, if present.</param>
    public FunctionKoto(ref TokenReader reader, TokenContext context, SourceSpan range, string name, List<TypeKoto>? genericArguments, List<FunctionParameterKoto>? parameters, Koto? returnType)
        : base(ref reader, range)
    {
        this.SetAttributeChain(context.AttributeKoto);
        this.Modifier = context.ModifierKind;
        this.IsExcluded = context.IsExcluded;
        this.Name = name;
        this.genericArguments = genericArguments;
        this.parameters = parameters;
        this.ReturnType = returnType;

        if (this.genericArguments is not null)
        {
            foreach (var argument in this.genericArguments)
            {
                argument.Parent = this;
            }
        }

        if (this.parameters is not null)
        {
            foreach (var parameter in this.parameters)
            {
                this.AttachParameter(parameter);
            }
        }

        if (returnType is not null)
        {
            returnType.Parent = this;
        }
    }

    /// <inheritdoc/>
    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    /// <summary>Consumes the function body.</summary>
    /// <param name="reader">The token reader.</param>
    public void Parse(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            return;
        }

        this.Body = Parser.ParseBlock(ref reader);
        this.Body.Parent = this;
        this.Span = SourceSpan.FromBounds(this.Span.Start, this.Body.Span.End);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.IsGenerated)
        {
            this.Body?.WriteTo(ref builder);
            return;
        }

        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(Constants.FuncKeyword);
        builder.AppendSpace();
        builder.Append(this.Name);

        if (this.GenericArguments.Count > 0)
        {
            builder.Append('<');
            for (var i = 0; i < this.GenericArguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                this.GenericArguments[i].WriteTo(ref builder);
            }

            builder.Append('>');
        }

        builder.Append('(');
        for (var i = 0; i < this.Parameters.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            var parameter = this.Parameters[i];
            if (parameter.AttributeChain is not null)
            {
                Parser.UnparseAttribute(parameter.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
            }

            builder.Append(parameter.ExternalName);
            if (parameter.IsOptional)
            {
                builder.Append('?');
            }

            if (!parameter.ExternalName.Equals(parameter.InternalName, StringComparison.Ordinal))
            {
                builder.Append(" => ");
                builder.Append(parameter.InternalName);
            }

            builder.Append(": ");
            parameter.Type.WriteTo(ref builder);
            if (parameter.DefaultValue is not null)
            {
                builder.Append(" = ");
                parameter.DefaultValue.WriteTo(ref builder);
            }
        }

        builder.Append(')');
        if (this.ReturnType is not null)
        {
            builder.Append(" -> ");
            this.ReturnType.WriteTo(ref builder);
        }

        if (this.Body is not null)
        {
            this.Body.WriteIndentedTo(ref builder);
        }
    }

    /// <summary>Initializes a new instance of the <see cref="FunctionKoto"/> class for generated syntax.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="name">The internal function name.</param>
    internal FunctionKoto(CodeContext codeContext, string name)
        : base(codeContext, default)
    {
        this.Name = name;
        this.IsGenerated = true;
        this.Body = new CodeBlockKoto(codeContext);
        this.Body.Parent = this;
    }

    /// <summary>Adds top-level syntax to this generated function.</summary>
    /// <param name="item">The syntax node to add.</param>
    /// <param name="hasTrailingExpression">Whether the item supplies the function body's value.</param>
    internal void AddGeneratedItem(Koto item, bool hasTrailingExpression)
    {
        if (!this.IsGenerated || this.Body is null)
        {
            throw new InvalidOperationException();
        }

        this.Body.AddLast(item, hasTrailingExpression);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.genericArguments is not null)
        {
            foreach (var argument in this.genericArguments)
            {
                yield return argument;
            }
        }

        if (this.parameters is not null)
        {
            foreach (var parameter in this.parameters)
            {
                if (parameter.AttributeChain is not null)
                {
                    yield return parameter.AttributeChain;
                }

                yield return parameter.Type;
                if (parameter.DefaultValue is not null)
                {
                    yield return parameter.DefaultValue;
                }
            }
        }

        if (this.ReturnType is not null)
        {
            yield return this.ReturnType;
        }

        if (this.Body is not null)
        {
            yield return this.Body;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Body == oldKoto && newKoto is CodeBlockKoto block)
        {
            this.Body = block;
            return true;
        }

        if (this.ReturnType == oldKoto)
        {
            this.ReturnType = newKoto;
            return true;
        }

        if (this.parameters is not null)
        {
            foreach (var parameter in this.parameters)
            {
                if (parameter.AttributeChain == oldKoto && newKoto is AttributeKoto attribute)
                {
                    parameter.AttributeChain = attribute;
                    return true;
                }

                if (parameter.Type == oldKoto)
                {
                    parameter.Type = newKoto;
                    return true;
                }

                if (parameter.DefaultValue == oldKoto)
                {
                    parameter.DefaultValue = newKoto;
                    return true;
                }
            }
        }

        if (this.genericArguments is not null &&
            oldKoto is TypeKoto oldType && newKoto is TypeKoto newType)
        {
            var index = this.genericArguments.IndexOf(oldType);
            if (index >= 0)
            {
                this.genericArguments[index] = newType;
                return true;
            }
        }

        return false;
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        if (this.genericArguments is not null)
        {
            foreach (var argument in this.genericArguments)
            {
                argument.Parent = this;
            }
        }

        if (this.parameters is not null)
        {
            foreach (var parameter in this.parameters)
            {
                this.AttachParameter(parameter);
            }
        }

        if (this.ReturnType is not null)
        {
            this.ReturnType.Parent = this;
        }

        if (this.Body is not null)
        {
            this.Body.Parent = this;
        }
    }

    private void AttachParameter(FunctionParameterKoto parameter)
    {
        Koto parent = this;
        var attribute = parameter.AttributeChain;
        while (attribute is not null)
        {
            attribute.Parent = parent;
            parent = attribute;
            attribute = attribute.AttributeChain;
        }

        parameter.Type.Parent = this;
        if (parameter.DefaultValue is not null)
        {
            parameter.DefaultValue.Parent = this;
        }
    }
}
