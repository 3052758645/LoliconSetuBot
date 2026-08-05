# LoliconSetuBot

二次元插画批量获取工具，基于 [lolicon.app](https://api.lolicon.app) API。

## 快速开始

```bash
# 获取指定标签的单张图片
dotnet run -- --tag 原神

# 批量获取多张
dotnet run -- --tag 原神 --count 5

# 无限循环模式（Ctrl+C 停止）
dotnet run -- --infinite

# 交互式 REPL
dotnet run
```

## CLI 参数

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `--tag <tag>` | 图片标签，支持空格/\`&\`/\`\|\`分隔多标签（不传则进入交互模式） | 无 |
| `--count <n>` | 获取图片数量 | 1 |
| `--infinite` | 无限循环模式 | 关闭 |
| `--interval <ms>` | 无限模式请求间隔（毫秒） | 1500 |
| `--quiet` | 隐藏元数据输出 | 显示 |
| `--save` | 将当前配置保存到 config.json | 不保存 |
| `--output <dir>` | 自定义输出目录（覆盖配置） | `cache/` |
| `--help` | 显示帮助 | - |

## 使用示例

### 单张/批量

```bash
# 单张
dotnet run -- --tag 原神

# 多张
dotnet run -- --tag 芙宁娜 --count 5

# 多标签（满足所有标签）
dotnet run -- --tag "原神 雷泽 纳西妲" --count 10
```

### 无限模式

```bash
# 无限循环（Ctrl+C 停止）
dotnet run -- --infinite

# 自定义间隔
dotnet run -- --infinite --interval 5000

# 安静模式（只显示进度条）
dotnet run -- --tag 原神 --infinite --quiet
```

### 配置持久化

```bash
# 修改配置并保存到 config.json
dotnet run --tag 原神 --save
```

### 自定义输出目录

```bash
# 指定输出到 /tmp/pics 而不是 cache/
dotnet run --tag 原神 --output /tmp/pics
```

## 配置

首次运行自动生成 `config.json`：

```json
{
  "enabled": true,
  "showInfo": true,
  "excludeAI": true,
  "flipHorizontal": true,
  "flipVertical": false,
  "autoRevoke": false,
  "revokeDelay": 5000,
  "coolDown": 0,
  "r18": false,
  "proxy": "i.pixiv.re",
  "size": "original",
  "outputDir": "",
  "fallbackUrls": []
}
```

| 字段 | 说明 |
|------|------|
| `enabled` | 是否启用 |
| `showInfo` | 显示图片信息（标题、作者、标签等） |
| `excludeAI` | 排除 AI 生成图 |
| `flipHorizontal` | 水平翻转图片 |
| `flipVertical` | 垂直翻转图片 |
| `autoRevoke` | 自动撤回（模拟） |
| `revokeDelay` | 撤回延迟（毫秒） |
| `coolDown` | 冷却时间（秒） |
| `r18` | 启用 R18 |
| `proxy` | 代理域名 |
| `size` | 图片尺寸：`original` / `regular` / `small` / `mini` / `thumb` |
| `outputDir` | 自定义输出目录（空字符串=默认 `cache/`） |
| `fallbackUrls` | 备用 API 地址列表 |

## 备用 API

主 API 不可用时自动切换备用 API：

- **anoSu** (`api.anosu.top`) — 支持 `tag`、`r18`、`size` 参数，返回原始图片
- **jitsu** (`moe.jitsu.top`) — 返回原始图片

备用 API 不返回图片元数据，仅作为下载源。

## 项目结构

```
LoliconSetuBot.sln
+-- LoliconSetuBot/
    +-- Program.cs            # CLI 参数解析 + REPL
    +-- Models/
    |   +-- BotConfig.cs      # 配置模型
    |   +-- ApiModels.cs      # API 响应模型
    +-- Services/
        +-- LoliconService.cs # API 请求、图片处理、缓存
    +-- cache/                # 图片缓存目录
    +-- logs/                 # 日志文件
```

## 许可证

MIT
