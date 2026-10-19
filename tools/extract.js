const fs = require('fs'), path = require('path');
const OUT = 'E:\\rea\\taskmanager\\tools\\';

function parsePE(b) {
  const peOff = b.readUInt32LE(0x3c);
  const nsec = b.readUInt16LE(peOff + 6);
  const optSize = b.readUInt16LE(peOff + 20), opt = peOff + 24;
  const magic = b.readUInt16LE(opt);
  const ddOff = opt + (magic === 0x20b ? 112 : 96);
  const secOff = opt + optSize, secs = [];
  for (let i = 0; i < nsec; i++) {
    const s = secOff + i * 40;
    secs.push({ name: b.toString('ascii', s, s + 8).replace(/\0+$/, ''), va: b.readUInt32LE(s + 12), vs: b.readUInt32LE(s + 8), raw: b.readUInt32LE(s + 20), rs: b.readUInt32LE(s + 16) });
  }
  const rva2off = r => { for (const s of secs) if (r >= s.va && r < s.va + Math.max(s.vs, s.rs)) return s.raw + (r - s.va); return -1; };
  return { peOff, nsec, opt, magic, ddOff, secs, rva2off };
}

function readResources(b, pe) {
  const resRva = b.readUInt32LE(pe.ddOff + 2 * 8);
  if (!resRva) return [];
  const base = pe.rva2off(resRva);
  const out = [];
  const TYPES = { 3: 'ICON', 4: 'MENU', 5: 'DIALOG', 6: 'STRINGTABLE', 12: 'GROUPICON', 14: 'GROUPICONDIR', 16: 'VERSION', 24: 'MANIFEST' };
  function walk(off, level, names) {
    const nn = b.readUInt16LE(off + 12) + b.readUInt16LE(off + 14);
    for (let i = 0; i < nn; i++) {
      const e = off + 16 + i * 8;
      let id = b.readUInt32LE(e);
      const isName = !!(id & 0x80000000);
      if (isName) { const so = base + (id & 0x7fffffff); id = b.readUInt16LE(so); const chars = []; for (let k = 0; k < id; k++) chars.push(String.fromCharCode(b.readUInt16LE(so + 2 + k * 2))); id = chars.join(''); }
      const sub = b.readUInt32LE(e + 4);
      if (sub & 0x80000000) walk(base + (sub & 0x7fffffff), level + 1, names.concat(id));
      else {
        const e2 = base + sub;
        const dataRva = b.readUInt32LE(e2), size = b.readUInt32LE(e2 + 4);
        out.push({ level, names: names.concat(id), type: names[0], id, dataRva, size });
      }
    }
  }
  walk(base, 0, []);
  return out;
}

function strings(b, min = 5) {
  const ascii = [], uni = [];
  let cur = '';
  for (let i = 0; i < b.length; i++) { const c = b[i]; if (c >= 32 && c < 127) cur += String.fromCharCode(c); else { if (cur.length >= min) ascii.push(cur); cur = ''; } }
  let cu = '';
  for (let i = 0; i + 1 < b.length; i += 2) { const c = b[i], h = b[i + 1]; if (h === 0 && c >= 32 && c < 127) cu += String.fromCharCode(c); else { if (cu.length >= min - 1) uni.push(cu); cu = ''; } }
  return { ascii, uni };
}

function extractMuiStrings(b, pe, res) {
  const rows = [];
  for (const r of res) {
    if (String(r.type) !== '6') continue;
    const off = pe.rva2off(r.dataRva);
    if (off < 0) continue;
    let p = off;
    while (true) {
      if (p + 4 > b.length) break;
      const len = b.readUInt16LE(p), wlen = b.readUInt16LE(p + 2);
      if (!len && !wlen) break;
      if (p + 4 + len * 2 > b.length) break;
      const id = (r.id - 1) * 16 + wlen;
      const s = b.toString('utf16le', p + 4, p + 4 + len * 2);
      rows.push([id, s]);
      p += 4 + len * 2; if (len % 2) p += 2;
    }
  }
  return rows;
}

const targets = [['taskmgr-exe', 'C:\\Windows\\System32\\Taskmgr.exe'], ['taskmgr-mui', 'C:\\Windows\\System32\\en-US\\Taskmgr.exe.mui']];
for (const [name, p] of targets) {
  const b = fs.readFileSync(p);
  const pe = parsePE(b);
  const res = readResources(b, pe);
  const st = strings(b, 5);
  const log = [];
  log.push(`# ${name} ${p} size=${b.length} machine=${b.readUInt16LE(pe.peOff + 4).toString(16)} subsystem=${b.readUInt16LE(pe.opt + 68)}`);
  log.push('# import DLLs: ' + (() => { const ir = pe.rva2off(b.readUInt32LE(pe.ddOff + 8)); if (ir < 0) return 'none'; const d = []; let i = ir; while (b.readUInt32LE(i) || b.readUInt32LE(i + 12)) { d.push(b.toString('ascii', pe.rva2off(b.readUInt32LE(i + 12))).split('\0')[0]); i += 20; } return d.join(', '); })());
  log.push(`# resources: ${res.length}`);
  const byType = {};
  res.forEach(r => { byType[r.type] = (byType[r.type] || 0) + 1; });
  log.push(`# resource types: ${JSON.stringify(byType)}`);
  log.push(`# ascii strings ${st.ascii.length}, utf16 ${st.uni.length}`);
  log.push('===== ASCII ====='); log.push(st.ascii.join('\n'));
  log.push('===== UTF16 ====='); log.push(st.uni.join('\n'));
  if (String(name).includes('mui')) {
    const rows = extractMuiStrings(b, pe, res);
    log.push(`===== STRINGTABLE (${rows.length}) =====`);
    rows.sort((a, c) => a[0] - c[0]).forEach(r => log.push(`${r[0]}\t${r[1]}`));
  }
  fs.writeFileSync(OUT + name + '.txt', log.join('\n'), 'utf8');
  console.log(`${name}: res=${res.length} ascii=${st.ascii.length} uni=${st.uni.length} -> ${name}.txt`);
}

