const statusEl = document.getElementById("status");
const ticksEl = document.getElementById("ticks");
const replaysEl = document.getElementById("replays");

const source = new EventSource("/sse/counter");

source.onopen = () => {
  statusEl.textContent = "已連線";
};

// 斷線瞬間會先觸發 onerror，接著瀏覽器才會依照 retry 設定自動重連——
// 這整個過程完全不需要我們自己寫任何重連邏輯。
source.onerror = () => {
  statusEl.textContent = "連線中斷，等待自動重連...";
};

source.addEventListener("resume-info", (event) => {
  const li = document.createElement("li");
  li.textContent = event.data;
  li.style.fontWeight = "bold";
  replaysEl.prepend(li);
});

// 重連後補送的訊息，用 replay 事件名稱跟正常的即時 tick 區分開來，
// 讓你可以清楚看到「哪些是補送的」。
source.addEventListener("replay", (event) => {
  const li = document.createElement("li");
  li.textContent = `#${event.lastEventId} - ${event.data}（補送）`;
  li.classList.add("replay");
  replaysEl.prepend(li);
});

source.addEventListener("tick", (event) => {
  const li = document.createElement("li");
  li.textContent = `#${event.lastEventId} - ${event.data}`;
  ticksEl.prepend(li);
});
