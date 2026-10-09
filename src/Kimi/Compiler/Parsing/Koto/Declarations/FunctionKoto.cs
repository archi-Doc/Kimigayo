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

    /// <summary>Gets the parameter type.</summary>
    public Koto Type { get; internal set; } = default!;

    /// <summary>Gets the default value, if present.</summary>
    public Koto? DefaultValue { get; internal set; }

    /// <summary>Gets the attributes applied to this parameter.</summary>
    public AttributeKoto? AttributeChain { get; internal set; }

    /// <summary>Gets the written external name's span for syntax evidence.</summary>
    internal SourceSpan ExternalNameSpan { get; init; }

    internal SourceSpan InternalNameSpan { get; init; }

    /// <summary>Initializes a new instance of the <see cref="FunctionParameterKoto"/> class.</summary>
    /// <param name="externalName">The caller-facing name.</param>
    /// <param name="internalName">The body-facing name.</param>
    /// <param name="type">The parameter type.</param>
    /// <param name="defaultValue">The default value, if present.</param>
    /// <param name="attributeChain">The parameter attributes, if present.</param>
    public FunctionParameterKoto(
        string externalName,
        string internalName,
        Koto type,
        Koto? defaultValue,
        AttributeKoto? attributeChain = null)
    {
        this.ExternalName = externalName;
        this.InternalName = internalName;
        this.Type = type;
        this.DefaultValue = defaultValue;
        this.AttributeChain = attributeChain;
    }
}

/// <summary>
/// Represents a function declaration.
/// </summary>
public sealed class FunctionKoto : DeclarationKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Function;

    /// <summary>Gets the function modifiers.</summary>
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets a value indicating whether this declaration introduces a virtual slot.</summary>
    public bool IsVirtual => (this.Modifier & ModifierKind.Virtual) != 0;

    /// <summary>Gets a value indicating whether this declaration supplies an inherited slot's implementation.</summary>
    public bool IsOverride => (this.Modifier & ModifierKind.Override) != 0;

    /// <summary>Gets the function name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the parsed signature span before the function's span is extended by its body.</summary>
    internal SourceSpan SignatureSpan { get; }

    internal SourceSpan DispatchModifierSpan { get; }

    // Anonymous bodies also inherit their enclosing function's substitution through every environment boundary.
    internal bool RequiresInstantiation => !this.IsSpecialization &&
        (this.GenericArguments.Count != 0 || this.BoundSymbol?.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 } ||
        (this.IsAnonymous && this.BoundSymbol?.Scope.Function?.RequiresInstantiation == true));

    private List<TypeKoto>? genericArguments;

    private List<FunctionParameterKoto>? parameters;
    private Dictionary<string, int>? parameterIndices;

    /// <summary>Gets the written parameter index after the ! boundary, or -1 when absent.</summary>
    public int NameBoundaryIndex { get; internal set; } = -1;

    /// <summary>Gets normalized K after Binding, or -1 for an unverified specialization.</summary>
    public int PositionalParameterCount
    {
        get
        {
            if (this.IsSpecialization)
            {
                return this.Kotonoha.Compilation.Binding.GetSpecializationOriginal(this)?.PositionalParameterCount ?? -1;
            }

            var limit = this.NameBoundaryIndex < 0 ? this.Parameters.Count : this.NameBoundaryIndex;
            var receiver = this.BoundSymbol?.ReceiverIndex ?? -1;
            return limit - (receiver >= 0 && receiver < limit ? 1 : 0);
        }
    }

    internal bool AllowsPositionalArgument(int index)
        => index == this.BoundSymbol?.ReceiverIndex || this.NameBoundaryIndex < 0 || index < this.NameBoundaryIndex;

    internal int MaxPositionalArguments(bool boundReceiver)
    {
        var limit = this.NameBoundaryIndex < 0 ? this.Parameters.Count : this.NameBoundaryIndex;
        var receiver = this.BoundSymbol?.ReceiverIndex ?? -1;
        if (receiver == limit)
        {
            limit++;
        }

        return limit - (boundReceiver && receiver >= 0 && receiver < limit ? 1 : 0);
    }

    internal int FindParameter(string name)
    {
        var parameters = this.parameters;
        if (parameters is null)
        {
            return -1;
        }

        // Small signatures are cheaper to scan. Larger signatures share one ordinal index.
        if (parameters.Count <= 8)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].ExternalName == name)
                {
                    return i;
                }
            }

            return -1;
        }

        if (this.parameterIndices is null)
        {
            this.parameterIndices = new(parameters.Count, StringComparer.Ordinal);
            for (var i = 0; i < parameters.Count; i++)
            {
                this.parameterIndices.TryAdd(parameters[i].ExternalName, i);
            }
        }

        return this.parameterIndices.GetValueOrDefault(name, -1);
    }

    internal bool TryMapArgument(string? label, ref int next, ref bool named, Span<bool> used, out int slot)
    {
        if (label is not null)
        {
            named = true;
            slot = this.FindParameter(label);
        }
        else
        {
            slot = -1;
            if (named)
            {
                return false;
            }

            // Only a receiver can already be supplied during the positional prefix.
            while (next < this.Parameters.Count && used[next])
            {
                next++;
            }

            slot = next++;
        }

        if ((uint)slot >= (uint)this.Parameters.Count || used[slot] ||
            (label is null && !this.AllowsPositionalArgument(slot)))
        {
            return false;
        }

        used[slot] = true;
        return true;
    }

    /// <summary>Gets the return type, if specified.</summary>
    public Koto? ReturnType { get; private set; }

    /// <summary>Gets the function body, if present.</summary>
    public CodeBlockKoto? Body { get; private set; }

    /// <summary>Gets a value indicating whether this function was synthesized for top-level syntax.</summary>
    public bool IsGenerated { get; private set; }

    /// <summary>Gets the expression after =>, if this function is expression-bodied.</summary>
    public Koto? ExpressionBody { get; private set; }

    /// <summary>Gets or sets a value indicating whether the body is missing because of a syntax Error; the checks of a body skip the function,
    /// since that Error explains everything its body would have established (SPEC 23.3.6.4).</summary>
    internal bool MissingBody { get; set; }

    private List<string>? origins;

    private List<Koto>? typeConstraints;

    private List<EffectBoundKoto>? effectBounds;

    /// <summary>Gets compile-time constraints declared before executable body items.</summary>
    public IReadOnlyList<Koto> TypeConstraints => (IReadOnlyList<Koto>?)this.typeConstraints ?? [];

    /// <summary>Gets the effect items written in the Constraint region (SPEC 8.4.10.1); only a Contract requirement may declare them.</summary>
    public IReadOnlyList<EffectBoundKoto> EffectBounds => (IReadOnlyList<EffectBoundKoto>?)this.effectBounds ?? [];

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

    /// <summary>Gets the checked direct common-function conversion, when present.</summary>
    public BoundClosure? BoundClosure => this.BindingState == BindingState.Resolved ? this.ClosureStorage : null;

    /// <summary>Gets the base constructor initializer.</summary>
    public InvocationKoto? BaseInitializer { get; private set; }

    private InvocationKoto? implicitBaseInitializer;

    /// <summary>Gets a value indicating whether <see cref="BaseInitializer"/> is the retained call of an omitted base clause.</summary>
    internal bool HasOmittedBaseInitializer => this.BaseInitializer is not null && ReferenceEquals(this.BaseInitializer, this.implicitBaseInitializer);

    internal void PrepareBaseInitializer()
    {
        if (this.BaseInitializer is not null && !ReferenceEquals(this.BaseInitializer, this.implicitBaseInitializer))
        {
            return;
        }

        this.BaseInitializer = null;
        if (this.IsConstructor && this.Parent is StructKoto { Bases.Count: > 0 })
        {
            if (this.implicitBaseInitializer is null)
            {
                var target = new SyntaxFormKoto(this);
                this.implicitBaseInitializer = new(this, target, []) { Span = this.SignatureSpan };
                target.Parent = this.implicitBaseInitializer;
            }

            this.SetBaseInitializer(this.implicitBaseInitializer);
        }
    }

    internal BoundClosure? ClosureStorage { get; set; }

    /// <summary>Gets or sets the end of the header, the closing parenthesis of the parameter list (SPEC 23.3.6.5 relates a closure end there).</summary>
    internal int HeaderEnd { get; set; }

    internal void SetCaptures(CaptureKoto[]? captures) => this.Captures = captures;

    internal void SetBaseInitializer(InvocationKoto initializer)
    {
        this.BaseInitializer = initializer;
        this.Adopt(initializer);
    }

    /// <summary>
    /// Gets the Origin parameters the signature introduces (SPEC 15.3.4). The parser never writes them, since a callable
    /// declares no Origin header of its own (SPEC 15.3.2); Binding supplies the names it discovers in the signature.
    /// </summary>
    public IReadOnlyList<string> Origins => (IReadOnlyList<string>?)this.origins ?? [];

    internal void SetOrigins(List<string>? origins) => this.origins = origins;

    internal void AddTypeConstraint(Koto constraint)
    {
        (this.typeConstraints ??= []).Add(constraint);
        this.Adopt(constraint);
    }

    internal void AddEffectBound(EffectBoundKoto effect)
    {
        (this.effectBounds ??= []).Add(effect);
        this.Adopt(effect);
    }

    /// <summary>Gets whether a spelling names one of this function's Type parameters or their Semantics parameters; the parser asks with the token's text, before any interning.</summary>
    /// <param name="name">The spelling.</param>
    /// <returns><see langword="true"/> when the function declares the parameter.</returns>
    internal bool IsGenericParameter(ReadOnlySpan<char> name)
        => this.genericArguments is not null && NamesParameter(this.genericArguments, name);

    // SPEC 7.4: a member function or constructor of a generic Type may constrain the declaring Type's parameters. The body
    // is parsed before the member is attached, so the container is recorded for the parse.
    internal bool IsDeclaringTypeParameter(ReadOnlySpan<char> name)
        => this.DeclaringContainer is StructKoto or EnumKoto && NamesParameter(this.DeclaringContainer.GenericParameterNodes, name);

    private static bool NamesParameter(IReadOnlyList<TypeKoto> parameters, ReadOnlySpan<char> name)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (name.SequenceEqual(parameters[i].Identifier) || (parameters[i].SemanticsParameter is { } semantics && name.SequenceEqual(semantics)))
            {
                return true;
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

    internal DeclarationContainerKoto? DeclaringContainer { get; set; }

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
        this.DispatchModifierSpan = context.DispatchModifierSpan;
        this.Name = name;
        this.SignatureSpan = range;
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

    internal FunctionKoto(StructKoto owner)
        : base(owner.CodeContext, owner.Span)
    {
        this.Name = "init";
        this.IsConstructor = true;
        this.Modifier = ModifierKind.Public;
        this.Parent = owner;
        this.Body = new CodeBlockKoto(owner.CodeContext) { Parent = this };
    }

    // Non-owning execution view: accessor syntax, source boundaries and Origin binders
    // remain unchanged. Ownership and native lowering share the ordinary function ABI.
    internal FunctionKoto(BoundAccessor accessor)
        : base(accessor.Declaration!.CodeContext, accessor.Declaration.Span)
    {
        this.Accessor = accessor;
        this.Name = accessor.Property.Symbol.Name + "." + accessor.Kind;
        this.Parent = accessor.Declaration.Parent;
        this.parameters = new();
        if (accessor.Receiver is not null)
        {
            this.parameters.Add(new("self", "self", new IdentifierNameKoto(accessor.Declaration, "self"), null));
        }

        if (accessor.Input is not null)
        {
            this.parameters.Add(new("value", "value", new IdentifierNameKoto(accessor.Declaration, "value"), null));
        }

        foreach (var parameter in this.parameters)
        {
            this.AttachParameter(parameter);
        }
    }

    internal BoundAccessor? Accessor { get; }

    internal bool IsPropertyWitness { get; private set; }

    internal void SetPropertyWitnessBody(Koto expression, BoundType result)
    {
        if (!this.IsPropertyWitness)
        {
            this.ReturnType = new IdentifierNameKoto(expression, "result") { Parent = this };
        }

        this.IsPropertyWitness = true;
        this.IsRequirement = false;
        this.Body = null;
        this.ExpressionBody = expression;
        expression.Parent = this;
        this.ReturnType!.BoundType = result;
        this.ReturnType.BindingState = BindingState.Resolved;
    }

    // An execution view of a static initializer, retaining its original syntax and source location.
    internal FunctionKoto(BoundProperty property)
        : base(property.Declaration.CodeContext, property.Declaration.Span)
    {
        this.StaticInitializer = property;
        this.Name = property.Symbol.Name + ".initialize";
        this.Parent = property.Declaration.Parent;
        this.BoundSymbol = new(this.Name, BindingSymbolKind.Function, this, property.Symbol.Scope);
        this.RefreshStaticInitializer();
    }

    internal BoundProperty? StaticInitializer { get; }

    /// <summary>Consumes the function body.</summary>
    /// <param name="reader">The token reader.</param>
    public void Parse(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan)
        {
            this.ExpressionBody = Parser.ParseSingleBodyItem(ref reader);
            this.Adopt(this.ExpressionBody);
            this.Span = SourceSpan.FromBounds(this.Span.Start, this.ExpressionBody.Span.End);
            return;
        }

        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.Expect(SyntaxForm.Body);
            this.MissingBody = true;
            return;
        }

        this.Body = Parser.ParseFunctionBlock(ref reader, this);
        this.Body.Parent = this;
        this.Span = SourceSpan.FromBounds(this.Span.Start, this.Body.Span.End);
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
        => this.WriteDeclarationTo(ref builder, headerOnly: false);

    /// <summary>Writes the published signature without bodies or default expressions.</summary>
    /// <param name="builder">The output.</param>
    internal void WriteHeaderTo(ref IndentedStringBuilder builder)
        => this.WriteDeclarationTo(ref builder, headerOnly: true);

    internal void RefreshStaticInitializer()
    {
        this.ExpressionBody = this.StaticInitializer!.Declaration.InitializerKoto;
        this.ReturnType = this.StaticInitializer.Declaration.TypeKoto;
        this.BoundSymbol!.Type = this.StaticInitializer.Type;
        this.BindingState = BindingState.Resolved;
    }

    internal void RefreshAccessor()
    {
        var accessor = this.Accessor!;
        this.Body = accessor.Declaration!.Body as CodeBlockKoto;
        this.ExpressionBody = this.Body is null ? accessor.Declaration.Body : null;
        if (!this.IsPropertyWitness)
        {
            this.ReturnType = accessor.Declaration.ReturnType;
        }

        for (var i = 0; i < this.Parameters.Count; i++)
        {
            this.Parameters[i].Type.BoundType = i == 0 && accessor.Receiver is not null ? accessor.Receiver : accessor.Input;
            this.Parameters[i].Type.BindingState = BindingState.Resolved;
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

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        if (this.BaseInitializer is not null)
        {
            visitor.Visit(this.BaseInitializer);
        }

        if (this.typeConstraints is not null)
        {
            for (var constraintIndex = 0; constraintIndex < this.typeConstraints.Count; constraintIndex++)
            {
                var constraint = this.typeConstraints[constraintIndex];
                visitor.Visit(constraint);
            }
        }

        visitor.VisitMany(this.effectBounds);

        if (this.genericArguments is not null)
        {
            for (var argumentIndex = 0; argumentIndex < this.genericArguments.Count; argumentIndex++)
            {
                var argument = this.genericArguments[argumentIndex];
                visitor.Visit(argument);
            }
        }

        if (this.parameters is not null)
        {
            for (var parameterIndex = 0; parameterIndex < this.parameters.Count; parameterIndex++)
            {
                var parameter = this.parameters[parameterIndex];
                if (parameter.AttributeChain is not null)
                {
                    visitor.Visit(parameter.AttributeChain);
                }

                visitor.Visit(parameter.Type);
                if (parameter.DefaultValue is not null)
                {
                    visitor.Visit(parameter.DefaultValue);
                }
            }
        }

        if (this.ReturnType is not null)
        {
            visitor.Visit(this.ReturnType);
        }

        if (this.Body is not null)
        {
            visitor.Visit(this.Body);
        }

        if (this.ExpressionBody is not null)
        {
            visitor.Visit(this.ExpressionBody);
        }
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

        if (this.effectBounds is not null)
        {
            foreach (var effect in this.effectBounds)
            {
                yield return effect;
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

        return ReplaceInList(this.typeConstraints, oldKoto, newKoto) || ReplaceInList(this.effectBounds, oldKoto, newKoto) ||
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

    private void WriteDeclarationTo(ref IndentedStringBuilder builder, bool headerOnly)
    {
        if (this.IsGenerated && !headerOnly)
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
            if (headerOnly)
            {
                OriginClauses.Write(this, ref builder);
                return;
            }

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

        var multilineParameters = false;
        if (!headerOnly && this.parameters is { } declaredParameters)
        {
            for (var i = 0; i < declaredParameters.Count; i++)
            {
                if (declaredParameters[i].DefaultValue is { } value && KotoHelper.ContainsBody(value))
                {
                    multilineParameters = true;
                    break;
                }
            }
        }

        builder.Append('(');
        if (multilineParameters)
        {
            builder.AppendLine();
            builder.IncrementIndent();
        }

        if (this.parameters is { } parameters)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                if (multilineParameters && i > 0)
                {
                    // Both separators belong outside any preceding default's indented body.
                    builder.AppendLine();
                }

                if (i == this.NameBoundaryIndex)
                {
                    builder.Append(i == 0 || multilineParameters ? "! " : " ! ");
                }
                else if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                var parameter = parameters[i];
                if (parameter.AttributeChain is not null)
                {
                    Parser.UnparseAttribute(parameter.AttributeChain, ref builder, KotoWriteOptions.AppendSpace);
                }

                builder.Append(parameter.ExternalName);
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
                    if (headerOnly)
                    {
                        builder.Append("<default omitted>");
                    }
                    else
                    {
                        parameter.DefaultValue.WriteTo(ref builder);
                    }
                }
            }
        }

        if (multilineParameters)
        {
            builder.AppendLine();
            builder.DecrementIndent();
        }

        builder.Append(')');
        if (!headerOnly && this.BaseInitializer is not null && !ReferenceEquals(this.BaseInitializer, this.implicitBaseInitializer))
        {
            builder.Append(" : ");
            this.BaseInitializer.WriteTo(ref builder);
        }

        if (this.ReturnType is not null)
        {
            builder.Append(" -> ");
            this.ReturnType.WriteTo(ref builder);
        }

        if (!headerOnly && this.ExpressionBody is not null)
        {
            builder.Append(" => ");
            this.ExpressionBody.WriteTo(ref builder);
        }
        else if (this.typeConstraints is { Count: > 0 } || this.effectBounds is { Count: > 0 } || OriginClauses.Get(this).Count != 0)
        {
            builder.AppendLine();
            builder.IncrementIndent();
            OriginClauses.Write(this, ref builder, false);
            foreach (var constraint in this.TypeConstraints)
            {
                constraint.WriteTo(ref builder);
                builder.AppendLine();
            }

            foreach (var effect in this.EffectBounds)
            {
                effect.WriteTo(ref builder);
                builder.AppendLine();
            }

            if (!headerOnly)
            {
                this.Body?.WriteTo(ref builder);
            }

            builder.DecrementIndent();
        }
        else if (!headerOnly)
        {
            this.Body?.WriteIndentedTo(ref builder);
        }
    }
}
