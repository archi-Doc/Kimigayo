// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Builds and solves whole-value and enum-construction ownership CFGs using committed Binding operations.</summary>
public sealed partial class OwnershipAnalysis
{
    internal const int DeferredOperationLimit = 8192;
    private readonly Compilation compilation;
    private readonly List<OwnershipBody> bodies = new();
    private readonly List<OwnershipBody> bodyPool = new();
    private readonly List<OwnershipIssue> issues = new();
    private readonly List<FunctionKoto> libraryBodies = new();
    private readonly Collector collector;
    private readonly List<Registration> locals = new();
    private readonly List<Registration> temporaries = new();
    private readonly List<LoopFrame> loops = new();
    private readonly List<int> arguments = new();
    private readonly List<CheckingContinuation> terminalSeeds = new();
    private readonly HashSet<InvocationKoto> referenceCalls = new(ReferenceEqualityComparer.Instance);
    private ControlFlowAnalysis? flow;
    private OwnershipBody body = null!;
    private int current;
    private int resultPlace;
    private int normalExit;
    private int abortExit;
    private int registrationSequence;
    private int checkingRegion;
    private int deferredLoopBase;
    private int deferredSelectionBase;
    private int deferredDepth;
    private int activeDeferred;

    internal OwnershipAnalysis(Compilation compilation)
    {
        this.compilation = compilation;
        this.collector = new(this);
    }

    public OwnershipResult Result { get; private set; }

    public IReadOnlyList<OwnershipBody> Bodies => this.bodies;

    public IReadOnlyList<OwnershipIssue> Issues => this.issues;

    public ControlFlowAnalysis? ControlFlow => this.flow;

    /// <summary>Checks all selected project function bodies after final Binding, reusing storage.</summary>
    /// <returns>The current subset verification summary.</returns>
    public OwnershipResult Analyze()
    {
        var binding = this.compilation.Binding;
        if (binding.Result.Mode != BindingMode.Final)
        {
            throw new InvalidOperationException("Ownership analysis requires final Binding.");
        }

        this.Invalidate();
        this.supportedTypes.Clear();
        this.checkingPatternTypes.Clear();
        this.visitingTypes.Clear();
        var root = this.compilation.Kotonoha.RootKoto;
        if (this.flow is null)
        {
            this.flow = ControlFlowAnalysis.Analyze(root, binding.TypeSystem);
        }
        else
        {
            this.flow.Reanalyze(root);
        }

        for (var i = 1; i < this.compilation.SourceModules.Length; i++)
        {
            this.flow.Append(this.compilation.SourceModules[i].RootKoto);
        }

        if (!binding.Result.IsComplete || !this.SupportsOriginObligations())
        {
            return this.Result;
        }

        foreach (var module in this.compilation.SourceModules)
        {
            this.collector.Visit(module.RootKoto);
        }

        for (var i = 0; i < this.libraryBodies.Count; i++)
        {
            this.flow.Append(this.libraryBodies[i]);
            this.collector.Visit(this.libraryBodies[i]);
        }

        var errors = 0;
        var unsupported = 0;
        for (var i = 0; i < this.issues.Count; i++)
        {
            if (this.issues[i].Failure is OwnershipFailure.Unsupported or OwnershipFailure.ExpansionLimit)
            {
                unsupported++;
            }
            else
            {
                errors++;
            }
        }

        var verified = this.flow.Issues.Count == 0 && this.flow.PendingBinding.Count == 0 && errors == 0 && unsupported == 0;
        if (!verified)
        {
            // A caller cannot certify a program containing an unchecked callee or flow contract.
            for (var i = 0; i < this.bodies.Count; i++)
            {
                this.bodies[i].IsVerified = false;
            }
        }

        return this.Result = new(verified, this.bodies.Count, errors, unsupported);
    }

    public void ReportDiagnostics()
    {
        for (var i = 0; i < this.issues.Count; i++)
        {
            var issue = this.issues[i];
            issue.Source.AddDiagnostic(issue.Code);
        }
    }

    internal void Invalidate()
    {
        this.Result = default;
        for (var i = 0; i < this.bodies.Count; i++)
        {
            this.bodies[i].IsVerified = false;
            this.bodies[i].InvalidateChecking();
        }

        this.bodies.Clear();
        this.libraryBodies.Clear();
        if (this.defaultBody is { } declaration)
        {
            declaration.IsVerified = false;
            declaration.InvalidateChecking();
        }

        this.issues.Clear();
        this.candidates.Clear();
        this.unmatchedCheckingSeeds.Clear();
    }

    private void Build(FunctionKoto function, int declarationDefault = -1)
    {
        try
        {
            this.BuildBody(function, declarationDefault);
        }
        catch (DeferredExpansionLimitException limit)
        {
            this.body.ReportIssue(new(limit.SourceNode, OwnershipFailure.ExpansionLimit));
            this.issues.AddRange(this.body.IssueStorage);
        }
    }

    private void BuildBody(FunctionKoto function, int declarationDefault)
    {
        if (declarationDefault >= 0)
        {
            // One reusable declaration scratch graph, never an executable function
            // body. Its diagnostics are retained before the next default reuses it.
            this.body = this.defaultBody ??= new();
        }
        else if (this.instanceBody is { } instanceBody)
        {
            this.body = instanceBody; // Never listed with the checked source bodies.
        }
        else
        {
            if (this.bodies.Count == this.bodyPool.Count)
            {
                this.bodyPool.Add(new());
            }

            this.body = this.bodyPool[this.bodies.Count];
            this.bodies.Add(this.body);
        }

        this.body.Reset(function);
        // Abstract Origin bindings affect field Types even when layout is fully
        // concrete. Prepare the same substituted metadata used by closed calls.
        for (var parameterIndex = 0; parameterIndex < function.Parameters.Count; parameterIndex++)
        {
            if (function.Parameters[parameterIndex].Type.BoundType is { } parameterType)
            {
                this.compilation.Binding.PrepareTypeStorage(parameterType);
            }
        }

        this.locals.Clear();
        this.temporaries.Clear();
        this.loops.Clear();
        this.arguments.Clear();
        this.referenceCalls.Clear();
        this.formattingPlaces.Clear();
        this.placeValues.Clear();
        this.resultHeads.Clear();
        this.resultJoins.Clear();
        this.resultDeclarations.Clear();
        this.pendingResults.Clear();
        this.selections.Clear();
        this.candidates.Clear();
        this.comparisonDepth = 0;
        this.activeDecompositions.Clear();
        this.patternStorageNeeded.Clear();
        this.registrationSequence = 0;
        this.checkingRegion = 0;
        this.terminalSeeds.Clear();
        this.normalCheckingSeeds.Clear();
        this.caughtCheckingSeeds.Clear();
        this.deferredLoopBase = 0;
        this.deferredSelectionBase = 0;
        this.deferredDepth = 0;
        this.activeDeferred = -1;
        this.current = -1;
        this.Emit(OwnershipOperationKind.Entry, function);
        this.normalExit = this.New(OwnershipOperationKind.Exit, function);
        this.abortExit = this.New(OwnershipOperationKind.Exit, function);
        this.resultPlace = this.Place(function, declarationDefault >= 0 ? function.Parameters[declarationDefault].Type.BoundType : function.BoundSymbol?.Type ?? BoundType.Unit, OwnershipPlaceKind.Result, true);
        if (declarationDefault < 0 && function.Captures is { Length: > 0 } && function.BoundClosure is null)
        {
            this.Unsupported(function);
        }

        var parameterCount = declarationDefault >= 0 ? declarationDefault : function.Parameters.Count;
        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = function.Parameters[i];
            var type = this.Concrete(parameter.Type.BoundType);
            var place = this.Place(parameter.Type, type, OwnershipPlaceKind.Parameter, false);
            this.body.SymbolPlaces[this.compilation.Binding.ParameterSymbol(function, i)] = place;
            this.locals.Add(new(place, parameter.Type, this.registrationSequence++));
            var initialized = this.Emit(OwnershipOperationKind.Produce, parameter.Type, place);
            if (type is not null && (ScalarResult(type) || ReferenceTypes.IsString(type)))
            {
                this.SetValue(initialized, OwnershipValueKind.Parameter, [], constant: i);
            }

            if (declarationDefault < 0 && parameter.DefaultValue is not null && !ScalarDefaults.Supports(function, i))
            {
                this.Unsupported(parameter.DefaultValue ?? parameter.Type);
            }
        }

        if (declarationDefault >= 0)
        {
            this.Expression(function.Parameters[declarationDefault].DefaultValue!);
            // Prepared arguments remain owned by the pending call. Declaration
            // checking neither destroys them nor applies the callee's return contract.
            this.Connect(this.current, this.normalExit);
            this.CompleteBody(function);
            return;
        }

        if (function.BoundClosure is { } closure)
        {
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                var capture = closure.Captures[i].Environment;
                var place = this.Place(function, capture.Type, closure.EnvironmentType is null ? OwnershipPlaceKind.Parameter : OwnershipPlaceKind.Local, capture.MutableCapture);
                this.body.SymbolPlaces[capture] = place;
                var initialized = this.Emit(OwnershipOperationKind.Produce, function, place);
                this.SetValue(initialized, OwnershipValueKind.Capture, [], constant: i);
                if (closure.EnvironmentType is not null && closure.Receiver == SemanticsKind.Owner)
                {
                    this.locals.Insert(i, new(place, function, i - closure.Captures.Count));
                }
            }
        }

        this.PrepareReceiverFields(function);
        var secured = -1;
        if (function.Body is { } block)
        {
            this.Block(block);
            if (ReferenceEquals(this.body.PlaceStorage[this.resultPlace].Type, BoundType.Unit))
            {
                this.Emit(OwnershipOperationKind.Produce, function, this.resultPlace);
            }
        }
        else if (function.ExpressionBody is { } expression)
        {
            if (KotoHelper.DiscardsFunctionBody(function) || !KotoHelper.IsBodyExpression(expression))
            {
                this.Statement(expression);
                this.Emit(OwnershipOperationKind.Produce, expression, this.resultPlace);
            }
            else
            {
                var value = this.Expression(expression);
                secured = this.WriteResult(expression, this.resultPlace, value);
            }
        }
        else
        {
            this.Unsupported(function);
        }

        this.CheckConstruction(function);
        this.Cleanup(0, 0, function, CleanupReason.Return);
        this.Deliver(function, secured);
        this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
        this.CompleteBody(function);
    }

    private void CompleteBody(FunctionKoto function)
    {
        this.body.Solve();
        this.FinalizeResults();
        this.body.CheckUnreachable();
        this.body.PrepareCallReservations();
        this.body.VerifyBorrows();
        this.body.VerifyCallReservations();

        for (var i = 0; i < this.body.IssueStorage.Count; i++)
        {
            this.issues.Add(this.body.IssueStorage[i]);
        }

        this.body.IsVerified = this.body.IssueStorage.Count == 0 && function.BindingState == BindingState.Resolved;
    }

    private int Place(Koto source, BoundType? type, OwnershipPlaceKind kind, bool mutable, AcquisitionKind? plannedAcquisition = null)
    {
        var id = this.body.PlaceStorage.Count;
        type = this.Concrete(type) ?? BoundType.Unit;
        // Never has no value storage. Its result marker is only used on unreachable
        // delivery nodes; control-flow checking rejects any normal completion.
        var neverResult = kind == OwnershipPlaceKind.Result && ReferenceEquals(type, BoundType.Never);
        var invalidCopy = false;
        var acquisition = plannedAcquisition.GetValueOrDefault();
        // An instance resolves a committed CopyOrMove to the exact effect of its closed Type (SPEC 21.3.1).
        if (plannedAcquisition is null || (acquisition == AcquisitionKind.CopyOrMove && this.instance is not null))
        {
            // Primitive classification needs no Constraint environment (SPEC 3.5.1).
            var proof = type.Kind == BoundTypeKind.Primitive && (!ReferenceEquals(type, BoundType.Never) || neverResult)
                ? (type.Name == "string" ? ConstraintProof.Refuted : ConstraintProof.Proven)
                : this.compilation.Binding.ProveCopy(type, source);
            invalidCopy = proof == ConstraintProof.Error;
            acquisition = proof == ConstraintProof.Proven ? AcquisitionKind.Copy : proof == ConstraintProof.Refuted ? AcquisitionKind.Move : AcquisitionKind.CopyOrMove;
        }

        this.body.PlaceStorage.Add(new(id, source, type, kind, mutable, acquisition));
        this.placeValues.Add(-1);
        this.resultDeclarations.Add(-1);
        this.body.IsConcrete &= type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection);
        if (invalidCopy || !(neverResult || type.Kind == BoundTypeKind.Parameter || this.SupportsType(type)))
        {
            this.Unsupported(source);
        }

        return id;
    }

    private int Temporary(Koto source, bool produce = true, int projection = -1)
    {
        // A non-completing block/selection may reserve a result destination, but
        // Never has no produced value and no temporary cleanup registration.
        var kind = !produce && ReferenceEquals(source.BoundType, BoundType.Never) ? OwnershipPlaceKind.Result : OwnershipPlaceKind.Temporary;
        var id = this.Place(source, source.BoundType, kind, true);
        if (produce)
        {
            this.Emit(OwnershipOperationKind.Produce, source, id, acquisition: projection >= 0 && this.body.Places[id].Acquisition is AcquisitionKind.Move or AcquisitionKind.CopyOrMove ? this.body.Places[id].Acquisition : AcquisitionKind.None, projection: projection);
            this.RegisterTemporary(id);
        }

        return id;
    }

    // A temporary of a Type other than the syntax's own: the reference a Place call returns.
    private int ReferenceTemporary(Koto source, BoundType type)
    {
        var id = this.Place(source, type, OwnershipPlaceKind.Temporary, true);
        this.Emit(OwnershipOperationKind.Produce, source, id);
        return this.RegisterTemporary(id);
    }

    private int RegisterTemporary(int place)
    {
        this.temporaries.Add(new(place, this.body.PlaceStorage[place].Source, this.registrationSequence++));
        return place;
    }

    private int Local(Koto source)
    {
        var symbol = source.BoundSymbol;
        if (this.defaultFunction is { } function && symbol is { Kind: BindingSymbolKind.Parameter } && ReferenceEquals(symbol.Scope.Owner, function))
        {
            return (uint)symbol.Slot < (uint)this.defaultParameter ? this.defaultPlaces[symbol.Slot] : -1;
        }

        if (symbol is not null && this.body.SymbolPlaces.TryGetValue(symbol, out var id))
        {
            return id;
        }

        this.Unsupported(source);
        return -1;
    }

    private int LocalPlace(BindingSymbol? symbol, Koto source, BoundType? type, bool mutable, AcquisitionKind? acquisition = null)
    {
        // Deferred replicas have separate operation/value IDs but nonoverlapping lifetimes
        // of the same lexical binding. Declare resets the shared Place on each execution.
        if (symbol is not null && this.body.SymbolPlaces.TryGetValue(symbol, out var existing))
        {
            return existing;
        }

        var place = this.Place(source, type, OwnershipPlaceKind.Local, mutable, acquisition);
        if (symbol is not null)
        {
            this.body.SymbolPlaces.Add(symbol, place);
        }

        return place;
    }

    private int Use(Koto source, int place, PlaceUseKind use, AcquisitionKind? acquisition = null)
    {
        if (place < 0)
        {
            return -1;
        }

        if (use != PlaceUseKind.Consume)
        {
            this.Emit(use == PlaceUseKind.Read ? OwnershipOperationKind.Read : OwnershipOperationKind.Borrow, source, place);
            return place;
        }

        // Only locals and parameters reach here; temporaries transfer without a Place use.
        var stored = this.body.PlaceStorage[place];
        if (acquisition is null)
        {
            // SPEC 15.1.5: a bare exclusive reference is reborrowed in its own mode; @move transfers it.
            if (stored.Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 })
            {
                return this.BorrowStruct(source, stored.Type);
            }

            // SPEC 3.5: a bare Place never Moves; a Non-Copy or Copy-unproven Place needs @move.
            if (stored.Acquisition != AcquisitionKind.Copy)
            {
                this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
            }
        }

        this.CheckAcquisition(place, acquisition);
        var value = this.Temporary(source, false);
        this.Emit(OwnershipOperationKind.Consume, source, place, value, acquisition ?? stored.Acquisition);
        return this.RegisterTemporary(value);
    }

    private int Block(CodeBlockKoto block, int destination = -1)
        => this.Block(block, out _, destination);

    private int Block(CodeBlockKoto block, out CheckingContinuation continuation, int destination = -1, bool retainCheckingRegion = false)
    {
        var region = this.checkingRegion;
        var result = -1;
        var mark = this.locals.Count;
        for (var i = 0; i < block.Items.Count; i++)
        {
            var item = block.Items[i];
            var temps = this.temporaries.Count;
            if (destination >= 0 && block.HasTrailingExpression && KotoHelper.IsValueContext(item) && i == block.Items.Count - 1)
            {
                var value = this.Expression(item);
                result = this.WriteResult(item, destination, value);
            }
            else
            {
                this.Statement(item);
            }

            this.Cleanup(temps, this.locals.Count, item, CleanupReason.ExpressionEnd);
            this.temporaries.RemoveRange(temps, this.temporaries.Count - temps);
        }

        if (ReferenceEquals(block, this.body.Function.Body))
        {
            this.CheckConstruction(block);
        }

        // Keep the terminal source state before this body's implicit cleanup and
        // lexical-region restoration. A caller must prove that no alternative
        // terminal path was discarded before using this checking-only seed.
        continuation = this.checkingRegion != region ? this.Continuation() : new(-1);
        this.Cleanup(this.temporaries.Count, mark, block, CleanupReason.ScopeExit);
        this.locals.RemoveRange(mark, this.locals.Count - mark);
        // A transfer's source continuation ends with this lexical body. It is not
        // a normal branch completion or a loop backedge, even inside dead source.
        if (!retainCheckingRegion || !this.flow!.Nodes[block].CanCompleteNormally)
        {
            this.checkingRegion = region;
        }

        return result;
    }

    private void Statement(Koto node)
    {
        if (node is FunctionKoto or DeclarationContainerKoto or AliasKoto or UnitLiteralKoto)
        {
            return;
        }

        if (node is FieldKoto field)
        {
            var id = this.LocalPlace(field.BoundSymbol, field, field.BoundType, field.VariableKind == VariableKind.Var);

            this.locals.Add(new(id, field, this.registrationSequence++));
            this.Emit(OwnershipOperationKind.Declare, field, id);
            if (field.InitializerKoto is { } initializer)
            {
                var value = this.Expression(initializer);
                if (value >= 0)
                {
                    this.Emit(OwnershipOperationKind.Write, field, id, value);
                }
            }
        }
        else if (node is DeferredBlockKoto deferred)
        {
            this.locals.Add(new(-1, deferred, this.registrationSequence++));
        }
        else if (node is UnsafeBlockKoto unsafeBlock)
        {
            this.Block(unsafeBlock.Body);
        }
        else
        {
            this.Expression(node);
        }
    }

    private int Expression(Koto node, PlaceUseKind use = PlaceUseKind.Consume, AcquisitionKind? acquisition = null)
    {
        // SPEC 10.2: the one implicit adaptation selected at the expression's fixed expected Type.
        if (this.compilation.Binding.TryGetAdaptation(node, out var adaptation))
        {
            if (adaptation.Kind == ExpectedAdaptationKind.ReferentRead)
            {
                return this.LoadReferent(node);
            }

            // A reference read also serves a receiver that is read for member selection (SPEC 3.4.1); a borrow
            // adaptation is formed only where the value is acquired.
            if (adaptation.Kind == ExpectedAdaptationKind.ReferenceRead && acquisition is null && use is PlaceUseKind.Consume or PlaceUseKind.Read)
            {
                return this.ReadReference(node, adaptation.Type);
            }

            if (use == PlaceUseKind.Consume && acquisition is null)
            {
                return this.BorrowStruct(node, adaptation.Type);
            }
        }

        if (node.ErasedFunctionType is { } erased)
        {
            var source = this.ExpressionCore(node, PlaceUseKind.Consume, acquisition);
            if (source < 0)
            {
                return -1;
            }

            var acquired = this.Value(source);
            this.Emit(OwnershipOperationKind.CallEntry, node, source);
            var result = this.Place(node, erased, OwnershipPlaceKind.Temporary, false);
            var create = this.Emit(OwnershipOperationKind.Produce, node, result);
            this.SetValue(create, OwnershipValueKind.ClosureErasure, [acquired]);
            return this.RegisterTemporary(result);
        }

        return this.ExpressionCore(node, use, acquisition);
    }

    private int ExpressionCore(Koto node, PlaceUseKind use, AcquisitionKind? acquisition)
    {
        if (this.propertyReceivers.TryGetValue(node, out var preparedReceiver))
        {
            return preparedReceiver;
        }

        if (this.compilation.Binding.PropertyCall(node, PropertyAccessorKind.Get) is { } getter)
        {
            return this.Call(getter);
        }

        if (this.compilation.Binding.IndexerCall(node, false) is { } indexer)
        {
            return this.Expression(indexer, use, acquisition); // SPEC 4.6.9: receiver[key] through a user conformance reads the published Place.
        }

        if (this.compilation.Binding.RangeValueCall(node) is { } rangeValue)
        {
            return this.Expression(rangeValue, use, acquisition); // SPEC 4.6.3: range syntax constructs the library Range.
        }

        if (this.compilation.Binding.StorageProjection(node) is { } storage)
        {
            return this.ReadBorrowedField(storage);
        }

        if (node is IdentifierNameKoto or MemberAccessKoto && StaticScalar.TryGet(node.BoundSymbol?.Property, out var staticValue))
        {
            var result = this.Temporary(node);
            this.SetValue(this.Value(result), OwnershipValueKind.Constant, [], constant: staticValue);
            return result;
        }

        if (this.SpecialField(node))
        {
            var place = this.Local(node);
            if (place >= 0 && use == PlaceUseKind.Consume && this.body.Places[place].Acquisition != AcquisitionKind.Copy)
            {
                this.Unsupported(node); // Special receivers cannot lose initialized fields.
            }

            return this.Use(node, place, use, acquisition);
        }

        if (this.compilation.Binding.TryGetEnumConstruction(node, out var construction))
        {
            return this.ConstructEnum(node, construction!);
        }

        switch (node)
        {
            case FromEndIndexKoto fromEnd:
                var offset = this.Expression(fromEnd.Operand);
                return offset < 0 ? -1 : this.SequenceValue(fromEnd, fromEnd.BoundType!, SequenceOperation.FromEnd, offset, index: this.Value(offset));
            case FunctionKoto { BoundClosure: { } } closure:
                return this.CreateClosure(closure);
            case ParenthesizedKoto parentheses:
                return this.Expression(parentheses.Operand, use, acquisition);
            case MacroKoto { Formatting: { } tryWrite }:
                return this.TryWrite(tryWrite);
            case MacroKoto { Operand: InvocationKoto abort } when ReferenceEquals(abort.BoundCall?.Target, this.compilation.Library.Abort):
                return this.Call(abort);
            case InterpolatedStringKoto { Formatting: { } formatting }:
                return this.Formatting(formatting);
            case FormattingKoto formattingValue:
                return this.FormattingValue(formattingValue);
            case IdentifierNameKoto:
                if (node.BoundSymbol?.Kind == BindingSymbolKind.PatternCandidate)
                {
                    return this.ReadCandidate(node, use);
                }

                return this.Use(node, this.Local(node), use, acquisition);
            case StringLiteralKoto or NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto or UnitLiteralKoto or NullLiteralKoto:
                return this.Temporary(node);
            case TupleLiteralKoto tuple when tuple.Elements.Count == 0:
                return this.Temporary(node);
            case TupleLiteralKoto tuple:
                return this.ConstructAggregate(tuple, tuple.Elements);
            case ArrayLiteralKoto array when array.BoundType?.Kind == BoundTypeKind.FixedArray:
                return this.ConstructAggregate(array, array.Elements);
            case ArrayLiteralKoto array when array.BoundType?.Kind == BoundTypeKind.Array:
                // SPEC 4.3, 4.7.4: an Array literal acquires its elements as payloads that construction moves into the buffer.
                return this.ConstructAggregate(array, array.Elements);
            case DictionaryLiteralKoto { Entries.Count: 0, BoundType.Kind: BoundTypeKind.Dictionary } dictionary:
                return this.ConstructAggregate(dictionary, []);
            case TupleTypeKoto { ElementNodes.Count: 0 }:
                return this.Temporary(node);
            case InvocationKoto call when ElementAccess.IsPlaceCall(call) && !this.referenceCalls.Remove(call):
                return this.ReadPlaceCall(call, acquisition); // SPEC 7.1.1: a value use of the published Place.
            case InvocationKoto call:
                return this.Call(call);
            case IsKoto { IsRuntimeTest: true } test:
                return this.RuntimeTypeTest(test);
            case ConversionKoto conversion:
                if (conversion.ConversionBinding == ConversionBinding.ObjectUpcast)
                {
                    if (ObjectTypes.IsBorrow(conversion.BoundType))
                    {
                        return this.BorrowStruct(conversion.Left, conversion.BoundType!);
                    }

                    var owner = this.Expression(conversion.Left, PlaceUseKind.Read);
                    return this.Use(conversion, owner, PlaceUseKind.Consume, AcquisitionKind.Move);
                }

                if (conversion.ConversionBinding == ConversionBinding.Borrow && ReferenceTypes.IsBorrow(conversion.BoundType))
                {
                    return this.BorrowStruct(conversion.Left, conversion.BoundType!);
                }

                if (conversion.ConversionBinding == ConversionBinding.Follow)
                {
                    // SPEC 13.5.5.1: a value use of a selected referent copies one proven-Copy layer.
                    return this.LoadReferent(conversion.Left, 1);
                }

                if (conversion.ConversionBinding == ConversionBinding.PayloadFollow)
                {
                    this.Unsupported(conversion); // A bare Copy of a payload through its handle remains a boundary.
                    return -1;
                }

                return this.ConversionValue(conversion);
            case BinaryKoto element when ElementAccess.IsSyntax(element) && IsPointerPlace(element):
                return this.ReadPointer(element, use);
            case MemberAccessKoto member when member.Left.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary || ReferenceTypes.IsArray(member.Left.BoundType) || ReferenceTypes.IsDynamicArray(member.Left.BoundType) || ReferenceTypes.IsDictionary(member.Left.BoundType) ||
                (member.Right is IdentifierNameKoto { IdentifierName: "length" } && (FormattingTypes.IsUtf8Slice(member.Left.BoundType) || FormattingTypes.IsSliceBorrow(member.Left.BoundType))):
                return this.SequenceMember(member);
            case MemberAccessKoto member when ReferenceTypes.IsStruct(member.Left.BoundType) || ReferenceTypes.IsTuple(member.Left.BoundType) || ObjectTypes.IsBorrow(member.Left.BoundType) ||
                ElementAccess.BorrowedPathRoot(member) is not null:
                return this.ReadBorrowedField(member);
            case IndexKoto element when ReferenceTypes.IsPointer(element.Left.BoundType):
                return this.ReadPointer(element, use);
            case IndexKoto slice when slice.BoundType?.Kind == BoundTypeKind.Slice && (slice.Right is RangeKoto || ElementAccess.IsResolvedSlice(slice)):
                return this.CreateSlice(slice);
            case IndexKoto element when element.Left.BoundType?.Kind is BoundTypeKind.Slice or BoundTypeKind.Array:
                return this.ReadSlice(element, acquisition);
            case IndexKoto element when ReferenceTypes.IsArray(element.Left.BoundType) || ReferenceTypes.IsDynamicArray(element.Left.BoundType):
                return this.ReadSlice(element, acquisition);
            case BinaryKoto element when ElementAccess.IsSyntax(element):
                return this.ElementValue(element, use, acquisition);
            case BinaryKoto binary:
                return this.Binary(binary);
            case IfKoto conditional:
                return this.Conditional(conditional);
            case MatchKoto match:
                return this.Match(match);
            case LabeledKoto labeled:
                return this.Expression(labeled.Target);
            case DoKoto scoped:
                return this.ScopedBody(scoped, scoped.Body);
            case LoopKoto repeat:
                return this.Repeat(repeat);
            case DiscardKoto discard:
                this.Expression(discard.Operand);
                return this.Temporary(node);
            case RequireKoto require:
                return this.Require(require);
            case TestVerificationKoto verification:
                return this.Verification(verification);
            case ForKoto iteration:
                this.Iterate(iteration);
                return this.Temporary(node);
            case WhileKoto loop:
                this.Loop(loop);
                if (!this.flow!.Nodes[loop].CanCompleteNormally)
                {
                    this.current = -1;
                    return -1;
                }

                return this.Temporary(node);
            case JumpKoto jump:
                return this.Jump(jump);
            case CodeBlockKoto block:
                var blockValue = this.Temporary(node, false);
                this.Block(block, blockValue);
                if (!this.flow!.Nodes[block].CanCompleteNormally)
                {
                    this.current = -1;
                    return -1;
                }

                if (!block.HasTrailingExpression || !KotoHelper.IsValueContext(block))
                {
                    this.Emit(OwnershipOperationKind.Produce, block, blockValue);
                }

                return this.RegisterTemporary(blockValue);
            case DereferenceKoto dereference:
                return this.ReadPointer(dereference, use);
            case UnaryKoto unary when node.Akind is KotoKind.Not or KotoKind.PrefixPlus or KotoKind.PrefixMinus or KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement:
                return this.UnaryValue(unary);
            default:
                this.Unsupported(node);
                return -1;
        }
    }

    private int Binary(BinaryKoto binary)
    {
        if (binary.ComparisonCall is { } comparisonCall)
        {
            var compared = this.Call(comparisonCall);
            if (compared < 0)
            {
                return -1;
            }

            var comparisonResult = this.Temporary(binary);
            this.SetValue(this.Value(comparisonResult), OwnershipValueKind.ContractComparison, [this.Value(compared)], binary.Akind);
            return comparisonResult;
        }

        if ((ReferenceTypes.EndsInString(binary.Left.BoundType) || ReferenceTypes.EndsInString(binary.Right.BoundType)) && ReferenceEquals(binary.BoundType, BoundType.Boolean))
        {
            return this.StringComparison(binary);
        }

        var assignment = binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals;
        if (assignment)
        {
            var target = KotoHelper.UnwrapParentheses(binary.Left);
            if (binary.Akind != KotoKind.Equals && this.compilation.Binding.PropertyUpdateStorage(target) is { } updateStorage)
            {
                return this.UpdateProperty(binary, target, updateStorage);
            }

            if (this.compilation.Binding.PropertyCall(target, PropertyAccessorKind.Set) is { } setter)
            {
                return this.Call(setter);
            }

            target = this.compilation.Binding.StorageProjection(target) ?? target;
            if (target is ConversionKoto { ConversionBinding: ConversionBinding.Follow } followed)
            {
                return this.WriteReferent(binary, followed);
            }

            if (target is InvocationKoto placeCall && ElementAccess.IsPlaceCall(placeCall))
            {
                return this.WritePlaceCall(binary, placeCall); // SPEC 7.1.1: a write through a place uniq/T result.
            }

            if (target is IndexKoto userIndex && this.compilation.Binding.IndexerCall(userIndex, true) is { } exclusiveIndexer)
            {
                return this.WritePlaceCall(binary, exclusiveIndexer); // SPEC 4.6.9: an update selects indexUniq.
            }

            if (IsPointerPlace(target))
            {
                return this.WritePointer(binary, target);
            }

            if (target is MemberAccessKoto borrowedField && ElementAccess.BorrowedPathRoot(borrowedField) is not null)
            {
                return this.WriteBorrowedField(binary, borrowedField);
            }

            if (target is BinaryKoto element && ElementAccess.IsSyntax(element) && !this.SpecialField(target))
            {
                return binary.Akind == KotoKind.Equals ? this.AssignElement(binary, element) : this.UpdateElement(binary, element);
            }

            // SPEC 13.7: simple and compound assignment secure the RHS before the target is located and read.
            var input = this.Expression(binary.Right);
            if (input < 0)
            {
                // Like a local initializer, an abrupt RHS has no value to place.
                return -1;
            }

            if (binary.Akind != KotoKind.Equals)
            {
                var op = KotoHelper.CompoundOperation(binary.Akind);
                var previous = this.Value(this.Expression(binary.Left, PlaceUseKind.Read));
                // SPEC 5.3: p += n and p -= n displace a pointer local like p + n and p - n.
                if (!(binary.Left.BoundType?.IsNumeric == true || (ReferenceTypes.IsPointer(binary.Left.BoundType) && op is KotoKind.Plus or KotoKind.Minus)) || op == KotoKind.Invalid)
                {
                    this.Unsupported(binary);
                }

                input = this.ComputeUpdate(binary, binary.Left.BoundType, previous, this.Value(input), op);
            }

            this.Emit(OwnershipOperationKind.Write, binary, this.Local(target), input);
            return this.Temporary(binary);
        }

        if (binary is AndKoto or OrKoto)
        {
            var mark = this.terminalSeeds.Count;
            var completes = this.flow!.Nodes[binary].CanCompleteNormally;
            var output = this.ResultPlace(binary);
            var condition = this.Value(this.Expression(binary.Left, PlaceUseKind.Read));
            var branch = this.Emit(OwnershipOperationKind.Branch, binary.Left);
            if (condition >= 0)
            {
                this.SetValue(branch, OwnershipValueKind.Alias, [condition]);
            }

            var terminalRight = !this.flow.Nodes[binary.Right].CanCompleteNormally;
            var partialRight = completes && !terminalRight && this.body.CheckingRegions[this.checkingRegion].MixedTargets &&
                !(this.scopedCheckingProof ??= new(this)).Check(binary.Right, false) && this.scopedCheckingProof.Check(binary.Right);
            var fork = !completes || terminalRight || partialRight ? this.ForkChecking(branch) : null;
            var normalMark = this.normalCheckingSeeds.Count;
            var evaluate = this.New(OwnershipOperationKind.Branch, binary.Right);
            var skip = this.New(OwnershipOperationKind.Branch, binary);
            var join = this.ResultJoin(binary, output);
            var evaluateWhen = binary is AndKoto;
            this.Connect(branch, evaluate, evaluateWhen ? OwnershipEdgeKind.True : OwnershipEdgeKind.False);
            this.Connect(branch, skip, evaluateWhen ? OwnershipEdgeKind.False : OwnershipEdgeKind.True);

            this.EnterCheckingBranch(evaluate, fork);
            var region = this.checkingRegion;
            var right = this.Value(this.Expression(binary.Right, PlaceUseKind.Read));
            if (right >= 0)
            {
                var produced = this.Emit(OwnershipOperationKind.Produce, binary, output);
                this.SetValue(produced, OwnershipValueKind.Alias, [right]);
                this.ConnectResult(join, produced);
                if (partialRight && fork is not null)
                {
                    this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
                }
            }

            if (terminalRight)
            {
                this.AddTerminalSeed(this.Continuation());
            }
            else if (!completes)
            {
                // Partial RHS terminal paths are already pending; include its
                // normal tail as well before joining with the skipped path.
                this.AddTerminalSeed((this.scopedCheckingProof ??= new(this)).Check(binary.Right) ? this.Continuation() : new(-1));
            }

            this.checkingRegion = region;
            this.EnterCheckingBranch(skip, fork);
            var skipped = this.Emit(OwnershipOperationKind.Produce, binary, output);
            this.SetValue(skipped, OwnershipValueKind.Constant, [], constant: evaluateWhen ? 0 : 1);
            this.ConnectResult(join, skipped);
            if (partialRight && fork is not null)
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
                this.JoinNormalChecking(binary, normalMark, join);
            }
            else if (fork is not null && completes)
            {
                // A terminal RHS contributes no normal result. Only the skipped
                // branch reaches this checking join; its target facts stay separate.
                this.body.OperationRegions[join] = this.checkingRegion;
            }

            if (!completes)
            {
                this.AddTerminalSeed(this.Continuation());
            }

            var completed = this.CompleteResult(binary, output, join);
            if (!completes)
            {
                this.JoinChecking(binary, mark);
            }
            else
            {
                this.FilterTerminalSeeds(binary, mark);
            }

            return completed;
        }

        var left = this.Expression(binary.Left, PlaceUseKind.Read);
        var leftValue = this.Value(left);
        var rightValue = this.Value(this.Expression(binary.Right, PlaceUseKind.Read));
        if (left >= 0 && this.body.PlaceStorage[left] is { Kind: not OwnershipPlaceKind.Temporary, Acquisition: not AcquisitionKind.Copy } &&
            KotoHelper.UnwrapParentheses(binary.Right) is not (IdentifierNameKoto or StringLiteralKoto))
        {
            // Retaining a non-Copy operand view across effectful RHS evaluation needs a Loan.
            this.Unsupported(binary);
        }

        if (binary is PlusKoto && ReferenceEquals(binary.BoundType, BoundType.String))
        {
            this.Unsupported(binary);
        }

        if (leftValue < 0 || rightValue < 0)
        {
            return -1; // Later source operands were checked, but no operator value arrived.
        }

        var result = this.Temporary(binary);
        if (binary.Akind is KotoKind.EqualsEquals or KotoKind.ExclamationEquals && ReferenceEquals(this.body.Places[left].Type, BoundType.Unit))
        {
            // SPEC 13.4: Unit has one value, so after both operands are evaluated equality is constant.
            this.SetValue(this.Value(result), OwnershipValueKind.Constant, [], constant: binary.Akind == KotoKind.EqualsEquals ? 1 : 0);
            return result;
        }

        this.SetValue(this.Value(result), OwnershipValueKind.Binary, [leftValue, rightValue], binary.Akind);
        return result;
    }

    private int Conditional(IfKoto conditional)
    {
        var entry = this.current;
        var completes = this.flow!.Nodes[conditional].CanCompleteNormally;
        var forkChecking = this.body.CheckingRegions[this.checkingRegion].MixedTargets && this.CanForkCheckingBranches(conditional);
        var normalConditions = true;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        var mark = this.terminalSeeds.Count;
        var output = this.ResultPlace(conditional);
        var join = this.ResultJoin(conditional, output);
        this.selections.Add(new(conditional, output, join, this.locals.Count, this.temporaries.Count, this.comparisonDepth, forkChecking && completes));
        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var branch = conditional.Branches[i];
            var condition = this.Condition(branch.Condition);
            normalConditions &= this.flow.Nodes[branch.Condition].CanCompleteNormally;
            var test = this.Emit(OwnershipOperationKind.Branch, branch.Condition);
            if (condition >= 0)
            {
                this.SetValue(test, OwnershipValueKind.Alias, [condition]);
            }

            var fork = forkChecking ? this.ForkChecking(test) : null;
            var yes = this.New(OwnershipOperationKind.Branch, branch.Body);
            var no = this.New(OwnershipOperationKind.Branch, conditional);
            this.Connect(test, yes, OwnershipEdgeKind.True);
            this.Connect(test, no, OwnershipEdgeKind.False);

            this.EnterCheckingBranch(yes, fork);
            var result = this.Block(branch.Body, out var continuation, output, forkChecking);
            this.RecordTerminalSeed(branch.Body, continuation, completes && normalConditions);

            if (ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
            {
                this.Emit(OwnershipOperationKind.Produce, branch.Body, output);
            }

            this.ConnectResult(join, result);
            if (forkChecking && completes && normalConditions && this.flow.Nodes[branch.Body].CanCompleteNormally)
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
            }

            this.EnterCheckingBranch(no, fork);
        }

        var otherwiseResult = -1;
        if (conditional.ElseBody is { } otherwise)
        {
            otherwiseResult = this.Block(otherwise, out var continuation, output, forkChecking);
            this.RecordTerminalSeed(otherwise, continuation, completes && normalConditions);

            if (ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
            {
                this.Emit(OwnershipOperationKind.Produce, otherwise, output);
            }
        }
        else
        {
            this.Emit(OwnershipOperationKind.Produce, conditional, output);
            if (!completes || !normalConditions)
            {
                this.AddTerminalSeed(this.Continuation());
            }
        }

        this.ConnectResult(join, otherwiseResult);
        if (forkChecking && completes)
        {
            if (normalConditions && (conditional.ElseBody is null || this.flow.Nodes[conditional.ElseBody].CanCompleteNormally))
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
            }

            this.CollectCaughtChecking(conditional, caughtMark);
            this.JoinNormalChecking(conditional, normalMark, join);
        }
        else if (completes)
        {
            // A later terminal condition may have opened a checking region.
            // Earlier normal arrivals still belong to the original join's region.
            this.checkingRegion = this.body.OperationRegions[join];
        }

        this.current = join;
        this.selections.RemoveAt(this.selections.Count - 1);
        this.body.RecordCompletion(entry, join, completes);
        var completed = this.CompleteResult(conditional, output, join);
        if (!completes)
        {
            this.JoinChecking(conditional, mark);
        }
        else
        {
            this.FilterTerminalSeeds(conditional, mark);
        }

        return completed;
    }

    // A terminal branch of a completing selection stays pending until the enclosing
    // selection or scope joins every terminal path; a partial early transfer must
    // not be discarded by a later termination. Each seed keeps its target until
    // the construct handling that transfer removes it from the pending paths.
    private void RecordTerminalSeed(CodeBlockKoto block, CheckingContinuation continuation, bool completes = true)
    {
        if (!this.flow!.Nodes[block].CanCompleteNormally)
        {
            this.AddTerminalSeed(continuation);
        }
        else if (!completes)
        {
            // Nested terminal branches already retain their own histories. The
            // remaining normal tail joins them after this body's local cleanup.
            this.AddTerminalSeed((this.scopedCheckingProof ??= new(this)).Check(block) ? this.Continuation() : new(-1));
        }
    }

    private int Call(InvocationKoto call, int preparedInput = -1, int preparedReceiver = -1)
    {
        if (call.BoundValueCall is { } valueCall)
        {
            return this.CallValue(call, valueCall);
        }

        if (call.BoundCall is not { } plan)
        {
            this.Unsupported(call);
            return -1;
        }

        if (plan.Target.CompilerFunction is CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap)
        {
            return this.WholeValueUpdate(call, plan);
        }

        if (plan.Target.Declaration is FunctionKoto libraryBody && plan.Target.CompilerFunction == CompilerFunctionKind.None)
        {
            this.CollectLibraryBody(libraryBody);
        }

        var mark = this.arguments.Count;
        var loanDepth = this.comparisonDepth++;
        var reservationMark = this.body.CallReservations.Count;
        var borrows = false;
        if (plan.Receiver is { } receiver)
        {
            var prepared = this.PrepareCallArgument(call, receiver, plan.ReceiverOperation);
            borrows |= this.HasCallInspection(prepared);
            this.arguments.Add(prepared);
        }

        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = plan.ArgumentOperations[i];
            var accessor = (plan.Target.Declaration as FunctionKoto)?.Accessor;
            var prepared = accessor is not null && argument.ParameterIndex == 0 && accessor.Receiver is not null && preparedReceiver >= 0 ? preparedReceiver
                : accessor?.Kind == PropertyAccessorKind.Set && preparedInput >= 0 ? preparedInput
                : this.PrepareCallArgument(call, call.ArgumentNodes[i], argument);
            borrows |= this.HasCallInspection(prepared);
            this.arguments.Add(prepared);
        }

        this.PrepareDefaults(plan, mark);
        this.ActivateCallReservations(call, reservationMark);
        if (plan.Target.Declaration is FunctionKoto target && target.Parameters.Count != this.arguments.Count - mark)
        {
            this.Unsupported(call); // Every parameter must have an explicit or default acquisition.
        }

        var acquired = true;
        for (var i = mark; i < this.arguments.Count; i++)
        {
            if (this.arguments[i] >= 0)
            {
                this.Emit(OwnershipOperationKind.CallEntry, call, this.arguments[i]);
            }
            else
            {
                acquired = false;
            }
        }

        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        var invoke = this.Emit(OwnershipOperationKind.Call, call);
        this.Connect(invoke, this.abortExit, OwnershipEdgeKind.Abort);
        if (!acquired || ReferenceEquals(call.BoundType, BoundType.Never))
        {
            if (borrows)
            {
                this.body.CallLoans.Add(new(invoke, -1, -1, LoanRequirement.None));
            }

            this.current = -1;
            // Source after a nonreturning call is checked from the acquired argument
            // state, without adding a runtime continuation or a result initialization.
            this.BeginChecking(invoke);
            this.EndComparisonLoans(loanDepth, call);
            this.comparisonDepth = loanDepth;

            return -1;
        }

        // SPEC 7.1.1: a Place call returns the reference it publishes; the use selects the referent.
        var result = ElementAccess.IsPlaceCall(call) ? this.ReferenceTemporary(call, plan.ReturnType) : this.Temporary(call);
        var scalar = ScalarResult(this.body.Places[result].Type);
        if (scalar || SlotTypes.IsResult(this.body.Places[result].Type))
        {
            // A stored-result Call names storage; only its normal successor Produce initializes it.
            this.body.OperationStorage[invoke] = this.body.Operations[invoke] with { Place = result };
        }

        if (scalar)
        {
            this.SetValue(invoke, OwnershipValueKind.Call, []);
            this.SetValue(this.Value(result), OwnershipValueKind.Alias, [invoke]);
        }

        // Result Origins retain their source Loans independently of call-argument Loans.
        // Secure the result before ending the latter.
        var loanEnd = this.EndComparisonLoans(loanDepth, call);
        if (borrows)
        {
            this.body.CallLoans.Add(new(invoke, this.Value(result), loanEnd, ReferenceTypes.IndependentResult(plan.ReturnType) ? LoanRequirement.None : LoanRequirement.Ref));
        }

        this.comparisonDepth = loanDepth;

        return result;
    }

    private void CollectLibraryBody(FunctionKoto function)
    {
        if ((function.Body is not null || function.ExpressionBody is not null) &&
            ReferenceEquals(function.CodeContext.Kotonoha, this.compilation.Library.Kotonoha) && !this.libraryBodies.Contains(function))
        {
            this.libraryBodies.Add(function);
        }
    }

    private int Argument(Koto argument, ArgumentOperationKind kind, AcquisitionKind? acquisition = null)
    {
        if (kind is ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead)
        {
            // SPEC 10.2: a Copy read acquires the referent as a fresh Copy temporary through the reference.
            var value = kind == ArgumentOperationKind.CopyRead ? this.LoadReferent(argument) : this.Expression(argument, acquisition: acquisition);
            this.CheckAcquisition(value, acquisition);
            return value;
        }

        // A borrowed source keeps its value and responsibility; it never enters the callee.
        this.Expression(argument, PlaceUseKind.Borrow);
        this.Unsupported(argument);
        return -1;
    }

    private int Condition(Koto condition) => this.Condition(condition, out _);

    private int Condition(Koto condition, out int cleanupStart)
    {
        var mark = this.temporaries.Count;
        var value = this.Value(this.Expression(condition, PlaceUseKind.Read));
        cleanupStart = this.body.Operations.Count;
        this.Cleanup(mark, this.locals.Count, condition, CleanupReason.ExpressionEnd);
        this.temporaries.RemoveRange(mark, this.temporaries.Count - mark);
        return value;
    }

    private int Require(RequireKoto require)
    {
        var condition = this.Condition(require.Condition);
        var test = this.Emit(OwnershipOperationKind.Branch, require.Condition);
        if (condition >= 0)
        {
            this.SetValue(test, OwnershipValueKind.Alias, [condition]);
        }

        var fork = this.ForkChecking(test);
        var success = this.New(OwnershipOperationKind.Branch, require);
        var failure = this.New(OwnershipOperationKind.Branch, require.ElseBody);
        this.Connect(test, success, OwnershipEdgeKind.True);
        this.Connect(test, failure, OwnershipEdgeKind.False);
        this.EnterCheckingBranch(failure, fork);
        var region = this.checkingRegion;
        if (require.ElseBody is CodeBlockKoto block)
        {
            this.Block(block, out var continuation);
            this.AddTerminalSeed(continuation);
        }
        else
        {
            var temps = this.temporaries.Count;
            var locals = this.locals.Count;
            this.Statement(require.ElseBody);
            this.AddTerminalSeed(this.checkingRegion != region ? this.Continuation() : new(-1));
            this.Cleanup(temps, locals, require, CleanupReason.ScopeExit);
            this.temporaries.RemoveRange(temps, this.temporaries.Count - temps);
            this.locals.RemoveRange(locals, this.locals.Count - locals);
        }

        this.Connect(this.current, success);
        this.checkingRegion = region;
        this.EnterCheckingBranch(success, fork);
        return -1;
    }

    private int ScopedBody(Koto owner, CodeBlockKoto block)
    {
        var entry = this.current;
        var completes = this.flow!.Nodes[owner].CanCompleteNormally;
        var retainNormal = completes && this.body.CheckingRegions[this.checkingRegion].MixedTargets &&
            !(this.scopedCheckingProof ??= new(this)).Check(block, false) && this.scopedCheckingProof.Check(block);
        var mark = this.terminalSeeds.Count;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        var output = owner is DoKoto ? this.ResultPlace(owner) : -1;
        var join = this.ResultJoin(owner, output);
        this.selections.Add(new(owner, output, join, this.locals.Count, this.temporaries.Count, this.comparisonDepth, retainNormal));
        var result = this.Block(block, out var continuation, output, retainNormal);
        if (output >= 0 && ReferenceEquals(this.body.Places[output].Type, BoundType.Unit))
        {
            this.Emit(OwnershipOperationKind.Produce, owner, output);
        }

        this.ConnectResult(join, result);
        if (retainNormal)
        {
            if (this.flow.Nodes[block].CanCompleteNormally)
            {
                this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
            }

            this.CollectCaughtChecking(owner, caughtMark);
            this.JoinNormalChecking(owner, normalMark, join);
        }

        this.selections.RemoveAt(this.selections.Count - 1);
        this.body.RecordCompletion(entry, join, completes);
        var completed = this.CompleteResult(owner, output, join);
        if (!completes)
        {
            // A completing scope leaves its pending terminal paths to the enclosing join.
            if (continuation.Seed >= 0 && (this.scopedCheckingProof ??= new(this)).Check(block))
            {
                this.AddTerminalSeed(continuation);
                this.JoinChecking(owner, mark);
            }

            this.terminalSeeds.RemoveRange(mark, this.terminalSeeds.Count - mark);
        }
        else
        {
            this.RecordTerminalSeed(block, continuation);
            this.FilterTerminalSeeds(owner, mark);
        }

        return completed;
    }

    private int Repeat(LoopKoto loop)
    {
        var output = this.ResultPlace(loop);
        var head = this.Emit(OwnershipOperationKind.Branch, loop);
        var exit = this.ResultJoin(loop, output);
        var fork = this.CanForkTerminalLoop(loop, loop.Body) ? this.ForkChecking(head) : null;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        if (fork is not null)
        {
            var enter = this.New(OwnershipOperationKind.Branch, loop.Body);
            this.Connect(head, enter);
            this.EnterCheckingBranch(enter, fork);
        }

        this.loops.Add(new(loop, head, exit, this.locals.Count, this.temporaries.Count, output, this.comparisonDepth, fork is not null));
        var mark = this.terminalSeeds.Count;
        this.Block(loop.Body, out var continuation);
        this.RecordTerminalSeed(loop.Body, continuation);
        this.FilterTerminalSeeds(loop, mark);
        if (fork is null)
        {
            this.Connect(this.current, head, OwnershipEdgeKind.Back);
        }
        else
        {
            // Every normal arrival is an explicit, post-cleanup exit. Return
            // histories remain pending at their original enclosing extent.
            this.CollectCaughtChecking(loop, caughtMark);
            this.JoinNormalChecking(loop, normalMark, exit);
        }

        this.loops.RemoveAt(this.loops.Count - 1);
        this.body.RecordCompletion(head, exit, this.flow!.Nodes[loop].CanCompleteNormally);
        var result = this.CompleteResult(loop, output, exit);
        if (!this.flow.Nodes[loop].CanCompleteNormally && this.IsStateNeutralDivergence(loop))
        {
            // These loops cannot change an entry fact. General divergent bodies
            // need a join of their effects; using their entry would restore Moves.
            this.BeginChecking(head);
            this.terminalSeeds.RemoveRange(mark, this.terminalSeeds.Count - mark); // The proof covers loop-internal transfers.
        }

        return result;
    }

    private bool IsStateNeutralDivergence(Koto source)
    {
        while (true)
        {
            source = KotoHelper.UnwrapParentheses(source);
            if (source is LabeledKoto labeled)
            {
                source = labeled.Target;
            }
            else if (source is DoKoto { Body.Items.Count: 1 } scoped)
            {
                source = scoped.Body.Items[0];
            }
            else if (source is LoopKoto loop)
            {
                if (loop.Body.Items.Count == 1)
                {
                    var item = KotoHelper.UnwrapParentheses(loop.Body.Items[0]);
                    if (item is UnitLiteralKoto or TupleLiteralKoto { Elements.Count: 0 } or TupleTypeKoto { ElementNodes.Count: 0 } ||
                        (item is ContinueKoto { Expression: null } again && ReferenceEquals(this.flow!.Targets.GetValueOrDefault(again), loop)))
                    {
                        return true;
                    }
                }

                return (this.localLoopProof ??= new(this)).Check(loop);
            }
            else
            {
                return false;
            }
        }
    }

    private void Loop(WhileKoto loop)
    {
        var head = this.Emit(OwnershipOperationKind.Branch, loop);
        var exit = this.New(OwnershipOperationKind.Branch, loop);
        var mark = this.temporaries.Count;
        var condition = this.Value(this.Expression(loop.Condition, PlaceUseKind.Read));
        this.Cleanup(mark, this.locals.Count, loop.Condition, CleanupReason.ExpressionEnd);
        this.temporaries.RemoveRange(mark, this.temporaries.Count - mark);
        var continuation = this.CheckingSeed();
        var test = this.Emit(OwnershipOperationKind.Branch, loop.Condition);
        if (condition >= 0)
        {
            this.SetValue(test, OwnershipValueKind.Alias, [condition]);
        }

        // A terminal body has no ordinary backedge. Keep its checking histories
        // separate from zero iterations, including exits caught by this while.
        var fork = this.CanForkTerminalLoop(loop, loop.Body) ? this.ForkChecking(test) : null;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        var enter = this.New(OwnershipOperationKind.Branch, loop.Body);
        var skipped = fork is not null ? this.New(OwnershipOperationKind.Branch, loop) : exit;
        this.Connect(test, enter, OwnershipEdgeKind.True);
        this.Connect(test, skipped, OwnershipEdgeKind.False);

        this.loops.Add(new(loop, head, exit, this.locals.Count, this.temporaries.Count, Comparisons: this.comparisonDepth, Checking: fork is not null));
        this.EnterCheckingBranch(enter, fork);
        var seedMark = this.terminalSeeds.Count;
        this.Block(loop.Body, out var bodyContinuation);
        this.RecordTerminalSeed(loop.Body, bodyContinuation);
        this.FilterTerminalSeeds(loop, seedMark);
        if (fork is null)
        {
            this.Connect(this.current, head, OwnershipEdgeKind.Back);
        }
        else
        {
            this.EnterCheckingBranch(skipped, fork);
            this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
            this.Connect(this.current, exit);
            this.CollectCaughtChecking(loop, caughtMark);
            this.JoinNormalChecking(loop, normalMark, exit);
        }

        this.loops.RemoveAt(this.loops.Count - 1);
        this.current = exit;
        if (!this.flow!.Nodes[loop.Condition].CanCompleteNormally && continuation >= 0 &&
            (this.localLoopProof ??= new(this)).Check(loop, loop.Body))
        {
            // The condition's acquisitions are already reflected in this seed.
            // Only body-local effects may be omitted from the enclosing state.
            this.BeginChecking(continuation);
            this.current = -1;
            this.terminalSeeds.RemoveRange(seedMark, this.terminalSeeds.Count - seedMark); // The body proof covers its transfers.
        }
    }

    private int Jump(JumpKoto jump)
    {
        var value = jump.Expression is { } expression ? this.Expression(expression) : -1;
        // The seed includes operand acquisition, but never this transfer's cleanup.
        // Consecutive bare transfers can reuse an as-yet unused seed: no source
        // operation changed its state. Other missing origins remain unsupported.
        var seed = this.current;
        if (seed < 0 && this.checkingRegion > 0 && this.body.CheckingRegions[this.checkingRegion].Entry < 0)
        {
            seed = this.body.CheckingRegions[this.checkingRegion].Seed;
        }

        var target = this.flow!.Targets.GetValueOrDefault(jump);
        if (target is not null && ReferenceEquals(target, this.body.Function.Accessor?.Declaration))
        {
            target = this.body.Function;
        }

        var loanDepth = this.comparisonDepth;
        if (jump is ReturnKoto && this.deferredDepth == 0 && ReferenceEquals(target, this.body.Function))
        {
            loanDepth = 0;
        }
        else if (this.TryGetSelection(target, out var loanSelection))
        {
            loanDepth = loanSelection.Comparisons;
        }
        else
        {
            for (var i = this.loops.Count - 1; i >= this.deferredLoopBase; i--)
            {
                if (ReferenceEquals(this.loops[i].Source, target))
                {
                    loanDepth = this.loops[i].Comparisons;
                    break;
                }
            }
        }

        var beforeEnd = this.current;
        this.EndComparisonLoans(loanDepth, jump);
        if (this.current != beforeEnd)
        {
            seed = this.current; // Ownership state is unchanged; abandoned Loans stay ended in checking code.
        }

        var continuationRegion = this.body.CheckingRegions[this.checkingRegion];
        Koto? caughtTarget = null;
        if (jump is ReturnKoto && this.deferredDepth == 0 && ReferenceEquals(target, this.body.Function))
        {
            var secured = this.WriteResult(jump, this.resultPlace, value);
            this.CheckConstruction(jump);
            this.Cleanup(0, 0, jump, CleanupReason.Return);
            this.Deliver(jump, secured);
            this.Connect(this.current, this.normalExit, OwnershipEdgeKind.Return);
        }
        else if (jump is YieldKoto or ExitKoto && this.TryGetSelection(target, out var selection))
        {
            var result = selection.Result >= 0 ? this.WriteResult(jump, selection.Result, value) : -1;

            this.Cleanup(selection.Temporaries, selection.Locals, jump, CleanupReason.SelectionResult);
            if (selection.Checking && this.flow.ReachesTarget(jump))
            {
                this.RecordCaughtChecking(selection.Source);
                caughtTarget = selection.Source;
            }

            this.ConnectResult(selection.Join, result);
        }
        else
        {
            var found = false;
            for (var i = this.loops.Count - 1; i >= this.deferredLoopBase; i--)
            {
                var loop = this.loops[i];
                if (ReferenceEquals(loop.Source, target) && jump is ExitKoto or ContinueKoto)
                {
                    var result = jump is ExitKoto && loop.Result >= 0 ? this.WriteResult(jump, loop.Result, value) : -1;

                    this.Cleanup(loop.Temporaries, loop.Locals, jump, CleanupReason.LoopTransfer);
                    if (jump is ContinueKoto)
                    {
                        this.Connect(this.current, loop.Head, OwnershipEdgeKind.Back);
                    }
                    else
                    {
                        if (loop.Checking && this.flow.ReachesTarget(jump))
                        {
                            this.RecordCaughtChecking(loop.Source);
                            caughtTarget = loop.Source;
                        }

                        this.ConnectResult(loop.Exit, result);
                    }

                    found = true;
                    break;
                }
            }

            if (!found)
            {
                this.Unsupported(jump);
            }
        }

        this.current = -1;
        this.BeginChecking(seed, ReferenceEquals(target, this.body.Function) ? null : target, continuationRegion, caughtTarget);
        return -1;
    }

    private void Cleanup(int tempStart, int localStart, Koto source, CleanupReason reason)
    {
        var start = this.body.CleanupStepStorage.Count;
        // Merge lexical registrations without allocating or removing live outer entries.
        // Separate marks let ExpressionEnd clean temporaries without ending a local's lifetime.
        var temporary = this.temporaries.Count - 1;
        var local = this.locals.Count - 1;
        while (temporary >= tempStart || local >= localStart)
        {
            var registration = temporary >= tempStart && (local < localStart || this.temporaries[temporary].Sequence > this.locals[local].Sequence)
                ? this.temporaries[temporary--] : this.locals[local--];
            if (registration.Source is DeferredBlockKoto deferred)
            {
                this.FinishCleanupSegment(start, reason);
                this.ExecuteDeferred(deferred);
                start = this.body.CleanupStepStorage.Count;
            }
            else if (registration.IsSubject)
            {
                this.CleanupSubject(registration.Place, source);
            }
            else
            {
                this.CleanupPlace(registration.Place, registration.Source, source);
            }
        }

        this.FinishCleanupSegment(start, reason);
    }

    private void FinishCleanupSegment(int start, CleanupReason reason)
    {
        var count = this.body.CleanupStepStorage.Count - start;
        if (count != 0)
        {
            var operation = this.body.CleanupStepStorage[start].Operation;
            this.body.CleanupPlanStorage.Add(new(this.body.IncomingEdges[operation], start, count, reason));
        }
    }

    private void CleanupPlace(int place, Koto declaration, Koto source)
    {
        var operation = this.Emit(OwnershipOperationKind.Cleanup, source, place);
        this.body.OperationSteps[operation] = this.body.CleanupStepStorage.Count;
        this.body.CleanupStepStorage.Add(new(operation, place, declaration, place < 0 ? CleanupAction.Unsupported : CleanupAction.Skip));
    }

    private int New(OwnershipOperationKind kind, Koto source, int place = -1, int input = -1, AcquisitionKind acquisition = AcquisitionKind.None, LoanRequirement loanMode = LoanRequirement.None, int projection = -1, int reservation = -1)
    {
        var id = this.body.OperationStorage.Count;
        if (id >= DeferredOperationLimit && this.body.DeferredPlans.Count != 0)
        {
            throw new DeferredExpansionLimitException(source);
        }

        this.body.OperationStorage.Add(new(kind, source, place, input, acquisition, LoanMode: loanMode, Projection: projection, Reservation: reservation));
        this.RecordComparisonState(source);
        this.resultHeads.Add(-2);
        this.RecordValue(id, kind, source, place, input);
        this.body.EdgeHeads.Add(-1);
        this.body.IncomingEdges.Add(-1);
        this.body.OperationSteps.Add(-1);
        this.body.OperationRegions.Add(this.checkingRegion);
        return id;
    }

    private int Emit(OwnershipOperationKind kind, Koto source, int place = -1, int input = -1, AcquisitionKind acquisition = AcquisitionKind.None, LoanRequirement loanMode = LoanRequirement.None, int projection = -1, int reservation = -1)
    {
        var id = this.New(kind, source, place, input, acquisition, loanMode, projection, reservation);
        if (this.checkingRegion > 0 && this.body.CheckingRegions[this.checkingRegion].Entry < 0)
        {
            var region = this.body.CheckingRegions[this.checkingRegion];
            this.body.CheckingRegions[this.checkingRegion] = region with { Entry = id };
        }

        this.Connect(this.current, id);
        this.current = id;
        return id;
    }

    private int Connect(int from, int to, OwnershipEdgeKind kind = OwnershipEdgeKind.Normal)
    {
        if (from < 0)
        {
            return -1;
        }

        this.body.EdgeStorage.Add(new(from, to, kind, this.body.EdgeHeads[from]));
        this.body.EdgeHeads[from] = this.body.EdgeStorage.Count - 1;
        this.body.IncomingEdges[to] = this.body.EdgeStorage.Count - 1;
        return this.body.EdgeStorage.Count - 1;
    }

    private void Unsupported(Koto source)
    {
        this.Emit(OwnershipOperationKind.Unsupported, source);
        this.body.ReportIssue(new(source, OwnershipFailure.Unsupported));
    }

    private readonly record struct Registration(int Place, Koto Source, int Sequence, bool IsSubject = false);

    private readonly record struct LoopFrame(Koto Source, int Head, int Exit, int Locals, int Temporaries, int Result = -1, int Comparisons = 0, bool Checking = false);

    private sealed class Collector : KotoVisitor
    {
        private readonly OwnershipAnalysis owner;

        internal Collector(OwnershipAnalysis owner)
        {
            this.owner = owner;
        }

        public override void Visit(Koto node)
        {
            if (node is FunctionKoto && TestDefinition.Marker(node) is not null && !TestDefinition.IsIncluded(node))
            {
                return;
            }

            if (node is FunctionKoto function)
            {
                this.owner.CheckDefaultDeclarations(function);
                // Requirement declarations and foreign imports have no body to verify.
                if (function.Body is not null || function.ExpressionBody is not null || !(function.IsRequirement || Parser.HasLibraryImport(function.AttributeChain)))
                {
                    this.owner.Build(function);
                }
            }
            else if (node is PropertyAccessorKoto { Body: not null } syntax)
            {
                var property = ((PropertyKoto)syntax.Parent!).BoundSymbol!.Property!;
                var accessor = syntax.AccessorKind == PropertyAccessorKind.Get ? property.Getter : property.Setter;
                if (accessor.Result is not { CarriesOrigin: false } result || this.owner.compilation.Binding.ProveCopy(result, syntax) != ConstraintProof.Proven ||
                    (accessor.Input is { } input && (input.CarriesOrigin || this.owner.compilation.Binding.ProveCopy(input, syntax) != ConstraintProof.Proven)))
                {
                    this.owner.issues.Add(new(node, OwnershipFailure.Unsupported));
                }
                else
                {
                    var executable = this.owner.compilation.Binding.AccessorFunction(accessor);
                    this.owner.flow!.Append(executable);
                    this.owner.Build(executable);
                }
            }

            node.VisitChildren(this);
        }
    }
}
