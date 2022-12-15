using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.SubKiller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using ILogger = MediaBrowser.Model.Logging.ILogger;

namespace Emby.SubKiller.ScheduledTasks
{
    //Use this section if you need to have Scheduled tasks run
    public class SubKillerScheduledTask : IScheduledTask, IConfigurableScheduledTask, IServerEntryPoint
    {
        private ITaskManager TaskManager { get; }
        private ILibraryManager LibraryManager { get; }
        private IFfmpegManager FfmpegManager { get; }
        private PluginConfiguration config => Plugin.Instance.Configuration;
        private ILogger Log { get; }
        public string Name => "SubKiller - Remove Unwanted Subtitles";

        public string Key => "SubKiller";

        public string Description => "Remove unwanted Subtitles from your Media";

        public string Category => "Sub-Killer";

        //public bool IsHidden => true;
        public bool IsHidden
        {
            get
            {
                var config = Plugin.Instance.Configuration;
                return !config.EnableSubKiller;
            }
        }
        public bool IsEnabled => true;
        public bool IsLogged => true;
        
        private double _totalProgress;
        private int _totalItems;

        private List<Tuple<string, int>> _subList;
        
        private long[] _newlibraryItemIds;
        private int _totalLibraries;
        private BaseItem[] _itemsInLibraries;
        private int _numberOfVideoItemsInLibraries;
        private List<Tuple<string,string, int, bool>> _subTextList;
        private List<Tuple<string, string, int, bool>> _subForcedTextList;
        private List<long> FailedItemList;

        private int ExitCode { get; set; }


        public SubKillerScheduledTask(ITaskManager taskManager, ILogManager logManager, ILibraryManager libraryManager, IFfmpegManager ffmpegManager)
        {
            TaskManager = taskManager;
            LibraryManager = libraryManager;
            FfmpegManager = ffmpegManager;
            Log = logManager.GetLogger(Plugin.Instance.Name);
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _totalProgress = 0;
            ExitCode = 0;
            
            if (config.EnableSubKillerRefresh)
            {
               config.SubKillerProcessedList.Clear();
            }

            try
            {
                Log.Debug("SUBKILLER TASK IS STARTING");
                Log.Debug("isEnabled = {0}", config.EnableSubKiller.ToString());

                if (config.EnableSubKiller)
                {
                    await GetItemIdInEmbyLibraries();

                    int movItems = _itemsInLibraries == null ? 0 : _itemsInLibraries.Length;
                    _totalItems = movItems;

                    if (movItems >= 1)
                    {
                        var processedItems = config.SubKillerProcessedList;
                        var itemsToProcess = _itemsInLibraries.Where(t => !processedItems.Contains(t.InternalId)).ToList();

                        if (itemsToProcess.Count == 0)
                        {
                            Log.Info("There are NO items to process this time");
                        }
                        else
                        {
                            Log.Info("Actual Items to process = {0}", itemsToProcess.Count.ToString());
                        }

                        Log.Info("PERFORMING SUBTITLE REMOVAL ON YOUR LIBRARIES");
                        foreach (var item in itemsToProcess)
                        {
                            var libOptions = LibraryManager.GetLibraryOptions(item);
                            bool rtmEnabled = false;
                            var isRTMEnabled = libOptions.EnableRealtimeMonitor;
                            
                            if (isRTMEnabled)
                            {
                                rtmEnabled = true;
                                libOptions.EnableRealtimeMonitor = false;

                                Thread.Sleep(500);

                                //Log.Info("Emby RTM Enabled = {0} for {1}", isRTMEnabled.ToString(), item.Parent.Name);
                            }
                            
                            try
                            {
                                if (!config.SubKillerProcessedList.Contains(item.InternalId))
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        Log.Info("SubKiller Task has been Cancelled and will exit");
                                        if(rtmEnabled == true)
                                        {
                                            libOptions.EnableRealtimeMonitor = true;
                                            return;
                                        }
                                    }

                                    var stopWatch = new Stopwatch();
                                    stopWatch.Start();

                                    Log.Info("PROCESSING OF  " + item.Name + "  HAS STARTED");
                                    Log.Info("Item FilePath = {0} ", item.Path);

                                    await MatchSubtitles(item);
                                    
                                    Log.Info("PROCESSING OF {0} HAS COMPLETED", item.Name);
                                    _totalProgress++;
                                    double dprogress = 100 * (_totalProgress / _totalItems);
                                    progress.Report(dprogress);

                                    stopWatch.Stop();
                                    Log.Debug("SubKiller processing for {0} and took {1} milliseconds", item.Name, (stopWatch.ElapsedMilliseconds).ToString());
                                    
                                    config.SubKillerProcessedList.Add(item.InternalId);
                                    Log.Debug("Id -{0} - {1} has already been added to the processed List", item.InternalId, item.Name);
                                    Plugin.Instance.UpdateConfiguration(Plugin.Instance.Configuration);
                                }
                                else
                                {
                                    _totalProgress++;
                                    double dprogress = 100 * (_totalProgress / _totalItems);
                                    progress.Report(dprogress);
                                    Log.Debug("Id -{0} - {1} has already been processed and will be skipped", item.InternalId, item.Name);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error("***** ERROR ***** UNABLE TO PERFORM SUBTITLE PROCESSING IN MAIN METHOD",ex.ToString());
                                Log.Error(ex.ToString());
                                
                            }

                            if (rtmEnabled == true)
                            {
                                libOptions.EnableRealtimeMonitor = true;
                                Thread.Sleep(500);
                            }
                        }
                        
                        Log.Info("SubKiller EXTRACTION Completed for {0} Media Items", _numberOfVideoItemsInLibraries.ToString());
                    }
                }
                
                if(!config.EnableSubKiller)
                {
                    Log.Info("SubKiller ISN'T ENABLED IN THE CONFIG");
                }

                await Task.FromResult(true);

                config.EnableSubKillerRefresh = false;
                foreach (var failedItem in FailedItemList)
                {
                    Log.Info("Removing {0} from the Processed List due to processing failure", failedItem.ToString());
                    config.SubKillerProcessedList.Remove(failedItem);

                }
                Plugin.Instance.UpdateConfiguration(Plugin.Instance.Configuration);

                await RunMetaDataScan();

                await RunLibraryScan();
            }
            catch (Exception ex)
            {
                progress.Report(0.0);
                Log.ErrorException(ex.Message, ex);
            }
        }

        private Task RunMetaDataScan()
        {
            var runMetScan = TaskManager.ScheduledTasks.FirstOrDefault(task => task.Name == "Scan Metadata Folder");

            TaskManager.Execute(runMetScan, new TaskOptions());

            return Task.FromResult(true);
        }

        private Task RunLibraryScan()
        {
            var libraryScan = TaskManager.ScheduledTasks.FirstOrDefault(t => t.Name == "Scan media library");

            TaskManager.Execute(libraryScan, new TaskOptions());

            return Task.FromResult(true);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new List<TaskTriggerInfo>();
        }
        
        #region 4.7.7.0 Library Code
        private async Task GetItemIdInEmbyLibraries()
        {
            List<long> convertedIdList = new List<long>();
            try
            {
                long[] libraryIds = config.LibrariesToConvert.Select(libraryId => Convert.ToInt64(libraryId)).ToArray();


                foreach (var libraryId in libraryIds)
                {
                    var library = LibraryManager.GetItemById(libraryId);
                    if (library != null)
                    {
                        convertedIdList.Add(libraryId);
                    }
                }
                _newlibraryItemIds = convertedIdList.ToArray();
                _totalLibraries = _newlibraryItemIds.Length;
                if (_totalLibraries == 0)
                {
                    Log.Warn("No libraries found to convert in the configuration - Exiting Now");
                    return;
                }
                
                Log.Info("No. of Libraries selected is {0}", _totalLibraries.ToString());

                foreach (var folder in _newlibraryItemIds)
                {
                    var rootItem = LibraryManager.GetItemById(folder);
                    Log.Debug("Internal Library Folder Id = {0} - Folder Name:{1}", folder.ToString(), rootItem.Name);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ERROR: NO Library Folders Found");
                Log.Error(ex.ToString());
            }

            try
            {
                Log.Info("Getting Root Folder Library Items");

                var queryList = new InternalItemsQuery
                {
                    Recursive = true,
                    ParentIds = _newlibraryItemIds,
                    MediaTypes = new[] { "Video" },
                    IsVirtualItem = false,
                };

                _itemsInLibraries = LibraryManager.GetItemList(queryList);
                _numberOfVideoItemsInLibraries = _itemsInLibraries.Length;
                Log.Info("Total No. of items in Library {0}", _numberOfVideoItemsInLibraries.ToString());
            }
            catch (Exception ex)
            {
                Log.Error("No Lib Items Found in Library");
                Log.Error(ex.ToString());
            }
        }
        #endregion

        private async Task MatchSubtitles(BaseItem item)
        {
            ExitCode = 0;

            Video stream = (Video)item;
            List<MediaStream> allstreams = stream.GetMediaStreams();
            var totalStreams = allstreams.Count;
            Log.Debug("total No of Streams = {0}", totalStreams.ToString());

            IEnumerable<MediaStream> subtitles = allstreams.Where(d => (int)d.Type == 3);
            IEnumerable<MediaStream> subStreams = subtitles as MediaStream[] ?? subtitles.ToArray();
            var totalSubStreams = subStreams.Count();
            Log.Debug("total No of Subtitles = {0}", totalSubStreams.ToString());
            var subStreamStart = (totalStreams - totalSubStreams);
            Log.Debug("Subtitle Start Index = {0}", subStreamStart.ToString());
            foreach (var subtitle in subStreams)
            {
                Log.Debug("Subtitle Display Lang = {0}, Embedded Title = {1}, Language = {2}, Index = {3}, isForced={4}, isTextBased = {5}", subtitle.DisplayLanguage, subtitle.Title, subtitle.Language, subtitle.Index, subtitle.IsForced.ToString(), subtitle.IsTextSubtitleStream.ToString());
            }

            string selectedLanguages = config.SelectedLanguages;
            string trimmed = selectedLanguages.Trim('[', ']');
            string[] languages = trimmed.Split(',');

            //create a list with string and int
            _subList = new List<Tuple<string, int>>();
            _subTextList = new List<Tuple<string,string, int, bool>>();
            _subForcedTextList = new List<Tuple<string, string, int, bool>>();

            Log.Info("Languages selected = {0}", selectedLanguages);

            foreach (var subtitle in subStreams)
            {
                if (languages.Length != 0)
                {
                    foreach (var lang in languages)
                    {

                        if (lang == subtitle.Language)
                        {
                           _subList.Add(new Tuple<string, int>(subtitle.Language, subtitle.Index - subStreamStart));
                        }
                        if (lang == subtitle.Language && subtitle.IsTextSubtitleStream && !subtitle.IsExternal)
                        {
                            _subTextList.Add(new Tuple<string, string, int, bool>(subtitle.Language, subtitle.Title, subtitle.Index - subStreamStart, subtitle.IsForced));
                        }
                        if (lang == subtitle.Language && subtitle.IsTextSubtitleStream && subtitle.IsForced && !subtitle.IsExternal)
                        {
                            _subForcedTextList.Add(new Tuple<string, string, int, bool>(subtitle.Language, subtitle.Title, subtitle.Index - subStreamStart, subtitle.IsForced));
                        }
                        
                    }
                }
            }

            if (config.EnableSubTitleExtract && !config.EnableExtractForced)
            {
                Log.Info("Extracting All Subtitles for {0}", item.Name);
                foreach (var storedValue in _subTextList)
                {
                    Log.Debug("Stored Lang:{0} - Stored Title:{1} - Index:{2} - Forced:{3}", storedValue.Item1, storedValue.Item2, storedValue.Item3, storedValue.Item4);
                }

                if (_subTextList.Count > 0 && !config.EnableExtractForced)
                {
                    await ExtractForcedSubtitles(item);
                    Log.Debug("Completed Extraction for All Subtitles for {0}", item.Name);
                    await RemoveTextSubtitles(item);
                    Log.Debug("Completed removal of text subtitles for {0}", item.Name);
                }
                
            }

            if (config.EnableExtractForced)
            {
                Log.Info("Extracting Only Forced Subtitles for {0}", item.Name);
                foreach (var storedValue in _subForcedTextList)
                {
                    Log.Debug("Stored Lang:{0} - Stored Title:{1} - Index:{2} - Forced:{3}", storedValue.Item1, storedValue.Item2, storedValue.Item3, storedValue.Item4);
                    await ExtractForcedSubtitles(item);
                    Log.Debug("Completed Extraction for forced Subtitles for {0}", item.Name);
                    await RemoveTextSubtitles(item);
                    Log.Debug("Completed removal of text subtitles for {0}", item.Name);
                }
            }

            foreach (var storedValue in _subList)
            {
                Log.Info("Processing Graphical based Subtitles for {0}", item.Name);
                Log.Debug("Stored Lang:{0} - Index:{1}", storedValue.Item1, storedValue.Item2);
            }
            if (_subList.Count > 0)
            {
               await ProcessSubtitlesInVideo(item);
            }

            
        }

        private async Task ExtractSubtitles(BaseItem item)
        {
            Log.Info("Extracting Subtitles to File");
            File.SetAttributes(item.Path, FileAttributes.Normal);
            var ffmpegConfiguration = FfmpegManager.FfmpegConfiguration;
            var ffmpegPath = ffmpegConfiguration.EncoderPath;
            
            
            foreach (var storedValue in _subTextList)
            {
                if (_subTextList != null)
                {
                    string forced = "";
                    string sdh = "";
                    var mapCode = "-map 0:s:" + storedValue.Item3 + " ";
                    var lang = storedValue.Item1;
                    var title = storedValue.Item2;
                    bool isForced = storedValue.Item4;
                    bool isSDH = title.IndexOf("SDH", StringComparison.OrdinalIgnoreCase) >= 0; //ignore case for string.Contains equivalent
                    string subExt = "." + config.SubtitleType;

                    forced = isForced ? ".forced" : "";
                    sdh = isSDH ? ".sdh" : "";


                    string extractPath = Path.Combine(item.ContainingFolderPath, Path.GetFileNameWithoutExtension(item.Path) + forced + sdh + "." + lang + subExt);
                    Log.Debug("Extract Subtitle File Path = {0}", extractPath);
                    if (File.Exists(extractPath))
                    {
                        Log.Debug("Subtitle File Already Exists..... Skipping");
                        return;
                    }

                    try
                    {
                        // ffmpeg -i input.ext -map 0:s:2 -c:s copy test2.srt
                        string Args = "-i \"" + item.Path + "\" " + mapCode + "-c:s copy \"" + extractPath + "\"";

                        Log.Debug($"Args={Args}");

                        Process proc = new Process();
                        proc.StartInfo.FileName = ffmpegPath;
                        proc.StartInfo.Arguments = Args;

                        //The command which will be executed
                        proc.StartInfo.UseShellExecute = false;
                        proc.StartInfo.CreateNoWindow = true;
                        proc.StartInfo.RedirectStandardOutput = false;
                        proc.StartInfo.RedirectStandardError = false;

                        //Clear LD_LIBRARY_PATH environment variable for Linux
                        proc.StartInfo.Environment["LD_LIBRARY_PATH"] = null;

                        proc.Start();

                        //var error = await proc.StandardError.ReadToEndAsync();

                        proc.WaitForExit();


                        var exitCode = proc.ExitCode;
                        Log.Debug("Exit Code = {0}", exitCode);

                        if (exitCode == 0)
                        {
                            Log.Debug("{0} - Subtitle File was Successfully Created", item.Name);
                        }

                        if (exitCode == 1 || exitCode == 2)
                        {
                            Log.Warn("Subtitle File FAILED to be created for {0} and exited with Code {1}",
                                item.Name, exitCode.ToString());
                            FailedItemList.Add(item.InternalId);
                            //Log.Warn("Process Error - ",error);
                        }
                    }
                    catch (Exception ex)
                    {
                        //We can catch any process issues that don't really affect the outcome of the process.
                        //Log.Error("Process Error:",ex.Message);
                    }
                }
            }
        }

        private async Task ExtractForcedSubtitles(BaseItem item)
        {
            Log.Info("Extracting FORCED Subtitles to File");
            File.SetAttributes(item.Path, FileAttributes.Normal);
            var ffmpegConfiguration = FfmpegManager.FfmpegConfiguration;
            var ffmpegPath = ffmpegConfiguration.EncoderPath;


            foreach (var storedValue in _subTextList)
            {
                if (_subTextList != null)
                {
                    string forced = "";
                    var mapCode = "-map 0:s:" + storedValue.Item3 + " ";
                    var lang = storedValue.Item1;
                    //var title = storedValue.Item2;
                    bool isForced = storedValue.Item4;
                    string subExt = "." + config.SubtitleType;

                    forced = isForced ? ".forced" : "";


                    string extractPath = Path.Combine(item.ContainingFolderPath, Path.GetFileNameWithoutExtension(item.Path) + forced + "." + lang + subExt);
                    Log.Debug("Extract Subtitle File Path = {0}", extractPath);
                    if (File.Exists(extractPath))
                    {
                        Log.Debug("Subtitle File Already Exists..... Skipping");
                        return;
                    }

                    try
                    {
                        // ffmpeg -i input.ext -map 0:s:2 -c:s copy test2.srt
                        string Args = "-i \"" + item.Path + "\" " + mapCode + "-c:s copy \"" + extractPath + "\"";

                        Log.Debug($"Args={Args}");

                        Process proc = new Process();
                        proc.StartInfo.FileName = ffmpegPath;
                        proc.StartInfo.Arguments = Args;

                        //The command which will be executed
                        proc.StartInfo.UseShellExecute = false;
                        proc.StartInfo.CreateNoWindow = true;
                        proc.StartInfo.RedirectStandardOutput = false;
                        proc.StartInfo.RedirectStandardError = false;

                        //Clear LD_LIBRARY_PATH environment variable for Linux
                        proc.StartInfo.Environment["LD_LIBRARY_PATH"] = null;
                        
                        proc.Start();

                        //var error = await proc.StandardError.ReadToEndAsync();

                        proc.WaitForExit();


                        var exitCode = proc.ExitCode;
                        Log.Debug("Exit Code = {0}", exitCode);

                        if (exitCode == 0)
                        {
                            Log.Debug("{0} - Subtitle File was Successfully Created", item.Name);
                        }

                        if (exitCode == 1 || exitCode == 2)
                        {
                            Log.Warn("Subtitle File FAILED to be created for {0} and exited with Code {1}",
                                item.Name, exitCode.ToString());
                            FailedItemList.Add(item.InternalId);
                            //Log.Warn("Process Error - ",error);
                        }
                    }
                    catch (Exception ex)
                    {
                        //We can catch any process issues that don't really affect the outcome of the process.
                        //Log.Error("Process Error:",ex.Message);
                    }
                }
            }
        }

        private async Task RemoveTextSubtitles(BaseItem item)
        {
            Log.Info("Removing Text Subtitles from Video");
            File.SetAttributes(item.Path, FileAttributes.Normal);
            var ffmpegConfiguration = FfmpegManager.FfmpegConfiguration;
            var ffmpegPath = ffmpegConfiguration.EncoderPath;
            var tempFolder = item.ContainingFolderPath;
            var ext = Path.GetExtension(item.Path);
            StringBuilder mapsb = new StringBuilder();
            
            var tempVideoPath = Path.Combine(tempFolder, Path.GetFileNameWithoutExtension(item.Path) + ".tmp");
            Log.Debug("Temporary Video File Path = {0}", tempVideoPath);

            
            Log.Debug("Remove Subtitle StringBuilder = {0}", mapsb);

            if (_subTextList != null)
            {
                try
                {
                    string Args = "-i \"" + item.Path + "\" -map 0:v -map 0:a -map -0:s -codec: copy -f matroska \"" + tempVideoPath + "\"";

                    Log.Debug($"Args={Args}");

                    Process proc = new Process();
                    proc.StartInfo.FileName = ffmpegPath;
                    proc.StartInfo.Arguments = Args;

                    //The command which will be executed
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.StartInfo.RedirectStandardOutput = false;
                    proc.StartInfo.RedirectStandardError = false;

                    //Clear LD_LIBRARY_PATH environment variable for Linux
                    proc.StartInfo.Environment["LD_LIBRARY_PATH"] = null;
                    
                    proc.Start();

                    proc.WaitForExit();
                    var exitCode = proc.ExitCode;
                    Log.Debug("Exit Code = {0}", exitCode);


                    if (exitCode == 0)
                    {
                        Log.Debug("Temporary File Successfully Created for {0}", item.Name);

                        await RenameTempFile(item, tempVideoPath, ext);

                    }

                    if (exitCode == 1 || exitCode == 2)
                    {
                        Log.Warn("Backup File FAILED to be created for {0} and exited with Code {1} - Probably already converted or no subtitles to remove", item.Name, exitCode.ToString());
                        Log.Warn("Temp file is being deleted");
                        File.Delete(tempVideoPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                }
            }
        }

        private async Task RemoveAllSubtitles(BaseItem item)
        {
            Log.Info("Removing ALL Subtitles from Video");
            var ffmpegConfiguration = FfmpegManager.FfmpegConfiguration;
            var ffmpegPath = ffmpegConfiguration.EncoderPath;
            var tempFolder = item.ContainingFolderPath;
            var ext = Path.GetExtension(item.Path);
            StringBuilder mapsb = new StringBuilder();

            var tempVideoPath = Path.Combine(tempFolder, Path.GetFileNameWithoutExtension(item.Path) + ".tmp");
            Log.Debug("Temporary Video File Path = {0}", tempVideoPath);

            try
            {
                //ffmpeg -i "C:\Transcode\input.mkv" -map 0 -c copy -sn "C:\Transcode\output.mkv"
                string Args = "-i \"" + item.Path + "\" -map 0 -c copy -sn \"" + tempVideoPath + "\"";

                Log.Debug($"Args={Args}");

                Process proc = new Process();
                proc.StartInfo.FileName = ffmpegPath;
                proc.StartInfo.Arguments = Args;

                //The command which will be executed
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.RedirectStandardOutput = false;
                proc.StartInfo.RedirectStandardError = false;
                proc.Start();

                proc.WaitForExit();
                var exitCode = proc.ExitCode;
                Log.Debug("Exit Code = {0}", exitCode);


                if (exitCode == 0)
                {
                    Log.Debug("Temporary File Successfully Created for {0}", item.Name);

                    await RenameTempFile(item, tempVideoPath, ext);
                }

                if (exitCode == 1 || exitCode == 2)
                {
                    Log.Warn("Backup File FAILED to be created for {0} and exited with Code {1} - Probably already converted or no subtitles to remove", item.Name, exitCode.ToString());
                    
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }

        }


        public async Task ProcessSubtitlesInVideo(BaseItem item)
        {
            Log.Info("Processing Graphical Subtitles in Video");
            var ffmpegConfiguration = FfmpegManager.FfmpegConfiguration;
            var ffmpegPath = ffmpegConfiguration.EncoderPath;
            //var tempFolder = GetSubKillerTempPath();
            var tempFolder = item.ContainingFolderPath;
            var ext = Path.GetExtension(item.Path);
            StringBuilder metasb = new StringBuilder();
            StringBuilder mapsb = new StringBuilder();
            int idx = 0;

            var tempVideoPath = Path.Combine(tempFolder, Path.GetFileNameWithoutExtension(item.Path) + ".tmp");
            Log.Debug("Temporary Video File Path = {0}", tempVideoPath);
            
            foreach (var storedValue in _subList)
            {
                if (_subList != null)
                {
                    mapsb.Append("-map 0:s:" + storedValue.Item2 + " ");
                    metasb.Append("-metadata:s:s:" + idx++ + " language=" + storedValue.Item1 + " ");
                }
            }
            Log.Debug("Map StringBuilder = {0}", mapsb);
            Log.Debug("Metadata StringBuilder = {0}", metasb);
            
            if (_subList != null)
            {
                try
                {
                    string Args = "-i \"" + item.Path + "\" -map 0:v -map 0:a " + mapsb + " -codec: copy -f matroska " + metasb + "\""  + tempVideoPath + "\"";

                    Log.Debug($"Args={Args}");

                    Process proc = new Process();
                    proc.StartInfo.FileName = ffmpegPath;
                    proc.StartInfo.Arguments = Args;

                    //The command which will be executed
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.StartInfo.RedirectStandardOutput = false;
                    proc.StartInfo.RedirectStandardError = false;
                    proc.Start();
                    
                    proc.WaitForExit();
                    var exitCode = proc.ExitCode;
                    Log.Debug("Exit Code = {0}", exitCode);
                    
                    
                    if (exitCode == 0)
                    {
                        Log.Debug("Temporary File Successfully Created for {0}", item.Name);
                        
                        await RenameTempFile(item, tempVideoPath, ext);
                    }

                    if (exitCode == 1 || exitCode == 2)
                    {
                        Log.Warn("Backup File FAILED to be created for {0} and exited with Code {1} - Probably already converted or no subtitles to remove", item.Name, exitCode.ToString());
                        Log.Warn("Temp file is being deleted");
                        File.Delete(tempVideoPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                }
            }
        }

        public async Task RenameTempFile(BaseItem item, string tempVideoPath, string ext)
        {
            //Check if tempfile is created
            Log.Debug("Conducting File Manipulation");
            if (!File.Exists(tempVideoPath))
            {
                Log.Warn("Temp File Does Not Exist.....");

            }
            //if its created, lets move the file to item folder, once moved and verified, delete existing file and rename current file
            else
            {
                try
                {
                    Log.Debug("File exists..... Renaming Temp File");
                    string renamedFile = Path.Combine(item.ContainingFolderPath, Path.GetFileNameWithoutExtension(item.Path) + ext);
                    Log.Debug("Renamed File Path = {0}", renamedFile);

                    var origCreationTime = File.GetLastWriteTime(item.Path);
                    File.SetCreationTime(tempVideoPath, origCreationTime);
                    Log.Debug("File Creation set to {0}", origCreationTime);
                    Thread.Sleep(2000);

                    Log.Debug("Deleting File");
                    File.Delete(item.Path);
                    
                    Thread.Sleep(2000);
                    Log.Debug("Renaming File from {0} to {1}", tempVideoPath, renamedFile);
                    File.Move(tempVideoPath, renamedFile);
                    
                }
                catch (Exception ex)
                {
                    Log.Error(ex.ToString());
                }
            }
        }

        

        public void Run()
        {
            throw new NotImplementedException();
        }
        

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}


    