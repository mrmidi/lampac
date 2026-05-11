namespace TvClient;

public class ModuleConf : ModuleBaseConf
{
    public string default_lang { get; set; }

    public string[] provider_order { get; set; }

    public int queue_limit { get; set; }

    public int providers_timeout_sec { get; set; }

    public int tmdb_timeout_sec { get; set; }
}
