// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public enum KeywordKind : byte
{
    // Primitive types
    Namespace = 32,
    Use,
    Group,
    Struct,
    Enum,
    Public,
    Protected,
    Private,
    Internal,
    Protected_or_internal,
    Protected_and_internal,
    Reference,
}
