// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The published HIR columns (docs/dev/COMPILER_ARCHITECTURE.md, R2b) over the Kimi library and every milestone program:
/// every node of the tree has an Op, every call Op its call, every selected symbol a Res, a parameter read from a later default the
/// prepared slot, every jump its target, every evaluated node its completion and its Place, every consumed child its Adjust, every
/// match its Pattern plan, every closure its definition, every expansion its plan, every omitted default its declaration, every body
/// its pair binders and every call target its disposition; publishing changes no other result and allocates nothing again.</summary>
public sealed class HirColumnsTest
{
    // Virtual bodies over a generic base whose applicability has premises, and over a closed base.
    internal const string VirtualSource =
        "contract Marker\nstruct NonCopy\n    public init() => ()\n    drop => ()\nopen struct Base<T>\n    T is Owned\n    public init() => ()\n" +
        "    public virtual func first(self: objref/Self) -> i32 => 1\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return value@follow\n" +
        "    public virtual func last(self: objref/Self) -> i32 => 3\nstruct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n" +
        "    override func copy(self: objref/Self, value: ref/T) -> T => value@follow\n    override func last(self: objref/Self) -> i32 => 30\n" +
        "open struct Plain\n    public init() => ()\n    public virtual func size(self: objref/Self) -> i32 => 1\nstruct Sub : Plain\n    public init() => ()\n" +
        "    override func size(self: objref/Self) -> i32 => 2\nlet a = Leaf<i32>.init()@obj\nlet b = Leaf<NonCopy>.init()@obj\nlet p = Sub.init()@obj\n" +
        "require a.copy(7) == 7 and a.last() == 30 and b.last() == 30 and b.first() == 1 and p.size() == 2 else => $abort(\"virtual\")\n";

    public static TheoryData<string> Corpus
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in SemanticSnapshotTest.CaseNames())
            {
                if (name == "library" || name.StartsWith("milestone/", StringComparison.Ordinal))
                {
                    data.Add(name);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryNodeIsPublished(string name)
    {
        var (c, module) = SemanticSnapshotTest.Bind(name);
        HirTables.Publish(c);
        var problems = Problems(module);
        Assert.True(problems.Count == 0, $"{problems.Count} unpublished facts:\n{string.Join('\n', problems.Take(40))}");
    }

    // Publishing before the ownership analysis leaves every Binding, control-flow and ownership fact and diagnostic unchanged.
    [Theory]
    [MemberData(nameof(Corpus))]
    public void PublishIsInvisible(string name)
        => Assert.Equal(SemanticSnapshotTest.Take(name, false).Text, SemanticSnapshotTest.Take(name, true).Text);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmPublishAllocatesNothing(bool virtualBodies)
    {
        var c = virtualBodies ? MinimalEmissionTest.Analyze(VirtualSource) : SemanticSnapshotTest.Bind("milestone/Milestone34").Compilation;
        HirTables.Publish(c);
        var nodes = Reachable(c.Kotonoha).Concat(Reachable(c.Library.Kotonoha)).ToArray();
        var (cold, warm) = (ReadViews(nodes), 0);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            HirTables.Publish(c);
            warm = ReadViews(nodes);
        }));
        Assert.True(cold > 0 && cold == warm, $"{cold} facts cold, {warm} warm");
    }

    // The publish found no node without the facts of its Op (HirTables.Broken: a call Op its call, a match, a try and a user loop their
    // Pattern plan, an expansion the calls it names), and every parsed node in the tree has an Op, a selected symbol its Res (the prepared
    // slot exactly where the parameter is read in a later default of its function), a jump the target it transfers to, and an evaluated
    // node its completion; every child an Op consumes has its Adjust: a call's argument and receiver the adaptation of the operation of a
    // call that takes them, unless Binding recorded one at the node itself, and a Pattern binding its acquisition; a closure has its
    // environment, signature and substitution, an expansion Op its plan, a requirement call its Contract unless its target is a built-in
    // witness (a Shim), and an Abort or Fail call diverges.
    internal static List<string> Problems(Kotonoha module)
    {
        var table = module.Syntax;
        var binding = module.Compilation.Binding;
        var problems = new List<string>();
        var operations = new Dictionary<Koto, HashSet<HirAdaptation>>(ReferenceEqualityComparer.Instance);
        void Expect(Koto source, HirAdaptation adaptation)
        {
            if (!binding.TryGetAdaptation(source, out _))
            {
                (operations.TryGetValue(source, out var set) ? set : operations[source] = []).Add(adaptation);
            }
        }

        bool Parsed(Koto? node) => node is not null && ReferenceEquals(node.SyntaxOwner, table) && node.SyntaxId <= table.ParsedCount;
        foreach (var node in table.Hir.Broken ?? [])
        {
            problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} {node.Akind} without the facts of its Op (a broken contract)");
        }

        foreach (var node in Reachable(module))
        {
            // A synthesized node in the tree belongs to the expansion that made it.
            if (!Parsed(node))
            {
                continue;
            }

            var op = HirTables.Op(node);
            var where = $"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} {node.Akind} {node.StateOf()} {op}";
            if (op == HirOp.Unset)
            {
                problems.Add(where);
            }

            if (node.SymbolOf() is { } symbol &&
                (HirTables.ResOf(node) == HirRes.None || (HirTables.ResOf(node) == HirRes.PreparedParam) != DefaultParameters.InLaterDefault(node, symbol)))
            {
                problems.Add($"{where} {symbol.Kind} {HirTables.ResOf(node)}");
            }

            if (op is HirOp.Jump or HirOp.Continue && !HoldsItsTarget((JumpKoto)node))
            {
                problems.Add($"{where} without its target");
            }

            // An evaluated node has its completion, a Never-typed one diverging; a node the builder does not evaluate has none.
            var evaluated = op is not (HirOp.Unset or HirOp.Syntax or HirOp.Body);
            var completion = HirTables.CompletionOf(node);
            if (evaluated == (completion == HirCompletion.Unset) || (evaluated && ReferenceEquals(node.TypeOf(), BoundType.Never) && completion != HirCompletion.Diverges))
            {
                problems.Add($"{where} completion {completion}");
            }

            // An evaluated number, a directly signed one included, is one Literal with the value Binding fitted (SPEC 13.5.4), unless it is a
            // fraction that a folded conversion takes.
            if (evaluated && op is not (HirOp.Error or HirOp.Call) && KotoHelper.SignedNumber(node, out _) is { } number &&
                (op != HirOp.Literal || (HirTables.PlanOf(node).Folded is null && (number.IsInteger || node.TypeOf() is { IsFloatingPoint: true }))))
            {
                problems.Add($"{where} without its literal value");
            }

            // An evaluated node and a node used in place have their Place, a stored Field, an explicit follow and a read name a Place, and
            // the layers a consumer follows are layers of the Type, every one of them under a stored Field, an element or a sequence's metadata.
            var place = HirTables.PlaceOf(node);
            var projection = op is HirOp.Field or HirOp.Index || (op == HirOp.PrimitiveOp && node is MemberAccessKoto);
            if ((evaluated || HirTables.AdjustOf(node).Acquire == HirAcquire.None) == (place.Kind == HirPlaceKind.Unset) || !Peel(node.TypeOf(), place.Layers, out _) ||
                ((op is HirOp.Field or HirOp.Deref || (op == HirOp.Name && HirTables.ResOf(node) != HirRes.Decl)) && place.Kind == HirPlaceKind.Value) ||
                (projection && node is BinaryKoto { Left: var left } && (!Peel(left.TypeOf(), HirTables.PlaceOf(left).Layers, out var projected) || IsLayer(projected))))
            {
                problems.Add($"{where} place {place} of {node.TypeOf()}");
            }

            // The binder of every pair layer a consumer or an explicit follow follows is a binder in scope of the body.
            var layer = node.TypeOf();
            for (var i = 0; i < place.Layers; i++)
            {
                if (TyCtxt.Layer(layer, out layer, out var binder) == HirPlaceKind.PairLayer && !InScope(node, binder))
                {
                    problems.Add($"{where} follows a pair layer of {binder?.Name} out of scope");
                }
            }

            if (op == HirOp.Deref && place.Kind == HirPlaceKind.PairLayer && (TyCtxt.Layer(((BinaryKoto)node).Left.TypeOf(), out _, out var followed) != HirPlaceKind.PairLayer || !InScope(node, followed)))
            {
                problems.Add($"{where} follows a pair layer of {followed?.Name} out of scope");
            }

            if (op is HirOp.Unset or HirOp.Error || (op == HirOp.Syntax && node is not FunctionKoto))
            {
                continue;
            }

            if (op is HirOp.Body or HirOp.Closure && !Binders(node).SequenceEqual(Binders(node, binding)))
            {
                problems.Add($"{where} pair binders");
            }

            if (!ViewsAgree(node, op))
            {
                problems.Add($"{where} views {IntrinsicDisposition.FormOf(node)} {BoundEnumConstruction.Of(node)?.Type}");
            }

            if (op == HirOp.Closure && (((FunctionKoto)node).ClosureDefinition is not { EnvironmentType: { } environment, Signature: not null } || module.Compilation.Types.SubstOfClosure(environment) is null))
            {
                problems.Add($"{where} without its closure definition");
            }

            // An index names its key resolution, which reads the evaluated receiver, and a user or view selection its selected call; an update
            // of a Property with an accessor its setter, which takes the computed value.
            var expansion = ExpansionPlan.Of(node);
            var written = (node as BinaryKoto)?.Left ?? (node as UnaryKoto)?.Operand;
            var updated = written is null ? null : KotoHelper.UnwrapParentheses(written); // The Property Binding designates.
            if ((expansion is null) == (op is HirOp.For or HirOp.Try or HirOp.Interpolation or HirOp.DictionaryLiteral or HirOp.Range or HirOp.CompoundAssign or HirOp.Index || (op == HirOp.Call && node is IndexKoto)) ||
                (op == HirOp.Range && CallPlan.Of(node) is not null) ||
                (node is IndexKoto && ((op == HirOp.Call) == (expansion?.Second is null) || (expansion?.First is not null) != (op == HirOp.Index && CallPlan.Of(node) is not null))) ||
                (op == HirOp.CompoundAssign && HirTables.Op(updated!) == HirOp.Call && updated is not IndexKoto && expansion?.Second is null))
            {
                problems.Add($"{where} expansion {expansion}");
            }

            foreach (var child in Consumed(node, op))
            {
                if (Parsed(child) && HirTables.AdjustOf(child!).Acquire == HirAcquire.Unset)
                {
                    problems.Add($"{where} consumes {child!.Akind}@{child.Span.Start} without its Adjust");
                }
            }

            for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
            {
                var disposition = call.Kind == CalleeKind.Value ? default : IntrinsicDisposition.Of(call.Target.CompilerFunction);
                if ((call.Kind == CalleeKind.Requirement && call.RequirementContract is null && disposition.Kind != DispositionKind.Shim) ||
                    (disposition.Id is IntrinsicId.Abort or IntrinsicId.Fail && !ReferenceEquals(call.ReturnType, BoundType.Never)) ||
                    (call.Kind != CalleeKind.Value && call.Target.Declaration is FunctionKoto { Accessor.Declaration: { Body: not null } accessor } && !ReferenceEquals(call.Target, CallPlan.Accessor(accessor))))
                {
                    problems.Add($"{where} calls {call.Target.Name} without its Contract, as {disposition} or not as its accessor");
                }

                foreach (ref readonly var omitted in call.DefaultArguments)
                {
                    var declaration = DefaultDecl.Of(omitted);
                    if (!ReferenceEquals(declaration.Function, call.Target.Declaration) || !ReferenceEquals(declaration.Expression, omitted.Expression) || HirTables.Op(omitted.Expression) is HirOp.Unset or HirOp.Error or HirOp.Syntax ||
                        !ReferenceEquals(module.Compilation.Types.Apply(declaration.Parameter(declaration.Index), module.Compilation.Types.SubstOf(call)!), omitted.ParameterType))
                    {
                        problems.Add($"{where} omits {omitted.Parameter.Name} without its default declaration");
                    }
                }

                if (call.Kind != CalleeKind.Value && call.Receiver is { } receiver)
                {
                    Expect(receiver, HirAdjust.Of(call.ReceiverOperation));
                }

                foreach (var argument in call.ArgumentOperations)
                {
                    if (argument.Source is { } source)
                    {
                        Expect(source, HirAdjust.Of(argument));
                    }
                }
            }

            if (node is MatchKoto match && binding.TryGetMatch(match, out var plan))
            {
                foreach (var position in plan!.Positions)
                {
                    if (position.Kind == BoundPatternKind.Binding && Parsed(position.Source) && HirTables.AdjustOf(position.Source) != HirAdjust.Of(position.Acquisition, position.AccessMode))
                    {
                        problems.Add($"{where} binds {position.Source.Span.Start} {position.Acquisition} as {HirTables.AdjustOf(position.Source)}");
                    }
                }
            }
        }

        foreach (var (source, adaptations) in operations)
        {
            if (Parsed(source) && !adaptations.Contains(HirTables.AdjustOf(source).Adaptation))
            {
                problems.Add($"{source.CodeContext.SourceDocument?.Path}:{source.Span.Start} {source.Akind} takes {HirTables.AdjustOf(source)} for {string.Join(',', adaptations)}");
            }
        }

        return problems;
    }

    // The HIR contract of a published Jump or Continue (ARCH "Binding facts for MIR"): its row holds the target it may go to, and a null
    // row is a transfer at which Binding publishes InvalidJumpTarget_Kd or UnlabeledYieldTarget_Kd.
    internal static bool HoldsItsTarget(JumpKoto jump)
    {
        var target = KotoHelper.ResolveTransferTarget(jump);
        if (HirTables.PlanOf(jump).Plan is Koto row)
        {
            return ReferenceEquals(row, target) && KotoHelper.IsValidTransfer(jump, target);
        }

        return CompilationTestHelper.BindingErrors(jump.CodeContext.Compilation)
            .Any(x => x.Span == jump.Span && x.Code is nameof(Kimi.DiagnosticCode.InvalidJumpTarget_Kd) or nameof(Kimi.DiagnosticCode.UnlabeledYieldTarget_Kd));
    }

    internal static bool IsWithin(Koto node, Koto ancestor)
    {
        for (Koto? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    internal static List<Koto> Reachable(Kotonoha module)
    {
        var nodes = new List<Koto>();
        var collector = new Collector(nodes);
        collector.Visit(module.RootKoto);
        return nodes;
    }

    // Reads R3 U1's lookup views at every node, as the MIR builder does: each layer's binder, an enum construction, a built-in form, an
    // accessor's executing function and every parameter of a body; the count of the facts found.
    private static int ReadViews(Koto[] nodes)
    {
        var found = 0;
        foreach (var node in nodes)
        {
            var type = HirTables.Meaning(node) as BoundType;
            for (var i = 0; i < HirTables.PlaceOf(node).Layers; i++)
            {
                found += TyCtxt.Layer(type, out type, out var binder) == HirPlaceKind.PairLayer && binder is not null ? 1 : 0;
            }

            found += (BoundEnumConstruction.Of(node) is null ? 0 : 1) + (int)IntrinsicDisposition.FormOf(node);
            var function = node is PropertyAccessorKoto accessor && HirTables.Op(node) == HirOp.Body ? CallPlan.Accessor(accessor)
                : node is FunctionKoto && HirTables.Op(node) is HirOp.Body or HirOp.Closure ? HirTables.Symbol(node) : null;
            for (var i = -1; function is not null && i < ((FunctionKoto)function.Declaration).Parameters.Count; i++)
            {
                found += CallPlan.Parameter(function, i) is null ? 0 : 1;
            }
        }

        return found;
    }

    // The children an Op consumes (SPEC 3.5, 10.2, 15.1.6): its own operands, conditions, Subject, initializer, result or jump operand,
    // then each call's receiver, value callee and arguments, and an enum Case's payloads.
    private static IEnumerable<Koto?> Consumed(Koto node, HirOp op)
    {
        IEnumerable<Koto?> own = (node, op) switch
        {
            (FunctionKoto function, _) => function.Parameters.Select(static x => x.DefaultValue).Append(op is HirOp.Body or HirOp.Closure ? Result(function.ExpressionBody) : null),
            (PropertyAccessorKoto accessor, HirOp.Body) => [Result(accessor.Body)],
            (PropertyKoto property, HirOp.Body) => [property.InitializerKoto],
            (VariableKoto variable, HirOp.Let) => [variable.InitializerKoto],
            (CodeBlockKoto block, HirOp.Block) => [Result(block.TrailingExpression)],
            (DoKoto scoped, HirOp.Do) => [Result(scoped.Body)],
            (LabeledKoto label, HirOp.Group) => [label.Target],
            (MemberAccessKoto member, HirOp.Field or HirOp.PrimitiveOp) => [member.Left],
            (BinaryKoto binary, HirOp.Group or HirOp.Borrow or HirOp.Deref or HirOp.Convert or HirOp.RuntimeTypeTest) => [binary.Left],
            (BinaryKoto binary, HirOp.Index or HirOp.Assign or HirOp.CompoundAssign or HirOp.PrimitiveOp or HirOp.Logical) => [binary.Left, binary.Right],
            (UnaryKoto unary, HirOp.Group or HirOp.Deref or HirOp.PrimitiveOp or HirOp.Range or HirOp.CompoundAssign) => [unary.Operand],
            (RangeKoto range, HirOp.Range) => [range.Start, range.End],
            (TupleLiteralKoto tuple, _) => tuple.Elements,
            (ArrayLiteralKoto array, _) => array.Elements,
            (DictionaryLiteralKoto dictionary, _) => dictionary.Entries.SelectMany(static x => new[] { x.Key, x.Value }),
            (JumpKoto jump, HirOp.Jump) => [jump.Expression],
            (IfKoto conditional, _) => conditional.Branches.SelectMany(static x => new[] { x.Condition, Result(x.Body) }).Append(Result(conditional.ElseBody)),
            (WhileKoto loop, _) => [loop.Condition],
            (RequireKoto require, _) => [require.Condition],
            (TestVerificationKoto verification, _) => [verification.Condition, verification.Message],
            (DiscardKoto discard, _) => [discard.Operand],
            (MatchKoto match, _) => match.Arms.SelectMany(static x => new[] { x.Guard, Result(x.Body) }).Prepend(match.Expression),
            (ForKoto loop, HirOp.For) => loop.Bindings.Prepend(loop.Iterable),
            (MacroKoto macro, HirOp.Interpolation) => [((InvocationKoto)macro.Operand).ArgumentNodes[0]],
            _ => [],
        };

        var calls = Enumerable.Range(0, int.MaxValue).Select(x => CallPlan.Of(node, x)).TakeWhile(static x => x is not null)
            .SelectMany(static x => x!.ArgumentOperations.ToArray().Select(static y => y.Source).Prepend(x.Kind == CalleeKind.Value ? x.CalleeValue : x.Receiver));
        var payloads = op == HirOp.Aggregate && node.CodeContext.Compilation.Binding.TryGetEnumConstruction(node, out var construction)
            ? construction!.PayloadOperations.ToArray().Select(static x => x.Source) : [];
        return own.Concat(calls).Concat(payloads);

        // A body or operand that supplies its construct's value.
        static Koto? Result(Koto? source) => source is not null && KotoHelper.IsValueContext(source) ? source : null;
    }

    // R3 U1's views at an evaluated node or a body: an enum Case value has its construction; a sequence's metadata its member, of the Type
    // that member has, and no other node a built-in form; an accessor's executing
    // function its result and its receiver and input; a parameter read the function's own parameter; an implicit constructor its receiver,
    // which every Field's initializer initializes, and its base call exactly where the structure has a base (SPEC 6.3.6).
    private static bool ViewsAgree(Koto node, HirOp op)
    {
        var construction = BoundEnumConstruction.Of(node);
        var type = node.TypeOf();
        var form = IntrinsicDisposition.FormOf(node);
        var agree = (construction is not null) == (op == HirOp.Aggregate && node is not (TupleLiteralKoto or ArrayLiteralKoto)) &&
            (construction is null || ReferenceEquals(construction.Type, type)) &&
            (op == HirOp.PrimitiveOp && node is MemberAccessKoto) == form is BuiltinForm.Length or BuiltinForm.IsEmpty or BuiltinForm.Indices or BuiltinForm.Capacity &&
            form switch
            {
                BuiltinForm.Indices => type?.Symbol?.LibraryDeclaration == KimiDeclarationId.ResolvedRange,
                BuiltinForm.IsEmpty => ReferenceEquals(type, BoundType.Boolean),
                BuiltinForm.Length or BuiltinForm.Capacity => ReferenceEquals(type, BoundType.ISize),
                _ => true,
            };

        if (op == HirOp.Name && HirTables.ResOf(node) is HirRes.Param or HirRes.PreparedParam)
        {
            Koto? callable = node.Parent;
            while (callable is not (null or FunctionKoto or PropertyAccessorKoto))
            {
                callable = callable.Parent;
            }

            var function = callable is PropertyAccessorKoto accessor ? CallPlan.Accessor(accessor) : callable?.SymbolOf();
            agree &= function is not null && Enumerable.Range(-1, ((FunctionKoto)function.Declaration).Parameters.Count + 1).Any(x => ReferenceEquals(CallPlan.Parameter(function, x), node.SymbolOf()));
        }

        if (node is PropertyAccessorKoto { } syntax && op == HirOp.Body)
        {
            var executing = CallPlan.Accessor(syntax);
            var bound = Binding.Accessor(syntax);
            var parameters = new[] { bound.Receiver, bound.Input }.Where(static x => x is not null).ToArray();
            agree &= ReferenceEquals(executing.Type, bound.Result) && ((FunctionKoto)executing.Declaration).Parameters.Count == parameters.Length &&
                parameters.Select((x, i) => ReferenceEquals(CallPlan.Parameter(executing, i)?.Type, x)).All(static x => x);
        }

        if (node is FunctionKoto { IsConstructor: true } constructor && op == HirOp.Body)
        {
            var self = AdtDef.ReceiverType(constructor);
            agree &= self is not null && ReferenceEquals(CallPlan.Parameter(constructor.SymbolOf()!, -1)?.Type, self);
            agree &= !constructor.IsImplicitConstructor || (constructor.Parameters.Count == 0 && (AdtDef.Base(self!) is null) == (constructor.BaseInitializer is null) &&
                (constructor.BaseInitializer is null || CallPlan.Of(constructor.BaseInitializer) is not null) &&
                Enumerable.Range(0, AdtDef.Count(self!)).All(x => AdtDef.Field(self!, x).InitializerKoto is { } initializer && HirTables.Op(initializer) is not (HirOp.Unset or HirOp.Syntax or HirOp.Error)));
        }

        return agree;
    }

    // Whether a pair binder is in scope of the body whose evaluation a node is part of.
    private static bool InScope(Koto node, BindingSymbol? binder)
    {
        Koto? body = node;
        while (body is not null && HirTables.Op(body) is not (HirOp.Body or HirOp.Closure))
        {
            body = body.Parent;
        }

        return body is not null && Binders(body).Any(x => ReferenceEquals(x.Target, binder));
    }

    // The pair binders in scope of a body, as published or as Binding collects them for its own analyses.
    private static List<PairBinder> Binders(Koto body, Binding? binding = null)
    {
        var binders = new List<PairBinder>();
        if (binding is null)
        {
            PairBinder.Collect(body, binders);
        }
        else
        {
            binding.PairBinders(body, binders);
        }

        return binders;
    }

    // SPEC 3.4.3, 13.5.5: a reference, object handle or pair layer, which selection follows implicitly.
    private static bool IsLayer(BoundType? type) => TyCtxt.Layer(type, out _, out _) != HirPlaceKind.Value;

    private static bool Peel(BoundType? type, int layers, out BoundType? result)
    {
        for (; layers > 0 && TyCtxt.Layer(type, out var below, out _) != HirPlaceKind.Value; layers--)
        {
            type = below;
        }

        result = type;
        return layers == 0;
    }

    private sealed class Collector(List<Koto> nodes) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            nodes.Add(node);
            base.Visit(node);
        }
    }
}
