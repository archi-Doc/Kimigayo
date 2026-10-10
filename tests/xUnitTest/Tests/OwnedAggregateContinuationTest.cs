// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class OwnedAggregateContinuationTest
{
    [Fact]
    public void GuardFailureAndSelectedDecompositionDestroyEachOwnedLeafOnce()
    {
        var source = "enum E<T>\n    Some(T)\n    None\nfunc route(selected: bool)\n    let value: E<(string, i32)> = .Some((\"active\", 3))\n    match value@move\n        .Some((_, let n)) if selected and n > 0 => Console.writeLine(\"selected\")\n        .Some((let text, _)) => Console.writeLine(text)\n        .None => ()\nroute(true)\nroute(false)\n" +
            "func choose(consume: bool) -> string\n    return match (\"chosen\", \"remaining\")@move\n        (var text, _)\n            if consume => Console.writeLine(text)\n            text = \"replacement\"\n            yield text@move\nConsole.writeLine(choose(true))\nConsole.writeLine(choose(false))";
        const string Name = "OwnedAggregateWindowCleanup";
        const string Output = "selected\nactive\nchosen\nreplacement\nreplacement\n";
        var ir = ScalarEmissionTest.EmitFixture(Name, source, Output);
        StringEmissionTest.WriteAuditedFixture(Name, source, ir, Output, "active=2;selected=1;chosen=2;remaining=2;replacement=2", order: [1, 0, 0, 2, 3, 4, 2, 3, 4]);
    }

    [Theory]
    [InlineData("(let s, let n) if (return n) => Console.writeLine(\"selected\")")]
    [InlineData("(let s, let n) if (if c => return n else => false) => Console.writeLine(s)")]
    public void CompositeGuardRetainsCandidateTypesAcrossReturnContinuations(string arms)
    {
        // Guard and unselected body retain their own candidate/acquired Types even
        // when a terminal guard transfers directly to the enclosing function.
        var c = MinimalEmissionTest.Analyze("func f(c: bool) -> i32\n    let t = (\"a\", 4)\n    match t@move\n        " + arms + "\n        _ => ()\n    return 0\nf(true)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Empty(c.Binding.Issues);
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var error), error);
    }

    [Fact]
    public void BodyExitsAndDeliveredBindingsDestroyEachOwnedLeafOnce()
    {
        const string Source = """
            enum P
                Some(string, string)
                None
            func early(flag: bool) -> i32
                let value: P = .Some("first", "second")
                match value@move
                    .Some(let a, _)
                        if flag => return 1
                        Console.writeLine(a)
                    .None => ()
                return 0
            func exits()
                loop
                    let value: P = .Some("loopA", "loopB")
                    match value@move
                        .Some(_, let b)
                            Console.writeLine(b)
                            exit
                        .None => ()
            func deliver() -> string
                let value: P = .Some("kept", "dropped")
                return match value@move
                    .Some(let a, _) => a@move
                    .None => "none"
            func replace(flag: bool)
                let value = (("n1", "n2"), 5)
                match value@move
                    ((var x, _), let n)
                        if flag => x = "n3"
                        Console.writeLine(x)
            if early(true) != 1 => $abort("early")
            if early(false) != 0 => $abort("late")
            exits()
            Console.writeLine(deliver())
            replace(true)
            replace(false)
            """;
        const string Name = "OwnedAggregateBodyExits";
        const string Output = "first\nloopB\nkept\nn3\nn1\n";
        var ir = ScalarEmissionTest.EmitFixture(Name, Source, Output);
        StringEmissionTest.WriteAuditedFixture(Name, Source, ir, Output, "first=2;second=2;loopA=1;loopB=1;kept=1;dropped=1;none=0;n1=2;n2=2;n3=1");
    }
}
