/**
 * Emit ImGui.gen.cs — the friendly static ImGui class mirroring ImGui.NET
 * emission: string marshalling via Util (stackalloc fast path), defaults,
 * ref/out, pOut UDT returns, Ptr wrapping, ReadOnlySpan overloads.
 */
import { nativeType, friendlyType, isUdtRet } from './emit-cs.mjs';

const UDT_RETS = { 'ImVec2': 'Vector2', 'ImVec4': 'Vector4', 'ImColor': 'ImColor' };
const STRUCT_PTR_WRAP = {
  ImGuiContext: 'ImGuiContextPtr', ImGuiIO: 'ImGuiIOPtr', ImGuiPlatformIO: 'ImGuiPlatformIOPtr',
  ImGuiStyle: 'ImGuiStylePtr', ImDrawData: 'ImDrawDataPtr', ImFont: 'ImFontPtr',
  ImFontAtlas: 'ImFontAtlasPtr', ImFontAtlasCustomRect: 'ImFontAtlasCustomRectPtr',
  ImFontConfig: 'ImFontConfigPtr', ImFontGlyph: 'ImFontGlyphPtr',
  ImFontGlyphRangesBuilder: 'ImFontGlyphRangesBuilderPtr', ImDrawList: 'ImDrawListPtr',
  ImDrawListSharedData: 'ImDrawListSharedDataPtr', ImDrawListSplitter: 'ImDrawListSplitterPtr',
  ImDrawChannel: 'ImDrawChannelPtr', ImDrawCmdHeader: 'ImDrawCmdHeaderPtr',
  ImDrawVert: 'ImDrawVertPtr', ImDrawCmd: 'ImDrawCmdPtr', ImGuiPayload: 'ImGuiPayloadPtr',
  ImGuiViewport: 'ImGuiViewportPtr', ImGuiPlatformMonitor: 'ImGuiPlatformMonitorPtr',
  ImGuiWindowClass: 'ImGuiWindowClassPtr', ImGuiKeyData: 'ImGuiKeyDataPtr',
  ImGuiStorage: 'ImGuiStoragePtr', ImGuiListClipper: 'ImGuiListClipperPtr',
  ImGuiTextBuffer: 'ImGuiTextBufferPtr', ImGuiTextFilter: 'ImGuiTextFilterPtr',
  ImGuiTextRange: 'ImGuiTextRangePtr', ImGuiOnceUponAFrame: 'ImGuiOnceUponAFramePtr',
  ImGuiInputTextCallbackData: 'ImGuiInputTextCallbackDataPtr',
  ImGuiSizeCallbackData: 'ImGuiSizeCallbackDataPtr',
  ImGuiTableSortSpecs: 'ImGuiTableSortSpecsPtr',
  ImGuiTableColumnSortSpecs: 'ImGuiTableColumnSortSpecsPtr',
  ImGuiMultiSelectIO: 'ImGuiMultiSelectIOPtr',
  ImGuiSelectionRequest: 'ImGuiSelectionRequestPtr',
  ImGuiSelectionBasicStorage: 'ImGuiSelectionBasicStoragePtr',
  ImGuiSelectionExternalStorage: 'ImGuiSelectionExternalStoragePtr',
  ImColor: 'ImColorPtr', ImGuiStoragePair: 'ImGuiStoragePairPtr',
  ImVec1: 'ImVec1Ptr', ImGuiContextHook: 'ImGuiContextHookPtr',
};

// default value translation (C++ expr -> C# expression)
export function defaultExpr(def, type) {
  if (def === undefined || def === null || def === '') return null;
  let d = def.trim();
  // string literal default (format strings "%.3f")
  if (/^"[^"]*"$/.test(d)) return { literal: d.slice(1, -1) };
  if (d === 'NULL' || d === 'nullptr' || d === '(void*)0') return 'null';
  if (/^ImVec2\(0\.?0*, ?0\.?0*\)$/.test(d)) return 'new Vector2(0f, 0f)';
  if (/^ImVec2\(0, ?0\)$/.test(d)) return 'new Vector2(0f, 0f)';
  if (/^ImVec2\(1\.?0*, ?1\.?0*\)$/.test(d)) return 'new Vector2(1f, 1f)';
  if (/^ImVec4\(0\.?0*(,\s*0\.?0*){3}\)$/.test(d)) return 'new Vector4(0f, 0f, 0f, 0f)';
  if (d === 'FLT_MAX') return type.includes('float') ? 'float.MaxValue' : null;
  if (d === '-' && 'FLT_MAX') return null;
  if (/^-?\d+\.?\d*[fF]?$/.test(d)) {
    const num = parseFloat(d.replace(/[fF]$/, ''));
    if (Number.isInteger(num) && !d.includes('.') && !/^[01]$/.test(d)) {
      return d.replace(/f$/,'');
    }
    return num + (type.includes('float') ? 'f' : '');
  }
  if (/^0[xX][0-9a-fA-F]+$/.test(d)) return '' + parseInt(d, 16) + (type.includes('uint') ? 'u' : '');
  if (/^ImGui\w+_(None|0)$/.test(d)) return '(0)';
  return null; // fallback: cannot translate -> caller decides
}

// does a C++ type marshal as a C# string?
export function isStringType(cpp) {
  const t = cpp.replace(/\s+/g, ' ').trim();
  return /^(const\s+)?char\s*\*$/.test(t);
}
export function isStringArrayType(cpp) {
  const t = cpp.replace(/\s+/g, ' ').trim();
  return /^(const\s+)?char\s*\*\*$/.test(t);
}

export default { friendlyType, isUdtRet, defaultExpr, isStringType };
