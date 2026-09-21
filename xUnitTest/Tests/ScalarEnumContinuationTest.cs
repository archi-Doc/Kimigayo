// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ScalarEnumContinuationTest
{
    [Theory]
    [InlineData("E<(i32, bool)>", ".Some((2, c))", ".Some((var n, let flag)) if flag\n                    n += 1\n                    return\n                .Some(_) => x = 3\n                .None => exit")]
    [InlineData("E<E<(i32, bool)>>", ".Some(.Some((2, c)))", ".Some(.Some((let n, _))) if (return) => x = n\n                .Some(_) => x = 3\n                .None => exit")]
    [InlineData("(E<i32>, bool)", "(.None, c)", "(.Some(let n), _) if (if c => return else => false) => x = n\n                _ => x = 3")]
    public void ScalarPayloadCasesPreserveMixedContinuationHistories(string type, string value, string arms)
    {
        var c = MinimalEmissionTest.Analyze(Source(type, value, arms, "var x = 1", "let y = x"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void MissingInitializationInOneCaseRemainsAnError()
    {
        var c = MinimalEmissionTest.Analyze(Source("E<i32>", ".Some(2)", ".Some(let n) => x = n\n                .None => return", "var x: i32", "let y = x"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WholeEnumAcquisitionHonorsItsDeclaredCopyCapability(bool copy)
    {
        var source = Source("E<i32>", ".Some(2)", ".Some(let n) => x = n\n                .None => return", "var x = 1", "let again = subject");
        if (copy)
        {
            source = source.Replace("enum E<T>\n", "enum E<T>\n    Self is Copy when T is Copy\n", StringComparison.Ordinal);
        }

        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        if (copy)
        {
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
            ScalarEmissionTest.EmitFixture("ScalarEnumWindowCopy", source, string.Empty);
        }
        else
        {
            Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
            Assert.False(c.Emission.Validate(out _));
        }
    }

    private static string Source(string type, string value, string arms, string declaration, string tail)
        => "enum E<T>\n    Some(T)\n    None\nfunc stop() -> Never => $abort(\"enum continuation\")\nfunc f(c: bool)\n    " + declaration +
            "\n    let subject: " + type + " = " + value + "\n    do\n        loop\n            if c => return else => exit\n            match subject\n                " + arms +
            "\n        stop()\n    " + tail + "\nf(true)";
}
