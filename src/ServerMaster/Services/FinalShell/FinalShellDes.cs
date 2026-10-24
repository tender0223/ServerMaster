using System;
using System.Security.Cryptography;
using System.Text;

namespace ServerMaster.Services.FinalShell
{
    /// <summary>
    /// FinalShell 存储密码解密。兼容两代算法（候选密钥依次尝试，首个解密成功者胜出）：
    ///
    /// ① 4.x（当前主流，密钥派生复杂）：
    ///    ks   = 3680984568597093857 / Random(head[5]符号扩展).nextInt(127)
    ///    随机序列: Random(ks) 先丢弃 head[0](符号扩展) 次 nextLong，再取一个 nextLong 得 n
    ///    Random(n) 取两个 nextLong，与 head[4]/head[7]/head[3]/head[1]/head[2]（符号扩展）
    ///    及 random 的下一个 nextLong 交错排成 8 个 long，按 Java DataOutputStream 大端序
    ///    写成 64 字节，MD5 后取前 8 字节作为 DES 密钥（视 JDK 实现可能需奇偶校验位修正，
    ///    两种候选都尝试）。
    /// ② 旧版（≤3.x）：密钥 = Random(头8字节大端long).nextBytes(8)。
    ///
    /// 密文统一为 Base64( 8字节头 + DES-ECB-PKCS5(密码) )，旧版可能带 "DES:" 前缀。
    /// 仅用于导入用户自己的连接数据；解出的密码立即以 DPAPI 重新加密落盘。
    /// 全部候选失败返回 null，由调用方标记"需手动录入"。
    /// </summary>
    public static class FinalShellDes
    {
        public static string Decode(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            string s = cipher.Trim();
            if (s.StartsWith("DES:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4).Trim();
            if (s.Length == 0) return "";

            byte[] buf = NormalizeBase64(s);
            if (buf == null || buf.Length <= 8) return null;

            byte[] head = new byte[8];
            Array.Copy(buf, head, 8);
            byte[] body = new byte[buf.Length - 8];
            Array.Copy(buf, 8, body, 0, body.Length);

            byte[] plain = TryDecrypt(body, DeriveKey4(head, false));
            if (plain == null) plain = TryDecrypt(body, DeriveKey4(head, true));
            if (plain == null) plain = TryDecrypt(body, DeriveKeyClassic(head));
            if (plain == null) return null;

            string pwd = Encoding.UTF8.GetString(plain).TrimEnd('\0');
            // 解出不可打印乱码 → 所有候选都不匹配，视为失败
            foreach (char c in pwd)
                if (c < 0x20 && c != '\t') return null;
            return pwd;
        }

        private static byte[] TryDecrypt(byte[] body, byte[] key)
        {
            if (key == null) return null;
            try
            {
                using (DES des = DES.Create())
                {
                    des.Mode = CipherMode.ECB;
                    des.Padding = PaddingMode.PKCS7;
                    des.Key = key;
                    using (ICryptoTransform dec = des.CreateDecryptor())
                        return dec.TransformFinalBlock(body, 0, body.Length);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>FinalShell 4.x 密钥派生（对应其客户端的 ranDomKey 逻辑）。</summary>
        private static byte[] DeriveKey4(byte[] head, bool fixParity)
        {
            JavaRandom rSeed = new JavaRandom((sbyte)head[5]);
            int div = rSeed.NextInt(127);
            if (div == 0) return null; // Java 侧会除零；视为该候选失败
            long ks = 3680984568597093857L / div;

            JavaRandom random = new JavaRandom(ks);
            int t = (sbyte)head[0];
            for (int i = 0; i < t; i++) random.NextLong();
            long n = random.NextLong();

            JavaRandom r2 = new JavaRandom(n);
            long[] ld = new long[]
            {
                (sbyte)head[4], r2.NextLong(), (sbyte)head[7], (sbyte)head[3],
                r2.NextLong(), (sbyte)head[1], random.NextLong(), (sbyte)head[2]
            };

            byte[] buf = new byte[64];
            for (int i = 0; i < 8; i++)
                WriteLongBE(buf, i * 8, ld[i]);

            byte[] key16;
            using (MD5 md5 = MD5.Create())
                key16 = md5.ComputeHash(buf);

            byte[] key = new byte[8];
            Array.Copy(key16, key, 8);
            return fixParity ? FixDesParity(key) : key;
        }

        /// <summary>旧版（≤3.x）密钥派生。</summary>
        private static byte[] DeriveKeyClassic(byte[] head)
        {
            long seed = 0;
            foreach (byte b in head) seed = unchecked((seed << 8) | b);
            JavaRandom rnd = new JavaRandom(seed);
            byte[] key = new byte[8];
            rnd.NextBytes(key);
            return key;
        }

        /// <summary>DES 每字节低位置为奇校验（JDK SecretKeyFactory 路径可能做的修正）。</summary>
        private static byte[] FixDesParity(byte[] key)
        {
            byte[] r = (byte[])key.Clone();
            for (int i = 0; i < r.Length; i++)
            {
                int bits = 0;
                for (int b = 1; b < 8; b++)
                    if ((r[i] & (1 << b)) != 0) bits++;
                if (bits % 2 == 0) r[i] |= 1;
                else r[i] &= 0xFE;
            }
            return r;
        }

        /// <summary>Java DataOutputStream.writeLong：64 位大端序。</summary>
        private static void WriteLongBE(byte[] buf, int offset, long v)
        {
            for (int b = 0; b < 8; b++)
                buf[offset + b] = (byte)((ulong)v >> (56 - b * 8));
        }

        private static byte[] NormalizeBase64(string s)
        {
            // 兼容标准/URL-safe 两种 Base64 及缺省填充
            string b64 = s.Replace('-', '+').Replace('_', '/')
                          .Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();
            if (b64.Length == 0) return null;
            int rem = b64.Length % 4;
            if (rem == 1) return null;
            if (rem == 2) b64 += "==";
            else if (rem == 3) b64 += "=";
            try { return Convert.FromBase64String(b64); }
            catch { return null; }
        }
    }

    /// <summary>java.util.Random 的等价实现（48 位 LCG）。</summary>
    internal class JavaRandom
    {
        private const long Multiplier = 0x5DEECE66DL;
        private const long Addend = 0xBL;
        private const long Mask = (1L << 48) - 1;

        private long _seed;

        public JavaRandom(long seed)
        {
            _seed = (seed ^ Multiplier) & Mask;
        }

        private int Next(int bits)
        {
            _seed = (_seed * Multiplier + Addend) & Mask;
            return (int)(_seed >> (48 - bits));
        }

        /// <summary>Java nextInt(bound)。bound 非二次幂时等价于 next(31) % bound。</summary>
        public int NextInt(int bound)
        {
            int r = Next(31);
            int m = bound - 1;
            if ((bound & m) == 0)
                return (int)(((long)bound * r) >> 31);
            return r % bound;
        }

        /// <summary>Java nextLong：((long)next(32) << 32) + next(32)（两个 int 均符号扩展）。</summary>
        public long NextLong()
        {
            return ((long)Next(32) << 32) + Next(32);
        }

        public void NextBytes(byte[] buf)
        {
            for (int i = 0; i < buf.Length; )
            {
                int rnd = Next(32);
                int n = Math.Min(buf.Length - i, 4);
                while (n-- > 0)
                {
                    buf[i++] = (byte)rnd;
                    rnd >>= 8;
                }
            }
        }
    }
}
