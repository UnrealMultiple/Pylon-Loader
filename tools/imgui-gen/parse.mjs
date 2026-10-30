/**
 * Parse preprocessed imgui headers into a declaration model:
 *  enums (evaluated member values), structs (fields + inlined methods),
 *  free functions (IMGUI_API), typedefs.
 * Token-based, tolerant of the specific C++ idioms imgui headers use.
 */
import { preprocess } from './preprocess.mjs';

// ---------- tokenizer ----------
export function tokenize(src) {
  const tokens = [];
  const n = src.length;
  let i = 0;
  while (i < n) {
    const c = src[i];
    if (c === ' ' || c === '\t' || c === '\r') { i++; continue; }
    if (c === '\n') { tokens.push({ t: 'NEWLINE' }); i++; continue; }
    if (/[A-Za-z_]/.test(c)) {
      let j = i; while (j < n && /[A-Za-z0-9_]/.test(src[j])) j++;
      tokens.push({ t: 'ID', v: src.slice(i, j) }); i = j; continue;
    }
    if (/[0-9]/.test(c) || (c === '.' && /[0-9]/.test(src[i+1] ?? ''))) {
      let j = i;
      if (src.startsWith('0x', i) || src.startsWith('0X', i)) {
        j = i + 2; while (j < n && /[0-9a-fA-F]/.test(src[j])) j++;
        tokens.push({ t: 'NUM', v: src.slice(i, j) }); i = j; continue;
      }
      while (j < n && /[0-9a-fA-FxX.]/.test(src[j]) && !/[A-Za-z_]/.test(src[j] ?? '')) j++;
      // float suffixes like 1.0f, 1e-3f, 0.5f
      if (src[j] === 'f' || src[j] === 'F' || src[j] === 'u' || src[j] === 'U' || src[j] === 'l' || src[j] === 'L') {
        const k = j;
        while (j < n && /[fFuUlL]/.test(src[j])) j++;
        if (j - k > 1) { j = k + 1; } // only one letter suffix
      }
      tokens.push({ t: 'NUM', v: src.slice(i, j) }); i = j; continue;
    }
    // multi-char ops
    const two = src.slice(i, i + 2);
    if (['->', '::', '<<', '>>', '==', '!=', '<=', '>=', '&&', '||'].includes(two)) { tokens.push({ t: 'OP', v: two }); i += 2; continue; }
    tokens.push({ t: 'PUNCT', v: c }); i++;
  }
  return tokens;
}

// ---------- constant expression evaluator for enum values ----------
export function evalExpr(tokens, idx, resolveId) {
  let pos = idx;
  const peek = (off = 0) => tokens[pos + off];
  const fail = () => { throw new Error('enum expr parse error at token ' + pos + ': ' + JSON.stringify(tokens.slice(pos, pos + 5).map(t => t.v))); };
  function parseExpr() { return parseOr(); }
  function parseOr() { let v = parseXor(); while (peek()?.v === '|') { pos++; v = v | parseXor(); } return v; }
  function parseXor() { let v = parseAnd(); while (peek()?.v === '^') { pos++; v = v ^ parseAnd(); } return v; }
  function parseAnd() { let v = parseShift(); while (peek()?.v === '&') { pos++; v = v & parseShift(); } return v; }
  function parseShift() { let v = parseAdd(); while (peek()?.v === '<<' || peek()?.v === '>>') { const op = peek().v; pos++; const r = parseAdd(); v = op === '<<' ? v << r : v >> r; } return v; }
  function parseAdd() { let v = parseMul(); while (peek()?.v === '+' || peek()?.v === '-') { const op = peek().v; pos++; const r = parseMul(); v = op === '+' ? v + r : v - r; } return v; }
  function parseMul() { let v = parseUnary(); while (peek()?.v === '*' || peek()?.v === '/' || peek()?.v === '%') { const op = peek().v; pos++; const r = parseUnary(); v = op === '*' ? v * r : op === '/' ? Math.trunc(v / r) : v % r; } return v; }
  function parseUnary() {
    const tk = peek();
    if (tk?.v === '-') { pos++; return -parseUnary(); }
    if (tk?.v === '~') { pos++; return ~parseUnary(); }
    if (tk?.v === '!') { pos++; return parseUnary() === 0 ? 1 : 0; }
    if (tk?.v === '+') { pos++; return parseUnary(); }
    return parsePrimary();
  }
  function parsePrimary() {
    const tk = peek();
    if (!tk) fail();
    if (tk.t === 'NUM') { pos++; return numValue(tk.v); }
    if (tk.v === '(') { pos++; const v = parseExpr(); if (peek()?.v !== ')') fail(); pos++; return v; }
    if (tk.t === 'ID') {
      if (tk.v === 'true') { pos++; return 1; }
      if (tk.v === 'false') { pos++; return 0; }
      if (tokens[pos+1]?.v === '(') fail();
      const id = tk.v; pos++;
      const r = resolveId(id);
      if (r !== undefined) return r;
      fail();
    }
    fail();
  }
  const value = parseExpr();
  return { value, next: pos };
}

export function numValue(raw) {
  raw = raw.replace(/[uUlLfF]+$/, '');
  if (/^0[xX]/.test(raw)) return parseInt(raw, 16);
  return parseFloat(raw);
}

// ---------- main parse ----------
export function parseHeader(src) {
  const pre = preprocess(src);
  const tokens = tokenize(pre);
  const model = { enums: [], structs: [], functions: [], typedefs: {}, defines: {} };

  // First pass: collect typedefs + simple #define NAME value
  for (const line of pre.split('\n')) {
    const t = line.trim();
    if (t.startsWith('#define ')) {
      const rest = t.slice(8).trim();
      const m = rest.match(/^(\w+)\s+(.+)$/);
      if (m) model.defines[m[1]] = m[2].trim();
    }
    const tm = t.match(/^typedef\s+(.+?)\s+(\w+)\s*;$/);
    if (tm) model.typedefs[tm[2]] = tm[1].trim();
  }

  let i = 0;
  const n = tokens.length;
  function skipLine() { while (i < n && tokens[i].t !== 'NEWLINE') i++; if (i < n) i++; }
  function skipBlock() { let d = 0; for (; i < n; i++) { const v = tokens[i].v; if (v === '{') d++; else if (v === '}') { d--; if (d === 0) { i++; return; } } } }

  while (i < n) {
    const tk = tokens[i];
    if (tk.t === 'NEWLINE') { i++; continue; }
    if (tk.t === 'ID') {
      const id = tk.v;
      if (id === 'enum') {
        i++;
        let name = null, underlying = null;
        // consume 'class'/'struct' keyword and possible name
        while (tokens[i] && (tokens[i].v === 'class' || tokens[i].v === 'struct') && tokens[i].t === 'ID') { i++; }
        if (tokens[i]?.t === 'ID') { name = tokens[i].v; i++; }
        if (tokens[i]?.v === ':') {
          i++;
          while (i < n && tokens[i].v !== '{' && tokens[i].v !== ';') { if (tokens[i].t === 'ID') underlying = tokens[i].v; i++; }
        } else {
          while (i < n && tokens[i]?.v !== '{' && tokens[i]?.v !== ';') i++;
        }
        if (tokens[i]?.v === ';') { i++; continue; }
        if (tokens[i]?.v !== '{') { skipLine(); continue; }
        i++;
        const members = [];
        let autoVal = 0;
        let depth = 1;
        while (i < n && depth > 0) {
          if (tokens[i]?.t === 'NEWLINE' || tokens[i]?.v === ',') { i++; continue; }
          if (tokens[i]?.v === '}') { depth--; i++; break; }
          if (tokens[i]?.t === 'ID') {
            const memName = tokens[i].v; i++;
            let val = null;
            if (tokens[i]?.v === '=') {
              i++;
              try {
                const r = evalExpr(tokens, i, (idn) => {
                  const found = members.find(mn => mn.name === idn);
                  if (found && typeof found.value === 'number') return found.value;
                  return undefined;
                });
                val = r.value; i = r.next;
              } catch { val = null; }
            }
            // for the failed-value case, we still consumed the members partially; reset autoVal only on success
            while (i < n && tokens[i]?.v !== ',' && tokens[i]?.v !== '}' && tokens[i]?.t !== 'NEWLINE') i++;
            if (typeof val === 'number') { members.push({ name: memName, value: val }); autoVal = val + 1; }
            else {
              let nv = autoVal; members.push({ name: memName, value: nv }); autoVal = nv + 1;
            }
            if (tokens[i]?.v === ',') i++;
          }
        }
        if (name && members.length) {
          model.enums.push({ name, underlying, members });
        }
        continue;
      }
      if (id === 'struct' || id === 'union' || id === 'class') {
        const kind = id;
        i++;
        let name = null;
        while (i < n && (tokens[i]?.t === 'NEWLINE' || tokens[i]?.v === 'IMGUI_API' || tokens[i]?.v === 'IMGUI_IMPL_API')) i++;
        if (tokens[i]?.t === 'ID' && !['{', ';'].includes(tokens[i]?.v)) { name = tokens[i].v; i++; }
        while (i < n && tokens[i]?.v !== '{' && tokens[i]?.v !== ';') i++;
        if (tokens[i]?.v === ';') { i++; continue; }
        if (tokens[i]?.v !== '{') { skipLine(); continue; }
        i++;
        const hoist = [];
        const entries = collectStructBody(tokens, i, hoist);
        i = entries.next;
        if (name) model.structs.push({ name, kind, entries: entries.entries });
        for (const h of hoist) {
          if (!model.structs.find(x => x.name === h.name)) model.structs.push({ name: h.name, kind: h.kind, entries: h.entries, nested: true });
        }
        continue;
      }
      if (id === 'IMGUI_API' || id === 'IMGUI_IMPL_API') {
        const isImpl = id === 'IMGUI_IMPL_API';
        i++;
        const decl = [];
        let depth = 0;
        while (i < n) {
          const c = tokens[i];
          if (c.v === ';' && depth === 0) { i++; break; }
          if (c.v === '{' && depth === 0) { // definition with body: skip
            skipBlock(); decl.length = 0; break;
          }
          if (c.v === '{') depth++;
          if (c.v === '}') depth--;
          decl.push(c); i++;
        }
        if (decl.length) {
          const fn = parseFunctionDecl(decl);
          if (fn) { fn.impl = isImpl; model.functions.push(fn); }
        }
        continue;
      }
      skipLine();
      continue;
    }
    skipLine();
  }
  return model;
}

function collectStructBody(tokens, start, hoist) {
  const entries = [];
  let i = start;
  const n = tokens.length;
  function gatherUntilSemi() {
    const seg = [];
    let d = 0;
    while (i < n) {
      const c = tokens[i];
      // NEWLINE tokens: skip for the segment but keep advancing
      if (c.t === 'NEWLINE') { i++; continue; }
      if (c.v === '{') d++;
      if (c.v === '}') {
        if (d === 0) return { seg, closed: true };
        d--;
      }
      if (c.v === ';' && d === 0) { i++; return { seg, closed: false }; }
      // inline method body: ')' followed by '{' (skipping const/newline) -> treat as method end
      if (c.v === ')' && seg.length) {
        let k = i + 1;
        while (k < n && (tokens[k]?.t === 'NEWLINE' || tokens[k]?.v === 'const' || tokens[k]?.v === 'noexcept' || tokens[k]?.v === 'override')) k++;
        if (tokens[k]?.v === '{') {
          seg.push(c); i = k;
          let bd = 0;
          while (i < n) {
            const cc = tokens[i];
            if (cc.v === '{') bd++;
            if (cc.v === '}') { bd--; if (bd === 0) { i++; break; } }
            if (cc.t !== 'NEWLINE') seg.push(cc);
            i++;
          }
          return { seg, closed: false, inlineBody: true };
        }
      }
      seg.push(c); i++;
    }
    return { seg, closed: true };
  }
  while (i < n) {
    const c = tokens[i];
    if (c.t === 'NEWLINE') { i++; continue; }
    if (c.v === '}') { i++; return { entries, next: i }; }
    // nested struct/class:  [struct Name { ... }]  (or anonymous)
    if (tokens[i]?.v === 'struct' || tokens[i]?.v === 'class' || tokens[i]?.v === 'union') {
      const nk = tokens[i].v;
      let j = i + 1;
      let nm = null;
      while (tokens[j]?.t === 'NEWLINE' || tokens[j]?.v === 'IMGUI_API' || tokens[j]?.v === 'IMGUI_IMPL_API') j++;
      if (tokens[j]?.t === 'ID' && tokens[j]?.v !== '{') { nm = tokens[j].v; j++; }
      while (tokens[j]?.t === 'NEWLINE') j++;
      if (tokens[j]?.v === '{') {
        const sub = collectStructBody(tokens, j + 1, hoist);
        if (nm && hoist) hoist.push({ name: nm, kind: nk, entries: sub.entries });
        entries.push({ kind: 'nested', name: nm, entries: sub.entries });
        i = sub.next;
        continue;
      }
    }
    const segRes = gatherUntilSemi();
    const seg = segRes.seg;
    if (segRes.closed) return { entries, next: i };
    const segIds = seg.filter(t => t.t === 'ID').map(t => t.v);
    if (!segIds.length) continue;
    const firstId = seg.find(t => t.t === 'ID')?.v;
    if (firstId === 'typedef') { entries.push({ kind: 'typedef', segments: seg }); continue; }
    if (firstId === 'using') { entries.push({ kind: 'using', segments: seg }); continue; }
    const hasBody = seg.some(t => t.v === '{');
    const inlineBody = segRes.inlineBody === true;
    // function-pointer member:  Type (*Name)(...)  -> field, not method
    const fnptrField = seg.some((t, idx) => t.v === '(' && seg[idx+1]?.v === '*' && seg[idx+2]);
    const isMethod = (inlineBody || !fnptrField) && seg.some((t, idx) => t.t === 'ID' && seg[idx+1]?.v === '(' && t.v !== 'operator');
    if (isMethod) entries.push({ kind: 'method', segments: seg, hasBody });
    else entries.push({ kind: 'field', segments: seg });
  }
  return { entries, next: i };
}

export function parseFunctionDecl(decl) {
  const typeWords = ['const','unsigned','signed','short','long','struct','class','enum','bool','int','char','float','double','void','static','volatile','inline','constexpr','size_t','uint8_t','uint16_t','uint32_t','uint64_t','int8_t','int16_t','int32_t','int64_t','ImWchar','ImWchar16','ImWchar32','ImU8','ImU16','ImU32','ImU64','ImS8','ImS16','ImS32','ImS64','uintptr_t','intptr_t','signed char','unsigned char'];
  // drop NEWLINE tokens & leading qualifiers
  decl = decl.filter(t => t.t !== 'NEWLINE');
  while (decl.length && ['IMGUI_API','IMGUI_IMPL_API','inline','static','constexpr','explicit','friend','virtual'].includes(decl[0].v)) decl.shift();
  // find name: identifier followed by '('
  let nameIdx = -1;
  for (let j = 0; j + 1 < decl.length; j++) {
    if (decl[j].t === 'ID' && decl[j+1].v === '(' && decl[j].v !== 'operator') { nameIdx = j; break; }
  }
  if (nameIdx < 0) return null;
  const retTokens = decl.slice(0, nameIdx);
  // function pointers in return (e.g. typedef) unlikely; keep simple
  const ret = retTokens.filter(t => t.t === 'ID' || t.t === 'PUNCT' || t.t === 'OP').map(t => t.v).join(' ').trim().replace(/\s+/g, ' ');
  // params
  let depth = 0, pEnd = -1;
  for (let j = nameIdx + 1; j < decl.length; j++) {
    if (decl[j].v === '(') depth++;
    else if (decl[j].v === ')') { depth--; if (depth === 0) { pEnd = j; break; } }
  }
  if (pEnd < 0) return null;
  const pTokens = decl.slice(nameIdx + 2, pEnd);
  const params = [];
  let cur = [], d = 0;
  for (const tk of pTokens) {
    if (tk.v === '(' || tk.v === '[') d++;
    if (tk.v === ')' || tk.v === ']') d--;
    if (tk.v === ',' && d === 0) { if (cur.length) params.push(cur); cur = []; continue; }
    cur.push(tk);
  }
  if (cur.length) params.push(cur);

  const parsedParams = params.map(p => {
    const eqIdx = p.findIndex(t => t.v === '=');
    const namePart = eqIdx >= 0 ? p.slice(0, eqIdx) : p;
    const defPart = eqIdx >= 0 ? p.slice(eqIdx + 1) : [];
    let pname = null;
    let nameTokenIdx = -1;
    if (namePart.length >= 1) {
      for (let k = namePart.length - 1; k >= 0; k--) {
        const tk = namePart[k];
        if (tk.t === 'ID' && !typeWords.includes(tk.v)) { pname = tk.v; nameTokenIdx = k; break; }
      }
    }
    // array params ('T name[N]' / 'T name[]') decay to pointer
    const isArray = namePart.some(t => t.v === '[');
    // type part = everything before the name token
    let typePart = nameTokenIdx >= 0 ? namePart.slice(0, nameTokenIdx) : namePart;
    // drop trailing 'const' (e.g. 'const char* const' -> 'const char*')
    while (typePart.length && typePart[typePart.length-1]?.v === 'const') typePart.pop();
    let typeStr = typePart.filter(t => t.t === 'ID' || t.v === '*' || t.v === '&' || t.v === '<' || t.v === '>' || t.v === '::').map(t => t.v).join(' ').trim().replace(/\s+/g, ' ');
    if (isArray) typeStr = (typeStr || 'void') + ' *';
    // normalize 'char * *' -> 'char **'
    typeStr = typeStr.replace(/\s*\*\s*\*/g, '**').replace(/(\*)\*/g, (m, a) => a + '*');
    return {
      type: typeStr,
      name: pname,
      hasDefault: eqIdx >= 0,
      defaultExpr: defPart.map(t => t.t === 'NEWLINE' ? '' : (t.v ?? '')).join('').trim(),
    };
  });
  const fname = decl[nameIdx].v;
  if (fname.startsWith('~')) return null; // destructor
  if (ret === 'operator') return null; // conversion operator
  return { ret, name: fname, params: parsedParams };
}

export default { tokenize, parseHeader, parseFunctionDecl, evalExpr, numValue };