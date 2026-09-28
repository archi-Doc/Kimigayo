// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>A comparison's concrete, verified witness composition; Operators preserves source operator semantics.</summary>
internal sealed record BoundComparison(BoundType Type, bool Equality, bool Operators, BoundCall? Implementation, BoundComparison[] Parts);
