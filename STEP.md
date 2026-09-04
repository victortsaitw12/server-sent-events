# Step 4：多客戶端廣播（一對多推播）

## 這一步要學什麼

前三步的 endpoint 都只服務「一個」連線。SSE 真正常見的應用場景，是伺服器
主動把同一則訊息**同時推送給所有正在連線的客戶端**——例如系統公告、
即時儀表板、多人協作時的狀態同步。

這一步用 `Channel<T>` 實作一個簡易的 pub/sub 廣播中心 `BroadcastHub`，
是這門課裡第一次出現「一個事件、多個接收者」的架構。

## 為什麼用 `Channel<T>`，而不是 Step 3 的輪詢？

Step 3 的每個連線各自去讀共用的 `CounterFeed` 歷史紀錄，客戶端一多，
就變成大家都在重複檢查同一份資料。這一步改成：

- 每個連線訂閱時，拿到**屬於自己的一個 `Channel<BroadcastMessage>`**。
- `PublishAsync` 被呼叫時（例如有人送出聊天訊息），把同一則訊息**分別寫入
  每一個訂閱者的 channel**。
- 每個連線的迴圈只需要 `await foreach` 讀自己的 channel，有新訊息就送出、
  沒有就一直等待（不會忙碌迴圈耗 CPU），彼此完全獨立。

```csharp
var channel = Channel.CreateBounded<BroadcastMessage>(new BoundedChannelOptions(20)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = true,
    SingleWriter = false,
});
```

- 用**有界（bounded）** channel 而不是無限累積，是為了避免「某個客戶端網路
  很差、消化訊息很慢」時，訊息在伺服器記憶體裡無限堆積。
- `DropOldest`：滿了就丟掉最舊的，寧可讓慢的客戶端漏掉幾則訊息，
  也不要讓它拖慢或塞爆整個伺服器。

## 後端關鍵程式碼（`backend/Program.cs`）

訂閱、讀取、離線清理：

```csharp
var (clientId, reader) = hub.Subscribe();
try
{
    await foreach (var message in reader.ReadAllAsync(cancellationToken))
    {
        await WriteSseMessageAsync(context.Response, cancellationToken,
            id: message.Id.ToString(), eventName: message.EventName, data: message.Data);
    }
}
finally
{
    hub.Unsubscribe(clientId); // 斷線時務必清理，否則會累積用不到的 channel（記憶體洩漏）
}
```

觸發廣播的 API：

```csharp
app.MapPost("/sse/chat/messages", async (ChatMessageRequest request, BroadcastHub hub) =>
{
    await hub.PublishAsync("chat", request.Text.Trim());
    return Results.Accepted();
});
```

- `EventSource` 只能用 GET 接收，**不能主動送資料**。所以「送出訊息」這件事
  要另外開一個普通的 POST API，伺服器收到後再透過 `BroadcastHub` 廣播出去。
- 每次有人連線／離線，也會呼叫 `hub.PublishPresenceAsync()` 廣播目前在線
  人數，示範「觸發廣播」不是只能靠計時器，任何伺服器端事件都可以是廣播的來源。

## 前端關鍵程式碼（`backend/wwwroot/app.js`）

```javascript
await fetch("/sse/chat/messages", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ text }),
});
```

送出訊息用一般的 `fetch`，跟接收訊息的 `EventSource` 是兩條分開的路徑——
這是 SSE 的固有限制，也是為什麼 SSE 適合「單向、伺服器主導」的推播場景，
真正需要雙向溝通時通常會考慮 WebSocket。

## 動手試試看

```bash
cd backend
dotnet run
```

打開**兩個瀏覽器分頁**都連到 `http://localhost:5080`：

1. 在其中一個分頁輸入文字送出，應該兩個分頁都會立刻收到同一則訊息。
2. 觀察在線人數：多開一個分頁、或關閉一個分頁，其他分頁的在線人數會即時更新。
3. 也可以直接用 curl 觸發廣播，同時開著瀏覽器分頁看效果：
   ```bash
   curl -X POST http://localhost:5080/sse/chat/messages \
     -H "Content-Type: application/json" \
     -d '{"text":"來自 curl 的廣播訊息"}'
   ```

## 下一步

Step 5 會補上兩個正式環境必備的機制：**心跳（heartbeat）**避免連線被反向
代理逾時判定為閒置而關閉，以及更完整地確保連線在各種斷線情境下都能正確
清理資源。
