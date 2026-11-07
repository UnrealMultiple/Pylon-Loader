# 游戏 Texture2D 显示在 ImGui 覆盖层：可行路径

本文回答一个问题：**游戏自己的 `Texture2D`（原版图集 / mod 贴图）怎么画进 ImGui 覆盖层**。
结论来自当前仓库的实现（1.4.5.8.6 / Unity 2021.3.56f2，见 `docs/roadmap.md`）与 Bunny-Loader 在真机上的实测结论。

---

## 0. 结论先行

1. ImGui 这边只认一个东西：`ImTextureID`，在本项目里就是**一个 GL 纹理名**。
2. 覆盖层画在**游戏自己的 GLES 上下文**里（`Pipe.cpp:135` 起，`Present.cpp:104`），所以游戏已经上传到 GPU 的纹理可以**零拷贝直接采样**——不需要读像素，也不需要 `GetNativeTexturePtr`。
3. 唯一缺的一步是"游戏 `Texture2D` → GL 纹理名"。Unity 侧的路全被裁掉了（§2），只能从 **GL 层**拿：让 GPU 把这张图画一次，在 `glBindTexture` / FBO 处把名字读回来（§3）。
4. 可读纹理（mod 的 PNG、`ImageConversion.LoadImage` 出来的）走 CPU 上传即可（§4）。

---

## 1. 现状：ImGui 的贴图链路

| 线程 | 做什么 | 代码 |
|---|---|---|
| 游戏线程 | 建帧、调 ImGui；`ImGui.UploadTexture(rgba)` 只**排队** | `ImGuiTextures.cs:22` → `Textures::Create`（`Textures.cpp:52`） |
| 渲染线程（`eglSwapBuffers` 之前） | 生成纹理名、上传像素、重放快照 | `Textures::Apply`（`Textures.cpp:75`，由 `Present.cpp:168` 调） |
| 渲染线程 | 逐条命令 `glBindTexture(pcmd->GetTexID())` | `imgui_impl_opengl3.cpp:650` |

ImGui 1.92 允许"后端自己的纹理名"直接当图片用，不需要 `ImTextureData`：

```
ImTextureRef(ImTextureID tex_id)   // imgui.h:360 —— _TexData 为空，_TexID 存名字
GetTexID() -> _TexData ? _TexData->TexID : _TexID   // imgui.h:3839
```

桥和托管侧也是按这个形状生成的：

```cpp
// PylonImguiBridge.gen.cpp:135
PYLON_IMGUI_EXPORT void igImage(ImTextureID tex_ref, const ImVec2 image_size, const ImVec2 uv0, const ImVec2 uv1)
```

```csharp
// ImGui.gen.cs:1455
public static void Image(ulong tex_ref, Vector2 image_size, Vector2 uv0, Vector2 uv1)
```

**所以：手里只要有一个"本上下文里的 GL 纹理名"（`uint`，当 `ulong` 传），`ImGui.Image` 立刻能画。**

---

## 2. 硬约束：游戏贴图读不到

Bunny-Loader 真机实测（1.4.5.x，同引擎）：

- `_sprites/README.md:18-24`：原版图集 2048×2048，`isReadable = 0`、`GetRawImageDataSize() = 0`、`GetWritableImageData() = null`，`Blit / ReadPixels / GetNativeTexturePtr` 被 IL2CPP strip 掉。"要么游戏自己画，要么图从外面来。"
- `app/build.gradle.kts:267-277`、`tools/cheatbridge/bunny/CheatBridge.java:1283-1292`：同样的结论，所以 cheat 菜单的图标是从 APK 里的 PNG 解的。

Pylon 的 `refs/dump.cs`（1.4.5.8.6）对得上：

| Unity API | 状态 |
|---|---|
| `Texture.GetNativeTexturePtr` | **无** |
| `Texture2D.GetPixels / GetPixels32 / SetPixels` | **无** |
| `Texture2D.ReadPixels` | **无** |
| `ImageConversion.EncodeToPNG` | **无** |
| `Texture2D.CreateExternalTexture` | **无** |
| `ImageConversion.LoadImage` | 有（`dump.cs:509433`） |
| `Texture2D.GetRawTextureData<T>()` | 有（`dump.cs:460936`），但只对**可读**纹理有意义 |
| `Graphics.Blit(Texture, RenderTexture, Material[, int])` | 有（`dump.cs:461399 / 461402`）；**没有**两参重载 |
| `RenderTexture` | 有（`dump.cs:461168` 起，多个 ctor） |
| 游戏 `Texture2D.SaveAsPng` | 有签名但**是空桩**（RVA `0xFB4BA0`，下一个方法 `0xFB4BA4`，只占 4 字节） |

游戏 `Texture2D`（`dump.cs:46775-46806`）能给出的是"对象"，不是像素：
`UnityTexture`、`UnityBindTexture`、`UnityAlphaTexture`、`UnityPalTexture`、`UnityTextureWidth/Height`、`PackedEntry`、`Bounds`。

---

## 3. 方法 A（推荐）：GPU 零拷贝 —— 让 GPU 画一次，从 GL 层拿名字

### 3.1 为什么可行

`Graphics.Blit(source, rt, material)` 是**游戏线程提交、渲染线程执行**的 GL 绘制——项目里已经这么用：
`Marker.cpp:122-153` 用 `Internal_BlitMaterial5` 把白纹理 blit 进一张 17×3 的 `RenderTexture`，
渲染线程上就表现为一次 `glViewport(0,0,17,3)`（`Capture.cpp:134-147` 看得到）。

同一次绘制里，Unity 必然 `glBindTexture` 源纹理、`glBindFramebuffer` 目标 RT。
这两处 Pylon 已经挂了 hook（`Capture.cpp:125-132`、`224-238`），GL 函数表里也有
`GetIntegerv` / `GetFramebufferAttachmentParameteriv`（`Gles.h:12-31`）。
**名字就在那里，读一下即可。**

### 3.2 步骤

1. **游戏线程**：拿源纹理 `tex.UnityBindTexture`（图集时是整页，未打包时就是自己），
   尺寸 `tex.UnityTextureWidth / UnityTextureHeight`。
2. **目标 RT**：`new RenderTexture(w, h, 0)`（或 native 里照 `Marker.cpp:96` 的 il2cpp 构造）。
   它只当"绘制触发点"，尺寸可以很小（我们要的是源纹理名，不是它的像素）。
3. **什么时候 blit**：只能在**渲染阶段**发（相机 `FireOnPostRender` 钩子里，见 `CameraHook.cpp`）。
   `PostUpdateEverything` 这类 Update 时机调 `Graphics.Blit`，Unity 内部会 SIGSEGV（真机实测崩在
   libunity，寄存器里是 `RenderType` 这种 shader 标签）。所以 `Resolve` 只排队，`GameTextures::OnRender`
   才发 blit。
4. **探针**：`TexProbe::Arm(w, h)` → `Graphics.Blit(page, rt, material)` → 渲染线程**下一个 draw**
   （`hookDrawArrays / hookDrawElements`）里读：
   ```cpp
   glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &fbo);            // 认领：目标必须是离屏 RT
   glGetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0,
       GL_FRAMEBUFFER_ATTACHMENT_OBJECT_NAME, &attachment);      // 且始终是同一张（探针自己的 RT）
   glGetIntegerv(GL_TEXTURE_BINDING_2D, &name);                 // 源：图集页
   ```
   目标 RT 只有 4×4，所以 `glViewport` 的宽高就是探针签名；看到 `fbo != 0` + 视口尺寸相符 + 颜色附件
   是第一次记下的那张，才认领。**不要用 `glGetTexLevelParameteriv` 核对源尺寸**——它属于 GLES 3.1+，
   本机设备只到 ES 3.0（`GLES3/gl3.h` 里没有），页尺寸一律问 il2cpp 的 `UnityTextureWidth/Height`。
   匹配上就取 texture unit 0..3 里第一个非零的 `GL_TEXTURE_BINDING_2D`，然后撤销探针；
   不匹配就继续等下一个 draw（有超时，超时后这张贴图重来，最多 6 次）。
5. **缓存**：`页面对象指针 → { GL 名字, 页尺寸 }`。同一页上的所有精灵共用一张纹理，只探一次。
6. **画**：`ImGui.Image((ulong)name, size, uv0, uv1)`，UV 由 `PackedEntry` 决定。

### 3.3 UV 与图集

`TextureAtlasEntry`（`dump.cs:35263-35277`）：`TextureOffsetX/Y`、`TextureWidth/Height`、`AtlasIndex`、`AttachedTexture`。

```csharp
float x0 = entry.TextureOffsetX / (float)pageW;
float y0 = entry.TextureOffsetY / (float)pageH;
float x1 = (entry.TextureOffsetX + entry.TextureWidth)  / (float)pageW;
float y1 = (entry.TextureOffsetY + entry.TextureHeight) / (float)pageH;

// Unity 纹理的 GL 行 0 在图像**底部**，ImGui 的 uv0 落在矩形左上角 → V 要翻
ImGui.Image(name, size, new Vector2(x0, y1), new Vector2(x1, y0));
```

- 没打包（`PackedEntry == null`）就是整张：`uv0=(0,1)`、`uv1=(1,0)`。
- 像素画必须最近邻：Unity 侧 `filterMode = Point` 会被保留——后端用的是"合成纹理/采样器状态"
  （`imgui_impl_opengl3.cpp:499` `glBindSampler(0, 0)`），不覆盖纹理自带的 filter。
- 线性色彩空间下颜色可能偏，必要时用 `ImageWithBg` 调色调，或改用 §4。

### 3.4 变体：不 blit，跟着游戏自己的绘制取名字

用托管钩子订阅 `SpriteBatch.Draw`（Bunny 就是这么拦的：`ResourceDraws.cpp:72-92`，`hook::install` + 原样调用），
当参数里的 `Texture2D` 是目标时 arm 探针，下一个 draw 读绑定名。
**零额外显存、零额外绘制**，代价是"要等游戏真的画到它"（背包、物品栏、Boss 血条这些场景都很快）。

### 3.5 坑

| 坑 | 说明 / 处理 |
|---|---|
| 图集页重建 | `RefreshAtlasTexture` 后 GL 名字会变 → 缓存按 `UnityBindTexture` 对象指针校验，变了就重探 |
| 名字复用 | Unity 删纹理后名字可能被别处再用 → 缓存的完整性靠"对象指针 + 尺寸"双校验 |
| 线程 | il2cpp/Unity 调用只能在游戏线程（`PylonTexture.cs:101-108` 的检查）；GL 查询只能在渲染线程；名字是整数，跨线程没问题 |
| 探针串行 | 一次只 arm 一个，否则多个 RT/页同帧会混淆 |
| alpha/palette | `UnityAlphaTexture` / `UnityPalTexture` 是给特定 shader 用的，普通显示用 `UnityBindTexture` |
| 名字池 | 与 `Textures::Create` 无关（不走池），但不要为每帧重新探针 |

---

## 4. 方法 B：CPU 上传（可读纹理 / mod 的 PNG）

现成的解码链路：`ScreenGraphics.Texture`（`ScreenGraphics.cs:61-97`）
`LoadImage → GetRawTextureData<byte>() → 翻行 → 上传`。

给 ImGui 用，把最后一步换成：

```csharp
ulong tex = ImGui.UploadTexture(rgba, w, h);   // ImGuiTextures.cs:22
if (tex != 0) ImGui.Image(tex, size, (0,0), (1,1));
```

- `Textures::Create` 期望**首行是图像顶部**（`ImGuiTextures.cs:19`，`Textures.cpp:34-43` 直接 `glTexImage2D`）：
  Unity 原始数据是底行在前，需要翻一次行；或干脆不翻、按 §3.3 用 UV 翻。
- 名字池只有 32 个（`Textures.cpp:19`），一次画很多图要允许返回 0 并重试。
- 原版图集 `isReadable = 0` → 这条路对 vanilla 无效。

---

## 5. 方法 C：Bunny 实际用的两条路（对照）

1. **让游戏自己画**：`UIImage : UIElement` 的 `DrawSelf(SpriteBatch)`（`UIImage.cs:68-84`）；
   Pylon 的对应物是 `ModPanel.AddImage`（`ModPanel.cs:38-51`）。贴图对象直接给 `SpriteBatch`，像素根本不出 GPU。
2. **游戏外要图**：把 PNG 放进包（`_sprites/`），`BitmapFactory` 解成 ARGB_8888 → RGBA → GL
   （`ModImages.kt:27-53`、`TextureRegistry.h:18-35`）。

Bunny 全仓**没有 ImGui**（`imgui` 关键字 0 处），它的菜单是 Java/dex UI，不在游戏的 GL 上下文里，
所以它只能走 1 或 2。Pylon 的覆盖层就在游戏上下文里，才有 §3 这条路——这也是两者最大的差别。

---

## 6. 选型

| 场景 | 走哪条 |
|---|---|
| 原版贴图（物品/NPC/Buff 图标、图集页） | A（GPU 零拷贝）；一页探一次，之后纯 UV |
| mod 的 PNG 贴图 | B（`UploadTexture`）或 A（mod 贴图同样在 GPU 上，A 也成立） |
| 要"随便给一张 `Texture2D` 就能画"的通用 API | A + `PylonTex_Resolve` 封装 |
| 不想动 native | C.1（用 `SpriteBatch` 面板画，不进 ImGui 窗口） |

---

## 7. 已经落地的实现

| 文件 | 作用 |
|---|---|
| `src/core/include/graphics/TexProbe.h` / `src/core/src/graphics/TexProbe.cpp` | 探针：游戏线程 `Arm(w,h)`，渲染线程 `OnDraw(fbo, viewport)` 认领那次 blit，`Take()` 取名字 |
| `src/core/include/graphics/GameTextures.h` / `src/core/src/graphics/GameTextures.cpp` | `Resolve(Texture2D*)`：il2cpp 读 `UnityBindTexture / UnityTextureWidth|Height / PackedEntry`，arm 探针 + `Internal_BlitMaterial5`，拿回名字后算 UV 与源尺寸；按贴图指针缓存（贴图集重建换绑就重探） |
| `src/core/src/graphics/Capture.cpp` | 两个 draw hook 各加一行 `TexProbe::OnDraw(...)`（没 arm 时只是一次原子读） |
| `src/core/include/pylon/graphics/GameTextureApi.h` / `src/core/src/graphics/GameTextureApi.cpp` | C 出口 `PylonTex_Resolve / PylonTex_Invalidate / PylonTex_Reset`，`PylonTexView` 直传 |
| `src/csharp/managed/Pylon.NaviteAPI/Graphics/GameTextureNative.cs` | 三个导出的 `LibraryImport` 绑定 |
| `src/csharp/managed/Pylon.ModLoader/Textures/GameTextures.cs` | `GameTextures.TryGet(Texture2D, out GameTextureView)`：托管缓存；内核缺导出时一次性关掉 |
| `src/csharp/managed/Pylon.ModLoader/ImGui/TerrariaUI.cs` | `TerrariaUI.Image(Texture2D?, Vector2 size, Rectangle? frame = null)` |

```csharp
// 物品图标：贴图 → 覆盖层直画，像素不出 GPU
TerrariaUI.Image(TextureAssets.Item[ItemID.LastPrism].Value, new Vector2(64f, 64f));

// 只画精灵里的一帧（参数是贴图内的源像素矩形）
TerrariaUI.Image(texture, new Vector2(40f, 56f), new Rectangle(0, 56, 40, 56));
```

- 第一次问一张贴图返回 `false` 并画一个凹陷占位：那次 blit 要等下一个渲染帧才被探针看到，之后一直命中缓存。
- 返回 `false` 的其它情况：不在游戏线程、图形层没装（`Pipe::Installed()` 为假）、这张贴图读不出来。
- 图集重建（`RefreshAtlasTexture`）后 GL 名字会变：宿主重置上下文时 `GameTextures.Reset()`，单张则 `Invalidate(texture)`。
- 想自己拿名字（画到自绘 draw list、或做别的采样），走 `GameTextures.TryGet` + `ImGui.Image(view.Texture, size, view.Uv0, view.Uv1)`。

### 冒烟验证（真机）

自动化：`src/csharp/samples/PipelineTest` 的 `tex` 组（`Tests/TextureTests.cs`）

```powershell
pwsh -File src/csharp/tools/pt.ps1 -Groups tex
```

- `TEX-01` null 入参、`TEX-02/03` 物品与 NPC 贴图解析 + UV 方向、`TEX-04` 缓存命中、`TEX-05` `Invalidate` 后重探。
- 判据：GL 名非 0、源尺寸 > 0、UV 在 `[0,1]`、`uv0.x < uv1.x`、`uv0.y > uv1.y`（V 已翻）。

肉眼：同一 Mod 的 **贴图** Tab（`src/TextureTab.cs`）列出物品/NPC/增益/抛射/图格五张原版贴图 + 一次取帧，看三件事：
**方向**（上下不能反）、**边界**（不能吃到相邻精灵）、**颜色**（不发灰/不发亮）。
之后进出世界、切物品栏再看一遍：页重建后名字会变，点「清缓存并重探」。
