#pragma once

#include <cstddef>
#include <cstdint>

#include "imgui.h"

#ifndef VK_USE_PLATFORM_ANDROID_KHR
#define VK_USE_PLATFORM_ANDROID_KHR
#endif
#include <vulkan/vulkan.h>

// Small helper that turns RGBA8 pixels into a Vulkan texture usable by the Vulkan ImGui
// backend (ImGui_ImplVulkan_AddTexture). Handles the staging buffer -> image copy and a
// one-time layout transition. Used by the Terraria item/buff icon grids (which originally
// uploaded GLES textures that the Vulkan backend cannot display).
namespace VulkanTexture
{
    // Called by VulkanBackend once the device/queue/pool are ready.
    void SetContext(VkDevice device, VkPhysicalDevice physicalDevice, VkQueue queue,
                    uint32_t queueFamily, VkCommandPool commandPool, VkDescriptorPool descriptorPool);
    void ClearContext();

    // Increments every time the Vulkan context is (re)created or torn down. Consumers cache
    // textures keyed by this epoch so they can invalidate stale ImTextureIDs after a Vulkan
    // surface/context rebuild (which destroys all previously created textures).
    uint64_t GetEpoch();

    // Returns an ImTextureID (a VkDescriptorSet) ready for ImGui::Image, or 0 on failure.
    ImTextureID CreateTexture(const std::uint8_t* rgba, int width, int height);

    // Destroys a texture and releases all its resources. Pass 0 safely.
    void DestroyTexture(ImTextureID id);

    // Releases all textures (called on backend shutdown).
    void DestroyAll();

    bool IsReady();
}
