// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns construction and validation of one MIR body per definition or closure.
// Input: syntax children, published HIR/plans, declaration views and TyCtxt. Output: a validated MirProgram.
// Op, Adjust and PlaceShape decide lowering; unsupported forms use the one located Unsupported path; it trusts the facts each Op guarantees.
// Every runtime-index selection on storage lends its receiver where it locates it (a guard, or a range's backing), the receiver's check
// at the selection, where its position local is located too, the subject A1 reports an access through the Index under (DECISIONS R4-M56).
// An Opaque temp, which holds a derived or unsupported value where its consumer needs a Place, is no storage: no Ref, pair-case
// acquisition or case adaptation lends a Place in it; its consumer receives a derived value (DECISIONS R4-M149).
// Must not read Binding internals, infer from parents or let consumers rebuild missing facts.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

/// <summary>The MIR builder (plan P6, R3 shadow): once per Binding pass it publishes the HIR, builds one body for each definition whose
/// value node the HIR publishes (a function, an accessor, a static's initializer, a Closure's whatever its creation) and validates it.
/// It reads syntax children, the HIR columns and plans and <see cref="TyCtxt"/> only. A form no rule builds goes through
/// <see cref="Unsupported(Koto, MirPlace)"/>, and building continues; it trusts the facts each published Op guarantees
/// (<see cref="Binding.HasFacts"/>), and records a node the publisher found without them (<see cref="HirTables.Broken"/>) as
/// InternalInvariant_Kd; an Error node becomes a derived Opaque after the operands the HIR keeps in place (DECISIONS R5-M63).
/// The program, its bodies and the builder's scratch are reused, so a warm
/// rebuild allocates nothing.</summary>
/// <param name="compilation">The compilation.</param>
internal sealed partial class MirBuilder(Compilation compilation)
{
    private readonly MirProgram program = new();
    private readonly MirValidator validator = new(compilation.Types);
    private readonly Dictionary<BindingSymbol, int> locals = new(); // The local of each parameter and local symbol of the body.
    private readonly List<MirOperand> arguments = []; // The evaluated arguments of the calls being built, innermost last.
    private readonly List<int> leastAfter = []; // By argument of the calls being built: the least position of those evaluated after it.
    private readonly HashSet<BoundType> walked = new(ReferenceEqualityComparer.Instance); // A Type walk's scratch (MayNeedDrop, HoldsRaw).
    private MirBody body = null!;
    private Koto declaration = null!;
    private SourceInfo entry; // The body's declaration in its own scope: where _0 without a written result Type and the drop tree's edges are.
    private Koto? constructed; // A constructor's body, whose completion requests its exit (SPEC 6.3.5); null in other bodies.
    private int current; // The block being built.
    private int receiver; // The local a Field of the implicit self, an accessor's storage or a capture is reached from (-1: none).
    private ushort scope;
    private bool statementStart; // The next statement or terminator starts a source statement.

    /// <summary>Builds and validates the MIR of the latest Binding pass, once per pass: each body, then the program.</summary>
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

        this.validator.Validate(this.program); // What the bodies require of each other (G191).
        this.program.Version = version;
        return this.program;
    }

    private static BoundType TypeOf(Koto node) => HirTables.Meaning(node) as BoundType ?? BoundType.Unit;

    // Whether the HIR publishes a definition its walk reached (an Op written; a synthesized executing function, which shares its accessor's
    // body, has none) and evaluates its value node (its body, expression body or initializer, not Syntax): exactly a Body's or a Closure's,
    // and a closure definition's whatever its creation (DECISIONS R5-M63).
    private static bool Published(Koto node)
    {
        var value = node switch
        {
            FunctionKoto function => (Koto?)function.Body ?? function.ExpressionBody,
            PropertyAccessorKoto accessor => accessor.Body,
            PropertyKoto property => property.InitializerKoto,
            _ => null,
        };
        return value is not null && HirTables.Op(node) != HirOp.Unset && HirTables.Op(value) != HirOp.Syntax;
    }

    // The syntax of a node, its scope and whether it starts a source statement, which the first statement or terminator built for the
    // source statement takes; a local does not take it.
    private SourceInfo Info(Koto node, bool local = false)
    {
        var info = new SourceInfo(SourceRef.Of(node), this.scope, this.statementStart && !local);
        this.statementStart &= local;
        return info;
    }

    // The definitions of a module whose value the HIR publishes (Published), in id order, a #Test function's a Test body and a Closure's
    // keyed by its own symbol; a struct instance
    // Field's initializer is built into each constructor of its struct. Or the module's broken HIR contracts as records, then the templates
    // of the defaults the HIR published for its named functions (an anonymous function has none, SPEC 7.2.2).
    private void Module(SyntaxTable table, bool templates)
    {
        if (templates)
        {
            foreach (var node in table.Hir.Broken!)
            {
                this.program.Report(SourceRef.Of(node), DiagnosticCode.InternalInvariant_Kd); // A broken HIR contract (DECISIONS R4-M61).
            }

            foreach (var decl in table.Hir.Defaults!)
            {
                if (decl.Function.ClosureDefinition is null && !this.visited.ContainsKey(decl))
                {
                    this.Visit(decl);
                }
            }

            return;
        }

        var nodes = table.Nodes;
        for (var id = 1; id < nodes.Length; id++)
        {
            if (nodes[id] is not { } node || !ReferenceEquals(node.SyntaxOwner, table) || !Published(node))
            {
                continue;
            }

            var symbol = HirTables.Symbol(node);
            switch (symbol?.Kind)
            {
                case null or BindingSymbolKind.Function: // A module's top-level statements have no symbol (SPEC 22.4.1).
                    var function = (FunctionKoto)node;
                    var closure = function.ClosureDefinition is not null; // A Closure's definition (DECISIONS R5-M79).
                    MirBodyKind kind;
                    if (closure)
                    {
                        kind = MirBodyKind.Closure;
                    }
                    else
                    {
                        kind = TestDefinition.Marker(function) is null ? MirBodyKind.Fn : MirBodyKind.Test;
                    }

                    var result = closure ? symbol?.Type ?? BoundType.Unit : TypeOf(node);
                    this.Body(new(symbol, kind, symbol is null ? table.Module : 0), node, symbol, result, (Koto?)function.Body ?? function.ExpressionBody!);
                    break;
                case BindingSymbolKind.PropertyAccessor:
                    var accessor = (PropertyAccessorKoto)node;
                    var executing = CallPlan.Accessor(accessor); // SPEC 11.3: the function its calls target.
                    kind = accessor.AccessorKind == PropertyAccessorKind.Get ? MirBodyKind.Getter : MirBodyKind.Setter;
                    this.Body(new(executing, kind), node, executing, executing.Type ?? BoundType.Unit, accessor.Body!);
                    break;
                case BindingSymbolKind.Property when HirTables.ResOf(node) == HirRes.Static:
                    this.Body(new(symbol, MirBodyKind.StaticInit), node, null, symbol!.Type ?? BoundType.Unit, ((PropertyKoto)node).InitializerKoto!);
                    break;
            }
        }
    }

    // A body: _0 of the result Type, the arguments with the receiver first (an owned one dropped when the body is left, SPEC 16.2.1), a
    // Closure's being its environment in the mode of its call (SPEC 7.7.3), the value into _0 with its temporaries, then Return where the
    // value completes. A destructor's self is no owner: the body's end cleans up its layer's parts (FieldCleanup, SPEC 16.3.1-16.3.2). A
    // constructor builds its object in _0, its self (MirLocalRole.Receiver), in the fresh storage its caller's construction expression
    // made (SPEC 6.3.2-6.3.4): the base constructor into the base subobject, this layer's Field initializers in declaration order, then the
    // body, whose Unit value is set aside (6.3.5). A default template's (plan 5.6.2), a generic body of its function, takes the slots its
    // call prepared for the earlier parameters as shared references p_i: ref/P_i in slot order (PreparedInput), which its names read
    // through (Bound), and gives the parameter's value. A parameter has no syntax node of its own: it, a template's p_i included, is located
    // at its written Type node, the one a fact about the slot names (DECISIONS R4-M90 D6, BU-48), and a function's result at its written
    // result Type; a Closure's environment, a constructor's or destructor's self, an accessor's arguments, a default template's _0 (the
    // parameter's value) and a result without a written Type are located at their declaration.
    private void Body(MirDefKey key, Koto declaration, BindingSymbol? function, BoundType result, Koto value)
    {
        var template = key.Kind == MirBodyKind.DefaultTemplate;
        var body = this.body = this.program.Begin(key, SourceRef.Of(template ? value : declaration));
        (this.declaration, this.scope, this.statementStart) = (declaration, 0, false);
        this.locals.Clear();
        this.targets.Clear();
        this.opaqueTemps.Clear();
        (this.temporaries, this.lexical, this.scopeIds, this.prepared) = (-1, -1, 0, 0);
        this.scopes.Clear();
        this.registrations.Clear();
        this.trees.Clear();
        var parameters = this.OpenScope(block: true);
        PairBinder.Collect(declaration, body.Binders);
        var special = template || function is null ? null : CallPlan.Parameter(function, -1); // A constructor's or destructor's self.
        var self = !template && AdtDef.IsConstructor(function) ? AdtDef.ReceiverType((FunctionKoto)declaration) : null;
        var written = declaration as FunctionKoto;
        this.entry = this.Info(declaration, local: true);
        body.AddLocal(self is null
            ? new(result, MirLocalKind.Return, !template && written?.ReturnType is { } type ? this.Info(type, local: true) : this.entry)
            : new(self, MirLocalKind.Return, this.entry, Role: MirLocalRole.Receiver));
        if (self is not null)
        {
            this.receiver = 0;
        }
        else if (function is null)
        {
            this.receiver = -1;
        }
        else if (template)
        {
            this.receiver = (uint)function.ReceiverIndex < (uint)key.Index ? function.ReceiverIndex + 1 : -1;
        }
        else
        {
            this.receiver = function.ReceiverIndex >= 0 || special is not null ? 1 : -1;
        }

        for (var i = 0; template && i < key.Index; i++)
        {
            var parameter = CallPlan.Parameter(function!, i)!;
            this.locals[parameter] = this.Argument(
                compilation.Types.Reference(SemanticsKind.Ref, parameter.Type ?? BoundType.Unit),
                parameter.Name,
                written!.Parameters[i].Type,
                parameters,
                MirLocalRole.PreparedInput);
        }

        if (self is not null)
        {
            this.locals[special!] = 0;
        }
        else if (special is not null)
        {
            var layer = this.body.AddLocal(new(special.Type ?? BoundType.Unit, MirLocalKind.Arg, this.Info(declaration, local: true), Name: special.Name));
            this.Register(Cleanup.Fields, this.locals[special] = layer, null, parameters);
        }
        else if (key.Kind == MirBodyKind.Closure && ((FunctionKoto)declaration).ClosureDefinition is { EnvironmentType: { } environment } plan)
        {
            this.receiver = this.Argument(
                plan.Receiver == SemanticsKind.Owner ? environment : compilation.Types.Reference(plan.Receiver, environment),
                null,
                declaration,
                parameters);
        }

        // The parameters after the special receiver in slot order, or receiver first (MirCallee.Slot), each at its written Type occurrence
        // (SPEC 8.15.3: a parameter's layout record at the use that needs the layout), an accessor's at its declaration.
        for (var position = 0; !template && function is not null && position < Arity(function); position++)
        {
            var slot = special is null ? MirCallee.Slot(function, position) : position;
            var parameter = CallPlan.Parameter(function, slot)!;
            this.locals[parameter] = this.Argument(parameter.Type ?? BoundType.Unit, parameter.Name, written?.Parameters[slot].Type ?? declaration, parameters);
        }

        this.current = body.AddBlock();
        var temporaries = this.OpenScope(block: false);
        var done = self is null ? new MirPlace(0) : this.Hold(BoundType.Unit, value);
        this.constructed = self is null ? null : value;
        this.targets.Add(new(declaration, done, -1, -1, At: default, Reached: false)); // A return's target, opened at the body's scope.
        if (self is not null)
        {
            this.Construct(self, (FunctionKoto)declaration);
        }

        this.Result(done, value, value, input: false);
        if (self is not null)
        {
            this.Made(done); // A constructor's body value, set aside.
        }

        this.CloseScope(temporaries);
        this.CloseScope(parameters);
        // A default's value that does not complete goes on to its return, the call that takes it, for the checks only (SPEC 14.10.3).
        var end = this.Info(value);
        var check = template && !Completes(value) ? body.AddBlock() : -1;
        body.Terminate(this.current, Completes(value) ? MirTerminator.Return(end) : MirTerminator.Unreachable(end, check));
        if (check >= 0)
        {
            body.Terminate(check, MirTerminator.Return(end));
        }

        this.validator.Validate(body, this.program.Findings);
    }

    // An argument local located at a syntax node, dropped when the body is left where its Type may need drop (SPEC 16.2.1).
    private int Argument(BoundType type, string? name, Koto source, int parameters, MirLocalRole role = MirLocalRole.None)
    {
        var local = this.body.AddLocal(new(type, MirLocalKind.Arg, this.Info(source, local: true), Name: name, Role: role));
        this.Created(local, parameters);
        return local;
    }
}
