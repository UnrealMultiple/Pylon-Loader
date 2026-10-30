/**
 * Compute cimgui-style export names for free functions, member functions,
 * constructors and destructors; validate against the ImGui.NET oracle
 * (ref_native.txt) where the same C++ signature exists.
 */
import { readFileSync } from 'fs';

export function loadOracle() {
  const map = new Map();   // key: `name|paramTypes` (C#-style) -> exportName
  const raw = readFileSync(new URL('./ref_native.txt', import.meta.url), 'utf8');
  for (const line of raw.split('\n')) {
    if (!line.trim()) continue;
    const tabs = line.split('\t');
    if (tabs.length < 3) continue;
    const ret = tabs[0].trim();
    const name = tabs[1].trim();
    const params = tabs[2].trim().replace(/^\(|\)$/g, '');
    map.set(name + '|' + params, name);
  }
  return map;
}

// short type names cimgui uses for overload disambiguation
function shortType(csType) {
  if (!csType) return 'P';
  switch (csType) {
    case 'byte*': return 'Str';            // const char*
    case 'byte**': return 'Str_arr';       // const char**
    case 'byte': return 'Bool';
    case 'byte*': return 'BoolPtr';        // handled above
    case 'int': return 'Int';
    case 'int*': return 'IntPtr';
    case 'uint': return 'ID';              // ImGuiID/uint -> ID (cimgui convention)
    case 'uint*': return 'UintPtr';
    case 'float': return 'Float';
    case 'float*': return 'FloatPtr';
    case 'Vector2': return 'Vec2';
    case 'Vector4': return 'Vec4';
    case 'void*': return 'Ptr';
    case 'ImU32': return 'U32';
    case 'ImColor': return 'Color';
  }
  // enum types: strip leading 'ImGui' -> TreeNodeFlags, TabBarFlags etc.
  if (/^Im[A-Z]/.test(csType)) return csType.replace(/^Im/, '');
  if (csType.endsWith('*')) return csType.replace(/\*$/, '') + 'Ptr';
  return csType;
}

function oracleKey(exportName, params) {
  const p = params.map(x => x.csType).join(', ');
  return exportName + '|' + p;
}

/**
 * Compute the cimgui export name for a free function.
 * ctx: { name, retCs, params: [{csType, nativeType}], isUdt, overloadGroup }
 */
export function freeName(fn, overloads) {
  const base = 'ig' + fn.name;
  // UDT returns: pOut form, no suffix beyond base name (cimgui nonUDT style used by ImGui.NET)
  if (fn.isUdt) return base;
  const group = overloads.filter(o => o.name === fn.name);
  if (group.length <= 1) return base;
  // find first parameter index where signatures differ
  const maxLen = Math.max(...group.map(o => o.paramsRaw.length));
  let diffIdx = -1;
  for (let i = 0; i < maxLen && diffIdx < 0; i++) {
    const types = new Set(group.map(o => (o.paramsRaw[i]?.type || 'P')));
    if (types.size > 1) diffIdx = i;
  }
  if (diffIdx < 0) return base;
  const t = shortType(fn.paramsRaw[diffIdx]?.csType || 'P');
  return base + '_' + t;
}

export function memberName(structName, fn) {
  return structName + '_' + fn.name;
}

export function ctorName(structName, paramsRaw) {
  if (!paramsRaw.length) return structName + '_' + structName + '_Nil';
  const suff = paramsRaw.map(p => shortType(p.csType || 'P')).join('_');
  return structName + '_' + structName + '_' + suff;
}

export function dtorName(structName) {
  return structName + '_destroy';
}

export default { loadOracle, freeName, memberName, ctorName, dtorName, shortType };
