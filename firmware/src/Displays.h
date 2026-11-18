#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>
#include <U8g2lib.h>

// DANTE's one screen: the 0.91" SSD1306 128x32 OLED (I2C) or the 3.5" 480x320 ST7796 colour TFT (SPI).
// Settings come from the PC (`cfg` message) and are stored in flash.
namespace Displays {
  // KIND_AUTO = the OLED if it answers on I2C, otherwise the TFT (an SPI screen can't be detected).
  enum Kind : uint8_t { KIND_OLED = 0, KIND_TFT = 2, KIND_AUTO = 255 };   // 1 was the character LCD (removed)

  struct Settings {
    Kind kind = KIND_AUTO;      // what's configured; see active() for what's in use
    bool flip = false;          // mounted upside down: rotate 180 degrees
    uint8_t bright = 200;       // OLED contrast / TFT backlight
    uint32_t accent = 0x5EEAD4; // TFT accent colour, 0xRRGGBB (title, happy eyes, highlights)
    uint8_t look = 0;           // Face::Look: DANTE's style and accessories
  };

  void begin();
  const Settings& settings();
  bool oledDetected();          // did the OLED answer at boot?
  Kind active();                // the screen in use (KIND_AUTO resolved)
  bool isTft();
  const char* kindName();       // "oled" / "tft" (in use)
  U8G2& oled();
  // One human-readable line about what DANTE found and uses, for the USB log.
  void describe(char* out, size_t n);
  // Probes every I2C address right now: "I2C scan now: bus 1 (SDA=8 SCL=9): 0x3C (OLED)".
  void scanNow(char* out, size_t n);

  // Applies the display-related fields of a `cfg` message. Returns true if anything changed.
  bool applyConfig(JsonDocument& msg);
}
