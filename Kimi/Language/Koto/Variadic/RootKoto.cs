// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

[TinyhandObject]
public sealed partial class RootKoto : GroupKoto
{
    private readonly Utf16Hashtable<GroupKoto> namespaceToGroupNode = new();

    public RootKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader,  range)
    {
    }

    public override string ToString()
        => $"Root: {this.Name}";

    public override void Parse(ref TokenReader reader)
    {
        if (reader.TryPeek(out var token))
        {
            if (token.IsIdentifierToken(Constants.NamespaceKeyword))
            {// namespace
                reader.Advance();
                var qualifiedName = KotoHelper.ValidateAndGetNamespace(ref reader);
                // var @namespace = this.GetOrAddGroup(qualifiedName);
                // this.namespaceToGroupNode.TryAdd(qualifiedName, @namespace);

                //this.RootNode.SetNamespace(qualifiedName);
            }
        }

        base.Parse(ref reader);
    }
}
