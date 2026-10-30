/**
 * Emit C# bindings mirroring ImGui.NET's exposure, from the parsed model.
 * Produces ImGuiNative.gen.cs + ImGui.gen.cs (friendly layer).
 */

// ---------- type translation for the NATIVE layer (DllImport) ----------
// Mirrors ImGui.NET TypeInfo.WellKnownTypes + pointer handling.
const TYPE_MAP = {
  'bool': 'bool', 'byte': 'byte', 'sbyte': 'sbyte', 'short': 'short', 'ushort': 'ushort',
  'int': 'int', 'uint': 'uint', 'long': 'long', 'ulong': 'ulong', 'float': 'float', 'double': 'double',
  'size_t': 'uint', 'intptr_t': 'IntPtr', 'uintptr_t': 'UIntPtr',
  'ImU8': 'byte', 'ImS8': 'sbyte', 'ImU16': 'ushort', 'ImS16': 'short',
  'ImU32': 'uint', 'ImS32': 'int', 'ImU64': 'ulong', 'ImS64': 'long',
  'ImWchar16': 'ushort', 'ImWchar32': 'uint', 'ImWchar': 'ushort', 'ImDrawIdx': 'ushort',
  'ImGuiID': 'uint', 'ImGuiSelectionUserData': 'long',
  'ImTextureID': 'IntPtr',
  'ImGuiKeyChord': 'ImGuiKey',
  'ImVec2': 'Vector2', 'ImVec4': 'Vector4', 'ImVec3': 'Vector3', 'ImColor': 'ImColor',
};

// include imgui_internal structs that ImGui.NET exposes
const INTERNAL_STRUCTS = new Set([
  'ImDrawChannel', 'ImDrawCmdHeader', 'ImDrawListSharedData', 'ImDrawListSplitter',
  'ImGuiDataVarInfo', 'ImGuiKeyData', 'ImGuiListClipperRange', 'ImGuiListClipperData',
  'ImGuiNavItemData', 'ImGuiPayload', 'ImGuiPlatformImeData', 'ImGuiPlatformMonitor',
  'ImGuiViewport', 'ImGuiWindowClass', 'ImGuiTextRange', 'ImVec1', 'ImVec2ih', 'ImVec2i',
  'ImGuiStoragePair', 'ImColor', 'ImFontAtlasCustomRect', 'ImTextureRect', 'ImTextureData',
  'ImGuiSelectionRequest', 'ImGuiSelectionBasicStorage', 'ImGuiSelectionExternalStorage',
  'ImGuiInputEventMousePos', 'ImGuiInputEventMouseWheel', 'ImGuiInputEventMouseButton',
  'ImGuiInputEventKey', 'ImGuiInputEventText', 'ImGuiInputEventAppFocused', 'ImGuiInputEvent',
  'ImGuiKeyOwnerData', 'ImGuiKeyRoutingData', 'ImGuiKeyRoutingTable',
]);

export function nativeType(cpp) {
  let t = cpp.replace(/\s+/g, ' ').trim();
  t = t.replace(/^const /, '').replace(/\s+&$/, '').replace(/\bconst\b/g, '').replace(/\s+/g, ' ').trim();
  if (t.includes('(')) {
    // function pointer param (ImGuiInputTextCallback etc.) — expose as IntPtr in native layer
    const fname = t.match(/\(\*\s*\w+\s*\)/)?.[0]?.replace(/[(*)\s]/g, '');
    return 'IntPtr';
  }
  const isPtrArr = t.endsWith('**');
  const isPtr = t.endsWith('*');
  let base = t.replace(/\*+$/, '').trim();
  if (base === 'char') base = 'byte'; // const char* -> byte* in native interop
  const mapped = TYPE_MAP[base] || base;
  const star = isPtrArr ? '**' : (isPtr ? '*' : '');
  return mapped + star;
}

// friendly-layer C# type for a parameter (bool stays bool, strings become string in friendly layer)
export function friendlyType(cpp, name) {
  let t = cpp.replace(/\s+/g, ' ').trim();
  const isPtrArr = t.endsWith('**');
  const isPtr = t.endsWith('*');
  let base = t.replace(/\*+$/, '').replace(/^const /, '').replace(/\s+&$/, '').replace(/\bconst\b/g, '').trim();
  if (t.includes('(')) return 'IntPtr';
  if (base === 'const char' || base === 'char') return isPtrArr ? 'string[]' : (isPtr ? 'string' : 'string');
  if (base === 'bool') return 'bool';
  const mapped = TYPE_MAP[base] || base;
  if (isPtr && ['float','int','uint','byte','ushort','short','long','ulong','double','bool','size_t','ImU32','ImS32','ImU64','ImS64','ImU16','ImWchar','ImDrawIdx','ImGuiID','ImWchar16','ImS8','ImU8'].includes(base)) {
    return 'ref ' + mapped;
  }
  return mapped + (isPtrArr ? '**' : isPtr ? '*' : '');
}

// is UDT struct return
export const UDT_TYPES = new Set(['ImVec2', 'ImVec4', 'ImColor', 'ImVec3']);
export function isUdtRet(cpp) {
  const t = cpp.replace(/\s+/g, '').replace(/\bconst\b/g, '').replace(/&$/, '');
  return UDT_TYPES.has(t);
}

export default { nativeType, friendlyType, isUdtRet };