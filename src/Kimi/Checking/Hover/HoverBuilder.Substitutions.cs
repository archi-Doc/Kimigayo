// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi.Checking;

internal sealed partial class HoverBuilder
{
    // A closure context's identity (DECISIONS R5-M36): its binder and every binding, so contexts that differ in any binding differ while
    // equal contexts of two compilations agree.
    private HoverKey SubstIdentity(Subst subst)
    {
        var text = FormattableString.Invariant($"closure context;{subst.Types.Length},{subst.Origins.Length},{subst.Inputs.Length}");
        var parts = new HoverKey[3 + subst.Types.Length + subst.Lengths.Length + subst.Origins.Length + subst.Inputs.Length];
        parts[0] = this.SymbolIdentity(subst.Target);
        parts[1] = this.TypeIdentity(subst.Self);
        parts[2] = this.TypeIdentity(subst.Declaring);
        var index = 3;
        foreach (var type in subst.Types)
        {
            parts[index++] = this.TypeIdentity(type);
        }

        foreach (var length in subst.Lengths)
        {
            parts[index++] = this.LengthIdentity(length);
        }

        foreach (var origin in subst.Origins)
        {
            parts[index++] = this.OriginIdentity(origin);
        }

        foreach (var origin in subst.Inputs)
        {
            parts[index++] = this.OriginIdentity(origin);
        }

        return new(text, parts);
    }
}
