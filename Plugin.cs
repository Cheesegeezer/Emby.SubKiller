using System;
using System.Collections.Generic;
using System.IO;
using Emby.SubKiller.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.SubKiller
{
	public class Plugin : BasePlugin<PluginConfiguration>, IHasThumbImage, IHasWebPages
	{
        public static Plugin Instance { get; set; }
        
        //You will need to generate a new GUID and paste it here - Tools => Create GUID
        public override Guid Id => new Guid("700B24C7-A6ED-4164-905D-C39622D08868");
        public override string Name => "SubKiller";
        public override string Description => "Remove unwanted Subtitles";
        
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogManager logManager) : base(applicationPaths,
            xmlSerializer)
        {
            Instance = this;

        }
        public ImageFormat ThumbImageFormat => ImageFormat.Jpg;

        //Display Thumbnail image for Plugin Catalogue  - you will need to change build action for thumb.jpg to embedded Resource
        public Stream GetThumbImage()
        {
            Type type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.jpg");
        }

        //Web pages for Server UI configuration
        public IEnumerable<PluginPageInfo> GetPages() => new[]
        {
            new PluginPageInfo
            {
                //html File
                //If in a folder in your project you must include the FolderName as well as the File name.
                Name = "SubKillerPluginConfigurationPage",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.SubKillerConfigurationPage.html",
                EnableInMainMenu = true,
                DisplayName = "SubKiller",
            },
            
            new PluginPageInfo
            {
                //javascript file
                Name = "SubKillerPluginConfigurationPageJS",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.SubKillerConfigurationPage.js"
            }
        };

       
    }
}
