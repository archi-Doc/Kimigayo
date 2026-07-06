// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

[TinyhandObject]
public sealed partial class NamespaceKoto : GroupKoto
{
    private readonly Utf16Hashtable<GroupKoto> namespaceToGroupNode = new();

    public NamespaceKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal NamespaceKoto(FrontendMetadata compilationMetadata)
        : base(compilationMetadata)
    {
    }

    public override string ToString()
        => $"Namespace: {this.Name}";

    public override void Parse(ref TokenReader reader)
    {
Loop:
        if (reader.TryPeek(out var token))
        {
            if (token.IsIdentifierToken(Constants.AliasKeyword))
            {// alias
                reader.Diagnostic.AddToken(token, Hashed.Kimi.TopLevelKeywordAfterCode);
            }
            else if (token.IsIdentifierToken(Constants.NamespaceKeyword))
            {// namespace
                reader.Advance();
                var qualifiedName = KotoHelper.ValidateAndGetNamespace(ref reader);

                goto Loop;

                // var @namespace = this.GetOrAddGroup(qualifiedName);
                // this.namespaceToGroupNode.TryAdd(qualifiedName, @namespace);

                //this.RootNode.SetNamespace(qualifiedName);
            }
        }

        base.Parse(ref reader);
    }
}
