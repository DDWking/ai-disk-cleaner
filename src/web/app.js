'use strict';

/* ================= 工具函数 ================= */
const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'];
function fmtSize(bytes) {
  if (!bytes) return '0 B';
  let i = 0, v = bytes;
  while (v >= 1024 && i < UNITS.length - 1) { v /= 1024; i++; }
  return v >= 100 ? Math.round(v) + ' ' + UNITS[i] : v.toFixed(1) + ' ' + UNITS[i];
}
function fmtTime(ms) {
  const d = new Date(ms);
  const p = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}
function fmtAge(days) {
  if (days < 1) return '今天';
  if (days < 30) return days + ' 天';
  if (days < 365) return Math.round(days / 30) + ' 个月';
  return (days / 365).toFixed(1) + ' 年';
}
function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

const CATEGORY_ZH = { system: '系统', log: '日志', temp: '临时', media: '媒体', doc: '文档', code: '代码', archive: '压缩包', data: '数据' };

/* ================= Mock 数据生成 ================= */
const GB = 1024 ** 3;
const EXT_BY_CATEGORY = {
  system: ['dll', 'sys', 'exe', 'mui'],
  log: ['log', 'etl', 'txt'],
  temp: ['tmp', 'temp', 'cache', 'bak'],
  media: ['jpg', 'png', 'mp4', 'mp3', 'mov'],
  doc: ['docx', 'xlsx', 'pdf', 'pptx'],
  code: ['py', 'js', 'ts', 'json', 'cs', 'cpp', 'h'],
  archive: ['zip', 'rar', '7z', 'msi'],
  data: ['db', 'sqlite', 'dat', 'bin'],
};

const DIR_SPEC = [
  { name: 'Windows', weight: 24, files: 6, children: [
    { name: 'System32', weight: 12, files: 20 },
    { name: 'WinSxS', weight: 9, files: 14 },
    { name: 'Logs', weight: 1.2, files: 12 },
    { name: 'Temp', weight: 2.5, files: 10 },
  ]},
  { name: 'Program Files', weight: 15, files: 4, children: [
    { name: 'Google', weight: 2.2, files: 8 },
    { name: 'Python311', weight: 1.8, files: 24 },
    { name: 'Microsoft', weight: 3.1, files: 9 },
  ]},
  { name: 'Program Files (x86)', weight: 8, files: 5 },
  { name: 'Users', weight: 30, files: 3, children: [
    { name: 'Public', weight: 2.5, files: 6 },
    { name: '32098', weight: 26, files: 4, children: [
      { name: 'Downloads', weight: 12, files: 15 },
      { name: 'Documents', weight: 6, files: 11 },
      { name: 'Desktop', weight: 3.5, files: 9 },
      { name: 'AppData', weight: 8, files: 18 },
    ]},
  ]},
  { name: 'ProgramData', weight: 6, files: 8 },
  { name: 'pagefile.sys', weight: 8, isFile: true },
  { name: 'hiberfil.sys', weight: 5.5, isFile: true },
];

let fidCounter = 0;
function pick(arr) { return arr[Math.floor(Math.random() * arr.length)]; }
function jitter(min = 0.8, max = 1.25) { return min + Math.random() * (max - min); }
function randomName(ext) {
  const prefixes = ['setup', 'install', 'update', 'cache', 'data', 'temp', 'backup', 'report', 'image', 'video', 'audio', 'notes', 'config', 'log', 'dump', 'archive', 'doc', 'sheet', 'slides'];
  return pick(prefixes) + '_' + Math.floor(Math.random() * 9999) + '.' + ext;
}
function randomSize(category) {
  const baseMB = { media: 400, archive: 250, system: 80, data: 120, doc: 2, code: 0.05, log: 0.2, temp: 0.5 }[category] || 1;
  const factor = Math.pow(10, Math.random() * 2.5); // 幂律：少数大文件
  return baseMB * 1024 * 1024 * factor * jitter();
}
function randomModified() {
  const daysAgo = Math.pow(Math.random(), 1.6) * 730; // 偏近期
  return Date.now() - daysAgo * 86400000;
}

function buildScan(rootName) {
  fidCounter = 0;
  const files = [];
  const tree = { name: rootName, path: rootName, isDir: true, size: 0, children: [] };

  function makeFile(dirPath, dirNode, name, size, category) {
    const modified = randomModified();
    const f = {
      id: fidCounter++, name, path: dirPath + '\\' + name,
      size, modified, category,
      ext: name.slice(name.lastIndexOf('.') + 1),
      ageDays: Math.max(0, Math.floor((Date.now() - modified) / 86400000)),
    };
    files.push(f);
    dirNode.children.push({ name, path: f.path, isDir: false, size, file: f });
    dirNode.size += size;
  }

  function walk(spec, parentPath, parentNode) {
    const node = { name: spec.name, path: parentPath + '\\' + spec.name, isDir: true, size: 0, children: [] };
    parentNode.children.push(node);
    for (let i = 0; i < (spec.files || 0); i++) {
      const cat = pick(Object.keys(EXT_BY_CATEGORY));
      makeFile(node.path, node, randomName(pick(EXT_BY_CATEGORY[cat])), randomSize(cat), cat);
    }
    (spec.children || []).forEach(c => walk(c, node.path, node));
    parentNode.size += node.size;
  }

  DIR_SPEC.forEach(spec => {
    if (spec.isFile) makeFile(rootName, tree, spec.name, spec.weight * GB * jitter(), 'system');
    else walk(spec, rootName, tree);
  });

  return { files, tree };
}

/* ================= Squarified Treemap 算法 ================= */
function squarify(children, x, y, w, h) {
  const sorted = children.slice().sort((a, b) => b.size - a.size);
  const res = [];

  function worst(row, length) {
    const s = row.reduce((a, c) => a + c.size, 0);
    if (s === 0) return Infinity;
    const mx = Math.max(...row.map(c => c.size));
    const mn = Math.min(...row.map(c => c.size));
    if (mn === 0) return Infinity;
    return Math.max((length * length * mx) / (s * s), (s * s) / (length * length * mn));
  }

  function place(nodes, rect) {
    if (nodes.length === 0) return;
    if (nodes.length === 1) { res.push({ x: rect.x, y: rect.y, w: rect.w, h: rect.h, item: nodes[0] }); return; }
    const length = Math.min(rect.w, rect.h);
    const horizontal = rect.w >= rect.h;
    let row = [nodes[0]], i = 1, cur = worst(row, length);
    while (i < nodes.length) {
      const cand = row.concat(nodes[i]);
      const w2 = worst(cand, length);
      if (w2 > cur) break;
      row = cand; cur = w2; i++;
    }
    const rest = nodes.slice(i);
    const rowSum = row.reduce((a, c) => a + c.size, 0);
    const total = rowSum + rest.reduce((a, c) => a + c.size, 0);
    const frac = rowSum / total;

    if (horizontal) {
      const rowW = rect.w * frac;
      let yy = rect.y;
      for (const c of row) {
        const hh = rect.h * (c.size / rowSum);
        res.push({ x: rect.x, y: yy, w: rowW, h: hh, item: c });
        yy += hh;
      }
      place(rest, { x: rect.x + rowW, y: rect.y, w: rect.w - rowW, h: rect.h });
    } else {
      const rowH = rect.h * frac;
      let xx = rect.x;
      for (const c of row) {
        const ww = rect.w * (c.size / rowSum);
        res.push({ x: xx, y: rect.y, w: ww, h: rowH, item: c });
        xx += ww;
      }
      place(rest, { x: rect.x, y: rect.y + rowH, w: rect.w, h: rect.h - rowH });
    }
  }

  place(sorted, { x, y, w, h });
  return res;
}

/* ================= 状态 ================= */
const state = { tree: null, files: [], currentPath: null, sortKey: 'size', sortDesc: true, query: '', treemapRects: [] };

/* ================= DOM ================= */
const $ = id => document.getElementById(id);
const driveSelect = $('driveSelect'), scanBtn = $('scanBtn'), stopBtn = $('stopBtn');
const searchBox = $('searchBox'), headerStats = $('headerStats');
const progressWrap = $('progressWrap'), progressBar = $('progressBar');
const dirTree = $('dirTree'), tbody = document.querySelector('#fileTable tbody');
const crumb = $('crumb'), canvas = $('treemap'), tooltip = $('tooltip');
const fileCount = $('fileCount'), totalSize = $('totalSize'), elapsed = $('elapsed'), cleanHint = $('cleanHint');
const thead = document.querySelector('#fileTable thead');
let scanTimer = null;

/* ================= 树查找 ================= */
function findNode(node, path) {
  if (node.path === path) return node;
  for (const c of (node.children || [])) {
    const r = findNode(c, path);
    if (r) return r;
  }
  return null;
}
function collectFiles(node, out) {
  (node.children || []).forEach(c => { c.isDir ? collectFiles(c, out) : out.push(c.file); });
}

/* ================= 目录树 ================= */
function renderTree() {
  function buildUl(node) {
    const ul = document.createElement('ul');
    node.children.forEach(child => {
      if (!child.isDir) return;
      const li = document.createElement('li');
      li.dataset.path = child.path;
      li.innerHTML = `<span class="node-name"><span class="folder">📁</span>${escapeHtml(child.name)}</span>`;
      if (child.children.some(c => c.isDir)) li.appendChild(buildUl(child));
      ul.appendChild(li);
    });
    return ul;
  }
  dirTree.innerHTML = '';
  const rootLi = document.createElement('li');
  rootLi.dataset.path = state.tree.path;
  rootLi.innerHTML = `<span class="node-name"><span class="folder">🖥️</span>${escapeHtml(state.tree.name)}</span>`;
  if (state.tree.children.some(c => c.isDir)) rootLi.appendChild(buildUl(state.tree));
  dirTree.appendChild(rootLi);
}

/* ================= 文件表格 ================= */
function renderTable() {
  const node = findNode(state.tree, state.currentPath) || state.tree;
  const list = [];
  collectFiles(node, list);
  if (state.query) {
    const q = state.query.toLowerCase();
    const filtered = list.filter(f => f.name.toLowerCase().includes(q) || f.path.toLowerCase().includes(q));
    list.length = 0; list.push(...filtered);
  }
  const { sortKey: key, sortDesc: desc } = state;
  list.sort((a, b) => {
    let r;
    if (key === 'name' || key === 'path' || key === 'category') r = String(a[key]).localeCompare(String(b[key]));
    else r = a[key] - b[key];
    return desc ? -r : r;
  });
  tbody.innerHTML = list.slice(0, 200).map(f => `
    <tr>
      <td class="name-cell">${escapeHtml(f.name)}</td>
      <td class="dim">${escapeHtml(f.path)}</td>
      <td class="num">${fmtSize(f.size)}</td>
      <td class="num">${fmtTime(f.modified)}</td>
      <td>${CATEGORY_ZH[f.category] || f.category}</td>
      <td class="num dim">${fmtAge(f.ageDays)}</td>
    </tr>`).join('');
  fileCount.textContent = list.length.toLocaleString() + ' 个文件';
  totalSize.textContent = fmtSize(list.reduce((s, f) => s + f.size, 0));
}

/* ================= Treemap ================= */
function fitLabel(ctx, text, maxWidth) {
  if (ctx.measureText(text).width <= maxWidth) return text;
  let t = text;
  while (t.length > 1 && ctx.measureText(t + '…').width > maxWidth) t = t.slice(0, -1);
  return t + '…';
}

function renderTreemap() {
  const node = findNode(state.tree, state.currentPath) || state.tree;
  const children = (node.children || []).filter(c => c.size > 0);
  crumb.textContent = node.path;

  const rect = canvas.getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  canvas.width = Math.max(1, rect.width * dpr);
  canvas.height = Math.max(1, rect.height * dpr);
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, rect.width, rect.height);

  if (!children.length) {
    ctx.fillStyle = '#94a3b8'; ctx.font = '13px sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText('空目录', rect.width / 2, rect.height / 2);
    state.treemapRects = [];
    return;
  }

  const items = children.map(c => ({ size: c.size, name: c.name, isDir: c.isDir, path: c.path }));
  const rects = squarify(items, 2, 2, rect.width - 4, rect.height - 4);
  const palette = ['#3b82f6', '#6366f1', '#8b5cf6', '#0ea5e9', '#22c55e', '#eab308', '#f97316', '#ec4899'];

  state.treemapRects = rects.map((r, i) => {
    const pad = 1.5;
    const px = { x: r.x + pad, y: r.y + pad, w: Math.max(0, r.w - pad * 2), h: Math.max(0, r.h - pad * 2) };
    if (px.w > 0 && px.h > 0) {
      ctx.fillStyle = r.item.isDir ? palette[i % palette.length] : '#64748b';
      ctx.fillRect(px.x, px.y, px.w, px.h);
      if (px.w > 34 && px.h > 18) {
        ctx.fillStyle = '#fff'; ctx.font = '11px sans-serif'; ctx.textBaseline = 'top'; ctx.textAlign = 'left';
        ctx.fillText(fitLabel(ctx, r.item.name, px.w - 8), px.x + 4, px.y + 4);
      }
    }
    return { ...r, px };
  });
}

/* ================= 交互 ================= */
function setCurrentPath(path) {
  state.currentPath = path;
  document.querySelectorAll('#dirTree li').forEach(li => li.classList.toggle('selected', li.dataset.path === path));
  renderTable();
  renderTreemap();
}

dirTree.addEventListener('click', e => {
  const li = e.target.closest('li');
  if (li) setCurrentPath(li.dataset.path);
});

thead.addEventListener('click', e => {
  const th = e.target.closest('th');
  if (!th || !th.dataset.key) return;
  if (state.sortKey === th.dataset.key) state.sortDesc = !state.sortDesc;
  else { state.sortKey = th.dataset.key; state.sortDesc = true; }
  thead.querySelectorAll('th').forEach(h => h.classList.remove('sorted', 'desc'));
  th.classList.add('sorted'); if (!state.sortDesc) th.classList.add('desc');
  renderTable();
});

searchBox.addEventListener('input', e => { state.query = e.target.value.trim(); renderTable(); });

canvas.addEventListener('mousemove', e => {
  const r = canvas.getBoundingClientRect();
  const mx = e.clientX - r.left, my = e.clientY - r.top;
  const hit = state.treemapRects.find(x => x.px && mx >= x.px.x && mx <= x.px.x + x.px.w && my >= x.px.y && my <= x.px.y + x.px.h);
  if (hit) {
    tooltip.hidden = false;
    tooltip.textContent = `${hit.item.name} — ${fmtSize(hit.item.size)}`;
    tooltip.style.left = (e.clientX + 12) + 'px';
    tooltip.style.top = (e.clientY + 12) + 'px';
    canvas.style.cursor = hit.item.isDir ? 'pointer' : 'default';
  } else { tooltip.hidden = true; canvas.style.cursor = 'default'; }
});
canvas.addEventListener('mouseleave', () => { tooltip.hidden = true; });
canvas.addEventListener('click', e => {
  const r = canvas.getBoundingClientRect();
  const mx = e.clientX - r.left, my = e.clientY - r.top;
  const hit = state.treemapRects.find(x => x.px && mx >= x.px.x && mx <= x.px.x + x.px.w && my >= x.px.y && my <= x.px.y + x.px.h);
  if (hit && hit.item.isDir) setCurrentPath(hit.item.path);
});

/* ================= 扫描 ================= */
function runScan() {
  const drive = driveSelect.value;
  scanBtn.disabled = true; stopBtn.disabled = false;
  progressWrap.hidden = false; progressBar.style.width = '0%';
  headerStats.textContent = '扫描中…';
  const start = Date.now();
  let pct = 0;
  scanTimer = setInterval(() => {
    pct += Math.random() * 12;
    if (pct >= 100) { pct = 100; clearInterval(scanTimer); finishScan(drive, start); }
    progressBar.style.width = pct.toFixed(0) + '%';
    headerStats.textContent = '扫描中… ' + pct.toFixed(0) + '%';
  }, 90);
}

function finishScan(drive, start) {
  const { files, tree } = buildScan(drive);
  state.tree = tree; state.files = files; state.currentPath = drive; state.query = '';
  searchBox.value = '';
  renderTree();
  setCurrentPath(drive);
  elapsed.textContent = '扫描耗时 ' + ((Date.now() - start) / 1000).toFixed(2) + 's';
  headerStats.textContent = files.length.toLocaleString() + ' 个文件';
  const cleanable = files.filter(f => f.category === 'temp' || f.category === 'log');
  const size = cleanable.reduce((s, f) => s + f.size, 0);
  cleanHint.textContent = cleanable.length
    ? `🧠 AI 建议：${cleanable.length} 个临时/日志文件可清理，约 ${fmtSize(size)}`
    : '🧠 AI 建议：磁盘很干净';
  scanBtn.disabled = false; stopBtn.disabled = true; progressWrap.hidden = true;
}

/* ================= 初始化 ================= */
function init() {
  ['C:', 'D:', 'E:'].forEach(d => {
    const opt = document.createElement('option');
    opt.value = d; opt.textContent = d;
    driveSelect.appendChild(opt);
  });
  thead.querySelector('th[data-key="size"]').classList.add('sorted', 'desc');
  scanBtn.addEventListener('click', runScan);
  stopBtn.addEventListener('click', () => { if (scanTimer) { clearInterval(scanTimer); finishScan(driveSelect.value, Date.now()); } });
  window.addEventListener('resize', renderTreemap);
  setTimeout(runScan, 50);
}
init();
