#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>
#include <U8g2lib.h>

// DANTE's one screen: a 0.91" SSD1306 128x32 OLED, a 16x2 / 20x4 character LCD (PCF8574 backpack),
// or the 3.5" 480x320 ST7796 colour TFT. The OLED and LCD share the I2C bus; the TFT is on SPI.
// Settings come from the PC (`cfg` message) and are stored in flash.
namespace Displays {
  // KIND_AUTO = whichever I2C screen answers (OLED preferred); with none, the TFT, which can't be detected.
  enum Kind : uint8_t { KIND_OLED = 0, KIND_LCD = 1, KIND_TFT = 2, KIND_AUTO = 255 };
  // Both LCD sizes look identical on the I2C bus, so the size is a setting (remembered, and
  // also used when an LCD is picked automatically).
  enum LcdSize : uint8_t { LCD_1602 = 0, LCD_2004 = 1 };

  struct Settings {
    Kind kind = KIND_AUTO;      // what's configured; see active() for what's in use
    uint8_t lcdAddr = 0x27;     // PCF8574 backpack address
    LcdSize lcdSize = LCD_1602;
    bool flip = false;          // mounted upside down: rotate 180 degrees (OLED, TFT)
    uint8_t bright = 200;
    uint32_t accent = 0x5EEAD4; // TFT accent colour, 0xRRGGBB (title, happy eyes, highlights)
  };

  void begin();
  const Settings& settings();
  bool oledDetected();          // did the OLED answer at boot?
  uint8_t lcdDetected();        // address an LCD backpack answered at during boot, or 0
  Kind active();                // the screen in use (KIND_AUTO resolved)
  bool isLcd();
  bool isTft();
  const char* kindName();       // "oled" / "lcd1602" / "lcd2004" / "tft" (in use)
  U8G2& oled();
  // One human-readable line about what DANTE found and uses, for the USB log.
  void describe(char* out, size_t n);
  // Probes every I2C address right now: "I2C scan now: bus 1 (SDA=8 SCL=9): 0x27 (LCD)".
  void scanNow(char* out, size_t n);

  // Applies the display-related fields of a `cfg` message. Returns true if anything changed.
  bool applyConfig(JsonDocument& msg);
}
