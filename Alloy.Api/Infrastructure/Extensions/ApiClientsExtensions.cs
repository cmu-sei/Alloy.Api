// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Alloy.Api.Infrastructure.Options;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Caster.Api;
using IdentityModel.Client;
using Steamfitter.Api;
using Player.Api;

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

        public static async Task<TokenResponse> GetToken(IServiceScope scope)
        {
            var resourceOwnerAuthorizationOptions = scope.ServiceProvider.GetRequiredService<ResourceOwnerAuthorizationOptions>();
            var tokenResponse = await RequestTokenAsync(resourceOwnerAuthorizationOptions);
            return tokenResponse;
        }

        public static async Task<TokenResponse> RequestTokenAsync(ResourceOwnerAuthorizationOptions authorizationOptions)
        {
            var disco = await DiscoveryClient.GetAsync(authorizationOptions.Authority);
            if (disco.IsError) throw new Exception(disco.Error);

            TokenClient client = null;

            if (string.IsNullOrEmpty(authorizationOptions.ClientSecret))
            {
                client = new TokenClient(disco.TokenEndpoint, authorizationOptions.ClientId);
            }
            else
            {
                client = new TokenClient(disco.TokenEndpoint, authorizationOptions.ClientId, authorizationOptions.ClientSecret);
            }

            return await client.RequestResourceOwnerPasswordAsync(authorizationOptions.UserName, authorizationOptions.Password, authorizationOptions.Scope);
        }

    }
}
