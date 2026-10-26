// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The formatting runtime's rows (R5 C9a; SPEC 22.10.2): the kind by which the runtime's built-in routines name a built-in formattable Type,
// the one table both generators read.
public sealed partial class FunctionCodegenTest
{
    // SPEC 22.10.2: an integer's kind by its width and signedness (a wrapping integer's by its argument, an isize's as i64's), then f32, f64,
    // char, bool, Unit and string; -1 exactly for a Type without built-in formatting.
    [Fact]
    public void FormatKindsAreTheRuntimesNumbering()
    {
        string[] names = ["i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64", "i128", "u128", "isize", "usize", "f32", "f64", "char"];
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 7, 8, 11, 12, 13], names.Select(static x => LegacyRuntimeHelpers.FormatKind(BoundType.Primitives[x])));
        Assert.Equal([14, 15, 16], new[] { BoundType.Boolean, BoundType.Unit, BoundType.String }.Select(LegacyRuntimeHelpers.FormatKind));
        Assert.Equal(2, LegacyRuntimeHelpers.FormatKind(BoundType.WrappingOf(BoundType.Primitives["u8"])));
        Assert.Equal(-1, LegacyRuntimeHelpers.FormatKind(BoundType.Range));
        Assert.All(
            BoundType.Primitives.Values.Concat(BoundType.WrappingScalars.Values),
            static x => Assert.Equal(FormattingTypes.IsBuiltin(x), LegacyRuntimeHelpers.FormatKind(x) >= 0));
    }
}
