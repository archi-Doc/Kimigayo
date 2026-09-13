// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class MatchEmissionTest
{
    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Boolean", "let b = true\nmatch b\n    true => writeLine(\"yes\")\n    false => writeLine(\"no\")", "yes\n" },
        { "FalseFirst", "let b = true\nmatch b\n    false => writeLine(\"no\")\n    true => writeLine(\"yes\")", "yes\n" },
        { "Integer", "let n = 2\nmatch n\n    1 => writeLine(\"one\")\n    2 => writeLine(\"two\")\n    _ => writeLine(\"other\")", "two\n" },
        { "ScalarResult", "let n = match 2\n    1 => 10\n    2 => 20\n    _ => 30\nif n == 20 => writeLine(\"ok\")", "ok\n" },
        { "Binding", "let n = match 7\n    0 => 0\n    var x\n        x += 1\n        yield x\nif n == 8 => writeLine(\"ok\")", "ok\n" },
        { "Unit", "match ()\n    () => writeLine(\"ok\")", "ok\n" },
        { "String", "match \"a\"\n    \"b\" => writeLine(\"bad\")\n    \"a\" => writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Empty", "match \"\"\n    \"\" => writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "StringResult", "let text = match \"a\"\n    \"b\" => \"bad\"\n    let other => other\nwriteLine(text)", "a\n" },
        { "Covered", "let n = match 0\n    _ => 1\n    0 => 2\nif n == 1 => writeLine(\"ok\")", "ok\n" },
        { "Duplicate", "let text = match 0\n    0 => \"ok\"\n    -0 => \"bad\"\n    _ => \"other\"\nwriteLine(text)", "ok\n" },
        { "BoolCovered", "let text = match false\n    true => \"bad\"\n    false => \"ok\"\n    _ => \"other\"\nwriteLine(text)", "ok\n" },
        { "Nested", "let text = match 1\n    0 => \"bad\"\n    _ => match true\n        true => \"ok\"\n        false => \"bad\"\nwriteLine(text)", "ok\n" },
        { "Return", "func f(n: i32) -> string\n    match n\n        0 => return \"zero\"\n        _ => return \"other\"\nwriteLine(f(0))", "zero\n" },
        { "Loop", "var n = 0\nwhile n < 3\n    match n\n        1 => writeLine(\"one\")\n        _ => ()\n    n += 1", "one\n" },
        { "Grouping", "match 1\n    ((1)) => writeLine(\"ok\")\n    (let other) => ()", "ok\n" },
        { "AllReturnCovered", "func f() -> i32\n    match 0\n        _ => return 1\n        0 => ()\n    return 2\nif f() == 1 => writeLine(\"ok\")", "ok\n" },
        { "CoveredNestedPhi", "let text = match 0\n    _ => \"ok\"\n    0 => if true => \"bad\" else => \"other\"\nwriteLine(text)", "ok\n" },
        { "CoveredCall", "func echo(text: string) -> string => text\nlet text = match 0\n    _ => \"ok\"\n    0 => echo(\"bad\")\nwriteLine(text)", "ok\n" },
        { "ComparisonLoan", "let text = \"a\"\nif text == (match 0\n    0 => \"a\"\n    _ => \"b\"\n) => writeLine(text)", "a\n" },
        { "Unicode", "match \"日本語\\0\"\n    \"日本語\" => writeLine(\"bad\")\n    \"日本語\\0\" => writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Continue", "var n = 0\nwhile n < 3\n    n += 1\n    match n\n        1 => continue\n        2 => writeLine(\"two\")\n        _ => exit", "two\n" },
        { "SubjectOnce", "func make() -> i32\n    writeLine(\"subject\")\n    return 2\nmatch make()\n    0 => ()\n    1 => ()\n    _ => writeLine(\"ok\")", "subject\nok\n" },
        { "Snapshot", "var n = 1\nlet result = match n\n    1\n        defer => n = 2\n        yield n\n    _ => 0\nif result == 1 and n == 2 => writeLine(\"ok\")", "ok\n" },
        { "NeverSubject", "func f()\n    match (return)\n        _ => writeLine(\"bad\")\nf()\nwriteLine(\"ok\")", "ok\n" },
        { "Raw", "match \"a\"\n    \"a\" => writeLine(\"ok\")\n    \"\"\"a\"\"\" => writeLine(\"bad\")\n    _ => ()", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void DispatchAndResults(string name, string source, string expected)
    {
        ScalarEmissionTest.EmitFixture("Match" + name, source, expected);
    }

    public static TheoryData<string, string, string, string> Audits => new()
    {
        { "OwnedLiteral", "let text = \"a\"\nmatch text\n    \"a\" => writeLine(\"ok\")\n    _ => ()", "ok\n", "a=1;ok=1" },
        { "OwnedBinding", "let text = \"a\"\nmatch text\n    let other => writeLine(other)", "a\n", "a=1" },
        { "OwnedWildcard", "match \"a\"\n    _ => writeLine(\"ok\")", "ok\n", "a=1;ok=1" },
        { "OwnedResult", "let result = match \"a\"\n    let other => other\nwriteLine(result)", "a\n", "a=1" },
        { "OwnedCall", "func echo(text: string) -> string => text\nmatch echo(\"a\")\n    let other => writeLine(other)", "a\n", "a=1" },
        { "OwnedSelection", "match (if true => \"a\" else => \"b\")\n    let other => writeLine(other)", "a\n", "a=1;b=0" },
        { "Replacement", "var text = \"a\"\ntext = match text\n    \"b\" => \"c\"\n    let other => other\nwriteLine(text)", "a\n", "a=1;b=0;c=0" },
        { "OwnedLoop", "var n = 0\nwhile n < 3\n    match \"a\"\n        \"b\" => ()\n        let text => writeLine(text)\n    n += 1", "a\na\na\n", "a=3;b=0" },
        { "Defer", "let result = match \"subject\"\n    let text\n        defer => writeLine(\"cleanup\")\n        yield text\nwriteLine(result)", "cleanup\nsubject\n", "subject=1;cleanup=1" },
        { "DeferredMatch", "var n = 0\nwhile n < 2\n    defer\n        match \"a\"\n            let text => writeLine(text)\n    n += 1", "a\na\n", "a=2" },
        { "EmptySubject", "match \"\"\n    \"\" => writeLine(\"ok\")\n    _ => ()", "ok\n", "=1;ok=1" },
        { "ConditionalBinding", "match \"a\"\n    var text\n        if true => writeLine(text)\n        text = \"b\"\n        writeLine(text)", "a\nb\n", "a=1;b=1" },
        { "CoveredFlags", "match 0\n    _ => writeLine(\"ok\")\n    0\n        var text = \"a\"\n        if true => writeLine(text)\n        text = \"b\"\n        writeLine(text)", "ok\n", "ok=1;a=0;b=0" },
        { "SharedResult", "var n = 0\nwhile n < 2\n    defer\n        let text = match n\n            1 => \"one\"\n            _ => \"other\"\n        writeLine(text)\n    n += 1", "one\nother\n", "one=1;other=1" },
    };

    [Theory]
    [MemberData(nameof(Audits))]
    public void SubjectResponsibility(string name, string source, string expected, string destructions)
    {
        name = "Match" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, expected);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, expected, destructions);
    }

    [Theory]
    [InlineData("match 0\n    _ => ()\n    0 => 1.0 + 2.0")]
    [InlineData("let text = \"a\"\ntext == (match text\n    _ => \"a\"\n)")]
    [InlineData("match \"a\"\n    let text\n        writeLine(text)\n        writeLine(text)")]
    [InlineData("match \"a\"\n    _ if true => ()\n    _ => ()")]
    public void RejectsUnsupportedAndConflictingArms(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("regular")]
    [InlineData("covered")]
    [InlineData("abrupt")]
    public void WarmAnalysisAndWritingAllocateNothing(string scenario)
    {
        var source = scenario switch
        {
            "abrupt" => "func f()\n    match (return)\n        _ => writeLine(\"unused\")\nf()",
            "covered" => "match 0\n    _ => ()\n    0\n        var text = \"a\"\n        if true => writeLine(text)\n        text = \"b\"",
            _ => "func echo(text: string) -> string => text\nlet result = match echo(\"a\")\n    \"b\" => \"other\"\n    let text => text\nwriteLine(result)",
        };
        var c = MinimalEmissionTest.Analyze(source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Ownership.Analyze().IsVerified;
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(valid);
    }

    [Theory]
    [InlineData("i8", "-128")]
    [InlineData("u8", "255")]
    [InlineData("i16", "-32768")]
    [InlineData("u16", "65535")]
    [InlineData("i32", "-2147483648")]
    [InlineData("u32", "4294967295")]
    [InlineData("i64", "-9223372036854775808")]
    [InlineData("u64", "18446744073709551615")]
    [InlineData("isize", "-9223372036854775808")]
    [InlineData("usize", "18446744073709551615")]
    public void FullWidthPatterns(string type, string literal)
    {
        ScalarEmissionTest.EmitFixture("MatchWidth" + type, $"func f(n: {type}) -> string\n    return match n\n        {literal} => \"ok\"\n        _ => \"bad\"\nwriteLine(f({literal}))", "ok\n");
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("test_input")]
    [InlineData("arm_edge")]
    [InlineData("acquisition")]
    [InlineData("arrival")]
    public void MalformedPlansFailBeforeWriting(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let result = match \"a\"\n    \"b\" => \"other\"\n    let text => text\nwriteLine(result)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var match = body.Matches[0];
        var test = body.MatchArms[0].Test;
        if (defect == "subject")
        {
            var id = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.InitializeSubject);
            body.OperationStorage[id] = body.Operations[id] with { Input = match.Subject };
        }
        else if (defect == "test_input")
        {
            body.OperationStorage[test] = body.Operations[test] with { Input = match.Subject };
        }
        else if (defect == "arm_edge")
        {
            var id = body.EdgeStorage.FindIndex(x => x.Kind == OwnershipEdgeKind.MatchArm);
            body.EdgeStorage[id] = body.Edges[id] with { To = body.MatchArms[1].Test };
        }
        else if (defect == "acquisition")
        {
            var id = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.AcquirePattern);
            body.OperationStorage[id] = body.Operations[id] with { Acquisition = AcquisitionKind.Copy };
        }
        else
        {
            var arrival = body.ResultArrivals[0];
            body.ResultArrivals[0] = arrival with { Write = test };
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Fact]
    public void SubjectAliasesAcquiredStorageAndFinalArmHasNoTest()
    {
        var c = MinimalEmissionTest.Analyze("func echo(text: string) -> string => text\nmatch echo(\"a\")\n    \"\" => ()\n    let text => writeLine(text)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies.Single(x => x.Matches.Count > 0);
        var function = Enumerable.Range(0, module.FunctionCount).Select(module.GetFunction).Single(x => x.SlotAddresses.Count == body.Places.Count);
        var initial = body.Operations.Single(x => x.Kind == OwnershipOperationKind.InitializeSubject);
        Assert.Equal(function.SlotAddresses[initial.Input], function.SlotAddresses[initial.Place]);
        Assert.DoesNotContain(function.Slots, x => x.Place == initial.Place);
        var initialId = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.InitializeSubject);
        Assert.DoesNotContain(function.Instructions, x => x.Operation == initialId && x.Opcode == EmissionOpcode.MoveString);
        Assert.Single(function.Instructions, x => x.Opcode == EmissionOpcode.StringPattern);
        Assert.Empty(function.LiveFlags);
    }

    [Theory]
    [InlineData("func f()\n    match (return)\n        _ => 1.0 + 2.0\nf()")]
    public void NoncompletingSubjectsStillCheckArms(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("Subject", "func f() -> string\n    var n = 2147483647\n    n += 1\n    return \"bad\"\nmatch f()\n    _ => writeLine(\"after\")", 3, 5, "bad=0;after=0")]
    [InlineData("Cleanup", "let text = match \"subject\"\n    _\n        defer\n            var n = 2147483647\n            n += 1\n        yield \"secured\"\nwriteLine(text)", 5, 13, "subject=0;secured=0")]
    public void AbortPreventsLaterAcquisitionAndCleanup(string name, string source, int line, int column, string destructions)
    {
        name = "MatchAbort" + name;
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture(name, source, ir, string.Empty, destructions, 1, stderr);
    }

    [Theory]
    [InlineData("Subject", "func f() -> string\n    loop => ()\nmatch f()\n    _ => writeLine(\"bad\")")]
    [InlineData("Cleanup", "writeLine(match \"subject\"\n    _\n        defer => loop => ()\n        yield \"secured\"\n)")]
    public void DivergenceHasNoResultDelivery(string name, string source)
    {
        var ir = ScalarEmissionTest.EmitFixture("MatchDivergent" + name, source, string.Empty, timeoutMilliseconds: 300);
        Assert.DoesNotContain("mustprogress", ir);
    }

    [Fact]
    public void ArmCleanupPrecedesRemainingSubjectAndResultDelivery()
    {
        const string Source = "let result = match \"subject\"\n    _\n        let local = \"local\"\n        defer => writeLine(\"cleanup\")\n        yield \"result\"\nwriteLine(result)";
        var ir = ScalarEmissionTest.EmitFixture("MatchCleanupOrder", Source, "cleanup\nresult\n");
        StringEmissionTest.WriteAuditedFixture("MatchCleanupOrder", Source, ir, "cleanup\nresult\n", "subject=1;local=1;cleanup=1;result=1", order: [2, 1, 0, 3]);
    }

    [Fact]
    public void CoveredArmKeepsDiagnosticsButContributesNoStorageOrFlags()
    {
        const string Source = "match 0\n    _ => ()\n    0\n        var text = \"a\"\n        if true => writeLine(text)\n        text = \"b\"";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var function = module.GetFunction(0);
        Assert.Empty(function.LiveFlags);
        Assert.Empty(function.Slots);
        Assert.DoesNotContain(function.Instructions, x => x.Opcode is EmissionOpcode.StoreStaticString or EmissionOpcode.MoveString or EmissionOpcode.Phi);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong_source")]
    [InlineData("binding")]
    public void ScalarSnapshotPlansAreValidated(string defect)
    {
        var c = MinimalEmissionTest.Analyze("let unrelated = 123\nlet result = match 7\n    let n => n\nif result == 7 => ()");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var id = body.OperationStorage.FindIndex(x => x.Kind == (defect == "binding" ? OwnershipOperationKind.AcquirePattern : OwnershipOperationKind.InitializeSubject));
        if (defect == "wrong_source")
        {
            var unrelated = body.Values.FindIndex(x => x.Kind == OwnershipValueKind.Constant && x.Constant == 123);
            body.ValueOperands[body.Values[id].Start] = unrelated;
        }
        else
        {
            body.Values[id] = default;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }
}
