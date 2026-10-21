// ValOutcast overlay, renderer side. See vo_overlay.h and protocol/FRAME.md.
#include "tr_local.h"
#include "vo_overlay.h"

#include <fcntl.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>

namespace {

const char *FRAME_PATH = "/dev/shm/valoutcast_frame";
const size_t HEADER_SIZE = 32;

struct FrameHeader {
	uint32_t magic;     // 'VOFR'
	uint32_t version;   // 1
	uint32_t width;
	uint32_t height;
	uint32_t frame;     // even = complete frame number * 2, odd = being written
	uint32_t reserved[3];
};

PFNGLBLENDFUNCSEPARATEPROC s_blendFuncSeparate = NULL;
bool s_triedBlendLookup = false;
PFNGLGENFRAMEBUFFERSPROC s_genFramebuffers = NULL;
PFNGLBINDFRAMEBUFFERPROC s_bindFramebuffer = NULL;
PFNGLISFRAMEBUFFERPROC s_isFramebuffer = NULL;
PFNGLFRAMEBUFFERRENDERBUFFERPROC s_framebufferRenderbuffer = NULL;
PFNGLCHECKFRAMEBUFFERSTATUSPROC s_checkFramebufferStatus = NULL;
PFNGLGENRENDERBUFFERSPROC s_genRenderbuffers = NULL;
PFNGLBINDRENDERBUFFERPROC s_bindRenderbuffer = NULL;
PFNGLRENDERBUFFERSTORAGEPROC s_renderbufferStorage = NULL;
GLuint s_fbo = 0, s_colorRb = 0, s_depthRb = 0;
int s_fboWidth = 0, s_fboHeight = 0;
bool s_fboFailed = false;
int s_enabled = -1;
int s_fd = -1;
unsigned char *s_map = NULL;
size_t s_mapSize = 0;
int s_width = 0, s_height = 0;
uint32_t s_counter = 0;
long s_lastPublishMs = 0;
long s_minPublishMs = -1;   // from VO_OVERLAY_FPS: the picture is copied out at most this often

bool LoadFramebufferFunctions()
{
	s_genFramebuffers = (PFNGLGENFRAMEBUFFERSPROC)ri.GL_GetProcAddress("glGenFramebuffers");
	s_bindFramebuffer = (PFNGLBINDFRAMEBUFFERPROC)ri.GL_GetProcAddress("glBindFramebuffer");
	s_isFramebuffer = (PFNGLISFRAMEBUFFERPROC)ri.GL_GetProcAddress("glIsFramebuffer");
	s_framebufferRenderbuffer = (PFNGLFRAMEBUFFERRENDERBUFFERPROC)ri.GL_GetProcAddress("glFramebufferRenderbuffer");
	s_checkFramebufferStatus = (PFNGLCHECKFRAMEBUFFERSTATUSPROC)ri.GL_GetProcAddress("glCheckFramebufferStatus");
	s_genRenderbuffers = (PFNGLGENRENDERBUFFERSPROC)ri.GL_GetProcAddress("glGenRenderbuffers");
	s_bindRenderbuffer = (PFNGLBINDRENDERBUFFERPROC)ri.GL_GetProcAddress("glBindRenderbuffer");
	s_renderbufferStorage = (PFNGLRENDERBUFFERSTORAGEPROC)ri.GL_GetProcAddress("glRenderbufferStorage");
	return s_genFramebuffers && s_bindFramebuffer && s_isFramebuffer && s_framebufferRenderbuffer &&
	       s_checkFramebufferStatus && s_genRenderbuffers && s_bindRenderbuffer && s_renderbufferStorage;
}

// Makes (or remakes) the off-screen buffer: RGBA color plus depth and stencil, as big as the window.
bool EnsureFramebuffer(int w, int h)
{
	if (s_fboFailed) return false;
	if (!s_genFramebuffers && !LoadFramebufferFunctions()) {
		s_fboFailed = true;
		return false;
	}
	if (s_fbo && s_isFramebuffer(s_fbo) && s_fboWidth == w && s_fboHeight == h) return true;

	if (!s_fbo || !s_isFramebuffer(s_fbo)) {
		s_genFramebuffers(1, &s_fbo);
		s_genRenderbuffers(1, &s_colorRb);
		s_genRenderbuffers(1, &s_depthRb);
	}
	s_bindRenderbuffer(GL_RENDERBUFFER, s_colorRb);
	s_renderbufferStorage(GL_RENDERBUFFER, GL_RGBA8, w, h);
	s_bindRenderbuffer(GL_RENDERBUFFER, s_depthRb);
	s_renderbufferStorage(GL_RENDERBUFFER, GL_DEPTH24_STENCIL8, w, h);
	s_bindFramebuffer(GL_FRAMEBUFFER, s_fbo);
	s_framebufferRenderbuffer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_RENDERBUFFER, s_colorRb);
	s_framebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_RENDERBUFFER, s_depthRb);
	if (s_checkFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) {
		s_bindFramebuffer(GL_FRAMEBUFFER, 0);
		s_fboFailed = true;
		ri.Printf(PRINT_ALL, "ValOutcast: could not make the off-screen buffer; drawing to the window instead\n");
		return false;
	}
	s_fboWidth = w;
	s_fboHeight = h;
	return true;
}

bool OpenFrameFile(int w, int h)
{
	if (s_map) {
		munmap(s_map, s_mapSize);
		s_map = NULL;
	}
	if (s_fd < 0) s_fd = open(FRAME_PATH, O_RDWR | O_CREAT, 0666);
	if (s_fd < 0) return false;
	s_mapSize = HEADER_SIZE + (size_t)w * h * 4;
	if (ftruncate(s_fd, (off_t)s_mapSize) != 0) return false;
	void *p = mmap(NULL, s_mapSize, PROT_READ | PROT_WRITE, MAP_SHARED, s_fd, 0);
	if (p == MAP_FAILED) return false;
	s_map = (unsigned char *)p;
	s_width = w;
	s_height = h;
	FrameHeader *hd = (FrameHeader *)s_map;
	hd->magic = 0x52464F56;  // "VOFR" in little endian
	hd->version = 1;
	hd->width = (uint32_t)w;
	hd->height = (uint32_t)h;
	hd->frame = 0;
	return true;
}

} // namespace

bool VO_OverlayEnabled(void)
{
	if (s_enabled < 0) s_enabled = getenv("VO_OVERLAY") ? 1 : 0;
	return s_enabled == 1;
}

void VO_OverlayBeginFrame(void)
{
	if (!VO_OverlayEnabled()) return;
	if (EnsureFramebuffer(glConfig.vidWidth, glConfig.vidHeight)) {
		s_bindFramebuffer(GL_FRAMEBUFFER, s_fbo);
	}
}

void VO_BlendFunc(GLenum src, GLenum dst)
{
	if (!VO_OverlayEnabled()) {
		qglBlendFunc(src, dst);
		return;
	}
	if (!s_triedBlendLookup) {
		s_triedBlendLookup = true;
		s_blendFuncSeparate = (PFNGLBLENDFUNCSEPARATEPROC)ri.GL_GetProcAddress("glBlendFuncSeparate");
	}
	if (!s_blendFuncSeparate) {
		qglBlendFunc(src, dst);
		return;
	}
	// Colors blend as the game asks. The alpha channel records "how much of the scene is covered here":
	//  - normal alpha blending adds coverage,
	//  - "replace" (one, zero) sets it,
	//  - additive glow and multiply passes leave it alone (additive light then shows up as plain added light).
	GLenum srcA = GL_ZERO, dstA = GL_ONE;
	if (dst == GL_ONE_MINUS_SRC_ALPHA) {
		srcA = GL_ONE;
		dstA = GL_ONE_MINUS_SRC_ALPHA;
	} else if (dst == GL_ZERO && src == GL_ONE) {
		srcA = GL_ONE;
		dstA = GL_ZERO;
	}
	s_blendFuncSeparate(src, dst, srcA, dstA);
}

// The frame is drawn with premultiplied alpha (colors already scaled by coverage). Valheim draws it with ordinary
// alpha blending, so convert in place. Light with no coverage (additive glow) becomes a see-through colored pixel.
static void ToStraightAlpha(unsigned char *px, size_t pixelCount)
{
	static unsigned int inv[256];
	static bool ready = false;
	if (!ready) {
		inv[0] = 0;
		for (unsigned int a = 1; a < 256; a++) inv[a] = (255u << 16) / a;
		ready = true;
	}
	for (size_t i = 0; i < pixelCount; i++, px += 4) {
		unsigned int r = px[0], g = px[1], b = px[2], a = px[3];
		unsigned int m = r > g ? r : g;
		if (b > m) m = b;
		if (m == 0 && a == 0) continue;
		if (a < m) a = m;
		if (a < 255) {
			const unsigned int k = inv[a];
			r = (r * k) >> 16; g = (g * k) >> 16; b = (b * k) >> 16;
			px[0] = (unsigned char)(r > 255 ? 255 : r);
			px[1] = (unsigned char)(g > 255 ? 255 : g);
			px[2] = (unsigned char)(b > 255 ? 255 : b);
		}
		px[3] = (unsigned char)a;
	}
}

static long NowMs(void)
{
	struct timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return ts.tv_sec * 1000L + ts.tv_nsec / 1000000L;
}

void VO_OverlayPublish(void)
{
	if (!VO_OverlayEnabled()) return;
	if (s_minPublishMs < 0) {
		const char *fps = getenv("VO_OVERLAY_FPS");
		int n = fps ? atoi(fps) : 45;
		s_minPublishMs = n > 0 ? 1000 / n : 0;
	}
	const long now = NowMs();
	if (now - s_lastPublishMs < s_minPublishMs) return;   // copying a frame out is the expensive part, so not every frame
	s_lastPublishMs = now;
	const int w = glConfig.vidWidth, h = glConfig.vidHeight;
	if (w <= 0 || h <= 0) return;
	if (!s_map || w != s_width || h != s_height) {
		if (!OpenFrameFile(w, h)) return;
	}
	FrameHeader *hd = (FrameHeader *)s_map;
	__atomic_store_n(&hd->frame, s_counter * 2 + 1, __ATOMIC_RELEASE);   // odd: being written
	qglPixelStorei(GL_PACK_ALIGNMENT, 1);
	qglReadPixels(0, 0, w, h, GL_RGBA, GL_UNSIGNED_BYTE, s_map + HEADER_SIZE);
	ToStraightAlpha(s_map + HEADER_SIZE, (size_t)w * h);
	s_counter++;
	__atomic_store_n(&hd->frame, s_counter * 2, __ATOMIC_RELEASE);        // even: complete
}
