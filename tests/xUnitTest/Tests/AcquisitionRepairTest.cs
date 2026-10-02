// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 3.5, 15.1.5, 23.3.6.9: a bare Place that needs @move, @uniq or @objuniq offers the spelling as a repair candidate, located
// at the Place: Take is judged from the Place's path (verified for an owned local path, refuted for a borrowed, published or
// dynamically indexed one), an exclusive borrow has ExclusiveAccess verified, and UsageLegality is always required.
public class AcquisitionRepairTest
{
    private const string Resource = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => ()\nfunc consume(value: Resource) => ()\n";

    [Fact]
    public void ACallArgumentOffersTheTransfer()
    {
        var source = Resource + "public func main()\n    let resource = Resource.init(1)\n    consume(resource)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
        Assert.Equal("resource", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("resource cannot be read as a Copy value", error.Label);
        Assert.Null(error.Advice);
        var repair = Assert.Single(error.Repairs!);
        Assert.Equal("Repair.Transfer", repair.Kind);
        Assert.Equal("Append @move to transfer resource to consume", repair.Title);
        Assert.Equal([RepairCondition.Take], repair.Verified);
        Assert.Equal([RepairCondition.UsageLegality], repair.Required.Select(static x => x.Condition));
        var edit = Assert.Single(repair.Edits);
        Assert.Equal("@move", edit.Text);
        Assert.Equal(error.Span.Value.End, edit.Span.Start);
        Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits)).Diagnostics);
    }

    // A Place reached through a shared reference, a dynamically indexed element and a Dictionary element offer no Take.
    [Theory]
    [InlineData("struct Holder\n    public var item: Resource\n    public init(item: Resource) => self.item = item\nfunc total(holder: ref/Holder) => consume(holder.item)\npublic func main() => ()\n", "holder.item")]
    [InlineData("func pick(values: [2 of Resource], i: isize) => consume(values[i])\npublic func main() => ()\n", "values[i]")]
    public void APlaceWithoutTakeOffersNoTransfer(string program, string place)
    {
        var source = Resource + program;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Code == nameof(DiagnosticCode.TransferRequired_Kd));
        Assert.Equal(place, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Null(error.Repairs);
        Assert.Contains("offers no Take", error.Advice);
    }

    [Fact]
    public void AnOwnedBindingInitializerOffersTheTransferAndTheBorrowAlternative()
    {
        var source = Resource + "public func main()\n    let first = Resource.init(1)\n    let second = first\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
        Assert.Equal("first", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Contains("Borrow it with @ref or @uniq", error.Advice);
        var repair = Assert.Single(error.Repairs!);
        Assert.Equal("Append @move to transfer first to the binding second", repair.Title);
        Assert.Equal([RepairCondition.Take], repair.Verified);
        Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits)).Diagnostics);
    }

    [Fact]
    public void AnEnumPayloadOffersTheTransfer()
    {
        var source = Resource + "public func main()\n    let resource = Resource.init(1)\n    let wrapped: Resource? = .Some(resource)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
        Assert.Equal("resource", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        var repair = Assert.Single(error.Repairs!);
        Assert.Equal("Append @move to transfer resource to .Some", repair.Title);
        Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits)).Diagnostics);
    }

    [Fact]
    public void AnExclusiveBorrowOffersUniq()
    {
        const string Source = "func bump(n: uniq/i32) => n@follow += 1\npublic func main()\n    var n = 1\n    bump(n)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ExclusiveBorrowRequired_Kd), error.Code);
        Assert.Equal("n", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("n is lent exclusively only with an explicit spelling", error.Label);
        var repair = Assert.Single(error.Repairs!);
        Assert.Equal("Repair.BorrowExclusively", repair.Kind);
        Assert.Equal("Append @uniq to borrow n exclusively", repair.Title);
        Assert.Equal([RepairCondition.ExclusiveAccess], repair.Verified);
        Assert.Equal([RepairCondition.UsageLegality], repair.Required.Select(static x => x.Condition));
        Assert.Equal("@uniq", Assert.Single(repair.Edits).Text);
        Assert.Empty(DiagnosticCorpus.Check(UnnecessaryUnsafeBlockTest.Apply(Source, repair.Edits)).Diagnostics);
    }

    // SPEC 23.3.6.8: the command shows the candidate's edit at the Place and its conditions.
    [Fact]
    public void TheCommandRendersTheTransferCandidate()
    {
        var source = Resource + "public func main()\n    let resource = Resource.init(1)\n    consume(resource)\n";
        var check = DiagnosticCorpus.Check(source);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(check.Diagnostics, check.Sources), string.Empty);
        Assert.Contains("Repair: Append @move to transfer resource to consume\n = ", console.Text, StringComparison.Ordinal);
        Assert.Contains("Program.kimi:8:21: insert '@move'\n = verified: Take; requires: the edited operation and every later use of resource satisfy the initialization, Loan and lifetime conditions\n", console.Text, StringComparison.Ordinal);
    }
}
