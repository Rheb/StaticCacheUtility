namespace StaticCacheUtility;

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
    /// Path where the cache can store a serialized file containing the loaded data model.
    /// Default: <see cref="string.Empty"/> => No serialization.
    /// </summary>
    public string SerializedDataSavePath { get; set; } = string.Empty;

    /// <summary>
    /// How long the serialized data is considered valied to load at startup.
    /// Default: 1 day.
    /// </summary>
    public TimeSpan SerializedFileMaxAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Custom error logger. Default: null => inactive.
    /// </summary>
    public Func<string, Task>? LogError { get; set; } = null;

    /// <summary>
    /// Friendly startup info logs. Default: null => inactive.
    /// </summary>
    public Func<string, Task>? LogStartup { get; set; } = null;
}
