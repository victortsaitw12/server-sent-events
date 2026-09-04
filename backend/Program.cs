using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// 這一步示範完整的 SSE 訊息格式：id / event / 多行 data / retry。
app.MapGet("/sse/notifications", async (HttpContext context) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    var cancellationToken = context.RequestAborted;
    long eventId = 0;

    try
    {
        // retry 告訴瀏覽器：如果連線中斷，等 3000ms 再自動重連。
        // 只需要在串流一開始送一次即可對整個連線生效。
        await WriteSseMessageAsync(context.Response, cancellationToken, retryMs: 3000);

        while (!cancellationToken.IsCancellationRequested)
        {
            eventId++;

            // 沒有指定 event 名稱的訊息，前端要用 addEventListener("tick", ...) 接收，
            // 而不是預設的 onmessage（onmessage 只接收「沒有指定名稱」的事件）。
            await WriteSseMessageAsync(
                context.Response,
                cancellationToken,
                id: eventId.ToString(),
                eventName: "tick",
                data: $"{DateTime.Now:HH:mm:ss}");

            // 每 5 次額外送出一則多行內容的 alert 事件，示範 data 可以有多行——
            // 每一行都要各自加上 "data: " 前綴，瀏覽器收到後會用 \n 把它們接回原本的樣子。
            if (eventId % 5 == 0)
            {
                var payload = $"已送出 {eventId} 則 tick 訊息\n伺服器時間：{DateTime.Now:HH:mm:ss}";
                await WriteSseMessageAsync(
                    context.Response,
                    cancellationToken,
                    id: eventId.ToString(),
                    eventName: "alert",
                    data: payload);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
        // 使用者關閉分頁或斷線，屬於正常結束。
    }
});

app.Run();

// 依照 SSE 協定組出一則訊息並寫入 response、然後立刻 Flush。
// 欄位順序不影響行為，但習慣上會把 id / event 放在 data 前面。
static async Task WriteSseMessageAsync(
    HttpResponse response,
    CancellationToken cancellationToken,
    string? id = null,
    string? eventName = null,
    string? data = null,
    int? retryMs = null)
{
    var builder = new StringBuilder();

    if (id is not null)
    {
        builder.Append("id: ").Append(id).Append('\n');
    }

    if (eventName is not null)
    {
        builder.Append("event: ").Append(eventName).Append('\n');
    }

    if (retryMs is not null)
    {
        builder.Append("retry: ").Append(retryMs.Value).Append('\n');
    }

    if (data is not null)
    {
        foreach (var line in data.Split('\n'))
        {
            builder.Append("data: ").Append(line).Append('\n');
        }
    }

    // 空白行代表這一則訊息結束，這是 SSE 協定強制規定的格式。
    builder.Append('\n');

    await response.WriteAsync(builder.ToString(), cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}
