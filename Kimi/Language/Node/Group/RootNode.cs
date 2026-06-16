// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Language;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

public sealed class RootNode : GroupNode
{
    public UrlDiagnostic Diagnostic { get; }

    public GroupNode CurrentGroup { get; private set; }

    private readonly HashSet<string> alias = new();
    private readonly Utf16Hashtable<GroupNode> namespaceToGroupNode = new();
    private bool allowTopLevelKeyword = true;

    public RootNode(UrlDiagnostic diagnostic)
    {
        this.Diagnostic = diagnostic;
        this.CurrentGroup = this.GetOrAddGroup(Constants.DefaultNamespace);
    }

    public override void Read(ref TokenReader reader)
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
