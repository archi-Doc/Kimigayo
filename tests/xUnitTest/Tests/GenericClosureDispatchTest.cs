// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

public class GenericClosureDispatchTest
{
    private const string Count = "func count<T>(value: T, depth: i32) -> i32\n    let action = func [value@move, depth] () -> i32 => if depth == 0 => 1 else => count(value@move, depth - 1) + 1\n    return action@move()\n";

    [Fact]
    public void CapturedGenericCallsSelectTheConcreteSpecialization()
    {
        const string Source = "func classify<T>(value: ref/T) -> i32 => 1\nspecialize func classify<i32>(value: ref/i32) -> i32 => 2\nfunc inspect<T>(value: T) -> i32\n    let action = func [value@move] () -> i32 => classify(value@ref)\n    return action()\nrequire inspect(7) == 2 and inspect(true) == 1 else => $abort(\"specialization\")";
        ScalarEmissionTest.EmitFixture("GenericClosureDispatchSpecialization", Source, string.Empty);
    }

    // DECISIONS R5-M34: a closure in a specialization of a generic struct's Type function is generic in the struct's slot (SPEC 6.1.3), while
    // the old generator keeps generating it once, as before, until G7.
    [Fact]
    public void TheOldGeneratorKeepsAClosureInAContainerSpecializationClosed()
    {
        const string Source = "struct Box<T>\n    T is Copy\n    public var value: T\n    public init(value: T) => self.value = value\n" +
            "    public func f<U>(x: U) -> i32 => 1\n    specialize func f<i32>(x: i32) -> i32\n        let g = func [] (y: i32) -> i32 => y\n" +
            "        return g(2)\nrequire Box<i32>.f(5) == 2 and Box<bool>.f(5) == 2 and Box<i32>.f(true) == 1 else => $abort(\"spec3\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var closure = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Kind == MirBodyKind.Closure && x.Source.Module == 0).Key.Decl!;
        Assert.True(c.Types.Instantiates(closure));
        Assert.True(c.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
    }

    [Fact]
    public void RecursiveClosureCallsReuseTheContextAndDestroyTheFinalValueOnce()
    {
        const string Source = "struct Token\n    drop => Console.writeLine(\"drop\")\n" + Count + "require count(Token.init(), 4) == 5 else => $abort(\"recursion\")\nrequire count(true, 2) == 3 else => $abort(\"second\")\nConsole.writeLine(\"done\")";
        ScalarEmissionTest.EmitFixture("GenericClosureDispatchRecursive", Source, "drop\ndone\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalCaptureMovesKeepExactlyOneOwner(bool move)
    {
        const string Source = "struct Item\n    drop => Console.writeLine(\"drop\")\nfunc consume(value: Item) => ()\nlet value = Item.init()\nlet action = func [value@move] (flag: bool) -> ()\n    if flag => consume(value@move)\naction@move(FLAG)\nConsole.writeLine(\"done\")";
        ScalarEmissionTest.EmitFixture("GenericClosureDispatchConditional" + move, Source.Replace("FLAG", move ? "true" : "false", StringComparison.Ordinal), "drop\ndone\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRecursiveGenericClosurePlansAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Count + "require count(\"owned\", 4) == 5 else => $abort(\"warm\")");
        Assert.True(c.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
