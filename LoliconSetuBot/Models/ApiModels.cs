using System.Text.Json.Serialization;

namespace LoliconSetuBot.Models;

/// <summary>
/// Lolicon API 返回响应模型：表示从 lolicon.app API 收到的 JSON 响应的数据结构
/// </summary>
public sealed class LoliconResponse {
    // JSON 字段 "data" 映射到 Data 属性，图片数据列表，可能为空（null）
    [JsonPropertyName("data")]
    public List<LoliconData>? Data { get; set; }

    // JSON 字段 "error" 映射到 Error 属性，错误信息，成功时为 null
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// 单张图片数据模型：存储从 API 获取的单张插画详细信息
/// </summary>
public sealed class LoliconData {
    // 插画作品 ID，允许从字符串解析数字
    [JsonPropertyName("pid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Pid { get; set; }

    // 插画作者/用户 ID，允许从字符串解析数字
    [JsonPropertyName("uid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Uid { get; set; }

    // 插画标题/名字
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    // 插画作者/画师名字
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    // 插画宽度像素数，允许从字符串解析数字
    [JsonPropertyName("width")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Width { get; set; }

    // 插画高度像素数，允许从字符串解析数字
    [JsonPropertyName("height")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Height { get; set; }

    // 插画标签/分类列表，如 原创、同人 等
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    // 各尺寸图片的 URL 地址
    [JsonPropertyName("urls")]
    public LoliconUrls Urls { get; set; } = new();
}

/// <summary>
/// 图片 URL 模型：存储同一张插画的不同尺寸链接
/// </summary>
public sealed class LoliconUrls {
    // 原始全尺寸图片链接
    [JsonPropertyName("original")]
    public string? Original { get; set; }

    // 常规尺寸图片链接
    [JsonPropertyName("regular")]
    public string? Regular { get; set; }

    // 小尺寸图片链接
    [JsonPropertyName("small")]
    public string? Small { get; set; }

    // ????????
    [JsonPropertyName("mini")]
    public string? Mini { get; set; }

    // ?????????
    [JsonPropertyName("thumb")]
    public string? Thumb { get; set; }
}

/// <summary>
/// 涩图获取结果模型：封装一次完整的图片获取结果，包含数据和图片二进制数据
/// </summary>
public sealed class SetuResult {
    // 插画详细数据（标题、作者、尺寸等）
    public required LoliconData Data { get; init; }
    // 图片的二进制字节数组，可用于发送或保存
    public required byte[] ImageBytes { get; init; }
    // 格式化后的图片信息文本，标题、作者、PID 等展示用
    public required string InfoText { get; init; }
}
