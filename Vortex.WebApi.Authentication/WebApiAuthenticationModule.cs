using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Plugins;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// The website's accounts: the credential check, passwords, second factors, e-mail, safety lock,
/// security questions and trusted locations. Host services rather than the web API plugin's own,
/// because the dashboard signs people in through the same checks.
/// </summary>
public sealed class WebApiAuthenticationModule : IHostPluginModule
{
    public string Key => "vortex-webapi-authentication";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<IAccountAuthenticator, AccountAuthenticator>();
        services.AddSingleton<IAccountMfaService, AccountMfaService>();
        services.AddSingleton<IAccountPasswordService, AccountPasswordService>();
        services.AddSingleton<IAccountEmailService, AccountEmailService>();
        services.AddSingleton<IAccountSafetyLockService, AccountSafetyLockService>();
        services.AddSingleton<IAccountSafetyQuestionsService, AccountSafetyQuestionsService>();
        services.AddSingleton<IAccountTrustedLocationService, AccountTrustedLocationService>();
    }
}
