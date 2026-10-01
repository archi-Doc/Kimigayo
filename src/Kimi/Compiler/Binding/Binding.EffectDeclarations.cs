// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.4.10.1: an effect item declares a bound of its Contract. A rejected bound word leaves a recovery whose checks
    // rest on the syntax Error.
    private BoundType? BindEffectBound(EffectBoundKoto effect)
        => effect.CodeContext.RecoveryCause(effect) is not null ? Complete(effect, null) : this.Fail(effect, BindingFailure.Unsupported);
}
