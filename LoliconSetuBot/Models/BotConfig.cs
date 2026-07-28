// LoliconSetuBot - 萌娘图画机器人
// 文件说明：机器人配置文件模型，从 config.json 加载
// 每行代码旁边附带中文注释说明用途

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoliconSetuBot.Models;

// 机器人配置类：从 config.json 文件加载并管理所有配置项
public sealed class BotConfig {
    // 是否启用机器人，true=启用，false=禁用
    public bool Enabled { get; set; } = true;

    // 是否显示图片信息（标题、作者、PID 等）
    public bool ShowInfo { get; set; } = true;

    // 是否排除 AI 生成图片，true=过滤掉 AI 作品
    public bool ExcludeAI { get; set; } = true;

    // 是否水平翻转图片，用于规避部分平台的图片检测
    public bool FlipHorizontal { get; set; } = true;

    // 是否垂直翻转图片，用于规避部分平台的图片检测
    public bool FlipVertical { get; set; } = false;

    // 是否自动撤回图片，true=发送后自动撤回
    public bool AutoRevoke { get; set; } = false;

    // 自动撤回前的延迟时间（毫秒），默认 5000ms 即 5 秒
    public int RevokeDelay { get; set; } = 5000;

    // 冷却时间（秒），两次请求之间的最小间隔，0=不限制
    public int CoolDown { get; set; } = 0;

    // 是否允许 R18/成人向图片，true=包含，false=排除
    public bool R18 { get; set; } = false;

    // 图片代理地址，如 i.pixiv.re 用于绕过 Pixiv 直连限制
    public string Proxy { get; set; } = "i.pixiv.re";

    // 图片尺寸请求参数，original=原始全尺寸 / regular=常规 / small=小尺寸
    public string Size { get; set; } = "original";

    // JSON 序列化选项配置：使用驼峰命名策略匹配 API 返回的 JSON 字段名，并美化输出格式
    private static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // 从文件加载配置：从指定路径加载配置文件，如果文件不存在则创建默认配置
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

    // 保存配置到文件：将当前配置序列化为 JSON 并写入指定文件
    public void Save(string filePath) {
        File.WriteAllText(filePath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
