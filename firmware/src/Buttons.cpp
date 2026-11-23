#include "Buttons.h"
#include "config.h"

// Each button is wired between its pin and GND. A pin of -1 means "not on this board": the yellow
// board has no buttons of its own (on the classic ESP32, the S3's GPIO 6 and 7 belong to the flash
// chip, so they must never be touched there), but can have an extra MAGIC button on IO22.
namespace {
  // The extra MAGIC button (yellow board): its pin has a built-in pull-up, so without a button it simply
  // reads "released" and can always be read.
  const int8_t extraMagicPin = PIN_EXTRA_MAGIC;
  uint32_t extraPresses = 0;   // presses of the extra MAGIC button since start, for the status line

  int8_t pinFor(uint8_t i) {
    static const int8_t pins[BTN_COUNT] = { PIN_BTN_LEFT, PIN_BTN_RIGHT, PIN_BTN_SELECT, PIN_BTN_MAGIC };
    if (i == BTN_MAGIC && pins[i] < 0) return extraMagicPin;
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

  void describeExtra(char* out, size_t n) {
    if (extraMagicPin < 0) { snprintf(out, n, "MAGIC button: none on this board"); return; }
    bool low = digitalRead(extraMagicPin) == LOW;
    snprintf(out, n, "MAGIC button (IO%d): pin reads %s right now, %lu presses seen", extraMagicPin,
             low ? "LOW (pressed)" : "HIGH (released)", (unsigned long)extraPresses);
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
          if (pin == extraMagicPin) extraPresses++;
          ev = { (ButtonId)i, false };
          return true;
        }
      }
      if (s.stable && !s.longFired && now - s.pressedAt >= BTN_LONG_MS) {
        s.longFired = true;
        if (pin == extraMagicPin) extraPresses++;
        ev = { (ButtonId)i, true };
        return true;
      }
    }
    return false;
  }
}
