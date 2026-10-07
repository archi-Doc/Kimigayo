// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 8.10, 13.5.8: creation uses ordinary effects; destruction accounts for every feasible Semantics case.
public class GenericAdaptationEffectTest
{
    private const string Destruction = "struct Payload\n    drop => Console.writeLine(\"drop\")\n" +
        "group Helpers\n    public func discard<s/T>(value: s/T)\n        s is ref or obj\n        return\n";

    [Theory]
    [InlineData("ref", true)]
    [InlineData("obj", false)]
    public void AClosedCallUsesOnlyItsOwnDestructionCase(string mode, bool valid)
    {
        var source = Destruction + $"contract Runner\n    func run(self: ref/Self, value: {mode}/Payload)\n        effect confined\n" +
            $"struct Worker\n    Self is Runner\n    public func run(self: ref/Self, value: {mode}/Payload)\n        Helpers.discard(value@move)\n" +
            "func later<u/U>(value: u/U)\n    u is ref or obj\n    return\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (valid)
        {
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        }
        else
        {
            MinimalEmissionTest.AssertEffectBoundRejected(c);
        }

        // The first body is the ref case, and later reused side bodies must not erase the obj case's facts.
        var body = Assert.Single(c.Ownership.Bodies, static x => x.Function.Name == "discard");
        Assert.Equal(SemanticsKind.Ref, body.Cases.Span[0].Semantics);
        Assert.Contains(body.CaseDestructions, static x => x.Type?.Semantics == SemanticsKind.Obj && (x.Cases & 2UL) != 0 && (x.Cases & 1UL) == 0);
    }

    [Theory]
    [InlineData("ref", "T", true)]
    [InlineData("ref or obj", "T", false)]
    [InlineData("ref", "Payload", true)]
    [InlineData("ref or obj", "Payload", false)]
    public void ASymbolicForwarderKeepsEveryAdmittedDestructionCase(string admitted, string payload, bool valid)
    {
        var source = Destruction + $"contract Runner\n    func run<s/T>(self: ref/Self, value: s/{payload})\n        s is {admitted}\n        effect confined\n" +
            $"struct Worker\n    Self is Runner\n    public func run<s/T>(self: ref/Self, value: s/{payload})\n        s is {admitted}\n        Helpers.discard(value@move)\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (valid)
        {
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        }
        else
        {
            MinimalEmissionTest.AssertEffectBoundRejected(c);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationIsConfinedAndKeepsItsOperandEffects(bool environment)
    {
        var source = "group Helpers\n    public func box<s/T>(value: T) -> s/T\n        s is object\n        T is ObjectPayload\n        return value@move@s\n" +
            "    public func input() -> i32\n        Console.writeLine(\"effect\")\n        return 7\n" +
            "contract Maker\n    func create(self: ref/Self) -> rc/i32\n        effect confined\n" +
            "struct Factory\n    Self is Maker\n    public func create(self: ref/Self) -> rc/i32\n" +
            "        return Helpers.box<rc/i32>(" + (environment ? "Helpers.input()" : "7") + ")\nlet factory = Factory.init()\nlet value = factory.create()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code is DiagnosticCode.UnresolvedBinding_Kd or DiagnosticCode.NoApplicableOverload_Kd);
        if (environment)
        {
            MinimalEmissionTest.AssertEffectBoundRejected(c);
        }
        else
        {
            Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        }
    }
}
