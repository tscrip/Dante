#pragma once
#include <Arduino.h>
#include <functional>
#define LGFX_USE_V1
#include <LovyanGFX.hpp>
#include "Gfx.h"
#include "config.h"

// Colours as 0xRRGGBB.
namespace Rgb {
  constexpr uint32_t Black = 0x000000, White = 0xFFFFFF;
  constexpr uint32_t Teal = 0x5EEAD4, Sky = 0x7DD3FC, Yellow = 0xFDE047, Orange = 0xFB923C, Red = 0xF87171;
  constexpr uint32_t Pink = 0xF472B6, Slate = 0x64748B, Light = 0xE2E8F0, Amber = 0xFBBF24, Dim = 0x1E293B;
}

// Draws the logical 128x32 canvas (Gfx.h) onto a strip of the 480x320 TFT, scaled and in colour.
// The transform maps logical (x, y) to screen (ox + x*sx, oy + y*sy); sx and sy may differ (the face
// stretches to fill the screen), text is scaled by the smaller of the two.
class TftGfx : public Gfx {
 public:
  explicit TftGfx(LGFX_Sprite& sprite) : s(sprite) {}

  void setTransform(float sx, float sy, float ox, float oy);
  // fg = colour 1, bg = colour 0 (also what the strip is cleared to), accent = lines drawn in colour 2.
  void setColors(uint32_t fg, uint32_t bg = Rgb::Black, uint32_t accent = Rgb::Teal);
  void setStripTop(int y) { stripTop = y; }
  // The sprite and the screen row its top line shows, for drawing in screen pixels.
  LGFX_Sprite& sprite() { return s; }
  int top() const { return stripTop; }

  void setFont(const uint8_t* font) override;
  void setDrawColor(uint8_t color) override { drawColor = color; }
  void drawStr(int x, int y, const char* str) override;
  int getStrWidth(const char* str) override;
  void drawPixel(int x, int y) override;
  void drawLine(int x0, int y0, int x1, int y1) override;
  void drawHLine(int x, int y, int w) override;
  void drawVLine(int x, int y, int h) override;
  void drawBox(int x, int y, int w, int h) override;
  void drawRBox(int x, int y, int w, int h, int r) override;
  void drawRFrame(int x, int y, int w, int h, int r) override;
  void drawDisc(int x, int y, int r) override;
  void drawCircle(int x, int y, int r, uint8_t opt) override;
  void drawTriangle(int x0, int y0, int x1, int y1, int x2, int y2) override;
  void drawFilledEllipse(int x, int y, int rx, int ry) override;
  void setClipWindow(int x0, int y0, int x1, int y1) override;
  void setMaxClipWindow() override;

  bool fine() const override { return true; }
  void fillRectF(float x, float y, float w, float h) override;
  void fillRoundRectF(float x, float y, float w, float h, float r) override;
  void fillTriangleF(float x0, float y0, float x1, float y1, float x2, float y2) override;
  void fillEllipseF(float cx, float cy, float rx, float ry) override;

 private:
  LGFX_Sprite& s;
  float sx = 1, sy = 1, ox = 0, oy = 0;
  int stripTop = 0;
  uint8_t drawColor = 1;
  uint32_t fg = Rgb::White, bg = Rgb::Black, accent = Rgb::Teal;
  const uint8_t* font = nullptr;

  // Pixel corners (boxes) and pixel centres (lines, circles).
  int X(float x) const { return (int)lroundf(ox + x * sx); }
  int Y(float y) const { return (int)lroundf(oy + y * sy) - stripTop; }
  int Xc(float x) const { return (int)lroundf(ox + (x + 0.5f) * sx); }
  int Yc(float y) const { return (int)lroundf(oy + (y + 0.5f) * sy) - stripTop; }
  float unit() const { return sx < sy ? sx : sy; }
  float Xf(float x) const { return ox + x * sx; }
  float Yf(float y) const { return oy + y * sy - stripTop; }
  uint32_t color() const { return drawColor == 0 ? bg : (drawColor == 2 ? accent : fg); }
  void invert(int x, int y, int w, int h);
};

// The 3.5" ST7796 TFT on SPI.
namespace Tft {
  bool begin();                    // panel + strip buffer; false if the buffer couldn't be allocated
  void end();                      // backlight off, panel asleep
  bool active();
  void setBrightness(uint8_t b);
  void setFlip(bool upsideDown);
  // Draws one frame. `draw` runs once per strip and must draw the whole screen each time
  // (anything outside the strip is clipped); strips that come out unchanged aren't sent.
  void frame(const std::function<void(TftGfx& g)>& draw);
  // "PSRAM frame buffer, 30 fps, 41 KB per frame" (or the strip mode), for the USB log and Check wiring.
  void describe(char* out, size_t n);
#ifdef DANTE_SIMULATOR
  // The screen as RGB565 (TFT_W x TFT_H) plus the backlight level, for the simulator's frames.
  bool simSnapshot(uint16_t* out, uint8_t& backlight);
#endif
}
