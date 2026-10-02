// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 5.4: P@raw is the address of the written Place P, a raw/V where V is the stored Type of P. It needs no unsafe context,
// performs the checks of an immediately ending @ref (initialization, read capability, Loan conflicts), reads no value and leaves
// no Loan; @raw/U only converts.
public class RawAddressTest
{
    private const string Point = "struct Point\n    public var x: i32\n    public var y: i32\n    public init(x: i32, y: i32)\n        self.x = x\n        self.y = y\n";

    [Theory]
    [InlineData("var number: i32 = 1\n    let address = number@raw\n", "raw/i32")]
    [InlineData("var number: i32 = 1\n    let pointer = number@raw\n    let address = pointer@raw\n", "raw/raw/i32")]
    [InlineData("var number: i32 = 1\n    let r = number@uniq\n    let address = r@follow@raw\n", "raw/i32")]
    [InlineData("var number: i32 = 1\n    let r = number@ref\n    let address = r@raw\n", "raw/ref/i32")]
    [InlineData("var point = Point.init(1, 2)\n    let address = point.y@raw\n", "raw/i32")]
    [InlineData("var point = Point.init(1, 2)\n    let address = point@raw\n", "raw/Point")]
    [InlineData("var point = Point.init(1, 2)\n    let p = point@raw\n    unsafe\n        let address = (*p).y@raw\n", "raw/i32")]
    [InlineData("var point = Point.init(1, 2)\n    let p = point@raw\n    unsafe\n        let address = p[0].x@raw\n", "raw/i32")]
    [InlineData("var point = Point.init(1, 2)\n    let p = point@raw\n    unsafe\n        let address = (*p)@raw\n", "raw/Point")]
    [InlineData("var values: [3 of i32] = [1, 2, 3]\n    let p = values@raw\n    unsafe\n        let address = (*p)[1]@raw\n", "raw/i32")]
    public void TheAddressHasTheStoredTypeOfTheWrittenSlot(string body, string type)
    {
        var source = Point + "public func main()\n    " + body;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var address = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().Single(static x => x.NameKoto.IdentifierName == "address");
        Assert.Equal(type, Text(address.BoundType!));
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    // The pointer keeps no Loan, so later writes to the Place and accesses through the pointer coexist.
    [Fact]
    public void TheAddressKeepsNoLoan()
    {
        var source = "public func main()\n    var number: i32 = 1\n    let pointer = number@raw\n    number += 1\n    unsafe => *pointer = 3\n    let other = number@uniq\n    other@follow += 1\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    // The address is checked as an immediately ending shared borrow.
    [Theory]
    [InlineData("var number: i32 = 1\n    let r = number@uniq\n    let pointer = number@raw\n    r@follow += 1\n")]
    [InlineData("var number: i32\n    let pointer = number@raw\n")]
    [InlineData("var text = \"a\"\n    let moved = text@move\n    let pointer = text@raw\n")]
    public void TheAddressChecksItsPlaceLikeAnImmediatelyEndingBorrow(string body)
    {
        var c = MinimalEmissionTest.Analyze("public func main()\n    " + body);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    // SPEC 5.4, 8.4.10.2: in an effect summary the address is an access to its Place, an environment effect only on a mutable static.
    [Theory]
    [InlineData("    let slot = self.count@raw\n", true)]
    [InlineData("    let p = self@raw\n        var slot = p@raw/i32\n        unsafe => slot = (*p).count@raw\n", true)]
    [InlineData("    let slot = Metrics.puts@raw\n", false)]
    public void TheAddressIsAnAccessToItsPlace(string body, bool valid)
    {
        const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32)\n        effect confined\ngroup Metrics\n    public var puts: i32 = 0\n" +
            "struct AddressSink\n    Self is Sink\n    var count: i32 = 0\n    public func put(self: uniq/Self, value: i32)\n    ";
        var c = MinimalEmissionTest.Analyze(Sink + body + "        unsafe => *slot = value\npublic func main() => ()\n");
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    // SPEC 13.5.5.2: a slot storing a raw pointer is borrowed like any other slot.
    [Fact]
    public void APointerSlotIsBorrowable()
    {
        var source = "public func main()\n    var number: i32 = 1\n    var pointer = number@raw\n    let slot = pointer@ref\n    let copied: raw/i32 = slot@follow\n    let edit = pointer@uniq\n    edit@follow = null\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ABareConversionTakesNoAddress()
    {
        var source = "public func main()\n    var number: i32 = 1\n    let pointer = number@raw\n    let address = pointer@raw/u8\n    let integer = pointer@usize\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var address = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().Single(static x => x.NameKoto.IdentifierName == "address");
        Assert.Equal("raw/u8", Text(address.BoundType!));
    }

    [Fact]
    public void WritesThroughTheAddressReachThePlace()
    {
        var source = Point + """
            func bump(pointer: raw/i32)
                unsafe => *pointer += 1
            var number: i32 = 1
            let pointer = number@raw
            bump(pointer)
            bump(number@raw)
            require number == 3 else => $abort("local")
            var point = Point.init(1, 2)
            bump(point.y@raw)
            require point.x == 1 and point.y == 3 else => $abort("field")
            var r = number@uniq
            bump(r@follow@raw)
            r@follow += 1
            require number == 5 else => $abort("referent")
            var slot = pointer
            let indirect = slot@raw
            unsafe => *indirect = null
            require slot == null else => $abort("pointer slot")
            Console.writeLine("addressed")
            """;
        ScalarEmissionTest.EmitFixture("RawAddressWrite", source, "addressed\n");
    }

    // SPEC 5.4, 5.2.2: the address of a raw Place part is displaced from the pointer; nothing is read.
    [Fact]
    public void WritesThroughARawPlacePartAddressReachThePart()
    {
        var source = Point + """
            func bump(pointer: raw/i32)
                unsafe => *pointer += 1
            var point = Point.init(1, 2)
            let p = point@raw
            unsafe
                bump((*p).y@raw)
                bump(p[0].x@raw)
            require point.x == 2 and point.y == 3 else => $abort("field")
            var values: [3 of i32] = [1, 2, 3]
            let q = values@raw
            var i: isize = 2
            unsafe
                bump((*q)[i]@raw)
                bump((*q)[0]@raw)
            require values[0] == 2 and values[1] == 2 and values[2] == 4 else => $abort("element")
            Console.writeLine("parts")
            """;
        ScalarEmissionTest.EmitFixture("RawAddressPart", source, "parts\n");
    }

    private static string Text(BoundType type)
        => type.Kind == BoundTypeKind.Semantics ? type.Semantics.ToString().ToLowerInvariant() + "/" + Text(type.Components[0]) : type.Name;
}
