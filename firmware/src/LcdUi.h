#pragma once
#include <Arduino.h>
#include "Apps.h"

// How DANTE looks on a character LCD (16x2 or 20x4; the size comes from Lcd::cols()/rows()).
// Each call fills the LCD shadow buffer; the caller flushes.
namespace LcdUi {
  void face(uint32_t now);
  void app(const App& a, int page, int pages, uint32_t now);
  void toast(const char* title, const char* text, uint32_t start, uint32_t now);
  void takeover(const char* title, const char* text, uint8_t lvl, uint32_t since, uint32_t now, bool light);
}
