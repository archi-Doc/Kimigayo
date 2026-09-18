// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConcreteClosureTest
{
    [Theory]
    [InlineData("let x: i32 = 4\nlet f = func [x] (y: i32) => x + y\nf(2)", SemanticsKind.Ref)]
    [InlineData("let x: i32 = 4\nvar f = func [var x] () -> i32\n    x += 1\n    return x\nf()", SemanticsKind.Uniq)]
    [InlineData("let text = \"owned\"\nlet f = func [text] () => Console.writeLine(text)\nf()", SemanticsKind.Owner)]
    public void InfersConcreteEnvironmentAndReceiver(string source, SemanticsKind receiver)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var closure = Assert.Single(c.Ownership.Bodies, x => x.Function.IsAnonymous).Function.BoundClosure!;
        Assert.Equal(BoundTypeKind.Closure, closure.EnvironmentType!.Kind);
        Assert.Equal(receiver, closure.Receiver);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var failure), MinimalEmissionTest.Describe(c, failure));
    }

    [Fact]
    public void BindsExclusiveCallableContract()
    {
        var c = MinimalEmissionTest.Analyze("func apply<F>(f: uniq/F) -> i32\n    F is Callable<uniq, () -> i32>\n    return f()\nlet n: i32 = 0\nvar f = func [var n] () -> i32\n    n += 1\n    return n\napply(f@uniq)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("Snapshot", "let n: i32 = 2\nvar f = func [var n] () => ++n\nrequire f() == 3 and f() == 4 and n == 2 else => $abort(\"snapshot\")", "")]
    [InlineData("Nested", "let n: i32 = 9\nlet f = func [n] () => func [n] (x: i32) => n + x\nlet g = f()\nlet h: (i32) -> i32 = g\nrequire h(1) == 10 and g(2) == 11 else => $abort(\"nested\")", "")]
    [InlineData("Consume", "let text = \"owned\"\nlet f = func [text] () => Console.writeLine(text)\nf()", "owned\n")]
    [InlineData("UnusedOwned", "let text = \"unused\"\nlet f = func [text] () => ()\nf()\nf()", "")]
    [InlineData("Empty", "let f = func () => 17\nlet g: () -> i32 = f\nrequire f() == 17 and g() == 17 else => $abort(\"empty\")", "")]
    [InlineData("Borrowed", "let n: i32 = 0\nvar f = func [var n] () => ++n\nlet borrowed = f@uniq\nrequire borrowed() == 1 and borrowed() == 2 else => $abort(\"borrow\")", "")]
    [InlineData("InspectOwned", "let text = \"owned\"\nlet f = func [text] () => text == \"owned\"\nrequire f() and f() else => $abort(\"shared\")", "")]
    [InlineData("Order", "let a = \"first\"\nlet b = \"second\"\nlet f = func [a, b] () -> ()\n    defer => Console.writeLine(\"defer\")\n    Console.writeLine(a)\n    Console.writeLine(b)\nf()", "first\nsecond\ndefer\n")]
    public void EmitsConcreteClosures(string name, string source, string output)
        => ScalarEmissionTest.EmitFixture("ConcreteClosure" + name, source, output);

    [Theory]
    [InlineData("let text = \"owned\"\nlet f = func [text] () => Console.writeLine(text)\nf()\nf()")]
    [InlineData("let text = \"owned\"\nlet f = func [text] () => ()\nConsole.writeLine(text)")]
    [InlineData("let n: i32 = 0\nlet f = func [var n] () => ++n\nf()")]
    [InlineData("let text = \"owned\"\nlet f = func () => Console.writeLine(text)")]
    [InlineData("let n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nf(f(1))")]
    [InlineData("let n: i32 = 0\nlet f = func [var n] () => (n) = 1\nf()")]
    [InlineData("let n: i32 = 0\nlet f = func [var n] () => ++n\n(f)()")]
    [InlineData("let text = \"owned\"\nlet f = func [text] () => Console.writeLine(text)\nlet borrowed = f@ref\nborrowed()")]
    public void RejectsInvalidAcquisition(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("let x = 0\nlet f = func [var x] () -> i32\n    x += 1\n    return x\nf()")]
    [InlineData("var x = 0\nvar f = func [x] () => x = 1")]
    [InlineData("let x = 0\nlet f = func [x, x] () => x")]
    [InlineData("let x = 0\nlet f = func [] () => x")]
    public void RejectsInvalidConcreteBinding(string source)
        => Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);
}
