// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public class CodeContext
{
    public Compilation Compilation { get; }

    internal CodeContext(Compilation compilation)
    {
        this.Compilation = compilation;
    }
}
