// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class AggregateValueCallTest
{
    public static TheoryData<string, string, string, string, string> Values => new()
    {
        { "Tuple", "(i32, i32)", "(20, 22)", "result.0 + result.1 == 42", string.Empty },
        { "OwnedTuple", "(string, i32)", "(\"text\", 42)", "result.1 == 42", "text=1" },
        { "Array", "[2 of i32]", "[20, 22]", "result[0] + result[1] == 42", string.Empty },
        { "EmptyArray", "[0 of string]", "[]", "result.length == 0", string.Empty },
        { "String", "string", "\"text\"", string.Empty, "text=1" },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void AggregateArgumentsAndResultsShareTheFunctionAbi(string name, string type, string value, string check, string destruction)
    {
        foreach (var erased in new[] { false, true })
        {
            // Check string contents through output without creating a second owned comparison literal.
            var print = name == "String" ? "Console.writeLine(result)\n" : name == "OwnedTuple" ? "Console.writeLine(result.0)\n" : string.Empty;
            var stdout = print.Length == 0 ? string.Empty : "text\n";
            var source = "let callback = func (value: " + type + ") -> " + type + " => value@move\n" +
                (erased ? "let f: (" + type + ") -> " + type + " = callback\n" : "let f = callback\n") +
                "let value: " + type + " = " + value + "\nlet result = f(value@move)\n" + print +
                (check.Length == 0 ? string.Empty : "require " + check + " else => $abort(\"result\")");
            var fixture = "AggregateValueCallRoundTrip" + name + erased;
            var ir = ScalarEmissionTest.EmitFixture(fixture, source, stdout);
            if (destruction.Length != 0)
            {
                StringEmissionTest.WriteAuditedFixture(fixture, source, ir, stdout, destruction);
            }
        }
    }

    [Fact]
    public void GenericForwardingRetainsAggregateAcquisition()
    {
        const string Source = "func apply<T>(f: (T) -> T, value: T) -> T => f(value@move)\n" +
            "let callback = func (value: (string, i32)) -> (string, i32) => value@move\n" +
            "let f: ((string, i32)) -> (string, i32) = callback\n" +
            "let result = apply(f@move, (\"generic\", 42))\nConsole.writeLine(result.0)\nrequire result.1 == 42 else => $abort(\"result\")";
        var ir = ScalarEmissionTest.EmitFixture("AggregateValueCallGeneric", Source, "generic\n");
        StringEmissionTest.WriteAuditedFixture("AggregateValueCallGeneric", Source, ir, "generic\n", "generic=1");
    }

    [Fact]
    public void ArgumentsKeepSourceEvaluationOrder()
    {
        const string Source = "func make() -> (i32, i32)\n    Console.writeLine(\"first\")\n    return (20, 22)\n" +
            "let callback = func (value: (i32, i32), gap: ()) -> i32 => value.0 + value.1\n" +
            "let f: ((i32, i32), ()) -> i32 = callback\n" +
            "require f(make(), Console.writeLine(\"second\")) == 42 else => $abort(\"order\")";
        ScalarEmissionTest.EmitFixture("AggregateValueCallOrder", Source, "first\nsecond\n");
    }

    [Fact]
    public void AbandonedArgumentsReleaseTheirAcquiredValues()
    {
        const string Source = "func run()\n" +
            "    let callback = func (value: (string, i32), gap: ()) => Console.writeLine(\"unreachable\")\n" +
            "    let f: ((string, i32), ()) -> () = callback\n" +
            "    f((\"abandoned\", 42), (return))\nrun()";
        var ir = ScalarEmissionTest.EmitFixture("AggregateValueCallAbandoned", Source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("AggregateValueCallAbandoned", Source, ir, string.Empty, "abandoned=1;unreachable=0");
    }

    [Theory]
    [InlineData("f(value)")]
    [InlineData("f(value@move)\nf(value@move)")]
    public void NonCopyArgumentsRequireAvailableOwnedAcquisition(string calls)
    {
        var c = MinimalEmissionTest.Analyze("let callback = func (value: (string, i32)) => ()\nlet f: ((string, i32)) -> () = callback\nlet value = (\"text\", 42)\n" + calls);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmAggregateCallsReusePhysicalSignatures()
    {
        const string Source = "let callback = func (value: (i32, i32)) -> (i32, i32) => value\nlet f: ((i32, i32)) -> (i32, i32) = callback\nf((20, 22))";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TupleArgumentsUseOrdinaryAcquisition(bool erased)
    {
        var source = "let callback = func (value: (i32, i32)) -> i32 => value.0 + value.1\n" +
            (erased ? "let f: ((i32, i32)) -> i32 = callback\n" : "let f = callback\n") +
            "let value = (20, 22)\nrequire f(value) == 42 and value.0 == 20 else => $abort(\"tuple\")";
        ScalarEmissionTest.EmitFixture("AggregateValueCallTuple" + erased, source, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyArgumentsHaveNoPhysicalSlot(bool erased)
    {
        var source = "let callback = func (empty: (), value: i32) -> i32 => value\n" +
            (erased ? "let f: ((), i32) -> i32 = callback\n" : "let f = callback\n") +
            "require f((), 42) == 42 else => $abort(\"unit\")";
        ScalarEmissionTest.EmitFixture("AggregateValueCallEmpty" + erased, source, string.Empty);
    }
}
