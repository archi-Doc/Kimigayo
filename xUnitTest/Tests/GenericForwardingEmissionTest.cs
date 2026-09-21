// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericForwardingEmissionTest
{
    internal const string Weight = "group W\n    public let baseline: i32 = 1\n    public func weight<T>(value: ref/T) -> i32 => baseline\n    specialize func weight<i32>(value: ref/i32) -> i32 => 2\n    public func forward<T>(value: ref/T) -> i32 => weight<T>(value)\n    public func total<length N, T>(values: ref/[N of T]) -> i32\n        var result: i32 = 0\n        for index in values.indices\n            result = result + forward<T>(values[index]@ref/T)\n        return result\n";

    [Theory]
    [InlineData("i32", "[10, 20, 30]", 6)]
    [InlineData("i64", "[10, 20, 30]", 3)]
    [InlineData("bool", "[true, false, true]", 3)]
    public void SelectsThroughForwarding(string type, string values, int expected)
        => ScalarEmissionTest.EmitFixture("GenericForwarding" + type, Weight + $"let values: [3 of {type}] = {values}\nrequire W.total<3, {type}>(values@ref) == {expected} else => $abort(\"selection\")", string.Empty);

    [Fact]
    public void EmptyArrayDoesNotBorrow()
        => ScalarEmissionTest.EmitFixture("GenericForwardingEmpty", Weight + "let values: [0 of i32] = []\nrequire W.total<0, i32>(values@ref) == 0 else => $abort(\"empty\")", string.Empty);

    [Fact]
    public void ReadsStaticLiteral()
        => ScalarEmissionTest.EmitFixture("GenericForwardingStatic", "group Settings\n    public let value: i32 = 7\nrequire Settings.value == 7 else => $abort(\"static\")", string.Empty);

    [Fact]
    public void SpecializationRecursionKeepsTheSelectedImplementation()
        => ScalarEmissionTest.EmitFixture("GenericForwardingRecursive", "func count<T>(value: T) -> i32 => 77\nspecialize func count<i32>(value: i32) -> i32 => if value == 0 => 0 else => count<i32>(value - 1) + 1\nrequire count<i32>(4) == 4 else => $abort(\"recursion\")", string.Empty);

    [Fact]
    public void RejectsUnsupportedDependentResultForwarding()
    {
        var c = MinimalEmissionTest.Analyze("func identity<T>(value: T) -> T => value\nfunc forward<T>(value: T) -> T => identity<T>(value)\nConsole.writeLine(forward<string>(\"owned\"))");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void ForwardsLengthArguments()
        => ScalarEmissionTest.EmitFixture("GenericForwardingLength", "func size<length N, T>(value: ref/[N of T]) -> isize => value.length\nfunc forward<length M, U>(value: ref/[M of U]) -> isize => size<M, U>(value)\nlet value: [2 of i64] = [4, 7]\nrequire forward<2, i64>(value@ref) == 2 else => $abort(\"length\")", string.Empty);

    [Fact]
    public void BorrowsNonCopyElementsWithoutMoving()
        => ScalarEmissionTest.EmitFixture("GenericForwardingNonCopy", Weight + "let values: [2 of string] = [\"left\", \"right\"]\nrequire W.total<2, string>(values@ref) == 2 else => $abort(\"count\")\nConsole.writeLine(values[0])\nConsole.writeLine(values[1])", "left\nright\n");

    [Fact]
    public void SharedI32Comparison()
        => ScalarEmissionTest.EmitFixture("GenericForwardingComparison", "func small<T>(value: T, n: i32) -> bool => n < 5\nrequire small<i32>(7, 4) and not small<i32>(7, 5) else => $abort(\"comparison\")", string.Empty);

    [Fact]
    public void SharedI32Overflow()
        => ScalarEmissionTest.EmitFixture("GenericForwardingOverflow", "func add<T>(value: T, n: i32) -> i32 => n + 1\nlet result = add<i32>(7, 2147483647)", string.Empty, 1, "Hello.kimi:1:41: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void IndexedBorrowChecksBounds(int index)
        => ScalarEmissionTest.EmitFixture("GenericForwardingBounds" + (index < 0 ? "Negative" : "End"), "func weight<T>(value: ref/T) -> i32 => 1\nfunc get<length N, T>(values: ref/[N of T], index: isize) -> i32 => weight<T>(values[index]@ref/T)\nlet values: [2 of i32] = [7, 8]\nlet result = get<2, i32>(values@ref, " + index + ")", string.Empty, 1, "Hello.kimi:2:79: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void InferenceUsesTheOrdinaryContract()
        => ScalarEmissionTest.EmitFixture("GenericForwardingInference", "specialize func weight<i32>(value: i32) -> i32 => 2\nfunc weight<T>(value: T) -> i32 => 1\nrequire weight(7) == 2 and weight(true) == 1 else => $abort(\"inference\")", string.Empty);

    [Fact]
    public void ForwardingPreservesOriginalOverloadSelection()
        => ScalarEmissionTest.EmitFixture("GenericForwardingOverload", "func weight<T>(value: T) -> i32 => 1\nspecialize func weight<i32>(value: i32) -> i32 => 2\nfunc weight(value: i32) -> i32 => 9\nfunc forward<T>(value: T) -> i32 => weight<T>(value)\nrequire forward<i32>(7) == 2 and weight(7) == 9 else => $abort(\"overload\")", string.Empty);

    [Theory]
    [InlineData("group W\n    public var value: i32 = 1\nrequire W.value == 1 else => $abort(\"value\")")]
    [InlineData("group W\n    public let value: i32 = effect()\n    public func effect() -> i32\n        Console.writeLine(\"effect\")\n        return 1\nConsole.writeLine(\"unused\")")]
    [InlineData("group W\n    public let value: i32 = 1\nW.value = 2")]
    [InlineData("group W\n    public let value: i32 = true\n()")]
    public void RejectsInvalidOrUnsupportedStaticState(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("index")]
    [InlineData("loan")]
    [InlineData("constant")]
    public void RejectsCorruptPlanAndRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Weight + "let values: [1 of i32] = [7]\nlet result = W.total<1, i32>(values@ref)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == (defect == "constant" ? "weight" : "total") && !x.Function.IsSpecialization);
        if (defect == "constant")
        {
            var id = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Constant);
            body.Values[id] = body.Values[id] with { Constant = 99 };
        }
        else
        {
            var id = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Borrow && x.Source is IndexKoto);
            if (defect == "loan")
            {
                body.OperationStorage[id] = body.Operations[id] with { LoanMode = LoanRequirement.Uniq };
            }
            else
            {
                body.ValueOperands[body.Values[id].Start + 1] = id;
            }
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }
}
