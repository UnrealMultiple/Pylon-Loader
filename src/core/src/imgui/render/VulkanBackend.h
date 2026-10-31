#pragma once

#include "IGraphicsBackend.h"

#ifndef VK_USE_PLATFORM_ANDROID_KHR
#define VK_USE_PLATFORM_ANDROID_KHR
#endif
#include <vulkan/vulkan.h>

#include <vector>

class VulkanBackend : public IGraphicsBackend
{
public:
    bool Init(ANativeWindow* window, int width, int height) override;
    void BeginFrame() override;
    void EndFrame() override;
    void Shutdown() override;
    bool IsReady() const override;
    bool RequiresSurfaceRebuild() const;
    uint64_t PresentedFrameCount() const;

private:
    enum class RecoveryScope
    {
        None,
        Backend,
        Surface,
    };

    bool CreateInstance();
    bool CreateSurface(ANativeWindow* window);
    bool SelectPhysicalDevice();
    bool CreateLogicalDevice();
    bool CreateSwapchain(int width, int height);
    bool CreateRenderPass();
    bool CreateFramebuffers();
    bool CreateCommandPool();
    bool CreateCommandBuffers();
    bool CreateSyncObjects();
    bool CreateDescriptorPool();
    bool InitImGuiVulkan();

    void CleanupSwapchain();
    void RequestRecovery(VkResult result);

    VkInstance instance_ = VK_NULL_HANDLE;
    VkPhysicalDevice physicalDevice_ = VK_NULL_HANDLE;
    VkDevice device_ = VK_NULL_HANDLE;
    VkQueue graphicsQueue_ = VK_NULL_HANDLE;
    uint32_t queueFamily_ = UINT32_MAX;
    VkSurfaceKHR surface_ = VK_NULL_HANDLE;
    VkSwapchainKHR swapchain_ = VK_NULL_HANDLE;
    VkRenderPass renderPass_ = VK_NULL_HANDLE;
    VkCommandPool commandPool_ = VK_NULL_HANDLE;
    VkDescriptorPool descriptorPool_ = VK_NULL_HANDLE;

    std::vector<VkImage> swapchainImages_;
    std::vector<VkImageView> swapchainImageViews_;
    std::vector<VkFramebuffer> framebuffers_;
    std::vector<VkCommandBuffer> commandBuffers_;

    std::vector<VkFence> inFlightFences_;
    std::vector<VkSemaphore> imageAvailableSemaphores_;
    std::vector<VkSemaphore> renderFinishedSemaphores_;

    VkFormat swapchainFormat_ = VK_FORMAT_UNDEFINED;
    VkExtent2D swapchainExtent_ = {};
    uint32_t imageCount_ = 0;

    uint32_t semaphoreIndex_ = 0;
    uint32_t semaphoreCount_ = 0;
    uint32_t acquiredImageIndex_ = 0;
    int width_ = 0;
    int height_ = 0;
    bool initialized_ = false;
    bool frameStarted_ = false;
    uint64_t presentedFrameCount_ = 0;
    RecoveryScope recoveryScope_ = RecoveryScope::None;
};
