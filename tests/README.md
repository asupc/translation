# 测试

```powershell
dotnet test tests\Translation.App.Tests\Translation.App.Tests.csproj
```

## 现有测试

| 文件 | 覆盖内容 |
|---|---|
| `WordPatternTests.cs` | 单个英文单词的判定与规整（接受/拒绝、剥首尾引号与句读） |
| `EntryParserTests.cs` | ECDICT 释义解析：多行义项、词性前缀、领域标签、七类词形变化 |
| `DictionaryServiceTests.cs` | 词典查询：变形词还原、大小写区分与不敏感兜底、未命中/库缺失回退 |
| `DictionaryRealDatabaseTests.cs` | 需要真实 `models/dictionary/ecdict.sqlite`（缺失时自动跳过）：真实词条多义项、变形词、生僻词 |

词典与翻译相关的测试不依赖 NMT 模型；未构建词典数据时大部分测试仍可运行。

## 待补

- `HotkeyDefinition` 解析/格式化
- `LanguageDetector` 中英混合/日韩文/空文本
- `SettingsStore` 读写与损坏 JSON 回退
- 原生核心翻译样本（zh↔en）
