#[compute]
#version 450
// FFT ocean, step 4 (OceanFft.cs): one mipmap level of the displacement and slope textures from the level above
// (2x2 average), so distant water samples band-limited waves instead of shimmering.
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba16f) uniform readonly image2DArray src_disp;
layout(set = 0, binding = 1, rgba16f) uniform writeonly image2DArray dst_disp;
layout(set = 0, binding = 2, rgba16f) uniform readonly image2DArray src_deriv;
layout(set = 0, binding = 3, rgba16f) uniform writeonly image2DArray dst_deriv;

layout(push_constant, std430) uniform Params {
	uint size; // destination size
	uint pad0;
	uint pad1;
	uint pad2;
} pc;

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	if (id.x >= int(pc.size) || id.y >= int(pc.size)) return;
	ivec3 s = ivec3(id.xy * 2, id.z);
	vec4 d = imageLoad(src_disp, s) + imageLoad(src_disp, s + ivec3(1, 0, 0)) + imageLoad(src_disp, s + ivec3(0, 1, 0)) + imageLoad(src_disp, s + ivec3(1, 1, 0));
	vec4 g = imageLoad(src_deriv, s) + imageLoad(src_deriv, s + ivec3(1, 0, 0)) + imageLoad(src_deriv, s + ivec3(0, 1, 0)) + imageLoad(src_deriv, s + ivec3(1, 1, 0));
	imageStore(dst_disp, id, d * 0.25);
	imageStore(dst_deriv, id, g * 0.25);
}
