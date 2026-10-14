#version 330 core

uniform float iGlobalTime;
uniform vec3 iResolution;
uniform vec3 ro;	// camera ray origin
uniform mat3 camProj;	// camera projection matrix

// Compiled scene program, uploaded by Render from SceneState (see Scene/SceneOpcodes.cs).
// g_map[0] = (programEnd, materialBase, 0, 0), instructions in [1, programEnd),
// material colors (rgb) start at materialBase.
uniform SdElements
{
	vec4 g_map[1024];	// Const.UBO_SDELEMENTSMAP_BLOCKCOUNT
};

in vec2 fragCoord;
out vec4 fragColor;

#define STACK_DEPTH 8	// Const.SCENE_STACK_DEPTH
#define EMPTY_DISTANCE 1e10

// opcodes, keep in sync with Scene/SceneOpcodes.cs
#define OP_SPHERE		1
#define OP_BOX			2
#define OP_PLANE		3
#define OP_CYLINDER		4
#define OP_TORUS		5
#define OP_TRANSLATE	10
#define OP_REPEAT		11
#define OP_ROTATE		12
#define OP_SCALE		13
#define OP_POPPOS		14
#define OP_UNION		20
#define OP_SUBTRACT		21
#define OP_INTERSECT	22
#define OP_SMOOTH		23

float sdSphere(vec3 p, float s)
{
	return length(p) - s;
}

float sdBox(vec3 p, vec3 b)
{
	vec3 d = abs(p) - b;
	return min(max(d.x, max(d.y, d.z)), 0.0) + length(max(d, 0.0));
}

float sdPlane(vec3 p, vec3 n, float dist)
{
	return dot(p, n) - dist;
}

float sdCylinder(vec3 p, float r, float halfHeight)
{
	return max(length(p.xz) - r, abs(p.y) - halfHeight);
}

float sdTorus(vec3 p, float bigR, float smallR)
{
	return length(vec2(length(p.xz) - bigR, p.y)) - smallR;
}

//----------------------------------------------------------------------

// mod(p, c) - c/2 per axis, an axis with zero period is left as is
vec3 opRep(vec3 p, vec3 c)
{
	bvec3 repeated = notEqual(c, vec3(0.0));
	vec3 safeC = mix(vec3(1.0), c, repeated);
	return mix(p, mod(p, safeC) - 0.5 * safeC, repeated);
}

vec2 opSmooth(vec2 a, vec2 b, float k)
{
	if (k <= 0.0)
		return a.x < b.x ? a : b;

	float h = clamp(0.5 + 0.5 * (b.x - a.x) / k, 0.0, 1.0);
	float d = b.x + (a.x - b.x) * h - k * h * (1.0 - h);
	return vec2(d, h > 0.5 ? a.y : b.y);
}

//----------------------------------------------------------------------

// Scene program interpreter, mirror of Scene/SceneVM.cs.
// Returns (distance, material id).
vec2 map(in vec3 pos)
{
	vec4 ps[STACK_DEPTH];	// xyz position, w scale
	vec2 ds[STACK_DEPTH];	// x distance, y material
	int pp = 0;
	int dp = 0;
	ps[0] = vec4(pos, 1.0);

	int end = int(g_map[0].x);
	int pc = 1;

	while (pc < end)
	{
		vec4 h = g_map[pc];
		int op = int(h.x);
		vec3 p = ps[pp].xyz;
		float s = ps[pp].w;

		if (op == OP_SPHERE)
		{
			ds[dp++] = vec2(sdSphere(p, h.y) * s, h.z);
			pc += 1;
		}
		else if (op == OP_BOX)
		{
			ds[dp++] = vec2(sdBox(p, g_map[pc + 1].xyz) * s, h.z);
			pc += 2;
		}
		else if (op == OP_PLANE)
		{
			ds[dp++] = vec2(sdPlane(p, g_map[pc + 1].xyz, h.y) * s, h.z);
			pc += 2;
		}
		else if (op == OP_CYLINDER)
		{
			ds[dp++] = vec2(sdCylinder(p, h.y, h.w) * s, h.z);
			pc += 1;
		}
		else if (op == OP_TORUS)
		{
			ds[dp++] = vec2(sdTorus(p, h.y, h.w) * s, h.z);
			pc += 1;
		}
		else if (op == OP_TRANSLATE)
		{
			ps[++pp] = vec4(p - h.yzw, s);
			pc += 1;
		}
		else if (op == OP_REPEAT)
		{
			ps[++pp] = vec4(opRep(p, h.yzw), s);
			pc += 1;
		}
		else if (op == OP_ROTATE)
		{
			ps[++pp] = vec4(
				dot(g_map[pc + 1].xyz, p),
				dot(g_map[pc + 2].xyz, p),
				dot(g_map[pc + 3].xyz, p),
				s);
			pc += 4;
		}
		else if (op == OP_SCALE)
		{
			ps[++pp] = vec4(p / h.y, s * h.y);
			pc += 1;
		}
		else if (op == OP_POPPOS)
		{
			pp--;
			pc += 1;
		}
		else
		{
			vec2 b = ds[--dp];
			vec2 a = ds[dp - 1];

			if (op == OP_UNION)
				ds[dp - 1] = a.x < b.x ? a : b;
			else if (op == OP_SUBTRACT)
				ds[dp - 1] = vec2(max(a.x, -b.x), a.y);
			else if (op == OP_INTERSECT)
				ds[dp - 1] = a.x > b.x ? a : b;
			else
				ds[dp - 1] = opSmooth(a, b, h.y);

			pc += 1;
		}
	}

	return dp > 0 ? ds[0] : vec2(EMPTY_DISTANCE, 0.0);
}

vec3 materialColor(float id)
{
	return g_map[int(g_map[0].y) + int(id)].rgb;
}

vec2 castRay(in vec3 ro, in vec3 rd)
{
	//TODO move to external constants w/ uniq names
	const float MAX_DIST = 100;
	const float MIN_DIST = 0.0002;
	const int MAX_RAY_STEPS = 100;

	float t = 0.0;
	vec2 h = vec2(1.0);
	float overstep = 0.0;
	float phx = MAX_DIST;

	int i = 0;
	while (i < MAX_RAY_STEPS && t < MAX_DIST)
	{
		h = map(ro + rd * t);

		if (h.x > overstep)
		{
			overstep = h.x * min(1.0, 0.5 * h.x / phx);
			t += h.x * 0.5 + overstep;
			phx = h.x;
			i++;
		}
		else
		{
			t -= overstep;
			phx = MAX_DIST;
			h.x = 1.0;
			overstep = 0.0;
		}

		if (h.x < MIN_DIST || t > MAX_DIST)
			break;
	}

	return vec2(t, h.y);
}

// TODO generalize over map() it similar
float softshadow(in vec3 ro, in vec3 rd)
{
	const float INIT_T = 0.02;
	const float INIT_RES = 0.1;
	const float MAX_DIST = 25;
	const float MIN_DIST = 0.001;
	const int MAX_RAY_STEPS = 256;		// higher -> longer shadow distance
	const float SHADOW_SMOOTH = 8.0;	// lower ~ smother, higher -> sharper

	float res = 1.0;
	float t = INIT_T;
	for (int i = 0; i < MAX_RAY_STEPS; i++)
	{
		float h = map(ro + rd * t).x;
		res = min(res, SHADOW_SMOOTH * h / t);
		t += clamp(h, INIT_T, INIT_RES);
		if (h < MIN_DIST || t > MAX_DIST) break;
	}

	return clamp(res, 0.0, 1.0);

}

vec3 calcNormal(in vec3 pos)
{
	vec3 eps = vec3(0.001, 0.0, 0.0);
	vec3 nor = vec3(
		map(pos + eps.xyy).x - map(pos - eps.xyy).x,
		map(pos + eps.yxy).x - map(pos - eps.yxy).x,
		map(pos + eps.yyx).x - map(pos - eps.yyx).x);
	return normalize(nor);
}

vec3 render(in vec3 ro, in vec3 rd)
{
	vec2 res = castRay(ro, rd);
	vec3 col = materialColor(res.y);
	float t = res.x;
	vec3 pos = ro + t * rd;
	vec3 nor = calcNormal(pos);
	vec3 ref = reflect(rd, nor);

	// lighitng
	vec3  lig = normalize(vec3(cos(iGlobalTime *0.1), abs(sin(iGlobalTime *0.1)), cos(iGlobalTime *0.1) * sin(iGlobalTime *0.1)));
	//vec3  lig = normalize(vec3(-0.6, 0.7, -0.5));
	float amb = clamp(0.5 + 0.5 * nor.y, 0.0, 1.0);
	float dif = clamp(dot(nor, lig), 0.0, 1.0);
	float spe = pow(clamp(dot(ref, lig), 0.0, 1.0), 16.0);

	dif *= softshadow(pos, lig);

	vec3 lin = vec3(0.0);
	lin += dif;
	lin += 1.20 * spe *dif;
	lin += 0.20 * amb;
	col *= lin;

	col = mix(col, vec3(0.8, 0.9, 1.0), 1.0 - exp(-0.002 * t * t));	// distance fog
	
	return vec3(clamp(col, 0.0, 1.0));
}

void main(void)
{
	// ray direction
	vec2 q = fragCoord.xy / iResolution.xy;
	vec2 p = -1.0 + 2.0 * q;
	p.x *= iResolution.x / iResolution.y;
	vec3 rd = camProj * normalize(vec3(p.xy, 2.0));

	vec3 col = render(ro, rd);

	col = pow(col, vec3(0.8545)); // tint
	fragColor = vec4(col, 1.0);
}