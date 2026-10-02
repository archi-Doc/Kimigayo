// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 2.5.1, 3.3, 5: the raw pointer Semantics is spelled raw; unsafe marks only the places where a promise is made.
public class RawPointerSyntaxTest
{
    [Fact]
    public void UnsafeIsNoSemanticsName()
    {
        Assert.True(CompilerHelper.TryParse("raw", out var raw));
        Assert.Equal(SemanticsKind.Raw, raw);
        Assert.False(CompilerHelper.TryParse("unsafe", out _));
        Assert.Equal("raw", SemanticsKind.Raw.ToText());
    }

    // raw is reserved only in Semantics positions and after '@'; elsewhere it is an ordinary Name.
    [Fact]
    public void RawIsAnOrdinaryNameElsewhere()
    {
        var c = MinimalEmissionTest.Analyze("struct Sample\n    public let raw: i32\n    public init(raw: i32) => self.raw = raw\n" +
            "func read(pointer: raw/i32) -> raw/i32 => pointer\npublic func main()\n    let raw = Sample.init(raw: 1)\n    let total = raw.raw + 1\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void TheOldSpellingIsNoSemantics()
    {
        var c = MinimalEmissionTest.Analyze("func read(pointer: unsafe/i32) -> () => ()\npublic func main() => ()\n");
        Assert.False(c.Binding.Result.IsComplete);
    }
}
