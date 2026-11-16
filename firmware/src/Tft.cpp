#include "Tft.h"

namespace {

// ST7796 480x320 on SPI2 (FSPI). Panel-specific settings are in config.h.
class Panel : public lgfx::LGFX_Device {
  lgfx::Panel_ST7796 panel;
  lgfx::Bus_SPI bus;
  lgfx::Light_PWM light;

 public:
  Panel() {
    {
      auto c = bus.config();
      c.spi_host = SPI2_HOST;
      c.spi_mode = 0;
      c.freq_write = TFT_SPI_HZ;
      c.freq_read = 16000000;
      c.spi_3wire = false;
      c.use_lock = true;
      c.dma_channel = SPI_DMA_CH_AUTO;
      c.pin_sclk = TFT_PIN_SCK;
      c.pin_mosi = TFT_PIN_MOSI;
      c.pin_miso = -1;
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
    setPanel(&panel);
  }
};

Panel lcd;
LGFX_Sprite strip(&lcd);
TftGfx canvas(strip);
const int STRIPS = (TFT_H + TFT_STRIP_H - 1) / TFT_STRIP_H;
uint32_t sent[STRIPS];          // hash of what each strip shows now
bool started = false, awake = false;
uint8_t brightness = 200;
bool flipped = false;

uint32_t hashBuffer(const uint32_t* p, size_t words) {
  uint32_t h = 2166136261u;
  for (size_t i = 0; i < words; i++) h = (h ^ p[i]) * 16777619u;
  return h;
}

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
namespace Tft {

bool begin() {
  if (!started) {
    lcd.init();
    strip.setColorDepth(16);
    strip.setPsram(false);
    if (!strip.createSprite(TFT_W, TFT_STRIP_H)) return false;
    started = true;
  } else {
    lcd.wakeup();
  }
  lcd.setRotation(flipped ? 3 : 1);
  lcd.fillScreen(TFT_BLACK);
  lcd.setBrightness(brightness);
  memset(sent, 0, sizeof(sent));
  awake = true;
  return true;
}

void end() {
  if (!started || !awake) return;
  lcd.setBrightness(0);
  lcd.fillScreen(TFT_BLACK);
  lcd.sleep();
  awake = false;
}

bool active() { return started && awake; }

void setBrightness(uint8_t b) {
  brightness = b;
  if (active()) lcd.setBrightness(b);
}

void setFlip(bool upsideDown) {
  flipped = upsideDown;
  if (!active()) return;
  lcd.setRotation(flipped ? 3 : 1);
  memset(sent, 0, sizeof(sent));   // everything has to be sent again
}

void frame(const std::function<void(TftGfx& g)>& draw) {
  if (!active()) return;
  for (int i = 0; i < STRIPS; i++) {
    int top = i * TFT_STRIP_H;
    strip.clearClipRect();
    strip.fillScreen(TFT_BLACK);
    canvas.setStripTop(top);
    canvas.setDrawColor(1);
    draw(canvas);
    strip.clearClipRect();
    uint32_t h = hashBuffer((const uint32_t*)strip.getBuffer(), strip.bufferLength() / 4);
    if (h == sent[i]) continue;   // nothing changed in this strip
    strip.pushSprite(0, top);
    sent[i] = h;
  }
}

} // namespace Tft
