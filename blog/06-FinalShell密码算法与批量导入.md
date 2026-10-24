# 06 · FinalShell 密码算法与批量导入

> 《从零写一个多服务器 RDP 批量管理工具》第 6 篇。[系列目录](00-系列总览.md)

用户手里有几十台服务器都在 FinalShell 里管着，要求把连接清单（含密码、分组）一键迁移过来。这个需求最后演变成整个项目里最有侦探故事色彩的一篇。

## 第一版：照公开资料实现，全灭

网上广泛流传的 FinalShell 密码算法（旧版 ≤3.x）：

```
密文 = Base64( 8字节随机头 + DES-ECB-PKCS5(密码, 密钥) )
密钥 = java.util.Random(头8字节按大端组成long).nextBytes(8)
```

C# 复刻要点是等价实现 Java 的 `Random`（48 位 LCG）：

```csharp
internal class JavaRandom   // java.util.Random 等价实现
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1;

    public JavaRandom(long seed) { _seed = (seed ^ Multiplier) & Mask; }

    private int Next(int bits)
    {
        _seed = (_seed * Multiplier + Addend) & Mask;
        return (int)(_seed >> (48 - bits));
    }

    public long NextLong()   // ((long)next(32) << 32) + next(32)，两个 int 都符号扩展
    {
        return ((long)Next(32) << 32) + Next(32);
    }
}
```

自测用"独立实现的加密侧"做回环，全部通过。结果用户一导入：**全部显示"需手动录入"**——解密一个都没成功。

## 转折：解密素材就在用户磁盘上

排查时在用户 FinalShell 安装目录里发现两个文件：`EncryptPass.java` 和 `VerifyPass.java`——**正是这个版本加密算法的完整 Java 源码**（用户此前折腾时留下的），甚至自带真实测试向量：

```java
static byte[] ranDomKey(byte[] head) {
    long ks = 3680984568597093857L / (long)(new Random((long)head[5])).nextInt(127);
    Random random = new Random(ks);
    int t = head[0];
    for (int i = 0; i < t; ++i) random.nextLong();
    long n = random.nextLong();
    Random r2 = new Random(n);
    long[] ld = new long[]{ (long)head[4], r2.nextLong(), (long)head[7], (long)head[3],
                            r2.nextLong(), (long)head[1], random.nextLong(), (long)head[2] };
    // 8 个 long 按 Java DataOutputStream.writeLong 大端序写 64 字节
    // → MD5 → 前 8 字节 = DES 密钥
}
```

这是 FinalShell **4.x 的新算法**，密钥派生比旧版复杂得多：除法、多次 Random 派生、交错取值、再过一遍 MD5。

## C# 复刻的三个细节

1. **`nextInt(127)` 的等价**：127 不是 2 的幂，Java 走通用路径，且循环条件恒不成立，等价于 `next(31) % 127`
2. **`(long)head[N]` 的符号扩展**：Java byte → long 是符号扩展，C# 要 `(sbyte)` 转换
3. **密钥校验位的不确定性**：JDK 的 `SecretKeyFactory` 路径可能对 DES 密钥做奇偶校验位修正——拿不准就**两种候选都试**：

```csharp
byte[] plain = TryDecrypt(body, DeriveKey4(head, false));   // 4.x 原始密钥
if (plain == null) plain = TryDecrypt(body, DeriveKey4(head, true));  // 校验位修正
if (plain == null) plain = TryDecrypt(body, DeriveKeyClassic(head));  // 旧版算法兜底
```

每个候选解出来还要过"可打印 UTF-8"检查，防止错密钥碰运气解出乱码。最终**新旧两代算法依次尝试**，兼容任何版本的 FinalShell。

## 真实数据验证

用 `VerifyPass.java` 里的真实密文 + 用户 conn 目录全量数据测试：

```
== VerifyPass.java 中的真实测试向量 ==
OK   解密匹配  × 3
== 真实 conn 目录端到端 ==
条目=30 RDP=30 密码解密成功=30 失败=0
```

**30/30 全部解密成功。**

## 字段适配：4.x 的 schema 变化

光解密还不够，conn 目录的 JSON 字段和文档假设的差异：

| 假设（旧） | 实际（4.x） | 处理 |
|-----------|------------|------|
| `username` | `user_name` | 两个字段都读 |
| `protocol: "RDP"` | `conection_type: 101`（数字） | 101 → RDP |
| 目录名 = 分组名 | 目录名是**内部 ID** | 逐级读 `folder.json` 取真名 |

分组名还原后是 `北少云`、`佛系云` 这样的真实分组（顺带发现"阿里云"其实是空分组）。

## 意外收获：批内去重

解析时发现 37 个文件里只有 30 条有效连接——用户把连接移动进分组后，**FinalShell 会残留根目录的旧文件**，同名同主机同端口。不处理就会重复导入。方案：批内按"主机+端口"去重，重复项置灰显示"（批内重复）"默认不勾选。最终 19 台真实服务器干净入库。

## 安全边界

必须说清楚：解密只发生在**导入瞬间的本机内存里**，解出的密码立即用 DPAPI 重新加密落盘——等价于用户手工重录一遍密码，不改变原有安全边界；解密失败的条目标记"需手动录入"，不阻断导入。

## 本篇要点

- 算法逆向的最短路径往往不是硬啃二进制，而是找到现成的实现痕迹
- 拿不准的密码学细节（如密钥奇偶校验）做成候选列表依次尝试，比赌一个答案可靠
- 数据迁移类功能必须做批内去重——上游工具的残留文件是常态

[下一篇：用户反馈驱动的迭代 →](07-用户反馈驱动的迭代.md)
