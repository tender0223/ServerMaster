using System;

namespace ServerMaster.Services
{
    /// <summary>
    /// "host:port" / "[ipv6]:port" / "[ipv6]" 解析。
    /// 用户习惯把端口一起填进主机栏；mstscax 的 Server 属性不接受带端口的
    /// 字符串（E_INVALIDARG），必须拆分后单独设置 RDPPort。
    /// </summary>
    public static class HostPort
    {
        /// <summary>拆出纯主机名；若携带数值端口则经 port 返回并返回 true。</summary>
        public static bool Split(string raw, out string host, out int port)
        {
            host = raw == null ? "" : raw.Trim();
            port = 0;
            if (host.Length == 0) return false;

            // [ipv6]:port 或 [ipv6]
            if (host[0] == '[')
            {
                int close = host.IndexOf(']');
                if (close > 0)
                {
                    string inner = host.Substring(1, close - 1);
                    if (host.Length > close + 2 && host[close + 1] == ':')
                    {
                        int p;
                        if (int.TryParse(host.Substring(close + 2), out p)) { host = inner; port = p; return true; }
                    }
                    host = inner;
                    return false;
                }
            }

            // 恰好一个冒号且冒号后为数字 → host:port（IPv4/域名；IPv6 裸地址有多个冒号不会误伤）
            int colon = host.IndexOf(':');
            if (colon > 0 && host.IndexOf(':', colon + 1) < 0)
            {
                int p;
                if (int.TryParse(host.Substring(colon + 1), out p)) { host = host.Substring(0, colon); port = p; return true; }
            }
            return false;
        }
    }
}
