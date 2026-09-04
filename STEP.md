# Step 5：心跳與斷線清理

## 這一步要學什麼

這是整門課的最後一步，補上兩個正式環境會遇到、但前面步驟為了聚焦核心概念
而先跳過的問題：

1. **心跳（heartbeat）**：長時間沒有訊息時，連線會不會被中間的反向代理
   （Nginx、雲端 LB）誤判成閒置而強制關閉？
2. **斷線清理**：連線用各種方式結束時（正常關閉、網路突然中斷、伺服器
   自己要關機），有沒有確實釋放資源、通知使用者？

## 為什麼需要心跳？一個常見的誤解

常見誤解：「心跳是用來偵測客戶端是否還活著」。

實際上在 ASP.NET Core／Kestrel 裡，**偵測連線是否還活著不需要心跳**——
只要底層 TCP 連線斷了（例如使用者關閉分頁、正常收到 FIN），
`HttpContext.RequestAborted` 就會自動被觸發，我們從 Step 1 開始就一直
在用它判斷「該結束這個迴圈了」。

心跳真正的目的，是應付**中間的網路設備**：很多反向代理、負載平衡器會有
「連線閒置 N 秒沒有任何位元組傳輸，就視為異常並強制關閉」的機制，跟這個
連線兩端（瀏覽器、我們的伺服器）是否還活著無關。心跳存在的意義，
就是定期送一點資料出去，讓中間設備看到「這條連線還在用」。

## 後端關鍵程式碼（`backend/Program.cs`）

用 SSE 的**註解行**當心跳封包——它會被 `EventSource` 的解析器完全忽略，
不會觸發任何前端事件，純粹只是「有資料在流動」：

```csharp
static async Task WriteSseCommentAsync(HttpResponse response, CancellationToken cancellationToken, string comment)
{
    await response.WriteAsync($": {comment}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}
```

要在「等待新的廣播訊息」跟「該送心跳了」這兩件事之間做選擇，用
`PeriodicTimer` 搭配 `Task.WhenAny`：

```csharp
using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
var readTask = reader.WaitToReadAsync(cancellationToken).AsTask();
var heartbeatTask = heartbeatTimer.WaitForNextTickAsync(cancellationToken).AsTask();

while (!cancellationToken.IsCancellationRequested)
{
    var completed = await Task.WhenAny(readTask, heartbeatTask);
    // completed 是 readTask -> 有新訊息，處理完再重新開始等下一次
    // completed 是 heartbeatTask -> 15 秒到了都沒有新訊息，送一個心跳註解
}
```

> 這一步把心跳間隔設成 15 秒方便課堂觀察；正式環境通常會設 30～60 秒，
> 依你實際使用的反向代理逾時設定而定，抓一個明顯短於逾時時間的值即可。

**更完整的斷線清理**，把可能結束這個迴圈的每一種情況都處理到：

```csharp
try { ... }
catch (OperationCanceledException) { /* 正常斷線：分頁關閉、伺服器關閉 */ }
catch (IOException) { /* 寫入時才發現對方已經斷線 */ }
finally
{
    hub.Unsubscribe(clientId); // 不管上面哪種情況，都要釋放這個連線的 channel
    ...
}
```

`IOException` 這個分支特別容易被忽略：`RequestAborted` 主要處理「乾淨」的
斷線（收到 FIN），但如果網路是直接被拔掉、沒有正常的 TCP 關閉程序，
伺服器往往要等到**下一次寫入**才會發現對方已經不在了，這時丟出來的是
`IOException` 而不是 `OperationCanceledException`。兩種都要接住，
才能確保 `finally` 裡的清理一定會執行。

## 優雅關機（graceful shutdown）

```csharp
lifetime.ApplicationStopping.Register(() =>
{
    broadcastHub.PublishAsync("server-shutdown", "伺服器即將關閉，稍後會自動重新連線")
        .GetAwaiter().GetResult();
});
```

當你要部署新版本、重啟服務時，`IHostApplicationLifetime.ApplicationStopping`
會在關機流程開始時觸發。這裡搶在連線真的被切斷之前，先廣播一則
`server-shutdown` 事件，讓前端能顯示「這是預期中的維護」，而不是讓使用者
看到一個來路不明的錯誤。

## 前端關鍵程式碼（`backend/wwwroot/app.js`）

```javascript
source.addEventListener("server-shutdown", (event) => {
  statusEl.textContent = `⚠ ${event.data}`;
});
```

心跳的註解行完全不會出現在任何 JavaScript 事件裡——這是刻意設計，
前端本來就不需要處理它，唯一「看得到」心跳的方式是檢查最原始的 HTTP 回應。

## 動手試試看

```bash
cd backend
dotnet run
```

用 curl 觀察心跳（`-N` 關閉緩衝，耐心等 15 秒以上）：

```bash
curl -N http://localhost:5080/sse/chat
# 前段會看到 presence 事件，接著安靜一段時間後，
# 應該會看到一行 ": heartbeat"，就是心跳被送出來了。
```

觀察優雅關機：開著瀏覽器分頁連線，然後在終端機按 `Ctrl+C` 停止伺服器，
應該會在畫面上的連線狀態看到「伺服器即將關閉」的提示，而不是單純顯示斷線。

## 課程回顧

到這裡，你已經從最基本的 `text/event-stream` response，一路做到：
事件格式（`event`/`id`/`retry`/多行 `data`）、自動重連與補送、
多客戶端廣播、心跳與斷線清理——這些是用 SSE 做真實專案時最常遇到的機制。

如果想繼續深入，可以研究的方向包括：SSE endpoint 的認證與授權
（`EventSource` 不支援自訂 header，通常要用 query string token 或 cookie）、
搭配反向代理部署時要確認 buffering 相關設定（例如 Nginx 的
`proxy_buffering off`），以及大規模多實例部署時，`BroadcastHub` 這種
存在單一程序記憶體裡的設計要怎麼換成 Redis Pub/Sub 之類的外部訊息系統。
