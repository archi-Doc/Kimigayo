// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstraintProjectionApiAccessBindingTest
{
    [Theory]
    [InlineData("public group Api\n    public func expose<T>(value: T)\n        T is S.C.Element\n        ()")]
    [InlineData("public struct Api<T>\n    T is S.C.Element")]
    [InlineData("public enum Api<T>\n    T is S.C.Element\n    Empty")]
    [InlineData("public contract Api\n    associate Element is S.C.Element")]
    public void DeclarationConstraintsCannotHideRestrictedProjectionRequirements(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\n" + declaration);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("S.Element")]
    [InlineData("S.C.Element")]
    [InlineData("not S.C.Element")]
    [InlineData("(S.C.Element or string)")]
    [InlineData("Copy and S.C.Element")]
    [InlineData("[1 of S.C.Element]")]
    public void CompoundFunctionRequirementsRetainProjectionDomains(string requirement)
    {
        Reject($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose<T>(value: T)\n        T is {requirement}\n        ()");
    }

    [Theory]
    [InlineData("public", "internal", "public", false)]
    [InlineData("internal", "public", "public", false)]
    [InlineData("public", "public", "public", true)]
    [InlineData("public", "internal", "internal", true)]
    [InlineData("internal", "public", "private", true)]
    public void FunctionConstraintDomainsIncludeQualifierAndRequirement(string typeAccess, string contractAccess, string functionAccess, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"{contractAccess} contract C\n    associate Element\n{typeAccess} struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    {functionAccess} func expose<T>(value: T)\n        T is S.C.Element\n        ()");
        Check(c, valid);
    }

    [Fact]
    public void ConstructedRequirementChecksProjectedArguments()
    {
        Reject("contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Box<T>\npublic group Api\n    public func expose<T>(value: T)\n        T is Box<S.C.Element>\n        ()");
    }

    [Theory]
    [InlineData("(i32, [1 of S.C.Element])")]
    [InlineData("Box<i32>.C.Element")]
    public void UnsupportedRequirementSpellingsRemainParseErrors(string requirement)
    {
        var c = MinimalEmissionTest.Analyze($"public contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose<T>(value: T)\n        T is {requirement}\n        ()");
        Assert.True(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C").CodeContext.DiagnosticCollection.HasErrors);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("T.C.Element")]
    [InlineData("T.Element")]
    public void SubjectProjectionsRetainTheirDefiningRequirement(string subject)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic group Api\n    public func expose<T>(value: T)\n        T is C\n        {subject} is i32\n        ()");
        Check(c, false);
        Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto { Left: MemberAccessKoto } && x.Node.BindingFailure == BindingFailure.Access);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    public void FunctionRequirementsUseTheirContractDomain(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\n{access} contract Api\n    func expose<T>(value: T)\n        T is S.C.Element");
        Check(c, valid);
    }

    [Theory]
    [InlineData("struct", "")]
    [InlineData("enum", "\n    Empty")]
    public void GenericContainersRetainValidInternalDomains(string kind, string suffix)
    {
        Accept($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\ninternal {kind} Api<T>\n    T is S.C.Element{suffix}");
    }

    [Theory]
    [InlineData("T.C.Element", "i32")]
    [InlineData("T.Element", "Copy")]
    public void DependentSubjectProjectionsRemainValid(string subject, string requirement)
    {
        Accept($"public contract C\n    associate Element\npublic group Api\n    public func expose<T>(value: T)\n        T is C\n        {subject} is {requirement}\n        ()");
    }

    [Theory]
    [InlineData("public contract Api\n    associate Element is S.C.Element", false)]
    [InlineData("internal contract Api\n    associate Element is S.C.Element", true)]
    [InlineData("public contract Parent\n    associate Element\npublic contract Api: Parent\n    Self.Parent.Element is S.C.Element", false)]
    [InlineData("public contract Parent\n    associate Element\ninternal contract Api: Parent\n    Self.Parent.Element is S.C.Element", true)]
    public void AssociatedRequirementsAndRefinementConstraintsUseTheirDeclarationDomain(string declaration, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\n" + declaration);
        Check(c, valid);
    }

    [Fact]
    public void InvalidContractConstraintsCannotPublishConformance()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic contract Api\n    associate Item is S.C.Element\npublic struct Implementation\n    Self is Api\n    associate Api.Item is i32");
        Check(c, false);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Implementation");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var conformance = c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!);
        Assert.NotNull(conformance);
        Assert.False(conformance.IsVerified);
    }

    [Theory]
    [InlineData("func local<T>(value: T)\n    T is S.C.Element\n    ()")]
    [InlineData("public group Api\n    public func expose<T>(value: T)\n        let item: S.C.Element = 1\n        ()")]
    [InlineData("public struct Api\n    Self is C\n    associate C.Element is S.C.Element")]
    [InlineData("public group Api\n    private func expose<T>(value: T)\n        T is S.C.Element\n        ()")]
    public void LocalBodiesAndConformanceSpecificationsKeepTheirDomains(string declaration)
    {
        Accept("contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\n" + declaration);
    }

    [Theory]
    [InlineData(false, "public", false)]
    [InlineData(false, "internal", true)]
    [InlineData(true, "public", false)]
    [InlineData(true, "internal", true)]
    public void RebindingAndReloadRetainConstraintDomains(bool contract, string access, bool valid)
    {
        var declaration = contract ? $"{access} contract Api\n    associate Item is S.C.Element" : $"public group Api\n    {access} func expose<T>(value: T)\n        T is S.C.Element\n        ()";
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\n" + declaration);
        Check(c, valid);
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
    public void ReplacingAFunctionConstraintClearsAndRestoresAccessFailure()
    {
        const string prefix = "contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public func expose<T>(value: T)\n        T is ";
        var c = MinimalEmissionTest.Analyze(prefix + "S.C.Element\n        ()");
        Check(c, false);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<FunctionKoto>().Single();
        var clause = (IsKoto)function.TypeConstraints.Single();
        var original = clause.Right;
        var valid = MinimalEmissionTest.Analyze(prefix + "i32\n        ()");
        var replacement = ((IsKoto)valid.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<FunctionKoto>().Single().TypeConstraints.Single()).Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingFailure.Access, clause.BindingFailure);
    }

    [Fact]
    public void WarmConstraintProjectionChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic contract Api\n    associate Item is S.C.Element\npublic group Functions\n    public func expose<T>(value: T)\n        T is S.C.Element\n        ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Constraint projection API access Binding failed.");
            }
        }));
    }

    private static void Accept(string source) => Check(MinimalEmissionTest.Analyze(source), true);

    private static void Reject(string source) => Check(MinimalEmissionTest.Analyze(source), false);

    private static void Check(Compilation c, bool valid)
    {
        var diagnostics = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C").CodeContext.DiagnosticCollection;
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.GetArray().Select(x => x.Entry.Name)));
        Assert.True(c.Binding.Result.IsComplete == valid, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.BindingFailure == BindingFailure.Access);
            Assert.False(c.Emission.Validate(out _));
        }
    }
}
