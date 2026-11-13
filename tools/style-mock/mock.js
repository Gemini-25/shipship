// 그래픽 시안 — 디딤호 가운데 구역을 세 화풍으로 (배 도면 데이터 그대로 · 사람 자리는 지금 그림과 같게)
const X0 = 11, Y0 = 21, CW = 43, CH = 26, C = 32, S = 2;
const W = CW * C, H = CH * C;
const rows = SHIP.rows, SW = SHIP.w;
const at = (x, y) => (y < 0 || y >= rows.length || x < 0 || x >= (rows[y] || '').length) ? ' ' : rows[y][x];
const isWall = ch => ch === '#' || ch === 'H';
const roomOf = new Map();
SHIP.rooms.forEach(r => r.cells.forEach(c => roomOf.set(c, r)));
const roomAt = (x, y) => roomOf.get(y * SW + x);
const px = x => (x - X0) * C, py = y => (y - Y0) * C;

const FLOOR = {
  Corridor: '#566176', Quarters: '#8a6e62', Lounge: '#8b7763', Mess: '#a3937d', Galley: '#a89c8a', Medbay: '#93a8b3',
  Hydroponics: '#5f7a66', Storage: '#6c7480', LifeSupport: '#5f7382', Bridge: '#4c5f7c', Engine: '#4a4f5a', Reactor: '#474c58',
  Power: '#4d525e', Workshop: '#5b5a58', Washroom: '#9bb0ba', Gear: '#636d7b', Airlock: '#5a6472', Comms: '#55637a',
};
const LAMP = {   // 방 종류마다 등 빛깔
  Corridor: [205, 220, 255], Quarters: [255, 214, 170], Lounge: [255, 205, 150], Mess: [255, 228, 190], Galley: [255, 236, 205],
  Medbay: [225, 245, 255], Hydroponics: [255, 170, 230], Storage: [220, 225, 235], LifeSupport: [200, 235, 245], Bridge: [150, 195, 255],
  Engine: [255, 190, 120], Reactor: [150, 230, 255], Power: [255, 205, 130], Workshop: [255, 225, 180], Washroom: [235, 248, 255],
  Gear: [220, 228, 240], Airlock: [210, 225, 245], Comms: [170, 205, 255],
};
const CAT = t => {
  if (/Bed$/.test(t) && t !== 'GrowBed' && t !== 'MedBed') return 'bed';
  if (t === 'MedBed') return 'medbed';
  if (/Seat|VrChair/.test(t)) return 'seat';
  if (/^Table|MeetingBoard/.test(t)) return 'table';
  if (t === 'GameTable') return 'game';
  if (t === 'Workbench') return 'bench';
  if (t === 'Bookshelf') return 'books';
  if (/Shelf|Locker|Cabinet|Cache|CargoNet|ClothesRack|ToolWall|SpiceRack/.test(t)) return 'store';
  if (t === 'OxygenMaskBox' || t === 'EmergencyLight' || t === 'PestTrap') return 'small';
  if (/Stove|Oven/.test(t)) return 'stove';
  if (/Fridge|IceMaker|CoffeeMachine|MealDispenser|Fermenter|EyeWash/.test(t)) return 'steel';
  if (/GrowBed|PlantRack|Composter|NutrientDoser/.test(t)) return 'grow';
  if (t === 'Aquarium') return 'aqua';
  if (t === 'ReactorCore') return 'reactor';
  if (t === 'EngineCore') return 'engine';
  if (/Console|Scanner|Arcade|MusicCorner/.test(t)) return 'screen';
  if (/Battery/.test(t)) return 'battery';
  if (/Dock/.test(t)) return 'dock';
  return 'tech';
};
const HEIGHT = { bed: 7, medbed: 9, seat: 9, table: 10, game: 10, bench: 11, books: 20, store: 18, small: 0, stove: 13, steel: 20, grow: 9, aqua: 14, reactor: 16, engine: 14, screen: 14, battery: 16, dock: 5, tech: 15 };
const PEOPLE = [
  { x: 16.4, y: 29.5, name: '이도윤', shirt: '#4f9d69', skin: '#e8b48a', hair: '#3b2a22' },
  { x: 20.4, y: 35.6, name: '최민준', shirt: '#e0803a', skin: '#c98f66', hair: '#1d1a1a' },
  { x: 24.4, y: 45.5, name: '김서연', shirt: '#c9b68a', skin: '#f0c4a0', hair: '#7a3b4a' },
  { x: 42.4, y: 35.6, name: '박지우', shirt: '#4a7fd0', skin: '#e3ac84', hair: '#2e5d55' },
  { x: 40.4, y: 42.6, name: '정하은', shirt: '#8d6fd1', skin: '#f2c9a8', hair: '#5a3a7a' },
  { x: 44.4, y: 43.3, name: '한지호', shirt: '#b0483f', skin: '#d59e78', hair: '#3a2416' },
];
const furn = SHIP.furniture.map(f => {
  const xs = f.cells.map(c => c[0]), ys = f.cells.map(c => c[1]);
  return { type: f.type, cat: CAT(f.type), x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs) + 1, y1: Math.max(...ys) + 1 };
}).filter(f => f.x1 > X0 - 1 && f.x0 < X0 + CW + 1 && f.y1 > Y0 - 1 && f.y0 < Y0 + CH + 1);

function mk(id) {
  const cv = document.getElementById(id); cv.width = W * S; cv.height = H * S; cv.style.width = W + 'px'; cv.style.height = H + 'px';
  const g = cv.getContext('2d'); g.scale(S, S); return g;
}
function off() { const cv = document.createElement('canvas'); cv.width = W * S; cv.height = H * S; const g = cv.getContext('2d'); g.scale(S, S); return [cv, g]; }
function rr(g, x, y, w, h, r) { g.beginPath(); g.roundRect(x, y, w, h, Math.min(r, w / 2, h / 2)); }
function shade(hex, k) { const n = parseInt(hex.slice(1), 16); let r = n >> 16, gg = (n >> 8) & 255, b = n & 255; const f = v => Math.max(0, Math.min(255, Math.round(k < 0 ? v * (1 + k) : v + (255 - v) * k))); return `rgb(${f(r)},${f(gg)},${f(b)})`; }
function rnd(i) { let t = (i * 2654435761) >>> 0; t ^= t >>> 15; t = Math.imul(t, 2246822519) >>> 0; t ^= t >>> 13; return (t >>> 0) / 4294967296; }

// ── 우주 ──
function space(g) {
  const gr = g.createLinearGradient(0, 0, W, H); gr.addColorStop(0, '#0b1020'); gr.addColorStop(1, '#05070e');
  g.fillStyle = gr; g.fillRect(0, 0, W, H);
  for (let i = 0; i < 260; i++) { const x = rnd(i) * W, y = rnd(i + 999) * H, a = 0.25 + rnd(i + 77) * 0.6, r = rnd(i + 5) < 0.06 ? 1.3 : 0.6; g.fillStyle = `rgba(${200 + rnd(i + 3) * 55},${210 + rnd(i + 4) * 45},255,${a})`; g.beginPath(); g.arc(x, y, r, 0, 7); g.fill(); }
}

// ── 빛 층: 방마다 등 · 벽에 막힌다 (방 모양으로 오려 낸다) · 가장자리는 부드럽게 ──
function lampsOf(r) {
  const out = []; let sx = 0, sy = 0;
  for (const c of r.cells) { const x = c % SW, y = Math.floor(c / SW); sx += x; sy += y; if (x % 5 === 2 && y % 5 === 2) out.push([x + 0.5, y + 0.5]); }
  if (out.length === 0) out.push([sx / r.cells.length + 0.5, sy / r.cells.length + 0.5]);
  return out;
}
function lightLayer(ambient, power, radius) {
  const [cv, g] = off();
  g.fillStyle = ambient; g.fillRect(0, 0, W, H);
  g.globalCompositeOperation = 'lighter';
  for (const r of SHIP.rooms) {
    const col = LAMP[r.type] || [230, 235, 245];
    g.save(); g.beginPath();
    for (const c of r.cells) { const x = c % SW, y = Math.floor(c / SW); g.rect(px(x) - 7, py(y) - 7, C + 14, C + 14); }
    g.clip();
    for (const [lx, ly] of lampsOf(r)) {
      const gx = px(lx), gy = py(ly), R = radius * C;
      const gr = g.createRadialGradient(gx, gy, 0, gx, gy, R);
      gr.addColorStop(0, `rgba(${col[0]},${col[1]},${col[2]},${power})`); gr.addColorStop(0.55, `rgba(${col[0]},${col[1]},${col[2]},${power * 0.38})`); gr.addColorStop(1, 'rgba(0,0,0,0)');
      g.fillStyle = gr; g.fillRect(gx - R, gy - R, 2 * R, 2 * R);
    }
    g.restore();
  }
  return cv;
}
function applyLight(g, layer, blur) {
  g.save(); g.setTransform(1, 0, 0, 1, 0, 0); g.globalCompositeOperation = 'multiply'; g.filter = `blur(${blur * S}px)`; g.drawImage(layer, 0, 0); g.restore();
}
// 벽 곁 바닥의 옅은 그늘 (구석이 어둑하게)
function wallShade(g, strength) {
  const [cv, o] = off();
  o.fillStyle = '#000';
  for (let y = Y0 - 1; y <= Y0 + CH; y++) for (let x = X0 - 1; x <= X0 + CW; x++) if (isWall(at(x, y))) o.fillRect(px(x), py(y), C, C);
  g.save(); g.setTransform(1, 0, 0, 1, 0, 0); g.globalAlpha = strength; g.filter = `blur(${9 * S}px)`; g.drawImage(cv, 0, 0); g.restore();
}
function glow(g, x, y, R, col, a) {
  g.save(); g.globalCompositeOperation = 'screen';
  const gr = g.createRadialGradient(x, y, 0, x, y, R); gr.addColorStop(0, `rgba(${col},${a})`); gr.addColorStop(1, `rgba(${col},0)`);
  g.fillStyle = gr; g.fillRect(x - R, y - R, 2 * R, 2 * R); g.restore();
}
function emissive(g, lift) {
  for (const f of furn) {
    const cx = px((f.x0 + f.x1) / 2), cy = py((f.y0 + f.y1) / 2) - lift * (HEIGHT[f.cat] || 0);
    if (f.cat === 'reactor') glow(g, cx, cy, 150, '90,220,255', 0.75);
    else if (f.cat === 'engine') glow(g, cx, cy, 120, '255,170,90', 0.55);
    else if (f.cat === 'screen') glow(g, cx, cy, 46, '110,200,255', 0.5);
    else if (f.cat === 'aqua') glow(g, cx, cy, 52, '80,230,220', 0.45);
    else if (f.cat === 'battery') glow(g, cx, cy, 30, '120,255,170', 0.3);
    else if (f.cat === 'stove') glow(g, cx, cy, 34, '255,150,80', 0.3);
  }
}
function vignette(g) {
  const gr = g.createRadialGradient(W / 2, H / 2, H * 0.45, W / 2, H / 2, W * 0.72); gr.addColorStop(0, 'rgba(0,0,0,0)'); gr.addColorStop(1, 'rgba(3,5,12,0.55)');
  g.fillStyle = gr; g.fillRect(0, 0, W, H);
}

// ── 바닥 ──
function floors(g, seams) {
  for (const r of SHIP.rooms) {
    const base = FLOOR[r.type] || '#5a6270';
    g.fillStyle = base;
    for (const c of r.cells) { const x = c % SW, y = Math.floor(c / SW); g.fillRect(px(x) - C / 2 - 0.5, py(y) - C / 2 - 0.5, C * 2 + 1, C * 2 + 1); }
  }
  // 문 칸 · 벽 칸 아래도 이웃 방 바닥으로 덮였다. 넓은 판 이음매만 아주 옅게
  if (seams) {
    g.strokeStyle = 'rgba(0,0,0,0.07)'; g.lineWidth = 1;
    for (let x = X0; x <= X0 + CW; x += 2) { g.beginPath(); g.moveTo(px(x) + 0.5, 0); g.lineTo(px(x) + 0.5, H); g.stroke(); }
    for (let y = Y0; y <= Y0 + CH; y += 2) { g.beginPath(); g.moveTo(0, py(y) + 0.5); g.lineTo(W, py(y) + 0.5); g.stroke(); }
  }
}
function clearOutside(g) {   // 배 밖(빈 칸)은 우주로 되돌린다
  const [cv, o] = off(); space(o);
  o.globalCompositeOperation = 'destination-in'; o.fillStyle = '#000'; o.beginPath();
  for (let y = Y0 - 1; y <= Y0 + CH; y++) for (let x = X0 - 1; x <= X0 + CW; x++) if (at(x, y) === ' ') o.rect(px(x) - 0.5, py(y) - 0.5, C + 1, C + 1);
  o.fill();
  g.save(); g.setTransform(1, 0, 0, 1, 0, 0); g.drawImage(cv, 0, 0); g.restore();
}

// ── 가구 (윗면 그림) ──
function drawTop(g, f, x, y, w, h, flat) {
  const c = f.cat, pad = c === 'small' ? 9 : 3;
  x += pad; y += pad; w -= 2 * pad; h -= 2 * pad;
  const fill = (col, r = 5) => { const gr = g.createLinearGradient(x, y, x, y + h); gr.addColorStop(0, shade(col, 0.1)); gr.addColorStop(1, shade(col, -0.08)); g.fillStyle = gr; rr(g, x, y, w, h, r); g.fill(); g.strokeStyle = 'rgba(10,14,22,0.45)'; g.lineWidth = 1; g.stroke(); };
  const horiz = w >= h;
  switch (c) {
    case 'bed': {
      fill('#7a6252', 6);
      const bl = ['#5d7fb8', '#b86a5d', '#6aa37d', '#b89a5d', '#8a6fb8'][(f.x0 * 7 + f.y0 * 3) % 5];
      g.fillStyle = bl; if (horiz) { rr(g, x + w * 0.3, y + 2, w * 0.7 - 2, h - 4, 5); } else { rr(g, x + 2, y + h * 0.3, w - 4, h * 0.7 - 2, 5); } g.fill();
      g.fillStyle = '#efe9df'; if (horiz) { rr(g, x + 3, y + 4, w * 0.22, h - 8, 4); } else { rr(g, x + 4, y + 3, w - 8, h * 0.22, 4); } g.fill();
      break;
    }
    case 'medbed': fill('#e6edf1', 6); g.fillStyle = '#58b7c4'; rr(g, x + 3, y + 3, w - 6, h - 6, 4); g.globalAlpha = 0.35; g.fill(); g.globalAlpha = 1; g.fillStyle = '#e25b5b'; g.fillRect(x + w / 2 - 5, y + h / 2 - 1.5, 10, 3); g.fillRect(x + w / 2 - 1.5, y + h / 2 - 5, 3, 10); break;
    case 'seat': fill('#4b5d7d', 8); g.fillStyle = '#6f84a8'; rr(g, x + 3, y + 3, w - 6, h - 9, 5); g.fill(); break;
    case 'table': fill('#b08a62', 7); g.strokeStyle = 'rgba(255,255,255,0.12)'; g.beginPath(); g.moveTo(x + 5, y + h / 2); g.lineTo(x + w - 5, y + h / 2); g.stroke();
      for (let i = 0; i < Math.floor(w / C); i++) { g.fillStyle = '#f3efe6'; g.beginPath(); g.arc(x + (i + 0.5) * (w / Math.floor(w / C)), y + h / 2, 5, 0, 7); g.fill(); } break;
    case 'game': fill('#6b4f3a', 7); g.fillStyle = '#2f7a64'; rr(g, x + 4, y + 4, w - 8, h - 8, 4); g.fill(); break;
    case 'bench': fill('#8a7860', 4); g.fillStyle = '#c9ced6'; g.fillRect(x + 6, y + 6, 12, 4); g.fillStyle = '#e0803a'; g.fillRect(x + w - 16, y + h - 12, 9, 5); break;
    case 'books': { fill('#5a4636', 4); const cols = ['#c9614f', '#d9a441', '#4f8fc9', '#63a877', '#8d6fd1', '#e0d6c2']; const n = Math.floor((horiz ? w : h) / 5) - 1; for (let i = 0; i < n; i++) { g.fillStyle = cols[(i * 3 + f.x0 + f.y0) % 6]; if (horiz) g.fillRect(x + 4 + i * 5, y + 4, 4, h - 8); else g.fillRect(x + 4, y + 4 + i * 5, w - 8, 4); } break; }
    case 'store': { fill('#76859c', 4); g.strokeStyle = 'rgba(15,20,30,0.35)'; const n = Math.max(1, Math.round((horiz ? w : h) / 16)); for (let i = 1; i < n; i++) { g.beginPath(); if (horiz) { g.moveTo(x + i * w / n, y + 3); g.lineTo(x + i * w / n, y + h - 3); } else { g.moveTo(x + 3, y + i * h / n); g.lineTo(x + w - 3, y + i * h / n); } g.stroke(); } g.fillStyle = 'rgba(255,255,255,0.25)'; g.fillRect(x + 4, y + 3, w - 8, 1.5); break; }
    case 'small': fill(f.type === 'OxygenMaskBox' ? '#e6a23c' : f.type === 'EmergencyLight' ? '#d9534f' : '#8b95a5', 3); break;
    case 'stove': fill('#3a3f49', 4); for (let i = 0; i < Math.max(1, Math.round(w / 16)); i++) { g.strokeStyle = '#ff9a57'; g.lineWidth = 1.6; g.beginPath(); g.arc(x + 9 + i * 15, y + h / 2, 4.5, 0, 7); g.stroke(); } break;
    case 'steel': fill('#d3d8df', 4); g.fillStyle = 'rgba(40,50,65,0.5)'; g.fillRect(x + w - 6, y + 5, 2, h - 10); break;
    case 'grow': { fill('#4b3b2d', 5); const n = Math.max(2, Math.round(w * h / 150)); for (let i = 0; i < n; i++) { const gx = x + 6 + rnd(f.x0 * 31 + f.y0 * 17 + i) * (w - 12), gy = y + 6 + rnd(f.x0 * 13 + f.y0 * 7 + i * 3) * (h - 12); g.fillStyle = ['#6fbf73', '#4fa35e', '#8fd18a'][i % 3]; g.beginPath(); g.arc(gx, gy, 4 + rnd(i) * 2.5, 0, 7); g.fill(); } break; }
    case 'aqua': fill('#1e6f78', 5); g.fillStyle = 'rgba(140,255,240,0.35)'; rr(g, x + 3, y + 3, w - 6, h - 6, 4); g.fill(); g.fillStyle = '#ffb347'; g.beginPath(); g.ellipse(x + w * 0.6, y + h * 0.55, 4, 2.2, 0, 0, 7); g.fill(); break;
    case 'reactor': { fill('#3b4352', 10); const cx = x + w / 2, cy = y + h / 2, R = Math.min(w, h) / 2 - 6; g.fillStyle = '#232a36'; g.beginPath(); g.arc(cx, cy, R, 0, 7); g.fill(); const gr = g.createRadialGradient(cx, cy, 0, cx, cy, R * 0.7); gr.addColorStop(0, '#f4ffff'); gr.addColorStop(0.5, '#5fe0ff'); gr.addColorStop(1, '#1b7fa8'); g.fillStyle = gr; g.beginPath(); g.arc(cx, cy, R * 0.62, 0, 7); g.fill(); g.strokeStyle = '#8fa3bd'; g.lineWidth = 2; g.beginPath(); g.arc(cx, cy, R, 0, 7); g.stroke(); break; }
    case 'engine': { fill('#444b58', 8); for (let i = 0; i < 5; i++) { g.fillStyle = i % 2 ? '#9aa6b8' : '#7c889b'; g.fillRect(x + 10 + i * (w - 20) / 5, y + 8, (w - 20) / 5 - 3, h - 16); } g.fillStyle = '#ffb057'; g.fillRect(x + w - 9, y + h / 2 - 3, 4, 6); break; }
    case 'screen': fill('#2a303c', 4); { const gr = g.createLinearGradient(x, y, x + w, y + h); gr.addColorStop(0, '#7fdcff'); gr.addColorStop(1, '#3f8fd8'); g.fillStyle = gr; rr(g, x + 4, y + 4, w - 8, Math.max(5, h * 0.45), 2); g.fill(); } break;
    case 'battery': fill('#30363f', 4); for (let i = 0; i < 4; i++) { g.fillStyle = i < 3 ? '#7dffb0' : '#3d4652'; g.fillRect(x + 5 + i * ((w - 10) / 4), y + h - 9, (w - 10) / 4 - 2, 4); } break;
    case 'dock': fill('#39414e', 6); g.strokeStyle = '#ffd166'; g.lineWidth = 1.5; g.setLineDash([4, 3]); rr(g, x + 4, y + 4, w - 8, h - 8, 4); g.stroke(); g.setLineDash([]); break;
    default: fill('#4a5262', 4); g.fillStyle = '#ffc857'; g.fillRect(x + 5, y + 5, 5, 3); g.fillStyle = '#7dffb0'; g.fillRect(x + 12, y + 5, 5, 3); g.fillStyle = 'rgba(255,255,255,0.12)'; g.fillRect(x + 4, y + h - 8, w - 8, 2);
  }
}
function softShadow(g, x, y, w, h, blur, a, dy) { g.save(); g.shadowColor = `rgba(4,6,12,${a})`; g.shadowBlur = blur; g.shadowOffsetY = dy * S; g.shadowOffsetX = 0; g.fillStyle = 'rgba(4,6,12,0.01)'; rr(g, x + 3, y + 3, w - 6, h - 6, 5); g.fill(); g.fill(); g.restore(); }

function tag(g, x, y, name) {
  g.font = '600 10.5px Pretendard,sans-serif'; const tw = g.measureText(name).width + 10;
  g.fillStyle = 'rgba(12,16,26,0.72)'; rr(g, x - tw / 2, y - 14, tw, 15, 7.5); g.fill();
  g.fillStyle = '#eef2f8'; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText(name, x, y - 6);
}
function labels(g, alpha) {
  g.font = '700 12px Pretendard,sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle';
  for (const r of SHIP.rooms) {
    if (r.type === 'Corridor') continue;
    let sx = 0; for (const c of r.cells) sx += c % SW; const mx = sx / r.cells.length;
    let ty = 1e9; for (const c of r.cells) { const x = c % SW, y = Math.floor(c / SW); if (Math.abs(x - mx) <= 1.2 && y < ty) ty = y; }
    const cx = px(mx + 0.5), cy = py(ty) + 15;
    if (cx < 40 || cx > W - 40 || cy < 10 || cy > H - 10) continue;
    const tw = g.measureText(r.name).width + 14;
    g.fillStyle = `rgba(10,14,24,${alpha * 0.6})`; rr(g, cx - tw / 2, cy - 9, tw, 18, 9); g.fill();
    g.fillStyle = `rgba(232,238,248,${alpha})`; g.fillText(r.name, cx, cy + 0.5);
  }
}

// ═══ 시안 1 — 지금 도트 그림 + 그래픽 카드 조명 ═══
function style1(img) {
  const g = mk('s1');
  g.imageSmoothingEnabled = false; g.drawImage(img, 0, 0, W, H);
  wallShade(g, 0.38);
  applyLight(g, lightLayer('rgb(112,118,146)', 0.6, 5.6), 8);
  // 밝은 곳 번짐
  g.save(); g.setTransform(1, 0, 0, 1, 0, 0); g.globalCompositeOperation = 'screen'; g.globalAlpha = 0.28; g.filter = `blur(${10 * S}px) brightness(1.15)`; g.drawImage(g.canvas, 0, 0); g.restore();
  emissive(g, 0);
  vignette(g);
}

// ═══ 시안 2 — 매끈한 평면 (위에서 본 선과 면) ═══
function wallSeg(g, x, y, col, wid, oy) {
  {
    g.lineCap = 'round'; g.lineJoin = 'round';
    g.strokeStyle = col; g.lineWidth = wid;
    const cx = px(x) + C / 2, cy = py(y) + C / 2 + oy; let any = false;
    for (const [dx, dy] of [[1, 0], [0, 1], [1, 1], [1, -1]]) {
      const n = at(x + dx, y + dy); if (!isWall(n)) continue;
      if (dx && dy && (isWall(at(x + dx, y)) || isWall(at(x, y + dy)))) continue;   // 대각선은 계단진 선체에서만
      g.beginPath(); g.moveTo(cx, cy); g.lineTo(cx + dx * C, cy + dy * C); g.stroke(); any = true;
    }
    for (const [dx, dy] of [[1, 0], [0, 1], [-1, 0], [0, -1]]) if (at(x + dx, y + dy) === 'D') { g.beginPath(); g.moveTo(cx, cy); g.lineTo(cx + dx * C * 0.5, cy + dy * C * 0.5); g.stroke(); }
    g.beginPath(); g.moveTo(cx, cy); g.lineTo(cx + 0.01, cy); g.stroke();
  }
}
function thinWalls(g) {
  const seg = (x, y, col, wid) => wallSeg(g, x, y, col, wid, 0);
  for (const pass of [0, 1]) for (let y = Y0 - 1; y <= Y0 + CH; y++) for (let x = X0 - 1; x <= X0 + CW; x++) {
    const ch = at(x, y); if (!isWall(ch)) continue;
    if (pass === 0) seg(x, y, ch === 'H' ? '#1a2130' : '#1c2331', ch === 'H' ? C * 1.08 : C * 0.44);
    else if (ch !== 'H') seg(x, y, '#35425a', C * 0.1);
  }
  for (const d of SHIP.doors) {   // 문: 가는 판 둘
    const cx = px(d.x) + C / 2, cy = py(d.y) + C / 2; g.fillStyle = '#8ec5e8';
    const vert = isWall(at(d.x, d.y - 1)) || isWall(at(d.x, d.y + 1));
    if (vert) { rr(g, cx - 3, cy - C / 2, 6, C * 0.42, 3); g.fill(); rr(g, cx - 3, cy + C * 0.08, 6, C * 0.42, 3); g.fill(); }
    else { rr(g, cx - C / 2, cy - 3, C * 0.42, 6, 3); g.fill(); rr(g, cx + C * 0.08, cy - 3, C * 0.42, 6, 3); g.fill(); }
  }
}
function style2() {
  const g = mk('s2');
  space(g); floors(g, true); clearOutside(g);
  wallShade(g, 0.3);
  for (const f of furn) { const x = px(f.x0), y = py(f.y0), w = (f.x1 - f.x0) * C, h = (f.y1 - f.y0) * C; if (f.cat !== 'small') softShadow(g, x, y, w, h, 10 * S, 0.55, 3); drawTop(g, f, x, y, w, h, true); }
  thinWalls(g);
  for (const p of PEOPLE) {
    const x = px(p.x), y = py(p.y);
    g.save(); g.shadowColor = 'rgba(4,6,12,0.6)'; g.shadowBlur = 8 * S; g.shadowOffsetY = 2 * S; g.fillStyle = p.shirt; g.beginPath(); g.ellipse(x, y + 3, 13, 10.5, 0, 0, 7); g.fill(); g.restore();
    g.fillStyle = p.skin; g.beginPath(); g.arc(x, y - 1, 7.4, 0, 7); g.fill();
    g.fillStyle = p.hair; g.beginPath(); g.arc(x, y - 2.8, 7.4, Math.PI, 0); g.fill();
    
  }
  applyLight(g, lightLayer('rgb(150,156,178)', 0.42, 5.6), 10);
  emissive(g, 0);
  labels(g, 0.92);
  for (const p of PEOPLE) tag(g, px(p.x), py(p.y) - 14, p.name);
  vignette(g);
}

// ═══ 시안 3 — 매끈한 반입체 (비스듬한 위 · 벽과 가구에 높이) ═══
function style3() {
  const g = mk('s3'), WH = 18;
  space(g); floors(g, true); clearOutside(g);
  wallShade(g, 0.34);
  const items = [];
  for (let y = Y0 - 1; y <= Y0 + CH + 1; y++) for (let x = X0 - 1; x <= X0 + CW; x++) { const ch = at(x, y); if (isWall(ch) || ch === 'D') items.push({ k: ch === 'D' ? 'door' : 'wall', hull: ch === 'H', x, y, by: y + (ch === 'D' ? 0.8 : 0.72) }); }
  for (const f of furn) items.push({ k: 'furn', f, by: f.y1 - 0.02 });
  for (const p of PEOPLE) items.push({ k: 'person', p, by: p.y + 0.3 });
  items.sort((a, b) => a.by - b.by || (a.k === 'wall' ? 1 : -1));
  // 가구 그림자 먼저 (바닥에)
  for (const f of furn) if (f.cat !== 'small') { const hh = HEIGHT[f.cat]; softShadow(g, px(f.x0) + 2, py(f.y0) + 4, (f.x1 - f.x0) * C, (f.y1 - f.y0) * C, 12 * S, 0.6, 5); }
  for (const p of PEOPLE) { g.fillStyle = 'rgba(4,6,12,0.45)'; g.save(); g.filter = `blur(${2.5 * S}px)`; g.beginPath(); g.ellipse(px(p.x) + 2, py(p.y) + 9, 10, 5, 0, 0, 7); g.fill(); g.restore(); }
  for (const it of items) {
    if (it.k === 'wall') {
      const wid = it.hull ? C * 1.08 : C * 0.42, faceC = it.hull ? '#141a26' : '#1b2230', topC = it.hull ? '#2f3b50' : '#3d4a63';
      for (let k = 0; k <= WH; k += 2) wallSeg(g, it.x, it.y, k > WH - 3 ? shade(faceC, 0.1) : faceC, wid, -k);
      wallSeg(g, it.x, it.y, topC, wid, -WH);
      if (!it.hull) wallSeg(g, it.x, it.y, '#566684', wid * 0.28, -WH - 1.5);
    } else if (it.k === 'door') {
      const cx = px(it.x) + C / 2, cy = py(it.y) + C / 2; const vert = isWall(at(it.x, it.y - 1)) || isWall(at(it.x, it.y + 1));
      const DH = WH - 3;
      if (vert) { g.fillStyle = '#3f6d8d'; rr(g, cx - 3, cy - C / 2 - DH, 6, C + DH, 3); g.fill(); g.fillStyle = '#8ec5e8'; rr(g, cx - 3, cy - C / 2 - DH, 6, C * 0.44, 3); g.fill(); rr(g, cx - 3, cy + C * 0.06 - DH, 6, C * 0.44, 3); g.fill(); }
      else { g.fillStyle = 'rgba(120,190,235,0.55)'; rr(g, cx - C / 2, cy - DH - 3, C, DH + 6, 3); g.fill(); g.fillStyle = '#8ec5e8'; rr(g, cx - C / 2, cy - DH - 3, C * 0.45, 6, 3); g.fill(); rr(g, cx + C * 0.05, cy - DH - 3, C * 0.45, 6, 3); g.fill(); g.fillStyle = 'rgba(255,255,255,0.35)'; g.fillRect(cx - C / 2 + 2, cy - DH + 5, C - 4, 1.2); }
    } else if (it.k === 'furn') {
      const f = it.f, hh = HEIGHT[f.cat], x = px(f.x0), y = py(f.y0), w = (f.x1 - f.x0) * C, h = (f.y1 - f.y0) * C;
      if (hh > 0) { const pad = 3; g.fillStyle = 'rgba(18,22,32,0.92)'; rr(g, x + pad, y + pad + h - 2 * pad - 6, w - 2 * pad, 6 + 0.5, 3); g.fill(); g.fillStyle = frontCol(f.cat); rr(g, x + pad, y + h - pad - hh, w - 2 * pad, hh, 3); g.fill(); g.fillStyle = 'rgba(0,0,0,0.18)'; g.fillRect(x + pad, y + h - pad - 2, w - 2 * pad, 2); }
      drawTop(g, f, x, y - hh, w, h, false);
    } else {
      const p = it.p, x = px(p.x), y = py(p.y);
      g.fillStyle = shade(p.shirt, -0.25); rr(g, x - 6, y - 4, 12, 13, 4); g.fill();          // 다리 · 몸통 앞면
      g.fillStyle = p.shirt; rr(g, x - 7.5, y - 15, 15, 14, 6); g.fill();                     // 윗몸
      g.fillStyle = 'rgba(255,255,255,0.18)'; rr(g, x - 7.5, y - 15, 15, 5, 5); g.fill();
      g.fillStyle = p.skin; g.beginPath(); g.arc(x, y - 21, 7, 0, 7); g.fill();               // 머리
      g.fillStyle = p.hair; g.beginPath(); g.arc(x, y - 22.6, 7.2, Math.PI * 0.95, Math.PI * 2.05); g.fill();
      g.fillStyle = '#1b1f2a'; g.beginPath(); g.arc(x - 2.4, y - 20, 0.9, 0, 7); g.arc(x + 2.4, y - 20, 0.9, 0, 7); g.fill();
    }
  }
  applyLight(g, lightLayer('rgb(140,146,170)', 0.5, 5.6), 9);
  g.save(); g.setTransform(1, 0, 0, 1, 0, 0); g.globalCompositeOperation = 'screen'; g.globalAlpha = 0.18; g.filter = `blur(${12 * S}px)`; g.drawImage(g.canvas, 0, 0); g.restore();
  emissive(g, 1);
  labels(g, 0.9);
  for (const p of PEOPLE) tag(g, px(p.x), py(p.y) - 32, p.name);
  vignette(g);
}
function frontCol(c) { return ({ bed: '#5c493c', medbed: '#b9c3ca', seat: '#35435c', table: '#7d5f41', game: '#4d3829', bench: '#62543f', books: '#3f3025', store: '#515e73', stove: '#262a32', steel: '#a2a9b3', grow: '#33281e', aqua: '#134a52', reactor: '#262d39', engine: '#2c323d', screen: '#1b2029', battery: '#20252d', dock: '#262c36', tech: '#31384599'.slice(0, 7) })[c] || '#333a47'; }

const img = new Image();
img.onload = () => { style1(img); style2(); style3(); document.body.dataset.ready = '1'; };
img.src = 'now.png';
