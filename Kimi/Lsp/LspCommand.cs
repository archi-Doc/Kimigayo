// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Threading;
using Arc.Unit;
using SimpleCommandLine;

namespace StandardConsole;

[SimpleCommand(LspCommand.Name)]
public class LspCommand : ISimpleCommand<LspCommand.Options>
{
    public const string Name = "lsp";

    public class Options
    {
        [SimpleOption("debugwait")]
        public bool DebugWait { get; set; } = false;
    }

    private readonly ILogger logger;

    public LspCommand(ILogger<LspCommand> logger)
    {
        this.logger = logger;
    }

    public async Task Execute(LspCommand.Options option, string[] args, CancellationToken cancellationToken)
    {
        if (option.DebugWait)
        {
            try
            {
                while (!Debugger.IsAttached)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        await LspServer.RunAsync().ConfigureAwait(false);
    }
}
