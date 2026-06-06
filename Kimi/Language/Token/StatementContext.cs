// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Language;

/// <summary>
/// Represents the syntactic context in which the statement appears.
/// </summary>
public enum StatementContext
{
    Root,
    Namespace,
    Group,
    Struct,
    Enum,
    Block,
}
