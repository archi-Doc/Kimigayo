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

    internal NamespaceKoto(CodeContext codeContext)
        : base(codeContext)
    {
    }

    /*internal NamespaceKoto(FrontendMetadata compilationMetadata)
        : base(compilationMetadata)
    {
    }*/

    public override string ToString()
        => $"Namespace: {this.Name}";

    public new void Parse(ref TokenReader reader)
    {
        while (true)
        {
            _ = KotoParser.ConsumeAttribute(ref reader);
            if (!reader.TryRead(out var token))
            {
                return;
            }

            if (token.IsIdentifierToken(Constants.NamespaceKeyword))
            {// namespace
                var qualifiedName = KotoHelper.ValidateAndGetNamespace(ref reader);
                var fileRoot = new FileRoot(default!);
                fileRoot.SetNamespace(qualifiedName);
                return;
            }

            this.Parse(ref token, ref reader);
        }
    }
}
