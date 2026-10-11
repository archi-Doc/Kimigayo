// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

/// <summary>The MIR builder (plan P6, R3 shadow): once per Binding pass it publishes the HIR, builds one body for each definition the
/// HIR names (<see cref="HirOp.Body"/>: a function, an accessor, a static's initializer) and validates it. It reads syntax children,
/// the HIR columns and plans and <see cref="TyCtxt"/> only. A form no rule builds goes through <see cref="Unsupported(Koto, MirPlace)"/>, and building
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
        foreach (var module in compilation.SourceModules)
        {
            this.Module(module.Syntax);
        }

        this.Module(compilation.Library.Kotonoha.Syntax);
        this.program.Version = version;
        return this.program;
    }

    private static BoundType TypeOf(Koto node) => HirTables.Meaning(node) as BoundType ?? BoundType.Unit;

    // The parameter slot a body reads as argument local 1 + position (SPEC 7.3): the receiver first (a constructor's or destructor's
    // self at -1, else the self parameter at its written slot), then the other parameters in slot order; false past the last.
    private static bool Slot(BindingSymbol function, int position, out int slot)
    {
        var special = CallPlan.Parameter(function, -1) is not null;
        var receiver = function.ReceiverIndex;
        slot = special ? position - 1 : receiver < 0 || position > receiver ? position : position == 0 ? receiver : position - 1;
        return position < ((FunctionKoto)function.Declaration).Parameters.Count + (special ? 1 : 0);
    }

    // The argument position of a parameter slot a call passes, Slot's inverse for a function without a constructor's or destructor's self.
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

    // The definitions of a module in id order; a struct instance Field's initializer is built into each constructor of its struct.
    private void Module(SyntaxTable table)
    {
        var nodes = table.Nodes;
        for (var id = 1; id < nodes.Length; id++)
        {
            if (nodes[id] is not { } node || !ReferenceEquals(node.SyntaxOwner, table) || HirTables.Op(node) != HirOp.Body)
            {
                continue;
            }

            switch (node)
            {
                case FunctionKoto function: // A module's top-level statements have no symbol (SPEC 22.2.1).
                    var symbol = HirTables.Symbol(node);
                    this.Body(new(symbol, MirBodyKind.Fn, symbol is null ? table.Module : 0), node, symbol, TypeOf(node), (Koto?)function.Body ?? function.ExpressionBody!);
                    break;
                case PropertyAccessorKoto accessor:
                    var executing = CallPlan.Accessor(accessor); // SPEC 11.2: the function its calls target.
                    var kind = accessor.AccessorKind == PropertyAccessorKind.Get ? MirBodyKind.Getter : MirBodyKind.Setter;
                    this.Body(new(executing, kind), node, executing, executing.Type ?? BoundType.Unit, accessor.Body!);
                    break;
                case PropertyKoto property when HirTables.ResOf(node) == HirRes.Static:
                    this.Body(new(HirTables.Symbol(node), MirBodyKind.StaticInit), node, null, HirTables.Symbol(node)!.Type ?? BoundType.Unit, property.InitializerKoto!);
                    break;
            }
        }
    }

    // A body: _0 of the result Type, the arguments with the receiver first, the value into _0, then Return where the value completes.
    private void Body(MirDefKey key, Koto declaration, BindingSymbol? function, BoundType result, Koto value)
    {
        var body = this.body = this.program.Begin(key, SourceRef.Of(declaration));
        (this.declaration, this.scope, this.statementStart) = (declaration, 0, false);
        this.locals.Clear();
        this.targets.Clear();
        this.targets.Add(new(declaration, new(0), -1, -1, 0, false)); // A return's target.
        PairBinder.Collect(declaration, body.Binders);
        body.AddLocal(new(result, MirLocalKind.Return, this.Info(declaration, true)));
        for (var position = 0; function is not null && Slot(function, position, out var slot); position++)
        {
            var parameter = CallPlan.Parameter(function, slot)!;
            this.locals[parameter] = body.AddLocal(new(parameter.Type ?? BoundType.Unit, MirLocalKind.Arg, this.Info(declaration, true), Name: parameter.Name));
        }

        this.current = body.AddBlock();
        if (declaration is FunctionKoto { IsConstructor: true } constructor)
        {
            // SPEC 6.2.3: the base completes first, then this layer's Field initializers run; U13 builds both into the constructor.
            if (constructor.BaseInitializer is { } baseCall)
            {
                this.Unsupported(baseCall, this.Temp(baseCall));
            }

            var self = AdtDef.ReceiverType(constructor);
            for (var i = 0; self is not null && i < AdtDef.Count(self); i++)
            {
                if (AdtDef.Field(self, i).InitializerKoto is not null)
                {
                    this.Unsupported(AdtDef.Field(self, i), this.Temp(AdtDef.FieldType(self, i) ?? BoundType.Unit, AdtDef.Field(self, i)));
                }
            }
        }

        this.Result(new(0), value, value);
        var end = this.Info(value);
        body.Terminate(this.current, Completes(value) ? MirTerminator.Return(end) : MirTerminator.Unreachable(end));
        this.validator.Validate(body, this.program.Findings);
    }

    // A block item for its effect: a local's declaration, or a value into a temp; a declaration builds nothing here.
    private void Statement(Koto item)
    {
        this.statementStart = true;
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

    // SPEC 14.2: a block's items in a scope of their own (an unsafe block's, SPEC 14.3.3), the last one its result.
    private void Block(MirPlace destination, CodeBlockKoto block, MirScopeKind kind = MirScopeKind.Lexical)
    {
        var outer = this.scope;
        this.scope = (ushort)this.body.AddScope(outer, kind, SourceRef.Of(block));
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

        this.scope = outer;
    }

    // SPEC 6.3: a local of its symbol's Type, visible after its declaration, initialized as its initializer's Adjust takes it.
    private void Let(VariableKoto variable)
    {
        var symbol = HirTables.Symbol(variable)!;
        var local = this.body.AddLocal(new(symbol.Type ?? BoundType.Unit, MirLocalKind.Var, this.Info(variable, true), variable.VariableKind == VariableKind.Var, symbol.Name));
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
        var site = this.body.AddOpaque(SourceRef.Of(node), CollectionsMarshal.AsSpan(this.roots));
        this.body.Push(this.current, MirStatement.Assign(destination, MirRvalue.Opaque(this.body.TypeOf(destination) ?? BoundType.Unit, site), this.Info(node)));
        if (!Completes(node))
        {
            var next = this.body.AddBlock();
            this.body.Terminate(this.current, MirTerminator.Unreachable(this.Info(node), next));
            this.current = next;
        }
    }

    // The locals, parameters and statics a replaced node's syntax names (their Res), the roots of its Opaque site.
    private sealed class Roots(MirBuilder builder) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            var root = HirTables.Symbol(node) is not { } symbol ? 0 : HirTables.ResOf(node) switch
            {
                HirRes.Local or HirRes.Param when builder.locals.TryGetValue(symbol, out var local) => local,
                HirRes.Static => builder.StaticRoot(symbol).Root,
                _ => 0,
            };

            if (root != 0 && !builder.roots.Contains(root))
            {
                builder.roots.Add(root);
            }

            node.VisitChildren(this);
        }
    }
}
