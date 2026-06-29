// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class SolutionFile
{
    [TinyhandObject(ImplicitMemberNameAsKey = true)]
    public partial record class DefaultClass
    {
        public string LangVersion { get; set; } = "0.0.1";

        public string Version { get; set; } = "0.0.1";
    }

    public List<string> Projects { get; init; } = [];

    public DefaultClass ProjectDefault { get; init; } = new();
}
