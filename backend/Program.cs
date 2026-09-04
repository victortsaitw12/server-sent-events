var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// 最基本的 SSE endpoint：每秒推送一次目前的伺服器時間。
app.MapGet("/sse/time", async (HttpContext context) =>
{
    // SSE 的核心：告訴瀏覽器這是一個 event-stream，且不要被快取。
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    // 當使用者關閉分頁或斷線時，RequestAborted 會被觸發，讓我們可以停止迴圈。
    var cancellationToken = context.RequestAborted;

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = $"data: {DateTime.Now:HH:mm:ss}\n\n";
            await context.Response.WriteAsync(message, cancellationToken);

            // SSE 是持續串流的 response，必須手動 Flush，
            // 否則資料會被緩衝在伺服器端，瀏覽器收不到任何東西。
            await context.Response.Body.FlushAsync(cancellationToken);

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
        // 使用者關閉分頁或斷線，屬於正常結束，不需要當成錯誤處理。
    }
});

app.Run();
