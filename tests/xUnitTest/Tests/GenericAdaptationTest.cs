// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Verification;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.9, 8.10 and 13.5.8: one fixed input and the ordinary operation in every admitted Semantics case.</summary>
public class GenericAdaptationTest
{
    [Theory]
    [InlineData("Short", "s")]
    [InlineData("Complete", "s/T")]
    [InlineData("Grouped", "(s/T)")]
    public void ObjectCasesSelectTheExistingFactories(string name, string target)
    {
        var source = $"func box<s/T>(value: T) -> s/T\n    s is object\n    T is ObjectPayload\n    return value@move@{target}\n" +
            "let first = box<obj/i32>(7)\nlet second = box<rc/i32>(8)\nlet third = box<arc/i32>(9)\n" +
            "require first@follow == 7 and second@follow == 8 and third@follow == 9 else => $abort(\"payload\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptation" + name, source, 3, 3, 60);
    }

    [Fact]
    public void OwnerAndObjectCasesShareTheFixedInputWithoutInventingAnObject()
    {
        const string Source = "func box<s/T>(value: T) -> s/T\n    s is owner or object\n    T is ObjectPayload\n    return value@move@s\n" +
            "let value = box<i32>(6)\nlet first = box<obj/i32>(7)\nlet second = box<rc/i32>(8)\nlet third = box<arc/i32>(9)\n" +
            "require value == 6 and first@follow == 7 and second@follow == 8 and third@follow == 9 else => $abort(\"cases\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationOwnerAndObjects", Source, 3, 3, 60);
    }

    [Fact]
    public void TypeIdentityPremiseSelectsTheSameObjectAdaptation()
    {
        const string Source = "func box<s/T, U>(value: T) -> U\n    s is object\n    T is ObjectPayload\n    U is s/T\n    return value@move@U\n" +
            "let first = box<obj/i32, obj/i32>(7)\nlet second = box<rc/i32, rc/i32>(8)\nlet third = box<arc/i32, arc/i32>(9)\n" +
            "require first@follow == 7 and second@follow == 8 and third@follow == 9 else => $abort(\"identity premise\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationTypeIdentity", Source, 3, 3, 60);
    }

    [Theory]
    [InlineData("s")]
    [InlineData("s/T")]
    public void SameTypeMovesCreateNoAdditionalObjects(string target)
    {
        var source = $"func keep<s/T>(value: s/T) -> s/T\n    s is object\n    return value@move@{target}\n" +
            "let first = keep<obj/i32>(7@obj)\nlet second = keep<rc/i32>(8@rc)\nlet third = keep<arc/i32>(9@arc)\n" +
            "require first@follow == 7 and second@follow == 8 and third@follow == 9 else => $abort(\"transfer\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationMove" + (target == "s" ? "Short" : "Complete"), source, 3, 3, 60);
    }

    [Theory]
    [InlineData("func box<s/T>(value: T) -> s/T\n    s is object\n    T is ObjectPayload\n    return value@s", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("func keep<s/T>(value: s/T) -> s/T\n    s is object\n    return value@s", DiagnosticCode.TransferRequired_Kd)]
    [InlineData("func wrong<s/T>() -> s/i64\n    s is object\n    return 1@s/i64", DiagnosticCode.TypeMismatch_Kd)]
    public void UnusedDefinitionsMustProveEveryAdmittedOperation(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void CopyEvidenceAllowsBarePayloadAcquisitionInEveryCase()
    {
        const string Source = "func box<s/T>(value: T) -> s/T\n    s is owner or object\n    T is Copy and ObjectPayload\n    return value@s\n" +
            "let number = 7\nlet plain = box<i32>(number)\nlet first = box<obj/i32>(number)\nlet second = box<rc/i32>(number)\nlet third = box<arc/i32>(number)\n" +
            "require number == 7 and plain == 7 and first@follow == 7 and second@follow == 7 and third@follow == 7 else => $abort(\"copy\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationCopy", Source, 3, 3, 60);
    }

    [Theory]
    [InlineData("s")]
    [InlineData("s/i32")]
    public void NeverDoesNotAcquireOrFormAPayload(string target)
    {
        var c = MinimalEmissionTest.Analyze($"func stop() -> Never => $abort(\"stop\")\nfunc run<s/T>() -> Never\n    s is object\n    return stop()@{target}\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var conversion = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>());
        Assert.Same(BoundType.Never, conversion.TypeOf());
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), failure);
    }

    [Theory]
    [InlineData("Complete", "s/Base")]
    [InlineData("Grouped", "(s/Base)")]
    public void GenericUpcastsKeepTheOriginalAllocationAndDynamicDestructor(string name, string target)
    {
        var source = "open struct Base\n    public let value: i32 = 9\nstruct Derived: Base\n    public init() => ()\n    drop => Console.writeLine(\"derived\")\n" +
            $"func widen<s/T>(value: s/Derived) -> s/Base\n    s is object\n    return value@move@{target}\n" +
            "let first = widen<obj/Derived>(Derived.init()@obj)\nlet second = widen<rc/Derived>(Derived.init()@rc)\nlet third = widen<arc/Derived>(Derived.init()@arc)\n" +
            "require first.value == 9 and second.value == 9 and third.value == 9 else => $abort(\"upcast\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationUpcast" + name, source, 3, 3, 60, "derived\nderived\nderived\n");
    }

    [Fact]
    public void GenericOwningInputsBorrowTheirBaseWithoutCreatingAnotherObject()
    {
        const string Source = "open struct Base\n    public let value: i32 = 9\nstruct Derived: Base\n    public init() => ()\n    drop => Console.writeLine(\"derived\")\n" +
            "func inspect<s/T>(value: s/Derived) -> i32\n    s is object\n    let view = value@objref/Base\n    return view.value\n" +
            "require inspect<obj/Derived>(Derived.init()@obj) == 9 and inspect<rc/Derived>(Derived.init()@rc) == 9 and inspect<arc/Derived>(Derived.init()@arc) == 9 else => $abort(\"borrow upcast\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationBorrowUpcast", Source, 3, 3, 60, "derived\nderived\nderived\n");
    }

    [Fact]
    public void MixedObjectAndReferenceShorthandBorrowsTheWrittenReferenceSlot()
    {
        const string Source = "func inspect<s/T>(value: s/T) -> ()\n    s is obj or ref\n    T is ObjectPayload\n    _ = value@move@s\n" +
            "let number = 7\ninspect(number@ref)\ninspect<obj/i32>(8@obj)\nrequire number == 7 else => $abort(\"slot\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var conversion = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), x => x.ConversionOf().Adaptation is not null);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "inspect");
        var type = body.Resolve(conversion.TypeOf(), InterpretationContext.Root)!;
        Assert.Equal(SemanticsKind.Ref, type.Semantics);
        Assert.Equal(SemanticsKind.Ref, type.Components[0].Semantics);
        NativeAllocationAudit.WriteFixture("GenericAdaptationMixedSlot", Source, 1, 1, 20);
    }

    [Fact]
    public void DiscardedCasesSeparateOwnerBorrowAndCreation()
    {
        const string Source = "func inspect<s/T>(value: i32) -> ()\n    s is owner or ref or object\n    _ = value@s\n" +
            "inspect<i32>(7)\ninspect<ref/i32 during static>(7)\ninspect<obj/i32>(7)\ninspect<rc/i32>(7)\ninspect<arc/i32>(7)";
        NativeAllocationAudit.WriteFixture("GenericAdaptationMixedDiscard", Source, 3, 3, 60);
    }

    [Fact]
    public void RawShorthandTakesAnAddressWhileObjectCaseCreates()
    {
        const string Source = "func inspect<s/T>(value: i32) -> ()\n    s is obj or raw\n    _ = value@s\n" +
            "inspect<raw/i32>(7)\ninspect<obj/i32>(7)";
        NativeAllocationAudit.WriteFixture("GenericAdaptationMixedAddress", Source, 1, 1, 20);
    }

    [Fact]
    public void DefaultEvaluatorsSelectTheirOwnCreationCase()
    {
        const string Source = "func take<s/T>(value: s/i32 = 7@s) -> s/i32\n    s is owner or object\n    return value@move\n" +
            "let plain = take<i32>()\nlet first = take<obj/i32>()\nlet second = take<rc/i32>()\nlet third = take<arc/i32>()\n" +
            "require plain == 7 and first@follow == 7 and second@follow == 7 and third@follow == 7 else => $abort(\"default\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationDefault", Source, 3, 3, 60);
    }

    [Fact]
    public void ClosureBodiesKeepTheEnclosingSemanticsSelection()
    {
        const string Source = "func make<s/T>() -> ()\n    s is owner or object\n    let create = func [] (value: i32) -> s/i32 => value@s\n    _ = create(7)@move\n    _ = create(8)@move\n" +
            "make<i32>()\nmake<obj/i32>()\nmake<rc/i32>()\nmake<arc/i32>()";
        NativeAllocationAudit.WriteFixture("GenericAdaptationClosure", Source, 6, 6, 120);
    }

    [Fact]
    public void TargetEditsRevokeAndRebuildEverySelectedCase()
    {
        const string Source = "func box<s/T>() -> s/i32\n    s is owner or object\n    return 7@s\n" +
            "let plain = box<i32>()\nlet first = box<obj/i32>()\nlet second = box<rc/i32>()\nlet third = box<arc/i32>()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var conversion = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>());
        var invalid = MinimalEmissionTest.Analyze(Source.Replace("7@s", "7@s/i64", StringComparison.Ordinal));
        var donor = Assert.Single(KotoTree.Walk(invalid.Kotonoha.RootKoto).OfType<ConversionKoto>());
        Assert.True(KotoHelper.Replace(conversion, conversion.Right, donor.Right));
        Assert.Null(conversion.ConversionOf().Adaptation);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));

        var restored = MinimalEmissionTest.Analyze(Source);
        donor = Assert.Single(KotoTree.Walk(restored.Kotonoha.RootKoto).OfType<ConversionKoto>());
        Assert.True(KotoHelper.Replace(conversion, conversion.Right, donor.Right));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out failure), MinimalEmissionTest.Describe(c, failure));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedCaseBindingOwnershipAndGenerationAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(AdaptationWorkloads.Single);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
