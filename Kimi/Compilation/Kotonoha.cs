// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;

namespace Kimigayo.Language;

[TinyhandObject]
public sealed partial class Kotonoha
{
    [Key(0)]
    public string Name { get; private set; } = string.Empty;

    [Key(1)]
    public string Url { get; private set; } = string.Empty;

    [Key(2)]
    public Utf16Hashtable<NamespaceKoto> Namespaces { get; private set; } = new();

    [Key(3)]
    public List<KimiFile> KimiList { get; private set; } = [];

    public Kotonoha(string name, string url)
    {
        this.Name = name;
        this.Url = url;
    }

    public override string ToString()
        => $"Kotonoha: {this.Name}";
}
