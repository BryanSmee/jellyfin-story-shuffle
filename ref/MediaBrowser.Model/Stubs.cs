using System;
using System.Collections.Generic;
using System.IO;

namespace MediaBrowser.Model.Plugins
{
    public class BasePluginConfiguration { }

    public class PluginPageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string EmbeddedResourcePath { get; set; } = string.Empty;
        public bool EnableInMainMenu { get; set; }
        public string? MenuSection { get; set; }
        public string? MenuIcon { get; set; }
    }

    public interface IHasWebPages
    {
        IEnumerable<PluginPageInfo> GetPages();
    }
}

namespace MediaBrowser.Model.Serialization
{
    public interface IXmlSerializer { }
}
