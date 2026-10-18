// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 22.6, 22.6: an import is safe unless its callers have obligations its Types cannot state. A ref or uniq borrow of a
// C-exchangeable referent passes the referent's address and keeps its ordinary Loan for the call; the call is an environment
// effect (SPEC 8.11.2).
public class ForeignBorrowImportTest
{
    private const string Native = "group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public func query(value: uniq/i64) -> i32\n";

    [Fact]
    public void ASafeImportNeedsNoUnsafeContext()
        => Assert.Empty(DiagnosticCorpus.Check(Native + "public func main()\n    var value: i64 = 0\n    let ok = Native.query(value@uniq)\n    let read = value\n").Diagnostics);

    // The exclusive argument borrow conflicts with a shared borrow kept across the call.
    [Fact]
    public void TheArgumentBorrowIsChecked()
    {
        var source = Native + "public func main()\n    var value: i64 = 0\n    let kept = value@ref\n    let ok = Native.query(value@uniq)\n    let read = kept@follow\n";
        Assert.Equal(nameof(DiagnosticCode.CallActivationConflict_Kd), Assert.Single(DiagnosticCorpus.Check(source).Diagnostics).Code);
    }

    [Fact]
    public void AForeignCallIsAnEnvironmentEffect()
    {
        const string Source = Native + "contract Sink\n    func put(self: uniq/Self, value: i32)\n        effect confined\n" +
            "struct ClockSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32)\n        var count: i64 = 0\n        _ = Native.query(count@uniq)\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void TheBorrowPassesTheReferentAddress()
    {
        var source = Native + """
            var value: i64 = -1
            require Native.query(value@uniq) != 0 else => $abort("query")
            require value >= 0 else => $abort("value")
            Console.writeLine("counted")
            """;
        ScalarEmissionTest.EmitFixture("ForeignBorrowCounter", source, "counted\n");
    }

    // A borrow of a C-layout struct passes the struct's address, where the foreign call writes its first Field.
    [Fact]
    public void ACLayoutStructBorrowPassesItsAddress()
    {
        const string Source = "group Native\n    #LibraryImport(\"kernel32\", \"QueryPerformanceCounter\")\n    public func query(value: uniq/Counter) -> i32\n" +
            "#Layout(\"C\")\nstruct Counter\n    public var ticks: i64\n    public init() => self.ticks = -1\n" + """
            var counter = Counter.init()
            require Native.query(counter@uniq) != 0 else => $abort("query")
            require counter.ticks >= 0 else => $abort("ticks")
            Console.writeLine("counted")
            """;
        ScalarEmissionTest.EmitFixture("ForeignBorrowCLayout", Source, "counted\n");
    }
}
