const statusEl = document.getElementById("status");
const onlineEl = document.getElementById("online");
const messagesEl = document.getElementById("messages");
const textEl = document.getElementById("text");
const sendEl = document.getElementById("send");

const source = new EventSource("/sse/chat");

source.onopen = () => {
  statusEl.textContent = "已連線";
};

source.onerror = () => {
  statusEl.textContent = "連線中斷，等待自動重連...";
};

// presence 事件由「有人連進來 / 離開」觸發，不是定時輪詢——
// 每個分頁的連線與離線，都會讓其他所有分頁的在線人數即時更新。
source.addEventListener("presence", (event) => {
  onlineEl.textContent = event.data;
});

source.addEventListener("chat", (event) => {
  const li = document.createElement("li");
  li.textContent = event.data;
  messagesEl.prepend(li);
});

// 伺服器準備關閉前主動廣播的訊息，讓使用者清楚知道「這是預期中的斷線」，
// 而不是把它當成一個看不出原因的錯誤。
source.addEventListener("server-shutdown", (event) => {
  statusEl.textContent = `⚠ ${event.data}`;
});

async function sendMessage() {
  const text = textEl.value.trim();
  if (!text) {
    return;
  }

  // 送出訊息是普通的 POST（EventSource 只能收，不能送），
  // 後端收到後會透過 BroadcastHub 廣播給所有正在連線的分頁，包含自己這一個。
  await fetch("/sse/chat/messages", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ text }),
  });

  textEl.value = "";
}

sendEl.addEventListener("click", sendMessage);
textEl.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    sendMessage();
  }
});
