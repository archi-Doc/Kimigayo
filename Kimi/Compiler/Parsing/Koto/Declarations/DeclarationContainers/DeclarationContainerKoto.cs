// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;
#pragma warning disable SA1202 // Serialization keys keep related storage together.
#pragma warning disable SA1204 // Parsing helpers are grouped by responsibility.

/// <summary>
/// Provides shared storage and parsing for Kimigayo Declaration Container nodes.
/// </summary>
[TinyhandObject]
public abstract partial class DeclarationContainerKoto : IdentifiableKoto
{
    protected enum DeclarationOrder : byte
    {
        None,
        TypeConstraint,
        Property,
        Function,
    }

    /// <inheritdoc/>
    public abstract override KotoKind Akind { get; }

    #region FieldAndProperty

    /// <summary>Gets the declaration modifiers.</summary>
    [Key(3)]
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets or sets the Declaration Container name.</summary>
    [Key(4)]
    public string Name { get; protected set; } = string.Empty;

    /// <summary>Gets the declaration keyword kind for the concrete Declaration Container type.</summary>
    public abstract TokenKind TokenKind { get; }

    /// <summary>Gets a value indicating whether the Declaration Container can be instantiated.</summary>
    public abstract bool IsInstantiable { get; }

    /// <summary>Gets a value indicating whether every member is static.</summary>
    public virtual bool HasStaticMembersOnly => false;

    /// <summary>Gets a value indicating whether generic parameters are supported.</summary>
    public virtual bool SupportsGenerics => false;

    /// <summary>Gets a value indicating whether origins are supported.</summary>
    public virtual bool SupportsOrigins => false;

    /// <summary>Gets a value indicating whether type constraints are supported.</summary>
    public virtual bool SupportsTypeConstraints => false;

    [Key(5)]
    private List<Koto>? kotoList;

    [Key(7)]
    private List<TypeKoto>? genericArguments;

    [Key(8)]
    private List<IsKoto>? typeConstraints;

    /// <summary>Gets the generic parameters.</summary>
    [IgnoreMember]
    public List<TypeKoto> GenericArguments => this.genericArguments ??= [];

    /// <summary>Gets the type constraints.</summary>
    [IgnoreMember]
    public List<IsKoto> TypeConstraints => this.typeConstraints ??= [];

    /// <summary>Gets the declared origins.</summary>
    [Key(9)]
    public List<string> Origins { get; private set; } = [];

    protected List<Koto> KotoList => this.kotoList ??= [];

    [Key(6)]
    protected Utf16Hashtable<Koto> IdentifierToDeclarationContainerKoto { get; set; } = new();

    /// <summary>Gets Properties and functions in declaration order.</summary>
    [IgnoreMember]
    public IReadOnlyList<Koto> Members => this.KotoList;

    /// <summary>Gets nested Declaration Containers.</summary>
    [IgnoreMember]
    public IEnumerable<DeclarationContainerKoto> NestedDeclarationContainers
        => this.IdentifierToDeclarationContainerKoto.ToArray().Cast<DeclarationContainerKoto>();

    #endregion

    /// <summary>Initializes a new instance of the <see cref="DeclarationContainerKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    protected DeclarationContainerKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DeclarationContainerKoto"/> class.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    protected DeclarationContainerKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, range)
    {
        this.SetAttributeChain(state.AttributeKoto);
        this.Modifier = state.ModifierKind;
    }

    /// <inheritdoc/>
    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    /// <summary>Adds a child node to this Declaration Container.</summary>
    /// <param name="koto">The child node to add.</param>
    public void AddLast(Koto koto)
    {
        this.KotoList.Add(koto);
        koto.Parent = this;
    }

    /// <summary>Adds generic parameters to this Declaration Container.</summary>
    /// <param name="genericArguments">The generic parameters to add.</param>
    public void AddGenericArguments(IEnumerable<TypeKoto> genericArguments)
    {
        if (!this.SupportsGenerics)
        {
            return;
        }

        foreach (var argument in genericArguments)
        {
            this.GenericArguments.Add(argument);
            argument.Parent = this;
        }
    }

    /// <summary>Adds a type constraint to this Declaration Container.</summary>
    /// <param name="constraint">The constraint to add.</param>
    public void AddTypeConstraint(IsKoto constraint)
    {
        if (!this.SupportsTypeConstraints)
        {
            return;
        }

        this.TypeConstraints.Add(constraint);
        constraint.Parent = this;
    }

    /// <summary>Adds Origin names to this Declaration Container.</summary>
    /// <param name="origins">The origin names to add.</param>
    public void AddOrigins(IEnumerable<string> origins)
    {
        if (this.SupportsOrigins)
        {
            this.Origins.AddRange(origins);
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (this.IsRoot)
        {
            return Constants.RootKotoName;
        }

        return $"{this.TokenKind.ToText()} {this.Name}";
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
        }

        if (this.IsRoot)
        {
            builder.Append(Constants.RootKotoName);
            return;
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.TokenKind.ToText());
        builder.Append(' ');
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

        if (this.Origins.Count > 0)
        {
            builder.AppendSpace();
            builder.Append(Constants.OriginKeyword);
            builder.AppendSpace();
            for (var i = 0; i < this.Origins.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                builder.Append(this.Origins[i]);
            }
        }
    }

    /// <summary>Writes a root-group declaration for this group.</summary>
    /// <param name="builder">The destination builder.</param>
    public void UnparseToRoot(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
            builder.AppendLine();
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(Constants.RootgroupKeyword);
        builder.Append(' ');
        Parser.WriteQualifiedNameTo(this, ref builder);
    }

    /// <summary>Removes all declarations and Declaration Container metadata.</summary>
    public void Clear()
    {
        this.KotoList.Clear();
        this.IdentifierToDeclarationContainerKoto.Clear();
        this.GenericArguments.Clear();
        this.TypeConstraints.Clear();
        this.Origins.Clear();
        if (ReferenceEquals(this, this.Kotonoha.RootKoto))
        {
            this.Kotonoha.ClearGeneratedFunction();
        }
    }

    /// <summary>Writes this Declaration Container and all nested Declaration Containers as source text.</summary>
    /// <param name="builder">The destination builder.</param>
    public void UnparseAll(ref IndentedStringBuilder builder)
    {
        this.UnparseAllInternal(ref builder);
    }

    /// <summary>Gets or creates a nested Declaration Container from a qualified name.</summary>
    /// <param name="qualifiedName">The dot-separated Declaration Container name.</param>
    /// <param name="kind">The final Declaration Container's declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <returns>The final Declaration Container.</returns>
    public DeclarationContainerKoto GetOrAddDeclarationContainer(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var text = qualifiedName;
        var container = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                GetOrAddDeclarationContainer(ref container, text, kind, state, range);
                return container;
            }

            var segment = text[..index];
            GetOrAddDeclarationContainer(ref container, segment, TokenKind.Group, default, default);
            text = text[(index + 1)..];
        }
    }

    /// <summary>Gets or creates a Declaration Container from a qualified name.</summary>
    /// <remarks>Retained as a source-compatible alias for <c>GetOrAddDeclarationContainer</c>.</remarks>
    /// <param name="qualifiedName">The dot-separated Declaration Container name.</param>
    /// <param name="kind">The final Declaration Container's declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <returns>The final Declaration Container.</returns>
    public DeclarationContainerKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range)
        => this.GetOrAddDeclarationContainer(qualifiedName, kind, state, range);

    /// <summary>Parses the body supported by this Declaration Container kind.</summary>
    /// <param name="reader">The token reader.</param>
    public abstract void Parse(ref TokenReader reader);

    internal static DeclarationContainerKoto CreateStandalone(CodeContext codeContext, TokenKind kind, TokenContext state, SourceSpan range, string name)
    {
        DeclarationContainerKoto container = kind switch
        {
            TokenKind.Struct => new StructKoto(codeContext, state, range),
            TokenKind.Enum => new EnumKoto(codeContext, state, range),
            TokenKind.Extension => new ExtensionKoto(codeContext, state, range),
            TokenKind.Contract => new ContractKoto(codeContext, state, range),
            _ => new GroupKoto(codeContext, state, range),
        };

        container.Name = name;
        return container;
    }

    internal void WriteAsBlockItem(ref IndentedStringBuilder builder)
    {
        this.WriteTo(ref builder);
        var containers = this.IdentifierToDeclarationContainerKoto.ToArray();
        if (this.TypeConstraints.Count == 0 && this.KotoList.Count == 0 && containers.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.IncrementIndent();
        var hasPrevious = false;
        foreach (var constraint in this.TypeConstraints)
        {
            WriteSeparator(ref builder, ref hasPrevious);
            this.WriteTypeConstraintTo(constraint, ref builder);
        }

        foreach (var koto in this.KotoList)
        {
            WriteSeparator(ref builder, ref hasPrevious);
            koto.WriteTo(ref builder);
        }

        foreach (var nested in containers)
        {
            WriteSeparator(ref builder, ref hasPrevious);
            ((DeclarationContainerKoto)nested).WriteAsBlockItem(ref builder);
        }

        builder.DecrementIndent();

        static void WriteSeparator(ref IndentedStringBuilder builder, ref bool hasPrevious)
        {
            if (hasPrevious)
            {
                builder.AppendLine();
            }
            else
            {
                hasPrevious = true;
            }
        }
    }

    /// <summary>Parses Properties, functions, and optionally ordinary type constraints.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="parseTypeConstraints">Whether ordinary type constraints are accepted.</param>
    protected void ParsePropertyAndFunctionMembers(ref TokenReader reader, bool parseTypeConstraints)
    {
        ConsumeBlockStart(ref reader);
        var declarationOrder = DeclarationOrder.None;
        var acceptsTypeConstraints = parseTypeConstraints && this.TypeConstraints.Count == 0;
        while (TryBeginDeclaration(ref reader))
        {
            if (parseTypeConstraints && Parser.IsTypeConstraintStart(ref reader))
            {
                if (!acceptsTypeConstraints)
                {
                    reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.DuplicateTypeConstraintDefinition_Kd);
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                    continue;
                }

                CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.TypeConstraint);
                var constraint = Parser.ParseTypeConstraint(ref reader);
                if (constraint is not null)
                {
                    this.AddTypeConstraint(constraint);
                }

                continue;
            }

            var token = reader.CurrentToken;
            if (!this.TryParsePropertyOrFunction(ref reader, ref declarationOrder))
            {
                SkipUnexpectedDeclaration(ref reader, token);
            }
        }
    }

    /// <summary>Consumes an unimplemented Declaration Container body without producing members.</summary>
    /// <param name="reader">The token reader.</param>
    protected static void SkipUnimplementedBody(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.SkipCurrentBlock(false);
            return;
        }

        var depth = 0;
        while (reader.CanRead)
        {
            if (reader.CurrentTokenKind == TokenKind.StartBlock)
            {
                depth++;
            }
            else if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                if (depth == 0)
                {
                    reader.Advance();
                    return;
                }

                depth--;
            }

            reader.Advance();
        }
    }

    /// <summary>Consumes the opening block token when the caller left it for the Declaration Container parser.</summary>
    /// <param name="reader">The token reader.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ConsumeBlockStart(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.Advance();
        }
    }

    /// <summary>Consumes declaration trivia and detects the end of the current Declaration Container body.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns><see langword="true"/> when another declaration is available.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryBeginDeclaration(ref TokenReader reader)
    {
        Parser.ConsumeAttributeAndModifier(ref reader, out var isEnd);
        if (isEnd)
        {
            return false;
        }

        if (reader.CurrentTokenKind == TokenKind.EndBlock)
        {
            reader.Advance();
            return false;
        }

        return true;
    }

    /// <summary>Attempts to parse one Property or function declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="declarationOrder">The current declaration-order state.</param>
    /// <returns><see langword="true"/> when a supported member was consumed.</returns>
    protected bool TryParsePropertyOrFunction(ref TokenReader reader, ref DeclarationOrder declarationOrder)
    {
        var token = reader.CurrentToken;
        if (token.Kind is TokenKind.Let or TokenKind.Var)
        {
            CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Property);
            reader.Advance();
            var propertyKoto = Parser.ParseProperty(ref reader, ref token);
            if (propertyKoto is not null)
            {
                this.AddLast(propertyKoto);
            }

            return true;
        }

        if (token.Kind != TokenKind.Func)
        {
            return false;
        }

        CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Function);
        reader.Advance();
        var functionKoto = Parser.ParseFuncDeclaration(ref reader);
        if (functionKoto is null)
        {
            return true;
        }

        if (!functionKoto.IsExcluded)
        {
            this.AddLast(functionKoto);
        }

        var functionBodyReader = reader;
        while (functionBodyReader.CurrentTokenKind == TokenKind.Separator)
        {
            functionBodyReader.Advance();
        }

        if (functionBodyReader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader = functionBodyReader;
            functionKoto.Parse(ref reader);
        }

        return true;
    }

    /// <summary>Reports and skips a declaration unsupported by the current Declaration Container kind.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The unsupported declaration's first token.</param>
    protected static void SkipUnexpectedDeclaration(ref TokenReader reader, Token token)
    {
        reader.Diagnostic.Add(
            token.Span,
            DiagnosticCode.UnexpectedToken_Kd,
            reader.GetSpan(token).ToString());
        reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
        while (reader.CurrentTokenKind == TokenKind.Separator)
        {
            reader.Advance();
        }

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.SkipCurrentBlock(false);
        }

        reader.ClearContext();
    }

    /// <summary>Writes one type constraint in the syntax used by this Declaration Container kind.</summary>
    /// <param name="constraint">The constraint to write.</param>
    /// <param name="builder">The destination builder.</param>
    protected virtual void WriteTypeConstraintTo(IsKoto constraint, ref IndentedStringBuilder builder)
        => constraint.WriteTo(ref builder);

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var argument in this.GenericArguments)
        {
            yield return argument;
        }

        foreach (var constraint in this.TypeConstraints)
        {
            yield return constraint;
        }

        foreach (var koto in this.KotoList)
        {
            yield return koto;
        }

        foreach (var container in this.IdentifierToDeclarationContainerKoto.ToArray())
        {
            yield return container;
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        var index = this.KotoList.IndexOf(oldKoto);
        if (index >= 0)
        {
            this.KotoList[index] = newKoto;
            return true;
        }

        if (oldKoto is TypeKoto oldType && newKoto is TypeKoto newType)
        {
            var typeIndex = this.GenericArguments.IndexOf(oldType);
            if (typeIndex >= 0)
            {
                this.GenericArguments[typeIndex] = newType;
                return true;
            }
        }

        if (oldKoto is IsKoto oldConstraint && newKoto is IsKoto newConstraint)
        {
            var constraintIndex = this.TypeConstraints.IndexOf(oldConstraint);
            if (constraintIndex >= 0)
            {
                this.TypeConstraints[constraintIndex] = newConstraint;
                return true;
            }
        }

        if (oldKoto is DeclarationContainerKoto oldContainer && newKoto is DeclarationContainerKoto newContainer &&
            this.IdentifierToDeclarationContainerKoto.TryGetValue(oldContainer.Name, out var registered) &&
            ReferenceEquals(registered, oldContainer))
        {
            if (!oldContainer.Name.Equals(newContainer.Name, StringComparison.Ordinal) &&
                this.IdentifierToDeclarationContainerKoto.TryGetValue(newContainer.Name, out _))
            {
                return false;
            }

            if (this.IdentifierToDeclarationContainerKoto.TryRemove(oldContainer.Name) &&
                this.IdentifierToDeclarationContainerKoto.TryAdd(newContainer.Name, newContainer))
            {
                return true;
            }

            this.IdentifierToDeclarationContainerKoto.TryAdd(oldContainer.Name, oldContainer);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void CheckDeclarationOrder(ref TokenReader reader, ref DeclarationOrder current, DeclarationOrder next)
    {
        if (next < current)
        {
            reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.DeclarationOrderWarning_Kd);
            current = next;
        }
        else
        {
            current = next;
        }
    }

    private static void GetOrAddDeclarationContainer(ref DeclarationContainerKoto container, ReadOnlySpan<char> text, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var parent = container;
        if (container.IdentifierToDeclarationContainerKoto.TryGetValue(text, out var existing))
        {
            container = (DeclarationContainerKoto)existing;
            container.Merge(state, range);
            return;
        }

        var name = text.ToString();
        container = CreateStandalone(parent.CodeContext, kind, state, range, name);
        container.Parent = parent;
        parent.IdentifierToDeclarationContainerKoto.Add(name, container);
    }

    private void Merge(TokenContext state, SourceSpan range)
    {
    }

    private void UnparseAllInternal(ref IndentedStringBuilder builder)
    {
        var containerDeclared = false;

        if ((!this.IsRoot && (this.KotoList.Count > 0 || this.TypeConstraints.Count > 0 || this.Origins.Count > 0))
            || this.Modifier != 0)
        {
            builder.EnsureTrailingBlankLine();
            if (this.Akind == KotoKind.Group)
            {
                builder.SetIndent(0);
                this.UnparseToRoot(ref builder);
                builder.AppendLine();
                builder.IncrementIndent();
            }
            else
            {
                this.WriteTo(ref builder);
                builder.AppendLine();
                builder.IncrementIndent();
            }

            containerDeclared = true;
        }

        if (this.TypeConstraints.Count > 0)
        {
            foreach (var constraint in this.TypeConstraints)
            {
                this.WriteTypeConstraintTo(constraint, ref builder);
                builder.AppendLine();
            }

            if (this.KotoList.Count > 0)
            {
                builder.AppendLine();
            }
        }

        if (this.KotoList.Count > 0)
        {
            var previousToplevel = false;
            foreach (var x in this.KotoList)
            {
                if (!x.IsToplevel && previousToplevel)
                {
                    builder.AppendLine();
                }

                x.WriteTo(ref builder);
                builder.AppendLine();

                previousToplevel = x.IsToplevel;
            }
        }

        if (this.IsRoot && this.Kotonoha.GeneratedFunction is { Body.Items.Count: > 0 } generatedFunction)
        {
            if (this.KotoList.Count > 0)
            {
                builder.AppendLine();
            }

            generatedFunction.WriteTo(ref builder);
            builder.AppendLine();
        }

        var containers = this.IdentifierToDeclarationContainerKoto.ToArray();
        if (containers.Length > 0)
        {
            builder.EnsureTrailingBlankLine();
            foreach (var x in containers)
            {
                ((DeclarationContainerKoto)x).UnparseAllInternal(ref builder);
            }
        }

        if (containerDeclared)
        {
            builder.DecrementIndent();
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        foreach (var argument in this.GenericArguments)
        {
            argument.Parent = this;
        }

        foreach (var constraint in this.TypeConstraints)
        {
            constraint.Parent = this;
        }

        foreach (var koto in this.KotoList)
        {
            koto.Parent = this;
        }

        foreach (var container in this.IdentifierToDeclarationContainerKoto.ToArray())
        {
            container.Parent = this;
        }
    }
}
