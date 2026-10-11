// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

/// <summary>The MIR builder (plan P6, R3 shadow): once per Binding pass it publishes the HIR, builds one body for each definition the
/// HIR names (<see cref="HirOp.Body"/>: a function, an accessor, a static's initializer; a <see cref="HirOp.Closure"/>) and validates it.
/// It reads syntax children, the HIR columns and plans and <see cref="TyCtxt"/> only. A form no rule builds goes through <see cref="Unsupported(Koto, MirPlace)"/>, and building
/// continues; an Error node of Binding becomes a derived Opaque. The program, its bodies and the builder's scratch are reused, so a warm
/// rebuild allocates nothing.</summary>
/// <param name="compilation">The compilation.</param>
internal sealed partial class MirBuilder(Compilation compilation)
{
    private readonly MirProgram program = new();
    private readonly MirValidator validator = new();
    private readonly Dictionary<BindingSymbol, int> locals = new(); // The local of each parameter and local symbol of the body.
    private readonly List<MirOperand> arguments = []; // The evaluated arguments of the calls being built, innermost last.
    private readonly List<int> roots = [];
    private Roots? collector;
    private MirBody body = null!;
    private Koto declaration = null!;
    private int current; // The block being built.
    private int receiver; // The local a Field of the implicit self, an accessor's storage or a capture is reached from (-1: none).
    private ushort scope;
    private bool statementStart; // The next statement or terminator starts a source statement.

    /// <summary>Builds and validates the MIR of the latest Binding pass, once per pass.</summary>
    /// <returns>The program, with its construction records and validator findings.</returns>
    internal MirProgram Build()
    {
        var version = HirTables.Publish(compilation);
        if (version == this.program.Version)
        {
            return this.program;
        }

        this.program.Clear();
        this.visited.Clear();
        this.omissions.Clear();
        // The default templates first, which the bodies that omit them copy.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var module in compilation.SourceModules)
            {
                this.Module(module.Syntax, pass == 0);
            }

            this.Module(compilation.Library.Kotonoha.Syntax, pass == 0);
        }

        this.program.Version = version;
        return this.program;
    }

    private static BoundType TypeOf(Koto node) => HirTables.Meaning(node) as BoundType ?? BoundType.Unit;

    // The parameter slot a body reads at an argument position (SPEC 7.3): the receiver first (a constructor's or destructor's self at -1,
    // else the self parameter at its written slot), then the other parameters in slot order; false past the last.
    private static bool Slot(BindingSymbol function, int position, out int slot)
    {
        var special = CallPlan.Parameter(function, -1) is not null;
        var receiver = function.ReceiverIndex;
        slot = special ? position - 1 : receiver < 0 || position > receiver ? position : position == 0 ? receiver : position - 1;
        return position < ((FunctionKoto)function.Declaration).Parameters.Count + (special ? 1 : 0);
    }

    // The argument position of a parameter slot a call passes, Slot's inverse; a constructor's self is the call's destination.
    private static int Position(BindingSymbol? function, int slot)
    {
        var receiver = function?.ReceiverIndex ?? -1;
        return receiver < 0 || slot > receiver ? slot : slot == receiver ? 0 : slot + 1;
    }

    // The syntax of a node, its scope and whether it starts a source statement, which the first statement or terminator built for the
    // source statement takes; a local does not take it.
    private SourceInfo Info(Koto node, bool local = false)
    {
        var info = new SourceInfo(SourceRef.Of(node), this.scope, this.statementStart && !local);
        this.statementStart &= local;
        return info;
    }

    // The definitions of a module in id order, a #Test function's a Test body and a Closure's keyed by its own symbol; a struct instance
    // Field's initializer is built into each constructor of its struct. Or the templates of the defaults the HIR published for the module's
    // named functions (an anonymous function has none, SPEC 7.2.2).
    private void Module(SyntaxTable table, bool templates)
    {
        if (templates)
        {
            foreach (var decl in table.Hir.Defaults!)
            {
                if (!decl.Function.IsAnonymous && !this.visited.ContainsKey(decl))
                {
                    this.Visit(decl);
                }
            }

            return;
        }

        var nodes = table.Nodes;
        for (var id = 1; id < nodes.Length; id++)
        {
            if (nodes[id] is not { } node || !ReferenceEquals(node.SyntaxOwner, table) || HirTables.Op(node) is not (HirOp.Body or HirOp.Closure))
            {
                continue;
            }

            var symbol = HirTables.Symbol(node);
            switch (node)
            {
                case FunctionKoto function: // A module's top-level statements have no symbol (SPEC 22.2.1).
                    var kind = function.IsAnonymous ? MirBodyKind.Closure : TestDefinition.Marker(function) is null ? MirBodyKind.Fn : MirBodyKind.Test;
                    var result = function.IsAnonymous ? symbol?.Type ?? BoundType.Unit : TypeOf(node);
                    this.Body(new(symbol, kind, symbol is null ? table.Module : 0), node, symbol, result, (Koto?)function.Body ?? function.ExpressionBody!);
                    break;
                case PropertyAccessorKoto accessor:
                    var executing = CallPlan.Accessor(accessor); // SPEC 11.2: the function its calls target.
                    kind = accessor.AccessorKind == PropertyAccessorKind.Get ? MirBodyKind.Getter : MirBodyKind.Setter;
                    this.Body(new(executing, kind), node, executing, executing.Type ?? BoundType.Unit, accessor.Body!);
                    break;
                case PropertyKoto property when HirTables.ResOf(node) == HirRes.Static:
                    this.Body(new(symbol, MirBodyKind.StaticInit), node, null, symbol!.Type ?? BoundType.Unit, property.InitializerKoto!);
                    break;
            }
        }
    }

    // A body: _0 of the result Type, the arguments with the receiver first (an owned one dropped when the body is left, SPEC 16.2.1), a
    // Closure's being its environment in the mode of its call (SPEC 7.6.3), the value into _0 with its temporaries, then Return where the
    // value completes. A constructor builds its object in _0, its self, from fresh storage (SPEC 6.2.3.3): the base constructor into the
    // base subobject, this layer's Field initializers in declaration order, then the body, whose Unit value is set aside. A default
    // template's (plan 5.6.2), a generic body of its function, takes the slots its call prepared for the earlier parameters as shared
    // references p_i: ref/P_i in slot order (PreparedInput), which its names read through (Bound), and gives the parameter's value.
    private void Body(MirDefKey key, Koto declaration, BindingSymbol? function, BoundType result, Koto value)
    {
        var template = key.Kind == MirBodyKind.DefaultTemplate;
        var body = this.body = this.program.Begin(key, SourceRef.Of(template ? value : declaration));
        (this.declaration, this.scope, this.statementStart) = (declaration, 0, false);
        this.locals.Clear();
        this.targets.Clear();
        (this.temporaries, this.lexical, this.scopeIds) = (-1, -1, 0);
        this.scopes.Clear();
        this.registrations.Clear();
        this.trees.Clear();
        var parameters = this.OpenScope(true);
        PairBinder.Collect(declaration, body.Binders);
        var self = !template && declaration is FunctionKoto { IsConstructor: true } constructor ? AdtDef.ReceiverType(constructor) : null;
        body.AddLocal(new(self ?? result, MirLocalKind.Return, this.Info(declaration, true)));
        this.receiver = self is not null ? 0 : function is null ? -1 : template ? ((uint)function.ReceiverIndex < (uint)key.Index ? function.ReceiverIndex + 1 : -1)
            : function.ReceiverIndex >= 0 || CallPlan.Parameter(function, -1) is not null ? 1 : -1;
        for (var i = 0; template && i < key.Index; i++)
        {
            var parameter = CallPlan.Parameter(function!, i)!;
            this.locals[parameter] = this.Argument(compilation.Types.Reference(SemanticsKind.Ref, parameter.Type ?? BoundType.Unit), parameter.Name, parameters, MirLocalRole.PreparedInput);
        }

        if (self is not null)
        {
            this.locals[CallPlan.Parameter(function!, -1)!] = 0;
        }
        else if (declaration is FunctionKoto { IsAnonymous: true } closure && BoundClosure.Of(closure) is { EnvironmentType: { } environment } plan)
        {
            this.receiver = this.Argument(plan.Receiver == SemanticsKind.Owner ? environment : compilation.Types.Reference(plan.Receiver, environment), null, parameters);
        }

        for (var position = self is null ? 0 : 1; !template && function is not null && Slot(function, position, out var slot); position++)
        {
            var parameter = CallPlan.Parameter(function, slot)!;
            this.locals[parameter] = this.Argument(parameter.Type ?? BoundType.Unit, parameter.Name, parameters);
        }

        this.current = body.AddBlock();
        var temporaries = this.OpenScope(false);
        var done = self is null ? new MirPlace(0) : this.Temp(BoundType.Unit, value);
        this.targets.Add(new(declaration, done, -1, -1, 0, 0, false)); // A return's target.
        if (self is not null)
        {
            if (((FunctionKoto)declaration).BaseInitializer is { } baseCall)
            {
                this.Initialize(body.Project(new(0), MirProjectionKind.Base, 1), baseCall);
            }

            for (var i = 0; i < AdtDef.Count(self); i++)
            {
                if (AdtDef.Field(self, i).InitializerKoto is { } initializer)
                {
                    this.Initialize(body.Project(new(0), MirProjectionKind.Field, i), initializer);
                }
            }
        }

        this.Result(done, value, value);
        this.CloseScope(temporaries);
        this.CloseScope(parameters);
        // A default's value that does not complete goes on to its return, the call that takes it, for the checks only (SPEC 14.10.3).
        var (end, check) = (this.Info(value), template && !Completes(value) ? body.AddBlock() : -1);
        body.Terminate(this.current, Completes(value) ? MirTerminator.Return(end) : MirTerminator.Unreachable(end, check));
        if (check >= 0)
        {
            body.Terminate(check, MirTerminator.Return(end));
        }

        this.validator.Validate(body, this.program.Findings);
    }

    // An argument local, dropped when the body is left where its Type may need drop (SPEC 16.2.1).
    private int Argument(BoundType type, string? name, int parameters, MirLocalRole role = MirLocalRole.None)
    {
        var local = this.body.AddLocal(new(type, MirLocalKind.Arg, this.Info(this.declaration, true), Name: name, Role: role));
        if (MirTypes.MayNeedDrop(type))
        {
            this.Register(Cleanup.Drop, local, null, parameters);
        }

        return local;
    }

    // A step of a constructor as a statement of its own (SPEC 6.2.3.3): the base call into the base subobject, or a Field's initializer,
    // as its Adjust takes it, into the Field.
    private void Initialize(MirPlace place, Koto node)
    {
        this.statementStart = true;
        var temporaries = this.OpenScope(false);
        if (HirTables.AdjustOf(node).Acquire == HirAcquire.Unset)
        {
            this.Into(place, node);
        }
        else
        {
            this.Assign(place, node);
        }

        this.CloseScope(temporaries);
    }

    // A block item for its effect, its temporaries ending with it (SPEC 3.6.2): a local's declaration, or a value into a temp; a declaration
    // builds nothing here.
    private void Statement(Koto item)
    {
        this.statementStart = true;
        var temporaries = this.OpenScope(false);
        switch (HirTables.Op(item))
        {
            case HirOp.Syntax or HirOp.Body:
                break;
            case HirOp.Let:
                this.Let((VariableKoto)item);
                break;
            default:
                this.Into(this.Temp(item), item);
                break;
        }

        this.CloseScope(temporaries);
        this.statementStart = false;
    }

    // The value a body or a block's last item leaves in the destination: the item as its consumer takes it, a block's own result, or the
    // container's Unit after the item as a statement where the container completes (SPEC 7.1, 14.2).
    private void Result(MirPlace destination, Koto item, Koto container)
    {
        this.statementStart = true;
        if (HirTables.AdjustOf(item).Acquire != HirAcquire.Unset)
        {
            this.Assign(destination, item);
        }
        else if (HirTables.Op(item) == HirOp.Block)
        {
            this.Into(destination, item);
        }
        else
        {
            this.Statement(item);
            if (HirTables.CompletionOf(container) == HirCompletion.Completes)
            {
                this.Unit(destination, container);
            }
        }
    }

    // SPEC 14.2, 16.2: a block's items in a scope of their own (an unsafe block's, SPEC 14.3.3), the last one its result; the locals and
    // defers it registers are cleaned up when it is left. Its last item's temporaries are its enclosing statement's (SPEC 3.6.2).
    private void Block(MirPlace destination, CodeBlockKoto block, MirScopeKind kind = MirScopeKind.Lexical)
    {
        var outer = this.scope;
        this.scope = (ushort)this.body.AddScope(outer, kind, SourceRef.Of(block));
        var lexical = this.OpenScope(true);
        var items = block.Items;
        for (var i = 0; i < items.Count - 1; i++)
        {
            this.Statement(items[i]);
        }

        if (items.Count > 0)
        {
            this.Result(destination, items[^1], block);
        }
        else
        {
            this.Unit(destination, block);
        }

        this.CloseScope(lexical);
        this.scope = outer;
    }

    // SPEC 6.3: a local of its symbol's Type, visible after its declaration, initialized as its initializer's Adjust takes it.
    private void Let(VariableKoto variable)
    {
        var symbol = HirTables.Symbol(variable)!;
        var local = this.Live(this.body.AddLocal(new(symbol.Type ?? BoundType.Unit, MirLocalKind.Var, this.Info(variable, true), variable.VariableKind == VariableKind.Var, symbol.Name)), this.lexical);
        if (variable.InitializerKoto is { } initializer)
        {
            this.Assign(new(local), initializer);
        }

        this.locals[symbol] = local;
    }

    // The one way a form without a building rule is built (plan 5.4, 6.2): a located record, merged by location, and an Opaque value in
    // its place.
    private void Unsupported(Koto node, MirPlace destination)
    {
        this.program.Report(SourceRef.Of(node));
        this.Opaque(node, destination);
    }

    // An Opaque value of the destination's Type in place of a node, its site naming the locals the node's syntax reads, writes or borrows;
    // where the node cannot complete, its block ends and building continues in a block only the checks reach (SPEC 14.10.3).
    private void Opaque(Koto node, MirPlace destination)
    {
        this.roots.Clear();
        (this.collector ??= new(this)).Visit(node);
        this.Site(node, destination, Completes(node));
    }

    // An Opaque value at a node's site naming the roots collected; where it cannot complete, building continues in a block only the
    // checks reach.
    private void Site(Koto node, MirPlace destination, bool completes)
    {
        var site = this.body.AddOpaque(SourceRef.Of(node), CollectionsMarshal.AsSpan(this.roots));
        this.body.Push(this.current, MirStatement.Assign(destination, MirRvalue.Opaque(this.body.TypeOf(destination) ?? BoundType.Unit, site), this.Info(node)));
        if (!completes)
        {
            var next = this.body.AddBlock();
            this.body.Terminate(this.current, MirTerminator.Unreachable(this.Info(node), next));
            this.current = next;
        }
    }

    // The locals, parameters, environment bindings and statics a replaced node's syntax names (their Res) or a Closure in it captures,
    // the roots of its Opaque site.
    private sealed class Roots(MirBuilder builder) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            var symbol = HirTables.Symbol(node);
            switch (HirTables.ResOf(node))
            {
                case HirRes.Local or HirRes.Param or HirRes.PreparedParam or HirRes.Capture:
                    this.Add(symbol);
                    break;
                case HirRes.Static when symbol is not null:
                    this.Add(builder.StaticRoot(symbol).Root);
                    break;
            }

            var captures = node is FunctionKoto { IsAnonymous: true } closure ? BoundClosure.Of(closure)?.Captures : null;
            for (var i = 0; i < captures?.Count; i++)
            {
                this.Add(captures[i].Source);
            }

            node.VisitChildren(this);
        }

        private void Add(BindingSymbol? symbol)
        {
            if (symbol is not null && builder.Bound(symbol) is { } place)
            {
                this.Add(place.Root);
            }
        }

        private void Add(int root)
        {
            if (!builder.roots.Contains(root))
            {
                builder.roots.Add(root);
            }
        }
    }
}
