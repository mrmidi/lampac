using Shared.Models.AppConf;

namespace TvClient;

public class ModInit : IModuleLoaded
{
    public static string modpath;
    public static ModuleConf conf;

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;

        updateConf();
        EventListener.UpdateInitFile += updateConf;

        if (conf?.limit_map != null)
        {
            foreach (var m in conf.limit_map)
                CoreInit.conf.WAF.limit_map.Insert(0, m);
        }
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("TvClient", new ModuleConf()
        {
            default_lang = "en-US",
            provider_order = new[] { "phantom", "kinobase", "zetflix", "hdvb", "kinogo" },
            queue_limit = 15,
            providers_timeout_sec = 15,
            tmdb_timeout_sec = 15,
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/api/tv/v1", new WafLimitMap { limit = 20, second = 1 })
            }
        });
    }
}
