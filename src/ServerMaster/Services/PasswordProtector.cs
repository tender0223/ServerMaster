using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ServerMaster.Services
{
    /// <summary>
    /// 密码加密：Windows DPAPI（CurrentUser 作用域 + 应用熵）。
    /// 密文仅本机当前 Windows 账户可解，导出配置到其他机器需重新录入密码。
    /// </summary>
    public static class PasswordProtector
    {
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ServerMaster.v1.Password");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn, string szDataDescr, ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] protectedBytes = ProtectBytes(Encoding.UTF8.GetBytes(plain), _entropy);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            try
            {
                byte[] plain = UnprotectBytes(Convert.FromBase64String(cipher), _entropy);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }

        private static byte[] ProtectBytes(byte[] data, byte[] entropy)
        {
            DATA_BLOB inBlob = BlobFromBytes(data);
            DATA_BLOB entBlob = BlobFromBytes(entropy);
            DATA_BLOB outBlob = new DATA_BLOB();
            try
            {
                if (!CryptProtectData(ref inBlob, "ServerMaster", ref entBlob, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
                    throw new InvalidOperationException("CryptProtectData 失败: " + Marshal.GetLastWin32Error());
                return BytesFromBlob(outBlob);
            }
            finally
            {
                LocalFree(inBlob.pbData);
                LocalFree(entBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private static byte[] UnprotectBytes(byte[] data, byte[] entropy)
        {
            DATA_BLOB inBlob = BlobFromBytes(data);
            DATA_BLOB entBlob = BlobFromBytes(entropy);
            DATA_BLOB outBlob = new DATA_BLOB();
            try
            {
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
                    throw new InvalidOperationException("CryptUnprotectData 失败: " + Marshal.GetLastWin32Error());
                return BytesFromBlob(outBlob);
            }
            finally
            {
                LocalFree(inBlob.pbData);
                LocalFree(entBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private static DATA_BLOB BlobFromBytes(byte[] data)
        {
            DATA_BLOB b = new DATA_BLOB();
            b.cbData = data.Length;
            b.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, b.pbData, data.Length);
            return b;
        }

        private static byte[] BytesFromBlob(DATA_BLOB b)
        {
            byte[] arr = new byte[b.cbData];
            if (b.cbData > 0) Marshal.Copy(b.pbData, arr, 0, b.cbData);
            return arr;
        }
    }
}
