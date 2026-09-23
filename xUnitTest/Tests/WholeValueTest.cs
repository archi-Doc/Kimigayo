// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class WholeValueTest
{
    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", true)]
    [InlineData("()", true)]
    [InlineData("Never", false)]
    [InlineData("S", true)]
    [InlineData("B", false)]
    [InlineData("Cell<B>", true)]
    [InlineData("(B, i32)", true)]
    [InlineData("[2 of B]", true)]
    [InlineData("(i32) -> i32", true)]
    [InlineData("ref/i32", false)]
    [InlineData("uniq/S", false)]
    [InlineData("unsafe/i32", false)]
    [InlineData("obj/S", false)]
    [InlineData("rc/S", false)]
    public void SealedTestsOnlyOuterCore(string type, bool expected)
    {
        var c = Parse($"open struct B\nstruct S\nstruct Cell<T>\n    let item: T\nfunc inspect(x: {type}) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single(x => x.Name == "inspect");
        Assert.Equal(expected ? ConstraintProof.Proven : ConstraintProof.Refuted, c.Binding.ProveSealed(f.Parameters[0].Type.BoundType!, f));
    }

    [Theory]
    [InlineData("objref", "ref", true)]
    [InlineData("objuniq", "ref", true)]
    [InlineData("objuniq", "uniq", true)]
    [InlineData("objref", "uniq", false)]
    [InlineData("rc", "uniq", false)]
    [InlineData("arc", "uniq", false)]
    public void GenericPayloadProjectionRequiresCapability(string source, string target, bool expected)
    {
        var c = Parse($"func project<T>(x: {source}/T) -> {target}/T during x\n    T is Sealed\n    return x@{target}/T");
        Assert.Equal(expected, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("func f<T>(x: objref/T) -> ref/T during x => x@ref/T")]
    [InlineData("open struct S\nfunc f(x: objref/S) -> ref/S during x => x@ref/S")]
    [InlineData("struct S\nfunc read(x: ref/S) => ()\nfunc f(x: objref/S) => read(x)")]
    [InlineData("struct S\n    Self is Sealed")]
    [InlineData("contract C: Sealed\nopen struct S\n    Self is C")]
    public void RejectsMissingOrManufacturedPayloadEvidence(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("Kimi.Intrinsics.replace(x@uniq, with: 2)")]
    [InlineData("let previous = Kimi.Intrinsics.exchange(x@uniq, with: 2)")]
    [InlineData("Kimi.Intrinsics.swap(x@uniq, y@uniq)")]
    public void UpdateSignaturesBindNormally(string expression)
    {
        var c = Parse("var x: i32 = 1\nvar y: i32 = 2\n" + expression);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("Scalar", "var x: i32 = 1\nvar y: i32 = 9\nKimi.Intrinsics.replace(x@uniq, with: 2)\nlet old = Kimi.Intrinsics.exchange(x@uniq, with: 3)\nKimi.Intrinsics.swap(x@uniq, y@uniq)\nif old == 2 and x == 9 and y == 3 => Console.writeLine(\"ok\")", "ok\n")]
    [InlineData("String", "var x = \"old\"\nvar y = \"other\"\nlet old = Kimi.Intrinsics.exchange(x@uniq, with: \"new\")\nKimi.Intrinsics.swap(x@uniq, y@uniq)\nConsole.writeLine(old)\nConsole.writeLine(x)\nConsole.writeLine(y)", "old\nother\nnew\n")]
    [InlineData("ReplaceDestroy", "struct S\n    let value: i32\n    public init(value: i32) => self.value = value\n    deinit\n        if self.value == 1 => Console.writeLine(\"drop 1\") else => Console.writeLine(\"drop 2\")\nvar x = S.init(1)\nKimi.Intrinsics.replace(x@uniq, with: S.init(2))\nConsole.writeLine(\"placed\")", "drop 1\nplaced\ndrop 2\n")]
    [InlineData("SwapDestroy", "struct S\n    public let value: i32\n    public init(value: i32) => self.value = value\n    deinit\n        if self.value == 1 => Console.writeLine(\"drop 1\") else => Console.writeLine(\"drop 2\")\nvar x = S.init(1)\nvar y = S.init(2)\nKimi.Intrinsics.swap(x@uniq, y@uniq)\nif x.value == 2 and y.value == 1 => Console.writeLine(\"ok\")", "ok\ndrop 1\ndrop 2\n")]
    public void EmitsWholeUpdates(string name, string source, string stdout)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), failure);
        ScalarEmissionTest.EmitFixture("WholeValue" + name, source, stdout);
    }

    [Theory]
    [InlineData("Replace", "func update(x: uniq/S) => Kimi.Intrinsics.replace(x, with: S.init(2))\nvar x = S.init(1)\nupdate(x@uniq)\nif x.value == 2 => Console.writeLine(\"ok\")", "drop 1\nok\ndrop 2\n")]
    [InlineData("Exchange", "func update(x: uniq/S) -> S => Kimi.Intrinsics.exchange(x, with: S.init(2))\nvar x = S.init(1)\nlet old = update(x@uniq)\nif old.value == 1 and x.value == 2 => Console.writeLine(\"ok\")", "ok\ndrop 1\ndrop 2\n")]
    [InlineData("Swap", "func update(x: uniq/S, y: uniq/S) => Kimi.Intrinsics.swap(x, y)\nvar x = S.init(1)\nvar y = S.init(2)\nupdate(x@uniq, y@uniq)\nif x.value == 2 and y.value == 1 => Console.writeLine(\"ok\")", "ok\ndrop 1\ndrop 2\n")]
    public void EmitsBorrowedUpdates(string name, string body, string stdout)
    {
        const string declaration = "struct S\n    public let value: i32\n    public init(value: i32) => self.value = value\n    deinit\n        if self.value == 1 => Console.writeLine(\"drop 1\") else => Console.writeLine(\"drop 2\")\n";
        this.EmitsWholeUpdates("Borrowed" + name, declaration + body, stdout);
    }

    [Fact]
    public void ScalarBorrowedUpdates()
        => this.EmitsWholeUpdates("BorrowedScalar", "func set(x: uniq/i32) => Kimi.Intrinsics.replace(x, with: 2)\nfunc take(x: uniq/i32) -> i32 => Kimi.Intrinsics.exchange(x, with: 3)\nfunc flip(x: uniq/i32, y: uniq/i32) => Kimi.Intrinsics.swap(x, y)\nvar x: i32 = 1\nvar y: i32 = 9\nset(x@uniq)\nlet old = take(x@uniq)\nflip(x@uniq, y@uniq)\nif old == 2 and x == 9 and y == 3 => Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void LocalExclusiveBorrowUpdatesOriginalStorage()
        => this.EmitsWholeUpdates("LocalBorrow", "var x: i32 = 1\nlet u = x@uniq/i32\nKimi.Intrinsics.replace(u, with: 2)\nlet old = Kimi.Intrinsics.exchange(u, with: 3)\nif old == 2 and x == 3 => Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void OrdinaryOpenOwnerDoesNotRequireSealed()
        => this.EmitsWholeUpdates("OpenOwner", "open struct S\n    public var value: i32 = 1\nvar x = S.init()\nx.value = 2\nKimi.Intrinsics.replace(x@uniq, with: S.init())\nif x.value == 1 => Console.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData("i32")]
    [InlineData("string")]
    [InlineData("()")]
    [InlineData("[2 of i32]")]
    [InlineData("(i32, string)")]
    public void ObjectsMayHaveNonStructPayloads(string type)
    {
        var c = Parse($"func f(x: objref/{type}) -> ref/{type} during x => x@ref/{type}");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void SealedBorrowedPayloadHasCheckedRuntimeSupport()
    {
        var c = MinimalEmissionTest.Analyze("struct S\nfunc f(x: objref/S) -> ref/S during x => x@ref/S\n()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.True(c.Ownership.Result.IsVerified);
        Assert.True(c.Emission.Validate(out var error), error);
    }

    [Theory]
    [InlineData("var x: i32 = 1\nKimi.Intrinsics.swap(x@uniq, x@uniq)")]
    [InlineData("var x: i32\nKimi.Intrinsics.replace(x@uniq, with: 2)")]
    [InlineData("let x: i32 = 1\nKimi.Intrinsics.replace(x@uniq, with: 2)")]
    [InlineData("var x = \"old\"\nKimi.Intrinsics.exchange(x@uniq, with: x@move)")]
    public void RejectsConflictingOrIncompleteTargets(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("func f(x: uniq/S) => Kimi.Intrinsics.swap(x, x)")]
    public void BorrowedTargetsProtectLaterArguments(string function)
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    public var value: i32 = 0\n    public init(value: i32) => self.value = value\n" + function + "\n()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Sealed", true)]
    [InlineData("Sealed and Copy", true)]
    [InlineData("Sealed or Copy", false)]
    [InlineData("not Sealed", false)]
    public void ProjectionUsesDeclaredProofRules(string constraint, bool expected)
    {
        var c = Parse($"func f<T>(x: objref/T) -> ref/T during x\n    T is {constraint}\n    return x@ref/T");
        Assert.Equal(expected, c.Bind().IsComplete);
    }

    [Fact]
    public void CompletePayloadReceiverUsesOrdinaryCallPath()
    {
        var c = Parse("struct S\n    public func reset(self: uniq/Self) => Kimi.Intrinsics.replace(self, with: S.init())\nfunc f(x: objuniq/S) => x.reset()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single(x => x.Name == "f");
        var call = Assert.IsType<InvocationKoto>(f.ExpressionBody);
        Assert.Equal(ArgumentOperationKind.PayloadProjection, call.BoundCall!.ReceiverOperation.Kind);
    }

    [Fact]
    public void NamedArgumentsUseTextualEvaluationOrder()
        => this.EmitsWholeUpdates("NamedOrder", "var x: i32 = 1\nvar y: i32 = 9\nKimi.Intrinsics.replace(with: x + 1, target: x@uniq)\nKimi.Intrinsics.swap(second: y@uniq, first: x@uniq)\nif x == 9 and y == 2 => Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void SameSpellingFunctionKeepsOrdinaryBehavior()
        => this.EmitsWholeUpdates("UserFunction", "func replace(x: i32 ! with => y: i32) -> i32 => x + y\nvar x: i32 = 1\nlet result = replace(x, with: x + 1)\nif x == 1 and result == 3 => Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void AliasRetainsIntrinsicIdentity()
        => this.EmitsWholeUpdates("Alias", "alias Standard => Kimi\nvar x: i32 = 1\nStandard.Intrinsics.replace(x@uniq, with: 2)\nif x == 2 => Console.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData("Named", "alias Memory => Kimi.Intrinsics\n", "Memory.")]
    [InlineData("Opened", "alias Kimi.Intrinsics\n", "")]
    public void IntrinsicsGroupAliasesExecute(string name, string alias, string qualifier)
    {
        var source = alias + "var x: i32 = 1\nvar y: i32 = 9\n" +
            qualifier + "replace(x@uniq, with: 2)\nlet old = " + qualifier + "exchange(x@uniq, with: 3)\n" +
            qualifier + "swap(x@uniq, y@uniq)\nif old == 2 and x == 9 and y == 3 => Console.writeLine(\"ok\")";
        this.EmitsWholeUpdates("GroupAlias" + name, source, "ok\n");
    }

    [Fact]
    public void SameSpellingGroupKeepsOrdinaryBehavior()
        => this.EmitsWholeUpdates("UserGroup", "group Intrinsics\n    public func replace(x: i32 ! with => y: i32) -> i32 => x + y\nvar x: i32 = 1\nlet result = Intrinsics.replace(x, with: 2)\n::Kimi.Intrinsics.replace(x@uniq, with: 4)\nif result == 3 and x == 4 => Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void ReplacementDestructionAbortPreventsPlacement()
        => ScalarEmissionTest.EmitFixture("WholeValueDestructorAbort", "struct S\n    public init() => ()\n    deinit => $abort(\"drop\")\nvar x = S.init()\nKimi.Intrinsics.replace(x@uniq, with: S.init())\nConsole.writeLine(\"bad\")", string.Empty, 1, "Hello.kimi:3:15: abort KIMI_E_ABORT: drop\n");

    [Fact]
    public void RebindingAndSerializationRetainUpdates()
    {
        var c = Parse("var x: i32 = 1\nKimi.Intrinsics.replace(x@uniq, with: 2)\nlet old = Kimi.Intrinsics.exchange(x@uniq, with: 3)\n()");
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var tree = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(c);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
            Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }
    }

    [Theory]
    [InlineData("func f(x: objref/Cell<i32>) -> ref/Cell<i64> during x => x@ref/Cell<i64>")]
    [InlineData("func f(x: objref/Cell<i32>) -> ref/Cell<i32> during x => x@(ref/Cell<i32> during x)")]
    public void PayloadProjectionDoesNotConvertInternalTypesOrAcceptTargetOrigin(string function)
    {
        var c = Parse("struct Cell<T>\n    let item: T\n" + function);
        Assert.False(c.Bind().IsComplete && !c.Kotonoha.HasSourceErrors);
    }

    [Fact]
    public void RequiredUpdateDeclarationShapeIsValidatedAgain()
    {
        var c = Parse("var x: i32 = 1\nKimi.Intrinsics.replace(x@uniq, with: 2)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var function = (FunctionKoto)c.Library.Replace.Declaration;
        function.Parameters[1].Type = function.Parameters[0].Type;
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(KimiDeclarationId.Replace));
    }

    [Fact]
    public void ExampleMatchesExpectedOutput()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var source = File.ReadAllText(Path.Combine(root, "examples", "WholeValueReplacement", "WholeValueReplacement.kimi"));
        this.EmitsWholeUpdates("Example", source, "Destroyed 1.\nReplacement installed.\nExchange and swap complete.\nDestroyed 2.\n");
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        return c;
    }

    private static string Describe(Compilation c) => c.Binding.Result + "\n" + string.Join('\n', c.Binding.Issues);
}
