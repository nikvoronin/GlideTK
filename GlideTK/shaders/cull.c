#version 430 core

// Which leaves of the BVH can matter for every tile of the picture.
// A work group handles one tile: it builds the pyramid of the rays of the tile and tests the
// bounding boxes of the leaves against it. Two lists per tile are written for trace.c:
// the leaves the primary rays can reach, and the leaves that can shade the tile (shadow rays).

layout(local_size_x = 64) in;	// WORK_GROUP

layout(location = 0) uniform vec3 iResolution;
layout(location = 1) uniform vec3 ro;	// camera ray origin
layout(location = 2) uniform mat3 camProj;	// camera projection matrix
layout(location = 6) uniform vec3 sun;	// direction to the sun
layout(location = 11) uniform vec2 shadows;	// sharpness, max distance (0: no shadows)
layout(location = 12) uniform vec3 march;	// max distance, max steps, max shadow steps

layout(std140, binding = 1) uniform SdElements
{
	vec4 g_map[1024];	// Const.UBO_SDELEMENTSMAP_BLOCKCOUNT
};

// per tile TILE_STRIDE ints, see trace.c
layout(std430, binding = 2) writeonly buffer TileLists
{
	int tileData[];
};

#define TILE_SIZE 8	// Const.TILE_SIZE
#define TILE_LIST 64	// Const.TILE_LIST
#define TILE_STRIDE (2 * TILE_LIST + 2)
#define WORK_GROUP 64
#define MARGIN 0.02		// the normal looks a little around the hit point
#define GUARD 4.0		// pixels: the pyramid of a tile is wider than the tile. The step of a ray depends on
						// the objects near it, a tight list changes the steps and so the picture at grazing angles

shared int primaryCount;
shared int shadowCount;
shared int primaryList[TILE_LIST];
shared int shadowList[TILE_LIST];

vec3 planeN[4];	// the sides of the pyramid, the normals look inside

// the same as the ray of a pixel in trace.c
vec3 pixelRay(vec2 fragCoord)
{
	vec2 q = fragCoord / iResolution.xy;
	vec2 p = -1.0 + 2.0 * q;
	p.x *= iResolution.x / iResolution.y;
	return camProj * normalize(vec3(p.xy, 2.0));
}

void buildPyramid(vec2 lo, vec2 hi)
{
	vec3 c[4];
	c[0] = pixelRay(vec2(lo.x, lo.y));
	c[1] = pixelRay(vec2(hi.x, lo.y));
	c[2] = pixelRay(vec2(hi.x, hi.y));
	c[3] = pixelRay(vec2(lo.x, hi.y));

	vec3 inside = c[0] + c[1] + c[2] + c[3];
	for (int i = 0; i < 4; i++)
	{
		vec3 n = normalize(cross(c[i], c[(i + 1) & 3]));
		planeN[i] = dot(n, inside) < 0.0 ? -n : n;
	}
}

float boxDistance(vec3 p, vec3 bmin, vec3 bmax)
{
	return length(max(max(bmin - p, p - bmax), 0.0));
}

// false only when the whole box is outside one side of the pyramid
bool inPyramid(vec3 bmin, vec3 bmax)
{
	for (int i = 0; i < 4; i++)
	{
		vec3 n = planeN[i];
		vec3 corner = vec3(
			n.x > 0.0 ? bmax.x : bmin.x,
			n.y > 0.0 ? bmax.y : bmin.y,
			n.z > 0.0 ? bmax.z : bmin.z);

		if (dot(n, corner - ro) < -MARGIN)
			return false;
	}

	return true;
}

void main(void)
{
	ivec2 resolution = ivec2(iResolution.xy);
	ivec2 tilesCount = (resolution + TILE_SIZE - 1) / TILE_SIZE;
	ivec2 tile = ivec2(gl_WorkGroupID.xy);
	int lane = int(gl_LocalInvocationIndex);

	if (lane == 0)
	{
		primaryCount = 0;
		shadowCount = 0;
	}
	barrier();

	ivec2 first = tile * TILE_SIZE;
	ivec2 last = min(first + TILE_SIZE, resolution);
	buildPyramid(vec2(first) - GUARD, vec2(last) + GUARD);

	vec3 sweep = -normalize(sun) * shadows.y;	// shadow rays go towards the sun
	// a soft shadow is also darkened by the objects near the ray: closer than t / sharpness
	float reach = shadows.y / max(shadows.x, 0.1);
	int bvh = int(g_map[0].y);
	int nodeCount = int(g_map[0].z);

	for (int node = lane; node < nodeCount; node += WORK_GROUP)
	{
		vec4 lo = g_map[bvh + 2 * node];
		if (lo.w >= 0.0)
			continue;	// not a leaf

		vec3 bmin = lo.xyz;
		vec3 bmax = g_map[bvh + 2 * node + 1].xyz;

		// the primary rays: the box meets the pyramid within the ray length
		if (boxDistance(ro, bmin, bmax) <= march.x + MARGIN && inPyramid(bmin, bmax))
		{
			int i = atomicAdd(primaryCount, 1);
			if (i < TILE_LIST)
				primaryList[i] = node;
		}

		// the shadow rays: the box can cast a shadow on the points of the pyramid,
		// its position before it is shifted to the sun covers them
		if (shadows.y > 0.0)
		{
			vec3 smin = min(bmin, bmin + sweep) - reach;
			vec3 smax = max(bmax, bmax + sweep) + reach;
			if (boxDistance(ro, smin, smax) <= march.x + MARGIN && inPyramid(smin, smax))
			{
				int i = atomicAdd(shadowCount, 1);
				if (i < TILE_LIST)
					shadowList[i] = node;
			}
		}
	}

	memoryBarrierShared();
	barrier();

	int base = (tile.y * tilesCount.x + tile.x) * TILE_STRIDE;
	int primary = primaryCount;
	int shadow = shadowCount;

	if (lane == 0)
	{
		// too many leaves: -1 sends the tile to the whole BVH
		tileData[base] = primary > TILE_LIST ? -1 : primary;
		tileData[base + TILE_LIST + 1] = shadow > TILE_LIST ? -1 : shadow;
	}

	for (int i = lane; i < min(primary, TILE_LIST); i += WORK_GROUP)
		tileData[base + 1 + i] = primaryList[i];

	for (int i = lane; i < min(shadow, TILE_LIST); i += WORK_GROUP)
		tileData[base + TILE_LIST + 2 + i] = shadowList[i];
}
