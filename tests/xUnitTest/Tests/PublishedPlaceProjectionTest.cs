// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class PublishedPlaceProjectionTest
{
    private const string Header = "func slot<D, K>(values: uniq/D, key: ref/K) -> place uniq/D.Element during values\n    D is UniqIndexable<K>\n    return values[key]\nfunc change(value: uniq/i32) => value@follow += 2\n";

    [Theory]
    [InlineData("Index", "values[1]")]
    [InlineData("Direct", "values.indexUniq(1)")]
    [InlineData("Generic", "slot(values@uniq, 1)")]
    public void FixedArrayElementsKeepTheirSelectedStorage(string name, string selection)
    {
        var source = Header + "var values: Dictionary<i32, [2 of i32]> = [1: [0, 7]]\n" + selection + "[0] = 40\n" + selection + "[0] += 2\nrequire values[1][0] == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceArray" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Index", "values[1]")]
    [InlineData("Direct", "values.indexUniq(1)")]
    [InlineData("Generic", "slot(values@uniq, 1)")]
    public void StoredExclusiveReferencesKeepTheSelectedPlaceCapability(string name, string selection)
    {
        var source = Header + "var value = 0\nvar values = [1: (value@uniq, 7)]\n" + selection + ".0@follow = 39\n" +
            selection + ".0@follow++\nchange(" + selection + ".0)\nrequire value == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceReference" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Index", "values[1]")]
    [InlineData("Direct", "values.indexUniq(1)")]
    [InlineData("Generic", "slot(values@uniq, 1)")]
    public void CopyFieldsUpdateTheSelectedStorage(string name, string selection)
    {
        var source = Header + "var values = [1: (0, 7)]\n" + selection + ".0 = 39\n" + selection + ".0++\n" + selection + ".0 += 2\nrequire values[1].0 == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceCopy" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("values[1].0@follow += 2")]
    [InlineData("change(values[1].0)")]
    public void SharedLayersStillRejectExclusiveAcquisition(string use)
    {
        var c = MinimalEmissionTest.Analyze(Header + "func run(values: ref/Dictionary<i32, (uniq/i32 during a, i32)>)\n    " + use);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code is DiagnosticCode.SharedPathAccess_Kd or DiagnosticCode.NoApplicableOverload_Kd);
    }

    [Theory]
    [InlineData("Index", "values[key()]")]
    [InlineData("Direct", "values.indexUniq(key())")]
    [InlineData("Generic", "slot(values@uniq, key())")]
    public void UpdatesEvaluateTheRightHandSideFirstAndTheSelectorOnce(string name, string selection)
    {
        var source = Header + "func key() -> i32\n    Console.writeLine(\"key\")\n    return 1\nfunc rhs() -> i32\n    Console.writeLine(\"rhs\")\n    return 2\n" +
            "var values = [1: (40, 7)]\n" + selection + ".0 += rhs()\nrequire values[1].0 == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceOrder" + name, source, "rhs\nkey\n");
    }

    [Theory]
    [InlineData("values[1]")]
    [InlineData("values.indexUniq(1)")]
    [InlineData("slot(values@uniq, 1)")]
    public void OptionalChildrenStillSuspendStoredParentMutation(string selection)
    {
        var source = Header + "func run(value: uniq/i32 during a)\n    var values = [1: (value@move, 7)]\n" +
            "    let child = match values.tryGet(1)\n        .Some(let item) => item.0@follow@ref\n        .None => $abort(\"missing\")\n" +
            "    " + selection + ".0@follow = 99\n    require child == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Source.ToString() != "item.0");
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ProjectionAcquisitionReusesAnalysisStorage()
    {
        var c = MinimalEmissionTest.Analyze(Header + "var value = 40\nvar values = [1: (value@uniq, 7)]\nchange(values[1].0)\nrequire value == 42 else => $abort(\"value\")");
        var valid = true;
        var bytes = AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified);
        Assert.True(valid);
        Assert.Equal(0, bytes);
    }

    [Theory]
    [InlineData("Index", "values[1]")]
    [InlineData("Direct", "values.indexUniq(1)")]
    [InlineData("Generic", "slot(values@uniq, 1)")]
    public void NamedStoredFieldsUseTheSameAcquisition(string name, string selection)
    {
        var source = Header + "struct Pair\n    public var item: i32\n    public init(item: i32) => self.item = item\n" +
            "var values = [1: Pair.init(0)]\n" + selection + ".item = 39\n" + selection + ".item++\n" + selection + ".item += 2\nrequire values[1].item == 42 else => $abort(\"value\")";
        ScalarEmissionTest.EmitFixture("PublishedPlaceField" + name, source, string.Empty);
    }
}
