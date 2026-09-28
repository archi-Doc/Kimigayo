// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Unit;

public class KimiUnit : UnitBase
{
    public static void ConfigureBase(IUnitConfigurationContext context)
    {
        context.AddScoped<IConsoleService, ConsoleService>();
        context.AddSingleton<Kimigayo>();
        context.AddTransient<Solution>();
        context.AddTransient<Project>();
    }

    public class Builder : UnitBuilder
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
                ConfigureBase(context);

                // Logger
                /*context.ClearLogOutputResolvers();
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

                        x.SetOutput<ConsoleAndFileLogOutput>();
                    });
                }*/
            });

            /*this.PostConfigure(context =>
            {
                var logfile = "Logs/Log.txt";
                context.SetOptions(context.GetOrCreateOptions<FileLogOutputOptions>() with
                {
                    FilePath = Path.Combine(context.DataDirectory, logfile),
                    MaxLogCapacityInMegabytes = 2,
                    ClearLogsAtStartup = false,
                });
            });*/
        }
    }

    public KimiUnit(UnitContext context)
        : base(context)
    {
    }
}
