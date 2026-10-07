#[compute]
#version 450
// FFT ocean, step 1 (OceanFft.cs): the spectrum at the current time for each cascade (layer). In: h0 = (h0(k),
// conj(h0(-k))) per texel, layers 0..2 the previous spectrum and 3..5 the new one, blended by misc.z (a change of sea
// state or wind fades in over a few seconds instead of popping), centred (texel n <-> k = 2pi(n - N/2)/L). Out, two complex fields per texel, layers 0..2 and
// 3..5 of one array (each pair is one inverse FFT of two real fields at once):
//   A = (Dx + i Dz, Dy + i dDy/dx)   B = (dDy/dz + i dDx/dx, dDz/dz + i dDx/dz)
// with the choppy displacement D = +i k/|k| h (crests pinch, as Gerstner waves).
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba32f) uniform readonly image2DArray h0_img;
layout(set = 0, binding = 1, rgba32f) uniform writeonly image2DArray spec_img;

layout(push_constant, std430) uniform Params {
	vec4 sizes; // tile size per cascade (m); w: time (s)
	vec4 misc;  // x: N; y: loop period T (s): frequencies are whole multiples of 2pi/T, time runs modulo T; z: blend
} pc;

vec2 cmul(vec2 a, vec2 b) { return vec2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x); }
vec2 mul_i(vec2 a) { return vec2(-a.y, a.x); }

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	int n = int(pc.misc.x);
	if (id.x >= n || id.y >= n) return;
	float size = id.z == 0 ? pc.sizes.x : (id.z == 1 ? pc.sizes.y : pc.sizes.z);
	vec2 k = 6.28318530718 * vec2(float(id.x - n / 2), float(id.y - n / 2)) / size;
	float kl = length(k);
	vec4 h0 = mix(imageLoad(h0_img, id), imageLoad(h0_img, ivec3(id.xy, id.z + 3)), pc.misc.z);
	vec2 h = vec2(0.0);
	if (kl > 1e-6) {
		// Frequencies rounded to whole multiples of 2pi/T: the sea repeats every T seconds exactly, so the time can wrap
		// (float precision over hours of play) without a jump.
		float base = 6.28318530718 / pc.misc.y;
		float omega = floor(sqrt(9.81 * kl) / base) * base;
		float phase = omega * pc.sizes.w;
		vec2 e = vec2(cos(phase), sin(phase));
		// h0 e^{-iwt} + conj(h0(-k)) e^{iwt}: each wave runs along its k (with the wind).
		h = cmul(h0.xy, vec2(e.x, -e.y)) + cmul(h0.zw, e);
	}
	vec2 kn = kl > 1e-6 ? k / kl : vec2(0.0);
	vec2 dx = mul_i(h) * kn.x;            // +i kx/k h
	vec2 dz = mul_i(h) * kn.y;
	vec2 dydx = mul_i(h) * k.x;           // i kx h
	vec2 dydz = mul_i(h) * k.y;
	vec2 dxx = -h * k.x * kn.x;           // i kx (i kx/k h)
	vec2 dzz = -h * k.y * kn.y;
	vec2 dxz = -h * k.x * kn.y;
	imageStore(spec_img, id, vec4(dx + mul_i(dz), h + mul_i(dydx)));
	imageStore(spec_img, ivec3(id.xy, id.z + 3), vec4(dydz + mul_i(dxx), dzz + mul_i(dxz)));
}
