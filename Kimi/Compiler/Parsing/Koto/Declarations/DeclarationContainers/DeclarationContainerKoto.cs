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
/// <remarks>
/// Member collections are allocated on first use because most containers only hold a few
/// of the possible member kinds.
/// </remarks>
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

    /// <summary>Gets or sets the nested Declaration Containers keyed by name, or <see langword="null"/> when none exist.</summary>
    [Key(6)]
    protected Utf16Hashtable<Koto>? NestedContainerTable { get; set; }

    [Key(7)]
    private List<TypeKoto>? genericArguments;

    [Key(8)]
    private List<IsKoto>? typeConstraints;

    /// <summary>Gets or sets the declared origins, or <see langword="null"/> when none exist.</summary>
    [Key(9)]
    protected List<string>? OriginList { get; set; }

    /// <summary>Gets the generic parameters.</summary>
    [IgnoreMember]
    public List<TypeKoto> GenericArguments => this.genericArguments ??= [];

    /// <summary>Gets the type constraints.</summary>
    [IgnoreMember]
    public List<IsKoto> TypeConstraints => this.typeConstraints ??= [];

    /// <summary>Gets the declared origins.</summary>
    [IgnoreMember]
    public List<string> Origins => this.OriginList ??= [];

    /// <summary>Gets Properties and functions in declaration order.</summary>
    [IgnoreMember]
    public IReadOnlyList<Koto> Members => (IReadOnlyList<Koto>?)this.kotoList ?? [];

    /// <summary>Gets the mutable member list, creating it on first use.</summary>
    protected List<Koto> KotoList => this.kotoList ??= [];

    /// <summary>Gets nested Declaration Containers.</summary>
    [IgnoreMember]
    public IEnumerable<DeclarationContainerKoto> NestedDeclarationContainers
        => this.NestedContainerTable?.ToArray().Cast<DeclarationContainerKoto>() ?? [];

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

    /// <summary>Applies a parsed declaration header when the corresponding member kind is still empty.</summary>
    /// <param name="genericArguments">The generic parameters, if declared.</param>
    /// <param name="origins">The origin names, if declared.</param>
    internal void AddHeader(List<TypeKoto>? genericArguments, List<string>? origins)
    {
        if (this.SupportsGenerics && genericArguments is not null && this.genericArguments is not { Count: > 0 })
        {
            if (this.genericArguments is null)
            {
                this.genericArguments = genericArguments;
            }
            else
            {
                this.genericArguments.AddRange(genericArguments);
            }

            this.Adopt(genericArguments);
        }

        if (this.SupportsOrigins && origins is not null && this.OriginList is not { Count: > 0 })
        {
            if (this.OriginList is null)
            {
                this.OriginList = origins;
            }
            else
            {
                this.OriginList.AddRange(origins);
            }
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
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendLineFeed);

        if (this.IsRoot)
        {
            builder.Append(Constants.RootKotoName);
            return;
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(this.TokenKind.ToText());
        builder.Append(' ');
        builder.Append(this.Name);

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

        if (this.OriginList is { Count: > 0 } origins)
        {
            builder.AppendSpace();
            builder.Append(Constants.OriginKeyword);
            builder.AppendSpace();
            for (var i = 0; i < origins.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                builder.Append(origins[i]);
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
        this.kotoList?.Clear();
        this.NestedContainerTable?.Clear();
        this.genericArguments?.Clear();
        this.typeConstraints?.Clear();
        this.OriginList?.Clear();
        if (ReferenceEquals(this, this.Kotonoha.RootKoto))
        {
            this.Kotonoha.ClearGeneratedFunction();
        }
    }

    /// <summary>Writes this Declaration Container and all nested Declaration Containers as source text.</summary>
    /// <param name="builder">The destination builder.</param>
    public void UnparseAll(ref IndentedStringBuilder builder)
    {
        var containerDeclared = false;

        if ((!this.IsRoot && (this.kotoList is { Count: > 0 } || this.typeConstraints is { Count: > 0 } || this.OriginList is { Count: > 0 }))
            || this.Modifier != 0)
        {
            builder.EnsureTrailingBlankLine();
            if (this.Akind == KotoKind.Group)
            {
                builder.SetIndent(0);
                this.UnparseToRoot(ref builder);
            }
            else
            {
                this.WriteTo(ref builder);
            }

            builder.AppendLine();
            builder.IncrementIndent();
            containerDeclared = true;
        }

        if (this.typeConstraints is { Count: > 0 } typeConstraints)
        {
            foreach (var constraint in typeConstraints)
            {
                this.WriteTypeConstraintTo(constraint, ref builder);
                builder.AppendLine();
            }

            if (this.kotoList is { Count: > 0 })
            {
                builder.AppendLine();
            }
        }

        if (this.kotoList is { Count: > 0 } kotoList)
        {
            var previousToplevel = false;
            foreach (var x in kotoList)
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
            if (this.kotoList is { Count: > 0 })
            {
                builder.AppendLine();
            }

            generatedFunction.WriteTo(ref builder);
            builder.AppendLine();
        }

        if (this.NestedContainerTable is { Count: > 0 } nestedContainers)
        {
            builder.EnsureTrailingBlankLine();
            foreach (var x in nestedContainers.ToArray())
            {
                ((DeclarationContainerKoto)x).UnparseAll(ref builder);
            }
        }

        if (containerDeclared)
        {
            builder.DecrementIndent();
        }
    }

    /// <summary>Gets or creates a nested Declaration Container from a qualified name.</summary>
    /// <param name="qualifiedName">The dot-separated Declaration Container name.</param>
    /// <param name="kind">The final Declaration Container's declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <returns>The final Declaration Container.</returns>
    public DeclarationContainerKoto GetOrAddDeclarationContainer(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var container = this;
        while (true)
        {
            var index = qualifiedName.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                return container.GetOrAddChild(qualifiedName, null, kind, state, range);
            }

            container = container.GetOrAddChild(qualifiedName[..index], null, TokenKind.Group, default, default);
            qualifiedName = qualifiedName[(index + 1)..];
        }
    }

    /// <summary>Gets or creates a directly nested Declaration Container from a simple name.</summary>
    /// <param name="name">The Declaration Container name without dots.</param>
    /// <param name="kind">The declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <returns>The nested Declaration Container.</returns>
    internal DeclarationContainerKoto GetOrAddDeclarationContainer(string name, TokenKind kind, TokenContext state, SourceSpan range)
        => name.Contains(Constants.DotChar)
            ? this.GetOrAddDeclarationContainer(name.AsSpan(), kind, state, range)
            : this.GetOrAddChild(name, name, kind, state, range);

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
        var containers = this.NestedContainerTable?.ToArray() ?? [];
        if (this.typeConstraints is not { Count: > 0 } && this.kotoList is not { Count: > 0 } && containers.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.IncrementIndent();
        var hasPrevious = false;
        if (this.typeConstraints is not null)
        {
            foreach (var constraint in this.typeConstraints)
            {
                WriteSeparator(ref builder, ref hasPrevious);
                this.WriteTypeConstraintTo(constraint, ref builder);
            }
        }

        if (this.kotoList is not null)
        {
            foreach (var koto in this.kotoList)
            {
                WriteSeparator(ref builder, ref hasPrevious);
                koto.WriteTo(ref builder);
            }
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

    /// <summary>Parses the member declarations of a block body.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="parseTypeConstraints">Whether ordinary type constraints are accepted.</param>
    /// <param name="parseDeclarationContainers">Whether nested Declaration Containers are accepted.</param>
    protected void ParseMembers(ref TokenReader reader, bool parseTypeConstraints, bool parseDeclarationContainers)
    {
        ConsumeBlockStart(ref reader);
        var declarationOrder = DeclarationOrder.None;
        var acceptsTypeConstraints = parseTypeConstraints && this.typeConstraints is not { Count: > 0 };
        while (TryBeginDeclaration(ref reader))
        {
            var isExcluded = reader.IsExcluded;
            var compileTimeIfPrefixes = reader.TakeCompileTimeIfPrefixes();
            if (isExcluded)
            {
                Parser.SkipExcludedSyntax(ref reader);
                continue;
            }

            if (Parser.IsCompileTimeCaseStart(ref reader))
            {
                var caseGroup = Parser.ParseCompileTimeCaseGroup(ref reader);
                this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, caseGroup));
                continue;
            }

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
                if (constraint is not null && !isExcluded)
                {
                    if (compileTimeIfPrefixes is null)
                    {
                        this.AddTypeConstraint(constraint);
                    }
                    else
                    {
                        this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, constraint));
                    }
                }

                continue;
            }

            var token = reader.CurrentToken;
            if (parseDeclarationContainers &&
                this.TryParseDeclarationContainer(ref reader, token, compileTimeIfPrefixes, isExcluded))
            {
                continue;
            }

            if (!this.TryParsePropertyOrFunction(
                ref reader,
                ref declarationOrder,
                compileTimeIfPrefixes,
                isExcluded))
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
        => reader.TryConsume(TokenKind.StartBlock);

    /// <summary>Consumes declaration trivia and detects the end of the current Declaration Container body.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns><see langword="true"/> when another declaration is available.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryBeginDeclaration(ref TokenReader reader)
    {
        Parser.ConsumeAttributeAndModifier(ref reader, out var isEnd, allowCompileTimeDirectives: true);
        if (isEnd)
        {
            return false;
        }

        return !reader.TryConsume(TokenKind.EndBlock);
    }

    /// <summary>Attempts to parse a nested Declaration Container declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The declaration keyword token.</param>
    /// <param name="compileTimeIfPrefixes">Deferred directives controlling the declaration.</param>
    /// <param name="isExcluded">Whether an early condition excludes the declaration.</param>
    /// <returns><see langword="true"/> when a Declaration Container keyword was consumed.</returns>
    protected bool TryParseDeclarationContainer(
        ref TokenReader reader,
        Token token,
        List<CompileTimeIfPrefix>? compileTimeIfPrefixes = null,
        bool isExcluded = false)
    {
        var tokenKind = token.Kind;
        if (tokenKind is not (TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract))
        {
            return false;
        }

        reader.Advance();
        var supportsGenericHeader = tokenKind == TokenKind.Struct;
        var declaration = Parser.ParseDeclarationContainerHeader(
            ref reader,
            supportsGenericHeader,
            supportsGenericHeader);
        if (isExcluded || reader.IsExcluded)
        {
            reader.SkipCurrentBlock(false);
            return true;
        }

        var state = reader.TakeContext();
        if (compileTimeIfPrefixes is not null)
        {
            var standalone = CreateStandalone(reader.CodeContext, tokenKind, state, token.Span, declaration.Name);
            standalone.AddHeader(declaration.GenericArguments, declaration.Origins);
            if (reader.CurrentTokenKind == TokenKind.StartBlock)
            {
                standalone.Parse(ref reader);
            }

            this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, standalone));
            return true;
        }

        var container = this.GetOrAddDeclarationContainer(declaration.Name, tokenKind, state, token.Span);
        container.AddHeader(declaration.GenericArguments, declaration.Origins);

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            container.Parse(ref reader);
        }

        return true;
    }

    /// <summary>Attempts to parse one Property or function declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="declarationOrder">The current declaration-order state.</param>
    /// <param name="compileTimeIfPrefixes">Deferred directives controlling the declaration.</param>
    /// <param name="isExcluded">Whether an early condition excludes the declaration.</param>
    /// <returns><see langword="true"/> when a supported member was consumed.</returns>
    protected bool TryParsePropertyOrFunction(
        ref TokenReader reader,
        ref DeclarationOrder declarationOrder,
        List<CompileTimeIfPrefix>? compileTimeIfPrefixes,
        bool isExcluded)
    {
        var token = reader.CurrentToken;
        if (token.Kind is TokenKind.Let or TokenKind.Var)
        {
            CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Property);
            reader.Advance();
            var propertyKoto = Parser.ParseProperty(ref reader, ref token);
            if (propertyKoto is not null && !isExcluded)
            {
                this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, propertyKoto));
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

        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            functionKoto.Parse(ref reader);
        }

        if (!isExcluded && !functionKoto.IsExcluded)
        {
            this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, functionKoto));
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
        reader.SkipSeparators();

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
        if (this.genericArguments is not null)
        {
            foreach (var argument in this.genericArguments)
            {
                yield return argument;
            }
        }

        if (this.typeConstraints is not null)
        {
            foreach (var constraint in this.typeConstraints)
            {
                yield return constraint;
            }
        }

        if (this.kotoList is not null)
        {
            foreach (var koto in this.kotoList)
            {
                yield return koto;
            }
        }

        if (this.NestedContainerTable is not null)
        {
            foreach (var container in this.NestedContainerTable.ToArray())
            {
                yield return container;
            }
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (ReplaceInList(this.kotoList, oldKoto, newKoto))
        {
            return true;
        }

        if (oldKoto is TypeKoto && ReplaceInList(this.genericArguments, oldKoto, newKoto))
        {
            return true;
        }

        if (oldKoto is IsKoto && ReplaceInList(this.typeConstraints, oldKoto, newKoto))
        {
            return true;
        }

        if (this.NestedContainerTable is { } nested &&
            oldKoto is DeclarationContainerKoto oldContainer && newKoto is DeclarationContainerKoto newContainer &&
            nested.TryGetValue(oldContainer.Name, out var registered) &&
            ReferenceEquals(registered, oldContainer))
        {
            if (!oldContainer.Name.Equals(newContainer.Name, StringComparison.Ordinal) &&
                nested.TryGetValue(newContainer.Name, out _))
            {
                return false;
            }

            if (nested.TryRemove(oldContainer.Name) &&
                nested.TryAdd(newContainer.Name, newContainer))
            {
                return true;
            }

            nested.TryAdd(oldContainer.Name, oldContainer);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void CheckDeclarationOrder(ref TokenReader reader, ref DeclarationOrder current, DeclarationOrder next)
    {
        if (next < current)
        {
            reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.DeclarationOrderWarning_Kd);
        }

        current = next;
    }

    /// <summary>Gets or creates a directly nested container.</summary>
    /// <param name="text">The container name.</param>
    /// <param name="name">The name as a string when already materialized, to avoid a second allocation.</param>
    private DeclarationContainerKoto GetOrAddChild(ReadOnlySpan<char> text, string? name, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var nested = this.NestedContainerTable ??= new();
        if (nested.TryGetValue(text, out var existing))
        {
            return (DeclarationContainerKoto)existing;
        }

        name ??= this.CodeContext.Compilation.Intern(text);
        var container = CreateStandalone(this.CodeContext, kind, state, range, name);
        container.Parent = this;
        nested.Add(name, container);
        return container;
    }
}
