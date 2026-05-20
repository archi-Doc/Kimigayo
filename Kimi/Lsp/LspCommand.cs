// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using SimpleCommandLine;

namespace Kimigayo.Lsp;

[SimpleCommand(LspCommand.Name)]
public class LspCommand : ISimpleCommand<LspCommand.Options>
{
    public const string Name = "lsp";
    private const int DebuggerWaitSeconds = 20;

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
                var time = DateTime.UtcNow;
                while (!Debugger.IsAttached)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    if (DateTime.UtcNow - time > TimeSpan.FromSeconds(DebuggerWaitSeconds))
                    {
                        break;
                    }
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
