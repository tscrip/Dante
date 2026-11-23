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
int radiusCap = 6;   // corner radius limit

EyeStyle cur[2], tgt[2];
float lookX = 0, lookY = 0, tgtLookX = 0, tgtLookY = 0;
float offX = 0, offY = 0;
bool calmMode = false;   // see Face::setCalm
Shape shape[2] = { SH_NORMAL, SH_NORMAL };
uint16_t decor = 0;

Mood moodNow = MOOD_HAPPY;
int pending = 0;
uint8_t look = LOOK_CLASSIC;
bool alone = true;

uint32_t blinkStart = 0, blinkDur = 140, nextBlink = 1500;
bool doubleBlink = false;
uint32_t nextSaccade = 0;
uint32_t idleStart = 0, idleUntil = 0, nextIdle = 3000;
uint8_t idleKind = 0;
Gag gag = GAG_NONE;
uint32_t gagStart = 0, gagDur = 0;
uint32_t sideEyeUntil = 0;
uint32_t frameNow = 0;   // time of the last update(): every strip of a TFT frame draws the same moment

inline float rnd(float a, float b) { return a + (b - a) * (float)random(0, 10001) / 10000.0f; }
inline uint32_t rndMs(uint32_t a, uint32_t b) { return (uint32_t)random(a, b + 1); }
// Eases towards the target and lands on it once close. Plain easing never quite arrives: the eyes would
// keep creeping by fractions of a pixel for a dozen frames, and on the big screen each of those frames
// means redrawing and resending the eyes.
inline float ease(float c, float t, float k, float snap) {
  float n = c + (t - c) * k;
  return fabsf(t - n) < snap ? t : n;
}
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

// The look reshapes the mood's eyes rather than replacing them, so moods stay readable.
void lookStyle(Mood m) {
  for (auto& t : tgt) {
    switch (look) {
      case LOOK_GENTLEMAN:   // composed: a little narrower, calmly half-lidded
        t.w *= 0.92f; t.h *= 0.85f; t.lid = fmaxf(t.lid, 0.14f);
        break;
      case LOOK_NERD:        // round eyes (the glasses frame them)
        t.w = fminf(t.w, t.h + 1.0f) * 0.92f; t.h *= 0.92f;
        break;
      case LOOK_UNCLE:       // a built-in frown under those brows
        t.angry = fminf(t.angry + 0.3f, 0.85f); t.h *= 0.9f;
        break;
      case LOOK_PARTY:       // big and bright
        t.w *= 1.08f; t.h = fminf(t.h * 1.1f, 27.0f);
        if (m == MOOD_HAPPY) t.happy = fmaxf(t.happy, 0.55f);
        break;
      default: break;
    }
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
  // Habits of the look.
  if (look == LOOK_GENTLEMAN) nextSaccade += rndMs(800, 1800);                   // unhurried
  if (look == LOOK_NERD && m != MOOD_MELTDOWN && random(0, 100) < 30) {          // "hmm, let me think"
    tgtLookX = rnd(-6, 6); tgtLookY = -5;
    nextSaccade += 600;
  }
  if (look == LOOK_PARTY) nextSaccade -= (nextSaccade - now) / 3;                // can't sit still
}

void scheduleBlink(uint32_t now, Mood m) {
  blinkStart = now;
  blinkDur = (m == MOOD_SLEEPY || m == MOOD_GRUMPY) ? 320 : 140;
  doubleBlink = (m == MOOD_ANTSY || m == MOOD_CURIOUS) && random(0, 100) < 35;
  uint32_t lo = 2000, hi = 5000;
  if (m == MOOD_SLEEPY) { lo = 1400; hi = 3000; }
  if (m == MOOD_MELTDOWN) { lo = 600; hi = 1600; }
  if (look == LOOK_GENTLEMAN) { blinkDur = 260; lo += 1500; hi += 2500; doubleBlink = false; }   // slow, deliberate
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
    case GAG_MAGIC:              // "abracadabra": happy squint up at the sparkles
      tgt[0] = tgt[1] = style(24, 20, 0, 0, 0, 0.5f);
      tgtLookX = 0; tgtLookY = -4;
      offY = -fabsf(sinf(t * 0.02f)) * 2.0f;
      decor |= DEC_STARS;
      break;
    default: break;
  }
}

// ---- drawing ---------------------------------------------------------------------
void drawEye(Gfx& g, int cx, int cy, const EyeStyle& e, float blink, bool left, Shape sh, uint32_t now) {
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

// The normal eye with fractional position and size, for screens that can draw them (the TFT): the eye
// glides one screen pixel at a time and its edges are anti-aliased. Same geometry as drawEye().
void drawEyeFine(Gfx& g, float cx, float cy, const EyeStyle& e, float blink, bool left) {
  float w = fmaxf(4.0f, e.w);
  float hh = fmaxf(2.0f, e.h * e.open * blink);
  float x0 = cx - w / 2, y0 = cy - hh / 2;
  if (hh <= 4) { g.fillRoundRectF(x0, cy - 1.5f, w, 3, 1.5f); return; }   // closed: a little line
  float r = fminf((float)radiusCap, fminf(w, hh) / 2 - 1);
  g.fillRoundRectF(x0, y0, w, hh, r > 0 ? r : 0);

  g.setDrawColor(0);
  if (e.lid > 0.02f) g.fillRectF(x0 - 1, y0 - 1, w + 2, hh * e.lid + 1);
  if (e.angry > 0.02f) {
    float innerX = left ? x0 + w + 1 : x0 - 1;
    g.fillTriangleF(x0 - 1, y0 - 1, x0 + w + 1, y0 - 1, innerX, y0 + hh * e.angry);
  }
  if (e.worried > 0.02f) {
    float outerX = left ? x0 - 1 : x0 + w + 1;
    g.fillTriangleF(x0 - 1, y0 - 1, x0 + w + 1, y0 - 1, outerX, y0 + hh * e.worried);
  }
  if (e.happy > 0.05f) g.fillEllipseF(cx, y0 + hh + hh / 6, w / 2 + 2, fmaxf(1.0f, hh * e.happy));
  g.setDrawColor(1);
}

void drawSmallHeart(Gfx& g, int x, int y) {
  g.drawDisc(x - 1, y, 1);
  g.drawDisc(x + 2, y, 1);
  g.drawTriangle(x - 3, y + 1, x + 4, y + 1, x, y + 4);
}

void drawDecor(Gfx& g, int ox, uint32_t now) {
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

// ---- accessories (looks) ---------------------------------------------------------
// Drawn in the face's own coordinates (logical 128x32): crisp and scaled up on the TFT, a few
// pixels on the OLED. They follow the eyes so the whole face moves together.

// A curly handlebar mustache under the eyes.
void drawMustache(Gfx& g, float cx, float my) {
  g.setDrawColor(1);
  g.fillEllipseF(cx - 6.5f, my, 7.0f, 2.3f);
  g.fillEllipseF(cx + 6.5f, my, 7.0f, 2.3f);
  g.fillEllipseF(cx - 13.8f, my - 1.6f, 1.9f, 1.5f);   // the curls
  g.fillEllipseF(cx + 13.8f, my - 1.6f, 1.9f, 1.5f);
  g.setDrawColor(0);
  g.fillTriangleF(cx - 1.4f, my - 2.6f, cx + 1.4f, my - 2.6f, cx, my + 0.4f);   // the parting
  g.setDrawColor(1);
}

// Round glasses: drawn before the eyes as a ring (frame), plus the bridge and the arms.
void drawGlasses(Gfx& g, int ox, float xL, float xR, float cy) {
  const float w = fmaxf(cur[0].w, cur[1].w) + 6.0f, h = 27.0f, t = 1.4f;
  for (float x : { xL, xR }) {
    g.setDrawColor(1);
    g.fillRoundRectF(ox + x - w / 2, cy - h / 2, w, h, 7.0f);
    g.setDrawColor(0);
    g.fillRoundRectF(ox + x - w / 2 + t, cy - h / 2 + t, w - 2 * t, h - 2 * t, 6.0f);
  }
  g.setDrawColor(1);
  g.fillRectF(ox + xL + w / 2, cy - 4.0f, (xR - xL) - w, 1.3f);                  // bridge
  g.fillRectF(ox + xL - w / 2 - 6.0f, cy - 7.0f, 6.0f, 1.2f);                     // arms
  g.fillRectF(ox + xR + w / 2, cy - 7.0f, 6.0f, 1.2f);
}

// Heavy eyebrows slanting down towards the nose, and a bristly mustache.
void drawUncle(Gfx& g, int ox, float xL, float xR, float cy, float cx) {
  g.setDrawColor(1);
  for (int side = 0; side < 2; side++) {
    float x = side ? xR : xL, w = cur[side].w + 3.0f;
    float top = cy - cur[side].h / 2 - 1.5f;
    float outer = side ? x + w / 2 : x - w / 2, inner = side ? x - w / 2 : x + w / 2;
    // A thick bar: higher at the outer end, lower at the inner end.
    g.fillTriangleF(ox + outer, top - 4.0f, ox + inner, top - 1.0f, ox + inner, top + 1.6f);
    g.fillTriangleF(ox + outer, top - 4.0f, ox + outer, top - 1.4f, ox + inner, top + 1.6f);
  }
  float my = cy + 13.0f;
  g.fillRoundRectF(ox + cx - 11.0f, my - 1.6f, 22.0f, 3.4f, 1.6f);
  g.setDrawColor(0);
  for (float x = cx - 9.0f; x < cx + 10.0f; x += 2.5f) g.fillRectF(ox + x, my + 0.6f, 0.7f, 1.4f);   // bristles
  g.setDrawColor(1);
}

// A striped party hat with a pom-pom, perched at the right eye's top corner.
void drawPartyHat(Gfx& g, int ox, float xR, float cy) {
  float top = cy - cur[1].h / 2;
  float bx = ox + xR + cur[1].w / 2 + 2.0f;
  g.setDrawColor(1);
  g.fillTriangleF(bx, top + 9.0f, bx + 12.0f, top + 9.0f, bx + 8.0f, top - 1.0f);
  g.setDrawColor(0);
  g.fillTriangleF(bx + 3.2f, top + 6.0f, bx + 9.6f, top + 6.0f, bx + 8.4f, top + 4.8f);   // stripe
  g.fillTriangleF(bx + 5.0f, top + 2.6f, bx + 9.0f, top + 2.6f, bx + 8.6f, top + 1.4f);   // stripe
  g.setDrawColor(1);
  g.fillEllipseF(bx + 8.0f, top - 1.5f, 1.8f, 1.8f);                                      // pom-pom
}

void drawAccessories(Gfx& g, int ox, float xL, float xR, float cy, float lx) {
  const float cx = ox + 64 + lx * 0.6f;   // the "nose": between the eyes, following the gaze a little
  switch (look) {
    case LOOK_GENTLEMAN: drawMustache(g, cx, cy + 13.2f); break;
    case LOOK_UNCLE:     drawUncle(g, ox, xL, xR, cy, cx - ox); break;
    case LOOK_PARTY:     drawPartyHat(g, ox, xR, cy); break;
    default: break;   // classic: as nature intended; nerd: glasses are drawn before the eyes
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
void setLook(uint8_t l) { look = l < LOOK_COUNT ? l : (uint8_t)LOOK_CLASSIC; }
void setAlone(bool a) { alone = a; }
void setCalm(bool c) { calmMode = c; }
bool calm() { return calmMode; }

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
  frameNow = now;
  Mood m = alone ? MOOD_SLEEPY : moodNow;

  offX = offY = 0;
  decor = 0;
  shape[0] = shape[1] = SH_NORMAL;
  baseline(m);
  lookStyle(m);

  if (now >= nextSaccade) scheduleSaccade(now, m);
  if (now >= nextBlink) scheduleBlink(now, m);

  if (gag != GAG_NONE && now - gagStart >= gagDur) gag = GAG_NONE;
  if (gag != GAG_NONE) gagBehaviour(now);
  else idleBehaviour(now, m);
  if (look == LOOK_PARTY && gag == GAG_NONE && m == MOOD_HAPPY) {
    offY -= fabsf(sinf(now * 0.006f)) * 1.5f;            // bopping to the music
    if ((now / 2500) % 3 == 0) decor |= DEC_STARS;       // and some confetti
  }

  // Calm: the face keeps still. Hops, foot tapping, shaking and jitter move both whole eyes, which is
  // most of the screen; expressions, blinks and glances stay.
  if (calmMode) offX = offY = 0;

  float k = (m == MOOD_MELTDOWN || gag != GAG_NONE) ? 0.45f : 0.28f;
  const float snap = calmMode ? 0.15f : 0.04f;          // logical pixels: calm lands sooner
  const float sizeSnap = calmMode ? 0.02f : 0.005f;     // for the 0..1 lid / mood amounts
  for (int i = 0; i < 2; i++) {
    cur[i].w = ease(cur[i].w, tgt[i].w, k, snap);
    cur[i].h = ease(cur[i].h, tgt[i].h, k, snap);
    cur[i].open = ease(cur[i].open, tgt[i].open, 0.35f, sizeSnap);
    cur[i].lid = ease(cur[i].lid, tgt[i].lid, k, sizeSnap);
    cur[i].angry = ease(cur[i].angry, tgt[i].angry, k, sizeSnap);
    cur[i].worried = ease(cur[i].worried, tgt[i].worried, k, sizeSnap);
    cur[i].happy = ease(cur[i].happy, tgt[i].happy, k, sizeSnap);
  }
  // Glances: calm ones get there in a few frames instead of gliding over many.
  const float lookK = calmMode ? 0.6f : 0.35f;
  lookX = ease(lookX, tgtLookX, lookK, snap);
  lookY = ease(lookY, tgtLookY, lookK, snap);
}

void draw(Gfx& g, int ox, bool chrome) {
  uint32_t now = frameNow;
  float blink = (gag == GAG_NONE) ? blinkFactor(now) : 1.0f;
  float lx = clampf(lookX + offX, -24, 24);
  float ly = clampf(lookY + offY, -5, 5);
  int cy = 16 + (int)lroundf(ly);
  int cxL = 64 - (int)lroundf(EYE_GAP / 2.0f + cur[0].w / 2.0f) + (int)lroundf(lx);
  int cxR = 64 + (int)lroundf(EYE_GAP / 2.0f + cur[1].w / 2.0f) + (int)lroundf(lx);

  g.setDrawColor(1);
  float axL = 64 - (EYE_GAP / 2.0f + cur[0].w / 2.0f) + lx, axR = 64 + (EYE_GAP / 2.0f + cur[1].w / 2.0f) + lx;
  if (look == LOOK_NERD) drawGlasses(g, ox, axL, axR, 16 + ly);
  if (g.fine()) {
    // Big screen: no rounding to the 128x32 grid, so the eyes glide and resize smoothly.
    float fcy = 16 + ly;
    float fxL = 64 - (EYE_GAP / 2.0f + cur[0].w / 2.0f) + lx, fxR = 64 + (EYE_GAP / 2.0f + cur[1].w / 2.0f) + lx;
    if (shape[0] == SH_NORMAL) drawEyeFine(g, ox + fxL, fcy, cur[0], blink, true);
    else drawEye(g, ox + (int)lroundf(fxL), (int)lroundf(fcy), cur[0], blink, true, shape[0], now);
    if (shape[1] == SH_NORMAL) drawEyeFine(g, ox + fxR, fcy, cur[1], blink, false);
    else drawEye(g, ox + (int)lroundf(fxR), (int)lroundf(fcy), cur[1], blink, false, shape[1], now);
  } else {
    drawEye(g, ox + cxL, cy, cur[0], blink, true, shape[0], now);
    drawEye(g, ox + cxR, cy, cur[1], blink, false, shape[1], now);
  }
  drawAccessories(g, ox, axL, axR, 16 + ly, lx);
  drawDecor(g, ox, now);

  if (!chrome) return;   // the TFT shows these in its status bar

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

void drawMini(Gfx& g, int cx, int cy, float scale, MiniExpr expr, uint32_t now) {
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
