#include "Touch.h"
#include "Tft.h"
#include "config.h"

namespace {
// The GT911 sometimes reports "no finger" for a single reading in the middle of a touch; only this
// long without a finger counts as letting go.
const uint32_t RELEASE_MS = 40;
// How often the panel is read. The GT911 updates about every 10 ms; reading it on every loop pass
// would only keep the I2C bus busy.
const uint32_t READ_EVERY_MS = 12;

Touch::Gestures gestures;
uint32_t lastRead = 0;
bool seenTouch = false;          // for describe()
int lastX = 0, lastY = 0;
uint32_t lastAt = 0;

#ifdef DANTE_SIMULATOR
bool simOn = false;
uint8_t simFingers = 0;
int simX = 0, simY = 0;
#endif

// What the panel reports right now: number of fingers, and where the first one is.
uint8_t readPanel(int& x, int& y) {
#ifdef DANTE_SIMULATOR
  x = simX; y = simY;
  return simOn ? simFingers : 0;
#elif HAS_TOUCH
  return Tft::readTouch(x, y);
#else
  (void)x; (void)y;
  return 0;
#endif
}
} // namespace

namespace Touch {

// A tap (or hold) anywhere is SELECT; with two fingers it's MAGIC. Moving between screens is swiping.
ButtonId Gestures::zone() const {
  return mostFingers >= 2 ? BTN_MAGIC : BTN_SELECT;
}

bool Gestures::feed(uint32_t now, uint8_t fingers, int x, int y, ButtonEvent& ev) {
  if (fingers == 0) {
    if (!down) return false;
    if (!zeroSince) { zeroSince = now ? now : 1; return false; }
    if (now - zeroSince < RELEASE_MS) return false;
    // Let go: a tap, unless this touch already was a long press or a swipe.
    down = false;
    zeroSince = 0;
    if (done) return false;
    ev = { zone(), false, (int16_t)startX, (int16_t)startY };
    return true;
  }

  zeroSince = 0;
  if (!down) {   // a new touch
    down = true;
    done = false;
    downAt = now;
    startX = x;
    startY = y;
    mostFingers = fingers;
    return false;
  }
  if (fingers > mostFingers) mostFingers = fingers;   // the second finger often lands a moment later
  if (done) return false;

  // A clear move with one finger is a swipe, acted on straight away, like a phone:
  //   finger moving left  = the next screen slides in from the right = RIGHT
  //   finger moving right = the previous screen = LEFT
  //   finger moving down  = home to the face (a long LEFT, as on the buttons)
  //   finger moving up    = nothing (yet)
  int dx = x - startX, dy = y - startY;
  if (mostFingers == 1 && (abs(dx) >= TOUCH_SWIPE_PX || abs(dy) >= TOUCH_SWIPE_PX)) {
    done = true;
    if (abs(dx) > abs(dy)) { ev = { dx < 0 ? BTN_RIGHT : BTN_LEFT, false }; return true; }
    if (dy > 0) { ev = { BTN_LEFT, true }; return true; }
    return false;
  }
  // Held still long enough: a long press, fired while the finger is still down (like the buttons).
  bool still = abs(dx) < TOUCH_SWIPE_PX && abs(dy) < TOUCH_SWIPE_PX;
  if (still && now - downAt >= BTN_LONG_MS) {
    done = true;
    ev = { zone(), true, (int16_t)startX, (int16_t)startY };
    return true;
  }
  return false;
}

void begin() {
  // The panel itself is set up with the screen (Tft::begin); nothing else to do.
}

bool available() {
#ifdef DANTE_SIMULATOR
  return simOn;
#else
  return HAS_TOUCH;
#endif
}

bool poll(ButtonEvent& ev) {
  if (!available()) return false;
  uint32_t now = millis();
  if (now - lastRead < READ_EVERY_MS) return false;
  lastRead = now;

  int x = 0, y = 0;
  uint8_t fingers = readPanel(x, y);
  if (fingers) { seenTouch = true; lastX = x; lastY = y; lastAt = now; }
  return gestures.feed(now, fingers, x, y, ev);
}

void describe(char* out, size_t n) {
  if (!available()) { snprintf(out, n, "touch: none"); return; }
  if (!seenTouch) { snprintf(out, n, "touch: not touched yet"); return; }
  snprintf(out, n, "touch: last at x=%d y=%d (screen is 480x320), %lus ago",
           lastX, lastY, (unsigned long)((millis() - lastAt) / 1000));
}

#ifdef DANTE_SIMULATOR
void simEnable(bool on) { simOn = on; }
void simSet(uint8_t fingers, int x, int y) {
  simFingers = fingers;
  simX = constrain(x, 0, TFT_W - 1);
  simY = constrain(y, 0, TFT_H - 1);
}
#endif

} // namespace Touch
