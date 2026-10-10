// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class AssociatedInferenceTest
{
    [Theory]
    [InlineData("Self.Item", "i32", "42", "i32")]
    [InlineData("Self.Item", "f32", "1.0", "f32")]
    [InlineData("Option<Self.Item>", "Option<i32>", "Option<i32>.None", "i32")]
    [InlineData("(Self.Item, bool)", "(i32, bool)", "(42, true)", "i32")]
    [InlineData("[2 of Self.Item]", "[2 of i32]", "[1, 2]", "i32")]
    [InlineData("Self.Item", "()", "()", "()")]
    [InlineData("Self.Item", "(i32, bool)", "(42, true)", "Tuple")]
    public void DeclaredResultSuppliesCompleteType(string required, string actual, string body, string inferred)
    {
        var c = Bound($"contract C\n    associate Item\n    func read() -> {required}\nstruct S\n    Self is C\n    public func read() -> {actual} => {body}");
        Assert.Equal(inferred, Assert.Single(Definition(c).AssociatedTypes).Value.Name);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("struct S<T>")]
    [InlineData("enum S<T>")]
    public void GenericDefinitionsRetainTheirTypeParameter(string declaration)
    {
        var c = Bound("contract C\n    associate Item\n    func read(value: Self) -> Self.Item\n" + declaration + "\n    Self is C\n" + (declaration.StartsWith("enum", StringComparison.Ordinal) ? "    Empty\n" : string.Empty) + "    public func read(value: Self) -> T => $abort(\"unused\")");
        Assert.Equal(BoundTypeKind.Parameter, Assert.Single(Definition(c).AssociatedTypes).Value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    associate C.Item is Copy\n")]
    [InlineData("    associate C.Item is i32\n")]
    public void ExplicitAndCapabilityOnlySpecificationsShareTheMapping(string specification)
    {
        var c = Bound("contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n" + specification + "    public func read() -> i32 => 1");
        Assert.Equal("i32", Assert.Single(Definition(c).AssociatedTypes).Value.Name);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" and P", "")]
    [InlineData(" and P", "contract L: P\ncontract R: P\n")]
    public void RedundantParentSharesEvidenceWithoutRequiringAChildCertificate(string direct, string diamond)
    {
        var parents = diamond.Length == 0 ? "P" : "L, R";
        var c = Bound("contract P\n    associate Item\n" + diamond + "contract C: " + parents + "\n    func read() -> Self.Item\nstruct S\n    Self is C" + direct + "\n    public func read() -> i32 => 1");
        Assert.Equal("i32", Assert.Single(Definition(c).AssociatedTypes).Value.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameNominalUnitNeverChainsInference(bool reverse)
    {
        var clauses = reverse ? "    Self is D\n    Self is C" : "    Self is C\n    Self is D";
        var source = "contract C\n    associate Item\n    func read() -> Self.Item\ncontract D\n    associate Output\n    func make() -> Self.Output\nstruct S\n" + clauses + "\n    public func read() -> i32 => 1\n    public func make() -> Self.(C).Item => 1";
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
        Assert.False(Definition(c, "D").IsVerified);
        Bound(source.Replace("    public func read()", "    associate C.Item is i32\n    public func read()", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentExternalTypeValuesDoNotDependOnDeclarationOrder(bool reverse)
    {
        const string contract = "contract C\n    associate Item\n    func read() -> Self.Item\n";
        const string origin = "struct Origin\n    Self is C\n    public func read() -> i32 => 1\n";
        const string consumer = "struct S\n    Self is C\n    public func read() -> Origin.(C).Item => 1\n";
        var c = Bound(contract + (reverse ? consumer + origin : origin + consumer));
        Assert.Equal("i32", Assert.Single(Definition(c).AssociatedTypes).Value.Name);
    }

    [Fact]
    public void NominalMutualReferencesNeedNoConformanceCycle()
    {
        Bound("contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> Other => Other.init()\nstruct Other\n    Self is C\n    public func read() -> S => S.init()");
    }

    [Theory]
    [InlineData("func read(value: Self.Item) -> Self.Item", "public func read(value: i32) -> i32 => value")]
    [InlineData("func read<T>(value: T) -> Self.Item", "public func read<U>(value: U) -> U => value")]
    [InlineData("func read(self: ref/Self) -> Self.Item", "public func read(self: ref/Self) -> ref/Self => self")]
    [InlineData("func read() -> Self.Item\n    func other() -> Self.Item", "public func read() -> i32 => 1\n    public func other() -> f32 => 1.0")]
    [InlineData("func read() -> Option<Self.Item>", "public func read() -> i32 => 1")]
    [InlineData("func read() -> (i32) -> Self.Item", "public func read() -> (i32) -> i32 => $abort(\"unused\")")]
    [InlineData("property item: Self.Item has get", "public let item: i32 = 1")]
    public void ExcludedEvidenceCannotCertifyAConformance(string requirement, string implementation)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Item\n    " + requirement + "\nstruct S\n    Self is C\n    " + implementation);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void OmittedResultIsUnitWithoutBodyInference()
    {
        var c = Bound("contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() => 123");
        Assert.Same(BoundType.Unit, Assert.Single(Definition(c).AssociatedTypes).Value);
    }

    [Fact]
    public void IteratorUsesThePublishedFamilyEquality()
    {
        var c = Bound("struct S\n    Self is Iterator\n    public func next(self: uniq/Self) -> Option<i32> => Option<i32>.None");
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var definition = c.Binding.GetConformanceDefinition(type.TypeOf()!, c.Library.Iterator)!;
        Assert.Contains(definition.AssociatedTypes, x => x.Key.Symbol.Name == "Item" && x.Value.Name == "i32");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OuterOriginsArePreservedWithOrWithoutExplicitSpecification(bool explicitBinding)
    {
        var source = "struct S {source}\n    contract C\n        associate Item\n        func read(self: ref/Self) -> Self.Item\n    Self is C\n" +
            (explicitBinding ? "    associate C.Item is ref/i32 during source\n" : string.Empty) +
            "    let value: ref/i32 during source\n    public func read(self: ref/Self) -> ref/i32 during source => self.value";
        var c = Bound(source);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = type.NestedContainers.Single(x => x.Name == "C");
        var bound = Assert.Single(c.Binding.GetConformanceDefinition(type.TypeOf()!, Assert.Single(type.ConstraintNodes).BoundConstraint!.Contract!)!.AssociatedTypes).Value;
        Assert.Equal(SemanticsKind.Ref, bound.Semantics);
        Assert.Same(type, bound.Origin!.Binder);
    }

    [Fact]
    public void AWholeFunctionTypeKeepsItsOwnBinders()
    {
        var c = Bound("contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> (ref/i32) -> ref/i32 => $abort(\"unused\")");
        Assert.Equal(BoundTypeKind.Function, Assert.Single(Definition(c).AssociatedTypes).Value.Kind);
    }

    [Theory]
    [InlineData("Copy", true)]
    [InlineData("Owned", false)]
    public void ConditionalChildEvidenceNeedsItsPremisesAtTheParent(string condition, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("contract P\n    associate Item\ncontract C: P\n    func read() -> Self.Item\nstruct S<T>\n    T is Copy\n    Self is P\n    Self is C when T is " + condition + "\n    public func read() -> i32 => 1");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void LaterInvalidEvidenceCannotCertifyARedundantParent()
    {
        var c = CompilationTestHelper.ParseSuccess("public contract P\n    associate Item\npublic contract C: P\n    func read() -> Self.Item\npublic struct S\n    Self is C and P\n    internal func read() -> i32 => 1");
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c, "P").IsVerified);
        Assert.All(Definition(c, "P").Paths, static x => Assert.False(x.IsVerified));
    }

    [Fact]
    public void EditingResultsInvalidatesEvidenceAndRecoversWithoutOldValues()
    {
        const string source = "contract C\n    associate Item\n    func read() -> Self.Item\n    func other() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> i32 => 1\n    public func other() -> i32 => 2";
        var c = Bound(source);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var function = type.Members.OfType<FunctionKoto>().First(x => x.Name == "other");
        var original = function.ReturnType!;
        var donor = KotoTree.Walk(CompilationTestHelper.ParseSuccess("func other() -> f32 => 1.0").Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "other").ReturnType!;
        AssociatedInferenceMetrics? recovered = null;
        for (var i = 0; i < 3; i++)
        {
            Assert.True(KotoHelper.Replace(function, original, donor));
            Assert.False(c.Bind().IsComplete);
            Assert.False(Definition(c).IsVerified);
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.AssociatedTypeInferenceFailed_Kd);
            Assert.True(KotoHelper.Replace(function, donor, original));
            Assert.True(c.Bind().IsComplete, Describe(c));
            Assert.Equal("i32", Assert.Single(Definition(c).AssociatedTypes).Value.Name);
            if (recovered is { } retained)
            {
                Assert.Equal(retained, c.Binding.AssociatedMetrics);
            }

            recovered = c.Binding.AssociatedMetrics;
        }
    }

    [Fact]
    public void PublicDiagnosticsRetainTheCauseSourceAndIndependentError()
    {
        const string source = "contract C\n    associate Item\n    func read<T>(value: T) -> Self.Item\nstruct S\n    Self is C\n    public func read<U>(value: U) -> U => value\nlet wrong: i32 = true";
        var path = Path.GetFullPath("associated-inference.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == "AssociatedTypeInferenceFailed_Kd");
        Assert.Contains(result.Diagnostics, static x => x.Code == "TypeMismatch_Kd");
        Assert.Contains(error.Reason!, static x => x.Name == "reason" && x.Value.Contains("outside the conformance scope", StringComparison.Ordinal));
        Assert.Contains(error.Related!, static x => x.Role == "result");
        Assert.Contains(error.Related!, static x => x.Role == "binder");
        Assert.Empty(error.Repairs ?? []);
        Assert.Contains("outside the conformance scope", System.Text.Json.JsonSerializer.Serialize(result));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("outside the conformance scope", console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var records = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Contains(records, static x => x.Code == "AssociatedTypeInferenceFailed_Kd");
        }
    }

    [Fact]
    public void HoverShowsVerifiedInferredBindingsAndTheirDeclarationSource()
    {
        const string source = "contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> i32 => 1";
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.CollectHover = true;
        c.Kotonoha.AddSource(new("associated-inference.kimi", source));
        Assert.True(c.Bind().IsComplete, Describe(c));
        var snapshot = HoverBuilder.Create(c);
        var document = Assert.Single(snapshot.Documents).Value;
        var entry = document.Entries[document.Find(source.IndexOf("struct S", StringComparison.Ordinal) + 7)];
        var rendered = HoverRenderer.Render(entry.Info, false).Body!;
        Assert.Contains("Inferred associate C.Item is i32", rendered);
        Assert.Contains("source: read declared result", rendered);
        Assert.Single(Assert.Single(Definition(c).Paths).InferenceEvidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredAndExplicitMappingsExecuteThroughGenericCallsAndFunctionItems(bool explicitBinding)
    {
        var source = """
            contract C
                associate Item
                func read() -> Self.Item
            struct S
                Self is C
                SPECIFICATION
                public func read() -> i32 => 42
            func use<T>() -> i32
                T is C
                T.(C).Item is i32
                return T.read()
            let item = S.read
            require item() == 42 else => $abort("item")
            require use<S>() == 42 else => $abort("generic")
            Console.writeLine("inference")
            """.Replace("SPECIFICATION", explicitBinding ? "associate C.Item is i32" : string.Empty, StringComparison.Ordinal);
        ScalarEmissionTest.EmitFixture("AssociatedInference" + (explicitBinding ? "Explicit" : "Inferred"), source, "inference\n");
    }

    [Theory]
    [InlineData("declarations", 12)]
    [InlineData("requirements", 12)]
    [InlineData("candidates", 12)]
    [InlineData("depth", 6)]
    [InlineData("shared", 6)]
    [InlineData("refinement", 12)]
    [InlineData("external", 12)]
    [InlineData("unrelated", 12)]
    public void ScalingWorkloadsPreserveExplicitAndInferredOutcomes(string axis, int size)
    {
        foreach (var explicitBinding in new[] { false, true })
        {
            var c = Bound(Verification.AssociatedInferenceWorkloads.Create(axis, size, explicitBinding));
            var storage = c.Binding.AssociatedMetrics;
            for (var i = 0; i < 3; i++)
            {
                Assert.True(c.Bind().IsComplete, Describe(c));
                Assert.Equal(storage, c.Binding.AssociatedMetrics);
            }

            c.Kotonoha.AddSource(new("unrelated.kimi", "func unrelated() -> i32 => 1"));
            Assert.True(c.Bind().IsComplete, Describe(c));
        }
    }

    [Fact]
    public void KnownResultPortionsRetainOrdinaryOriginCompatibility()
    {
        Bound("struct S {longer, shorter}\n    origin longer outlives shorter\n    contract C\n        associate Item\n        func read() -> (Self.Item, ref/i32 during shorter)\n    Self is C\n    public func read() -> (i32, ref/i32 during longer) => $abort(\"unused\")");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ConflictingOriginsNeedAnExplicitCompatiblePublicType(bool explicitBinding, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("struct S {longer, shorter}\n    origin longer outlives shorter\n    contract C\n        associate Item\n        func read() -> Self.Item\n        func other() -> Self.Item\n    Self is C\n" +
            (explicitBinding ? "    associate C.Item is ref/i32 during shorter\n" : string.Empty) +
            "    public func read() -> ref/i32 during longer => $abort(\"unused\")\n    public func other() -> ref/i32 during shorter => $abort(\"unused\")");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Fact]
    public void OtherRequirementsCanMatchAfterCompletionWithoutSupplyingEvidence()
    {
        Bound("contract C\n    associate Item\n    func read() -> Self.Item\n    func consume(value: Self.Item)\nstruct S\n    Self is C\n    public func read() -> i32 => 1\n    public func consume(value: i32) => ()");
    }

    [Theory]
    [InlineData("ref/i32", "")]
    [InlineData("ref/i32 during a", "        origin a == static\n")]
    public void PartialResultsCompleteIndependentOriginsWithTheOrdinaryHeader(string result, string clause)
    {
        var c = Bound("contract C\n    associate Item\n    associate Other\n    func read() -> (Self.Item, Self.Other)\n    func other() -> Self.Other\nstruct S\n    Self is C\n    public func read() -> (" + result + ", Self.Other)\n" + clause + "        $abort(\"unused\")\n    public func other() -> i32 => 1");
        var item = Definition(c).AssociatedTypes.Single(x => x.Key.Symbol.Name == "Item").Value;
        Assert.Equal(SemanticsKind.Ref, item.Semantics);
        Assert.Same(BoundOrigin.Static, item.Origin);
    }

    [Theory]
    [InlineData("contract C\n    associate Item is Copy\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> string => \"x\"")]
    [InlineData("contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> Other.(C).Item => 1\nstruct Other\n    Self is C\n    public func read() -> S.(C).Item => 1")]
    [InlineData("contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> i32 => 1\n    public func read() -> f32 => 1.0")]
    public void CapabilitiesCyclesAndDuplicateDeclarationsRemainErrors(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
    }

    [Fact]
    public void Vec2InfersAllFourIndependentArithmeticOutputs()
    {
        const string source = """
            struct Vec2
                Self is Addable<Self> and Multipliable<f32> and LeftMultipliable<f32> and Negatable
                public let x: f32
                public let y: f32
                public init(x: f32, y: f32)
                    self.x = x
                    self.y = y
                drop => ()
                public func added(self: ref/Self, right: ref/Self) -> Self
                    return Self.init(self.x + right.x, self.y + right.y)
                public func multiplied(self: ref/Self, right: ref/f32) -> Self
                    return Self.init(self.x * right, self.y * right)
                public func multipliedFrom(left: ref/f32, self: ref/Self) -> Self
                    return self.multiplied(left)
                public func negated(self: ref/Self) -> Self
                    return Self.init(-self.x, -self.y)
            let value = Vec2.init(3.0, 4.0)
            let sum = value + value
            let right = value * 2.0
            let left = 2.0 * value
            let opposite = -value
            require sum.x == 6.0 and sum.y == 8.0 else => $abort("sum")
            require right.x == left.x and right.y == left.y else => $abort("directions")
            require opposite.x == -3.0 and opposite.y == -4.0 else => $abort("negation")
            Console.writeLine("vec2")
            """;
        ScalarEmissionTest.EmitFixture("AssociatedInferenceVec2", source, "vec2\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmInferenceAndRetainedWitnessesAllocateNothing()
    {
        var c = Bound("contract C\n    associate Item\n    func read() -> Option<Self.Item>\nstruct S\n    Self is C\n    public func read() -> Option<i32> => Option<i32>.None");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Inference lost its retained conformance.");
            }
        }));
    }

    private static Compilation Bound(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source, "associated-inference.kimi");
        Assert.True(c.Bind().IsComplete, Describe(c));
        return c;
    }

    private static BoundConformance Definition(Compilation c, string contractName = "C")
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == contractName);
        return c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.BoundSymbol!)!;
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
