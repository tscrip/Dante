#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>
#include "Gfx.h"
#include "config.h"

enum Visual : uint8_t { VIS_GAUGE = 0, VIS_BAR, VIS_COUNTER, VIS_TEXT, VIS_SPARK, VIS_STATUS, VIS_TIMER, VIS_SPARK2, VIS_MEDIA };

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

  // Dual sparkline (`spark2`): a second line, `v2` in `val`. Its history shares histN / histHead.
  char lbl1[5], lbl2[5];
  bool hasValue2;
  float value2;
  float hist2[HIST_LEN];

  // Countdowns (`val` with `tot`): `value` = seconds left when it arrived, ticking down locally while `timerRun`.
  bool hasTimer;
  bool timerRun;
  float timerTotal;  // seconds in the whole countdown (for the progress bar)
  uint32_t timerAt;  // millis() when `value` arrived

  // Media players (`media`): `txt` is the title; the artist, play state, mute, volume (-1 = unknown)
  // and the fingerprint of the album art to show (see Art.h).
  char sub[TXT_LEN];
  bool playing;
  bool muted;
  int8_t volume;
  char artHash[9];
};

namespace Apps {
  // Replaces the list, keeping the values of matching ids. `save`: also remember it in flash.
  void setList(JsonArrayConst arr, bool save = true);
  // At boot: the widget list from last time, so the screens and page dots show straight away.
  void restore();
  int count();
  App* get(int i);
  App* find(const char* id);
  // Applies a `val` message. Returns the app (or nullptr if unknown id).
  App* applyValue(JsonDocument& msg);
  // A media control was tapped: show its effect straight away, before the PC confirms it.
  void mediaPressed(App& a, const char* ctl);

  void update(uint32_t now);
  // Seconds left on a countdown app right now.
  float secondsLeft(const App& a, uint32_t now);
  // "24:59", or "2h05" for an hour or more.
  void formatClock(float seconds, char* out, size_t n);
  // Draws app `index` full-screen. `page`/`pages` drive the page indicator dots.
  void draw(Gfx& g, int index, int ox, int page, int pages, uint32_t now);
}

// Shared helper: draw `text` scrolling inside [x, x+w) at baseline y with the current font.
void drawMarquee(Gfx& g, const char* text, int x, int y, int w, uint32_t now, uint32_t startMs, int clipTop, int clipBottom);
