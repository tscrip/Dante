#include "MediaScreen.h"
#include "Art.h"
#include "Tft.h"

namespace {
// Layout, in screen pixels (480x320). Drawing and tapping use the same numbers.
const int ART_X = 20, ART_Y = 52;                 // album art, 120x120 (Art::SIZE)
const int TEXT_X = 160, TEXT_W = TFT_W - 160 - 16;
const int ROW_Y = 270;                             // centre of the button row
const int BTN_R = 27, PLAY_R = 33;                 // button radii (play is bigger)
const int HIT_R = 38;                              // how far from a button's centre a tap still counts
const int PRESS_MS = 220;                          // how long a pressed button stays lit

struct Control { const char* id; int x; };
// Media group on the left, volume group on the right.
const Control controls[] = { {"prev", 40}, {"play", 120}, {"next", 200}, {"vold", 280}, {"mute", 360}, {"volu", 440} };

const char* lit = nullptr;
uint32_t litAt = 0;

bool isLit(const char* id, uint32_t now) { return lit && !strcmp(lit, id) && now - litAt < PRESS_MS; }

// A speaker: box + cone, centred at (cx, cy).
void speaker(LGFX_Sprite& s, int cx, int cy, uint32_t c) {
  s.fillRect(cx - 9, cy - 5, 6, 10, c);
  s.fillTriangle(cx - 4, cy - 5, cx + 4, cy - 12, cx + 4, cy + 12, c);
  s.fillTriangle(cx - 4, cy - 5, cx - 4, cy + 5, cx + 4, cy + 12, c);
}

void drawControl(LGFX_Sprite& s, const Control& c, int y, const App& a, uint32_t accent, uint32_t now) {
  bool play = !strcmp(c.id, "play");
  int r = play ? PLAY_R : BTN_R;
  bool on = isLit(c.id, now);
  uint32_t ring = play ? accent : Rgb::Slate;
  uint32_t fill = on ? Rgb::White : play ? accent : Rgb::Dim;
  uint32_t ink = on || play ? Rgb::Black : Rgb::Light;
  s.fillCircle(c.x, y, r, fill);
  s.drawCircle(c.x, y, r, ring);

  if (play) {
    if (a.playing) {   // pause sign
      s.fillRoundRect(c.x - 11, y - 13, 8, 26, 2, ink);
      s.fillRoundRect(c.x + 3, y - 13, 8, 26, 2, ink);
    } else {
      s.fillTriangle(c.x - 8, y - 14, c.x - 8, y + 14, c.x + 14, y, ink);
    }
  } else if (!strcmp(c.id, "next") || !strcmp(c.id, "prev")) {
    int d = !strcmp(c.id, "next") ? 1 : -1;        // which way the triangles point
    s.fillTriangle(c.x - 9 * d, y - 10, c.x - 9 * d, y + 10, c.x + 3 * d, y, ink);
    s.fillTriangle(c.x - 1 * d, y - 10, c.x - 1 * d, y + 10, c.x + 11 * d, y, ink);
    s.fillRect(d > 0 ? c.x + 9 : c.x - 12, y - 10, 3, 21, ink);
  } else if (!strcmp(c.id, "mute")) {
    speaker(s, c.x - 3, y, a.muted ? Rgb::Red : ink);
    if (a.muted) {
      s.drawWideLine(c.x + 5, y - 6, c.x + 15, y + 6, 1.5f, Rgb::Red);
      s.drawWideLine(c.x + 5, y + 6, c.x + 15, y - 6, 1.5f, Rgb::Red);
    } else {
      s.drawArc(c.x + 2, y, 9, 8, -45, 45, ink);
      s.drawArc(c.x + 2, y, 15, 14, -45, 45, ink);
    }
  } else {   // volume down / up: a speaker with - or +
    speaker(s, c.x - 5, y, ink);
    s.fillRect(c.x + 5, y - 1, 11, 3, ink);
    if (!strcmp(c.id, "volu")) s.fillRect(c.x + 9, y - 5, 3, 11, ink);
  }
}

// Text that doesn't fit scrolls (marquee), clipped to its box: it waits a moment at the start of each
// lap (from `since`, when the text last changed), so the beginning can be read.
void line(LGFX_Sprite& s, const char* text, int x, int y, int w, int h, uint32_t now, uint32_t since) {
  int tw = s.textWidth(text);
  s.setClipRect(x, y - h / 2, w, h);
  if (tw <= w) {
    s.drawString(text, x, y);
  } else {
    const int gap = 48;
    const uint32_t pauseMs = 1500, msPerPx = 25;
    uint32_t lap = pauseMs + (uint32_t)(tw + gap) * msPerPx;
    uint32_t t = (now - since) % lap;
    int offset = t < pauseMs ? 0 : (int)((t - pauseMs) / msPerPx);
    s.drawString(text, x - offset, y);
    s.drawString(text, x - offset + tw + gap, y);
  }
  s.clearClipRect();
}

// A music note, for when there is no album art.
void placeholder(LGFX_Sprite& s, int x, int y, uint32_t c) {
  s.fillRoundRect(x, y, Art::SIZE, Art::SIZE, 10, Rgb::Dim);
  int cx = x + Art::SIZE / 2, cy = y + Art::SIZE / 2;
  s.fillEllipse(cx - 18, cy + 20, 11, 8, c);
  s.fillEllipse(cx + 16, cy + 14, 11, 8, c);
  s.fillRect(cx - 9, cy - 26, 4, 46, c);
  s.fillRect(cx + 25, cy - 32, 4, 46, c);
  s.fillTriangle(cx - 9, cy - 26, cx + 29, cy - 32, cx + 29, cy - 22, c);
  s.fillTriangle(cx - 9, cy - 26, cx - 9, cy - 16, cx + 29, cy - 22, c);
}
} // namespace

namespace MediaScreen {

void draw(TftGfx& g, const App& a, uint32_t accent, uint32_t now) {
  LGFX_Sprite& s = g.sprite();
  const int dy = -g.top();   // screen row → row in this strip

  // Album art, or a note when there is none (yet).
  if (Art::ready(a.artHash)) Art::draw(g, ART_X, ART_Y);
  else placeholder(s, ART_X, ART_Y + dy, Rgb::Slate);

  // Title, artist, state.
  s.setTextDatum(lgfx::textdatum_t::middle_left);
  s.setTextSize(1);
  s.setFont(&fonts::FreeSansBold12pt7b);
  s.setTextColor(Rgb::White);
  line(s, a.txt[0] ? a.txt : "Nothing playing", TEXT_X, ART_Y + 18 + dy, TEXT_W, 30, now, a.changedAt);
  s.setFont(&fonts::FreeSans9pt7b);
  s.setTextColor(Rgb::Light);
  if (a.sub[0]) line(s, a.sub, TEXT_X, ART_Y + 48 + dy, TEXT_W, 24, now, a.changedAt);
  s.setTextColor(a.playing ? accent : Rgb::Slate);
  // "Paused" only for a song (one with an artist): not under "Nothing playing".
  s.drawString(a.playing ? "Playing" : (a.sub[0] ? "Paused" : ""), TEXT_X, ART_Y + 76 + dy);

  // Volume bar.
  int barY = ART_Y + 106 + dy, barX = TEXT_X + 26, barW = TEXT_W - 26 - 54;
  speaker(s, TEXT_X + 9, barY, a.muted ? Rgb::Red : Rgb::Light);
  s.drawRoundRect(barX, barY - 6, barW, 12, 6, Rgb::Slate);
  if (a.volume >= 0) {
    int fill = (barW - 4) * a.volume / 100;
    if (fill > 0) s.fillRoundRect(barX + 2, barY - 4, fill, 8, 4, a.muted ? Rgb::Slate : accent);
    char pct[8];
    snprintf(pct, sizeof(pct), a.muted ? "mute" : "%d%%", a.volume);
    s.setTextColor(a.muted ? Rgb::Red : Rgb::Light);
    s.setTextDatum(lgfx::textdatum_t::middle_right);
    s.drawString(pct, TEXT_X + TEXT_W, barY);
  }

  // The buttons.
  s.drawFastHLine(16, ROW_Y - 48 + dy, TFT_W - 32, Rgb::Dim);
  for (const Control& c : controls) drawControl(s, c, ROW_Y + dy, a, accent, now);
}

const char* hit(int x, int y) {
  if (abs(y - ROW_Y) > HIT_R) return nullptr;
  for (const Control& c : controls)
    if (abs(x - c.x) <= HIT_R) return c.id;
  return nullptr;
}

void pressed(const char* ctl, uint32_t now) {
  lit = ctl;
  litAt = now;
}

} // namespace MediaScreen
