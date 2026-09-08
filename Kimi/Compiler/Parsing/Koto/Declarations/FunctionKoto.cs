// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>A capture spelling and acquisition operation.</summary>
/// <param name="Name">The captured name.</param>
/// <param name="IsMutable">Whether the environment binding is mutable.</param>
/// <param name="Operation">The explicit operation, or null.</param>
/// <param name="Span">The source span.</param>
public readonly record struct CaptureKoto(string Name, bool IsMutable, string? Operation, SourceSpan Span);

/// <summary>
/// Describes a parsed function parameter.
/// </summary>
public sealed record class FunctionParameterKoto
{
    /// <summary>Gets the parameter name used by callers.</summary>
    public string ExternalName { get; private set; } = string.Empty;

    /// <summary>Gets the parameter name used in the function body.</summary>
    public string InternalName { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether callers may omit the parameter.</summary>
    public bool IsOptional { get; private set; }

    /// <summary>Gets the parameter type.</summary>
    public Koto Type { get; internal set; } = default!;

    /// <summary>Gets the default value, if present.</summary>
    public Koto? DefaultValue { get; internal set; }

    /// <summary>Gets the attributes applied to this parameter.</summary>
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
public sealed class FunctionKoto : IdentifiableKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Function;

    /// <summary>Gets the function modifiers.</summary>
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets the function name.</summary>
    public string Name { get; private set; } = string.Empty;

    private List<TypeKoto>? genericArguments;

    private List<FunctionParameterKoto>? parameters;

    /// <summary>Gets the return type, if specified.</summary>
    public Koto? ReturnType { get; private set; }

    /// <summary>Gets the function body, if present.</summary>
    public CodeBlockKoto? Body { get; private set; }

    /// <summary>Gets a value indicating whether this function was synthesized for top-level syntax.</summary>
    public bool IsGenerated { get; private set; }

    /// <summary>Gets the expression after =>, if this function is expression-bodied.</summary>
    public Koto? ExpressionBody { get; private set; }

    private List<string>? origins;

    private List<Koto>? typeConstraints;

    /// <summary>Gets compile-time constraints declared before executable body items.</summary>
    public IReadOnlyList<Koto> TypeConstraints => (IReadOnlyList<Koto>?)this.typeConstraints ?? [];

    /// <summary>Gets a value indicating whether this function is a destructor body.</summary>
    public bool IsDestructor { get; internal set; }

    /// <summary>Gets a value indicating whether this is an anonymous function.</summary>
    public bool IsAnonymous { get; internal set; }

    /// <summary>Gets a value indicating whether this is a constructor declaration.</summary>
    public bool IsConstructor { get; internal set; }

    /// <summary>Gets a value indicating whether this is a contract function requirement.</summary>
    public bool IsRequirement { get; internal set; }

    /// <summary>Gets a value indicating whether this is an explicit specialization.</summary>
    public bool IsSpecialization { get; internal set; }

    /// <summary>Gets the capture list; null distinguishes an omitted list.</summary>
    public CaptureKoto[]? Captures { get; private set; }

    /// <summary>Gets the base constructor initializer.</summary>
    public InvocationKoto? BaseInitializer { get; private set; }

    internal void SetCaptures(CaptureKoto[]? captures) => this.Captures = captures;

    internal void SetBaseInitializer(InvocationKoto initializer)
    {
        this.BaseInitializer = initializer;
        this.Adopt(initializer);
    }

    /// <summary>Gets the abstract Origin parameters.</summary>
    public IReadOnlyList<string> Origins => (IReadOnlyList<string>?)this.origins ?? [];

    internal void SetOrigins(List<string>? origins) => this.origins = origins;

    internal void AddTypeConstraint(Koto constraint)
    {
        (this.typeConstraints ??= []).Add(constraint);
        this.Adopt(constraint);
    }

    internal bool IsGenericParameter(string name)
    {
        if (this.genericArguments is not null)
        {
            foreach (var parameter in this.genericArguments)
            {
                if (parameter.Identifier == name || parameter.SemanticsParameter == name)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Gets the generic parameters.</summary>
    public IReadOnlyList<TypeKoto> GenericArguments
        => (IReadOnlyList<TypeKoto>?)this.genericArguments ?? [];

    /// <summary>Gets the function parameters.</summary>
    public IReadOnlyList<FunctionParameterKoto> Parameters
        => (IReadOnlyList<FunctionParameterKoto>?)this.parameters ?? [];

    /// <summary>Gets a value indicating whether conditional attributes exclude this function.</summary>
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

        this.Adopt(genericArguments);
        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                this.AttachParameter(parameter);
            }
        }

        this.Adopt(returnType);
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

    /// <inheritdoc/>
    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    /// <summary>Consumes the function body.</summary>
    /// <param name="reader">The token reader.</param>
    public void Parse(ref TokenReader reader)
    {
        if (reader.TryConsume(TokenKind.EqualsGreaterThan))
        {
            this.ExpressionBody = Parser.ParseRequiredExpression(ref reader);
            this.Adopt(this.ExpressionBody);
            this.Span = SourceSpan.FromBounds(this.Span.Start, this.ExpressionBody.Span.End);
            return;
        }

        reader.TrySkipSeparatorsTo(TokenKind.StartBlock);
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.Diagnostic.Add(this.Span, DiagnosticCode.EmptyExecutableBlock_Kd);
            return;
        }

        this.Body = Parser.ParseFunctionBlock(ref reader, this);
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

        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendLineFeed);
        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        if (this.IsSpecialization)
        {
            builder.Append("specialize ");
        }

        if (!this.IsDestructor && !this.IsConstructor)
        {
            builder.Append(Constants.FuncKeyword);
            builder.AppendSpace();
        }

        builder.Append(this.Name);

        if (this.Captures is { } captures)
        {
            builder.Append('[');
            for (var i = 0; i < captures.Length; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                if (captures[i].IsMutable)
                {
                    builder.Append("var ");
                }

                builder.Append(captures[i].Name);
                if (captures[i].Operation is { } operation)
                {
                    builder.Append('@');
                    builder.Append(operation);
                }
            }

            builder.Append(']');
        }

        if (this.IsDestructor)
        {
            if (this.ExpressionBody is not null)
            {
                builder.Append(" => ");
                this.ExpressionBody.WriteTo(ref builder);
            }
            else
            {
                this.Body?.WriteIndentedTo(ref builder);
            }

            return;
        }

        if (this.genericArguments is { Count: > 0 } genericArguments)
        {
            builder.Append('<');
            for (var i = 0; i < genericArguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                genericArguments[i].WriteTo(ref builder);
            }

            builder.Append('>');
        }

        if (this.origins is { Count: > 0 })
        {
            builder.Append(" origin ");
            for (var i = 0; i < this.origins.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                builder.Append(this.origins[i]);
            }
        }

        builder.Append('(');
        if (this.parameters is { } parameters)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                var parameter = parameters[i];
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

                if (parameter.Type.Akind != KotoKind.InferredType)
                {
                    builder.Append(": ");
                    parameter.Type.WriteTo(ref builder);
                }

                if (parameter.DefaultValue is not null)
                {
                    builder.Append(" = ");
                    parameter.DefaultValue.WriteTo(ref builder);
                }
            }
        }

        builder.Append(')');
        if (this.BaseInitializer is not null)
        {
            builder.Append(" : ");
            this.BaseInitializer.WriteTo(ref builder);
        }

        if (this.ReturnType is not null)
        {
            builder.Append(" -> ");
            this.ReturnType.WriteTo(ref builder);
        }

        if (this.ExpressionBody is not null)
        {
            builder.Append(" => ");
            this.ExpressionBody.WriteTo(ref builder);
        }
        else if (this.typeConstraints is { Count: > 0 })
        {
            builder.AppendLine();
            builder.IncrementIndent();
            foreach (var constraint in this.typeConstraints)
            {
                constraint.WriteTo(ref builder);
                builder.AppendLine();
            }

            this.Body?.WriteTo(ref builder);
            builder.DecrementIndent();
        }
        else
        {
            this.Body?.WriteIndentedTo(ref builder);
        }
    }

    /// <summary>Adds top-level syntax to this generated function.</summary>
    /// <param name="item">The syntax node to add.</param>
    internal void AddGeneratedItem(Koto item)
    {
        if (!this.IsGenerated || this.Body is null)
        {
            throw new InvalidOperationException();
        }

        this.Body.AddLast(item);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.BaseInitializer is not null)
        {
            yield return this.BaseInitializer;
        }

        if (this.typeConstraints is not null)
        {
            foreach (var constraint in this.typeConstraints)
            {
                yield return constraint;
            }
        }

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

        if (this.ExpressionBody is not null)
        {
            yield return this.ExpressionBody;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.BaseInitializer == oldKoto && newKoto is InvocationKoto initializer)
        {
            this.BaseInitializer = initializer;
            return true;
        }

        if (this.ExpressionBody == oldKoto)
        {
            this.ExpressionBody = newKoto;
            return true;
        }

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

        return ReplaceInList(this.typeConstraints, oldKoto, newKoto) ||
            (oldKoto is TypeKoto && newKoto is TypeKoto && ReplaceInList(this.genericArguments, oldKoto, newKoto));
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
        this.Adopt(parameter.DefaultValue);
    }
}
