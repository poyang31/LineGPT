# LineGPT

簡易的LINE聊天機器人 💬

## 執行

在 `appsettings.json` 填入設定：

```json
{
  "OpenAI": {
    "ApiKey": "你的 OpenAI API Key",
    "BaseUrl": "https://api.openai.com/v1",
    "Model": "gpt-6-luna"
  },
  "Line": {
    "ChannelSecret": "你的 LINE Channel Secret",
    "ChannelAccessToken": "你的 LINE Channel Access Token"
  }
}
```

`BaseUrl` 可改成 OpenAI 相容服務的 API 基底網址（依服務要求包含 `/v1`），不要填入 `/chat/completions`。
留空或未設定時使用 OpenAI 官方端點。

保留檔案中原有的 `Logging` 與 `AllowedHosts` 設定，填好後執行：

```sh
dotnet run --project LineGPT.csproj --launch-profile http
```

僅透過驗證 LINE 簽章的 `/callback` 呼叫 agent。
開發模式可在 `/openapi/v1.json` 查看 API 定義。

## LINE Bot

採用 LINE 文件列出的社群 SDK `Line.Bot.SDK` 5.0.0（上游已封存）。
文字訊息經簽章驗證後交給同一個 agent，再以 LINE reply token 回覆。

發佈版本請修改執行檔旁的 `appsettings.json`，並在該目錄執行 `./LineGPT`。
實際金鑰請勿提交到 Git。

將服務公開於 HTTPS 網址，
在 LINE Developers Console 將 Webhook URL 設為：

```text
https://your-domain/callback
```

按 Verify 並啟用 Use webhook；停用官方帳號的自動回應，避免重複回覆。
簽章缺失或錯誤回傳 401，LINE 設定缺失回傳 503，空事件陣列回傳 200。
僅處理文字訊息，其他事件直接略過。每則訊息獨立執行，不保留對話歷史。
模型執行上限 40 秒；失敗時回覆稍後再試。超長回覆會依 SDK 的 2,000 字元限制截短。

目前採同步 webhook，未實作持久佇列與事件去重；正式環境若啟用 webhook
重送，需補上依 webhookEventId 去重的機制，避免重複呼叫模型或回覆。

參考：[LINE SDK 列表](https://developers.line.biz/en/docs/messaging-api/line-bot-sdk/)、
[SDK 原始碼](https://github.com/dlemstra/line-bot-sdk-dotnet)。
