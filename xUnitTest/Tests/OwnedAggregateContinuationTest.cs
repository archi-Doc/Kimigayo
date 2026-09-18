// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class OwnedAggregateContinuationTest
{
    [Theory]
    [InlineData("(string, i32)", "(\"payload\", 2)", "(_, let n) if (if c => stop() else => false) => x = n\n                (let text, _) => Console.writeLine(text)")]
    [InlineData("E<(string, i32)>", ".Some((\"payload\", 2))", ".Some((_, let n)) if (return) => x = n\n                .Some((let text, _)) => Console.writeLine(text)\n                .None => exit")]
    public void OwnedStringLeavesKeepDecompositionAndTerminalHistories(string type, string value, string arms)
    {
        var c = MinimalEmissionTest.Analyze(Source(type, value, arms));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void AcquiringOwnedSubjectStillMovesTheOriginalAggregate()
    {
        var c = MinimalEmissionTest.Analyze(Source("E<(string, i32)>", ".Some((\"payload\", 2))", ".Some((let text, _)) => Console.writeLine(text)\n                .None => return", "let again = subject"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void GuardFailureAndSelectedDecompositionDestroyEachOwnedLeafOnce()
    {
        var source = Source("E<(string, i32)>", ".Some((\"payload\", 2))", ".Some((_, let n)) if (return) => x = n\n                .Some((let text, _)) => Console.writeLine(text)\n                .None => exit") +
            "\nfunc route(selected: bool)\n    let value: E<(string, i32)> = .Some((\"active\", 3))\n    match value\n        .Some((_, let n)) if selected and n > 0 => Console.writeLine(\"selected\")\n        .Some((let text, _)) => Console.writeLine(text)\n        .None => ()\nroute(true)\nroute(false)\n" +
            "func choose(consume: bool) -> string\n    return match (\"chosen\", \"remaining\")\n        (var text, _)\n            if consume => Console.writeLine(text)\n            text = \"replacement\"\n            yield text\nConsole.writeLine(choose(true))\nConsole.writeLine(choose(false))";
        const string Name = "OwnedAggregateWindowCleanup";
        const string Output = "selected\nactive\nchosen\nreplacement\nreplacement\n";
        var ir = ScalarEmissionTest.EmitFixture(Name, source, Output);
        StringEmissionTest.WriteAuditedFixture(Name, source, ir, Output, "payload=1;active=2;selected=1;chosen=2;remaining=2;replacement=2", order: [0, 2, 1, 1, 3, 4, 5, 3, 4, 5]);
    }

    [Fact]
    public void ReloadedOwnedPatternsReuseCheckingStorage()
    {
        var c = MinimalEmissionTest.Analyze(Source("E<(string, i32)>", ".Some((\"payload\", 2))", ".Some((_, let n)) if (if c => stop() else => false) => x = n\n                .Some((let text, _)) => Console.writeLine(text)\n                .None => exit"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var syntax = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref syntax);
        Assert.NotNull(syntax);
        syntax.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified)
            {
                throw new InvalidOperationException("Reloaded owned Pattern verification failed.");
            }
        }));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    private static string Source(string type, string value, string arms, string tail = "let y = x")
        => "enum E<T>\n    Some(T)\n    None\nfunc stop() -> Never => $abort(\"owned continuation\")\nfunc f(c: bool)\n    var x = 1\n    let subject: " + type + " = " + value +
            "\n    do\n        loop\n            if c => return else => exit\n            match subject\n                " + arms + "\n        stop()\n    " + tail + "\nf(true)";
}
