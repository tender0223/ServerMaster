using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace ServerMaster.Services.FinalShell
{
    /// <summary>一条 FinalShell 连接（解析结果）。</summary>
    public class FinalShellEntry
    {
        public string Name;
        public string Host;
        public int Port;
        public string Username;
        /// <summary>解密后的密码；""=无密码；null=密文存在但解密失败(需手动录入)。</summary>
        public string PasswordDecoded;
        public bool HasPassword;
        public string Protocol;
        public string Description;
        /// <summary>来自 FinalShell 目录/分组节点的分组名；空=无。</summary>
        public string SourceGroup;
    }

    /// <summary>
    /// 解析 FinalShell 连接数据。支持两种来源：
    ///  ① FinalShell「导出连接」生成的 .json 文件（数组/单对象/带分组的嵌套结构均可）；
    ///  ② FinalShell 的 conn 数据目录（每个连接一个 .json，分组名从各级 folder.json 读取——
    ///     4.x 的子目录名是内部 ID，不是分组名）。
    /// 字段容错：4.x 用 user_name/conection_type，旧版用 username/protocol；未知字段忽略。
    /// </summary>
    public static class FinalShellImporter
    {
        public static List<FinalShellEntry> ParseFile(string path)
        {
            return ParseJson(File.ReadAllText(path), "");
        }

        public static List<FinalShellEntry> ParseDirectory(string dir)
        {
            List<FinalShellEntry> all = new List<FinalShellEntry>();
            string root = Path.GetFullPath(dir).TrimEnd('\\', '/');
            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals("folder.json", StringComparison.OrdinalIgnoreCase))
                    continue; // 分组节点本身不是连接
                string full = Path.GetDirectoryName(Path.GetFullPath(file));
                string rel = full != null && full.Length > root.Length
                    ? full.Substring(root.Length).TrimStart('\\', '/')
                    : "";
                try
                {
                    all.AddRange(ParseJson(File.ReadAllText(file), ResolveGroupName(full, root, rel)));
                }
                catch
                {
                    // 单个文件损坏不影响整体导入
                }
            }
            return all;
        }

        /// <summary>分组目录名是内部 ID；逐级读 folder.json 换成用户可见的分组名。</summary>
        private static string ResolveGroupName(string fullDir, string root, string rel)
        {
            if (rel.Length == 0) return "";
            string[] parts = rel.Split('\\', '/');
            List<string> names = new List<string>();
            string cur = root;
            foreach (string p in parts)
            {
                cur = Path.Combine(cur, p);
                string name = "";
                string fj = Path.Combine(cur, "folder.json");
                if (File.Exists(fj))
                {
                    try
                    {
                        JavaScriptSerializer ser = new JavaScriptSerializer();
                        IDictionary<string, object> d = ser.DeserializeObject(File.ReadAllText(fj))
                            as IDictionary<string, object>;
                        if (d != null)
                        {
                            object v;
                            if (d.TryGetValue("name", out v) && v != null) name = v.ToString().Trim();
                        }
                    }
                    catch { }
                }
                names.Add(name.Length > 0 ? name : p);
            }
            return string.Join("\\", names);
        }

        public static List<FinalShellEntry> ParseJson(string json, string defaultGroup)
        {
            List<FinalShellEntry> list = new List<FinalShellEntry>();
            if (string.IsNullOrEmpty(json)) return list;
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            object root;
            try { root = ser.DeserializeObject(json); }
            catch { return list; }
            Collect(root, defaultGroup, list);
            return list;
        }

        /// <summary>递归收集连接条目：数组展开、包装对象展开、无 host 的分组节点向下钻取。</summary>
        private static void Collect(object node, string group, List<FinalShellEntry> list)
        {
            IDictionary<string, object> dict = node as IDictionary<string, object>;
            if (dict != null)
            {
                string host = GetStr(dict, "host").Trim();
                if (host.Length > 0)
                {
                    list.Add(ToEntry(dict, group));
                    return;
                }
                // 分组/包装节点：其 name 作为子节点的分组名
                string childGroup = GetStr(dict, "name").Trim();
                if (childGroup.Length == 0) childGroup = group;
                foreach (object v in dict.Values) Collect(v, childGroup, list);
                return;
            }

            object[] arr = node as object[];
            if (arr != null)
            {
                foreach (object v in arr) Collect(v, group, list);
            }
        }

        private static FinalShellEntry ToEntry(IDictionary<string, object> d, string group)
        {
            FinalShellEntry e = new FinalShellEntry();
            e.Host = GetStr(d, "host").Trim();
            e.Port = GetInt(d, "port", 0);
            // 4.x 字段是 user_name，旧版/导出可能是 username
            e.Username = GetStr(d, "user_name").Trim();
            if (e.Username.Length == 0) e.Username = GetStr(d, "username").Trim();
            e.Name = GetStr(d, "name").Trim();
            if (e.Name.Length == 0) e.Name = e.Host;
            e.Protocol = MapProtocol(d);
            e.Description = GetStr(d, "description").Trim();
            e.SourceGroup = group == null ? "" : group;
            string cipher = GetStr(d, "password");
            e.HasPassword = cipher.Length > 0;
            e.PasswordDecoded = e.HasPassword ? FinalShellDes.Decode(cipher) : "";
            return e;
        }

        /// <summary>协议识别：4.x 无 protocol 字段，用 conection_type 数字（101=RDP）。</summary>
        private static string MapProtocol(IDictionary<string, object> d)
        {
            string proto = GetStr(d, "protocol").Trim();
            if (proto.Length > 0) return proto.ToUpperInvariant();
            int ct = GetInt(d, "conection_type", 0);
            if (ct == 101) return "RDP";
            if (ct == 0) return "";
            return "TYPE" + ct;
        }

        private static string GetStr(IDictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return "";
            return v.ToString();
        }

        private static int GetInt(IDictionary<string, object> d, string key, int def)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            try
            {
                return Convert.ToInt32(Convert.ToDouble(v, CultureInfo.InvariantCulture));
            }
            catch
            {
                return def;
            }
        }
    }
}
