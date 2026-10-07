using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Shop;
using Vortex.WebApi.Shop.Payments;

namespace Vortex.WebApi.Shop;

/// <summary>
/// Registers the paid shop: its configuration, its payment providers, and the service the website
/// and the dashboard both call.
/// </summary>
/// <remarks>
/// <para>
/// Providers are registered as a COLLECTION, not as one implementation, which is the whole of the
/// module's extensibility: a second PSP is
/// <c>services.AddSingleton&lt;IShopPaymentProvider, StripeProvider&gt;()</c> from its own assembly
/// and nothing here, in <c>ShopService</c>, in the endpoints or on the site changes. Which one new
/// orders open against is <c>ShopConfig.Provider</c> — an operator's decision, not a deployment's.
/// </para>
/// <para>
/// <c>Manual</c> is always registered, including once a real PSP exists: a bank transfer, a
/// competition prize paid as credits, or a payment the provider lost still have to be settled, and
/// the alternative is somebody editing the currency table by hand.
/// </para>
/// <para>
/// The configuration is NOT validated on start. An unconfigured shop is a perfectly good state — most
/// hotels will never take money — and it fails closed on its own: with no secret, no order opens and
/// every webhook answers as if the provider did not exist.
/// </para>
/// </remarks>
public sealed class ShopModule : IHostPluginModule
{
    public string Key => "vortex-shop";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services
            .AddOptions<ShopConfig>()
            .Bind(builder.Configuration.GetSection(ShopConfig.SECTION_NAME));

        services.AddSingleton<IShopPaymentProvider, ManualPaymentProvider>();
        services.TryAddSingleton<IShopService, ShopService>();
    }
}
