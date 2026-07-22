// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;

namespace Kimi;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class ProjectFile
{
    public string[] Targets { get; set; } = [];

    public KotonohaIdentifier[] KotonohaArray { get; set; } = [];

    public string[] Alias { get; set; } = [];
}
