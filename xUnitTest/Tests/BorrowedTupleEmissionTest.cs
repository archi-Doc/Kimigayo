// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class BorrowedTupleEmissionTest
{
    [Theory]
    [InlineData("Local", "let pair: (i32, bool) = (42, true)\nlet r = pair@ref\nrequire r.0 == 42 and r.1 else => $abort(\"value\")")]
    [InlineData("ExclusiveRead", "var pair: (i32, bool) = (42, true)\nlet r = pair@uniq\nrequire r.0 == 42 and r.1 else => $abort(\"value\")")]
    [InlineData("Implicit", "func first(pair: ref/(i32, bool)) -> i32 => pair.0\nlet pair: (i32, bool) = (42, true)\nrequire first(pair) == 42 else => $abort(\"value\")")]
    [InlineData("Returned", "func view(pair: ref/(i32, bool)) -> ref/(i32, bool) from pair => pair\nlet pair: (i32, bool) = (42, true)\nlet r = view(pair@ref)\nrequire r.0 == 42 else => $abort(\"value\")")]
    [InlineData("Reborrow", "func first(pair: ref/(i32, bool)) -> i32 => pair.0\nfunc forward(pair: uniq/(i32, bool)) -> i32 => first(pair)\nvar pair: (i32, bool) = (42, true)\nrequire forward(pair@uniq) == 42 else => $abort(\"value\")")]
    [InlineData("LastUse", "var pair: (i32, bool) = (42, true)\nlet r = pair@ref\nlet n = r.0\npair = (7, false)\nrequire n == 42 and pair.0 == 7 else => $abort(\"value\")")]
    [InlineData("UnitPrefix", "func read(pair: ref/((), i64, bool)) -> i64 => pair.1\nrequire read(((), 42, true)) == 42 else => $abort(\"offset\")")]
    [InlineData("Padding", "func read(pair: ref/(bool, u8, i64, f32)) -> f32 => pair.3\nrequire read((true, 255, 42, 1.5)) == 1.5 else => $abort(\"offset\")")]
    public void ExecutesCheckedScalarReads(string name, string source)
        => ScalarEmissionTest.EmitFixture("BorrowedTuple" + name, source, string.Empty);

    [Theory]
    [InlineData("var pair: (i32, bool) = (1, true)\nlet r = pair@ref\npair = (2, false)\nlet n = r.0")]
    [InlineData("var pair: (i32, bool) = (1, true)\nlet r = pair@ref\npair.0 = 2\nlet n = r.0")]
    [InlineData("let pair: (i32, bool)\nlet r = pair@ref\nlet n = r.0")]
    [InlineData("func view(pair: ref/(i32, bool)) -> ref/(i32, bool) from pair => pair\nlet r = view((1, true))\nlet n = r.0")]
    [InlineData("let pair: (i32, string) = (1, \"owned\")\nlet moved = pair.1\nlet r = pair@ref\nlet n = r.0")]
    public void RejectsInvalidLifetimeOrInitialization(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.NotEmpty(c.Ownership.Issues);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("func read(pair: ref/(i32, bool)) => pair.2")]
    [InlineData("func read(pair: ref/(i32, bool)) => pair.999999999999999999999999999999")]
    [InlineData("func write(pair: ref/(i32, bool))\n    pair.0 = 2")]
    public void RejectsInvalidSelectorAndSharedWrites(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void BorrowingDoesNotConsumeOwnedTupleElements()
    {
        const string Source = "func read(pair: ref/(i32, string)) -> i32 => pair.0\nlet pair: (i32, string) = (42, \"owned\")\nrequire read(pair) == 42 else => $abort(\"value\")\nlet text = pair.1";
        var ir = ScalarEmissionTest.EmitFixture("BorrowedTupleCleanup", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("BorrowedTupleCleanup", Source, ir, string.Empty, "owned=1", order: [0]);
    }

    [Fact]
    public void RebindAndReloadRetainTupleBorrowPlans()
    {
        var c = MinimalEmissionTest.Analyze("func first(pair: ref/(u8, bool)) -> u8 => pair.0\nrequire first((255, true)) == 255 else => $abort(\"value\")");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        for (var i = 0; i < 2; i++)
        {
            Assert.True(restored.Bind().IsComplete);
            Assert.True(restored.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(restored.Ownership.Analyze().IsVerified);
            using var output = new StringWriter();
            Assert.True(restored.Emission.WriteIr(output, out error), error);
            Assert.Equal(original.ToString(), output.ToString());
        }
    }
}
