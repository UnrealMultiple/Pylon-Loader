/**
 * @file VulkanBackend.cpp
 * @brief Vulkan 渲染后端实现
 *
 * 本文件实现了基于 Vulkan API 的图形渲染后端，核心流程包括：
 *   1. 创建 Vulkan 实例 / Surface / 设备 / 交换链 / 渲染通道 等基础设施
 *   2. 初始化 ImGui Vulkan 渲染后端
 *   3. 每帧通过 BeginFrame / EndFrame 完成渲染和呈现
 *   4. Shutdown 时安全释放所有 GPU 资源
 *
 * This is the only graphics backend kept by the project.
 */

#include "VulkanBackend.h"

#include <android/native_window.h>
#include <android/set_abort_message.h>
#include <algorithm>
#include <cstring>

#include "imgui.h"
#include "imgui_impl_vulkan.h"
#include "VulkanTexture.h"
#include "Logger.h"

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

/**
 * @brief Vulkan API 调用结果检查宏
 *
 * 若 Vulkan 函数返回非 VK_SUCCESS，记录错误日志并从当前函数 return false。
 * 仅在返回 bool 的函数中使用。
 */
#define VK_CHECK(expr)                                                     \
    do {                                                                   \
        VkResult _r = (expr);                                              \
        if (_r != VK_SUCCESS) {                                            \
            LOGE("[VulkanBackend] %s failed: %d", #expr, (int)_r);         \
            return false;                                                  \
        }                                                                  \
    } while (0)

constexpr uint64_t kAcquireTimeoutNanoseconds = 1'000'000;

// Init 逐步留痕：某一步若把进程直接打死（驱动 abort / ImGui 断言），日志里最后一条
// "init: xxx" 就是现场，不必靠猜。
#define PYLON_INIT_STEP(name, expr)                        \
    do {                                                   \
        if (!ok) break;                                    \
        android_set_abort_message("vulkan-init: " name);   \
        LOGI("[VulkanBackend] init: %s", name);            \
        ok = (expr);                                       \
    } while (0)

/**
 * @brief ImGui Vulkan 后端的错误回调
 *
 * 传递给 ImGui_ImplVulkan_InitInfo::CheckVkResultFn，
 * ImGui 内部调用 Vulkan API 时若失败会通过此回调记录日志。
 */
static void CheckVkResult(VkResult err)
{
    if (err != VK_SUCCESS)
        LOGE("[VulkanBackend] VkResult = %d", (int)err);
}

// ===========================================================================
// Init —— 整体初始化入口
// ===========================================================================

/**
 * @brief 初始化整个 Vulkan 渲染管线
 *
 * 按顺序调用 12 个子步骤初始化 Vulkan 英古融：
 *   Instance → Surface → PhysicalDevice → LogicalDevice → Swapchain
 *   → RenderPass → Framebuffers → CommandPool → CommandBuffers
 *   → SyncObjects → DescriptorPool → ImGui Vulkan 后端
 *
 * 任何一步失败均会调用 Shutdown() 回滚已创建的资源，避免泄漏。
 *
 * @param window  Android 原生窗口句柄
 * @param width   渲染宽度（像素）
 * @param height  渲染高度（像素）
 * @return true 初始化成功，false 失败（资源已回滚）
 */
bool VulkanBackend::Init(ANativeWindow *window, int width, int height)
{
    width_  = width;
    height_ = height;
    semaphoreIndex_ = 0;
    acquiredImageIndex_ = 0;
    presentedFrameCount_ = 0;
    recoveryScope_ = RecoveryScope::None;

    // 逐步执行并留痕：某一步若把进程直接打死（驱动 abort / ImGui 断言），
    // 日志里最后一条 "init: xxx" 就是现场，不必猜。
    bool ok = true;
    PYLON_INIT_STEP("instance",         CreateInstance());
    PYLON_INIT_STEP("surface",          CreateSurface(window));
    PYLON_INIT_STEP("physical-device",  SelectPhysicalDevice());
    PYLON_INIT_STEP("logical-device",   CreateLogicalDevice());
    PYLON_INIT_STEP("swapchain",        CreateSwapchain(width, height));
    PYLON_INIT_STEP("render-pass",      CreateRenderPass());
    PYLON_INIT_STEP("framebuffers",     CreateFramebuffers());
    PYLON_INIT_STEP("command-pool",     CreateCommandPool());
    PYLON_INIT_STEP("command-buffers",  CreateCommandBuffers());
    PYLON_INIT_STEP("sync-objects",     CreateSyncObjects());
    PYLON_INIT_STEP("descriptor-pool",  CreateDescriptorPool());
    PYLON_INIT_STEP("imgui-vulkan",     InitImGuiVulkan());

    if (!ok)
    {
        Shutdown();  // 安全回滚已创建的资源
        return false;
    }

    initialized_ = true;
    LOGI("[VulkanBackend] initialized %dx%d  images=%u", width_, height_, imageCount_);

    // Hand the Vulkan context to the icon-texture helper (used by the Terraria item/buff grids).
    VulkanTexture::SetContext(device_, physicalDevice_, graphicsQueue_, queueFamily_,
                              commandPool_, descriptorPool_);
    return true;
}

// ===========================================================================
// Per-frame —— 每帧渲染循环
// ===========================================================================

/**
 * @brief 帧开始：同步 + 获取交换链图像 + ImGui NewFrame
 *
 * 流程：
 *   1. 等待当前帧对应的 Fence，确保 GPU 已完成上一次使用该帧槽位的渲染
 *   2. 从交换链获取下一张可用图像（通过 imageAvailableSemaphore 通知）
 *   3. 重置 Fence，标记帧已开始
 *   4. 调用 ImGui_ImplVulkan_NewFrame() 准备 ImGui 渲染状态
 *
 * 若获取图像失败，frameStarted_ 保持 false，EndFrame() 将跳过提交。
 */
void VulkanBackend::BeginFrame()
{
    frameStarted_ = false;
    if (recoveryScope_ != RecoveryScope::None)
        return;

    if (semaphoreIndex_ >= semaphoreCount_ ||
        semaphoreIndex_ >= imageAvailableSemaphores_.size())
    {
        RequestRecovery(VK_ERROR_UNKNOWN);
        return;
    }

    // Acquire semaphores are independent from swapchain image ownership.
    VkResult result = vkAcquireNextImageKHR(
        device_, swapchain_, kAcquireTimeoutNanoseconds,
        imageAvailableSemaphores_[semaphoreIndex_], VK_NULL_HANDLE,
        &acquiredImageIndex_);

    if (result == VK_TIMEOUT || result == VK_NOT_READY)
        return;
    if (result != VK_SUCCESS && result != VK_SUBOPTIMAL_KHR)
    {
        LOGE("[VulkanBackend] vkAcquireNextImageKHR failed: %d", (int)result);
        RequestRecovery(result);
        return;
    }

    if (acquiredImageIndex_ >= inFlightFences_.size() ||
        acquiredImageIndex_ >= commandBuffers_.size() ||
        acquiredImageIndex_ >= framebuffers_.size())
    {
        RequestRecovery(VK_ERROR_UNKNOWN);
        return;
    }

    // An acquired image cannot still be owned by presentation. Its previous
    // render fence should therefore already be signaled; keep the wait bounded
    // so a broken driver cannot block Unity's render callback.
    VkResult waitResult = vkWaitForFences(
        device_, 1, &inFlightFences_[acquiredImageIndex_], VK_TRUE,
        kAcquireTimeoutNanoseconds);
    if (waitResult != VK_SUCCESS)
    {
        if (waitResult != VK_TIMEOUT && waitResult != VK_NOT_READY)
            LOGE("[VulkanBackend] vkWaitForFences failed: %d", (int)waitResult);
        RequestRecovery(waitResult == VK_TIMEOUT || waitResult == VK_NOT_READY
                            ? VK_ERROR_UNKNOWN
                            : waitResult);
        return;
    }

    VkResult resetResult =
        vkResetFences(device_, 1, &inFlightFences_[acquiredImageIndex_]);
    if (resetResult != VK_SUCCESS)
    {
        LOGE("[VulkanBackend] vkResetFences failed: %d", (int)resetResult);
        RequestRecovery(resetResult);
        return;
    }
    frameStarted_ = true;

    // 通知 ImGui Vulkan 后端新帧开始
    ImGui_ImplVulkan_NewFrame();
}

/**
 * @brief 帧结束：录制命令 → 提交 → 呈现
 *
 * 流程：
 *   1. 获取 ImGui 绘制数据（DrawData）
 *   2. 重置并开始录制命令缓冲
 *   3. 启动 RenderPass，清屏色为透明（用于叠加层）
 *   4. 调用 ImGui_ImplVulkan_RenderDrawData 将 UI 录制到命令缓冲
 *   5. 结束 RenderPass 和命令缓冲
 *   6. 提交命令缓冲到图形队列，等待 imageAvailable 信号量，
 *      完成后发出 renderFinished 信号量并标记 Fence
 *   7. 将渲染结果呈现到屏幕
 *   8. 成功呈现后推进 semaphore 索引
 */
void VulkanBackend::EndFrame()
{
    if (!frameStarted_)
        return;

    ImDrawData *drawData = ImGui::GetDrawData();
    if (!drawData)
    {
        RequestRecovery(VK_ERROR_UNKNOWN);
        return;
    }

    // ------ 1. 录制命令缓冲 ------
    VkCommandBuffer cmd = commandBuffers_[acquiredImageIndex_];
    VkResult resetCmdResult = vkResetCommandBuffer(cmd, 0);
    if (resetCmdResult != VK_SUCCESS)
    {
        LOGE("[VulkanBackend] vkResetCommandBuffer failed: %d", (int)resetCmdResult);
        RequestRecovery(resetCmdResult);
        return;
    }

    VkCommandBufferBeginInfo beginInfo{};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT; // 每帧重新录制
    VkResult beginCmdResult = vkBeginCommandBuffer(cmd, &beginInfo);
    if (beginCmdResult != VK_SUCCESS)
    {
        LOGE("[VulkanBackend] vkBeginCommandBuffer failed: %d", (int)beginCmdResult);
        RequestRecovery(beginCmdResult);
        return;
    }

    // ------ 2. 开始渲染通道，清屏为全透明（用作叠加层） ------
    VkClearValue clearValue{};
    clearValue.color = {{0.0f, 0.0f, 0.0f, 0.0f}}; // RGBA = (0,0,0,0) 透明

    VkRenderPassBeginInfo rpBegin{};
    rpBegin.sType             = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    rpBegin.renderPass        = renderPass_;
    rpBegin.framebuffer       = framebuffers_[acquiredImageIndex_];
    rpBegin.renderArea.extent = swapchainExtent_;
    rpBegin.clearValueCount   = 1;
    rpBegin.pClearValues      = &clearValue;

    vkCmdBeginRenderPass(cmd, &rpBegin, VK_SUBPASS_CONTENTS_INLINE);

    // ------ 3. 将 ImGui 绘制数据录制到命令缓冲 ------
    ImGui_ImplVulkan_RenderDrawData(drawData, cmd);

    vkCmdEndRenderPass(cmd);
    VkResult endCmdResult = vkEndCommandBuffer(cmd);
    if (endCmdResult != VK_SUCCESS)
    {
        LOGE("[VulkanBackend] vkEndCommandBuffer failed: %d", (int)endCmdResult);
        RequestRecovery(endCmdResult);
        return;
    }

    // ------ 4. 提交命令缓冲 ------
    // 等待 imageAvailable 信号量在 COLOR_ATTACHMENT_OUTPUT 阶段
    VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;

    VkSubmitInfo submitInfo{};
    submitInfo.sType                = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submitInfo.waitSemaphoreCount   = 1;
    submitInfo.pWaitSemaphores      = &imageAvailableSemaphores_[semaphoreIndex_];
    submitInfo.pWaitDstStageMask    = &waitStage;
    submitInfo.commandBufferCount   = 1;
    submitInfo.pCommandBuffers      = &cmd;
    submitInfo.signalSemaphoreCount = 1;
    submitInfo.pSignalSemaphores    = &renderFinishedSemaphores_[semaphoreIndex_];

    // 提交到图形队列，完成后标记 inFlightFence
    VkResult submitResult = vkQueueSubmit(
        graphicsQueue_, 1, &submitInfo, inFlightFences_[acquiredImageIndex_]);
    if (submitResult != VK_SUCCESS)
    {
        LOGE("[VulkanBackend] vkQueueSubmit failed: %d", (int)submitResult);
        RequestRecovery(submitResult);
        return;
    }

    // ------ 5. 呈现到屏幕 ------
    VkPresentInfoKHR presentInfo{};
    presentInfo.sType              = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
    presentInfo.waitSemaphoreCount = 1;
    presentInfo.pWaitSemaphores    = &renderFinishedSemaphores_[semaphoreIndex_];
    presentInfo.swapchainCount     = 1;
    presentInfo.pSwapchains        = &swapchain_;
    presentInfo.pImageIndices      = &acquiredImageIndex_;

    VkResult presentResult = vkQueuePresentKHR(graphicsQueue_, &presentInfo);
    if (presentResult != VK_SUCCESS && presentResult != VK_SUBOPTIMAL_KHR)
    {
        LOGE("[VulkanBackend] vkQueuePresentKHR failed: %d", (int)presentResult);
        RequestRecovery(presentResult);
    }
    else
    {
        ++presentedFrameCount_;
        semaphoreIndex_ = (semaphoreIndex_ + 1) % semaphoreCount_;
    }
}

// ===========================================================================
// Shutdown —— 安全释放所有 Vulkan 资源
// ===========================================================================

/**
 * @brief 释放所有已创建的 Vulkan 资源
 *
 * 释放顺序与创建顺序相反，确保依赖关系正确：
 *   1. 等待 GPU 空闲
 *   2. 关闭 ImGui Vulkan 后端
 *   3. 销毁同步原语（Fence / Semaphore）
 *   4. 清理交换链相关资源
 *   5. 销毁描述符池、命令池、逻辑设备、Surface、Instance
 *
 * 每个资源在销毁前检查是否为 VK_NULL_HANDLE，避免重复释放。
 */
void VulkanBackend::Shutdown()
{
    if (device_)                                                           // 等待 GPU 所有操作完成
        vkDeviceWaitIdle(device_);

    VulkanTexture::DestroyAll();                                           // 释放图标纹理（在 device 销毁前）
    VulkanTexture::ClearContext();

    if (initialized_)                                                       // 关闭 ImGui Vulkan 后端
        ImGui_ImplVulkan_Shutdown();

    // 销毁同步原语（每帧一组 Fence + 2 个 Semaphore）
    for (auto f : inFlightFences_)
        if (f) vkDestroyFence(device_, f, nullptr);
    for (auto s : imageAvailableSemaphores_)
        if (s) vkDestroySemaphore(device_, s, nullptr);
    for (auto s : renderFinishedSemaphores_)
        if (s) vkDestroySemaphore(device_, s, nullptr);
    inFlightFences_.clear();
    imageAvailableSemaphores_.clear();
    renderFinishedSemaphores_.clear();

    CleanupSwapchain();                                                    // 清理交换链及关联资源

    // 销毁剩余核心对象（反序创建顺序）
    if (descriptorPool_) { vkDestroyDescriptorPool(device_, descriptorPool_, nullptr); descriptorPool_ = VK_NULL_HANDLE; }
    if (commandPool_)    { vkDestroyCommandPool(device_, commandPool_, nullptr);       commandPool_    = VK_NULL_HANDLE; }
    if (device_)         { vkDestroyDevice(device_, nullptr);                          device_         = VK_NULL_HANDLE; }
    if (surface_)        { vkDestroySurfaceKHR(instance_, surface_, nullptr);           surface_        = VK_NULL_HANDLE; }
    if (instance_)       { vkDestroyInstance(instance_, nullptr);                       instance_       = VK_NULL_HANDLE; }

    physicalDevice_ = VK_NULL_HANDLE;
    initialized_    = false;
    frameStarted_   = false;
    semaphoreIndex_ = 0;
    semaphoreCount_ = 0;
    acquiredImageIndex_ = 0;

    LOGI("[VulkanBackend] shutdown");
}

bool VulkanBackend::IsReady() const
{
    return initialized_ && recoveryScope_ == RecoveryScope::None;
}

bool VulkanBackend::RequiresSurfaceRebuild() const
{
    return recoveryScope_ == RecoveryScope::Surface;
}

bool VulkanBackend::Resize(int width, int height)
{
    if (!initialized_ || device_ == VK_NULL_HANDLE) return false;
    if (width <= 0 || height <= 0) return false;
    if (width == width_ && height == height_ && recoveryScope_ == RecoveryScope::None)
        return true;

    vkDeviceWaitIdle(device_);

    // 同步原语与命令缓冲按 imageCount 分配，交换链可能换出不同的图像数，必须整组重来。
    for (auto f : inFlightFences_)
        if (f) vkDestroyFence(device_, f, nullptr);
    for (auto s : imageAvailableSemaphores_)
        if (s) vkDestroySemaphore(device_, s, nullptr);
    for (auto s : renderFinishedSemaphores_)
        if (s) vkDestroySemaphore(device_, s, nullptr);
    inFlightFences_.clear();
    imageAvailableSemaphores_.clear();
    renderFinishedSemaphores_.clear();

    if (commandPool_ && !commandBuffers_.empty())
        vkFreeCommandBuffers(device_, commandPool_,
                             static_cast<uint32_t>(commandBuffers_.size()),
                             commandBuffers_.data());
    commandBuffers_.clear();

    // ImGui 后端的管线绑定旧的 render pass，必须一起重建（字体纹理由 NewFrame 自动重做）。
    ImGui_ImplVulkan_Shutdown();
    CleanupSwapchain();

    width_  = width;
    height_ = height;
    semaphoreIndex_     = 0;
    semaphoreCount_     = 0;
    acquiredImageIndex_ = 0;
    frameStarted_       = false;

    const bool ok = CreateSwapchain(width, height)
                 && CreateRenderPass()
                 && CreateFramebuffers()
                 && CreateCommandBuffers()
                 && CreateSyncObjects()
                 && InitImGuiVulkan();
    if (!ok)
    {
        LOGE("[VulkanBackend] resize -> %dx%d failed", width, height);
        initialized_   = false;
        recoveryScope_ = RecoveryScope::Backend;
        return false;
    }

    recoveryScope_ = RecoveryScope::None;
    LOGI("[VulkanBackend] resized %dx%d images=%u", width_, height_, imageCount_);
    return true;
}

uint64_t VulkanBackend::PresentedFrameCount() const
{
    return presentedFrameCount_;
}

void VulkanBackend::RequestRecovery(VkResult result)
{
    if (result == VK_ERROR_SURFACE_LOST_KHR)
    {
        recoveryScope_ = RecoveryScope::Surface;
        return;
    }

    if (recoveryScope_ == RecoveryScope::None)
        recoveryScope_ = RecoveryScope::Backend;
}

// ===========================================================================
// 1. Instance —— 创建 Vulkan 实例
// ===========================================================================

/**
 * @brief 创建 Vulkan 实例
 *
 * 启用 VK_KHR_surface 和 VK_KHR_android_surface 扩展，
 * 这是在 Android 上创建渲染表面的必要条件。
 * 使用 Vulkan 1.0 API 版本以确保最广泛的设备兼容性。
 */
bool VulkanBackend::CreateInstance()
{
    VkApplicationInfo appInfo{};
    appInfo.sType              = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    appInfo.pApplicationName   = "ImGuiOverlay";
    appInfo.applicationVersion = VK_MAKE_VERSION(1, 0, 0);
    appInfo.pEngineName        = "NoEngine";
    appInfo.engineVersion      = VK_MAKE_VERSION(1, 0, 0);
    appInfo.apiVersion         = VK_API_VERSION_1_0;  // 使用 Vulkan 1.0 保证兼容性

    // Android 平台必须启用的两个表面扩展
    const char *extensions[] = {
        VK_KHR_SURFACE_EXTENSION_NAME,          // 通用表面扩展
        VK_KHR_ANDROID_SURFACE_EXTENSION_NAME,  // Android 专用表面扩展
    };

    VkInstanceCreateInfo ci{};
    ci.sType                   = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    ci.pApplicationInfo        = &appInfo;
    ci.enabledExtensionCount   = 2;
    ci.ppEnabledExtensionNames = extensions;

    VK_CHECK(vkCreateInstance(&ci, nullptr, &instance_));
    LOGI("[VulkanBackend] instance created");
    return true;
}

// ===========================================================================
// 2. Surface —— 创建 Android 渲染表面
// ===========================================================================

/**
 * @brief 从 Android 原生窗口创建 Vulkan 渲染表面
 *
 * VkSurfaceKHR 是 Vulkan 与窗口系统之间的桥梁，
 * 交换链将图像呈现到这个表面上。
 *
 * @param window Android 原生窗口句柄
 */
bool VulkanBackend::CreateSurface(ANativeWindow *window)
{
    VkAndroidSurfaceCreateInfoKHR ci{};
    ci.sType  = VK_STRUCTURE_TYPE_ANDROID_SURFACE_CREATE_INFO_KHR;
    ci.window = window;

    const VkResult result =
        vkCreateAndroidSurfaceKHR(instance_, &ci, nullptr, &surface_);
    if (result != VK_SUCCESS)
    {
        LOGE("[VulkanBackend] vkCreateAndroidSurfaceKHR failed: %d", (int)result);
        RequestRecovery(result);
        return false;
    }
    LOGI("[VulkanBackend] surface created");
    return true;
}

// ===========================================================================
// 3. Physical device —— 选择物理 GPU
// ===========================================================================

/**
 * @brief 枚举并选择合适的物理设备（GPU）
 *
 * 选择条件：
 *   - 拥有支持图形操作的队列族（VK_QUEUE_GRAPHICS_BIT）
 *   - 该队列族同时支持呈现（present）到已创建的 Surface
 *   - 设备支持 VK_KHR_swapchain 扩展
 *
 * 找到第一个满足条件的设备即停止搜索，
 * 并记录 physicalDevice_ 和 queueFamily_。
 */
bool VulkanBackend::SelectPhysicalDevice()
{
    uint32_t count = 0;
    vkEnumeratePhysicalDevices(instance_, &count, nullptr);
    if (count == 0) { LOGE("[VulkanBackend] no GPU found"); return false; }

    std::vector<VkPhysicalDevice> devices(count);
    vkEnumeratePhysicalDevices(instance_, &count, devices.data());

    for (auto dev : devices)
    {
        // 遍历每个设备的队列族，查找同时支持 graphics + present 的队列
        uint32_t qfCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(dev, &qfCount, nullptr);
        std::vector<VkQueueFamilyProperties> qfProps(qfCount);
        vkGetPhysicalDeviceQueueFamilyProperties(dev, &qfCount, qfProps.data());

        for (uint32_t i = 0; i < qfCount; ++i)
        {
            // 检查该队列族是否支持图形操作
            if (!(qfProps[i].queueFlags & VK_QUEUE_GRAPHICS_BIT))
                continue;

            // 检查该队列族是否支持 present 到 Surface
            VkBool32 presentSupport = VK_FALSE;
            vkGetPhysicalDeviceSurfaceSupportKHR(dev, i, surface_, &presentSupport);
            if (!presentSupport)
                continue;

            // 检查设备是否支持 VK_KHR_swapchain 扩展（呈现的前置条件）
            uint32_t extCount = 0;
            vkEnumerateDeviceExtensionProperties(dev, nullptr, &extCount, nullptr);
            std::vector<VkExtensionProperties> exts(extCount);
            vkEnumerateDeviceExtensionProperties(dev, nullptr, &extCount, exts.data());

            bool hasSwapchain = false;
            for (auto &e : exts)
                if (strcmp(e.extensionName, VK_KHR_SWAPCHAIN_EXTENSION_NAME) == 0)
                { hasSwapchain = true; break; }

            if (!hasSwapchain)
                continue;

            physicalDevice_ = dev;
            queueFamily_    = i;

            VkPhysicalDeviceProperties props;
            vkGetPhysicalDeviceProperties(dev, &props);
            LOGI("[VulkanBackend] GPU: %s  queueFamily=%u", props.deviceName, i);
            return true;
        }
    }

    LOGE("[VulkanBackend] no suitable GPU found");
    return false;
}

// ===========================================================================
// 4. Logical device —— 创建逻辑设备
// ===========================================================================

/**
 * @brief 创建 Vulkan 逻辑设备并获取图形队列
 *
 * 从已选定的物理设备创建逻辑设备，启用 VK_KHR_swapchain 扩展。
 * 仅请求一个队列，因为图形和呈现使用同一个队列族。
 */
bool VulkanBackend::CreateLogicalDevice()
{
    float priority = 1.0f;  // 队列优先级（0.0 ~ 1.0）

    // 只需一个队列（图形 + 呈现共用）
    VkDeviceQueueCreateInfo qci{};
    qci.sType            = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
    qci.queueFamilyIndex = queueFamily_;
    qci.queueCount       = 1;
    qci.pQueuePriorities = &priority;

    const char *devExts[] = { VK_KHR_SWAPCHAIN_EXTENSION_NAME }; // 启用交换链扩展

    VkDeviceCreateInfo dci{};
    dci.sType                   = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    dci.queueCreateInfoCount    = 1;
    dci.pQueueCreateInfos       = &qci;
    dci.enabledExtensionCount   = 1;
    dci.ppEnabledExtensionNames = devExts;

    VK_CHECK(vkCreateDevice(physicalDevice_, &dci, nullptr, &device_));
    vkGetDeviceQueue(device_, queueFamily_, 0, &graphicsQueue_);

    LOGI("[VulkanBackend] device created");
    return true;
}

// ===========================================================================
// 5. Swapchain —— 创建交换链
// ===========================================================================

/**
 * @brief 创建交换链及其图像视图
 *
 * 交换链是 Vulkan 与窗口系统之间的图像队列，配置过程包括：
 *   - 图像格式：优先选择 R8G8B8A8_UNORM
 *   - 分辨率：优先使用 Surface 当前尺寸，否则 clamp 到允许范围
 *   - 图像数量：minImageCount + 1（三重缓冲）
 *   - 合成 Alpha：优先 INHERIT，退而求其次（用于透明叠加层）
 *   - 呈现模式：优先 MAILBOX（低延迟），回退 FIFO（始终可用）
 *
 * 创建完成后还会为每张图像创建对应的 VkImageView。
 *
 * @param width   期望宽度
 * @param height  期望高度
 */
bool VulkanBackend::CreateSwapchain(int width, int height)
{
    VkSurfaceCapabilitiesKHR caps;
    VK_CHECK(vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice_, surface_, &caps));

    // ------ 选择图像格式：优先 R8G8B8A8_UNORM ------
    uint32_t fmtCount = 0;
    vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice_, surface_, &fmtCount, nullptr);
    std::vector<VkSurfaceFormatKHR> formats(fmtCount);
    vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice_, surface_, &fmtCount, formats.data());

    VkSurfaceFormatKHR selectedFmt = formats[0];
    for (auto &f : formats)
    {
        if (f.format == VK_FORMAT_R8G8B8A8_UNORM)
        { selectedFmt = f; break; }
    }
    swapchainFormat_ = selectedFmt.format;

    // ------ 确定交换链尺寸 ------
    if (caps.currentExtent.width != UINT32_MAX)
    {
        swapchainExtent_ = caps.currentExtent;
    }
    else
    {
        swapchainExtent_.width  = std::clamp(static_cast<uint32_t>(width),
                                             caps.minImageExtent.width,  caps.maxImageExtent.width);
        swapchainExtent_.height = std::clamp(static_cast<uint32_t>(height),
                                             caps.minImageExtent.height, caps.maxImageExtent.height);
    }

    // ------ 图像数量：minImageCount + 1 实现三重缓冲 ------
    imageCount_ = caps.minImageCount + 1;
    if (caps.maxImageCount > 0 && imageCount_ > caps.maxImageCount)
        imageCount_ = caps.maxImageCount;

    // ------ 合成 Alpha 模式：用于透明叠加层渲染 ------
    VkCompositeAlphaFlagBitsKHR compositeAlpha = VK_COMPOSITE_ALPHA_INHERIT_BIT_KHR;
    if (!(caps.supportedCompositeAlpha & VK_COMPOSITE_ALPHA_INHERIT_BIT_KHR))
    {
        if (caps.supportedCompositeAlpha & VK_COMPOSITE_ALPHA_POST_MULTIPLIED_BIT_KHR)
            compositeAlpha = VK_COMPOSITE_ALPHA_POST_MULTIPLIED_BIT_KHR;
        else if (caps.supportedCompositeAlpha & VK_COMPOSITE_ALPHA_PRE_MULTIPLIED_BIT_KHR)
            compositeAlpha = VK_COMPOSITE_ALPHA_PRE_MULTIPLIED_BIT_KHR;
        else
            compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
    }

    // ------ 呈现模式：优先 MAILBOX（三重缓冲低延迟），回退 FIFO ------
    uint32_t pmCount = 0;
    vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice_, surface_, &pmCount, nullptr);
    std::vector<VkPresentModeKHR> presentModes(pmCount);
    vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice_, surface_, &pmCount, presentModes.data());

    VkPresentModeKHR presentMode = VK_PRESENT_MODE_FIFO_KHR;
    for (auto m : presentModes)
        if (m == VK_PRESENT_MODE_MAILBOX_KHR) { presentMode = m; break; }

    // ------ 创建交换链对象 ------
    VkSwapchainCreateInfoKHR sci{};
    sci.sType            = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
    sci.surface          = surface_;
    sci.minImageCount    = imageCount_;
    sci.imageFormat      = selectedFmt.format;
    sci.imageColorSpace  = selectedFmt.colorSpace;
    sci.imageExtent      = swapchainExtent_;
    sci.imageArrayLayers = 1;                                  // 非立体渲染
    sci.imageUsage       = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT; // 仅用作颜色附件
    sci.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;           // 单队列族独占
    sci.preTransform     = VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR;
    sci.compositeAlpha   = compositeAlpha;
    sci.presentMode      = presentMode;
    sci.clipped          = VK_TRUE;                             // 裁剪被遮挡像素

    VK_CHECK(vkCreateSwapchainKHR(device_, &sci, nullptr, &swapchain_));

    // ------ 获取交换链图像句柄 ------
    vkGetSwapchainImagesKHR(device_, swapchain_, &imageCount_, nullptr);
    swapchainImages_.resize(imageCount_);
    vkGetSwapchainImagesKHR(device_, swapchain_, &imageCount_, swapchainImages_.data());

    // ------ 为每张图像创建 ImageView（渲染通道需要） ------
    swapchainImageViews_.resize(imageCount_);
    for (uint32_t i = 0; i < imageCount_; ++i)
    {
        VkImageViewCreateInfo vci{};
        vci.sType    = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        vci.image    = swapchainImages_[i];
        vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
        vci.format   = swapchainFormat_;
        vci.components = { VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY,
                           VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY };
        vci.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };

        VK_CHECK(vkCreateImageView(device_, &vci, nullptr, &swapchainImageViews_[i]));
    }

    LOGI("[VulkanBackend] swapchain %ux%u  images=%u  fmt=%d",
         swapchainExtent_.width, swapchainExtent_.height, imageCount_, (int)swapchainFormat_);
    return true;
}

// ===========================================================================
// 6. Render pass —— 创建渲染通道
// ===========================================================================

/**
 * @brief 创建渲染通道
 *
 * 配置单个 color attachment：
 *   - loadOp = CLEAR：每帧开始时清屏
 *   - storeOp = STORE：渲染结果保留以便呈现
 *   - initialLayout = UNDEFINED → finalLayout = PRESENT_SRC
 *
 * 子通道依赖确保外部 → 子通道0 的过渡正确，
 * 即在 COLOR_ATTACHMENT_OUTPUT 阶段完成布局转换。
 */
bool VulkanBackend::CreateRenderPass()
{
    VkAttachmentDescription att{};
    att.format         = swapchainFormat_;
    att.samples        = VK_SAMPLE_COUNT_1_BIT;         // 不使用多重采样
    att.loadOp         = VK_ATTACHMENT_LOAD_OP_CLEAR;    // 帧开始时清除
    att.storeOp        = VK_ATTACHMENT_STORE_OP_STORE;   // 帧结束后保存
    att.stencilLoadOp  = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    att.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    att.initialLayout  = VK_IMAGE_LAYOUT_UNDEFINED;      // 不关心初始内容
    att.finalLayout    = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR; // 渲染后直接可呈现

    VkAttachmentReference ref{};
    ref.attachment = 0;
    ref.layout     = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint    = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount = 1;
    subpass.pColorAttachments    = &ref;

    // 子通道依赖：确保外部→子通道0的布局转换在 COLOR_ATTACHMENT_OUTPUT 阶段完成
    VkSubpassDependency dep{};
    dep.srcSubpass    = VK_SUBPASS_EXTERNAL;
    dep.dstSubpass    = 0;
    dep.srcStageMask  = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    dep.dstStageMask  = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    dep.srcAccessMask = 0;
    dep.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

    VkRenderPassCreateInfo rpci{};
    rpci.sType           = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    rpci.attachmentCount = 1;
    rpci.pAttachments    = &att;
    rpci.subpassCount    = 1;
    rpci.pSubpasses      = &subpass;
    rpci.dependencyCount = 1;
    rpci.pDependencies   = &dep;

    VK_CHECK(vkCreateRenderPass(device_, &rpci, nullptr, &renderPass_));
    return true;
}

// ===========================================================================
// 7. Framebuffers —— 创建帧缓冲
// ===========================================================================

/**
 * @brief 为每张交换链图像创建帧缓冲
 *
 * 每个帧缓冲绑定一个交换链 ImageView，
 * 渲染时通过 acquiredImageIndex_ 选择对应的帧缓冲。
 */
bool VulkanBackend::CreateFramebuffers()
{
    framebuffers_.resize(imageCount_);
    for (uint32_t i = 0; i < imageCount_; ++i)
    {
        VkFramebufferCreateInfo fci{};
        fci.sType           = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        fci.renderPass      = renderPass_;
        fci.attachmentCount = 1;
        fci.pAttachments    = &swapchainImageViews_[i];
        fci.width           = swapchainExtent_.width;
        fci.height          = swapchainExtent_.height;
        fci.layers          = 1;

        VK_CHECK(vkCreateFramebuffer(device_, &fci, nullptr, &framebuffers_[i]));
    }
    return true;
}

// ===========================================================================
// 8. Command pool —— 创建命令池
// ===========================================================================

/**
 * @brief 创建命令池
 *
 * 启用 RESET_COMMAND_BUFFER_BIT 允许单独重置每个命令缓冲，
 * 这样每帧可以重新录制而不必重建整个命令池。
 */
bool VulkanBackend::CreateCommandPool()
{
    VkCommandPoolCreateInfo cpci{};
    cpci.sType            = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    cpci.flags            = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT; // 允许单独重置
    cpci.queueFamilyIndex = queueFamily_;  // 绑定到图形队列族

    VK_CHECK(vkCreateCommandPool(device_, &cpci, nullptr, &commandPool_));
    return true;
}

// ===========================================================================
// 9. Command buffers —— 分配命令缓冲
// ===========================================================================

/**
 * @brief 分配主要级命令缓冲
 *
 * 每张交换链图像使用独立命令缓冲。
 * PRIMARY 级别可直接提交到队列。
 */
bool VulkanBackend::CreateCommandBuffers()
{
    commandBuffers_.resize(imageCount_);

    VkCommandBufferAllocateInfo ai{};
    ai.sType              = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    ai.commandPool        = commandPool_;
    ai.level              = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    ai.commandBufferCount = imageCount_;

    VK_CHECK(vkAllocateCommandBuffers(device_, &ai, commandBuffers_.data()));
    return true;
}

// ===========================================================================
// 10. Sync objects —— 创建同步原语
// ===========================================================================

/**
 * @brief 创建帧同步原语
 *
 * 每帧槽位包含：
 *   - inFlightFence：CPU 等待 GPU 完成（初始为 signaled 状态以免第一帧死锁）
 *   - imageAvailableSemaphore：GPU 内部同步，交换链图像可用时触发
 *   - renderFinishedSemaphore：GPU 内部同步，渲染完成时触发，用于呈现等待
 */
bool VulkanBackend::CreateSyncObjects()
{
    semaphoreCount_ = imageCount_ + 1;
    inFlightFences_.resize(imageCount_);
    imageAvailableSemaphores_.resize(semaphoreCount_);
    renderFinishedSemaphores_.resize(semaphoreCount_);

    VkFenceCreateInfo fci{};
    fci.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    fci.flags = VK_FENCE_CREATE_SIGNALED_BIT;  // 初始为 signaled，避免第一帧死锁

    VkSemaphoreCreateInfo sci{};
    sci.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

    for (uint32_t i = 0; i < imageCount_; ++i)
    {
        VK_CHECK(vkCreateFence(device_, &fci, nullptr, &inFlightFences_[i]));
    }
    for (uint32_t i = 0; i < semaphoreCount_; ++i)
    {
        VK_CHECK(vkCreateSemaphore(device_, &sci, nullptr, &imageAvailableSemaphores_[i]));
        VK_CHECK(vkCreateSemaphore(device_, &sci, nullptr, &renderFinishedSemaphores_[i]));
    }
    return true;
}

// ===========================================================================
// 11. Descriptor pool —— 创建描述符池
// ===========================================================================

/**
 * @brief 创建描述符池
 *
 * ImGui Vulkan 后端需要描述符集来绑定字体纹理等资源。
 * 配置 16 个 COMBINED_IMAGE_SAMPLER 描述符，足够 ImGui 使用。
 * 启用 FREE_DESCRIPTOR_SET_BIT 允许单独释放描述符集。
 */
bool VulkanBackend::CreateDescriptorPool()
{
    VkDescriptorPoolSize poolSize{};
    poolSize.type            = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    poolSize.descriptorCount = 1024;

    VkDescriptorPoolCreateInfo dpci{};
    dpci.sType         = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    dpci.flags         = VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT;
    dpci.maxSets       = 1024;
    dpci.poolSizeCount = 1;
    dpci.pPoolSizes    = &poolSize;

    VK_CHECK(vkCreateDescriptorPool(device_, &dpci, nullptr, &descriptorPool_));
    return true;
}

// ===========================================================================
// 12. ImGui Vulkan backend —— 初始化 ImGui 渲染后端
// ===========================================================================

/**
 * @brief 初始化 ImGui Vulkan 渲染后端
 *
 * 将已创建的 Vulkan 对象传递给 ImGui，使其能够：
 *   - 创建内部管线、着色器
 *   - 上传字体纹理
 *   - 每帧录制 ImGui 绘制命令
 *
 * 调用前需确保 ImGui::CreateContext() 已完成。
 */
bool VulkanBackend::InitImGuiVulkan()
{
    ImGui_ImplVulkan_InitInfo info{};
    info.ApiVersion      = VK_API_VERSION_1_0;
    info.Instance        = instance_;
    info.PhysicalDevice  = physicalDevice_;
    info.Device          = device_;
    info.QueueFamily     = queueFamily_;
    info.Queue           = graphicsQueue_;
    info.DescriptorPool  = descriptorPool_;
    info.RenderPass      = renderPass_;
    info.MinImageCount   = imageCount_;
    info.ImageCount      = imageCount_;
    info.MSAASamples     = VK_SAMPLE_COUNT_1_BIT;
    info.PipelineCache   = VK_NULL_HANDLE;
    info.Subpass         = 0;
    info.CheckVkResultFn = CheckVkResult;

    if (!ImGui_ImplVulkan_Init(&info))
    {
        LOGE("[VulkanBackend] ImGui_ImplVulkan_Init failed");
        return false;
    }

    LOGI("[VulkanBackend] ImGui Vulkan backend initialized");
    return true;
}

// ---------------------------------------------------------------------------
// ===========================================================================
// Swapchain cleanup —— 清理交换链相关资源
// ===========================================================================

/**
 * @brief 销毁交换链及其关联资源
 *
 * 清理顺序：帧缓冲 → 图像视图 → 渲染通道 → 交换链。
 * 在 Shutdown 或重建交换链时调用。
 */
void VulkanBackend::CleanupSwapchain()
{
    for (auto fb : framebuffers_)
        if (fb) vkDestroyFramebuffer(device_, fb, nullptr);
    framebuffers_.clear();

    for (auto view : swapchainImageViews_)
        if (view) vkDestroyImageView(device_, view, nullptr);
    swapchainImageViews_.clear();

    swapchainImages_.clear();

    if (renderPass_) { vkDestroyRenderPass(device_, renderPass_, nullptr); renderPass_ = VK_NULL_HANDLE; }
    if (swapchain_)  { vkDestroySwapchainKHR(device_, swapchain_, nullptr); swapchain_ = VK_NULL_HANDLE; }
}
