// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class MatchEmissionTest
{
    // SPEC 14.8.1 (DECISIONS BU-14): a Wildcard at a pair layer of an owned Subject or item selects nothing, so the old path judges and
    // generates it as before, in the owner and uniq instances: a Tuple element, a whole Subject, a for slot `_` and a structural test.
    private const string PairWildcard = "func second<s/T>(pair: (s/T, i32)) -> i32\n    s is owner or uniq\n    match pair@move\n        (_, let n) => return n\n" +
        "func only<s/T>(pair: (s/T, i32)) -> i32\n    s is owner\n    match pair@move\n        (_, let n) => return n\n" +
        "func has<s/T>(o: s/Option<T>, marker: s/T) -> bool\n    s is owner or uniq\n    match o@move\n        .Some(_) => return true\n        .None => return false\n" +
        "func whole<s/T>(value: s/T) -> i32\n    s is owner or uniq\n    match value@move\n        _ => return 3\n" +
        "func sum<s/T>(items: Array<(s/T, i32)>) -> i32\n    s is owner or uniq\n    var total: i32 = 0\n    for (_, n) in items@move\n        total += n\n    return total\n" +
        "var n: i32 = 1\nvar p: i32 = 2\nlet five: i32 = 5\nvar v: Option<i32> = .Some(1)\nvar m: i32 = 0\nlet none: Option<i32> = .None\nlet k: i32 = 0\n" +
        "let a = second((n@uniq, 7)) + second((five, 8)) + only((five, 9))\nlet b = has(v@uniq, m@uniq) and not has(none, k)\nlet c = whole(n@uniq) + whole(five)\n" +
        "let d = sum([(five, 2), (five, 3)]) + sum([(n@uniq, 4), (p@uniq, 6)])\nrequire a == 24 and b and c == 6 and d == 15 else => $abort(\"wildcard\")\n" +
        "Console.writeLine(\"ok\")";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "Boolean", "let b = true\nmatch b\n    true => Console.writeLine(\"yes\")\n    false => Console.writeLine(\"no\")", "yes\n" },
        { "FalseFirst", "let b = true\nmatch b\n    false => Console.writeLine(\"no\")\n    true => Console.writeLine(\"yes\")", "yes\n" },
        { "Integer", "let n = 2\nmatch n\n    1 => Console.writeLine(\"one\")\n    2 => Console.writeLine(\"two\")\n    _ => Console.writeLine(\"other\")", "two\n" },
        { "ScalarResult", "let n = match 2@move\n    1 => 10\n    2 => 20\n    _ => 30\nif n == 20 => Console.writeLine(\"ok\")", "ok\n" },
        { "Binding", "let n = match 7@move\n    0 => 0\n    var x\n        x += 1\n        yield x\nif n == 8 => Console.writeLine(\"ok\")", "ok\n" },
        { "Unit", "match ()@move\n    () => Console.writeLine(\"ok\")", "ok\n" },
        { "String", "match \"a\"@move\n    \"b\" => Console.writeLine(\"bad\")\n    \"a\" => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Empty", "match \"\"@move\n    \"\" => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "StringResult", "let text = match \"a\"@move\n    \"b\" => \"bad\"\n    let other => other@move\nConsole.writeLine(text)", "a\n" },
        { "Covered", "let n = match 0@move\n    _ => 1\n    0 => 2\nif n == 1 => Console.writeLine(\"ok\")", "ok\n" },
        { "Duplicate", "let text = match 0@move\n    0 => \"ok\"\n    -0 => \"bad\"\n    _ => \"other\"\nConsole.writeLine(text)", "ok\n" },
        { "BoolCovered", "let text = match false@move\n    true => \"bad\"\n    false => \"ok\"\n    _ => \"other\"\nConsole.writeLine(text)", "ok\n" },
        { "Nested", "let text = match 1@move\n    0 => \"bad\"\n    _ => match true@move\n        true => \"ok\"\n        false => \"bad\"\nConsole.writeLine(text)", "ok\n" },
        { "Return", "func f(n: i32) -> string\n    match n\n        0 => return \"zero\"\n        _ => return \"other\"\nConsole.writeLine(f(0))", "zero\n" },
        { "Loop", "var n = 0\nwhile n < 3\n    match n\n        1 => Console.writeLine(\"one\")\n        _ => ()\n    n += 1", "one\n" },
        { "Grouping", "match 1@move\n    ((1)) => Console.writeLine(\"ok\")\n    (let other) => ()", "ok\n" },
        { "AllReturnCovered", "func f() -> i32\n    match 0@move\n        _ => return 1\n        0 => ()\n    return 2\nif f() == 1 => Console.writeLine(\"ok\")", "ok\n" },
        { "CoveredNestedPhi", "let text = match 0@move\n    _ => \"ok\"\n    0 => if true => \"bad\" else => \"other\"\nConsole.writeLine(text)", "ok\n" },
        { "CoveredCall", "func echo(text: string) -> string => text@move\nlet text = match 0@move\n    _ => \"ok\"\n    0 => echo(\"bad\")\nConsole.writeLine(text)", "ok\n" },
        { "ComparisonLoan", "let text = \"a\"\nif text == (match 0@move\n    0 => \"a\"\n    _ => \"b\"\n) => Console.writeLine(text)", "a\n" },
        { "Unicode", "match \"日本語\\0\"@move\n    \"日本語\" => Console.writeLine(\"bad\")\n    \"日本語\\0\" => Console.writeLine(\"ok\")\n    _ => ()", "ok\n" },
        { "Continue", "var n = 0\nwhile n < 3\n    n += 1\n    match n\n        1 => continue\n        2 => Console.writeLine(\"two\")\n        _ => exit", "two\n" },
        { "SubjectOnce", "func make() -> i32\n    Console.writeLine(\"subject\")\n    return 2\nmatch make()@move\n    0 => ()\n    1 => ()\n    _ => Console.writeLine(\"ok\")", "subject\nok\n" },
        { "Snapshot", "var n = 1\nlet result = match n\n    1\n        defer => n = 2\n        yield n\n    _ => 0\nif result == 1 and n == 2 => Console.writeLine(\"ok\")", "ok\n" },
        { "NeverSubject", "func f()\n    match (return)\n        _ => Console.writeLine(\"bad\")\nf()\nConsole.writeLine(\"ok\")", "ok\n" },
        { "Raw", "match \"a\"@move\n    \"a\" => Console.writeLine(\"ok\")\n    \"\"\"a\"\"\" => Console.writeLine(\"bad\")\n    _ => ()", "ok\n" },
        { "PairWildcard", PairWildcard, "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void DispatchAndResults(string name, string source, string expected)
    {
        ScalarEmissionTest.EmitFixture("Match" + name, source, expected);
    }

    public static TheoryData<string, string, string, string> Audits => new()
    {
        { "OwnedLiteral", "let text = \"a\"\nmatch text@move\n    \"a\" => Console.writeLine(\"ok\")\n    _ => ()", "ok\n", "a=1;ok=1" },
        { "OwnedBinding", "let text = \"a\"\nmatch text@move\n    let other => Console.writeLine(other)", "a\n", "a=1" },
        { "OwnedWildcard", "match \"a\"@move\n    _ => Console.writeLine(\"ok\")", "ok\n", "a=1;ok=1" },
        { "OwnedResult", "let result = match \"a\"@move\n    let other => other@move\nConsole.writeLine(result)", "a\n", "a=1" },
        { "OwnedCall", "func echo(text: string) -> string => text@move\nmatch echo(\"a\")@move\n    let other => Console.writeLine(other)", "a\n", "a=1" },
        { "OwnedSelection", "match (if true => \"a\" else => \"b\")@move\n    let other => Console.writeLine(other)", "a\n", "a=1;b=0" },
        { "Replacement", "var text = \"a\"\ntext = match text@move\n    \"b\" => \"c\"\n    let other => other@move\nConsole.writeLine(text)", "a\n", "a=1;b=0;c=0" },
        { "OwnedLoop", "var n = 0\nwhile n < 3\n    match \"a\"@move\n        \"b\" => ()\n        let text => Console.writeLine(text)\n    n += 1", "a\na\na\n", "a=3;b=0" },
        { "Defer", "let result = match \"subject\"@move\n    let text\n        defer => Console.writeLine(\"cleanup\")\n        yield text@move\nConsole.writeLine(result)", "cleanup\nsubject\n", "subject=1;cleanup=1" },
        { "DeferredMatch", "var n = 0\nwhile n < 2\n    defer\n        match \"a\"@move\n            let text => Console.writeLine(text)\n    n += 1", "a\na\n", "a=2" },
        { "EmptySubject", "match \"\"@move\n    \"\" => Console.writeLine(\"ok\")\n    _ => ()", "ok\n", "=1;ok=1" },
        { "ConditionalBinding", "match \"a\"@move\n    var text\n        if true => Console.writeLine(text)\n        text = \"b\"\n        Console.writeLine(text)", "a\nb\n", "a=1;b=1" },
        { "CoveredFlags", "match 0@move\n    _ => Console.writeLine(\"ok\")\n    0\n        var text = \"a\"\n        if true => Console.writeLine(text)\n        text = \"b\"\n        Console.writeLine(text)", "ok\n", "ok=1;a=0;b=0" },
        { "SharedResult", "var n = 0\nwhile n < 2\n    defer\n        let text = match n\n            1 => \"one\"\n            _ => \"other\"\n        Console.writeLine(text)\n    n += 1", "one\nother\n", "one=1;other=1" },
    };

    [Theory]
    [MemberData(nameof(Audits))]
    public void SubjectResponsibility(string name, string source, string expected, string destructions)
    {
        name = "Match" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, expected);
        StringEmissionTest.WriteAuditedFixture(name, ir, expected, destructions);
    }

    [Theory]
    [InlineData("match 0@move\n    _ => ()\n    0 => 1.0 + 2.0", true)]
    [InlineData("let text = \"a\"\ntext == (match text\n    _ => \"a\"\n)", true)]
    [InlineData("match \"a\"@move\n    let text\n        _ = text@move\n        Console.writeLine(text)")]
    [InlineData("match 1.0\n    _ if true => ()\n    _ => ()", true)]
    public void ArmSupportPreservesConflictRejection(string source, bool emitted = false)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("regular")]
    [InlineData("covered")]
    [InlineData("abrupt")]
    public void WarmAnalysisAndWritingAllocateNothing(string scenario)
    {
        var source = scenario switch
        {
            "abrupt" => "func f()\n    match (return)\n        _ => Console.writeLine(\"unused\")\nf()",
            "covered" => "match 0@move\n    _ => ()\n    0\n        var text = \"a\"\n        if true => Console.writeLine(text)\n        text = \"b\"",
            _ => "func echo(text: string) -> string => text@move\nlet result = match echo(\"a\")@move\n    \"b\" => \"other\"\n    let text => text@move\nConsole.writeLine(result)",
        };
        var c = MinimalEmissionTest.Analyze(source);
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.Generate(TextWriter.Null);
        });
        Assert.True(valid);
        Assert.Equal(0, allocated);
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
        ScalarEmissionTest.EmitFixture("MatchWidth" + type, $"func f(n: {type}) -> string\n    return match n\n        {literal} => \"ok\"\n        _ => \"bad\"\nConsole.writeLine(f({literal}))", "ok\n");
    }

    [Theory]
    [InlineData("func f()\n    match (return)\n        _ => 1.0 + 2.0\nf()")]
    public void NoncompletingSubjectsStillCheckFloatingArms(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, true);
    }

    [Theory]
    [InlineData("Subject", "func f() -> string\n    var n = 2147483647\n    n += 1\n    return \"bad\"\nmatch f()\n    _ => Console.writeLine(\"after\")", 3, 5, "bad=0;after=0")]
    [InlineData("Cleanup", "let text = match \"subject\"@move\n    _\n        defer\n            var n = 2147483647\n            n += 1\n        yield \"secured\"\nConsole.writeLine(text)", 5, 13, "subject=0;secured=0")]
    public void AbortPreventsLaterAcquisitionAndCleanup(string name, string source, int line, int column, string destructions)
    {
        name = "MatchAbort" + name;
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture(name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture(name, ir, string.Empty, destructions, 1, stderr);
    }

    [Theory]
    [InlineData("Subject", "func f() -> string\n    loop => ()\nmatch f()\n    _ => Console.writeLine(\"bad\")")]
    [InlineData("Cleanup", "Console.writeLine(match \"subject\"@move\n    _\n        defer => loop => ()\n        yield \"secured\"\n)")]
    public void DivergenceHasNoResultDelivery(string name, string source)
    {
        var ir = ScalarEmissionTest.EmitFixture("MatchDivergent" + name, source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Fact]
    public void ArmCleanupPrecedesRemainingSubjectAndResultDelivery()
    {
        const string Source = "let result = match \"subject\"@move\n    _\n        let local = \"local\"\n        defer => Console.writeLine(\"cleanup\")\n        yield \"result\"\nConsole.writeLine(result)";
        var ir = ScalarEmissionTest.EmitFixture("MatchCleanupOrder", Source, "cleanup\nresult\n");
        StringEmissionTest.WriteAuditedFixture("MatchCleanupOrder", ir, "cleanup\nresult\n", "subject=1;local=1;cleanup=1;result=1", order: [2, 1, 0, 3]);
    }
}
