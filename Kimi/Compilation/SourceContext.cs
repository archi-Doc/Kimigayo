// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;

namespace Kimi.Compiler;

public class SourceContext
{
    public Utf16Hashtable<Koto[]> NamespaceToKotoArray { get; } = new();
}
