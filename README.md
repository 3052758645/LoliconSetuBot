# LoliconSetuBot

二次元插画随机获取工具，基于 [lolicon.app](https://api.lolicon.app) API。

## 功能

- 从 Pixiv 随机获取二次元插画
- 支持标签筛选（校园、泳装、和风等）
- R18 过滤与 AI 图排除
- 图片翻转（水平/垂直）
- 图片缓存到本地
- 控制台交互式 REPL 模式

## 快速开始

```bash
# 获取单张指定标签的图片
dotnet run -- --tag 原神

# 获取多张图片
dotnet run -- --tag 芙宁娜 --count 3

# 无限模式获取（Ctrl+C 停止）
dotnet run -- --infinite

# 交互式模式（默认）
dotnet run
```

## CLI 参数

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `--tag <tag>` | 获取指定标签的图片（支持空格、`&`、`\|` 分隔多个标签） | 无（交互式模式） |
| `--count <n>` | 获取图片数量 | 1 |
| `--infinite` | 无限循环模式，Ctrl+C 停止 | 关闭 |
| `--interval <ms>` | 无限模式请求间隔（毫秒） | 1500 |
| `--quiet` | 不显示图片元数据（标题、作者等） | 显示 |
| `--help` | 显示帮助信息 | - |

## 使用示例

### 单张获取

```bash
dotnet run -- --tag 原神
```

输出：
```
LoliconSetuBot v1.1

  📦 标签: 原神

  ⏳ 请求中…
┌──────────────────────────────────────────────────────┐
│ 标题: 妮露~                                          │
│ 作者: 冰冻鱼粽                                       │
│ PID: 100927538                                       │
│ 尺寸: 1127x2028                                      │
└──────────────────────────────────────────────────────┘

  ⬇️ 1.0MB / 3.7MB (27%): 27%
  ⬇️ 3.5MB / 3.7MB (94%): 94%
  ✅ 下载完成 (3786 KB) · JPEG

  ✅ 完成！
```

### 批量获取

```bash
# 获取 5 张「芙宁娜」标签图片
dotnet run -- --tag 芙宁娜 --count 5

# 多标签获取（原神 + 雷泽 + 纳西妲）
dotnet run -- --tag "原神 雷泽 纳西妲" --count 10
```

### 无限模式

```bash
# 无限循环获取（Ctrl+C 停止）
dotnet run -- --infinite

# 自定义请求间隔（每 5 秒请求一次）
dotnet run -- --infinite --interval 5000

# 安静模式：不显示元数据，只显示进度
dotnet run -- --tag 原神 --infinite --quiet
```

### 交互式模式

不传参数时进入交互式 REPL：

```
╔════════════════════════╦══════════════════════════╗
║ 命令                     ║ 说明                      ║
╠════════════════════════╬══════════════════════════╣
║ 来张标签涩图           ║ 获取单张涩图              ║
║ 无限涩图 / 循环        ║ 进入无限循环模式          ║
║ exit                   ║ 退出程序                  ║
╚════════════════════════╩══════════════════════════╝

  > 来张原神涩图
  > 来张 雷泽 芙宁娜 涩图
  > 无限涩图
  > exit
```

### 显示帮助

```bash
dotnet run -- --help
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
| `size` | 图片尺寸：`original` / `regular` / `small` / `mini` / `thumb` |

## 项目结构

```
LoliconSetuBot.sln
+-- LoliconSetuBot/
    +-- Program.cs            # 控制台入口（CLI 参数 + REPL）
    +-- Models/
    |   +-- BotConfig.cs      # 配置模型
    |   +-- ApiModels.cs      # API 响应模型
    +-- Services/
        +-- LoliconService.cs # 核心服务（请求、处理、缓存）
    +-- cache/                # 本地图片缓存
    +-- logs/                 # 日志文件
```

## 许可证

MIT
