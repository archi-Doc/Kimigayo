// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

/// <summary>A logical native link input. Values are data, never shell command fragments.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class NativeLibraryInput
{
    public string Kind { get; set; } = "import";

    public string Input { get; set; } = string.Empty;
}
