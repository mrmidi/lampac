using TvClient.Models;

namespace TvClient.Services;

public class InternalApiClient
{
    readonly string _host;
    readonly string _scheme;
    readonly AuthContext _auth;

    public InternalApiClient(string host, string scheme, AuthContext auth)
    {
        _host = host;
        _scheme = scheme;
        _auth = auth;
    }

    string LocalBase => $"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}";

    List<HeadersModel> LocalHeaders()
        => HeadersModel.Init(
            ("lcrqpasswd", CoreInit.rootPasswd),
            ("xhost", _host),
            ("xscheme", _scheme)
        );

    public virtual string BuildAuthQuery()
    {
        var qs = new List<string>(5);

        void add(string key, string val)
        {
            if (!string.IsNullOrWhiteSpace(val))
                qs.Add($"{key}={HttpUtility.UrlEncode(val)}");
        }

        add("account_email", _auth?.account_email);
        add("uid", _auth?.uid);
        add("token", _auth?.token);
        add("nws_id", _auth?.nws_id);
        add("profile_id", _auth?.profile_id);

        return string.Join("&", qs);
    }

    public virtual async Task<JToken> GetJsonToken(string pathAndQuery, int timeoutSec = 15, bool statusCodeOK = true)
    {
        string url = pathAndQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathAndQuery
            : $"{LocalBase}{(pathAndQuery.StartsWith('/') ? pathAndQuery : "/" + pathAndQuery)}";

        string raw = await Http.Get(url, timeoutSeconds: timeoutSec, headers: LocalHeaders(), statusCodeOK: statusCodeOK);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JToken.Parse(raw);
        }
        catch
        {
            return null;
        }
    }

    public virtual async Task<string> GetRaw(string pathAndQuery, int timeoutSec = 15, bool statusCodeOK = true)
    {
        string url = pathAndQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathAndQuery
            : $"{LocalBase}{(pathAndQuery.StartsWith('/') ? pathAndQuery : "/" + pathAndQuery)}";

        return await Http.Get(url, timeoutSeconds: timeoutSec, headers: LocalHeaders(), statusCodeOK: statusCodeOK);
    }
}
