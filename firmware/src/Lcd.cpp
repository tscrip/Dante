#include "Lcd.h"
#include <Wire.h>

namespace {
// PCF8574 pin mapping used by practically every 1602/2004 backpack.
const uint8_t RS = 0x01, EN = 0x04, BL = 0x08;
const uint8_t ROW_OFFSET[4] = { 0x00, 0x40, 0x14, 0x54 };

uint8_t addr = LCD_DEFAULT_ADDR;
bool running = false;
bool light = true, lightSent = false;

uint8_t want[LCD_ROWS][LCD_COLS], shown[LCD_ROWS][LCD_COLS];
uint8_t glyphWant[8][8], glyphShown[8][8];
bool glyphValid[8];

void expander(uint8_t v) {
  Wire.beginTransmission(addr);
  Wire.write(v | (light ? BL : 0));
  Wire.endTransmission();
}

// One 4-bit transfer with an enable pulse (init sequence only).
void nibble(uint8_t highNibble) {
  uint8_t bl = light ? BL : 0;
  Wire.beginTransmission(addr);
  Wire.write(highNibble | bl | EN);
  Wire.write(highNibble | bl);
  Wire.endTransmission();
  delayMicroseconds(50);
}

// A whole byte as two nibbles in one I2C transaction (~100 us at 400 kHz, longer than the
// controller's 37 us execution time, so no extra waiting is needed).
void send(uint8_t value, uint8_t mode) {
  uint8_t bl = light ? BL : 0;
  uint8_t hi = value & 0xF0, lo = (uint8_t)(value << 4);
  Wire.beginTransmission(addr);
  Wire.write(hi | mode | bl | EN);
  Wire.write(hi | mode | bl);
  Wire.write(lo | mode | bl | EN);
  Wire.write(lo | mode | bl);
  Wire.endTransmission();
}

inline void command(uint8_t c) { send(c, 0); }
inline void data(uint8_t d) { send(d, RS); }
} // namespace

namespace Lcd {

bool probe(uint8_t a) {
  Wire.beginTransmission(a);
  return Wire.endTransmission() == 0;
}

void begin(uint8_t a) {
  addr = a;
  light = true;
  expander(0);
  delay(50);
  // Datasheet "initialisation by instruction" into 4-bit mode.
  nibble(0x30); delayMicroseconds(4500);
  nibble(0x30); delayMicroseconds(4500);
  nibble(0x30); delayMicroseconds(150);
  nibble(0x20);
  command(0x28);              // 4-bit, 2 lines, 5x8 font
  command(0x0C);              // display on, no cursor, no blink
  command(0x01); delay(2);    // clear
  command(0x06);              // entry mode: increment, no shift
  memset(shown, ' ', sizeof(shown));
  memset(want, ' ', sizeof(want));
  memset(glyphValid, 0, sizeof(glyphValid));
  lightSent = true;
  running = true;
}

void end() {
  if (!running) return;
  command(0x01); delay(2);
  light = false;
  expander(0);
  running = false;
}

bool active() { return running; }

void clear() { memset(want, ' ', sizeof(want)); }

void put(int col, int row, uint8_t ch) {
  if (col < 0 || col >= LCD_COLS || row < 0 || row >= LCD_ROWS) return;
  want[row][col] = ch;
}

void print(int col, int row, const char* s, int maxLen) {
  if (!s) return;
  if (maxLen < 0) maxLen = LCD_COLS - col;
  for (int i = 0; s[i] && i < maxLen; i++) put(col + i, row, (uint8_t)s[i]);
}

void glyph(uint8_t slot, const uint8_t rows[8]) {
  if (slot > 7) return;
  for (int i = 0; i < 8; i++) glyphWant[slot][i] = rows[i] & 0x1F;
}

void backlight(bool on) { light = on; }

void flush() {
  if (!running) return;
  if (lightSent != light) { expander(0); lightSent = light; }

  // Custom glyphs first: cells already showing a changed glyph update by themselves.
  for (uint8_t g = 0; g < 8; g++) {
    if (glyphValid[g] && !memcmp(glyphWant[g], glyphShown[g], 8)) continue;
    command(0x40 | (g << 3));
    for (int i = 0; i < 8; i++) data(glyphWant[g][i]);
    memcpy(glyphShown[g], glyphWant[g], 8);
    glyphValid[g] = true;
  }

  // Then the changed characters, as runs so the address auto-increments.
  for (int r = 0; r < LCD_ROWS; r++) {
    int c = 0;
    while (c < LCD_COLS) {
      if (want[r][c] == shown[r][c]) { c++; continue; }
      command(0x80 | (ROW_OFFSET[r] + c));
      while (c < LCD_COLS && want[r][c] != shown[r][c]) {
        data(want[r][c]);
        shown[r][c] = want[r][c];
        c++;
      }
    }
  }
}

uint8_t cell(int col, int row) { return want[row][col]; }
const uint8_t* glyphRows(uint8_t slot) { return glyphWant[slot & 7]; }
bool backlightOn() { return light; }

} // namespace Lcd
