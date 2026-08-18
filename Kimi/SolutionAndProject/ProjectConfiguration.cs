// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class ProjectConfiguration
{
    public string LangVersion { get; set; } = "0.0.1";

    public string Version { get; set; } = "0.0.1";
}
