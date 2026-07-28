using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoliconSetuBot.Models;

/// <summary>
/// 机器人配置类：从 config.json 文件加载并管理所有配置项
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
    /// JSON 序列化选项配置：使用驼峰命名策略匹配 API 返回的 JSON 字段名，并美化输出格式
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// 从文件加载配置：从指定路径加载配置文件，如果文件不存在则创建默认配置
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static BotConfig Load(string filePath) {
        // 如果配置文件不存在，则创建默认配置文件并返回默认配置
        if (!File.Exists(filePath)) {
            var cfg = new BotConfig();
            File.WriteAllText(filePath, JsonSerializer.Serialize(cfg, JsonOpts));
            return cfg;
        }
        try {
            // 尝试反序列化 JSON 配置文件为 BotConfig 对象
            var deserialized = JsonSerializer.Deserialize<BotConfig>(File.ReadAllText(filePath), JsonOpts);
            return deserialized ?? new BotConfig();
        } catch {
            // 如果解析失败，使用默认配置并覆盖原文件
            var fallback = new BotConfig();
            File.WriteAllText(filePath, JsonSerializer.Serialize(fallback, JsonOpts));
            return fallback;
        }
    }

    /// <summary>
    /// 保存配置到文件：将当前配置序列化为 JSON 并写入指定文件
    /// </summary>
    /// <param name="filePath"></param>
    public void Save(string filePath) {
        File.WriteAllText(filePath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
