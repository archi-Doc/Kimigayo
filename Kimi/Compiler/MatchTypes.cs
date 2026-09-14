// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>The guarded Subject subset shared by binding, ownership and emission.</summary>
internal static class MatchTypes
{
    internal static bool SupportsGuard(BoundType? type) => ScalarTypes.Supports(type) ||
        ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String);
}
