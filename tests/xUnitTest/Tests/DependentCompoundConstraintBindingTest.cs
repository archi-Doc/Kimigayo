// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DependentCompoundConstraintBindingTest
{
    // The owner's O-42 (a): a constructed Type is no subject (SPEC F.6). Each row is such a subject, a syntax Error, beside its equivalent
    // inside the grammar, whose substitution decides the same outcome.
    [Theory]
    [InlineData("[2 of T] is Copy", "T is Copy", "i32", true)]
    [InlineData("[2 of T] is Copy", "T is Copy", "string", false)]
    [InlineData("(T, i32) is Copy", "T is Copy", "i32", true)]
    [InlineData("(T, i32) is Copy", "T is Copy", "string", false)]
    [InlineData("[2 of T] is not Copy", "T is not Copy", "string", true)]
    [InlineData("[2 of T] is not Copy", "T is not Copy", "i32", false)]
    [InlineData("[2 of T] is Copy and Owned", "T is Copy and Owned", "i32", true)]
    [InlineData("[2 of T] is Copy and Owned", "T is Copy and Owned", "string", false)]
    [InlineData("[2 of T] is Copy or [2 of string]", "T is Copy or string", "string", true)]
    [InlineData("[2 of T] is [2 of i32]", "T is i32", "i32", true)]
    [InlineData("[2 of T] is [2 of i32]", "T is i32", "u32", false)]
    [InlineData("((T)) is Copy", "T is Copy", "i32", true)]
    [InlineData("((T)) is Copy", "T is Copy", "string", false)]
    [InlineData("Box<T> is Box<i32>", "T is i32", "i32", true)]
    [InlineData("Box<T> is Box<i32>", "T is i32", "string", false)]
    public void AnInputIsCheckedAfterSubstitution(string constructed, string clause, string argument, bool valid)
    {
        AssertRejected(Use(constructed, argument));
        var c = MinimalEmissionTest.Analyze(Use(clause, argument));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target").StateOf());
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("struct", "i32", true)]
    [InlineData("struct", "string", false)]
    [InlineData("enum", "i32", true)]
    [InlineData("enum", "string", false)]
    public void SelfRetainsCopyDerivation(string kind, string argument, bool valid)
    {
        AssertRejected(Derived(kind, "((Self))", argument));
        var c = MinimalEmissionTest.Analyze(Derived(kind, "Self", argument));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void SelfDoesNotAssumeUnimplementedRequirements()
    {
        AssertRejected(Unimplemented("(Self)"));
        var c = MinimalEmissionTest.Analyze(Unimplemented("Self"));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        var target = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(target.TypeOf()!, contract.SymbolOf()!)?.IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssumptionsSupportGenericForwarding(bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    T is Copy\npublic struct Forward<U>\n    " + (valid ? "U is Copy" : "U is Owned") + "\n    var value: Target<U>");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("(T.Origin.Item, i32) is Copy", "i32", true)]
    [InlineData("(T.Origin.Item, i32) is Copy", "string", false)]
    [InlineData("[2 of T.Origin.Item] is Copy", "i32", true)]
    [InlineData("[2 of T.Origin.Item] is Copy", "string", false)]
    public void ProjectionInputsAreBoundAfterTheirRootPremises(string constructed, string item, bool valid)
    {
        AssertRejected(Projected(constructed, item));
        var c = MinimalEmissionTest.Analyze(Projected("T.Origin.Item is Copy", item));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target").StateOf());
        Assert.Equal(valid, CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void ContradictoryAssumptionsInvalidateTheDeclaration()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    T is Copy\n    T is not Copy");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single().StateOf());
    }

    [Fact]
    public void CopyPremiseSupportsStoredFieldDerivation()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    T is Copy\n    Self is Copy\n    var values: [2 of T]");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void ReplacingAnInputRechecksExistingUses()
    {
        const string source = "public struct Target<T>\n    T is Copy\npublic func use(value: Target<i32>)\n    return";
        var c = MinimalEmissionTest.Analyze(source);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes[0];
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(source.Replace("is Copy", "is not Copy", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes[0].Right;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmInputsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    T is Copy\npublic func use(value: Target<[2 of i32]>)\n    return");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Input constraint failed.");
            }
        }));
    }

    private static void AssertRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(nameof(DiagnosticCode.ExpectedSyntax_Kd), Assert.Single(TestDiagnostics.Of(c, "Hello.kimi")).Code);
    }

    private static string Use(string clause, string argument)
        => "public struct Box<T>\npublic struct Target<T>\n    " + clause + "\npublic func use(value: Target<" + argument + ">)\n    return";

    private static string Derived(string kind, string subject, string argument)
        => "public " + kind + " Target<T>\n    T is Copy\n    " + subject + " is Copy\n" + (kind == "enum" ? "    A(T)\n" : "    var value: T\n") + "public func use(value: Target<" + argument + ">)\n    return";

    private static string Unimplemented(string subject)
        => "public contract C\n    func read(self: ref/Self) -> i32\npublic struct Target<T>\n    " + subject + " is C";

    private static string Projected(string clause, string item)
        => "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is " + item + "\npublic struct Target<T>\n    " + clause + "\n    T is Origin\npublic func use(value: Target<Source>)\n    return";
}
