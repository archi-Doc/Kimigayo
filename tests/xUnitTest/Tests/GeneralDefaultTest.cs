// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GeneralDefaultTest
{
    [Theory]
    [InlineData("Array", "func f(x: Array<i32> = [3, 4]) -> i32 => x[0] + x[1]\nrequire f() == 7 and f([1, 2]) == 3 else => $abort(\"array\")")]
    [InlineData("Dictionary", "func f(x: Dictionary<i32, i32> = [1: 9]) -> i32 => x[1]\nrequire f() == 9 else => $abort(\"dictionary\")")]
    [InlineData("Interpolation", "func f(x: i32, y: string = \"value \\(x)\") -> bool => y == \"value 3\"\nrequire f(3) else => $abort(\"format\")")]
    [InlineData("Iteration", "func f(x: i32 = label work: do\n    var sum = 0\n    for n in [1, 2, 3]\n        sum += n\n    exit to work sum\n) -> i32 => x\nrequire f() == 6 else => $abort(\"iteration\")")]
    [InlineData("ArrayMutation", "func f(x: Array<i32> = label work: do\n    var a: Array<i32> = []\n    a@uniq.append(3)\n    exit to work a@move\n) -> i32 => x[0]\nrequire f() == 3 else => $abort(\"array local\")")]
    [InlineData("GenericArray", "func f<T>(x: T, values: Array<T> = [x]) -> T\n    T is Copy\n    return values[0]\nrequire f(3) == 3 and f(true) else => $abort(\"generic array\")")]
    [InlineData("GenericDictionary", "func f<T>(x: T, values: Dictionary<i32, T> = [1: x]) -> T\n    T is Copy\n    return values[1]\nrequire f(3) == 3 and f(true) else => $abort(\"generic dictionary\")")]
    [InlineData("GenericArrayMutation", "func f<T>(x: T, values: Array<T> = label work: do\n    var a = Array<T>.init(capacity: 1)\n    a@uniq.append(x)\n    exit to work a@move\n) -> T\n    T is Copy\n    return values[0]\nrequire f(3) == 3 and f(true) else => $abort(\"generic mutation\")")]
    public void DefaultsUseOrdinaryBodyOperations(string name, string source)
        => ScalarEmissionTest.EmitFixture("GeneralDefault" + name, source, string.Empty);

    [Theory]
    [InlineData("obj", "Obj")]
    [InlineData("rc", "Rc")]
    [InlineData("arc", "Arc")]
    public void CompilerFactoriesDeliverOneOwnedHandle(string mode, string factory)
    {
        var source = "func f(x: " + mode + "/i32 = Kimi.Intrinsics.make" + factory + "(3)) -> i32 => x@follow\nrequire f() == 3 else => $abort(\"handle\")";
        NativeAllocationAudit.WriteFixture("GeneralDefault" + factory, source, 1, 1, 20);
    }

    [Theory]
    [InlineData("rc", "Rc")]
    [InlineData("arc", "Arc")]
    public void PreparedSharedHandlesMayBeCloned(string mode, string factory)
    {
        var source = "func f(x: " + mode + "/i32, y: " + mode + "/i32 = Kimi.Intrinsics.clone(x@ref)) -> i32 => x@follow + y@follow\nrequire f(Kimi.Intrinsics.make" + factory + "(3)) == 6 else => $abort(\"clone\")";
        NativeAllocationAudit.WriteFixture("GeneralDefaultClone" + factory, source, 1, 1, 20);
    }

    [Fact]
    public void DefaultLocalClosuresMayBorrowIndependentLocalStorage()
        => ScalarEmissionTest.EmitFixture("GeneralDefaultLocalClosure", "func f(x: i32 = label work: do\n    var n = 3\n    var action = func [n@uniq] () -> i32\n        n@follow += 1\n        return n@follow\n    exit to work action()\n) -> i32 => x\nrequire f() == 4 else => $abort(\"closure\")", string.Empty);

    [Fact]
    public void GenericFactoryDefaultsUseThePreparedCopy()
        => ScalarEmissionTest.EmitFixture("GeneralDefaultGenericObject", "func f<T>(x: T, y: obj/T = Kimi.Intrinsics.makeObj(x)) -> obj/T\n    T is Copy and ObjectPayload\n    return y@move\nlet number = f(3)\nlet flag = f(true)\nrequire number@follow == 3 and flag@follow else => $abort(\"generic object\")", string.Empty);

    [Fact]
    public void DefaultDefersFinishBeforeCalleeCleanup()
    {
        const string source = """
            struct Item
                public let id: i32
                public init(id: i32) => self.id = id
                drop
                    if self.id == 1 => Console.writeLine("first")
                    else => Console.writeLine("second")
            func f(values: Array<Item> = label work: do
                defer => Console.writeLine("default end")
                exit to work [Item.init(1), Item.init(2)]
            ) => Console.writeLine("callee")
            f()
            """;
        ScalarEmissionTest.EmitFixture("GeneralDefaultDefers", source, "default end\ncallee\nsecond\nfirst\n");
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void GeneralDefaultPlansAllocateNothingWarm()
        {
            const string source = "func f(x: obj/i32 = Kimi.Intrinsics.makeObj(3), a: Array<i32> = [1, 2], d: Dictionary<i32, i32> = [1: 4]) -> i32 => x@follow + a[0] + d[1]\nrequire f() == 8 else => $abort(\"defaults\")";
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
            var restored = CompilationTestHelper.Reload(c);
            Assert.True(restored.Bind().IsComplete);
            restored.Binding.CheckStartup(OutputKind.Application);
            Assert.True(restored.Ownership.Analyze().IsVerified);
            Assert.True(restored.Emission.WriteIr(TextWriter.Null, out failure), MinimalEmissionTest.Describe(restored, failure));
        }
    }

    [Theory]
    [InlineData("func f(x: string, y: Array<string> = [x]) => ()")]
    [InlineData("func f(x: string, y: Dictionary<i32, string> = [1: x]) => ()")]
    public void CollectionPayloadsCannotMovePreparedInputs(string source)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(source + "\npublic func main() => ()").Diagnostics);
        Assert.Equal("DefaultArgumentMove_Kd", record.Code);
    }
}
