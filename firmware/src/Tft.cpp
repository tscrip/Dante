#include "Tft.h"

namespace {

#ifdef DANTE_SIMULATOR
}   // namespace (the include below must not land inside it)
#include <lgfx/v1/panel/Panel_FrameBufferBase.hpp>
namespace {
// Simulator: the same LovyanGFX device, but the panel is a frame buffer in memory (no SPI). Everything
// above it (sprites, rotation, pushImageDMA) is LovyanGFX's own code; the simulator reads the result.
class SimLight : public lgfx::ILight {
 public:
  uint8_t level = 0;
  bool init(uint8_t b) override { level = b; return true; }
  void setBrightness(uint8_t b) override { level = b; }
};

class SimPanel : public lgfx::Panel_FrameBufferBase {
  lgfx::Bus_NULL nullBus;
  uint8_t* pixels = nullptr;
 public:
  SimPanel() {
    auto c = config();
    c.panel_width = 320;            // the ST7796 is a portrait panel, used landscape (rotation 1)
    c.panel_height = 480;
    c.pin_cs = -1;
    c.pin_rst = -1;
    c.readable = true;
    config(c);
    setBus(&nullBus);
    _write_depth = lgfx::rgb565_2Byte;
    _read_depth = lgfx::rgb565_2Byte;
    pixels = (uint8_t*)calloc(320 * 480, 2);
    _lines_buffer = new uint8_t*[480];
    for (int y = 0; y < 480; y++) _lines_buffer[y] = pixels + y * 320 * 2;
  }
};

class Panel : public lgfx::LGFX_Device {
  SimPanel panel;
 public:
  SimLight light;
  Panel() {
    panel.setLight(&light);
    setPanel(&panel);
  }
};
#else
// ST7796 480x320 on SPI2 (FSPI on the S3, HSPI on the classic ESP32). Pins and panel settings are in
// config.h. On the yellow board the GT911 touch panel is part of the same LovyanGFX device.
class Panel : public lgfx::LGFX_Device {
  lgfx::Panel_ST7796 panel;
  lgfx::Bus_SPI bus;
  lgfx::Light_PWM light;
#if HAS_TOUCH
  lgfx::Touch_GT911 touchPanel;
#endif

 public:
  Panel() {
    {
      auto c = bus.config();
      c.spi_host = TFT_SPI_HOST;
      c.spi_mode = 0;
      c.freq_write = TFT_SPI_HZ;
      c.freq_read = 16000000;
      c.spi_3wire = false;
      c.use_lock = true;
      c.dma_channel = SPI_DMA_CH_AUTO;
      c.pin_sclk = TFT_PIN_SCK;
      c.pin_mosi = TFT_PIN_MOSI;
      c.pin_miso = TFT_PIN_MISO;
      c.pin_dc = TFT_PIN_DC;
      bus.config(c);
      panel.setBus(&bus);
    }
    {
      auto c = panel.config();
      c.pin_cs = TFT_PIN_CS;
      c.pin_rst = TFT_PIN_RST;
      c.pin_busy = -1;
      c.panel_width = 320;
      c.panel_height = 480;
      c.offset_x = 0;
      c.offset_y = 0;
      c.offset_rotation = 0;
      c.readable = false;
      c.invert = TFT_INVERT;
      c.rgb_order = TFT_BGR;
      c.dlen_16bit = false;
      c.bus_shared = false;
      panel.config(c);
    }
    {
      auto c = light.config();
      c.pin_bl = TFT_PIN_BL;
      c.invert = false;
      c.freq = 44100;
      c.pwm_channel = 7;
      light.config(c);
      panel.setLight(&light);
    }
#if HAS_TOUCH
    {
      // GT911 on its own I2C bus. The panel is 320x480 in portrait, like the screen; LovyanGFX turns
      // touch points with the screen (setRotation), so x, y come out in the same pixels DANTE draws in.
      auto c = touchPanel.config();
      c.x_min = 0;
      c.x_max = 319;
      c.y_min = 0;
      c.y_max = 479;
      // No INT and no RST here, on purpose:
      //  * with INT set, LovyanGFX only reads the chip while INT is low, so a board that wires INT
      //    differently never reports a touch. Without it, the chip is simply read every poll.
      //  * with RST set, LovyanGFX talks to the chip ~5 ms after the reset, but the GT911 needs
      //    ~50-100 ms to wake up; resetTouchChip() below does the reset with the proper waits.
      c.pin_int = -1;
      c.pin_rst = -1;
      c.bus_shared = false;
      c.offset_rotation = TOUCH_OFFSET_ROTATION;
      c.i2c_port = TOUCH_I2C_PORT;
      c.i2c_addr = 0x5D;          // the GT911 answers at 0x5D or 0x14; LovyanGFX tries both
      c.pin_sda = TOUCH_PIN_SDA;
      c.pin_scl = TOUCH_PIN_SCL;
      c.freq = 400000;
      touchPanel.config(c);
      panel.setTouch(&touchPanel);
    }
#endif
    setPanel(&panel);
  }
};
#endif

Panel tft;

#if HAS_TOUCH && !defined(DANTE_SIMULATOR)
// The GT911 touch chip: did it answer, and when to try again if it didn't.
bool touchFound = false;
uint8_t touchTries = 0;
uint32_t touchRetryAt = 0;
const uint8_t TOUCH_MAX_TRIES = 5;

// Resets the GT911 the way its datasheet asks. INT held low while RST goes high selects I2C address
// 0x5D (LovyanGFX also tries 0x14, in case INT is wired differently), and the chip then needs about
// 50-100 ms before it answers on I2C.
void resetTouchChip() {
  pinMode(TOUCH_PIN_INT, OUTPUT);
  digitalWrite(TOUCH_PIN_INT, LOW);
  pinMode(TOUCH_PIN_RST, OUTPUT);
  digitalWrite(TOUCH_PIN_RST, LOW);
  delay(10);
  digitalWrite(TOUCH_PIN_RST, HIGH);
  delay(10);
  pinMode(TOUCH_PIN_INT, INPUT);   // let go: the chip drives it from now on
  delay(100);
}

// Wakes the touch chip and checks that it answers on I2C.
void startTouch() {
  touchTries++;
  resetTouchChip();
  touchFound = tft.touch() && tft.touch()->init();   // init() only tries again if it failed before
}
#endif

// Two ways to draw a frame:
//  * With PSRAM (e.g. N16R8 / N8R8 modules): the whole frame is drawn once into PSRAM, compared with
//    the previous one, and only the changed part of each 16-row band is sent, by DMA from a small
//    internal buffer while the next band is prepared. A blink or a glance sends a fraction of the screen.
//  * Without (e.g. the yellow ESP32-3248S035C board): the frame is drawn in strips of TFT_STRIP_H rows
//    (the scene is drawn once per strip). Each strip is split into columns that are compared with what
//    the screen shows (by hash), and only the changed span is sent, by DMA. There are two strip buffers,
//    so the next strip is drawn while the previous one is still on its way to the screen.
LGFX_Sprite full(&tft);          // PSRAM mode: the frame being drawn
TftGfx fullCanvas(full);
uint16_t* previous = nullptr;    // PSRAM mode: what the screen shows now
uint16_t* dmaBuf[2] = {};        // PSRAM mode: internal, DMA-capable band buffers
const int BAND = 16;
bool psramMode = false;
bool forceAll = true;            // send everything next frame (after begin / flip)

LGFX_Sprite strips[2] = { LGFX_Sprite(&tft), LGFX_Sprite(&tft) };   // strip mode: two buffers
TftGfx canvases[2] = { TftGfx(strips[0]), TftGfx(strips[1]) };
int stripBuffers = 0;            // 2 normally; 1 if there was only room for one
const int STRIPS = (TFT_H + TFT_STRIP_H - 1) / TFT_STRIP_H;
const int CHUNK_W = 32;          // strip mode: columns compared at a time
const int CHUNKS = TFT_W / CHUNK_W;
static_assert(TFT_W % CHUNK_W == 0, "the screen width must be a whole number of chunks");
uint32_t sent[STRIPS][CHUNKS];   // strip mode: hash of what each part of the screen shows now
bool started = false, awake = false;

// Frame-rate statistics over the last couple of seconds.
uint32_t statFrom = 0, statFrames = 0, statBytes = 0;
float fps = 0, kbPerFrame = 0;
uint8_t brightness = 200;
bool flipped = false;

// u8g2 fonts wrapped for LovyanGFX, made on first use.
lgfx::U8g2font* fontFor(const uint8_t* data) {
  static const uint8_t* keys[12] = {};
  static lgfx::U8g2font* fonts[12] = {};
  for (int i = 0; i < 12; i++) {
    if (keys[i] == data) return fonts[i];
    if (!keys[i]) { keys[i] = data; fonts[i] = new lgfx::U8g2font(data); return fonts[i]; }
  }
  return fonts[11];
}
} // namespace

// ---- TftGfx ------------------------------------------------------------------------------
void TftGfx::setTransform(float scaleX, float scaleY, float offX, float offY) {
  sx = scaleX; sy = scaleY; ox = offX; oy = offY;
}

void TftGfx::setColors(uint32_t f, uint32_t b, uint32_t a) { fg = f; bg = b; accent = a; }

void TftGfx::setFont(const uint8_t* f) { font = f; }

void TftGfx::drawStr(int x, int y, const char* str) {
  if (!font) return;
  s.setFont(fontFor(font));
  s.setTextSize(unit());
  s.setTextDatum(lgfx::textdatum_t::baseline_left);
  s.setTextColor(color());
  s.drawString(str, X(x), Y(y));
}

int TftGfx::getStrWidth(const char* str) {
  if (!font) return 0;
  s.setFont(fontFor(font));
  s.setTextSize(1);
  return s.textWidth(str);
}

void TftGfx::drawPixel(int x, int y) { s.fillRect(X(x), Y(y), X(x + 1) - X(x), Y(y + 1) - Y(y), color()); }

void TftGfx::drawLine(int x0, int y0, int x1, int y1) {
  float w = unit();
  if (w < 1.5f) s.drawLine(Xc(x0), Yc(y0), Xc(x1), Yc(y1), color());
  else s.drawWideLine(Xc(x0), Yc(y0), Xc(x1), Yc(y1), w / 2, color());
}

void TftGfx::drawHLine(int x, int y, int w) { drawBox(x, y, w, 1); }
void TftGfx::drawVLine(int x, int y, int h) { drawBox(x, y, 1, h); }

void TftGfx::drawBox(int x, int y, int w, int h) {
  if (w <= 0 || h <= 0) return;
  if (drawColor == 2) invert(X(x), Y(y), X(x + w) - X(x), Y(y + h) - Y(y));
  else s.fillRect(X(x), Y(y), X(x + w) - X(x), Y(y + h) - Y(y), color());
}

void TftGfx::drawRBox(int x, int y, int w, int h, int r) {
  if (w <= 0 || h <= 0) return;
  s.fillRoundRect(X(x), Y(y), X(x + w) - X(x), Y(y + h) - Y(y), (int)lroundf(r * unit()), color());
}

void TftGfx::drawRFrame(int x, int y, int w, int h, int r) {
  int px = X(x), py = Y(y), pw = X(x + w) - px, ph = Y(y + h) - py, pr = (int)lroundf(r * unit());
  int t = (int)lroundf(unit());
  if (t < 1) t = 1;
  for (int i = 0; i < t && pw > 2 * i && ph > 2 * i; i++)
    s.drawRoundRect(px + i, py + i, pw - 2 * i, ph - 2 * i, pr > i ? pr - i : 0, color());
}

void TftGfx::drawDisc(int x, int y, int r) {
  s.fillEllipse(Xc(x), Yc(y), (int)lroundf((r + 0.5f) * sx), (int)lroundf((r + 0.5f) * sy), color());
}

void TftGfx::drawCircle(int x, int y, int r, uint8_t opt) {
  int rx = (int)lroundf((r + 0.5f) * sx), ry = (int)lroundf((r + 0.5f) * sy);
  int t = (int)lroundf(unit());
  if (t < 1) t = 1;
  // Quadrants as u8g2 names them; LovyanGFX angles start at 3 o'clock and run clockwise.
  struct { uint8_t bit; float a0, a1; } q[4] = {
    { U8G2_DRAW_UPPER_RIGHT, 270, 360 }, { U8G2_DRAW_UPPER_LEFT, 180, 270 },
    { U8G2_DRAW_LOWER_LEFT, 90, 180 },   { U8G2_DRAW_LOWER_RIGHT, 0, 90 },
  };
  for (auto& k : q)
    if (opt & k.bit) s.fillEllipseArc(Xc(x), Yc(y), rx - t, rx, ry - t, ry, k.a0, k.a1, color());
}

void TftGfx::drawTriangle(int x0, int y0, int x1, int y1, int x2, int y2) {
  s.fillTriangle(X(x0), Y(y0), X(x1), Y(y1), X(x2), Y(y2), color());
}

void TftGfx::drawFilledEllipse(int x, int y, int rx, int ry) {
  s.fillEllipse(Xc(x), Yc(y), (int)lroundf((rx + 0.5f) * sx), (int)lroundf((ry + 0.5f) * sy), color());
}

void TftGfx::setClipWindow(int x0, int y0, int x1, int y1) {
  s.setClipRect(X(x0), Y(y0), X(x1) - X(x0), Y(y1) - Y(y0));
}

void TftGfx::setMaxClipWindow() { s.clearClipRect(); }

// ---- fractional, anti-aliased shapes (the face's eyes) ------------------------------------
void TftGfx::fillRectF(float x, float y, float w, float h) {
  int px = (int)lroundf(Xf(x)), py = (int)lroundf(Yf(y));
  s.fillRect(px, py, (int)lroundf(Xf(x + w)) - px, (int)lroundf(Yf(y + h)) - py, color());
}

void TftGfx::fillRoundRectF(float x, float y, float w, float h, float r) {
  int px = (int)lroundf(Xf(x)), py = (int)lroundf(Yf(y));
  int pw = (int)lroundf(Xf(x + w)) - px, ph = (int)lroundf(Yf(y + h)) - py;
  int pr = (int)lroundf(r * unit());
  if (pr > pw / 2) pr = pw / 2;
  if (pr > ph / 2) pr = ph / 2;
  if (pw <= 0 || ph <= 0) return;
  s.fillSmoothRoundRect(px, py, pw, ph, pr, color());
}

void TftGfx::fillTriangleF(float x0, float y0, float x1, float y1, float x2, float y2) {
  float ax = Xf(x0), ay = Yf(y0), bx = Xf(x1), by = Yf(y1), cx = Xf(x2), cy = Yf(y2);
  uint32_t c = color();
  s.fillTriangle(lroundf(ax), lroundf(ay), lroundf(bx), lroundf(by), lroundf(cx), lroundf(cy), c);
  // Anti-alias the slanted edges (the eyelid cuts) with thin smooth lines in the same colour.
  s.drawWideLine(lroundf(ax), lroundf(ay), lroundf(cx), lroundf(cy), 0.6f, c);
  s.drawWideLine(lroundf(bx), lroundf(by), lroundf(cx), lroundf(cy), 0.6f, c);
}

void TftGfx::fillEllipseF(float cx, float cy, float rx, float ry) {
  float pcx = Xf(cx), pcy = Yf(cy), prx = rx * sx, pry = ry * sy;
  uint32_t c = color();
  s.fillEllipse(lroundf(pcx), lroundf(pcy), lroundf(prx), lroundf(pry), c);
  // Smooth the upper arc (the edge that cuts into the eye for "happy" cheeks).
  const int SEG = 24;
  float px = pcx - prx, py = pcy;
  for (int i = 1; i <= SEG; i++) {
    float a = (float)M_PI + (float)M_PI * i / SEG;   // 180 -> 360 degrees: the top half
    float nx = pcx + cosf(a) * prx, ny = pcy + sinf(a) * pry;
    s.drawWideLine(lroundf(px), lroundf(py), lroundf(nx), lroundf(ny), 0.6f, c);
    px = nx; py = ny;
  }
}

// XOR on a monochrome screen = inverted colours here (16-bit pixels: flip every bit).
void TftGfx::invert(int x, int y, int w, int h) {
  int x1 = x + w, y1 = y + h;
  if (x < 0) x = 0;
  if (y < 0) y = 0;
  if (x1 > s.width()) x1 = s.width();
  if (y1 > s.height()) y1 = s.height();
  uint16_t* buf = (uint16_t*)s.getBuffer();
  if (!buf) return;
  for (int row = y; row < y1; row++)
    for (int col = x; col < x1; col++) buf[row * s.width() + col] ^= 0xFFFF;
}

// ---- Tft -----------------------------------------------------------------------------------
namespace {

// PSRAM: a full-screen sprite, the previous frame and two internal DMA band buffers.
bool allocPsram() {
  if (!psramFound()) return false;
  full.setColorDepth(16);
  full.setPsram(true);
  if (!full.createSprite(TFT_W, TFT_H)) return false;
  previous = (uint16_t*)heap_caps_malloc(TFT_W * TFT_H * 2, MALLOC_CAP_SPIRAM);
  for (auto& b : dmaBuf) b = (uint16_t*)heap_caps_malloc(TFT_W * BAND * 2, MALLOC_CAP_DMA | MALLOC_CAP_INTERNAL);
  if (previous && dmaBuf[0] && dmaBuf[1]) return true;
  full.deleteSprite();
  heap_caps_free(previous); previous = nullptr;
  for (auto& b : dmaBuf) { heap_caps_free(b); b = nullptr; }
  return false;
}

// Sends what changed between `full` and `previous`, band by band, and updates `previous`.
uint32_t pushChanged() {
  const uint16_t* cur = (const uint16_t*)full.getBuffer();
  uint32_t bytes = 0;
  int which = 0;
  tft.startWrite();
  for (int y0 = 0; y0 < TFT_H; y0 += BAND) {
    int rows = TFT_H - y0 < BAND ? TFT_H - y0 : BAND;
    int x0 = TFT_W, x1 = -1;
    if (forceAll) { x0 = 0; x1 = TFT_W - 1; }
    else {
      // Compare two pixels at a time; find the leftmost and rightmost change in the band.
      const int words = TFT_W / 2;
      for (int r = 0; r < rows; r++) {
        const uint32_t* a = (const uint32_t*)(cur + (y0 + r) * TFT_W);
        const uint32_t* b = (const uint32_t*)(previous + (y0 + r) * TFT_W);
        int i = 0;
        while (i < words && a[i] == b[i]) i++;
        if (i == words) continue;
        int j = words - 1;
        while (a[j] == b[j]) j--;
        if (i * 2 < x0) x0 = i * 2;
        if (j * 2 + 1 > x1) x1 = j * 2 + 1;
      }
    }
    if (x1 < 0) continue;   // nothing changed in this band

    int w = x1 - x0 + 1;
    uint16_t* buf = dmaBuf[which];
    which ^= 1;
    // The other buffer may still be on its way out; this one finished before that one started.
    for (int r = 0; r < rows; r++) {
      const uint16_t* src = cur + (y0 + r) * TFT_W + x0;
      memcpy(buf + r * w, src, w * 2);
      memcpy(previous + (y0 + r) * TFT_W + x0, src, w * 2);
    }
    tft.pushImageDMA(x0, y0, w, rows, (const lgfx::swap565_t*)buf);
    bytes += w * rows * 2;
  }
  tft.waitDMA();
  tft.endWrite();
  forceAll = false;
  return bytes;
}

// Hash of one CHUNK_W-wide column block of a strip.
uint32_t hashChunk(const uint16_t* buf, int rows, int chunk) {
  uint32_t h = 2166136261u;
  for (int r = 0; r < rows; r++) {
    const uint32_t* p = (const uint32_t*)(buf + r * TFT_W + chunk * CHUNK_W);
    for (int i = 0; i < CHUNK_W / 2; i++) h = (h ^ p[i]) * 16777619u;
  }
  return h;
}

uint32_t pushStrips(const std::function<void(TftGfx& g)>& draw) {
  uint32_t bytes = 0;
  int cur = 0;   // the buffer to draw into; the other one may still be going out by DMA
  tft.startWrite();
  for (int i = 0; i < STRIPS; i++) {
    int top = i * TFT_STRIP_H;
    int rows = TFT_H - top < TFT_STRIP_H ? TFT_H - top : TFT_STRIP_H;
    if (stripBuffers < 2) tft.waitDMA();   // only one buffer: it must be sent before drawing again
    LGFX_Sprite& s = strips[cur];
    TftGfx& g = canvases[cur];
    s.clearClipRect();
    s.fillScreen(TFT_BLACK);
    g.setStripTop(top);
    g.setDrawColor(1);
    draw(g);
    s.clearClipRect();

    // Which columns changed? Send only the span from the first to the last changed block.
    uint16_t* buf = (uint16_t*)s.getBuffer();
    int first = CHUNKS, last = -1;
    for (int c = 0; c < CHUNKS; c++) {
      uint32_t h = hashChunk(buf, rows, c);
      if (h == sent[i][c] && !forceAll) continue;
      sent[i][c] = h;
      if (c < first) first = c;
      last = c;
    }
    if (last < 0) continue;   // nothing changed in this strip

    int x0 = first * CHUNK_W, w = (last - first + 1) * CHUNK_W;
    // DMA needs the span as one block: move its rows together, in place, at the start of the buffer
    // (each row moves down or stays, so nothing is overwritten before it is copied).
    if (w < TFT_W)
      for (int r = 0; r < rows; r++) memmove(buf + r * w, buf + r * TFT_W + x0, w * 2);
    // pushImageDMA waits for the previous transfer (from the other buffer) before starting this one.
    tft.pushImageDMA(x0, top, w, rows, (const lgfx::swap565_t*)buf);
    bytes += w * rows * 2;
    if (stripBuffers == 2) cur ^= 1;
  }
  tft.waitDMA();
  tft.endWrite();
  forceAll = false;
  return bytes;
}
} // namespace

namespace Tft {

bool begin() {
  if (!started) {
#if HAS_TOUCH && !defined(DANTE_SIMULATOR)
    resetTouchChip();   // before tft.init(), which starts the touch panel too
    touchTries = 1;
#endif
    tft.init();
#if HAS_TOUCH && !defined(DANTE_SIMULATOR)
    touchFound = tft.touch() && tft.touch()->init();
#endif
    psramMode = allocPsram();
    if (!psramMode) {
      // Two strip buffers if there is room (smoother), otherwise one.
      stripBuffers = 0;
      for (auto& s : strips) {
        s.setColorDepth(16);
        s.setPsram(false);
        if (!s.createSprite(TFT_W, TFT_STRIP_H)) break;
        stripBuffers++;
      }
      if (stripBuffers == 0) return false;
    }
    started = true;
  } else {
    tft.wakeup();
  }
  tft.setRotation(flipped ? 3 : 1);
  tft.fillScreen(TFT_BLACK);
  tft.setBrightness(brightness);
  forceAll = true;
  awake = true;
  return true;
}

void end() {
  if (!started || !awake) return;
  tft.setBrightness(0);
  tft.fillScreen(TFT_BLACK);
  tft.sleep();
  awake = false;
}

bool active() { return started && awake; }

void setBrightness(uint8_t b) {
  brightness = b;
  if (active()) tft.setBrightness(b);
}

void setFlip(bool upsideDown) {
  flipped = upsideDown;
  if (!active()) return;
  tft.setRotation(flipped ? 3 : 1);
  forceAll = true;   // everything has to be sent again
}

void frame(const std::function<void(TftGfx& g)>& draw) {
  if (!active()) return;
  uint32_t bytes;
  if (psramMode) {
    full.clearClipRect();
    full.fillScreen(TFT_BLACK);
    fullCanvas.setStripTop(0);
    fullCanvas.setDrawColor(1);
    draw(fullCanvas);
    full.clearClipRect();
    bytes = pushChanged();
  } else {
    bytes = pushStrips(draw);
  }

  uint32_t now = millis();
  if (!statFrom) statFrom = now;
  statFrames++;
  statBytes += bytes;
  if (now - statFrom >= 2000) {
    fps = statFrames * 1000.0f / (now - statFrom);
    kbPerFrame = statBytes / 1024.0f / statFrames;
    statFrom = now; statFrames = 0; statBytes = 0;
  }
}

#ifdef DANTE_SIMULATOR
bool simSnapshot(uint16_t* out, uint8_t& backlight) {
  if (!started) return false;
  // Read the panel as it's normally mounted (rotation 1): an upside-down setting shows upside down,
  // just like on a panel that isn't actually mounted upside down.
  int rotation = tft.getRotation();
  tft.setRotation(1);
  tft.readRect(0, 0, TFT_W, TFT_H, out);
  tft.setRotation(rotation);
  // readRect gives the panel's wire order (big-endian RGB565); make it plain RGB565.
  for (int i = 0; i < TFT_W * TFT_H; i++) out[i] = (uint16_t)((out[i] >> 8) | (out[i] << 8));
  backlight = awake ? tft.light.level : 0;
  return true;
}
#endif

#if HAS_TOUCH && !defined(DANTE_SIMULATOR)
uint8_t readTouch(int& x, int& y) {
  if (!active()) return 0;
  if (!touchFound) {
    // Not found at boot: try again every few seconds, a few times (each try pauses ~120 ms).
    if (touchTries < TOUCH_MAX_TRIES && (int32_t)(millis() - touchRetryAt) >= 0) {
      touchRetryAt = millis() + 3000;
      startTouch();
    }
    if (!touchFound) return 0;
  }
  lgfx::touch_point_t points[2];
  uint8_t n = tft.getTouch(points, 2);
  if (n) { x = points[0].x; y = points[0].y; }
  return n;
}

void touchStatus(char* out, size_t n) {
  if (touchFound)
    snprintf(out, n, "touch panel: GT911 found at 0x%02X", (unsigned)tft.touch()->config().i2c_addr);
  else
    snprintf(out, n, "touch panel: GT911 NOT FOUND on I2C (SDA=%d SCL=%d RST=%d), tried %u times",
             TOUCH_PIN_SDA, TOUCH_PIN_SCL, TOUCH_PIN_RST, (unsigned)touchTries);
}
#endif

void describe(char* out, size_t n) {
  if (!started) { snprintf(out, n, "TFT not started"); return; }
  snprintf(out, n, "TFT: %s, %.0f fps, %.0f KB per frame sent, SPI %d MHz",
           psramMode ? "PSRAM frame buffer" : stripBuffers == 2 ? "strips (no PSRAM), 2 buffers" : "strips (no PSRAM), 1 buffer",
           fps, kbPerFrame, TFT_SPI_HZ / 1000000);
}

} // namespace Tft
