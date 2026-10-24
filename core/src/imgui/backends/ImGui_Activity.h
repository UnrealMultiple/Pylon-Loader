#pragma once

#include <cstdint>

#include <jni.h>

namespace AndroidPlatform
{

jobject GetActivity();
uint64_t GetActivityGeneration();

}
