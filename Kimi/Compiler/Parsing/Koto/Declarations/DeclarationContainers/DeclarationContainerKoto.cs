// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;
#pragma warning disable SA1202 // Related storage is grouped together.
#pragma warning disable SA1204 // Parsing helpers are grouped by responsibility.

/// <summary>
/// Provides shared storage and parsing for Kimigayo Declaration Container nodes.
/// </summary>
/// <remarks>
/// Member collections are allocated on first use because most containers only hold a few
/// of the possible member kinds.
/// </remarks>
public abstract class DeclarationContainerKoto : DeclarationKoto
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
    public ModifierKind Modifier { get; private set; }

    /// <summary>Gets or sets the Declaration Container name.</summary>
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

    private List<Koto>? kotoList;

    private List<DeclarationContainerKoto>? nestedContainers;

    /// <summary>Gets nested declarations in insertion order without allocating a snapshot.</summary>
    public IReadOnlyList<DeclarationContainerKoto> NestedContainers => this.nestedContainers ?? (IReadOnlyList<DeclarationContainerKoto>)Array.Empty<DeclarationContainerKoto>();

    /// <summary>Gets generic parameters without materializing empty storage.</summary>
    public IReadOnlyList<TypeKoto> GenericParameterNodes => this.genericArguments ?? (IReadOnlyList<TypeKoto>)Array.Empty<TypeKoto>();

    /// <summary>Gets constraints without materializing empty storage.</summary>
    public IReadOnlyList<IsKoto> ConstraintNodes => this.typeConstraints ?? (IReadOnlyList<IsKoto>)Array.Empty<IsKoto>();

    /// <summary>Gets or sets the nested Declaration Containers keyed by name, or <see langword="null"/> when none exist.</summary>
    protected Utf16Hashtable<Koto>? NestedContainerTable { get; set; }

    private List<TypeKoto>? genericArguments;

    private List<IsKoto>? typeConstraints;

    private Koto[]? bases;

    /// <summary>Gets the written base type or parent contracts.</summary>
    public IReadOnlyList<Koto> Bases => this.bases ?? [];

    internal void SetBases(Koto[]? bases)
    {
        this.bases = bases;
        this.Adopt(bases);
    }

    /// <summary>Gets or sets the declared origins, or <see langword="null"/> when none exist.</summary>
    protected List<string>? OriginList { get; set; }

    /// <summary>Gets the generic parameters.</summary>
    public List<TypeKoto> GenericArguments => this.genericArguments ??= [];

    /// <summary>Gets the type constraints.</summary>
    public List<IsKoto> TypeConstraints => this.typeConstraints ??= new(4);

    /// <summary>Gets the declared origins.</summary>
    public List<string> Origins => this.OriginList ??= [];

    /// <summary>Gets declared Origin names without allocating empty storage.</summary>
    public IReadOnlyList<string> OriginNames => this.OriginList ?? (IReadOnlyList<string>)Array.Empty<string>();

    internal bool HasIncompatibleBindingHeader { get; private set; }

    private bool hasBindingHeader;

    /// <summary>Gets Properties and functions in declaration order.</summary>
    public IReadOnlyList<Koto> Members => (IReadOnlyList<Koto>?)this.kotoList ?? [];

    /// <summary>Gets the mutable member list, creating it on first use.</summary>
    protected List<Koto> KotoList => this.kotoList ??= new(4);

    /// <summary>Gets nested Declaration Containers.</summary>
    public IEnumerable<DeclarationContainerKoto> NestedDeclarationContainers
        => this.NestedContainers;

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
        if (this.hasBindingHeader)
        {
            var count = genericArguments?.Count ?? 0;
            var originCount = origins?.Count ?? 0;
            var same = count == this.GenericParameterNodes.Count && originCount == this.OriginNames.Count;
            for (var i = 0; same && i < count; i++)
            {
                same = genericArguments![i].Akind == this.GenericParameterNodes[i].Akind && genericArguments[i].Identifier == this.GenericParameterNodes[i].Identifier && genericArguments[i].SemanticsParameter == this.GenericParameterNodes[i].SemanticsParameter;
            }

            for (var i = 0; same && i < originCount; i++)
            {
                same = origins![i] == this.OriginNames[i];
            }

            this.HasIncompatibleBindingHeader |= !same;
            return;
        }

        this.hasBindingHeader = true;
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

        if (this.bases is { Length: > 0 } bases)
        {
            builder.Append(" : ");
            for (var i = 0; i < bases.Length; i++)
            {
                if (i > 0)
                {
                    builder.AppendCommaAndSpace();
                }

                bases[i].WriteTo(ref builder);
            }
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
        this.hasBindingHeader = false;
        this.HasIncompatibleBindingHeader = false;
        this.kotoList?.Clear();
        this.NestedContainerTable?.Clear();
        this.nestedContainers?.Clear();
        this.genericArguments?.Clear();
        this.typeConstraints?.Clear();
        this.OriginList?.Clear();
        this.bases = null;
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

        if (!this.IsRoot || this.Modifier != 0)
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

                WriteMemberTo(x, ref builder);
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
                WriteMemberTo(koto, ref builder);
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

    private static void WriteMemberTo(Koto member, ref IndentedStringBuilder builder)
    {
        if (member is CodeBlockKoto block)
        {
            builder.Append("#if true");
            block.WriteIndentedTo(ref builder);
        }
        else
        {
            member.WriteTo(ref builder);
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
            if (isExcluded)
            {
                Parser.SkipExcludedSyntax(ref reader);
                continue;
            }

            if (Parser.IsCompileTimeMatchStart(ref reader))
            {
                var caseGroup = Parser.ParseCompileTimeMatch(ref reader, this);
                this.AddLast(caseGroup);
                continue;
            }

            if (reader.HasCompileTimeIfPrefix && reader.CurrentTokenKind == TokenKind.StartBlock)
            {
                var body = Parser.ParseDeclarationDirectiveBody(ref reader, this);
                this.AddLast(body);
                continue;
            }

            if (parseTypeConstraints && Parser.IsTypeConstraintStart(ref reader))
            {
                if (!acceptsTypeConstraints && !reader.IsCurrentIdentifier("Self"))
                {
                    reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.DuplicateTypeConstraintDefinition_Kd);
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                    continue;
                }

                var constraint = Parser.ParseTypeConstraint(ref reader, finishLine: false);
                if (constraint is not null && reader.IsCurrentIdentifier("when"))
                {
                    reader.Advance();
                    var conditions = new List<Koto>();
                    do
                    {
                        var condition = Parser.ParseTypeConstraint(ref reader, finishLine: false);
                        if (condition is null)
                        {
                            break;
                        }

                        conditions.Add(condition);
                    }
                    while (reader.TryConsume(TokenKind.Comma));
                    var span = SourceSpan.FromBounds(constraint.Span.Start, conditions.Count == 0 ? constraint.Span.End : conditions[^1].Span.End);
                    var premises = new SyntaxFormKoto(ref reader, conditions.Count == 0 ? span : SourceSpan.FromBounds(conditions[0].Span.Start, span.End), KotoKind.ConditionalConformance, string.Empty, conditions.ToArray());
                    this.AddLast(new SyntaxFormKoto(ref reader, span, KotoKind.ConditionalConformance, string.Empty, [constraint, premises], separator: " when "));
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
                    continue;
                }

                if (!acceptsTypeConstraints)
                {
                    reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.DuplicateTypeConstraintDefinition_Kd);
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                    continue;
                }

                CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.TypeConstraint);
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
                if (constraint is not null && !isExcluded)
                {
                    this.AddTypeConstraint(constraint);
                }

                continue;
            }

            var token = reader.CurrentToken;
            if (parseDeclarationContainers &&
                this.TryParseDeclarationContainer(ref reader, token, isExcluded))
            {
                continue;
            }

            if (!this.TryParsePropertyOrFunction(
                ref reader,
                ref declarationOrder,
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

        if (reader.HasCompileTimeIfPrefix && reader.CurrentTokenKind == TokenKind.EndBlock)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }

        return !reader.TryConsume(TokenKind.EndBlock);
    }

    /// <summary>Attempts to parse a nested Declaration Container declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The declaration keyword token.</param>
    /// <param name="isExcluded">Whether an early condition excludes the declaration.</param>
    /// <returns><see langword="true"/> when a Declaration Container keyword was consumed.</returns>
    protected bool TryParseDeclarationContainer(
        ref TokenReader reader,
        Token token,
        bool isExcluded = false)
    {
        var tokenKind = token.Kind;
        if (tokenKind is not (TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract))
        {
            return false;
        }

        if (tokenKind == TokenKind.Extension)
        {
            reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, "extension");
        }

        reader.Advance();
        var supportsGenericHeader = tokenKind is TokenKind.Struct or TokenKind.Enum;
        var declaration = Parser.ParseDeclarationContainerHeader(
            ref reader,
            supportsGenericHeader,
            supportsGenericHeader,
            tokenKind);
        if (isExcluded || reader.IsExcluded)
        {
            reader.SkipCurrentBlock(false);
            return true;
        }

        var state = reader.TakeContext();
        var container = this.GetOrAddDeclarationContainer(declaration.Name, tokenKind, state, token.Span);
        container.AddHeader(declaration.GenericArguments, declaration.Origins);
        container.SetBases(declaration.Bases);

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            container.Parse(ref reader);
        }
        else if (container is EnumKoto)
        {
            container.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }

        return true;
    }

    /// <summary>Attempts to parse one Property or function declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="declarationOrder">The current declaration-order state.</param>
    /// <param name="isExcluded">Whether an early condition excludes the declaration.</param>
    /// <returns><see langword="true"/> when a supported member was consumed.</returns>
    protected bool TryParsePropertyOrFunction(
        ref TokenReader reader,
        ref DeclarationOrder declarationOrder,
        bool isExcluded)
    {
        var token = reader.CurrentToken;
        if (reader.IsCurrentIdentifier("specialize") && reader.PeekKind(1) == TokenKind.Func)
        {
            var specialized = Parser.ParseSpecialization(ref reader);
            if (specialized is not null)
            {
                this.AddLast(specialized);
            }

            return true;
        }

        if (this is StructKoto && token.Kind == TokenKind.Init)
        {
            var constructor = Parser.ParseFuncDeclaration(ref reader, constructor: true);
            if (constructor is not null)
            {
                constructor.Parse(ref reader);
                this.AddLast(constructor);
            }

            return true;
        }

        if (token.Kind == TokenKind.Associate)
        {
            reader.Advance();
            if (Parser.IsTypeConstraintStart(ref reader))
            {
                var associated = Parser.ParseTypeConstraint(ref reader);
                if (associated is not null)
                {
                    associated.IsAssociatedConstraint = true;
                    if (this is ContractKoto)
                    {
                        this.AddTypeConstraint(associated);
                    }
                    else
                    {
                        this.AddLast(associated);
                    }
                }
            }
            else
            {
                var name = reader.Read();
                if (this is not ContractKoto || !name.Kind.IsIdentifierOrContextualKeyword())
                {
                    reader.Diagnostic.Add(name.Span, DiagnosticCode.UnexpectedToken_Kd, "associate");
                }

                if (IdentifierNameKoto.TryCreate(ref reader, name, out var identifier))
                {
                    this.AddLast(new SyntaxFormKoto(ref reader, name.Span, KotoKind.AssociatedType, "associate ", [identifier]));
                }
            }

            return true;
        }

        if (this is EnumKoto && token.Kind.IsIdentifierOrContextualKeyword() &&
            !(token.Kind is TokenKind.Computed or TokenKind.Property && reader.PeekKind(1).IsIdentifierOrContextualKeyword()))
        {
            this.AddLast(Parser.ParseEnumCase(ref reader));
            reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
            return true;
        }

        if (token.Kind is TokenKind.Let or TokenKind.Var or TokenKind.Computed or TokenKind.Property)
        {
            if (this is not ContractKoto)
            {
                CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Property);
            }

            reader.Advance();
            var propertyKoto = Parser.ParseProperty(ref reader, ref token);
            if (propertyKoto is not null && !isExcluded)
            {
                if ((this is ContractKoto) != propertyKoto.IsContractRequirement || this is EnumKoto)
                {
                    propertyKoto.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "property declaration container");
                    return true;
                }

                if (propertyKoto.IsContractRequirement &&
                    (propertyKoto.Modifier != ModifierKind.NoModifier || propertyKoto.AttributeChain is not null))
                {
                    propertyKoto.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "property requirement modifiers or attributes");
                }

                foreach (var accessor in propertyKoto.Accessors)
                {
                    if (accessor.HasExplicitSignature &&
                        (accessor.ReceiverType is not null) != (this is StructKoto or ContractKoto))
                    {
                        accessor.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "instance accessors require self; static accessors omit self");
                    }
                }

                this.AddLast(propertyKoto);
            }

            return true;
        }

        if (this is StructKoto && token.Kind == TokenKind.Deinit)
        {
            var context = reader.TakeContext();
            reader.Advance();
            var destructor = new FunctionKoto(ref reader, context, token.Span, "deinit", null, null, new TupleTypeKoto(ref reader, token.Span, []))
            {
                IsDestructor = true,
            };
            destructor.Parse(ref reader);
            this.AddLast(destructor);
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

        if (this is ContractKoto)
        {
            Parser.ParseRequirementBody(ref reader, functionKoto);
        }
        else
        {
            Parser.ParseNamedFunctionBody(ref reader, functionKoto);
        }

        if (!isExcluded && !functionKoto.IsExcluded)
        {
            this.AddLast(functionKoto);
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

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        if (this.bases is not null)
        {
            for (var typeIndex = 0; typeIndex < KotoVisitor.Count(this.bases); typeIndex++)
            {
                var type = this.bases[typeIndex];
                visitor.Visit(type);
            }
        }

        if (this.genericArguments is not null)
        {
            for (var argumentIndex = 0; argumentIndex < KotoVisitor.Count(this.genericArguments); argumentIndex++)
            {
                var argument = this.genericArguments[argumentIndex];
                visitor.Visit(argument);
            }
        }

        if (this.typeConstraints is not null)
        {
            for (var constraintIndex = 0; constraintIndex < KotoVisitor.Count(this.typeConstraints); constraintIndex++)
            {
                var constraint = this.typeConstraints[constraintIndex];
                visitor.Visit(constraint);
            }
        }

        if (this.kotoList is not null)
        {
            for (var kotoIndex = 0; kotoIndex < KotoVisitor.Count(this.kotoList); kotoIndex++)
            {
                var koto = this.kotoList[kotoIndex];
                visitor.Visit(koto);
            }
        }

        if (this.NestedContainerTable is not null)
        {
            for (var containerIndex = 0; containerIndex < KotoVisitor.Count(this.NestedContainers); containerIndex++)
            {
                var container = this.NestedContainers[containerIndex];
                visitor.Visit(container);
            }
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        if (this.bases is not null)
        {
            foreach (var type in this.bases)
            {
                yield return type;
            }
        }

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

        if (this.nestedContainers is not null)
        {
            foreach (var container in this.nestedContainers)
            {
                yield return container;
            }
        }
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (ReplaceInList(this.bases, oldKoto, newKoto))
        {
            return true;
        }

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
                ReplaceInList(this.nestedContainers, oldContainer, newContainer);
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
        (this.nestedContainers ??= []).Add(container);
        return container;
    }
}
