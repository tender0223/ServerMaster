using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using ServerMaster.Models;

namespace ServerMaster.Services
{
    /// <summary>
    /// 便携配置：整份服务器清单（含明文密码）用访问密码派生密钥加密后落盘，
    /// 文件可在任意电脑导入且无需逐台重输密码。落盘内容不含明文。
    /// 算法：PBKDF2-SHA256(100000 次) 派生 64 字节 → AES-256-CBC 加密 + HMAC-SHA256 完整性校验。
    /// </summary>
    public static class PortableConfig
    {
        public const string FormatTag = "ServerMaster-Portable";
        public const int CurrentVersion = 1;

        private const int Iters = 100000;
        private const int SaltLen = 16;
        private const int IvLen = 16;
        private const int KeyLen = 32;

        public class ServerDto
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public string Domain { get; set; }
            public string Username { get; set; }
            /// <summary>明文密码，仅存在于加密载荷内部。</summary>
            public string Password { get; set; }
            public string Group { get; set; }
            public bool ConnectToConsole { get; set; }
            public int ColorDepth { get; set; }
            public bool RedirectDrives { get; set; }
            public bool AutoReconnect { get; set; }
            public string Notes { get; set; }
            public int DesktopWidth { get; set; }
            public int DesktopHeight { get; set; }
        }

        public class Payload
        {
            public List<ServerDto> Servers { get; set; }
            public List<string> Groups { get; set; }

            public Payload()
            {
                Servers = new List<ServerDto>();
                Groups = new List<string>();
            }
        }

        public class Envelope
        {
            public string Format { get; set; }
            public int Version { get; set; }
            public string Kdf { get; set; }
            public int Iterations { get; set; }
            public string Salt { get; set; }
            public string Iv { get; set; }
            public string Cipher { get; set; }
            public string Mac { get; set; }
            public string CreatedUtc { get; set; }
            public int ServerCount { get; set; }
        }

        /// <summary>导出便携配置。无法在本机解密的密码会留空，由 unreadable 返回台数。</summary>
        public static int Export(AppConfig cfg, string path, string passphrase, out int unreadable)
        {
            Payload p = new Payload();
            p.Groups = new List<string>(cfg.Groups);
            unreadable = 0;
            foreach (ServerInfo s in cfg.Servers)
            {
                ServerDto d = new ServerDto();
                d.Id = s.Id;
                d.Name = s.Name;
                d.Host = s.Host;
                d.Port = s.Port;
                d.Domain = s.Domain;
                d.Username = s.Username;
                d.Password = PasswordProtector.Unprotect(s.EncryptedPassword);
                if (d.Password.Length == 0 && !string.IsNullOrEmpty(s.EncryptedPassword)) unreadable++;
                d.Group = s.Group;
                d.ConnectToConsole = s.ConnectToConsole;
                d.ColorDepth = s.ColorDepth;
                d.RedirectDrives = s.RedirectDrives;
                d.AutoReconnect = s.AutoReconnect;
                d.Notes = s.Notes;
                d.DesktopWidth = s.DesktopWidth;
                d.DesktopHeight = s.DesktopHeight;
                p.Servers.Add(d);
            }

            JavaScriptSerializer ser = new JavaScriptSerializer();
            byte[] inner = Encoding.UTF8.GetBytes(ser.Serialize(p));

            byte[] salt = RandomBytes(SaltLen);
            byte[] iv = RandomBytes(IvLen);
            byte[] key, macKey;
            DeriveKeys(passphrase, salt, Iters, out key, out macKey);
            try
            {
                byte[] cipher = AesEncrypt(inner, key, iv);
                byte[] mac = Mac(macKey, iv, cipher);

                Envelope env = new Envelope();
                env.Format = FormatTag;
                env.Version = CurrentVersion;
                env.Kdf = "PBKDF2-SHA256";
                env.Iterations = Iters;
                env.Salt = Convert.ToBase64String(salt);
                env.Iv = Convert.ToBase64String(iv);
                env.Cipher = Convert.ToBase64String(cipher);
                env.Mac = Convert.ToBase64String(mac);
                env.CreatedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                env.ServerCount = p.Servers.Count;

                WriteAtomic(path, ser.Serialize(env));
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(macKey, 0, macKey.Length);
                Array.Clear(inner, 0, inner.Length);
            }
            return p.Servers.Count;
        }

        public static bool IsPortableFile(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Length > 20 * 1024 * 1024) return false;
                Envelope env = new JavaScriptSerializer().Deserialize<Envelope>(File.ReadAllText(path));
                return env != null && env.Format == FormatTag;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>校验访问密码并返回服务器清单（密码保持明文，调用方需立即 DPAPI 加密）。</summary>
        public static Payload Read(string path, string passphrase)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            Envelope env;
            try
            {
                env = ser.Deserialize<Envelope>(File.ReadAllText(path));
            }
            catch
            {
                throw new InvalidDataException("文件不是有效的 ServerMaster 便携配置");
            }
            if (env == null || env.Format != FormatTag) throw new InvalidDataException("文件不是 ServerMaster 便携配置");
            if (env.Version > CurrentVersion) throw new InvalidDataException("配置文件版本较新，请升级 ServerMaster 后再导入");
            if (!string.Equals(env.Kdf, "PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("不支持的密钥派生方式: " + env.Kdf);

            byte[] salt, iv, cipher, mac;
            try
            {
                salt = Convert.FromBase64String(env.Salt);
                iv = Convert.FromBase64String(env.Iv);
                cipher = Convert.FromBase64String(env.Cipher);
                mac = Convert.FromBase64String(env.Mac);
            }
            catch
            {
                throw new InvalidDataException("文件已损坏（编码错误）");
            }
            if (salt.Length < 8 || iv.Length != IvLen || mac.Length != 32) throw new InvalidDataException("文件已损坏（参数异常）");

            int iters = env.Iterations < 10000 ? 10000 : env.Iterations;
            if (iters > 1000000) throw new InvalidDataException("文件参数异常（迭代次数过大）");

            byte[] key, macKey;
            DeriveKeys(passphrase, salt, iters, out key, out macKey);
            try
            {
                if (!FixedTimeEquals(mac, Mac(macKey, iv, cipher)))
                    throw new InvalidDataException("访问密码错误，或文件已被修改/损坏");

                byte[] plain = AesDecrypt(cipher, key, iv);
                try
                {
                    Payload p = ser.Deserialize<Payload>(Encoding.UTF8.GetString(plain));
                    if (p == null) throw new InvalidDataException("文件内容为空");
                    if (p.Servers == null) p.Servers = new List<ServerDto>();
                    if (p.Groups == null) p.Groups = new List<string>();
                    return p;
                }
                finally
                {
                    Array.Clear(plain, 0, plain.Length);
                }
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(macKey, 0, macKey.Length);
            }
        }

        /// <summary>把解密后的清单转为本地配置，密码立即用 DPAPI 重新加密。</summary>
        public static AppConfig ToLocal(Payload p, AppSettings localSettings)
        {
            AppConfig cfg = new AppConfig();
            cfg.Settings = localSettings ?? new AppSettings();
            cfg.Groups = new List<string>();
            foreach (string g in p.Groups)
            {
                string name = (g ?? "").Trim();
                if (name.Length > 0 && !cfg.Groups.Contains(name)) cfg.Groups.Add(name);
            }
            foreach (ServerDto d in p.Servers)
            {
                if (d == null || string.IsNullOrEmpty(d.Host)) continue;
                ServerInfo s = new ServerInfo();
                if (!string.IsNullOrEmpty(d.Id)) s.Id = d.Id;
                s.Name = d.Name ?? "";
                s.Host = d.Host.Trim();
                s.Port = d.Port > 0 && d.Port <= 65535 ? d.Port : 3389;
                s.Domain = d.Domain ?? "";
                s.Username = string.IsNullOrEmpty(d.Username) ? "Administrator" : d.Username;
                s.EncryptedPassword = PasswordProtector.Protect(d.Password);
                s.Group = string.IsNullOrEmpty(d.Group) ? "默认分组" : d.Group;
                s.ConnectToConsole = d.ConnectToConsole;
                s.ColorDepth = d.ColorDepth;
                s.RedirectDrives = d.RedirectDrives;
                s.AutoReconnect = d.AutoReconnect;
                s.Notes = d.Notes ?? "";
                s.DesktopWidth = d.DesktopWidth;
                s.DesktopHeight = d.DesktopHeight;
                cfg.Servers.Add(s);
                if (!cfg.Groups.Contains(s.Group)) cfg.Groups.Add(s.Group);
            }
            if (cfg.Groups.Count == 0) cfg.Groups.Add("默认分组");
            return cfg;
        }

        private static void DeriveKeys(string passphrase, byte[] salt, int iterations, out byte[] key, out byte[] macKey)
        {
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256))
            {
                byte[] material = kdf.GetBytes(KeyLen * 2);
                key = new byte[KeyLen];
                macKey = new byte[KeyLen];
                Buffer.BlockCopy(material, 0, key, 0, KeyLen);
                Buffer.BlockCopy(material, KeyLen, macKey, 0, KeyLen);
                Array.Clear(material, 0, material.Length);
            }
        }

        private static byte[] AesEncrypt(byte[] data, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (ICryptoTransform t = aes.CreateEncryptor())
                    return t.TransformFinalBlock(data, 0, data.Length);
            }
        }

        private static byte[] AesDecrypt(byte[] data, byte[] key, byte[] iv)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (ICryptoTransform t = aes.CreateDecryptor())
                    return t.TransformFinalBlock(data, 0, data.Length);
            }
        }

        private static byte[] Mac(byte[] macKey, byte[] iv, byte[] cipher)
        {
            byte[] buf = new byte[iv.Length + cipher.Length];
            Buffer.BlockCopy(iv, 0, buf, 0, iv.Length);
            Buffer.BlockCopy(cipher, 0, buf, iv.Length, cipher.Length);
            using (HMACSHA256 h = new HMACSHA256(macKey))
                return h.ComputeHash(buf);
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] RandomBytes(int len)
        {
            byte[] b = new byte[len];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        private static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
