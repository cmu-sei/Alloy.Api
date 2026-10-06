using System;
using System.Linq;
using Alloy.Api.Infrastructure.Extensions;
using Xunit;

namespace Alloy.Api.Tests;

/// <summary>
/// Caster rejects a Workspace name unless it is 1 to 90 characters of letters, numbers, -, _, and .
/// Alloy builds the name from the user's display name, so every display name must give a name Caster accepts,
/// or the event launch fails.
/// </summary>
public class CasterWorkspaceNameTests
{
    private static readonly Guid UserId = Guid.Parse("6fe1b3d2-0d7c-4c1e-9d5a-2f3b8c9e4a10");

    private static bool IsValidCasterName(string name) =>
        name.Length is >= 1 and <= 90 &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.');

    [Theory]
    [InlineData("Admin User", "admin_user-6fe1b3d2-0d7c-4c1e-9d5a-2f3b8c9e4a10")]
    [InlineData("Smith, John", "smith_john-6fe1b3d2-0d7c-4c1e-9d5a-2f3b8c9e4a10")]
    [InlineData("O'Brien & Co. <ops>", "obrien__co._ops-6fe1b3d2-0d7c-4c1e-9d5a-2f3b8c9e4a10")]
    [InlineData("José+test@example.com", "jostestexample.com-6fe1b3d2-0d7c-4c1e-9d5a-2f3b8c9e4a10")]
    public void GetWorkspaceNameRemovesCharactersCasterRejects(string userName, string expected)
    {
        Assert.Equal(expected, CasterApiExtensions.GetWorkspaceName(userName, UserId));
    }

    [Theory]
    [InlineData("A Display Name That Is Much Longer Than Anyone Would Expect To See Here")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("🙂")]
    public void GetWorkspaceNameIsAlwaysAcceptedByCaster(string userName)
    {
        var name = CasterApiExtensions.GetWorkspaceName(userName, UserId);

        Assert.True(IsValidCasterName(name), $"Caster would reject '{name}'");
        Assert.EndsWith(UserId.ToString(), name);
    }
}
