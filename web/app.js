/* PM230 Label-Studio: freies Positionieren, Text/Bild/PDF, 1:1-Vorschau, Druck 1-Bit */
const MM = 8, PRINT_DOTS = 384, PAD_X = 3 * MM, PAD_Y = 2 * MM, PAPER_MM = 54;
const cv = document.getElementById('cv'), ctx = cv.getContext('2d');

const doc = {
  autoHeight: true, heightMM: 40, density: 10, copies: 1, dither: true, invert: false,
  contrast: 1, snap: 0.5, zoom: 2.5, els: [], sel: null, nextId: 1,
  pdf: { pages: [], idx: 0 }
};
const FONTS = ['Segoe UI', 'Arial', 'Arial Black', 'Verdana', 'Tahoma', 'Georgia', 'Times New Roman',
  'Courier New', 'Consolas', 'Impact', 'Franklin Gothic Medium', 'Bahnschrift'];

const $ = id => document.getElementById(id);
const mm2px = v => v * MM;
const lblDots = () => doc.autoHeight ? Math.max(8 * MM, Math.ceil(contentBox().b * MM)) : Math.round(doc.heightMM * MM);
const canvasH = () => lblDots() + 2 * PAD_Y;

function contentBox() {
  let l = 999, t = 999, r = 0, b = 0;
  doc.els.forEach(e => { measure(e); l = Math.min(l, e.x); t = Math.min(t, e.y); r = Math.max(r, e.x + e.w); b = Math.max(b, e.y + e.h); });
  return doc.els.length ? { l, t, r, b } : { l: 0, t: 0, r: PRINT_DOTS / MM, b: doc.heightMM };
}

/* ---------------- elemente ---------------- */

function measure(e) {
  if (e.type !== 'text') return;
  const g = cv.getContext('2d'), px = e.sizeMM * MM;
  g.font = (e.bold ? 'bold ' : '') + px + 'px "' + e.font + '"';
  const lines = String(e.text).split('\n');
  let w = 0;
  lines.forEach(t => { w = Math.max(w, g.measureText(t === '' ? ' ' : t).width); });
  e.lines = lines; e.w = Math.max(1, w / MM); e.h = Math.max(1, lines.length * px * e.lh / MM);
}

function addText() {
  const e = { id: doc.nextId++, type: 'text', x: 2, y: 2, rot: 0, text: 'Neuer Text', font: 'Segoe UI', sizeMM: 6, bold: false, lh: 1.2, align: 'left' };
  doc.els.push(e); select(e.id); sync();
}
function addImage(src, name) {
  const im = new Image();
  im.onload = () => {
    const natMM = im.naturalWidth / MM;
    const w = Math.min(PRINT_DOTS / MM, natMM), h = w * im.naturalHeight / im.naturalWidth;
    const e = { id: doc.nextId++, type: 'img', x: 0, y: 0, rot: 0, w, h, img: im, name: name || 'Bild' };
    e.x = (PRINT_DOTS / MM - w) / 2; e.y = 2;
    doc.els.push(e); select(e.id); sync();
  };
  im.src = src;
}
function select(id) { doc.sel = id; showProps(); drawLayers(); draw(); }
function sel_() { return doc.els.find(e => e.id === doc.sel) || null; }

/* ---------------- zeichnen ---------------- */

function drawElements(g, els) {
  els.forEach(e => {
    if (e.type === 'text') measure(e);
    g.save();
    g.translate(mm2px(e.x + e.w / 2), mm2px(e.y + e.h / 2));
    if (e.rot) g.rotate(e.rot * Math.PI / 180);
    if (e.type === 'img' && e.img) g.drawImage(e.img, -mm2px(e.w / 2), -mm2px(e.h / 2), mm2px(e.w), mm2px(e.h));
    else if (e.type === 'text') {
      const px = e.sizeMM * MM;
      g.font = (e.bold ? 'bold ' : '') + px + 'px "' + e.font + '"';
      g.textBaseline = 'top'; g.fillStyle = '#000';
      const lh = px * e.lh, top = -mm2px(e.h / 2);
      e.lines.forEach((t, i) => {
        let x = -mm2px(e.w / 2);
        const tw = g.measureText(t).width;
        if (e.align === 'center') x = -tw / 2;
        if (e.align === 'right') x = mm2px(e.w / 2) - tw;
        g.fillText(t, x, top + i * lh);
      });
    }
    g.restore();
  });
}

function draw() {
  cv.width = PRINT_DOTS + 2 * PAD_X; cv.height = canvasH();
  cv.style.width = cv.width * doc.zoom + 'px'; cv.style.height = cv.height * doc.zoom + 'px';
  ctx.fillStyle = '#c9cdd3'; ctx.fillRect(0, 0, cv.width, cv.height);
  ctx.fillStyle = '#fff'; ctx.fillRect(PAD_X, PAD_Y, PRINT_DOTS, lblDots());
  ctx.strokeStyle = '#dfe3e8'; ctx.lineWidth = 1;
  for (let m = 0; m <= PRINT_DOTS / MM; m += 5) {
    const x = PAD_X + m * MM + .5;
    ctx.beginPath(); ctx.moveTo(x, PAD_Y); ctx.lineTo(x, PAD_Y + lblDots()); ctx.stroke();
  }
  for (let m = 0; m <= lblDots() / MM; m += 5) {
    const y = PAD_Y + m * MM + .5;
    ctx.beginPath(); ctx.moveTo(PAD_X, y); ctx.lineTo(PAD_X + PRINT_DOTS, y); ctx.stroke();
  }
  ctx.save(); ctx.translate(PAD_X, PAD_Y);
  drawElements(ctx, doc.els);
  const e = sel_();
  if (e) {
    measure(e);
    ctx.save();
    ctx.translate(mm2px(e.x + e.w / 2), mm2px(e.y + e.h / 2));
    if (e.rot) ctx.rotate(e.rot * Math.PI / 180);
    ctx.strokeStyle = '#3ea6ff'; ctx.lineWidth = 1; ctx.setLineDash([4, 3]);
    ctx.strokeRect(-mm2px(e.w / 2) - .5, -mm2px(e.h / 2) - .5, mm2px(e.w) + 1, mm2px(e.h) + 1);
    ctx.setLineDash([]); ctx.fillStyle = '#3ea6ff';
    [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(c =>
      ctx.fillRect(c[0] * mm2px(e.w / 2) - 3, c[1] * mm2px(e.h / 2) - 3, 6, 6));
    ctx.beginPath(); ctx.moveTo(0, -mm2px(e.h / 2)); ctx.lineTo(0, -mm2px(e.h / 2) - 22); ctx.stroke();
    ctx.beginPath(); ctx.arc(0, -mm2px(e.h / 2) - 26, 5, 0, 7); ctx.fill();
    ctx.restore();
  }
  ctx.restore();
  const b = contentBox();
  $('paperInfo').textContent = (PRINT_DOTS / MM) + ' mm Druckfeld (' + PRINT_DOTS + ' dots) · Label ' +
    (lblDots() / MM).toFixed(1) + ' mm hoch · Inhalt ' + (b.r - b.l).toFixed(1) + ' × ' + (b.b - b.t).toFixed(1) + ' mm';
}

/* ---------------- interaktion ---------------- */

let drag = null;
function local(ev) {
  const r = cv.getBoundingClientRect();
  return { x: (ev.clientX - r.left) / doc.zoom - PAD_X, y: (ev.clientY - r.top) / doc.zoom - PAD_Y };
}
function hit(p) {
  for (let i = doc.els.length - 1; i >= 0; i--) {
    const e = doc.els[i]; measure(e);
    const cx = mm2px(e.x + e.w / 2), cy = mm2px(e.y + e.h / 2), a = -e.rot * Math.PI / 180;
    const dx = p.x - cx, dy = p.y - cy;
    const ux = dx * Math.cos(a) - dy * Math.sin(a), uy = dx * Math.sin(a) + dy * Math.cos(a);
    if (Math.abs(ux) <= mm2px(e.w / 2) + 2 && Math.abs(uy) <= mm2px(e.h / 2) + 2) return e;
    if (Math.hypot(dx, dy - mm2px(e.h / 2) - 26) < 8) return { el: e, handle: 'rot' };
  }
  return null;
}
cv.addEventListener('pointerdown', ev => {
  const p = local(ev), h = hit(p);
  if (!h) { select(null); return; }
  const e = h.el || h;
  if (h.handle === 'rot') { drag = { e, mode: 'rot', a0: Math.atan2(p.y - mm2px(e.y + e.h / 2), p.x - mm2px(e.x + e.w / 2)), r0: e.rot }; }
  else {
    select(e.id);
    drag = { e, mode: 'move', px: p.x, py: p.y, x0: e.x, y0: e.y };
  }
  cv.setPointerCapture(ev.pointerId);
});
cv.addEventListener('pointermove', ev => {
  if (!drag) return;
  const p = local(ev), e = drag.e;
  if (drag.mode === 'move') {
    let nx = drag.x0 + (p.x - drag.px) / MM, ny = drag.y0 + (p.y - drag.py) / MM;
    if (doc.snap && !ev.shiftKey) { nx = Math.round(nx / doc.snap) * doc.snap; ny = Math.round(ny / doc.snap) * doc.snap; }
    e.x = Math.round(nx * 10) / 10; e.y = Math.round(ny * 10) / 10;
  } else if (drag.mode === 'rot') {
    const a = Math.atan2(p.y - mm2px(e.y + e.h / 2), p.x - mm2px(e.x + e.w / 2));
    e.rot = Math.round(drag.r0 + (a - drag.a0) * 180 / Math.PI) % 360;
    $('rot').value = (e.rot + 360) % 360; $('rotVal').textContent = ((e.rot + 360) % 360) + '°';
  }
  syncInputs(); draw();
});
cv.addEventListener('pointerup', () => drag = null);
document.addEventListener('keydown', ev => {
  const e = sel_(); if (!e) return;
  if (ev.target.tagName === 'TEXTAREA' || ev.target.tagName === 'INPUT') return;
  const s = ev.shiftKey ? 0.1 : (doc.snap || 0.5);
  if (ev.key === 'ArrowLeft') { e.x -= s; } else if (ev.key === 'ArrowRight') { e.x += s; }
  else if (ev.key === 'ArrowUp') { e.y -= s; } else if (ev.key === 'ArrowDown') { e.y += s; }
  else if (ev.key === 'Delete') { del(); return; }
  else if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'd') { dup(); return; }
  else return;
  e.x = Math.round(e.x * 10) / 10; e.y = Math.round(e.y * 10) / 10;
  ev.preventDefault(); syncInputs(); draw();
});

/* ---------------- eigenschaften ---------------- */

function showProps() {
  const e = sel_();
  $('propsText').hidden = !(e && e.type === 'text');
  $('propsImg').hidden = !(e && e.type === 'img');
  $('pdfRow').hidden = !(e && e.type === 'img' && e.pdf);
  if (!e) return;
  syncInputs();
}
function syncInputs() {
  const e = sel_(); if (!e) return;
  if (e.type === 'text') {
    $('txt').value = e.text; $('font').value = e.font; $('sizeMM').value = e.sizeMM;
    $('bold').checked = e.bold; $('lh').value = e.lh; $('lhVal').textContent = e.lh.toFixed(2);
    document.querySelectorAll('#align button').forEach(b => b.classList.toggle('on', b.dataset.a === e.align));
  } else {
    $('imgW').value = e.w.toFixed(1); $('imgH').value = e.h.toFixed(1);
  }
  $('rot').value = (e.rot + 360) % 360; $('rotVal').textContent = ((e.rot + 360) % 360) + '°';
  $('posX').value = e.x.toFixed(1); $('posY').value = e.y.toFixed(1);
}
function drawLayers() {
  const s = $('layers'); s.innerHTML = '';
  doc.els.forEach(e => {
    const o = document.createElement('option');
    o.value = e.id;
    o.textContent = (e.type === 'text' ? 'T  ' : 'B  ') + (e.type === 'text' ? (e.text.split('\n')[0] || 'Text').slice(0, 18) : (e.name || 'Bild'));
    if (e.id === doc.sel) o.selected = true;
    s.appendChild(o);
  });
}
$('layers').onchange = e => select(+e.target.value);
$('txt').oninput = e => { const t = sel_(); if (t && t.type === 'text') { t.text = e.target.value; sync(); } };
$('font').onchange = e => { const t = sel_(); if (t) { t.font = e.target.value; sync(); } };
$('sizeMM').oninput = e => { const t = sel_(); if (t) { t.sizeMM = +e.target.value; sync(); } };
$('bold').onchange = e => { const t = sel_(); if (t) { t.bold = e.target.checked; sync(); } };
$('lh').oninput = e => { const t = sel_(); if (t) { t.lh = +e.target.value; $('lhVal').textContent = t.lh.toFixed(2); sync(); } };
document.querySelectorAll('#align button').forEach(b => b.onclick = () => { const t = sel_(); if (t) { t.align = b.dataset.a; sync(); } });
$('imgW').oninput = e => { const t = sel_(); if (t && t.type === 'img') { const r = t.h / t.w; t.w = +e.target.value; if ($('keepAspect').checked) t.h = t.w * r; sync(); } };
$('imgH').oninput = e => { const t = sel_(); if (t && t.type === 'img') { const r = t.w / t.h; t.h = +e.target.value; if ($('keepAspect').checked) t.w = t.h * r; sync(); } };
$('rot').oninput = e => { const t = sel_(); if (t) { t.rot = +e.target.value; $('rotVal').textContent = t.rot + '°'; sync(); } };
$('posX').oninput = e => { const t = sel_(); if (t) { t.x = +e.target.value; draw(); } };
$('posY').oninput = e => { const t = sel_(); if (t) { t.y = +e.target.value; draw(); } };
document.querySelectorAll('[data-pos]').forEach(b => b.onclick = () => {
  const e = sel_(); if (!e) return; measure(e);
  const W = PRINT_DOTS / MM, H = lblDots() / MM, p = b.dataset.pos;
  if (p[1] === 'l') e.x = 0; if (p[1] === 'c') e.x = (W - e.w) / 2; if (p[1] === 'r') e.x = W - e.w;
  if (p[0] === 't') e.y = 0; if (p[0] === 'c') e.y = (H - e.h) / 2; if (p[0] === 'b') e.y = H - e.h;
  e.x = Math.round(e.x * 10) / 10; e.y = Math.round(e.y * 10) / 10; sync();
});
document.querySelectorAll('[data-fit]').forEach(b => b.onclick = () => {
  const e = sel_(); if (!e) return; measure(e);
  if (b.dataset.fit === 'width') { const r = e.h / e.w; e.w = PRINT_DOTS / MM; e.h = e.w * r; }
  else { const H = lblDots() / MM, r = e.w / e.h; e.h = H; e.w = e.h * r; }
  e.x = 0; e.y = 0; sync();
});
document.querySelectorAll('[data-add]').forEach(b => b.onclick = () => {
  if (b.dataset.add === 'text') addText(); else { $('filePick').dataset.want = b.dataset.add; $('filePick').click(); }
});
function del() { const i = doc.els.findIndex(e => e.id === doc.sel); if (i >= 0) doc.els.splice(i, 1); select(doc.els.length ? doc.els[Math.max(0, i - 1)].id : null); }
function dup() { const e = sel_(); if (!e) return; const c = Object.assign({}, e, { id: doc.nextId++, x: e.x + 2, y: e.y + 2 }); doc.els.push(c); select(c.id); }
$('del').onclick = del; $('dup').onclick = dup;
$('front').onclick = () => { const i = doc.els.findIndex(e => e.id === doc.sel); if (i >= 0) { const [e] = doc.els.splice(i, 1); doc.els.push(e); sync(); } };
$('back').onclick = () => { const i = doc.els.findIndex(e => e.id === doc.sel); if (i >= 0) { const [e] = doc.els.splice(i, 1); doc.els.unshift(e); sync(); } };
$('snapToggle').onclick = e => { doc.snap = doc.snap ? 0 : 0.5; e.target.classList.toggle('on', !!doc.snap); };
$('zoom').oninput = e => { doc.zoom = +e.target.value; $('zoomVal').textContent = doc.zoom + '×'; draw(); };
$('contrast').oninput = e => { doc.contrast = +e.target.value; $('contrastVal').textContent = doc.contrast.toFixed(2); };
$('paperPreset').onchange = e => {
  const v = e.target.value;
  if (v === '54a') { doc.autoHeight = true; }
  else { doc.autoHeight = false; doc.heightMM = +v.split('x')[1]; $('heightMM').value = doc.heightMM; }
  draw();
};
$('heightMM').oninput = e => { doc.heightMM = +e.target.value; doc.autoHeight = false; $('paperPreset').value = '54a'; draw(); };
$('density').oninput = e => { doc.density = +e.target.value; $('densityVal').textContent = doc.density; };
$('copies').oninput = e => doc.copies = Math.max(1, +e.target.value);
$('dither').onchange = e => doc.dither = e.target.checked;
$('invert').onchange = e => doc.invert = e.target.checked;
$('pdfPrev').onclick = () => pageStep(-1); $('pdfNext').onclick = () => pageStep(1);
function pageStep(d) {
  const e = sel_(); if (!e || !e.pdf) return;
  doc.pdf.idx = (doc.pdf.idx + d + e.pdf.length) % e.pdf.length;
  $('pdfPage').textContent = doc.pdf.idx + 1;
  const im = new Image(); im.onload = () => { e.img = im; sync(); }; im.src = e.pdf[doc.pdf.idx];
}

function sync() { drawLayers(); syncInputs(); draw(); }

/* ---------------- dateien ---------------- */

async function handleFiles(files) {
  for (const f of files) {
    if (/pdf$/i.test(f.name)) {
      toast('PDF wird gerendert ...');
      const r = await fetch('/api/pdf', { method: 'POST', body: await f.arrayBuffer() });
      const j = await r.json();
      if (!j.ok) { toast(j.error, true); continue; }
      doc.pdf = { pages: j.pages, idx: 0 };
      const im = new Image();
      im.onload = () => {
        const w = PRINT_DOTS / MM, h = w * im.naturalHeight / im.naturalWidth;
        const e = { id: doc.nextId++, type: 'img', x: 0, y: 0, rot: 0, w, h, img: im, name: f.name, pdf: j.pages };
        doc.els.push(e); doc.pdf.idx = 0; select(e.id); sync();
        toast(j.pages.length + ' PDF-Seite(n) eingefuegt - mit Seite vor/zurueck wechseln');
      };
      im.src = j.pages[0];
    } else if (/^image\//.test(f.type)) {
      const rd = new FileReader();
      rd.onload = () => { addImage(rd.result, f.name); toast('Bild eingefuegt'); };
      rd.readAsDataURL(f);
    }
  }
}
$('filePick').onchange = e => { handleFiles([...e.target.files]); e.target.value = ''; };
document.addEventListener('dragover', e => e.preventDefault());
document.addEventListener('drop', e => { e.preventDefault(); if (e.dataTransfer.files.length) handleFiles([...e.dataTransfer.files]); });

/* ---------------- rastern + drucken ---------------- */

function raster() {
  const H = lblDots(), c = document.createElement('canvas');
  c.width = PRINT_DOTS; c.height = H;
  const g = c.getContext('2d');
  g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height);
  drawElements(g, doc.els);
  const d = g.getImageData(0, 0, c.width, c.height).data;
  const gray = new Float32Array(c.width * c.height);
  for (let i = 0; i < gray.length; i++) {
    let v = 0.299 * d[i * 4] + 0.587 * d[i * 4 + 1] + 0.114 * d[i * 4 + 2];
    v = (v - 128) * doc.contrast + 128;
    if (doc.invert) v = 255 - v;
    gray[i] = v < 0 ? 0 : v > 255 ? 255 : v;
  }
  const wb = c.width / 8, out = new Uint8Array(wb * H).fill(0xFF);
  const black = (x, y) => { out[y * wb + (x >> 3)] &= ~(1 << (7 - (x & 7))); };
  if (!doc.dither) {
    for (let y = 0; y < H; y++) for (let x = 0; x < c.width; x++) if (gray[y * c.width + x] < 128) black(x, y);
  } else {
    for (let y = 0; y < H; y++) for (let x = 0; x < c.width; x++) {
      const i = y * c.width + x, old = gray[i], nv = old < 128 ? 0 : 255, err = old - nv;
      gray[i] = nv; if (nv === 0) black(x, y);
      if (x + 1 < c.width) gray[i + 1] += err * 7 / 16;
      if (y + 1 < H) {
        if (x > 0) gray[i + c.width - 1] += err * 3 / 16;
        gray[i + c.width] += err * 5 / 16;
        if (x + 1 < c.width) gray[i + c.width + 1] += err / 16;
      }
    }
  }
  return { bytes: out, wb, h: H };
}

$('printBtn').onclick = async () => {
  const r = raster();
  let bin = '';
  for (let i = 0; i < r.bytes.length; i++) bin += String.fromCharCode(r.bytes[i]);
  const q = '?wb=' + r.wb + '&h=' + r.h + '&density=' + doc.density + '&copies=' + doc.copies + '&paper=' + PAPER_MM;
  $('printBtn').disabled = true; toast('drucke ' + r.h / MM + ' mm Label ...');
  try {
    const res = await fetch('/api/print' + q, { method: 'POST', body: new Uint8Array(r.bytes).buffer });
    const j = await res.json();
    if (j.ok) toast('fertig - Drucker meldet: ' + (j.reply || 'ok') + '  (' + j.bytes + ' Bytes)');
    else toast('Drucker Fehler: ' + j.error, true);
  } catch (e) { toast('Drucken fehlgeschlagen: ' + e, true); }
  $('printBtn').disabled = false; info();
};

let toastT = null;
function toast(msg, bad) {
  const t = $('toast'); t.textContent = msg; t.className = 'show' + (bad ? ' bad' : '');
  clearTimeout(toastT); toastT = setTimeout(() => t.className = '', bad ? 9000 : 4000);
}
async function info() {
  try {
    const j = await (await fetch('/api/info')).json();
    const s = $('stat');
    s.textContent = 'Drucker ' + j.link + ' · Status ' + j.status + ' · Akku ' + j.battery + ' %';
    s.className = 'stat ' + (/ready/.test(j.status) ? 'ok' : 'bad');
  } catch (e) { $('stat').textContent = 'Editor offline'; $('stat').className = 'stat bad'; }
}

/* ---------------- start ---------------- */

$('font').innerHTML = FONTS.map(f => '<option>' + f + '</option>').join('');
addText();
const e0 = sel_(); e0.text = 'Etikett'; e0.sizeMM = 9; e0.bold = true; e0.align = 'center'; e0.y = 4;
select(e0.id);
setInterval(info, 15000); info();
