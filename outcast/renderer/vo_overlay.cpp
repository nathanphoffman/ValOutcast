// ValOutcast overlay, renderer side. See vo_overlay.h and protocol/FRAME.md.
#include "tr_local.h"
#include "vo_overlay.h"

#include <fcntl.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
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
int s_enabled = -1;
int s_fd = -1;
unsigned char *s_map = NULL;
size_t s_mapSize = 0;
int s_width = 0, s_height = 0;
uint32_t s_counter = 0;

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

void VO_OverlayPublish(void)
{
	if (!VO_OverlayEnabled()) return;
	const int w = glConfig.vidWidth, h = glConfig.vidHeight;
	if (w <= 0 || h <= 0) return;
	if (!s_map || w != s_width || h != s_height) {
		if (!OpenFrameFile(w, h)) return;
	}
	FrameHeader *hd = (FrameHeader *)s_map;
	__atomic_store_n(&hd->frame, s_counter * 2 + 1, __ATOMIC_RELEASE);   // odd: being written
	qglPixelStorei(GL_PACK_ALIGNMENT, 1);
	qglReadPixels(0, 0, w, h, GL_RGBA, GL_UNSIGNED_BYTE, s_map + HEADER_SIZE);
	s_counter++;
	__atomic_store_n(&hd->frame, s_counter * 2, __ATOMIC_RELEASE);        // even: complete
}
