// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using Arc.Collections;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

namespace Kimigayo.Language;

[TinyhandObject(ReservedKeyCount = 2)]
public abstract partial class IdentifiableKoto : Koto
{
    [Key(1)]
    public ulong KotoId
    {
        get
        {
            if (field == 0)
            {
                var hash = XxHash3Slim.Hash64(this.GetIdentifier());

                var parent = this.Parent;
                while (parent is not null)
                {
                    if (parent is IdentifiableKoto identifiableKoto)
                    {
                        hash = XxHash3Slim.Combine(identifiableKoto.KotoId, hash);
                    }

                    parent = parent.Parent;
                }

                field = hash;
            }

            return field;
        }

        protected set
        {
            field = value;
        }
    }

    public abstract ReadOnlySpan<char> GetIdentifier();

    public IdentifiableKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    public IdentifiableKoto(CodeContext codeContext)
        : base(codeContext)
    {
    }

    /*internal IdentifiableKoto(FrontendMetadata compilationMetadata)
        : base(compilationMetadata)
    {
    }*/
}

/// <summary>
/// namespace, struct, enum, extension, contract.
/// </summary>
[TinyhandObject]
public partial class GroupKoto : IdentifiableKoto
{
    #region FieldAndProperty

    [Key(2)]
    public string Name { get; protected set; } = string.Empty;

    [Key(3)]
    protected List<Koto> KotoList { get; set; } = [];

    private readonly Utf16Hashtable<GroupKoto> identifierToGroupKoto = new();

    public KotoKind Kind => this switch
    {
        StructKoto => KotoKind.Struct,
        EnumKoto => KotoKind.Enum,
        ExtensionKoto => KotoKind.Extension,
        ContractKoto => KotoKind.Contract,
        _ => KotoKind.Group,
    };

    #endregion

    public GroupKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal GroupKoto(CodeContext codeContext)
        : base(codeContext)
    {
    }

    public override ReadOnlySpan<char> GetIdentifier()
        => this.Name;

    public void AddLast(Koto koto)
    {
        this.KotoList.Add(koto);
        koto.Parent = this;
    }

    public override string ToString()
    {
        if (this.IsRoot)
        {
            return "Root";
        }

        return this.Kind switch
        {
            KotoKind.Group => $"{TokenKind.Group.ToText()} {this.Name}",
            KotoKind.Struct => $"{TokenKind.Struct.ToText()} {this.Name}",
            KotoKind.Enum => $"{TokenKind.Enum.ToText()} {this.Name}",
            KotoKind.Extension => $"{TokenKind.Extension.ToText()} {this.Name}",
            KotoKind.Contract => $"{TokenKind.Contract.ToText()} {this.Name}",
            _ => string.Empty,
        };
    }

    public void Parse(ref Token token, ref TokenReader reader)
    {
        while (true)
        {
            GroupKoto? nextGroup = default;

            if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias (not supported)
                _ = KotoHelper.ValidateAndGetNamespace2(ref reader);
                reader.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
            }
            else if (token.Kind == TokenKind.EndBlock)
            {// Exit block
                _ = KotoParser.ConsumeAttributeModifierAndRead(ref reader, out token);
                break;
            }
            else if (token.Kind == TokenKind.Let ||
                token.Kind == TokenKind.Var)
            {// let a = 1, var b = 2
                var fieldKoto = KotoParser.ParseField(ref reader, ref token);
                if (fieldKoto is not null)
                {
                    this.AddLast(fieldKoto);
                }
            }
            else if (token.Kind == TokenKind.RootGroup)
            {// rootgroup
                var name = KotoHelper.ValidateAndGetNamespace(ref reader);
                var groupKoto = this.Kotonoha.RootKoto.GetOrAddGroup(name, TokenKind.Group);
                this.CodeContext.CurrentGroup = groupKoto;

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                }

                nextGroup = groupKoto;
            }
            else if (token.Kind == TokenKind.Group)
            {// group
            }
            else if (token.Kind == TokenKind.Struct)
            {// struct
                var r = KotoHelper.ParseGroupDeclaration(ref reader);
                var structKoto = (StructKoto)this.GetOrAddGroup(r.Name, token.Kind);

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                    nextGroup = structKoto;
                }

                if (r.List is not null)
                {
                    structKoto.BaseList.AddRange(r.List);
                }
            }
            else
            {// unknown
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
            }

            // Consume Attribute and modifiers
            _ = KotoParser.ConsumeAttributeModifierAndRead(ref reader, out token);
            if (!token.IsValid)
            {
                return;
            }

            if (nextGroup is not null)
            {
                nextGroup.Parse(ref token, ref reader);
            }
        }
    }

    public override void Unparse(StringWriter writer)
    {//
        foreach (var x in this.KotoList)
        {
            x.Unparse(writer);
        }

        if (this.KotoList.Count > 0)
        {
            writer.WriteLine();
        }

        var groups = this.identifierToGroupKoto.ToArray();
        foreach (var x in groups)
        {
            x.Unparse(writer);
        }
    }

    public GroupKoto GetOrAddGroup(ReadOnlySpan<char> qualifiedName, TokenKind kind)
    {
        var text = qualifiedName;
        var group = this;
        while (true)
        {
            var index = text.IndexOf(Constants.DotChar);
            if (index < 0)
            {
                GetOrAddGroup(ref group, text, kind);
                return group;
            }

            var segment = text[..index];
            GetOrAddGroup(ref group, segment, TokenKind.Group);
            text = text[(index + 1)..];
        }
    }

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text, TokenKind kind)
    {
        var parent = group;
        var codeContext = group.CodeContext;
        Func<string, GroupKoto> factory = kind switch
        {
            TokenKind.Struct => x => new StructKoto(codeContext),
            TokenKind.Enum => x => new EnumKoto(codeContext),
            TokenKind.Extension => x => new ExtensionKoto(codeContext),
            TokenKind.Contract => x => new ContractKoto(codeContext),
            _ => x => new GroupKoto(codeContext),
        };

        group = (GroupKoto)group.identifierToGroupKoto.GetOrAdd(text, factory);
        if (string.IsNullOrEmpty(group.Name))
        {
            group.Parent = parent;
            group.Name = text.ToString();
        }
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
    }
}
