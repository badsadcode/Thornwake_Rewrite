#[compute]
#version 450
// FFT ocean, step 2 (OceanFft.cs): an inverse FFT along every row (vertical = 0) or column (vertical = 1) of each
// layer, in place; one workgroup per line, in shared memory. Each texel holds two complex numbers (xy, zw).
layout(local_size_x = 256, local_size_y = 1, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba32f) uniform restrict image2DArray spec_img;

layout(push_constant, std430) uniform Params {
	uint vertical;
	uint log2n;
	uint pad0;
	uint pad1;
} pc;

shared vec4 buf[2][256];

vec2 cmul(vec2 a, vec2 b) { return vec2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x); }

void main() {
	uint i = gl_LocalInvocationID.x;
	uint line = gl_WorkGroupID.y;
	int layer = int(gl_WorkGroupID.z);
	ivec3 coord = pc.vertical == 1u ? ivec3(line, i, layer) : ivec3(i, line, layer);
	buf[0][bitfieldReverse(i) >> (32u - pc.log2n)] = imageLoad(spec_img, coord);
	barrier();
	uint src = 0u;
	for (uint s = 1u; s <= pc.log2n; s++) {
		uint m = 1u << s, half_m = m >> 1u;
		uint k = i & (m - 1u), base = i - k;
		uint j = k & (half_m - 1u);
		float angle = 6.28318530718 * float(j) / float(m); // inverse transform: e^{+2pii j/m}
		vec2 w = vec2(cos(angle), sin(angle));
		vec4 a = buf[src][base + j];
		vec4 b = buf[src][base + j + half_m];
		vec4 wb = vec4(cmul(w, b.xy), cmul(w, b.zw));
		buf[1u - src][i] = k < half_m ? a + wb : a - wb;
		src = 1u - src;
		barrier();
	}
	imageStore(spec_img, coord, buf[src][i]);
}
