/**
 * Main generator: parse embedded imgui headers, build model, emit:
 *  1) C++ bridge (PylonImguiBridge.gen.cpp) — extern "C" exports for every function
 *  2) C# bindings mirroring ImGui.NET exposure (ImGuiNative/ImGui/structs/enums)
 */
import { readFileSync } from 'fs';
import { buildModel, cppToNative, isUdt, enumMemberName } from './model.mjs';
import { loadOracle, freeName, memberName, ctorName, dtorName } from './names.mjs';

const HEADERS = [
  'C:/Users/admin/Desktop/Pylon-Loader/src/core/src/imgui/imgui.h',
  'C:/Users/admin/Desktop/Pylon-Loader/src/core/src/imgui/imgui_internal.h',
  'C:/Users/admin/Desktop/Pylon-Loader/src/core/src/imgui/imstb_textedit.h',
];

export function loadModel() {
  const oracle = loadOracle();
  const model = buildModel(HEADERS.map(p => readFileSync(p, 'utf8')));
  model.oracle = oracle;
  return model;
}

// Resolve struct methods from parsed struct entries
export function resolveStructMethods(model) {
  for (const s of Object.values(model.structs)) {
    if (!s.entries) continue;
    const methods = [];
    for (const e of s.entries) {
      if (e.kind !== 'method') continue;
      const seg = e.segments.filter(t => t.v !== 'IMGUI_API' && t.v !== 'IMGUI_IMPL_API' && t.t !== 'NEWLINE');
      if (!seg.length) continue;
      const fn = parseFn(seg);
      if (fn && !fn.name.startsWith('~')) methods.push(fn);
    }
    s.methods = methods;
  }
  return model;
}

// helper: parse function decl from token segments — reuse parse.mjs
import { parseFunctionDecl } from './parse.mjs';
const parseFn = parseFunctionDecl;

// Assign cimgui export names by comparing against oracle
export function nameAll(model) {
  for (const fn of model.functions) {
    fn.nameIsOverloaded = model.functions.filter(o => o.name === fn.name).length > 1;
  }
  // free functions: compute export name; prefer oracle when signature matches
  for (const fn of model.functions) {
    const paramsCs = (fn.params || []).map(p => p.type);
    // use cimgui conventions w/ oracle check
    fn.exportName = freeName(fn, model.functions.filter(o => o.name === fn.name));
  }
  for (const s of Object.values(model.structs)) {
    if (!s.methods) continue;
    for (const m of s.methods) {
      m.exportName = memberName(s.name, m);
    }
  }
  return model;
}

export default { loadModel, resolveStructMethods, nameAll };
