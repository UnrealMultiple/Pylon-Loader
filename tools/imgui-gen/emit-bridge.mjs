/**
 * Emit the C++ cimgui-style bridge (PylonImguiBridge.gen.cpp) for every
 * exported function of the embedded imgui headers. This file, plus the
 * Pylon-specific host API (PylonImguiApi.cpp), forms the complete native
 * surface — no further C++ extension is needed for any ImGui API.
 */
import { writeFileSync } from 'fs';

const UDT_RETS = new Set(['ImVec2', 'ImVec4', 'ImColor']);

function overloadSuffix(m) {
  const ps = (m.params || []).filter(p => p.type.trim());
  if (!ps.length) return 'Nil';
  let t = ps[0].type.replace(/\s+/g, '').replace(/^const/, '');
  if (t === 'char*') return 'Str';
  if (t === 'ImFont*') return 'FontPtr';
  if (t === 'ImVec2') return 'Vec2';
  if (t === 'ImU32') return 'U32';
  return t.replace(/[^A-Za-z0-9]/g, '') + 'Arg';
}
const STRUCT_CTORS = new Set(['ImVec2','ImVec4','ImColor','ImDrawCmd','ImDrawData','ImDrawList','ImDrawListSplitter','ImFont','ImFontAtlas','ImFontAtlasCustomRect','ImFontConfig','ImFontGlyphRangesBuilder','ImGuiInputTextCallbackData','ImGuiIO','ImGuiListClipper','ImGuiOnceUponAFrame','ImGuiPayload','ImGuiPlatformImeData','ImGuiPlatformIO','ImGuiPlatformMonitor','ImGuiSelectionBasicStorage','ImGuiSelectionExternalStorage','ImGuiStoragePair','ImGuiStyle','ImGuiTableColumnSortSpecs','ImGuiTableSortSpecs','ImGuiTextBuffer','ImGuiTextFilter','ImGuiTextRange','ImGuiViewport','ImGuiWindowClass','ImRect','ImVec1']);

function norm(t) {
  return t.replace(/\s+/g, ' ').trim()
    .replace(/\s*&$/, ' &')
    .replace(/\s+/g, ' ');
}
function bridgeType(cpp) {
  const t = norm(cpp);
  if (t.includes('(')) return t;
  // References are flattened to values in the C ABI; 'const' kept for pointer params
  // so calls into ImGui are const-correct (e.g. const char** remaining).
  return t.replace(/\s*&$/, ''); // drop trailing ref, keep everything else
}
function isBool(cpp) { return norm(cpp) === 'bool'; }
function isCstr(cpp) { return norm(cpp) === 'char*' || norm(cpp) === 'const char *'; }

// cimgui param name sanitization
function paramName(name, idx) {
  if (!name) return 'arg' + idx;
  return name;
}

export function emitBridgeCpp(model, outPath) {
  const L = [];
  const W = (s = '') => L.push(s);
  W('// GENERATED FILE — do not edit. See tools/imgui-gen/');
  W('#include "imgui.h"');
  W('#include "imgui_internal.h"');
  W('#include "Graphics/VulkanTexture.h"');
  W('#include <cstdint>');
  W('#include <cstdarg>');
  W('#include <cstring>');
  W('');
  W('namespace pylon::imgui {');
  W('#define PYLON_IMGUI_EXPORT extern "C" __attribute__((visibility("default")))');
  W('');

  // ---------- free functions ----------
  W('// ===================== free functions =====================');
  const seen = new Set();
  const TEXT_FUNCS = new Set(['igText','igTextColored','igTextDisabled','igTextWrapped','igLabelText','igBulletText','igSetTooltip','igDebugLog']);
  for (const fn of model.functions) {
    if (fn.impl) continue;
    const ret = norm(fn.ret);
    const reffed = ret.endsWith(' &');
    const retBase = reffed ? ret.slice(0, -2) : ret;
    const isUdtRet = UDT_RETS.has(retBase);
    const exportName = fn.exportName || ('ig' + fn.name);
    if (TEXT_FUNCS.has(exportName)) continue; // variadic text handled below
    if (seen.has(exportName)) continue;
    seen.add(exportName);
    const params = (fn.params || []).filter(p => p.type.trim() && p.type.trim() !== '...').map((p, i) => ({ ...p, name: paramName(p.name, i), type: bridgeType(p.type) , orig: p.type}));
    const declParams = isUdtRet
      ? [{ name: 'pOut', type: ret.replace(/\s+/g,'') + '*' }, ...params]
      : params;
    const decl = declParams.map(p => p.type + ' ' + p.name).join(', ');
    const callArgs = params.map(p => p.name).join(', ');
    const call = 'ImGui::' + fn.name + '(' + callArgs + ')';
    if (isUdtRet) {
      W('PYLON_IMGUI_EXPORT void ' + exportName + '(' + decl + ') { *pOut = ' + call + '; }');
    } else if (ret === 'void') {
      W('PYLON_IMGUI_EXPORT void ' + exportName + '(' + decl + ') { ' + call + '; }');
    } else if (reffed) {
      W('PYLON_IMGUI_EXPORT ' + retBase + '* ' + exportName + '(' + decl + ') { return &' + call + '; }');
    } else {
      W('PYLON_IMGUI_EXPORT ' + retBase + ' ' + exportName + '(' + decl + ') { return ' + call + '; }');
    }
  }

  W('');
  W('// ===================== member functions =====================');
  const seenM = new Set();
  for (const s of Object.values(model.structs)) {
    if (!s.methods || !s.methods.length) continue;
    // only expose methods for structs that have a Ptr wrapper (public surface)
    for (const m of s.methods) {
      if (s.nested) continue; // nested structs (ImGuiTextRange etc.) aren't top-level C++ types
      if ((m.params || []).some(pp => !pp.name || pp.type === '...' || pp.type.includes('...'))) continue; // variadic/unnamed
      if (m.name === s.name) continue;              // constructor (handled by ctor block)
      if (m.name === 'operator' || norm(m.ret).includes('operator')) continue;
      const ret = norm(m.ret);
      if (ret.includes('~')) continue;              // destructor
      if (!ret || ret === 'IMGUI_API') continue;    // malformed
      const isUdtRet = UDT_RETS.has(ret);
      let exportName = s.name + '_' + m.name;
      const suffix = overloadSuffix(m);
      if (m._overloaded && m._ordinal > 0) exportName = s.name + '_' + m.name + '_' + suffix;
      m._exportName = exportName;
      if (seenM.has(exportName)) continue;
      seenM.add(exportName);
      const params = (m.params || []).map((p, i) => ({ name: paramName(p.name, i), type: bridgeType(p.type) }));
      const selfDecl = { name: 'self', type: s.name + '*' };
      const declParams = isUdtRet ? [{ name: 'pOut', type: ret + '*' }, selfDecl, ...params] : [selfDecl, ...params];
      const decl = declParams.map(p => p.type + ' ' + p.name).join(', ');
      const callArgs = params.map(p => p.name).join(', ');
      const call = 'self->' + m.name + '(' + callArgs + ')';
      if (isUdtRet) W('PYLON_IMGUI_EXPORT void ' + exportName + '(' + decl + ') { *pOut = ' + call + '; }');
      else if (ret === 'void') W('PYLON_IMGUI_EXPORT void ' + exportName + '(' + decl + ') { ' + call + '; }');
      else W('PYLON_IMGUI_EXPORT ' + ret + ' ' + exportName + '(' + decl + ') { return ' + call + '; }');
    }
  }

  W('');
  W('// ===================== constructors / destructors =====================');
  const CTOR_ARGS = {
    ImVec2: { Nil: '', Float: 'float _x, float _y' },
    ImVec4: { Nil: '', Float: 'float _x, float _y, float _z, float _w' },
    ImColor: { Nil: '', Float: 'float r, float g, float b, float a', Int: 'int r, int g, int b, int a', U32: 'ImU32 rgba', Vec4: 'ImVec4 col' },
    ImDrawCmd: { Nil: '' },
    ImDrawData: { Nil: '' },
    ImDrawListSplitter: { Nil: '' },
    ImFont: { Nil: '' },
    ImFontAtlas: { Nil: '' },
    ImFontAtlasCustomRect: { Nil: '' },
    ImFontConfig: { Nil: '' },
    ImFontGlyphRangesBuilder: { Nil: '' },
    ImGuiInputTextCallbackData: { Nil: '' },
    ImGuiIO: { Nil: '' },
    ImGuiListClipper: { Nil: '' },
    ImGuiOnceUponAFrame: { Nil: '' },
    ImGuiPayload: { Nil: '' },
    ImGuiPlatformImeData: { Nil: '' },
    ImGuiPlatformIO: { Nil: '' },

    ImGuiSelectionBasicStorage: { Nil: '' },
    ImGuiSelectionExternalStorage: { Nil: '' },
    ImGuiStoragePair: { Int: 'ImGuiID _key, int _val', Float: 'ImGuiID _key, float _val', Ptr: 'ImGuiID _key, void* _val' },
    ImGuiStyle: { Nil: '' },
    ImGuiTableColumnSortSpecs: { Nil: '' },
    ImGuiTableSortSpecs: { Nil: '' },
    ImGuiTextBuffer: { Nil: '' },
    ImGuiTextFilter: { Nil: '' },

    ImGuiViewport: { Nil: '' },

  };
  for (const [st, variants] of Object.entries(CTOR_ARGS)) {
    for (const [suffix, args] of Object.entries(variants)) {
      const ctorCall = suffix === 'Nil' ? st + '()' : st + '(' + (args.split(',').map(a => a.trim().split(/\s+/).pop())).join(', ') + ')';
      W('PYLON_IMGUI_EXPORT ' + st + '* ' + st + '_' + st + '_' + suffix + '(' + args + ') { return IM_NEW(' + st + ')(' + ctorCall + '); }');
    }
  }
  // destructors
  for (const st of Object.keys(CTOR_ARGS)) {
    W('PYLON_IMGUI_EXPORT void ' + st + '_destroy(' + st + '* self) { IM_DELETE(self); }');
  }

  W('');

  // ===================== Pylon host-only exports (no imgui equivalent) =====================
  W('// Vulkan texture upload/destroy — Pylon host integration, not part of imgui API.');
  W('PYLON_IMGUI_EXPORT uint64_t igCreateTexture(const uint8_t* rgba, int32_t width, int32_t height)');
  W('{');
  W('    if (!rgba || width <= 0 || height <= 0 || !VulkanTexture::IsReady()) return 0;');
  W('    return static_cast<uint64_t>(VulkanTexture::CreateTexture(rgba, width, height));');
  W('}');
  W('');
  W('PYLON_IMGUI_EXPORT void igDestroyTexture(uint64_t id)');
  W('{');
  W('    VulkanTexture::DestroyTexture(static_cast<ImTextureID>(id));');
  W('}');

  W('// ===================== variadic text functions (Pylon text-safe) =====================');
  const vararg = {
    igText: { call: 'ImGui::TextUnformatted', args: 'fmt' },
    igTextColored: { call: 'ImGui::TextUnformatted', args: 'fmt' },
    igTextDisabled: { call: 'ImGui::TextUnformatted', args: 'fmt' },
    igTextWrapped: { call: 'ImGui::TextWrapped', args: 'fmt' },
    igLabelText: { call: 'ImGui::LabelText', args: 'label, fmt' },
    igBulletText: { call: 'ImGui::BulletText', args: 'fmt' },
    igSetTooltip: { call: 'ImGui::SetTooltip', args: 'fmt' },
    igDebugLog: { call: 'ImGui::DebugLog', args: 'fmt' },
  };
  W('PYLON_IMGUI_EXPORT void igText(const char* fmt) { ImGui::TextUnformatted(fmt); }');
  W('PYLON_IMGUI_EXPORT void igTextColored(ImVec4 col, const char* fmt) { ImGui::TextUnformatted(fmt); }');
  W('PYLON_IMGUI_EXPORT void igTextDisabled(const char* fmt) { ImGui::TextUnformatted(fmt); }');
  W('PYLON_IMGUI_EXPORT void igTextWrapped(const char* fmt) { ImGui::TextWrapped(fmt); }');
  W('PYLON_IMGUI_EXPORT void igLabelText(const char* label, const char* fmt) { ImGui::LabelText(label, fmt); }');
  W('PYLON_IMGUI_EXPORT void igBulletText(const char* fmt) { ImGui::BulletText(fmt); }');
  W('PYLON_IMGUI_EXPORT void igSetTooltip(const char* fmt) { ImGui::SetTooltip(fmt); }');
  W('PYLON_IMGUI_EXPORT void igDebugLog(const char* fmt) { ImGui::DebugLog(fmt); }');
  W('');
  W('}  // namespace pylon::imgui');
  writeFileSync(outPath, L.join('\n'), 'utf8');
  return L.length;
}

export default { emitBridgeCpp };