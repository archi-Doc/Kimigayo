// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi;

/// <summary>A Project scalar setting; exactly one typed value must be specified.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record class CompileTimeSetting
{
    /// <summary>Gets or sets a Boolean value.</summary>
    public bool? Bool { get; set; }

    /// <summary>Gets or sets an i64 value.</summary>
    public long? Integer { get; set; }

    /// <summary>Gets or sets a string value.</summary>
    public string? String { get; set; }

    internal bool TryGetValue(out BasicValue value)
    {
        value = default;
        if ((this.Bool.HasValue ? 1 : 0) + (this.Integer.HasValue ? 1 : 0) + (this.String is not null ? 1 : 0) != 1)
        {
            return false;
        }

        value = this.Bool is { } boolean ? new(boolean) : this.Integer is { } integer ? new(integer) : new(this.String!);
        return true;
    }
}
