using Binaron.Serializer;

namespace StaticCacheUtility;

public class DataCache<TData>
    where TData : class, new()
{
    #region Private props and constructor

    private DataCacheSettings Settings { get; set; }
    private Func<Task<TData>> LoadData { get; set; }
    private CacheStatus Status { get; set; } = CacheStatus.NotStarted;
    private TrackerModel Tracker { get; set; } = new TrackerModel();
    private bool SerializationEnabled { get; set; } = true;

    private static int MinDelayMs_100 { get; } = 100;
    private string MessageHeader { get; }
    private string SerializedDataFileName { get; }
    private string Temporary_SerializedDataFileName { get; }

    public DataCache(DataCacheSettings p_Settings, Func<Task<TData>> p_LoadData)
    {
        Settings = p_Settings;
        LoadData = p_LoadData;

        string dataType = typeof(TData).ToString();

        MessageHeader = $"{nameof(DataCache<TData>)}_{dataType.Split('.').Last()} => ";

        string fileName = $"{dataType.Replace(".", "_")}.bin";

        SerializedDataFileName = Path.Combine(p_Settings.SerializedDataSavePath, fileName);
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
        public int DataModelVersionNr { get; set; } = 1;
    }

    #endregion

    #region Public access

    public TData Data => Tracker.Data;
    public bool IsActive => Status == CacheStatus.Active;

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

    public async Task StartWithoutSerialization()
    {
        SerializationEnabled = false;
        await Start();
    }

    public async Task Reload()
    {
        DateTime loadingStartedUtc = DateTime.UtcNow;
        Tracker.ReloadByUtc = loadingStartedUtc;

        await VerifyLoadDataSuccessOrTimeout(loadingStartedUtc, nameof(Reload));
    }

    public void RequestBackgroundReload()
    {
        Tracker.ReloadByUtc = DateTime.UtcNow;
    }

    #endregion

    #region VerifyLoadDataSuccessOrTimeout

    private async Task VerifyLoadDataSuccessOrTimeout(DateTime p_LoadingStartedUtc, string p_FunctionName)
    {
        int totalDelayMs = 0;

        while (
            Tracker.ReloadByUtc <= p_LoadingStartedUtc
            && totalDelayMs < Settings.DataLoadingTimeout.TotalMilliseconds
        )
        {
            await Task.Delay(MinDelayMs_100);
            totalDelayMs += MinDelayMs_100;
        }

        if (Tracker.ReloadByUtc <= p_LoadingStartedUtc)
        {
            await LogError($"Timeout of {p_FunctionName}: Cache failed to load data in {Settings.DataLoadingTimeout.TotalSeconds} sec");
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
            DataModelVersionNr = Settings.DataModelVersionNr,
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
                    && loadedTracker.DataModelVersionNr == Settings.DataModelVersionNr // Model structure ok
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
        catch { /* Provided error logger failed... nothing we can do here */ }
    }

    #endregion
}

#region DataCacheSettings

public class DataCacheSettings
{
    /// <summary>
    /// Interval between await LoadData() calls.
    /// Default: 60 min.
    /// </summary>
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// If this timeout is reached the cache will stop waiting for LoadData() and log an error instead.
    /// Default: 15 min.
    /// </summary>
    public TimeSpan DataLoadingTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// When up ticked the cache will reject the current serialized model and refresh instead.
    /// </summary>
    public int DataModelVersionNr { get; set; } = 1;

    /// <summary>
    /// Path where the cache can store a serialized file copy of the latest data.
    /// Default: no serialization (empty string).
    /// </summary>
    public string SerializedDataSavePath { get; set; } = "";

    /// <summary>
    /// How long the serialized data is considered valied to load at startup.
    /// Default: 1 day.
    /// </summary>
    public TimeSpan SerializedFileMaxAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Custom error logger. Default: inactive.
    /// </summary>
    public Func<string, Task>? LogError { get; set; } = null;

    /// <summary>
    /// Friendly startup info logs. Default: inactive.
    /// </summary>
    public Func<string, Task>? LogStartup { get; set; } = null;
}

#endregion
