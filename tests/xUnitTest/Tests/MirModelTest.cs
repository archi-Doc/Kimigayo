// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The MIR model (docs/dev/COMPILER_ARCHITECTURE.md, R3): a body is struct arrays the builder appends to; projections and
/// constants are interned, a projection with the Type its base admits; statements of a block are one range; a pooled rebuild of the same
/// body allocates nothing. <see cref="SourceRef"/> names a node by its module ordinal and SyntaxId.</summary>
public sealed class MirModelTest
{
    internal const string Source = "open struct Base\n    public var tag: i32 = 0\n    public init() => ()\nstruct Point : Base\n    public var x: i32 = 0\n" +
        "    public var y: bool = false\n    public init() => ()\nenum Shape\n    Dot\n    Line(i32, i32)\nfunc f() -> i32 => 1\n" +
        "func g<s/T>(value: s/T) -> s/T\n    s is owner or uniq\n    T is Copy\n    return value\nfunc h(a: i32, flag: bool) -> bool => flag\n" +
        "func d(a: i32, b: bool = true) -> i32 => a\nstruct Meter\n    public var value: i32 = 0\n    public init() => ()\n" +
        "    public func scaled(by: i32, self: ref/Self) -> i32 => by\n";

    private static readonly SourceInfo At = default;

    // The source modules hold their positions as ordinals and the library the next one, so a SourceRef finds its node again.
    [Fact]
    public void SourceRefNamesANodeByModuleAndId()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var modules = c.SourceModules.Append(c.Library.Kotonoha).ToArray();
        Assert.Equal(Enumerable.Range(0, modules.Length).Select(static x => (ushort)x), modules.Select(static x => x.Syntax.Module));
        foreach (var node in KotoTree.Walk(c.Kotonoha.RootKoto).Concat(KotoTree.Walk(c.Library.Kotonoha.RootKoto).Take(500)))
        {
            Assert.Same(node, c.SyntaxOf(SourceRef.Of(node)));
        }
    }

    // A projection is interned with the Type it selects, shared by every root of that Type and by every chain it prefixes; a projection
    // the base Type does not admit is interned with no Type.
    [Fact]
    public void ProjectionsAreInternedWithTheTypeTheyAdmit()
    {
        var (c, types) = Fixture();
        var body = NewBody(types);
        var tuple = c.Types.Tuple([BoundType.I32, BoundType.Boolean]);
        var (a, b) = (Local(body, tuple), Local(body, tuple));
        var second = body.Project(a, MirProjectionKind.Field, 1);
        Assert.Equal(second, body.Project(a, MirProjectionKind.Field, 1));
        Assert.Equal(second.Projection, body.Project(b, MirProjectionKind.Field, 1).Projection);
        Assert.Same(BoundType.Boolean, body.TypeOf(second));

        var deref = body.Project(Local(body, c.Types.Reference(SemanticsKind.Ref, tuple)), MirProjectionKind.Deref, 0);
        var first = body.Project(deref, MirProjectionKind.Field, 0);
        Assert.Equal(deref.Projection, body.Projections[first.Projection].Prefix);
        Assert.Same(BoundType.I32, body.TypeOf(first));
        Assert.Same(BoundType.I32, body.TypeOf(body.Project(Local(body, c.Types.Pointer(BoundType.I32)), MirProjectionKind.Deref, 0)));

        var point = Local(body, types.Point);
        Assert.Same(BoundType.Boolean, body.TypeOf(body.Project(point, MirProjectionKind.Field, 1)));
        Assert.Same(types.Base, body.TypeOf(body.Project(point, MirProjectionKind.Base, 1)));
        Assert.Same(BoundType.I32, body.TypeOf(body.Project(body.Project(point, MirProjectionKind.Base, 1), MirProjectionKind.Field, 0)));
        Assert.Null(body.TypeOf(body.Project(point, MirProjectionKind.Field, 2)));
        Assert.Null(body.TypeOf(body.Project(point, MirProjectionKind.Base, 2)));

        var line = body.Project(Local(body, types.Shape), MirProjectionKind.Downcast, 1);
        Assert.Equal(2, body.TypeOf(line)!.Components.Count);
        Assert.Same(BoundType.I32, body.TypeOf(body.Project(line, MirProjectionKind.Field, 1)));
        Assert.Null(body.TypeOf(body.Project(Local(body, types.Shape), MirProjectionKind.Downcast, 2)));

        var array = Local(body, types.Array);
        Assert.Same(BoundType.I32, body.TypeOf(body.Project(array, MirProjectionKind.ConstIndex, 2)));
        Assert.Null(body.TypeOf(body.Project(array, MirProjectionKind.ConstIndex, 3)));
        Assert.Same(BoundType.I32, body.TypeOf(body.Project(array, MirProjectionKind.Index, Local(body, BoundType.ISize).Root)));
        Assert.Null(body.TypeOf(body.Project(array, MirProjectionKind.Index, Local(body, BoundType.I32).Root)));

        var count = body.Projections.Length;
        var invalid = body.Project(Local(body, BoundType.I32), MirProjectionKind.Field, 0);
        Assert.Null(body.TypeOf(invalid));
        Assert.Equal(invalid.Projection, body.Project(Local(body, BoundType.I32), MirProjectionKind.Field, 0).Projection);
        Assert.Equal(count + 1, body.Projections.Length);
        Assert.Null(body.TypeOf(body.Project(Local(body, BoundType.I32), MirProjectionKind.PairLayer, 0)));
        var pair = Local(body, types.Pair);
        Assert.Null(body.TypeOf(body.Project(pair, MirProjectionKind.PairLayer, 0)));
        PairBinder.Collect(types.Generic, body.Binders);
        _ = TyCtxt.Layer(types.Pair, out var target, out _);
        Assert.Same(target, body.TypeOf(body.Project(pair, MirProjectionKind.PairLayer, 0)));
        Assert.Null(body.TypeOf(body.Project(pair, MirProjectionKind.PairLayer, 1)));
    }

    // Equal constants share an id; the Type, the kind and the value tell constants apart, a string by its text.
    [Fact]
    public void ConstantsAreInterned()
    {
        var (_, types) = Fixture();
        var body = NewBody(types);
        var five = body.Const(new(MirConstKind.Integer, BoundType.I32, 5));
        Assert.Equal(five, body.Const(new(MirConstKind.Integer, BoundType.I32, 5)));
        Assert.NotEqual(five, body.Const(new(MirConstKind.Integer, BoundType.ISize, 5)));
        Assert.NotEqual(five, body.Const(new(MirConstKind.Integer, BoundType.I32, 6)));
        var text = body.Const(new(MirConstKind.String, BoundType.String, Value: "text"));
        Assert.Equal(text, body.Const(new(MirConstKind.String, BoundType.String, Value: new string("text".AsSpan()))));
        Assert.NotEqual(body.Const(new(MirConstKind.Bool, BoundType.Boolean, 1)), body.Const(new(MirConstKind.Bool, BoundType.Boolean, 0)));
        Assert.Equal(6, body.Consts.Length);
        Assert.Equal((Int128)5, body.Consts[five].Bits);
    }

    // A block's statements are one range of the body's statements, appended together; a new block's terminator is a Goto without a
    // target until the block is terminated.
    [Fact]
    public void BlocksHoldOneRangeOfStatements()
    {
        var (_, types) = Fixture();
        var body = NewBody(types);
        var local = Local(body, BoundType.I32);
        var (entry, next) = (body.AddBlock(), body.AddBlock());
        Assert.Equal(new MirTerminator(MirTerminatorKind.Goto, default), body.Blocks[entry].Terminator);
        body.Push(entry, MirStatement.Of(MirStatementKind.StorageLive, local, At));
        body.Push(entry, MirStatement.Assign(local, MirRvalue.Use(body.AddOperand(MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, 1))))), At));
        body.Push(next, MirStatement.Of(MirStatementKind.StorageDead, local, At));
        Assert.Throws<InvalidOperationException>(() => body.Push(entry, MirStatement.Of(MirStatementKind.FakeRead, local, At)));
        body.Terminate(entry, MirTerminator.Goto(next, At));
        body.Terminate(next, MirTerminator.Return(At));
        Assert.Equal((0, 2, MirTerminatorKind.Goto, 1), (body.Blocks[entry].Start, body.Blocks[entry].Count, body.Blocks[entry].Terminator.Kind, body.Blocks[entry].Terminator.Target));
        Assert.Equal((2, 1), (body.Blocks[next].Start, body.Blocks[next].Count));
        Assert.Equal(MirStatementKind.StorageDead, body.Statements[body.Blocks[next].Start].Kind);
    }

    // A static is one root however often the body names it; an Opaque site keeps the roots its syntax touched.
    [Fact]
    public void StaticsAndOpaqueSitesKeepTheirRoots()
    {
        var (_, types) = Fixture();
        var body = NewBody(types);
        var root = body.Static(new(types.Function, BoundType.I32, true, false));
        Assert.Equal(root, body.Static(new(types.Function, BoundType.Boolean, false, true)));
        Assert.True(root.IsStatic);
        Assert.Same(BoundType.I32, body.TypeOf(root));
        Assert.Equal(-1, root.Root);
        Assert.False(body.HasOpaque);
        var local = Local(body, BoundType.I32);
        Assert.Equal(0, body.AddOpaque(new(0, 7), [local.Root, root.Root]));
        Assert.True(body.HasOpaque);
        Assert.Equal(new[] { local.Root, root.Root }, body.OpaqueRoots.Slice(body.OpaqueSites[0].Start, body.OpaqueSites[0].Count).ToArray());
        Assert.Equal(new MirScope(-1, MirScopeKind.Lexical, default), Assert.Single(body.Scopes.ToArray()));
    }

    // A scope lies in the innermost scope of a kind that encloses it, itself included: a defer body's nested scopes and an inline copy in
    // it lie in its Deferred scope, a sibling in none; Inlining is the Inlined kind's.
    [Fact]
    public void ScopesLieInTheInnermostEnclosingScopeOfAKind()
    {
        var (_, types) = Fixture();
        var body = NewBody(types);
        var deferred = body.AddScope(0, MirScopeKind.Deferred, default);
        var nested = body.AddScope(deferred, MirScopeKind.Lexical, default);
        var copy = body.AddScope(nested, MirScopeKind.Inlined, default);
        var inner = body.AddScope(copy, MirScopeKind.Deferred, default);
        var sibling = body.AddScope(0, MirScopeKind.Lexical, default);
        Assert.Equal(deferred, body.Enclosing(nested, MirScopeKind.Deferred));
        Assert.Equal(deferred, body.Enclosing(copy, MirScopeKind.Deferred));
        Assert.Equal(inner, body.Enclosing(inner, MirScopeKind.Deferred));
        Assert.Equal(-1, body.Enclosing(sibling, MirScopeKind.Deferred));
        Assert.Equal(-1, body.Enclosing(0, MirScopeKind.Deferred));
        Assert.Equal(copy, body.Inlining(inner));
        Assert.Equal(-1, body.Inlining(nested));
    }

    // Clearing a program returns its bodies to the pool, and the next build reuses them in order.
    [Fact]
    public void ProgramReusesItsBodiesInOrder()
    {
        var (_, types) = Fixture();
        var program = new MirProgram();
        var (fn, closure) = (new MirDefKey(types.Function, MirBodyKind.Fn), new MirDefKey(types.Function, MirBodyKind.Closure, 1));
        var first = program.Begin(fn, new(0, 1));
        var second = program.Begin(closure, default);
        Assert.Same(second, program[closure]);
        Assert.Null(program[closure with { Index = 2 }]);
        Assert.Throws<ArgumentException>(() => program.Begin(fn, default));
        program.Clear();
        Assert.Null(program[fn]);
        Assert.Same(first, program.Begin(closure, default));
        Assert.Equal(new[] { first }, program.Bodies.ToArray());
        Assert.Equal(closure, first.Key);
    }

    // V11: rebuilding a pooled body of the same shape allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void PooledRebuildAllocatesNothing()
    {
        var (c, types) = Fixture();
        var program = new MirProgram();
        var tuple = c.Types.Tuple([BoundType.I32, BoundType.Boolean]);
        var key = new MirDefKey(types.Function, MirBodyKind.Fn);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            program.Clear();
            var body = program.Begin(key, default);
            body.AddLocal(new(BoundType.I32, MirLocalKind.Return, At));
            var pair = new MirPlace(body.AddLocal(new(tuple, MirLocalKind.Arg, At)));
            for (var i = 0; i < 64; i++)
            {
                var block = body.AddBlock();
                var field = body.Project(pair, MirProjectionKind.Field, i & 1);
                var start = body.AddOperand(MirOperand.Copy(field));
                body.AddOperand(MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, i))));
                body.Push(block, MirStatement.Assign(new(0), MirRvalue.Binary(MirBinaryOp.Add, start, false), At));
                body.Push(block, MirStatement.Of(MirStatementKind.FakeRead, field, At));
                body.AddCase(i, block);
                body.Terminate(block, MirTerminator.Goto(block + 1, At));
            }

            body.AddOpaque(default, [1]);
            body.Static(new(types.Function, BoundType.I32, false, false));
            body.AddScope(0, MirScopeKind.Unsafe, default);
        }));
    }

    internal static (Compilation Compilation, Types Types) Fixture()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var containers = c.Kotonoha.RootKoto.NestedContainers;
        BoundType Self(string name) => c.Semantics.SelfType(containers.Single(x => x.Name == name).SymbolOf()!);
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").SymbolOf()!;
        var array = c.Types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [BoundType.I32], 3);
        var generic = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g");
        return (c, new(Self("Base"), Self("Point"), Self("Shape"), array, function, generic, generic.Parameters[0].Type.TypeOf()!));
    }

    internal static MirBody NewBody(Types types)
    {
        var body = new MirBody();
        body.Reset(new(types.Function, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.I32, MirLocalKind.Return, At));
        return body;
    }

    internal static MirPlace Local(MirBody body, BoundType type, MirLocalKind kind = MirLocalKind.Var) => new(body.AddLocal(new(type, kind, At)));

    // Pair is the pair layer of g's parameter, whose binder PairBinder.Collect lists for Generic.
    internal sealed record Types(BoundType Base, BoundType Point, BoundType Shape, BoundType Array, BindingSymbol Function, FunctionKoto Generic, BoundType Pair);
}
