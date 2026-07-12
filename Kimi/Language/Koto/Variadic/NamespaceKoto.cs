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
}
