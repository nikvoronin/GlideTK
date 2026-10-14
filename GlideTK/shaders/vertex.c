#version 330 core

out vec2 fragCoord;
uniform vec3 iResolution;

void main()
{
	float x = -1.0 + float((gl_VertexID & 1) << 2);
	float y = -1.0 + float((gl_VertexID & 2) << 1);

	vec2 texCoord = vec2(
		(x + 1.0) * 0.5,
		(y + 1.0) * 0.5 );
	
	gl_Position = vec4(x, y, 0, 1);

	// shadertoy compatibility
	texCoord *= iResolution;
	fragCoord = texCoord;
}