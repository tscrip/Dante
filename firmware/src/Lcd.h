#pragma once
#include <Arduino.h>
#include "config.h"

// HD44780 16x2 character LCD behind a PCF8574 I2C backpack ("1602 I2C").
// Draw into a shadow buffer with put/print/glyph, then flush() sends only what changed.
namespace Lcd {
  bool probe(uint8_t addr);          // is something answering at this address?
  void begin(uint8_t addr);          // (re)initialise the controller (~60 ms)
  void end();                        // blank + backlight off
  bool active();

  void clear();                      // shadow buffer -> spaces
  void put(int col, int row, uint8_t ch);
  // Prints up to maxLen chars (default: to the end of the row).
  void print(int col, int row, const char* s, int maxLen = -1);
  void glyph(uint8_t slot, const uint8_t rows[8]);   // custom char 0..7, 5 bits per row (bit 4 = left)
  void backlight(bool on);
  void flush();

  // For the host simulator / debugging.
  uint8_t cell(int col, int row);
  const uint8_t* glyphRows(uint8_t slot);
  bool backlightOn();
}
