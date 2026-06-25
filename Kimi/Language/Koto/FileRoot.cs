// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Arc.Collections;
using Kimigayo.Language;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

public sealed class FileRoot
{
    public DiagnosticCollection Diagnostic { get; }

    public GroupKoto CurrentGroup { get; private set; }

    private readonly GroupKoto rootGroup;
    private readonly HashSet<string> alias = new();
    private readonly Utf16Hashtable<GroupKoto> namespaceToGroupNode = new();
    private bool allowTopLevelKeyword = true;

    public FileRoot(DiagnosticCollection diagnostic)
    {
        this.rootGroup = new();
        this.Diagnostic = diagnostic;
        this.SetNamespace(Constants.DefaultNamespace);
    }

    [MemberNotNull(nameof(CurrentGroup))]
    public void SetNamespace(ReadOnlySpan<char> qualifiedName)
    {
        this.CurrentGroup = this.rootGroup.GetOrAddGroup(qualifiedName, KotoKind.Namespace);
    }

    public void Parse(ref TokenReader reader)
    {
        if (reader.TryPeek(out var token))
        {
            if (token.Kind == TokenKind.Sharp)
            {// #Attribute
                var koto = AttributeKotoHelper.Parse(ref reader);
                // var bin = TinyhandSerializer.Serialize(koto);
            }
            else if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                if (!this.allowTopLevelKeyword)
                {
                    goto UnexpectedTopLevelKeyword;
                }

                reader.MoveNext();
                var qualifiedName = KotoHelper.ValidateAndGetNamespace(ref reader);
                this.alias.Add(qualifiedName);
            }
        }

        this.allowTopLevelKeyword = false;
        this.CurrentGroup.Parse(ref reader);
        return;

UnexpectedTopLevelKeyword:
        this.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
    }
}
