#version 430 core

// A glyph quad per instance, the 4 corners of a triangle strip come from gl_VertexID

layout(location = 0) in vec4 rect;		// x, y of the top left corner, width, height: pixels from the top left
layout(location = 1) in float glyph;	// glyph index in the atlas
layout(location = 2) in vec4 color;

layout(location = 0) uniform vec2 screenSize;

out vec2 texel;		// position in the font atlas, texels
out vec4 glyphColor;

#define GLYPH_SIZE 8.0	// Font8x8.SIZE

void main()
{
	vec2 corner = vec2(gl_VertexID & 1, (gl_VertexID >> 1) & 1);
	vec2 pos = rect.xy + corner * rect.zw;

	gl_Position = vec4(
		pos.x / screenSize.x * 2.0 - 1.0,
		1.0 - pos.y / screenSize.y * 2.0,
		0.0,
		1.0);

	// the whole glyph cell is stretched over the quad
	texel = vec2(glyph + corner.x, corner.y) * GLYPH_SIZE;
	glyphColor = color;
}
