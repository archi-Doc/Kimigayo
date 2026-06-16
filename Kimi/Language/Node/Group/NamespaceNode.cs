// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Language;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

public sealed class NamespaceNode : GroupNode
{
    public UrlDiagnostic Diagnostic { get; }

    private readonly Utf16Hashtable<GroupNode> namespaceToGroupNode = new();

    public NamespaceNode()
    {
    }

    public override void Read(ref TokenReader reader)
    {
        if (reader.TryPeek(out var token))
        {
            if (token.IsIdentifierToken(Constants.NamespaceKeyword))
            {// namespace
                reader.MoveNext();
                var multiIdentifier = NodeHelper.ValidateAndGetNamespace(this.Diagnostic, reader);
                var @namespace = this.GetOrAddGroup(multiIdentifier);
                this.namespaceToGroupNode.TryAdd(multiIdentifier, @namespace);

                this.CurrentGroup = @namespace;
            }
        }


    }
}
