// 层：内核层 / graphics::fx —— 编译实现
#include "graphics/fx/Shaders.h"

#include <memory>
#include <mutex>
#include <vector>

#include "graphics/GlState.h"
#include "graphics/fx/Frame.h"
#include "graphics/fx/ShaderLibrary.h"
#include "pylon/Log.h"

namespace pylon::graphics::fx {

namespace {

struct Entry {
    std::string name;
    std::string source;
    bool mesh = false;
    ShaderState state = ShaderState::Pending;
    std::string error;
    Program program;  // 只有渲染线程碰
};

std::mutex g_mutex;
std::vector<std::unique_ptr<Entry>> g_entries;  // id = 下标 + 1
GLuint g_vertex = 0;
GLuint g_meshVertex = 0;
bool g_vertexTried = false;
bool g_meshVertexTried = false;

GLuint compile(GLenum type, const std::string& source, std::string& error) {
    const Gles& g = gles();
    const GLuint shader = g.CreateShader(type);
    const char* text = source.c_str();
    g.ShaderSource(shader, 1, &text, nullptr);
    g.CompileShader(shader);
    GLint ok = 0;
    g.GetShaderiv(shader, GL_COMPILE_STATUS, &ok);
    if (ok) return shader;
    char log[2048] = {0};
    g.GetShaderInfoLog(shader, sizeof log - 1, nullptr, log);
    error = log;
    g.DeleteShader(shader);
    return 0;
}

GLuint vertexShader() {
    if (g_vertexTried) return g_vertex;
    g_vertexTried = true;
    std::string error;
    g_vertex = compile(GL_VERTEX_SHADER, ShaderLibrary::Vertex(), error);
    if (g_vertex == 0) LOGE("fx", "全屏四边形顶点着色器编译失败：%s", error.c_str());
    return g_vertex;
}

GLuint meshVertexShader() {
    if (g_meshVertexTried) return g_meshVertex;
    g_meshVertexTried = true;
    std::string error;
    g_meshVertex = compile(GL_VERTEX_SHADER, ShaderLibrary::MeshVertex(), error);
    if (g_meshVertex == 0) LOGE("fx", "几何顶点着色器编译失败：%s", error.c_str());
    return g_meshVertex;
}

void markFailed(Entry& entry, const std::string& error) {
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        entry.state = ShaderState::Failed;
        entry.error = error;
    }
    LOGE("fx", "着色器 '%s' 编译失败：\n%s", entry.name.c_str(), error.c_str());
}

bool build(Entry& entry) {
    const Gles& g = gles();
    std::string error;
    const std::string fragment = entry.mesh ? ShaderLibrary::BuildFragmentMesh(entry.source, error)
                                            : ShaderLibrary::BuildFragment(entry.source, error);
    if (fragment.empty()) {
        markFailed(entry, error);
        return false;
    }
    std::string compileLog;
    const GLuint vs = entry.mesh ? meshVertexShader() : vertexShader();
    const GLuint fs = compile(GL_FRAGMENT_SHADER, fragment, compileLog);
    if (vs == 0 || fs == 0) {
        markFailed(entry, ShaderLibrary::Describe(compileLog, entry.name, entry.source));
        return false;
    }

    ScopedState saved;
    const GLuint id = g.CreateProgram();
    g.AttachShader(id, vs);
    g.AttachShader(id, fs);
    g.LinkProgram(id);
    g.DeleteShader(fs);
    GLint linked = 0;
    g.GetProgramiv(id, GL_LINK_STATUS, &linked);
    if (!linked) {
        char log[2048] = {0};
        g.GetProgramInfoLog(id, sizeof log - 1, nullptr, log);
        g.DeleteProgram(id);
        markFailed(entry, ShaderLibrary::Describe(log, entry.name, entry.source));
        return false;
    }

    Program& program = entry.program;
    program.id = id;
    // Keep the legacy-safe default for unknown shaders. XihePost and other
    // screen effects contain explicit blScreen()/uScreen reads; geometry-only
    // effects do not. This avoids treating the shared prelude declaration as a
    // read dependency while retaining compatibility for external mods.
    program.readsScreen = entry.source.find("blScreen(") != std::string::npos
                       || entry.source.find("uScreen") != std::string::npos;
    if (entry.mesh) program.readsScreen = false;
    auto loc = [&](const char* name) { return g.GetUniformLocation(id, name); };
    program.builtin = {loc("uBox"), loc("uScreen"), loc("uSource"), loc("uScreenTexel"), loc("uSourceTexel"),
                       loc("uView"), loc("uResolution"), loc("uAspect"), loc("uTime"), loc("uAge"),
                       loc("uProgress"), loc("uIntensity"), loc("uCenter"), loc("uRadius"), loc("uColor")};
    g.UseProgram(id);
    if (program.builtin.screen >= 0) g.Uniform1i(program.builtin.screen, kUnitScreen);
    if (program.builtin.source >= 0) g.Uniform1i(program.builtin.source, kUnitSource);

    std::lock_guard<std::mutex> lock(g_mutex);
    entry.state = ShaderState::Ready;
    return true;
}

}  // namespace

int ShaderStore::Register(const std::string& name, const std::string& source, bool mesh) {
    std::lock_guard<std::mutex> lock(g_mutex);
    for (size_t i = 0; i < g_entries.size(); ++i) {
        const Entry& e = *g_entries[i];
        if (e.name == name && e.source == source && e.mesh == mesh) return static_cast<int>(i + 1);
    }
    auto entry = std::make_unique<Entry>();
    entry->name = name;
    entry->source = source;
    entry->mesh = mesh;
    g_entries.push_back(std::move(entry));
    return static_cast<int>(g_entries.size());
}

ShaderState ShaderStore::State(int id, std::string* error) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (id <= 0 || id > static_cast<int>(g_entries.size())) return ShaderState::Failed;
    const Entry& e = *g_entries[id - 1];
    if (error != nullptr) *error = e.error;
    return e.state;
}

Program* ShaderStore::Use(int id) {
    Entry* entry = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (id <= 0 || id > static_cast<int>(g_entries.size())) return nullptr;
        entry = g_entries[id - 1].get();
        if (entry->state == ShaderState::Failed) return nullptr;
        if (entry->state == ShaderState::Ready) return &entry->program;
    }
    return build(*entry) ? &entry->program : nullptr;
}

GLint ShaderStore::Uniform(Program& program, const std::string& name) {
    auto it = program.custom.find(name);
    if (it != program.custom.end()) return it->second;
    const GLint location = gles().GetUniformLocation(program.id, name.c_str());
    program.custom.emplace(name, location);
    return location;
}

}  // namespace pylon::graphics::fx
