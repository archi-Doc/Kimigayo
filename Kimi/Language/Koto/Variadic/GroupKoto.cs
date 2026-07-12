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
/// group, struct, enum.
/// </summary>
[TinyhandObject]
public abstract partial class GroupKoto : IdentifiableKoto
{
    #region FieldAndProperty

    [Key(2)]
    public string Name { get; protected set; } = string.Empty;

    [Key(3)]
    protected List<Koto> KotoList { get; set; } = [];

    private readonly Utf16Hashtable<Koto> identifierToGroupKoto = new();

    public KotoKind Kind => this switch
    {
        NamespaceKoto => KotoKind.Namespace,
        StructKoto => KotoKind.Struct,
        EnumKoto => KotoKind.Enum,
        ExtensionKoto => KotoKind.Extension,
        ContractKoto => KotoKind.Contract,
        _ => KotoKind.Invalid,
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

    public void Add(Koto koto)
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
            KotoKind.Namespace => $"namespace {this.Name}",
            KotoKind.Struct => $"struct {this.Name}",
            KotoKind.Enum => $"enum {this.Name}",
            KotoKind.Extension => $"extension {this.Name}",
            KotoKind.Contract => $"contract {this.Name}",
            _ => string.Empty,
        };
    }

    public void Parse(ref Token token, ref TokenReader reader)
    {
        while (true)
        {
            GroupKoto? nextGroup = default;

            if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                _ = KotoHelper.ValidateAndGetNamespace2(ref reader);
                reader.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
            }
            else if (token.Kind == TokenKind.EndBlock)
            {
                // reader.Clear();
                break;
            }
            else if (token.Kind == TokenKind.Let ||
                token.Kind == TokenKind.Var)
            {// let a = 1, var b = 2
                // KotoHelper.ParseVariable(this, ref reader, ref token);
            }
            else if (token.Kind == TokenKind.Namespace ||
                token.Kind == TokenKind.Struct ||
                token.Kind == TokenKind.Enum ||
                token.Kind == TokenKind.Extension ||
                token.Kind == TokenKind.Contract)
            {// group
                string name;
                if (token.Kind == TokenKind.Namespace)
                {
                    name = KotoHelper.ValidateAndGetNamespace(ref reader);
                }
                else
                {
                    name = KotoHelper.ValidateAndGetGroupName(ref reader);
                }

                var group = this.GetOrAddGroup(name, token.Kind);
                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.Advance();
                    nextGroup = group;
                }
            }

            // Consume Attribute and modifiers
            _ = KotoParser.ConsumeTriviaAndRead(ref reader, out token);
            if (!token.IsValid)
            {
                return;
            }

            if (nextGroup is not null)
            {
                nextGroup.Parse(ref token, ref reader);
            }

            // this.Parse(ref token, ref reader);
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
            GetOrAddGroup(ref group, segment, kind);
            text = text[(index + 1)..];
        }
    }

    private static void GetOrAddGroup(ref GroupKoto group, ReadOnlySpan<char> text, TokenKind kind)
    {
        var parent = group;
        var codeContext = group.CodeContext;
        Func<string, Koto> factory = kind switch
        {
            TokenKind.Namespace => x => new NamespaceKoto(codeContext),
            TokenKind.Struct => x => new StructKoto(codeContext),
            TokenKind.Enum => x => new EnumKoto(codeContext),
            TokenKind.Extension => x => new ExtensionKoto(codeContext),
            TokenKind.Contract => x => new ContractKoto(codeContext),
            _ => throw new InvalidOperationException(),
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
