#pragma once
#include <Arduino.h>
#include "Gfx.h"

enum Mood : uint8_t {
  MOOD_HAPPY = 0,
  MOOD_CURIOUS,
  MOOD_ANTSY,
  MOOD_GRUMPY,
  MOOD_MELTDOWN,
  MOOD_SLEEPY,      // device-local: used when no PC is around
};

// DANTE's look: eye style, habits and accessories, chosen in the PC app ("personality 1-5").
enum Look : uint8_t {
  LOOK_CLASSIC = 0,   // the original
  LOOK_GENTLEMAN,     // curly mustache, relaxed half-lidded eyes, slow blinks, unhurried glances
  LOOK_NERD,          // round eyes behind glasses, often looks up while "thinking"
  LOOK_UNCLE,         // grumpy uncle: heavy eyebrows (a built-in frown) and a bristly mustache
  LOOK_PARTY,         // big sparkly eyes, a party hat, happy hops and confetti
  LOOK_COUNT
};

enum Gag : uint8_t {
  GAG_NONE = 0,
  GAG_LOVE,
  GAG_DIZZY,
  GAG_WINK,
  GAG_LAUGH,
  GAG_SURPRISED,
  GAG_BOOP,
  GAG_COUNT,
  GAG_MAGIC,        // not a random gag: played when the MAGIC button is pressed
};

// Decorations around the eyes ("z", sweat, "!!", ...).
enum FaceDecor : uint16_t {
  DEC_Z      = 1 << 0,
  DEC_SWEAT  = 1 << 1,
  DEC_BANG   = 1 << 2,
  DEC_HA     = 1 << 3,
  DEC_HEARTS = 1 << 4,
  DEC_BOOP   = 1 << 5,
  DEC_QUEST  = 1 << 6,
  DEC_WOW    = 1 << 7,
  DEC_STARS  = 1 << 8,
};

// What the face is doing right now, for renderers that draw it differently.
struct FaceInfo {
  Mood mood;          // effective mood (sleepy when alone)
  Gag gag;            // gag playing right now, GAG_NONE otherwise
  uint16_t decor;     // FaceDecor flags
  int pending;
  bool alone;
  float lookX, lookY; // gaze incl. shakes/hops; x roughly -24..24, y -5..5
};

// Expressions for the small faces used in toasts / takeovers.
enum MiniExpr : uint8_t { MINI_LOOK = 0, MINI_SURPRISED, MINI_HAPPY, MINI_WORRIED, MINI_ANGRY };

namespace Face {
  void begin();
  void setMood(Mood m);
  Mood mood();
  Mood moodFromString(const char* s);
  void setPending(int n);
  void setLook(uint8_t look);       // a Look; out-of-range values mean LOOK_CLASSIC
  void setAlone(bool alone);
  // Calm: the face stays put (no hops, shakes or jitter; quick glances that settle at once), so on boards
  // that draw in strips only the parts that really change (blinks, lids) have to be redrawn and sent.
  void setCalm(bool calm);
  bool calm();

  void playGag(Gag g, uint16_t durationMs = 2400);
  void playRandomGag();
  void react();                    // quick surprised double-take

  void update(uint32_t now);
  // Full-screen face, shifted by ox pixels. chrome = also the pending badge and the "no PC" sign
  // (the TFT shows those in its status bar instead).
  void draw(Gfx& g, int ox, bool chrome = true);

  FaceInfo info();

  // Small pair of eyes centred at (cx, cy); `scale` 1.0 == full-size eyes.
  void drawMini(Gfx& g, int cx, int cy, float scale, MiniExpr expr, uint32_t now);
}
