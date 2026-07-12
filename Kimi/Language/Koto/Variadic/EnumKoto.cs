// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;
using Kimigayo.Language;

[TinyhandObject]
public sealed partial class EnumKoto : GroupKoto
{
    public EnumKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal EnumKoto(CodeContext codeContext)
        : base(codeContext)
    {
    }
}
