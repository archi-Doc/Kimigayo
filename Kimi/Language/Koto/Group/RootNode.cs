// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Language;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

public sealed class RootNode
{
    public UrlDiagnostic Diagnostic { get; }

    public GroupNode CurrentGroup { get; private set; }

    private readonly GroupNode rootGroup;
    private readonly HashSet<string> alias = new();
    private readonly Utf16Hashtable<GroupNode> namespaceToGroupNode = new();
    private bool allowTopLevelKeyword = true;

    public RootNode(UrlDiagnostic diagnostic)
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

    public void Read(ref TokenReader reader)
    {
        if (reader.TryPeek(out var token))
        {
            if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                if (!this.allowTopLevelKeyword)
                {
                    goto UnexpectedTopLevelKeyword;
                }

                reader.MoveNext();
                var qualifiedName = NodeHelper.ValidateAndGetNamespace(this.Diagnostic, reader);
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
