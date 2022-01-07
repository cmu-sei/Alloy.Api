using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Infrastructure.Options;
using Caster.Api;
using IdentityModel.Client;
// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Player.Api;
using Steamfitter.Api;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class ApiClientsExtensions
    {
        public static HttpClient GetHttpClient(IHttpClientFactory httpClientFactory, string apiUrl, TokenResponse tokenResponse)
        {
            var client = httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("authorization", $"{tokenResponse.TokenType} {tokenResponse.AccessToken}");
            return client;
        }

        public static async Task<TokenResponse> GetToken(IServiceProvider serviceProvider)
        {
            var resourceOwnerAuthorizationOptions = serviceProvider.GetRequiredService<ResourceOwnerAuthorizationOptions>();
            var tokenResponse = await RequestTokenAsync(resourceOwnerAuthorizationOptions, serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient());
            return tokenResponse;
        }

        public static async Task<TokenResponse> RequestTokenAsync(ResourceOwnerAuthorizationOptions authorizationOptions, HttpClient httpClient)
        {
            var disco = await httpClient.GetDiscoveryDocumentAsync(
                new DiscoveryDocumentRequest
                {
                    Address = authorizationOptions.Authority,
                    Policy =
                    {
                        ValidateIssuerName = authorizationOptions.ValidateDiscoveryDocument,
                        ValidateEndpoints = authorizationOptions.ValidateDiscoveryDocument,
                    },
                }
            );

            if (disco.IsError) throw new Exception(disco.Error);

            PasswordTokenRequest request = new PasswordTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = authorizationOptions.ClientId,
                ClientSecret = string.IsNullOrEmpty(authorizationOptions.ClientSecret) ? null : authorizationOptions.ClientSecret,
                Password = authorizationOptions.Password,
                Scope = authorizationOptions.Scope,
                UserName = authorizationOptions.UserName
            };

            return await httpClient.RequestPasswordTokenAsync(request);
        }

    }
}
