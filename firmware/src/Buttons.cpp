#include "Buttons.h"
#include "config.h"

// Each button is wired between its pin and GND. A pin of -1 means "not on this board": the yellow
// board has no buttons of its own (on the classic ESP32, the S3's GPIO 6 and 7 belong to the flash
// chip, so they must never be touched there), but can have an extra MAGIC button on P3 (IO35).
namespace {
  // The extra MAGIC button counts only when switched on in the PC app: its pin has no built-in pull-up,
  // so without the resistor that goes with the button it floats and would make up presses.
  const int8_t extraMagicPin = PIN_EXTRA_MAGIC;
  bool extraMagicOn = false;

  int8_t pinFor(uint8_t i) {
    static const int8_t pins[BTN_COUNT] = { PIN_BTN_LEFT, PIN_BTN_RIGHT, PIN_BTN_SELECT, PIN_BTN_MAGIC };
    if (i == BTN_MAGIC && pins[i] < 0 && extraMagicOn) return extraMagicPin;
    return pins[i];
  }

  struct State {
    bool raw = false, stable = false;
    uint32_t changedAt = 0, pressedAt = 0;
    bool longFired = false;
  };
  State st[BTN_COUNT];

  void setUp(int8_t pin) {
    if (pin < 0) return;
    // GPIO 34-39 on the classic ESP32 can only read and have no pull-up: the button brings its own resistor.
    pinMode(pin, pin >= 34 ? INPUT : INPUT_PULLUP);
  }
}

namespace Buttons {
  void begin() {
    for (uint8_t i = 0; i < BTN_COUNT; i++) setUp(pinFor(i));
  }

  void setExtraMagic(bool on) {
    if (extraMagicPin < 0 || on == extraMagicOn) return;
    extraMagicOn = on;
    st[BTN_MAGIC] = State();   // start fresh: no half-seen press from before
    if (on) setUp(extraMagicPin);
  }

  bool poll(ButtonEvent& ev) {
    uint32_t now = millis();
    for (uint8_t i = 0; i < BTN_COUNT; i++) {
      int8_t pin = pinFor(i);
      if (pin < 0) continue;
      State& s = st[i];
      bool down = digitalRead(pin) == LOW;
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
