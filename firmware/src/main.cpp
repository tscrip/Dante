// DeskBuddy firmware: a tiny desk companion with opinions.
// ESP32 + SSD1306 128x32 (I2C) + 3 buttons, talking JSON lines over Bluetooth SPP / USB.
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

U8G2_SSD1306_128X32_UNIVISION_F_HW_I2C u8g2(U8G2_R0, U8X8_PIN_NONE, PIN_SCL, PIN_SDA);

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
static void sendHello() {
  JsonDocument d;
  d["t"] = "hello";
  d["fw"] = FW_VERSION;
  d["name"] = DEVICE_NAME;
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
  d["b"] = b == BTN_SELECT ? "sel" : (b == BTN_LEFT ? "left" : "right");
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
    if (m["bright"].is<int>()) u8g2.setContrast((uint8_t)constrain(m["bright"].as<int>(), 0, 255));
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
static void renderScreen(int s, int ox, uint32_t now) {
  if (s == 0) Face::draw(u8g2, ox);
  else Apps::draw(u8g2, s - 1, ox, s, screenCount(), now);
}

static float easeInOut(float p) { return p < 0.5f ? 2 * p * p : 1 - powf(-2 * p + 2, 2) / 2; }

// Slide transition: render both screens into separate buffers and splice columns.
// (SSD1306 buffer = 4 pages x 128 columns, one byte per column per page.)
static void renderSlide(uint32_t now) {
  static uint8_t a[512], b[512];
  uint8_t* buf = u8g2.getBufferPtr();
  u8g2.clearBuffer(); renderScreen(slideFrom, 0, now); memcpy(a, buf, sizeof(a));
  u8g2.clearBuffer(); renderScreen(slideTo, 0, now);   memcpy(b, buf, sizeof(b));

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

static void drawToast(uint32_t now) {
  uint32_t t = now - toast.start;
  if (t > TOAST_MS) { toast.active = false; Face::react(); return; }   // double-take afterwards
  // Bubble pops in from the right.
  int slide = t < 160 ? (int)((160 - t) * 96 / 160) : 0;
  int bx = 30 + slide, by = 1, bw = 97, bh = 30;
  u8g2.setDrawColor(0);
  u8g2.drawBox(0, 0, 128, 32);
  u8g2.setDrawColor(1);
  Face::drawMini(u8g2, 14, 15 + (int)(sinf(t * 0.012f) * 2), 0.42f, t < 600 ? MINI_SURPRISED : MINI_HAPPY, now);
  u8g2.drawRFrame(bx, by, bw, bh, 4);
  u8g2.drawTriangle(bx, 13, bx, 19, bx - 4, 16);   // bubble tail
  u8g2.setDrawColor(0); u8g2.drawVLine(bx, 14, 5); u8g2.setDrawColor(1);
  u8g2.drawLine(bx, 13, bx - 4, 16); u8g2.drawLine(bx - 4, 16, bx, 19);

  int textY = 20;
  if (toast.title[0]) {
    u8g2.setFont(u8g2_font_4x6_tr);
    u8g2.drawStr(bx + 4, by + 7, toast.title);
    textY = 24;
  }
  u8g2.setFont(u8g2_font_6x10_tr);
  drawMarquee(u8g2, toast.text, bx + 3, textY, bw - 6, now, toast.start + 300, by + 1, by + bh - 1);
}

static void drawTakeover(Alert& a, uint32_t now) {
  uint32_t t = now - a.since;
  // "Knock knock": shake every 3 s.
  uint32_t ph = t % 3000;
  int ox = ph < 450 ? (int)(sinf(ph * 0.09f) * 3) : 0;

  int bounce = (int)(-fabsf(sinf(t * 0.008f)) * 3);
  MiniExpr e = a.lvl >= 2 ? ((t / 1500) % 2 ? MINI_WORRIED : MINI_SURPRISED) : ((t / 2000) % 2 ? MINI_LOOK : MINI_WORRIED);
  Face::drawMini(u8g2, ox + 16, 17 + bounce, 0.5f, e, now);

  u8g2.setFont(u8g2_font_6x10_tr);
  drawMarquee(u8g2, a.title, ox + 36, 8, 92, now, a.since, 0, 9);
  u8g2.setFont(u8g2_font_7x13B_tr);
  drawMarquee(u8g2, a.txt[0] ? a.txt : "psst... over here!", ox + 36, 22, 92, now, a.since + 600, 10, 23);
  u8g2.setFont(u8g2_font_4x6_tr);
  if ((t / 4000) % 2 == 1) u8g2.drawStr(ox + 36, 31, "SEL:on it  </>:later");
  else u8g2.drawStr(ox + 36, 31, a.lvl >= 2 ? "!! URGENT !!" : "need you :)");

  // Urgent alerts flash the whole screen.
  if (a.lvl >= 2 && (t % 1000) < 140) {
    u8g2.setDrawColor(2);
    u8g2.drawBox(0, 0, 128, 32);
    u8g2.setDrawColor(1);
  }
}

// ---- Arduino entry points -------------------------------------------------------------
void setup() {
  randomSeed(esp_random());
  Buttons::begin();
  u8g2.setBusClock(400000);
  u8g2.begin();
  u8g2.setContrast(200);
  Face::begin();
  Link::begin(onMessage);

  // Say hi on both links; whoever is listening will answer with `hello`.
  JsonDocument d;
  d["t"] = "hello"; d["fw"] = FW_VERSION; d["name"] = DEVICE_NAME;
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

  u8g2.clearBuffer();
  u8g2.setDrawColor(1);
  if (Alert* a = currentAlert()) {
    drawTakeover(*a, now);
  } else {
    if (sliding && now - slideStart >= SLIDE_MS) sliding = false;
    if (sliding) renderSlide(now);
    else renderScreen(screen, 0, now);
    if (toast.active) drawToast(now);
  }
  u8g2.sendBuffer();
}
