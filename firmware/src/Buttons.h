#pragma once
#include <Arduino.h>

enum ButtonId : uint8_t { BTN_LEFT = 0, BTN_RIGHT = 1, BTN_SELECT = 2, BTN_MAGIC = 3, BTN_COUNT = 4 };

struct ButtonEvent {
  ButtonId id;
  bool longPress;
  int16_t x = -1, y = -1;   // touch screens: where the tap started (screen pixels); -1 for real buttons
};

// Debounced buttons with short/long press detection.
// Short press fires on release; long press fires once while still held.
namespace Buttons {
  void begin();
  // Returns true and fills `ev` if a press event is ready.
  bool poll(ButtonEvent& ev);
}
