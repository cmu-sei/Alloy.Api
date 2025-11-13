// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Alloy.Api.Infrastructure.OperationFilters;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Services;
using Caster.Api.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Player.Api.Client;
using Steamfitter.Api.Client;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddSwagger(this IServiceCollection services, AuthorizationOptions authOptions)
        {
            // XML Comments path
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string commentsFileName = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
            string commentsFile = Path.Combine(baseDirectory, commentsFileName);

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Alloy API", Version = "v1" });

                c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri(authOptions.AuthorizationUrl),
                            TokenUrl = new Uri(authOptions.TokenUrl),
                            Scopes = new Dictionary<string, string>()
                            {
                                {authOptions.AuthorizationScope, "public api access"}
                            }
                        }
                    }
                });
                c.AddSecurityRequirement((document) => new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("oauth2", document), [authOptions.AuthorizationScope] }
                });
                c.IncludeXmlComments(commentsFile);
                c.EnableAnnotations();
                c.OperationFilter<DefaultResponseOperationFilter>();
                c.MapType<Optional<Guid?>>(() => new OpenApiSchema
                {
                    OneOf = new List<IOpenApiSchema>
                    {
                        new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                        new OpenApiSchema { Type = JsonSchemaType.Null }
                    }
                });

                c.MapType<JsonElement?>(() => new OpenApiSchema
                {
                    OneOf = new List<IOpenApiSchema>
                    {
                        new OpenApiSchema { Type = JsonSchemaType.Object },
                        new OpenApiSchema { Type = JsonSchemaType.Null }
                    }
                });
            });
        }

        public static void AddPlayerApiClient(this IServiceCollection services)
        {
            services.AddScoped<IPlayerApiClient, PlayerApiClient>(p =>
            {
                var httpContextAccessor = p.GetRequiredService<IHttpContextAccessor>();
                var httpClientFactory = p.GetRequiredService<IHttpClientFactory>();
                var clientOptions = p.GetRequiredService<ClientOptions>();

                var playerUri = new Uri(clientOptions.urls.playerApi);

                string authHeader = httpContextAccessor.HttpContext.Request.Headers["Authorization"];

                if (authHeader == null)
                {
                    var token = httpContextAccessor.HttpContext.Request.Query["access_token"];
                    authHeader = new AuthenticationHeaderValue("Bearer", token).ToString();
                }

                var httpClient = httpClientFactory.CreateClient();
                httpClient.BaseAddress = playerUri;
                httpClient.DefaultRequestHeaders.Add("Authorization", authHeader);

                var apiClient = new PlayerApiClient(httpClient);
                return apiClient;
            });
        }

        public static void AddCasterApiClient(this IServiceCollection services)
        {
            services.AddScoped<ICasterApiClient, CasterApiClient>(p =>
            {
                var httpContextAccessor = p.GetRequiredService<IHttpContextAccessor>();
                var httpClientFactory = p.GetRequiredService<IHttpClientFactory>();
                var clientOptions = p.GetRequiredService<ClientOptions>();

                var casterUri = new Uri(clientOptions.urls.casterApi);

                string authHeader = httpContextAccessor.HttpContext.Request.Headers["Authorization"];

                var httpClient = httpClientFactory.CreateClient();
                httpClient.BaseAddress = casterUri;
                httpClient.DefaultRequestHeaders.Add("Authorization", authHeader);

                var apiClient = new CasterApiClient(httpClient);
                return apiClient;
            });
        }

        public static void AddSteamfitterApiClient(this IServiceCollection services)
        {
            services.AddScoped<ISteamfitterApiClient, SteamfitterApiClient>(p =>
            {
                var httpContextAccessor = p.GetRequiredService<IHttpContextAccessor>();
                var httpClientFactory = p.GetRequiredService<IHttpClientFactory>();
                var clientOptions = p.GetRequiredService<ClientOptions>();

                var steamfitterUri = new Uri(clientOptions.urls.steamfitterApi);

                string authHeader = httpContextAccessor.HttpContext.Request.Headers["Authorization"];

                var httpClient = httpClientFactory.CreateClient();
                httpClient.BaseAddress = steamfitterUri;
                httpClient.DefaultRequestHeaders.Add("Authorization", authHeader);

                var apiClient = new SteamfitterApiClient(httpClient);
                return apiClient;
            });
        }

        public static void AddAlloyBackgroundService(this IServiceCollection services)
        {
            services.AddSingleton<IAlloyEventQueue, AlloyEventQueue>();
            services.AddHostedService<AlloyQueryService>();
            services.AddHostedService<AlloyBackgroundService>();
        }
    }
}
