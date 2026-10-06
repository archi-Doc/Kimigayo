// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class RecursiveDefaultTest
{
    private const string Generic = "func f<T>(n: i32, x: T, value: T = if n == 0 => x else => f(n - 1, x)) -> T\n    T is Copy\n    return value\n";

    [Theory]
    [InlineData("Scalar", "func f(n: i32, value: i32 = if n == 0 => 1 else => f(n - 1)) -> i32 => value\nrequire f(8) == 1 and f(8, 7) == 7 else => $abort(\"recursive default\")")]
    [InlineData("Mutual", "func f(n: i32, value: i32 = if n == 0 => 1 else => g(n - 1)) -> i32 => value\nfunc g(n: i32, value: i32 = if n == 0 => 2 else => f(n - 1)) -> i32 => value\nrequire f(2) == 1 and f(3) == 2 else => $abort(\"mutual defaults\")")]
    [InlineData("Generic", "func f<T>(n: i32, x: T, value: T = if n == 0 => x else => f(n - 1, x)) -> T\n    T is Copy\n    return value\nrequire f(4, 7) == 7 and f(5, true) else => $abort(\"generic defaults\")")]
    [InlineData("Owned", "func f(n: i32, value: obj/i32 = if n == 0 => Kimi.Intrinsics.makeObj(3) else => f(n - 1)) -> obj/i32 => value@move\nlet result = f(5)\nrequire result@follow == 3 else => $abort(\"owned default\")")]
    [InlineData("String", "func f(n: i32, text: string, value: string = if n == 0 => Text.toString(text) else => f(n - 1, Text.toString(text))) -> string => value@move\nlet result = f(3, \"text\")\nrequire result == \"text\" else => $abort(\"string default\")")]
    [InlineData("Reference", Generic + "var n = 3\nlet r = f(3, n@ref)\nrequire r@follow == 3 else => $abort(\"reference\")\nn = 4\nrequire n == 4 else => $abort(\"last use\")")]
    [InlineData("Closure", "func f(n: i32, value: () -> i32 = if n == 0 => func () => 7 else => f(n - 1)) -> () -> i32 => value@move\nlet action = f(3)\nrequire action() == 7 else => $abort(\"closure default\")")]
    [InlineData("PreparedAddress", "func f(n: i32, x: i32, p: raw/i32 = x@raw, value: bool = if n == 0 => x@raw == p else => f(n - 1, x)) -> bool => value\nrequire f(2, 3) else => $abort(\"prepared address\")")]
    [InlineData("UnitAddress", "func f(n: i32, x: (), p: raw/() = x@raw, value: bool = if n == 0 => x@raw == p else => f(n - 1, x)) -> bool => value\nrequire f(2, ()) else => $abort(\"unit address\")")]
    [InlineData("BorrowedString", "func f(n: i32, text: ref/string, value: bool = if n == 0 => text == \"text\" else => f(n - 1, text)) -> bool => value\nlet text = \"text\"\nrequire f(2, text@ref) else => $abort(\"borrowed string\")")]
    public void OmittedDefaultsRecurseAtRuntime(string name, string source)
        => ScalarEmissionTest.EmitFixture("RecursiveDefault" + name, source, string.Empty);

    [Fact]
    public void EvaluatorsLeavePreparedOwnershipWithEachCallee()
    {
        const string source = """
            struct Item
                public let id: i32
                public init(id: i32) => self.id = id
                drop => Console.writeLine(Text.toString(self.id))
            func f(n: i32, item: Item, value: Item = if n == 0 => Item.init(100) else => f(n - 1, Item.init(n))) -> Item => value@move
            let result = f(2, Item.init(9))
            """;
        ScalarEmissionTest.EmitFixture("RecursiveDefaultCleanup", source, "1\n2\n9\n100\n");
    }

    [Fact]
    public void DefaultLocalDefersRunBeforeEachCallee()
    {
        const string source = "func f(n: i32, value: i32 = label work: do\n    defer => Console.writeLine(Text.toString(n))\n    exit to work if n == 0 => 7 else => f(n - 1)\n) -> i32\n    Console.writeLine(\"callee\")\n    return value\nrequire f(2) == 7 else => $abort(\"defer\")";
        ScalarEmissionTest.EmitFixture("RecursiveDefaultDefers", source, "0\ncallee\n1\ncallee\n2\ncallee\n");
    }

    [Theory]
    [InlineData("rc", "Rc")]
    [InlineData("arc", "Arc")]
    public void RecursiveClonesShareOneAllocation(string mode, string factory)
    {
        var source = "func f(n: i32, handle: " + mode + "/i32, value: " + mode + "/i32 = if n == 0 => Kimi.Intrinsics.clone(handle@ref) else => f(n - 1, Kimi.Intrinsics.clone(handle@ref))) -> " + mode + "/i32 => value@move\nlet result = f(3, Kimi.Intrinsics.make" + factory + "(7))\nrequire result@follow == 7 else => $abort(\"clone default\")";
        NativeAllocationAudit.WriteFixture("RecursiveDefaultClone" + factory, source, 1, 1, 20);
    }

    [Fact]
    public void RecursiveReferenceResultKeepsTheCallersLoan()
    {
        var error = Assert.Single(DiagnosticCorpus.Check(Generic + "var n = 3\nlet r = f(3, n@ref)\nn = 4\n_ = r@follow").Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
    }

    [Fact]
    public void RuntimeTypeTestsInEvaluatorsDiscoverTheirTarget()
    {
        const string source = """
            open struct Base
                protected init() => ()
            struct Leaf: Base
                public init(): base() => ()
            func f(n: i32, item: objref/Base, value: bool = if n == 0 => item is Leaf else => f(n - 1, item)) -> bool => value
            let item = Kimi.Intrinsics.makeObj(Leaf.init())
            require f(2, item@objref/Base) else => $abort("runtime type")
            """;
        ScalarEmissionTest.EmitFixture("RecursiveDefaultTypeTest", source, string.Empty);
    }

    [Fact]
    public void ExplicitObjectUpcastsRetainTheTargetViewAndOneOwner()
    {
        const string source = """
            open struct Base
                protected init() => ()
                drop => Console.writeLine("base")
            struct Leaf: Base
                public init(): base() => ()
                drop => Console.writeLine("leaf")
            func f(n: i32, item: obj/Base = if n == 0 => Kimi.Intrinsics.makeObj(Leaf.init())@obj/Base else => f(n - 1)) -> obj/Base => item@move
            let item = f(2)
            require item is Leaf else => $abort("dynamic type")
            """;
        NativeAllocationAudit.WriteFixture("RecursiveDefaultUpcast", source, 1, 1, 16, "leaf\nbase\n");
    }

    [Fact]
    public void GrowingDefaultSubstitutionsAreResourceDiagnosed()
    {
        const string source = "func grow<T>(n: i32, result: i32 = if n == 0 => 0 else => grow<[1 of T]>(n - 1)) -> i32 => result\n_ = grow<i32>(2)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out var error));
        Assert.Empty(output.ToString());
        Assert.True(c.Emission.FailureIsResourceLimit, error);
    }

    [Fact]
    public void RecursiveAbortDoesNotUnwindPreparedInputsOrDefaultDefers()
    {
        const string source = """
            struct Item
                public init() => ()
                drop => Console.writeLine("drop")
            func f(n: i32, item: Item, value: i32 = label work: do
                defer => Console.writeLine("defer")
                if n == 0
                    $abort("stop")
                exit to work f(n - 1, Item.init())
            ) -> i32 => value
            _ = f(2, Item.init())
            """;
        ScalarEmissionTest.EmitFixture("RecursiveDefaultAbort", source, string.Empty, 1, "Hello.kimi:7:9: abort KIMI_E_ABORT: stop\n");
    }

    [Fact]
    public void UnboundedRuntimeRecursionHasFiniteGeneration()
    {
        var c = MinimalEmissionTest.Analyze("func f(value: i32 = f()) -> i32 => value\nif false => f()");
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains("@__kimi_default0", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("@__kimi_default1", ir, StringComparison.Ordinal);
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void RecursiveEvaluatorPlansReuseStorage()
        {
            var c = MinimalEmissionTest.Analyze(Generic + "require f(3, 7) == 7 and f(4, true) else => $abort(\"reuse\")");
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
            var restored = CompilationTestHelper.Reload(c);
            Assert.True(restored.Bind().IsComplete);
            restored.Binding.CheckStartup(OutputKind.Application);
            Assert.True(restored.Ownership.Analyze().IsVerified);
            Assert.True(restored.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(restored, error));
        }
    }
}
