/**
 * Model building: normalize parsed headers into a binding model.
 * Maps C++ types -> C# types (cimgui-style naming), classifies functions
 * into free / member / ctor / dtor, computes export names.
 */
import { readFileSync } from 'fs';
import { parseHeader, parseFunctionDecl } from './parse.mjs';

// ---------- C++ -> C# type mapping (mirrors ImGui.NET TypeInfo.WellKnownTypes) ----------
const PRIMITIVE_MAP = {
  'bool': 'bool',
  'int': 'int',
  'unsigned int': 'uint',
  'float': 'float',
  'double': 'double',
  'char': 'sbyte',
  'unsigned char': 'byte',
  'short': 'short',
  'unsigned short': 'ushort',
  'long': 'int',               // android long is 32-bit
  'unsigned long': 'uint',
  'long long': 'long',
  'unsigned long long': 'ulong',
  'size_t': 'uint',            // passthru as uint; bridge casts
  'intptr_t': 'IntPtr',
  'uintptr_t': 'UIntPtr',
  'ImU8': 'byte', 'ImS8': 'sbyte', 'ImU16': 'ushort', 'ImS16': 'short',
  'ImU32': 'uint', 'ImS32': 'int', 'ImU64': 'ulong', 'ImS64': 'long',
  'ImWchar16': 'ushort', 'ImWchar32': 'uint',
  'ImGuiID': 'uint',
  'ImTextureID': 'IntPtr',     // IMGUI_USE_WCHAR32 off; ImU64 on arm (IntPtr), matches ImGui.NET
  'ImDrawIdx': 'ushort',
  'ImWchar': 'ushort',         // IMGUI_USE_WCHAR32 off
  'ImGuiKeyChord': 'ImGuiKey', // mapped like ImGui.NET
  'ImGuiSelectionUserData': 'long',
};

export function cppToNative(cppType) {
  // normalize whitespace
  let t = cppType.replace(/\s+/g, ' ').trim();
  // references: const ImVec2& -> ImVec2 (by value in cimgui exports)
  t = t.replace(/^const /, '').replace(/\s+&$/, '').replace(/^const\s+/, '');
  // strip 'const' anywhere
  t = t.replace(/\bconst\b/g, '').replace(/\s+/g, ' ').trim();
  // function pointers -> keep native name (e.g. ImGuiInputTextCallback)
  if (t.includes('(')) return t.replace(/\(\*\)/, '').trim() || 'IntPtr';
  // pointers
  const isPtr = t.endsWith('*');
  const isPtrPtr = t.endsWith('**');
  const base = t.replace(/\*+$/, '').trim();
  // fundamental types stay as-is in native (float, int, ...); structs/enums by name
  const mapped = PRIMITIVE_MAP[base] || base;
  if (isPtrPtr) return mapped + '*';
  if (isPtr) return mapped + '*';
  return mapped;
}

export function cppExact(cppType) {
  let t = cppType.replace(/\s+/g, ' ').trim();
  t = t.replace(/^const /, '').replace(/\s+&$/, '').replace(/\bconst\b/g, '').replace(/\s+/g, ' ').trim();
  if (t.includes('(')) return t.replace(/\(\*\)/, '').trim() || 'IntPtr';
  const isPtr = t.endsWith('*');
  const base = t.replace(/\*+$/, '').trim();
  return (PRIMITIVE_MAP[base] || base);
}

const USDT_TYPES = new Set(['ImVec2', 'ImVec4', 'ImColor', 'ImRect']);

export function isUdt(cppType) { return new Set(['ImVec2','ImVec4','ImColor']).has(cppType.replace(/\s+/g,'').replace(/^const/,'').replace(/&$/,'')); }

// ---------- build model ----------
export function buildModel(headerSrcs) {
  const model = { enums: {}, structs: {}, functions: [], typedefs: {} };
  for (const src of headerSrcs) {
    const parsed = parseHeader(src);
    for (const e of parsed.enums) if (!model.enums[e.name]) model.enums[e.name] = e;
    for (const s of parsed.structs) if (!model.structs[s.name] && s.kind !== 'union') model.structs[s.name] = s;
    for (const f of parsed.functions) {
      if (!f.name) continue;
      const existing = model.functions.find(x => x.name === f.name && sameParams(x.params, f.params));
      if (!existing) model.functions.push(f);
    }
    Object.assign(model.typedefs, parsed.typedefs);
  }
  return model;
}

function sameParams(a, b) {
  if (a.length !== b.length) return false;
  return a.every((p, i) => p.type === b[i].type);
}

// Enum naming: strip 'ImGuiXxx_' prefix for member names, keep value
export function enumMemberName(enumName, member) {
  const prefixes = [enumName + '_', 'ImGuiKey_'];
  for (const pre of prefixes) {
    if (member.startsWith(pre)) return member.slice(pre.length);
  }
  return member;
}

// ImGuiKey spec: keys have values >= 512 plus ImGuiMod_* (1<<12 etc.)
export const KEY_SPECIAL = {
  'ImGuiKey': 'ImGuiKey',
  'ImGuiKey_NamedKey_BEGIN': 'NamedKey_BEGIN',
};

export default { cppToNative, isUdt, buildModel, enumMemberName, PRIMITIVE_MAP };
