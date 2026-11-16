#include "Lcd.h"
#include <Wire.h>

namespace {
// PCF8574 pin mapping used by practically every 1602/2004 backpack.
const uint8_t RS = 0x01, EN = 0x04, BL = 0x08;

// The LCD on the I2C bus.
struct Panel {
  explicit Panel(TwoWire* b) : bus(b) {}
  TwoWire* bus;
  uint8_t addr = LCD_DEFAULT_ADDR;
  uint8_t nCols = 16, nRows = 2;
  bool running = false;
  bool light = true, lightSent = false;
  uint8_t want[LCD_MAX_ROWS][LCD_MAX_COLS] = {}, shown[LCD_MAX_ROWS][LCD_MAX_COLS] = {};
  uint8_t glyphWant[8][8] = {}, glyphShown[8][8] = {};   // every controller has its own 8 custom glyphs
  bool glyphValid[8] = {};
};

Panel panel(&Wire);
Panel* const P = &panel;

// DDRAM address of each row's first cell. Rows 3 and 4 continue rows 1 and 2, so their
// offset depends on the width (0x14/0x54 on a 20x4, 0x10/0x50 on a 16x4).
uint8_t rowOffset(int r) { return (uint8_t)((r & 1 ? 0x40 : 0x00) + (r >= 2 ? P->nCols : 0)); }

void expander(uint8_t v) {
  P->bus->beginTransmission(P->addr);
  P->bus->write(v | (P->light ? BL : 0));
  P->bus->endTransmission();
}

// One 4-bit transfer with an enable pulse (init sequence only).
void nibble(uint8_t highNibble) {
  uint8_t bl = P->light ? BL : 0;
  P->bus->beginTransmission(P->addr);
  P->bus->write(highNibble | bl | EN);
  P->bus->write(highNibble | bl);
  P->bus->endTransmission();
  delayMicroseconds(50);
}

// A whole byte as two nibbles in one I2C transaction (~100 us at 400 kHz, longer than the
// controller's 37 us execution time, so no extra waiting is needed).
void send(uint8_t value, uint8_t mode) {
  uint8_t bl = P->light ? BL : 0;
  uint8_t hi = value & 0xF0, lo = (uint8_t)(value << 4);
  P->bus->beginTransmission(P->addr);
  P->bus->write(hi | mode | bl | EN);
  P->bus->write(hi | mode | bl);
  P->bus->write(lo | mode | bl | EN);
  P->bus->write(lo | mode | bl);
  P->bus->endTransmission();
}

inline void command(uint8_t c) { send(c, 0); }
inline void data(uint8_t d) { send(d, RS); }
} // namespace

namespace Lcd {

bool probe(uint8_t a) {
  P->bus->beginTransmission(a);
  return P->bus->endTransmission() == 0;
}

void begin(uint8_t a, uint8_t c, uint8_t r) {
  P->addr = a;
  P->nCols = c > LCD_MAX_COLS ? LCD_MAX_COLS : c;
  P->nRows = r > LCD_MAX_ROWS ? LCD_MAX_ROWS : r;
  P->light = true;
  expander(0);
  delay(50);
  // Datasheet "initialisation by instruction" into 4-bit mode.
  nibble(0x30); delayMicroseconds(4500);
  nibble(0x30); delayMicroseconds(4500);
  nibble(0x30); delayMicroseconds(150);
  nibble(0x20);
  command(0x28);              // 4-bit, "2 lines" (also what 4-line modules use), 5x8 font
  command(0x0C);              // display on, no cursor, no blink
  command(0x01); delay(2);    // clear
  command(0x06);              // entry mode: increment, no shift
  memset(P->shown, ' ', sizeof(P->shown));
  memset(P->want, ' ', sizeof(P->want));
  memset(P->glyphValid, 0, sizeof(P->glyphValid));
  P->lightSent = true;
  P->running = true;
}

void end() {
  if (!P->running) return;
  command(0x01); delay(2);
  P->light = false;
  expander(0);
  P->running = false;
}

bool active() { return P->running; }
int cols() { return P->nCols; }
int rows() { return P->nRows; }

void clear() { memset(P->want, ' ', sizeof(P->want)); }

void put(int col, int row, uint8_t ch) {
  if (col < 0 || col >= P->nCols || row < 0 || row >= P->nRows) return;
  P->want[row][col] = ch;
}

void print(int col, int row, const char* s, int maxLen) {
  if (!s) return;
  if (maxLen < 0) maxLen = P->nCols - col;
  for (int i = 0; s[i] && i < maxLen; i++) put(col + i, row, (uint8_t)s[i]);
}

void glyph(uint8_t slot, const uint8_t rows[8]) {
  if (slot > 7) return;
  for (int i = 0; i < 8; i++) P->glyphWant[slot][i] = rows[i] & 0x1F;
}

void backlight(bool on) { P->light = on; }

void flush() {
  if (!P->running) return;
  if (P->lightSent != P->light) { expander(0); P->lightSent = P->light; }

  // Custom glyphs first: cells already showing a changed glyph update by themselves.
  for (uint8_t g = 0; g < 8; g++) {
    if (P->glyphValid[g] && !memcmp(P->glyphWant[g], P->glyphShown[g], 8)) continue;
    command(0x40 | (g << 3));
    for (int i = 0; i < 8; i++) data(P->glyphWant[g][i]);
    memcpy(P->glyphShown[g], P->glyphWant[g], 8);
    P->glyphValid[g] = true;
  }

  // Then the changed characters, as runs so the address auto-increments.
  for (int r = 0; r < P->nRows; r++) {
    int c = 0;
    while (c < P->nCols) {
      if (P->want[r][c] == P->shown[r][c]) { c++; continue; }
      command(0x80 | (rowOffset(r) + c));
      while (c < P->nCols && P->want[r][c] != P->shown[r][c]) {
        data(P->want[r][c]);
        P->shown[r][c] = P->want[r][c];
        c++;
      }
    }
  }
}

uint8_t cell(int col, int row) { return P->want[row][col]; }
const uint8_t* glyphRows(uint8_t slot) { return P->glyphWant[slot & 7]; }
bool backlightOn() { return P->light; }

} // namespace Lcd
