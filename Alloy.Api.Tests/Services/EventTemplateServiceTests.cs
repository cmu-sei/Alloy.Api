// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Services;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Tests.Services;

/// <summary>
/// <see cref="EventTemplateService"/> where the request itself is the subject: a request cancelled while a
/// reference is being checked. Over HTTP a cancelled request has no response to read, so only a service
/// test can see what the cancellation became. The reference checks themselves, and the routes' gates, are
/// tested over HTTP in <c>EventTemplateReferenceValidationTests</c> and <c>EventTemplateControllerTests</c>.
/// </summary>
public class EventTemplateServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    [Fact]
    public async Task Create_passes_on_a_cancellation_during_the_directory_check_rather_than_refusing_the_directory()
    {
        var directoryId = Guid.NewGuid();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var host = HostFor(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.CreateEventTemplates).Build());
        host.SiblingApis.Handle = _ =>
        {
            request.Cancel();
            throw new OperationCanceledException(request.Token);
        };
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventTemplateService>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateAsync(new EventTemplate { Name = "Template", DurationHours = 1, DirectoryId = directoryId }, request.Token));

        Assert.Equal([$"GET /api/directories/{directoryId}"], host.SiblingApis.Requests);
        Assert.False(await ReadBack(db => db.EventTemplates.AnyAsync(Ct)));
    }
}
