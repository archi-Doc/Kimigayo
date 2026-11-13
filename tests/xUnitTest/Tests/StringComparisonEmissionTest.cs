// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StringComparisonEmissionTest
{
    private const string Echo = "func echo(text: string) -> string => text@move\n";

    // A string's record, as the library's Storage.StringRepr states it, and SPEC 13.4's rule on the bytes themselves: the sign of the first
    // differing unsigned byte, then of the lengths.
    private const string Bytes = """
        #Layout("C")
        struct StringRepr
            public var data: raw/u8
            public var length: isize
            public var releaseKind: u8

        func order(left: raw/u8, leftLength: isize, right: raw/u8, rightLength: isize) -> i32
            var index: isize = 0
            while index < leftLength and index < rightLength
                unsafe
                    let x = *(left + index)
                    let y = *(right + index)
                    if x != y
                        if x < y => return -1
                        return 1
                index += 1
            if leftLength == rightLength => return 0
            if leftLength < rightLength => return -1
            return 1

        // The Contracts' requirements, called through a Constraint (SPEC 8.6.4): a built-in conformance adds no member (13.4.1).
        func contractOrder<T>(left: ref/T, right: ref/T) -> i32
            T is Comparable
            return left.compare(right)
        func contractEqual<T>(left: ref/T, right: ref/T) -> bool
            T is Equatable
            return left.equals(right)


        """;

    public static TheoryData<string, string, string, string> Fixtures => new()
    {
        { "Same", "let text = \"a\"\nif text == text => Console.writeLine(\"ok\")\nConsole.writeLine(text)", "ok\na\n", "a=1;ok=1" },
        { "Nested", "let text = \"a\"\nif text == (if text == \"a\" => \"a\" else => \"b\") => Console.writeLine(\"ok\")\nConsole.writeLine(text)", "ok\na\n", "a=2;b=0;ok=1" },
        { "Temporary", Echo + "if echo(\"a\") == \"a\" => Console.writeLine(\"ok\")", "ok\n", "a=1;ok=1" },
        { "Ordering", "if \"a\" < \"ab\" and \"ab\" > \"a\" and \"a\" <= \"a\" and \"z\" >= \"a\" and \"a\" != \"b\" => Console.writeLine(\"ok\")", "ok\n", "a=0;ab=0;z=0;b=0;ok=1" },
        { "Empty", "if \"\" == \"\" and \"\" < \"a\" and \"a\" > \"\" and \"\" != \"a\" => Console.writeLine(\"ok\")", "ok\n", "=0;a=0;ok=1" },
        { "Unicode", "if \"z\" < \"日本語\" and \"a\\0b\" < \"a\\0c\" and \"a\\0\" != \"a\" => Console.writeLine(\"ok\")", "ok\n", "z=0;日本語=0;a\0b=0;a\0c=0;a\0=0;a=0;ok=1" },
        { "Parameter", "func equal(a: string, b: string) -> bool => a == b\nif equal(\"a\", \"a\") => Console.writeLine(\"ok\")", "ok\n", "a=2;ok=1" },
        { "Loop", "let text = \"a\"\nvar n = 0\nwhile n < 3\n    if text == \"a\" => n += 1\nConsole.writeLine(text)", "a\n", "a=1" },
        { "ReturnLiteral", "func f() -> string\n    let text = \"a\"\n    text == (return \"x\")\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=1;x=1;bad=0" },
        { "Defer", "var text = \"a\"\ndefer\n    if text == \"a\" => Console.writeLine(\"ok\")", "ok\n", "a=1;ok=1" },
        { "BranchTransfer", "func f(c: bool) -> string\n    let text = \"a\"\n    if text == (if c => (return \"x\") else => \"a\") => return text@move\n    return \"bad\"\nConsole.writeLine(f(false))\nConsole.writeLine(f(true))", "a\nx\n", "a=3;x=1;bad=0" },
        { "Skipped", Echo + "if false and echo(\"skip\") == \"skip\" => Console.writeLine(\"bad\")\nif true or echo(\"skip\") == \"skip\" => Console.writeLine(\"ok\")", "ok\n", "skip=0;bad=0;ok=1" },
        { "RepeatedConditional", Echo + "var n = 0\nwhile n < 4\n    if n == 1 and echo(\"once\") == \"once\" => Console.writeLine(\"ok\")\n    n += 1", "ok\n", "once=1;ok=1" },
        { "LoopExit", "let text = \"a\"\nif text == (loop => exit \"a\") => Console.writeLine(text)", "a\n", "a=2" },
        { "AbandonedCleanup", "func f() -> string\n    var text = \"a\"\n    defer => text = \"b\"\n    text == (return \"x\")\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=1;b=1;x=1;bad=0" },
        { "TwoReturns", "func f(c: bool) -> string\n    let text = \"a\"\n    text == (if c => (return \"x\") else => (return \"y\"))\n    return \"bad\"\nConsole.writeLine(f(true))\nConsole.writeLine(f(false))", "x\ny\n", "a=2;x=1;y=1;bad=0" },
        { "NeverRight", "func f() -> string\n    \"a\" < (return \"x\")\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=0;x=1;bad=0" },
        { "NeverLeft", "func f() -> string\n    (return \"x\") >= \"a\"\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "a=0;x=1;bad=0" },
        {
            "NeverNotEqual",
            "func f() -> string\n    let text = \"a\"\n    (return \"x\") != text\n    return \"bad\"\nConsole.writeLine(f())",
            "x\n", "a=1;x=1;bad=0"
        },
        { "NeverBoth", "func f() -> string\n    (return \"x\") == (return \"y\")\n    return \"bad\"\nConsole.writeLine(f())", "x\n", "x=1;y=0;bad=0" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ComparisonsPreserveOwnership(string name, string source, string stdout, string destructions)
    {
        name = "StringComparison" + name;
        var ir = ScalarEmissionTest.EmitFixture(name, source, stdout);
        StringEmissionTest.WriteAuditedFixture(name, ir, stdout, destructions);
    }

    [Theory]
    [InlineData("func f() -> string\n    let text = \"a\"\n    text == (return text@move)\n    return \"x\"")]
    [InlineData(Echo + "let text = \"a\"\ntext == echo(text@move)")]
    [InlineData("var text = \"a\"\ntext == label work: do\n    defer => text = \"b\"\n    exit to work \"a\"")]
    [InlineData("var text = \"a\"\ntext == (if false\n    text = \"b\"\n    yield \"a\"\nelse => \"a\")")]
    public void ConflictingOperationsAreLanguageErrors(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void ConditionSecuresBoolBeforeDestructionAndBranching()
    {
        const string Source = Echo + "if echo(\"condition\") == \"literal\" => Console.writeLine(\"bad\")\nConsole.writeLine(\"after\")";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonConditionOrder", Source, "after\n");
        StringEmissionTest.WriteAuditedFixture("StringComparisonConditionOrder", ir, "after\n", "condition=1;literal=0;bad=0;after=1", order: [0, 3]);
    }

    [Theory]
    [InlineData("Operand", Echo + "func fail() -> string\n    var n = 2147483647\n    n += 1\n    return \"bad\"\nlet text = \"held\"\ntext == fail()", 4, 5, "held=0;bad=0")]
    [InlineData("Cleanup", "func f() -> string\n    var text = \"held\"\n    defer\n        var n = 2147483647\n        n += 1\n    text == (return \"secured\")\n    return \"bad\"\nConsole.writeLine(f())", 5, 9, "held=0;secured=0;bad=0")]
    public void AbortStopsComparisonAndCleanup(string name, string source, int line, int column, string destructions)
    {
        var stderr = $"Hello.kimi:{line}:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonAbort" + name, source, string.Empty, 1, stderr);
        StringEmissionTest.WriteAuditedFixture("StringComparisonAbort" + name, ir, string.Empty, destructions, 1, stderr);
    }

    [Fact]
    public void DivergentOperandKeepsTheProgramNonterminating()
    {
        const string Source = "func forever() -> string\n    loop => ()\nlet text = \"held\"\ntext == forever()";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonDivergent", Source, string.Empty, observationMilliseconds: 300);
        GeneratedIr.Parse(ir).AssertNoTerminationClaim();
    }

    [Fact]
    public void HelpersCompareDistinctBuffersAndSkipUnneededReads()
    {
        const string Source = "if \"a\" != \"b\" and \"a\" < \"b\" => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("StringComparisonHelperSource", Source, "ok\n");
        const string Entry = "define void @__kimi_start() noreturn #0 {\nentry:\n";
        var equal = Assert.Single(Regex.Matches(ir, @"define internal i1 @(__kimi_f\d+)\(ptr %a0, i64 %a1, ptr %a2, i64 %a3\)")).Groups[1].Value;
        var order = Assert.Single(Regex.Matches(ir, @"define internal i32 @(__kimi_f\d+)\(ptr %a0, i64 %a1, ptr %a2, i64 %a3\)")).Groups[1].Value;
        // A separate helper fixture, not a claim that source syntax constructs Heap strings or these pointers: the library's byte comparisons
        // by name, whose operands no Kimigayo string reaches.
        // External linkage also keeps unknown-input helper bodies observable after O2.
        ir = NativeAllocationAudit.Instrument(ir, $"define internal i1 @{equal}(", $"define i1 @{equal}(");
        ir = NativeAllocationAudit.Instrument(ir, $"define internal i32 @{order}(", $"define i32 @{order}(");
        ir = NativeAllocationAudit.Instrument(ir, Entry, Entry + "  call void @__test_string_comparisons()\n");
        ir += $$"""

            @test_a = private constant [3 x i8] c"a\00b"
            @test_b = private constant [3 x i8] c"a\00b"
            @test_c = private constant [3 x i8] c"a\00c"
            define internal void @__test_string_comparisons() {
            entry:
              %a = call i1 @{{equal}}(ptr @test_a, i64 3, ptr @test_b, i64 3)
              %order = call i32 @{{order}}(ptr @test_a, i64 3, ptr @test_c, i64 3)
              %differentLength = call i1 @{{equal}}(ptr inttoptr (i64 1 to ptr), i64 1, ptr inttoptr (i64 2 to ptr), i64 2)
              %empty = call i32 @{{order}}(ptr null, i64 0, ptr inttoptr (i64 1 to ptr), i64 3)
              %b = icmp slt i32 %order, 0
              %c = xor i1 %differentLength, true
              %d = icmp slt i32 %empty, 0
              %ab = and i1 %a, %b
              %cd = and i1 %c, %d
              %ok = and i1 %ab, %cd
              br i1 %ok, label %success, label %failure
            success:
              ret void
            failure:
              call void @ExitProcess(i32 123)
              unreachable
            }
            """ + "\n";
        ScalarEmissionTest.WriteFixture("StringComparisonHelpers", ir, "ok\n", 0, string.Empty);
    }

    // SPEC 13.4, 13.4.1: every operator, `equals` and `compare` on every ordered pair of 22 strings, Static and Heap, empty, prefixes, NUL bytes
    // and bytes above 0x7F, against a byte loop that states the rule: the first differing unsigned byte decides, then the shorter orders
    // first. `compare` is held to its sign alone. The hash is over `compare`'s sign and the two operators.
    [Fact]
    public void EveryPairAnswersTheSignContract()
    {
        const string Source = Bytes + """
            func expected(left: ref/string, right: ref/string) -> i32
                unsafe
                    let a = left@follow@raw@raw/StringRepr
                    let b = right@follow@raw@raw/StringRepr
                    return order((*a).data, (*a).length, (*b).data, (*b).length)

            func sign(value: i32) -> i32
                if value < 0 => return -1
                if value > 0 => return 1
                return 0

            public func main()
                var cases: Array<string> = [
                    "", "a", "b", "aa", "ab", "abc", "abd", "ab\0", "ab\0c", "é", "z", "zé", "\u(7F)", "\u(80)", "\u(10FFFF)",
                    "01234567", "012345678", "0123456789012345", "0123456789012346"]
                cases.append(Text.toString("abc"))
                cases.append(Text.toString("0123456789012345"))
                cases.append(Text.toString(""))
                var mismatches = 0
                var hash: i64 = 0
                for i in 0..cases.length
                    for j in 0..cases.length
                        let left = cases[i]@ref
                        let right = cases[j]@ref
                        let rule = expected(left, right)
                        let compared = sign(contractOrder(left, right))
                        let equal = left == right
                        let less = left < right
                        hash = (hash * 31 + (compared + 2)@i64 * 4 + (if equal => 2@i64 else => 0@i64) + (if less => 1@i64 else => 0@i64)) & 0xFFFF_FFFF
                        if compared != rule or equal != (rule == 0) or less != (rule < 0) => mismatches += 1
                        if contractEqual(left, right) != (rule == 0) or (left != right) != (rule != 0) => mismatches += 1
                        if (left <= right) != (rule <= 0) or (left > right) != (rule > 0) or (left >= right) != (rule >= 0) => mismatches += 1
                Console.writeLine("pairs \(cases.length * cases.length) mismatches \(mismatches) hash \(hash)")
            """;
        ScalarEmissionTest.EmitFixture("StringComparisonPairs", Source, "pairs 484 mismatches 0 hash 49004486\n");
    }

    // SPEC 13.4, 13.4.1: the same rule on every pair of lengths from 0 to 40, each string at a page's end or start, so at every misalignment
    // and with no-access pages around it: a read past either end faults, and whole words read neither beyond a string nor at an aligned
    // address only. Each pair runs equal and with a byte differing at its first, middle and last common position, both ways.
    [Fact]
    public void BytesAreReadAtAnyAlignmentAndNoFurther()
    {
        const string Source = Bytes + """
            group Pages
                /// Reserves pages, or commits reserved ones.
                ///
                /// - safety: `address` is null or reserved by an earlier call, `kind` is 8192 (reserve) or 4096 (commit) and
                ///   `protect` is 1 (no access) or 4 (read-write). A nonnull result is the start of `size` bytes in whole pages;
                ///   committed read-write pages are its provenance, readable and writable, and reserved ones cannot be touched.
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: raw/u8, size: u64, kind: u32, protect: u32) -> raw/u8

            // One read-write page between two that have no access.
            func page() -> raw/u8
                unsafe
                    let start = Pages.allocate(null, 12288, 8192, 1)
                    require start != null else => $abort("reserve")
                    let middle = Pages.allocate(start + 4096, 4096, 4096, 4)
                    require middle != null else => $abort("commit")
                    return middle

            func fill(start: raw/u8, length: isize)
                var index: isize = 0
                while index < length
                    unsafe
                        *(start + index) = ((index * 37 + 11) % 256)@u8
                    index += 1

            // A string over `length` bytes at `data`, Static, so never released.
            func view(data: raw/u8, length: isize) -> string
                var text = ""
                unsafe
                    let record = text@raw@raw/StringRepr
                    (*record).data = data
                    (*record).length = length
                return text@move

            func wrong(left: raw/u8, leftLength: isize, right: raw/u8, rightLength: isize) -> i64
                let a = view(left, leftLength)
                let b = view(right, rightLength)
                let rule = order(left, leftLength, right, rightLength)
                let compared = contractOrder(a@ref, b@ref)
                var found: i64 = 0
                if (a == b) != (rule == 0) or contractEqual(a@ref, b@ref) != (rule == 0) or (a != b) != (rule != 0) => found += 1
                if (a < b) != (rule < 0) or (a <= b) != (rule <= 0) or (a > b) != (rule > 0) or (a >= b) != (rule >= 0) => found += 1
                if (compared < 0) != (rule < 0) or (compared > 0) != (rule > 0) => found += 1
                return found

            public func main()
                let first = page()
                let second = page()
                var checks = 0
                var found: i64 = 0
                var leftLength: isize = 0
                while leftLength <= 40
                    var rightLength: isize = 0
                    while rightLength <= 40
                        var placement = 0
                        while placement < 4
                            var left = first
                            var right = second
                            unsafe
                                if placement % 2 == 0 => left = first + 4096 - leftLength
                                if placement / 2 == 0 => right = second + 4096 - rightLength
                            fill(left, leftLength)
                            fill(right, rightLength)
                            found += wrong(left, leftLength, right, rightLength)
                            checks += 1
                            let common = if leftLength < rightLength => leftLength else => rightLength
                            var flip: isize = 0
                            while common > 0 and flip < 3
                                var at: isize = 0
                                if flip == 1 => at = common - 1
                                if flip == 2 => at = common / 2
                                unsafe
                                    let keepLeft = *(left + at)
                                    let keepRight = *(right + at)
                                    *(left + at) = 255
                                    *(right + at) = 1
                                    found += wrong(left, leftLength, right, rightLength)
                                    *(left + at) = 1
                                    *(right + at) = 255
                                    found += wrong(left, leftLength, right, rightLength)
                                    *(left + at) = keepLeft
                                    *(right + at) = keepRight
                                checks += 2
                                flip += 1
                            placement += 1
                        rightLength += 1
                    leftLength += 1
                Console.writeLine("checks \(checks) wrong \(found)")
            """;
        ScalarEmissionTest.EmitFixture("StringComparisonWindows", Source, "checks 45124 wrong 0\n");
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmComparisonAnalysisAndWritingAllocateNothing(bool conditional)
    {
        var c = MinimalEmissionTest.Analyze(conditional ? "var n = 0\nwhile n < 3\n    if n == 1 and \"a\" == \"a\" => Console.writeLine(\"ok\")\n    n += 1" : "func equal(a: string, b: string) -> bool => a == b\nlet text = \"a\"\nif text == (if text == \"a\" => \"a\" else => \"b\") => Console.writeLine(text)");
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.Generate(TextWriter.Null);
        });

        Assert.Equal(0, allocated);
        Assert.True(valid);
    }
}
