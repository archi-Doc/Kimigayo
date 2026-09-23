// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class StaticElementUpdateTest
{
    private const string Resource = "func read(n: ref/i32) => ()\nstruct Resource\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit\n        match self.id\n            1 => Console.writeLine(\"first\")\n            2 => Console.writeLine(\"second\")\n            _ => Console.writeLine(\"new\")\n";

    [Fact]
    public void EmitsUnchangedMilestone17()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../milestones/Milestone17.kimi"));
        ScalarEmissionTest.EmitFixture("StaticElementUpdateMilestone17", source, "Exchange scope finished.\nResource 2 destroyed.\nResource 3 destroyed.\nPrepared result.\nResource 1 destroyed.\nResult received.\nResource 5 destroyed.\nResource 4 destroyed.\nCleanup finished.\n");
    }

    [Theory]
    [InlineData("Explicit", "a[0]@uniq", "a[1]@uniq")]
    [InlineData("Literal", "a[((0x0))]@uniq", "a[(0b1)]@uniq")]
    public void ExchangesDisjointElementsInPlace(string name, string first, string second)
        => ScalarEmissionTest.EmitFixture("StaticElementUpdate" + name, ExchangeSource(first, second), string.Empty);

    // SPEC 15.1.5: a directly owned element is never lent exclusively without @uniq.
    [Theory]
    [InlineData("a[0]", "a[1]")]
    public void BareElementsAreNotLentExclusively(string first, string second)
    {
        var c = MinimalEmissionTest.Analyze(ExchangeSource(first, second));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.ExclusiveBorrowRequired);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("Nested", "var a: [2 of ([2 of i32], bool)] = [([1, 2], true), ([3, 4], false)]\nKimi.Intrinsics.swap((a[0].0)[1]@uniq, a[1].0[0]@uniq)\nrequire a[0].0[1] == 3 and a[1].0[0] == 2 else => $abort(\"value\")")]
    [InlineData("Retained", "var a: [2 of i32] = [1, 2]\nlet left = a[0]@uniq\nlet right = a[1]@uniq\nKimi.Intrinsics.swap(left, right)\nrequire a[0] == 2 and a[1] == 1 else => $abort(\"value\")")]
    [InlineData("Call", "func change(value: uniq/i32) => Kimi.Intrinsics.replace(value, with: 42)\nvar a: [2 of i32] = [1, 2]\nchange(a[1]@uniq)\nrequire a[0] == 1 and a[1] == 42 else => $abort(\"value\")")]
    [InlineData("FieldArray", "struct Box\n    public var data: [2 of i32] = [1, 2]\nvar box = Box.init()\nKimi.Intrinsics.swap(box.data[0]@uniq, box.data[1]@uniq)\nrequire box.data[0] == 2 and box.data[1] == 1 else => $abort(\"value\")")]
    [InlineData("MixedRoot", "var a: [2 of i32] = [1, 2]\nvar b: i32 = 3\nKimi.Intrinsics.swap(a[0]@uniq, b@uniq)\nrequire a[0] == 3 and b == 1 else => $abort(\"value\")")]
    public void PreservesStaticPathsAcrossStorageAndCalls(string name, string source)
        => ScalarEmissionTest.EmitFixture("StaticElementUpdate" + name, source, string.Empty);

    [Theory]
    [InlineData("Remaining", "func work()\n    var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\n    let taken = a[0]@move\n    Kimi.Intrinsics.replace(a[1]@uniq, with: Resource.init(3))\n    defer => Console.writeLine(\"defer\")\nwork()", "second\ndefer\nfirst\nnew\n")]
    [InlineData("EarlyReturn", "func work() -> Resource\n    var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\n    defer => Console.writeLine(\"defer\")\n    return Kimi.Intrinsics.exchange(a[0]@uniq, with: Resource.init(3))\nlet result = work()\nConsole.writeLine(\"received\")", "defer\nsecond\nnew\nreceived\nfirst\n")]
    [InlineData("SiblingMove", "var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\nlet held = a[1]@ref\nlet taken = a[0]@move\nrequire held.id == 2 else => $abort(\"value\")\nConsole.writeLine(\"observed\")", "observed\nfirst\nsecond\n")]
    [InlineData("SharedRead", "let a: [2 of i32] = [1, 2]\nlet value = a[0]@ref\nread(value)", "")]
    public void PreservesOwnershipAndCleanup(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("StaticElementUpdate" + name, Resource + source, stdout);

    [Fact]
    public void AbortingReplacementDestroysOldValueBeforePlacement()
    {
        const string Source = "struct Resource\n    public let id: i32\n    public init(id: i32)\n        self.id = id\n        Console.writeLine(\"constructed\")\n    deinit\n        if self.id == 1\n            Console.writeLine(\"destroying old\")\n            $abort(\"drop\")\n        Console.writeLine(\"bad new cleanup\")\nvar a: [1 of Resource] = [Resource.init(1)]\ndefer => Console.writeLine(\"bad defer\")\nKimi.Intrinsics.replace(a[0]@uniq, with: Resource.init(2))\nConsole.writeLine(\"bad continuation\")";
        ScalarEmissionTest.EmitFixture("StaticElementUpdateDestructorAbort", Source, "constructed\nconstructed\ndestroying old\n", 1, "Hello.kimi:9:13: abort KIMI_E_ABORT: drop\n");
    }

    [Theory]
    [InlineData("var a: [2 of i32] = [1, 2]\nKimi.Intrinsics.swap(a[0]@uniq, a[((0x0))]@uniq)")]
    [InlineData("var a: [2 of i32] = [1, 2]\nlet held = a[0]@ref\nKimi.Intrinsics.replace(a[0]@uniq, with: 3)\nread(held)")]
    [InlineData("var a: [2 of i32] = [1, 2]\nlet held = a[1]@ref\nKimi.Intrinsics.swap(a[0]@uniq, a[1]@uniq)\nread(held)")]
    [InlineData("var a: [2 of i32] = [1, 2]\nlet held = a@ref\nKimi.Intrinsics.replace(a[0]@uniq, with: 3)\nlet n = held[0]")]
    [InlineData("var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\nlet taken = a[0]@move\nKimi.Intrinsics.replace(a[0]@uniq, with: Resource.init(3))")]
    [InlineData("var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\nKimi.Intrinsics.exchange(a[0]@uniq, with: a[0]@move)")]
    [InlineData("var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\nlet taken = a[0]@move\nlet borrowed = a@ref")]
    [InlineData("var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\nlet taken = a[0]\nlet borrowed = a@ref")]
    [InlineData("let a: [2 of i32] = [1, 2]\nKimi.Intrinsics.replace(a[0]@uniq, with: 3)")]
    [InlineData("let a: [2 of i32] = [1, 2]\nKimi.Intrinsics.replace(a[0], with: 3)")]
    [InlineData("let a: [2 of i32] = [1, 2]\nKimi.Intrinsics.replace(a[0]@uniq/i32, with: 3)")]
    [InlineData("struct Holder\n    public var items: [2 of Resource] = [Resource.init(1), Resource.init(2)]\n    deinit => Console.writeLine(\"holder\")\nvar owner = Holder.init()\nlet taken = owner.items[0]@move")]
    [InlineData("var a: [2 of i32]\nKimi.Intrinsics.replace(a[0]@uniq, with: 3)")]
    [InlineData("var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\nlet all = a@move\nKimi.Intrinsics.replace(a[0]@uniq, with: Resource.init(3))")]
    [InlineData("var a: [2 of Resource] = [Resource.init(1), Resource.init(2)]\nlet borrowed = a[0]@uniq\nlet taken = *borrowed")]
    [InlineData("var a: [2 of i32] = [1, 2]\nlet i: isize = 0\nKimi.Intrinsics.swap(a[i]@uniq, a[1]@uniq)")]
    [InlineData("var a: [2 of i32] = [1, 2]\nKimi.Intrinsics.swap(a[0 + 0]@uniq, a[1]@uniq)")]
    public void RejectsInvalidStorageAndConflicts(string source)
    {
        var c = MinimalEmissionTest.Analyze(Resource + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, source);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RebindingAndReloadPreserveStaticAddresses()
    {
        var c = MinimalEmissionTest.Analyze("var a: [2 of i32] = [1, 2]\nKimi.Intrinsics.swap(a[0]@uniq, a[1]@uniq)");
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

    private static string ExchangeSource(string first, string second)
        => "var a: [2 of i32] = [10, 20]\nlet old = Kimi.Intrinsics.exchange(" + first + ", with: a[0] + 1)\nKimi.Intrinsics.swap(" + first + ", " + second + ")\nKimi.Intrinsics.replace(" + second + ", with: 42)\nrequire old == 10 and a[0] == 20 and a[1] == 42 else => $abort(\"value\")";
}
