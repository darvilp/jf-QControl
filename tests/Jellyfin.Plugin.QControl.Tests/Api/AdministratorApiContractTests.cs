using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.QControl.Api;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Api;

public sealed class AdministratorApiContractTests
{
    [Theory]
    [InlineData(typeof(QControlConfigurationController), "QControl/Configuration")]
    [InlineData(typeof(QControlConnectionController), "QControl/Connection")]
    [InlineData(typeof(QControlStatusController), "QControl/Status")]
    [InlineData(typeof(QControlRecoveryController), "QControl/Recovery")]
    public void EveryControllerRequiresAdministratorElevation(Type controllerType, string route)
    {
        var authorize = Assert.Single(controllerType.GetCustomAttributes<AuthorizeAttribute>());
        var routeAttribute = Assert.Single(controllerType.GetCustomAttributes<RouteAttribute>());

        Assert.Equal(Policies.RequiresElevation, authorize.Policy);
        Assert.Equal(route, routeAttribute.Template);
        Assert.NotNull(controllerType.GetCustomAttribute<ApiControllerAttribute>());
    }

    [Fact]
    public void RecoveryCommandsUseExplicitNonOverlappingRoutes()
    {
        var methods = typeof(QControlRecoveryController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(QControlRecoveryController))
            .ToArray();

        Assert.Equal(
            ["MarkResolved", "RestorePreviousSpeedSetting", "ResumeMarkedTorrents"],
            methods
                .Select(method => method.GetCustomAttribute<HttpPostAttribute>()?.Template)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ConnectionEndpointsAreReadOnlyQbittorrentContracts()
    {
        var methods = typeof(QControlConnectionController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public);
        var test = Assert.Single(methods, method => method.Name == "TestAsync");
        var categories = Assert.Single(methods, method => method.Name == "GetCategoriesAsync");

        Assert.Equal("Test", test.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal("Categories", categories.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }
}
