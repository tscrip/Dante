#include "Face.h"
#include "config.h"
#include <math.h>

namespace {

struct EyeStyle {
  float w, h;      // size in px
  float open;      // 0 closed .. 1 open
  float lid;       // sleepy flat top lid (fraction of height)
  float angry;     // inner-top diagonal cut
  float worried;   // outer-top diagonal cut
  float happy;     // bottom crescent cut ("^ ^" cheeks)
};

enum Shape : uint8_t { SH_NORMAL, SH_HEART, SH_CROSS, SH_SPIRAL, SH_SQUINT };

const int EYE_GAP = 16;
// Dual-screen mode: each eye gets a whole 128x32 panel, so stretch it into a big wide eye.
const float BIG_W = 2.3f, BIG_H = 1.2f, BIG_LOOK = 1.6f;
int radiusCap = 6;   // corner radius limit; bigger for the big single eyes

EyeStyle cur[2], tgt[2];
float lookX = 0, lookY = 0, tgtLookX = 0, tgtLookY = 0;
float offX = 0, offY = 0;
Shape shape[2] = { SH_NORMAL, SH_NORMAL };
uint16_t decor = 0;

Mood moodNow = MOOD_HAPPY;
int pending = 0;
bool alone = true;

uint32_t blinkStart = 0, blinkDur = 140, nextBlink = 1500;
bool doubleBlink = false;
uint32_t nextSaccade = 0;
uint32_t idleStart = 0, idleUntil = 0, nextIdle = 3000;
uint8_t idleKind = 0;
Gag gag = GAG_NONE;
uint32_t gagStart = 0, gagDur = 0;
uint32_t sideEyeUntil = 0;

inline float rnd(float a, float b) { return a + (b - a) * (float)random(0, 10001) / 10000.0f; }
inline uint32_t rndMs(uint32_t a, uint32_t b) { return (uint32_t)random(a, b + 1); }
inline float approach(float c, float t, float k) { return c + (t - c) * k; }
inline float clampf(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }

EyeStyle style(float w, float h, float lid = 0, float angry = 0, float worried = 0, float happy = 0) {
  return EyeStyle{ w, h, 1.0f, lid, angry, worried, happy };
}

void baseline(Mood m) {
  switch (m) {
    case MOOD_HAPPY:    tgt[0] = tgt[1] = style(24, 22, 0, 0, 0, 0.45f); break;
    case MOOD_CURIOUS:  tgt[0] = style(26, 24); tgt[1] = style(21, 17, 0.1f); break;
    case MOOD_ANTSY:    tgt[0] = tgt[1] = style(23, 22, 0, 0, 0.38f); break;
    case MOOD_GRUMPY:   tgt[0] = tgt[1] = style(25, 18, 0.12f, 0.55f); break;
    case MOOD_MELTDOWN: tgt[0] = tgt[1] = style(22, 24, 0, 0.2f, 0.3f); break;
    case MOOD_SLEEPY:   tgt[0] = tgt[1] = style(25, 20, 0.55f); break;
  }
}

void scheduleSaccade(uint32_t now, Mood m) {
  switch (m) {
    case MOOD_CURIOUS:
      tgtLookX = rnd(-20, 20); tgtLookY = rnd(-4, 3);
      nextSaccade = now + rndMs(500, 1600);
      break;
    case MOOD_ANTSY:
      // Keeps glancing back at the human: "...well?"
      if (random(0, 100) < 55) { tgtLookX = 0; tgtLookY = 2; }
      else { tgtLookX = rnd(-16, 16); tgtLookY = rnd(-3, 3); }
      nextSaccade = now + rndMs(400, 1200);
      break;
    case MOOD_GRUMPY:
      // Long, judgemental side-eye.
      if (random(0, 100) < 60) { tgtLookX = random(0, 2) ? 20 : -20; tgtLookY = 1; sideEyeUntil = now + 2500; }
      else { tgtLookX = rnd(-6, 6); tgtLookY = 0; }
      nextSaccade = now + rndMs(2200, 4500);
      break;
    case MOOD_MELTDOWN:
      tgtLookX = rnd(-22, 22); tgtLookY = rnd(-4, 4);
      nextSaccade = now + rndMs(150, 500);
      break;
    case MOOD_SLEEPY:
      tgtLookX = rnd(-6, 6); tgtLookY = 3;
      nextSaccade = now + rndMs(3000, 6000);
      break;
    default:
      tgtLookX = rnd(-14, 14); tgtLookY = rnd(-3, 3);
      if (random(0, 100) < 30) { tgtLookX = 0; tgtLookY = 0; }
      nextSaccade = now + rndMs(1200, 3500);
      break;
  }
}

void scheduleBlink(uint32_t now, Mood m) {
  blinkStart = now;
  blinkDur = (m == MOOD_SLEEPY || m == MOOD_GRUMPY) ? 320 : 140;
  doubleBlink = (m == MOOD_ANTSY || m == MOOD_CURIOUS) && random(0, 100) < 35;
  uint32_t lo = 2000, hi = 5000;
  if (m == MOOD_SLEEPY) { lo = 1400; hi = 3000; }
  if (m == MOOD_MELTDOWN) { lo = 600; hi = 1600; }
  nextBlink = now + rndMs(lo, hi);
}

float blinkFactor(uint32_t now) {
  uint32_t total = doubleBlink ? blinkDur * 2 + 80 : blinkDur;
  uint32_t t = now - blinkStart;
  if (t >= total) return 1.0f;
  if (doubleBlink && t >= blinkDur) {
    if (t < blinkDur + 80) return 1.0f;
    t -= blinkDur + 80;
  }
  float p = (float)t / blinkDur;        // 0..1
  return fabsf(1.0f - 2.0f * p);        // 1 -> 0 -> 1
}

// ---- mood-specific idle shenanigans ------------------------------------------
void idleBehaviour(uint32_t now, Mood m) {
  if (now >= nextIdle && now >= idleUntil) {
    idleStart = now;
    idleKind = (uint8_t)random(0, 3);
    switch (m) {
      case MOOD_HAPPY:    idleUntil = now + (idleKind == 0 ? 700 : 900); nextIdle = now + rndMs(3000, 7000); break;
      case MOOD_ANTSY:    idleUntil = now + 1800; nextIdle = now + rndMs(2500, 5000); break;
      case MOOD_SLEEPY:   idleUntil = now + 1600; nextIdle = now + rndMs(5000, 9000); break;
      case MOOD_MELTDOWN: idleUntil = now + 350;  nextIdle = now + rndMs(1200, 2600); break;
      case MOOD_CURIOUS:  idleUntil = now + 1200; nextIdle = now + rndMs(4000, 8000); break;
      case MOOD_GRUMPY:   idleUntil = now + 1500; nextIdle = now + rndMs(6000, 11000); break;
    }
  }
  bool active = now < idleUntil;
  float t = (float)(now - idleStart);

  switch (m) {
    case MOOD_HAPPY:
      if (active) {
        if (idleKind == 0) offY = -fabsf(sinf(t * 0.0135f)) * 4.0f;             // little hop
        else if (idleKind == 1) { tgt[1].open = 0.05f; tgt[0].happy = 0.6f; }      // wink
        else { offY = -fabsf(sinf(t * 0.009f)) * 2.0f; decor |= DEC_STARS; }
      }
      break;
    case MOOD_CURIOUS:
      if (active) { tgt[1].h = 24; tgt[1].w = 25; tgt[0].h = 16; decor |= DEC_QUEST; } // swap: other brow up
      break;
    case MOOD_ANTSY:
      if (active) offX = sinf(t * 0.03f) * 2.5f;                                    // foot tapping
      if (pending > 0 && (now / 1500) % 4 == 0) decor |= DEC_BANG;
      break;
    case MOOD_GRUMPY:
      if (now < sideEyeUntil) { tgt[0].lid = tgt[1].lid = 0.35f; }
      if (active && idleKind == 0) { tgt[0].angry = tgt[1].angry = 0.75f; offY = 1; } // huff
      break;
    case MOOD_MELTDOWN:
      offX += rnd(-1.5f, 1.5f);
      offY += rnd(-1.0f, 1.0f);
      decor |= DEC_SWEAT;
      if ((now / 400) % 3 == 0) decor |= DEC_BANG;
      if (active) shape[0] = shape[1] = SH_CROSS;
      break;
    case MOOD_SLEEPY:
      decor |= DEC_Z;
      if (active) {                                                                  // nodding off...
        float p = t / 1600.0f;
        tgt[0].lid = tgt[1].lid = p < 0.8f ? 0.55f + 0.4f * (p / 0.8f) : 0.3f;
        offY = p < 0.8f ? 2.0f * (p / 0.8f) : -1.0f;
      }
      break;
  }
}

// ---- button-triggered gags -----------------------------------------------------
void gagBehaviour(uint32_t now) {
  uint32_t t = now - gagStart;
  float p = (float)t / gagDur;
  switch (gag) {
    case GAG_LOVE:
      shape[0] = shape[1] = SH_HEART;
      offY = -fabsf(sinf(t * 0.008f)) * 3.0f;
      tgtLookX = 0; tgtLookY = 0;
      decor |= DEC_HEARTS;
      break;
    case GAG_DIZZY:
      shape[0] = shape[1] = SH_SPIRAL;
      offX = sinf(t * 0.006f) * 6.0f;
      offY = cosf(t * 0.012f) * 2.0f;
      decor |= DEC_STARS;
      break;
    case GAG_WINK:
      tgt[0] = style(24, 22, 0, 0, 0, 0.55f);
      tgt[1] = style(24, 22, 0, 0, 0, 0.55f);
      if (p > 0.15f && p < 0.75f) tgt[1].open = 0.04f;
      tgtLookX = 3; tgtLookY = 0;
      break;
    case GAG_LAUGH:
      shape[0] = shape[1] = SH_SQUINT;
      offY = -fabsf(sinf(t * 0.025f)) * 3.0f;
      decor |= DEC_HA;
      break;
    case GAG_SURPRISED:
      tgt[0] = tgt[1] = style(27, 27);
      tgtLookX = 0; tgtLookY = -2;
      if (t < 120) offY = -3;
      decor |= DEC_WOW;
      break;
    case GAG_BOOP:
      if (p < 0.35f) {           // squish
        tgt[0] = tgt[1] = style(28, 10, 0, 0, 0, 0.5f);
        offX = rnd(-1, 1);
      } else {                   // pop!
        tgt[0] = tgt[1] = style(24, 24, 0, 0, 0, 0.3f);
        offY = -fabsf(sinf((t - gagDur * 0.35f) * 0.015f)) * 3.0f;
      }
      tgtLookX = 0; tgtLookY = 0;
      decor |= DEC_BOOP;
      break;
    default: break;
  }
}

// ---- drawing ---------------------------------------------------------------------
void drawEye(U8G2& g, int cx, int cy, const EyeStyle& e, float blink, bool left, Shape sh, uint32_t now) {
  int s = (int)fminf(e.w, e.h);
  switch (sh) {
    case SH_HEART: {
      float pulse = 1.0f + 0.12f * sinf(now * 0.012f);
      int r = max(2, (int)(s * 0.26f * pulse));
      int ty = cy - r / 2;
      g.drawDisc(cx - r + 1, ty, r);
      g.drawDisc(cx + r - 1, ty, r);
      g.drawTriangle(cx - 2 * r + 1, ty + r / 3, cx + 2 * r - 1, ty + r / 3, cx, cy + (int)(r * 1.7f));
      return;
    }
    case SH_CROSS: {
      int h = max(3, (int)(s * 0.4f));
      for (int d = -1; d <= 1; d++) {
        g.drawLine(cx - h + d, cy - h, cx + h + d, cy + h);
        g.drawLine(cx + h + d, cy - h, cx - h + d, cy + h);
      }
      return;
    }
    case SH_SPIRAL: {
      int r = max(3, (int)(s * 0.45f));
      g.drawCircle(cx, cy, r);
      g.drawCircle(cx, cy, max(1, r / 2));
      float a = now * 0.012f * (left ? 1.0f : -1.0f);
      g.drawDisc(cx + (int)(cosf(a) * r * 0.75f), cy + (int)(sinf(a) * r * 0.75f), max(1, r / 4));
      return;
    }
    case SH_SQUINT: {
      int hw = max(3, (int)(e.w * 0.38f));
      int hh = max(3, (int)(e.h * 0.32f));
      int dir = left ? 1 : -1;               // ">" on the left eye, "<" on the right
      for (int d = 0; d < 3; d++) {
        int x0 = cx - dir * hw - d;
        g.drawLine(x0, cy - hh, cx + dir * hw - d, cy);
        g.drawLine(cx + dir * hw - d, cy, x0, cy + hh);
      }
      return;
    }
    default: break;
  }

  int w = max(4, (int)lroundf(e.w));
  int hh = max(2, (int)lroundf(e.h * e.open * blink));
  int x0 = cx - w / 2;
  int y0 = cy - hh / 2;
  if (hh <= 4) { g.drawBox(x0, cy - 1, w, 3); return; }   // closed: a little line
  int r = min(radiusCap, min(w, hh) / 2 - 1);
  if (r >= 1) g.drawRBox(x0, y0, w, hh, r); else g.drawBox(x0, y0, w, hh);

  g.setDrawColor(0);
  if (e.lid > 0.02f) g.drawBox(x0 - 1, y0 - 1, w + 2, (int)(hh * e.lid) + 1);
  if (e.angry > 0.02f) {
    int innerX = left ? x0 + w + 1 : x0 - 1;
    g.drawTriangle(x0 - 1, y0 - 1, x0 + w + 1, y0 - 1, innerX, y0 + (int)(hh * e.angry));
  }
  if (e.worried > 0.02f) {
    int outerX = left ? x0 - 1 : x0 + w + 1;
    g.drawTriangle(x0 - 1, y0 - 1, x0 + w + 1, y0 - 1, outerX, y0 + (int)(hh * e.worried));
  }
  if (e.happy > 0.05f) {
    int ry = max(1, (int)(hh * e.happy));
    g.drawFilledEllipse(cx, y0 + hh + hh / 6, w / 2 + 2, ry);
  }
  g.setDrawColor(1);
}

void drawSmallHeart(U8G2& g, int x, int y) {
  g.drawDisc(x - 1, y, 1);
  g.drawDisc(x + 2, y, 1);
  g.drawTriangle(x - 3, y + 1, x + 4, y + 1, x, y + 4);
}

void drawDecor(U8G2& g, int ox, uint32_t now) {
  g.setFont(u8g2_font_5x7_tr);
  if (decor & DEC_Z) {
    for (int i = 0; i < 3; i++) {
      uint32_t ph = (now / 25 + i * 40) % 120;      // float upward in a loop
      int y = 30 - ph / 5;
      int x = 106 + i * 6 + (int)(sinf((now + i * 300) * 0.004f) * 2);
      if (y > 6) g.drawStr(ox + x, y, i == 1 ? "Z" : "z");
    }
  }
  if (decor & DEC_SWEAT) {
    int y = 4 + (now / 60) % 18;
    g.drawDisc(ox + 104, y, 2);
    g.drawTriangle(ox + 102, y - 1, ox + 106, y - 1, ox + 104, y - 5);
  }
  if (decor & DEC_BANG) {
    g.setFont(u8g2_font_7x13B_tr);
    g.drawStr(ox + 4, 22, "!!");
  }
  if (decor & DEC_QUEST) {
    g.setFont(u8g2_font_7x13B_tr);
    g.drawStr(ox + 108, 14, "?");
  }
  if (decor & DEC_HA) {
    g.setFont(u8g2_font_5x7_tr);
    bool flip = (now / 250) % 2;
    g.drawStr(ox + 2, flip ? 10 : 14, "ha");
    g.drawStr(ox + 112, flip ? 26 : 22, "ha");
    g.drawStr(ox + 6, flip ? 28 : 24, "ha");
  }
  if (decor & DEC_HEARTS) {
    for (int i = 0; i < 3; i++) {
      uint32_t ph = (now / 20 + i * 45) % 140;
      int y = 32 - ph / 4;
      int x = (i % 2 ? 112 : 10) + (int)(sinf((now + i * 500) * 0.005f) * 3);
      if (y > 2) drawSmallHeart(g, ox + x, y);
    }
  }
  if (decor & DEC_BOOP) {
    g.setFont(u8g2_font_5x7_tr);
    g.drawStr(ox + 102, 9, "boop!");
  }
  if (decor & DEC_WOW) {
    g.setFont(u8g2_font_7x13B_tr);
    g.drawStr(ox + 110, 20, "!");
    g.drawStr(ox + 10, 20, "!");
  }
  if (decor & DEC_STARS) {
    for (int i = 0; i < 4; i++) {
      if (((now / 180) + i) % 3 == 0) continue;
      int x = (i < 2 ? 8 + i * 10 : 104 + (i - 2) * 12);
      int y = 6 + (i * 7) % 20;
      g.drawPixel(ox + x, y);
      g.drawPixel(ox + x - 1, y); g.drawPixel(ox + x + 1, y);
      g.drawPixel(ox + x, y - 1); g.drawPixel(ox + x, y + 1);
    }
  }
}

} // namespace

namespace Face {

void begin() {
  baseline(MOOD_HAPPY);
  cur[0] = cur[1] = tgt[0];
  cur[0].open = cur[1].open = 0.0f;       // wake up: eyes open on boot
}

void setMood(Mood m) {
  if (m != moodNow) { nextSaccade = 0; nextIdle = millis() + 800; }
  moodNow = m;
}
Mood mood() { return moodNow; }

Mood moodFromString(const char* s) {
  if (!s) return MOOD_HAPPY;
  if (!strcmp(s, "curious"))  return MOOD_CURIOUS;
  if (!strcmp(s, "antsy"))    return MOOD_ANTSY;
  if (!strcmp(s, "grumpy"))   return MOOD_GRUMPY;
  if (!strcmp(s, "meltdown")) return MOOD_MELTDOWN;
  if (!strcmp(s, "sleepy"))   return MOOD_SLEEPY;
  return MOOD_HAPPY;
}

void setPending(int n) { pending = n < 0 ? 0 : n; }
void setAlone(bool a) { alone = a; }

void playGag(Gag g, uint16_t durationMs) {
  gag = g;
  gagStart = millis();
  gagDur = durationMs;
}

void playRandomGag() {
  static Gag last = GAG_NONE;
  Gag g;
  do { g = (Gag)random(1, GAG_COUNT); } while (g == last);
  last = g;
  playGag(g, g == GAG_BOOP ? 1600 : 2400);
}

void react() { playGag(GAG_SURPRISED, 900); }

void update(uint32_t now) {
  Mood m = alone ? MOOD_SLEEPY : moodNow;

  offX = offY = 0;
  decor = 0;
  shape[0] = shape[1] = SH_NORMAL;
  baseline(m);

  if (now >= nextSaccade) scheduleSaccade(now, m);
  if (now >= nextBlink) scheduleBlink(now, m);

  if (gag != GAG_NONE && now - gagStart >= gagDur) gag = GAG_NONE;
  if (gag != GAG_NONE) gagBehaviour(now);
  else idleBehaviour(now, m);

  float k = (m == MOOD_MELTDOWN || gag != GAG_NONE) ? 0.45f : 0.28f;
  for (int i = 0; i < 2; i++) {
    cur[i].w = approach(cur[i].w, tgt[i].w, k);
    cur[i].h = approach(cur[i].h, tgt[i].h, k);
    cur[i].open = approach(cur[i].open, tgt[i].open, 0.35f);
    cur[i].lid = approach(cur[i].lid, tgt[i].lid, k);
    cur[i].angry = approach(cur[i].angry, tgt[i].angry, k);
    cur[i].worried = approach(cur[i].worried, tgt[i].worried, k);
    cur[i].happy = approach(cur[i].happy, tgt[i].happy, k);
  }
  lookX = approach(lookX, tgtLookX, 0.35f);
  lookY = approach(lookY, tgtLookY, 0.35f);
}

void draw(U8G2& g, int ox) {
  uint32_t now = millis();
  float blink = (gag == GAG_NONE) ? blinkFactor(now) : 1.0f;
  float lx = clampf(lookX + offX, -24, 24);
  float ly = clampf(lookY + offY, -5, 5);
  int cy = 16 + (int)lroundf(ly);
  int cxL = 64 - (int)lroundf(EYE_GAP / 2.0f + cur[0].w / 2.0f) + (int)lroundf(lx);
  int cxR = 64 + (int)lroundf(EYE_GAP / 2.0f + cur[1].w / 2.0f) + (int)lroundf(lx);

  g.setDrawColor(1);
  drawEye(g, ox + cxL, cy, cur[0], blink, true, shape[0], now);
  drawEye(g, ox + cxR, cy, cur[1], blink, false, shape[1], now);
  drawDecor(g, ox, now);

  // Pending-items badge (top-right): "you have stuff to do, human".
  if (pending > 0 && !alone) {
    char b[6];
    snprintf(b, sizeof(b), "%d", pending > 99 ? 99 : pending);
    g.setFont(u8g2_font_5x7_tr);
    int w = g.getStrWidth(b) + 4;
    g.drawRBox(ox + 127 - w, 0, w, 9, 2);
    g.setDrawColor(0);
    g.drawStr(ox + 129 - w, 8, b);
    g.setDrawColor(1);
  }

  // Lonely indicator: blinking Bluetooth rune + "no PC".
  if (alone && (now / 700) % 2 == 0) {
    int x = ox + 1, y = 1;
    g.drawLine(x + 2, y, x + 2, y + 8);
    g.drawLine(x + 2, y, x + 4, y + 2);
    g.drawLine(x + 4, y + 2, x, y + 6);
    g.drawLine(x + 2, y + 8, x + 4, y + 6);
    g.drawLine(x + 4, y + 6, x, y + 2);
    g.setFont(u8g2_font_4x6_tr);
    g.drawStr(x + 7, y + 7, "no PC");
  }
}

void drawSingle(U8G2& g, int eye, int ox, float lookBias, int dy) {
  uint32_t now = millis();
  eye = eye ? 1 : 0;
  float blink = (gag == GAG_NONE) ? blinkFactor(now) : 1.0f;
  float lx = clampf((lookX + offX) * BIG_LOOK + lookBias, -36, 36);
  float ly = clampf(lookY + offY + dy, -4, 4);

  EyeStyle e = cur[eye];
  e.w *= BIG_W;
  e.h = fminf(e.h * BIG_H, 30.0f);

  g.setDrawColor(1);
  radiusCap = 9;
  drawEye(g, ox + 64 + (int)lroundf(lx), 16 + (int)lroundf(ly), e, blink, eye == 0, shape[eye], now);
  radiusCap = 6;

  // Decorations live in the outer margins: the left screen gets the left-hand ones, the right
  // screen the right-hand ones, so together the two panels read as one face.
  if (eye == 0) g.setClipWindow(0, 0, 64, 32); else g.setClipWindow(64, 0, 128, 32);
  drawDecor(g, ox, now);
  g.setMaxClipWindow();

  if (eye == 1 && pending > 0 && !alone) {
    char b[6];
    snprintf(b, sizeof(b), "%d", pending > 99 ? 99 : pending);
    g.setFont(u8g2_font_5x7_tr);
    int w = g.getStrWidth(b) + 4;
    g.drawRBox(ox + 127 - w, 0, w, 9, 2);
    g.setDrawColor(0);
    g.drawStr(ox + 129 - w, 8, b);
    g.setDrawColor(1);
  }
  if (eye == 0 && alone && (now / 700) % 2 == 0) {
    g.setFont(u8g2_font_4x6_tr);
    g.drawStr(ox + 1, 7, "no PC");
  }
}

FaceInfo info() {
  FaceInfo f;
  f.mood = alone ? MOOD_SLEEPY : moodNow;
  f.gag = gag;
  f.decor = decor;
  f.pending = pending;
  f.alone = alone;
  f.lookX = clampf(lookX + offX, -24, 24);
  f.lookY = clampf(lookY + offY, -5, 5);
  return f;
}

void drawEyeScaled(U8G2& g, int eye, int cx, int cy, float sx, float sy, int radius) {
  uint32_t now = millis();
  eye = eye ? 1 : 0;
  float blink = (gag == GAG_NONE) ? blinkFactor(now) : 1.0f;
  EyeStyle e = cur[eye];
  e.w *= sx;
  e.h *= sy;
  int saved = radiusCap;
  radiusCap = radius;
  g.setDrawColor(1);
  drawEye(g, cx, cy, e, blink, eye == 0, shape[eye], now);
  radiusCap = saved;
}

void drawMini(U8G2& g, int cx, int cy, float scale, MiniExpr expr, uint32_t now) {
  EyeStyle e = style(24 * scale, 22 * scale);
  Shape sh = SH_NORMAL;
  switch (expr) {
    case MINI_SURPRISED: e = style(26 * scale, 26 * scale); break;
    case MINI_HAPPY:     e.happy = 0.5f; break;
    case MINI_WORRIED:   e.worried = 0.4f; break;
    case MINI_ANGRY:     e.angry = 0.55f; e.h *= 0.85f; break;
    default: break;
  }
  // Mini faces blink too.
  uint32_t ph = now % 3200;
  float blink = ph < 140 ? fabsf(1.0f - 2.0f * ph / 140.0f) : 1.0f;
  int gap = (int)(EYE_GAP * scale * 0.7f);
  int half = gap / 2 + (int)(e.w / 2);
  drawEye(g, cx - half, cy, e, blink, true, sh, now);
  drawEye(g, cx + half, cy, e, blink, false, sh, now);
}

} // namespace Face
