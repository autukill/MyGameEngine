#version 330 core
in vec2 Frag_TexCoord;
in vec4 Frag_Color;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uGain;

void main() {
    FragColor = texture(uTexture, Frag_TexCoord) * Frag_Color * uGain;
}
