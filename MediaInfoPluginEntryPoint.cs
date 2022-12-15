using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.SubKiller
{
    public class MediaInfoPluginEntryPoint : IServerEntryPoint
    {
        public static MediaInfoPluginEntryPoint Instance { get; private set; }

        private readonly IServerConfigurationManager _config;

        private readonly ITaskManager TaskManager;
        private ILibraryMonitor LibraryMonitor { get; }
        private ILibraryManager LibraryManager { get; }
        private ILogger Log { get; }
        private IFileSystem FileSystem { get; }
        public IApplicationPaths ApplicationPaths { get; set; }

        

        public MediaInfoPluginEntryPoint(IServerConfigurationManager config, ITaskManager taskManager,
            IFileSystem fileSystem, ILogManager logManager, ILibraryMonitor libraryMonitor, ILibraryManager libraryManager)
        {
            _config = config;
            TaskManager = taskManager;
            FileSystem = fileSystem;
            LibraryMonitor = libraryMonitor;
            LibraryManager = libraryManager;
            Log = logManager.GetLogger(Plugin.Instance.Name);
        }

        public void Run()
        {
            Plugin.Instance.UpdateConfiguration(Plugin.Instance.Configuration);
            //LibraryManager.ItemUpdated += LibraryManagerItemAdded;
            LibraryManager.ItemAdded += LibraryManagerItemAdded;
            LibraryManager.ItemRemoved += LibraryManagerItemRemoved;
            TaskManager.TaskCompleted += TaskManagerOnTaskCompleted;
        }

        private void LibraryManagerItemRemoved(object sender, ItemChangeEventArgs e)
        {
            var item = e.Item;
            Log.Info("Library Monitory has removed {0} from the library", item);
            var config = Plugin.Instance.Configuration;
            
            try
            {
                config.SubKillerProcessedList.Remove(item.InternalId);
                Log.Info("{0} has been removed from SubKiller Processed List", item.Name);
            }
            catch
            {
                //catch the null error
            }
        }

        private async void LibraryManagerItemAdded(object sender, ItemChangeEventArgs e)
        {
            
            var config = Plugin.Instance.Configuration;
            /*var item = e.Item;
            var libOptions = LibraryManager.GetLibraryOptions(item);
            if (libOptions != null)
            {
                var isRTMEnabled = libOptions.EnableRealtimeMonitor;
                Log.Info("Emby RTM Enabled = {0} for {1}", isRTMEnabled.ToString(), item.Parent.Name);
            }*/

            if (config.RunSubKillerOnItemAdded && config.EnableSubKiller)
            {
                Log.Info("Library Monitory has started for SubKiller but will wait 5 mins");
                await Task.Delay(TimeSpan.FromSeconds(300));
                try
                {
                    Log.Info("New Item Added --- Running SubKiller Task for {0}", e.Item.Name);
                    await TaskManager.Execute(TaskManager.ScheduledTasks.FirstOrDefault(t => t.Name == "SubKiller - Remove Unwanted Subtitles"), new TaskOptions());

                }
                catch (Exception ex)
                {
                    Log.Error("Error in Starting Subkiller Task for newly added item {0}", e.Item.Name);
                    Log.Error(ex.ToString());
                }
            }
        }
        
        public void Dispose()
        {
            //Plugin.Instance.UpdateConfiguration(Plugin.Instance.Configuration);
            TaskManager.TaskCompleted -= TaskManagerOnTaskCompleted;
        }

        private void TaskManagerOnTaskCompleted(object sender, TaskCompletionEventArgs e)
        {
            var config = Plugin.Instance.Configuration;

            switch (e.Task.Name)
            {
                case "1. Media Info":
                    if (!config.RunSubKillerOnItemAdded) return;
                    try
                    {
                        TaskManager.Execute(
                            TaskManager.ScheduledTasks.FirstOrDefault(t => t.Name == "Scan media library"),
                            new TaskOptions());
                    }
                    catch { } //If this task is already running, we'll catch the error

                    break;
            }
        }

        
    }
}
