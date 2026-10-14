#version 330 core

uniform sampler2D atlas;	// glyphs in a row, 1 where a pixel is set

in vec2 texel;
in vec4 glyphColor;

out vec4 fragColor;

void main()
{
	if (texelFetch(atlas, ivec2(texel), 0).r < 0.5)
		discard;

	fragColor = glyphColor;
}
