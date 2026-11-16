#pragma once
#include <Arduino.h>
#include <U8g2lib.h>

// What the face, the app screens, bubbles and nudges draw with. Everything is laid out on a
// logical 128x32 canvas in OLED pixels (u8g2's API and fonts); each screen type maps that to
// its own pixels:
//   * OLED: U8g2Gfx, 1:1 into the SSD1306 frame buffer.
//   * 3.5" TFT: TftGfx (Tft.h), scaled up and in colour.
//   * character LCD: U8g2Gfx into an off-screen buffer that is cut into custom glyphs (LcdUi).
//
// Draw colours follow u8g2: 0 = background, 1 = foreground, 2 = XOR (on the TFT: inverted
// colours for boxes, the accent colour for lines).
class Gfx {
 public:
  virtual ~Gfx() {}
  virtual void setFont(const uint8_t* font) = 0;
  virtual void setDrawColor(uint8_t color) = 0;
  virtual void drawStr(int x, int y, const char* s) = 0;   // y = baseline
  virtual int getStrWidth(const char* s) = 0;
  virtual void drawPixel(int x, int y) = 0;
  virtual void drawLine(int x0, int y0, int x1, int y1) = 0;
  virtual void drawHLine(int x, int y, int w) = 0;
  virtual void drawVLine(int x, int y, int h) = 0;
  virtual void drawBox(int x, int y, int w, int h) = 0;
  virtual void drawRBox(int x, int y, int w, int h, int r) = 0;
  virtual void drawRFrame(int x, int y, int w, int h, int r) = 0;
  virtual void drawDisc(int x, int y, int r) = 0;
  virtual void drawCircle(int x, int y, int r, uint8_t opt = U8G2_DRAW_ALL) = 0;
  virtual void drawTriangle(int x0, int y0, int x1, int y1, int x2, int y2) = 0;
  virtual void drawFilledEllipse(int x, int y, int rx, int ry) = 0;
  // Clip to [x0, x1) x [y0, y1) in logical pixels, like u8g2.
  virtual void setClipWindow(int x0, int y0, int x1, int y1) = 0;
  virtual void setMaxClipWindow() = 0;
};

// Pass-through to a u8g2 display or off-screen buffer.
class U8g2Gfx : public Gfx {
  U8G2& g;
 public:
  explicit U8g2Gfx(U8G2& target) : g(target) {}
  U8G2& u8g2() { return g; }
  void setFont(const uint8_t* font) override { g.setFont(font); }
  void setDrawColor(uint8_t c) override { g.setDrawColor(c); }
  void drawStr(int x, int y, const char* s) override { g.drawStr(x, y, s); }
  int getStrWidth(const char* s) override { return g.getStrWidth(s); }
  void drawPixel(int x, int y) override { g.drawPixel(x, y); }
  void drawLine(int x0, int y0, int x1, int y1) override { g.drawLine(x0, y0, x1, y1); }
  void drawHLine(int x, int y, int w) override { g.drawHLine(x, y, w); }
  void drawVLine(int x, int y, int h) override { g.drawVLine(x, y, h); }
  void drawBox(int x, int y, int w, int h) override { g.drawBox(x, y, w, h); }
  void drawRBox(int x, int y, int w, int h, int r) override { g.drawRBox(x, y, w, h, r); }
  void drawRFrame(int x, int y, int w, int h, int r) override { g.drawRFrame(x, y, w, h, r); }
  void drawDisc(int x, int y, int r) override { g.drawDisc(x, y, r); }
  void drawCircle(int x, int y, int r, uint8_t opt) override { g.drawCircle(x, y, r, opt); }
  void drawTriangle(int x0, int y0, int x1, int y1, int x2, int y2) override { g.drawTriangle(x0, y0, x1, y1, x2, y2); }
  void drawFilledEllipse(int x, int y, int rx, int ry) override { g.drawFilledEllipse(x, y, rx, ry); }
  void setClipWindow(int x0, int y0, int x1, int y1) override { g.setClipWindow(x0, y0, x1, y1); }
  void setMaxClipWindow() override { g.setMaxClipWindow(); }
};
