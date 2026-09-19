// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

/// <summary>A self-targeted native supply. Values are data, never shell command fragments.</summary>
/// <remarks>Kind, ContractId and Sha256 also declare the requirement they expand into (SPEC 20.8.2.1).</remarks>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class NativeLibraryInput
{
    public string? Kind { get; set; }

    public string Input { get; set; } = string.Empty;

    public string? ContractId { get; set; }

    public string? Sha256 { get; set; }
}

/// <summary>A definition-side native requirement; semantic checking never reads native files.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class NativeRequirement
{
    public string? Kind { get; set; }

    public string? ContractId { get; set; }

    public string? Sha256 { get; set; }
}
