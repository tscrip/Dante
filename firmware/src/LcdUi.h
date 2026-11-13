#pragma once
#include <Arduino.h>
#include "Apps.h"

// How DANTE looks on a character LCD (16x2 or 20x4; the size comes from Lcd::cols()/rows()).
// Each call fills the LCD shadow buffer; the caller flushes.
namespace LcdUi {
  void face(uint32_t now);
  // Two LCDs: one big eye (4x2 characters) on the selected LCD. eye 0 = left, 1 = right.
  // lookBias (in OLED pixels, like Face's gaze) turns it towards the other LCD.
  void faceSingle(int eye, float lookBias, uint32_t now);
  // Two LCDs, during a nudge: the eye LCD stares at the message and joins in the urgent flash.
  void takeoverEye(int eye, float lookBias, uint8_t lvl, uint32_t since, uint32_t now, bool light);
  void app(const App& a, int page, int pages, uint32_t now);
  void toast(const char* title, const char* text, uint32_t start, uint32_t now);
  void takeover(const char* title, const char* text, uint8_t lvl, uint32_t since, uint32_t now, bool light);
}
