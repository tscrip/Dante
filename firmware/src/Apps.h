#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>
#include <U8g2lib.h>
#include "config.h"

enum Visual : uint8_t { VIS_GAUGE = 0, VIS_BAR, VIS_COUNTER, VIS_TEXT, VIS_SPARK, VIS_STATUS };

struct App {
  char id[ID_LEN];
  char name[NAME_LEN];
  char unit[UNIT_LEN];
  Visual vis;
  float min, max;

  bool hasValue;
  float value;       // latest value from the PC
  float shown;       // tweened value used for drawing
  float prevValue;
  char txt[TXT_LEN];
  uint8_t lvl;       // 0 ok, 1 warn, 2 crit
  uint32_t changedAt;

  float hist[HIST_LEN];
  uint8_t histN;     // number of samples stored
  uint8_t histHead;  // next write position
};

namespace Apps {
  void setList(JsonArrayConst arr);   // replaces list, keeps values of matching ids
  int count();
  App* get(int i);
  App* find(const char* id);
  // Applies a `val` message. Returns the app (or nullptr if unknown id).
  App* applyValue(JsonDocument& msg);

  void update(uint32_t now);
  // Draws app `index` full-screen. `page`/`pages` drive the page indicator dots.
  void draw(U8G2& g, int index, int ox, int page, int pages, uint32_t now);
}

// Shared helper: draw `text` scrolling inside [x, x+w) at baseline y with the current font.
void drawMarquee(U8G2& g, const char* text, int x, int y, int w, uint32_t now, uint32_t startMs, int clipTop, int clipBottom);
