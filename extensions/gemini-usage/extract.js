// Kuti Dock – Gemini kullanımı: sayfa metninden yalnızca iki kartın bölümünü ayırır (e-posta, ad vb. gönderilmez).
const KUTI_HEADS = /(Current usage|Geçerli kullanım|Mevcut kullanım|Güncel kullanım)[\s\S]*?(Weekly limit|Weekly usage|Haftalık (?:sınır|limit|kullanım))[\s\S]{0,300}/i;

function kutiExtract(text) {
  if (!text) return null;
  const m = text.match(KUTI_HEADS);
  if (!m) return null;
  const seg = m[0];
  return seg.length > 1500 ? seg.slice(0, 1500) : seg;
}
if (typeof self !== "undefined") self.kutiExtract = kutiExtract;
