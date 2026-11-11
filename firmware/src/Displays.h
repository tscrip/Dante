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
// The display type can also be a 1602 character LCD (see Lcd.h / LcdUi.h); that is always a
// single screen.
namespace Displays {
  enum Layout : uint8_t { LAYOUT_WATCH = 0, LAYOUT_MIRROR = 1 };
  // KIND_AUTO = use whichever screen answers on the I2C bus (OLED preferred).
  enum Kind : uint8_t { KIND_OLED = 0, KIND_LCD1602 = 1, KIND_AUTO = 255 };

  struct Settings {
    Kind kind = KIND_AUTO;      // what's configured; see isLcd()/kindName() for what's in use
    uint8_t lcdAddr = 0x27;     // PCF8574 backpack address
    uint8_t screens = 1;        // 1 or 2 (OLED only)
    Layout layout = LAYOUT_WATCH;
    uint8_t contentSide = 1;    // WATCH layout: 0 = app on the left panel, 1 = right panel
    bool swap = false;          // wired the wrong way round? swap which panel is left
    bool flip[2] = { false, false };   // rotate a physical panel 180 degrees
    uint8_t bright = 200;
  };

  void begin();
  const Settings& settings();
  bool secondDetected();        // did a panel answer at the second display's address?
  bool oledDetected();          // did the first OLED answer at boot?
  uint8_t lcdDetected();        // address an LCD backpack answered at during boot, or 0
  bool isLcd();                 // the screen in use (after resolving KIND_AUTO)
  const char* kindName();       // "oled" / "lcd1602" (in use)
  // One human-readable line about the I2C bus and the chosen screen, for the USB log.
  void describe(char* out, size_t n);

  // Applies the display-related fields of a `cfg` message. Returns true if anything changed.
  bool applyConfig(JsonDocument& msg);

  int count();                  // OLED panels in use (1 or 2); 1 for the LCD
  // Panel for a logical side: 0 = left, 1 = right. With one screen, side 0 is the only panel.
  U8G2& side(int s);
}
