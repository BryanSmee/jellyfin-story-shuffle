using Microsoft.Extensions.DependencyInjection;

namespace MediaBrowser.Controller
{
    public interface IServerApplicationHost { }
}

namespace MediaBrowser.Controller.Plugins
{
    public interface IPluginServiceRegistrator
    {
        void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost);
    }
}
