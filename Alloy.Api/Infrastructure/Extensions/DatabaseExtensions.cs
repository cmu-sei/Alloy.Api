// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Data;
using Microsoft.Extensions.Hosting;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class DatabaseExtensions
    {
        public static IHost InitializeDatabase(this IHost webHost)
        {
            using (var scope = webHost.Services.CreateScope())
            {
                var services = scope.ServiceProvider;

                try
                {
                    var seedDataOptions = services.GetService<SeedDataOptions>();
                    var databaseOptions = services.GetService<DatabaseOptions>();
                    var ctx = services.GetRequiredService<AlloyContext>();

                    if (ctx != null)
                    {
                        if (databaseOptions.DevModeRecreate)
                            ctx.Database.EnsureDeleted();

                        // Do not run migrations on Sqlite, only devModeRecreate allowed
                        if (!ctx.Database.IsSqlite())
                        {
                            ctx.Database.Migrate();
                        }

                        if (databaseOptions.DevModeRecreate)
                        {
                            ctx.Database.EnsureCreated();
                            ProcessSeedDataOptions(seedDataOptions, ctx);

                            if (!ctx.EventTemplates.Any())
                            {
                                Seed.Run(ctx);
                            }
                        }
                        else
                        {
                            ProcessSeedDataOptions(seedDataOptions, ctx);
                        }
                    }

                }
                catch (Exception ex)
                {
                    var logger = services.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "An error occurred while initializing the database.");

                    // exit on database connection error on startup so app can be restarted to try again
                    throw;
                }
            }

            return webHost;
        }

        public static string DbProvider(IConfiguration config)
        {
            return config.GetValue<string>("Database:Provider", "Sqlite").Trim();
        }

        public static DbContextOptionsBuilder UseConfiguredDatabase(
            this DbContextOptionsBuilder builder,
            IConfiguration config
        )
        {
            string dbProvider = DbProvider(config);
            var migrationsAssembly = String.Format("{0}.Migrations.{1}", typeof(Startup).GetTypeInfo().Assembly.GetName().Name, dbProvider);
            var connectionString = config.GetConnectionString(dbProvider);

            switch (dbProvider)
            {
                case "Sqlite":
                    builder.UseSqlite(connectionString, options => options.MigrationsAssembly(migrationsAssembly));
                    break;

                case "PostgreSQL":
                    builder.UseNpgsql(connectionString, options => options.MigrationsAssembly(migrationsAssembly));
                    break;

            }
            return builder;
        }

        private static void ProcessSeedDataOptions(SeedDataOptions options, AlloyContext context)
        {
            if (options.Roles?.Any() == true)
            {
                var dbRoles = context.SystemRoles.ToHashSet();

                foreach (var role in options.Roles)
                {
                    if (!dbRoles.Any(x => x.Name == role.Name))
                    {
                        context.SystemRoles.Add(role);
                    }
                }

                context.SaveChanges();
            }

            if (options.Users?.Any() == true)
            {
                var dbUserIds = context.Users.Select(x => x.Id).ToHashSet();

                foreach (UserEntity user in options.Users)
                {
                    if (!dbUserIds.Contains(user.Id))
                    {
                        if (user.Role?.Id == Guid.Empty && !string.IsNullOrEmpty(user.Role.Name))
                        {
                            var role = context.SystemRoles.FirstOrDefault(x => x.Name == user.Role.Name);
                            if (role != null)
                            {
                                user.RoleId = role.Id;
                                user.Role = role;
                            }
                            else
                            {
                                user.RoleId = null;
                                user.Role = null;
                            }
                        }

                        context.Users.Add(user);
                    }
                }

                context.SaveChanges();
            }

            if (options.Groups?.Any() == true)
            {
                var dbGroup = context.Groups.ToHashSet();

                foreach (var group in options.Groups)
                {
                    if (!dbGroup.Any(x => x.Name == group.Name))
                    {
                        context.Groups.Add(group);
                    }
                }

                context.SaveChanges();
            }
        }

    }
}
