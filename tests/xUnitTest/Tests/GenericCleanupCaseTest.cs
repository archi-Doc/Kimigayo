// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 13.5.8, 22.1.2.4: each feasible Semantics case destroys only initialized parts that were not transferred.
public class GenericCleanupCaseTest
{
    [Theory]
    [InlineData("ref", true, true)]
    [InlineData("obj", true, false)]
    [InlineData("ref", false, true)]
    [InlineData("obj", false, true)]
    public void AReturnedPartLeavesOnlyTheOtherPartsDestruction(string mode, bool partial, bool valid)
    {
        var element = mode == "ref" ? "ref/Payload during a" : "obj/Payload";
        var result = partial ? element : $"({element}, {element})";
        var source = "struct Payload\n    drop => Console.writeLine(\"drop\")\ngroup Helpers\n" +
            "    public func select<s/T>(pair: (s/T, s/T)) -> " + (partial ? "s/T" : "(s/T, s/T)") + "\n" +
            "        s is ref or obj\n        return " + (partial ? "pair.0@move" : "pair@move") + "\n" +
            $"contract Runner\n    func run(self: ref/Self, pair: ({element}, {element})) -> {result}\n        effect confined\n" +
            "struct Worker\n    Self is Runner\n" +
            $"    public func run(self: ref/Self, pair: ({element}, {element})) -> {result}\n        return Helpers.select(pair@move)\n()";
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

        var body = Assert.Single(c.Ownership.Bodies, static x => x.Function.Name == "select");
        if (partial)
        {
            Assert.Contains(body.CaseDestructions, static x => x.Type?.Semantics == SemanticsKind.Obj && (x.Cases & 2UL) != 0);
            Assert.DoesNotContain(body.CaseDestructions, static x => x.Type?.Kind == BoundTypeKind.Tuple && (x.Cases & 2UL) != 0);
            // A ref component Copies, so its case keeps the whole tuple without owning a payload destruction.
            Assert.Contains(body.CaseDestructions, static x => x.Type?.Kind == BoundTypeKind.Tuple && x.Cases == 1UL);
        }
        else
        {
            Assert.Empty(body.CaseDestructions);
        }
    }
}
