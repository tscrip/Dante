#pragma once
#include <Arduino.h>
#include <U8g2lib.h>

enum Mood : uint8_t {
  MOOD_HAPPY = 0,
  MOOD_CURIOUS,
  MOOD_ANTSY,
  MOOD_GRUMPY,
  MOOD_MELTDOWN,
  MOOD_SLEEPY,      // device-local: used when no PC is around
};

enum Gag : uint8_t {
  GAG_NONE = 0,
  GAG_LOVE,
  GAG_DIZZY,
  GAG_WINK,
  GAG_LAUGH,
  GAG_SURPRISED,
  GAG_BOOP,
  GAG_COUNT
};

// Expressions for the small faces used in toasts / takeovers.
enum MiniExpr : uint8_t { MINI_LOOK = 0, MINI_SURPRISED, MINI_HAPPY, MINI_WORRIED, MINI_ANGRY };

namespace Face {
  void begin();
  void setMood(Mood m);
  Mood mood();
  Mood moodFromString(const char* s);
  void setPending(int n);
  void setAlone(bool alone);

  void playGag(Gag g, uint16_t durationMs = 2400);
  void playRandomGag();
  void react();                    // quick surprised double-take

  void update(uint32_t now);
  void draw(U8G2& g, int ox);      // full-screen face, shifted by ox pixels

  // Small pair of eyes centred at (cx, cy); `scale` 1.0 == full-size eyes.
  void drawMini(U8G2& g, int cx, int cy, float scale, MiniExpr expr, uint32_t now);
}
