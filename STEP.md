# Step 2：SSE 事件格式

## 這一步要學什麼

Step 1 只用了 SSE 最簡單的 `data:` 欄位。實際上 SSE 訊息還有另外三個常用欄位：

- `event:` — 為這則訊息命名，前端可以用 `addEventListener("名稱", ...)` 分別處理
  不同種類的訊息，而不是全部擠在 `onmessage` 裡判斷。
- `id:` — 為這則訊息編號，瀏覽器會記住「目前收到的最後一個 id」，斷線重連時
  會透過 `Last-Event-ID` header 告訴伺服器（下一步 Step 3 會用到這個機制）。
- `retry:` — 告訴瀏覽器斷線後要等多久再自動重連（單位毫秒），只需要送一次。

以及 `data:` 其實可以**重複多次**組成多行內容——這是很多人會誤解的地方。

## SSE 訊息的完整格式

一則完整的 SSE 訊息長這樣（欄位間用 `\n`，訊息結尾用「空白行」`\n\n` 結束）：

```
id: 5
event: alert
data: 已送出 5 則 tick 訊息
data: 伺服器時間：14:32:10

```

瀏覽器收到後，會把兩個 `data:` 行用 `\n` 接回一個字串：
`"已送出 5 則 tick 訊息\n伺服器時間：14:32:10"`。

## 後端關鍵程式碼（`backend/Program.cs`）

`WriteSseMessageAsync` 這個 helper 把「組欄位」這件事抽出來，逐行組出正確格式：

```csharp
if (data is not null)
{
    foreach (var line in data.Split('\n'))
    {
        builder.Append("data: ").Append(line).Append('\n');
    }
}
builder.Append('\n'); // 空白行 = 這則訊息結束
```

`retry:` 只在連線一開始送一次：

```csharp
await WriteSseMessageAsync(context.Response, cancellationToken, retryMs: 3000);
```

## 前端關鍵程式碼（`backend/wwwroot/app.js`）

```javascript
source.addEventListener("tick", (event) => {
  console.log(event.lastEventId, event.data);
});
```

- 沒有用 `addEventListener("tick", ...)` 訂閱的話，`tick` 事件不會觸發
  `onmessage`——具名事件必須明確訂閱才會收到。
- `event.lastEventId` 就是後端送的 `id:`。

## 動手試試看

```bash
cd backend
dotnet run
```

用 curl 直接看原始的位元組流，觀察 `id:` / `event:` / 多行 `data:` 的排列：

```bash
curl -N http://localhost:5080/sse/notifications
```

打開瀏覽器到 `http://localhost:5080`，觀察 tick 跟 alert 兩個區塊分別更新。

## 下一步

Step 3 會用到這一步的 `id:` 機制：模擬斷線後，示範瀏覽器怎麼透過
`Last-Event-ID` header 讓伺服器知道要從哪裡「補送」遺漏的訊息。
