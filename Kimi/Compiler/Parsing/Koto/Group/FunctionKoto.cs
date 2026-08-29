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
    public Koto Type { get; private set; } = default!;

    /// <summary>Gets the default value, if present.</summary>
    [Key(4)]
    public Koto? DefaultValue { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="FunctionParameterKoto"/> class.</summary>
    /// <param name="externalName">The caller-facing name.</param>
    /// <param name="internalName">The body-facing name.</param>
    /// <param name="isOptional">Whether callers may omit the parameter.</param>
    /// <param name="type">The parameter type.</param>
    /// <param name="defaultValue">The default value, if present.</param>
    public FunctionParameterKoto(string externalName, string internalName, bool isOptional, Koto type, Koto? defaultValue)
    {
        this.ExternalName = externalName;
        this.InternalName = internalName;
        this.IsOptional = isOptional;
        this.Type = type;
        this.DefaultValue = defaultValue;
    }
}

/// <summary>
/// Represents a function declaration.
/// </summary>
[TinyhandObject]
public partial class FunctionKoto : IdentifiableKoto, ITokenParser
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Function;

    /// <summary>Gets the function modifiers.</summary>
    [Key(2)]
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets the function name.</summary>
    [Key(3)]
    public string Name { get; private set; } = string.Empty;

    [Key(4)]
    private List<TypeKoto> genericArguments = [];

    [Key(5)]
    private List<FunctionParameterKoto> parameters = [];

    /// <summary>Gets the return type, if specified.</summary>
    [Key(6)]
    public Koto? ReturnType { get; private set; }

    /// <summary>Gets the generic parameters.</summary>
    [IgnoreMember]
    public IReadOnlyList<TypeKoto> GenericArguments => this.genericArguments;

    /// <summary>Gets the function parameters.</summary>
    [IgnoreMember]
    public IReadOnlyList<FunctionParameterKoto> Parameters => this.parameters;

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
    public FunctionKoto(ref TokenReader reader, TokenContext context, SourceSpan range, string name, List<TypeKoto>? genericArguments, List<FunctionParameterKoto> parameters, Koto? returnType)
        : base(ref reader, range)
    {
        this.AttributeChain = context.AttributeKoto;
        this.Modifier = context.ModifierKind;
        this.IsExcluded = context.IsExcluded;
        this.Name = name;
        this.genericArguments = genericArguments ?? [];
        this.parameters = parameters;
        this.ReturnType = returnType;

        foreach (var argument in this.genericArguments)
        {
            argument.Parent = this;
        }

        foreach (var parameter in this.parameters)
        {
            parameter.Type.Parent = this;
            if (parameter.DefaultValue is not null)
            {
                parameter.DefaultValue.Parent = this;
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
        // Function bodies are not modeled yet, so consume the balanced block.
        var depth = 1;
        while (reader.CanRead)
        {
            var kind = reader.CurrentTokenKind;
            reader.Advance();
            if (kind == TokenKind.StartBlock)
            {
                depth++;
            }
            else if (kind == TokenKind.EndBlock && --depth == 0)
            {
                return;
            }
        }
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
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
    }

    internal override void RestoreAfterDeserialization(CodeContext codeContext, Koto? parent)
    {
        base.RestoreAfterDeserialization(codeContext, parent);
        foreach (var argument in this.genericArguments)
        {
            argument.RestoreAfterDeserialization(codeContext, this);
        }

        foreach (var parameter in this.parameters)
        {
            parameter.Type.RestoreAfterDeserialization(codeContext, this);
            parameter.DefaultValue?.RestoreAfterDeserialization(codeContext, this);
        }

        this.ReturnType?.RestoreAfterDeserialization(codeContext, this);
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        foreach (var argument in this.genericArguments)
        {
            argument.Parent = this;
        }

        foreach (var parameter in this.parameters)
        {
            parameter.Type.Parent = this;
            if (parameter.DefaultValue is not null)
            {
                parameter.DefaultValue.Parent = this;
            }
        }

        if (this.ReturnType is not null)
        {
            this.ReturnType.Parent = this;
        }
    }
}
