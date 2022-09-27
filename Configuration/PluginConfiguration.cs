using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Emby.SubKiller.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        //Sub Killer
        public bool EnableSubKiller { get; set; }
        public bool RunSubKillerOnItemAdded { get; set; }
        public bool EnableSubTitleExtract { get; set; }
        public bool EnableExtractForced { get; set; }
        public bool KeepUndefined { get; set; }
        public string SelectedLanguages { get; set; }
        public bool EnableSubKillerRefresh { get; set; }
        public List<string> LibrariesToConvert { get; set; }

        //Processed Lists
        public List<long> SubKillerProcessedList { get; set; }
        
        public PluginConfiguration()
        {
            //add default values here to use
            
            EnableSubKiller = true;
            RunSubKillerOnItemAdded = false;
            EnableSubTitleExtract = false;
            EnableExtractForced = false;
            KeepUndefined = false;
            SelectedLanguages = "";
            EnableSubKillerRefresh = false;
            LibrariesToConvert = new List<string>();
            
            //add default values here to use

        }

    }
}
