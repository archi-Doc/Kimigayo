// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Linq.Expressions;
using Arc.Collections;
using Kimi.Language;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

public sealed class FileRoot
{
    public DiagnosticCollection Diagnostic { get; }

    public GroupNode CurrentGroup { get; private set; }

    private readonly GroupNode rootGroup;
    private readonly HashSet<string> alias = new();
    private readonly Utf16Hashtable<GroupNode> namespaceToGroupNode = new();
    private bool allowTopLevelKeyword = true;

    public FileRoot(DiagnosticCollection diagnostic)
    {
        this.rootGroup = new(this);
        this.Diagnostic = diagnostic;
        this.CurrentGroup = this.SetNamespace(Constants.DefaultNamespace);
    }

    public GroupNode SetNamespace(ReadOnlySpan<char> qualifiedName)
    {
        var group = this.rootGroup.GetOrAddGroup(qualifiedName);
        return group;
    }

    public void Parse(ref TokenReader reader)
    {
        if (reader.TryPeek(out var token))
        {
            if (token.Kind == TokenKind.Sharp)
            {// #Condition
                var parser = new AttributeParser(this.Diagnostic);
                var koto = parser.ParseConditionDirective(ref reader);
            }
            else if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                if (!this.allowTopLevelKeyword)
                {
                    goto UnexpectedTopLevelKeyword;
                }

                reader.MoveNext();
                var qualifiedName = KotoHelper.ValidateAndGetNamespace(this.Diagnostic, ref reader);
                this.alias.Add(qualifiedName);
            }
        }

        this.allowTopLevelKeyword = false;
        this.CurrentGroup.Read(ref reader);
        return;

UnexpectedTopLevelKeyword:
        this.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
    }
}
