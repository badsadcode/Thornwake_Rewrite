#[compute]
#version 450
// FFT ocean, step 3 (OceanFft.cs): the transformed fields into the textures the water shader samples, per cascade:
//   disp  = (lambda Dx, Dy, lambda Dz, foam)   deriv = (dDy/dx, dDy/dz, lambda dDx/dx, lambda dDz/dz)
// Foam is born where the surface folds (the Jacobian dropping below a threshold) and lingers, fading. It lives in
// its own pair of textures (read last frame's, write this frame's) so it can be carried: experimental 3A moves it
// with the wind drift (texels per frame, sampled bilinearly), so the foam a crest leaves behind travels away instead
// of riding back and forth with the orbiting water; experimental 3 keeps it in place (shift 0).
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba32f) uniform readonly image2DArray spec_img;
layout(set = 0, binding = 1, rgba16f) uniform writeonly image2DArray disp_img;
layout(set = 0, binding = 2, rgba16f) uniform writeonly image2DArray deriv_img;
layout(set = 0, binding = 3, r16f) uniform readonly image2DArray foam_prev;
layout(set = 0, binding = 4, r16f) uniform writeonly image2DArray foam_next;

layout(push_constant, std430) uniform Params {
	vec4 chop;  // lambda per cascade; w: dt (s)
	vec4 foam;  // x: fold threshold, y: unused, z: fade per second, w: N
	vec4 gain;  // birth gain per cascade
	vec4 adv01; // foam shift this frame (texels): cascade 0 x, z, cascade 1 x, z
	vec4 adv2;  // cascade 2 x, z
} pc;

float foam_at(ivec2 p, int layer, int n) {
	p = ((p % n) + n) % n;
	return imageLoad(foam_prev, ivec3(p, layer)).r;
}

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	int n = int(pc.foam.w);
	if (id.x >= n || id.y >= n) return;
	// The centred spectrum: (-1)^(x+y) after the transform.
	float sgn = ((id.x + id.y) & 1) == 0 ? 1.0 : -1.0;
	vec4 a = imageLoad(spec_img, id) * sgn;
	vec4 b = imageLoad(spec_img, ivec3(id.xy, id.z + 3)) * sgn;
	float lambda = id.z == 0 ? pc.chop.x : (id.z == 1 ? pc.chop.y : pc.chop.z);
	float gain = id.z == 0 ? pc.gain.x : (id.z == 1 ? pc.gain.y : pc.gain.z);
	vec2 adv = id.z == 0 ? pc.adv01.xy : (id.z == 1 ? pc.adv01.zw : pc.adv2.xy);
	float dxx = lambda * b.y, dzz = lambda * b.z, dxz = lambda * b.w;
	float jacobian = (1.0 + dxx) * (1.0 + dzz) - dxz * dxz;
	// Last frame's foam where this texel's water came from (bilinear; exact when the shift is 0).
	vec2 src = vec2(id.xy) - adv;
	ivec2 s0 = ivec2(floor(src));
	vec2 f = src - vec2(s0);
	float prev = mix(mix(foam_at(s0, id.z, n), foam_at(s0 + ivec2(1, 0), id.z, n), f.x),
		mix(foam_at(s0 + ivec2(0, 1), id.z, n), foam_at(s0 + ivec2(1, 1), id.z, n), f.x), f.y);
	float born = clamp((pc.foam.x - jacobian) * gain, 0.0, 1.0);
	float foam = max(prev * exp(-pc.chop.w * pc.foam.z), born);
	imageStore(foam_next, id, vec4(foam));
	imageStore(disp_img, id, vec4(lambda * a.x, a.z, lambda * a.y, foam));
	imageStore(deriv_img, id, vec4(a.w, b.x, dxx, dzz));
}
