// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The place writer of FunctionCodegen (R5 package C 3.2, plan C2a): a Place's address and the loads and stores of a local's home.
internal sealed partial class FunctionCodegen
{
    // The place writer: a Place's address. A local without projection that has storage is its home; projections are C2a's rows, a static's
    // root C7b's, and a zero-sized local's address (the zero-sized address symbol) C2b's.
    private bool Address(MirPlace place, out Value address)
    {
        var known = IsLocal(place) && this.slots[place.Root].Home != Home.None;
        address = known ? new('h', place.Root) : default;
        return known;
    }

    // A local's value, loaded from its home in its storage representation (I4).
    private Value Load(int local)
    {
        var value = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(value);
        this.output.Write(" = load ");
        this.output.Write(this.slots[local].Value.Layout.StorageType);
        this.WriteAccess(local);
        return value;
    }

    // A value in a local's storage representation, stored into its home.
    private void Store(int local, Value value)
    {
        this.output.Write("  store ");
        this.output.Write(this.slots[local].Value.Layout.StorageType);
        this.output.Write(' ');
        this.WriteValue(value);
        this.WriteAccess(local);
    }

    // The end of a load or store of a local: its home and its Layout's alignment.
    private void WriteAccess(int local)
    {
        this.output.Write(", ptr ");
        this.WriteHome(local);
        this.output.Write(", align ");
        WriteNumber(this.output, this.slots[local].Value.Layout.Alignment);
        this.output.Write('\n');
    }

    private void WriteHome(int local)
    {
        var slot = this.slots[local];
        switch (slot.Home)
        {
            case Home.Slot:
                this.output.Write("%l");
                WriteNumber(this.output, local);
                return;
            case Home.Argument:
                this.output.Write("%a");
                WriteNumber(this.output, slot.Index);
                return;
            case Home.Result:
                this.output.Write("%ret");
                return;
            default:
                throw new InvalidOperationException("A zero-sized local has no home.");
        }
    }
}
