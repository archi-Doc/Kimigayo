// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;
using Kimigayo.Language;

[TinyhandObject]
public sealed partial class ExtensionKoto : GroupKoto
{
    public ExtensionKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal ExtensionKoto(CodeContext codeContext)
        : base(codeContext)
    {
    }
}
