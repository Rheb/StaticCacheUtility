# StaticCacheUtility

Usage examples:

```sh
static async Task Main()
{
    await Cache.Basic.Start();
    // ... run application with Cache.Dasic.Data always available
}
```

```sh
public static class Cache
{
    public static DataCache<BasicDataModel> Basic { get; } = new DataCache<BasicDataModel>(
        p_Settings: BasicDataLoader.Settings,
        p_LoadData: BasicDataLoader.LoadBasicData
    );
}
```

```sh
public static class BasicDataLoader
{
    public static DataCacheSettings Settings => new DataCacheSettings
    {
        ReloadInterval = TimeSpan.FromMinutes(5),
        DataLoadingTimeout = TimeSpan.FromMinutes(30),
        DataModelVersionNr = 1,
        SerializedFileMaxAge = TimeSpan.FromDays(1),
        SerializedDataSavePath = $"{AppDomain.CurrentDomain.BaseDirectory}/cache-serialized-data/",
        LogError = MyLogger.LogError,
        LogStartup = MyLogger.LogMessage,
    };

    public static async Task<BasicDataModel> LoadBasicData()
    {
        BasicDataModel data = new();
        // ... async load lots of data
        return data;
    }
}
```

## License

MIT