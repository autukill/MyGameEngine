#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in vec4 aColor;

out vec2 Frag_TexCoord;
out vec4 Frag_Color;
uniform mat4 uProjection;

void main() {
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    Frag_TexCoord = aTexCoord;
    Frag_Color = aColor;
}
