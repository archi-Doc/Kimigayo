// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Scopes and drop trees (SPEC 16, plan 5.6.4). A Var or Temp local lives from its StorageLive to its StorageDead, a value whose Type may
// need drop (MirTypes.MayNeedDrop) is dropped first, and a defer runs where its scope is left. Each open scope keeps its registrations in
// order: a block its locals and defers, a statement or a condition its temporaries (a guard candidate also its EndBorrow), the body its
// parameters, a destructor's self as its layer's parts (SPEC 16.3.2). A local's StorageDead registers where its storage begins; a
// binding's Drop with it, in declaration order (SPEC 16.2.1), a temp's where its value is created (Hold, Made), so temporaries are
// destroyed in reverse creation order (SPEC 3.6, DECISIONS R4-M129). The normal end of a scope cleans them up in reverse order; a
// transfer leaves through the drop tree of its target, whose node for a scope's first registrations and the rest of the tree is built
// once and shared by every exit that reaches it (rustc's drop tree), a defer body included.
internal sealed partial class MirBuilder
{
    private readonly List<DropScope> scopes = []; // The open scopes, innermost last.
    private readonly List<Registration> registrations = []; // Every registration of the body, each scope's linked from its last.
    private readonly Dictionary<(int Scope, int Count, int Next), int> trees = new(); // The node of a scope's first Count registrations, then Next.
    private readonly List<int> pending = []; // The registrations a drop tree is building nodes for, a stack nested builds share.
    private int temporaries; // The innermost scope of temporaries: a statement's, a condition's or the body value's.
    private int lexical; // The innermost block's scope, or the body's.
    private int scopeIds;

    private enum Cleanup : byte
    {
        StorageDead,
        Drop,
        Fields,
        Defer,
        EndBorrow,
    }

    // Opens a scope of a block's locals and defers, or of temporaries.
    private int OpenScope(bool block)
    {
        this.scopes.Add(new(++this.scopeIds, -1, 0, this.temporaries, this.lexical));
        if (block)
        {
            this.lexical = this.scopes.Count - 1;
        }
        else
        {
            this.temporaries = this.scopes.Count - 1;
        }

        return this.scopes.Count - 1;
    }

    // Closes the innermost scope, cleaning its registrations up in reverse order from the current block, unless its owner cleans them up
    // where it continues (a test's bool).
    private void CloseScope(int index, bool clean = true)
    {
        var scope = this.scopes[index];
        for (var at = clean ? scope.Last : -1; at >= 0; at = this.registrations[at].Prev)
        {
            this.Clean(this.registrations[at]);
        }

        (this.temporaries, this.lexical) = (scope.Temporaries, scope.Lexical);
        this.scopes.RemoveAt(index);
    }

    // A cleanup the scope at an index runs when it is left.
    private void Register(Cleanup kind, int local, Koto? defer, int index)
    {
        ref var scope = ref CollectionsMarshal.AsSpan(this.scopes)[index];
        this.registrations.Add(new(kind, local, defer, this.scope, scope.Last));
        (scope.Last, scope.Count) = (this.registrations.Count - 1, scope.Count + 1);
    }

    // A Var local whose storage begins here and ends with the scope at an index, which drops its value first where it may need drop: a
    // binding's cleanup is ordered by its declaration, which a later initialization does not move (SPEC 16.2.1).
    private int Live(int local, int index)
    {
        this.Allocated(local, index);
        this.Created(local, index);
        return local;
    }

    // SPEC 3.6, 16.2.1, 7.2.3: a temp of the innermost scope of temporaries whose value its caller builds after it: its storage begins here,
    // and Made, once the value is complete, registers its Drop after the temporaries created while it was evaluated, so they are destroyed
    // after it; an exit that interrupts the build only ends its storage. Temp holds and makes a temp written from operands evaluated before.
    private MirPlace Hold(BoundType type, Koto node, MirLocalRole role = MirLocalRole.None, string? name = null)
        => new(this.Allocated(this.body.AddLocal(new(type, MirLocalKind.Temp, this.Info(node, local: true), Name: name, Role: role)), this.temporaries));

    // A held temp's value complete (Hold), in the scope of temporaries it was held in.
    private MirPlace Made(MirPlace temp)
    {
        this.Created(temp.Root, this.temporaries);
        return temp;
    }

    // A local's storage allocated here, ending with the scope at an index.
    private int Allocated(int local, int index)
    {
        this.body.Push(this.current, MirStatement.Of(MirStatementKind.StorageLive, new(local), this.body.Locals[local].Source));
        this.Register(Cleanup.StorageDead, local, null, index);
        return local;
    }

    // A local's value created, dropped with the scope at an index, before what the scope registered earlier, where its Type may need drop.
    private void Created(int local, int index)
    {
        if (MirTypes.MayNeedDrop(this.body.Locals[local].Type, this.walked))
        {
            this.Register(Cleanup.Drop, local, null, index);
        }
    }

    // The entry of the drop tree from the open scopes above a depth to a target: each scope's registrations in reverse, the innermost scope
    // first (plan 5.6.4). The node of a scope's registrations before the same rest is built once and shared: a later exit sees as many
    // registrations or more, so it builds the ones registered since the last node and goes on to that node.
    private int Exit(int depth, int target)
    {
        var current = this.current;
        for (var index = depth; index < this.scopes.Count; index++)
        {
            var scope = this.scopes[index];
            var (mark, count, at, next) = (this.pending.Count, scope.Count, scope.Last, target);
            while (count > 0 && !this.trees.TryGetValue((scope.Id, count, target), out next))
            {
                this.pending.Add(at);
                (at, count) = (this.registrations[at].Prev, count - 1);
            }

            next = count == 0 ? target : next;
            if (this.pending.Count > mark)
            {
                var node = this.current = this.body.AddBlock();
                this.trees.Add((scope.Id, scope.Count, target), node);
                for (var i = mark; i < this.pending.Count; i++)
                {
                    this.Clean(this.registrations[this.pending[i]]);
                }

                this.body.Terminate(this.current, MirTerminator.Goto(next, this.entry));
                (next, this.current) = (node, current);
                this.pending.RemoveRange(mark, this.pending.Count - mark);
            }

            target = next;
        }

        return target;
    }

    // One registration's cleanup from the current block; building goes on in it, or after a Drop or a defer in the block after it.
    private void Clean(in Registration registration)
    {
        var source = registration.Local < 0 ? default : this.body.Locals[registration.Local].Source;
        switch (registration.Kind)
        {
            case Cleanup.StorageDead:
                this.body.Push(this.current, MirStatement.Of(MirStatementKind.StorageDead, new(registration.Local), source));
                break;
            case Cleanup.EndBorrow:
                this.body.Push(this.current, MirStatement.Of(MirStatementKind.EndBorrow, new(registration.Local), source));
                break;
            case Cleanup.Drop:
                this.DropAt(new(registration.Local), source);
                break;
            case Cleanup.Fields:
                this.FieldCleanup(registration.Local, source);
                break;
            default:
                this.Deferred(registration);
                break;
        }
    }

    // A Drop of a Place from the current block (SPEC 16.3); building goes on in the block after it.
    private void DropAt(MirPlace place, in SourceInfo source)
    {
        var next = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Drop(place, next, source));
        this.current = next;
    }

    // SPEC 16.3.1-16.3.2: a destructor's self is no owner, and its body's end destroys the layer's parts, never the layer: each own Field
    // whose Type may need drop, last to first, then the direct base, each by its own complete destruction (A2 skips a moved one).
    private void FieldCleanup(int local, in SourceInfo source)
    {
        var type = this.body.Locals[local].Type;
        for (var i = AdtDef.Count(type) - 1; i >= 0; i--)
        {
            if (MirTypes.MayNeedDrop(AdtDef.FieldType(type, i), this.walked))
            {
                this.DropAt(this.body.Project(new(local), MirProjectionKind.Field, i), source);
            }
        }

        if (MirTypes.MayNeedDrop(AdtDef.Base(type), this.walked))
        {
            this.DropAt(this.body.Project(new(local), MirProjectionKind.Base, 1), source);
        }
    }

    // SPEC 16.1: a defer body where its scope is left, built once per target of the drop tree it is in (plan 5.6.4), in the scope that
    // registered it; an exit to it goes on with the cleanup after it, and a body that does not complete goes on there for the checks only.
    private void Deferred(in Registration registration)
    {
        var scope = this.scope;
        var start = this.statementStart;
        var defer = (DeferredBlockKoto)registration.Defer!;
        var next = this.body.AddBlock();
        (this.scope, this.statementStart) = (registration.Scope, false);
        var depth = this.scopes.Count;
        var temporaries = this.OpenScope(block: false);
        var result = this.Hold(BoundType.Unit, defer);
        this.targets.Add(new(defer, result, next, -1, new(this.arguments.Count, depth, this.scope), Reached: false));
        this.Block(result, defer.Body, MirScopeKind.Deferred);
        this.Made(result);
        this.targets.RemoveAt(this.targets.Count - 1);
        this.CloseScope(temporaries);
        this.Join(next, defer.Body);
        (this.scope, this.statementStart, this.current) = (scope, start, next);
    }

    // SPEC 14.2.3: building goes on in a test's successor, where the tested bool's storage ends.
    private void Enter(int block, MirPlace test)
    {
        this.current = block;
        this.body.Push(block, MirStatement.Of(MirStatementKind.StorageDead, test, this.body.Locals[test.Root].Source));
    }

    // An open scope: its id, its last registration and their count, and the innermost scopes of temporaries and of a block around it.
    private record struct DropScope(int Id, int Last, int Count, int Temporaries, int Lexical);

    // A cleanup: its kind, the local it ends or drops or the defer it runs, the scope it was registered in, and its scope's previous one.
    private readonly record struct Registration(Cleanup Kind, int Local, Koto? Defer, ushort Scope, int Prev);
}
