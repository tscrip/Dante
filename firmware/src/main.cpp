// DANTE firmware: a tiny desk companion with opinions.
// ESP32-S3 + one screen (SSD1306 128x32 OLED, 1602/2004 LCD or 3.5" ST7796 TFT) + 4 buttons, talking JSON lines over Bluetooth LE / USB.
// Protocol: docs/protocol.md
#include <Arduino.h>
#include <Wire.h>
#include <U8g2lib.h>
#include <ArduinoJson.h>

#include "config.h"
#include "Link.h"
#include "Buttons.h"
#include "Face.h"
#include "Apps.h"
#include "Displays.h"
#include "Lcd.h"
#include "LcdUi.h"
#include "Tft.h"
#include "Gfx.h"

// ---- screens ---------------------------------------------------------------------
// Screen 0 is the face, screens 1..n are the PC-defined apps.
static int screen = 0;
static int slideFrom = 0, slideTo = 0, slideDir = 0;   // dir: +1 = new screen enters from the right
static uint32_t slideStart = 0;
static bool sliding = false;

static int screenCount() { return 1 + Apps::count(); }

// ---- toast (speech bubble) ---------------------------------------------------------
static struct {
  bool active;
  char title[NAME_LEN];
  char text[QUIP_LEN];
  uint32_t start;
} toast;

// ---- takeover alerts ---------------------------------------------------------------
struct Alert {
  bool used;
  char id[ID_LEN];
  char title[QUIP_LEN];
  char txt[QUIP_LEN];
  uint8_t lvl;
  uint32_t since;
};
static Alert alerts[MAX_ALERTS];

static void copyStr(char* dst, size_t n, const char* src) {
  if (!src) src = "";
  strncpy(dst, src, n - 1);
  dst[n - 1] = 0;
}

// Highest level wins; ties go to the newest.
static Alert* currentAlert() {
  Alert* best = nullptr;
  for (auto& a : alerts) {
    if (!a.used) continue;
    if (!best || a.lvl > best->lvl || (a.lvl == best->lvl && (int32_t)(a.since - best->since) > 0)) best = &a;
  }
  return best;
}

static void showToast(const char* title, const char* text) {
  copyStr(toast.title, sizeof(toast.title), title);
  copyStr(toast.text, sizeof(toast.text), text);
  toast.start = millis();
  toast.active = true;
}

// ---- outgoing messages ----------------------------------------------------------
static void fillHello(JsonDocument& d) {
  d["t"] = "hello";
  d["fw"] = FW_VERSION;
  d["name"] = DEVICE_NAME;
  d["display"] = Displays::kindName();      // oled / lcd1602 / lcd2004 / tft
  d["oled"] = Displays::oledDetected();
  d["lcd"] = Displays::lcdDetected();       // backpack address found at boot, 0 = none
}

static void sendHello() {
  JsonDocument d;
  fillHello(d);
  Link::send(d);
}

static void sendSimple(const char* type, const char* id) {
  JsonDocument d;
  d["t"] = type;
  if (id) d["id"] = id;
  Link::send(d);
}

static const char* screenId(int s) {
  if (s == 0) return "face";
  App* a = Apps::get(s - 1);
  return a ? a->id : "face";
}

static void sendButton(ButtonId b, bool longPress, int scr) {
  JsonDocument d;
  d["t"] = "btn";
  d["b"] = b == BTN_SELECT ? "sel" : b == BTN_LEFT ? "left" : b == BTN_RIGHT ? "right" : "magic";
  d["long"] = longPress;
  d["scr"] = screenId(scr);
  Link::send(d);
}

// ---- incoming messages ----------------------------------------------------------
static void onMessage(JsonDocument& m) {
  const char* t = m["t"];
  if (!strcmp(t, "hello")) {
    sendHello();
  } else if (!strcmp(t, "diag")) {             // "Check wiring" in the PC app
    JsonDocument d;
    char buf[320];
    d["t"] = "diag";
    Displays::describe(buf, sizeof(buf));
    d["boot"] = buf;                            // ArduinoJson copies char arrays
    Displays::scanNow(buf, sizeof(buf));
    d["scan"] = buf;
    Link::send(d);
  } else if (!strcmp(t, "ping")) {
    sendSimple("pong", nullptr);
  } else if (!strcmp(t, "apps")) {
    Apps::setList(m["apps"].as<JsonArrayConst>());
    if (screen >= screenCount()) screen = 0;
    sliding = false;
  } else if (!strcmp(t, "val")) {
    App* a = Apps::applyValue(m);
    const char* quip = m["quip"] | "";
    if (a && quip[0]) showToast(a->name, quip);
  } else if (!strcmp(t, "mood")) {
    Face::setMood(Face::moodFromString(m["m"] | "happy"));
    Face::setPending(m["n"] | 0);
  } else if (!strcmp(t, "alert")) {
    const char* id = m["id"] | "";
    Alert* slot = nullptr;
    for (auto& a : alerts) if (a.used && !strcmp(a.id, id)) { slot = &a; break; }
    if (!slot) for (auto& a : alerts) if (!a.used) { slot = &a; break; }
    if (!slot) {   // full: evict the oldest
      slot = &alerts[0];
      for (auto& a : alerts) if ((int32_t)(a.since - slot->since) < 0) slot = &a;
    }
    bool isNew = !slot->used || strcmp(slot->id, id);
    copyStr(slot->id, sizeof(slot->id), id);
    copyStr(slot->title, sizeof(slot->title), m["title"] | "Hey!");
    copyStr(slot->txt, sizeof(slot->txt), m["txt"] | "");
    slot->lvl = m["lvl"] | 1;
    if (isNew) slot->since = millis();
    slot->used = true;
  } else if (!strcmp(t, "clear")) {
    const char* id = m["id"] | "";
    for (auto& a : alerts) if (a.used && !strcmp(a.id, id)) a.used = false;
  } else if (!strcmp(t, "say")) {
    showToast("", m["txt"] | "");
  } else if (!strcmp(t, "cfg")) {
    if (Displays::applyConfig(m)) sliding = false;
  }
}

// "?" (or "diag") typed in a serial monitor: what DANTE found at boot, plus a live I2C scan.
static void onText(const char* line) {
  if (strcmp(line, "?") != 0 && strcasecmp(line, "diag") != 0) return;
  char buf[320];
  Displays::describe(buf, sizeof(buf));
  Link::sendText(buf);
  Displays::scanNow(buf, sizeof(buf));
  Link::sendText(buf);
}

// ---- input --------------------------------------------------------------------------
static void goTo(int target, int dir) {
  int n = screenCount();
  target = (target % n + n) % n;
  if (target == screen) return;
  slideFrom = screen;
  slideTo = target;
  slideDir = dir;
  slideStart = millis();
  sliding = true;
  screen = target;
}

static void handleButton(const ButtonEvent& ev) {
  // 0) MAGIC always goes to the PC, whatever is on screen; the PC does the configured thing
  //    and answers with a `say` ("Mic muted").
  if (ev.id == BTN_MAGIC) {
    if (!Link::pcAlive()) { showToast("", "No PC, no magic :("); return; }
    // On a countdown screen MAGIC is start / pause (the PC answers with a bubble); elsewhere, sparkles.
    App* on = screen > 0 ? Apps::get(screen - 1) : nullptr;
    if (!on || on->vis != VIS_TIMER) Face::playGag(GAG_MAGIC, 900);
    sendButton(ev.id, ev.longPress, screen);
    return;
  }
  // 1) A takeover owns the buttons: SELECT = "on it", LEFT/RIGHT = "later".
  if (Alert* a = currentAlert()) {
    if (ev.longPress && ev.id != BTN_SELECT) return;   // ignore long side presses
    sendSimple(ev.id == BTN_SELECT ? "ack" : "snooze", a->id);
    a->used = false;
    if (ev.id == BTN_SELECT) Face::playGag(GAG_WINK, 1500);
    return;
  }
  // 2) Any button just dismisses a speech bubble.
  if (toast.active) { toast.active = false; return; }

  int pressedOn = screen;
  switch (ev.id) {
    case BTN_LEFT:
      if (ev.longPress) goTo(0, -1); else goTo(screen - 1, -1);
      break;
    case BTN_RIGHT:
      if (ev.longPress) goTo(0, +1); else goTo(screen + 1, +1);
      break;
    case BTN_SELECT:
      if (screen == 0 && !ev.longPress) Face::playRandomGag();
      break;
    default: break;
  }
  sendButton(ev.id, ev.longPress, pressedOn);
}

// ---- rendering ------------------------------------------------------------------------
// Everything draws on a logical 128x32 canvas (Gfx.h). The OLED shows it 1:1, the TFT scaled up
// and in colour, the character LCD has its own text layouts (LcdUi).

static void renderOn(Gfx& g, int s, uint32_t now, bool chrome = true) {
  if (s == 0) Face::draw(g, 0, chrome);
  else Apps::draw(g, s - 1, 0, s, screenCount(), now);
}

static float easeInOut(float p) { return p < 0.5f ? 2 * p * p : 1 - powf(-2 * p + 2, 2) / 2; }

// OLED slide transition: render both screens into separate buffers and splice columns.
// (SSD1306 buffer = 4 pages x 128 columns, one byte per column per page.)
static void renderSlide(U8G2& u, uint32_t now) {
  static uint8_t a[512], b[512];
  U8g2Gfx g(u);
  uint8_t* buf = u.getBufferPtr();
  u.clearBuffer(); renderOn(g, slideFrom, now); memcpy(a, buf, sizeof(a));
  u.clearBuffer(); renderOn(g, slideTo, now);   memcpy(b, buf, sizeof(b));

  float p = easeInOut((float)(now - slideStart) / SLIDE_MS);
  int s = (int)(p * 128);
  for (int page = 0; page < 4; page++) {
    uint8_t* out = buf + page * 128;
    const uint8_t* A = a + page * 128;
    const uint8_t* B = b + page * 128;
    for (int x = 0; x < 128; x++) {
      if (slideDir > 0) out[x] = (x < 128 - s) ? A[x + s] : B[x - (128 - s)];
      else              out[x] = (x >= s) ? A[x - s] : B[x + (128 - s)];
    }
  }
}

static void drawToast(Gfx& g, uint32_t now) {
  uint32_t t = now - toast.start;
  // Bubble pops in from the right.
  int slide = t < 160 ? (int)((160 - t) * 96 / 160) : 0;
  int bx = 30 + slide, by = 1, bw = 97, bh = 30;
  g.setDrawColor(0);
  g.drawBox(0, 0, 128, 32);
  g.setDrawColor(1);
  Face::drawMini(g, 14, 15 + (int)(sinf(t * 0.012f) * 2), 0.42f, t < 600 ? MINI_SURPRISED : MINI_HAPPY, now);
  g.drawRFrame(bx, by, bw, bh, 4);
  g.drawTriangle(bx, 13, bx, 19, bx - 4, 16);   // bubble tail
  g.setDrawColor(0); g.drawVLine(bx, 14, 5); g.setDrawColor(1);
  g.drawLine(bx, 13, bx - 4, 16); g.drawLine(bx - 4, 16, bx, 19);

  int textY = 20;
  if (toast.title[0]) {
    g.setFont(u8g2_font_4x6_tr);
    g.drawStr(bx + 4, by + 7, toast.title);
    textY = 24;
  }
  g.setFont(u8g2_font_6x10_tr);
  drawMarquee(g, toast.text, bx + 3, textY, bw - 6, now, toast.start + 300, by + 1, by + bh - 1);
}

// "Knock knock": shake every 3 s.
static int knockOffset(uint32_t t) {
  uint32_t ph = t % 3000;
  return ph < 450 ? (int)(sinf(ph * 0.09f) * 3) : 0;
}

static void urgentFlash(Gfx& g, Alert& a, uint32_t t) {
  if (a.lvl >= 2 && (t % 1000) < 140) {
    g.setDrawColor(2);
    g.drawBox(0, 0, 128, 32);
    g.setDrawColor(1);
  }
}

static void drawTakeover(Gfx& g, Alert& a, uint32_t now) {
  uint32_t t = now - a.since;
  int ox = knockOffset(t);

  int bounce = (int)(-fabsf(sinf(t * 0.008f)) * 3);
  MiniExpr e = a.lvl >= 2 ? ((t / 1500) % 2 ? MINI_WORRIED : MINI_SURPRISED) : ((t / 2000) % 2 ? MINI_LOOK : MINI_WORRIED);
  Face::drawMini(g, ox + 16, 17 + bounce, 0.5f, e, now);

  g.setFont(u8g2_font_6x10_tr);
  drawMarquee(g, a.title, ox + 36, 8, 92, now, a.since, 0, 9);
  g.setFont(u8g2_font_7x13B_tr);
  drawMarquee(g, a.txt[0] ? a.txt : "psst... over here!", ox + 36, 22, 92, now, a.since + 600, 10, 23);
  g.setFont(u8g2_font_4x6_tr);
  if ((t / 4000) % 2 == 1) g.drawStr(ox + 36, 31, "SEL:on it  </>:later");
  else g.drawStr(ox + 36, 31, a.lvl >= 2 ? "!! URGENT !!" : "need you :)");

  urgentFlash(g, a, t);
}

static void renderOled(Alert* alert, uint32_t now) {
  U8G2& u = Displays::oled();
  U8g2Gfx g(u);
  u.clearBuffer();
  u.setDrawColor(1);
  if (alert) {
    drawTakeover(g, *alert, now);
  } else {
    if (sliding) renderSlide(u, now);
    else renderOn(g, screen, now);
    if (toast.active) drawToast(g, now);
  }
  u.sendBuffer();
}

// Character LCD (16x2 / 20x4). Text-mode rendering, no slides.
static void renderLcd(Alert* alert, uint32_t now) {
  static uint32_t lastLcd = 0;
  if (now - lastLcd < LCD_FRAME_MS) return;
  lastLcd = now;

  bool light = Displays::settings().bright > 0;
  Lcd::backlight(light);
  Lcd::clear();
  if (alert) LcdUi::takeover(alert->title, alert->txt, alert->lvl, alert->since, now, light);
  else if (toast.active) LcdUi::toast(toast.title, toast.text, toast.start, now);
  else if (screen == 0) LcdUi::face(now);
  else if (App* a = Apps::get(screen - 1)) LcdUi::app(*a, screen, Apps::count(), now);   // "app 2 of 5"
  Lcd::flush();
}

// ---- 3.5" TFT ---------------------------------------------------------------------------
// A status bar on top; below it the face fills the screen (the eyes stretch tall), while app
// screens, bubbles and nudges are the OLED layouts scaled up 3.75x in a band across the middle.
static const int TFT_BAR = 40;                         // status bar height
static const float TFT_SX = TFT_W / 128.0f;            // 3.75
static const float TFT_BAND_Y = TFT_BAR + (TFT_H - TFT_BAR - 32 * TFT_SX) / 2;

// The accent colour chosen in the PC app (teal unless changed): title, happy eyes, page dot, highlights.
static uint32_t accent() { return Displays::settings().accent; }

static uint32_t moodColor(const FaceInfo& f) {
  if (f.gag == GAG_LOVE) return Rgb::Pink;
  switch (f.mood) {
    case MOOD_CURIOUS:  return Rgb::Sky;
    case MOOD_ANTSY:    return Rgb::Yellow;
    case MOOD_GRUMPY:   return Rgb::Orange;
    case MOOD_MELTDOWN: return Rgb::Red;
    case MOOD_SLEEPY:   return Rgb::Slate;
    default:            return accent();
  }
}

static uint32_t levelColor(uint8_t lvl) { return lvl >= 2 ? Rgb::Red : lvl == 1 ? Rgb::Amber : Rgb::Light; }

static const char* moodName(Mood m) {
  switch (m) {
    case MOOD_CURIOUS:  return "curious";
    case MOOD_ANTSY:    return "antsy";
    case MOOD_GRUMPY:   return "grumpy";
    case MOOD_MELTDOWN: return "meltdown!";
    case MOOD_SLEEPY:   return "sleepy";
    default:            return "happy";
  }
}

// Name, mood, waiting count and link on top; page dots for the screens.
static void tftStatusBar(TftGfx& g, uint32_t now) {
  LGFX_Sprite& s = g.sprite();
  const int y = -g.top();
  if (y + TFT_BAR < 0) return;   // this strip is below the bar
  FaceInfo f = Face::info();
  s.setTextSize(1);
  s.setTextDatum(lgfx::textdatum_t::middle_left);
  s.setFont(&fonts::FreeSansBold12pt7b);
  s.setTextColor(accent());
  s.drawString("DANTE", 12, y + TFT_BAR / 2);
  s.setFont(&fonts::FreeSans9pt7b);
  s.setTextColor(moodColor(f));
  s.drawString(moodName(f.mood), 112, y + TFT_BAR / 2 + 1);

  // Right: link state, then the "waiting on you" badge.
  s.setTextDatum(lgfx::textdatum_t::middle_right);
  int right = TFT_W - 12;
  if (f.alone) {
    if ((now / 700) % 2 == 0) { s.setTextColor(Rgb::Red); s.drawString("no PC", right, y + TFT_BAR / 2 + 1); }
  } else {
    s.setTextColor(Rgb::Slate);
    s.drawString(Link::btConnected() ? "Bluetooth" : "USB", right, y + TFT_BAR / 2 + 1);
    if (f.pending > 0) {
      char b[16];
      snprintf(b, sizeof(b), "%d waiting", f.pending > 99 ? 99 : f.pending);
      int w = s.textWidth(b) + 16;
      int x = right - s.textWidth(Link::btConnected() ? "Bluetooth" : "USB") - 14 - w;
      s.fillRoundRect(x, y + 8, w, TFT_BAR - 16, 10, Rgb::Amber);
      s.setTextColor(Rgb::Black);
      s.setTextDatum(lgfx::textdatum_t::middle_center);
      s.drawString(b, x + w / 2, y + TFT_BAR / 2 + 1);
    }
  }
  // Page dots, centred: the face plus one per app.
  int n = screenCount();
  int x0 = TFT_W / 2 - (n - 1) * 8;
  for (int i = 0; i < n; i++) {
    if (i == screen) s.fillCircle(x0 + i * 16, y + TFT_BAR / 2, 5, accent());
    else s.drawCircle(x0 + i * 16, y + TFT_BAR / 2, 4, Rgb::Slate);
  }
  s.drawFastHLine(0, y + TFT_BAR - 1, TFT_W, Rgb::Dim);
}

static void renderTft(Alert* alert, uint32_t now) {
  static uint32_t last = 0;
  if (now - last < TFT_FRAME_MS) return;
  last = now;
  Tft::frame([&](TftGfx& g) {
    tftStatusBar(g, now);
    if (alert) {
      g.setTransform(TFT_SX, TFT_SX, 0, TFT_BAND_Y);
      g.setColors(alert->lvl >= 2 ? Rgb::Red : Rgb::Amber, Rgb::Black, accent());
      drawTakeover(g, *alert, now);
    } else if (toast.active) {
      g.setTransform(TFT_SX, TFT_SX, 0, TFT_BAND_Y);
      g.setColors(Rgb::White, Rgb::Black, accent());
      drawToast(g, now);
    } else if (screen == 0) {
      // The face fills everything under the status bar: tall robot eyes.
      g.setTransform(TFT_SX, (TFT_H - TFT_BAR - 8) / 32.0f, 0, TFT_BAR + 4);
      g.setColors(moodColor(Face::info()), Rgb::Black, accent());
      Face::draw(g, 0, false);
    } else if (App* a = Apps::get(screen - 1)) {
      g.setTransform(TFT_SX, TFT_SX, 0, TFT_BAND_Y);
      g.setColors(levelColor(a->lvl), Rgb::Black, accent());
      Apps::draw(g, screen - 1, 0, screen, screenCount(), now);
    }
  });
}

// ---- Arduino entry points -------------------------------------------------------------
void setup() {
  randomSeed(esp_random());
  Buttons::begin();
  Displays::begin();
  Face::begin();
  Link::begin(onMessage);
  Link::onText(onText);

  // A plain-text line for `pio device monitor` (the PC app ignores non-JSON lines).
  char diag[320];
  Displays::describe(diag, sizeof(diag));
  Serial.println(diag);

  // Say hi on both links; whoever is listening will answer with `hello`.
  JsonDocument d;
  fillHello(d);
  serializeJson(d, Serial); Serial.write('\n');
}

void loop() {
  static uint32_t lastFrame = 0;
  Link::poll();

  ButtonEvent ev;
  while (Buttons::poll(ev)) handleButton(ev);

  uint32_t now = millis();
  if (now - lastFrame < FRAME_MS) { delay(1); return; }
  lastFrame = now;

  Face::setAlone(!Link::pcAlive());
  Face::update(now);
  Apps::update(now);

  if (toast.active && now - toast.start > TOAST_MS) { toast.active = false; Face::react(); }   // double-take afterwards
  if (sliding && now - slideStart >= SLIDE_MS) sliding = false;
  Alert* alert = currentAlert();

  switch (Displays::active()) {
    case Displays::KIND_LCD: renderLcd(alert, now); break;
    case Displays::KIND_TFT: renderTft(alert, now); break;
    default:                 renderOled(alert, now); break;
  }
}
