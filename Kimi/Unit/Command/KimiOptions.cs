// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

public class KimiOptions
{
    [SimpleOption("Target")]
    public string Target { get; set; } = string.Empty;

    [SimpleOption("Debug")]
    public bool Debug { get; set; } = false;

    [SimpleOption("LlvmBin")]
    public string? LlvmBin { get; set; }

    [SimpleOption("AllowUnpinnedToolchain")]
    public bool AllowUnpinnedToolchain { get; set; }
}
