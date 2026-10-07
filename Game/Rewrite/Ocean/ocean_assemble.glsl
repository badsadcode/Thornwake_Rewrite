#[compute]
#version 450
// FFT ocean, step 3 (OceanFft.cs): the transformed fields into the textures the water shader samples, per cascade:
//   disp  = (lambda  Dx, Dy, lambda  Dz, foam)   deriv = (dDy/dx, dDy/dz, lambda  dDx/dx, lambda  dDz/dz)
// The foam lingers: born where the surface folds (Jacobian below ~0.5), fading over a few seconds.
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba32f) uniform readonly image2DArray spec_img;
layout(set = 0, binding = 1, rgba16f) uniform restrict image2DArray disp_img;
layout(set = 0, binding = 2, rgba16f) uniform writeonly image2DArray deriv_img;

layout(push_constant, std430) uniform Params {
	vec4 chop;  // lambda  per cascade; w: dt (s)
	vec4 foam;  // x: fold threshold, y: birth gain, z: fade per second, w: N
} pc;

void main() {
	ivec3 id = ivec3(gl_GlobalInvocationID);
	int n = int(pc.foam.w);
	if (id.x >= n || id.y >= n) return;
	// The centred spectrum: (-1)^(x+y) after the transform.
	float sgn = ((id.x + id.y) & 1) == 0 ? 1.0 : -1.0;
	vec4 a = imageLoad(spec_img, id) * sgn;
	vec4 b = imageLoad(spec_img, ivec3(id.xy, id.z + 3)) * sgn;
	float lambda = id.z == 0 ? pc.chop.x : (id.z == 1 ? pc.chop.y : pc.chop.z);
	float dxx = lambda * b.y, dzz = lambda * b.z, dxz = lambda * b.w;
	float jacobian = (1.0 + dxx) * (1.0 + dzz) - dxz * dxz;
	float prev = imageLoad(disp_img, id).w;
	float born = clamp((pc.foam.x - jacobian) * pc.foam.y, 0.0, 1.0);
	float foam = max(prev * exp(-pc.chop.w * pc.foam.z), born);
	imageStore(disp_img, id, vec4(lambda * a.x, a.z, lambda * a.y, foam));
	imageStore(deriv_img, id, vec4(a.w, b.x, dxx, dzz));
}
