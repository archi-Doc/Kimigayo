// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The DictionaryStorage symbol view for the Backend (plan F2c; DECISIONS R5-M28, R5-M124): the symbol of each DictionaryStorage function a
// legacy Dictionary row or the Dictionary runtime declares as its callee (X4), KimiLibrary's view, which TyCtxt forwards unchanged. Its one
// reader is the instance collector, which admits the hidden instances its legacy rows declare (append, initialize, clearEntries). R8
// deletes it with KimiLibrary.DictionaryStorage.cs.
internal sealed partial class TyCtxt
{
    /// <summary>Gets the symbol of a DictionaryStorage function: exactly KimiLibrary's view (<see cref="KimiLibrary.DictionaryStorage"/>),
    /// whose None is a broken contract.</summary>
    /// <param name="callee">The function.</param>
    /// <returns>The one symbol Binding declares for it, which names its definition the way MirDefKey does.</returns>
    [Transitional("R8", Reason = KimiLibrary.DictionaryStorageRemoval)]
    internal BindingSymbol DictionaryStorage(DictionaryCallee callee) => library.DictionaryStorage(callee);
}
