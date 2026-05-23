// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class SolutionFile
{
    [TinyhandObject(ImplicitMemberNameAsKey = true)]
    public partial record class DefaultClass
    {
        public string LangVersion { get; init; } = "0.0.1";

        public string Version { get; init; } = "0.0.1";
    }

    public string[] Projects { get; init; } = [];

    public DefaultClass Default { get; init; } = new();
}
