// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// The handlers are the four AuthorizationPolicyExtensions.AddAuthorizationPolicy registers. Alloy's own
// service, AuthorizationService, wraps the framework one with an IIdentityResolver and the context.

using System.Security.Claims;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Tests.Support;

/// <summary>The authorization stack wired as production wires it, for testing handlers directly.</summary>
public static class AuthorizationHarness
{
    /// <summary>The framework authorization service with the app's handlers registered.</summary>
    public static IAuthorizationService CreateFrameworkAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, SystemPermissionHandler>();
        services.AddSingleton<IAuthorizationHandler, EventPermissionHandler>();
        services.AddSingleton<IAuthorizationHandler, EventTemplatePermissionHandler>();
        services.AddSingleton<IAuthorizationHandler, GroupPermissionsHandler>();

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>
    /// Alloy's <see cref="IAlloyAuthorizationService"/> for <paramref name="user"/>, over the framework
    /// service above and <paramref name="db"/>, which it reads to resolve a membership to its resource.
    /// </summary>
    public static IAlloyAuthorizationService CreateAlloyAuthorizationService(ClaimsPrincipal user, AlloyContext db)
    {
        var framework = CreateFrameworkAuthorizationService();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };

        return new AuthorizationService(framework, new IdentityResolver(accessor, framework), db);
    }

    /// <summary>
    /// Runs a requirement through a handler directly and returns the resulting context.
    /// </summary>
    public static async Task<AuthorizationHandlerContext> HandleAsync<TRequirement>(
        IAuthorizationHandler handler,
        TRequirement requirement,
        ClaimsPrincipal user,
        object resource = null)
        where TRequirement : IAuthorizationRequirement
    {
        var context = new AuthorizationHandlerContext([requirement], user, resource);
        await handler.HandleAsync(context);

        return context;
    }
}
