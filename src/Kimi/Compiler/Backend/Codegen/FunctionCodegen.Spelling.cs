// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The text's spelling of FunctionCodegen (R5 package C 3.2 I1): a value of the text and its name (Value), a local's or literal's spelling
// (Spell, LlvmScalarText's operand precondition), a named one's (Spelled: a pooled constant, a parameter, an item's global, a local's
// home), a value and a branch target written into the function's text (WriteValue, WriteLabel), and a check's failure operands spelled
// for LlvmScalarText's failure block (Failure), which writes the branch and the Abort. Spelling writes nothing but names and allocates
// nothing; a zero-sized home's spelling requests the module's zero-sized symbol (IMPL 21.1.3).
internal sealed partial class FunctionCodegen
{
    // The longest spelling of an operand, a label or a location operand: a two-character prefix and an Int128, or @ and a constant's name.
    private const int SpellingLength = 48;

    // A value's spelling (LlvmScalarText's operand precondition): a prefixed name such as %t<n>, a floating constant's hexadecimal double
    // or an integer's signed decimal.
    private static ReadOnlySpan<char> Spell(Value value, Span<char> text)
    {
        if (value.Prefix == 'n')
        {
            "null".CopyTo(text);
            return text[..4];
        }

        if (value.Prefix == 'x')
        {
            "0x".CopyTo(text);
            unchecked((ulong)value.Number).TryFormat(text[2..], out var hex, "X16", CultureInfo.InvariantCulture);
            return text[..(2 + hex)];
        }

        var start = 0;
        if (value.Prefix != '\0')
        {
            text[0] = '%';
            text[1] = value.Prefix;
            start = 2;
        }

        value.Number.TryFormat(text[start..], out var digits, default, CultureInfo.InvariantCulture);
        return text[..(start + digits)];
    }

    // The global of an item a Value prefix names, the prefix and suffix of its name around the item's index: an adapter item's table
    // @__kimi_erase<item>_table (ModuleCodegen.AdapterPrefix), a StaticInit item's slot @__kimi_static<item> and its state
    // @__kimi_static<item>_state (ModuleCodegen.StaticPrefix), a body item's function @__kimi_f<item> (ModuleCodegen.FunctionPrefix, a user
    // Writer's dispatch word); none for another prefix.
    private static (string Prefix, string Suffix)? ItemGlobal(char prefix) => prefix switch
    {
        'g' => (ModuleCodegen.AdapterPrefix, ModuleCodegen.TableSuffix),
        'v' => (ModuleCodegen.StaticPrefix, string.Empty),
        'w' => (ModuleCodegen.StaticPrefix, ModuleCodegen.StateSuffix),
        'e' => (ModuleCodegen.FunctionPrefix, string.Empty),
        _ => null,
    };

    // The failure of a check named by the number id (abort<id>, the success label c<id>), which Aborts at its site's location (SPEC 22.5.1,
    // Location), spelled into text (3 * SpellingLength characters).
    private ScalarCheck Failure(int id, Site site, Span<char> text) => new(
        id,
        Spell(new('c', id), text[..SpellingLength]),
        this.Spelled(site.Text, text.Slice(SpellingLength, SpellingLength)),
        this.Spelled(site.Length, text[(2 * SpellingLength)..]));

    // A branch target: the label of a block, b<index> (I1).
    private void WriteLabel(int block)
    {
        this.output.Write("label %b");
        WriteNumber(this.output, block);
    }

    private void WriteValue(Value value) => this.output.Write(this.Spelled(value, stackalloc char[SpellingLength]));

    // A value's spelling into text (SpellingLength characters): a pooled constant's @<name>, a parameter's %<name>, its signature's, an
    // item's global (ItemGlobal), a local's home (SpellHome), or Spell's.
    private ReadOnlySpan<char> Spelled(Value value, Span<char> text)
    {
        if (value.Prefix == 'h')
        {
            return this.SpellHome((int)value.Number, text);
        }

        if (ItemGlobal(value.Prefix) is var (prefix, suffix))
        {
            text[0] = '@';
            prefix.CopyTo(text[1..]);
            var at = 1 + prefix.Length;
            value.Number.TryFormat(text[at..], out var digits, default, CultureInfo.InvariantCulture);
            suffix.CopyTo(text[(at + digits)..]);
            return text[..(at + digits + suffix.Length)];
        }

        if (value.Prefix is not ('@' or 'p'))
        {
            return Spell(value, text);
        }

        var name = value.Prefix == '@' ? this.module.Constants[(int)value.Number].Name : this.abi.Parameters[(int)value.Number].Name;
        text[0] = value.Prefix == '@' ? '@' : '%';
        name.CopyTo(text[1..]);
        return text[..(name.Length + 1)];
    }

    // A local's home spelled into text (I2): its alloca %l<local> (a Region's too), the parameter its caller passed, or the module's
    // zero-sized symbol, which ModuleCodegen defines on request.
    private ReadOnlySpan<char> SpellHome(int local, Span<char> text)
    {
        var slot = this.slots[local];
        switch (slot.Home)
        {
            case Home.Slot or Home.Region:
                "%l".CopyTo(text);
                local.TryFormat(text[2..], out var digits, default, CultureInfo.InvariantCulture);
                return text[..(2 + digits)];
            case Home.Parameter:
                return this.Spelled(new('p', slot.Index), text);
            case Home.None:
                this.module.Request(ModuleCodegen.Needs.ZeroSized);
                text[0] = '@';
                ModuleCodegen.ZeroSized.CopyTo(text[1..]);
                return text[..(ModuleCodegen.ZeroSized.Length + 1)];
            default:
                throw new InvalidOperationException($"The home {slot.Home} has no spelling.");
        }
    }

    // A value of the text: %t<n> (Prefix t), the parameter Number of the function's signature, %<its name> (Prefix p; Abi.Of names them),
    // the copy slot %s<n> (Prefix s; never c, a check's success label), the drop flag's slot %f<n> (Prefix f, FunctionCodegen.Flags), the
    // bytes %r<n> of local Number's Region home (Prefix r, FunctionCodegen.Frame), the
    // pooled constant @<name> of index Number (Prefix @), the table of adapter item Number (Prefix g), the slot of StaticInit item Number
    // (Prefix v) and its state (Prefix w), the function of body item Number (Prefix e), the home of local Number, its address (Prefix h),
    // the floating constant whose double bits are Number (Prefix x, LLVM's hexadecimal spelling), the null pointer (Prefix n) or the
    // literal integer Number (Prefix NUL).
    private readonly record struct Value(char Prefix, Int128 Number);
}
