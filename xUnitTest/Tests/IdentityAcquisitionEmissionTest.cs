// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class IdentityAcquisitionEmissionTest
{
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Bool", "let flag = true\nif flag@bool and flag => Console.writeLine(\"ok\")", "ok\n" },
        { "Char", "let value = 'a'\nif value@char == value => Console.writeLine(\"ok\")", "ok\n" },
        { "String", "let source = \"value\"\nlet value = source@move@string\nConsole.writeLine(value)", "value\n" },
        { "Tuple", "let source = (\"value\", 42)\nlet value = source@move@(string, i32)\nConsole.writeLine(value.0)", "value\n" },
        { "Array", "let source: [1 of string] = [\"value\"]\nlet value = source@move@[1 of string]\nConsole.writeLine(value[0])", "value\n" },
        { "CopyType", "let source: i32 = 42\nlet value = source@i32\nif value == source => Console.writeLine(\"value\")", "value\n" },
        { "Owner", "let source = \"value\"\nlet value = source@move@owner\nConsole.writeLine(value)", "value\n" },
        { "ExplicitOwner", "let source = \"value\"\nlet value = source@move@owner/string\nConsole.writeLine(value)", "value\n" },
        { "OwnerNumeric", "let source: i32 = 42\nif source@owner/u8 == 42 and 5000000000@owner/f64 == 5000000000.0 => Console.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void OrdinaryIdentityAcquisitionExecutes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("IdentityAcquisition" + name, source, stdout);

    [Theory]
    [InlineData("Partial", "let pair = (\"first\", \"last\")\nlet first = pair.0@move@string\nlet last = pair.1@move", "first=1;last=1", new[] { 1, 0 })]
    [InlineData("Repair", "var pair = (\"first\", \"last\")\nlet first = pair.0@move@string\npair.0 = \"new\"\nlet whole = pair@move", "first=1;last=1;new=1", new[] { 1, 2, 0 })]
    [InlineData("SelfReplace", "var value = \"value\"\nvalue = value@move@string", "value=1", new[] { 0 })]
    [InlineData("Temporary", "let value = (\"first\", \"last\")@owner@(string, string)", "first=1;last=1", new[] { 1, 0 })]
    [InlineData("Parameter", "func take(value: (string, string)) -> string => value.0@move\nlet result = take((\"first\", \"last\"))", "first=1;last=1", new[] { 1, 0 })]
    [InlineData("Deferred", "let value = \"value\"\ndefer\n    let taken = value@move", "value=1", new[] { 0 })]
    public void AcquisitionTransfersExactlyOneDestructionResponsibility(string name, string source, string counts, int[] order)
    {
        var ir = ScalarEmissionTest.EmitFixture("IdentityAcquisition" + name, source, string.Empty);
        StringEmissionTest.WriteAuditedFixture("IdentityAcquisition" + name, source, ir, string.Empty, counts, order: order);
    }

    [Theory]
    [InlineData("let value = \"value\"\nlet taken = value@move\nlet twice = value@move", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let value = \"value\"\nlet taken = value@move\nlet twice = value@move@string", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let pair = (\"first\", \"last\")\nlet first = pair.0@move\nlet whole = pair@move", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f()\n    let value = \"value\"\n    return\n    let first = value@move\n    let twice = value@move\nf()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let value: i32 = 1\nlet taken = value@move\nlet twice = value", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let value = \"value\"\nlet same = value == value@move", OwnershipFailure.ComparisonLoanConflict)]
    [InlineData("func same(a: ref/string, b: string) => ()\nlet value = \"value\"\nsame(value, value@move)", OwnershipFailure.ComparisonLoanConflict)]
    [InlineData("var value: string\nlet taken = value@move", OwnershipFailure.UninitializedUse)]
    public void IdentityRetainsOrdinaryOwnershipErrors(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    // SPEC 13.5.3, 15.1.5: a Type-only target Copies a Copy Place and never transfers a Non-Copy Place.
    [Theory]
    [InlineData("let value = \"value\"\nlet taken = value@string")]
    [InlineData("let pair = (\"first\", \"last\")\nlet first = pair.0@string")]
    [InlineData("let source: [1 of string] = [\"value\"]\nlet value = source@[1 of string]")]
    public void TypeOnlyTargetRequiresATransferForANonCopyPlace(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.TransferRequired_Kd);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("Unit", "let value = ()@owner@()\nConsole.writeLine(\"ok\")")]
    [InlineData("Empty", "let value: [0 of string] = []\nlet taken = value@move@owner@[0 of string]\nConsole.writeLine(\"ok\")")]
    [InlineData("CopyArray", "let value: [2 of i32] = [1, 2]\nlet copy = value@[2 of i32]\nif copy[0] == value[0] => Console.writeLine(\"ok\")")]
    [InlineData("CopyIdentity", "let value: [2 of i32] = [1, 2]\nlet moved = value@owner\nif moved[0] == 1 => Console.writeLine(\"ok\")")]
    [InlineData("Snapshot", "var value: i32 = 1\nlet sum = value@i32 + value++\nif sum == 2 and value == 2 => Console.writeLine(\"ok\")")]
    [InlineData("Once", "func get() -> string\n    Console.writeLine(\"ok\")\n    return \"value\"\nlet value = get()@owner@string")]
    [InlineData("Selection", "let value = if true => \"ok\"@owner else => \"bad\"@owner\nConsole.writeLine(value)")]
    [InlineData("Abrupt", "func get() -> string\n    (return \"ok\")@owner\nConsole.writeLine(get())")]
    [InlineData("Grouped", "let text = \"ok\"\nlet value = text@move@((owner))@((string))\nConsole.writeLine(value)")]
    public void BoundariesAndEvaluationOrderExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("IdentityAcquisition" + name, source, "ok\n");

    [Theory]
    [InlineData("binding")]
    [InlineData("target")]
    [InlineData("place")]
    public void CorruptIdentityPlansAreRejectedAndRebindingRestoresThem(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let value = \"value\"\nlet taken = value@move");
        Assert.True(c.Emission.Validate(out var error), error);
        var identities = c.Ownership.Bodies[0].Identities!;
        var identity = Assert.Single(identities);
        if (defect == "binding")
        {
            identity.Source.ConversionBinding = ConversionBinding.Literal;
        }
        else if (defect == "target")
        {
            identity.Source.Right.BoundType = BoundType.Boolean;
        }
        else
        {
            identities[0] = identity with { Place = -1 };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out error), error);
    }

    [Fact]
    public void RebindingAndReloadPreserveIdentityAcquisition()
    {
        var c = MinimalEmissionTest.Analyze("func take(value: (string, i32)) -> string => value.0@move\nlet pair = (\"ok\", 42)\nConsole.writeLine(take(pair@move@(string, i32)))");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        using var rebound = new StringWriter();
        Assert.True(c.Emission.WriteIr(rebound, out error), error);
        Assert.Equal(original.ToString(), rebound.ToString());
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        restored.Bind();
        restored.Binding.CheckStartup(OutputKind.Application);
        restored.Ownership.Analyze();
        using var writer = new StringWriter();
        Assert.True(restored.Emission.WriteIr(writer, out error), error);
        Assert.Equal(original.ToString(), writer.ToString());
        ScalarEmissionTest.WriteFixture("IdentityAcquisitionReload", writer.ToString(), "ok\n");
    }

    [Fact]
    public void WarmIdentityAnalysisAndWritingAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("func take(value: (string, i32)) -> string => value.0@move\nlet pair = (\"ok\", 42)\nConsole.writeLine(take(pair@move@(string, i32)))");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Identity acquisition analysis or writing failed.");
            }
        }));
    }
}
