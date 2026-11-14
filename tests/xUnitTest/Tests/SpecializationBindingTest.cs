// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class SpecializationBindingTest
{
    private const string Ordinary = "func weight<T>(value: ref/T) -> i32 => 1\n";

    // SPEC 8.13: a length slot is supplied as an evaluated constant and enters the selection key (LengthKey(N)).
    private const string Lengths = "func pick<length N, T>(values: ref/[N of T]) -> i32 => 0\n";

    [Fact]
    public void LengthSlotsSelectTheSpecialization()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationLengthSlots",
            Lengths + "specialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 1\nfunc forward<length N, T>(values: ref/[N of T]) -> i32 => pick<N, T>(values)\nlet a: [3 of i32] = [1, 2, 3]\nlet b: [2 of i32] = [1, 2]\nrequire pick<3, i32>(a@ref) == 1 and pick<2, i32>(b@ref) == 0 and forward<3, i32>(a@ref) == 1 and forward<2, i32>(b@ref) == 0 else => $abort(\"selection\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.1, 8.13.3: closed compound arguments (Tuple, fixed array) form distinct selection keys.
    [Fact]
    public void CompoundArgumentsSelectTheirSpecializations()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationCompound",
            Ordinary + "specialize func weight<(i32, bool)>(value: ref/(i32, bool)) -> i32 => 2\nspecialize func weight<[2 of i32]>(value: ref/[2 of i32]) -> i32 => 3\nlet pair = (1, true)\nlet array: [2 of i32] = [1, 2]\nlet n: i32 = 0\nrequire weight<(i32, bool)>(pair@ref) == 2 and weight<[2 of i32]>(array@ref) == 3 and weight<i32>(n@ref) == 1 else => $abort(\"weight\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.3: function references obey the same choice: an explicit Item, an Item selected by its expected signature and an Item
    // formed in a generic body run the specialization their key selects, and an Item whose key selects none runs the original.
    [Fact]
    public void FunctionItemsSelectTheSpecialization()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationFunctionItem",
            Ordinary + "specialize func weight<i32>(value: ref/i32) -> i32 => 2\nfunc relay<T>(value: ref/T) -> i32\n    let item = weight<T>\n    return item(value)\nlet n: i32 = 4\nlet flag = true\nlet f = weight<i32>\nlet g: (ref/i32) -> i32 = weight\nlet h = weight<bool>\nrequire f(n@ref) == 2 and g(n@ref) == 2 and relay<i32>(n@ref) == 2 and h(flag@ref) == 1 and relay<bool>(flag@ref) == 1 else => $abort(\"item\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.2: the original's generic Constraints are inherited; the closed arguments must satisfy them.
    private const string Constrained = "func twice<T>(value: T) -> T\n    T is Copy\n    return value\n";

    [Fact]
    public void ConstrainedOriginalsAcceptSatisfyingSpecializations()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationConstrained",
            Constrained + "specialize func twice<i32>(value: i32) -> i32 => value + value\nrequire twice<i32>(4) == 8 and twice<bool>(true) == true else => $abort(\"twice\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void RejectsSpecializationsThatViolateInheritedConstraints()
    {
        var c = MinimalEmissionTest.Analyze("struct Token\n    drop => ()\n" + Constrained + "specialize func twice<Token>(value: Token) -> Token => value@move");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.UnsatisfiedConstraint_Kd);
    }

    // SPEC 8.13.1: an instance method's specialization restates the receiver at the original's position.
    private const string Receiver = "struct Scale\n    var unit: i32 = 1\n    public func weight<U>(self: ref/Self, other: ref/U) -> i32 => self.unit\n";

    [Fact]
    public void ReceiverSpecializationsSelectThroughMethodCalls()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationReceiver",
            Receiver + "    specialize func weight<i32>(self: ref/Self, other: ref/i32) -> i32 => self.unit + other\nfunc relay<U>(scale: ref/Scale, other: ref/U) -> i32 => scale.weight<U>(other)\nlet scale = Scale.init()\nlet n: i32 = 4\nlet flag = true\nrequire scale.weight<i32>(n@ref) == 5 and scale.weight<bool>(flag@ref) == 1 and relay<i32>(scale@ref, n@ref) == 5 else => $abort(\"weight\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Theory]
    [InlineData("    specialize func weight<i32>(self: uniq/Self, other: ref/i32) -> i32 => 2")]
    [InlineData("    specialize func weight<i32>(other: ref/i32) -> i32 => 2")]
    [InlineData("    specialize func weight<i32>(self: ref/Self, other: ref/i32) -> i64 => 2")]
    public void RejectsMismatchedReceiverContracts(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Receiver + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    // SPEC 8.13.2: the header inherits the original's boundary and defaults; callers still use the original omission rules.
    private const string Defaults = "func find<T>(value: T, count: i32 = 1) -> i32 => count\n";

    [Fact]
    public void InheritsDefaultsWithoutRedeclaringThem()
    {
        var c = MinimalEmissionTest.Analyze(Defaults + "specialize func find<i32>(value: i32, count: i32) -> i32 => count + 1\nrequire find<i32>(10) == 2 and find<i32>(10, count: 5) == 6 and find<bool>(true) == 1 else => $abort(\"defaults\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // SPEC 8.13.2: a redeclared default is the specialization check's IncompatibleContractImplementation_Kd. The parser owns a
    // redeclared modifier or `!` boundary (syntax corpus `specialization-access-modifier`, `specialization-argument-boundary`).
    [Fact]
    public void RejectsRedeclaredDefaults()
    {
        var c = MinimalEmissionTest.Analyze(Defaults + "specialize func find<i32>(value: i32, count: i32 = 1) -> i32 => count");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // SPEC 8.13.2: written binder names are inherited from the original; the body serves every admitted binding.
    private const string Named = "func first<T>(values: ref/[3 of T] during source) -> ref/T during source => values[0]@ref/T\n";

    [Fact]
    public void InheritedBindersKeepTheOriginalContract()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationInheritedBinders",
            Named + "specialize func first<i32>(values: ref/[3 of i32] during source) -> ref/i32 during source => values[2]@ref/i32\nfunc forward<T>(values: ref/[3 of T] during source) -> ref/T during source => first<T>(values)\nlet a: [3 of i32] = [1, 2, 3]\nlet b: [3 of bool] = [true, false, false]\ndo\n    let local: [3 of i32] = [7, 8, 9]\n    require first<i32>(local@ref) == 9 else => $abort(\"local\")\nrequire first<i32>(a@ref) == 3 and forward<i32>(a@ref) == 3 and first<bool>(b@ref) == true else => $abort(\"first\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void OmittedBinderNamesAreInheritedThroughTheirInputs()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationOmittedBinders",
            Named + "specialize func first<i32>(values: ref/[3 of i32]) -> ref/i32 => values[2]@ref/i32\nlet a: [3 of i32] = [1, 2, 3]\ndo\n    let local: [3 of i32] = [7, 8, 9]\n    require first<i32>(local@ref) == 9 else => $abort(\"local\")\nrequire first<i32>(a@ref) == 3 else => $abort(\"first\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Theory]
    [InlineData("specialize func first<i32>(values: ref/[3 of i32] during other) -> ref/i32 during other => values[2]@ref/i32")]
    [InlineData("specialize func first<i32>(values: ref/[3 of i32] during source) -> ref/i32 during static => values[2]@ref/i32")]
    [InlineData("specialize func first<i32>(values: ref/[3 of i32] during source) -> ref/i32 during source => values[2]@ref/i32\nspecialize func first<i32>(values: ref/[3 of i32] during source) -> ref/i32 during source => values[1]@ref/i32")]
    public void RejectsRenamedNarrowedOrDuplicateBinders(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Named + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("specialize func pick<2, i32>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<i32, 3>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<N, i32>(values: ref/[3 of i32]) -> i32 => 1")]
    [InlineData("specialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 1\nspecialize func pick<3, i32>(values: ref/[3 of i32]) -> i32 => 2")]
    public void RejectsInvalidLengthSlots(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Lengths + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i64>(value: ref/i64) -> i32 => 3")]
    [InlineData("specialize func weight<i32>(value => renamed: ref/i32) -> i32 => 2")]
    public void AcceptsClosedImplementation(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization + "\nfunc forward<T>(value: ref/T) -> i32 => weight<T>(value)\nlet value: i32 = 4\nlet result = forward<i32>(value@ref/i32)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("specialize func missing<i32>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i64) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i64 => 2")]
    [InlineData("specialize func weight<i32>(other: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<i32, i64>(value: ref/i32) -> i32 => 2")]
    [InlineData("specialize func weight<T>(value: ref/T) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i32 during static) -> i32 => 2")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => true")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32 => 2\nspecialize func weight<i32>(value: ref/i32) -> i32 => 3")]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i32\n    i32 is Copy\n    return 2")]
    public void RejectsInvalidImplementation(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    // SPEC 8.13.2, 15.4.2 (PLAN G863): a Type argument's omitted Origin slots are the original's, each call's, and the restated
    // inputs and result inherit them: a ref, Slice and uniq slot, a nested one, one argument in two inputs or only in the result.
    private const string Box = "struct Box {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\nstruct W {source}\n    public let data: uniq/i32 during source\n    public init(data: uniq/i32 during source) => self.data = data\nstruct Wrap<T>\n    public let inner: T\n    public init(inner: T) => self.inner = inner@move\nfunc f<T>(x: ref/T) -> i32 => 0\n";

    [Theory]
    [InlineData("specialize func f<Box>(x: ref/Box) -> i32 => 1")]
    [InlineData("func g<T>(x: T) -> i32 => 0\nspecialize func g<Slice<u8>>(x: Slice<u8>) -> i32 => 1")]
    [InlineData("func w<T>(d: uniq/T) -> i32 => 0\nspecialize func w<W>(d: uniq/W) -> i32 => 1")]
    [InlineData("specialize func f<Wrap<Box>>(x: ref/Wrap<Box>) -> i32 => 1")]
    [InlineData("func two<T>(x: ref/T, y: Option<T>) -> i32 => 0\nspecialize func two<Box>(x: ref/Box, y: Option<Box>) -> i32 => 1")]
    [InlineData("func make<T>(x: i32) -> Option<T> => .None\nspecialize func make<Box>(x: i32) -> Option<Box> => .None")]
    [InlineData("func apply<T>(f: T) -> i32 => 0\nspecialize func apply<(i32) -> i32>(f: (i32) -> i32) -> i32 => 1")]
    public void ArgumentOriginsAreInheritedFromTheOriginal(string specialization)
    {
        var c = MinimalEmissionTest.Analyze(Box + specialization + "\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // SPEC 8.13.3: the key excludes Origins, so the specialization runs for a local Box and W, at a direct call and after substitution.
    [Fact]
    public void InheritedArgumentOriginsSelectTheSpecialization()
        => ScalarEmissionTest.EmitFixture(
            "SpecializationInheritedArgumentOrigins",
            Box + "specialize func f<Box>(x: ref/Box) -> i32 => 1\nfunc relay<T>(x: ref/T) -> i32 => f(x)\nfunc w<T>(d: uniq/T) -> i32 => 0\nspecialize func w<W>(d: uniq/W) -> i32 => 1\nfunc local() -> i32\n    let n = 5\n    let b = Box.init(n)\n    var m = 6\n    var u = W.init(m@uniq)\n    return f(b) + relay(b) * 10 + w(u@uniq) * 100\nrequire local() == 111 else => $abort(\"the generic body ran\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.13.2: the header writes no Origin of the inherited contract: a Type argument's slot written static, as a parameter's
    // Origin or inside a nested argument, and a restated input's slot written static restrict the bindings the original admits (the
    // second of two one-key specializations included). Each row lists its records' nodes in source order.
    [Theory]
    [InlineData("specialize func f<Box during static>(x: ref/Box) -> i32 => 1", "specialize func f<Box during static>(x: ref/Box) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("specialize func f<Box during x>(x: ref/Box) -> i32 => 1", "specialize func f<Box during x>(x: ref/Box) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("specialize func f<Option<Box during static>>(x: ref/Option<Box>) -> i32 => 1", "specialize func f<Option<Box during static>>(x: ref/Option<Box>) -> i32 => 1", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("specialize func f<Box>(x: ref/(Box during static)) -> i32 => 1", "specialize func f<Box>(x: ref/(Box during static)) -> i32 => 1", Kimi.DiagnosticCode.UnprovenOriginContract_Kd)]
    [InlineData("specialize func f<Box>(x: ref/Box) -> i32 => 1\nspecialize func f<Box during static>(x: ref/Box) -> i32 => 2", "specialize func f<Box during static>(x: ref/Box) -> i32 => 2", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("specialize func f<Box>(x: ref/Box) -> i32 => 1\nspecialize func f<Box>(x: ref/Box) -> i32 => 2", "specialize func f<Box>(x: ref/Box) -> i32 => 1|specialize func f<Box>(x: ref/Box) -> i32 => 2", Kimi.DiagnosticCode.DuplicateBinding_Kd)]
    public void RejectsWrittenArgumentOrigins(string specialization, string located, Kimi.DiagnosticCode code)
        => AssertRecords(specialization, located, code);

    // SPEC 8.13.2, 15.4.2 (PLAN G863): an omitted Origin of T's binding that no row completes yet is one located Unsupported_Kd per
    // occurrence, and the header rests on it: a borrow layer in a Type argument or nested in a restated input, a Function Type result
    // that no input of its own gives, and an undecided inherited Constraint over the header's universal Origins (`T is Owned`).
    [Theory]
    [InlineData("func h<T>(x: T) -> i32 => 0\nspecialize func h<ref/i32>(x: ref/i32) -> i32 => 1", "ref/i32")]
    [InlineData("func h<T>(x: T) -> i32 => 0\nspecialize func h<uniq/i32>(x: uniq/i32) -> i32 => 1", "uniq/i32")]
    [InlineData("func h<T>(x: T) -> i32 => 0\nspecialize func h<Wrap<ref/i32>>(x: Wrap<ref/i32>) -> i32 => 1", "ref/i32|ref/i32")]
    [InlineData("func g<T>(x: Option<ref/T during a>) -> i32 => 0\nspecialize func g<i32>(x: Option<ref/i32>) -> i32 => 1", "ref/i32")]
    [InlineData("func apply<T>(f: ref/T) -> i32 => 0\nspecialize func apply<() -> ref/i32>(f: ref/(() -> ref/i32)) -> i32 => 1", "ref/i32")]
    [InlineData("func apply<T>(f: T) -> i32 => 0\nspecialize func apply<() -> ref/i32>(f: () -> ref/i32) -> i32 => 1", "ref/i32")]
    [InlineData("func o<T>(x: ref/T) -> i32\n    T is Owned\n    return 0\nspecialize func o<Box>(x: ref/Box) -> i32 => 1", "specialize func o<Box>(x: ref/Box) -> i32 => 1")]
    public void UncompletedArgumentOriginsAreALocatedLimit(string specialization, string located)
        => AssertRecords(specialization, located, Kimi.DiagnosticCode.Unsupported_Kd);

    // SPEC 8.13.3: specializations never enter the candidate set; an invalid one does not fail calls to the original.
    [Fact]
    public void InvalidSpecializationDoesNotCascadeIntoCalls()
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + "specialize func weight<i32>(other: ref/i32) -> i32 => 2\nfunc forward<T>(value: ref/T) -> i32 => weight<T>(value)\nlet value: i32 = 4\nlet result = weight<i32>(value@ref) + forward<i32>(value@ref)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(c.Binding.Issues, x => Assert.Equal(Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd, x.Code));
    }

    // SPEC 8.13.1: diagnostics distinguish no target, an input-structure mismatch, multiple targets and a contract mismatch.
    [Theory]
    [InlineData("specialize func missing<i32>(value: ref/i32) -> i32 => 2", Kimi.DiagnosticCode.MissingSpecializationTarget_Kd)]
    [InlineData("specialize func weight<i32, i64>(value: ref/i32) -> i32 => 2", Kimi.DiagnosticCode.MissingSpecializationTarget_Kd)]
    [InlineData("specialize func weight<i32>(value: ref/i64) -> i32 => 2", Kimi.DiagnosticCode.SpecializationInputMismatch_Kd)]
    [InlineData("specialize func weight<i32>(value: ref/i32, other: i32) -> i32 => 2", Kimi.DiagnosticCode.SpecializationInputMismatch_Kd)]
    [InlineData("func weight<T>(other: ref/i32) -> i32 => 3\nspecialize func weight<i32>(value: ref/i32) -> i32 => 2", Kimi.DiagnosticCode.AmbiguousBinding_Kd)]
    [InlineData("specialize func weight<i32>(value: ref/i32) -> i64 => 2", Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd)]
    public void TargetIdentificationDiagnosticsAreDistinct(string specialization, Kimi.DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + specialization);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Fact]
    public void StillChecksOrdinaryBody()
    {
        var c = MinimalEmissionTest.Analyze("func weight<T>(value: ref/T) -> i32 => true\nspecialize func weight<i32>(value: ref/i32) -> i32 => 2\nlet value: i32 = 4\nlet result = weight<i32>(value@ref)");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void CannotUseResultsOrNamesToResolveTargetAmbiguity()
    {
        var c = MinimalEmissionTest.Analyze("func f<T>(value: T) -> i32 => 1\nfunc f<T>(other: i32) -> bool => true\nspecialize func f<i32>(value: i32) -> i32 => 2\n()");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void RebindingRechecksTheSpecializationSet()
    {
        var c = MinimalEmissionTest.Analyze(Ordinary + "specialize func weight<i32>(value: ref/i32) -> i32 => 2\n()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Bind().IsComplete);
        c.Kotonoha.AddSource(new Kimi.Compiler.SourceDocument("duplicate.kimi", "specialize func weight<i32>(value: ref/i32) -> i32 => 3"));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.MissingSpecializationTarget_Kd);
    }

    [Fact]
    public void InheritedOriginRelationsAreAvailableInTheSpecializedBody()
        => ScalarEmissionTest.EmitFixture("SpecializationInheritedRelations", "func choose<T>(a: ref/T, b: ref/T) -> ref/T during b\n    origin a outlives b\n    return a\nspecialize func choose<i32>(a: ref/i32, b: ref/i32) -> ref/i32 during b => a\nlet a = 7\nlet b = 9\nrequire choose(a@ref, b@ref) == 7 else => $abort(\"inherited relation\")", string.Empty);

    [Theory]
    [InlineData("origin a outlives b")]
    [InlineData("origin a == b")]
    [InlineData("origin a == static")]
    public void AFullSpecializationCannotAddAnOriginPrecondition(string clause)
    {
        var c = MinimalEmissionTest.Analyze("func choose<T>(a: ref/T, b: ref/T) -> i32 => 1\nspecialize func choose<i32>(a: ref/i32, b: ref/i32) -> i32\n    " + clause + "\n    return 2\n()");
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.FunctionKoto>().Single(x => x.IsSpecialization);
        Assert.Single(Kimi.Compiler.Parsing.OriginClauses.Get(implementation));
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.UnprovenOriginContract_Kd);
    }

    private static void AssertRecords(string specialization, string located, Kimi.DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Box + specialization + "\n()");
        Assert.False(c.Binding.Result.IsComplete);
        var issues = c.Binding.Issues.OrderBy(x => x.Node.Span.Start).ToArray();
        Assert.Equal(located.Split('|'), issues.Select(x => x.Node.ToString()));
        Assert.All(issues, x => Assert.Equal(code, x.Code));
    }
}
