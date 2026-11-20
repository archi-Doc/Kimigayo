// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Command;
using Kimi.Lsp;
using SimpleCommandLine;

namespace Kimi.Unit;

public class CommandUnit : UnitBase, IUnitPreparable, IUnitExecutable
{
    public class Builder : UnitBuilder<Product>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {
            this.PreConfigure(context =>
            {
            });

            // Configuration for Unit.
            this.Configure(context =>
            {
                KimiUnit.ConfigureBase(context);

                context.AddSingleton<CommandUnit>();
                context.RegisterInstanceCreation<CommandUnit>();
                context.AddSingleton<LspServer>();

                // Command
                context.AddCommand<DefaultCommand>();
                context.AddCommand<LspCommand, LspCommand.Options>();
                context.AddCommand<BuildCommand, BuildCommand.Options>();
                context.AddCommand<CheckCommand, KimiOptions>();
                context.AddCommand<RestoreCommand, KimiOptions>();
                context.AddCommand<EmitCommand, KimiOptions>();
                context.AddCommand<RunCommand, RunCommand.Options>();
                context.AddCommand<TestCommand>();
                context.AddCommand<ToolchainCommand, ToolchainCommand.Options>();

                // Logger
                context.ClearLogOutputResolvers();
                if (Program.SuppressConsoleOutput)
                {
                    context.AddLogOutputResolver(x =>
                    {
                        x.SetOutput<FileLogOutput<FileLogOutputOptions>>();
                    });
                }
                else
                {
                    context.AddLogOutputResolver(x =>
                    {// Log source/level -> Resolver() -> Output/filter
                        if (x.LogLevel <= LogLevel.Debug)
                        {
                            x.SetOutput<ConsoleLogOutput>();
                            return;
                        }

                        // x.SetOutput<ConsoleAndFileLogOutput>();
                        x.SetOutput<ConsoleLogOutput>();
                    });
                }
            });

            this.PostConfigure(context =>
            {
                var logfile = "Logs/Log.txt";
                context.SetOptions(context.GetOrCreateOptions<FileLogOutputOptions>() with
                {
                    FilePath = Path.Combine(context.DataDirectory, logfile),
                    MaxLogCapacityInMegabytes = 2,
                    ClearLogsAtStartup = false,
                });
            });
        }
    }

    public class Product : UnitProduct
    {// Unit class for customizing behaviors.
        public record Param(string Args);

        public Product(UnitContext context)
            : base(context)
        {
        }

        public async Task RunAsync(Param param)
        {
            // Create optional instances
            this.Context.CreateInstances();

            await this.Context.SendPrepareAsync();
            await this.Context.SendStartAsync();

            await this.Execute(param.Args, this.Context.ServiceProvider, this.Context.ExecutionRoot.CancellationToken);

            await this.Context.SendStopAsync();
            await this.Context.SendTerminateAsync();
        }

        /// <summary>Parses a command line (the text after <c>kimi </c>) and executes its command.</summary>
        /// <param name="commandLine">The command line.</param>
        /// <param name="serviceProvider">The provider the command is resolved from.</param>
        /// <param name="cancellationToken">The command's cancellation.</param>
        /// <returns>A task that completes when the command has run.</returns>
        internal async Task Execute(string commandLine, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var parserOptions = SimpleParserOptions.Standard with
            {
                ServiceProvider = serviceProvider,
                RequireCommandName = false,
                RejectUnknownOptionNames = false,
                SuppressConsoleOutput = Program.SuppressConsoleOutput,
            };

            // Main
            var arguments = SimpleParserHelper.SplitArguments(commandLine, default);
            if (arguments.Length != 0 && arguments[0] == "test")
            {
                await serviceProvider.GetRequiredService<TestCommand>().Execute(arguments[1..], cancellationToken);
            }
            else
            {
                var parser = this.Context.CreateSimpleParser(parserOptions);
                if (!parser.Parse(KimiOptions.ExpandFlags(commandLine)))
                {
                    Environment.ExitCode = 1;
                }

                await parser.Execute(cancellationToken);
            }
        }
    }

    public CommandUnit(UnitContext context)
        : base(context)
    {
    }

    async Task IUnitPreparable.PrepareAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
    }

    async Task IUnitExecutable.StartAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
    }

    async Task IUnitExecutable.StopAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
    }

    async Task IUnitExecutable.TerminateAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
    }
}
