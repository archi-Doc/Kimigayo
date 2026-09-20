// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ProjectionApiAccessBindingTest
{
    [Theory]
    [InlineData("public", "internal")]
    [InlineData("internal", "public")]
    [InlineData("internal", "internal")]
    public void NormalizedParametersRetainQualifierAndRequirementDomains(string typeAccess, string contractAccess)
    {
        var c = MinimalEmissionTest.Analyze($"{contractAccess} contract C\n    associate Element\n{typeAccess} struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose(value?: S.C.Element) => ()");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node is FunctionKoto { Name: "expose" } && x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("S.Element")]
    [InlineData("S.C.Element")]
    [InlineData("(S.C.Element)")]
    [InlineData("ref/S.C.Element")]
    [InlineData("(i32, [1 of S.C.Element])")]
    [InlineData("(S.C.Element) -> ()")]
    public void NestedAndShortProjectionsCannotHideTheQualifier(string type)
    {
        Reject($"public contract C\n    associate Element\nstruct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose(value?: {type}) => ()");
    }

    [Theory]
    [InlineData("public", "internal")]
    [InlineData("internal", "public")]
    public void NormalizedResultsRetainProjectionAccess(string typeAccess, string contractAccess)
    {
        Reject($"{contractAccess} contract C\n    associate Element\n{typeAccess} struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose() -> S.C.Element => 1");
    }

    [Fact]
    public void ConstructedQualifiersCheckConcreteArguments()
    {
        Reject("struct Hidden\npublic contract C\n    associate Element\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose(value?: Box<Hidden>.C.Element) => ()");
    }

    [Fact]
    public void RequirementSignaturesUseTheirContractDomain()
    {
        Reject("contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic contract Api\n    func expose(value?: S.C.Element)");
    }

    [Fact]
    public void RefinementQualificationCannotHideTheDefiningRequirement()
    {
        var c = MinimalEmissionTest.Analyze("contract Parent\n    associate Element\npublic contract Child: Parent\npublic struct S\n    Self is Child\n    associate Child.Element is i32\npublic group Api\n    public func expose(value?: S.Child.Element) => ()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node is ContractKoto { Name: "Child" } && x.Node.BindingFailure == BindingFailure.Access);
    }

    [Theory]
    [InlineData("public", "public", "public")]
    [InlineData("internal", "public", "internal")]
    [InlineData("public", "internal", "internal")]
    [InlineData("internal", "internal", "private")]
    public void CoveredDomainsRemainValid(string typeAccess, string contractAccess, string functionAccess)
    {
        Accept($"{contractAccess} contract C\n    associate Element\n{typeAccess} struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    {functionAccess} func expose(value?: S.C.Element) -> S.Element => value");
    }

    [Theory]
    [InlineData("contract C\n    associate Element\nstruct S\n    Self is C\n    associate C.Element is i32\nfunc expose(value?: S.C.Element) => ()")]
    [InlineData("public group Api\n    private contract C\n        associate Element\n    private struct S\n        Self is C\n        associate C.Element is i32\n    private func expose(value?: S.C.Element) => ()")]
    [InlineData("group Api\n    public contract C\n        associate Element\n    public struct S\n        Self is C\n        associate C.Element is i32\n    public func expose(value?: S.C.Element) => ()")]
    [InlineData("contract C\n    associate Element\nstruct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose() -> i32\n        let value: S.C.Element = 1\n        return value")]
    [InlineData("public contract C\n    associate Element\npublic group Api\n    public func expose<T>(value?: T.C.Element)\n        T is C\n        ()")]
    [InlineData("public contract Parent\n    associate Element\npublic contract Child: Parent\npublic group Api\n    public func expose<T>(value?: T.Child.Element)\n        T is Child\n        ()")]
    public void EffectiveContainersBodiesAndDependentProjectionsRemainValid(string source) => Accept(source);

    [Fact]
    public void ExposedConcreteBindingsRemainRestricted()
    {
        var c = MinimalEmissionTest.Analyze("struct Hidden\npublic contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is Hidden\npublic group Api\n    public func expose(value?: S.C.Element) => ()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    public void RebindingAndReloadRetainProjectionDomains(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"public contract C\n    associate Element\n{access} struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose(value?: S.C.Element) => ()");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, c.Bind().IsComplete);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(c.Binding.Issues.Select(x => x.Code), restored.Binding.Issues.Select(x => x.Code));
    }

    [Fact]
    public void ReplacingASignatureClearsAndRestoresProjectionAccessFailures()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose(value?: S.C.Element) => ()");
        Assert.False(c.Binding.Result.IsComplete);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<FunctionKoto>().Single(x => x.Name == "expose");
        var original = function.Parameters[0].Type;
        var replacementSource = MinimalEmissionTest.Analyze("group Api\n    func replacement(value?: i32) => ()");
        var replacement = replacementSource.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<FunctionKoto>().Single().Parameters[0].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingFailure.Access, function.BindingFailure);
    }

    [Fact]
    public void WarmProjectionApiChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose(value?: (S.C.Element, S.Element)) => ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Projection API access Binding failed.");
            }
        }));
    }

    private static void Accept(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    private static void Reject(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node is FunctionKoto { Name: "expose" } && x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(c.Emission.Validate(out _));
    }
}
