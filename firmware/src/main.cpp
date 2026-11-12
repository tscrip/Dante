// DANTE firmware: a tiny desk companion with opinions.
// ESP32-S3 + one or two SSD1306 128x32 OLEDs or a 1602/2004 LCD + 4 buttons, talking JSON lines over Bluetooth LE / USB.
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
  d["display"] = Displays::kindName();
  d["screens"] = Displays::count();
  d["disp2"] = Displays::secondDetected();
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
    Face::playGag(GAG_MAGIC, 900);
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
// Everything below draws into a given panel. Which panel shows what:
//
//   1 screen            : face (both eyes) or the app, as always.
//   2 screens, face     : left panel = left eye, right panel = right eye.
//   2 screens, app      : WATCH  - the app on the content side, the other eye watches it;
//                         MIRROR - the app on both panels.
static bool dual() { return Displays::count() == 2; }
static int contentSide() { return Displays::settings().contentSide; }
static bool mirror() { return Displays::settings().layout == Displays::LAYOUT_MIRROR; }

// Does this side show "content" (apps, bubbles, takeover text) rather than a lone eye?
static bool showsContent(int side) { return !dual() || mirror() || side == contentSide(); }

// The watching eye glances towards the other panel.
static float lookTowardContent(int side) { return side < contentSide() ? 26.0f : -26.0f; }

static void renderOn(U8G2& g, int side, int s, uint32_t now) {
  if (!dual()) {
    if (s == 0) Face::draw(g, 0);
    else Apps::draw(g, s - 1, 0, s, screenCount(), now);
    return;
  }
  if (s == 0) { Face::drawSingle(g, side, 0, 0, 0); return; }
  if (showsContent(side)) Apps::draw(g, s - 1, 0, s, screenCount(), now);
  else Face::drawSingle(g, side, 0, lookTowardContent(side), 0);
}

static float easeInOut(float p) { return p < 0.5f ? 2 * p * p : 1 - powf(-2 * p + 2, 2) / 2; }

// Slide transition: render both screens into separate buffers and splice columns.
// (SSD1306 buffer = 4 pages x 128 columns, one byte per column per page.)
static void renderSlide(U8G2& g, int side, uint32_t now) {
  static uint8_t a[512], b[512];
  uint8_t* buf = g.getBufferPtr();
  g.clearBuffer(); renderOn(g, side, slideFrom, now); memcpy(a, buf, sizeof(a));
  g.clearBuffer(); renderOn(g, side, slideTo, now);   memcpy(b, buf, sizeof(b));

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

static void drawToast(U8G2& g, uint32_t now) {
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

static void urgentFlash(U8G2& g, Alert& a, uint32_t t) {
  if (a.lvl >= 2 && (t % 1000) < 140) {
    g.setDrawColor(2);
    g.drawBox(0, 0, 128, 32);
    g.setDrawColor(1);
  }
}

static void drawTakeover(U8G2& g, Alert& a, uint32_t now) {
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

// Dual-screen WATCH layout: the eye panel bounces and stares at the message, knocking in sync.
static void drawTakeoverEye(U8G2& g, int side, Alert& a, uint32_t now) {
  uint32_t t = now - a.since;
  int bounce = (int)(-fabsf(sinf(t * 0.008f)) * 3);
  // Every few seconds it turns to look at the human: "...well?"
  float look = ((t / 2500) % 3 == 2) ? 0 : lookTowardContent(side);
  Face::drawSingle(g, side, knockOffset(t), look, bounce);
  g.setFont(u8g2_font_7x13B_tr);
  if ((t / 400) % 2 == 0) g.drawStr(side == 0 ? 4 : 112, 20, a.lvl >= 2 ? "!!" : "!");
  urgentFlash(g, a, t);
}

// Character LCD (16x2 / 20x4): one screen, text-mode rendering, no slide transitions.
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

// ---- Arduino entry points -------------------------------------------------------------
void setup() {
  randomSeed(esp_random());
  Buttons::begin();
  Displays::begin();
  Face::begin();
  Link::begin(onMessage);

  // A plain-text line for `pio device monitor` (the PC app ignores non-JSON lines).
  char diag[256];
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

  if (Displays::isLcd()) { renderLcd(alert, now); return; }

  for (int side = 0; side < Displays::count(); side++) {
    U8G2& g = Displays::side(side);
    g.clearBuffer();
    g.setDrawColor(1);
    if (alert) {
      if (showsContent(side)) drawTakeover(g, *alert, now);
      else drawTakeoverEye(g, side, *alert, now);
    } else {
      // A lone watching eye doesn't slide: it just turns to look at the new screen.
      if (sliding && showsContent(side)) renderSlide(g, side, now);
      else renderOn(g, side, screen, now);
      if (toast.active && showsContent(side)) drawToast(g, now);
    }
    g.sendBuffer();
  }
}
