# StaticCacheUtility

Usage examples:

```sh
static async Task Main()
{
    await Cache.Basic.Start();
    // ... Cache.Basic.Data will now be available across the application
}
```

```sh
public static class BasicDataModel
{
    // ... any data properties you want cached
}
```

```sh
public static class Cache
{
    public static DataCache<BasicDataModel> Basic { get; } = new DataCache<BasicDataModel>(
        settings: BasicDataLoader.Settings,
        loadData: BasicDataLoader.LoadBasicData
    );
}
```

```sh
public static class BasicDataLoader
{
#if DEBUG
    private static string SerializedBasicDataPath { get; } = $"{AppDomain.CurrentDomain.BaseDirectory}/cache-serialized-data/";
#else
    private static string SerializedBasicDataPath { get; } = string.Empty;
#endif

    public static DataCacheSettings Settings => new DataCacheSettings
    {
        ReloadInterval = TimeSpan.FromMinutes(5),
        DataLoadingTimeout = TimeSpan.FromMinutes(30),
        SerializedFileMaxAge = TimeSpan.FromDays(1),
        SerializedDataSavePath = SerializedBasicDataPath,
        LogError = MyLogger.LogError,
        LogStartup = MyLogger.LogMessage,
    };

    public static async Task<BasicDataModel> LoadBasicData()
    {
        BasicDataModel data = new();
        // ... await loading of data properties
        return data;
    }
}
```

## License

MIT