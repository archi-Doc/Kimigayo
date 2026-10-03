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
            "func accepts<T>(value: T, f: (T) -> bool) -> bool => f(value@move)\nif not accepts<" + type + ">(" + value + ", func (x: " + type + ") => x == " + value + ")\n    $abort(\"callback\")",
            string.Empty);

    [Theory]
    [InlineData("Twice", "func invoke<T>(x: T, f: (T) -> bool) -> bool\n    T is Copy\n    let first = f(x)\n    return f(x)\nlet f: (i32) -> bool = func (x: i32)\n    Console.writeLine(\"called\")\n    return x == 7\nrequire invoke<i32>(7, f@move) else => $abort(\"value\")", "called\ncalled\n")]
    [InlineData("TwoArguments", "func invoke<T>(x: T, y: T, f: (T, T) -> bool) -> bool => f(x@move, y@move)\nrequire invoke<i32>(3, 7, func (x: i32, y: i32) => x == 3 and y == 7) else => $abort(\"order\")", "")]
    [InlineData("Isize", "func invoke<T>(x: T, f: (T) -> isize) -> isize => f(x@move)\nrequire invoke<i32>(7, func (x: i32) => 42) == 42 else => $abort(\"result\")", "")]
    [InlineData("Unit", "func invoke<T>(x: T, f: (T) -> ()) => f(x@move)\ninvoke<i32>(7, func (x: i32) => Console.writeLine(\"called\"))", "called\n")]
    [InlineData("NoArguments", "func invoke<T>(x: T, f: () -> bool) -> bool => f()\nrequire invoke<i32>(7, func () => true) else => $abort(\"result\")", "")]
    // An instance's `(T) -> T` value call returns the substituted T: its scalar result was left without a value, so building
    // any instance failed with GenerationFailed_Kd ("Missing scalar computation") after the check passed.
    [InlineData("GenericResult", "func apply<T>(f: (T) -> T, v: T) -> T => f(v@move)\nlet g = func [] (value: i32) -> i32 => value + 1\nrequire apply(g, 2) == 3 else => $abort(\"result\")", "")]
    [InlineData("GenericResultNested", "func twice<T>(f: (T) -> T, v: T) -> T\n    T is Copy\n    return f(f(v))\nlet g = func [] (value: i64) -> i64 => value * 3\nrequire twice(g, 2@i64) == 18 else => $abort(\"nested\")", "")]
    [InlineData("GenericResultTuple", "func apply<T>(f: (T) -> T, v: T) -> T => f(v@move)\nlet g = func [] (p: (i32, i32)) -> (i32, i32) => (p.1, p.0)\nlet r = apply(g, (1, 2))\nrequire r.0 == 2 and r.1 == 1 else => $abort(\"tuple\")", "")]
    [InlineData("GenericResultString", "func apply<T>(f: (T) -> T, v: T) -> T => f(v@move)\nlet g = func [] (s: string) -> string => s@move\nrequire apply(g, \"abc\") == \"abc\" else => $abort(\"string\")", "")]
    // SPEC 10.5: a written closure header is evidence for T before the literal's body is checked, so an untyped integer
    // argument is fitted to it; it was UnprovenConstraint_Kd for `T is Copy`.
    [InlineData("HeaderEvidence", "func apply<T>(f: (T) -> T, v: T) -> T\n    T is Copy\n    return f(v)\nrequire apply(func [] (value: i32) -> i32 => value + 1, 2) == 3 else => $abort(\"header\")", "")]
    public void SharedCallsPreserveBehavior(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("GenericCallback" + name, source, stdout);

    [Theory]
    [InlineData("i8", "7")]
    [InlineData("i32", "7")]
    [InlineData("bool", "true")]
    public void MonomorphizesCallableValueCalls(string type, string value)
    {
        // SPEC 21.3.1: the instance calls its (T) -> bool value through the substituted signature.
        var c = MinimalEmissionTest.Analyze("func accepts<T>(value: T, f: (T) -> bool) -> bool => f(value@move)\nlet r = accepts<" + type + ">(" + value + ", func (x: " + type + ") => x == " + value + ")");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(module.PendingEntries);
        var instance = Assert.Single(GenericStorageEmissionTest.Instances(module));
        var valueCall = Assert.Single(instance.Instructions, x => x.Opcode == EmissionOpcode.CallValue);
        Assert.Equal([type == "bool" ? "i1" : type], valueCall.Callee!.Parameters.Select(p => p.Type).ToArray());
    }

    [Theory]
    [InlineData("loan")]
    [InlineData("argument")]
    [InlineData("receiver")]
    public void RejectsCorruptValueCallAndReanalysisRecovers(string defect)
    {
        // BodyLowering validates every lowered body, including each monomorphized instance (SPEC 21.3.1);
        // the corrupt value call is rejected on the ordinary body that owns it.
        var c = MinimalEmissionTest.Analyze("func invoke(x: i32, f: (i32) -> bool) -> bool => f(x)\nlet result = invoke(7, func (x: i32) => true)");
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

    [Fact]
    public void AClosureHeaderThatContradictsAnotherArgumentIsInapplicable()
    {
        var c = MinimalEmissionTest.Analyze("func apply<T>(f: (T) -> T, v: T) -> T\n    T is Copy\n    return f(v)\nlet two: i32 = 2\nlet r = apply(func [] (value: i64) -> i64 => value, two)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static issue => issue.Code == Kimi.DiagnosticCode.NoApplicableOverload_Kd);
    }
}
