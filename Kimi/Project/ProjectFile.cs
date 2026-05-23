// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class ProjectFile
{
    [TinyhandObject(ImplicitMemberNameAsKey = true)]
    public partial record class PackageClass
    {
        public string Name { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;
    }

    public string[] Targets { get; set; } = [];

    public PackageClass[] Packages { get; set; } = [];

    public string[] Use { get; set; } = [];
}
