#include "Apps.h"
#include <math.h>

namespace {
  App apps[MAX_APPS];
  int appCount = 0;

  void copyStr(char* dst, size_t n, const char* src) {
    if (!src) src = "";
    strncpy(dst, src, n - 1);
    dst[n - 1] = 0;
  }

  Visual visFromString(const char* s) {
    if (!s) return VIS_TEXT;
    if (!strcmp(s, "gauge"))   return VIS_GAUGE;
    if (!strcmp(s, "bar"))     return VIS_BAR;
    if (!strcmp(s, "counter")) return VIS_COUNTER;
    if (!strcmp(s, "spark"))   return VIS_SPARK;
    if (!strcmp(s, "status"))  return VIS_STATUS;
    if (!strcmp(s, "timer"))   return VIS_TIMER;
    return VIS_TEXT;
  }

  float frac(const App& a, float v) {
    if (a.max <= a.min) return 0;
    float f = (v - a.min) / (a.max - a.min);
    return f < 0 ? 0 : (f > 1 ? 1 : f);
  }

  void fmtValue(float v, char* out, size_t n) {
    if (fabsf(v - roundf(v)) < 0.05f || fabsf(v) >= 100) snprintf(out, n, "%.0f", v);
    else snprintf(out, n, "%.1f", v);
  }

  float easeOutBounce(float x) {
    const float n1 = 7.5625f, d1 = 2.75f;
    if (x < 1 / d1) return n1 * x * x;
    if (x < 2 / d1) { x -= 1.5f / d1; return n1 * x * x + 0.75f; }
    if (x < 2.5f / d1) { x -= 2.25f / d1; return n1 * x * x + 0.9375f; }
    x -= 2.625f / d1; return n1 * x * x + 0.984375f;
  }

  // Critical values blink to get attention.
  bool blinkHidden(const App& a, uint32_t now) { return a.lvl >= 2 && (now % 1000) < 280; }

  void drawHeader(Gfx& g, const App& a, int ox, int page, int pages) {
    g.setFont(u8g2_font_4x6_tr);
    int dotsW = pages * 4;
    int maxChars = (128 - dotsW - 4) / 4;
    char name[NAME_LEN];
    copyStr(name, sizeof(name), a.name);
    if ((int)strlen(name) > maxChars) name[maxChars] = 0;
    g.drawStr(ox + 0, 5, name);
    int x = ox + 128 - dotsW;
    for (int i = 0; i < pages; i++, x += 4) {
      if (i == page) g.drawBox(x, 1, 3, 3);
      else g.drawPixel(x + 1, 2);
    }
    if (a.lvl == 1) g.drawStr(ox + 128 - dotsW - 6, 5, "!");
    if (a.lvl >= 2) g.drawStr(ox + 128 - dotsW - 10, 5, "!!");
  }

  void drawSmallText(Gfx& g, const App& a, int ox, int x, int y, uint32_t now) {
    if (!a.txt[0]) return;
    g.setFont(u8g2_font_4x6_tr);
    drawMarquee(g, a.txt, ox + x, y, 128 - x, now, a.changedAt, y - 6, y + 1);
  }

  // ---- visuals -------------------------------------------------------------------
  void drawGauge(Gfx& g, const App& a, int ox, uint32_t now) {
    const int cx = ox + 22, cy = 30, ro = 20, ri = 13;
    float f = frac(a, a.shown);
    // Filled arc from the left (180deg) clockwise to the value.
    for (float d = 0; d <= f * 180.0f; d += 2.0f) {
      float ang = (180.0f - d) * DEG_TO_RAD;
      float c = cosf(ang), s = sinf(ang);
      g.drawLine(cx + (int)(c * ri), cy - (int)(s * ri), cx + (int)(c * ro), cy - (int)(s * ro));
    }
    g.drawCircle(cx, cy, ro, U8G2_DRAW_UPPER_LEFT | U8G2_DRAW_UPPER_RIGHT);
    g.drawCircle(cx, cy, ri, U8G2_DRAW_UPPER_LEFT | U8G2_DRAW_UPPER_RIGHT);
    g.drawHLine(cx - ro, cy, ro - ri + 1);
    g.drawHLine(cx + ri, cy, ro - ri + 1);
    // Needle (XOR so it shows over the filled arc), with a tiny wobble when it moves.
    float wob = fabsf(a.shown - a.value) > 0.5f ? sinf(now * 0.05f) * 2.0f : 0;
    float ang = (180.0f - f * 180.0f + wob) * DEG_TO_RAD;
    g.setDrawColor(2);
    g.drawLine(cx, cy, cx + (int)(cosf(ang) * (ro + 1)), cy - (int)(sinf(ang) * (ro + 1)));
    g.setDrawColor(1);
    g.drawDisc(cx, cy, 2);

    if (!blinkHidden(a, now)) {
      char v[12];
      if (a.hasValue) fmtValue(a.shown, v, sizeof(v)); else copyStr(v, sizeof(v), "--");
      g.setFont(u8g2_font_logisoso16_tr);
      g.drawStr(ox + 48, 25, v);
      int w = g.getStrWidth(v);
      g.setFont(u8g2_font_6x10_tr);
      g.drawStr(ox + 50 + w, 25, a.unit);
    }
    drawSmallText(g, a, ox, 48, 31, now);
  }

  void drawBar(Gfx& g, const App& a, int ox, uint32_t now) {
    if (!blinkHidden(a, now)) {
      char v[12];
      if (a.hasValue) fmtValue(a.shown, v, sizeof(v)); else copyStr(v, sizeof(v), "--");
      g.setFont(u8g2_font_7x13B_tr);
      g.drawStr(ox + 0, 18, v);
      int w = g.getStrWidth(v);
      g.setFont(u8g2_font_5x7_tr);
      g.drawStr(ox + w + 2, 18, a.unit);
    }
    if (a.txt[0]) {
      g.setFont(u8g2_font_4x6_tr);
      int tw = g.getStrWidth(a.txt);
      if (tw < 70) g.drawStr(ox + 128 - tw, 17, a.txt);
      else drawMarquee(g, a.txt, ox + 58, 17, 70, now, a.changedAt, 11, 18);
    }
    int bw = 126, fill = (int)(frac(a, a.shown) * (bw - 4));
    g.drawRFrame(ox + 0, 21, 128, 11, 3);
    if (fill > 0) g.drawRBox(ox + 2, 23, fill, 7, 2);
    // marching highlight on the bar edge while it is growing
    if (fabsf(a.shown - a.value) > 0.3f && fill > 4) {
      g.setDrawColor(0);
      g.drawVLine(ox + 2 + fill - 2, 24, 5);
      g.setDrawColor(1);
    }
  }

  void drawCounter(Gfx& g, const App& a, int ox, uint32_t now) {
    char v[12];
    if (a.hasValue) snprintf(v, sizeof(v), "%ld", lroundf(a.value)); else copyStr(v, sizeof(v), "-");
    // Drop-in bounce when the number changes.
    uint32_t t = now - a.changedAt;
    int dy = 0;
    if (a.hasValue && t < 700) dy = (int)(-26.0f * (1.0f - easeOutBounce(t / 700.0f)));
    g.setFont(u8g2_font_logisoso22_tn);
    int w = g.getStrWidth(v);
    if (!blinkHidden(a, now)) {
      g.setClipWindow(0, 7, 128, 32);          // don't fall through the header
      g.drawStr(ox + 2, 31 + dy, v);
      g.setMaxClipWindow();
    }

    // "+2!" floating up when it increases.
    if (a.hasValue && t < 1400 && a.value != a.prevValue) {
      char d[10];
      long delta = lroundf(a.value - a.prevValue);
      snprintf(d, sizeof(d), delta > 0 ? "+%ld!" : "%ld", delta);
      g.setFont(u8g2_font_5x7_tr);
      g.drawStr(ox + w + 6, 20 - (int)(t / 120), d);
    }
    g.setFont(u8g2_font_5x7_tr);
    if (a.unit[0]) g.drawStr(ox + w + 6, 31, a.unit);
    if (a.txt[0]) {
      g.setFont(u8g2_font_4x6_tr);
      int x = w + 8 + (a.unit[0] ? g.getStrWidth(a.unit) + 6 : 0);
      if (x < 70) x = 70;
      drawMarquee(g, a.txt, ox + x, 30, 128 - x, now, a.changedAt, 24, 31);
    }
  }

  void drawText(Gfx& g, const App& a, int ox, uint32_t now) {
    g.setFont(u8g2_font_7x13_tr);
    const char* s = a.txt[0] ? a.txt : (a.hasValue ? "" : "waiting...");
    char buf[TXT_LEN + 16];
    if (!a.txt[0] && a.hasValue) {
      char v[12]; fmtValue(a.value, v, sizeof(v));
      snprintf(buf, sizeof(buf), "%s %s", v, a.unit);
      s = buf;
    }
    if (blinkHidden(a, now)) return;
    drawMarquee(g, s, ox, 24, 128, now, a.changedAt, 8, 31);
  }

  void drawSpark(Gfx& g, const App& a, int ox, uint32_t now) {
    if (a.hasValue && !blinkHidden(a, now)) {
      char v[16], buf[24];
      fmtValue(a.value, v, sizeof(v));
      snprintf(buf, sizeof(buf), "%s%s", v, a.unit);
      g.setFont(u8g2_font_4x6_tr);
      int nx = g.getStrWidth(a.name) + 6;      // value sits right after the name
      g.setFont(u8g2_font_5x7_tr);
      g.drawStr(ox + nx, 7, buf);
    }
    if (a.histN < 2) return;
    float lo = a.min, hi = a.max;
    if (hi <= lo) {
      lo = 1e9f; hi = -1e9f;
      for (int i = 0; i < a.histN; i++) { lo = fminf(lo, a.hist[i]); hi = fmaxf(hi, a.hist[i]); }
      if (hi - lo < 1e-3f) { hi = lo + 1; }
    }
    const int top = 9, bottom = 31, n = a.histN;
    int start = (a.histHead - n + HIST_LEN) % HIST_LEN;
    float step = 127.0f / (HIST_LEN - 1);
    int px = -1, py = -1;
    for (int i = 0; i < n; i++) {
      float v = a.hist[(start + i) % HIST_LEN];
      float f = (v - lo) / (hi - lo); f = f < 0 ? 0 : (f > 1 ? 1 : f);
      int x = ox + (int)((HIST_LEN - n + i) * step);
      int y = bottom - (int)(f * (bottom - top));
      if (px >= 0 || i > 0) g.drawLine(px, py, x, y);
      px = x; py = y;
    }
    if ((now / 300) % 2) g.drawDisc(px, py, 2); else g.drawCircle(px, py, 2);
  }

  // Countdown: big m:ss, the text beside it, a progress bar along the bottom.
  void drawTimer(Gfx& g, const App& a, int ox, uint32_t now) {
    float left = Apps::secondsLeft(a, now);
    bool done = a.hasTimer && a.timerRun && left <= 0;
    char clock[12];
    if (a.hasValue) Apps::formatClock(left, clock, sizeof(clock)); else copyStr(clock, sizeof(clock), "--:--");
    // Paused: the clock blinks slowly. Ran out: it flashes.
    bool hide = (done && (now % 600) < 300) || (a.hasTimer && !a.timerRun && left > 0 && a.timerTotal > left && (now % 1600) < 400);
    g.setFont(u8g2_font_logisoso16_tr);
    int w = g.getStrWidth(clock);
    if (!hide) g.drawStr(ox + 0, 25, clock);
    if (a.txt[0]) {
      g.setFont(u8g2_font_5x7_tr);
      drawMarquee(g, a.txt, ox + w + 6, 21, 128 - w - 6, now, a.changedAt, 13, 23);
    }
    // Progress: fills up as time passes.
    float frac = a.hasTimer && a.timerTotal > 0 ? 1.0f - left / a.timerTotal : 0;
    frac = frac < 0 ? 0 : (frac > 1 ? 1 : frac);
    g.drawHLine(ox + 0, 28, 128);
    g.drawHLine(ox + 0, 31, 128);
    g.drawVLine(ox + 0, 28, 4);
    g.drawVLine(ox + 127, 28, 4);
    int fill = (int)(frac * 126);
    if (fill > 0) g.drawBox(ox + 1, 29, fill, 2);
    if (a.hasTimer && !a.timerRun) {            // pause sign at the end of the bar
      g.setDrawColor(0);
      g.drawBox(ox + 120, 27, 8, 5);
      g.setDrawColor(1);
      g.drawBox(ox + 122, 27, 2, 5);
      g.drawBox(ox + 125, 27, 2, 5);
    }
  }

  void drawStatus(Gfx& g, const App& a, int ox, uint32_t now) {
    const int cx = ox + 12, cy = 19;
    bool hide = a.lvl >= 2 && (now % 800) < 250;
    if (!hide) {
      if (a.lvl == 0) {
        g.drawCircle(cx, cy, 10);
        g.drawLine(cx - 5, cy, cx - 1, cy + 4); g.drawLine(cx - 5, cy + 1, cx - 1, cy + 5);
        g.drawLine(cx - 1, cy + 4, cx + 5, cy - 4); g.drawLine(cx - 1, cy + 5, cx + 5, cy - 3);
      } else if (a.lvl == 1) {
        g.drawTriangle(cx, cy - 11, cx - 11, cy + 9, cx + 11, cy + 9);
        g.setDrawColor(0);
        g.drawBox(cx - 1, cy - 4, 3, 8);
        g.drawBox(cx - 1, cy + 5, 3, 2);
        g.setDrawColor(1);
      } else {
        g.drawDisc(cx, cy, 10);
        g.setDrawColor(0);
        for (int d = -1; d <= 1; d++) {
          g.drawLine(cx - 5 + d, cy - 5, cx + 5 + d, cy + 5);
          g.drawLine(cx + 5 + d, cy - 5, cx - 5 + d, cy + 5);
        }
        g.setDrawColor(1);
      }
    }
    g.setFont(u8g2_font_6x10_tr);
    const char* s = a.txt[0] ? a.txt : (a.lvl == 0 ? "all good" : a.lvl == 1 ? "heads up" : "uh oh");
    drawMarquee(g, s, ox + 28, 22, 100, now, a.changedAt, 8, 31);
    if (a.hasValue) {
      char v[16], buf[24];
      fmtValue(a.value, v, sizeof(v));
      snprintf(buf, sizeof(buf), "%s%s", v, a.unit);
      g.setFont(u8g2_font_4x6_tr);
      g.drawStr(ox + 28, 31, buf);
    }
  }
}

void drawMarquee(Gfx& g, const char* text, int x, int y, int w, uint32_t now, uint32_t startMs, int clipTop, int clipBottom) {
  int tw = g.getStrWidth(text);
  if (tw <= w) { g.drawStr(x, y, text); return; }
  // Pause at the start, then scroll at ~30 px/s and wrap with a gap.
  const int gap = 24;
  uint32_t t = now - startMs;
  int period = tw + gap;
  int off = t < 1200 ? 0 : (int)(((t - 1200) * 30 / 1000) % period);
  int cl = x < 0 ? 0 : x, cr = x + w > 128 ? 128 : x + w;
  if (cr <= cl) return;
  g.setClipWindow(cl, clipTop < 0 ? 0 : clipTop, cr, clipBottom > 32 ? 32 : clipBottom);
  g.drawStr(x - off, y, text);
  g.drawStr(x - off + period, y, text);
  g.setMaxClipWindow();
}

namespace Apps {

void setList(JsonArrayConst arr) {
  static App old[MAX_APPS];
  int oldCount = appCount;
  memcpy(old, apps, sizeof(App) * oldCount);
  appCount = 0;
  for (JsonObjectConst o : arr) {
    if (appCount >= MAX_APPS) break;
    App& a = apps[appCount];
    memset(&a, 0, sizeof(App));
    copyStr(a.id, sizeof(a.id), o["id"] | "");
    if (!a.id[0]) continue;
    copyStr(a.name, sizeof(a.name), o["name"] | a.id);
    copyStr(a.unit, sizeof(a.unit), o["unit"] | "");
    a.vis = visFromString(o["vis"] | "text");
    a.min = o["min"] | 0.0f;
    a.max = o["max"] | 100.0f;
    // Keep the last known value/history if this app already existed.
    for (int i = 0; i < oldCount; i++) {
      if (!strcmp(old[i].id, a.id)) {
        a.hasValue = old[i].hasValue; a.value = old[i].value; a.shown = old[i].shown;
        a.prevValue = old[i].prevValue; a.lvl = old[i].lvl; a.changedAt = old[i].changedAt;
        memcpy(a.txt, old[i].txt, sizeof(a.txt));
        memcpy(a.hist, old[i].hist, sizeof(a.hist));
        a.histN = old[i].histN; a.histHead = old[i].histHead;
        a.hasTimer = old[i].hasTimer; a.timerRun = old[i].timerRun;
        a.timerTotal = old[i].timerTotal; a.timerAt = old[i].timerAt;
        break;
      }
    }
    appCount++;
  }
}

float secondsLeft(const App& a, uint32_t now) {
  if (!a.hasValue) return 0;
  if (!a.hasTimer || !a.timerRun) return a.value;
  float left = a.value - (now - a.timerAt) / 1000.0f;
  return left > 0 ? left : 0;
}

void formatClock(float seconds, char* out, size_t n) {
  long s = (long)ceilf(seconds < 0 ? 0 : seconds);   // a countdown rounds up: 20:00 right after it starts
  if (s >= 3600) snprintf(out, n, "%ldh%02ld", s / 3600, (s % 3600) / 60);
  else snprintf(out, n, "%ld:%02ld", s / 60, s % 60);
}

int count() { return appCount; }
App* get(int i) { return (i >= 0 && i < appCount) ? &apps[i] : nullptr; }

App* find(const char* id) {
  if (!id) return nullptr;
  for (int i = 0; i < appCount; i++) if (!strcmp(apps[i].id, id)) return &apps[i];
  return nullptr;
}

App* applyValue(JsonDocument& msg) {
  App* a = find(msg["id"] | "");
  if (!a) return nullptr;
  uint32_t now = millis();
  bool changed = false;
  if (msg["v"].is<float>()) {
    float v = msg["v"].as<float>();
    if (!a->hasValue) { a->shown = v; a->prevValue = v; }
    else a->prevValue = a->value;
    changed = !a->hasValue || v != a->value;
    a->value = v;
    a->hasValue = true;
    a->hist[a->histHead] = v;
    a->histHead = (a->histHead + 1) % HIST_LEN;
    if (a->histN < HIST_LEN) a->histN++;
  }
  if (msg["txt"].is<const char*>()) {
    const char* t = msg["txt"];
    if (strncmp(t, a->txt, TXT_LEN - 1)) changed = true;
    copyStr(a->txt, sizeof(a->txt), t);
  }
  if (a->vis == VIS_TIMER && !msg["v"].is<float>()) {   // no countdown right now (e.g. no next meeting)
    a->hasValue = false;
    a->hasTimer = false;
  }
  if (msg["tot"].is<float>()) {          // a countdown: restart the local clock from the PC's value
    a->hasTimer = true;
    a->timerTotal = msg["tot"].as<float>();
    a->timerRun = msg["run"] | false;
    a->timerAt = now;
  }
  a->lvl = msg["lvl"] | a->lvl;
  if (changed) a->changedAt = now;
  return a;
}

void update(uint32_t now) {
  (void)now;
  for (int i = 0; i < appCount; i++) {
    App& a = apps[i];
    if (!a.hasValue) continue;
    float d = a.value - a.shown;
    a.shown = fabsf(d) < 0.05f ? a.value : a.shown + d * 0.15f;
  }
}

void draw(Gfx& g, int index, int ox, int page, int pages, uint32_t now) {
  App* a = get(index);
  if (!a) return;
  g.setDrawColor(1);
  drawHeader(g, *a, ox, page, pages);
  switch (a->vis) {
    case VIS_GAUGE:   drawGauge(g, *a, ox, now); break;
    case VIS_BAR:     drawBar(g, *a, ox, now); break;
    case VIS_COUNTER: drawCounter(g, *a, ox, now); break;
    case VIS_SPARK:   drawSpark(g, *a, ox, now); break;
    case VIS_STATUS:  drawStatus(g, *a, ox, now); break;
    case VIS_TIMER:   drawTimer(g, *a, ox, now); break;
    default:          drawText(g, *a, ox, now); break;
  }
}

} // namespace Apps
