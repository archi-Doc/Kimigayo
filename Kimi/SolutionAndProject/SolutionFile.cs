// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class SolutionFile
{
    public List<string> Projects { get; init; } = [];

    public ProjectConfiguration Configuration { get; init; } = new();
}
