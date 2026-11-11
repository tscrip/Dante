#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>
#include <U8g2lib.h>

// One or two SSD1306 128x32 panels.
//  * 1 screen : the classic face with both eyes on one panel.
//  * 2 screens: one big eye per panel. App screens then use a layout:
//      WATCH  - one panel shows the app, the other keeps an eye on it (literally);
//      MIRROR - both panels show the app.
// Settings come from the PC (`cfg` message) and are stored in flash.
namespace Displays {
  enum Layout : uint8_t { LAYOUT_WATCH = 0, LAYOUT_MIRROR = 1 };

  struct Settings {
    uint8_t screens = 1;        // 1 or 2
    Layout layout = LAYOUT_WATCH;
    uint8_t contentSide = 1;    // WATCH layout: 0 = app on the left panel, 1 = right panel
    bool swap = false;          // wired the wrong way round? swap which panel is left
    bool flip[2] = { false, false };   // rotate a physical panel 180 degrees
    uint8_t bright = 200;
  };

  void begin();
  const Settings& settings();
  bool secondDetected();        // did a panel answer at the second display's address?

  // Applies the display-related fields of a `cfg` message. Returns true if anything changed.
  bool applyConfig(JsonDocument& msg);

  int count();                  // panels in use (1 or 2)
  // Panel for a logical side: 0 = left, 1 = right. With one screen, side 0 is the only panel.
  U8G2& side(int s);
}
