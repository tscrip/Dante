#include "LcdUi.h"
#include "Lcd.h"
#include "Face.h"
#include "config.h"
#include <U8g2lib.h>
#include <math.h>

namespace {

// ---- off-screen canvas -------------------------------------------------------------
// The eyes are rasterised with the same code as the OLED face into a small off-screen
// U8g2 buffer, then cut into 5x8 glyphs. Its "display" is never talked to.
extern "C" uint8_t lcdui_null_byte(u8x8_t*, uint8_t, uint8_t, void*) { return 1; }
extern "C" uint8_t lcdui_null_gpio(u8x8_t*, uint8_t, uint8_t, void*) { return 1; }

class Canvas : public U8G2 {
  uint8_t frame[128 * 32 / 8];
 public:
  Canvas() : U8G2() {
    u8g2_SetupDisplay(&u8g2, u8x8_d_ssd1306_128x32_univision, u8x8_cad_ssd13xx_fast_i2c, lcdui_null_byte, lcdui_null_gpio);
    u8g2_SetupBuffer(&u8g2, frame, 4, u8g2_ll_hvline_vertical_top_lsb, U8G2_R0);
  }
  bool px(int x, int y) { return (frame[(y / 8) * 128 + x] >> (y % 8)) & 1; }
};
Canvas canvas;

// Glyph slots. The face uses all 8 (2x2 cells per eye); app screens reuse them.
const uint8_t FULL = 0xFF;                  // ROM character: solid block
enum { G_BAR1 = 0, G_CHECK = 5, G_BELL = 6, G_HEART = 7 };   // bar set: slots 0..4 = 1..5 columns lit

const uint8_t CHECK[8] = { 0, 0x01, 0x03, 0x16, 0x1C, 0x08, 0, 0 };
const uint8_t BELL[8]  = { 0x04, 0x0E, 0x0E, 0x0E, 0x1F, 0x00, 0x04, 0 };
const uint8_t HEART[8] = { 0, 0x0A, 0x1F, 0x1F, 0x0E, 0x04, 0, 0 };

void loadBarGlyphs() {
  for (int n = 1; n <= 5; n++) {
    uint8_t rows[8];
    uint8_t bits = (uint8_t)(0x1F << (5 - n)) & 0x1F;
    for (int r = 0; r < 8; r++) rows[r] = (r == 0 || r == 7) ? 0 : bits;
    Lcd::glyph(G_BAR1 + n - 1, rows);
  }
  Lcd::glyph(G_CHECK, CHECK);
  Lcd::glyph(G_BELL, BELL);
  Lcd::glyph(G_HEART, HEART);
}

void loadSparkGlyphs() {   // slot k = a column k+1 pixels tall
  for (int k = 0; k < 8; k++) {
    uint8_t rows[8];
    for (int r = 0; r < 8; r++) rows[r] = (r >= 7 - k) ? 0x1F : 0;
    Lcd::glyph(k, rows);
  }
}

inline int W() { return Lcd::cols(); }
inline int H() { return Lcd::rows(); }
inline bool tall() { return Lcd::rows() >= 4; }

// Text that doesn't fit scrolls (after a short pause) and wraps with a gap.
void marquee(int col, int row, int width, const char* text, uint32_t now, uint32_t start) {
  int len = (int)strlen(text);
  if (len <= width) { Lcd::print(col, row, text, width); return; }
  const int gap = 3;
  uint32_t t = now - start;
  int period = len + gap;
  int off = t < 1200 ? 0 : (int)(((t - 1200) / 260) % period);
  for (int i = 0; i < width; i++) {
    int k = (off + i) % period;
    Lcd::put(col + i, row, k < len ? (uint8_t)text[k] : ' ');
  }
}

// Two rows of text, word-wrapped. If it's longer than two rows, the second row scrolls the rest.
void wrap2(int col, int row, int width, const char* text, uint32_t now, uint32_t start) {
  int len = (int)strlen(text);
  if (len <= width) { Lcd::print(col, row, text, width); return; }
  int cut = width;
  while (cut > 0 && text[cut] != ' ') cut--;   // last space that keeps line 1 within width
  if (cut < width / 2) cut = width;            // one long word: just split it
  Lcd::print(col, row, text, cut);
  const char* rest = text + cut + (text[cut] == ' ' ? 1 : 0);
  marquee(col, row + 1, width, rest, now, start);
}

void printRight(int row, const char* s, int rightCol = -1) {
  if (rightCol < 0) rightCol = W() - 1;
  int len = (int)strlen(s);
  Lcd::print(rightCol - len + 1, row, s, len);
}

void printCentered(int row, const char* s) {
  int len = (int)strlen(s);
  Lcd::print((W() - len) / 2, row, s, len);
}

void fmtValue(float v, char* out, size_t n) {
  if (fabsf(v - roundf(v)) < 0.05f || fabsf(v) >= 100) snprintf(out, n, "%.0f", v);
  else snprintf(out, n, "%.1f", v);
}

// "<  3/6  >"-style page indicator for the bottom row of a 20x4.
void pageLine(int row, int page, int pages) {
  char b[24];
  snprintf(b, sizeof(b), "<  %d/%d  >", page, pages);
  printCentered(row, b);
}

// ---- face -----------------------------------------------------------------------------
// Each eye is 2x2 cells (10x16 px). Rendered at 0.42x width / 0.62x height of the OLED eye.
void eyeGlyphs(int eye, int cy) {
  const int ox = eye * 16;   // separate canvas regions per eye
  canvas.setClipWindow(ox, 0, ox + 10, 16);
  Face::drawEyeScaled(canvas, eye, ox + 5, cy, 0.42f, 0.62f, 2);
  canvas.setMaxClipWindow();
  for (int cr = 0; cr < 2; cr++)
    for (int cc = 0; cc < 2; cc++) {
      uint8_t rows[8];
      for (int y = 0; y < 8; y++) {
        uint8_t b = 0;
        for (int x = 0; x < 5; x++)
          if (canvas.px(ox + cc * 5 + x, cr * 8 + y)) b |= (uint8_t)(0x10 >> x);
        rows[y] = b;
      }
      Lcd::glyph(eye * 4 + cr * 2 + cc, rows);
    }
}

// Margin decorations around the eye rows (top = first eye row).
void decorations(const FaceInfo& f, int top, uint32_t now) {
  const int R = W() - 3;   // start of the right margin
  bool flip = (now / 300) % 2;
  int a = top + (flip ? 1 : 0), b = top + (flip ? 0 : 1);
  if (f.decor & DEC_Z)      { Lcd::put(R, a, 'z'); Lcd::put(R + 1, b, 'Z'); }
  if (f.decor & DEC_SWEAT)  Lcd::put(R, top + (now / 250) % 2, '\'');
  if (f.decor & DEC_BANG)   Lcd::print(0, top + 1, "!!");
  if (f.decor & DEC_QUEST)  Lcd::put(R + 1, top, '?');
  if (f.decor & DEC_HA)     { Lcd::print(0, a, "ha"); Lcd::print(R, b, "ha"); }
  if (f.decor & DEC_HEARTS) { Lcd::print(1, a, "<3"); Lcd::print(R, b, "<3"); }
  if (f.decor & DEC_BOOP)   Lcd::print(W() - 4, top, "boop");
  if (f.decor & DEC_WOW)    { Lcd::put(1, top, '!'); Lcd::put(R + 1, top, '!'); }
  if (f.decor & DEC_STARS)  { if (flip) Lcd::put(1, top, '*'); else Lcd::put(R + 1, top + 1, '*'); }
}

// 20x4 only: a mouth under the eyes. (No backslash: the LCD's ROM shows it as a yen sign.)
const char* mouth(const FaceInfo& f, uint32_t now) {
  switch (f.gag) {
    case GAG_LOVE:      return "`--'";
    case GAG_DIZZY:     return (now / 300) % 2 ? "~~~" : "-~-";
    case GAG_WINK:      return "`-'";
    case GAG_LAUGH:     return (now / 150) % 2 ? "(DD)" : "(OO)";
    case GAG_SURPRISED: return "O";
    case GAG_BOOP:      return "o";
    default: break;
  }
  switch (f.mood) {
    case MOOD_HAPPY:    return "`--'";
    case MOOD_CURIOUS:  return "o";
    case MOOD_ANTSY:    return (now / 400) % 2 ? "~~~" : "-~-";
    case MOOD_GRUMPY:   return "---";
    case MOOD_MELTDOWN: return (now / 200) % 2 ? "{OO}" : "[OO]";
    case MOOD_SLEEPY:   return (now / 1200) % 2 ? "o" : "-";
  }
  return "-";
}

} // namespace

namespace LcdUi {

void face(uint32_t now) {
  FaceInfo f = Face::info();
  canvas.clearBuffer();
  int cy = 8 + (int)lroundf(f.lookY * 0.4f);
  eyeGlyphs(0, cy);
  eyeGlyphs(1, cy);

  const int top = tall() ? 1 : 0;   // eye rows: 0-1 on a 16x2, 1-2 on a 20x4
  decorations(f, top, now);
  if (f.pending > 0 && !f.alone) {
    char b[6];
    snprintf(b, sizeof(b), "[%d]", f.pending > 99 ? 99 : f.pending);
    printRight(0, b);
  }
  if (f.alone && (now / 700) % 2 == 0) Lcd::print(0, 0, "noPC");
  if (tall()) printCentered(3, mouth(f, now));

  // Eyes last so nothing overwrites them; looking around moves them across the columns.
  int reach = W() >= 20 ? 3 : 2;
  int look = (int)lroundf(f.lookX / 24.0f * reach);
  look = look < -reach ? -reach : (look > reach ? reach : look);
  int mid = W() / 2;
  int cols[2] = { mid - 4 + look, mid + 2 + look };
  for (int e = 0; e < 2; e++)
    for (int r = 0; r < 2; r++)
      for (int c = 0; c < 2; c++)
        Lcd::put(cols[e] + c, top + r, (uint8_t)(e * 4 + r * 2 + c));
}

void app(const App& a, int page, int pages, uint32_t now) {
  char v[16] = "--";
  bool blinkOff = a.lvl >= 2 && (now / 500) % 2;
  bool fresh = now - a.changedAt < 1500;

  // Row 0: name ............ value+unit
  char right[24] = "";
  if (a.vis != VIS_TEXT && a.vis != VIS_STATUS) {
    if (a.hasValue) fmtValue(a.vis == VIS_COUNTER ? a.value : a.shown, v, sizeof(v));
    snprintf(right, sizeof(right), "%s%s", v, a.unit);
    if (blinkOff) memset(right, ' ', strlen(right));
  }
  int nameRoom = W() - (int)strlen(right) - (right[0] ? 1 : 0);
  Lcd::print(0, 0, a.name, nameRoom);
  printRight(0, right);

  // Row 1: the visual. On a 20x4, row 2 gets the extra text and row 3 the page indicator.
  bool textShown = false;
  switch (a.vis) {
    case VIS_GAUGE:
    case VIS_BAR: {
      loadBarGlyphs();
      float frac = (a.hasValue && a.max > a.min) ? (a.shown - a.min) / (a.max - a.min) : 0;
      frac = frac < 0 ? 0 : (frac > 1 ? 1 : frac);
      int px = (int)lroundf(frac * W() * 5);
      for (int c = 0; c < W(); c++) {
        int lit = px - c * 5;
        Lcd::put(c, 1, lit >= 5 ? FULL : (lit > 0 ? (uint8_t)(G_BAR1 + lit - 1) : (a.vis == VIS_GAUGE && c % 4 == 3 ? '.' : ' ')));
      }
      break;
    }
    case VIS_COUNTER: {
      if (fresh && a.hasValue) {
        long d = lroundf(a.value - a.prevValue);
        char b[12];
        snprintf(b, sizeof(b), d > 0 ? "+%ld!" : "%ld", d);
        int col = (now / 120) % 2 ? 0 : 1;   // a little hop
        Lcd::print(col, 1, b);
      } else if (!tall()) {
        marquee(0, 1, W(), a.txt, now, a.changedAt);
        textShown = true;
      }
      break;
    }
    case VIS_SPARK: {
      loadSparkGlyphs();
      float lo = a.min, hi = a.max;
      if (hi <= lo) hi = lo + 1;
      int n = a.histN < W() ? a.histN : W();
      for (int i = 0; i < n; i++) {
        int idx = (a.histHead - n + i + HIST_LEN) % HIST_LEN;
        float f = (a.hist[idx] - lo) / (hi - lo);
        int lvl = (int)lroundf((f < 0 ? 0 : (f > 1 ? 1 : f)) * 7);
        Lcd::put(W() - n + i, 1, (uint8_t)lvl);
      }
      break;
    }
    case VIS_STATUS: {
      loadBarGlyphs();
      uint8_t icon = a.lvl == 0 ? (uint8_t)G_CHECK : (uint8_t)(a.lvl == 1 ? '!' : 'X');
      if (!blinkOff) Lcd::put(0, 1, icon);
      if (a.hasValue) { fmtValue(a.value, v, sizeof(v)); printRight(0, v); }
      const char* t = a.txt[0] ? a.txt : (a.lvl ? "needs a look" : "all good");
      if (tall()) wrap2(2, 1, W() - 2, t, now, a.changedAt);
      else marquee(2, 1, W() - 2, t, now, a.changedAt);
      textShown = true;
      break;
    }
    case VIS_TEXT:
    default: {
      char buf[TXT_LEN + 16];
      if (a.txt[0]) snprintf(buf, sizeof(buf), "%s", a.txt);
      else if (a.hasValue) { fmtValue(a.value, v, sizeof(v)); snprintf(buf, sizeof(buf), "%s %s", v, a.unit); }
      else snprintf(buf, sizeof(buf), "...");
      if (tall()) wrap2(0, 1, W(), buf, now, a.changedAt);
      else marquee(0, 1, W(), buf, now, a.changedAt);
      textShown = true;
      break;
    }
  }

  if (tall()) {
    if (!textShown && a.txt[0]) marquee(0, 2, W(), a.txt, now, a.changedAt);
    else if (!textShown && a.vis == VIS_GAUGE) {     // scale under the gauge bar
      char lo[12], hi[12];
      fmtValue(a.min, lo, sizeof(lo));
      fmtValue(a.max, hi, sizeof(hi));
      Lcd::print(0, 2, lo);
      printRight(2, hi);
    }
    pageLine(3, page, pages);
  }
}

void toast(const char* title, const char* text, uint32_t start, uint32_t now) {
  uint32_t t = now - start;
  loadBarGlyphs();
  Lcd::print(0, 0, t < 600 ? ":O" : ":D");
  Lcd::put(2, 0, G_BELL);
  Lcd::print(4, 0, title[0] ? title : "psst!", W() - 4);
  if (tall()) wrap2(0, 1, W(), text, now, start + 300);
  else marquee(0, 1, W(), text, now, start + 300);
}

void takeover(const char* title, const char* text, uint8_t lvl, uint32_t since, uint32_t now, bool light) {
  uint32_t t = now - since;
  // "Knock knock": everything jumps one column every 3 s.
  int k = (t % 3000) < 450 && ((t / 110) % 2) ? 1 : 0;
  const char* eyes = lvl >= 2 ? ((t / 400) % 2 ? "(>_<)" : "(O_O)") : ((t / 2000) % 2 ? "(o_o)" : "(O_O)");
  const char* msg = text[0] ? text : "psst... over here!";
  Lcd::print(k, 0, eyes);
  marquee(6 + k, 0, W() - 6 - k, title, now, since);
  if (tall()) {
    wrap2(k, 1, W() - k, msg, now, since + 600);
    printCentered(3, (t / 4000) % 2 ? "SEL:on it  </>:later" : (lvl >= 2 ? "!! URGENT !!" : "need you :)"));
  } else if ((t / 4000) % 2 == 1) {
    Lcd::print(0, 1, "SEL:ok </>:later");
  } else {
    marquee(k, 1, W() - k, msg, now, since + 600);
  }
  // Urgent: the backlight flashes.
  Lcd::backlight(light && !(lvl >= 2 && (t % 1000) < 160));
}

} // namespace LcdUi
