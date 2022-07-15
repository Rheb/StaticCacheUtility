using Binaron.Serializer;

namespace StaticCacheUtility;

public class DataCache<TData>
    where TData : class, new()
{
    #region Properties and constructor

    private DataCacheSettings Settings { get; set; }
    private Func<Task<TData>> LoadData { get; set; }
    private CacheStatus Status { get; set; } = CacheStatus.NotStarted;
    private TrackerModel Tracker { get; set; } = new TrackerModel();
    private bool SerializationEnabled { get; set; } = true;

    private static int MinDelayMs_100 { get; } = 100;
    private string MessageHeader { get; }
    private string SerializedDataFileName { get; }
    private string Temporary_SerializedDataFileName { get; }

    public DataCache(DataCacheSettings settings, Func<Task<TData>> loadData)
    {
        Settings = settings;
        LoadData = loadData;

        string dataType = typeof(TData).ToString();

        MessageHeader = $"{nameof(DataCache<TData>)}_{dataType.Split('.').Last()} => ";

        string fileName = $"{dataType.Replace(".", "_")}.bin";

        SerializedDataFileName = Path.Combine(settings.SerializedDataSavePath, fileName);
        Temporary_SerializedDataFileName = Path.Combine(Settings.SerializedDataSavePath, $"TEMP_{fileName}");
    }

    #endregion

    #region Private tracker model

    private enum CacheStatus
    {
        NotStarted = 1,
        FirstTimeLoading = 2,
        Active = 3,
    }

    private class TrackerModel
    {
        public TData Data { get; set; } = new TData();
        public DateTime ReloadByUtc { get; set; } = DateTime.UtcNow;
    }

    #endregion

    #region Public access

    /// <summary>
    /// The cached data model as provided by the LoadData() call.
    /// </summary>
    public TData Data => Tracker.Data;

    /// <summary>
    /// True once the first first LoadData() call has completed.
    /// </summary>
    public bool IsActive => Status == CacheStatus.Active;

    /// <summary>
    /// Starts the cache as a background task, but awaits the first LoadData() call.
    /// </summary>
    public async Task Start()
    {
        if (Status == CacheStatus.NotStarted)
        {
            Status = CacheStatus.FirstTimeLoading;

            await LogStartup($"{nameof(CacheUpdateLoop)} starting.");

            DateTime loadingStartedUtc = DateTime.UtcNow;
            Tracker.ReloadByUtc = loadingStartedUtc;

            _ = CacheUpdateLoop();

            await VerifyLoadDataSuccessOrTimeout(loadingStartedUtc, nameof(Start));
        }
    }

    /// <summary>
    /// Trigger a LoadData() call in the backround thread and await completion.
    /// </summary>
    public async Task Reload()
    {
        DateTime loadingStartedUtc = DateTime.UtcNow;
        Tracker.ReloadByUtc = loadingStartedUtc;

        await VerifyLoadDataSuccessOrTimeout(loadingStartedUtc, nameof(Reload));
    }

    /// <summary>
    /// Trigger a LoadData() call in the backround thread (without awaiting).
    /// </summary>
    public void RequestBackgroundReload()
    {
        Tracker.ReloadByUtc = DateTime.UtcNow;
    }

    #endregion

    #region VerifyLoadDataSuccessOrTimeout

    private async Task VerifyLoadDataSuccessOrTimeout(DateTime loadingStartedUtc, string functionName)
    {
        int totalDelayMs = 0;

        while (
            Tracker.ReloadByUtc <= loadingStartedUtc
            && totalDelayMs < Settings.DataLoadingTimeout.TotalMilliseconds
        )
        {
            await Task.Delay(MinDelayMs_100);
            totalDelayMs += MinDelayMs_100;
        }

        if (Tracker.ReloadByUtc <= loadingStartedUtc)
        {
            await LogError($"Timeout of {functionName}: Cache failed to load data in {Settings.DataLoadingTimeout.TotalSeconds} sec");
        }
    }

    #endregion

    #region CacheUpdateLoop

    private async Task CacheUpdateLoop()
    {
        while (true)
        {
            // Do work safely
            try
            {
                // Fetch Data
                if (
                    Status == CacheStatus.FirstTimeLoading
                 && SerializationEnabled
                 && !string.IsNullOrWhiteSpace(Settings.SerializedDataSavePath)
                )
                {
                    await SafeLoadSerializedFile();
                }
                else if (Tracker.ReloadByUtc <= DateTime.UtcNow)
                {
                    await RefreshTrackerAndLoadData();
                }

                if (Status == CacheStatus.FirstTimeLoading)
                {
                    Status = CacheStatus.Active;
                }
            }
            catch (Exception e)
            {
                await LogError($"{nameof(CacheUpdateLoop)} - {e}");
            }

            // Always delay next iteration by min delay value
            await Task.Delay(MinDelayMs_100);
        }
    }

    private async Task RefreshTrackerAndLoadData()
    {
        TData newData = await LoadData();

        Tracker = new TrackerModel
        {
            Data = newData,
            ReloadByUtc = DateTime.UtcNow + Settings.ReloadInterval,
        };

        if (Status == CacheStatus.FirstTimeLoading)
        {
            await LogStartup($"{nameof(LoadData)} completed.");
        }

        // Save Instance as .bin if it is serializable
        if (
            SerializationEnabled
         && !string.IsNullOrWhiteSpace(Settings.SerializedDataSavePath)
        )
        {
            await SafeSaveAsSerializedFile();
        }
    }

    private async Task SafeSaveAsSerializedFile()
    {
        try
        {
            // Ensure folder exist
            Directory.CreateDirectory(Settings.SerializedDataSavePath);

            {
                // Write to Temp file first (safer if corruption error occurs)
                using FileStream stream = File.OpenWrite(Temporary_SerializedDataFileName);
                BinaronConvert.Serialize(Tracker, stream);
            }

            // Move to overwrite the main file
            File.Move(
                sourceFileName: Temporary_SerializedDataFileName,
                destFileName: SerializedDataFileName,
                overwrite: true
            );
        }
        catch (Exception e)
        {
            await LogError($"{nameof(SafeSaveAsSerializedFile)} - {e}");
        }
    }

    private async Task SafeLoadSerializedFile()
    {
        bool loadSuccess = false;

        try
        {
            if (File.Exists(SerializedDataFileName))
            {
                using FileStream stream = File.OpenRead(SerializedDataFileName);

                TrackerModel? loadedTracker = BinaronConvert.Deserialize<TrackerModel?>(stream);

                DateTime minReloadByUtc = DateTime.UtcNow - Settings.SerializedFileMaxAge;

                if (
                    loadedTracker != null
                    && loadedTracker.ReloadByUtc > minReloadByUtc // Model age ok
                )
                {
                    loadSuccess = true;
                    Tracker = loadedTracker;

                    // Always reload cached data on next background thread iteration when loading from file
                    Tracker.ReloadByUtc = DateTime.UtcNow.AddMilliseconds(MinDelayMs_100);

                    await LogStartup($"Load serialized file completed.");
                }
            }
        }
        catch (Exception e)
        {
            await LogError($"{nameof(SafeLoadSerializedFile)} - {e}");
        }

        if (!loadSuccess)
        {
            // Attempt regular cahce load
            await RefreshTrackerAndLoadData();
        }
    }

    #endregion

    #region Log utility

    private async Task LogStartup(string message)
    {
        try
        {
            if (Settings.LogStartup != null)
            {
                await Settings.LogStartup($"{MessageHeader}{message}");
            }
        }
        catch (Exception e)
        {
            await LogError($"Logging failed: {message} -- {e}");
        }
    }

    private async Task LogError(string error)
    {
        try
        {
            if (Settings.LogError != null)
            {
                await Settings.LogError($"Cache ERROR: {MessageHeader}{error}");
            }
        }
        catch { /* We have caught an error and LogError failed, nothing we can do here */ }
    }

    #endregion
}
