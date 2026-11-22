#include "Art.h"
#include "Tft.h"

namespace {
const size_t MAX_JPEG = 12 * 1024;     // must match MaxArtBytes in the PC app's DeviceProtocol
const size_t CHUNK = 1024;           // bytes per chunk: ArtChunkBytes in the PC app's DeviceProtocol
const int MAX_CHUNKS = 16;

// The picture being put together.
uint8_t* jpeg = nullptr;               // allocated while chunks arrive, freed once decoded
char incoming[9] = "";                 // fingerprint of the picture being received
uint32_t got = 0;                      // bit per chunk received
int chunks = 0;
size_t jpegLen = 0;

// The picture ready to show.
LGFX_Sprite picture;
bool hasPicture = false;
char shown[9] = "";

int b64(char c) {
  if (c >= 'A' && c <= 'Z') return c - 'A';
  if (c >= 'a' && c <= 'z') return c - 'a' + 26;
  if (c >= '0' && c <= '9') return c - '0' + 52;
  if (c == '+') return 62;
  if (c == '/') return 63;
  return -1;
}

void dropIncoming() {
  free(jpeg);
  jpeg = nullptr;
  incoming[0] = 0;
  got = 0;
  chunks = 0;
  jpegLen = 0;
}

// All chunks are in: decode the JPEG into the picture (once), then free the JPEG.
void finish() {
  if (!hasPicture) {
    picture.setColorDepth(16);
    picture.setPsram(false);
    hasPicture = picture.createSprite(Art::SIZE, Art::SIZE) != nullptr;
  }
  if (hasPicture) {
    picture.fillScreen(TFT_BLACK);
    if (picture.drawJpg(jpeg, jpegLen, 0, 0, Art::SIZE, Art::SIZE)) strncpy(shown, incoming, sizeof(shown));
    else shown[0] = 0;   // not a JPEG this decoder understands: show the placeholder
  }
  dropIncoming();
}
} // namespace

namespace Art {

int decodeBase64(const char* in, uint8_t* out, size_t max) {
  size_t n = 0;
  uint32_t acc = 0;
  int bits = 0;
  for (const char* p = in; *p && *p != '='; p++) {
    int v = b64(*p);
    if (v < 0) return -1;
    acc = (acc << 6) | (uint32_t)v;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      if (n >= max) return -1;
      out[n++] = (uint8_t)(acc >> bits);
    }
  }
  return (int)n;
}

void chunk(JsonDocument& m) {
  const char* hash = m["h"] | "";
  int i = m["i"] | -1, n = m["n"] | 0;
  size_t len = m["len"] | 0;
  const char* data = m["d"] | "";
  if (!hash[0] || n < 1 || n > MAX_CHUNKS || i < 0 || i >= n || len == 0 || len > MAX_JPEG) return;
  if (!strncmp(hash, shown, sizeof(shown) - 1) && hasPicture) return;   // already showing it

  if (strncmp(hash, incoming, sizeof(incoming) - 1) || !jpeg) {   // a new picture: start over
    dropIncoming();
    jpeg = (uint8_t*)malloc(len);
    if (!jpeg) return;   // no room right now: the PC sends it again after the next song or reconnect
    strncpy(incoming, hash, sizeof(incoming) - 1);
    incoming[sizeof(incoming) - 1] = 0;
    chunks = n;
    jpegLen = len;
  }
  // Every chunk but the last is CHUNK bytes, so its place follows from its number.
  size_t at = (size_t)i * CHUNK;
  if (at >= jpegLen) return;
  int bytes = decodeBase64(data, jpeg + at, jpegLen - at);
  if (bytes < 0) { dropIncoming(); return; }
  got |= 1u << i;
  if (got == (chunks >= 32 ? 0xFFFFFFFFu : (1u << chunks) - 1)) finish();
}

bool ready(const char* hash) {
  return hasPicture && hash && hash[0] && !strncmp(hash, shown, sizeof(shown) - 1);
}

void draw(TftGfx& g, int x, int y) {
  if (!hasPicture) return;
  picture.pushSprite(&g.sprite(), x, y - g.top());
}

} // namespace Art
