using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Plugins;

namespace Vortex.WebApi.Authentication;

/// <summary>The website's account check: email and password, nothing else (coupe au noyau).</summary>
public sealed class WebApiAuthenticationModule : IHostPluginModule
{
    public string Key => "vortex-webapi-authentication";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<IAccountAuthenticator, AccountAuthenticator>();
    }
}
