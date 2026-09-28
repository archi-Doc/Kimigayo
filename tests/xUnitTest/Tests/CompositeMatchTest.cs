// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class CompositeMatchTest
{
    [Theory]
    [InlineData("Tuple", "let x: (i32, bool) = (7, true)\nlet y = match x\n    (let n, true) if n > 0 => n\n    (_, _) => 0\nrequire y == 7 else => $abort(\"bad\")")]
    [InlineData("Enum", "enum E<T>\n    A(T)\n    B\nlet x: E<(i32, bool)> = .A((7, true))\nlet y = match x@move\n    .A((let n, true)) if n > 0 => n\n    .A(_) => 0\n    .B => -1\nrequire y == 7 else => $abort(\"bad\")")]
    [InlineData("InactivePayload", "enum E<T>\n    A(T)\n    B\nlet x: E<(i64, bool)> = .B\nlet y = match x@move\n    .A((let n, true)) if n > 0 => 1\n    .A(_) => 2\n    .B => 3\nrequire y == 3 else => $abort(\"bad\")")]
    [InlineData("GuardFalse", "let x: (i32, bool) = (-1, true)\nlet y = match x\n    (let n, true) if n > 0 => 0\n    (let n, _) => n\nrequire y == -1 else => $abort(\"bad\")")]
    [InlineData("BoolBinding", "let x = (true, 3)\nlet y = match x\n    (let b, let n) if b => b\n    (_, _) => false\nrequire y else => $abort(\"bad\")")]
    [InlineData("TwoCandidates", "let x: (i32, i64) = (7, 5)\nlet y = match x\n    (let a, let b) if a == 7 and b == 5 => a\n    (_, _) => 0\nrequire y == 7 else => $abort(\"bad\")")]
    [InlineData("NestedEnum", "enum E<T>\n    A(T)\n    B\nlet x: E<E<(u8, i64)>> = .A(.A((3, 9)))\nlet y = match x@move\n    .A(.A((let n, 9))) if n == 3 => n\n    .A(_) => 0\n    .B => 0\nrequire y == 3 else => $abort(\"bad\")")]
    [InlineData("Wide", "let x: (u8, u128) = (3, 340282366920938463463374607431768211455)\nlet y = match x\n    (3, let n) => n\n    (_, _) => 0\nrequire y == 340282366920938463463374607431768211455 else => $abort(\"bad\")")]
    [InlineData("GuardEffects", "func check(n: i32) -> bool\n    Console.writeLine(\"guard\")\n    return n > 0\nlet x = (-1, true)\nlet y = match x\n    (let n, false) if check(n) => 1\n    (let n, true) if check(n) => 2\n    (_, _) => 3\nrequire y == 3 else => $abort(\"bad\")")]
    [InlineData("ArrayTuple", "let a: [2 of (i32, bool)] = [(2, true), (3, false)]\nvar total = 0\nfor item in a@move\n    match item\n        (let n, _) => total += n\nrequire total == 5 else => $abort(\"bad\")")]
    [InlineData("ArrayUnit", "let a: [3 of ()] = [(), (), ()]\nvar total = 0\nfor item in a@move\n    total += 1\nrequire total == 3 else => $abort(\"bad\")")]
    [InlineData("EmptyArray", "let a: [0 of (i32, bool)] = []\nfor item in a\n    $abort(\"bad\")")]
    [InlineData("GuardTransfer", "func f() -> i32\n    match (7, true)\n        (let n, _) if (return n) => ()\n        (_, _) => ()\n    return 0\nrequire f() == 7 else => $abort(\"bad\")")]
    [InlineData("Snapshot", "var values: [2 of (i32, bool)] = [(2, true), (3, false)]\nvar sum = 0\nfor item in values@move\n    values = [(9, false), (9, false)]\n    match item\n        (let n, _) => sum += n\nrequire sum == 5 else => $abort(\"bad\")")]
    [InlineData("WholeBinding", "let x = (7, true)\nlet y = match x\n    let whole => whole\nmatch y\n    (7, true) => ()\n    (_, _) => $abort(\"bad\")")]
    [InlineData("AlignedEnum", "enum E\n    A(u8, u128, u16)\n    B\nlet value: E = .A(3, 340282366920938463463374607431768211455, 9)\nmatch value@move\n    .A(let a, let b, let c)\n        require a == 3 and b == 340282366920938463463374607431768211455 and c == 9 else => $abort(\"bad\")\n    .B => $abort(\"bad\")")]
    public void NativePatterns(string name, string source)
    {
        ScalarEmissionTest.EmitFixture("CompositePattern" + name, source + "\nConsole.writeLine(\"ok\")", name == "GuardEffects" ? "guard\nok\n" : "ok\n");
    }

    [Theory]
    [InlineData("func f(x: (i32, bool)) -> i32 => match x\n    (let n, true) if n > 0 => n\n    (_, _) => 0")]
    [InlineData("func f(x: Option<(i32, bool)>) -> i32 => match x\n    .Some((let n, true)) if n > 0 => n\n    .Some(_) => 0\n    .None => -1")]
    [InlineData("func f(x: (i32, i32)) -> i32 => match x\n    (let a, let b) if a > b => a\n    (_, let b) => b")]
    [InlineData("func f(x: ((i32, bool), i32)) -> i32 => match x\n    ((let a, true), let b) if a > b => a\n    (_, _) => 0")]
    [InlineData("func f(x: (string, i32)) -> string => match x@move\n    (let s, _) => s@move")]
    public void OwnedNestedPatternsVerify(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, string.Join("; ", c.Ownership.Issues));
    }

    [Theory]
    [InlineData("func f(x: (i32, bool)) => match x\n    (var n, _) if (n = 1) => ()\n    (_, _) => ()")]
    [InlineData("func f(x: (i32, bool)) -> i32 => match x\n    (let n, true) if n > 0 => n")]
    [InlineData("func f(x: Option<(i32, bool)>) => match x\n    .Some((let n, let n)) => ()\n    .None => ()")]
    public void InvalidNestedPatternsAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("arm")]
    [InlineData("decomposition")]
    [InlineData("acquisition")]
    [InlineData("guard")]
    [InlineData("parent")]
    public void CorruptCompositePlansAreRejected(string defect)
    {
        const string Source = "enum E<T>\n    A(T)\n    B\nlet x: E<(i32, bool)> = .A((7, true))\nlet y = match x@move\n    .A((let n, true)) if n > 0 => n\n    .A(_) => 0\n    .B => -1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Matches.Count != 0);
        var read = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Read && x.Source.BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate);
        switch (defect)
        {
            case "position":
                body.Values[read] = body.Values[read] with { Constant = 0 };
                break;
            case "arm":
                body.OperationSteps[read] = 1;
                break;
            case "decomposition":
                body.DecompositionStorage[0] = body.Decompositions[0] with { PayloadStart = 0 };
                break;
            case "acquisition":
                var acquire = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.AcquirePattern);
                body.OperationStorage[acquire] = body.Operations[acquire] with { Acquisition = AcquisitionKind.Move };
                break;
            case "guard":
                body.MatchArmStorage[0] = body.MatchArms[0] with { GuardValue = body.MatchArms[0].Test };
                break;
            case "parent":
                var binding = body.Matches[0].Binding;
                binding.PositionStorage[2] = binding.Positions[2] with { Parent = 2 };
                break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void ReloadAndWarmAnalysisPreserveNestedCandidates()
    {
        const string Source = "enum E<T>\n    A(T)\n    B\nlet x: E<(i32, bool)> = .A((7, true))\nmatch x@move\n    .A((let n, true)) if n == 7 => Console.writeLine(\"ok\")\n    .A(_) => ()\n    .B => ()";
        var c = MinimalEmissionTest.Analyze(Source);
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        using var writer = new StringWriter();
        Assert.True(restored.Emission.WriteIr(writer, out error), error);
        Assert.Equal(original.ToString(), writer.ToString());
        ScalarEmissionTest.WriteFixture("CompositePatternReload", writer.ToString(), "ok\n");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
        Assert.True(c.Emission.TryPrepare(out var module, out error), error);
        Assert.Equal(0, AllocationMeasurement.Measure(() => module.WriteIr(TextWriter.Null)));
    }
}
