#include "Buttons.h"
#include "config.h"

namespace {
  const uint8_t pins[BTN_COUNT] = { PIN_BTN_LEFT, PIN_BTN_RIGHT, PIN_BTN_SELECT, PIN_BTN_MAGIC };

  struct State {
    bool raw = false, stable = false;
    uint32_t changedAt = 0, pressedAt = 0;
    bool longFired = false;
  };
  State st[BTN_COUNT];
}

namespace Buttons {
  void begin() {
    for (uint8_t i = 0; i < BTN_COUNT; i++) pinMode(pins[i], INPUT_PULLUP);
  }

  bool poll(ButtonEvent& ev) {
    uint32_t now = millis();
    for (uint8_t i = 0; i < BTN_COUNT; i++) {
      State& s = st[i];
      bool down = digitalRead(pins[i]) == LOW;
      if (down != s.raw) { s.raw = down; s.changedAt = now; }
      if (s.raw != s.stable && now - s.changedAt >= BTN_DEBOUNCE_MS) {
        s.stable = s.raw;
        if (s.stable) {
          s.pressedAt = now;
          s.longFired = false;
        } else if (!s.longFired) {
          ev = { (ButtonId)i, false };
          return true;
        }
      }
      if (s.stable && !s.longFired && now - s.pressedAt >= BTN_LONG_MS) {
        s.longFired = true;
        ev = { (ButtonId)i, true };
        return true;
      }
    }
    return false;
  }
}
