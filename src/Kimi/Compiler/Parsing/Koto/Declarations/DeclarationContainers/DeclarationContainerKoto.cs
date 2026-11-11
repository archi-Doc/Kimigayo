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

    /// <summary>The member state of one Declaration Container body, shared by its directive targets and arms (SPEC 19.4).</summary>
    /// <param name="parseTypeConstraints">Whether ordinary type constraints are accepted.</param>
    /// <param name="parseDeclarationContainers">Whether nested Declaration Containers are accepted.</param>
    /// <param name="acceptsTypeConstraints">Whether this fragment may still define the container's Constraints.</param>
    private struct MemberParseState(bool parseTypeConstraints, bool parseDeclarationContainers, bool acceptsTypeConstraints)
    {
        public readonly bool ParseTypeConstraints = parseTypeConstraints;

        public readonly bool ParseDeclarationContainers = parseDeclarationContainers;

        public readonly bool AcceptsTypeConstraints = acceptsTypeConstraints;

        public DeclarationOrder DeclarationOrder;
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
        if (bases is not { Length: > 0 })
        {
            return;
        }

        if (this.bases is { Length: > 0 })
        {
            this.HasIncompatibleBindingHeader = true;
            return;
        }

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
    public List<string> Origins => this.OriginList ??= new OriginNameList();

    /// <summary>Gets declared Origin names without allocating empty storage.</summary>
    public IReadOnlyList<string> OriginNames => this.OriginList ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>Gets a value indicating whether this Type writes an Origin header, which alone declares its own slots (SPEC 15.3.2).</summary>
    public bool HasOriginHeader => this.OriginList is { Count: > 0 };

    internal bool HasIncompatibleBindingHeader { get; private set; }

    private DeclarationContainerKoto? nextArity;

    private bool hasBindingHeader;

    private int fragmentOrdinal;

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
        if (koto is PropertyKoto property)
        {
            property.FragmentOrdinal = this.fragmentOrdinal;
        }

        this.KotoList.Add(koto);
        koto.Parent = this;
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

    /// <summary>Applies a parsed declaration header when the corresponding member kind is still empty.</summary>
    /// <param name="kind">The written declaration kind.</param>
    /// <param name="modifier">The written modifiers.</param>
    /// <param name="genericArguments">The generic parameters, if declared.</param>
    /// <param name="origins">The origin names, if declared.</param>
    /// <param name="attributes">The selected fragment's Attribute chain.</param>
    internal void AddHeader(TokenKind kind, ModifierKind modifier, List<TypeKoto>? genericArguments, List<string>? origins, AttributeKoto? attributes)
    {
        this.fragmentOrdinal++;
        if (attributes is not null)
        {
            var last = attributes;
            for (var attribute = attributes; attribute is not null; attribute = attribute.AttributeChain)
            {
                attribute.FragmentOrdinal = this.fragmentOrdinal;
                last = attribute;
            }

            var previous = this.AttributeChain;
            this.SetAttributeChain(attributes);
            if (previous is not null && !ReferenceEquals(previous, attributes))
            {
                last.AttributeChain = previous;
                previous.Parent = last;
            }
        }

        this.HasIncompatibleBindingHeader |= this.TokenKind != kind;
        if (this.hasBindingHeader)
        {
            // Enums and contracts are closed declarations, even when repeated headers agree.
            this.HasIncompatibleBindingHeader |= this is EnumKoto or ContractKoto;
            var previous = this.Modifier.ExtractAccessibilityModifiers() == ModifierKind.NoModifier ? this.Modifier | ModifierKind.Private : this.Modifier;
            var current = modifier.ExtractAccessibilityModifiers() == ModifierKind.NoModifier ? modifier | ModifierKind.Private : modifier;
            this.HasIncompatibleBindingHeader |= previous != current;
            // SPEC 6.1.2, 15.3.2: fragments agree on their own slots; an omitted header declares none.
            var count = genericArguments?.Count ?? 0;
            var same = count == this.GenericParameterNodes.Count &&
                OriginNameList.SameParameters((IReadOnlyList<string>?)origins ?? [], this.OriginNames);
            for (var i = 0; same && i < count; i++)
            {
                same = genericArguments![i].Akind == this.GenericParameterNodes[i].Akind && genericArguments[i].Identifier == this.GenericParameterNodes[i].Identifier && genericArguments[i].SemanticsParameter == this.GenericParameterNodes[i].SemanticsParameter;
            }

            this.HasIncompatibleBindingHeader |= !same;
            return;
        }

        this.hasBindingHeader = true;
        // Synthesized path groups have no header and acquire their first explicit modifiers.
        this.Modifier = modifier;
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
            else if (this.OriginList is OriginNameList exposed)
            {// Keep a previously exposed list instance, including parsed spans and bounds.
                exposed.AppendFrom(origins);
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
        Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed, mergeFragmentLayouts: true);

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

        if (this.HasOriginHeader)
        {
            OriginNameList.WriteTo(this.OriginNames, ref builder);
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
    }

    /// <summary>Writes a root-group declaration for this group.</summary>
    /// <param name="builder">The destination builder.</param>
    public void UnparseToRoot(ref IndentedStringBuilder builder)
    {
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed, mergeFragmentLayouts: true);
            builder.AppendLine();
        }

        this.Modifier.WriteTo(ref builder, KotoWriteOptions.AppendSpace);
        builder.Append(TokenKind.RootGroup.ToText());
        builder.Append(' ');
        Parser.WriteQualifiedNameTo(this, ref builder);
    }

    /// <summary>Removes all declarations and Declaration Container metadata.</summary>
    public void Clear()
    {
        this.hasBindingHeader = false;
        ((IOriginClauseOwner)this).OriginClauses?.Clear();
        this.fragmentOrdinal = 0;
        this.AttributeChain = null;
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

        this.CodeContext.Compilation.NoteSyntaxEdit(this);
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

        OriginClauses.Write(this, ref builder, false);
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

        if (this.nestedContainers is { Count: > 0 } nestedContainers)
        {
            builder.EnsureTrailingBlankLine();
            foreach (var x in nestedContainers)
            {
                x.UnparseAll(ref builder);
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
    /// <param name="genericArity">The number of generic header slots.</param>
    /// <param name="codeContext">The parsing fragment's context that a newly created container retains; <see langword="null"/> uses this container's context.</param>
    /// <returns>The final Declaration Container.</returns>
    public DeclarationContainerKoto GetOrAddDeclarationContainer(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range, int genericArity = 0, CodeContext? codeContext = null)
    {
        var container = this;
        while (true)
        {
            var index = qualifiedName.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                return container.GetOrAddChild(qualifiedName, null, kind, state, range, genericArity, codeContext);
            }

            container = container.GetOrAddChild(qualifiedName[..index], null, TokenKind.Group, default, default, codeContext: codeContext);
            qualifiedName = qualifiedName[(index + 1)..];
        }
    }

    /// <summary>Gets or creates a directly nested Declaration Container from a simple name.</summary>
    /// <param name="name">The Declaration Container name without dots.</param>
    /// <param name="kind">The declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <param name="genericArity">The number of generic header slots.</param>
    /// <param name="codeContext">The parsing fragment's context that a newly created container retains; <see langword="null"/> uses this container's context.</param>
    /// <returns>The nested Declaration Container.</returns>
    internal DeclarationContainerKoto GetOrAddDeclarationContainer(string name, TokenKind kind, TokenContext state, SourceSpan range, int genericArity = 0, CodeContext? codeContext = null)
        => name.Contains(Constants.DotChar)
            ? this.GetOrAddDeclarationContainer(name.AsSpan(), kind, state, range, genericArity, codeContext)
            : this.GetOrAddChild(name, name, kind, state, range, genericArity, codeContext);

    /// <summary>Gets or creates a Declaration Container from a qualified name.</summary>
    /// <remarks>Retained as a source-compatible alias for <c>GetOrAddDeclarationContainer</c>.</remarks>
    /// <param name="qualifiedName">The dot-separated Declaration Container name.</param>
    /// <param name="kind">The final Declaration Container's declaration kind.</param>
    /// <param name="state">The declaration context.</param>
    /// <param name="range">The declaration source span.</param>
    /// <returns>The final Declaration Container.</returns>
    public DeclarationContainerKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range)
        => this.GetOrAddDeclarationContainer(qualifiedName, kind, state, range, genericArity: -1);

    /// <summary>Parses the body supported by this Declaration Container kind.</summary>
    /// <param name="reader">The token reader.</param>
    public abstract void Parse(ref TokenReader reader);

    /// <summary>Creates a container of this kind outside the tree that parses members for this container and registers nothing in it.</summary>
    /// <param name="codeContext">The code context.</param>
    /// <param name="range">The container's source span.</param>
    /// <returns>The detached container.</returns>
    /// <remarks>It shares this container's generic parameters, so its members are parsed under this header, such as a member's
    /// Constraint on the declaring Type's parameters (SPEC 7.5): excluded members (SPEC 19.4) and a conditional conformance's block (SPEC 8.8.1).</remarks>
    internal DeclarationContainerKoto CreateDetached(CodeContext codeContext, SourceSpan range)
    {
        var detached = CreateStandalone(codeContext, this.TokenKind, default, range, string.Empty);
        detached.genericArguments = this.genericArguments;
        return detached;
    }

    internal void WriteAsBlockItem(ref IndentedStringBuilder builder)
    {
        this.WriteTo(ref builder);
        var containers = this.NestedContainers;
        if (this.typeConstraints is not { Count: > 0 } && this.kotoList is not { Count: > 0 } && containers.Count == 0 && OriginClauses.Get(this).Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.IncrementIndent();
        var hasPrevious = false;
        foreach (var relation in OriginClauses.Get(this))
        {
            WriteSeparator(ref builder, ref hasPrevious);
            relation.WriteTo(ref builder);
        }

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
            nested.WriteAsBlockItem(ref builder);
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
            builder.Append(Parser.IfTrueDirectiveText);
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
        var list = new MemberItems(
            this,
            new MemberParseState(
                parseTypeConstraints,
                parseDeclarationContainers,
                parseTypeConstraints && this.typeConstraints is not { Count: > 0 } && OriginClauses.Get(this).Count == 0));
        Parser.ParseItems(ref reader, ref list);
    }

    /// <summary>Consumes the opening block token when the caller left it for the Declaration Container parser.</summary>
    /// <param name="reader">The token reader.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void ConsumeBlockStart(ref TokenReader reader)
        => reader.TryConsume(TokenKind.StartBlock);

    /// <summary>Attempts to parse a nested Declaration Container declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The declaration keyword token.</param>
    /// <returns><see langword="true"/> when a Declaration Container keyword was consumed.</returns>
    /// <remarks>Callers skip syntax excluded by an early directive before reaching a declaration.</remarks>
    protected bool TryParseDeclarationContainer(ref TokenReader reader, Token token)
    {
        var tokenKind = token.Kind;
        if (!Parser.IsContainerDeclarationStart(ref reader))
        {
            return false;
        }

        if (tokenKind == TokenKind.Extension)
        {
            // `extension` is reserved (SPEC 2.5): the declaration is skipped with its body.
            Parser.OmitDeclaration(ref reader, reader.Unexpected(SyntaxForm.ExtensionDeclaration, token.Span));
            return true;
        }

        reader.Advance();
        var state = reader.TakeContext();
        var nameSpan = reader.CurrentTokenRange;
        var declaration = Parser.ParseDeclarationContainerHeader(ref reader, tokenKind);
        if (declaration.Name is null)
        {
            // Without its Name the declaration merges with no other; it is skipped with its body (DIAGNOSTICS.md §4.4).
            Parser.OmitDeclaration(ref reader, reader.Diagnostic.LastError);
            return true;
        }

        var container = this.GetOrAddDeclarationContainer(declaration.Name, tokenKind, state, token.Span, declaration.GenericArguments?.Count ?? 0, reader.CodeContext);
        reader.Document(container, reader.PreviousSyntaxEnd, state.AttributeKoto);
        reader.Hover(container, nameSpan);
        container.AddHeader(tokenKind, state.ModifierKind, declaration.GenericArguments, declaration.Origins, state.AttributeKoto);
        container.SetBases(declaration.Bases);

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            container.Parse(ref reader);
        }
        else if (container is EnumKoto)
        {
            reader.CodeContext.RecordRecovery(container, reader.Expect(SyntaxForm.Body));
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
                constructor.DeclaringContainer = this;
                constructor.Parse(ref reader);
                this.AddLast(constructor);
            }

            return true;
        }

        if (token.Kind == TokenKind.Associate)
        {
            Parser.ParseAssociatedTypeItem(ref reader, this, token);
            return true;
        }

        if (this is EnumKoto && token.Kind.IsIdentifierOrContextualKeyword() &&
            !(token.Kind is TokenKind.Computed or TokenKind.Property && reader.PeekKind(1).IsIdentifierOrContextualKeyword()))
        {
            this.AddLast(Parser.ParseEnumCase(ref reader));
            reader.ExpectLineEnd();
            return true;
        }

        if (token.Kind is TokenKind.Let or TokenKind.Var or TokenKind.Computed or TokenKind.Property)
        {
            if (token.Kind == TokenKind.Property)
            {
                // A Property requirement takes no access word (SPEC F.6.2 PropertyRequirement), wherever it is written.
                Parser.RejectAccessWord(ref reader);
            }

            if (this is not ContractKoto)
            {
                CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Property, reader.CurrentTokenRange);
            }

            reader.Advance();
            var propertyKoto = Parser.ParseProperty(ref reader, ref token);
            if (propertyKoto is not null)
            {
                if ((this is ContractKoto) != propertyKoto.IsContractRequirement || this is EnumKoto)
                {
                    propertyKoto.Unexpected(SyntaxForm.PropertyDeclaration);
                    return true;
                }

                foreach (var accessor in propertyKoto.Accessors)
                {
                    if (accessor.ReceiverType is not null && this is not (StructKoto or ContractKoto))
                    {
                        accessor.Unexpected(SyntaxForm.ReceiverParameter);
                    }
                }

                this.AddLast(propertyKoto);
            }

            return true;
        }

        if (this is StructKoto && token.Kind == TokenKind.Drop)
        {
            // drop accepts a common Body, without modifiers or attributes (SPEC 16.3): the prefix's attributes are reported and attach
            // to nothing, and its access word is rejected.
            Parser.ReportPendingAttributes(ref reader);
            Parser.RejectAccessWord(ref reader);
            var context = reader.TakeContext();
            reader.Advance();
            var destructor = new FunctionKoto(ref reader, context, token.Span, "drop", null, null, new TupleTypeKoto(ref reader, token.Span, []))
            {
                IsDestructor = true,
            };
            reader.Document(destructor, token.Span.End);
            reader.Hover(destructor, token.Span);
            destructor.Parse(ref reader);
            this.AddLast(destructor);
            return true;
        }

        if (token.Kind != TokenKind.Func)
        {
            return false;
        }

        if (this is ContractKoto)
        {
            // A Contract's function item is a requirement, which takes no access word (SPEC F.6.2 FunctionRequirement).
            Parser.RejectAccessWord(ref reader);
        }

        CheckDeclarationOrder(ref reader, ref declarationOrder, DeclarationOrder.Function, reader.CurrentTokenRange);
        reader.Advance();
        var functionKoto = Parser.ParseFuncDeclaration(ref reader);
        if (functionKoto is null)
        {
            return true;
        }

        functionKoto.DeclaringContainer = this;
        if (this is ContractKoto)
        {
            Parser.ParseRequirementBody(ref reader, functionKoto);
        }
        else
        {
            Parser.ParseNamedFunctionBody(ref reader, functionKoto);
        }

        if (this is StructKoto or EnumKoto or ContractKoto)
        {
            Parser.ValidateReceiverParameters(functionKoto);
        }

        this.AddLast(functionKoto);

        return true;
    }

    /// <summary>Reports and skips a declaration unsupported by the current Declaration Container kind.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The unsupported declaration's first token.</param>
    protected static void SkipUnexpectedDeclaration(ref TokenReader reader, Token token)
        => Parser.OmitDeclaration(ref reader, reader.Expect(SyntaxForm.Declaration, token));

    /// <summary>Writes one type constraint in the syntax used by this Declaration Container kind.</summary>
    /// <param name="constraint">The constraint to write.</param>
    /// <param name="builder">The destination builder.</param>
    protected virtual void WriteTypeConstraintTo(IsKoto constraint, ref IndentedStringBuilder builder)
        => constraint.WriteTo(ref builder);

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        slots.List(this.bases);
        slots.List(this.genericArguments);
        slots.List(this.typeConstraints);
        slots.List(this.kotoList);

        // A nested container is replaced where its lookup by name exists, by one that declares no second container of its name and
        // arity; the lookup is then rebuilt.
        if (slots.Replacement is DeclarationContainerKoto replacement && (this.NestedContainerTable is null || this.DeclaresAgain(replacement, slots.Original)))
        {
            return;
        }

        var replaced = slots.Replaced;
        slots.List(this.nestedContainers);
        if (replaced || !slots.Replaced || this.NestedContainerTable is not { } nested)
        {
            return;
        }

        ((DeclarationContainerKoto)slots.Original!).nextArity = null;
        nested.Clear();
        foreach (var candidate in this.nestedContainers!)
        {
            nested.TryGetValue(candidate.Name, out var previous);
            candidate.nextArity = previous as DeclarationContainerKoto;
            nested.AddOrUpdate(candidate.Name, candidate);
        }
    }

    /// <summary>Warns when a member comes after a later kind of member, at the member's first token.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="current">The latest member kind so far.</param>
    /// <param name="next">The kind of this member.</param>
    /// <param name="at">The member's first token, read before the member is parsed.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void CheckDeclarationOrder(ref TokenReader reader, ref DeclarationOrder current, DeclarationOrder next, SourceSpan at)
    {
        if (next < current)
        {
            reader.Diagnostic.Add(at, DiagnosticCode.DeclarationOrderWarning_Kd);
        }

        current = next;
    }

    private static DeclarationContainerKoto CreateStandalone(CodeContext codeContext, TokenKind kind, TokenContext state, SourceSpan range, string name)
    {
        DeclarationContainerKoto container = kind switch
        {
            TokenKind.Struct => new StructKoto(codeContext, state, range),
            TokenKind.Enum => new EnumKoto(codeContext, state, range),
            TokenKind.Contract => new ContractKoto(codeContext, state, range),
            _ => new GroupKoto(codeContext, state, range),
        };

        container.Name = name;
        return container;
    }

    // Whether a nested container other than the replaced one declares the replacement's name with another kind or the same arity.
    private bool DeclaresAgain(DeclarationContainerKoto replacement, Koto? original)
    {
        for (var i = 0; i < this.NestedContainers.Count; i++)
        {
            var candidate = this.NestedContainers[i];
            if (!ReferenceEquals(candidate, original) && candidate.Name == replacement.Name &&
                (candidate.TokenKind != replacement.TokenKind || candidate.GenericParameterNodes.Count == replacement.GenericParameterNodes.Count))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets or creates a directly nested container.</summary>
    /// <param name="text">The container name.</param>
    /// <param name="name">The name as a string when already materialized, to avoid a second allocation.</param>
    private DeclarationContainerKoto GetOrAddChild(ReadOnlySpan<char> text, string? name, TokenKind kind, TokenContext state, SourceSpan range, int genericArity = 0, CodeContext? codeContext = null)
    {
        var nested = this.NestedContainerTable ??= new();
        if (nested.TryGetValue(text, out var existing))
        {
            var declaration = (DeclarationContainerKoto)existing;
            if (declaration.TokenKind != kind)
            {
                declaration.HasIncompatibleBindingHeader = true;
                return declaration;
            }

            for (var candidate = declaration; candidate is not null; candidate = candidate.nextArity)
            {
                if (genericArity < 0 || !candidate.hasBindingHeader || candidate.GenericParameterNodes.Count == genericArity)
                {
                    return candidate;
                }
            }
        }

        name ??= this.CodeContext.Compilation.Intern(text);
        // A merged container keeps the first declaring fragment's context so its own diagnostics stay in that document.
        var container = CreateStandalone(codeContext ?? this.CodeContext, kind, state, range, name);
        container.nextArity = existing as DeclarationContainerKoto;
        container.Parent = this;
        nested.AddOrUpdate(name, container);
        (this.nestedContainers ??= []).Add(container);
        return container;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseMemberItem(ref TokenReader reader, ref MemberParseState state)
    {
        // A Contract item takes no attributes (SPEC 6.7, F.6.2 ContractItem): the prefix's are reported and attach to nothing.
        if (this is ContractKoto)
        {
            Parser.ReportPendingAttributes(ref reader);
        }

        // SPEC 8.11.1: an effect specification is a Contract item; elsewhere it is kept for Binding to reject. Like the
        // Constraints and relations below, it takes no attributes (SPEC 6.7).
        if (Parser.IsEffectStart(ref reader, specification: true))
        {
            Parser.ReportPendingAttributes(ref reader);
            Parser.RejectAccessWord(ref reader);
            this.AddLast(Parser.ParseEffectBound(ref reader));
            return;
        }

        // Count and uniqueness rules are judged after selection, so excluded syntax skips them (SPEC 19.4).
        var acceptsTypeConstraints = state.AcceptsTypeConstraints || reader.InExcludedSyntax;
        if (Parser.IsOriginRelationStart(ref reader))
        {
            Parser.ReportPendingAttributes(ref reader);
            Parser.RejectAccessWord(ref reader);
            var keyword = reader.CurrentTokenRange;
            var relation = Parser.ParseOriginRelation(ref reader);
            if (!acceptsTypeConstraints)
            {
                relation.AddDiagnostic(DiagnosticCode.DuplicateTypeConstraintDefinition_Kd);
            }

            CheckDeclarationOrder(ref reader, ref state.DeclarationOrder, DeclarationOrder.TypeConstraint, keyword);
            OriginClauses.Add(this, relation);
            return;
        }

        if (state.ParseTypeConstraints && Parser.IsTypeConstraintStart(ref reader, declarationContext: true))
        {
            Parser.ReportPendingAttributes(ref reader);
            Parser.RejectAccessWord(ref reader);

            // The checks of the Constraint item are located at its subject, read before the Constraint is parsed.
            var subject = reader.CurrentTokenRange;
            if (!acceptsTypeConstraints && reader.CurrentTokenKind != TokenKind.Self)
            {
                reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.DuplicateTypeConstraintDefinition_Kd);
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                return;
            }

            var constraint = Parser.ParseTypeConstraint(ref reader, finishLine: false);
            if (reader.IsCurrentIdentifier("when"))
            {
                reader.Advance();
                var conditions = new List<Koto>();
                do
                {
                    conditions.Add(Parser.ParseTypeConstraint(ref reader, finishLine: false));
                }
                while (reader.TryConsume(TokenKind.Comma));
                var span = SourceSpan.FromBounds(constraint.Span.Start, conditions.Count == 0 ? constraint.Span.End : conditions[^1].Span.End);
                var premises = new SyntaxFormKoto(ref reader, conditions.Count == 0 ? span : SourceSpan.FromBounds(conditions[0].Span.Start, span.End), KotoKind.ConditionalConformance, string.Empty, conditions.ToArray());
                if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    // A condition takes no effect list (SPEC F.6): the block is the conformance's, whatever item starts it.
                    var block = this.ParseConditionalImplementationBlock(ref reader);
                    this.AddLast(new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(span.Start, block.Span.End), KotoKind.ConditionalConformance, string.Empty, [constraint, premises, block], separator: " when "));
                    return;
                }

                this.AddLast(new SyntaxFormKoto(ref reader, span, KotoKind.ConditionalConformance, string.Empty, [constraint, premises], separator: " when "));
                reader.ExpectLineEnd();
                return;
            }

            if (!acceptsTypeConstraints)
            {
                reader.Diagnostic.Add(subject, DiagnosticCode.DuplicateTypeConstraintDefinition_Kd);
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                return;
            }

            CheckDeclarationOrder(ref reader, ref state.DeclarationOrder, DeclarationOrder.TypeConstraint, subject);
            reader.ExpectLineEnd();
            if (constraint is not null)
            {
                Parser.ParseCallableEffects(ref reader, constraint);
                this.AddTypeConstraint(constraint);
            }

            return;
        }

        var token = reader.CurrentToken;
        if (token.Kind == TokenKind.StartBlock)
        {
            // An indented block that no member header introduces is skipped whole; it never ends the Container (SPEC F.4.1).
            Parser.OmitDeclaration(ref reader, reader.Unexpected(SyntaxForm.IndentedBody));
            return;
        }

        if (state.ParseDeclarationContainers &&
            this.TryParseDeclarationContainer(ref reader, token))
        {
            return;
        }

        if (!state.ParseDeclarationContainers && Parser.IsContainerDeclarationStart(ref reader))
        {
            // No Container nests in an enum or a Contract (SPEC 6.1.1); the declaration is skipped with its body.
            Parser.OmitDeclaration(ref reader, reader.Unexpected(token.Kind == TokenKind.Extension ? SyntaxForm.ExtensionDeclaration : SyntaxForm.ContainerDeclaration, token.Span));
            return;
        }

        if (!this.TryParsePropertyOrFunction(ref reader, ref state.DeclarationOrder))
        {
            SkipUnexpectedDeclaration(ref reader, token);
        }
    }

    /// <summary>Parses the indented list of a conditional conformance (SPEC F.6.2) as the members of a detached container of this kind.</summary>
    /// <param name="reader">The token reader at the list's StartBlock.</param>
    /// <returns>The block of the list's items.</returns>
    private CodeBlockKoto ParseConditionalImplementationBlock(ref TokenReader reader)
    {
        var start = reader.CurrentTokenRange.Start;
        var temporary = this.CreateDetached(reader.CodeContext, reader.CurrentTokenRange);
        temporary.Parse(ref reader);
        var items = new List<Koto>();
        items.AddRange(temporary.TypeConstraints);
        items.AddRange(temporary.Members);
        items.AddRange(temporary.NestedDeclarationContainers);
        return new CodeBlockKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, reader.PreviousSyntaxEnd)), items)
        {
            DeclarationContext = this.TokenKind,
        };
    }

    /// <summary>The owner of a member or root list's excluded syntax (SPEC 19.4): one detached container of the list container's kind, which
    /// shares its generic parameters (<see cref="CreateDetached"/>), so excluded syntax registers nothing in the container but keeps source
    /// order. It is created at the first exclusion the list enters outside excluded syntax and used while inside it; a list that starts inside
    /// excluded syntax keeps its container, which is already the excluded syntax's own.</summary>
    /// <typeparam name="TContainer">The container's type.</typeparam>
    /// <param name="container">The list's container.</param>
    private protected struct ExcludedOwner<TContainer>(TContainer container)
        where TContainer : DeclarationContainerKoto
    {
        private TContainer? detached;

        /// <summary>Gets the list's container, which receives its selected syntax.</summary>
        public readonly TContainer Container => container;

        /// <summary>Prepares the owner before the list enters excluded syntax.</summary>
        /// <param name="reader">The token reader, before the excluded syntax.</param>
        public void Enter(ref TokenReader reader)
        {
            if (!reader.InExcludedSyntax)
            {
                this.detached ??= (TContainer)container.CreateDetached(reader.CodeContext, container.Span);
            }
        }

        /// <summary>Gets the container that receives the item at the reader.</summary>
        /// <param name="reader">The token reader at the item.</param>
        /// <returns>The detached container inside excluded syntax the list entered, otherwise the list's container.</returns>
        public readonly TContainer Of(ref TokenReader reader)
            => reader.InExcludedSyntax && this.detached is { } detached ? detached : container;
    }

    /// <summary>The members of one Declaration Container body (SPEC F.6.1, F.6.2); excluded syntax goes to its
    /// <see cref="ExcludedOwner{TContainer}"/>.</summary>
    /// <param name="container">The container.</param>
    /// <param name="state">The member state of the body.</param>
    private struct MemberItems(DeclarationContainerKoto container, MemberParseState state) : Parser.IItemList
    {
        private ExcludedOwner<DeclarationContainerKoto> owner = new(container);

        private MemberParseState state = state;

        public readonly bool RequiresItem => false;

        public void EnterExcluded(ref TokenReader reader)
            => this.owner.Enter(ref reader);

        public void ParseItem(ref TokenReader reader)
            => this.owner.Of(ref reader).ParseMemberItem(ref reader, ref this.state);

        // A #switch keeps its marker only in selected syntax (Parser.CompileTimeSwitchArms.Unselected), so it goes to the container.
        public readonly void AddUnselected(ref TokenReader reader, Koto unselected)
            => this.owner.Container.AddLast(unselected);
    }
}
