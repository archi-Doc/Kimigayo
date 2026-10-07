// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ObjectCreationAdaptationTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void ShortCompleteAndGroupedTargetsShareCreation(string mode)
    {
        var source = $"let n = 7\nlet a = n@{mode}\nlet b = n@{mode}/i32\nlet c = n@({mode}/i32)\n" +
            "require a@follow == 7 and b@follow == 7 and c@follow == 7 and n == 7 else => $abort(\"payload\")";
        NativeAllocationAudit.WriteFixture("ObjectCreationForms" + mode, source, 3, 3, 60);
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void SameTypeTransferCreatesNoSecondObject(string mode)
    {
        var source = $"let a = 7@{mode}\nlet b = a@move@{mode}\nlet c = b@move@{mode}/i32\nrequire c@follow == 7 else => $abort(\"identity\")";
        NativeAllocationAudit.WriteFixture("ObjectCreationIdentity" + mode, source, 1, 1, 20);
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void ExistingContainerAliasesKeepTargetIdentity(string mode)
    {
        var source = $"alias A => Types\ngroup Types\n    public struct Payload\n        public let value: i32 = 9\nlet h = A.Payload.init()@{mode}/Types.Payload\nrequire h.value == 9 else => $abort(\"alias\")";
        NativeAllocationAudit.WriteFixture("ObjectCreationAlias" + mode, source, 1, 1, 20);
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void ConstructorAndZeroSizedDestructionRunOnce(string mode)
    {
        var source = $"struct Empty\n    public init() => Console.writeLine(\"init\")\n    drop => Console.writeLine(\"drop\")\nlet h = Empty.init()@{mode}\nlet same = h@move@{mode}\nConsole.writeLine(\"body\")";
        NativeAllocationAudit.WriteFixture("ObjectCreationZero" + mode, source, 1, 1, 16, "init\nbody\ndrop\n");
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void CreationAndUpcastRemainSeparate(string mode)
    {
        var types = "open struct Base\n    public let value: i32 = 9\nstruct Derived: Base\n    public init() => ()\n    drop => Console.writeLine(\"derived\")\n";
        var source = types + $"let h = Derived.init()@{mode}\nlet b = h@move@{mode}/Base\nrequire b.value == 9 else => $abort(\"base\")";
        NativeAllocationAudit.WriteFixture("ObjectCreationUpcast" + mode, source, 1, 1, 20, "derived\n");
        var invalid = MinimalEmissionTest.Analyze(types + $"let b = Derived.init()@{mode}/Base");
        Assert.Contains(invalid.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        var missingMove = MinimalEmissionTest.Analyze(types + $"let h = Derived.init()@{mode}\nlet b = h@{mode}/Base");
        Assert.Contains(missingMove.Binding.Issues, x => x.Code == DiagnosticCode.TransferRequired_Kd);
    }

    [Fact]
    public void FactoryInferenceAndFunctionItemsRemainIndependent()
    {
        const string Source = "func makeObj(value: bool) -> bool => value\nlet fromApi: obj/i64 = Kimi.Intrinsics.makeObj(1)\nlet factory = Kimi.Intrinsics.makeObj<i32>\nlet fromItem = factory(2)\nlet explicitWidth = 3@i64@obj\nlet intrinsic = 4@obj\nrequire fromApi@follow == 1 and fromItem@follow == 2 and explicitWidth@follow == 3 and intrinsic@follow == 4 else => $abort(\"factories\")";
        NativeAllocationAudit.WriteFixture("ObjectCreationFactories", Source, 4, 4, 88);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllocationIsConfinedButOperandEffectsRemainVisible(bool environment)
    {
        var source = "contract Maker\n    func create(self: ref/Self) -> obj/i32\n        effect confined\n" +
            "struct Factory\n    Self is Maker\n    public func create(self: ref/Self) -> obj/i32\n" +
            (environment ? "        Console.writeLine(\"effect\")\n" : string.Empty) +
            "        return 7@obj\nlet factory = Factory.init()\nlet value = factory.create()";
        var c = MinimalEmissionTest.Analyze(source);
        if (environment)
        {
            MinimalEmissionTest.AssertEffectBoundRejected(c);
        }
        else
        {
            Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), failure);
        }
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void ExactPayloadKeepsInternalLoansWithoutOwned(string mode)
    {
        var source = $"struct View{{source}}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\nvar n = 7\nlet h = View.init(n@ref)@{mode}\nrequire h.value@follow == 7 else => $abort(\"loan\")";
        NativeAllocationAudit.WriteFixture("ObjectCreationLoan" + mode, source, 1, 1, 24);
        var invalid = MinimalEmissionTest.Analyze(source.Replace("require h.value", "n = 8\nrequire h.value", StringComparison.Ordinal));
        Assert.True(invalid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(invalid, null));
        Assert.False(invalid.Ownership.Result.IsVerified);
        Assert.False(invalid.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void GenericPayloadUsesTheExistingFactoryPlan(string mode)
    {
        var source = $"func box<T>(value: T) -> {mode}/T\n    T is ObjectPayload\n    return value@move@{mode}\n" +
            "let number = box(7)\nlet flag = box(true)\nrequire number@follow == 7 and flag@follow else => $abort(\"generic\")";
        NativeAllocationAudit.WriteFixture("ObjectCreationGeneric" + mode, source, 2, 2, 37);
    }

    [Theory]
    [InlineData("let h = 1@obj/i64", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let h: obj/i64 = 1@obj", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let n = 1\nlet h = n@ref@obj", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let h = 1@obj\nlet wrong = h@move@rc", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let h = 1@rc\nlet wrong = h@move@arc", DiagnosticCode.TypeMismatch_Kd)]
    [InlineData("let text = \"text\"\nlet h = text@obj", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("let h = 1@obj\nlet again = h@obj", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("let h = 1@owner", DiagnosticCode.BareOwningShorthand_Kd)]
    [InlineData("struct Payload\n    Self is not ObjectPayload\nlet h = Payload.init()@obj", DiagnosticCode.NotObjectPayload_Kd)]
    public void InvalidOperationsKeepTheirSpecificCause(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsupportedBinding_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void NeverDoesNotFormAPayloadType(string mode)
    {
        var c = MinimalEmissionTest.Analyze($"func stop() -> Never => $abort(\"stop\")\nfunc run() => stop()@{mode}\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Single();
        Assert.Same(BoundType.Never, conversion.BoundType);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), failure);
    }

    [Fact]
    public void NeverStillChecksExplicitTargetFormation()
    {
        var c = MinimalEmissionTest.Analyze("func stop() -> Never => $abort(\"stop\")\nfunc run() => stop()@obj/Never");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTypeFormation_Kd);
    }

    [Fact]
    public void MoveFailurePublishesTheWrittenSourceInCliAndLsp()
    {
        const string Source = "let text = \"text\"\nlet boxed = text@obj";
        var path = Path.GetFullPath("object-creation.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("TransferRequired_Kd", error.Code);
        Assert.Contains("text", error.Text!, StringComparison.Ordinal);
        Assert.Contains("@move", error.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("<synthetic>", error.Explanation, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var diagnostic = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(diagnostic.Display!.Range, sent.Range);
            Assert.Contains("@move", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void TargetEditsReplaceTheFactoryAndRevokeStalePlans()
    {
        var c = MinimalEmissionTest.Analyze("let n = 7@obj");
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Single();
        var original = Assert.IsType<InvocationKoto>(conversion.CreationCall);
        Assert.Equal(CompilerFunctionKind.MakeObj, original.BoundCall!.Target.CompilerFunction);
        var donor = KotoTree.Walk(MinimalEmissionTest.Analyze("let n = 7@rc").Kotonoha.RootKoto).OfType<ConversionKoto>().Single();
        Assert.True(KotoHelper.Replace(conversion, conversion.Right, donor.Right));
        Assert.Null(conversion.CreationCall);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(CompilerFunctionKind.MakeRc, conversion.CreationCall!.BoundCall!.Target.CompilerFunction);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), failure);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedBindingOwnershipAndEmissionAllocateNothing()
    {
        const string Source = "func box<T>(value: T) -> rc/T\n    T is ObjectPayload\n    return value@move@rc\nlet n = box(7)\nlet b = box(true)\nlet direct = 1@obj";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
