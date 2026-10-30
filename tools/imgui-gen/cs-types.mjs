/**
 * Shared type normalization for the C# generators.
 */

export const FNPTR_TYPES = new Set([
  'ImGuiInputTextCallback','ImGuiSizeCallback','ImGuiMemAllocFunc','ImGuiMemFreeFunc',
  'ImDrawCallback','ImGuiErrorLogCallback','ImGuiPlatformImeDataPtr', 'ImGuiTextEditCallback',
]);

const SIMPLE = {
    'ImGuiContext':'IntPtr',
  'unsigned int':'uint','unsigned short':'ushort','unsigned char':'byte','unsigned long':'uint','unsigned long long':'ulong',
  'long long':'long','long':'int','short':'short','size_t':'uint','intptr_t':'IntPtr','uintptr_t':'UIntPtr',
  'ImU8':'byte','ImS8':'sbyte','ImU16':'ushort','ImS16':'short','ImU32':'uint','ImS32':'int','ImU64':'ulong','ImS64':'long',
  'ImWchar16':'ushort','ImWchar32':'uint','ImWchar':'ushort','ImDrawIdx':'ushort','ImGuiID':'uint','ImTextureID':'IntPtr',
  'ImGuiKeyChord':'ImGuiKey','ImGuiSelectionUserData':'long','bool':'byte','float':'float','double':'double','int':'int',
  'ImVec2':'Vector2','ImVec4':'Vector4','ImVec3':'Vector3','ImColor':'ImColor',
  'ImFontAtlasRectId':'int',
};

export function normType(t) {
  return (t || '').replace(/\s+/g, ' ').trim()
    .replace(/\b(mutable|static|extern|register|virtual|inline|constexpr|explicit|friend)\b/g, '')
    .replace(/^const /, '')
    .replace(/\s+:/, ':')                // bitfield
    .replace(/^(.+?)\s*:\s*\d+$/, '$1')
    .replace(/:\s*\d+$/, '')
    .replace(/\s*:\s*\d+/, '')
    .replace(/\bconst\b/g, '')
    .replace(/\s+/g, ' ').trim();
}

// normalize a single C++ param/field type to C# (keeps '*' pointers)
export function csType(cpp) {
  let t = normType(cpp);
  if (t.includes('(')) return 'IntPtr';           // fn ptr
  t = t.replace(/&+$/, '');
  const stars = (t.match(/\*+/g) || []).reduce((a, s) => a + s.length, 0);
  const baseSpaced = t.replace(/\*+/g, '').trim();
  const base = baseSpaced.replace(/\s+/g, '');
  if (base === 'char') return 'byte' + '*'.repeat(stars);
  if (base === 'void') return 'void' + '*'.repeat(stars);
  const iv = base.match(/^ImVector<(.+)>$/);
  if (iv && stars > 0) return 'ImVector' + '*'.repeat(stars);
  if (iv) {
    const inner = SIMPLE[iv[1]] || iv[1];
    return 'ImVector<' + inner + '>' + '*'.repeat(stars);
  }
  if (FNPTR_TYPES.has(base) || base === 'ImTextureRef') return 'IntPtr' + '*'.repeat(stars);
  if (base === 'ImGuiContext') return 'IntPtr'; // opaque context type
  const mapped = SIMPLE[baseSpaced] || SIMPLE[base] || base;
  return mapped + '*'.repeat(stars);
}

// pure element type (drop trailing *) for array/vector element mapping
export function csElem(cpp) {
  const t = normType(cpp).replace(/\*+$/, '').replace(/&$/, '').trim();
  if (t === 'char') return 'byte';
  return SIMPLE[t] || t;
}

// struct ptr C# name for a `Foo*` field type (Foo in PTR_WRAP) else null
export const PTR_WRAP = {
ImGuiIO:'ImGuiIOPtr', ImGuiPlatformIO:'ImGuiPlatformIOPtr',
  ImGuiStyle:'ImGuiStylePtr', ImDrawData:'ImDrawDataPtr', ImFont:'ImFontPtr',
  ImFontAtlas:'ImFontAtlasPtr', ImFontAtlasCustomRect:'ImFontAtlasCustomRectPtr',
  ImFontAtlasRect:'ImFontAtlasRectPtr', ImFontConfig:'ImFontConfigPtr', ImFontGlyph:'ImFontGlyphPtr',
  ImFontGlyphRangesBuilder:'ImFontGlyphRangesBuilderPtr', ImDrawList:'ImDrawListPtr',
  ImDrawListSharedData:'ImDrawListSharedDataPtr', ImDrawListSplitter:'ImDrawListSplitterPtr',
  ImDrawChannel:'ImDrawChannelPtr', ImDrawCmdHeader:'ImDrawCmdHeaderPtr',
  ImDrawVert:'ImDrawVertPtr', ImDrawCmd:'ImDrawCmdPtr', ImGuiPayload:'ImGuiPayloadPtr',
  ImGuiViewport:'ImGuiViewportPtr', ImGuiPlatformMonitor:'ImGuiPlatformMonitorPtr',
  ImGuiWindowClass:'ImGuiWindowClassPtr', ImGuiKeyData:'ImGuiKeyDataPtr',
  ImGuiStorage:'ImGuiStoragePtr', ImGuiStoragePair:'ImGuiStoragePairPtr',
  ImGuiListClipper:'ImGuiListClipperPtr', ImGuiTextBuffer:'ImGuiTextBufferPtr',
  ImGuiTextFilter:'ImGuiTextFilterPtr', ImGuiTextRange:'ImGuiTextRangePtr',
  ImGuiOnceUponAFrame:'ImGuiOnceUponAFramePtr', ImGuiInputTextCallbackData:'ImGuiInputTextCallbackDataPtr',
  ImGuiSizeCallbackData:'ImGuiSizeCallbackDataPtr', ImGuiTableSortSpecs:'ImGuiTableSortSpecsPtr',
  ImGuiTableColumnSortSpecs:'ImGuiTableColumnSortSpecsPtr', ImGuiMultiSelectIO:'ImGuiMultiSelectIOPtr',
  ImGuiSelectionRequest:'ImGuiSelectionRequestPtr', ImGuiSelectionBasicStorage:'ImGuiSelectionBasicStoragePtr',
  ImGuiSelectionExternalStorage:'ImGuiSelectionExternalStoragePtr', ImTextureData:'ImTextureDataPtr',
  ImTextureRect:'ImTextureRectPtr', ImFontBaked:'ImFontBakedPtr', ImColor:'ImColorPtr',
  ImVec1:'ImVec1Ptr', ImDrawListData:'ImDrawListDataPtr', ImGuiTextIndex:'ImGuiTextIndexPtr',
};

export { SIMPLE };
