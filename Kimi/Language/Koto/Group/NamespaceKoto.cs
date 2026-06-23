// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Language;
using Kimigayo.Language;

[TinyhandObject]
public sealed partial class NamespaceKoto : GroupKoto
{
    private readonly Utf16Hashtable<GroupKoto> namespaceToGroupNode = new();

    public NamespaceKoto()
        : base()
    {
    }

    public override void Read(ref TokenReader reader)
    {
        if (reader.TryPeek(out var token))
        {
            if (token.IsIdentifierToken(Constants.NamespaceKeyword))
            {// namespace
                reader.MoveNext();
                var qualifiedName = KotoHelper.ValidateAndGetNamespace(reader.Diagnostic, ref reader);
                // var @namespace = this.GetOrAddGroup(qualifiedName);
                // this.namespaceToGroupNode.TryAdd(qualifiedName, @namespace);

                //this.RootNode.SetNamespace(qualifiedName);
            }
        }

        base.Read(ref reader);
    }
}
