using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.StoryShuffle.Web;

/// <summary>Puts the shuffle middleware in front of Jellyfin's request pipeline.</summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddTransient<IStartupFilter, StartupFilter>();
    }

    internal sealed class StartupFilter : IStartupFilter
    {
        public System.Action<IApplicationBuilder> Configure(System.Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseMiddleware<ShuffleMiddleware>();
                next(app);
            };
        }
    }
}
