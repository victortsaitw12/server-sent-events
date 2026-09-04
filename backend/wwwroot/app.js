const statusEl = document.getElementById("status");
const ticksEl = document.getElementById("ticks");
const alertsEl = document.getElementById("alerts");

const source = new EventSource("/sse/notifications");

source.onopen = () => {
  statusEl.textContent = "已連線";
};

source.onerror = () => {
  statusEl.textContent = "連線中斷或發生錯誤（瀏覽器會依 retry 設定自動重連）";
};

// 具名事件必須用 addEventListener 接收，"tick" 這個名稱要跟後端 event: 的值一致。
source.addEventListener("tick", (event) => {
  const li = document.createElement("li");
  // event.lastEventId 對應後端送出的 id: 欄位。
  li.textContent = `#${event.lastEventId} - ${event.data}`;
  ticksEl.prepend(li);
});

source.addEventListener("alert", (event) => {
  const li = document.createElement("li");
  // 多行 data 在瀏覽器收到時，已經被自動接回帶有 \n 的原始字串。
  li.textContent = `#${event.lastEventId} - ${event.data}`;
  li.style.whiteSpace = "pre-line";
  alertsEl.prepend(li);
});
