// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericCallbackEmissionTest
{
    [Theory]
    [InlineData("i8", "7")]
    [InlineData("i32", "7")]
    [InlineData("i64", "7")]
    [InlineData("bool", "true")]
    public void AdaptsInstantiatedParameter(string type, string value)
        => ScalarEmissionTest.EmitFixture(
            "GenericCallback" + type,
            "func accepts<T>(value: T, f: (T) -> bool) -> bool => f(value)\nif not accepts<" + type + ">(" + value + ", func (x: " + type + ") => x == " + value + ")\n    $abort(\"callback\")",
            string.Empty);

    [Theory]
    [InlineData("Twice", "func invoke<T>(x: T, f: (T) -> bool) -> bool\n    T is Copy\n    let first = f(x)\n    return f(x)\nlet f: (i32) -> bool = func (x: i32)\n    writeLine(\"called\")\n    return x == 7\nrequire invoke<i32>(7, f) else => $abort(\"value\")", "called\ncalled\n")]
    [InlineData("TwoArguments", "func invoke<T>(x: T, y: T, f: (T, T) -> bool) -> bool => f(x, y)\nrequire invoke<i32>(3, 7, func (x: i32, y: i32) => x == 3 and y == 7) else => $abort(\"order\")", "")]
    [InlineData("Isize", "func invoke<T>(x: T, f: (T) -> isize) -> isize => f(x)\nrequire invoke<i32>(7, func (x: i32) => 42) == 42 else => $abort(\"result\")", "")]
    [InlineData("Unit", "func invoke<T>(x: T, f: (T) -> ()) => f(x)\ninvoke<i32>(7, func (x: i32) => writeLine(\"called\"))", "called\n")]
    [InlineData("NoArguments", "func invoke<T>(x: T, f: () -> bool) -> bool => f()\nrequire invoke<i32>(7, func () => true) else => $abort(\"result\")", "")]
    public void SharedCallsPreserveBehavior(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("GenericCallback" + name, source, stdout);

    [Theory]
    [InlineData("loan")]
    [InlineData("argument")]
    [InlineData("receiver")]
    public void RejectsCorruptSharedCallAndReanalysisRecovers(string defect)
    {
        var c = MinimalEmissionTest.Analyze("func invoke<T>(x: T, f: (T) -> bool) -> bool => f(x)\nlet result = invoke<i32>(7, func (x: i32) => true)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Function.Name == "invoke");
        var call = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Call);
        if (defect == "loan")
        {
            body.ComparisonLoans[0] = body.ComparisonLoans[0] with { Place = 0 };
        }
        else if (defect == "receiver")
        {
            body.OperationStorage[call] = body.Operations[call] with { Input = 0 };
        }
        else
        {
            var entry = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.CallEntry);
            body.OperationStorage[entry] = body.Operations[entry] with { Place = 0 };
        }

        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }
}
