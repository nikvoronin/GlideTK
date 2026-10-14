# GlideTK

Project is no longer active. Shaders are partially compatible with [ShaderToy](https://www.shadertoy.com/).

![screenshot](https://user-images.githubusercontent.com/11328666/51066117-7be35400-1619-11e9-83e8-7d1c5bf00d12.png)

- WASD + mouse-look
- SPACE - Up
- ESC - Exit
- F11 - Fullscreen/Normal
- F1 - Key help on the screen (frame time, position and scene errors are always shown)
- F7 - Bounding volume hierarchy on/off (on by default, off evaluates every object to compare the speed)
- F5 - Reload scene (`scenes/*.scene` next to the executable, or the file passed as the first argument: `GlideTK.exe scenes/showcase.scene`)

## Scene language

Scene files live in `GlideTK/scenes/*.scene` and are copied next to the executable.
`GlideTK.exe` loads `scenes/default.scene`, or the file given as the first argument:

```shell
./GlideTK.exe scenes/showcase.scene
```

`F5` reloads the scene at runtime. The parsed scene is compiled every physics tick into a vec4 program
(see `Scene/SceneOpcodes.cs`), which is evaluated both by the CPU physics (`Scene/SceneVM.cs`)
and by the shader (`map()` in `shaders/fragment.c`).

**Performance:** unions and domain operations (translate, rotate, repeat, ...) are opened into separate objects,
each with a bounding box in a BVH; objects whose box is farther than the nearest surface found so far are skipped
(`F7` toggles it on the GPU). Unbounded objects are evaluated everywhere: `plane`, `infcylinder`, `infcone`
and `repeat` without `limit`. Prefer `repeat (c) limit (n)` and finite shapes for big scenes.

Distance functions and operations follow [iquilezles.org/articles/distfunctions](https://iquilezles.org/articles/distfunctions/).

- `scenes/showcase.scene` places every one of them in a row.
- `scenes/parthenon.scene` is a Doric temple,
- `scenes/escher.scene` builds the stairs of M. C. Escher (Relativity, Ascending and Descending, Waterfall, Belvedere),
- `scenes/menger.scene` is a Menger sponge of four levels with tunnels to walk through.

### Primitives

Centered at the origin. Optional `color (r, g, b)` may follow the arguments. Angles are in degrees.

| Syntax | Notes |
| --- | --- |
| `sphere r` | |
| `box (sx, sy, sz)` | half sizes |
| `roundbox (sx, sy, sz) r` | half sizes include the rounding r |
| `boxframe (sx, sy, sz) e` | edge thickness e |
| `plane (nx, ny, nz) dist` | normal, offset along the normal |
| `torus R r` | in XZ plane, big and small radius |
| `cappedtorus angle R r` | arc of ±angle in XY plane |
| `link le r1 r2` | chain link: half length, radius, thickness |
| `infcylinder r` | infinite, along Y |
| `cylinder r h` | along Y, h is the half height |
| `segcylinder (a) (b) r` | between points a and b |
| `roundcylinder r rounding h` | along Y, h is the half height |
| `cone angle h` | apex at the origin, opens down to y = -h |
| `infcone angle` | infinite, apex at the origin, opens down |
| `cappedcone h r1 r2` | along Y, half height h, bottom r1, top r2 |
| `segcone (a) (b) ra rb` | between points a and b |
| `solidangle angle r` | sphere sector, opens up |
| `cutsphere r h` | sphere cut by the plane y = h, -r < h < r |
| `cuthollowsphere r h thickness` | shell cut by the plane y = h, -r < h < r |
| `deathstar ra rb d` | sphere ra minus sphere rb at distance d along X |
| `roundcone r1 r2 h` | along Y, bottom sphere r1 at 0, top sphere r2 at h |
| `segroundcone (a) (b) r1 r2` | between points a and b |
| `ellipsoid (rx, ry, rz)` | approximate distance |
| `rhombus la lb h rounding` | in XZ plane, half diagonals la lb, half height h |
| `octahedron s` | |
| `pyramid h` | base 1 x 1 at y = 0, apex at y = h |
| `hexprism r h` | along Z, half length h |
| `triprism r h` | along Z, half length h |
| `capsule (a) (b) r` | |
| `vcapsule h r` | from y = 0 to y = h |
| `triangle (a) (b) (c)` | unsigned, zero thickness |
| `quad (a) (b) (c) (d)` | unsigned, zero thickness, planar points in order |

### Domain operations

Transform the position for the elements of the block, the block is a union.

| Syntax | Notes |
| --- | --- |
| `translate (x, y, z) { ... }` | |
| `rotate (degX, degY, degZ) { ... }` | object rotation R = Rz * Ry * Rx |
| `scale s { ... }` | |
| `repeat (cx, cy, cz) { ... }` | infinite, cells centered at c/2, period 0 disables an axis |
| `repeat (cx, cy, cz) limit (lx, ly, lz) { ... }` | 2 * l + 1 copies per axis centered at the origin |
| `mirror (x, y, z) { ... }` | symmetry: abs() on the axes that are not 0 |
| `elongate (hx, hy, hz) { ... }` | stretches the shape by h on each side |
| `twist k { ... }` | around Y, k radians per unit |
| `bend k { ... }` | in XY plane, k radians per unit |
| `revolve o { ... }` | revolves the XY cross-section (z = 0) around Y at radius o |
| `extrude h { ... }` | extrudes the XY cross-section (z = 0) along Z, half length h |

`twist`, `bend` and `displace` distort distances, keep their values moderate.

### Distance operations

Apply to the union of the block.

| Syntax | Notes |
| --- | --- |
| `round r { ... }` | rounds the shape by r (grows it by r) |
| `onion thickness { ... }` | hollow shell |
| `displace amplitude frequency { ... }` | adds amplitude * sin(f x) sin(f y) sin(f z) |

### Object operations

| Syntax | Notes |
| --- | --- |
| `union { a b ... }` | |
| `subtract { a b ... }` | a minus all the rest |
| `intersect { a b ... }` | |
| `xor { a b ... }` | |
| `smooth k { a b ... }` | smooth union with radius k |
| `smoothsubtract k { a b ... }` | |
| `smoothintersect k { a b ... }` | |

The top level of a file is an implicit union.

### Expressions

- numbers, `t` (physics time in seconds), `pi`
- `+ - * /`, unary minus, parentheses
- `sin(x) cos(x) abs(x) min(a, b) max(a, b)`

Arguments of an element are separated by spaces, so arithmetic in a separate argument must be parenthesized:
`sphere (1 + 0.2 * sin(t))`. Inside a vector any expression is allowed: `repeat (10, 10 + sin(t), 10) { ... }`

Comments start with `#` and last to the end of the line.

### Example

```text
plane (0, 1, 0) 0
repeat (10, 10 + sin(t), 10) { sphere 1 color (1, 0.55, 0.45) }
subtract { box (2, 2, 2)  translate (0, 1, 0) { sphere 2.5 } }
smooth 0.5 { sphere 1  rotate (0, 90, 0) { torus 1 0.2 } }
translate (5, 1, 0) { twist (sin(t)) { round 0.1 { box (0.5, 1, 0.5) } } }
```

## History

| Date | Change |
|---|---|
| 08.10.2026 | Drawing text: 8x8 bitmap font, instanced HUD with stats, F1 help and scene errors |
| 07.10.2026 | Player collides as a sphere with the scene SDF: substeps, push-out from the closest contact, gravity |
| 12.01.2026 | Map in UBO with primitives: scene language (scenes/*.scene) shared by GPU and physics |
| 12.01.2026 | Translate iGlobalTimer to the Phys engine (single physics.GlobalTime) |
| 01.01.2026 | Delta to FpsController |
| 27.12.2017 16:15 | Made the FpsController class |
| 27.12.2017 13:37 | Made the Camera class |
| 26.12.2017 16:35 | Extract SetCamera from shader to the CPU and update matrix only if camera changes its position or target |
| 02.12.2017 14:42 | Print coordinates |
