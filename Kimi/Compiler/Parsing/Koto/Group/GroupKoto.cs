// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection.Metadata;
using Arc.Collections;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// namespace, struct, enum, extension, contract.
/// </summary>
[TinyhandObject]
public partial class GroupKoto : IdentifiableKoto, ITokenParser
{
    public override KotoKind Akind => KotoKind.Group;

    // public static readonly TokenState DefaultState = new(default, ModifierKind.Public);

    #region FieldAndProperty

    [Key(3)]
    public ModifierKind Modifier { get; private set; }

    [Key(4)]
    public string Name { get; protected set; } = string.Empty;

    public KotoKind KotoKind => this switch
    {
        StructKoto => KotoKind.Struct,
        EnumKoto => KotoKind.Enum,
        ExtensionKoto => KotoKind.Extension,
        ContractKoto => KotoKind.Contract,
        _ => KotoKind.Group,
    };

    public TokenKind TokenKind => this switch
    {
        StructKoto => TokenKind.Struct,
        EnumKoto => TokenKind.Enum,
        ExtensionKoto => TokenKind.Extension,
        ContractKoto => TokenKind.Contract,
        _ => TokenKind.Group,
    };

    [IgnoreMember]
    public List<TypeSemanticsKoto> GenericArguments { get; } = [];

    [IgnoreMember]
    public List<IsKoto> TypeConstraints { get; } = [];

    [Key(5)]
    protected List<Koto> KotoList { get; set; } = [];

    [Key(6)]
    protected Utf16Hashtable<GroupKoto> IdentifierToGroupKoto { get; set; } = new();

    #endregion

    public GroupKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal GroupKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, range)
    {
        this.AttributeChain = state.AttributeKoto;
        this.Modifier = state.ModifierKind;
    }

    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    public void AddLast(Koto koto)
    {
        this.KotoList.Add(koto);
        koto.Parent = this;
    }

    public void AddGenericArguments(IEnumerable<TypeSemanticsKoto> genericArguments)
    {
        foreach (var argument in genericArguments)
        {
            this.GenericArguments.Add(argument);
            argument.Parent = this;
        }
    }

    public void AddTypeConstraint(IsKoto constraint)
    {
        this.TypeConstraints.Add(constraint);
        constraint.Parent = this;
    }

    public override string ToString()
    {
        if (this.IsRoot)
        {
            return "Root";
        }

        return $"{this.TokenKind.ToText()} {this.Name}";
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {// public group A
        if (this.AttributeChain is not null)
        {
            Parser.UnparseAttribute(this.AttributeChain, ref builder, KotoWriteOptions.AppendLineFeed);
        }

        if (this.IsRoot)
        {
            builder.Append("Root");
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
    }

    public void UnparseToRoot(ref IndentedStringBuilder builder)
    {// rootgroup A
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

    public void Parse(ref TokenReader reader)
    {
        var acceptsTypeConstraints = true;
        while (reader.CanRead)
        {
            Parser.ConsumeAttributeAndModifier(ref reader, out var isEnd);
            if (isEnd)
            {
                return;
            }

            if (acceptsTypeConstraints && Parser.IsTypeConstraintStart(ref reader))
            {
                var constraint = Parser.ParseTypeConstraint(ref reader);
                if (constraint is not null)
                {
                    this.AddTypeConstraint(constraint);
                }

                continue;
            }

            acceptsTypeConstraints = false;

            var token = reader.CurrentToken;
            var tokenKind = token.Kind;
            ITokenParser? nextParser = default;

            if (tokenKind == TokenKind.Alias)
            {// alias (not supported)
                reader.Advance();
                _ = KotoHelper.ParseQualifiedNameSegments(ref reader);
                reader.Diagnostic.Add(token.Span, DiagnosticCode.TopLevelKeywordAfterCode_Kd);
            }
            else if (tokenKind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }
            else if (tokenKind == TokenKind.EndBlock)
            {// Exit block
                reader.Advance();
                break;
            }
            else if (tokenKind == TokenKind.Let ||
                tokenKind == TokenKind.Var)
            {// let a = 1, var b = 2
                reader.Advance();
                var fieldKoto = Parser.ParseField(ref reader, ref token);
                if (fieldKoto is not null)
                {
                    this.AddLast(fieldKoto);
                }
            }
            else if (tokenKind == TokenKind.RootGroup)
            {// rootgroup
                reader.Advance();
                var name = KotoHelper.ValidateAndGetNamespace(ref reader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(true);
                    goto NextToken;
                }

                var state = reader.TakeContext();
                var groupKoto = this.Kotonoha.RootKoto.GetOrAddGroup(name, TokenKind.Group, state, token.Span);
                // this.CodeContext.CurrentGroup = groupKoto;

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                }

                nextParser = groupKoto;
            }
            else if (tokenKind == TokenKind.Group)
            {// group
                reader.Advance();
                var r = Parser.ParseGroupDeclaration(ref reader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(false);
                    goto NextToken;
                }

                var state = reader.TakeContext();
                var groupKoto = this.GetOrAddGroup(r.Name, tokenKind, state, token.Span);
                if (r.GenericArguments is not null)
                {
                    groupKoto.AddGenericArguments(r.GenericArguments);
                }

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                    nextParser = groupKoto;
                }
            }
            else if (tokenKind == TokenKind.Struct)
            {// struct
                reader.Advance();
                var r = Parser.ParseGroupDeclaration(ref reader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(false);
                    goto NextToken;
                }

                var state = reader.TakeContext();
                var structKoto = (StructKoto)this.GetOrAddGroup(r.Name, tokenKind, state, token.Span);
                if (r.GenericArguments is not null)
                {
                    structKoto.AddGenericArguments(r.GenericArguments);
                }

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                    nextParser = structKoto;
                }
            }
            else if (tokenKind == TokenKind.Func)
            {// public func Main() -> ()
                reader.Advance();
                var functionKoto = Parser.ParseFuncDeclaration(ref reader);
                if (functionKoto is not null)
                {
                    if (!functionKoto.IsExcluded)
                    {
                        this.AddLast(functionKoto);
                    }

                    while (reader.CurrentTokenKind == TokenKind.Separator)
                    {
                        reader.Advance();
                    }

                    if (reader.CurrentTokenKind == TokenKind.StartBlock)
                    {
                        reader.Advance();
                        nextParser = functionKoto;
                    }
                    else
                    {// Skip tokens up to StartBlock due to a syntax error.
                        reader.SkipUntil(TokenKind.StartBlock, default);
                        nextParser = functionKoto;
                    }
                }
            }
            else
            {// Other
                var koto = Parser.ParseExpression(ref reader);
                if (koto is ErrorKoto)
                {// Error
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                }
                else
                {
                    this.AddLast(koto);
                }
            }

NextToken:
            if (nextParser is not null)
            {
                nextParser.Parse(ref reader);
            }
        }
    }

    public void Clear()
    {
        this.KotoList.Clear(); // TODO
        this.IdentifierToGroupKoto.Clear(); // TODO
        this.GenericArguments.Clear();
        this.TypeConstraints.Clear();
    }

    public void UnparseAll(ref IndentedStringBuilder builder)
    {
        GroupKoto? currentGroup = this.IsRoot ? null : this;
        this.UnparseAllInternal(0, ref builder, false);
    }

    public GroupKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var text = qualifiedName;
        var group = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                GetOrAddGroup(ref group, text, kind, state, range);
                return group;
            }

            var segment = text[..index];
            GetOrAddGroup(ref group, segment, TokenKind.Group, default, default);
            text = text[(index + 1)..];
        }
    }

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text, TokenKind kind, TokenContext state, SourceSpan range)
    {
        var parent = group;
        var codeContext = group.CodeContext;
        Func<string, GroupKoto> factory = kind switch
        {
            TokenKind.Struct => x => new StructKoto(codeContext, state, range),
            TokenKind.Enum => x => new EnumKoto(codeContext, state, range),
            TokenKind.Extension => x => new ExtensionKoto(codeContext, state, range),
            TokenKind.Contract => x => new ContractKoto(codeContext, state, range),
            _ => x => new GroupKoto(codeContext, state, range),
        };

        group = (GroupKoto)group.IdentifierToGroupKoto.GetOrAdd(text, factory);
        if (string.IsNullOrEmpty(group.Name))
        {// New
            group.Parent = parent;
            group.Name = text.ToString();
        }
        else
        {// Existing
            group.Merge(state, range);
        }
    }

    private void Merge(TokenContext state, SourceSpan range)
    {
    }

    private void UnparseAllInternal(int indents, ref IndentedStringBuilder builder, bool parentDeclared)
    {
        var groupDeclared = false;

        if ((!this.IsRoot && (this.KotoList.Count > 0 || this.TypeConstraints.Count > 0))
            || this.Modifier != 0)
        {
            builder.EnsureTrailingBlankLine();
            if (this.KotoKind == KotoKind.Group)
            {// rootgroup A
                builder.SetIndent(0);
                this.UnparseToRoot(ref builder);
                builder.AppendLine();
                builder.IncrementIndent();
            }
            else
            {// struct A
                this.WriteTo(ref builder);
                builder.AppendLine();
                builder.IncrementIndent();
            }

            groupDeclared = true;
        }

        if (this.TypeConstraints.Count > 0)
        {
            foreach (var constraint in this.TypeConstraints)
            {
                constraint.WriteTo(ref builder);
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

        var groups = this.IdentifierToGroupKoto.ToArray();
        if (groups.Length > 0)
        {
            // DeclareGroup(builder, ref currentGroup);

            builder.EnsureTrailingBlankLine();
            foreach (var x in groups)
            {
                x.UnparseAllInternal(indents + 1, ref builder, groupDeclared);
            }
        }

        if (groupDeclared)
        {
            builder.DecrementIndent();
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
    }
}
