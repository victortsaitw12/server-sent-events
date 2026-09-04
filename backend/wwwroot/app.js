const statusEl = document.getElementById("status");
const messagesEl = document.getElementById("messages");

// EventSource 是瀏覽器內建的 API，會自動用 GET 建立連線、
// 自動處理斷線重連，我們完全不需要自己寫 XHR 或 fetch。
const source = new EventSource("/sse/time");

source.onopen = () => {
  statusEl.textContent = "已連線";
};

// 沒有指定 event 名稱的 SSE 訊息，會觸發預設的 onmessage。
source.onmessage = (event) => {
  const li = document.createElement("li");
  li.textContent = event.data;
  messagesEl.prepend(li);
};

source.onerror = () => {
  statusEl.textContent = "連線中斷或發生錯誤（瀏覽器會自動嘗試重連）";
};
