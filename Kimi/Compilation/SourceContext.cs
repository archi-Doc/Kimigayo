// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;

namespace Kimigayo.Language;

public class SourceContext
{
    public Utf16Hashtable<Koto[]> NamespaceToKotoArray { get; } = new();
}
