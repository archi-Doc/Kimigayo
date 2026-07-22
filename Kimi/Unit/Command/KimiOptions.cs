// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Kimi.Command;

public class KimiOptions
{
    [SimpleOption("Target")]
    public string Target { get; set; } = string.Empty;

    [SimpleOption("DumpToken")]
    public bool DumpToken { get; set; } = false;
}
