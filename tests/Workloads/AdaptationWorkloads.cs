// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;

namespace Verification;

// Shared compiler inputs; timing and repetition belong to the benchmark harness.
internal static class AdaptationWorkloads
{
    internal static readonly string Single = Create(1);

    internal static string Create(int functions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(functions, 1);
        var source = new StringBuilder();
        for (var i = 0; i < functions; i++)
        {
            source.Append("func box").Append(i).Append("<s/T>(value: T) -> s/T\n    s is owner or object\n    T is ObjectPayload\n    return value@move@s\n");
        }

        for (var i = 0; i < functions; i++)
        {
            source.Append("let plain").Append(i).Append(" = box").Append(i).Append("<i32>(7)\n");
            source.Append("let first").Append(i).Append(" = box").Append(i).Append("<obj/i32>(7)\n");
            source.Append("let second").Append(i).Append(" = box").Append(i).Append("<rc/bool>(true)\n");
            source.Append("let third").Append(i).Append(" = box").Append(i).Append("<arc/i32>(9)\n");
        }

        return source.ToString();
    }
}
