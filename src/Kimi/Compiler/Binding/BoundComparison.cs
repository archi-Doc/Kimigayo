// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>A comparison's concrete, verified witness composition; Operators preserves source operator semantics.</summary>
[Transitional("R7", Reason = "The legacy comparison composition beside TyCtxt.Comparison, read by A4's Witnesses until A-12 reads the view, else R7.")]
internal sealed record BoundComparison(BoundType Type, bool Equality, bool Operators, CallPlan? Implementation, BoundComparison[] Parts);
