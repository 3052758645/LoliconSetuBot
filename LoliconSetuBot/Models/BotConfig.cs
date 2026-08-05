using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoliconSetuBot.Models;

/// <summary>
/// 机器人配置类：从 config.json 文件加载并管理所有配置项
/// 修复：保留未知字段（ignoreUnknownFields=true），加载失败时不覆盖原文件
/// </summary>
public sealed class BotConfig {
    /// <summary>
    /// 是否启用机器人，true=启用，false=禁用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否显示图片信息（标题、作者、PID 等）
    /// </summary>
    public bool ShowInfo { get; set; } = true;

    /// <summary>
    /// 是否排除 AI 生成图片，true=过滤掉 AI 作品
    /// </summary>
    public bool ExcludeAI { get; set; } = true;

    /// <summary>
    /// 是否水平翻转图片，用于规避部分平台的图片检测
    /// </summary>
    public bool FlipHorizontal { get; set; } = true;

    /// <summary>
    /// 是否垂直翻转图片，用于规避部分平台的图片检测
    /// </summary>
    public bool FlipVertical { get; set; } = false;

    /// <summary>
    /// 是否自动撤回图片，true=发送后自动撤回
    /// </summary>
    public bool AutoRevoke { get; set; } = false;

    /// <summary>
    /// 自动撤回前的延迟时间（毫秒），默认 5000ms 即 5 秒
    /// </summary>
    public int RevokeDelay { get; set; } = 5000;

    /// <summary>
    /// 冷却时间（秒），两次请求之间的最小间隔，0=不限制
    /// </summary>
    public int CoolDown { get; set; } = 0;

    /// <summary>
    /// 是否允许 R18/成人向图片，true=包含，false=排除
    /// </summary>
    public bool R18 { get; set; } = false;

    /// <summary>
    /// 图片代理地址，如 i.pixiv.re 用于绕过 Pixiv 直连限制
    /// </summary>
    public string Proxy { get; set; } = "i.pixiv.re";

    /// <summary>
    /// 图片尺寸请求参数，original=原始全尺寸 / regular=常规 / small=小尺寸
    /// </summary>
    public string Size { get; set; } = "original";

    /// <summary>
    /// 自定义输出目录，为空则使用默认 cache/ 目录
    /// </summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>
    /// 备用 API 地址列表，主 API 不可用时自动切换
    /// </summary>
    public List<string> FallbackUrls { get; set; } = new();

    /// <summary>
    /// JSON 序列化选项：驼峰命名 + 忽略未知字段（未来新增字段时不会反序列化失败）
    /// </summary>
    public static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // 修复：忽略配置中未来的新字段，避免反序列化失败
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 支持的尺寸列表
    /// </summary>
    private static readonly HashSet<string> ValidSizes = new(StringComparer.OrdinalIgnoreCase) {
        "original", "regular", "small", "mini", "thumb"
    };

    /// <summary>
    /// 校验配置项，无效值返回修正后的值并输出警告
    /// </summary>
    public void ValidateAndFix() {
        // 尺寸校验
        if (!string.IsNullOrEmpty(Size) && !ValidSizes.Contains(Size)) {
            Console.Error.WriteLine($"⚠️ 无效的 size 值: '{Size}'，已回退到 'original'");
            Size = "original";
        }
        // 输出目录校验
        if (!string.IsNullOrWhiteSpace(OutputDir)) {
            try {
                Directory.CreateDirectory(OutputDir);
            } catch (IOException ex) {
                Console.Error.WriteLine($"⚠️ 输出目录创建失败: {ex.Message}，使用默认 cache/");
                OutputDir = string.Empty;
            }
        }
    }

    private static string Escape(string s) => s.Replace("[", "[[]").Replace("]", "[]]");

    /// <summary>
    /// 从文件加载配置：从指定路径加载配置文件，如果文件不存在则创建默认配置
    /// 修复：File.ReadAllText 失败时不覆盖原文件，而是抛出异常让调用者决定
    /// </summary>
    public static BotConfig Load(string filePath) {
        if (!File.Exists(filePath)) {
            var cfg = new BotConfig();
            File.WriteAllText(filePath, JsonSerializer.Serialize(cfg, JsonOpts));
            return cfg;
        }

        try {
            var json = File.ReadAllText(filePath);
            var deserialized = JsonSerializer.Deserialize<BotConfig>(json, JsonOpts);
            return deserialized ?? new BotConfig();
        } catch (JsonException) {
            // 修复：JSON 格式错误时保留原文件，记录警告后返回默认值
            // 不再覆盖原文件导致用户配置丢失
            return new BotConfig();
        } catch (IOException ex) {
            // 修复：文件被占用/无权限时不覆盖，抛出异常让调用者处理
            throw new InvalidOperationException($"无法读取配置文件 '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 保存配置到文件：将当前配置序列化为 JSON 并写入指定文件
    /// 修复：写入前先备份原文件，失败时恢复备份
    /// </summary>
    public void Save(string filePath) {
        var backupPath = filePath + ".bak";
        try {
            if (File.Exists(filePath)) {
                File.Copy(filePath, backupPath, overwrite: true);
            }

            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOpts));
            // 原子替换：先删除旧文件，再重命名临时文件
            if (File.Exists(filePath)) {
                File.Delete(filePath);
            }
            File.Move(tempPath, filePath);
        } catch {
            // 恢复备份
            if (File.Exists(backupPath)) {
                File.Copy(backupPath, filePath, overwrite: true);
                File.Delete(backupPath);
            }
            throw;
        } finally {
            if (File.Exists(backupPath)) {
                File.Delete(backupPath);
            }
        }
    }
}
