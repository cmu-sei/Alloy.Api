// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Principal;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Hubs;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.DbInterceptors;
using Alloy.Api.Infrastructure.Filters;
using Alloy.Api.Infrastructure.Identity;
using Alloy.Api.Infrastructure.JsonConverters;
using Alloy.Api.Infrastructure.Mappings;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Services;
using AutoMapper.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace Alloy.Api
{
    public class Startup
    {
        public Infrastructure.Options.AuthorizationOptions _authOptions = new();
        private readonly SignalROptions _signalROptions = new();
        private const string _routePrefix = "api";
        private string _pathbase;

        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            Configuration.GetSection("Authorization").Bind(_authOptions);
            Configuration.GetSection("SignalR").Bind(_signalROptions);
            _pathbase = Configuration["PathBase"] ?? "";
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var provider = Configuration["Database:Provider"];
            switch (provider)
            {
                case "InMemory":
                    services.AddPooledDbContextFactory<AlloyContext>((ServiceProvider, builder) => builder
                        .AddInterceptors(ServiceProvider.GetRequiredService<EventInterceptor>())
                        .UseInMemoryDatabase("api"));
                    break;
                case "Sqlite":
                case "SqlServer":
                case "PostgreSQL":
                    services.AddPooledDbContextFactory<AlloyContext>((serviceProvider, builder) => builder
                        .AddInterceptors(serviceProvider.GetRequiredService<EventInterceptor>())
                        .UseConfiguredDatabase(Configuration));
                    break;
            }

            services.AddScoped<AlloyContextFactory>();
            services.AddScoped(sp => sp.GetRequiredService<AlloyContextFactory>().CreateDbContext());

            services.AddSingleton<StartupHealthCheck>();
            services.AddSingleton<HostedServiceHealthCheck>();
            services.AddHealthChecks()
                .AddCheck<StartupHealthCheck>(
                    "startup",
                    failureStatus: HealthStatus.Degraded,
                    tags: new[] { "ready" })
                .AddCheck<HostedServiceHealthCheck>(
                    "service_responsive",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { "live" });

            var connectionString = Configuration.GetConnectionString(DatabaseExtensions.DbProvider(Configuration));
            switch (provider)
            {
                case "Sqlite":
                    services.AddHealthChecks().AddSqlite(connectionString, tags: new[] { "ready", "live" });
                    break;
                case "SqlServer":
                    services.AddHealthChecks().AddSqlServer(connectionString, tags: new[] { "ready", "live" });
                    break;
                case "PostgreSQL":
                    services.AddHealthChecks().AddNpgSql(connectionString, tags: new[] { "ready", "live" });
                    break;
            }

            services.AddOptions()
                .Configure<DatabaseOptions>(Configuration.GetSection("Database"))
                    .AddScoped(config => config.GetService<IOptionsMonitor<DatabaseOptions>>().CurrentValue)

                .Configure<ClaimsTransformationOptions>(Configuration.GetSection("ClaimsTransformation"))
                    .AddScoped(config => config.GetService<IOptionsMonitor<ClaimsTransformationOptions>>().CurrentValue);

            services
                .Configure<ClientOptions>(Configuration.GetSection("ClientSettings"))
                .AddScoped(config => config.GetService<IOptionsMonitor<ClientOptions>>().CurrentValue);

            services
                .Configure<FilesOptions>(Configuration.GetSection("Files"))
                .AddScoped(config => config.GetService<IOptionsMonitor<FilesOptions>>().CurrentValue);

            services
                .Configure<ResourceOwnerAuthorizationOptions>(Configuration.GetSection("ResourceOwnerAuthorization"))
                .AddScoped(config => config.GetService<IOptionsMonitor<ResourceOwnerAuthorizationOptions>>().CurrentValue);

            services
                .Configure<ResourceOptions>(Configuration.GetSection("Resource"))
                .AddScoped(config => config.GetService<IOptionsMonitor<ResourceOptions>>().CurrentValue);

            services.AddCors(options => options.UseConfiguredCors(Configuration.GetSection("CorsPolicy")));

            services.AddSignalR(o => o.StatefulReconnectBufferSize = _signalROptions.StatefulReconnectBufferSizeBytes)
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.PayloadSerializerOptions.Converters.Add(new JsonDateTimeConverter());
            });

            services.AddMvc(options =>
            {
                options.Filters.Add(typeof(ValidateModelStateFilter));
                options.Filters.Add(typeof(JsonExceptionFilter));

                // Require all scopes in authOptions
                var policyBuilder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
                Array.ForEach(_authOptions.AuthorizationScope.Split(' '), x => policyBuilder.RequireScope(x));

                var policy = policyBuilder.Build();
                options.Filters.Add(new AuthorizeFilter(policy));
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonNullableGuidConverter());
                // options.JsonSerializerOptions.Converters.Add(new JsonIntegerConverter());
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.Converters.Add(new JsonDateTimeConverter());

            });

            services.AddSwagger(_authOptions);

            JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = _authOptions.Authority;
                options.RequireHttpsMetadata = _authOptions.RequireHttpsMetadata;
                options.SaveToken = true;

                string[] validAudiences;

                if (_authOptions.ValidAudiences != null && _authOptions.ValidAudiences.Any())
                {
                    validAudiences = _authOptions.ValidAudiences;
                }
                else
                {
                    validAudiences = _authOptions.AuthorizationScope.Split(' ');
                }

                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateAudience = _authOptions.ValidateAudience,
                    ValidAudiences = validAudiences
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // If the request is for our hub...
                        var path = context.HttpContext.Request.Path;
                        var accessToken = context.Request.Query["access_token"];

                        if (!string.IsNullOrEmpty(accessToken) &&
                                (path.StartsWithSegments("/hubs")))
                        {
                            // Read the token out of the query string
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

            services.AddRouting(options =>
            {
                options.LowercaseUrls = true;
            });

            services.AddMemoryCache();

            services.AddScoped<IEventTemplateService, EventTemplateService>();
            services.AddScoped<IEventTemplateMembershipService, EventTemplateMembershipService>();
            services.AddScoped<IEventTemplateRoleService, EventTemplateRoleService>();
            services.AddScoped<IEventService, EventService>();
            services.AddScoped<IEventMembershipService, EventMembershipService>();
            services.AddScoped<IEventRoleService, EventRoleService>();
            services.AddScoped<ICasterService, CasterService>();
            services.AddScoped<IPlayerService, PlayerService>();
            services.AddScoped<ISteamfitterService, SteamfitterService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IUserClaimsService, UserClaimsService>();
            services.AddScoped<IClaimsTransformation, AuthorizationClaimsTransformer>();
            services.AddTransient<EventInterceptor>();
            services.AddScoped<IAlloyAuthorizationService, AuthorizationService>();
            services.AddScoped<IIdentityResolver, IdentityResolver>();
            services.AddScoped<IGroupService, GroupService>();
            services.AddScoped<ISystemRoleService, SystemRoleService>();

            // add the other API clients
            services.AddPlayerApiClient();
            services.AddCasterApiClient();
            services.AddSteamfitterApiClient();

            // add the background IHostedServices
            services.AddAlloyBackgroundService();

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IPrincipal>(p => p.GetService<IHttpContextAccessor>().HttpContext.User);

            services.AddHttpClient();

            ApplyPolicies(services);

            services.AddAutoMapper(cfg =>
            {
                cfg.Internal().ForAllPropertyMaps(
                    pm => pm.SourceType != null && Nullable.GetUnderlyingType(pm.SourceType) == pm.DestinationType,
                    (pm, c) => c.MapFrom<object, object, object, object>(new IgnoreNullSourceValues(), pm.SourceMember.Name));


            }, typeof(Startup));

            services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Startup>());
            services.AddScoped<IClaimsTransformation, AuthorizationClaimsTransformer>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UsePathBase(_pathbase);

            app.UseRouting();
            app.UseCors("default");

            //move any querystring jwt to Auth bearer header
            app.Use(async (context, next) =>
            {
                if (string.IsNullOrWhiteSpace(context.Request.Headers["Authorization"])
                    && context.Request.QueryString.HasValue)
                {
                    string token = context.Request.QueryString.Value
                        .Substring(1)
                        .Split('&')
                        .SingleOrDefault(x => x.StartsWith("bearer="))?.Split('=')[1];

                    if (!String.IsNullOrWhiteSpace(token))
                        context.Request.Headers.Append("Authorization", new[] { $"Bearer {token}" });
                }

                await next.Invoke();

            });

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = _routePrefix;
                c.SwaggerEndpoint($"{_pathbase}/swagger/v1/swagger.json", "Alloy v1");
                c.OAuthClientId(_authOptions.ClientId);
                c.OAuthClientSecret(_authOptions.ClientSecret);
                c.OAuthAppName(_authOptions.ClientName);
                c.OAuthUsePkce();
            });

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks($"/{_routePrefix}/health/ready", new HealthCheckOptions()
                {
                    Predicate = (check) => check.Tags.Contains("ready"),
                });

                endpoints.MapHealthChecks($"/{_routePrefix}/health/live", new HealthCheckOptions()
                {
                    Predicate = (check) => check.Tags.Contains("live"),
                });
                endpoints.MapHub<EngineHub>("/hubs/engine", options =>
                    {
                        options.AllowStatefulReconnects = _signalROptions.EnableStatefulReconnect;
                    }
                );
            });
        }


        private void ApplyPolicies(IServiceCollection services)
        {
            services.AddAuthorizationPolicy(_authOptions);
        }
    }
}
