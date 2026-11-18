// Kuti Dock – Gemini kullanımı (MV3 service worker)
// Veriyi YALNIZCA 127.0.0.1:47615'teki Kuti Dock'a, dock'un ürettiği gizli değerle gönderir. Başka hiçbir yere gönderilmez.
// Çerez/oturum okunmaz; arka plan okuması Chrome'un normal isteğiyle (Kutay'ın kendi oturumu) sayfayı ister ve yalnızca
// görünür metinde iki kartı arar. Sayfa kartları JavaScript ile çiziyorsa bu bulunamaz; o zaman dock satıra tıklanınca sayfayı açar.
importScripts("extract.js");

const DOCK = "http://127.0.0.1:47615";
const ARKA_PLAN_ARALIK_DK = 10;

async function gizli() {
  try {
    const r = await fetch(chrome.runtime.getURL("dock-gizli.json"), { cache: "no-store" });
    return (await r.json()).gizli;
  } catch (e) { return null; }
}

async function dock(path, body) {
  const g = await gizli();
  if (!g) return null;
  try {
    const r = await fetch(DOCK + path, {
      method: body ? "POST" : "GET",
      headers: { "X-KutiDock-Gizli": g, "Content-Type": "application/json" },
      body: body ? JSON.stringify(body) : undefined,
    });
    return r.ok ? await r.json() : null;
  } catch (e) { return null; } // dock kapalı
}

function gorunurMetin(html) {
  return html
    .replace(/<script[\s\S]*?<\/script>/gi, " ")
    .replace(/<style[\s\S]*?<\/style>/gi, " ")
    .replace(/<[^>]+>/g, "\n")
    .replace(/&nbsp;/g, " ").replace(/&amp;/g, "&").replace(/&#39;/g, "'").replace(/&quot;/g, '"')
    .replace(/\n\s*\n+/g, "\n");
}

async function arkaPlanOku() {
  let seg = null;
  try {
    const r = await fetch("https://gemini.google.com/usage", { credentials: "include", cache: "no-store" });
    if (r.ok) seg = kutiExtract(gorunurMetin(await r.text()));
  } catch (e) { seg = null; }
  await chrome.storage.local.set({ sonArkaPlan: Date.now(), arkaPlanOk: !!seg });
  await dock("/gemini", seg ? { text: seg, source: "arka plan", backgroundOk: true } : { backgroundOk: false });
}

async function kontrol() {
  const istek = await dock("/gemini/istek");
  const { sonArkaPlan = 0 } = await chrome.storage.local.get("sonArkaPlan");
  if ((istek && istek.yenile) || Date.now() - sonArkaPlan >= ARKA_PLAN_ARALIK_DK * 60 * 1000) await arkaPlanOku();
}

chrome.runtime.onMessage.addListener((m, sender) => {
  if (m && m.tur === "gemini-kartlar" && sender.tab && typeof m.text === "string" && m.text.length <= 4000) {
    chrome.storage.local.get("arkaPlanOk").then(({ arkaPlanOk }) =>
      dock("/gemini", { text: m.text, source: "sayfa", backgroundOk: !!arkaPlanOk }));
  }
});

chrome.alarms.create("kuti-gemini", { periodInMinutes: 1 });
chrome.alarms.onAlarm.addListener((a) => { if (a.name === "kuti-gemini") kontrol(); });
chrome.runtime.onStartup.addListener(kontrol);
chrome.runtime.onInstalled.addListener(kontrol);
