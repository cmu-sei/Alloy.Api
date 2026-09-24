using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Mappings;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Services;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Alloy.Api.Tests;

internal sealed class TestEnvironment : IDisposable
{
    private readonly TestDatabase database;
    public DbContextOptions<AlloyContext> DbOptions { get; }
    public ServiceProvider Services { get; }
    public FakeHttp Http { get; } = new();
    public AlloyEventQueue Queue { get; } = new();
    public IMapper Mapper { get; } = new MapperConfiguration(c =>
    {
        c.AddProfile<EventProfile>();
        c.AddProfile<EventTemplateProfile>();
    }).CreateMapper();
    public ClientOptions Options { get; } = new()
    {
        ApiClientLaunchFailureMaxRetries = 2,
        ApiClientEndFailureMaxRetries = 2,
        CasterPlanningMaxWaitMinutes = 1,
        CasterDeployMaxWaitMinutes = 1,
        CasterDestroyMaxWaitMinutes = 1,
        urls = new() { casterApi = "https://caster.test", playerApi = "https://player.test", steamfitterApi = "https://steamfitter.test" }
    };

    public TestEnvironment(PostgresFixture postgres)
    {
        database = postgres.CreateDatabase();
        DbOptions = new DbContextOptionsBuilder<AlloyContext>()
            .UseNpgsql(database.ConnectionString, x => x.MigrationsAssembly("Alloy.Api.Migrations.PostgreSQL")).Options;
        using var db = Context();
        db.Database.EnsureCreated();
        Services = new ServiceCollection()
            .AddScoped(_ => Context())
            .AddSingleton<IHttpClientFactory>(Http)
            .AddSingleton(new ResourceOwnerAuthorizationOptions
            {
                Authority = "https://identity.test", ClientId = "test", UserName = "test", Password = "test"
            })
            .BuildServiceProvider();
    }

    public AlloyContext Context() => new(DbOptions);

    public async Task<EventEntity> Seed(EventStatus status = EventStatus.Active,
        InternalEventStatus internalStatus = InternalEventStatus.Launched)
    {
        using var db = Context();
        var template = new EventTemplateEntity { Id = Guid.NewGuid(), Name = "Template", DurationHours = 1 };
        var entity = new EventEntity
        {
            Id = Guid.NewGuid(), EventTemplateId = template.Id, Name = "Event", Username = "user",
            Status = status, InternalStatus = internalStatus
        };
        db.EventTemplates.Add(template);
        db.Events.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public EventService EventService(AlloyContext db) => new(db, null, null,
        new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())])),
        Mapper, null, null, null, Queue, NullLogger<EventService>.Instance, null, null,
        Services.GetRequiredService<ResourceOwnerAuthorizationOptions>(), Options, Http, Services);

    public EventTemplateService EventTemplateService(AlloyContext db, IPlayerService player) => new(db, null,
        new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())])),
        Mapper, NullLogger<EventTemplateService>.Instance, player);

    public AlloyBackgroundService Worker() => new(
        NullLogger<AlloyBackgroundService>.Instance, new Monitor<ClientOptions>(Options),
        Services.GetRequiredService<IServiceScopeFactory>(), Queue, Http,
        new StartupHealthCheck(), new TelemetryService());

    public async Task RequestEnd(Guid id)
    {
        using var db = Context();
        await EventService(db).EndAsync(id, CancellationToken.None);
    }

    public async Task<EventEntity> Read(Guid id)
    {
        using var db = Context();
        return await db.Events.SingleAsync(x => x.Id == id);
    }

    public void Dispose()
    {
        Services.Dispose();
        Http.Dispose();
        database.Dispose();
    }

    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string name) => value;
        public IDisposable OnChange(Action<T, string> listener) => null;
    }
}

/// <summary>
/// Stands in for <see cref="IPlayerService"/> so EventTemplate validation can be exercised without Player.
/// Only GetViewAsync is used; the other members are not reached by the code under test.
/// </summary>
internal sealed class StubPlayerService : IPlayerService
{
    public Func<Guid, Task<Player.Api.Client.View>> OnGetView { get; set; } =
        id => throw new InvalidOperationException($"Unexpected GetViewAsync({id})");

    public int GetViewCalls { get; private set; }

    public Task<Player.Api.Client.View> GetViewAsync(Guid viewId, CancellationToken ct)
    {
        GetViewCalls++;
        return OnGetView(viewId);
    }

    public Task<IEnumerable<Player.Api.Client.View>> GetViewsAsync(CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Player.Api.Client.View> CloneViewAsync(Guid viewId, Player.Api.Client.CloneViewCommand command, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task DeleteViewAsync(Guid viewId, CancellationToken ct) => throw new NotSupportedException();

    /// <summary>A View with a default team, so validation accepts it.</summary>
    public static StubPlayerService WithDefaultTeam() => new()
    {
        OnGetView = id => Task.FromResult(new Player.Api.Client.View
        {
            Id = id, Name = "Good View", DefaultTeamId = Guid.NewGuid()
        })
    };

    /// <summary>A View with no default team, so validation rejects it.</summary>
    public static StubPlayerService WithoutDefaultTeam() => new()
    {
        OnGetView = id => Task.FromResult(new Player.Api.Client.View
        {
            Id = id, Name = "Bad View", DefaultTeamId = null
        })
    };

    /// <summary>Player cannot be reached or the View is not visible, so validation must fail closed.</summary>
    public static StubPlayerService Unavailable() => new()
    {
        OnGetView = _ => throw new HttpRequestException("Player is unavailable")
    };
}

internal sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
{
    public List<string> Requests { get; } = [];
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handle { get; set; } =
        request => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");

    public HttpClient CreateClient(string name = "") => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri.Host == "identity.test")
        {
            if (uri.AbsolutePath.EndsWith("/.well-known/openid-configuration"))
                return Task.FromResult(Json(new { issuer = "https://identity.test", token_endpoint = "https://identity.test/token", jwks_uri = "https://identity.test/keys" }));
            if (uri.AbsolutePath == "/keys")
                return Task.FromResult(Json(new { keys = Array.Empty<object>() }));
            return Task.FromResult(Json(new { access_token = "test", token_type = "Bearer", expires_in = 3600 }));
        }
        Requests.Add($"{request.Method} {uri.AbsolutePath}");
        return Handle(request);
    }

    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            System.Text.Encoding.UTF8, "application/json")
    };
}
