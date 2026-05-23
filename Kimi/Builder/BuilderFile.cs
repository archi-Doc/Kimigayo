// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Tinyhand;

namespace Kimigayo.Builder;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class BuilderFile
{
    [TinyhandObject(ImplicitMemberNameAsKey = true)]
    public partial record class DefaultClass
    {
        public double LangVersion { get; init; } = 0.1d;

        public double Version { get; init; } = 0.1d;
    }

    public string[] Projects { get; init; } = [];

    public DefaultClass Default { get; init; } = new();
}
