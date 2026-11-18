// Kutay gemini.google.com/usage sayfasını açtığında kartlar çizilince metni arka plana iletir.
let kutiLast = "";
let kutiTimer = null;

function kutiSend() {
  const seg = kutiExtract(document.body ? document.body.innerText : "");
  if (!seg || seg === kutiLast) return;
  kutiLast = seg;
  chrome.runtime.sendMessage({ tur: "gemini-kartlar", text: seg }).catch(() => {});
}

function kutiSchedule() {
  clearTimeout(kutiTimer);
  kutiTimer = setTimeout(kutiSend, 800);
}

new MutationObserver(kutiSchedule).observe(document.documentElement, { childList: true, subtree: true, characterData: true });
kutiSchedule();
// Sayfa açık kaldıkça "sıfırlanma" ve yüzdeler değişirse yeniden gönder (değişmezse gönderilmez).
setInterval(() => { kutiLast = ""; kutiSend(); }, 5 * 60 * 1000);
