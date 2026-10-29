// 层：内核层 / graphics —— 着色器注册与编译实现
#include "graphics/ShaderRegistry.h"
#include "graphics/GlState.h"
#include "graphics/ShaderLibrary.h"

#include <memory>
#include <mutex>
#include <vector>

#include "pylon/Log.h"

namespace pylon::graphics {

namespace {

struct Entry {
    std::string name;
    std::string source;
    bool mesh = false;
    ShaderState state = ShaderState::Pending;
    std::string error;
    CompiledShader compiled;  // 只有渲染线程碰
};

std::mutex g_mutex;
std::vector<std::unique_ptr<Entry>> g_entries;  // id = 下标 + 1
GLuint g_vertex = 0;
bool g_vertexTried = false;
GLuint g_meshVertex = 0;
bool g_meshVertexTried = false;

GLuint compile(GLenum type, const std::string& src, std::string& log) {
    auto& g = gl();
    GLuint s = g.CreateShader(type);
    const char* text = src.c_str();
    g.ShaderSource(s, 1, &text, nullptr);
    g.CompileShader(s);
    GLint ok = 0;
    g.GetShaderiv(s, GL_COMPILE_STATUS, &ok);
    if (ok) return s;
    char buf[2048] = {0};
    g.GetShaderInfoLog(s, sizeof buf - 1, nullptr, buf);
    log = buf;
    g.DeleteShader(s);
    return 0;
}

GLuint vertexShader() {
    if (g_vertexTried) return g_vertex;
    g_vertexTried = true;
    std::string log;
    g_vertex = compile(GL_VERTEX_SHADER, ShaderLibrary::Vertex(), log);
    if (!g_vertex) LOGE("fx", "公共顶点着色器编译失败：%s", log.c_str());
    return g_vertex;
}

GLuint meshVertexShader() {
    if (g_meshVertexTried) return g_meshVertex;
    g_meshVertexTried = true;
    std::string log;
    g_meshVertex = compile(GL_VERTEX_SHADER, ShaderLibrary::MeshVertex(), log);
    if (!g_meshVertex) LOGE("fx", "网格顶点着色器编译失败：%s", log.c_str());
    return g_meshVertex;
}

void fail(Entry& e, const std::string& error) {
    std::lock_guard<std::mutex> lock(g_mutex);
    e.state = ShaderState::Failed;
    e.error = error;
    LOGE("fx", "屏幕效果着色器 '%s' 编译失败：\n%s", e.name.c_str(), error.c_str());
}

bool build(Entry& e) {
    auto& g = gl();
    std::string error;
    const std::string fragment = e.mesh ? ShaderLibrary::BuildFragmentMesh(e.source, error)
                                        : ShaderLibrary::BuildFragment(e.source, error);
    if (fragment.empty()) {
        fail(e, error);
        return false;
    }
    const GLuint vs = e.mesh ? meshVertexShader() : vertexShader();
    std::string log;
    const GLuint fs = compile(GL_FRAGMENT_SHADER, fragment, log);
    if (!vs || !fs) {
        fail(e, ShaderLibrary::Describe(log, e.name, e.source));
        return false;
    }

    GlStateGuard saved;
    const GLuint program = g.CreateProgram();
    g.AttachShader(program, vs);
    g.AttachShader(program, fs);
    g.LinkProgram(program);
    g.DeleteShader(fs);
    GLint linked = 0;
    g.GetProgramiv(program, GL_LINK_STATUS, &linked);
    if (!linked) {
        char buf[2048] = {0};
        g.GetProgramInfoLog(program, sizeof buf - 1, nullptr, buf);
        fail(e, ShaderLibrary::Describe(buf, e.name, e.source));
        return false;
    }

    CompiledShader& c = e.compiled;
    c.program = program;
    auto loc = [&](const char* name) { return g.GetUniformLocation(program, name); };
    c.builtin = {loc("uBox"), loc("uScreen"), loc("uSource"), loc("uScreenTexel"), loc("uSourceTexel"),
                 loc("uView"), loc("uResolution"), loc("uAspect"), loc("uTime"), loc("uAge"),
                 loc("uProgress"), loc("uIntensity"), loc("uCenter"), loc("uRadius"), loc("uColor")};
    g.UseProgram(program);
    if (c.builtin.screen >= 0) g.Uniform1i(c.builtin.screen, static_cast<GLint>(kScreenUnit - GL_TEXTURE0));
    if (c.builtin.source >= 0) g.Uniform1i(c.builtin.source, static_cast<GLint>(kSourceUnit - GL_TEXTURE0));

    std::lock_guard<std::mutex> lock(g_mutex);
    e.state = ShaderState::Ready;
    return true;
}

}  // namespace

int ShaderRegistry::Register(const std::string& name, const std::string& source) {
    return registerImpl(name, source, false);
}

int ShaderRegistry::RegisterMesh(const std::string& name, const std::string& source) {
    return registerImpl(name, source, true);
}

int ShaderRegistry::registerImpl(const std::string& name, const std::string& source, bool mesh) {
    std::lock_guard<std::mutex> lock(g_mutex);
    for (size_t i = 0; i < g_entries.size(); ++i) {
        if (g_entries[i]->name == name && g_entries[i]->source == source && g_entries[i]->mesh == mesh)
            return static_cast<int>(i + 1);
    }
    auto e = std::make_unique<Entry>();
    e->name = name;
    e->source = source;
    e->mesh = mesh;
    g_entries.push_back(std::move(e));
    return static_cast<int>(g_entries.size());
}

ShaderState ShaderRegistry::Status(int id, std::string* error) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (id <= 0 || id > static_cast<int>(g_entries.size())) return ShaderState::Failed;
    const Entry& e = *g_entries[id - 1];
    if (error) *error = e.error;
    return e.state;
}

CompiledShader* ShaderRegistry::Use(int id) {
    Entry* e = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (id <= 0 || id > static_cast<int>(g_entries.size())) return nullptr;
        e = g_entries[id - 1].get();
        if (e->state == ShaderState::Failed) return nullptr;
        if (e->state == ShaderState::Ready) return &e->compiled;
    }
    return build(*e) ? &e->compiled : nullptr;
}

GLint ShaderRegistry::Uniform(CompiledShader& shader, const std::string& name) {
    auto it = shader.custom.find(name);
    if (it != shader.custom.end()) return it->second;
    const GLint loc = gl().GetUniformLocation(shader.program, name.c_str());
    shader.custom.emplace(name, loc);
    return loc;
}

}  // namespace pylon::graphics
