#version 430 core

layout(location = 0) uniform vec3 iResolution;
layout(location = 1) uniform vec3 ro;	// camera ray origin
layout(location = 2) uniform mat3 camProj;	// camera projection matrix
layout(location = 5) uniform int useBvh;	// 0: evaluate every leaf, to compare the speed
layout(location = 6) uniform vec3 sun;	// direction to the sun, from the scene (SceneEnvironment)
layout(location = 7) uniform vec3 fogColor;	// also the sky
layout(location = 8) uniform float fogDensity;
layout(location = 9) uniform vec3 sky;	// color of the rays that hit nothing
layout(location = 10) uniform vec4 light;	// ambient, specular, shininess, gamma
layout(location = 11) uniform vec2 shadows;	// sharpness, max distance (0: no shadows)
layout(location = 12) uniform vec3 march;	// max distance, max steps, max shadow steps
layout(location = 13) uniform int bounces;	// reflections deep (0..3), 0 turns them off

// Compiled scene program, uploaded by Render from SceneState (see Scene/SceneOpcodes.cs).
// g_map[0] = (materialBase, bvhStart, bvhNodeCount, alwaysCount),
// g_map[1 .. 1 + alwaysCount) = (start, end) of unbounded leaves, then leaf programs,
// BVH nodes (min.xyz, left | -start) (max.xyz, right | end), material colors (rgb).
// The leaf programs are not interpreted: SceneGlsl generates a function for every distinct
// instruction sequence (shape, its id is the w of the first instruction) at the marker line below,
// the functions read the arguments from g_map by constant offsets.
layout(std140, binding = 1) uniform SdElements
{
	vec4 g_map[1024];	// Const.UBO_SDELEMENTSMAP_BLOCKCOUNT
};

in vec2 fragCoord;
out vec4 fragColor;

#define BVH_STACK_DEPTH 32	// Const.BVH_STACK_DEPTH
#define EMPTY_DISTANCE 1e10

//----------------------------------------------------------------------
// Signed distance functions, https://iquilezles.org/articles/distfunctions/
// Mirror of Scene/Sdf.cs

float dot2(vec2 v) { return dot(v, v); }
float dot2(vec3 v) { return dot(v, v); }
float ndot(vec2 a, vec2 b) { return a.x * b.x - a.y * b.y; }

float sdSphere(vec3 p, float r)
{
	return length(p) - r;
}

float sdBox(vec3 p, vec3 b)
{
	vec3 d = abs(p) - b;
	return min(max(d.x, max(d.y, d.z)), 0.0) + length(max(d, 0.0));
}

float sdRoundBox(vec3 p, vec3 b, float r)
{
	vec3 q = abs(p) - b + r;
	return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - r;
}

float sdBoxFrame(vec3 p, vec3 b, float e)
{
	p = abs(p) - b;
	vec3 q = abs(p + e) - e;
	return min(min(
		length(max(vec3(p.x, q.y, q.z), 0.0)) + min(max(p.x, max(q.y, q.z)), 0.0),
		length(max(vec3(q.x, p.y, q.z), 0.0)) + min(max(q.x, max(p.y, q.z)), 0.0)),
		length(max(vec3(q.x, q.y, p.z), 0.0)) + min(max(q.x, max(q.y, p.z)), 0.0));
}

float sdPlane(vec3 p, vec3 n, float h)
{
	return dot(p, n) - h;
}

float sdTorus(vec3 p, float bigR, float r)
{
	return length(vec2(length(p.xz) - bigR, p.y)) - r;
}

float sdCappedTorus(vec3 p, vec2 sc, float ra, float rb)
{
	p.x = abs(p.x);
	float k = (sc.y * p.x > sc.x * p.y) ? dot(p.xy, sc) : length(p.xy);
	return sqrt(dot(p, p) + ra * ra - 2.0 * ra * k) - rb;
}

float sdLink(vec3 p, float le, float r1, float r2)
{
	vec3 q = vec3(p.x, max(abs(p.y) - le, 0.0), p.z);
	return length(vec2(length(q.xy) - r1, q.z)) - r2;
}

float sdInfCylinder(vec3 p, float r)
{
	return length(p.xz) - r;
}

float sdCylinder(vec3 p, float r, float h)
{
	vec2 d = abs(vec2(length(p.xz), p.y)) - vec2(r, h);
	return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
}

float sdSegCylinder(vec3 p, vec3 a, vec3 b, float r)
{
	vec3 ba = b - a;
	vec3 pa = p - a;
	float baba = dot(ba, ba);
	float paba = dot(pa, ba);
	float x = length(pa * baba - ba * paba) - r * baba;
	float y = abs(paba - baba * 0.5) - baba * 0.5;
	float x2 = x * x;
	float y2 = y * y * baba;
	float d = (max(x, y) < 0.0) ? -min(x2, y2) : (((x > 0.0) ? x2 : 0.0) + ((y > 0.0) ? y2 : 0.0));
	return sign(d) * sqrt(abs(d)) / baba;
}

float sdRoundCylinder(vec3 p, float ra, float rb, float h)
{
	vec2 d = vec2(length(p.xz) - ra + rb, abs(p.y) - h);
	return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - rb;
}

float sdCone(vec3 p, vec2 c, float h)
{
	vec2 q = h * vec2(c.x / c.y, -1.0);
	vec2 w = vec2(length(p.xz), p.y);
	vec2 a = w - q * clamp(dot(w, q) / dot(q, q), 0.0, 1.0);
	vec2 b = w - q * vec2(clamp(w.x / q.x, 0.0, 1.0), 1.0);
	float k = sign(q.y);
	float d = min(dot(a, a), dot(b, b));
	float s = max(k * (w.x * q.y - w.y * q.x), k * (w.y - q.y));
	return sqrt(d) * sign(s);
}

float sdInfCone(vec3 p, vec2 c)
{
	vec2 q = vec2(length(p.xz), -p.y);
	float d = length(q - c * max(dot(q, c), 0.0));
	return d * ((q.x * c.y - q.y * c.x < 0.0) ? -1.0 : 1.0);
}

float sdCappedCone(vec3 p, float h, float r1, float r2)
{
	vec2 q = vec2(length(p.xz), p.y);
	vec2 k1 = vec2(r2, h);
	vec2 k2 = vec2(r2 - r1, 2.0 * h);
	vec2 ca = vec2(q.x - min(q.x, (q.y < 0.0) ? r1 : r2), abs(q.y) - h);
	vec2 cb = q - k1 + k2 * clamp(dot(k1 - q, k2) / dot2(k2), 0.0, 1.0);
	float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
	return s * sqrt(min(dot2(ca), dot2(cb)));
}

float sdSegCone(vec3 p, vec3 a, vec3 b, float ra, float rb)
{
	float rba = rb - ra;
	float baba = dot(b - a, b - a);
	float papa = dot(p - a, p - a);
	float paba = dot(p - a, b - a) / baba;
	float x = sqrt(papa - paba * paba * baba);
	float cax = max(0.0, x - ((paba < 0.5) ? ra : rb));
	float cay = abs(paba - 0.5) - 0.5;
	float k = rba * rba + baba;
	float f = clamp((rba * (x - ra) + paba * baba) / k, 0.0, 1.0);
	float cbx = x - ra - f * rba;
	float cby = paba - f;
	float s = (cbx < 0.0 && cay < 0.0) ? -1.0 : 1.0;
	return s * sqrt(min(cax * cax + cay * cay * baba, cbx * cbx + cby * cby * baba));
}

float sdSolidAngle(vec3 p, vec2 c, float ra)
{
	vec2 q = vec2(length(p.xz), p.y);
	float l = length(q) - ra;
	float m = length(q - c * clamp(dot(q, c), 0.0, ra));
	return max(l, m * sign(c.y * q.x - c.x * q.y));
}

float sdCutSphere(vec3 p, float r, float h)
{
	float w = sqrt(r * r - h * h);
	vec2 q = vec2(length(p.xz), p.y);
	float s = max((h - r) * q.x * q.x + w * w * (h + r - 2.0 * q.y), h * q.x - w * q.y);
	return (s < 0.0) ? length(q) - r
		: (q.x < w) ? h - q.y
		: length(q - vec2(w, h));
}

float sdCutHollowSphere(vec3 p, float r, float h, float t)
{
	float w = sqrt(r * r - h * h);
	vec2 q = vec2(length(p.xz), p.y);
	return ((h * q.x < w * q.y) ? length(q - vec2(w, h)) : abs(length(q) - r)) - t;
}

float sdDeathStar(vec3 p2, float ra, float rb, float d)
{
	float a = (ra * ra - rb * rb + d * d) / (2.0 * d);
	float b = sqrt(max(ra * ra - a * a, 0.0));
	vec2 p = vec2(p2.x, length(p2.yz));
	if (p.x * b - p.y * a > d * max(b - p.y, 0.0))
		return length(p - vec2(a, b));
	return max(length(p) - ra, -(length(p - vec2(d, 0.0)) - rb));
}

float sdRoundCone(vec3 p, float r1, float r2, float h)
{
	float b = (r1 - r2) / h;
	float a = sqrt(1.0 - b * b);
	vec2 q = vec2(length(p.xz), p.y);
	float k = dot(q, vec2(-b, a));
	if (k < 0.0) return length(q) - r1;
	if (k > a * h) return length(q - vec2(0.0, h)) - r2;
	return dot(q, vec2(a, b)) - r1;
}

float sdSegRoundCone(vec3 p, vec3 a, vec3 b, float r1, float r2)
{
	vec3 ba = b - a;
	float l2 = dot(ba, ba);
	float rr = r1 - r2;
	float a2 = l2 - rr * rr;
	float il2 = 1.0 / l2;
	vec3 pa = p - a;
	float y = dot(pa, ba);
	float z = y - l2;
	float x2 = dot2(pa * l2 - ba * y);
	float y2 = y * y * l2;
	float z2 = z * z * l2;
	float k = sign(rr) * rr * rr * x2;
	if (sign(z) * a2 * z2 > k) return sqrt(x2 + z2) * il2 - r2;
	if (sign(y) * a2 * y2 < k) return sqrt(x2 + y2) * il2 - r1;
	return (sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
}

float sdEllipsoid(vec3 p, vec3 r)
{
	float k0 = length(p / r);
	float k1 = length(p / (r * r));
	return k0 * (k0 - 1.0) / k1;
}

float sdRhombus(vec3 p, float la, float lb, float h, float ra)
{
	p = abs(p);
	vec2 b = vec2(la, lb);
	float f = clamp(ndot(b, b - 2.0 * p.xz) / dot(b, b), -1.0, 1.0);
	vec2 q = vec2(
		length(p.xz - 0.5 * b * vec2(1.0 - f, 1.0 + f)) * sign(p.x * b.y + p.z * b.x - b.x * b.y) - ra,
		p.y - h);
	return min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
}

float sdOctahedron(vec3 p, float s)
{
	p = abs(p);
	float m = p.x + p.y + p.z - s;
	vec3 q;
	if (3.0 * p.x < m) q = p.xyz;
	else if (3.0 * p.y < m) q = p.yzx;
	else if (3.0 * p.z < m) q = p.zxy;
	else return m * 0.57735027;
	float k = clamp(0.5 * (q.z - q.y + s), 0.0, s);
	return length(vec3(q.x, q.y - s + k, q.z - k));
}

float sdPyramid(vec3 p, float h)
{
	float m2 = h * h + 0.25;
	p.xz = abs(p.xz);
	p.xz = (p.z > p.x) ? p.zx : p.xz;
	p.xz -= 0.5;
	vec3 q = vec3(p.z, h * p.y - 0.5 * p.x, h * p.x + 0.5 * p.y);
	float s = max(-q.x, 0.0);
	float t = clamp((q.y - 0.5 * p.z) / (m2 + 0.25), 0.0, 1.0);
	float a = m2 * (q.x + s) * (q.x + s) + q.y * q.y;
	float b = m2 * (q.x + 0.5 * t) * (q.x + 0.5 * t) + (q.y - m2 * t) * (q.y - m2 * t);
	float d2 = min(q.y, -q.x * m2 - q.y * 0.5) > 0.0 ? 0.0 : min(a, b);
	return sqrt((d2 + q.z * q.z) / m2) * sign(max(q.z, -p.y));
}

float sdHexPrism(vec3 p, vec2 h)
{
	const vec3 k = vec3(-0.8660254, 0.5, 0.57735);
	p = abs(p);
	p.xy -= 2.0 * min(dot(k.xy, p.xy), 0.0) * k.xy;
	vec2 d = vec2(
		length(p.xy - vec2(clamp(p.x, -k.z * h.x, k.z * h.x), h.x)) * sign(p.y - h.x),
		p.z - h.y);
	return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
}

float sdTriPrism(vec3 p, vec2 h)
{
	vec3 q = abs(p);
	return max(q.z - h.y, max(q.x * 0.866025 + p.y * 0.5, -p.y) - h.x * 0.5);
}

float sdCapsule(vec3 p, vec3 a, vec3 b, float r)
{
	vec3 pa = p - a, ba = b - a;
	float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
	return length(pa - ba * h) - r;
}

float sdVerticalCapsule(vec3 p, float h, float r)
{
	p.y -= clamp(p.y, 0.0, h);
	return length(p) - r;
}

float udTriangle(vec3 p, vec3 a, vec3 b, vec3 c)
{
	vec3 ba = b - a; vec3 pa = p - a;
	vec3 cb = c - b; vec3 pb = p - b;
	vec3 ac = a - c; vec3 pc = p - c;
	vec3 nor = cross(ba, ac);

	return sqrt(
		(sign(dot(cross(ba, nor), pa)) +
		 sign(dot(cross(cb, nor), pb)) +
		 sign(dot(cross(ac, nor), pc)) < 2.0)
		?
		min(min(
			dot2(ba * clamp(dot(ba, pa) / dot2(ba), 0.0, 1.0) - pa),
			dot2(cb * clamp(dot(cb, pb) / dot2(cb), 0.0, 1.0) - pb)),
			dot2(ac * clamp(dot(ac, pc) / dot2(ac), 0.0, 1.0) - pc))
		:
		dot(nor, pa) * dot(nor, pa) / dot2(nor));
}

float udQuad(vec3 p, vec3 a, vec3 b, vec3 c, vec3 d)
{
	vec3 ba = b - a; vec3 pa = p - a;
	vec3 cb = c - b; vec3 pb = p - b;
	vec3 dc = d - c; vec3 pc = p - c;
	vec3 ad = a - d; vec3 pd = p - d;
	vec3 nor = cross(ba, ad);

	return sqrt(
		(sign(dot(cross(ba, nor), pa)) +
		 sign(dot(cross(cb, nor), pb)) +
		 sign(dot(cross(dc, nor), pc)) +
		 sign(dot(cross(ad, nor), pd)) < 3.0)
		?
		min(min(min(
			dot2(ba * clamp(dot(ba, pa) / dot2(ba), 0.0, 1.0) - pa),
			dot2(cb * clamp(dot(cb, pb) / dot2(cb), 0.0, 1.0) - pb)),
			dot2(dc * clamp(dot(dc, pc) / dot2(dc), 0.0, 1.0) - pc)),
			dot2(ad * clamp(dot(ad, pd) / dot2(ad), 0.0, 1.0) - pd))
		:
		dot(nor, pa) * dot(nor, pa) / dot2(nor));
}

// Fractals, mirror of Sdf.Mandelbulb and Sdf.MengerSponge in Scene/Sdf.cs

#define MAX_FRACTAL_ITERATIONS 12.0

// distance estimate of https://iquilezles.org/articles/mandelbulb/ (not exact), the polar axis is Y
float sdMandelbulb(vec3 p, float power, float iterations)
{
	vec3 w = p;
	float m = dot(w, w);
	float dz = 1.0;
	int count = int(min(iterations, MAX_FRACTAL_ITERATIONS));

	for (int i = 0; i < count; i++)
	{
		dz = power * pow(m, 0.5 * (power - 1.0)) * dz + 1.0;

		float r = max(length(w), 1e-8);
		float b = power * acos(clamp(w.y / r, -1.0, 1.0));
		float a = power * atan(w.x, w.z);
		w = p + pow(r, power) * vec3(sin(b) * sin(a), cos(b), sin(b) * cos(a));

		m = dot(w, w);
		if (m > 256.0)
			break;
	}

	m = max(m, 1e-12);
	return 0.25 * log(m) * sqrt(m) / dz;
}

// a cube of half size h minus the crosses of bars on every level
float sdMengerSponge(vec3 p, float halfSize, float iterations)
{
	p /= halfSize;

	float d = sdBox(p, vec3(1.0));
	float s = 1.0;
	int count = int(min(iterations, MAX_FRACTAL_ITERATIONS));

	for (int i = 0; i < count; i++)
	{
		vec3 a = mod(p * s, 2.0) - 1.0;
		s *= 3.0;

		vec3 r = abs(1.0 - 3.0 * abs(a));
		float da = max(r.x, r.y);
		float db = max(r.y, r.z);
		float dc = max(r.z, r.x);

		d = max(d, (min(da, min(db, dc)) - 1.0) / s);
	}

	return d * halfSize;
}

//----------------------------------------------------------------------
// mod(p, c) - c/2 per axis, an axis with zero period is left as is
vec3 opRep(vec3 p, vec3 c)
{
	bvec3 repeated = notEqual(c, vec3(0.0));
	vec3 safeC = mix(vec3(1.0), c, repeated);
	return mix(p, mod(p, safeC) - 0.5 * safeC, repeated);
}

// p - c * clamp(round(p / c), -l, l) per axis, an axis with zero period is left as is
vec3 opRepLimited(vec3 p, vec3 c, vec3 l)
{
	bvec3 repeated = notEqual(c, vec3(0.0));
	vec3 safeC = mix(vec3(1.0), c, repeated);
	return mix(p, p - safeC * clamp(floor(p / safeC + 0.5), -l, l), repeated);
}

vec2 opSmooth(vec2 a, vec2 b, float k)
{
	if (k <= 0.0)
		return a.x < b.x ? a : b;

	float h = clamp(0.5 + 0.5 * (b.x - a.x) / k, 0.0, 1.0);
	float d = b.x + (a.x - b.x) * h - k * h * (1.0 - h);
	return vec2(d, h > 0.5 ? a.y : b.y);
}

vec2 opUnion(vec2 a, vec2 b)
{
	return a.x < b.x ? a : b;
}

//@SCENE_SHAPES

// a box can lower the distance only when it is closer than the best distance or contains
// the point: inside the box the distance can be negative
bool mayContribute(float boxDist, float best)
{
	return boxDist <= 0.0 || boxDist < best;
}

float boxDistance(vec3 p, vec3 bmin, vec3 bmax)
{
	return length(max(max(bmin - p, p - bmax), 0.0));
}

float nodeDistance(int bvh, int node, vec3 p)
{
	return boxDistance(p, g_map[bvh + 2 * node].xyz, g_map[bvh + 2 * node + 1].xyz);
}

// Scene distance, mirror of SceneVM.Eval. Returns (distance, material id).
vec2 map(in vec3 pos)
{
	// unbounded leaves, called one after another by the generated code
	vec2 best = evalAlways(pos);

	int bvh = int(g_map[0].y);
	int nodeCount = int(g_map[0].z);

	if (useBvh == 0)
	{
		for (int node = 0; node < nodeCount; node++)
		{
			vec4 lo = g_map[bvh + 2 * node];
			if (lo.w < 0.0)
				best = opUnion(best, evalLeaf(int(-lo.w), pos));
		}
		return best;
	}

	// bounded leaves: skip boxes not closer than the best distance found so far
	int stack[BVH_STACK_DEPTH];
	int sp = 0;
	if (nodeCount > 0)
		stack[sp++] = 0;

	while (sp > 0)
	{
		int node = stack[--sp];
		vec4 lo = g_map[bvh + 2 * node];
		vec4 hi = g_map[bvh + 2 * node + 1];

		if (!mayContribute(boxDistance(pos, lo.xyz, hi.xyz), best.x))
			continue;

		if (lo.w < 0.0)
		{
			best = opUnion(best, evalLeaf(int(-lo.w), pos));
			continue;
		}

		// visit the nearer child first: push it last
		int left = int(lo.w);
		int right = int(hi.w);
		float dl = nodeDistance(bvh, left, pos);
		float dr = nodeDistance(bvh, right, pos);
		int nearNode = dl < dr ? left : right;
		int farNode = dl < dr ? right : left;

		if (mayContribute(max(dl, dr), best.x))
			stack[sp++] = farNode;
		if (mayContribute(min(dl, dr), best.x))
			stack[sp++] = nearNode;
	}

	return best;
}

vec3 materialColor(float id)
{
	return g_map[int(g_map[0].x) + int(id)].rgb;
}

float materialReflect(float id)
{
	return g_map[int(g_map[0].x) + int(id)].w;
}

vec2 castRay(in vec3 ro, in vec3 rd)
{
	float MAX_DIST = march.x;
	const float MIN_DIST = 0.0002;
	int MAX_RAY_STEPS = int(march.y);

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
		}
		else
		{
			// overstepped into a shape: step back. Nothing to step back means the ray
			// starts inside a shape, which is a hit right here (looping on would hang the GPU)
			if (overstep <= 0.0)
				break;

			t -= overstep;
			phx = MAX_DIST;
			h.x = 1.0;
			overstep = 0.0;
		}
		i++;

		if (h.x < MIN_DIST || t > MAX_DIST)
			break;
	}

	return vec2(t, h.y);
}

// TODO generalize over map() it similar
float softshadow(in vec3 ro, in vec3 rd, in int maxSteps)
{
	const float INIT_T = 0.02;
	const float INIT_RES = 0.5;			// max step: 0.1 doubles the frame time with no visible gain
	float MAX_DIST = shadows.y;
	const float MIN_DIST = 0.001;
	int MAX_RAY_STEPS = maxSteps;		// higher -> longer shadow distance
	float SHADOW_SMOOTH = shadows.x;	// lower ~ smother, higher -> sharper

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

// Color of the surface hit by the ray rd at the distance t, shadowSteps limits the shadow ray
vec3 shade(in vec3 rd, in vec3 pos, in vec3 nor, in float t, in float material, in int shadowSteps)
{
	vec3 col = materialColor(material);
	vec3 ref = reflect(rd, nor);

	// lighitng
	vec3  lig = normalize(sun);
	float amb = clamp(0.5 + 0.5 * nor.y, 0.0, 1.0);
	float dif = clamp(dot(nor, lig), 0.0, 1.0);
	float spe = pow(clamp(dot(ref, lig), 0.0, 1.0), light.z);

	if (shadows.y > 0.0)
		dif *= softshadow(pos, lig, shadowSteps);

	vec3 lin = vec3(0.0);
	lin += dif;
	lin += light.y * spe * dif;
	lin += light.x * amb;
	col *= lin;

	col = mix(col, fogColor, 1.0 - exp(-fogDensity * t * t));	// distance fog

	return vec3(clamp(col, 0.0, 1.0));
}

// The primary ray and up to `bounces` reflected rays: a surface of the reflection k shows
// k of the next ray and 1 - k of itself
vec3 render(in vec3 ro, in vec3 rd)
{
	vec3 total = vec3(0.0);
	float weight = 1.0;

	for (int bounce = 0; bounce <= 3; bounce++)
	{
		vec2 res = castRay(ro, rd);
		float t = res.x;
		if (t > march.x)
		{
			total += weight * sky;
			break;
		}

		vec3 pos = ro + t * rd;
		vec3 nor = calcNormal(pos);

		// a reflected ray gets a short shadow ray
		int shadowSteps = bounce == 0 ? int(march.z) : int(march.z) / 4;
		vec3 col = shade(rd, pos, nor, t, res.y, shadowSteps);

		float k = bounce < bounces ? materialReflect(res.y) : 0.0;
		total += weight * (1.0 - k) * col;
		if (k <= 0.0)
			break;

		weight *= k;
		ro = pos + nor * 0.002;	// off the surface: the next ray must not hit it again
		rd = reflect(rd, nor);
	}

	return clamp(total, 0.0, 1.0);
}

void main(void)
{
	// ray direction
	vec2 q = fragCoord.xy / iResolution.xy;
	vec2 p = -1.0 + 2.0 * q;
	p.x *= iResolution.x / iResolution.y;
	vec3 rd = camProj * normalize(vec3(p.xy, 2.0));

	vec3 col = render(ro, rd);

	col = pow(col, vec3(light.w)); // gamma
	fragColor = vec4(col, 1.0);
}