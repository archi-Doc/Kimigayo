// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ScalarDefaultEmissionTest
{
    [Theory]
    [InlineData("Tuple", "func f(x: (i32, i32), y?: i32 = x.0 + x.1) -> i32 => y\nif f((2, 3)) == 5 => Console.writeLine(\"ok\")")]
    [InlineData("Array", "func f(x: [2 of i32], y?: i32 = x[1]) -> i32 => y\nif f([2, 3]) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("Dynamic", "func f(x: [2 of i32], i: isize, y?: i32 = x[i]) -> i32 => y\nif f([2, 3], 1) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("Nested", "func f(x: ((i32, i32), i32), y?: i32 = x.0.1 + x.1) -> i32 => y\nif f(((2, 3), 4)) == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Repeated", "func f(x: [2 of i32], i: isize, y?: i32 = x[i]) -> i32 => y\nvar n: isize = 0\nvar sum = 0\nwhile n < 2\n    sum += f([2, 3], n)\n    n += 1\nif sum == 5 => Console.writeLine(\"ok\")")]
    [InlineData("Field", "struct S\n    public let value: i32\n    public init(value: i32) => self.value = value\nfunc f(x: S, y?: i32 = x.value) -> i32 => y\nif f(S.init(7)) == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Unit", "func f(x: ((), i32), y?: () = x.0) -> i32 => x.1\nif f(((), 7)) == 7 => Console.writeLine(\"ok\")")]
    public void DefaultsCopyScalarPreparedSubplaces(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + "Element" + name, source, "ok\n", 0);

    [Fact]
    public void PreparedOwnerIsDestroyedByTheCalleeAfterDefaultInspection()
        => ScalarEmissionTest.EmitFixture(Prefix + "ElementDrop", "struct S\n    public let value: i32\n    public init(value: i32) => self.value = value\n    deinit => Console.writeLine(\"drop\")\nfunc f(x: S, y?: i32 = x.value) -> i32 => y\nif f(S.init(7)) == 7 => Console.writeLine(\"ok\")", "drop\nok\n", 0);

    [Fact]
    public void PreparedDefaultIndexBoundsAbortAtTheDeclaration()
    {
        const string Source = "func f(x: [2 of i32], i: isize, y?: i32 = x[i]) => Console.writeLine(\"bad\")\nf([1, 2], 2)";
        var column = Source.IndexOf("x[i]", StringComparison.Ordinal) + 1;
        ScalarEmissionTest.EmitFixture(Prefix + "ElementBounds", Source, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    [Fact]
    public void PreparedElementReceiverCannotAliasAnotherArgument()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: (i32, i32), other: (i32, i32), y?: i32 = x.0) -> i32 => y\nf((1, 2), (3, 4))");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.IsGenerated);
        var source = Assert.Single(body.Operations, x => x.Kind == OwnershipOperationKind.LocateReceiver).Source;
        var original = source.BoundSymbol!;
        source.BoundSymbol = c.Binding.ParameterSymbol((FunctionKoto)original.Scope.Owner, 1);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        source.BoundSymbol = original;
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void WarmPreparedElementReadsReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: [2 of i32], i: isize, y?: i32 = x[i]) -> i32 => y\nf([1, 2], 0)\nf([3, 4], 1)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Prepared element default emission failed.");
            }
        }));
    }

    [Theory]
    [InlineData("Literal", "func f(x?: () = ()) => Console.writeLine(\"ok\")\nf()")]
    [InlineData("Chain", "func f(x?: () = (), y?: () = x) => Console.writeLine(\"ok\")\nf()")]
    [InlineData("Select", "func f(x: bool, y?: () = (if x => () else => ())) => Console.writeLine(\"ok\")\nf(true)")]
    [InlineData("IfNoElse", "func f(x: bool, y?: () = (if x => ())) => Console.writeLine(\"ok\")\nf(false)")]
    [InlineData("Do", "func f(x?: () = (scope: do => exit to scope)) => Console.writeLine(\"ok\")\nf()")]
    [InlineData("Loop", "func f(x?: () = (loop => exit)) => Console.writeLine(\"ok\")\nf()")]
    [InlineData("Local", "func f(x?: () = (scope: do\n    let n = ()\n    exit to scope: n\n)) => Console.writeLine(\"ok\")\nf()")]
    [InlineData("BeforeScalar", "func f(x?: () = (), y?: i32 = 3) -> i32 => y\nif f() == 3 => Console.writeLine(\"ok\")")]
    [InlineData("Supplied", "func f(x?: () = (loop => continue)) => Console.writeLine(\"ok\")\nf(())")]
    public void UnitDefaultsUseTheExistingZeroSizedArgumentPath(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + "Unit" + name, source, "ok\n", 0);

    [Fact]
    public void NoncompletingUnitDefaultSkipsTheCallee()
        => ScalarEmissionTest.EmitFixture(Prefix + "UnitNever", "func f(x?: () = (loop => continue)) => Console.writeLine(\"bad\")\nConsole.writeLine(\"begin\")\nf()", "begin\n", 0, timeoutMilliseconds: 250);

    [Fact]
    public void WarmUnitDefaultPlansReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: bool, first?: () = (if x => ()), second?: () = first, y?: i32 = 3) -> i32 => y\nf(true)\nf(false)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Unit default emission failed.");
            }
        }));
    }

    public static TheoryData<string, string> Fixtures => new()
    {
        { "Chained", "func f(x: i32, y?: i32 = x + 1, z?: i32 = y * 2) -> i32 => x + y + z\nif f(2) == 11 => Console.writeLine(\"ok\")" },
        { "Named", "func f(x: i32, y?: i32 = x + 1, z?: i32 = y * 2) -> i32 => x + y + z\nif f(z: 9, x: 2) == 14 => Console.writeLine(\"ok\")" },
        { "Snapshot", "func f(x: i32, touch: (), y?: i32 = x) -> i32 => y\nvar value = 7\nlet result = f(value, value = 99)\nif result == 7 and value == 99 => Console.writeLine(\"ok\")" },
        { "Repeated", "func f(x: i32, y?: i32 = x + 1) -> i32 => y\nvar n = 0\nvar sum = 0\nwhile n < 3\n    sum += f(n)\n    n += 1\nif sum == 6 and f(9) == 10 => Console.writeLine(\"ok\")" },
        { "Float", "func f(x: f64, y?: f64 = x + 0.5) -> f64 => y\nif f(1.0) == 1.5 => Console.writeLine(\"ok\")" },
        { "Short", "func f(x: i32, y?: bool = false and x + 1 > 0) -> bool => y\nif not f(2147483647) => Console.writeLine(\"ok\")" },
        { "Supplied", "func f(x: i32, y?: i32 = x + 1) -> i32 => y\nif f(2147483647, 7) == 7 => Console.writeLine(\"ok\")" },
        { "Required", "func f(x: i32, y: i32 = x + 1) -> i32 => y\nif f(2147483647, 7) == 7 => Console.writeLine(\"ok\")" },
        { "Nested", "func f(x: i32, y?: i32 = x + 1) -> i32 => y\nif f(f(1)) == 3 => Console.writeLine(\"ok\")" },
        { "Branches", "func f(x: i32, y?: bool = (x > 0 and x < 3) or x == 4, z?: bool = y and x > 0) -> bool => z\nif f(2) and f(4) and not f(3) => Console.writeLine(\"ok\")" },
        { "Receiver", "struct S\n    public func f(x: i32, self, y?: i32 = x + 1) -> i32 => y\nlet s = S.init()\nif s.f(2) == 3 and S.f(4, s) == 5 => Console.writeLine(\"ok\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EmitsScalarDefaultsThroughPreparedSlots(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + name, source, "ok\n", 0);

    [Theory]
    [InlineData("Widen", "func f(x: i32, y?: i64 = x@i64) -> i64 => y\nif f(2147483647) == 2147483647 => Console.writeLine(\"ok\")")]
    [InlineData("Narrow", "func f(x: i32, y?: u8 = x@u8) -> u8 => y\nif f(255) == 255 => Console.writeLine(\"ok\")")]
    [InlineData("Identity", "func f(x: i32, y?: i32 = x@i32) -> i32 => y\nif f(7) == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Literal", "func f(x?: f64 = 5000000000@f64) -> f64 => x\nif f() == 5000000000.0 => Console.writeLine(\"ok\")")]
    [InlineData("Truncate", "func f(x: f64, y?: i32 = x@i32) -> i32 => y\nif f(-3.9) == -3 => Console.writeLine(\"ok\")")]
    [InlineData("Round", "func f(x: i64, y?: f32 = x@f32, z?: i64 = y@i64) -> i64 => z\nif f(16777217) == 16777216 => Console.writeLine(\"ok\")")]
    [InlineData("Zero", "func f(x: f64, y?: f32 = x@f32) -> f32 => y\nif 1.0 / f(-0.0) < 0.0 => Console.writeLine(\"ok\")")]
    [InlineData("Supplied", "func f(x: i32, y?: u8 = x@u8) -> u8 => y\nif f(300, 7) == 7 => Console.writeLine(\"ok\")")]
    public void DefaultsUseEstablishedScalarConversionPlans(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + "Convert" + name, source, "ok\n", 0);

    [Theory]
    [InlineData("Branches", "func f(x: i32, y?: i32 = (if x < 0 => -x else => x)) -> i32 => y\nif f(-3) == 3 and f(4) == 4 => Console.writeLine(\"ok\")")]
    [InlineData("Skip", "func f(x: i32, y?: i32 = (if x == 2147483647 => 7 else => x + 1)) -> i32 => y\nif f(2147483647) == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Nested", "func f(x: i32, y?: i32 = (if x == 0 => 1 else => (if x == 1 => 2 else => 3)), z?: i32 = (if y > 1 => y * 2 else => 0)) -> i32 => z\nif f(0) == 0 and f(1) == 4 and f(2) == 6 => Console.writeLine(\"ok\")")]
    [InlineData("Repeat", "func f(x: i32, y?: i32 = (if x < 2 => x else => 2)) -> i32 => y\nvar n = 0\nvar sum = 0\nwhile n < 4\n    sum += f(n)\n    n += 1\nif sum == 5 => Console.writeLine(\"ok\")")]
    [InlineData("Convert", "func f(x: i32, y?: u8 = (if x < 0 => 0@u8 else => x@u8)) -> u8 => y\nif f(-1) == 0 and f(255) == 255 => Console.writeLine(\"ok\")")]
    public void DefaultSelectionsUsePreparedSlotsAndResultJoins(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + "Select" + name, source, "ok\n", 0);

    [Theory]
    [InlineData("Simple", "func f(x: i32, y?: i32 = (do => x + 1)) -> i32 => y\nif f(2) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("Nested", "func f(x: i32, y?: i32 = (do => do => x * 2)) -> i32 => y\nif f(2) == 4 and f(3) == 6 => Console.writeLine(\"ok\")")]
    [InlineData("Chained", "func f(x: i32, y?: i32 = (do => x + 1), z?: i32 = (do => y * 2)) -> i32 => z\nif f(2) == 6 => Console.writeLine(\"ok\")")]
    [InlineData("Select", "func f(x: i32, y?: u8 = (do => if x < 0 => 0@u8 else => x@u8)) -> u8 => y\nif f(-1) == 0 and f(255) == 255 => Console.writeLine(\"ok\")")]
    [InlineData("Snapshot", "func f(x: i32, touch: (), y?: i32 = (do => x)) -> i32 => y\nvar n = 3\nif f(n, n = 7) == 3 and n == 7 => Console.writeLine(\"ok\")")]
    public void DefaultDoBodiesRetainPreparedValues(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + "Do" + name, source, "ok\n", 0);

    [Theory]
    [InlineData("Loop", "func f(x: i32, y?: i32 = (loop => exit x + 1)) -> i32 => y\nif f(2) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("Choice", "func f(x: i32, y?: i32 = (loop => if x < 0 => exit -x else => exit x)) -> i32 => y\nif f(-3) == 3 and f(4) == 4 => Console.writeLine(\"ok\")")]
    [InlineData("DoExit", "func f(x: i32, y?: i32 = (scope: do => exit to scope: x + 1)) -> i32 => y\nif f(2) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("SelectYield", "func f(x: i32, y?: i32 = (scope: if x > 0 => yield to scope: x + 1 else => 0)) -> i32 => y\nif f(2) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("Nested", "func f(x: i32, y?: i32 = (outer: loop => loop => exit to outer: x + 1)) -> i32 => y\nif f(2) == 3 => Console.writeLine(\"ok\")")]
    [InlineData("Chain", "func f(x: i32, y?: i32 = (loop => exit x + 1), z?: i32 = (loop => exit y * 2)) -> i32 => z\nif f(2) == 6 => Console.writeLine(\"ok\")")]
    [InlineData("Repeat", "func f(x: i32, y?: i32 = (loop => exit x + 1)) -> i32 => y\nvar n = 0\nvar sum = 0\nwhile n < 3\n    sum += f(n)\n    n += 1\nif sum == 6 => Console.writeLine(\"ok\")")]
    [InlineData("Snapshot", "func f(x: i32, touch: (), y?: i32 = (loop => exit x)) -> i32 => y\nvar n = 3\nif f(n, n = 7) == 3 and n == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Supplied", "func f(x: i32, y?: i32 = (loop => exit x + 1)) -> i32 => y\nif f(2147483647, 7) == 7 => Console.writeLine(\"ok\")")]
    public void DefaultTransfersStayInsideTheirEvaluation(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + "Transfer" + name, source, "ok\n", 0);

    [Fact]
    public void NoncompletingDefaultSkipsLaterDefaultsAndTheCallee()
        => ScalarEmissionTest.EmitFixture(Prefix + "TransferNever", "func f(x?: i32 = (loop => continue), y?: i32 = 2147483647 + 1) => Console.writeLine(\"bad\")\nConsole.writeLine(\"begin\")\nf()", "begin\n", 0, timeoutMilliseconds: 250);

    [Fact]
    public void DefaultTransferOperandOverflowAbortsAtItsDeclaration()
    {
        const string Source = "func f(x: i32, y?: i32 = (loop => exit x + 1)) => Console.writeLine(\"bad\")\nf(2147483647)";
        var column = Source.IndexOf("x + 1", StringComparison.Ordinal) + 1;
        ScalarEmissionTest.EmitFixture(Prefix + "TransferAbort", Source, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Theory]
    [InlineData("Simple", "func f(x: i32, y?: i32 = (scope: do\n    let n = x + 1\n    exit to scope: n * 2\n)) -> i32 => y\nif f(2) == 6 => Console.writeLine(\"ok\")")]
    [InlineData("Snapshot", "func f(x: i32, touch: (), y?: i32 = (scope: do\n    let n = x\n    exit to scope: n\n)) -> i32 => y\nvar n = 3\nif f(n, n = 7) == 3 and n == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Repeated", "func f(x: i32, y?: i32 = (scope: do\n    let n = x + 1\n    exit to scope: n + n\n)) -> i32 => y\nvar n = 0\nvar sum = 0\nwhile n < 3\n    sum += f(n)\n    n += 1\nif sum == 12 and f(5) == 12 => Console.writeLine(\"ok\")")]
    [InlineData("Branches", "func f(x: i32, y?: i32 = (if x < 0\n    let n = -x\n    yield n\nelse\n    let n = x + 1\n    yield n\n)) -> i32 => y\nif f(-3) == 3 and f(3) == 4 => Console.writeLine(\"ok\")")]
    [InlineData("Nested", "func f(x: i32, y?: i32 = (outer: do\n    let n = x + 1\n    let m = (inner: do\n        let n = 10\n        exit to inner: n\n    )\n    exit to outer: n + m\n)) -> i32 => y\nif f(2) == 13 => Console.writeLine(\"ok\")")]
    [InlineData("Chained", "func f(x: i32, y?: i32 = (loop\n    let n = x + 1\n    exit n\n), z?: i32 = (loop\n    let n = y * 2\n    exit n\n)) -> i32 => z\nif f(2) == 6 and f(5) == 12 => Console.writeLine(\"ok\")")]
    [InlineData("Conversion", "func f(x: i32, y?: u8 = (scope: do\n    let wide = x@i64\n    let n = wide@u8\n    exit to scope: n\n)) -> u8 => y\nif f(255) == 255 => Console.writeLine(\"ok\")")]
    public void DefaultLocalCopiesHaveIndependentStorage(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + "Local" + name, source, "ok\n", 0);

    [Theory]
    [InlineData("Assign", "func f(x: i32, y?: i32 = (scope: do\n    var n = x\n    n = n + 1\n    n *= 2\n    exit to scope: n\n)) -> i32 => y\nif f(2) == 6 => Console.writeLine(\"ok\")")]
    [InlineData("Loop", "func f(x: i32, y?: i32 = (scope: do\n    var n = 0\n    loop\n        n += 1\n        if n >= x => exit\n    exit to scope: n\n)) -> i32 => y\nif f(3) == 3 and f(5) == 5 => Console.writeLine(\"ok\")")]
    [InlineData("While", "func f(x: i32, y?: i32 = (scope: do\n    var n = 0\n    var sum = 0\n    while n < x\n        n += 1\n        sum += n\n    exit to scope: sum\n)) -> i32 => y\nif f(3) == 6 and f(0) == 0 => Console.writeLine(\"ok\")")]
    [InlineData("Increment", "func f(x: i32, y?: i32 = (scope: do\n    var n = x\n    let old = n++\n    let next = ++n\n    exit to scope: old + next + n\n)) -> i32 => y\nif f(2) == 10 => Console.writeLine(\"ok\")")]
    [InlineData("Float", "func f(x: f64, y?: f64 = (scope: do\n    var n = x\n    n += 0.5\n    exit to scope: n\n)) -> f64 => y\nif f(2.0) == 2.5 => Console.writeLine(\"ok\")")]
    [InlineData("Bits", "func f(x: i32, y?: i32 = (scope: do\n    var n = x\n    n <<= 2\n    n |= 1\n    exit to scope: n\n)) -> i32 => y\nif f(2) == 9 => Console.writeLine(\"ok\")")]
    [InlineData("Snapshot", "func f(x: i32, touch: (), y?: i32 = (scope: do\n    var n = x\n    n *= 2\n    exit to scope: n\n)) -> i32 => y\nvar n = 3\nif f(n, n = 7) == 6 and n == 7 => Console.writeLine(\"ok\")")]
    [InlineData("Supplied", "func f(x: i32, y?: i32 = (scope: do\n    var n = x\n    n += 1\n    exit to scope: n\n)) -> i32 => y\nif f(2147483647, 7) == 7 => Console.writeLine(\"ok\")")]
    public void DefaultMutationIsConfinedToItsInitializedLocals(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + "Mutable" + name, source, "ok\n", 0);

    [Fact]
    public void DefaultLocalUpdateAbortKeepsItsDeclarationLocation()
    {
        const string Source = "func f(x: i32, y?: i32 = (scope: do\n    var n = x\n    n += 1\n    exit to scope: n\n)) => Console.writeLine(\"bad\")\nf(2147483647)";
        ScalarEmissionTest.EmitFixture(Prefix + "MutableAbort", Source, string.Empty, 1, "Hello.kimi:3:5: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Theory]
    [InlineData("x = 3")]
    [InlineData("x += 1")]
    [InlineData("++x")]
    public void DefaultsCannotMutatePreparedParametersEvenWhenSupplied(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y?: i32 = (scope: do\n    " + mutation + "\n    exit to scope: x\n)) => ()\nf(1, 2)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void DefaultLocalInitializerAbortSkipsRemainingPreparation()
    {
        const string Source = "func f(x: i32, y?: i32 = (scope: do\n    let n = x + 1\n    exit to scope: n\n)) => Console.writeLine(\"bad\")\nf(2147483647)";
        ScalarEmissionTest.EmitFixture(Prefix + "LocalAbort", Source, string.Empty, 1, "Hello.kimi:2:13: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Theory]
    [InlineData("f()")]
    [InlineData("f(3)")]
    public void NoncompletingLocalInitializerReportsUninitializedUse(string call)
    {
        var c = MinimalEmissionTest.Analyze("func f(y?: i32 = (scope: do\n    let n: i32 = (loop => continue)\n    exit to scope: n + 1\n)) => ()\n" + call);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("func f(x?: i32 = (scope: do\n    let n = \"a\"\n    exit to scope: 1\n)) => ()\nf(3)")]
    [InlineData("func helper() -> i32 => 1\nfunc f(x?: i32 = (scope: do\n    let n = helper()\n    exit to scope: n\n)) => ()\nf(3)")]
    public void SuppliedDefaultsStillRejectUnsupportedLocalEffects(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void UnexecutedDefaultArmStillRequiresSupportedEffects()
    {
        var c = MinimalEmissionTest.Analyze("func helper() -> i32 => 1\nfunc f(x?: i32 = (if true => 1 else => helper())) => ()\nf(3)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void RebindingAndReloadRebuildExecutableDefaultPlans()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y?: i32 = (scope: do\n    var n = (loop => exit (do => if x > 0 => (x@i64 + 1)@i32 else => 0))\n    n += 0\n    exit to scope: n\n)) -> i32 => y\nif f(2) == 3 => Console.writeLine(\"ok\")");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        Assert.True(c.Bind().IsComplete);
        Assert.False(c.Emission.Validate(out _));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        using var rebound = new StringWriter();
        Assert.True(c.Emission.WriteIr(rebound, out error), error);
        Assert.Equal(original.ToString(), rebound.ToString());

        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.False(restored.Emission.Validate(out _));
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        using var reloaded = new StringWriter();
        Assert.True(restored.Emission.WriteIr(reloaded, out error), error);
        Assert.Equal(original.ToString(), reloaded.ToString());
    }

    [Fact]
    public void DefaultNarrowingAbortsAtTheDeclarationExpression()
    {
        const string Source = "func f(x: i32, y?: u8 = x@u8) => Console.writeLine(\"bad\")\nf(300)";
        var column = Source.IndexOf("x@u8", StringComparison.Ordinal) + 1;
        ScalarEmissionTest.EmitFixture(Prefix + "ConvertAbort", Source, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_INT_CONVERSION: Integer conversion out of range\n");
    }

    [Fact]
    public void OverflowInAnOmittedDefaultAbortsBeforeTheCallee()
    {
        const string Source = "func f(x: i32, y?: i32 = x + 1) => Console.writeLine(\"bad\")\nf(2147483647)";
        var column = Source.IndexOf("x + 1", StringComparison.Ordinal) + 1;
        ScalarEmissionTest.EmitFixture(Prefix + "Overflow", Source, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Fact]
    public void NoncompletingExplicitArgumentSkipsDefaultsAndTheCallee()
        => ScalarEmissionTest.EmitFixture(Prefix + "Never", "func spin() -> Never => loop => ()\nfunc f(x: i32, y?: i32 = 2147483647 + 1) => Console.writeLine(\"bad\")\nConsole.writeLine(\"begin\")\nf(spin())", "begin\n", 0, timeoutMilliseconds: 250);

    [Fact]
    public void PreparedReadCannotAliasAnotherAcquiredArgument()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y: i32, z?: i32 = x + y) -> i32 => z\nf(2, 4)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.IsGenerated);
        var read = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Read);
        var other = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Constant && x.Constant == 4);
        Assert.True(read > other && other >= 0);
        body.ValueOperands[body.Values[read].Start] = other;
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out error));
        Assert.NotNull(error);
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expression")]
    [InlineData("order")]
    public void CorruptDefaultPlansFailBeforeWritingAndRecover(string mutation)
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y?: i32 = x + 1, z?: i32 = y + 2) -> i32 => z\nf(2)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.IsGenerated);
        var call = (InvocationKoto)Assert.Single(body.Operations, x => x.Kind == OwnershipOperationKind.Call).Source;
        var plan = call.BoundCall!;
        var defaults = plan.DefaultArguments.ToArray();
        if (mutation == "missing")
        {
            defaults = [];
        }
        else if (mutation == "expression")
        {
            defaults[0] = defaults[0] with { Expression = call.ArgumentNodes[0] };
        }
        else
        {
            Array.Reverse(defaults);
        }

        plan.Set(plan.Target, plan.ReturnType, null, plan.ArgumentToParameter, [], operations: plan.ArgumentOperations, defaults: defaults);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out error));
        Assert.NotNull(error);
        Assert.Empty(writer.ToString());
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Theory]
    [InlineData("func f(x: string, y?: string = x) => ()\nf(\"a\", \"b\")")]
    [InlineData("func helper() -> i32 => 1\nfunc f(x?: i32 = helper()) => ()\nf(2)")]
    [InlineData("func helper() -> i32 => 1\nfunc f(x?: i32 = (loop => exit helper())) => ()\nf(3)")]
    public void UnsupportedDefaultBodiesRemainRejectedEvenWhenSupplied(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void WarmDefaultOwnershipAndEmissionReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: i32, y?: i32 = (scope: do\n    var n = (loop => exit (do => if x > 0 => (x@i64 + 1)@i32 else => 0))\n    n += 0\n    exit to scope: n\n)) -> i32 => y\nlet a = f(1)\nlet b = f(2)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Scalar default emission failed.");
            }
        }));
    }

#if DEBUG
    private const string Prefix = "Default_Debug_";
#else
    private const string Prefix = "Default_Release_";
#endif
}
