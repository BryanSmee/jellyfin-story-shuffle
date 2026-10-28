using System;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Common.Configuration
{
    public interface IApplicationPaths { }
}

namespace MediaBrowser.Common.Plugins
{
    public abstract class BasePlugin
    {
        public abstract string Name { get; }
        public virtual string Description => string.Empty;
        public virtual Guid Id { get; private set; }
    }

    public abstract class BasePlugin<TConfigurationType> : BasePlugin
        where TConfigurationType : BasePluginConfiguration
    {
        protected BasePlugin(MediaBrowser.Common.Configuration.IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            => throw new NotSupportedException();

        public TConfigurationType Configuration
        {
            get => throw new NotSupportedException();
            protected set => throw new NotSupportedException();
        }

        public EventHandler<BasePluginConfiguration> ConfigurationChanged
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
