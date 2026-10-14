#version 330 core

uniform float iGlobalTime;
uniform vec3 iResolution;
uniform vec3 ro;	// camera ray origin
uniform mat3 camProj;	// camera projection matrix
uniform int useBvh;	// 0: evaluate every leaf, to compare the speed

// Compiled scene program, uploaded by Render from SceneState (see Scene/SceneOpcodes.cs).
// g_map[0] = (materialBase, bvhStart, bvhNodeCount, alwaysCount),
// g_map[1 .. 1 + alwaysCount) = (start, end) of unbounded leaves, then leaf programs,
// BVH nodes (min.xyz, left | -start) (max.xyz, right | end), material colors (rgb).
uniform SdElements
{
	vec4 g_map[1024];	// Const.UBO_SDELEMENTSMAP_BLOCKCOUNT
};

in vec2 fragCoord;
out vec4 fragColor;

#define STACK_DEPTH 8	// Const.SCENE_STACK_DEPTH
#define BVH_STACK_DEPTH 32	// Const.BVH_STACK_DEPTH
#define EMPTY_DISTANCE 1e10

// opcodes, keep in sync with Scene/SceneOpcodes.cs
#define OP_SPHERE			1
#define OP_BOX				2
#define OP_ROUNDBOX			3
#define OP_BOXFRAME			4
#define OP_PLANE			5
#define OP_TORUS			6
#define OP_CAPPEDTORUS		7
#define OP_LINK				8
#define OP_INFCYLINDER		9
#define OP_CYLINDER			10
#define OP_SEGCYLINDER		11
#define OP_ROUNDCYLINDER	12
#define OP_CONE				13
#define OP_INFCONE			14
#define OP_CAPPEDCONE		15
#define OP_SEGCONE			16
#define OP_SOLIDANGLE		17
#define OP_CUTSPHERE		18
#define OP_CUTHOLLOWSPHERE	19
#define OP_DEATHSTAR		20
#define OP_ROUNDCONE		21
#define OP_SEGROUNDCONE		22
#define OP_ELLIPSOID		23
#define OP_RHOMBUS			24
#define OP_OCTAHEDRON		25
#define OP_PYRAMID			26
#define OP_HEXPRISM			27
#define OP_TRIPRISM			28
#define OP_CAPSULE			29
#define OP_VCAPSULE			30
#define OP_TRIANGLE			31
#define OP_QUAD				32

#define OP_DOMAIN_FIRST		50
#define OP_TRANSLATE		50
#define OP_REPEAT			51
#define OP_REPEAT_LIMITED	52
#define OP_ROTATE			53
#define OP_SCALE			54
#define OP_MIRROR			55
#define OP_ELONGATE			56
#define OP_TWIST			57
#define OP_BEND				58
#define OP_REVOLVE			59
#define OP_EXTRUDE_SLICE	60
#define OP_POPPOS			61

#define OP_POST_FIRST		70
#define OP_ROUND			70
#define OP_ONION			71
#define OP_DISPLACE			72
#define OP_EXTRUDE			73

#define OP_FOLD_FIRST		80
#define OP_UNION			80
#define OP_SUBTRACT			81
#define OP_INTERSECT		82
#define OP_XOR				83
#define OP_SMOOTH_UNION		84
#define OP_SMOOTH_SUBTRACT	85
#define OP_SMOOTH_INTERSECT	86

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

//----------------------------------------------------------------------
// Scene program interpreter, mirror of Scene/SceneVM.cs.
// Instruction at pc: header (op, length, material, 0), S = g_map[pc + 1], Vi = g_map[pc + 2 + i]

#define S	g_map[pc + 1]
#define V(i)	g_map[pc + 2 + (i)].xyz

float sdPrimitive(int op, int pc, vec3 p)
{
	switch (op)
	{
		case OP_SPHERE:				return sdSphere(p, S.x);
		case OP_BOX:				return sdBox(p, V(0));
		case OP_ROUNDBOX:			return sdRoundBox(p, V(0), S.x);
		case OP_BOXFRAME:			return sdBoxFrame(p, V(0), S.x);
		case OP_PLANE:				return sdPlane(p, V(0), S.x);
		case OP_TORUS:				return sdTorus(p, S.x, S.y);
		case OP_CAPPEDTORUS:		return sdCappedTorus(p, S.xy, S.z, S.w);
		case OP_LINK:				return sdLink(p, S.x, S.y, S.z);
		case OP_INFCYLINDER:		return sdInfCylinder(p, S.x);
		case OP_CYLINDER:			return sdCylinder(p, S.x, S.y);
		case OP_SEGCYLINDER:		return sdSegCylinder(p, V(0), V(1), S.x);
		case OP_ROUNDCYLINDER:		return sdRoundCylinder(p, S.x, S.y, S.z);
		case OP_CONE:				return sdCone(p, S.xy, S.z);
		case OP_INFCONE:			return sdInfCone(p, S.xy);
		case OP_CAPPEDCONE:			return sdCappedCone(p, S.x, S.y, S.z);
		case OP_SEGCONE:			return sdSegCone(p, V(0), V(1), S.x, S.y);
		case OP_SOLIDANGLE:			return sdSolidAngle(p, S.xy, S.z);
		case OP_CUTSPHERE:			return sdCutSphere(p, S.x, S.y);
		case OP_CUTHOLLOWSPHERE:	return sdCutHollowSphere(p, S.x, S.y, S.z);
		case OP_DEATHSTAR:			return sdDeathStar(p, S.x, S.y, S.z);
		case OP_ROUNDCONE:			return sdRoundCone(p, S.x, S.y, S.z);
		case OP_SEGROUNDCONE:		return sdSegRoundCone(p, V(0), V(1), S.x, S.y);
		case OP_ELLIPSOID:			return sdEllipsoid(p, V(0));
		case OP_RHOMBUS:			return sdRhombus(p, S.x, S.y, S.z, S.w);
		case OP_OCTAHEDRON:			return sdOctahedron(p, S.x);
		case OP_PYRAMID:			return sdPyramid(p, S.x);
		case OP_HEXPRISM:			return sdHexPrism(p, S.xy);
		case OP_TRIPRISM:			return sdTriPrism(p, S.xy);
		case OP_CAPSULE:			return sdCapsule(p, V(0), V(1), S.x);
		case OP_VCAPSULE:			return sdVerticalCapsule(p, S.x, S.y);
		case OP_TRIANGLE:			return udTriangle(p, V(0), V(1), V(2));
		case OP_QUAD:				return udQuad(p, V(0), V(1), V(2), V(3));
	}
	return EMPTY_DISTANCE;
}

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

// returns the new (position, scale)
vec4 opDomain(int op, int pc, vec3 p, float s)
{
	switch (op)
	{
		case OP_TRANSLATE:
			return vec4(p - V(0), s);

		case OP_REPEAT:
			return vec4(opRep(p, V(0)), s);

		case OP_REPEAT_LIMITED:
			return vec4(opRepLimited(p, V(0), V(1)), s);

		case OP_ROTATE:
			return vec4(dot(V(0), p), dot(V(1), p), dot(V(2), p), s);

		case OP_SCALE:
			return vec4(p / S.x, s * S.x);

		case OP_MIRROR:
			return vec4(mix(p, abs(p), notEqual(V(0), vec3(0.0))), s);

		case OP_ELONGATE:
			return vec4(p - clamp(p, -V(0), V(0)), s);

		case OP_TWIST:
		{
			float a = S.x * p.y;
			float co = cos(a);
			float si = sin(a);
			return vec4(co * p.x - si * p.z, p.y, si * p.x + co * p.z, s);
		}

		case OP_BEND:
		{
			float a = S.x * p.x;
			float co = cos(a);
			float si = sin(a);
			return vec4(co * p.x - si * p.y, si * p.x + co * p.y, p.z, s);
		}

		case OP_REVOLVE:
			return vec4(length(p.xz) - S.x, p.y, 0.0, s);

		case OP_EXTRUDE_SLICE:
			return vec4(p.x, p.y, 0.0, s);
	}
	return vec4(p, s);
}

// modifies the top distance, p and s are the block's position and scale
vec2 opPost(int op, int pc, vec2 d, vec3 p, float s)
{
	switch (op)
	{
		case OP_ROUND:
			return vec2(d.x - S.x * s, d.y);

		case OP_ONION:
			return vec2(abs(d.x) - S.x * s, d.y);

		case OP_DISPLACE:
			return vec2(d.x + S.x * s * sin(S.y * p.x) * sin(S.y * p.y) * sin(S.y * p.z), d.y);

		case OP_EXTRUDE:
		{
			vec2 w = vec2(d.x, (abs(p.z) - S.x) * s);
			return vec2(min(max(w.x, w.y), 0.0) + length(max(w, 0.0)), d.y);
		}
	}
	return d;
}

#undef S
#undef V

vec2 opSmooth(vec2 a, vec2 b, float k)
{
	if (k <= 0.0)
		return a.x < b.x ? a : b;

	float h = clamp(0.5 + 0.5 * (b.x - a.x) / k, 0.0, 1.0);
	float d = b.x + (a.x - b.x) * h - k * h * (1.0 - h);
	return vec2(d, h > 0.5 ? a.y : b.y);
}

vec2 opFold(int op, vec2 a, vec2 b, float k)
{
	switch (op)
	{
		case OP_UNION:
			return a.x < b.x ? a : b;

		case OP_SUBTRACT:
			return vec2(max(a.x, -b.x), a.y);

		case OP_INTERSECT:
			return a.x > b.x ? a : b;

		case OP_XOR:
			return vec2(max(min(a.x, b.x), -max(a.x, b.x)), a.x < b.x ? a.y : b.y);

		case OP_SMOOTH_UNION:
			return opSmooth(a, b, k);

		case OP_SMOOTH_SUBTRACT:
			return vec2(-opSmooth(vec2(-a.x, a.y), b, k).x, a.y);

		case OP_SMOOTH_INTERSECT:
		{
			vec2 r = opSmooth(vec2(-a.x, a.y), vec2(-b.x, b.y), k);
			return vec2(-r.x, r.y);
		}
	}
	return a;
}

// Runs the stack program of a leaf in [start, end), returns (distance, material id)
vec2 evalLeaf(int start, int end, vec3 pos)
{
	vec4 ps[STACK_DEPTH];	// xyz position, w scale
	vec2 ds[STACK_DEPTH];	// x distance, y material
	int pp = 0;
	int dp = 0;
	ps[0] = vec4(pos, 1.0);

	int pc = start;

	while (pc < end)
	{
		vec4 h = g_map[pc];
		int op = int(h.x);
		vec3 p = ps[pp].xyz;
		float s = ps[pp].w;

		if (op < OP_DOMAIN_FIRST)
		{
			ds[dp++] = vec2(sdPrimitive(op, pc, p) * s, h.z);
		}
		else if (op == OP_POPPOS)
		{
			pp--;
		}
		else if (op < OP_POST_FIRST)
		{
			ps[pp + 1] = opDomain(op, pc, p, s);
			pp++;
		}
		else if (op < OP_FOLD_FIRST)
		{
			ds[dp - 1] = opPost(op, pc, ds[dp - 1], p, s);
		}
		else
		{
			float k = h.y > 1.0 ? g_map[pc + 1].x * s : 0.0;
			vec2 b = ds[--dp];
			ds[dp - 1] = opFold(op, ds[dp - 1], b, k);
		}

		pc += int(h.y);
	}

	return ds[0];
}

vec2 opUnion(vec2 a, vec2 b)
{
	return a.x < b.x ? a : b;
}

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
	vec2 best = vec2(EMPTY_DISTANCE, 0.0);

	// unbounded leaves
	int alwaysCount = int(g_map[0].w);
	for (int i = 0; i < alwaysCount; i++)
		best = opUnion(best, evalLeaf(int(g_map[1 + i].x), int(g_map[1 + i].y), pos));

	int bvh = int(g_map[0].y);
	int nodeCount = int(g_map[0].z);

	if (useBvh == 0)
	{
		for (int node = 0; node < nodeCount; node++)
		{
			vec4 lo = g_map[bvh + 2 * node];
			if (lo.w < 0.0)
				best = opUnion(best, evalLeaf(int(-lo.w), int(g_map[bvh + 2 * node + 1].w), pos));
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
			best = opUnion(best, evalLeaf(int(-lo.w), int(hi.w), pos));
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
float softshadow(in vec3 ro, in vec3 rd)
{
	const float INIT_T = 0.02;
	const float INIT_RES = 0.5;			// max step: 0.1 doubles the frame time with no visible gain
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