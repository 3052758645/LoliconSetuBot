# LoliconSetuBot

二次元插画随机获取机器人，基于 [lolicon.app](https://api.lolicon.app) API。

## 功能

- 从 Pixiv 随机获取二次元插画
- 支持标签筛选（校园、泳装、和风等）
- R18 过滤与 AI 图排除
- 图片翻转（水平/垂直）
- 图片缓存到本地
- 控制台交互式 REPL

## 技术栈

- **.NET 10.0** (C#)
- **SkiaSharp** — 图片处理（翻转）
- 指数退避重试（最多 3 次）
- 完整 async/await（无 `.Wait()` / `.GetResult()`）

## 构建与运行

```powershell
cd LoliconSetuBot
dotnet run

# 带 groupId 参数
dotnet run -- your-group-id
```

## 命令

| 命令 | 说明 |
|------|------|
| `来张校园涩图` | 获取校园标签图片 |
| `来张涩图` | 随机获取（无标签） |
| `无限涩图` | 循环获取，Ctrl+C 停止 |
| `exit` | 退出程序 |

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
  "size": "original"
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
| `size` | 图片尺寸：`original` / `regular` / `small` |

## 项目结构

```
LoliconSetuBot.sln
+-- LoliconSetuBot/
|   +-- Program.cs            # 控制台 REPL 入口
|   +-- Models/
|   |   +-- BotConfig.cs      # 配置模型
|   |   +-- ApiModels.cs      # API 响应模型
|   +-- Services/
|       +-- LoliconService.cs  # 核心服务（请求、处理、缓存）
```

## 许可证

MIT
