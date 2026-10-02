// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 15.3.5: Kimi.Loan<T> is a zero-sized struct over a complete ref, uniq, objref or objuniq borrow Type. A Loan<T> Field is
// analyzed as storing a T, so the slot it uses keeps that borrow's Loan requirement and is not a Phantom Origin; it is Copy exactly
// when T is Copy, has no address and grants no access.
public class LoanFieldTest
{
    private const string Shared = "struct View {source}\n    Self is Copy\n    let loan: Loan<ref/i32 during source>\n    public init(target: ref/i32 during source) => self.loan = Loan<ref/i32 during source>.init(target)\n";

    private const string Exclusive = "struct View {source}\n    let loan: Loan<uniq/i32 during source>\n    public init(target: uniq/i32 during source) => self.loan = Loan<uniq/i32 during source>.init(target@move)\n";

    private const string Phantom = "struct View {source}\n    Self is Copy\n    let length: isize\n    public init(target: ref/i32 during source) => self.length = 0\n";

    [Theory]
    [InlineData("Loan<i32>")]
    [InlineData("Loan<raw/i32>")]
    [InlineData("Loan<string>")]
    [InlineData("Loan<Option<ref/i32 during source>>")]
    public void OnlyABorrowTypeFormsALoan(string type)
    {
        var source = "struct Holder {source}\n    let loan: " + type + "\n    let target: ref/i32 during source\npublic func main() => ()\n";
        var diagnostic = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.InvalidTypeFormation_Kd), diagnostic.Code);
        Assert.Equal(type, source.Substring(diagnostic.Span!.Value.Start, diagnostic.Span.Value.Length));
        Assert.Contains("complete ref, uniq, objref or objuniq borrow Type", diagnostic.Note);
    }

    [Theory]
    [InlineData("Loan<ref/i32 during source>")]
    [InlineData("Loan<uniq/i32 during source>")]
    [InlineData("Loan<uniq/(i32, i64) during source>")]
    public void ABorrowTypeFormsALoan(string type)
        => Assert.Empty(DiagnosticCorpus.Check("struct Holder {source}\n    let loan: " + type + "\npublic func main() => ()\n").Diagnostics);

    // An exclusive Loan Field keeps the exclusive Loan of its source.
    [Fact]
    public void AnExclusiveLoanFieldKeepsItsLoan()
    {
        var source = Exclusive + "public func main()\n    var number: i32 = 1\n    let view = View.init(number@uniq)\n    let read = number\n    let kept = view@move\n";
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), Assert.Single(DiagnosticCorpus.Check(source).Diagnostics).Code);
    }

    // Loan<T> is Copy exactly when T is Copy, so only a shared Loan Field admits a Copy holder.
    [Theory]
    [InlineData("ref", true)]
    [InlineData("uniq", false)]
    public void ALoanIsCopyExactlyWhenItsBorrowIs(string semantics, bool copy)
    {
        var source = "struct View {source}\n    Self is Copy\n    let loan: Loan<" + semantics + "/i32 during source>\npublic func main() => ()\n";
        Assert.Equal(copy, !DiagnosticCorpus.Check(source).Diagnostics.Any());
    }

    // A shared Loan Field allows reads and Copies, and rejects writes while the holder is live; a Phantom Origin keeps none.
    [Theory]
    [InlineData(Shared, "    let read = number\n    let again = view\n", null)]
    [InlineData(Shared, "    number = 2\n", nameof(DiagnosticCode.ComparisonLoanConflict_Kd))]
    [InlineData(Phantom, "    number = 2\n", null)]
    public void ASharedLoanFieldKeepsASharedLoan(string declaration, string use, string? code)
    {
        var source = declaration + "public func main()\n    var number: i32 = 1\n    let view = View.init(number@ref)\n" + use + "    let kept = view\n";
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics;
        if (code is null)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            Assert.Equal(code, Assert.Single(diagnostics).Code);
        }
    }

    // A covariant view keeps a raw/() address beside its Loan Field and converts the address just before an access.
    [Fact]
    public void ACovariantViewAccessesThroughItsAddress()
    {
        var source = """
            struct Window<T> {source}
                let loan: Loan<ref/T during source>
                let data: raw/()
                let length: isize
                public init(values: ref/[3 of T] during source)
                    self.loan = Loan<ref/T during source>.init(values[0]@ref)
                    self.data = values[0]@raw@raw/()
                    self.length = 3
                public func get(self: ref/Self, index: isize) -> ref/T during source
                    require 0 <= index and index < self.length else => $abort("index")
                    unsafe => return (self.data@raw/T)[index]@ref
            var numbers: [3 of i32] = [10, 20, 30]
            let window = Window<i32>.init(numbers@ref)
            require window.get(2) == 30 else => $abort("element")
            Console.writeLine("windowed")
            """;
        ScalarEmissionTest.EmitFixture("LoanFieldWindow", source, "windowed\n");
    }
}
