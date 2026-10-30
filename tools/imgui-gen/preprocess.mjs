/**
 * imgui headers mini-preprocessor.
 * Strips comments and evaluates #if/#ifdef/#ifndef/#else/#elif/#endif with a
 * fixed macro set matching the Android kernel build (imconfig.h + build flags).
 * Output keeps line structure so error messages map back to the header.
 */
export const MACROS = new Map(Object.entries({
  IMGUI_DISABLE: 0, IMGUI_DISABLE_OBSOLETE_FUNCTIONS: 0, IMGUI_DISABLE_DEBUG_TOOLS: 0,
  IMGUI_DISABLE_METRICS_WINDOW: 0, IMGUI_DISABLE_DEMO_WINDOWS: 0,
  IMGUI_USE_WCHAR32: 0, IMGUI_DEFINE_MATH_OPERATORS: 1, IMGUI_ENABLE_FREETYPE: 0,
  IMGUI_ENABLE_STB_TRUETYPE: 0, IMGUI_ENABLE_FREETYPE_PLUTOSVG: 0, IMGUI_ENABLE_FREETYPE_LUNASVG: 0,
  IMGUI_USE_STB_SPRINTF: 0, IMGUI_ENABLE_TEST_ENGINE: 0, IMGUI_HAS_TABLE: 1, IMGUI_HAS_DOCK: 0,
  IMGUI_USE_BGRA_PACKED_COLOR: 0, IMGUI_OVERRIDE_DRAWVERT_STRUCT_LAYOUT: 0,
  IMGUI_DISABLE_FILE_FUNCTIONS: 0, IMGUI_DISABLE_DEFAULT_FILE_FUNCTIONS: 0,
  IMGUI_DISABLE_FORMAT_STRING_FUNCTIONS: 0, IMGUI_DISABLE_MATH_FUNCTIONS: 0,
  IMGUI_DISABLE_DEFAULT_MATH_FUNCTIONS: 0, IMGUI_DISABLE_DEFAULT_FORMAT_FUNCTIONS: 0,
  IMGUI_DISABLE_SSE: 0, IMGUI_ENABLE_SSE: 0, IMGUI_ENABLE_SSE4_2: 0, IMGUI_DEBUG_PARANOID: 0,
  IMGUI_DEBUG_HIGHLIGHT_ALL_ID_CONFLICTS: 0, IMGUI_DEBUG_PRINTF: 0, IMGUI_STB_NAMESPACE: 0,
  IMGUI_INCLUDE_IMGUI_USER_H: 0, IMGUI_USER_CONFIG: 0, IMGUI_USE_LEGACY_CRC32_ADLER: 0,
  IMGUI_ENABLE_OSX_DEFAULT_CLIPBOARD_FUNCTIONS: 0, IMGUI_DISABLE_WIN32_DEFAULT_CLIPBOARD_FUNCTIONS: 0,
  IMGUI_DISABLE_WIN32_DEFAULT_IME_FUNCTIONS: 0, IMGUI_DISABLE_WIN32_DEFAULT_IME_FUNCTIONS: 0,
  IMGUI_DISABLE_WIN32_FUNCTIONS: 0, IMGUI_DISABLE_DEFAULT_SHELL_FUNCTIONS: 0,
  IMGUI_ENABLE_NAV_WINDOWING: 0,
  STB_TEXTEDIT_IS_SPACE: 0, STB_TEXTEDIT_STRING: 0,
  _MSC_VER: 0, __clang__: 1, __GNUC__: 1, __ANDROID__: 1, __APPLE__: 0, _WIN32: 0,
}));

function macroDefined(name) { return MACROS.get(name) === 1; }
function macroValue(name) { return MACROS.get(name); }

export function stripComments(src) {
  let out = ''; let i = 0; const n = src.length;
  while (i < n) {
    const c = src[i];
    if (c === '/' && src[i+1] === '/') { while (i < n && src[i] !== '\n') i++; }
    else if (c === '/' && src[i+1] === '*') {
      i += 2; while (i < n && !(src[i] === '*' && src[i+1] === '/')) i++; i += 2;
    } else { out += c; i++; }
  }
  return out;
}

function evalCond(expr) {
  const ev1 = (e) => {
    e = e.trim();
    let m = e.match(/^defined\s*\(\s*(\w+)\s*\)$/); if (m) return macroDefined(m[1]);
    m = e.match(/^defined\s+(\w+)$/); if (m) return macroDefined(m[1]);
    if (/^\d+$/.test(e)) return parseInt(e, 10) !== 0;
    if (e === '1') return true;
    if (e === '0') return false;
    m = e.match(/^(\w+)$/); if (m) { const v = macroValue(m[1]); return v !== undefined && v !== 0; }
    return false;
  };
  const evNot = (e) => { e = e.trim(); if (e.startsWith('!')) return !ev1(e.slice(1)); return ev1(e); };
  const evAnd = (e) => e.split('&&').every(evNot);
  const evOr = (e) => e.split('||').some(evAnd);
  return evOr(expr);
}

export function preprocess(src) {
  const s = stripComments(src);
  const lines = s.split('\n');
  const stack = [];
  const out = [];
  for (const raw of lines) {
    const line = raw.replace(/\r$/, '');
    const t = line.trim();
    const m = t.match(/^#\s*(ifdef|ifndef|if|elif|else|endif)\b(.*)$/);
    if (!m) {
      if (stack.every(x => x.active)) out.push(line);
      continue;
    }
    const kind = m[1]; const rest = m[2].trim();
    if (kind === 'ifdef' || kind === 'ifndef' || kind === 'if') {
      let cond;
      if (kind === 'ifdef') cond = macroDefined(rest);
      else if (kind === 'ifndef') cond = !macroDefined(rest);
      else cond = evalCond(rest);
      const parentActive = stack.every(x => x.active);
      stack.push({ active: parentActive && cond, taken: cond });
      continue;
    }
    if (kind === 'elif') {
      const top = stack[stack.length - 1];
      const cond = evalCond(rest);
      if (!top.taken && stack.slice(0, -1).every(x => x.active)) { top.active = cond; top.taken = cond; }
      else top.active = false;
      continue;
    }
    if (kind === 'else') {
      const top = stack[stack.length - 1];
      if (!top.taken && stack.slice(0, -1).every(x => x.active)) { top.active = true; top.taken = true; }
      else top.active = false;
      continue;
    }
    if (kind === 'endif') stack.pop();
  }
  return out.join('\n');
}