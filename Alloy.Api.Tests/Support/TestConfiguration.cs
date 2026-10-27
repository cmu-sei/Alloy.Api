// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// The factory takes step 1B, so there is no open-api-only switch here; the database provider and the
// host's connection string are host settings (AlloyAppFactory), because Program.Main reads them.

using System.Collections.Generic;

namespace Alloy.Api.Tests.Support;

/// <summary>
/// The configuration the app factory layers over the application's own <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <c>WebApplicationFactory</c> resolves the content root to the API project directory, so the shipped
/// configuration is already in force and only keys whose shipped value breaks or weakens a test run belong
/// here. Every entry states which.
/// </remarks>
internal static class TestConfiguration
{
    public static Dictionary<string, string> Values => new()
    {
        // One host serves the whole run, and the claims cache is keyed on user id alone, so cached claims
        // would leak across tests: a user whose permissions one test seeds would keep them in the next
        // test that uses the same id. A test whose subject is the cache drives it directly.
        ["ClaimsTransformation:EnableCaching"] = "false",

        // Shipped empty, and IdentityModel refuses a password token request without a user name, so every
        // call Alloy makes on its own behalf (enlist, redeploy) would fail before reaching TestIdentity.
        ["ResourceOwnerAuthorization:UserName"] = "alloy-service",
        ["ResourceOwnerAuthorization:Password"] = "alloy-service-password",
    };
}
