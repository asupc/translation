# 测试

```powershell
dotnet test tests\Translation.App.Tests\Translation.App.Tests.csproj
```

## 现有测试

| 文件 | 覆盖内容 |
|---|---|
| `WordPatternTests.cs` | 单个英文单词的判定与规整（接受/拒绝、剥首尾引号与句读） |
| `EntryParserTests.cs` | ECDICT 释义解析：多行义项、词性前缀、领域标签、七类词形变化 |
| `DictionaryServiceTests.cs` | 词典查询：变形词还原、大小写区分与不敏感兜底、未命中/库缺失回退、释放后重开连接 |
| `DictionaryRealDatabaseTests.cs` | 需要真实 `models/dictionary/ecdict.sqlite`（缺失时显示为跳过）：真实词条多义项、变形词、生僻词 |
| `HotkeyDefinitionTests.cs` | 热键解析/格式化、非法组合、修饰键校验 |
| `LanguageDetectorTests.cs` | 中英识别、中英混排、日韩/西里尔等不支持语言、全角/扩展汉字 |
| `SettingsStoreTests.cs` | 设置读写、损坏 JSON 回退、越界值修正 |

词典与翻译相关的测试不依赖 NMT 模型；未构建词典数据时大部分测试仍可运行，真实库测试会显示为跳过。

## 待补

- 原生核心翻译样本（zh↔en）
