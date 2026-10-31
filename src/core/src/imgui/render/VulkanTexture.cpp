#include "VulkanTexture.h"

#include <atomic>
#include <cstring>
#include <mutex>
#include <unordered_map>
#include <vector>

#include "../backends/imgui_impl_vulkan.h"
#include "Logger.h"

namespace VulkanTexture
{

namespace
{

struct TextureRecord
{
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkImageView view = VK_NULL_HANDLE;
    VkSampler sampler = VK_NULL_HANDLE;
};

struct Context
{
    VkDevice device = VK_NULL_HANDLE;
    VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;
    VkQueue queue = VK_NULL_HANDLE;
    uint32_t queueFamily = 0;
    VkCommandPool commandPool = VK_NULL_HANDLE;
    VkDescriptorPool descriptorPool = VK_NULL_HANDLE;
    bool ready = false;
};

Context g;
std::mutex g_Mutex;
std::unordered_map<VkDescriptorSet, TextureRecord> g_Textures;
std::atomic<uint64_t> g_Epoch{0};

VkPhysicalDeviceMemoryProperties GetMemProps()
{
    VkPhysicalDeviceMemoryProperties props{};
    if (!g.physicalDevice)
        return props;
    vkGetPhysicalDeviceMemoryProperties(g.physicalDevice, &props);
    return props;
}

uint32_t FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags properties)
{
    const VkPhysicalDeviceMemoryProperties props = GetMemProps();
    for (uint32_t i = 0; i < props.memoryTypeCount; ++i)
    {
        if ((typeFilter & (1u << i)) && (props.memoryTypes[i].propertyFlags & properties) == properties)
            return i;
    }
    return UINT32_MAX;
}

bool CreateBuffer(VkDeviceSize size, VkBufferUsageFlags usage,
                  VkMemoryPropertyFlags properties, VkBuffer& outBuffer,
                  VkDeviceMemory& outMemory)
{
    VkBufferCreateInfo ci{};
    ci.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    ci.size = size;
    ci.usage = usage;
    ci.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (vkCreateBuffer(g.device, &ci, nullptr, &outBuffer) != VK_SUCCESS)
        return false;

    VkMemoryRequirements req{};
    vkGetBufferMemoryRequirements(g.device, outBuffer, &req);
    const uint32_t typeIdx = FindMemoryType(req.memoryTypeBits, properties);
    if (typeIdx == UINT32_MAX)
    {
        vkDestroyBuffer(g.device, outBuffer, nullptr);
        outBuffer = VK_NULL_HANDLE;
        return false;
    }

    VkMemoryAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    ai.allocationSize = req.size;
    ai.memoryTypeIndex = typeIdx;
    if (vkAllocateMemory(g.device, &ai, nullptr, &outMemory) != VK_SUCCESS)
    {
        vkDestroyBuffer(g.device, outBuffer, nullptr);
        outBuffer = VK_NULL_HANDLE;
        return false;
    }

    vkBindBufferMemory(g.device, outBuffer, outMemory, 0);
    return true;
}

bool CreateImage(VkDeviceSize size, uint32_t w, uint32_t h,
                 VkMemoryPropertyFlags properties, VkImage& outImage,
                 VkDeviceMemory& outMemory)
{
    VkImageCreateInfo ci{};
    ci.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ci.imageType = VK_IMAGE_TYPE_2D;
    ci.format = VK_FORMAT_R8G8B8A8_UNORM;
    ci.extent = {w, h, 1};
    ci.mipLevels = 1;
    ci.arrayLayers = 1;
    ci.samples = VK_SAMPLE_COUNT_1_BIT;
    ci.tiling = VK_IMAGE_TILING_OPTIMAL;
    ci.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
    ci.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    ci.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (vkCreateImage(g.device, &ci, nullptr, &outImage) != VK_SUCCESS)
        return false;

    VkMemoryRequirements req{};
    vkGetImageMemoryRequirements(g.device, outImage, &req);
    const uint32_t typeIdx = FindMemoryType(req.memoryTypeBits, properties);
    if (typeIdx == UINT32_MAX)
    {
        vkDestroyImage(g.device, outImage, nullptr);
        outImage = VK_NULL_HANDLE;
        return false;
    }

    VkMemoryAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    ai.allocationSize = req.size;
    ai.memoryTypeIndex = typeIdx;
    if (vkAllocateMemory(g.device, &ai, nullptr, &outMemory) != VK_SUCCESS)
    {
        vkDestroyImage(g.device, outImage, nullptr);
        outImage = VK_NULL_HANDLE;
        return false;
    }

    vkBindImageMemory(g.device, outImage, outMemory, 0);
    return true;
}

// Copy staging buffer -> image, transitioning layout to SHADER_READ_ONLY_OPTIMAL.
bool CopyBufferToImage(VkBuffer src, VkImage dst, uint32_t w, uint32_t h)
{
    VkCommandBufferAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    ai.commandPool = g.commandPool;
    ai.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    ai.commandBufferCount = 1;

    VkCommandBuffer cmd = VK_NULL_HANDLE;
    if (vkAllocateCommandBuffers(g.device, &ai, &cmd) != VK_SUCCESS)
        return false;

    VkCommandBufferBeginInfo bi{};
    bi.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    bi.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (vkBeginCommandBuffer(cmd, &bi) != VK_SUCCESS)
    {
        vkFreeCommandBuffers(g.device, g.commandPool, 1, &cmd);
        return false;
    }

    const VkImageSubresourceRange range{ VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };

    // UNDEFINED -> TRANSFER_DST_OPTIMAL
    VkImageMemoryBarrier barrier{};
    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    barrier.srcAccessMask = 0;
    barrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    barrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    barrier.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.image = dst;
    barrier.subresourceRange = range;
    vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT,
                         0, 0, nullptr, 0, nullptr, 1, &barrier);

    VkBufferImageCopy region{};
    region.bufferOffset = 0;
    region.bufferRowLength = 0;
    region.bufferImageHeight = 0;
    region.imageSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 };
    region.imageOffset = { 0, 0, 0 };
    region.imageExtent = { w, h, 1 };
    vkCmdCopyBufferToImage(cmd, src, dst, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);

    // TRANSFER_DST_OPTIMAL -> SHADER_READ_ONLY_OPTIMAL
    barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    barrier.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    barrier.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    vkCmdPipelineBarrier(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                         0, 0, nullptr, 0, nullptr, 1, &barrier);

    if (vkEndCommandBuffer(cmd) != VK_SUCCESS)
    {
        vkFreeCommandBuffers(g.device, g.commandPool, 1, &cmd);
        return false;
    }

    VkSubmitInfo si{};
    si.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    si.commandBufferCount = 1;
    si.pCommandBuffers = &cmd;
    const VkResult submit = vkQueueSubmit(g.queue, 1, &si, VK_NULL_HANDLE);
    vkDeviceWaitIdle(g.device);   // Simple, bounded by the per-frame icon budget.

    vkFreeCommandBuffers(g.device, g.commandPool, 1, &cmd);
    return submit == VK_SUCCESS;
}

bool CreateSampler(VkSampler& sampler)
{
    VkSamplerCreateInfo ci{};
    ci.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    ci.magFilter = VK_FILTER_LINEAR;
    ci.minFilter = VK_FILTER_LINEAR;
    ci.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
    ci.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    ci.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    ci.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    ci.mipLodBias = 0.0f;
    ci.anisotropyEnable = VK_FALSE;
    ci.maxAnisotropy = 1.0f;
    ci.compareEnable = VK_FALSE;
    ci.minLod = 0.0f;
    ci.maxLod = VK_LOD_CLAMP_NONE;
    ci.borderColor = VK_BORDER_COLOR_FLOAT_TRANSPARENT_BLACK;
    ci.unnormalizedCoordinates = VK_FALSE;
    return vkCreateSampler(g.device, &ci, nullptr, &sampler) == VK_SUCCESS;
}

bool CreateImageView(VkImage image, VkImageView& view)
{
    VkImageViewCreateInfo ci{};
    ci.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    ci.image = image;
    ci.viewType = VK_IMAGE_VIEW_TYPE_2D;
    ci.format = VK_FORMAT_R8G8B8A8_UNORM;
    ci.components = {
        VK_COMPONENT_SWIZZLE_R, VK_COMPONENT_SWIZZLE_G,
        VK_COMPONENT_SWIZZLE_B, VK_COMPONENT_SWIZZLE_A };
    ci.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
    return vkCreateImageView(g.device, &ci, nullptr, &view) == VK_SUCCESS;
}

void DestroyRecord(const TextureRecord& r)
{
    if (!g.device)
        return;
    if (r.view) vkDestroyImageView(g.device, r.view, nullptr);
    if (r.sampler) vkDestroySampler(g.device, r.sampler, nullptr);
    if (r.image) vkDestroyImage(g.device, r.image, nullptr);
    if (r.memory) vkFreeMemory(g.device, r.memory, nullptr);
}

} // namespace

void SetContext(VkDevice device, VkPhysicalDevice physicalDevice, VkQueue queue,
                uint32_t queueFamily, VkCommandPool commandPool, VkDescriptorPool descriptorPool)
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    g.device = device;
    g.physicalDevice = physicalDevice;
    g.queue = queue;
    g.queueFamily = queueFamily;
    g.commandPool = commandPool;
    g.descriptorPool = descriptorPool;
    g.ready = device != VK_NULL_HANDLE && queue != VK_NULL_HANDLE &&
              commandPool != VK_NULL_HANDLE && descriptorPool != VK_NULL_HANDLE;
    g_Epoch.fetch_add(1, std::memory_order_release);   // new context: stale caches must reload
}

void ClearContext()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    g = Context{};
    g_Epoch.fetch_add(1, std::memory_order_release);
}

uint64_t GetEpoch()
{
    return g_Epoch.load(std::memory_order_acquire);
}

bool IsReady()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    return g.ready;
}

ImTextureID CreateTexture(const std::uint8_t* rgba, int width, int height)
{
    if (!rgba || width <= 0 || height <= 0)
        return 0;

    TextureRecord rec;
    bool ok = false;
    const VkDeviceSize size = static_cast<VkDeviceSize>(width) * height * 4;

    {
        std::lock_guard<std::mutex> lock(g_Mutex);
        if (!g.ready)
            return 0;

        VkBuffer staging = VK_NULL_HANDLE;
        VkDeviceMemory stagingMem = VK_NULL_HANDLE;
        VkImage image = VK_NULL_HANDLE;
        VkDeviceMemory imageMem = VK_NULL_HANDLE;

        if (!CreateBuffer(size, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                          VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT |
                          VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                          staging, stagingMem))
            return 0;

        void* mapped = nullptr;
        if (vkMapMemory(g.device, stagingMem, 0, size, 0, &mapped) == VK_SUCCESS)
        {
            std::memcpy(mapped, rgba, static_cast<size_t>(size));
            vkUnmapMemory(g.device, stagingMem);
        }

        if (!CreateImage(size, static_cast<uint32_t>(width), static_cast<uint32_t>(height),
                         VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, image, imageMem))
        {
            vkDestroyBuffer(g.device, staging, nullptr);
            vkFreeMemory(g.device, stagingMem, nullptr);
            return 0;
        }

        if (!CopyBufferToImage(staging, image, static_cast<uint32_t>(width), static_cast<uint32_t>(height)))
        {
            vkDestroyImage(g.device, image, nullptr);
            vkFreeMemory(g.device, imageMem, nullptr);
            vkDestroyBuffer(g.device, staging, nullptr);
            vkFreeMemory(g.device, stagingMem, nullptr);
            return 0;
        }

        vkDestroyBuffer(g.device, staging, nullptr);
        vkFreeMemory(g.device, stagingMem, nullptr);

        if (!CreateSampler(rec.sampler) || !CreateImageView(image, rec.view))
        {
            DestroyRecord(rec);
            vkDestroyImage(g.device, image, nullptr);
            vkFreeMemory(g.device, imageMem, nullptr);
            return 0;
        }

        rec.image = image;
        rec.memory = imageMem;

        // Note: descriptorPool_ is used by imgui_impl_vulkan via AddTexture.
        const VkDescriptorSet ds = ImGui_ImplVulkan_AddTexture(
            rec.sampler, rec.view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
        if (!ds)
        {
            DestroyRecord(rec);
            return 0;
        }

        g_Textures[ds] = rec;
        ok = true;
        return (ImTextureID)(uintptr_t)ds;
    }
}

void DestroyTexture(ImTextureID id)
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    if (!id || !g.device)
        return;
    const auto it = g_Textures.find((VkDescriptorSet)(uintptr_t)id);
    if (it == g_Textures.end())
        return;
    ImGui_ImplVulkan_RemoveTexture(it->first);
    DestroyRecord(it->second);
    g_Textures.erase(it);
}

void DestroyAll()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    for (auto& [ds, rec] : g_Textures)
    {
        ImGui_ImplVulkan_RemoveTexture(ds);
        DestroyRecord(rec);
    }
    g_Textures.clear();
}

} // namespace VulkanTexture
