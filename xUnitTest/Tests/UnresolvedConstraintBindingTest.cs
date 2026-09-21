// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class UnresolvedConstraintBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingConformanceReportsItsNameWithoutDerivedDeclarationErrors(bool missingFirst)
    {
        var clauses = missingFirst ? "    Self is Missing\n    Self is Copy\n" : "    Self is Copy\n    Self is Missing\n";
        var c = Parse("public struct Reading {}\n" + clauses + "    public let value: i32");
        Assert.False(c.Bind().IsComplete);
        var reading = Container(c, "Reading");
        Assert.Equal(BindingState.Invalid, reading.BindingState);
        Assert.False(Assert.IsType<PropertyKoto>(Assert.Single(reading.Members)).BoundSymbol!.Property!.IsVerified);
        Assert.Equal(ConstraintProof.Error, c.Binding.Prove(reading.ConstraintNodes.Single(x => x.Right.ToString() == "Copy").BoundConstraint!, reading));
        c.Binding.ReportDiagnostics();
        var diagnostic = Assert.Single(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal("UnresolvedBinding_Kd", diagnostic.Entry.Name);
        Assert.Equal("Missing", diagnostic.SourceDocument!.SourceText.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.False(c.Binding.CheckBound().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Single(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
    }

    [Fact]
    public void MissingConformanceDoesNotHideIndependentErrorsOrCertifyAUse()
    {
        var c = Parse("""
            public contract Marker
            public struct Reading {}
                Self is Copy
                Self is Missing
                Self is Marker
                public let value: i32
                public let other: MissingField
                public func wrong() -> i32 => true
            func take<T>(value?: ref/T) -> i32
                T is Marker
                return 1
            func use(value?: ref/Reading) -> i32 => take(value)
            struct Contradiction<T> {}
                T is i32
                T is not i32
            """);
        Assert.False(c.Bind().IsComplete);
        var reading = Container(c, "Reading");
        var marker = Container(c, "Marker");
        Assert.False(c.Binding.GetConformanceDefinition(reading.BoundType!, marker.BoundSymbol!)!.IsVerified);
        var use = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single(x => x.Name == "use");
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, use) || ReferenceEquals(x.Node, use.ExpressionBody));
        c.Binding.ReportDiagnostics();
        var diagnostics = c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray();
        Assert.Contains(diagnostics, x => x.Entry.Name == "UnresolvedBinding_Kd" && x.SourceDocument!.SourceText.Substring(x.Span.Start, x.Span.Length) == "Missing");
        Assert.Contains(diagnostics, x => x.Entry.Name == "UnresolvedBinding_Kd" && x.SourceDocument!.SourceText.Substring(x.Span.Start, x.Span.Length) == "MissingField");
        Assert.Contains(diagnostics, x => x.Entry.Name == "TypeMismatch_Kd");
        Assert.Contains(diagnostics, x => x.Entry.Name == "InvalidConstraint_Kd" && x.Span.Start == Container(c, "Contradiction").Span.Start);
        Assert.DoesNotContain(diagnostics, x => x.Entry.Name == "InvalidConstraint_Kd" && x.Span.Start == reading.Span.Start);
    }

    [Fact]
    public void ConformanceDiagnosticCausesAreRebuiltAfterSourceChanges()
    {
        var c = Parse("public struct Reading {}\n    Self is Copy\n    Self is Missing\n    public let value: i32");
        Assert.False(c.Bind().IsComplete);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Missing"));
        Assert.True(c.Bind().IsComplete);
        Assert.Empty(c.Binding.Issues);
        c.Binding.ReportDiagnostics();
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void MissingConformanceInAnotherFragmentKeepsItsOwnLocation()
    {
        var c = Parse("public struct Reading {}\n    public let value: i32");
        c.Kotonoha.AddSource(new SourceDocument("Other.kimi", "public struct Reading {}\n    Self is Copy\n    Self is Missing\n    public let other: i32"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Other.kimi").GetArray());
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        var diagnostic = Assert.Single(c.Kimigayo.GetOrAddDiagnosticCollection("Other.kimi").GetArray());
        Assert.Equal("UnresolvedBinding_Kd", diagnostic.Entry.Name);
        Assert.Equal("Other.kimi", diagnostic.SourceDocument!.Path);
    }

    [Fact]
    public void MultipleMissingConformanceNamesRemainSeparateCauses()
    {
        var c = Parse("public struct Reading {}\n    Self is Copy\n    Self is Missing and Other\n    public let value: i32");
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var diagnostics = c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("UnresolvedBinding_Kd", x.Entry.Name));
        Assert.Equal(new[] { "Missing", "Other" }, diagnostics.OrderBy(x => x.Span.Start).Select(x => x.SourceDocument!.SourceText.Substring(x.Span.Start, x.Span.Length)));
    }

    [Fact]
    public void WarmMissingConformanceBindingReusesDiagnosticCauseStorage()
    {
        var c = Parse("public struct Reading {}\n    Self is Copy\n    Self is Missing\n    public let value: i32");
        for (var i = 0; i < 100; i++)
        {
            Assert.False(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Missing conformance became valid.");
            }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingConformanceDoesNotHideContradictoryInputsOfTheSameType(bool missingFirst)
    {
        const string Missing = "    Self is Missing\n";
        const string Inputs = "    T is i32\n    T is not i32\n";
        var c = Parse("public struct Reading<T> {}\n" + (missingFirst ? Missing + Inputs : Inputs + Missing));
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var diagnostics = c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray();
        Assert.Contains(diagnostics, x => x.Entry.Name == "UnresolvedBinding_Kd");
        Assert.Contains(diagnostics, x => x.Entry.Name == "InvalidConstraint_Kd" && x.Span.Start == Container(c, "Reading").Span.Start);
    }

    [Theory]
    [InlineData("public struct Target {}\n    Future is Copy", "public struct Future {}\n    Self is Copy", true)]
    [InlineData("public contract Target\n    Future is Copy", "public struct Future {}\n    Self is Copy", true)]
    [InlineData("public enum Target\n    Future is Copy\n    A", "public struct Future {}\n    Self is Copy", true)]
    [InlineData("public struct Target {}\n    i32 is Future", "public struct Future {}", false)]
    [InlineData("public struct Target<T> {}\n    T is Future", "public contract Future", true)]
    [InlineData("group G\n    func take<T>()\n        T is Future\n        ()", "public contract Future", true)]
    [InlineData("public struct Target {}\n    [2 of Future] is Copy", "public struct Future {}\n    Self is Copy", true)]
    [InlineData("public struct Target {}\n    (Future, i32) is Copy", "public struct Future {}\n    Self is Copy", true)]
    [InlineData("public struct Target {}\n    (Future) -> i32 is Owned", "public struct Future {}", true)]
    [InlineData("public struct Target<T> {}\n    T is [2 of Future]", "public struct Future {}", true)]
    [InlineData("public struct Target {}\n    Self is Future", "public contract Future", true)]
    [InlineData("public struct Target<T> {}\n    T is not Future", "public contract Future", true)]
    public void MissingDeclarationsRemainUnresolvedUntilFinalBinding(string source, string generated, bool valid)
    {
        var c = Parse(source);
        var provisional = c.Binding.Bind(BindingMode.Provisional);
        Assert.Equal(0, provisional.InvalidCount);
        Assert.True(provisional.UnresolvedCount > 0);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", generated));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Generated.kimi").GetArray());
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("public struct Target {}\n    Future is Copy")]
    [InlineData("public contract Target\n    Future is Copy")]
    [InlineData("public enum Target\n    Future is Copy\n    A")]
    [InlineData("public struct Target<T> {}\n    T is Future")]
    [InlineData("group G\n    func take<T>()\n        T is Future\n        ()")]
    public void FinalBindingRejectsNamesThatRemainMissing(string source)
    {
        var c = Parse(source);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure is BindingFailure.MissingType or BindingFailure.InvalidConstraint);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("public group Future\npublic struct Target {}\n    i32 is Future")]
    [InlineData("public group Invalid\npublic struct Target {}\n    (Future, Invalid) is Copy")]
    [InlineData("public group Invalid\npublic struct Target<T> {}\n    T is Future or Invalid")]
    [InlineData("public struct Target<s/T> {}\n    s is Future")]
    public void AvailableViolationsRemainInvalidInProvisionalBinding(string source)
    {
        var c = Parse(source);
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("Copy or Future")]
    [InlineData("Future or Copy")]
    [InlineData("not ((not Copy) and Future)")]
    public void ATrueBooleanBranchDoesNotCompleteAnUnformedRequirement(string requirement)
    {
        var c = Parse("public contract Marker\npublic struct Target {}\n    i32 is " + requirement + "\n    Self is Marker");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var target = Container(c, "Target");
        Assert.Equal(BindingState.Unresolved, target.BindingState);
        Assert.False(c.Binding.GetConformanceDefinition(target.BoundType!, Container(c, "Marker").BoundSymbol!)!.IsVerified);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future"));
        Assert.True(c.Bind().IsComplete, string.Join(", ", c.Binding.Issues.Select(x => $"{x.Node.Akind}:{x.Node.BindingFailure}:{x.Node.Span}")));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Future", false)]
    [InlineData("Future or Copy", true)]
    public void MissingFunctionRequirementsCannotPublishCompletedCalls(string requirement, bool valid)
    {
        var c = Parse("group G\n    func take<T>()\n        T is " + requirement + "\n        ()\n    func call() => take<i32>()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Container(c, "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "take");
        Assert.Equal(BindingState.Unresolved, function.BindingState);
        var call = Assert.IsType<InvocationKoto>(Container(c, "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody);
        Assert.Null(call.BoundCall);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future"));
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, call.BoundCall is not null);
    }

    [Fact]
    public void MissingPropositionsAreNotAddedAsAssumptions()
    {
        var c = Parse("public struct Target<T> {}\n    T is Future\n    T is not Other");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var target = Container(c, "Target");
        foreach (var clause in target.ConstraintNodes)
        {
            Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(clause.BoundConstraint!, target));
        }

        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future\npublic contract Other"));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void FunctionSubjectRestrictionsRemainDiagnostics()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "group G\n    func take<T>()\n        Future is Copy\n        ()"));
        Assert.NotEmpty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void WarmMissingNamePassesAllocateNothing()
    {
        var c = Parse("public struct Target {}\n    [2 of Future] is Copy");
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.Bind(BindingMode.Provisional).InvalidCount != 0)
            {
                throw new InvalidOperationException("Missing constraint declaration became invalid.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    private static DeclarationContainerKoto Container(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        return c;
    }
}
