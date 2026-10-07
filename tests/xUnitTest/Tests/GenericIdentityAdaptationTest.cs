// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class GenericIdentityAdaptationTest(ITestOutputHelper output)
{
    private const string AssociatedOwnerSource = "contract C\n    associate Item is Copy\n" +
        "func keep<s/T>(value: s/T) -> s/T\n    s is owner\n    T is Copy\n    return value@s\n" +
        "func relay<U>(value: U.(C).Item) -> U.(C).Item\n    U is C\n    return keep(value)\n";

    [Theory]
    [InlineData("T")]
    [InlineData("owner/T")]
    [InlineData("(owner/T)")]
    public void CopyAloneDoesNotSelectIdentityForEveryCompleteType(string target)
    {
        var c = MinimalEmissionTest.Analyze($"func keep<T>(value: T) -> T\n    T is Copy\n    return value@{target}\n()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("ObjectPayload", "T")]
    [InlineData("ObjectPayload", "owner/T")]
    [InlineData("Sealed", "T")]
    [InlineData("Sealed", "owner/T")]
    [InlineData("PrimitiveInteger", "owner/T")]
    public void AProvenOwnerUsesOrdinaryIdentityAcquisition(string capability, string target)
    {
        var c = MinimalEmissionTest.Analyze($"func keep<T>(value: T) -> T\n    T is Copy and {capability}\n    return value@{target}\nlet number = 7\nlet kept = keep(number)\nrequire kept == number else => $abort(\"identity\")");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
    }

    [Theory]
    [InlineData("s")]
    [InlineData("s/U")]
    [InlineData("(s/U)")]
    public void AnOwnerSelectorDoesNotProveAnUnrelatedCopyTypeIsOwner(string target)
    {
        var c = MinimalEmissionTest.Analyze($"func keep<s/T, U>(value: U) -> U\n    s is owner\n    U is Copy\n    return value@{target}\n()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void ThePairsOwnOwnerCaseAcquiresAnOptedOutPayload()
    {
        const string Source = "open struct Payload\n    Self is not ObjectPayload\n    public let number: i32 = 7\n    public init() => ()\n" +
            "func keep<s/T>(value: s/T) -> s/T\n    s is owner or object\n    return value@move@s\n" +
            "let payload = keep<Payload>(Payload.init())\nrequire payload.number == 7 else => $abort(\"owner\")\n" +
            "let handle = keep<obj/i32>(8@obj)\nrequire handle@follow == 8 else => $abort(\"object\")";
        NativeAllocationAudit.WriteFixture("GenericIdentityOptedOutOwner", Source, 1, 1, 20);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" and ObjectPayload", true)]
    [InlineData(" and Sealed", true)]
    [InlineData(" and PrimitiveInteger", true)]
    public void AnAssociatedTypeNeedsPublishedOwnerEvidence(string evidence, bool valid)
    {
        var source = AssociatedOwnerSource.Replace("Item is Copy", "Item is Copy" + evidence, StringComparison.Ordinal);
        if (valid)
        {
            source += "struct S\n    Self is C\n    associate C.Item is i32\n" +
                "let value = relay<S>(7)\nrequire value == 7 else => $abort(\"identity\")";
        }

        var path = Path.GetFullPath("associated-owner-proof.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (valid)
        {
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        }
        else
        {
            c.Binding.ReportDiagnostics();
            var error = Assert.Single(TestDiagnostics.Of(c), x => x.Severity == DiagnosticSeverity.Error);
            Assert.Equal(nameof(DiagnosticCode.UnprovenConstraint_Kd), error.Code);
            Assert.Contains("cannot be proven", error.Message, StringComparison.Ordinal);
            Assert.Equal("keep(value)", error.Text);
            c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
            var result = c.Diagnostics.Finalize(rejected: true);
            var record = Assert.Single(result.Diagnostics);
            Assert.Equal(DiagnosticCategory.Proof, record.Category);
            Assert.Contains(record.Reason!, x => x.Name == "subject" && x.Value == "U.Item");
            Assert.Contains(record.Reason!, x => x.Name == "member" && x.Value == "keep");
            Assert.Contains(record.Reason!, x => x.Name == "constraint" && x.Value == "s is owner");
            Assert.Equal("constraint", Assert.Single(record.Related!).Role);
            Assert.Equal(new SourceRange(new(8, 11), new(8, 22)), record.Display!.Range);
            Assert.Contains("s is owner", record.Note!, StringComparison.Ordinal);
            Assert.Contains("U.Item", record.Note!, StringComparison.Ordinal);
            var console = new DiagnosticContractTest.DiagnosticConsole();
            new Kimigayo(console).Render(result, string.Empty);
            Assert.Contains("s is owner", console.Text, StringComparison.Ordinal);
            Assert.Contains("U.Item", console.Text, StringComparison.Ordinal);
            output.WriteLine(console.Text);
            var identity = SourceIdentity.FromPath(path);
            foreach (var related in new[] { false, true })
            {
                var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
                Assert.Equal(record.Code, sent.Code);
                Assert.Equal(record.Display.Range, sent.Range);
                Assert.Contains("s is owner", sent.Message, StringComparison.Ordinal);
                Assert.Contains("U.Item", sent.Message, StringComparison.Ordinal);
                output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
            }

            Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingCallConstraintsReuseTheirFailureFacts(bool associated)
    {
        const string PlainOwnerSource = "func keep<s/T>(value: s/T) -> s/T\n    s is owner\n    T is Copy\n    return value@s\n" +
            "func relay<U>(value: U) -> U\n    U is Copy\n    return keep(value)\n";
        var c = MinimalEmissionTest.Analyze(associated ? AssociatedOwnerSource : PlainOwnerSource);
        Assert.False(c.Binding.Result.IsComplete);
        var rejected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => rejected &= !c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(rejected);
    }
}
