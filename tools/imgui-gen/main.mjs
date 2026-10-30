/**
 * Pipeline: parse embedded headers -> model -> emit C++ bridge + C# bindings.
 */
import { readFileSync, writeFileSync } from 'fs';
import { parseHeader, parseFunctionDecl } from './parse.mjs';
import { normType } from './cs-types.mjs';
import { emitBridgeCpp } from './emit-bridge.mjs';
import { emitNativeGen } from './emit-native.mjs';
import { emitImGuiGen } from './emit-imguigen.mjs';
import { emitStructGen as emitStructs } from './emit-structs.mjs';
import { emitEnums } from './emit-enums.mjs';

const PATHS = {
  imgui: 'C:/Users/admin/Desktop/Pylon-Loader/src/core/src/imgui/imgui.h',
  internal: 'C:/Users/admin/Desktop/Pylon-Loader/src/core/src/imgui/imgui_internal.h',
  imstb: 'C:/Users/admin/Desktop/Pylon-Loader/src/core/src/imgui/imstb_textedit.h',
};

// The structs for which ImGui.NET generates Ptr wrappers (public surface).
// Embedded 1.92.2b mainline lacks docking-only structs — those are simply absent here.
export const WRAPPER_STRUCTS = new Set([
  'ImColor','ImDrawChannel','ImDrawCmd','ImDrawCmdHeader','ImDrawData','ImDrawList',
  'ImDrawListSharedData','ImDrawListSplitter','ImDrawVert','ImFont','ImFontAtlas',
  'ImFontAtlasCustomRect','ImFontConfig','ImFontGlyph','ImFontGlyphRangesBuilder',
  'ImGuiIO','ImGuiInputTextCallbackData','ImGuiKeyData','ImGuiListClipper',
  'ImGuiMultiSelectIO','ImGuiOnceUponAFrame','ImGuiPayload','ImGuiPlatformIO',
  'ImGuiPlatformImeData','ImGuiSelectionBasicStorage','ImGuiSelectionExternalStorage',
  'ImGuiSelectionRequest','ImGuiSizeCallbackData','ImGuiStorage','ImGuiStoragePair',
  'ImGuiStyle','ImGuiTableColumnSortSpecs','ImGuiTableSortSpecs','ImGuiTextBuffer',
  'ImGuiTextFilter','ImGuiTextRange','ImGuiViewport','ImRect','ImTextureData',
  'ImTextureRect','ImFontAtlasRect','ImFontBaked','ImDrawCmdHeader','ImGuiPayload',
]);

export function parseAll() {
  const model = { enums: {}, structs: {}, functions: [], typedefs: {}, defines: {}, src: {} };
  for (const [key, path] of Object.entries(PATHS)) {
    const parsed = parseHeader(readFileSync(path, 'utf8'));
    for (const e of parsed.enums) { if (!model.enums[e.name]) { model.enums[e.name] = e; model.src['enum:' + e.name] = key; } }
    for (const s of parsed.structs) {
      if (WRAPPER_STRUCTS.has(s.name) && !model.structs[s.name] && s.kind === 'struct') { model.structs[s.name] = s; model.src['struct:' + s.name] = key; }
    }
    for (const f of parsed.functions) {
      const dup = model.functions.find(x => x.name === f.name && JSON.stringify(x.params) === JSON.stringify(f.params));
      if (!dup) model.functions.push({ ...f, _src: key });
    }
    Object.assign(model.typedefs, parsed.typedefs);
    Object.assign(model.defines, parsed.defines);
  }
  for (const s of Object.values(model.structs)) {
    if (!WRAPPER_STRUCTS.has(s.name)) { s.methods = []; continue; }
    const methods = [];
    if (!s.nested) {  // nested types (e.g. ImGuiTextRange) are not top-level C++ types — no exported methods
      for (const e of (s.entries || [])) {
        if (e.kind !== 'method') continue;
        const seg = e.segments.filter(t => t.v !== 'IMGUI_API' && t.v !== 'IMGUI_IMPL_API' && t.t !== 'NEWLINE' && t.v !== 'inline');
        const fn = parseFunctionDecl(seg);
        if (fn && !fn.name.startsWith('~') && !fn.name.includes('operator') && !fn.ret.trim().startsWith('~') && !fn.ret.trim().includes('operator') && fn.name !== 'operator') {
          // skip variadic methods (appendf/appendfv etc.) — no C ABI export
          if ((fn.params || []).some(p => p.type.includes('...') || p.type.includes('va_list') || (!p.name && !p.type.trim()))) continue;
          methods.push(fn);
        }
      }
    }
    s.methods = methods;
    // overload disambiguation: mark + assign deterministic ordinals
    {
      const byName = new Map();
      for (const mm of methods) {
        const key = mm.name + '|' + (mm.params || []).map(p => normType(p.type).replace(/\s+/g, '')).join(',');
        if (!byName.has(mm.name)) byName.set(mm.name, []);
        byName.get(mm.name).push(key);
      }
      for (const mm of methods) {
        const variants = byName.get(mm.name) || [];
        if (new Set(variants).size > 1) mm._overloaded = true;
      }
      const ord = new Map();
      for (const mm of methods) {
        const o = ord.get(mm.name) || 0;
        mm._ordinal = o;
        ord.set(mm.name, o + 1);
      }
      const seenSig = new Set();
      for (let mi = methods.length - 1; mi >= 0; mi--) {
        const mm = methods[mi];
        const sig = mm.name + '|' + (mm.params || []).map(p => normType(p.type).replace(/\s+/g, '')).join(',');
        if (seenSig.has(sig)) methods.splice(mi, 1);
        else { seenSig.add(sig); mm._sigKey = sig; }
      }
    }
    s.methods = methods;
  }
  model.functions = model.functions.filter(f => f._src === 'imgui' && !f.impl
    && !(f.params || []).some(pp => typeof pp.type === 'string' && pp.type.includes('(') && pp.type.includes(')')));
  model.functions.forEach(f => { f.exportName = 'ig' + f.name; });
  return model;
}

export function runAll(opts = {}) {
  const model = parseAll();
  const outBridge = opts.bridge || 'C:/Users/admin/Desktop/Pylon-Loader/src/core/src/imgui/PylonImguiBridge.gen.cpp';
  const outCs = opts.cs || 'C:/Users/admin/Desktop/Pylon-Loader/src/csharp/managed/Pylon.ImGui/Generated/';
  const nBridge = emitBridgeCpp(model, outBridge);
  const nNative = emitNativeGen(model, outCs);
  const nImGui = emitImGuiGen(model, outCs);
  const nStructs = emitStructs(model, outCs);
  const nEnums = emitEnums(model, outCs);
  return { nBridge, nNative, nImGui, nStructs, nEnums };
}

export default { parseAll, runAll, WRAPPER_STRUCTS };