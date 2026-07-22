// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class KotonohaIdentifier
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}
