// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 13.6.2, 22.2.3: each feasible Semantics case destroys only initialized parts that were not transferred.
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        if (valid)
        {
            Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
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

    // SPEC 15.1.6, 13.5.5, 8.6 (DECISIONS BU-14): a part borrowed past a pair layer admitting owner stays in the owned Subject, which
    // destroys it in the owner case; the confined bound excludes Payload's drop. The MIR's effects reject it, and the old analysis,
    // which judges no borrow into an owned Subject, reports its located limit (until R6-check) instead of counting the Subject's parts.
    [Fact]
    public void APartBorrowedPastAPairLayerIsDestroyedWithTheSubject()
    {
        const string Source = "struct Payload\n    drop => Console.writeLine(\"drop\")\ngroup Helpers\n" +
            "    public func peek<s/T>(o: Option<s/Option<Payload> during a>, marker: s/T) -> i32\n        s is owner or ref\n" +
            "        match o@move\n            .Some(.Some(let n)) => ()\n            .Some(_) => $abort(\"other\")\n            .None => $abort(\"none\")\n        return 1\n" +
            "contract Runner\n    func run(self: ref/Self, o: Option<Option<Payload>>) -> i32\n        effect confined\n" +
            "struct Worker\n    Self is Runner\n    public func run(self: ref/Self, o: Option<Option<Payload>>) -> i32\n        let m: i32 = 0\n        return Helpers.peek(o@move, m)\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, static x => x.Code is DiagnosticCode.IncompatibleContractImplementation_Kd or DiagnosticCode.Unsupported_Kd);
    }
}
