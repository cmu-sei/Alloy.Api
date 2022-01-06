// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Infrastructure.Options
{
    public class ResourceOwnerAuthorizationOptions
    {
        public string Authority { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string Scope { get; set; }
        public int TokenExpirationBufferSeconds { get; set; }
        public bool ValidateDiscoveryDocument { get; set; }
    }
}

