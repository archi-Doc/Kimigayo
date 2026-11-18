// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns construction and validation of one MIR body per definition or closure.
// Input: syntax children, published HIR/plans, declaration views, TyCtxt and one Binding view, the guessed completion nodes of a require's
// failure body (Binding.GuessedCompletionNodes, DECISIONS R5-M186). Output: a validated MirProgram: the bodies of a root range built
// eagerly, and any other body a lookup of the open version demands (Demand, PLAN G951), each with its program pass where it is built.
// Numbers the library's ordinals (PLAN G951 U2-K, G950 R-4): the library's templates, then the library walk's definitions in id order
// (Library), each through the one node-to-definition filter (Walked) without building, so a library definition's body has the same
// ordinal (Slot) in an eager and a lazy build; MirProgram numbers every other body.
// Op, Adjust and PlaceShape decide lowering; unsupported forms use the one located Unsupported path; it trusts the facts each Op guarantees.
// Every runtime-index selection on storage lends its receiver where it locates it (a guard, or a range's backing), the receiver's check
// at the selection, where its position local is located too, the subject A1 reports an access through the Index under (DECISIONS R4-M56).
// An Opaque temp, which holds a derived or unsupported value where its consumer needs a Place, is no storage: no Ref, pair-case
// acquisition or case adaptation lends a Place in it; its consumer receives a derived value (DECISIONS R4-M149).
// A body's expansions, a default's template copy and a defer body's copy, consult one body budget where a copy would begin (Refuses).
// Must not read Binding internals, infer from parents or let consumers rebuild missing facts.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

/// <summary>The MIR builder (plan P6, R3 shadow): once per Binding pass it publishes the HIR, builds one body for each definition whose
/// value node the HIR publishes (a function, an accessor, a static's initializer, a Closure's whatever its creation) and validates it.
/// It reads syntax children, the HIR columns and plans, <see cref="TyCtxt"/> and, at a require's failure body, Binding's guessed completion
/// nodes only. A form no rule builds goes through
/// <see cref="Unsupported(Koto, MirPlace)"/>, and building continues; it trusts the facts each published Op guarantees
/// (<see cref="Binding.HasFacts"/>), and records a node the publisher found without them (<see cref="HirTables.Broken"/>) as
/// InternalInvariant_Kd; an Error node becomes a derived Opaque after the operands the HIR keeps in place (DECISIONS R5-M63).
/// The program, its bodies and the builder's scratch are reused, so a warm
/// rebuild allocates nothing.</summary>
/// <param name="compilation">The compilation.</param>
internal sealed partial class MirBuilder(Compilation compilation)
{
    private const int ExpansionLimit = 65_536; // MIR statements (IMPL 21.3.2): no copy begins past it, one of known size never ends past it.
    private readonly MirProgram program = new();
    private readonly MirValidator validator = new(compilation.Types);
    private readonly Dictionary<BindingSymbol, int> locals = new(); // The local of each parameter and local symbol of the body.
    private readonly List<MirOperand> arguments = []; // The evaluated arguments of the calls being built, innermost last.
    private readonly List<int> leastAfter = []; // By argument of the calls being built: the least position of those evaluated after it.
    private readonly HashSet<BoundType> walked = new(ReferenceEqualityComparer.Instance); // A Type walk's scratch (MayNeedDrop, HoldsRaw).
    private MirBody body = null!;
    private Koto declaration = null!;
    private int named; // The bodies of the version whose program pass ran: a position of the build-order list (Name).
    private SourceInfo entry; // The body's declaration in its own scope: where _0 without a written result Type and the drop tree's edges are.
    private Koto? constructed; // A constructor's body, whose completion requests its exit (SPEC 6.3.5); null in other bodies.
    private int current; // The block being built.
    private int receiver; // The local a Field of the implicit self, an accessor's storage or a capture is reached from (-1: none).
    private int scope;
    private bool statementStart; // The next statement or terminator starts a source statement.
    private int[] slots = []; // The library walk's definitions' node ids, ascending: a definition's slot is its index here (Library).
    private int slotCount;
    private int templates; // The library's templates, built before its walk: the ordinals before the slots.

    /// <summary>Builds and validates the MIR of the latest Binding pass, once per pass (PLAN G951): every module's default templates, the
    /// bodies of the modules below the root range, then each body's program pass, the library segment's before the program segment's,
    /// once per library segment: a version keeps its segment while the library version holds and the version before it keeps it
    /// (<see cref="MirProgram.Keeps"/>, R-K2'', PLAN G950, G951 U2-K), else builds a new one; the version's lookups build any other body
    /// on demand (<see cref="Demand"/>) until it is sealed.
    /// One version is built with one range, and every version of a builder with the library on one side of its range (D1 per builder,
    /// PLAN G951 U2): a version asked for with another range, or a later version with the library on the other side, is a broken
    /// contract, since the analyses and the collector keep a version's facts by its <see cref="MirProgram.Version"/> and a kept library
    /// segment (<see cref="MirProgram.LibraryVersion"/>, which holds no range) has the shape of its builder's side: every library body
    /// inside, the library's templates only outside. A range that grows (a module added) keeps the side. Version 0 is no version: before
    /// any Binding pass the program is empty, whatever the range.</summary>
    /// <param name="rootModules">The root range: <see cref="Parsing.SyntaxTable.LibraryModule"/> + 1 builds every body eagerly, the
    /// source modules' count the program's.</param>
    /// <returns>The program, with its construction records and validator findings.</returns>
    internal MirProgram Build(int rootModules)
    {
        var version = HirTables.Publish(compilation);
        var (library, built) = (compilation.Library.Kotonoha.Syntax, this.program.RootModules);
        if (this.program.Version != 0 && (version == this.program.Version ? rootModules != built : (library.Module < rootModules) != (library.Module < built)))
        {
            throw new InvalidOperationException($"Version {version} asks for the root range {rootModules} after version {this.program.Version} was built with {built} (InternalInvariant).");
        }

        if (version == this.program.Version)
        {
            return this.program;
        }

        // The library version (PLAN G950-K): Binding's library publication version, per compilation and written once at the library's
        // freeze (G949 S1-1, ruling 174), so a later version keeps the segment while each version keeps it. A built version follows a
        // Bind whose library pass froze (a thrown library pass publishes no Result and builds no MIR): 0 is a broken contract.
        var libraryVersion = HirTables.LibraryVersion(compilation);
        if (libraryVersion == 0)
        {
            throw new InvalidOperationException($"Version {version} follows no frozen library pass (InternalInvariant).");
        }

        this.program.Open(this, rootModules);
        if (libraryVersion != this.program.LibraryVersion || !this.program.Keeps())
        {
            this.program.Clear();
            this.visited.Clear();
            this.omissions.Clear();
            this.named = 0;
            // The library's range first, the ordinals' prefix, then the program's (R8-L1 M-B1): its walk numbers its definitions in
            // every build and builds them in a build whose root range holds the library (Library). In each, the default templates
            // first, which the bodies that omit them copy: a library body omits the library's only, so the library's nodes of the
            // omission graph are its prefix. The templates are built in every build, a demanded body's included (G951 M2). A library
            // body names library bodies only (SPEC 18), so the library segment's program passes run here, before the program's (G191).
            this.Module(library, templates: true);
            this.Library(library);

            this.Name();
            this.program.EndLibrary(libraryVersion);
            this.libraryOmissions = this.omissions.Count;
        }
        else
        {
            this.program.ClearProgram();
            this.ClearProgramOmissions();
            this.named = this.program.LibraryBodies.Length; // The kept library list's bodies had their program pass.
        }

        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var module in compilation.SourceModules)
            {
                if (pass == 0 || module.Syntax.Module < rootModules)
                {
                    this.Module(module.Syntax, pass == 0);
                }
            }
        }

        this.Name(); // The program segment's program passes (G191).
        this.program.Version = version;
        return this.program;
    }

    /// <summary>Builds the body of a definition outside the root range that a lookup of an open version did not find (MirProgram's demand
    /// point, PLAN G951): the body <see cref="Definition"/> gives exactly the key, built as the module walk builds it, with its program
    /// pass, whose lookups may demand more. The builder is idle here: it never looks a body up while it builds one.</summary>
    /// <param name="key">The definition.</param>
    /// <returns>The body, or null when the definition has none: the HIR publishes no value for it, the key is not its body's, or its
    /// module is in the root range, whose walk built every body.</returns>
    internal MirBody? Demand(MirDefKey key)
    {
        var node = (Koto?)(key.Decl?.Declaration as FunctionKoto)?.Accessor?.Declaration ?? key.Decl?.Declaration; // An accessor's executing function's.
        if (node?.SyntaxOwner is not { } table || table.Module < this.program.RootModules || Walked(table, node) is not { } definition || definition.Key != key)
        {
            return null;
        }

        this.Body(definition.Key, node, definition.Function, definition.Result, definition.Value);
        var body = this.body;
        this.Name();
        return body;
    }

    private static BoundType TypeOf(Koto node) => HirTables.TypeOf(node) ?? BoundType.Unit;

    // SPEC 14.2.2, 14.2.4 (DECISIONS BU-98): the Type of the value a statement or an explicit discard holds for its operand: a discarded
    // Place's published Type (PlanRow.DiscardedType: a stored exclusive reference's Reborrow by Binding's name, BU-27), else, a value
    // statement's, its Meaning.
    private static BoundType Held(Koto node) => HirTables.PlanOf(node).DiscardedType ?? TypeOf(node);

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

    // The one map from a published definition's node to its body's key and Body's arguments, the module walk's and Demand's (G951 N6): a
    // function's Fn body, a #Test function's Test body, a Closure's keyed by its own symbol, a module's top-level statements by the module's
    // ordinal; an accessor's Getter or Setter keyed by the function its calls target (SPEC 11.3); a static's initializer; null for another
    // node.
    private static (MirDefKey Key, BindingSymbol? Function, BoundType Result, Koto Value)? Definition(Koto node, SyntaxTable table)
    {
        var symbol = HirTables.Symbol(node);
        switch (symbol?.Kind)
        {
            case null or BindingSymbolKind.Function: // A module's top-level statements have no symbol (SPEC 22.4.1).
                var function = (FunctionKoto)node;
                var closure = BoundClosure.Of(function) is not null; // A Closure's definition (DECISIONS R5-M79).
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
                return (new(symbol, kind, symbol is null ? table.Module : 0), symbol, result, (Koto?)function.Body ?? function.ExpressionBody!);
            case BindingSymbolKind.PropertyAccessor:
                var accessor = (PropertyAccessorKoto)node;
                var executing = CallPlan.Accessor(accessor); // SPEC 11.3: the function its calls target.
                kind = accessor.AccessorKind == PropertyAccessorKind.Get ? MirBodyKind.Getter : MirBodyKind.Setter;
                return (new(executing, kind), executing, executing.Type ?? BoundType.Unit, accessor.Body!);
            case BindingSymbolKind.Property when HirTables.ResOf(node) == HirRes.Static:
                return (new(symbol, MirBodyKind.StaticInit), null, symbol!.Type ?? BoundType.Unit, ((PropertyKoto)node).InitializerKoto!);
            default:
                return null;
        }
    }

    // A module walk's definition at a node: its body's key and Body's arguments (Definition) where the node is the table's own and the
    // HIR publishes it with a value (Published), null for another node. The one filter of the module walk, the library's numbering and
    // Demand, so every library body a build or a lookup builds has a slot.
    private static (MirDefKey Key, BindingSymbol? Function, BoundType Result, Koto Value)? Walked(SyntaxTable table, Koto? node)
        => node is not null && ReferenceEquals(node.SyntaxOwner, table) && Published(node) ? Definition(node, table) : null;

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
                if (BoundClosure.Of(decl.Function) is null && !this.visited.ContainsKey(decl))
                {
                    this.Visit(decl);
                }
            }

            return;
        }

        var nodes = table.Nodes;
        for (var id = 1; id < nodes.Length; id++)
        {
            if (Walked(table, nodes[id]) is { } definition)
            {
                this.Body(definition.Key, nodes[id]!, definition.Function, definition.Result, definition.Value);
            }
        }
    }

    // The library's walk (PLAN G951 U2-K, G950 R-4), once per library segment after the library's templates: its definitions in id
    // order, each numbered, its slot the next (Slot), and built here, at its slot, in a build whose root range holds the library; in a
    // build whose range is the program's a lookup builds a slot's body (Demand). So the library's ordinal range, its templates and its
    // slots (MirProgram.Reserve), is the same in every build, and an eager build's ordinals are the walk's order by construction.
    private void Library(SyntaxTable table)
    {
        var nodes = table.Nodes;
        (this.slotCount, this.templates) = (0, this.program.Bodies.Length);
        for (var id = 1; id < nodes.Length; id++)
        {
            if (Walked(table, nodes[id]) is { } definition)
            {
                if (this.slotCount == this.slots.Length)
                {
                    Array.Resize(ref this.slots, Math.Max(512, this.slotCount * 2));
                }

                this.slots[this.slotCount++] = id;
                if (table.Module < this.program.RootModules)
                {
                    this.Body(definition.Key, nodes[id]!, definition.Function, definition.Result, definition.Value);
                }
            }
        }

        this.program.Reserve(this.templates + this.slotCount);
    }

    // A library definition's ordinal: the library's templates' count plus its position in the library walk (Library), the one number of
    // its body in every build and version of the library segment. A definition the walk did not number is a broken contract.
    private int Slot(Koto declaration)
    {
        var index = Array.BinarySearch(this.slots, 0, this.slotCount, declaration.SyntaxId);
        return index >= 0 ? this.templates + index
            : throw new InvalidOperationException($"The library walk numbered no slot for node {declaration.SyntaxId} (InternalInvariant).");
    }

    // The program pass (G191) of each body built since the last, in build order, each once: the root build's after its walk, a demanded
    // body's after it is built; a lookup in a pass may demand a body, which joins the queue (MirValidator.Program.cs).
    private void Name()
    {
        while (this.named < this.program.Bodies.Length)
        {
            this.validator.Validate(this.program, this.program.Bodies[this.named++]);
        }
    }

    // A body: _0 of the result Type, the arguments with the receiver first (an owned one dropped when the body is left, SPEC 16.2.1), a
    // Closure's being its environment in the mode of its call (SPEC 7.7.3), the value into _0 with its temporaries, then Return where the
    // value completes. A destructor's self (_1) is no owner but its destruction receiver (MirLocalRole.Receiver, SPEC 16.3.1): storage the
    // destroyed value's creation made; the body's end cleans up its layer's parts (FieldCleanup, SPEC 16.3.2). A
    // constructor builds its object in _0, its self (MirLocalRole.Receiver), in the fresh storage its caller's construction expression
    // made (SPEC 6.3.2-6.3.4): the base constructor into the base subobject, this layer's Field initializers in declaration order, then the
    // body, whose Unit value is set aside (6.3.5). A default template's (plan 5.6.2), a generic body of its function, takes the slots its
    // call prepared for the earlier parameters as shared references p_i: ref/P_i in slot order (PreparedInput), which its names read
    // through (Bound), and gives the parameter's value. A parameter has no syntax node of its own: it, a template's p_i included, is located
    // at its written Type node, the one a fact about the slot names (DECISIONS R4-M90 D6, BU-48), and a function's result at its written
    // result Type (TyCtxt.SignatureSite); a Closure's environment, a constructor's or destructor's self, an accessor's arguments, a
    // default template's _0 (the parameter's value) and a result without a written Type are located at their declaration.
    private void Body(MirDefKey key, Koto declaration, BindingSymbol? function, BoundType result, Koto value)
    {
        var template = key.Kind == MirBodyKind.DefaultTemplate;
        var source = SourceRef.Of(template ? value : declaration);
        var body = this.body = template || !ReferenceEquals(declaration.SyntaxOwner, compilation.Library.Kotonoha.Syntax)
            ? this.program.Begin(key, source) : this.program.Begin(key, source, this.Slot(declaration));
        (this.declaration, this.scope, this.statementStart) = (declaration, 0, false);
        this.locals.Clear();
        this.targets.Clear();
        this.opaqueTemps.Clear();
        (this.temporaries, this.lexical, this.scopeIds, this.prepared) = (-1, -1, 0, 0);
        this.scopes.Clear();
        this.registrations.Clear();
        this.trees.Clear();
        var parameters = this.OpenScope(block: true);
        PairBinder.Collect(compilation.Binding, declaration, body.Binders);
        var special = template || function is null ? null : CallPlan.Parameter(function, -1); // A constructor's or destructor's self.
        var self = !template && AdtDef.IsConstructor(function) ? AdtDef.ReceiverType(compilation.Binding, (FunctionKoto)declaration) : null;
        this.entry = this.Info(declaration, local: true);
        body.AddLocal(self is null
            ? new(result, MirLocalKind.Return, template ? this.entry : this.Info(TyCtxt.SignatureSite(declaration, -1), local: true))
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
                TyCtxt.SignatureSite(declaration, i),
                parameters,
                MirLocalRole.PreparedInput);
        }

        if (self is not null)
        {
            this.locals[special!] = 0;
        }
        else if (special is not null)
        {
            var layer = this.body.AddLocal(
                new(special.Type ?? BoundType.Unit, MirLocalKind.Arg, this.Info(declaration, local: true), Name: special.Name, Role: MirLocalRole.Receiver));
            this.Register(Cleanup.Fields, this.locals[special] = layer, null, parameters);
        }
        else if (key.Kind == MirBodyKind.Closure && BoundClosure.Of((FunctionKoto)declaration) is { EnvironmentType: { } environment } plan)
        {
            this.receiver = this.Argument(
                plan.Receiver == SemanticsKind.Owner ? environment : compilation.Types.Reference(plan.Receiver, environment),
                null,
                declaration,
                parameters);
        }

        // The parameters after the special receiver in slot order, or receiver first (MirCallee.Slot), each at its written Type occurrence
        // (SPEC 8.15.3: a parameter's layout record at the use that needs the layout), an accessor's at its declaration.
        for (var position = 0; !template && function is not null && position < this.Arity(function); position++)
        {
            var slot = special is null ? MirCallee.Slot(function, position) : position;
            var parameter = CallPlan.Parameter(function, slot)!;
            this.locals[parameter] = this.Argument(parameter.Type ?? BoundType.Unit, parameter.Name, TyCtxt.SignatureSite(declaration, slot), parameters);
        }

        this.current = body.AddBlock();
        this.OpenScope(block: false); // The body value's temporaries, which its end leaves (End).
        var done = self is null ? new MirPlace(0) : this.Hold(BoundType.Unit, value);
        this.constructed = self is null ? null : value;
        this.targets.Add(new(declaration, done, -1, -1, -1, At: default)); // A return's target, opened at the body's scope.
        if (self is not null)
        {
            this.Construct(self, (FunctionKoto)declaration);
        }

        var valued = this.TargetBody(value, input: false);
        if (self is not null)
        {
            this.Made(done); // A constructor's body value, set aside.
        }

        this.End(value, valued);
        this.Pop(parameters);
        // SPEC 14.10.3: a non-value end of the body, which no construct closes, goes on nowhere; a default's, built after its cleanup,
        // to its return, the call that takes its value, for the checks only.
        foreach (var (block, info) in this.ends)
        {
            var check = template ? body.AddBlock() : -1;
            body.Terminate(block, MirTerminator.Unreachable(info, check));
            if (check >= 0)
            {
                body.Terminate(check, MirTerminator.Return(info));
            }
        }

        this.ends.Clear();
        this.body = this.program.End(); // The stored body, which the validator marks and a default's copies read.
        this.validator.Validate(this.body, this.program.Findings);
    }

    // IMPL 21.3.2 (DECISIONS R4-M176): the body budget both expansions consult where a copy would begin: a default's template copy, of its
    // known size, and a defer body's copy, whose size is not known before it is built (0). No copy begins past the limit, and one of known
    // size never ends past it, so a body passes it only by what the copies begun before it build. A refused copy is not built, and the
    // node of this body whose expansion it is, the omitting call or the defer statement, records ExpansionLimit_Kd (Resource; once per
    // node, MirReport's key).
    private bool Refuses(int size, Koto node)
    {
        if (this.body.Statements.Length + size <= ExpansionLimit)
        {
            return false;
        }

        this.Record(node, DiagnosticCode.ExpansionLimit_Kd);
        return true;
    }

    // An argument local located at a syntax node, dropped when the body is left where its Type may need drop (SPEC 16.2.1).
    private int Argument(BoundType type, string? name, Koto source, int parameters, MirLocalRole role = MirLocalRole.None)
    {
        var local = this.body.AddLocal(new(type, MirLocalKind.Arg, this.Info(source, local: true), Name: name, Role: role));
        this.Created(local, parameters);
        return local;
    }
}
