// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

public class KimiUnit : UnitBase
{
    public static void ConfigureBase(IUnitConfigurationContext context)
    {
        context.AddScoped<IConsoleService, ConsoleService>();
        context.AddSingleton<KimiControl>();
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
                /*context.ClearLoggerResolver();
                if (Program.SuppressConsoleOutput)
                {
                    context.AddLoggerResolver(x =>
                    {
                        x.SetOutput<FileLogger<FileLoggerOptions>>();
                    });
                }
                else
                {
                    context.AddLoggerResolver(x =>
                    {// Log source/level -> Resolver() -> Output/filter
                        if (x.LogLevel <= LogLevel.Debug)
                        {
                            x.SetOutput<ConsoleLogger>();
                            return;
                        }

                        x.SetOutput<ConsoleAndFileLogger>();
                    });
                }*/
            });

            /*this.PostConfigure(context =>
            {
                var logfile = "Logs/Log.txt";
                context.SetOptions(context.GetOptions<FileLoggerOptions>() with
                {
                    Path = Path.Combine(context.DataDirectory, logfile),
                    MaxLogCapacity = 2,
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
